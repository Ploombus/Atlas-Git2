using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Transforms;
using Unity.NetCode;
using Managers;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule; // Unity 6+
#else
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

// ======================================================
// STYLE KNOBS
// ======================================================
static class LinkVisualStyle
{
    public static float MinDistance      = 0.4f;  // meters
    public static float VerticalOffsetY  = 0.01f;  // meters
    // Enemy (entity target resolved)
    public static float EnemyWidth       = 0.06f;  // meters
    public static float EnemyOpacity     = 0.20f;  // 0..1
    public static float EnemyDashLength  = 0.10f;  // meters; <=0 => solid
    public static float EnemyGapLength   = 0.10f;  // meters; <=0 => solid
    // Neutral/Other (ground/destination)
    public static float NeutralWidth     = 0.06f;  // meters
    public static float NeutralOpacity   = 0.20f;  // 0..1
    public static float NeutralDashLength= 0.00f;  // 0=solid
    public static float NeutralGapLength = 0.00f;  // 0=solid
    // Anchor the dash pattern at the target end (so it doesn't "move" near the target)
    public static bool EnemyAnchorAtTargetEnd   = true;
    public static bool NeutralAnchorAtTargetEnd = false;
}

// ======================================================
// STATIC DRAW COLLECTOR (ECS writes, pass consumes)
// ======================================================
static class LinkVisualDraw
{
    static Mesh sQuad;
    static Material sMat;
    static bool sInit;

    static readonly List<Matrix4x4> sEnemyMats   = new(1024);
    static readonly List<Matrix4x4> sNeutralMats = new(1024);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        sQuad = null; sMat = null; sInit = false;
        sEnemyMats.Clear(); sNeutralMats.Clear();
    }

    static void EnsureResources()
    {
        if (sInit) return;

        // XZ quad, 1m along +X, UP-facing (visible with backface culling)
        sQuad = new Mesh { name = "LinkLineQuad" };
        sQuad.SetVertices(new List<Vector3> {
            new(-0.5f,0f,-0.5f), new(0.5f,0f,-0.5f),
            new( 0.5f,0f, 0.5f), new(-0.5f,0f, 0.5f),
        });
        sQuad.SetUVs(0, new List<Vector2> { new(0,0), new(1,0), new(1,1), new(0,1) });
        sQuad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        sMat = new Material(sh) { name = "LinkLine_Mat", hideFlags = HideFlags.HideAndDontSave };
        sMat.renderQueue = (int)RenderQueue.Transparent;
        sMat.enableInstancing = false;

        // Transparent blending (so opacity works)
        if (sMat.HasProperty("_Surface")) sMat.SetFloat("_Surface", 1f);      // 0=Opaque, 1=Transparent
        if (sMat.HasProperty("_Blend"))   sMat.SetFloat("_Blend",   0f);      // 0=Alpha
        if (sMat.HasProperty("_SrcBlend")) sMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (sMat.HasProperty("_DstBlend")) sMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (sMat.HasProperty("_ZWrite"))   sMat.SetInt("_ZWrite",   0);       // don’t write depth for transparents
        if (sMat.HasProperty("_AlphaClip")) sMat.SetFloat("_AlphaClip", 0f);
        sMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        sMat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        sMat.DisableKeyword("_ALPHATEST_ON");

        if (sMat.HasProperty("_BaseColor")) sMat.SetColor("_BaseColor", Color.white);
        if (sMat.HasProperty("_Color"))     sMat.SetColor("_Color",     Color.white);
        if (sMat.HasProperty("_ZTest"))     sMat.SetInt  ("_ZTest",  (int)CompareFunction.LessEqual);
        if (sMat.HasProperty("_Cull"))      sMat.SetInt  ("_Cull",   (int)CullMode.Back);

        sInit = true;
    }

    public static void BeginFrame()
    {
        EnsureResources();
        sEnemyMats.Clear();
        sNeutralMats.Clear();
    }

    public static void AddEnemy(Matrix4x4 m)   => sEnemyMats.Add(m);
    public static void AddNeutral(Matrix4x4 m) => sNeutralMats.Add(m);

    // --- Render Graph path (no warnings) ---
    public static void DrawIntoRG(RasterCommandBuffer cmd, UniversalCameraData cameraData)
    {
        EnsureResources();

        // Enemy bucket
        SetMaterialAlpha(LinkVisualStyle.EnemyOpacity);
        for (int i = 0; i < sEnemyMats.Count; i++)
            cmd.DrawMesh(sQuad, sEnemyMats[i], sMat, 0, 0, null);

        // Neutral bucket
        SetMaterialAlpha(LinkVisualStyle.NeutralOpacity);
        for (int i = 0; i < sNeutralMats.Count; i++)
            cmd.DrawMesh(sQuad, sNeutralMats[i], sMat, 0, 0, null);
    }

#if LINKVISUAL_COMPAT
    // --- Compatibility path (Render Graph OFF). Compiles only if you define LINKVISUAL_COMPAT. ---
    public static void DrawCompat(ScriptableRenderContext ctx, ref RenderingData renderingData, ScriptableRenderer renderer)
    {
        EnsureResources();

        var cmd = CommandBufferPool.Get("LinkVisual");
        try
        {
#if UNITY_2022_1_OR_NEWER
            #pragma warning disable 0618
            var colorTarget = renderer.cameraColorTargetHandle;
            var depthTarget = renderer.cameraDepthTargetHandle;
            #pragma warning restore 0618
            CoreUtils.SetRenderTarget(cmd, colorTarget, depthTarget);
#else
            CoreUtils.SetRenderTarget(cmd, renderer.cameraColorTarget, renderer.cameraDepth);
#endif
            cmd.SetViewProjectionMatrices(renderingData.cameraData.GetViewMatrix(),
                                          renderingData.cameraData.GetProjectionMatrix());

            SetMaterialAlpha(LinkVisualStyle.EnemyOpacity);
            for (int i = 0; i < sEnemyMats.Count; i++)
                cmd.DrawMesh(sQuad, sEnemyMats[i], sMat, 0, 0, null);

            SetMaterialAlpha(LinkVisualStyle.NeutralOpacity);
            for (int i = 0; i < sNeutralMats.Count; i++)
                cmd.DrawMesh(sQuad, sNeutralMats[i], sMat, 0, 0, null);

            ctx.ExecuteCommandBuffer(cmd);
        }
        finally
        {
            CommandBufferPool.Release(cmd);
        }
    }
#endif

    static void SetMaterialAlpha(float alpha01)
    {
        alpha01 = Mathf.Clamp01(alpha01);
        var col = new Color(1f, 1f, 1f, alpha01);
        if (sMat.HasProperty("_BaseColor")) sMat.SetColor("_BaseColor", col);
        if (sMat.HasProperty("_Color"))     sMat.SetColor("_Color",     col);
    }

    public static Matrix4x4 BuildQuadMatrix(in Vector3 start, in Vector3 end, float thickness)
    {
        Vector3 dir = end - start;
        float len = dir.magnitude;
        if (len < 1e-6f) return Matrix4x4.identity;

        Vector3 mid = (start + end) * 0.5f;
        Vector3 dirXZ = new Vector3(dir.x, 0f, dir.z).normalized;
        if (!float.IsFinite(dirXZ.x)) dirXZ = Vector3.right;

        var rot = Quaternion.FromToRotation(Vector3.right, dirXZ);
        var scale = new Vector3(len, 1f, Mathf.Max(1e-4f, thickness));
        return Matrix4x4.TRS(mid, rot, scale);
    }

    public static void AddDashed(in Vector3 start, in Vector3 end, float thickness,
                                 float dashLen, float gapLen, bool enemyStyle)
    {
        // Solid if dash/gap disabled
        if (dashLen <= 0f || gapLen <= 0f)
        {
            var mSolid = BuildQuadMatrix(start, end, thickness);
            if (enemyStyle) AddEnemy(mSolid); else AddNeutral(mSolid);
            return;
        }

        // Decide anchor: keep pattern stable at the target end if requested
        bool anchorAtEnd = enemyStyle ? LinkVisualStyle.EnemyAnchorAtTargetEnd
                                      : LinkVisualStyle.NeutralAnchorAtTargetEnd;

        // Build from 'a' (anchor) toward 'b' (moving end) so dashes don't "crawl" at the anchor
        Vector3 a = anchorAtEnd ? end   : start;  // anchor = target end when true
        Vector3 b = anchorAtEnd ? start : end;    // moving end

        Vector3 d = b - a;
        float L = d.magnitude;
        if (L <= 1e-6f) return;

        Vector3 n = d / L;
        float period = dashLen + gapLen;
        if (period <= 1e-6f) period = dashLen;

        int full = Mathf.FloorToInt(L / period);
        float rem = L - full * period;

        float cursor = 0f;
        for (int i = 0; i < full; i++)
        {
            float a0 = cursor;
            float a1 = cursor + dashLen;
            var m = BuildQuadMatrix(a + n * a0, a + n * a1, thickness);
            if (enemyStyle) AddEnemy(m); else AddNeutral(m);
            cursor += period;
        }

        if (rem > 1e-6f)
        {
            float a0 = cursor;
            float a1 = cursor + Mathf.Min(dashLen, rem);
            var m = BuildQuadMatrix(a + n * a0, a + n * a1, thickness);
            if (enemyStyle) AddEnemy(m); else AddNeutral(m);
        }
    }

    public static void DisposeResources()
    {
        sInit = false;
        if (sMat  != null) { UnityEngine.Object.DestroyImmediate(sMat);  sMat  = null; }
        if (sQuad != null) { UnityEngine.Object.DestroyImmediate(sQuad); sQuad = null; }
        sEnemyMats.Clear();
        sNeutralMats.Clear();
    }
}



// ======================================================
// ECS SYSTEM (collects matrices each frame)
// ======================================================
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(UnitAnimateSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct LinkVisualSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()))
            return;

        var em = state.EntityManager;
        LinkVisualDraw.BeginFrame();

        foreach (var (ltw, e) in
                 SystemAPI.Query<RefRO<LocalToWorld>>()
                          .WithAll<Unit, GhostOwnerIsLocal>()
                          .WithEntityAccess())
        {
            if (!em.HasComponent<Selected>(e) || !em.IsComponentEnabled<Selected>(e))
                continue;

            // Resolve endpoint from UnitTargets (predicted) OR UnitTargetsNetcode (requests) OR dot.
            if (!TryResolveEndPointAny(em, e, out float3 end, out bool isEntityTarget))
                continue;

            float3 start = ltw.ValueRO.Position;
            start.y += LinkVisualStyle.VerticalOffsetY;
            end.y   += LinkVisualStyle.VerticalOffsetY;

            float3 delta = end - start;
            if (math.lengthsq(delta) < LinkVisualStyle.MinDistance * LinkVisualStyle.MinDistance)
                continue;

            float width      = isEntityTarget ? LinkVisualStyle.EnemyWidth      : LinkVisualStyle.NeutralWidth;
            float dashLength = isEntityTarget ? LinkVisualStyle.EnemyDashLength : LinkVisualStyle.NeutralDashLength;
            float gapLength  = isEntityTarget ? LinkVisualStyle.EnemyGapLength  : LinkVisualStyle.NeutralGapLength;

            var s = new Vector3(start.x, start.y, start.z);
            var t = new Vector3(end.x,   end.y,   end.z);
            LinkVisualDraw.AddDashed(s, t, width, dashLength, gapLength, isEntityTarget);
        }
    }

    // ---- Resolver that prevents (0,0,0) at startup and prefers predicted data ----
    static bool TryResolveEndPointAny(EntityManager em, Entity unit, out float3 end, out bool isEntityTarget)
    {
        static bool HasMeaningfulVector(float3 p)
            => math.all(math.isfinite(p)) && math.lengthsq(p) > 1e-6f;

        // (1) Predicted UnitTargets (client/local) – most up-to-date for follow
        if (em.HasComponent<UnitTargets>(unit))
        {
            var t = em.GetComponentData<UnitTargets>(unit);

            // Prefer entity target
            if (t.targetEntity != Entity.Null && em.Exists(t.targetEntity))
            {
                if (em.HasComponent<LocalTransform>(t.targetEntity))
                {
                    end = em.GetComponentData<LocalTransform>(t.targetEntity).Position; isEntityTarget = true; return true;
                }
                if (em.HasComponent<LocalToWorld>(t.targetEntity))
                {
                    end = em.GetComponentData<LocalToWorld>(t.targetEntity).Position; isEntityTarget = true; return true;
                }
            }
            // Predicted destination (only if meaningful)
            if (HasMeaningfulVector(t.destinationPosition))
            {
                end = t.destinationPosition; isEntityTarget = false; return true;
            }
        }

        // (2) Ghosted requests (netcode)
        if (em.HasComponent<UnitTargetsNetcode>(unit))
        {
            var ut = em.GetComponentData<UnitTargetsNetcode>(unit);

            if (ut.requestTargetEntity != Entity.Null && em.HasComponent<LocalToWorld>(ut.requestTargetEntity))
            {
                end = em.GetComponentData<LocalToWorld>(ut.requestTargetEntity).Position; isEntityTarget = true; return true;
            }

            // Only trust requested destination if an active request is flagged AND it's meaningful
            if (ut.requestActiveTargetSet && HasMeaningfulVector(ut.requestDestinationPosition))
            {
                end = ut.requestDestinationPosition; isEntityTarget = false; return true;
            }
        }

        // (3) Movement dot as last resort
        if (em.HasComponent<MovementDotRef>(unit))
        {
            var dotRef = em.GetComponentData<MovementDotRef>(unit);
            if (dotRef.Dot != Entity.Null && em.HasComponent<LocalToWorld>(dotRef.Dot))
            {
                end = em.GetComponentData<LocalToWorld>(dotRef.Dot).Position; isEntityTarget = false; return true;
            }
        }

        end = default; isEntityTarget = false; return false;
    }
}

sealed class LinkVisualDisposal : MonoBehaviour
{
    static bool sCreated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (sCreated) return;
        var existing = GameObject.Find("~LinkVisual_Disposal");
        if (existing != null) { sCreated = true; return; }

        var go = new GameObject("~LinkVisual_Disposal");
        go.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(go);
        go.AddComponent<LinkVisualDisposal>();
        sCreated = true;
    }

    void OnDisable() { LinkVisualDraw.DisposeResources(); }
    void OnDestroy() { LinkVisualDraw.DisposeResources(); }
}