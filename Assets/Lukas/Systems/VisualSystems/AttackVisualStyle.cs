using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#else
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

// =======================
// Style knobs
// =======================
static class AttackVisualStyle
{
    public static float VerticalOffsetY = 0.02f;
    public static float LineThickness   = 0.03f;  // meters

    public static float ArcSegmentLen   = 0.40f;  // ~meters per arc segment

    public static Color PlannedColor    = new Color(0.20f, 0.85f, 1f, 0.7f); // cyan-ish, faint
    public static Color ImpactColor     = new Color(1f, 0.35f, 0.15f, 0.9f); // orange-red, stronger
    public static float ImpactFlashTime = 1f;  // seconds to flash at impact
}

// =======================
// Static collector + drawer
// =======================
static class AttackVisualDraw
{
    static Mesh sQuad;
    static Material sMat;
    static bool sInit;

    // Per-draw property blocks for colors (no shared material mutation at runtime)
    static MaterialPropertyBlock sMPBPlanned;
    static MaterialPropertyBlock sMPBImpact;

    // Buckets of matrices (outline quads)
    static readonly List<Matrix4x4> sPlanned = new(4096);
    static readonly List<Matrix4x4> sImpact  = new(4096);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        sQuad = null; sMat = null; sInit = false;
        sMPBPlanned = null; sMPBImpact = null;
        sPlanned.Clear(); sImpact.Clear();
    }

    static void EnsureResources()
    {
        if (sInit) return;

        // Unit XZ quad, up-facing, 1x1
        sQuad = new Mesh { name = "AttackVisual_Quad" };
        sQuad.SetVertices(new List<Vector3> {
            new(-0.5f,0f,-0.5f), new(0.5f,0f,-0.5f),
            new( 0.5f,0f, 0.5f), new(-0.5f,0f, 0.5f),
        });
        sQuad.SetUVs(0, new List<Vector2> { new(0, 0), new(1, 0), new(1, 1), new(0, 1) });
        sQuad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        sMat = new Material(sh) { name = "AttackVisual_Mat", hideFlags = HideFlags.HideAndDontSave };
        sMat.renderQueue = (int)RenderQueue.Transparent;
        sMat.enableInstancing = false; // we're using per-draw matrices, not GPU instancing

        // Make it transparent, no Z writes; depth test LE; backface cull
        if (sMat.HasProperty("_Surface"))   sMat.SetFloat("_Surface", 1f); // 1 = Transparent in URP
        if (sMat.HasProperty("_Blend"))     sMat.SetFloat("_Blend", 0f);
        if (sMat.HasProperty("_SrcBlend"))  sMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (sMat.HasProperty("_DstBlend"))  sMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        if (sMat.HasProperty("_ZWrite"))    sMat.SetInt("_ZWrite", 0);
        if (sMat.HasProperty("_AlphaClip")) sMat.SetFloat("_AlphaClip", 0f);
        if (sMat.HasProperty("_ZTest"))     sMat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        if (sMat.HasProperty("_Cull"))      sMat.SetInt("_Cull", (int)CullMode.Back);

        // Setup per-draw color property blocks once
        sMPBPlanned = new MaterialPropertyBlock();
        sMPBImpact  = new MaterialPropertyBlock();

        // Support both common URP color property names
        sMPBPlanned.SetColor("_BaseColor", AttackVisualStyle.PlannedColor);
        sMPBPlanned.SetColor("_Color",     AttackVisualStyle.PlannedColor);

        sMPBImpact.SetColor("_BaseColor", AttackVisualStyle.ImpactColor);
        sMPBImpact.SetColor("_Color",     AttackVisualStyle.ImpactColor);

        sInit = true;
    }

    public static void BeginFrame()
    {
        EnsureResources();
        sPlanned.Clear();
        sImpact.Clear();
    }

    // Build a quad along (a->b), centered, thick on Z
    static Matrix4x4 BuildQuadMatrix(in Vector3 a, in Vector3 b, float thickness)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 1e-5f) return Matrix4x4.identity;

        Vector3 mid = (a + b) * 0.5f;
        Vector3 dir = new Vector3(d.x, 0f, d.z).normalized;
        if (!float.IsFinite(dir.x)) dir = Vector3.right;

        var rot   = Quaternion.FromToRotation(Vector3.right, dir);
        var scale = new Vector3(len, 1f, Mathf.Max(1e-4f, thickness));
        return Matrix4x4.TRS(mid, rot, scale);
    }

    // Add a cone outline: two radial edges + arc segments
    public static void AddConeOutline(Vector3 origin, float yawRad, float radius, float degrees, bool impact)
    {
        EnsureResources();

        if (radius <= 0f || degrees <= 0.5f) return;

        float y = origin.y + AttackVisualStyle.VerticalOffsetY;
        origin.y = y;

        float half = 0.5f * degrees * Mathf.Deg2Rad;

        // Endpoints on arc
        Vector3 dirC = new(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad)); // kept for clarity; not used directly
        Vector3 dirL = new(Mathf.Sin(yawRad - half), 0f, Mathf.Cos(yawRad - half));
        Vector3 dirR = new(Mathf.Sin(yawRad + half), 0f, Mathf.Cos(yawRad + half));

        Vector3 pL = origin + dirL * radius;
        Vector3 pR = origin + dirR * radius;

        // Radial edges
        var list = impact ? sImpact : sPlanned;
        list.Add(BuildQuadMatrix(origin, pL, AttackVisualStyle.LineThickness));
        list.Add(BuildQuadMatrix(origin, pR, AttackVisualStyle.LineThickness));

        // Arc subdivisions (target segment length)
        float arcLen = radius * (half * 2f);
        int segs = Mathf.Max(3, Mathf.CeilToInt(arcLen / Mathf.Max(0.05f, AttackVisualStyle.ArcSegmentLen)));
        float step = (half * 2f) / segs;

        float a0 = yawRad - half;
        for (int i = 0; i < segs; i++)
        {
            float a1 = a0 + step;
            Vector3 s = origin + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * radius;
            Vector3 t = origin + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * radius;
            list.Add(BuildQuadMatrix(s, t, AttackVisualStyle.LineThickness));
            a0 = a1;
        }
    }

    public static void DrawIntoRG(RasterCommandBuffer cmd, UniversalCameraData cameraData)
    {
        EnsureResources();

        // Draw PLANNED first (cyan), then IMPACT on top (orange).
        for (int i = 0; i < sPlanned.Count; i++)
            cmd.DrawMesh(sQuad, sPlanned[i], sMat, 0, 0, sMPBPlanned);

        for (int i = 0; i < sImpact.Count; i++)
            cmd.DrawMesh(sQuad, sImpact[i], sMat, 0, 0, sMPBImpact);
    }

    public static void DisposeResources()
    {
        sInit = false;
        if (sMat  != null) { Object.DestroyImmediate(sMat);  sMat  = null; }
        if (sQuad != null) { Object.DestroyImmediate(sQuad); sQuad = null; }
        sMPBPlanned = null;
        sMPBImpact  = null;
        sPlanned.Clear();
        sImpact.Clear();
    }
}
