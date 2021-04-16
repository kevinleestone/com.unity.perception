using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Simulation;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

#if HDRP_PRESENT
using UnityEngine.Rendering.HighDefinition;
#endif

namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    /// Depth image from render z-buffer. saved inside 1 channel PNG 16-bits where units are mm and 0 represents
    /// invalid/unknown depth
    /// </summary>
    [Serializable]
    public sealed class DepthLabeler : CameraLabeler, IOverlayPanelProvider
    {
        ///<inheritdoc/>
        public override string description
        {
            get => "Depth labeller";
            protected set {}
        }

        const string k_DepthDirectory = "Depth";
        const string k_DepthFilePrefix = "depth_";
        internal string depthDirectory;

        /// <summary>
        /// The id to associate with depth annotations in the dataset.
        /// </summary>
        [Tooltip("The id to associate with depth annotations in the dataset.")]
        public string annotationId = "12f94d8d-5425-4deb-9b21-0123456789ab";

        /// <summary>
        /// Event information for <see cref="DepthLabeler.imageReadback"/>
        /// </summary>
        public struct ImageReadbackEventArgs
        {
            /// <summary>
            /// The <see cref="Time.frameCount"/> on which the image was rendered. This may be multiple frames in the past.
            /// </summary>
            public int frameCount;
            /// <summary>
            /// Color pixel data.
            /// </summary>
            public NativeArray<Color32> data;
            /// <summary>
            /// The source image texture.
            /// </summary>
            public RenderTexture sourceTexture;
        }

        /// <summary>
        /// Event which is called each frame a depth image is read back from the GPU.
        /// </summary>
        public event Action<ImageReadbackEventArgs> imageReadback;

        /// <summary>
        /// The RenderTexture on which depth images are drawn. Will be resized on startup to match
        /// the camera resolution.
        /// </summary>
        public RenderTexture targetTexture => m_TargetTextureOverride;

        /// <inheritdoc cref="IOverlayPanelProvider"/>
        public Texture overlayImage=> targetTexture;

        /// <inheritdoc cref="IOverlayPanelProvider"/>
        public string label => "Depth";

        [Tooltip("(Optional) The RenderTexture on which depth images will be drawn. Will be reformatted on startup.")]
        [SerializeField]
        RenderTexture m_TargetTextureOverride;

        AnnotationDefinition m_DepthAnnotationDefinition;
        RenderTextureReader<Color32> m_DepthTextureReader;

#if HDRP_PRESENT
        DepthPass m_DepthPass;
        LensDistortionPass m_LensDistortionPass;
    #elif URP_PRESENT
        DepthUrpPass m_DepthPass;
        LensDistortionUrpPass m_LensDistortionPass;
    #endif

        Dictionary<int, AsyncAnnotation> m_AsyncAnnotations;

        /// <summary>
        /// Creates a new DepthLabeler. Be sure to assign <see cref="labelConfig"/> before adding to a <see cref="PerceptionCamera"/>.
        /// </summary>
        public DepthLabeler() { }

        /// <summary>
        /// Creates a new DepthLabeler with the given <see cref="DepthLabelConfig"/>.
        /// </summary>
        /// <param name="labelConfig">The label config associating labels with colors.</param>
        /// <param name="targetTextureOverride">Override the target texture of the labeler. Will be reformatted on startup.</param>
        public DepthLabeler(RenderTexture targetTextureOverride = null)
        {
            m_TargetTextureOverride = targetTextureOverride;
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        struct DepthSpec
        {
            [UsedImplicitly]
            public string unused;
        }

        struct AsyncDepthWrite
        {
            public NativeArray<Color32> data;
            public int width;
            public int height;
            public string path;
        }

        /// <inheritdoc/>
        protected override bool supportsVisualization => false;

        /// <inheritdoc/>
        protected override void Setup()
        {
            var myCamera = perceptionCamera.GetComponent<Camera>();
            var camWidth = myCamera.pixelWidth;
            var camHeight = myCamera.pixelHeight;
            myCamera.depthTextureMode = myCamera.depthTextureMode | DepthTextureMode.Depth;

            m_AsyncAnnotations = new Dictionary<int, AsyncAnnotation>();

            if (targetTexture != null)
            {
                if (targetTexture.sRGB)
                {
                    Debug.LogError("targetTexture supplied to DepthLabeler must be in Linear mode. Disabling labeler.");
                    enabled = false;
                }
                var renderTextureDescriptor = new RenderTextureDescriptor(camWidth, camHeight, GraphicsFormat.R8G8B8A8_UNorm, 8);
                targetTexture.descriptor = renderTextureDescriptor;
            }
            else
                m_TargetTextureOverride = new RenderTexture(camWidth, camHeight, 8, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            targetTexture.Create();
            targetTexture.name = "Labeling";
            depthDirectory = k_DepthDirectory + Guid.NewGuid();

#if HDRP_PRESENT
            // TODO: Add HDRP support
            // var gameObject = perceptionCamera.gameObject;
            // var customPassVolume = gameObject.GetComponent<CustomPassVolume>() ?? gameObject.AddComponent<CustomPassVolume>();
            // customPassVolume.injectionPoint = CustomPassInjectionPoint.BeforeRendering;
            // customPassVolume.isGlobal = true;
            // m_DepthPass = new DepthPass(myCamera, targetTexture)
            // {
            //     name = "Labeling Pass"
            // };
            // customPassVolume.customPasses.Add(m_DepthPass);

            // m_LensDistortionPass = new LensDistortionPass(myCamera, targetTexture)
            // {
            //     name = "Lens Distortion Pass"
            // };
            // customPassVolume.customPasses.Add(m_LensDistortionPass);
#elif URP_PRESENT
            // Semantic Segmentation
            m_DepthPass = new DepthUrpPass(myCamera, targetTexture);
            perceptionCamera.AddScriptableRenderPass(m_DepthPass);

            // TODO: Add lens distortion support
            // Lens Distortion
            //m_LensDistortionPass = new LensDistortionUrpPass(myCamera, targetTexture);
            //perceptionCamera.AddScriptableRenderPass(m_LensDistortionPass);
#endif

            m_DepthAnnotationDefinition = DatasetCapture.RegisterAnnotationDefinition(
                "depth",
                "depth z-buffer image",
                "PNG",
                id: Guid.Parse(annotationId));

            m_DepthTextureReader = new RenderTextureReader<Color32>(targetTexture);
            visualizationEnabled = supportsVisualization;
        }

        void OnDepthImageRead(int frameCount, NativeArray<Color32> data)
        {
            if (!m_AsyncAnnotations.TryGetValue(frameCount, out var annotation))
                return;

            var datasetRelativePath = $"{depthDirectory}/{k_DepthFilePrefix}{frameCount}.png";
            var localPath = $"{Manager.Instance.GetDirectoryFor(depthDirectory)}/{k_DepthFilePrefix}{frameCount}.png";

            annotation.ReportFile(datasetRelativePath);

            var asyncRequest = Manager.Instance.CreateRequest<AsyncRequest<AsyncDepthWrite>>();

            imageReadback?.Invoke(new ImageReadbackEventArgs
            {
                data = data,
                frameCount = frameCount,
                sourceTexture = targetTexture
            });
            asyncRequest.data = new AsyncDepthWrite
            {
                data = new NativeArray<Color32>(data, Allocator.Persistent),
                width = targetTexture.width,
                height = targetTexture.height,
                path = localPath
            };
            asyncRequest.Enqueue((r) =>
            {
                Profiler.BeginSample("Encode");
                var pngBytes = ImageConversion.EncodeArrayToPNG(r.data.data.ToArray(), GraphicsFormat.R8G8B8A8_UNorm, (uint)r.data.width, (uint)r.data.height);
                Profiler.EndSample();
                Profiler.BeginSample("WritePng");
                File.WriteAllBytes(r.data.path, pngBytes);
                Manager.Instance.ConsumerFileProduced(r.data.path);
                Profiler.EndSample();
                r.data.data.Dispose();
                return AsyncRequest.Result.Completed;
            });
            asyncRequest.Execute();
        }

        /// <inheritdoc/>
        protected override void OnEndRendering(ScriptableRenderContext scriptableRenderContext)
        {
            m_AsyncAnnotations[Time.frameCount] = perceptionCamera.SensorHandle.ReportAnnotationAsync(m_DepthAnnotationDefinition);
            m_DepthTextureReader.Capture(scriptableRenderContext,
                (frameCount, data, renderTexture) => OnDepthImageRead(frameCount, data));

        }

        /// <inheritdoc/>
        protected override void Cleanup()
        {
            m_DepthTextureReader?.WaitForAllImages();
            m_DepthTextureReader?.Dispose();
            m_DepthTextureReader = null;

            if (m_TargetTextureOverride != null)
                m_TargetTextureOverride.Release();

            m_TargetTextureOverride = null;
        }
    }
}
