/*using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ApplyMoveRequestsServerSystem))]
public partial struct CombatFocusServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        // ---------- Cache all units once ----------
        var ents     = new NativeList<Entity>(Allocator.Temp);
        var positions= new NativeList<float3>(Allocator.Temp);
        var owners   = new NativeList<int>(Allocator.Temp);
        var factions = new NativeList<byte>(Allocator.Temp);
        var alive    = new NativeList<bool>(Allocator.Temp);

        foreach (var (lt, e) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Unit>().WithEntityAccess())
        {
            bool isAlive = true;
            if (SystemAPI.HasComponent<HealthState>(e))
            {
                var h = SystemAPI.GetComponent<HealthState>(e);
                isAlive = h.currentStage != HealthStage.Dead;
            }

            ents.Add(e);
            positions.Add(lt.ValueRO.Position);
            owners.Add(SystemAPI.HasComponent<GhostOwner>(e) ? SystemAPI.GetComponent<GhostOwner>(e).NetworkId : int.MinValue);
            factions.Add(SystemAPI.HasComponent<Faction>(e) ? SystemAPI.GetComponent<Faction>(e).FactionId : (byte)255);
            alive.Add(isAlive);
        }

        bool hasRelations = SystemAPI.HasSingleton<FactionRelations>();
        FactionRelations rel = default;
        byte factionCount = 32;
        if (hasRelations)
        {
            rel = SystemAPI.GetSingleton<FactionRelations>();
            if (SystemAPI.HasSingleton<FactionCount>())
                factionCount = SystemAPI.GetSingleton<FactionCount>().Value;
        }

        // Helper: hostility test (faction masks if present -> else owner mismatch)
        bool IsHostile(byte myFac, int myOwner, byte othFac, int othOwner)
        {
            if (hasRelations && myFac < factionCount && othFac < factionCount)
            {
                // EA0001-safe: take a ref to blob array, then index it
                ref var enemiesMask = ref rel.Blob.Value.EnemiesMask;
                uint mask = enemiesMask[myFac];
                bool hostile = (mask & (1u << othFac)) != 0;

                // dev-friendly fallback: if row unset, treat different owners as hostile
                if (!hostile && mask == 0)
                    hostile = (othOwner != int.MinValue) && (othOwner != myOwner);

                return hostile;
            }
            // fallback: different owner = hostile
            return (othOwner != int.MinValue) && (othOwner != myOwner);
        }

        // ---------- Per attacker: compute focus ----------
        foreach (var (lt, statsRO, attRW, targetsRW, e) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitStats>, RefRW<Attacker>, RefRW<UnitTargets>>()
                          .WithAll<Unit>()
                          .WithEntityAccess())
        {
            var att     = attRW.ValueRO;
            var targets = targetsRW.ValueRO;

            // gating: when are we allowed to auto-focus?
            bool allowAutoFocus =
                (att.attackMove && targets.activeTargetSet) ||  // A-move marching
                (!targets.activeTargetSet && att.autoTarget);   // idle auto-target

            float3 selfPos = lt.ValueRO.Position;
            int myOwner    = SystemAPI.HasComponent<GhostOwner>(e) ? SystemAPI.GetComponent<GhostOwner>(e).NetworkId : int.MinValue;
            byte myFac     = SystemAPI.HasComponent<Faction>(e)    ? SystemAPI.GetComponent<Faction>(e).FactionId   : (byte)255;
            float detectR  = statsRO.ValueRO.detectionRadius;
            float detectSq = detectR * detectR;

            bool wroteFocus = false;

            // Priority 1: if player right-clicked a target entity and it's alive → focus it
            if (targets.targetEntity != Entity.Null && em.Exists(targets.targetEntity))
            {
                Entity t = targets.targetEntity;

                bool tAlive = true;
                if (SystemAPI.HasComponent<HealthState>(t))
                {
                    var th = SystemAPI.GetComponent<HealthState>(t);
                    tAlive = th.currentStage != HealthStage.Dead;
                }

                if (tAlive && SystemAPI.HasComponent<LocalTransform>(t))
                {
                    float3 posT = SystemAPI.GetComponent<LocalTransform>(t).Position;
                    float3 d = posT - selfPos; d.y = 0f;

                    // Even if it's beyond detection, it’s OK to face explicit target;
                    // but if you want stricter behavior, require d2 <= detectSq.
                    float yaw = math.atan2(d.x, d.z);
                    targetsRW.ValueRW.targetPosition = posT;
                    targetsRW.ValueRW.targetRotation = yaw;
                    wroteFocus = true;
                }
            }

            // Priority 2: no explicit target focus → auto-focus nearest hostile in detection
            if (!wroteFocus && allowAutoFocus)
            {
                int bestIdx = -1;
                float bestD2 = float.MaxValue;

                for (int i = 0; i < ents.Length; i++)
                {
                    Entity other = ents[i];
                    if (other == e || !alive[i]) continue;

                    if (!IsHostile(myFac, myOwner, factions[i], owners[i])) continue;

                    float3 d = positions[i] - selfPos; d.y = 0f;
                    float d2 = math.lengthsq(d);
                    if (d2 <= detectSq && d2 < bestD2)
                    {
                        bestD2 = d2;
                        bestIdx = i;
                    }
                }

                if (bestIdx >= 0)
                {
                    float3 posT = positions[bestIdx];
                    float3 d = posT - selfPos; d.y = 0f;
                    float yaw = math.atan2(d.x, d.z);

                    targetsRW.ValueRW.targetPosition = posT;
                    targetsRW.ValueRW.targetRotation = yaw;
                    wroteFocus = true;
                }
            }

            if (!wroteFocus)
            {
                // Clear focus so Movement doesn’t try to face anything
                targetsRW.ValueRW.targetRotation = float.NaN;
                targetsRW.ValueRW.targetPosition = selfPos; // harmless default
            }
        }

        ents.Dispose();
        positions.Dispose();
        owners.Dispose();
        factions.Dispose();
        alive.Dispose();
    }
}
*/