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

        /// <summary>
        /// Vrai tant que la fenêtre n'a pas retrouvé sa taille normale complète.
        /// </summary>
        public static bool IsDocked(int windowId)
            => _docked.ContainsKey(windowId);

        /// <summary>
        /// Restaure un dock déjà mémorisé. Le rect fourni reste le rect normal de
        /// la fenêtre ; la vignette dockée est recalculée pour la résolution courante.
        /// </summary>
        public static void RestoreDockedWindow(int windowId, Rect fullRect)
        {
            if (fullRect.width <= 0f || fullRect.height <= 0f) return;

            _docked[windowId] = new DockState
            {
                fullRect = fullRect,
                animFrom = fullRect,
                // État stable immédiatement : pas d'animation de repli au lancement.
                animStart = Time.realtimeSinceStartup - DockAnimDuration,
                restoring = false
            };
            RecalculateDockTargets();
        }

        /// <summary>Supprime un dock sans modifier le rect normal mémorisé.</summary>
        public static void ClearDockedWindow(int windowId)
        {
            if (_docked.Remove(windowId)) RecalculateDockTargets();
        }

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
        /// Échelle uniforme issue de la largeur : un dock représente une réduction
        /// proportionnelle de la fenêtre normale, sans déformation ni recadrage.
        /// </summary>
        public static float DockContentScale(int windowId, Rect dockedRect)
        {
            if (!_docked.TryGetValue(windowId, out var d)) return 1f;

            float sourceWidth = Mathf.Max(1f, d.fullRect.width);
            float targetWidth = Mathf.Max(1f, dockedRect.width);
            return Mathf.Clamp01(targetWidth / sourceWidth);
        }

        /// <summary>Rect logique complet à dessiner pour un aperçu docké mis à l'échelle.</summary>
        public static bool TryGetDockSourceRect(int windowId, out Rect sourceRect)
        {
            sourceRect = default;
            if (!_docked.TryGetValue(windowId, out var d)) return false;
            if (d.fullRect.width <= 0f || d.fullRect.height <= 0f) return false;
            sourceRect = d.fullRect;
            return true;
        }

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
                // Un aperçu docké est déjà à son état cible : le relancer à chaque
                // clic carte le ferait repartir de son rect courant et scintiller.
                // Même pendant son animation de restauration, on le laisse finir
                // naturellement plutôt que de l'inverser au clic suivant.
                if (_docked.ContainsKey(id)) continue;
                if (!ProviderRect(id, out Rect cur)) continue;
                if (cur.height <= CollapsedHeight + 1f) continue;

                _docked[id] = new DockState
                {
                    fullRect = cur,
                    animFrom = cur,
                    animStart = now,
                    restoring = false
                };
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

        /// <summary>Restaure une seule fenêtre dockée, sans toucher aux autres.</summary>
        public static bool RestoreDockedWindow(int windowId)
        {
            if (!_docked.TryGetValue(windowId, out DockState state) || state.restoring) return false;
            if (ProviderRect(windowId, out Rect current)) state.animFrom = current;
            state.restoring = true;
            state.animStart = Time.realtimeSinceStartup;
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

            // Écran étroit : les deux colonnes se recouvriraient horizontalement
            // (une colonne standard fait 160px + 36px de gouttière) -> une seule pile.
            if (leftList.Count > 0 && rightList.Count > 0 && Screen.width > 0f
                && Screen.width < (DockColumnEdgeInset + 160f) * 2f)
            {
                leftList.AddRange(rightList);
                rightList.Clear();
            }

            LayoutDockColumn(leftList, isRight: false);
            LayoutDockColumn(rightList, isRight: true);
            // Colonne gauche et droite peuvent se recouvrir (écran étroit : les deux
            // premières vignettes atterrissent à la même hauteur et se chevauchent
            // côte à côte). Passe globale : aucune paire de vignettes ne se recouvre.
            ResolveAllDockOverlaps(leftList, rightList);
        }

        /// <summary>
        /// Passe globale anti-chevauchement sur TOUTES les vignettes (gauche + droite).
        /// Traite de haut en bas : chaque dock qui en recouvre un déjà placé descend
        /// sous lui. Couvre les collisions inter-colonnes (écran étroit) et tout résidu
        /// intra-colonne (ex : clamp écran après poussée anti-bloqueur).
        /// </summary>
        private static void ResolveAllDockOverlaps(List<DockEntry> leftList, List<DockEntry> rightList)
        {
            int total = leftList.Count + rightList.Count;
            if (total < 2) return;
            var all = new List<DockEntry>(total);
            all.AddRange(leftList);
            all.AddRange(rightList);
            all.Sort((a, b) => a.state.targetDockRect.y.CompareTo(b.state.targetDockRect.y));
            for (int i = 0; i < all.Count; i++)
            {
                Rect r = all[i].state.targetDockRect;
                if (r.width <= 0f || r.height <= 0f) continue;
                int guard = 0;
                bool moved;
                do
                {
                    moved = false;
                    for (int j = 0; j < i; j++)
                    {
                        Rect p = all[j].state.targetDockRect;
                        if (p.width <= 0f || p.height <= 0f) continue;
                        if (r.Overlaps(p))
                        {
                            r.y = p.yMax + DockMargin;
                            moved = true;
                        }
                    }
                } while (moved && guard++ < 32);
                float maxY = Mathf.Max(0f, Screen.height - 12f - r.height);
                if (r.y > maxY) r.y = maxY;
                all[i].state.targetDockRect = r;
            }
        }

        private static void LayoutDockColumn(List<DockEntry> list, bool isRight)
        {
            int count = list.Count;
            if (count == 0) return;

            float w = Mathf.Clamp(Screen.width * 0.13f, 160f, 210f);
            float colX = isRight
                ? Mathf.Max(DockMargin, Screen.width - DockColumnEdgeInset - w)
                : DockColumnEdgeInset;
            Rect colRect = new Rect(colX, 0f, w, Screen.height);

            float topSafeY = isRight ? 90f : 94f;
            float bottomSafeY = Screen.height - 60f;

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

            list.Sort((a, b) => a.state.fullRect.y.CompareTo(b.state.fullRect.y));

            // Une carte dockée garde le ratio de sa fenêtre d'origine. Si la pile ne
            // tient pas, c'est la largeur commune de la colonne qui diminue : contenu,
            // largeur et hauteur sont alors tous réduits dans les mêmes proportions.
            float availableHeight = Mathf.Max(44f, bottomSafeY - topSafeY);
            float totalContentAspect = 0f;
            for (int i = 0; i < count; i++)
            {
                Rect source = list[i].state.fullRect;
                totalContentAspect += Mathf.Max(0.05f,
                    Mathf.Max(1f, source.height - TitleHeight) / Mathf.Max(1f, source.width));
            }

            float widthToFitStack = (availableHeight - count * TitleHeight - (count - 1) * DockMargin)
                / Mathf.Max(0.05f, totalContentAspect);
            w = Mathf.Clamp(Mathf.Min(w, widthToFitStack), 48f, w);
            colX = isRight
                ? Mathf.Max(DockMargin, Screen.width - DockColumnEdgeInset - w)
                : DockColumnEdgeInset;

            float[] heights = new float[count];
            float[] ys = new float[count];
            for (int i = 0; i < count; i++)
            {
                Rect source = list[i].state.fullRect;
                float contentAspect = Mathf.Max(0.05f,
                    Mathf.Max(1f, source.height - TitleHeight) / Mathf.Max(1f, source.width));
                heights[i] = TitleHeight + contentAspect * w;
                ys[i] = i == 0 ? topSafeY : ys[i - 1] + heights[i - 1] + DockMargin;
            }

            // Les vignettes dockées ne doivent jamais recouvrir l'élément derrière
            // (fenêtre normale/minimisée restée visible au bord, panneau HUD latéral...).
            // Chaque dock qui chevauche un bloqueur descend sous celui-ci ; l'ordre
            // haut->bas de la pile est préservé.
            for (int i = 0; i < count; i++)
            {
                Rect r = new Rect(colX, ys[i], w, heights[i]);
                int guard = 0;
                bool moved;
                do
                {
                    moved = false;
                    foreach (var kvp in _rectProviders)
                    {
                        int id = kvp.Key;
                        if (id == NoDockWindowId) continue;
                        if (_docked.ContainsKey(id)) continue; // autres docks : gérés par la pile elle-même
                        Rect b;
                        try
                        {
                            if (_visibilityProviders.TryGetValue(id, out var vis) && vis != null && !vis()) continue;
                            if (kvp.Value == null) continue;
                            b = kvp.Value();
                        }
                        catch { continue; }
                        if (b.width <= 0f || b.height <= 0f) continue;
                        if (b.xMax <= r.xMin || b.xMin >= r.xMax) continue; // hors colonne : ignore
                        if (r.Overlaps(b))
                        {
                            r.y = b.yMax + DockMargin;
                            moved = true;
                        }
                    }
                    for (int j = 0; j < i; j++)
                    {
                        Rect prev = new Rect(colX, ys[j], w, heights[j]);
                        if (r.Overlaps(prev))
                        {
                            r.y = prev.yMax + DockMargin;
                            moved = true;
                        }
                    }
                } while (moved && guard++ < 24);
                // Reste à l'écran : au pire on remonte (colonne saturée, cas pathologique
                // où un chevauchement résiduel est préférable à une vignette inaccessible).
                float maxY = Mathf.Max(topSafeY, Screen.height - 12f - r.height);
                if (r.y > maxY) r.y = maxY;
                ys[i] = r.y;
            }

            for (int i = 0; i < count; i++)
            {
                list[i].state.targetDockRect = new Rect(colX, ys[i], w, heights[i]);
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
                    rect = d.fullRect;
                    _docked.Remove(windowId);
                    RecalculateDockTargets();
                    FocusWindow(windowId);
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

        private static Rect DockRestoreAllButtonRect(Rect dockRect)
        {
            bool isLeft = dockRect.center.x < Screen.width * 0.5f;
            float x = isLeft
                ? Mathf.Max(2f, dockRect.xMin - DockRestoreAllButtonGap - DockRestoreAllButtonWidth)
                : Mathf.Min(Screen.width - DockRestoreAllButtonWidth - 2f,
                    dockRect.xMax + DockRestoreAllButtonGap);
            float y = Mathf.Clamp(dockRect.center.y - DockRestoreAllButtonHeight * 0.5f, 4f,
                Mathf.Max(4f, Screen.height - DockRestoreAllButtonHeight - 4f));
            return new Rect(x, y, DockRestoreAllButtonWidth, DockRestoreAllButtonHeight);
        }

        /// <summary>
        /// Retrouve le dock survolé. La zone de sa commande latérale reste active
        /// afin que le bouton ne disparaisse pas pendant le déplacement de souris.
        /// </summary>
        private static bool TryGetHoveredDock(Vector2 mp, out int windowId, out Rect dockRect)
        {
            windowId = 0;
            dockRect = default;
            if (_docked.Count == 0) return false;
            foreach (var kvp in _docked)
            {
                if (kvp.Value.restoring) continue;
                if (!ProviderVisible(kvp.Key)) continue;
                if (!ProviderRect(kvp.Key, out Rect live)) continue;
                if (live.Contains(mp) || DockRestoreAllButtonRect(live).Contains(mp))
                {
                    windowId = kvp.Key;
                    dockRect = live;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gère les clics autour d'un aperçu docké avant le rendu des GUI.Window.
        /// Le bouton latéral est testé avant l'aperçu lui-même, ce qui garantit le
        /// même comportement aux deux bords de l'écran, indépendamment de l'ordre
        /// de rendu IMGUI des fenêtres.
        /// </summary>
        private static bool ConsumeDockedClick(Vector2 mp)
        {
            if (!TryGetHoveredDock(mp, out int hoveredId, out Rect dockRect)) return false;

            if (DockRestoreAllButtonRect(dockRect).Contains(mp))
            {
                RestoreAll();
                return true;
            }

            if (dockRect.Contains(mp))
            {
                RestoreDockedWindow(hoveredId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Dessine le bouton latéral de restauration globale pour la fenêtre dockée
        /// actuellement survolée. Chaque fenêtre appelle cette méthode, mais seule
        /// celle qui est survolée effectue le rendu.
        /// </summary>
        public static void DrawDockRestoreAllButton(int windowId)
        {
            Event e = Event.current;
            if (e == null || !TryGetHoveredDock(e.mousePosition, out int hoveredId, out Rect dockRect)
                || hoveredId != windowId)
                return;

            Rect buttonRect = DockRestoreAllButtonRect(dockRect);
            // L'action est traitée en pré-fenêtre par ConsumeDockedClick : cette
            // boîte conserve le visuel de bouton sans dépendre du hot-control IMGUI.
            GUI.Box(buttonRect, new GUIContent("↔", "Restaurer toutes les fenêtres dockées"), GUI.skin.button);
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
                Color previousTitleColor = GUI.color;
                GUI.color = new Color(0.92f, 0.96f, 1f, 0.75f);
                GUI.Label(titleRect, TruncateTitle(title, titleRect.width), TitleStyle());
                GUI.color = previousTitleColor;
            }

            Rect minR = new Rect(rect.x + rect.width - (BtnW * 2f + Pad * 2f), rect.y + Pad, BtnW, BtnH);
            Rect closeR = new Rect(rect.x + rect.width - (BtnW + Pad), rect.y + Pad, BtnW, BtnH);

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

            Color previousBackground = GUI.backgroundColor;
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
            GUI.backgroundColor = previousBackground;
        }

        private static float _lastDockAvoidRecalc;

        /// <summary>
        /// Teste si une cible de dock recouvre une fenêtre visible non dockée.
        /// Rects seuls, sans allocation : appelé chaque frame via HandleResizeEvents.
        /// </summary>
        private static bool DockTargetOverlapsBlocker(Rect target, int selfId)
        {
            if (target.width <= 0f || target.height <= 0f) return false;
            foreach (var kvp in _rectProviders)
            {
                int id = kvp.Key;
                if (id == selfId || id == NoDockWindowId) continue;
                if (_docked.ContainsKey(id)) continue;
                Rect b;
                try
                {
                    if (_visibilityProviders.TryGetValue(id, out var vis) && vis != null && !vis()) continue;
                    if (kvp.Value == null) continue;
                    b = kvp.Value();
                }
                catch { continue; }
                if (b.width <= 0f || b.height <= 0f) continue;
                if (target.Overlaps(b)) return true;
            }
            return false;
        }

        /// <summary>
        /// Garde-fou temps réel : si une fenêtre a été déplacée ou ouverte sous une
        /// vignette dockée existante, la pile se décale pour libérer l'élément derrière.
        /// Recalcul throttlé (la pile converge en un passage, pas de ping-pong).
        /// </summary>
        private static void RefreshDockLayoutIfOverlapping()
        {
            if (_docked.Count == 0) return;
            if (Time.realtimeSinceStartup - _lastDockAvoidRecalc < 0.25f) return;
            foreach (var kvp in _docked)
            {
                if (kvp.Value.restoring) continue;
                if (kvp.Value.targetDockRect.width <= 0f) continue;
                if (DockTargetOverlapsBlocker(kvp.Value.targetDockRect, kvp.Key))
                {
                    _lastDockAvoidRecalc = Time.realtimeSinceStartup;
                    RecalculateDockTargets();
                    return;
                }
            }
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
            // Les docks ne recouvrent jamais l'élément derrière : si une fenêtre est
            // passée sous une vignette existante, la pile se décale (throttlé).
            RefreshDockLayoutIfOverlapping();
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
            // latéral pour éviter tout click-through vers la carte.
            if ((e.type == EventType.MouseUp || e.type == EventType.MouseDrag
                    || e.type == EventType.ContextClick || e.type == EventType.ScrollWheel)
                && TryGetHoveredDock(e.mousePosition, out _, out _))
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
