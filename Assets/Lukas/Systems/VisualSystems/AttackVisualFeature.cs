using UnityEngine;
using UnityEngine.Rendering;              // <-- needed for ProfilingSampler
using UnityEngine.Rendering.Universal;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule; // Unity 6+
#else
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

public class AttackVisualFeature : ScriptableRendererFeature
{
    class Pass : ScriptableRenderPass
    {
        readonly ProfilingSampler _prof = new ProfilingSampler("AttackVisual");

        class PassData
        {
            public UniversalResourceData resourceData;
            public UniversalCameraData   cameraData;
        }

        // RenderGraph path (URP 14+/Unity 2022+; same as your LinkVisual)
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
            if (!Application.isPlaying) return;

            var cameraData = frameContext.Get<UniversalCameraData>();
            if (cameraData.isPreviewCamera) return;

            var resourceData = frameContext.Get<UniversalResourceData>();

            using var builder = renderGraph.AddRasterRenderPass<PassData>("AttackVisual", out var passData, _prof);
            passData.resourceData = resourceData;
            passData.cameraData   = cameraData;

            builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                AttackVisualDraw.DrawIntoRG(ctx.cmd, data.cameraData);
            });
        }

#if ATTACKVISUAL_COMPAT
        // Optional compat path if you ever turn RenderGraph off.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!Application.isPlaying) return;
            if (renderingData.cameraData.isPreviewCamera) return;

            // Very thin wrapper: reuse DrawIntoRG (it only needs a RasterCommandBuffer equivalent).
            // If you need a pure CommandBuffer path, mirror your LinkVisualCompat code.
            var cmd = CommandBufferPool.Get("AttackVisual");
            try
            {
                // Set render targets (URP 12-style)
#pragma warning disable 0618
                var color = renderingData.cameraData.renderer.cameraColorTargetHandle;
                var depth = renderingData.cameraData.renderer.cameraDepthTargetHandle;
#pragma warning restore 0618
                CoreUtils.SetRenderTarget(cmd, color, depth);

                // Fake a "cameraData" to pass along (we already have it here)
                // We can’t call DrawIntoRG directly without a RasterCommandBuffer; for a full compat,
                // create a small DrawCompat similar to your LinkVisualDraw.DrawCompat.
                // (Left out by default; enable ATTACKVISUAL_COMPAT only if you implement that.)
            }
            finally
            {
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
#endif
    }

    Pass _pass;

    public override void Create()
    {
        _pass = new Pass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_pass);
    }
}