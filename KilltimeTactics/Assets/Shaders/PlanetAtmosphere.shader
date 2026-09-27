Shader "Killtime/Space/PlanetAtmosphere"
{
    Properties
    {
        _AtmosphereColor ("Atmosphere Color", Color) = (0.35, 0.65, 1.0, 1.0)
        _RimPower ("Rim Power", Range(0.5, 8.0)) = 3.5
        _RimIntensity ("Rim Intensity", Range(0.0, 10.0)) = 4.0
        _TerminatorSmoothness ("Terminator Smoothness", Range(0.01, 1.0)) = 0.4
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
        }

        Blend One OneMinusSrcAlpha
        Cull Front
        ZWrite Off

        Pass
        {
            Name "AtmospherePass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldViewDir : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _AtmosphereColor;
                float _RimPower;
                float _RimIntensity;
                float _TerminatorSmoothness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.worldPos = vertexInput.positionWS;
                output.worldNormal = normalInput.normalWS;
                output.worldViewDir = normalize(GetCameraPositionWS() - vertexInput.positionWS);

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                float3 viewDir = normalize(input.worldViewDir);
                float3 normal = normalize(input.worldNormal);

                float NdotV = saturate(dot(-normal, viewDir));
                float rim = pow(1.0 - NdotV, _RimPower) * _RimIntensity;

                float lightDot = dot(-normal, lightDir);
                float lightMask = smoothstep(-_TerminatorSmoothness, _TerminatorSmoothness, lightDot);

                float alpha = rim * lightMask * _AtmosphereColor.a;
                float3 finalColor = _AtmosphereColor.rgb * rim * lightMask * mainLight.color;

                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}