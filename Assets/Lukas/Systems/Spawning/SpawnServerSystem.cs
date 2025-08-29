using Unity.Entities;
using Managers;
using Unity.NetCode;
using UnityEngine;
using Unity.Mathematics;
using Unity.Transforms;


[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct SpawnServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EntitiesReferencesLukas>();
        state.RequireForUpdate<EntitiesReferencesLuti>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()))
            return;

        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var unitRef = SystemAPI.GetSingleton<EntitiesReferencesLukas>();
        var buildingReferences = SystemAPI.GetSingleton<EntitiesReferencesLuti>();

        //Start game spawner
        foreach ((RefRO<NetworkId> netId, Entity entity)
                 in SystemAPI.Query<RefRO<NetworkId>>()
                             .WithAll<PendingPlayerSpawn>()
                             .WithEntityAccess())
        {
            float3 basePosition = new float3(RandomWithGap(-90, -60, 60, 90), 0f, RandomWithGap(-90, -60, 60, 90));
            float spacing = 1f;

            for (int i = 0; i < 5; i++)
            {
                float3 unitPosition = basePosition + new float3(spacing * i - 2f, 0f, -8f);
                var unitEntity = buffer.Instantiate(unitRef.unitPrefabEntity);
                buffer.AddComponent(unitEntity, new GhostOwner { NetworkId = netId.ValueRO.Value });
                buffer.SetComponent(unitEntity, LocalTransform.FromPosition(unitPosition));

                buffer.SetComponent(unitEntity, SpawnTargetsAt(unitPosition));

                int ownerId = netId.ValueRO.Value;
                var rgba = PlayerColorUtil.FromId(ownerId);
                buffer.SetComponent(unitEntity, new Owner { OwnerColor = rgba });

                buffer.AppendToBuffer(entity, new LinkedEntityGroup { Value = unitEntity });
            }
            // Spawn Barracks building for the player
            float3 barracksPosition = basePosition + new float3(0f, 0f, 0f); // Position barracks 5 units behind the units
            var barracksEntity = buffer.Instantiate(buildingReferences.buildingPrefabEntity);

            buffer.SetComponent(barracksEntity, LocalTransform.FromPosition(barracksPosition.x, 0f, barracksPosition.z));
            buffer.AddComponent(barracksEntity, new GhostOwner { NetworkId = netId.ValueRO.Value });

            // Set player color for the barracks (same as units)
            int ownerId2 = netId.ValueRO.Value;
            var rgba2 = PlayerColorUtil.FromId(ownerId2);
            buffer.SetComponent(barracksEntity, new Owner { OwnerColor = rgba2 });

            buffer.AppendToBuffer(entity, new LinkedEntityGroup { Value = barracksEntity });

            var rpcEntity = buffer.CreateEntity();
            buffer.AddComponent(rpcEntity, new CenterCameraRpc { position = basePosition });
            buffer.AddComponent(rpcEntity, new SendRpcCommandRequest { TargetConnection = entity }); // 'entity' is the connection here

            buffer.RemoveComponent<PendingPlayerSpawn>(entity); // prevent re-spawning
        }

        //Button spawners
        foreach (var (rpc, request, rpcEntity)
        in SystemAPI.Query<RefRO<SpawnUnitRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
        {
            // Find who sent it
            var connection = request.ValueRO.SourceConnection;
            var owner = rpc.ValueRO.owner;
            var netId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            float3 position = rpc.ValueRO.position;

            var unitEntity = buffer.Instantiate(unitRef.unitPrefabEntity);

            buffer.SetComponent(unitEntity, LocalTransform.FromPosition(position));
            buffer.SetComponent(unitEntity, SpawnTargetsAt(position));

            if (owner == 1)
            {
                buffer.AddComponent(unitEntity, new GhostOwner { NetworkId = netId });
            }
            if (owner == -1)
            {
                buffer.AddComponent(unitEntity, new GhostOwner { NetworkId = -1 });
            }

            var colorId = (owner == -1) ? -1 : netId;
            var rgba = PlayerColorUtil.FromId(colorId);
            buffer.SetComponent(unitEntity, new Owner { OwnerColor = rgba });

            // consume RPC
            buffer.DestroyEntity(rpcEntity);
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }

    float RandomWithGap(int negmax, int neggap, int posgap, int posmax)
    {
        if (UnityEngine.Random.value < 0.5f)
            return UnityEngine.Random.Range(negmax, neggap);
        else
            return UnityEngine.Random.Range(posgap, posmax);
    }

    public static UnitTargets SpawnTargetsAt(float3 position) => new UnitTargets
    {
        destinationPosition = position,
        destinationRotation = 3.14f,
        targetPosition      = float3.zero,
        targetRotation      = float.NaN,
        lastAppliedSequence = 0,
        activeTargetSet     = false,
        targetEntity        = Entity.Null,
        hasArrived          = false
    };
}

public struct CenterCameraRpc : IRpcCommand
{
    public float3 position; // where the rig should move (XZ), keep your current Y
}
