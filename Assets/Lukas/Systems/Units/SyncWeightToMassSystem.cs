using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;

[UpdateInGroup(typeof(PhysicsSystemGroup))]
//[UpdateBefore(typeof(BuildPhysicsWorld))]
partial struct SyncWeightToMassSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach ((
            RefRO<UnitStats> unitStats,
            RefRO<PhysicsCollider> physicsCollider,
            RefRW<PhysicsMass> physicsMass,
            RefRW<UnitWeightCache> weightCache,
            Entity e
        ) in SystemAPI
            .Query<
                RefRO<UnitStats>,
                RefRO<PhysicsCollider>,
                RefRW<PhysicsMass>,
                RefRW<UnitWeightCache>
            >()
            .WithEntityAccess())
        {
            // Only dynamic bodies
            if (physicsMass.ValueRO.InverseMass <= 0f)
                continue;

            float w = math.clamp(unitStats.ValueRO.weight, 0.1f, 1e6f);

            // Run if first time (NaN) or changed meaningfully
            bool firstTime = float.IsNaN(weightCache.ValueRO.lastWeight);
            if (!firstTime && math.abs(w - weightCache.ValueRO.lastWeight) <= 0.01f)
                continue;

            // Recompute mass from collider mass properties
            var collider  = physicsCollider.ValueRO.Value;
            var massProps = collider.Value.MassProperties;
            physicsMass.ValueRW = PhysicsMass.CreateDynamic(massProps, w);

            weightCache.ValueRW.lastWeight = w;
        }
    }
}