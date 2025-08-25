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

        // Monitor for buildings with ENABLED Selected component
        Entity currentlySelectedBuilding = Entity.Null;
        foreach (var (building, entity) in
            SystemAPI.Query<RefRO<Building>>()
            .WithAll<Selected>()  // Only matches if Selected is enabled
            .WithEntityAccess())
        {
            currentlySelectedBuilding = entity;

            if (entity != lastSelectedBuilding)
            {
                lastSelectedBuilding = entity;
                HandleBuildingSelection(ref state, entity, currentResource1, currentResource2);
            }
        }

        // If we had a selected building but don't anymore, it was deselected
        if (lastSelectedBuilding != Entity.Null && currentlySelectedBuilding == Entity.Null)
        {
            lastSelectedBuilding = Entity.Null;
            BuildingUIEvents.RaiseBuildingDeselected();
        }
    }

    private void HandleBuildingSelection(ref SystemState state, Entity buildingEntity,
        int currentResource1, int currentResource2)
    {
        // Check ownership
        bool isOwned = false;
        if (state.EntityManager.HasComponent<GhostOwner>(buildingEntity))
        {
            var ghostOwner = state.EntityManager.GetComponentData<GhostOwner>(buildingEntity);

            // Get local player network ID to compare
            if (PlayerStatsUtils.TryGetLocalPlayerStats(out var localStats))
            {
                isOwned = ghostOwner.NetworkId == localStats.playerId;
            }
        }

        var eventData = new BuildingSelectedEventData
        {
            BuildingEntity = buildingEntity,
            IsOwned = isOwned,
            Resource1Cost = 0,
            Resource2Cost = 0
        };

        // Get costs if available
        if (state.EntityManager.HasComponent<UnitSpawnCost>(buildingEntity))
        {
            var cost = state.EntityManager.GetComponentData<UnitSpawnCost>(buildingEntity);
            eventData.Resource1Cost = cost.unitResource1Cost;
            eventData.Resource2Cost = cost.unitResource2Cost;
        }

        BuildingUIEvents.RaiseBuildingSelected(eventData);

        // Also send cost/affordability update
        UpdateBuildingAffordability(ref state, buildingEntity, currentResource1, currentResource2);
    }

    private void UpdateBuildingAffordability(ref SystemState state, Entity buildingEntity,
        int currentResource1, int currentResource2)
    {
        if (!state.EntityManager.HasComponent<UnitSpawnCost>(buildingEntity))
            return;

        var cost = state.EntityManager.GetComponentData<UnitSpawnCost>(buildingEntity);

        var costData = new SpawnCostUIData
        {
            BuildingEntity = buildingEntity,
            Resource1Cost = cost.unitResource1Cost,
            Resource2Cost = cost.unitResource2Cost,
            CanAfford = currentResource1 >= cost.unitResource1Cost &&
                       currentResource2 >= cost.unitResource2Cost
        };

        BuildingUIEvents.RaiseSpawnCostUpdated(costData);

        // Also update general resource UI
        var resourceData = new ResourceUIData
        {
            CurrentResource1 = currentResource1,
            CurrentResource2 = currentResource2,
            RequiredResource1 = cost.unitResource1Cost,
            RequiredResource2 = cost.unitResource2Cost,
            CanAffordCurrent = costData.CanAfford
        };

        BuildingUIEvents.RaiseResourcesUpdated(resourceData);
    }
}