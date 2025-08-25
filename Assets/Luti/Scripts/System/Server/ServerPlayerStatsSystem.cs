using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// FIXED: Server system that creates proper ghosted player entities
/// IMPORTANT: Process PlayerStats BEFORE SpawnServerSystem removes PendingPlayerSpawn
/// </summary>
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
        // Option A: Use prefab if available
        if (SystemAPI.TryGetSingleton<EntitiesReferencesLuti>(out var references) &&
            references.playerStatsPrefabEntity != Entity.Null)
        {
            ProcessPlayerSpawningWithPrefab(ref state, ecb, references);
        }
        else
        {
            // Option B: Fallback to manual entity creation
            ProcessPlayerSpawningManual(ref state, ecb);
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
            // FIXED: Instantiate from prefab for proper replication
            var playerStatsEntity = ecb.Instantiate(references.playerStatsPrefabEntity);

            // Set player-specific data
            ecb.SetComponent(playerStatsEntity, new PlayerStats
            {
                resource1 = STARTING_RESOURCE1,
                resource2 = STARTING_RESOURCE2,
                totalScore = 0,
                resource1Score = 0,
                resource2Score = 0,
                playerId = netId.ValueRO.Value
            });

            // Set ownership
            ecb.SetComponent(playerStatsEntity, new GhostOwner { NetworkId = netId.ValueRO.Value });

            // Link connection to stats entity
            ecb.AddComponent(connectionEntity, new PlayerStatsEntity { Value = playerStatsEntity });

            // DON'T remove PendingPlayerSpawn here - let SpawnServerSystem handle it
        }
    }

    private void ProcessPlayerSpawningManual(ref SystemState state, EntityCommandBuffer ecb)
    {
        foreach (var (netId, connectionEntity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithAll<PendingPlayerSpawn>()
            .WithNone<PlayerStatsEntity>()
            .WithEntityAccess())
        {
            // Fallback: Create entity manually (won't replicate properly)
            var playerStatsEntity = ecb.CreateEntity();

            ecb.AddComponent(playerStatsEntity, new PlayerStats
            {
                resource1 = STARTING_RESOURCE1,
                resource2 = STARTING_RESOURCE2,
                totalScore = 0,
                resource1Score = 0,
                resource2Score = 0,
                playerId = netId.ValueRO.Value
            });

            ecb.AddComponent(playerStatsEntity, new GhostOwner { NetworkId = netId.ValueRO.Value });
            ecb.AddComponent(connectionEntity, new PlayerStatsEntity { Value = playerStatsEntity });

            // DON'T remove PendingPlayerSpawn here - let SpawnServerSystem handle it
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
            stats.resource1 = math.max(0, stats.resource1);
            stats.resource2 = math.max(0, stats.resource2);

            // Award score points if specified
            if (changeEvent.ValueRO.awardScorePoints)
            {
                int r1ScoreIncrease = math.max(0, changeEvent.ValueRO.resource1Delta) * config.pointsPerResource1;
                int r2ScoreIncrease = math.max(0, changeEvent.ValueRO.resource2Delta) * config.pointsPerResource2;

                stats.totalScore += r1ScoreIncrease + r2ScoreIncrease;
                stats.resource1Score += r1ScoreIncrease;
                stats.resource2Score += r2ScoreIncrease;
            }

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
            var playerConnection = scoreEvent.ValueRO.playerConnection;
            var playerStatsEntity = FindPlayerStatsEntity(ref state, playerConnection);

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
        foreach (var (request, receiveRequest, rpcEntity) in
            SystemAPI.Query<RefRO<AddResourcesRpc>, RefRO<ReceiveRpcCommandRequest>>()
            .WithEntityAccess())
        {
            var connection = receiveRequest.ValueRO.SourceConnection;

            if (FindPlayerStatsEntity(ref state, connection) != Entity.Null)
            {
                TriggerStatsChange(ecb, connection,
                    request.ValueRO.resource1ToAdd,
                    request.ValueRO.resource2ToAdd,
                    awardScorePoints: true);
            }

            ecb.DestroyEntity(rpcEntity);
        }
    }

    private Entity FindPlayerStatsEntity(ref SystemState state, Entity playerConnection)
    {
        if (!state.EntityManager.HasComponent<PlayerStatsEntity>(playerConnection))
            return Entity.Null;

        var statsLink = state.EntityManager.GetComponentData<PlayerStatsEntity>(playerConnection);
        return statsLink.Value;
    }

    // Static utility methods for other systems
    public static Entity FindPlayerConnectionByNetworkId(ref SystemState state, int networkId)
    {
        var entityManager = state.EntityManager;

        using var query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NetworkId>(),
            ComponentType.ReadOnly<NetworkStreamConnection>(),
            ComponentType.ReadOnly<PlayerStatsEntity>()
        );

        var entities = query.ToEntityArray(Allocator.Temp);
        var networkIds = query.ToComponentDataArray<NetworkId>(Allocator.Temp);

        Entity result = Entity.Null;
        for (int i = 0; i < networkIds.Length; i++)
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

    public static void TriggerStatsChange(EntityCommandBuffer ecb, Entity playerConnection,
        int resource1Delta, int resource2Delta, bool awardScorePoints = false)
    {
        var eventEntity = ecb.CreateEntity();
        ecb.AddComponent(eventEntity, new StatsChangeEvent
        {
            resource1Delta = resource1Delta,
            resource2Delta = resource2Delta,
            playerConnection = playerConnection,
            awardScorePoints = awardScorePoints
        });
    }

    public static void AwardDirectScore(EntityCommandBuffer ecb, Entity playerConnection,
        int scorePoints, ScoreReason reason = ScoreReason.Custom)
    {
        var eventEntity = ecb.CreateEntity();
        ecb.AddComponent(eventEntity, new DirectScoreEvent
        {
            scorePoints = scorePoints,
            playerConnection = playerConnection,
            reason = reason
        });
    }

    public static bool TrySpendResources(ref SystemState state, EntityCommandBuffer ecb,
        Entity playerConnection, int resource1Cost, int resource2Cost)
    {
        if (!state.EntityManager.HasComponent<PlayerStatsEntity>(playerConnection))
        {
            return false;
        }

        var statsEntity = state.EntityManager.GetComponentData<PlayerStatsEntity>(playerConnection).Value;
        if (statsEntity == Entity.Null || !state.EntityManager.HasComponent<PlayerStats>(statsEntity))
        {
            return false;
        }

        var stats = state.EntityManager.GetComponentData<PlayerStats>(statsEntity);

        if (stats.resource1 >= resource1Cost && stats.resource2 >= resource2Cost)
        {
            TriggerStatsChange(ecb, playerConnection, -resource1Cost, -resource2Cost, false);
            return true;
        }

        return false;
    }
}