using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.DuoTech;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// RD-085 : armure fissurée + résolutions table des 6 Duo-Techs (Livre VI §25.5). — extrait sans modification depuis DuoTechRegistry.cs.
    /// </summary>
    public static partial class DuoTechRegistry
    {
        // =====================================================================
        // RD-085 — ARMURE FISSURÉE (Trempe Inversée, Livre VI §25.5)
        // -2 absorption jusqu'à la fin du round suivant. Suivi dans ce registre :
        // ApplyArmorFissure débite, RestoreArmorFissure rembourse (MJ / TurnManager
        // à la fin du round suivant). Testable sans Unity.
        // =====================================================================

        private static readonly Dictionary<CharacterStats, int> _armorFissures = new();

        public static void ApplyArmorFissure(CharacterStats target, int amount = 2)
        {
            if (target == null || amount <= 0) return;
            // Enregistre le débit RÉEL (plancher 0) pour un remboursement exact.
            int before = target.BaseArmorAbsorption;
            target.BaseArmorAbsorption = Math.Max(0, before - amount);
            int debited = before - target.BaseArmorAbsorption;
            if (debited <= 0) return;
            _armorFissures.TryGetValue(target, out int prev);
            _armorFissures[target] = prev + debited;
        }

        public static bool HasArmorFissure(CharacterStats target)
        {
            return target != null
                && _armorFissures.TryGetValue(target, out int v) && v > 0;
        }

        public static void RestoreArmorFissure(CharacterStats target)
        {
            if (target == null) return;
            if (_armorFissures.TryGetValue(target, out int v) && v > 0)
            {
                target.BaseArmorAbsorption += v;
                _armorFissures.Remove(target);
            }
        }

        /// <summary>
        /// Restaure TOUTES les fissures suivies (fin d'effet groupée), puis
        /// vide le suivi. Ne fait jamais perdre d'armure : sans suivi, rien.
        /// </summary>
        public static void ClearAllArmorFissures()
        {
            foreach (var kvp in _armorFissures)
            {
                if (kvp.Key != null && kvp.Value > 0)
                    kvp.Key.BaseArmorAbsorption += kvp.Value;
            }
            _armorFissures.Clear();
        }

        /// <summary>
        /// Dégâts perforants (Railgun Parfaite) : raw moins la moitié de l'armure
        /// totale (base + portée, arrondi inférieur), plancher 0. Le reste suit la
        /// voie brute (létal via EvaluateFatalBlow, sinon PV directs).
        /// </summary>
        private static void DealPerforant(CharacterStats target, int raw, Action<string> log, string label)
        {
            if (target == null || !target.IsAlive) return;
            int totalArmor = Math.Max(0, target.BaseArmorAbsorption + target.GetWornArmorBonus());
            int net = Math.Max(0, raw - totalArmor / 2);
            if (net <= 0)
            {
                log?.Invoke($"{label} : {raw} perforants absorbés par {totalArmor} armure (moitié = {totalArmor / 2}) — 0 net.");
                return;
            }
            log?.Invoke($"{label} : {raw} perforants - {totalArmor / 2} (moitié de {totalArmor} armure) = {net} nets.");
            DealBrut(target, net, log, label);
        }

        /// <summary>
        /// Whiff Uppercut (Ratée) : +1 Essoufflement chacun, sans dépasser le
        /// plafond de Constitution ; au plafond, Ralenti 1 tour (cohérent avec
        /// TriggerRedlineAP). PA déjà payés perdus (pas de refund).
        /// </summary>
        private static void DealWhiffEssoufflement(CharacterStats s, Action<string> log, string label)
        {
            if (s == null || !s.IsAlive) return;
            int maxEss = s.Attributes.Constitution;
            s.Essoufflement = Math.Min(maxEss, s.Essoufflement + 1);
            log?.Invoke($"{label} : {s.Name} +1 Essoufflement ({s.Essoufflement}/{maxEss}).");
            if (s.Essoufflement >= maxEss)
                s.ApplyStatus(StatusEffect.Ralenti, 1);
        }

        /// <summary>
        /// Condition Uppercut (Livre VI §25.5) : cible entravée (étourdie,
        /// ralentie ou gelée — gelée lue comme Immobilisé/Sonné/Paralysé, les
        /// entraves dures du compendium) ET à ≤ 50% PV.
        /// </summary>
        public static bool IsEntraveeForUppercut(CharacterStats target)
        {
            if (target == null || !target.IsAlive) return false;
            return target.ActiveStatus.HasFlag(StatusEffect.Etourdi)
                || target.ActiveStatus.HasFlag(StatusEffect.Ralenti)
                || target.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                || target.ActiveStatus.HasFlag(StatusEffect.Sonne)
                || target.ActiveStatus.HasFlag(StatusEffect.Paralyse);
        }

        public static bool IsUppercutCondition(CharacterStats target)
        {
            if (!IsEntraveeForUppercut(target)) return false;
            return target.CurrentHealth * 2 <= target.MaxHealth;
        }

        // =====================================================================
        // RD-085 — RÉSOLUTIONS TABLE (Parfaite / Semi / Ratée, Livre VI §25.5)
        // Le WeaveController appelle en binaire (Perfect/Miss) après validation
        // de placement ; le Semi n'existe qu'aux dés (TableOutcome partiel).
        // =====================================================================

        /// <summary>
        /// Trempe Inversée : Parfaite = Ralenti 1t + armures fissurées (-2
        /// absorption jusqu'à fin round suivant) ; Semi = Ralenti seul ;
        /// Ratée = le gel ne prend pas.
        /// </summary>
        public static void ResolveTrempe(
            List<CharacterStats> targets, DuoTechOutcome outcome, Action<string> log)
        {
            if (outcome == DuoTechOutcome.Miss)
            {
                log?.Invoke("❄️ <b>Trempe Inversée</b> [Ratée] : le gel ne prend pas (PA perdus).");
                return;
            }
            if (targets == null || targets.Count == 0)
            {
                log?.Invoke("❄️ <b>Trempe Inversée</b> : aucun groupé dans la zone (PA perdus).");
                return;
            }
            string tag = outcome == DuoTechOutcome.Perfect ? "Parfaite" : "Semi";
            log?.Invoke($"❄️ <b>Trempe Inversée</b> [{tag}] : {targets.Count} cible(s).");
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.IsAlive) continue;
                t.ApplyStatus(StatusEffect.Ralenti, 1);
                if (outcome == DuoTechOutcome.Perfect)
                {
                    ApplyArmorFissure(t, 2);
                    log?.Invoke($"Trempe : {t.Name} est [Ralenti] 1 tour, armure fissurée -2 (restaurer +2 fin round suivant) !");
                }
                else
                {
                    log?.Invoke($"Trempe : {t.Name} est [Ralenti] 1 tour.");
                }
            }
        }

        /// <summary>
        /// Railgun Artisanale : Parfaite = 8 perforants (moitié d'armure) sur la
        /// cible dure ; Semi = 5 bruts, armure normale ; Ratée = couloir refermé.
        /// </summary>
        public static void ResolveRailgun(
            CharacterStats target, DuoTechOutcome outcome, Action<string> log)
        {
            if (target == null || !target.IsAlive) return;
            switch (outcome)
            {
                case DuoTechOutcome.Perfect:
                    log?.Invoke("🛤️ <b>Railgun Artisanale</b> [Parfaite] : décharge à plein régime !");
                    DealPerforant(target, 8, log, "Railgun");
                    break;
                case DuoTechOutcome.Partial:
                    log?.Invoke("🛤️ <b>Railgun Artisanale</b> [Semi] : bobines à demi-charge.");
                    DealBrut(target, 5, log, "Railgun");
                    break;
                default:
                    log?.Invoke("🛤️ <b>Railgun Artisanale</b> [Ratée] : le couloir se referme (PA perdus).");
                    break;
            }
        }

        /// <summary>
        /// Cisaille Entropique : Parfaite = 5 bruts + Saignement sur chaque cible
        /// à l'intersection ; Semi = 3 bruts ; Ratée = branches désynchronisées.
        /// </summary>
        public static void ResolveCisaille(
            List<CharacterStats> targets, DuoTechOutcome outcome, Action<string> log)
        {
            if (outcome == DuoTechOutcome.Miss)
            {
                log?.Invoke("✖️ <b>Cisaille Entropique</b> [Ratée] : les branches se désynchronisent (PA perdus).");
                return;
            }
            if (targets == null || targets.Count == 0)
            {
                log?.Invoke("✖️ <b>Cisaille Entropique</b> : personne à l'intersection (PA perdus).");
                return;
            }
            if (outcome == DuoTechOutcome.Perfect)
            {
                log?.Invoke($"✖️ <b>Cisaille Entropique</b> [Parfaite] : {targets.Count} cible(s) cisaillée(s) !");
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.IsAlive) continue;
                    DealBrut(t, 5, log, "Cisaille");
                    if (!t.IsAlive) continue;
                    t.ApplyStatus(StatusEffect.Saignement, 1);
                    log?.Invoke($"Cisaille : {t.Name} [Saignement] !");
                }
            }
            else
            {
                log?.Invoke($"✖️ <b>Cisaille Entropique</b> [Semi] : {targets.Count} cible(s), coupe partielle.");
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.IsAlive) continue;
                    DealBrut(t, 3, log, "Cisaille");
                }
            }
        }

        /// <summary>
        /// Parallaxe : Parfaite = cible à 0 en défense face à 4 bruts ;
        /// Semi = défense à -2 face aux mêmes 4 bruts (canon littéral) ;
        /// Ratée = la cible esquive entre les deux temps.
        /// </summary>
        public static void ResolveParallaxe(
            CharacterStats target, DuoTechOutcome outcome, Action<string> log)
        {
            if (target == null || !target.IsAlive) return;
            switch (outcome)
            {
                case DuoTechOutcome.Perfect:
                    log?.Invoke("⧉ <b>Parallaxe</b> [Parfaite] : la cible ne peut pas se défendre (score 0) !");
                    DealBrut(target, 4, log, "Parallaxe");
                    break;
                case DuoTechOutcome.Partial:
                    log?.Invoke("⧉ <b>Parallaxe</b> [Semi] : défense adverse à -2 !");
                    DealBrut(target, 4, log, "Parallaxe");
                    break;
                default:
                    log?.Invoke("⧉ <b>Parallaxe</b> [Ratée] : la cible esquive entre les deux temps (PA perdus).");
                    break;
            }
        }

        /// <summary>
        /// Mirage à Deux Temps (défensif) : Parfaite = replacement 3 cases +
        /// réaction défensive gratuite (0 PA) jusqu'au prochain tour de Mina ;
        /// Semi = replacement seul ; Ratée = pré-vision brouillée. Le déplacement
        /// grille est exécuté par le WeaveController (téléport) ; ici : statuts.
        /// </summary>
        public static void ResolveMirage(
            CharacterStats mina, DuoTechOutcome outcome, Action<string> log)
        {
            switch (outcome)
            {
                case DuoTechOutcome.Perfect:
                    log?.Invoke("🌫️ <b>Mirage à Deux Temps</b> [Parfaite] : replacement 3 cases + réaction défensive gratuite (0 PA) jusqu'au prochain tour !");
                    mina?.GrantFreeDefensiveReaction();
                    break;
                case DuoTechOutcome.Partial:
                    log?.Invoke("🌫️ <b>Mirage à Deux Temps</b> [Semi] : replacement 3 cases (sans réaction gratuite).");
                    break;
                default:
                    log?.Invoke("🌫️ <b>Mirage à Deux Temps</b> [Ratée] : la pré-vision se brouille (PA perdus).");
                    break;
            }
        }

        /// <summary>
        /// Uppercut Thermobarique : Parfaite = 7 bruts + À Terre ; Semi = 4 bruts ;
        /// Ratée = PA perdus + 1 Essoufflement chacun (whiff punit, même au tissage).
        /// </summary>
        public static void ResolveUppercut(
            CharacterStats mina, CharacterStats lucas,
            CharacterStats target, DuoTechOutcome outcome, Action<string> log)
        {
            switch (outcome)
            {
                case DuoTechOutcome.Perfect:
                    if (target == null || !target.IsAlive) return;
                    log?.Invoke("⬆️ <b>Uppercut Thermobarique</b> [Parfaite] : détonation ascendante !");
                    DealBrut(target, 7, log, "Uppercut");
                    if (!target.IsAlive) return;
                    target.ApplyStatus(StatusEffect.ATerre, 1);
                    log?.Invoke($"Uppercut : {target.Name} est [À Terre] !");
                    break;
                case DuoTechOutcome.Partial:
                    if (target == null || !target.IsAlive) return;
                    log?.Invoke("⬆️ <b>Uppercut Thermobarique</b> [Semi] : percussion partielle.");
                    DealBrut(target, 4, log, "Uppercut");
                    break;
                default:
                    log?.Invoke("⬆️ <b>Uppercut Thermobarique</b> [Ratée] : whiff — le vide se referme, +1 Essoufflement chacun (PA perdus).");
                    DealWhiffEssoufflement(mina, log, "Uppercut (whiff)");
                    DealWhiffEssoufflement(lucas, log, "Uppercut (whiff)");
                    break;
            }
        }

    }
}
