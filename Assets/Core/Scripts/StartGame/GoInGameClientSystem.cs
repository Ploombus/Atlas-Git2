using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Managers;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct GoInGameClientSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        //state.RequireForUpdate<NetworkId>();
    }

    //[BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<NetworkId>())
        return;
        
        EntityCommandBuffer buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        foreach ((
            RefRO<NetworkId> networkId, Entity entity)
            in SystemAPI.Query<
                RefRO<NetworkId>>().WithNone<NetworkStreamInGame>().WithEntityAccess())
        {
            buffer.AddComponent<NetworkStreamInGame>(entity);
            Entity rpcEntity = buffer.CreateEntity();
            buffer.AddComponent(rpcEntity, new GoInGameRequestRpc());
            buffer.AddComponent(rpcEntity, new SendRpcCommandRequest());

            Debug.Log($"Trying to connect with Network ID :: {networkId.ValueRO.Value}");
        }
        buffer.Playback(state.EntityManager);
        buffer.Dispose();

        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false)
        {
            return;
        }

        EntityCommandBuffer gameplayBuffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        //Logic related to gameplay goes here

        gameplayBuffer.Playback(state.EntityManager);
        gameplayBuffer.Dispose();
    }
}

public struct GoInGameRequestRpc : IRpcCommand { }
