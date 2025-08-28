using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct HealthServerSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // 1) Apply health deltas + queue despawns (server-authoritative)
        foreach (var (healthRW, e) in SystemAPI.Query<RefRW<HealthState>>().WithEntityAccess())
        {
            int delta = healthRW.ValueRO.healthChange;
            if (delta == 0) continue;

            var oldStage = healthRW.ValueRO.currentStage;
            var newStage = HealthStageUtil.ApplyDelta(oldStage, delta, HealthStage.Dead, HealthStage.Healthy);

            healthRW.ValueRW.previousStage = oldStage;
            healthRW.ValueRW.currentStage  = newStage;
            healthRW.ValueRW.healthChange  = 0;

            // Transitioned to Dead → schedule despawn once
            if (newStage == HealthStage.Dead && oldStage != HealthStage.Dead && !SystemAPI.HasComponent<PendingDespawn>(e))
                ecb.AddComponent(e, new PendingDespawn { seconds = 0.25f });
        }

        // 2) Update movement modifiers from health table (if available)
        if (SystemAPI.TryGetSingleton<HealthStageTableSingleton>(out var tableSingleton))
        {
            ref var table = ref tableSingleton.Table.Value;
            foreach (var (healthRO, modsRW) in SystemAPI.Query<RefRO<HealthState>, RefRW<UnitModifiers>>())
            {
                float mult = 1f;
                ref var entries = ref table.entries;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].stage == healthRO.ValueRO.currentStage)
                    {
                        mult = entries[i].moveSpeedMultiplier;
                        break;
                    }
                }
                modsRW.ValueRW.moveSpeedMultiplier = mult;
            }
        }

        // 3) Despawn countdown
        float dt = SystemAPI.Time.DeltaTime;
        foreach (var (pendingRW, e) in SystemAPI.Query<RefRW<PendingDespawn>>().WithEntityAccess())
        {
            pendingRW.ValueRW.seconds -= dt;
            if (pendingRW.ValueRO.seconds <= 0f)
                ecb.DestroyEntity(e);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}

public struct PendingDespawn : IComponentData { public float seconds; }