using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Character.Classes;
using Killtime.Core.Dice;
using Killtime.Tactics.Units;

namespace Killtime.UI
{
    public partial class SkillTreeCosmosWindow : FloatingWindow<SkillTreeCosmosWindow>
    {
        private static void InitProceduralTextures()
        {
            if (_pixelTex == null)
            {
                _pixelTex = new Texture2D(1, 1);
                _pixelTex.SetPixel(0, 0, Color.white);
                _pixelTex.Apply();
            }

            if (_starCircleTex == null)
            {
                int sz = 64;
                _starCircleTex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
                for (int y = 0; y < sz; y++)
                {
                    for (int x = 0; x < sz; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f;
                        float alpha = Mathf.Clamp01(1f - Mathf.SmoothStep(0.25f, 0.95f, d));
                        _starCircleTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
                _starCircleTex.Apply();
            }

            if (_starGlowTex == null)
            {
                int sz = 64;
                _starGlowTex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
                for (int y = 0; y < sz; y++)
                {
                    for (int x = 0; x < sz; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f;
                        float alpha = Mathf.Exp(-d * 3.5f) * Mathf.Clamp01(1f - d);
                        _starGlowTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
                _starGlowTex.Apply();
            }

            if (_starDiamondTex == null)
            {
                int sz = 64;
                _starDiamondTex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
                for (int y = 0; y < sz; y++)
                {
                    for (int x = 0; x < sz; x++)
                    {
                        float nx = Mathf.Abs(x - 31.5f) / 31.5f;
                        float ny = Mathf.Abs(y - 31.5f) / 31.5f;
                        float d = nx + ny;
                        float alpha = Mathf.Clamp01(1f - d);
                        alpha = Mathf.Pow(alpha, 1.8f);
                        _starDiamondTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
                _starDiamondTex.Apply();
            }
        }

        private static void EnsureStyles()
        {
            if (_nodeLabelStyle == null)
            {
                _nodeLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    alignment = TextAnchor.UpperCenter,
                    richText = true,
                    wordWrap = true
                };
            }

            if (_tooltipHeaderStyle == null)
            {
                _tooltipHeaderStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    richText = true
                };
            }

            if (_tooltipDescStyle == null)
            {
                _tooltipDescStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    richText = true,
                    wordWrap = true
                };
            }

            if (_tooltipRuleStyle == null)
            {
                _tooltipRuleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    richText = true,
                    wordWrap = true
                };
            }

            if (_constellationTitleStyle == null)
            {
                _constellationTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true
                };
            }
        }
    }
}
