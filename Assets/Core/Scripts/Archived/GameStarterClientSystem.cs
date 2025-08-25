/*
using Unity.Entities;
using Unity.NetCode;
using UnityEngine.SceneManagement;
using UnityEngine;
using Managers;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct GameStarterClientSystem : ISystem
{
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

        Debug.Log("[Client] Starting game and switching scenes...");
        _gameStarted = true;

        SceneManager.LoadScene("GameScene", LoadSceneMode.Additive);
        SceneManager.UnloadSceneAsync("MenuScene");
    }

}
*/