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
    /// Noyau : constantes, champs partagés, registre, helpers partagés.
    /// Voir aussi : FloatingWindowChrome.DockLifecycle, .DockLayout, .DockRender, .Chrome.
    /// </summary>
    public static partial class FloatingWindowChrome
    {
        public const float TitleHeight = 25f;
        public const float CollapsedHeight = 26f;

        private const float BtnW = 26f;
        private const float BtnH = 18f;
        private const float Pad = 4f;
        public const float ResizeGripSize = 16f;

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
        private const float DockRestoreAllButtonWidth = 26f;
        private const float DockRestoreAllButtonHeight = 46f;
        private const float DockRestoreAllButtonGap = 5f;
        // Réserve une gouttière entre le bord de l'écran et les docks pour la
        // commande « restaurer tout » affichée au survol.
        private const float DockColumnEdgeInset = DockRestoreAllButtonWidth + DockRestoreAllButtonGap * 2f;
        private const float DockAnimDuration = 0.35f;
        // Les fenêtres dockées restent suffisamment lisibles pour servir d'aperçu,
        // tout en signalant clairement qu'elles sont en lecture seule.
        public const float DockedAlpha = 0.62f;
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
        /// Sécurise l'état GUI après chaque fenêtre (appelé en finally).
        /// Ne réinitialise jamais keyboardControl globalement pour préserver la saisie dans les champs texte.
        /// </summary>
        public static void SanitizeGuiState()
        {
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

        /// <summary>
        /// Dessine une ligne/zone d'accent sans laisser sa couleur contaminer le GUI suivant.
        /// </summary>
        private static void DrawAccentLine(Rect rect, Color color, float alpha)
        {
            if (rect.width <= 0f || rect.height <= 0f || alpha <= 0f) return;

            Color previousColor = GUI.color;
            color.a *= alpha;
            GUI.color = color;
            GUI.DrawTexture(rect, BgTex());
            GUI.color = previousColor;
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
    }
}
