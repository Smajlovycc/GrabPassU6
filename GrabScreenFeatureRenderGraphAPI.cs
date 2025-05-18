using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RendererUtils;

public class GrabScreenFeatureRenderGraphAPI : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        public string TextureName = "_GrabPassTransparent";
        public LayerMask LayerMask;
    }

    [SerializeField] private Settings settings;

    GrabPass grabPass;
    RenderPass renderPass;

    public override void Create()
    {
        grabPass = new GrabPass(settings);
        renderPass = new RenderPass(settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(grabPass);
        renderer.EnqueuePass(renderPass);
    }

    class GrabPass : ScriptableRenderPass
    {
        Settings settings;
        int globalTextureId;

        public GrabPass(Settings settings)
        {
            this.settings = settings;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            globalTextureId = Shader.PropertyToID(settings.TextureName);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            var descriptor = cameraData.cameraTargetDescriptor;

            TextureHandle destination = renderGraph.CreateTexture(new TextureDesc(descriptor)
            {
                name = settings.TextureName,
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                clearBuffer = true,
                clearColor = Color.clear
            });

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Grab Screen Pass", out var passData))
            {
                passData.source = source;
                passData.destination = destination;
                passData.globalTextureId = globalTextureId;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(destination, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(
                        ctx.cmd,
                        data.source,
                        new Vector4(1, 1, 0, 0),
                        0,
                        false
                    );
                    ctx.cmd.SetGlobalTexture(data.globalTextureId, data.destination);
                });
            }
        }

        private class PassData
        {
            public TextureHandle source;
            public TextureHandle destination;
            public int globalTextureId;
        }
    }

    class RenderPass : ScriptableRenderPass
    {
        Settings settings;
        List<ShaderTagId> shaderTagIds = new List<ShaderTagId>
    {
        new ShaderTagId("SRPDefaultUnlit"),
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("LightweightForward")
    };

        FilteringSettings filteringSettings;
        RenderStateBlock renderStateBlock;

        public RenderPass(Settings settings)
        {
            this.settings = settings;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents + 1;
            
            filteringSettings = new FilteringSettings(RenderQueueRange.all, settings.LayerMask);
            
            renderStateBlock = new RenderStateBlock(RenderStateMask.Depth)
            {
                depthState = new DepthState(writeEnabled: false, compareFunction: CompareFunction.LessEqual)
            };
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            TextureHandle depthTarget = resourceData.activeDepthTexture;

            var rendererListDesc = new RendererListDesc(
                shaderTagIds.ToArray(),
                renderingData.cullResults,
                cameraData.camera)
            {
                renderQueueRange = filteringSettings.renderQueueRange,
                sortingCriteria = SortingCriteria.CommonTransparent,
                layerMask = filteringSettings.layerMask,
                stateBlock = renderStateBlock
            };

            RendererListHandle rendererList = renderGraph.CreateRendererList(rendererListDesc);

            TextureHandle colorTarget = resourceData.activeColorTexture;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Render Objects Pass", out var passData))
            {
                passData.rendererList = rendererList;

                builder.SetRenderAttachment(colorTarget, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(depthTarget, AccessFlags.Read);

                builder.AllowPassCulling(false);
                builder.UseRendererList(rendererList);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    if (data.rendererList.IsValid())
                        ctx.cmd.DrawRendererList(data.rendererList);
                });
            }
        }

        private class PassData
        {
            public RendererListHandle rendererList;
        }
    }
}
