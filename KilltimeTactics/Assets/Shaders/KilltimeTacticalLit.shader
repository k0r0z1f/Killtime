Shader "Killtime/TacticalLit"
{
    Properties
    {
        [MainColor] _BaseColor("Couleur Principale", Color) = (0.12, 0.14, 0.18, 1.0)
        _Metallic("Métal", Range(0.0, 1.0)) = 0.2
        _Smoothness("Lissage Spéculaire", Range(0.0, 1.0)) = 0.65

        [Header(Eclairage Rasif Tactique (Rim Light))]
        _RimColor("Couleur du Rim", Color) = (0.0, 0.85, 1.0, 1.0)
        _RimPower("Puissance du Rim", Range(0.5, 8.0)) = 3.2
        _RimIntensity("Intensité du Rim", Range(0.0, 5.0)) = 1.4

        [Header(Emission et Resonance Causale)]
        [HDR] _EmissionColor("Couleur d'Émission", Color) = (0.0, 0.0, 0.0, 1.0)
        _PulseFrequency("Fréquence de Pulsation", Range(0.0, 8.0)) = 1.2
        _PulseIntensity("Amplitude Pulsation", Range(0.0, 1.0)) = 0.2
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "Queue" = "Geometry" 
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float2 uv           : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float4 _EmissionColor;
                float _Metallic;
                float _Smoothness;
                float _RimPower;
                float _RimIntensity;
                float _PulseFrequency;
                float _PulseIntensity;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);
                output.uv = input.uv;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // 1. Éclairage diffus direct (Half-Lambert)
                half nDotL = saturate(dot(normalWS, mainLight.direction));
                half halfLambert = nDotL * 0.5 + 0.5;
                half3 diffuse = mainLight.color * (mainLight.shadowAttenuation * halfLambert);

                // 2. Éclairage ambiant (Harmoniques sphériques)
                half3 ambient = SampleSH(normalWS) * 0.35;

                // 3. Spéculaire Blinn-Phong
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                half nDotH = saturate(dot(normalWS, halfDir));
                half specPow = exp2(10.0 * _Smoothness + 1.0);
                half specular = pow(nDotH, specPow) * _Smoothness * mainLight.shadowAttenuation;
                half3 specColor = lerp(half3(0.04, 0.04, 0.04), _BaseColor.rgb, _Metallic);

                // 4. Rim Lighting cybernétique
                half fresnel = 1.0 - saturate(dot(normalWS, viewDirWS));
                half rim = pow(fresnel, _RimPower) * _RimIntensity;
                half3 rimLighting = rim * _RimColor.rgb;

                // 5. Modulation temporelle / Pulsation causale
                half pulse = sin(_Time.y * _PulseFrequency) * 0.5 + 0.5;
                half3 emissive = _EmissionColor.rgb + (_RimColor.rgb * (pulse * _PulseIntensity));

                // 6. Lumières additionnelles
                half3 additionalLighting = half3(0, 0, 0);
                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                for (uint i = 0u; i < pixelLightCount; ++i)
                {
                    Light addLight = GetAdditionalLight(i, input.positionWS);
                    half addNDotL = saturate(dot(normalWS, addLight.direction));
                    additionalLighting += addLight.color * (addNDotL * addLight.distanceAttenuation * addLight.shadowAttenuation);
                }
                #endif

                half3 finalColor = (_BaseColor.rgb * (diffuse + ambient + additionalLighting)) 
                                 + (specular * specColor) 
                                 + rimLighting 
                                 + emissive;

                return half4(finalColor, _BaseColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _MainLightPosition.xyz));
                #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}