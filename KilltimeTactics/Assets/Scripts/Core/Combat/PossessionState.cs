using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// RD-204 (Livre VI §24.4) : Possession par la voie Esprit.
    /// Sur un différentiel ≥ 10 en épreuve Esprit, le contrôleur prend le
    /// contrôle de la cible pendant 1 tour (boss : seuil relevé, cf.
    /// CoreRulesConfig.PossessBossThreshold).
    ///
    /// Keyé sur <see cref="CharacterStats"/> pour rester dans la couche Core
    /// (joueur + IA, sans dépendance Unity), même assiette que GrappleState :
    /// purge à la mort/neutralisation, expiration après le tour contrôlé,
    /// ClearAll au reset de combat (TurnManager).
    ///
    /// Périmètre : état + duel + expiration. Le pilotage effectif de l'unité
    /// possédée (ordres arène/IA, menu radial) est un suivi à part.
    /// </summary>
    public static class PossessionState
    {
        private sealed class PossessionHold
        {
            public CharacterStats Controller;
            public CharacterStats Victim;
            // Faux = la victime n'a pas encore joué son tour sous contrôle.
            // Basculé au premier OnUnitTurnStart de la victime, purgé au second.
            public bool Consumed;
        }

        private static readonly Dictionary<CharacterStats, PossessionHold> _victims = new();
        private static readonly Dictionary<CharacterStats, PossessionHold> _controllers = new();

        /// <summary>
        /// Établit le contrôle : rompt toute possession antérieure impliquant
        /// l'un ou l'autre des participants (un contrôleur = une victime).
        /// </summary>
        public static void SetPossession(CharacterStats controller, CharacterStats victim)
        {
            if (controller == null || victim == null || controller == victim) return;

            ReleasePossession(controller);
            ReleasePossession(victim);

            var hold = new PossessionHold { Controller = controller, Victim = victim };
            _victims[victim] = hold;
            _controllers[controller] = hold;
        }

        /// <summary>Rompt la possession impliquant ce participant (contrôleur ou victime).</summary>
        public static bool ReleasePossession(CharacterStats participant)
        {
            if (participant == null) return false;

            PossessionHold hold = null;
            if (_victims.TryGetValue(participant, out hold) || _controllers.TryGetValue(participant, out hold))
            {
                if (hold != null)
                {
                    if (hold.Controller != null) _controllers.Remove(hold.Controller);
                    if (hold.Victim != null) _victims.Remove(hold.Victim);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Vrai si cette unité est actuellement sous contrôle étranger (et valide).</summary>
        public static bool IsPossessed(CharacterStats victim)
        {
            if (victim == null) return false;
            if (!_victims.TryGetValue(victim, out var hold) || hold == null) return false;
            if (!CanMaintainPossession(hold.Controller, hold.Victim))
            {
                ReleasePossession(victim);
                return false;
            }
            return true;
        }

        /// <summary>Vrai si cette unité contrôle actuellement quelqu'un (et valide).</summary>
        public static bool IsControlling(CharacterStats controller)
        {
            if (controller == null) return false;
            if (!_controllers.TryGetValue(controller, out var hold) || hold == null) return false;
            if (!CanMaintainPossession(hold.Controller, hold.Victim))
            {
                ReleasePossession(controller);
                return false;
            }
            return true;
        }

        /// <summary>Retrouve le contrôleur de cette victime.</summary>
        public static bool TryGetController(CharacterStats victim, out CharacterStats controller)
        {
            controller = null;
            if (victim == null) return false;
            if (_victims.TryGetValue(victim, out var hold) && hold != null && hold.Controller != null)
            {
                if (CanMaintainPossession(hold.Controller, hold.Victim))
                {
                    controller = hold.Controller;
                    return true;
                }
                ReleasePossession(victim);
            }
            return false;
        }

        /// <summary>Retrouve la victime contrôlée par ce contrôleur.</summary>
        public static bool TryGetVictim(CharacterStats controller, out CharacterStats victim)
        {
            victim = null;
            if (controller == null) return false;
            if (_controllers.TryGetValue(controller, out var hold) && hold != null && hold.Victim != null)
            {
                if (CanMaintainPossession(hold.Controller, hold.Victim))
                {
                    victim = hold.Victim;
                    return true;
                }
                ReleasePossession(controller);
            }
            return false;
        }

        /// <summary>
        /// Maintien du lien : les deux doivent être vivants, le contrôleur
        /// lucide (ni Inconscient, ni Paralysé, ni Sonné, ni Agonisant), la
        /// victime vivante et consciente (une victime K.O. n'est plus pilotable).
        /// </summary>
        public static bool CanMaintainPossession(CharacterStats controller, CharacterStats victim)
        {
            if (controller == null || victim == null) return false;
            if (!controller.IsAlive || !victim.IsAlive) return false;

            var cFx = controller.ActiveStatus;
            if (cFx.HasFlag(StatusEffect.Inconscient) ||
                cFx.HasFlag(StatusEffect.Paralyse) ||
                cFx.HasFlag(StatusEffect.Sonne) ||
                cFx.HasFlag(StatusEffect.Agonisant))
            {
                return false;
            }

            if (victim.ActiveStatus.HasFlag(StatusEffect.Inconscient))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Durée « 1 tour » : la victime joue UN tour sous contrôle (premier
        /// OnUnitTurnStart = tour contrôlé, marqué consommé), le lien expire
        /// au tour suivant. Le tour du contrôleur ne fait jamais expirer
        /// (ordre d'initiative quelconque).
        /// </summary>
        public static void OnUnitTurnStart(CharacterStats unit)
        {
            if (unit == null) return;
            if (_victims.TryGetValue(unit, out var hold) && hold != null)
            {
                if (!CanMaintainPossession(hold.Controller, hold.Victim))
                {
                    ReleasePossession(unit);
                    return;
                }
                if (!hold.Consumed)
                {
                    hold.Consumed = true;
                    return;
                }
                ReleasePossession(unit);
            }
            // Balayage des liens caducs (mort/neutralisation entre deux tours).
            // ReleasePossession (pas un Remove partiel) : les deux dictionnaires
            // restent synchronisés, sans entrée contrôleur orpheline.
            if (_victims.Count == 0) return;
            CharacterStats[] keys = new CharacterStats[_victims.Count];
            _victims.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                if (k == null || !_victims.TryGetValue(k, out var h) || h == null
                    || !CanMaintainPossession(h.Controller, h.Victim))
                {
                    if (k != null) ReleasePossession(k);
                }
            }
        }

        /// <summary>Retire une unité qui quitte la carte (lien perdu).</summary>
        public static void RemoveUnit(CharacterStats unit)
        {
            if (unit == null) return;
            ReleasePossession(unit);
        }

        public static void ClearAll()
        {
            _victims.Clear();
            _controllers.Clear();
        }
    }
}
