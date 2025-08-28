using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(SpawnServerSystem))] // CRITICAL: Run before SpawnServerSystem
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ServerPlayerStatsSystem : ISystem
{
    private const int STARTING_RESOURCE1 = 100;
    private const int STARTING_RESOURCE2 = 100;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkTime>();

        // Create config entity if it doesn't exist
        if (!SystemAPI.HasSingleton<StatsConfig>())
        {
            var configEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(configEntity, StatsConfig.Default);
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var config = SystemAPI.GetSingleton<StatsConfig>();

        // Process player spawning FIRST (before SpawnServerSystem removes PendingPlayerSpawn)
        ProcessPlayerSpawning(ref state, ecb);

        // Process resource reservation events
        ProcessResourceReservations(ref state, ecb);

        // Process deduction of reserved resources
        ProcessReservedResourceDeductions(ref state, ecb);

        // Process stats change events
        ProcessStatsChangeEvents(ref state, ecb, config);

        // Process direct score events
        ProcessDirectScoreEvents(ref state, ecb);

        // Process resource addition requests
        ProcessResourceRequests(ref state, ecb);

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private void ProcessPlayerSpawning(ref SystemState state, EntityCommandBuffer ecb)
    {
        if (SystemAPI.TryGetSingleton<EntitiesReferencesLuti>(out var references) &&
            references.playerStatsPrefabEntity != Entity.Null)
        {
            ProcessPlayerSpawningWithPrefab(ref state, ecb, references);
        }
    }

    private void ProcessPlayerSpawningWithPrefab(ref SystemState state, EntityCommandBuffer ecb, EntitiesReferencesLuti references)
    {
        foreach (var (netId, connectionEntity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithAll<PendingPlayerSpawn>()
            .WithNone<PlayerStatsEntity>()
            .WithEntityAccess())
        {
            var playerStatsEntity = ecb.Instantiate(references.playerStatsPrefabEntity);

            ecb.SetComponent(playerStatsEntity, new PlayerStats
            {
                resource1 = STARTING_RESOURCE1,
                resource2 = STARTING_RESOURCE2,
                reservedResource1 = 0, // Initialize reserved resources
                reservedResource2 = 0,
                totalScore = 0,
                resource1Score = 0,
                resource2Score = 0,
                playerId = netId.ValueRO.Value
            });

            // Set ownership
            ecb.SetComponent(playerStatsEntity, new GhostOwner { NetworkId = netId.ValueRO.Value });

            // Link connection to stats entity
            ecb.AddComponent(connectionEntity, new PlayerStatsEntity { Value = playerStatsEntity });
        }
    }

    private void ProcessResourceReservations(ref SystemState state, EntityCommandBuffer ecb)
    {
        foreach (var (reservation, eventEntity) in
            SystemAPI.Query<RefRO<ResourceReservationEvent>>()
            .WithEntityAccess())
        {
            var playerStatsEntity = FindPlayerStatsEntity(ref state, reservation.ValueRO.playerConnection);

            if (playerStatsEntity == Entity.Null)
            {
                ecb.DestroyEntity(eventEntity);
                continue;
            }

            var stats = state.EntityManager.GetComponentData<PlayerStats>(playerStatsEntity);

            if (reservation.ValueRO.isReservation)
            {
                // Reserve resources
                stats.reservedResource1 += reservation.ValueRO.resource1Amount;
                stats.reservedResource2 += reservation.ValueRO.resource2Amount;
            }
            else
            {
                // Release reservation (e.g., if spawn was cancelled)
                stats.reservedResource1 = math.max(0, stats.reservedResource1 - reservation.ValueRO.resource1Amount);
                stats.reservedResource2 = math.max(0, stats.reservedResource2 - reservation.ValueRO.resource2Amount);
            }

            ecb.SetComponent(playerStatsEntity, stats);
            ecb.DestroyEntity(eventEntity);
        }
    }

    private void ProcessReservedResourceDeductions(ref SystemState state, EntityCommandBuffer ecb)
    {
        foreach (var (deduction, eventEntity) in
            SystemAPI.Query<RefRO<DeductReservedResourcesEvent>>()
            .WithEntityAccess())
        {
            var playerStatsEntity = FindPlayerStatsEntity(ref state, deduction.ValueRO.playerConnection);

            if (playerStatsEntity == Entity.Null)
            {
                ecb.DestroyEntity(eventEntity);
                continue;
            }

            var stats = state.EntityManager.GetComponentData<PlayerStats>(playerStatsEntity);

            // Deduct from actual resources
            stats.resource1 -= deduction.ValueRO.resource1Amount;
            stats.resource2 -= deduction.ValueRO.resource2Amount;

            // Remove from reserved
            stats.reservedResource1 = math.max(0, stats.reservedResource1 - deduction.ValueRO.resource1Amount);
            stats.reservedResource2 = math.max(0, stats.reservedResource2 - deduction.ValueRO.resource2Amount);

            // Clamp to ensure non-negative
            stats.resource1 = math.max(0, stats.resource1);
            stats.resource2 = math.max(0, stats.resource2);

            ecb.SetComponent(playerStatsEntity, stats);
            ecb.DestroyEntity(eventEntity);
        }
    }

    private void ProcessStatsChangeEvents(ref SystemState state, EntityCommandBuffer ecb, StatsConfig config)
    {
        foreach (var (changeEvent, eventEntity) in
            SystemAPI.Query<RefRO<StatsChangeEvent>>()
            .WithEntityAccess())
        {
            var playerConnection = changeEvent.ValueRO.playerConnection;
            var playerStatsEntity = FindPlayerStatsEntity(ref state, playerConnection);

            if (playerStatsEntity == Entity.Null)
            {
                ecb.DestroyEntity(eventEntity);
                continue;
            }

            var stats = state.EntityManager.GetComponentData<PlayerStats>(playerStatsEntity);

            // Update resources
            stats.resource1 += changeEvent.ValueRO.resource1Delta;
            stats.resource2 += changeEvent.ValueRO.resource2Delta;

            // Award score points if requested
            if (changeEvent.ValueRO.awardScorePoints && changeEvent.ValueRO.resource1Delta > 0)
            {
                int scoreGain = changeEvent.ValueRO.resource1Delta * config.pointsPerResource1;
                stats.totalScore += scoreGain;
                stats.resource1Score += scoreGain;
            }

            if (changeEvent.ValueRO.awardScorePoints && changeEvent.ValueRO.resource2Delta > 0)
            {
                int scoreGain = changeEvent.ValueRO.resource2Delta * config.pointsPerResource2;
                stats.totalScore += scoreGain;
                stats.resource2Score += scoreGain;
            }

            // Ensure resources don't go negative
            stats.resource1 = math.max(0, stats.resource1);
            stats.resource2 = math.max(0, stats.resource2);

            ecb.SetComponent(playerStatsEntity, stats);
            ecb.DestroyEntity(eventEntity);
        }
    }

    private void ProcessDirectScoreEvents(ref SystemState state, EntityCommandBuffer ecb)
    {
        foreach (var (scoreEvent, eventEntity) in
            SystemAPI.Query<RefRO<DirectScoreEvent>>()
            .WithEntityAccess())
        {
            var playerStatsEntity = FindPlayerStatsEntity(ref state, scoreEvent.ValueRO.playerConnection);

            if (playerStatsEntity == Entity.Null)
            {
                ecb.DestroyEntity(eventEntity);
                continue;
            }

            var stats = state.EntityManager.GetComponentData<PlayerStats>(playerStatsEntity);
            stats.totalScore += scoreEvent.ValueRO.scorePoints;
            ecb.SetComponent(playerStatsEntity, stats);
            ecb.DestroyEntity(eventEntity);
        }
    }

    private void ProcessResourceRequests(ref SystemState state, EntityCommandBuffer ecb)
    {
        foreach (var (rpc, request, rpcEntity) in
            SystemAPI.Query<RefRO<AddResourcesRpc>, RefRO<ReceiveRpcCommandRequest>>()
            .WithEntityAccess())
        {
            var connection = request.ValueRO.SourceConnection;
            var eventEntity = ecb.CreateEntity();
            ecb.AddComponent(eventEntity, new StatsChangeEvent
            {
                resource1Delta = rpc.ValueRO.resource1ToAdd,
                resource2Delta = rpc.ValueRO.resource2ToAdd,
                playerConnection = connection,
                awardScorePoints = true
            });

            ecb.DestroyEntity(rpcEntity);
        }
    }

    // Static helper methods for external systems
    public static Entity FindPlayerStatsEntity(ref SystemState state, Entity connectionEntity)
    {
        if (connectionEntity == Entity.Null || !state.EntityManager.Exists(connectionEntity))
            return Entity.Null;

        if (!state.EntityManager.HasComponent<PlayerStatsEntity>(connectionEntity))
            return Entity.Null;

        var statsEntity = state.EntityManager.GetComponentData<PlayerStatsEntity>(connectionEntity).Value;

        if (!state.EntityManager.Exists(statsEntity))
            return Entity.Null;

        return statsEntity;
    }

    public static Entity FindPlayerConnectionByNetworkId(ref SystemState state, int networkId)
    {
        var entityManager = state.EntityManager;
        var query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NetworkId>(),
            ComponentType.ReadOnly<PlayerStatsEntity>()
        );

        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        var networkIds = query.ToComponentDataArray<NetworkId>(Unity.Collections.Allocator.Temp);

        Entity result = Entity.Null;
        for (int i = 0; i < entities.Length; i++)
        {
            if (networkIds[i].Value == networkId)
            {
                result = entities[i];
                break;
            }
        }

        entities.Dispose();
        networkIds.Dispose();

        return result;
    }

    // Check if player can afford (considering reserved resources)
    public static bool CanAfford(ref SystemState state, Entity connectionEntity, int resource1Cost, int resource2Cost)
    {
        var statsEntity = FindPlayerStatsEntity(ref state, connectionEntity);
        if (statsEntity == Entity.Null)
            return false;

        var stats = state.EntityManager.GetComponentData<PlayerStats>(statsEntity);
        int availableR1 = stats.resource1 - stats.reservedResource1;
        int availableR2 = stats.resource2 - stats.reservedResource2;

        return availableR1 >= resource1Cost && availableR2 >= resource2Cost;
    }

    // Reserve resources for pending spawn
    public static bool TryReserveResources(ref SystemState state, EntityCommandBuffer ecb,
        Entity connectionEntity, int resource1Cost, int resource2Cost)
    {
        if (!CanAfford(ref state, connectionEntity, resource1Cost, resource2Cost))
            return false;

        var eventEntity = ecb.CreateEntity();
        ecb.AddComponent(eventEntity, new ResourceReservationEvent
        {
            resource1Amount = resource1Cost,
            resource2Amount = resource2Cost,
            playerConnection = connectionEntity,
            isReservation = true
        });

        return true;
    }

    // Deduct reserved resources when unit spawns
    public static void DeductReservedResources(ref SystemState state, EntityCommandBuffer ecb,
        Entity connectionEntity, int resource1Cost, int resource2Cost)
    {
        var eventEntity = ecb.CreateEntity();
        ecb.AddComponent(eventEntity, new DeductReservedResourcesEvent
        {
            resource1Amount = resource1Cost,
            resource2Amount = resource2Cost,
            playerConnection = connectionEntity
        });
    }

    // Legacy method - now just checks without spending
    public static bool TrySpendResources(ref SystemState state, EntityCommandBuffer ecb,
        Entity connectionEntity, int resource1Cost, int resource2Cost)
    {
        // This now only checks affordability, actual spending is handled via events
        return CanAfford(ref state, connectionEntity, resource1Cost, resource2Cost);
    }
}