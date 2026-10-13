using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi
{
    public partial class VTTRoomWindow : FloatingWindow<VTTRoomWindow>
    {
        // Onglet Annuaire : browser des tables publiques + refresh.

        private void DrawDirectorySection()
        {
            Label($"<b>3. Tables publiques (build {Application.version}) :</b>");
            Label($"<color=gray><i>Annuaire : {EffectiveServerUrl()}</i></color>");
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUI.enabled = !_dirBusy;
            if (GUILayout.Button(_dirBusy ? "⏳ Actualisation…" : "🔄 Actualiser", GUILayout.Width(130)))
            {
                RefreshDirectory();
            }
            GUI.enabled = true;
            if (_dirError != null)
                Label($"<color=red>⚠️ {_dirError}</color>");
            else
                Label($"<color=gray><i>{_publicRooms.Count} table(s) compatible(s).</i></color>");
            GUILayout.EndHorizontal();
            _dirScroll = GUILayout.BeginScrollView(_dirScroll, GUILayout.Height(110));
            if (_publicRooms.Count == 0)
            {
                Label("<color=gray><i>Aucune table publique pour ce build. Créez-en une !</i></color>");
            }
            else
            {
                foreach (var r in _publicRooms)
                {
                    if (r == null) continue;
                    bool full = r.players >= r.maxPlayers;
                    GUILayout.BeginHorizontal();
                    Label($"<b>{r.tableName}</b> [{r.code}]  {r.players}/{r.maxPlayers}  GM: {r.gmName}");
                    GUI.enabled = !full && IsNetReadySoft();
                    if (GUILayout.Button(full ? "Complet" : "Rejoindre", GUILayout.Width(85)))
                    {
                        EnsureMultiStack();
                        _room.SetIdentity(_username.Trim());
                        _room.JoinRoom(r.code);
                        _joinCode = r.code;
                        _statusMessage = $"Jonction à {r.code}…";
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private bool IsNetReadySoft()
        {
            return _net != null && _net.IsConnected && _room != null;
        }

        /// <summary>
        /// URL source de vérité : la connexion active si elle existe, sinon le champ.
        /// Ça garantit que l'annuaire interroge toujours le hub auquel on est connecté.
        /// </summary>
        private string EffectiveServerUrl()
        {
            if (_net != null && _net.IsConnected && !string.IsNullOrEmpty(_net.ServerUrl))
                return _net.ServerUrl;
            return _serverUrl;
        }

        private void RefreshDirectory()
        {
            EnsureMultiStack();
            if (_directory == null || _dirBusy) return;
            string baseUrl = EffectiveServerUrl().Trim();
            if (string.IsNullOrEmpty(baseUrl))
            {
                _dirError = "renseignez l'URL du hub puis connectez-vous";
                return;
            }
            _dirBusy = true;
            _directory.FetchPublicRooms(baseUrl, Application.version, (rooms, error) =>
            {
                _dirBusy = false;
                if (error != null)
                {
                    _dirError = error;
                }
                else
                {
                    _dirError = null;
                    _publicRooms.Clear();
                    if (rooms != null) _publicRooms.AddRange(rooms);
                }
                _nextDirRefresh = Time.realtimeSinceStartup + DirRefreshInterval;
            });
        }
    }
}
