using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.NetCode;

[UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct UnitAnimateSystem : ISystem
{
    // ========================= KNOBS =========================
    // --- Idle/motion gating ---
    // When planar speed ≤ this, we are in true Idle (params zeroed, but idle clip plays at Speed=1).
    const float IDLE_TO_MOVING_MPS = 0.0f;
    // Hysteresis: when slowing down, drop to idle below (IDLE_TO_MOVING_MPS * HYSTERESIS_RATIO)
    const float HYSTERESIS_RATIO = 0.8f;   // 0.5..0.85 recommended

    // --- Smoothing near idle ---
    const float LOCOMOTION_IDLE_DAMP_MULT = 1.3f;   // multiplies LOCOMOTION_DAMP near 0
    const float SPEED_IDLE_DAMP_MULT      = 1.3f;   // multiplies SPEED_DAMP near 0

    // --- Locomotion normalization (0..1) ---
    const float RUN_FULL_MPS = 3.0f;       // what “full run” means for Locomotion/Forward/Strafe
    const float LOC_EASE     = 0.5f;       // 0 = linear, 1 = smoothstep near 0

    // --- Movement playback caps (movement only; idle always Speed=1) ---
    const float MIN_MOVE_PLAYRATE = 0.70f;
    const float MAX_MOVE_PLAYRATE = 1.20f;

    // --- Clip pacing reference (how far clips move at Speed=1) ---
    const float CLIP_FWD_MPS    = 5.00f;
    const float CLIP_BACK_MPS   = 5.00f;
    const float CLIP_STRAFE_MPS = 3.00f;

    // --- Chopping (wood) state handling ---
    // Force a fixed playrate while in the chop state so timing matches server impact delay.
    const bool  CHOP_FORCE_PLAYRATE   = true;
    const float CHOP_FORCED_PLAYRATE  = 1.00f;   // typically 1.0
    const bool  CHOP_SUPPRESS_LOCOMOTION = true; // While chopping, suppress locomotion inputs so the blend tree sits still.

    // --- Animator plumbing ---
    const int   BASE_LAYER = 0; // 0 unless you use layers
    const string WOOD_STATE_PATH   = "Base Layer.Gathering Wood"; // full path to be robust
    const string WOOD_TRIGGER_NAME = "Wood";

    // --- Small internals (leave as-is) ---
    const float LOCOMOTION_DAMP = 0.03f; // Animator float damping for Locomotion/axes
    const float SPEED_DAMP      = 0.05f; // Animator float damping for Speed
    const float EPS             = 1e-4f;

    // Cached hashes
    static readonly int WoodStateHash = Animator.StringToHash(WOOD_STATE_PATH);

    // =========================================================

    public void OnUpdate(ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        float delta = SystemAPI.Time.DeltaTime;

        // Init animator for entities missing a reference
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
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.Update(0f);

            // Reveal
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = true;

            buffer.AddComponent(entity, new UnitAnimatorReference { Value = anim });
            buffer.AddComponent(entity, new AnimationPreviousPosition {
                hasPreviousPosition = false,
                previousPosition    = localTransform.Position
            });
        }

        // Ensure client cache exists for any animated combat unit
        foreach (var (animRef, entity) in
                 SystemAPI.Query<UnitAnimatorReference>()
                          .WithAll<AttackAnimationState>()
                          .WithNone<AttackAnimClientCache>()
                          .WithEntityAccess())
        {
            var st = SystemAPI.GetComponent<AttackAnimationState>(entity);
            buffer.AddComponent(entity, new AttackAnimClientCache {
                lastSeenAttackTick = st.attackTick,
                lastSeenCancelTick = st.attackCancelTick
            });
        }

        // Ensure client cache exists for any animated wood-gathering unit
        foreach (var (animRef, entity) in
                 SystemAPI.Query<UnitAnimatorReference>()
                          .WithAll<GatheringWoodState>()
                          .WithNone<WoodAnimClientCache>()
                          .WithEntityAccess())
        {
            var st = SystemAPI.GetComponent<GatheringWoodState>(entity);
            buffer.AddComponent(entity, new WoodAnimClientCache {
                lastSeenStartTick  = st.woodStartTick,
                lastSeenCancelTick = st.woodCancelTick
            });
        }
        
        // Ensure prev-pos exists for any animated unit (covers entities that already had UnitAnimatorReference)
        foreach (var (lt, _, e) in
                SystemAPI.Query<RefRO<LocalTransform>, UnitAnimatorReference>()
                        .WithNone<AnimationPreviousPosition>()
                        .WithEntityAccess())
        {
            buffer.AddComponent(e, new AnimationPreviousPosition
            {
                hasPreviousPosition = true,
                previousPosition    = lt.ValueRO.Position,
                samplePosition      = lt.ValueRO.Position,           // NEW
                sampleTime          = SystemAPI.Time.ElapsedTime     // NEW
            });
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
                anim.SetFloat("Speed", 0f, SPEED_DAMP, delta);
                continue;
            }

            // If we are currently in (or transitioning into) the wood state, suppress locomotion and force playrate
            var stInfo = anim.GetCurrentAnimatorStateInfo(BASE_LAYER);
            var nxtInfo = anim.GetNextAnimatorStateInfo(BASE_LAYER);
            bool inWood = (stInfo.fullPathHash == WoodStateHash) || (nxtInfo.fullPathHash == WoodStateHash);

            if (inWood)
            {
                if (CHOP_SUPPRESS_LOCOMOTION)
                {
                    anim.SetFloat("Locomotion", 0f, LOCOMOTION_DAMP, delta);
                    anim.SetFloat("Forward", 0f, LOCOMOTION_DAMP, delta);
                    anim.SetFloat("Strafe", 0f, LOCOMOTION_DAMP, delta);
                }

                if (CHOP_FORCE_PLAYRATE)
                {
                    anim.SetFloat("Speed", CHOP_FORCED_PLAYRATE, SPEED_DAMP, delta);
                }

                // Keep visual synced
                animatorReference.Value.transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation);
            }
            else
            {
                // -------- Normal locomotion pipeline --------

                // Planar velocity: prefer transform-delta unless we are simulating locally (owner-predicted)
                float3 posNow = localTransform.Position;
                float3 vPlanar = float3.zero;

                bool isSimulatingLocally =
                    SystemAPI.HasComponent<Simulate>(entity) &&
                    SystemAPI.HasComponent<GhostOwnerIsLocal>(entity);

                // Per-frame transform delta (may be ~0 for some ordering)
                float3 vFromTransform = float3.zero;
                if (prevPosRW.ValueRO.hasPreviousPosition)
                {
                    float dt = math.max(1e-6f, delta);
                    float3 dp = posNow - prevPosRW.ValueRO.previousPosition;
                    dp.y = 0f;
                    vFromTransform = dp / dt;
                }

                // Windowed estimator (~0.10s) — insensitive to tiny per-frame deltas
                double now = SystemAPI.Time.ElapsedTime;
                double dtWindow = now - prevPosRW.ValueRO.sampleTime;
                float3 vWindow = float3.zero;
                if (dtWindow >= 0.08) // ~80ms to 150ms is fine; pick 80ms for responsiveness
                {
                    float3 dps = posNow - prevPosRW.ValueRO.samplePosition;
                    dps.y = 0f;
                    float invDtW = (float)(1.0 / math.max(1e-6, dtWindow));
                    vWindow = dps * invDtW;

                    // advance the sampling window
                    prevPosRW.ValueRW.samplePosition = posNow;
                    prevPosRW.ValueRW.sampleTime     = now;
                }

                // Physics velocity only when we truly simulate locally
                float3 vFromPhysics = float3.zero;
                if (isSimulatingLocally && SystemAPI.HasComponent<PhysicsVelocity>(entity))
                {
                    var pv = SystemAPI.GetComponent<PhysicsVelocity>(entity).Linear;
                    vFromPhysics = new float3(pv.x, 0f, pv.z);
                }

                // Pick the best signal in this order: physics (local), windowed, per-frame
                vPlanar = vFromPhysics;
                if (math.lengthsq(vPlanar) < 1e-10f) vPlanar = vWindow;
                if (math.lengthsq(vPlanar) < 1e-10f) vPlanar = vFromTransform;

                // Optional: clamp impossible spikes to keep animation sane
                float speedCap = 20f;
                float mag = math.length(vPlanar);
                if (mag > speedCap) vPlanar *= speedCap / mag;

                // update prev-pos cache every frame so enemies animate smoothly
                prevPosRW.ValueRW.previousPosition    = posNow;
                prevPosRW.ValueRW.hasPreviousPosition = true;

                // Local axes
                float3 fwd = math.forward(localTransform.Rotation); fwd.y = 0f; fwd = math.normalizesafe(fwd, float3.zero);
                float3 right = math.cross(math.up(), fwd); right.y = 0f; right = math.normalizesafe(right, float3.zero);

                // Signed components
                float forwardSpeed = math.dot(vPlanar, fwd);     // +forward, −back
                float strafeSpeed = math.dot(vPlanar, right);   // +right, −left
                float planarSpeed = math.length(vPlanar);

                // Locomotion 0..1 with optional easing + hysteresis (NO early returns)
                float t = math.saturate(planarSpeed / math.max(0.001f, RUN_FULL_MPS));
                float locLinear = t;
                float locEase = math.lerp(locLinear, locLinear * locLinear * (3f - 2f * locLinear), math.saturate(LOC_EASE));

                // Use previous smoothed Locomotion as a cheap state to decide if we "were moving"
                float prevLoc = anim.GetFloat("Locomotion");
                bool wasMoving = prevLoc > 0.1f;

                // Hysteresis thresholds from one knob
                float enterThresh = IDLE_TO_MOVING_MPS;
                float exitThresh = IDLE_TO_MOVING_MPS * HYSTERESIS_RATIO;

                // Decide moving/idle with hysteresis
                bool moving = wasMoving ? (planarSpeed > exitThresh) : (planarSpeed > enterThresh);

                // Stronger damping near idle so the fade is gentle
                float nearIdle01 = math.saturate(locLinear * 2f); // 0..~0.5 is "near idle"
                float locDamp = math.lerp(LOCOMOTION_DAMP * LOCOMOTION_IDLE_DAMP_MULT, LOCOMOTION_DAMP, nearIdle01);
                float speedDamp = math.lerp(SPEED_DAMP * SPEED_IDLE_DAMP_MULT, SPEED_DAMP, nearIdle01);

                // Apply Locomotion (0 when idle)
                float locTarget = moving ? locEase : 0f;
                anim.SetFloat("Locomotion", locTarget, locDamp, delta);

                // Directional inputs (zeroed when idle so the tree sits at Idle node)
                float forwardNorm = math.clamp(forwardSpeed / RUN_FULL_MPS, -1f, 1f);
                float strafeNorm = math.clamp(strafeSpeed / RUN_FULL_MPS, -1f, 1f);
                anim.SetFloat("Forward", moving ? forwardNorm : 0f, locDamp, delta);
                anim.SetFloat("Strafe", moving ? strafeNorm : 0f, locDamp, delta);

                // Playback rate (Speed multiplier) — movement only
                float absF = math.abs(forwardSpeed);
                float absS = math.abs(strafeSpeed);
                float wSum = math.max(EPS, absF + absS);

                float baseClipMps =
                    (absF * (forwardSpeed >= 0f ? CLIP_FWD_MPS : CLIP_BACK_MPS) +
                     absS * CLIP_STRAFE_MPS) / wSum;

                // Convert world m/s to clip playrate so feet track distance
                float playRate = planarSpeed / math.max(0.5f, baseClipMps);
                float animSpeed = math.clamp(playRate, MIN_MOVE_PLAYRATE, MAX_MOVE_PLAYRATE);

                // Idle plays authored rate (1f); movement uses floored/capped rate
                anim.SetFloat("Speed", moving ? animSpeed : 1f, speedDamp, delta);

                // Keep visual synced
                animatorReference.Value.transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation);
            }
        }

        // Attack triggers
        foreach (var (animRef, attackStateRO, cacheRW) in
                 SystemAPI.Query<UnitAnimatorReference, RefRO<AttackAnimationState>, RefRW<AttackAnimClientCache>>())
        {
            if (attackStateRO.ValueRO.attackTick != cacheRW.ValueRO.lastSeenAttackTick)
            {
                animRef.Value.SetTrigger("Attack");
                cacheRW.ValueRW.lastSeenAttackTick = attackStateRO.ValueRO.attackTick;
            }

            if (attackStateRO.ValueRO.attackCancelTick != cacheRW.ValueRO.lastSeenCancelTick)
            {
                animRef.Value.ResetTrigger("Attack");
                animRef.Value.CrossFade("Locomotion", 0.05f, 0, 0f);
                cacheRW.ValueRW.lastSeenCancelTick = attackStateRO.ValueRO.attackCancelTick;
            }
        }

        // Wood-gathering triggers
        foreach (var (animRef, woodStateRO, cacheRW) in
                 SystemAPI.Query<UnitAnimatorReference, RefRO<GatheringWoodState>, RefRW<WoodAnimClientCache>>())
        {
            // Start chopping
            if (woodStateRO.ValueRO.woodStartTick != cacheRW.ValueRO.lastSeenStartTick)
            {
                animRef.Value.SetTrigger(WOOD_TRIGGER_NAME);
                cacheRW.ValueRW.lastSeenStartTick = woodStateRO.ValueRO.woodStartTick;
            }

            // Cancel/stop chopping
            if (woodStateRO.ValueRO.woodCancelTick != cacheRW.ValueRO.lastSeenCancelTick)
            {
                animRef.Value.ResetTrigger(WOOD_TRIGGER_NAME);
                animRef.Value.CrossFade("Locomotion", 0.05f, 0, 0f);
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

    // NEW: sampling window for robust planar velocity on interpolated ghosts
    public float3 samplePosition;
    public double sampleTime;
}

public struct WoodAnimClientCache : IComponentData
{
    public uint lastSeenStartTick;
    public uint lastSeenCancelTick;
}