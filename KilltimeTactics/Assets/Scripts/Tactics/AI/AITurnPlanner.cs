using System.Collections.Generic;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// RD-115 (lot AITurnPlanner) : intentions atomiques du tour d'IA, dans
    /// l'ordre verbatim de la boucle ExecuteAITurn d'origine :
    /// 1. Reddition 2. Déroute 3. Soutien médical 4. Soutien allié
    /// 5. Cible nulle (observation / patrouille / break) 6. Retraite
    /// 7. Ralliement 8. Technique de combat 9. Manœuvre Livre XII
    /// 10. DuoTech auto 11. TEEP / dégagement 12. Grenade 13. Sort
    /// 14. Intimidation 15. Entretien 16. Balise laser 17. Charge
    /// 18. Frappe titanesque 19. Bousculade 20. Lancer d'arme 21. Flanking
    /// 22. Attaque prédictive (dont souffle d'urgence) 23. Approche
    /// 24. Ordre tactique 25. Guet / fin de tour.
    /// Le contrôleur n'exécute que des coroutines ; le planner ne fait que
    /// prioriser (aucun MonoBehaviour, aucune coroutine, aucun singleton).
    /// </summary>
    public enum AIIntent
    {
        EndTurn_Surrender,      // 1. Reddition : fin de tour immédiate.
        PanickedFlee,           // 2. Déroute Agonisant : fuite panique, break.
        MedicalSupport,         // 3. Soutien médical d'urgence (Livre VI).
        AllySupport,            // 4. Soutien tactique allié (corde, radio...).
        ActiveObservation,      // 5a. Observation active face au vide.
        AlertedPatrol,          // 5b. Patrouille alertée vers bruit/lumière.
        EndTurn_NoContact,      // 5c. Aucune cible ni patrouille : break.
        TacticalRetreat,        // 6. Retraite tactique (+ verrou guet), break.
        RallyWoundedAlly,       // 7. Ralliement SupportAndHeal vers l'allié.
        CombatTechnique,        // 8. Techniques de spécialisation (Livre III).
        LivreXIManeuver,        // 9. Manœuvres PNJ/automates (break si guet).
        DuoTechAuto,            // 10. Duo-Tech auto (RD-105).
        TeepBreak,              // 11a. Rupture d'engagement au contact (ranged).
        DisengageStep,          // 11b. Dégagement au contact (ranged).
        Grenade,                // 12. Grenades de zone (Livre VIII §31.3).
        BestSpell,              // 13. Sort arcanotech rentable (Livre IV).
        Intimidation,           // 14. Intimidation & provocation (Livre III).
        SelfUpkeep,             // 15. Entretien & buffs personnels.
        LaserDesignation,       // 16. Balise laser anti-blindage.
        ChargeAttack,           // 17. Charge au contact (mêlée, dist > 1).
        TitanSlam,              // 18. Frappe titanesque de zone / saisie.
        Shove,                  // 19. Bousculade / coup de bouclier (dist ≤ 1).
        WeaponThrow,            // 20. Lancer d'arme, ultime recours (dist ≥ 2).
        FlankToCover,           // 21. Flanking / prise de couvert avant frappe.
        PredictiveAttack,       // 22a. Attaque prédictive (duel aveugle).
        EmergencyBreath,        // 22b. Souffle d'urgence pour attaquer.
        EndTurn_NoAttackOption, // 22c. À portée mais rien à tenter : break.
        AdvanceToContact,       // 23. Approche ligne de tir / engagement.
        TacticalOrder,          // 24. Ordre tactique fin de tour (fallthrough).
        Overwatch,              // 25a. Guet / Overwatch du surplus.
        EndTurn,                // 25b. Fin de tour générique (break).
    }

    /// <summary>
    /// RD-115 : instantané pur d'une itération de tour d'IA. Le contrôleur
    /// remplit chaque champ depuis ses lectures live (stats, grille, config)
    /// ; le planner n'y lit que des valeurs, sans effet de bord.
    /// Les noms reprennent les locaux verbatim d'ExecuteAITurn.
    /// </summary>
    public struct AITurnSnapshot
    {
        public TacticalPosture Posture; // EvaluateTacticalPosture(actor,target,dist)
        public bool IsRanged;           // HasRangedWeapon + blocage sprint RD-038
        public int Dist;                // MinDistanceBetweenUnits (empreintes)
        public CoverType TargetCover;   // GetCoverLevel(pos → cible)
        public bool InAttackRange;      // ranged ? dist≤max+LOS : dist≤1
        public int ActionPoints;        // Stats.CurrentActionPoints live
        public int DefensiveReserve;    // GetRequiredDefensiveReserve(posture)
        public bool IsWatching;         // OverwatchState.IsWatching
        public bool ArenaAvailable;     // _arena != null
        public int OverwatchAPCost;     // CoreRulesConfig.Instance (RD-030)
        public bool HasMartialArts;     // spé "Arts Martiaux" (TEEP)
        public bool CanUnarmedAttack;   // CanAttack(GetSkillDie(MainsNues))
    }

    /// <summary>
    /// RD-115 : priorités pures du tour d'IA, extraites verbatim d'ExecuteAITurn.
    /// Quatre phases, dans l'ordre d'origine :
    /// pré-cible (1-4, avant EvaluateBestTarget), sans cible (5),
    /// engagement (6-11), frappe (12-25, après recalcul contact).
    /// La frappe est planifiée sur un snapshot FRAIS (post-TEEP/dégagement)
    /// car l'original recalcule dist/LOS/portée avant les seuils 17-22.
    /// Testable en EditMode : aucune grille, aucun singleton, aucun Unity.
    /// </summary>
    public static class AITurnPlanner
    {
        /// <summary>
        /// Phases 1-4 (avant EvaluateBestTarget, verbatim) : reddition puis
        /// déroute sont terminales ; sinon soutiens médicaux puis alliés
        /// (échec = poursuite vers l'évaluation de cible).
        /// </summary>
        public static IReadOnlyList<AIIntent> BuildPreTargetPlan(bool isSurrendered, bool isAgonisant)
        {
            if (isSurrendered)
                return new[] { AIIntent.EndTurn_Surrender };
            if (isAgonisant)
                return new[] { AIIntent.PanickedFlee };
            return new[] { AIIntent.MedicalSupport, AIIntent.AllySupport };
        }

        /// <summary>
        /// Phase 5, cible nulle (verbatim) : observation active, puis
        /// patrouille alertée, sinon fin de tour (break d'origine).
        /// </summary>
        public static IReadOnlyList<AIIntent> BuildNoTargetPlan()
        {
            return new[]
            {
                AIIntent.ActiveObservation,
                AIIntent.AlertedPatrol,
                AIIntent.EndTurn_NoContact,
            };
        }

        /// <summary>
        /// Phases 6-11 (verbatim). La retraite est un plan terminal à elle
        /// seule (break d'origine). Le ralliement n'est proposé qu'en posture
        /// SupportAndHeal (chemin nul = poursuite, comme l'original).
        /// TEEP vs dégagement : seuils d'origine (isRanged &amp; dist == 1,
        /// arts martiaux + attaque à mains nues + PA ≥ 2, sinon PA ≥ 3).
        /// </summary>
        public static IReadOnlyList<AIIntent> BuildEngagementPlan(in AITurnSnapshot snapshot)
        {
            // 6. RETRAITE TACTIQUE : exécution puis break (verrou guet inclus).
            if (snapshot.Posture == TacticalPosture.TacticalRetreat)
                return new[] { AIIntent.TacticalRetreat };

            var plan = new List<AIIntent>(8);

            // 7. SOUTIEN : ralliement vers l'allié blessé (posture dédiée).
            if (snapshot.Posture == TacticalPosture.SupportAndHeal)
                plan.Add(AIIntent.RallyWoundedAlly);

            // 8-10. Registre joueur : techniques, Livre XII, Duo-Tech.
            plan.Add(AIIntent.CombatTechnique);
            plan.Add(AIIntent.LivreXIManeuver);
            plan.Add(AIIntent.DuoTechAuto);

            // 11. CONTACT DIRECT (ranged, dist == 1) : TEEP ou dégagement.
            if (snapshot.IsRanged && snapshot.Dist == 1)
            {
                if (snapshot.HasMartialArts && snapshot.CanUnarmedAttack && snapshot.ActionPoints >= 2)
                    plan.Add(AIIntent.TeepBreak);
                else if (snapshot.ActionPoints >= 3)
                    plan.Add(AIIntent.DisengageStep);
            }

            return plan;
        }

        /// <summary>
        /// Phases 12-25 (verbatim), sur snapshot frais post-contact :
        /// grenade, sort, intimidation, entretien, laser, charge (!ranged,
        /// dist &gt; 1), titan, bousculade (dist ≤ 1), lancer (dist ≥ 2),
        /// flanking (PA ≥ 3, couvert 3/4 ou hors portée), attaque / souffle /
        /// break à portée, approche (PA ≥ 1), ordre (PA ≥ 2), guet du surplus,
        /// fin de tour. L'attaque (ou le souffle, ou le break) termine le
        /// plan : l'original quitte l'itération dans tous ces cas.
        /// </summary>
        public static IReadOnlyList<AIIntent> BuildStrikePlan(in AITurnSnapshot snapshot)
        {
            var plan = new List<AIIntent>(16);

            // 12-16. Registre joueur à distance / avant frappe.
            plan.Add(AIIntent.Grenade);
            plan.Add(AIIntent.BestSpell);
            plan.Add(AIIntent.Intimidation);
            plan.Add(AIIntent.SelfUpkeep);
            plan.Add(AIIntent.LaserDesignation);

            // 17. CHARGE AU CONTACT (mêlée, dist > 1).
            if (!snapshot.IsRanged && snapshot.Dist > 1)
                plan.Add(AIIntent.ChargeAttack);

            // 18. FRAPPE TITANESQUE.
            plan.Add(AIIntent.TitanSlam);

            // 19. BOUSCULADE (dist ≤ 1).
            if (snapshot.Dist <= 1)
                plan.Add(AIIntent.Shove);

            // 20. LANCER D'ARME (dist ≥ 2).
            if (snapshot.Dist >= 2)
                plan.Add(AIIntent.WeaponThrow);

            // 21. FLANKING (PA ≥ 3, couvert 3/4 ou hors portée).
            if (snapshot.ActionPoints >= 3
                && (snapshot.TargetCover == CoverType.ThreeQuarters || !snapshot.InAttackRange))
                plan.Add(AIIntent.FlankToCover);

            // 22. ATTAQUE PRÉDICTIVE : à portée, l'original tranche en
            // if/elif/else sur des lectures live (attaque, puis souffle,
            // puis break). Le planner émet le trio dans l'ordre ; l'exécuteur
            // réapplique les seuils verbatim. Termine toujours l'itération.
            if (snapshot.InAttackRange)
            {
                plan.Add(AIIntent.PredictiveAttack);
                plan.Add(AIIntent.EmergencyBreath);
                plan.Add(AIIntent.EndTurn_NoAttackOption);
                return plan;
            }

            // 23. APPROCHE (PA ≥ 1).
            if (snapshot.ActionPoints >= 1)
                plan.Add(AIIntent.AdvanceToContact);

            // 24. ORDRE TACTIQUE (PA ≥ 2, fallthrough vers le guet).
            if (snapshot.ActionPoints >= 2)
                plan.Add(AIIntent.TacticalOrder);

            // 25. GUET du surplus, puis fin de tour systématique.
            if (snapshot.IsRanged && snapshot.ArenaAvailable && !snapshot.IsWatching
                && snapshot.ActionPoints >= snapshot.OverwatchAPCost + snapshot.DefensiveReserve)
                plan.Add(AIIntent.Overwatch);
            plan.Add(AIIntent.EndTurn);

            return plan;
        }
    }
}
