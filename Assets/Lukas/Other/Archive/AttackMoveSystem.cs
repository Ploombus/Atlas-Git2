/*using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct AttackMoveSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var unitTargets in SystemAPI.Query<RefRW<UnitTargets>>().WithAll<Attacker>())
        {
            if (!unitTargets.ValueRO.attackMove)
            {
                var updated = unitTargets.ValueRW;
                updated.attackMove = true;   // start with attack-move ON
                unitTargets.ValueRW = updated;
            }
        }

        // Run once
        state.Enabled = false;
    }
}*/