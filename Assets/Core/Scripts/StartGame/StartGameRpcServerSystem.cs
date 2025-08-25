using Unity.Entities;
using Managers;
using Unity.NetCode;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct StartGameRpcServerSystem : ISystem
{
    public static void BroadcastStartGameRpc(EntityManager em)
    {
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
        var connections = query.ToEntityArray(Unity.Collections.Allocator.Temp);

        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        foreach (var conn in connections)
        {
            var rpcEntity = ecb.CreateEntity();
            ecb.AddComponent(rpcEntity, new StartGameRpc());
            ecb.AddComponent(rpcEntity, new SendRpcCommandRequest
            {
                TargetConnection = conn
            });
        }

        ecb.Playback(em);
        ecb.Dispose();
        connections.Dispose();
    }
}
