using Unity.Entities;
using Managers;
using Unity.NetCode;
using UnityEngine;

partial struct SelectorSystem : ISystem
{
    int unitCount;
    int prevUnitCount;
    int aggressive;
    int prevAggressive;
    int prevDefensive;
    int defensive;
    int holdGround;
    int prevHoldGround;

    public void OnCreate(ref SystemState state)
    {
        unitCount = 0;
        prevUnitCount = 0;
        aggressive = 0;
        prevAggressive = 0;
        defensive = 0;
        prevDefensive = 0;
        holdGround = 0;
        prevHoldGround = 0;
    }
    public void OnUpdate(ref SystemState state)
    {
        //System check
        bool isInGame = CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld());
        if (isInGame == false) return;

        foreach ((
            var unit,
            var attacker,
            var selected)
            in SystemAPI.Query<
                RefRO<Unit>,
                RefRO<Attacker>,
                RefRO<Selected>>().WithAll<GhostOwnerIsLocal>())
        {
            unitCount++;

            var autoTargetEnabled = attacker.ValueRO.autoTarget;
            var chasingBudget = attacker.ValueRO.maxChaseMeters;

            if (autoTargetEnabled == true && chasingBudget < 0f)
            {
                aggressive++;
            }
            if (autoTargetEnabled == true && chasingBudget > 0f)
            {
                defensive++;
            }
            if (autoTargetEnabled == true && chasingBudget == 0f)
            {
                holdGround++;
            }
        }

        if (unitCount != prevUnitCount || aggressive != prevAggressive || defensive != prevDefensive || holdGround != prevHoldGround)
        {

            if (unitCount > 0)
            {
                var em = WorldManager.GetClientWorld().EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new ShowStancesRequest
                {
                    aggressive = aggressive,
                    defensive = defensive,
                    holdGround = holdGround
                });
            }
            if (unitCount == 0)
            {
                var em = WorldManager.GetClientWorld().EntityManager;
                var e = em.CreateEntity();
                em.AddComponent<HideStancesRequest>(e);
            }

            prevUnitCount = unitCount;
            prevAggressive = aggressive;
            prevDefensive = defensive;
            prevHoldGround = holdGround;
        }

        unitCount = 0;
        aggressive = 0;
        defensive = 0;
        holdGround = 0;
    }
}

public struct ShowStancesRequest : IComponentData
{
    public int aggressive;
    public int defensive;
    public int holdGround;
}
public struct HideStancesRequest : IComponentData {}