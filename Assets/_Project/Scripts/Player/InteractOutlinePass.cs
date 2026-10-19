using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>
    /// HDRP Custom Pass outline: mask of the selected renderers -> silhouette line
    /// (shader Galilego/InteractOutline). The mask lives in HDRP's custom color buffer,
    /// so no RenderTextures of our own are needed. State is set by InteractionHighlighter.
    /// </summary>
    internal sealed class InteractOutlinePass : CustomPass
    {
        private static readonly int ColorId = Shader.PropertyToID("_InteractColor");
        private static readonly int ParamsId = Shader.PropertyToID("_InteractParams");

        public Material Material;
        public Renderer[] Renderers;
        public int[] SubMeshCounts;
        public int Count;
        public Vector4 Color;   // rgb — color*brightness (linear), a — visibility 0..1
        public Vector4 Params;  // line width px, glow radius px, glow strength, time

        protected override void Execute(CustomPassContext ctx)
        {
            if (Material == null || Renderers == null || Count == 0 || Color.w <= 0.001f)
            {
                return;
            }

            // 1) Mask: the object with the camera depth test (hidden parts are not outlined).
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.customColorBuffer.Value, ctx.cameraDepthBuffer,
                ClearFlag.Color, UnityEngine.Color.clear);
            for (int i = 0; i < Count; i++)
            {
                Renderer renderer = Renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                for (int s = 0; s < SubMeshCounts[i]; s++)
                {
                    ctx.cmd.DrawRenderer(renderer, Material, s, 0);
                }
            }

            // 2) Line and glow over the camera frame.
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            ctx.propertyBlock.SetVector(ColorId, Color);
            ctx.propertyBlock.SetVector(ParamsId, Params);
            CoreUtils.DrawFullScreen(ctx.cmd, Material, ctx.propertyBlock, shaderPassId: 1);
        }
    }
}
