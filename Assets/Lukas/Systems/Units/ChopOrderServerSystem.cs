using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
//[UpdateAfter(typeof(FollowTargetServerSystem))]
[UpdateBefore(typeof(GatheringServerSystem))] // if you named it differently, update this
public partial struct ChopOrderServerSystem : ISystem
{
    const float DEFAULT_HIT_RANGE    = 2.2f;
    const float DEFAULT_HIT_INTERVAL = 0.7f;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkTime>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (targetsRO, unit) in
                 SystemAPI.Query<RefRO<UnitTargets>>()
                          .WithEntityAccess())
        {
            var tgt = targetsRO.ValueRO.targetEntity;
            bool isTree = tgt != Entity.Null
                       && state.EntityManager.Exists(tgt)
                       && state.EntityManager.HasComponent<Tree>(tgt);

            if (isTree)
            {
                // Ensure required components exist
                if (!state.EntityManager.HasComponent<GatheringWoodState>(unit))
                    ecb.AddComponent(unit, new GatheringWoodState());

                if (!state.EntityManager.HasComponent<WoodcutRuntime>(unit))
                    ecb.AddComponent(unit, new WoodcutRuntime { cooldown = 0f, wasChopping = 0 });

                // Set/refresh the order (idempotent)
                var ho = new HarvestOrder
                {
                    targetTree  = tgt,
                    hitRange    = DEFAULT_HIT_RANGE,
                    hitInterval = DEFAULT_HIT_INTERVAL
                };

                if (state.EntityManager.HasComponent<HarvestOrder>(unit))
                    ecb.SetComponent(unit, ho);
                else
                    ecb.AddComponent(unit, ho);
            }
            else
            {
                // Not a tree (or no target): remove harvest order and emit one cancel tick if needed
                if (state.EntityManager.HasComponent<HarvestOrder>(unit))
                    ecb.RemoveComponent<HarvestOrder>(unit);

                if (state.EntityManager.HasComponent<WoodcutRuntime>(unit) &&
                    state.EntityManager.HasComponent<GatheringWoodState>(unit))
                {
                    var rt = state.EntityManager.GetComponentData<WoodcutRuntime>(unit);
                    if (rt.wasChopping != 0)
                    {
                        var ws = state.EntityManager.GetComponentData<GatheringWoodState>(unit);
                        ws.woodCancelTick++;
                        rt.wasChopping = 0;
                        ecb.SetComponent(unit, ws);
                        ecb.SetComponent(unit, rt);
                    }
                }
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}