using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.UI
{
    /// <summary>
    /// Rendu des docks + blocage du contenu docké. Partial de <see cref="FloatingWindowChrome"/>.
    /// </summary>
    public static partial class FloatingWindowChrome
    {
        /// <summary>
        /// Voile fantôme peint par-dessus le contenu replié (visuel seul).
        /// La restauration au clic est gérée en amont par HandleResizeEvents (pré-Window),
        /// et le blocage des interactions par ConsumeDockedContentEvent (pré-contenu) :
        /// ce voile ne doit PAS être un bouton, sinon l'ordre de dessin IMGUI laisserait
        /// les contrôles du contenu (dessinés avant) consommer le clic en premier.
        /// </summary>
        public static void DrawDockVeil(ref Rect rect, int windowId)
        {
            float ghost = DockGhostT(windowId);
            Rect contentRect = new Rect(0f, TitleHeight, rect.width,
                Mathf.Max(0f, rect.height - TitleHeight));
            if (ghost <= 0.01f || contentRect.width <= 0f || contentRect.height <= 0f) return;

            Color previousColor = GUI.color;
            GUI.color = new Color(0.02f, 0.03f, 0.06f, 0.28f * ghost);
            GUI.DrawTexture(contentRect, BgTex());
            GUI.color = previousColor;
        }

        /// <summary>
        /// Bloque toute interaction avec le contenu d'une fenêtre dockée.
        /// À appeler DANS le callback GUI.Window, APRÈS DrawTitleBar (titre toujours
        /// utilisable : drag / minimiser / fermer) et AVANT le contenu.
        /// Consomme l'événement courant s'il tombe dans la zone contenu, AVANT que les
        /// contrôles enfants ne le voient. Infranchissable même si un contenu force
        /// GUI.enabled=true : un événement consommé (Used) ne déclenche plus aucun contrôle.
        /// e.mousePosition est ici en coordonnées locales fenêtre (repère GUI.Window).
        /// Laisse passer Layout / Repaint / Used / Ignore pour ne pas casser le dessin.
        /// </summary>
        /// <returns>true si l'événement a été consommé (contenu non cliquable ce frame).</returns>
        public static bool ConsumeDockedContentEvent(Rect windowRectLocal, int windowId)
        {
            Event e = Event.current;
            if (e == null) return false;
            if (e.type == EventType.Used || e.type == EventType.Ignore) return false;
            if (e.type == EventType.Layout || e.type == EventType.Repaint) return false;

            // Clavier : un champ docké pourrait garder le focus un frame (avant SanitizeGuiState).
            // Avale la frappe pour qu'elle n'édite rien. Les raccourcis globaux (ToggleKeys)
            // passent par Update/Input, pas par IMGUI : non impactés.
            if (e.type == EventType.KeyDown || e.type == EventType.KeyUp)
            {
                if (GUIUtility.keyboardControl != 0)
                {
                    e.Use();
                    return true;
                }
                return false;
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                case EventType.MouseUp:
                case EventType.MouseDrag:
                case EventType.MouseMove:
                case EventType.ContextClick:
                case EventType.ScrollWheel:
                case EventType.DragUpdated:
                case EventType.DragPerform:
                case EventType.DragExited:
                    break;
                default:
                    return false;
            }

            Rect contentRect = new Rect(0f, TitleHeight, windowRectLocal.width, Mathf.Max(0f, windowRectLocal.height - TitleHeight));
            bool inContent = contentRect.width > 0f && contentRect.height > 0f
                && contentRect.Contains(e.mousePosition);
            // Aperçu docké réduit via GUI.matrix : le callback GUI.Window travaille
            // dans le repère source (pleine taille) alors que l'appelant peut passer
            // un rect à la taille du dock. Teste aussi le rect source pour que le
            // blocage reste infranchissable (cas Dev Arena : sliders/boutons larges).
            if (!inContent && _docked.TryGetValue(windowId, out DockState dockState)
                && dockState.fullRect.width > 0f && dockState.fullRect.height > 0f)
            {
                Rect sourceContent = new Rect(0f, TitleHeight, dockState.fullRect.width,
                    Mathf.Max(0f, dockState.fullRect.height - TitleHeight));
                if (sourceContent.width > 0f && sourceContent.height > 0f
                    && sourceContent.Contains(e.mousePosition))
                {
                    inContent = true;
                }
            }
            if (inContent)
            {
                // Filet de sécurité : si le pré-Window (HandleResizeEvents) a raté le
                // MouseDown (cas limite de repère), restaure cette fenêtre plutôt que
                // de l'avaler silencieusement : un clic contenu docké ne doit jamais
                // l'actionner, ni réouvrir les autres aperçus.
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    try { RestoreDockedWindow(windowId); } catch { /* ignore */ }
                }
                e.Use();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Peint le fond d'un aperçu docké dans les coordonnées écran. Contrairement
        /// à DrawTitleBar, cette méthode ne passe pas par GUI.Window : elle ne peut
        /// donc pas hériter de l'échelle utilisée pour le contenu de l'aperçu.
        /// À appeler avant le rendu du contenu réduit.
        /// </summary>
        public static void DrawDockedFrameBackground(Rect rect, int windowId)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;

            float ghost = DockGhostT(windowId);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.02f, 0.035f, 0.06f,
                Mathf.Lerp(0.96f, 0.22f, ghost) * previousColor.a);
            GUI.DrawTexture(rect, BgTex());
            GUI.color = previousColor;
        }

        /// <summary>
        /// Dessine le contour, le titre et les commandes d'un aperçu docké dans les
        /// coordonnées écran. Il reste ainsi aligné avec le rect docké même lorsque
        /// GUI.Window réduit le contenu dans son propre repère.
        /// À appeler après le rendu du contenu réduit afin que ses commandes restent
        /// toujours visibles au premier plan.
        /// </summary>
        public static void DrawDockedFrameForeground(Rect rect, ref bool isOpen, ref bool isMinimized,
            ref Vector2 savedSize, int windowId, Action onClose = null, string title = "")
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            // Dock entièrement caché sous une fenêtre normale au premier plan :
            // on ne peint rien par-dessus elle (ni boutons fantômes, ni titre).
            if (IsFullyCoveredByFrontNormalWindow(rect, windowId)) return;

            float ghost = DockGhostT(windowId);
            Color borderCol = new Color(0.0f, 0.85f, 1.0f, 0.35f * Mathf.Max(ghost, 0.2f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 1f), borderCol, 1f);
            DrawAccentLine(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), borderCol, 0.7f);
            DrawAccentLine(new Rect(rect.x, rect.y, 1f, rect.height), borderCol, 0.7f);
            DrawAccentLine(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), borderCol, 0.7f);

            float dragW = Mathf.Max(20f, rect.width - (BtnW * 2f + Pad * 3f));
            if (!string.IsNullOrEmpty(title))
            {
                Rect titleRect = new Rect(rect.x + 8f, rect.y + 2f, Mathf.Max(10f, dragW - 10f), 20f);
                // Titre recouvert : on le masque plutôt que de le peindre sur la fenêtre du dessus.
                if (!IsRectCoveredByFrontNormalWindow(titleRect, windowId))
                {
                    Color previousTitleColor = GUI.color;
                    GUI.color = new Color(0.92f, 0.96f, 1f, 0.75f);
                    GUI.Label(titleRect, TruncateTitle(title, titleRect.width), TitleStyle());
                    GUI.color = previousTitleColor;
                }
            }

            Rect minR = new Rect(rect.x + rect.width - (BtnW * 2f + Pad * 2f), rect.y + Pad, BtnW, BtnH);
            Rect closeR = new Rect(rect.x + rect.width - (BtnW + Pad), rect.y + Pad, BtnW, BtnH);

            // Commande recouverte par la fenêtre du dessus : on ne la dessine pas,
            // sinon elle resterait cliquable à travers cette fenêtre.
            if (!IsRectCoveredByFrontNormalWindow(minR, windowId))
            {
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
                        savedSize = Vector2.zero;
                    }
                }
            }

            if (!IsRectCoveredByFrontNormalWindow(closeR, windowId))
            {
                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.38f, 0.38f, 1f);
                try
                {
                    if (GUI.Button(closeR, new GUIContent("✕", "Fermer")))
                    {
                        isOpen = false;
                        isMinimized = false;
                        savedSize = Vector2.zero;
                        if (_resizing.ContainsKey(windowId)) _resizing[windowId] = false;
                        _grabOffset.Remove(windowId);
                        try { onClose?.Invoke(); } catch (Exception e) { Debug.LogWarning($"[FloatingWindowChrome] onClose: {e.Message}"); }
                    }
                }
                finally
                {
                    GUI.backgroundColor = previousBackground;
                }
            }
        }

        /// <summary>
        /// Teste les fenêtres normales visibles (non dockées, hors menu contextuel
        /// transitoire) situées DEVANT le dock indiqué. Seule une fenêtre réellement
        /// au premier plan recouvre le dock : une fenêtre derrière lui ne l'empêche
        /// ni de se restaurer ni d'afficher ses commandes.
        /// </summary>
        private static bool AnyFrontNormalWindowMatches(int dockId, Func<Rect, bool> match)
        {
            if (match == null) return false;
            int dockIdx = _zOrder.IndexOf(dockId);
            foreach (var kvp in _rectProviders)
            {
                int id = kvp.Key;
                if (id == NoDockWindowId || id == dockId) continue;
                if (_docked.ContainsKey(id)) continue;
                if (_zOrder.IndexOf(id) < dockIdx) continue; // derrière le dock : ne couvre pas
                Rect b;
                try
                {
                    if (_visibilityProviders.TryGetValue(id, out var vis) && vis != null && !vis()) continue;
                    if (kvp.Value == null) continue;
                    b = kvp.Value();
                }
                catch { continue; }
                if (b.width <= 0f || b.height <= 0f) continue;
                try
                {
                    if (match(b)) return true;
                }
                catch { /* prédicat défaillant : ignore ce bloqueur */ }
            }
            return false;
        }

        /// <summary>
        /// Vrai si le point est recouvert par une fenêtre normale visible située
        /// devant le dock. Dans ce cas la fenêtre du dessus garde le clic et le
        /// dock reste fixe à sa place.
        /// </summary>
        private static bool IsCoveredByFrontNormalWindow(Vector2 mp, int dockId)
        {
            return AnyFrontNormalWindowMatches(dockId, b => b.Contains(mp));
        }

        /// <summary>
        /// Vrai si le rect est (même partiellement) recouvert par une fenêtre normale
        /// visible située devant le dock. Sert à masquer les commandes du chrome docké
        /// qu'une fenêtre du dessus recouvre, pour ne jamais cliquer à travers elle.
        /// </summary>
        private static bool IsRectCoveredByFrontNormalWindow(Rect r, int dockId)
        {
            if (r.width <= 0f || r.height <= 0f) return false;
            return AnyFrontNormalWindowMatches(dockId, b => r.Overlaps(b));
        }

        /// <summary>
        /// Vrai si le rect est entièrement contenu dans une fenêtre normale visible
        /// située devant le dock. Le chrome docké est alors intégralement caché :
        /// on ne peint rien par-dessus la fenêtre du dessus.
        /// </summary>
        private static bool IsFullyCoveredByFrontNormalWindow(Rect r, int dockId)
        {
            if (r.width <= 0f || r.height <= 0f) return false;
            return AnyFrontNormalWindowMatches(dockId,
                b => b.xMin <= r.xMin && b.yMin <= r.yMin && b.xMax >= r.xMax && b.yMax >= r.yMax);
        }
    }
}
