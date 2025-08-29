using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using Unity.Transforms;
using Managers;

public struct AimIndicatorBind : IComponentData { public Entity target; }

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct AimVisualSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        state.RequireForUpdate<EntitiesReferencesLukas>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false) return;

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

        // Collect desired AIM targets from selected local units
        var desiredTargets = new NativeHashSet<Entity>(64, Allocator.Temp);
        foreach (var attacker in SystemAPI
                     .Query<RefRO<Attacker>>()
                     .WithAll<GhostOwnerIsLocal, Selected>())
        {
            var aim = attacker.ValueRO.aimEntity;
            if (aim != Entity.Null) desiredTargets.Add(aim);
        }

        // Update existing indicators
        var existingTargets = new NativeHashSet<Entity>(64, Allocator.Temp);
        var indicatorsToDestroy = new NativeList<Entity>(Allocator.Temp);

        foreach (var (tx, bind, indicator) in SystemAPI
                     .Query<RefRW<LocalTransform>, RefRO<AimIndicatorBind>>()
                     .WithAll<AimIndicator>()
                     .WithEntityAccess())
        {
            var target = bind.ValueRO.target;
            existingTargets.Add(target);

            if (!em.Exists(target) || !desiredTargets.Contains(target) || !TryGetWorldPos(target, out var targetPos))
            {
                indicatorsToDestroy.Add(indicator);
                continue;
            }

            var basePos   = targetPos + new float3(0f, TopY(target), 0f);
            var bobPhase  = target.Index * 0.73f;
            var bobOffset = math.sin(time * 3.5f + bobPhase) * 0.08f;
            var posNow    = basePos; posNow.y += bobOffset;

            var pulsePhase = target.Index * 0.41f;
            var pulse      = 1f + math.sin(time * 5.0f + pulsePhase) * 0.15f;
            var scaleNow   = baseScale * pulse;

            tx.ValueRW.Position = posNow;
            tx.ValueRW.Scale    = scaleNow;

            var cam = Camera.main;
            if (cam != null)
            {
                float3 toCam = (float3)cam.transform.position - posNow;
                toCam.y = 0f;
                if (math.lengthsq(toCam) > 1e-6f)
                {
                    var faceDir = math.normalize(toCam);
                    tx.ValueRW.Rotation = quaternion.LookRotationSafe(faceDir, math.up());
                }
            }
        }

        // Destroy culled indicators
        for (int i = 0; i < indicatorsToDestroy.Length; i++)
            ecb.DestroyEntity(indicatorsToDestroy[i]);

        // Spawn indicators for new targets
        using (var it = desiredTargets.GetEnumerator())
        {
            while (it.MoveNext())
            {
                var target = it.Current;
                if (existingTargets.Contains(target)) continue;
                if (!TryGetWorldPos(target, out var targetPos)) continue;

                var spawnPos = targetPos + new float3(0f, TopY(target), 0f);

                var indicator = ecb.Instantiate(refs.aimIndicatorPrefabEntity); // <- uses Aim prefab

                if (em.HasComponent<LocalTransform>(refs.aimIndicatorPrefabEntity))
                {
                    ecb.SetComponent(indicator, LocalTransform.FromPositionRotationScale(
                        spawnPos, quaternion.identity, baseScale));
                }

                ecb.AddComponent<AimIndicator>(indicator);
                ecb.AddComponent(indicator, new AimIndicatorBind { target = target });
            }
        }

        ecb.Playback(em);
        ecb.Dispose();

        desiredTargets.Dispose();
        existingTargets.Dispose();
        indicatorsToDestroy.Dispose();
    }
}