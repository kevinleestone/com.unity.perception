using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    /// Custom Pass which renders labeled images where each object labeled with a Labeling component is drawn with the
    /// value specified by the given LabelingConfiguration.
    /// </summary>
    class DepthCrossPipelinePass : GroundTruthCrossPipelinePass
    {
        const string k_ShaderName = "Perception/Depth";

        static int s_LastFrameExecuted = -1;

        // NOTICE: Serialize the shader so that the shader asset is included in player builds when the DepthPass is used.
        // Currently commented out and shaders moved to Resources folder due to serialization crashes when it is enabled.
        // See https://fogbugz.unity3d.com/f/cases/1187378/
        // [SerializeField]
        Shader m_DepthShader;
        Material m_OverrideMaterial;

        public DepthCrossPipelinePass(Camera targetCamera) : base(targetCamera)
        {
        }

        public override void Setup()
        {
            base.Setup();
            m_DepthShader = Shader.Find(k_ShaderName);

            var shaderVariantCollection = new ShaderVariantCollection();

            if (shaderVariantCollection != null)
            {
                shaderVariantCollection.Add(
                    new ShaderVariantCollection.ShaderVariant(m_DepthShader, PassType.ScriptableRenderPipeline));
            }

            m_OverrideMaterial = new Material(m_DepthShader);

            shaderVariantCollection.WarmUp();
        }

        protected override void ExecutePass(
            ScriptableRenderContext renderContext, CommandBuffer cmd, Camera camera, CullingResults cullingResult)
        {
            if (s_LastFrameExecuted == Time.frameCount)
                return;

            s_LastFrameExecuted = Time.frameCount;
            var renderList = CreateRendererListDesc(camera, cullingResult, "FirstPass", 0, m_OverrideMaterial, -1);
            //cmd.ClearRenderTarget(true, true, m_LabelConfig.skyColor);
            DrawRendererList(renderContext, cmd, RendererList.Create(renderList));
        }

        public override void SetupMaterialProperties(
            MaterialPropertyBlock mpb, Renderer meshRenderer, Labeling labeling, uint instanceId) { }

        public override void ClearMaterialProperties(
            MaterialPropertyBlock mpb, Renderer renderer, Labeling labeling, uint instanceId) { }
    }
}
