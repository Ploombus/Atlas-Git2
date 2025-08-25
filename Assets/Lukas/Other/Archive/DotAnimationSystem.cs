/*
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct DotAnimationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MovementDot>();
    }

    public void OnUpdate(ref SystemState state)
    {
        float time = (float)SystemAPI.Time.ElapsedTime;

        foreach (var transform in SystemAPI.Query<RefRW<LocalTransform>>()
                 .WithAll<MovementDot>())
        {
            float scale = 0.1f + math.sin(time * 5f) * 0.05f; // base + pulse
            transform.ValueRW.Scale = scale;
        }
    }
}
*/