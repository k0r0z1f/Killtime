using System;
using System.Collections;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;
using Killtime.WebGL;
using Killtime.UI;
using Killtime.CameraSystem;
using Killtime.Tactics.AI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Passe d'armes principale (duel aveugle Livres II §7 + VI §24.1),
    /// attaque de décor, grappin magnétique. Logique déplacée verbatim
    /// depuis CombatDevArena (ExecuteAttack + 2 routines + obstacle + grappin).
    /// La killcam cinématique reste hébergée par la façade (MonoBehaviour).
    /// </summary>
    public sealed class ArenaAttackService
    {
        private readonly ArenaContext _ctx;
        private readonly ArenaTargetingService _targeting;
        private readonly ArenaOverwatchService _overwatch;
        private readonly ArenaMeleeService _melee;
        private ArenaState _state => _ctx.State;
        private IArenaHost _host => _ctx.Host;

        /// <summary>Enregistre un snapshot chrono (fourni par la façade).</summary>
        public Action<string> SnapshotRecorder;

        public ArenaAttackService(ArenaContext ctx, ArenaTargetingService targeting, ArenaOverwatchService overwatch, ArenaMeleeService melee)
        {
            _ctx = ctx;
            _targeting = targeting;
            _overwatch = overwatch;
            _melee = melee;
        }

        public bool ExecuteAttackOnObstacle(
            HexCoordinates targetCoords,
            SkillType attackSkill = SkillType.ManiementArmes,
            TacticalUnit explicitAttacker = null)
        {
            var turnManager = _ctx.TurnManager;
            var grid = _ctx.Grid;
            var visualizer = _ctx.Visualizer;
            if (_ctx.Guard.IsResolving) return false;
            var attacker = explicitAttacker != null ? explicitAttacker : (turnManager != null ? turnManager.ActiveUnit : _state.PlayerUnit);
            if (attacker == null || attacker.Stats == null || !attacker.Stats.IsAlive) return false;
            if (attacker.IsMoving) return false;

            int cost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.BaseAttackAPCost : 2;
            if (!_ctx.GetInfiniteAP() && attacker.Stats.CurrentActionPoints < cost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : attaque de décor = {cost} PA (reste {attacker.Stats.CurrentActionPoints}).");
                return false;
            }

            var weapon = attacker.Sheet?.GetEquippedWeapon();
            int dmg = weapon != null && weapon.BaseDamage > 0 ? weapon.BaseDamage : 5;
            bool isMelee = SkillDefinitions.IsMeleeAttackSkill(attackSkill) || (weapon != null && weapon.RangeInTiles <= 1);
            int dist = attacker.CurrentCoords.DistanceTo(targetCoords);
            int maxReach = weapon != null ? Mathf.Max(1, weapon.RangeInTiles) : 1;

            if (dist > maxReach)
            {
                _host.ArenaLog($"⚠️ Obstacle hors de portée ({dist} cases > max {maxReach}).");
                return false;
            }

            ObstacleDamageType dmgType = ObstacleDamageType.Perforant;
            if (isMelee)
            {
                dmgType = (attackSkill == SkillType.ManiementArmes && attacker.Stats.HasSpecialization("Marteau de Guerre"))
                    ? ObstacleDamageType.Contondant
                    : (attacker.Stats.HasSpecialization("Hache de Guerre") ? ObstacleDamageType.Tranchant : ObstacleDamageType.Contondant);
            }
            else if (weapon != null && weapon.Category.Contains("Laser"))
            {
                dmgType = ObstacleDamageType.Thermique;
            }

            if (!_ctx.GetInfiniteAP()) attacker.Stats.ConsumeActionPoints(cost);
            attacker.Stats.RegisterAttack();

            Vector3 dir = targetCoords.ToWorldPosition(grid != null ? grid.HexRadius : 1f, 0f) - attacker.transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero) attacker.transform.rotation = Quaternion.LookRotation(dir);

            var attVis = attacker.GetComponent<TacticalUnitVisual>();
            attVis?.TriggerRoundkick();

            if (PropObstacleRegistry.ApplyDamage(targetCoords, dmg, dmgType, grid, visualizer, out var obsRes))
            {
                _host.ArenaLog($"🎯 <b>{attacker.Stats.Name}</b> attaque le décor en ({targetCoords.Q}, {targetCoords.R}) (-{cost} PA) :\n   {obsRes.Log}");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, attacker.transform.position, 0.7f);
                return true;
            }
            else
            {
                _host.ArenaLog($"🎯 <b>{attacker.Stats.Name}</b> frappe en ({targetCoords.Q}, {targetCoords.R}) : aucun obstacle destructible.");
                return false;
            }
        }

        public void ExecuteGrapnel(TacticalUnit attacker, TacticalUnit defender)
        {
            var turnManager = _ctx.TurnManager;
            var calculator = _ctx.Calculator;
            if (_ctx.Guard.IsResolving)
            {
                _host.ArenaLog("⚠️ Lancer ignoré : une résolution est déjà en cours.");
                return;
            }
            if (TurnManager.IsMultiplayerPlayerClient())
            {
                _host.ArenaLog("⚠️ Grappin réservé au GM en multijoueur (pas encore synchronisé VTT).");
                return;
            }
            if (attacker == null) attacker = turnManager != null ? turnManager.ActiveUnit : _state.PlayerUnit;
            if (attacker == null || attacker.Stats == null) { _host.ArenaLog("⚠️ Aucun attaquant pour le grappin."); return; }
            if (defender == null || defender.Stats == null || !defender.Stats.IsAlive) { _host.ArenaLog("⚠️ Aucune cible valide pour le grappin."); return; }
            if (attacker.IsMoving) { _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> est en déplacement : attendez l'arrivée !"); return; }
            if (SmokeScreen.FindItemByName(attacker.Stats, "Grappin") == null)
            {
                _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> n'a pas de Grappin Magnétique en poche !");
                return;
            }
            int dist = attacker.CurrentCoords.DistanceTo(defender.CurrentCoords);
            if (dist < 2 || dist > 4)
            {
                _host.ArenaLog($"⚠️ <b>Grappin impossible</b> : {dist} cases (portée 2-4).");
                return;
            }
            DiceType atkDie = attacker.Stats.GetSkillDie(SkillType.Ballistique, true);
            if (!attacker.Stats.CanAttack(atkDie)) { _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> a épuisé son quota d'attaque ce tour !"); return; }
            if (!_ctx.GetInfiniteAP() && attacker.Stats.CurrentActionPoints < 2)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : grappin = 2 PA (reste {attacker.Stats.CurrentActionPoints}).");
                return;
            }

            Vector3 dir = defender.transform.position - attacker.transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero) attacker.transform.rotation = Quaternion.LookRotation(dir);
            attacker.GetComponent<TacticalUnitVisual>()?.TriggerGrenadeThrow();

            calculator.SetContactDistanceState(false);
            CoverType cover = _targeting.GetCoverToTarget(attacker, defender);
            var throwDuel = calculator.BeginSkillDuel(
                attacker.Stats, defender.Stats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive, 3, false,
                true, null, null,
                defender.Stats.BaseArmorAbsorption, out string throwError, cover);
            if (throwDuel == null)
            {
                _host.ArenaLog(throwError ?? "⚠️ Grappin impossible : PA insuffisants.");
                return;
            }
            calculator.DeclareAttackerStakes(throwDuel, 0, 0);
            if (throwDuel.DefenderWantsToDefend && !throwDuel.IsDefenderIncapacitated && throwDuel.CanDefenderReact)
                calculator.AutoDeclareDefenderStakes(throwDuel);
            else
                calculator.DeclareDefenderStakes(throwDuel, SkillType.Esquive, false, 0, 0);
            var result = calculator.ResolveBlindDuel(throwDuel);
            attacker.Stats.RegisterAttack();

            _host.ArenaLog($"🪝 <b>HARPON</b> : {attacker.Stats.Name} harponne {defender.Stats.Name} ({dist} cases, -2 PA)\n   {result.CombatLog}");
            _overwatch.HandleChannelInterruptionFx(defender, result.ChannelInterrupted);
            var defVis = defender.GetComponent<TacticalUnitVisual>();
            if (result.IsHit) defVis?.TriggerHitFlash();
            if (result.IsHit && result.Differential >= 2)
            {
                defender.Stats.ApplyStatus(StatusEffect.ATerre, 1);
                defVis?.SpawnFloatingText("[À TERRE] happé !", Color.yellow);
                _host.ArenaLog($"🪝 Le grappin fait chuter <b>{defender.Stats.Name}</b> (<b>À Terre</b>, Diff +{result.Differential}).");
            }
            if (!defender.Stats.IsAlive) _host.RequestAutoTargetNextAlive();
            _host.RequestMusicUpdate();
            _host.RequestCombatOverCheck();
        }

        /// <summary>
        /// Déclenche une passe d'armes ou tir ciblé selon le duel aveugle officiel (Livres II §7 + VI §24.1) :
        /// déclaration attaquant (mise PA/PE cachée) -> déclaration défenseur à l'aveugle
        /// -> révélation simultanée -> résolution normale. Conforme à l'Axiome Fondateur : résolution par compétence.
        /// defenderBonusAP &lt; 0 = défense réactive auto (besoin réel après révélation de l'attaque).
        /// </summary>
        public void ExecuteAttack(
            BodyPart targetedPart,
            bool cancelPenaltyWithAP,
            SkillType attackSkill = SkillType.Ballistique,
            SkillType defenseSkill = SkillType.Esquive,
            bool defenderWantsToDefend = true,
            string attackerSpecialization = null,
            string defenderSpecialization = null,
            int weaponBaseDamage = 5,
            int attackerBonusAP = 0,
            int defenderBonusAP = -1,
            DiceType? attackDie = null,
            DiceType? defenseDie = null,
            TacticalUnit explicitTarget = null,
            int attackerPE = 0,
            int defenderPE = 0,
            TacticalUnit explicitAttacker = null)
        {
            var turnManager = _ctx.TurnManager;
            var grid = _ctx.Grid;
            var visualizer = _ctx.Visualizer;
            var calculator = _ctx.Calculator;
            if (_ctx.Guard.IsResolving)
            {
                _host.ArenaLog("⚠️ Attaque ignorée : une résolution est déjà en cours (anti double-clic).");
                return;
            }

            // Déclaration de l'attaquant : mise PA (+ PE) engagée avant tout jet, cachée.
            if (attackerBonusAP <= 0 && CombatUI.CombatContextMenuUI.CurrentInjectedAP > 0)
            {
                attackerBonusAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            }
            if (attackerPE <= 0 && CombatUI.CombatContextMenuUI.CurrentInjectedPE > 0)
            {
                attackerPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;
            }

            // Cohérence affichage/exécution : le menu contextuel calcule les coûts depuis
            // GetActiveActor() (unité active en combat). Si l'appelant fournit l'attaquant
            // explicite (menu radial), il fait foi ; sinon repli sur l'unité active.
            // Avant, l'exécution ignorait toujours l'acteur du menu, d'où un attaquant à
            // 0 PA pour la cible de derrière alors que le menu affichait une unité pleine.
            var attacker = explicitAttacker != null ? explicitAttacker : turnManager.ActiveUnit;
            var defender = explicitTarget != null ? explicitTarget : _state.CurrentTarget;

            if (defender != null && _state.CurrentTarget != defender)
            {
                _targeting.SelectTarget(defender);
            }

            if (turnManager != null && turnManager.IsInExploration)
            {
                _host.ArenaLog("🚨 <b>COUP DE FEU EN EXPLORATION !</b> Passage immédiat en combat tactique.");
                turnManager.EnterCombatMode(attacker);
            }

            if (TurnManager.IsMultiplayerPlayerClient())
            {
                if (attacker != null && defender != null)
                {
                    Killtime.Multi.VTTTableSync.Instance?.RequestAttack(
                        attacker,
                        defender,
                        targetedPart,
                        cancelPenaltyWithAP,
                        attackSkill,
                        defenseSkill,
                        attackerBonusAP,
                        attackerPE
                    );
                }
                return;
            }

            if (attacker == null || defender == null)
            {
                _host.ArenaLog("⚠️ Impossible d'attaquer : aucune cible active.");
                return;
            }

            if (!defender.Stats.IsAlive)
            {
                _host.ArenaLog($"⚠️ {defender.Stats.Name} est déjà hors de combat !");
                return;
            }

            // Garde anti-état transitoire : les CurrentCoords ne sont fiables qu'une fois
            // le déplacement terminé (MoveAlongPath met à jour case par case). Attaquer
            // en plein mouvement figeait une distance/skill obsolète (ex : mêlée jouée
            // alors que l'unité venait de sortir de portée).
            if (attacker.IsMoving)
            {
                _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> est en déplacement : attendez l'arrivée avant d'attaquer !");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            int distanceToTarget = TitanFootprint.MinDistanceBetweenUnits(attacker.CurrentCoords, attacker.FootprintType, defender.CurrentCoords, defender.FootprintType);
            var activeWeapon = attacker.Sheet?.GetEquippedWeapon();
            bool hasRifleVisual = attacker.GetComponent<TacticalUnitVisual>()?.HasRifleEquipped() == true;
            bool hasRangedArmamentForCorrection = (activeWeapon != null && activeWeapon.RangeInTiles > 1) || hasRifleVisual;

            // Auto-correction autoritaire : une compétence de mêlée figée (menu dév,
            // menu contextuel, HUD dessiné avant fin de déplacement) ne doit jamais
            // produire une animation de coup de poing à distance. Hors de portée de
            // contact avec une arme à distance disponible => bascule en Ballistique.
            if (distanceToTarget > 1 && hasRangedArmamentForCorrection
                && SkillDefinitions.IsMeleeAttackSkill(attackSkill))
            {
                _host.ArenaLog($"🔫 <b>{attacker.Stats.Name}</b> hors de contact ({distanceToTarget} cases) : bascule automatique en Tir (Ballistique).");
                attackSkill = SkillType.Ballistique;
            }

            bool isMeleeAttack = (SkillDefinitions.IsMeleeAttackSkill(attackSkill)
                               || (activeWeapon != null && activeWeapon.RangeInTiles <= 1));

            int maxReach = activeWeapon != null ? Mathf.Max(1, activeWeapon.RangeInTiles) : 1;
            if (hasRifleVisual && maxReach < 6) maxReach = 8;

            // RD-036 Hauteur / contrebas (palier 1.5 m) : gate verticale en
            // mêlée (inatteignable au-delà d'1 palier sans allonge, 2 avec),
            // bonus/malus au jet sinon. Le tir n'a pas de gate (vue tranche).
            ElevationRuling elevationRuling = _targeting.GetElevationRuling(attacker, defender, attackSkill, activeWeapon, isMeleeAttack);
            if (isMeleeAttack && !elevationRuling.Reachable)
            {
                _host.ArenaLog($"⛰️ <b>Hors d'atteinte verticale</b> : {defender.Stats.Name} est {elevationRuling.Label} pour {attacker.Stats.Name} — rapprochez-vous du même palier ou utilisez une arme à allonge / à distance !");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }
            if (elevationRuling.AttackMod != 0)
            {
                _host.ArenaLog($"⛰️ <b>Hauteur</b> : {attacker.Stats.Name} ➔ {defender.Stats.Name} {elevationRuling.Label} ({elevationRuling.AttackMod:+0;-0} attaque).");
            }

            if (isMeleeAttack && distanceToTarget > maxReach)
            {
                _host.ArenaLog($"⚠️ <b>Attaque de contact impossible</b> : {defender.Stats.Name} est hors de portée ({distanceToTarget} cases > portée max {maxReach}) !");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            bool isRangedAttackSkill = (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir);
            bool hasRangedArmament = (activeWeapon != null && activeWeapon.RangeInTiles > 1) || hasRifleVisual;

            // RD-038 : Le sprint bloque tout tir à distance ce tour
            if ((isRangedAttackSkill || (!isMeleeAttack && distanceToTarget > 1)) && !ChargeState.CanFireRanged(attacker.Stats))
            {
                _host.ArenaLog($"⚠️ <b>Tir impossible</b> : {attacker.Stats.Name} a sprinté ce tour ! (RD-038 : Le sprint bloque le tir)");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            if (isRangedAttackSkill && !hasRangedArmament && distanceToTarget > 1)
            {
                _host.ArenaLog($"⚠️ <b>Tir impossible</b> : {attacker.Stats.Name} ne possède aucune arme à distance ({distanceToTarget} cases) !");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            if (!isMeleeAttack && distanceToTarget > maxReach)
            {
                _host.ArenaLog($"⚠️ <b>Tir impossible</b> : {defender.Stats.Name} est hors de portée ({distanceToTarget} cases > portée max {maxReach}) !");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            // Livre VI §25.3 — Couvert & Visibilité : la cible totalement à couvert
            // (non visible pour l'attaquant) ne peut pas être attaquée directement.
            CoverType attackCover = _targeting.GetCoverToTarget(attacker, defender);
            if (attackCover == CoverType.Full)
            {
                _host.ArenaLog($"🛡️ <b>Couvert total</b> : {defender.Stats.Name} est non visible pour {attacker.Stats.Name} — attaque impossible ! Déplacez-vous pour retrouver une ligne de mire.");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            DiceType resolvedAttackDie = attackDie ?? attacker.Stats.GetSkillDie(attackSkill, true);
            DiceType resolvedDefenseDie = defenseDie ?? defender.Stats.GetSkillDie(defenseSkill);

            int maxAttacks = attacker.Stats.GetMaxAttacksAllowed(resolvedAttackDie);
            if (!attacker.Stats.CanAttack(resolvedAttackDie))
            {
                _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> a déjà épuisé son quota d'attaque ce tour ({attacker.Stats.AttacksThisTurn}/{maxAttacks}) ! Requis pour 2 attaques : palier 2d6, 2d8, 2d10 ou 2d12.");
                return;
            }

            // RD-033 Munitions : un tir consomme 1 coup ; vide ou enrayé = refus sans PA.
            // Mêlée au contact avec l'arme (coup de crosse) : aucun coup consommé.
            bool ammoFiredHere = WeaponAmmo.UsesAmmo(activeWeapon, attackSkill);
            if (ammoFiredHere)
            {
                if (activeWeapon.Jammed)
                {
                    _host.ArenaLog($"🔧 <b>Enrayée</b> : {activeWeapon.Name} de {attacker.Stats.Name} est enrayée ! Désenrayez (1 PA).");
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                    return;
                }
                if (activeWeapon.AmmoRemaining <= 0)
                {
                    _host.ArenaLog($"🔋 <b>Chargeur vide</b> : {activeWeapon.Name} ({attacker.Stats.Name}) — rechargez ({activeWeapon.ReloadAPCost} PA).");
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                    return;
                }
            }

            // Duel aveugle Livres II §7 + VI §24.1 : les mises (PA bonus + PE) se déclarent
            // AVANT les jets et restent cachées jusqu'à la révélation simultanée.
            // Bonus explicite (>= 0) : mise déclarée du défenseur (fenêtre paramétrage).
            // Bonus auto (< 0) : déclaration aveugle auto (estimation sans voir le jet adverse).
            bool autoReactiveDefense = defenderBonusAP < 0;
            int resolvedDefenderBonus = autoReactiveDefense ? 0 : Mathf.Max(0, defenderBonusAP);

            Vector3 combatDir = defender.transform.position - attacker.transform.position;
            combatDir.y = 0f;
            if (combatDir != Vector3.zero)
            {
                attacker.transform.rotation = Quaternion.LookRotation(combatDir);
                if (defender.Stats.CanDefendActively())
                {
                    defender.transform.rotation = Quaternion.LookRotation(-combatDir);
                }
            }

            var attVisual = attacker.GetComponent<TacticalUnitVisual>();
            var defVisual = defender.GetComponent<TacticalUnitVisual>();
            bool isMeleeStrike = SkillDefinitions.IsMeleeAttackSkill(attackSkill);

            // Brouillard de guerre asymétrique : un tir bruyant révèle le tireur
            // dans le rayon d'Ouïe jusqu'à la fin du round suivant (sauf mur Full).
            // La mêlée reste silencieuse (pas de révélation).
            if (!isMeleeStrike)
            {
                try { Killtime.Tactics.Visibility.FogOfWarManager.Instance?.NotifyLoudShot(attacker); }
                catch { /* fog optionnel */ }
            }

            bool isAttackerStealthed = StealthState.IsStealthed(attacker.Stats);

            // Recalcul au moment du déclenchement : la distance a pu changer entre le clic
            // et l'impact (fin de déplacement, cinématique). Le visuel doit refléter la
            // position réelle, pas celle figée au clic.
            Func<bool> isMeleeAtTrigger = () =>
            {
                if (attacker == null || defender == null) return isMeleeStrike;
                int freshDist = attacker.CurrentCoords.DistanceTo(defender.CurrentCoords);
                bool skillIsMelee = SkillDefinitions.IsMeleeAttackSkill(attackSkill);
                return skillIsMelee && freshDist <= 1;
            };

            Action triggerKickAction = () =>
            {
                if (isMeleeAtTrigger())
                {
                    attVisual?.TriggerRoundkick(forceKick: true);
                }
                else
                {
                    attVisual?.TriggerRoundkick();
                }

                if (KilltimeAudioManager.Instance != null && attacker != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, attacker.transform.position, 0.7f);
            };

            Action triggerDefenseAction = () =>
            {
                if (!isAttackerStealthed && defender != null && defender.Stats != null && defender.Stats.CanDefendActively())
                {
                    defVisual?.TriggerBodyBlock();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Defense_Block, defender.transform.position, 0.5f);
                }
            };

            TacticalUnit aiInterceptionProtector = null;
            if (!isAttackerStealthed && (!defender.IsPlayerControlled || (_ctx.AI != null && _ctx.AI.Mode == CombatAIMode.FullAuto)))
            {
                var aiProtector = Killtime.Tactics.CombatUI.CombatTechniqueRegistry.FindBestAIProtector(defender);
                // ExecuteIntercept exige la façade concrète (Log + snapshot + progression).
                // L'hôte EST la façade (seul implémenteur d'IArenaHost) : cast sûr.
                var interceptArena = aiProtector != null ? _ctx.Host as CombatDevArena : null;
                if (interceptArena != null)
                {
                    if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteIntercept(aiProtector, defender, interceptArena))
                    {
                        aiInterceptionProtector = aiProtector;
                        defender = aiProtector;
                        defVisual = defender.GetComponent<TacticalUnitVisual>();
                    }
                }
            }

            bool hasPlayerProtector = !isAttackerStealthed && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.HasPlayerControlledProtector(defender);
            bool isInteractivePlayerDefense = !isAttackerStealthed
                && (defender.IsPlayerControlled || hasPlayerProtector)
                && (_ctx.AI == null || _ctx.AI.Mode != CombatAIMode.FullAuto)
                && defenderBonusAP < 0
                && !TurnManager.IsMultiplayerPlayerClient();

            if (isInteractivePlayerDefense)
            {
                _ctx.Guard.TryEnter();
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Fire, 0.85f);

                _host.ArenaStartCoroutine(ExecuteAttackWithPlayerDefenseRoutine(
                    attacker, defender, targetedPart, cancelPenaltyWithAP,
                    attackSkill, defenseSkill, weaponBaseDamage, attackerBonusAP, attackerPE,
                    attackerSpecialization, defenderSpecialization, triggerKickAction, triggerDefenseAction));
                return;
            }

            Action resolveAction = () =>
            {
                attacker.Stats.RegisterAttack();

                int effectiveWeaponDamage = weaponBaseDamage;
                var equippedWeapon = attacker.Sheet?.GetEquippedWeapon();
                if (equippedWeapon != null && equippedWeapon.BaseDamage > 0)
                {
                    effectiveWeaponDamage = equippedWeapon.BaseDamage;
                }
                int weaponBonusEc = equippedWeapon != null ? Math.Max(0, equippedWeapon.AttackBonusEc) : 0;
                // RD-033 : charge Haute Densité chambrée = +1 dégât (sniper/Deglazer).
                bool hdShot = ammoFiredHere && equippedWeapon != null && equippedWeapon.LoadedHD;
                if (hdShot) effectiveWeaponDamage += 1;

                calculator.SetContactDistanceState(attacker.IsCanonEntrave());

                // Recalcul frais au moment de l'impact (positions réelles) : si la
                // cible est passée derrière un couvert total entre le clic et la
                // frappe, BeginDuel refusera et le log l'expliquera (aucun jet).
                CoverType freshCover = _targeting.GetCoverToTarget(attacker, defender);

                DamageResult result;
                AttackDuel duel = null;
                if (autoReactiveDefense)
                {
                    string duelError = null;
                    if (attackDie.HasValue || defenseDie.HasValue)
                    {
                        DiceType reactiveDefDie = resolvedDefenseDie;
                        if (!SkillDefinitions.IsExclusivelyDefensive(defenseSkill))
                        {
                            bool hasDefSpec = (!string.IsNullOrEmpty(defenderSpecialization) && defender.Stats.HasSpecialization(defenderSpecialization))
                                            || SkillDefinitions.HasDefensiveSpecialization(defender.Stats.Sheet, defenseSkill);
                            if (!defenseDie.HasValue && !hasDefSpec)
                                reactiveDefDie = SkillDefinitions.StepDownDie(reactiveDefDie);
                        }
                        duel = calculator.BeginDuel(
                            attacker.Stats, defender.Stats, targetedPart,
                            resolvedAttackDie, attacker.Stats.GetSkillModifier(attackSkill, isOffensive: true),
                            reactiveDefDie, defender.Stats.GetSkillModifier(defenseSkill, isOffensive: false),
                            effectiveWeaponDamage, cancelPenaltyWithAP, defenderWantsToDefend,
                            defender.Stats.BaseArmorAbsorption, attackSkill, defenseSkill, out duelError, freshCover, weaponBonusEc,
                            false, elevationRuling.AttackMod, elevationRuling.Label);
                    }
                    else
                    {
                        duel = calculator.BeginSkillDuel(
                            attacker.Stats, defender.Stats, targetedPart,
                            attackSkill, defenseSkill, effectiveWeaponDamage, cancelPenaltyWithAP,
                            defenderWantsToDefend, attackerSpecialization, defenderSpecialization,
                            defender.Stats.BaseArmorAbsorption, out duelError, freshCover, weaponBonusEc,
                            false, elevationRuling.AttackMod, elevationRuling.Label);
                    }
                    if (duel == null)
                    {
                        _host.ArenaLog(duelError ?? "⚠️ Attaque impossible : PA insuffisants pour le coût de base.");
                        return;
                    }
                    else
                    {
                        if (aiInterceptionProtector != null)
                        {
                            duel.DefenderBasePaid = true;
                            duel.CanDefenderReact = !duel.IsDefenderIncapacitated;
                        }

                        // Duel aveugle auto : déclaration attaquant (mise cachée), puis
                        // déclaration aveugle auto du défenseur (estimation sans voir le jet),
                        // puis révélation simultanée. Aucun résultat révélé entre les deux.
                        calculator.DeclareAttackerStakes(duel, attackerBonusAP, attackerPE);
                        if (duel.DefenderWantsToDefend && !duel.IsDefenderIncapacitated && duel.CanDefenderReact)
                        {
                            calculator.AutoDeclareDefenderStakes(duel, defenderSpecialization);
                        }
                        else
                        {
                            calculator.DeclareDefenderStakes(duel, defenseSkill, false, 0, 0, defenderSpecialization);
                        }
                        result = calculator.ResolveBlindDuel(duel);
                    }
                }
                else if (attackDie.HasValue || defenseDie.HasValue)
                {
                    result = calculator.ResolveTargetedAttack(
                        attacker: attacker.Stats,
                        defender: defender.Stats,
                        targetedPart: targetedPart,
                        attackDie: resolvedAttackDie,
                        attackModifier: attacker.Stats.GetSkillModifier(attackSkill, isOffensive: true),
                        defenseDie: resolvedDefenseDie,
                        defenseModifier: defender.Stats.GetSkillModifier(defenseSkill, isOffensive: false),
                        weaponBaseDamage: effectiveWeaponDamage,
                        cancelPenaltyWithAP: cancelPenaltyWithAP,
                        defenderArmor: defender.Stats.BaseArmorAbsorption,
                        attackerBonusAP: attackerBonusAP,
                        defenderBonusAP: resolvedDefenderBonus,
                        defenderWantsToDefend: defenderWantsToDefend,
                        attackSkill: attackSkill,
                        defenseSkill: defenseSkill,
                        attackerPE: attackerPE,
                        defenderPE: defenderPE,
                        cover: freshCover,
                        weaponBonusEc: weaponBonusEc
                    );
                }
                else
                {
                    result = calculator.ResolveTargetedAttack(
                        attacker: attacker.Stats,
                        defender: defender.Stats,
                        targetedPart: targetedPart,
                        attackSkill: attackSkill,
                        defenseSkill: defenseSkill,
                        weaponBaseDamage: effectiveWeaponDamage,
                        cancelPenaltyWithAP: cancelPenaltyWithAP,
                        defenderWantsToDefend: defenderWantsToDefend,
                        attackerSpecialization: attackerSpecialization,
                        defenderSpecialization: defenderSpecialization,
                        defenderArmor: defender.Stats.BaseArmorAbsorption,
                        attackerBonusAP: attackerBonusAP,
                        defenderBonusAP: resolvedDefenderBonus,
                        attackerPE: attackerPE,
                        defenderPE: defenderPE,
                        cover: freshCover,
                        weaponBonusEc: weaponBonusEc
                    );
                }

                _host.ArenaLog(result.CombatLog);
                _overwatch.HandleChannelInterruptionFx(defender, result.ChannelInterrupted);

                // Dégâts collatéraux sur l'obstacle si l'attaque est absorbée par le couvert ou neutralisée
                if (result.IsBlocked && freshCover != CoverType.None && grid != null)
                {
                    ObstacleDamageType directDmgType = ObstacleDamageType.Perforant;
                    if (isMeleeAtTrigger())
                    {
                        directDmgType = (attackSkill == SkillType.ManiementArmes && attacker.Stats.HasSpecialization("Marteau de Guerre"))
                            ? ObstacleDamageType.Contondant
                            : (attacker.Stats.HasSpecialization("Hache de Guerre") ? ObstacleDamageType.Tranchant : ObstacleDamageType.Contondant);
                    }
                    else if (equippedWeapon != null && equippedWeapon.Category.Contains("Laser"))
                    {
                        directDmgType = ObstacleDamageType.Thermique;
                    }

                    if (CoverSystem.ScanCover(attacker.CurrentCoords, defender.CurrentCoords, grid, smokeZones: _state.SmokeZones, seesThroughSmoke: ArenaSmokeService.SeesThroughSmoke(attacker)).Rays is var rays && rays != null)
                    {
                        for (int rIdx = 0; rIdx < rays.Count; rIdx++)
                        {
                            if (rays[rIdx].Blocked && rays[rIdx].HasBlockingCell)
                            {
                                if (PropObstacleRegistry.ApplyDamage(rays[rIdx].BlockingCell, effectiveWeaponDamage, directDmgType, grid, visualizer, out var obsRes))
                                {
                                    _host.ArenaLog($"   🛡️ <b>IMPACT COLLATÉRAL</b> : {obsRes.Log}");
                                }
                                break;
                            }
                        }
                    }
                }

                _melee.TryDisarmDefender(attacker, defender, targetedPart, result);
                if (ammoFiredHere && equippedWeapon != null && result.AttackRoll.RawRoll > 0)
                    _melee.ConsumeShotAndCheckJam(attacker, equippedWeapon, result, hdShot);

                var laserWeapon = attacker.GetComponentInChildren<LaserRifleWeapon>();
                // Un roundkick / une frappe au corps-à-corps (Mains Nues, Maniement,
                // Perçantes) ne doit jamais déclencher le rayon laser + sons de tir, même si un
                // laser est équipé visuellement. Seul un vrai tir (Ballistique) tire au laser.
                // Vérification sur positions fraîches : si l'attaquant s'est éloigné entre le
                // clic et l'impact, on tire au laser ; s'il est revenu au contact avec une
                // compétence de mêlée, on reste en coup de poing.
                bool freshMeleeAtImpact = isMeleeAtTrigger();
                if (attackSkill == SkillType.Ballistique && !freshMeleeAtImpact && laserWeapon != null && defender != null)
                {
                    Vector3 targetCenter = defender.transform.position + Vector3.up * 1.15f;
                    laserWeapon.FireLaser(targetCenter, result.IsHit);
                }

                // Télémétrie d'engagement
                bool isTargetDeadOrDown = !defender.Stats.IsAlive || result.FatalResolution != FatalBlowResolution.None;
                CombatHUD.RecordCombatAction(
                    attacker.Stats.Name,
                    defender.Stats.Name,
                    attacker.IsPlayerControlled,
                    defender.IsPlayerControlled,
                    result.IsHit,
                    result.IsCritical,
                    result.IsBlocked,
                    result.RawDamage,
                    result.ArmorAbsorbed,
                    result.FinalDamageApplied,
                    result.ExceededEncaissement,
                    isTargetDeadOrDown,
                    (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP
                );

                // SFX combat : mapping complet du résultat (hybride procédural/clips).
                if (KilltimeAudioManager.Instance != null && defender != null)
                {
                    KilltimeAudioManager.Instance.PlayCombatResult(result, defender.transform.position);
                    KilltimeAudioManager.Instance.Play(SoundId.Dice_Roll, 0.35f);
                }

                // Affichage du texte flottant sur l'attaquant à l'impact exact
                if (attVisual != null)
                {
                    int totalCost = (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP;
                    if (isAttackerStealthed)
                    {
                        attVisual.SpawnFloatingText($"🥷 EMBUSCADE (+2 Attaque)", new Color(0.2f, 0.95f, 0.75f));
                    }
                    else
                    {
                        attVisual.SpawnFloatingText($"Attaque {targetedPart} (-{totalCost} PA)", new Color(0.3f, 0.8f, 1.0f));
                    }
                }

                // Textes flottants et retours visuels sur le défenseur à l'impact exact
                if (defVisual != null)
                {
                    if (result.IsHit)
                    {
                        defVisual.TriggerHitFlash();

                        if (result.IsCritical)
                        {
                            defVisual.SpawnFloatingText("[CRITIQUE] COUP DÉCISIF !", new Color(1.0f, 0.85f, 0.1f));
                        }

                        if (isAttackerStealthed)
                        {
                            defVisual.SpawnFloatingText("SURPRIS ! (0 réaction)", Color.yellow);
                        }

                        if (result.WasDeflected)
                        {
                            defVisual.SpawnFloatingText($"[DÉVIATION] -> {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff 0)", Color.yellow);
                        }
                        else
                        {
                            string diffSign = result.Differential > 0 ? $"+{result.Differential}" : $"{result.Differential}";
                            defVisual.SpawnFloatingText($"[TOUCHÉ] {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff {diffSign})", new Color(0.9f, 0.9f, 1.0f));
                        }

                        string dmgText = (result.ArmorAbsorbed > 0 || result.ShieldAbsorbed > 0)
                            ? $"-{result.FinalDamageApplied} PV (Bruts {result.RawDamage} | Armure -{result.ArmorAbsorbed}" + (result.ShieldAbsorbed > 0 ? $" | Bouclier -{result.ShieldAbsorbed}" : "") + ")"
                            : $"-{result.FinalDamageApplied} PV";
                        defVisual.SpawnFloatingText(dmgText, new Color(1.0f, 0.25f, 0.25f));

                        if (result.ExceededEncaissement)
                        {
                            defVisual.SpawnFloatingText($"[CHOC] TRAUMATIQUE ! [{result.InflictedStatus}]", new Color(1.0f, 0.4f, 0.95f));
                        }

                        if (result.CausedKnockback)
                        {
                            _melee.TryApplyKnockback(attacker, defender);
                        }

                        if (result.MoraleResult.Triggered)
                        {
                            if (result.MoraleResult.Surrendered)
                            {
                                defVisual.SpawnFloatingText("🏳️ REDDITION !", Color.white);
                                defVisual.DropHeldItemsWithPhysics();
                            }
                            else if (result.MoraleResult.Routed)
                            {
                                defVisual.SpawnFloatingText("😱 DÉROUTE ! [Agonisant]", new Color(0.95f, 0.2f, 0.85f));
                            }
                            else
                            {
                                defVisual.SpawnFloatingText("🧠 Sang-froid préservé", Color.cyan);
                            }
                        }

                        if (result.FatalResolution == FatalBlowResolution.InstantDeath)
                        {
                            defVisual.SpawnFloatingText("[MORTEL] INSTANTANÉ !", Color.black);
                            defVisual.TriggerFallingBackDeath();
                        }
                        else if (result.FatalResolution == FatalBlowResolution.MiracleSaved)
                        {
                            defVisual.SpawnFloatingText("[MIRACLE] DESTIN SAUVÉ ! (1 PV)", new Color(1.0f, 0.85f, 0.1f));
                            defVisual.TriggerFallingBackDeath();
                        }
                        else if (result.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                        {
                            defVisual.SpawnFloatingText("[K.O.] SYNCOPE TRAUMATIQUE !", Color.magenta);
                            defVisual.TriggerFallingBackDeath();
                        }
                        else if (result.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                        {
                            if (!defender.IsPlayerControlled)
                            {
                                defender.Stats.ChooseSombrer();
                                defVisual.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                                defVisual.TriggerFallingBackDeath();
                            }
                            else
                            {
                                defender.Stats.ChooseLastBreath();
                                defVisual.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                            }
                        }
                    }
                    else if (result.IsBlocked)
                    {
                        defVisual.SpawnFloatingText($"[PARADE] Neutralisation (Diff {result.Differential})", new Color(0.2f, 0.9f, 1.0f));
                    }
                }

                // Pont WebGL
                WebBridgeManager.Instance?.SendCombatEvent(
                    result.IsHit ? "HIT" : "MISS",
                    result.CombatLog,
                    result.FinalDamageApplied
                );

                SnapshotRecorder?.Invoke($"Attaque sur {defender.Stats.Name} ({targetedPart})");

                // Diffusion VTT Multijoueur si GM
                if (Killtime.Multi.VTTTableSync.Instance != null
                    && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction
                    && Killtime.Multi.VTTRoomManager.Instance != null
                    && Killtime.Multi.VTTRoomManager.Instance.InRoom
                    && Killtime.Multi.VTTRoomManager.Instance.IsGM)
                {
                    var actionPayload = new Killtime.Multi.VTTCombatActionPayload
                    {
                        action = "attack",
                        actorId = Killtime.Multi.VTTTableSync.UnitIdOf(attacker),
                        targetId = Killtime.Multi.VTTTableSync.UnitIdOf(defender),
                        targetedPart = (int)targetedPart,
                        actualHitPart = (int)result.ActualHitPart,
                        attackSkill = (int)attackSkill,
                        defenseSkill = (int)defenseSkill,
                        isHit = result.IsHit,
                        isCritical = result.IsCritical,
                        wasDeflected = result.WasDeflected,
                        finalDamageApplied = result.FinalDamageApplied,
                        rawDamage = result.RawDamage,
                        armorAbsorbed = result.ArmorAbsorbed,
                        differential = result.Differential,
                        inflictedStatus = result.InflictedStatus.ToString(),
                        isMeleeStrike = freshMeleeAtImpact,
                        attackerCostAP = (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP,
                        defenderCostAP = (duel != null && duel.DefenderBasePaid ? duel.BaseDefenseCost : 0) + (duel != null ? duel.DefenderBonusPAApplied : resolvedDefenderBonus),
                        attackerPE = duel != null ? duel.AttackerPEApplied : 0,
                        defenderPE = duel != null ? duel.DefenderPEApplied : 0,
                        attackerNewAP = attacker.Stats.CurrentActionPoints,
                        defenderNewAP = defender.Stats.CurrentActionPoints,
                        defenderNewHealth = defender.Stats.CurrentHealth,
                        defenderNewActiveStatus = (int)defender.Stats.ActiveStatus,
                        defenderIsDead = defender.Stats.IsDead || !defender.Stats.IsAlive,
                        actorRotationY = attacker.transform.rotation.eulerAngles.y,
                        defenderRotationY = defender.transform.rotation.eulerAngles.y,
                        defenderNewQ = defender.CurrentCoords.Q,
                        defenderNewR = defender.CurrentCoords.R,
                        combatLog = result.CombatLog
                    };
                    Killtime.Multi.VTTTableSync.Instance.BroadcastCombatAction(actionPayload);
                    Killtime.Multi.VTTTableSync.Instance.BroadcastFullCombatState();
                }

                if (!defender.Stats.IsAlive || defender.Stats.IsSurrendered)
                {
                    _host.RequestAutoTargetNextAlive();
                }

                // RD-049 : rupture de furtivité répercutée immédiatement sur le brouillard
                if (isAttackerStealthed)
                {
                    Killtime.Tactics.Visibility.FogOfWarManager.Instance?.RefreshFog("stealth-broken");
                }

                // RD-030 : l'attaque en zone surveillée expose l'attaquant aux
                // tirs de guet ennemis (duel aveugle auto, tour adverse).
                if (attacker != null && attacker.Stats != null && attacker.Stats.IsAlive)
                {
                    _overwatch.CheckOverwatchTriggers(attacker, attacker.CurrentCoords);
                }

                // Les PV ont changé : réévalue mood + intensité AVANT le verdict final
                // (si le combat est plié, CheckCombatOver -> HandleCombatEnded impose Victory/Defeat après).
                _host.RequestMusicUpdate();
                _host.RequestCombatOverCheck();
            };

            _ctx.Guard.TryEnter();
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Fire, 0.85f);

            bool freeLookActive = _ctx.Cinematic != null && _ctx.Cinematic.IsFreeLook;
            if (_ctx.GetKillcamEnabled() && _ctx.Cinematic != null && !freeLookActive)
            {
                _ctx.Cinematic.PlayCinematicKillshot(
                    attacker.transform,
                    defender.transform,
                    onStrikePoint: resolveAction,
                    onComplete: () => { _ctx.Guard.ForceReset(); },
                    onActionStart: triggerKickAction,
                    onDefenseStart: triggerDefenseAction
                );
            }
            else
            {
                _host.ArenaStartCoroutine(ExecuteAttackRoutine(triggerKickAction, triggerDefenseAction, resolveAction));
            }
        }

        public IEnumerator ExecuteAttackRoutine(Action triggerKick, Action triggerDefense, Action resolveImpact)
        {
            triggerKick?.Invoke();
            yield return new WaitForSeconds(0.06f);
            triggerDefense?.Invoke();
            yield return new WaitForSeconds(0.16f);
            resolveImpact?.Invoke();
            yield return new WaitForSeconds(0.65f);
            _ctx.Guard.ForceReset();
        }

        public IEnumerator ExecuteAttackWithPlayerDefenseRoutine(
            TacticalUnit attacker,
            TacticalUnit defender,
            BodyPart targetedPart,
            bool cancelPenaltyWithAP,
            SkillType attackSkill,
            SkillType defenseSkill,
            int weaponBaseDamage,
            int attackerBonusAP,
            int attackerPE,
            string attackerSpecialization,
            string defenderSpecialization,
            Action triggerKick,
            Action triggerDefense)
        {
            var grid = _ctx.Grid;
            var visualizer = _ctx.Visualizer;
            var calculator = _ctx.Calculator;
            _ctx.Guard.TryEnter();

            int effectiveWeaponDamage = weaponBaseDamage;
            var equippedWeapon = attacker.Sheet?.GetEquippedWeapon();
            if (equippedWeapon != null && equippedWeapon.BaseDamage > 0)
            {
                effectiveWeaponDamage = equippedWeapon.BaseDamage;
            }
            int weaponBonusEc2 = equippedWeapon != null ? Math.Max(0, equippedWeapon.AttackBonusEc) : 0;
            // RD-033 : charge Haute Densité chambrée = +1 dégât (sniper/Deglazer).
            bool hdShot2 = WeaponAmmo.UsesAmmo(equippedWeapon, attackSkill) && equippedWeapon.LoadedHD;
            if (hdShot2) effectiveWeaponDamage += 1;

            calculator.SetContactDistanceState(attacker.IsCanonEntrave());

            // Duel aveugle : Begin (base attaque) puis DÉCLARATION attaquant immédiate
            // (mise cachée, aucun jet lancé). La fenêtre défenseur s'ouvre ensuite en
            // aveugle : le défenseur ne voit ni jet ni mise adverse.
            // Livre VI §25.3 : couvert évalué sur positions réelles (Full => refus).
            // RD-036 : ruling hauteur recalculé sur positions réelles (même gate
            // que ExecuteAttack — la routine n'est atteinte qu'après le gate, le
            // mod suffit ici).
            CoverType defenseCover = _targeting.GetCoverToTarget(attacker, defender);
            bool isMeleeHere = SkillDefinitions.IsMeleeAttackSkill(attackSkill)
                || (equippedWeapon != null && equippedWeapon.RangeInTiles <= 1);
            ElevationRuling defenseRuling = _targeting.GetElevationRuling(attacker, defender, attackSkill, equippedWeapon, isMeleeHere);
            var duel = calculator.BeginSkillDuel(
                attacker.Stats, defender.Stats, targetedPart,
                attackSkill, defenseSkill, effectiveWeaponDamage, cancelPenaltyWithAP,
                false, attackerSpecialization, defenderSpecialization,
                defender.Stats.BaseArmorAbsorption, out string duelError, defenseCover, weaponBonusEc2,
                false, defenseRuling.AttackMod, defenseRuling.Label);

            if (duel == null)
            {
                _host.ArenaLog(duelError ?? "⚠️ Attaque impossible : PA insuffisants pour le coût de base.");
                _ctx.Guard.ForceReset();
                yield break;
            }

            calculator.DeclareAttackerStakes(duel, attackerBonusAP, attackerPE);

            bool playerDecided = false;
            SkillType finalDefSkill = defenseSkill;
            bool finalWantsDefend = true;
            int finalBonusAP = 0;
            int finalPE = 0;

            // La défense se paramètre dans le menu contextuel radial, centré sur le
            // défenseur comme après un clic droit sur lui (aucun modal séparé).
            var menu = CombatUI.CombatContextMenuUI.EnsureInstance();
            menu.OpenDefenseMenu(duel, attacker, defender, (chosenSkill, wantsDef, bonusAP, pe) =>
            {
                if (menu.InterceptedBy != null)
                {
                    defender = menu.InterceptedBy;
                }
                finalDefSkill = chosenSkill;
                finalWantsDefend = wantsDef;
                finalBonusAP = bonusAP;
                finalPE = pe;
                playerDecided = true;
            });

            while (!playerDecided)
            {
                // Garde-fou : si le menu de défense a disparu sans décision (changement
                // de scène, unité détruite...), on résout en encaissement passif plutôt
                // que de bloquer toutes les attaques suivantes pour toujours.
                if (!CombatUI.CombatContextMenuUI.IsDefenseOpen)
                {
                    finalDefSkill = defenseSkill;
                    finalWantsDefend = false;
                    finalBonusAP = 0;
                    finalPE = 0;
                    playerDecided = true;
                    _host.ArenaLog($"🛡️ <b>{defender.Stats.Name}</b> ne répond pas : encaissement passif (0 PA).");
                    break;
                }
                yield return null;
            }

            if (menu.InterceptedBy != null)
            {
                defender = menu.InterceptedBy;
            }

            // Déclaration aveugle du défenseur (mise débitée, rien révélé).
            calculator.DeclareDefenderStakes(duel, finalDefSkill, finalWantsDefend, finalBonusAP, finalPE, defenderSpecialization);

            // RÉVÉLATION SIMULTANÉE : les deux dés sont lancés ensemble.
            DamageResult result = calculator.ResolveBlindDuel(duel);

            triggerKick?.Invoke();
            yield return new WaitForSeconds(0.06f);
            if (finalWantsDefend)
            {
                var curDefVisual = defender != null ? defender.GetComponent<TacticalUnitVisual>() : null;
                if (defender != null && defender.Stats != null && defender.Stats.CanDefendActively())
                {
                    curDefVisual?.TriggerBodyBlock();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Defense_Block, defender.transform.position, 0.5f);
                }
            }
            yield return new WaitForSeconds(0.16f);

            attacker.Stats.RegisterAttack();
            _host.ArenaLog(result.CombatLog);
            _overwatch.HandleChannelInterruptionFx(defender, result.ChannelInterrupted);

            // Dégâts collatéraux sur l'obstacle si l'attaque est absorbée par le couvert ou neutralisée
            if (result.IsBlocked && defenseCover != CoverType.None && grid != null)
            {
                ObstacleDamageType directDmgType = ObstacleDamageType.Perforant;
                if (isMeleeHere)
                {
                    directDmgType = (attackSkill == SkillType.ManiementArmes && attacker.Stats.HasSpecialization("Marteau de Guerre"))
                        ? ObstacleDamageType.Contondant
                        : (attacker.Stats.HasSpecialization("Hache de Guerre") ? ObstacleDamageType.Tranchant : ObstacleDamageType.Contondant);
                }
                else if (equippedWeapon != null && equippedWeapon.Category.Contains("Laser"))
                {
                    directDmgType = ObstacleDamageType.Thermique;
                }

                if (CoverSystem.ScanCover(attacker.CurrentCoords, defender.CurrentCoords, grid, smokeZones: _state.SmokeZones, seesThroughSmoke: ArenaSmokeService.SeesThroughSmoke(attacker)).Rays is var rays && rays != null)
                {
                    for (int rIdx = 0; rIdx < rays.Count; rIdx++)
                    {
                        if (rays[rIdx].Blocked && rays[rIdx].HasBlockingCell)
                        {
                            if (PropObstacleRegistry.ApplyDamage(rays[rIdx].BlockingCell, effectiveWeaponDamage, directDmgType, grid, visualizer, out var obsRes))
                            {
                                _host.ArenaLog($"   🛡️ <b>IMPACT COLLATÉRAL</b> : {obsRes.Log}");
                            }
                            break;
                        }
                    }
                }
            }

            _melee.TryDisarmDefender(attacker, defender, targetedPart, result);
            if (WeaponAmmo.UsesAmmo(equippedWeapon, attackSkill) && equippedWeapon != null && result.AttackRoll.RawRoll > 0)
                _melee.ConsumeShotAndCheckJam(attacker, equippedWeapon, result, hdShot2);

            var laserWeapon = attacker.GetComponentInChildren<LaserRifleWeapon>();
            bool skillIsMelee = SkillDefinitions.IsMeleeAttackSkill(attackSkill);
            int freshDistAtImpact = (attacker != null && defender != null) ? attacker.CurrentCoords.DistanceTo(defender.CurrentCoords) : 1;
            bool isMeleeStrike = skillIsMelee && freshDistAtImpact <= 1;
            if (attackSkill == SkillType.Ballistique && !isMeleeStrike && laserWeapon != null && defender != null)
            {
                Vector3 targetCenter = defender.transform.position + Vector3.up * 1.15f;
                laserWeapon.FireLaser(targetCenter, result.IsHit);
            }

            bool isTargetDeadOrDown = !defender.Stats.IsAlive || result.FatalResolution != FatalBlowResolution.None;
            CombatHUD.RecordCombatAction(
                attacker.Stats.Name,
                defender.Stats.Name,
                attacker.IsPlayerControlled,
                defender.IsPlayerControlled,
                result.IsHit,
                result.IsCritical,
                result.IsBlocked,
                result.RawDamage,
                result.ArmorAbsorbed,
                result.FinalDamageApplied,
                result.ExceededEncaissement,
                isTargetDeadOrDown,
                (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP
            );

            if (KilltimeAudioManager.Instance != null && defender != null)
            {
                KilltimeAudioManager.Instance.PlayCombatResult(result, defender.transform.position);
                KilltimeAudioManager.Instance.Play(SoundId.Dice_Roll, 0.35f);
            }

            var attVisual = attacker.GetComponent<TacticalUnitVisual>();
            var defVisual = defender.GetComponent<TacticalUnitVisual>();

            if (attVisual != null)
            {
                int totalCost = (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP;
                attVisual.SpawnFloatingText($"Attaque {targetedPart} (-{totalCost} PA)", new Color(0.3f, 0.8f, 1.0f));
            }

            if (defVisual != null)
            {
                if (result.IsHit)
                {
                    defVisual.TriggerHitFlash();

                    if (result.IsCritical)
                    {
                        defVisual.SpawnFloatingText("[CRITIQUE] COUP DÉCISIF !", new Color(1.0f, 0.85f, 0.1f));
                    }

                    if (result.WasDeflected)
                    {
                        defVisual.SpawnFloatingText($"[DÉVIATION] -> {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff 0)", Color.yellow);
                    }
                    else
                    {
                        string diffSign = result.Differential > 0 ? $"+{result.Differential}" : $"{result.Differential}";
                        defVisual.SpawnFloatingText($"[TOUCHÉ] {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff {diffSign})", new Color(0.9f, 0.9f, 1.0f));
                    }

                    string dmgText = (result.ArmorAbsorbed > 0 || result.ShieldAbsorbed > 0)
                        ? $"-{result.FinalDamageApplied} PV (Bruts {result.RawDamage} | Armure -{result.ArmorAbsorbed}" + (result.ShieldAbsorbed > 0 ? $" | Bouclier -{result.ShieldAbsorbed}" : "") + ")"
                        : $"-{result.FinalDamageApplied} PV";
                    defVisual.SpawnFloatingText(dmgText, new Color(1.0f, 0.25f, 0.25f));

                    if (result.ExceededEncaissement)
                    {
                        defVisual.SpawnFloatingText($"[CHOC] TRAUMATIQUE ! [{result.InflictedStatus}]", new Color(1.0f, 0.4f, 0.95f));
                    }

                    if (result.CausedKnockback)
                    {
                        _melee.TryApplyKnockback(attacker, defender);
                    }

                    if (result.MoraleResult.Triggered)
                    {
                        if (result.MoraleResult.Surrendered)
                        {
                            defVisual.SpawnFloatingText("🏳️ REDDITION !", Color.white);
                            defVisual.DropHeldItemsWithPhysics();
                        }
                        else if (result.MoraleResult.Routed)
                        {
                            defVisual.SpawnFloatingText("😱 DÉROUTE ! [Agonisant]", new Color(0.95f, 0.2f, 0.85f));
                        }
                        else
                        {
                            defVisual.SpawnFloatingText("🧠 Sang-froid préservé", Color.cyan);
                        }
                    }

                    if (result.FatalResolution == FatalBlowResolution.InstantDeath)
                    {
                        defVisual.SpawnFloatingText("[MORTEL] INSTANTANÉ !", Color.black);
                        defVisual.TriggerFallingBackDeath();
                    }
                    else if (result.FatalResolution == FatalBlowResolution.MiracleSaved)
                    {
                        defVisual.SpawnFloatingText("[MIRACLE] DESTIN SAUVÉ ! (1 PV)", new Color(1.0f, 0.85f, 0.1f));
                        defVisual.TriggerFallingBackDeath();
                    }
                    else if (result.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                    {
                        defVisual.SpawnFloatingText("[K.O.] SYNCOPE TRAUMATIQUE !", Color.magenta);
                        defVisual.TriggerFallingBackDeath();
                    }
                    else if (result.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                    {
                        if (!defender.IsPlayerControlled)
                        {
                            defender.Stats.ChooseSombrer();
                            defVisual.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                            defVisual.TriggerFallingBackDeath();
                        }
                        else
                        {
                            defender.Stats.ChooseLastBreath();
                            defVisual.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                        }
                    }
                }
                else if (result.IsBlocked)
                {
                    defVisual.SpawnFloatingText($"[PARADE] Neutralisation (Diff {result.Differential})", new Color(0.2f, 0.9f, 1.0f));
                }
            }

            WebBridgeManager.Instance?.SendCombatEvent(
                result.IsHit ? "HIT" : "MISS",
                result.CombatLog,
                result.FinalDamageApplied
            );

            SnapshotRecorder?.Invoke($"Attaque sur {defender.Stats.Name} ({targetedPart})");

            if (!defender.Stats.IsAlive)
            {
                _host.RequestAutoTargetNextAlive();
            }

            _host.RequestMusicUpdate();
            _host.RequestCombatOverCheck();

            yield return new WaitForSeconds(0.55f);
            _ctx.Guard.ForceReset();
        }
    }
}
