using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi
{
    public partial class VTTRoomWindow : FloatingWindow<VTTRoomWindow>
    {
        // Onglet Table : creation/join + pilotage GM (DrawRoomSection).

        private void DrawRoomSection()
        {
            Label("<b>2. Room de table :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            if (_room == null || !_room.InRoom)
            {
                GUILayout.BeginHorizontal();
                Label("Nom table :", GUILayout.Width(95));
                _tableName = GUILayout.TextField(_tableName ?? "");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                Label("Joueurs max :", GUILayout.Width(95));
                _maxPlayersStr = GUILayout.TextField(_maxPlayersStr ?? "6", GUILayout.Width(45));
                _isPublic = GUILayout.Toggle(_isPublic, "📢 Lister publiquement");
                GUILayout.EndHorizontal();
                if (GUILayout.Button("✨ Créer une table (je deviens GM)", GUILayout.Height(30)))
                {
                    EnsureMultiStack();
                    if (!IsNetReady()) return;
                    int max = 6;
                    int.TryParse((_maxPlayersStr ?? "6").Trim(), out max);
                    max = Mathf.Clamp(max, 2, 12);
                    _maxPlayersStr = max.ToString();
                    _room.SetIdentity((_username ?? "Joueur").Trim());
                    _room.CreateRoom((_tableName ?? "").Trim(),
                        _isPublic ? VTTProtocol.VisibilityPublic : VTTProtocol.VisibilityPrivate, max);
                    _statusMessage = "Création de la table…";
                }
                GUILayout.BeginHorizontal();
                Label("Code room :", GUILayout.Width(95));
                _joinCode = GUILayout.TextField(_joinCode ?? "");
                string cleanCode = (_joinCode ?? "").Trim().ToUpperInvariant();
                GUI.enabled = !string.IsNullOrEmpty(cleanCode) && IsNetReady();
                if (GUILayout.Button("🚪 Rejoindre", GUILayout.Width(110)))
                {
                    _room.SetIdentity((_username ?? "Joueur").Trim());
                    _room.JoinRoom(cleanCode);
                    _statusMessage = $"Jonction à {cleanCode}…";
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (!IsNetReady())
                {
                    Label("<color=gray><i>Connectez-vous d'abord pour créer/rejoindre.</i></color>");
                }
            }
            else
            {
                string roleLabel = _room.IsGM ? "<color=orange><b>GM</b></color>" : "<b>Joueur</b>";
                GUILayout.BeginHorizontal();
                Label($"<b>{_room.TableName ?? _room.RoomCode}</b> [{_room.RoomCode}] — vous êtes {roleLabel}");
                if (GUILayout.Button("🚪 Quitter", GUILayout.Width(90)))
                {
                    _room.LeaveRoom();
                }
                GUILayout.EndHorizontal();
                if (_room.IsGM)
                {
                    bool pub = _room.IsPublic;
                    bool next = GUILayout.Toggle(pub, "📢 Lister publiquement (annuaire)");
                    if (next != pub)
                    {
                        _room.SetVisibility(next ? VTTProtocol.VisibilityPublic : VTTProtocol.VisibilityPrivate);
                    }
                }
                else if (!_room.IsPublic)
                {
                    Label("<color=gray><i>Table privée (sur code uniquement).</i></color>");
                }

                GUILayout.Space(4);
                string mapName = _sync != null ? _sync.ActiveMapName : null;
                Label(string.IsNullOrEmpty(mapName)
                    ? "<color=gray><i>🗺️ Aucune carte VTT chargée par le GM.</i></color>"
                    : $"<color=#8FE3FF>🗺️ Carte VTT active : <b>{mapName}</b></color>");
                if (_room.IsGM && GUILayout.Button("🗺️ Charger / changer la carte de la table", GUILayout.Height(26)))
                {
                    MapEditorDevWindow.Open();
                    _statusMessage = "Choisissez « Charger » dans l'onglet Cartes : la carte sera envoyée à la table.";
                }

                GUILayout.Space(6);
                var ai = FindAnyObjectByType<Killtime.Tactics.AI.TacticalAIController>();
                if (_room.IsGM)
                {
                    Label("<b>🎮 Contrôle du Combat & de l'IA (GM) :</b>");
                    if (ai != null)
                    {
                        GUILayout.BeginHorizontal();
                        bool aiOn = GUILayout.Toggle(ai.IsAIEnabled, "IA Active");
                        if (aiOn != ai.IsAIEnabled) ai.IsAIEnabled = aiOn;

                        bool isFull = ai.Mode == Killtime.Tactics.AI.CombatAIMode.FullAuto;
                        string modeTxt = isFull ? "Mode: FullAuto" : "Mode: Normal";
                        if (GUILayout.Button(modeTxt, GUILayout.Width(130)))
                        {
                            ai.Mode = isFull ? Killtime.Tactics.AI.CombatAIMode.Normal : Killtime.Tactics.AI.CombatAIMode.FullAuto;
                        }
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("🔄 Synchroniser Combat (PV/PA)", GUILayout.Height(24)))
                    {
                        _sync?.BroadcastFullCombatState();
                        _sync?.BroadcastRoomSettings();
                        _statusMessage = "État du combat et paramètres diffusés à tous les joueurs.";
                    }
                    GUILayout.EndHorizontal();
                }
                else
                {
                    Label("<color=gray><i>🎮 Combat VTT : L'IA et l'arène sont pilotées en temps réel par le GM.</i></color>");
                }
            }
            GUILayout.EndVertical();
        }
    }
}
