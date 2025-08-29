using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine; // optional
using Managers;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateAfter(typeof(MovementSystem))]
public partial struct CombatServerSystem : ISystem
{
    // --- Tunables --- 
    const float AttackRangeTolerance = 0.1f;  // small forgiveness at edges
    const float AimSwitchHysteresis = 0.10f;  // new target must be >=10% closer (distance) to switch
    const float POST_IMPACT_SLOW_EXTRA_SECONDS = 0.5f; // 0 = no extra post-impact slow

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        state.RequireForUpdate<FactionRelations>();
        state.RequireForUpdate<FactionCount>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false) return;

        float deltaTime = SystemAPI.Time.DeltaTime;
        EntityManager entityManager = state.EntityManager;

        // --- Faction singletons ---
        FactionRelations factionRelations = SystemAPI.GetSingleton<FactionRelations>();
        byte factionMax = SystemAPI.GetSingleton<FactionCount>().Value;

        // --- Component lookups (RO where possible) ---
        var localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var unitStatsLookup = SystemAPI.GetComponentLookup<UnitStats>(true);
        var combatStatsLookup = SystemAPI.GetComponentLookup<CombatStats>(true);
        var unitTargetsLookup = SystemAPI.GetComponentLookup<UnitTargets>(true);
        var attackerLookup = SystemAPI.GetComponentLookup<Attacker>(false);
        var healthStateLookup = SystemAPI.GetComponentLookup<HealthState>(true);
        var factionLookup = SystemAPI.GetComponentLookup<Faction>(true);
        var ghostOwnerLookup = SystemAPI.GetComponentLookup<GhostOwner>(true);
        var unitTagLookup = SystemAPI.GetComponentLookup<Unit>(true);
        var targetingSizeLookup = SystemAPI.GetComponentLookup<TargetingSize>(true);

        // --- Cache the working set once (Units with LocalTransform) ---
        var unitQuery = SystemAPI.QueryBuilder().WithAll<Unit, LocalTransform>().Build();
        NativeArray<Entity> unitEntities = unitQuery.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> unitTransforms = unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        int unitCount = unitEntities.Length;

        // --- Helpers (do NOT call SystemAPI here) ---
        bool IsAlive(Entity entity)
        {
            if (!entityManager.Exists(entity)) return false;
            if (healthStateLookup.HasComponent(entity))
                return healthStateLookup[entity].currentStage != HealthStage.Dead;
            return true;
        }

        bool AreHostile(Entity entityA, Entity entityB)
        {
            // Prefer faction mask if available
            if (factionLookup.HasComponent(entityA) && factionLookup.HasComponent(entityB))
            {
                byte factionA = factionLookup[entityA].FactionId;
                byte factionB = factionLookup[entityB].FactionId;
                return FactionUtility.AreHostile(factionA, factionB, factionRelations, factionMax);
            }
            // Fallback: different GhostOwner
            if (ghostOwnerLookup.HasComponent(entityA) && ghostOwnerLookup.HasComponent(entityB))
            {
                int ownerA = ghostOwnerLookup[entityA].NetworkId;
                int ownerB = ghostOwnerLookup[entityB].NetworkId;
                return (ownerA != int.MinValue) && (ownerB != int.MinValue) && (ownerA != ownerB);
            }
            return false;
        }

        bool IsEnemyUnit(Entity selfEntity, Entity otherEntity)
        {
            if (otherEntity == Entity.Null) return false;
            if (!entityManager.Exists(otherEntity)) return false;
            if (!IsAlive(otherEntity)) return false;

            // exclude buildings/props for now: must carry Unit tag
            if (!unitTagLookup.HasComponent(otherEntity)) return false;

            // need at least one identity on both for hostility check
            if (!factionLookup.HasComponent(selfEntity) && !ghostOwnerLookup.HasComponent(selfEntity)) return false;
            if (!factionLookup.HasComponent(otherEntity) && !ghostOwnerLookup.HasComponent(otherEntity)) return false;

            return AreHostile(selfEntity, otherEntity);
        }
        
        float RadiusOrZero(Entity entity)
        {
            return targetingSizeLookup.HasComponent(entity) ? targetingSizeLookup[entity].radius : 0f;
        }


        // ------------------------------------------------------------
        // 1) IMPACT (post-move, inside open swing window)
        // ------------------------------------------------------------
        for (int impactIndex = 0; impactIndex < unitCount; impactIndex++)
        {
            Entity attackerEntity = unitEntities[impactIndex];

            if (!attackerLookup.HasComponent(attackerEntity) || !combatStatsLookup.HasComponent(attackerEntity))
                continue;

            if (healthStateLookup.HasComponent(attackerEntity) &&
                healthStateLookup[attackerEntity].currentStage == HealthStage.Dead)
                continue;

            ref Attacker attacker = ref attackerLookup.GetRefRW(attackerEntity).ValueRW;
            CombatStats combat = combatStatsLookup[attackerEntity];

            bool impactWindowOpen =
                attacker.attackDurationTimeLeft > 0f &&
                !attacker.impactDone &&
                attacker.impactDelayTimeLeft <= 0f &&
                math.isfinite(attacker.aimRotation);

            if (!impactWindowOpen)
                continue;

            float3 origin = unitTransforms[impactIndex].Position;
            float3 aimDir = math.normalizesafe(
                                      new float3(math.sin(attacker.aimRotation), 0f, math.cos(attacker.aimRotation)),
                                      new float3(0, 0, 1));
            float rangeBase = math.max(0f, combat.attackRange + AttackRangeTolerance); // base; per-target add radii
            float selfRadius = RadiusOrZero(attackerEntity);
            float halfAngle = math.radians(math.clamp(combat.attackConeDeg, 1f, 179f) * 0.5f);
            float cosHalfAng = math.cos(halfAngle);

            var candidateEntities = new NativeList<Entity>(Allocator.Temp);
            var candidateRanges = new NativeList<float>(Allocator.Temp);

            // Gather candidates inside cone (surface model for range)
            for (int candidateIndex = 0; candidateIndex < unitCount; candidateIndex++)
            {
                Entity targetEntity = unitEntities[candidateIndex];
                if (targetEntity == attackerEntity) continue;
                if (!IsAlive(targetEntity)) continue;
                if (!AreHostile(attackerEntity, targetEntity)) continue;

                float3 to = unitTransforms[candidateIndex].Position - origin; to.y = 0f;
                float dist = math.length(to);
                if (dist <= 1e-6f) continue;

                float targetRadius = RadiusOrZero(targetEntity);
                float effectiveRange = rangeBase + selfRadius + targetRadius; // <-- both radii
                if (dist > effectiveRange) continue;

                float3 dir = to / math.max(1e-6f, dist);
                float dot = math.dot(aimDir, dir);
                if (dot + 1e-5f < cosHalfAng) continue;

                candidateEntities.Add(targetEntity);
                candidateRanges.Add(dist); // keep sorting by center distance (fine)
            }

            // Sort nearest-first (small N insertion sort)
            for (int sortIndex = 1; sortIndex < candidateRanges.Length; sortIndex++)
            {
                float keyRange = candidateRanges[sortIndex];
                Entity keyEntity = candidateEntities[sortIndex];
                int inner = sortIndex - 1;

                while (inner >= 0 && candidateRanges[inner] > keyRange)
                {
                    candidateRanges[inner + 1] = candidateRanges[inner];
                    candidateEntities[inner + 1] = candidateEntities[inner];
                    inner--;
                }
                candidateRanges[inner + 1] = keyRange;
                candidateEntities[inner + 1] = keyEntity;
            }

            int maxHits = combat.maxEntitiesHit <= 0 ? int.MaxValue : combat.maxEntitiesHit;
            int hitsApplied = 0;
            float hitchanceRaw = combat.hitchance;
            float hitchance01 = math.saturate(hitchanceRaw <= 1f ? hitchanceRaw : hitchanceRaw * 0.01f);

            for (int hitIndex = 0; hitIndex < candidateEntities.Length && hitsApplied < maxHits; hitIndex++)
            {
                Entity victimEntity = candidateEntities[hitIndex];
                if (!entityManager.Exists(victimEntity)) continue;

                // Stateless Bernoulli using deterministic hash
                uint hash = math.hash(new uint4(
                    (uint)attacker.attackTick,
                    (uint)attackerEntity.Index,
                    (uint)attackerEntity.Version,
                    (uint)victimEntity.Index ^ (uint)victimEntity.Version));

                uint threshold = (uint)math.round(hitchance01 * 16777216f); // 2^24
                bool didHit = (hash & 0x00FFFFFFu) < threshold;

                if (didHit && healthStateLookup.HasComponent(victimEntity))
                {
                    HealthState victimHealth = healthStateLookup[victimEntity];
                    if (victimHealth.currentStage != HealthStage.Dead)
                    {
                        victimHealth.healthChange -= 1; // TODO: scale by damage when you add it
                        entityManager.SetComponentData(victimEntity, victimHealth);
                        hitsApplied++;
                    }
                }
            }

            candidateEntities.Dispose();
            candidateRanges.Dispose();

            attacker.impactDone = true; // seal this impact window
        }

        // ------------------------------------------------------------
        // 2) AIM ACQUISITION (prefer targetEntity if hittable, else nearest; hysteresis)
        // ------------------------------------------------------------
        for (int aimIndex = 0; aimIndex < unitCount; aimIndex++)
        {
            Entity selfEntity = unitEntities[aimIndex];

            if (!attackerLookup.HasComponent(selfEntity) ||
                !unitStatsLookup.HasComponent(selfEntity) ||
                !combatStatsLookup.HasComponent(selfEntity))
                continue;

            float3 selfPosition = unitTransforms[aimIndex].Position;

            // keep your +RangeTolerance here (aim side)
            float attackRange = math.max(0f, combatStatsLookup[selfEntity].attackRange + AttackRangeTolerance);
            float selfRadius = RadiusOrZero(selfEntity);

            ref Attacker attacker = ref attackerLookup.GetRefRW(selfEntity).ValueRW;

            // 1) Prefer targetEntity if it's an enemy unit AND within (attackRange + self + target)
            Entity targetEntityPref = unitTargetsLookup.HasComponent(selfEntity)
                ? unitTargetsLookup[selfEntity].targetEntity
                : Entity.Null;

            bool aimedFromPreferred = false;
            if (IsEnemyUnit(selfEntity, targetEntityPref))
            {
                float3 preferredTargetPos = localTransformLookup.HasComponent(targetEntityPref)
                    ? localTransformLookup[targetEntityPref].Position
                    : attacker.aimPosition; // fallback

                float3 toPreferred = preferredTargetPos - selfPosition; toPreferred.y = 0f;
                float d2Preferred = math.lengthsq(toPreferred);

                float targetRadius = RadiusOrZero(targetEntityPref);
                float reachPref = attackRange + selfRadius + targetRadius;
                float reachPref2 = reachPref * reachPref;

                if (d2Preferred > 1e-10f && d2Preferred <= reachPref2)
                {
                    attacker.aimEntity = targetEntityPref;
                    attacker.aimPosition = preferredTargetPos;
                    attacker.aimRotation = math.atan2(toPreferred.x, toPreferred.z);
                    aimedFromPreferred = true;
                }
            }

            if (aimedFromPreferred) continue;

            // 2) Else choose nearest hostile unit **within ATTACK surface range** (per-candidate)
            int bestCandidateIndex = -1;
            float bestCandidateD2 = float.MaxValue;

            for (int candidateIndex = 0; candidateIndex < unitCount; candidateIndex++)
            {
                Entity candidateEntity = unitEntities[candidateIndex];
                if (candidateEntity == selfEntity) continue;
                if (!IsEnemyUnit(selfEntity, candidateEntity)) continue;

                float3 toCandidate = unitTransforms[candidateIndex].Position - selfPosition; toCandidate.y = 0f;
                float d2Candidate = math.lengthsq(toCandidate);

                float candRadius = RadiusOrZero(candidateEntity);
                float reachCand = attackRange + selfRadius + candRadius;
                float reachCand2 = reachCand * reachCand;

                if (d2Candidate > reachCand2) continue;

                if (d2Candidate < bestCandidateD2)
                {
                    bestCandidateD2 = d2Candidate;
                    bestCandidateIndex = candidateIndex;
                }
            }

            // Hysteresis: keep current aim unless the new best is sufficiently closer (and in-range)
            Entity currentAimEntity = attacker.aimEntity;
            bool currentValidInRange = IsEnemyUnit(selfEntity, currentAimEntity) &&
                                         localTransformLookup.HasComponent(currentAimEntity);
            float currentAimD2 = float.MaxValue;

            if (currentValidInRange)
            {
                float3 toCurrent = localTransformLookup[currentAimEntity].Position - selfPosition; toCurrent.y = 0f;
                currentAimD2 = math.lengthsq(toCurrent);

                float curRadius = RadiusOrZero(currentAimEntity);
                float reachCur = attackRange + selfRadius + curRadius;
                float reachCur2 = reachCur * reachCur;

                currentValidInRange = currentAimD2 <= reachCur2 && currentAimD2 > 1e-10f;
            }
            else
            {
                currentValidInRange = false;
            }

            if (bestCandidateIndex >= 0)
            {
                bool shouldSwitch = true;
                if (currentValidInRange)
                {
                    float keepThresholdSq = (1f - AimSwitchHysteresis);
                    keepThresholdSq *= keepThresholdSq; // squared compare
                    shouldSwitch = !(bestCandidateD2 <= currentAimD2 * keepThresholdSq);
                }

                if (shouldSwitch)
                {
                    float3 bestPos = unitTransforms[bestCandidateIndex].Position;
                    float3 toBest = bestPos - selfPosition; toBest.y = 0f;

                    attacker.aimEntity = unitEntities[bestCandidateIndex];
                    attacker.aimPosition = bestPos;
                    attacker.aimRotation = math.atan2(toBest.x, toBest.z);
                }
                else
                {
                    // Keep current aim; refresh
                    float3 curPos = localTransformLookup[currentAimEntity].Position;
                    float3 toCur = curPos - selfPosition; toCur.y = 0f;

                    attacker.aimPosition = curPos;
                    attacker.aimRotation = math.atan2(toCur.x, toCur.z);
                }
            }
            else
            {
                // 3) No hostile in attack range → clear aim
                attacker.aimEntity = Entity.Null;
                attacker.aimRotation = float.NaN;
            }
        }

        // ------------------------------------------------------------
        // 3) SWING START (open attack window)
        // ------------------------------------------------------------
        for (int startIndex = 0; startIndex < unitCount; startIndex++)
        {
            Entity attackerEntity = unitEntities[startIndex];

            if (!attackerLookup.HasComponent(attackerEntity) || !combatStatsLookup.HasComponent(attackerEntity))
                continue;

            ref Attacker attacker = ref attackerLookup.GetRefRW(attackerEntity).ValueRW;
            CombatStats  combat   = combatStatsLookup[attackerEntity];

            if (attacker.attackDurationTimeLeft > 0f) continue;
            if (attacker.attackCooldownLeft     > 0f) continue;
            if (!math.isfinite(attacker.aimRotation)) continue;

            float3 origin     = unitTransforms[startIndex].Position;
            float3 aimDir     = math.normalizesafe(
                                    new float3(math.sin(attacker.aimRotation), 0f, math.cos(attacker.aimRotation)),
                                    new float3(0, 0, 1));
            float  rangeBase  = math.max(0f, combat.attackRange + AttackRangeTolerance); // base (will add radii per target)
            float  selfRadius = RadiusOrZero(attackerEntity);

            float  halfAngle  = math.radians(math.clamp(combat.attackConeDeg, 1f, 179f) * 0.5f);
            float  cosHalfAng = math.cos(halfAngle);

            bool anyInRange = false;
            for (int candidateIndex = 0; candidateIndex < unitCount; candidateIndex++)
            {
                Entity targetEntity = unitEntities[candidateIndex];
                if (targetEntity == attackerEntity) continue;
                if (!IsAlive(targetEntity)) continue;
                if (!AreHostile(attackerEntity, targetEntity)) continue;

                float3 to   = unitTransforms[candidateIndex].Position - origin; to.y = 0f;
                float  dist = math.length(to);

                float targetRadius    = RadiusOrZero(targetEntity);
                float effectiveRange  = rangeBase + selfRadius + targetRadius;   // surface model

                if (dist > effectiveRange || dist <= 1e-6f) continue;

                float3 dir = to / math.max(1e-6f, dist);
                if (math.dot(aimDir, dir) + 1e-5f < cosHalfAng) continue;

                anyInRange = true;
                break;
            }

            if (!anyInRange) continue;

            float baseDuration  = math.max(0.01f, combat.attackDuration);
            float totalDuration = baseDuration + math.max(0f, POST_IMPACT_SLOW_EXTRA_SECONDS);

            attacker.attackDurationTimeLeft = totalDuration;
            attacker.impactDelayTimeLeft    = math.clamp(combat.impactDelay, 0f, totalDuration);
            attacker.impactDone             = false;
            attacker.attackTick++;
            Debug.Log(attacker.attackTick);
        }

        // ------------------------------------------------------------
        // 4) TIMERS & CLEANUP
        // ------------------------------------------------------------
        for (int timerIndex = 0; timerIndex < unitCount; timerIndex++)
        {
            Entity entity = unitEntities[timerIndex];
            if (!attackerLookup.HasComponent(entity) || !combatStatsLookup.HasComponent(entity))
                continue;

            ref Attacker attacker = ref attackerLookup.GetRefRW(entity).ValueRW;
            CombatStats combat = combatStatsLookup[entity];

            // tick cooldown
            attacker.attackCooldownLeft = math.max(0f, attacker.attackCooldownLeft - deltaTime);

            if (attacker.attackDurationTimeLeft > 0f)
            {
                attacker.attackDurationTimeLeft = math.max(0f, attacker.attackDurationTimeLeft - deltaTime);
                attacker.impactDelayTimeLeft = math.max(0f, attacker.impactDelayTimeLeft - deltaTime);

                // window just ended -> set cooldown and finalize
                if (attacker.attackDurationTimeLeft <= 0f)
                {
                    float attacksPerSecond = math.max(0.01f, combat.attacksPerSecond);
                    attacker.attackCooldownLeft = math.max(attacker.attackCooldownLeft, 1f / attacksPerSecond);

                    if (!attacker.impactDone)
                        attacker.impactDone = true; // whiff
                }
            }
        }

        // Disposals
        unitTransforms.Dispose();
        unitEntities.Dispose();
    }
}