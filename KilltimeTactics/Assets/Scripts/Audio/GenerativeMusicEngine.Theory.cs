using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Audio
{
    public static partial class GenerativeMusicEngine
    {
        // ================= THÉORIE, FORME & CONDUCTION DES VOIX =================

        /// <summary>
        /// Détermine le rôle structurel de la boucle dans le cycle narratif.
        /// Cycle de 5 générations : A (Thème) -> A' (Variation) -> B (Pont harmonique) -> Climax -> Breakdown (si énergie modérée) ou A''.
        /// </summary>
        public static GenerativeSectionRole GetSectionRole(GenerativeMusicParams p)
        {
            int pos = ((p.generation % 5) + 5) % 5;
            if (pos == 4)
            {
                return (p.intensity01 < 0.85f)
                    ? GenerativeSectionRole.Breakdown
                    : GenerativeSectionRole.Climax;
            }
            return pos switch
            {
                0 => GenerativeSectionRole.ThemeA,
                1 => GenerativeSectionRole.ThemeAVar,
                2 => GenerativeSectionRole.BridgeB,
                _ => GenerativeSectionRole.Climax
            };
        }

        private static int[] GetScaleIntervals(GenerativeScale scale)
        {
            switch (scale)
            {
                case GenerativeScale.Dorian: return new[] { 0, 2, 3, 5, 7, 9, 10 };
                case GenerativeScale.Phrygian: return new[] { 0, 1, 3, 5, 7, 8, 10 };
                case GenerativeScale.HarmonicMinor: return new[] { 0, 2, 3, 5, 7, 8, 11 };
                default: return new[] { 0, 2, 3, 5, 7, 8, 10 };
            }
        }

        private static GenerativeDrumStyle ResolveStyle(GenerativeMusicParams p, System.Random rng)
        {
            if (p.drumStyle != GenerativeDrumStyle.Auto) return p.drumStyle;
            float e = p.intensity01, t = p.tension01;
            if (e < 0.32f) return GenerativeDrumStyle.WarEnsemble;
            if (e < 0.58f) return (t > 0.6f) ? GenerativeDrumStyle.HalfTimeTrap : GenerativeDrumStyle.TribalTechno;
            if (e < 0.82f) return (rng.NextDouble() < 0.5 + (t - 0.5f) * 0.4) ? GenerativeDrumStyle.DoubleTime : GenerativeDrumStyle.TribalTechno;
            return (t > 0.55f) ? GenerativeDrumStyle.Blast : GenerativeDrumStyle.DoubleTime;
        }

        /// <summary>
        /// Construit la progression d'accords en préservant l'ADN harmonique du seed (themeRng)
        /// tout en infléchissant la couleur selon la section (BridgeB, Climax) et avec un vrai Voice-Leading.
        /// </summary>
        private static void BuildProgression(System.Random themeRng, System.Random varRng, GenerativeMusicParams p,
            GenerativeSectionRole role, int keyRoot, int[] scaleSteps, int count, out int[][] chords, out int[] bassRoots)
        {
            // Degrés chromatiques depuis la tonique : i=0, bII=1, III=3, iv=5, bV=6, v/V=7, bVI=8, bVII=10
            int[][] banksCalm =
            {
                new[] { 0, 8, 3, 10 }, // i - VI - III - VII (héroïque sombre)
                new[] { 0, 5, 8, 7 },  // i - iv - VI - V (andalou / tragique)
                new[] { 0, 10, 8, 7 }  // i - VII - VI - V (descente épique)
            };
            int[][] banksDrive =
            {
                new[] { 0, 8, 5, 7 },  // i - VI - iv - V
                new[] { 0, 3, 8, 10 }, // i - III - VI - VII
                new[] { 0, 10, 5, 7 }, // i - VII - iv - V
                new[] { 0, 1, 8, 7 }   // i - bII - VI - V (phrygien tendu)
            };
            int[][] banksBridge =
            {
                new[] { 8, 10, 3, 7 }, // départ sur VI -> VII -> III -> V (ouverture lumineuse/dramatique)
                new[] { 5, 8, 0, 7 },  // départ sous-dominante iv -> VI -> i -> V
                new[] { 3, 10, 8, 7 }  // départ relatif majeur III -> VII -> VI -> V
            };

            // Le thème d'ancrage est choisi une fois pour toutes par themeRng : même combat = même grille racine
            int[] anchorDegrees = (p.tension01 > 0.55f)
                ? banksDrive[themeRng.Next(banksDrive.Length)]
                : banksCalm[themeRng.Next(banksCalm.Length)];
            int[] bridgeDegrees = banksBridge[themeRng.Next(banksBridge.Length)];

            int[] baseDegrees = (role == GenerativeSectionRole.BridgeB) ? bridgeDegrees : anchorDegrees;
            int[] degrees = (int[])baseDegrees.Clone();

            // En Climax ou Chaos haut : substitution sur l'avant-dernier accord ou dominante renforcée en fin de cycle
            if (role == GenerativeSectionRole.Climax)
            {
                degrees[degrees.Length - 1] = 7; // cadence V forte pour relancer la boucle
                if (p.tension01 > 0.65f && degrees.Length >= 4) degrees[2] = 1; // napolitain bII dramatique
            }
            else if (p.chaos01 > 0.55f && varRng.NextDouble() < (p.chaos01 - 0.35f))
            {
                int sub = 1 + varRng.Next(Math.Max(1, degrees.Length - 2)); // préserve le 1er accord (tonique)
                degrees[sub] = (varRng.NextDouble() < 0.5) ? 6 : 1;
            }

            chords = new int[count][];
            bassRoots = new int[count];
            int[] prevVoicing = null;

            for (int i = 0; i < count; i++)
            {
                int deg = degrees[i % degrees.Length];
                int chordRoot = keyRoot + deg;
                bool isDominant = (deg == 7);
                bool majorThird = isDominant && (p.scale == GenerativeScale.HarmonicMinor || p.tension01 > 0.5f || role == GenerativeSectionRole.Climax);
                bool isNaturallyMajor = (deg == 3 || deg == 8 || deg == 10 || deg == 1);
                int third = (majorThird || isNaturallyMajor) ? 4 : 3;
                int fifth = (deg == 6) ? 6 : 7;
                int seventh = isDominant ? 10 : (deg == 3 || deg == 8 ? 11 : 10);

                // Pitch-classes cibles de l'accord (fondamentale, tierce, quinte, 7e, + 9e de couleur si brillant)
                var intervals = new List<int> { 0, third, fifth, seventh };
                if (p.brightness01 > 0.40f || role == GenerativeSectionRole.BridgeB || role == GenerativeSectionRole.Breakdown)
                {
                    int ninth = (deg == 0 && p.scale == GenerativeScale.Phrygian) ? 1 : 2;
                    intervals.Add(ninth);
                }

                int[] voiced = VoiceLeadChord(chordRoot, intervals, prevVoicing);
                prevVoicing = voiced;
                chords[i] = voiced;
                bassRoots[i] = chordRoot;
            }
        }

        /// <summary>
        /// Conduction des voix (Voice-Leading) : dispose les notes entre MIDI 50 (D3) et 74 (D5)
        /// en minimisant le déplacement total en demi-tons par rapport à l'accord précédent,
        /// et en garantissant un écart d'au moins 3 demi-tons entre voix adjacentes (pas de boue dans le bas-médium).
        /// </summary>
        private static int[] VoiceLeadChord(int chordRoot, List<int> intervals, int[] prevVoicing)
        {
            int n = intervals.Count;
            int[] result = new int[n];
            for (int k = 0; k < n; k++)
            {
                int rawPitch = chordRoot + 24 + intervals[k];
                while (rawPitch < 52) rawPitch += 12;
                while (rawPitch > 72) rawPitch -= 12;

                if (prevVoicing != null && k < prevVoicing.Length)
                {
                    int target = prevVoicing[k];
                    int best = rawPitch;
                    int bestDist = Math.Abs(rawPitch - target);
                    for (int shift = -12; shift <= 12; shift += 12)
                    {
                        int cand = rawPitch + shift;
                        if (cand < 48 || cand > 76) continue;
                        int dist = Math.Abs(cand - target);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = cand;
                        }
                    }
                    rawPitch = best;
                }
                result[k] = rawPitch;
            }

            Array.Sort(result);
            // Écarte les secondes serrées dans le registre grave (< 60) pour garder de la clarté
            for (int k = 1; k < n; k++)
            {
                if (result[k] - result[k - 1] < 3 && result[k] < 62)
                    result[k] = Math.Min(77, result[k] + 12);
            }
            Array.Sort(result);
            return result;
        }

        /// <summary>
        /// Construit le thème mélodique principal (16 pas) ancré sur les notes des accords sur les temps forts
        /// et utilisant la gamme comme notes de passage, avec une vraie ponctuation de phrase.
        /// </summary>
        private static int[] BuildThemeMotif(System.Random themeRng, GenerativeMusicParams p,
            int keyRoot, int[] scaleSteps, int[][] chords, int len)
        {
            int[] motif = new int[len];
            int scaleLen = scaleSteps.Length;
            int scalePos = scaleLen + themeRng.Next(0, 5); // registre médium-haut (octave +1)
            double restProb = Mathf.Lerp(0.30f, 0.10f, p.intensity01);

            for (int i = 0; i < len; i++)
            {
                bool strongBeat = (i == 0 || i == len / 2)
                    || (len == 20 && (i == 6 || i == 16))
                    || (len == 14 && (i == 4 || i == 11))
                    || (len != 14 && len != 20 && i % 4 == 0);
                bool cadenceStep = (i == len - 2);
                bool phraseBreath = (i == len / 2 - 1 || i == len - 1);

                // Respiration naturelle en fin de sous-phrase (pas 7 et pas 15)
                if (phraseBreath && themeRng.NextDouble() < 0.65)
                {
                    motif[i] = -1;
                    continue;
                }
                if (!strongBeat && !cadenceStep && themeRng.NextDouble() < restProb)
                {
                    motif[i] = -1;
                    continue;
                }

                if (strongBeat && chords != null && chords.Length > 0)
                {
                    // Temps fort : cible une note constitutive de l'accord courant (fondamentale, tierce ou quinte)
                    int ci = (i * chords.Length) / len;
                    var chord = chords[Math.Min(chords.Length - 1, ci)];
                    int pick = chord[themeRng.Next(Math.Min(3, chord.Length))];
                    while (pick < keyRoot + 24) pick += 12;
                    while (pick > keyRoot + 43) pick -= 12;
                    motif[i] = Mathf.Clamp(pick, 52, 81);
                    // Resynchronise scalePos sur la note choisie
                    int rel = ((pick - keyRoot) % 12 + 12) % 12;
                    int bestIdx = 0;
                    for (int s = 0; s < scaleLen; s++)
                        if (Math.Abs(scaleSteps[s] - rel) < Math.Abs(scaleSteps[bestIdx] - rel)) bestIdx = s;
                    scalePos = scaleLen + bestIdx;
                }
                else if (cadenceStep)
                {
                    // Avant-dernier pas : note sensible / seconde ou quinte qui tend vers la tonique
                    int deg = (themeRng.NextDouble() < 0.5) ? scaleSteps[1] : scaleSteps[4];
                    motif[i] = Mathf.Clamp(keyRoot + 36 + deg, 52, 81);
                }
                else
                {
                    // Mouvement conjoint majoritaire (chantable) + saut expressif occasionnel
                    int stepMove = (themeRng.NextDouble() < 0.78)
                        ? (themeRng.NextDouble() < 0.55 ? -1 : 1)
                        : themeRng.Next(-3, 4);
                    scalePos = Mathf.Clamp(scalePos + stepMove, 2, scaleLen * 2 - 1);
                    int oct = scalePos / scaleLen;
                    int note = keyRoot + 24 + scaleSteps[scalePos % scaleLen] + oct * 12;
                    motif[i] = Mathf.Clamp(note, 50, 82);
                }
            }
            return motif;
        }

        /// <summary>
        /// Développe le motif racine selon le rôle de la section (A = fidèle, A' = orné, B = inversé/transposé, Climax = octave/intense).
        /// </summary>
        private static int[] DevelopMotif(System.Random varRng, int[] coreMotif, int[] scaleSteps,
            int keyRoot, GenerativeSectionRole role, float chaos, bool isSecondHalf)
        {
            int[] dst = (int[])coreMotif.Clone();
            int len = dst.Length;

            if (role == GenerativeSectionRole.ThemeA && !isSecondHalf)
            {
                // Exposition fidèle du thème principal : seules 1 ou 2 micro-variations de fin
                if (chaos > 0.4f && len >= 4 && dst[len - 3] > 0)
                    dst[len - 3] = QuantizeToScale(dst[len - 3] + 2, keyRoot, scaleSteps);
                return dst;
            }

            for (int i = 0; i < len; i++)
            {
                if (i == 0 && role != GenerativeSectionRole.BridgeB) continue; // garde la 1re note d'accroche reconnaissable

                if (dst[i] < 0)
                {
                    // En Climax ou 2e moitié, comble certains silences par une note de relance
                    double fillProb = (role == GenerativeSectionRole.Climax ? 0.42 : 0.20) + chaos * 0.22;
                    if (isSecondHalf && varRng.NextDouble() < fillProb)
                    {
                        int refNote = dst[(i + len - 2) % len];
                        if (refNote > 0) dst[i] = QuantizeToScale(refNote + (varRng.NextDouble() < 0.5 ? 2 : -2), keyRoot, scaleSteps);
                    }
                    continue;
                }

                double r = varRng.NextDouble();
                if (role == GenerativeSectionRole.BridgeB)
                {
                    // Pont B : transposition diatonique à la tierce/quarte + rythme plus aéré
                    if (i % 4 == 2 && r < 0.35) { dst[i] = -1; continue; }
                    int shift = (i < len / 2) ? 3 : -2;
                    dst[i] = QuantizeToScale(dst[i] + shift, keyRoot, scaleSteps);
                }
                else if (role == GenerativeSectionRole.Climax)
                {
                    // Climax : poussée vers le registre aigu sur les temps d'accent
                    if ((i % 4 == 0 || isSecondHalf) && dst[i] <= 68 && r < 0.65)
                        dst[i] += 12;
                    else if (r < 0.25 + chaos * 0.25)
                        dst[i] = QuantizeToScale(dst[i] + (varRng.NextDouble() < 0.6 ? 2 : -2), keyRoot, scaleSteps);
                }
                else
                {
                    // ThemeAVar / Breakdown : ornementation diatonique respectueuse du contour
                    if (r < 0.14 + chaos * 0.20)
                        dst[i] = QuantizeToScale(dst[i] + varRng.Next(-3, 4), keyRoot, scaleSteps);
                    else if (r < 0.22 + chaos * 0.15 && dst[i] < 72)
                        dst[i] += 12;
                    else if (role == GenerativeSectionRole.Breakdown && i % 2 == 1 && r < 0.65)
                        dst[i] = -1; // allège fortement en Breakdown
                }
                if (dst[i] > 0) dst[i] = Mathf.Clamp(dst[i], 48, 84);
            }

            // En 2e moitié (Réponse B), conclut la dernière note jouée sur la tonique ou la quinte (résolution)
            if (isSecondHalf)
            {
                for (int i = len - 1; i >= len - 3 && i >= 0; i--)
                {
                    if (dst[i] > 0)
                    {
                        int targetPc = (role == GenerativeSectionRole.BridgeB) ? scaleSteps[4] : 0; // quinte sur le pont, tonique sinon
                        int oct = (dst[i] - keyRoot) / 12;
                        dst[i] = Mathf.Clamp(keyRoot + oct * 12 + targetPc, 52, 81);
                        break;
                    }
                }
            }
            return dst;
        }

        /// <summary>
        /// Génère un contre-chant harmonisé (tierce / sixte diatonique en dessous)
        /// actif pendant les Climax et les réponses intenses.
        /// </summary>
        private static int[] BuildCounterMotif(System.Random varRng, int[] motifA, int[] motifB,
            int[] scaleSteps, int keyRoot, GenerativeSectionRole role)
        {
            int len = motifA.Length;
            int[] counter = new int[len];
            for (int i = 0; i < len; i++)
            {
                int src = (i < len / 2) ? motifA[i] : motifB[i];
                if (src < 0)
                {
                    counter[i] = -1;
                    continue;
                }
                // Harmonise une tierce (-3/-4 demi-tons diatoniques) ou une sixte (-8/-9) sous le lead
                int interval = (role == GenerativeSectionRole.Climax && i % 4 == 0) ? -8 : -3;
                counter[i] = Mathf.Clamp(QuantizeToScale(src + interval, keyRoot, scaleSteps), 48, 78);
            }
            return counter;
        }

        private static int QuantizeToScale(int midi, int keyRoot, int[] scaleSteps)
        {
            int rel = midi - keyRoot;
            int oct = Mathf.FloorToInt(rel / 12f);
            int pc = rel - oct * 12;
            int bestStep = scaleSteps[0];
            int bestDist = 99;
            for (int i = 0; i < scaleSteps.Length; i++)
            {
                int d = Math.Abs(scaleSteps[i] - pc);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestStep = scaleSteps[i];
                }
            }
            return keyRoot + oct * 12 + bestStep;
        }
    }
}
