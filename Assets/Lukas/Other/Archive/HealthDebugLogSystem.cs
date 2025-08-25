/*
using Unity.Entities;
using UnityEngine; // for Debug.Log

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HealthSystem_Server))] // log after server applied the change
public partial struct HealthDebugLogSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (healthState, entity) in
                 SystemAPI.Query<RefRO<HealthState>>()
                          .WithEntityAccess())
        {
            // Only log when a transition happened this tick
            if (healthState.ValueRO.previousStage != healthState.ValueRO.currentStage)
            {
                int deltaSteps =
                    (int)healthState.ValueRO.currentStage - (int)healthState.ValueRO.previousStage;

                Debug.Log(
                    $"[Health] Entity {entity.Index}: " +
                    $"{healthState.ValueRO.previousStage} -> {healthState.ValueRO.currentStage} " +
                    $"(delta {deltaSteps}) at t={SystemAPI.Time.ElapsedTime:0.00}s");
            }
        }
    }
}
*/