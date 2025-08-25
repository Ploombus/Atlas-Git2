using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

public static class PlayerStatsServerUtils
{
    public static void AwardResources(EntityCommandBuffer ecb, Entity playerConnection,
        int resource1Amount, int resource2Amount, bool awardScorePoints = true)
    {
        var eventEntity = ecb.CreateEntity();
        ecb.AddComponent(eventEntity, new StatsChangeEvent
        {
            resource1Delta = resource1Amount,
            resource2Delta = resource2Amount,
            playerConnection = playerConnection,
            awardScorePoints = awardScorePoints
        });
    }

    public static void SpendResources(EntityCommandBuffer ecb, Entity playerConnection,
        int resource1Cost, int resource2Cost)
    {
        var eventEntity = ecb.CreateEntity();
        ecb.AddComponent(eventEntity, new StatsChangeEvent
        {
            resource1Delta = -resource1Cost,
            resource2Delta = -resource2Cost,
            playerConnection = playerConnection,
            awardScorePoints = false // Spending doesn't give score
        });
    }

    public static void AwardScore(EntityCommandBuffer ecb, Entity playerConnection,
        int scorePoints, ScoreReason reason = ScoreReason.Custom)
    {
        var eventEntity = ecb.CreateEntity();
        ecb.AddComponent(eventEntity, new DirectScoreEvent
        {
            scorePoints = scorePoints,
            playerConnection = playerConnection,
            reason = reason
        });
    }

    public static void AwardWood(EntityCommandBuffer ecb, Entity playerConnection, int woodAmount = 1)
    {
        AwardResources(ecb, playerConnection, woodAmount, 0, awardScorePoints: true);
    }

    public static void AwardStone(EntityCommandBuffer ecb, Entity playerConnection, int stoneAmount = 1)
    {
        AwardResources(ecb, playerConnection, 0, stoneAmount, awardScorePoints: true);
    }

    public static void AwardKillResources(EntityCommandBuffer ecb, Entity playerConnection,
        int resource1Reward, int resource2Reward, int bonusScore = 0)
    {
        // Award resources with score points
        AwardResources(ecb, playerConnection, resource1Reward, resource2Reward, awardScorePoints: true);

        // Award bonus score if specified
        if (bonusScore > 0)
        {
            AwardScore(ecb, playerConnection, bonusScore, ScoreReason.UnitKill);
        }
    }

    public static bool TryGetPlayerConnectionFromUnit(ref SystemState state, Entity unitEntity,
        NativeParallelHashMap<int, Entity> networkIdToConnection, out Entity playerConnection)
    {
        playerConnection = Entity.Null;

        // Get the unit owner's network ID
        if (!state.EntityManager.HasComponent<GhostOwner>(unitEntity))
            return false;

        int ownerId = state.EntityManager.GetComponentData<GhostOwner>(unitEntity).NetworkId;

        // Find the connection entity for this network ID
        return networkIdToConnection.TryGetValue(ownerId, out playerConnection);
    }
}