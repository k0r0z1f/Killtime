using System;
using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;

namespace Killtime.Core.Combat
{
    /// <summary>Typage élémentaire minimal pour les réactions des Boss Titans (RD-087).</summary>
    public enum TitanBossElement
    {
        Other,
        Fire,
        Plasma,
        Cold
    }

    /// <summary>Résultat du crochet dégâts élémentaires (réactions boss, avant létal).</summary>
    public struct TitanBossReaction
    {
        public bool HasReaction;
        public int AdjustedDamage;
        public int Healed;
        public bool Absorbed;
        public bool ShockTriggered;
        public int MemoryReduction;
        public bool ArmorRestored;
        public string LogFragment;
    }

    /// <summary>
    /// Mécaniques pures des Boss Titans (Livre XI §45-46 — RD-087).
    /// Aucune dépendance Unity/MonoBehaviour : 100% testable en EditMode.
    /// Les noms de spécialisations recopient à l'identique TitanBossCatalog.
    /// </summary>
    public static class TitanBossMechanics
    {
        // Noms officiels (TitanBossCatalog — ne pas renommer).
        public const string SpecFlameMemory = "Mémoire des Flammes";
        public const string SpecClawSweep = "Balayage de Griffes en Arc";
        public const string SpecTelluricCharge = "Charge Tellurique des Falaises";
        public const string SpecThermalAbsorption = "Absorption d'Énergie Thermique";
        public const string SpecVoidChain = "Éclairs de Néant en Chaîne";
        public const string SpecQuantumLeap = "Saut Quantique Déphasé";
        public const string SpecTectonicFist = "Poing Tectonique";
        public const string SpecLithoRegen = "Régénération Lithosphérique";
        public const string SpecMagmaCarapace = "Carapace de Magma";
        public const string SpecThermalShock = "Vulnérabilité Choc Thermique";

        // Bête d'Hybris.
        public const int FlameMemoryReductionPerHit = 2;
        public const int ClawSweepDamage = 4;
        public const int ClawSweepMaxTargets = 3;
        public const int ClawSweepCostFew = 2;
        public const int ClawSweepCostFull = 3;
        public const int ClawSweepRange = 2;
        public const int ClawSweepKnockback = 1;
        public const int ClawSweepGroundedTurns = 2;
        public const int TelluricChargeCost = 5;
        public const int TelluricChargeMinRange = 3;
        public const int TelluricChargeMaxRange = 10;
        public const int TelluricCollapseDamage = 3;
        public const int TelluricCollapseGroundedTurns = 1;

        // Minulican.
        public const int ThermalAbsorbCost = 2;
        public const int VoidChainCost = 4;
        public const int VoidChainMaxTargets = 3;
        public const int VoidChainMaxRange = 15;
        public const int VoidChainDamage = 5;
        public const int VoidChainDestabilisedTurns = 2;
        public const int QuantumLeapCost = 3;
        public const int QuantumLeapMaxRange = 6;
        public const int QuantumRiposteBonusEC = 2;

        // Disciple de Terre.
        public const int TectonicFistCost = 3;
        public const int TectonicFistDamage = 7;
        public const int LithoRegenAmount = 3;
        public const int ThermalShockWindowRounds = 2;
        public const int ThermalShockArmorLossTurns = 2;
        public const int ThermalShockDamageMultiplier = 2;

        // ------------------------------------------------------------------
        // Réglages JSON (RD-087 suite) : les consts ci-dessus restent les
        // défauts ; à JSON égal le comportement est inchangé.
        // ------------------------------------------------------------------

        /// <summary>Params bossAI JSON du porteur (défauts si entrée absente).</summary>
        public static TitanBossAIParams ParamsFor(CharacterStats s) => TitanBossAIConfig.GetParams(s);

        public static int ClawSweepCostFor(CharacterStats boss, int targetCount)
        {
            var p = ParamsFor(boss);
            return targetCount >= p.sweepMaxTargets ? p.sweepCostFull : p.sweepCostFew;
        }

        public static int ClampSweepTargetsFor(CharacterStats boss, int candidateCount)
        {
            var p = ParamsFor(boss);
            return Math.Max(0, Math.Min(p.sweepMaxTargets, candidateCount));
        }

        public static bool IsValidSweepTargetFor(CharacterStats boss, int distance)
        {
            var p = ParamsFor(boss);
            return distance >= 0 && distance <= p.sweepRange;
        }

        public static bool CanTelluricChargeFor(CharacterStats boss, int distance)
        {
            var p = ParamsFor(boss);
            return distance >= p.chargeMinRange && distance <= p.chargeMaxRange;
        }

        public static int ClampChainTargetsFor(CharacterStats boss, int candidateCount)
        {
            var p = ParamsFor(boss);
            return Math.Max(0, Math.Min(p.chainMaxTargets, candidateCount));
        }

        public static bool IsValidChainTargetFor(CharacterStats boss, int distance)
        {
            var p = ParamsFor(boss);
            return distance >= 0 && distance <= p.chainMaxRange;
        }

        public static bool CanQuantumLeapFor(CharacterStats boss, int distance)
        {
            var p = ParamsFor(boss);
            return distance >= 0 && distance <= p.leapMaxRange;
        }

        public static int TectonicStructureDamageFor(CharacterStats disciple, ObstacleMaterial material)
        {
            var p = ParamsFor(disciple);
            return ComputeTectonicStructureDamage(material, Math.Max(0, p.fistDamage));
        }

        // ------------------------------------------------------------------
        // Détection
        // ------------------------------------------------------------------

        public static bool IsBeastOfHybris(CharacterStats s) =>
            s != null && (s.HasSpecialization(SpecFlameMemory) || s.HasSpecialization(SpecClawSweep));

        public static bool IsMinulican(CharacterStats s) =>
            s != null && (s.HasSpecialization(SpecThermalAbsorption) || s.HasSpecialization(SpecVoidChain));

        public static bool IsDiscipleDeTerre(CharacterStats s) =>
            s != null && (s.HasSpecialization(SpecThermalShock) || s.HasSpecialization(SpecTectonicFist));

        // ------------------------------------------------------------------
        // Typage élémentaire
        // ------------------------------------------------------------------

        public static TitanBossElement ClassifyGrenadeKind(string grenadeKind)
        {
            if (string.IsNullOrEmpty(grenadeKind)) return TitanBossElement.Other;
            string t = grenadeKind.Trim().ToLowerInvariant();
            if (t.Contains("plasma")) return TitanBossElement.Plasma;
            if (t.Contains("cryo") || t.Contains("gel") || t.Contains("stase") || t.Contains("froid") || t.Contains("glace")) return TitanBossElement.Cold;
            if (t.Contains("incend") || t.Contains("feu") || t.Contains("flamme") || t.Contains("thermite")
                || t.Contains("thermobar") || t.Contains("thermique") || t.Contains("pyro") || t.Contains("brûl") || t.Contains("brul"))
                return TitanBossElement.Fire;
            return TitanBossElement.Other;
        }

        public static TitanBossElement ClassifyDuelElement(SkillType skill, Killtime.Core.Inventory.InventoryItem weapon, CharacterStats attacker)
        {
            if (weapon != null)
            {
                string desc = $"{weapon.Name} {weapon.Description} {weapon.Category} {weapon.AmmoType} {weapon.GrenadeKind}".ToLowerInvariant();
                if (desc.Contains("plasma")) return TitanBossElement.Plasma;
                if (desc.Contains("cryo") || desc.Contains("gel") || desc.Contains("froid") || desc.Contains("glace") || desc.Contains("congel"))
                    return TitanBossElement.Cold;
            }
            if (CombatProgressionService.IsFireOrPlasmaAttack(skill, weapon, attacker))
                return TitanBossElement.Fire;
            if ((skill == SkillType.MagieElementale || skill == SkillType.MagiePrimale) && attacker != null
                && (attacker.HasSpecialization("Glace") || attacker.HasSpecialization("Froid") || attacker.HasSpecialization("Cryo")))
                return TitanBossElement.Cold;
            return TitanBossElement.Other;
        }

        // ------------------------------------------------------------------
        // Crochet dégâts élémentaires (DamageEngine + Grenades, avant létal)
        // ------------------------------------------------------------------

        public static TitanBossReaction ResolveIncomingElementalDamage(
            CharacterStats defender, TitanBossElement element, int rawDamage, int currentRound = -1)
        {
            var res = new TitanBossReaction { AdjustedDamage = Math.Max(0, rawDamage) };
            if (defender == null) return res;
            if (currentRound < 0) currentRound = TitanBossState.CurrentRound;

            // Expiration de fissuration (même sur coup non élémentaire ou nul).
            if (TitanBossState.UpdateArmorCrack(defender, currentRound))
            {
                res.HasReaction = true;
                res.ArmorRestored = true;
                res.LogFragment = $"🪨 Cuirasse du <b>{defender.Name}</b> ressoudée (fin du Choc Thermique).";
            }
            if (rawDamage <= 0) return res;
            if (element == TitanBossElement.Other) return res;

            bool isFireOrPlasma = element == TitanBossElement.Fire || element == TitanBossElement.Plasma;

            // 1) Bête d'Hybris — Mémoire des Flammes (feu uniquement).
            if (element == TitanBossElement.Fire && defender.HasSpecialization(SpecFlameMemory))
            {
                int prior = TitanBossState.GetFlameHits(defender);
                int perHit = Math.Max(0, ParamsFor(defender).flameReductionPerHit);
                int reduction = Math.Min(res.AdjustedDamage, perHit * prior);
                res.AdjustedDamage -= reduction;
                res.MemoryReduction = reduction;
                TitanBossState.RegisterFlameHit(defender);
                res.HasReaction = true;
                res.LogFragment += $"🔥 <b>Mémoire des Flammes</b> : {defender.Name} a déjà encaissé {prior} feu(x) — −{reduction} dégâts (compteur {prior + 1}).";
            }

            // 2) Minulican — Absorption d'Énergie Thermique (réaction 2 PA, feu/plasma).
            if (isFireOrPlasma && defender.HasSpecialization(SpecThermalAbsorption))
            {
                int absorbCost = Math.Max(0, ParamsFor(defender).absorbCost);
                if (defender.ConsumeActionPoints(absorbCost))
                {
                    int absorbed = res.AdjustedDamage / 2;
                    res.AdjustedDamage -= absorbed;
                    int missing = Math.Max(0, defender.MaxHealth - defender.CurrentHealth);
                    int healed = Math.Min(absorbed, missing);
                    defender.CurrentHealth += healed;
                    res.Healed = healed;
                    res.Absorbed = true;
                    res.HasReaction = true;
                    TitanBossState.AddAbsorbed(defender, absorbed);
                    res.LogFragment += $"🌀 <b>Absorption Thermique</b> (−{absorbCost} PA) : {absorbed} dégâts feu/plasma convertis en PV (+{healed} PV, total {TitanBossState.GetAbsorbedTotal(defender)}).";
                }
                else
                {
                    res.HasReaction = true;
                    res.LogFragment += $"🌀 <b>Absorption Thermique IMPOSSIBLE</b> ({defender.Name} : {defender.CurrentActionPoints} PA, coût {absorbCost}) — dégâts subis intacts.";
                }
            }

            // 3) Disciple de Terre — Vulnérabilité Choc Thermique (froid PUIS feu ≤ 2 tours).
            if (defender.HasSpecialization(SpecThermalShock))
            {
                var cfg = ParamsFor(defender);
                if (element == TitanBossElement.Cold)
                {
                    TitanBossState.RegisterColdHit(defender, currentRound);
                    res.HasReaction = true;
                    res.LogFragment += $"❄️ <b>Refroidissement</b> de la cuirasse de {defender.Name} (round {currentRound}) — un coup de feu sous {cfg.shockWindowRounds} tours la fissurera !";
                }
                else if (element == TitanBossElement.Fire
                    && TitanBossState.HasRecentColdHit(defender, currentRound, cfg.shockWindowRounds))
                {
                    TitanBossState.ClearColdHit(defender);
                    res.AdjustedDamage *= Math.Max(1, cfg.shockDamageMultiplier);
                    TitanBossState.CrackArmor(defender, currentRound, cfg.shockArmorLossTurns);
                    res.ShockTriggered = true;
                    res.HasReaction = true;
                    res.LogFragment += $"💥 <b>CHOC THERMIQUE</b> sur {defender.Name} (froid→feu, fenêtre {cfg.shockWindowRounds} tours) : dégâts doublés ({res.AdjustedDamage}) + cuirasse fissurée (armure 0 pendant {cfg.shockArmorLossTurns} tours) !";
                }
            }

            return res;
        }

        // ------------------------------------------------------------------
        // Balayage de griffes (Bête) : 4D, projection 1 case, À Terre, max 3 cibles
        // ------------------------------------------------------------------

        public static int ClawSweepCost(int targetCount) =>
            targetCount >= ClawSweepMaxTargets ? ClawSweepCostFull : ClawSweepCostFew;

        public static int ClampSweepTargets(int candidateCount) =>
            Math.Max(0, Math.Min(ClawSweepMaxTargets, candidateCount));

        public static bool IsValidSweepTarget(int distance) => distance >= 0 && distance <= ClawSweepRange;

        // ------------------------------------------------------------------
        // Charge tellurique (Bête) : 5 PA, ≤ 10 cases en ligne, effondrement
        // ------------------------------------------------------------------

        public static bool CanTelluricCharge(int distance) =>
            distance >= TelluricChargeMinRange && distance <= TelluricChargeMaxRange;

        // ------------------------------------------------------------------
        // Éclairs de néant en chaîne (Minulican) : 4 PA, ≤ 3 cibles à 15 cases
        // ------------------------------------------------------------------

        public static int ClampChainTargets(int candidateCount) =>
            Math.Max(0, Math.Min(VoidChainMaxTargets, candidateCount));

        public static bool IsValidChainTarget(int distance) => distance >= 0 && distance <= VoidChainMaxRange;

        // ------------------------------------------------------------------
        // Saut quantique déphasé (Minulican) : 3 PA, téléport ≤ 6 cases
        // ------------------------------------------------------------------

        public static bool CanQuantumLeap(int distance) => distance >= 0 && distance <= QuantumLeapMaxRange;

        public static int QuantumRiposteDamage(CharacterStats minulican, int baseDamage) =>
            Math.Max(0, baseDamage) + Math.Max(0, ParamsFor(minulican).leapRiposteBonusEC);

        // ------------------------------------------------------------------
        // Poing tectonique (Disciple) : 3 PA, 7D + bonus vs structures (RD-044)
        // ------------------------------------------------------------------

        public static int ComputeTectonicStructureDamage(ObstacleMaterial material, int baseDamage = TectonicFistDamage)
        {
            float mult = DestructibleEnvironmentRules.ComputeDamageMultiplier(material, ObstacleDamageType.Contondant);
            return Math.Max(0, (int)Math.Floor(Math.Max(0, baseDamage) * mult));
        }

        // ------------------------------------------------------------------
        // Régénération lithosphérique (Disciple) : +3 PV/tour si contact pierre/couvert
        // ------------------------------------------------------------------

        public static bool TickLithosphericRegen(CharacterStats disciple, bool inStoneContactOrCover, out string logFragment)
        {
            logFragment = "";
            if (disciple == null || !disciple.IsAlive) return false;
            if (!disciple.HasSpecialization(SpecLithoRegen)) return false;
            // La fissuration du Choc Thermique expire aussi au tick de tour.
            TitanBossState.UpdateArmorCrack(disciple, TitanBossState.CurrentRound);
            if (!inStoneContactOrCover) return false;
            int missing = disciple.MaxHealth - disciple.CurrentHealth;
            if (missing <= 0) return false;
            int healed = Math.Min(Math.Max(0, ParamsFor(disciple).regenAmount), missing);
            disciple.CurrentHealth += healed;
            logFragment = $"🪨 <b>Régénération Lithosphérique</b> : {disciple.Name} régénère +{healed} PV au contact de la pierre.";
            return true;
        }

        /// <summary>
        /// Contact pierre/couvert : une case occupée est adjacente à un nœud
        /// non praticable (roche/mur) ou porteur de couvert. Pur via sonde.
        /// </summary>
        public static bool IsInStoneContact(
            IReadOnlyList<HexCoordinates> occupiedCells,
            Func<HexCoordinates, (bool exists, bool walkable, bool hasCover)> probe)
        {
            if (occupiedCells == null || probe == null) return false;
            for (int c = 0; c < occupiedCells.Count; c++)
            {
                for (int dir = 0; dir < 6; dir++)
                {
                    var n = occupiedCells[c].GetNeighbor(dir);
                    var info = probe(n);
                    if (!info.exists) continue;
                    if (!info.walkable || info.hasCover) return true;
                }
            }
            return false;
        }
    }
}
