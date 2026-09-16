using UnityEngine;

namespace Killtime.UI
{
    /// <summary>
    /// Socle commun à toutes les fenêtres flottantes IMGUI de Killtime Tactics.
    /// Centralise style ET comportement pour qu'ils restent identiques partout :
    /// drag par barre de titre, boutons minimiser / fermer, resize par poignée,
    /// repli animé sur les côtés au clic map, premier plan au clic / à l'ouverture,
    /// blocage des interactions 3D au survol (via FloatingWindowChrome), toggle clavier.
    ///
    /// Usage : public class MaFenetre : FloatingWindow&lt;MaFenetre&gt;
    ///   - fournir WindowId, Title, MinSize, DefaultRect (+ ToggleKeys si raccourci),
    ///   - dessiner le contenu dans DrawContent() (pas de OnGUI, pas de chrome manuel),
    ///   - hooks : OnAwake / OnOpened / OnClosed, OnUpdate pour le per-frame custom.
    /// IMPORTANT : toute redéfinition de Awake/OnEnable/OnDisable/OnDestroy/Update
    /// DOIT appeler base.X(), sinon enregistrement chrome / toggles / singleton cassés.
    /// </summary>
    public abstract class FloatingWindow<T> : MonoBehaviour where T : FloatingWindow<T>
    {
        public static T Instance { get; protected set; }

        protected abstract int WindowId { get; }
        protected abstract string Title { get; }
        protected virtual Vector2 MinSize => new Vector2(360f, 220f);
        protected virtual Rect DefaultRect => new Rect(40f, 40f, 620f, 680f);
        protected virtual KeyCode[] ToggleKeys => null;
        protected virtual bool CanDraw => true;

        [SerializeField] protected bool _isOpen;
        protected bool _isMinimized;
        protected Vector2 _savedSize = Vector2.zero;
        protected Rect _windowRect;

        // Rect logique complet employé pendant le rendu d'un aperçu docké et pour
        // persister la taille normale, jamais la vignette réduite affichée.
        private Rect _dockRenderSourceRect;
        private bool _drawingDockedPreview;
        private float _dockContentScale = 1f;

        private Rect _lastSavedRect;
        private bool _lastSavedOpen;
        private bool _lastSavedMinimized;
        private bool _lastSavedDocked;
        private bool _layoutTrackingInit;
        private bool _restoreDockedAfterLoad;

        /// <summary>Crée la fenêtre si absente de la scène, puis l'ouvre au premier plan.</summary>
        public static void Open()
        {
            if (Instance == null)
            {
                Instance = FindAnyObjectByType<T>();
                if (Instance == null)
                    Instance = new GameObject("[UI] " + typeof(T).Name).AddComponent<T>();
            }
            Instance.OpenInstance();
        }

        public void OpenInstance()
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (!enabled) enabled = true;
            _isOpen = true;
            _isMinimized = false;
            FloatingWindowChrome.ClearDockedWindow(WindowId);
            _restoreDockedAfterLoad = false;
            // Anti-chevauchement : décale la fenêtre si son rect recouvre le HUD ou une autre fenêtre.
            try { _windowRect = FloatingWindowChrome.FindNonOverlappingRect(_windowRect, WindowId); }
            catch { /* ignore : l'ouverture ne doit jamais échouer */ }
            FloatingWindowChrome.FocusWindow(WindowId);
            OnOpened();
            try
            {
                Rect persistentRect = GetPersistentRect(out bool isDocked);
                DevUIPreferences.SetWindow(WindowId, persistentRect, _isOpen, _isMinimized, isDocked);
                _lastSavedRect = persistentRect;
                _lastSavedOpen = _isOpen;
                _lastSavedMinimized = _isMinimized;
                _lastSavedDocked = isDocked;
            }
            catch { /* prefs optionnelles */ }
        }

        public virtual void CloseWindow()
        {
            Rect persistentRect = GetPersistentRect(out _);
            FloatingWindowChrome.ClearDockedWindow(WindowId);
            _restoreDockedAfterLoad = false;
            _isOpen = false;
            _isMinimized = false;
            _savedSize = Vector2.zero;
            OnClosed();
            try
            {
                DevUIPreferences.SetWindow(WindowId, persistentRect, _isOpen, _isMinimized, false);
                _lastSavedRect = persistentRect;
                _lastSavedOpen = _isOpen;
                _lastSavedMinimized = _isMinimized;
                _lastSavedDocked = false;
            }
            catch { /* ignore */ }
        }

        protected virtual void OnAwake() { }
        protected virtual void OnOpened() { }
        protected virtual void OnClosed() { }
        protected virtual void OnUpdate() { }
        protected virtual void DrawClosedState() { }
        protected abstract void DrawContent();

        private void ApplySavedLayout()
        {
            _layoutTrackingInit = false;
            try
            {
                var entry = DevUIPreferences.GetWindow(WindowId);
                if (entry != null && entry.W > 10f && entry.H > 10f)
                {
                    _windowRect = new Rect(entry.X, entry.Y, entry.W, entry.H);
                    _isOpen = entry.IsOpen;
                    _isMinimized = entry.IsMinimized;
                    _restoreDockedAfterLoad = entry.IsDocked && _isOpen && !_isMinimized;
                    if (_isMinimized)
                    {
                        _savedSize = new Vector2(entry.W, entry.H);
                    }
                }
            }
            catch { /* garde le défaut */ }
            finally
            {
                _lastSavedRect = _windowRect;
                _lastSavedOpen = _isOpen;
                _lastSavedMinimized = _isMinimized;
                _lastSavedDocked = false;
                _layoutTrackingInit = true;
            }
        }

        /// <summary>
        /// La vignette dockée est un état de rendu transitoire. Les préférences
        /// doivent toujours retenir le rect complet qui servira au redémarrage et à
        /// la restauration de la fenêtre.
        /// </summary>
        private Rect GetPersistentRect(out bool isDocked)
        {
            isDocked = FloatingWindowChrome.TryGetDockSourceRect(WindowId, out Rect fullRect);
            return isDocked ? fullRect : _windowRect;
        }

        private void TrackLayoutChanges()
        {
            if (!_layoutTrackingInit) return;
            try
            {
                Rect persistentRect = GetPersistentRect(out bool isDocked);
                bool rectChanged =
                    !Mathf.Approximately(_lastSavedRect.x, persistentRect.x) ||
                    !Mathf.Approximately(_lastSavedRect.y, persistentRect.y) ||
                    !Mathf.Approximately(_lastSavedRect.width, persistentRect.width) ||
                    !Mathf.Approximately(_lastSavedRect.height, persistentRect.height);
                bool stateChanged = (_lastSavedOpen != _isOpen) || (_lastSavedMinimized != _isMinimized)
                    || (_lastSavedDocked != isDocked);
                if (rectChanged || stateChanged)
                {
                    DevUIPreferences.SetWindow(WindowId, persistentRect, _isOpen, _isMinimized, isDocked);
                    _lastSavedRect = persistentRect;
                    _lastSavedOpen = _isOpen;
                    _lastSavedMinimized = _isMinimized;
                    _lastSavedDocked = isDocked;
                }
            }
            catch { /* prefs optionnelles */ }
        }

        protected virtual void Awake()
        {
            if (Instance == null) Instance = (T)this;
            if (Instance != this)
            {
                // Doublon (ex : instance de scène + création runtime) : deux fenêtres avec
                // le même id corrompraient mutuellement leur rendu. Un seul pilote.
                Destroy(gameObject);
                return;
            }
            _windowRect = DefaultRect;
            try { ApplySavedLayout(); } catch { /* prefs optionnelles : garde le défaut */ }
            FloatingWindowChrome.RegisterWindow(WindowId, () => _windowRect, () => isActiveAndEnabled && _isOpen);
            OnAwake();
        }

        protected virtual void OnEnable()
        {
            FloatingWindowChrome.RegisterWindow(WindowId, () => _windowRect, () => isActiveAndEnabled && _isOpen);
        }

        protected virtual void OnDisable()
        {
            // Ne désenregistre que si on est le pilote : un doublon en cours de
            // destruction ne doit pas arracher l'enregistrement du vrai propriétaire.
            if (Instance == this) FloatingWindowChrome.UnregisterWindow(WindowId);
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this)
            {
                FloatingWindowChrome.UnregisterWindow(WindowId);
                Instance = null;
            }
        }

        protected virtual void Update()
        {
            KeyCode[] keys = ToggleKeys;
            if (keys != null)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    if (Input.GetKeyDown(keys[i]))
                    {
                        if (_isOpen) CloseWindow();
                        else OpenInstance();
                        break;
                    }
                }
            }
            OnUpdate();
        }

        private void OnGUI()
        {
            if (!_isOpen)
            {
                DrawClosedState();
                return;
            }
            if (!CanDraw) return;

            if (_restoreDockedAfterLoad && Screen.width > 0 && Screen.height > 0)
            {
                FloatingWindowChrome.RestoreDockedWindow(WindowId, _windowRect);
                _restoreDockedAfterLoad = false;
            }

            if (_isMinimized)
            {
                _windowRect.height = FloatingWindowChrome.CollapsedHeight;
            }
            else
            {
                if (_savedSize.y > 30f && _windowRect.height <= FloatingWindowChrome.CollapsedHeight + 1f)
                {
                    _windowRect.height = _savedSize.y;
                    _windowRect.width = _savedSize.x;
                    _savedSize = Vector2.zero;
                }
                _windowRect.width = Mathf.Clamp(_windowRect.width, MinSize.x, Mathf.Max(MinSize.x, Screen.width - 20));
                _windowRect.height = Mathf.Clamp(_windowRect.height, MinSize.y, Mathf.Max(MinSize.y, Screen.height - 20));
            }

            FloatingWindowChrome.HandleResizeEvents(ref _windowRect, WindowId, MinSize, _isMinimized);
            FloatingWindowChrome.ApplyDockedRect(ref _windowRect, WindowId);
            Color prevDockCol = GUI.color;
            Color fadedDockCol = prevDockCol; fadedDockCol.a *= FloatingWindowChrome.DockAlpha(WindowId);
            GUI.color = fadedDockCol;
            Matrix4x4 previousMatrix = GUI.matrix;
            Rect guiWindowRect = _windowRect;
            _drawingDockedPreview = FloatingWindowChrome.IsDocked(WindowId)
                && FloatingWindowChrome.TryGetDockSourceRect(WindowId, out _dockRenderSourceRect);
            bool drawIndependentDockChrome = _drawingDockedPreview;
            if (_drawingDockedPreview)
            {
                _dockContentScale = FloatingWindowChrome.DockContentScale(WindowId, _windowRect);
                // Le contenu est réduit, mais le chrome (contour, titre et commandes)
                // reste à la taille du dock. Le clip logique doit donc couvrir la
                // hauteur du dock réel, qui inclut une barre de titre non réduite.
                guiWindowRect = _dockRenderSourceRect;
                guiWindowRect.height = Mathf.Max(guiWindowRect.height,
                    _windowRect.height / Mathf.Max(0.01f, _dockContentScale));
                FloatingWindowChrome.DrawDockedFrameBackground(_windowRect, WindowId);
                GUI.matrix = Matrix4x4.Translate(new Vector3(_windowRect.x, _windowRect.y, 0f))
                    * Matrix4x4.Scale(new Vector3(_dockContentScale, _dockContentScale, 1f))
                    * Matrix4x4.Translate(new Vector3(-_dockRenderSourceRect.x, -_dockRenderSourceRect.y, 0f));
            }
            try
            {
                Rect result = GUI.Window(WindowId, guiWindowRect, DrawWindowContentInternal, "", GUIStyle.none);
                if (!_drawingDockedPreview) _windowRect = result;
            }
            finally
            {
                GUI.matrix = previousMatrix;
                _drawingDockedPreview = false;
                _dockContentScale = 1f;
                GUI.color = prevDockCol;
                FloatingWindowChrome.SanitizeGuiState();
            }
            FloatingWindowChrome.ClampToScreen(ref _windowRect);
            if (drawIndependentDockChrome && _isOpen)
            {
                FloatingWindowChrome.DrawDockedFrameForeground(_windowRect, ref _isOpen, ref _isMinimized,
                    ref _savedSize, WindowId, CloseWindow, Title);
                FloatingWindowChrome.DrawDockRestoreAllButton(WindowId);
            }
            TrackLayoutChanges();
        }

        private void DrawWindowContentInternal(int windowId)
        {
            bool isDocked = FloatingWindowChrome.IsDocked(WindowId);
            // GUI.Window remet son propre repère dans le callback. Le chrome doit
            // donc être dessiné directement dans le rect docké réel, tandis que le
            // contenu est mis à l'échelle séparément plus bas.
            Rect drawingRect = _drawingDockedPreview
                ? new Rect(0f, 0f, _windowRect.width, _windowRect.height)
                : _windowRect;

            if (!_drawingDockedPreview)
            {
                FloatingWindowChrome.DrawTitleBar(ref drawingRect, ref _isOpen, ref _isMinimized,
                    ref _savedSize, WindowId, CloseWindow, Title);
            }
            if (_isMinimized) return;

            if (isDocked)
            {
                FloatingWindowChrome.ConsumeDockedContentEvent(drawingRect, WindowId);
                bool previousEnabled = GUI.enabled;
                GUI.enabled = false;
                try
                {
                    // GUI.Window ne propage pas systématiquement la matrice externe
                    // à ses contrôles enfants. Appliquer l'échelle ici évite que le
                    // contenu (tabs, scrollbars, boutons) déborde du cadre docké.
                    float logicalHeight = Mathf.Max(_dockRenderSourceRect.height,
                        _windowRect.height / Mathf.Max(0.01f, _dockContentScale));
                    DrawContentBelowTitle(_dockRenderSourceRect.width, logicalHeight, _dockContentScale);
                }
                finally
                {
                    GUI.enabled = previousEnabled;
                }
                FloatingWindowChrome.DrawDockVeil(ref drawingRect, WindowId);
                return;
            }

            DrawContentBelowTitle(drawingRect.width, drawingRect.height, reserveResizeCorner: true);

            FloatingWindowChrome.DrawResizeHandle(ref _windowRect, WindowId, MinSize, _isMinimized);
        }

        /// <summary>
        /// Réserve la barre de titre au-dessus du contenu. Sans ce groupe, les
        /// premiers contrôles GUILayout commencent à y = 0 et recouvrent les boutons
        /// réduire / fermer dès que le chrome est affiché à sa taille normale.
        /// </summary>
        private void DrawContentBelowTitle(float width, float height, float contentScale = 1f,
            bool reserveResizeCorner = false)
        {
            float contentTop = FloatingWindowChrome.TitleHeight;
            float resizeReserve = reserveResizeCorner ? FloatingWindowChrome.ResizeGripSize : 0f;
            GUI.BeginGroup(new Rect(0f, contentTop, Mathf.Max(0f, width),
                Mathf.Max(0f, height - contentTop - resizeReserve)));
            try
            {
                Matrix4x4 previousContentMatrix = GUI.matrix;
                try
                {
                    if (!Mathf.Approximately(contentScale, 1f))
                    {
                        GUI.matrix = GUI.matrix * Matrix4x4.Scale(
                            new Vector3(contentScale, contentScale, 1f));
                    }
                    DrawContent();
                }
                finally
                {
                    GUI.matrix = previousContentMatrix;
                }
            }
            finally
            {
                GUI.EndGroup();
            }
        }
    }
}
