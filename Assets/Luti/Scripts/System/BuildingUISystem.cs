using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct BuildingUISystem : ISystem
{
    private Entity lastSelectedBuilding;
    private int lastResource1;
    private int lastResource2;

    public void OnCreate(ref SystemState state)
    {
        lastSelectedBuilding = Entity.Null;
        lastResource1 = -1;
        lastResource2 = -1;
    }

    public void OnUpdate(ref SystemState state)
    {
        // MIGRATED: Get resources directly from PlayerStats instead of ResourceManager
        if (!PlayerStatsUtils.TryGetLocalResources(out int currentResource1, out int currentResource2))
        {
            return; // No local player stats available yet
        }

        // Check for resource changes
        if (currentResource1 != lastResource1 || currentResource2 != lastResource2)
        {
            lastResource1 = currentResource1;
            lastResource2 = currentResource2;

            // If we have a selected building, update affordability
            if (lastSelectedBuilding != Entity.Null && state.EntityManager.Exists(lastSelectedBuilding))
            {
                UpdateBuildingAffordability(ref state, lastSelectedBuilding, currentResource1, currentResource2);
            }
        }

        // FIXED: Monitor for buildings with ENABLED Selected component
        Entity currentlySelectedBuilding = Entity.Null;
        foreach (var (building, entity) in
            SystemAPI.Query<RefRO<Building>>()
            .WithAll<Selected>()  // Only buildings that have Selected component
            .WithEntityAccess())
        {
            // CRITICAL: Check if the Selected component is actually ENABLED
            if (state.EntityManager.IsComponentEnabled<Selected>(entity))
            {
                currentlySelectedBuilding = entity;
                break; // Should only be one selected building at a time
            }
        }

        // Handle building selection changes
        if (currentlySelectedBuilding != lastSelectedBuilding)
        {
            // Deselect previous building if there was one
            if (lastSelectedBuilding != Entity.Null)
            {
                BuildingUIEvents.RaiseBuildingDeselected();
            }

            // Select new building if there is one
            if (currentlySelectedBuilding != Entity.Null)
            {
                HandleBuildingSelection(ref state, currentlySelectedBuilding, currentResource1, currentResource2);
            }

            lastSelectedBuilding = currentlySelectedBuilding;
        }
    }

    private void HandleBuildingSelection(ref SystemState state, Entity buildingEntity,
        int currentResource1, int currentResource2)
    {
        // FIXED: Check ownership using the same method as PlayerStatsUtils
        bool isOwned = false;
        int ownerNetworkId = -999;
        int localPlayerNetworkId = -999;

        if (state.EntityManager.HasComponent<GhostOwner>(buildingEntity))
        {
            var ghostOwner = state.EntityManager.GetComponentData<GhostOwner>(buildingEntity);
            ownerNetworkId = ghostOwner.NetworkId;

            // Get local player network ID to compare
            if (PlayerStatsUtils.TryGetLocalPlayerStats(out var localStats))
            {
                localPlayerNetworkId = localStats.playerId;
                isOwned = (ownerNetworkId == localPlayerNetworkId);
            }
        }

        // UPDATED: Create event data with all required fields
        var eventData = new BuildingSelectedEventData
        {
            BuildingEntity = buildingEntity,
            HasSpawnCapability = false,
            Resource1Cost = 0,
            Resource2Cost = 0,
            OwnerNetworkId = ownerNetworkId,
            LocalPlayerNetworkId = localPlayerNetworkId
        };

        // Get spawn capability and costs
        if (state.EntityManager.HasComponent<UnitSpawnCost>(buildingEntity))
        {
            eventData.HasSpawnCapability = true;
            var cost = state.EntityManager.GetComponentData<UnitSpawnCost>(buildingEntity);
            eventData.Resource1Cost = cost.unitResource1Cost;
            eventData.Resource2Cost = cost.unitResource2Cost;
        }

        // ALWAYS raise the selection event (TesterUI will decide whether to show UI based on ownership)
        BuildingUIEvents.RaiseBuildingSelected(eventData);

        // Send affordability updates if building has spawn capability
        if (eventData.HasSpawnCapability)
        {
            UpdateBuildingAffordability(ref state, buildingEntity, currentResource1, currentResource2);
        }

    }

    private void UpdateBuildingAffordability(ref SystemState state, Entity buildingEntity,
        int currentResource1, int currentResource2)
    {
        if (!state.EntityManager.HasComponent<UnitSpawnCost>(buildingEntity))
            return;

        var cost = state.EntityManager.GetComponentData<UnitSpawnCost>(buildingEntity);
        bool canAfford = currentResource1 >= cost.unitResource1Cost &&
                        currentResource2 >= cost.unitResource2Cost;

        // Send spawn cost update event
        var costData = new SpawnCostUIData
        {
            BuildingEntity = buildingEntity,
            Resource1Cost = cost.unitResource1Cost,
            Resource2Cost = cost.unitResource2Cost,
            CanAfford = canAfford
        };

        BuildingUIEvents.RaiseSpawnCostUpdated(costData);

        // Send general resource update event
        var resourceData = new ResourceUIData
        {
            CurrentResource1 = currentResource1,
            CurrentResource2 = currentResource2,
            RequiredResource1 = cost.unitResource1Cost,
            RequiredResource2 = cost.unitResource2Cost,
            CanAffordCurrent = canAfford
        };

        BuildingUIEvents.RaiseResourcesUpdated(resourceData);
    }
}