using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Managers;
using UnityEngine.SceneManagement;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct StartGameRpcClientSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        foreach (var (rpc, entity) in SystemAPI.Query<RefRO<StartGameRpc>>().WithEntityAccess())
        {
            Debug.Log("[Client] Starting Game RPC Received.");

            SceneManager.LoadScene("GameScene", LoadSceneMode.Additive);
            SceneManager.UnloadSceneAsync("MenuScene");

            CheckGameplayStateAccess.SetGameplayState(WorldManager.GetClientWorld(), true);
            CheckGameplayStateAccess.SetGameplayState(WorldManager.GetServerWorld(), true);

            ecb.DestroyEntity(entity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}