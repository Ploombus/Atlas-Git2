using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using Managers;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateAfter(typeof(ApplyMoveRequestsServerSystem))]
partial struct MovementToEntityServerSystem : ISystem
{

    const float AttackRangeTolerance = 0.1f;
    const float followPadding        = 0.05f;
    const float MovementStoppingTolerance = 0.31f; // arrival-braking is stopping us too early
        
    public void OnUpdate(ref SystemState state)
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false) return;

        var em = state.EntityManager;

        em.CompleteDependencyBeforeRO<LocalToWorld>();
        em.CompleteDependencyBeforeRO<LocalTransform>();

        // optional faction singletons (mask-based)
        bool hasRel = SystemAPI.TryGetSingleton<FactionRelations>(out var rel);
        bool hasCount = SystemAPI.TryGetSingleton<FactionCount>(out var fCount);
        byte factionCount = hasCount ? fCount.Value : (byte)32;

        var lt = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var ltw = SystemAPI.GetComponentLookup<LocalToWorld>(true);

        bool TryGetWorldPos(Entity e, out float3 pos)
        {
            if (lt.HasComponent(e)) { pos = lt[e].Position; return true; }
            if (ltw.HasComponent(e)) { pos = ltw[e].Position; return true; }
            pos = default; return false;
        }

        bool IsEnemyUnit(Entity self, Entity other)
        {
            if (!em.HasComponent<Unit>(other))
                return false;

            bool hostile = false;

            if (hasRel && hasCount &&
                em.HasComponent<Faction>(self) &&
                em.HasComponent<Faction>(other))
            {
                byte myF = em.GetComponentData<Faction>(self).FactionId;
                byte tgF = em.GetComponentData<Faction>(other).FactionId;
                hostile = FactionUtility.AreHostile(myF, tgF, rel, factionCount);
            }
            else if (em.HasComponent<GhostOwner>(self) && em.HasComponent<GhostOwner>(other))
            {
                int selfOwner = em.GetComponentData<GhostOwner>(self).NetworkId;
                int targetOwner = em.GetComponentData<GhostOwner>(other).NetworkId;
                hostile = (selfOwner != int.MinValue) && (targetOwner != int.MinValue) && (selfOwner != targetOwner);
            }

            return hostile;
        }

        foreach (var (targetsRW, selfXformRO, selfCombatRO, selfEntity) in
                 SystemAPI.Query<RefRW<UnitTargets>, RefRO<LocalTransform>, RefRO<CombatStats>>()
                          .WithEntityAccess())
        {
            ref var targets = ref targetsRW.ValueRW;

            bool haveTarget = (targets.targetEntity != Entity.Null);
            bool haveAnchor = (targets.destinationEntity != Entity.Null);
            if (!haveTarget && !haveAnchor)
                continue;

            float dt = SystemAPI.Time.DeltaTime;
            float alpha = 1f - math.exp(-14f * dt);

            // Helper to compute desired stop/yaw for a focus entity.
            bool ComputeDesired(Entity focus, out float3 desiredStopPos, out float desiredYaw)
            {
                desiredStopPos = default;
                desiredYaw = float.NaN;

                if (focus == Entity.Null || !em.Exists(focus))
                    return false;

                float3 focusPos;
                if (!TryGetWorldPos(focus, out focusPos))
                    return false;

                float3 selfPos = selfXformRO.ValueRO.Position;
                float3 toFocus = focusPos - selfPos; toFocus.y = 0f;
                float distSq = math.lengthsq(toFocus);
                if (distSq <= 1e-10f)
                    return false;

                float distance = math.sqrt(distSq);
                float3 dir = toFocus / distance;

                float targetRadius = em.HasComponent<TargetingSize>(focus)
                    ? em.GetComponentData<TargetingSize>(focus).radius
                    : 0f;

                float selfRadius = em.HasComponent<TargetingSize>(selfEntity)
                    ? em.GetComponentData<TargetingSize>(selfEntity).radius
                    : 0f;

                bool hasAttacker = em.HasComponent<Attacker>(selfEntity);
                var att = hasAttacker ? em.GetComponentData<Attacker>(selfEntity) : default;
                bool isCharging = hasAttacker && att.isCharging;
                bool kiting = hasAttacker && att.kitingEnabled;

                // keep your tolerance on attack range (movement side)
                float effectiveAttackRangeForMove =
                    math.max(0f, selfCombatRO.ValueRO.attackRange - MovementStoppingTolerance - AttackRangeTolerance);

                bool enemy = IsEnemyUnit(selfEntity, focus);

                // --- Rings (center-to-center) ---
                float innerStop = selfRadius + targetRadius; // rings just touch
                float outerStop = enemy
                    ? (effectiveAttackRangeForMove + selfRadius + targetRadius)  // add radii on top of (range - tol)
                    : (selfRadius + targetRadius + followPadding);               // follow spacing

                float stopDistanceFromCenter;

                if (isCharging)
                {
                    // charge ignores spacing; go straight in
                    stopDistanceFromCenter = 0f;
                }
                else if (enemy)
                {
                    if (kiting)
                    {
                        // old behavior: sit on the outer ring
                        stopDistanceFromCenter = outerStop;
                    }
                    else
                    {
                        // zone behavior: hold if already between rings
                        const float epsilon = 1e-4f;

                        if (distance > outerStop + epsilon)
                        {
                            stopDistanceFromCenter = outerStop;   // outside → move in to outer
                        }
                        else if (distance < innerStop - epsilon)
                        {
                            stopDistanceFromCenter = innerStop;   // too close → back out to inner
                        }
                        else
                        {
                            // inside the zone → hold pos, only face
                            desiredStopPos = selfPos;
                            desiredYaw = math.atan2(dir.x, dir.z);
                            return true;
                        }
                    }
                }
                else
                {
                    // non-enemy anchor/follow
                    stopDistanceFromCenter = innerStop + followPadding;
                }

                desiredStopPos = focusPos - dir * stopDistanceFromCenter;
                desiredYaw = math.atan2(dir.x, dir.z);
                return true;
            }

            // ===== TARGET PATH (AI steer) =====
            if (haveTarget)
            {
                var focus = targets.targetEntity;

                // Compute base desired stop/yaw around the target
                if (ComputeDesired(focus, out var desiredStopPos, out var desiredYaw))
                {
                    targets.targetPosition = math.lerp(targets.targetPosition, desiredStopPos, alpha);
                    targets.targetRotation = desiredYaw;
                    targets.hasArrived = false;
                }
            }

            // ===== DESTINATION ANCHOR PATH (player intent) =====
            if (haveAnchor)
            {
                var anchor = targets.destinationEntity;

                // If anchor is gone/missing transform → clear and "stick" (do not change the value)
                if (!em.Exists(anchor) ||
                    !(lt.HasComponent(anchor) || ltw.HasComponent(anchor)))
                {
                    targets.destinationEntity = Entity.Null;
                }
                else
                {
                    bool reused = false;

                    // If anchor == target and we already computed desiredStopPos/desiredYaw, reuse them.
                    if (haveTarget && anchor == targets.targetEntity)
                    {
                        // Recompute once (cheap) to avoid keeping extra locals/flags.
                        if (ComputeDesired(anchor, out var stopPosA, out var yawA))
                        {
                            targets.destinationPosition = math.lerp(targets.destinationPosition, stopPosA, alpha);
                            targets.destinationRotation = yawA;
                            reused = true;
                        }
                    }

                    if (!reused)
                    {
                        if (ComputeDesired(anchor, out var stopPos, out var yaw))
                        {
                            // No stance leash for the anchor (manual intent).
                            targets.destinationPosition = math.lerp(targets.destinationPosition, stopPos, alpha);
                            targets.destinationRotation = yaw;
                        }
                        else
                        {
                            // Could not compute (e.g., degenerate distance) → keep last value, but clear anchor if invalid
                            if (!em.Exists(anchor))
                                targets.destinationEntity = Entity.Null;
                        }
                    }
                }
            }
        }
    }
}