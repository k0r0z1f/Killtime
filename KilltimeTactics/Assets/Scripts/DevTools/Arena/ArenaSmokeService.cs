using System;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Zones persistantes (fumée, gaz, feu) + ticks DoT résiduels.
    /// Logique déplacée verbatim depuis CombatDevArena (l.1283-1419).
    /// Stateless vis-à-vis d'Unity : les listes et le TurnManager sont
    /// fournis par la façade via paramètres / IArenaHost (V2 : F1/F4/F5).
    /// </summary>
    public sealed class ArenaSmokeService
    {
        private readonly DiceRoller _dice;
        private readonly IArenaHost _host;
        private readonly Func<TacticalUnit> _getCurrentTarget;

        public ArenaSmokeService(DiceRoller dice, IArenaHost host, Func<TacticalUnit> getCurrentTarget)
        {
            _dice = dice;
            _host = host;
            _getCurrentTarget = getCurrentTarget;
        }

        public bool IsSmoked(HexCoordinates coords, System.Collections.Generic.IReadOnlyList<SmokeZone> zones)
        {
            return SmokeScreen.IsCellSmoked(coords, zones);
        }

        /// <summary>Thermique (spécialisation ou Jumelles) : voit à travers la fumée.</summary>
        public static bool SeesThroughSmoke(TacticalUnit viewer)
        {
            var st = viewer?.Stats;
            if (st == null) return false;
            try
            {
                if (st.HasSpecialization(Killtime.Tactics.Visibility.FogOfWarSystem.SpecThermal)) return true;
            }
            catch { /* fiche partielle */ }
            return SmokeScreen.HasGoggles(st);
        }

        /// <summary>
        /// RD-045 : journalise le tick DoT résiduel du début de tour (déjà appliqué
        /// par TurnManager.RefreshAndNotifyActiveUnitTurnStart) et gère la mort.
        /// </summary>
        public void LogDotTick(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return;
            var tick = unit.Stats.LastDotTick;
            if (tick == null || !tick.HasTick) return;
            var parts = new System.Collections.Generic.List<string>();
            foreach (var kv in tick.DamageByStatus)
            {
                if (kv.Value > 0) parts.Add($"{kv.Value} PV [{kv.Key}]");
            }
            string detail = parts.Count > 0 ? string.Join(" + ", parts) : "effet";
            string extra = "";
            if (tick.ShieldAbsorbed > 0) extra += $", bouclier -{tick.ShieldAbsorbed}";
            if (tick.PaDrained > 0) extra += $", -{tick.PaDrained} PA (asphyxie)";
            _host.ArenaLog($"🩸 <b>DoT résiduel</b> : {unit.Stats.Name} subit <b>{detail}</b> (dégressif -1/tour{extra}).");
            for (int i = 0; i < tick.ClearedStatuses.Count; i++)
                _host.ArenaLog($"✅ <b>{unit.Stats.Name}</b> : [{tick.ClearedStatuses[i]}] dissipé (résiduels épuisés).");
            // Consomme le tick pour ne pas le re-journaliser (téléport/move).
            unit.Stats.LastDotTick = new Killtime.Core.Character.DotTickResult();
            if (!unit.Stats.IsAlive)
            {
                unit.GetComponent<TacticalUnitVisual>()?.TriggerFallingBackDeath();
                if (_getCurrentTarget() == unit) _host.RequestAutoTargetNextAlive();
                _host.RequestMusicUpdate();
                _host.RequestCombatOverCheck();
            }
        }

        /// Effets d'environnement en début de tour : dégâts (ignore armure, barrière
        /// d'abord) + statuts de la zone. Masque : filtre les gaz (dégâts /2).
        /// </summary>
        public void TickSmokeForUnit(TacticalUnit unit, System.Collections.Generic.List<SmokeZone> zones)
        {
            if (unit == null || unit.Stats == null || !unit.Stats.IsAlive) return;
            if (zones.Count == 0) return;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (z.TurnsLeft <= 0) continue;
                if (unit.CurrentCoords.DistanceTo(z.Center) > Math.Max(0, z.Radius)) continue;

                bool masked = SmokeScreen.HasGasMask(unit.Stats) && SmokeScreen.IsGas(z.Kind);
                StatusEffect fx = z.Statuses;
                if (masked) fx = SmokeScreen.FilterGasStatuses(fx, z.Kind, z.Arcanotech);
                if (fx != StatusEffect.None)
                {
                    unit.Stats.ApplyStatus(fx, 1);
                    unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                        $"[{GrenadeCalculator.StatusesToLabel(fx)}]{(masked ? " 😷" : "")}", new Color(1f, 0.4f, 0.95f));
                }

                int dmg = SmokeScreen.ZoneDamagePerTurn(z.Kind);
                if (masked) dmg = SmokeScreen.HalveGasDamage(dmg, z.Kind, z.Arcanotech);
                if (dmg > 0)
                {
                    int shieldRemainder = unit.Stats.AbsorbShield(dmg);
                    int shieldAbs = Math.Max(0, dmg - shieldRemainder);
                    int finalDmg = Math.Max(0, shieldRemainder);
                    if (!SmokeScreen.ZoneIgnoresArmor(z.Kind))
                    {
                        int totalArmor = unit.Stats.BaseArmorAbsorption + unit.Stats.GetWornArmorBonus();
                        int armorAbs = Math.Min(totalArmor, finalDmg);
                        finalDmg = Math.Max(0, finalDmg - armorAbs);
                    }

                    if (finalDmg > 0)
                    {
                        if (unit.Stats.CurrentHealth - finalDmg <= 0)
                        {
                            var fatalRes = unit.Stats.EvaluateFatalBlow(BodyPart.Torse, finalDmg);
                            if (fatalRes == FatalBlowResolution.EligibleForLastBreath)
                            {
                                if (!unit.IsPlayerControlled) unit.Stats.ChooseSombrer();
                                else unit.Stats.ChooseLastBreath();
                            }
                        }
                        else
                        {
                            unit.Stats.CurrentHealth -= finalDmg;
                            var mRes = unit.Stats.CheckMoraleAtThreshold(_dice, CoreRulesConfig.Instance.StandardTargetDC);
                            if (mRes.Triggered)
                            {
                                _host.ArenaLog(mRes.Log);
                                var uVis = unit.GetComponent<TacticalUnitVisual>();
                                if (mRes.Surrendered)
                                {
                                    uVis?.SpawnFloatingText("🏳️ REDDITION !", Color.white);
                                    uVis?.DropHeldItemsWithPhysics();
                                    uVis?.SetCombatStance(false);
                                }
                                else if (mRes.Routed)
                                {
                                    uVis?.SpawnFloatingText("😱 DÉROUTE ! [Agonisant]", new Color(0.95f, 0.2f, 0.85f));
                                }
                            }
                        }
                    }

                    unit.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
                    unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"-{finalDmg} PV ({z.Kind})", new Color(1f, 0.30f, 0.20f));
                    _host.ArenaLog($"🌫️ <b>Zone {z.Kind}</b> : {unit.Stats.Name} subit <b>{finalDmg} PV</b> (bruts {dmg}{(shieldAbs > 0 ? $", bouclier -{shieldAbs}" : "")}{(masked ? ", masque 😷" : "")}).");
                    if (!unit.Stats.IsAlive)
                    {
                        unit.GetComponent<TacticalUnitVisual>()?.TriggerFallingBackDeath();
                        if (_getCurrentTarget() == unit) _host.RequestAutoTargetNextAlive();
                        _host.RequestMusicUpdate();
                        _host.RequestCombatOverCheck();
                    }
                }
                else if (fx != StatusEffect.None)
                {
                    _host.ArenaLog($"🌫️ <b>Zone {z.Kind}</b> : {unit.Stats.Name} [{GrenadeCalculator.StatusesToLabel(fx)}]{(masked ? " (masque 😷)" : "")}.");
                }
            }
        }
    }
}
