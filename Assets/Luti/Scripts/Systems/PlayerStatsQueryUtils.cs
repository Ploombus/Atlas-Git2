/*using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;

/// <summary>
/// Compatibility layer for existing code that uses PlayerStatsQueryUtils
/// Now reads from Ghost-replicated PlayerStats instead of singleton
/// </summary>
public static class PlayerStatsQueryUtils
{
    public static bool TryGetLocalPlayerStats(World clientWorld,
        out int resource1, out int resource2,
        out int totalScore, out int resource1Score, out int resource2Score)
    {
        resource1 = resource2 = totalScore = resource1Score = resource2Score = 0;

        if (clientWorld == null || !clientWorld.IsCreated) return false;

        var entityManager = clientWorld.EntityManager;

        // Get local player network ID
        int localPlayerNetworkId = GetLocalPlayerNetworkId(entityManager);
        if (localPlayerNetworkId == -1) return false;

        // Find PlayerStats for local player
        using var query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<PlayerStats>(),
            ComponentType.ReadOnly<NetworkId>()
        );

        if (query.IsEmpty) return false;

        var allStats = query.ToComponentDataArray<PlayerStats>(Allocator.Temp);
        var allNetIds = query.ToComponentDataArray<NetworkId>(Allocator.Temp);

        bool found = false;
        for (int i = 0; i < allStats.Length; i++)
        {
            if (allStats[i].playerId == localPlayerNetworkId)
            {
                resource1 = allStats[i].resource1;
                resource2 = allStats[i].resource2;
                totalScore = allStats[i].totalScore;
                resource1Score = allStats[i].resource1Score;
                resource2Score = allStats[i].resource2Score;
                found = true;
                break;
            }
        }

        allStats.Dispose();
        allNetIds.Dispose();
        return found;
    }

    public static CurrentPlayerStatsData GetCurrentPlayerStats(World clientWorld)
    {
        if (TryGetLocalPlayerStats(clientWorld, out int r1, out int r2, out int total, out int r1Score, out int r2Score))
        {
            return new CurrentPlayerStatsData
            {
                resource1 = r1,
                resource2 = r2,
                totalScore = total,
                resource1Score = r1Score,
                resource2Score = r2Score,
                hasValidData = true
            };
        }

        return new CurrentPlayerStatsData { hasValidData = false };
    }

    private static int GetLocalPlayerNetworkId(EntityManager entityManager)
    {
        // First try to find using GhostOwnerIsLocal (preferred method)
        using var ghostOwnerQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<GhostOwner>(),
            ComponentType.ReadOnly<GhostOwnerIsLocal>()
        );

        if (!ghostOwnerQuery.IsEmpty)
        {
            var ghostOwners = ghostOwnerQuery.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            if (ghostOwners.Length > 0)
            {
                int localId = ghostOwners[0].NetworkId;
                ghostOwners.Dispose();
                return localId;
            }
            ghostOwners.Dispose();
        }

        // Fallback: find the first NetworkStreamConnection (client connection)
        using var connectionQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NetworkId>(),
            ComponentType.ReadOnly<NetworkStreamConnection>()
        );

        if (!connectionQuery.IsEmpty)
        {
            var networkIds = connectionQuery.ToComponentDataArray<NetworkId>(Allocator.Temp);
            if (networkIds.Length > 0)
            {
                int localId = networkIds[0].Value;
                networkIds.Dispose();
                return localId;
            }
            networkIds.Dispose();
        }

        return -1; // No local player found
    }
}

/// <summary>
/// Keep existing data structures for compatibility
/// </summary>
public struct CurrentPlayerStatsData : IComponentData
{
    public int resource1;
    public int resource2;
    public int totalScore;
    public int resource1Score;
    public int resource2Score;
    public bool hasValidData;
}*/