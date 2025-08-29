using Unity.Entities;
using UnityEngine;
using Managers;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(UnitAnimateSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct ApplyPlayerTintSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false) return;

        foreach (var (animRef, player) in
                 SystemAPI.Query<UnitAnimatorReference, RefRO<Owner>>()
                          .WithChangeFilter<Owner>()) // only when value changes
        {
            var go = animRef.Value.gameObject;
            var tint = go.GetComponentInChildren<TintTarget>(true);
            if (tint == null || tint.rendererRef == null) continue;

            var p = player.ValueRO.OwnerColor;
            var mpb = new MaterialPropertyBlock();
            tint.rendererRef.GetPropertyBlock(mpb);
            var c = new Color(p.x, p.y, p.z, p.w);
            mpb.SetColor("_BaseColor", c); // URP
            mpb.SetColor("_Color", c); // Built-in
            tint.rendererRef.SetPropertyBlock(mpb);
        }

        foreach (var (animRef, player) in
                 SystemAPI.Query<BarracksAnimatorReference, RefRO<Owner>>()
                          .WithChangeFilter<Owner>()) // only when value changes
        {
            var go = animRef.Value.gameObject;
            var tint = go.GetComponentInChildren<TintTarget>(true);
            if (tint == null || tint.rendererRef == null) continue;

            var p = player.ValueRO.OwnerColor;
            var mpb = new MaterialPropertyBlock();
            tint.rendererRef.GetPropertyBlock(mpb);
            var c = new Color(p.x, p.y, p.z, p.w);
            mpb.SetColor("_BaseColor", c); // URP
            mpb.SetColor("_Color", c); // Built-in
            tint.rendererRef.SetPropertyBlock(mpb);
        }
    }
}