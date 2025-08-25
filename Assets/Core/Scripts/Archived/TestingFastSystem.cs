/*using Unity.Entities;
using Managers;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
public partial struct TestingFastSystem : ISystem
{
    public static void StartFastTest()
    {
        //var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        //CheckGameplayStateAccess.SetGameplayState(WorldManager.GetClientWorld(), true);
        //CheckGameplayStateAccess.SetGameplayState(WorldManager.GetServerWorld(), true);
        //foreach (var world in World.All)
        //{
        //    CheckGameplayStateAccess.SetGameplayState(world, true);
        //    bool isInGame = CheckGameplayStateAccess.GetGameplayState(world);
        //    Debug.Log(world + ":: is in game = " + isInGame);
	    //}
    }

    public void OnUpdate(ref SystemState state)
    {

        //foreach (var (rpc, entity) in SystemAPI.Query<RefRO<StartGameRpc>>().WithEntityAccess())
        //{}

        //ecb.Playback(state.EntityManager);
        //ecb.Dispose();
    }
}
*/

//[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]