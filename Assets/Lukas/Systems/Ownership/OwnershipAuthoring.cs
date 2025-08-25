using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.NetCode;

public class OwnershipAuthoring : MonoBehaviour
{
    public Color color = Color.white;
    
    [Header("Faction ID")]
    [Range(0, 31)] public int FactionId = 0;

    public class Baker : Baker<OwnershipAuthoring>
    {
        public override void Bake(OwnershipAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new Owner
            {
                OwnerColor = new float4(authoring.color.r, authoring.color.g, authoring.color.b, authoring.color.a)
            });
            AddComponent(entity, new Faction { FactionId = (byte)math.clamp(authoring.FactionId, 0, 31) });
        }
    }
}

public struct Owner : IComponentData
{
    [GhostField] public float4 OwnerColor;
}
public struct Faction : IComponentData
{
    public byte FactionId; // supports 32 factions
}