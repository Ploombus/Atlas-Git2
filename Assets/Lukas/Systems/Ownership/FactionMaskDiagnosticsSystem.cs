/*
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct FactionMaskDiagnosticsSystem : ISystem
{
    bool _ran;
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FactionRelations>();
        state.RequireForUpdate<FactionCount>();
    }
    public void OnUpdate(ref SystemState state)
    {
        if (_ran) return; _ran = true;
        var rel = SystemAPI.GetSingleton<FactionRelations>();
        var fc  = SystemAPI.GetSingleton<FactionCount>().Value;
        ref var blob = ref rel.Blob.Value;
        for (int i = 0; i < fc && i < 32; i++)
        {
            if (blob.EnemiesMask[i] == 0u)
                UnityEngine.Debug.LogWarning($"[Factions] EnemiesMask[{i}] is zero — faction {i} has no enemies.");
        }
    }
}
*/