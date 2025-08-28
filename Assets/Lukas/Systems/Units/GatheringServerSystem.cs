using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct GatheringServerSystem : ISystem
{
    // --- timing derived from your chop clip (client plays at Speed=1) ---
    const float CHOP_PLAYRATE        = 1.0f;  // client playback multiplier we assume
    const float CHOP_CLIP_SECONDS    = 5f;    // full clip length at Speed=1
    const float CHOP_IMPACT_NORMTIME = 0.5f;  // normalized time of axe contact (0..1)
    static float ImpactDelay => (CHOP_CLIP_SECONDS * CHOP_IMPACT_NORMTIME) / math.max(0.01f, CHOP_PLAYRATE);

    // --- cadence knobs (server authority) ---
    const float DEFAULT_HIT_INTERVAL = 4.00f; // seconds between impacts if order.hitInterval <= 0
    const float MIN_HIT_INTERVAL     = 0.40f; // safety floor

    static float HitIntervalSec(in HarvestOrder o)
    {
        float hi = (o.hitInterval > 0f) ? o.hitInterval : DEFAULT_HIT_INTERVAL;
        return math.max(hi, MIN_HIT_INTERVAL);
    }

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkTime>();
    }

    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        var ecb  = new EntityCommandBuffer(Allocator.Temp);

        // Map: NetworkId -> connection entity (for crediting resources)
        var idToConn = new NativeParallelHashMap<int, Entity>(64, Allocator.Temp);
        foreach (var (nid, conn) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamConnection>().WithEntityAccess())
            idToConn.TryAdd(nid.ValueRO.Value, conn);

        // NEW: set of trees we decided to destroy this frame (prevents later SetComponent on a dead entity)
        var treesPendingDestroy = new NativeParallelHashMap<Entity, byte>(64, Allocator.Temp);

        foreach (var (lt, order, runtimeRW, woodStateRW, unitEnt) in
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<HarvestOrder>, RefRW<WoodcutRuntime>, RefRW<GatheringWoodState>>()
                          .WithEntityAccess())
        {
            var runtime   = runtimeRW.ValueRO;
            var woodState = woodStateRW.ValueRO;

            // ---- Validate target (quick pre-check) ----
            Entity treeEnt = order.ValueRO.targetTree;
            if (treeEnt == Entity.Null || !state.EntityManager.Exists(treeEnt))
            {
                runtime.pendingHit  = 0;
                runtime.wasChopping = 0;
                runtime.cooldown    = 0;
                runtime.impactTimer = 0;
                runtimeRW.ValueRW   = runtime;
                continue;
            }

            // If another unit already marked this tree for destroy this frame, ignore it safely.
            if (treesPendingDestroy.ContainsKey(treeEnt))
            {
                runtime.pendingHit  = 0;
                runtime.wasChopping = 0;
                runtime.cooldown    = 0;
                runtime.impactTimer = 0;
                runtimeRW.ValueRW   = runtime;
                continue;
            }

            // Guard component presence before reading
            if (!state.EntityManager.HasComponent<Tree>(treeEnt) ||
                !state.EntityManager.HasComponent<LocalTransform>(treeEnt))
            {
                runtime.pendingHit  = 0;
                runtime.wasChopping = 0;
                runtime.cooldown    = 0;
                runtime.impactTimer = 0;
                runtimeRW.ValueRW   = runtime;
                continue;
            }

            // Safe reads (just checked presence)
            var treeLt = state.EntityManager.GetComponentData<LocalTransform>(treeEnt);
            var tree   = state.EntityManager.GetComponentData<Tree>(treeEnt);

            var treePos = treeLt.Position;
            var myPos   = lt.ValueRO.Position;

            float3 toTree = new float3(treePos.x - myPos.x, 0f, treePos.z - myPos.z);
            bool   inRange = math.lengthsq(toTree) <= (order.ValueRO.hitRange * order.ValueRO.hitRange);

            // ---- Out of range or depleted: cancel & optionally clear order ----
            if (!inRange || tree.woodLeft <= 0)
            {
                runtime.pendingHit = 0;
                if (runtime.wasChopping != 0)
                {
                    woodState.woodCancelTick++;
                    runtime.wasChopping = 0;
                    woodStateRW.ValueRW = woodState;
                }
                runtime.cooldown    = 0;
                runtime.impactTimer = 0;
                runtimeRW.ValueRW   = runtime;

                if (tree.woodLeft <= 0 && state.EntityManager.HasComponent<HarvestOrder>(unitEnt))
                    ecb.RemoveComponent<HarvestOrder>(unitEnt);

                continue;
            }

            // ---- Pending impact (wind-up) ----
            if (runtime.pendingHit != 0)
            {
                runtime.impactTimer -= dt;
                if (runtime.impactTimer <= 0f)
                {
                    // If already marked for destroy, skip doing anything to this tree
                    if (!treesPendingDestroy.ContainsKey(treeEnt) &&
                        state.EntityManager.Exists(treeEnt) &&
                        state.EntityManager.HasComponent<Tree>(treeEnt))
                    {
                        var t = state.EntityManager.GetComponentData<Tree>(treeEnt);
                        if (t.woodLeft > 0)
                        {
                            t.woodLeft = math.max(0, t.woodLeft - 1);
                            ecb.SetComponent(treeEnt, t);

                            // Credit owner
                            if (SystemAPI.HasComponent<GhostOwner>(unitEnt))
                            {
                                int ownerId = SystemAPI.GetComponent<GhostOwner>(unitEnt).NetworkId;
                                if (idToConn.TryGetValue(ownerId, out var conn))
                                {
                                    // Create StatsChangeEvent entity to award resources
                                    var eventEntity = ecb.CreateEntity();
                                    ecb.AddComponent(eventEntity, new StatsChangeEvent
                                    {
                                        resource1Delta = 1,      // Award 1 wood (resource1)
                                        resource2Delta = 0,      // No resource2 from trees
                                        playerConnection = conn,
                                        awardScorePoints = true  // Give score points for gathering
                                    });
                                }
                            }

                                // Despawn on depletion
                                if (t.woodLeft == 0)
                            {
                                // Mark so later units in this same frame won't touch it
                                treesPendingDestroy.TryAdd(treeEnt, 1);

                                woodState.woodCancelTick++;
                                woodStateRW.ValueRW = woodState;

                                if (state.EntityManager.HasComponent<HarvestOrder>(unitEnt))
                                    ecb.RemoveComponent<HarvestOrder>(unitEnt);

                                ecb.DestroyEntity(treeEnt);
                            }
                        }
                    }

                    runtime.pendingHit  = 0;
                    runtime.impactTimer = 0f;

                    // Arm recovery BETWEEN impacts
                    runtime.cooldown = HitIntervalSec(in order.ValueRO);
                }

                runtimeRW.ValueRW = runtime;
                continue; // do not return from the system
            }

            // ---- Recovery / cooldown ----
            if (runtime.cooldown > 0f)
            {
                runtime.cooldown = math.max(0f, runtime.cooldown - dt);
                runtimeRW.ValueRW = runtime;
                continue;
            }

            // ---- Ready -> start a new swing ----
            if (!treesPendingDestroy.ContainsKey(treeEnt) &&
                state.EntityManager.Exists(treeEnt) &&
                state.EntityManager.HasComponent<Tree>(treeEnt))
            {
                var t = state.EntityManager.GetComponentData<Tree>(treeEnt);
                if (t.woodLeft > 0 && inRange)
                {
                    // Tell client to play a chop
                    woodState.woodStartTick++;
                    woodStateRW.ValueRW = woodState;

                    // Arm the wind-up to the impact moment
                    runtime.wasChopping = 1;
                    runtime.pendingHit  = 1;
                    runtime.impactTimer = ImpactDelay;

                    // NOTE: we do NOT set cooldown here (we measure seconds between impacts)
                    runtimeRW.ValueRW = runtime;
                    continue;
                }
            }

            // If we got here, something changed (e.g., tree vanished). Reset the runtime cleanly.
            runtime.pendingHit  = 0;
            runtime.wasChopping = 0;
            runtime.cooldown    = 0;
            runtime.impactTimer = 0;
            runtimeRW.ValueRW   = runtime;
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        idToConn.Dispose();
        treesPendingDestroy.Dispose();
    }
}

// Assign this when issuing a chop order
public struct HarvestOrder : IComponentData
{
    public Entity targetTree;  // the tree entity to chop
    public float  hitRange;    // meters (e.g., 1.5f)
    public float  hitInterval; // seconds BETWEEN impacts (server enforces)
}

// Per-unit runtime (server)
public struct WoodcutRuntime : IComponentData
{
    public float cooldown;     // time until we can start next swing (after an impact)
    public float impactTimer;  // time until current swing “hits”
    public byte  wasChopping;  // 0/1
    public byte  pendingHit;   // 0/1
}