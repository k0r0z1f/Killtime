using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Killtime.Core.Combat;

namespace Killtime.Audio
{
    /// <summary>KilltimeAudioManager — Generatif infini — stems, overrides, variations (partial, move-only).</summary>
    public partial class KilltimeAudioManager : MonoBehaviour
    {
        public List<ProceduralStemStatus> GetProceduralStemsStatus()
        {
            var stems = new List<ProceduralStemStatus>(8);
            float rms = GetActiveMusicRms();
            float t = (_activeMusic != null && _activeMusic.isPlaying) ? _activeMusic.time : Time.unscaledTime;

            if (_generativeCombat && IsGenerativeMood(_targetMood) && _genParamsValid)
            {
                var gp = _genParams;
                float bpm = Mathf.Max(60f, gp.bpm);
                float beat = t * (bpm / 60f);

                float cineStemDucking = Mathf.Clamp01(1f - 0.45f * _cinematicDuck);

                // 1. Batterie
                var drumOv = GetStemOverride(ProceduralStemType.Drums);
                bool isBreak = (gp.generation % 5 == 4) && gp.intensity01 < 0.85f;
                float drumGate = isBreak ? 0.25f : 1f;
                float drumVol = (drumOv == StemOverrideState.ForceActive) ? 0.85f : ((0.55f + 0.65f * gp.intensity01) * drumGate * cineStemDucking);
                bool drumsOn = drumOv == StemOverrideState.ForceActive || (drumOv == StemOverrideState.Auto && drumVol > 0.04f);
                if (drumOv == StemOverrideState.Muted) drumsOn = false;
                float drumPulse = drumsOn ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * Mathf.PI)), 3f) : 0f;
                string drumSub = isBreak ? $"Break ({gp.drumStyle})" : gp.drumStyle.ToString();
                if (_cinematicDuck > 0.1f) drumSub += " [Ciné-Duck]";
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Drums,
                    Name = "Batterie",
                    SubType = drumSub,
                    IsActive = drumsOn,
                    Intensity = drumsOn ? Mathf.Clamp01(drumVol) : 0f,
                    RhythmicPulse = Mathf.Clamp01(drumPulse * (0.6f + rms * 2.5f)),
                    Color = new Color(1.0f, 0.25f, 0.4f),
                    OverrideState = drumOv
                });

                // 2. Basse
                var bassOv = GetStemOverride(ProceduralStemType.Bass);
                float bassVol = (bassOv == StemOverrideState.ForceActive) ? 0.42f : (0.30f + 0.14f * gp.intensity01);
                bool bassOn = bassOv != StemOverrideState.Muted;
                bool bassGrowl = gp.intensity01 > 0.62f && gp.preset != GenerativeStylePreset.Retro16Bit;
                float bassPulse = bassOn ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * 2f * Mathf.PI)), 2.5f) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Bass,
                    Name = "Basse FM",
                    SubType = bassGrowl ? "Growl FM & Reese" : "Sub Clean / 808",
                    IsActive = bassOn,
                    Intensity = bassOn ? Mathf.Clamp01(bassVol * 1.5f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(bassPulse * (0.5f + rms * 3f)),
                    Color = new Color(0.2f, 0.9f, 0.3f),
                    OverrideState = bassOv
                });

                // 3. Pad Supersaw
                var padOv = GetStemOverride(ProceduralStemType.Pad);
                float padVol = (padOv == StemOverrideState.ForceActive) ? 0.32f : (0.20f + 0.10f * (1f - gp.intensity01) + 0.06f * gp.tension01);
                bool padOn = padOv != StemOverrideState.Muted;
                float padPulse = padOn ? (0.65f + 0.35f * Mathf.Sin(t * 0.75f)) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Pad,
                    Name = "Nappe (Pad)",
                    SubType = $"Supersaw (Bri {Mathf.RoundToInt(gp.brightness01 * 100)}%)",
                    IsActive = padOn,
                    Intensity = padOn ? Mathf.Clamp01(padVol * 2.2f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(padPulse * (0.7f + rms * 1.5f)),
                    Color = new Color(0.15f, 0.75f, 1.0f),
                    OverrideState = padOv
                });

                // 4. Arpège Euclidien
                var arpOv = GetStemOverride(ProceduralStemType.Arp);
                bool arpAuto = gp.intensity01 > 0.25f;
                bool arpOn = arpOv == StemOverrideState.ForceActive || (arpOv == StemOverrideState.Auto && arpAuto);
                if (arpOv == StemOverrideState.Muted) arpOn = false;
                float arpVol = arpOn ? (0.08f + 0.10f * gp.intensity01) : 0f;
                float arpPulse = arpOn ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * 4f * Mathf.PI)), 2f) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Arp,
                    Name = "Arpège",
                    SubType = arpOn ? "Euclidien 16-step" : "Mute (< 25% Énergie)",
                    IsActive = arpOn,
                    Intensity = arpOn ? Mathf.Clamp01(arpVol * 3.5f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(arpPulse * (0.6f + rms * 2f)),
                    Color = new Color(0.95f, 0.85f, 0.2f),
                    OverrideState = arpOv
                });

                // 5. Lead Soliste
                var leadOv = GetStemOverride(ProceduralStemType.Lead);
                bool leadAuto = gp.intensity01 > 0.45f || gp.tension01 > 0.5f;
                bool leadOn = leadOv == StemOverrideState.ForceActive || (leadOv == StemOverrideState.Auto && leadAuto);
                if (leadOv == StemOverrideState.Muted) leadOn = false;
                float leadVol = leadOn ? ((0.10f + 0.09f * gp.intensity01) * cineStemDucking) : 0f;
                float leadPulse = leadOn ? Mathf.Abs(Mathf.Sin(beat * 0.5f * Mathf.PI)) : 0f;
                string leadSub = leadOn ? (gp.preset == GenerativeStylePreset.Retro16Bit ? "Square 16-Bit" : "Call-Response Saw") : "Standby";
                if (_cinematicDuck > 0.1f && leadOn) leadSub += " [Ciné-Duck]";
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Lead,
                    Name = "Lead Synth",
                    SubType = leadSub,
                    IsActive = leadOn,
                    Intensity = leadOn ? Mathf.Clamp01(leadVol * 3f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(leadPulse * (0.5f + rms * 2.5f)),
                    Color = new Color(1.0f, 0.55f, 0.1f),
                    OverrideState = leadOv
                });

                // 6. Chœur Arcanotech
                var choirOv = GetStemOverride(ProceduralStemType.Choir);
                bool choirAuto = gp.tension01 > 0.45f && gp.preset != GenerativeStylePreset.Retro16Bit;
                bool choirOn = choirOv == StemOverrideState.ForceActive || (choirOv == StemOverrideState.Auto && choirAuto);
                if (choirOv == StemOverrideState.Muted) choirOn = false;
                float choirVol = choirOn ? (0.05f + 0.09f * gp.tension01) : 0f;
                float choirPulse = choirOn ? (0.6f + 0.4f * Mathf.Sin(t * 1.1f)) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Choir,
                    Name = "Chœur",
                    SubType = choirOn ? "Arcanotech Vox Bed" : "Inactif (Tension < 45%)",
                    IsActive = choirOn,
                    Intensity = choirOn ? Mathf.Clamp01(choirVol * 4f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(choirPulse * (0.7f + rms * 1.5f)),
                    Color = new Color(0.8f, 0.45f, 1.0f),
                    OverrideState = choirOv
                });

                // 7. Staccato / Djent
                var staccOv = GetStemOverride(ProceduralStemType.Staccato);
                bool staccAuto = gp.intensity01 > 0.35f;
                bool staccOn = staccOv == StemOverrideState.ForceActive || (staccOv == StemOverrideState.Auto && staccAuto);
                if (staccOv == StemOverrideState.Muted) staccOn = false;
                float staccVol = staccOn ? (0.10f + 0.10f * gp.intensity01) : 0f;
                float staccPulse = staccOn ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * 2f * Mathf.PI + 0.5f)), 3f) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Staccato,
                    Name = "Staccato",
                    SubType = staccOn ? "Strings & Chug" : "Mute (< 35% Énergie)",
                    IsActive = staccOn,
                    Intensity = staccOn ? Mathf.Clamp01(staccVol * 3f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(staccPulse * (0.5f + rms * 2.5f)),
                    Color = new Color(0.9f, 0.3f, 0.6f),
                    OverrideState = staccOv
                });

                // 8. Sub & Shimmer
                var subAirOv = GetStemOverride(ProceduralStemType.SubAir);
                bool subAirOn = subAirOv != StemOverrideState.Muted;
                float subPulse = subAirOn ? (0.8f + 0.2f * Mathf.Sin(t * 0.4f)) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.SubAir,
                    Name = "Sub & Air",
                    SubType = $"{gp.rootMidi} MIDI / Shimmer",
                    IsActive = subAirOn,
                    Intensity = subAirOn ? (0.5f + 0.3f * gp.tension01) : 0f,
                    RhythmicPulse = Mathf.Clamp01(subPulse * (0.8f + rms * 1.2f)),
                    Color = new Color(0.0f, 0.85f, 0.95f),
                    OverrideState = subAirOv
                });
            }
            else
            {
                // Mode Standard / Préfaites (Explore, Tension, Combat T0-T2, Boss, Victory, Defeat)
                int level = _currentLevel;
                float bpm = (_targetMood == MusicMood.Explore || _targetMood == MusicMood.Tension || _targetMood == MusicMood.Defeat) ? 60f : 120f;
                float beat = t * (bpm / 60f);

                bool drumsActive = level >= 1 && (_targetMood == MusicMood.Combat || _targetMood == MusicMood.CombatBoss || _targetMood == MusicMood.Victory);
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Batterie",
                    SubType = drumsActive ? (_targetMood == MusicMood.CombatBoss ? "War Taikos" : "Production Drums") : "Mute (Niveau 0)",
                    IsActive = drumsActive,
                    Intensity = drumsActive ? (0.4f + level * 0.3f) : 0f,
                    RhythmicPulse = drumsActive ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * Mathf.PI)), 2.5f) : 0f,
                    Color = new Color(1.0f, 0.25f, 0.4f)
                });

                bool bassActive = level >= 1 && (_targetMood == MusicMood.Combat || _targetMood == MusicMood.CombatBoss || _targetMood == MusicMood.Victory);
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Basse",
                    SubType = bassActive ? (_targetMood == MusicMood.Combat && _currentTrackIndex == 2 ? "Neuro Cyber Bass" : "Master Bassline") : "Mute",
                    IsActive = bassActive,
                    Intensity = bassActive ? (0.4f + level * 0.28f) : 0f,
                    RhythmicPulse = bassActive ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * 2f * Mathf.PI)), 2f) : 0f,
                    Color = new Color(0.2f, 0.9f, 0.3f)
                });

                stems.Add(new ProceduralStemStatus
                {
                    Name = "Harmonie (Pad)",
                    SubType = $"{_targetMood} Chords",
                    IsActive = true,
                    Intensity = 0.6f + level * 0.2f,
                    RhythmicPulse = 0.7f + 0.3f * Mathf.Sin(t * 0.8f),
                    Color = new Color(0.15f, 0.75f, 1.0f)
                });

                bool arpActive = level >= 1 && (_targetMood == MusicMood.Combat || _targetMood == MusicMood.CombatBoss || _targetMood == MusicMood.Victory);
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Arpège",
                    SubType = arpActive ? "Master Arp Echo" : "Mute",
                    IsActive = arpActive,
                    Intensity = arpActive ? (0.35f + level * 0.3f) : 0f,
                    RhythmicPulse = arpActive ? Mathf.Abs(Mathf.Sin(beat * 4f * Mathf.PI)) : 0f,
                    Color = new Color(0.95f, 0.85f, 0.2f)
                });

                bool leadActive = _targetMood == MusicMood.CombatBoss && level >= 1;
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Lead Acid",
                    SubType = leadActive ? "Cybernetic Acid Lead" : "Inactif",
                    IsActive = leadActive,
                    Intensity = leadActive ? (0.45f + level * 0.25f) : 0f,
                    RhythmicPulse = leadActive ? Mathf.Abs(Mathf.Sin(beat * Mathf.PI)) : 0f,
                    Color = new Color(1.0f, 0.55f, 0.1f)
                });

                bool choirActive = _targetMood == MusicMood.CombatBoss;
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Chœur",
                    SubType = choirActive ? "Arcanotech Choir (Boss)" : "Inactif",
                    IsActive = choirActive,
                    Intensity = choirActive ? (0.5f + level * 0.25f) : 0f,
                    RhythmicPulse = choirActive ? (0.6f + 0.4f * Mathf.Sin(t * 1.2f)) : 0f,
                    Color = new Color(0.8f, 0.45f, 1.0f)
                });

                bool staccActive = _targetMood == MusicMood.Combat && (_currentTrackIndex == 0 || _currentTrackIndex == 1);
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Staccato / Clang",
                    SubType = staccActive ? "Pulse Strings & Anvil" : (_targetMood == MusicMood.Combat && _currentTrackIndex == 2 ? "Djent Chug" : "Mute"),
                    IsActive = staccActive || (_targetMood == MusicMood.Combat && _currentTrackIndex == 2),
                    Intensity = staccActive ? 0.65f : 0f,
                    RhythmicPulse = staccActive ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * 2f * Mathf.PI)), 2.5f) : 0f,
                    Color = new Color(0.9f, 0.3f, 0.6f)
                });

                bool heartActive = _targetMood == MusicMood.Tension && level >= 1;
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Sub / Atmos",
                    SubType = heartActive ? "Heartbeat & Riser" : (_targetMood == MusicMood.Explore ? "Sub & Shimmer" : "Sub Rumble"),
                    IsActive = true,
                    Intensity = 0.6f,
                    RhythmicPulse = heartActive ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * Mathf.PI)), 4f) : (0.7f + 0.3f * Mathf.Sin(t * 0.5f)),
                    Color = new Color(0.0f, 0.85f, 0.95f)
                });
            }

            return stems;
        }

        // --- État génératif ∞ ---
        private int _genGeneration = 0;
        private GenerativeMusicParams _genParams;
        private bool _genParamsValid = false;
        private float _genLoopTimer = 0f;
        private float _genLoopDur = 16f;
        private float _pendingStrike = 0f;
        private float _lastAllyHp = 1f;
        private int _lastAllies = 3;
        private int _lastEnemies = 2;
        private int _lastTurn = 0;
        private int _stemOverrides = 0;

        public int StemOverridesMask => _stemOverrides;

        public StemOverrideState GetStemOverride(ProceduralStemType stem)
        {
            return (StemOverrideState)((_stemOverrides >> ((int)stem * 2)) & 0x3);
        }

        public void SetStemOverride(ProceduralStemType stem, StemOverrideState state)
        {
            int shift = (int)stem * 2;
            _stemOverrides = (_stemOverrides & ~(0x3 << shift)) | (((int)state & 0x3) << shift);
            ApplyStemOverridesAndRefresh();
        }

        public void ResetAllStemOverrides()
        {
            if (_stemOverrides == 0) return;
            _stemOverrides = 0;
            ApplyStemOverridesAndRefresh();
        }

        private void ApplyStemOverridesAndRefresh()
        {
            if (_generativeCombat && IsGenerativeMood(_targetMood))
            {
                _genParams.stemOverrides = _stemOverrides;
                RequestGenerativeVariation();
            }
            else if (_targetMood != MusicMood.None)
            {
                PlayMusic(_targetMood, _currentTrackIndex, _targetIntensity01, forceRestart: true);
            }
        }

        public bool GenerativeCombatEnabled => _generativeCombat;
        public GenerativeStylePreset GenerativePreset => _generativePreset;
        public float GenerativeBrightness => _generativeBrightness;
        public float GenerativeChaos => _generativeChaos;
        public int GenerativeGeneration => _genGeneration;
        public GenerativeMusicParams CurrentGenerativeParams => _genParams;
        public float GenerativeLoopProgress => _genLoopDur > 0f ? Mathf.Clamp01(_genLoopTimer / _genLoopDur) : 0f;

        // =====================================================================
        // MODE GÉNÉRATIF ∞ — composition live sans chanson préfaite
        // =====================================================================
        public static bool IsGenerativeMood(MusicMood mood)
            => mood == MusicMood.Combat || mood == MusicMood.CombatBoss || mood == MusicMood.Tension;

        /// <summary>Active/coupe le mode génératif. Les chansons préfaites restent intactes.</summary>
        public void SetGenerativeMode(bool on, int seed = -1)
        {
            bool wasOn = _generativeCombat;
            _generativeCombat = on;
            if (seed >= 0) _generativeSeed = seed;
            if (on && !wasOn)
            {
                _genGeneration = 0;
                _genParamsValid = false;
                _genLoopTimer = 0f;
                _pendingStrike = 0f;
                // Rebranche immédiatement le mood courant en génératif s'il s'y prête.
                if (IsGenerativeMood(_targetMood))
                    StartGenerativeLoop(_targetMood, _targetIntensity01, forceRestart: true);
            }
            else if (!on && wasOn)
            {
                _genParamsValid = false;
                _genLoopTimer = 0f;
                _pendingStrike = 0f;
                // Retour aux chansons préfaites sur le mood courant.
                if (IsGenerativeMood(_targetMood))
                    PlayMusic(_targetMood, _currentTrackIndex, _targetIntensity01, forceRestart: true);
            }
        }

        public void SetGenerativeColor(float brightness01, float chaos01)
        {
            _generativeBrightness = Mathf.Clamp01(brightness01);
            _generativeChaos = Mathf.Clamp01(chaos01);
        }

        /// <summary>Impose un style au moteur génératif (Auto = défaut actuel).
        /// Changer de style régénère aussitôt une variation dans le nouveau style.</summary>
        public void SetGenerativePreset(GenerativeStylePreset preset)
        {
            if (_generativePreset == preset) return;
            _generativePreset = preset;
            if (_generativeCombat && IsGenerativeMood(_targetMood))
                RequestGenerativeVariation();
        }

        /// <summary>
        /// Accent réactif : à appeler sur coup critique / kill / gros dégât.
        /// Le sub-drop + taiko sont cousés dans la variation SUIVANTE (barre 4),
        /// donc le coup "colle" musicalement au lieu de juste empiler un SFX.
        /// </summary>
        public void NotifyCombatStrike(bool critical, bool kill, float amount01 = -1f)
        {
            float s = amount01 >= 0f ? Mathf.Clamp01(amount01) : (kill ? 1f : (critical ? 0.75f : 0.4f));
            _pendingStrike = Mathf.Max(_pendingStrike, s);
        }

        /// <summary>Force immédiatement une variation inédite (bouton UI / rotation).</summary>
        public void RequestGenerativeVariation()
        {
            if (!IsGenerativeMood(_targetMood))
            {
                PlayMusic(MusicMood.Combat, _targetIntensity01, forceRestart: true);
                return;
            }
            _genGeneration++;
            float strike = _pendingStrike;
            _pendingStrike = 0f;
            _genParams = AdaptiveMusicDirector.ComputeGenerativeParams(
                _lastAllyHp, _lastAllies, _lastEnemies, _lastTurn,
                _generativeSeed, _genGeneration, strike, _generativeBrightness, _generativeChaos, _generativePreset);
            _genParams.stemOverrides = _stemOverrides;
            _genParamsValid = true;
            _genLoopDur = ProceduralAudioFactory.EstimateGenerativeDuration(_genParams);
            _genLoopTimer = 0f;
            _targetIntensity01 = _genParams.intensity01;
            _lastAdaptiveSwitchTime = Time.unscaledTime;
            AudioClip clip;
            try { clip = ProceduralAudioFactory.GetGenerativeLoop(_genParams); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Audio] Génératif G{_genGeneration} impossible : {ex.GetType().Name}: {ex.Message}");
                return;
            }
            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            _musicRoutine = StartCoroutine(CrossfadeClipRoutine(clip, _targetMood, _genGeneration, _genParams.intensity01));
            Debug.Log($"[Audio] ∞ Génératif {_targetMood} G{_genGeneration} : {clip.samples} spl ({clip.length:F1}s) bpm={_genParams.bpm:0} sty={_genParams.drumStyle} gam={_genParams.scale} int={_genParams.intensity01:F2} ten={_genParams.tension01:F2} strike={strike:F2}");
        }

        private void StartGenerativeLoop(MusicMood mood, float intensity01, bool forceRestart)
        {
            bool sameMood = (mood == _targetMood);
            _targetMood = mood;
            _targetIntensity01 = Mathf.Clamp01(intensity01);
            if (!sameMood || !_genParamsValid || forceRestart)
            {
                if (!sameMood || forceRestart) { _genGeneration = forceRestart && sameMood ? _genGeneration + 1 : 0; }
                float strike = _pendingStrike;
                _pendingStrike = 0f;
                _genParams = AdaptiveMusicDirector.ComputeGenerativeParams(
                    _lastAllyHp, _lastAllies, _lastEnemies, _lastTurn,
                    _generativeSeed, _genGeneration, strike, _generativeBrightness, _generativeChaos, _generativePreset);
                _genParams.stemOverrides = _stemOverrides;
                // L'intensité de référence vient de l'état tactique, pas du paramètre d'appel,
                // pour que le BPM/gamme suivent le vrai danger (l'appel ne donne qu'une base).
                if (!sameMood && _genParamsValid) { /* garde la continuité */ }
                _genParamsValid = true;
                _genLoopDur = ProceduralAudioFactory.EstimateGenerativeDuration(_genParams);
                _genLoopTimer = 0f;
                _targetIntensity01 = _genParams.intensity01;
                _currentLevel = (int)AdaptiveMusicDirector.Quantize(_targetIntensity01);
                _lastAdaptiveSwitchTime = Time.unscaledTime;
                AudioClip clip;
                try { clip = ProceduralAudioFactory.GetGenerativeLoop(_genParams); }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Audio] Génératif {mood} G{_genGeneration} impossible : {ex.GetType().Name}: {ex.Message}");
                    return;
                }
                if (_musicRoutine != null) StopCoroutine(_musicRoutine);
                _musicRoutine = StartCoroutine(CrossfadeClipRoutine(clip, mood, _genGeneration, _genParams.intensity01));
                Debug.Log($"[Audio] ∞ Génératif {mood} G{_genGeneration} : {clip.samples} spl ({clip.length:F1}s) bpm={_genParams.bpm:0} sty={_genParams.drumStyle} gam={_genParams.scale}");
            }
            else
            {
                _targetIntensity01 = Mathf.Clamp01(intensity01);
            }
        }

        private void AdvanceGenerativeVariation()
        {
            // Garde-fou : une seule transition à la fois.
            if (_isFading) return;
            _genGeneration++;
            float strike = _pendingStrike;
            _pendingStrike = 0f;
            _genParams = AdaptiveMusicDirector.ComputeGenerativeParams(
                _lastAllyHp, _lastAllies, _lastEnemies, _lastTurn,
                _generativeSeed, _genGeneration, strike, _generativeBrightness, _generativeChaos, _generativePreset);
            _genParams.stemOverrides = _stemOverrides;
            _genLoopDur = ProceduralAudioFactory.EstimateGenerativeDuration(_genParams);
            _genLoopTimer = 0f;
            _targetIntensity01 = _genParams.intensity01;
            _currentLevel = (int)AdaptiveMusicDirector.Quantize(_targetIntensity01);
            _lastAdaptiveSwitchTime = Time.unscaledTime;
            AudioClip clip;
            try { clip = ProceduralAudioFactory.GetGenerativeLoop(_genParams); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Audio] Génératif advance G{_genGeneration} impossible : {ex.GetType().Name}: {ex.Message}");
                return;
            }
            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            _musicRoutine = StartCoroutine(CrossfadeClipRoutine(clip, _targetMood, _genGeneration, _genParams.intensity01));
        }
    }
}
