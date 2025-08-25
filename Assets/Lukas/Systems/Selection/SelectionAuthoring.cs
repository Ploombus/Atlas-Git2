using Unity.Entities;
using UnityEngine;
using Unity.NetCode;

public class SelectionAuthoring : MonoBehaviour
{
    public GameObject selectorGameObject;
    public float showScale;
    public class Baker : Baker<SelectionAuthoring>
    {
        public override void Bake(SelectionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new Selected
            {
                selectorEntity = GetEntity(authoring.selectorGameObject, TransformUsageFlags.Renderable),
                showScale = authoring.showScale,
            });
            SetComponentEnabled<Selected>(entity, false);
        }
    }
}

public struct Selected : IComponentData, IEnableableComponent
{
    public Entity selectorEntity;
    public float showScale;
}