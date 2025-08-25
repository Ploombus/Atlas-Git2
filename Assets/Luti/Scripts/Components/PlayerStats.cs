using Unity.Entities;
using Unity.NetCode;


/// <summary>
/// FIXED: Now replicates to all clients for cross-player visibility
/// Simple Ghost component approach - server authoritative, visible to all
/// FIXED: Removed problematic SendToOwner parameter - using default behavior
/// </summary>
[GhostComponent(PrefabType = GhostPrefabType.All)]
public struct PlayerStats : IComponentData
{
    // Current resources
    [GhostField] public int resource1;
    [GhostField] public int resource2;

    // Lifetime scores (for leaderboard display)
    [GhostField] public int totalScore;
    [GhostField] public int resource1Score;
    [GhostField] public int resource2Score;

    // Player identifier for display
    [GhostField] public int playerId;

    // For display purposes - shows current resources in scoreboard
    public int CurrentResource1 => resource1;
    public int CurrentResource2 => resource2;
}

// Keep existing RPCs for backward compatibility if needed
public struct SyncResourcesRpc : IRpcCommand
{
    public int resource1;
    public int resource2;
}

public struct AddResourcesRpc : IRpcCommand
{
    public int resource1ToAdd;
    public int resource2ToAdd;
}

/// <summary>
/// Configuration for score calculation
/// </summary>
public struct StatsConfig : IComponentData
{
    public int pointsPerResource1;
    public int pointsPerResource2;

    public static StatsConfig Default => new StatsConfig
    {
        pointsPerResource1 = 10,
        pointsPerResource2 = 10
    };
}

/// <summary>
/// Events for triggering score updates
/// </summary>
public struct StatsChangeEvent : IComponentData
{
    public int resource1Delta;
    public int resource2Delta;
    public Entity playerConnection;
    public bool awardScorePoints; // True for resource gains, false for resource spending
}
public struct PlayerStatsEntity : IComponentData
{
    public Entity Value; // FIXED: Changed from "Entity" to "Value" to match your existing code usage
}
public struct DirectScoreEvent : IComponentData
{
    public int scorePoints;
    public Entity playerConnection;
    public ScoreReason reason;
}

public enum ScoreReason : byte
{
    UnitKill = 0,
    UnitSpawn = 1,
    ResourceGathering = 2,
    Custom = 255
}