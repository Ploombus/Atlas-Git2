using UnityEngine;
using Unity.Entities;

public class EntitiesReferencesAuthoringLuti : MonoBehaviour
{
    public GameObject buildingPrefabObject;
    public GameObject playerStatsPrefabObject; // FIXED: Changed name to match usage

    public class Baker : Baker<EntitiesReferencesAuthoringLuti>
    {
        public override void Bake(EntitiesReferencesAuthoringLuti authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EntitiesReferencesLuti
            {
                buildingPrefabEntity = GetEntity(authoring.buildingPrefabObject, TransformUsageFlags.Dynamic),
                playerStatsPrefabEntity = GetEntity(authoring.playerStatsPrefabObject, TransformUsageFlags.None),
            });
        }
    }
}

public struct EntitiesReferencesLuti : IComponentData
{
    public Entity buildingPrefabEntity;
    public Entity playerStatsPrefabEntity;
}