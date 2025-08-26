using Unity.Entities;
using Unity.NetCode;


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


public enum GamePhase : byte
{
    WaitingForPlayers = 0,
    GameActive = 1,
    GameEnding = 2,
    GameEnded = 3
}


public struct GameStateTrackedUnit : IComponentData, IEnableableComponent
{
    // This component is automatically enabled/disabled based on unit alive state
}

public struct VictoryScreenTrigger : IComponentData
{
    public int winnerPlayerId;
    public bool isLocalPlayerWinner;
}