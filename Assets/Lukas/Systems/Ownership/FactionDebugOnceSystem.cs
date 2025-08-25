/*
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(OwnerToFactionSystem))]        // ensure factions are assigned first
[UpdateBefore(typeof(AutoTargetServerSystem))]     // dump info before targeting uses it
public partial struct FactionDebugWhenPlayersPresentSystem : ISystem
{
    bool ran;
    EntityQuery _ownedUnitsQ;

    public void OnCreate(ref SystemState state)
    {
        // We need masks present
        state.RequireForUpdate<FactionRelations>();
        state.RequireForUpdate<FactionCount>();
        state.RequireForUpdate<NetworkStreamInGame>();

        // Wait until at least one Unit with a GhostOwner exists (players actually in)
        _ownedUnitsQ = state.GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Unit>(),
                ComponentType.ReadOnly<GhostOwner>()
            }
        });
        state.RequireForUpdate(_ownedUnitsQ);
    }

    public void OnUpdate(ref SystemState state)
    {
        if (ran) return;
        ran = true;

        byte fc = SystemAPI.GetSingleton<FactionCount>().Value;
        ref var blob = ref SystemAPI.GetSingleton<FactionRelations>().Blob.Value;

        // Print mask rows actually in play (0..fc-1)
        for (int i = 0; i < fc && i < 32; i++)
        {
            uint row = blob.EnemiesMask[i];
            Debug.Log($"[Factions] EnemiesMask[{i}] = 0x{row:X8}");
        }

        // Count & list owned units and their factions
        int ownedZero = 0, totalOwned = 0;
        foreach (var (f, e) in SystemAPI.Query<RefRO<Faction>>().WithAll<Unit, GhostOwner>().WithEntityAccess())
        {
            totalOwned++;
            byte fid = (byte)(f.ValueRO.FactionId & 31);
            if (fid == 0) ownedZero++;
            int owner = SystemAPI.GetComponent<GhostOwner>(e).NetworkId;
            Debug.Log($"[Factions] Unit {e.Index}:{e.Version} owner={owner} faction={fid}");
        }
        Debug.Log($"[Factions] Owned units: {totalOwned}, of which FactionId==0: {ownedZero}");
    }
}
*/