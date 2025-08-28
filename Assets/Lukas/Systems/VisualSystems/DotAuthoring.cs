using Unity.Entities;
using UnityEngine;

public class DotPrefabAuthoring : MonoBehaviour
{
    public class Baker : Baker<DotPrefabAuthoring>
    {
        public override void Bake(DotPrefabAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Renderable);
        }
    }
}
public struct MovementDot : IComponentData
{
    public Entity owner;
}