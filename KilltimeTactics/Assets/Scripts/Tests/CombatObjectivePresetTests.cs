#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Objectives;
using Killtime.Core.Character;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-189 : presets objectifs paramétrables (surcouches TurnManager.Objectives).
    /// Presets Setup*Preset historiques inchangés ; aucune règle modifiée.
    /// </summary>
    [TestFixture]
    public class CombatObjectivePresetTests
    {
        private GameObject _holder;
        private TurnManager _turnManager;
        private readonly List<GameObject> _spawned = new();

        private static Attributes DefaultAttrs => new Attributes(3, 3, 3, 3, 2, 2, 1, 2, 0);

        [SetUp]
        public void SetUp()
        {
            _holder = new GameObject("Test_ObjectivePresets_Holder");
            _turnManager = _holder.AddComponent<TurnManager>();
            _turnManager.SetExplorationMode(false);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();
            if (_holder != null) Object.DestroyImmediate(_holder);
        }

        private TacticalUnit CreateUnit(string name, bool isPlayer, HexCoordinates coords)
        {
            var go = new GameObject($"Unit_{name}");
            _spawned.Add(go);
            var unit = go.AddComponent<TacticalUnit>();
            unit.ConfigureStats(name, DefaultAttrs, baseArmor: 1, isPlayer: isPlayer);
            unit.SetPositionDirect(coords);
            _turnManager.RegisterUnit(unit);
            return unit;
        }

        [Test]
        public void BuildObjectiveDisc_Radius0_SingleHex()
        {
            var zone = TurnManager.BuildObjectiveDisc(new HexCoordinates(2, 1), 0);
            Assert.AreEqual(1, zone.Count);
            Assert.Contains(new HexCoordinates(2, 1), zone);
        }

        [Test]
        public void BuildObjectiveDisc_Radius1_SevenHexes()
        {
            var center = new HexCoordinates(0, 0);
            var zone = TurnManager.BuildObjectiveDisc(center, 1);
            Assert.AreEqual(7, zone.Count);
            Assert.Contains(center, zone);
            for (int d = 0; d < 6; d++)
                Assert.Contains(center.GetNeighbor(d), zone);
        }

        [Test]
        public void BuildObjectiveDisc_NegativeRadius_ClampedToSingle()
        {
            var zone = TurnManager.BuildObjectiveDisc(new HexCoordinates(0, 0), -2);
            Assert.AreEqual(1, zone.Count);
        }

        [Test]
        public void SetupExtractionCustom_UsesDiscAndFlag()
        {
            CreateUnit("Alpha", true, new HexCoordinates(0, 0));
            _turnManager.SetupExtractionCustom(new HexCoordinates(3, 0), 1, false);
            Assert.IsTrue(_turnManager.HasCustomObjectives);
            var obj = _turnManager.Objectives[0];
            Assert.AreEqual(CombatObjectiveType.Extraction, obj.Type);
            Assert.AreEqual(7, obj.ExtractionCoords.Count);
            Assert.IsFalse(obj.RequireAllLivingSquad);
        }

        [Test]
        public void SetupSurviveCustom_ClampsToMin1()
        {
            CreateUnit("Alpha", true, new HexCoordinates(0, 0));
            int round = _turnManager.CurrentRound;
            _turnManager.SetupSurviveCustom(0);
            Assert.AreEqual(round, _turnManager.Objectives[0].TargetRounds);
            _turnManager.SetupSurviveCustom(4);
            Assert.AreEqual(round + 3, _turnManager.Objectives[0].TargetRounds);
        }

        [Test]
        public void SetupEscortCustom_DestinationDisc()
        {
            var vip = CreateUnit("VIP_Doc", true, new HexCoordinates(1, 0));
            var dest = TurnManager.BuildObjectiveDisc(new HexCoordinates(4, 0), 0);
            _turnManager.SetupEscortCustom(vip, dest);
            var obj = _turnManager.Objectives[0];
            Assert.AreEqual(CombatObjectiveType.EscortVIP, obj.Type);
            Assert.AreEqual(vip, obj.VipUnit);
            Assert.AreEqual(1, obj.VipDestinationCoords.Count);
        }

        [Test]
        public void SetupEscortCustom_NullDestination_NoDestination()
        {
            var vip = CreateUnit("VIP_Doc", true, new HexCoordinates(1, 0));
            _turnManager.SetupEscortCustom(vip, null);
            var obj = _turnManager.Objectives[0];
            Assert.AreEqual(0, obj.VipDestinationCoords.Count);
        }

        [Test]
        public void SetupHackTerminalCustom_ById_Count()
        {
            CreateUnit("Hacker", true, new HexCoordinates(0, 0));
            _turnManager.SetupHackTerminalCustom("Console_Test", 2, null);
            var obj = _turnManager.Objectives[0];
            Assert.AreEqual(CombatObjectiveType.HackTerminal, obj.Type);
            Assert.AreEqual("Console_Test", obj.TargetInteractableId);
            Assert.AreEqual(2, obj.TerminalsRequired);
        }
    }
}
#endif
