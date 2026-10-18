#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.AI.Tests
{
    /// <summary>
    /// RD-115 (lot AITurnPlanner) : non-régression de l'ordre des priorités
    /// d'ExecuteAITurn, désormais exprimé en <see cref="AIIntent"/> purs.
    /// Chaque test fige un segment de la chaîne if/continue verbatim :
    /// reddition &gt; déroute &gt; soutiens &gt; cible nulle &gt; retraite &gt;
    /// ralliement &gt; registre joueur &gt; contact &gt; frappe &gt; approche &gt;
    /// ordre &gt; guet. Pur EditMode : aucun MonoBehaviour, aucune grille.
    /// </summary>
    [TestFixture]
    public class AITurnPlannerTests
    {
        private static AITurnSnapshot BaseCombatSnapshot() => new AITurnSnapshot
        {
            Posture = TacticalPosture.CalculatedStrike,
            IsRanged = true,
            Dist = 5,
            TargetCover = CoverType.None,
            InAttackRange = true,
            ActionPoints = 6,
            DefensiveReserve = 1,
            IsWatching = false,
            ArenaAvailable = true,
            OverwatchAPCost = 2,
            HasMartialArts = false,
            CanUnarmedAttack = false,
        };

        private static List<AIIntent> AsList(IReadOnlyList<AIIntent> plan) => plan.ToList();

        // ---- Phases 1-4 : pré-cible ----

        [Test]
        public void PreTarget_Surrendered_IsImmediateEndTurn()
        {
            var plan = AsList(AITurnPlanner.BuildPreTargetPlan(true, false));
            Assert.AreEqual(new[] { AIIntent.EndTurn_Surrender }, plan);
        }

        [Test]
        public void PreTarget_Agonisant_IsPanickedFlee()
        {
            var plan = AsList(AITurnPlanner.BuildPreTargetPlan(false, true));
            Assert.AreEqual(new[] { AIIntent.PanickedFlee }, plan);
        }

        [Test]
        public void PreTarget_SurrenderBeatsRout_VerbatimOrder()
        {
            // Verbatim : la reddition est testée avant la déroute.
            var plan = AsList(AITurnPlanner.BuildPreTargetPlan(true, true));
            Assert.AreEqual(new[] { AIIntent.EndTurn_Surrender }, plan);
        }

        [Test]
        public void PreTarget_Healthy_IsMedicalThenAlly()
        {
            var plan = AsList(AITurnPlanner.BuildPreTargetPlan(false, false));
            Assert.AreEqual(new[] { AIIntent.MedicalSupport, AIIntent.AllySupport }, plan);
        }

        // ---- Phase 5 : cible nulle ----

        [Test]
        public void NoTarget_IsObservationThenPatrolThenBreak()
        {
            var plan = AsList(AITurnPlanner.BuildNoTargetPlan());
            Assert.AreEqual(new[]
            {
                AIIntent.ActiveObservation,
                AIIntent.AlertedPatrol,
                AIIntent.EndTurn_NoContact,
            }, plan);
        }

        // ---- Phases 6-11 : engagement ----

        [Test]
        public void Engagement_RetreatPosture_IsSoleTerminal()
        {
            var snap = BaseCombatSnapshot();
            snap.Posture = TacticalPosture.TacticalRetreat;
            var plan = AsList(AITurnPlanner.BuildEngagementPlan(in snap));
            Assert.AreEqual(new[] { AIIntent.TacticalRetreat }, plan);
        }

        [Test]
        public void Engagement_SupportPosture_RalliesFirst()
        {
            var snap = BaseCombatSnapshot();
            snap.Posture = TacticalPosture.SupportAndHeal;
            var plan = AsList(AITurnPlanner.BuildEngagementPlan(in snap));
            Assert.IsTrue(plan.Count > 1);
            Assert.AreEqual(AIIntent.RallyWoundedAlly, plan[0]);
            Assert.AreEqual(AIIntent.CombatTechnique, plan[1]);
        }

        [Test]
        public void Engagement_DefaultOrder_IsTechniqueThenLivreXIThenDuo()
        {
            var plan = AsList(AITurnPlanner.BuildEngagementPlan(BaseCombatSnapshot()));
            Assert.AreEqual(new[]
            {
                AIIntent.CombatTechnique,
                AIIntent.LivreXIManeuver,
                AIIntent.DuoTechAuto,
            }, plan);
        }

        [Test]
        public void Engagement_RangedContactMartial_IsTeepBreak()
        {
            var snap = BaseCombatSnapshot();
            snap.Dist = 1;
            snap.HasMartialArts = true;
            snap.CanUnarmedAttack = true;
            var plan = AsList(AITurnPlanner.BuildEngagementPlan(in snap));
            Assert.AreEqual(AIIntent.TeepBreak, plan[plan.Count - 1]);
        }

        [Test]
        public void Engagement_RangedContactNoMartial_IsDisengage()
        {
            var snap = BaseCombatSnapshot();
            snap.Dist = 1;
            snap.ActionPoints = 3;
            var plan = AsList(AITurnPlanner.BuildEngagementPlan(in snap));
            Assert.AreEqual(AIIntent.DisengageStep, plan[plan.Count - 1]);
        }

        [Test]
        public void Engagement_RangedContactBroke_IsNeither()
        {
            // Ni arts martiaux ni PA ≥ 3 : l'original tombe à travers.
            var snap = BaseCombatSnapshot();
            snap.Dist = 1;
            snap.ActionPoints = 2;
            var plan = AsList(AITurnPlanner.BuildEngagementPlan(in snap));
            CollectionAssert.DoesNotContain(plan, AIIntent.TeepBreak);
            CollectionAssert.DoesNotContain(plan, AIIntent.DisengageStep);
        }

        [Test]
        public void Engagement_MeleeContact_HasNoTeepNorDisengage()
        {
            var snap = BaseCombatSnapshot();
            snap.IsRanged = false;
            snap.Dist = 1;
            var plan = AsList(AITurnPlanner.BuildEngagementPlan(in snap));
            CollectionAssert.DoesNotContain(plan, AIIntent.TeepBreak);
            CollectionAssert.DoesNotContain(plan, AIIntent.DisengageStep);
        }

        // ---- Phases 12-25 : frappe ----

        [Test]
        public void Strike_DefaultRangedOrder_ToWeaponThrow()
        {
            // dist 5 sans couvert à portée : pas de charge/shove/flank/attaque-tail.
            var snap = BaseCombatSnapshot();
            snap.InAttackRange = false;
            var plan = AsList(AITurnPlanner.BuildStrikePlan(in snap));
            int throwIdx = plan.IndexOf(AIIntent.WeaponThrow);
            Assert.IsTrue(throwIdx >= 0);
            Assert.AreEqual(new[]
            {
                AIIntent.Grenade, AIIntent.BestSpell, AIIntent.Intimidation,
                AIIntent.SelfUpkeep, AIIntent.LaserDesignation, AIIntent.TitanSlam,
                AIIntent.WeaponThrow,
            }, plan.Take(throwIdx + 1).ToArray());
            CollectionAssert.DoesNotContain(plan, AIIntent.ChargeAttack);
            CollectionAssert.DoesNotContain(plan, AIIntent.Shove);
            // Hors portée à couvert nul : l'original flanque quand même.
            Assert.AreEqual(AIIntent.FlankToCover, plan[throwIdx + 1]);
        }

        [Test]
        public void Strike_MeleeFar_HasChargeThrowApproachOrderEnd()
        {
            var snap = BaseCombatSnapshot();
            snap.IsRanged = false;
            snap.Dist = 3;
            snap.InAttackRange = false;
            var plan = AsList(AITurnPlanner.BuildStrikePlan(in snap));
            Assert.AreEqual(new[]
            {
                AIIntent.Grenade, AIIntent.BestSpell, AIIntent.Intimidation,
                AIIntent.SelfUpkeep, AIIntent.LaserDesignation, AIIntent.ChargeAttack,
                AIIntent.TitanSlam, AIIntent.WeaponThrow, AIIntent.FlankToCover,
                AIIntent.AdvanceToContact, AIIntent.TacticalOrder, AIIntent.EndTurn,
            }, plan);
        }

        [Test]
        public void Strike_ContactMelee_HasShoveNoThrowNoCharge()
        {
            var snap = BaseCombatSnapshot();
            snap.IsRanged = false;
            snap.Dist = 1;
            snap.InAttackRange = true;
            var plan = AsList(AITurnPlanner.BuildStrikePlan(in snap));
            CollectionAssert.Contains(plan, AIIntent.Shove);
            CollectionAssert.DoesNotContain(plan, AIIntent.WeaponThrow);
            CollectionAssert.DoesNotContain(plan, AIIntent.ChargeAttack);
            Assert.AreEqual(AIIntent.EndTurn_NoAttackOption, plan[plan.Count - 1]);
        }

        [Test]
        public void Strike_InRange_TrioTerminatesPlan()
        {
            var snap = BaseCombatSnapshot();
            snap.InAttackRange = true;
            var plan = AsList(AITurnPlanner.BuildStrikePlan(in snap));
            Assert.AreEqual(new[]
            {
                AIIntent.Grenade, AIIntent.BestSpell, AIIntent.Intimidation,
                AIIntent.SelfUpkeep, AIIntent.LaserDesignation, AIIntent.TitanSlam,
                AIIntent.WeaponThrow, AIIntent.PredictiveAttack,
                AIIntent.EmergencyBreath, AIIntent.EndTurn_NoAttackOption,
            }, plan);
        }

        [Test]
        public void Strike_FlankOnThreeQuarters_BeforeApproach()
        {
            var snap = BaseCombatSnapshot();
            snap.InAttackRange = false;
            snap.TargetCover = CoverType.ThreeQuarters;
            var plan = AsList(AITurnPlanner.BuildStrikePlan(in snap));
            int flank = plan.IndexOf(AIIntent.FlankToCover);
            int advance = plan.IndexOf(AIIntent.AdvanceToContact);
            Assert.IsTrue(flank >= 0 && advance > flank);
        }

        [Test]
        public void Strike_HalfCoverInRange_NoFlank()
        {
            // Verbatim : seul le couvert 3/4 déclenche le flanking à portée.
            var snap = BaseCombatSnapshot();
            snap.InAttackRange = true;
            snap.TargetCover = CoverType.Half;
            var plan = AsList(AITurnPlanner.BuildStrikePlan(in snap));
            CollectionAssert.DoesNotContain(plan, AIIntent.FlankToCover);
        }

        [Test]
        public void Strike_OverwatchSurplus_BeforeFinalEnd()
        {
            var snap = BaseCombatSnapshot();
            snap.InAttackRange = false;
            snap.ActionPoints = 6;
            var plan = AsList(AITurnPlanner.BuildStrikePlan(in snap));
            Assert.AreEqual(AIIntent.Overwatch, plan[plan.Count - 2]);
            Assert.AreEqual(AIIntent.EndTurn, plan[plan.Count - 1]);
        }

        [Test]
        public void Strike_Watching_NoOverwatch()
        {
            var snap = BaseCombatSnapshot();
            snap.InAttackRange = false;
            snap.IsWatching = true;
            var plan = AsList(AITurnPlanner.BuildStrikePlan(in snap));
            CollectionAssert.DoesNotContain(plan, AIIntent.Overwatch);
            Assert.AreEqual(AIIntent.EndTurn, plan[plan.Count - 1]);
        }

        [Test]
        public void Strike_Melee_NoOverwatch()
        {
            var snap = BaseCombatSnapshot();
            snap.IsRanged = false;
            snap.InAttackRange = false;
            snap.Dist = 3;
            var plan = AsList(AITurnPlanner.BuildStrikePlan(in snap));
            CollectionAssert.DoesNotContain(plan, AIIntent.Overwatch);
        }
    }
}
#endif
