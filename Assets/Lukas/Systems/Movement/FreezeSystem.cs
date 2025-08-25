using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Physics;

[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
public partial struct FreezeSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsVelocity>();
    }

    public void OnUpdate(ref SystemState state)
    {
        foreach ((
            RefRW<LocalTransform> localTransform,
            RefRW<PhysicsVelocity> physicsVelocity)
            in SystemAPI.Query<
                RefRW<LocalTransform>,
                RefRW<PhysicsVelocity>>().WithAll<Simulate>()
                .WithNone<TargetArrow>())
        {

            //physicsVelocity.ValueRW.Angular.x = 100f;
            localTransform.ValueRW.Rotation.value.x = 0f;
            localTransform.ValueRW.Rotation.value.z = 0f;
            localTransform.ValueRW.Position.y = 0f;
            
            /*
            if (math.abs(r.Rotation.value.x) > 0.001)
            {
                v.Angular.x = 100 * r.Rotation.value.x;
            }
            if (math.abs(r.Rotation.value.z) > 0.001)
            {
                v.Angular.z = 100 * r.Rotation.value.z;
            }
            if (math.abs(r.Rotation.value.y) > 0.001)
            {
                v.Angular.y = 100 * r.Rotation.value.y;
            }
            */
        }
    }
}