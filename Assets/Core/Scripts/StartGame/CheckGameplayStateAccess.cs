using UnityEngine;
using Managers;
using Unity.Entities;

public static class CheckGameplayStateAccess
{
    public static bool GetGameplayState(World world)
    {
        if (world == null)
        {
            return false;
        }
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<CheckGameplayState>());
        if (query.IsEmpty)
        {
            return false;
        }

        return query.GetSingleton<CheckGameplayState>().GameplayState;
    }

    public static void SetGameplayState(World world, bool isInGame)
    {
        if (world == null)
        {
            return;
        }

        var em = world.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadWrite<CheckGameplayState>());
        if (query.IsEmpty)
        {
            return;
        }

        var entity = query.GetSingletonEntity();
        em.SetComponentData(entity, new CheckGameplayState { GameplayState = isInGame });
    }
}

// Use on Client to get state
// bool isInGame = CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld());

// Use on Server to set state
// CheckGameplayStateAccess.SetGameplayState(WorldManager.GetServerWorld(), true);