using Unity.Entities;
using UnityEngine;

public class TargetArrowPrefabAuthoring : MonoBehaviour
{
    public class Baker : Baker<TargetArrowPrefabAuthoring>
    {
        public override void Bake(TargetArrowPrefabAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, new TargetArrow{});
        }
    }
}
public struct TargetArrow : IComponentData
{
}