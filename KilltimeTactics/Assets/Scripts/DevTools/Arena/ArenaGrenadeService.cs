using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.WebGL;
using Killtime.UI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Grenades (lancer, dispersion, souffle, zones persistantes) + lancer d'arme.
    /// Logique déplacée verbatim depuis CombatDevArena.
    /// Les helpers statiques restent appelables via la façade (HUD, IA).
    /// La coroutine est hébergée par la façade via IArenaHost (F2).
    /// TryApplyBlastKnockback (phase 6) est reçu via BlastKnockback.
    /// </summary>
    public sealed class ArenaGrenadeService
    {
        private readonly ArenaContext _ctx;
        private readonly ArenaUnitStore _units;
        private readonly ArenaTargetingService _targeting;
        private readonly ArenaOverwatchService _overwatch;
        private ArenaState _state => _ctx.State;
        private IArenaHost _host => _ctx.Host;

        /// <summary>Enregistre un snapshot chrono (fourni par la façade).</summary>
        public Action<string> SnapshotRecorder;

        /// <summary>Refoulement radial des gros calibres (fourni par la façade, phase 6).</summary>
        public Func<TacticalUnit, HexCoordinates, bool> BlastKnockback;

        public ArenaGrenadeService(ArenaContext ctx, ArenaUnitStore units, ArenaTargetingService targeting, ArenaOverwatchService overwatch)
        {
            _ctx = ctx;
            _units = units;
            _targeting = targeting;
            _overwatch = overwatch;
        }

        public static List<InventoryItem> GetThrowableGrenades(CharacterSheet sheet)
        {
            var list = new List<InventoryItem>();
            if (sheet?.Inventory == null) return list;
            for (int i = 0; i < sheet.Inventory.Count; i++)
            {
                var it = sheet.Inventory[i];
                if (it != null && it.IsThrowableGrenade()) list.Add(it);
            }
            return list;
        }

        public static InventoryItem GetEquippedLauncher(CharacterSheet sheet)
        {
            if (sheet?.Inventory == null) return null;
            for (int i = 0; i < sheet.Inventory.Count; i++)
            {
                var it = sheet.Inventory[i];
                if (it != null && it.IsLauncher && it.IsEquipped) return it;
            }
            return null;
        }

        public static InventoryItem GetAnyLauncher(CharacterSheet sheet)
        {
            if (sheet?.Inventory == null) return null;
            var eq = GetEquippedLauncher(sheet);
            if (eq != null) return eq;
            for (int i = 0; i < sheet.Inventory.Count; i++)
            {
                var it = sheet.Inventory[i];
                if (it != null && it.IsLauncher) return it;
            }
            return null;
        }

        /// <summary>
        /// Déclenche un lancer de grenade sur une CASE (pas une cible anatomique) :
        /// paie les PA (2 main / 3 lanceur + 1 visée + bonus), consomme 1 grenade du stock,
        /// anime le lancer, disperse en cas d'échec Ballistique, puis fait détoner la zone.
        /// </summary>
        public void ExecuteGrenadeThrow(
            HexCoordinates targetCoords,
            string grenadeItemId,
            bool useLauncher,
            bool aimed,
            int attackerBonusAP = 0)
        {
            var turnManager = _ctx.TurnManager;
            var grid = _ctx.Grid;
            if (_ctx.Guard.IsResolving)
            {
                _host.ArenaLog("⚠️ Lancer ignoré : une résolution est déjà en cours (anti double-clic).");
                return;
            }

            var attacker = turnManager != null ? turnManager.ActiveUnit : _state.PlayerUnit;

            if (TurnManager.IsMultiplayerPlayerClient())
            {
                if (attacker != null)
                {
                    Killtime.Multi.VTTTableSync.Instance?.RequestGrenade(
                        attacker,
                        targetCoords,
                        grenadeItemId,
                        useLauncher,
                        aimed,
                        attackerBonusAP
                    );
                }
                return;
            }
            if (attacker == null || attacker.Stats == null)
            {
                _host.ArenaLog("⚠️ Impossible de lancer : aucune unité active.");
                return;
            }
            if (!attacker.Stats.IsAlive)
            {
                _host.ArenaLog($"⚠️ {attacker.Stats.Name} est hors de combat !");
                return;
            }

            var sheet = attacker.GetOrBuildSheet();
            InventoryItem grenade = null;
            if (!string.IsNullOrEmpty(grenadeItemId) && sheet.Inventory != null)
                grenade = sheet.Inventory.Find(i => i != null && i.ItemId == grenadeItemId && i.IsThrowableGrenade());
            if (grenade == null)
            {
                var throwables = GetThrowableGrenades(sheet);
                grenade = throwables.Count > 0 ? throwables[0] : null;
            }
            if (grenade == null)
            {
                _host.ArenaLog($"⚠️ {attacker.Stats.Name} n'a aucune grenade en inventaire ! Achetez-en au marché (Grenades).");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            InventoryItem launcher = null;
            if (useLauncher)
            {
                launcher = GetAnyLauncher(sheet);
                if (launcher == null)
                {
                    _host.ArenaLog($"⚠️ Aucun lance-grenades en inventaire : lancer à la main ({grenade.Name}).");
                    useLauncher = false;
                }
                else if (!grenade.LauncherCompatible)
                {
                    _host.ArenaLog($"⚠️ {grenade.Name} incompatible avec un lanceur (poudre noire / inerte) : lancer à la main.");
                    launcher = null;
                    useLauncher = false;
                }
            }

            int maxRange = GrenadeRules.ComputeMaxRange(grenade, launcher);
            int dist = attacker.CurrentCoords.DistanceTo(targetCoords);
            if (dist > maxRange)
            {
                _host.ArenaLog($"⚠️ Hors de portée : {dist} cases > {maxRange} ({(launcher != null ? launcher.Name : "main")}). Rapprochez-vous ou équipez un lanceur.");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.Move_Denied, 0.6f);
                return;
            }

            int baseCost = GrenadeRules.ComputeAPCost(launcher, aimed);
            // Milkor MGL : cadence de zone, -1 PA par tir (ApCostModifier du lanceur utilisé).
            if (launcher != null && launcher.ApCostModifier != 0)
                baseCost = Math.Max(1, baseCost + launcher.ApCostModifier);
            int bonusWant = Mathf.Max(0, attackerBonusAP);
            DiceType atkDie = attacker.Stats.GetSkillDie(SkillType.Ballistique, true);
            if (!attacker.Stats.CanAttack(atkDie))
            {
                int maxAtt = attacker.Stats.GetMaxAttacksAllowed(atkDie);
                _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> a épuisé son quota d'attaque ({attacker.Stats.AttacksThisTurn}/{maxAtt}) !");
                return;
            }
            if (!_ctx.GetInfiniteAP() && attacker.Stats.CurrentActionPoints < baseCost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : lancer {(launcher != null ? "au lanceur" : "à la main")}{(aimed ? " visé" : "")} = {baseCost} PA (reste {attacker.Stats.CurrentActionPoints}).");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            // Débit PA de base (malus armures inclus) puis bonus d'injection
            // (mises exemptées du malus, comme les passes d'armes).
            if (!_ctx.GetInfiniteAP())
            {
                attacker.Stats.ConsumeActionPoints(baseCost);
                int applied = 0;
                for (int i = 0; i < bonusWant; i++)
                {
                    if (attacker.Stats.ConsumeActionPoints(1, true)) applied++;
                    else break;
                }
                bonusWant = applied;
            }
            attacker.Stats.RegisterAttack();

            // Consomme 1 grenade du stock (décrémente la pile ou retire l'item).
            var grenadeDef = grenade.Clone();
            if (grenade.IsStackable && grenade.Quantity > 1)
            {
                grenade.Quantity--;
            }
            else
            {
                sheet.RemoveItem(grenade.ItemId);
            }
            attacker.NotifyInventoryChanged(true);

            // Orientation + animation de lancer.
            var targetNode = grid != null ? grid.GetNode(targetCoords) : null;
            Vector3 targetWorld = targetNode != null ? targetNode.WorldPosition
                : targetCoords.ToWorldPosition(grid != null ? grid.HexRadius : 1f, 0f);
            Vector3 dir = targetWorld - attacker.transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero) attacker.transform.rotation = Quaternion.LookRotation(dir);
            var attVisual = attacker.GetComponent<TacticalUnitVisual>();
            attVisual?.TriggerGrenadeThrow();

            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.PlayGrenadeThrow(attacker.transform.position, useLauncher);
            }

            // Brouillard de guerre : l'explosion révèle le lanceur (tirs bruyants).
            try { Killtime.Tactics.Visibility.FogOfWarManager.Instance?.NotifyLoudShot(attacker); }
            catch { /* fog optionnel */ }

            // Jet de précision immédiat (pour connaître le point de chute avant l'arc visuel).
            var calc = new GrenadeCalculator(_ctx.Dice);
            var throwOutcome = calc.ResolveThrow(attacker.Stats, dist, grenadeDef, launcher, bonusWant, aimed);
            var blastCoords = ResolveScatterCoords(targetCoords, throwOutcome);

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Fire, 0.85f);
            attVisual?.SpawnFloatingText(
                $"💣 {grenadeDef.Name} (-{baseCost + bonusWant} PA)", new Color(1f, 0.72f, 0.15f));

            _ctx.Guard.TryEnter();
            _host.ArenaStartCoroutine(GrenadeThrowRoutine(attacker, grenadeDef, launcher, targetCoords, blastCoords, throwOutcome, dist, maxRange, baseCost + bonusWant));
        }

        private HexCoordinates ResolveScatterCoords(HexCoordinates target, GrenadeThrowOutcome outcome)
        {
            var grid = _ctx.Grid;
            if (outcome.IsOnTarget || outcome.ScatterDistance <= 0 || grid == null) return target;
            var cur = target;
            for (int i = 0; i < outcome.ScatterDistance; i++)
            {
                var next = cur.GetNeighbor(Mathf.Max(0, outcome.ScatterDirIndex));
                if (grid.GetNode(next) == null) break;
                cur = next;
            }
            return cur;
        }

        private IEnumerator GrenadeThrowRoutine(
            TacticalUnit attacker, InventoryItem grenadeDef, InventoryItem launcher,
            HexCoordinates aimedCoords, HexCoordinates blastCoords,
            GrenadeThrowOutcome throwOutcome, int dist, int maxRange, int totalPa)
        {
            var grid = _ctx.Grid;
            Vector3 start = attacker.transform.position + Vector3.up * 1.4f;
            var blastNode = grid != null ? grid.GetNode(blastCoords) : null;
            Vector3 blastPos = blastNode != null ? blastNode.WorldPosition
                : blastCoords.ToWorldPosition(grid != null ? grid.HexRadius : 1f, 0f);
            blastPos.y = (blastNode != null ? blastNode.WorldPosition.y : 0f) + 0.05f;

            bool impacted = false;
            bool withLauncher = launcher != null && launcher.IsLauncher;
            GrenadeProjectile.Launch(start, blastPos, grenadeDef, withLauncher, () => { impacted = true; });

            float timeout = 4f;
            float waited = 0f;
            while (!impacted && waited < timeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            GrenadeExplosionFX.Detonate(blastPos, grenadeDef, Mathf.Max(0, grenadeDef.BlastRadius), grid != null ? grid.HexRadius : 1f);

            if (KilltimeAudioManager.Instance != null)
            {
                bool heavy = grenadeDef.DamageDiceCount >= 3 || grenadeDef.BaseDamage >= 12;
                KilltimeAudioManager.Instance.PlayGrenadeDetonation(blastPos, grenadeDef.GrenadeKind, heavy);
            }

            ResolveGrenadeBlast(attacker, grenadeDef, launcher, aimedCoords, blastCoords, throwOutcome, dist, maxRange, totalPa, blastPos);

            yield return new WaitForSeconds(0.55f);
            _ctx.Guard.ForceReset();
        }

        private void ResolveGrenadeBlast(
            TacticalUnit attacker, InventoryItem grenadeDef, InventoryItem launcher,
            HexCoordinates aimedCoords, HexCoordinates blastCoords,
            GrenadeThrowOutcome throwOutcome, int dist, int maxRange, int totalPa, Vector3 blastPos)
        {
            var grid = _ctx.Grid;
            var visualizer = _ctx.Visualizer;
            var calc = new GrenadeCalculator(_ctx.Dice);
            int diceTotal = calc.RollBlastDice(Mathf.Max(0, grenadeDef.DamageDiceCount), out _, out string diceDetail);
            int blastR = Mathf.Max(0, grenadeDef.BlastRadius);
            bool isMortar = launcher != null && (launcher.Name ?? "").Contains("Mortier");

            var allUnits = TacticalUnitRegistry.AllUnits;
            var hits = new List<GrenadeHitResult>();
            int totalDealt = 0;

            for (int i = 0; i < allUnits.Count; i++)
            {
                var u = allUnits[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                int d = u.CurrentCoords.DistanceTo(blastCoords);
                if (d > blastR) continue;
                int cover = 0;
                var node = grid != null ? grid.GetNode(u.CurrentCoords) : null;
                if (node != null)
                {
                    cover = GrenadeRules.ToCoverLevel(node.Cover);
                    // Le mortier lobé passe par-dessus les couverts partiels (Half, 3/4), jamais Full.
                    if (isMortar && (node.Cover == CoverType.Half || node.Cover == CoverType.ThreeQuarters))
                        cover = 0;
                }
                var hit = calc.ResolveHitOnTarget(u.Stats, grenadeDef, diceTotal, d, cover, d == 0);
                hits.Add(hit);
                totalDealt += hit.FinalDamage;
            }

            // Dégradation structurelle de l'environnement par le souffle de la grenade
            var damagedObstacles = new List<ObstacleDamageResult>();
            if (grid != null && blastR >= 0)
            {
                ObstacleDamageType envDmgType = ObstacleDamageType.Explosif;
                if (grenadeDef != null && !string.IsNullOrEmpty(grenadeDef.GrenadeKind))
                {
                    string k = grenadeDef.GrenadeKind.ToLowerInvariant();
                    if (k.Contains("incend") || k.Contains("thermite") || k.Contains("feu")) envDmgType = ObstacleDamageType.Thermique;
                    else if (k.Contains("plasma")) envDmgType = ObstacleDamageType.Plasma;
                    else if (k.Contains("sonique") || k.Contains("flash")) envDmgType = ObstacleDamageType.Sonique;
                }

                for (int q = -blastR; q <= blastR; q++)
                {
                    int r1 = Mathf.Max(-blastR, -q - blastR);
                    int r2 = Mathf.Min(blastR, -q + blastR);
                    for (int r = r1; r <= r2; r++)
                    {
                        var targetCell = new HexCoordinates(blastCoords.Q + q, blastCoords.R + r);
                        int d = blastCoords.DistanceTo(targetCell);
                        int cellRaw = calc.ComputeRawDamage(grenadeDef, diceTotal, d);
                        if (cellRaw > 0 && PropObstacleRegistry.ApplyDamage(targetCell, cellRaw, envDmgType, grid, visualizer, out var obsRes))
                        {
                            damagedObstacles.Add(obsRes);
                        }
                    }
                }
            }

            // --- Zone persistante (RD-041/042/043) : fumée, gaz, feu... ---
            string zoneNote = "";
            if (grenadeDef != null && grenadeDef.ZoneDurationTurns > 0 && blastR > 0)
            {
                _state.SmokeZones.Add(new SmokeZone
                {
                    Center = blastCoords,
                    Radius = blastR,
                    Kind = grenadeDef.GrenadeKind ?? "",
                    Statuses = GrenadeCalculator.ParseStatuses(grenadeDef.GrenadeStatuses ?? ""),
                    TurnsLeft = Math.Max(1, grenadeDef.ZoneDurationTurns),
                    Arcanotech = SmokeScreen.IsArcanotechEra(grenadeDef.Era)
                });
                visualizer?.UpdatePersistentZones(_state.SmokeZones);
                zoneNote = $" 🌫️ <b>Zone {grenadeDef.GrenadeKind}</b> R{blastR} persistante {grenadeDef.ZoneDurationTurns} round(s) en ({blastCoords.Q},{blastCoords.R})"
                    + (SmokeScreen.BlocksSight(grenadeDef.GrenadeKind) ? " — <b>bloque la visée</b> (sauf thermique)" : "")
                    + (SmokeScreen.ZoneDamagePerTurn(grenadeDef.GrenadeKind) > 0 ? " — dégâts/round" : "");
            }

            // --- Log tactique ---
            string via = (launcher != null && launcher.IsLauncher) ? $" au <b>{launcher.Name}</b>" : " à la main";
            string aimTag = throwOutcome.DistancePenalty != 0 ? $" (malus dist {throwOutcome.DistancePenalty})" : "";
            string scatterTag = throwOutcome.IsOnTarget
                ? $"pile sur <b>({blastCoords.Q},{blastCoords.R})</b>"
                : $"dispersée en <b>({blastCoords.Q},{blastCoords.R})</b> (visé ({aimedCoords.Q},{aimedCoords.R}), déviation {throwOutcome.ScatterDistance})";
            string log = $"💣 <b>GRENADE</b> : {attacker.Stats.Name} lance <b>{grenadeDef.Name}</b> [{grenadeDef.Era} / {grenadeDef.GrenadeKind}]{via} à {dist} cases (max {maxRange}, -{totalPa} PA){aimTag} ➔ {scatterTag}\n";
            log += $"   🎲 Lancer : {throwOutcome.LogFragment}\n";
            log += $"   💥 Souffle R{blastR} : [{grenadeDef.BaseDamage} + {diceDetail} ({grenadeDef.DamageDiceCount}d10) = {grenadeDef.BaseDamage + diceTotal}] x falloff (-25%/case, min 25%) + shrapnels +{grenadeDef.ShrapnelDamage} ➔ {hits.Count} cible(s) dans la zone.";
            if (hits.Count == 0) log += " <i>Souffle dans le vide.</i>";
            if (damagedObstacles.Count > 0)
            {
                for (int oIdx = 0; oIdx < damagedObstacles.Count; oIdx++)
                {
                    log += $"\n   • {damagedObstacles[oIdx].Log}";
                }
            }
            if (!string.IsNullOrEmpty(zoneNote)) log += "\n  " + zoneNote;

            // --- Retours visuels/sonores par cible ---
            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i];
                var unit = _units.FindUnitByName(h.TargetName);
                string allyTag = "";
                if (unit != null && unit != attacker && unit.IsPlayerControlled == attacker.IsPlayerControlled)
                    allyTag = " ⚠️<b>TIR ALLIÉ</b>";
                string hpTag = unit != null ? unit.Stats.CurrentHealth + "/" + unit.Stats.MaxHealth + " PV" : "?";
                log += $"\n   • <b>{h.TargetName}</b>{allyTag} à {h.DistanceFromBlast} case(s) : bruts {h.RawDamage} − couv {h.CoverReduction} − armure {h.ArmorAbsorbed}{(h.ShieldAbsorbed > 0 ? $" − bouclier {h.ShieldAbsorbed}" : "")}{(h.Masked ? " 😷<i>masque</i>" : "")} ➔ <color=#FF3B5C><b>{h.FinalDamage} PV</b></color> ({hpTag})";
                if (h.InflictedStatus != StatusEffect.None)
                    log += $" | [{GrenadeCalculator.StatusesToLabel(h.InflictedStatus)}]";
                if (!string.IsNullOrEmpty(h.BossReactionLog))
                    log += $"\n   • {h.BossReactionLog}";
                if (h.ExceededEncaissement) log += " | ⚡<b>CHOC</b>";
                if (h.FatalResolution == FatalBlowResolution.InstantDeath) log += " | 💀<b>MORT</b>";
                else if (h.FatalResolution == FatalBlowResolution.MiracleSaved) log += " | 🔮<b>MIRACLE</b>";
                else if (h.FatalResolution == FatalBlowResolution.ForcedUnconscious) log += " | 💥<b>K.O.</b>";
                else if (h.FatalResolution == FatalBlowResolution.EligibleForLastBreath) log += " | ⚡<b>0 PV</b>";

                if (unit == null) continue;
                var vis = unit.GetComponent<TacticalUnitVisual>();
                bool isGrenadeFatal = (unit != null && !unit.Stats.IsAlive) || h.FatalResolution != FatalBlowResolution.None;
                CombatHUD.RecordCombatAction(
                    attacker.Stats.Name,
                    h.TargetName,
                    attacker.IsPlayerControlled,
                    unit != null && unit.IsPlayerControlled,
                    h.FinalDamage > 0,
                    false,
                    false,
                    h.RawDamage,
                    h.CoverReduction + h.ArmorAbsorbed + h.ShieldAbsorbed,
                    h.FinalDamage,
                    h.ExceededEncaissement,
                    isGrenadeFatal,
                    0
                );

                if (h.FinalDamage > 0)
                {
                    vis?.TriggerHitFlash();
                    vis?.SpawnFloatingText($"-{h.FinalDamage} PV (💣)", new Color(1f, 0.30f, 0.20f));

                    if (unit.Stats.IsAlive && unit.Stats.CurrentHealth > 0)
                    {
                        var mRes = unit.Stats.CheckMoraleAtThreshold(_ctx.Dice, CoreRulesConfig.Instance.StandardTargetDC);
                        if (mRes.Triggered)
                        {
                            log += $"\n   • {mRes.Log}";
                            if (mRes.Surrendered)
                            {
                                vis?.SpawnFloatingText("🏳️ REDDITION !", Color.white);
                                vis?.DropHeldItemsWithPhysics();
                                vis?.SetCombatStance(false);
                            }
                            else if (mRes.Routed)
                            {
                                vis?.SpawnFloatingText("😱 DÉROUTE ! [Agonisant]", new Color(0.95f, 0.2f, 0.85f));
                            }
                        }
                    }
                    if (h.InflictedStatus != StatusEffect.None)
                        vis?.SpawnFloatingText($"[{GrenadeCalculator.StatusesToLabel(h.InflictedStatus)}]", new Color(1f, 0.4f, 0.95f));
                    if (KilltimeAudioManager.Instance != null)
                    {
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Hurt_Heavy, unit.transform.position, 0.8f);
                        if (h.ExceededEncaissement)
                            KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, unit.transform.position, 0.8f);
                    }
                    // Refoulement radial pour les gros calibres au contact du souffle.
                    bool heavy = grenadeDef.DamageDiceCount >= 3 || grenadeDef.BaseDamage >= 12;
                    if (heavy && h.DistanceFromBlast <= 1)
                        BlastKnockback?.Invoke(unit, blastCoords);
                }
                else
                {
                    vis?.SpawnFloatingText(h.CoverReduction > 0 ? $"[COUVERT] Souffle absorbé (-{h.CoverReduction})" : "[SOUFFLE] 0 PV", new Color(0.4f, 0.85f, 1f));
                }

                if (h.FatalResolution == FatalBlowResolution.InstantDeath)
                {
                    vis?.SpawnFloatingText("[MORTEL] INSTANTANÉ !", Color.black);
                    vis?.TriggerFallingBackDeath();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Death_Instant, unit.transform.position, 0.9f);
                }
                else if (h.FatalResolution == FatalBlowResolution.MiracleSaved)
                {
                    vis?.SpawnFloatingText("[MIRACLE] DESTIN SAUVÉ ! (1 PV)", new Color(1f, 0.85f, 0.1f));
                    vis?.TriggerFallingBackDeath();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Miracle_Saved, unit.transform.position, 0.9f);
                }
                else if (h.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                {
                    vis?.SpawnFloatingText("[K.O.] SYNCOPE TRAUMATIQUE !", Color.magenta);
                    vis?.TriggerFallingBackDeath();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.KO_Fall, unit.transform.position, 0.9f);
                }
                else if (h.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                {
                    if (!unit.IsPlayerControlled)
                    {
                        unit.Stats.ChooseSombrer();
                        vis?.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                        vis?.TriggerFallingBackDeath();
                    }
                    else
                    {
                        unit.Stats.ChooseLastBreath();
                        vis?.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1f, 0.5f, 0.1f));
                    }
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.LastBreath, unit.transform.position, 0.9f);
                }
            }

            _host.ArenaLog(log);

            WebBridgeManager.Instance?.SendCombatEvent("GRENADE", log, totalDealt);
            SnapshotRecorder?.Invoke($"Grenade {grenadeDef.Name} en ({blastCoords.Q},{blastCoords.R})");

            // Diffusion VTT Multijoueur si GM
            if (Killtime.Multi.VTTTableSync.Instance != null
                && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction
                && Killtime.Multi.VTTRoomManager.Instance != null
                && Killtime.Multi.VTTRoomManager.Instance.InRoom
                && Killtime.Multi.VTTRoomManager.Instance.IsGM)
            {
                var actionPayload = new Killtime.Multi.VTTCombatActionPayload
                {
                    action = "grenade",
                    actorId = Killtime.Multi.VTTTableSync.UnitIdOf(attacker),
                    destQ = blastCoords.Q,
                    destR = blastCoords.R,
                    grenadeItemId = grenadeDef != null ? grenadeDef.ItemId : "",
                    useLauncher = launcher != null && launcher.IsLauncher,
                    aimed = throwOutcome.IsOnTarget,
                    blastRadius = grenadeDef != null ? grenadeDef.BlastRadius : 0,
                    attackerCostAP = totalPa,
                    combatLog = log
                };
                Killtime.Multi.VTTTableSync.Instance.BroadcastCombatAction(actionPayload);
                // RD-089 : le state_sync embarque zones persistantes + objets au
                // sol — les clients voient la zone dès sa création.
                Killtime.Multi.VTTTableSync.Instance.BroadcastFullCombatState();
            }

            if (_state.CurrentTarget != null && !_state.CurrentTarget.Stats.IsAlive) _host.RequestAutoTargetNextAlive();
            _host.RequestMusicUpdate();
            _host.RequestCombatOverCheck();
        }

        /// <summary>
        /// Lancer d'arme équipée (2 PA) : Hachette de Jet et armes de fortune.
        /// Duel Ballistique à 2-4 cases, dégâts = BaseDamage + différentiel + EC,
        /// puis l'arme quitte l'inventaire et tombe à la case de la cible.
        /// </summary>
        public void ExecuteWeaponThrow(TacticalUnit attacker, TacticalUnit defender)
        {
            var turnManager = _ctx.TurnManager;
            var grid = _ctx.Grid;
            var calculator = _ctx.Calculator;
            if (_ctx.Guard.IsResolving)
            {
                _host.ArenaLog("⚠️ Lancer ignoré : une résolution est déjà en cours.");
                return;
            }
            if (TurnManager.IsMultiplayerPlayerClient())
            {
                _host.ArenaLog("⚠️ Lancer d'arme réservé au GM en multijoueur (pas encore synchronisé VTT).");
                return;
            }
            if (attacker == null) attacker = turnManager != null ? turnManager.ActiveUnit : _state.PlayerUnit;
            if (attacker == null || attacker.Stats == null) { _host.ArenaLog("⚠️ Aucun attaquant pour le lancer."); return; }
            if (defender == null || defender.Stats == null || !defender.Stats.IsAlive) { _host.ArenaLog("⚠️ Aucune cible valide pour le lancer."); return; }
            if (attacker.IsMoving) { _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> est en déplacement : attendez l'arrivée !"); return; }

            var sheet = attacker.GetOrBuildSheet();
            var weapon = sheet?.GetEquippedWeapon();
            if (weapon == null) { _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> n'a aucune arme équipée à lancer !"); return; }
            if (weapon.IsThrowableGrenade()) { _host.ArenaLog("⚠️ Grenades : utilisez l'action Grenade (2 PA), pas le lancer d'arme."); return; }

            int dist = attacker.CurrentCoords.DistanceTo(defender.CurrentCoords);
            if (dist < 2 || dist > CombatDevArena.WeaponThrowMaxRange)
            {
                _host.ArenaLog($"⚠️ <b>Lancer impossible</b> : {dist} cases (portée {2}-{CombatDevArena.WeaponThrowMaxRange}). Au contact, frappez ; au-delà, rapprochez-vous.");
                return;
            }

            DiceType atkDie = attacker.Stats.GetSkillDie(SkillType.Ballistique, true);
            if (!attacker.Stats.CanAttack(atkDie)) { _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> a épuisé son quota d'attaque ce tour !"); return; }
            if (!_ctx.GetInfiniteAP() && attacker.Stats.CurrentActionPoints < CombatDevArena.WeaponThrowAPCost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : lancer = {CombatDevArena.WeaponThrowAPCost} PA (reste {attacker.Stats.CurrentActionPoints}).");
                return;
            }

            string weaponName = weapon.Name;
            int weaponDmg = Math.Max(1, weapon.BaseDamage);
            int weaponEc = Math.Max(0, weapon.AttackBonusEc);
            string weaponId = weapon.ItemId;

            Vector3 dir = defender.transform.position - attacker.transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero) attacker.transform.rotation = Quaternion.LookRotation(dir);
            attacker.GetComponent<TacticalUnitVisual>()?.TriggerGrenadeThrow();

            calculator.SetContactDistanceState(false);
            CoverType cover = _targeting.GetCoverToTarget(attacker, defender);
            // Duel aveugle manuel (pas de double débit PA) : Begin consomme la base 2 PA,
            // échec => on avorte sans perdre l'arme ni le quota d'attaque.
            // RD-036 : le lancer suit la règle hauteur du tir (balistique ±1, laser exempté).
            ElevationRuling throwRuling = _targeting.GetElevationRuling(attacker, defender, SkillType.Ballistique, weapon, false);
            var throwDuel = calculator.BeginSkillDuel(
                attacker.Stats, defender.Stats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive, weaponDmg, false,
                true, null, null,
                defender.Stats.BaseArmorAbsorption, out string throwError, cover, weaponEc,
                false, throwRuling.AttackMod, throwRuling.Label);
            if (throwDuel == null)
            {
                _host.ArenaLog(throwError ?? "⚠️ Lancer impossible : PA insuffisants.");
                return;
            }
            calculator.DeclareAttackerStakes(throwDuel, 0, 0);
            if (throwDuel.DefenderWantsToDefend && !throwDuel.IsDefenderIncapacitated && throwDuel.CanDefenderReact)
                calculator.AutoDeclareDefenderStakes(throwDuel);
            else
                calculator.DeclareDefenderStakes(throwDuel, SkillType.Esquive, false, 0, 0);
            var result = calculator.ResolveBlindDuel(throwDuel);
            attacker.Stats.RegisterAttack();

            _host.ArenaLog($"🗡️ <b>LANCER</b> : {attacker.Stats.Name} lance <b>{weaponName}</b> sur {defender.Stats.Name} ({dist} cases, -{CombatDevArena.WeaponThrowAPCost} PA)\n   {result.CombatLog}");
            _overwatch.HandleChannelInterruptionFx(defender, result.ChannelInterrupted);

            // L'arme quitte la main dans tous les cas (touché ou manqué) et tombe à la case cible.
            try
            {
                var toDrop = sheet.Inventory?.Find(i => i != null && i.ItemId == weaponId);
                if (toDrop != null) sheet.RemoveItem(weaponId);
                else toDrop = weapon;
                toDrop.IsEquipped = false;
                attacker.NotifyInventoryChanged(true);
                Vector3 spawnPos = defender.transform.position;
                var node = grid != null ? grid.GetNode(defender.CurrentCoords) : null;
                if (node != null) spawnPos = node.WorldPosition;
                DroppedWeaponPickup.SpawnAt(toDrop, spawnPos, attacker.Stats.Name);
                _host.ArenaLog($"⚔ <b>{weaponName}</b> tombe au sol en ({defender.CurrentCoords.Q},{defender.CurrentCoords.R}).");
            }
            catch (Exception e) { _host.ArenaLog($"⚠️ Dépôt de l'arme lancée impossible : {e.Message}"); }

            var defVis = defender.GetComponent<TacticalUnitVisual>();
            if (result.IsHit) defVis?.TriggerHitFlash();
            if (!defender.Stats.IsAlive) _host.RequestAutoTargetNextAlive();
            _host.RequestMusicUpdate();
            _host.RequestCombatOverCheck();
        }
    }
}
