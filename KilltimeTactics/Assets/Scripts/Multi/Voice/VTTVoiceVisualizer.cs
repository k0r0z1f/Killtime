using System;
using UnityEngine;

namespace Killtime.Multi.Voice
{
    /// <summary>
    /// Générateur de textures et de visualisations audio temps réel pour l'UI IMGUI :
    /// - Oscilloscope de forme d'onde (signal brut vs signal débruité)
    /// - VU-Mètre dynamique (RMS + Peak Hold + repère visuel du seuil de Noise Gate)
    /// - Analyseur de spectre 4 bandes (Basses, Bas-Médiums, Haut-Médiums, Aigus).
    /// </summary>
    public class VTTVoiceVisualizer
    {
        private const int ScopeW = 280;
        private const int ScopeH = 50;
        private Texture2D _scopeTex;
        private Color32[] _scopePixels;

        // Couleurs de l'oscilloscope tactique
        private static readonly Color32 ScopeBg = new Color32(8, 14, 22, 240);
        private static readonly Color32 ScopeGrid = new Color32(18, 30, 42, 255);
        private static readonly Color32 ScopeCenter = new Color32(28, 48, 68, 255);
        private static readonly Color32 RawWaveColor = new Color32(95, 120, 145, 160);
        private static readonly Color32 CleanWaveColor = new Color32(0, 230, 255, 255);
        private static readonly Color32 GateClosedWaveColor = new Color32(40, 70, 90, 120);

        private float _peakHold = 0f;
        private float _peakHoldTime = 0f;

        public Texture2D ScopeTexture => _scopeTex;

        public void UpdateScopeTexture(VoiceDspProcessor dsp)
        {
            if (dsp == null) return;

            if (_scopeTex == null)
            {
                _scopeTex = new Texture2D(ScopeW, ScopeH, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                _scopePixels = new Color32[ScopeW * ScopeH];
            }

            // 1. Fond et grille
            for (int i = 0; i < _scopePixels.Length; i++) _scopePixels[i] = ScopeBg;

            int midY = ScopeH / 2;
            for (int x = 0; x < ScopeW; x++)
            {
                _scopePixels[midY * ScopeW + x] = ScopeCenter;
                if (x % 28 == 0)
                {
                    for (int y = 0; y < ScopeH; y++)
                    {
                        if (y % 4 == 0) _scopePixels[y * ScopeW + x] = ScopeGrid;
                    }
                }
            }

            // 2. Tracé des formes d'onde (brute et nettoyée)
            var raw = dsp.RawScopeBuffer;
            var clean = dsp.CleanScopeBuffer;
            int bufLen = VoiceDspProcessor.ScopeBufferSize;
            Color32 activeCleanColor = dsp.IsGateOpen ? CleanWaveColor : GateClosedWaveColor;

            int step = Mathf.Max(1, bufLen / ScopeW);
            for (int x = 0; x < ScopeW; x++)
            {
                int sampleIdx = (x * bufLen) / ScopeW;
                if (sampleIdx >= bufLen) sampleIdx = bufLen - 1;

                // Signal brut (atténué/grisé)
                float rVal = Mathf.Clamp(raw[sampleIdx], -1f, 1f);
                int ry = Mathf.Clamp(midY + (int)(rVal * (midY - 2)), 0, ScopeH - 1);
                _scopePixels[ry * ScopeW + x] = RawWaveColor;

                // Signal nettoyé par le DSP (cyan vif quand la porte est ouverte)
                float cVal = Mathf.Clamp(clean[sampleIdx], -1f, 1f);
                int cy = Mathf.Clamp(midY + (int)(cVal * (midY - 2)), 0, ScopeH - 1);
                _scopePixels[cy * ScopeW + x] = activeCleanColor;
            }

            _scopeTex.SetPixels32(_scopePixels);
            _scopeTex.Apply(false);
        }

        private static Texture2D _whiteTex;
        private static Texture2D WhiteTex
        {
            get
            {
                if (_whiteTex == null) _whiteTex = Texture2D.whiteTexture;
                return _whiteTex;
            }
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, WhiteTex);
            GUI.color = prev;
        }

        /// <summary>
        /// Dessine un VU-Mètre complet en IMGUI avec dégradé vert-jaune-rouge,
        /// barre de Peak Hold et repère vertical du seuil de Noise Gate.
        /// </summary>
        public void DrawVuMeter(Rect rect, float rms, float peak, float gateThreshold, bool isGateOpen)
        {
            // Peak Hold
            if (peak > _peakHold || Time.realtimeSinceStartup > _peakHoldTime)
            {
                _peakHold = peak;
                _peakHoldTime = Time.realtimeSinceStartup + 1.2f;
            }

            // Fond noir/bleuté
            DrawSolidRect(rect, new Color(0.04f, 0.06f, 0.09f, 0.95f));

            float w = rect.width;
            float h = rect.height;

            // Remplissage RMS (valeur visuelle normalisée avec courbe logarithmique douce)
            float normRms = Mathf.Clamp01(Mathf.Pow(rms * 2.5f, 0.65f));
            float fillW = normRms * w;

            if (fillW > 0f)
            {
                Color fillColor = isGateOpen
                    ? (normRms > 0.85f ? new Color(1f, 0.25f, 0.25f) : (normRms > 0.60f ? new Color(1f, 0.85f, 0.1f) : new Color(0.1f, 0.95f, 0.4f)))
                    : new Color(0.25f, 0.35f, 0.45f, 0.6f);

                Rect fillRect = new Rect(rect.x, rect.y, fillW, h);
                DrawSolidRect(fillRect, fillColor);
            }

            // Barre de Peak Hold
            float normPeak = Mathf.Clamp01(Mathf.Pow(_peakHold * 2.5f, 0.65f));
            float peakX = rect.x + normPeak * w;
            Rect peakRect = new Rect(Mathf.Min(peakX, rect.xMax - 2), rect.y, 2f, h);
            DrawSolidRect(peakRect, new Color(1f, 0.9f, 0.3f, 0.9f));

            // Repère vertical du seuil de Noise Gate (Gate Notch Marker)
            float normThresh = Mathf.Clamp01(Mathf.Pow(gateThreshold * 2.5f, 0.65f));
            float threshX = rect.x + normThresh * w;

            // Ligne repère
            Rect markerRect = new Rect(threshX - 1f, rect.y - 1f, 2f, h + 2f);
            Color markerColor = isGateOpen ? new Color(0f, 0.9f, 1f, 0.9f) : new Color(1f, 0.4f, 0.2f, 0.9f);
            DrawSolidRect(markerRect, markerColor);

            // Contour
            DrawRectOutline(rect, new Color(0.2f, 0.3f, 0.4f, 0.6f));
        }

        /// <summary>
        /// Dessine l'analyseur de spectre 4 bandes (Basses, Bas-Médiums, Haut-Médiums, Aigus).
        /// </summary>
        public void DrawSpectrumBars(Rect rect, VoiceDspProcessor dsp)
        {
            if (dsp == null) return;

            DrawSolidRect(rect, new Color(0.04f, 0.06f, 0.09f, 0.85f));

            float[] bands = new float[] { dsp.EnergyBass, dsp.EnergyLowMid, dsp.EnergyHighMid, dsp.EnergyTreble };
            Color[] colors = new Color[]
            {
                new Color(1.0f, 0.45f, 0.15f), // Basses
                new Color(0.2f, 0.95f, 0.35f), // Bas-médiums
                new Color(0.0f, 0.85f, 1.0f),  // Haut-médiums
                new Color(0.85f, 0.3f, 1.0f)   // Aigus
            };

            float barSpacing = 4f;
            float totalSpacing = barSpacing * (bands.Length + 1);
            float barW = (rect.width - totalSpacing) / bands.Length;

            for (int i = 0; i < bands.Length; i++)
            {
                float barH = Mathf.Clamp01(bands[i]) * (rect.height - 4f);
                float x = rect.x + barSpacing + i * (barW + barSpacing);
                float y = rect.yMax - 2f - barH;

                Rect barRect = new Rect(x, y, barW, barH);
                DrawSolidRect(barRect, colors[i]);
            }

            DrawRectOutline(rect, new Color(0.2f, 0.3f, 0.4f, 0.5f));
        }

        private static void DrawRectOutline(Rect r, Color color)
        {
            DrawSolidRect(new Rect(r.x, r.y, r.width, 1), color);
            DrawSolidRect(new Rect(r.x, r.yMax - 1, r.width, 1), color);
            DrawSolidRect(new Rect(r.x, r.y, 1, r.height), color);
            DrawSolidRect(new Rect(r.xMax - 1, r.y, 1, r.height), color);
        }

        public void Destroy()
        {
            if (_scopeTex != null)
            {
                UnityEngine.Object.Destroy(_scopeTex);
                _scopeTex = null;
            }
        }
    }
}
