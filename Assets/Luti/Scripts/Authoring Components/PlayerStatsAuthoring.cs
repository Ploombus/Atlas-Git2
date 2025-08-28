using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public class PlayerStatsAuthoring : MonoBehaviour
{
    [Header("Starting Values")]
    [SerializeField] private int startingResource1 = 0;
    [SerializeField] private int startingResource2 = 0;

    class Baker : Baker<PlayerStatsAuthoring>
    {
        public override void Bake(PlayerStatsAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None); 

            AddComponent(entity, new PlayerStats
            {
                resource1 = authoring.startingResource1,
                resource2 = authoring.startingResource2,
                totalScore = 0,
                resource1Score = 0,
                resource2Score = 0,
                playerId = -1 // stays -1 if not overwritten by server
            });

        }
    }
}