using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi
{
    public partial class VTTRoomWindow : FloatingWindow<VTTRoomWindow>
    {
        // Onglet Table : connexion au hub (DrawConnectionSection).

        private void DrawConnectionSection()
        {
            Label("<b>1. Connexion au hub :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            bool connected = _net != null && _net.IsConnected;

            if (!connected)
            {
                GUILayout.BeginHorizontal();
                Label("Serveur :", GUILayout.Width(80));
                _serverUrl = GUILayout.TextField(_serverUrl ?? "");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                Label("Pseudo :", GUILayout.Width(80));
                _username = GUILayout.TextField(_username ?? "");
                GUILayout.EndHorizontal();
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
                if (GUILayout.Button("🔌 Se connecter", GUILayout.Height(30)))
                {
                    EnsureMultiStack();
                    _room.SetIdentity(_username.Trim());
                    _net.SetServerUrl(_serverUrl.Trim());
                    _net.Connect();
                    _statusMessage = $"Connexion à {_serverUrl.Trim()}…";
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUI.color = Color.green;
                Label($"🟢 Connecté ({_room.ClientId ?? "…"})");
                GUI.color = Color.white;
                if (GUILayout.Button("🔌 Déconnecter", GUILayout.Width(120)))
                {
                    _room.Disconnect();
                }
                GUILayout.EndHorizontal();
                if (_net != null)
                {
                    Label($"<color=gray><i>Hub : {_net.ServerUrl}</i></color>");
                }
            }
            GUILayout.EndVertical();
        }
    }
}
