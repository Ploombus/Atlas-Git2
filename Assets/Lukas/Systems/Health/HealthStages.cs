using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

public enum HealthStage : byte
{
    Dead     = 0,
    Critical = 1,
    Wounded  = 2,
    Grazed   = 3,
    Healthy  = 4
}

public static class HealthStageUtil
{
    public static HealthStage Clamp(HealthStage s, byte min = 0, byte max = 255)
        => (HealthStage)math.clamp((int)s, (int)min, (int)max);

    public static HealthStage ApplyDelta(HealthStage s, int delta,
        HealthStage min = HealthStage.Dead, HealthStage max = HealthStage.Healthy)
        => (HealthStage)math.clamp((int)s + delta, (int)min, (int)max);

    public static HealthStage Damage(HealthStage s, int steps = 1) => ApplyDelta(s, -steps);
    public static HealthStage Heal  (HealthStage s, int steps = 1) => ApplyDelta(s,  steps);
}