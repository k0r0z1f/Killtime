using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.AI;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Objectives;
using Killtime.Core.Chrono;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.CameraSystem;
using Killtime.Multi;
using Killtime.Story;

// Partial move-only de CombatDevToolbar (facade : CombatDevToolbar.cs) — 0 logique modifiée.
namespace Killtime.UI
{
    public partial class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        private void DrawUnitStatusHeader(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{unit.Stats.Name}</b> (Round {_turnManager.CurrentRound})", GUILayout.Width(220));

            GUI.color = Color.cyan;
            GUILayout.Label($"⚡ PA: <b>{unit.Stats.CurrentActionPoints}/{unit.Stats.MaxActionPoints}</b>");

            GUI.color = Color.green;
            GUILayout.Label($"❤️ PV: <b>{unit.Stats.CurrentHealth}/{unit.Stats.MaxHealth}</b>");
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"🛡️ Encaissement: {unit.Stats.EncaissementThreshold} | Armure: {unit.Stats.BaseArmorAbsorption}", GUILayout.Width(240));

            if (unit.Stats.Essoufflement > 0)
            {
                GUI.color = Color.yellow;
                GUILayout.Label($"🫁 Essoufflement: {unit.Stats.Essoufflement}");
                GUI.color = Color.white;
            }

            if (GUILayout.Button("📜 Fiche", GUILayout.Width(70)))
            {
                CharacterDevWindow.OpenForUnit(unit);
            }

            if (GUILayout.Button("🎒 Sac", GUILayout.Width(60)))
            {
                InventoryDevWindow.Open();
                InventoryDevWindow.Instance?.InspectUnit(unit);
            }

            if (GUILayout.Button("⏭️ Fin du Tour", GUILayout.Width(110)))
            {
                _turnManager?.EndCurrentTurn();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawTargetedAttackTab(TacticalUnit activeUnit)
        {
            GUILayout.Label("<b>1. Sélection de la Cible :</b>");
            GUILayout.BeginHorizontal();

            if (_arena != null)
            {
                foreach (var dummy in _arena.SparringDummies)
                {
                    bool isSelected = (_arena.CurrentTarget == dummy);
                    GUI.backgroundColor = isSelected ? new Color(0.9f, 0.3f, 0.3f) : Color.white;
                    if (GUILayout.Button($"{dummy.Stats.Name}\n({dummy.Stats.CurrentHealth} PV)", GUILayout.Height(38)))
                    {
                        _arena.SelectTarget(dummy);
                    }
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.EndHorizontal();

            var target = _arena != null ? _arena.CurrentTarget : null;
            if (target != null && target.Stats != null)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Cible active : <b>{target.Stats.Name}</b> | PV: {target.Stats.CurrentHealth}/{target.Stats.MaxHealth} | Encaissement: {target.Stats.EncaissementThreshold} | Armure: {target.Stats.BaseArmorAbsorption} | Agi: {target.Stats.Attributes.Agilite}");
                if (GUILayout.Button("📜 Voir Fiche", GUILayout.Width(95)))
                {
                    CharacterDevWindow.OpenForUnit(target);
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>2. Anatomie Ciblée (Livre VI, Chap. 26) :</b>");

            GUILayout.BeginVertical(GUI.skin.box);
            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
            {
                var info = BodyPartInfo.GetInfo(part);
                string desc = $"{info.DisplayName} [Malus: {info.DifficultyModifier} | Crit: x{info.CriticalDamageMultiplier}]";

                bool isChosen = (_selectedPart == part);
                if (GUILayout.Toggle(isChosen, desc))
                {
                    _selectedPart = part;
                }
            }
            GUILayout.EndVertical();

            _cancelPenaltyWithAP = GUILayout.Toggle(_cancelPenaltyWithAP, "Dépenser +1 PA pour annuler le malus de visée");

            GUILayout.Space(6);
            GUILayout.Label("<b>3. Compétences d'Attaque & Défense (Axiome du Codex) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Compétence Attaque:", GUILayout.Width(130));
            if (GUILayout.Toggle(_attackSkill == SkillType.Ballistique, "Ballistique / Tir")) _attackSkill = SkillType.Ballistique;
            if (GUILayout.Toggle(_attackSkill == SkillType.ManiementArmes, "Maniement d'Arme")) _attackSkill = SkillType.ManiementArmes;
            if (GUILayout.Toggle(_attackSkill == SkillType.MainsNues, "Mains Nues")) _attackSkill = SkillType.MainsNues;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Défense Cible:", GUILayout.Width(130));
            if (GUILayout.Toggle(_defenseSkill == SkillType.Esquive, "Esquive")) _defenseSkill = SkillType.Esquive;
            if (GUILayout.Toggle(_defenseSkill == SkillType.DefenseCorporelle, "Défense Corporelle")) _defenseSkill = SkillType.DefenseCorporelle;
            if (GUILayout.Toggle(_defenseSkill == SkillType.ManiementArmes, "Parade Arme")) _defenseSkill = SkillType.ManiementArmes;
            GUILayout.EndHorizontal();

            _defenderWantsToDefend = GUILayout.Toggle(_defenderWantsToDefend, "Cible réactive (tente de parer/esquiver) / Décocher si passive (0 PA, aucune défense)");

            DiceType attDie = activeUnit != null ? activeUnit.Stats.GetSkillDie(_attackSkill, true) : DiceType.D6;
            DiceType defDie = target != null ? target.Stats.GetSkillDie(_defenseSkill) : DiceType.D4;
            int attMod = activeUnit != null ? activeUnit.Stats.GetSkillModifier(_attackSkill, isOffensive: true) : 0;
            int defMod = target != null ? target.Stats.GetSkillModifier(_defenseSkill, isOffensive: false) : 0;
            string attStatus = activeUnit != null ? activeUnit.Stats.GetStatusBreakdownString(_attackSkill, isOffensive: true) : "";
            string defStatus = target != null ? target.Stats.GetStatusBreakdownString(_defenseSkill, isOffensive: false) : "";
            string attStatusDisplay = !string.IsNullOrEmpty(attStatus) ? $" [{attStatus}]" : "";
            string defStatusDisplay = !string.IsNullOrEmpty(defStatus) ? $" [{defStatus}]" : "";

            GUILayout.Label($"<color=cyan>Attaquant : {SkillDefinitions.GetDisplayName(_attackSkill)} ➔ Dé : {attDie} (États: {attMod:+0;-0;0}{attStatusDisplay})</color> | <color=orange>Défenseur : {(_defenderWantsToDefend ? $"{SkillDefinitions.GetDisplayName(_defenseSkill)} ➔ Dé : {defDie} (États: {defMod:+0;-0;0}{defStatusDisplay})" : "Sans Défense (0)")}</color>");
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Dégâts Bruts de l'Arme : {_weaponDamage}", GUILayout.Width(200));
            _weaponDamage = (int)GUILayout.HorizontalSlider(_weaponDamage, 1, 20);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>4. Mises du duel aveugle (PA + PE, Livres II §7 + VI §24.1) :</b>");
            GUILayout.Label("<i>Déclarations masquées avant les jets : attaquant puis défenseur (aveugle), révélation simultanée. 1 PA = +1, 1 PE = +1 (plafond Constitution).</i>");

            int baseCost = _cancelPenaltyWithAP ? 3 : 2;
            int currentAttackerAP = activeUnit != null ? activeUnit.Stats.CurrentActionPoints : 0;
            int maxAttackerBonus = Mathf.Max(0, currentAttackerAP - baseCost);
            _attackerBonusAP = Mathf.Clamp(_attackerBonusAP, 0, maxAttackerBonus);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"⚡ Mise Attaquant (aveugle) : <b>+{_attackerBonusAP} PA</b>", GUILayout.Width(260));
            _attackerBonusAP = (int)GUILayout.HorizontalSlider(_attackerBonusAP, 0, maxAttackerBonus);
            GUILayout.EndHorizontal();

            int maxAttackerPE = activeUnit != null ? activeUnit.Stats.GetSpendablePE() : 0;
            _attackerPE = Mathf.Clamp(_attackerPE, 0, maxAttackerPE);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"🫁 PE Attaquant (aveugle) : <b>+{_attackerPE} PE</b> (ESS {activeUnit?.Stats.Essoufflement ?? 0}/{activeUnit?.Stats.Attributes.Constitution ?? 0})", GUILayout.Width(260));
            _attackerPE = (int)GUILayout.HorizontalSlider(_attackerPE, 0, maxAttackerPE);
            GUILayout.EndHorizontal();

            int currentDefenderAP = target != null ? target.Stats.CurrentActionPoints : 0;
            int maxDefenderBonus = Mathf.Max(0, currentDefenderAP - 1);
            _defenderBonusAP = Mathf.Clamp(_defenderBonusAP, 0, maxDefenderBonus);

            if (_defenderWantsToDefend)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"🛡️ Mise Défenseur (aveugle) : <b>+{_defenderBonusAP} PA</b>", GUILayout.Width(260));
                _defenderBonusAP = (int)GUILayout.HorizontalSlider(_defenderBonusAP, 0, maxDefenderBonus);
                GUILayout.EndHorizontal();

                int maxDefenderPE = target != null ? target.Stats.GetSpendablePE() : 0;
                _defenderPE = Mathf.Clamp(_defenderPE, 0, maxDefenderPE);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"🫁 PE Défenseur (aveugle) : <b>+{_defenderPE} PE</b> (ESS {target?.Stats.Essoufflement ?? 0}/{target?.Stats.Attributes.Constitution ?? 0})", GUILayout.Width(260));
                _defenderPE = (int)GUILayout.HorizontalSlider(_defenderPE, 0, maxDefenderPE);
                GUILayout.EndHorizontal();
            }

            int maxAttacks = activeUnit != null ? activeUnit.Stats.GetMaxAttacksAllowed(attDie) : 1;
            int attacksDone = activeUnit != null ? activeUnit.Stats.AttacksThisTurn : 0;
            bool canAttack = activeUnit != null && activeUnit.Stats.CanAttack(attDie);

            GUILayout.Space(6);
            GUILayout.Label($"Attaques du tour : <b>{attacksDone} / {maxAttacks}</b> {(maxAttacks > 1 ? "<color=cyan>(Double action active)</color>" : "<color=gray>(Requis 2d6+ pour 2e attaque)</color>")}");

            GUILayout.Space(6);
            GUI.backgroundColor = canAttack ? new Color(0.9f, 0.2f, 0.2f) : Color.gray;
            // Ne jamais réactiver un contenu docké (base : GUI.enabled=false) :
            // un `= true` inconditionnel rendrait l'aperçu cliquable.
            bool prevEnabledAttack = GUI.enabled;
            GUI.enabled = canAttack && prevEnabledAttack;
            int totalApCost = baseCost + _attackerBonusAP;
            if (GUILayout.Button(canAttack ? $"🎯 EXÉCUTER L'ATTAQUE CIBLÉE ({totalApCost} PA)" : "⚠️ QUOTA D'ATTAQUE ÉPUISÉ POUR CE TOUR", GUILayout.Height(40)))
            {
                if (_arena != null)
                {
                    _arena.ExecuteAttack(
                        targetedPart: _selectedPart, 
                        cancelPenaltyWithAP: _cancelPenaltyWithAP, 
                        attackSkill: _attackSkill, 
                        defenseSkill: _defenseSkill, 
                        defenderWantsToDefend: _defenderWantsToDefend,
                        weaponBaseDamage: _weaponDamage, 
                        attackerBonusAP: _attackerBonusAP, 
                        defenderBonusAP: _defenderBonusAP,
                        attackerPE: _attackerPE,
                        defenderPE: _defenderWantsToDefend ? _defenderPE : 0
                    );
                }
            }
            GUI.enabled = prevEnabledAttack;
            GUI.backgroundColor = Color.white;
        }
    }
}
