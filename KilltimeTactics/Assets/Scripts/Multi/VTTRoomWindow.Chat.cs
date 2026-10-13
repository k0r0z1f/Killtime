using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi
{
    public partial class VTTRoomWindow : FloatingWindow<VTTRoomWindow>
    {
        // Onglet Chat : log, saisie, IsNetReady, AddChatLine.

        private void DrawChatSection()
        {
            GUILayout.BeginHorizontal();
            Label("<b>5. Chat de table :</b>");
            bool newLock = GUILayout.Toggle(_chatScrollLock, "🔒 Scroll Lock", GUILayout.Width(110));
            if (newLock != _chatScrollLock)
            {
                _chatScrollLock = newLock;
                if (!_chatScrollLock) _chatScrollToBottomPending = true;
                else _chatScrollToBottomPending = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginVertical(GUI.skin.box);
            if (_chatScrollLock)
            {
                _chatScrollToBottomPending = false;
            }
            else if (_chatScrollToBottomPending)
            {
                _chatScroll.y = float.MaxValue;
                _chatScrollToBottomPending = false;
            }
            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(120));
            if (_chatLog.Count == 0)
            {
                Label("<color=gray><i>Aucun message pour l'instant.</i></color>");
            }
            else
            {
                foreach (var line in _chatLog)
                {
                    Label(line);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            _chatInput = GUILayout.TextField(_chatInput);
            GUI.enabled = _room != null && _room.InRoom && !string.IsNullOrWhiteSpace(_chatInput);
            if (GUILayout.Button("Envoyer", GUILayout.Width(80)))
            {
                string text = _chatInput.Trim();
                _chatInput = "";
                _room.SendChat(text);
                AddChatLine($"<b>{_username.Trim()} (vous) :</b> {text}");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private bool IsNetReady()
        {
            EnsureMultiStack();
            return _net != null && _net.IsConnected;
        }

        private void AddChatLine(string line)
        {
            _chatLog.Add(line);
            if (_chatLog.Count > 50) _chatLog.RemoveAt(0);
            // Verrou actif => la lecture reste figée ; sinon retour en bas.
            if (!_chatScrollLock)
            {
                _chatScrollToBottomPending = true;
            }
        }
    }
}
