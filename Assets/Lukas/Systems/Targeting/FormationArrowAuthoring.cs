using Unity.Entities;
using UnityEngine;

public class FormationArrowPrefabAuthoring : MonoBehaviour
{
    public class Baker : Baker<FormationArrowPrefabAuthoring>
    {
        public override void Bake(FormationArrowPrefabAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new FormationArrow{});
        }
    }
}
public struct FormationArrow : IComponentData
{
}