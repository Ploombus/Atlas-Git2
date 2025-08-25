/*
using Unity.Burst;
using Unity.Entities;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct GameStateSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        EntityManager em = state.EntityManager;
        if (!em.CreateEntityQuery(typeof(CheckState)).IsEmpty)
            return;

        var entity = em.CreateEntity();
        em.AddComponentData(entity, new CheckState { IsInGame = false });
    }
}

//Reference to check if the game started
public struct CheckState : IComponentData
{
    public bool IsInGame;
}
*/