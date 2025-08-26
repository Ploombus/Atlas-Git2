using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SpawnServerSystem))]
[UpdateAfter(typeof(ServerPlayerStatsSystem))]
public partial struct GameStateServerSystem : ISystem
{
    private const int MINIMUM_RESOURCE1_TO_SURVIVE = 20;

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
        }

        state.RequireForUpdate<NetworkTime>();
        state.RequireForUpdate<GameState>();
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
                HandleGameActive(ref state, ref gameState, deltaTime);
                break;

            case GamePhase.GameEnding:
                HandleGameEnding(ref state, ref gameState, deltaTime);
                break;

            case GamePhase.GameEnded:
                // Game has ended, could implement restart logic here
                break;
        }
    }

    private void HandleWaitingForPlayers(ref SystemState state, ref RefRW<GameState> gameState)
    {
        // Check if any players have spawned units
        if (!gameState.ValueRO.gameHasStarted && HasAnyPlayerUnits(ref state))
        {
            gameState.ValueRW.gameHasStarted = true;
            gameState.ValueRW.currentPhase = GamePhase.GameActive;
            gameState.ValueRW.gameStartTimer = GameState.GAME_START_GRACE_PERIOD;
        }
    }

    private void HandleGameActive(ref SystemState state, ref RefRW<GameState> gameState, float deltaTime)
    {
        // Countdown grace period after game start
        if (gameState.ValueRO.gameStartTimer > 0f)
        {
            gameState.ValueRW.gameStartTimer -= deltaTime;
            if (gameState.ValueRO.gameStartTimer <= 0f)
            return; // Don't check win conditions during grace period
        }

        // Check win conditions only after grace period
        if (CheckSimpleWinConditions(ref state, out int winnerId))
        {
            gameState.ValueRW.currentPhase = GamePhase.GameEnding;
            gameState.ValueRW.winnerPlayerId = winnerId;
            gameState.ValueRW.gameEndTimer = GameState.GAME_END_GRACE_PERIOD;

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
        // Simple check: any units with GhostOwner exist?
        foreach (var _ in SystemAPI.Query<RefRO<GhostOwner>>().WithAll<Unit>())
        {
            return true;
        }
        return false;
    }

    private bool CheckSimpleWinConditions(ref SystemState state, out int winnerId)
    {
        winnerId = -1;

        // Get count of connected players
        int connectedPlayerCount = GetConnectedPlayerCount(ref state);

        if (connectedPlayerCount == 0)
        {
            return false; // No players, no winner
        }

        // Check each player's elimination status
        using var eliminatedPlayers = new NativeList<int>(Allocator.Temp);
        using var alivePlayers = new NativeList<int>(Allocator.Temp);

        foreach (var (networkId, connectionEntity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithAll<PlayerStatsEntity>()
            .WithEntityAccess())
        {
            int playerId = networkId.ValueRO.Value;
            bool isEliminated = IsPlayerEliminated(ref state, playerId);

            if (isEliminated)
            {
                eliminatedPlayers.Add(playerId);
            }
            else
            {
                alivePlayers.Add(playerId);
            }
        }

        // Determine if game should end and who wins
        bool gameEnded = false;

        if (connectedPlayerCount == 1)
        {
            // SINGLE PLAYER: Game ends only if the single player is eliminated
            if (eliminatedPlayers.Length > 0)
            {
                winnerId = -1; // No winner in single player elimination
                gameEnded = true;
            }
        }
        else if (connectedPlayerCount >= 2)
        {
            // MULTIPLAYER: Game ends when any player is eliminated
            if (eliminatedPlayers.Length > 0)
            {

                if (alivePlayers.Length == 1)
                {
                    winnerId = alivePlayers[0];
                }
                else if (alivePlayers.Length == 0)
                {
                    winnerId = FindWinnerByScore(ref state);
                }
                else
                {
                    winnerId = FindWinnerByScoreAmongPlayers(ref state, alivePlayers);
                }
                gameEnded = true;
            }
        }

        return gameEnded;
    }

    private bool IsPlayerEliminated(ref SystemState state, int playerId)
    {
        // Count alive units for this player
        int aliveUnitCount = 0;
        foreach (var ghostOwner in SystemAPI.Query<RefRO<GhostOwner>>().WithAll<Unit>())
        {
            if (ghostOwner.ValueRO.NetworkId == playerId && IsUnitAlive(ref state, ghostOwner))
            {
                aliveUnitCount++;
            }
        }

        bool canAffordNewUnits = false;
        if (TryGetPlayerStats(ref state, playerId, out var playerStats))
        {
            canAffordNewUnits = playerStats.resource1 > MINIMUM_RESOURCE1_TO_SURVIVE;
        }

        bool isEliminated = aliveUnitCount == 0 && !canAffordNewUnits;

        return isEliminated;
    }

    private int GetConnectedPlayerCount(ref SystemState state)
    {
        int count = 0;
        foreach (var _ in SystemAPI.Query<RefRO<NetworkId>>().WithAll<PlayerStatsEntity>())
        {
            count++;
        }
        return count;
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

    private int FindWinnerByScoreAmongPlayers(ref SystemState state, NativeList<int> eligiblePlayers)
    {
        int winnerId = -1;
        int highestScore = -1;

        foreach (var playerStats in SystemAPI.Query<RefRO<PlayerStats>>())
        {
            // Check if this player is eligible
            bool isEligible = false;
            for (int i = 0; i < eligiblePlayers.Length; i++)
            {
                if (eligiblePlayers[i] == playerStats.ValueRO.playerId)
                {
                    isEligible = true;
                    break;
                }
            }

            if (isEligible && playerStats.ValueRO.totalScore > highestScore)
            {
                highestScore = playerStats.ValueRO.totalScore;
                winnerId = playerStats.ValueRO.playerId;
            }
        }

        return winnerId;
    }

    private void TriggerVictoryScreenEvents(ref SystemState state, int winnerId)
    {
        using var ecb = new EntityCommandBuffer(Allocator.Temp);

        // Send victory screen RPC to each connected client
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

            ecb.AddComponent(rpcEntity, new SendRpcCommandRequest
            {
                TargetConnection = connectionEntity
            });
        }

        ecb.Playback(state.EntityManager);
    }

    private bool IsUnitAlive(ref SystemState state, RefRO<GhostOwner> ghostOwner)
    {
        // Simple check: if unit has HealthState, check if not dead
        // Otherwise assume alive
        var entityManager = state.EntityManager;

        // Find the entity with this GhostOwner
        foreach (var (unitGhostOwner, unitEntity) in
            SystemAPI.Query<RefRO<GhostOwner>>()
            .WithAll<Unit>()
            .WithEntityAccess())
        {
            if (unitGhostOwner.ValueRO.NetworkId == ghostOwner.ValueRO.NetworkId)
            {
                if (entityManager.HasComponent<HealthState>(unitEntity))
                {
                    var healthState = entityManager.GetComponentData<HealthState>(unitEntity);
                    return healthState.currentStage != HealthStage.Dead;
                }
                return true; // No health component = alive
            }
        }

        return false; // Unit not found
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