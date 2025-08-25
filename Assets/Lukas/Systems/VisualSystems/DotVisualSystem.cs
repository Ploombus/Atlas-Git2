using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct DotVisualSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MovementDot>();
    }

    public void OnUpdate(ref SystemState state)
    {

        var unitTargetsLookup = SystemAPI.GetComponentLookup<UnitTargets>(true);
        var selectedLookup    = SystemAPI.GetComponentLookup<Selected>(true);
        var attackerLookup    = SystemAPI.GetComponentLookup<Attacker>(true); // exists only on combatants
        var transformLookup   = SystemAPI.GetComponentLookup<LocalTransform>(true);

        const float showDistanceThreshold = 0.5f; // hide dot if essentially at the unit

        foreach (var (dotTransform, dotData) in
         SystemAPI.Query<RefRW<LocalTransform>, RefRO<MovementDot>>())
        {
            Entity unit = dotData.ValueRO.owner;

            if (!transformLookup.HasComponent(unit) || !unitTargetsLookup.HasComponent(unit))
            {
                dotTransform.ValueRW.Scale = 0f;
                continue;
            }

            var unitXform = transformLookup[unit];
            var unitTargets = unitTargetsLookup[unit];

            // Early cull: not selected, arrived, or destination is essentially at the unit
            bool isSelected = selectedLookup.HasComponent(unit) && selectedLookup.IsComponentEnabled(unit);
            float distToDestSq = math.lengthsq(unitTargets.destinationPosition - unitXform.Position);

            if (!isSelected || unitTargets.hasArrived || distToDestSq <= (showDistanceThreshold * showDistanceThreshold))
            {
                dotTransform.ValueRW.Scale = 0f;
                continue;
            }

            // Decide which position to visualize
            float3 desiredDotPosition;
            bool isCombatant = attackerLookup.HasComponent(unit);

            if (isCombatant)
            {
                desiredDotPosition = unitTargets.destinationPosition;
            }
            else
            {
                float distDestVsTargetSq = math.lengthsq(unitTargets.targetPosition - unitTargets.destinationPosition);
                bool aiHasMeaningfulTask = distDestVsTargetSq > (showDistanceThreshold * showDistanceThreshold);
                desiredDotPosition = aiHasMeaningfulTask ? unitTargets.targetPosition : unitTargets.destinationPosition;
            }

            dotTransform.ValueRW.Position = desiredDotPosition;

            // Pulse (visible because we didn't early-cull)
            float time = (float)SystemAPI.Time.ElapsedTime;
            float scale = 0.15f + math.sin(time * 5f) * 0.05f;
            dotTransform.ValueRW.Scale = scale;
        }

    }
}