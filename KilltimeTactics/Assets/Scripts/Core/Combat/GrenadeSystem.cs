using System;
using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.Tactics.Grid;

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
        Exercice,
        Stase
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
    /// - Couverture (souffle, Livre VI §25.3) : Half -2, ThreeQuarters -3, Full -4
    ///   (+ bloque les shrapnels si Full). Le mortier lobé ignore Half et ThreeQuarters.
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
        public const int ThreeQuartersCoverReduction = 3;
        public const int FullCoverReduction = 4;

        /// <summary>
        /// Réduction de souffle selon le couvert de la case occupée (Livre VI §25.3).
        /// Le mortier lobé ignore les couverts partiels (Half, ThreeQuarters), jamais Full.
        /// </summary>
        public static int CoverReductionFor(CoverType cover, bool isMortar)
        {
            return cover switch
            {
                CoverType.Half => isMortar ? 0 : HalfCoverReduction,
                CoverType.ThreeQuarters => isMortar ? 0 : ThreeQuartersCoverReduction,
                CoverType.Full => FullCoverReduction,
                _ => 0,
            };
        }

        /// <summary>Niveau int historique (0=None, 1=Half, 2=Full, 3=ThreeQuarters).</summary>
        public static int ToCoverLevel(CoverType cover)
        {
            return cover switch
            {
                CoverType.Half => 1,
                CoverType.Full => 2,
                CoverType.ThreeQuarters => 3,
                _ => 0,
            };
        }

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
            if (t.Contains("stase") || t.Contains("chrono")) { kind = GrenadeEffectKind.Stase; return true; }
            if (t.Contains("pestil")) { kind = GrenadeEffectKind.Gaz; return true; }
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
        public int ShieldAbsorbed;
        public bool Masked;
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
        /// coverLevel : 0=None, 1=Half, 2=Full, 3=ThreeQuarters (Livre VI §25.3).
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
            // Niveaux : 0=None, 1=Half (-2), 2=Full (-4 + bloque shrapnels), 3=ThreeQuarters (-3).
            int coverRed = coverLevel == 2 ? GrenadeRules.FullCoverReduction
                : coverLevel == 3 ? GrenadeRules.ThreeQuartersCoverReduction
                : (coverLevel == 1 ? GrenadeRules.HalfCoverReduction : 0);
            // Full cover bloque les shrapnels : on les retire avant armure.
            bool isFullCover = coverLevel == 2;
            if (isFullCover && grenade != null && grenade.ShrapnelDamage > 0 && distanceFromBlast > 0)
                raw = Math.Max(0, raw - grenade.ShrapnelDamage);
            int afterCover = Math.Max(0, raw - coverRed);

            // Champs de force portés (Livre VIII §32.2) : barrière avant armure/chair.
            // AbsorbShield retourne le RESTE : absorbé = avant - reste.
            int shieldRemainder = target.AbsorbShield(afterCover);
            int shieldAbs = Math.Max(0, afterCover - shieldRemainder);
            int afterShield = Math.Max(0, shieldRemainder);

            int totalArmor = target.BaseArmorAbsorption + target.GetWornArmorBonus();
            int absorbed = Math.Min(totalArmor, afterShield);
            int final = Math.Max(0, afterShield - absorbed);

            StatusEffect inflicted = StatusEffect.None;
            bool exceeded = final > target.EncaissementThreshold;
            bool masked = false;

            bool isGasGrenade = grenade != null && SmokeScreen.IsGas(grenade.GrenadeKind);
            bool isArcanoGas = isGasGrenade && SmokeScreen.IsArcanotechEra(grenade.Era);
            bool hasGasMask = isGasGrenade && SmokeScreen.HasGasMask(target);

            if (hasGasMask)
            {
                masked = true;
                final = SmokeScreen.HalveGasDamage(final, grenade.GrenadeKind, isArcanoGas);
                exceeded = final > target.EncaissementThreshold;
            }

            if (grenade != null && !string.IsNullOrEmpty(grenade.GrenadeStatuses) && distanceFromBlast <= Math.Max(1, grenade.BlastRadius))
            {
                inflicted = ParseStatuses(grenade.GrenadeStatuses);
                if (hasGasMask)
                {
                    inflicted = SmokeScreen.FilterGasStatuses(inflicted, grenade.GrenadeKind, isArcanoGas);
                }
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
                ShieldAbsorbed = shieldAbs,
                Masked = masked,
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

    /// <summary>
    /// Zone persistante laissée par une grenade à effet durable (RD-041/042/043).
    /// turnsLeft décompté à chaque round (0 = se dissipe). Struct pur, copié par valeur.
    /// </summary>
    [Serializable]
    public struct SmokeZone
    {
        public HexCoordinates Center;
        public int Radius;
        public string Kind;
        public StatusEffect Statuses;
        public int TurnsLeft;
        public bool Arcanotech;
    }

    /// <summary>
    /// Règles pures des zones persistantes + contre-équipement associé.
    /// Fumigène : bloque la visée (sauf thermique). Gaz : contourne l'armure.
    /// Masque Filtrant équipé : immunité Asphyxie/Empoisonne (gaz courants) + dégâts gaz /2.
    /// </summary>
    public static class SmokeScreen
    {
        public const string GasMaskName = "Masque";
        public const string GogglesName = "Jumelles";
        public const string LampName = "Lampe";
        public const string BatteryName = "Batterie";

        public static bool BlocksSight(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            string k = kind.Trim().ToLowerInvariant();
            return k.Contains("fumi") || k.Contains("smoke");
        }

        public static bool IsGas(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            string k = kind.Trim().ToLowerInvariant();
            return k.Contains("gaz") || k.Contains("gas") || k.Contains("pestil") || k.Contains("lacrymo");
        }

        /// <summary>Dégâts par tour dans la zone (0 = effets seuls). Gaz/feu : ignore l'armure.</summary>
        public static int ZoneDamagePerTurn(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return 0;
            string k = kind.Trim().ToLowerInvariant();
            if (k.Contains("plasma")) return 5;
            if (k.Contains("incend") || k.Contains("thermite") || k.Contains("thermobar") || k.Contains("feu")) return 4;
            if (k.Contains("pestil")) return 3;
            if (k.Contains("gaz") || k.Contains("gas") || k.Contains("lacrymo")) return 2;
            if (k.Contains("cryo") || k.Contains("gel")) return 2;
            if (k.Contains("stase") || k.Contains("chrono")) return 0;
            return 0;
        }

        public static bool ZoneIgnoresArmor(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            string k = kind.Trim().ToLowerInvariant();
            return k.Contains("gaz") || k.Contains("gas") || k.Contains("pestil") || k.Contains("lacrymo")
                || k.Contains("incend") || k.Contains("thermite") || k.Contains("thermobar") || k.Contains("feu")
                || k.Contains("plasma") || k.Contains("cryo") || k.Contains("gel");
        }

        /// <summary>Couleur d'overlay au sol pour chaque famille de zone persistante.</summary>
        public static UnityEngine.Color ZoneThemeColor(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return new UnityEngine.Color(0.85f, 0.85f, 0.88f, 0.50f);
            string k = kind.Trim().ToLowerInvariant();
            if (k.Contains("pestil")) return new UnityEngine.Color(0.35f, 0.65f, 0.25f, 0.60f);
            if (k.Contains("incend") || k.Contains("thermite") || k.Contains("feu")) return new UnityEngine.Color(1.0f, 0.35f, 0.05f, 0.55f);
            if (k.Contains("cryo") || k.Contains("gel")) return new UnityEngine.Color(0.20f, 0.85f, 1.0f, 0.50f);
            if (k.Contains("stase") || k.Contains("chrono")) return new UnityEngine.Color(0.70f, 0.20f, 0.95f, 0.55f);
            if (k.Contains("gaz") || k.Contains("gas") || k.Contains("lacrymo")) return new UnityEngine.Color(0.75f, 0.85f, 0.20f, 0.50f);
            if (k.Contains("plasma")) return new UnityEngine.Color(0.15f, 0.65f, 1.0f, 0.60f);
            return new UnityEngine.Color(0.85f, 0.85f, 0.88f, 0.50f);
        }

        /// <summary>Masque Filtrant disponible sur soi (équipé ou en inventaire) ?</summary>
        public static bool HasGasMask(CharacterStats stats)
        {
            return HasItemByName(stats, GasMaskName);
        }

        public static bool HasGoggles(CharacterStats stats)
        {
            return HasItemByName(stats, GogglesName);
        }

        public static bool HasLamp(CharacterStats stats)
        {
            return HasItemByName(stats, LampName);
        }

        public static bool HasBattery(CharacterStats stats)
        {
            return HasItemByName(stats, BatteryName);
        }

        public static bool HasItemByName(CharacterStats stats, string fragment)
        {
            var inv = stats?.Sheet?.Inventory;
            if (inv == null || string.IsNullOrEmpty(fragment)) return false;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || string.IsNullOrEmpty(it.Name)) continue;
                if (it.Name.Contains(fragment)) return true;
            }
            return false;
        }

        public static InventoryItem FindItemByName(CharacterStats stats, string fragment)
        {
            var inv = stats?.Sheet?.Inventory;
            if (inv == null || string.IsNullOrEmpty(fragment)) return null;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || string.IsNullOrEmpty(it.Name)) continue;
                if (it.Name.Contains(fragment)) return it;
            }
            return null;
        }

        /// <summary>
        /// Filtre respiratoire étanche : retire Asphyxie, Aveugle lacrymal (visière hermétique)
        /// et Empoisonne/Etourdi (gaz courants conventionnels, pas l'arcanotech).
        /// </summary>
        public static StatusEffect FilterGasStatuses(StatusEffect inflicted, string kind, bool arcanotech)
        {
            if (!IsGas(kind)) return inflicted;
            inflicted &= ~StatusEffect.Asphyxie;
            inflicted &= ~StatusEffect.Aveugle;
            if (!arcanotech)
            {
                inflicted &= ~StatusEffect.Empoisonne;
                inflicted &= ~StatusEffect.Etourdi;
            }
            return inflicted;
        }

        /// <summary>Le masque neutralise les dégâts des gaz conventionnels et divise par deux les gaz arcanotech.</summary>
        public static int HalveGasDamage(int damage, string kind, bool arcanotech = false)
        {
            if (!IsGas(kind) || damage <= 0) return Math.Max(0, damage);
            return arcanotech ? Math.Max(1, damage / 2) : 0;
        }

        public static bool IsArcanotechEra(string era)
        {
            return !string.IsNullOrEmpty(era) && era.ToLowerInvariant().Contains("arcano");
        }

        /// <summary>La case est-elle couverte par au moins une zone ?</summary>
        public static bool IsCellSmoked(HexCoordinates cell, System.Collections.Generic.IReadOnlyList<SmokeZone> zones)
        {
            if (zones == null) return false;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (z.TurnsLeft <= 0) continue;
                if (!BlocksSight(z.Kind)) continue;
                if (cell.DistanceTo(z.Center) <= Math.Max(0, z.Radius)) return true;
            }
            return false;
        }

        /// <summary>
        /// Interpolation cubique (redblobgames) : toutes les cases du segment,
        /// extrémités incluses. Pur et testable.
        /// </summary>
        public static List<HexCoordinates> CellsOnLine(HexCoordinates a, HexCoordinates b)
        {
            int n = a.DistanceTo(b);
            var cells = new List<HexCoordinates>(Math.Max(1, n + 1));
            if (n <= 0)
            {
                cells.Add(a);
                return cells;
            }
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                double q = a.Q + (b.Q - a.Q) * t;
                double r = a.R + (b.R - a.R) * t;
                double s = -q - r;
                double rq = Math.Round(q), rr = Math.Round(r), rs = Math.Round(s);
                double dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - s);
                if (dq > dr && dq > ds) rq = -rr - rs;
                else if (dr > ds) rr = -rq - rs;
                cells.Add(new HexCoordinates((int)rq, (int)rr));
            }
            return cells;
        }

        /// <summary>Un écran de fumée coupe-t-il la ligne de mire (extrémités incluses) ?</summary>
        public static bool SegmentSmoked(HexCoordinates from, HexCoordinates to, System.Collections.Generic.IReadOnlyList<SmokeZone> zones)
        {
            if (zones == null || zones.Count == 0) return false;
            var cells = CellsOnLine(from, to);
            for (int i = 0; i < cells.Count; i++)
                if (IsCellSmoked(cells[i], zones)) return true;
            return false;
        }
    }

    /// <summary>
    /// RD-034 : Modes de tir d'armes à distance balistiques et à dispersion.
    /// </summary>
    public enum AreaFireMode
    {
        SingleShot,
        Burst,          // Rafale 4 cases couloir droit
        FullAuto,       // Plein auto 4 cases cône large
        ShotgunCone,    // Chevrotine 4 cases cône progressif
        Suppression     // Tir de barrage 5 cases cône étendu
    }

    /// <summary>
    /// Profil balistique pour un tir de zone / cône / rafale (RD-034).
    /// </summary>
    [Serializable]
    public class AreaFireProfile
    {
        public string WeaponName = "Arme";
        public AreaFireMode Mode = AreaFireMode.ShotgunCone;
        public int RangeTiles = 4;
        public int BaseDamage = 10;
        public int DamageDiceCount = 0;
        public int AmmoCost = 1;
        public float FalloffPerTile = 0.25f;
        public float MinFalloffFactor = 0.25f;
        public bool CausesKnockback = true;
        public int SpreadWidth = 1;
        public string InflictedStatuses = "Destabilise";
        public string PreferredAmmoName = "Cartouche";
    }

    /// <summary>
    /// Impact subi par une entité dans la zone balistique (RD-034).
    /// </summary>
    public struct AreaFireHitResult
    {
        public CharacterStats Target;
        public string TargetName;
        public HexCoordinates Cell;
        public int Distance;
        public bool IsAlly;
        public int RawDamage;
        public int FalloffDamage;
        public int CoverReduction;
        public int ShieldAbsorbed;
        public int ArmorAbsorbed;
        public int FinalDamage;
        public bool ExceededEncaissement;
        public StatusEffect InflictedStatus;
        public bool KnockedBack;
        public HexCoordinates KnockbackTargetCell;
        public FatalBlowResolution FatalResolution;
    }

    /// <summary>
    /// Bilan global d'un tir de cône, rafale ou suppression (RD-034).
    /// </summary>
    public struct AreaFireResult
    {
        public AreaFireMode Mode;
        public HexCoordinates Origin;
        public HexCoordinates AimedTarget;
        public HexCoordinates ActualTarget;
        public bool IsOnTarget;
        public int ScatterDistance;
        public int AmmoConsumed;
        public List<HexCoordinates> AffectedCells;
        public List<AreaFireHitResult> Hits;
        public List<AreaFireHitResult> FriendlyFireHits;
        public string CombatLog;
    }

    /// <summary>
    /// Règles pures de calcul géométrique et balistique pour les cônes, rafales et tirs automatiques (RD-034).
    /// </summary>
    public static class AreaFireRules
    {
        public static readonly (int q, int r)[] AxialDirections = new[]
        {
            (1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1)
        };

        public static int DefaultAmmoCost(AreaFireMode mode)
        {
            return mode switch
            {
                AreaFireMode.SingleShot => 1,
                AreaFireMode.ShotgunCone => 1,
                AreaFireMode.Burst => 3,
                AreaFireMode.FullAuto => 6,
                AreaFireMode.Suppression => 10,
                _ => 1
            };
        }

        public static float ComputeDistanceFalloff(int distance, AreaFireMode mode, float customFalloff = 0f, float customFloor = 0f)
        {
            if (distance <= 1) return 1.0f;

            float falloffPerTile = customFalloff > 0f ? customFalloff : mode switch
            {
                AreaFireMode.ShotgunCone => 0.25f,
                AreaFireMode.Burst => 0.15f,
                AreaFireMode.FullAuto => 0.10f,
                AreaFireMode.Suppression => 0.20f,
                _ => 0.20f
            };

            float minFloor = customFloor > 0f ? customFloor : mode switch
            {
                AreaFireMode.ShotgunCone => 0.25f,
                AreaFireMode.Burst => 0.40f,
                AreaFireMode.FullAuto => 0.50f,
                AreaFireMode.Suppression => 0.20f,
                _ => 0.25f
            };

            float factor = 1.0f - (distance - 1) * falloffPerTile;
            return Math.Max(minFloor, factor);
        }

        public static List<HexCoordinates> ComputeConeCells(HexCoordinates origin, HexCoordinates target, int range, int spreadWidth)
        {
            var result = new List<HexCoordinates>();
            if (range <= 0) return result;

            int distToTarget = origin.DistanceTo(target);
            if (distToTarget <= 0) return result;

            var seen = new HashSet<HexCoordinates>();

            for (int d = 1; d <= range; d++)
            {
                double t = (double)d / distToTarget;
                double q = origin.Q + (target.Q - origin.Q) * t;
                double r = origin.R + (target.R - origin.R) * t;
                double s = -q - r;
                double rq = Math.Round(q), rr = Math.Round(r), rs = Math.Round(s);
                double dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - s);
                if (dq > dr && dq > ds) rq = -rr - rs;
                else if (dr > ds) rr = -rq - rs;
                var centerAtD = new HexCoordinates((int)rq, (int)rr);

                int lateral = Math.Min(spreadWidth, d - 1);

                for (int lq = -lateral; lq <= lateral; lq++)
                {
                    for (int lr = -lateral; lr <= lateral; lr++)
                    {
                        var candidate = new HexCoordinates(centerAtD.Q + lq, centerAtD.R + lr);
                        if (centerAtD.DistanceTo(candidate) <= lateral && origin.DistanceTo(candidate) == d)
                        {
                            if (seen.Add(candidate))
                            {
                                result.Add(candidate);
                            }
                        }
                    }
                }
            }

            return result;
        }

        public static HexCoordinates ComputeKnockbackCell(HexCoordinates origin, HexCoordinates target)
        {
            int dist = origin.DistanceTo(target);
            if (dist <= 0) return target;
            double t = (double)(dist + 1) / dist;
            double q = origin.Q + (target.Q - origin.Q) * t;
            double r = origin.R + (target.R - origin.R) * t;
            double s = -q - r;
            double rq = Math.Round(q), rr = Math.Round(r), rs = Math.Round(s);
            double dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - s);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new HexCoordinates((int)rq, (int)rr);
        }

        public static HexCoordinates ComputeScatteredTarget(HexCoordinates origin, HexCoordinates target, int scatterDistance, Random random)
        {
            if (scatterDistance <= 0) return target;
            var rand = random ?? new Random();
            int dir = rand.Next(0, AxialDirections.Length);
            for (int i = 0; i < AxialDirections.Length; i++)
            {
                var (dq, dr) = AxialDirections[(dir + i) % AxialDirections.Length];
                var candidate = new HexCoordinates(target.Q + dq * scatterDistance, target.R + dr * scatterDistance);
                if (origin.DistanceTo(candidate) > 0)
                    return candidate;
            }
            return target;
        }

        public static int ConsumeAmmoFromInventory(CharacterStats attacker, int count, string preferredAmmoName = null)
        {
            if (attacker?.Sheet?.Inventory == null || count <= 0) return 0;
            int remaining = count;
            var inv = attacker.Sheet.Inventory;
            string frag = string.IsNullOrEmpty(preferredAmmoName) ? "Munition" : preferredAmmoName;

            for (int i = 0; i < inv.Count && remaining > 0; i++)
            {
                var it = inv[i];
                if (it == null) continue;
                string name = it.Name ?? "";
                if (name.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Cartouche", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Balle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int taken = Math.Min(remaining, Math.Max(0, it.Quantity));
                    it.Quantity -= taken;
                    remaining -= taken;
                }
            }
            return count - remaining;
        }
    }

    /// <summary>
    /// Calculateur d'aire d'effet balistique : résolution du jet, dispersion, friendly fire,
    /// falloff longue portée, seuils d'encaissement et refoulement cinétique (RD-034).
    /// </summary>
    public class AreaFireCalculator
    {
        private readonly DiceRoller _dice;
        private readonly Random _random;

        public AreaFireCalculator(DiceRoller dice = null, int? seed = null)
        {
            _dice = dice ?? new DiceRoller(seed);
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public AreaFireResult ResolveAreaFire(
            CharacterStats attacker,
            HexCoordinates originPos,
            HexCoordinates targetPos,
            AreaFireProfile profile,
            IReadOnlyList<(CharacterStats stats, HexCoordinates pos, bool isAlly)> combatantsOnField,
            int attackerBonusAP = 0,
            int attackerPE = 0,
            CoverType fieldCover = CoverType.None)
        {
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            profile ??= new AreaFireProfile();

            DiceType die = attacker.GetSkillDie(SkillType.Ballistique, true);
            int statusMod = attacker.GetStatusModifier(SkillType.Ballistique, isOffensive: true);
            int distToAimed = originPos.DistanceTo(targetPos);
            int distMalus = GrenadeRules.DistancePenalty(distToAimed, false);
            int mod = statusMod + distMalus;

            var roll = _dice.Roll(die, mod, GrenadeRules.StandardThrowDC);
            int finalTotal = roll.Total + Math.Max(0, attackerBonusAP) + Math.Max(0, attackerPE);
            bool onTarget = roll.IsCriticalSuccess || finalTotal >= GrenadeRules.StandardThrowDC;

            int scatter = 0;
            HexCoordinates actualTarget = targetPos;
            if (!onTarget)
            {
                int margin = GrenadeRules.StandardThrowDC - finalTotal;
                scatter = roll.IsCriticalFailure ? 2 : Math.Max(1, Math.Min(2, 1 + margin / 4));
                actualTarget = AreaFireRules.ComputeScatteredTarget(originPos, targetPos, scatter, _random);
            }

            int spreadWidth = profile.Mode switch
            {
                AreaFireMode.Burst => 0,
                AreaFireMode.ShotgunCone => profile.SpreadWidth > 0 ? profile.SpreadWidth : 1,
                AreaFireMode.FullAuto => Math.Max(1, profile.SpreadWidth),
                AreaFireMode.Suppression => Math.Max(2, profile.SpreadWidth),
                _ => 1
            };

            var affectedCells = AreaFireRules.ComputeConeCells(originPos, actualTarget, profile.RangeTiles, spreadWidth);
            var affectedSet = new HashSet<HexCoordinates>(affectedCells);

            int diceTotal = 0;
            if (profile.DamageDiceCount > 0)
            {
                for (int i = 0; i < profile.DamageDiceCount; i++)
                    diceTotal += _dice.Roll(DiceType.D10, 0, 10).RawRoll;
            }

            var hits = new List<AreaFireHitResult>();
            var friendlyHits = new List<AreaFireHitResult>();
            var logLines = new List<string>();

            string rollTag = onTarget
                ? $"<b>AJUSTÉ</b> (Jet {finalTotal} vs SD{GrenadeRules.StandardThrowDC})"
                : $"<b>DISPERSION</b> ({scatter} case(s), Jet {finalTotal} vs SD{GrenadeRules.StandardThrowDC})";
            logLines.Add($"🔥 <b>TIR DE ZONE [{profile.Mode}] — {profile.WeaponName}</b> par {attacker.Name} ➔ {rollTag}");
            logLines.Add($"   📐 Cône : {affectedCells.Count} cases couvertes (Portée {profile.RangeTiles}, Axe {originPos} ➔ {actualTarget}).");

            if (combatantsOnField != null)
            {
                for (int i = 0; i < combatantsOnField.Count; i++)
                {
                    var (target, cell, isAlly) = combatantsOnField[i];
                    if (target == null || !target.IsAlive) continue;
                    if (!affectedSet.Contains(cell)) continue;

                    int d = originPos.DistanceTo(cell);
                    float falloff = AreaFireRules.ComputeDistanceFalloff(d, profile.Mode, profile.FalloffPerTile, profile.MinFalloffFactor);
                    int rawDamage = profile.BaseDamage + diceTotal;
                    int scaledDamage = rawDamage > 0 ? Math.Max(1, (int)Math.Floor(rawDamage * falloff)) : 0;

                    int coverRed = GrenadeRules.CoverReductionFor(fieldCover, false);
                    int afterCover = Math.Max(0, scaledDamage - coverRed);

                    int shieldRemainder = target.AbsorbShield(afterCover);
                    int shieldAbs = Math.Max(0, afterCover - shieldRemainder);
                    int afterShield = Math.Max(0, shieldRemainder);

                    int totalArmor = target.BaseArmorAbsorption + target.GetWornArmorBonus();
                    int absorbed = Math.Min(totalArmor, afterShield);
                    int finalDamage = Math.Max(0, afterShield - absorbed);

                    bool exceeded = finalDamage > target.EncaissementThreshold;
                    StatusEffect inflicted = StatusEffect.None;
                    if (!string.IsNullOrEmpty(profile.InflictedStatuses))
                    {
                        var parsed = GrenadeCalculator.ParseStatuses(profile.InflictedStatuses);
                        if (exceeded || finalDamage > 0)
                        {
                            inflicted = parsed;
                            target.ActiveStatus |= inflicted;
                        }
                    }
                    else if (exceeded)
                    {
                        inflicted = StatusEffect.Destabilise;
                        target.ActiveStatus |= inflicted;
                    }

                    bool knockedBack = false;
                    HexCoordinates knockCell = cell;
                    if (profile.CausesKnockback && (finalDamage > 0 || exceeded || profile.Mode == AreaFireMode.Burst))
                    {
                        knockedBack = true;
                        knockCell = AreaFireRules.ComputeKnockbackCell(originPos, cell);
                    }

                    FatalBlowResolution fatal = FatalBlowResolution.None;
                    if (finalDamage > 0)
                    {
                        if (target.CurrentHealth - finalDamage <= 0)
                            fatal = target.EvaluateFatalBlow(BodyPart.Torse, finalDamage);
                        else
                            target.CurrentHealth -= finalDamage;
                    }

                    var hit = new AreaFireHitResult
                    {
                        Target = target,
                        TargetName = target.Name,
                        Cell = cell,
                        Distance = d,
                        IsAlly = isAlly,
                        RawDamage = rawDamage,
                        FalloffDamage = scaledDamage,
                        CoverReduction = coverRed,
                        ShieldAbsorbed = shieldAbs,
                        ArmorAbsorbed = absorbed,
                        FinalDamage = finalDamage,
                        ExceededEncaissement = exceeded,
                        InflictedStatus = inflicted,
                        KnockedBack = knockedBack,
                        KnockbackTargetCell = knockCell,
                        FatalResolution = fatal
                    };

                    hits.Add(hit);
                    if (isAlly) friendlyHits.Add(hit);

                    string allyTag = isAlly ? " <color=#FF3B30><b>[⚠️ TIR AMI]</b></color>" : "";
                    string knockTag = knockedBack ? $" ➔ <b>Refoulé</b> vers {knockCell}" : "";
                    string shockTag = exceeded ? $" | ⚡ Choc ({inflicted})" : "";
                    logLines.Add($"   🎯 Impact sur <b>{target.Name}</b>{allyTag} à {d} case(s) : {scaledDamage} brut (Falloff {(int)(falloff * 100)}%) &minus; Armure {absorbed} &minus; Bouclier {shieldAbs} = <b>{finalDamage} dégâts</b>{shockTag}{knockTag}");
                }
            }

            int ammoReq = profile.AmmoCost > 0 ? profile.AmmoCost : AreaFireRules.DefaultAmmoCost(profile.Mode);
            AreaFireRules.ConsumeAmmoFromInventory(attacker, ammoReq, profile.PreferredAmmoName);

            return new AreaFireResult
            {
                Mode = profile.Mode,
                Origin = originPos,
                AimedTarget = targetPos,
                ActualTarget = actualTarget,
                IsOnTarget = onTarget,
                ScatterDistance = scatter,
                AmmoConsumed = ammoReq,
                AffectedCells = affectedCells,
                Hits = hits,
                FriendlyFireHits = friendlyHits,
                CombatLog = string.Join("\n", logLines)
            };
        }
    }
}
