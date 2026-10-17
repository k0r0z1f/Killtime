// Styles, textures procedurales et glyphes pixel (extrait de CombatContextMenuUI.cs).
using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics.CombatUI
{
    public partial class CombatContextMenuUI : MonoBehaviour
    {
        private static string GetCategoryDisplayName(ActionCategory cat)
        {
            return cat switch
            {
                ActionCategory.AttaqueEtPassesDarmes => "Attaque & Passes d'Armes",
                ActionCategory.CinquiemeForceEtSorts => "5e Force & Sorts",
                ActionCategory.TraumatologieEtSoins => "Traumatologie & Soins",
                ActionCategory.TactiqueEtOrdres => "Tactique & Ordres",
                ActionCategory.TechniquesDeSpecialisation => "Techniques & Spécialisations",
                ActionCategory.CommandesDev => "Commandes Développeur",
                _ => "Actions"
            };
        }

        private static Color GetCategoryColor(ActionCategory cat)
        {
            return cat switch
            {
                ActionCategory.AttaqueEtPassesDarmes => ColorArcaneCrimson,
                ActionCategory.CinquiemeForceEtSorts => ColorArcaneViolet,
                ActionCategory.TraumatologieEtSoins => ColorArcaneEmerald,
                ActionCategory.TactiqueEtOrdres => ColorArcaneAmber,
                ActionCategory.TechniquesDeSpecialisation => ColorArcaneCyan,
                ActionCategory.CommandesDev => ColorArcaneSlate,
                _ => ColorArcaneCyan
            };
        }

        private static void EnsureProceduralTextures()
        {
            if (_circleTex == null) _circleTex = GenerateCircleTexture(64, false);
            if (_ringTex == null) _ringTex = GenerateCircleTexture(64, true, 3.5f);
            if (_glowTex == null) _glowTex = GenerateGlowTexture(64);
            if (_pixelTex == null)
            {
                _pixelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _pixelTex.SetPixel(0, 0, Color.white);
                _pixelTex.Apply();
            }
            if (_runeTickTex == null) _runeTickTex = GenerateRuneTickTexture(128, RuneTickCount);
        }

        // Calibration PA partagée attaque/défense (RD-155) : cyan → ambre → crimson.
        private static Color GetPAFractionColor(float frac)
        {
            if (frac > 0.5f) return ColorArcaneCyan;
            if (frac > 0.25f) return ColorArcaneAmber;
            return ColorArcaneCrimson;
        }

        // Anneau runique (RD-155) : ticks procéduraux, teinté via GUI.color par l'appelant.
        private static Texture2D GenerateRuneTickTexture(int size, int ticks)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] clear = new Color32[size * size];
            for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(0, 0, 0, 0);
            tex.SetPixels32(clear);
            float center = (size - 1) * 0.5f;
            float outerR = center - 1f;
            for (int i = 0; i < ticks; i++)
            {
                float ang = (Mathf.PI * 2f * i) / ticks;
                bool major = (i % 6 == 0);
                float len = major ? 14f : 8f;
                int x0 = Mathf.RoundToInt(center + Mathf.Cos(ang) * outerR);
                int y0 = Mathf.RoundToInt(center + Mathf.Sin(ang) * outerR);
                int x1 = Mathf.RoundToInt(center + Mathf.Cos(ang) * (outerR - len));
                int y1 = Mathf.RoundToInt(center + Mathf.Sin(ang) * (outerR - len));
                DrawLine(tex, x0, y0, x1, y1, Color.white);
                if (major) DrawDot(tex, x1, y1, 1, Color.white);
            }
            tex.Apply();
            return tex;
        }

        private static void DrawRuneTickRing(Rect r, Color col, float rotDeg)
        {
            if (_runeTickTex == null) return;
            Color prev = GUI.color;
            Matrix4x4 old = GUI.matrix;
            GUIUtility.RotateAroundPivot(rotDeg, r.center);
            GUI.color = col;
            GUI.DrawTexture(r, _runeTickTex);
            GUI.matrix = old;
            GUI.color = prev;
        }

        // Thereline pointillée arcanotech entre deux points écran.
        private static void DrawTetherLine(Vector2 a, Vector2 b, Color col, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 4f) return;
            Color prev = GUI.color;
            Matrix4x4 old = GUI.matrix;
            Vector2 mid = (a + b) * 0.5f;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(ang, mid);
            GUI.color = col;
            int dashes = 10;
            for (int i = 0; i < dashes; i += 2)
            {
                float x0 = mid.x - len * 0.5f + len * i / dashes;
                float w = len / dashes;
                GUI.DrawTexture(new Rect(x0, mid.y - width * 0.5f, w, width), _pixelTex);
            }
            GUI.matrix = old;
            GUI.color = prev;
        }

        // Moteur d'orbite inclinée RD-156 (partagé attaque/défense) : l'angle 90°
        // (bas de l'ellipse, proche caméra) est l'avant = choix courant.
        private static float WrapAngle(float a)
        {
            while (a > 180f) a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }

        private static Vector2 OrbitNodePos(Vector2 center, float rx, float ry, float angleDeg)
        {
            float r = angleDeg * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(r) * rx, Mathf.Sin(r) * ry);
        }

        // Profondeur 0 (fond, haut) → 1 (avant, bas) : taille et opacité des nœuds.
        private static float OrbitDepth(float angleDeg)
        {
            return Mathf.Clamp01((Mathf.Sin(angleDeg * Mathf.Deg2Rad) + 1f) * 0.5f);
        }

        private static int OrbitFrontIndex(float angle, int count)
        {
            if (count <= 0) return -1;
            float step = 360f / count;
            int best = 0;
            float bestD = 9999f;
            for (int i = 0; i < count; i++)
            {
                float d = Mathf.Abs(WrapAngle(angle + i * step - 90f));
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static float OrbitSnapTarget(float angle, int count, int index)
        {
            if (count <= 0) return angle;
            float step = 360f / count;
            float t = 90f - index * step;
            t += 360f * Mathf.Round((angle - t) / 360f);
            return t;
        }

        // Moteur d'orbite RD-156/164 (partagé attaque/défense) : l'angle 90°
        // (bas de l'ellipse, proche caméra) est l'avant = choix courant.
        // RD-164 : snap ease-out cubic (250 ms) au lieu du linéaire, rampe de
        // vélocité (180 ms), hystérésis entrée/sortie de bande, dwell (pause
        // quand le curseur tient un nœud) et snap magnétique à la sortie de
        // bande vers l'item le plus proche de l'avant. Hors bande + hors snap
        // l'angle est strictement fixe : aucun mouvement sans cause.
        private static void UpdateOrbitAngle(ref float angle, ref float target, ref bool hasTarget, ref float snapFrom, ref float snapT, ref float vel, ref bool wasInBand, int count, Vector2 mouse, Vector2 center, float rx, float ry, float dt, bool hoverHold)
        {
            if (count <= 0) return;
            float snapDur = Mathf.Max(0.05f, OrbitTuning.SnapMs / 1000f);
            float rampRate = OrbitTuning.MaxSpeed / Mathf.Max(0.05f, OrbitTuning.RampMs / 1000f);

            // Snap explicite (clic, clavier, molette, magnétique) : ease-out.
            if (hasTarget)
            {
                snapT += dt;
                float k = Mathf.Clamp01(snapT / snapDur);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                angle = snapFrom + WrapAngle(target - snapFrom) * e;
                if (k >= 1f)
                {
                    angle = target;
                    hasTarget = false;
                }
                vel = 0f;
                wasInBand = false;
                return;
            }

            // Clic en cours : l'anneau ne bouge pas sous le curseur pressé.
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
            {
                vel = 0f;
                return;
            }

            float dx = mouse.x - center.x;
            float dy = mouse.y - center.y;
            float adx = Mathf.Abs(dx);
            float inner = rx * (wasInBand ? OrbitTuning.ReleaseFactor : OrbitTuning.EdgeInnerFactor);
            float outer = rx + OrbitTuning.EdgeOuterExtra;
            bool inBand = adx > inner && adx < outer && Mathf.Abs(dy) < ry + 70f;

            if (!inBand)
            {
                // Sortie de bande : snap magnétique systématique vers l'item
                // le plus proche de l'avant (ease-out 250 ms, position
                // continue : aucune coupure visible même à pleine vitesse).
                if (wasInBand)
                {
                    wasInBand = false;
                    int front = OrbitFrontIndex(angle, count);
                    target = OrbitSnapTarget(angle, count, front);
                    snapFrom = angle;
                    snapT = 0f;
                    hasTarget = true;
                    vel = 0f;
                }
                return;
            }

            wasInBand = true;

            // Dwell : curseur sur un nœud = freinage, l'item devient visable.
            float desired = 0f;
            if (!(hoverHold && OrbitTuning.DwellPause))
            {
                float t = Mathf.Clamp01((adx - inner) / Mathf.Max(1f, outer - inner));
                float eased = t * t;
                if (eased >= 0.02f) desired = Mathf.Sign(dx) * OrbitTuning.MaxSpeed * eased;
            }
            vel = Mathf.MoveTowards(vel, desired, rampRate * dt);
            angle += vel * dt;
        }

        // Pips PA diégétiques (calibrage visuel) : le chiffré reste affiché à côté.
        // Doit être appelé dans un bloc GUILayout (hubs attaque/défense).
        private void DrawPAPips(int currentPA, int cost)
        {
            int total = Mathf.Clamp(currentPA, 0, MaxPAPips);
            int filled = Mathf.Clamp(currentPA - cost, 0, total);
            if (total <= 0) return;
            float frac = currentPA > 0 ? Mathf.Clamp01((float)(currentPA - cost) / currentPA) : 0f;
            var pipStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };
            pipStyle.normal.textColor = GetPAFractionColor(frac);
            GUI.color = Color.white;
            GUILayout.Label(new string('●', filled) + new string('○', total - filled), pipStyle);
        }

        // Pastille latérale de mise RD-161 : fond translucide, pleine au survol.
        private static void DrawStakeChip(Rect rect, string text, Color accent, bool hovered)
        {
            Color prev = GUI.color;
            GUI.color = new Color(accent.r, accent.g, accent.b, hovered ? 0.38f : 0.15f);
            GUI.DrawTexture(rect, _circleTex);
            GUI.color = new Color(1f, 1f, 1f, hovered ? 1f : 0.60f);
            var chipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 8,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(rect, text, chipStyle);
            GUI.color = prev;
        }

        private static Texture2D GenerateCircleTexture(int size, bool hollow, float strokeWidth = 3f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float maxR = center;
            float innerR = center - strokeWidth;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = 0f;

                    if (!hollow)
                    {
                        alpha = Mathf.Clamp01(maxR - dist + 0.5f);
                    }
                    else
                    {
                        float distOuter = maxR - dist + 0.5f;
                        float distInner = dist - innerR + 0.5f;
                        alpha = Mathf.Clamp01(Mathf.Min(distOuter, distInner));
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateGlowTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha = alpha * alpha;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D GetCategoryPlaceholderIcon(ActionCategory cat)
        {
            string key = "CAT_" + cat;
            if (_glyphCache.TryGetValue(key, out var cached)) return cached;

            int s = 32;
            var tex = CreateEmptyTexture(s);
            switch (cat)
            {
                case ActionCategory.AttaqueEtPassesDarmes:
                    DrawLine(tex, 16, 4, 16, 28, Color.white);
                    DrawLine(tex, 4, 16, 28, 16, Color.white);
                    DrawHollowSquare(tex, 10, 10, 12, Color.white);
                    break;
                case ActionCategory.CinquiemeForceEtSorts:
                    DrawDiamond(tex, 16, 16, 12, Color.white);
                    DrawDot(tex, 16, 16, 2, Color.white);
                    break;
                case ActionCategory.TraumatologieEtSoins:
                    DrawCross(tex, 16, 16, 10, 4, Color.white);
                    break;
                case ActionCategory.TactiqueEtOrdres:
                    DrawChevron(tex, 16, 10, 12, Color.white);
                    DrawChevron(tex, 16, 18, 12, Color.white);
                    break;
                case ActionCategory.TechniquesDeSpecialisation:
                    DrawLine(tex, 16, 4, 16, 28, Color.white);
                    DrawLine(tex, 4, 16, 28, 16, Color.white);
                    DrawLine(tex, 8, 8, 24, 24, Color.white);
                    DrawLine(tex, 24, 8, 8, 24, Color.white);
                    DrawDot(tex, 16, 16, 2, Color.white);
                    break;
                case ActionCategory.CommandesDev:
                    DrawHollowSquare(tex, 8, 8, 16, Color.white);
                    DrawLine(tex, 8, 8, 24, 24, Color.white);
                    break;
            }
            tex.Apply();
            _glyphCache[key] = tex;
            return tex;
        }

        private static Texture2D GetActionPlaceholderIcon(string title, ActionCategory cat)
        {
            string lower = title.ToLowerInvariant();
            string key = "ACT_" + lower;
            if (_glyphCache.TryGetValue(key, out var cached)) return cached;

            int s = 32;
            var tex = CreateEmptyTexture(s);

            if (lower.Contains("tête") || lower.Contains("neutraliser"))
            {
                DrawCircle(tex, 16, 13, 8, Color.white);
                DrawHollowSquare(tex, 12, 20, 8, Color.white);
            }
            else if (lower.Contains("bras") || lower.Contains("désarmement"))
            {
                DrawLine(tex, 6, 26, 26, 6, Color.white);
                DrawLine(tex, 4, 22, 10, 28, Color.white);
            }
            else if (lower.Contains("jambes") || lower.Contains("faucher"))
            {
                DrawLine(tex, 16, 4, 10, 28, Color.white);
                DrawLine(tex, 16, 4, 22, 28, Color.white);
            }
            else if (lower.Contains("grenade"))
            {
                DrawCircle(tex, 16, 18, 8, Color.white);
                DrawLine(tex, 16, 10, 16, 5, Color.white);
                DrawDot(tex, 18, 5, 2, Color.white);
            }
            else if (lower.Contains("soin") || lower.Contains("suture"))
            {
                DrawCross(tex, 16, 16, 10, 4, Color.white);
            }
            else if (lower.Contains("défibrillation"))
            {
                DrawLine(tex, 6, 16, 12, 16, Color.white);
                DrawLine(tex, 12, 16, 15, 6, Color.white);
                DrawLine(tex, 15, 6, 18, 26, Color.white);
                DrawLine(tex, 18, 26, 21, 16, Color.white);
                DrawLine(tex, 21, 16, 26, 16, Color.white);
            }
            else if (lower.Contains("souffle"))
            {
                DrawChevron(tex, 10, 16, 8, Color.white);
                DrawChevron(tex, 18, 16, 8, Color.white);
            }
            else if (lower.Contains("ordre") || lower.Contains("couvrir"))
            {
                DrawChevron(tex, 16, 12, 10, Color.white);
                DrawChevron(tex, 16, 20, 10, Color.white);
            }
            else if (lower.Contains("intimidation"))
            {
                DrawDiamond(tex, 16, 16, 10, Color.white);
                DrawLine(tex, 10, 16, 22, 16, Color.white);
            }
            else if (lower.Contains("clé"))
            {
                DrawLine(tex, 8, 24, 24, 8, Color.white);
                DrawLine(tex, 8, 18, 24, 18, Color.white);
                DrawLine(tex, 12, 18, 12, 24, Color.white);
                DrawLine(tex, 20, 18, 20, 24, Color.white);
            }
            else if (lower.Contains("faille") || lower.Contains("analyse"))
            {
                DrawCircle(tex, 13, 13, 8, Color.white);
                DrawLine(tex, 19, 19, 27, 27, Color.white);
            }
            else if (lower.Contains("rugissement") || lower.Contains("terreur"))
            {
                DrawLine(tex, 8, 8, 8, 24, Color.white);
                DrawLine(tex, 16, 6, 16, 26, Color.white);
                DrawLine(tex, 24, 8, 24, 24, Color.white);
            }
            else if (lower.Contains("regard") || lower.Contains("prédateur"))
            {
                DrawCircle(tex, 16, 16, 10, Color.white);
                DrawDot(tex, 16, 16, 3, Color.white);
            }
            else if (lower.Contains("mener") || lower.Contains("commandement"))
            {
                DrawChevron(tex, 16, 10, 10, Color.white);
                DrawChevron(tex, 16, 17, 10, Color.white);
                DrawChevron(tex, 16, 24, 10, Color.white);
            }
            else if (lower.Contains("tenir") || lower.Contains("ligne"))
            {
                DrawHollowSquare(tex, 6, 12, 20, Color.white);
                DrawLine(tex, 6, 26, 26, 26, Color.white);
                DrawLine(tex, 11, 12, 11, 6, Color.white);
                DrawLine(tex, 21, 12, 21, 6, Color.white);
            }
            else if (lower.Contains("fiche"))
            {
                DrawHollowSquare(tex, 8, 6, 16, Color.white);
                DrawLine(tex, 12, 11, 20, 11, Color.white);
                DrawLine(tex, 12, 16, 20, 16, Color.white);
            }
            else if (lower.Contains("inventaire"))
            {
                DrawHollowSquare(tex, 7, 10, 18, Color.white);
                DrawHollowSquare(tex, 12, 5, 8, Color.white);
            }
            else
            {
                switch (cat)
                {
                    case ActionCategory.AttaqueEtPassesDarmes:
                        DrawCrosshair(tex, 16, 16, 10, Color.white);
                        break;
                    case ActionCategory.CinquiemeForceEtSorts:
                        DrawDiamond(tex, 16, 16, 11, Color.white);
                        DrawDot(tex, 16, 16, 3, Color.white);
                        break;
                    case ActionCategory.TraumatologieEtSoins:
                        DrawCross(tex, 16, 16, 9, 3, Color.white);
                        break;
                    default:
                        DrawDot(tex, 16, 16, 5, Color.white);
                        break;
                }
            }

            tex.Apply();
            _glyphCache[key] = tex;
            return tex;
        }

        private static Texture2D GetGlyphTexture(string key)
        {
            if (_glyphCache.TryGetValue(key, out var cached)) return cached;

            int s = 32;
            var tex = CreateEmptyTexture(s);
            if (key == "GLYPH_ENDTURN")
            {
                DrawLine(tex, 8, 6, 24, 6, Color.white);
                DrawLine(tex, 8, 26, 24, 26, Color.white);
                DrawLine(tex, 8, 6, 24, 26, Color.white);
                DrawLine(tex, 24, 6, 8, 26, Color.white);
            }
            tex.Apply();
            _glyphCache[key] = tex;
            return tex;
        }

        private static Texture2D CreateEmptyTexture(int s)
        {
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] clear = new Color32[s * s];
            for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(0, 0, 0, 0);
            tex.SetPixels32(clear);
            return tex;
        }

        private static void DrawHollowRect(Rect r, float stroke)
        {
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, stroke), _pixelTex);
            GUI.DrawTexture(new Rect(r.x, r.yMax - stroke, r.width, stroke), _pixelTex);
            GUI.DrawTexture(new Rect(r.x, r.y, stroke, r.height), _pixelTex);
            GUI.DrawTexture(new Rect(r.xMax - stroke, r.y, stroke, r.height), _pixelTex);
        }

        private static void DrawDot(Texture2D tex, int cx, int cy, int r, Color col)
        {
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r * r)
                        SetPixelSafe(tex, cx + x, cy + y, col);
        }

        private static void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color col)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                SetPixelSafe(tex, x0, y0, col);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private static void DrawHollowSquare(Texture2D tex, int x, int y, int size, Color col)
        {
            DrawLine(tex, x, y, x + size, y, col);
            DrawLine(tex, x, y + size, x + size, y + size, col);
            DrawLine(tex, x, y, x, y + size, col);
            DrawLine(tex, x + size, y, x + size, y + size, col);
        }

        private static void DrawCross(Texture2D tex, int cx, int cy, int length, int thickness, Color col)
        {
            int halfThick = thickness / 2;
            for (int x = cx - length; x <= cx + length; x++)
                for (int y = cy - halfThick; y <= cy + halfThick; y++)
                    SetPixelSafe(tex, x, y, col);

            for (int y = cy - length; y <= cy + length; y++)
                for (int x = cx - halfThick; x <= cx + halfThick; x++)
                    SetPixelSafe(tex, x, y, col);
        }

        private static void DrawCrosshair(Texture2D tex, int cx, int cy, int r, Color col)
        {
            DrawLine(tex, cx - r, cy, cx + r, cy, col);
            DrawLine(tex, cx, cy - r, cx, cy + r, col);
            DrawCircle(tex, cx, cy, r - 2, col);
        }

        private static void DrawDiamond(Texture2D tex, int cx, int cy, int r, Color col)
        {
            DrawLine(tex, cx - r, cy, cx + r, cy, col);
            DrawLine(tex, cx + r, cy, cx, cy + r, col);
            DrawLine(tex, cx, cy + r, cx - r, cy, col);
            DrawLine(tex, cx - r, cy, cx, cy - r, col);
        }

        private static void DrawChevron(Texture2D tex, int cx, int cy, int size, Color col)
        {
            DrawLine(tex, cx - size / 2, cy - size / 4, cx, cy + size / 4, col);
            DrawLine(tex, cx, cy + size / 4, cx + size / 2, cy - size / 4, col);
        }

        private static void DrawCircle(Texture2D tex, int cx, int cy, int r, Color col)
        {
            int x = r, y = 0;
            int radiusError = 1 - x;
            while (x >= y)
            {
                SetPixelSafe(tex, cx + x, cy + y, col);
                SetPixelSafe(tex, cx - x, cy + y, col);
                SetPixelSafe(tex, cx + x, cy - y, col);
                SetPixelSafe(tex, cx - x, cy - y, col);
                SetPixelSafe(tex, cx + y, cy + x, col);
                SetPixelSafe(tex, cx - y, cy + x, col);
                SetPixelSafe(tex, cx + y, cy - x, col);
                SetPixelSafe(tex, cx - y, cy - x, col);
                y++;
                if (radiusError < 0) radiusError += 2 * y + 1;
                else { x--; radiusError += 2 * (y - x + 1); }
            }
        }

        private static void SetPixelSafe(Texture2D tex, int x, int y, Color col)
        {
            if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
                tex.SetPixel(x, y, col);
        }
    }
}
