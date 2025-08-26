using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Server system that manages the overall game state, win conditions, and victory detection
/// Runs after SpawnServerSystem to ensure players are properly spawned before game starts
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SpawnServerSystem))]
[UpdateAfter(typeof(ServerPlayerStatsSystem))]
public partial struct GameStateServerSystem : ISystem
{
    private EntityQuery playerConnectionQuery;
    private EntityQuery gameStateQuery;

    public void OnCreate(ref SystemState state)
    {
        // Create game state singleton if it doesn't exist
        if (!SystemAPI.HasSingleton<GameState>())
        {
            var gameStateEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(gameStateEntity, new GameState
            {
                currentPhase = GamePhase.WaitingForPlayers,
                gameEndTimer = 0f,
                winnerPlayerId = -1,
                gameHasStarted = false,
                gameStartTimer = 0f
            });

            // Add minimum unit costs config
            state.EntityManager.AddComponentData(gameStateEntity, MinimumUnitCosts.Default);
        }

        // Setup queries
        playerConnectionQuery = state.EntityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NetworkId>(),
            ComponentType.ReadOnly<PlayerStatsEntity>()
        );

        gameStateQuery = state.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<GameState>());

        state.RequireForUpdate<NetworkTime>();
        state.RequireForUpdate(gameStateQuery);
    }

    public void OnUpdate(ref SystemState state)
    {
        var gameState = SystemAPI.GetSingletonRW<GameState>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        switch (gameState.ValueRO.currentPhase)
        {
            case GamePhase.WaitingForPlayers:
                HandleWaitingForPlayers(ref state, ref gameState);
                break;

            case GamePhase.GameActive:
                HandleGameActive(ref state, ref gameState);
                break;

            case GamePhase.GameEnding:
                HandleGameEnding(ref state, ref gameState, deltaTime);
                break;

            case GamePhase.GameEnded:
                // Game has ended, do nothing (could reset here if needed)
                break;
        }
    }

    private void HandleWaitingForPlayers(ref SystemState state, ref RefRW<GameState> gameState)
    {
        // Check if any players have been spawned (have units in the world)
        if (!gameState.ValueRO.gameHasStarted && HasAnyPlayerUnits(ref state))
        {
            gameState.ValueRW.gameHasStarted = true;
            gameState.ValueRW.currentPhase = GamePhase.GameActive;
            gameState.ValueRW.gameStartTimer = GameState.GAME_START_GRACE_PERIOD; // Set grace period

            Debug.Log("Game Started! First player has spawned units. Grace period active.");

            // Initialize player alive unit tracking
            InitializePlayerAliveUnitTracking(ref state);
        }
    }

    private void HandleGameActive(ref SystemState state, ref RefRW<GameState> gameState)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        // Update alive unit counts for all players
        UpdatePlayerAliveUnitCounts(ref state);

        // Countdown grace period after game start
        if (gameState.ValueRO.gameStartTimer > 0f)
        {
            gameState.ValueRW.gameStartTimer -= deltaTime;

            if (gameState.ValueRO.gameStartTimer <= 0f)
            {
                Debug.Log("Game start grace period ended. Now checking win conditions.");
            }
            return; // Don't check win conditions during grace period
        }

        // Check win conditions only after grace period
        if (CheckWinConditions(ref state, out int winnerId))
        {
            gameState.ValueRW.currentPhase = GamePhase.GameEnding;
            gameState.ValueRW.winnerPlayerId = winnerId;
            gameState.ValueRW.gameEndTimer = GameState.GAME_END_GRACE_PERIOD;

            Debug.Log($"Game ending! Winner: Player {winnerId}");

            // Trigger victory screen events for all clients
            TriggerVictoryScreenEvents(ref state, winnerId);
        }
    }

    private void HandleGameEnding(ref SystemState state, ref RefRW<GameState> gameState, float deltaTime)
    {
        gameState.ValueRW.gameEndTimer -= deltaTime;

        if (gameState.ValueRO.gameEndTimer <= 0f)
        {
            gameState.ValueRW.currentPhase = GamePhase.GameEnded;
            Debug.Log("Game officially ended!");
        }
    }

    private bool HasAnyPlayerUnits(ref SystemState state)
    {
        // Check if any units exist with GhostOwner (player-owned units)
        var unitQuery = state.EntityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Unit>(),
            ComponentType.ReadOnly<GhostOwner>()
        );

        int unitCount = unitQuery.CalculateEntityCount();
        bool hasUnits = unitCount > 0;

        if (hasUnits)
        {
            Debug.Log($"Detected {unitCount} player units in world - ready to start game");
        }

        unitQuery.Dispose();
        return hasUnits;
    }

    private void InitializePlayerAliveUnitTracking(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // Create PlayerAliveUnits entities for each connected player
        foreach (var (networkId, connectionEntity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithAll<PlayerStatsEntity>()
            .WithEntityAccess())
        {
            var playerTrackingEntity = ecb.CreateEntity();
            ecb.AddComponent(playerTrackingEntity, new PlayerAliveUnits
            {
                unitCount = 0,
                playerId = networkId.ValueRO.Value,
                canAffordNewUnit = true
            });
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private void UpdatePlayerAliveUnitCounts(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // First, reset all counts and find all player tracking entities
        var playerTrackingEntities = new NativeList<Entity>(Allocator.Temp);
        var playerIds = new NativeList<int>(Allocator.Temp);

        foreach (var (playerAliveUnits, entity) in
            SystemAPI.Query<RefRW<PlayerAliveUnits>>()
            .WithEntityAccess())
        {
            playerAliveUnits.ValueRW.unitCount = 0;
            playerAliveUnits.ValueRW.canAffordNewUnit = false;

            playerTrackingEntities.Add(entity);
            playerIds.Add(playerAliveUnits.ValueRO.playerId);
        }

        // Count alive units per player
        int totalUnitsFound = 0;
        foreach (var (ghostOwner, unitEntity) in
            SystemAPI.Query<RefRO<GhostOwner>>()
            .WithAll<Unit>()
            .WithEntityAccess())
        {
            bool isAlive = IsUnitAlive(ref state, unitEntity);
            if (!isAlive) continue;

            totalUnitsFound++;
            int playerId = ghostOwner.ValueRO.NetworkId;

            // Find and update the corresponding PlayerAliveUnits
            for (int i = 0; i < playerIds.Length; i++)
            {
                if (playerIds[i] == playerId)
                {
                    var playerAliveUnits = SystemAPI.GetComponentRW<PlayerAliveUnits>(playerTrackingEntities[i]);
                    playerAliveUnits.ValueRW.unitCount++;
                    break;
                }
            }
        }

        // Check resource affordability for each player
        var minimumCosts = SystemAPI.GetSingleton<MinimumUnitCosts>();

        for (int i = 0; i < playerIds.Length; i++)
        {
            int playerId = playerIds[i];

            // Find player stats
            if (TryGetPlayerStats(ref state, playerId, out var playerStats))
            {
                bool canAfford = playerStats.resource1 >= minimumCosts.minResource1Cost &&
                               playerStats.resource2 >= minimumCosts.minResource2Cost;

                var playerAliveUnits = SystemAPI.GetComponentRW<PlayerAliveUnits>(playerTrackingEntities[i]);
                playerAliveUnits.ValueRW.canAffordNewUnit = canAfford;
            }
        }

        // Debug logging (only occasionally to avoid spam)
        if (UnityEngine.Time.frameCount % 120 == 0) // Every 2 seconds at 60fps
        {
            Debug.Log($"GameState Debug: Found {totalUnitsFound} total alive units");
            for (int i = 0; i < playerIds.Length; i++)
            {
                var playerData = SystemAPI.GetComponent<PlayerAliveUnits>(playerTrackingEntities[i]);
                Debug.Log($"  Player {playerIds[i]}: {playerData.unitCount} units, can afford: {playerData.canAffordNewUnit}");
            }
        }

        playerTrackingEntities.Dispose();
        playerIds.Dispose();
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private bool CheckWinConditions(ref SystemState state, out int winnerId)
    {
        winnerId = -1;

        var alivePlayers = new NativeList<int>(Allocator.Temp);

        // Find players who still have units OR can afford to build units
        foreach (var playerAliveUnits in SystemAPI.Query<RefRO<PlayerAliveUnits>>())
        {
            if (playerAliveUnits.ValueRO.unitCount > 0 || playerAliveUnits.ValueRO.canAffordNewUnit)
            {
                alivePlayers.Add(playerAliveUnits.ValueRO.playerId);
            }
        }

        bool gameEnded = false;

        if (alivePlayers.Length < 1)
        {
            gameEnded = true;

            Debug.Log($"Win condition triggered! {alivePlayers.Length} players remain alive/capable");

            if (alivePlayers.Length == 1)
            {
                winnerId = alivePlayers[0];
                Debug.Log($"Winner by elimination: Player {winnerId}");
            }
            else
            {
                // No players left - find winner by highest score
                winnerId = FindWinnerByScore(ref state);
                Debug.Log($"Winner by score: Player {winnerId}");
            }
        }

        alivePlayers.Dispose();
        return gameEnded;
    }

    private int FindWinnerByScore(ref SystemState state)
    {
        int winnerId = -1;
        int highestScore = -1;

        foreach (var playerStats in SystemAPI.Query<RefRO<PlayerStats>>())
        {
            if (playerStats.ValueRO.totalScore > highestScore)
            {
                highestScore = playerStats.ValueRO.totalScore;
                winnerId = playerStats.ValueRO.playerId;
            }
        }

        return winnerId;
    }

    private void TriggerVictoryScreenEvents(ref SystemState state, int winnerId)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // Create victory screen RPC for each connected client
        foreach (var (networkId, connectionEntity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithAll<PlayerStatsEntity>()
            .WithEntityAccess())
        {
            var rpcEntity = ecb.CreateEntity();
            ecb.AddComponent(rpcEntity, new ShowVictoryScreenRpc
            {
                winnerPlayerId = winnerId,
                isLocalPlayerWinner = networkId.ValueRO.Value == winnerId
            });

            // Set the RPC to be sent to this specific client
            ecb.AddComponent(rpcEntity, new SendRpcCommandRequest
            {
                TargetConnection = connectionEntity
            });
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private bool IsUnitAlive(ref SystemState state, Entity unitEntity)
    {
        if (!SystemAPI.HasComponent<HealthState>(unitEntity))
            return true; // No health component means alive

        var healthState = SystemAPI.GetComponent<HealthState>(unitEntity);
        return healthState.currentStage != HealthStage.Dead;
    }

    private bool TryGetPlayerStats(ref SystemState state, int playerId, out PlayerStats playerStats)
    {
        playerStats = default;

        foreach (var stats in SystemAPI.Query<RefRO<PlayerStats>>())
        {
            if (stats.ValueRO.playerId == playerId)
            {
                playerStats = stats.ValueRO;
                return true;
            }
        }

        return false;
    }
}