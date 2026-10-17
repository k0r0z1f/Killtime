using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi
{
    public partial class VTTRoomWindow : FloatingWindow<VTTRoomWindow>
    {
        // Barre vocale rapide persistante (DrawQuickVoiceBar).

        // =========================================================================
        // MODE VOIX VTT : CONTRÔLES, GRAPHES ET RÉDUCTION DE BRUITS
        // =========================================================================

        private void DrawQuickVoiceBar()
        {
            if (_voice == null) return;

            GUILayout.BeginHorizontal(GUI.skin.box);

            // Bouton Mute micro
            Color prevBg = GUI.backgroundColor;
            if (_voice.IsMuted) GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (GUILayout.Button(_voice.IsMuted ? "Micro Coupé" : "Micro Actif", GUILayout.Width(105), GUILayout.Height(22)))
            {
                _voice.SetMuted(!_voice.IsMuted);
            }
            GUI.backgroundColor = prevBg;

            // Bouton Sourdine (Deafen)
            if (_voice.IsDeafened) GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (GUILayout.Button(_voice.IsDeafened ? "Sourdine" : "Écoute", GUILayout.Width(85), GUILayout.Height(22)))
            {
                _voice.SetDeafened(!_voice.IsDeafened);
            }
            GUI.backgroundColor = prevBg;

            // Statut de transmission
            if (_voice.ActivationMode == Killtime.Multi.Voice.VoiceActivationMode.PushToTalk)
            {
                bool pttActive = Input.GetKey(_voice.PttKey);
                GUI.color = pttActive ? Color.green : Color.gray;
                GUILayout.Label($"[PTT: {_voice.PttKey}]", RichLabel(), GUILayout.Width(68));
                GUI.color = Color.white;
            }
            else if (_voice.ActivationMode == Killtime.Multi.Voice.VoiceActivationMode.VoiceActivity)
            {
                GUI.color = _voice.IsLocalSpeaking ? Color.green : Color.gray;
                GUILayout.Label(_voice.IsLocalSpeaking ? "● PARLE" : "○ Silencieux", RichLabel(), GUILayout.Width(75));
                GUI.color = Color.white;
            }

            // Mini VU-Mètre
            if (_voice.Dsp != null && _voiceVisualizer != null)
            {
                Rect vuRect = GUILayoutUtility.GetRect(60, 16, GUILayout.ExpandWidth(true));
                _voiceVisualizer.DrawVuMeter(vuRect, _voice.Dsp.CurrentRms, _voice.Dsp.CurrentPeak, _voice.Dsp.GateThreshold, _voice.Dsp.IsGateOpen);
            }

            GUILayout.EndHorizontal();
        }
    }
}
