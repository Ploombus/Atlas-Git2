using Unity.Entities;
using Unity.NetCode;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct ClientPlayerStatsSystem : ISystem
{
    private int lastResource1;
    private int lastResource2;
    private int lastTotalScore;
    private bool hasInitialized;

    public void OnUpdate(ref SystemState state)
    {
        if (!PlayerStatsUtils.TryGetLocalPlayerStats(out var currentStats))
        {
            return; 
        }

        // Check if this is the first time or if values changed
        bool statsChanged = !hasInitialized ||
                           currentStats.resource1 != lastResource1 ||
                           currentStats.resource2 != lastResource2 ||
                           currentStats.totalScore != lastTotalScore;

        if (statsChanged)
        {
            // Update cached values
            lastResource1 = currentStats.resource1;
            lastResource2 = currentStats.resource2;
            lastTotalScore = currentStats.totalScore;
            hasInitialized = true;

            // Trigger UI events so other systems know stats changed
            PlayerStatsUIEvents.OnLocalStatsChanged?.Invoke(
                currentStats.resource1,
                currentStats.resource2,
                currentStats.totalScore,
                currentStats.resource1Score,
                currentStats.resource2Score
            );

            PlayerStatsUIEvents.OnAllPlayerStatsUpdated?.Invoke();
        }
    }
}

public static class PlayerStatsUIEvents
{
    public static System.Action<int, int, int, int, int> OnLocalStatsChanged; // r1, r2, totalScore, r1Score, r2Score
    public static System.Action OnAllPlayerStatsUpdated; // Signals scoreboard to refresh
}