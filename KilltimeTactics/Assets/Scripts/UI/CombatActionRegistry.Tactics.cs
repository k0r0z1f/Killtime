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
    /// §4 Tactique & commandement (Livre III) + Intimidation + Parler-à — extrait sans modification depuis CombatActionRegistry.cs.
    /// </summary>
    public static partial class CombatActionRegistry
    {
        private static void AddTacticsActions(List<CombatAction> actions, TacticalUnit actor, TacticalUnit target, CombatDevArena arena, bool isSelf, bool isEnemy, bool targetIsAlive)
        {
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
        }
    }
}
