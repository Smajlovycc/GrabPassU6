using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

//Original code from Refsa - git link https://gist.github.com/Refsa/54da34a9e2fc8e45472286572216ad17
//Upgraded to Unity 6000.0.33f1 - URP 17.0.3 by Tarik Smajlovic - https://github.com/Smajlovycc

public class GrabScreenFeatureU6 : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public string TextureName = "_GrabPassTransparent";
        public LayerMask LayerMask;
    }

    class GrabPass : ScriptableRenderPass
    {
        RTHandle tempColorTargetNew;
        Settings settings;

        RenderTargetIdentifier cameraTarget;

        public GrabPass(Settings s)
        {
            settings = s;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            tempColorTargetNew = RTHandles.Alloc(settings.TextureName, name: settings.TextureName);
        }

        public void Setup(RenderTargetIdentifier cameraTarget)
        {
            this.cameraTarget = cameraTarget;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            cmd.GetTemporaryRT(Shader.PropertyToID(tempColorTargetNew.name), cameraTextureDescriptor);
            cmd.SetGlobalTexture(settings.TextureName, tempColorTargetNew.nameID);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            cmd.Blit(cameraTarget, tempColorTargetNew.nameID);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(Shader.PropertyToID(tempColorTargetNew.name));
        }
    }

    class RenderPass : ScriptableRenderPass
    {
        Settings settings;
        List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();

        FilteringSettings m_FilteringSettings;
        RenderStateBlock m_RenderStateBlock;

        public RenderPass(Settings settings)
        {
            this.settings = settings;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents + 1;

            m_ShaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
            m_ShaderTagIdList.Add(new ShaderTagId("UniversalForward"));
            m_ShaderTagIdList.Add(new ShaderTagId("LightweightForward"));

            m_FilteringSettings = new FilteringSettings(RenderQueueRange.all, settings.LayerMask);
            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            DrawingSettings drawSettings;
            drawSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, SortingCriteria.CommonTransparent);
            context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings, ref m_RenderStateBlock);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    GrabPass grabPass;
    RenderPass renderPass;
    [SerializeField] Settings settings;

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
}