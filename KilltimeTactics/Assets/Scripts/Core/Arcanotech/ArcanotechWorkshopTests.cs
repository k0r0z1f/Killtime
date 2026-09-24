#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using Killtime.Core.Arcanotech;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Tests
{
    [TestFixture]
    public class ArcanotechWorkshopTests
    {
        [Test]
        public void Test_GoldenRule_CreationXpEqualsActivationPa_SingleCategory()
        {
            var spell = new NythariteSpell(
                name: "Frappe Mentale",
                description: "Onde de choc purement offensive.",
                skill: SkillType.MagieEsprit,
                associatedAttribute: "Magie",
                discipline: PsychicDiscipline.Telekinesie,
                stage: PsychicStage.Stade2_AccesProfond,
                modules: new[]
                {
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_DirectDamage, 2), // 2 pts = +4 dégâts
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_Knockdown, 1)     // 2 pts = À terre
                }
            );

            // Total effets = 2 + 2 = 4 pts. Mono-catégorie (Offensif) => 0 pénalité.
            Assert.AreEqual(0, spell.HybridXpPenalty);
            Assert.AreEqual(4, spell.ActionPointCost);
            Assert.AreEqual(4, spell.CreationXpCost);
            Assert.AreEqual(spell.CreationXpCost, spell.ActionPointCost, "Règle d'or violée : 1 XP doit égaler 1 PA.");
        }

        [Test]
        public void Test_HybridRule_TwoCategories_AddsOneXpPenalty_WithoutIncreasingPa()
        {
            var hybridSpell = new NythariteSpell(
                name: "Frappe Électrique Survoltée",
                description: "Attaque offensive conférant l'état Survolté.",
                skill: SkillType.MagieElementale,
                associatedAttribute: "Magie",
                discipline: PsychicDiscipline.Pyrokynesie,
                stage: PsychicStage.Stade2_AccesProfond,
                modules: new[]
                {
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_DirectDamage, 1), // 1 pt (Offensif)
                    new PowerModuleSelection(ArcanotechModuleId.Utility_Survolte, 1)        // 4 pts (Utilitaire)
                }
            );

            // Total effets = 1 + 4 = 5 pts. 2 catégories => +1 XP de pénalité.
            Assert.AreEqual(1, hybridSpell.HybridXpPenalty);
            Assert.AreEqual(5, hybridSpell.ActionPointCost, "Le coût en PA ne doit PAS inclure la pénalité d'hybridation.");
            Assert.AreEqual(6, hybridSpell.CreationXpCost, "Le coût en XP doit inclure les 5 pts d'effets + 1 XP de pénalité.");
        }

        [Test]
        public void Test_HybridRule_ThreeCategories_AddsTwoXpPenalty_WithoutIncreasingPa()
        {
            var triHybrid = new NythariteSpell(
                name: "Tempête Tri-Fusionnelle",
                description: "Offensif + Défensif + Utilitaire combinés.",
                skill: SkillType.MagieEsprit,
                associatedAttribute: "Magie",
                discipline: PsychicDiscipline.Telepathie,
                stage: PsychicStage.Stade4_ProjectionSynaptique,
                modules: new[]
                {
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_DirectDamage, 1),          // 1 pt (Offensif)
                    new PowerModuleSelection(ArcanotechModuleId.Defensive_ResidualResistance, 1),    // 3 pts (Défensif)
                    new PowerModuleSelection(ArcanotechModuleId.Utility_Rapide, 1)                  // 3 pts (Utilitaire)
                }
            );

            // Total effets = 1 + 3 + 3 = 7 pts. 3 catégories => (3 - 1) = +2 XP de pénalité.
            Assert.AreEqual(2, triHybrid.HybridXpPenalty);
            Assert.AreEqual(7, triHybrid.ActionPointCost);
            Assert.AreEqual(9, triHybrid.CreationXpCost);
        }

        [Test]
        public void Test_LivreIV_OfficialExample_RalentissementGravitationnel()
        {
            var spell = ArcanotechWorkshop.CreateRalentissementGravitationnel();

            // Modificateurs de §19.3 : +1 jet (1 pt) + perte 4 PA (2 pts) = 3 pts
            Assert.AreEqual(3, spell.CreationXpCost);
            Assert.AreEqual(3, spell.ActionPointCost);
            Assert.AreEqual(0, spell.HybridXpPenalty);
        }

        [Test]
        public void Test_LivreIV_OfficialExample_EcrasementDeMatiere()
        {
            var spell = ArcanotechWorkshop.CreateEcrasementDeMatiere();

            // Modificateurs de §19.3 : +1 résultat (1 pt) + 2 Dégâts Absolus (2 pts) = 3 pts
            Assert.AreEqual(3, spell.CreationXpCost);
            Assert.AreEqual(3, spell.ActionPointCost);
            Assert.AreEqual(0, spell.HybridXpPenalty);
        }

        [Test]
        public void Test_RangeFormula_And_DistancePenalty_MatchesLivreIV16()
        {
            // Exemple canonique de Menna (Livre IV §16) :
            // MAG = 5, 1 entraînement en Magie, 1 entraînement en Magie de Feu => Portée = 5 + 1 + 1 = 7 cases.
            int baseRange = NythariteSpell.CalculateBaseRange(magicRank: 5, trainingCount: 2);
            Assert.AreEqual(7, baseRange);

            // Tranche 1 [1-7 cases] : 0 malus
            Assert.AreEqual(0, NythariteSpell.CalculateDistancePenalty(1, baseRange));
            Assert.AreEqual(0, NythariteSpell.CalculateDistancePenalty(7, baseRange));

            // Tranche 2 [8-14 cases] : -1 niveau
            Assert.AreEqual(-1, NythariteSpell.CalculateDistancePenalty(8, baseRange));
            Assert.AreEqual(-1, NythariteSpell.CalculateDistancePenalty(14, baseRange));

            // Tranche 3 [15-21 cases] (Menna attaque à 20 cases) : -2 niveaux
            Assert.AreEqual(-2, NythariteSpell.CalculateDistancePenalty(15, baseRange));
            Assert.AreEqual(-2, NythariteSpell.CalculateDistancePenalty(20, baseRange));
            Assert.AreEqual(-2, NythariteSpell.CalculateDistancePenalty(21, baseRange));

            // Tranche 4 [22-28 cases] : -3 niveaux
            Assert.AreEqual(-3, NythariteSpell.CalculateDistancePenalty(22, baseRange));
        }

        [Test]
        public void Test_CharacterSheet_PurchaseAndLearnSpell_DeductsXpAndUpdatesTotalSpent()
        {
            var sheet = new CharacterSheet
            {
                Name = "Mage Nytharite",
                AvailableXP = 10,
                TotalSpentXP = 0
            };

            var spell = ArcanotechWorkshop.CreateRalentissementGravitationnel(); // 3 XP

            bool success = ArcanotechWorkshop.LearnSpell(sheet, spell, out string msg);

            Assert.IsTrue(success, msg);
            Assert.AreEqual(7, sheet.AvailableXP);
            Assert.AreEqual(3, sheet.TotalSpentXP);
            Assert.AreEqual(1, sheet.LearnedSpells.Count);
            Assert.AreEqual("Ralentissement Gravitationnel", sheet.LearnedSpells[0].Name);
        }

        [Test]
        public void Test_CharacterSheet_InsufficientXp_RefusesPurchase()
        {
            var sheet = new CharacterSheet
            {
                Name = "Apprenti",
                AvailableXP = 2,
                TotalSpentXP = 0
            };

            var spell = ArcanotechWorkshop.CreateRalentissementGravitationnel(); // 3 XP requis

            bool success = ArcanotechWorkshop.LearnSpell(sheet, spell, out string reason);

            Assert.IsFalse(success);
            StringAssert.Contains("XP insuffisant", reason);
            Assert.AreEqual(2, sheet.AvailableXP);
            Assert.AreEqual(0, sheet.TotalSpentXP);
            Assert.AreEqual(0, sheet.LearnedSpells.Count);
        }

        [Test]
        public void Test_SpellCast_ConsumesActionPointsEqualToActionPointCost()
        {
            var attr = new Attributes(@for: 3, agi: 3, con: 4, rap: 3, @int: 4, eru: 2, cha: 1, ins: 2, mag: 5);
            var sheet = new CharacterSheet { BaseAttributes = attr };
            var caster = sheet.ToCombatStats();
            caster.CurrentActionPoints = 8;

            var spell = ArcanotechWorkshop.CreateRalentissementGravitationnel(); // 3 PA

            var targetAttr = new Attributes(@for: 3, agi: 3, con: 4, rap: 3, @int: 2, eru: 1, cha: 1, ins: 2, mag: 0);
            var target = new CharacterSheet { BaseAttributes = targetAttr }.ToCombatStats();

            var result = ArcanotechWorkshop.CastSpell(caster, target, spell, distanceInTiles: 3);

            Assert.AreEqual(3, result.ActionPointsSpent);
            Assert.AreEqual(5, caster.CurrentActionPoints);
        }

        [Test]
        public void Test_SpellCast_DirectAbsoluteDamage_IgnoresArmorAndEncaissement()
        {
            var casterSheet = new CharacterSheet
            {
                BaseAttributes = new Attributes(@for: 2, agi: 2, con: 3, rap: 2, @int: 4, eru: 2, cha: 1, ins: 2, mag: 5)
            };
            var caster = casterSheet.ToCombatStats();

            var targetSheet = new CharacterSheet
            {
                BaseArmor = 5,
                BaseAttributes = new Attributes(@for: 5, agi: 2, con: 6, rap: 2, @int: 2, eru: 1, cha: 1, ins: 2, mag: 0)
            };
            var target = targetSheet.ToCombatStats();
            int hpBefore = target.CurrentHealth;

            var spell = ArcanotechWorkshop.CreateEcrasementDeMatiere(); // 2 Dégâts Absolus

            var result = ArcanotechWorkshop.CastSpell(caster, target, spell, distanceInTiles: 2);

            Assert.AreEqual(2, result.AbsoluteDamageDealt);
            Assert.AreEqual(2, result.TotalDamageApplied, "Les dégâts absolus doivent ignorer l'armure de 5.");
            Assert.AreEqual(hpBefore - 2, target.CurrentHealth);
        }

        [Test]
        public void Test_SpellCast_AppliesStatuses_Debalance_Ralenti_And_DrainAP()
        {
            var caster = new CharacterSheet
            {
                BaseAttributes = new Attributes(@for: 2, agi: 2, con: 3, rap: 2, @int: 4, eru: 2, cha: 1, ins: 2, mag: 5)
            }.ToCombatStats();

            var target = new CharacterSheet
            {
                BaseAttributes = new Attributes(@for: 3, agi: 3, con: 3, rap: 3, @int: 2, eru: 1, cha: 1, ins: 2, mag: 0)
            }.ToCombatStats();
            target.CurrentActionPoints = 6;

            var spell = new NythariteSpell(
                name: "Vortex de Gravité",
                description: "Ralentit et débalance la cible en drainant ses PA.",
                skill: SkillType.MagieEsprit,
                associatedAttribute: "Magie",
                discipline: PsychicDiscipline.Telekinesie,
                stage: PsychicStage.Stade2_AccesProfond,
                modules: new[]
                {
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_Ralenti, 1),
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_Debalance, 1),
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_DrainAP, 1) // 2 PA perdus
                }
            );

            var result = ArcanotechWorkshop.CastSpell(caster, target, spell, distanceInTiles: 2);

            Assert.AreEqual(2, result.TargetAPLost);
            Assert.AreEqual(4, target.CurrentActionPoints);
            Assert.IsTrue(target.ActiveStatus.HasFlag(StatusEffect.Ralenti));
            Assert.IsTrue(target.ActiveStatus.HasFlag(StatusEffect.Debalance));
        }

        [Test]
        public void Test_SpellCast_SymbioticImmunity_ThomasImmuneToMinaAndLucas()
        {
            var minaSheet = MinaCharacter.BuildHeroicSheet();
            var minaStats = minaSheet.ToCombatStats();

            var thomasSheet = ThomasCharacter.BuildHeroicSheet();
            var thomasStats = thomasSheet.ToCombatStats();
            int hpBefore = thomasStats.CurrentHealth;

            var offensiveSpell = new NythariteSpell(
                name: "Flamme Primale",
                description: "Feu végétal direct.",
                skill: SkillType.MagiePrimale,
                associatedAttribute: "Magie",
                discipline: PsychicDiscipline.Biokinesie,
                stage: PsychicStage.Stade2_AccesProfond,
                modules: new[]
                {
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_DirectDamage, 4) // 8 dégâts
                }
            );

            var result = ArcanotechWorkshop.CastSpell(minaStats, thomasStats, offensiveSpell, distanceInTiles: 2);

            Assert.IsTrue(result.WasImmunizedBySymbiosis);
            Assert.AreEqual(0, result.TotalDamageApplied);
            Assert.AreEqual(hpBefore, thomasStats.CurrentHealth);
        }

        [Test]
        public void Test_BackwardCompatibility_NythariteSpellConstructor()
        {
            var spell = new NythariteSpell("Décharge Cinétique", PsychicDiscipline.Telekinesie, PsychicStage.Stade2_AccesProfond, costXpPa: 4);

            Assert.AreEqual(4, spell.CreationXpCost);
            Assert.AreEqual(4, spell.ActionPointCost);
            Assert.AreEqual(0, spell.HybridXpPenalty);
        }
    }
}
#endif