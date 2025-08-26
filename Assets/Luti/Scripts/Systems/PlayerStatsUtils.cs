using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public static class PlayerStatsUtils
{
    public static bool TryGetLocalResources(out int resource1, out int resource2)
    {
        resource1 = resource2 = 0;

        if (TryGetLocalPlayerStats(out var stats))
        {
            resource1 = stats.resource1;
            resource2 = stats.resource2;
            return true;
        }

        return false;
    }

    public static bool TryGetLocalPlayerStats(out PlayerStats stats)
    {
        stats = default;

        var clientWorld = GetClientWorld();
        if (clientWorld == null) return false;

        var localPlayerId = GetLocalPlayerId(clientWorld);
        if (localPlayerId == -1) return false;

        return TryGetPlayerStatsById(clientWorld, localPlayerId, out stats);
    }

    public static bool TryGetPlayerStatsById(World world, int playerId, out PlayerStats stats)
    {
        stats = default;

        if (world == null || !world.IsCreated) return false;

        var entityManager = world.EntityManager;
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerStats>());

        if (query.IsEmpty) return false;

        var allStats = query.ToComponentDataArray<PlayerStats>(Allocator.Temp);
        bool found = false;

        for (int i = 0; i < allStats.Length; i++)
        {
            if (allStats[i].playerId == playerId)
            {
                stats = allStats[i];
                found = true;
                break;
            }
        }

        allStats.Dispose();
        return found;
    }

    public static bool CanAfford(int resource1Cost, int resource2Cost)
    {
        if (TryGetLocalResources(out int currentR1, out int currentR2))
        {
            return currentR1 >= resource1Cost && currentR2 >= resource2Cost;
        }
        return false;
    }

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
            missingResource1 = resource1Needed;
            missingResource2 = resource2Needed;
        }
    }

    public static List<PlayerStats> GetAllPlayerStats(World world)
    {
        var result = new List<PlayerStats>();

        if (world == null || !world.IsCreated) return result;

        var entityManager = world.EntityManager;
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerStats>());

        if (query.IsEmpty) return result;

        var allStats = query.ToComponentDataArray<PlayerStats>(Allocator.Temp);

        for (int i = 0; i < allStats.Length; i++)
        {
            // Only include valid player data (playerId should be >= 0)
            if (allStats[i].playerId >= 0)
            {
                result.Add(allStats[i]);
            }
        }

        allStats.Dispose();
        return result;
    }

    public static bool IsLocalPlayer(int playerId)
    {
        var clientWorld = GetClientWorld();
        if (clientWorld == null) return false;

        var localPlayerId = GetLocalPlayerId(clientWorld);
        return localPlayerId != -1 && localPlayerId == playerId;
    }

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

        // Method 1: Try GhostOwnerIsLocal (most reliable for NetCode)
        using (var ghostOwnerQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<GhostOwner>(),
            ComponentType.ReadOnly<GhostOwnerIsLocal>()
        ))
        {
            if (!ghostOwnerQuery.IsEmpty)
            {
                var ghostOwners = ghostOwnerQuery.ToComponentDataArray<GhostOwner>(Allocator.Temp);
                if (ghostOwners.Length > 0)
                {
                    var localId = ghostOwners[0].NetworkId;
                    ghostOwners.Dispose();
                    return localId;
                }
                ghostOwners.Dispose();
            }
        }

        return -1; // No local player found
    }

    public static void LogLocalPlayerStats()
    {
        if (TryGetLocalPlayerStats(out var stats))
        {
            Debug.Log($"Local Player Stats - ID: {stats.playerId}, " +
                     $"R1: {stats.resource1}, R2: {stats.resource2}, " +
                     $"Score: {stats.totalScore}");
        }
        else
        {
            Debug.Log("No local player stats available");
        }
    }

    public static void LogAllPlayerStats()
    {
        var clientWorld = GetClientWorld();
        if (clientWorld == null)
        {
            Debug.Log("No client world found");
            return;
        }

        var allStats = GetAllPlayerStats(clientWorld);
        Debug.Log($"Found {allStats.Count} player(s) with stats:");

        foreach (var stats in allStats)
        {
            var isLocal = IsLocalPlayer(stats.playerId);
            Debug.Log($"Player {stats.playerId} {(isLocal ? "(LOCAL)" : "")}: " +
                     $"R1={stats.resource1}, R2={stats.resource2}, Score={stats.totalScore}");
        }
    }
}