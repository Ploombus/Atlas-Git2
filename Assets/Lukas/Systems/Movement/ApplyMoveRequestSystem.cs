using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Transforms;
using Managers;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct ApplyMoveRequestsServerSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false) return;

        var em = state.EntityManager;

        foreach (var (reqRW, targetsRW, e) in
                 SystemAPI.Query<RefRW<UnitTargetsNetcode>, RefRW<UnitTargets>>()
                          .WithEntityAccess())
        {
            var req = reqRW.ValueRO;
            ref var targets = ref targetsRW.ValueRW;

            if (req.requestLastAppliedSequence == 0 ||
                req.requestLastAppliedSequence == targets.lastAppliedSequence)
                continue;

            targets.lastAppliedSequence = req.requestLastAppliedSequence;
            targets.activeTargetSet     = true;
            targets.hasArrived          = false;

            if (req.requestTargetEntity != Entity.Null && em.Exists(req.requestTargetEntity))
            {
                targets.targetEntity = req.requestTargetEntity;
                if (req.requestDestinationEntity != Entity.Null)
                targets.destinationEntity = req.requestDestinationEntity;

                if (em.HasComponent<LocalTransform>(req.requestTargetEntity))
                {
                    float3 myPos = 0f;
                    if (em.HasComponent<LocalTransform>(e))
                        myPos = em.GetComponentData<LocalTransform>(e).Position;

                    float3 tPos = em.GetComponentData<LocalTransform>(req.requestTargetEntity).Position;
                    float3 to = tPos - myPos; to.y = 0f;
                    targets.targetRotation = math.atan2(to.x, to.z);
                }
                else
                {
                    // Target may be static or missing LT here — just clear facing.
                    targets.targetRotation = float.NaN;
                }

                if (em.HasComponent<Attacker>(e))
                {
                    var att = em.GetComponentData<Attacker>(e);
                    att.attackMove = false;  // manual attack cancels A-move
                    em.SetComponentData(e, att);
                }
            }
            else
            {
                // Move order (no explicit target)
                targets.destinationEntity   = Entity.Null;
                targets.targetEntity        = Entity.Null;
                targets.targetRotation      = float.NaN;
                targets.destinationPosition = req.requestDestinationPosition;
                targets.destinationRotation = req.requestDestinationRotation;

                if (em.HasComponent<Attacker>(e))
                {
                    var att = em.GetComponentData<Attacker>(e);
                    att.attackMove = req.requestAttackMove && req.requestActiveTargetSet;
                    em.SetComponentData(e, att);
                }
            }
        }
    }
}