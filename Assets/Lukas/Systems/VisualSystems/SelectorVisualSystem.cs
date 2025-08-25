using Unity.Entities;
using UnityEngine;
using Managers;
using Unity.Transforms;
using Unity.NetCode;

partial struct SelectorVisualSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        //System check
        bool isInGame = CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld());
        if (isInGame == false) return;

        foreach (RefRO<Selected> selected in SystemAPI.Query<RefRO<Selected>>().WithDisabled<Selected>())
        {
            RefRW<LocalTransform> visualLocalTransform = SystemAPI.GetComponentRW<LocalTransform>(selected.ValueRO.selectorEntity);
            visualLocalTransform.ValueRW.Scale = 0f;
        }

        foreach (RefRO<Selected> selected in SystemAPI.Query<RefRO<Selected>>())
        {
            RefRW<LocalTransform> visualLocalTransform = SystemAPI.GetComponentRW<LocalTransform>(selected.ValueRO.selectorEntity);
            visualLocalTransform.ValueRW.Scale = selected.ValueRO.showScale; 
        }
    }
}