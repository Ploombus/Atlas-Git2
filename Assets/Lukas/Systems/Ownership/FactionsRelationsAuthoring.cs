using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class FactionRelationsAuthoring : MonoBehaviour
{
    [Range(1,32)] public int factionCount = 32;
    public bool friendlyFireEnabled = false;

    public class Baker : Baker<FactionRelationsAuthoring>
    {
        public override void Bake(FactionRelationsAuthoring a)
        {
            var e = CreateAdditionalEntity(TransformUsageFlags.None);

            int  fc        = math.clamp(a.factionCount, 1, 32);
            uint allBitsFc = (fc == 32) ? 0xFFFFFFFFu : ((1u << fc) - 1u);

            // 1) Build with a Temp builder
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<FactionRelationsBlob>();

            var enemies = builder.Allocate(ref root.EnemiesMask, 32);
            var allies  = builder.Allocate(ref root.AlliesMask,  32);

            for (int i = 0; i < 32; i++) 
            {
                if (i < fc)
                {
                    uint selfBit = 1u << i;
                    enemies[i] = (allBitsFc & ~selfBit); // everyone except self
                }
                else
                {
                    enemies[i] = 0u; // unused rows
                }
                allies[i] = 0u;
            }

            // 2) Create the blob with Allocator.Persistent
            var blobRef = builder.CreateBlobAssetReference<FactionRelationsBlob>(Allocator.Persistent);
            builder.Dispose();

            // 3) Register with the baking store (dedupe + lifetime mgmt)
            AddBlobAsset(ref blobRef, out var _);

            // 4) Attach to singleton; do NOT dispose blobRef yourself
            AddComponent(e, new FactionRelations { Blob = blobRef });
            AddComponent(e, new CombatRules { FriendlyFireEnabled = a.friendlyFireEnabled });
            AddComponent(e, new FactionCount { Value = (byte)fc });
        }
    }
}

// ===== runtime components =====
public struct FactionRelations : IComponentData
{
    public BlobAssetReference<FactionRelationsBlob> Blob;
}

public struct FactionRelationsBlob
{
    public BlobArray<uint> EnemiesMask; // 32 entries; bit j set => j hostile to i
    public BlobArray<uint> AlliesMask;  // unused for this test
}

public struct CombatRules : IComponentData
{
    public bool FriendlyFireEnabled;
}

public struct FactionCount : IComponentData { public byte Value; }

public struct TemporaryFactionOverride : IComponentData
{
    public byte  FactionId;
    public float SecondsLeft;
}