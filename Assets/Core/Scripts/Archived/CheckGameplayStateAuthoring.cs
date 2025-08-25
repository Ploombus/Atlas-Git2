/*
using UnityEngine;
using Unity.Entities;

public class CheckGameplayStateAuthoring : MonoBehaviour
{
    public bool gameplayState = false;

    public class Baker : Baker<CheckGameplayStateAuthoring>
    {
        public override void Bake(CheckGameplayStateAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new CheckGameplayState
            {
                GameplayState = authoring.gameplayState
            });
        }
    }
}

public struct CheckGameplayState : IComponentData
{
    public bool GameplayState;
}
*/