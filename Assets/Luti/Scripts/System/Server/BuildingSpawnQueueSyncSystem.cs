using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct BuildingSpawnQueueSyncSystem : ISystem
{

    public void OnUpdate(ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        int syncedCount = 0;
        int addedCount = 0;
        int updatedCount = 0;

        foreach (var (spawnQueue, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnQueue>>().WithEntityAccess())
        {
            syncedCount++;

            var clientData = new BuildingSpawnQueueClient
            {
                unitsInQueue = spawnQueue.ValueRO.unitsInQueue,
                isCurrentlySpawning = spawnQueue.ValueRO.isCurrentlySpawning
            };

            // Add or update the client component
            if (SystemAPI.HasComponent<BuildingSpawnQueueClient>(entity))
            {
                // Update existing client component
                buffer.SetComponent(entity, clientData);
                updatedCount++;
            }
            else
            {
                // Add new client component
                buffer.AddComponent(entity, clientData);
                addedCount++;
            }
        }

        // Remove client components for entities that no longer have server components
        int removedCount = 0;
        foreach (var (clientQueue, entity) in
                 SystemAPI.Query<RefRO<BuildingSpawnQueueClient>>().WithEntityAccess().WithNone<BuildingSpawnQueue>())
        {
            buffer.RemoveComponent<BuildingSpawnQueueClient>(entity);
            removedCount++;
        }

     

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}