using System.Collections.Generic;
using Killtime.Tactics.Grid;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// RD-188 : les 6 zones persistantes posables par nom en arène (dev-only,
    /// sans lancer de grenade). Profils miroirs d'ArmoryCatalog (l.601-615) :
    /// Kind/Radius/Turns/Statuses/Era verbatim pour durées et overlays vérifiés.
    /// Pur et testable sans Unity (EditMode). Aucune règle modifiée.
    /// Couvre RD-173/176 (tirs de zone + zones persistantes).
    /// </summary>
    public readonly struct NamedZoneProfile
    {
        public readonly string Key;
        public readonly string DisplayName;
        public readonly string Kind;
        public readonly int Radius;
        public readonly int Turns;
        public readonly string StatusesCsv;
        public readonly string Era;

        public NamedZoneProfile(string key, string displayName, string kind, int radius, int turns, string statusesCsv, string era)
        {
            Key = key;
            DisplayName = displayName;
            Kind = kind;
            Radius = radius;
            Turns = turns;
            StatusesCsv = statusesCsv;
            Era = era;
        }
    }

    public static class NamedZoneProfiles
    {
        public static readonly IReadOnlyList<NamedZoneProfile> All = new List<NamedZoneProfile>
        {
            new NamedZoneProfile("M18", "M18 Fumigène", "Fumigene", 2, 2, "Aveugle", "Guerre Froide"),
            new NamedZoneProfile("ANM14", "AN-M14 Incendiaire", "Incendiaire", 1, 2, "EnFeu", "Moderne"),
            new NamedZoneProfile("CM6", "Lacrymogène CM-6", "Gaz", 2, 2, "Aveugle,Asphyxie,Etourdi", "Moderne"),
            new NamedZoneProfile("C9", "Cryo C-9", "Cryo", 2, 1, "Ralenti,Immobilise", "Futuriste"),
            new NamedZoneProfile("T0", "Stase Chrono T-0", "Stase", 2, 2, "Paralyse,ChronoFracture", "Arcanotech"),
            new NamedZoneProfile("KARK", "Pestilentielle Karkjiue", "Pestilentielle", 3, 3, "Empoisonne,Saignement", "Arcanotech"),
        };

        public static bool TryGet(string key, out NamedZoneProfile profile)
        {
            if (!string.IsNullOrEmpty(key))
            {
                for (int i = 0; i < All.Count; i++)
                {
                    if (string.Equals(All[i].Key, key.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    {
                        profile = All[i];
                        return true;
                    }
                }
            }
            profile = default;
            return false;
        }

        public static SmokeZone BuildSmokeZone(NamedZoneProfile profile, HexCoordinates center)
        {
            return new SmokeZone
            {
                Center = center,
                Radius = profile.Radius,
                Kind = profile.Kind ?? "",
                Statuses = GrenadeCalculator.ParseStatuses(profile.StatusesCsv ?? ""),
                TurnsLeft = System.Math.Max(1, profile.Turns),
                Arcanotech = SmokeScreen.IsArcanotechEra(profile.Era),
            };
        }

        /// <summary>Purge sélective par Kind (match exact insensible à la casse). Retourne le nombre supprimé.</summary>
        public static int PurgeByKind(List<SmokeZone> zones, string kind)
        {
            if (zones == null || zones.Count == 0 || string.IsNullOrEmpty(kind)) return 0;
            string want = kind.Trim();
            int removed = 0;
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                if (string.Equals(zones[i].Kind ?? "", want, System.StringComparison.OrdinalIgnoreCase))
                {
                    zones.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }
    }
}
