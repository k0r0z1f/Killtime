using System;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Sous-systèmes de véhicule (Livre V §22.3 — RD-051).
    /// Essentiels : la destruction d'UNE zone essentielle (PV à 0) détruit l'engin.
    /// Superficiels : perte fonctionnelle sans détruire l'engin.
    /// </summary>
    public enum VehicleSubsystem
    {
        // --- Zones essentielles ---
        Cockpit,
        Moteur,
        Cadre,
        Rails,
        // --- Zones superficielles ---
        Guidon,
        CanonsTourelles,
        Passagers,
        Queue,
        Roues
    }

    /// <summary>Paliers de gabarit (Livre V §22.3 — RD-051).</summary>
    public enum VehicleScale
    {
        Velo,       // 1-10 PV (vélo)
        Charrette,  // 11-50 PV (charrette)
        JetTank,    // 51-1000 PV (jet, tank)
        Croiseur,   // 1001-10000 PV (croiseur stellaire Aeschylus)
        Station     // 10001+ PV (stations orbitales, forteresses)
    }

    /// <summary>Bande de PV d'un gabarit (Min inclus, Max inclus ; Station = non borné).</summary>
    public struct VehicleHpBand
    {
        public int Min;
        public int Max;

        public VehicleHpBand(int min, int max)
        {
            Min = min;
            Max = max;
        }
    }

    /// <summary>Résultat pur d'un impact sur un sous-système.</summary>
    public struct VehicleHitResult
    {
        public VehicleSubsystem Subsystem;
        public bool IsEssentialHit;
        public int DamageDealt;
        public int PreviousHp;
        public int CurrentHp;
        /// <summary>Vrai si une zone ESSENTIELLE tombe à 0 → engin détruit.</summary>
        public bool Destroyed;
        /// <summary>Vrai si une zone SUPERFICIELLE tombe à 0 → fonction perdue, engin intact.</summary>
        public bool Disabled;
        public string LogFragment;
    }

    /// <summary>
    /// Mécaniques pures du combat de véhicules (Livre V §22.3 — RD-051).
    /// Aucune dépendance Unity/MonoBehaviour : 100% testable en EditMode.
    /// Pattern TitanBossMechanics : consts = défauts Codex, JSON via CoreRulesConfig.
    /// Cohérence RD-044 (DestructibleEnvironmentRules) : les paliers reprennent la
    /// même logique (profil PV + dureté par matériau), étendue aux engins mobiles.
    /// </summary>
    public static class VehicleMechanics
    {
        // --- Défauts Codex (overridables via CoreRulesConfig, cf. HpBandFor) ---
        public const int HeavyVsVehicleBonusDefault = 100;
        public const int EmbarkAPCostDefault = 2;

        public const int VeloMinHp = 1;
        public const int VeloMaxHp = 10;
        public const int CharretteMinHp = 11;
        public const int CharretteMaxHp = 50;
        public const int JetTankMinHp = 51;
        public const int JetTankMaxHp = 1000;
        public const int CroiseurMinHp = 1001;
        public const int CroiseurMaxHp = 10000;
        public const int StationMinHp = 10001;
        // Station : non bornée vers le haut (int.MaxValue).

        // --- Écrasement de piéton (choix d'implémentation RD-051) ---
        // Dégâts fixes par gabarit, sans jet : l'engin en mouvement écrase
        // automatiquement le piéton sur sa trajectoire (cohérent RD-052 :
        // piétinement Titan = dégâts bruts + À Terre, simplifié à 1 cible).
        // Knockback 1 case + À Terre systématiques si l'engin est mobile.
        public const int RunOverKnockbackTiles = 1;

        public static bool IsEssential(VehicleSubsystem subsystem)
        {
            switch (subsystem)
            {
                case VehicleSubsystem.Cockpit:
                case VehicleSubsystem.Moteur:
                case VehicleSubsystem.Cadre:
                case VehicleSubsystem.Rails:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Bande de PV d'un gabarit. Les Max sont configurables (CoreRulesConfig) ;
        /// à config égale le comportement est inchangé (défauts = consts).
        /// </summary>
        public static VehicleHpBand HpBandFor(VehicleScale scale)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            int veloMax = cfg != null ? Math.Max(VeloMinHp, cfg.VehicleHpVeloMax) : VeloMaxHp;
            int charretteMax = cfg != null ? Math.Max(veloMax + 1, cfg.VehicleHpCharretteMax) : CharretteMaxHp;
            int jetTankMax = cfg != null ? Math.Max(charretteMax + 1, cfg.VehicleHpJetTankMax) : JetTankMaxHp;
            int croiseurMax = cfg != null ? Math.Max(jetTankMax + 1, cfg.VehicleHpCroiseurMax) : CroiseurMaxHp;
            switch (scale)
            {
                case VehicleScale.Velo: return new VehicleHpBand(VeloMinHp, veloMax);
                case VehicleScale.Charrette: return new VehicleHpBand(veloMax + 1, charretteMax);
                case VehicleScale.JetTank: return new VehicleHpBand(charretteMax + 1, jetTankMax);
                case VehicleScale.Croiseur: return new VehicleHpBand(jetTankMax + 1, croiseurMax);
                default: return new VehicleHpBand(croiseurMax + 1, int.MaxValue);
            }
        }

        /// <summary>PV par défaut d'un sous-système neuf = Max du palier (choix RD-051).</summary>
        public static int DefaultHpFor(VehicleScale scale) => HpBandFor(scale).Max == int.MaxValue
            ? StationMinHp
            : HpBandFor(scale).Max;

        /// <summary>Clamp un PV dans le palier du gabarit.</summary>
        public static int ClampHp(VehicleScale scale, int hp)
        {
            var band = HpBandFor(scale);
            return Math.Max(band.Min, Math.Min(band.Max, hp));
        }

        // ------------------------------------------------------------------
        // Armes lourdes : +100 dégâts contre structures et véhicules
        // ------------------------------------------------------------------

        /// <summary>
        /// Bonus configuré (+100 par défaut). Paramètre explicite prioritaire,
        /// sinon CoreRulesConfig, sinon défaut Codex.
        /// </summary>
        public static int HeavyBonusFor(int? configured = null)
        {
            if (configured.HasValue) return Math.Max(0, configured.Value);
            var cfg = Rules.CoreRulesConfig.Instance;
            return cfg != null ? Math.Max(0, cfg.HeavyVsVehicleBonus) : HeavyVsVehicleBonusDefault;
        }

        /// <summary>
        /// Détection d'arme lourde (choix RD-051) : flag HeavyAmmo (ex. Sniper
        /// Lourd, Deglazer), gros calibre (BaseDamage >= 12 ou >= 3 dés de
        /// dégâts comme les charges lourdes GrenadeFX), ou nom/description
        /// explicite (lourd, canon, mortier, roquette, missile, lance-grenades).
        /// </summary>
        public static bool IsHeavyWeapon(Killtime.Core.Inventory.InventoryItem weapon)
        {
            if (weapon == null) return false;
            if (weapon.HeavyAmmo) return true;
            if (weapon.BaseDamage >= 12) return true;
            if (weapon.DamageDiceCount >= 3) return true;
            string desc = $"{weapon.Name} {weapon.Description} {weapon.Category}".ToLowerInvariant();
            return desc.Contains("lourd") || desc.Contains("heavy")
                || desc.Contains("canon") || desc.Contains("mortier")
                || desc.Contains("roquette") || desc.Contains("rocket")
                || desc.Contains("missile") || desc.Contains("lance-grenade")
                || desc.Contains("lance grenade") || desc.Contains("thermobarique");
        }

        /// <summary>Bonus +100 si arme lourde ET cible véhicule/structure, sinon 0.</summary>
        public static int ComputeHeavyBonus(bool isHeavyWeapon, bool targetIsVehicleOrStructure, int? configuredBonus = null)
        {
            if (!isHeavyWeapon || !targetIsVehicleOrStructure) return 0;
            return HeavyBonusFor(configuredBonus);
        }

        /// <summary>Dégâts totaux vs véhicule/structure (bruts + bonus lourdes).</summary>
        public static int ApplyHeavyDamage(int rawDamage, Killtime.Core.Inventory.InventoryItem weapon, bool targetIsVehicleOrStructure)
        {
            return Math.Max(0, rawDamage)
                + ComputeHeavyBonus(IsHeavyWeapon(weapon), targetIsVehicleOrStructure);
        }

        // ------------------------------------------------------------------
        // Impact sous-système
        // ------------------------------------------------------------------

        /// <summary>
        /// Applique des dégâts (bonus lourdes déjà inclus par l'appelant) aux PV
        /// courants d'un sous-système. Pur : ne touche aucun registre.
        /// </summary>
        public static VehicleHitResult ResolveSubsystemHit(
            VehicleSubsystem subsystem, int currentHp, int damage, string vehicleName = "Véhicule")
        {
            int dealt = Math.Max(0, damage);
            int before = Math.Max(0, currentHp);
            int after = Math.Max(0, before - dealt);
            bool essential = IsEssential(subsystem);
            bool destroyed = essential && after <= 0;
            bool disabled = !essential && after <= 0;
            string name = string.IsNullOrEmpty(vehicleName) ? "Véhicule" : vehicleName;
            string log = destroyed
                ? $"💥 <b>{name} DÉTRUIT</b> : {subsystem} essentiel pulvérisé ({before} → 0, −{dealt}) !"
                : disabled
                    ? $"⚠️ <b>{name}</b> : {subsystem} hors service ({before} → 0, −{dealt}) — fonction perdue, engin intact."
                    : $"🔧 <b>{name}</b> : {subsystem} touché ({before} → {after}, −{dealt}).";
            return new VehicleHitResult
            {
                Subsystem = subsystem,
                IsEssentialHit = essential,
                DamageDealt = dealt,
                PreviousHp = before,
                CurrentHp = after,
                Destroyed = destroyed,
                Disabled = disabled,
                LogFragment = log
            };
        }

        // ------------------------------------------------------------------
        // Écrasement de piéton
        // ------------------------------------------------------------------

        /// <summary>
        /// Dégâts d'écrasement par gabarit (choix RD-051 : fixes, sans jet,
        /// proportionnés au palier — un tank écrase, un vélo bouscule).
        /// </summary>
        public static int RunOverDamageFor(VehicleScale scale)
        {
            switch (scale)
            {
                case VehicleScale.Velo: return 2;
                case VehicleScale.Charrette: return 4;
                case VehicleScale.JetTank: return 8;
                case VehicleScale.Croiseur: return 15;
                default: return 25;
            }
        }
    }
}
