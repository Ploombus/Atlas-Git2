using Unity.Entities;
using Managers;
using Unity.NetCode;
using UnityEngine;
using Unity.Mathematics;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct UnitFromBuildingSpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EntitiesReferencesLukas>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetServerWorld()))
            return;

        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var unitReferences = SystemAPI.GetSingleton<EntitiesReferencesLukas>();
        float deltaTime = SystemAPI.Time.DeltaTime;
        const float timeToSpawnUnit = 3.0f;

        // Step 1: Handle new spawn unit from building requests (add to queue)
        HandleNewSpawnRequests(ref state, buffer, timeToSpawnUnit);

        // Step 2: Process buildings with spawn queues - start spawning if not already spawning
        StartSpawningFromQueues(ref state, buffer, timeToSpawnUnit);

        // Step 3: Process active pending spawns (countdown timers and spawn when ready)
        UpdateSpawnProgress(ref state, buffer, deltaTime, unitReferences);

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }

    private void HandleNewSpawnRequests(ref SystemState state, EntityCommandBuffer buffer, float timeToSpawnUnit)
    {
        foreach (var (rpc, request, rpcEntity)
        in SystemAPI.Query<RefRO<SpawnUnitFromBuildingRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
        {
            var connection = request.ValueRO.SourceConnection;
            var requesterNetId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            var buildingEntity = rpc.ValueRO.buildingEntity;

            // Check if the building exists
            if (SystemAPI.Exists(buildingEntity))
            {
                // Get building position to spawn unit nearby
                var buildingTransform = SystemAPI.GetComponent<LocalTransform>(buildingEntity);
                var buildingPosition = buildingTransform.Position;

                // Get building owner
                int buildingOwnerId = -1;
                if (SystemAPI.HasComponent<GhostOwner>(buildingEntity))
                {
                    buildingOwnerId = SystemAPI.GetComponent<GhostOwner>(buildingEntity).NetworkId;
                }

                // Security check
                if (buildingOwnerId != requesterNetId)
                {
                    Debug.LogWarning($"Player {requesterNetId} tried to spawn unit from building owned by {buildingOwnerId}");
                    buffer.DestroyEntity(rpcEntity);
                    continue;
                }

                // CHECK AND DEDUCT RESOURCES
                if (SystemAPI.HasComponent<UnitSpawnCost>(buildingEntity))
                {
                    var spawnCost = SystemAPI.GetComponent<UnitSpawnCost>(buildingEntity);

                    // Only check if there's actually a cost
                    if (spawnCost.unitResource1Cost > 0 || spawnCost.unitResource2Cost > 0)
                    {
                        // Try to spend resources using the helper method
                        Entity playerConnectionEntity = ServerPlayerStatsSystem.FindPlayerConnectionByNetworkId(ref state, requesterNetId);

                        if (playerConnectionEntity == Entity.Null)
                        {
                            Debug.LogWarning($"Player {requesterNetId} connection not found or no PlayerStats");
                            buffer.DestroyEntity(rpcEntity);
                            continue;
                        }

                        // Try to spend resources using the correct player connection entity
                        if (!ServerPlayerStatsSystem.TrySpendResources(ref state, buffer,
                            playerConnectionEntity, spawnCost.unitResource1Cost, spawnCost.unitResource2Cost))
                        {
                            Debug.Log($"Player {requesterNetId} cannot afford unit. Cost: R1:{spawnCost.unitResource1Cost}/R2:{spawnCost.unitResource2Cost}");
                            buffer.DestroyEntity(rpcEntity);
                            continue;
                        }

                    }
                }

                // Initialize or update building spawn queue
                if (SystemAPI.HasComponent<BuildingSpawnQueue>(buildingEntity))
                {
                    // Update existing queue
                    var spawnQueue = SystemAPI.GetComponent<BuildingSpawnQueue>(buildingEntity);
                    spawnQueue.unitsInQueue++;
                    buffer.SetComponent(buildingEntity, spawnQueue);
                }
                else
                {
                    // Initialize new queue
                    buffer.AddComponent(buildingEntity, new BuildingSpawnQueue
                    {
                        unitsInQueue = 1,
                        isCurrentlySpawning = false,
                        timeToSpawnUnit = timeToSpawnUnit
                    });
                }

                // Calculate spawn position
                float3 spawnPosition = FindValidSpawnPosition(buildingPosition, ref state);

                // Add to queue
                var queuedSpawnEntity = buffer.CreateEntity();
                buffer.AddComponent(queuedSpawnEntity, new QueuedUnitSpawn
                {
                    buildingEntity = buildingEntity,
                    ownerNetworkId = buildingOwnerId,
                    spawnPosition = spawnPosition
                });
            }
            else
            {
                Debug.LogWarning("Tried to spawn unit from non-existent building");
            }

            // consume RPC
            buffer.DestroyEntity(rpcEntity);
        }
    }

    private void StartSpawningFromQueues(ref SystemState state, EntityCommandBuffer buffer, float timeToSpawnUnit)
    {
        foreach (var (spawnQueue, buildingEntity) in
                 SystemAPI.Query<RefRW<BuildingSpawnQueue>>().WithEntityAccess())
        {
            // If building has queued units and is not currently spawning, start spawning
            if (spawnQueue.ValueRO.unitsInQueue > 0 && !spawnQueue.ValueRO.isCurrentlySpawning)
            {
                // Find the first queued spawn for this building
                foreach (var (queuedSpawn, queuedEntity) in
                         SystemAPI.Query<RefRO<QueuedUnitSpawn>>().WithEntityAccess())
                {
                    if (queuedSpawn.ValueRO.buildingEntity == buildingEntity)
                    {
                        // Convert queued spawn to active pending spawn
                        var pendingSpawnEntity = buffer.CreateEntity();
                        buffer.AddComponent(pendingSpawnEntity, new PendingUnitSpawn
                        {
                            buildingEntity = queuedSpawn.ValueRO.buildingEntity,
                            ownerNetworkId = queuedSpawn.ValueRO.ownerNetworkId,
                            spawnPosition = queuedSpawn.ValueRO.spawnPosition,
                            remainingTime = timeToSpawnUnit,
                            totalSpawnTime = timeToSpawnUnit
                        });

                        // Mark building as currently spawning and decrement queue
                        spawnQueue.ValueRW.isCurrentlySpawning = true;
                        spawnQueue.ValueRW.unitsInQueue--;

                        // Remove from queue
                        buffer.DestroyEntity(queuedEntity);

                        break; // Only process one unit at a time
                    }
                }
            }
        }
    }

    // Updates spawn progress and spawns units when timers complete
    private void UpdateSpawnProgress(ref SystemState state, EntityCommandBuffer buffer, float deltaTime, EntitiesReferencesLukas unitReferences)
    {
        foreach (var (pendingSpawn, pendingEntity) in
                 SystemAPI.Query<RefRW<PendingUnitSpawn>>().WithEntityAccess())
        {
            // Countdown the timer
            pendingSpawn.ValueRW.remainingTime -= deltaTime;

            // Check if it's time to spawn the unit
            if (pendingSpawn.ValueRO.remainingTime <= 0f)
            {
                var buildingEntity = pendingSpawn.ValueRO.buildingEntity;

                // Verify the building still exists
                if (SystemAPI.Exists(buildingEntity))
                {
                    // Spawn the unit
                    SpawnUnit(ref state, buffer, pendingSpawn.ValueRO, unitReferences);

                    // Update building spawn queue state
                    if (SystemAPI.HasComponent<BuildingSpawnQueue>(buildingEntity))
                    {
                        var spawnQueue = SystemAPI.GetComponent<BuildingSpawnQueue>(buildingEntity);
                        spawnQueue.isCurrentlySpawning = false;

                        // Always set the component, even if queue is empty
                        // This avoids trying to remove a component that might not exist
                        buffer.SetComponent(buildingEntity, spawnQueue);

                        // Remove progress component if queue is empty
                        if (spawnQueue.unitsInQueue <= 0 && SystemAPI.HasComponent<BuildingSpawnProgress>(buildingEntity))
                        {
                            buffer.RemoveComponent<BuildingSpawnProgress>(buildingEntity);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("Building was destroyed before unit could spawn");
                }

                // Remove the pending spawn entity
                buffer.DestroyEntity(pendingEntity);
            }
        }
    }

    private void SpawnUnit(ref SystemState state, EntityCommandBuffer buffer, PendingUnitSpawn spawnData, EntitiesReferencesLukas unitReferences)
    {
        // Create the unit entity
        var unitEntity = buffer.Instantiate(unitReferences.unitPrefabEntity);

        // Set unit position
        buffer.SetComponent(unitEntity, LocalTransform.FromPosition(spawnData.spawnPosition));

        // Set unit owner
        buffer.AddComponent(unitEntity, new GhostOwner { NetworkId = spawnData.ownerNetworkId });

        // Set unit color based on owner
        var rgba = PlayerColorUtil.FromId(spawnData.ownerNetworkId);
        buffer.SetComponent(unitEntity, new Owner { OwnerColor = rgba });

        // Set initial unit mover state
        buffer.SetComponent(unitEntity, new UnitTargets
        {
            destinationPosition = spawnData.spawnPosition,
        });

        Entity playerConnection = Entity.Null;
        foreach (var (netId, connEntity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithAll<NetworkStreamConnection>()
            .WithEntityAccess())
        {
            if (netId.ValueRO.Value == spawnData.ownerNetworkId)
            {
                playerConnection = connEntity;
                break;
            }
        }
        // Award 10 points for spawning a unit
        if (playerConnection != Entity.Null)
        {
            ServerPlayerStatsSystem.AwardDirectScore(buffer, playerConnection, 10, ScoreReason.UnitSpawn);
        }
    }

    private float3 FindValidSpawnPosition(float3 buildingPosition, ref SystemState state)
    {
        // Array of potential spawn offsets around the building
        float3[] offsets = new float3[]
        {
            new float3(4f, 0f, 0f),   // Right - only one currently used
            new float3(-4f, 0f, 0f),  // Left
            new float3(0f, 0f, 4f),   // Front
            new float3(0f, 0f, -4f),  // Back
            new float3(4f, 0f, 4f),   // Front-right
            new float3(-4f, 0f, 4f),  // Front-left
            new float3(4f, 0f, -4f),  // Back-right
            new float3(-4f, 0f, -4f), // Back-left
        };

        return buildingPosition + offsets[0];
    }
}
