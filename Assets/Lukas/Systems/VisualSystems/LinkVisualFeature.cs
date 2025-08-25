using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule; // Unity 6+
#else
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

// ======================================================
// URP RENDERER FEATURE (Render Graph primary; compat optional)
// ======================================================
public class LinkVisualFeature : ScriptableRendererFeature
{
    class LinkVisualPass : ScriptableRenderPass
    {
        readonly ProfilingSampler _prof = new ProfilingSampler("LinkVisual");

        class PassData
        {
            public UniversalResourceData resourceData;
            public UniversalCameraData cameraData;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
            if (!Application.isPlaying) return; // skip teardown/editor

            var cameraData = frameContext.Get<UniversalCameraData>();
            if (cameraData.isPreviewCamera) return;

            var resourceData = frameContext.Get<UniversalResourceData>();

            using var builder = renderGraph.AddRasterRenderPass<PassData>("LinkVisual", out var passData, _prof);
            passData.resourceData = resourceData;
            passData.cameraData = cameraData;

            builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                LinkVisualDraw.DrawIntoRG(ctx.cmd, data.cameraData);
            });
        }

#if LINKVISUAL_COMPAT
        // Only compiled if you define LINKVISUAL_COMPAT (Render Graph disabled).
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!Application.isPlaying) return;
            if (renderingData.cameraData.isPreviewCamera) return;

            LinkVisualDraw.DrawCompat(context, ref renderingData, renderingData.cameraData.renderer);
        }
#endif
    }

    LinkVisualPass _pass;

    public override void Create()
    {
        _pass = new LinkVisualPass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_pass);
    }
}