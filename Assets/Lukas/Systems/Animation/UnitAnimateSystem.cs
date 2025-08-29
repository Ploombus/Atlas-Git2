using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.NetCode;
using Managers;

[UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct UnitAnimateSystem : ISystem
{
    // ========================= KNOBS =========================
    // --- Idle/motion gating ---
    const float IDLE_TO_MOVING_MPS = 0.01f;
    const float HYSTERESIS_RATIO   = 0.8f;

    // --- Smoothing near idle ---
    const float LOCOMOTION_IDLE_DAMP_MULT = 1.3f;

    // --- Locomotion normalization (0..1) ---
    const float RUN_FULL_MPS = 3.0f;
    const float LOC_EASE     = 0.5f;

    // --- Chopping (wood) state handling (full-body clip lives on base layer) ---
    const bool  CHOP_FORCE_PLAYRATE      = true;
    const float CHOP_FORCED_PLAYRATE     = 1.00f;
    const bool  CHOP_SUPPRESS_LOCOMOTION = true;

    // --- Animator plumbing / names ---
    const int    BASE_LAYER = 0;
    const string UPPER_LAYER_NAME         = "Upper Body";
    const int    UPPER_LAYER_FALLBACK_IDX = 1;

    // Full-paths or state names (adjust to match your controller)
    const string LOCOMOTION_STATE = "Locomotion";

    // Attack symmetry
    const string ATTACK_TRIGGER_NAME = "Attack";
    const string ATTACK_STATE_NAME   = "Melee"; // state on both layers (AnyState->Attack)

    // Wood symmetry
    const string WOOD_TRIGGER_NAME   = "Wood";
    const string WOOD_STATE_NAME     = "Gathering Wood"; // state on both layers (AnyState->Gathering Wood)
    const string WOOD_STATE_PATH     = "Base Layer.Gathering Wood"; // for in-wood detection only

    // --- Small internals (leave as-is) ---
    const float LOCOMOTION_DAMP = 0.03f;
    const float EPS             = 1e-4f;

    // Optional: tiny deadzone after damping to avoid sub-1% residue
    const float LOCOMOTION_POST_DAMP_DEADZONE = 0.02f;

    static readonly int WoodStateHash = Animator.StringToHash(WOOD_STATE_PATH);

    static int GetUpperLayerIndex(Animator a)
    {
        int idx = a.GetLayerIndex(UPPER_LAYER_NAME);
        return (idx >= 0) ? idx : UPPER_LAYER_FALLBACK_IDX;
    }

    public void OnUpdate(ref SystemState state)
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false) return;

        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        float  delta = SystemAPI.Time.DeltaTime;
        double now   = SystemAPI.Time.ElapsedTime;

        // helper: snap tiny targets to exact 0 to avoid damping residue (use for axes)
        static void SetFloatZeroSafe(Animator a, string name, float target, float damp, float dt, float eps = 1e-3f)
        {
            if (math.abs(target) <= eps) a.SetFloat(name, 0f);
            else                         a.SetFloat(name, target, damp, dt);
        }

        // Init animator + caches
        foreach (var (unitGameObjectPrefab, localTransform, entity) in
                 SystemAPI.Query<UnitGameObjectPrefab, LocalTransform>()
                          .WithNone<UnitAnimatorReference>()
                          .WithEntityAccess())
        {
            var unitBody = Object.Instantiate(unitGameObjectPrefab.Value);

            // Hide to avoid 0,0,0 T-pose flash
            var renderers = unitBody.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false;

            unitBody.transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation);
            var anim = unitBody.GetComponent<Animator>();
            if (unitBody.GetComponent<UnitUpperBodyAim>() == null)
                unitBody.gameObject.AddComponent<UnitUpperBodyAim>();
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.Update(0f);

            // Reveal
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = true;

            buffer.AddComponent(entity, new UnitAnimatorReference { Value = anim });

            // prev-pos cache
            buffer.AddComponent(entity, new AnimationPreviousPosition {
                hasPreviousPosition = false,
                previousPosition    = localTransform.Position,
                samplePosition      = localTransform.Position,
                sampleTime          = now
            });

            // client caches
            if (SystemAPI.HasComponent<Attacker>(entity))
            {
                var st = SystemAPI.GetComponent<Attacker>(entity);
                buffer.AddComponent(entity, new AttackAnimClientCache {
                    lastSeenAttackTick = st.attackTick,
                    lastSeenCancelTick = st.attackCancelTick
                });
            }
            if (SystemAPI.HasComponent<GatheringWoodState>(entity))
            {
                var st = SystemAPI.GetComponent<GatheringWoodState>(entity);
                buffer.AddComponent(entity, new WoodAnimClientCache {
                    lastSeenStartTick  = st.woodStartTick,
                    lastSeenCancelTick = st.woodCancelTick
                });
            }
        }

        // Ensure caches for existing animated units
        foreach (var (animRef, entity) in SystemAPI.Query<UnitAnimatorReference>().WithEntityAccess())
        {
            if (!SystemAPI.HasComponent<AnimationPreviousPosition>(entity) &&
                SystemAPI.HasComponent<LocalTransform>(entity))
            {
                var lt = SystemAPI.GetComponent<LocalTransform>(entity);
                buffer.AddComponent(entity, new AnimationPreviousPosition {
                    hasPreviousPosition = true,
                    previousPosition    = lt.Position,
                    samplePosition      = lt.Position,
                    sampleTime          = now
                });
            }

            if (SystemAPI.HasComponent<Attacker>(entity) &&
                !SystemAPI.HasComponent<AttackAnimClientCache>(entity))
            {
                var st = SystemAPI.GetComponent<Attacker>(entity);
                buffer.AddComponent(entity, new AttackAnimClientCache {
                    lastSeenAttackTick = st.attackTick,
                    lastSeenCancelTick = st.attackCancelTick
                });
            }

            if (SystemAPI.HasComponent<GatheringWoodState>(entity) &&
                !SystemAPI.HasComponent<WoodAnimClientCache>(entity))
            {
                var st = SystemAPI.GetComponent<GatheringWoodState>(entity);
                buffer.AddComponent(entity, new WoodAnimClientCache {
                    lastSeenStartTick  = st.woodStartTick,
                    lastSeenCancelTick = st.woodCancelTick
                });
            }
        }

        // Animate predicted + interpolated (skip if dead)
        foreach (var (localTransform, animatorReference, health, unitStats, unitModifiers, prevPosRW, entity) in
                 SystemAPI.Query<LocalTransform, UnitAnimatorReference, RefRO<HealthState>, RefRO<UnitStats>, RefRO<UnitModifiers>, RefRW<AnimationPreviousPosition>>()
                          .WithEntityAccess())
        {
            var anim = animatorReference.Value;
            if (anim == null) continue;

            // Death flag
            var hs = health.ValueRO;
            var stage = SystemAPI.HasComponent<GhostOwnerIsLocal>(entity)
                ? HealthStageUtil.ApplyDelta(hs.currentStage, hs.healthChange, HealthStage.Dead, HealthStage.Healthy)
                : hs.currentStage;
            bool isDead = stage == HealthStage.Dead;
            anim.SetBool("Dead", isDead);
            if (isDead)
            {
                anim.SetFloat("Locomotion", 0f, LOCOMOTION_DAMP, delta);
                continue;
            }

            // If we are in or entering wood, suppress locomotion & force rate (visual-only)
            var stInfo = anim.GetCurrentAnimatorStateInfo(BASE_LAYER);
            var nxtInfo = anim.GetNextAnimatorStateInfo(BASE_LAYER);
            bool inWood = (stInfo.fullPathHash == WoodStateHash) || (nxtInfo.fullPathHash == WoodStateHash);

            if (inWood)
            {
                if (CHOP_SUPPRESS_LOCOMOTION)
                {
                    anim.SetFloat("Locomotion", 0f, LOCOMOTION_DAMP, delta);
                    anim.SetFloat("Forward", 0f, LOCOMOTION_DAMP, delta);
                    anim.SetFloat("Strafe",  0f, LOCOMOTION_DAMP, delta);
                }

                if (CHOP_FORCE_PLAYRATE)
                    animatorReference.Value.speed = CHOP_FORCED_PLAYRATE;

                animatorReference.Value.transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation);
                continue;
            }
            else
            {
                animatorReference.Value.speed = 1f;
            }

            // -------- Normal locomotion pipeline --------
            float3 posNow = localTransform.Position;
            float3 vPlanar = float3.zero;

            bool isSimulatingLocally =
                SystemAPI.HasComponent<Simulate>(entity) &&
                SystemAPI.HasComponent<GhostOwnerIsLocal>(entity);

            // per-frame delta
            float3 vFromTransform = float3.zero;
            if (prevPosRW.ValueRO.hasPreviousPosition)
            {
                float dt = math.max(1e-6f, delta);
                float3 dp = posNow - prevPosRW.ValueRO.previousPosition;
                dp.y = 0f;
                vFromTransform = dp / dt;
            }

            // windowed estimator
            double dtWindow = now - prevPosRW.ValueRO.sampleTime;
            float3 vWindow = float3.zero;
            if (dtWindow >= 0.08)
            {
                float3 dps = posNow - prevPosRW.ValueRO.samplePosition;
                dps.y = 0f;
                float invDtW = (float)(1.0 / math.max(1e-6, dtWindow));
                vWindow = dps * invDtW;

                prevPosRW.ValueRW.samplePosition = posNow;
                prevPosRW.ValueRW.sampleTime     = now;
            }

            // physics velocity (local sim)
            float3 vFromPhysics = float3.zero;
            if (isSimulatingLocally && SystemAPI.HasComponent<PhysicsVelocity>(entity))
            {
                var pv = SystemAPI.GetComponent<PhysicsVelocity>(entity).Linear;
                vFromPhysics = new float3(pv.x, 0f, pv.z);
            }

            // pick best signal
            vPlanar = vFromPhysics;
            if (math.lengthsq(vPlanar) < 1e-10f) vPlanar = vWindow;
            if (math.lengthsq(vPlanar) < 1e-10f) vPlanar = vFromTransform;

            // clamp spikes
            float speedCap = 20f;
            float mag = math.length(vPlanar);
            if (mag > speedCap) vPlanar *= speedCap / mag;

            // update prev-pos cache
            prevPosRW.ValueRW.previousPosition    = posNow;
            prevPosRW.ValueRW.hasPreviousPosition = true;

            // local axes
            float3 fwd = math.forward(localTransform.Rotation); fwd.y = 0f; fwd = math.normalizesafe(fwd, float3.zero);
            float3 right = math.cross(math.up(), fwd); right.y = 0f; right = math.normalizesafe(right, float3.zero);

            // signed components
            float forwardSpeed = math.dot(vPlanar, fwd);
            float strafeSpeed  = math.dot(vPlanar, right);
            float planarSpeed  = math.length(vPlanar);

            // normalized locomotion + easing
            float t = math.saturate(planarSpeed / math.max(0.001f, RUN_FULL_MPS));
            float locLinear = t;
            float locEase = math.lerp(locLinear, locLinear * locLinear * (3f - 2f * locLinear), math.saturate(LOC_EASE));

            // cheap moving state
            float prevLoc = anim.GetFloat("Locomotion");
            bool wasMoving = prevLoc > 0.1f;

            float enterThresh = IDLE_TO_MOVING_MPS;
            float exitThresh  = IDLE_TO_MOVING_MPS * HYSTERESIS_RATIO;
            bool moving = wasMoving ? (planarSpeed > exitThresh) : (planarSpeed > enterThresh);

            float nearIdle01 = math.saturate(locLinear * 2f);
            float locDamp = math.lerp(LOCOMOTION_DAMP * LOCOMOTION_IDLE_DAMP_MULT, LOCOMOTION_DAMP, nearIdle01);

            // drive locomotion
            anim.SetFloat("Locomotion", locEase, locDamp, delta);
            float locAfter = anim.GetFloat("Locomotion");
            if (locAfter < LOCOMOTION_POST_DAMP_DEADZONE && locEase < LOCOMOTION_POST_DAMP_DEADZONE)
                anim.SetFloat("Locomotion", 0f);

            // axes
            float forwardNorm = math.clamp(forwardSpeed / RUN_FULL_MPS, -1f, 1f);
            float strafeNorm  = math.clamp(strafeSpeed  / RUN_FULL_MPS, -1f, 1f);
            SetFloatZeroSafe(anim, "Forward", moving ? forwardNorm : 0f, locDamp, delta);
            SetFloatZeroSafe(anim,  "Strafe", moving ?  strafeNorm : 0f, locDamp, delta);

            // upper-body aim (disabled automatically by your wood branch above due to continue)
            {
                var upper = animatorReference.Value.GetComponent<UnitUpperBodyAim>();
                if (upper != null)
                {
                    bool enableAim = false;
                    float3 target = localTransform.Position + math.forward(localTransform.Rotation) * 8f;

                    if (SystemAPI.HasComponent<Attacker>(entity))
                    {
                        var att = SystemAPI.GetComponent<Attacker>(entity);
                        if (math.isfinite(att.aimRotation))
                        {
                            float3 dir = new float3(math.sin(att.aimRotation), 0f, math.cos(att.aimRotation));
                            target = localTransform.Position + dir * 8f;
                            enableAim = true;
                        }
                    }

                    upper.SetAimTarget(target, enableAim);
                }
            }

            // sync visual
            animatorReference.Value.transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation);
        }

        // =================== SYMMETRIC TRIGGERS (Attack & Wood) ===================

        // Attack: Trigger + persistent bool + CrossFade on BOTH layers; symmetric cancel.
        foreach (var (animRef, attackStateRO, cacheRW) in
                 SystemAPI.Query<UnitAnimatorReference, RefRO<Attacker>, RefRW<AttackAnimClientCache>>())
        {
            var anim = animRef.Value;
            int upperIdx = GetUpperLayerIndex(anim);

            if (attackStateRO.ValueRO.attackTick != cacheRW.ValueRO.lastSeenAttackTick)
            {
                anim.SetTrigger(ATTACK_TRIGGER_NAME);

                // Enter the attack state explicitly on both layers this frame
                anim.CrossFade(ATTACK_STATE_NAME, 0.05f, BASE_LAYER, 0f);
                anim.CrossFade(ATTACK_STATE_NAME, 0.05f, upperIdx,   0f);

                cacheRW.ValueRW.lastSeenAttackTick = attackStateRO.ValueRO.attackTick;
            }

            if (attackStateRO.ValueRO.attackCancelTick != cacheRW.ValueRO.lastSeenCancelTick)
            {
                anim.ResetTrigger(ATTACK_TRIGGER_NAME);

                // Exit to locomotion on both layers
                anim.CrossFade(LOCOMOTION_STATE, 0.05f, BASE_LAYER, 0f);
                anim.CrossFade(LOCOMOTION_STATE, 0.05f, upperIdx,   0f);

                cacheRW.ValueRW.lastSeenCancelTick = attackStateRO.ValueRO.attackCancelTick;
            }
        }

        // Wood: Trigger + persistent bool + CrossFade on BOTH layers; symmetric cancel.
        foreach (var (animRef, woodStateRO, cacheRW) in
                 SystemAPI.Query<UnitAnimatorReference, RefRO<GatheringWoodState>, RefRW<WoodAnimClientCache>>())
        {
            var anim = animRef.Value;
            int upperIdx = GetUpperLayerIndex(anim);

            if (woodStateRO.ValueRO.woodStartTick != cacheRW.ValueRO.lastSeenStartTick)
            {
                anim.SetTrigger(WOOD_TRIGGER_NAME);

                // Enter the wood state explicitly on both layers this frame
                anim.CrossFade(WOOD_STATE_NAME, 0.05f, BASE_LAYER, 0f);
                anim.CrossFade(WOOD_STATE_NAME, 0.05f, upperIdx,   0f);

                cacheRW.ValueRW.lastSeenStartTick = woodStateRO.ValueRO.woodStartTick;
            }

            if (woodStateRO.ValueRO.woodCancelTick != cacheRW.ValueRO.lastSeenCancelTick)
            {
                anim.ResetTrigger(WOOD_TRIGGER_NAME);

                // Exit to locomotion on both layers
                anim.CrossFade(LOCOMOTION_STATE, 0.05f, BASE_LAYER, 0f);
                anim.CrossFade(LOCOMOTION_STATE, 0.05f, upperIdx,   0f);

                cacheRW.ValueRW.lastSeenCancelTick = woodStateRO.ValueRO.woodCancelTick;
            }
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}

// ===================== Small PODs =====================

public struct AttackAnimClientCache : IComponentData
{
    public uint lastSeenAttackTick;
    public uint lastSeenCancelTick;
}

public struct AnimationPreviousPosition : IComponentData
{
    public bool   hasPreviousPosition;
    public float3 previousPosition;

    public float3 samplePosition;
    public double sampleTime;
}

public struct WoodAnimClientCache : IComponentData
{
    public uint lastSeenStartTick;
    public uint lastSeenCancelTick;
}