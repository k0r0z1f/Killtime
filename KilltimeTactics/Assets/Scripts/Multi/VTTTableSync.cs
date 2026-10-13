using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.AI;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.CameraSystem;
using Killtime.Audio;
using Killtime.UI;

namespace Killtime.Multi
{
    /// <summary>
    /// Synchronisation de la table virtuelle (Virtual Tabletop).
    /// - Autorité : Le GM est l'hôte de référence du combat et de l'IA.
    /// - File d'attente FIFO : Toutes les actions distantes (mouvements le long d'un chemin,
    ///   attaques ciblées, grenades, transitions de tour) sont exécutées dans l'ordre strict reçu.
    /// - Anti-boucle : Les actions exécutées depuis le réseau ne sont jamais réémises.
    /// </summary>
    [DisallowMultipleComponent]
    public partial class VTTTableSync : MonoBehaviour
    {
        public static VTTTableSync Instance { get; private set; }

        [Header("Références")]
        [SerializeField] private VTTRoomManager _room;
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private CombatDevArena _arena;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private TacticalAIController _aiController;
        [SerializeField] private CombatHUD _hud;
        [SerializeField] private TacticalCameraController _cameraController;
        [SerializeField] private CinematicDirector _cinematicDirector;

        [Header("Options")]
        [Tooltip("Si coché, force la téléportation instantanée pour tous les mouvements distants (désactive l'animation de marche).")]
        [SerializeField] private bool _teleportRemoteMoves = false;

        public bool TeleportRemoteMoves
        {
            get => _teleportRemoteMoves;
            set => _teleportRemoteMoves = value;
        }

        public event Action<string, string, string> OnChatReceived; // (from, role, text)
        public event Action<string, string, VTTDicePayload> OnDiceReceived; // (from, role, dice)
        public event Action<VTTSceneControlPayload> OnRemoteSceneControlApplied;
        public event Action<TacticalUnit, string, string> OnRemoteMoveApplied; // (unit, from, role)
        public event Action<string, string> OnRemoteMapApplied; // (mapName, from)
        public event Action<VTTCombatActionPayload> OnRemoteCombatActionApplied;
        public event Action<VTTTurnControlPayload> OnRemoteTurnControlApplied;
        public event Action<VTTRoomSettingsPayload> OnRemoteRoomSettingsApplied;

        public string ActiveMapName { get; private set; }
        public bool IsApplyingRemoteAction => _applyingRemote;
        public int CurrentRound => _turnManager != null ? _turnManager.CurrentRound : 1;

        private readonly Dictionary<string, TacticalUnit> _units = new();
        private readonly Queue<IEnumerator> _actionQueue = new();
        private bool _isProcessingQueue;
        private bool _applyingRemote;
        private bool _applyingRemoteMap;
        private float _lastMapPublishTime = -99f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureDependencies();
            if (_arena != null && _arena.CurrentLoadedMap != null)
                ActiveMapName = _arena.CurrentLoadedMap.MapName;
            RebuildRegistry();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            EnsureDependencies();
            if (_room != null)
            {
                _room.OnTableOp += HandleTableOp;
                _room.OnJoined += HandleRoomJoined;
            }
            if (_arena != null) _arena.OnLoadedMapRegistered += HandleLocalMapRegistered;

            TacticalUnit.OnAnyUnitMoved += HandleAnyUnitMoved;
            TacticalUnit.OnAnyUnitTeleported += HandleAnyUnitTeleported;
        }

        private void OnDisable()
        {
            if (_room != null)
            {
                _room.OnTableOp -= HandleTableOp;
                _room.OnJoined -= HandleRoomJoined;
            }
            if (_arena != null) _arena.OnLoadedMapRegistered -= HandleLocalMapRegistered;

            TacticalUnit.OnAnyUnitMoved -= HandleAnyUnitMoved;
            TacticalUnit.OnAnyUnitTeleported -= HandleAnyUnitTeleported;
        }

        private void EnsureDependencies()
        {
            if (_room == null) _room = VTTRoomManager.Instance ?? FindAnyObjectByType<VTTRoomManager>();
            if (_grid == null) _grid = GetComponent<TacticalHexGrid>() ?? FindAnyObjectByType<TacticalHexGrid>();
            if (_arena == null) _arena = GetComponent<CombatDevArena>() ?? FindAnyObjectByType<CombatDevArena>();
            if (_turnManager == null) _turnManager = GetComponent<TurnManager>() ?? FindAnyObjectByType<TurnManager>();
            if (_aiController == null) _aiController = GetComponent<TacticalAIController>() ?? FindAnyObjectByType<TacticalAIController>();
            if (_hud == null) _hud = FindAnyObjectByType<CombatHUD>();
            if (_cameraController == null) _cameraController = FindAnyObjectByType<TacticalCameraController>();
            if (_cinematicDirector == null) _cinematicDirector = FindAnyObjectByType<CinematicDirector>();
        }


        // =========================================================================
        // RÉCEPTION DES OPS DE TABLE (DISPATCH & FILE FIFO)
        // =========================================================================

        private void HandleTableOp(VTTOpEnvelope op, string payloadJson)
        {
            if (op == null || string.IsNullOrEmpty(op.op)) return;
            // Ignore l'écho de nos propres ops
            if (_room != null && op.from == _room.ClientId) return;

            switch (op.op)
            {
                case VTTProtocol.OpUnitMove:
                    EnqueueRemoteAction(HandleRemoteMoveOpRoutine(op, payloadJson));
                    break;
                case VTTProtocol.OpCombatAction:
                    EnqueueRemoteAction(HandleRemoteCombatActionRoutine(op, payloadJson));
                    break;
                case VTTProtocol.OpTurnControl:
                    ApplyRemoteTurnControl(op, payloadJson);
                    break;
                case VTTProtocol.OpRoomSettings:
                    ApplyRemoteRoomSettings(op, payloadJson);
                    break;
                case VTTProtocol.OpChat:
                    try { OnChatReceived?.Invoke(op.fromName ?? op.from, op.fromRole ?? "", ExtractText(payloadJson)); }
                    catch (Exception e) { Debug.LogException(e); }
                    break;
                case VTTProtocol.OpDice:
                    try
                    {
                        var dice = JsonUtility.FromJson<VTTDicePayload>(payloadJson);
                        OnDiceReceived?.Invoke(op.fromName ?? op.from, op.fromRole ?? "", dice);
                    }
                    catch (Exception e) { Debug.LogException(e); }
                    Debug.Log($"[VTT] Dés de {op.fromName} ({op.fromRole}) : {payloadJson}");
                    break;
                case VTTProtocol.OpSceneControl:
                    ApplyRemoteSceneControl(op, payloadJson);
                    break;
                case VTTProtocol.OpMapLoad:
                    ApplyRemoteMap(op, payloadJson);
                    break;
                case VTTProtocol.OpMapRequest:
                    if (_room != null && _room.IsGM)
                    {
                        if (Time.unscaledTime - _lastMapPublishTime >= 2.0f)
                        {
                            _lastMapPublishTime = Time.unscaledTime;
                            PublishCurrentMap();
                        }
                        BroadcastRoomSettings();
                        BroadcastFullCombatState();
                    }
                    break;
                case VTTProtocol.OpActionRequest:
                    if (_room != null && _room.IsGM)
                    {
                        HandleActionRequestFromPlayer(op, payloadJson);
                    }
                    break;
                case VTTProtocol.OpUnitClaim:
                    // Hub uniquement : mémorise les avatars possédés par l'émetteur
                    // pour le filtrage asymétrique. Les clients ignorent l'écho.
                    break;
                default:
                    Debug.Log($"[VTT] Op '{op.op}' reçue (non gérée).");
                    break;
            }
        }
    }
}
