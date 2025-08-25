using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

public struct SpawnBarracksRpc : IRpcCommand
{
    public float3 position;
    public int owner;
}

public struct SpawnUnitFromBuildingRpc : IRpcCommand
{
    public Entity buildingEntity;
}

public struct AddResourcesRpc : IRpcCommand
{
    public int resource1ToAdd;
    public int resource2ToAdd;
}