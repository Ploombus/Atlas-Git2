using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.Collections;
using Managers;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct BuildingUISystem : ISystem
{
    private Entity lastSelectedBuilding;
    private int lastResource1;
    private int lastResource2;
    private int lastReserved1;
    private int lastReserved2;

    public void OnUpdate(ref SystemState state)
    {
        // Get current selected building
        Entity currentlySelectedBuilding = Entity.Null;
        foreach (var (selected, entity) in
                 SystemAPI.Query<RefRO<Selected>>().WithEntityAccess())
        {
            if (SystemAPI.HasComponent<Building>(entity))
            {
                currentlySelectedBuilding = entity;
                break;
            }
        }

        // Get current resources (including reserved)
        int currentResource1 = 0;
        int currentResource2 = 0;
        int currentReserved1 = 0;
        int currentReserved2 = 0;

        if (PlayerStatsUtils.TryGetCurrentResources(out int r1, out int r2))
        {
            currentResource1 = r1;
            currentResource2 = r2;
        }

        if (PlayerStatsUtils.TryGetReservedResources(out int res1, out int res2))
        {
            currentReserved1 = res1;
            currentReserved2 = res2;
        }

        // Check if we need to update (selection changed or resources changed)
        bool selectionChanged = currentlySelectedBuilding != lastSelectedBuilding;
        bool resourcesChanged = currentResource1 != lastResource1 ||
                               currentResource2 != lastResource2 ||
                               currentReserved1 != lastReserved1 ||
                               currentReserved2 != lastReserved2;

        if (selectionChanged)
        {
            // Deselect previous building if there was one
            if (lastSelectedBuilding != Entity.Null)
            {
                BuildingUIEvents.RaiseBuildingDeselected();
            }

            // Select new building if there is one
            if (currentlySelectedBuilding != Entity.Null)
            {
                HandleBuildingSelection(ref state, currentlySelectedBuilding,
                    currentResource1, currentResource2, currentReserved1, currentReserved2);
            }

            lastSelectedBuilding = currentlySelectedBuilding;
        }
        else if (resourcesChanged && currentlySelectedBuilding != Entity.Null)
        {
            // Resources changed while building is selected - update affordability
            UpdateBuildingAffordability(ref state, currentlySelectedBuilding,
                currentResource1, currentResource2, currentReserved1, currentReserved2);
        }

        // Update cached values
        lastResource1 = currentResource1;
        lastResource2 = currentResource2;
        lastReserved1 = currentReserved1;
        lastReserved2 = currentReserved2;
    }

    private void HandleBuildingSelection(ref SystemState state, Entity buildingEntity,
        int currentResource1, int currentResource2, int reservedResource1, int reservedResource2)
    {
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

        // ALWAYS raise the selection event (BuildingUI will decide whether to show UI based on ownership)
        BuildingUIEvents.RaiseBuildingSelected(eventData);

        // Send affordability updates if building has spawn capability
        if (eventData.HasSpawnCapability)
        {
            UpdateBuildingAffordability(ref state, buildingEntity,
                currentResource1, currentResource2, reservedResource1, reservedResource2);
        }
    }

    private void UpdateBuildingAffordability(ref SystemState state, Entity buildingEntity,
        int currentResource1, int currentResource2, int reservedResource1, int reservedResource2)
    {
        if (!state.EntityManager.HasComponent<UnitSpawnCost>(buildingEntity))
            return;

        var cost = state.EntityManager.GetComponentData<UnitSpawnCost>(buildingEntity);

        // Calculate available resources (current minus reserved)
        int availableResource1 = currentResource1 - reservedResource1;
        int availableResource2 = currentResource2 - reservedResource2;

        // Check if we can afford based on available resources
        bool canAfford = availableResource1 >= cost.unitResource1Cost &&
                        availableResource2 >= cost.unitResource2Cost;

        // Send spawn cost update event
        var costData = new SpawnCostUIData
        {
            BuildingEntity = buildingEntity,
            Resource1Cost = cost.unitResource1Cost,
            Resource2Cost = cost.unitResource2Cost,
            CanAfford = canAfford
        };

        BuildingUIEvents.RaiseSpawnCostUpdated(costData);

        // Send general resource update event (with available resources, not total)
        var resourceData = new ResourceUIData
        {
            CurrentResource1 = availableResource1, // Send available, not total
            CurrentResource2 = availableResource2, // Send available, not total
            RequiredResource1 = cost.unitResource1Cost,
            RequiredResource2 = cost.unitResource2Cost,
            CanAffordCurrent = canAfford
        };

        BuildingUIEvents.RaiseResourcesUpdated(resourceData);
    }
}