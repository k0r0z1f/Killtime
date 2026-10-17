// Styles/primitives du HUD (extrait de CombatHUD.cs, comportement inchange).
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Audio;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Objectives;

namespace Killtime.UI
{
    public partial class CombatHUD : MonoBehaviour
    {
        // --- Couleurs de la Palette Tactique (Dark Cybernetic) ---
        private static readonly Color ColorBgBase = new Color(0.03f, 0.05f, 0.08f, 0.72f);
        private static readonly Color ColorBgHover = new Color(0.05f, 0.09f, 0.14f, 0.85f);
        private static readonly Color ColorCyanAccent = new Color(0.0f, 0.90f, 1.0f, 0.85f);
        private static readonly Color ColorCyanDim = new Color(0.0f, 0.90f, 1.0f, 0.25f);
        private static readonly Color ColorAmber = new Color(1.0f, 0.72f, 0.15f, 0.95f);
        private static readonly Color ColorCrimson = new Color(1.0f, 0.22f, 0.32f, 0.95f);
        private static readonly Color ColorGhostDamage = new Color(1.0f, 0.4f, 0.2f, 0.75f);
        private static readonly Color ColorTextBright = new Color(0.94f, 0.97f, 1.0f, 0.98f);
        private static readonly Color ColorTextMuted = new Color(0.52f, 0.64f, 0.75f, 0.80f);

        // Textures procédurales
        private static Texture2D _pixelTex;
        private static Texture2D _gradientTex;

        // Styles mis en cache
        private static GUIStyle _hudNameStyle;
        private static GUIStyle _hudSubStyle;
        private static GUIStyle _hudStatStyle;
        private static GUIStyle _btnFlatNormal;
        private static GUIStyle _floatingLogStyle;
        private static GUIStyle _terminalHeaderStyle;
        private static GUIStyle _terminalBodyStyle;
        private static GUIStyle _terminalDiceStyle;
        private static GUIStyle _bannerTitleStyle;
        private static GUIStyle _bannerSubStyle;
        private static GUIStyle _bannerTagStyle;

        private static void InitProceduralTextures()
        {
            if (_pixelTex == null)
            {
                _pixelTex = new Texture2D(1, 1);
                _pixelTex.SetPixel(0, 0, Color.white);
                _pixelTex.Apply();
            }

            if (_gradientTex == null)
            {
                int w = 64;
                _gradientTex = new Texture2D(w, 1, TextureFormat.RGBA32, false);
                for (int x = 0; x < w; x++)
                {
                    float alpha = Mathf.SmoothStep(1.0f, 0.0f, (float)x / (w - 1));
                    _gradientTex.SetPixel(x, 0, new Color(1f, 1f, 1f, alpha));
                }
                _gradientTex.Apply();
            }
        }

        private void EnsureStyles()
        {
            if (_hudNameStyle != null) return;

            _hudNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                richText = false,
                wordWrap = false
            };
            _hudNameStyle.normal.textColor = ColorTextBright;

            _hudSubStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                richText = true
            };
            _hudSubStyle.normal.textColor = ColorTextMuted;

            _hudStatStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
            _hudStatStyle.normal.textColor = ColorTextBright;

            _btnFlatNormal = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 1, 1),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(0, 0, 0, 0)
            };
            _btnFlatNormal.normal.textColor = ColorTextBright;

            _floatingLogStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleRight,
                wordWrap = false
            };

            _terminalHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _terminalHeaderStyle.normal.textColor = ColorCyanAccent;

            _terminalBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                richText = true
            };
            _terminalBodyStyle.normal.textColor = new Color(0.86f, 0.90f, 0.95f, 0.90f);

            _terminalDiceStyle = new GUIStyle(_terminalBodyStyle)
            {
                fontStyle = FontStyle.Bold
            };
            _terminalDiceStyle.normal.textColor = ColorCyanAccent;

            _bannerTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };
            _bannerTitleStyle.normal.textColor = ColorTextBright;

            _bannerSubStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };
            _bannerSubStyle.normal.textColor = ColorCyanAccent;

            _bannerTagStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 8,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };
            _bannerTagStyle.normal.textColor = ColorTextMuted;
        }

        /// <summary>
        /// Tronque un texte avec "…" pour qu'il tienne dans availWidth (mesuré avec le style).
        /// Évite les retours à la ligne et les chevauchements dans les cartes HUD à largeur fixe.
        /// </summary>
        private static string TruncateToFit(string text, GUIStyle style, float availWidth)
        {
            if (string.IsNullOrEmpty(text) || availWidth <= 0f) return "";
            if (style.CalcSize(new GUIContent(text)).x <= availWidth) return text;
            const string ellipsis = "…";
            float ellW = style.CalcSize(new GUIContent(ellipsis)).x;
            int len = text.Length;
            while (len > 1 && style.CalcSize(new GUIContent(text.Substring(0, len) + ellipsis)).x > availWidth)
            {
                len--;
            }
            if (len <= 1) return ellipsis;
            // Garantit que même avec l'ellipse ça passe (polices proportionnelles).
            string candidate = text.Substring(0, len) + ellipsis;
            while (candidate.Length > 2 && style.CalcSize(new GUIContent(candidate)).x > availWidth + ellW * 0.5f)
            {
                candidate = candidate.Substring(0, candidate.Length - 2) + ellipsis;
            }
            return candidate;
        }

        // =========================================================================
        // PRIMITIVES DE RENDU GRAPHIQUE ÉPURÉ (Vector-Like HUD)
        // =========================================================================

        /// <summary>
        /// Dessine un cadre tactique moderne avec des encoches aux coins (Bracketed Corners).
        /// </summary>
        private static void DrawSoftPanel(Rect r, Color bg, bool withShadow = true)
        {
            if (withShadow)
                DrawSolidRect(new Rect(r.x, r.y + 2f, r.width, r.height),
                    new Color(0f, 0f, 0f, bg.a * 0.24f));

            DrawSolidRect(r, bg);
            DrawAccentLine(new Rect(r.x, r.y, r.width, 1f),
                new Color(1f, 1f, 1f, 0.035f), 1f);
        }

        private static void DrawAccentLine(Rect r, Color color, float alpha)
        {
            Color c = color;
            c.a *= alpha;
            DrawSolidRect(r, c);
        }

        private static void DrawTacticalFrame(Rect r, Color bg, Color border, float cornerLen, float thickness)
        {
            DrawSoftPanel(r, bg);
            DrawAccentLine(new Rect(r.x, r.y, cornerLen, thickness), border, 0.70f);
            DrawAccentLine(new Rect(r.x, r.y, thickness, cornerLen), border, 0.70f);
            DrawAccentLine(new Rect(r.x + r.width - cornerLen, r.y, cornerLen, thickness), border, 0.70f);
            DrawAccentLine(new Rect(r.x + r.width - thickness, r.y, thickness, cornerLen), border, 0.70f);
        }

        private static bool DrawTacticalButton(Rect r, string label, string badge, bool active, Color accent, bool enabled = true)
        {
            bool hovered = enabled && r.Contains(Event.current.mousePosition);

            DrawSolidRect(r, active
                ? new Color(accent.r, accent.g, accent.b, 0.13f)
                : new Color(1f, 1f, 1f, hovered ? 0.055f : 0.018f));

            float lineHeight = (active || hovered) ? 2f : 1f;
            DrawAccentLine(new Rect(r.x, r.y + r.height - lineHeight, r.width, lineHeight),
                accent,
                enabled ? (active ? 0.90f : hovered ? 0.55f : 0.16f) : 0.08f);

            GUI.color = enabled
                ? (active || hovered ? ColorTextBright : new Color(0.88f, 0.92f, 0.97f, 0.84f))
                : new Color(0.40f, 0.45f, 0.50f, 0.42f);

            bool clicked = enabled && GUI.Button(r, label, _btnFlatNormal);

            if (!string.IsNullOrEmpty(badge))
            {
                GUI.color = enabled ? ColorTextMuted : new Color(0.35f, 0.40f, 0.45f, 0.35f);
                GUI.Label(new Rect(r.x + r.width - 56f, r.y + 8f, 50f, 14f), badge, _hudStatStyle);
            }

            GUI.color = Color.white;
            return clicked;
        }

        private static void DrawSolidRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _pixelTex);
            GUI.color = prev;
        }
    }
}
