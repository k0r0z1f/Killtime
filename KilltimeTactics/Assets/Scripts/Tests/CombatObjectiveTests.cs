#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Objectives;
using Killtime.Core.Character;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-050 : Objectifs autres que kill-all (Extraction, tenir X rounds, escorter, hacker terminal).
    /// Validation des règles d'évaluation dans TurnManager.CheckCombatOver.
    /// </summary>
    [TestFixture]
    public class CombatObjectiveTests
    {
        private GameObject _holder;
        private TurnManager _turnManager;
        private readonly List<GameObject> _spawnedObjects = new();

        private static Attributes DefaultAttrs => new Attributes(3, 3, 3, 3, 2, 2, 1, 2, 0);

        [SetUp]
        public void SetUp()
        {
            _holder = new GameObject("Test_TurnManager_Holder");
            _turnManager = _holder.AddComponent<TurnManager>();
            _turnManager.SetExplorationMode(false); // Mode combat
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawnedObjects.Count; i++)
            {
                if (_spawnedObjects[i] != null)
                {
                    Object.DestroyImmediate(_spawnedObjects[i]);
                }
            }
            _spawnedObjects.Clear();

            if (_holder != null)
            {
                Object.DestroyImmediate(_holder);
            }
        }

        private TacticalUnit CreateUnit(string name, bool isPlayer, HexCoordinates coords)
        {
            var go = new GameObject($"Unit_{name}");
            _spawnedObjects.Add(go);
            var unit = go.AddComponent<TacticalUnit>();
            unit.ConfigureStats(name, DefaultAttrs, baseArmor: 1, isPlayer: isPlayer);
            unit.SetPositionDirect(coords);
            _turnManager.RegisterUnit(unit);
            return unit;
        }

        private TacticalInteractable CreateTerminal(string name, HexCoordinates coords)
        {
            var go = new GameObject($"Terminal_{name}");
            _spawnedObjects.Add(go);
            var prop = go.AddComponent<TacticalInteractable>();
            prop.Configure(name, "Hacker", coords, radius: 1);
            return prop;
        }

        private static void KillUnit(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return;
            unit.Stats.CurrentHealth = 0;
            unit.Stats.IsDead = true;
            unit.Stats.ChooseSombrer();
        }

        [Test]
        public void Annihilation_DefaultBehavior_VictoryWhenAllEnemiesDead()
        {
            var p1 = CreateUnit("Héros", true, new HexCoordinates(0, 0));
            var e1 = CreateUnit("Gredin", false, new HexCoordinates(2, 0));

            Assert.IsFalse(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.InProgress, _turnManager.CurrentOutcome);

            // Tuer l'ennemi
            KillUnit(e1);
            Assert.IsFalse(e1.Stats.IsAlive);

            Assert.IsTrue(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.Victory, _turnManager.CurrentOutcome);
            StringAssert.Contains("neutralisées", _turnManager.CombatEndReason);
        }

        [Test]
        public void Annihilation_DefeatWhenAllPlayersDead()
        {
            var p1 = CreateUnit("Héros", true, new HexCoordinates(0, 0));
            var e1 = CreateUnit("Gredin", false, new HexCoordinates(2, 0));

            // Tuer le héros
            KillUnit(p1);
            Assert.IsFalse(p1.Stats.IsAlive);

            Assert.IsTrue(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.Defeat, _turnManager.CurrentOutcome);
            StringAssert.Contains("Anéantissement", _turnManager.CombatEndReason);
        }

        [Test]
        public void Extraction_RequiresAllLivingSquadMembersInZone()
        {
            var p1 = CreateUnit("Alpha", true, new HexCoordinates(0, 0));
            var p2 = CreateUnit("Bravo", true, new HexCoordinates(1, 0));
            var e1 = CreateUnit("Gredin", false, new HexCoordinates(5, 5));

            var exitZone = new List<HexCoordinates> { new HexCoordinates(3, 3), new HexCoordinates(3, 4) };
            _turnManager.SetupExtractionPreset(exitZone, requireAllLiving: true);

            // Aucun en zone
            Assert.IsFalse(_turnManager.CheckCombatOver());

            // Seul Alpha en zone
            p1.SetPositionDirect(new HexCoordinates(3, 3));
            Assert.IsFalse(_turnManager.CheckCombatOver());

            // Bravo rejoint la zone
            p2.SetPositionDirect(new HexCoordinates(3, 4));
            Assert.IsTrue(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.Victory, _turnManager.CurrentOutcome);
            StringAssert.Contains("Zone d'Extraction", _turnManager.CombatEndReason);
        }

        [Test]
        public void Extraction_IgnoresFallenSquadMembers()
        {
            var p1 = CreateUnit("Alpha", true, new HexCoordinates(0, 0));
            var p2 = CreateUnit("Bravo", true, new HexCoordinates(1, 0));
            var e1 = CreateUnit("Gredin", false, new HexCoordinates(5, 5));

            var exitZone = new List<HexCoordinates> { new HexCoordinates(3, 3) };
            _turnManager.SetupExtractionPreset(exitZone, requireAllLiving: true);

            // Bravo est tué
            KillUnit(p2);
            Assert.IsFalse(p2.Stats.IsAlive);

            // Alpha entre seul en zone d'extraction
            p1.SetPositionDirect(new HexCoordinates(3, 3));
            Assert.IsTrue(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.Victory, _turnManager.CurrentOutcome);
        }

        [Test]
        public void Extraction_RequireAtLeastOneSquadMember()
        {
            var p1 = CreateUnit("Alpha", true, new HexCoordinates(0, 0));
            var p2 = CreateUnit("Bravo", true, new HexCoordinates(1, 0));
            var e1 = CreateUnit("Gredin", false, new HexCoordinates(5, 5));

            var exitZone = new List<HexCoordinates> { new HexCoordinates(4, 4) };
            _turnManager.SetupExtractionPreset(exitZone, requireAllLiving: false);

            p1.SetPositionDirect(new HexCoordinates(4, 4));
            Assert.IsTrue(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.Victory, _turnManager.CurrentOutcome);
        }

        [Test]
        public void SurviveRounds_VictoryUponReachingTargetRound()
        {
            var p1 = CreateUnit("Alpha", true, new HexCoordinates(0, 0));
            var e1 = CreateUnit("Gredin", false, new HexCoordinates(2, 0));

            // Tenir 3 rounds
            _turnManager.SetupSurvivePreset(3);

            Assert.IsFalse(_turnManager.CheckCombatOver());
            Assert.AreEqual(1, _turnManager.CurrentRound);

            // Tour 1 terminé, début Tour 2
            _turnManager.EndCurrentTurn();
            Assert.IsFalse(_turnManager.IsCombatOver);

            // Tour 2 -> Fin de round -> Début Round 2
            _turnManager.EndCurrentTurn();
            Assert.IsFalse(_turnManager.IsCombatOver);

            // Tuer les héros avant le round 3 -> Défaite
            KillUnit(p1);
            Assert.IsTrue(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.Defeat, _turnManager.CurrentOutcome);
        }

        [Test]
        public void EscortVIP_DefeatImmediatelyIfVipDies()
        {
            var p1 = CreateUnit("Garde", true, new HexCoordinates(0, 0));
            var vip = CreateUnit("VIP_Docteur", true, new HexCoordinates(1, 0));
            var e1 = CreateUnit("Assassins", false, new HexCoordinates(4, 0));

            _turnManager.SetupEscortPreset(vip, new[] { new HexCoordinates(6, 0) });

            Assert.IsFalse(_turnManager.CheckCombatOver());

            // Le VIP est abattu
            KillUnit(vip);
            Assert.IsFalse(vip.Stats.IsAlive);

            Assert.IsTrue(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.Defeat, _turnManager.CurrentOutcome);
            StringAssert.Contains("VIP", _turnManager.CombatEndReason);
        }

        [Test]
        public void EscortVIP_VictoryWhenDestinationReached()
        {
            var p1 = CreateUnit("Garde", true, new HexCoordinates(0, 0));
            var vip = CreateUnit("VIP_Docteur", true, new HexCoordinates(1, 0));
            var e1 = CreateUnit("Assassins", false, new HexCoordinates(4, 0));

            var dest = new List<HexCoordinates> { new HexCoordinates(5, 0) };
            _turnManager.SetupEscortPreset(vip, dest);

            Assert.IsFalse(_turnManager.CheckCombatOver());

            // Déplacer le VIP sur la case destination
            vip.SetPositionDirect(new HexCoordinates(5, 0));

            Assert.IsTrue(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.Victory, _turnManager.CurrentOutcome);
        }

        [Test]
        public void HackTerminal_InteractionCompletesObjective()
        {
            var p1 = CreateUnit("Hacker", true, new HexCoordinates(0, 0));
            var e1 = CreateUnit("Sentinelle", false, new HexCoordinates(4, 0));
            var terminal = CreateTerminal("Console_Principale", new HexCoordinates(1, 0));

            _turnManager.SetupHackTerminalPreset(terminal);

            Assert.IsFalse(_turnManager.CheckCombatOver());

            // Interaction avec le terminal
            bool success = terminal.TryInteract(p1, out _);
            Assert.IsTrue(success);

            Assert.IsTrue(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.Victory, _turnManager.CurrentOutcome);
            StringAssert.Contains("Console_Principale", _turnManager.CombatEndReason);
        }

        [Test]
        public void Composite_HackThenExtract_RequiresBoth()
        {
            var p1 = CreateUnit("Operateur", true, new HexCoordinates(0, 0));
            var e1 = CreateUnit("Garde", false, new HexCoordinates(5, 0));
            var terminal = CreateTerminal("Serveur_Donnees", new HexCoordinates(1, 0));
            var extraction = new List<HexCoordinates> { new HexCoordinates(4, 4) };

            _turnManager.ClearObjectives();
            var hackObj = CombatObjective.CreateHackTerminal(terminal, "Hacker Serveur");
            var extractObj = CombatObjective.CreateExtraction(extraction, true, "Extraction Navette");

            _turnManager.AddObjective(hackObj);
            _turnManager.AddObjective(extractObj);

            // 1. Juste en zone d'extraction sans avoir hacké -> Pas de victoire
            p1.SetPositionDirect(new HexCoordinates(4, 4));
            Assert.IsFalse(_turnManager.CheckCombatOver());

            // 2. Retourne hacker le terminal mais hors zone -> Pas de victoire
            p1.SetPositionDirect(new HexCoordinates(1, 0));
            terminal.TryInteract(p1, out _);
            Assert.IsTrue(hackObj.IsCompleted);
            Assert.IsFalse(_turnManager.CheckCombatOver());

            // 3. Retourne en zone d'extraction -> Les deux sont complétés -> Victoire !
            p1.SetPositionDirect(new HexCoordinates(4, 4));
            Assert.IsTrue(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.Victory, _turnManager.CurrentOutcome);
        }

        [Test]
        public void OptionalObjective_FailureDoesNotBlockVictory()
        {
            var p1 = CreateUnit("Heros", true, new HexCoordinates(0, 0));
            var e1 = CreateUnit("Gredin", false, new HexCoordinates(3, 0));

            _turnManager.ClearObjectives();
            var mainObj = CombatObjective.CreateExtraction(new[] { new HexCoordinates(1, 0) }, true, "Extraction Principale", isOptional: false);
            var optObj = CombatObjective.CreateAnnihilation("Bonus Anéantissement", isOptional: true);

            _turnManager.AddObjective(mainObj);
            _turnManager.AddObjective(optObj);

            // Héros s'extrait sans tuer l'ennemi
            p1.SetPositionDirect(new HexCoordinates(1, 0));

            Assert.IsTrue(_turnManager.CheckCombatOver());
            Assert.AreEqual(CombatOutcome.Victory, _turnManager.CurrentOutcome);
            Assert.IsTrue(mainObj.IsCompleted);
            Assert.IsFalse(optObj.IsCompleted);
        }

        [Test]
        public void SurviveRounds_VictoryWhenHoldingRequiredRounds()
        {
            var p1 = CreateUnit("Alpha", true, new HexCoordinates(0, 0));
            var e1 = CreateUnit("Gredin", false, new HexCoordinates(2, 0));

            // Tenir jusqu'au round 3
            _turnManager.ClearObjectives();
            _turnManager.AddObjective(CombatObjective.CreateSurvive(3));

            Assert.IsFalse(_turnManager.CheckCombatOver());
            Assert.AreEqual(1, _turnManager.CurrentRound);

            // Jouer le Round 1 (Alpha + Gredin) -> Début Round 2
            _turnManager.EndCurrentTurn();
            _turnManager.EndCurrentTurn();
            Assert.AreEqual(2, _turnManager.CurrentRound);
            Assert.IsFalse(_turnManager.IsCombatOver);

            // Jouer le Round 2 (Alpha + Gredin) -> Début Round 3
            _turnManager.EndCurrentTurn();
            _turnManager.EndCurrentTurn();
            Assert.AreEqual(3, _turnManager.CurrentRound);

            // Au round 3, l'objectif de survie est complété -> Victoire !
            Assert.IsTrue(_turnManager.IsCombatOver);
            Assert.AreEqual(CombatOutcome.Victory, _turnManager.CurrentOutcome);
            StringAssert.Contains("Tenir la Position", _turnManager.CombatEndReason);
        }
    }
}
#endif
