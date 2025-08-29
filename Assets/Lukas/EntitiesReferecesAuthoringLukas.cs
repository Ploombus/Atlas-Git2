using UnityEngine;
using Unity.Entities;


public class EntitiesReferencesAuthoringLukas : MonoBehaviour
{
    public GameObject unitPrefabGameObject;
    public GameObject dotPrefabGameObject;
    public GameObject formationArrowPrefabGameObject;
    public GameObject targetArrowPrefabGameObject;
    public GameObject aimIndicatorPrefabGameObject;
    public GameObject rallyFlagPrefabGameObject;
    public GameObject treePrefabGameObject;
   
    public class Baker : Baker<EntitiesReferencesAuthoringLukas>
    {
        public override void Bake(EntitiesReferencesAuthoringLukas authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EntitiesReferencesLukas
            {
                unitPrefabEntity = GetEntity(authoring.unitPrefabGameObject, TransformUsageFlags.Dynamic),
                dotPrefabEntity = GetEntity(authoring.dotPrefabGameObject, TransformUsageFlags.Renderable),
                formationArrowPrefabEntity = GetEntity(authoring.formationArrowPrefabGameObject, TransformUsageFlags.Renderable),
                targetArrowPrefabEntity = GetEntity(authoring.targetArrowPrefabGameObject, TransformUsageFlags.Renderable),
                aimIndicatorPrefabEntity = GetEntity(authoring.aimIndicatorPrefabGameObject, TransformUsageFlags.Renderable),
                rallyFlagPrefabEntity = GetEntity(authoring.rallyFlagPrefabGameObject, TransformUsageFlags.Renderable),
                treePrefabEntity = GetEntity(authoring.treePrefabGameObject, TransformUsageFlags.Renderable),
            });
        }
    }
   
}
public struct EntitiesReferencesLukas : IComponentData
{
    public Entity unitPrefabEntity;
    public Entity dotPrefabEntity;
    public Entity formationArrowPrefabEntity;
    public Entity targetArrowPrefabEntity;
    public Entity aimIndicatorPrefabEntity;
    public Entity rallyFlagPrefabEntity;
    public Entity treePrefabEntity;
}