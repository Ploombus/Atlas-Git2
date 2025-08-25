/*
using Unity.Entities;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct ClientWorldRegisterSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        WorldRegistry.Register(state.World);
        Debug.Log("World registered: " + state.World.Name);
    }
}
*/