using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.UI
{
    /// <summary>
    /// Barre de titre, resize, placement écran. Partial de <see cref="FloatingWindowChrome"/>.
    /// Ordre d'appel imposé : HandleResizeEvents JUSTE AVANT GUI.Window,
    /// DrawTitleBar en premier dans le callback, DrawResizeHandle en dernier.
    /// </summary>
    public static partial class FloatingWindowChrome
    {
        /// <summary>
        /// Dessine la barre de titre : fond opaque, zone de drag + boutons Minimiser / Fermer.
        /// Doit être le premier appel de DrawWindowContent.
        /// (Le premier plan est géré en amont par HandleResizeEvents, pas ici.)
        /// </summary>
        public static void DrawTitleBar(ref Rect rect, ref bool isOpen, ref bool isMinimized,
            ref Vector2 savedSize, int windowId, Action onClose = null, string title = "")
        {
            float g = DockGhostT(windowId);
            float bgAlpha = Mathf.Lerp(0.96f, 0.22f, g);

            Color prevFill = GUI.color;
            GUI.color = new Color(0.02f, 0.035f, 0.06f, bgAlpha);
            GUI.DrawTexture(new Rect(0, 0, rect.width, rect.height), BgTex());

            GUI.color = prevFill;

            float dragW = Mathf.Max(20f, rect.width - (BtnW * 2f + Pad * 3f));
            GUI.DragWindow(new Rect(0, 0, dragW, TitleHeight));

            if (!string.IsNullOrEmpty(title))
            {
                Rect titleRect = new Rect(8f, 2f, Mathf.Max(10f, dragW - 10f), 20f);
                Color prevTitleCol = GUI.color;
                GUI.color = new Color(0.92f, 0.96f, 1f, IsDocked(windowId) ? 0.75f : 1f);
                GUI.Label(titleRect, TruncateTitle(title, titleRect.width), TitleStyle());
                GUI.color = prevTitleCol;
            }

            // Le chrome d'un dock est peint séparément dans les coordonnées écran.
            // Ne jamais laisser la version réduite de GUI.Window dessiner une
            // seconde paire de commandes par-dessus celle-ci.
            if (IsDocked(windowId)) return;

            Rect minR = new Rect(rect.width - (BtnW * 2f + Pad * 2f), Pad, BtnW, BtnH);
            Rect closeR = new Rect(rect.width - (BtnW + Pad), Pad, BtnW, BtnH);

            string minLabel = isMinimized ? "▢" : "–";
            if (GUI.Button(minR, new GUIContent(minLabel, isMinimized ? "Restaurer" : "Minimiser")))
            {
                if (!isMinimized)
                {
                    savedSize = new Vector2(rect.width, rect.height);
                    isMinimized = true;
                }
                else
                {
                    isMinimized = false;
                    if (savedSize.x > 100f) rect.width = savedSize.x;
                    if (savedSize.y > 30f) rect.height = savedSize.y;
                    savedSize = Vector2.zero;
                }
            }

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.38f, 0.38f, 1f);
            if (GUI.Button(closeR, new GUIContent("✕", "Fermer")))
            {
                isOpen = false;
                isMinimized = false;
                savedSize = Vector2.zero;
                if (_resizing.ContainsKey(windowId)) _resizing[windowId] = false;
                _grabOffset.Remove(windowId);
                try { onClose?.Invoke(); } catch (Exception e) { Debug.LogWarning($"[FloatingWindowChrome] onClose: {e.Message}"); }
            }
            GUI.backgroundColor = prevBg;
        }

        /// <summary>
        /// Traite les événements pré-fenêtre : premier plan + poignée de resize.
        /// À appeler dans OnGUI JUSTE AVANT GUI.Window (dans le callback ce serait écrasé
        /// par la valeur de retour de GUI.Window, qui prend le rect par valeur).
        /// - Focus demandé via FocusWindow() (chemins d'ouverture) : appliqué ici.
        /// - Clic (tout bouton) dans la fenêtre : seule la fenêtre la plus haute sous le
        ///   curseur passe devant (pas de vol de focus, pas de Use : le contrôle cliqué reçoit l'event).
        /// - Poignée bas-droite : MouseDown consommé ici avant scrollbars/boutons, suivi absolu
        ///   (souris + offset de grab) : idempotent, pas de dérive, pas de zone morte après un clamp.
        /// e.mousePosition est ici en coordonnées GUI écran, même repère que rect : aucun décalage.
        /// </summary>
        public static void HandleResizeEvents(ref Rect rect, int windowId, Vector2 minSize, bool isMinimized = false)
        {
            // Les docks sont fixes : une fenêtre non dockée qui passe dessus ne les
            // déplace jamais (ni en drag, ni en resize, ni à l'ouverture).
            Event e = Event.current;
            int resizeControlId = GUIUtility.GetControlID(windowId, FocusType.Passive);
            if (_pendingFront.Remove(windowId))
            {
                TouchZ(windowId);
                if (e != null)
                {
                    GUI.BringWindowToFront(windowId);
                    GUI.FocusWindow(windowId);
                }
            }
            if (e == null) return;

            // Clic gauche sur un aperçu docké → restaure uniquement cet aperçu ;
            // clic sur le bouton latéral → restaure tout. Ces actions ne doivent
            // ni focuser ni redimensionner ni agir en 3D.
            if (e.type == EventType.MouseDown && e.button == 0 && ConsumeDockedClick(e.mousePosition))
            {
                e.Use();
                return;
            }

            // Fenêtre repliée : contenu non utilisable / non cliquable.
            // Consomme les autres événements souris sur l'aperçu et le bouton
            // latéral pour éviter tout click-through vers la carte. Si une fenêtre
            // normale au premier plan recouvre le dock, l'événement lui revient.
            if ((e.type == EventType.MouseUp || e.type == EventType.MouseDrag
                    || e.type == EventType.ContextClick || e.type == EventType.ScrollWheel)
                && TryGetHoveredDock(e.mousePosition, out int hoveredDockId, out _)
                && !IsCoveredByFrontNormalWindow(e.mousePosition, hoveredDockId))
            {
                e.Use();
                return;
            }

            bool resizing = _resizing.TryGetValue(windowId, out bool r) && r;

            if (resizing && (e.rawType == EventType.MouseUp || (!Input.GetMouseButton(0) && e.type != EventType.MouseDrag)))
            {
                _resizing[windowId] = false;
                resizing = false;
            }

            Vector2 mp = e.mousePosition;

            if (e.type == EventType.MouseDown && !resizing && IsTopmostAt(windowId, mp, ref rect))
            {
                int myIdx = _zOrder.IndexOf(windowId);
                if (myIdx != _zOrder.Count - 1)
                {
                    TouchZ(windowId);
                    GUI.BringWindowToFront(windowId);
                    GUI.FocusWindow(windowId);
                }
            }

            if (isMinimized || _docked.ContainsKey(windowId)) return;
            if (rect.width < ResizeGripSize * 2f || rect.height < ResizeGripSize * 2f) return;

            Rect gripScreen = new Rect(rect.x + rect.width - ResizeGripSize, rect.y + rect.height - ResizeGripSize,
                ResizeGripSize, ResizeGripSize);

            if (e.type == EventType.MouseDown && e.button == 0 && !resizing
                && gripScreen.Contains(mp) && IsTopmostAt(windowId, mp, ref rect))
            {
                _resizing[windowId] = true;
                _grabOffset[windowId] = new Vector2((rect.x + rect.width) - mp.x, (rect.y + rect.height) - mp.y);
                TouchZ(windowId);
                GUI.BringWindowToFront(windowId);
                GUI.FocusWindow(windowId);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && resizing)
            {
                Vector2 off = _grabOffset.TryGetValue(windowId, out Vector2 o) ? o : Vector2.zero;
                float maxW = Mathf.Max(minSize.x, Screen.width - rect.x - 10f);
                float maxH = Mathf.Max(minSize.y, Screen.height - rect.y - 10f);
                rect.width = Mathf.Clamp(mp.x + off.x - rect.x, minSize.x, maxW);
                rect.height = Mathf.Clamp(mp.y + off.y - rect.y, minSize.y, maxH);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && resizing)
            {
                _resizing[windowId] = false;
                e.Use();
            }
        }

        /// <summary>
        /// Dessine la poignée de redimensionnement en bas à droite (visuel seul,
        /// les événements sont gérés par HandleResizeEvents en pré-Window).
        /// Masquée si minimisé ou suivi (repli/restauration : le rect est piloté).
        /// Doit être le dernier appel de DrawWindowContent (ne rien dessiner après).
        /// </summary>
        private static GUIStyle _resizeGripStyle;
        private static GUIStyle ResizeGripStyle()
        {
            if (_resizeGripStyle == null)
            {
                _resizeGripStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0)
                };
            }
            return _resizeGripStyle;
        }

        /// <summary>
        /// Bloque toute interaction du contenu avec le coin de la poignée de redimensionnement (bas-droite).
        /// À appeler dans GUI.Window AVANT le contenu pour empêcher les scrollbars invisibles
        /// d'intercepter les clics ou la molette dans ce coin.
        /// </summary>
        public static bool ConsumeResizeGripEvent(Rect windowRectLocal, int windowId, ref Rect rect, bool isMinimized = false)
        {
            if (isMinimized || _docked.ContainsKey(windowId)) return false;
            if (windowRectLocal.width < ResizeGripSize * 2f || windowRectLocal.height < ResizeGripSize * 2f) return false;

            Event e = Event.current;
            if (e == null) return false;
            if (e.type == EventType.Used || e.type == EventType.Ignore) return false;
            if (e.type == EventType.Layout || e.type == EventType.Repaint) return false;

            Rect gripRect = new Rect(windowRectLocal.width - ResizeGripSize, windowRectLocal.height - ResizeGripSize,
                ResizeGripSize, ResizeGripSize);

            if (!gripRect.Contains(e.mousePosition)) return false;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _resizing[windowId] = true;
                Vector2 screenMouse = GUIUtility.GUIToScreenPoint(e.mousePosition);
                _grabOffset[windowId] = new Vector2((rect.x + rect.width) - screenMouse.x, (rect.y + rect.height) - screenMouse.y);
                TouchZ(windowId);
                GUI.BringWindowToFront(windowId);
                GUI.FocusWindow(windowId);
                e.Use();
                return true;
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                case EventType.MouseUp:
                case EventType.MouseDrag:
                case EventType.ContextClick:
                case EventType.ScrollWheel:
                    e.Use();
                    return true;
            }

            return false;
        }

        public static void DrawResizeHandle(ref Rect rect, int windowId, Vector2 minSize, bool isMinimized = false)
        {
            if (isMinimized || _docked.ContainsKey(windowId)) return;
            if (rect.width < 40f || rect.height < 40f) return;

            Rect gripRect = new Rect(rect.width - ResizeGripSize, rect.height - ResizeGripSize,
                ResizeGripSize, ResizeGripSize);
            bool resizing = _resizing.TryGetValue(windowId, out bool r) && r;
            Vector2 mouseLocal = Event.current != null ? Event.current.mousePosition : Vector2.zero;
            bool hovered = gripRect.Contains(mouseLocal);

            Color prevCol = GUI.color;
            GUI.color = new Color(0.02f, 0.035f, 0.06f, 1f);
            GUI.DrawTexture(gripRect, BgTex());
            GUI.color = (resizing || hovered) ? new Color(0f, 1f, 1f, 1f) : new Color(0f, 0.90f, 1f, 0.75f);
            GUI.Label(gripRect, "◢", ResizeGripStyle());
            GUI.color = prevCol;
        }

        /// <summary>
        /// Maintient la fenêtre entièrement visible à l'écran :
        /// interdit tout débordement en bas, en haut, à gauche ou à droite.
        /// </summary>
        public static void ClampToScreen(ref Rect r, Vector2 minSize = default)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;

            const float margin = 10f;
            float minW = minSize.x > 50f ? minSize.x : 200f;
            float minH = (r.height <= CollapsedHeight + 2f) ? CollapsedHeight : (minSize.y > 30f ? minSize.y : CollapsedHeight);

            float maxW = Mathf.Max(minW, Screen.width - margin * 2f);
            float maxH = Mathf.Max(minH, Screen.height - margin * 2f);

            r.width = Mathf.Clamp(r.width, minW, maxW);
            r.height = Mathf.Clamp(r.height, minH, maxH);

            if (r.yMax > Screen.height - margin)
            {
                r.y = Screen.height - margin - r.height;
            }
            if (r.y < margin)
            {
                r.y = margin;
                if (r.height > Screen.height - margin * 2f)
                {
                    r.height = Screen.height - margin * 2f;
                }
            }

            if (r.xMax > Screen.width - margin)
            {
                r.x = Screen.width - margin - r.width;
            }
            if (r.x < margin)
            {
                r.x = margin;
                if (r.width > Screen.width - margin * 2f)
                {
                    r.width = Screen.width - margin * 2f;
                }
            }
        }

        private const float OverlapMargin = 8f;

        /// <summary>
        /// Teste si un rect chevauche une zone HUD ou une autre fenêtre visible.
        /// Marge de 8px pour garantir un espace visuel (jamais de fenêtres collées / superposées).
        /// </summary>
        private static bool OverlapsBlockers(Rect candidate, int selfId)
        {
            Rect expanded = new Rect(
                candidate.x - OverlapMargin, candidate.y - OverlapMargin,
                candidate.width + OverlapMargin * 2f, candidate.height + OverlapMargin * 2f);

            // 1. Zones d'exclusion HUD enregistrées (carte joueur, cible, pause, feed, ruban).
            for (int i = 0; i < _exclusionZones.Count; i++)
            {
                var provider = _exclusionZones[i];
                if (provider == null) continue;
                Rect zone;
                try { zone = provider(); } catch { continue; }
                if (zone.width <= 0f || zone.height <= 0f) continue;
                if (expanded.Overlaps(zone, true)) return true;
            }

            // 2. Filet de sécurité HUD (si CombatHUD pas encore enregistré) : carte joueur + pause.
            // Le bouton fermé Dev Arena est déjà couvert par le provider de la fenêtre 999
            // (ouvert = grande fenêtre, fermé = petit bouton) : pas de doublon statique ici.
            if (Screen.width > 0 && Screen.height > 0)
            {
                float pw = Mathf.Clamp(Screen.width * 0.27f, 250f, 340f);
                if (expanded.Overlaps(new Rect(24f, 16f, pw, 72f), true)) return true;
                if (expanded.Overlaps(new Rect(Screen.width - 100f, 16f, 80f, 32f), true)) return true;
            }

            // 3. Autres fenêtres flottantes visibles.
            foreach (var kvp in _rectProviders)
            {
                int id = kvp.Key;
                if (id == selfId) continue;
                if (id == NoDockWindowId) continue; // menu contextuel transitoire : peut recouvrir
                try
                {
                    if (_visibilityProviders.TryGetValue(id, out var isVisible) && isVisible != null && !isVisible())
                        continue;
                    var getRect = kvp.Value;
                    if (getRect == null) continue;
                    Rect r = getRect();
                    if (r.width <= 0f || r.height <= 0f) continue;
                    if (expanded.Overlaps(r, true)) return true;
                }
                catch
                {
                    // Provider détruit : ignore ce frame.
                }
            }

            return false;
        }

        /// <summary>
        /// Retourne un rect sans chevauchement pour l'ouverture d'une fenêtre.
        /// Cascade diagonale (32x24) avec retour à la ligne, en restant sous le HUD haut (y>=96)
        /// et à l'écran. Garantit : deux fenêtres ne s'ouvrent jamais l'une sur l'autre,
        /// ni sur la carte joueur / pause / bouton Dev Arena.
        /// À appeler dans OpenInstance() avant FocusWindow().
        /// </summary>
        public static Rect FindNonOverlappingRect(Rect desired, int selfId)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return desired;
            if (selfId == NoDockWindowId) return desired; // menu contextuel : suit le curseur, pas de cascade

            float minY = 96f; // sous carte joueur (zone d'exclusion 16->88) + 8px de marge
            float maxAllowedH = Mathf.Max(CollapsedHeight, Screen.height - minY - 12f);
            float w = Mathf.Clamp(desired.width, 200f, Screen.width - 20f);
            float h = Mathf.Clamp(desired.height, CollapsedHeight, maxAllowedH);
            Rect candidate = new Rect(
                Mathf.Clamp(desired.x, 10f, Mathf.Max(10f, Screen.width - w - 10f)),
                Mathf.Clamp(desired.y, minY, Mathf.Max(minY, Screen.height - h - 10f)),
                w, h);

            if (!OverlapsBlockers(candidate, selfId)) return candidate;

            const float stepX = 32f;
            const float stepY = 24f;
            for (int attempt = 1; attempt <= 60; attempt++)
            {
                float nx = candidate.x + stepX;
                float ny = candidate.y + stepY;
                // Retour à la ligne : si on sort à droite/bas, on repart à gauche/haut (sous le HUD).
                if (nx + w > Screen.width - 10f)
                    nx = 10f + (attempt % 5) * stepX;
                if (ny + h > Screen.height - 10f)
                    ny = minY + (attempt % 7) * stepY;
                nx = Mathf.Clamp(nx, 10f, Mathf.Max(10f, Screen.width - w - 10f));
                ny = Mathf.Clamp(ny, minY, Mathf.Max(minY, Screen.height - h - 10f));
                candidate.x = nx;
                candidate.y = ny;
                if (!OverlapsBlockers(candidate, selfId)) return candidate;
            }

            return candidate;
        }

        /// <summary>
        /// Vrai si la souris survole une fenêtre flottante ou un panel HUD interactif.
        /// À appeler depuis tous les handlers 3D (clic, déplacement, peinture, zoom, orbit).
        /// Pendant un resize, bloque aussi même si la souris sort de la fenêtre.
        /// </summary>
        public static bool IsPointerOverAnyWindow()
        {
            Vector2 mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return IsPointerOverAnyWindow(mouseGui);
        }

        public static bool IsPointerOverAnyWindow(Vector2 mouseGui)
        {
            if (IsAnyResizing()) return true;

            foreach (var kvp in _rectProviders)
            {
                int id = kvp.Key;
                try
                {
                    if (_visibilityProviders.TryGetValue(id, out var isVisible) && isVisible != null && !isVisible())
                        continue;
                    var getRect = kvp.Value;
                    if (getRect == null) continue;
                    Rect r = getRect();
                    if (r.width > 0f && r.height > 0f && r.Contains(mouseGui))
                        return true;
                }
                catch
                {
                    // Provider détruit (changement de scène) : ignore ce frame.
                }
            }

            // La gouttière du bouton « restaurer tout » est en dehors du rect de
            // la fenêtre : elle doit néanmoins bloquer les interactions 3D.
            if (TryGetHoveredDock(mouseGui, out _, out _)) return true;

            for (int i = 0; i < _extraBlockers.Count; i++)
            {
                try
                {
                    var blocker = _extraBlockers[i];
                    if (blocker != null && blocker(mouseGui))
                        return true;
                }
                catch
                {
                    // Ignore blocker défaillant ce frame.
                }
            }

            return false;
        }
    }
}
