using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Physics;
using UnityEngine;
using Unity.Collections;
using Managers;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
partial struct MovementSystem : ISystem
{
    // ========================= KNOBS =========================

    // --- General movement ---
    const float MIN_DISTANCE           = 0.33f; // [m] distance to goal at which we mark "arrived"
    const float STICK_RADIUS_MULT      = 3f;    // sticky zone radius = MIN_DISTANCE * this

    // --- Mass / acceleration model (asymmetric accel/decel) ---
    const float REF_MASS_KG            = 100f;  // [kg] reference mass for scaling accel/decel
    const float ACCEL_AT_REF           = 10f;   // [m/s^2] forward accel at REF_MASS_KG
    const float DECEL_AT_REF           = 10f;   // [m/s^2] braking decel at REF_MASS_KG
    const float MASS_EXPONENT          = 0.8f;  // how strongly mass changes accel/decel (higher = more effect)
    const float MIN_ACCEL              = 2.2f;  // [m/s^2] lower clamp for accel
    const float MAX_ACCEL              = 50f;   // [m/s^2] upper clamp for accel
    const float MIN_DECEL              = 1.5f;  // [m/s^2] lower clamp for decel
    const float MAX_DECEL              = 50f;   // [m/s^2] upper clamp for decel

    // --- Arrival / braking ---
    const float ARRIVE_SAFETY           = 1.1f;  // multiplies stopping distance (extra buffer)
    const float ARRIVE_MIN_DIST         = 1f;    // [m] minimum arrival radius (never smaller)
    const float ARRIVE_MAX_DIST         = 20f;   // [m] bypass arrival slowdown when farther than this
    const float ARRIVE_SPEED_FLOOR_MPS  = 3f;    // [m/s] absolute floor on target speed near goal
    const float ARRIVE_SPEED_INFLUENCE  = 0.5f;  // 0..1 — extra radius scale from current speed fraction
    const float ARRIVE_WEIGHT_INFLUENCE = 0.3f;  // 0..1 — extra radius scale from weight (vs REF_MASS_KG)
    const float ARRIVE_CAP_CURVE        = 0.2f;  // 0=linear, 1=steeper near target (uses p in [1..3])

    // --- Heading slowdown (reduces speed when not facing input) ---
    const float HEADING_CURVE_EXP = 0.4f;       // shape of slowdown vs. angle (1=linear; lower = gentler)
    const float HEADING_THROTTLE_FLOOR = 0.5f;  // minimum forward fraction even at large heading error

    // --- Lateral slip reduction (pulls velocity toward desired heading) ---
    const float LATERAL_KILL_MIN  = 0.5f;  // strength at low speed
    const float LATERAL_KILL_MAX  = 10f;   // strength at high speed
    const float LATERAL_KILL_EXP  = 0.5f;  // curve shape
    const float LATERAL_MASS_EXP  = 0.2f;  // heavier kills less

    // --- Turn braking (slows down during sharp heading changes) ---
    const float TURN_BRAKE_START_DEG = 75f;
    const float TURN_BRAKE_RANGE_DEG = 60f;
    const float TURN_BRAKE_STRENGTH  = 0.5f;
    const float TURN_BRAKE_EXP       = 1.10f;

    // --- Snap / sleep near target ---
    const float SNAP_STOP_DIST_SQ = 0.1f;

    // --- Rotation (visual yaw) ---
    const bool  AUTO_FACE_WHEN_IDLE     = true;  // when not moving, face nearest enemy in attack range
    const float MOVE_FACING_THRESHOLD   = 0.06f; // [m/s] prefer facing movement direction above this speed
    const float MAX_YAW_DEG_PER_SEC     = 180f;  // visual yaw cap
    const float ROT_YAW_WEIGHT_INFLUENCE = 1.20f; // ≥0
    const float ROT_YAW_SPEED_INFLUENCE  = 0.70f; // ≥0
    const float ROT_YAW_TURN_RATE_MULT   = 2.0f;  // visual yaw multiplier

    // --- Steering rotation (velocity alignment) ---
    const float ROT_STEER_SPEED_INFLUENCE  = 1.00f; // ≥0
    const float ROT_STEER_WEIGHT_INFLUENCE = 1.00f; // ≥0
    const float ROT_STEER_TURN_RATE_MULT   = 3.0f;

    // ===================== HELPERS =====================

    static float ComputeTurnRateRadPerSec(float currentSpeed, float topSpeed, float massKg)
    {
        const float turnRateAtZero = 7f;   // rad/s
        const float turnRateAtRun  = 4f;   // rad/s

        float speedFrac = math.saturate(currentSpeed / math.max(0.1f, topSpeed));
        float t = math.lerp(0f, speedFrac, math.max(0f, ROT_STEER_SPEED_INFLUENCE));

        const float refMass = 80f;
        const float massExp = 0.25f;
        float massScale = math.pow(refMass / math.max(1f, massKg), massExp); // heavier → smaller
        massScale = math.clamp(massScale, 0.8f, 1.25f);
        massScale = math.lerp(1f, massScale, math.max(0f, ROT_STEER_WEIGHT_INFLUENCE));

        float baseRate = math.lerp(turnRateAtZero, turnRateAtRun, t);
        return baseRate * massScale; // rad/s
    }

    static float ComputeYawTurnRateRadPerSec(float currentSpeed, float topSpeed, float massKg)
    {
        const float turnRateAtZero = 7f;  // rad/s
        const float turnRateAtRun  = 4f;  // rad/s

        float speedFrac = math.saturate(currentSpeed / math.max(0.1f, topSpeed));
        float t = math.lerp(0f, speedFrac, math.max(0f, ROT_YAW_SPEED_INFLUENCE));

        const float refMass = 80f;
        const float massExp = 0.25f;
        float massScale = math.pow(refMass / math.max(1f, massKg), massExp);
        massScale = math.clamp(massScale, 0.8f, 1.25f);
        massScale = math.lerp(1f, massScale, math.max(0f, ROT_YAW_WEIGHT_INFLUENCE));

        float baseRate = math.lerp(turnRateAtZero, turnRateAtRun, t);
        return baseRate * massScale; // rad/s
    }

    static float GetCurrentYaw(quaternion rot)
    {
        float3 fwd = math.rotate(rot, new float3(0f, 0f, 1f));
        return math.atan2(fwd.x, fwd.z);
    }

    static void RotateYawToward(ref LocalTransform lt, float targetYaw, float maxDeltaYaw)
    {
        float currentYaw = GetCurrentYaw(lt.Rotation);
        float delta = targetYaw - currentYaw;
        delta = math.atan2(math.sin(delta), math.cos(delta)); // wrap to [-pi, pi]
        float applied = math.clamp(delta, -maxDeltaYaw, maxDeltaYaw);
        float newYaw = currentYaw + applied;
        lt.Rotation = quaternion.RotateY(newYaw);
    }
    // =====================================================================

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        bool isInGame = CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld());
        if (!isInGame) return;

        float physicsDt = SystemAPI.Time.DeltaTime;
        float deltaTime = SystemAPI.Time.DeltaTime;

        if (SystemAPI.TryGetSingleton<ClientServerTickRate>(out var tickRate) && tickRate.SimulationTickRate > 0)
            physicsDt = 1f / (float)tickRate.SimulationTickRate;

        bool haveCollisionWorld = SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var pws);
        CollisionWorld collisionWorld = haveCollisionWorld ? pws.CollisionWorld : default;
        bool skipDampingComp = SystemAPI.Time.ElapsedTime < 0.05;

        foreach ((
            RefRW<LocalTransform> localTransform,
            RefRW<PhysicsVelocity> physicsVelocity,
            RefRO<UnitStats> unitStats,
            RefRO<UnitModifiers> unitModifiers,
            RefRW<UnitTargets> unitTargets,
            Entity unitEntity
        ) in SystemAPI.Query<
                RefRW<LocalTransform>,
                RefRW<PhysicsVelocity>,
                RefRO<UnitStats>,
                RefRO<UnitModifiers>,
                RefRW<UnitTargets>
            >().WithAll<Simulate>().WithEntityAccess())
        {
            // ================= RUNTIME FETCH =================

            float minDistance = MIN_DISTANCE;
            float stickRadius = MIN_DISTANCE * STICK_RADIUS_MULT;

            float3 goalPosition = unitTargets.ValueRO.destinationPosition;
            float goalRotation = unitTargets.ValueRO.destinationRotation;

            float3 toTarget = goalPosition - localTransform.ValueRO.Position;
            float distSq = math.lengthsq(toTarget);

            bool haveFacingFromTarget = math.isfinite(unitTargets.ValueRO.targetRotation);
            bool targetInRangeForFacing = false;

            if (SystemAPI.HasComponent<CombatStats>(unitEntity) && haveFacingFromTarget)
            {
                var combatStats = SystemAPI.GetComponentRO<CombatStats>(unitEntity);
                float effectiveAttackRange = combatStats.ValueRO.attackRange - 0.2f;
                effectiveAttackRange = math.max(0f, effectiveAttackRange);
                float effectiveAttackRangeSq = effectiveAttackRange * effectiveAttackRange;

                float3 toTargetNow = unitTargets.ValueRO.targetPosition - localTransform.ValueRO.Position;
                toTargetNow.y = 0f;
                float d2 = math.lengthsq(toTargetNow);
                if (d2 >= 1e-6f && d2 <= effectiveAttackRangeSq)
                {
                    targetInRangeForFacing = true;
                }
            }

            // ================= STICKY ARRIVAL =================
            if (unitTargets.ValueRO.hasArrived)
            {
                bool chasing = unitTargets.ValueRO.targetEntity != Entity.Null;
                if (!chasing && SystemAPI.HasComponent<Attacker>(unitEntity))
                {
                    var att = SystemAPI.GetComponentRO<Attacker>(unitEntity).ValueRO;
                    bool hasTargetFocus = math.isfinite(unitTargets.ValueRO.targetRotation);
                    chasing =
                        hasTargetFocus &&
                        (
                            (att.attackMove && unitTargets.ValueRO.activeTargetSet) ||
                            (!unitTargets.ValueRO.activeTargetSet && att.autoTarget)
                        );
                }

                if (distSq <= stickRadius * stickRadius &&
                    !unitTargets.ValueRO.activeTargetSet &&
                    !chasing)
                {
                    if (math.length(physicsVelocity.ValueRO.Linear) <= MOVE_FACING_THRESHOLD)
                        physicsVelocity.ValueRW.Linear = float3.zero;
                    physicsVelocity.ValueRW.Angular = float3.zero;

                    // rotation while sticking
                    {
                        float speedNow = math.length(physicsVelocity.ValueRO.Linear);
                        float3 pos = localTransform.ValueRO.Position;
                        float targetYaw = GetCurrentYaw(localTransform.ValueRO.Rotation); // default: keep

                        bool hasTargetFocus = math.isfinite(unitTargets.ValueRO.targetRotation);
                        bool hasExplicitFocus = unitTargets.ValueRO.targetEntity != Entity.Null && hasTargetFocus;

                        // keep your existing gate for auto-focus (A-move / idle auto)
                        bool faceFocusWhenIdleOrAMove = false;
                        if (SystemAPI.HasComponent<Attacker>(unitEntity))
                        {
                            var att = SystemAPI.GetComponentRO<Attacker>(unitEntity).ValueRO;
                            faceFocusWhenIdleOrAMove =
                                (att.attackMove && unitTargets.ValueRO.activeTargetSet) ||
                                (!unitTargets.ValueRO.activeTargetSet && att.autoTarget);
                        }

                        if (speedNow >= MOVE_FACING_THRESHOLD)
                        {
                            float3 dir = math.normalizesafe(physicsVelocity.ValueRO.Linear, new float3(0f, 0f, 1f));
                            dir.y = 0f;
                            if (math.lengthsq(dir) > 1e-12f) targetYaw = math.atan2(dir.x, dir.z);
                        }
                        else if (hasExplicitFocus)
                        {
                            // Explicit follow (tree/building/unit) → face the focus center
                            targetYaw = unitTargets.ValueRO.targetRotation;
                        }
                        else if (hasTargetFocus && faceFocusWhenIdleOrAMove)
                        {
                            // Auto-focus (A-move/idle auto)
                            float3 toF = unitTargets.ValueRO.targetPosition - pos; toF.y = 0f;
                            if (math.lengthsq(toF) > 1e-12f) targetYaw = math.atan2(toF.x, toF.z);
                        }
                        else if (targetInRangeForFacing)
                        {
                            float3 toTgt = unitTargets.ValueRO.targetPosition - pos; toTgt.y = 0f;
                            if (math.lengthsq(toTgt) > 1e-12f) targetYaw = math.atan2(toTgt.x, toTgt.z);
                        }
                        else if (math.isfinite(goalRotation))
                        {
                            targetYaw = goalRotation;
                        }

                        float moveSpeedForTurn = unitStats.ValueRO.moveSpeed * unitModifiers.ValueRO.moveSpeedMultiplier;
                        float massKgForTurn = math.max(0.1f, unitStats.ValueRO.weight);
                        if (SystemAPI.HasComponent<PhysicsMass>(unitEntity))
                        {
                            float invMass = SystemAPI.GetComponentRO<PhysicsMass>(unitEntity).ValueRO.InverseMass;
                            if (invMass > 0f) massKgForTurn = 1f / invMass;
                        }
                        float turnRate = ComputeYawTurnRateRadPerSec(speedNow, math.max(0.1f, moveSpeedForTurn), massKgForTurn) * ROT_YAW_TURN_RATE_MULT;
                        float globalCap = math.radians(MAX_YAW_DEG_PER_SEC);
                        if (globalCap > 0f) turnRate = math.min(turnRate, globalCap);
                        float maxDeltaYaw = turnRate * deltaTime;

                        RotateYawToward(ref localTransform.ValueRW, targetYaw, maxDeltaYaw);
                    }

                    continue;
                }
                else
                {
                    unitTargets.ValueRW.hasArrived = false;
                }
            }

            // ================= ARRIVAL TRIGGER =================
            if (distSq <= minDistance * minDistance)
            {
                bool chasing = unitTargets.ValueRO.targetEntity != Entity.Null;
                if (!chasing && SystemAPI.HasComponent<Attacker>(unitEntity))
                {
                    var att = SystemAPI.GetComponentRO<Attacker>(unitEntity).ValueRO;
                    bool hasTargetFocus = math.isfinite(unitTargets.ValueRO.targetRotation);
                    chasing =
                        hasTargetFocus &&
                        (
                            (att.attackMove && unitTargets.ValueRO.activeTargetSet) ||
                            (!unitTargets.ValueRO.activeTargetSet && att.autoTarget)
                        );
                }

                if (!chasing)
                {
                    unitTargets.ValueRW.hasArrived = true;

                    bool keepAMove = false;
                    if (SystemAPI.HasComponent<Attacker>(unitEntity))
                        keepAMove = SystemAPI.GetComponentRO<Attacker>(unitEntity).ValueRO.attackMove;

                    if (!keepAMove)
                    {
                        unitTargets.ValueRW.activeTargetSet = false;

                        if (SystemAPI.HasComponent<UnitTargetsNetcode>(unitEntity))
                        {
                            var net = SystemAPI.GetComponentRW<UnitTargetsNetcode>(unitEntity);
                            net.ValueRW.requestActiveTargetSet = false;
                        }
                    }

                    physicsVelocity.ValueRW.Linear  = float3.zero;
                    physicsVelocity.ValueRW.Angular = float3.zero;

                    // arrival facing
                    {
                        float speedNow = 0f; // we just zeroed linear
                        float3 pos = localTransform.ValueRO.Position;
                        float targetYaw = GetCurrentYaw(localTransform.ValueRO.Rotation);

                        bool hasTargetFocus = math.isfinite(unitTargets.ValueRO.targetRotation);
                        bool hasExplicitFocus = unitTargets.ValueRO.targetEntity != Entity.Null && hasTargetFocus;

                        bool faceFocusWhenIdleOrAMove = false;
                        if (SystemAPI.HasComponent<Attacker>(unitEntity))
                        {
                            var att = SystemAPI.GetComponentRO<Attacker>(unitEntity).ValueRO;
                            faceFocusWhenIdleOrAMove =
                                (att.attackMove && unitTargets.ValueRO.activeTargetSet) ||
                                (!unitTargets.ValueRO.activeTargetSet && att.autoTarget);
                        }

                        if (hasExplicitFocus)
                        {
                            targetYaw = unitTargets.ValueRO.targetRotation;
                        }
                        else if (hasTargetFocus && faceFocusWhenIdleOrAMove)
                        {
                            float3 toF = unitTargets.ValueRO.targetPosition - pos; toF.y = 0f;
                            if (math.lengthsq(toF) > 1e-12f) targetYaw = math.atan2(toF.x, toF.z);
                        }
                        else if (targetInRangeForFacing)
                        {
                            float3 toTgt = unitTargets.ValueRO.targetPosition - pos; toTgt.y = 0f;
                            if (math.lengthsq(toTgt) > 1e-12f) targetYaw = math.atan2(toTgt.x, toTgt.z);
                        }
                        else if (math.isfinite(goalRotation))
                        {
                            targetYaw = goalRotation;
                        }

                        float moveSpeedForTurn = unitStats.ValueRO.moveSpeed * unitModifiers.ValueRO.moveSpeedMultiplier;
                        float massKgForTurn = math.max(0.1f, unitStats.ValueRO.weight);
                        if (SystemAPI.HasComponent<PhysicsMass>(unitEntity))
                        {
                            float invMass = SystemAPI.GetComponentRO<PhysicsMass>(unitEntity).ValueRO.InverseMass;
                            if (invMass > 0f) massKgForTurn = 1f / invMass;
                        }
                        float turnRate = ComputeYawTurnRateRadPerSec(speedNow, math.max(0.1f, moveSpeedForTurn), massKgForTurn) * ROT_YAW_TURN_RATE_MULT;
                        float globalCap = math.radians(MAX_YAW_DEG_PER_SEC);
                        if (globalCap > 0f) turnRate = math.min(turnRate, globalCap);
                        float maxDeltaYaw = turnRate * deltaTime;

                        RotateYawToward(ref localTransform.ValueRW, targetYaw, maxDeltaYaw);
                    }

                    continue;
                }
                else
                {
                    // chasing -> do not park; let movement core run
                    unitTargets.ValueRW.hasArrived = false;
                }
            }

            // ================= MOVE VECTOR & PRE-FACING (choose steer point) =================
            float3 position = localTransform.ValueRO.Position;

            bool allowTargetSteer = unitTargets.ValueRO.targetEntity != Entity.Null; // explicit follow always steers
            if (!allowTargetSteer && SystemAPI.HasComponent<Attacker>(unitEntity))
            {
                var att = SystemAPI.GetComponentRO<Attacker>(unitEntity).ValueRO;
                if (att.attackMove && unitTargets.ValueRO.activeTargetSet)
                    allowTargetSteer = true;
                else if (!unitTargets.ValueRO.activeTargetSet && att.autoTarget)
                    allowTargetSteer = true;
            }

            bool hasTargetFocusSteer = math.isfinite(unitTargets.ValueRO.targetRotation);

            float3 steerPoint = (allowTargetSteer && hasTargetFocusSteer)
                ? unitTargets.ValueRO.targetPosition
                : goalPosition;

            float3 toSteer = steerPoint - position; toSteer.y = 0f;
            float steerDistSq = math.lengthsq(toSteer);

            // === What are we steering toward? ===
            bool steeringToFocus = (allowTargetSteer && hasTargetFocusSteer);
            bool steeringToEnemyUnit = false;

            if (steeringToFocus && unitTargets.ValueRO.targetEntity != Entity.Null)
            {
                var t = unitTargets.ValueRO.targetEntity;

                if (SystemAPI.HasComponent<Unit>(t))
                {
                    if (SystemAPI.TryGetSingleton<FactionRelations>(out var rel) &&
                        SystemAPI.TryGetSingleton<FactionCount>(out var fCount) &&
                        SystemAPI.HasComponent<Faction>(unitEntity) &&
                        SystemAPI.HasComponent<Faction>(t))
                    {
                        byte meF = SystemAPI.GetComponent<Faction>(unitEntity).FactionId;
                        byte tgF = SystemAPI.GetComponent<Faction>(t).FactionId;
                        steeringToEnemyUnit = FactionUtility.AreHostile(meF, tgF, rel, fCount.Value);
                    }
                    else if (SystemAPI.HasComponent<GhostOwner>(unitEntity) && SystemAPI.HasComponent<GhostOwner>(t))
                    {
                        int me = SystemAPI.GetComponent<GhostOwner>(unitEntity).NetworkId;
                        int tg = SystemAPI.GetComponent<GhostOwner>(t).NetworkId;
                        steeringToEnemyUnit = (me != tg) && (me != int.MinValue) && (tg != int.MinValue);
                    }
                }
            }

            bool isCharging = SystemAPI.HasComponent<Attacker>(unitEntity)
                && SystemAPI.GetComponentRO<Attacker>(unitEntity).ValueRO.isCharging;

            float3 moveDirection = (steerDistSq > 0.0001f)
                ? (toSteer / math.sqrt(steerDistSq))
                : float3.zero;

            // ================= MOVEMENT CORE =================

            float speedMultiplier = unitModifiers.ValueRO.moveSpeedMultiplier;
            float moveSpeed = unitStats.ValueRO.moveSpeed;

            if (SystemAPI.HasComponent<Attacker>(unitEntity) && SystemAPI.HasComponent<CombatStats>(unitEntity))
            {
                var attacker = SystemAPI.GetComponentRO<Attacker>(unitEntity);
                if (attacker.ValueRO.attackDurationTimeLeft > 0f)
                {
                    var combatStats = SystemAPI.GetComponentRO<CombatStats>(unitEntity).ValueRO;
                    float slow = math.saturate(combatStats.attackSlowdown);
                    moveSpeed *= slow;
                }
            }
            moveSpeed *= speedMultiplier;

            float massKg = math.max(0.1f, unitStats.ValueRO.weight);
            if (SystemAPI.HasComponent<PhysicsMass>(unitEntity))
            {
                float invMass = SystemAPI.GetComponentRO<PhysicsMass>(unitEntity).ValueRO.InverseMass;
                if (invMass > 0f) massKg = 1f / invMass;
            }
            float massScale = math.pow(massKg / REF_MASS_KG, MASS_EXPONENT);
            float accelPerSecond = math.clamp(ACCEL_AT_REF / math.max(0.01f, massScale), MIN_ACCEL, MAX_ACCEL);
            float decelPerSecond = math.clamp(DECEL_AT_REF / math.max(0.01f, massScale), MIN_DECEL, MAX_DECEL);

            float3 vNow = physicsVelocity.ValueRO.Linear;
            float vNowMag = math.length(vNow);
            float3 vNowDir = math.normalizesafe(vNow, float3.zero);

            float maxTurnRate = ComputeTurnRateRadPerSec(vNowMag, math.max(0.1f, moveSpeed), massKg) * ROT_STEER_TURN_RATE_MULT;
            float maxAngle = maxTurnRate * deltaTime;

            float3 inputDir = math.normalizesafe(moveDirection, float3.zero);
            float3 desiredDir = inputDir;
            float headingAngle = 0f;

            if (math.lengthsq(vNowDir) > 1e-12f && math.lengthsq(inputDir) > 1e-12f)
            {
                float cosAng = math.clamp(math.dot(vNowDir, inputDir), -1f, 1f);
                headingAngle = math.acos(cosAng);

                if (headingAngle > 1e-6f)
                {
                    float t = math.saturate(maxAngle / headingAngle);
                    float3 blended = vNowDir * (1f - t) + inputDir * t;
                    desiredDir = math.normalizesafe(blended, inputDir);
                }
            }
            if (math.lengthsq(vNow) < 1e-6f)
                desiredDir = inputDir;

            float targetSpeed = (math.lengthsq(moveDirection) > 1e-8f) ? moveSpeed : 0f;

            float cosHeading = (headingAngle > 0f) ? math.cos(headingAngle) : 1f;
            float headingScale = (cosHeading > 0f) ? math.pow(cosHeading, HEADING_CURVE_EXP) : 0f;
            if (headingAngle < math.radians(TURN_BRAKE_START_DEG))
                if (headingScale < HEADING_THROTTLE_FLOOR) headingScale = HEADING_THROTTLE_FLOOR;
            targetSpeed *= headingScale;

            // === Arrival / braking (gated by charge override) ===
            if (distSq <= ARRIVE_MAX_DIST * ARRIVE_MAX_DIST)
            {
                bool skipCap = steeringToFocus && steeringToEnemyUnit && isCharging;
                if (!skipCap)
                {
                    // Use steer distance when following; else destination distance
                    float distanceForArrivalSq = steeringToFocus ? steerDistSq : distSq;

                    float v = vNowMag;
                    float a = math.max(0.01f, decelPerSecond);
                    float dStop = (v * v) / (2f * a);
                    float rPhys = ARRIVE_MIN_DIST + ARRIVE_SAFETY * dStop;

                    float speedFrac = (moveSpeed > 1e-4f) ? math.saturate(v / moveSpeed) : 0f;
                    float wRel = (massKg / REF_MASS_KG) - 1f;
                    float rScaled = rPhys
                                    * (1f + ARRIVE_SPEED_INFLUENCE * speedFrac)
                                    * (1f + ARRIVE_WEIGHT_INFLUENCE * wRel);

                    float arrivalRadius = math.clamp(rScaled, ARRIVE_MIN_DIST, ARRIVE_MAX_DIST);

                    float distForCap = math.sqrt(distanceForArrivalSq);
                    if (distForCap <= arrivalRadius)
                    {
                        float p = math.lerp(1f, 3f, math.saturate(ARRIVE_CAP_CURVE));
                        float s = math.saturate(distForCap / math.max(0.001f, arrivalRadius));
                        float vCap = moveSpeed * math.pow(s, p);

                        // Keep a floor ONLY when explicitly charging through an enemy unit.
                        // Otherwise (trees/buildings/friendlies, or enemy units when not charging) allow full stop.
                        bool chargeThrough = (steeringToFocus && steeringToEnemyUnit && isCharging);
                        float speedFloor   = chargeThrough ? ARRIVE_SPEED_FLOOR_MPS : 0f;

                        vCap = math.max(vCap, speedFloor);
                        targetSpeed = math.min(targetSpeed, vCap);
                    }
                }
            }

            float vParallelMag = 0f;
            float3 vParallel = float3.zero;
            float3 vLateral = vNow;
            if (math.lengthsq(desiredDir) > 1e-12f)
            {
                vParallelMag = math.dot(vNow, desiredDir);
                vParallel = desiredDir * vParallelMag;
                vLateral = vNow - vParallel;
            }

            float damping = 0f;
            if (SystemAPI.HasComponent<PhysicsDamping>(unitEntity))
                damping = SystemAPI.GetComponentRO<PhysicsDamping>(unitEntity).ValueRO.Linear;

            float desiredForwardDelta = targetSpeed - vParallelMag;
            float maxDeltaVForward = ((desiredForwardDelta >= 0f) ? accelPerSecond : decelPerSecond) * deltaTime;
            desiredForwardDelta = math.clamp(desiredForwardDelta, -maxDeltaVForward, maxDeltaVForward);
            float3 deltaVForward = desiredDir * desiredForwardDelta;

            float3 deltaVLateral = float3.zero;
            float lateralSpeed = math.length(vLateral);
            if (lateralSpeed > 1e-6f)
            {
                float speedFrac2 = vNowMag / math.max(0.1f, moveSpeed);
                speedFrac2 = math.clamp(speedFrac2, 0f, 1f);
                speedFrac2 = math.pow(speedFrac2, LATERAL_KILL_EXP);
                float speedKillScale = math.lerp(LATERAL_KILL_MIN, LATERAL_KILL_MAX, speedFrac2);
                float massFactor = math.pow(REF_MASS_KG / math.max(1f, massKg), LATERAL_MASS_EXP);
                float maxDeltaVLateral = accelPerSecond * speedKillScale * massFactor * deltaTime;

                float killAmount = math.min(lateralSpeed, maxDeltaVLateral);
                float3 lateralDir = vLateral / lateralSpeed;
                deltaVLateral = -lateralDir * killAmount;
            }

            if (headingAngle > math.radians(TURN_BRAKE_START_DEG) && math.lengthsq(moveDirection) > 1e-8f)
            {
                float speedNowTB = vNowMag;
                if (speedNowTB > 1e-6f)
                {
                    float h01 = math.saturate((headingAngle - math.radians(TURN_BRAKE_START_DEG)) / math.radians(TURN_BRAKE_RANGE_DEG));
                    h01 = math.pow(h01, TURN_BRAKE_EXP);

                    float brakePerSecond = decelPerSecond * TURN_BRAKE_STRENGTH * h01;
                    float maxBrake = brakePerSecond * deltaTime;
                    float brake = math.min(speedNowTB, maxBrake);

                    float3 dir = math.normalizesafe(vNow, float3.zero);
                    deltaVLateral += dir * -brake;
                }
            }

            if (math.lengthsq(moveDirection) <= 1e-8f)
            {
                float speedNowIB = vNowMag;
                if (speedNowIB > 1e-6f)
                {
                    float maxBrake = decelPerSecond * deltaTime;
                    float brake = math.min(speedNowIB, maxBrake);
                    float3 dir = math.normalizesafe(vNow, float3.zero);
                    deltaVLateral += dir * -brake;
                }
            }

            float3 vNew = vNow + deltaVForward + deltaVLateral;

            // === Snap / stop — gated like the arrival cap ===
            {
                float snapDistSq = steeringToFocus ? steerDistSq : distSq;
                bool snapEnabled = !(steeringToFocus && steeringToEnemyUnit && isCharging);

                if (snapEnabled && snapDistSq < SNAP_STOP_DIST_SQ)
                {
                    vNew = float3.zero;
                }
                else
                {
                    if (!skipDampingComp && damping > 1e-6f && math.lengthsq(vNew) > 1e-12f)
                    {
                        float k = 1f - damping * physicsDt;
                        if (k > 0.0f)
                            vNew /= k;
                    }
                }
            }

            // ================= ROTATION (VISUAL YAW) =================
            {
                float speedNow = math.length(vNew);
                float3 pos = localTransform.ValueRO.Position;
                float targetYaw = GetCurrentYaw(localTransform.ValueRO.Rotation);

                bool hasTargetFocus = math.isfinite(unitTargets.ValueRO.targetRotation);
                bool hasExplicitFocus = unitTargets.ValueRO.targetEntity != Entity.Null && hasTargetFocus;

                bool faceFocusWhenIdleOrAMove = false;
                if (SystemAPI.HasComponent<Attacker>(unitEntity))
                {
                    var att = SystemAPI.GetComponentRO<Attacker>(unitEntity).ValueRO;
                    faceFocusWhenIdleOrAMove =
                        (att.attackMove && unitTargets.ValueRO.activeTargetSet) ||
                        (!unitTargets.ValueRO.activeTargetSet && att.autoTarget);
                }

                if (speedNow >= MOVE_FACING_THRESHOLD)
                {
                    float3 dir = math.normalizesafe(vNew, new float3(0f, 0f, 1f));
                    dir.y = 0f;
                    if (math.lengthsq(dir) > 1e-12f) targetYaw = math.atan2(dir.x, dir.z);
                }
                else if (hasExplicitFocus)
                {
                    targetYaw = unitTargets.ValueRO.targetRotation;
                }
                else if (hasTargetFocus && faceFocusWhenIdleOrAMove)
                {
                    float3 toF = unitTargets.ValueRO.targetPosition - pos; toF.y = 0f;
                    if (math.lengthsq(toF) > 1e-12f) targetYaw = math.atan2(toF.x, toF.z);
                }
                else if (targetInRangeForFacing)
                {
                    float3 toTgt = unitTargets.ValueRO.targetPosition - pos; toTgt.y = 0f;
                    if (math.lengthsq(toTgt) > 1e-12f) targetYaw = math.atan2(toTgt.x, toTgt.z);
                }
                else if (math.isfinite(goalRotation))
                {
                    targetYaw = goalRotation;
                }

                float turnRate = ComputeYawTurnRateRadPerSec(speedNow, math.max(0.1f, moveSpeed), massKg) * ROT_YAW_TURN_RATE_MULT;
                float globalCap = math.radians(MAX_YAW_DEG_PER_SEC);
                if (globalCap > 0f) turnRate = math.min(turnRate, globalCap);
                float maxDeltaYaw = turnRate * deltaTime;

                RotateYawToward(ref localTransform.ValueRW, targetYaw, maxDeltaYaw);
            }

            physicsVelocity.ValueRW.Linear = vNew;
            physicsVelocity.ValueRW.Angular = float3.zero;
        }
    }
}