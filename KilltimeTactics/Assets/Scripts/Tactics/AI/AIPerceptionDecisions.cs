using System.Collections.Generic;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// RD-115 (phase 2) : décisions de perception pures, extraites verbatim du
    /// contrôleur. Aucune ref MonoBehaviour, aucun singleton : le contrôleur
    /// réunit les lectures Unity (brouillard, statuts, escouade) dans les
    /// structs d'entrée via <see cref="IAIPerception"/>, puis appelle ici.
    /// Testable en EditMode sans grille ni fog.
    /// </summary>

    /// <summary>
    /// Entrées du gating visibilité/bruit d'un candidat d'EvaluateBestTarget.
    /// Le contrôleur résout chaque champ via IAIPerception + états Core.
    /// </summary>
    public struct AITargetVisibilityInput
    {
        public bool SeenByFog;       // IsCellVisible à 360° (portée + murs)
        public bool IsNight;
        public bool TargetHasLamp;   // lampe allumée : trahit la position
        public bool LampLineOfSight; // HasLineOfSight (CdV + fumée)
        public bool TargetStealthed; // StealthState
        public int FootprintDistance; // MinDistanceBetweenUnits (contact = 1)
        public bool ObserverThermal; // vp.Thermal (spé ou jumelles)
        public bool NoiseRevealed;   // tir bruyant récent
        public bool ManuallyRevealed; // jet d'Observation réussi
    }

    /// <summary>Bruit d'escouade entendu (tirs) : état statique du contrôleur.</summary>
    public struct AIPatrolSquadNoise
    {
        public bool HasValue;
        public HexCoordinates Coords;
        public int AlertRound;
        public bool FromPlayer;
    }

    /// <summary>Ennemi snapshoté pour le scan de patrouille (données plaines).</summary>
    public struct AIPatrolEnemy
    {
        public bool IsSelf;
        public bool IsAlive;
        public bool IsPlayerControlled;
        public int CenterDistance; // distance centre-à-centre acteur→ennemi
        public HexCoordinates Coords;
        public bool HasLamp;
        public bool NoiseActive;
        public HexCoordinates? LastKnownCoords; // fantôme HUD si jamais vu
    }

    /// <summary>Entrées de la patrouille alertée (RD-053).</summary>
    public struct AIPatrolInput
    {
        public bool ActorAlive;
        public int ActorActionPoints;
        public bool SensorsReady; // grille + pathfinder présents
        public HexCoordinates ActorCoords;
        public bool ActorIsPlayer;
        public bool FullAutoMode;
        public int CurrentRound;
        public AIPatrolSquadNoise SquadNoise;
        public bool IsNight;
        public IReadOnlyList<AIPatrolEnemy> Enemies;
    }

    /// <summary>Résultat : cible de convergence + raison d'alerte.</summary>
    public struct AIPatrolResult
    {
        public bool HasTarget;
        public HexCoordinates Target;
        public string Reason;
        // Bruit d'escouade périmé (acteur arrivé sur place) : le contrôleur
        // doit purger _squadLastHeardNoiseCoords (effet de bord d'origine).
        public bool ClearStaleSquadNoise;
    }

    public static class AIPerceptionDecisions
    {
        public const string ReasonSquadNoise = "Tir bruyant entendu";
        public const string ReasonLocalizedNoise = "Tir bruyant localisé";
        public const string ReasonLightBeam = "Faisceau lumineux repéré";

        /// <summary>
        /// Gating fog + furtivité d'EvaluateBestTarget (verbatim).
        /// RD-053 : la lampe dans la nuit sauve un candidat hors vue si la
        /// ligne de mire passe. La furtivité bloque au-delà du contact sauf
        /// thermique ; le bruit / l'observation révèlent toujours.
        /// </summary>
        public static bool IsCandidateVisible(in AITargetVisibilityInput input)
        {
            bool seen = input.SeenByFog;
            if (!seen && input.IsNight && input.TargetHasLamp && input.LampLineOfSight)
                seen = true;
            if (!seen || (input.TargetStealthed && input.FootprintDistance > 1 && !input.ObserverThermal))
                return input.NoiseRevealed || input.ManuallyRevealed;
            return true;
        }

        /// <summary>
        /// Sélection de la cible de patrouille alertée (verbatim de
        /// HasAlertedPatrolTarget). Ordre inchangé : bruit d'escouade d'abord,
        /// puis scan bruit localisé / faisceau lumineux par ennemi.
        /// </summary>
        public static AIPatrolResult FindPatrolTarget(in AIPatrolInput input)
        {
            var none = new AIPatrolResult { HasTarget = false };
            if (!input.ActorAlive || input.ActorActionPoints <= 0 || !input.SensorsReady)
                return none;

            bool isNoiseHostile = input.ActorIsPlayer
                ? !input.SquadNoise.FromPlayer
                : input.SquadNoise.FromPlayer;
            if (input.SquadNoise.HasValue && isNoiseHostile
                && input.SquadNoise.AlertRound >= input.CurrentRound - 1)
            {
                int noiseDist = input.ActorCoords.DistanceTo(input.SquadNoise.Coords);
                if (noiseDist > 1)
                {
                    return new AIPatrolResult
                    {
                        HasTarget = true,
                        Target = input.SquadNoise.Coords,
                        Reason = ReasonSquadNoise,
                    };
                }
                none.ClearStaleSquadNoise = true;
            }

            var enemies = input.Enemies;
            if (enemies == null)
                return none;
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (enemy.IsSelf || !enemy.IsAlive)
                    continue;
                bool hostile = input.ActorIsPlayer
                    ? !enemy.IsPlayerControlled
                    : enemy.IsPlayerControlled;
                if (input.FullAutoMode && enemy.IsPlayerControlled != input.ActorIsPlayer)
                    hostile = true;
                if (!hostile)
                    continue;
                if (enemy.CenterDistance <= 1)
                    continue;
                if (enemy.NoiseActive)
                {
                    return new AIPatrolResult
                    {
                        HasTarget = true,
                        Target = enemy.LastKnownCoords ?? enemy.Coords,
                        Reason = ReasonLocalizedNoise,
                        ClearStaleSquadNoise = none.ClearStaleSquadNoise,
                    };
                }
                if (input.IsNight && enemy.HasLamp)
                {
                    return new AIPatrolResult
                    {
                        HasTarget = true,
                        Target = enemy.Coords,
                        Reason = ReasonLightBeam,
                        ClearStaleSquadNoise = none.ClearStaleSquadNoise,
                    };
                }
            }

            return none;
        }
    }
}
