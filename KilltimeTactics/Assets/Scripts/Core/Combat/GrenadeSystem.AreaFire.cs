using System;
using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.Tactics.Grid;

namespace Killtime.Core.Combat
{
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
