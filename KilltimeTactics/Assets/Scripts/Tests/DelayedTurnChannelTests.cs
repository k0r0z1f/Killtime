#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-048 Retarder / Tenir / Canalisation (Livre VI — Tours) :
    /// - Tenir (0 PA) : passe en attente, PA conservés, interruption ultérieure.
    /// - Retarder (0 PA) : initiative -2, réinsertion plus tard, 1/round.
    /// - Canalisation (1 PA) : +2 au jet à l'attaque, -1 en défense,
    ///   interrompue si dégât net &gt; encaissement.
    /// </summary>
    [TestFixture]
    public class DelayedTurnChannelTests
    {
        private static CharacterStats MakeStats(string name, int con = 3, int ap = 6)
        {
            var attr = new Attributes(4, 4, con, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = name, BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            stats.CurrentActionPoints = ap;
            return stats;
        }

        [SetUp]
        public void SetUp()
        {
            DelayedTurnState.ClearAll();
            ChannelingState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            DelayedTurnState.ClearAll();
            ChannelingState.ClearAll();
        }

        // ================= TENIR =================

        [Test]
        public void Hold_MarksHolding_KeepsPA()
        {
            var unit = MakeStats("Attente", ap: 5);
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsTrue(calc.TryHoldAction(unit, out string error), error);
            Assert.IsTrue(DelayedTurnState.IsHolding(unit));
            Assert.AreEqual(5, unit.CurrentActionPoints);
        }

        [Test]
        public void Hold_RefusedWhenAlreadyHolding()
        {
            var unit = MakeStats("Attente");
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsTrue(calc.TryHoldAction(unit, out _));
            Assert.IsFalse(calc.TryHoldAction(unit, out string error));
            StringAssert.Contains("déjà", error);
        }

        [Test]
        public void Hold_CancelledWhenStunnedAfterEntry()
        {
            var unit = MakeStats("Attente");
            var calc = new CombatCalculator(new DiceRoller(7));
            Assert.IsTrue(calc.TryHoldAction(unit, out _));

            unit.ApplyStatus(StatusEffect.Etourdi, 1);
            Assert.IsFalse(DelayedTurnState.IsHolding(unit));
        }

        [Test]
        public void Hold_ExpiresOnOwnerTurnStart()
        {
            var unit = MakeStats("Attente");
            var calc = new CombatCalculator(new DiceRoller(7));
            Assert.IsTrue(calc.TryHoldAction(unit, out _));

            DelayedTurnState.OnUnitTurnStart(unit);
            Assert.IsFalse(DelayedTurnState.IsHolding(unit));
        }

        [Test]
        public void Hold_ConsumeToAct_ReturnsTrueOnce()
        {
            var unit = MakeStats("Attente");
            var calc = new CombatCalculator(new DiceRoller(7));
            Assert.IsTrue(calc.TryHoldAction(unit, out _));

            Assert.IsTrue(DelayedTurnState.ConsumeHoldToAct(unit));
            Assert.IsFalse(DelayedTurnState.IsHolding(unit));
            Assert.IsFalse(DelayedTurnState.ConsumeHoldToAct(unit));
        }

        // ================= RETARDER =================

        [Test]
        public void Delay_ReducesInitiativeByPenalty_MarksDelayed()
        {
            var unit = MakeStats("Retard");
            unit.InitiativeRollTotal = 10;
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsTrue(calc.TryDelayInitiative(unit, out int newTotal, out string error), error);
            Assert.AreEqual(8, newTotal);
            Assert.AreEqual(8, unit.InitiativeRollTotal);
            Assert.IsTrue(DelayedTurnState.HasDelayed(unit));
        }

        [Test]
        public void Delay_RefusedTwiceSameRound_ReArmedOnTurnStart()
        {
            var unit = MakeStats("Retard");
            unit.InitiativeRollTotal = 10;
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsTrue(calc.TryDelayInitiative(unit, out _, out _));
            Assert.IsFalse(calc.TryDelayInitiative(unit, out _, out string error));
            StringAssert.Contains("déjà", error);

            DelayedTurnState.OnUnitTurnStart(unit);
            Assert.IsFalse(DelayedTurnState.HasDelayed(unit));
            Assert.IsTrue(calc.TryDelayInitiative(unit, out int second, out _));
            Assert.AreEqual(6, second);
        }

        [Test]
        public void Delay_RefusedWhenStunned()
        {
            var unit = MakeStats("Retard");
            unit.InitiativeRollTotal = 10;
            unit.ApplyStatus(StatusEffect.Paralyse, 1);
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsFalse(calc.TryDelayInitiative(unit, out _, out _));
            Assert.AreEqual(10, unit.InitiativeRollTotal);
        }

        // ================= CANALISATION =================

        [Test]
        public void Channel_BeginCostsPA_AppliesDefensePenalty()
        {
            var caster = MakeStats("Lanceur", ap: 6);
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsTrue(calc.TryBeginChanneling(caster, "Coup en progression", out string error), error);
            Assert.IsTrue(ChannelingState.IsChanneling(caster));
            Assert.AreEqual(5, caster.CurrentActionPoints);
            Assert.AreEqual(-1, ChannelingState.GetDefenseModifier(caster));
        }

        [Test]
        public void Channel_RefusedWhenAlreadyChanneling()
        {
            var caster = MakeStats("Lanceur");
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsTrue(calc.TryBeginChanneling(caster, "Premier", out _));
            Assert.IsFalse(calc.TryBeginChanneling(caster, "Second", out string error));
            StringAssert.Contains("déjà", error);
        }

        [Test]
        public void Channel_SurvivesSmallDamage_InterruptedWhenExceedsEncaissement()
        {
            var caster = MakeStats("Lanceur", con: 3); // encaissement 6
            var calc = new CombatCalculator(new DiceRoller(7));
            Assert.IsTrue(calc.TryBeginChanneling(caster, "Sort", out _));

            // Dégât ≤ encaissement : la canalisation survit.
            Assert.IsFalse(calc.CheckChannelInterruption(caster, caster.EncaissementThreshold, out _));
            Assert.IsTrue(ChannelingState.IsChanneling(caster));

            // Dégât > encaissement : interrompue.
            Assert.IsTrue(calc.CheckChannelInterruption(caster, caster.EncaissementThreshold + 1, out string label));
            Assert.AreEqual("Sort", label);
            Assert.IsFalse(ChannelingState.IsChanneling(caster));
        }

        [Test]
        public void Channel_BonusConsumedOnAttack_Logged()
        {
            var attacker = MakeStats("Assaillant");
            var defender = MakeStats("Cible");
            var calc = new CombatCalculator(new QueueDiceRoller(8, 3));
            Assert.IsTrue(calc.TryBeginChanneling(attacker, "Frappe préparée", out _));

            var duel = calc.BeginSkillDuel(
                attacker, defender, BodyPart.Torse,
                SkillType.MainsNues, SkillType.Esquive,
                5, false, true, null, null, 0, out string error);
            Assert.IsNotNull(duel, error);
            Assert.IsTrue(duel.IsChannelAttack);
            Assert.AreEqual(2, duel.ChannelAttackBonus);

            calc.DeclareAttackerStakes(duel, 0, 0);
            calc.AutoDeclareDefenderStakes(duel);
            var result = calc.ResolveBlindDuel(duel);

            Assert.IsFalse(ChannelingState.IsChanneling(attacker));
            StringAssert.Contains("Canalisation", result.CombatLog);
        }

        [Test]
        public void Channel_InterruptedWhenHitExceedsEncaissement_Logged()
        {
            // Défenseur fragile (CON 2 → encaissement 4) en canalisation, frappé fort.
            var attacker = MakeStats("Assaillant");
            var defender = MakeStats("Fragile", con: 2);
            var calc = new CombatCalculator(new QueueDiceRoller(9, 2));
            Assert.IsTrue(calc.TryBeginChanneling(defender, "Incantation", out _));

            var result = calc.ResolveTargetedAttack(
                attacker: attacker, defender: defender,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.MainsNues, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5,
                cancelPenaltyWithAP: false, defenderWantsToDefend: false);

            Assert.IsTrue(result.IsHit);
            Assert.Greater(result.FinalDamageApplied, defender.EncaissementThreshold);
            Assert.IsTrue(result.ChannelInterrupted);
            Assert.AreEqual("Incantation", result.InterruptedChannelLabel);
            Assert.IsFalse(ChannelingState.IsChanneling(defender));
            StringAssert.Contains("CANALISATION INTERROMPUE", result.CombatLog);
        }

        [Test]
        public void Channel_SurvivesWhenHitBelowEncaissement()
        {
            // Défenseur blindé (CON 8 → encaissement 16) : petit coup sans choc.
            var attacker = MakeStats("Assaillant");
            var defender = MakeStats("Blindé", con: 8);
            var calc = new CombatCalculator(new QueueDiceRoller(2, 0));
            Assert.IsTrue(calc.TryBeginChanneling(defender, "Mur", out _));

            var result = calc.ResolveTargetedAttack(
                attacker: attacker, defender: defender,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.MainsNues, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 1,
                cancelPenaltyWithAP: false, defenderWantsToDefend: false);

            Assert.IsFalse(result.ChannelInterrupted);
            Assert.IsTrue(ChannelingState.IsChanneling(defender));
        }

        // ================= TURNMANAGER =================

        private GameObject _holder;
        private TurnManager _turnManager;
        private readonly List<GameObject> _spawned = new();

        private TurnManager CreateCombatManager()
        {
            _holder = new GameObject("Test_TurnManager_RD048");
            _turnManager = _holder.AddComponent<TurnManager>();
            _turnManager.SuspendAutoStart = true;
            return _turnManager;
        }

        private TacticalUnit SpawnUnit(string name, bool isPlayer, int q, int r)
        {
            var go = new GameObject($"Unit_{name}");
            _spawned.Add(go);
            var unit = go.AddComponent<TacticalUnit>();
            unit.ConfigureStats(name, new Attributes(3, 3, 3, 3, 2, 2, 1, 2, 0), baseArmor: 0, isPlayer: isPlayer);
            unit.SetPositionDirect(new HexCoordinates(q, r));
            _turnManager.RegisterUnit(unit);
            return unit;
        }

        [Test]
        public void TurnManager_DelayActiveUnit_ReinsertsLaterWithMinusTwo()
        {
            CreateCombatManager();
            try
            {
                var a = SpawnUnit("Alpha", true, 0, 0);
                var b = SpawnUnit("Bravo", true, 1, 0);
                var e = SpawnUnit("Gredin", false, 5, 5);
                _turnManager.SuspendAutoStart = false;
                _turnManager.SetDiceSeed(42);
                _turnManager.EnterCombatMode();

                var active = _turnManager.ActiveUnit;
                Assert.IsNotNull(active);
                int before = _turnManager.GetInitiativeTotal(active);
                Assert.Greater(before, int.MinValue);
                int countBefore = _turnManager.TurnOrder.Count;

                Assert.IsTrue(_turnManager.TryDelayActiveUnit(out string message), message);
                StringAssert.Contains("-2", message);
                Assert.AreEqual(before - 2, _turnManager.GetInitiativeTotal(active));
                Assert.IsTrue(DelayedTurnState.HasDelayed(active.Stats));
                Assert.AreNotSame(active, _turnManager.ActiveUnit);
                Assert.AreEqual(countBefore, _turnManager.TurnOrder.Count);

                // Le retardé est plus tard dans l'ordre, pas en tête.
                var order = _turnManager.TurnOrder;
                int idx = -1;
                for (int i = 0; i < order.Count; i++)
                    if (order[i] == active) idx = i;
                Assert.Greater(idx, 0);
            }
            finally
            {
                CleanupTurnManager();
            }
        }

        [Test]
        public void TurnManager_HoldThenResume_InsertsJustAfterActive()
        {
            CreateCombatManager();
            try
            {
                var a = SpawnUnit("Alpha", true, 0, 0);
                var b = SpawnUnit("Bravo", true, 1, 0);
                var e = SpawnUnit("Gredin", false, 5, 5);
                _turnManager.SuspendAutoStart = false;
                _turnManager.SetDiceSeed(7);
                _turnManager.EnterCombatMode();

                var holder = _turnManager.ActiveUnit;
                Assert.IsNotNull(holder);
                int heldPA = holder.Stats.CurrentActionPoints;

                Assert.IsTrue(_turnManager.TryHoldActiveUnit(out string holdMsg), holdMsg);
                Assert.IsTrue(DelayedTurnState.IsHolding(holder.Stats));
                Assert.AreNotSame(holder, _turnManager.ActiveUnit);

                var current = _turnManager.ActiveUnit;
                Assert.IsNotNull(current);
                Assert.IsTrue(_turnManager.TryResumeHeldUnit(holder, out string resumeMsg), resumeMsg);
                Assert.IsFalse(DelayedTurnState.IsHolding(holder.Stats));
                Assert.AreEqual(heldPA, holder.Stats.CurrentActionPoints);

                var order = _turnManager.TurnOrder;
                int currentIdx = -1, heldIdx = -1;
                for (int i = 0; i < order.Count; i++)
                {
                    if (order[i] == current) currentIdx = i;
                    if (order[i] == holder) heldIdx = i;
                }
                Assert.AreEqual(currentIdx + 1, heldIdx);
            }
            finally
            {
                CleanupTurnManager();
            }
        }

        [Test]
        public void TurnManager_DelayRefusedTwice_HoldRefusedWhenNotActive()
        {
            CreateCombatManager();
            try
            {
                var a = SpawnUnit("Alpha", true, 0, 0);
                var b = SpawnUnit("Bravo", true, 1, 0);
                var e = SpawnUnit("Gredin", false, 5, 5);
                _turnManager.SuspendAutoStart = false;
                _turnManager.SetDiceSeed(11);
                _turnManager.EnterCombatMode();

                var active = _turnManager.ActiveUnit;
                Assert.IsTrue(_turnManager.TryDelayActiveUnit(out _));

                // L'unité retardée n'est plus active : second retard impossible
                // (TryDelayActiveUnit agit sur la nouvelle unité active ; on
                // vérifie le garde 1/round au niveau de l'état).
                Assert.IsTrue(DelayedTurnState.HasDelayed(active.Stats));
                Assert.IsFalse(DelayedTurnState.TryDelay(active.Stats, 2, out _, out _));

                // Interruption sans attente : refusée.
                Assert.IsFalse(_turnManager.TryResumeHeldUnit(active, out string resumeMsg));
                StringAssert.Contains("aucune action", resumeMsg);
            }
            finally
            {
                CleanupTurnManager();
            }
        }

        private void CleanupTurnManager()
        {
            DelayedTurnState.ClearAll();
            ChannelingState.ClearAll();
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            }
            _spawned.Clear();
            if (_holder != null) Object.DestroyImmediate(_holder);
            _holder = null;
            _turnManager = null;
        }

        /// <summary>Mock séquentiel : chaque appel à Roll consomme la valeur suivante.</summary>
        private class QueueDiceRoller : DiceRoller
        {
            private readonly Queue<int> _values;

            public QueueDiceRoller(params int[] values) : base(0)
            {
                _values = new Queue<int>(values);
            }

            public override DiceRollResult Roll(DiceType dieType, int modifier = 0, int targetDC = 10)
            {
                int val = _values.Count > 0 ? _values.Dequeue() : 5;
                return new DiceRollResult
                {
                    DieType = dieType,
                    RawRoll = val,
                    Modifier = modifier,
                    Total = val + modifier,
                    TargetDC = targetDC,
                    Differential = (val + modifier) - targetDC,
                    IsSuccess = (val + modifier) >= targetDC,
                    IsCriticalSuccess = false,
                    IsCriticalFailure = false
                };
            }
        }
    }
}
#endif
