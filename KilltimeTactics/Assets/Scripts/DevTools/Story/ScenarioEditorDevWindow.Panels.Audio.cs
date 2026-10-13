using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;

namespace Killtime.Story
{
    public partial class ScenarioEditorDevWindow : FloatingWindow<ScenarioEditorDevWindow>
    {

        private void DrawAudioToolbar()
        {
            _data.AudioSettings ??= new SceneAudioSettings();
            var settings = _data.AudioSettings;
            int sndCount = _data.Sounds != null ? _data.Sounds.Count : 0;
            int ambCount = _data.Ambiences != null ? _data.Ambiences.Count : 0;
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"<b>🔊 Audio</b> ({sndCount} 🔊 + {ambCount} 🌌)", GUILayout.Width(160));
            GUI.backgroundColor = _showBankBrowser ? new Color(0.35f, 0.7f, 1f) : Color.white;
            if (GUILayout.Button("🎵 Banque", GUILayout.Width(95), GUILayout.Height(20))) _showBankBrowser = !_showBankBrowser;
            GUI.backgroundColor = _showAudioAudit ? new Color(1f, 0.8f, 0.3f) : Color.white;
            string auditLabel = _auditDirty ? "🔍 Audit" : (_auditErrorCount > 0 ? $"🔍 Audit ({_auditErrorCount})" : "🔍 Audit ✔");
            if (GUILayout.Button(auditLabel, GUILayout.Width(105), GUILayout.Height(20))) { _showAudioAudit = !_showAudioAudit; if (_showAudioAudit) _auditDirty = true; }
            GUI.backgroundColor = _showAudioMonitor ? new Color(0.4f, 1f, 0.6f) : Color.white;
            if (GUILayout.Button("🎛 Monitor", GUILayout.Width(95), GUILayout.Height(20))) _showAudioMonitor = !_showAudioMonitor;
            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();
            bool newMute = GUILayout.Toggle(settings.Muted, "🔇 Muet", GUILayout.Width(70));
            if (newMute != settings.Muted) { settings.Muted = newMute; if (newMute) KilltimeAudioManager.Instance?.StopAllSceneSounds(0.1f); }
            GUILayout.Label("Master", GUILayout.Width(48));
            settings.MasterVolumeScale = GUILayout.HorizontalSlider(Mathf.Clamp(settings.MasterVolumeScale, 0f, 2f), 0f, 2f, GUILayout.Width(90));
            settings.MasterVolumeScale = Mathf.Clamp(settings.MasterVolumeScale, 0f, 2f);
            GUILayout.Label(settings.MasterVolumeScale.ToString("0.0"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
        }

        private void DrawBankBrowserPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>🎵 Banque sons/fx</b> <color=grey>(pré-écoute 2D — les réglages carte s'appliquent au runtime)</color>", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("■ Stop", GUILayout.Width(80), GUILayout.Height(20))) StopBankPreview();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Filtre :", GUILayout.Width(50));
            _bankSearch = GUILayout.TextField(_bankSearch ?? "", GUILayout.Width(200));
            GUILayout.Label("Vol test :", GUILayout.Width(60));
            _bankTestVolume = GUILayout.HorizontalSlider(Mathf.Clamp(_bankTestVolume, 0f, 2f), 0f, 2f, GUILayout.Width(100));
            _bankTestVolume = Mathf.Clamp(_bankTestVolume, 0f, 2f);
            GUILayout.Label(_bankTestVolume.ToString("0.0"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            var all = Killtime.Audio.SoundBankCatalog.GetAll();
            _bankScroll = GUILayout.BeginScrollView(_bankScroll, GUILayout.Height(140));
            if (all != null)
            {
                string f = (_bankSearch ?? "").Trim();
                for (int i = 0; i < all.Count; i++)
                {
                    var e = all[i];
                    if (e == null || string.IsNullOrWhiteSpace(e.id)) continue;
                    if (!string.IsNullOrEmpty(f)
                        && e.id.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                        && (e.label ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    GUILayout.BeginHorizontal();
                    bool playing = string.Equals(_bankPreviewCue, e.id, StringComparison.OrdinalIgnoreCase)
                        && _bankPreviewSrc != null && _bankPreviewSrc.isPlaying;
                    GUI.backgroundColor = playing ? new Color(0.2f, 0.75f, 0.4f) : Color.white;
                    if (GUILayout.Button(playing ? "■" : "▶", GUILayout.Width(30), GUILayout.Height(20)))
                    {
                        if (playing) StopBankPreview();
                        else PreviewBankEntry(e.id);
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.Label($"<b>{e.DisplayName()}</b> <color=grey>[{e.id}] {e.duration:0.0}s vol {e.volume:0.0}</color>", GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void PreviewBankEntry(string bankId)
        {
            StopBankPreview();
            if (string.IsNullOrWhiteSpace(bankId) || KilltimeAudioManager.Instance == null) return;
            var entry = Killtime.Audio.SoundBankCatalog.Find(bankId);
            if (entry == null) return;
            var clip = Killtime.Audio.ProceduralAudioFactory.GetBankClip(entry, entry.duration);
            if (clip == null) return;
            _bankPreviewSrc = KilltimeAudioManager.Instance.PlayBankUI(entry.id, clip, _bankTestVolume * entry.volume);
            _bankPreviewCue = entry.id;
        }

        private void StopBankPreview()
        {
            _bankPreviewCue = "";
            if (_bankPreviewSrc != null)
            {
                try { _bankPreviewSrc.Stop(); } catch { /* ignore */ }
                _bankPreviewSrc = null;
            }
        }

        private void DrawAudioAuditPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            string title = _auditDirty ? "<b>🔍 Audit audio</b> <color=grey>(à analyser)</color>"
                : $"<b>🔍 Audit audio</b> ({_auditErrorCount} erreur(s))";
            GUILayout.Label(title, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Analyser", GUILayout.Width(90), GUILayout.Height(20))) BuildAudioAudit();
            if (GUILayout.Button("Réparer auto", GUILayout.Width(110), GUILayout.Height(20))) RepairAudioLinks();
            GUILayout.EndHorizontal();
            _auditScroll = GUILayout.BeginScrollView(_auditScroll, GUILayout.Height(140));
            if (_auditDirty)
            {
                GUILayout.Label("<color=grey>Cliquez « Analyser » : liens morts, cues inconnus, orphelines, doublons, loops infinies, conflits legacy.</color>");
            }
            else if (_auditLines.Count == 0)
            {
                GUILayout.Label("<color=#7CFF9B><b>✔ Aucun problème détecté.</b></color>");
            }
            else
            {
                for (int i = 0; i < _auditLines.Count; i++)
                    GUILayout.Label(_auditLines[i]);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private bool AudioCardIdExists(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            return TryGetSoundSourceBottom(id, out _);
        }

        private static bool IsKnownSoundCue(string cue)
        {
            if (string.IsNullOrWhiteSpace(cue)) return false;
            if (Killtime.Audio.SoundBankCatalog.Find(cue) != null) return true;
            return System.Enum.TryParse<Killtime.Audio.SoundId>(cue.Trim(), out _);
        }

        private void BuildAudioAudit()
        {
            _auditLines.Clear();
            _auditErrorCount = 0;
            if (_data == null) { _auditDirty = false; return; }
            void Err(string s) { _auditLines.Add($"<color=#FF6B6B>✕ {s}</color>"); _auditErrorCount++; }
            void Warn(string s) { _auditLines.Add($"<color=#FFC94D>⚠ {s}</color>"); }
            void Info(string s) { _auditLines.Add($"<color=grey>• {s}</color>"); }

            var seenSnd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_data.Sounds != null)
            {
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) { Err($"Son #{i} : entrée nulle."); continue; }
                    string sid = string.IsNullOrWhiteSpace(snd.SoundId) ? $"#{i}" : snd.SoundId;
                    if (!string.IsNullOrWhiteSpace(snd.SoundId) && !seenSnd.Add(snd.SoundId))
                        Err($"[{sid}] doublon de SoundId (renommage auto conseillé).");
                    if (string.IsNullOrWhiteSpace(snd.SoundCueId))
                        Warn($"[{sid}] aucun son configuré (SoundCueId vide).");
                    else if (!IsKnownSoundCue(snd.SoundCueId))
                        Err($"[{sid}] SoundCueId inconnu : '{snd.SoundCueId}' (ni banque ni SoundId).");
                    if (!string.IsNullOrWhiteSpace(snd.SourceCardId) && !AudioCardIdExists(snd.SourceCardId))
                        Err($"[{sid}] lien mort : source '{snd.SourceCardId}' introuvable.");
                    if (!string.IsNullOrWhiteSpace(snd.StopSourceCardId) && !AudioCardIdExists(snd.StopSourceCardId))
                        Err($"[{sid}] lien d'arrêt mort : cible '{snd.StopSourceCardId}' introuvable.");
                    if (string.IsNullOrWhiteSpace(snd.SourceCardId))
                        Warn($"[{sid}] orpheline (← non liée) : ne jouera jamais.");
                    if (snd.Loop && snd.DurationSeconds < 0.05f && string.IsNullOrWhiteSpace(snd.StopSourceCardId)
                        && (_data.AudioSettings == null || (!_data.AudioSettings.StopAllSoundsOnNodeChange && !_data.AudioSettings.StopAllSoundsOnSceneExit)))
                        Err($"[{sid}] loop infini sans arrêt (ni ■→, ni kill-switch) : fuira.");
                }
            }
            var seenAmb = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_data.Ambiences != null)
            {
                for (int i = 0; i < _data.Ambiences.Count; i++)
                {
                    var amb = _data.Ambiences[i];
                    if (amb == null) { Err($"Ambiance #{i} : entrée nulle."); continue; }
                    string aid = string.IsNullOrWhiteSpace(amb.AmbienceId) ? $"#{i}" : amb.AmbienceId;
                    if (!string.IsNullOrWhiteSpace(amb.AmbienceId) && !seenAmb.Add(amb.AmbienceId))
                        Err($"[{aid}] doublon d'AmbienceId.");
                    if (Killtime.Audio.AmbienceCatalog.Find(amb.PresetId) == null)
                        Err($"[{aid}] preset inconnu : '{amb.PresetId}'.");
                    if (!string.IsNullOrWhiteSpace(amb.StartSourceCardId) && !AudioCardIdExists(amb.StartSourceCardId))
                        Err($"[{aid}] lien mort : entrée '{amb.StartSourceCardId}' introuvable.");
                    if (!string.IsNullOrWhiteSpace(amb.StopSourceCardId) && !AudioCardIdExists(amb.StopSourceCardId))
                        Err($"[{aid}] lien d'arrêt mort : cible '{amb.StopSourceCardId}' introuvable.");
                    if (string.IsNullOrWhiteSpace(amb.StartSourceCardId) && string.IsNullOrWhiteSpace(amb.StopSourceCardId))
                        Warn($"[{aid}] orpheline (ni entrée ni arrêt) : ne jouera jamais.");
                }
            }
            // Conflits legacy : cue legacy + carte 🔊 liée (ignoré si PreferCards).
            bool preferCards = _data.AudioSettings == null || _data.AudioSettings.PreferCardsOverLegacy;
            if (_data.Nodes != null)
            {
                for (int n = 0; n < _data.Nodes.Count; n++)
                {
                    var node = _data.Nodes[n];
                    if (node == null || node.Dialogues == null) continue;
                    for (int d = 0; d < node.Dialogues.Count; d++)
                    {
                        var line = node.Dialogues[d];
                        if (line == null || string.IsNullOrWhiteSpace(line.SoundCueId)) continue;
                        if (_data.HasLinkedSounds(line.LineId))
                            Info($"[{line.LineId}] cue legacy '{line.SoundCueId}' + carte 🔊 : {(preferCards ? "legacy ignoré (PreferCards)" : "DOUBLE JEU — les deux jouent")}.");
                        else if (!IsKnownSoundCue(line.SoundCueId))
                            Warn($"[{line.LineId}] cue legacy inconnu : '{line.SoundCueId}'.");
                    }
                }
            }
            _auditDirty = false;
        }

        private void RepairAudioLinks()
        {
            if (_data == null) return;
            // 1) Liens morts → vidés (démarrage + arrêts, sons + ambiances).
            if (_data.Sounds != null)
            {
                var usedSnd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) continue;
                    if (!string.IsNullOrWhiteSpace(snd.SourceCardId) && !AudioCardIdExists(snd.SourceCardId))
                        snd.SourceCardId = "";
                    if (!string.IsNullOrWhiteSpace(snd.StopSourceCardId) && !AudioCardIdExists(snd.StopSourceCardId))
                        snd.StopSourceCardId = "";
                    if (string.IsNullOrWhiteSpace(snd.SoundId)) snd.SoundId = $"snd_{i + 1}";
                    string baseId = snd.SoundId;
                    int suffix = 2;
                    while (!usedSnd.Add(snd.SoundId)) snd.SoundId = $"{baseId}_{suffix++}";
                }
            }
            if (_data.Ambiences != null)
            {
                var usedAmb = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _data.Ambiences.Count; i++)
                {
                    var amb = _data.Ambiences[i];
                    if (amb == null) continue;
                    if (!string.IsNullOrWhiteSpace(amb.StartSourceCardId) && !AudioCardIdExists(amb.StartSourceCardId))
                        amb.StartSourceCardId = "";
                    if (!string.IsNullOrWhiteSpace(amb.StopSourceCardId) && !AudioCardIdExists(amb.StopSourceCardId))
                        amb.StopSourceCardId = "";
                    if (string.IsNullOrWhiteSpace(amb.AmbienceId)) amb.AmbienceId = $"amb_{i + 1}";
                    string baseId = amb.AmbienceId;
                    int suffix = 2;
                    while (!usedAmb.Add(amb.AmbienceId)) amb.AmbienceId = $"{baseId}_{suffix++}";
                }
            }
            BuildAudioAudit();
        }

        private void DrawAudioMonitorPanel()
        {
            _data.AudioSettings ??= new SceneAudioSettings();
            var s = _data.AudioSettings;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>🎛 Monitor audio scène</b>", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("■ Stop sons", GUILayout.Width(100), GUILayout.Height(20)))
                KilltimeAudioManager.Instance?.StopAllSceneSounds(0.2f);
            if (GUILayout.Button("■ Stop amb", GUILayout.Width(100), GUILayout.Height(20)))
                KilltimeAudioManager.Instance?.StopAllSceneAmbiences(0.5f);
            if (GUILayout.Button("■ Stop tests", GUILayout.Width(100), GUILayout.Height(20)))
            {
                StopAllSceneSoundPreviews();
                StopBankPreview();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            s.StopAllSoundsOnNodeChange = GUILayout.Toggle(s.StopAllSoundsOnNodeChange, "Kill inter-nœuds", GUILayout.Width(130));
            s.StopAllSoundsOnSceneExit = GUILayout.Toggle(s.StopAllSoundsOnSceneExit, "Kill sortie scène", GUILayout.Width(130));
            s.PreferCardsOverLegacy = GUILayout.Toggle(s.PreferCardsOverLegacy, "Cartes > legacy", GUILayout.Width(130));
            GUILayout.EndHorizontal();
            _monitorScroll = GUILayout.BeginScrollView(_monitorScroll, GUILayout.Height(110));
            var mgr = KilltimeAudioManager.Instance;
            if (mgr == null)
            {
                GUILayout.Label("<color=grey>AudioManager absent (pas de runtime) : les sources suivies apparaîtront en jeu.</color>");
            }
            else
            {
                var sounds = mgr.GetActiveSceneSoundKeys();
                var ambs = mgr.GetActiveSceneAmbienceKeys();
                if (sounds.Count == 0 && ambs.Count == 0)
                    GUILayout.Label("<color=grey>Aucune source suivie active.</color>");
                for (int i = 0; i < sounds.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"🔊 <b>{sounds[i]}</b>", GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("■", GUILayout.Width(30), GUILayout.Height(18)))
                        mgr.StopSceneSound(sounds[i], 0.2f);
                    GUILayout.EndHorizontal();
                }
                for (int i = 0; i < ambs.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"🌌 <b>{ambs[i]}</b>", GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("■", GUILayout.Width(30), GUILayout.Height(18)))
                        mgr.StopSceneAmbience(ambs[i], 0.5f);
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void StopAllSceneSoundPreviews()
        {
            var keys = new List<string>(_soundPreviewSources.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                if (_soundPreviewSources.TryGetValue(keys[i], out var src) && src != null)
                {
                    try { src.Stop(); } catch { /* ignore */ }
                }
            }
            _soundPreviewSources.Clear();
        }
        private void DrawSoundCard(SceneSoundData snd, int index, Vector2 mouseWorld)
        {
            if (snd == null) return;
            if (string.IsNullOrEmpty(snd.SoundId)) snd.SoundId = $"snd_{index + 1}";

            Vector2 size = GetSoundCardSize(snd);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(snd.GraphPosX, snd.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"sound_{index}_{snd.SoundId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            bool lateResizeSnd = IsLateResizeFor(snd);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeSnd)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeSnd) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                snd.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 240f, 750f);
                snd.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 130f, 500f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            bool lateDragSnd = IsLateDragFor(snd);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragSnd)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragSnd ? _lateDownWorld : mouseWorld) - new Vector2(snd.GraphPosX, snd.GraphPosY);
                if (lateDragSnd) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                snd.GraphPosX = mouseWorld.x - _dragOffset.x;
                snd.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            DrawGraphSolidRect(cardRect, new Color(0.14f, 0.09f, 0.03f, 0.95f));
            DrawGraphSolidRect(headerRect, new Color(0.55f, 0.35f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(1f, 0.75f, 0.25f, 0.8f));

            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>🔊 {snd.Title}</b> [{snd.SoundId}]");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                StopSceneSoundPreview(snd);
                if (_data.Sounds != null) _data.Sounds.RemoveAt(index);
                if (!string.IsNullOrWhiteSpace(snd.SoundId))
                {
                    _cachedSoundInputSockets.Remove(snd.SoundId);
                    _cachedSoundStopSockets.Remove(snd.SoundId);
                }
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            if (_zoom < CardLodThreshold)
            {
                DrawGraphSolidRect(new Rect(cardRect.x + 8f, cardRect.y + 32f, (cardW - 16f) * 0.8f, 8f), new Color(1f, 0.6f, 0.1f, 0.14f));
            }
            else
            {
                float innerX = cardRect.x + 8f;
                float innerW = cardW - 16f;
                float y = cardRect.y + 30f;

                GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
                snd.SoundId = GraphTextField(new Rect(innerX + 26f, y, 100f, 18f), snd.SoundId ?? "");
                snd.Muted = GraphToggle(new Rect(innerX + 132f, y, Mathf.Max(60f, innerW - 132f), 18f), snd.Muted, "🔇 Muet");
                y += 20f;

                GraphLabel(new Rect(innerX, y, 34f, 18f), "Titre:");
                snd.Title = GraphTextField(new Rect(innerX + 36f, y, Mathf.Max(40f, innerW - 36f), 18f), snd.Title ?? "");
                y += 20f;

                GraphLabel(new Rect(innerX, y, 36f, 18f), "Son :");
                snd.SoundCueId = GraphTextField(new Rect(innerX + 38f, y, Mathf.Max(40f, innerW - 38f - 68f), 18f), snd.SoundCueId ?? "");
                // Sélecteur banque sons/fx 🔊 : parcourt les presets (id + label).
                var bankList = Killtime.Audio.SoundBankCatalog.GetAll();
                if (bankList != null && bankList.Count > 0)
                {
                    int curBank = -1;
                    for (int bi = 0; bi < bankList.Count; bi++)
                    {
                        var be = bankList[bi];
                        if (be == null || string.IsNullOrWhiteSpace(be.id)) continue;
                        if (string.Equals(be.id.Trim(), (snd.SoundCueId ?? "").Trim(), System.StringComparison.OrdinalIgnoreCase)) { curBank = bi; break; }
                    }
                    if (GraphButton(new Rect(innerX + innerW - 64f, y, 20f, 18f), "♪"))
                    {
                        PreviewSceneSound(snd);
                    }
                    if (GraphButton(new Rect(innerX + innerW - 42f, y, 20f, 18f), "◀"))
                    {
                        curBank = curBank < 0 ? bankList.Count - 1 : (curBank - 1 + bankList.Count) % bankList.Count;
                        snd.SoundCueId = bankList[curBank] != null ? bankList[curBank].id : snd.SoundCueId;
                    }
                    if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                    {
                        curBank = curBank < 0 ? 0 : (curBank + 1) % bankList.Count;
                        snd.SoundCueId = bankList[curBank] != null ? bankList[curBank].id : snd.SoundCueId;
                    }
                }
                y += 20f;

                GraphLabel(new Rect(innerX, y, 42f, 18f), "Durée:");
                string durTxt = GraphTextField(new Rect(innerX + 44f, y, 40f, 18f), snd.DurationSeconds > 0.001f ? snd.DurationSeconds.ToString("0.0") : "0");
                if (float.TryParse(durTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedDur))
                    snd.DurationSeconds = Mathf.Clamp(parsedDur, 0f, 60f);

                GraphLabel(new Rect(innerX + 90f, y, 22f, 18f), "+d:");
                string delTxt = GraphTextField(new Rect(innerX + 114f, y, 36f, 18f), snd.DelaySeconds.ToString("0.0"));
                if (float.TryParse(delTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedDel))
                    snd.DelaySeconds = Mathf.Clamp(parsedDel, 0f, 60f);

                GraphLabel(new Rect(innerX + 156f, y, 28f, 18f), "Vol:");
                if (snd.VolumeScale <= 0.001f && !snd.Muted) snd.VolumeScale = 1.0f;
                string volTxt = GraphTextField(new Rect(innerX + 186f, y, 38f, 18f), snd.VolumeScale.ToString("0.0"));
                if (float.TryParse(volTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedVol))
                    snd.VolumeScale = Mathf.Clamp(parsedVol, 0f, 2f);
                y += 20f;

                // Garde enum (JSON édité à la main) : GraphToolbar planterait sinon.
                int trigIdx = System.Enum.IsDefined(typeof(SoundTriggerPoint), snd.TriggerPoint) ? (int)snd.TriggerPoint : 0;
                snd.TriggerPoint = (SoundTriggerPoint)GraphToolbar(new Rect(innerX, y, innerW, 18f), trigIdx, new[] { "▶ Début carte", "■ Fin carte" });
                y += 22f;

                GraphLabel(new Rect(innerX, y, 26f, 18f), "Pit:");
                string pitTxt = GraphTextField(new Rect(innerX + 28f, y, 38f, 18f), snd.PitchScale.ToString("0.00"));
                if (float.TryParse(pitTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedPit))
                    snd.PitchScale = Mathf.Clamp(parsedPit, 0.25f, 3f);
                snd.Loop = GraphToggle(new Rect(innerX + 72f, y, 66f, 18f), snd.Loop, "Loop ∞");
                GraphLabel(new Rect(innerX + 142f, y, 36f, 18f), "Prio:");
                string priTxt = GraphTextField(new Rect(innerX + 180f, y, 36f, 18f), snd.Priority.ToString());
                if (int.TryParse(priTxt, out int parsedPri))
                    snd.Priority = Mathf.Clamp(parsedPri, 0, 255);
                GraphLabel(new Rect(innerX + 220f, y, 24f, 18f), "Cd:");
                string cdTxt = GraphTextField(new Rect(innerX + 246f, y, Mathf.Max(30f, innerW - 246f), 18f), snd.CooldownSeconds.ToString("0.0"));
                if (float.TryParse(cdTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedCd))
                    snd.CooldownSeconds = Mathf.Clamp(parsedCd, 0f, 30f);
                y += 20f;

                GraphLabel(new Rect(innerX, y, 30f, 18f), "FIn:");
                string finTxt = GraphTextField(new Rect(innerX + 32f, y, 34f, 18f), snd.FadeInDuration.ToString("0.0"));
                if (float.TryParse(finTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedFIn))
                    snd.FadeInDuration = Mathf.Clamp(parsedFIn, 0f, 20f);
                GraphLabel(new Rect(innerX + 72f, y, 38f, 18f), "FOut:");
                string foutTxt = GraphTextField(new Rect(innerX + 112f, y, 34f, 18f), snd.FadeOutDuration.ToString("0.0"));
                if (float.TryParse(foutTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedFOut))
                    snd.FadeOutDuration = Mathf.Clamp(parsedFOut, 0f, 20f);
                snd.Spatialized = GraphToggle(new Rect(innerX + 152f, y, Mathf.Max(60f, innerW - 152f), 18f), snd.Spatialized, "Spatial 3D");
                y += 20f;

                if (snd.Spatialized)
                {
                    GraphLabel(new Rect(innerX, y, 16f, 18f), "Q:");
                    int.TryParse(GraphTextField(new Rect(innerX + 18f, y, 32f, 18f), snd.Q.ToString()), out snd.Q);
                    GraphLabel(new Rect(innerX + 54f, y, 16f, 18f), "R:");
                    int.TryParse(GraphTextField(new Rect(innerX + 72f, y, 32f, 18f), snd.R.ToString()), out snd.R);
                    GraphLabel(new Rect(innerX + 108f, y, 44f, 18f), "Rayon:");
                    string rTxt = GraphTextField(new Rect(innerX + 154f, y, 38f, 18f), snd.Radius.ToString("0.#"));
                    if (float.TryParse(rTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pRad))
                        snd.Radius = Mathf.Clamp(pRad, 1f, 120f);
                    y += 20f;
                }

                bool linked = !string.IsNullOrWhiteSpace(snd.SourceCardId);
                GUI.color = linked ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                GraphLabel(new Rect(innerX, y, Mathf.Max(40f, innerW - 26f), 18f),
                    linked ? $"← {snd.SourceCardId}" : "(drag carte ↓ → ici)");
                GUI.color = Color.white;
                if (linked && GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "⊘")) snd.SourceCardId = "";
                y += 20f;

                bool hasStop = !string.IsNullOrWhiteSpace(snd.StopSourceCardId);
                GUI.color = hasStop ? new Color(1f, 0.6f, 0.6f) : new Color(1f, 1f, 1f, 0.45f);
                GraphLabel(new Rect(innerX, y, Mathf.Max(40f, innerW - 26f), 18f),
                    hasStop ? $"■ Stop → {snd.StopSourceCardId}" : "(drag sortie bas → bas cible)");
                GUI.color = Color.white;
                if (hasStop && GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "⊘")) snd.StopSourceCardId = "";
                y += 22f;

                float halfBtn = (innerW - 6f) * 0.5f;
                GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);
                if (GraphButton(new Rect(innerX, y, halfBtn, 20f), "▶ Tester"))
                {
                    PreviewSceneSound(snd);
                }
                GUI.backgroundColor = new Color(0.85f, 0.35f, 0.35f);
                if (GraphButton(new Rect(innerX + halfBtn + 6f, y, halfBtn, 20f), "■ Stopper"))
                {
                    StopSceneSoundPreview(snd);
                }
                GUI.backgroundColor = Color.white;
            }

            // Port d'entrée (haut) : reçoit le fil du bas des cartes sources.
            Vector2 inCenter = GetSoundInCenter(snd);
            _cachedSoundInputSockets[snd.SoundId] = inCenter;
            DrawGraphSolidRect(new Rect(inCenter.x - 7f, inCenter.y - 7f, 14f, 14f),
                !string.IsNullOrWhiteSpace(snd.SourceCardId) ? SoundPortCol : new Color(SoundPortCol.r, SoundPortCol.g, SoundPortCol.b, 0.35f));

            // Port de sortie d'ARRÊT (bas) : tire un fil vers le bas d'une carte
            // cible pour couper le son (loops, longues durées). Miroir ambiance.
            Vector2 stopCenter = GetSoundStopCenter(snd);
            _cachedSoundStopSockets[snd.SoundId] = stopCenter;
            Rect sndStopPort = new Rect(stopCenter.x - 7f, stopCenter.y - 7f, 14f, 14f);
            bool sndHasStop = !string.IsNullOrWhiteSpace(snd.StopSourceCardId);
            DrawGraphSolidRect(sndStopPort,
                sndHasStop ? SoundStopPortCol : new Color(SoundStopPortCol.r, SoundStopPortCol.g, SoundStopPortCol.b, 0.35f));

            if (GraphPrimaryDown(mouseWorld, sndStopPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsSoundStopLink = true,
                    SourceSoundStop = snd,
                    StartPos = stopCenter
                };
                evt.Use();
            }
        }

        /// <summary>
        /// Pré-écoute d'une carte son dans l'éditeur (2D, volume × master scène,
        /// pitch/loop/fades de la carte). Écrase la pré-écoute précédente de la carte.
        /// </summary>
        private void PreviewSceneSound(SceneSoundData snd)
        {
            if (snd == null || KilltimeAudioManager.Instance == null) return;
            StopSceneSoundPreview(snd);
            string cue = (snd.SoundCueId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cue)) return;
            float master = _data != null && _data.AudioSettings != null ? Mathf.Clamp(_data.AudioSettings.MasterVolumeScale, 0f, 2f) : 1f;
            float vol = Mathf.Clamp(snd.VolumeScale, 0f, 2f) * master;
            AudioSource src = null;
            var bankEntry = Killtime.Audio.SoundBankCatalog.Find(cue);
            if (bankEntry != null)
            {
                var clip = Killtime.Audio.ProceduralAudioFactory.GetBankClip(bankEntry, bankEntry.duration);
                if (clip != null)
                    src = KilltimeAudioManager.Instance.PlayBankSound(bankEntry.id, clip, vol * bankEntry.volume,
                        Mathf.Clamp(snd.PitchScale <= 0f ? 1f : snd.PitchScale, 0.25f, 3f),
                        snd.Loop, Mathf.Max(0f, snd.FadeInDuration), Mathf.Clamp(snd.Priority, 0, 255),
                        false, Vector3.zero, 15f, null);
            }
            else if (System.Enum.TryParse<Killtime.Audio.SoundId>(cue, out var soundEnum))
            {
                src = KilltimeAudioManager.Instance.PlayUI(soundEnum, vol);
            }
            else
            {
                CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[ÉDITEUR] SoundCueId inconnu : '{cue}'.</color>", LogCategory.MovementAndTurns, "[ÉDITEUR]", Color.gray);
            }
            if (src != null && !string.IsNullOrWhiteSpace(snd.SoundId))
                _soundPreviewSources[snd.SoundId] = src;
        }

        /// <summary>Stoppe la pré-écoute d'une carte son (éditeur).</summary>
        private void StopSceneSoundPreview(SceneSoundData snd)
        {
            if (snd == null || string.IsNullOrWhiteSpace(snd.SoundId)) return;
            if (_soundPreviewSources.TryGetValue(snd.SoundId, out var src))
            {
                _soundPreviewSources.Remove(snd.SoundId);
                if (src != null)
                {
                    try
                    {
                        if (Mathf.Approximately(snd.FadeOutDuration, 0f)) src.Stop();
                        else StartCoroutine(FadePreviewSourceRoutine(src, Mathf.Max(0f, snd.FadeOutDuration)));
                    }
                    catch { /* ignore */ }
                }
            }
        }

        private static System.Collections.IEnumerator FadePreviewSourceRoutine(AudioSource src, float duration)
        {
            if (src == null) yield break;
            float startVol = src.volume;
            float elapsed = 0f;
            duration = Mathf.Max(0.05f, duration);
            while (elapsed < duration && src != null && src.isPlaying)
            {
                elapsed += Time.unscaledDeltaTime;
                src.volume = Mathf.Lerp(startVol, 0f, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            if (src != null)
            {
                try { src.Stop(); } catch { /* ignore */ }
                src.volume = startVol;
            }
        }

        private void DrawAmbienceCard(SceneAmbienceTrackData amb, int index, Vector2 mouseWorld)
        {
            if (amb == null) return;
            if (string.IsNullOrEmpty(amb.AmbienceId)) amb.AmbienceId = $"amb_{index + 1}";

            Vector2 size = GetAmbienceCardSize(amb);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(amb.GraphPosX, amb.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"amb_{index}_{amb.AmbienceId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            bool lateResizeAmb = IsLateResizeFor(amb);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeAmb)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeAmb) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                amb.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 260f, 750f);
                amb.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 160f, 500f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            bool lateDragAmb = IsLateDragFor(amb);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragAmb)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragAmb ? _lateDownWorld : mouseWorld) - new Vector2(amb.GraphPosX, amb.GraphPosY);
                if (lateDragAmb) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                amb.GraphPosX = mouseWorld.x - _dragOffset.x;
                amb.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            DrawGraphSolidRect(cardRect, new Color(0.04f, 0.12f, 0.11f, 0.95f));
            DrawGraphSolidRect(headerRect, new Color(0.08f, 0.45f, 0.38f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1.5f), AmbiencePortCol);

            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>🌌 {amb.Title}</b> [{amb.AmbienceId}]");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                string doomed = amb.AmbienceId;
                if (_data.Ambiences != null) _data.Ambiences.RemoveAt(index);
                PurgeAmbienceReferences(doomed);
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float y = cardRect.y + 30f;

            GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
            amb.AmbienceId = GraphTextField(new Rect(innerX + 26f, y, 90f, 18f), amb.AmbienceId ?? "");
            GraphLabel(new Rect(innerX + 120f, y, 36f, 18f), "Titre:");
            amb.Title = GraphTextField(new Rect(innerX + 158f, y, Mathf.Max(40f, innerW - 158f), 18f), amb.Title ?? "");
            y += 20f;

            var presets = AmbienceCatalog.GetAll();
            int currentIdx = presets.FindIndex(p => string.Equals(p.id, amb.PresetId, StringComparison.OrdinalIgnoreCase));
            if (currentIdx < 0 && presets.Count > 0) currentIdx = 0;

            GraphLabel(new Rect(innerX, y, 50f, 18f), "Preset :");
            if (GraphButton(new Rect(innerX + 52f, y, 20f, 18f), "◀"))
            {
                currentIdx = (currentIdx - 1 + presets.Count) % presets.Count;
                amb.PresetId = presets[currentIdx].id;
                amb.VolumeScale = presets[currentIdx].defaultVolume;
                amb.Spatialized = presets[currentIdx].spatial;
                amb.Radius = presets[currentIdx].defaultRadius;
            }
            string curName = currentIdx >= 0 && currentIdx < presets.Count ? presets[currentIdx].displayName : (amb.PresetId ?? "(aucun)");
            GraphLabel(new Rect(innerX + 74f, y, Mathf.Max(60f, innerW - 74f - 24f), 18f), $"<b>{curName}</b>");
            if (GraphButton(new Rect(innerX + innerW - 22f, y, 20f, 18f), "▶"))
            {
                currentIdx = (currentIdx + 1) % presets.Count;
                amb.PresetId = presets[currentIdx].id;
                amb.VolumeScale = presets[currentIdx].defaultVolume;
                amb.Spatialized = presets[currentIdx].spatial;
                amb.Radius = presets[currentIdx].defaultRadius;
            }
            y += 22f;

            GraphLabel(new Rect(innerX, y, 32f, 18f), "Vol:");
            string volTxt = GraphTextField(new Rect(innerX + 34f, y, 38f, 18f), amb.VolumeScale.ToString("0.00"));
            if (float.TryParse(volTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pVol))
                amb.VolumeScale = Mathf.Clamp(pVol, 0f, 2f);

            GraphLabel(new Rect(innerX + 80f, y, 42f, 18f), "FadeIn:");
            string inTxt = GraphTextField(new Rect(innerX + 124f, y, 34f, 18f), amb.FadeInDuration.ToString("0.0"));
            if (float.TryParse(inTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pIn))
                amb.FadeInDuration = Mathf.Clamp(pIn, 0f, 20f);

            GraphLabel(new Rect(innerX + 166f, y, 48f, 18f), "FadeOut:");
            string outTxt = GraphTextField(new Rect(innerX + 216f, y, 34f, 18f), amb.FadeOutDuration.ToString("0.0"));
            if (float.TryParse(outTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pOut))
                amb.FadeOutDuration = Mathf.Clamp(pOut, 0f, 20f);
            y += 22f;

            amb.Spatialized = GraphToggle(new Rect(innerX, y, 95f, 18f), amb.Spatialized, "Spatial 3D");
            if (amb.Spatialized)
            {
                GraphLabel(new Rect(innerX + 100f, y, 16f, 18f), "Q:");
                int.TryParse(GraphTextField(new Rect(innerX + 118f, y, 30f, 18f), amb.Q.ToString()), out amb.Q);
                GraphLabel(new Rect(innerX + 152f, y, 16f, 18f), "R:");
                int.TryParse(GraphTextField(new Rect(innerX + 170f, y, 30f, 18f), amb.R.ToString()), out amb.R);
                GraphLabel(new Rect(innerX + 204f, y, 42f, 18f), "Rayon:");
                string rTxt = GraphTextField(new Rect(innerX + 248f, y, 36f, 18f), amb.Radius.ToString("0.#"));
                if (float.TryParse(rTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pRad))
                    amb.Radius = Mathf.Clamp(pRad, 1f, 120f);
            }
            y += 22f;

            bool hasStart = !string.IsNullOrWhiteSpace(amb.StartSourceCardId);
            GUI.color = hasStart ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            GraphLabel(new Rect(innerX, y, Mathf.Max(40f, innerW - 26f), 18f),
                hasStart ? $"▶ In: ← {amb.StartSourceCardId}" : "(drag bas carte → entrée haut)");
            GUI.color = Color.white;
            if (hasStart && GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "⊘")) amb.StartSourceCardId = "";
            y += 20f;

            bool hasStop = !string.IsNullOrWhiteSpace(amb.StopSourceCardId);
            GUI.color = hasStop ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            GraphLabel(new Rect(innerX, y, Mathf.Max(40f, innerW - 26f), 18f),
                hasStop ? $"■ Out: ➔ {amb.StopSourceCardId}" : "(drag sortie bas → bas carte cible)");
            GUI.color = Color.white;
            if (hasStop && GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "⊘")) amb.StopSourceCardId = "";
            y += 22f;

            float halfBtn = (innerW - 6f) * 0.5f;
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.65f);
            if (GraphButton(new Rect(innerX, y, halfBtn, 20f), "▶ Tester"))
            {
                // Test fidèle : spatialisation + position hex réelles quand dispo
                // (repli 2D si aucune grille chargée ou hex introuvable : un test
                // spatial vers une position invalide serait un silence trompeur).
                Vector3 ambTestPos = Vector3.zero;
                bool ambTestSpatial = amb.Spatialized;
                if (ambTestSpatial)
                {
                    try
                    {
                        var testGrid = FindAnyObjectByType<TacticalHexGrid>();
                        var testNode = testGrid != null ? testGrid.GetNode(new HexCoordinates(amb.Q, amb.R)) : null;
                        if (testNode != null) ambTestPos = testNode.WorldPosition;
                        else ambTestSpatial = false;
                    }
                    catch { ambTestSpatial = false; }
                }
                KilltimeAudioManager.Instance?.PlaySceneAmbience(amb.AmbienceId, amb.PresetId, amb.VolumeScale, amb.FadeInDuration, ambTestSpatial, ambTestPos, amb.Radius);
            }
            GUI.backgroundColor = new Color(0.85f, 0.35f, 0.35f);
            if (GraphButton(new Rect(innerX + halfBtn + 6f, y, halfBtn, 20f), "■ Stopper"))
            {
                KilltimeAudioManager.Instance?.StopSceneAmbience(amb.AmbienceId, amb.FadeOutDuration);
            }
            GUI.backgroundColor = Color.white;

            // Port d'entrée (haut) : reçoit la connexion de départ (fil du bas d'une carte source).
            Vector2 inCenter = GetAmbienceInCenter(amb);
            _cachedAmbienceInputSockets[amb.AmbienceId] = inCenter;
            DrawGraphSolidRect(new Rect(inCenter.x - 7f, inCenter.y - 7f, 14f, 14f),
                hasStart ? AmbiencePortCol : new Color(AmbiencePortCol.r, AmbiencePortCol.g, AmbiencePortCol.b, 0.35f));

            // Port de sortie (bas) : tire un fil vers le bas d'une carte cible pour arrêter l'ambiance.
            Vector2 stopCenter = GetAmbienceStopCenter(amb);
            _cachedAmbienceStopSockets[amb.AmbienceId] = stopCenter;
            Rect stopPort = new Rect(stopCenter.x - 7f, stopCenter.y - 7f, 14f, 14f);
            Color stopCol = hasStop ? new Color(0.95f, 0.35f, 0.55f) : new Color(0.95f, 0.35f, 0.55f, 0.35f);
            DrawGraphSolidRect(stopPort, stopCol);

            if (GraphPrimaryDown(mouseWorld, stopPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsAmbienceStopLink = true,
                    SourceAmbienceTrack = amb,
                    StartPos = stopCenter
                };
                evt.Use();
            }
        }
    }
}
