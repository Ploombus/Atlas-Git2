using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;
using Unity.NetCode;
using Unity.Transforms;
using Managers;


[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct GoInGameServerSystem : ISystem
{
    public void OnStart(ref SystemState state)
    {
        //state.RequireForUpdate<EntitiesReferences>();
        state.RequireForUpdate<NetworkId>();
        Debug.Log("Server Created.");
    }

    //[BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        foreach ((
            RefRO<ReceiveRpcCommandRequest> receiveRpcCommandRequest,
            Entity entity)
            in SystemAPI.Query<
                RefRO<ReceiveRpcCommandRequest>>().WithAll<GoInGameRequestRpc>().WithEntityAccess())
        {
            Debug.Log("Player is trying to establish connection...");
            buffer.AddComponent<NetworkStreamInGame>(receiveRpcCommandRequest.ValueRO.SourceConnection);
            //Get Network ID
            NetworkId networkId = SystemAPI.GetComponent<NetworkId>(receiveRpcCommandRequest.ValueRO.SourceConnection);
            buffer.AddComponent<PendingPlayerSpawn>(receiveRpcCommandRequest.ValueRO.SourceConnection);
            Debug.Log($"Player with network ID :: {networkId.Value} :: entered the lobby.");

            buffer.DestroyEntity(entity);
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}
public struct PendingPlayerSpawn : IComponentData { }
