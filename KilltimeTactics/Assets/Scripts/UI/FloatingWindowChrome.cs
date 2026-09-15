using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.UI
{
    /// <summary>
    /// Chrome unifié pour toutes les fenêtres flottantes IMGUI de Killtime Tactics.
    /// Garantit : redimensionnable (poignée bas-droite), fermable (✕), minimisable (— / ▢),
    /// fond opaque lisible, premier plan au clic et à l'ouverture.
    /// Usage :
    ///   - Dans OnGUI, appeler HandleResizeEvents JUSTE AVANT GUI.Window (jamais dedans :
    ///     GUI.Window prend le rect par valeur et sa valeur de retour écraserait toute
    ///     modification faite pendant le callback). Y sont gérés : FocusWindow en attente,
    ///     premier plan au clic (seule la fenêtre du dessus réagit) et poignée de resize.
    ///   - Dans DrawWindowContent : DrawTitleBar en premier, return si minimisé,
    ///     contenu, puis DrawResizeHandle (visuel seul) en dernier.
    /// </summary>
    public static class FloatingWindowChrome
    {
        public const float TitleHeight = 25f;
        public const float CollapsedHeight = 26f;

        private const float BtnW = 26f;
        private const float BtnH = 18f;
        private const float Pad = 4f;
        private const float GripSize = 20f;

        private static readonly Dictionary<int, bool> _resizing = new();
        private static readonly Dictionary<int, Vector2> _grabOffset = new();
        private static readonly List<int> _zOrder = new(); // arrière -> avant (miroir de l'ordre réel)
        private static Texture2D _bgTex;
        private static GUIStyle _titleStyle;
        private static readonly HashSet<int> _pendingFront = new();

        private class DockState
        {
            public Rect fullRect;       // figé au repli : là où la fenêtre reviendra
            public Rect animFrom;       // rect de départ de l'animation en cours
            public float animStart;     // Time.realtimeSinceStartup du départ
            public bool restoring;      // true pendant réouverture (contenu déjà visible)
            public Rect targetDockRect; // rect latéral calculé sans chevauchement
        }
        private static readonly Dictionary<int, DockState> _docked = new();
        private const float DockMargin = 10f;
        private const float DockAnimDuration = 0.35f;
        public const float DockedAlpha = 0.12f;
        private const int NoDockWindowId = 777; // menu contextuel : transitoire, se ferme seul au clic map

        // Registre central des fenêtres flottantes pour bloquer les interactions 3D au survol.
        // Chaque fenêtre s'enregistre (id, getRect, isVisible). Les HUD panels passent par RegisterExtraBlocker.
        private static readonly Dictionary<int, Func<Rect>> _rectProviders = new();
        private static readonly Dictionary<int, Func<bool>> _visibilityProviders = new();
        private static readonly List<Func<Vector2, bool>> _extraBlockers = new();
        private static readonly List<Func<Rect>> _exclusionZones = new();

        public static void RegisterExclusionZone(Func<Rect> provider)
        {
            if (provider != null && !_exclusionZones.Contains(provider))
                _exclusionZones.Add(provider);
        }

        public static void UnregisterExclusionZone(Func<Rect> provider)
        {
            if (provider != null)
                _exclusionZones.Remove(provider);
        }

        /// <summary>
        /// Enregistre une fenêtre flottante. Appeler dans Awake/OnEnable, Unregister dans OnDestroy/OnDisable.
        /// </summary>
        public static void RegisterWindow(int windowId, Func<Rect> getRect, Func<bool> isVisible)
        {
            if (getRect == null || isVisible == null) return;
            _rectProviders[windowId] = getRect;
            _visibilityProviders[windowId] = isVisible;
            if (!_zOrder.Contains(windowId)) _zOrder.Add(windowId);
        }

        public static void UnregisterWindow(int windowId)
        {
            _rectProviders.Remove(windowId);
            _visibilityProviders.Remove(windowId);
            _zOrder.Remove(windowId);
            _pendingFront.Remove(windowId);
            _docked.Remove(windowId);
            _resizing.Remove(windowId);
            _grabOffset.Remove(windowId);
        }

        public static void RegisterExtraBlocker(Func<Vector2, bool> blocker)
        {
            if (blocker == null || _extraBlockers.Contains(blocker)) return;
            _extraBlockers.Add(blocker);
        }

        public static void UnregisterExtraBlocker(Func<Vector2, bool> blocker)
        {
            if (blocker == null) return;
            _extraBlockers.Remove(blocker);
        }

        /// <summary>
        /// Demande le premier plan pour une fenêtre (appliqué à son prochain DrawTitleBar).
        /// À appeler sur les chemins d'ouverture (Open(), toggle d'ouverture, bouton réouvrir).
        /// Remplace les BringWindowToFront() inconditionnels qui provoquaient des guerres de z-order.
        /// </summary>
        public static void FocusWindow(int windowId)
        {
            _pendingFront.Add(windowId);
        }

        /// <summary>
        /// Vrai tant que la fenêtre n'a pas retrouvé sa taille normale complète.
        /// </summary>
        public static bool IsDocked(int windowId)
            => _docked.ContainsKey(windowId);

        private static float Ease01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Rect LerpRect(Rect a, Rect b, float t)
        {
            return new Rect(
                Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t),
                Mathf.Lerp(a.width, b.width, t), Mathf.Lerp(a.height, b.height, t));
        }

        /// <summary>Opacité courante : 1 = normal, DockedAlpha = replié (animé entre les deux).</summary>
        public static float DockAlpha(int windowId)
        {
            return Mathf.Lerp(1f, DockedAlpha, Mathf.Clamp01(DockGhostT(windowId)));
        }

        /// <summary>Progression fantôme 0..1 (0 = normal, 1 = replié stable ; animée aux transitions).</summary>
        public static float DockGhostT(int windowId)
        {
            if (!_docked.TryGetValue(windowId, out var d)) return 0f;
            float t = Ease01((Time.realtimeSinceStartup - d.animStart) / DockAnimDuration);
            return d.restoring ? 1f - t : t;
        }

        /// <summary>
        /// Voile fantôme peint par-dessus le contenu replié (visuel seul).
        /// La restauration au clic est gérée en amont par HandleResizeEvents (pré-Window),
        /// et le blocage des interactions par ConsumeDockedContentEvent (pré-contenu) :
        /// ce voile ne doit PAS être un bouton, sinon l'ordre de dessin IMGUI laisserait
        /// les contrôles du contenu (dessinés avant) consommer le clic en premier,
        /// et GUI.enabled=false serait contourné par les contenus qui forcent GUI.enabled=true.
        /// </summary>
        public static void DrawDockVeil(ref Rect rect, int windowId)
        {
            float g = DockGhostT(windowId);
            Rect contentRect = new Rect(0f, TitleHeight, rect.width, Mathf.Max(0f, rect.height - TitleHeight));
            if (contentRect.width <= 0f || contentRect.height <= 0f) return;

            if (g <= 0.01f) return;
            Color prev = GUI.color;
            GUI.color = new Color(0.02f, 0.03f, 0.06f, 0.9f * g);
            GUI.DrawTexture(contentRect, BgTex());
            GUI.color = prev;
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
        public static bool ConsumeDockedContentEvent(Rect windowRectLocal)
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
            if (contentRect.width <= 0f || contentRect.height <= 0f) return false;
            if (contentRect.Contains(e.mousePosition))
            {
                // Filet de sécurité : si le pré-Window (HandleResizeEvents) a raté le
                // MouseDown (cas limite de repère), restaure ici plutôt que de l'avaler
                // silencieusement : un clic contenu docké ne doit jamais l'actionner.
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    try { RestoreAll(); } catch { /* ignore */ }
                }
                e.Use();
                return true;
            }
            return false;
        }

        private static bool ProviderVisible(int windowId)
        {
            if (!_visibilityProviders.TryGetValue(windowId, out var vis) || vis == null) return false;
            try { return vis(); } catch { return false; }
        }

        private static bool ProviderRect(int windowId, out Rect rect)
        {
            rect = default;
            if (!_rectProviders.TryGetValue(windowId, out var getRect) || getRect == null) return false;
            try { rect = getRect(); } catch { return false; }
            return rect.width > 0f && rect.height > 0f;
        }

        /// <summary>
        /// Replie les fenêtres ouvertes vers le bord d'écran le plus proche (gauche ou droite)
        /// en conservant au plus près leur hauteur d'origine et en interdisant tout chevauchement.
        /// </summary>
        public static void OnMapClicked()
        {
            PurgeStaleDock();
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < _zOrder.Count; i++)
            {
                int id = _zOrder[i];
                if (id == NoDockWindowId) continue;
                if (!ProviderVisible(id)) continue;
                if (!ProviderRect(id, out Rect cur)) continue;
                if (cur.height <= CollapsedHeight + 1f) continue;

                if (_docked.TryGetValue(id, out var ex))
                {
                    ex.restoring = false;
                    ex.animFrom = cur;
                    ex.animStart = now;
                }
                else
                {
                    _docked[id] = new DockState
                    {
                        fullRect = cur,
                        animFrom = cur,
                        animStart = now,
                        restoring = false
                    };
                }
            }

            RecalculateDockTargets();
        }

        /// <summary>
        /// Restaure toutes les fenêtres repliées : le retour est animé vers leur rect d'origine.
        /// </summary>
        public static bool RestoreAll()
        {
            PurgeStaleDock();
            if (_docked.Count == 0) return false;
            float now = Time.realtimeSinceStartup;
            foreach (var kvp in _docked)
            {
                if (ProviderRect(kvp.Key, out Rect cur)) kvp.Value.animFrom = cur;
                kvp.Value.restoring = true;
                kvp.Value.animStart = now;
            }
            return true;
        }

        private static void PurgeStaleDock()
        {
            if (_docked.Count == 0) return;
            List<int> stale = null;
            foreach (var kvp in _docked)
            {
                if (!ProviderVisible(kvp.Key)) (stale ??= new List<int>()).Add(kvp.Key);
            }
            if (stale == null) return;
            foreach (int id in stale) _docked.Remove(id);
        }

        private struct DockEntry
        {
            public int id;
            public DockState state;
        }

        private static void RecalculateDockTargets()
        {
            if (_docked.Count == 0) return;

            var leftList = new List<DockEntry>();
            var rightList = new List<DockEntry>();
            float screenMidX = Screen.width * 0.5f;

            foreach (var kvp in _docked)
            {
                if (!ProviderVisible(kvp.Key)) continue;
                var state = kvp.Value;
                float midX = state.fullRect.x + state.fullRect.width * 0.5f;
                if (midX < screenMidX)
                    leftList.Add(new DockEntry { id = kvp.Key, state = state });
                else
                    rightList.Add(new DockEntry { id = kvp.Key, state = state });
            }

            LayoutDockColumn(leftList, isRight: false);
            LayoutDockColumn(rightList, isRight: true);
        }

        private static void LayoutDockColumn(List<DockEntry> list, bool isRight)
        {
            int count = list.Count;
            if (count == 0) return;

            float w = Mathf.Clamp(Screen.width * 0.18f, 240f, 320f);
            float colX = isRight ? Mathf.Max(DockMargin, Screen.width - DockMargin - w) : DockMargin;
            Rect colRect = new Rect(colX, 0f, w, Screen.height);

            // Planchers de sécurité : protège les cartes HUD (Joueur à gauche, Cible et Pause à droite)
            float topSafeY = isRight ? 90f : 94f;
            float bottomSafeY = Screen.height - 60f;

            // Détection dynamique des zones d'exclusion enregistrées (cartes HUD, barres, menus)
            for (int i = 0; i < _exclusionZones.Count; i++)
            {
                var provider = _exclusionZones[i];
                if (provider == null) continue;
                Rect zone;
                try { zone = provider(); } catch { continue; }
                if (zone.width <= 0f || zone.height <= 0f) continue;

                if (zone.xMax > colRect.xMin && zone.xMin < colRect.xMax)
                {
                    float zoneCenterY = zone.y + zone.height * 0.5f;
                    if (zoneCenterY < Screen.height * 0.5f)
                    {
                        float neededTop = zone.yMax + DockMargin;
                        if (neededTop > topSafeY) topSafeY = neededTop;
                    }
                    else
                    {
                        float neededBottom = zone.yMin - DockMargin;
                        if (neededBottom < bottomSafeY) bottomSafeY = neededBottom;
                    }
                }
            }

            float maxAvailableHeight = Mathf.Max(100f, bottomSafeY - topSafeY - (count - 1) * DockMargin);
            float baseH = Mathf.Clamp((Screen.height - 20f) / 3f - DockMargin, 160f, 260f);

            float h = (count * baseH > maxAvailableHeight && count > 0)
                ? Mathf.Max(100f, maxAvailableHeight / count)
                : baseH;

            // Tri par hauteur d'origine pour préserver l'ordre spatial
            list.Sort((a, b) => a.state.fullRect.y.CompareTo(b.state.fullRect.y));

            float[] ys = new float[count];
            float maxBottomY = Mathf.Max(topSafeY, bottomSafeY - h);
            for (int i = 0; i < count; i++)
            {
                ys[i] = Mathf.Clamp(list[i].state.fullRect.y, topSafeY, maxBottomY);
            }

            // 1. Balayage avant : interdit le chevauchement vers le bas
            for (int i = 1; i < count; i++)
            {
                if (ys[i] < ys[i - 1] + h + DockMargin)
                    ys[i] = ys[i - 1] + h + DockMargin;
            }

            // 2. Balayage arrière : interdit le débordement sous la zone basse
            if (ys[count - 1] > maxBottomY)
            {
                ys[count - 1] = maxBottomY;
                for (int i = count - 2; i >= 0; i--)
                {
                    if (ys[i] + h + DockMargin > ys[i + 1])
                        ys[i] = ys[i + 1] - h - DockMargin;
                }
            }

            // 3. Répartition uniforme si la colonne complète dépasse l'espace utile
            if (ys[0] < topSafeY)
            {
                float step = (count > 1)
                    ? (bottomSafeY - topSafeY - h) / (count - 1)
                    : 0f;
                for (int i = 0; i < count; i++)
                    ys[i] = topSafeY + i * step;
            }

            for (int i = 0; i < count; i++)
            {
                list[i].state.targetDockRect = new Rect(colX, ys[i], w, h);
            }
        }

        /// <summary>
        /// À appeler dans OnGUI avant GUI.Window. Impose le rect animé calculé sans chevauchement.
        /// </summary>
        public static bool ApplyDockedRect(ref Rect rect, int windowId)
        {
            if (!_docked.TryGetValue(windowId, out DockState d)) return false;
            if (!ProviderVisible(windowId))
            {
                _docked.Remove(windowId);
                RecalculateDockTargets();
                return false;
            }

            float t = Ease01((Time.realtimeSinceStartup - d.animStart) / DockAnimDuration);
            if (d.restoring)
            {
                rect = LerpRect(d.animFrom, d.fullRect, t);
                if (t >= 1f)
                {
                    _docked.Remove(windowId);
                    RecalculateDockTargets();
                }
                return false;
            }

            if (d.targetDockRect.width <= 0f)
            {
                RecalculateDockTargets();
            }

            rect = LerpRect(d.animFrom, d.targetDockRect, t);
            return true;
        }

        /// <summary>
        /// Vrai si le point touche une fenêtre repliée (rect affiché réel, suit l'animation).
        /// </summary>
        private static bool HitsDockedStrip(Vector2 mp)
        {
            if (_docked.Count == 0) return false;
            foreach (var kvp in _docked)
            {
                if (kvp.Value.restoring) continue;
                if (!ProviderVisible(kvp.Key)) continue;
                if (ProviderRect(kvp.Key, out Rect live) && live.Contains(mp)) return true;
            }
            return false;
        }

        /// <summary>
        /// Clic gauche sur une fenêtre repliée → restaure tout, clic consommé.
        /// </summary>
        private static bool RestoreClickConsumed(Vector2 mp)
        {
            if (!HitsDockedStrip(mp)) return false;
            RestoreAll();
            return true;
        }

        private static GUIStyle TitleStyle()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    richText = false,
                    wordWrap = false,
                    clipping = TextClipping.Clip
                };
                _titleStyle.normal.textColor = Color.white;
            }
            return _titleStyle;
        }

        private static string TruncateTitle(string text, float availWidth)
        {
            var style = TitleStyle();
            if (availWidth <= 0f) return "";
            if (style.CalcSize(new GUIContent(text)).x <= availWidth) return text;
            const string ellipsis = "…";
            int len = text.Length;
            while (len > 1 && style.CalcSize(new GUIContent(text.Substring(0, len) + ellipsis)).x > availWidth) len--;
            return len <= 1 ? ellipsis : text.Substring(0, len) + ellipsis;
        }

        /// <summary>
        /// Sécurise l'état GUI après chaque fenêtre (appelé en finally) : le focus clavier
        /// est lâché quand des fenêtres sont repliées (contenu non interactif).
        /// Sans effet dans tous les autres cas.
        /// </summary>
        public static void SanitizeGuiState()
        {
            if (_docked.Count > 0 && GUIUtility.keyboardControl != 0) GUIUtility.keyboardControl = 0;
        }

        private static Texture2D BgTex()
        {
            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _bgTex.SetPixel(0, 0, Color.white);
                _bgTex.Apply();
            }
            return _bgTex;
        }

        private static void TouchZ(int windowId)
        {
            _zOrder.Remove(windowId);
            _zOrder.Add(windowId);
        }

        /// <summary>
        /// Vrai si windowId est la fenêtre la plus haute sous le curseur (parmi les
        /// fenêtres enregistrées visibles). Seule elle réagit au clic : pas de vol de focus.
        /// </summary>
        private static bool IsTopmostAt(int windowId, Vector2 mp, ref Rect myRect)
        {
            if (myRect.width <= 0f || myRect.height <= 0f || !myRect.Contains(mp)) return false;
            int myIdx = _zOrder.IndexOf(windowId);
            for (int i = _zOrder.Count - 1; i > myIdx; i--)
            {
                int id = _zOrder[i];
                if (!_visibilityProviders.TryGetValue(id, out var vis) || vis == null) continue;
                bool visible;
                try { visible = vis(); } catch { continue; }
                if (!visible) continue;
                if (!_rectProviders.TryGetValue(id, out var getRect) || getRect == null) continue;
                Rect r;
                try { r = getRect(); } catch { continue; }
                if (r.width > 0f && r.height > 0f && r.Contains(mp)) return false; // couvert par une fenêtre devant
            }
            return true;
        }

        /// <summary>
        /// Dessine la barre de titre : fond opaque, zone de drag + boutons Minimiser / Fermer.
        /// Doit être le premier appel de DrawWindowContent.
        /// (Le premier plan est géré en amont par HandleResizeEvents, pas ici.)
        /// </summary>
        public static void DrawTitleBar(ref Rect rect, ref bool isOpen, ref bool isMinimized,
            ref Vector2 savedSize, int windowId, Action onClose = null, string title = "")
        {
            // Fond : suit l'opacité entrante (fantôme quand replié), jamais forcé opaque.
            Color prevFill = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.11f, 0.97f * prevFill.a);
            GUI.DrawTexture(new Rect(0, 0, rect.width, rect.height), BgTex());
            GUI.color = prevFill;

            float dragW = Mathf.Max(20f, rect.width - (BtnW * 2f + Pad * 3f));
            GUI.DragWindow(new Rect(0, 0, dragW, TitleHeight));

            // Titre dessiné par nous (garanti visible) : le titre natif de GUI.Window reste vide.
            // Lisible même en fantôme : alpha propre, pas celle du fondu global.
            if (!string.IsNullOrEmpty(title))
            {
                Rect titleRect = new Rect(8f, 2f, Mathf.Max(10f, dragW - 10f), 20f);
                Color prevTitleCol = GUI.color;
                GUI.color = new Color(0.92f, 0.96f, 1f, IsDocked(windowId) ? 0.85f : 1f);
                GUI.Label(titleRect, TruncateTitle(title, titleRect.width), TitleStyle());
                GUI.color = prevTitleCol;
            }

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
            Event e = Event.current;
            if (_pendingFront.Remove(windowId))
            {
                TouchZ(windowId);
                if (e != null) GUI.BringWindowToFront(windowId);
            }
            if (e == null) return;

            // Clic gauche sur une fenêtre repliée → restaure toutes les fenêtres, clic consommé.
            // Avant tout le reste : ce clic ne doit ni focuser ni redimensionner ni agir en 3D.
            if (e.type == EventType.MouseDown && e.button == 0 && RestoreClickConsumed(e.mousePosition))
            {
                e.Use();
                return;
            }

            // Fenêtre repliée : contenu non utilisable / non cliquable.
            // Consomme les autres événements souris sur le strip (sans restaurer) pour éviter
            // tout click-through : ni activation d'un contrôle pendant la restauration animée
            // (le MouseUp suit le MouseDown de restauration), ni scroll fantôme, ni zoom/orbit
            // caméra derrière, ni clic 3D. La barre de titre reste utilisable (gérée intra-Window).
            if ((e.type == EventType.MouseUp || e.type == EventType.MouseDrag
                    || e.type == EventType.ContextClick || e.type == EventType.ScrollWheel)
                && HitsDockedStrip(e.mousePosition))
            {
                e.Use();
                return;
            }

            bool resizing = _resizing.TryGetValue(windowId, out bool r) && r;

            // Filet de sécurité : MouseUp perdu (relâché hors de la vue) → ne pas rester collé.
            if (resizing && e.type != EventType.MouseDrag && e.type != EventType.MouseDown && !Input.GetMouseButton(0))
            {
                _resizing[windowId] = false;
                return;
            }

            Vector2 mp = e.mousePosition;

            // Clic n'importe où dans la fenêtre → premier plan si on est déjà la plus haute dessous.
            // (Sans Use() : titre, boutons et drag reçoivent l'event normalement.)
            if (e.type == EventType.MouseDown && !resizing && IsTopmostAt(windowId, mp, ref rect))
            {
                TouchZ(windowId);
                GUI.BringWindowToFront(windowId);
            }

            if (isMinimized || _docked.ContainsKey(windowId)) return;
            if (rect.width < GripSize * 2f || rect.height < GripSize * 2f) return;

            Rect gripScreen = new Rect(rect.x + rect.width - GripSize, rect.y + rect.height - GripSize, GripSize, GripSize);

            if (e.type == EventType.MouseDown && e.button == 0 && !resizing
                && gripScreen.Contains(mp) && IsTopmostAt(windowId, mp, ref rect))
            {
                _resizing[windowId] = true;
                _grabOffset[windowId] = new Vector2((rect.x + rect.width) - mp.x, (rect.y + rect.height) - mp.y);
                TouchZ(windowId);
                GUI.BringWindowToFront(windowId);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && resizing)
            {
                Vector2 off = _grabOffset.TryGetValue(windowId, out Vector2 o) ? o : Vector2.zero;
                float maxW = Screen.width > 0 ? Screen.width : 4096f;
                float maxH = Screen.height > 0 ? Screen.height : 4096f;
                rect.width = Mathf.Clamp(mp.x + off.x - rect.x, minSize.x, maxW);
                rect.height = Mathf.Clamp(mp.y + off.y - rect.y, minSize.y, maxH);
                rect.x = Mathf.Clamp(rect.x, -rect.width + 100f, Screen.width - 100f);
                rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height - 30f));
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
        public static void DrawResizeHandle(ref Rect rect, int windowId, Vector2 minSize, bool isMinimized = false)
        {
            if (isMinimized || _docked.ContainsKey(windowId)) return;
            if (rect.width < GripSize * 2f || rect.height < GripSize * 2f) return;

            Rect gripRect = new Rect(rect.width - GripSize, rect.height - GripSize, GripSize, GripSize);
            bool resizing = _resizing.TryGetValue(windowId, out bool r) && r;
            Color prev = GUI.backgroundColor;
            if (resizing) GUI.backgroundColor = new Color(0.2f, 0.9f, 1f, 1f);
            GUI.Box(gripRect, "◢");
            GUI.backgroundColor = prev;
        }

        /// <summary>
        /// Maintient la fenêtre visible à l'écran après GUI.Window.
        /// </summary>
        public static void ClampToScreen(ref Rect r)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            r.x = Mathf.Clamp(r.x, -r.width + 100f, Screen.width - 100f);
            r.y = Mathf.Clamp(r.y, 0f, Mathf.Max(0f, Screen.height - 30f));
            r.width = Mathf.Clamp(r.width, 200f, Screen.width);
            r.height = Mathf.Clamp(r.height, CollapsedHeight, Screen.height);
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

            float w = Mathf.Clamp(desired.width, 200f, Screen.width - 20f);
            float h = Mathf.Clamp(desired.height, CollapsedHeight, Screen.height - 20f);
            float minY = 96f; // sous carte joueur (zone d'exclusion 16->88) + 8px de marge
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

        public static bool IsResizing(int windowId)
        {
            return _resizing.TryGetValue(windowId, out bool v) && v;
        }

        public static bool IsAnyResizing()
        {
            foreach (var kvp in _resizing)
            {
                if (kvp.Value) return true;
            }
            return false;
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
