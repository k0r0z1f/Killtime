using System;
using Killtime.Core.Dice;
using Killtime.Core.Combat;

namespace Killtime.Core.Character
{
    // Split move-only de CharacterStats — Status : Statuts generiques, durees, synchronisation.
    public partial class CharacterStats
    {

        public static bool IsPersistentStatus(StatusEffect effect)
        {
            return effect == StatusEffect.Inconscient
                || effect == StatusEffect.Agonisant
                || effect == StatusEffect.Saignement
                || effect == StatusEffect.Empoisonne
                || effect == StatusEffect.EnFeu
                || effect == StatusEffect.Asphyxie
                // RD-083 (Livre VII §28.3) : Souffrant ne s'efface jamais au tick —
                // seuls des Soins Majeurs (CON×2 en un coup) le lèvent.
                || effect == StatusEffect.Souffrant;
        }

        /// <summary>
        /// RD-045 : statuts à dégâts résiduels (DoT). Tick en début de tour personnel.
        /// </summary>
        public static bool IsDotStatus(StatusEffect effect)
        {
            return effect == StatusEffect.Saignement
                || effect == StatusEffect.Empoisonne
                || effect == StatusEffect.EnFeu
                || effect == StatusEffect.Asphyxie;
        }

        public void ApplyStatus(StatusEffect effect, int durationInTurns = 1, int residualDamage = 0, bool aggravatingResidual = false)
        {
            if (effect == StatusEffect.None) return;

            _activeStatus |= effect;

            var allEffects = (StatusEffect[])Enum.GetValues(typeof(StatusEffect));
            for (int i = 0; i < allEffects.Length; i++)
            {
                var flag = allEffects[i];
                if (flag == StatusEffect.None) continue;
                if (effect.HasFlag(flag))
                {
                    if (IsPersistentStatus(flag))
                    {
                        _statusDurations[flag] = -1;
                    }
                    else
                    {
                        int existing = _statusDurations.TryGetValue(flag, out int d) ? d : 0;
                        _statusDurations[flag] = Math.Max(existing, Math.Max(1, durationInTurns));
                    }
                    // RD-045 : pool résiduel posé via ApplyStatus(..., residualDamage).
                    if (residualDamage > 0 && IsDotStatus(flag))
                    {
                        int existingRes = _residualDamages.TryGetValue(flag, out int r) ? r : 0;
                        _residualDamages[flag] = Math.Max(existingRes, residualDamage);
                        if (aggravatingResidual) _aggravatingDots.Add(flag);
                    }
                }
            }
        }

        public void RemoveStatus(StatusEffect effect)
        {
            if (effect == StatusEffect.None) return;

            var allEffects = (StatusEffect[])Enum.GetValues(typeof(StatusEffect));
            for (int i = 0; i < allEffects.Length; i++)
            {
                var flag = allEffects[i];
                if (flag != StatusEffect.None && effect.HasFlag(flag))
                {
                    _statusDurations.Remove(flag);
                    _activeStatus &= ~flag;
                    // RD-045 : le soin stoppe le DoT → purge le pool résiduel.
                    _residualDamages.Remove(flag);
                    _aggravatingDots.Remove(flag);
                }
            }
        }

        public void ClearAllStatus()
        {
            _statusDurations.Clear();
            _residualDamages.Clear();
            _aggravatingDots.Clear();
            _activeStatus = StatusEffect.None;
        }

        private void SynchronizeDurationsFromActiveStatus()
        {
            var allEffects = (StatusEffect[])Enum.GetValues(typeof(StatusEffect));
            for (int i = 0; i < allEffects.Length; i++)
            {
                var flag = allEffects[i];
                if (flag == StatusEffect.None) continue;

                if (_activeStatus.HasFlag(flag))
                {
                    if (!_statusDurations.ContainsKey(flag))
                    {
                        _statusDurations[flag] = IsPersistentStatus(flag) ? -1 : 1;
                    }
                }
                else
                {
                    _statusDurations.Remove(flag);
                }
            }
        }

        public System.Collections.Generic.List<StatusEffect> TickTurnStatusDurations()
        {
            var expired = new System.Collections.Generic.List<StatusEffect>();
            var keys = new System.Collections.Generic.List<StatusEffect>(_statusDurations.Keys);

            for (int i = 0; i < keys.Count; i++)
            {
                var flag = keys[i];
                int rem = _statusDurations[flag];

                if (rem > 0)
                {
                    rem--;
                    if (rem <= 0)
                    {
                        expired.Add(flag);
                    }
                    else
                    {
                        _statusDurations[flag] = rem;
                    }
                }
            }

            for (int i = 0; i < expired.Count; i++)
            {
                _statusDurations.Remove(expired[i]);
                _activeStatus &= ~expired[i];
            }

            return expired;
        }
    }
}
