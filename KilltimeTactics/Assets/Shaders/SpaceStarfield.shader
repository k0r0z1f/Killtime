Shader "Killtime/Space/SpaceStarfield"
{
    Properties
    {
        _StarDensity ("Densite etoiles", Range(0.0, 2.0)) = 1.0
        _StarExposure ("Exposition etoiles (0=jour, 1=nuit)", Range(0.0, 1.0)) = 1.0
        _StarSize ("Taille etoiles", Range(0.5, 3.0)) = 1.0
        _MilkyWayIntensity ("Intensite Voie lactee", Range(0.0, 1.0)) = 0.35
        _BackgroundColor ("Fond espace", Color) = (0.005, 0.008, 0.02, 1.0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Background"
            "Queue" = "Background-10"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Front
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "StarfieldPass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _StarDensity;
                float _StarExposure;
                float _StarSize;
                float _MilkyWayIntensity;
                float4 _BackgroundColor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                // Sphère suiveuse caméra côté CPU : on rend en monde tel quel.
                // Profondeur naturelle (sphère à 90% du far) : compatible Z inversé URP.
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                // Direction = position objet normalisée (sphère unité centrée caméra).
                // Sphère de rayon 1 : jamais de vecteur nul, pas de NaN.
                output.dirWS = normalize(input.positionOS.xyz);
                return output;
            }

            // Hash 3D stable sans texture : petites constantes, pas d'explosion
            // de précision (v1 : division par ~0 + smoothstep inversé = plaques grises
            // sur AMD/OpenGL). Aucune division par variable dans tout le shader.
            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.11, 0.17, 0.13));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float3 starTint(float h)
            {
                // Physique : blanc dominant, bleues chaudes, rares ambrées.
                if (h > 0.85) return float3(0.65, 0.82, 1.0);
                if (h > 0.70) return float3(1.0, 0.85, 0.65);
                return float3(0.92, 0.95, 1.0);
            }

            // Une couche d'étoiles sur grille 3D de la direction : aucune projection,
            // aucune singularité. Distance en corde (chord) dir↔centre étoile.
            float3 starLayer(float3 dir, float scale, float threshold, float sizeMul)
            {
                float3 g = dir * scale;
                float3 cell = floor(g);
                float h = hash31(cell);

                // Seuil : seules les cellules rares portent une étoile.
                float star = step(1.0 - threshold, h);
                if (star < 0.5) return float3(0.0, 0.0, 0.0);

                // Centre de l'étoile : jitter stable dans la cellule, renormalisé.
                // |cell| >> 0 loin de l'origine (dir unitaire × scale ≥ 100) : pas de NaN.
                float3 jitter = float3(hash31(cell + 7.13), hash31(cell + 3.71), hash31(cell + 5.37)) - 0.5;
                float3 starDir = normalize(cell + 0.5 + jitter * 0.85);
                float d = length(dir - starDir);

                // Taille angulaire en unités de corde (~8e-4 ≈ 1px à 1080p/48°).
                // smoothstep TOUJOURS croissant (edge0 < edge1) : pas de UB driver.
                float coreR = 0.0008 * sizeMul;
                float core = 1.0 - smoothstep(0.0, coreR, d);
                float halo = (1.0 - smoothstep(0.0, coreR * 3.0, d)) * 0.35;
                float mag = hash31(cell + 11.31);
                // Distribution magnitudes : peu de brillantes, beaucoup de faibles.
                float brightness = (0.25 + 0.75 * mag * mag) * (core + halo);
                return starTint(hash31(cell + 5.91)) * brightness;
            }

            // Bruit de valeur 3D LISSÉ (interpolation trilinear) : la version
            // par blocs floor() donnait des rectangles à bords durs dans la Voie lactée.
            float smoothNoise3(float3 p)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                float a = hash31(cell);
                float b = hash31(cell + float3(1.0, 0.0, 0.0));
                float c = hash31(cell + float3(0.0, 1.0, 0.0));
                float d = hash31(cell + float3(1.0, 1.0, 0.0));
                float e = hash31(cell + float3(0.0, 0.0, 1.0));
                float f2 = hash31(cell + float3(1.0, 0.0, 1.0));
                float g = hash31(cell + float3(0.0, 1.0, 1.0));
                float h = hash31(cell + float3(1.0, 1.0, 1.0));
                return lerp(lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y),
                            lerp(lerp(e, f2, u.x), lerp(g, h, u.x), u.y), u.z);
            }

            float milkyWay(float3 dir)
            {
                // Grand cercle incliné : bande diffuse subtile, bords ordonnés.
                float3 bandN = normalize(float3(0.35, 1.0, 0.25));
                float d = abs(dot(dir, bandN));
                float band = 1.0 - smoothstep(0.02, 0.28, d);
                // Granulosité basse fréquence LISSÉE : taches douces, aucune couture.
                float n = smoothNoise3(dir * 5.0);
                return band * (0.55 + 0.45 * n);
            }

            float4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.dirWS);
                float3 col = _BackgroundColor.rgb;

                // 3 couches = parallaxe de magnitudes, sans vrai déplacement.
                float density = clamp(_StarDensity, 0.0, 2.0);
                col += starLayer(dir, 300.0, 0.060 * density, 0.8 * _StarSize);
                col += starLayer(dir, 140.0, 0.040 * density, 1.6 * _StarSize) * 1.4;
                col += starLayer(dir, 620.0, 0.090 * density, 0.55 * _StarSize) * 0.55;

                // Voie lactée : fond diffus, jamais noir pur côté nuit.
                col += float3(0.35, 0.45, 0.62) * milkyWay(dir) * _MilkyWayIntensity * 0.35;

                // Exposition réaliste : côté jour (planète/soleil dans le champ),
                // le script baisse _StarExposure -> les faibles s'effacent d'abord.
                // On garde ~4% des plus brillantes même à 0 pour éviter le vide total.
                float keep = 0.04 + 0.96 * _StarExposure;
                float3 bg = _BackgroundColor.rgb;
                col = bg + (col - bg) * keep;

                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
