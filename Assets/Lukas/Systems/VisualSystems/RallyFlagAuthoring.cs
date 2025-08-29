using Unity.Entities;
using UnityEngine;

public class RallyFlagPrefabAuthoring : MonoBehaviour
{
    public class Baker : Baker<RallyFlagPrefabAuthoring>
    {
        public override void Bake(RallyFlagPrefabAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, new RallyFlag{});
        }
    }
}
public struct RallyFlag : IComponentData
{
}
public struct RallyFlagBind : IComponentData
{
    public Entity building;
}
