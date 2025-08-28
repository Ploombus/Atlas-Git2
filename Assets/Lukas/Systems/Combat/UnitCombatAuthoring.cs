using Unity.Entities;
using UnityEngine;
using Unity.NetCode;
using Unity.Mathematics;

public class UnitCombatAuthoring : MonoBehaviour
{
    [Header("Combat Stats")]
    [Range(0f, 1f)] public float hitchance = 0.2f;
    public float attacksPerSecond = 1.0f;
    public float attackRange = 1.5f;
    public float attackConeDeg = 30f;
    public int maxEntitiesHit = 3;
    [Range(0f, 1f)] public float attackSlowdown = 0.3f;
    public float maxChaseMeters = 15f;

    [Header("Animation Timing")]
    public float attackDuration = 1.0f;
    public float impactDelay = 0.55f;

    public class Baker : Baker<UnitCombatAuthoring>
    {
        public override void Bake(UnitCombatAuthoring a)
        {
            var e = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(e, new Attacker
            {
                aimEntity = Entity.Null,
                aimPosition = float3.zero,
                aimRotation = 0f,
                attackCooldownLeft = 0f,
                impactDelayTimeLeft = 0f,
                attackDurationTimeLeft = 0f,
                attackTick = 0,
                attackCancelTick = 0,

                isCharging = false,
                attackMove = false,
                autoTarget = true,
                maxChaseMeters = a.maxChaseMeters
            });
            AddComponent(e, new CombatStats
            {
                hitchance       = math.saturate(a.hitchance),
                attacksPerSecond= math.max(0.01f, a.attacksPerSecond),
                attackRange     = a.attackRange,
                attackConeDeg   = a.attackConeDeg,
                maxEntitiesHit = a.maxEntitiesHit,

                attackSlowdown  = math.saturate(a.attackSlowdown),
                attackDuration  = math.max(0.01f, a.attackDuration),
                impactDelay     = math.clamp(a.impactDelay, 0f, math.max(0.01f, a.attackDuration))
            });
        }
    }
}

public struct Attacker : IComponentData
{
    [GhostField] public Entity aimEntity;
    public float3 aimPosition;
    [GhostField] public float aimRotation;

    public float attackCooldownLeft;
    public float attackDurationTimeLeft;
    public float impactDelayTimeLeft;
    public bool impactDone;
    [GhostField] public uint attackTick;
    [GhostField] public uint attackCancelTick;

    [GhostField] public bool isCharging;
    [GhostField] public bool attackMove;
    [GhostField] public bool autoTarget;
    [GhostField] public float maxChaseMeters;
    public bool kitingEnabled; // I will implement later, its prepared
}

public struct CombatStats : IComponentData
{
    [GhostField] public float hitchance;
    [GhostField] public int maxEntitiesHit;
    [GhostField] public float attackRange;
    [GhostField] public float attackConeDeg;
    [GhostField] public float attacksPerSecond;
    [GhostField] public float attackSlowdown;
    [GhostField] public float attackDuration;
    [GhostField] public float impactDelay;
}