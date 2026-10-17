// Feed tactique + logs ephemeres (extrait de CombatHUD.cs, comportement inchange).
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
        private readonly List<CombatLogEntry> _logEntries = new();
        private Vector2 _logScroll;
        private LogCategory _activeCategory = LogCategory.All;
        private bool _isLogDrawerExpanded = false;
        private bool _scrollLock = false;
        // Demande de retour en bas reportée au prochain Draw (où la hauteur exacte
        // du contenu est connue). Évite le float.MaxValue fragile et garantit que
        // le verrou (_scrollLock) n'est jamais contourné : quand il est actif,
        // seul un clamp de sécurité s'applique, jamais un retour en bas.
        private bool _scrollToBottomPending = false;
        // Mémorise si la vue était en bas à la fin du dernier Draw : sert à ne
        // demander un suivi en bas que si l'utilisateur y était déjà (sinon on
        // le laisse lire sans le ramener de force = "reste en place").
        private bool _logWasAtBottom = true;

        public void AddCombatLog(string message) => AddAdvancedLog(message);

        public void AddAdvancedLog(string message, LogCategory forcedCategory = LogCategory.All, string forcedTag = null, Color? forcedColor = null)
        {
            string timeStr = DateTime.Now.ToString("HH:mm:ss");
            LogCategory resolvedCategory = forcedCategory;
            string tag = forcedTag;
            Color tagCol = forcedColor ?? ColorCyanAccent;

            if (forcedCategory == LogCategory.All)
            {
                if (message.Contains("CRITIQUE") || message.Contains("CONSÉQUENCE CRITIQUE"))
                {
                    resolvedCategory = LogCategory.Combat;
                    tag = tag ?? "[CRITIQUE]";
                    tagCol = forcedColor ?? ColorAmber;
                }
                else if (message.Contains("attaque") || message.Contains("Touché") || message.Contains("DÉVIATION") || message.Contains("PARADE") || message.Contains("ESQUIVE"))
                {
                    resolvedCategory = LogCategory.Combat;
                    tag = tag ?? (message.Contains("PARADE") || message.Contains("ESQUIVE") ? "[PARADE]" : "[BALISTIQUE]");
                    tagCol = forcedColor ?? (message.Contains("PARADE") ? ColorCyanAccent : ColorCrimson);
                }
                else if (message.Contains("PA") || message.Contains("Points d'Action") || message.Contains("souffle"))
                {
                    resolvedCategory = LogCategory.ReactionAndAP;
                    tag = tag ?? "[RÉACTION]";
                    tagCol = ColorAmber;
                }
                else if (message.Contains("PV") || message.Contains("Dégâts") || message.Contains("CHOC") || message.Contains("K.O.") || message.Contains("MORT"))
                {
                    resolvedCategory = LogCategory.VitalityAndTrauma;
                    tag = tag ?? (message.Contains("MORT") ? "[CRITIQUE]" : "[IMPACT]");
                    tagCol = ColorCrimson;
                }
                else if (message.Contains("avance") || message.Contains("Tour") || message.Contains("Round"))
                {
                    resolvedCategory = LogCategory.MovementAndTurns;
                    tag = tag ?? "[PHASE]";
                    tagCol = new Color(0.7f, 0.85f, 1.0f);
                }
                else
                {
                    resolvedCategory = LogCategory.Combat;
                    tag = tag ?? "[SYSTÈME]";
                    tagCol = ColorTextMuted;
                }
            }

            _logEntries.Add(new CombatLogEntry
            {
                Timestamp = timeStr,
                RawMessage = message,
                Category = resolvedCategory,
                HeaderTag = tag,
                TagColor = tagCol,
                CreatedRealtime = Time.unscaledTime
            });

            if (_logEntries.Count > 100) _logEntries.RemoveAt(0);

            // Verrou actif => on ne touche jamais au scroll : l'utilisateur reste
            // exactement où il lit, même si des logs arrivent en rafale.
            // Verrou inactif => on ne suit en bas que si l'utilisateur y était
            // déjà (sinon il est en train de lire plus haut : on bascule en
            // verrou pour "rester en place" au lieu de le ramener de force).
            if (!_scrollLock)
            {
                if (_logWasAtBottom)
                    _scrollToBottomPending = true;
                else
                {
                    // L'utilisateur a quitté le bas pour lire : on fige la vue.
                    _scrollLock = true;
                    _scrollToBottomPending = false;
                }
            }
        }

        private void DrawAdvancedLogSystem(bool isPauseActive)
        {
            float drawerWidth = Mathf.Clamp(Screen.width * 0.44f, 480f, 660f);
            float drawerHeight = Mathf.Clamp(Screen.height * 0.35f, 220f, 320f);
            float margin = 20f;

            if (!_isLogDrawerExpanded)
            {
                if (!isPauseActive)
                {
                    DrawFloatingEphemeralLogs();
                }

                Rect toggle = new Rect(Screen.width - 92f - margin, Screen.height - 30f - margin, 92f, 22f);
                bool hover = toggle.Contains(Event.current.mousePosition);
                DrawSoftPanel(toggle, new Color(0.02f, 0.035f, 0.05f, hover ? 0.68f : 0.40f), false);
                DrawAccentLine(new Rect(toggle.x, toggle.y + toggle.height - 1f, toggle.width, 1f),
                    hover ? ColorCyanAccent : ColorCyanDim, 1f);

                GUI.color = hover ? ColorTextBright : ColorTextMuted;
                if (GUI.Button(toggle, _logEntries.Count > 0 ? $"FEED  {_logEntries.Count}" : "FEED", _btnFlatNormal))
                {
                    _isLogDrawerExpanded = true;
                    // Réouverture : en suivi on repart en bas, en verrou on garde
                    // la position mémorisée.
                    if (!_scrollLock) _scrollToBottomPending = true;
                }
                GUI.color = Color.white;
                return;
            }

            Rect terminal = new Rect(Screen.width - drawerWidth - margin,
                Screen.height - drawerHeight - margin, drawerWidth, drawerHeight);

            DrawSoftPanel(terminal, new Color(0.012f, 0.025f, 0.040f, 0.94f));
            DrawAccentLine(new Rect(terminal.x + 16, terminal.y, terminal.width - 32, 2f),
                ColorCyanAccent, 0.85f);

            GUI.Label(new Rect(terminal.x + 14, terminal.y + 10, terminal.width - 265, 18),
                "FLUX TACTIQUE // DÉTAIL DES ACTIONS", _terminalHeaderStyle);

            Rect lockBtnRect = new Rect(terminal.x + terminal.width - 246, terminal.y + 8, 108, 20);
            bool lockHover = lockBtnRect.Contains(Event.current.mousePosition);
            DrawSoftPanel(lockBtnRect, _scrollLock
                ? new Color(ColorAmber.r, ColorAmber.g, ColorAmber.b, 0.22f)
                : new Color(1f, 1f, 1f, lockHover ? 0.05f : 0.018f), false);
            DrawAccentLine(new Rect(lockBtnRect.x, lockBtnRect.y + lockBtnRect.height - 1f, lockBtnRect.width, 1f),
                _scrollLock ? ColorAmber : (lockHover ? ColorCyanAccent : ColorCyanDim), 1f);

            GUI.color = _scrollLock ? ColorAmber : (lockHover ? ColorTextBright : ColorTextMuted);
            if (GUI.Button(lockBtnRect, _scrollLock ? "🔒 SCROLL LOCK" : "🔓 AUTO-SCROLL", _btnFlatNormal))
            {
                _scrollLock = !_scrollLock;
                if (!_scrollLock)
                {
                    // Déverrouillage => retour immédiat en bas (consommé plus bas
                    // dans ce même Draw, une fois totalContentHeight connu).
                    _scrollToBottomPending = true;
                }
                else
                {
                    // Verrouillage => on annule toute demande en cours pour ne pas
                    // sauter en bas juste après avoir verrouillé.
                    _scrollToBottomPending = false;
                }
            }

            GUI.color = ColorTextMuted;
            if (GUI.Button(new Rect(terminal.x + terminal.width - 132, terminal.y + 8, 60, 20),
                "EFFACER", _btnFlatNormal))
            {
                _logEntries.Clear();
                _logScroll.y = 0f;
                _scrollToBottomPending = false;
                _logWasAtBottom = true;
            }

            if (GUI.Button(new Rect(terminal.x + terminal.width - 66, terminal.y + 8, 56, 20),
                "FERMER", _btnFlatNormal))
            {
                _isLogDrawerExpanded = false;
                GUIUtility.hotControl = 0;
                GUIUtility.keyboardControl = 0;
            }
            GUI.color = Color.white;

            Rect filters = new Rect(terminal.x + 12, terminal.y + 34, terminal.width - 24, 20);
            float fx = filters.x;
            DrawFilterPill(new Rect(fx, filters.y, 48, 18), "TOUT", LogCategory.All); fx += 51;
            DrawFilterPill(new Rect(fx, filters.y, 58, 18), "COMBAT", LogCategory.Combat); fx += 61;
            DrawFilterPill(new Rect(fx, filters.y, 42, 18), "PA", LogCategory.ReactionAndAP); fx += 45;
            DrawFilterPill(new Rect(fx, filters.y, 68, 18), "TRAUMA", LogCategory.VitalityAndTrauma); fx += 71;
            DrawFilterPill(new Rect(fx, filters.y, 52, 18), "PHASE", LogCategory.MovementAndTurns);

            float textW = terminal.width - 145f;
            float totalContentHeight = 0f;
            for (int i = 0; i < _logEntries.Count; i++)
            {
                var entry = _logEntries[i];
                if (_activeCategory != LogCategory.All && entry.Category != _activeCategory) continue;
                float h = GetLogEntryHeight(entry, textW);
                totalContentHeight += h + 6f;
            }

            Rect scrollRect = new Rect(terminal.x + 12, terminal.y + 58, terminal.width - 24, terminal.height - 70);
            // Marge basse élargie (+20) pour que la dernière ligne ne soit jamais
            // coupée ("descend mais pas complètement"), et largeur de vue = largeur
            // du scroll (le message à x=116 + textW tient avec 5px de marge, sans clip).
            float viewH = Mathf.Max(scrollRect.height, totalContentHeight + 20f);
            float maxScrollY = Mathf.Max(0f, viewH - scrollRect.height);
            _logScroll.x = 0f;
            if (_scrollLock)
            {
                // Verrou : on reste exactement où l'utilisateur a scrollé.
                // On clamp au cas où le contenu a rétréci (filtre, EFFACER) et on
                // oublie toute demande de retour en bas.
                _scrollToBottomPending = false;
                _logScroll.y = Mathf.Clamp(_logScroll.y, 0f, maxScrollY);
            }
            else
            {
                // Mode suivi : si l'utilisateur a remonté manuellement depuis la
                // dernière frame, on bascule en verrou au lieu de le ramener de
                // force (sinon "ne reste pas en place"). Sinon on colle en bas
                // en continu pour descendre complètement et y rester.
                if (!_scrollToBottomPending && _logWasAtBottom && _logScroll.y < maxScrollY - 20f && maxScrollY > 0f)
                {
                    _scrollLock = true;
                    _logScroll.y = Mathf.Clamp(_logScroll.y, 0f, maxScrollY);
                }
                else
                {
                    _logScroll.y = maxScrollY;
                    _scrollToBottomPending = false;
                }
            }
            _logScroll = GUI.BeginScrollView(scrollRect, _logScroll,
                new Rect(0, 0, scrollRect.width, viewH));

            float y = 2f;
            _diceHitRects.Clear();
            for (int i = 0; i < _logEntries.Count; i++)
            {
                var entry = _logEntries[i];
                if (_activeCategory != LogCategory.All && entry.Category != _activeCategory) continue;

                var layout = GetDiceLineLayout(entry.RawMessage, textW);
                float msgH = layout.HasDice ? layout.Height
                    : Mathf.Max(20f, _terminalBodyStyle.CalcHeight(new GUIContent(entry.RawMessage), textW));

                GUI.color = entry.TagColor;
                GUI.Label(new Rect(0, y, 70, 18), entry.HeaderTag, _terminalHeaderStyle);
                GUI.color = ColorTextMuted;
                GUI.Label(new Rect(72, y, 42, 18), entry.Timestamp, _hudSubStyle);
                GUI.color = ColorTextBright;
                if (layout.HasDice)
                    DrawDiceLineSegments(layout, entry, y);
                else
                    GUI.Label(new Rect(116, y, textW, msgH), entry.RawMessage, _terminalBodyStyle);
                GUI.color = Color.white;

                y += msgH + 6f;
            }
            GUI.EndScrollView();
            // Mémorise la position pour AddAdvancedLog : seuls les nouveaux logs
            // arrivant alors qu'on était en bas déclenchent un suivi. Si
            // l'utilisateur vient de remonter à la molette/barre en mode suivi,
            // on verrouille aussitôt pour "rester en place".
            bool atBottom = _logScroll.y >= maxScrollY - 12f;
            if (!_scrollLock && !atBottom && maxScrollY > 0f)
            {
                _scrollLock = true;
                _scrollToBottomPending = false;
            }
            _logWasAtBottom = atBottom;
            DrawDiceHoverTooltip(scrollRect);
        }

        private void DrawFilterPill(Rect r, string label, LogCategory cat)
        {
            bool selected = _activeCategory == cat;
            bool hover = r.Contains(Event.current.mousePosition);
            DrawSoftPanel(r,
                selected ? new Color(0f, 0.75f, 0.9f, 0.15f)
                         : new Color(1f, 1f, 1f, hover ? 0.05f : 0.018f), false);

            GUI.color = selected ? ColorCyanAccent : (hover ? ColorTextBright : ColorTextMuted);
            if (GUI.Button(r, label, _btnFlatNormal))
            {
                if (_activeCategory != cat)
                {
                    _activeCategory = cat;
                    // Changement de filtre : en mode auto-scroll on repart en bas,
                    // en mode verrou on reste où l'utilisateur lit.
                    if (!_scrollLock) _scrollToBottomPending = true;
                }
            }
            GUI.color = Color.white;
        }

        // =========================================================================
        // LOGS ÉPHÉMÈRES FLOTTANTS
        // =========================================================================
        private void DrawFloatingEphemeralLogs()
        {
            float width = Mathf.Clamp(Screen.width * 0.42f, 420f, 600f);
            float yOffset = Screen.height - 58f;
            float now = Time.unscaledTime;
            int drawn = 0;

            for (int i = _logEntries.Count - 1; i >= 0 && drawn < 3; i--)
            {
                var entry = _logEntries[i];
                float age = now - entry.CreatedRealtime;
                if (age > 5.5f) continue;

                float alpha = Mathf.Clamp01(age < 0.4f ? 1f : 1f - ((age - 3.5f) / 2f));
                float slide = Mathf.SmoothStep(18f, 0f, Mathf.Clamp01(age / 0.35f));

                Rect line = new Rect(Screen.width - width - 24f + slide, yOffset, width, 18f);
                Color fade = ColorTextBright;
                fade.a = alpha * 0.92f;
                _floatingLogStyle.normal.textColor = fade;

                string displayLine = entry.RawMessage.Contains('\n') 
                    ? entry.RawMessage.Split('\n')[0] 
                    : entry.RawMessage;

                GUI.Label(line, $"{entry.HeaderTag}  {displayLine}", _floatingLogStyle);

                yOffset -= 20f;
                drawn++;
            }
        }
    }
}
