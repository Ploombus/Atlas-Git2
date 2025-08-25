using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ApplyMoveRequestsServerSystem))]
[UpdateAfter(typeof(TransformSystemGroup))]
partial struct FollowEntityServerSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        em.CompleteDependencyBeforeRO<LocalToWorld>();
        em.CompleteDependencyBeforeRO<LocalTransform>();

        // optional faction singletons (mask-based)
        bool hasRel   = SystemAPI.TryGetSingleton<FactionRelations>(out var rel);
        bool hasCount = SystemAPI.TryGetSingleton<FactionCount>(out var fCount);
        byte factionCount = hasCount ? fCount.Value : (byte)32;

        var lt  = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var ltw = SystemAPI.GetComponentLookup<LocalToWorld>(true);
        bool TryGetWorldPos(Entity e, out float3 pos)
        {
            if (lt.HasComponent(e))  { pos = lt[e].Position;  return true; }
            if (ltw.HasComponent(e)) { pos = ltw[e].Position; return true; }
            pos = default; return false;
        }

        const float attackRangeTolerance = 1f;
        const float followPadding        = 0.05f;

        foreach (var (targetsRW, selfXformRO, selfCombatRO, selfEntity) in
                 SystemAPI.Query<RefRW<UnitTargets>, RefRO<LocalTransform>, RefRO<CombatStats>>()
                          .WithEntityAccess())
        {
            ref var targets = ref targetsRW.ValueRW;

            Entity targetEntity = targets.targetEntity;
            if (targetEntity == Entity.Null)
                continue;

            // Resolve target world pos safely
            bool exists  = em.Exists(targetEntity);
            float3 targetWorldPos = default;
            bool gotPos  = exists && TryGetWorldPos(targetEntity, out targetWorldPos);

            // Target died/despawned or no transform => clear focus and either park (manual) or keep marching (A-move)
            if (!exists || !gotPos)
            {
                targets.targetEntity   = Entity.Null;
                targets.targetRotation = float.NaN;

                bool keepMarching = false;
                if (em.HasComponent<Attacker>(selfEntity))
                {
                    var att = em.GetComponentData<Attacker>(selfEntity);
                    keepMarching = att.attackMove; // A-move continues toward previous destination
                }

                if (keepMarching)
                {
                    targets.activeTargetSet = true;
                    targets.hasArrived      = false;
                }
                else
                {
                    if (!targets.hasArrived)
                    {
                        float3 selfPos = selfXformRO.ValueRO.Position;
                        targets.destinationPosition = selfPos;
                        targets.destinationRotation = float.NaN; // keep facing as-is
                        targets.hasArrived          = true;
                    }

                    targets.activeTargetSet = false;
                    targets.targetPosition  = float3.zero;
                }
                continue;
            }

            // Compute steer ring
            float3 selfWorldPos = selfXformRO.ValueRO.Position;
            float3 toTarget     = targetWorldPos - selfWorldPos; toTarget.y = 0f;
            float  distSq       = math.lengthsq(toTarget);
            if (distSq <= 1e-10f)
                continue;

            float distance = math.sqrt(distSq);

            // radii (0 if missing)
            float targetRadius = em.HasComponent<TargetingSize>(targetEntity)
                               ? em.GetComponentData<TargetingSize>(targetEntity).radius
                               : 0f;
            float selfRadius   = em.HasComponent<TargetingSize>(selfEntity)
                               ? em.GetComponentData<TargetingSize>(selfEntity).radius
                               : 0f;

            // hostile check — prefer faction mask; else fall back to owner mismatch
            bool targetIsEnemyUnit = false;
            if (em.HasComponent<Unit>(targetEntity))
            {
                bool hostile = false;

                if (hasRel && hasCount &&
                    em.HasComponent<Faction>(selfEntity) &&
                    em.HasComponent<Faction>(targetEntity))
                {
                    byte myF = em.GetComponentData<Faction>(selfEntity).FactionId;
                    byte tgF = em.GetComponentData<Faction>(targetEntity).FactionId;
                    hostile = FactionUtility.AreHostile(myF, tgF, rel, factionCount);
                }
                else if (em.HasComponent<GhostOwner>(selfEntity) && em.HasComponent<GhostOwner>(targetEntity))
                {
                    int selfOwner   = em.GetComponentData<GhostOwner>(selfEntity).NetworkId;
                    int targetOwner = em.GetComponentData<GhostOwner>(targetEntity).NetworkId;
                    hostile = (selfOwner != int.MinValue) && (targetOwner != int.MinValue) && (selfOwner != targetOwner);
                }

                targetIsEnemyUnit = hostile;
            }

            // charge gate
            bool isCharging = em.HasComponent<Attacker>(selfEntity) && em.GetComponentData<Attacker>(selfEntity).isCharging;

            float effectiveAttackRange = math.max(0f, selfCombatRO.ValueRO.attackRange - attackRangeTolerance);

            float stopDistanceFromCenter =
                isCharging
                    ? 0f
                    : (targetIsEnemyUnit
                        ? effectiveAttackRange
                        : (targetRadius + selfRadius + followPadding));

            float3 dirToTarget    = toTarget / distance;
            float3 desiredStopPos = targetWorldPos - dirToTarget * stopDistanceFromCenter;
            float  desiredYaw     = math.atan2(dirToTarget.x, dirToTarget.z);

            // ===== STANCE LEASH (AUTO-FOCUS ONLY) =====
            // Only when following an auto-acquired focus (not manual) and autoTarget is enabled.
            bool isAutoFocus = (targets.targetEntity != Entity.Null) && !targets.activeTargetSet;
            if (isAutoFocus && em.HasComponent<Attacker>(selfEntity))
            {
                var att = em.GetComponentData<Attacker>(selfEntity);
                if (att.autoTarget)
                {
                    if (att.maxChaseMeters == 0f)
                    {
                        // Hold Ground: face but don't advance.
                        desiredStopPos = selfWorldPos;
                    }
                    else if (att.maxChaseMeters > 0f)
                    {
                        // Defensive: clamp pursuit to circle around destinationPosition (home).
                        float3 center = targets.destinationPosition;
                        float3 off    = desiredStopPos - center; off.y = 0f;
                        float  d      = math.length(off);
                        if (d > att.maxChaseMeters && d > 1e-6f)
                            desiredStopPos = center + (off / d) * att.maxChaseMeters;
                    }
                    // Aggressive (<0): no clamp.
                }
            }
            // ===== END STANCE LEASH =====

            // Smooth targetPosition; never touch destinationPosition here
            float  dt    = SystemAPI.Time.DeltaTime;
            float  alpha = 1f - math.exp(-14f * dt);
            targets.targetPosition = math.lerp(targets.targetPosition, desiredStopPos, alpha);
            targets.targetRotation = desiredYaw;
            targets.hasArrived     = false; // prevent Movement sticky-idle while following
        }
    }
}


[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[UpdateAfter(typeof(ApplyMoveRequestsClientPredictSystem))]
partial struct FollowEntityClientPredictSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        em.CompleteDependencyBeforeRO<LocalToWorld>();
        em.CompleteDependencyBeforeRO<LocalTransform>();

        // optional faction singletons (mask-based)
        bool hasRel   = SystemAPI.TryGetSingleton<FactionRelations>(out var rel);
        bool hasCount = SystemAPI.TryGetSingleton<FactionCount>(out var fCount);
        byte factionCount = hasCount ? fCount.Value : (byte)32;

        var lt  = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var ltw = SystemAPI.GetComponentLookup<LocalToWorld>(true);
        bool TryGetWorldPos(Entity e, out float3 pos)
        {
            if (lt.HasComponent(e))  { pos = lt[e].Position;  return true; }
            if (ltw.HasComponent(e)) { pos = ltw[e].Position; return true; }
            pos = default; return false;
        }

        const float attackRangeTolerance = 1f;
        const float followPadding        = 0.05f;

        foreach (var (targetsRW, selfXformRO, selfCombatRO, selfEntity) in
                 SystemAPI.Query<RefRW<UnitTargets>, RefRO<LocalTransform>, RefRO<CombatStats>>()
                          .WithAll<PredictedGhost>()
                          .WithEntityAccess())
        {
            ref var targets = ref targetsRW.ValueRW;

            Entity targetEntity = targets.targetEntity;
            if (targetEntity == Entity.Null)
                continue;

            // Resolve target world pos safely
            bool exists  = em.Exists(targetEntity);
            float3 targetWorldPos = default;
            bool gotPos  = exists && TryGetWorldPos(targetEntity, out targetWorldPos);

            // Mirror server despawn handling
            if (!exists || !gotPos)
            {
                targets.targetEntity   = Entity.Null;
                targets.targetRotation = float.NaN;

                bool keepMarching = false;
                if (em.HasComponent<Attacker>(selfEntity))
                {
                    var att = em.GetComponentData<Attacker>(selfEntity);
                    keepMarching = att.attackMove;
                }

                if (keepMarching)
                {
                    targets.activeTargetSet = true;
                    targets.hasArrived      = false;
                }
                else
                {
                    if (!targets.hasArrived)
                    {
                        float3 selfPos = selfXformRO.ValueRO.Position;
                        targets.destinationPosition = selfPos;
                        targets.destinationRotation = float.NaN;
                        targets.hasArrived          = true;
                    }

                    targets.activeTargetSet = false;
                    targets.targetPosition  = float3.zero;
                }
                continue;
            }

            // Compute steer ring
            float3 selfWorldPos = selfXformRO.ValueRO.Position;
            float3 toTarget     = targetWorldPos - selfWorldPos; toTarget.y = 0f;
            float  distSq       = math.lengthsq(toTarget);
            if (distSq <= 1e-10f)
                continue;

            float distance = math.sqrt(distSq);

            float targetRadius = em.HasComponent<TargetingSize>(targetEntity)
                               ? em.GetComponentData<TargetingSize>(targetEntity).radius
                               : 0f;
            float selfRadius   = em.HasComponent<TargetingSize>(selfEntity)
                               ? em.GetComponentData<TargetingSize>(selfEntity).radius
                               : 0f;

            bool targetIsEnemyUnit = false;
            if (em.HasComponent<Unit>(targetEntity))
            {
                bool hostile = false;

                if (hasRel && hasCount &&
                    em.HasComponent<Faction>(selfEntity) &&
                    em.HasComponent<Faction>(targetEntity))
                {
                    byte myF = em.GetComponentData<Faction>(selfEntity).FactionId;
                    byte tgF = em.GetComponentData<Faction>(targetEntity).FactionId;
                    hostile = FactionUtility.AreHostile(myF, tgF, rel, factionCount);
                }
                else if (em.HasComponent<GhostOwner>(selfEntity) && em.HasComponent<GhostOwner>(targetEntity))
                {
                    int selfOwner   = em.GetComponentData<GhostOwner>(selfEntity).NetworkId;
                    int targetOwner = em.GetComponentData<GhostOwner>(targetEntity).NetworkId;
                    hostile = (selfOwner != int.MinValue) && (targetOwner != int.MinValue) && (selfOwner != targetOwner);
                }

                targetIsEnemyUnit = hostile;
            }

            bool isCharging = em.HasComponent<Attacker>(selfEntity) && em.GetComponentData<Attacker>(selfEntity).isCharging;

            float effectiveAttackRange = math.max(0f, selfCombatRO.ValueRO.attackRange - attackRangeTolerance);

            float stopDistanceFromCenter =
                isCharging
                    ? 0f
                    : (targetIsEnemyUnit
                        ? effectiveAttackRange
                        : (targetRadius + selfRadius + followPadding));

            float3 dirToTarget    = toTarget / distance;
            float3 desiredStopPos = targetWorldPos - dirToTarget * stopDistanceFromCenter;
            float  desiredYaw     = math.atan2(dirToTarget.x, dirToTarget.z);

            // ===== STANCE LEASH (AUTO-FOCUS ONLY, PREDICTION) =====
            bool isAutoFocus = (targets.targetEntity != Entity.Null) && !targets.activeTargetSet;
            if (isAutoFocus && em.HasComponent<Attacker>(selfEntity))
            {
                var att = em.GetComponentData<Attacker>(selfEntity);
                if (att.autoTarget)
                {
                    if (att.maxChaseMeters == 0f)
                    {
                        // Hold Ground: face but don't advance.
                        desiredStopPos = selfWorldPos;
                    }
                    else if (att.maxChaseMeters > 0f)
                    {
                        float3 center = targets.destinationPosition;
                        float3 off    = desiredStopPos - center; off.y = 0f;
                        float  d      = math.length(off);
                        if (d > att.maxChaseMeters && d > 1e-6f)
                            desiredStopPos = center + (off / d) * att.maxChaseMeters;
                    }
                    // Aggressive (<0): no clamp.
                }
            }
            // ===== END STANCE LEASH =====

            float  dt    = SystemAPI.Time.DeltaTime;
            float  alpha = 1f - math.exp(-14f * dt);
            targets.targetPosition = math.lerp(targets.targetPosition, desiredStopPos, alpha);
            targets.targetRotation = desiredYaw;
            targets.hasArrived     = false;
        }
    }
}