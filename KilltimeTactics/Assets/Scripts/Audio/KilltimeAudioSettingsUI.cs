using System;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Audio
{
    /// <summary>
    /// Panneau de mixage audio tactique unifié (Master, Musique, Ambiance, SFX, UI).
    /// Hérite de FloatingWindow pour partager le chrome, le style et le cycle de vie du HUD.
    /// Raccourcis : F11 pour ouvrir/fermer, M pour couper/rétablir le son.
    /// </summary>
    public partial class KilltimeAudioSettingsUI : FloatingWindow<KilltimeAudioSettingsUI>
    {
        protected override int WindowId => 892;
        protected override string Title => "Mixage // Killtime";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(24f, 140f, 320f, 380f);
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F11 };
        private static readonly Vector2 _minSize = new Vector2(280f, 260f);

        private Vector2 _scrollPos;
        private static GUIStyle _richLabel;
        private bool _showGenStyleList;
        private bool _showStemGraph = true;
        private static readonly string[] _genStyleNames =
        {
            "Auto (dynamique)", "Electro", "Drum & Bass", "Rétro 16-bit", "Ciné moderne"
        };
        private static readonly string[] _genMeterLabels = { "Auto", "4/4", "3/4", "5/4", "7/8" };
        private static readonly int[] _genMeterValues = { 0, 4, 3, 5, 7 };

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

            string label = (mgr != null && mgr.IsMuted) ? "🔇 SON (M)" : "🔊 SON (F11)";
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
            bool genActive = mgr.GenerativeCombatEnabled && KilltimeAudioManager.IsGenerativeMood(mgr.CurrentMood);
            string trackTag = genActive
                ? $" <color=#00E5FF>[∞ G{mgr.GenerativeGeneration} {mgr.CurrentGenerativeParams.bpm:0}BPM {GenerativeMusicEngine.FormatMeterLabel(mgr.CurrentGenerativeParams.beatsPerBar)}]</color>"
                : (trackCount > 1
                    ? $" <color=#00E5FF>[{mgr.CurrentTrackIndex + 1}/{trackCount}]</color>"
                    : "");

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Humeur :</b> <color={moodColor}>{mgr.CurrentMood}</color> [{mgr.CurrentIntensityLevel}]{trackTag}", RichLabel(), GUILayout.ExpandWidth(true));
            GUILayout.Label($"<b>Débit :</b> {Mathf.RoundToInt(mgr.CurrentIntensity * 100)}%", RichLabel(), GUILayout.Width(68));
            GUILayout.EndHorizontal();

            bool isGen = mgr.GenerativeCombatEnabled && KilltimeAudioManager.IsGenerativeMood(mgr.CurrentMood);
            bool canRotate = isGen || (mgr.CurrentMood == MusicMood.Combat && trackCount > 1);
            GUI.enabled = canRotate;
            string rotateLabel = isGen ? $"⏭️ Variation suivante (G{mgr.GenerativeGeneration + 1})"
                : (canRotate ? "⏭️ Piste Suivante (Rotation)" : "⏭️ Piste Unique (Fixe)");
            if (GUILayout.Button(rotateLabel, GUILayout.Height(20)))
            {
                mgr.NextCombatTrack();
            }
            GUI.enabled = true;

            if (!isGen && mgr.CurrentMood == MusicMood.Combat && trackCount > 1)
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

            // 1.a Mode Génératif ∞ (sans chanson préfaite) — dynamise combats/scènes.
            GUILayout.BeginVertical(GUI.skin.box);
            bool genOn = mgr.GenerativeCombatEnabled;
            Color prevGenBg = GUI.backgroundColor;
            if (genOn) GUI.backgroundColor = new Color(0f, 0.85f, 1f);
            string genBtnLabel = genOn
                ? "∞ MODE GENERATIF : ON (clic = retour prefaites)"
                : "∞ ACTIVER LE MODE GENERATIF (live, jamais 2x pareil)";
            if (GUILayout.Button(genBtnLabel, GUILayout.Height(24)))
            {
                mgr.SetGenerativeMode(!genOn);
            }
            GUI.backgroundColor = prevGenBg;
            if (mgr.GenerativeCombatEnabled)
            {
                var gp = mgr.CurrentGenerativeParams;
                var role = GenerativeMusicEngine.GetSectionRole(gp);
                string meterStr = GenerativeMusicEngine.FormatMeterLabel(gp.beatsPerBar);
                string genInfo = $"<color=#00E5FF>G{mgr.GenerativeGeneration} [{role} {meterStr}]</color> {gp.bpm:0} BPM {gp.drumStyle}/{gp.scale} "
                    + $"E:{Mathf.RoundToInt(gp.intensity01 * 100)}% T:{Mathf.RoundToInt(gp.tension01 * 100)}% "
                    + $"▶ {Mathf.RoundToInt(mgr.GenerativeLoopProgress * 100)}%";
                GUILayout.Label(genInfo, RichLabel());
                GUILayout.Label("Bus: " + mgr.GetMusicSourcesDebug(), RichLabel());

                GUILayout.BeginHorizontal();
                GUILayout.Label("Style :", GUILayout.Width(70));
                int curStyle = Mathf.Clamp((int)mgr.GenerativePreset, 0, _genStyleNames.Length - 1);
                if (GUILayout.Button((_showGenStyleList ? "▾ " : "▸ ") + _genStyleNames[curStyle], GUILayout.Height(20)))
                {
                    _showGenStyleList = !_showGenStyleList;
                }
                GUILayout.EndHorizontal();
                if (_showGenStyleList)
                {
                    int sel = GUILayout.SelectionGrid(curStyle, _genStyleNames, 1, GUILayout.Height(20 * _genStyleNames.Length));
                    if (sel != curStyle)
                    {
                        mgr.SetGenerativePreset((GenerativeStylePreset)sel);
                        _showGenStyleList = false;
                    }
                }

                // Sélecteur de signature rythmique (Auto = 4/4 standard, 5/4 sur Boss)
                GUILayout.BeginHorizontal();
                GUILayout.Label("Mesure :", GUILayout.Width(70));
                int curMeterIdx = Array.IndexOf(_genMeterValues, mgr.GenerativeMeterOverride);
                if (curMeterIdx < 0) curMeterIdx = 0;
                int newMeterIdx = GUILayout.Toolbar(curMeterIdx, _genMeterLabels, GUILayout.Height(19));
                if (newMeterIdx != curMeterIdx)
                {
                    mgr.SetGenerativeMeterOverride(_genMeterValues[newMeterIdx]);
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Brillance :", GUILayout.Width(70));
                float bCur = mgr.GenerativeBrightness;
                float bNew = GUILayout.HorizontalSlider(bCur, 0f, 1f);
                GUILayout.Label($"{Mathf.RoundToInt(bNew * 100)}%", GUILayout.Width(35));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Chaos :", GUILayout.Width(70));
                float cCur = mgr.GenerativeChaos;
                float cNew = GUILayout.HorizontalSlider(cCur, 0f, 1f);
                GUILayout.Label($"{Mathf.RoundToInt(cNew * 100)}%", GUILayout.Width(35));
                GUILayout.EndHorizontal();

                if (!Mathf.Approximately(bNew, bCur) || !Mathf.Approximately(cNew, cCur))
                {
                    mgr.SetGenerativeColor(bNew, cNew);
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("🎲 Nouvelle variation", GUILayout.Height(20)))
                {
                    mgr.RequestGenerativeVariation();
                }
                if (GUILayout.Button("💥 Strike test", GUILayout.Height(20), GUILayout.Width(95)))
                {
                    mgr.NotifyCombatStrike(true, false);
                }
                GUILayout.EndHorizontal();

                GUILayout.Label("<color=#8899AA><i>Combat/Boss/Tension = live. Explore/Victoire/Defaite = prefaites.</i></color>", RichLabel());
            }
            else
            {
                GUILayout.Label("<color=#8899AA><i>Off : playlist prefaite (3 themes combat + rotation 48s).</i></color>", RichLabel());
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 1.b Matrice d'Orchestration & Graphe des 8 Voix / Stems
            DrawStemOrchestrationGraph(mgr);

            GUILayout.Space(4);

            // 1.c Module Caméléon (Écoute & Imitation Système)
            DrawChameleonPanel();

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
                string micStatus = voiceMgr.IsMuted ? "<color=red>🔇 Muet</color>" : (voiceMgr.IsLocalSpeaking ? "<color=#00FF88>● En parole</color>" : "<color=#8899AA>○ Écoute</color>");
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

        private void DrawStemOrchestrationGraph(KilltimeAudioManager mgr)
        {
            var stems = mgr.GetProceduralStemsStatus();
            int activeCount = 0;
            for (int i = 0; i < stems.Count; i++) if (stems[i].IsActive) activeCount++;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            string foldoutIcon = _showStemGraph ? "▾" : "▸";
            if (GUILayout.Button($"{foldoutIcon} 🎼 <b>Orchestration Procédurale</b> <color=#00E5FF>[{activeCount}/8 Stems]</color>", RichLabel(), GUILayout.ExpandWidth(true)))
            {
                _showStemGraph = !_showStemGraph;
            }

            if (mgr.StemOverridesMask != 0 && GUILayout.Button("↺ Auto", GUILayout.Width(54), GUILayout.Height(18)))
            {
                mgr.ResetAllStemOverrides();
            }
            GUILayout.EndHorizontal();

            if (_showStemGraph)
            {
                GUILayout.Space(2);

                Rect spectrumRect = GUILayoutUtility.GetRect(ScopeW, 10);
                Color prevColor = GUI.color;
                GUI.color = new Color(0.04f, 0.08f, 0.12f, 0.9f);
                GUI.DrawTexture(spectrumRect, Texture2D.whiteTexture);

                float stepW = spectrumRect.width / Mathf.Max(1, stems.Count);
                for (int i = 0; i < stems.Count; i++)
                {
                    var stem = stems[i];
                    if (!stem.IsActive) continue;

                    float hVal = Mathf.Clamp01(stem.Intensity * stem.RhythmicPulse);
                    if (hVal <= 0.02f) continue;

                    Rect barRect = new Rect(spectrumRect.x + i * stepW + 1f, spectrumRect.y + (1f - hVal) * spectrumRect.height, stepW - 2f, spectrumRect.height * hVal);
                    GUI.color = stem.Color;
                    GUI.DrawTexture(barRect, Texture2D.whiteTexture);
                }
                GUI.color = prevColor;

                GUILayout.Space(4);

                for (int i = 0; i < stems.Count; i++)
                {
                    var stem = stems[i];
                    GUILayout.BeginHorizontal();

                    string ovLabel;
                    Color ovBtnBg;
                    switch (stem.OverrideState)
                    {
                        case StemOverrideState.Muted:
                            ovLabel = "🔇";
                            ovBtnBg = new Color(0.9f, 0.25f, 0.25f, 0.9f);
                            break;
                        case StemOverrideState.ForceActive:
                            ovLabel = "🔊";
                            ovBtnBg = new Color(0.15f, 0.85f, 0.35f, 0.9f);
                            break;
                        default:
                            ovLabel = "⚡A";
                            ovBtnBg = new Color(0.2f, 0.3f, 0.4f, 0.6f);
                            break;
                    }

                    Color prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = ovBtnBg;
                    if (GUILayout.Button(ovLabel, GUILayout.Width(28), GUILayout.Height(18)))
                    {
                        StemOverrideState nextState = stem.OverrideState switch
                        {
                            StemOverrideState.Auto => StemOverrideState.ForceActive,
                            StemOverrideState.ForceActive => StemOverrideState.Muted,
                            _ => StemOverrideState.Auto
                        };
                        mgr.SetStemOverride(stem.Type, nextState);
                    }
                    GUI.backgroundColor = prevBg;

                    string statusColor = stem.IsActive ? "#00FF88" : "#667788";
                    string nameText = stem.IsActive ? $"<color={statusColor}>●</color> <b>{stem.Name}</b>" : $"<color={statusColor}>○</color> <color=#778899>{stem.Name}</color>";
                    GUILayout.Label(nameText, RichLabel(), GUILayout.Width(72));

                    string subText = stem.IsActive ? $"<color=#A0B0C0>{TruncateDeviceName(stem.SubType, 13)}</color>" : "<color=#556677>Mute</color>";
                    GUILayout.Label(subText, RichLabel(), GUILayout.Width(78));

                    Rect vuRect = GUILayoutUtility.GetRect(50, 10);
                    GUI.color = new Color(0.06f, 0.10f, 0.14f, 0.9f);
                    GUI.DrawTexture(vuRect, Texture2D.whiteTexture);

                    if (stem.IsActive)
                    {
                        float activity = Mathf.Clamp01(stem.Intensity * (0.35f + stem.RhythmicPulse * 0.65f));
                        GUI.color = stem.Color;
                        GUI.DrawTexture(new Rect(vuRect.x, vuRect.y, vuRect.width * activity, vuRect.height), Texture2D.whiteTexture);
                        GUI.color = Color.white;
                        GUILayout.Label($"{Mathf.RoundToInt(activity * 100)}%", RichLabel(), GUILayout.Width(26));
                    }
                    else
                    {
                        GUI.color = Color.white;
                        GUILayout.Label("<color=#556677>0%</color>", RichLabel(), GUILayout.Width(26));
                    }

                    GUI.color = prevColor;
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
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
