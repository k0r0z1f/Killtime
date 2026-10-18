#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Tactics.CombatUI;

namespace Killtime.Tests
{
    [TestFixture]
    public class DuoTechTests
    {
        private static CharacterStats MakeLucas()
        {
            // Fiche héroïque : PA = max(3,5)+4+2 = 11.
            var attr = new Attributes(@for: 2, agi: 3, con: 3, rap: 4, @int: 5, eru: 3, cha: 3, ins: 3, mag: 5);
            return new CharacterStats("Lucas", attr);
        }

        private static CharacterStats MakeMina()
        {
            // PA = max(5,3)+4+2 = 11.
            var attr = new Attributes(@for: 3, agi: 5, con: 3, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 5);
            return new CharacterStats("Mina", attr);
        }

        private static CharacterStats MakeThomas()
        {
            // Fiche héroïque : FOR 5 AGI 3 CON 4 RAP 3 INT 2 ERU 1 CHA 3 INS 3.
            // PA = Max(3,2) + 3 + Min(1) = 7.
            var attr = new Attributes(@for: 5, agi: 3, con: 4, rap: 3, @int: 2, eru: 1, cha: 3, ins: 3, mag: 0);
            return new CharacterStats("Thomas", attr);
        }

        private static CharacterStats MakeSbire(string name = "Sbire")
        {
            var attr = new Attributes(@for: 2, agi: 2, con: 2, rap: 2, @int: 2, eru: 2, cha: 2, ins: 2);
            return new CharacterStats(name, attr);
        }

        // ==================== IDENTITÉ ====================

        [Test]
        public void TestDuo_RequiresLucasPlusMina()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var sbire = MakeSbire();

            Assert.IsTrue(DuoTechRegistry.AreComplementary(lucas, mina));
            Assert.IsFalse(DuoTechRegistry.AreComplementary(lucas, lucas));
            Assert.IsFalse(DuoTechRegistry.AreComplementary(lucas, sbire));
            Assert.IsFalse(DuoTechRegistry.AreComplementary(mina, mina));
        }

        // ==================== PAIEMENT IMMÉDIAT ====================

        [Test]
        public void TestDuo_SillagePayment_DebitsBothImmediately()
        {
            var lucas = MakeLucas(); // 11 PA
            var mina = MakeMina();   // 11 PA
            var def = DuoTechRegistry.GetDef(DuoTechId.SillageIgne); // 1 + 3 + 4 = 8

            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(lucas, mina, def, 1));
            Assert.AreEqual(11 - (1 + 4), lucas.CurrentActionPoints);
            Assert.AreEqual(11 - 3, mina.CurrentActionPoints);
        }

        [Test]
        public void TestDuo_PartnerWithoutPA_RefusesAndRefundsInitiator()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            mina.CurrentActionPoints = 1; // < 3 requis pour Sillage
            var def = DuoTechRegistry.GetDef(DuoTechId.SillageIgne);

            Assert.IsFalse(DuoTechRegistry.TryDeclarePayment(lucas, mina, def, 1));
            Assert.AreEqual(11, lucas.CurrentActionPoints); // remboursé
            Assert.AreEqual(1, mina.CurrentActionPoints);   // intact
        }

        [Test]
        public void TestDuo_PaymentDoesNotConsumeMainAction()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var def = DuoTechRegistry.GetDef(DuoTechId.LacetNytharite);

            int attacksBefore = lucas.AttacksThisTurn;
            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(mina, lucas, def, 1));
            Assert.AreEqual(attacksBefore, lucas.AttacksThisTurn); // action unique : pas de RegisterAttack
        }

        // ==================== LIMITE 1 DUO / ROUND ====================

        [Test]
        public void TestDuo_SecondDuoSameRound_RefusedWithoutPayment()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var sillage = DuoTechRegistry.GetDef(DuoTechId.SillageIgne);
            var lacet = DuoTechRegistry.GetDef(DuoTechId.LacetNytharite);

            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(lucas, mina, sillage, 1));
            int lucasPA = lucas.CurrentActionPoints;
            int minaPA = mina.CurrentActionPoints;

            // Même initiateur, autre duo, même round : refusé, PA intacts.
            Assert.IsFalse(DuoTechRegistry.TryDeclarePayment(lucas, mina, lacet, 1));
            Assert.AreEqual(lucasPA, lucas.CurrentActionPoints);
            Assert.AreEqual(minaPA, mina.CurrentActionPoints);

            // L'autre moitié comme initiatrice : refusé aussi (limite par participant).
            Assert.IsFalse(DuoTechRegistry.TryDeclarePayment(mina, lucas, lacet, 1));
        }

        [Test]
        public void TestDuo_NextRound_AllowedAgain()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var sillage = DuoTechRegistry.GetDef(DuoTechId.SillageIgne);
            var lacet = DuoTechRegistry.GetDef(DuoTechId.LacetNytharite);

            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(lucas, mina, sillage, 1));
            lucas.ResetTurn();
            mina.ResetTurn();
            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(mina, lucas, lacet, 2));
        }

        [Test]
        public void TestDuo_ExplorationRoundZero_NoLimit()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var lacet = DuoTechRegistry.GetDef(DuoTechId.LacetNytharite);

            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(lucas, mina, lacet, 0));
            lucas.ResetTurn();
            mina.ResetTurn();
            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(lucas, mina, lacet, 0));
        }

        // ==================== GÉOMÉTRIE ====================

        [Test]
        public void TestDuo_AlignedEnemies_Validated()
        {
            var a = new WeaveVec2(0f, 0f);
            var b = new WeaveVec2(6f, 0f);
            var pts = new List<WeaveVec2>
            {
                new WeaveVec2(1f, 0.1f),
                new WeaveVec2(3f, -0.1f),
                new WeaveVec2(5f, 0.05f)
            };
            Assert.IsTrue(DuoTechGeometry.AreAligned(a, b, pts, 0.6f));
        }

        [Test]
        public void TestDuo_ScatteredEnemies_Rejected()
        {
            var a = new WeaveVec2(0f, 0f);
            var b = new WeaveVec2(6f, 0f);
            var pts = new List<WeaveVec2>
            {
                new WeaveVec2(1f, 0.1f),
                new WeaveVec2(3f, 2.5f),
                new WeaveVec2(5f, 0.05f)
            };
            Assert.IsFalse(DuoTechGeometry.AreAligned(a, b, pts, 0.6f));
        }

        [Test]
        public void TestDuo_LoopClosure_Detected()
        {
            var loop = new List<WeaveVec2>
            {
                new WeaveVec2(0f, 0f), new WeaveVec2(2f, 0f),
                new WeaveVec2(2f, 2f), new WeaveVec2(0f, 2f),
                new WeaveVec2(0.1f, 0.1f)
            };
            Assert.IsTrue(DuoTechGeometry.IsLoopClosed(loop));

            var open = new List<WeaveVec2>
            {
                new WeaveVec2(0f, 0f), new WeaveVec2(3f, 0f), new WeaveVec2(6f, 0f)
            };
            Assert.IsFalse(DuoTechGeometry.IsLoopClosed(open));
        }

        [Test]
        public void TestDuo_SyncGrade_Boundaries()
        {
            // waveTime = arcT1 / minaSpeed ; lucasTime = arcT2 / lucasSpeed.
            // Perfect : wave 1.0s, lucas 0.7s -> lead 0.3s.
            Assert.AreEqual(WeaveSyncGrade.Perfect, DuoTechGeometry.GradeSync(4f, 8.4f, 4f, 12f));
            // Risky : wave 1.0s, lucas 0.95s -> lead 0.05s.
            Assert.AreEqual(WeaveSyncGrade.Risky, DuoTechGeometry.GradeSync(4f, 11.4f, 4f, 12f));
            // Late : wave 2.0s, lucas 0.5s -> lead 1.5s.
            Assert.AreEqual(WeaveSyncGrade.Late, DuoTechGeometry.GradeSync(8f, 6f, 4f, 12f));
        }

        [Test]
        public void TestDuo_SillagePerfect_AppliesDashPlusFireAndBurn()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var e1 = MakeSbire("E1");
            var e2 = MakeSbire("E2");
            int hp1 = e1.CurrentHealth, hp2 = e2.CurrentHealth;

            var logs = new List<string>();
            DuoTechRegistry.ResolveSillage(mina, lucas,
                new List<CharacterStats> { e1, e2 }, WeaveSyncGrade.Perfect, logs.Add);

            Assert.AreEqual(hp1 - 7, e1.CurrentHealth); // 4 dash + 3 feu
            Assert.AreEqual(hp2 - 7, e2.CurrentHealth);
            Assert.IsTrue(e1.ActiveStatus.HasFlag(StatusEffect.EnFeu));
        }

        [Test]
        public void TestDuo_SillageRisky_BurnsMina()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            int minaHp = mina.CurrentHealth;
            var e1 = MakeSbire("E1");

            var logs = new List<string>();
            DuoTechRegistry.ResolveSillage(mina, lucas,
                new List<CharacterStats> { e1 }, WeaveSyncGrade.Risky, logs.Add);

            Assert.AreEqual(minaHp - 3, mina.CurrentHealth); // friendly fire
        }

        [Test]
        public void TestDuo_Lacet_ImmobilisesOnlyIfClosed()
        {
            var target = MakeSbire("Cible");
            var logs = new List<string>();

            DuoTechRegistry.ResolveLacet(target, true, logs.Add);
            Assert.IsTrue(target.ActiveStatus.HasFlag(StatusEffect.Immobilise));

            var target2 = MakeSbire("Cible2");
            DuoTechRegistry.ResolveLacet(target2, false, logs.Add);
            Assert.IsFalse(target2.ActiveStatus.HasFlag(StatusEffect.Immobilise));
        }

        [Test]
        public void TestDuo_Fournaise_StunsAndDrainsPA()
        {
            var target = MakeSbire("Isole");
            target.ResetTurn();
            int pa = target.CurrentActionPoints;
            var logs = new List<string>();

            DuoTechRegistry.ResolveFournaise(target, true, logs.Add);
            Assert.IsTrue(target.ActiveStatus.HasFlag(StatusEffect.Etourdi));
            Assert.AreEqual(pa - 2, target.CurrentActionPoints);
        }

        // ==================== RD-085 : TABLE §25.5 (6 MANQUANTES) ====================

        [Test]
        public void TestDuo_RD085_AllNineDefs_CostsAndND()
        {
            var expected = new (DuoTechId id, int total, int nd, string attrs)[]
            {
                (DuoTechId.SillageIgne, 8, 5, "AGI+INT"),
                (DuoTechId.LacetNytharite, 5, 5, "AGI+INT"),
                (DuoTechId.FournaiseRetardement, 7, 5, "AGI+INT"),
                (DuoTechId.TrempeInversee, 9, 6, "AGI+INT"),
                (DuoTechId.RailgunArtisanale, 11, 7, "FOR+INT"),
                (DuoTechId.CisailleEntropique, 9, 6, "AGI+INT"),
                (DuoTechId.Parallaxe, 6, 5, "AGI+INT"),
                (DuoTechId.MirageDeuxTemps, 4, 4, "AGI+INT"),
                (DuoTechId.UppercutThermobarique, 9, 6, "FOR+INT"),
                (DuoTechId.RempartTellurique, 8, 6, "FOR+AGI"),
                (DuoTechId.ZeroTectonique, 9, 6, "FOR+INT"),
                (DuoTechId.TriFusion, 11, 7, "FOR+AGI+INT"),
            };
            Assert.AreEqual(12, DuoTechRegistry.GetAllDefs().Count);
            for (int i = 0; i < expected.Length; i++)
            {
                var def = DuoTechRegistry.GetDef(expected[i].id);
                Assert.IsNotNull(def, expected[i].id.ToString());
                Assert.AreEqual(expected[i].total, def.TotalCost, expected[i].id.ToString());
                Assert.AreEqual(expected[i].nd, def.SyncND, expected[i].id.ToString());
                Assert.AreEqual(expected[i].attrs, def.SyncAttrs, expected[i].id.ToString());
            }
        }

        [Test]
        public void TestDuo_RD085_TableOutcome_Matrix()
        {
            Assert.AreEqual(DuoTechOutcome.Perfect, DuoTechRegistry.TableOutcome(true, true));
            Assert.AreEqual(DuoTechOutcome.Partial, DuoTechRegistry.TableOutcome(true, false));
            Assert.AreEqual(DuoTechOutcome.Partial, DuoTechRegistry.TableOutcome(false, true));
            Assert.AreEqual(DuoTechOutcome.Miss, DuoTechRegistry.TableOutcome(false, false));
        }

        [Test]
        public void TestDuo_RD085_Payments_AllSix()
        {
            var costs = new (DuoTechId id, int minaNeed, int lucasNeed)[]
            {
                (DuoTechId.TrempeInversee, 4, 4),
                (DuoTechId.RailgunArtisanale, 5, 5),
                (DuoTechId.CisailleEntropique, 4, 4),
                (DuoTechId.Parallaxe, 3, 2),
                (DuoTechId.MirageDeuxTemps, 2, 1),
                (DuoTechId.UppercutThermobarique, 4, 4),
            };
            for (int i = 0; i < costs.Length; i++)
            {
                var lucas = MakeLucas();
                var mina = MakeMina();
                var def = DuoTechRegistry.GetDef(costs[i].id);
                Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(mina, lucas, def, 0), costs[i].id.ToString());
                Assert.AreEqual(11 - (1 + costs[i].minaNeed), mina.CurrentActionPoints, costs[i].id.ToString());
                Assert.AreEqual(11 - costs[i].lucasNeed, lucas.CurrentActionPoints, costs[i].id.ToString());
            }
        }

        [Test]
        public void TestDuo_RD085_Trempe_PerfectRalentiPlusFissure_PartialRalentiOnly()
        {
            var e1 = MakeSbire("E1");
            var e2 = MakeSbire("E2");
            e1.BaseArmorAbsorption = 3;
            e2.BaseArmorAbsorption = 3;
            var logs = new List<string>();

            DuoTechRegistry.ResolveTrempe(
                new List<CharacterStats> { e1, e2 }, DuoTechOutcome.Perfect, logs.Add);
            Assert.IsTrue(e1.ActiveStatus.HasFlag(StatusEffect.Ralenti));
            Assert.IsTrue(e2.ActiveStatus.HasFlag(StatusEffect.Ralenti));
            Assert.AreEqual(1, e1.BaseArmorAbsorption); // 3 - 2
            Assert.IsTrue(DuoTechRegistry.HasArmorFissure(e1));
            DuoTechRegistry.RestoreArmorFissure(e1);
            Assert.AreEqual(3, e1.BaseArmorAbsorption);
            DuoTechRegistry.ClearAllArmorFissures();

            var e3 = MakeSbire("E3");
            e3.BaseArmorAbsorption = 3;
            DuoTechRegistry.ResolveTrempe(
                new List<CharacterStats> { e3 }, DuoTechOutcome.Partial, logs.Add);
            Assert.IsTrue(e3.ActiveStatus.HasFlag(StatusEffect.Ralenti));
            Assert.AreEqual(3, e3.BaseArmorAbsorption); // pas de fissure en Semi
            Assert.IsFalse(DuoTechRegistry.HasArmorFissure(e3));

            var e4 = MakeSbire("E4");
            DuoTechRegistry.ResolveTrempe(
                new List<CharacterStats> { e4 }, DuoTechOutcome.Miss, logs.Add);
            Assert.IsFalse(e4.ActiveStatus.HasFlag(StatusEffect.Ralenti));
        }

        [Test]
        public void TestDuo_RD085_Railgun_PerfectHalvesArmor_PartialBrut()
        {
            var logs = new List<string>();

            var hard = MakeSbire("Dure");
            hard.BaseArmorAbsorption = 4; // moitié = 2 → 8 - 2 = 6 nets
            int hp = hard.CurrentHealth;
            DuoTechRegistry.ResolveRailgun(hard, DuoTechOutcome.Perfect, logs.Add);
            Assert.AreEqual(hp - 6, hard.CurrentHealth);

            var soft = MakeSbire("Mollet");
            int hp2 = soft.CurrentHealth;
            DuoTechRegistry.ResolveRailgun(soft, DuoTechOutcome.Partial, logs.Add);
            Assert.AreEqual(hp2 - 5, soft.CurrentHealth);

            var missed = MakeSbire("Loin");
            int hp3 = missed.CurrentHealth;
            DuoTechRegistry.ResolveRailgun(missed, DuoTechOutcome.Miss, logs.Add);
            Assert.AreEqual(hp3, missed.CurrentHealth);
        }

        [Test]
        public void TestDuo_RD085_Cisaille_PerfectBleeds_PartialNoBleed()
        {
            var logs = new List<string>();
            var e1 = MakeSbire("E1");
            var e2 = MakeSbire("E2");
            int hp1 = e1.CurrentHealth, hp2 = e2.CurrentHealth;

            DuoTechRegistry.ResolveCisaille(
                new List<CharacterStats> { e1, e2 }, DuoTechOutcome.Perfect, logs.Add);
            Assert.AreEqual(hp1 - 5, e1.CurrentHealth);
            Assert.AreEqual(hp2 - 5, e2.CurrentHealth);
            Assert.IsTrue(e1.ActiveStatus.HasFlag(StatusEffect.Saignement));
            Assert.IsTrue(e2.ActiveStatus.HasFlag(StatusEffect.Saignement));

            var e3 = MakeSbire("E3");
            int hp3 = e3.CurrentHealth;
            DuoTechRegistry.ResolveCisaille(
                new List<CharacterStats> { e3 }, DuoTechOutcome.Partial, logs.Add);
            Assert.AreEqual(hp3 - 3, e3.CurrentHealth);
            Assert.IsFalse(e3.ActiveStatus.HasFlag(StatusEffect.Saignement));
        }

        [Test]
        public void TestDuo_RD085_Parallaxe_PerfectAndSemiDealFour_MissDealsNothing()
        {
            var logs = new List<string>();
            var t1 = MakeSbire("T1");
            int hp1 = t1.CurrentHealth;
            DuoTechRegistry.ResolveParallaxe(t1, DuoTechOutcome.Perfect, logs.Add);
            Assert.AreEqual(hp1 - 4, t1.CurrentHealth);

            var t2 = MakeSbire("T2");
            int hp2 = t2.CurrentHealth;
            DuoTechRegistry.ResolveParallaxe(t2, DuoTechOutcome.Partial, logs.Add);
            Assert.AreEqual(hp2 - 4, t2.CurrentHealth); // canon littéral : mêmes 4 bruts, défense -2

            var t3 = MakeSbire("T3");
            int hp3 = t3.CurrentHealth;
            DuoTechRegistry.ResolveParallaxe(t3, DuoTechOutcome.Miss, logs.Add);
            Assert.AreEqual(hp3, t3.CurrentHealth);
        }

        [Test]
        public void TestDuo_RD085_Mirage_PerfectGrantsFreeReaction()
        {
            var mina = MakeMina();
            var logs = new List<string>();
            Assert.IsFalse(mina.HasFreeDefensiveReaction);

            DuoTechRegistry.ResolveMirage(mina, DuoTechOutcome.Perfect, logs.Add);
            Assert.IsTrue(mina.HasFreeDefensiveReaction);
            Assert.IsTrue(mina.ConsumeFreeDefensiveReaction());
            Assert.IsFalse(mina.HasFreeDefensiveReaction);

            DuoTechRegistry.ResolveMirage(mina, DuoTechOutcome.Partial, logs.Add);
            Assert.IsFalse(mina.HasFreeDefensiveReaction); // Semi = replacement seul
        }

        [Test]
        public void TestDuo_RD085_Uppercut_PerfectKnocksDown_MissPunishesEss()
        {
            var mina = MakeMina();
            var lucas = MakeLucas();
            var logs = new List<string>();

            // Cible endurcie (CON 4) : 7 bruts restent non-létaux à mi-PV,
            // sinon EvaluateFatalBlow forcerait PV à 0 (voie létale), pas hp-7.
            var tankyAttrs = new Attributes(@for: 2, agi: 2, con: 4, rap: 2, @int: 2, eru: 2, cha: 2, ins: 2);
            var cible = new CharacterStats("Entravee", tankyAttrs);
            cible.ApplyStatus(StatusEffect.Etourdi, 1);
            cible.CurrentHealth = cible.MaxHealth / 2;
            Assert.IsTrue(DuoTechRegistry.IsUppercutCondition(cible));
            int hp = cible.CurrentHealth;
            Assert.IsTrue(hp - 7 > 0, "Précondition test : 7 bruts non-létaux.");
            DuoTechRegistry.ResolveUppercut(mina, lucas, cible, DuoTechOutcome.Perfect, logs.Add);
            Assert.AreEqual(hp - 7, cible.CurrentHealth);
            Assert.IsTrue(cible.ActiveStatus.HasFlag(StatusEffect.ATerre));

            var cible2 = MakeSbire("Entravee2");
            cible2.ApplyStatus(StatusEffect.Ralenti, 1);
            cible2.CurrentHealth = cible2.MaxHealth / 2;
            int hp2 = cible2.CurrentHealth;
            DuoTechRegistry.ResolveUppercut(mina, lucas, cible2, DuoTechOutcome.Partial, logs.Add);
            Assert.AreEqual(hp2 - 4, cible2.CurrentHealth);
            Assert.IsFalse(cible2.ActiveStatus.HasFlag(StatusEffect.ATerre));

            int essM = mina.Essoufflement, essL = lucas.Essoufflement;
            DuoTechRegistry.ResolveUppercut(mina, lucas, cible2, DuoTechOutcome.Miss, logs.Add);
            Assert.AreEqual(essM + 1, mina.Essoufflement);
            Assert.AreEqual(essL + 1, lucas.Essoufflement);

            var saine = MakeSbire("Saine");
            Assert.IsFalse(DuoTechRegistry.IsUppercutCondition(saine)); // ni entravée ni ≤ 50%
        }

        [Test]
        public void TestDuo_RD085_Geometry_OppositionCorridorCrossing()
        {
            var mina = new WeaveVec2(-3f, 0f);
            var lucas = new WeaveVec2(3f, 0f);
            var cible = new WeaveVec2(0f, 0f);
            Assert.IsTrue(DuoTechGeometry.AreOpposed180(mina, lucas, cible));
            Assert.IsFalse(DuoTechGeometry.AreOpposed180(
                new WeaveVec2(3f, 0f), new WeaveVec2(3f, 1f), cible));

            var t1 = new List<WeaveVec2> { new WeaveVec2(0f, -2f), new WeaveVec2(0f, 0f), new WeaveVec2(0f, 2f) };
            var t2 = new List<WeaveVec2> { new WeaveVec2(-2f, 0f), new WeaveVec2(0f, 0f), new WeaveVec2(2f, 0f) };
            Assert.IsTrue(DuoTechGeometry.TryFindCrossingPoint(t1, t2, 1.2f, out _));

            var start = new WeaveVec2(0f, 0f);
            var end = new WeaveVec2(8f, 0f);
            Assert.IsTrue(DuoTechGeometry.IsStraightCorridor(start, end, 6.8f, 1.0f, null));
            var obstacles = new List<WeaveVec2> { new WeaveVec2(4f, 0.2f) };
            Assert.IsFalse(DuoTechGeometry.IsStraightCorridor(start, end, 6.8f, 1.0f, obstacles));
            Assert.IsTrue(DuoTechGeometry.IsStraightCorridor(start, end, 6.8f, 1.0f, obstacles, 0));
        }

        // ==================== RD-086 : SYNERGIES THOMAS ====================

        [Test]
        public void TestDuo_RD086_Identity_ThomasPairsAndTrio()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var thomas = MakeThomas();
            var sbire = MakeSbire();

            Assert.IsTrue(DuoTechRegistry.IsThomasMember(thomas));
            Assert.IsFalse(DuoTechRegistry.IsThomasMember(mina));
            Assert.IsFalse(DuoTechRegistry.IsThomasMember(lucas));
            Assert.IsTrue(DuoTechRegistry.IsTrioMember(thomas));

            Assert.IsTrue(DuoTechRegistry.AreThomasMina(thomas, mina));
            Assert.IsTrue(DuoTechRegistry.AreThomasMina(mina, thomas));
            Assert.IsFalse(DuoTechRegistry.AreThomasMina(lucas, mina));
            Assert.IsFalse(DuoTechRegistry.AreThomasMina(thomas, thomas));
            Assert.IsFalse(DuoTechRegistry.AreThomasMina(thomas, sbire));

            Assert.IsTrue(DuoTechRegistry.AreThomasLucas(thomas, lucas));
            Assert.IsTrue(DuoTechRegistry.AreThomasLucas(lucas, thomas));
            Assert.IsFalse(DuoTechRegistry.AreThomasLucas(mina, lucas));
            Assert.IsFalse(DuoTechRegistry.AreThomasLucas(thomas, sbire));

            Assert.IsTrue(DuoTechRegistry.AreTrio(thomas, mina, lucas));
            Assert.IsTrue(DuoTechRegistry.AreTrio(mina, lucas, thomas));
            Assert.IsFalse(DuoTechRegistry.AreTrio(thomas, mina, mina));
            Assert.IsFalse(DuoTechRegistry.AreTrio(thomas, mina, sbire));

            // Les 9 classiques restent Lucas+Mina uniquement.
            Assert.IsFalse(DuoTechRegistry.AreComplementary(thomas, mina));
            Assert.IsFalse(DuoTechRegistry.AreComplementary(thomas, lucas));
        }

        [Test]
        public void TestDuo_RD086_RempartPayment_ThomasPaysFive()
        {
            var thomas = MakeThomas(); // 7 PA
            var mina = MakeMina();     // 11 PA
            var def = DuoTechRegistry.GetDef(DuoTechId.RempartTellurique); // 1 + 3 + 4 = 8

            Assert.IsTrue(DuoTechRegistry.TryDeclarePairPayment(thomas, mina, def, 1));
            Assert.AreEqual(7 - (1 + 4), thomas.CurrentActionPoints);
            Assert.AreEqual(11 - 3, mina.CurrentActionPoints);
        }

        [Test]
        public void TestDuo_RD086_ZeroPayment_ThomasPaysFive()
        {
            var thomas = MakeThomas(); // 7 PA
            var lucas = MakeLucas();   // 11 PA
            var def = DuoTechRegistry.GetDef(DuoTechId.ZeroTectonique); // 1 + 4 + 4 = 9

            Assert.IsTrue(DuoTechRegistry.TryDeclarePairPayment(lucas, thomas, def, 2));
            Assert.AreEqual(11 - (1 + 4), lucas.CurrentActionPoints);
            Assert.AreEqual(7 - 4, thomas.CurrentActionPoints);
        }

        [Test]
        public void TestDuo_RD086_WrongPair_RefusedWithoutPayment()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var rempart = DuoTechRegistry.GetDef(DuoTechId.RempartTellurique);

            Assert.IsFalse(DuoTechRegistry.TryDeclarePairPayment(lucas, mina, rempart, 1));
            Assert.AreEqual(11, lucas.CurrentActionPoints);
            Assert.AreEqual(11, mina.CurrentActionPoints);
        }

        [Test]
        public void TestDuo_RD086_TrioPayment_AllThreeDebited()
        {
            var thomas = MakeThomas(); // 7 PA
            var mina = MakeMina();     // 11 PA
            var lucas = MakeLucas();   // 11 PA
            var def = DuoTechRegistry.GetDef(DuoTechId.TriFusion); // 1 + 3 + 3 + 4 = 11

            Assert.IsTrue(DuoTechRegistry.TryDeclareTrioPayment(thomas, mina, lucas, def, 1));
            Assert.AreEqual(7 - (1 + 4), thomas.CurrentActionPoints);
            Assert.AreEqual(11 - 3, mina.CurrentActionPoints);
            Assert.AreEqual(11 - 3, lucas.CurrentActionPoints);
        }

        [Test]
        public void TestDuo_RD086_TrioSecondDeclaration_RefusedWithoutPayment()
        {
            var thomas = MakeThomas();
            var mina = MakeMina();
            var lucas = MakeLucas();
            var trio = DuoTechRegistry.GetDef(DuoTechId.TriFusion);
            var rempart = DuoTechRegistry.GetDef(DuoTechId.RempartTellurique);

            Assert.IsTrue(DuoTechRegistry.TryDeclareTrioPayment(thomas, mina, lucas, trio, 1));

            // Même round : trio ou paire Thomas refusés (tampon 1/round).
            Assert.IsFalse(DuoTechRegistry.TryDeclareTrioPayment(mina, lucas, thomas, trio, 1));
            Assert.IsFalse(DuoTechRegistry.TryDeclarePairPayment(thomas, mina, rempart, 1));
            Assert.AreEqual(2, thomas.CurrentActionPoints); // inchangé après refus
        }

        [Test]
        public void TestDuo_RD086_TrioNextRound_AllowedAgain()
        {
            var thomas = MakeThomas();
            var mina = MakeMina();
            var lucas = MakeLucas();
            var trio = DuoTechRegistry.GetDef(DuoTechId.TriFusion);

            Assert.IsTrue(DuoTechRegistry.TryDeclareTrioPayment(thomas, mina, lucas, trio, 1));
            thomas.ResetTurn();
            mina.ResetTurn();
            lucas.ResetTurn();
            Assert.IsTrue(DuoTechRegistry.TryDeclareTrioPayment(mina, lucas, thomas, trio, 2));
        }

        [Test]
        public void TestDuo_RD086_Rempart_PerfectDestabilises_PartialNoStatus()
        {
            var logs = new List<string>();
            var e1 = MakeSbire("E1");
            var e2 = MakeSbire("E2");
            int hp1 = e1.CurrentHealth;

            DuoTechRegistry.ResolveRempart(
                new List<CharacterStats> { e1, e2 }, DuoTechOutcome.Perfect, logs.Add);
            Assert.AreEqual(hp1 - 4, e1.CurrentHealth);
            Assert.IsTrue(e1.ActiveStatus.HasFlag(StatusEffect.Destabilise));

            var e3 = MakeSbire("E3");
            int hp3 = e3.CurrentHealth;
            DuoTechRegistry.ResolveRempart(
                new List<CharacterStats> { e3 }, DuoTechOutcome.Partial, logs.Add);
            Assert.AreEqual(hp3 - 2, e3.CurrentHealth);
            Assert.IsFalse(e3.ActiveStatus.HasFlag(StatusEffect.Destabilise));

            var e4 = MakeSbire("E4");
            DuoTechRegistry.ResolveRempart(
                new List<CharacterStats> { e4 }, DuoTechOutcome.Miss, logs.Add);
            Assert.AreEqual(e4.MaxHealth, e4.CurrentHealth);
            Assert.IsFalse(e4.ActiveStatus.HasFlag(StatusEffect.Destabilise));
        }

        [Test]
        public void TestDuo_RD086_Zero_PerfectImmobilises_PartialSlows()
        {
            var logs = new List<string>();
            var e1 = MakeSbire("E1");
            int hp1 = e1.CurrentHealth;

            DuoTechRegistry.ResolveZero(
                new List<CharacterStats> { e1 }, DuoTechOutcome.Perfect, logs.Add);
            Assert.AreEqual(hp1 - 4, e1.CurrentHealth);
            Assert.IsTrue(e1.ActiveStatus.HasFlag(StatusEffect.Immobilise));

            var e2 = MakeSbire("E2");
            int hp2 = e2.CurrentHealth;
            DuoTechRegistry.ResolveZero(
                new List<CharacterStats> { e2 }, DuoTechOutcome.Partial, logs.Add);
            Assert.AreEqual(hp2 - 2, e2.CurrentHealth);
            Assert.IsTrue(e2.ActiveStatus.HasFlag(StatusEffect.Ralenti));
            Assert.IsFalse(e2.ActiveStatus.HasFlag(StatusEffect.Immobilise));

            var e3 = MakeSbire("E3");
            DuoTechRegistry.ResolveZero(
                new List<CharacterStats> { e3 }, DuoTechOutcome.Miss, logs.Add);
            Assert.AreEqual(e3.MaxHealth, e3.CurrentHealth);
        }

        [Test]
        public void TestDuo_RD086_TriFusion_PerfectKnocksDownAndBurns_MissPunishesTrio()
        {
            var mina = MakeMina();
            var lucas = MakeLucas();
            var thomas = MakeThomas();
            var logs = new List<string>();

            var tankyAttrs = new Attributes(@for: 2, agi: 2, con: 4, rap: 2, @int: 2, eru: 2, cha: 2, ins: 2);
            var cible = new CharacterStats("Cible", tankyAttrs);
            int hp = cible.CurrentHealth;
            Assert.IsTrue(hp - 8 > 0, "Précondition test : 8 bruts non-létaux.");
            DuoTechRegistry.ResolveTriFusion(mina, lucas, thomas,
                new List<CharacterStats> { cible }, DuoTechOutcome.Perfect, logs.Add);
            Assert.AreEqual(hp - 8, cible.CurrentHealth);
            Assert.IsTrue(cible.ActiveStatus.HasFlag(StatusEffect.ATerre));
            Assert.IsTrue(cible.ActiveStatus.HasFlag(StatusEffect.EnFeu));

            var cible2 = MakeSbire("Cible2");
            int hp2 = cible2.CurrentHealth;
            DuoTechRegistry.ResolveTriFusion(mina, lucas, thomas,
                new List<CharacterStats> { cible2 }, DuoTechOutcome.Partial, logs.Add);
            Assert.AreEqual(hp2 - 5, cible2.CurrentHealth);
            Assert.IsFalse(cible2.ActiveStatus.HasFlag(StatusEffect.ATerre));

            int essM = mina.Essoufflement, essL = lucas.Essoufflement, essT = thomas.Essoufflement;
            DuoTechRegistry.ResolveTriFusion(mina, lucas, thomas,
                new List<CharacterStats> { cible2 }, DuoTechOutcome.Miss, logs.Add);
            Assert.AreEqual(essM + 1, mina.Essoufflement);
            Assert.AreEqual(essL + 1, lucas.Essoufflement);
            Assert.AreEqual(essT + 1, thomas.Essoufflement);
        }
    }
}
#endif
