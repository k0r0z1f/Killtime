using System;
using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Ères historiques / technologiques des grenades (Livre VIII §31.3 étendu).
    /// Sert au filtre du marché + au fluff + aux placeholders 3D.
    /// </summary>
    public enum GrenadeEra
    {
        PoudreNoire,    // XVe-XVIIIe : pot à feu, grenade cloutée
        GrandeGuerre,   // 1914-1918 : Mills, Stielhandgranate M1915
        SecondeGuerre,  // 1939-1945 : Mk2 Pineapple, F1, M24
        GuerreFroide,   // 1947-1991 : M67, RGD-5, M18 fumigène
        Moderne,        // 1992-2150 : M84 flash, AN-M14, thermobarique
        Futuriste,      // 2150+ : plasma industriel, cryo, IEM
        Arcanotech      // Nytharite impériale : graviton, stase, chant
    }

    /// <summary>Familles d'effet de grenade (dégâts + statuts Livre VII).</summary>
    public enum GrenadeEffectKind
    {
        Fragmentation,
        Explosive,
        Incendiaire,
        Fumigene,
        Flash,
        Gaz,
        EMP,
        Plasma,
        Cryo,
        Thermobarique,
        Graviton,
        Exercice
    }

    /// <summary>Modes de propulsion (main nue vs lanceurs).</summary>
    public enum GrenadeLauncherKind
    {
        Main,                       // Lancer à la main (1H, 8 cases)
        LanceGrenadeLeger,          // M79, coup par coup (20 cases)
        LanceGrenadeSousCanon,      // M203 sous fusil (18 cases)
        LanceGrenadeMulti,          // Milkor MGL, barillet 6 coups (16 cases, tir rapide)
        MortierLeger,               // 60mm d'escouade (20 cases, zone large)
        LancePlasmaArcanotech       // Thumper nytharite (20 cases, +dégâts énergie)
    }

    /// <summary>
    /// Règles tactiques des grenades (Livre VI §24 + Livre VIII §31.3).
    /// - Main : 2 PA, 8 cases. Lanceur : 3 PA, +bonus portée/précision.
    /// - Visée compensée : +1 PA, annule le malus de distance (-1 / 3 cases au-delà de 4).
    /// - Échec du jet Ballistique (SD 10) => dispersion 1-3 cases (réduite par lanceur/précision).
    /// - Zone : 100% à l'épicentre, -25% par case (min 25%). Shrapnels : flat bonus à distance &gt; 0.
    /// - Couverture : Half -2, Full -4 (+ bloque les shrapnels si Full).
    /// </summary>
    public static class GrenadeRules
    {
        public const int HandRangeTiles = 8;
        public const int HandThrowAPCost = 2;
        public const int LauncherShotAPCost = 3;
        public const int AimBonusAPCost = 1;
        public const int StandardThrowDC = 10;
        public const int MaxScatterTiles = 3;
        public const float FalloffPerTile = 0.25f;
        public const float MinFalloffFactor = 0.25f;
        public const int HalfCoverReduction = 2;
        public const int FullCoverReduction = 4;

        public static int ComputeMaxRange(InventoryItem grenade, InventoryItem launcher)
        {
            int hand = HandRangeTiles;
            if (grenade != null && grenade.RangeInTiles > 0)
                hand = Math.Min(grenade.RangeInTiles, HandRangeTiles);
            if (launcher != null && launcher.IsLauncher)
                return Math.Min(20, hand + Math.Max(0, launcher.LauncherRangeBonus));
            return hand;
        }

        public static int ComputeAPCost(InventoryItem launcher, bool aimed)
        {
            int baseCost = (launcher != null && launcher.IsLauncher) ? LauncherShotAPCost : HandThrowAPCost;
            return baseCost + (aimed ? AimBonusAPCost : 0);
        }

        /// <summary>Malus de distance : -1 par tranche de 3 cases au-delà de 4 (annulé par visée).</summary>
        public static int DistancePenalty(int distanceTiles, bool aimed)
        {
            if (aimed) return 0;
            if (distanceTiles <= 4) return 0;
            return -((distanceTiles - 4 + 2) / 3);
        }

        public static string EraLabel(GrenadeEra era)
        {
            return era switch
            {
                GrenadeEra.PoudreNoire => "Poudre Noire",
                GrenadeEra.GrandeGuerre => "Grande Guerre",
                GrenadeEra.SecondeGuerre => "Seconde Guerre",
                GrenadeEra.GuerreFroide => "Guerre Froide",
                GrenadeEra.Moderne => "Moderne",
                GrenadeEra.Futuriste => "Futuriste",
                GrenadeEra.Arcanotech => "Arcanotech",
                _ => "Inconnu"
            };
        }

        public static bool TryParseEra(string s, out GrenadeEra era)
        {
            era = GrenadeEra.Moderne;
            if (string.IsNullOrEmpty(s)) return false;
            string t = s.Trim().ToLowerInvariant();
            if (t.Contains("poudre")) { era = GrenadeEra.PoudreNoire; return true; }
            if (t.Contains("grande")) { era = GrenadeEra.GrandeGuerre; return true; }
            if (t.Contains("seconde") || t.Contains("ww2") || t.Contains("2nde")) { era = GrenadeEra.SecondeGuerre; return true; }
            if (t.Contains("froide") || t.Contains("cold")) { era = GrenadeEra.GuerreFroide; return true; }
            if (t.Contains("futur")) { era = GrenadeEra.Futuriste; return true; }
            if (t.Contains("arcano") || t.Contains("nyth") || t.Contains("imperial")) { era = GrenadeEra.Arcanotech; return true; }
            if (t.Contains("modern")) { era = GrenadeEra.Moderne; return true; }
            return false;
        }

        public static bool TryParseKind(string s, out GrenadeEffectKind kind)
        {
            kind = GrenadeEffectKind.Fragmentation;
            if (string.IsNullOrEmpty(s)) return false;
            string t = s.Trim().ToLowerInvariant();
            if (t.Contains("frag")) { kind = GrenadeEffectKind.Fragmentation; return true; }
            if (t.Contains("explo")) { kind = GrenadeEffectKind.Explosive; return true; }
            if (t.Contains("incend") || t.Contains("thermite")) { kind = GrenadeEffectKind.Incendiaire; return true; }
            if (t.Contains("fumi") || t.Contains("smoke")) { kind = GrenadeEffectKind.Fumigene; return true; }
            if (t.Contains("flash") || t.Contains("aveugl") || t.Contains("stun")) { kind = GrenadeEffectKind.Flash; return true; }
            if (t.Contains("gaz") || t.Contains("gas")) { kind = GrenadeEffectKind.Gaz; return true; }
            if (t.Contains("emp") || t.Contains("iem") || t.Contains("ion")) { kind = GrenadeEffectKind.EMP; return true; }
            if (t.Contains("plasma")) { kind = GrenadeEffectKind.Plasma; return true; }
            if (t.Contains("cryo") || t.Contains("gel")) { kind = GrenadeEffectKind.Cryo; return true; }
            if (t.Contains("thermobar")) { kind = GrenadeEffectKind.Thermobarique; return true; }
            if (t.Contains("grav")) { kind = GrenadeEffectKind.Graviton; return true; }
            if (t.Contains("exercice") || t.Contains("inerte") || t.Contains("entrain")) { kind = GrenadeEffectKind.Exercice; return true; }
            return false;
        }
    }

    /// <summary>Résultat du jet de lancer (précision + dispersion).</summary>
    public struct GrenadeThrowOutcome
    {
        public bool IsOnTarget;
        public int ScatterDistance;   // 0 si sur cible, sinon 1-3
        public int ScatterDirIndex;   // 0-5 (direction hex), -1 si sur cible
        public int AttackTotal;
        public int TargetDC;
        public int DistancePenalty;
        public string LogFragment;
    }

    /// <summary>Dégâts subis par UNE cible dans la zone.</summary>
    public struct GrenadeHitResult
    {
        public string TargetName;
        public int DistanceFromBlast;
        public int RawDamage;
        public int CoverReduction;
        public int ArmorAbsorbed;
        public int FinalDamage;
        public bool ExceededEncaissement;
        public StatusEffect InflictedStatus;
        public FatalBlowResolution FatalResolution;
        public bool IsPrimaryTarget;
    }

    /// <summary>Résultat complet d'une détonation (toutes cibles + log).</summary>
    public struct GrenadeBlastResult
    {
        public bool ThrowSucceeded;
        public GrenadeThrowOutcome Throw;
        public List<GrenadeHitResult> Hits;
        public int DiceRolled;
        public int DiceTotal;
        public string CombatLog;
    }

    /// <summary>
    /// Calculateur pur (testable sans Unity) : précision, dispersion, dégâts de zone,
    /// shrapnels, couverture, armure, statuts Livre VII. L'arène s'occupe du visuel/sons.
    /// </summary>
    public class GrenadeCalculator
    {
        private readonly DiceRoller _dice;
        private readonly Random _random;

        public GrenadeCalculator(DiceRoller dice = null, int? seed = null)
        {
            _dice = dice ?? new DiceRoller(seed);
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>
        /// Jet de lancer : Ballistique (dé de compétence + états) vs SD 10 + malus distance.
        /// Échec => dispersion 1-3 cases, réduite par AccuracyBonus / lanceur.
        /// Critique attaquant => toujours sur cible. Échec critique => dispersion max + 1.
        /// </summary>
        public GrenadeThrowOutcome ResolveThrow(
            CharacterStats attacker,
            int distanceTiles,
            InventoryItem grenade,
            InventoryItem launcher,
            int attackerBonusPA = 0,
            bool aimed = false)
        {
            DiceType die = attacker.GetSkillDie(SkillType.Ballistique, true);
            int statusMod = attacker.GetStatusModifier(SkillType.Ballistique, isOffensive: true);
            int distMalus = GrenadeRules.DistancePenalty(distanceTiles, aimed);
            int accBonus = (grenade?.AccuracyBonus ?? 0) + (launcher?.AccuracyBonus ?? 0);
            int mod = statusMod + distMalus + accBonus;

            var roll = _dice.Roll(die, mod, GrenadeRules.StandardThrowDC);
            int finalTotal = roll.Total + Math.Max(0, attackerBonusPA);

            bool onTarget = roll.IsCriticalSuccess || finalTotal >= GrenadeRules.StandardThrowDC;
            int scatter = 0;
            int dir = -1;
            if (!onTarget)
            {
                int margin = GrenadeRules.StandardThrowDC - finalTotal;
                scatter = Math.Min(GrenadeRules.MaxScatterTiles, 1 + margin / 3);
                // Précision matérielle + lanceur : -1 case de dispersion (min 1).
                int reduction = 0;
                if (accBonus > 0) reduction += 1;
                if (launcher != null && launcher.IsLauncher) reduction += 1;
                scatter = Math.Max(1, scatter - reduction);
                if (roll.IsCriticalFailure) scatter = Math.Min(GrenadeRules.MaxScatterTiles, scatter + 1);
                dir = _random.Next(0, 6);
            }

            string launcherTag = (launcher != null && launcher.IsLauncher) ? $" via {launcher.Name}" : " à la main";
            string log = roll.IsCriticalSuccess
                ? $"Lancer critique{launcherTag} : {die} [{roll.RawRoll}+{mod}+PA{Math.Max(0, attackerBonusPA)}={finalTotal}] ➔ PILE sur l'objectif !"
                : (onTarget
                    ? $"Lancer réussi{launcherTag} : {die} [{roll.RawRoll}+{mod}+PA{Math.Max(0, attackerBonusPA)}={finalTotal} vs SD10] ➔ sur cible (malus dist {distMalus})."
                    : $"Lancer manqué{launcherTag} : {die} [{roll.RawRoll}+{mod}+PA{Math.Max(0, attackerBonusPA)}={finalTotal} vs SD10] ➔ dispersion {scatter} case(s) dir {dir} (malus dist {distMalus}).");

            return new GrenadeThrowOutcome
            {
                IsOnTarget = onTarget,
                ScatterDistance = scatter,
                ScatterDirIndex = dir,
                AttackTotal = finalTotal,
                TargetDC = GrenadeRules.StandardThrowDC,
                DistancePenalty = distMalus,
                LogFragment = log
            };
        }

        /// <summary>
        /// Lance Nd10 (dés de souffle). Retourne le total + le détail pour le log.
        /// </summary>
        public int RollBlastDice(int diceCount, out int diceTotal, out string detail)
        {
            diceTotal = 0;
            detail = "0";
            if (diceCount <= 0) return 0;
            var parts = new List<string>(diceCount);
            for (int i = 0; i < diceCount; i++)
            {
                var r = _dice.Roll(DiceType.D10, 0, 10);
                diceTotal += r.RawRoll;
                parts.Add(r.RawRoll.ToString());
            }
            detail = string.Join("+", parts);
            return diceTotal;
        }

        /// <summary>
        /// Dégâts bruts à une distance donnée : (flat + Nd10) x falloff + shrapnels (si dist &gt; 0).
        /// Flash/fumigène/gaz : dégâts réduits mais statuts garantis (gérés par l'arène).
        /// </summary>
        public int ComputeRawDamage(InventoryItem grenade, int diceTotal, int distanceFromBlast)
        {
            if (grenade == null) return 0;
            int flat = Math.Max(0, grenade.BaseDamage);
            float factor = 1f - GrenadeRules.FalloffPerTile * distanceFromBlast;
            factor = Math.Max(GrenadeRules.MinFalloffFactor, factor);
            // Les utilitaires (flash/fumi) ne font presque aucun dégât au-delà de l'épicentre.
            GrenadeRules.TryParseKind(grenade.GrenadeKind, out var kind);
            if ((kind == GrenadeEffectKind.Flash || kind == GrenadeEffectKind.Fumigene) && distanceFromBlast > 0)
                factor = Math.Min(factor, 0.25f);
            int scaled = (int)Math.Floor((flat + diceTotal) * factor);
            int shrap = (distanceFromBlast > 0 && grenade.ShrapnelDamage > 0) ? grenade.ShrapnelDamage : 0;
            // Couverture Full bloque les éclats (le souffle passe, pas les shrapnels).
            // Appliqué par l'arène via coverFull flag — ici on laisse le flat, l'arène retranche.
            return Math.Max(0, scaled + shrap);
        }

        /// <summary>
        /// Applique armure + couverture + seuils Livre VII à UNE cible. Modifie ses PV/statuts.
        /// coverLevel : 0=None, 1=Half, 2=Full.
        /// </summary>
        public GrenadeHitResult ResolveHitOnTarget(
            CharacterStats target,
            InventoryItem grenade,
            int diceTotal,
            int distanceFromBlast,
            int coverLevel,
            bool isPrimary)
        {
            int raw = ComputeRawDamage(grenade, diceTotal, distanceFromBlast);
            int coverRed = coverLevel >= 2 ? GrenadeRules.FullCoverReduction : (coverLevel == 1 ? GrenadeRules.HalfCoverReduction : 0);
            // Full cover bloque les shrapnels : on les retire avant armure.
            if (coverLevel >= 2 && grenade != null && grenade.ShrapnelDamage > 0 && distanceFromBlast > 0)
                raw = Math.Max(0, raw - grenade.ShrapnelDamage);
            int afterCover = Math.Max(0, raw - coverRed);

            int totalArmor = target.BaseArmorAbsorption;
            int absorbed = Math.Min(totalArmor, afterCover);
            int final = Math.Max(0, afterCover - absorbed);

            StatusEffect inflicted = StatusEffect.None;
            bool exceeded = final > target.EncaissementThreshold;
            if (grenade != null && !string.IsNullOrEmpty(grenade.GrenadeStatuses) && distanceFromBlast <= Math.Max(1, grenade.BlastRadius))
            {
                inflicted = ParseStatuses(grenade.GrenadeStatuses);
                // Les utilitaires aveuglent / enfument même sans dépasser l'encaissement.
                GrenadeRules.TryParseKind(grenade.GrenadeKind, out var k);
                bool forceStatus = (k == GrenadeEffectKind.Flash || k == GrenadeEffectKind.Fumigene
                    || k == GrenadeEffectKind.Gaz || k == GrenadeEffectKind.Incendiaire
                    || k == GrenadeEffectKind.Cryo || k == GrenadeEffectKind.EMP);
                if (exceeded || forceStatus || final > 0)
                {
                    target.ActiveStatus |= inflicted;
                }
                else inflicted = StatusEffect.None;
            }
            else if (exceeded)
            {
                inflicted = StatusEffect.Destabilise;
                target.ActiveStatus |= inflicted;
            }

            FatalBlowResolution fatal = FatalBlowResolution.None;
            if (final > 0)
            {
                if (target.CurrentHealth - final <= 0)
                    fatal = target.EvaluateFatalBlow(BodyPart.Torse, final);
                else
                    target.CurrentHealth -= final;
            }

            return new GrenadeHitResult
            {
                TargetName = target.Name,
                DistanceFromBlast = distanceFromBlast,
                RawDamage = raw,
                CoverReduction = coverRed,
                ArmorAbsorbed = absorbed,
                FinalDamage = final,
                ExceededEncaissement = exceeded,
                InflictedStatus = inflicted,
                FatalResolution = fatal,
                IsPrimaryTarget = isPrimary
            };
        }

        public static StatusEffect ParseStatuses(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return StatusEffect.None;
            StatusEffect acc = StatusEffect.None;
            string[] parts = csv.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string t = parts[i].Trim().ToLowerInvariant();
                if (t.Contains("destab")) acc |= StatusEffect.Destabilise;
                else if (t.Contains("etourdi")) acc |= StatusEffect.Etourdi;
                else if (t.Contains("immobil")) acc |= StatusEffect.Immobilise;
                else if (t.Contains("paralys")) acc |= StatusEffect.Paralyse;
                else if (t.Contains("sonn")) acc |= StatusEffect.Sonne;
                else if (t.Contains("terre")) acc |= StatusEffect.ATerre;
                else if (t.Contains("ralenti")) acc |= StatusEffect.Ralenti;
                else if (t.Contains("agon")) acc |= StatusEffect.Agonisant;
                else if (t.Contains("inconsc")) acc |= StatusEffect.Inconscient;
                else if (t.Contains("aveugle")) acc |= StatusEffect.Aveugle;
                else if (t.Contains("sourd")) acc |= StatusEffect.Sourd;
                else if (t.Contains("asphyx")) acc |= StatusEffect.Asphyxie;
                else if (t.Contains("empois")) acc |= StatusEffect.Empoisonne;
                else if (t.Contains("feu") || t.Contains("brul")) acc |= StatusEffect.EnFeu;
                else if (t.Contains("saign")) acc |= StatusEffect.Saignement;
                else if (t.Contains("chrono")) acc |= StatusEffect.ChronoFracture;
            }
            return acc;
        }

        public static string StatusesToLabel(StatusEffect fx)
        {
            if (fx == StatusEffect.None) return "—";
            var names = new List<string>();
            foreach (StatusEffect flag in Enum.GetValues(typeof(StatusEffect)))
            {
                if (flag == StatusEffect.None) continue;
                if (fx.HasFlag(flag)) names.Add(flag.ToString());
            }
            return string.Join("+", names);
        }
    }
}
