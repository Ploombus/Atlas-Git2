
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.Collections;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct ChatServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GhostCollection>();
    }

    //[BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Allocator.Temp);

        var connections = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>())
            .ToEntityArray(Allocator.Temp);

        foreach (var (rpc, request, entity)
            in SystemAPI.Query<
                RefRO<ChatMessageRPC>,
                RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
        {
            foreach (var connection in connections)
            {
                var broadcastEntity = buffer.CreateEntity();
                buffer.AddComponent(broadcastEntity, new ChatMessageRPC
                {
                    message = $"{request.ValueRO.SourceConnection.Index} :: {rpc.ValueRO.message}"
                });
                buffer.AddComponent(broadcastEntity, new SendRpcCommandRequest
                {
                    TargetConnection = connection
                });
            }

            buffer.DestroyEntity(entity);
        }

        connections.Dispose();
        buffer.Playback(state.EntityManager);
    }

}