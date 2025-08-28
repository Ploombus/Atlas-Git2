using Unity.Entities;
using UnityEngine;

public class AimIndicatorPrefabAuthoring : MonoBehaviour
{
    public class Baker : Baker<AimIndicatorPrefabAuthoring>
    {
        public override void Bake(AimIndicatorPrefabAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, new AimIndicator{});
        }
    }
}
public struct AimIndicator : IComponentData
{
}