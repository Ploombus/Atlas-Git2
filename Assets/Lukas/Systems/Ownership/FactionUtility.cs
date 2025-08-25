using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Collections;

public static class FactionUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte EffectiveFaction(Entity e, EntityManager em)
    {
        if (em.HasComponent<TemporaryFactionOverride>(e))
        {
            var t = em.GetComponentData<TemporaryFactionOverride>(e);
            if (t.SecondsLeft > 0f) return (byte)(t.FactionId & 31);
        }
        return em.HasComponent<Faction>(e) ? (byte)(em.GetComponentData<Faction>(e).FactionId & 31) : (byte)0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AreHostile(byte a, byte b, FactionRelations rel, byte factionCount)
    {
        if (!rel.Blob.IsCreated) return false;               // mask is required
        a = (byte)(a & 31); b = (byte)(b & 31);
        if (a >= factionCount || b >= factionCount) return false;

        ref var blob = ref rel.Blob.Value;
        ref var masks = ref blob.EnemiesMask;
        uint row = masks[a];
        if (row == 0u) return false;                         // unset row = no enemies
        return (row & (1u << b)) != 0u;
    }
}