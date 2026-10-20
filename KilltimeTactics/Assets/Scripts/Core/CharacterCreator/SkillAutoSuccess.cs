using System;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Rang de maîtrise d'une compétence (Livre II §7.2).
    /// Le « rang » du Livre = rang de compétence effectif = rang de base
    /// (caractéristiques, <see cref="SkillDefinitions.GetBaseRank"/>) + entraînements,
    /// soit <c>CharacterStats.GetSkillRank</c>. Bandes Livre II §7.2 :
    /// Débutant = rangs 1-3, Initié/Avancé = rangs 4-7, Expert/Maître = rangs 8-10+.
    /// Le découpage intra-bande (4-5 Initié, 6-7 Avancé, 8-9 Expert, 10+ Maître)
    /// est l'interprétation médiane : chaque sous-rang couvre 2 valeurs,
    /// monotone avec les seuils auto 4/6/8/10.
    /// </summary>
    public enum SkillRank
    {
        Debutant = 0,
        Initie = 1,
        Avance = 2,
        Expert = 3,
        Maitre = 4
    }

    /// <summary>
    /// Réussites automatiques par rang (RD-203, Livre II §7.2) : logique pure,
    /// sans dépendance Unity, testable en EditMode.
    ///
    /// Table Livre II §7.2 — seuil maximal réussi SANS jet :
    ///   Débutant (rangs 1-3) → 0 · Initié (4-5) → 4 · Avancé (6-7) → 6
    ///   Expert (8-9) → 8 · Maître (10+) → 10.
    /// Règle : si le seuil de difficulté (SD fixe) est &lt;= seuil auto du rang,
    /// pas de jet — réussite immédiate avec traçabilité log.
    ///
    /// Portée : épreuves simples à SD fixe (ResolveSkillCheck, pré-checks
    /// Observation/Discretion passifs, interactables, défis hors-combat simples).
    /// Les défis OPPOSÉS (total adverse dynamique, ex. duel de détection
    /// Observation vs Discretion du brouillard) gardent leurs deux jets :
    /// aucun seuil fixe ne s'y applique (Livre II §7.3).
    /// </summary>
    public static class SkillAutoSuccess
    {
        public const int DebutantAuto = 0;
        public const int InitieAuto = 4;
        public const int AvanceAuto = 6;
        public const int ExpertAuto = 8;
        public const int MaitreAuto = 10;

        /// <summary>
        /// Rang de maîtrise déduit du rang de compétence effectif
        /// (base + entraînements). Rangs &lt;= 3 → Débutant (couvre 0/négatif).
        /// </summary>
        public static SkillRank ForSkillRank(int skillRank)
        {
            if (skillRank >= 10) return SkillRank.Maitre;
            if (skillRank >= 8) return SkillRank.Expert;
            if (skillRank >= 6) return SkillRank.Avance;
            if (skillRank >= 4) return SkillRank.Initie;
            return SkillRank.Debutant;
        }

        /// <summary>Seuil auto du rang (Livre II §7.2).</summary>
        public static int AutoThresholdForRank(SkillRank rank)
        {
            return rank switch
            {
                SkillRank.Initie => InitieAuto,
                SkillRank.Avance => AvanceAuto,
                SkillRank.Expert => ExpertAuto,
                SkillRank.Maitre => MaitreAuto,
                _ => DebutantAuto,
            };
        }

        /// <summary>Seuil auto déduit du rang de compétence effectif.</summary>
        public static int AutoThresholdFor(int skillRank)
        {
            return AutoThresholdForRank(ForSkillRank(skillRank));
        }

        /// <summary>Vrai si le seuil est couvert par le rang : pas de jet.</summary>
        public static bool IsAutoSuccess(int skillRank, int difficulty)
        {
            return difficulty <= AutoThresholdFor(skillRank);
        }

        /// <summary>Libellé français du rang (logs).</summary>
        public static string RankLabel(SkillRank rank)
        {
            return rank switch
            {
                SkillRank.Initie => "Initié",
                SkillRank.Avance => "Avancé",
                SkillRank.Expert => "Expert",
                SkillRank.Maitre => "Maître",
                _ => "Débutant",
            };
        }

        /// <summary>
        /// Ligne de traçabilité quand l'auto-succès s'applique (rang, seuil, auto).
        /// Appelée par chaque call site branché — ne jamais réussir en silence.
        /// </summary>
        public static string BuildAutoSuccessLog(string skillName, int skillRank, int difficulty)
        {
            var rank = ForSkillRank(skillRank);
            int auto = AutoThresholdForRank(rank);
            return $"✨ <b>[SUCCÈS AUTOMATIQUE]</b> {skillName} — rang {RankLabel(rank)} "
                + $"(compétence rang {Math.Max(0, skillRank)}) : "
                + $"seuil {difficulty} ≤ auto {auto}, pas de jet (Livre II §7).";
        }
    }
}
