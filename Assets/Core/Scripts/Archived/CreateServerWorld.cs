/*
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
using Managers;

public static class ServerBootstrapHelper
{
    public static void StartServerWorld()
    {
        foreach (var world in World.All)
        {
            if (world.Flags == WorldFlags.GameServer)
                return;
        }

        //Create Server
        var serverWorld = new World("ServerWorld", WorldFlags.GameServer | WorldFlags.Game);
        var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.ServerSimulation);
        DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(serverWorld, systems);
        ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(serverWorld); //Super important!!!

        Debug.Log("Server Created.");

        //Adding to my registry
        WorldManager.Register(serverWorld);
        Debug.Log("World registered: " + serverWorld.Name);

    }
    public static void StopServerWorld()
    {
        var worlds = World.All;

        for (int i = worlds.Count - 1; i >= 0; i--)
        {
            var world = worlds[i];

            if (world.Name == "ServerWorld")
            {
                world.Dispose();
                Debug.Log("Server Closed.");
            }
        }
    }
    
}  
*/
 
