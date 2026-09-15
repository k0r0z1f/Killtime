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
            // Anti-chevauchement : décale la fenêtre si son rect recouvre le HUD ou une autre fenêtre.
            try { _windowRect = FloatingWindowChrome.FindNonOverlappingRect(_windowRect, WindowId); }
            catch { /* ignore : l'ouverture ne doit jamais échouer */ }
            FloatingWindowChrome.FocusWindow(WindowId);
            OnOpened();
        }

        public virtual void CloseWindow()
        {
            _isOpen = false;
            _isMinimized = false;
            _savedSize = Vector2.zero;
            OnClosed();
        }

        protected virtual void OnAwake() { }
        protected virtual void OnOpened() { }
        protected virtual void OnClosed() { }
        protected virtual void OnUpdate() { }
        protected virtual void DrawClosedState() { }
        protected abstract void DrawContent();

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
            try
            {
                _windowRect = GUI.Window(WindowId, _windowRect, DrawWindowContentInternal, "");
            }
            finally
            {
                GUI.color = prevDockCol;
                FloatingWindowChrome.SanitizeGuiState();
            }
            FloatingWindowChrome.ClampToScreen(ref _windowRect);
        }

        private void DrawWindowContentInternal(int windowId)
        {
            bool isDocked = FloatingWindowChrome.IsDocked(WindowId);

            FloatingWindowChrome.DrawTitleBar(ref _windowRect, ref _isOpen, ref _isMinimized, ref _savedSize, WindowId, CloseWindow, Title);
            if (_isMinimized) return;

            if (isDocked)
            {
                // Contenu docké non utilisable / non cliquable : consomme l'event AVANT
                // que les contrôles enfants ne le voient (infranchissable même si un
                // contenu force GUI.enabled=true). Barre de titre déjà dessinée : intacte.
                FloatingWindowChrome.ConsumeDockedContentEvent(_windowRect);

                bool prevEnabled = GUI.enabled;
                GUI.enabled = false;

                try
                {
                    DrawContent();
                }
                finally
                {
                    GUI.enabled = prevEnabled;
                }

                // Voile visuel seul (la restauration au clic est gérée en pré-Window).
                FloatingWindowChrome.DrawDockVeil(ref _windowRect, WindowId);
                return;
            }

            DrawContent();

            FloatingWindowChrome.DrawResizeHandle(ref _windowRect, WindowId, MinSize, _isMinimized);
        }
    }
}
