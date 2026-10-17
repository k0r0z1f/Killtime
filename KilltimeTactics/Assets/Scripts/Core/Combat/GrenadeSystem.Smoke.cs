using System;
using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.Tactics.Grid;

namespace Killtime.Core.Combat
{
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
}
