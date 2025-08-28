using Managers;
using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.InputSystem.Processors;
using UnityEngine.LightTransport;
using static Unity.Entities.EntitiesJournaling;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct EntitiesEventSystem : ISystem
{

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EntitiesReferencesLuti>();

    }

    public void OnUpdate(ref SystemState state)
    {



        var prefabRef = SystemAPI.GetSingleton<EntitiesReferencesLuti>();
        var spawnEntityBuffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);


        foreach (var (rpc, request, rpcEntity)
         in SystemAPI.Query<RefRO<SpawnBarracksRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
        {
            // Find who sent it
            var connection = request.ValueRO.SourceConnection;
            var owner = rpc.ValueRO.owner;
            var netId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            float3 position = rpc.ValueRO.position;

            var unitEntity = spawnEntityBuffer.Instantiate(prefabRef.buildingPrefabEntity);

            spawnEntityBuffer.SetComponent(unitEntity, LocalTransform.FromPosition(position));
            

            if (owner == 1)
            {
                spawnEntityBuffer.AddComponent(unitEntity, new GhostOwner { NetworkId = netId });
            }
            if (owner == -1)
            {
                spawnEntityBuffer.AddComponent(unitEntity, new GhostOwner { NetworkId = -1 });
            }

            var colorId = (owner == -1) ? -1 : netId;
            var rgba = PlayerColorUtil.FromId(colorId);
            spawnEntityBuffer.SetComponent(unitEntity, new Owner { OwnerColor = rgba });

            // consume RPC
            spawnEntityBuffer.DestroyEntity(rpcEntity);
        }

        spawnEntityBuffer.Playback(state.EntityManager);
        spawnEntityBuffer.Dispose();

    }
}

/*public struct PendingBuildingSpawn : IComponentData 
{

}*/

/* use if another system is checking BuildingModeEnd
        ComponentRequestQueue.BuildingModeEnd.Add(new AddComponentRequest()); luti*/













/*foreach ((RefRW<Building> building,
    RefRW<LocalTransform> localTransform) 
    in SystemAPI.Query<RefRW<Building>, RefRW<LocalTransform>>().WithAll<Player, Simulate>())
{

    if (building.ValueRO.inBuildMode)
    {

        Entity buildingPrefabEntity = state.EntityManager.Instantiate(entitiesReferences.buildingPrefabEntity);

        SystemAPI.SetComponent(buildingPrefabEntity, LocalTransform.FromPosition(MouseWorldPosition.Instance.GetPosition()));
        building.ValueRW.inBuildMode = false;
        Debug.Log("Building spawned!");
    }
    else
    {
        Debug.Log("No Building to spawn, nothing executed");
    }
}


}*/
