using System;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Audio
{
    /// <summary>
    /// Panneau de mixage audio tactique unifié (Master, Musique, Ambiance, SFX, UI).
    /// Hérite de FloatingWindow pour partager le chrome, le style et le cycle de vie du HUD.
    /// Raccourcis : F9 pour ouvrir/fermer, M pour couper/rétablir le son.
    /// </summary>
    public class KilltimeAudioSettingsUI : FloatingWindow<KilltimeAudioSettingsUI>
    {
        protected override int WindowId => 892;
        protected override string Title => "Mixage // Killtime";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(24f, 140f, 320f, 380f);
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F9 };
        private static readonly Vector2 _minSize = new Vector2(280f, 260f);

        private Vector2 _scrollPos;
        private static GUIStyle _richLabel;

        protected override void OnAwake()
        {
            DontDestroyOnLoad(gameObject);
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            if (Input.GetKeyDown(KeyCode.M))
            {
                var mgr = KilltimeAudioManager.Instance;
                if (mgr != null)
                {
                    mgr.SetMuted(!mgr.IsMuted);
                }
            }
        }

        protected override void DrawClosedState()
        {
            var mgr = KilltimeAudioManager.Instance;
            Rect pill = new Rect(16f, Screen.height - 52f, 106f, 22f);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.02f, 0.035f, 0.05f, 0.65f);

            string label = (mgr != null && mgr.IsMuted) ? "🔇 SON (M)" : "🔊 SON (F9)";
            if (GUI.Button(pill, label))
            {
                OpenInstance();
            }

            GUI.backgroundColor = prevBg;
        }

        protected override void DrawContent()
        {
            var mgr = KilltimeAudioManager.Instance;
            if (mgr == null)
            {
                GUILayout.Label("<i>KilltimeAudioManager introuvable.</i>", RichLabel());
                return;
            }

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            // 1. Statut de la musique adaptative (Hauteur Verrouillée Immuable)
            GUILayout.BeginVertical(GUI.skin.box);
            string moodColor = mgr.CurrentMood switch
            {
                MusicMood.Combat => "#FF3B5C",
                MusicMood.CombatBoss => "#FF0077",
                MusicMood.Tension => "#FFAA00",
                MusicMood.Victory => "#00FF88",
                MusicMood.Defeat => "#8888AA",
                _ => "#00E5FF"
            };

            int trackCount = ProceduralAudioFactory.GetTrackCount(mgr.CurrentMood);
            string trackTag = trackCount > 1 
                ? $" <color=#00E5FF>[{mgr.CurrentTrackIndex + 1}/{trackCount}]</color>" 
                : "";

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Humeur :</b> <color={moodColor}>{mgr.CurrentMood}</color> [{mgr.CurrentIntensityLevel}]{trackTag}", RichLabel(), GUILayout.ExpandWidth(true));
            GUILayout.Label($"<b>Débit :</b> {Mathf.RoundToInt(mgr.CurrentIntensity * 100)}%", RichLabel(), GUILayout.Width(68));
            GUILayout.EndHorizontal();

            // Bouton persistant : toujours présent pour fixer le layout, grisé si inactif
            bool canRotate = (mgr.CurrentMood == MusicMood.Combat && trackCount > 1);
            GUI.enabled = canRotate;
            if (GUILayout.Button(canRotate ? "⏭️ Piste Suivante (Rotation)" : "⏭️ Piste Unique (Fixe)", GUILayout.Height(20)))
            {
                mgr.NextCombatTrack();
            }
            GUI.enabled = true;

            if (mgr.CurrentMood == MusicMood.Combat && trackCount > 1)
            {
                GUILayout.BeginHorizontal();
                for (int tIdx = 0; tIdx < trackCount; tIdx++)
                {
                    bool isCurrent = (mgr.CurrentTrackIndex == tIdx);
                    Color prevBtn = GUI.backgroundColor;
                    if (isCurrent) GUI.backgroundColor = new Color(0f, 0.85f, 1f);
                    if (GUILayout.Button($"Piste {tIdx + 1}", GUILayout.Height(18)))
                    {
                        mgr.PlayMusic(MusicMood.Combat, tIdx, mgr.CurrentIntensity, forceRestart: true);
                    }
                    GUI.backgroundColor = prevBtn;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 1.b Module Caméléon (Écoute & Imitation Système)
            // Pas de AddComponent pendant l'OnGUI : la création à la volée jouait
            // son Start (AudioSettings.Reset) et coupait la musique à la 1re
            // ouverture. Le composant est pré-créé par l'AutoBootstrap.
            var chameleon = Experimental.SystemAudioChameleon.Instance
                ?? FindAnyObjectByType<Experimental.SystemAudioChameleon>();
            if (chameleon == null)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("<color=gray><i>🦎 Caméléon indisponible (bootstrap audio incomplet).</i></color>", RichLabel());
                GUILayout.EndVertical();
            }
            else
            {

            GUILayout.BeginVertical(GUI.skin.box);
            bool prevActive = chameleon.IsActive;
            bool newActive = GUILayout.Toggle(prevActive, "🦎 <b>Mode Caméléon Système</b>", RichLabel());
            if (newActive != prevActive)
            {
                chameleon.IsActive = newActive;
            }

            if (chameleon.IsActive)
            {
                var devices = Microphone.devices;
                int devCount = devices != null ? devices.Length : 0;

                if (devCount == 0)
                {
                    GUILayout.Label("<color=#FF4444>⚠️ Aucun périphérique d'entrée détecté sous Linux.</color>", RichLabel());
                }
                else
                {
                    // Détection et liaison directe d'application (PipeWire Linux)
                    if (Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor)
                    {
                        GUILayout.BeginVertical(GUI.skin.box);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("<b>Applications audio détectées :</b>", RichLabel());
                        if (GUILayout.Button("🔄 Scan", GUILayout.Width(60)))
                        {
                            chameleon.RefreshLinuxApps();
                        }
                        GUILayout.EndHorizontal();

                        if (chameleon.DetectedMediaApps.Count == 0)
                        {
                            GUILayout.Label("<color=gray><i>Aucun lecteur externe actif (lancez Chrome, Spotify, etc.)</i></color>", RichLabel());
                        }
                        else
                        {
                            for (int i = 0; i < chameleon.DetectedMediaApps.Count; i++)
                            {
                                var app = chameleon.DetectedMediaApps[i];
                                bool isLinked = string.Equals(chameleon.LinkedLinuxApp, app.DisplayName, StringComparison.OrdinalIgnoreCase);

                                GUILayout.BeginHorizontal();
                                GUI.color = isLinked ? Color.cyan : Color.white;
                                bool toggled = GUILayout.Toggle(isLinked, $" <b>{app.DisplayName}</b> ({app.Ports.Count} ch)", RichLabel(), GUILayout.Height(20));
                                GUI.color = Color.white;

                                if (toggled != isLinked)
                                {
                                    if (toggled)
                                    {
                                        chameleon.LinkLinuxApp(app);
                                    }
                                    else
                                    {
                                        chameleon.UnlinkLinuxApp(app);
                                    }
                                }
                                GUILayout.EndHorizontal();
                            }
                        }

                        if (!string.IsNullOrEmpty(chameleon.LinkStatusMessage))
                        {
                            GUILayout.Label(chameleon.LinkStatusMessage, RichLabel());
                        }
                        GUILayout.EndVertical();
                    }

                    // Sélecteur de périphérique (Largeurs calibrées anti-wrap)
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Périphérique :", RichLabel(), GUILayout.Width(88));

                    int currentIdx = Array.IndexOf(devices, chameleon.ActiveDeviceName);
                    if (currentIdx < 0) currentIdx = 0;

                    if (GUILayout.Button("◀", GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        int prev = (currentIdx - 1 + devCount) % devCount;
                        chameleon.SelectDevice(devices[prev]);
                    }

                    string devDisplayName = !string.IsNullOrEmpty(chameleon.ActiveDeviceName) 
                        ? TruncateDeviceName(chameleon.ActiveDeviceName, 16) 
                        : "Défaut";
                    GUI.color = chameleon.IsRecordingActive ? Color.cyan : Color.yellow;
                    GUILayout.Label($"<b>{devDisplayName}</b>", RichLabel(), GUILayout.Width(118));
                    GUI.color = Color.white;

                    if (GUILayout.Button("▶", GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        int next = (currentIdx + 1) % devCount;
                        chameleon.SelectDevice(devices[next]);
                    }

                    if (GUILayout.Button("↺", GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        chameleon.StartCapture();
                    }
                    GUILayout.EndHorizontal();

                    // Réglage du Gain Préampli avec Auto-Calibration
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Préampli :", RichLabel(), GUILayout.Width(70));

                    bool autoActive = chameleon.AutoCalibrateClarity;
                    GUI.backgroundColor = autoActive ? new Color(0f, 0.85f, 1f) : Color.white;
                    if (GUILayout.Button(autoActive ? "AUTO" : "MANU", GUILayout.Width(46), GUILayout.Height(18)))
                    {
                        chameleon.AutoCalibrateClarity = !chameleon.AutoCalibrateClarity;
                    }
                    GUI.backgroundColor = Color.white;

                    GUI.enabled = !autoActive;
                    chameleon.InputPreGain = GUILayout.HorizontalSlider(chameleon.InputPreGain, 0.5f, 6.0f);
                    GUI.enabled = true;

                    GUILayout.Label($"{chameleon.InputPreGain:0.0}x", RichLabel(), GUILayout.Width(32));
                    GUILayout.EndHorizontal();

                    // Oscilloscope
                    DrawChameleonScope(chameleon);

                    // Télémétrie spectrale (Largeurs étendues sans retour à la ligne)
                    DrawChameleonVUMeter("Sub-Basses [20-180Hz]", chameleon.BassEnergyRMS, new Color(1.0f, 0.45f, 0.15f));
                    DrawChameleonVUMeter("Médiums [180-2500Hz]", chameleon.MidEnergyRMS, new Color(0.2f, 0.95f, 0.35f));
                    DrawChameleonVUMeter("Aigus & Harmoniques", chameleon.HighEnergyRMS, new Color(0.0f, 0.85f, 1.0f));

                    GUILayout.Space(2);

                    if (chameleon.IsExternalMusicDetected)
                    {
                        string purityCol = chameleon.PitchConfidence > 0.65f ? "#00FF88" : (chameleon.PitchConfidence > 0.4f ? "#FFD200" : "#FF5555");
                        string beatIcon = chameleon.BeatPulse > 0.15f ? "<color=#FF0055>● BEAT</color>" : "<color=#445566>○ beat</color>";

                        GUILayout.Label($"🎶 Fondamentale : <color=#00E5FF><b>{chameleon.DetectedNoteName}</b></color> ({chameleon.DetectedFrequencyHz:0.0} Hz)", RichLabel());
                        GUILayout.Label($"🎯 Clarté : <color={purityCol}><b>{Mathf.RoundToInt(chameleon.PitchConfidence * 100)}%</b></color> (Pureté : {Mathf.RoundToInt(chameleon.HarmonicPurity * 100)}%) | Cadence : <b>{chameleon.DetectedBPM:0} BPM</b> {beatIcon}", RichLabel());
                    }
                    else
                    {
                        GUILayout.Label("<color=gray><i>Flux silencieux ou inharmonique. Jouez une source externe.</i></color>", RichLabel());
                    }

                    GUILayout.Space(4);

                    // Émulateur Basse Dédié (Bass Synth)
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.BeginHorizontal();
                    bool bassActive = GUILayout.Toggle(chameleon.EmulateBass, "🎸 <b>Émulateur Basse (Bass Synth)</b>", RichLabel());
                    if (bassActive != chameleon.EmulateBass)
                    {
                        chameleon.EmulateBass = bassActive;
                    }
                    GUILayout.FlexibleSpace();

                    if (chameleon.BassPitchConfidence >= 0.26f)
                    {
                        GUILayout.Label($"<color=#00FF88><b>{chameleon.DetectedBassFrequencyHz:0.0} Hz</b> ({Mathf.RoundToInt(chameleon.BassPitchConfidence * 100)}%)</color>", RichLabel());
                    }
                    else if (chameleon.IsHarmonicNoteStable)
                    {
                        GUILayout.Label($"<color=#00E5FF><b>{chameleon.CurrentBassPitchHz:0.0} Hz</b> (Synth)</color>", RichLabel());
                    }
                    else
                    {
                        GUILayout.Label("<color=#8899AA>---</color>", RichLabel());
                    }
                    GUILayout.EndHorizontal();  

                    if (chameleon.EmulateBass)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Style Basse :", GUILayout.Width(80));
                        chameleon.BassStyle = (Experimental.ChameleonBassStyle)GUILayout.Toolbar((int)chameleon.BassStyle, new[] { "303", "Reese", "808", "Neuro" }, GUILayout.Height(20));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Articulation :", GUILayout.Width(80));
                        int artSel = chameleon.BassRhythmicPluck ? 0 : 1;
                        int newArtSel = GUILayout.Toolbar(artSel, new[] { "⚡ Pluck", "〰️ Legato" }, GUILayout.Height(20));
                        if (newArtSel != artSel)
                        {
                            chameleon.BassRhythmicPluck = (newArtSel == 0);
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Volume Basse :", GUILayout.Width(90));
                        chameleon.BassMasterVolume = GUILayout.HorizontalSlider(chameleon.BassMasterVolume, 0f, 2.5f);
                        GUILayout.Label($"{Mathf.RoundToInt(chameleon.BassMasterVolume * 100)}%", GUILayout.Width(35));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Sub Épuré :", GUILayout.Width(90));
                        chameleon.BassSubCleanLayer = GUILayout.HorizontalSlider(chameleon.BassSubCleanLayer, 0f, 1f);
                        GUILayout.Label($"{Mathf.RoundToInt(chameleon.BassSubCleanLayer * 100)}%", GUILayout.Width(35));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Punch / Kick :", GUILayout.Width(90));
                        chameleon.BassPunch = GUILayout.HorizontalSlider(chameleon.BassPunch, 0f, 1f);
                        GUILayout.Label($"{Mathf.RoundToInt(chameleon.BassPunch * 100)}%", GUILayout.Width(35));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Drive / Satur :", GUILayout.Width(90));
                        chameleon.BassDrive = GUILayout.HorizontalSlider(chameleon.BassDrive, 1.0f, 6.0f);
                        GUILayout.Label($"{chameleon.BassDrive:0.0}x", GUILayout.Width(35));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Filtre Cutoff :", GUILayout.Width(90));
                        chameleon.BassCutoff = GUILayout.HorizontalSlider(chameleon.BassCutoff, 200f, 4500f);
                        GUILayout.Label($"{chameleon.BassCutoff:0} Hz", GUILayout.Width(50));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Octave :", GUILayout.Width(90));
                        int octSelection = chameleon.BassOctave + 1;
                        int newOctSelection = GUILayout.Toolbar(octSelection, new[] { "-1", "0", "+1" }, GUILayout.Height(18));
                        if (newOctSelection != octSelection)
                        {
                            chameleon.BassOctave = newOctSelection - 1;
                        }
                        if (GUILayout.Button("🎸 Test", GUILayout.Width(60), GUILayout.Height(18)))
                        {
                            chameleon.TestTriggerBass();
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();

                    GUILayout.Space(4);

                    // Overlay Harmonique Optionnel
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Lead Synth :", GUILayout.Width(75));
                    chameleon.SynthMode = (Experimental.ChameleonSynthMode)GUILayout.Toolbar((int)chameleon.SynthMode, new[] { "Sub", "Hybride", "Nytharite" }, GUILayout.Height(20));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Lead Vol :", GUILayout.Width(75));
                    chameleon.MimicVolume = GUILayout.HorizontalSlider(chameleon.MimicVolume, 0f, 1f);
                    GUILayout.Label($"{Mathf.RoundToInt(chameleon.MimicVolume * 100)}%", GUILayout.Width(35));
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4);

                    // Voix de Chorale (Choir Synth)
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.BeginHorizontal();
                    bool choirActive = GUILayout.Toggle(chameleon.EmulateChoir, "🕊️ <b>Voix de Chorale (Choir Synth)</b>", RichLabel());
                    if (choirActive != chameleon.EmulateChoir)
                    {
                        chameleon.EmulateChoir = choirActive;
                    }
                    GUILayout.FlexibleSpace();

                    if (chameleon.IsHarmonicNoteStable && chameleon.EmulateChoir)
                    {
                        GUILayout.Label($"<color=#00E5FF><b>{chameleon.CurrentChoirPitchHz:0.0} Hz</b></color>", RichLabel());
                    }
                    else
                    {
                        GUILayout.Label("<color=#8899AA>---</color>", RichLabel());
                    }
                    GUILayout.EndHorizontal();

                    if (chameleon.EmulateChoir)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Style Voix :", GUILayout.Width(80));
                        chameleon.ChoirStyle = (Experimental.ChameleonChoirStyle)GUILayout.Toolbar((int)chameleon.ChoirStyle, new[] { "Aah", "Ooh", "Hymne" }, GUILayout.Height(20));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Volume Chœur :", GUILayout.Width(95));
                        chameleon.ChoirMasterVolume = GUILayout.HorizontalSlider(chameleon.ChoirMasterVolume, 0f, 2.5f);
                        GUILayout.Label($"{Mathf.RoundToInt(chameleon.ChoirMasterVolume * 100)}%", GUILayout.Width(35));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Chœur / Détune :", GUILayout.Width(95));
                        chameleon.ChoirDetune = GUILayout.HorizontalSlider(chameleon.ChoirDetune, 0.5f, 4.0f);
                        GUILayout.Label($"{chameleon.ChoirDetune:0.0}¢", GUILayout.Width(35));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Shimmer / Air :", GUILayout.Width(95));
                        chameleon.ChoirShimmer = GUILayout.HorizontalSlider(chameleon.ChoirShimmer, 0f, 1f);
                        GUILayout.Label($"{Mathf.RoundToInt(chameleon.ChoirShimmer * 100)}%", GUILayout.Width(35));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Octave :", GUILayout.Width(95));
                        int choirOctSel = chameleon.ChoirOctave + 1;
                        int newChoirOctSel = GUILayout.Toolbar(choirOctSel, new[] { "-1", "0", "+1", "+2" }, GUILayout.Height(18));
                        if (newChoirOctSel != choirOctSel)
                        {
                            chameleon.ChoirOctave = newChoirOctSel - 1;
                        }
                        if (GUILayout.Button("🕊️ Test", GUILayout.Width(60), GUILayout.Height(18)))
                        {
                            chameleon.TestTriggerChoir();
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();

                    GUILayout.Space(4);

                    // Voix Chantée Émulée (Singing Voice Synth)
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.BeginHorizontal();
                    bool vocalActive = GUILayout.Toggle(chameleon.EmulateVocal, "🎤 <b>Voix Chantée Émulée (Solo Vocal)</b>", RichLabel());
                    if (vocalActive != chameleon.EmulateVocal)
                    {
                        chameleon.EmulateVocal = vocalActive;
                    }
                    GUILayout.FlexibleSpace();

                    if (chameleon.IsHarmonicNoteStable && chameleon.EmulateVocal)
                    {
                        GUILayout.Label($"<color=#FFD200><b>{chameleon.CurrentVocalPitchHz:0.0} Hz</b></color>", RichLabel());
                    }
                    else
                    {
                        GUILayout.Label("<color=#8899AA>---</color>", RichLabel());
                    }
                    GUILayout.EndHorizontal();

                    if (chameleon.EmulateVocal)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Style Vocal :", GUILayout.Width(80));
                        chameleon.VocalStyle = (Experimental.ChameleonVocalStyle)GUILayout.Toolbar((int)chameleon.VocalStyle, new[] { "Soprano", "Ténor", "Morph" }, GUILayout.Height(20));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Volume Voix :", GUILayout.Width(95));
                        chameleon.VocalMasterVolume = GUILayout.HorizontalSlider(chameleon.VocalMasterVolume, 0f, 2.5f);
                        GUILayout.Label($"{Mathf.RoundToInt(chameleon.VocalMasterVolume * 100)}%", GUILayout.Width(35));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Vibrato Chant :", GUILayout.Width(95));
                        chameleon.VocalVibratoDepth = GUILayout.HorizontalSlider(chameleon.VocalVibratoDepth, 0f, 1f);
                        GUILayout.Label($"{Mathf.RoundToInt(chameleon.VocalVibratoDepth * 100)}%", GUILayout.Width(35));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Souffle Vocal :", GUILayout.Width(95));
                        chameleon.VocalBreathiness = GUILayout.HorizontalSlider(chameleon.VocalBreathiness, 0f, 1f);
                        GUILayout.Label($"{Mathf.RoundToInt(chameleon.VocalBreathiness * 100)}%", GUILayout.Width(35));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Octave :", GUILayout.Width(95));
                        int vocalOctSel = chameleon.VocalOctave + 1;
                        int newVocalOctSel = GUILayout.Toolbar(vocalOctSel, new[] { "-1", "0", "+1", "+2" }, GUILayout.Height(18));
                        if (newVocalOctSel != vocalOctSel)
                        {
                            chameleon.VocalOctave = newVocalOctSel - 1;
                        }
                        if (GUILayout.Button("🎤 Test", GUILayout.Width(60), GUILayout.Height(18)))
                        {
                            chameleon.TestTriggerVocal();
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();

                    GUILayout.Space(4);

                    // Émulation Percussive Dédiée (Drums) & Anticipateur Prédictif
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.BeginHorizontal();
                    bool drumsActive = GUILayout.Toggle(chameleon.EmulateDrums, "🥁 <b>Émulateur Percussif (Drums)</b>", RichLabel());
                    if (drumsActive != chameleon.EmulateDrums)
                    {
                        chameleon.EmulateDrums = drumsActive;
                    }
                    GUILayout.FlexibleSpace();

                    // Témoins de Déclenchement Dynamique
                    string kCol = chameleon.KickPulse > 0.15f ? "#FF0055" : "#334455";
                    string sCol = chameleon.SnarePulse > 0.15f ? "#00E5FF" : "#334455";
                    string hCol = chameleon.HatPulse > 0.15f ? "#FFE600" : "#334455";

                    GUILayout.Label($"<color={kCol}><b>[KICK]</b></color> <color={sCol}><b>[SNARE]</b></color> <color={hCol}><b>[HAT]</b></color>", RichLabel());
                    GUILayout.EndHorizontal();

                    if (chameleon.EmulateDrums)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Master Drums :", GUILayout.Width(90));
                        chameleon.DrumMasterVolume = GUILayout.HorizontalSlider(chameleon.DrumMasterVolume, 0f, 2.5f);
                        GUILayout.Label($"{Mathf.RoundToInt(chameleon.DrumMasterVolume * 100)}%", GUILayout.Width(35));
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Mix K / S / H :", GUILayout.Width(90));
                        chameleon.KickVolume = GUILayout.HorizontalSlider(chameleon.KickVolume, 0f, 2.5f, GUILayout.Width(50));
                        chameleon.SnareVolume = GUILayout.HorizontalSlider(chameleon.SnareVolume, 0f, 2.5f, GUILayout.Width(50));
                        chameleon.HatVolume = GUILayout.HorizontalSlider(chameleon.HatVolume, 0f, 2.5f, GUILayout.Width(50));
                        GUILayout.EndHorizontal();

                        // Boutons de Test Immédiat
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Test Sonore :", GUILayout.Width(90));
                        if (GUILayout.Button("🥁 Kick", GUILayout.Height(20))) chameleon.TestTriggerKick();
                        if (GUILayout.Button("💥 Snare", GUILayout.Height(20))) chameleon.TestTriggerSnare();
                        if (GUILayout.Button("⚡ Hat", GUILayout.Height(20))) chameleon.TestTriggerHat();
                        GUILayout.EndHorizontal();

                        GUILayout.Space(4);

                        // Synchronisation Prédictive PLL
                        GUILayout.BeginVertical(GUI.skin.box);
                        GUILayout.BeginHorizontal();
                        chameleon.UsePredictiveSync = GUILayout.Toggle(chameleon.UsePredictiveSync, "⚡ <b>Verrouillage Prédictif (Zéro-Latence)</b>", RichLabel());
                        string lockStatus = chameleon.PhaseLockConfidence >= 0.35f ? "<color=#00FF88>CALÉ (PLL)</color>" : "<color=#FFB830>RÉACTIF (DIRECT)</color>";
                        GUILayout.Label(lockStatus, RichLabel(), GUILayout.Width(105));
                        GUILayout.EndHorizontal();

                        if (chameleon.UsePredictiveSync)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("Avance Latence :", GUILayout.Width(95));
                            chameleon.LatencyCompensationMs = GUILayout.HorizontalSlider(chameleon.LatencyCompensationMs, 0f, 250f);
                            GUILayout.Label($"{chameleon.LatencyCompensationMs:0} ms", GUILayout.Width(45));
                            GUILayout.EndHorizontal();
                        }
                        GUILayout.EndVertical();
                    }
                    GUILayout.EndVertical();
                }
            }
            GUILayout.EndVertical();
            } // fin else (caméléon présent)

            GUILayout.Space(4);

            // 2. Curseurs des canaux de mixage
            GUILayout.Label("<b>Canaux de Volume :</b>", RichLabel());
            GUILayout.BeginVertical(GUI.skin.box);
            DrawSliderRow(mgr, SoundCategory.Master, "Master");
            DrawSliderRow(mgr, SoundCategory.Music, "Musique");
            DrawSliderRow(mgr, SoundCategory.Ambience, "Ambiance");
            DrawSliderRow(mgr, SoundCategory.SFX, "SFX");
            DrawSliderRow(mgr, SoundCategory.UI, "Interface");
            GUILayout.EndVertical();

            // 2.b Voix Multijoueur VTT
            var voiceMgr = Killtime.Multi.Voice.VTTVoiceManager.Instance;
            if (voiceMgr != null)
            {
                GUILayout.Space(4);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label("🎙️ Voix VTT :", GUILayout.Width(85));
                float curVoiceVol = voiceMgr.MasterOutputVolume;
                float newVoiceVol = GUILayout.HorizontalSlider(curVoiceVol, 0f, 2f);
                if (!Mathf.Approximately(curVoiceVol, newVoiceVol))
                {
                    voiceMgr.SetMasterOutputVolume(newVoiceVol);
                }
                GUILayout.Label($"{Mathf.RoundToInt(newVoiceVol * 100)}%", GUILayout.Width(38));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                string micStatus = voiceMgr.IsMuted ? "<color=red>🔇 Muet</color>" : (voiceMgr.IsLocalSpeaking ? "<color=#00FF88>● En parole</color>" : "<color=gray>○ Écoute</color>");
                GUILayout.Label($"Micro : {micStatus}", RichLabel(), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("⚙️ Config Voix (F4)", GUILayout.Width(130), GUILayout.Height(20)))
                {
                    Killtime.Multi.VTTRoomWindow.Open();
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(4);

            // 3. Intensité de l'arrangement adaptatif
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Intensité :", GUILayout.Width(75));
            float inten = mgr.CurrentIntensity;
            float ninten = GUILayout.HorizontalSlider(inten, 0f, 1f);
            if (!Mathf.Approximately(ninten, inten) && mgr.CurrentMood != MusicMood.None)
            {
                mgr.PlayMusic(mgr.CurrentMood, ninten);
            }
            GUILayout.Label($"{Mathf.RoundToInt(ninten * 100)}%", GUILayout.Width(38));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 4. Contrôles généraux (Mute & Stinger)
            GUILayout.BeginHorizontal();
            Color prevBg = GUI.backgroundColor;
            if (mgr.IsMuted) GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (GUILayout.Button(mgr.IsMuted ? "🔇 RÉACTIVER (M)" : "🔊 SILENCE (M)", GUILayout.Height(26)))
            {
                mgr.SetMuted(!mgr.IsMuted);
            }
            GUI.backgroundColor = prevBg;

            if (GUILayout.Button("⚡ STINGER", GUILayout.Height(26)))
            {
                mgr.PlayStinger(mgr.CurrentMood == MusicMood.None ? MusicMood.Combat : mgr.CurrentMood);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 5. Presets d'ambiance
            GUILayout.Label("<b>Changement d'Humeur :</b>", RichLabel());
            GUILayout.BeginHorizontal();
            DrawMoodButton(mgr, MusicMood.Combat, "⚔️ Combat I");
            DrawMoodButton(mgr, MusicMood.CombatBoss, "🔥 Combat II (Boss)");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawMoodButton(mgr, MusicMood.Explore, "🌌 Explore");
            DrawMoodButton(mgr, MusicMood.Tension, "⚠️ Tension");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawMoodButton(mgr, MusicMood.Victory, "🏆 Victoire");
            DrawMoodButton(mgr, MusicMood.Defeat, "💀 Défaite");
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
        }

        private static void DrawSliderRow(KilltimeAudioManager mgr, SoundCategory cat, string label)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(75));
            float vol = mgr.GetCategoryVolume(cat);
            float nvol = GUILayout.HorizontalSlider(vol, 0f, 1f);
            if (!Mathf.Approximately(nvol, vol))
            {
                mgr.SetCategoryVolume(cat, nvol);
            }
            GUILayout.Label($"{Mathf.RoundToInt(nvol * 100)}%", GUILayout.Width(38));
            GUILayout.EndHorizontal();
        }

        private static void DrawMoodButton(KilltimeAudioManager mgr, MusicMood mood, string label)
        {
            bool active = (mgr.CurrentMood == mood);
            Color prev = GUI.backgroundColor;
            if (active) GUI.backgroundColor = new Color(0.0f, 0.85f, 1.0f);

            if (GUILayout.Button(label, GUILayout.Height(24)))
            {
                if (mood == MusicMood.Victory)
                {
                    mgr.PlayStinger(MusicMood.Victory);
                    mgr.PlayMusic(MusicMood.Victory, MusicIntensity.Intense, true);
                }
                else if (mood == MusicMood.Defeat)
                {
                    mgr.PlayStinger(MusicMood.Defeat);
                    mgr.PlayMusic(MusicMood.Defeat, MusicIntensity.Calm, true);
                }
                else if (mood == MusicMood.CombatBoss)
                {
                    mgr.PlayStinger(MusicMood.CombatBoss);
                    mgr.PlayMusic(MusicMood.CombatBoss, MusicIntensity.Intense, true);
                }
                else if (mood == MusicMood.Combat)
                {
                    mgr.PlayMusic(MusicMood.Combat, 0, mgr.CurrentIntensity, forceRestart: true);
                }
                else if (mood == MusicMood.Tension)
                {
                    mgr.PlayMusic(MusicMood.Tension, Mathf.Max(mgr.CurrentIntensity, 0.7f), true);
                }
                else
                {
                    mgr.PlayMusic(mood, mgr.CurrentIntensity, true);
                }
            }

            GUI.backgroundColor = prev;
        }

        private static Texture2D _scopeTex;
        private static Color32[] _scopePixels;
        private const int ScopeW = 260;
        private const int ScopeH = 44;

        private static void DrawChameleonScope(Experimental.SystemAudioChameleon chameleon)
        {
            if (_scopeTex == null)
            {
                _scopeTex = new Texture2D(ScopeW, ScopeH, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                _scopePixels = new Color32[ScopeW * ScopeH];
            }

            Color32 bg = new Color32(8, 14, 22, 240);
            Color32 gridCol = new Color32(20, 32, 45, 255);
            Color32 rawWaveCol = new Color32(90, 120, 140, 180);
            Color32 cleanWaveCol = new Color32(0, 230, 255, 255);

            for (int i = 0; i < _scopePixels.Length; i++) _scopePixels[i] = bg;

            int midY = ScopeH / 2;
            for (int x = 0; x < ScopeW; x++) _scopePixels[midY * ScopeW + x] = gridCol;

            var raw = chameleon.RawBuffer;
            var clean = chameleon.CleanedBuffer;
            int bufLen = raw != null ? raw.Length : 0;

            if (bufLen >= ScopeW)
            {
                int step = bufLen / ScopeW;
                for (int x = 0; x < ScopeW; x++)
                {
                    int sampleIdx = x * step;

                    float rVal = Mathf.Clamp(raw[sampleIdx] * 1.8f, -1f, 1f);
                    int ry = Mathf.Clamp(midY + (int)(rVal * (midY - 2)), 0, ScopeH - 1);
                    _scopePixels[ry * ScopeW + x] = rawWaveCol;

                    float cVal = Mathf.Clamp(clean[sampleIdx] * 1.8f, -1f, 1f);
                    int cy = Mathf.Clamp(midY + (int)(cVal * (midY - 2)), 0, ScopeH - 1);
                    _scopePixels[cy * ScopeW + x] = cleanWaveCol;
                }
            }

            _scopeTex.SetPixels32(_scopePixels);
            _scopeTex.Apply();

            Rect scopeRect = GUILayoutUtility.GetRect(ScopeW, ScopeH);
            GUI.DrawTexture(scopeRect, _scopeTex, ScaleMode.StretchToFill);

            var noteStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 8,
                alignment = TextAnchor.UpperLeft
            };
            noteStyle.normal.textColor = new Color(0f, 0.9f, 1f, 0.75f);
            GUI.Label(new Rect(scopeRect.x + 4, scopeRect.y + 2, 180, 12), "── Isolé (Cyan)  ── Brut (Gris)", noteStyle);
        }

        private static void DrawChameleonVUMeter(string label, float rms01, Color barColor)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, RichLabel(), GUILayout.Width(148));
            float val = Mathf.Clamp01(rms01 * 3.2f);

            Rect bar = GUILayoutUtility.GetRect(65, 10);
            Color prev = GUI.color;
            GUI.color = new Color(0.08f, 0.12f, 0.16f, 0.9f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);

            if (val > 0.005f)
            {
                GUI.color = barColor;
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * val, bar.height), Texture2D.whiteTexture);
            }
            GUI.color = prev;

            GUILayout.Label($"{Mathf.RoundToInt(val * 100)}%", RichLabel(), GUILayout.Width(30));
            GUILayout.EndHorizontal();
        }

        private static string TruncateDeviceName(string name, int maxChars)
        {
            if (string.IsNullOrEmpty(name)) return "---";
            if (name.Length <= maxChars) return name;
            return name.Substring(0, maxChars - 1) + "…";
        }

        private static GUIStyle RichLabel()
        {
            if (_richLabel == null)
            {
                _richLabel = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 10,
                    wordWrap = false,
                    clipping = TextClipping.Clip
                };
            }
            return _richLabel;
        }
    }
}