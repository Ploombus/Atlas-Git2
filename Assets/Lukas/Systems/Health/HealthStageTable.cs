using Unity.Entities;

public struct HealthStageTable
{
    public BlobArray<StageEntry> entries;

    public struct StageEntry
    {
        public HealthStage stage;
        public float moveSpeedMultiplier;
        // extend here later if needed
    }
}