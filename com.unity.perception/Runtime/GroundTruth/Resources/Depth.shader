Shader "Perception/SemanticSegmentation"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 ps4 xboxone vulkan metal switch

    //enable GPU instancing support
    #pragma multi_compile_instancing

    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "SRP" }

            Blend Off
            ZWrite On
            ZTest LEqual

            Cull Back

            CGPROGRAM

            #pragma vertex semanticSegmentationVertexStage
            #pragma fragment semanticSegmentationFragmentStage

            #include "UnityCG.cginc"

            sampler2D _CameraDepthTexture;

            struct in_vert
            {
                float4 vertex : POSITION;
            };

            struct vertexToFragment
            {
                float4 vertex : SV_POSITION;
            };

            vertexToFragment semanticSegmentationVertexStage (in_vert vertWorldSpace)
            {
                vertexToFragment vertScreenSpace;
                vertScreenSpace.vertex = UnityObjectToClipPos(vertWorldSpace.vertex);
                return vertScreenSpace;
            }

            fixed4 semanticSegmentationFragmentStage (vertexToFragment vertScreenSpace) : SV_Target
            {
                float depth = tex2D(_CameraDepthTexture, vertScreenSpace.uv).r;
                depth = Linear01Depth(depth);
                depth = depth * _ProjectionParams.z;
                return depth;
            }

            ENDCG
        }
    }
}
