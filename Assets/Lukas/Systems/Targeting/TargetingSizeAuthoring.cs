using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.NetCode;

public class TargetingSizeAuthoring : MonoBehaviour
{
    [Min(0f)] public float radius = 0.3f;   // horizontal footprint (XZ)
    [Min(0f)] public float height = 2f;   // visual top from origin (Y)

    public class Baker : Baker<TargetingSizeAuthoring>
    {
        public override void Bake(TargetingSizeAuthoring authoring)
        {
            var e = GetEntity(TransformUsageFlags.None);
            AddComponent(e, new TargetingSize
            {
                radius = math.max(0f, authoring.radius),
                height = math.max(0f, authoring.height)
            });
        }
    }
}

public struct TargetingSize : IComponentData
{
    [GhostField] public float radius;
    [GhostField] public float height;
}