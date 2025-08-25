/*
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.Collections;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct NetcodeServer : ISystem
{
    //private Entity receivedMessagePrefab;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GhostCollection>();
        state.RequireForUpdate<EntitiesReferences>();
    }


    //[BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Allocator.Temp);

        EntitiesReferences entitiesReferences = SystemAPI.GetSingleton<EntitiesReferences>();


        foreach (var (rpc, request, entity)
            in SystemAPI.Query<
                RefRO<ChatMessageRPC>,
                RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
        {
            Debug.Log($"{request.ValueRO.SourceConnection} : {rpc.ValueRO.message}");

            buffer.DestroyEntity(entity);
        }

        buffer.Playback(state.EntityManager);
    }
}
*/

//Creates ghost messageEntity
//Entity messageEntity = buffer.Instantiate(entitiesReferences.chatMessagePrefabEntity);
//buffer.SetComponent(messageEntity, new ReceivedMessage { message = rpc.ValueRO.message });
