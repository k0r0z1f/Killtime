using System;
using Killtime.Core.Dice;
using Killtime.Core.Combat;

namespace Killtime.Core.Character
{
    // Split move-only de CharacterStats — Dots : DoT RD-045 : pools residuels, tick.
    public partial class CharacterStats
    {

        // RD-045 : pool de dégâts résiduels dégressifs par statut DoT.
        // Ex : 3 résiduels sur Roger → début de tour : 3 dégâts, pool → 2, etc.
        // jusqu'à 0 (disparition du statut) ou soin (RemoveStatus/Heal).
        private readonly System.Collections.Generic.Dictionary<StatusEffect, int> _residualDamages = new();
        public System.Collections.Generic.IReadOnlyDictionary<StatusEffect, int> ResidualDamages => _residualDamages;

        // RD-045 : aggravation (ex : Hémorragie Profonde) : au lieu de décroître,
        // les dégâts résiduels DOUBLENT chaque tour jusqu'au soin
        // (spé « saignement doublé », test TestDot_RD045_Aggravation).
        private readonly System.Collections.Generic.HashSet<StatusEffect> _aggravatingDots = new();

        /// <summary>
        /// Dégât résiduel restant pour un DoT (0 = aucun pool, tick minimal 1 si statut présent).
        /// </summary>
        public int GetResidualDamage(StatusEffect dot)
        {
            return _residualDamages.TryGetValue(dot, out int v) ? Math.Max(0, v) : 0;
        }

        /// <summary>
        /// Pose un pool résiduel dégressif + le statut persistant associé.
        /// Prend le max (pas de cumul infini). aggravating=true : +1/tour au lieu de -1.
        /// </summary>
        public void ApplyResidualDamage(StatusEffect dot, int amount, bool aggravating = false)
        {
            if (!IsDotStatus(dot) || amount <= 0) return;
            ApplyStatus(dot, 1);
            int existing = _residualDamages.TryGetValue(dot, out int v) ? v : 0;
            _residualDamages[dot] = Math.Max(existing, amount);
            if (aggravating) _aggravatingDots.Add(dot);
        }

        /// <summary>
        /// Stoppe un DoT (soin) : purge le pool + le statut. Retourne vrai si présent.
        /// </summary>
        public bool ClearResidualDamage(StatusEffect dot)
        {
            bool had = _residualDamages.Remove(dot);
            _aggravatingDots.Remove(dot);
            if (ActiveStatus.HasFlag(dot))
            {
                RemoveStatus(dot);
                return true;
            }
            return had;
        }

        /// <summary>
        /// RD-045 : tick DoT en DÉBUT de tour personnel (Livre VII §27.1).
        /// Pour chaque Saignement/Empoisonne/EnFeu/Asphyxie actif : inflige le pool
        /// résiduel (ou le tick minimal si aucun pool), puis décrémente de 1
        /// (0 = disparition) sauf aggravation (pool doublé, spé Hémorragie Profonde).
        /// Ignore l'armure ; EnFeu passe par le bouclier d'abord ; Asphyxie draine
        /// aussi 1 PA. Soin = RemoveStatus.
        /// </summary>
        public DotTickResult TickTurnStatus()
        {
            var result = new DotTickResult();
            if (IsDead) return result;

            StatusEffect[] dots = new StatusEffect[]
            {
                StatusEffect.Saignement,
                StatusEffect.Empoisonne,
                StatusEffect.EnFeu,
                StatusEffect.Asphyxie
            };

            for (int i = 0; i < dots.Length; i++)
            {
                var dot = dots[i];
                if (!ActiveStatus.HasFlag(dot)) continue;

                int pool = GetResidualDamage(dot);
                if (pool <= 0)
                {
                    pool = 3;
                    _residualDamages[dot] = pool;
                }
                bool aggravating = _aggravatingDots.Contains(dot);
                // Aggravation (Hémorragie Profonde : « saignement doublé ») : le pool
                // double AVANT le tick, et la valeur doublée est infligée puis conservée.
                int dmg;
                if (aggravating)
                {
                    int grown = Math.Min(10, Math.Max(2, pool * 2));
                    _residualDamages[dot] = grown;
                    dmg = Math.Max(1, grown);
                }
                else
                {
                    dmg = Math.Max(1, pool);
                }

                int shieldAbs = 0;
                int finalDmg = dmg;

                // EnFeu : brûlure externe → bouclier d'abord (Livre VIII §32.2).
                // AbsorbShield retourne le RESTE (test ArmoryJsonTests) : absorbé = dmg - reste.
                if (dot == StatusEffect.EnFeu)
                {
                    int remainder = AbsorbShield(dmg);
                    shieldAbs = Math.Max(0, dmg - remainder);
                    finalDmg = Math.Max(0, remainder);
                    result.ShieldAbsorbed += shieldAbs;
                }

                if (finalDmg > 0)
                {
                    if (CurrentHealth - finalDmg <= 0)
                    {
                        result.FatalResolution = EvaluateFatalBlow(BodyPart.Torse, finalDmg);
                    }
                    else
                    {
                        CurrentHealth -= finalDmg;
                    }
                }

                // Asphyxie : suffocation → 1 PA drainé en plus des PV.
                if (dot == StatusEffect.Asphyxie && IsAlive)
                {
                    CurrentActionPoints = Math.Max(0, CurrentActionPoints - 1);
                    result.PaDrained += 1;
                }

                result.TotalDamage += finalDmg;
                result.DamageByStatus[dot] = finalDmg;

                // Décrément dégressif : 3 → 2 → 1 → 0 (disparition).
                // Aggravation : déjà doublé ci-dessus, le pool reste tel quel.
                if (pool > 0 && !aggravating)
                {
                    int left = pool - 1;
                    if (left <= 0)
                    {
                        RemoveStatus(dot);
                        result.ClearedStatuses.Add(dot);
                    }
                    else
                    {
                        _residualDamages[dot] = left;
                    }
                }
                // Pool à 0 (statut legacy sans valeur) : tick minimal permanent,
                // ne disparaît qu'au soin (RemoveStatus) — pas d'auto-clear ici.

                if (!IsAlive)
                {
                    result.Died = true;
                    break;
                }
            }

            LastDotTick = result;
            return result;
        }

        /// <summary>Dernier tick DoT (posé par TickTurnStatus, lu par l'arène pour logs/FX).</summary>
        public DotTickResult LastDotTick { get; set; } = new DotTickResult();

        /// <summary>Tick minimal quand un DoT est actif sans pool résiduel (legacy).</summary>
        public static int DefaultDotDamage(StatusEffect dot)
        {
            if (dot == StatusEffect.EnFeu) return 2;
            return 1;
        }
    }
}
