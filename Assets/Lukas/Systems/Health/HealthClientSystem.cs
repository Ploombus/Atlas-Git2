/*
using Unity.NetCode;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
public partial struct HealthClientSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<HealthStageTableSingleton>()) return;
        ref var table = ref SystemAPI.GetSingleton<HealthStageTableSingleton>().Table.Value;

        foreach (var (healthState, unitModifiers, entity)
                 in SystemAPI.Query<RefRO<HealthState>, RefRW<UnitModifiers>>()
                              .WithEntityAccess())
        {
            float speedMultiplier = 1f;

            if (SystemAPI.HasComponent<GhostOwnerIsLocal>(entity))
            {
                // Predicted: current + pending change
                var predictedStage = HealthStageUtil.ApplyDelta(
                    healthState.ValueRO.currentStage,
                    healthState.ValueRO.healthChange,
                    HealthStage.Dead, HealthStage.Healthy);

                ref var entries = ref table.entries;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].stage == predictedStage)
                    {
                        speedMultiplier = entries[i].moveSpeedMultiplier;
                        break;
                    }
                }
            }
            else
            {
                // Non-owner: use replicated stage directly
                var replicatedStage = healthState.ValueRO.currentStage;

                ref var entries = ref table.entries;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].stage == replicatedStage)
                    {
                        speedMultiplier = entries[i].moveSpeedMultiplier;
                        break;
                    }
                }
            }

            unitModifiers.ValueRW.moveSpeedMultiplier = speedMultiplier;
        }
    }
}
*/