#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Tactics.AI.Tests
{
    /// <summary>
    /// RD-115 (phase 2, IAIPerception) : le gating fog/furtivité d'EvaluateBestTarget
    /// et la sélection de patrouille alertée sont des décisions pures derrière
    /// IAIPerception. Fake en EditMode : aucune scène, aucun singleton.
    /// </summary>
    [TestFixture]
    public class AIPerceptionTests
    {
        private static AITargetVisibilityInput VisibleInput() => new AITargetVisibilityInput
        {
            SeenByFog = true,
            IsNight = false,
            TargetHasLamp = false,
            LampLineOfSight = false,
            TargetStealthed = false,
            FootprintDistance = 4,
            ObserverThermal = false,
            NoiseRevealed = false,
            ManuallyRevealed = false,
        };

        private static AIPatrolInput BasePatrolInput() => new AIPatrolInput
        {
            ActorAlive = true,
            ActorActionPoints = 6,
            SensorsReady = true,
            ActorCoords = new HexCoordinates(0, 0),
            ActorIsPlayer = false,
            FullAutoMode = false,
            CurrentRound = 5,
            SquadNoise = new AIPatrolSquadNoise { HasValue = false },
            IsNight = false,
            Enemies = new List<AIPatrolEnemy>(),
        };

        private static AIPatrolEnemy EnemyAt(HexCoordinates coords, int centerDist, bool isPlayer = true)
        {
            return new AIPatrolEnemy
            {
                IsSelf = false,
                IsAlive = true,
                IsPlayerControlled = isPlayer,
                CenterDistance = centerDist,
                Coords = coords,
                HasLamp = false,
                NoiseActive = false,
                LastKnownCoords = null,
            };
        }

        // ---- Gating visibilité ----

        [Test]
        public void Visible_PlainCandidate_IsKept()
        {
            Assert.IsTrue(AIPerceptionDecisions.IsCandidateVisible(VisibleInput()));
        }

        [Test]
        public void Unseen_WithoutReveal_IsSkipped()
        {
            var input = VisibleInput();
            input.SeenByFog = false;
            Assert.IsFalse(AIPerceptionDecisions.IsCandidateVisible(input));
        }

        [Test]
        public void Unseen_NoiseOrManualReveal_IsKept()
        {
            var noise = VisibleInput();
            noise.SeenByFog = false;
            noise.NoiseRevealed = true;
            Assert.IsTrue(AIPerceptionDecisions.IsCandidateVisible(noise));

            var manual = VisibleInput();
            manual.SeenByFog = false;
            manual.ManuallyRevealed = true;
            Assert.IsTrue(AIPerceptionDecisions.IsCandidateVisible(manual));
        }

        [Test]
        public void NightLamp_WithLOS_Rescues_WithoutLOS_Skipped()
        {
            var rescued = VisibleInput();
            rescued.SeenByFog = false;
            rescued.IsNight = true;
            rescued.TargetHasLamp = true;
            rescued.LampLineOfSight = true;
            Assert.IsTrue(AIPerceptionDecisions.IsCandidateVisible(rescued));

            var blocked = rescued;
            blocked.LampLineOfSight = false;
            Assert.IsFalse(AIPerceptionDecisions.IsCandidateVisible(blocked));
        }

        [Test]
        public void DayLamp_DoesNotRescue()
        {
            var input = VisibleInput();
            input.SeenByFog = false;
            input.IsNight = false;
            input.TargetHasLamp = true;
            input.LampLineOfSight = true;
            Assert.IsFalse(AIPerceptionDecisions.IsCandidateVisible(input));
        }

        [Test]
        public void Stealth_BeyondContact_BlocksDespiteSeen()
        {
            var input = VisibleInput();
            input.TargetStealthed = true;
            input.FootprintDistance = 4;
            Assert.IsFalse(AIPerceptionDecisions.IsCandidateVisible(input));

            input.NoiseRevealed = true;
            Assert.IsTrue(AIPerceptionDecisions.IsCandidateVisible(input));
        }

        [Test]
        public void Stealth_AtContactOrThermal_DoesNotBlock()
        {
            var contact = VisibleInput();
            contact.TargetStealthed = true;
            contact.FootprintDistance = 1;
            Assert.IsTrue(AIPerceptionDecisions.IsCandidateVisible(contact));

            var thermal = VisibleInput();
            thermal.TargetStealthed = true;
            thermal.FootprintDistance = 4;
            thermal.ObserverThermal = true;
            Assert.IsTrue(AIPerceptionDecisions.IsCandidateVisible(thermal));
        }

        // ---- Patrouille alertée ----

        [Test]
        public void Patrol_DeadOrNoAPOrNoSensors_HasNoTarget()
        {
            var dead = BasePatrolInput();
            dead.ActorAlive = false;
            Assert.IsFalse(AIPerceptionDecisions.FindPatrolTarget(dead).HasTarget);

            var exhausted = BasePatrolInput();
            exhausted.ActorActionPoints = 0;
            Assert.IsFalse(AIPerceptionDecisions.FindPatrolTarget(exhausted).HasTarget);

            var blind = BasePatrolInput();
            blind.SensorsReady = false;
            Assert.IsFalse(AIPerceptionDecisions.FindPatrolTarget(blind).HasTarget);
        }

        [Test]
        public void Patrol_FreshHostileSquadNoise_Converges()
        {
            var input = BasePatrolInput();
            input.SquadNoise = new AIPatrolSquadNoise
            {
                HasValue = true,
                Coords = new HexCoordinates(4, 1),
                AlertRound = 5,
                FromPlayer = true, // hostile pour un acteur IA
            };
            var result = AIPerceptionDecisions.FindPatrolTarget(input);
            Assert.IsTrue(result.HasTarget);
            Assert.AreEqual(new HexCoordinates(4, 1), result.Target);
            Assert.AreEqual(AIPerceptionDecisions.ReasonSquadNoise, result.Reason);
        }

        [Test]
        public void Patrol_SquadNoiseArrived_ClearsStale()
        {
            var input = BasePatrolInput();
            input.SquadNoise = new AIPatrolSquadNoise
            {
                HasValue = true,
                Coords = new HexCoordinates(0, 0), // sur place
                AlertRound = 5,
                FromPlayer = true,
            };
            var result = AIPerceptionDecisions.FindPatrolTarget(input);
            Assert.IsFalse(result.HasTarget);
            Assert.IsTrue(result.ClearStaleSquadNoise);
        }

        [Test]
        public void Patrol_StaleOrFriendlyNoise_Ignored()
        {
            var stale = BasePatrolInput();
            stale.SquadNoise = new AIPatrolSquadNoise
            {
                HasValue = true,
                Coords = new HexCoordinates(4, 1),
                AlertRound = 3, // trop vieux (round 5)
                FromPlayer = true,
            };
            Assert.IsFalse(AIPerceptionDecisions.FindPatrolTarget(stale).HasTarget);

            var friendly = BasePatrolInput();
            friendly.SquadNoise = new AIPatrolSquadNoise
            {
                HasValue = true,
                Coords = new HexCoordinates(4, 1),
                AlertRound = 5,
                FromPlayer = false, // tirs alliés IA : pas hostile
            };
            Assert.IsFalse(AIPerceptionDecisions.FindPatrolTarget(friendly).HasTarget);
        }

        [Test]
        public void Patrol_EnemyNoise_LocalizesLastKnownOrCoords()
        {
            var ghost = new HexCoordinates(6, 2);
            var enemy = EnemyAt(new HexCoordinates(3, 0), 3);
            enemy.NoiseActive = true;
            enemy.LastKnownCoords = ghost;

            var input = BasePatrolInput();
            input.Enemies = new List<AIPatrolEnemy> { enemy };
            var result = AIPerceptionDecisions.FindPatrolTarget(input);
            Assert.IsTrue(result.HasTarget);
            Assert.AreEqual(ghost, result.Target);
            Assert.AreEqual(AIPerceptionDecisions.ReasonLocalizedNoise, result.Reason);

            var fresh = enemy;
            fresh.LastKnownCoords = null;
            var input2 = BasePatrolInput();
            input2.Enemies = new List<AIPatrolEnemy> { fresh };
            Assert.AreEqual(
                new HexCoordinates(3, 0),
                AIPerceptionDecisions.FindPatrolTarget(input2).Target);
        }

        [Test]
        public void Patrol_NightLampBeam_Spotted_DayLamp_Ignored()
        {
            var lamp = EnemyAt(new HexCoordinates(4, 1), 4);
            lamp.HasLamp = true;

            var night = BasePatrolInput();
            night.IsNight = true;
            night.Enemies = new List<AIPatrolEnemy> { lamp };
            var result = AIPerceptionDecisions.FindPatrolTarget(night);
            Assert.IsTrue(result.HasTarget);
            Assert.AreEqual(new HexCoordinates(4, 1), result.Target);
            Assert.AreEqual(AIPerceptionDecisions.ReasonLightBeam, result.Reason);

            var day = BasePatrolInput();
            day.IsNight = false;
            day.Enemies = new List<AIPatrolEnemy> { lamp };
            Assert.IsFalse(AIPerceptionDecisions.FindPatrolTarget(day).HasTarget);
        }

        [Test]
        public void Patrol_AdjacentEnemiesAndAllies_Skipped()
        {
            var adjacent = EnemyAt(new HexCoordinates(0, 0), 1);
            adjacent.NoiseActive = true; // même bruyant : le contact se gère au ciblage
            var ally = EnemyAt(new HexCoordinates(4, 1), 4, isPlayer: false);
            ally.NoiseActive = true;

            var input = BasePatrolInput();
            input.Enemies = new List<AIPatrolEnemy> { adjacent, ally };
            Assert.IsFalse(AIPerceptionDecisions.FindPatrolTarget(input).HasTarget);
        }

        [Test]
        public void Patrol_FullAuto_SameSide_NotHostile()
        {
            // Comportement verbatim : même camp en FullAuto n'est pas hostile
            // (la clause FullAuto exige des camps différents, cas où hostile
            // est déjà vrai — elle est inopérante, conservée telle quelle).
            var lamp = EnemyAt(new HexCoordinates(4, 1), 4, isPlayer: false);
            lamp.HasLamp = true;

            var input = BasePatrolInput();
            input.FullAutoMode = true;
            input.IsNight = true;
            input.Enemies = new List<AIPatrolEnemy> { lamp };
            Assert.IsFalse(AIPerceptionDecisions.FindPatrolTarget(input).HasTarget);
        }

        // ---- Live impl sans manager ----

        [Test]
        public void LivePerception_WithoutManager_SafeDefaults()
        {
            if (FogOfWarManager.Instance != null)
                Assert.Ignore("Fog de scène présent : défauts non testables ici.");
            var perception = new FogOfWarPerception();
            Assert.IsFalse(perception.IsNight);
            Assert.AreEqual(FogOfWarSystem.LongSightRange, perception.SightRange);
            Assert.IsFalse(perception.IsNoiseActive("QuelquUn"));
            Assert.IsFalse(perception.IsManuallyRevealed("QuelquUn"));
            Assert.IsNull(perception.GetLastKnownPosition("QuelquUn"));
        }
    }
}
#endif
