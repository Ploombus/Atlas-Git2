using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;

/// <summary>
/// Main game state component - singleton entity
/// </summary>
public struct GameState : IComponentData
{
    [GhostField] public GamePhase currentPhase;
    [GhostField] public float gameEndTimer;
    [GhostField] public int winnerPlayerId;
    [GhostField] public bool gameHasStarted;
    [GhostField] public float gameStartTimer; // Grace period after game starts

    public const float GAME_END_GRACE_PERIOD = 3.0f; // 3 seconds to show results
    public const float GAME_START_GRACE_PERIOD = 2.0f; // 2 seconds before checking win conditions
}

/// <summary>
/// Game phases for state management
/// </summary>
public enum GamePhase : byte
{
    WaitingForPlayers = 0,
    GameActive = 1,
    GameEnding = 2,
    GameEnded = 3
}

/// <summary>
/// Component to track alive units per player
/// </summary>
public struct PlayerAliveUnits : IComponentData
{
    public int unitCount;
    public int playerId;
    public bool canAffordNewUnit;
}

/// <summary>
/// Event component to trigger victory screen (server-side)
/// </summary>
public struct VictoryScreenTrigger : IComponentData
{
    public int winnerPlayerId;
    public bool isLocalPlayerWinner;
}

/// <summary>
/// Component to mark units as countable for game state
/// </summary>
public struct GameStateTrackedUnit : IComponentData, IEnableableComponent
{
    // This component is automatically enabled/disabled based on unit alive state
}

/// <summary>
/// Config for minimum unit costs - used to determine if player can afford any unit
/// </summary>
public struct MinimumUnitCosts : IComponentData
{
    public int minResource1Cost;
    public int minResource2Cost;

    public static MinimumUnitCosts Default => new MinimumUnitCosts
    {
        minResource1Cost = 10, // Set based on your cheapest unit
        minResource2Cost = 5   // Set based on your cheapest unit
    };
}