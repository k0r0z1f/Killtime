using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Grid;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.UI;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Registre officiel des actions contextuelles de combat classées par livre du Codex.
    /// Valide rigoureusement la portée géométrique (mêlée vs tir) et l'armement équipé.
    /// </summary>
    public static class CombatActionRegistry
    {
        public static bool HasRangedWeaponEquipped(TacticalUnit actor)
        {
            if (actor == null) return false;
            var weapon = actor.Sheet?.GetEquippedWeapon();
            return weapon != null && weapon.RangeInTiles > 1;
        }

        public static int GetAttackMaxRange(TacticalUnit actor)
        {
            if (actor == null) return 1;
            var weapon = actor.Sheet?.GetEquippedWeapon();
            if (weapon != null && weapon.RangeInTiles > 0) return weapon.RangeInTiles;
            return 1; // Mains nues par défaut = portée de contact 1 case (Codex Livre VI)
        }

        public static bool IsTargetInRange(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null || target == null) return false;
            int distance = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            int maxRange = GetAttackMaxRange(actor);

            // Arme de contact ou mains nues (portée 1)
            if (maxRange <= 1 || !HasRangedWeaponEquipped(actor))
            {
                return distance <= 1;
            }

            // Arme à distance équipée
            return distance <= maxRange;
        }

        public static bool HasEnoughAP(TacticalUnit actor, int apCost)
        {
            return actor != null && actor.Stats != null && actor.Stats.CurrentActionPoints >= apCost;
        }

        private static bool CanAttackTarget(TacticalUnit actor, TacticalUnit target, int apCost)
        {
            if (actor == null || target == null) return false;
            if (target.Stats == null || !target.Stats.IsAlive) return false;
            if (actor.Stats == null || actor.Stats.CurrentActionPoints < apCost) return false;

            int distance = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            int maxRange = GetAttackMaxRange(actor);

            // Combat à mains nues ou arme de contact : contact immédiat requis (<= 1 case)
            if (maxRange <= 1 || !HasRangedWeaponEquipped(actor))
            {
                return distance <= 1;
            }

            // RD-038 : Le sprint bloque tout tir à distance ce tour
            if (!ChargeState.CanFireRanged(actor.Stats))
            {
                return false;
            }

            return distance <= maxRange;
        }

        private static SkillType ResolveContactSkill(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null) return SkillType.MainsNues;

            // Hors de portée de contact avec une arme à distance disponible => tir.
            // Évite le cas "mêlée jouée à distance" quand le menu a été ouvert au contact
            // puis l'unité s'est déplacée avant de valider.
            if (actor != null && target != null)
            {
                int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
                if (dist > 1 && HasRangedWeaponEquipped(actor))
                {
                    return SkillType.Ballistique;
                }
            }

            var weapon = actor.Sheet?.GetEquippedWeapon();

            if (weapon != null)
            {
                // Valeur legacy ArmesContondantes rabattue sur Maniement d'Arme.
                return SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill);
            }

            // Absence d'inventaire ou d'arme équipée : combat au corps-à-corps à mains nues
            return SkillType.MainsNues;
        }

        /// <summary>RD-033 : ce duel consomme-t-il un coup du chargeur ? (même règle que l'arène).</summary>
        private static bool FiresShot(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null) return false;
            var weapon = actor.Sheet?.GetEquippedWeapon();
            if (weapon == null || weapon.AmmoCapacity <= 0) return false;
            return WeaponAmmo.UsesAmmo(weapon, ResolveContactSkill(actor, target));
        }

        /// <summary>RD-033 : tir possible (chargeur non vide, arme non enrayée) ?</summary>
        private static bool CanFireShot(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null) return false;
            // RD-038 : Le sprint bloque tout tir ce tour
            if (FiresShot(actor, target) && !ChargeState.CanFireRanged(actor.Stats)) return false;
            var weapon = actor.Sheet?.GetEquippedWeapon();
            if (weapon == null || weapon.AmmoCapacity <= 0) return true;
            if (!FiresShot(actor, target)) return true;
            return !weapon.Jammed && weapon.AmmoRemaining > 0;
        }

        /// <summary>RD-033 : compteur chargeur pour les libellés ([🔋X/Y], [VIDE], [ENRAYÉE]).</summary>
        private static string AmmoTag(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null) return "";
            if (FiresShot(actor, target) && !ChargeState.CanFireRanged(actor.Stats))
            {
                return " [SPRINT : TIR BLOQUÉ]";
            }
            var weapon = actor.Sheet?.GetEquippedWeapon();
            if (weapon == null || weapon.AmmoCapacity <= 0) return "";
            if (!FiresShot(actor, target)) return "";
            if (weapon.Jammed) return " [ENRAYÉE]";
            return weapon.AmmoRemaining > 0
                ? $" [🔋{weapon.AmmoRemaining}/{weapon.AmmoCapacity}{(weapon.LoadedHD ? "+HD" : "")}]"
                : " [VIDE]";
        }

        public static List<CombatAction> GetAvailableActions(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            var actions = new List<CombatAction>();
            if (actor == null || target == null) return actions;

            bool isSelf = (actor == target);
            bool isEnemy = !target.IsPlayerControlled;
            bool targetIsAlive = target.Stats.IsAlive;

            // =========================================================================
            // 1. ATTAQUE & PASSES D'ARMES (LIVRE VI)
            // =========================================================================
            if (!isSelf && isEnemy && targetIsAlive)
            {
                // Frappe Standard
                actions.Add(new CombatAction(
                    $"🎯 Attaque Standard (2 PA){AmmoTag(actor, target)}",
                    "Frappe générale sur le centre de masse (Torse).",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => CanAttackTarget(act, tgt, 2) && CanFireShot(act, tgt),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Torse, cancelPenaltyWithAP: false,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

                // Visée Chirurgicale Tête
                actions.Add(new CombatAction(
                    $"💀 Visée : Tête (3 PA){AmmoTag(actor, target)}",
                    "Tir chirurgical avec dépense préalable de +1 PA pour annuler le malus de -2.",
                    ActionCategory.AttaqueEtPassesDarmes,
                    3,
                    (act, tgt) => CanAttackTarget(act, tgt, 3) && CanFireShot(act, tgt),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Tete, cancelPenaltyWithAP: true,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

                // Désarmement (Bras Droit) : fait tomber l'arme au sol sur gros différentiel.
                actions.Add(new CombatAction(
                    $"🗡️ Visée : Bras Droit (2 PA){AmmoTag(actor, target)}",
                    "Frappe le bras porteur : désarme et fait tomber l'arme au sol si Diff ≥ 4 (≥ 3 Croc, ≥ 2 Fleuret).",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => CanAttackTarget(act, tgt, 2) && CanFireShot(act, tgt),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.BrasDroit, cancelPenaltyWithAP: false,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

                // Faucher (Jambes)
                actions.Add(new CombatAction(
                    $"🦵 Visée : Jambes (2 PA){AmmoTag(actor, target)}",
                    "Impact sur les membres inférieurs pour infliger l'état À Terre et Ralenti.",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => CanAttackTarget(act, tgt, 2) && CanFireShot(act, tgt),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Jambes, cancelPenaltyWithAP: false,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

                // RD-038 : Charge au contact (3+ cases) + frappe d'assaut
                int chargeAtkCost = 2;
                bool isChargeDistance = actor.CurrentCoords.DistanceTo(target.CurrentCoords) >= 3;
                bool canChargeTarget = arena != null && arena.Pathfinder != null
                    && arena.Pathfinder.CanChargeTarget(actor, target, 3, chargeAtkCost)
                    && !GrappleState.IsGrappled(actor.Stats);

                if (canChargeTarget || isChargeDistance)
                {
                    int minChargeAP = 3 + chargeAtkCost;
                    actions.Add(new CombatAction(
                        $"⚡ Charge au contact ({minChargeAP}+ PA)",
                        "Manœuvre d'assaut (RD-038, Livre VI) : déplacement d'au moins 3 cases vers un flanc libre de la cible immédiatement suivi d'une frappe au contact (+2 dégâts d'impact, -1 défense jusqu'au prochain tour personnel, tir bloqué ce tour).",
                        ActionCategory.AttaqueEtPassesDarmes,
                        minChargeAP,
                        (act, tgt) => arena != null && arena.Pathfinder != null && arena.Pathfinder.CanChargeTarget(act, tgt, 3, chargeAtkCost) && !GrappleState.IsGrappled(act.Stats),
                        (act, tgt) => arena.ExecuteChargeAttack(act, tgt, BodyPart.Torse, CombatContextMenuUI.CurrentInjectedAP, CombatContextMenuUI.CurrentInjectedPE)
                    ));
                }

                // --- Grenades : lancer sur la case de la cible si à portée ---
                InventoryItem firstGrenade = null;
                InventoryItem anyLauncher = null;
                if (actor.Sheet != null && actor.Sheet.Inventory != null)
                {
                    for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
                    {
                        var it = actor.Sheet.Inventory[i];
                        if (it == null) continue;
                        if (firstGrenade == null && it.IsThrowableGrenade()) firstGrenade = it;
                        if (anyLauncher == null && it.IsLauncher) anyLauncher = it;
                    }
                }
                if (firstGrenade != null)
                {
                    var gCap = firstGrenade;
                    int handMaxRange = GrenadeRules.ComputeMaxRange(gCap, null);
                    actions.Add(new CombatAction(
                        $"💣 Grenade : {gCap.Name} (2 PA, main)",
                        $"Souffle R{gCap.BlastRadius} : {gCap.BaseDamage}+{gCap.DamageDiceCount}d10 + shrap {gCap.ShrapnelDamage}. Vise la case de la cible.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= handMaxRange,
                        (act, tgt) => arena.ExecuteGrenadeThrow(tgt.CurrentCoords, gCap.ItemId, false, false, 0)
                    ));
                    if (anyLauncher != null)
                    {
                        var lCap = anyLauncher;
                        var gCap2 = firstGrenade;
                        int launcherMaxRange = GrenadeRules.ComputeMaxRange(gCap2, lCap);
                        int launcherCost = Mathf.Max(1, 3 + lCap.ApCostModifier);
                        actions.Add(new CombatAction(
                            $"💣 Lance-grenades : {gCap2.Name} via {lCap.Name} ({launcherCost} PA)",
                            $"Portée {launcherMaxRange} cases, dispersion réduite. Vise la case de la cible.",
                            ActionCategory.AttaqueEtPassesDarmes,
                            launcherCost,
                            (act, tgt) => act.Stats.CurrentActionPoints >= launcherCost && targetIsAlive && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= launcherMaxRange,
                            (act, tgt) => arena.ExecuteGrenadeThrow(tgt.CurrentCoords, gCap2.ItemId, true, false, 0)
                        ));
                    }
                }

                // Lancer d'arme équipée (2 PA) : 2-4 cases, duel Ballistique, l'arme tombe à la case cible.
                InventoryItem equippedForThrow = actor.Sheet?.GetEquippedWeapon();
                if (equippedForThrow != null && !equippedForThrow.IsThrowableGrenade() && !equippedForThrow.IsLauncher)
                {
                    string throwName = equippedForThrow.Name;
                    int throwDmg = equippedForThrow.BaseDamage;
                    actions.Add(new CombatAction(
                        $"🗡️ Lancer : {throwName} (2 PA, 2-4 cases)",
                        $"Projette l'arme ({throwDmg}D + diff) en Ballistique. L'arme quitte la main et tombe au sol, même manquée.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) =>
                        {
                            if (act.Stats.CurrentActionPoints < 2 || !targetIsAlive) return false;
                            var w = act.Sheet?.GetEquippedWeapon();
                            if (w == null || w.IsThrowableGrenade() || w.IsLauncher) return false;
                            int d = act.CurrentCoords.DistanceTo(tgt.CurrentCoords);
                            return d >= 2 && d <= 4;
                        },
                        (act, tgt) => arena.ExecuteWeaponThrow(act, tgt)
                    ));
                }

                // Balise laser (Lampe + Batterie) : 1 PA, ≤12 cases, cible visible.
                // Expose la faille (prochaine attaque ignore l'encaissement).
                if (HasTool(actor, "Lampe") && HasTool(actor, "Batterie"))
                {
                    actions.Add(new CombatAction(
                        "🔦 Balise Laser (1 PA, 12 cases)",
                        "Désigne la cible (visible, 12 cases) : expose sa faille, la prochaine attaque ignore son encaissement.",
                        ActionCategory.TactiqueEtOrdres,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 12
                            && HasTool(act, "Lampe") && HasTool(act, "Batterie")
                            && (arena == null || arena.GetCoverToTarget(act, tgt) != CoverType.Full),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            Killtime.Core.Combat.SkillTechniqueState.ApplyExposedFlaw(act.Stats, tgt.Stats);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🔦 DÉSIGNÉ", Color.yellow);
                            arena?.Log($"🔦 <b>{act.Stats.Name}</b> désigne <b>{tgt.Stats.Name}</b> à la balise (faille exposée).");
                            arena?.RecordChronoSnapshot($"Balise : {act.Stats.Name} -> {tgt.Stats.Name}");
                        }
                    ));
                }

                // Grappin Magnétique (2 PA, 2-4 cases) : harpon 3D, À Terre si Diff ≥ 2.
                if (HasTool(actor, "Grappin"))
                {
                    actions.Add(new CombatAction(
                        "🪝 Grappin : Harpon (2 PA, 2-4 cases)",
                        "Projette le grappin (3D Ballistique) : À Terre si différentiel ≥ 2. Le grappin revient.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) =>
                        {
                            if (act.Stats.CurrentActionPoints < 2 || !targetIsAlive) return false;
                            if (!HasTool(act, "Grappin")) return false;
                            int d = act.CurrentCoords.DistanceTo(tgt.CurrentCoords);
                            return d >= 2 && d <= 4;
                        },
                        (act, tgt) => arena.ExecuteGrapnel(act, tgt)
                    ));
                }

                // Menottes Magnétiques (2 PA, contact, cible affaiblie) : Immobilise 2 tours, consommées.
                InventoryItem cuffs = FindTool(actor, "Menottes");
                if (cuffs != null)
                {
                    actions.Add(new CombatAction(
                        "🔗 Menotter (2 PA, contact)",
                        "Entrave une cible affaiblie (À Terre, Déstabilisée, Sonnée, Étourdie, Paralysée) : Immobilise 2 tours. Menottes consommées.",
                        ActionCategory.TactiqueEtOrdres,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && HasWeakenedStatus(tgt.Stats) && HasTool(act, "Menottes"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(2)) return;
                            if (!ConsumeTool(act, "Menottes")) return;
                            act.NotifyInventoryChanged(true);
                            tgt.Stats.ApplyStatus(StatusEffect.Immobilise, 2);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🔗 MENOTTÉ (2 tours)", Color.yellow);
                            arena?.Log($"🔗 <b>{act.Stats.Name}</b> menotte <b>{tgt.Stats.Name}</b> (Immobilise 2 tours).");
                        }
                    ));
                }

                // Corde : Ligoter (1 PA, contact, ennemi À Terre) → Immobilise 1 tour, réutilisable.
                if (HasTool(actor, "Corde") && target.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre))
                {
                    actions.Add(new CombatAction(
                        "🪢 Ligoter (1 PA, contact)",
                        "Ligue un ennemi à terre : Immobilise 1 tour. La corde est réutilisable.",
                        ActionCategory.TactiqueEtOrdres,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && tgt.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre) && HasTool(act, "Corde"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            tgt.Stats.ApplyStatus(StatusEffect.Immobilise, 1);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🪢 LIGOTÉ", Color.yellow);
                            arena?.Log($"🪢 <b>{act.Stats.Name}</b> ligote <b>{tgt.Stats.Name}</b> (Immobilise 1 tour).");
                        }
                    ));
                }

                // =============================================================
                // RD-039 : LUTTE, GRAPPLE, ÉTRANGLEMENT & TRAÎNÉE DE CORPS
                // =============================================================
                bool isGrappledByMe = Killtime.Core.Combat.GrappleState.IsGrappling(actor.Stats, target.Stats);
                bool isGrappledByTarget = Killtime.Core.Combat.GrappleState.IsGrappling(target.Stats, actor.Stats);

                if (isGrappledByTarget)
                {
                    actions.Add(new CombatAction(
                        "🔓 Lutte : Se Libérer de la Prise (2 PA)",
                        "Duel opposé aveugle Athlétisme/MainsNues/Acrobatie vs Athlétisme pour briser la prise et dissiper l'état Immobilisé.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && Killtime.Core.Combat.GrappleState.IsGrappling(tgt.Stats, act.Stats),
                        (act, tgt) => arena.ExecuteGrappleEscape(act, tgt)
                    ));
                }
                else if (isGrappledByMe)
                {
                    actions.Add(new CombatAction(
                        "🥋 Lutte : Relâcher la Prise (0 PA)",
                        $"Relâche l'étreinte sur {target.Stats.Name} (dissipe son état Immobilisé de lutte).",
                        ActionCategory.AttaqueEtPassesDarmes,
                        0,
                        (act, tgt) => Killtime.Core.Combat.GrappleState.IsGrappling(act.Stats, tgt.Stats),
                        (act, tgt) =>
                        {
                            Killtime.Core.Combat.GrappleState.ReleaseGrapple(act.Stats);
                            act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("Prise relâchée", Color.gray);
                            arena?.Log($"🥋 <b>{act.Stats.Name}</b> relâche sa prise de lutte sur <b>{tgt.Stats.Name}</b>.");
                        }
                    ));

                    actions.Add(new CombatAction(
                        "🫁 Étranglement (2 PA, contact)",
                        "Suffocation au corps-à-corps (Athlétisme vs Endurance) : dégâts bruts au cou + statut Asphyxie (2 tours, drain 1 PA/tour, pool 2 dégâts continus).",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1,
                        (act, tgt) => arena.ExecuteStrangulation(act, tgt)
                    ));

                    actions.Add(new CombatAction(
                        "🤼 Traîner le corps (2 PA, 1 case)",
                        "Recule ou pivote d'1 case et tire la cible saisie dans la case libérée (coût 2 PA pour le grappler).",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1 && (arena == null || arena.CanDragBody(act, tgt)),
                        (act, tgt) => arena.ExecuteDragBody(act, tgt)
                    ));
                }
                else
                {
                    actions.Add(new CombatAction(
                        "🥋 Lutte : Prise au Corps-à-Corps (2 PA)",
                        "Duel opposé aveugle Athlétisme vs Athlétisme : saisit et applique l'état Immobilisé 1 tour. Permet ensuite la traînée (1 case / 2 PA) et l'étranglement (Asphyxie).",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && !act.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise),
                        (act, tgt) => arena.ExecuteGrapple(act, tgt)
                    ));

                    if (target.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise))
                    {
                        actions.Add(new CombatAction(
                            "🫁 Étranglement (2 PA, contact)",
                            "Suffocation au corps-à-corps (Athlétisme vs Endurance) sur cible immobilisée : dégâts bruts au cou + statut Asphyxie (2 tours, drain 1 PA/tour).",
                            ActionCategory.AttaqueEtPassesDarmes,
                            2,
                            (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1,
                            (act, tgt) => arena.ExecuteStrangulation(act, tgt)
                        ));

                        actions.Add(new CombatAction(
                            "🤼 Traîner le corps (2 PA, 1 case)",
                            "Déplace le corps d'une cible immobilisée d'1 case (coût 2 PA).",
                            ActionCategory.AttaqueEtPassesDarmes,
                            2,
                            (act, tgt) => act.Stats.CurrentActionPoints >= 2 && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1 && (arena == null || arena.CanDragBody(act, tgt)),
                            (act, tgt) => arena.ExecuteDragBody(act, tgt)
                        ));
                    }
                }
            }

            // =========================================================================
            // 2. CINQUIÈME FORCE & SORTS MODULAIRES (LIVRE IV)
            // =========================================================================
            if (actor.Sheet != null && actor.Sheet.LearnedSpells.Count > 0)
            {
                bool hasFocus = Killtime.Core.Arcanotech.ArcanotechWorkshop.HasFocus(actor.Sheet);
                bool hasEclat = Killtime.Core.Arcanotech.ArcanotechWorkshop.HasEclat(actor.Sheet);
                foreach (var spell in actor.Sheet.LearnedSpells)
                {
                    int spellCost = spell.ActionPointCost + (hasFocus ? 0 : 2);
                    actions.Add(new CombatAction(
                        $"🔮 Canaliser : {spell.Name} ({spellCost} PA{(hasFocus ? "" : ", sans focus +2")}{(hasEclat ? ", +1 Éclat" : "")})",
                        $"5e Force ({spell.Discipline}) — Dégâts de base: {spell.BaseArcaneDamage}",
                        ActionCategory.CinquiemeForceEtSorts,
                        spellCost,
                        (act, tgt) => act.Stats.CurrentActionPoints >= spell.ActionPointCost + Killtime.Core.Arcanotech.ArcanotechWorkshop.FocusTax(act.Sheet) && targetIsAlive,
                        (act, tgt) =>
                        {
                            var dice = new DiceRoller();
                            if (spell.Cast(act.Stats, tgt.Stats, dice, out string log))
                            {
                                var vis = tgt.GetComponent<TacticalUnitVisual>();
                                vis?.TriggerHitFlash();
                                vis?.SpawnFloatingText($"-{spell.BaseArcaneDamage} Arcanique", Color.magenta);
                            }
                        }
                    ));
                }
            }

            // =========================================================================
            // 3. TRAUMATOLOGIE & SOINS (LIVRE VII)
            // =========================================================================
            if (targetIsAlive)
            {
                // Chirurgie : Suture Réflexe (Livre III) — gestes médicaux d'urgence
                // sur le front : les premiers soins passent de 3 PA à 2 PA.
                // Kit de Chirurgie en poche (non consommé) : CON×3 + purge Empoisonne.
                int healCost = (actor.Stats != null && actor.Stats.HasSpecialization("Chirurgie : Suture Réflexe")) ? 2 : 3;
                bool hasChirKit = HasTool(actor, "Chirurgie");
                string healDesc = (healCost == 2
                    ? "Suture Réflexe au contact (Régénère Constitution × 2 PV). Coût réduit à 2 PA par la spécialisation."
                    : "Stabilisation et suture d'urgence au contact (Régénère Constitution × 2 PV).")
                    + (hasChirKit ? " Kit de Chirurgie : CON×3 + purge Empoisonne." : "");
                actions.Add(new CombatAction(
                    $"🩹 Premiers Soins d'Urgence ({healCost} PA{(hasChirKit ? " +Kit" : "")})",
                    healDesc,
                    ActionCategory.TraumatologieEtSoins,
                    healCost,
                    (act, tgt) => act.Stats.CurrentActionPoints >= healCost && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(healCost))
                        {
                            bool kit = HasTool(act, "Chirurgie");
                            int healAmount = tgt.Stats.Attributes.Constitution * (kit ? 3 : 2);
                            tgt.Stats.CurrentHealth = Mathf.Min(tgt.Stats.MaxHealth, tgt.Stats.CurrentHealth + healAmount);
                            // RD-045 : le soin stoppe le DoT (RemoveStatus purge le pool résiduel).
                            tgt.Stats.RemoveStatus(StatusEffect.Saignement);
                            if (kit)
                            {
                                tgt.Stats.RemoveStatus(StatusEffect.Empoisonne);
                                tgt.Stats.RemoveStatus(StatusEffect.EnFeu);
                                tgt.Stats.RemoveStatus(StatusEffect.Asphyxie);
                            }

                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"+{healAmount} PV Soignés{(kit ? " (kit)" : "")}", Color.green);
                            if (kit) arena?.Log($"🩹 Bloc opératoire de campagne : Empoisonne/EnFeu/Asphyxie purgés sur <b>{tgt.Stats.Name}</b>.");
                        }
                    }
                ));

                // Consommables marketplace Livre VIII §32.3 : seringues + bandage (HealingAmount).
                // 1 PA, soi-même ou allié adjacent, consomme 1 dose du stock.
                InventoryItem bestHeal = FindBestHealingConsumable(actor);
                if (bestHeal != null)
                {
                    string healName = bestHeal.Name;
                    int healValue = bestHeal.HealingAmount;
                    int healStock = CountConsumableStock(actor, healName);
                    bool isBandage = healName.Contains("Bandage");
                    actions.Add(new CombatAction(
                        $"💉 {healName} (+{healValue} PV, 1 PA){(healStock > 1 ? $" x{healStock}" : "")}",
                        isBandage
                            ? $"Injection/pansement au contact : +{healValue} PV + stoppe Saignement. Consomme 1 dose."
                            : $"Injection au contact : +{healValue} PV. Consomme 1 dose (ne réanime pas).",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1
                            && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && FindBestHealingConsumable(act) != null,
                        (act, tgt) =>
                        {
                            var dose = FindBestHealingConsumable(act);
                            if (dose == null) return;
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            string usedName = dose.Name;
                            int usedHeal = dose.HealingAmount;
                            bool usedBandage = usedName.Contains("Bandage");
                            act.Sheet?.ConsumeOne(dose.ItemId);
                            act.NotifyInventoryChanged(true);
                            int healed = tgt.Stats.Heal(usedHeal);
                            if (usedBandage) tgt.Stats.RemoveStatus(StatusEffect.Saignement);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"+{healed} PV ({usedName})", Color.green);
                            arena?.Log($"💉 <b>{act.Stats.Name}</b> utilise <b>{usedName}</b> sur <b>{tgt.Stats.Name}</b> (+{healed} PV{(usedBandage ? ", Saignement stoppé" : "")}).");
                            arena?.RecordChronoSnapshot($"Soin {usedName} : {act.Stats.Name} -> {tgt.Stats.Name}");
                        }
                    ));
                }

                // Attelle Rigide : retire Ralenti (fracture immobilisée), 1 PA, contact.
                InventoryItem attelle = FindConsumableByName(actor, "Attelle");
                if (attelle != null)
                {
                    string attName = attelle.Name;
                    actions.Add(new CombatAction(
                        $"🦴 {attName} (retire Ralenti, 1 PA)",
                        "Immobilise un membre fracturé : retire Ralenti (et Immobilise léger). Consomme 1 dose.",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1
                            && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && FindConsumableByName(act, "Attelle") != null,
                        (act, tgt) =>
                        {
                            var dose = FindConsumableByName(act, "Attelle");
                            if (dose == null) return;
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            act.Sheet?.ConsumeOne(dose.ItemId);
                            act.NotifyInventoryChanged(true);
                            tgt.Stats.RemoveStatus(StatusEffect.Ralenti);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🦴 Fracture immobilisée", Color.green);
                            arena?.Log($"🦴 <b>{act.Stats.Name}</b> pose <b>{dose.Name}</b> sur <b>{tgt.Stats.Name}</b> (Ralenti retiré).");
                        }
                    ));
                }
            }
            else
            {
                // Réanimation d'urgence
                actions.Add(new CombatAction(
                    "⚡ Défibrillation Arcanique (4 PA)",
                    "Tente de ramener un combattant au contact à 1 PV avant le coma définitif.",
                    ActionCategory.TraumatologieEtSoins,
                    4,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 4 && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(4))
                        {
                            tgt.Stats.CurrentHealth = 1;
                            tgt.Stats.ActiveStatus &= ~StatusEffect.Inconscient;

                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("RÉANIMATION!", Color.cyan);
                        }
                    }
                ));
            }

            // Souffle : actions personnelles (Livres I §4.2 + VI §24.2).
            // Refresh PA = début de son propre tour uniquement (TurnManager).
            // Ici : les deux conversions Souffle <-> PA, jouables sur soi-même.
            if (isSelf)
            {
                bool hasMarathonHeart = actor.Stats.HasSpecialization("Course d'Endurance : Cœur de Marathon");
                int breathPA = hasMarathonHeart ? 3 : 2;
                actions.Add(new CombatAction(
                    $"🫁 Souffle d'Urgence (+{breathPA} PA, +1 ESS)",
                    $"Prend 1 point d'essoufflement pour gagner {breathPA} PA immédiats.",
                    ActionCategory.TraumatologieEtSoins,
                    0,
                    (act, tgt) => act.Stats.Essoufflement < act.Stats.Attributes.Constitution,
                    (act, tgt) => act.Stats.TakeEmergencyBreath(act.Stats.HasSpecialization("Course d'Endurance : Cœur de Marathon") ? 3 : 2)
                ));
                bool hasSecondWind = actor.Stats.HasSpecialization("Course d'Endurance : Second Souffle");
                int recoverESS = hasSecondWind ? 2 : 1;
                actions.Add(new CombatAction(
                    $"🌬️ Reprendre son Souffle (1 PA → -{recoverESS} ESS)",
                    $"Début de son propre tour : dépense 1 PA pour effacer {recoverESS} point(s) d'essoufflement (répétable, max Constitution).",
                    ActionCategory.TraumatologieEtSoins,
                    1,
                    (act, tgt) => act.Stats.Essoufflement > 0 && act.Stats.CurrentActionPoints >= 1,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(1))
                        {
                            // Second Souffle (Athlétisme) : récupération doublée.
                            int recovered = act.Stats.HasSpecialization("Course d'Endurance : Second Souffle") ? 2 : 1;
                            act.Stats.RecoverBreath(recovered);
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"Souffle repris (-{recovered} ESS)", UnityEngine.Color.green);
                        }
                    }
                ));

                if (actor.Stats.HasSpecialization("Seconde Respiration"))
                {
                    actions.Add(new CombatAction(
                        "🌬️ Seconde Respiration (0 PA → -2 ESS, 1x/combat)",
                        "Ventilation cellulaire d'urgence : efface immédiatement 2 points d'essoufflement sans dépense de PA (1 fois par combat).",
                        ActionCategory.TraumatologieEtSoins,
                        0,
                        (act, tgt) => act.Stats.Essoufflement > 0 && !act.Stats.HasUsedSecondeRespiration,
                        (act, tgt) =>
                        {
                            act.Stats.HasUsedSecondeRespiration = true;
                            act.Stats.RecoverBreath(2);
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("Seconde Respiration (-2 ESS)", UnityEngine.Color.cyan);
                            arena?.Log($"🌬️ <b>{act.Stats.Name}</b> déclenche sa <b>Seconde Respiration</b> (-2 ESS, 0 PA) !");
                        }
                    ));
                }

                actions.Add(new CombatAction(
                    "⚡ Poussée Cardiovasculaire (Redline : +1 PA, +1 ESS)",
                    "Dépasse les limites physiologiques pour forcer 1 PA au prix d'un sur-échauffement immédiat.",
                    ActionCategory.TraumatologieEtSoins,
                    0,
                    (act, tgt) => act.Stats.Essoufflement < act.Stats.Attributes.Constitution,
                    (act, tgt) =>
                    {
                        if (act.Stats.TriggerRedlineAP(1))
                        {
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("⚡ REDLINE (+1 PA, +1 ESS)", UnityEngine.Color.red);
                            arena?.Log($"⚡ <b>{act.Stats.Name}</b> entre en <b>Poussée Cardiovasculaire (Redline)</b> (+1 PA, +1 ESS) !");
                        }
                    }
                ));

                // RD-030 Guet / Overwatch : réserve un tir de réaction (2 PA, arme à
                // distance). Duel aveugle auto pendant le tour adverse, 1 tir puis
                // consommé. Expire au prochain tour personnel, annulé si Étourdi/Paralysé.
                int overwatchCost = Killtime.Core.Rules.CoreRulesConfig.Instance.OverwatchAPCost;
                actions.Add(new CombatAction(
                    $"👁️ Guet / Overwatch ({overwatchCost} PA, tir réservé)",
                    "Réserve un tir de réaction dans le rayon de l'arme à distance : duel aveugle automatique (Ballistique vs Esquive) pendant le tour adverse. 1 tir puis consommé.",
                    ActionCategory.TactiqueEtOrdres,
                    overwatchCost,
                    (act, tgt) => act != null && act.Stats != null && act.Stats.IsAlive
                        && HasRangedWeaponEquipped(act)
                        && act.Stats.CurrentActionPoints >= Killtime.Core.Rules.CoreRulesConfig.Instance.OverwatchAPCost
                        && !OverwatchState.IsWatching(act.Stats)
                        && !OverwatchState.IsCancelledByStatus(act.Stats),
                    (act, tgt) =>
                    {
                        if (arena != null)
                        {
                            arena.TryEnterOverwatch(act, out _);
                        }
                        else
                        {
                            var w = act.Sheet?.GetEquippedWeapon();
                            int range = w != null ? Mathf.Max(1, w.RangeInTiles) : 1;
                            if (new CombatCalculator().TryEnterOverwatch(act.Stats, range, out _))
                            {
                                var vis = act.GetComponent<TacticalUnitVisual>();
                                vis?.SpawnFloatingText($"👁️ EN GUET ({range} cases)", Color.cyan);
                            }
                        }
                    }
                ));

                // RD-031 Opportunités (Livre VI §24.4) : posture de réaction au contact
                // rompu (persistante, 0 PA, changeable à volonté pendant son tour) +
                // Décrochage (1 PA : prochain déplacement sans réaction adverse).
                // 1 réaction/round : Frappe gratuite (défaut), Poursuite 1 PA (suit
                // d'1 case), Blocage 1 PA (annule le pas, sans grapple), Balayage
                // gratuit (Jambes : À Terre + Ralenti si choc).
                var opportunityStances = new (OpportunityReactionType stance, string icon, string desc)[]
                {
                    (OpportunityReactionType.Strike, "🗡️", "Frappe gratuite de mêlée en duel aveugle auto quand un ennemi quitte votre contact. Base du hit-and-run."),
                    (OpportunityReactionType.Follow, "👣", "Suit le fuyard d'1 case (1 PA de réaction) au lieu de frapper. Sans attaque, sans grapple."),
                    (OpportunityReactionType.Block, "🛡️", "Retient le fuyard (duel Athlétisme, 1 PA) : succès = pas annulé, sans grapple permanent, PA du fuyard remboursés."),
                    (OpportunityReactionType.Trip, "🦵", "Balayage gratuit visant les Jambes : À Terre + Ralenti en cas de choc, au lieu de dégâts max."),
                };
                for (int si = 0; si < opportunityStances.Length; si++)
                {
                    var stanceChoice = opportunityStances[si].stance;
                    string stanceIcon = opportunityStances[si].icon;
                    string stanceDesc = opportunityStances[si].desc;
                    actions.Add(new CombatAction(
                        $"{stanceIcon} Posture : {OpportunityState.StanceLabel(stanceChoice)} (0 PA)",
                        stanceDesc + $" Posture actuelle : {OpportunityState.StanceLabel(OpportunityState.GetStance(actor.Stats))}.",
                        ActionCategory.TactiqueEtOrdres,
                        0,
                        (act, tgt) => act != null && act.Stats != null && act.Stats.IsAlive
                            && OpportunityState.GetStance(act.Stats) != stanceChoice,
                        (act, tgt) =>
                        {
                            OpportunityState.SetStance(act.Stats, stanceChoice);
                            act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"{stanceIcon} {OpportunityState.StanceLabel(stanceChoice)}", Color.cyan);
                            arena?.Log($"🎯 <b>{act.Stats.Name}</b> adopte la posture d'opportunité <b>{OpportunityState.StanceLabel(stanceChoice)}</b> (réaction au contact rompu).");
                        }
                    ));
                }

                int disengageCost = Killtime.Core.Rules.CoreRulesConfig.Instance.DisengageAPCost;
                actions.Add(new CombatAction(
                    $"💨 Décrochage ({disengageCost} PA : prochain déplacement sans opportunité)",
                    "Prépare un retrait propre (Livre VI §26.2) : le prochain déplacement ne provoque aucune frappe/poursuite/blocage/balayage. Expire au prochain tour personnel si inutilisé.",
                    ActionCategory.TactiqueEtOrdres,
                    disengageCost,
                    (act, tgt) => act != null && act.Stats != null && act.Stats.IsAlive
                        && !OpportunityState.HasSafeDisengage(act.Stats)
                        && !OpportunityState.IsExemptFromProvoking(act.Stats)
                        && act.Stats.CurrentActionPoints >= Killtime.Core.Rules.CoreRulesConfig.Instance.DisengageAPCost
                        && !OpportunityState.IsCancelledByStatus(act.Stats),
                    (act, tgt) =>
                    {
                        if (arena != null) arena.TryRequestSafeDisengage(act, out _);
                        else if (OpportunityState.RequestSafeDisengage(act.Stats, Killtime.Core.Rules.CoreRulesConfig.Instance.DisengageAPCost, out _))
                            act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💨 DÉCROCHAGE", Color.cyan);
                    }
                ));

                // Cellules Nytharite : recharge tactique du champ de force personnel (1 PA).
                if (actor.Stats.MaxShieldHP > 0 && actor.Stats.CurrentShieldHP < actor.Stats.MaxShieldHP)
                {
                    if (HasTool(actor, "Cellule Nytharite Standard"))
                    {
                        actions.Add(new CombatAction(
                            "🔮 Cellule Nytharite (+10 Bouclier, 1 PA)",
                            "Consomme 1 Cellule Nytharite Standard pour restaurer 10 PV de barrière énergétique.",
                            ActionCategory.TraumatologieEtSoins,
                            1,
                            (act, tgt) => act.Stats.CurrentActionPoints >= 1 && act.Stats.CurrentShieldHP < act.Stats.MaxShieldHP && HasTool(act, "Cellule Nytharite Standard"),
                            (act, tgt) =>
                            {
                                if (arena != null)
                                {
                                    if (arena.TryRechargeShieldWithCell(act, false, out string msg)) arena.Log(msg);
                                    else arena.Log("⚠️ " + msg);
                                }
                            }
                        ));
                    }
                    if (HasTool(actor, "Cellule Nytharite Pure"))
                    {
                        actions.Add(new CombatAction(
                            "🔮 Cellule Pure (+25 Bouclier, 1 PA)",
                            "Consomme 1 Cellule Nytharite Pure pour restaurer 25 PV de barrière énergétique.",
                            ActionCategory.TraumatologieEtSoins,
                            1,
                            (act, tgt) => act.Stats.CurrentActionPoints >= 1 && act.Stats.CurrentShieldHP < act.Stats.MaxShieldHP && HasTool(act, "Cellule Nytharite Pure"),
                            (act, tgt) =>
                            {
                                if (arena != null)
                                {
                                    if (arena.TryRechargeShieldWithCell(act, true, out string msg)) arena.Log(msg);
                                    else arena.Log("⚠️ " + msg);
                                }
                            }
                        ));
                    }
                }

                // Boîte à Outils Arcanotech : maintenance de son propre bouclier (1 PA).
                if (actor.Stats.MaxShieldHP > 0 && actor.Stats.CurrentShieldHP < actor.Stats.MaxShieldHP && HasTool(actor, "Boîte à Outils"))
                {
                    actions.Add(new CombatAction(
                        "🔧 Boîte à Outils : Réparer Bouclier (1 PA)",
                        "Répare le circuit du champ de force (+10 Bouclier, outil réutilisable).",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && act.Stats.CurrentShieldHP < act.Stats.MaxShieldHP && HasTool(act, "Boîte à Outils"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            int before = act.Stats.CurrentShieldHP;
                            act.Stats.CurrentShieldHP = Mathf.Min(act.Stats.MaxShieldHP, act.Stats.CurrentShieldHP + 10);
                            int gained = act.Stats.CurrentShieldHP - before;
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"🔧 +{gained} Bouclier", Color.cyan);
                            arena?.Log($"🔧 <b>{act.Stats.Name}</b> répare son champ à la Boîte à Outils (+{gained} Bouclier, -1 PA).");
                        }
                    ));
                }

                // Moteur Arcanique de Poche : injection d'énergie de secours (1 PA, non consommé).
                if (actor.Stats.MaxShieldHP > 0 && actor.Stats.CurrentShieldHP < actor.Stats.MaxShieldHP && HasTool(actor, "Moteur"))
                {
                    actions.Add(new CombatAction(
                        "⚙️ Moteur Arcanique (+10 Bouclier, 1 PA)",
                        "Force une suralimentation du champ de force via le générateur portatif (10 PV barrière, non consommé).",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && act.Stats.CurrentShieldHP < act.Stats.MaxShieldHP && HasTool(act, "Moteur"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            int before = act.Stats.CurrentShieldHP;
                            act.Stats.CurrentShieldHP = Mathf.Min(act.Stats.MaxShieldHP, act.Stats.CurrentShieldHP + 10);
                            int gained = act.Stats.CurrentShieldHP - before;
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"⚙️ +{gained} Bouclier", Color.cyan);
                            arena?.Log($"⚙️ <b>{act.Stats.Name}</b> déclenche son <b>Moteur Arcanique de Poche</b> (+{gained} Bouclier, -1 PA).");
                        }
                    ));
                }

                // Résonateur Nytharite (focus) : harmonisation psi offensive (1 PA).
                if (HasTool(actor, "Résonateur"))
                {
                    actions.Add(new CombatAction(
                        "🔮 Harmonisation Psi (1 PA)",
                        "Canalise le Résonateur Nytharite : confère l'état Survolté (+1 EC) pour 1 tour.",
                        ActionCategory.CinquiemeForceEtSorts,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && !act.Stats.ActiveStatus.HasFlag(StatusEffect.Survolte) && HasTool(act, "Résonateur"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            act.Stats.ApplyStatus(StatusEffect.Survolte, 1);
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🔮 SURVOLTÉ (+1 EC)", Color.magenta);
                            arena?.Log($"🔮 <b>{act.Stats.Name}</b> s'harmonise avec son <b>Résonateur Nytharite</b> (+1 EC, Survolté).");
                        }
                    ));
                }

                // Dopants marketplace Livre VIII §32.3 : Antidouleurs + Speed, 1 PA, soi-même.
                InventoryItem antalgique = FindConsumableByName(actor, "Antidouleur");
                if (antalgique != null)
                {
                    actions.Add(new CombatAction(
                        $"💊 {antalgique.Name} (+2 Encaissement, 1 PA)",
                        "Dopage 2h : +2 Encaissement pour le combat, dissipe Étourdi/Déstabilisé. Consomme 1 dose. Continue au-delà des limites.",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && FindConsumableByName(act, "Antidouleur") != null,
                        (act, tgt) =>
                        {
                            var dose = FindConsumableByName(act, "Antidouleur");
                            if (dose == null) return;
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            act.Sheet?.ConsumeOne(dose.ItemId);
                            act.NotifyInventoryChanged(true);
                            act.Stats.AddEncaissementBonus(2);
                            act.Stats.RemoveStatus(StatusEffect.Etourdi);
                            act.Stats.RemoveStatus(StatusEffect.Destabilise);
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("💊 Dopé (+2 Encaissement)", new Color(1f, 0.6f, 0.2f));
                            arena?.Log($"💊 <b>{act.Stats.Name}</b> prend <b>{dose.Name}</b> (+2 Encaissement, douleurs ignorées).");
                        }
                    ));
                }
                InventoryItem speed = FindConsumableByName(actor, "Speed");
                if (speed != null)
                {
                    actions.Add(new CombatAction(
                        $"💨 {speed.Name} (+3 PA, 1 PA)",
                        "Stimulant : +3 PA immédiats + état Rapide affiché. Consomme 1 dose.",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && FindConsumableByName(act, "Speed") != null,
                        (act, tgt) =>
                        {
                            var dose = FindConsumableByName(act, "Speed");
                            if (dose == null) return;
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            act.Sheet?.ConsumeOne(dose.ItemId);
                            act.NotifyInventoryChanged(true);
                            act.Stats.CurrentActionPoints += 3;
                            act.Stats.ApplyStatus(StatusEffect.Rapide, 3);
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("💨 SPEED (+3 PA)", Color.yellow);
                            arena?.Log($"💨 <b>{act.Stats.Name}</b> prend <b>{dose.Name}</b> (+3 PA, Rapide).");
                        }
                    ));
                }

                // RD-033 Munitions : recharger le chargeur + désenrayer (gratuit avec Kit d'Entretien).
                InventoryItem magWeapon = actor.Sheet?.GetEquippedWeapon();
                if (magWeapon != null && magWeapon.AmmoCapacity > 0)
                {
                    if (magWeapon.Jammed)
                    {
                        bool hasMaintenanceKit = HasTool(actor, "Kit d'Entretien");
                        int clearCost = hasMaintenanceKit ? 0 : 1;
                        actions.Add(new CombatAction(
                            hasMaintenanceKit ? "🔧 Désenrayer (0 PA, Kit d'Entretien)" : "🔧 Désenrayer (1 PA)",
                            $"Remet {magWeapon.Name} en batterie après un enrayement sur critique adverse.{(hasMaintenanceKit ? " Kit d'Entretien : 0 PA !" : "")}",
                            ActionCategory.TactiqueEtOrdres,
                            clearCost,
                            (act, tgt) =>
                            {
                                var w = act.Sheet?.GetEquippedWeapon();
                                int reqCost = HasTool(act, "Kit d'Entretien") ? 0 : 1;
                                return w != null && w.Jammed && act.Stats.CurrentActionPoints >= reqCost;
                            },
                            (act, tgt) =>
                            {
                                if (arena.TryClearJam(act, out string msg)) arena?.Log(msg);
                                else arena?.Log("⚠️ " + msg);
                            }
                        ));
                    }
                    else if (magWeapon.AmmoRemaining < magWeapon.AmmoCapacity)
                    {
                        int rcost = Mathf.Max(1, magWeapon.ReloadAPCost);
                        int stdStock = WeaponAmmo.StockShots(actor.Sheet, magWeapon.AmmoType, false);
                        int hdStock = magWeapon.HeavyAmmo ? WeaponAmmo.StockShots(actor.Sheet, magWeapon.AmmoType, true) : 0;
                        if (stdStock > 0)
                        {
                            actions.Add(new CombatAction(
                                $"🔋 Recharger ({rcost} PA) — {magWeapon.AmmoRemaining}/{magWeapon.AmmoCapacity}, réserve {stdStock}",
                                $"Recharge {magWeapon.Name} en charges standard (éjecte le reste HD éventuel).",
                                ActionCategory.TactiqueEtOrdres,
                                rcost,
                                (act, tgt) =>
                                {
                                    var w = act.Sheet?.GetEquippedWeapon();
                                    return w != null && !w.Jammed && w.AmmoCapacity > 0
                                        && w.AmmoRemaining < w.AmmoCapacity
                                        && WeaponAmmo.StockShots(act.Sheet, w.AmmoType, false) > 0
                                        && act.Stats.CurrentActionPoints >= Mathf.Max(1, w.ReloadAPCost);
                                },
                                (act, tgt) =>
                                {
                                    if (arena.TryReloadWeapon(act, false, out string msg)) arena?.Log(msg);
                                    else arena?.Log("⚠️ " + msg);
                                }
                            ));
                        }
                        if (hdStock > 0)
                        {
                            actions.Add(new CombatAction(
                                $"🔋 Recharger HD +1D ({rcost} PA) — {magWeapon.AmmoRemaining}/{magWeapon.AmmoCapacity}, réserve {hdStock}",
                                $"Recharge {magWeapon.Name} en Haute Densité : +1 dégât tant que chambrée (sniper/Deglazer).",
                                ActionCategory.TactiqueEtOrdres,
                                rcost,
                                (act, tgt) =>
                                {
                                    var w = act.Sheet?.GetEquippedWeapon();
                                    return w != null && !w.Jammed && w.HeavyAmmo && w.AmmoCapacity > 0
                                        && w.AmmoRemaining < w.AmmoCapacity
                                        && WeaponAmmo.StockShots(act.Sheet, w.AmmoType, true) > 0
                                        && act.Stats.CurrentActionPoints >= Mathf.Max(1, w.ReloadAPCost);
                                },
                                (act, tgt) =>
                                {
                                    if (arena.TryReloadWeapon(act, true, out string msg)) arena?.Log(msg);
                                    else arena?.Log("⚠️ " + msg);
                                }
                            ));
                        }
                    }
                }

                // Manips d'armes Livre VI : ramasser au sol (1 PA combat / gratuit explo) + swap (1 PA combat).
                int pickupPa = Killtime.Tactics.Units.DroppedWeaponPickup.PickupCostPA();
                actions.Add(new CombatAction(
                    pickupPa > 0 ? "⚔ Ramasser arme au sol (1 PA)" : "⚔ Ramasser arme au sol",
                    "Ramasse la plus proche arme au sol à portée. 1 PA en combat, gratuit en exploration.",
                    ActionCategory.TactiqueEtOrdres,
                    pickupPa,
                    (act, tgt) => act.Stats.CurrentActionPoints >= Killtime.Tactics.Units.DroppedWeaponPickup.PickupCostPA(),
                    (act, tgt) =>
                    {
                        bool ok = Killtime.Tactics.Units.DroppedWeaponPickup.TryPickupNearest(act, out string msg);
                        arena?.Log((ok ? "⚔ " : "⚠️ ") + $"<b>{act.Stats.Name}</b> : {msg}");
                    }
                ));
                if (actor.Sheet?.Inventory != null)
                {
                    int swapPa = Killtime.Tactics.Units.DroppedWeaponPickup.PickupCostPA();
                    for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
                    {
                        var sw = actor.Sheet.Inventory[i];
                        if (sw == null || sw.Type != ItemType.Weapon || sw.IsEquipped) continue;
                        if (sw.IsThrowableGrenade() || sw.IsLauncher) continue;
                        string swapName = sw.Name;
                        string swapId = sw.ItemId;
                        actions.Add(new CombatAction(
                            swapPa > 0 ? $"🔄 Équiper : {swapName} (1 PA)" : $"🔄 Équiper : {swapName}",
                            $"Swap d'arme : range l'arme en main et équipe {swapName}. Coût 1 PA en combat.",
                            ActionCategory.TactiqueEtOrdres,
                            swapPa,
                            (act, tgt) =>
                            {
                                if (act.Stats.CurrentActionPoints < Killtime.Tactics.Units.DroppedWeaponPickup.PickupCostPA()) return false;
                                var cand = act.Sheet?.Inventory?.Find(x => x != null && x.ItemId == swapId);
                                return cand != null && !cand.IsEquipped;
                            },
                            (act, tgt) =>
                            {
                                int cost = Killtime.Tactics.Units.DroppedWeaponPickup.PickupCostPA();
                                if (cost > 0 && !act.Stats.ConsumeActionPoints(cost)) return;
                                if (act.Sheet.EquipItem(swapId))
                                {
                                    act.NotifyInventoryChanged(true);
                                    var vis = act.GetComponent<TacticalUnitVisual>();
                                    vis?.SpawnFloatingText(cost > 0 ? $"🔄 {swapName} équipée (-1 PA)" : $"🔄 {swapName} équipée", Color.cyan);
                                    arena?.Log(cost > 0 ? $"🔄 <b>{act.Stats.Name}</b> équipe <b>{swapName}</b> (-1 PA)." : $"🔄 <b>{act.Stats.Name}</b> équipe <b>{swapName}</b>.");
                                }
                            }
                        ));
                    }
                }
            }

            // =========================================================================
            // 4. TACTIQUE & COMMANDEMENT (LIVRE III)
            // =========================================================================
            if (!isSelf && !isEnemy && targetIsAlive)
            {
                int orderRange = HasTool(actor, "Radio") ? 12 : 6;
                actions.Add(new CombatAction(
                    $"📢 Ordre Tactique : Couvrir (+1 PA) (2 PA, {orderRange} cases)",
                    $"Délègue un point d'action réflexe à l'allié désigné.{(orderRange > 6 ? " Radio Tactique : portée étendue." : "")}",
                    ActionCategory.TactiqueEtOrdres,
                    2,
                    (act, tgt) => act != null && act.Stats.CurrentActionPoints >= 2 && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= (HasTool(act, "Radio") ? 12 : 6),
                    (act, tgt) =>
                    {
                        if (act == null || tgt == null) return;
                        if (act.Stats.ConsumeActionPoints(2))
                        {
                            var actVis = act.GetComponent<TacticalUnitVisual>();
                            actVis?.SpawnFloatingText("Ordre Tactique (-2 PA)", new Color(0.2f, 0.85f, 1.0f));

                            tgt.Stats.CurrentActionPoints = Mathf.Min(tgt.Stats.MaxActionPoints, tgt.Stats.CurrentActionPoints + 1);
                            var tgtVis = tgt.GetComponent<TacticalUnitVisual>();
                            tgtVis?.SpawnFloatingText("+1 PA Reçu", Color.cyan);

                            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                            {
                                Killtime.Audio.KilltimeAudioManager.Instance.PlayAt(Killtime.Audio.SoundId.PA_Refill, tgt.transform.position, 0.7f);
                            }

                            arena?.Log($"📢 <b>{act.Stats.Name}</b> donne un ordre de couverture à <b>{tgt.Stats.Name}</b> (-2 PA / +1 PA réflexe) !");
                            arena?.RecordChronoSnapshot($"Ordre Tactique : {act.Stats.Name} -> {tgt.Stats.Name}");
                        }
                    }
                ));

                // Corde : Relever (1 PA) un allié À Terre adjacent. Non consommée.
                if (target.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre) && HasTool(actor, "Corde"))
                {
                    actions.Add(new CombatAction(
                        "🪢 Relever (1 PA, contact)",
                        "Remet sur pied un allié à terre (retire À Terre). La corde est réutilisable.",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && tgt.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre) && HasTool(act, "Corde"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            tgt.Stats.RemoveStatus(StatusEffect.ATerre);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🪢 RELEVÉ", Color.green);
                            arena?.Log($"🪢 <b>{act.Stats.Name}</b> relève <b>{tgt.Stats.Name}</b> à la corde.");
                        }
                    ));
                }

                // Clé Magnétique / Crochetage : Libérer (1 PA) un allié entravé. Non consommés.
                if (target.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                    && (HasTool(actor, "Clé Magnétique") || HasTool(actor, "Crochetage")))
                {
                    actions.Add(new CombatAction(
                        "🔓 Libérer (1 PA, contact)",
                        "Crochette les menottes / liens d'un allié immobilisé (retire Immobilise). Outil réutilisable.",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && tgt.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                            && (HasTool(act, "Clé Magnétique") || HasTool(act, "Crochetage")),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            tgt.Stats.RemoveStatus(StatusEffect.Immobilise);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🔓 LIBÉRÉ", Color.green);
                            arena?.Log($"🔓 <b>{act.Stats.Name}</b> libère <b>{tgt.Stats.Name}</b> de ses entraves.");
                        }
                    ));
                }

                // RD-039 : Traîner un allié (2 PA, contact) : évacue un allié immobilisé, inconscient ou à terre
                if (target.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                    || target.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient)
                    || target.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre))
                {
                    actions.Add(new CombatAction(
                        "🤼 Traîner l'allié (2 PA, 1 case)",
                        "Évacue un allié immobilisé, à terre ou inconscient d'1 case (coût 2 PA pour le sauveteur).",
                        ActionCategory.TraumatologieEtSoins,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && (arena == null || arena.CanDragBody(act, tgt)),
                        (act, tgt) => arena.ExecuteDragBody(act, tgt)
                    ));
                }

                // Boîte à Outils Arcanotech : maintenance de terrain sur un allié (1 PA, contact : bouclier ou arme enrayée).
                bool targetNeedsToolRepair = target.Sheet?.GetEquippedWeapon()?.Jammed == true
                    || (target.Stats.MaxShieldHP > 0 && target.Stats.CurrentShieldHP < target.Stats.MaxShieldHP);
                if (HasTool(actor, "Boîte à Outils") && targetNeedsToolRepair)
                {
                    actions.Add(new CombatAction(
                        "🔧 Maintenance de Terrain (1 PA, contact)",
                        "Répare le circuit du champ de force d'un allié (+10 Bouclier) ou désenraye son arme avec la Boîte à Outils (outil réutilisable).",
                        ActionCategory.TactiqueEtOrdres,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && HasTool(act, "Boîte à Outils")
                            && (tgt.Sheet?.GetEquippedWeapon()?.Jammed == true || (tgt.Stats.MaxShieldHP > 0 && tgt.Stats.CurrentShieldHP < tgt.Stats.MaxShieldHP)),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            var tgtWeapon = tgt.Sheet?.GetEquippedWeapon();
                            if (tgtWeapon != null && tgtWeapon.Jammed)
                            {
                                tgtWeapon.Jammed = false;
                                tgt.NotifyInventoryChanged(true);
                                var tgtVis = tgt.GetComponent<TacticalUnitVisual>();
                                tgtVis?.SpawnFloatingText($"🔧 {tgtWeapon.Name} DÉSENRAYÉE", Color.cyan);
                                arena?.Log($"🔧 <b>{act.Stats.Name}</b> désenraye <b>{tgtWeapon.Name}</b> de <b>{tgt.Stats.Name}</b> à la Boîte à Outils (-1 PA).");
                            }
                            else if (tgt.Stats.MaxShieldHP > 0 && tgt.Stats.CurrentShieldHP < tgt.Stats.MaxShieldHP)
                            {
                                int before = tgt.Stats.CurrentShieldHP;
                                tgt.Stats.CurrentShieldHP = Mathf.Min(tgt.Stats.MaxShieldHP, tgt.Stats.CurrentShieldHP + 10);
                                int gained = tgt.Stats.CurrentShieldHP - before;
                                var tgtVis = tgt.GetComponent<TacticalUnitVisual>();
                                tgtVis?.SpawnFloatingText($"🔧 +{gained} Bouclier", Color.cyan);
                                arena?.Log($"🔧 <b>{act.Stats.Name}</b> répare le champ de <b>{tgt.Stats.Name}</b> (+{gained} Bouclier).");
                            }
                        }
                    ));
                }

                // Radio Tactique : Ralliement d'escouade à distance (1 PA, 12 cases).
                if (HasTool(actor, "Radio") && (target.Stats.ActiveStatus.HasFlag(StatusEffect.Destabilise) || target.Stats.ActiveStatus.HasFlag(StatusEffect.Etourdi)))
                {
                    actions.Add(new CombatAction(
                        "📻 Ralliement Radio (1 PA, 12 cases)",
                        "Transmet des repères audio d'urgence par radio : dissipe Déstabilisé et Étourdi sur l'allié.",
                        ActionCategory.TactiqueEtOrdres,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 12
                            && (tgt.Stats.ActiveStatus.HasFlag(StatusEffect.Destabilise) || tgt.Stats.ActiveStatus.HasFlag(StatusEffect.Etourdi))
                            && HasTool(act, "Radio"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            tgt.Stats.RemoveStatus(StatusEffect.Destabilise);
                            tgt.Stats.RemoveStatus(StatusEffect.Etourdi);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("📻 RALLIÉ", Color.cyan);
                            arena?.Log($"📻 <b>{act.Stats.Name}</b> coordonne <b>{tgt.Stats.Name}</b> par radio (Déstabilisé/Étourdi purgés).");
                        }
                    ));
                }
            }

            if (!isSelf && isEnemy && targetIsAlive)
            {
                int tauntRange = HasTool(actor, "Radio") ? 12 : 6;
                actions.Add(new CombatAction(
                    $"🗣️ Intimidation / Provocation (2 PA, {tauntRange} cases)",
                    "Défi opposé aveugle Intimidation vs Intuition : mises masquées (PA/PE), révélation simultanée. Victoire = cible Déstabilisée (-2) et -1 PA de réaction.",
                    ActionCategory.TactiqueEtOrdres,
                    2,
                    (act, tgt) => act != null && act.Stats.CurrentActionPoints >= 2 && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= (HasTool(act, "Radio") ? 12 : 6),
                    (act, tgt) =>
                    {
                        if (act == null || tgt == null) return;
                        if (act.Stats.ConsumeActionPoints(2))
                        {
                            var calc = new CombatCalculator();
                            // Défi opposé aveugle (Livres II §7 + III) : mises déclarées
                            // avant les jets, résultat caché jusqu'à révélation simultanée.
                            // La cible résiste en aveugle (mise auto estimée, jamais de PE auto).
                            var duel = calc.ResolveOpposedCheck(
                                act.Stats, SkillType.Intimidation,
                                tgt.Stats, SkillType.Intuition,
                                attackerBonusAP: 0, attackerPE: 0,
                                defenderAutoStakes: true);

                            var actVis = act.GetComponent<TacticalUnitVisual>();
                            var tgtVis = tgt.GetComponent<TacticalUnitVisual>();
                            arena?.Log($"🗣️ <b>{act.Stats.Name}</b> intimide <b>{tgt.Stats.Name}</b> (-2 PA base, duel aveugle) :\n   {duel.CombatLog}");

                            if (duel.AttackerWins)
                            {
                                actVis?.SpawnFloatingText("Intimidation (-2 PA)", new Color(0.2f, 0.85f, 1.0f));
                                tgt.Stats.ActiveStatus |= StatusEffect.Destabilise;
                                tgtVis?.TriggerHitFlash();
                                tgtVis?.SpawnFloatingText("DÉSTABILISÉ! (-2)", Color.yellow);

                                // Amputation de 1 PA de réaction/réserve sur la cible si disponible (Livre III)
                                if (tgt.Stats.CurrentActionPoints > 0)
                                {
                                    tgt.Stats.ConsumeActionPoints(1);
                                    tgtVis?.SpawnFloatingText("-1 PA Réaction", new Color(1f, 0.6f, 0.2f));
                                }
                            }
                            else
                            {
                                actVis?.SpawnFloatingText("Intimidation contenue", new Color(0.6f, 0.6f, 0.6f));
                                tgtVis?.SpawnFloatingText("IMPASSIBLE", Color.cyan);
                            }

                            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                            {
                                Killtime.Audio.KilltimeAudioManager.Instance.PlayAt(Killtime.Audio.SoundId.Trauma_Shock, tgt.transform.position, 0.8f);
                            }

                            arena?.RecordChronoSnapshot($"Intimidation : {act.Stats.Name} -> {tgt.Stats.Name}");
                        }
                    }
                ));
            }

            // Parler à (social, éditeur de scènes) : enclenche la sortie 💬 de la
            // carte 👥 Acteur de la cible qui correspond à celui qui parle
            // (TalkEntries : réplique ou nœud). Portée 2 cases,
            // 1 PA en combat, gratuit en exploration. Disponible sur tout
            // personnage vivant autre que soi (allié comme ennemi).
            if (!isSelf && targetIsAlive)
            {
                actions.Add(new CombatAction(
                    "💬 Parler à",
                    "Engage la conversation : joue la réplique configurée sur la carte d'acteur (éditeur de scènes, output 💬 + locuteur optionnel). 1 PA en combat, gratuit en exploration.",
                    ActionCategory.TactiqueEtOrdres,
                    0,
                    (act, tgt) => act != null && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 2,
                    (act, tgt) =>
                    {
                        if (act == null || tgt == null) return;
                        var tm = Object.FindAnyObjectByType<TurnManager>();
                        bool inExploration = tm != null && tm.IsInExploration;
                        if (!inExploration)
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                        }
                        var story = Object.FindAnyObjectByType<Killtime.Story.Scenes.JsonStorySceneController>();
                        if (story != null)
                        {
                            story.TalkToActor(act, tgt);
                            arena?.RecordChronoSnapshot($"Parler à : {act.Stats.Name} -> {tgt.Stats.Name}");
                        }
                        else
                        {
                            var tgtVis = tgt.GetComponent<TacticalUnitVisual>();
                            tgtVis?.SpawnFloatingText("💬 …", Color.cyan);
                            arena?.Log($"💬 <b>{act.Stats.Name}</b> parle à <b>{tgt.Stats.Name}</b> (aucun contrôleur de scène actif).");
                        }
                    }
                ));
            }

            // =========================================================================
            // 5. TECHNIQUES DE SPÉCIALISATION (LIVRE III)
            // Clé d'Articulation, Analyse de Faille, Rugissement, Regard de
            // Prédateur, Commandement de zone, Tenir la Ligne ! — 2 PA chacune.
            // Construites par le registre partagé joueur + IA.
            // =========================================================================
            if (CombatTechniqueRegistry.HasAnyCombatTechnique(actor))
            {
                actions.AddRange(CombatTechniqueRegistry.GetTechniqueActions(actor, target, arena));
            }

            // =========================================================================
            // 5b. DUO-TECH (Lucas + Mina) — action unique, paiement immédiat des
            // deux, tissage souris T1 + onde + T2, sans expiration.
            // =========================================================================
            actions.AddRange(DuoTechRegistry.GetDuoTechActions(actor, target, arena));

            // =========================================================================
            // 6. COMMANDES DÉVELOPPEUR
            // =========================================================================
            actions.Add(new CombatAction(
                "📜 [DEV] Fiche de Personnage Complète",
                "Ouvre et affiche la fiche technique intégrale (Attributs, PA, Encaissement, Compétences).",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) => CharacterDevWindow.OpenForUnit(tgt)
            ));

            actions.Add(new CombatAction(
                "🎒 [DEV] Gérer l'Inventaire & Armes",
                "Ouvre la fenêtre d'inventaire et d'armurerie 3D pour cette unité.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    InventoryDevWindow.Open();
                    InventoryDevWindow.Instance?.InspectUnit(tgt);
                }
            ));

            actions.Add(new CombatAction(
                "⚡ [DEV] Recharger tous les PA",
                "Restaure instantanément la réserve de PA au plafond maximal.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) => tgt.Stats.CurrentActionPoints = tgt.Stats.MaxActionPoints
            ));

            actions.Add(new CombatAction(
                "❤️ [DEV] Soin Intégral & Dissipation",
                "Restaure l'intégralité des PV et annule tous les statuts négatifs.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    tgt.Stats.CurrentHealth = tgt.Stats.MaxHealth;
                    tgt.Stats.ActiveStatus = StatusEffect.None;
                    tgt.Stats.Essoufflement = 0;
                }
            ));

            actions.Add(new CombatAction(
                "💀 [DEV] Neutraliser Immédiatement (K.O.)",
                "Bascule les PV à 0 et applique l'inconscience clinique.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    tgt.Stats.CurrentHealth = 0;
                    tgt.Stats.ActiveStatus |= StatusEffect.Inconscient;
                }
            ));

            actions.Add(new CombatAction(
                "🗑️ [DEV] Retirer de la Carte (Supprimer)",
                "Désenregistre et détruit définitivement cette unité de la grille.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    if (arena != null)
                    {
                        arena.RemoveUnit(tgt);
                    }
                    else
                    {
                        var tm = Object.FindAnyObjectByType<TurnManager>();
                        tm?.UnregisterUnit(tgt);
                        Object.Destroy(tgt.gameObject);
                    }
                }
            ));

            return actions;
        }

        // Helpers consommables marketplace (Livre VIII §32.3). Résolus à l'exécution
        // pour suivre le stock réel (Quantity / ConsumeOne).
        private static InventoryItem FindBestHealingConsumable(TacticalUnit actor)
        {
            if (actor?.Sheet?.Inventory == null) return null;
            InventoryItem best = null;
            for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
            {
                var it = actor.Sheet.Inventory[i];
                if (it == null) continue;
                if (it.Type != ItemType.Consumable) continue;
                if (it.HealingAmount <= 0) continue;
                if (best == null || it.HealingAmount > best.HealingAmount) best = it;
            }
            return best;
        }

        private static InventoryItem FindConsumableByName(TacticalUnit actor, string nameFragment)
        {
            if (actor?.Sheet?.Inventory == null || string.IsNullOrEmpty(nameFragment)) return null;
            for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
            {
                var it = actor.Sheet.Inventory[i];
                if (it == null) continue;
                if (!string.IsNullOrEmpty(it.Name) && it.Name.Contains(nameFragment)) return it;
            }
            return null;
        }

        private static int CountConsumableStock(TacticalUnit actor, string itemName)
        {
            if (actor?.Sheet?.Inventory == null) return 0;
            int total = 0;
            for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
            {
                var it = actor.Sheet.Inventory[i];
                if (it == null) continue;
                if (it.Name == itemName) total += it.IsStackable ? System.Math.Max(1, it.Quantity) : 1;
            }
            return total;
        }

        // Helpers outils marketplace (possession sauf mention, §32-33).
        private static InventoryItem FindTool(TacticalUnit actor, string fragment)
        {
            var inv = actor?.Sheet?.Inventory;
            if (inv == null || string.IsNullOrEmpty(fragment)) return null;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || string.IsNullOrEmpty(it.Name)) continue;
                if (it.Name.Contains(fragment)) return it;
            }
            return null;
        }

        private static bool HasTool(TacticalUnit actor, string fragment) => FindTool(actor, fragment) != null;

        private static bool ConsumeTool(TacticalUnit actor, string fragment)
        {
            var tool = FindTool(actor, fragment);
            if (tool == null || actor?.Sheet == null) return false;
            return actor.Sheet.ConsumeOne(tool.ItemId);
        }

        private static bool HasWeakenedStatus(CharacterStats stats)
        {
            if (stats == null) return false;
            var fx = stats.ActiveStatus;
            return fx.HasFlag(StatusEffect.ATerre) || fx.HasFlag(StatusEffect.Destabilise)
                || fx.HasFlag(StatusEffect.Sonne) || fx.HasFlag(StatusEffect.Etourdi)
                || fx.HasFlag(StatusEffect.Paralyse);
        }
    }
}