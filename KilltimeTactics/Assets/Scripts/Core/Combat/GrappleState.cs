using System;
using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// État partagé de la lutte / grapple au corps-à-corps (RD-039, Livre VI) :
    /// maintient les liens d'étreinte entre combattants (grappler et victime).
    ///
    /// Keyé sur <see cref="CharacterStats"/> pour rester dans la couche Core
    /// (utilisable indifféremment par le joueur et l'IA, sans dépendance Unity).
    /// </summary>
    public static class GrappleState
    {
        private sealed class GrappleHold
        {
            public CharacterStats Grappler;
            public CharacterStats Victim;
        }

        // Mappages bidirectionnels : grappler -> hold et victim -> hold
        private static readonly Dictionary<CharacterStats, GrappleHold> _grapplers = new();
        private static readonly Dictionary<CharacterStats, GrappleHold> _victims = new();

        /// <summary>
        /// Établit une prise de lutte : le grappler saisit et immobilise la victime.
        /// Rompt toute prise antérieure sur l'un ou l'autre des participants.
        /// </summary>
        public static void SetGrapple(CharacterStats grappler, CharacterStats victim)
        {
            if (grappler == null || victim == null || grappler == victim) return;

            // Purge d'éventuelles prises antérieures
            ReleaseGrapple(grappler);
            ReleaseGrapple(victim);

            var hold = new GrappleHold { Grappler = grappler, Victim = victim };
            _grapplers[grappler] = hold;
            _victims[victim] = hold;

            victim.ApplyStatus(StatusEffect.Immobilise, 1);
        }

        /// <summary>
        /// Rompt la prise impliquant ce combattant (qu'il soit grappler ou victime).
        /// Retire l'état Immobilisé sur la victime si elle n'a pas d'autres entraves.
        /// </summary>
        public static bool ReleaseGrapple(CharacterStats participant)
        {
            if (participant == null) return false;

            GrappleHold hold = null;
            if (_grapplers.TryGetValue(participant, out hold) || _victims.TryGetValue(participant, out hold))
            {
                if (hold != null)
                {
                    if (hold.Grappler != null) _grapplers.Remove(hold.Grappler);
                    if (hold.Victim != null)
                    {
                        _victims.Remove(hold.Victim);
                        hold.Victim.RemoveStatus(StatusEffect.Immobilise);
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Vrai si ce combattant maintient quelqu'un en lutte.
        /// </summary>
        public static bool IsGrappling(CharacterStats grappler)
        {
            if (grappler == null) return false;
            if (!_grapplers.TryGetValue(grappler, out var hold) || hold == null) return false;
            if (!CanMaintainGrapple(hold.Grappler, hold.Victim))
            {
                ReleaseGrapple(grappler);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Vrai si ce combattant est actuellement saisi et immobilisé en lutte.
        /// </summary>
        public static bool IsGrappled(CharacterStats victim)
        {
            if (victim == null) return false;
            if (!_victims.TryGetValue(victim, out var hold) || hold == null) return false;
            if (!CanMaintainGrapple(hold.Grappler, hold.Victim))
            {
                ReleaseGrapple(victim);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Vrai si le grappler maintient spécifiquement cette victime.
        /// </summary>
        public static bool IsGrappling(CharacterStats grappler, CharacterStats victim)
        {
            if (grappler == null || victim == null) return false;
            if (_grapplers.TryGetValue(grappler, out var hold) && hold != null)
            {
                return hold.Victim == victim && CanMaintainGrapple(hold.Grappler, hold.Victim);
            }
            return false;
        }

        /// <summary>
        /// Retrouve la victime maintenue par ce grappler.
        /// </summary>
        public static bool TryGetGrappledVictim(CharacterStats grappler, out CharacterStats victim)
        {
            victim = null;
            if (grappler == null) return false;
            if (_grapplers.TryGetValue(grappler, out var hold) && hold != null && hold.Victim != null)
            {
                if (CanMaintainGrapple(hold.Grappler, hold.Victim))
                {
                    victim = hold.Victim;
                    return true;
                }
                ReleaseGrapple(grappler);
            }
            return false;
        }

        /// <summary>
        /// Retrouve le grappler qui enserre cette victime.
        /// </summary>
        public static bool TryGetGrappler(CharacterStats victim, out CharacterStats grappler)
        {
            grappler = null;
            if (victim == null) return false;
            if (_victims.TryGetValue(victim, out var hold) && hold != null && hold.Grappler != null)
            {
                if (CanMaintainGrapple(hold.Grappler, hold.Victim))
                {
                    grappler = hold.Grappler;
                    return true;
                }
                ReleaseGrapple(victim);
            }
            return false;
        }

        /// <summary>
        /// Vérifie si la lutte peut être physiquement maintenue :
        /// les deux combattants doivent être vivants et le grappler ne doit pas être
        /// neutralisé (Inconscient, Agonisant, Paralysé, Sonné, Étourdi).
        /// </summary>
        public static bool CanMaintainGrapple(CharacterStats grappler, CharacterStats victim)
        {
            if (grappler == null || victim == null) return false;
            if (!grappler.IsAlive || !victim.IsAlive) return false;

            var gFx = grappler.ActiveStatus;
            if (gFx.HasFlag(StatusEffect.Inconscient) ||
                gFx.HasFlag(StatusEffect.Agonisant) ||
                gFx.HasFlag(StatusEffect.Paralyse) ||
                gFx.HasFlag(StatusEffect.Sonne) ||
                gFx.HasFlag(StatusEffect.Etourdi))
            {
                return false;
            }

            var vFx = victim.ActiveStatus;
            if (vFx.HasFlag(StatusEffect.Inconscient) || vFx.HasFlag(StatusEffect.Agonisant))
            {
                // La victime inconsciente/agonisante n'est plus en lutte active : la prise cède
                // (le corps devient simplement inerte et peut être traîné).
                return false;
            }

            return true;
        }

        /// <summary>
        /// Nettoie les prises caduques au début de chaque tour personnel.
        /// </summary>
        public static void OnUnitTurnStart(CharacterStats unit)
        {
            if (unit == null) return;
            if (_grapplers.TryGetValue(unit, out var gHold) && gHold != null)
            {
                if (!CanMaintainGrapple(gHold.Grappler, gHold.Victim))
                {
                    ReleaseGrapple(unit);
                }
            }
            if (_victims.TryGetValue(unit, out var vHold) && vHold != null)
            {
                if (!CanMaintainGrapple(vHold.Grappler, vHold.Victim))
                {
                    ReleaseGrapple(unit);
                }
                else if (!vHold.Victim.ActiveStatus.HasFlag(StatusEffect.Immobilise))
                {
                    vHold.Victim.ApplyStatus(StatusEffect.Immobilise, 1);
                }
            }
        }

        /// <summary>
        /// Réinitialise l'ensemble des prises (utile pour réinitialisation de combat et tests unitaires).
        /// </summary>
        public static void ClearAll()
        {
            _grapplers.Clear();
            _victims.Clear();
        }
    }
}
