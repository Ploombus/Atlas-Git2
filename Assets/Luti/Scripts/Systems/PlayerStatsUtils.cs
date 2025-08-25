using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Utility class to replace ResourceManager - reads directly from networked PlayerStats
/// </summary>
public static class PlayerStatsUtils
{
    /// <summary>
    /// Get local player's resources directly from PlayerStats
    /// </summary>
    public static bool TryGetLocalResources(out int resource1, out int resource2)
    {
        resource1 = resource2 = 0;

        var clientWorld = GetClientWorld();
        if (clientWorld == null) return false;

        var localPlayerId = GetLocalPlayerId(clientWorld);
        if (localPlayerId == -1) return false;

        // Find local player's PlayerStats
        var entityManager = clientWorld.EntityManager;
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerStats>());

        if (query.IsEmpty) return false;

        var allStats = query.ToComponentDataArray<PlayerStats>(Allocator.Temp);
        bool found = false;

        for (int i = 0; i < allStats.Length; i++)
        {
            if (allStats[i].playerId == localPlayerId)
            {
                resource1 = allStats[i].resource1;
                resource2 = allStats[i].resource2;
                found = true;
                break;
            }
        }

        allStats.Dispose();
        return found;
    }

    /// <summary>
    /// Check if local player can afford given costs
    /// </summary>
    public static bool CanAfford(int resource1Cost, int resource2Cost)
    {
        if (TryGetLocalResources(out int r1, out int r2))
        {
            return r1 >= resource1Cost && r2 >= resource2Cost;
        }
        return false;
    }

    /// <summary>
    /// Get missing resources for a given cost
    /// </summary>
    public static void GetMissingResources(int resource1Needed, int resource2Needed,
        out int missingResource1, out int missingResource2)
    {
        missingResource1 = missingResource2 = 0;

        if (TryGetLocalResources(out int currentR1, out int currentR2))
        {
            missingResource1 = Mathf.Max(0, resource1Needed - currentR1);
            missingResource2 = Mathf.Max(0, resource2Needed - currentR2);
        }
        else
        {
            // If we can't get current resources, assume we need everything
            missingResource1 = resource1Needed;
            missingResource2 = resource2Needed;
        }
    }

    /// <summary>
    /// Get local player's complete stats
    /// </summary>
    public static bool TryGetLocalPlayerStats(out PlayerStats stats)
    {
        stats = default;

        var clientWorld = GetClientWorld();
        if (clientWorld == null) return false;

        var localPlayerId = GetLocalPlayerId(clientWorld);
        if (localPlayerId == -1) return false;

        var entityManager = clientWorld.EntityManager;
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerStats>());

        if (query.IsEmpty) return false;

        var allStats = query.ToComponentDataArray<PlayerStats>(Allocator.Temp);
        bool found = false;

        for (int i = 0; i < allStats.Length; i++)
        {
            if (allStats[i].playerId == localPlayerId)
            {
                stats = allStats[i];
                found = true;
                break;
            }
        }

        allStats.Dispose();
        return found;
    }

    // Helper methods
    private static World GetClientWorld()
    {
        foreach (var world in World.All)
        {
            if (world.IsClient())
            {
                return world;
            }
        }
        return null;
    }

    private static int GetLocalPlayerId(World clientWorld)
    {
        if (clientWorld == null || !clientWorld.IsCreated) return -1;

        var entityManager = clientWorld.EntityManager;

        // Try to find using GhostOwnerIsLocal
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

        // Fallback: find the first NetworkStreamConnection
        using var connectionQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NetworkStreamConnection>(),
            ComponentType.ReadOnly<NetworkId>()
        );

        if (!connectionQuery.IsEmpty)
        {
            var networkIds = connectionQuery.ToComponentDataArray<NetworkId>(Allocator.Temp);
            if (networkIds.Length > 0)
            {
                var localId = networkIds[0].Value;
                networkIds.Dispose();
                return localId;
            }
            networkIds.Dispose();
        }

        return -1;
    }
}