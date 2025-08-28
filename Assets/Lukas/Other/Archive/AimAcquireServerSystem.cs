/*
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateAfter(typeof(AutoTargetServerSystem))]
[UpdateBefore(typeof(MovementSystem))]
public partial struct AimAcquireServerSystem : ISystem
{
    // Tunables (small hysteresis so aim doesn’t thrash at edges)
    const float KEEP_DIST_EPS   = 0.75f;   // meters: extra distance to keep current aim
    const float KEEP_ANGLE_COS  = -1f;     // set > -1 to require rough frontal alignment (e.g., 0 = 180°, 0.5 = ±60°). Here we don’t require frontality.

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        // Optional faction singletons
        bool hasRel   = SystemAPI.TryGetSingleton<FactionRelations>(out var rel);
        bool hasCount = SystemAPI.TryGetSingleton<FactionCount>(out var fCount);
        byte factionCount = hasCount ? fCount.Value : (byte)32;

        // Cache candidate units (hostility checked per-attacker)
        var candQ = SystemAPI.QueryBuilder().WithAll<Unit, LocalTransform>().Build();
        NativeArray<Entity>         candEntities   = candQ.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> candTransforms = candQ.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        bool IsHostile(Entity self, Entity other)
        {
            if (!em.HasComponent<Unit>(other)) return false;

            if (hasRel && hasCount && em.HasComponent<Faction>(self) && em.HasComponent<Faction>(other))
            {
                byte me = em.GetComponentData<Faction>(self).FactionId;
                byte ot = em.GetComponentData<Faction>(other).FactionId;
                return FactionUtility.AreHostile(me, ot, rel, factionCount);
            }

            if (em.HasComponent<GhostOwner>(self) && em.HasComponent<GhostOwner>(other))
            {
                int a = em.GetComponentData<GhostOwner>(self).NetworkId;
                int b = em.GetComponentData<GhostOwner>(other).NetworkId;
                return (a != int.MinValue) && (b != int.MinValue) && (a != b);
            }
            return false;
        }

        bool IsAlive(Entity e)
        {
            if (!em.HasComponent<HealthState>(e)) return true;
            var h = em.GetComponentData<HealthState>(e);
            return h.currentStage != HealthStage.Dead;
        }

        foreach (var (ltRO, statsRO, attackerRW, selfEntity) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitStats>, RefRW<Attacker>>()
                          .WithAll<Unit>()
                          .WithEntityAccess())
        {
            float3 selfPos = ltRO.ValueRO.Position;
            float  detectR = math.max(0f, statsRO.ValueRO.detectionRadius);
            float  keepR   = detectR + KEEP_DIST_EPS;
            float  keepR2  = keepR * keepR;

            var attacker = attackerRW.ValueRO;

            // Try to KEEP current aimEntity if it’s still good.
            bool kept = false;
            if (attacker.aimEntity != Entity.Null && em.Exists(attacker.aimEntity) && IsAlive(attacker.aimEntity) && IsHostile(selfEntity, attacker.aimEntity))
            {
                float3 tgtPos = em.HasComponent<LocalTransform>(attacker.aimEntity)
                              ? em.GetComponentData<LocalTransform>(attacker.aimEntity).Position
                              : (em.HasComponent<LocalToWorld>(attacker.aimEntity)
                                 ? em.GetComponentData<LocalToWorld>(attacker.aimEntity).Position
                                 : attacker.aimPosition); // fallback to last-known

                float3 to = tgtPos - selfPos; to.y = 0f;
                float d2 = math.lengthsq(to);
                if (d2 > 1e-10f && d2 <= keepR2)
                {
                    // Optional frontal check (disabled by default: KEEP_ANGLE_COS = -1)
                    bool keepFront = true;
                    if (KEEP_ANGLE_COS > -1f)
                    {
                        float3 fwd = math.forward(ltRO.ValueRO.Rotation); fwd.y = 0f; fwd = math.normalizesafe(fwd, new float3(0,0,1));
                        float3 dir = to / math.sqrt(d2);
                        keepFront = math.dot(fwd, dir) >= KEEP_ANGLE_COS;
                    }

                    if (keepFront)
                    {
                        attackerRW.ValueRW.aimEntity   = attacker.aimEntity;
                        attackerRW.ValueRW.aimPosition = tgtPos;
                        attackerRW.ValueRW.aimRotation = math.atan2(to.x, to.z);
                        kept = true;
                    }
                }
            }

            if (kept)
                continue;

            // Reacquire: closest hostile within detection radius
            int   bestIdx = -1;
            float bestD2  = float.MaxValue;

            for (int i = 0; i < candEntities.Length; i++)
            {
                var e = candEntities[i];
                if (e == selfEntity) continue;
                if (!IsAlive(e))     continue;
                if (!IsHostile(selfEntity, e)) continue;

                float3 to = candTransforms[i].Position - selfPos; to.y = 0f;
                float  d2 = math.lengthsq(to);
                if (d2 > detectR * detectR) continue;

                if (d2 < bestD2) { bestD2 = d2; bestIdx = i; }
            }

            if (bestIdx >= 0)
            {
                float3 tgtPos = candTransforms[bestIdx].Position;
                float3 to = tgtPos - selfPos; to.y = 0f;

                attackerRW.ValueRW.aimEntity   = candEntities[bestIdx];
                attackerRW.ValueRW.aimPosition = tgtPos;
                attackerRW.ValueRW.aimRotation = math.atan2(to.x, to.z);
            }
            else
            {
                // No aim this tick
                attackerRW.ValueRW.aimEntity   = Entity.Null;
                attackerRW.ValueRW.aimRotation = float.NaN; // consumers can test math.isfinite
                // aimPosition left as-is (preserves ground-aim if set by abilities)
            }
        }

        candTransforms.Dispose();
        candEntities.Dispose();
    }
}
*/