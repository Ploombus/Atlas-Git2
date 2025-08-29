using Unity.Entities;
using UnityEngine;
using Managers;
using Unity.NetCode;
using UnityEngine.Rendering; // added for ShadowCastingMode

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(UnitAnimateSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct HealthVisualSystem : ISystem
{
    static Material sUnlitMat;
    static MaterialPropertyBlock sMpb;
    static readonly int sBaseColorId = Shader.PropertyToID("_BaseColor");

    static void EnsureMat()
    {
        if (sUnlitMat != null) return;

        // URP unlit is safe in builds; if this returns null, include the shader (note below).
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogError("URP/Unlit shader not found. Add it to Graphics > Always Included Shaders or reference a material asset.");
            return;
        }
        sUnlitMat = new Material(shader);
        sMpb = new MaterialPropertyBlock();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()))
            return;

        var entityManager = state.EntityManager;
        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // PASS 1: create indicator once per unit (from instantiated GO)
        foreach (var (animRef, entity) in
                 SystemAPI.Query<UnitAnimatorReference>().WithEntityAccess())
        {
            if (entityManager.HasComponent<UnitHealthIndicator>(entity))
                continue;

            if (animRef.Value == null) continue;

            var root = new GameObject("HealthIndicator");
            root.transform.SetParent(animRef.Value.transform, false);
            root.transform.localPosition = new Vector3(0f, 2.4f, 0f);
            root.AddComponent<Billboard>();

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "HI_Background";
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
            var bgRenderer = bg.GetComponent<MeshRenderer>();
            bgRenderer.material.color = Color.white;

            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "HI_Fill";
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            fill.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
            var fillRenderer = fill.GetComponent<MeshRenderer>();
            fillRenderer.material.color = Color.green;

            // >>> Only change: disable shadows on both quads
            bgRenderer.shadowCastingMode = ShadowCastingMode.Off;
            bgRenderer.receiveShadows = false;
            fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fillRenderer.receiveShadows = false;
            // <<<

            EnsureMat();
            if (sUnlitMat != null)
            {
                bgRenderer.sharedMaterial = sUnlitMat;
                fillRenderer.sharedMaterial = sUnlitMat;

                sMpb.Clear();
                sMpb.SetColor(sBaseColorId, Color.white);
                bgRenderer.SetPropertyBlock(sMpb);

                sMpb.Clear();
                sMpb.SetColor(sBaseColorId, Color.green);
                fillRenderer.SetPropertyBlock(sMpb);
            }

            // start hidden (we'll decide below)
            bgRenderer.enabled = false;
            fillRenderer.enabled = false;

            buffer.AddComponent(entity, new UnitHealthIndicator
            {
                backgroundRenderer = bgRenderer,
                fillRenderer = fillRenderer
            });
        }

        // PASS 2: color + visibility (selected OR damaged), Dead -> hidden
        foreach (var (unit, health, entity) in
                 SystemAPI.Query<Unit, RefRO<HealthState>>().WithEntityAccess())
        {
            if (!entityManager.HasComponent<UnitHealthIndicator>(entity))
                continue;

            var healthState = health.ValueRO;
            var stage = entityManager.HasComponent<GhostOwnerIsLocal>(entity)
                ? HealthStageUtil.ApplyDelta(
                    healthState.currentStage, healthState.healthChange, HealthStage.Dead, HealthStage.Healthy)
                : healthState.currentStage;

            var color = stage switch
            {
                HealthStage.Healthy => new Color(0.20f, 0.85f, 0.20f),
                HealthStage.Grazed => new Color(0.75f, 0.85f, 0.20f),
                HealthStage.Wounded => new Color(0.95f, 0.55f, 0.15f),
                HealthStage.Critical => new Color(0.90f, 0.20f, 0.20f),
                HealthStage.Dead => new Color(0.40f, 0.40f, 0.40f),
                _ => Color.magenta
            };

            var indicator = entityManager.GetComponentObject<UnitHealthIndicator>(entity);
            if (indicator.fillRenderer != null && sUnlitMat != null)
            {
                sMpb.Clear();
                sMpb.SetColor(sBaseColorId, color);
                indicator.fillRenderer.SetPropertyBlock(sMpb);
            }

            bool isSelected = SystemAPI.HasComponent<Selected>(entity)
                              && SystemAPI.IsComponentEnabled<Selected>(entity);
            bool isDamaged = stage != HealthStage.Healthy && stage != HealthStage.Dead;

            bool visible = (stage != HealthStage.Dead) && (isSelected || isDamaged);

            if (indicator.backgroundRenderer) indicator.backgroundRenderer.enabled = visible;
            if (indicator.fillRenderer) indicator.fillRenderer.enabled = visible;
        }

        // PASS 3 (defensive): if HealthState is gone (despawn), ensure hidden
        foreach (var (indicator, entity) in
                 SystemAPI.Query<UnitHealthIndicator>().WithNone<HealthState>().WithEntityAccess())
        {
            if (indicator.backgroundRenderer) indicator.backgroundRenderer.enabled = false;
            if (indicator.fillRenderer) indicator.fillRenderer.enabled = false;
        }

        buffer.Playback(entityManager);
        buffer.Dispose();
    }
}