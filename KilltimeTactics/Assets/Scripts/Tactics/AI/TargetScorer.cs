using System;
using UnityEngine;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// Entrées du scoring de cible. Le contrôleur réunit tout ce qui dépend
    /// d'Unity/singletons (brouillard, couvert, statuts, escouade) puis appelle
    /// Score() qui est une formule pure. Aucune lecture de mise adverse
    /// (duel aveugle) : uniquement PV, statuts publics, couvert, distance.
    /// </summary>
    [Serializable]
    public struct TargetScoreInput
    {
        public bool IsRanged;
        public int Dist;
        public int MaxRange;
        public bool HasLOS;
        public CoverType TargetCover;
        public float CurHp;
        public float MaxHp;
        public int Encaissement;
        public bool IsSquadMarked;
        public bool NightLampBonus;   // IsNight && HasFlashlight
        public bool NoiseBonus;       // IsNoiseActive(nom)
        public bool HasDestabilise;
        public bool HasATerre;
        public bool FlawExposed;      // SkillTechniqueState.IsFlawExposed
        public int TargetCurrentAP;   // menace active si >= 4
    }

    /// <summary>
    /// Scoring de cible extrait verbatim de TacticalAIController.EvaluateBestTarget
    /// (boucle lignes ~3410-3480). Formule inchangée, constantes inchangées.
    /// Testable en EditMode sans grille ni fog.
    /// </summary>
    public static class TargetScorer
    {
        public static float Score(in TargetScoreInput input)
        {
            float score = 80f;

            if (input.IsRanged)
            {
                bool inRangedZone = (input.Dist <= input.MaxRange);

                if (inRangedZone && input.HasLOS)
                {
                    score += 50f;
                    if (input.Dist >= 2 && input.Dist <= input.MaxRange) score += 15f;
                    if (input.TargetCover == CoverType.Half) score -= 10f;
                    else if (input.TargetCover == CoverType.ThreeQuarters) score -= 25f;
                }
                else if (inRangedZone && !input.HasLOS)
                {
                    score += 5f;
                }
                else
                {
                    score -= (input.Dist - input.MaxRange) * 6f;
                }
            }
            else
            {
                score -= (input.Dist * 8f);
                if (input.Dist == 1) score += 45f;
            }

            // Opportunisme létal & Seuil d'encaissement (Livre I)
            score += (1.0f - (input.CurHp / Mathf.Max(1f, input.MaxHp))) * 40f;
            if (input.CurHp <= input.Encaissement) score += 30f;
            if (input.CurHp <= 6) score += 50f;

            // Coordination d'Escouade (Livre III) : Focus-Fire
            if (input.IsSquadMarked)
            {
                score += 45f;
            }

            // RD-053 : Nuit / Lampe / Bruit attire l'IA
            if (input.NightLampBonus) score += 25f;
            if (input.NoiseBonus) score += 20f;

            // Exploitation des brèches défensives créées par les alliés
            if (input.HasDestabilise) score += 20f;
            if (input.HasATerre) score += 25f;

            // Analyse de Faille (Livre III) : encaissement ignoré
            if (input.FlawExposed) score += 30f;

            // Priorité de menace active
            if (input.TargetCurrentAP >= 4) score += 15f;

            return score;
        }
    }
}
