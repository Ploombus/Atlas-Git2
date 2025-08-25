/*
using Unity.Entities;
using UnityEngine;


public class UnitAnimatorAuthoring : MonoBehaviour
{
    public GameObject UnitGameObjectPrefab;

    public class UnitGameObjectPrefabBaker : Baker<UnitAnimatorAuthoring>
    {
        public override void Bake(UnitAnimatorAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponentObject(entity, new UnitGameObjectPrefab { Value = authoring.UnitGameObjectPrefab });
        }
    }
}
*/