using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Managers;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct RallyFlagVisualSystem : ISystem
{
    // ========================= KNOBS =========================
    const float FLAG_BASE_SCALE           = 0.20f;  // overall sprite size
    const float FLAG_Y_OFFSET             = 0.02f;  // lift off the ground slightly
    const float FLAG_LOCAL_Z_OFFSET       = -0.65f; // move the flag mesh along its OWN +Z (forward) after billboard

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        state.RequireForUpdate<EntitiesReferencesLukas>(); // expects rallyFlagPrefabEntity inside
    }

    public void OnUpdate(ref SystemState state)
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false) return;

        var em   = state.EntityManager;
        var refs = SystemAPI.GetSingleton<EntitiesReferencesLukas>();
        var ecb  = new EntityCommandBuffer(Allocator.Temp);

        // 1) Determine which buildings should currently show a flag (selected + owned + rally set)
        var desired = new NativeHashSet<Entity>(64, Allocator.Temp);

        // Only for locally-owned selected buildings that have a rally point set
        foreach (var (rp, building, e) in SystemAPI
                     .Query<RefRO<RallyPoint>, RefRO<GhostOwner>>()
                     .WithAll<Building, Selected>()
                     .WithEntityAccess())
        {
            if (!rp.ValueRO.isSet) continue;

            // Must be owned by local player
            if (!TryGetLocalPlayerNetId(out int myId)) continue;
            if (building.ValueRO.NetworkId != myId)    continue;

            desired.Add(e);
        }

        // 2) Update existing flags
        var existingFor = new NativeHashSet<Entity>(64, Allocator.Temp);
        var killList    = new NativeList<Entity>(Allocator.Temp);

        var ltLookup  = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var ltwLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true);

        foreach (var (tx, bind, flagEntity) in SystemAPI
                     .Query<RefRW<LocalTransform>, RefRO<RallyFlagBind>>()
                     .WithAll<RallyFlag>()
                     .WithEntityAccess())
        {
            var building = bind.ValueRO.building;
            existingFor.Add(building);

            // Cull if building gone or no longer desired
            if (!em.Exists(building) || !desired.Contains(building))
            {
                killList.Add(flagEntity);
                continue;
            }

            // Get rally position
            if (!SystemAPI.HasComponent<RallyPoint>(building))
            {
                killList.Add(flagEntity);
                continue;
            }

            var rp = SystemAPI.GetComponent<RallyPoint>(building);
            if (!rp.isSet)
            {
                killList.Add(flagEntity);
                continue;
            }

            float3 basePos = rp.position;
            basePos.y += FLAG_Y_OFFSET;

            // Billboard rotation
            quaternion rot = quaternion.identity;
            var cam = Camera.main;
            if (cam != null)
            {
                float3 toCam = (float3)cam.transform.position - basePos;
                toCam.y = 0f;
                if (math.lengthsq(toCam) > 1e-12f)
                    rot = quaternion.LookRotationSafe(math.normalize(toCam), math.up());
            }

            // >>> Local Z offset on the FLAG OBJECT itself <<<
            // Move along the object's own forward (+Z) after billboard
            float3 fwd = math.forward(rot);
            basePos += fwd * FLAG_LOCAL_Z_OFFSET;

            tx.ValueRW.Position = basePos;
            tx.ValueRW.Rotation = rot;
            tx.ValueRW.Scale    = FLAG_BASE_SCALE;
        }

        // 3) Destroy culled flags
        for (int i = 0; i < killList.Length; i++)
            ecb.DestroyEntity(killList[i]);

        // 4) Spawn flags for buildings that need one but don't have it yet
        using (var it = desired.GetEnumerator())
        {
            while (it.MoveNext())
            {
                var building = it.Current;
                if (existingFor.Contains(building)) continue;

                if (!SystemAPI.HasComponent<RallyPoint>(building)) continue;
                var rp = SystemAPI.GetComponent<RallyPoint>(building);
                if (!rp.isSet) continue;

                float3 pos = rp.position; pos.y += FLAG_Y_OFFSET;

                quaternion rot = quaternion.identity;
                var cam = Camera.main;
                if (cam != null)
                {
                    float3 toCam = (float3)cam.transform.position - pos;
                    toCam.y = 0f;
                    if (math.lengthsq(toCam) > 1e-12f)
                        rot = quaternion.LookRotationSafe(math.normalize(toCam), math.up());
                }

                // Apply the local Z offset on the spawned transform as well
                pos += math.forward(rot) * FLAG_LOCAL_Z_OFFSET;

                var flag = ecb.Instantiate(refs.rallyFlagPrefabEntity);
                ecb.SetComponent(flag, LocalTransform.FromPositionRotationScale(pos, rot, FLAG_BASE_SCALE));
                ecb.AddComponent(flag, new RallyFlagBind { building = building });
            }
        }

        ecb.Playback(em);
        ecb.Dispose();
        desired.Dispose();
        existingFor.Dispose();
        killList.Dispose();
    }

    static bool TryGetLocalPlayerNetId(out int id)
    {
        id = -1;
        if (!PlayerStatsUtils.TryGetLocalPlayerStats(out var localStats)) return false;
        id = localStats.playerId;
        return true;
    }
}