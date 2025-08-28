/*
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(UnitAnimateSystem))]
public partial struct UpperBodyFacingSystem : ISystem
{
    const float UpperClampDeg            = 70f;   // max torso twist left/right
    const float UpperTurnRateDegPerSec   = 540f;  // how fast torso chases desired yaw
    const float ReturnRateDegPerSec      = 360f;  // how fast torso recenters when no aim
    const float DeadZoneDeg              = 1.0f;  // snap small jitters to zero

    static float GetYawRad(quaternion q)
    {
        float3 fwd = math.forward(q);
        return math.atan2(fwd.x, fwd.z);
    }

    static float DeltaDegToward(float current, float target, float maxStep)
    {
        // shortest-path delta in degrees
        float delta = Mathf.DeltaAngle(current, target);
        delta = Mathf.Clamp(delta, -maxStep, +maxStep);
        return current + delta;
    }

    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (ltRO, attackerRO, pivotRef, twistRW) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<Attacker>, UpperBodyPivotReference, RefRW<UpperBodyTwistState>>())
        {
            var pivot = pivotRef.Pivot;
            if (pivot == null) continue;

            float legYawDeg = math.degrees(GetYawRad(ltRO.ValueRO.Rotation));

            // Desired upper yaw: aim when valid, else follow legs
            float desiredUpperYawDeg = legYawDeg;
            if (math.isfinite(attackerRO.ValueRO.aimRotation))
                desiredUpperYawDeg = math.degrees(attackerRO.ValueRO.aimRotation);

            // Desired *relative* twist from legs
            float desiredTwistDeg = Mathf.DeltaAngle(legYawDeg, desiredUpperYawDeg);

            // Clamp twist to safe range
            desiredTwistDeg = Mathf.Clamp(desiredTwistDeg, -UpperClampDeg, +UpperClampDeg);

            // Smooth toward desired (faster when actively aiming, slower when returning)
            float rate = math.isfinite(attackerRO.ValueRO.aimRotation) ? UpperTurnRateDegPerSec : ReturnRateDegPerSec;
            float nextTwist = DeltaDegToward(twistRW.ValueRO.CurrentYawOffsetDeg, desiredTwistDeg, rate * dt);

            // Kill tiny jitter
            if (Mathf.Abs(nextTwist) < DeadZoneDeg) nextTwist = 0f;

            // Apply: pivot local rot = base * yaw(nextTwist)
            pivot.localRotation = pivotRef.BaseLocalRotation * Quaternion.AngleAxis(nextTwist, Vector3.up);

            // Cache state
            twistRW.ValueRW.CurrentYawOffsetDeg = nextTwist;
        }
    }
}
*/