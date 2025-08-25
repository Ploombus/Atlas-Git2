/*
using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct DebugTickDamageSystem : ISystem
{
    double _next;

    public void OnCreate(ref SystemState state) => _next = 0;

    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.Time.ElapsedTime < _next) return;
        _next = SystemAPI.Time.ElapsedTime + 1.5; // every 1.5s

        foreach (var hs in SystemAPI.Query<RefRW<HealthState>>())
        {
            hs.ValueRW.healthChange -= 1; // one step of damage
        }
    }
}
*/