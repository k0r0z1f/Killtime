using System;
using UnityEngine;
using Killtime.UI;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Menu contextuel d'un objet au sol (gourdin / arme lâchée après K.O.).
    /// Ouvert au clic droit sur l'objet (voir <see cref="TacticalSelectionManager.OnOpenDroppedWeaponMenuRequested"/>).
    /// Actions : ramasser (gratuit, à portée), shooter plus loin (1 PA en combat),
    /// détruire (définitif). Fermé sur Échap / clic extérieur / reset d'arène.
    /// </summary>
    public class DroppedWeaponContextMenuUI : MonoBehaviour
    {
        public static DroppedWeaponContextMenuUI Instance { get; private set; }

        private const int WindowId = 778;
        private const float PanelWidth = 264f;
        private const float PanelHeight = 218f;

        [Header("Systèmes")]
        [SerializeField] private TacticalSelectionManager _selectionManager;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CombatDevArena _arena;

        private bool _isOpen;
        private DroppedWeaponPickup _target;
        private Vector2 _centerPos;
        private Rect _panelRect = Rect.zero;
        private int _openFrame;
        private bool _blockUntilMouseUp;
        private int _blockUntilFrame;

        public bool IsOpen => _isOpen;
        public DroppedWeaponPickup Target => _target;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance == null && FindAnyObjectByType<DroppedWeaponContextMenuUI>() == null)
            {
                var go = new GameObject("[UI] DroppedWeaponContextMenu");
                go.AddComponent<DroppedWeaponContextMenuUI>();
            }
        }

        public static DroppedWeaponContextMenuUI EnsureInstance()
        {
            if (Instance != null) return Instance;
            var found = FindAnyObjectByType<DroppedWeaponContextMenuUI>();
            if (found != null) return found;
            var go = new GameObject("[UI] DroppedWeaponContextMenu");
            return go.AddComponent<DroppedWeaponContextMenuUI>();
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            EnsureReferences();
            if (_selectionManager != null)
            {
                _selectionManager.OnOpenDroppedWeaponMenuRequested -= OpenMenu;
                _selectionManager.OnOpenDroppedWeaponMenuRequested += OpenMenu;
                _selectionManager.OnCloseContextMenuRequested -= HandleExternalCloseRequest;
                _selectionManager.OnCloseContextMenuRequested += HandleExternalCloseRequest;
            }
            FloatingWindowChrome.RegisterWindow(WindowId, GetBoundingRect, IsBlockingInput);
            FloatingWindowChrome.RegisterExtraBlocker(IsPointerOverMenu);
        }

        private void OnEnable()
        {
            FloatingWindowChrome.RegisterExtraBlocker(IsPointerOverMenu);
        }

        private void OnDisable()
        {
            FloatingWindowChrome.UnregisterExtraBlocker(IsPointerOverMenu);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                FloatingWindowChrome.UnregisterWindow(WindowId);
                FloatingWindowChrome.UnregisterExtraBlocker(IsPointerOverMenu);
                Instance = null;
            }
            if (_selectionManager != null)
            {
                _selectionManager.OnOpenDroppedWeaponMenuRequested -= OpenMenu;
                _selectionManager.OnCloseContextMenuRequested -= HandleExternalCloseRequest;
            }
        }

        private void EnsureReferences()
        {
            if (_selectionManager == null) _selectionManager = FindAnyObjectByType<TacticalSelectionManager>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
        }

        private void HandleExternalCloseRequest()
        {
            CloseMenu();
        }

        private void Update()
        {
            if (!_isOpen) return;
            if (_target == null || _target.DroppedItem == null)
            {
                CloseMenu();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ArmInputBlocker();
                CloseMenu();
                return;
            }
            if (Time.frameCount > _openFrame && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
            {
                Vector2 mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                // Clic hors panneau => congédie (avec marge). Le clic sur l'objet lui-même
                // (re-clic droit) est géré par le SelectionManager qui rouvre le menu.
                if (_panelRect.width > 0f && !_panelRect.Contains(mouseGui))
                {
                    ArmInputBlocker();
                    CloseMenu();
                }
            }
        }

        public void OpenMenu(DroppedWeaponPickup pickup, Vector2 screenPos)
        {
            if (pickup == null || pickup.DroppedItem == null) return;
            EnsureReferences();
            // Le radial d'unité ne doit pas rester ouvert sous le menu objet.
            try { CombatContextMenuUI.Instance?.CloseMenu(); } catch { }
            _target = pickup;
            float m = 150f;
            float clampedX = Mathf.Clamp(screenPos.x, m, Mathf.Max(m, Screen.width - m));
            float clampedY = Mathf.Clamp(screenPos.y, m, Mathf.Max(m, Screen.height - m));
            _centerPos = new Vector2(clampedX, clampedY);
            _isOpen = true;
            _openFrame = Time.frameCount;
            _blockUntilMouseUp = false;
            try
            {
                if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                    Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Open, 0.5f);
            }
            catch { }
        }

        public void CloseMenu()
        {
            _isOpen = false;
            _target = null;
            _panelRect = Rect.zero;
        }

        /// <summary>Acteur joueur qui interagit : unité active > sélection > joueur arène.</summary>
        public TacticalUnit GetActor()
        {
            if (_turnManager != null && !_turnManager.IsInExploration && !_turnManager.IsCombatOver
                && _turnManager.ActiveUnit != null && _turnManager.ActiveUnit.IsPlayerControlled
                && _turnManager.ActiveUnit.Stats != null && _turnManager.ActiveUnit.Stats.IsAlive)
                return _turnManager.ActiveUnit;
            if (_selectionManager != null && _selectionManager.PrimarySelected != null
                && _selectionManager.PrimarySelected.IsPlayerControlled
                && _selectionManager.PrimarySelected.Stats != null && _selectionManager.PrimarySelected.Stats.IsAlive)
                return _selectionManager.PrimarySelected;
            if (_arena != null)
            {
                if (_arena.PlayerUnit != null && _arena.PlayerUnit.Stats != null && _arena.PlayerUnit.Stats.IsAlive)
                    return _arena.PlayerUnit;
                if (_arena.AdditionalPlayers != null)
                {
                    for (int i = 0; i < _arena.AdditionalPlayers.Count; i++)
                    {
                        var ally = _arena.AdditionalPlayers[i];
                        if (ally != null && ally.Stats != null && ally.Stats.IsAlive && ally.IsPlayerControlled)
                            return ally;
                    }
                }
            }
            if (_turnManager != null && _turnManager.ActiveUnit != null) return _turnManager.ActiveUnit;
            return null;
        }

        private void ArmInputBlocker()
        {
            _blockUntilMouseUp = true;
            _blockUntilFrame = Time.frameCount + 2;
        }

        private bool IsBlockingInput()
        {
            if (_isOpen) return true;
            if (_blockUntilMouseUp)
            {
                if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && Time.frameCount > _blockUntilFrame)
                {
                    _blockUntilMouseUp = false;
                    return false;
                }
                return true;
            }
            return false;
        }

        private bool IsPointerOverMenu(Vector2 mouseGui)
        {
            if (!_isOpen) return false;
            if (_panelRect.width > 0f && _panelRect.Contains(mouseGui)) return true;
            // Pendant l'ouverture, neutralise le clic 3D comme le radial d'unité.
            return IsBlockingInput();
        }

        private Rect GetBoundingRect()
        {
            if (_panelRect.width > 0f) return _panelRect;
            return new Rect(_centerPos.x - PanelWidth * 0.5f, _centerPos.y - PanelHeight * 0.5f, PanelWidth, PanelHeight);
        }

        private void OnGUI()
        {
            Event e = Event.current;
            Vector2 mousePos = e.mousePosition;

            if (!_isOpen)
            {
                if (_blockUntilMouseUp && e.type == EventType.MouseDown)
                {
                    float d = Vector2.Distance(mousePos, _centerPos);
                    if (d < 200f) e.Use();
                }
                return;
            }
            if (_target == null || _target.DroppedItem == null) return;

            var actor = GetActor();
            string itemName = _target.DroppedItem.Name ?? "Objet";
            string owner = string.IsNullOrEmpty(_target.FormerOwnerName) ? "—inconnu—" : _target.FormerOwnerName;
            float weight = _target.DroppedItem.WeightKg;
            float dist = actor != null
                ? Vector3.Distance(actor.transform.position, _target.transform.position)
                : -1f;

            float x = Mathf.Clamp(_centerPos.x - PanelWidth * 0.5f, 8f, Mathf.Max(8f, Screen.width - PanelWidth - 8f));
            float y = Mathf.Clamp(_centerPos.y - PanelHeight * 0.5f, 8f, Mathf.Max(8f, Screen.height - PanelHeight - 8f));
            _panelRect = new Rect(x, y, PanelWidth, PanelHeight);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.015f, 0.03f, 0.05f, 0.96f);
            GUI.Box(_panelRect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            Color prevCol = GUI.color;
            GUI.color = new Color(0f, 0.85f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(_panelRect.x, _panelRect.y, _panelRect.width, 2f), Texture2D.whiteTexture);
            GUI.color = prevCol;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false
            };
            titleStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true
            };
            bodyStyle.normal.textColor = new Color(0.85f, 0.92f, 1f, 0.92f);
            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true
            };
            hintStyle.normal.textColor = new Color(0.6f, 0.7f, 0.8f, 0.9f);

            float cx = _panelRect.x + 10f;
            float cw = _panelRect.width - 20f;
            GUI.Label(new Rect(cx, _panelRect.y + 6f, cw, 18f), $"⚔ {itemName}", titleStyle);
            string distTxt = dist >= 0f ? $" · à {dist:0.0} m" : "";
            GUI.Label(new Rect(cx, _panelRect.y + 24f, cw, 16f),
                $"Ex-{owner} · {weight:0.0} kg{distTxt}", hintStyle);

            bool actorValid = actor != null && actor.Stats != null && actor.Stats.IsAlive
                && !actor.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient)
                && !actor.Stats.ActiveStatus.HasFlag(StatusEffect.Paralyse);
            bool canPickup = actorValid && _target.CanBePickedUpBy(actor);
            bool canKick = actorValid && dist >= 0f && dist <= _target.PickupRadius + 1.0f;

            // Coût du shoot : 1 PA en combat (anti-spam), gratuit en exploration.
            bool inCombat = _turnManager != null && !_turnManager.IsInExploration && !_turnManager.IsCombatOver;
            bool infiniteAP = _arena != null && _arena.InfiniteAP;
            int kickCost = (inCombat && !infiniteAP) ? 1 : 0;
            bool canAffordKick = !actorValid || kickCost <= 0 || actor.Stats.CurrentActionPoints >= kickCost;

            float btnY = _panelRect.y + 44f;
            float btnH = 26f;
            float gap = 6f;

            // --- Ramasser ---
            bool prevEnabled = GUI.enabled;
            GUI.enabled = canPickup;
            string pickupLabel = actor == null ? "✋ Ramasser (aucune unité)" : $"✋ Ramasser — {actor.Stats.Name}";
            if (GUI.Button(new Rect(cx, btnY, cw, btnH), pickupLabel))
            {
                bool ok = _target.TryPickup(actor);
                _arena?.Log(ok
                    ? $"⚔ <b>{actor.Stats.Name}</b> ramasse <b>{itemName}</b>."
                    : $"⚠️ Ramassage impossible : rapprochez <b>{(actor != null ? actor.Stats.Name : "?")}</b> (à {dist:0.0} m).");
                if (ok) { ArmInputBlocker(); CloseMenu(); }
                GUI.enabled = prevEnabled;
                e.Use();
                return;
            }
            GUI.enabled = prevEnabled;
            btnY += btnH + gap;
            if (!actorValid)
                GUI.Label(new Rect(cx, btnY - gap + 1f, cw, 12f), "", hintStyle);
            else if (!canPickup)
                GUI.Label(new Rect(cx, btnY - 16f, cw, 14f), $"<i>Trop loin : rapprochez-vous (portée {(_target.PickupRadius + 0.35f):0.0} m)</i>", hintStyle);

            // --- Kicker plus loin ---
            prevEnabled = GUI.enabled;
            GUI.enabled = canKick && canAffordKick;
            string kickLabel = kickCost > 0 ? $"🦵 Shooter plus loin (1 PA)" : "🦵 Shooter plus loin";
            if (GUI.Button(new Rect(cx, btnY, cw, btnH), kickLabel))
            {
                bool paid = true;
                if (kickCost > 0 && actor != null)
                    paid = actor.Stats.ConsumeActionPoints(kickCost);
                if (!paid)
                {
                    _arena?.Log($"⚠️ <b>{actor.Stats.Name}</b> n'a pas assez de PA pour shooter.");
                }
                else
                {
                    bool ok = _target.TryKickFurther(actor);
                    _arena?.Log(ok
                        ? $"🦵 <b>{actor.Stats.Name}</b> shoote <b>{itemName}</b> plus loin."
                        : "⚠️ Shoot impossible : trop loin.");
                    ArmInputBlocker();
                    CloseMenu();
                }
                GUI.enabled = prevEnabled;
                e.Use();
                return;
            }
            GUI.enabled = prevEnabled;
            btnY += btnH + gap;
            if (actorValid && !canAffordKick)
                GUI.Label(new Rect(cx, btnY - 16f, cw, 14f), "<i>PA insuffisants (1 PA requis)</i>", hintStyle);

            // --- Détruire ---
            if (GUI.Button(new Rect(cx, btnY, cw, btnH), "🗑 Détruire (définitif)"))
            {
                _arena?.Log($"🗑 <b>{(actor != null ? actor.Stats.Name : "Quelqu'un")}</b> détruit <b>{itemName}</b> (ex-{owner}).");
                try { _target.DestroyPickup(actor); } catch { }
                ArmInputBlocker();
                CloseMenu();
                e.Use();
                return;
            }
            btnY += btnH + gap;

            // --- Fermer ---
            if (GUI.Button(new Rect(cx, btnY, cw, 22f), "Fermer (Échap)"))
            {
                ArmInputBlocker();
                CloseMenu();
                e.Use();
                return;
            }

            // Consomme les clics résiduels sur le panneau (anti click-through vers le sol 3D).
            if (_panelRect.Contains(mousePos)
                && (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ContextClick || e.type == EventType.ScrollWheel))
            {
                e.Use();
            }
        }
    }
}
