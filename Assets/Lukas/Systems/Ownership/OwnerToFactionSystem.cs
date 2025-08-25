using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(AutoTargetServerSystem))]
[UpdateBefore(typeof(CombatServerSystem))]
public partial struct OwnerToFactionSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        state.RequireForUpdate<FactionCount>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        var em = state.EntityManager;
        byte fc    = SystemAPI.GetSingleton<FactionCount>().Value;
        byte slots = (byte)math.max(1, fc - 1); // reserve 0 for Neutral

        foreach (var (factionRW, entity) in
                 SystemAPI.Query<RefRW<Faction>>()
                          .WithAll<Unit, GhostOwner>()
                          .WithNone<FactionInitialized>()
                          .WithEntityAccess())
        {
            int nid = em.GetComponentData<GhostOwner>(entity).NetworkId; // can be -1, 0, 1..N

            // NEW: neutrals/unowned keep their authored faction (expected 0)
            if (nid <= 0)
            {
                // do not remap — leave FactionId as authored (e.g., 0)
                ecb.AddComponent<FactionInitialized>(entity);
                continue;
            }

            // Players: map to 1..slots (wrap if needed)
            byte fid = (byte)(1 + ((nid - 1) % slots));
            factionRW.ValueRW.FactionId = (byte)(fid & 31);

            ecb.AddComponent<FactionInitialized>(entity);
        }
    }
}

public struct FactionInitialized : IComponentData { }