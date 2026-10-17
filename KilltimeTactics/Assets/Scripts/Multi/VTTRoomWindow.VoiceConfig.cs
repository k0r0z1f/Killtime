using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi
{
    public partial class VTTRoomWindow : FloatingWindow<VTTRoomWindow>
    {
        // Onglet Voix : DrawVoiceSection en entier (methode insecable).

        private void DrawVoiceSection()
        {
            if (_voice == null)
            {
                Label("<i>Gestionnaire de voix non initialisé.</i>");
                return;
            }

            Label("<b>🎙️ Configuration & Métrologie Voix :</b>");

            // 1. Périphérique Microphone
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            Label("<b>Microphone :</b>", GUILayout.Width(85));

            var devs = _voice.AvailableDevices;
            int devCount = devs.Count;
            if (devCount == 0)
            {
                Label("<color=red>⚠️ Aucun micro détecté</color>");
            }
            else
            {
                int curIdx = -1;
                for (int i = 0; i < devCount; i++)
                {
                    if (devs[i] == _voice.ActiveDeviceName) { curIdx = i; break; }
                }
                if (curIdx < 0) curIdx = 0;

                if (GUILayout.Button("◀", GUILayout.Width(22), GUILayout.Height(20)))
                {
                    int prev = (curIdx - 1 + devCount) % devCount;
                    _voice.SelectDevice(devs[prev]);
                }

                string devName = TruncateStr(_voice.ActiveDeviceName, 18);
                GUI.color = _voice.IsCapturing ? Color.cyan : Color.yellow;
                Label($"<b>{devName}</b>", GUILayout.Width(135));
                GUI.color = Color.white;

                if (GUILayout.Button("▶", GUILayout.Width(22), GUILayout.Height(20)))
                {
                    int next = (curIdx + 1) % devCount;
                    _voice.SelectDevice(devs[next]);
                }
            }

            if (GUILayout.Button("🔄 Scan", GUILayout.Width(58), GUILayout.Height(20)))
            {
                _voice.RefreshDevices();
                if (!_voice.IsCapturing) _voice.StartCapture();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string capStatus = _voice.IsCapturing 
                ? $"<color=#00FF88>● Active</color> ({_voice.CapturedSampleRate / 1000} kHz {(_voice.CapturedChannels > 1 ? "stéréo" : "mono")})" 
                : "<color=orange>○ Inactive</color>";
            Label($"Capture : {capStatus}");
            if (!_voice.IsCapturing && GUILayout.Button("Démarrer Capture", GUILayout.Width(130), GUILayout.Height(18)))
            {
                _voice.StartCapture();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 2. Choix du Codec Voix
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Codec Audio :</b>");
            int curCodecIdx = (int)_voice.CurrentCodecType;
            string[] codecLabels = new[] { "IMA 16k (Défaut)", "IMA 8k", "µ-law 16k", "µ-law 8k", "PCM 16k" };
            int newCodecIdx = GUILayout.Toolbar(curCodecIdx, codecLabels, GUILayout.Height(22));
            if (newCodecIdx != curCodecIdx)
            {
                _voice.SetCodec((Killtime.Multi.Voice.VoiceCodecType)newCodecIdx);
            }

            var activeCodec = Killtime.Multi.Voice.VoiceCodecRegistry.GetCodec(_voice.CurrentCodecType);
            Label($"<color=#00E5FF><b>{activeCodec.DisplayName}</b></color> — {activeCodec.Description}");
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 3. Mode de transmission (VAD / PTT / Continu)
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Mode de Transmission :</b>");
            int curModeIdx = (int)_voice.ActivationMode;
            int newModeIdx = GUILayout.Toolbar(curModeIdx, new[] { "🎙️ Détection (VAD)", "⚡ Push-to-Talk (PTT)", "〰️ Continu" }, GUILayout.Height(22));
            if (newModeIdx != curModeIdx)
            {
                _voice.SetActivationMode((Killtime.Multi.Voice.VoiceActivationMode)newModeIdx);
            }

            if (_voice.ActivationMode == Killtime.Multi.Voice.VoiceActivationMode.PushToTalk)
            {
                GUILayout.BeginHorizontal();
                Label("Touche PTT :", GUILayout.Width(85));
                KeyCode[] pttOptions = new[] { KeyCode.V, KeyCode.Space, KeyCode.T, KeyCode.LeftAlt, KeyCode.C };
                string[] pttNames = new[] { "V", "Espace", "T", "Alt", "C" };
                int currentPttIdx = Array.IndexOf(pttOptions, _voice.PttKey);
                if (currentPttIdx < 0) currentPttIdx = 0;
                int newPttIdx = GUILayout.Toolbar(currentPttIdx, pttNames, GUILayout.Height(20));
                if (newPttIdx != currentPttIdx)
                {
                    _voice.SetPttKey(pttOptions[newPttIdx]);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 4. Contrôles de Gain & Volumes
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Niveaux Entrée & Sortie :</b>");

            GUILayout.BeginHorizontal();
            Label("Gain Micro :", GUILayout.Width(110));
            float curGain = _voice.Dsp != null ? _voice.Dsp.InputGain : 1.0f;
            float newGain = GUILayout.HorizontalSlider(curGain, 0.2f, 5.0f);
            if (!Mathf.Approximately(curGain, newGain)) _voice.SetInputGain(newGain);
            Label($"{newGain:0.0}x", GUILayout.Width(40));
            GUILayout.EndHorizontal();

            // Auto-Gain Dynamique (AGC)
            bool prevAgc = _voice.AutoGainEnabled;
            GUILayout.BeginHorizontal();
            bool newAgc = GUILayout.Toggle(prevAgc, " <b>Auto-Gain (AGC)</b> (Amplification auto micro faible)", RichToggle(), GUILayout.ExpandWidth(true));
            GUILayout.Label(newAgc ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ MANUEL</color>", RichLabel(), GUILayout.Width(75));
            GUILayout.EndHorizontal();
            if (newAgc != prevAgc)
            {
                _voice.SetAutoGainEnabled(newAgc);
            }

            GUILayout.BeginHorizontal();
            Label("Volume Voix :", GUILayout.Width(110));
            float curOutVol = _voice.MasterOutputVolume;
            float newOutVol = GUILayout.HorizontalSlider(curOutVol, 0f, 2.0f);
            if (!Mathf.Approximately(curOutVol, newOutVol)) _voice.SetMasterOutputVolume(newOutVol);
            Label($"{Mathf.RoundToInt(newOutVol * 100)}%", GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool testActive = _voice.IsLoopbackTestActive;
            Color prevBg = GUI.backgroundColor;
            if (testActive) GUI.backgroundColor = new Color(0f, 0.85f, 1f);
            if (GUILayout.Button(testActive ? "■ Arrêter le Test Micro" : "▶ Tester mon micro (Loopback)", GUILayout.Height(22)))
            {
                _voice.SetLoopbackTest(!testActive);
            }
            GUI.backgroundColor = prevBg;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 4.b Modulateur Vocal Avancé (Voice Changer)
            GUILayout.BeginVertical(GUI.skin.box);
            bool prevVc = _voice.VoiceChangerEnabled;
            GUILayout.BeginHorizontal();
            bool newVc = GUILayout.Toggle(prevVc, " 🎭 <b>Modulateur Vocal (Voice Changer)</b>", RichToggle(), GUILayout.ExpandWidth(true));
            GUILayout.Label(newVc ? "<color=#00E5FF>● MODULÉ</color>" : "<color=gray>○ DIRECT</color>", RichLabel(), GUILayout.Width(75));
            GUILayout.EndHorizontal();

            if (newVc != prevVc)
            {
                _voice.SetVoiceChangerEnabled(newVc);
            }

            if (_voice.VoiceChanger != null)
            {
                GUILayout.Space(2);
                Label("<b>Profils & Personnalités Vocales :</b>");

                int curPresetIdx = (int)_voice.CurrentVoiceChangerPreset;
                string[] presetLabels = new[]
                {
                    "Naturel", "Opérateur Lourd", "Cyborg", "Infiltrateur", "Nytharite", "Radio",
                    "♀ Valkyrie", "♀ Mina", "♀ Prêtresse", "♀ V.I.C.I. (Android)", "Manuel"
                };
                int newPresetIdx = GUILayout.SelectionGrid(curPresetIdx, presetLabels, 3, GUILayout.Height(92));
                if (newPresetIdx != curPresetIdx)
                {
                    _voice.SetVoiceChangerPreset((VoiceChangerPreset)newPresetIdx);
                }

                if (_voice.VoiceChangerEnabled)
                {
                    GUILayout.Space(4);

                    // Transposition / Pitch
                    GUILayout.BeginHorizontal();
                    Label("Hauteur / Pitch :", GUILayout.Width(115));
                    float curPitch = _voice.VoiceChanger.PitchSemitones;
                    float newPitch = GUILayout.HorizontalSlider(curPitch, -12f, 12f);
                    if (!Mathf.Approximately(curPitch, newPitch))
                    {
                        _voice.SetVoiceChangerPitch(newPitch);
                        _voice.SetVoiceChangerPreset(VoiceChangerPreset.Custom);
                    }
                    string sign = newPitch >= 0 ? "+" : "";
                    Label($"{sign}{newPitch:0.0} st", GUILayout.Width(55));
                    GUILayout.EndHorizontal();

                    // Timbre / Formant
                    GUILayout.BeginHorizontal();
                    Label("Timbre / Larynx :", GUILayout.Width(115));
                    float curFormant = _voice.VoiceChanger.FormantShift;
                    float newFormant = GUILayout.HorizontalSlider(curFormant, -1f, 1f);
                    if (!Mathf.Approximately(curFormant, newFormant))
                    {
                        _voice.SetVoiceChangerFormant(newFormant);
                        _voice.SetVoiceChangerPreset(VoiceChangerPreset.Custom);
                    }
                    string formantTag = newFormant < -0.1f ? "Grave" : (newFormant > 0.1f ? "Clair" : "Neutre");
                    Label($"{formantTag} ({newFormant:0.0})", GUILayout.Width(55));
                    GUILayout.EndHorizontal();

                    // Saturation / Chaleur
                    GUILayout.BeginHorizontal();
                    Label("Grain / Chaleur :", GUILayout.Width(115));
                    float curDrive = _voice.VoiceChanger.HarmonicDrive;
                    float newDrive = GUILayout.HorizontalSlider(curDrive, 0f, 1f);
                    if (!Mathf.Approximately(curDrive, newDrive))
                    {
                        _voice.SetVoiceChangerDrive(newDrive);
                        _voice.SetVoiceChangerPreset(VoiceChangerPreset.Custom);
                    }
                    Label($"{Mathf.RoundToInt(newDrive * 100)}%", GUILayout.Width(55));
                    GUILayout.EndHorizontal();

                    // Robotisation / Ring Mod
                    GUILayout.BeginHorizontal();
                    Label("Robotisation :", GUILayout.Width(115));
                    float curRobo = _voice.VoiceChanger.RoboticModulation;
                    float newRobo = GUILayout.HorizontalSlider(curRobo, 0f, 1f);
                    if (!Mathf.Approximately(curRobo, newRobo))
                    {
                        _voice.SetVoiceChangerRobotic(newRobo);
                        _voice.SetVoiceChangerPreset(VoiceChangerPreset.Custom);
                    }
                    Label($"{Mathf.RoundToInt(newRobo * 100)}%", GUILayout.Width(55));
                    GUILayout.EndHorizontal();

                    // Mix Wet/Dry
                    GUILayout.BeginHorizontal();
                    Label("Mixage Effet :", GUILayout.Width(115));
                    float curMix = _voice.VoiceChanger.WetDryMix;
                    float newMix = GUILayout.HorizontalSlider(curMix, 0f, 1f);
                    if (!Mathf.Approximately(curMix, newMix))
                    {
                        _voice.SetVoiceChangerMix(newMix);
                    }
                    Label($"{Mathf.RoundToInt(newMix * 100)}%", GUILayout.Width(55));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 5. Réduction de Bruits & Traitement DSP
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Réduction de Bruits de Fond (DSP) :</b>");

            if (_voice.Dsp != null)
            {
                bool prevNg = _voice.Dsp.NoiseGateEnabled;
                GUILayout.BeginHorizontal();
                bool newNg = GUILayout.Toggle(prevNg, " <b>Noise Gate</b> (Suppression des silences & clics)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newNg ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newNg != prevNg)
                {
                    _voice.SetNoiseGateEnabled(newNg);
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.VoiceNoiseGateEnabled = newNg;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }

                if (_voice.Dsp.NoiseGateEnabled)
                {
                    GUILayout.BeginHorizontal();
                    Label("Seuil du Gate :", GUILayout.Width(115));
                    float curThresh = _voice.Dsp.GateThreshold;
                    float newThresh = GUILayout.HorizontalSlider(curThresh, 0.005f, 0.12f);
                    if (!Mathf.Approximately(curThresh, newThresh)) _voice.SetGateThreshold(newThresh);
                    float db = 20f * Mathf.Log10(newThresh);
                    Label($"{db:0.0} dB", GUILayout.Width(55));
                    GUILayout.EndHorizontal();
                }

                bool prevHp = _voice.Dsp.HighPassFilterEnabled;
                GUILayout.BeginHorizontal();
                bool newHp = GUILayout.Toggle(prevHp, " <b>Filtre Passe-Haut 80Hz</b> (Anti-ronflement & vibrations)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newHp ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newHp != prevHp)
                {
                    _voice.SetHighPassFilterEnabled(newHp);
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.VoiceHighPassFilter = newHp;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }

                bool prevNr = _voice.Dsp.NoiseReductionEnabled;
                GUILayout.BeginHorizontal();
                bool newNr = GUILayout.Toggle(prevNr, " <b>Réducteur de souffle adaptatif</b> (Bruit ambiant)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newNr ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newNr != prevNr)
                {
                    _voice.SetNoiseReductionEnabled(newNr);
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.VoiceNoiseReductionEnabled = newNr;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }

                bool prevLim = _voice.OutputLimiterEnabled;
                GUILayout.BeginHorizontal();
                bool newLim = GUILayout.Toggle(prevLim, " <b>Plafond Anti-Cris & Rires</b> (Protection auditive)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newLim ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newLim != prevLim)
                {
                    _voice.SetOutputLimiterEnabled(newLim);
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.VoiceOutputLimiterEnabled = newLim;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }

                if (_voice.OutputLimiterEnabled)
                {
                    GUILayout.BeginHorizontal();
                    Label("Seuil Plafond :", GUILayout.Width(110));
                    float curCeil = _voice.OutputCeiling;
                    float newCeil = GUILayout.HorizontalSlider(curCeil, 0.15f, 0.95f);
                    if (!Mathf.Approximately(curCeil, newCeil))
                    {
                        _voice.SetOutputCeiling(newCeil);
                        if (Killtime.UI.DevUIPreferences.Current != null)
                        {
                            Killtime.UI.DevUIPreferences.Current.VoiceOutputCeiling = newCeil;
                            Killtime.UI.DevUIPreferences.MarkDirty();
                        }
                    }
                    float ceilDb = 20f * Mathf.Log10(newCeil);
                    Label($"{ceilDb:0.0} dB", GUILayout.Width(55));
                    GUILayout.EndHorizontal();
                }

                // Gate Deleter (Élimination absolue des bruits hors parole)
                bool prevDel = _voice.GateDeleterEnabled;
                GUILayout.BeginHorizontal();
                bool newDel = GUILayout.Toggle(prevDel, " <b>Silence Absolu (Gate Deleter)</b> (Zéro fuite de bruit)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newDel ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newDel != prevDel)
                {
                    _voice.SetGateDeleterEnabled(newDel);
                }

                // Filtre Anti-Clavier & Détection Transitoires
                bool prevKb = _voice.AntiKeyboardFilterEnabled;
                GUILayout.BeginHorizontal();
                bool newKb = GUILayout.Toggle(prevKb, " <b>Filtre Anti-Clavier</b> (Suppression des frappes & clics souris)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newKb ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newKb != prevKb)
                {
                    _voice.SetAntiKeyboardFilterEnabled(newKb);
                }

                // Effet Comms Radio Militaire (Filtre passe-bande 300Hz - 3.4kHz)
                bool prevRad = _voice.RadioCommsEffectEnabled;
                GUILayout.BeginHorizontal();
                bool newRad = GUILayout.Toggle(prevRad, " <b>Filtre Radio Tactique</b> (Intercom casque 300Hz - 3.4kHz)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newRad ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newRad != prevRad)
                {
                    _voice.SetRadioCommsEffectEnabled(newRad);
                }

                // Clarté & Présence Cristalline (Supprime le son étouffé / téléphone)
                bool prevCla = _voice.VoiceClarityEnabled;
                GUILayout.BeginHorizontal();
                bool newCla = GUILayout.Toggle(prevCla, " <b>Clarté Vocale HD</b> (Présence studio & brillance)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newCla ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newCla != prevCla)
                {
                    _voice.SetVoiceClarityEnabled(newCla);
                }

                if (_voice.VoiceClarityEnabled && !_voice.RadioCommsEffectEnabled)
                {
                    GUILayout.BeginHorizontal();
                    Label("Brillance / Air :", GUILayout.Width(110));
                    float curCla = _voice.ClarityBoostAmount;
                    float newClaVal = GUILayout.HorizontalSlider(curCla, 0f, 1.2f);
                    if (!Mathf.Approximately(curCla, newClaVal))
                    {
                        _voice.SetClarityBoostAmount(newClaVal);
                    }
                    Label($"{Mathf.RoundToInt(newClaVal * 100)}%", GUILayout.Width(55));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 6. Graphes & Visualisation Temps Réel
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Graphes & Métrologie Temps Réel :</b>");

            if (_voice.Dsp != null && _voiceVisualizer != null)
            {
                float displayPeak = (_voice.OutputLimiterEnabled && _voice.CurrentOutputPeak > 0f)
                    ? _voice.CurrentOutputPeak
                    : _voice.Dsp.CurrentPeak;

                bool isClamped = _voice.OutputLimiterEnabled && displayPeak >= (_voice.OutputCeiling - 0.02f);
                string peakStr = isClamped
                    ? $"<color=#00E5FF><b>{Mathf.RoundToInt(displayPeak * 100)}% [PLAFONNÉ]</b></color>"
                    : $"{Mathf.RoundToInt(displayPeak * 100)}%";

                Label($"RMS: {_voice.Dsp.CurrentRmsDb:0.0} dB | Crête: {peakStr} | Porte: {(_voice.Dsp.IsGateOpen ? "<color=#00FF88>OUVERTE</color>" : "<color=#888888>FERMÉE</color>")}");
                Rect vuRect = GUILayoutUtility.GetRect(280, 20, GUILayout.ExpandWidth(true));
                _voiceVisualizer.DrawVuMeter(vuRect, _voice.Dsp.CurrentRms, displayPeak, _voice.Dsp.GateThreshold, _voice.Dsp.IsGateOpen);

                GUILayout.Space(4);

                Label("Oscilloscope (Cyan: Signal propre / Gris: Brut) :");
                _voiceVisualizer.UpdateScopeTexture(_voice.Dsp);
                if (_voiceVisualizer.ScopeTexture != null)
                {
                    Rect scopeRect = GUILayoutUtility.GetRect(280, 50, GUILayout.ExpandWidth(true));
                    GUI.DrawTexture(scopeRect, _voiceVisualizer.ScopeTexture);
                }

                GUILayout.Space(4);

                Label("Spectre Fréquentiel (Basses, Bas-Médiums, Haut-Médiums, Aigus) :");
                Rect specRect = GUILayoutUtility.GetRect(280, 32, GUILayout.ExpandWidth(true));
                _voiceVisualizer.DrawSpectrumBars(specRect, _voice.Dsp);
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 7. Joueurs Distants & Volumes Individuels
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Volumes des Joueurs Connectés :</b>");
            if (_room == null || !_room.InRoom || _room.Members.Count <= 1)
            {
                Label("<color=gray><i>Aucun autre joueur dans la room.</i></color>");
            }
            else
            {
                foreach (var m in _room.Members)
                {
                    if (m == null || m.clientId == _room.ClientId) continue;

                    var player = _voice.GetOrCreateRemotePlayer(m.clientId, m.username);
                    bool isSpeaking = player.IsSpeaking;
                    string speakIcon = isSpeaking ? "<color=#00FF88>●</color>" : "<color=gray>○</color>";

                    GUILayout.BeginHorizontal();
                    Label($"{speakIcon} <b>{m.username}</b>", GUILayout.Width(100));

                    player.PlayerVolume = GUILayout.HorizontalSlider(player.PlayerVolume, 0f, 3.0f);
                    Label($"{Mathf.RoundToInt(player.PlayerVolume * 100)}%", GUILayout.Width(40));

                    Color prevPlayerBg = GUI.backgroundColor;
                    if (player.IsMuted) GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button(player.IsMuted ? "MUET" : "ACTIF", GUILayout.Width(50), GUILayout.Height(18)))
                    {
                        player.IsMuted = !player.IsMuted;
                    }
                    GUI.backgroundColor = prevPlayerBg;

                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
        }
    }
}
