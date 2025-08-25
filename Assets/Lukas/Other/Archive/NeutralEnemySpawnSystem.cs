/*
using Unity.Entities;
using Managers;
using Unity.NetCode;
using UnityEngine;
using Unity.Mathematics;
using Unity.Transforms;


[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct NeutralEnemySpawnServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EntitiesReferencesLukas>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetServerWorld()))
            return;



        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var references = SystemAPI.GetSingleton<EntitiesReferencesLukas>();
        //var mousePosition = new MouseWorldPosition.GetPosition()

        var neutralEntity = buffer.Instantiate(references.unitPrefabEntity);
        buffer.SetComponent(neutralEntity, LocalTransform.FromPosition(0, 0, 0));
        buffer.AddComponent(neutralEntity, new GhostOwner { NetworkId = -1 });
        
        buffer.Playback(state.EntityManager);
        buffer.Dispose();
        
    }
}
*/