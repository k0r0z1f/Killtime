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
    /// Registre des techniques de spécialisation activables en combat (Livre III).
    /// Chaque technique correspond à une spécialisation du compendium
    /// (CharacterProgressionManager) dont l'effet mécanique est jouable au tour par tour.
    /// Utilisé à la fois par le menu contextuel joueur (CombatActionRegistry) et par
    /// l'IA tactique (TacticalAIController) : une seule implémentation, deux appelants.
    ///
    /// Coûts fixes (Livre III) : 2 PA par technique, débités via ConsumeActionPoints
    /// (Ralenti x2 appliqué automatiquement). Toute réussite coche la progression
    /// organique de la compétence source (Livre I §5).
    /// </summary>
    public static class CombatTechniqueRegistry
    {
        public const int CleCost = 2;
        public const int AnalyseCost = 2;
        public const int RugissementCost = 2;
        public const int RegardCost = 2;
        public const int MenerCost = 2;
        public const int TenirCost = 2;
        public const int InterceptCost = 1;
        public const int InterceptRange = 1;

        public const int CleRawDamage = 3;
        public const int AnalyseRange = 10;
        public const int RegardRange = 6;
        public const int AuraRange = 3;
        public const int TenirArmorBonus = 2;

        public const string SpecCle = "Arts Martiaux : Clé d'Articulation";
        public const string SpecRupture = "Arts Martiaux : Rupture Ligamentaire";
        public const string SpecAnalyse = "Analyse de Faille";
        public const string SpecTirCoordonne = "Analyse de Faille : Tir Coordonné";
        public const string SpecRugissement = "Intimider : Rugissement de Terreur";
        public const string SpecRegard = "Intimider : Regard de Prédateur";
        public const string SpecMener = "Mener (Commandement)";
        public const string SpecTenir = "Mener : Tenir la Ligne !";
        public const string SpecGardeDuCorps = "Garde du corps";
        public const string SpecCommandementTectonique = "Commandement Tectonique";
        public const string SpecPasDeRetraite = "Commandement Tectonique : Pas de Retraite !";
        public const string SpecChuteDePression = "Vide Calculant : Chute de Pression";
        public const string SpecSuggestionsBreves = "Pare-feu Psychologique : Suggestions Brèves";
        public const string SpecArretVectoriel = "Arrêt Vectoriel";

        // =====================================================================
        // REQUÊTES GÉNÉRIQUES
        // =====================================================================

        public static bool HasAnyCombatTechnique(TacticalUnit actor)
        {
            if (actor?.Stats == null) return false;
            var s = actor.Stats;
            return s.HasSpecialization(SpecCle)
                || s.HasSpecialization(SpecAnalyse)
                || s.HasSpecialization(SpecRugissement)
                || s.HasSpecialization(SpecRegard)
                || s.HasSpecialization(SpecMener)
                || s.HasSpecialization(SpecTenir)
                || s.HasSpecialization(SpecGardeDuCorps)
                || s.HasSpecialization(SpecCommandementTectonique)
                || s.HasSpecialization(SpecPasDeRetraite)
                || s.HasSpecialization(SpecChuteDePression)
                || s.HasSpecialization(SpecSuggestionsBreves)
                || s.HasSpecialization(SpecArretVectoriel);
        }

        private static List<TacticalUnit> GetAllUnits()
        {
            return new List<TacticalUnit>(Object.FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude));
        }

        private static bool IsEnemyOf(TacticalUnit actor, TacticalUnit other)
        {
            if (actor == null || other == null) return false;
            return other.IsPlayerControlled != actor.IsPlayerControlled;
        }

        private static bool IsAliveUnit(TacticalUnit u)
        {
            return u != null && u.Stats != null && u.Stats.IsAlive;
        }

        public static int CountEnemiesInRange(TacticalUnit actor, int range, bool onlyNotDestabilised = false)
        {
            if (actor?.Stats == null) return 0;
            int count = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || !IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > range) continue;
                if (onlyNotDestabilised && (u.Stats.ActiveStatus & StatusEffect.Destabilise) != 0) continue;
                count++;
            }
            return count;
        }

        public static int CountAlliesInRange(TacticalUnit actor, int range, bool onlyMissingPA = false, bool onlyWithoutLineBonus = false)
        {
            if (actor?.Stats == null) return 0;
            int count = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || u == actor || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > range) continue;
                if (onlyMissingPA && u.Stats.CurrentActionPoints >= u.Stats.MaxActionPoints) continue;
                if (onlyWithoutLineBonus && SkillTechniqueState.HasLineBonus(u.Stats)) continue;
                count++;
            }
            return count;
        }

        private static void TickProgression(CharacterSheet sheet, SkillType skill, CombatDevArena arena)
        {
            if (sheet == null) return;
            if (CharacterProgressionManager.RegisterSuccessfulSkillUse(sheet, skill, out string msg, out _, true)
                && !string.IsNullOrEmpty(msg))
            {
                arena?.Log(msg);
            }
        }

        // =====================================================================
        // 1. CLÉ D'ARTICULATION (Mains Nues — 2 PA, contact)
        // =====================================================================

        public static bool CanUseCle(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target)) return false;
            if (!IsEnemyOf(actor, target)) return false;
            if (actor.CurrentCoords.DistanceTo(target.CurrentCoords) > 1) return false;
            if (!actor.Stats.HasSpecialization(SpecCle)) return false;
            return actor.Stats.CurrentActionPoints >= CleCost;
        }

        public static bool ExecuteCle(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseCle(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(CleCost)) return false;

            bool wasDestabilised = (target.Stats.ActiveStatus & StatusEffect.Destabilise) != 0;

            // 3 dégâts bruts sans réduction d'armure (Livre III).
            int remaining = target.Stats.CurrentHealth - CleRawDamage;
            if (remaining <= 0)
            {
                target.Stats.EvaluateFatalBlow(BodyPart.Torse, CleRawDamage);
            }
            else
            {
                target.Stats.CurrentHealth = remaining;
            }

            // Rupture Ligamentaire : cible déjà Déstabilisée → Paralysé 1 tour,
            // sinon Immobilisé 1 tour (effet de base de la Clé).
            string statusLabel;
            if (wasDestabilised && actor.Stats.HasSpecialization(SpecRupture))
            {
                target.Stats.ApplyStatus(StatusEffect.Paralyse, 1);
                statusLabel = "PARALYSÉ !";
            }
            else
            {
                target.Stats.ApplyStatus(StatusEffect.Immobilise, 1);
                statusLabel = "IMMOBILISÉ !";
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🥋 Clé d'Articulation (-2 PA)", Color.cyan);
            var tgtVis = target.GetComponent<TacticalUnitVisual>();
            tgtVis?.TriggerHitFlash();
            tgtVis?.SpawnFloatingText($"-{CleRawDamage} bruts + {statusLabel}", Color.red);
            arena?.Log($"🥋 <b>{actor.Stats.Name}</b> verrouille <b>{target.Stats.Name}</b> en <b>Clé d'Articulation</b> (-2 PA) : {CleRawDamage} dégâts bruts + [{statusLabel}] !");
            arena?.RecordChronoSnapshot($"Clé d'Articulation : {actor.Stats.Name} -> {target.Stats.Name}");

            TickProgression(actor.Sheet, SkillType.MainsNues, arena);
            return true;
        }

        // =====================================================================
        // 2. ANALYSE DE FAILLE (Tactique & Stratégie — 2 PA, ≤10 cases)
        // =====================================================================

        public static bool CanUseAnalyse(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target)) return false;
            if (!IsEnemyOf(actor, target)) return false;
            if (actor.CurrentCoords.DistanceTo(target.CurrentCoords) > AnalyseRange) return false;
            if (!actor.Stats.HasSpecialization(SpecAnalyse)) return false;
            if (SkillTechniqueState.IsFlawExposed(target.Stats)) return false;
            return actor.Stats.CurrentActionPoints >= AnalyseCost;
        }

        public static bool ExecuteAnalyse(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseAnalyse(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(AnalyseCost)) return false;

            SkillTechniqueState.ApplyExposedFlaw(actor.Stats, target.Stats);

            // Tir Coordonné : le guidage balistique déstabilise la cible exposée
            // (équivalent mécanique du +2 d'attaque d'escouade, Livre III).
            bool coordinated = actor.Stats.HasSpecialization(SpecTirCoordonne);
            if (coordinated)
            {
                target.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("📡 Analyse de Faille (-2 PA)", new Color(0.2f, 0.85f, 1.0f));
            target.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("FAILLE EXPOSÉE !", Color.yellow);
            arena?.Log($"📡 <b>{actor.Stats.Name}</b> expose la faille de <b>{target.Stats.Name}</b> (-2 PA) : la prochaine touche ignore l'encaissement{(coordinated ? " + [Déstabilisé] (Tir Coordonné)" : "")} !");
            arena?.RecordChronoSnapshot($"Analyse de Faille : {actor.Stats.Name} -> {target.Stats.Name}");

            TickProgression(actor.Sheet, SkillType.TactiqueStrategie, arena);
            return true;
        }

        // =====================================================================
        // 3. RUGISSEMENT DE TERREUR (Intimidation — 2 PA, zone 3 cases, sur soi)
        // =====================================================================

        public static bool CanUseRugissement(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecRugissement)) return false;
            if (actor.Stats.CurrentActionPoints < RugissementCost) return false;
            return CountEnemiesInRange(actor, AuraRange, onlyNotDestabilised: true) > 0;
        }

        public static bool ExecuteRugissement(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUseRugissement(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(RugissementCost)) return false;

            var calc = new CombatCalculator();
            int shaken = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || !IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > AuraRange) continue;
                if ((u.Stats.ActiveStatus & StatusEffect.Destabilise) != 0) continue;

                var duel = calc.ResolveOpposedCheck(actor.Stats, SkillType.Intimidation, u.Stats, SkillType.Intuition, 0, 0, defenderAutoStakes: true);
                arena?.Log($"😱 <b>{actor.Stats.Name}</b> rugit sur <b>{u.Stats.Name}</b> :\n   {duel.CombatLog}");
                if (duel.AttackerWins)
                {
                    u.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                    var vis = u.GetComponent<TacticalUnitVisual>();
                    vis?.TriggerHitFlash();
                    vis?.SpawnFloatingText("TERRORISÉ ! (-2)", Color.yellow);
                    shaken++;
                }
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"😱 Rugissement (-2 PA, {shaken} secoué(s))", new Color(1f, 0.5f, 0.1f));
            arena?.Log($"😱 <b>{actor.Stats.Name}</b> déchaîne un <b>Rugissement de Terreur</b> (-2 PA) : {shaken} ennemi(s) Déstabilisé(s) à moins de {AuraRange} cases !");
            arena?.RecordChronoSnapshot($"Rugissement de Terreur : {actor.Stats.Name} ({shaken})");

            if (shaken > 0) TickProgression(actor.Sheet, SkillType.Intimidation, arena);
            return true;
        }

        // =====================================================================
        // 4. REGARD DE PRÉDATEUR (Intimidation — 2 PA, ≤6 cases)
        // =====================================================================

        public static bool CanUseRegard(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target)) return false;
            if (!IsEnemyOf(actor, target)) return false;
            if (actor.CurrentCoords.DistanceTo(target.CurrentCoords) > RegardRange) return false;
            if (!actor.Stats.HasSpecialization(SpecRegard)) return false;
            return actor.Stats.CurrentActionPoints >= RegardCost;
        }

        public static bool ExecuteRegard(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseRegard(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(RegardCost)) return false;

            var calc = new CombatCalculator();
            var duel = calc.ResolveOpposedCheck(actor.Stats, SkillType.Intimidation, target.Stats, SkillType.Intuition, 0, 0, defenderAutoStakes: true);
            arena?.Log($"👁️ <b>{actor.Stats.Name}</b> fixe <b>{target.Stats.Name}</b> en duel singulier (-2 PA) :\n   {duel.CombatLog}");

            var actVis = actor.GetComponent<TacticalUnitVisual>();
            var tgtVis = target.GetComponent<TacticalUnitVisual>();
            if (duel.AttackerWins)
            {
                target.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                SkillTechniqueState.ApplyTaunt(actor.Stats, target.Stats);
                tgtVis?.TriggerHitFlash();
                tgtVis?.SpawnFloatingText("PROVOQUÉ ! (doit t'attaquer)", Color.yellow);
                actVis?.SpawnFloatingText("👁️ Regard de Prédateur (-2 PA)", Color.cyan);
                if (target.Stats.CurrentActionPoints > 0)
                {
                    target.Stats.ConsumeActionPoints(1);
                    tgtVis?.SpawnFloatingText("-1 PA Réaction", new Color(1f, 0.6f, 0.2f));
                }
                TickProgression(actor.Sheet, SkillType.Intimidation, arena);
            }
            else
            {
                actVis?.SpawnFloatingText("Regard soutenu sans effet", Color.gray);
                tgtVis?.SpawnFloatingText("IMPASSIBLE", Color.cyan);
            }

            arena?.RecordChronoSnapshot($"Regard de Prédateur : {actor.Stats.Name} -> {target.Stats.Name}");
            return true;
        }

        // =====================================================================
        // 5. MENER / COMMANDEMENT ZONE (Leadership — 2 PA, alliés ≤3 cases, sur soi)
        // =====================================================================

        public static bool CanUseMenerZone(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecMener)) return false;
            if (actor.Stats.CurrentActionPoints < MenerCost) return false;
            return CountAlliesInRange(actor, AuraRange, onlyMissingPA: true) > 0;
        }

        public static bool ExecuteMenerZone(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUseMenerZone(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(MenerCost)) return false;

            int rallied = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || u == actor || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > AuraRange) continue;
                if (u.Stats.CurrentActionPoints >= u.Stats.MaxActionPoints) continue;
                u.Stats.CurrentActionPoints = Mathf.Min(u.Stats.MaxActionPoints, u.Stats.CurrentActionPoints + 1);
                u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("+1 PA Reçu", Color.cyan);
                rallied++;
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"📢 Commandement (-2 PA, {rallied} rallié(s))", new Color(0.2f, 0.85f, 1.0f));
            arena?.Log($"📢 <b>{actor.Stats.Name}</b> galvanise l'escouade (-2 PA) : +1 PA réflexe à {rallied} allié(s) à moins de {AuraRange} cases !");
            arena?.RecordChronoSnapshot($"Commandement : {actor.Stats.Name} ({rallied})");

            TickProgression(actor.Sheet, SkillType.Leadership, arena);
            return true;
        }

        // =====================================================================
        // 6. TENIR LA LIGNE ! (Leadership — 2 PA, alliés ≤3 cases, sur soi)
        // =====================================================================

        public static bool CanUseTenir(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecTenir)) return false;
            if (actor.Stats.CurrentActionPoints < TenirCost) return false;
            return CountAlliesInRange(actor, AuraRange, onlyWithoutLineBonus: true) > 0;
        }

        public static bool ExecuteTenir(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUseTenir(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(TenirCost)) return false;

            int braced = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || u == actor || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > AuraRange) continue;
                if (SkillTechniqueState.HasLineBonus(u.Stats)) continue;
                SkillTechniqueState.ApplyLineBonus(actor.Stats, u.Stats, TenirArmorBonus);
                u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"+{TenirArmorBonus} Armure (Ligne !)", Color.green);
                braced++;
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🛡️ Tenir la Ligne ! (-2 PA, {braced} soutenu(s))", Color.green);
            arena?.Log($"🛡️ <b>{actor.Stats.Name}</b> ordonne de <b>Tenir la Ligne !</b> (-2 PA) : +{TenirArmorBonus} armure à {braced} allié(s) jusqu'à son prochain tour !");
            arena?.RecordChronoSnapshot($"Tenir la Ligne ! : {actor.Stats.Name} ({braced})");

            TickProgression(actor.Sheet, SkillType.Leadership, arena);
            return true;
        }

        // =====================================================================
        // 7. TECHNIQUES HÉROÏQUES : THOMAS-0 & LUCAS-0 (RD-063)
        // =====================================================================

        public static bool CanUseCommandementTectonique(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecCommandementTectonique)) return false;
            return actor.Stats.CurrentActionPoints >= 2 && CountAlliesInRange(actor, 4) > 0;
        }

        public static bool ExecuteCommandementTectonique(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUseCommandementTectonique(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(2)) return false;

            int boosted = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || u == actor || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > 4) continue;
                u.Stats.CurrentActionPoints = Mathf.Min(u.Stats.MaxActionPoints + 2, u.Stats.CurrentActionPoints + 1);
                u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("+1 PA Manœuvre", Color.cyan);
                boosted++;
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⚡ Manœuvre Tectonique (-2 PA)", Color.cyan);
            arena?.Log($"⚡ <b>{actor.Stats.Name}</b> ordonne une manœuvre coordonnée (-2 PA) : +1 PA de mouvement accordé à {boosted} allié(s) !");
            TickProgression(actor.Sheet, SkillType.Leadership, arena);
            return true;
        }

        public static bool CanUsePasDeRetraite(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecPasDeRetraite)) return false;
            return actor.Stats.CurrentActionPoints >= 2;
        }

        public static bool ExecutePasDeRetraite(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUsePasDeRetraite(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(2)) return false;

            int purged = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > 3) continue;

                bool hadStatus = u.Stats.ActiveStatus.HasFlag(StatusEffect.Destabilise)
                              || u.Stats.ActiveStatus.HasFlag(StatusEffect.Etourdi)
                              || u.Stats.ActiveStatus.HasFlag(StatusEffect.Agonisant);

                if (hadStatus)
                {
                    u.Stats.RemoveStatus(StatusEffect.Destabilise);
                    u.Stats.RemoveStatus(StatusEffect.Etourdi);
                    u.Stats.RemoveStatus(StatusEffect.Agonisant);
                    u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("PANIQUE PURGÉE !", Color.green);
                    purged++;
                }
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🛡️ Pas de Retraite ! (-2 PA)", Color.green);
            arena?.Log($"🛡️ <b>{actor.Stats.Name}</b> lance '<b>Pas de Retraite !</b>' (-2 PA) : panique et étourdissement purgés sur {purged} allié(s) !");
            TickProgression(actor.Sheet, SkillType.Leadership, arena);
            return true;
        }

        public static bool CanUseChuteDePression(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target) || !IsEnemyOf(actor, target)) return false;
            if (!actor.Stats.HasSpecialization(SpecChuteDePression)) return false;
            return actor.Stats.CurrentActionPoints >= 2 && actor.CurrentCoords.DistanceTo(target.CurrentCoords) <= 4;
        }

        public static bool ExecuteChuteDePression(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseChuteDePression(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(2)) return false;

            int remaining = target.Stats.CurrentHealth - 4;
            if (remaining <= 0)
            {
                target.Stats.EvaluateFatalBlow(BodyPart.Torse, 4);
            }
            else
            {
                target.Stats.CurrentHealth = remaining;
            }

            target.Stats.ApplyStatus(StatusEffect.Destabilise, 1);

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("❄️ Chute de Pression (-2 PA)", Color.cyan);
            var tgtVis = target.GetComponent<TacticalUnitVisual>();
            tgtVis?.TriggerHitFlash();
            tgtVis?.SpawnFloatingText("-4 PV Absolus (Vide) [Déstabilisé]", Color.cyan);

            if (!target.Stats.IsAlive)
            {
                tgtVis?.TriggerFallingBackDeath();
                arena?.AutoTargetNextAlive();
            }

            arena?.Log($"❄️ <b>{actor.Stats.Name}</b> raréfie l'air autour de <b>{target.Stats.Name}</b> (Vide Calculant : -2 PA) : 4 dégâts Absolus et [Déstabilisé] !");
            TickProgression(actor.Sheet, SkillType.MagieElementale, arena);
            return true;
        }

        public static bool CanUseSuggestionsBreves(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target) || !IsEnemyOf(actor, target)) return false;
            if (!actor.Stats.HasSpecialization(SpecSuggestionsBreves)) return false;
            return actor.Stats.CurrentActionPoints >= 2 && actor.CurrentCoords.DistanceTo(target.CurrentCoords) <= 4;
        }

        public static bool ExecuteSuggestionsBreves(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseSuggestionsBreves(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(2)) return false;

            target.Stats.CurrentActionPoints = Mathf.Max(0, target.Stats.CurrentActionPoints - 1);
            target.Stats.ApplyStatus(StatusEffect.Destabilise, 1);

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🧠 Suggestion Brève (-2 PA)", new Color(0.7f, 0.4f, 1f));
            var tgtVis = target.GetComponent<TacticalUnitVisual>();
            tgtVis?.SpawnFloatingText("-1 PA Réserve & Déstabilisé", Color.magenta);

            arena?.Log($"🧠 <b>{actor.Stats.Name}</b> perturbe les synapses de <b>{target.Stats.Name}</b> (Pare-feu Psychologique : -2 PA) : perte de 1 PA et [Déstabilisé] !");
            TickProgression(actor.Sheet, SkillType.MagieEsprit, arena);
            return true;
        }

        public static bool CanUseArretVectoriel(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target) || !IsEnemyOf(actor, target)) return false;
            if (!actor.Stats.HasSpecialization(SpecArretVectoriel)) return false;
            return actor.Stats.CurrentActionPoints >= 1 && actor.CurrentCoords.DistanceTo(target.CurrentCoords) <= 4;
        }

        public static bool ExecuteArretVectoriel(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseArretVectoriel(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(1)) return false;

            target.Stats.ApplyStatus(StatusEffect.Immobilise, 1);

            int remaining = target.Stats.CurrentHealth - 3;
            if (remaining <= 0)
            {
                target.Stats.EvaluateFatalBlow(BodyPart.Torse, 3);
            }
            else
            {
                target.Stats.CurrentHealth = remaining;
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🛑 Arrêt Vectoriel (-1 PA)", Color.cyan);
            var tgtVis = target.GetComponent<TacticalUnitVisual>();
            tgtVis?.TriggerHitFlash();
            tgtVis?.SpawnFloatingText("IMMOBILISÉ (-3 PV Absolus)", Color.cyan);

            if (!target.Stats.IsAlive)
            {
                tgtVis?.TriggerFallingBackDeath();
                arena?.AutoTargetNextAlive();
            }

            arena?.Log($"🛑 <b>{actor.Stats.Name}</b> brise les vecteurs cinétiques de <b>{target.Stats.Name}</b> (-1 PA) : cible figée [Immobilisé] et 3 dégâts Absolus !");
            TickProgression(actor.Sheet, SkillType.MagieEsprit, arena);
            return true;
        }

        // =====================================================================
        // GARDE DU CORPS & INTERCEPTION D'ALLIÉ (RD-032, Livre VI §24.5)
        // =====================================================================

        /// <summary>
        /// Posture active : le protecteur choisit de veiller sur un allié adjacent.
        /// </summary>
        public static bool CanSetBodyguard(TacticalUnit actor, TacticalUnit targetAlly)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(targetAlly)) return false;
            if (actor == targetAlly || IsEnemyOf(actor, targetAlly)) return false;
            if (!actor.Stats.HasSpecialization(SpecTenir) && !actor.Stats.HasSpecialization(SpecGardeDuCorps)) return false;
            int dist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, targetAlly.CurrentCoords, targetAlly.FootprintType);
            return dist <= InterceptRange;
        }

        public static bool ExecuteSetBodyguard(TacticalUnit actor, TacticalUnit targetAlly, CombatDevArena arena)
        {
            if (!CanSetBodyguard(actor, targetAlly)) return false;

            SkillTechniqueState.SetBodyguard(actor.Stats, targetAlly.Stats);
            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🛡️ Garde : {targetAlly.Stats.Name}", Color.cyan);
            targetAlly.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🛡️ Protégé par {actor.Stats.Name}", Color.green);
            arena?.Log($"🛡️ <b>{actor.Stats.Name}</b> prend la posture de <b>Garde du corps</b> pour <b>{targetAlly.Stats.Name}</b> : prendra les coups à sa place au contact (1 PA réaction) !");
            arena?.RecordChronoSnapshot($"Garde du corps : {actor.Stats.Name} -> {targetAlly.Stats.Name}");
            return true;
        }

        /// <summary>
        /// Règle pure Core : vérifie si un protecteur peut intercepter un coup ciblant targetAlly (RD-032).
        /// Contact (distance <= 1), 1 PA disponible (ajusté avec Ralenti), réaction du round disponible,
        /// non neutralisé (étourdi/paralysé/sonné/inconscient), et qualifié (spé Tenir la Ligne / Garde du corps,
        /// donneur du bonus de ligne, ou garde du corps attitré).
        /// </summary>
        public static bool CanInterceptStats(CharacterStats protector, CharacterStats targetAlly, int distance)
        {
            if (protector == null || targetAlly == null || protector == targetAlly) return false;
            if (!protector.IsAlive || !targetAlly.IsAlive) return false;
            if (OpportunityState.IsCancelledByStatus(protector)) return false;
            if (!OpportunityState.CanReact(protector)) return false;

            if (!protector.CanAffordActionPoints(InterceptCost)) return false;
            if (distance > InterceptRange) return false;

            bool hasSpec = protector.HasSpecialization(SpecTenir)
                        || protector.HasSpecialization(SpecGardeDuCorps);
            bool isDonor = SkillTechniqueState.TryGetLineBonusDonor(targetAlly, out var donor) && donor == protector;
            bool isBodyguard = SkillTechniqueState.IsGuardedBy(targetAlly, protector);

            return hasSpec || isDonor || isBodyguard;
        }

        /// <summary>
        /// Débite 1 PA et consomme l'unique réaction du round du protecteur pour s'interposer.
        /// </summary>
        public static bool ExecuteInterceptStats(CharacterStats protector, CharacterStats targetAlly, out string error)
        {
            error = null;
            if (!CanInterceptStats(protector, targetAlly, 1))
            {
                error = "Interception impossible (invalide, distance > 1, ou PA/réaction manquante).";
                return false;
            }

            int effectiveCost = protector.GetEffectiveApCost(InterceptCost);
            if (!protector.ConsumeActionPoints(InterceptCost))
            {
                error = $"{protector.Name} n'a pas assez de PA ({protector.CurrentActionPoints}/{effectiveCost}) pour intercepter.";
                return false;
            }
            if (!OpportunityState.TryConsumeReaction(protector))
            {
                protector.CurrentActionPoints += effectiveCost;
                error = $"{protector.Name} a déjà dépensé sa réaction ce round.";
                return false;
            }
            return true;
        }

        public static bool CanIntercept(TacticalUnit protector, TacticalUnit targetAlly)
        {
            if (!IsAliveUnit(protector) || !IsAliveUnit(targetAlly)) return false;
            if (protector == targetAlly || IsEnemyOf(protector, targetAlly)) return false;
            int dist = TitanFootprint.MinDistanceBetweenUnits(
                protector.CurrentCoords, protector.FootprintType,
                targetAlly.CurrentCoords, targetAlly.FootprintType);
            return CanInterceptStats(protector.Stats, targetAlly.Stats, dist);
        }

        public static bool ExecuteIntercept(TacticalUnit protector, TacticalUnit targetAlly, CombatDevArena arena)
        {
            if (protector == null || targetAlly == null) return false;
            int dist = TitanFootprint.MinDistanceBetweenUnits(
                protector.CurrentCoords, protector.FootprintType,
                targetAlly.CurrentCoords, targetAlly.FootprintType);
            if (!CanInterceptStats(protector.Stats, targetAlly.Stats, dist)) return false;

            if (!ExecuteInterceptStats(protector.Stats, targetAlly.Stats, out var err))
            {
                arena?.Log($"⚠️ {err}");
                return false;
            }

            int apCost = protector.Stats.GetEffectiveApCost(InterceptCost);

            var pVis = protector.GetComponent<TacticalUnitVisual>();
            var tVis = targetAlly.GetComponent<TacticalUnitVisual>();

            pVis?.SpawnFloatingText($"🛡️ Garde du corps (-{apCost} PA)", Color.cyan);
            pVis?.TriggerBodyBlock();
            tVis?.SpawnFloatingText($"🛡️ Protégé par {protector.Stats.Name} !", Color.green);

            arena?.Log($"🛡️ <b>{protector.Stats.Name}</b> s'interpose pour <b>{targetAlly.Stats.Name}</b> (Tenir la Ligne : -{apCost} PA réaction) et prend le coup à sa place !");
            arena?.RecordChronoSnapshot($"Interception allié : {protector.Stats.Name} -> {targetAlly.Stats.Name}");

            TickProgression(protector.Sheet, SkillType.Leadership, arena);
            return true;
        }

        public static List<TacticalUnit> GetEligibleProtectors(TacticalUnit targetAlly)
        {
            var list = new List<TacticalUnit>();
            if (!IsAliveUnit(targetAlly)) return list;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (CanIntercept(u, targetAlly)) list.Add(u);
            }
            return list;
        }

        public static bool HasPlayerControlledProtector(TacticalUnit targetAlly)
        {
            var protectors = GetEligibleProtectors(targetAlly);
            for (int i = 0; i < protectors.Count; i++)
            {
                if (protectors[i].IsPlayerControlled) return true;
            }
            return false;
        }

        public static TacticalUnit FindBestAIProtector(TacticalUnit targetAlly)
        {
            var protectors = GetEligibleProtectors(targetAlly);
            TacticalUnit best = null;
            float bestScore = -1f;

            for (int i = 0; i < protectors.Count; i++)
            {
                var p = protectors[i];
                if (p.IsPlayerControlled) continue;

                float targetHpRatio = (float)targetAlly.Stats.CurrentHealth / Mathf.Max(1, targetAlly.Stats.MaxHealth);
                float protHpRatio = (float)p.Stats.CurrentHealth / Mathf.Max(1, p.Stats.MaxHealth);

                // Ne pas sacrifier un garde déjà moribond pour un allié en pleine forme
                if (p.Stats.CurrentHealth <= 3 && targetHpRatio > 0.6f) continue;

                float score = (1.0f - targetHpRatio) * 10f + protHpRatio * 5f + p.Stats.BaseArmorAbsorption;
                if (SkillTechniqueState.IsGuardedBy(targetAlly.Stats, p.Stats)) score += 20f;
                else if (SkillTechniqueState.TryGetLineBonusDonor(targetAlly.Stats, out var d) && d == p.Stats) score += 10f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            return best;
        }

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
