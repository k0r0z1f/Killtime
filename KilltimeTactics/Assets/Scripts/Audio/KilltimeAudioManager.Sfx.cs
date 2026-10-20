using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Killtime.Core.Combat;

namespace Killtime.Audio
{
    /// <summary>KilltimeAudioManager — SFX — Play/PlayAt/CombatResult/Grenades + pools (partial, move-only).</summary>
    public partial class KilltimeAudioManager : MonoBehaviour
    {
        // =====================================================================
        // API principale
        // =====================================================================
        public void Play(SoundId id, float volumeScale = 1f, float pitchScale = 1f)
        {
            var def = Resolve(id);
            if (!CheckCooldown(def)) return;
            var clip = PickClip(def);
            if (clip == null) return;
            var src = Next2D();
            ConfigureAndPlay(src, def, clip, volumeScale, pitchScale, Vector3.zero, false);
        }

        public AudioSource PlayUI(SoundId id, float volumeScale = 1f)
        {
            var def = Resolve(id);
            if (!CheckCooldown(def)) return null;
            var clip = PickClip(def);
            if (clip == null) return null;
            var src = Next2D(isUI: true);
            ConfigureAndPlay(src, def, clip, volumeScale, 1f, Vector3.zero, false, forceCategory: SoundCategory.UI);
            return src;
        }

        /// <summary>
        /// Lecture d'un clip de banque (recette JSON) : 2D non-spatial, sans jitter
        /// de pitch (les swells longs ne doivent pas varier). Le volume de la
        /// recette est déjà combiné par l'appelant.
        /// </summary>
        public AudioSource PlayBankUI(string bankId, AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || string.IsNullOrWhiteSpace(bankId)) return null;
            var def = new SoundDefinition
            {
                Category = SoundCategory.Cinematic,
                Volume = 1f,
                PitchMin = 1f,
                PitchMax = 1f,
                Priority = 128,
                Loop = false,
                SpatialBlend = 0f
            };
            var src = Next2D(isUI: true);
            ConfigureAndPlay(src, def, clip, volumeScale, 1f, Vector3.zero, false, forceCategory: SoundCategory.UI);
            return src;
        }

        /// <summary>
        /// Lecture d'un son de carte scène 🔊 (contrôle total) : pitch, loop, fades,
        /// priorité, spatialisation 3D optionnelle. Sans jitter de pitch (les swells
        /// longs ne doivent pas varier). Si instanceId est fourni, la source est
        /// suivie (stop explicite, monitor, kill-switch) et rejeuée par-dessus
        /// l'instance précédente (stop immédiat de l'ancienne, sans fade).
        /// Le volume de la recette est déjà combiné par l'appelant.
        /// </summary>
        public AudioSource PlayBankSound(string bankId, AudioClip clip, float volumeScale = 1f,
            float pitchScale = 1f, bool loop = false, float fadeInDuration = 0f, int priority = 128,
            bool spatial = false, Vector3 worldPos = default, float radius = 15f, string instanceId = null)
        {
            if (clip == null || string.IsNullOrWhiteSpace(bankId)) return null;
            pitchScale = Mathf.Clamp(pitchScale <= 0f ? 1f : pitchScale, 0.25f, 3f);
            priority = Mathf.Clamp(priority, 0, 255);
            var def = new SoundDefinition
            {
                Category = SoundCategory.Cinematic,
                Volume = 1f,
                PitchMin = 1f,
                PitchMax = 1f,
                Priority = priority,
                Loop = loop,
                SpatialBlend = spatial ? 1f : 0f,
                MaxDistance = Mathf.Max(radius, 4f)
            };
            AudioSource src;
            if (spatial)
            {
                src = Next3D();
                src.transform.position = worldPos;
            }
            else
            {
                src = Next2D(isUI: true);
            }
            ConfigureAndPlay(src, def, clip, volumeScale, pitchScale, worldPos, spatial, forceCategory: SoundCategory.UI);
            if (spatial && src != null)
            {
                // Distances utiles aux cartes scène (le pool 3D standard vise
                // 18m/85m, trop large pour un rayon de carte de 15m par défaut).
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = Mathf.Max(1.5f, radius * 0.25f);
                src.maxDistance = Mathf.Max(radius, 4f);
                src.transform.position = worldPos;
            }
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                string key = instanceId.Trim();
                StopTrackedSceneSound(key, immediate: true);
                _activeSceneSoundSources[key] = src;
                if (fadeInDuration > 0.05f && !Mathf.Approximately(src.volume, 0f))
                {
                    float target = src.volume;
                    src.volume = 0f;
                    _activeSceneSoundFades[key] = StartCoroutine(FadeSceneSoundRoutine(key, src, target, fadeInDuration, destroyOnComplete: false));
                }
            }
            else if (fadeInDuration > 0.05f)
            {
                // Sans suivi : fade-in simple sur la source du pool (pas de stop possible).
                var captured = src;
                float target = src.volume;
                src.volume = 0f;
                StartCoroutine(FadePoolSourceRoutine(captured, target, fadeInDuration));
            }
            return src;
        }

        /// <summary>Stoppe une instance de son de scène suivie (fade-out, puis stop).</summary>
        public void StopSceneSound(string instanceId, float fadeOutDuration = 0.3f)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return;
            string key = instanceId.Trim();
            if (!_activeSceneSoundSources.TryGetValue(key, out var src) || src == null)
            {
                _activeSceneSoundSources.Remove(key);
                return;
            }
            // Entrée fossile (one-shot terminé naturellement, sans timer) : purger
            // sans toucher la source (déjà réutilisable par les pools).
            if (!src.isPlaying && !_activeSceneSoundFades.ContainsKey(key))
            {
                _activeSceneSoundSources.Remove(key);
                return;
            }
            if (_activeSceneSoundFades.TryGetValue(key, out var activeRoutine) && activeRoutine != null)
                StopCoroutine(activeRoutine);
            if (fadeOutDuration <= 0.05f)
            {
                StopTrackedSceneSound(key, immediate: true);
                return;
            }
            _activeSceneSoundFades[key] = StartCoroutine(FadeSceneSoundRoutine(key, src, 0f, fadeOutDuration, destroyOnComplete: true));
        }

        /// <summary>Stoppe tous les sons de scène suivis (kill-switch transitions).</summary>
        public void StopAllSceneSounds(float fadeOutDuration = 0.3f)
        {
            var keys = new List<string>(_activeSceneSoundSources.Keys);
            for (int i = 0; i < keys.Count; i++)
                StopSceneSound(keys[i], fadeOutDuration);
        }

        /// <summary>Clés des sons de scène suivis (monitor éditeur).</summary>
        public List<string> GetActiveSceneSoundKeys() => new List<string>(_activeSceneSoundSources.Keys);

        /// <summary>Clés des ambiances suivies (monitor éditeur).</summary>
        public List<string> GetActiveSceneAmbienceKeys() => new List<string>(_activeSceneAmbienceSources.Keys);

        private void StopTrackedSceneSound(string key, bool immediate)
        {
            if (_activeSceneSoundFades.TryGetValue(key, out var routine) && routine != null)
            {
                try { StopCoroutine(routine); } catch { /* ignore */ }
                _activeSceneSoundFades.Remove(key);
            }
            if (_activeSceneSoundSources.TryGetValue(key, out var src) && src != null && immediate)
            {
                try { src.Stop(); } catch { /* ignore */ }
            }
            if (immediate) _activeSceneSoundSources.Remove(key);
        }

        private IEnumerator FadeSceneSoundRoutine(string key, AudioSource src, float targetVol, float duration, bool destroyOnComplete)
        {
            if (src == null) yield break;
            float startVol = src.volume;
            float elapsed = 0f;
            duration = Mathf.Max(0.05f, duration);
            while (elapsed < duration && src != null)
            {
                elapsed += Time.unscaledDeltaTime;
                src.volume = Mathf.Lerp(startVol, targetVol, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            if (src != null)
            {
                src.volume = targetVol;
                if (destroyOnComplete)
                {
                    try { src.Stop(); } catch { /* ignore */ }
                    _activeSceneSoundSources.Remove(key);
                }
            }
            _activeSceneSoundFades.Remove(key);
        }

        private static IEnumerator FadePoolSourceRoutine(AudioSource src, float targetVol, float duration)
        {
            if (src == null) yield break;
            float elapsed = 0f;
            duration = Mathf.Max(0.05f, duration);
            while (elapsed < duration && src != null)
            {
                elapsed += Time.unscaledDeltaTime;
                src.volume = Mathf.Lerp(0f, targetVol, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            if (src != null) src.volume = targetVol;
        }

        public void PlayAt(SoundId id, Vector3 worldPos, float volumeScale = 1f, float pitchScale = 1f)
        {
            var def = Resolve(id);
            if (!CheckCooldown(def)) return;
            var clip = PickClip(def);
            if (clip == null) return;
            // Sons UI et musique restent 2D même via PlayAt.
            bool is3D = def.SpatialBlend > 0.01f || def.Category == SoundCategory.SFX || def.Category == SoundCategory.Cinematic;
            var src = is3D ? Next3D() : Next2D();
            if (is3D) src.transform.position = worldPos;
            ConfigureAndPlay(src, def, clip, volumeScale, pitchScale, worldPos, is3D);
        }

        /// <summary>
        /// Résolution audio directe depuis la structure DamageResult officielle (Livre V).
        /// Automatise les accents percussifs et l'élévation de tension des stems procéduraux.
        /// </summary>
        public void PlayCombatResult(DamageResult result, Vector3 atPos)
        {
            if (_generativeCombat && result.IsHit)
            {
                bool isKill = result.FatalResolution == FatalBlowResolution.InstantDeath;
                float strikeWeight = isKill ? 1.0f : (result.IsCritical ? 0.85f : (result.ExceededEncaissement ? 0.65f : 0.4f));
                NotifyCombatStrike(result.IsCritical, isKill, strikeWeight);
            }

            PlayCombatResult(
                result.IsHit,
                result.IsBlocked,
                result.IsCritical,
                result.ArmorAbsorbed > 0,
                result.ExceededEncaissement,
                result.FatalResolution != FatalBlowResolution.None ? result.FatalResolution.ToString() : null,
                atPos
            );
        }

        /// <summary>Helper combat legacy : mappe un DamageResult vers les bons SFX (appelé par l'arène).</summary>
        public void PlayCombatResult(bool isHit, bool isBlocked, bool isCritical, bool armorAbsorbed,
            bool shock, string fatalResolution, Vector3 atPos)
        {
            // Voie générative : chaque coup marquant injecte un accent dans la musique.
            if (_generativeCombat && isHit)
            {
                bool kill = !string.IsNullOrEmpty(fatalResolution) && fatalResolution != "None"
                    && fatalResolution != "MiracleSaved" && fatalResolution != "EligibleForLastBreath";
                NotifyCombatStrike(isCritical, kill);
            }
            if (!isHit && isBlocked) { PlayAt(SoundId.Defense_Parry, atPos); return; }
            if (!isHit) { PlayAt(SoundId.Attack_Miss, atPos); PlayAt(SoundId.Defense_Dodge, atPos, 0.6f); return; }

            PlayAt(isCritical ? SoundId.Attack_Impact_Crit : SoundId.Attack_Impact_Hit, atPos);
            PlayAt(SoundId.Impact_DeepBoom, atPos, isCritical ? 0.8f : 0.35f);
            if (armorAbsorbed) PlayAt(SoundId.Armor_Absorb, atPos, 0.7f);
            else PlayAt(isCritical ? SoundId.Hurt_Heavy : SoundId.Hurt_Light, atPos, 0.8f);
            if (shock) PlayAt(SoundId.Trauma_Shock, atPos, 0.8f);

            if (!string.IsNullOrEmpty(fatalResolution) && fatalResolution != "None")
            {
                switch (fatalResolution)
                {
                    case "InstantDeath": PlayAt(SoundId.Death_Instant, atPos); PlayAt(SoundId.KO_Fall, atPos, 0.8f); break;
                    case "MiracleSaved": PlayAt(SoundId.Miracle_Saved, atPos); break;
                    case "ForcedUnconscious": PlayAt(SoundId.KO_Fall, atPos); break;
                    case "EligibleForLastBreath": PlayAt(SoundId.LastBreath, atPos); break;
                }
            }
        }

        /// <summary>
        /// Séquence sonore complète d'une grenade : goupille -&gt; lancer (ou thump lanceur) -&gt;
        /// rebond -&gt; détonation (frag lourde / flash / fumée) + shrapnels. Appelé par l'arène/FX.
        /// kind : GrenadeKind brut ("Flash", "Fumigene", "Gaz", ...). heavy = gros calibre / thermobarique / plasma.
        /// </summary>
        public void PlayGrenadeThrow(Vector3 fromPos, bool withLauncher)
        {
            PlayAt(SoundId.Grenade_Pin, fromPos, 0.7f);
            if (withLauncher) PlayAt(SoundId.Launcher_Thump, fromPos, 1f);
            else PlayAt(SoundId.Grenade_Throw, fromPos, 0.8f);
        }

        public void NotifyGrenadeStrike(bool heavy)
        {
            if (_generativeCombat) NotifyCombatStrike(false, heavy, heavy ? 0.9f : 0.6f);
        }

        public void PlayGrenadeDetonation(Vector3 atPos, string kind, bool heavy)
        {
            string k = (kind ?? "").ToLowerInvariant();
            bool isFlash = k.Contains("flash") || k.Contains("stun") || k.Contains("sonique");
            bool isSmoke = k.Contains("fumi") || k.Contains("smoke");
            bool isGas = k.Contains("gaz") || k.Contains("gas");
            if (isFlash) { PlayAt(SoundId.Grenade_Flash, atPos, 1f); PlayAt(SoundId.Impact_DeepBoom, atPos, 0.4f); NotifyGrenadeStrike(false); return; }
            if (isSmoke || isGas) { PlayAt(SoundId.Grenade_Smoke, atPos, 1f); PlayAt(SoundId.Grenade_Gas, atPos, 0.8f); return; }
            PlayAt(heavy ? SoundId.Grenade_Explosion_Heavy : SoundId.Grenade_Explosion_Frag, atPos, 1f);
            PlayAt(SoundId.Grenade_Shrapnel, atPos, 0.7f);
            PlayAt(SoundId.Impact_DeepBoom, atPos, heavy ? 0.9f : 0.5f);
            NotifyGrenadeStrike(heavy);
        }

        // =====================================================================
        // Internes
        // =====================================================================
        private void BuildPools()
        {
            var root2D = new GameObject("Pool2D") { hideFlags = HideFlags.DontSave };
            root2D.transform.SetParent(transform, false);
            for (int i = 0; i < _pool2DSize; i++)
            {
                var src = root2D.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.dopplerLevel = 0f;
                _pool2D.Add(src);
            }

            var root3D = new GameObject("Pool3D") { hideFlags = HideFlags.DontSave };
            root3D.transform.SetParent(transform, false);
            for (int i = 0; i < _pool3DSize; i++)
            {
                var go = new GameObject($"SFX3D_{i:00}");
                go.transform.SetParent(root3D.transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0.35f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 18f;
                src.maxDistance = 85f;
                src.dopplerLevel = 0f;
                _pool3D.Add(src);
            }

            var musicGo = new GameObject("Music") { hideFlags = HideFlags.DontSave };
            musicGo.transform.SetParent(transform, false);
            _musicA = musicGo.AddComponent<AudioSource>();
            _musicB = musicGo.AddComponent<AudioSource>();
            foreach (var m in new[] { _musicA, _musicB })
            {
                m.playOnAwake = false;
                m.loop = true;
                m.spatialBlend = 0f;
                m.dopplerLevel = 0f;
                m.volume = 0f;
            }

            var ambGo = new GameObject("Ambience");
            ambGo.transform.SetParent(transform, false);
            _ambienceSource = ambGo.AddComponent<AudioSource>();
            _ambienceSource.playOnAwake = false;
            _ambienceSource.loop = true;
            _ambienceSource.spatialBlend = 0f;
            _ambienceSource.volume = 0.4f;
        }

        private AudioSource Next2D(bool isUI = false)
        {
            for (int i = 0; i < _pool2D.Count; i++)
            {
                var s = _pool2D[(_cursor2D + i) % _pool2D.Count];
                if (!s.isPlaying)
                {
                    _cursor2D = (_cursor2D + i + 1) % _pool2D.Count;
                    s.ignoreListenerPause = isUI;
                    return s;
                }
            }
            var src = _pool2D[_cursor2D % _pool2D.Count];
            _cursor2D++;
            src.ignoreListenerPause = isUI;
            return src;
        }

        private AudioSource Next3D()
        {
            for (int i = 0; i < _pool3D.Count; i++)
            {
                var s = _pool3D[(_cursor3D + i) % _pool3D.Count];
                if (!s.isPlaying)
                {
                    _cursor3D = (_cursor3D + i + 1) % _pool3D.Count;
                    return s;
                }
            }
            var src = _pool3D[_cursor3D % _pool3D.Count];
            _cursor3D++;
            return src;
        }
    }
}
