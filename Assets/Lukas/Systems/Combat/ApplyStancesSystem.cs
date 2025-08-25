using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct ApplyStancesFromInputSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (attackerRW, netRO) in SystemAPI
                 .Query<RefRW<Attacker>, RefRO<UnitTargetsNetcode>>())
        {
            var req = netRO.ValueRO.requestStance;
            if (req == Stance.None) continue;

            float desiredMeters;
            switch (req)
            {
                case Stance.Aggressive: desiredMeters = -1f; break;
                case Stance.Defensive:  desiredMeters = 10f; break;
                case Stance.HoldGround: desiredMeters = 0f;  break;
                default: continue;
            }

            ref var a = ref attackerRW.ValueRW;
            // idempotent: only write if something would change
            if (!a.autoTarget || a.maxChaseMeters != desiredMeters)
            {
                a.autoTarget     = true;
                a.maxChaseMeters = desiredMeters;
            }
        }
    }
}

public enum Stance
{
    None       = 0, // means "no request"
    Aggressive = 1,
    Defensive  = 2,
    HoldGround = 3
}
