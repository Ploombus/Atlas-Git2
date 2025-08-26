using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;

/// <summary>
/// Client system that handles victory screen display
/// Processes ShowVictoryScreenRpc RPCs from server
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct VictoryScreenClientSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkTime>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // Process victory screen RPCs from server
        foreach (var (victoryRpc, receiveRequest, rpcEntity) in
            SystemAPI.Query<RefRO<ShowVictoryScreenRpc>, RefRO<ReceiveRpcCommandRequest>>()
            .WithEntityAccess())
        {
            // Get final scores for display
            var finalScores = PlayerStatsUtils.GetAllPlayerStats(state.World);

            // Create victory screen data
            var victoryData = new VictoryScreenData
            {
                winnerPlayerId = victoryRpc.ValueRO.winnerPlayerId,
                isLocalPlayerWinner = victoryRpc.ValueRO.isLocalPlayerWinner,
                finalScores = finalScores
            };

            // Trigger UI event
            VictoryScreenEvents.ShowVictoryScreen(victoryData);

            // Clean up the RPC
            ecb.DestroyEntity(rpcEntity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}

/// <summary>
/// RPC command for sending victory screen events to clients
/// </summary>
public struct ShowVictoryScreenRpc : IRpcCommand
{
    public int winnerPlayerId;
    public bool isLocalPlayerWinner;
}