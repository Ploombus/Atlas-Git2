using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct TargetArrowVisualSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        state.RequireForUpdate<EntitiesReferencesLukas>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var em   = state.EntityManager;
        var time = (float)SystemAPI.Time.ElapsedTime;
        var refs = SystemAPI.GetSingleton<EntitiesReferencesLukas>();
        var ecb  = new EntityCommandBuffer(Allocator.Temp);

        const float hover = 1.2f;
        const float baseScale = 0.13f;

        var sizeLookup = SystemAPI.GetComponentLookup<TargetingSize>(true);
        var ltLookup   = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var ltwLookup  = SystemAPI.GetComponentLookup<LocalToWorld>(true);

        float TopY(Entity target)
        {
            if (sizeLookup.HasComponent(target))
            {
                var ts = sizeLookup[target];
                var baseY = ts.height > 0f ? ts.height : ts.radius;
                return baseY + hover;
            }
            return hover;
        }

        bool TryGetWorldPos(Entity e, out float3 pos)
        {
            if (ltLookup.HasComponent(e))  { pos = ltLookup[e].Position;  return true; }
            if (ltwLookup.HasComponent(e)) { pos = ltwLookup[e].Position; return true; }
            pos = default; return false;
        }

        // collect desired targets
        var desiredTargets = new NativeHashSet<Entity>(64, Allocator.Temp);
        foreach (var unitTargets in SystemAPI.Query<RefRO<UnitTargets>>().WithAll<GhostOwnerIsLocal, Selected>())
        {
            var t = unitTargets.ValueRO.targetEntity;
            if (t != Entity.Null) desiredTargets.Add(t);
        }

        // update existing arrows
        var existingTargets = new NativeHashSet<Entity>(64, Allocator.Temp);
        var arrowsToDestroy = new NativeList<Entity>(Allocator.Temp);

        foreach (var (arrowTx, bind, arrow) in SystemAPI
                     .Query<RefRW<LocalTransform>, RefRO<TargetArrowBind>>()
                     .WithAll<TargetArrow>()
                     .WithEntityAccess())
        {
            var target = bind.ValueRO.target;
            existingTargets.Add(target);

            if (!em.Exists(target) || !desiredTargets.Contains(target) || !TryGetWorldPos(target, out var targetPos))
            {
                arrowsToDestroy.Add(arrow);
                continue;
            }

            var basePos   = targetPos + new float3(0f, TopY(target), 0f);
            var bobPhase  = target.Index * 0.73f;
            var bobOffset = math.sin(time * 3.5f + bobPhase) * 0.08f;
            var arrowPos  = basePos; arrowPos.y += bobOffset;

            var pulsePhase = target.Index * 0.41f;
            var pulse      = 1f + math.sin(time * 5.0f + pulsePhase) * 0.15f;
            var finalScale = baseScale * pulse;

            arrowTx.ValueRW.Position = arrowPos;
            arrowTx.ValueRW.Scale    = finalScale;

            var cam = Camera.main;
            if (cam != null)
            {
                float3 toCam = (float3)cam.transform.position - arrowPos;
                toCam.y = 0f;
                if (math.lengthsq(toCam) > 1e-6f)
                {
                    var faceDir = math.normalize(toCam);
                    arrowTx.ValueRW.Rotation = quaternion.LookRotationSafe(faceDir, math.up());
                }
            }
        }

        // destroy culled arrows
        for (int i = 0; i < arrowsToDestroy.Length; i++)
            ecb.DestroyEntity(arrowsToDestroy[i]);

        // spawn arrows for new targets
        using (var it = desiredTargets.GetEnumerator())
        {
            while (it.MoveNext())
            {
                var target = it.Current;
                if (existingTargets.Contains(target)) continue;
                if (!TryGetWorldPos(target, out var targetPos)) continue;

                var spawnPos = targetPos + new float3(0f, TopY(target), 0f);

                var arrow = ecb.Instantiate(refs.targetArrowPrefabEntity);

                if (em.HasComponent<LocalTransform>(refs.targetArrowPrefabEntity))
                {
                    ecb.SetComponent(arrow, LocalTransform.FromPositionRotationScale(
                        spawnPos, quaternion.identity, baseScale));
                }

                ecb.AddComponent(arrow, new TargetArrowBind { target = target });
            }
        }

        ecb.Playback(em);
        ecb.Dispose();

        desiredTargets.Dispose();
        existingTargets.Dispose();
        arrowsToDestroy.Dispose();
    }
}