using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ApplyMoveRequestsServerSystem))]
[UpdateBefore(typeof(FollowEntityServerSystem))]
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
            bool allowAutoFocus = attacker.attackMove || (!targets.activeTargetSet && attacker.autoTarget);

            if (!allowAutoFocus)
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

                // Mask check (no owner fallback)
                byte otherFaction = candFactions[i];
                if (!FactionUtility.AreHostile(myFaction, otherFaction, rel, factionCount))
                    continue;

                float3 d  = candTransforms[i].Position - myP; d.y = 0f;
                float  d2 = math.lengthsq(d);
                if (d2 > r2) continue;

                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0)
            {
                var    best = candEntities[bestIdx];
                float3 tPos = candTransforms[bestIdx].Position;
                float3 to   = tPos - myP; to.y = 0f;

                targetsRW.ValueRW.targetEntity   = best;
                targetsRW.ValueRW.targetRotation = math.atan2(to.x, to.z);

                // Optional: mirror on Attacker for later combat logic
                if (em.HasComponent<Attacker>(e))
                {
                    var att = em.GetComponentData<Attacker>(e);
                    att.attackTargetEntity = best;
                    em.SetComponentData(e, att);
                }
            }
            else
            {
                targetsRW.ValueRW.targetRotation = float.NaN;
                targetsRW.ValueRW.targetEntity   = Entity.Null;
            }
        }

        // Disposals
        candFactions.Dispose();
        candTransforms.Dispose();
        candEntities.Dispose();
    }
}