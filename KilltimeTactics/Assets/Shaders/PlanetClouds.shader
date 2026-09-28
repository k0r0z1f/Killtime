Shader "Killtime/Space/PlanetClouds"
{
    Properties
    {
        _CloudMap ("Cloud Texture (Grayscale)", 2D) = "white" {}
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Cloud Coverage (Cutoff)", Range(0.0, 1.0)) = 0.58
        _Softness ("Cloud Edge Softness", Range(0.01, 0.5)) = 0.18
        _Density ("Cloud Opacity Multiplier", Range(0.0, 2.0)) = 1.0
        _TerminatorSmoothness ("Terminator Softness", Range(0.01, 0.5)) = 0.15
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent-1" 
            "RenderPipeline" = "UniversalPipeline" 
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back
        // Léger biais depth pour passer devant la surface même quand
        // la précision depth s'effondre au loin (far/near élevé).
        Offset -1, -1

        Pass
        {
            Name "PlanetCloudsPass"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos   : TEXCOORD2;
            };

            TEXTURE2D(_CloudMap);
            SAMPLER(sampler_CloudMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _CloudMap_ST;
                float4 _CloudColor;
                float _Cutoff;
                float _Softness;
                float _Density;
                float _TerminatorSmoothness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _CloudMap);
                output.worldNormal = normalInput.normalWS;
                output.worldPos = vertexInput.positionWS;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_CloudMap, sampler_CloudMap, input.uv);
                float gray = tex.r;

                // Anti-speckle à longue distance : la texture mipée converge vers
                // sa moyenne (~_Cutoff) et le smoothstep fixe se met à scintiller.
                // On élargit la transition avec la dérivée écran pour garder une
                // couverture stable quand un pixel couvre beaucoup de texels.
                float fw = max(fwidth(gray), 1e-4);
                float w = max(_Softness, fw * 1.5);
                float alpha = smoothstep(_Cutoff - w, _Cutoff + w, gray) * _Density;
                alpha = saturate(alpha * _CloudColor.a);

                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                float3 normal = normalize(input.worldNormal);

                float NdotL = dot(normal, lightDir);
                float dayTerminator = smoothstep(-_TerminatorSmoothness, _TerminatorSmoothness, NdotL);

                float3 finalColor = _CloudColor.rgb * mainLight.color * dayTerminator;

                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}