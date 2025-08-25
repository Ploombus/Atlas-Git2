using Unity.Burst;
using Unity.Entities;
using UnityEngine;
using Managers;

[DisableAutoCreation]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct WorldShutdownCleanerSystem : ISystem
{
    public void OnDestroy(ref SystemState state)
    {
        Debug.Log("Cleaning up WorldManager static state on shutdown.");
        WorldManager.Clear();
    }
}