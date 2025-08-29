using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Managers;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct RallyPointServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()))
            return;

        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // Process rally point RPCs
        foreach (var (rpc, request, rpcEntity) in
                SystemAPI.Query<RefRO<SetRallyPointRpc>, RefRO<ReceiveRpcCommandRequest>>()
                          .WithEntityAccess())
        {

            var connection = request.ValueRO.SourceConnection;
            var requesterNetId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            var buildingEntity = rpc.ValueRO.buildingEntity;
            var rallyPosition = rpc.ValueRO.rallyPosition;

            // Validate that the building exists
            if (!SystemAPI.Exists(buildingEntity))
            {
                buffer.DestroyEntity(rpcEntity);
                continue;
            }

            // Validate that the requester owns this building
            if (!SystemAPI.HasComponent<GhostOwner>(buildingEntity))
            {
                buffer.DestroyEntity(rpcEntity);
                continue;
            }

            var buildingOwner = SystemAPI.GetComponent<GhostOwner>(buildingEntity);
            if (buildingOwner.NetworkId != requesterNetId)
            {
                buffer.DestroyEntity(rpcEntity);
                continue;
            }

            // Validate that this is actually a building
            if (!SystemAPI.HasComponent<Building>(buildingEntity))
            {
                buffer.DestroyEntity(rpcEntity);
                continue;
            }

            // Set or update the rally point
            if (SystemAPI.HasComponent<RallyPoint>(buildingEntity))
            {
                // Update existing rally point
                var currentRallyPoint = SystemAPI.GetComponent<RallyPoint>(buildingEntity);
                buffer.SetComponent(buildingEntity, new RallyPoint
                {
                    position = rallyPosition,
                    isSet = true
                });
            }
            else
            {
                // Add new rally point component
                buffer.AddComponent(buildingEntity, new RallyPoint
                {
                    position = rallyPosition,
                    isSet = true
                });
            }


            // Clean up the RPC
            buffer.DestroyEntity(rpcEntity);
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}