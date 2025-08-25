using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ApplyMoveRequestsServerSystem))]
public partial struct CombatServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        state.RequireForUpdate<FactionRelations>(); // masks required
        state.RequireForUpdate<FactionCount>();     // bounds for mask indices
        // CombatRules (FriendlyFireEnabled) is optional
    }

    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        var em = state.EntityManager;

        // Mask singletons (required in OnCreate)
        var rel = SystemAPI.GetSingleton<FactionRelations>();
        byte factionCount = SystemAPI.GetSingleton<FactionCount>().Value;
        bool friendlyFire = SystemAPI.HasSingleton<CombatRules>() && SystemAPI.GetSingleton<CombatRules>().FriendlyFireEnabled;

        foreach (var (attackerLT, attackerRW, statsRO, unitTargetsRW, attackerEntity)
                 in SystemAPI.Query<
                        RefRO<LocalTransform>,
                        RefRW<Attacker>,
                        RefRO<CombatStats>,
                        RefRW<UnitTargets>>()
                     .WithAll<Unit>()
                     .WithEntityAccess())
        {
            // 1) Skip dead attackers
            if (SystemAPI.HasComponent<HealthState>(attackerEntity))
            {
                var h = SystemAPI.GetComponent<HealthState>(attackerEntity);
                if (h.currentStage == HealthStage.Dead) continue;
            }

            // 2) Cooldown tick
            attackerRW.ValueRW.cooldownLeft = math.max(0f, attackerRW.ValueRO.cooldownLeft - dt);

            // 3) Read the current explicit target (no perception here)
            Entity target = unitTargetsRW.ValueRO.targetEntity;
            if (target == Entity.Null || !em.Exists(target)) continue;

            // Must be a Unit (you can relax this later if you want buildings, etc.)
            if (!SystemAPI.HasComponent<Unit>(target)) continue;

            // 4) Skip dead targets
            if (SystemAPI.HasComponent<HealthState>(target))
            {
                var th = SystemAPI.GetComponent<HealthState>(target);
                if (th.currentStage == HealthStage.Dead) continue;
            }

            // 5) Mask-only hostility gate (no owner fallback)
            byte atkFaction = FactionUtility.EffectiveFaction(attackerEntity, em);
            byte tgtFaction = FactionUtility.EffectiveFaction(target, em);

            bool hostile = FactionUtility.AreHostile(atkFaction, tgtFaction, rel, factionCount);
            bool allowed = friendlyFire || hostile;
            if (!allowed) continue;

            // 6) In-range check (planar)
            float3 aPos = attackerLT.ValueRO.Position;
            float3 tPos = SystemAPI.GetComponent<LocalTransform>(target).Position;

            float3 diff = tPos - aPos; diff.y = 0f;
            float distSq = math.lengthsq(diff);
            float range  = math.max(0f, statsRO.ValueRO.attackRange);
            float rangeSq = range * range;

            if (distSq > rangeSq) continue;

            // 7) Swing if off cooldown
            if (attackerRW.ValueRO.cooldownLeft <= 0f)
            {
                // Apply 1 damage
                if (SystemAPI.HasComponent<HealthState>(target))
                {
                    var th = SystemAPI.GetComponent<HealthState>(target);
                    if (th.currentStage != HealthStage.Dead)
                    {
                        th.healthChange -= 1;
                        SystemAPI.SetComponent(target, th);
                    }
                }

                // Notify animation (optional but nice)
                if (SystemAPI.HasComponent<AttackAnimationState>(attackerEntity))
                {
                    var anim = SystemAPI.GetComponent<AttackAnimationState>(attackerEntity);
                    anim.attackTick++;
                    SystemAPI.SetComponent(attackerEntity, anim);
                }

                // Reset cooldown (simple APS; no windup/recovery here)
                float gap = 1f / math.max(0.01f, statsRO.ValueRO.attacksPerSecond);
                attackerRW.ValueRW.cooldownLeft = gap;
            }
        }
    }
}


/*
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ApplyMoveRequestsServerSystem))]
public partial struct CombatServerSystem : ISystem
{
    private Random _randomState;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        _randomState = Random.CreateFromIndex(0xC0FFEEu);
    }

    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var entityManager = state.EntityManager;

        // --------------------------------------------------------------------
        // Cache units once per frame: entity, position, owner, alive
        // --------------------------------------------------------------------
        var cachedEntities  = new NativeList<Entity>(Allocator.Temp);
        var cachedPositions = new NativeList<float3>(Allocator.Temp);
        var cachedOwners    = new NativeList<int>(Allocator.Temp);
        var cachedAlive     = new NativeList<bool>(Allocator.Temp);

        foreach (var (transform, unitEntity) in
                 SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Unit>().WithEntityAccess())
        {
            int ownerId = SystemAPI.GetComponent<GhostOwner>(unitEntity).NetworkId;
            bool isAlive = true;
            if (SystemAPI.HasComponent<HealthState>(unitEntity))
            {
                var health = SystemAPI.GetComponent<HealthState>(unitEntity);
                isAlive = health.currentStage != HealthStage.Dead;
            }

            cachedEntities.Add(unitEntity);
            cachedPositions.Add(transform.ValueRO.Position);
            cachedOwners.Add(ownerId);
            cachedAlive.Add(isAlive);
        }
        
        const float CancelPenaltySeconds = 1f;
        const float RotationCancelSuppressSeconds = 2f;

        // -------------------
        // Per-attacker loop: 
        // -------------------
        foreach (var (attackerTransform,
                      unitStats,
                      combatStats,
                      attacker,
                      unitTargets,
                      attackerEntity)
                 in SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitStats>, RefRO<CombatStats>, RefRW<Attacker>, RefRW<UnitTargets>>()
                             .WithAll<Unit>()
                             .WithEntityAccess())
        {
            // Skip dead attackers
            if (SystemAPI.HasComponent<HealthState>(attackerEntity))
            {
                var healthState = SystemAPI.GetComponent<HealthState>(attackerEntity);
                if (healthState.currentStage == HealthStage.Dead) continue;
            }

            //Variables:
            int attackerOwnerNetworkId = SystemAPI.GetComponent<GhostOwner>(attackerEntity).NetworkId;
            float3 attackerPosition = attackerTransform.ValueRO.Position;
            float attackRange = combatStats.ValueRO.attackRange;
            float detectionRadius = unitStats.ValueRO.detectionRadius;
            float detectionRadiusSq = detectionRadius * detectionRadius;

            // ----------------------------
            // Timers: cooldown & pending impact
            // ----------------------------
            float newCooldown = math.max(0f, attacker.ValueRO.cooldownLeft - deltaTime);
            attacker.ValueRW.cooldownLeft = newCooldown;
            bool impactWasPending = attacker.ValueRO.pendingImpactTime > 0f;
            float newPending = math.max(0f, attacker.ValueRO.pendingImpactTime - deltaTime);
            attacker.ValueRW.pendingImpactTime = newPending;
            attacker.ValueRW.rotationSuppressSeconds = math.max(0f, attacker.ValueRO.rotationSuppressSeconds - deltaTime);

            

            // CANCEL ONLY if we are still in wind-up
            if (newPending > 0f && unitTargets.ValueRO.lastAppliedSequence != attacker.ValueRO.swingStartSequence)
            {
                attacker.ValueRW.pendingImpactTime      = 0f;
                attacker.ValueRW.moveLockLeft           = 0f;
                attacker.ValueRW.rotationSuppressSeconds = math.max(
                attacker.ValueRO.rotationSuppressSeconds, RotationCancelSuppressSeconds);
                
                if (SystemAPI.HasComponent<Attacker>(attackerEntity))
                {
                    var att = SystemAPI.GetComponentRW<Attacker>(attackerEntity);
                    att.ValueRW.rotationSuppressSeconds =
                        math.max(att.ValueRO.rotationSuppressSeconds, RotationCancelSuppressSeconds);
                }

                if (SystemAPI.HasComponent<AttackAnimationState>(attackerEntity))
                {
                    var anim = SystemAPI.GetComponent<AttackAnimationState>(attackerEntity);
                    anim.attackCancelTick++;
                    SystemAPI.SetComponent(attackerEntity, anim);
                }

                attacker.ValueRW.cooldownLeft = math.max(newCooldown, CancelPenaltySeconds);
                continue;
            }

            // Find nearest alive enemy in detection range, and decide if it is within attack range
            float attackRangeTolerance = 0.2f; //Set tolerance

            float effectiveAttackRange = math.max(0f, attackRange - attackRangeTolerance);
            float effectiveAttackRangeSq = effectiveAttackRange * effectiveAttackRange;
            int nearestEnemyIndex = -1;
            float nearestDistSq = float.MaxValue;

            for (int i = 0; i < cachedEntities.Length; i++)
            {
                if (!cachedAlive[i]) continue;                           // skip dead
                if (cachedOwners[i] == attackerOwnerNetworkId) continue; // skip own team

                float3 diff = cachedPositions[i] - attackerPosition;
                float distSq = math.lengthsq(diff);

                if (distSq < nearestDistSq && distSq <= detectionRadiusSq)
                {
                    nearestDistSq = distSq;
                    nearestEnemyIndex = i;
                }
            }

            float3 toNearestEnemy = float3.zero;
            bool targetInAttackRange = false;
            float distanceToNearestEnemy = 0f;

            //Set the variables
            if (nearestEnemyIndex != -1)
            {
                toNearestEnemy = cachedPositions[nearestEnemyIndex] - attackerPosition;
                targetInAttackRange = nearestDistSq <= effectiveAttackRangeSq;
                distanceToNearestEnemy = math.sqrt(nearestDistSq);
            }

            //Rotate to nearest enemy in range
            if (nearestEnemyIndex != -1)
            {
                float3 enemyPos = cachedPositions[nearestEnemyIndex];
                float3 toEnemy = enemyPos - attackerPosition;
                float yaw = math.atan2(toEnemy.x, toEnemy.z);

                unitTargets.ValueRW.targetPosition = enemyPos;   // valid this frame
                unitTargets.ValueRW.targetRotation = yaw;        // radians to face
            }
            else
            {
                // Neutralize when no enemy this frame so Movement override stays off
                unitTargets.ValueRW.targetPosition = attackerPosition;
                unitTargets.ValueRW.targetRotation = float.NaN;
            }


            float3 facing = math.forward(attackerTransform.ValueRO.Rotation);
            attacker.ValueRW.attackDirection = math.normalizesafe(facing, new float3(0, 0, 1));


            // ----------------------------
            // Resolve impact if the pending timer just expired
            // ----------------------------
            if (impactWasPending && newPending <= 0f)
            {
                float coneDeg = math.clamp(combatStats.ValueRO.attackConeDeg, 1f, 179f);
                float halfAngleRad = math.radians(coneDeg * 0.5f);
                float cosHalf = math.cos(halfAngleRad);
                float impactRange = attackRange; // optional forgiveness: +0.25f

                var candidateIndices = new NativeList<int>(Allocator.Temp);
                var candidateRanges = new NativeList<float>(Allocator.Temp);

                float3 swingFacing = attacker.ValueRO.attackDirection;
                swingFacing.y = 0f;
                swingFacing = math.normalizesafe(swingFacing, new float3(0, 0, 1));

                for (int i = 0; i < cachedEntities.Length; i++)
                {
                    if (!cachedAlive[i]) continue;
                    if (cachedOwners[i] == attackerOwnerNetworkId) continue;

                    float3 to = cachedPositions[i] - attackerPosition;
                    to.y = 0f;
                    float dist = math.length(to);
                    if (dist > impactRange) continue;

                    float3 dir = dist > 0f ? to / dist : new float3(0, 0, 0);
                    float dotF = math.dot(swingFacing, dir);
                    if (dotF + 1e-4f < cosHalf) continue;

                    candidateIndices.Add(i);
                    candidateRanges.Add(dist);
                }

                if (candidateIndices.Length == 0)
                {
                    // whiff → cancel VFX/UI, but DO NOT lock
                    if (SystemAPI.HasComponent<AttackAnimationState>(attackerEntity))
                    {
                        var anim = SystemAPI.GetComponent<AttackAnimationState>(attackerEntity);
                        anim.attackCancelTick++;
                        SystemAPI.SetComponent(attackerEntity, anim);
                    }

                    // end swing, no lock
                    attacker.ValueRW.attackTargetEntity = Entity.Null;
                    attacker.ValueRW.pendingImpactTime = 0f;
                    attacker.ValueRW.postHitFreezeFrames  = 0;
                }
                else
                {
                    // apply hits nearest-first...
                    const int MaxConeHits = int.MaxValue;
                    int hitsApplied = 0;
                    while (hitsApplied < MaxConeHits && candidateIndices.Length > 0)
                    {
                        int bestIdxInList = 0;
                        float bestRange = candidateRanges[0];
                        for (int k = 1; k < candidateIndices.Length; k++)
                            if (candidateRanges[k] < bestRange) { bestRange = candidateRanges[k]; bestIdxInList = k; }

                        int victimCacheIndex = candidateIndices[bestIdxInList];
                        candidateIndices.RemoveAtSwapBack(bestIdxInList);
                        candidateRanges.RemoveAtSwapBack(bestIdxInList);

                        Entity victim = cachedEntities[victimCacheIndex];
                        if (!entityManager.Exists(victim) || !SystemAPI.HasComponent<HealthState>(victim))
                            continue;

                        var rng = _randomState;
                        bool didHit = rng.NextFloat() <= math.saturate(combatStats.ValueRO.hitchance);
                        _randomState = rng;

                        if (didHit)
                        {
                            var health = SystemAPI.GetComponent<HealthState>(victim);
                            if (health.currentStage != HealthStage.Dead)
                            {
                                health.healthChange -= 1;
                                SystemAPI.SetComponent(victim, health);
                            }
                        }

                        hitsApplied++;
                        
                    }

                    // real impact → start post-swing lock
                    attacker.ValueRW.moveLockLeft = math.max(attacker.ValueRO.moveLockLeft,
                                                             combatStats.ValueRO.postSwingLockSeconds);

                    attacker.ValueRW.postHitFreezeFrames = 0;

                    // clear per-swing
                    attacker.ValueRW.attackTargetEntity = Entity.Null;
                    attacker.ValueRW.pendingImpactTime = 0f;
                }

                candidateIndices.Dispose();
                candidateRanges.Dispose();
            }

            // ----------------------------
            // Start a new swing if not pending and within range
            // ----------------------------
            if (attacker.ValueRO.pendingImpactTime <= 0f
                && newCooldown <= 0f
                && targetInAttackRange)
            {
                // Lock impact timing
                attacker.ValueRW.pendingImpactTime = combatStats.ValueRO.impactDelaySeconds;
                // remember which order sequence started this wind-up
                attacker.ValueRW.swingStartSequence = unitTargets.ValueRO.lastAppliedSequence;

                // Notify clients to play swing animation
                if (SystemAPI.HasComponent<AttackAnimationState>(attackerEntity))
                {
                    var anim = SystemAPI.GetComponent<AttackAnimationState>(attackerEntity);
                    anim.attackTick++;
                    SystemAPI.SetComponent(attackerEntity, anim);
                }

                // Cooldown until the next available swing (wind-up + recovery both included)
                float baseGap = 1f / math.max(0.01f, combatStats.ValueRO.attacksPerSecond);
                attacker.ValueRW.cooldownLeft = baseGap + combatStats.ValueRO.postSwingLockSeconds;
            }
        }

        // Cleanup caches
        cachedEntities.Dispose();
        cachedPositions.Dispose();
        cachedOwners.Dispose();
        cachedAlive.Dispose();
    }
}
*/