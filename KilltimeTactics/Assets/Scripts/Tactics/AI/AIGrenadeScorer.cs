using System;
using Killtime.Core.Combat;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// Scoring pur des grenades utilitaires pour l'IA (RD-106).
    /// Avant : pénalité forfaitaire -6 ⇒ fumigènes / flash / gaz jamais joués.
    /// Après : chaque famille est valorisée selon son effet réel —
    /// écran de fumée (rupture de vue), flash (Aveugle -4 tir avant assaut),
    /// zone persistante (DoT × tours, alliés masqués ignorés).
    /// Pur C# EditMode : aucune scène, aucun singleton.
    /// Le contrôleur fournit les comptages (foes/allies/LOS/masques) déjà filtrés
    /// brouillard (fog symétrique §25.4) ; ici seules les formules vivent.
    /// </summary>
    public static class AIGrenadeScorer
    {
        /// <summary>Seuil historique conservé pour le létal (inchangé).</summary>
        public const float LethalPlayThreshold = 18f;
        /// <summary>
        /// Seuil utilitaire : aveugler ou enfumer 1-2 tireurs vaut ~1 attaque
        /// niée, comparable à une grenade létale sur une cible.
        /// </summary>
        public const float UtilityPlayThreshold = 12f;

        // --- Classification (miroir des kinds Armory.json) ---

        public static bool IsSmoke(string kind) => SmokeScreen.BlocksSight(kind);

        public static bool IsFlashKind(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            string k = kind.Trim().ToLowerInvariant();
            return k.Contains("flash") || k.Contains("sonique") || k.Contains("stun")
                || k.Contains("aveugl");
        }

        public static bool IsPersistent(string kind, int zoneTurns, int blastRadius)
            => zoneTurns > 0 && blastRadius > 0 && !IsSmoke(kind);

        public static float ExpectedDirect(int baseDamage, int diceCount)
            => baseDamage + diceCount * 5.5f;

        // --- Fumigène (ex. M18 : 0 dmg, R2, 2 tours, Aveugle) ---

        /// <param name="blockedFoes">hostiles dont la vue vers l'acteur sera coupée.</param>
        /// <param name="foesInBlast">ennemis dans le souffle (aveuglés dedans).</param>
        /// <param name="alliesInBlast">alliés dans le souffle (pénalité douce : la fumée ne blesse pas).</param>
        public static float ScoreSmoke(
            int blockedFoes, int foesInBlast, int alliesInBlast,
            TacticalPosture posture, float actorHpRatio)
        {
            float score = blockedFoes * 7f + foesInBlast * 4f - alliesInBlast * 3f;
            if (posture == TacticalPosture.TacticalRetreat || posture == TacticalPosture.HitAndRun)
                score += 6f;
            else if (posture == TacticalPosture.SupportAndHeal)
                score += 4f;
            else if (posture == TacticalPosture.AlertedPatrol)
                score += 2f;
            if (actorHpRatio < 0.5f)
                score += 4f;
            return score;
        }

        // --- Flash instantané (ex. M84 : 2 dmg, Aveugle/Sourd/Etourdi) ---

        /// <remarks>
        /// Aveugle = -4 tirs distance, -2 mêlée/défense : idéal avant assaut.
        /// Les alliés dans le souffle sont aveuglés aussi ⇒ blocage dur sauf AllInLethal
        /// (géré par le contrôleur, pas ici).
        /// </remarks>
        public static float ScoreFlash(int foesInBlast, float expectedDirect, TacticalPosture posture)
        {
            float score = foesInBlast * 9f + Math.Max(0f, expectedDirect);
            if (posture == TacticalPosture.CalculatedStrike
                || posture == TacticalPosture.FlankAndSuppress
                || posture == TacticalPosture.AllInLethal)
                score += 6f;
            return score;
        }

        // --- Zone persistante : gaz / cryo / stase / pestilentiel ---

        /// <param name="exposedAllies">
        /// alliés sans protection dans la zone. Pour un gaz conventionnel, un allié
        /// au masque filtrant est immunisé (dégâts ×0) ⇒ le contrôleur le retire
        /// déjà de ce compte. Arcanotech : dégâts /2 seulement ⇒ toujours compté.
        /// </param>
        public static float ScorePersistent(
            int foesInBlast, float expectedDirect, int zoneDpt, int zoneTurns,
            bool hasHardControl, int exposedAllies, bool finishingBlow)
        {
            float score = foesInBlast * (10f + expectedDirect + zoneDpt * Math.Max(1, zoneTurns) * 4f);
            if (hasHardControl)
                score += foesInBlast * 6f; // Paralyse / Immobilisé multi-tours
            if (finishingBlow)
                score += 10f;
            score -= exposedAllies * 25f;
            return score;
        }

        public static bool HasHardControl(string statusesCsv)
        {
            if (string.IsNullOrEmpty(statusesCsv)) return false;
            string s = statusesCsv.ToLowerInvariant();
            return s.Contains("paralys") || s.Contains("immobilis");
        }

        // --- Létal : formule historique verbatim (le -6 utilitaire est parti avec RD-106) ---

        public static float ScoreLethal(int foesInBlast, float expectedDirect, int alliesInBlast, bool finishingBlow)
        {
            float score = foesInBlast * (10f + expectedDirect) - alliesInBlast * 25f;
            if (finishingBlow)
                score += 10f;
            return score;
        }
    }
}
