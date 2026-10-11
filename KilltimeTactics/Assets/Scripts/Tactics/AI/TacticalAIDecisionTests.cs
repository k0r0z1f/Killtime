#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Combat;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.AI.Tests
{
    /// <summary>
    /// Non-régression du refactor phase 1 : les formules pures extraites du
    /// god-class TacticalAIController doivent reproduire exactement les
    /// constantes et priorités d'origine (posture, réserve, scoring, hexes).
    /// Pur EditMode : aucune scène, aucune grille, aucun singleton.
    /// </summary>
    [TestFixture]
    public class TacticalAIDecisionTests
    {
        // ---- Posture ----

        [Test]
        public void Posture_MortalDangerWithoutKill_Retreats()
        {
            var p = TacticalPostureEvaluator.Evaluate(
                hpRatio: 0.10f, retreatHealthRatio: 0.28f, canKillTargetThisTurn: false,
                hasWoundedAllyAdjacent: false, currentAP: 6,
                personality: AIPersonality.Balanced, dist: 5, targetCover: CoverType.None);
            Assert.AreEqual(TacticalPosture.TacticalRetreat, p);
        }

        [Test]
        public void Posture_MortalDangerWithKill_GoesAllIn()
        {
            var p = TacticalPostureEvaluator.Evaluate(
                hpRatio: 0.10f, retreatHealthRatio: 0.28f, canKillTargetThisTurn: true,
                hasWoundedAllyAdjacent: false, currentAP: 6,
                personality: AIPersonality.Balanced, dist: 5, targetCover: CoverType.None);
            Assert.AreEqual(TacticalPosture.AllInLethal, p);
        }

        [Test]
        public void Posture_WoundedAllyAdjacent_SupportsWhenNotAggressive()
        {
            var p = TacticalPostureEvaluator.Evaluate(
                hpRatio: 0.9f, retreatHealthRatio: 0.28f, canKillTargetThisTurn: false,
                hasWoundedAllyAdjacent: true, currentAP: 4,
                personality: AIPersonality.Balanced, dist: 5, targetCover: CoverType.None);
            Assert.AreEqual(TacticalPosture.SupportAndHeal, p);
        }

        [Test]
        public void Posture_WoundedAllyAdjacent_AggressiveIgnoresSupport()
        {
            var p = TacticalPostureEvaluator.Evaluate(
                hpRatio: 0.9f, retreatHealthRatio: 0.28f, canKillTargetThisTurn: false,
                hasWoundedAllyAdjacent: true, currentAP: 4,
                personality: AIPersonality.Aggressive, dist: 5, targetCover: CoverType.None);
            Assert.AreEqual(TacticalPosture.CalculatedStrike, p);
        }

        [Test]
        public void Posture_TacticianAtContact_HitAndRun()
        {
            var p = TacticalPostureEvaluator.Evaluate(
                hpRatio: 0.9f, retreatHealthRatio: 0.28f, canKillTargetThisTurn: false,
                hasWoundedAllyAdjacent: false, currentAP: 4,
                personality: AIPersonality.Tactician, dist: 1, targetCover: CoverType.None);
            Assert.AreEqual(TacticalPosture.HitAndRun, p);
        }

        [Test]
        public void Posture_CoveredTarget_Flanks()
        {
            foreach (var cover in new[] { CoverType.ThreeQuarters, CoverType.Full })
            {
                var p = TacticalPostureEvaluator.Evaluate(
                    hpRatio: 0.9f, retreatHealthRatio: 0.28f, canKillTargetThisTurn: false,
                    hasWoundedAllyAdjacent: false, currentAP: 6,
                    personality: AIPersonality.Balanced, dist: 5, targetCover: cover);
                Assert.AreEqual(TacticalPosture.FlankAndSuppress, p);
            }
        }

        [Test]
        public void Posture_Default_IsCalculatedStrike()
        {
            var p = TacticalPostureEvaluator.Evaluate(
                hpRatio: 0.9f, retreatHealthRatio: 0.28f, canKillTargetThisTurn: false,
                hasWoundedAllyAdjacent: false, currentAP: 6,
                personality: AIPersonality.Balanced, dist: 5, targetCover: CoverType.None);
            Assert.AreEqual(TacticalPosture.CalculatedStrike, p);
        }

        // ---- Réserve défensive (RD-IA03) ----

        [Test]
        public void Reserve_AllInLethal_IsZero() =>
            Assert.AreEqual(0, AIPersonalityProfile.GetDefensiveReserve(
                AIPersonality.Balanced, TacticalPosture.AllInLethal, 2));

        [Test]
        public void Reserve_Aggressive_IsZero() =>
            Assert.AreEqual(0, AIPersonalityProfile.GetDefensiveReserve(
                AIPersonality.Aggressive, TacticalPosture.CalculatedStrike, 2));

        [Test]
        public void Reserve_AlertedPatrol_IsOne() =>
            Assert.AreEqual(1, AIPersonalityProfile.GetDefensiveReserve(
                AIPersonality.Balanced, TacticalPosture.AlertedPatrol, 2));

        [Test]
        public void Reserve_RespectsSettingClamped02()
        {
            Assert.AreEqual(2, AIPersonalityProfile.GetDefensiveReserve(
                AIPersonality.Balanced, TacticalPosture.CalculatedStrike, 2));
            Assert.AreEqual(0, AIPersonalityProfile.GetDefensiveReserve(
                AIPersonality.Balanced, TacticalPosture.CalculatedStrike, 0));
            Assert.AreEqual(2, AIPersonalityProfile.GetDefensiveReserve(
                AIPersonality.Balanced, TacticalPosture.CalculatedStrike, 99));
        }

        // ---- Scoring cible ----

        private static TargetScoreInput BaseRangedInput() => new TargetScoreInput
        {
            IsRanged = true, Dist = 4, MaxRange = 10, HasLOS = true,
            TargetCover = CoverType.None, CurHp = 20, MaxHp = 20,
            Encaissement = 8, IsSquadMarked = false,
            NightLampBonus = false, NoiseBonus = false,
            HasDestabilise = false, HasATerre = false,
            FlawExposed = false, TargetCurrentAP = 2,
        };

        [Test]
        public void Scorer_InRangeLOS_BeatsOutOfRange()
        {
            var near = BaseRangedInput();
            var far = BaseRangedInput();
            far.Dist = 14; // hors portée : pénalité (dist-max)*6
            Assert.Greater(TargetScorer.Score(in near), TargetScorer.Score(in far));
        }

        [Test]
        public void Scorer_CoverPenalties_Half10_ThreeQuarters25()
        {
            var open = BaseRangedInput();
            var half = BaseRangedInput(); half.TargetCover = CoverType.Half;
            var three = BaseRangedInput(); three.TargetCover = CoverType.ThreeQuarters;
            Assert.AreEqual(10f, TargetScorer.Score(in open) - TargetScorer.Score(in half), 1e-4f);
            Assert.AreEqual(25f, TargetScorer.Score(in open) - TargetScorer.Score(in three), 1e-4f);
        }

        [Test]
        public void Scorer_LethalBonuses_Stack()
        {
            var healthy = BaseRangedInput();
            var dying = BaseRangedInput(); dying.CurHp = 5; // <=6 et <= encaissement
            // +40*(1-5/20)=+30 létalité, +30 encaissement, +50 mourant
            Assert.AreEqual(110f, TargetScorer.Score(in dying) - TargetScorer.Score(in healthy), 1e-4f);
        }

        [Test]
        public void Scorer_SquadMark_Adds45()
        {
            var a = BaseRangedInput();
            var b = BaseRangedInput(); b.IsSquadMarked = true;
            Assert.AreEqual(45f, TargetScorer.Score(in b) - TargetScorer.Score(in a), 1e-4f);
        }

        [Test]
        public void Scorer_MeleeContact_Bonus()
        {
            var contact = new TargetScoreInput
            {
                IsRanged = false, Dist = 1, CurHp = 20, MaxHp = 20,
                Encaissement = 8, TargetCurrentAP = 2,
            };
            var far = contact; far.Dist = 3;
            // contact : 80 - 8 + 45 = 117 ; loin : 80 - 24 = 56
            Assert.AreEqual(117f, TargetScorer.Score(in contact), 1e-4f);
            Assert.AreEqual(56f, TargetScorer.Score(in far), 1e-4f);
        }

        // ---- Scoring hexes ----

        [Test]
        public void Retreat_FullCover_BeatsHalf()
        {
            float full = AIMovementSolver.ScoreRetreatHex(3, CoverType.Full);
            float half = AIMovementSolver.ScoreRetreatHex(3, CoverType.Half);
            Assert.AreEqual(40f, full - half, 1e-4f); // 65 - 25
        }

        [Test]
        public void CombatHex_OpenFlank_BeatsCovered()
        {
            float open = AIMovementSolver.ScoreCombatHex(
                CoverType.None, CoverType.None, true, 4, 0f, 0f, AIPersonality.Balanced);
            float covered = AIMovementSolver.ScoreCombatHex(
                CoverType.ThreeQuarters, CoverType.None, true, 4, 0f, 0f, AIPersonality.Balanced);
            Assert.AreEqual(45f, open - covered, 1e-4f);
        }

        [Test]
        public void CombatHex_RangePrefilter_BeforeCoverScan()
        {
            // IsRangeEligible seul : aucun scan de couvert requis pour ces cas.
            Assert.IsFalse(AIMovementSolver.IsRangeEligible(true, 1, 10)); // contact exclu au tir
            Assert.IsFalse(AIMovementSolver.IsRangeEligible(true, 11, 10)); // hors portée
            Assert.IsFalse(AIMovementSolver.IsRangeEligible(false, 2, 10)); // mêlée ≠ contact
            Assert.IsTrue(AIMovementSolver.IsRangeEligible(true, 4, 10));
            Assert.IsTrue(AIMovementSolver.IsRangeEligible(false, 1, 10));
        }

        [Test]
        public void CombatHex_Eligibility_MatchesOriginalContinues()
        {
            Assert.IsFalse(AIMovementSolver.IsCombatHexEligible(true, 1, 10, CoverType.None)); // contact exclu au tir
            Assert.IsFalse(AIMovementSolver.IsCombatHexEligible(true, 11, 10, CoverType.None)); // hors portée
            Assert.IsFalse(AIMovementSolver.IsCombatHexEligible(true, 4, 10, CoverType.Full)); // mur
            Assert.IsFalse(AIMovementSolver.IsCombatHexEligible(false, 2, 10, CoverType.None)); // mêlée ≠ contact
            Assert.IsTrue(AIMovementSolver.IsCombatHexEligible(true, 4, 10, CoverType.Half));
            Assert.IsTrue(AIMovementSolver.IsCombatHexEligible(false, 1, 10, CoverType.None));
        }

        [Test]
        public void Crossfire_PerfectL_Is35_Outside_IsZero()
        {
            var toCandidate = new Vector3(1, 0, 0);
            var allies = new List<Vector3> { new Vector3(0, 0, 1) }; // 90°
            Assert.AreEqual(35f, AIMovementSolver.ScoreCrossfire(toCandidate, allies), 1e-3f);

            var aligned = new List<Vector3> { new Vector3(1, 0, 0) }; // 0° : pas de L
            Assert.AreEqual(0f, AIMovementSolver.ScoreCrossfire(toCandidate, aligned), 1e-4f);
        }

        // ---- Posture d'opportunité RD-031 ----

        [Test]
        public void Stance_Athletisme_Blocks() =>
            Assert.AreEqual(OpportunityReactionType.Block,
                AIPersonalityProfile.ChooseOpportunityStance(true, 3, false));

        [Test]
        public void Stance_Rapide_Follows() =>
            Assert.AreEqual(OpportunityReactionType.Follow,
                AIPersonalityProfile.ChooseOpportunityStance(false, 5, false));

        [Test]
        public void Stance_Esquiveur_Trips() =>
            Assert.AreEqual(OpportunityReactionType.Trip,
                AIPersonalityProfile.ChooseOpportunityStance(false, 3, true));

        [Test]
        public void Stance_Default_Strikes() =>
            Assert.AreEqual(OpportunityReactionType.Strike,
                AIPersonalityProfile.ChooseOpportunityStance(false, 3, false));

        // ---- Plan d'attaque ----

        // Difficultés Codex par défaut : Torse 0, Tête -2, Jambes -1, BrasDroit -1.
        private static int DefaultAim(BodyPart part) => part switch
        {
            BodyPart.Tete => -2,
            BodyPart.Jambes => -1,
            BodyPart.BrasDroit => -1,
            _ => 0,
        };

        private static AttackPlanInput BasePlanInput() => new AttackPlanInput
        {
            BaseAttExpected = 10, BaseDefExpected = 5,
            WeaponDmg = 8, TargetArmor = 0, TargetEncaissement = 10, TargetHp = 12,
            AvailableAP = 6, ReserveNeeded = 1, SpendablePE = 0, PeValue = 1,
            MaxAttacks = 1, AttacksThisTurn = 0,
            IsRanged = true, Posture = TacticalPosture.CalculatedStrike,
            TargetRapidite = 3, TargetHasATerre = false, TargetCurrentAP = 2,
        };

        [Test]
        public void AttackPlan_LethalObvious_AimsHeadCompensated()
        {
            // Tête compensée : net 13 létal, utilité 382 > Torse 306.
            var input = BasePlanInput();
            var plan = AttackPlanEvaluator.Evaluate(in input, DefaultAim);
            Assert.AreEqual(BodyPart.Tete, plan.TargetedPart);
            Assert.IsTrue(plan.CancelPenalty);
            Assert.AreEqual(0, plan.BonusAP);
            Assert.AreEqual(0, plan.BonusPE);
            Assert.IsTrue(plan.IsLethal);
            Assert.IsTrue(plan.ExceedsEncaissement);
            Assert.AreEqual(13.0, plan.ExpectedNetDamage, 1e-6);
        }

        [Test]
        public void AttackPlan_NoAP_NoValidPlan()
        {
            var input = BasePlanInput();
            input.AvailableAP = 0;
            var plan = AttackPlanEvaluator.Evaluate(in input, DefaultAim);
            Assert.AreEqual(float.MinValue, plan.UtilityScore);
            Assert.IsFalse(plan.IsLethal);
        }

        [Test]
        public void AttackPlan_PEInjection_DecidesLethal()
        {
            // Sans PE : net max 8 < 9 PV. Avec 1 PE : net 9 létal.
            // Jambes compensées gagnent (utilité 233 : +45 clouage + 150 létal).
            var input = BasePlanInput();
            input.BaseAttExpected = 6;
            input.WeaponDmg = 4;
            input.TargetEncaissement = 20;
            input.TargetHp = 9;
            input.ReserveNeeded = 0;
            input.SpendablePE = 2;
            input.Posture = TacticalPosture.AllInLethal;
            input.TargetRapidite = 6;
            input.TargetCurrentAP = 4;
            var plan = AttackPlanEvaluator.Evaluate(in input, DefaultAim);
            Assert.AreEqual(BodyPart.Jambes, plan.TargetedPart);
            Assert.IsTrue(plan.CancelPenalty);
            Assert.AreEqual(3, plan.BonusAP);
            Assert.AreEqual(1, plan.BonusPE);
            Assert.IsTrue(plan.IsLethal);
            Assert.AreEqual(9.0, plan.ExpectedNetDamage, 1e-6);
        }

        [Test]
        public void AttackPlan_Melee_NeverCompensates()
        {
            var input = BasePlanInput();
            input.IsRanged = false;
            var plan = AttackPlanEvaluator.Evaluate(in input, DefaultAim);
            Assert.IsFalse(plan.CancelPenalty);
        }
    }
}
#endif
