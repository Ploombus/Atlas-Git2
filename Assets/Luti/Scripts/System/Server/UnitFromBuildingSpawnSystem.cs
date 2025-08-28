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

        // Step 1: Handle new spawn unit from building requests (add to queue with reservation)
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
        foreach (var (rpc, request, rpcEntity) in
            SystemAPI.Query<RefRO<SpawnUnitFromBuildingRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
        {
            var connection = request.ValueRO.SourceConnection;
            var requesterNetId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            var buildingEntity = rpc.ValueRO.buildingEntity;

            // Check if the building exists
            if (!SystemAPI.Exists(buildingEntity))
            {
                Debug.LogWarning("Tried to spawn unit from non-existent building");
                buffer.DestroyEntity(rpcEntity);
                continue;
            }

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

            // Get spawn costs
            int resource1Cost = 0;
            int resource2Cost = 0;
            if (SystemAPI.HasComponent<UnitSpawnCost>(buildingEntity))
            {
                var spawnCost = SystemAPI.GetComponent<UnitSpawnCost>(buildingEntity);
                resource1Cost = spawnCost.unitResource1Cost;
                resource2Cost = spawnCost.unitResource2Cost;
            }

            // Find player connection entity
            Entity playerConnectionEntity = ServerPlayerStatsSystem.FindPlayerConnectionByNetworkId(ref state, requesterNetId);
            if (playerConnectionEntity == Entity.Null)
            {
                Debug.LogWarning($"Player {requesterNetId} connection not found");
                buffer.DestroyEntity(rpcEntity);
                continue;
            }

            // CHECK AND RESERVE RESOURCES (not deduct)
            if (resource1Cost > 0 || resource2Cost > 0)
            {
                if (!ServerPlayerStatsSystem.TryReserveResources(ref state, buffer,
                    playerConnectionEntity, resource1Cost, resource2Cost))
                {
                    buffer.DestroyEntity(rpcEntity);
                    continue;
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

            // Add to queue with reserved resource tracking
            var queuedSpawnEntity = buffer.CreateEntity();
            buffer.AddComponent(queuedSpawnEntity, new QueuedUnitSpawn
            {
                buildingEntity = buildingEntity,
                ownerNetworkId = buildingOwnerId,
                spawnPosition = spawnPosition,
                reservedResource1 = resource1Cost,
                reservedResource2 = resource2Cost
            });

            // Consume RPC
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
                            totalSpawnTime = timeToSpawnUnit,
                            reservedResource1 = queuedSpawn.ValueRO.reservedResource1,
                            reservedResource2 = queuedSpawn.ValueRO.reservedResource2
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
                    // Deduct the reserved resources now that unit is spawning
                    Entity playerConnectionEntity = ServerPlayerStatsSystem.FindPlayerConnectionByNetworkId(
                        ref state, pendingSpawn.ValueRO.ownerNetworkId);

                    if (playerConnectionEntity != Entity.Null)
                    {
                        ServerPlayerStatsSystem.DeductReservedResources(ref state, buffer,
                            playerConnectionEntity,
                            pendingSpawn.ValueRO.reservedResource1,
                            pendingSpawn.ValueRO.reservedResource2);
                    }

                    // Spawn the unit
                    SpawnUnit(ref state, buffer, pendingSpawn.ValueRO, unitReferences);

                    // Update building spawn queue state
                    if (SystemAPI.HasComponent<BuildingSpawnQueue>(buildingEntity))
                    {
                        var spawnQueue = SystemAPI.GetComponent<BuildingSpawnQueue>(buildingEntity);
                        spawnQueue.isCurrentlySpawning = false;

                        // Always set the component, even if queue is empty
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

                    // Release reserved resources since building was destroyed
                    Entity playerConnectionEntity = ServerPlayerStatsSystem.FindPlayerConnectionByNetworkId(
                        ref state, pendingSpawn.ValueRO.ownerNetworkId);

                    if (playerConnectionEntity != Entity.Null)
                    {
                        // Release reservation instead of deducting
                        var eventEntity = buffer.CreateEntity();
                        buffer.AddComponent(eventEntity, new ResourceReservationEvent
                        {
                            resource1Amount = pendingSpawn.ValueRO.reservedResource1,
                            resource2Amount = pendingSpawn.ValueRO.reservedResource2,
                            playerConnection = playerConnectionEntity,
                            isReservation = false // Release reservation
                        });
                    }
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

        // Set position
        buffer.SetComponent(unitEntity, LocalTransform.FromPosition(spawnData.spawnPosition));

        // Set ownership
        buffer.AddComponent(unitEntity, new GhostOwner { NetworkId = spawnData.ownerNetworkId });

        // Set color
        var rgba = PlayerColorUtil.FromId(spawnData.ownerNetworkId);
        buffer.SetComponent(unitEntity, new Owner { OwnerColor = rgba });

        // Set initial target to spawn position
        var targets = SpawnServerSystem.SpawnTargetsAt(spawnData.spawnPosition);
        buffer.SetComponent(unitEntity, targets);
    }

    private float3 FindValidSpawnPosition(float3 buildingPosition, ref SystemState state)
    {
        // Simple spawn position logic - spawn units in front of building
        //float spawnRadius = 10f;
        //float angle = UnityEngine.Random.Range(0f, 360f) * math.PI / 180f;
        //float3 offset = new float3(math.cos(angle) * spawnRadius, 0, math.sin(angle) * spawnRadius);
        //return buildingPosition + offset;
        float3 offset = new float3(0f, 0f, -8f);
        return buildingPosition + offset;
    }
}