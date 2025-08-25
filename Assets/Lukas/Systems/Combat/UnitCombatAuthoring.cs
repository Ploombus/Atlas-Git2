using Unity.Entities;
using UnityEngine;
using Unity.NetCode;
using Unity.Mathematics;

public class UnitCombatAuthoring : MonoBehaviour
{
    [Header("Combat Stats")]
    [Range(0f, 1f)] public float hitchance = 0.33f;
    public float attackRange = 1.5f;
    public float attackConeDeg = 30f;
    public float attacksPerSecond = 1.0f;
    [Range(0f, 1f)] public float attackSlowdown = 0.7f;

    [Header("Animation Timing")]
    public float attackDuration = 1.0f;  // total attack window
    public float impactDelay = 0.55f; // time to impact within the window

    public class Baker : Baker<UnitCombatAuthoring>
    {
        public override void Bake(UnitCombatAuthoring a)
        {
            var e = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(e, new Attacker
            {
                cooldownLeft = 0f,
                impactDelayTimeLeft = 0f,
                attackDurationTimeLeft = 0f,
                swingStartSequence = 0,

                isCharging = false,

                attackMove = false,
                autoTarget = true,
                maxChaseMeters = -1f,
            });
            AddComponent(e, new CombatStats
            {
                hitchance       = math.saturate(a.hitchance),
                attackRange     = a.attackRange,
                attacksPerSecond= math.max(0.01f, a.attacksPerSecond),
                attackConeDeg   = a.attackConeDeg,

                attackSlowdown  = math.saturate(a.attackSlowdown),
                attackDuration  = math.max(0.01f, a.attackDuration),
                impactDelay     = math.clamp(a.impactDelay, 0f, math.max(0.01f, a.attackDuration))
            });
            AddComponent(e, new AttackAnimationState { attackTick = 0 });
        }
    }
}

public struct Attacker : IComponentData
{
    public Entity attackTargetEntity;
    public float3 attackDirection;
    public float cooldownLeft;
    public float impactDelayTimeLeft; // seconds left to impact
    public float attackDurationTimeLeft; // seconds of attack anim left
    public int swingStartSequence; // lastAppliedSequence captured at swing start
    [GhostField] public bool isCharging;

    [GhostField] public bool attackMove;
    [GhostField] public bool autoTarget;
    [GhostField] public float maxChaseMeters;
}

public struct CombatStats : IComponentData
{
    [GhostField] public float hitchance;
    [GhostField] public float attackRange;
    [GhostField] public float attackConeDeg;
    [GhostField] public float attacksPerSecond;
    [GhostField] public float attackSlowdown;
    [GhostField] public float attackDuration;
    [GhostField] public float impactDelay; // time from swing start to impact
}
public struct AttackAnimationState : IComponentData
{
    [GhostField] public uint attackTick; // increments every swing (hit or miss)
    [GhostField] public uint attackCancelTick; // increments when a swing is canceled pre-impact
}