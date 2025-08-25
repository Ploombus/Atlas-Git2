using Unity.Entities;

public struct SpawnProgress : IComponentData
{
    public float currentTime;
    public int unitsInQueue;
}

public struct BuildingSpawnProgress : IComponentData
{
    public float progress;
}
