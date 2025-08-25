using Unity.Entities;
using UnityEngine;
using Unity.NetCode;
using Unity.Mathematics;

public class UnitAuthoring : MonoBehaviour
{
    public GameObject UnitGameObjectPrefab;
    public float moveSpeed = 5f;
    public float detectionRadius = 10f;
    public float weight = 10f;

    public class Baker : Baker<UnitAuthoring>
    {
        public override void Bake(UnitAuthoring authoring)
        {
            var e = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(e, new Unit());
            AddComponent(e, new UnitTargets
            {
                destinationPosition = float3.zero,
                destinationRotation = float.NaN,
                targetPosition      = float3.zero,
                targetRotation      = float.NaN,
                lastAppliedSequence = 0,
                activeTargetSet     = false,
                targetEntity        = Entity.Null,
                hasArrived          = true
            });

            AddComponent(e, new UnitTargetsNetcode
            {
                requestDestinationPosition = float3.zero,
                requestDestinationRotation = float.NaN,
                requestLastAppliedSequence = 0,
                requestActiveTargetSet     = false,
                requestTargetEntity        = Entity.Null,
                requestAttackMove          = false
            });
            AddComponent(e, new HealthState
            {
                currentStage = HealthStage.Healthy,
                previousStage = HealthStage.Healthy,
                healthChange = 0
            });
            AddComponent(e, new UnitStats
            {
                moveSpeed = authoring.moveSpeed,
                detectionRadius = authoring.detectionRadius,
                weight = math.max(0f, authoring.weight)
            });
            AddComponent(e, new UnitModifiers { moveSpeedMultiplier = 1f });
            AddComponent(e, new UnitWeightCache { lastWeight = float.NaN });
            AddComponentObject(e, new UnitGameObjectPrefab { Value = authoring.UnitGameObjectPrefab });
            AddComponent(e, new MovementDotRef { Dot = Entity.Null });
            AddComponent(e, new GatheringWoodState { woodStartTick = 0, woodCancelTick = 0 }); // gathering test
        }
    }
}


public struct Unit : IComponentData { }

//Movement and targeting
public struct UnitTargets : IComponentData
{
    [GhostField] public float3 destinationPosition;
    [GhostField] public float destinationRotation;
    [GhostField] public float3 targetPosition;
    [GhostField] public float targetRotation;
    [GhostField] public int lastAppliedSequence;
    [GhostField] public bool activeTargetSet;
    public Entity targetEntity;
    public bool hasArrived;

    //[GhostField] public bool isRunning;
}
public struct UnitTargetsNetcode : IInputComponentData
{
    public float3 requestDestinationPosition;
    public float requestDestinationRotation;
    public int requestLastAppliedSequence;
    public bool requestActiveTargetSet;
    public Entity requestTargetEntity;
    public bool requestAttackMove;
    public Stance requestStance;

    //public bool requestIsRunning;
}

//Stats
public struct HealthState : IComponentData
{
    [GhostField] public HealthStage currentStage;
    [GhostField] public HealthStage previousStage;
    public int healthChange;
}
public struct UnitStats : IComponentData
{
    [GhostField] public float moveSpeed;
    [GhostField] public float detectionRadius;
    [GhostField] public float weight;
}
public struct UnitModifiers : IComponentData
{
    public float moveSpeedMultiplier;
}

//Mass handling
public struct UnitMassSynced : IComponentData { }
public struct UnitWeightCache : IComponentData { public float lastWeight; }

//Body / Animation / UI
public sealed class UnitGameObjectPrefab : IComponentData { public GameObject Value; }
public sealed class UnitAnimatorReference : IComponentData { public Animator Value; }
public sealed class UnitHealthIndicator : IComponentData { public Renderer backgroundRenderer; public Renderer fillRenderer; }
public struct MovementDotRef : IComponentData { public Entity Dot; }


// Gathering test
[GhostComponent(PrefabType = GhostPrefabType.All)]
public struct GatheringWoodState : IComponentData
{
    [GhostField] public uint woodStartTick;   // ++ each time a swing begins
    [GhostField] public uint woodCancelTick;  // ++ when we stop chopping (leave range / depleted)
}