using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Managers;
using Unity.Mathematics;


[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(GhostInputSystemGroup))]
public partial struct RallyPointInputSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false) return;
        
        // Check for right mouse button click first
        if (Input.GetMouseButtonDown(1))
        {
        }
        else
        {
            return; // No right click, exit early
        }

        // Only process input if we're in the game
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()))
        {
            return;
        }


        // Don't process if mouse is over UI
        if (UIUtility.IsPointerOverUI())
        {
            return;
        }


        // Get our network ID using the existing utility
        if (!PlayerStatsUtils.TryGetLocalPlayerStats(out var localStats))
        {
            return;
        }

        int myNetId = localStats.playerId;

        // Find selected building that we own (using the same logic as BuildingUISystem)
        Entity selectedBuilding = Entity.Null;
        bool foundOwnedBuilding = false;
        int buildingsChecked = 0;
        int selectedEntities = 0;

        foreach (var (selected, entity) in SystemAPI.Query<RefRO<Selected>>().WithEntityAccess())
        {
            selectedEntities++;

            // Check if it's a selected building
            if (!SystemAPI.IsComponentEnabled<Selected>(entity))
            {
                continue;
            }

            if (!SystemAPI.HasComponent<Building>(entity))
            {
                continue;
            }

            buildingsChecked++;

            // Check ownership
            if (!SystemAPI.HasComponent<GhostOwner>(entity))
            {
                continue;
            }

            var ghostOwner = SystemAPI.GetComponent<GhostOwner>(entity);

            if (ghostOwner.NetworkId != myNetId)
            {
                continue;
            }

            // Check if it has spawn capability (same as BuildingUI logic)
            if (!SystemAPI.HasComponent<UnitSpawnCost>(entity))
            {
                continue;
            }

            selectedBuilding = entity;
            foundOwnedBuilding = true;
            break; // Found our selected building
        }


        // Only proceed if we found a valid building
        if (!foundOwnedBuilding || selectedBuilding == Entity.Null)
        {
            return;
        }

        // Additional check: make sure BuildingUI instance exists (sanity check)
        if (BuildingUI.Instance == null)
        {
            return;
        }


        // Get world position from mouse
        float3 rallyPosition = MouseWorldPosition.Instance.GetPosition();

        // Create and send RPC
        var rpcEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(rpcEntity, new SetRallyPointRpc
        {
            buildingEntity = selectedBuilding,
            rallyPosition = rallyPosition
        });
        state.EntityManager.AddComponentData(rpcEntity, new SendRpcCommandRequest());

    }
}