Shader "Killtime/Space/SpaceStarfield"
{
    Properties
    {
        _StarDensity ("Densite etoiles", Range(0.0, 2.0)) = 1.0
        _StarExposure ("Exposition etoiles (0=jour, 1=nuit)", Range(0.0, 1.0)) = 1.0
        _StarSize ("Taille etoiles", Range(0.5, 3.0)) = 1.0
        _MilkyWayIntensity ("Intensite Voie lactee", Range(0.0, 1.0)) = 0.35
        _BackgroundColor ("Fond espace", Color) = (0.005, 0.008, 0.02, 1.0)
        _WarpAmount ("Effet supraluminique (0=normal, 1=vitesse lumiere)", Range(0.0, 1.0)) = 0.0
        _WarpCenter ("Direction centre warp (auto camera forward)", Vector) = (0, 0, 1, 0)
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
                float _WarpAmount;
                float3 _WarpCenter;
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
            // aucune singularité. Distance en corde (chord) dir↔centre étoile, optionnellement
            // étirée le long d'un axe (streakAxis, facteur streakElong ≥ 1) : UNE étoile = UN segment
            // continu (pas de taps multiples → jamais d'étoile en double/quadruple).
            float3 starLayer(float3 dir, float scale, float threshold, float sizeMul, float3 streakAxis, float streakElong)
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
                // Étirement : la composante le long de l'axe est compressée d'un facteur elong,
                // donc l'étoile s'allonge en segment continu le long de l'axe. elong=1 → rond parfait
                // (bit-identique à avant aux arrondis près). Division par elong ≥ 1 : sans danger.
                float3 off = dir - starDir;
                float par = dot(off, streakAxis);
                float3 perp = off - par * streakAxis;
                float d = length(perp + (par / max(streakElong, 1.0)) * streakAxis);

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
                float warp = clamp(_WarpAmount, 0.0, 1.0);
                float density = clamp(_StarDensity, 0.0, 2.0);
                float3 col = float3(0.0, 0.0, 0.0);
                float vaultMask = 1.0;

                if (warp < 0.001)
                {
                    col = _BackgroundColor.rgb;

                    // 3 couches = parallaxe de magnitudes, sans vrai déplacement.
                    col += starLayer(dir, 300.0, 0.060 * density, 0.8 * _StarSize, float3(0.0, 0.0, 1.0), 1.0);
                    col += starLayer(dir, 140.0, 0.040 * density, 1.6 * _StarSize, float3(0.0, 0.0, 1.0), 1.0) * 1.4;
                    col += starLayer(dir, 620.0, 0.090 * density, 0.55 * _StarSize, float3(0.0, 0.0, 1.0), 1.0) * 0.55;

                    // Voie lactée : fond diffus, jamais noir pur côté nuit.
                    col += float3(0.35, 0.45, 0.62) * milkyWay(dir) * _MilkyWayIntensity * 0.35;
                }
                else
                {
                    // CONTRACTION RELATIVISTE INTÉGRALE & ÉTIREMENT RADIAL DEPUIS LE POINT CENTRAL
                    // Réf vidéo RD-110 : The Entire Trip to the Nearest Star in Real Time (cEkozf-zCkY)
                    //
                    // AU DÉPART (warp = 1, vitesse supraluminique de croisière) :
                    // Toute la voûte céleste entière (360° x 180°) est 100% contractée dans un
                    // minuscule point/cœur central (rayon theta0 ~ 0.012 rad = 0.7°). Le reste entier du ciel
                    // est noir absolu (0, 0, 0) : AUCUNE étoile n'est visible sur les côtés avant que
                    // l'étirement ne s'amorce depuis le centre !
                    //
                    // DÉCÉLÉRATION (15s, warp 1 -> 0) :
                    // La voûte s'étire et jaillit depuis le centre. Chaque étoile part du point central
                    // et s'étire en faisceau radial le long de r_perp vers sa coordonnée céleste normale.
                    // Les traînées s'allongent puissamment lors du déploiement puis se rétractent en
                    // points parfaits lorsque warp atteint 0, formant exactement la voûte normale.
                    float3 wc = _WarpCenter;
                    float wlen = length(wc);
                    wc = wlen > 0.0001 ? wc / wlen : float3(0.0, 0.0, 1.0);
                    float cosA = clamp(dot(dir, wc), -1.0, 1.0);

                    // Angle de vue depuis le centre (0 au centre, PI à l'opposé)
                    float thetaObs = acos(cosA);

                    // Front d'expansion radiale : à warp = 1, confiné au point central theta0 (0.012 rad = 0.7°).
                    // Dès que la décélération s'enclenche (warp < 1), thetaFront s'ouvre continûment jusqu'à PI (180°).
                    float u = 1.0 - warp; // 0 à vitesse lumière -> 1 à l'arrêt
                    float theta0 = 0.012; // Rayon du point contracté au centre
                    float thetaFront = theta0 + (3.14159265 - theta0) * pow(u, 1.30);

                    // Masque de confinement strict : en dehors de thetaFront, le ciel est STRICTEMENT NOIR.
                    float delta = 0.12 * thetaFront + 0.006;
                    float rawMask = 1.0 - smoothstep(thetaFront - delta, thetaFront, thetaObs);
                    // Quand warp touche à 0 (< 0.08), ouverture globale à 100% sur toute la sphère
                    vaultMask = lerp(rawMask, 1.0, 1.0 - smoothstep(0.0, 0.08, warp));

                    // VECTEUR TRANSVERSE ET DIRECTION RADIALE 3D
                    float3 rvec = dir - cosA * wc;
                    float rlen = length(rvec);
                    float3 rperp = rlen > 0.0001 ? rvec / rlen : float3(1.0, 0.0, 0.0);

                    // Position relative dans le front d'expansion (0 au centre -> 1 au bord du front)
                    float xi = thetaObs / max(0.0001, thetaFront);

                    // =========================================================================
                    // PROFONDEUR VOLUMÉTRIQUE 3D & PARALLAXE DIFFÉRENTIELLE MULTI-COUCHES
                    // Dans un univers 3D, les étoiles ne sont pas sur un plan 2D qui s'étire :
                    // elles sont réparties en profondeur et réagissent avec des vitesses de parallaxe
                    // et des vecteurs de fuite 3D distincts selon leur distance à l'observateur.
                    // =========================================================================

                    // COUCHE 1 : ÉTOILES PROCHES / AVANT-PLAN (échelle 140, brillantes et massives)
                    // - Parallaxe forte : jaillissent en premier, dépassent l'angle moyen (exposant 0.70)
                    // - Vecteur de traînée 3D : composante radiale + fuite vers la caméra en profondeur (-wc)
                    // - Étirement prononcé : traînées longues et dynamiques qui rasent le champ de vision
                    float xi1 = pow(xi, 0.70);
                    float thetaRest1 = clamp(lerp(thetaObs, xi1 * 3.14159265, warp), 0.0, 3.14159265);
                    float3 S1 = cos(thetaRest1) * wc + sin(thetaRest1) * rperp;
                    float3 vStreak1 = normalize(rperp * 0.88 - wc * 0.48 * saturate(xi * 1.5));
                    float elong1 = 1.0 + warp * 75.0 * saturate(xi * 2.5);
                    float blue1 = saturate((1.0 - xi * 0.55) * warp);
                    float3 tint1 = lerp(float3(1.0, 1.0, 1.0), float3(0.55, 0.82, 1.55), blue1);
                    float3 lay1 = starLayer(S1, 140.0, 0.040 * density, 1.6 * _StarSize, vStreak1, elong1) * (1.4 * tint1);

                    // COUCHE 0 : ÉTOILES DU PLAN INTERMÉDIAIRE (échelle 300, étoiles moyennes)
                    // - Parallaxe intermédiaire : expansion équilibrée au cœur du flux stellaire
                    // - Vecteur de traînée 3D : équilibre radial et profondeur modérée
                    float xi0 = xi;
                    float thetaRest0 = clamp(lerp(thetaObs, xi0 * 3.14159265, warp), 0.0, 3.14159265);
                    float3 S0 = cos(thetaRest0) * wc + sin(thetaRest0) * rperp;
                    float3 vStreak0 = normalize(rperp * 0.94 - wc * 0.22 * saturate(xi * 1.2));
                    float elong0 = 1.0 + warp * 45.0 * saturate(xi * 2.0);
                    float blue0 = saturate((1.0 - xi * 0.75) * warp);
                    float3 tint0 = lerp(float3(1.0, 1.0, 1.0), float3(0.68, 0.86, 1.35), blue0);
                    float3 lay0 = starLayer(S0, 300.0, 0.060 * density, 0.8 * _StarSize, vStreak0, elong0) * tint0;

                    // COUCHE 2 : COSMOS PROFOND / ARRIÈRE-PLAN (échelle 620, poussières d'étoiles lointaines)
                    // - Parallaxe lente : restent groupées plus longtemps au centre (entonnoir de profondeur)
                    // - Traînées courtes et fines : ancrent l'infini et la profondeur abyssale de l'espace
                    float xi2 = pow(xi, 1.42);
                    float thetaRest2 = clamp(lerp(thetaObs, xi2 * 3.14159265, warp), 0.0, 3.14159265);
                    float3 S2 = cos(thetaRest2) * wc + sin(thetaRest2) * rperp;
                    float3 vStreak2 = rperp;
                    float elong2 = 1.0 + warp * 18.0 * saturate(xi * 1.6);
                    float blue2 = saturate((1.0 - xi * 0.90) * warp);
                    float3 tint2 = lerp(float3(1.0, 1.0, 1.0), float3(0.80, 0.90, 1.20), blue2);
                    float3 lay2 = starLayer(S2, 620.0, 0.090 * density, 0.55 * _StarSize, vStreak2, elong2) * (0.55 * tint2);

                    // ATTÉNUATION DE LUMINOSITÉ QUAND CONTRACTÉES AU CENTRE (retour utilisateur) :
                    // Les étoiles sont assombries/plus douces quand elles sont contractées près du centre,
                    // et montent progressivement vers leur luminosité normale (100%) au fur et à mesure
                    // que la voûte s'étend et redevient normale (warp -> 0).
                    float starBright = lerp(1.0, lerp(0.20, 0.85, saturate(xi * 1.3)), warp);

                    // Somme des 3 couches volumétriques (confinées au front et atténuées au centre)
                    col = (lay0 + lay1 + lay2) * (vaultMask * starBright);

                    // DISPARITION RAPIDE DU POINT CENTRAL DÈS LE DÉBUT DE L'EXPANSION (retour utilisateur) :
                    // Au départ (warp = 1, croisière), le point central brille intensément.
                    // Dès que la voûte commence à s'étendre (warp < 0.99), le point central s'estompe
                    // rapidement et disparaît complètement (éteint dès warp <= 0.82) pour laisser place aux étoiles.
                    float coreFade = smoothstep(0.82, 0.99, warp);
                    coreFade *= coreFade; // Chute rapide et nette dès l'ouverture

                    float angDist = max(0.0, 1.0 - cosA);
                    float focalStar = exp(-angDist * 35000.0) * 5.0 * coreFade;
                    float coreCone = saturate(1.0 - thetaObs / 0.10);
                    float corona = exp(-angDist * 1800.0) * 1.20 * coreFade * coreCone;
                    float bloom = exp(-angDist * 220.0) * 0.45 * coreFade * coreCone;
                    col += float3(0.98, 0.99, 1.0) * focalStar;
                    col += float3(0.40, 0.75, 1.45) * (corona + bloom);

                    // Voie lactée : ancrée sur la coordonnée du cosmos profond S2 (structure galactique en arrière-plan)
                    float mwGate = (1.0 - warp * 0.85) * vaultMask;
                    col += float3(0.35, 0.45, 0.62) * milkyWay(S2) * _MilkyWayIntensity * 0.35 * mwGate;

                    // Fond spatial noir profond : noir pur tant que le front n'a pas atteint la zone
                    float bgGate = (1.0 - warp * 0.98) * vaultMask;
                    col += _BackgroundColor.rgb * bgGate;
                }

                // Exposition réaliste (adaptation optique)
                float keep = 0.04 + 0.96 * _StarExposure;
                float3 bg = _BackgroundColor.rgb;
                if (warp >= 0.001)
                {
                    // Pendant le warp, le fond hors du front d'expansion reste noir pur
                    float bgGateExp = (1.0 - warp * 0.98) * vaultMask;
                    bg = _BackgroundColor.rgb * bgGateExp;
                }
                col = bg + (col - bg) * keep;

                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
