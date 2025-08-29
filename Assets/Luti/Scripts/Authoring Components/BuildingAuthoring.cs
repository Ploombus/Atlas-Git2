using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

class BuildingAuthoring : MonoBehaviour
{
    [SerializeField] private int buildTime;
    [SerializeField] private int radius;
    [SerializeField] private bool spawnRequested;
    [SerializeField] private GameObject barracksGameObjectPrefab;
    [SerializeField] private int unitResource1Cost;
    [SerializeField] private int unitResource2Cost;



    public class Baker : Baker<BuildingAuthoring>
    {
        public override void Bake(BuildingAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, new Building
            {
                buildTime = authoring.buildTime,
                radius = authoring.radius,
            });
            AddComponent(entity, new UnitSpawnFromBuilding
            {
                spawnRequested = authoring.spawnRequested,
            });
            AddComponentObject(entity, new UnitGameObjectPrefab { Value = authoring.barracksGameObjectPrefab });
            AddComponent(entity, new UnitSpawnCost
            {
                unitResource1Cost = authoring.unitResource1Cost,
                unitResource2Cost = authoring.unitResource2Cost,
            });

            AddComponent(entity, new RallyPoint
            {
                position = float3.zero,
                isSet = false
            });
        }

    }

}

public struct Building : IComponentData
{
    public int buildTime;
    public int radius;

}
public struct UnitSpawnFromBuilding : IComponentData
{
    public bool spawnRequested; // We can just toggle this true when UI button is pressed
}

public sealed class BarracksGameObjectPrefab : IComponentData
{
    public GameObject Value;
}
public sealed class BarracksAnimatorReference : IComponentData
{
    public Animator Value;
}
public struct UnitSpawnCost : IComponentData
{
    public int unitResource1Cost;
    public int unitResource2Cost;
}
