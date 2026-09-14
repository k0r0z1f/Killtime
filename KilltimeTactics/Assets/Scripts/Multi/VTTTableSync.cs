using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;

namespace Killtime.Multi
{
    /// <summary>
    /// Synchronisation minimale de la table virtuelle (fondation, pas autorité).
    /// - Émission : appelez NotifyLocalMove(unit) après un déplacement local
    ///   (TacticalUnit.TeleportTo / MoveAlongPath) pour le diffuser à la room.
    /// - Réception : les ops unit_move distants téléportent l'unité locale
    ///   correspondante (même unitId). Pas de re-diffusion (anti-boucle).
    /// Règle MVP : le hub relaie tout ; le GM fait foi en cas de conflit
    /// (le dernier op GM gagne, les joueurs se recalent dessus).
    /// unitId recommandé : nom unique du GameObject ou GUID de fiche.
    /// </summary>
    [DisallowMultipleComponent]
    public class VTTTableSync : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private VTTRoomManager _room;
        [SerializeField] private TacticalHexGrid _grid;
        [Tooltip("Si coché, les mouvements distants utilisent TeleportTo (robuste). Sinon tentative de MoveAlongPath.")]
        [SerializeField] private bool _teleportRemoteMoves = true;

        public event Action<string, string, string> OnChatReceived; // (from, role, text)
        public event Action<TacticalUnit, string, string> OnRemoteMoveApplied; // (unit, from, role)

        private readonly Dictionary<string, TacticalUnit> _units = new();
        private bool _applyingRemote;

        private void Awake()
        {
            if (_room == null) _room = VTTRoomManager.Instance ?? FindAnyObjectByType<VTTRoomManager>();
            if (_grid == null) _grid = FindAnyObjectByType<TacticalHexGrid>();
            RebuildRegistry();
        }

        private void OnEnable()
        {
            if (_room != null) _room.OnTableOp += HandleTableOp;
        }

        private void OnDisable()
        {
            if (_room != null) _room.OnTableOp -= HandleTableOp;
        }

        /// <summary>Reconstruit l'annuaire unitId -> TacticalUnit (unitId = GameObject.name).</summary>
        public void RebuildRegistry()
        {
            _units.Clear();
            var all = FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
            foreach (var u in all)
            {
                if (u == null) continue;
                string id = UnitIdOf(u);
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
            // Stable par défaut dans la scène ; les spawns dynamiques
            // devraient renommer le GameObject avec un GUID de fiche.
            return unit.gameObject.name;
        }

        /// <summary>À appeler après un déplacement LOCAL validé par les règles (PA consommés, etc.).</summary>
        public void NotifyLocalMove(TacticalUnit unit)
        {
            if (_applyingRemote) return; // anti-boucle
            if (unit == null || _room == null || !_room.InRoom) return;
            RebuildRegistryIfMissing(UnitIdOf(unit), unit);
            _room.SendTableOp(VTTProtocol.OpUnitMove,
                $"{{\"unitId\":\"{VTTProtocol.Escape(UnitIdOf(unit))}\",\"q\":{unit.CurrentCoords.Q},\"r\":{unit.CurrentCoords.R}}}");
        }

        public void SendChat(string text)
        {
            if (_room != null) _room.SendChat(text);
        }

        private void RebuildRegistryIfMissing(string id, TacticalUnit unit)
        {
            if (!_units.ContainsKey(id)) _units[id] = unit;
        }

        private void HandleTableOp(VTTOpEnvelope op, string payloadJson)
        {
            if (op == null || string.IsNullOrEmpty(op.op)) return;
            // Ignore l'écho de nos propres ops (le hub diffuse à tous, expéditeur inclus).
            if (_room != null && op.from == _room.ClientId) return;

            switch (op.op)
            {
                case VTTProtocol.OpUnitMove:
                    ApplyRemoteMove(op, payloadJson);
                    break;
                case VTTProtocol.OpChat:
                    try { OnChatReceived?.Invoke(op.fromName ?? op.from, op.fromRole ?? "", ExtractText(payloadJson)); }
                    catch (Exception e) { Debug.LogException(e); }
                    break;
                case VTTProtocol.OpDice:
                    Debug.Log($"[VTT] Dés de {op.fromName} ({op.fromRole}) : {payloadJson}");
                    break;
                default:
                    // turn_control / scene_control / room_settings : le jeu les
                    // branchera quand le tour partagé sera implémenté.
                    Debug.Log($"[VTT] Op '{op.op}' de {op.fromName} ignorée par TableSync (pas encore gérée).");
                    break;
            }
        }

        private void ApplyRemoteMove(VTTOpEnvelope op, string payloadJson)
        {
            string unitId = ExtractString(payloadJson, "unitId");
            if (string.IsNullOrEmpty(unitId)) return;
            if (!_units.TryGetValue(unitId, out var unit) || unit == null)
            {
                // L'unité n'existe pas (encore) en local : on tente un rattrapage.
                RebuildRegistry();
                if (!_units.TryGetValue(unitId, out unit) || unit == null)
                {
                    Debug.LogWarning($"[VTT] unit_move ignorée : unité '{unitId}' introuvable en local.");
                    return;
                }
            }
            int q = ExtractInt(payloadJson, "q", unit.CurrentCoords.Q);
            int r = ExtractInt(payloadJson, "r", unit.CurrentCoords.R);
            var grid = _grid != null ? _grid : FindAnyObjectByType<TacticalHexGrid>();
            if (grid == null)
            {
                Debug.LogWarning("[VTT] unit_move ignorée : aucune grille en scène.");
                return;
            }
            var coords = new HexCoordinates(q, r);
            var node = grid.GetNode(coords);
            if (node == null || !node.IsWalkable)
            {
                Debug.LogWarning($"[VTT] unit_move ignorée : case ({q},{r}) invalide.");
                return;
            }
            // 1 case = 1 avatar : on n'empile jamais via le réseau.
            if (node.IsOccupied && !unit.CurrentCoords.Equals(coords))
            {
                // Vérifie que c'est bien un autre avatar (et pas un drapeau fantôme).
                bool occupiedByOther = false;
                var all = FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
                for (int i = 0; i < all.Length; i++)
                {
                    var other = all[i];
                    if (other != null && other != unit && other.CurrentCoords.Equals(coords))
                    {
                        occupiedByOther = true;
                        break;
                    }
                }
                if (occupiedByOther)
                {
                    Debug.LogWarning($"[VTT] unit_move ignorée : case ({q},{r}) déjà occupée par un autre avatar.");
                    return;
                }
                node.IsOccupied = false; // Fantôme : on répare et on continue.
            }
            try
            {
                _applyingRemote = true;
                if (_teleportRemoteMoves)
                {
                    unit.TeleportTo(coords, grid);
                }
                else
                {
                    unit.TeleportTo(coords, grid); // fallback robuste pour le MVP
                }
                try { OnRemoteMoveApplied?.Invoke(unit, op.fromName ?? op.from, op.fromRole ?? ""); }
                catch (Exception e) { Debug.LogException(e); }
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private static string ExtractString(string json, string field)
        {
            string key = "\"" + field + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "";
            int colon = json.IndexOf(':', i);
            if (colon < 0) return "";
            int q1 = json.IndexOf('"', colon);
            if (q1 < 0) return "";
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return "";
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        private static int ExtractInt(string json, string field, int fallback)
        {
            string key = "\"" + field + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return fallback;
            int colon = json.IndexOf(':', i);
            if (colon < 0) return fallback;
            int k = colon + 1;
            while (k < json.Length && char.IsWhiteSpace(json[k])) k++;
            int start = k;
            if (k < json.Length && (json[k] == '-' || json[k] == '+')) k++;
            while (k < json.Length && char.IsDigit(json[k])) k++;
            if (int.TryParse(json.Substring(start, k - start), out int v)) return v;
            return fallback;
        }

        private static string ExtractText(string payloadJson)
        {
            return ExtractString(payloadJson, "text");
        }
    }
}
