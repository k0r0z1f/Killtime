using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>Partiel Génératif infini (délègue à GenerativeMusicEngine, cache LRU fenêtré + pré-rendu asynchrone Zéro-Hitch).</summary>
    public static partial class ProceduralAudioFactory
    {
        // =====================================================================
        // MODE GÉNÉRATIF INFINI (sans chanson préfaite) — ne touche pas aux
        // chansons legacy ci-dessus. Chaque appel avec generation+1 donne une
        // variation inédite mais cohérente (même seed => même famille).
        // =====================================================================
        private static readonly object _genTaskLock = new();
        private static readonly Dictionary<string, Task<float[]>> _generativePendingTasks = new();
        private static readonly Queue<string> _generativeTaskLru = new();

        public static float EstimateGenerativeDuration(GenerativeMusicParams p)
            => GenerativeMusicEngine.EstimateDuration(p);

        /// <summary>
        /// Lance la synthèse C# pure (Render) sur un thread de fond pendant la lecture de la boucle courante.
        /// Quand GetGenerativeLoop est appelé au moment du crossfade, le tableau float[] est déjà prêt :
        /// seul AudioClip.Create + SetData (< 0.2 ms) s'exécute sur le Main Thread Unity.
        /// </summary>
        public static void PrewarmGenerativeLoopAsync(GenerativeMusicParams p)
        {
            string key = GenerativeKey(p);
            if (_generativeCache.TryGetValue(key, out var cached) && IsClipUsable(cached)) return;
            lock (_genTaskLock)
            {
                if (_generativePendingTasks.ContainsKey(key)) return;
                GenerativeMusicParams copy = p;
                _generativePendingTasks[key] = Task.Run(() => GenerativeMusicEngine.Render(copy));
                _generativeTaskLru.Enqueue(key);
                while (_generativeTaskLru.Count > 4)
                {
                    var old = _generativeTaskLru.Dequeue();
                    if (old != key) _generativePendingTasks.Remove(old);
                }
            }
        }

        public static bool IsGenerativeLoopPrewarmed(GenerativeMusicParams p)
        {
            string key = GenerativeKey(p);
            if (_generativeCache.TryGetValue(key, out var cached) && IsClipUsable(cached)) return true;
            lock (_genTaskLock)
            {
                return _generativePendingTasks.TryGetValue(key, out var task) && task.IsCompletedSuccessfully;
            }
        }

        public static AudioClip GetGenerativeLoop(GenerativeMusicParams p)
        {
            string key = GenerativeKey(p);
            if (_generativeCache.TryGetValue(key, out var cached) && IsClipUsable(cached)) return cached;

            Task<float[]> pendingTask = null;
            lock (_genTaskLock)
            {
                if (_generativePendingTasks.TryGetValue(key, out pendingTask))
                    _generativePendingTasks.Remove(key);
            }

            float[] samples = null;
            if (pendingTask != null)
            {
                try { samples = pendingTask.GetAwaiter().GetResult(); }
                catch (Exception) { samples = null; }
            }
            if (samples == null)
            {
                samples = GenerativeMusicEngine.Render(p);
            }

            string meterTag = GenerativeMusicEngine.FormatMeterLabel(p.beatsPerBar);
            var clip = MakeClip($"GEN_S{p.seed}_G{p.generation}_{p.bpm:0}bpm_{meterTag}", samples);
            _generativeCache[key] = clip;
            _generativeLru.Enqueue(key);
            // Fenêtre glissante : on ne garde que les 3 dernières variations.
            while (_generativeLru.Count > 3)
            {
                var old = _generativeLru.Dequeue();
                if (old != key) _generativeCache.Remove(old);
            }
            return clip;
        }

        private static readonly Dictionary<string, AudioClip> _generativeStrikeCache = new();

        public static AudioClip GetGenerativeStrikeClip(int rootMidi, GenerativeScale scale, float strike01)
        {
            int sQ = Mathf.Clamp(Mathf.RoundToInt(strike01 * 4f), 1, 4);
            string key = $"STRIKE_{rootMidi}_{(int)scale}_{sQ}";
            if (_generativeStrikeCache.TryGetValue(key, out var cached) && IsClipUsable(cached)) return cached;
            float[] samples = GenerativeMusicEngine.RenderReactiveStrike(rootMidi, scale, sQ * 0.25f);
            var clip = MakeClip(key, samples);
            _generativeStrikeCache[key] = clip;
            return clip;
        }

        private static string GenerativeKey(GenerativeMusicParams p)
        {
            // Quantifie les continus pour éviter de régénérer à chaque micro-glissé
            // de slider, tout en gardant la réactivité (pas de 0.05).
            int iQ = Mathf.RoundToInt(p.intensity01 * 20f);
            int tQ = Mathf.RoundToInt(p.tension01 * 20f);
            int bQ = Mathf.RoundToInt(p.brightness01 * 10f);
            int cQ = Mathf.RoundToInt(p.chaos01 * 10f);
            int sQ = Mathf.RoundToInt(p.strike01 * 10f);
            int meter = GenerativeMusicEngine.GetStepsPerBar(p.beatsPerBar);
            return $"{p.seed}:{p.generation}:{iQ}:{tQ}:{bQ}:{cQ}:{p.rootMidi}:{(int)p.scale}:{(int)p.drumStyle}:{(int)p.preset}:{p.bpm:0}:{p.bars}:{sQ}:{p.stemOverrides}:{meter}";
        }
    }
}
