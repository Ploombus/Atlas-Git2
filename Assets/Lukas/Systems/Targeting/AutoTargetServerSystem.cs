using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateAfter(typeof(ApplyMoveRequestsServerSystem))]
[UpdateBefore(typeof(MovementToEntityServerSystem))]
public partial struct AutoTargetServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        state.RequireForUpdate<FactionRelations>();
        state.RequireForUpdate<FactionCount>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        // Mask singletons (required in OnCreate)
        var rel          = SystemAPI.GetSingleton<FactionRelations>();
        byte factionCount= SystemAPI.GetSingleton<FactionCount>().Value;

        // Cache candidate units once per frame
        var candQuery = SystemAPI.QueryBuilder()
                                 .WithAll<Unit, LocalTransform>()
                                 .Build();

        NativeArray<Entity>          candEntities   = candQuery.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform>  candTransforms = candQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        NativeArray<byte>            candFactions   = new NativeArray<byte>(candEntities.Length, Allocator.Temp);

        for (int i = 0; i < candEntities.Length; i++)
        {
            var e = candEntities[i];
            candFactions[i] = em.HasComponent<Faction>(e) ? em.GetComponentData<Faction>(e).FactionId : (byte)0;
        }

        // Per-attacker pass
        foreach (var (lt, stats, attackerRO, targetsRW, e) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitStats>, RefRO<Attacker>, RefRW<UnitTargets>>()
                          .WithAll<Unit>()
                          .WithEntityAccess())
        {
            var targets  = targetsRW.ValueRO;
            var attacker = attackerRO.ValueRO;

            // Keep manual focus if a manual target is set and valid
            if (targets.activeTargetSet && targets.targetEntity != Entity.Null && em.Exists(targets.targetEntity))
            {
                if (em.HasComponent<LocalTransform>(targets.targetEntity))
                {
                    float3 myPos = lt.ValueRO.Position;
                    float3 tPos  = em.GetComponentData<LocalTransform>(targets.targetEntity).Position;
                    float3 to    = tPos - myPos; to.y = 0f;

                    targetsRW.ValueRW.targetRotation = math.atan2(to.x, to.z);
                }
                else
                {
                    // Lost transform → clear so FollowTarget stops
                    targetsRW.ValueRW.targetRotation = float.NaN;
                    targetsRW.ValueRW.targetEntity   = Entity.Null;
                }
                continue;
            }

            // Decide if auto-focus is allowed this tick

            bool isAMove = attacker.attackMove && targets.activeTargetSet;
            bool isAutoFocus = !targets.activeTargetSet && attacker.autoTarget;
            bool allowAutoFocus = isAMove || isAutoFocus;

            if (!allowAutoFocus)
            {
                targetsRW.ValueRW.targetRotation = float.NaN;
                targetsRW.ValueRW.targetEntity   = Entity.Null;
                continue;
            }

            // === NEW: leash gate for auto-focus (not for A-move) ===
            float leashMeters = attacker.maxChaseMeters;
            bool useLeash = isAutoFocus && leashMeters >= 0f; // negative = unlimited, match movement behavior

            // If leash == 0 → never acquire a target
            if (useLeash && leashMeters == 0f)
            {
                targetsRW.ValueRW.targetRotation = float.NaN;
                targetsRW.ValueRW.targetEntity   = Entity.Null;
                continue;
            }

            // Mask-only hostility search
            float3 myP = lt.ValueRO.Position;
            float  r   = math.max(0f, stats.ValueRO.detectionRadius);
            float  r2  = r * r;

            byte myFaction = FactionUtility.EffectiveFaction(e, em);

            float effectiveAttackRange = 0f;
            if (em.HasComponent<CombatStats>(e))
            {
                var cs = em.GetComponentData<CombatStats>(e);
                const float tol = 1f; // keep consistent with MovementToEntity’s tolerance
                effectiveAttackRange = math.max(0f, cs.attackRange - tol);
            }


            int   bestIdx = -1;
            float bestD2  = float.MaxValue;

            for (int i = 0; i < candEntities.Length; i++)
            {
                var other = candEntities[i];
                if (other == e) continue;

                // Skip dead if present
                if (em.HasComponent<HealthState>(other))
                {
                    var hs = em.GetComponentData<HealthState>(other);
                    if (hs.currentStage == HealthStage.Dead) continue;
                }

                // Hostility
                byte otherFaction = candFactions[i];
                if (!FactionUtility.AreHostile(myFaction, otherFaction, rel, factionCount))
                    continue;

                // Detection radius
                float3 d  = candTransforms[i].Position - myP; d.y = 0f;
                float  d2 = math.lengthsq(d);
                if (d2 > r2) continue;

                // === NEW: leash filter (auto-focus only) ===
                if (useLeash && leashMeters > 0f)
                {
                    // Where would we stop if we chased this unit? (enemy: stop at attack range from target)
                    float3 dir = math.normalizesafe(d, new float3(0,0,1));
                    float3 desiredStopPos = candTransforms[i].Position - dir * effectiveAttackRange;

                    float3 center = targets.destinationPosition; // same “center” you clamp to in Movement systems
                    float3 off = desiredStopPos - center; off.y = 0f;
                    float  distFromCenter = math.length(off);
                    if (distFromCenter > leashMeters) continue; // out of leash → ignore this candidate
                }

                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0)
            {
                var best = candEntities[bestIdx];
                float3 tPos = candTransforms[bestIdx].Position;
                float3 to = tPos - myP; to.y = 0f;

                targetsRW.ValueRW.targetEntity = best;
                targetsRW.ValueRW.targetRotation = math.atan2(to.x, to.z);
                
                /*
                // Optional: mirror on Attacker for later combat logic
                if (em.HasComponent<Attacker>(e))
                {
                    var att = em.GetComponentData<Attacker>(e);
                    att.attackAimEntity = best;
                    em.SetComponentData(e, att);
                }
                */
            }
            else
            {
                targetsRW.ValueRW.targetRotation = float.NaN;
                targetsRW.ValueRW.targetEntity = Entity.Null;
            }
        }

        // Disposals
        candFactions.Dispose();
        candTransforms.Dispose();
        candEntities.Dispose();
    }
}