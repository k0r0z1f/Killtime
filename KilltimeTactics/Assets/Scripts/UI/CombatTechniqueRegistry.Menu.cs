using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Rules;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Construction des actions du menu contextuel (GetTechniqueActions). — extrait sans modification depuis CombatTechniqueRegistry.cs.
    /// </summary>
    public static partial class CombatTechniqueRegistry
    {

        // =====================================================================
        // CONSTRUCTION DES ACTIONS DU MENU CONTEXTUEL
        // =====================================================================

        /// <summary>
        /// Construit les actions de techniques débloquées par l'acteur, filtrées
        /// selon la cible du menu (ennemi : Clé/Analyse/Regard ; soi : zones ; allié : garde du corps).
        /// </summary>
        public static List<CombatAction> GetTechniqueActions(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            var actions = new List<CombatAction>();
            if (!IsAliveUnit(actor) || target == null) return actions;

            bool isSelf = (actor == target);
            bool isEnemy = IsEnemyOf(actor, target);
            bool targetIsAlive = IsAliveUnit(target);

            // Comptages de zone figés à l'ouverture du menu : le radial bloque toute
            // action extérieure tant qu'il est ouvert, donc l'état est stable jusqu'au
            // clic (l'exécution re-valide en live via les Execute*). Évite un scan de
            // scène par frame dans les Conditions du menu.
            int shakableAtOpen = isSelf ? CountEnemiesInRange(actor, AuraRange, onlyNotDestabilised: true) : 0;
            int alliesMissingPAAtOpen = isSelf ? CountAlliesInRange(actor, AuraRange, onlyMissingPA: true) : 0;
            int alliesNoLineAtOpen = isSelf ? CountAlliesInRange(actor, AuraRange, onlyWithoutLineBonus: true) : 0;

            if (isEnemy && targetIsAlive)
            {
                if (actor.Stats.HasSpecialization(SpecCle))
                {
                    actions.Add(new CombatAction(
                        $"🥋 Clé d'Articulation ({CleCost} PA)",
                        "Prise de soumission au contact : 3 dégâts bruts (ignore l'armure) + Immobilisé 1 tour (Paralysé si cible déjà Déstabilisée + Rupture Ligamentaire).",
                        ActionCategory.TechniquesDeSpecialisation,
                        CleCost,
                        (act, tgt) => CanUseCle(act, tgt),
                        (act, tgt) => ExecuteCle(act, tgt, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecAnalyse))
                {
                    bool coordinated = actor.Stats.HasSpecialization(SpecTirCoordonne);
                    actions.Add(new CombatAction(
                        $"📡 Analyse de Faille ({AnalyseCost} PA)",
                        $"Détection du point faible (≤{AnalyseRange} cases) : la prochaine touche ignore l'encaissement{(coordinated ? " + Déstabilisé (Tir Coordonné)" : "")}.",
                        ActionCategory.TechniquesDeSpecialisation,
                        AnalyseCost,
                        (act, tgt) => CanUseAnalyse(act, tgt),
                        (act, tgt) => ExecuteAnalyse(act, tgt, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecRegard))
                {
                    actions.Add(new CombatAction(
                        $"👁️ Regard de Prédateur ({RegardCost} PA)",
                        $"Défi en duel singulier (≤{RegardRange} cases, Intimidation vs Intuition) : Déstabilisé + la cible doit vous attaquer.",
                        ActionCategory.TechniquesDeSpecialisation,
                        RegardCost,
                        (act, tgt) => CanUseRegard(act, tgt),
                        (act, tgt) => ExecuteRegard(act, tgt, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecChuteDePression))
                {
                    actions.Add(new CombatAction(
                        "❄️ Vide Calculant : Chute de Pression (2 PA)",
                        "Raréfaction d'air (≤4 cases) : 4 dégâts Absolus (ignore armure) + cible Déstabilisée.",
                        ActionCategory.TechniquesDeSpecialisation,
                        2,
                        (act, tgt) => CanUseChuteDePression(act, tgt),
                        (act, tgt) => ExecuteChuteDePression(act, tgt, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecSuggestionsBreves))
                {
                    actions.Add(new CombatAction(
                        "🧠 Suggestions Brèves (2 PA)",
                        "Aiguillage synaptique (≤4 cases) : draine 1 PA de réserve et applique Déstabilisé.",
                        ActionCategory.TechniquesDeSpecialisation,
                        2,
                        (act, tgt) => CanUseSuggestionsBreves(act, tgt),
                        (act, tgt) => ExecuteSuggestionsBreves(act, tgt, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecArretVectoriel))
                {
                    actions.Add(new CombatAction(
                        "🛑 Arrêt Vectoriel (1 PA)",
                        "Télékinésie mathématique (≤4 cases) : fige sur place (Immobilisé 1 tour) et inflige 3 dégâts Absolus.",
                        ActionCategory.TechniquesDeSpecialisation,
                        1,
                        (act, tgt) => CanUseArretVectoriel(act, tgt),
                        (act, tgt) => ExecuteArretVectoriel(act, tgt, arena)
                    ));
                }
            }

            if (!isSelf && !isEnemy && targetIsAlive)
            {
                if (actor.Stats.HasSpecialization(SpecTenir) || actor.Stats.HasSpecialization(SpecGardeDuCorps))
                {
                    bool isAlreadyGuarded = SkillTechniqueState.IsGuardedBy(target.Stats, actor.Stats);
                    if (isAlreadyGuarded)
                    {
                        actions.Add(new CombatAction(
                            "🛡️ Garde du corps : Rompre la garde (0 PA)",
                            $"Cesse de veiller sur {target.Stats.Name} en tant que garde du corps.",
                            ActionCategory.TechniquesDeSpecialisation,
                            0,
                            (act, tgt) => true,
                            (act, tgt) =>
                            {
                                SkillTechniqueState.ClearBodyguard(act.Stats);
                                act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("Garde rompue", Color.gray);
                                arena?.Log($"🛡️ <b>{act.Stats.Name}</b> cesse de monter la garde pour <b>{tgt.Stats.Name}</b>.");
                            }
                        ));
                    }
                    else if (actor.CurrentCoords.DistanceTo(target.CurrentCoords) <= InterceptRange)
                    {
                        actions.Add(new CombatAction(
                            "🛡️ Garde du corps (0 PA, contact)",
                            $"Veille sur {target.Stats.Name} : vous prendrez les coups à sa place au contact (coût 1 PA réaction).",
                            ActionCategory.TechniquesDeSpecialisation,
                            0,
                            (act, tgt) => CanSetBodyguard(act, tgt),
                            (act, tgt) => ExecuteSetBodyguard(act, tgt, arena)
                        ));
                    }
                }
            }

            if (isSelf)
            {
                if (actor.Stats.HasSpecialization(SpecRugissement))
                {
                    actions.Add(new CombatAction(
                        $"😱 Rugissement de Terreur ({RugissementCost} PA)",
                        $"Pression psychologique de zone (ennemis ≤{AuraRange} cases, duel Intimidation vs Intuition chacun) : Déstabilisé 1 tour.",
                        ActionCategory.TechniquesDeSpecialisation,
                        RugissementCost,
                        (act, tgt) => IsAliveUnit(act) && act.Stats.HasSpecialization(SpecRugissement)
                            && act.Stats.CurrentActionPoints >= RugissementCost && shakableAtOpen > 0,
                        (act, tgt) => ExecuteRugissement(act, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecMener))
                {
                    actions.Add(new CombatAction(
                        $"📢 Mener : Commandement ({MenerCost} PA)",
                        $"Coordination d'escouade (alliés ≤{AuraRange} cases) : +1 PA réflexe à chacun (zone, vs Ordre Tactique mono-cible).",
                        ActionCategory.TechniquesDeSpecialisation,
                        MenerCost,
                        (act, tgt) => IsAliveUnit(act) && act.Stats.HasSpecialization(SpecMener)
                            && act.Stats.CurrentActionPoints >= MenerCost && alliesMissingPAAtOpen > 0,
                        (act, tgt) => ExecuteMenerZone(act, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecTenir))
                {
                    actions.Add(new CombatAction(
                        $"🛡️ Tenir la Ligne ! ({TenirCost} PA)",
                        $"Discipline de fer (alliés ≤{AuraRange} cases) : +{TenirArmorBonus} armure à chacun jusqu'à votre prochain tour.",
                        ActionCategory.TechniquesDeSpecialisation,
                        TenirCost,
                        (act, tgt) => IsAliveUnit(act) && act.Stats.HasSpecialization(SpecTenir)
                            && act.Stats.CurrentActionPoints >= TenirCost && alliesNoLineAtOpen > 0,
                        (act, tgt) => ExecuteTenir(act, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecCommandementTectonique))
                {
                    actions.Add(new CombatAction(
                        "⚡ Commandement Tectonique (2 PA)",
                        "Manœuvre coordonnée (alliés ≤4 cases) : +1 PA immédiat pour le repositionnement.",
                        ActionCategory.TechniquesDeSpecialisation,
                        2,
                        (act, tgt) => CanUseCommandementTectonique(act),
                        (act, tgt) => ExecuteCommandementTectonique(act, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecPasDeRetraite))
                {
                    actions.Add(new CombatAction(
                        "🛡️ Pas de Retraite ! (2 PA)",
                        "Discipline morale (alliés ≤3 cases) : purge immédiatement la panique, Déstabilisé et Étourdi.",
                        ActionCategory.TechniquesDeSpecialisation,
                        2,
                        (act, tgt) => CanUsePasDeRetraite(act),
                        (act, tgt) => ExecutePasDeRetraite(act, arena)
                    ));
                }
            }

            return actions;
        }
    }
}
