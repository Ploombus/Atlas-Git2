using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class HealthStageTableAuthoring : MonoBehaviour
{
    [System.Serializable]
    public struct Row
    {
        public HealthStage stage;
        public float moveSpeedMultiplier;
    }

    public List<Row> rows = new()
    {
        new Row{ stage = HealthStage.Healthy,  moveSpeedMultiplier = 1.00f },
        new Row{ stage = HealthStage.Grazed,   moveSpeedMultiplier = 0.90f },
        new Row{ stage = HealthStage.Wounded,  moveSpeedMultiplier = 0.70f },
        new Row{ stage = HealthStage.Critical, moveSpeedMultiplier = 0.50f },
        new Row{ stage = HealthStage.Dead,     moveSpeedMultiplier = 0.00f },
    };

    public class Baker : Baker<HealthStageTableAuthoring>
    {
        public override void Bake(HealthStageTableAuthoring authoring)
        {
            // Build the blob
            var builder = new BlobBuilder(Unity.Collections.Allocator.Temp);
            ref var table = ref builder.ConstructRoot<HealthStageTable>();
            var array = builder.Allocate(ref table.entries, authoring.rows.Count);
            for (int i = 0; i < authoring.rows.Count; i++)
            {
                array[i] = new HealthStageTable.StageEntry
                {
                    stage = authoring.rows[i].stage,
                    moveSpeedMultiplier = authoring.rows[i].moveSpeedMultiplier,
                };
            }
            var blob = builder.CreateBlobAssetReference<HealthStageTable>(Unity.Collections.Allocator.Persistent);
            builder.Dispose();

            AddBlobAsset(ref blob, out var _);

            // Store it on a singleton entity so systems can read it
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new HealthStageTableSingleton { Table = blob });
        }
    }
}

public struct HealthStageTableSingleton : IComponentData
{
    public BlobAssetReference<HealthStageTable> Table;
}