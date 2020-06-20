using System;
using System.Diagnostics;
using System.IO;
using Stride.Assets.Presentation.AssetEditors.EntityHierarchyEditor.Game;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;

namespace Stride.Assets.Presentation.SceneEditor
{
    /// <summary>
    /// Used by game studio to indicate selected object.
    /// Draws x-ray wireframe over mesh
    /// </summary>
    public class SelectionWireframeRenderFeature : SubRenderFeature
    {
        readonly string ShaderName = "SelectionWireframeShader";
        readonly float LineWidth = 3.0f;
        readonly Color ColorOccludedLines = new Color((Vector3)Color.LightYellow, 0.02f);
        readonly Color ColorNonOccludedLines = new Color((Vector3)Color.Yellow, 0.3f);

        private EffectInstance shader;

        private MutablePipelineState pipelineState;

        private IEditorGameEntitySelectionService selectionService;
        
        private readonly Stopwatch clockSelection = new Stopwatch();

        protected override void InitializeCore()
        {
            base.InitializeCore();

#if DEBUG
            // copy shader file to nuget package folder
            string nugetFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $@".nuget\packages\stride.rendering\{StrideConfig.LatestPackageVersion}\stride\Assets\{ShaderName}.sdsl");
            if (!File.Exists(nugetFilePath))
            {
                string dir = Directory.GetCurrentDirectory();
                if (dir.Contains(@"Stride.GameStudio\bin\Debug\net472"))
                {
                    dir = dir.Replace(@"Stride.GameStudio\bin\Debug\net472", "");
                    string shaderFileName = Path.Combine(dir, $@"Stride.Assets.Presentation\Shaders\{ShaderName}.sdsl");
                    File.Copy(shaderFileName, nugetFilePath);
                }
                else
                {
                    throw new Exception("Cannot copy shader file to nuget package folder");
                }
            }
#else
#error This is debug code. You have to remove it in release build
#endif

            var effect = RenderSystem.EffectSystem.LoadEffect(ShaderName).WaitForResult();
            shader = new EffectInstance(effect);

            // create the pipeline state and set properties that won't change
            pipelineState = new MutablePipelineState(Context.GraphicsDevice);
            pipelineState.State.SetDefaults();
            pipelineState.State.InputElements = VertexPositionNormalTexture.Layout.CreateInputElements();
            pipelineState.State.BlendState = BlendStates.NonPremultiplied;
            pipelineState.State.RasterizerState.CullMode = CullMode.None;
        }

        public void RegisterSelectionService(IEditorGameEntitySelectionService selectionService)
        {
            if (selectionService == null) throw new ArgumentNullException(nameof(selectionService));

            this.selectionService = selectionService;
            selectionService.SelectionUpdated += SelectionUpdated;
        }

        public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage)
        {
            float blendValue = (selectionService?.DisplaySelectionMask ?? false) 
                ? 1.0f 
                : MathUtil.Clamp((1.0f - (float)clockSelection.Elapsed.TotalSeconds), 0.0f, 1.0f);

            shader.UpdateEffect(context.GraphicsDevice);

            foreach (var renderNode in renderViewStage.SortedRenderNodes)
            {
                var renderMesh = renderNode.RenderObject as RenderMesh;
                if (renderMesh == null)
                {
                    continue;
                }

                MeshDraw drawData = renderMesh.ActiveMeshDraw;

                // bind VB
                for (int slot = 0; slot < drawData.VertexBuffers.Length; slot++)
                {
                    var vertexBuffer = drawData.VertexBuffers[slot];
                    context.CommandList.SetVertexBuffer(slot, vertexBuffer.Buffer, vertexBuffer.Offset, vertexBuffer.Stride);
                }

                // set shader parameters
                shader.Parameters.Set(SelectionWireframeShaderKeys.WorldViewProjection, renderMesh.World * renderView.ViewProjection); // matrix
                shader.Parameters.Set(SelectionWireframeShaderKeys.WorldScale, new Vector3(1.0001f)); // increase scale to avoid z-fight
                shader.Parameters.Set(SelectionWireframeShaderKeys.Viewport, new Vector4(context.RenderContext.RenderView.ViewSize, 0, 0));
                shader.Parameters.Set(SelectionWireframeShaderKeys.LineWidth, LineWidth);

                // prepare pipeline state
                pipelineState.State.RootSignature = shader.RootSignature;
                pipelineState.State.EffectBytecode = shader.Effect.Bytecode;
                pipelineState.State.PrimitiveType = drawData.PrimitiveType;

                Draw(context, drawData, GetColor(ColorOccludedLines, blendValue), DepthStencilStates.None);           // occluded
                Draw(context, drawData, GetColor(ColorNonOccludedLines, blendValue), DepthStencilStates.DepthRead);   // non-occluded
            }
        }

        private Vector4 GetColor(Color color, float alpha)
        {
            return new Vector4((Vector3)color, color.A / 255.0f * alpha);
        }

        private void Draw(RenderDrawContext context, MeshDraw drawData, Vector4 color, DepthStencilStateDescription depthStencilState)
        {
            pipelineState.State.DepthStencilState = depthStencilState;
            pipelineState.State.Output.CaptureState(context.CommandList);
            pipelineState.Update();

            context.CommandList.SetIndexBuffer(drawData.IndexBuffer.Buffer, drawData.IndexBuffer.Offset, drawData.IndexBuffer.Is32Bit);
            context.CommandList.SetPipelineState(pipelineState.CurrentState);
            
            shader.Parameters.Set(SelectionWireframeShaderKeys.LineColor, color);
            shader.Apply(context.GraphicsContext);

            if (drawData.IndexBuffer != null)
            {
                context.CommandList.DrawIndexed(drawData.DrawCount, drawData.StartLocation);
            }
            else
            {
                context.CommandList.Draw(drawData.DrawCount, drawData.StartLocation);
            }
        }

        private void SelectionUpdated(object sender, EntitySelectionEventArgs e)
        {
            clockSelection.Restart();
        }
    }
}
