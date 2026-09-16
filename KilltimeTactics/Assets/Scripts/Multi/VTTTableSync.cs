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
    public class VTTTableSync : MonoBehaviour
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
        // ANNUAIRE DES UNITÉS (unitId = GameObject.name)
        // =========================================================================

        public void RebuildRegistry()
        {
            _units.Clear();
            var all = FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
            {
                var u = all[i];
                if (u == null || !u.gameObject.activeInHierarchy) continue;
                string id = UnitIdOf(u);
                if (id.StartsWith("[Disposed]")) continue;
                if (!_units.ContainsKey(id)) _units.Add(id, u);
            }
        }

        public void RegisterUnit(TacticalUnit unit)
        {
            if (unit == null) return;
            _units[UnitIdOf(unit)] = unit;
        }

        public void UnregisterUnit(TacticalUnit unit)
        {
            if (unit == null) return;
            _units.Remove(UnitIdOf(unit));
        }

        public static string UnitIdOf(TacticalUnit unit)
        {
            if (unit == null) return "";
            return unit.gameObject.name;
        }

        public TacticalUnit FindUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return null;
            if (_units.TryGetValue(unitId, out var unit) && unit != null) return unit;
            RebuildRegistry();
            _units.TryGetValue(unitId, out unit);
            return unit;
        }

        private void RebuildRegistryIfMissing(string id, TacticalUnit unit)
        {
            if (!_units.ContainsKey(id)) _units[id] = unit;
        }

        // =========================================================================
        // ÉMISSION CÔTÉ GM (AUTORITÉ DU COMBAT)
        // =========================================================================

        private void HandleAnyUnitMoved(TacticalUnit unit, List<HexCoordinates> path, int apCost)
        {
            if (_applyingRemote) return;
            if (_room == null || !_room.InRoom) return;
            if (!_room.IsGM && !unit.IsPlayerControlled) return;

            NotifyLocalMove(unit, path, apCost);
        }

        private void HandleAnyUnitTeleported(TacticalUnit unit, HexCoordinates coords)
        {
            if (_applyingRemote) return;
            if (_room == null || !_room.InRoom) return;
            if (!_room.IsGM && !unit.IsPlayerControlled) return;

            NotifyLocalMove(unit, null, 0);
        }

        public void NotifyLocalMove(TacticalUnit unit, List<HexCoordinates> path = null, int apCost = 0)
        {
            if (_applyingRemote) return;
            if (unit == null || _room == null || !_room.InRoom) return;
            if (!_room.IsGM && !unit.IsPlayerControlled) return;
            RebuildRegistryIfMissing(UnitIdOf(unit), unit);

            int finalQ = unit.CurrentCoords.Q;
            int finalR = unit.CurrentCoords.R;

            List<VTTCoord> vttPath = null;
            if (path != null && path.Count > 0)
            {
                vttPath = new List<VTTCoord>(path.Count);
                for (int i = 0; i < path.Count; i++)
                {
                    vttPath.Add(new VTTCoord(path[i].Q, path[i].R));
                }
                finalQ = path[path.Count - 1].Q;
                finalR = path[path.Count - 1].R;
            }

            var payload = new VTTCombatActionPayload
            {
                action = "move",
                actorId = UnitIdOf(unit),
                destQ = finalQ,
                destR = finalR,
                path = vttPath,
                apCost = apCost
            };

            _room.SendTableOp(VTTProtocol.OpUnitMove, JsonUtility.ToJson(payload));
        }

        public void BroadcastCombatAction(VTTCombatActionPayload action)
        {
            if (_applyingRemote) return;
            if (action == null || _room == null || !_room.InRoom || !_room.IsGM) return;
            _room.SendTableOp(VTTProtocol.OpCombatAction, JsonUtility.ToJson(action));
        }

        public void BroadcastTurnControl(VTTTurnControlPayload turnControl)
        {
            if (_applyingRemote) return;
            if (turnControl == null || _room == null || !_room.InRoom || !_room.IsGM) return;
            _room.SendTableOp(VTTProtocol.OpTurnControl, JsonUtility.ToJson(turnControl));
        }

        public void BroadcastRoomSettings()
        {
            if (_room == null || !_room.InRoom || !_room.IsGM) return;
            if (_aiController == null) _aiController = FindAnyObjectByType<TacticalAIController>();
            if (_aiController == null) return;

            var payload = new VTTRoomSettingsPayload
            {
                isAIEnabled = _aiController.IsAIEnabled,
                aiMode = (int)_aiController.Mode,
                aiPersonality = (int)_aiController.DefaultPersonality,
                aiActionDelay = _aiController.ActionDelay,
                aiRetreatRatio = _aiController.RetreatHealthRatio,
                aiDefensiveReserve = _aiController.DefensiveAPReserve,
                activeMapName = ActiveMapName ?? ""
            };
            _room.SendTableOp(VTTProtocol.OpRoomSettings, JsonUtility.ToJson(payload));
        }

        public void BroadcastArenaReset(int layoutType = -1)
        {
            if (_applyingRemote) return;
            if (_room == null || !_room.InRoom || !_room.IsGM) return;

            var payload = new VTTTurnControlPayload
            {
                action = VTTProtocol.TurnActionArenaReset,
                round = 1,
                targetQ = layoutType
            };
            _room.SendTableOp(VTTProtocol.OpTurnControl, JsonUtility.ToJson(payload));
        }

        public void RequestAttack(TacticalUnit attacker, TacticalUnit target, BodyPart part, bool cancelPenalty, SkillType atkSkill, SkillType defSkill, int bonusAP)
        {
            if (_room == null || !_room.InRoom || attacker == null || target == null) return;
            var req = new VTTActionRequestPayload
            {
                action = "attack",
                actorId = UnitIdOf(attacker),
                targetId = UnitIdOf(target),
                targetedPart = (int)part,
                cancelPenaltyWithAP = cancelPenalty,
                attackSkill = (int)atkSkill,
                defenseSkill = (int)defSkill,
                attackerBonusAP = bonusAP
            };
            _room.SendTableOp(VTTProtocol.OpActionRequest, JsonUtility.ToJson(req));
        }

        public void RequestGrenade(TacticalUnit attacker, HexCoordinates coords, string grenadeId, bool launcher, bool aim, int bonusAP)
        {
            if (_room == null || !_room.InRoom || attacker == null) return;
            var req = new VTTActionRequestPayload
            {
                action = "grenade",
                actorId = UnitIdOf(attacker),
                targetQ = coords.Q,
                targetR = coords.R,
                grenadeItemId = grenadeId ?? "",
                useLauncher = launcher,
                aimed = aim,
                attackerBonusAP = bonusAP
            };
            _room.SendTableOp(VTTProtocol.OpActionRequest, JsonUtility.ToJson(req));
        }

        public void RequestBreath(int apGain = 2)
        {
            if (_room == null || !_room.InRoom) return;
            var actor = _turnManager != null ? _turnManager.ActiveUnit : (_arena != null ? _arena.PlayerUnit : null);
            if (actor == null) return;
            var req = new VTTActionRequestPayload
            {
                action = "breath",
                actorId = UnitIdOf(actor),
                breathGainAP = apGain
            };
            _room.SendTableOp(VTTProtocol.OpActionRequest, JsonUtility.ToJson(req));
        }

        public void RequestEndTurn()
        {
            if (_room == null || !_room.InRoom) return;
            var actor = _turnManager != null ? _turnManager.ActiveUnit : (_arena != null ? _arena.PlayerUnit : null);
            var req = new VTTActionRequestPayload
            {
                action = "end_turn",
                actorId = actor != null ? UnitIdOf(actor) : ""
            };
            _room.SendTableOp(VTTProtocol.OpActionRequest, JsonUtility.ToJson(req));
        }

        public void RequestTargetSelection(TacticalUnit target)
        {
            if (_room == null || !_room.InRoom || target == null) return;

            string targetId = UnitIdOf(target);
            if (_lastRequestedTargetId == targetId) return;
            _lastRequestedTargetId = targetId;

            var req = new VTTActionRequestPayload
            {
                action = "select_target",
                targetId = targetId,
                targetQ = target.CurrentCoords.Q,
                targetR = target.CurrentCoords.R
            };
            _room.SendTableOp(VTTProtocol.OpActionRequest, JsonUtility.ToJson(req));
        }

        private void HandleActionRequestFromPlayer(VTTOpEnvelope op, string payloadJson)
        {
            VTTActionRequestPayload req;
            try { req = JsonUtility.FromJson<VTTActionRequestPayload>(payloadJson); }
            catch (Exception e)
            {
                Debug.LogWarning($"[VTT] action_request illisible : {e.Message}");
                return;
            }
            if (req == null || string.IsNullOrEmpty(req.action)) return;

            var actor = FindUnit(req.actorId);
            if (actor == null && _turnManager != null) actor = _turnManager.ActiveUnit;
            if (actor == null) return;

            switch (req.action)
            {
                case "attack":
                    var target = FindUnit(req.targetId);
                    if (target == null && _arena != null) target = _arena.CurrentTarget;
                    if (target != null && _arena != null)
                    {
                        _arena.SelectTarget(target);
                        _arena.ExecuteAttack(
                            (BodyPart)req.targetedPart,
                            req.cancelPenaltyWithAP,
                            (SkillType)req.attackSkill,
                            (SkillType)req.defenseSkill,
                            defenderWantsToDefend: true,
                            weaponBaseDamage: 5,
                            attackerBonusAP: req.attackerBonusAP
                        );
                    }
                    break;

                case "grenade":
                    if (_arena != null)
                    {
                        _arena.ExecuteGrenadeThrow(
                            new HexCoordinates(req.targetQ, req.targetR),
                            req.grenadeItemId,
                            req.useLauncher,
                            req.aimed,
                            req.attackerBonusAP
                        );
                    }
                    break;

                case "breath":
                    if (actor.Stats != null && actor.Stats.TakeEmergencyBreath(req.breathGainAP))
                    {
                        var vis = actor.GetComponent<TacticalUnitVisual>();
                        vis?.SpawnFloatingText($"🫁 Souffle d'Urgence (+{req.breathGainAP} PA)", Color.yellow);
                        if (KilltimeAudioManager.Instance != null)
                            KilltimeAudioManager.Instance.PlayAt(SoundId.Breath_Emergency, actor.transform.position, 0.85f);

                        var actionPayload = new VTTCombatActionPayload
                        {
                            action = "breath",
                            actorId = UnitIdOf(actor),
                            breathGainAP = req.breathGainAP,
                            attackerNewAP = actor.Stats.CurrentActionPoints
                        };
                        BroadcastCombatAction(actionPayload);
                        BroadcastFullCombatState();
                    }
                    break;

                case "end_turn":
                    if (_turnManager != null)
                    {
                        _turnManager.EndCurrentTurn();
                    }
                    break;

                case "select_target":
                    var tgt = FindUnit(req.targetId);
                    if (tgt != null && _arena != null)
                    {
                        _arena.SelectTarget(tgt);
                    }
                    break;
            }
        }

        private string _lastBroadcastTargetId;
        private string _lastRequestedTargetId;

        public void BroadcastTargetSelection(TacticalUnit target)
        {
            if (_applyingRemote) return;
            if (_room == null || !_room.InRoom || !_room.IsGM || target == null) return;

            string targetId = UnitIdOf(target);
            if (_lastBroadcastTargetId == targetId) return;
            _lastBroadcastTargetId = targetId;

            var payload = new VTTTurnControlPayload
            {
                action = VTTProtocol.TurnActionTargetSync,
                round = _turnManager != null ? _turnManager.CurrentRound : 1,
                targetUnitId = targetId,
                targetQ = target.CurrentCoords.Q,
                targetR = target.CurrentCoords.R
            };
            _room.SendTableOp(VTTProtocol.OpTurnControl, JsonUtility.ToJson(payload));
        }

        public void BroadcastFullCombatState()
        {
            if (_room == null || !_room.InRoom || !_room.IsGM) return;
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_turnManager == null) return;

            var curTarget = _arena != null ? _arena.CurrentTarget : null;

            var payload = new VTTTurnControlPayload
            {
                action = VTTProtocol.TurnActionStateSync,
                round = _turnManager.CurrentRound,
                activeUnitId = _turnManager.ActiveUnit != null ? UnitIdOf(_turnManager.ActiveUnit) : "",
                targetUnitId = curTarget != null ? UnitIdOf(curTarget) : "",
                targetQ = curTarget != null ? curTarget.CurrentCoords.Q : 0,
                targetR = curTarget != null ? curTarget.CurrentCoords.R : 0,
                outcome = _turnManager.CurrentOutcome.ToString()
            };

            var allUnits = FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
            for (int i = 0; i < allUnits.Length; i++)
            {
                var u = allUnits[i];
                if (u == null || u.Stats == null) continue;
                var entry = new VTTUnitStateSyncEntry
                {
                    unitId = UnitIdOf(u),
                    q = u.CurrentCoords.Q,
                    r = u.CurrentCoords.R,
                    rotationY = u.transform.rotation.eulerAngles.y,
                    currentHealth = u.Stats.CurrentHealth,
                    maxHealth = u.Stats.MaxHealth,
                    currentAP = u.Stats.CurrentActionPoints,
                    maxAP = u.Stats.MaxActionPoints,
                    essoufflement = u.Stats.Essoufflement,
                    isAlive = u.Stats.IsAlive,
                    isDead = u.Stats.IsDead,
                    activeStatus = (int)u.Stats.ActiveStatus
                };
                if (u.Stats.ActiveStatus != StatusEffect.None)
                {
                    entry.statusEffects.Add(u.Stats.ActiveStatus.ToString());
                }
                payload.unitStates.Add(entry);
            }

            _room.SendTableOp(VTTProtocol.OpTurnControl, JsonUtility.ToJson(payload));
        }

        public void SendChat(string text)
        {
            if (_room != null) _room.SendChat(text);
        }

        public void BroadcastDice(int roll, string formula = "", string detail = "", string reason = "", bool isCritical = false, bool isFumble = false)
        {
            if (_room == null || !_room.InRoom) return;
            string opJson = VTTProtocol.BuildDiceOp(roll, formula, detail, reason, isCritical, isFumble);
            _room.Net?.Send(opJson);
        }

        public void BroadcastSceneControl(VTTSceneControlPayload payload)
        {
            if (_room == null || !_room.InRoom || !_room.IsGM || payload == null) return;
            _room.SendTableOp(VTTProtocol.OpSceneControl, JsonUtility.ToJson(payload));
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
                default:
                    Debug.Log($"[VTT] Op '{op.op}' reçue (non gérée).");
                    break;
            }
        }

        private void HandleRoomJoined()
        {
            if (_room == null) return;
            if (_room.IsGM)
            {
                PublishCurrentMap();
                BroadcastRoomSettings();
                BroadcastFullCombatState();
            }
            else
            {
                _room.SendTableOp(VTTProtocol.OpMapRequest, "{}");
            }
        }

        private void HandleLocalMapRegistered(TacticalMapSaveData map)
        {
            if (map == null) return;
            ActiveMapName = map.MapName;
            if (!_applyingRemoteMap)
            {
                PublishMap(map);
                BroadcastRoomSettings();
                StartCoroutine(DeferredCombatStateBroadcast());
            }
        }

        private IEnumerator DeferredCombatStateBroadcast()
        {
            yield return null;
            yield return new WaitForSeconds(0.1f);
            BroadcastFullCombatState();
        }

        public void PublishCurrentMap()
        {
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
            if (_arena != null && _arena.CurrentLoadedMap != null)
            {
                PublishMap(_arena.CurrentLoadedMap);
            }
        }

        private void CaptureLiveUnitsIntoMap(TacticalMapSaveData map)
        {
            if (map == null) return;
            var liveUnits = FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
            if (liveUnits.Length == 0) return;

            map.PlacedUnits ??= new List<MapUnitData>();
            map.PlacedUnits.Clear();

            for (int i = 0; i < liveUnits.Length; i++)
            {
                var u = liveUnits[i];
                if (u == null || u.Stats == null) continue;
                var entry = new MapUnitData
                {
                    UnitId = u.gameObject.name,
                    Sheet = u.GetOrBuildSheet(),
                    Q = u.CurrentCoords.Q,
                    R = u.CurrentCoords.R,
                    IsPlayer = u.IsPlayerControlled,
                    currentHealth = u.Stats.CurrentHealth,
                    currentAP = u.Stats.CurrentActionPoints,
                    essoufflement = u.Stats.Essoufflement,
                    activeStatus = (int)u.Stats.ActiveStatus
                };
                if (u.Stats.ActiveStatus != StatusEffect.None)
                {
                    entry.statusEffects.Add(u.Stats.ActiveStatus.ToString());
                }
                map.PlacedUnits.Add(entry);
            }
        }

        private void PublishMap(TacticalMapSaveData map)
        {
            if (map == null || _room == null || !_room.InRoom || !_room.IsGM) return;

            CaptureLiveUnitsIntoMap(map);

            string mapJson = JsonUtility.ToJson(map);
            if (System.Text.Encoding.UTF8.GetByteCount(mapJson) > 480 * 1024)
            {
                Debug.LogWarning($"[VTT] Carte '{map.MapName}' non publiée : dépasse 480 Ko.");
                return;
            }
            ActiveMapName = map.MapName;
            _room.SendTableOp(VTTProtocol.OpMapLoad, "{\"map\":" + mapJson + "}");
        }

        private void ApplyRemoteMap(VTTOpEnvelope op, string payloadJson)
        {
            if (_room != null && op.fromRole != VTTProtocol.RoleGM)
            {
                Debug.LogWarning("[VTT] map_load ignoré : l'émetteur n'est pas GM.");
                return;
            }
            string mapJson = ExtractObject(payloadJson, "map");
            if (string.IsNullOrEmpty(mapJson)) return;

            TacticalMapSaveData map;
            try { map = JsonUtility.FromJson<TacticalMapSaveData>(mapJson); }
            catch (Exception e)
            {
                Debug.LogWarning($"[VTT] map_load illisible : {e.Message}");
                return;
            }
            if (map == null || string.IsNullOrEmpty(map.MapName)) return;

            try
            {
                _applyingRemoteMap = true;
                var editor = MapEditorDevWindow.Instance ?? FindAnyObjectByType<MapEditorDevWindow>();
                if (editor == null)
                {
                    var go = new GameObject("[Map] Remote Loader");
                    editor = go.AddComponent<MapEditorDevWindow>();
                    editor.CloseWindow();
                }
                editor.ApplyLoadedMap(map);
                ActiveMapName = map.MapName;
                RebuildRegistry();
                try { OnRemoteMapApplied?.Invoke(map.MapName, op.fromName ?? op.from); }
                catch (Exception e) { Debug.LogException(e); }
            }
            finally
            {
                _applyingRemoteMap = false;
            }
        }

        // =========================================================================
        // GESTIONNAIRE DE FILE D'ATTENTE D'ACTIONS SÉQUENTIELLES (FIFO)
        // =========================================================================

        private void EnqueueRemoteAction(IEnumerator actionRoutine)
        {
            if (actionRoutine == null) return;
            // Si la pause est active, ne pas empiler d'animations
            if (CombatHUD.IsPaused)
            {
                _actionQueue.Clear();
                return;
            }

            _actionQueue.Enqueue(actionRoutine);
            if (!_isProcessingQueue)
            {
                StartCoroutine(ProcessActionQueue());
            }
        }

        private IEnumerator ProcessActionQueue()
        {
            _isProcessingQueue = true;
            while (_actionQueue.Count > 0)
            {
                // Si la pause est enclenchée, vider la file immédiatement
                if (CombatHUD.IsPaused)
                {
                    _actionQueue.Clear();
                    break;
                }

                // Évite tout engorgement : si plus de 5 actions s'accumulent, on purge le retard
                if (_actionQueue.Count > 5)
                {
                    Debug.LogWarning($"[VTT] Engorgement évité ({_actionQueue.Count} actions en attente) : purge et rattrapage en direct.");
                    _actionQueue.Clear();
                    break;
                }

                var action = _actionQueue.Dequeue();
                if (action != null)
                {
                    yield return StartCoroutine(action);
                }
            }
            _isProcessingQueue = false;
        }

        // =========================================================================
        // REJEU DU MOUVEMENT DISTANT
        // =========================================================================

        private IEnumerator HandleRemoteMoveOpRoutine(VTTOpEnvelope op, string payloadJson)
        {
            _applyingRemote = true;
            try
            {
                VTTCombatActionPayload payload = null;
                try { payload = JsonUtility.FromJson<VTTCombatActionPayload>(payloadJson); } catch { /* ignore */ }

                string unitId = payload != null && !string.IsNullOrEmpty(payload.actorId)
                    ? payload.actorId
                    : ExtractString(payloadJson, "unitId");

                if (string.IsNullOrEmpty(unitId)) yield break;

                var unit = FindUnit(unitId);
                if (unit == null)
                {
                    Debug.LogWarning($"[VTT] unit_move ignorée : unité '{unitId}' introuvable.");
                    yield break;
                }

                int destQ = payload != null ? payload.destQ : ExtractInt(payloadJson, "destQ", ExtractInt(payloadJson, "q", unit.CurrentCoords.Q));
                int destR = payload != null ? payload.destR : ExtractInt(payloadJson, "destR", ExtractInt(payloadJson, "r", unit.CurrentCoords.R));

                if (payload != null && payload.path != null && payload.path.Count > 0)
                {
                    destQ = payload.path[payload.path.Count - 1].q;
                    destR = payload.path[payload.path.Count - 1].r;
                }

                var targetCoords = new HexCoordinates(destQ, destR);

                var grid = _grid != null ? _grid : FindAnyObjectByType<TacticalHexGrid>();
                if (grid == null) yield break;

                bool fastForward = _teleportRemoteMoves || _actionQueue.Count > 3 || CombatHUD.IsPaused;

                if (!fastForward && payload != null && payload.path != null && payload.path.Count > 1)
                {
                    var hexPath = new List<HexCoordinates>(payload.path.Count);
                    for (int i = 0; i < payload.path.Count; i++)
                    {
                        hexPath.Add(new HexCoordinates(payload.path[i].q, payload.path[i].r));
                    }

                    if (!unit.CurrentCoords.Equals(hexPath[0]))
                    {
                        unit.TeleportTo(hexPath[0], grid);
                    }

                    var vis = unit.GetComponent<TacticalUnitVisual>();
                    vis?.SpawnFloatingText($"Déplacement (-{payload.apCost} PA)", new Color(0.2f, 0.85f, 1.0f));

                    yield return StartCoroutine(unit.MoveAlongPath(hexPath, grid, payload.apCost));

                    if (!unit.CurrentCoords.Equals(targetCoords))
                    {
                        unit.TeleportTo(targetCoords, grid);
                    }
                }
                else
                {
                    unit.TeleportTo(targetCoords, grid);
                }

                try { OnRemoteMoveApplied?.Invoke(unit, op.fromName ?? op.from, op.fromRole ?? ""); }
                catch (Exception e) { Debug.LogException(e); }
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        // =========================================================================
        // REJEU D'ACTION DE COMBAT (ATTAQUE CIBLÉE, GRENADE, SORT, SOUFFLE)
        // =========================================================================

        private IEnumerator HandleRemoteCombatActionRoutine(VTTOpEnvelope op, string payloadJson)
        {
            VTTCombatActionPayload payload;
            try { payload = JsonUtility.FromJson<VTTCombatActionPayload>(payloadJson); }
            catch (Exception e)
            {
                Debug.LogWarning($"[VTT] combat_action illisible : {e.Message}");
                yield break;
            }
            if (payload == null || string.IsNullOrEmpty(payload.action)) yield break;

            switch (payload.action)
            {
                case "attack":
                    yield return StartCoroutine(ExecuteRemoteAttackRoutine(payload));
                    break;
                case "move":
                    yield return StartCoroutine(HandleRemoteMoveOpRoutine(op, payloadJson));
                    break;
                case "grenade":
                    yield return StartCoroutine(ExecuteRemoteGrenadeRoutine(payload));
                    break;
                case "spell":
                    yield return StartCoroutine(ExecuteRemoteSpellRoutine(payload));
                    break;
                case "breath":
                    yield return StartCoroutine(ExecuteRemoteBreathRoutine(payload));
                    break;
            }

            try { OnRemoteCombatActionApplied?.Invoke(payload); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private IEnumerator ExecuteRemoteAttackRoutine(VTTCombatActionPayload p)
        {
            _applyingRemote = true;
            try
            {
                var attacker = FindUnit(p.actorId);
                var defender = FindUnit(p.targetId);

                if (attacker == null || defender == null)
                {
                    Debug.LogWarning($"[VTT] Attaque distante ignorée : attaquant '{p.actorId}' ou défenseur '{p.targetId}' introuvable.");
                    yield break;
                }

                if (_arena != null)
                {
                    _arena.SelectTarget(defender);
                }

                Vector3 combatDir = defender.transform.position - attacker.transform.position;
                combatDir.y = 0f;
                if (combatDir != Vector3.zero)
                {
                    attacker.transform.rotation = Quaternion.LookRotation(combatDir);
                    if (defender.Stats != null && defender.Stats.CanDefendActively())
                    {
                        defender.transform.rotation = Quaternion.LookRotation(-combatDir);
                    }
                }

                var attVisual = attacker.GetComponent<TacticalUnitVisual>();
                var defVisual = defender.GetComponent<TacticalUnitVisual>();
                var laserWeapon = attacker.GetComponentInChildren<LaserRifleWeapon>();

                Action onActionStart = () =>
                {
                    attVisual?.TriggerRoundkick(p.isMeleeStrike);

                    if (!p.isMeleeStrike && laserWeapon != null)
                    {
                        Vector3 targetCenter = defender.transform.position + Vector3.up * 1.15f;
                        laserWeapon.FireLaser(targetCenter, p.isHit);
                    }

                    if (KilltimeAudioManager.Instance != null)
                    {
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, attacker.transform.position, 0.7f);
                    }

                    string partLabel = BodyPartInfo.GetInfo((BodyPart)p.targetedPart).DisplayName;
                    attVisual?.SpawnFloatingText($"Attaque {partLabel} (-{p.attackerCostAP} PA)", new Color(0.3f, 0.8f, 1.0f));
                };

                Action onDefenseStart = () =>
                {
                    if (defender.Stats != null && defender.Stats.CanDefendActively())
                    {
                        defVisual?.TriggerBodyBlock();
                    }
                };

                Action onStrikePoint = () =>
                {
                    if (KilltimeAudioManager.Instance != null)
                    {
                        KilltimeAudioManager.Instance.PlayCombatResult(
                            p.isHit, !p.isHit, p.isCritical,
                            p.armorAbsorbed > 0, false, "None", defender.transform.position);
                        KilltimeAudioManager.Instance.Play(SoundId.Dice_Roll, 0.35f);
                    }

                    if (defVisual != null)
                    {
                        if (p.isHit)
                        {
                            defVisual.TriggerHitFlash();
                            if (p.isCritical)
                            {
                                defVisual.SpawnFloatingText("[CRITIQUE] COUP DÉCISIF !", new Color(1.0f, 0.85f, 0.1f));
                            }

                            if (p.wasDeflected)
                            {
                                string deflectedPart = BodyPartInfo.GetInfo((BodyPart)p.actualHitPart).DisplayName;
                                defVisual.SpawnFloatingText($"[DÉVIATION] -> {deflectedPart} (Diff 0)", Color.yellow);
                            }
                            else
                            {
                                string diffSign = p.differential > 0 ? $"+{p.differential}" : $"{p.differential}";
                                string hitPart = BodyPartInfo.GetInfo((BodyPart)p.actualHitPart).DisplayName;
                                defVisual.SpawnFloatingText($"[TOUCHÉ] {hitPart} (Diff {diffSign})", new Color(0.9f, 0.9f, 1.0f));
                            }

                            string dmgText = p.armorAbsorbed > 0
                                ? $"-{p.finalDamageApplied} PV (Bruts {p.rawDamage} | Armure -{p.armorAbsorbed})"
                                : $"-{p.finalDamageApplied} PV";
                            defVisual.SpawnFloatingText(dmgText, new Color(1.0f, 0.25f, 0.25f));

                            if (!string.IsNullOrEmpty(p.inflictedStatus))
                            {
                                defVisual.SpawnFloatingText($"[CHOC] TRAUMATIQUE ! [{p.inflictedStatus}]", new Color(1.0f, 0.4f, 0.95f));
                            }
                        }
                        else
                        {
                            defVisual.TriggerBodyBlock();
                            defVisual.SpawnFloatingText("🛡️ Parade / Esquive réussie", Color.cyan);
                        }
                    }

                    if (p.actorRotationY != 0f)
                    {
                        attacker.transform.rotation = Quaternion.Euler(0f, p.actorRotationY, 0f);
                    }
                    if (p.defenderRotationY != 0f)
                    {
                        defender.transform.rotation = Quaternion.Euler(0f, p.defenderRotationY, 0f);
                    }

                    if (p.defenderNewQ != 0 || p.defenderNewR != 0)
                    {
                        var targetCoords = new HexCoordinates(p.defenderNewQ, p.defenderNewR);
                        var currentGrid = _grid != null ? _grid : FindAnyObjectByType<TacticalHexGrid>();
                        if (currentGrid != null && !defender.CurrentCoords.Equals(targetCoords))
                        {
                            defender.TeleportTo(targetCoords, currentGrid);
                        }
                    }

                    if (attacker.Stats != null)
                    {
                        attacker.Stats.CurrentActionPoints = p.attackerNewAP;
                        attacker.Stats.RegisterAttack();
                    }

                    if (defender.Stats != null)
                    {
                        defender.Stats.CurrentActionPoints = p.defenderNewAP;
                        defender.Stats.CurrentHealth = p.defenderNewHealth;

                        if (p.defenderNewActiveStatus != 0)
                        {
                            defender.Stats.ActiveStatus = (StatusEffect)p.defenderNewActiveStatus;
                        }

                        bool shouldDie = p.defenderIsDead || p.defenderNewHealth <= 0 || defender.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient);
                        if (shouldDie)
                        {
                            defender.Stats.IsDead = p.defenderIsDead || p.defenderNewHealth <= 0;
                            if (p.defenderNewHealth <= 0) defender.Stats.CurrentHealth = 0;
                            defender.Stats.ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;

                            defVisual?.SpawnFloatingText("[HORS DE COMBAT]", Color.red);
                            defVisual?.TriggerFallingBackDeath();
                        }
                    }

                    bool isFatalRemote = p.defenderIsDead || p.defenderNewHealth <= 0;
                    CombatHUD.RecordCombatAction(
                        attacker.Stats != null ? attacker.Stats.Name : p.actorId,
                        defender.Stats != null ? defender.Stats.Name : p.targetId,
                        attacker.IsPlayerControlled,
                        defender.IsPlayerControlled,
                        p.isHit,
                        p.isCritical,
                        !p.isHit,
                        p.rawDamage,
                        p.armorAbsorbed,
                        p.finalDamageApplied,
                        !string.IsNullOrEmpty(p.inflictedStatus),
                        isFatalRemote,
                        p.attackerCostAP
                    );

                    if (!string.IsNullOrEmpty(p.combatLog))
                    {
                        _hud?.AddCombatLog(p.combatLog);
                        _arena?.Log(p.combatLog);
                    }
                };

                bool fastForward = _teleportRemoteMoves || _actionQueue.Count > 3 || CombatHUD.IsPaused;
                bool freeLookActive = _cinematicDirector != null && _cinematicDirector.IsFreeLook;
                bool allowCinematic = !fastForward && _cinematicDirector != null && !freeLookActive 
                                      && (_arena == null || _arena.EnableCinematicKillcam);

                if (allowCinematic)
                {
                    bool cinematicDone = false;
                    _cinematicDirector.PlayCinematicKillshot(
                        attacker.transform,
                        defender.transform,
                        onStrikePoint: onStrikePoint,
                        onComplete: () => { cinematicDone = true; },
                        onActionStart: onActionStart,
                        onDefenseStart: onDefenseStart
                    );

                    float safetyTimeout = 5.0f;
                    while (!cinematicDone && safetyTimeout > 0f)
                    {
                        safetyTimeout -= Time.unscaledDeltaTime;
                        yield return null;
                    }
                }
                else
                {
                    onActionStart();
                    if (!fastForward) yield return new WaitForSeconds(0.06f);
                    onDefenseStart();
                    if (!fastForward) yield return new WaitForSeconds(0.16f);
                    onStrikePoint();
                    if (!fastForward) yield return new WaitForSeconds(0.35f);
                }
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private IEnumerator ExecuteRemoteGrenadeRoutine(VTTCombatActionPayload p)
        {
            _applyingRemote = true;
            try
            {
                var attacker = FindUnit(p.actorId);
                var attVisual = attacker?.GetComponent<TacticalUnitVisual>();
                attVisual?.SpawnFloatingText(p.useLauncher ? "💣 Tir Lance-Grenades" : "💣 Lancer de Grenade", new Color(1f, 0.6f, 0.15f));

                if (attacker?.Stats != null)
                {
                    attacker.Stats.ConsumeActionPoints(p.attackerCostAP);
                }

                if (KilltimeAudioManager.Instance != null && attacker != null)
                {
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, attacker.transform.position, 0.8f);
                }

                yield return new WaitForSeconds(0.4f);

                if (!string.IsNullOrEmpty(p.combatLog))
                {
                    _hud?.AddCombatLog(p.combatLog);
                    _arena?.Log(p.combatLog);
                }

                yield return new WaitForSeconds(0.2f);
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private IEnumerator ExecuteRemoteSpellRoutine(VTTCombatActionPayload p)
        {
            _applyingRemote = true;
            try
            {
                var actor = FindUnit(p.actorId);
                var target = FindUnit(p.targetId);
                var actorVis = actor?.GetComponent<TacticalUnitVisual>();
                var targetVis = target?.GetComponent<TacticalUnitVisual>();

                actorVis?.SpawnFloatingText($"🔮 {p.spellName} (-{p.apCost} PA)", Color.cyan);
                yield return new WaitForSeconds(0.3f);

                targetVis?.TriggerHitFlash();
                targetVis?.SpawnFloatingText($"-{p.spellDamage} Arcanique", Color.magenta);

                if (target?.Stats != null)
                {
                    target.Stats.CurrentHealth = p.defenderNewHealth;
                }
                if (actor?.Stats != null)
                {
                    actor.Stats.CurrentActionPoints = p.attackerNewAP;
                }

                yield return new WaitForSeconds(0.2f);
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private IEnumerator ExecuteRemoteBreathRoutine(VTTCombatActionPayload p)
        {
            _applyingRemote = true;
            try
            {
                var unit = FindUnit(p.actorId);
                var vis = unit?.GetComponent<TacticalUnitVisual>();
                vis?.SpawnFloatingText($"🫁 Souffle d'Urgence (+{p.breathGainAP} PA)", Color.yellow);
                if (unit?.Stats != null)
                {
                    unit.Stats.CurrentActionPoints = p.attackerNewAP;
                }
                yield return new WaitForSeconds(0.2f);
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        // =========================================================================
        // REJEU DU CONTRÔLE DE TOUR (ROUND, INITIATIVE, TOURS)
        // =========================================================================

        private void ApplyRemoteTurnControl(VTTOpEnvelope op, string payloadJson)
        {
            if (_room != null && op.fromRole != VTTProtocol.RoleGM) return;

            _applyingRemote = true;
            try
            {
                VTTTurnControlPayload payload;
                try { payload = JsonUtility.FromJson<VTTTurnControlPayload>(payloadJson); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VTT] turn_control illisible : {e.Message}");
                    return;
                }
                if (payload == null || string.IsNullOrEmpty(payload.action)) return;

                if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
                if (_turnManager == null) return;

                switch (payload.action)
                {
                case VTTProtocol.TurnActionRoundStart:
                    _turnManager.ApplyRemoteRoundStart(payload.round, payload.turnOrder);
                    _hud?.AddCombatLog($"⚔️ <b>--- ROUND {payload.round} COMMENCÉ ---</b>");
                    break;
                case VTTProtocol.TurnActionTurnStart:
                    _turnManager.ApplyRemoteTurnStart(payload.activeUnitId);
                    var active = FindUnit(payload.activeUnitId);
                    if (active != null)
                    {
                        if (_cameraController == null) _cameraController = FindAnyObjectByType<TacticalCameraController>();
                        _cameraController?.FocusOn(active.transform);
                        _hud?.AddCombatLog($"--- Tour de <b>{active.Stats.Name}</b> ({active.Stats.CurrentActionPoints} PA) ---");
                    }

                    if (!string.IsNullOrEmpty(payload.targetUnitId))
                    {
                        var tgt = FindUnit(payload.targetUnitId);
                        if (tgt != null && _arena != null) _arena.SelectTarget(tgt);
                    }
                    else if (payload.targetQ != 0 || payload.targetR != 0)
                    {
                        var gVis = _arena != null ? _arena.GetComponent<HexGridVisualizer>() : FindAnyObjectByType<HexGridVisualizer>();
                        gVis?.SetTargetCoord(new HexCoordinates(payload.targetQ, payload.targetR));
                    }
                    break;
                case VTTProtocol.TurnActionTargetSync:
                    if (!string.IsNullOrEmpty(payload.targetUnitId))
                    {
                        var tgtSync = FindUnit(payload.targetUnitId);
                        if (tgtSync != null && _arena != null) _arena.SelectTarget(tgtSync);
                    }
                    else if (payload.targetQ != 0 || payload.targetR != 0)
                    {
                        var gVis = _arena != null ? _arena.GetComponent<HexGridVisualizer>() : FindAnyObjectByType<HexGridVisualizer>();
                        gVis?.SetTargetCoord(new HexCoordinates(payload.targetQ, payload.targetR));
                    }
                    break;
                case VTTProtocol.TurnActionArenaReset:
                    _applyingRemote = true;
                    try
                    {
                        if (_arena != null)
                        {
                            _arena.ClearAllUnits();
                            if (payload.targetQ >= 0)
                            {
                                _arena.ApplyArenaLayout((ArenaLayoutType)payload.targetQ);
                            }
                            _arena.ResetArena();
                        }
                        RebuildRegistry();
                        _hud?.AddCombatLog("🔄 L'Hôte (MJ) a réinitialisé le champ de bataille.");
                    }
                    finally
                    {
                        _applyingRemote = false;
                    }
                    break;
                case VTTProtocol.TurnActionEndTurn:
                    _turnManager.ApplyRemoteEndTurn(payload.activeUnitId);
                    break;
                case VTTProtocol.TurnActionCombatEnded:
                    _turnManager.ApplyRemoteCombatEnded(payload.outcome);
                    _hud?.AddCombatLog($"🏆 Combat terminé : {payload.outcome}");
                    break;
                case VTTProtocol.TurnActionPause:
                    CombatHUD.Instance?.SetPause(true);
                    _actionQueue.Clear();
                    _hud?.AddCombatLog("⏸ Le Maître du Jeu a mis le combat en pause.");
                    break;
                case VTTProtocol.TurnActionResume:
                    CombatHUD.Instance?.SetPause(false);
                    _hud?.AddCombatLog("▶ Le Maître du Jeu a repris le combat.");
                    break;
                case VTTProtocol.TurnActionStateSync:
                    _turnManager.ApplyRemoteStateSync(payload.round, payload.activeUnitId, payload.outcome);
                    ApplyRemoteStateSync(payload);
                    break;
                }

                try { OnRemoteTurnControlApplied?.Invoke(payload); }
                catch (Exception e) { Debug.LogException(e); }
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private void ApplyRemoteStateSync(VTTTurnControlPayload payload)
        {
            if (payload.unitStates == null) return;
            var grid = _grid != null ? _grid : FindAnyObjectByType<TacticalHexGrid>();

            for (int i = 0; i < payload.unitStates.Count; i++)
            {
                var s = payload.unitStates[i];
                if (s == null) continue;
                var unit = FindUnit(s.unitId);
                if (unit == null) continue;

                if (unit.Stats != null)
                {
                    unit.Stats.CurrentHealth = s.currentHealth;
                    unit.Stats.CurrentActionPoints = s.currentAP;
                    unit.Stats.Essoufflement = s.essoufflement;
                    unit.Stats.IsDead = s.isDead || (!s.isAlive && s.currentHealth <= 0);

                    if (s.activeStatus != 0)
                    {
                        unit.Stats.ActiveStatus = (StatusEffect)s.activeStatus;
                    }
                    else if (s.statusEffects != null && s.statusEffects.Count > 0)
                    {
                        StatusEffect flags = StatusEffect.None;
                        for (int k = 0; k < s.statusEffects.Count; k++)
                        {
                            if (Enum.TryParse<StatusEffect>(s.statusEffects[k], out var parsed))
                            {
                                flags |= parsed;
                            }
                        }
                        unit.Stats.ActiveStatus = flags;
                    }
                    else
                    {
                        unit.Stats.ActiveStatus = StatusEffect.None;
                    }

                    if (unit.Stats.IsDead || !s.isAlive || unit.Stats.CurrentHealth <= 0)
                    {
                        unit.Stats.ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
                    }
                }

                unit.transform.rotation = Quaternion.Euler(0f, s.rotationY, 0f);

                var targetCoords = new HexCoordinates(s.q, s.r);
                if (grid != null && !unit.CurrentCoords.Equals(targetCoords))
                {
                    unit.TeleportTo(targetCoords, grid);
                }

                var vis = unit.GetComponent<TacticalUnitVisual>();
                if (unit.Stats != null)
                {
                    if (!s.isAlive || unit.Stats.CurrentHealth <= 0 || unit.Stats.IsDead || unit.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient))
                    {
                        unit.Stats.CurrentHealth = 0;
                        unit.Stats.IsDead = true;
                        vis?.TriggerFallingBackDeath();
                    }
                    vis?.RefreshEquippedWeaponVisual();
                }
            }
        }

        // =========================================================================
        // RÉGLAGES DE LA SALLE (IA ET PARAMÈTRES)
        // =========================================================================

        private void ApplyRemoteRoomSettings(VTTOpEnvelope op, string payloadJson)
        {
            if (_room != null && op.fromRole != VTTProtocol.RoleGM) return;
            try
            {
                var settings = JsonUtility.FromJson<VTTRoomSettingsPayload>(payloadJson);
                if (settings != null)
                {
                    if (_aiController == null) _aiController = FindAnyObjectByType<TacticalAIController>();
                    _aiController?.ApplyGMSettings(settings);
                    try { OnRemoteRoomSettingsApplied?.Invoke(settings); }
                    catch (Exception e) { Debug.LogException(e); }
                    Debug.Log($"[VTT] Paramètres d'IA et de table appliqués depuis le GM ({op.fromName}).");
                }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void ApplyRemoteSceneControl(VTTOpEnvelope op, string payloadJson)
        {
            if (_room != null && op.fromRole != VTTProtocol.RoleGM) return;
            try
            {
                var payload = JsonUtility.FromJson<VTTSceneControlPayload>(payloadJson);
                if (payload == null) return;
                try { OnRemoteSceneControlApplied?.Invoke(payload); }
                catch (Exception e) { Debug.LogException(e); }
                Debug.Log($"[VTT] Contrôle de scène reçu du GM ({op.fromName}) : {payload.action}");
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        // =========================================================================
        // HELPERS D'EXTRACTION JSON
        // =========================================================================

        private static string ExtractString(string json, string field)
        {
            return VTTProtocol.ExtractString(json, field);
        }

        private static int ExtractInt(string json, string field, int fallback)
        {
            string s = VTTProtocol.ExtractString(json, field, "");
            if (int.TryParse(s, out int val)) return val;
            return fallback;
        }

        private static string ExtractText(string payloadJson)
        {
            return VTTProtocol.ExtractString(payloadJson, "text");
        }

        private static string ExtractObject(string json, string field)
        {
            return VTTProtocol.ExtractObject(json, field);
        }
    }
}
