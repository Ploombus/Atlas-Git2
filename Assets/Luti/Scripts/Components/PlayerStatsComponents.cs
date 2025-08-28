using Unity.Entities;
using Unity.NetCode;

[GhostComponent(PrefabType = GhostPrefabType.All)]
public struct PlayerStats : IComponentData
{
    // Current resources
    [GhostField] public int resource1;
    [GhostField] public int resource2;

    // Reserved resources (for pending unit spawns)
    [GhostField] public int reservedResource1;
    [GhostField] public int reservedResource2;

    [GhostField] public int totalScore;
    [GhostField] public int resource1Score;
    [GhostField] public int resource2Score;

    [GhostField] public int playerId;

    // For display purposes - shows current resources in scoreboard
    public int CurrentResource1 => resource1;
    public int CurrentResource2 => resource2;

    // Available resources (current minus reserved)
    public int AvailableResource1 => resource1 - reservedResource1;
    public int AvailableResource2 => resource2 - reservedResource2;
}

// Resource reservation request event
public struct ResourceReservationEvent : IComponentData
{
    public int resource1Amount;
    public int resource2Amount;
    public Entity playerConnection;
    public Entity sourceEntity; // The entity that requested the reservation (for tracking)
    public bool isReservation; // true = reserve, false = release reservation
}

// Resource deduction from reservation event
public struct DeductReservedResourcesEvent : IComponentData
{
    public int resource1Amount;
    public int resource2Amount;
    public Entity playerConnection;
}

// Keep existing RPCs for backward compatibility if needed
public struct SyncResourcesRpc : IRpcCommand
{
    public int resource1;
    public int resource2;
}

public struct StatsConfig : IComponentData
{
    public int pointsPerResource1;
    public int pointsPerResource2;

    public static StatsConfig Default => new StatsConfig
    {
        pointsPerResource1 = 1,
        pointsPerResource2 = 1
    };
}

public struct StatsChangeEvent : IComponentData
{
    public int resource1Delta;
    public int resource2Delta;
    public Entity playerConnection;
    public bool awardScorePoints; // True for resource gains, false for resource spending
}

public struct PlayerStatsEntity : IComponentData
{
    public Entity Value;
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