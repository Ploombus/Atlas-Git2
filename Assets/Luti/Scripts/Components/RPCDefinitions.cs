using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

/// <summary>
/// RPC for spawning barracks buildings
/// </summary>
public struct SpawnBarracksRpc : IRpcCommand
{
    public float3 position;
    public int owner;
}

/// <summary>
/// RPC for spawning units from buildings
/// </summary>
public struct SpawnUnitFromBuildingRpc : IRpcCommand
{
    public Entity buildingEntity;
}