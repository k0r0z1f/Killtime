using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Fiche tactique d'un véhicule en jeu (PV par sous-système, équipage, épave).
    /// </summary>
    public sealed class VehicleRecord
    {
        public string Id;
        public VehicleScale Scale;
        public Dictionary<VehicleSubsystem, int> HpBySubsystem = new Dictionary<VehicleSubsystem, int>();
        public HashSet<CharacterStats> Crew = new HashSet<CharacterStats>();
        public bool Destroyed;
    }

    /// <summary>
    /// Registre des véhicules en combat (Livre VI §26.3 — RD-051).
    /// Pur C# testable (aucune dépendance Unity) : clés string + refs
    /// CharacterStats comme TitanBossState (RD-087). Purge par ClearCombatState.
    /// </summary>
    public static class VehicleState
    {
        private static readonly Dictionary<string, VehicleRecord> _vehicles = new Dictionary<string, VehicleRecord>();
        private static readonly Dictionary<CharacterStats, string> _embarkedIn = new Dictionary<CharacterStats, string>();

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        /// <summary>
        /// Enregistre un véhicule (PV par sous-système = Max du palier, clampé).
        /// hpOverride : PV unitaires alternatifs (ex. engin déjà endommagé).
        /// </summary>
        public static VehicleRecord Register(string vehicleId, VehicleScale scale, int? hpOverride = null)
        {
            if (string.IsNullOrEmpty(vehicleId)) return null;
            var rec = new VehicleRecord { Id = vehicleId, Scale = scale };
            int hp = hpOverride.HasValue
                ? VehicleMechanics.ClampHp(scale, hpOverride.Value)
                : VehicleMechanics.DefaultHpFor(scale);
            foreach (VehicleSubsystem sub in System.Enum.GetValues(typeof(VehicleSubsystem)))
                rec.HpBySubsystem[sub] = hp;
            _vehicles[vehicleId] = rec;
            return rec;
        }

        public static bool Unregister(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId)) return false;
            if (!_vehicles.TryGetValue(vehicleId, out var rec)) return false;
            foreach (var member in rec.Crew)
                _embarkedIn.Remove(member);
            _vehicles.Remove(vehicleId);
            return true;
        }

        public static VehicleRecord Get(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId)) return null;
            return _vehicles.TryGetValue(vehicleId, out var rec) ? rec : null;
        }

        public static int Count => _vehicles.Count;

        public static int GetHp(string vehicleId, VehicleSubsystem subsystem)
        {
            var rec = Get(vehicleId);
            if (rec == null) return 0;
            return rec.HpBySubsystem.TryGetValue(subsystem, out int hp) ? hp : 0;
        }

        public static bool IsDestroyed(string vehicleId)
        {
            var rec = Get(vehicleId);
            return rec != null && rec.Destroyed;
        }

        // ------------------------------------------------------------------
        // Dégâts sous-système (avec routage pur VehicleMechanics)
        // ------------------------------------------------------------------

        /// <summary>
        /// Inflige des dégâts à un sous-système (bonus lourdes déjà inclus par
        /// l'appelant via VehicleMechanics.ApplyHeavyDamage). Marque l'épave si
        /// une zone essentielle tombe à 0. Retourne faux si véhicule inconnu.
        /// </summary>
        public static bool DamageSubsystem(string vehicleId, VehicleSubsystem subsystem, int damage, out VehicleHitResult hit)
        {
            hit = default;
            var rec = Get(vehicleId);
            if (rec == null || rec.Destroyed) return false;
            int current = GetHp(vehicleId, subsystem);
            hit = VehicleMechanics.ResolveSubsystemHit(subsystem, current, damage, vehicleId);
            rec.HpBySubsystem[subsystem] = hit.CurrentHp;
            if (hit.Destroyed)
            {
                rec.Destroyed = true;
                // L'équipage survit à la destruction (choix RD-051 : pas de
                // létalité automatique — débarquement forcé, cohérent avec les
                // règles de souffle RD-044 qui blessent sans exécuter).
                foreach (var member in rec.Crew)
                    _embarkedIn.Remove(member);
                rec.Crew.Clear();
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Embarquement (2 PA) / débarquement
        // ------------------------------------------------------------------

        /// <summary>
        /// Embarquement : 2 PA (CoreRulesConfig.VehicleEmbarkAPCost), refusé sans
        /// PA, sur épave, ou si déjà à bord. Paie via ConsumeActionPoints.
        /// </summary>
        public static bool TryEmbark(CharacterStats unit, string vehicleId, out string log)
        {
            log = "";
            if (unit == null || !unit.IsAlive) { log = "⛔ Embarquement impossible : unité invalide ou hors de combat."; return false; }
            var rec = Get(vehicleId);
            if (rec == null) { log = $"⛔ Embarquement impossible : véhicule « {vehicleId} » inconnu."; return false; }
            if (rec.Destroyed) { log = $"⛔ Embarquement impossible : {vehicleId} est une épave."; return false; }
            if (_embarkedIn.ContainsKey(unit)) { log = $"⛔ {unit.Name} est déjà à bord de {_embarkedIn[unit]}."; return false; }
            int cost = Rules.CoreRulesConfig.Instance != null
                ? Rules.CoreRulesConfig.Instance.VehicleEmbarkAPCost
                : VehicleMechanics.EmbarkAPCostDefault;
            if (!unit.ConsumeActionPoints(cost))
            {
                log = $"⛔ Embarquement IMPOSSIBLE ({unit.Name} : {unit.CurrentActionPoints} PA, coût {cost}) — reste à quai.";
                return false;
            }
            rec.Crew.Add(unit);
            _embarkedIn[unit] = vehicleId;
            log = $"🚙 <b>Embarquement</b> : {unit.Name} monte à bord de {vehicleId} (−{cost} PA).";
            return true;
        }

        /// <summary>Débarquement (gratuit, 0 PA — choix RD-051 : descendre est réflexe).</summary>
        public static bool Disembark(CharacterStats unit, out string log)
        {
            log = "";
            if (unit == null) return false;
            if (!_embarkedIn.TryGetValue(unit, out string vehicleId)) { log = $"⛔ {unit.Name} n'est à bord d'aucun véhicule."; return false; }
            _embarkedIn.Remove(unit);
            var rec = Get(vehicleId);
            if (rec != null) rec.Crew.Remove(unit);
            log = $"🚶 <b>Débarquement</b> : {unit.Name} descend de {vehicleId} (0 PA).";
            return true;
        }

        public static bool IsEmbarked(CharacterStats unit) =>
            unit != null && _embarkedIn.ContainsKey(unit);

        public static string GetVehicleOf(CharacterStats unit) =>
            unit != null && _embarkedIn.TryGetValue(unit, out string id) ? id : null;

        // ------------------------------------------------------------------
        // Écrasement de piéton
        // ------------------------------------------------------------------

        /// <summary>
        /// Écrasement : si l'engin est mobile, dégâts fixes du gabarit +
        /// projection 1 case + À Terre (cohérent RD-052 piétinement). À l'arrêt :
        /// simple contact, aucun dégât (choix RD-051). Les embarqués sont
        /// immunisés (à bord, pas sur la trajectoire).
        /// </summary>
        public static bool TryRunOver(string vehicleId, CharacterStats pedestrian, bool vehicleMoving, out string log)
        {
            log = "";
            var rec = Get(vehicleId);
            if (rec == null || pedestrian == null || !pedestrian.IsAlive) return false;
            if (rec.Destroyed) { log = $"⛔ {vehicleId} est une épave immobile — aucun écrasement."; return false; }
            if (IsEmbarked(pedestrian) && GetVehicleOf(pedestrian) == vehicleId)
            {
                log = $"🛡️ {pedestrian.Name} est à bord de {vehicleId} — immunisé contre son propre écrasement.";
                return false;
            }
            if (!vehicleMoving)
            {
                log = $"🚧 {vehicleId} à l'arrêt : {pedestrian.Name} bousculé sans dégât.";
                return false;
            }
            int damage = VehicleMechanics.RunOverDamageFor(rec.Scale);
            // Convention hors-duel (grenades, DoT, stomp RD-052...) : à 0 PV,
            // résolution du coup fatal au Torse (miracles, syncope, dernier souffle).
            int rem = pedestrian.CurrentHealth - damage;
            string fatalTag = "";
            if (rem <= 0)
            {
                var fatal = pedestrian.EvaluateFatalBlow(BodyPart.Torse, damage);
                fatalTag = $" + [{fatal}]";
            }
            else
            {
                pedestrian.CurrentHealth = rem;
            }
            pedestrian.ActiveStatus |= StatusEffect.ATerre;
            log = $"🚛 <b>ÉCRASEMENT</b> : {vehicleId} ({rec.Scale}) roule sur {pedestrian.Name} — {damage} dégâts + À Terre (projection {VehicleMechanics.RunOverKnockbackTiles} case){fatalTag}.";
            return true;
        }

        // ------------------------------------------------------------------

        public static void ClearCombatState()
        {
            _vehicles.Clear();
            _embarkedIn.Clear();
        }

        /// <summary>Alias explicite pour la purge inter-combats (cf. TitanBossState).</summary>
        public static void ClearAll() => ClearCombatState();

        public static void Cancel(string vehicleId) => Unregister(vehicleId);
    }
}
