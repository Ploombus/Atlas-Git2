using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

public struct RallyPoint : IComponentData
{
    [GhostField] public float3 position;
    [GhostField] public bool isSet; // Whether a rally point has been set
}

public struct SetRallyPointRpc : IRpcCommand
{
    public Entity buildingEntity;
    public float3 rallyPosition;
}