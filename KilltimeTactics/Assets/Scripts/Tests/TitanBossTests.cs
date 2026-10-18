#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-087 : Capacités Réactives &amp; Vulnérabilités des Boss Titans (Livre XI §45-46).
    /// Couvre les 9 mécaniques au niveau pur (TitanBossMechanics + TitanBossState) :
    /// mémoire cumulative + reset, balayage 3 cibles + À Terre, charge 10 cases,
    /// absorption 50% + coût 2 PA + refus sans PA, éclairs 3 cibles + Déstabilisé,
    /// saut téléport + revers, poing 7D + bonus structure, regen 3 PV conditionnelle,
    /// choc thermique (doublement + perte armure 2 tours).
    /// </summary>
    [TestFixture]
    public class TitanBossTests
    {
        private static CharacterStats MakeStats(string name, int ap = 8, int armor = 2, params string[] specs)
        {
            var attr = new Attributes(6, 5, 6, 4, 4, 2, 2, 2);
            var sheet = new CharacterSheet
            {
                Name = name,
                BaseAttributes = attr,
                UnlockedSpecializations = new List<string>()
            };
            if (specs != null)
                for (int i = 0; i < specs.Length; i++)
                    sheet.UnlockedSpecializations.Add(specs[i]);
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            stats.CurrentActionPoints = ap;
            stats.BaseArmorAbsorption = armor;
            return stats;
        }

        [SetUp]
        public void SetUp()
        {
            LivreXIPNJState.ClearCombatState();
            TitanBossState.CurrentRound = 1;
            BestiaryCatalog.InvalidateCache();
            TitanBossAIConfig.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            LivreXIPNJState.ClearCombatState();
            TitanBossState.CurrentRound = 1;
            BestiaryCatalog.InvalidateCache();
            TitanBossAIConfig.InvalidateCache();
        }

        // ------------------------------------------------------------------
        // 1) Bête d'Hybris — Mémoire des Flammes
        // ------------------------------------------------------------------

        [Test]
        public void FlameMemory_FirstFireHit_NoReduction()
        {
            var beast = MakeStats("La Bête d'Hybris", specs: new[] { TitanBossMechanics.SpecFlameMemory });
            var res = TitanBossMechanics.ResolveIncomingElementalDamage(beast, TitanBossElement.Fire, 10, 1);
            Assert.IsTrue(res.HasReaction);
            Assert.AreEqual(0, res.MemoryReduction);
            Assert.AreEqual(10, res.AdjustedDamage);
            Assert.AreEqual(1, TitanBossState.GetFlameHits(beast));
        }

        [Test]
        public void FlameMemory_SuccessiveHits_CumulativeReduction()
        {
            var beast = MakeStats("La Bête d'Hybris", specs: new[] { TitanBossMechanics.SpecFlameMemory });
            Assert.AreEqual(10, TitanBossMechanics.ResolveIncomingElementalDamage(beast, TitanBossElement.Fire, 10, 1).AdjustedDamage);
            Assert.AreEqual(8, TitanBossMechanics.ResolveIncomingElementalDamage(beast, TitanBossElement.Fire, 10, 1).AdjustedDamage);
            var third = TitanBossMechanics.ResolveIncomingElementalDamage(beast, TitanBossElement.Fire, 10, 2);
            Assert.AreEqual(6, third.AdjustedDamage);
            Assert.AreEqual(4, third.MemoryReduction);
            StringAssert.Contains("Mémoire des Flammes", third.LogFragment);
        }

        [Test]
        public void FlameMemory_ClearCombatState_ResetsCounter()
        {
            var beast = MakeStats("La Bête d'Hybris", specs: new[] { TitanBossMechanics.SpecFlameMemory });
            TitanBossMechanics.ResolveIncomingElementalDamage(beast, TitanBossElement.Fire, 10, 1);
            TitanBossMechanics.ResolveIncomingElementalDamage(beast, TitanBossElement.Fire, 10, 1);
            Assert.AreEqual(2, TitanBossState.GetFlameHits(beast));
            LivreXIPNJState.ClearCombatState();
            Assert.AreEqual(0, TitanBossState.GetFlameHits(beast));
            Assert.AreEqual(10, TitanBossMechanics.ResolveIncomingElementalDamage(beast, TitanBossElement.Fire, 10, 3).AdjustedDamage);
        }

        // ------------------------------------------------------------------
        // 2) Balayage de griffes triple-cible
        // ------------------------------------------------------------------

        [Test]
        public void ClawSweep_Cost_TwoBelowThree_FullAtThree()
        {
            Assert.AreEqual(2, TitanBossMechanics.ClawSweepCost(1));
            Assert.AreEqual(2, TitanBossMechanics.ClawSweepCost(2));
            Assert.AreEqual(3, TitanBossMechanics.ClawSweepCost(3));
            Assert.AreEqual(4, TitanBossMechanics.ClawSweepDamage);
        }

        [Test]
        public void ClawSweep_MaxThreeTargets_ContactOrPerimeter_KnocksDown()
        {
            Assert.IsTrue(TitanBossMechanics.IsValidSweepTarget(0));
            Assert.IsTrue(TitanBossMechanics.IsValidSweepTarget(2));
            Assert.IsFalse(TitanBossMechanics.IsValidSweepTarget(3));
            Assert.AreEqual(3, TitanBossMechanics.ClampSweepTargets(5));

            var victims = new List<CharacterStats>();
            for (int i = 0; i < 5; i++) victims.Add(MakeStats("Victime" + i));
            int applied = TitanBossMechanics.ClampSweepTargets(victims.Count);
            for (int i = 0; i < applied; i++)
                victims[i].ApplyStatus(StatusEffect.ATerre, TitanBossMechanics.ClawSweepGroundedTurns);
            Assert.AreEqual(3, applied);
            for (int i = 0; i < 3; i++)
                Assert.IsTrue(victims[i].ActiveStatus.HasFlag(StatusEffect.ATerre));
            Assert.IsFalse(victims[3].ActiveStatus.HasFlag(StatusEffect.ATerre));
            Assert.IsFalse(victims[4].ActiveStatus.HasFlag(StatusEffect.ATerre));
        }

        // ------------------------------------------------------------------
        // 3) Charge tellurique
        // ------------------------------------------------------------------

        [Test]
        public void TelluricCharge_Range3to10_Cost5()
        {
            Assert.AreEqual(5, TitanBossMechanics.TelluricChargeCost);
            Assert.IsFalse(TitanBossMechanics.CanTelluricCharge(2));
            Assert.IsTrue(TitanBossMechanics.CanTelluricCharge(3));
            Assert.IsTrue(TitanBossMechanics.CanTelluricCharge(10));
            Assert.IsFalse(TitanBossMechanics.CanTelluricCharge(11));
        }

        // ------------------------------------------------------------------
        // 4) Minulican — Absorption d'Énergie Thermique
        // ------------------------------------------------------------------

        [Test]
        public void ThermalAbsorption_HalvesDamage_HealsHalf_Costs2PA()
        {
            var minu = MakeStats("Le Minulican", ap: 8, specs: new[] { TitanBossMechanics.SpecThermalAbsorption });
            minu.CurrentHealth = minu.MaxHealth - 10;
            int hpBefore = minu.CurrentHealth;
            var res = TitanBossMechanics.ResolveIncomingElementalDamage(minu, TitanBossElement.Fire, 8, 1);
            Assert.IsTrue(res.Absorbed);
            Assert.AreEqual(4, res.AdjustedDamage);
            Assert.AreEqual(4, res.Healed);
            Assert.AreEqual(hpBefore + 4, minu.CurrentHealth);
            Assert.AreEqual(6, minu.CurrentActionPoints);
            StringAssert.Contains("Absorption", res.LogFragment);
        }

        [Test]
        public void ThermalAbsorption_RefusedWithoutPA_DamageIntact()
        {
            var minu = MakeStats("Le Minulican", ap: 1, specs: new[] { TitanBossMechanics.SpecThermalAbsorption });
            var res = TitanBossMechanics.ResolveIncomingElementalDamage(minu, TitanBossElement.Plasma, 8, 1);
            Assert.IsFalse(res.Absorbed);
            Assert.AreEqual(8, res.AdjustedDamage);
            Assert.AreEqual(0, res.Healed);
            Assert.AreEqual(1, minu.CurrentActionPoints);
            StringAssert.Contains("IMPOSSIBLE", res.LogFragment);
        }

        [Test]
        public void ThermalAbsorption_IgnoresNonThermal()
        {
            var minu = MakeStats("Le Minulican", ap: 8, specs: new[] { TitanBossMechanics.SpecThermalAbsorption });
            var res = TitanBossMechanics.ResolveIncomingElementalDamage(minu, TitanBossElement.Other, 8, 1);
            Assert.IsFalse(res.Absorbed);
            Assert.AreEqual(8, res.AdjustedDamage);
            Assert.AreEqual(8, minu.CurrentActionPoints);
        }

        // ------------------------------------------------------------------
        // 5) Éclairs de Néant en Chaîne
        // ------------------------------------------------------------------

        [Test]
        public void VoidChain_MaxThreeTargets_Range15_Cost4()
        {
            Assert.AreEqual(4, TitanBossMechanics.VoidChainCost);
            Assert.AreEqual(3, TitanBossMechanics.VoidChainMaxTargets);
            Assert.AreEqual(5, TitanBossMechanics.VoidChainDamage);
            Assert.IsTrue(TitanBossMechanics.IsValidChainTarget(15));
            Assert.IsFalse(TitanBossMechanics.IsValidChainTarget(16));
            Assert.AreEqual(3, TitanBossMechanics.ClampChainTargets(7));
        }

        [Test]
        public void VoidChain_InflictsDestabilised()
        {
            var victims = new List<CharacterStats>();
            for (int i = 0; i < 3; i++) victims.Add(MakeStats("Cible" + i));
            int n = TitanBossMechanics.ClampChainTargets(victims.Count);
            for (int i = 0; i < n; i++)
                victims[i].ApplyStatus(StatusEffect.Destabilise, TitanBossMechanics.VoidChainDestabilisedTurns);
            for (int i = 0; i < 3; i++)
                Assert.IsTrue(victims[i].ActiveStatus.HasFlag(StatusEffect.Destabilise));
        }

        // ------------------------------------------------------------------
        // 6) Saut Quantique Déphasé
        // ------------------------------------------------------------------

        [Test]
        public void QuantumLeap_Range6_Cost3_RipostePlus2EC()
        {
            Assert.AreEqual(3, TitanBossMechanics.QuantumLeapCost);
            Assert.IsTrue(TitanBossMechanics.CanQuantumLeap(6));
            Assert.IsFalse(TitanBossMechanics.CanQuantumLeap(7));
            Assert.AreEqual(2, TitanBossMechanics.QuantumRiposteBonusEC);
            var minu = MakeStats("Le Minulican", specs: new[] { TitanBossMechanics.SpecQuantumLeap });
            Assert.AreEqual(5 + 2, TitanBossMechanics.QuantumRiposteDamage(minu, 5));
        }

        // ------------------------------------------------------------------
        // 7) Disciple — Poing Tectonique
        // ------------------------------------------------------------------

        [Test]
        public void TectonicFist_Cost3_Damage7()
        {
            Assert.AreEqual(3, TitanBossMechanics.TectonicFistCost);
            Assert.AreEqual(7, TitanBossMechanics.TectonicFistDamage);
        }

        [Test]
        public void TectonicFist_StructureBonus_ContondantVsStone()
        {
            Assert.AreEqual(10, TitanBossMechanics.ComputeTectonicStructureDamage(ObstacleMaterial.Pierre));
            Assert.AreEqual(7, TitanBossMechanics.ComputeTectonicStructureDamage(ObstacleMaterial.Bois));
            Assert.AreEqual(0, TitanBossMechanics.ComputeTectonicStructureDamage(ObstacleMaterial.Indestructible));
        }

        // ------------------------------------------------------------------
        // 8) Disciple — Régénération Lithosphérique
        // ------------------------------------------------------------------

        [Test]
        public void LithoRegen_Heals3_WhenStoneContact()
        {
            var disciple = MakeStats("Disciple de Terre", specs: new[] { TitanBossMechanics.SpecLithoRegen });
            disciple.CurrentHealth = disciple.MaxHealth - 5;
            Assert.IsTrue(TitanBossMechanics.TickLithosphericRegen(disciple, true, out string log));
            Assert.AreEqual(disciple.MaxHealth - 2, disciple.CurrentHealth);
            StringAssert.Contains("Régénération Lithosphérique", log);
        }

        [Test]
        public void LithoRegen_NoHeal_WithoutContact_FullHP_OrNoSpec()
        {
            var disciple = MakeStats("Disciple de Terre", specs: new[] { TitanBossMechanics.SpecLithoRegen });
            disciple.CurrentHealth = disciple.MaxHealth - 5;
            Assert.IsFalse(TitanBossMechanics.TickLithosphericRegen(disciple, false, out _));
            Assert.AreEqual(disciple.MaxHealth - 5, disciple.CurrentHealth);

            disciple.CurrentHealth = disciple.MaxHealth;
            Assert.IsFalse(TitanBossMechanics.TickLithosphericRegen(disciple, true, out _));

            var plain = MakeStats("Soldat");
            plain.CurrentHealth = plain.MaxHealth - 5;
            Assert.IsFalse(TitanBossMechanics.TickLithosphericRegen(plain, true, out _));
        }

        [Test]
        public void StoneContact_AdjacentBlockedOrCovered()
        {
            var cells = new List<HexCoordinates> { new HexCoordinates(0, 0) };
            Assert.IsTrue(TitanBossMechanics.IsInStoneContact(cells, h =>
                h.Equals(new HexCoordinates(1, 0)) ? (true, false, false) : (true, true, false)));
            Assert.IsTrue(TitanBossMechanics.IsInStoneContact(cells, h => (true, true, true)));
            Assert.IsFalse(TitanBossMechanics.IsInStoneContact(cells, h => (true, true, false)));
            Assert.IsFalse(TitanBossMechanics.IsInStoneContact(cells, null));
        }

        // ------------------------------------------------------------------
        // 9) Disciple — Vulnérabilité Choc Thermique
        // ------------------------------------------------------------------

        [Test]
        public void ThermalShock_ColdThenFire_DoublesDamage_CracksArmor()
        {
            var disciple = MakeStats("Disciple", armor: 6, specs: new[] { TitanBossMechanics.SpecThermalShock });
            var cold = TitanBossMechanics.ResolveIncomingElementalDamage(disciple, TitanBossElement.Cold, 5, 1);
            Assert.AreEqual(5, cold.AdjustedDamage);
            Assert.IsFalse(cold.ShockTriggered);

            var fire = TitanBossMechanics.ResolveIncomingElementalDamage(disciple, TitanBossElement.Fire, 6, 2);
            Assert.IsTrue(fire.ShockTriggered);
            Assert.AreEqual(12, fire.AdjustedDamage);
            Assert.AreEqual(0, disciple.BaseArmorAbsorption);
            StringAssert.Contains("CHOC THERMIQUE", fire.LogFragment);
        }

        [Test]
        public void ThermalShock_FireWithoutCold_NoTrigger()
        {
            var disciple = MakeStats("Disciple", armor: 6, specs: new[] { TitanBossMechanics.SpecThermalShock });
            var res = TitanBossMechanics.ResolveIncomingElementalDamage(disciple, TitanBossElement.Fire, 6, 1);
            Assert.IsFalse(res.ShockTriggered);
            Assert.AreEqual(6, res.AdjustedDamage);
            Assert.AreEqual(6, disciple.BaseArmorAbsorption);
        }

        [Test]
        public void ThermalShock_ExpiredWindow_NoTrigger_ArmorKept()
        {
            var disciple = MakeStats("Disciple", armor: 6, specs: new[] { TitanBossMechanics.SpecThermalShock });
            TitanBossMechanics.ResolveIncomingElementalDamage(disciple, TitanBossElement.Cold, 5, 1);
            var late = TitanBossMechanics.ResolveIncomingElementalDamage(disciple, TitanBossElement.Fire, 6, 4);
            Assert.IsFalse(late.ShockTriggered);
            Assert.AreEqual(6, late.AdjustedDamage);
            Assert.AreEqual(6, disciple.BaseArmorAbsorption);
        }

        [Test]
        public void ThermalShock_ArmorRestored_AfterTwoTurns()
        {
            var disciple = MakeStats("Disciple", armor: 6, specs: new[] { TitanBossMechanics.SpecThermalShock });
            TitanBossMechanics.ResolveIncomingElementalDamage(disciple, TitanBossElement.Cold, 5, 1);
            TitanBossMechanics.ResolveIncomingElementalDamage(disciple, TitanBossElement.Fire, 6, 2);
            Assert.AreEqual(0, disciple.BaseArmorAbsorption);
            Assert.IsTrue(TitanBossState.IsArmorCracked(disciple, 3));
            var upkeep = TitanBossMechanics.ResolveIncomingElementalDamage(disciple, TitanBossElement.Other, 0, 5);
            Assert.IsTrue(upkeep.ArmorRestored);
            Assert.AreEqual(6, disciple.BaseArmorAbsorption);
        }

        // ------------------------------------------------------------------
        // Typage élémentaire
        // ------------------------------------------------------------------

        [Test]
        public void ClassifyGrenadeKind_FirePlasmaColdOther()
        {
            Assert.AreEqual(TitanBossElement.Fire, TitanBossMechanics.ClassifyGrenadeKind("Incendiaire"));
            Assert.AreEqual(TitanBossElement.Fire, TitanBossMechanics.ClassifyGrenadeKind("Thermobarique"));
            Assert.AreEqual(TitanBossElement.Plasma, TitanBossMechanics.ClassifyGrenadeKind("Plasma"));
            Assert.AreEqual(TitanBossElement.Cold, TitanBossMechanics.ClassifyGrenadeKind("Cryo"));
            Assert.AreEqual(TitanBossElement.Other, TitanBossMechanics.ClassifyGrenadeKind("Fragmentation"));
            Assert.AreEqual(TitanBossElement.Other, TitanBossMechanics.ClassifyGrenadeKind(null));
        }

        // ------------------------------------------------------------------
        // RD-087 suite : bossAI JSON (défauts, override, fallback, délégation)
        // ------------------------------------------------------------------

        [Test]
        public void BossAIDefaults_MatchConsts()
        {
            var p = new TitanBossAIParams();
            Assert.AreEqual(TitanBossMechanics.FlameMemoryReductionPerHit, p.flameReductionPerHit);
            Assert.AreEqual(TitanBossMechanics.ClawSweepCostFew, p.sweepCostFew);
            Assert.AreEqual(TitanBossMechanics.ClawSweepCostFull, p.sweepCostFull);
            Assert.AreEqual(TitanBossMechanics.ClawSweepMaxTargets, p.sweepMaxTargets);
            Assert.AreEqual(TitanBossMechanics.ClawSweepRange, p.sweepRange);
            Assert.AreEqual(TitanBossMechanics.ClawSweepDamage, p.sweepDamage);
            Assert.AreEqual(TitanBossMechanics.ClawSweepKnockback, p.sweepKnockback);
            Assert.AreEqual(TitanBossMechanics.ClawSweepGroundedTurns, p.sweepGroundedTurns);
            Assert.AreEqual(TitanBossMechanics.TelluricChargeCost, p.chargeCost);
            Assert.AreEqual(TitanBossMechanics.TelluricChargeMinRange, p.chargeMinRange);
            Assert.AreEqual(TitanBossMechanics.TelluricChargeMaxRange, p.chargeMaxRange);
            Assert.AreEqual(TitanBossMechanics.TelluricCollapseDamage, p.chargeCollapseDamage);
            Assert.AreEqual(TitanBossMechanics.TelluricCollapseGroundedTurns, p.chargeCollapseGroundedTurns);
            Assert.AreEqual(TitanBossMechanics.ThermalAbsorbCost, p.absorbCost);
            Assert.AreEqual(TitanBossMechanics.VoidChainCost, p.chainCost);
            Assert.AreEqual(TitanBossMechanics.VoidChainMaxTargets, p.chainMaxTargets);
            Assert.AreEqual(TitanBossMechanics.VoidChainMaxRange, p.chainMaxRange);
            Assert.AreEqual(TitanBossMechanics.VoidChainDamage, p.chainDamage);
            Assert.AreEqual(TitanBossMechanics.VoidChainDestabilisedTurns, p.chainDestabilisedTurns);
            Assert.AreEqual(TitanBossMechanics.QuantumLeapCost, p.leapCost);
            Assert.AreEqual(TitanBossMechanics.QuantumLeapMaxRange, p.leapMaxRange);
            Assert.AreEqual(TitanBossMechanics.QuantumRiposteBonusEC, p.leapRiposteBonusEC);
            Assert.AreEqual(TitanBossMechanics.TectonicFistCost, p.fistCost);
            Assert.AreEqual(TitanBossMechanics.TectonicFistDamage, p.fistDamage);
            Assert.AreEqual(TitanBossMechanics.LithoRegenAmount, p.regenAmount);
            Assert.AreEqual(TitanBossMechanics.ThermalShockWindowRounds, p.shockWindowRounds);
            Assert.AreEqual(TitanBossMechanics.ThermalShockArmorLossTurns, p.shockArmorLossTurns);
            Assert.AreEqual(TitanBossMechanics.ThermalShockDamageMultiplier, p.shockDamageMultiplier);
        }

        [Test]
        public void BossAI_JsonRoundTrip_PartialBlockKeepsDefaults_MissingBlockIsDefaults()
        {
            const string json = @"{ ""version"": 2, ""source"": ""test"",
  ""entries"": [
    { ""id"": ""boss_bete_hybris"", ""name"": ""La Bête d'Hybris"", ""category"": ""Boss"",
      ""bossAI"": { ""sweepDamage"": 9, ""fistCost"": 5 } },
    { ""id"": ""civil_effraye"", ""name"": ""Civil Effrayé"", ""category"": ""Civil"" }
  ] }";
            var data = JsonUtility.FromJson<BestiaryData>(json);
            Assert.IsNotNull(data);
            Assert.AreEqual(2, data.entries.Count);
            var boss = data.entries[0].bossAI;
            Assert.IsNotNull(boss);
            Assert.AreEqual(9, boss.sweepDamage);
            Assert.AreEqual(5, boss.fistCost);
            Assert.AreEqual(TitanBossMechanics.VoidChainDamage, boss.chainDamage);
            // JsonUtility instancie les classes imbriquées absentes : défauts moteur.
            var civ = data.entries[1].bossAI;
            Assert.IsNotNull(civ);
            Assert.AreEqual(TitanBossMechanics.ClawSweepDamage, civ.sweepDamage);
            Assert.AreEqual(TitanBossMechanics.TectonicFistDamage, civ.fistDamage);
            // En C# pur, le champ reste nullable (null = défauts).
            Assert.IsNull(new BestiaryEntry().bossAI);
        }

        private static void InjectBoss(params BestiaryEntry[] entries)
        {
            TitanBossAIConfig.InjectEntries(new List<BestiaryEntry>(entries));
        }

        private static BestiaryEntry BossEntry(string id, string name, TitanBossAIParams bossAI, params string[] specs)
        {
            return new BestiaryEntry
            {
                id = id,
                name = name,
                category = "Boss",
                profile = "PnjBoss",
                specializations = new List<string>(specs),
                bossAI = bossAI
            };
        }

        [Test]
        public void BossAI_OverrideBeast_DrivesFlameSweepAndCharge()
        {
            InjectBoss(BossEntry("boss_bete_hybris", "La Bête d'Hybris",
                new TitanBossAIParams
                {
                    flameReductionPerHit = 5,
                    sweepCostFew = 9, sweepCostFull = 9, sweepMaxTargets = 1,
                    sweepRange = 1, sweepDamage = 9,
                    chargeCost = 9, chargeMinRange = 6, chargeMaxRange = 7
                },
                TitanBossMechanics.SpecFlameMemory, TitanBossMechanics.SpecClawSweep));
            var beast = MakeStats("La Bête d'Hybris", specs: new[] { TitanBossMechanics.SpecFlameMemory });
            Assert.AreEqual(9, TitanBossMechanics.ClawSweepCostFor(beast, 1));
            Assert.IsFalse(TitanBossMechanics.IsValidSweepTargetFor(beast, 2));
            Assert.IsTrue(TitanBossMechanics.IsValidSweepTargetFor(beast, 1));
            Assert.IsFalse(TitanBossMechanics.CanTelluricChargeFor(beast, 3));
            Assert.IsTrue(TitanBossMechanics.CanTelluricChargeFor(beast, 6));
            Assert.AreEqual(10, TitanBossMechanics.ResolveIncomingElementalDamage(beast, TitanBossElement.Fire, 10, 1).AdjustedDamage);
            Assert.AreEqual(5, TitanBossMechanics.ResolveIncomingElementalDamage(beast, TitanBossElement.Fire, 10, 1).AdjustedDamage);
        }

        [Test]
        public void BossAI_OverrideMinulican_DrivesAbsorbChainAndLeap()
        {
            InjectBoss(BossEntry("boss_minulican", "Le Minulican",
                new TitanBossAIParams
                {
                    absorbCost = 1, chainMaxTargets = 1, chainMaxRange = 1,
                    chainDamage = 9, leapCost = 9, leapMaxRange = 1, leapRiposteBonusEC = 5
                },
                TitanBossMechanics.SpecThermalAbsorption, TitanBossMechanics.SpecVoidChain));
            var minu = MakeStats("Le Minulican", ap: 1, specs: new[] { TitanBossMechanics.SpecThermalAbsorption });
            minu.CurrentHealth = minu.MaxHealth - 10;
            var res = TitanBossMechanics.ResolveIncomingElementalDamage(minu, TitanBossElement.Fire, 8, 1);
            Assert.IsTrue(res.Absorbed);
            Assert.AreEqual(4, res.AdjustedDamage);
            Assert.AreEqual(0, minu.CurrentActionPoints);
            Assert.IsFalse(TitanBossMechanics.IsValidChainTargetFor(minu, 2));
            Assert.AreEqual(1, TitanBossMechanics.ClampChainTargetsFor(minu, 5));
            Assert.IsFalse(TitanBossMechanics.CanQuantumLeapFor(minu, 2));
            Assert.AreEqual(10, TitanBossMechanics.QuantumRiposteDamage(minu, 5));
        }

        [Test]
        public void BossAI_OverrideDisciple_DrivesShockRegenAndFist()
        {
            InjectBoss(BossEntry("boss_disciple_terre", "Disciple de Terre de Capricius",
                new TitanBossAIParams
                {
                    fistDamage = 9, regenAmount = 9, shockWindowRounds = 1,
                    shockArmorLossTurns = 4, shockDamageMultiplier = 3
                },
                TitanBossMechanics.SpecTectonicFist, TitanBossMechanics.SpecThermalShock));
            var disciple = MakeStats("Disciple", armor: 6, specs: new[] { TitanBossMechanics.SpecThermalShock });
            TitanBossMechanics.ResolveIncomingElementalDamage(disciple, TitanBossElement.Cold, 5, 1);
            var fire = TitanBossMechanics.ResolveIncomingElementalDamage(disciple, TitanBossElement.Fire, 6, 2);
            Assert.IsTrue(fire.ShockTriggered);
            Assert.AreEqual(18, fire.AdjustedDamage);

            var regen = MakeStats("Disciple de Terre", specs: new[] { TitanBossMechanics.SpecLithoRegen });
            regen.CurrentHealth = regen.MaxHealth - 20;
            Assert.IsTrue(TitanBossMechanics.TickLithosphericRegen(regen, true, out _));
            Assert.AreEqual(regen.MaxHealth - 11, regen.CurrentHealth);
            Assert.AreEqual(
                TitanBossMechanics.ComputeTectonicStructureDamage(ObstacleMaterial.Pierre, 9),
                TitanBossMechanics.TectonicStructureDamageFor(regen, ObstacleMaterial.Pierre));
        }

        [Test]
        public void BossAI_UnknownBoss_FallsBackToDefaults()
        {
            InjectBoss();
            var plain = MakeStats("Soldat");
            Assert.AreSame(TitanBossAIConfig.Defaults, TitanBossAIConfig.GetParams(plain));
            Assert.AreSame(TitanBossAIConfig.Defaults, TitanBossAIConfig.GetParams(null));
        }

        [Test]
        public void BossAI_MissingEntry_FallsBackToDefaults()
        {
            InjectBoss(BossEntry("boss_minulican", "Le Minulican", new TitanBossAIParams(),
                TitanBossMechanics.SpecVoidChain));
            var beast = MakeStats("La Bête d'Hybris", specs: new[] { TitanBossMechanics.SpecFlameMemory });
            var p = TitanBossMechanics.ParamsFor(beast);
            Assert.AreEqual(TitanBossMechanics.ClawSweepDamage, p.sweepDamage);
            Assert.AreEqual(TitanBossMechanics.TelluricChargeCost, p.chargeCost);
            Assert.AreEqual(2, TitanBossMechanics.ClawSweepCostFor(beast, 1));
        }

        [Test]
        public void BossAI_NameFallback_MatchesMinulicanWithoutSpecs()
        {
            InjectBoss(BossEntry("boss_minulican", "Le Minulican",
                new TitanBossAIParams { chainDamage = 11 },
                TitanBossMechanics.SpecVoidChain));
            var nameless = MakeStats("Le Minulican");
            Assert.AreEqual(11, TitanBossMechanics.ParamsFor(nameless).chainDamage);
        }

        [Test]
        public void BossAI_Validate_ClampsAberrantValues()
        {
            var e = new BestiaryEntry
            {
                id = "boss_test",
                profile = "PnjBoss",
                bossAI = new TitanBossAIParams
                {
                    sweepDamage = 999, shockDamageMultiplier = 0,
                    chargeMinRange = 9, chargeMaxRange = 3, sweepMaxTargets = 0
                }
            };
            Assert.IsTrue(BestiaryCatalog.Validate(e, out _));
            Assert.AreEqual(30, e.bossAI.sweepDamage);
            Assert.AreEqual(1, e.bossAI.shockDamageMultiplier);
            Assert.AreEqual(9, e.bossAI.chargeMaxRange);
            Assert.AreEqual(1, e.bossAI.sweepMaxTargets);
        }

        private static void FakeBestiaryCache(List<BestiaryEntry> entries)
        {
            typeof(BestiaryCatalog).GetField("_cached", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, entries);
            typeof(BestiaryCatalog).GetField("_loadedFrom", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, "test:fake");
        }

        [Test]
        public void TitanBossCatalog_DelegatesToBestiaryJson()
        {
            FakeBestiaryCache(new List<BestiaryEntry>
            {
                new BestiaryEntry
                {
                    id = "boss_disciple_terre",
                    name = "Disciple TEST",
                    category = "Boss",
                    profile = "PnjBoss",
                    species = "Humain",
                    footprint = "Rosette7",
                    attributes = new BestiaryAttributes { FOR = 9, AGI = 2, CON = 9, RAP = 2, INT = 2, ERU = 1, CHA = 1, INS = 4, MAG = 4, Vision = 3, Ouie = 3, Miracle = 0 },
                    armor = 9,
                    skills = new List<BestiarySkillEntry>
                    {
                        new BestiarySkillEntry { skill = "MainsNues", level = 2 }
                    },
                    specializations = new List<string> { "Poing Tectonique", "Vulnérabilité Choc Thermique" },
                    equipment = new List<string>(),
                    equippedIndex = -1
                }
            });
            var sheet = TitanBossCatalog.BuildDiscipleDeTerreSheet();
            Assert.AreEqual("Disciple TEST", sheet.Name);
            Assert.AreEqual(9, sheet.BaseArmor);
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Poing Tectonique"));
            Assert.AreEqual(2, sheet.GetSkill(SkillType.MainsNues).TrainingLevel);
        }

        [Test]
        public void TitanBossCatalog_FallsBackToCode_WhenEntryMissing()
        {
            FakeBestiaryCache(new List<BestiaryEntry>());
            var sheet = TitanBossCatalog.BuildMinulicanSheet();
            Assert.AreEqual("Le Minulican", sheet.Name);
            Assert.AreEqual(2, sheet.BaseArmor);
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Éclairs de Néant en Chaîne"));
        }
    }
}
#endif
