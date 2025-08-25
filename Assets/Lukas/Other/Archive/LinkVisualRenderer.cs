/*using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class LinkVisualRenderer : MonoBehaviour
{
    // ===== Debug knobs =====
    public static bool UseInstancing = false;          // flip on after you SEE lines
    public static bool ShowCenterDebugLine = true;     // draws a short line under crosshair
    public static float DebugGroundY = 0.0f;           // y-plane for debug line (try 0 first)
    public static float DebugLineLength = 3f;          // meters

    // Render layer (camera must include this layer)
    public static int RenderLayer = 0;

    // Shared resources
    static Mesh sQuad;
    static Material sMat;
    static readonly List<Matrix4x4> sMatrices = new(512);
    static readonly List<Matrix4x4> sMatricesToRender = new(512);
    static bool sInit;

    static LinkVisualRenderer sInstance;
    public static void EnsureInstance()
    {
        if (sInstance != null) return;
        var go = new GameObject("~LinkVisualRenderer");
        go.hideFlags = HideFlags.HideAndDontSave;
        sInstance = go.AddComponent<LinkVisualRenderer>();
        DontDestroyOnLoad(go);
    }

    // ECS calls these every frame
    public static void BeginFrame()
    {
        EnsureInstance();
        EnsureResources();
        sMatrices.Clear();
    }
    public static void AddMatrix(Matrix4x4 m) => sMatrices.Add(m);

    void OnEnable()
    {
        EnsureResources();
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }
    void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (!sInit || sQuad == null || sMat == null) return;
        //if (((1 << RenderLayer) & cam.cullingMask) == 0) return; // camera doesn't render this layer

        // Copy matrices accumulated by ECS this frame
        sMatricesToRender.Clear();
        sMatricesToRender.AddRange(sMatrices);

        // Optional center-screen debug line to prove render path
        if (ShowCenterDebugLine)
        {
            if (TryRayToPlane(cam, new Vector2(0.5f, 0.5f), DebugGroundY, out var p))
            {
                var a = p + cam.transform.right * (-DebugLineLength * 0.5f);
                var b = p + cam.transform.right * ( DebugLineLength * 0.5f);
                sMatricesToRender.Add(BuildQuadMatrix(a, b, 0.03f));
            }
        }

        if (sMatricesToRender.Count == 0) return;

        // URP-friendly: submit via CommandBuffer through the ScriptableRenderContext
        var cmd = CommandBufferPool.Get("LinkVisualRenderer");
        try
        {
            cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);

            // Set color on both common property names (URP Unlit uses _BaseColor)
            sMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.9f));
            sMat.SetColor("_Color",     new Color(1f, 1f, 1f, 0.9f));
            

            if (UseInstancing && SystemInfo.supportsInstancing && sMat.enableInstancing)
            {
                const int MaxBatch = 1023;
                int i = 0;
                while (i < sMatricesToRender.Count)
                {
                    int count = Mathf.Min(MaxBatch, sMatricesToRender.Count - i);
                    // CommandBuffer needs an array
                    var tmp = new Matrix4x4[count];
                    sMatricesToRender.CopyTo(i, tmp, 0, count);
                    cmd.DrawMeshInstanced(sQuad, 0, sMat, 0, tmp, count, null);
                    i += count;
                }
            }
            else
            {
                for (int i = 0; i < sMatricesToRender.Count; i++)
                    cmd.DrawMesh(sQuad, sMatricesToRender[i], sMat, 0, 0, null);
            }

            ctx.ExecuteCommandBuffer(cmd);
        }
        finally
        {
            CommandBufferPool.Release(cmd);
        }
    }

    static void EnsureResources()
    {
        if (sInit) return;

        // Thin quad on XZ plane, 1 unit long along +X
        sQuad = new Mesh { name = "LinkLineQuad" };
        sQuad.SetVertices(new List<Vector3>
        {
            new(-0.5f, 0f, -0.5f), new(0.5f, 0f, -0.5f),
            new( 0.5f, 0f,  0.5f), new(-0.5f,0f,  0.5f),
        });
        sQuad.SetUVs(0, new List<Vector2> { new(0,0), new(1,0), new(1,1), new(0,1) });
        sQuad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);

        // URP Unlit if present; else built-in Unlit/Color; else Sprites/Default
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        sMat = new Material(sh) { name = "LinkLine_Mat" };
        sMat.renderQueue = (int)RenderQueue.Transparent; // draw late
        sMat.enableInstancing = true;

        sInit = true;
    }

    static bool TryRayToPlane(Camera cam, Vector2 viewport01, float planeY, out Vector3 hit)
    {
        var ray = cam.ViewportPointToRay(new Vector3(viewport01.x, viewport01.y, 0));
        if (Mathf.Abs(ray.direction.y) < 1e-5f) { hit = default; return false; }
        float t = (planeY - ray.origin.y) / ray.direction.y;
        if (t < 0) { hit = default; return false; }
        hit = ray.origin + ray.direction * t;
        return true;
    }

    // Same matrix builder we used in ECS (XZ quad, rotated so +X points along segment)
    public static Matrix4x4 BuildQuadMatrix(in Vector3 start, in Vector3 end, float thickness)
    {
        Vector3 dir = end - start;
        float len = dir.magnitude;
        if (len < 1e-6f) return Matrix4x4.identity;

        Vector3 mid = (start + end) * 0.5f;
        Vector3 dirXZ = new Vector3(dir.x, 0f, dir.z).normalized;
        if (!float.IsFinite(dirXZ.x)) dirXZ = Vector3.right;

        var rot = Quaternion.FromToRotation(Vector3.right, dirXZ);
        var scale = new Vector3(len, 1f, thickness);
        return Matrix4x4.TRS(mid, rot, scale);
    }
}
*/