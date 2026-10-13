using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi
{
    public partial class VTTRoomWindow : FloatingWindow<VTTRoomWindow>
    {
        // Onglet Table : liste des membres + actions GM.

        private void DrawMembersSection()
        {
            Label("<b>4. Membres :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            if (_room == null || !_room.InRoom || _room.Members.Count == 0)
            {
                Label("<color=gray><i>Aucun membre (rejoignez une room).</i></color>");
            }
            else
            {
                foreach (var m in _room.Members)
                {
                    if (m == null) continue;
                    bool isSelf = (m.clientId == _room.ClientId);
                    string badge = m.IsGM ? "👑 GM" : "🎲 Joueur";

                    bool isSpeaking = false;
                    if (isSelf)
                    {
                        isSpeaking = _voice != null && _voice.IsLocalSpeaking;
                    }
                    else if (_voice != null && _voice.RemotePlayers.TryGetValue(m.clientId, out var rp))
                    {
                        isSpeaking = rp.IsSpeaking;
                    }
                    string speakIcon = isSpeaking ? "<color=#00FF88>● [VOIX]</color> " : "<color=gray>○</color> ";

                    GUILayout.BeginHorizontal();
                    Label($"{speakIcon}{(isSelf ? "<b>" : "")}{m.username} [{badge}]{(isSelf ? " (vous)</b>" : "")}");
                    if (_room.IsGM && !isSelf)
                    {
                        if (GUILayout.Button("⬆ GM", GUILayout.Width(60)))
                        {
                            _room.TransferGM(m.clientId);
                        }
                        if (GUILayout.Button("⛔", GUILayout.Width(40)))
                        {
                            _room.Kick(m.clientId);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
        }
    }
}
