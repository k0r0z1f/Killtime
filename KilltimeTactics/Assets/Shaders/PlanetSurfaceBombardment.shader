Shader "Killtime/Space/PlanetSurfaceBombardment"
{
    Properties
    {
        _BaseMap ("Diffuse / Albedo Map", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Range(0.0, 2.0)) = 1.0
        _MaskMap ("Mask Map (R: Metallic, G: Occlusion, B: Bombardment Mask, A: Ocean Specular)", 2D) = "white" {}
        
        [Header(Oceans)]
        _WaterSpecularColor ("Water Specular Color", Color) = (0.8, 0.9, 1.0, 1.0)
        _WaterSmoothness ("Water Smoothness", Range(0.0, 1.0)) = 0.92

        [Header(Orbital Bombardment)]
        _BombardmentEmissionMap ("Bombardment Fire & Plasma Map", 2D) = "black" {}
        [HDR] _BombardmentColor ("Bombardment Color HDR", Color) = (15.0, 4.2, 0.8, 1.0)
        _FlickerSpeed ("Flicker Speed", Float) = 14.0
        _FlickerIntensity ("Flicker Variation", Range(0.0, 1.0)) = 0.35

        [Header(Terminator Settings)]
        _TerminatorSharpness ("Terminator Sharpness", Range(0.001, 0.5)) = 0.08
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "Queue" = "Geometry" 
            "RenderPipeline" = "UniversalPipeline" 
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 worldPos   : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldTangent : TEXCOORD3;
                float3 worldBitangent : TEXCOORD4;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            TEXTURE2D(_MaskMap);
            SAMPLER(sampler_MaskMap);

            TEXTURE2D(_BombardmentEmissionMap);
            SAMPLER(sampler_BombardmentEmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _NormalScale;
                float4 _WaterSpecularColor;
                float _WaterSmoothness;
                float4 _BombardmentColor;
                float _FlickerSpeed;
                float _FlickerIntensity;
                float _TerminatorSharpness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.worldPos = vertexInput.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.worldNormal = normalInput.normalWS;
                output.worldTangent = normalInput.tangentWS;
                output.worldBitangent = normalInput.bitangentWS;

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.uv);
                float4 normalSample = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv);
                float3 normalTS = UnpackNormalScale(normalSample, _NormalScale);

                float3x3 TBN = float3x3(normalize(input.worldTangent), normalize(input.worldBitangent), normalize(input.worldNormal));
                float3 worldNormal = normalize(mul(normalTS, TBN));

                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                float3 viewDir = normalize(GetCameraPositionWS() - input.worldPos);

                float NdotL = dot(worldNormal, lightDir);
                float dayTerminator = smoothstep(-_TerminatorSharpness, _TerminatorSharpness, NdotL);

                float3 diffuseLighting = albedo.rgb * mainLight.color * dayTerminator;

                float3 halfVector = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(worldNormal, halfVector));
                float oceanSpec = pow(NdotH, _WaterSmoothness * 128.0) * mask.a;
                float3 specularLighting = oceanSpec * _WaterSpecularColor.rgb * mainLight.color * dayTerminator;

                float4 bombTexture = SAMPLE_TEXTURE2D(_BombardmentEmissionMap, sampler_BombardmentEmissionMap, input.uv);
                
                float flickerNoise = sin(_Time.y * _FlickerSpeed + input.uv.x * 120.0 + input.uv.y * 80.0) * 0.5 + 0.5;
                float flickerMod = 1.0 - (_FlickerIntensity * flickerNoise);
                float3 bombardmentEmission = bombTexture.rgb * mask.b * _BombardmentColor.rgb * flickerMod;

                float3 finalColor = diffuseLighting + specularLighting + bombardmentEmission;

                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}