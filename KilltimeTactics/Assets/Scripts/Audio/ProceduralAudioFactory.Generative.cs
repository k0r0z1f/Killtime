using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>Partiel Génératif infini (délègue à GenerativeMusicEngine, cache LRU fenêtré).</summary>
    public static partial class ProceduralAudioFactory
    {
        // =====================================================================
        // MODE GÉNÉRATIF INFINI (sans chanson préfaite) — ne touche pas aux
        // chansons legacy ci-dessus. Chaque appel avec generation+1 donne une
        // variation inédite mais cohérente (même seed => même famille).
        // =====================================================================
        public static float EstimateGenerativeDuration(GenerativeMusicParams p)
            => GenerativeMusicEngine.EstimateDuration(p);

        public static AudioClip GetGenerativeLoop(GenerativeMusicParams p)
        {
            string key = GenerativeKey(p);
            if (_generativeCache.TryGetValue(key, out var cached) && IsClipUsable(cached)) return cached;
            float[] samples = GenerativeMusicEngine.Render(p);
            var clip = MakeClip($"GEN_S{p.seed}_G{p.generation}_{p.bpm:0}bpm", samples);
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

        private static string GenerativeKey(GenerativeMusicParams p)
        {
            // Quantifie les continus pour éviter de régénérer à chaque micro-glissé
            // de slider, tout en gardant la réactivité (pas de 0.05).
            int iQ = Mathf.RoundToInt(p.intensity01 * 20f);
            int tQ = Mathf.RoundToInt(p.tension01 * 20f);
            int bQ = Mathf.RoundToInt(p.brightness01 * 10f);
            int cQ = Mathf.RoundToInt(p.chaos01 * 10f);
            int sQ = Mathf.RoundToInt(p.strike01 * 10f);
            return $"{p.seed}:{p.generation}:{iQ}:{tQ}:{bQ}:{cQ}:{p.rootMidi}:{(int)p.scale}:{(int)p.drumStyle}:{(int)p.preset}:{p.bpm:0}:{p.bars}:{sQ}:{p.stemOverrides}";
        }
    }
}

