// Ciblage chirurgical, grenades, menu unite (extrait de CombatHUD.cs).
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Audio;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Objectives;

namespace Killtime.UI
{
    public partial class CombatHUD : MonoBehaviour
    {
        private BodyPart _selectedBodyPart = BodyPart.Torse;
        private bool _cancelPenaltyWithAP = false;
        private bool _showAnatomyDrawer = false;

        private bool _showActiveUnitContextMenu = true;
        private Rect _activeUnitContextMenuRect = Rect.zero;

        public void ToggleActiveUnitContextMenu()
        {
            _showActiveUnitContextMenu = !_showActiveUnitContextMenu;
        }

        public void OpenActiveUnitContextMenu()
        {
            _showActiveUnitContextMenu = true;
        }

        public void CloseActiveUnitContextMenu()
        {
            _showActiveUnitContextMenu = false;
            _activeUnitContextMenuRect = Rect.zero;
        }

        // --- Grenades (Livre VIII §31.3) : viser la case de la cible verrouillée,
        // --- lancer main (2 PA / 8 cases) ou lanceur (3 PA / 20 cases), visée +1 PA.
        private bool _showGrenadeDrawer = false;
        private float _grenadeOpenAnim;
        private string _selectedGrenadeId;
        private bool _grenadeUseLauncher = true;
        private bool _grenadeAimed = false;
        private int _grenadeBonusPA;

        private float _anatomyOpenAnim;

        // =========================================================================
        // MATRICE DE CIBLAGE — DRAWER COMPACT
        // =========================================================================
        private void DrawSurgicalTargetingMatrix(TacticalUnit unit)
        {
            float width = Mathf.Clamp(Screen.width * 0.34f, 330f, 460f);
            float height = 216f;
            // Livre V §21.3 : réserve une ligne d'affichage du couvert si la cible
            // est à couvert (le bouton TIR est repoussé vers le bas d'autant).
            CoverType preCover = _arena != null ? _arena.GetCoverToTarget(unit, _arena.CurrentTarget) : CoverType.None;
            if (preCover != CoverType.None) height += 18f;
            // RD-033 : ligne compteur chargeur si arme à munitions équipée.
            var hudWeapon = unit.Sheet?.GetEquippedWeapon();
            bool hudAmmoLine = hudWeapon != null && hudWeapon.AmmoCapacity > 0;
            if (hudAmmoLine) height += 18f;
            float targetY = Screen.height - height - 24f;
            float y = Mathf.Lerp(Screen.height + 10f, targetY, _anatomyOpenAnim);
            Rect rect = new Rect((Screen.width - width) * 0.5f, y, width, height);

            DrawSoftPanel(rect, new Color(0.018f, 0.032f, 0.050f, 0.95f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f), ColorCyanAccent, 0.9f);

            GUI.Label(new Rect(rect.x + 14, rect.y + 10, width - 60, 19),
                "CIBLAGE // ZONES VULNÉRABLES", _hudNameStyle);
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 14, rect.y + 29, width - 55, 14),
                "Sélectionnez un point d'impact.", _hudSubStyle);
            GUI.color = Color.white;

            Rect close = new Rect(rect.x + width - 34, rect.y + 8, 22, 22);
            bool closeHover = close.Contains(Event.current.mousePosition);
            GUI.color = closeHover ? ColorCrimson : ColorTextMuted;
            if (GUI.Button(close, "×", _btnFlatNormal))
            {
                _showAnatomyDrawer = false;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Close, 0.6f);
                GUI.color = Color.white;
                return;
            }
            GUI.color = Color.white;

            BodyPart[] parts = { BodyPart.Tete, BodyPart.Torse, BodyPart.BrasDroit, BodyPart.Jambes };
            float rowY = rect.y + 50f;
            float rowH = 27f;

            foreach (var p in parts)
            {
                var info = BodyPartInfo.GetInfo(p);
                bool selected = _selectedBodyPart == p;
                Rect row = new Rect(rect.x + 12f, rowY, width - 24f, rowH);
                bool hovered = row.Contains(Event.current.mousePosition);

                if (selected)
                {
                    DrawSolidRect(row, new Color(0f, 0.85f, 1f, 0.10f));
                    DrawSolidRect(new Rect(row.x, row.y, 2f, row.height), ColorCyanAccent);
                }
                else if (hovered)
                    DrawSolidRect(row, new Color(1f, 1f, 1f, 0.045f));

                GUI.color = selected ? ColorCyanAccent : (hovered ? ColorTextBright : ColorTextMuted);
                GUI.Label(new Rect(row.x + 10, row.y + 5, 100, 17),
                    info.DisplayName.ToUpperInvariant(), _hudSubStyle);

                GUI.color = ColorTextMuted;
                GUI.Label(new Rect(row.x + 112, row.y + 5, row.width - 122, 17),
                    $"MOD {info.DifficultyModifier:+0;-0;0}   CRIT ×{info.CriticalDamageMultiplier:0.##}",
                    _hudStatStyle);
                GUI.color = Color.white;

                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    if (_selectedBodyPart != p && KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_TargetChange, 0.55f);
                    _selectedBodyPart = p;
                }

                rowY += rowH + 2f;
            }

            GUI.color = ColorTextMuted;
            _cancelPenaltyWithAP = GUI.Toggle(
                new Rect(rect.x + 14f, rowY + 1f, width - 28f, 18f),
                _cancelPenaltyWithAP,
                "Visée compensée  ·  +1 PA", _hudSubStyle);
            GUI.color = Color.white;

            // Au contact : meilleure mêlée de l'attaquant (ex : Mains Nues entraînées),
            // à distance : Ballistique. Jamais de Maniement d'Arme imposé par défaut.
            var hudTarget = _arena != null ? _arena.CurrentTarget : null;
            SkillType hudAttackSkill = (hudTarget != null
                && unit.CurrentCoords.DistanceTo(hudTarget.CurrentCoords) <= 1)
                ? unit.Stats.GetBestMeleeAttackSkill()
                : SkillType.Ballistique;

            int apCost = _cancelPenaltyWithAP ? 3 : 2;
            // Livre V §21.3 : le couvert de la cible est affiché avant le tir
            // (moitié -1, 3/4 -2, total = tir impossible).
            CoverType hudCover = preCover;
            bool coverBlocks = hudCover == CoverType.Full;
            // RD-033 : vide ou enrayé = tir impossible (recharger / désenrayer).
            bool hudJammed = hudWeapon != null && hudWeapon.Jammed;
            bool hudEmpty = hudWeapon != null && hudWeapon.AmmoCapacity > 0 && hudWeapon.AmmoRemaining <= 0;
            bool canAttack = !unit.IsMoving && !coverBlocks && !hudJammed && !hudEmpty && unit.Stats.CurrentActionPoints >= apCost && unit.Stats.CanAttack(unit.Stats.GetSkillDie(hudAttackSkill, true));

            if (hudAmmoLine && hudWeapon != null)
            {
                GUI.color = hudJammed ? new Color(1f, 0.35f, 0.4f) : (hudEmpty ? new Color(1f, 0.75f, 0.3f) : new Color(0.4f, 0.85f, 1f));
                GUI.Label(new Rect(rect.x + 14f, rowY + 19f, width - 28f, 16f),
                    hudJammed ? $"🔧 {hudWeapon.Name} ENRAYÉE — désenrayer (1 PA)"
                    : hudEmpty ? $"🔋 {hudWeapon.Name} VIDE — recharger ({hudWeapon.ReloadAPCost} PA)"
                    : $"🔫 {hudWeapon.Name} [🔋{hudWeapon.AmmoRemaining}/{hudWeapon.AmmoCapacity}{(hudWeapon.LoadedHD ? "+HD" : "")}]", _hudSubStyle);
                GUI.color = Color.white;
                rowY += 18f;
            }

            if (hudCover == CoverType.Half || hudCover == CoverType.ThreeQuarters)
            {
                GUI.color = new Color(1f, 0.75f, 0.3f);
                GUI.Label(new Rect(rect.x + 14f, rowY + 19f, width - 28f, 16f),
                    hudCover == CoverType.Half ? "Couvert : moitié visible (-1 attaque)" : "Couvert : 3/4 couvert (-2 attaque)", _hudSubStyle);
                GUI.color = Color.white;
            }
            else if (coverBlocks)
            {
                GUI.color = new Color(1f, 0.35f, 0.4f);
                GUI.Label(new Rect(rect.x + 14f, rowY + 19f, width - 28f, 16f),
                    "Cible NON VISIBLE — déplacez-vous !", _hudSubStyle);
                GUI.color = Color.white;
            }

            Rect fire = new Rect(rect.x + 12f, rect.y + height - 38f, width - 24f, 28f);
            string fireLabel = coverBlocks ? "CIBLE À COUVERT"
                : hudJammed ? "ENRAYÉE — DÉSENRAYER (1 PA)"
                : hudEmpty ? "VIDE — RECHARGER"
                : (canAttack ? $"TIR  ·  {apCost} PA" : "TIR INDISPONIBLE");
            if (DrawTacticalButton(fire,
                fireLabel,
                null, false, canAttack ? ColorCrimson : ColorTextMuted, canAttack))
            {
                _showAnatomyDrawer = false;
                _arena?.ExecuteAttack(_selectedBodyPart, _cancelPenaltyWithAP, attackSkill: hudAttackSkill);
                OnAttackRequested?.Invoke(_selectedBodyPart, _cancelPenaltyWithAP);
            }
        }

        // =========================================================================
        // PANNEAU GRENADES — VISÉE DE CASE + LANCEUR + SOUFFLE
        // =========================================================================
        private void DrawGrenadeTargetingPanel(TacticalUnit unit)
        {
            float width = Mathf.Clamp(Screen.width * 0.38f, 360f, 500f);
            float height = 264f;
            float targetY = Screen.height - height - 24f;
            float y = Mathf.Lerp(Screen.height + 10f, targetY, _grenadeOpenAnim);
            Rect rect = new Rect((Screen.width - width) * 0.5f, y, width, height);

            DrawSoftPanel(rect, new Color(0.030f, 0.028f, 0.020f, 0.95f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f), ColorAmber, 0.9f);

            GUI.Label(new Rect(rect.x + 14, rect.y + 10, width - 60, 19),
                "GRENADE // SOUFFLE DE ZONE", _hudNameStyle);
            var tgt = _arena != null ? _arena.CurrentTarget : null;
            string tgtLabel = (tgt != null && tgt.Stats != null && tgt.Stats.IsAlive)
                ? $"Case visée : ({tgt.CurrentCoords.Q},{tgt.CurrentCoords.R}) — {tgt.Stats.Name}"
                : "Aucune cible verrouillée (sélectionnez un mannequin).";
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 14, rect.y + 29, width - 55, 14), tgtLabel, _hudSubStyle);
            GUI.color = Color.white;

            Rect close = new Rect(rect.x + width - 34, rect.y + 8, 22, 22);
            bool closeHover = close.Contains(Event.current.mousePosition);
            GUI.color = closeHover ? ColorCrimson : ColorTextMuted;
            if (GUI.Button(close, "×", _btnFlatNormal))
            {
                _showGrenadeDrawer = false;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Close, 0.6f);
                GUI.color = Color.white;
                return;
            }
            GUI.color = Color.white;

            var sheet = unit.GetOrBuildSheet();
            var throwables = CombatDevArena.GetThrowableGrenades(sheet);
            var launcher = CombatDevArena.GetAnyLauncher(sheet);
            int maxRange = 8;
            try
            {
                var sel = throwables.Find(g => g != null && g.ItemId == _selectedGrenadeId) ?? (throwables.Count > 0 ? throwables[0] : null);
                if (sel != null)
                {
                    _selectedGrenadeId = sel.ItemId;
                    maxRange = Killtime.Core.Combat.GrenadeRules.ComputeMaxRange(sel, (_grenadeUseLauncher ? launcher : null));
                }
            }
            catch { }

            float rowY = rect.y + 50f;
            // Sélecteur de grenade (cycle).
            Rect selRect = new Rect(rect.x + 12f, rowY, width - 24f, 26f);
            string selName = "— aucune grenade (marché : Grenades) —";
            string selSub = "";
            var current = throwables.Find(g => g != null && g.ItemId == _selectedGrenadeId);
            if (current == null && throwables.Count > 0) { current = throwables[0]; _selectedGrenadeId = current.ItemId; }
            if (current != null)
            {
                int stock = current.IsStackable ? Mathf.Max(1, current.Quantity) : 1;
                selName = $"{current.Name} x{stock}";
                selSub = $"{current.Era} • {current.GrenadeKind} • R{current.BlastRadius} • {current.BaseDamage}+{current.DamageDiceCount}d10";
            }
            if (GUI.Button(selRect, GUIContent.none, GUIStyle.none))
            {
                if (throwables.Count > 1)
                {
                    int idx = throwables.FindIndex(g => g != null && g.ItemId == _selectedGrenadeId);
                    int next = (idx + 1) % throwables.Count;
                    _selectedGrenadeId = throwables[next].ItemId;
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_TargetChange, 0.55f);
                }
            }
            GUI.color = ColorAmber;
            GUI.Label(new Rect(selRect.x + 8, selRect.y + 1, selRect.width - 16, 15), selName.ToUpperInvariant(), _hudSubStyle);
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(selRect.x + 8, selRect.y + 13, selRect.width - 16, 12), selSub, _hudSubStyle);
            GUI.color = Color.white;
            DrawSolidRect(new Rect(selRect.x, selRect.yMax - 1, selRect.width, 1), new Color(1f, 0.72f, 0.15f, 0.25f));

            rowY += 30f;
            // Toggles lanceur + visée.
            GUI.color = ColorTextMuted;
            bool hasLauncher = launcher != null;
            GUI.enabled = hasLauncher;
            bool wantLauncher = GUI.Toggle(new Rect(rect.x + 14f, rowY, (width - 28f) * 0.55f, 18f),
                _grenadeUseLauncher && hasLauncher,
                hasLauncher ? $"Lanceur : {launcher.Name} (+{launcher.LauncherRangeBonus})" : "Lanceur : — (main 8 cases)", _hudSubStyle);
            GUI.enabled = true;
            if (wantLauncher != _grenadeUseLauncher)
            {
                _grenadeUseLauncher = wantLauncher && hasLauncher;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Toggle, 0.55f);
            }
            _grenadeAimed = GUI.Toggle(
                new Rect(rect.x + 14f + (width - 28f) * 0.58f, rowY, (width - 28f) * 0.42f, 18f),
                _grenadeAimed, "Visée +1 PA", _hudSubStyle);
            GUI.color = Color.white;

            rowY += 20f;
            // Mise PA déclarée avant le lancer (+1 / PA, duel aveugle).
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 14f, rowY, 120f, 18f), $"Injection : +{_grenadeBonusPA} PA", _hudSubStyle);
            GUI.color = Color.white;
            float newBonus = GUI.HorizontalSlider(new Rect(rect.x + 140f, rowY + 4f, width - 154f, 14f), _grenadeBonusPA, 0f, 4f);
            int rounded = Mathf.RoundToInt(newBonus);
            if (rounded != _grenadeBonusPA) _grenadeBonusPA = rounded;

            rowY += 22f;
            int dist = -1;
            if (tgt != null) dist = unit.CurrentCoords.DistanceTo(tgt.CurrentCoords);
            int baseCost = (_grenadeUseLauncher && hasLauncher)
                ? Killtime.Core.Combat.GrenadeRules.LauncherShotAPCost
                : Killtime.Core.Combat.GrenadeRules.HandThrowAPCost;
            int apCost = baseCost + (_grenadeAimed ? 1 : 0) + _grenadeBonusPA;
            string rangeTxt = dist >= 0 ? $"Dist {dist} / Max {maxRange}" : $"Max {maxRange}";
            bool inRange = dist < 0 || dist <= maxRange;
            bool canThrow = current != null && tgt != null && inRange
                && unit.Stats.CurrentActionPoints >= apCost
                && unit.Stats.CanAttack(unit.Stats.GetSkillDie(SkillType.Ballistique, true));
            GUI.color = inRange ? ColorTextMuted : ColorCrimson;
            GUI.Label(new Rect(rect.x + 14f, rowY, width - 28f, 15f),
                $"{rangeTxt} • Coût {apCost} PA • Blast R{(current != null ? current.BlastRadius : 0)}", _hudSubStyle);
            GUI.color = Color.white;

            Rect launch = new Rect(rect.x + 12f, rect.y + height - 38f, width - 24f, 28f);
            if (DrawTacticalButton(launch,
                !canThrow ? "LANCER INDISPONIBLE" : $"💣 LANCER  ·  {apCost} PA",
                null, false, canThrow ? ColorAmber : ColorTextMuted, canThrow))
            {
                var targetUnit = _arena != null ? _arena.CurrentTarget : null;
                if (targetUnit != null && current != null)
                {
                    _showGrenadeDrawer = false;
                    var coords = targetUnit.CurrentCoords;
                    var gid = current.ItemId;
                    bool useL = _grenadeUseLauncher && hasLauncher;
                    bool aim = _grenadeAimed;
                    int bonus = _grenadeBonusPA;
                    _arena.ExecuteGrenadeThrow(coords, gid, useL, aim, bonus);
                    OnGrenadeRequested?.Invoke(gid, useL, aim, bonus);
                }
            }
        }

        private void DrawActiveUnitContextMenu(TacticalUnit unit)
        {
            if (_turnManager == null || _turnManager.IsInExploration || _turnManager.IsCombatOver)
            {
                _activeUnitContextMenuRect = Rect.zero;
                return;
            }

            if (unit == null || unit.Stats == null || !unit.Stats.IsAlive || !unit.IsPlayerControlled)
            {
                _activeUnitContextMenuRect = Rect.zero;
                return;
            }

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                _activeUnitContextMenuRect = Rect.zero;
                return;
            }

            Vector3 worldPos = unit.transform.position + Vector3.up * 2.30f;
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

            if (screenPos.z <= 0f)
            {
                _activeUnitContextMenuRect = Rect.zero;
                return;
            }

            float menuWidth = 194f;
            float menuHeight = 58f;
            float x = screenPos.x - (menuWidth * 0.5f);
            float y = Screen.height - screenPos.y - menuHeight;

            x = Mathf.Clamp(x, 10f, Screen.width - menuWidth - 10f);
            y = Mathf.Clamp(y, 10f, Screen.height - menuHeight - 10f);

            Rect menuRect = new Rect(x, y, menuWidth, menuHeight);
            _activeUnitContextMenuRect = menuRect;

            bool isHovered = menuRect.Contains(Event.current.mousePosition);
            DrawSoftPanel(menuRect, new Color(0.018f, 0.034f, 0.052f, isHovered ? 0.96f : 0.88f), true);
            DrawAccentLine(new Rect(menuRect.x, menuRect.y, menuRect.width, 2f), ColorCyanAccent, 0.95f);

            GUI.color = ColorCyanAccent;
            GUI.Label(new Rect(menuRect.x + 8f, menuRect.y + 4f, menuRect.width - 32f, 15f), unit.Stats.Name.ToUpperInvariant(), _hudNameStyle);
            GUI.color = ColorTextMuted;

            if (GUI.Button(new Rect(menuRect.x + menuRect.width - 20f, menuRect.y + 2f, 16f, 16f), "×", _btnFlatNormal))
            {
                CloseActiveUnitContextMenu();
                return;
            }

            int currentAp = unit.Stats.CurrentActionPoints;
            string reserveLabel = currentAp > 0 ? $"Réserve : {currentAp} PA (Réaction)" : "0 PA résiduel";
            GUI.color = currentAp > 0 ? ColorAmber : ColorTextMuted;
            GUI.Label(new Rect(menuRect.x + 8f, menuRect.y + 19f, menuRect.width - 16f, 13f), reserveLabel, _hudSubStyle);
            GUI.color = Color.white;

            Rect btnRect = new Rect(menuRect.x + 6f, menuRect.y + 33f, menuRect.width - 12f, 20f);
            if (DrawTacticalButton(btnRect, "⌛ TERMINER LE TOUR", "ESPACE", false, ColorCyanAccent, true))
            {
                TriggerEndTurn();
            }
        }

        private void TriggerEndTurn()
        {
            _showAnatomyDrawer = false;
            _showGrenadeDrawer = false;
            _showActiveUnitContextMenu = false;
            _activeUnitContextMenuRect = Rect.zero;
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.Turn_End, 0.7f);

            if (TurnManager.IsMultiplayerPlayerClient())
            {
                Killtime.Multi.VTTTableSync.Instance?.RequestEndTurn();
            }
            else
            {
                _turnManager?.EndCurrentTurn();
            }
            OnEndTurnRequested?.Invoke();
        }
    }
}
