#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Combat;
using Killtime.Core.Arcanotech;
using Killtime.Core.Chrono;
using Killtime.Tactics.Grid;

namespace Killtime.Tests
{
    public partial class CoreRulesTests
    {
        [Test]
        public void TestStatusConditions_ApplyStrictCodexPenalties_AndIncapacitateDefense()
        {
            var attr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = "Guerrier", BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;

            // RÈGLE CODEX : aucun mod carac ajouté. Base = 0, seuls les états modifient.
            int baseMod = stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true);
            Assert.AreEqual(0, baseMod);

            // 1. Déstabilisé (-2)
            stats.ActiveStatus |= StatusEffect.Destabilise;
            Assert.AreEqual(-2, stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true));

            // 2. Étourdi (-1) => Cumul à -3
            stats.ActiveStatus |= StatusEffect.Etourdi;
            Assert.AreEqual(-3, stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true));

            // 3. À Terre (-1 attaque, -2 défense)
            stats.ActiveStatus |= StatusEffect.ATerre;
            Assert.AreEqual(-4, stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true));
            Assert.AreEqual(-5, stats.GetSkillModifier(SkillType.Esquive, isOffensive: false));

            // 4. Inconscient / K.O. : Parade impossible et interdiction d'attaquer
            stats.ActiveStatus |= StatusEffect.Inconscient;
            Assert.IsFalse(stats.CanDefendActively());
            Assert.IsFalse(stats.CanAttack(DiceType.D6));
        }

        [Test]
        public void TestCover_AttackPenaltiesAndFullBlock_MatchesLivreVI253()
        {
            // Livre V §21.3 : moitié visible -1, 3/4 couvert -2, non visible = impossible.
            var diceRoller = new DiceRoller(99);
            var calc = new CombatCalculator(diceRoller);
            var cfg = Killtime.Core.Rules.CoreRulesConfig.Instance;
            Assert.AreEqual(-1, cfg.HalfCoverAttackPenalty);
            Assert.AreEqual(-2, cfg.ThreeQuartersCoverAttackPenalty);
            Assert.IsTrue(cfg.FullCoverBlocksAttack);

            var attAttr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var defAttr = new Attributes(2, 3, 4, 2, 2, 2, 2, 2);
            var attSheet = new CharacterSheet { Name = "Attaquant", BaseAttributes = attAttr };
            var defSheet = new CharacterSheet { Name = "Défenseur", BaseAttributes = defAttr };
            var attStats = attSheet.ToCombatStats();
            var defStats = defSheet.ToCombatStats();

            var duelNone = calc.BeginSkillDuel(
                attStats, defStats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                5, false, false, null, null, 0, out string errNone, CoverType.None);
            Assert.IsNotNull(duelNone, errNone);
            Assert.AreEqual(0, duelNone.CoverAttackPenalty);

            attStats.ResetTurn(); defStats.ResetTurn();
            var duelHalf = calc.BeginSkillDuel(
                attStats, defStats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                5, false, false, null, null, 0, out string errHalf, CoverType.Half);
            Assert.IsNotNull(duelHalf, errHalf);
            Assert.AreEqual(-1, duelHalf.CoverAttackPenalty);
            Assert.AreEqual(duelNone.AttackBaseMod - 1, duelHalf.AttackBaseMod);

            attStats.ResetTurn(); defStats.ResetTurn();
            var duelTQ = calc.BeginSkillDuel(
                attStats, defStats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                5, false, false, null, null, 0, out string errTQ, CoverType.ThreeQuarters);
            Assert.IsNotNull(duelTQ, errTQ);
            Assert.AreEqual(-2, duelTQ.CoverAttackPenalty);
            Assert.AreEqual(duelNone.AttackBaseMod - 2, duelTQ.AttackBaseMod);

            // Couvert total : Begin refuse (aucun PA dépensé au-delà du contrôle).
            attStats.ResetTurn(); defStats.ResetTurn();
            int paBefore = attStats.CurrentActionPoints;
            var duelFull = calc.BeginSkillDuel(
                attStats, defStats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                5, false, false, null, null, 0, out string errFull, CoverType.Full);
            Assert.IsNull(duelFull);
            StringAssert.Contains("attaque impossible", errFull.ToLower());
            Assert.AreEqual(paBefore, attStats.CurrentActionPoints);

            // API classique : résultat marqué Bloqué-Par-Couvert, log explicite.
            attStats.ResetTurn(); defStats.ResetTurn();
            var blocked = calc.ResolveTargetedAttack(
                attacker: attStats, defender: defStats, targetedPart: BodyPart.Torse,
                attackSkill: SkillType.Ballistique, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5, cancelPenaltyWithAP: false, defenderWantsToDefend: false,
                cover: CoverType.Full);
            Assert.IsTrue(blocked.BlockedByCover);
            Assert.IsFalse(blocked.IsHit);
            StringAssert.Contains("COUVERT", blocked.CombatLog.ToUpper());

            // Malus tracé dans le log d'un tir à moitié couvert.
            attStats.ResetTurn(); defStats.ResetTurn();
            var halfRes = calc.ResolveTargetedAttack(
                attacker: attStats, defender: defStats, targetedPart: BodyPart.Torse,
                attackSkill: SkillType.Ballistique, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5, cancelPenaltyWithAP: false, defenderWantsToDefend: false,
                cover: CoverType.Half);
            Assert.AreEqual(CoverType.Half, halfRes.Cover);
            Assert.AreEqual(-1, halfRes.CoverAttackPenalty);
            StringAssert.Contains("Couvert", halfRes.CombatLog);

            // Au contact, les combattants se voient : le couvert est ignoré.
            calc.SetContactDistanceState(true);
            try
            {
                attStats.ResetTurn(); defStats.ResetTurn();
                var duelContact = calc.BeginSkillDuel(
                    attStats, defStats, BodyPart.Torse,
                    SkillType.Ballistique, SkillType.Esquive,
                    5, false, false, null, null, 0, out string errContact, CoverType.Full);
                Assert.IsNotNull(duelContact, errContact);
                Assert.AreEqual(CoverType.None, duelContact.Cover);
                Assert.AreEqual(0, duelContact.CoverAttackPenalty);
            }
            finally
            {
                calc.SetContactDistanceState(false);
            }
        }

        [Test]
        public void TestStatusConditions_ExpireAfterOneTurn_UnlessPersistent()
        {
            var attr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = "Guerrier", BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;

            // 1. Application d'un statut temporaire (1 tour par défaut) et d'un statut persistant
            stats.ApplyStatus(StatusEffect.Destabilise, 1);
            stats.ApplyStatus(StatusEffect.Inconscient);

            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Destabilise));
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Inconscient));

            // 2. Fin de tour : Décrémentation
            var expired = stats.TickTurnStatusDurations();

            Assert.IsTrue(expired.Contains(StatusEffect.Destabilise));
            Assert.IsFalse(stats.ActiveStatus.HasFlag(StatusEffect.Destabilise));
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Inconscient));
        }
    }
}
#endif