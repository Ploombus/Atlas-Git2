using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;


[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct TreeSpawnerServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EntitiesReferencesLukas>();
        state.RequireForUpdate<GhostCollection>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.HasSingleton<TreeSpawningDone>()) return;
        
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var entitiesReferences = SystemAPI.GetSingleton<EntitiesReferencesLukas>();
        
        var rng = new Unity.Mathematics.Random((uint)(System.DateTime.UtcNow.Ticks & 0xFFFFFFFF));

        //Tree spawner
        for (int i = 0; i < 50; i++)
        {
            float3 position = new float3(rng.NextFloat(-95f, 95f), 0f, rng.NextFloat(-95f, 95f));
            float scale = rng.NextFloat(0.4f, 0.6f);
            quaternion rotation = quaternion.RotateY(rng.NextFloat(0, 2f * math.PI));

            var treeEntity = buffer.Instantiate(entitiesReferences.treePrefabEntity);
            buffer.SetComponent(treeEntity, LocalTransform.FromPositionRotationScale(position, rotation, scale));
        }

        var done = buffer.CreateEntity();
        buffer.AddComponent<TreeSpawningDone>(done);

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}
public struct TreeSpawningDone : IComponentData {}