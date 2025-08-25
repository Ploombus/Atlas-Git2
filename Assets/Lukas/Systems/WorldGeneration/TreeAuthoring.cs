using Unity.Entities;
using UnityEngine;

public class TreeAuthoring : MonoBehaviour
{
    [SerializeField] private int woodLeft;

    public class Baker : Baker<TreeAuthoring>
    {
        public override void Bake(TreeAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, new Tree
            {
                woodLeft = authoring.woodLeft,
            });
        }
    }
}
public struct Tree : IComponentData
{
    public int woodLeft;
}