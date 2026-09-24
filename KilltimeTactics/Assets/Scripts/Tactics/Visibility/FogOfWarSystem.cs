// <copyright file="FogOfWarSystem.cs" company="Killtime Tactics">
// Brouillard de guerre asymétrique — logique pure testable (Livre I §2-3, Livre VI §25.3).
//
// RÈGLES CODEX (gelées) :
// - En combat, un personnage voit à 360° autour de lui : AUCUN cône frontal,
//   aucune direction de vision (le cap de l'avatar est ignoré).
// - La portée de vue est ENVIRONNEMENTALE, jamais issue des stats : courte
//   portée en intérieur / obstacles denses, longue portée par météo claire.
//   (FogOfWarManager.CurrentSightRange ; presets Courte 6 / Longue 12.)
// - Les stats de Vision / Ouïe ne servent qu'aux compétences : Observation,
//   Écoute, jets de détection — jamais à la portée du masque.
// - Murs (CoverType.Full, Livre VI §25.3) bloquent la vue : si le cône de visée
//   attaquant->cible est Full, la case est plongée dans l'obscurité.
//   Half / ThreeQuarters restent visibles (simple malus d'attaque, pas de masque).
// - Contact (distance <= 1) : toujours visible (cohérent avec
//   CoreRulesConfig.CoverIgnoredAtContactDistance).
// - Ennemi non détecté = invisible. Révélation par :
//   (a) entrée dans le champ d'au moins un avatar de l'escouade (automatique) ;
//   (b) jet d'Observation (opposé Observation vs Discretion, malus distance + couvert) ;
//   (c) tir bruyant (Ballistique / grenade) : révélé dans le rayon d'Ouïe (D6, 1..6)
//       jusqu'à la fin du round suivant, sauf derrière un mur Full.
//   « Détection Thermique » : détecte jusqu'à Portee/2 (arrondi sup.) même
//   derrière un Full.
// </copyright>
using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.Visibility
{
    /// <summary>Paramètres de vision d'un observateur (portée d'ambiance + sens).</summary>
    [Serializable]
    public struct FogVisionParams
    {
        public int Range;            // cases hex : portée ENVIRONNEMENTALE (preset room/MJ)
        public float HalfAngleDeg;   // Toujours 180 (360° : KT n'a pas de direction de vision)
        public bool Panoramic;       // Toujours vrai (conservé pour compatibilité)
        public bool Thermal;         // Détection Thermique
        public int HearingRange;     // Ouïe (1..6), pour la révélation au bruit
    }

    /// <summary>
    /// Cœur pur du brouillard de guerre : portée d'ambiance à 360° et occlusion
    /// par les murs Full / relief / gros accessoires (ligne de mire à un rayon,
    /// zéro allocation — les tirs gardent le CoverSystem exact à 28 rayons).
    /// La portée ne dépend JAMAIS des stats (Vision = jets de compétences).
    /// Aucun MonoBehaviour ici : déterministe et couvert par des tests NUnit.
    /// </summary>
    public static class FogOfWarSystem
    {
        /// <summary>Courte portée : intérieur, obstacles denses.</summary>
        public const int ShortSightRange = 6;
        /// <summary>Longue portée : extérieur dégagé, météo claire.</summary>
        public const int LongSightRange = 12;
        public const int NoiseRevealExtraRounds = 1;

        public const string SpecPanoramic = "Vigilance Réflexe : Perception Panoramique 360°";
        public const string SpecThermal = "Vigilance Réflexe : Détection Thermique";
        public const string SpecLynx = "Vigilance Réflexe : Oeil de Lynx";

        /// <summary>
        /// Résout les paramètres d'un observateur. baseRange = portée
        /// environnementale (FogOfWarManager.CurrentSightRange). Les stats ne
        /// fournissent que l'Ouïe et les sens (Thermique) ; la Vision sert aux
        /// jets d'Observation, jamais à la portée.
        /// </summary>
        public static FogVisionParams ResolveObserverParams(CharacterStats stats, int baseRange)
        {
            var p = new FogVisionParams
            {
                Range = Mathf.Clamp(baseRange, 1, 32),
                HalfAngleDeg = 180f,
                Panoramic = true,
                Thermal = false,
                HearingRange = 3,
            };
            if (stats == null) return p;
            p.HearingRange = Mathf.Clamp(stats.Attributes.Ouie, 1, 6);
            try
            {
                if (stats.HasSpecialization(SpecThermal))
                    p.Thermal = true;
            }
            catch { /* fiche partielle en tests */ }
            return p;
        }

        /// <summary>
        /// Helper mathématique pur (angle signé), conservé testé mais INUTILISÉ
        /// par la visibilité : KT voit à 360°, sans direction (Livre VI §25.4).
        /// </summary>
        public static bool IsInCone(Vector3 observerWorld, float observerYawDeg, Vector3 targetWorld, FogVisionParams p)
        {
            if (p.Panoramic) return true;
            Vector3 d = targetWorld - observerWorld;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return true; // même case : toujours vue.
            float targetYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(observerYawDeg, targetYaw);
            return Mathf.Abs(delta) <= p.HalfAngleDeg + 1e-3f;
        }

        /// <summary>
        /// Test complet : portée d'ambiance + mur Full. 360° : le
        /// cap (observerYawDeg) est ignoré — conservé en signature pour parité
        /// avec le hub VTT et une éventuelle règle de facing future.
        /// getNode : accès aux cases (peut retourner null hors carte = traversable).
        /// PERF : un seul rayon, zéro allocation — NE PAS appeler CoverSystem
        /// ici (6 coins × 28 rayons + HashSets par rayon = ~2 fps sur la carte).
        /// </summary>
        public static bool IsCellVisible(
            HexCoordinates observer,
            float observerYawDeg,
            HexCoordinates target,
            Func<HexCoordinates, HexNode> getNode,
            FogVisionParams p,
            float hexRadius = 1f)
        {
            if (observer.Equals(target)) return true;
            int dist = observer.DistanceTo(target);
            if (dist > p.Range) return false;
            // Contact : toujours visible (règle du couvert ignoré au contact).
            // 360° : aucun test de cap ici (KT n'a pas de direction de vision).
            if (dist <= 1) return true;
            // Thermique : voit jusqu'à Portee/2 même derrière un mur Full.
            int thermalSeeThrough = p.Thermal ? Mathf.Max(1, (p.Range + 1) / 2) : -1;
            bool ignoreWalls = dist <= thermalSeeThrough;
            return HasFogLineOfSight(observer, target, getNode, hexRadius, ignoreWalls);
        }

        /// <summary>
        /// Variante branchée sur la grille + yaw explicite.
        /// </summary>
        public static bool IsCellVisible(
            HexCoordinates observer,
            float observerYawDeg,
            HexCoordinates target,
            TacticalHexGrid grid,
            FogVisionParams p)
        {
            if (grid == null) return false;
            return IsCellVisible(observer, observerYawDeg, target, c => grid.GetNode(c), p, grid.HexRadius);
        }

        // Hauteurs du rayon de brouillard (Livre VI §25.3 : œil 1,5 m).
        private const float FogEyeHeight = 1.5f;
        private const float FogAimHeight = 0.9f;
        // Un accessoire moins haut que ça au-dessus du sol ne bouche jamais la vue.
        private const float FogTallPropMinHeight = 1.2f;

        /// <summary>
        /// Ligne de mire « brouillard » : UN seul rayon œil (1,5 m) ➔ buste cible
        /// (sol + 0,9 m), échantillonné fin en espace pixel. Bloqué par : case
        /// intermédiaire marquée Full, relief avalant le rayon, accessoire haut
        /// (≥ 1,2 m, ex. pilier/mur — les caisses basses et les suspendus qui
        /// passent au-dessus du rayon laissent voir). Extrémités exclues (on voit
        /// depuis/vers une case de mur ; le contact est traité par l'appelant).
        /// ZÉRO allocation (pas de HashSet/Liste) : cases consécutives identiques
        /// sautées par comparaison au précédent (échantillons ordonnés).
        /// Approximation volontaire : les tirs gardent le CoverSystem exact
        /// (28 rayons) ; le masque n'a besoin que du mur/relief franc.
        /// </summary>
        public static bool HasFogLineOfSight(
            HexCoordinates observer,
            HexCoordinates target,
            Func<HexCoordinates, HexNode> getNode,
            float hexRadius = 1f,
            bool ignoreWalls = false)
        {
            if (getNode == null) return true;
            float r = Mathf.Max(0.05f, hexRadius);
            Vector3 oW = observer.ToWorldPosition(r, 0f);
            Vector3 tW = target.ToWorldPosition(r, 0f);

            HexNode obsNode = null, tgtNode = null;
            try { obsNode = getNode(observer); } catch { /* ignore */ }
            try { tgtNode = getNode(target); } catch { /* ignore */ }
            float groundO = obsNode != null ? obsNode.WorldPosition.y : 0f;
            float groundT = tgtNode != null ? tgtNode.WorldPosition.y : 0f;
            float eyeY = groundO + FogEyeHeight;
            float aimY = groundT + FogAimHeight;

            float dx = tW.x - oW.x;
            float dz = tW.z - oW.z;
            float segLen = Mathf.Sqrt(dx * dx + dz * dz);
            int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / (r * 0.25f)));

            HexCoordinates prev = observer;
            bool hasPrev = true; // l'observateur lui-même est exclu d'office.
            for (int i = 1; i < steps; i++)
            {
                float t = (float)i / steps;
                float x = oW.x + dx * t;
                float z = oW.z + dz * t;
                HexCoordinates cell = WorldXZToHex(x, z, r);
                if (cell.Equals(target)) continue; // case cible exclue.
                if (hasPrev && cell.Equals(prev)) continue; // doublon consécutif.
                prev = cell;
                hasPrev = true;

                HexNode node = null;
                try { node = getNode(cell); } catch { node = null; }
                float groundY = node != null ? node.WorldPosition.y : 0f;
                float rayY = eyeY + (aimY - eyeY) * t;

                // 1) Relief : le rayon ne passe jamais sous le terrain.
                if (groundY > rayY + 0.05f) return false;

                if (ignoreWalls) continue;

                // 2) Mur franc (marquage Full : mur, pilier, paroi blindée).
                if (node != null && node.Cover == CoverType.Full) return false;

                // 3) Accessoire 3D haut (pilier, mur prop, mobilier massif).
                // Les suspendus dont le bas passe au-dessus du rayon sont ignorés.
                if (PropObstacleRegistry.TryGet(cell, out PropObstacle prop))
                {
                    float topY = prop.HasMeshBounds ? prop.MeshBounds.max.y : prop.MaxY;
                    if (topY > rayY + 0.05f && topY - groundY >= FogTallPropMinHeight)
                    {
                        float bottomY = prop.HasMeshBounds ? prop.MeshBounds.min.y : prop.MinY;
                        if (bottomY <= rayY + 0.05f) return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Conversion monde (x,z) ➔ case, miroir de TacticalHexGrid.</summary>
        private static HexCoordinates WorldXZToHex(float x, float z, float hexRadius)
        {
            float qFrac = ((float)System.Math.Sqrt(3) / 3f * x - 1f / 3f * z) / hexRadius;
            float rFrac = (2f / 3f * z) / hexRadius;
            float sFrac = -qFrac - rFrac;

            int q = (int)System.Math.Round(qFrac);
            int rr = (int)System.Math.Round(rFrac);
            int s = (int)System.Math.Round(sFrac);

            float qDiff = System.Math.Abs(q - qFrac);
            float rDiff = System.Math.Abs(rr - rFrac);
            float sDiff = System.Math.Abs(s - sFrac);

            if (qDiff > rDiff && qDiff > sDiff) q = -rr - s;
            else if (rDiff > sDiff) rr = -q - s;
            return new HexCoordinates(q, rr);
        }

        /// <summary>
        /// Union des cases visibles par une escouade (un observateur = coords + cap + params).
        /// Borné au rayon Vision max : BFS en anneaux depuis chaque observateur.
        /// </summary>
        public static HashSet<HexCoordinates> ComputeSquadVisibleCells(
            IReadOnlyList<(HexCoordinates coords, float yawDeg, FogVisionParams vision)> observers,
            Func<HexCoordinates, HexNode> getNode,
            float hexRadius = 1f)
        {
            var visible = new HashSet<HexCoordinates>();
            if (observers == null || getNode == null) return visible;
            for (int i = 0; i < observers.Count; i++)
            {
                var (coords, yaw, p) = observers[i];
                // L'observateur voit toujours sa propre case.
                visible.Add(coords);
                var visited = new HashSet<HexCoordinates> { coords };
                var frontier = new Queue<HexCoordinates>();
                frontier.Enqueue(coords);
                int guard = 0;
                while (frontier.Count > 0 && guard++ < 4000)
                {
                    var cur = frontier.Dequeue();
                    int distOrigin = coords.DistanceTo(cur);
                    if (distOrigin >= p.Range) continue;
                    for (int dir = 0; dir < 6; dir++)
                    {
                        var nb = cur.GetNeighbor(dir);
                        if (!visited.Add(nb)) continue;
                        if (coords.DistanceTo(nb) > p.Range) continue;
                        // On traverse même les murs pour explorer l'anneau suivant
                        // (un mur masque mais ne raccourcit pas la portée théorique) ;
                        // le test exact tranche la visibilité case par case.
                        if (IsCellVisible(coords, yaw, nb, getNode, p, hexRadius))
                            visible.Add(nb);
                        frontier.Enqueue(nb);
                    }
                }
            }
            return visible;
        }

        /// <summary>
        /// Malus de distance + couvert pour le duel de détection
        /// (Observation vs Discretion). Thermique : +2 (voit les camouflés).
        /// </summary>
        public static int BuildDetectionModifier(int distance, CoverType cover, bool thermal)
        {
            int mod = -Math.Max(0, distance - 1); // -1 par case au-delà du contact.
            mod += cover switch
            {
                CoverType.Half => -1,
                CoverType.ThreeQuarters => -2,
                CoverType.Full => -4,
                _ => 0,
            };
            if (thermal) mod += 2;
            return mod;
        }

        /// <summary>
        /// Duel de détection : Observation (observateur, offensif) vs Discretion
        /// (cible, défensive). Le modificateur de portée/couvert s'applique à
        /// l'observateur. La progression organique (Livre I §5) est gérée par le
        /// calculateur sur réussite. Retourne le résultat opposé complet + log.
        /// </summary>
        public static OpposedCheckResult ResolveDetectionDuel(
            CombatCalculator calc,
            CharacterStats observer,
            CharacterStats target,
            int distance,
            CoverType cover)
        {
            if (calc == null) throw new ArgumentNullException(nameof(calc));
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            if (target == null) throw new ArgumentNullException(nameof(target));
            bool thermal = false;
            bool lynx = false;
            try
            {
                thermal = observer.HasSpecialization(SpecThermal);
                lynx = observer.HasSpecialization(SpecLynx);
            }
            catch { thermal = false; lynx = false; }
            // L'Œil de Lynx n'étend plus la portée (environnementale, §25.4) :
            // acuité dans l'obscurité = +2 au duel de détection (pur bonus skill).
            int mod = BuildDetectionModifier(distance, cover, thermal) + (lynx ? 2 : 0);
            // ResolveOpposedCheck ne prend pas de modificateur libre : duel à
            // mises nulles (aucun PA/PE engagé par le duel lui-même ; le coût
            // d'Observation est débité par l'appelant), puis ajustement direct
            // du différentiel par le malus portée/couvert. La progression
            // organique du calculateur reste acquise sur réussite.
            var raw = calc.ResolveOpposedCheck(observer, SkillType.Observation, target, SkillType.Discretion);
            if (raw == null) return null;
            if (mod != 0)
            {
                raw = new OpposedCheckResult
                {
                    Attacker = raw.Attacker,
                    Defender = raw.Defender,
                    AttackSkill = raw.AttackSkill,
                    DefenseSkill = raw.DefenseSkill,
                    AttackRoll = raw.AttackRoll,
                    DefenseRoll = raw.DefenseRoll,
                    AttackerBonusPAApplied = raw.AttackerBonusPAApplied,
                    AttackerPEApplied = raw.AttackerPEApplied,
                    DefenderBonusPAApplied = raw.DefenderBonusPAApplied,
                    DefenderPEApplied = raw.DefenderPEApplied,
                    Differential = raw.Differential + mod,
                    AttackerWins = (raw.Differential + mod) >= 0,
                    CombatLog = raw.CombatLog + $"\n   👁️ Détection : modificateur portée/couvert {mod:+0;-0} (dist {distance}, couvert {cover}) ➔ différentiel ajusté {(raw.Differential + mod >= 0 ? "+" : "")}{raw.Differential + mod}.",
                };
            }
            return raw;
        }

        /// <summary>
        /// Rayon de révélation au bruit : Ouïe du tireur (1..6 cases).
        /// Un tir bruyant révèle le tireur aux ennemis dont la distance hex
        /// au tireur est &lt;= Ouïe, sauf mur Full entre les deux (sauf Thermique
        /// côté détecteur, géré par l'appelant via IsCellVisible).
        /// </summary>
        public static int NoiseRevealRadius(CharacterStats shooter)
        {
            if (shooter == null) return 3;
            return Mathf.Clamp(shooter.Attributes.Ouie, 1, 6);
        }
    }
}
