using Unity.Entities;
using Unity.NetCode;

/// <summary>
/// Simple system to trigger scoreboard updates when PlayerStats change
/// Runs on client and monitors for changes in PlayerStats components
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct ScoreboardUpdateSystem : ISystem
{
    private ComponentLookup<PlayerStats> playerStatsLookup;
    private EntityQuery playerStatsQuery;

    // Cache previous values to detect changes
    private int lastLocalResource1;
    private int lastLocalResource2;
    private int lastLocalTotalScore;
    private int lastPlayerCount;
    private bool hasInitialized;

    public void OnCreate(ref SystemState state)
    {
        playerStatsLookup = state.GetComponentLookup<PlayerStats>(true);

        playerStatsQuery = state.EntityManager.CreateEntityQuery(
            ComponentType.ReadOnly<PlayerStats>()
        );

        // Require at least one PlayerStats entity to run
        state.RequireForUpdate(playerStatsQuery);
    }

    public void OnUpdate(ref SystemState state)
    {
        playerStatsLookup.Update(ref state);

        // Check if number of players changed
        int currentPlayerCount = playerStatsQuery.CalculateEntityCount();
        bool playerCountChanged = currentPlayerCount != lastPlayerCount;

        // Check if local player stats changed
        bool localStatsChanged = CheckForLocalPlayerChanges();

        // Trigger update if anything important changed
        if (!hasInitialized || playerCountChanged || localStatsChanged)
        {
            // Update cache
            lastPlayerCount = currentPlayerCount;
            hasInitialized = true;

            // Trigger UI update through events
            PlayerStatsUIEvents.OnAllPlayerStatsUpdated?.Invoke();
        }
    }

    private bool CheckForLocalPlayerChanges()
    {
        if (!PlayerStatsUtils.TryGetLocalPlayerStats(out var currentStats))
        {
            // No local player found - this might be a change if we had data before
            bool hadDataBefore = hasInitialized;
            return hadDataBefore;
        }

        // Check if any values changed
        bool statsChanged = !hasInitialized ||
                           currentStats.resource1 != lastLocalResource1 ||
                           currentStats.resource2 != lastLocalResource2 ||
                           currentStats.totalScore != lastLocalTotalScore;

        if (statsChanged)
        {
            // Update cached values
            lastLocalResource1 = currentStats.resource1;
            lastLocalResource2 = currentStats.resource2;
            lastLocalTotalScore = currentStats.totalScore;

            // Also trigger the local stats changed event
            PlayerStatsUIEvents.OnLocalStatsChanged?.Invoke(
                currentStats.resource1,
                currentStats.resource2,
                currentStats.totalScore,
                currentStats.resource1Score,
                currentStats.resource2Score
            );

            return true;
        }

        return false;
    }
}