using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// Authoring component for PlayerStats Ghost prefab
/// This bakes to create a networked PlayerStats entity
/// </summary>
public class PlayerStatsAuthoring : MonoBehaviour
{
    [Header("Starting Values")]
    [SerializeField] private int startingResource1 = 100;
    [SerializeField] private int startingResource2 = 100;

    class Baker : Baker<PlayerStatsAuthoring>
    {
        public override void Bake(PlayerStatsAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None); // No transform needed for stats

            // Add PlayerStats component - this will be replicated via Ghost system
            AddComponent(entity, new PlayerStats
            {
                resource1 = authoring.startingResource1,
                resource2 = authoring.startingResource2,
                totalScore = 0,
                resource1Score = 0,
                resource2Score = 0,
                playerId = -1 // Will be set by server when spawned
            });

            // GhostOwner will be added by the server when spawning
        }
    }
}