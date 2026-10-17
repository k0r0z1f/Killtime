// Pause/stase/outcome/objectifs/banner (extrait de CombatHUD.cs).
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
        private bool _hideOutcomeCardForReview = false;

        private float _prePauseTimeScale = 1.0f;

        private float _roundBannerTimer;
        private const float RoundBannerDuration = 2.6f;
        private int _displayedRoundNumber = 1;

        public void TogglePause() => SetPause(!IsPaused);

        public void SetPause(bool pause)
        {
            if (IsPaused == pause) return;

            IsPaused = pause;
            if (IsPaused)
            {
                _prePauseTimeScale = Time.timeScale > 0.001f ? Time.timeScale : 1.0f;
                Time.timeScale = 0f;
                AudioListener.pause = true;
                if (KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.EnterPauseMode();
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Open, 0.7f);
                }
                AddAdvancedLog("⏸ Stase tactique engagée.", LogCategory.MovementAndTurns, "[STASE]", ColorCyanAccent);
            }
            else
            {
                Time.timeScale = _prePauseTimeScale;
                Time.fixedDeltaTime = Mathf.Clamp(0.02f * _prePauseTimeScale, 0.0002f, 0.05f);
                AudioListener.pause = false;
                if (KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.ExitPauseMode();
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Close, 0.7f);
                }
                AddAdvancedLog("▶ Reprise de la dynamique causale.", LogCategory.MovementAndTurns, "[REPRISE]", Color.green);
            }

            // Diffusion multijoueur si GM
            var room = Killtime.Multi.VTTRoomManager.Instance;
            if (room != null && room.InRoom && room.IsGM)
            {
                var payload = new Killtime.Multi.VTTTurnControlPayload
                {
                    action = pause ? Killtime.Multi.VTTProtocol.TurnActionPause : Killtime.Multi.VTTProtocol.TurnActionResume,
                    round = Killtime.Multi.VTTTableSync.Instance != null ? Killtime.Multi.VTTTableSync.Instance.CurrentRound : 1
                };
                Killtime.Multi.VTTTableSync.Instance?.BroadcastTurnControl(payload);
                if (pause)
                {
                    Killtime.Multi.VTTTableSync.Instance?.BroadcastFullCombatState();
                }
            }
        }

        /// <summary>
        /// Mémorise une nouvelle consigne de vitesse (0.01x–100x) pendant la pause :
        /// Time.timeScale reste à 0 (stase), la reprise utilisera cette valeur.
        /// Appelé par TimeScaleDevWindow.SetTimeScale quand IsPaused.
        /// </summary>
        public static void UpdatePrePauseTimeScale(float scale)
        {
            if (!IsPaused) return;
            if (Instance != null)
                Instance._prePauseTimeScale = Mathf.Clamp(scale, 0.01f, 100f);
        }

        private void DrawRoundStartBanner()
        {
            if (_roundBannerTimer <= 0f) return;

            float elapsed = RoundBannerDuration - _roundBannerTimer;
            float alpha;
            if (elapsed < 0.25f)
                alpha = Mathf.Clamp01(elapsed / 0.25f);
            else if (_roundBannerTimer < 0.45f)
                alpha = Mathf.Clamp01(_roundBannerTimer / 0.45f);
            else
                alpha = 1f;

            float width = Mathf.Clamp(Screen.width * 0.44f, 440f, 620f);
            float height = 64f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.16f;

            Rect bannerRect = new Rect(x, y, width, height);

            DrawSoftPanel(bannerRect, new Color(0.012f, 0.022f, 0.038f, 0.94f * alpha), true);

            Color cyanLine = ColorCyanAccent;
            cyanLine.a *= alpha;
            DrawAccentLine(new Rect(bannerRect.x + 20f, bannerRect.y, bannerRect.width - 40f, 2f), cyanLine, 0.95f);
            DrawAccentLine(new Rect(bannerRect.x + 40f, bannerRect.yMax - 1f, bannerRect.width - 80f, 1f), cyanLine, 0.5f);

            DrawSolidRect(new Rect(bannerRect.x, bannerRect.y + 4f, 3f, bannerRect.height - 8f), cyanLine);
            DrawSolidRect(new Rect(bannerRect.xMax - 3f, bannerRect.y + 4f, 3f, bannerRect.height - 8f), cyanLine);

            Color tagCol = ColorTextMuted;
            tagCol.a *= alpha;
            _bannerTagStyle.normal.textColor = tagCol;
            GUI.Label(new Rect(bannerRect.x, bannerRect.y + 6f, bannerRect.width, 14f),
                "// SIMULATION TEMPORELLE CAUSALE (LIVRE VI) — CYCLE DE 10 SECONDES", _bannerTagStyle);

            Color titleCol = ColorTextBright;
            titleCol.a *= alpha;
            _bannerTitleStyle.normal.textColor = titleCol;
            GUI.Label(new Rect(bannerRect.x, bannerRect.y + 20f, bannerRect.width, 24f),
                $"CYCLE TACTIQUE · ROUND {_displayedRoundNumber:00}", _bannerTitleStyle);

            Color subCol = ColorCyanAccent;
            subCol.a *= alpha;
            _bannerSubStyle.normal.textColor = subCol;
            GUI.Label(new Rect(bannerRect.x, bannerRect.y + 44f, bannerRect.width, 14f),
                "ORDRE D'INITIATIVE ENGAGÉ  ·  RÉGÉNÉRATION DES PA AU DÉBUT DE CHAQUE TOUR", _bannerSubStyle);
        }

        private void DrawTopRightPausePill()
        {
            float width = 80f;
            float margin = 20f;
            Rect rect = new Rect(Screen.width - width - margin, 22f, width, 24f);
            bool hovered = rect.Contains(Event.current.mousePosition);

            DrawSoftPanel(rect,
                new Color(0.02f, 0.035f, 0.05f, hovered || IsPaused ? 0.68f : 0.36f),
                false);

            Color accent = IsPaused ? ColorAmber : (hovered ? ColorCyanAccent : ColorCyanDim);
            DrawAccentLine(new Rect(rect.x, rect.y + rect.height - 1f, rect.width, 1f), accent, 1f);

            GUI.color = accent;
            if (GUI.Button(rect, IsPaused ? "REPRENDRE" : "PAUSE", _btnFlatNormal))
                TogglePause();
            GUI.color = Color.white;
        }

        private void DrawStasisModal()
        {
            float width = 390f;
            float height = 178f;

            DrawSolidRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(0.005f, 0.010f, 0.018f, 0.42f));

            Rect rect = new Rect((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.46f, width, height);

            DrawSoftPanel(rect, new Color(0.018f, 0.032f, 0.05f, 0.97f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f), ColorCyanAccent, 0.95f);

            GUI.color = ColorCyanAccent;
            GUI.Label(new Rect(rect.x + 22, rect.y + 20, width - 44, 22),
                "TEMPS SUSPENDU", _hudNameStyle);

            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 22, rect.y + 46, width - 44, 36),
                "Le champ tactique est figé. Reprise instantanée à la prochaine commande.",
                _terminalBodyStyle);
            GUI.color = Color.white;

            Rect resume = new Rect(rect.x + 22, rect.y + 92, width - 44, 30f);
            if (DrawTacticalButton(resume, "REPRENDRE L'ENGAGEMENT", null, false, ColorCyanAccent))
                SetPause(false);

            Rect rewind = new Rect(rect.x + 22, rect.y + 130, (width - 50f) * 0.5f, 24f);
            if (DrawTacticalButton(rewind, "REMBOBINER", null, false, ColorCyanAccent))
                _arena?.RewindLastSnapshot();

            Rect reset = new Rect(rewind.xMax + 6f, rewind.y, rewind.width, 24f);
            if (DrawTacticalButton(reset, "RÉINITIALISER", null, false, ColorCrimson))
            {
                SetPause(false);
                _arena?.ResetArena();
            }
        }

        private void DrawOutcomeCard()
        {
            if (_hideOutcomeCardForReview)
            {
                Rect reviewPill = new Rect((Screen.width - 190f) * 0.5f, 22f, 190f, 26f);
                DrawSoftPanel(reviewPill, new Color(0.018f, 0.032f, 0.05f, 0.92f), false);
                DrawAccentLine(new Rect(reviewPill.x, reviewPill.y + reviewPill.height - 1f, reviewPill.width, 1f), ColorCyanAccent, 1f);
                GUI.color = ColorCyanAccent;
                if (GUI.Button(reviewPill, "📊 RAPPORT D'ENGAGEMENT", _btnFlatNormal))
                {
                    _hideOutcomeCardForReview = false;
                }
                GUI.color = Color.white;
                return;
            }

            bool isVictory = _turnManager.CurrentOutcome == CombatOutcome.Victory;
            Color accent = isVictory ? ColorCyanAccent : ColorCrimson;
            bool isPlayerClient = TurnManager.IsMultiplayerPlayerClient();

            float extraHeight = (_turnManager != null && _turnManager.HasCustomObjectives) ? 34f : 0f;
            float width = Mathf.Clamp(Screen.width * 0.46f, 520f, 640f);
            float height = (isPlayerClient ? 285f : 335f) + extraHeight;

            DrawSolidRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(0.005f, 0.010f, 0.018f, 0.55f));

            Rect rect = new Rect((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.44f, width, height);

            DrawSoftPanel(rect, new Color(0.014f, 0.025f, 0.042f, 0.98f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f), accent, 1f);

            int roundCount = _turnManager != null ? _turnManager.CurrentRound : 1;
            int causalSeconds = roundCount * 10;

            string title = isPlayerClient
                ? (isVictory ? "🏆 ENGAGEMENT REMPORTÉ" : "💀 SIGNAUX VITAUX ROMPUS")
                : (isVictory ? "🏆 ENGAGEMENT TERMINÉ (GM)" : "💀 SIGNAUX VITAUX ROMPUS (GM)");

            string subtitle = !string.IsNullOrEmpty(_turnManager?.CombatEndReason)
                ? _turnManager.CombatEndReason
                : (isVictory
                    ? "Menace neutralisée. Télémétrie causale synchronisée avec les 11 Livres du Codex."
                    : "L'escouade a succombé au combat. Données vitales archivées.");

            GUI.color = accent;
            GUI.Label(new Rect(rect.x + 20, rect.y + 16, width - 40, 20), title, _hudNameStyle);
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 20, rect.y + 36, width - 40, 16), subtitle, _hudSubStyle);
            GUI.color = Color.white;

            var allUnits = TacticalUnitRegistry.AllUnits;
            int liveAllies = 0, deadAllies = 0, deadEnemies = 0;
            for (int i = 0; i < allUnits.Count; i++)
            {
                var u = allUnits[i];
                if (u == null || u.Stats == null) continue;
                if (u.IsPlayerControlled)
                {
                    if (u.Stats.IsAlive) liveAllies++;
                    else deadAllies++;
                }
                else
                {
                    if (!u.Stats.IsAlive) deadEnemies++;
                }
            }

            int finalNeutralized = Mathf.Max(Telemetry.EnemiesNeutralized, deadEnemies);
            int finalAlliesDown = Mathf.Max(Telemetry.AlliesDown, deadAllies);
            int totalAttacks = Telemetry.TotalAttacks;
            int hits = Telemetry.SuccessfulHits;
            int hitPct = totalAttacks > 0 ? Mathf.RoundToInt(((float)hits / totalAttacks) * 100f) : 0;

            float gridY = rect.y + 60f;
            float cellW = (width - 48f) / 3f;
            float cellH = 46f;
            float gapX = 4f;
            float gapY = 4f;

            DrawMetricCell(new Rect(rect.x + 20f, gridY, cellW, cellH), "CYCLES CAUSAUX", $"ROUND {roundCount:00}", $"{causalSeconds}s de combat (L. VI)", ColorCyanAccent);
            DrawMetricCell(new Rect(rect.x + 20f + cellW + gapX, gridY, cellW, cellH), "PRÉCISION BALISTIQUE", $"{hitPct}%", $"{hits}/{totalAttacks} touches ({Telemetry.CriticalHits} crits)", ColorAmber);
            DrawMetricCell(new Rect(rect.x + 20f + (cellW + gapX) * 2f, gridY, cellW, cellH), "DÉGÂTS INFLIGÉS", $"{Telemetry.DamageDealtByAllies} PV", $"{Telemetry.ArmorAbsorbedByEnemies} absorbés armure", Color.green);

            float row2Y = gridY + cellH + gapY;
            DrawMetricCell(new Rect(rect.x + 20f, row2Y, cellW, cellH), "DÉGÂTS SUBIS", $"{Telemetry.DamageTakenByAllies} PV", $"{Telemetry.ArmorAbsorbedByAllies} encaissés", ColorCrimson);
            DrawMetricCell(new Rect(rect.x + 20f + cellW + gapX, row2Y, cellW, cellH), "NEUTRALISATIONS", $"{finalNeutralized} cibles", $"{Telemetry.TraumaShocks} chocs traumatiques", Color.Lerp(ColorCrimson, ColorAmber, 0.5f));
            DrawMetricCell(new Rect(rect.x + 20f + (cellW + gapX) * 2f, row2Y, cellW, cellH), "BILAN ESCOUADE", $"{liveAllies} vivant(s)", $"{finalAlliesDown} hors de combat", liveAllies > 0 ? ColorCyanAccent : ColorCrimson);

            float mvpY = row2Y + cellH + gapY;
            Rect mvpRect = new Rect(rect.x + 20f, mvpY, width - 40f, 24f);
            DrawSolidRect(mvpRect, new Color(0.025f, 0.045f, 0.07f, 0.70f));
            DrawAccentLine(new Rect(mvpRect.x, mvpRect.y, 2f, mvpRect.height), ColorCyanDim, 1f);

            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(mvpRect.x + 8f, mvpY + 4f, 120f, 16f), "IMPACT MAJEUR :", _hudSubStyle);
            GUI.color = ColorTextBright;
            string mvpText = Telemetry.TopDamageAmount > 0
                ? $"<b>{Telemetry.TopDamageDealer}</b> ({Telemetry.TopDamageAmount} PV infligés) • {Telemetry.DeflectedOrParried} parades/esquives réussies"
                : "Aucune frappe décisive enregistrée ce cycle.";
            GUI.Label(new Rect(mvpRect.x + 115f, mvpY + 4f, mvpRect.width - 125f, 16f), mvpText, _hudSubStyle);
            GUI.color = Color.white;

            Rect lastDivider = mvpRect;
            if (_turnManager != null && _turnManager.HasCustomObjectives)
            {
                float objY = mvpY + 28f;
                Rect objRect = new Rect(rect.x + 20f, objY, width - 40f, 24f);
                DrawSolidRect(objRect, new Color(0.015f, 0.035f, 0.055f, 0.85f));
                DrawAccentLine(new Rect(objRect.x, objRect.y, 2f, objRect.height), ColorCyanAccent, 1f);

                string objListText = "";
                var objs = _turnManager.Objectives;
                for (int i = 0; i < objs.Count; i++)
                {
                    var o = objs[i];
                    if (o == null) continue;
                    string statusIcon = o.IsCompleted ? "<color=#00FFAA>✔</color>" : (o.IsFailed ? "<color=#FF4444>❌</color>" : "<color=#FFAA00>⏳</color>");
                    if (i > 0) objListText += "   ";
                    objListText += $"{statusIcon} <b>{o.Title}</b> ({o.GetProgressString(_turnManager)})";
                }
                GUI.color = ColorTextBright;
                GUI.Label(new Rect(objRect.x + 8f, objY + 4f, objRect.width - 16f, 16f), objListText, _hudSubStyle);
                GUI.color = Color.white;
                lastDivider = objRect;
            }

            DrawAccentLine(new Rect(rect.x + 20f, lastDivider.yMax + 8f, width - 40f, 1f), ColorCyanDim, 0.45f);

            if (isPlayerClient)
            {
                float bottomY = lastDivider.yMax + 14f;
                Rect statusRect = new Rect(rect.x + 20f, bottomY, width - 165f, 30f);
                DrawSolidRect(statusRect, new Color(0.02f, 0.05f, 0.08f, 0.75f));
                DrawAccentLine(new Rect(statusRect.x, statusRect.y, 2f, statusRect.height), ColorAmber, 0.9f);

                GUI.color = ColorAmber;
                GUI.Label(new Rect(statusRect.x + 8f, bottomY + 3f, statusRect.width - 16f, 14f), "● EN ATTENTE DU MAÎTRE DU JEU", _hudSubStyle);
                GUI.color = ColorTextMuted;
                GUI.Label(new Rect(statusRect.x + 8f, bottomY + 15f, statusRect.width - 16f, 12f), "Le MJ administre le flux temporel et la réinitialisation.", _hudSubStyle);
                GUI.color = Color.white;

                Rect observeBtn = new Rect(statusRect.xMax + 6f, bottomY, 119f, 30f);
                if (DrawTacticalButton(observeBtn, "OBSERVER", null, false, ColorCyanAccent))
                {
                    _hideOutcomeCardForReview = true;
                }
            }
            else
            {
                float bottomY = lastDivider.yMax + 14f;
                float btnW = (width - 40f - 12f) / 3f;

                Rect rewindBtn = new Rect(rect.x + 20f, bottomY, btnW, 30f);
                if (DrawTacticalButton(rewindBtn, "REMBOBINER", null, false, ColorCyanAccent))
                {
                    _arena?.RewindLastSnapshot();
                }

                Rect resetBtn = new Rect(rewindBtn.xMax + 6f, bottomY, btnW, 30f);
                if (DrawTacticalButton(resetBtn, "NOUVEL ESSAI", null, false, accent))
                {
                    _arena?.ResetArena();
                }

                Rect observeBtn = new Rect(resetBtn.xMax + 6f, bottomY, btnW, 30f);
                if (DrawTacticalButton(observeBtn, "OBSERVER", null, false, ColorTextMuted))
                {
                    _hideOutcomeCardForReview = true;
                }
            }
        }

        private static void DrawMetricCell(Rect r, string label, string mainValue, string subValue, Color accentColor)
        {
            DrawSolidRect(r, new Color(0.02f, 0.038f, 0.06f, 0.85f));
            DrawAccentLine(new Rect(r.x, r.y, 2f, r.height), accentColor, 0.8f);

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 8,
                alignment = TextAnchor.UpperLeft,
                fontStyle = FontStyle.Normal
            };
            labelStyle.normal.textColor = ColorTextMuted;

            var valStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold
            };
            valStyle.normal.textColor = accentColor;

            var subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 8,
                alignment = TextAnchor.LowerLeft,
                fontStyle = FontStyle.Normal
            };
            subStyle.normal.textColor = new Color(0.72f, 0.82f, 0.92f, 0.85f);

            GUI.Label(new Rect(r.x + 8f, r.y + 2f, r.width - 12f, 12f), label, labelStyle);
            GUI.Label(new Rect(r.x + 8f, r.y + 12f, r.width - 12f, 18f), mainValue, valStyle);
            GUI.Label(new Rect(r.x + 8f, r.y + 28f, r.width - 12f, 14f), subValue, subStyle);
        }

        private void DrawObjectivesTracker()
        {
            if (_turnManager == null || !_turnManager.HasCustomObjectives || _turnManager.IsInExploration) return;

            var objectives = _turnManager.Objectives;
            if (objectives == null || objectives.Count == 0) return;

            float cardW = Mathf.Clamp(Screen.width * 0.28f, 260f, 360f);
            float itemH = 22f;
            float headerH = 24f;
            float totalH = headerH + (objectives.Count * itemH) + 6f;

            float x = (Screen.width - cardW) * 0.5f;
            float y = 16f;

            if (_roundBannerTimer > 0f)
            {
                y = 104f;
            }

            Rect rect = new Rect(x, y, cardW, totalH);
            bool hovered = rect.Contains(Event.current.mousePosition);

            DrawSoftPanel(rect, new Color(0.015f, 0.030f, 0.048f, hovered ? 0.88f : 0.72f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f), ColorCyanAccent, 0.85f);

            GUI.color = ColorCyanAccent;
            GUI.Label(new Rect(rect.x + 12f, rect.y + 4f, cardW - 24f, 16f), "🎯 OBJECTIFS DE MISSION (RD-050)", _hudNameStyle);
            GUI.color = Color.white;

            float curY = rect.y + headerH;
            for (int i = 0; i < objectives.Count; i++)
            {
                var obj = objectives[i];
                if (obj == null) continue;

                Rect lineRect = new Rect(rect.x + 8f, curY, cardW - 16f, itemH - 2f);
                Color statusCol = obj.IsCompleted ? new Color(0.1f, 0.95f, 0.6f) :
                                 (obj.IsFailed ? new Color(1.0f, 0.3f, 0.3f) : new Color(0.85f, 0.9f, 0.95f));
                string badge = obj.IsCompleted ? "✔" : (obj.IsFailed ? "❌" : obj.GetTypeIcon());
                string optTag = obj.IsOptional ? " <color=grey>[Opt]</color>" : "";

                GUI.color = statusCol;
                GUI.Label(new Rect(lineRect.x + 4f, lineRect.y, 22f, lineRect.height), badge, _hudSubStyle);

                GUI.color = ColorTextBright;
                string titleText = $"{obj.Title}{optTag}";
                GUI.Label(new Rect(lineRect.x + 28f, lineRect.y, lineRect.width - 124f, lineRect.height), titleText, _hudSubStyle);

                GUI.color = statusCol;
                string progressText = obj.GetProgressString(_turnManager);
                GUI.Label(new Rect(lineRect.x + lineRect.width - 96f, lineRect.y, 92f, lineRect.height), progressText, _hudStatStyle);
                GUI.color = Color.white;

                curY += itemH;
            }
        }
    }
}
