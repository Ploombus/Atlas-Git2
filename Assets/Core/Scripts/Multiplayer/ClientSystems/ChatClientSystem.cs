using UnityEngine;
using Unity.NetCode;
using Unity.Entities;
using Unity.Collections;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct ChatNetcodeClient : ISystem
{

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkId>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Allocator.Temp);

        // Send outgoing RPCs
        foreach (var (request, entity) in SystemAPI.Query<RefRO<RequestMessage>>().WithEntityAccess())
        {
            var rpcEntity = buffer.CreateEntity();
            buffer.AddComponent(rpcEntity, new ChatMessageRPC
            {
                message = request.ValueRO.message
            });
            buffer.AddComponent<SendRpcCommandRequest>(rpcEntity);
            buffer.DestroyEntity(entity);
        }

        // Consume received RPCs (no ReceiveRpcCommandRequest here!)
        foreach (var (rpc, entity) in SystemAPI.Query<RefRO<ChatMessageRPC>>().WithEntityAccess())
        {
            Debug.Log($"[Chat] {rpc.ValueRO.message}");
            buffer.DestroyEntity(entity);
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
    
    public void OnDestroy(ref SystemState state)
    {
    }

}
