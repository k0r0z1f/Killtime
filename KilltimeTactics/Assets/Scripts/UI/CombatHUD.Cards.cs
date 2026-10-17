// Cartes joueur/cible + gauges (extrait de CombatHUD.cs, comportement inchange).
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
        // Gauges dynamiques lissées
        private float _smoothPlayerHealth = -1f;
        private float _ghostPlayerHealth = -1f;
        private float _smoothPlayerAP = -1f;
        private float _smoothTargetHealth = -1f;

        private void UpdateDynamicGauges()
        {
            if (Time.timeScale <= 0.0001f) return;

            var unit = _turnManager != null ? _turnManager.ActiveUnit : (_arena != null ? _arena.PlayerUnit : null);
            if (unit != null && unit.Stats != null)
            {
                if (_smoothPlayerHealth < 0)
                {
                    _smoothPlayerHealth = unit.Stats.CurrentHealth;
                    _ghostPlayerHealth = unit.Stats.CurrentHealth;
                    _smoothPlayerAP = unit.Stats.CurrentActionPoints;
                }

                _smoothPlayerHealth = Mathf.Lerp(_smoothPlayerHealth, unit.Stats.CurrentHealth, Time.unscaledDeltaTime * 12f);
                _ghostPlayerHealth = Mathf.Lerp(_ghostPlayerHealth, _smoothPlayerHealth, Time.unscaledDeltaTime * 2.5f);
                _smoothPlayerAP = Mathf.Lerp(_smoothPlayerAP, unit.Stats.CurrentActionPoints, Time.unscaledDeltaTime * 14f);
            }

            var target = _arena != null ? _arena.CurrentTarget : null;
            if (target != null && target.Stats != null)
            {
                if (_smoothTargetHealth < 0) _smoothTargetHealth = target.Stats.CurrentHealth;
                _smoothTargetHealth = Mathf.Lerp(_smoothTargetHealth, target.Stats.CurrentHealth, Time.unscaledDeltaTime * 10f);
            }
        }

        // =========================================================================
        // CARTE JOUEUR — FICHE DE STATUT DISCRÈTE
        // =========================================================================

        private void DrawTacticalPlayerCard(TacticalUnit unit)
        {
            float width = Mathf.Clamp(Screen.width * 0.27f, 250f, 340f);
            Rect rect = new Rect(24f, 22f, width, 62f);
            bool hovered = rect.Contains(Event.current.mousePosition);

            DrawSoftPanel(rect, new Color(0.025f, 0.045f, 0.065f, hovered ? 0.70f : 0.56f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f),
                Color.Lerp(ColorCyanDim, ColorCyanAccent, hovered ? 1f : 0.62f), 0.78f);

            // Ligne titre : [Nom tronqué] [SOUFF.?] [ROUND nn] — sans chevauchement.
            const float roundW = 78f;
            Rect roundRect = new Rect(rect.x + width - 14f - roundW, rect.y + 8, roundW, 16);
            float nameRight = roundRect.x - 4f;
            Rect souffRect = new Rect();
            bool hasSouff = unit.Stats.Essoufflement > 0;
            if (hasSouff)
            {
                const float souffW = 52f;
                souffRect = new Rect(nameRight - souffW, rect.y + 8, souffW, 16);
                nameRight = souffRect.x - 6f;
            }
            float nameAvail = Mathf.Max(20f, nameRight - (rect.x + 14f));
            string name = TruncateToFit(unit.Stats.Name.ToUpperInvariant(), _hudNameStyle, nameAvail);
            GUI.Label(new Rect(rect.x + 14, rect.y + 7, nameAvail, 19), name, _hudNameStyle);

            int round = _turnManager != null ? _turnManager.CurrentRound : 1;
            GUI.color = ColorTextMuted;
            string roundLabel = (_turnManager != null && _turnManager.IsInExploration) ? "EXPLORE" : $"ROUND {round:00}";
            GUI.Label(roundRect, roundLabel, _hudSubStyle);
            GUI.color = Color.white;

            if (hasSouff)
            {
                GUI.color = ColorAmber;
                GUI.Label(souffRect, "SOUFF.", _hudSubStyle);
                GUI.color = Color.white;
            }

            float maxHp = Mathf.Max(1f, unit.Stats.MaxHealth);
            float hpRatio = Mathf.Clamp01(_smoothPlayerHealth / maxHp);
            float ghostRatio = Mathf.Clamp01(_ghostPlayerHealth / maxHp);
            Rect hp = new Rect(rect.x + 14, rect.y + 30, width - 28, 6);

            DrawSolidRect(hp, new Color(0.08f, 0.12f, 0.16f, 0.72f));
            if (ghostRatio > hpRatio)
                DrawSolidRect(new Rect(hp.x, hp.y, hp.width * ghostRatio, hp.height),
                    new Color(ColorGhostDamage.r, ColorGhostDamage.g, ColorGhostDamage.b, 0.26f));

            Color hpColor = hpRatio > 0.35f
                ? ColorCyanAccent
                : hpRatio > 0.20f
                    ? ColorAmber
                    : Color.Lerp(ColorCrimson, Color.white, Mathf.PingPong(_hudPulse * 3.5f, 1f));
            DrawSolidRect(new Rect(hp.x, hp.y, hp.width * hpRatio, hp.height), hpColor);

            int maxAp = Mathf.Max(1, unit.Stats.MaxActionPoints);
            int curAp = Mathf.Clamp(unit.Stats.CurrentActionPoints, 0, maxAp);
            float apY = rect.y + 45f;

            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 14, apY - 2, 26, 16), "PA", _hudSubStyle);
            GUI.color = Color.white;

            // Compteur à droite, pips dans l'espace restant — jamais de chevauchement même à 13+ PA.
            const float countW = 46f;
            Rect countRect = new Rect(rect.x + width - 12f - countW, apY - 3, countW, 17);
            float pipsStart = rect.x + 44f;
            float pipsAvail = Mathf.Max(0f, countRect.x - 6f - pipsStart);
            float pipW = maxAp > 0
                ? Mathf.Clamp((pipsAvail - (maxAp - 1) * 3f) / maxAp, 3f, 15f)
                : 15f;
            for (int i = 0; i < maxAp; i++)
            {
                float x = pipsStart + i * (pipW + 3f);
                if (x + pipW > countRect.x - 2f) break; // sécurité : ne jamais écrire sous le compteur
                bool filled = i < curAp;
                float pulse = filled ? 0.76f + 0.24f * Mathf.Sin(_hudPulse * 4f + i) : 0f;
                DrawSolidRect(new Rect(x, apY + 2f, pipW, 4f),
                    filled ? new Color(ColorCyanAccent.r, ColorCyanAccent.g, ColorCyanAccent.b, pulse)
                           : new Color(0.13f, 0.20f, 0.26f, 0.38f));
            }

            GUI.color = ColorTextBright;
            GUI.Label(countRect, $"{curAp}/{maxAp}", _hudStatStyle);
            GUI.color = Color.white;
        }

        // =========================================================================
        // CARTE CIBLE — POSITIONNÉE À GAUCHE DU BOUTON PAUSE
        // =========================================================================
        private void DrawTacticalTargetCard()
        {
            if (_arena == null || _arena.CurrentTarget == null || !_arena.CurrentTarget.Stats.IsAlive) return;

            var target = _arena.CurrentTarget;
            float pauseW = 80f;
            float margin = 20f;
            float width = Mathf.Clamp(Screen.width * 0.20f, 200f, 280f);
            float x = Screen.width - margin - pauseW - 12f - width;
            bool hasShield = target.Stats.MaxShieldHP > 0;
            float shieldDy = hasShield ? 13f : 0f;
            Rect rect = new Rect(x, 22f, width, 56f + shieldDy);
            bool hovered = rect.Contains(Event.current.mousePosition);

            DrawSoftPanel(rect, new Color(0.055f, 0.025f, 0.035f, hovered ? 0.70f : 0.54f));
            DrawAccentLine(new Rect(rect.x + rect.width * 0.55f, rect.y,
                rect.width * 0.45f, 2f), ColorCrimson, 0.9f);

            GUI.color = ColorCrimson;
            const float armW = 58f;
            Rect armRect = new Rect(rect.x + width - 12f - armW, rect.y + 8, armW, 15);
            float targetNameAvail = Mathf.Max(20f, armRect.x - 4f - (rect.x + 12f));
            GUI.Label(new Rect(rect.x + 12, rect.y + 7, targetNameAvail, 19),
                TruncateToFit(target.Stats.Name.ToUpperInvariant(), _hudNameStyle, targetNameAvail), _hudNameStyle);
            GUI.color = ColorTextMuted;
            int wornArmor = target.Stats.GetWornArmorBonus();
            GUI.Label(armRect, $"ARM {target.Stats.BaseArmorAbsorption + wornArmor}", _hudSubStyle);
            if (hasShield)
            {
                GUI.color = new Color(0.0f, 0.85f, 1f);
                GUI.Label(new Rect(rect.x + 12, rect.y + 25, width - 24, 12),
                    $"🔮 BOUCLIER {target.Stats.CurrentShieldHP}/{target.Stats.MaxShieldHP}", _hudSubStyle);
                GUI.color = ColorTextMuted;
            }

            float maxHp = Mathf.Max(1f, target.Stats.MaxHealth);
            float ratio = Mathf.Clamp01(_smoothTargetHealth / maxHp);
            Rect bar = new Rect(rect.x + 12, rect.y + 33 + shieldDy, width - 24, 4);
            DrawSolidRect(bar, new Color(0.20f, 0.06f, 0.09f, 0.70f));
            DrawSolidRect(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), ColorCrimson);

            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 12, rect.y + 40 + shieldDy, width - 24, 12), "CIBLE ACTIVE", _hudSubStyle);
            GUI.color = Color.white;
        }
    }
}
