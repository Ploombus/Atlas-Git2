using System;
using Unity.Entities;

/// <summary>
/// CLEANED: BuildingUIEvents system that matches current usage
/// </summary>
public static class BuildingUIEvents
{
    // Events
    public static event Action<BuildingSelectedEventData> OnBuildingSelected;
    public static event Action OnBuildingDeselected;
    public static event Action<SpawnCostUIData> OnSpawnCostUpdated;
    public static event Action<ResourceUIData> OnResourcesUpdated;
    public static event Action<SpawnValidationData> OnSpawnValidated;

    // Event trigger methods
    public static void RaiseBuildingSelected(BuildingSelectedEventData data)
    {
        OnBuildingSelected?.Invoke(data);
    }

    public static void RaiseBuildingDeselected()
    {
        OnBuildingDeselected?.Invoke();
    }

    public static void RaiseSpawnCostUpdated(SpawnCostUIData data)
    {
        OnSpawnCostUpdated?.Invoke(data);
    }

    public static void RaiseResourcesUpdated(ResourceUIData data)
    {
        OnResourcesUpdated?.Invoke(data);
    }

    public static void RaiseSpawnValidated(SpawnValidationData data)
    {
        OnSpawnValidated?.Invoke(data);
    }
}

/// <summary>
/// Event data structures - Updated to match current usage
/// </summary>
public struct BuildingSelectedEventData
{
    public Entity BuildingEntity;
    public bool IsOwned;           // Simple ownership flag
    public int Resource1Cost;
    public int Resource2Cost;
}

public struct SpawnCostUIData
{
    public Entity BuildingEntity;
    public int Resource1Cost;
    public int Resource2Cost;
    public bool CanAfford;
}

public struct ResourceUIData
{
    public int CurrentResource1;
    public int CurrentResource2;
    public int RequiredResource1;
    public int RequiredResource2;
    public bool CanAffordCurrent;
}

public struct SpawnValidationData
{
    public bool Success;
    public int RefundResource1;
    public int RefundResource2;
    public string Message;
}