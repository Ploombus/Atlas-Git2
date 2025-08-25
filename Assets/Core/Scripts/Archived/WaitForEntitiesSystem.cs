/* Yes but no

using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Scenes;
using Managers;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
public partial struct WaitForEntitiesSystem : ISystem
{
    private float _delay;
    private bool _gameStarted;

    public void OnUpdate(ref SystemState state)
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false)
        {
            return;
        }

        EntityCommandBuffer buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        buffer.Playback(state.EntityManager);
        buffer.Dispose();

        if (_gameStarted) return;

        Debug.Log("[WaitForEntitiesSystem] Scene loaded, waiting a bit for ghosts...");
        // optional buffer
        

        Debug.Log("[WaitForEntitiesSystem] Declaring gameplay state ready.");
        
    }

}
*/