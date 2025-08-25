using System;
using Unity.Entities;

public static class BuildingUIEvents
{
    public static event Action<BuildingSelectedEventData> OnBuildingSelected;

    public static event Action OnBuildingDeselected;

    public static event Action<SpawnCostUIData> OnSpawnCostUpdated;

    public static event Action<ResourceUIData> OnResourcesUpdated;

    public static event Action<SpawnValidationData> OnSpawnValidated;

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

// Data structures for events
public struct BuildingSelectedEventData
{
    public Entity BuildingEntity;
    public bool HasSpawnCapability;
    public int Resource1Cost;
    public int Resource2Cost;
    public int OwnerNetworkId;        
    public int LocalPlayerNetworkId; 
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
