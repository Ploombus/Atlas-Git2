using Unity.Entities;
using Unity.NetCode;
using System.Collections.Generic;
using UnityEngine;

public static class PlayerStatsUtils
{
    // Cache for performance
    private static PlayerStats? cachedLocalStats = null;
    private static int lastUpdateFrame = -1;

    public static bool TryGetLocalPlayerStats(out PlayerStats stats)
    {
        // Use cache if valid for this frame
        if (lastUpdateFrame == Time.frameCount && cachedLocalStats.HasValue)
        {
            stats = cachedLocalStats.Value;
            return true;
        }

        stats = default;
        var clientWorld = Managers.WorldManager.GetClientWorld();
        if (clientWorld == null || !clientWorld.IsCreated)
            return false;

        var em = clientWorld.EntityManager;

        // Method 1: Try to find local player's network ID through GhostOwnerIsLocal
        int localNetworkId = -1;
        var localQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<NetworkId>(),
            ComponentType.ReadOnly<GhostOwnerIsLocal>()
        );

        if (localQuery.CalculateEntityCount() > 0)
        {
            var localEntities = localQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            if (localEntities.Length > 0)
            {
                localNetworkId = em.GetComponentData<NetworkId>(localEntities[0]).Value;
            }
            localEntities.Dispose();
        }

        // Method 2: If method 1 failed, try to find through NetworkStreamInGame
        if (localNetworkId == -1)
        {
            var streamQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<NetworkId>(),
                ComponentType.ReadOnly<NetworkStreamInGame>()
            );

            if (streamQuery.CalculateEntityCount() > 0)
            {
                var streamEntities = streamQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                if (streamEntities.Length > 0)
                {
                    localNetworkId = em.GetComponentData<NetworkId>(streamEntities[0]).Value;
                }
                streamEntities.Dispose();
            }
        }

        if (localNetworkId == -1)
        {
            return false;
        }

        // Find PlayerStats with matching playerId
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerStats>());
        var playerStatsArray = query.ToComponentDataArray<PlayerStats>(Unity.Collections.Allocator.Temp);

        foreach (var playerStats in playerStatsArray)
        {
            if (playerStats.playerId == localNetworkId)
            {
                stats = playerStats;

                // Update cache
                cachedLocalStats = stats;
                lastUpdateFrame = Time.frameCount;

                playerStatsArray.Dispose();
                return true;
            }
        }

        playerStatsArray.Dispose();
        return false;
    }

    public static bool CanAfford(int resource1Cost, int resource2Cost)
    {
        if (TryGetLocalPlayerStats(out var stats))
        {
            // Check against available resources (current - reserved)
            int availableResource1 = stats.resource1 - stats.reservedResource1;
            int availableResource2 = stats.resource2 - stats.reservedResource2;

            return availableResource1 >= resource1Cost && availableResource2 >= resource2Cost;
        }
        return false;
    }

    public static bool TryGetCurrentResources(out int resource1, out int resource2)
    {
        if (TryGetLocalPlayerStats(out var stats))
        {
            resource1 = stats.resource1;
            resource2 = stats.resource2;
            return true;
        }
        resource1 = 0;
        resource2 = 0;
        return false;
    }
    public static bool TryGetAvailableResources(out int available1, out int available2)
    {
        if (TryGetLocalPlayerStats(out var stats))
        {
            available1 = stats.resource1 - stats.reservedResource1;
            available2 = stats.resource2 - stats.reservedResource2;
            return true;
        }
        available1 = 0;
        available2 = 0;
        return false;
    }

    public static bool TryGetReservedResources(out int reserved1, out int reserved2)
    {
        if (TryGetLocalPlayerStats(out var stats))
        {
            reserved1 = stats.reservedResource1;
            reserved2 = stats.reservedResource2;
            return true;
        }
        reserved1 = 0;
        reserved2 = 0;
        return false;
    }

    public static bool IsLocalPlayer(int playerId)
    {
        var clientWorld = Managers.WorldManager.GetClientWorld();
        if (clientWorld == null || !clientWorld.IsCreated)
            return false;

        var em = clientWorld.EntityManager;

        // Find local player's network ID
        foreach (var netId in em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<GhostOwnerIsLocal>())
                                 .ToComponentDataArray<NetworkId>(Unity.Collections.Allocator.Temp))
        {
            return netId.Value == playerId;
        }

        return false;
    }

    public static List<PlayerStats> GetAllPlayerStats()
    {
        var result = new List<PlayerStats>();
        var clientWorld = Managers.WorldManager.GetClientWorld();

        if (clientWorld == null || !clientWorld.IsCreated)
            return result;

        var em = clientWorld.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerStats>());
        var playerStatsArray = query.ToComponentDataArray<PlayerStats>(Unity.Collections.Allocator.Temp);

        foreach (var stats in playerStatsArray)
        {
            result.Add(stats);
        }

        playerStatsArray.Dispose();
        return result;
    }

    public static void InvalidateCache()
    {
        cachedLocalStats = null;
        lastUpdateFrame = -1;
    }
}