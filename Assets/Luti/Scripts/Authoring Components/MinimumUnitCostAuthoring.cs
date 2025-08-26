using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for configuring minimum unit costs
/// Place this on a GameObject in your scene to override default values
/// </summary>
public class MinimumUnitCostsAuthoring : MonoBehaviour
{
    [Header("Minimum Unit Costs Configuration")]
    [Tooltip("Minimum Resource 1 cost for the cheapest unit")]
    public int minResource1Cost = 10;

    [Tooltip("Minimum Resource 2 cost for the cheapest unit")]
    public int minResource2Cost = 5;

    [Header("Info")]
    [TextArea(3, 5)]
    public string description = "These values determine when a player can no longer afford to build any units. " +
                               "Set these to match your cheapest unit's resource costs. " +
                               "The game will end when no player has units alive AND cannot afford to build new ones.";
}

/// <summary>
/// Baker for MinimumUnitCostsAuthoring
/// </summary>
public class MinimumUnitCostsBaker : Baker<MinimumUnitCostsAuthoring>
{
    public override void Bake(MinimumUnitCostsAuthoring authoring)
    {
        var entity = GetEntity(TransformUsageFlags.None);

        AddComponent(entity, new MinimumUnitCosts
        {
            minResource1Cost = authoring.minResource1Cost,
            minResource2Cost = authoring.minResource2Cost
        });
    }
}