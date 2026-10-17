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
                    if (string.IsNullOrWhiteSpace(snd.SoundId)) snd.SoundId = MakeUniqueSoundId();
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
                    if (string.IsNullOrWhiteSpace(amb.AmbienceId)) amb.AmbienceId = MakeUniqueAmbienceId();
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
    }
}
