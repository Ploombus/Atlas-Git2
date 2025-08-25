/*using Managers;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// Server-side system that manages player resources and syncs them with clients
/// Each player connection entity has a PlayerStats component
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ServerResourceManagementSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkTime>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetServerWorld()))
            return; 
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // Initialize resources for new player connections
        foreach (var (netId, entity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithNone<PlayerStats>()
            .WithAll<NetworkStreamConnection>()
            .WithEntityAccess())
        {
            // Give new players starting resources
            buffer.AddComponent(entity, new PlayerStats
            {
                resource1 = 100, // Starting resources
                resource2 = 100
            });

        }

    }
}

// RPC to refund resources to client if server validation fails
public struct ResourceRefundRpc : IRpcCommand
{
    public int resource1Amount;
    public int resource2Amount;
}*/