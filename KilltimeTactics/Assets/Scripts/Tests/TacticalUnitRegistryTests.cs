#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;

namespace Killtime.Tests
{
    [TestFixture]
    public class TacticalUnitRegistryTests
    {
        private GameObject _container;

        [SetUp]
        public void Setup()
        {
            TacticalUnitRegistry.Clear();
            _container = new GameObject("RegistryTestContainer");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_container);
            TacticalUnitRegistry.Clear();
        }

        private TacticalUnit CreateUnit(bool isPlayer = true)
        {
            var go = new GameObject("TestUnit");
            go.transform.SetParent(_container.transform);
            var unit = go.AddComponent<TacticalUnit>();
            unit.ConfigureStats(
                "Test",
                new Attributes(@for: 3, agi: 3, con: 3, rap: 3, @int: 3, eru: 3, cha: 3, ins: 3, mag: 0),
                0,
                isPlayer
            );
            unit.Stats.CurrentHealth = 10;
            unit.Stats.IsDead = false;
            return unit;
        }

        [Test]
        public void Register_AddsUnit_OnEnable()
        {
            var unit = CreateUnit();
            Assert.That(TacticalUnitRegistry.AllUnits, Has.Member(unit));
        }

        [Test]
        public void Register_Duplicate_IsIgnored()
        {
            var unit = CreateUnit();
            var countBefore = TacticalUnitRegistry.AllUnits.Count;
            TacticalUnitRegistry.Register(unit);
            Assert.AreEqual(countBefore, TacticalUnitRegistry.AllUnits.Count);
        }

        [Test]
        public void Unregister_RemovesUnit()
        {
            var unit = CreateUnit();
            TacticalUnitRegistry.Unregister(unit);
            Assert.That(TacticalUnitRegistry.AllUnits, Has.No.Member(unit));
        }

        [Test]
        public void Clear_RemovesAllUnits_And_FiresEvents()
        {
            var a = CreateUnit();
            var b = CreateUnit();
            int unregistered = 0;
            Action<TacticalUnit> handler = _ => unregistered++;
            TacticalUnitRegistry.OnUnitUnregistered += handler;

            TacticalUnitRegistry.Clear();

            Assert.AreEqual(0, TacticalUnitRegistry.AllUnits.Count);
            Assert.AreEqual(2, unregistered);
            TacticalUnitRegistry.OnUnitUnregistered -= handler;
        }

        [Test]
        public void GetAliveUnits_FiltersDeadUnits()
        {
            var alive = CreateUnit();
            var dead = CreateUnit();
            dead.Stats.CurrentHealth = 0;
            dead.Stats.IsDead = true;

            var result = new List<TacticalUnit>(TacticalUnitRegistry.GetAliveUnits());

            Assert.That(result, Has.Member(alive));
            Assert.That(result, Has.No.Member(dead));
        }

        [Test]
        public void GetPlayerUnits_And_GetEnemyUnits_SplitByControl()
        {
            var player = CreateUnit(isPlayer: true);
            var enemy = CreateUnit(isPlayer: false);

            var players = new List<TacticalUnit>(TacticalUnitRegistry.GetPlayerUnits());
            var enemies = new List<TacticalUnit>(TacticalUnitRegistry.GetEnemyUnits());

            Assert.That(players, Has.Member(player));
            Assert.That(players, Has.No.Member(enemy));
            Assert.That(enemies, Has.Member(enemy));
            Assert.That(enemies, Has.No.Member(player));
        }

        [Test]
        public void GetUnitsAt_ReturnsUnitsOnHex()
        {
            var a = CreateUnit();
            var b = CreateUnit();
            var hex = new HexCoordinates(2, -1);
            a.SetPositionDirect(hex);
            b.SetPositionDirect(new HexCoordinates(3, -2));

            var atHex = new List<TacticalUnit>(TacticalUnitRegistry.GetUnitsAt(hex));

            Assert.That(atHex, Has.Member(a));
            Assert.That(atHex, Has.No.Member(b));
        }

        [Test]
        public void GetUnitsAt_IgnoresUnitsWithoutLogicalPosition()
        {
            var unit = CreateUnit();

            var atOrigin = new List<TacticalUnit>(TacticalUnitRegistry.GetUnitsAt(new HexCoordinates(0, 0)));

            Assert.That(atOrigin, Has.No.Member(unit));
        }

        [Test]
        public void OnUnitRegistered_EventFires()
        {
            TacticalUnit captured = null;
            Action<TacticalUnit> handler = u => captured = u;
            TacticalUnitRegistry.OnUnitRegistered += handler;

            var unit = CreateUnit();

            Assert.AreEqual(unit, captured);
            TacticalUnitRegistry.OnUnitRegistered -= handler;
        }

        [Test]
        public void OnUnitUnregistered_EventFires()
        {
            TacticalUnit captured = null;
            Action<TacticalUnit> handler = u => captured = u;
            TacticalUnitRegistry.OnUnitUnregistered += handler;

            var unit = CreateUnit();
            TacticalUnitRegistry.Unregister(unit);

            Assert.AreEqual(unit, captured);
            TacticalUnitRegistry.OnUnitUnregistered -= handler;
        }
    }
}
#endif
