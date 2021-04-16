#if URP_PRESENT
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Perception.GroundTruth
{
    class DepthUrpPass : ScriptableRenderPass
    {
        public DepthCrossPipelinePass m_DepthCrossPipelinePass;

        public DepthUrpPass(Camera camera, RenderTexture targetTexture)
        {
            m_DepthCrossPipelinePass = new DepthCrossPipelinePass(camera);
            ConfigureTarget(targetTexture, targetTexture.depthBuffer);
            m_DepthCrossPipelinePass.Setup();
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var commandBuffer = CommandBufferPool.Get(nameof(DepthUrpPass));
            m_DepthCrossPipelinePass.Execute(context, commandBuffer, renderingData.cameraData.camera, renderingData.cullResults);
            CommandBufferPool.Release(commandBuffer);
        }

        public void Cleanup()
        {
            m_DepthCrossPipelinePass.Cleanup();
        }
    }
}
#endif
