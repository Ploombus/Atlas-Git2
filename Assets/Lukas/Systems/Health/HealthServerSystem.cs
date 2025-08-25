using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct HealthSystem_Server : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        foreach (var (healthState, entity) in
                 SystemAPI.Query<RefRW<HealthState>>().WithEntityAccess())
        {
            int delta = healthState.ValueRO.healthChange;
            if (delta == 0) continue;

            var oldStage = healthState.ValueRO.currentStage;
            var newStage = HealthStageUtil.ApplyDelta(oldStage, delta, HealthStage.Dead, HealthStage.Healthy);
            healthState.ValueRW.currentStage = HealthStageUtil.ApplyDelta(oldStage, delta, HealthStage.Dead, HealthStage.Healthy);

            healthState.ValueRW.previousStage = oldStage;
            healthState.ValueRW.currentStage = newStage;
            healthState.ValueRW.healthChange = 0;

            //Dead
            if (newStage == HealthStage.Dead)
            {
                if (!SystemAPI.HasComponent<PendingDespawn>(entity))
                    buffer.AddComponent(entity, new PendingDespawn { seconds = 0.25f });
            }
        }

        if (!SystemAPI.HasSingleton<HealthStageTableSingleton>())
            return;

        ref var table = ref SystemAPI.GetSingleton<HealthStageTableSingleton>().Table.Value;

        // Modifiers
        foreach (var (healthState, unitModifiers) in
                 SystemAPI.Query<RefRO<HealthState>, RefRW<UnitModifiers>>())
        {
            float speedMultiplier = 1f;

            ref var entries = ref table.entries;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].stage == healthState.ValueRO.currentStage)
                {
                    speedMultiplier = entries[i].moveSpeedMultiplier;
                    break;
                }
            }

            unitModifiers.ValueRW.moveSpeedMultiplier = speedMultiplier;
        }

        //Cleanup
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (pending, entity) in
                SystemAPI.Query<RefRW<PendingDespawn>>().WithEntityAccess())
        {
            pending.ValueRW.seconds -= dt;
            if (pending.ValueRO.seconds <= 0f)
                buffer.DestroyEntity(entity);
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}

public struct PendingDespawn : IComponentData
{
    public float seconds;
}