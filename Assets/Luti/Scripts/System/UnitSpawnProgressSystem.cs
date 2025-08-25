using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// BULLETPROOF simple system - tracks spawn requests directly
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct UnitSpawnProgressSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var manager = UnitSpawnProgressManager.Instance;
        if (manager == null) return;

        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        float deltaTime = SystemAPI.Time.DeltaTime;

        // Count new spawn requests per building
        var spawnCounts = new System.Collections.Generic.Dictionary<Entity, int>();
        foreach (var rpc in SystemAPI.Query<RefRO<SpawnUnitFromBuildingRpc>>())
        {
            var building = rpc.ValueRO.buildingEntity;
            if (spawnCounts.ContainsKey(building))
                spawnCounts[building]++;
            else
                spawnCounts[building] = 1;
        }

        // Add spawn requests to existing progress or create new
        foreach (var kvp in spawnCounts)
        {
            var building = kvp.Key;
            var newSpawns = kvp.Value;

            if (SystemAPI.HasComponent<SpawnProgress>(building))
            {
                // Add to existing queue
                var progress = SystemAPI.GetComponent<SpawnProgress>(building);
                progress.unitsInQueue += newSpawns;
                buffer.SetComponent(building, progress);
            }
            else
            {
                // Create new progress tracker
                buffer.AddComponent(building, new SpawnProgress
                {
                    currentTime = 0f,
                    unitsInQueue = newSpawns
                });
            }
        }

        // Update all buildings with progress
        foreach (var (progress, transform, entity) in
                 SystemAPI.Query<RefRW<SpawnProgress>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            if (progress.ValueRO.unitsInQueue > 0)
            {
                // Update spawn timer
                progress.ValueRW.currentTime += deltaTime;

                // Calculate progress (3 seconds per unit) - needs change to not hardcode 3 seconds
                float progressPercent = (progress.ValueRO.currentTime / 3.0f) * 100f;
                if (progressPercent > 100f) progressPercent = 100f;

                int percent = Mathf.RoundToInt(progressPercent);
                string text = $"SPAWNING {percent}%\nQueue: {progress.ValueRO.unitsInQueue}";

                var position = transform.ValueRO.Position;
                manager.ShowText(entity, position, text);

                // Check if unit finished spawning
                if (progress.ValueRO.currentTime >= 3.0f)
                {
                    // Unit spawned, decrease queue and reset timer
                    progress.ValueRW.unitsInQueue--;
                    progress.ValueRW.currentTime = 0f;
                }
            }
            else
            {
                // No more units in queue, remove progress and hide text
                manager.HideText(entity);
                buffer.RemoveComponent<SpawnProgress>(entity);
            }
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}