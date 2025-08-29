using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using UnityEngine;
using Managers;

public struct AttackVisualRuntime : IComponentData
{
    public uint  lastAttackTick;   // last seen Attacker.attackTick
    public float timeSinceStart;   // client-estimated seconds since swing start
    public bool  flashed;          // impact flash already emitted this swing
    public float plannedReach;     // cached reach for this swing (attackRange + selfRadius + targetRadiusAtStart)
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct AttackVisualSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false) return;

        float dt = SystemAPI.Time.DeltaTime;

        // ---------- PRE-PASS: add missing AttackVisualRuntime via ECB ----------
        {
            var missingQ = SystemAPI.QueryBuilder()
                                    .WithAll<Unit, Attacker>()
                                    .WithNone<AttackVisualRuntime>()
                                    .Build();

            NativeArray<Entity> toAdd = missingQ.ToEntityArray(Allocator.Temp);
            if (toAdd.Length > 0)
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                for (int i = 0; i < toAdd.Length; i++)
                {
                    Entity e = toAdd[i];
                    uint tick = 0;
                    if (SystemAPI.HasComponent<Attacker>(e))
                        tick = SystemAPI.GetComponent<Attacker>(e).attackTick;

                    ecb.AddComponent(e, new AttackVisualRuntime
                    {
                        lastAttackTick = tick,
                        timeSinceStart = 999f,
                        flashed        = true,
                        plannedReach   = 0f
                    });
                }
                ecb.Playback(state.EntityManager);
                ecb.Dispose();
            }
            toAdd.Dispose();
        }

        // Lookup for radii
        var sizeLookup = SystemAPI.GetComponentLookup<TargetingSize>(true);

        // ---------- DRAW ----------
        AttackVisualDraw.BeginFrame();

        foreach (var (lt, cstats, attacker, runtimeRW, entity) in
                 SystemAPI.Query<
                     RefRO<LocalTransform>,
                     RefRO<CombatStats>,
                     RefRO<Attacker>,
                     RefRW<AttackVisualRuntime>>()
                  .WithAll<Unit>()
                  .WithEntityAccess())
        {
            ref var rt = ref runtimeRW.ValueRW;

            // Detect new swing
            if (attacker.ValueRO.attackTick != rt.lastAttackTick)
            {
                rt.lastAttackTick = attacker.ValueRO.attackTick;
                rt.timeSinceStart = 0f;
                rt.flashed        = false;

                // Cache planned reach at swing start:
                // attackRange + self radius + target radius (if aim entity exists)
                float selfRadius = sizeLookup.HasComponent(entity) ? sizeLookup[entity].radius : 0f;
                float targetRadius = 0f;
                Entity aim = attacker.ValueRO.aimEntity;
                if (aim != Entity.Null && sizeLookup.HasComponent(aim))
                    targetRadius = sizeLookup[aim].radius;

                rt.plannedReach = math.max(0f, cstats.ValueRO.attackRange) + selfRadius + targetRadius;
            }
            else
            {
                rt.timeSinceStart += dt;
            }

            // Need a valid aim direction to draw
            if (!math.isfinite(attacker.ValueRO.aimRotation))
                continue;

            float window = math.max(0.01f, cstats.ValueRO.attackDuration);
            float t = rt.timeSinceStart;
            if (t > window)
                continue;

            float3 pos = lt.ValueRO.Position;

            // Use the same reach for wind-up and impact for consistency
            float reach = rt.plannedReach > 0f
                ? rt.plannedReach
                : math.max(0f, cstats.ValueRO.attackRange); // fallback (should rarely happen)

            // Planned cone (wind-up)
            AttackVisualDraw.AddConeOutline(
                new Vector3(pos.x, pos.y, pos.z),
                attacker.ValueRO.aimRotation,
                reach,
                cstats.ValueRO.attackConeDeg,
                impact: false
            );

            // Impact flash around impactDelay — SAME reach
            float impactDelay = math.clamp(cstats.ValueRO.impactDelay, 0f, window);
            if (!rt.flashed &&
                t >= impactDelay &&
                t <= impactDelay + AttackVisualStyle.ImpactFlashTime)
            {
                AttackVisualDraw.AddConeOutline(
                    new Vector3(pos.x, pos.y, pos.z),
                    attacker.ValueRO.aimRotation,
                    reach,
                    cstats.ValueRO.attackConeDeg,
                    impact: true
                );
            }
            else if (!rt.flashed && t > impactDelay + AttackVisualStyle.ImpactFlashTime)
            {
                rt.flashed = true;
            }
        }
    }
}