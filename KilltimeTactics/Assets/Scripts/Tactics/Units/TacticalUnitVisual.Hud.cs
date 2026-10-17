using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Killtime.Tactics.Units;
using Killtime.Tactics.CombatUI;
using Killtime.Tactics.Grid;
using Killtime.Core.Character;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// HUD : floating texts, OnGUI, overhead, halo statuts, icônes, fog.
    /// Façade : TacticalUnitVisual.cs (champs sérialisés, lifecycle). Aucune API changée.
    /// </summary>
    public partial class TacticalUnitVisual : MonoBehaviour
    {
        public void SpawnFloatingText(string message, Color color)
        {
            // Brouillard de guerre : aucun indice visuel ne doit fuir la position
            // d'un ennemi non détecté (ni dégâts flottants, ni logs 3D).
            if (_hiddenByFog) return;
            float stackOffset = _floatingTexts.Count * 0.38f;
            Vector3 headPos = GetHeadWorldPosition();
            bool hasStatus = _unit != null && _unit.Stats != null && _unit.Stats.ActiveStatus != StatusEffect.None;
            float baseHeight = hasStatus ? 0.95f : 0.65f;

            _floatingTexts.Add(new FloatingText
            {
                Text = message,
                Color = color,
                WorldPos = headPos + Vector3.up * (baseHeight + stackOffset),
                Lifetime = 3.6f,
                MaxLifetime = 3.6f
            });
        }

        private void UpdateFloatingTexts()
        {
            for (int i = _floatingTexts.Count - 1; i >= 0; i--)
            {
                var ft = _floatingTexts[i];
                ft.Lifetime -= Time.unscaledDeltaTime;
                ft.WorldPos += Vector3.up * (0.35f * Time.unscaledDeltaTime);

                if (ft.Lifetime <= 0f)
                {
                    _floatingTexts.RemoveAt(i);
                }
            }
        }

        private Vector3 GetHeadWorldPosition()
        {
            bool isDown = _unit.Stats != null && (!_unit.Stats.IsAlive || _unit.Stats.IsDead || _unit.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient));
            if (isDown)
            {
                return transform.position + Vector3.up * 0.75f;
            }

            if (_modelRoot != null)
            {
                return _modelRoot.TransformPoint(new Vector3(0, 1.85f * _unitScale, 0));
            }
            return transform.position + Vector3.up * (1.85f * _unitScale);
        }

        public static bool ShowAnimationTelemetry = false;

        /// <summary>
        /// Boîte d'info au-dessus des personnages (nom + PV/PA). Visible par
        /// défaut, masquée en mode scène narrative (le contrôleur de scène la
        /// coupe à l'entrée et la restaure à la sortie). Bascule via touche N
        /// ou option du dev UI.
        /// </summary>
        public static bool ShowOverheadHUD = true;

        private void OnGUI()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null || _unit == null || _unit.Stats == null) return;

            Vector3 headBase = GetHeadWorldPosition();
            bool hasStatus = _unit.Stats.ActiveStatus != StatusEffect.None;

            Vector3 hudBoxPos = headBase + Vector3.up * (hasStatus ? 0.85f : 0.35f);
            Vector3 screenPos = cam.WorldToScreenPoint(hudBoxPos);

            if (screenPos.z > 0.5f)
            {
                float uiX = screenPos.x;
                float uiY = Screen.height - screenPos.y;

                // Liste des unités mise en cache pour un seul appel : sert à tous les tests de profondeur.
                var allUnits = TacticalUnitRegistry.AllUnits;
                float hudAlpha = ComputeVisionAlpha(cam, hudBoxPos, allUnits);

                if (ShowOverheadHUD)
                {
                    DrawOverheadHUD(uiX, uiY, hudAlpha);
                }
                if (ShowAnimationTelemetry)
                {
                    DrawAnimationTelemetry(uiX, uiY);
                }
                DrawCircularStatusHalo(cam, allUnits);
                DrawFloatingCombatTexts(cam, allUnits);

                if (_hoveredStatusInfo.HasValue)
                {
                    DrawStatusTooltip(_hoveredStatusInfo.Value, Event.current.mousePosition);
                    _hoveredStatusInfo = null;
                }
            }
        }

        /// <summary>
        /// Alpha de vision : 1 si lisible, <see cref="_occludedIconAlpha"/> si cet avatar est
        /// plus loin qu'un autre dans la vision (masqué derrière un avatar ou un décor).
        /// Combine occlusion décor (raycast) + test écran/profondeur inter-avatars
        /// (les avatars procéduraux n'ont pas de colliders, d'où le test manuel).
        /// </summary>
        private float ComputeVisionAlpha(UnityEngine.Camera cam, Vector3 worldPos, IReadOnlyList<TacticalUnit> allUnits)
        {
            if (cam == null) return 1f;
            float myDist = Vector3.Distance(cam.transform.position, worldPos);
            if (myDist < 0.001f) return 1f;

            Vector3 toIcon = worldPos - cam.transform.position;
            float dist = toIcon.magnitude;
            if (dist < 0.001f) return 1f;
            Vector3 rayDir = toIcon / dist;

            // 1) Décor / obstacle devant l'icône (covers, props, murs).
            if (dist > 0.5f)
            {
                var ray = new Ray(cam.transform.position, toIcon / dist);
                if (Physics.Raycast(ray, out RaycastHit hit, dist - 0.4f))
                {
                    var hitUnit = hit.transform != null ? hit.transform.GetComponentInParent<TacticalUnit>() : null;
                    if (hitUnit == null || hitUnit != _unit)
                    {
                        return _occludedIconAlpha;
                    }
                }
            }

            // 2) Un autre avatar plus proche dans la même zone d'écran masque celui-ci.
            Vector3 myScreen = cam.WorldToScreenPoint(worldPos);
            if (myScreen.z < 0.1f) return 1f;

            if (allUnits == null || allUnits.Count == 0) return 1f;

            for (int i = 0; i < allUnits.Count; i++)
            {
                var other = allUnits[i];
                if (other == null || other == _unit || other.Stats == null) continue;

                Vector3 otherHead = other.transform.position + Vector3.up * 1.85f;
                float otherDist = Vector3.Distance(cam.transform.position, otherHead);
                if (otherDist + _occlusionDepthBias >= myDist) continue;

                // 2a) Test volumétrique 3D : le rayon caméra -> icône traverse-t-il le corps
                // de l'avatar plus proche ? Indispensable quand la caméra est collée à
                // l'avatar : sa tête est alors hors écran (ou derrière le near plane) donc
                // le test écran ci-dessous ne peut plus le détecter, alors que son corps
                // remplit l'écran et bouche la vue (cas du screenshot).
                Vector3 otherFeet = other.transform.position + Vector3.up * 0.15f;
                Vector3 otherCenter = other.transform.position + Vector3.up * 1.0f;
                float tBody = Vector3.Dot(otherCenter - cam.transform.position, rayDir);
                if (tBody > 0.05f && tBody + _occlusionDepthBias < myDist)
                {
                    Vector3 closestOnRay = cam.transform.position + rayDir * tBody;
                    float qy = Mathf.Clamp(closestOnRay.y, otherFeet.y, otherHead.y);
                    Vector3 closestOnBody = new Vector3(other.transform.position.x, qy, other.transform.position.z);
                    if (Vector3.Distance(closestOnRay, closestOnBody) < _occlusionBodyRadius)
                    {
                        return _occludedIconAlpha;
                    }
                }

                Vector3 otherScreen = cam.WorldToScreenPoint(otherHead);
                if (otherScreen.z < 0.1f) continue;

                float pixelDist = Vector2.Distance(
                    new Vector2(myScreen.x, myScreen.y),
                    new Vector2(otherScreen.x, otherScreen.y));

                // La borne haute est volontairement large : de près, l'avatar occupe une
                // grande partie de l'écran, le rayon de masquage doit grandir en conséquence.
                float adaptiveRadius = _occlusionScreenRadiusPx * Mathf.Clamp(10f / Mathf.Max(1f, otherDist), 0.6f, 6f);
                if (pixelDist < adaptiveRadius)
                {
                    return _occludedIconAlpha;
                }
            }

            return 1f;
        }

        private void DrawAnimationTelemetry(float x, float y)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            bool isSelected = TacticalSelectionManager.Instance != null && TacticalSelectionManager.Instance.PrimarySelected == _unit;
            bool isActive = _unit.IsPlayerControlled && _unit.Stats.IsAlive;
            if (!isSelected && !isActive) return;

            var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
            var clipInfo = _animator.GetCurrentAnimatorClipInfo(0);
            AnimationClip curClip = (clipInfo != null && clipInfo.Length > 0) ? clipInfo[0].clip : null;
            string clipName = curClip != null ? curClip.name : "Sans Clip";
            bool isLooping = curClip != null && curClip.isLooping;

            float normTime = stateInfo.normalizedTime;
            float cyclePct = (normTime % 1f) * 100f;
            if (cyclePct < 0f) cyclePct += 100f;
            int loopCount = Mathf.Max(0, (int)normTime);

            bool inTrans = _animator.IsInTransition(0);

            float w = 220f;
            float h = inTrans ? 58f : 46f;
            float topY = y - 36f - h - 6f;
            Rect rect = new Rect(x - w * 0.5f, topY, w, h);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.015f, 0.035f, 0.065f, 0.94f);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            Color accentCol = inTrans ? new Color(1.0f, 0.72f, 0.15f, 0.95f) : new Color(0.0f, 0.90f, 1.0f, 0.85f);
            Color prevCol = GUI.color;
            GUI.color = accentCol;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2f), Texture2D.whiteTexture);
            GUI.color = prevCol;

            string loopTag = isLooping 
                ? "<color=#00FF88>[Loop: OUI]</color>" 
                : "<color=#FF4444>[Loop: NON]</color>";

            string line1 = $"🎬 <b>{clipName}</b>  {loopTag}";
            string line2 = $"⏱️ Cycle: <b>{cyclePct:0}%</b> (Tour #{loopCount})";

            if (inTrans)
            {
                var transInfo = _animator.GetAnimatorTransitionInfo(0);
                var nextClips = _animator.GetNextAnimatorClipInfo(0);
                string nextName = (nextClips != null && nextClips.Length > 0 && nextClips[0].clip != null) ? nextClips[0].clip.name : "Suivant";
                line2 += $" | <color=#FFB830>➔ {nextName} ({transInfo.normalizedTime * 100f:0}%)</color>";
            }

            bool inCombat = _animator.GetBool(AnimIsInCombat);
            bool isMoving = _animator.GetBool(AnimIsMoving);
            string combatTag = inCombat ? "<color=#00FF88>OUI</color>" : "<color=#FF6666>NON</color>";
            string moveTag = isMoving ? "<color=#00FF88>OUI</color>" : "<color=#8899AA>NON</color>";
            string line3 = $"⚙️ Combat: {combatTag} | Marche: {moveTag}";

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                richText = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 4, 1, 1)
            };
            labelStyle.normal.textColor = new Color(0.90f, 0.95f, 1.0f, 0.95f);

            GUI.Label(new Rect(rect.x + 4, rect.y + 3, rect.width - 8, 14), line1, labelStyle);
            GUI.Label(new Rect(rect.x + 4, rect.y + 17, rect.width - 8, 14), line2, labelStyle);
            GUI.Label(new Rect(rect.x + 4, rect.y + 31, rect.width - 8, 14), line3, labelStyle);
        }

        private void DrawOverheadHUD(float x, float y, float visionAlpha = 1f)
        {
            var stats = _unit.Stats;
            float width = 170f;
            float height = 34f;
            Rect rect = new Rect(x - width * 0.5f, y - height, width, height);

            float a = Mathf.Clamp01(visionAlpha);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.015f, 0.025f, 0.045f, 0.88f * a);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            Color nameCol = _unit.IsPlayerControlled ? new Color(0.35f, 0.80f, 1f) : new Color(1f, 0.35f, 0.35f);
            nameCol.a *= a;
            var nameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            nameStyle.normal.textColor = nameCol;
            GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, 15), stats.Name, nameStyle);

            float hpPct = Mathf.Clamp01((float)stats.CurrentHealth / Mathf.Max(1, stats.MaxHealth));
            Color hpColor = hpPct > 0.5f ? new Color(0.2f, 0.9f, 0.4f) : (hpPct > 0.25f ? Color.yellow : new Color(1f, 0.3f, 0.3f));
            hpColor.a *= a;
            var statStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            statStyle.normal.textColor = hpColor;
            GUI.Label(new Rect(rect.x + 4, rect.y + 16, rect.width - 8, 15), $"PV: {stats.CurrentHealth}/{stats.MaxHealth} | PA: {stats.CurrentActionPoints}/{stats.MaxActionPoints}", statStyle);
        }

        private void DrawCircularStatusHalo(UnityEngine.Camera cam, IReadOnlyList<TacticalUnit> allUnits = null)
        {
            if (_unit == null || _unit.Stats == null) return;

            var currentStatus = _unit.Stats.ActiveStatus;
            if (currentStatus == StatusEffect.None) return;

            var activeList = new List<StatusEffect>();
            for (int i = 0; i < _allStatusEffects.Length; i++)
            {
                var flag = _allStatusEffects[i];
                if (flag != StatusEffect.None && currentStatus.HasFlag(flag))
                {
                    activeList.Add(flag);
                }
            }

            int count = activeList.Count;
            if (count == 0) return;

            Vector3 headBase = GetHeadWorldPosition();
            Vector3 crownCenter = headBase + Vector3.up * 0.28f;

            float radius = count == 1 ? 0f : Mathf.Clamp(0.28f + (count * 0.04f), 0.30f, 0.52f) * _unitScale;
            float orbitSpeed = 0.80f;
            float baseAngle = Time.time * orbitSpeed;

            for (int i = 0; i < count; i++)
            {
                var status = activeList[i];
                var info = GetStatusVisualInfo(status);
                var iconTex = GetStatusIconTexture(status, info.PrimaryColor);

                Vector3 worldPos;
                if (count == 1)
                {
                    float bob = Mathf.Sin(Time.time * 2.8f) * 0.04f;
                    worldPos = crownCenter + Vector3.up * (0.08f + bob);
                }
                else
                {
                    float angle = baseAngle + (i * (Mathf.PI * 2f / count));
                    float bob = Mathf.Sin(Time.time * 2.4f + i * 1.2f) * 0.035f;
                    Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, bob, Mathf.Sin(angle) * radius);
                    worldPos = crownCenter + offset;
                }

                Vector3 screenPoint = cam.WorldToScreenPoint(worldPos);
                if (screenPoint.z <= 0.3f) continue;

                float dist = Vector3.Distance(cam.transform.position, worldPos);
                float badgeSize = Mathf.Clamp(340f / Mathf.Max(1f, dist), 24f, 32f);

                Vector2 pos2D = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
                Rect badgeRect = new Rect(pos2D.x - badgeSize * 0.5f, pos2D.y - badgeSize * 0.5f, badgeSize, badgeSize);

                // Icône plus loin qu'un autre avatar dans la vision -> transparente.
                float iconAlpha = ComputeVisionAlpha(cam, worldPos, allUnits);
                Color prevIconCol = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(iconAlpha));
                GUI.DrawTexture(badgeRect, iconTex);
                GUI.color = prevIconCol;

                if (iconAlpha > 0.6f && badgeRect.Contains(Event.current.mousePosition))
                {
                    _hoveredStatusInfo = info;
                }
            }
        }

        private void DrawStatusTooltip(StatusVisualInfo info, Vector2 mousePos)
        {
            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            titleStyle.normal.textColor = info.PrimaryColor;

            var descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                wordWrap = true
            };
            descStyle.normal.textColor = new Color(0.82f, 0.90f, 0.98f, 0.95f);

            float width = 250f;
            float textW = width - 20f;
            float descHeight = Mathf.Max(20f, descStyle.CalcHeight(new GUIContent(info.Description), textW));
            float height = 24f + descHeight + 10f;

            float x = Mathf.Clamp(mousePos.x + 14f, 10f, Screen.width - width - 10f);
            float y = Mathf.Clamp(mousePos.y - height - 6f, 10f, Screen.height - height - 10f);
            Rect rect = new Rect(x, y, width, height);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.012f, 0.022f, 0.040f, 0.96f);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            Color prevCol = GUI.color;
            GUI.color = info.PrimaryColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), Texture2D.whiteTexture);
            GUI.color = prevCol;

            GUI.Label(new Rect(rect.x + 10f, rect.y + 4f, textW, 18f), $"[{info.Tag}] {info.Name}", titleStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 22f, textW, descHeight), info.Description, descStyle);
        }

        private static Texture2D GetStatusIconTexture(StatusEffect status, Color primary)
        {
            if (_statusIconCache.TryGetValue(status, out var cached) && cached != null)
            {
                return cached;
            }

            var loaded = Resources.Load<Texture2D>($"Icons/Status/{status}")
                      ?? Resources.Load<Texture2D>($"Status/{status}");

            if (loaded != null)
            {
                _statusIconCache[status] = loaded;
                return loaded;
            }

            var generated = GenerateProceduralStatusIcon(status, primary);
            _statusIconCache[status] = generated;
            return generated;
        }

        private static Texture2D GenerateProceduralStatusIcon(StatusEffect status, Color primary)
        {
            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x - 31.5f) / 30f;
                    float v = (y - 31.5f) / 30f;
                    float r = Mathf.Sqrt(u * u + v * v);

                    if (r > 0.98f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    if (r >= 0.80f)
                    {
                        float ringAlpha = Mathf.Clamp01((0.98f - r) / 0.06f) * Mathf.Clamp01((r - 0.80f) / 0.06f);
                        byte rB = (byte)(primary.r * 255);
                        byte gB = (byte)(primary.g * 255);
                        byte bB = (byte)(primary.b * 255);
                        byte aB = (byte)(ringAlpha * 255);
                        pixels[y * size + x] = new Color32(rB, gB, bB, aB);
                        continue;
                    }

                    // Fond en verre fumé semi-transparent : la scène 3D reste visible
                    // à travers le badge (seuls l'anneau et le glyphe restent opaques).
                    Color bg = new Color(0.02f, 0.04f, 0.07f, 0.35f);
                    if (r >= 0.72f)
                    {
                        float edge = (r - 0.72f) / 0.08f;
                        bg = Color.Lerp(bg, primary * 0.5f, edge);
                    }

                    float shape = EvaluateGlyphShape(status, u, v);
                    if (shape > 0f)
                    {
                        Color iconColor = Color.Lerp(primary, Color.white, 0.45f);
                        Color blended = Color.Lerp(bg, iconColor, Mathf.Clamp01(shape));
                        pixels[y * size + x] = (Color32)blended;
                    }
                    else
                    {
                        pixels[y * size + x] = (Color32)bg;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static float EvaluateGlyphShape(StatusEffect status, float u, float v)
        {
            switch (status)
            {
                case StatusEffect.Inconscient:
                    float dHead = Mathf.Sqrt(u * u + (v - 0.12f) * (v - 0.12f));
                    bool bone = (dHead <= 0.28f) || (Mathf.Abs(u) <= 0.16f && v >= -0.34f && v <= 0.05f);
                    float dEyeL = Mathf.Sqrt((u + 0.10f) * (u + 0.10f) + (v - 0.10f) * (v - 0.10f));
                    float dEyeR = Mathf.Sqrt((u - 0.10f) * (u - 0.10f) + (v - 0.10f) * (v - 0.10f));
                    bool eye = (dEyeL <= 0.075f) || (dEyeR <= 0.075f);
                    bool nose = (v >= -0.10f && v <= -0.02f && Mathf.Abs(u) <= (v + 0.10f) * 0.7f);
                    bool teeth = (v >= -0.34f && v <= -0.22f && (Mathf.Abs(u - 0.055f) <= 0.02f || Mathf.Abs(u + 0.055f) <= 0.02f));
                    return (bone && !eye && !nose && !teeth) ? 1f : 0f;

                case StatusEffect.Sonne:
                    float angleS = Mathf.Atan2(v, u);
                    float distS = Mathf.Sqrt(u * u + v * v);
                    float starR = 0.15f + 0.32f * Mathf.Pow(Mathf.Abs(Mathf.Cos(4f * angleS)), 3f);
                    return distS <= starR ? 1f : 0f;

                case StatusEffect.ATerre:
                    bool groundBar = Mathf.Abs(u) <= 0.44f && v >= -0.36f && v <= -0.22f;
                    bool fallenHead = Mathf.Sqrt((u + 0.30f) * (u + 0.30f) + (v + 0.12f) * (v + 0.12f)) <= 0.08f;
                    bool arrowShaft = Mathf.Abs(u) <= 0.06f && v >= 0.02f && v <= 0.40f;
                    bool arrowHead = v >= -0.14f && v <= 0.06f && Mathf.Abs(u) <= (0.06f - v) * 1.35f;
                    return (groundBar || fallenHead || arrowShaft || arrowHead) ? 1f : 0f;

                case StatusEffect.Destabilise:
                    float ru = u * 0.866f - v * 0.5f;
                    float rv = u * 0.5f + v * 0.866f;
                    bool diamond = (Mathf.Abs(ru) + Mathf.Abs(rv) <= 0.42f) && !(Mathf.Abs(ru) + Mathf.Abs(rv) <= 0.22f);
                    bool crack = Mathf.Abs(u) <= 0.045f && Mathf.Abs(v) <= 0.38f;
                    return (diamond || crack) ? 1f : 0f;

                case StatusEffect.Etourdi:
                    float d1 = Mathf.Abs(u) + Mathf.Abs(v - 0.24f);
                    float d2 = Mathf.Abs(u + 0.22f) + Mathf.Abs(v + 0.14f);
                    float d3 = Mathf.Abs(u - 0.22f) + Mathf.Abs(v + 0.14f);
                    return (d1 <= 0.15f || d2 <= 0.15f || d3 <= 0.15f) ? 1f : 0f;

                case StatusEffect.Immobilise:
                    bool lockBody = Mathf.Abs(u) <= 0.24f && v >= -0.36f && v <= 0.02f;
                    bool lockShackle = v >= 0.02f && v <= 0.38f && Mathf.Abs(u) <= 0.18f && !(v <= 0.30f && Mathf.Abs(u) <= 0.09f);
                    bool lockHole = (Mathf.Sqrt(u * u + (v + 0.12f) * (v + 0.12f)) <= 0.045f) || (Mathf.Abs(u) <= 0.025f && v >= -0.25f && v <= -0.12f);
                    return ((lockBody || lockShackle) && !lockHole) ? 1f : 0f;

                case StatusEffect.Paralyse:
                    bool seg1 = v >= 0.02f && v <= 0.46f && Mathf.Abs(u - (v - 0.02f) * 0.4f - 0.04f) <= 0.08f;
                    bool seg2 = v <= 0.04f && v >= -0.46f && Mathf.Abs(u - (v + 0.46f) * 0.4f + 0.12f) <= 0.08f;
                    bool midBar = Mathf.Abs(v - 0.03f) <= 0.05f && u >= -0.18f && u <= 0.18f;
                    return (seg1 || seg2 || midBar) ? 1f : 0f;

                case StatusEffect.Ralenti:
                    bool topTri = v >= 0f && v <= 0.36f && Mathf.Abs(u) <= (v * 0.85f + 0.03f);
                    bool botTri = v <= 0f && v >= -0.36f && Mathf.Abs(u) <= (-v * 0.85f + 0.03f);
                    bool hCaps = (v >= 0.36f && v <= 0.43f && Mathf.Abs(u) <= 0.32f) || (v <= -0.36f && v >= -0.43f && Mathf.Abs(u) <= 0.32f);
                    return (topTri || botTri || hCaps) ? 1f : 0f;

                case StatusEffect.Agonisant:
                    float hVal = (u * u + (v - 0.10f) * (v - 0.10f) - 0.20f);
                    bool inHeart = (hVal * hVal * hVal - u * u * (v - 0.10f) * (v - 0.10f) * (v - 0.10f)) <= 0f;
                    bool ecgSlash = Mathf.Abs(u - v * 0.4f) <= 0.04f;
                    return (inHeart && !ecgSlash) ? 1f : 0f;

                case StatusEffect.Aveugle:
                    bool inEye = v <= 0.30f * (1f - (u / 0.44f) * (u / 0.44f)) && v >= -0.30f * (1f - (u / 0.44f) * (u / 0.44f)) && Mathf.Abs(u) <= 0.44f;
                    bool slashEye = Mathf.Abs(u - v) <= 0.05f && Mathf.Abs(u) <= 0.46f;
                    return (inEye || slashEye) ? 1f : 0f;

                case StatusEffect.Sourd:
                    float dEar = Mathf.Sqrt((u - 0.04f) * (u - 0.04f) + v * v);
                    bool earShape = (dEar <= 0.32f && !(dEar <= 0.16f && u >= 0.04f));
                    bool slashEar = Mathf.Abs(u - v) <= 0.05f && Mathf.Abs(u) <= 0.46f;
                    return (earShape || slashEar) ? 1f : 0f;

                case StatusEffect.Asphyxie:
                    bool l1 = Mathf.Sqrt((u + 0.15f) * (u + 0.15f) + (v + 0.06f) * (v + 0.06f)) <= 0.18f;
                    bool l2 = Mathf.Sqrt((u - 0.15f) * (u - 0.15f) + (v + 0.06f) * (v + 0.06f)) <= 0.18f;
                    bool trac = Mathf.Abs(u) <= 0.05f && v >= -0.10f && v <= 0.40f;
                    bool band = Mathf.Abs(v - 0.18f) <= 0.04f && Mathf.Abs(u) <= 0.22f;
                    return ((l1 || l2 || trac) && !band) ? 1f : 0f;

                case StatusEffect.Empoisonne:
                    float dDrop = Mathf.Sqrt(u * u + (v + 0.10f) * (v + 0.10f));
                    bool baseDrop = dDrop <= 0.25f;
                    bool tipDrop = v >= -0.10f && v <= 0.44f && Mathf.Abs(u) <= (0.44f - v) * 0.46f;
                    bool crossHole = (Mathf.Abs(u) <= 0.035f && Mathf.Abs(v + 0.10f) <= 0.09f) || (Mathf.Abs(v + 0.10f) <= 0.035f && Mathf.Abs(u) <= 0.09f);
                    return ((baseDrop || tipDrop) && !crossHole) ? 1f : 0f;

                case StatusEffect.EnFeu:
                    float fBase = Mathf.Sqrt(u * u + (v + 0.14f) * (v + 0.14f));
                    bool b1 = fBase <= 0.28f && v <= 0.05f;
                    bool bMain = v >= -0.05f && v <= 0.48f && Mathf.Abs(u) <= (0.48f - v) * 0.42f;
                    bool bL = v >= -0.05f && v <= 0.25f && Mathf.Abs(u + 0.12f) <= (0.25f - v) * 0.45f;
                    bool bR = v >= -0.05f && v <= 0.30f && Mathf.Abs(u - 0.12f) <= (0.30f - v) * 0.45f;
                    return (b1 || bMain || bL || bR) ? 1f : 0f;

                case StatusEffect.Saignement:
                    float dDrop1 = Mathf.Sqrt((u + 0.06f) * (u + 0.06f) + (v - 0.04f) * (v - 0.04f));
                    bool mainD = (dDrop1 <= 0.20f) || (v >= 0.04f && v <= 0.42f && Mathf.Abs(u + 0.06f) <= (0.42f - v) * 0.45f);
                    float dDrop2 = Mathf.Sqrt((u - 0.20f) * (u - 0.20f) + (v + 0.24f) * (v + 0.24f));
                    bool smallD = (dDrop2 <= 0.10f) || (v >= -0.24f && v <= -0.08f && Mathf.Abs(u - 0.20f) <= (-0.08f - v) * 0.5f);
                    return (mainD || smallD) ? 1f : 0f;

                default:
                    return (Mathf.Abs(u) + Mathf.Abs(v) <= 0.35f) ? 1f : 0f;
            }
        }

        private static StatusVisualInfo GetStatusVisualInfo(StatusEffect status)
        {
            return status switch
            {
                StatusEffect.Destabilise => new StatusVisualInfo { Status = status, Tag = "DST", Name = "DÉSTABILISÉ", Description = "-2 aux épreuves, coût PA x2 pour marcher", PrimaryColor = new Color(0.15f, 0.85f, 1f) },
                StatusEffect.Etourdi => new StatusVisualInfo { Status = status, Tag = "STN", Name = "ÉTOURDI", Description = "-1 sur tous les jets", PrimaryColor = new Color(1f, 0.85f, 0.2f) },
                StatusEffect.Immobilise => new StatusVisualInfo { Status = status, Tag = "IMM", Name = "IMMOBILISÉ", Description = "Mouvement impossible, -2 esquive", PrimaryColor = new Color(1f, 0.45f, 0.15f) },
                StatusEffect.Paralyse => new StatusVisualInfo { Status = status, Tag = "PAR", Name = "PARALYSÉ", Description = "Incapacité totale d'action/réaction", PrimaryColor = new Color(0.1f, 0.95f, 1f) },
                StatusEffect.Sonne => new StatusVisualInfo { Status = status, Tag = "SON", Name = "SONNÉ", Description = "Tombe à terre, aucune action possible", PrimaryColor = new Color(1f, 0.65f, 0.1f) },
                StatusEffect.ATerre => new StatusVisualInfo { Status = status, Tag = "PRN", Name = "À TERRE", Description = "-1 en attaque, -2 en défense/esquive", PrimaryColor = new Color(0.45f, 0.75f, 1f) },
                StatusEffect.Ralenti => new StatusVisualInfo { Status = status, Tag = "SLW", Name = "RALENTI", Description = "Toutes les actions coûtent le double de PA", PrimaryColor = new Color(0.3f, 0.9f, 0.95f) },
                StatusEffect.Agonisant => new StatusVisualInfo { Status = status, Tag = "AGN", Name = "AGONISANT", Description = "Panique motrice, détresse vitale (-2)", PrimaryColor = new Color(0.95f, 0.2f, 0.85f) },
                StatusEffect.Inconscient => new StatusVisualInfo { Status = status, Tag = "KO", Name = "INCONSCIENT", Description = "K.O. total / Syncope (Parade impossible)", PrimaryColor = new Color(0.35f, 0.65f, 1f) },
                StatusEffect.Aveugle => new StatusVisualInfo { Status = status, Tag = "AVG", Name = "AVEUGLE", Description = "-4 attaques à distance, -2 en mêlée", PrimaryColor = new Color(0.7f, 0.7f, 0.8f) },
                StatusEffect.Sourd => new StatusVisualInfo { Status = status, Tag = "SRD", Name = "SOURD", Description = "Perte d'initiative réflexe", PrimaryColor = new Color(0.75f, 0.75f, 0.8f) },
                StatusEffect.Asphyxie => new StatusVisualInfo { Status = status, Tag = "ASP", Name = "ASPHYXIE", Description = "Perte continue de PA, suffocation", PrimaryColor = new Color(0.2f, 0.95f, 0.75f) },
                StatusEffect.Empoisonne => new StatusVisualInfo { Status = status, Tag = "TOX", Name = "EMPOISONNÉ", Description = "Dégâts toxiques récurrents par tour", PrimaryColor = new Color(0.35f, 0.95f, 0.25f) },
                StatusEffect.EnFeu => new StatusVisualInfo { Status = status, Tag = "BRN", Name = "EN FEU", Description = "Dégâts thermiques continus", PrimaryColor = new Color(1f, 0.35f, 0.1f) },
                StatusEffect.Saignement => new StatusVisualInfo { Status = status, Tag = "BLD", Name = "SAIGNEMENT", Description = "Hémorragie active, perte continue de PV", PrimaryColor = new Color(1f, 0.2f, 0.25f) },
                StatusEffect.Souffrant => new StatusVisualInfo { Status = status, Tag = "SFR", Name = "SOUFFRANT", Description = "Blessure critique ouverte (Soins Majeurs CON×2 requis)", PrimaryColor = new Color(0.9f, 0.1f, 0.2f) },
                _ => new StatusVisualInfo { Status = status, Tag = "ALT", Name = "ALTÉRATION", Description = "Statut actif", PrimaryColor = Color.yellow }
            };
        }

        private void DrawFloatingCombatTexts(UnityEngine.Camera cam, IReadOnlyList<TacticalUnit> allUnits = null)
        {
            if (_hiddenByFog) return;
            for (int i = 0; i < _floatingTexts.Count; i++)
            {
                var ft = _floatingTexts[i];
                Vector3 sp = cam.WorldToScreenPoint(ft.WorldPos);
                if (sp.z > 0.3f)
                {
                    float lifetimeAlpha = Mathf.Clamp01(ft.Lifetime / (ft.MaxLifetime * 0.35f));
                    float visionAlpha = ComputeVisionAlpha(cam, ft.WorldPos, allUnits);
                    float alpha = lifetimeAlpha * Mathf.Clamp01(visionAlpha);
                    Color c = ft.Color;
                    c.a = alpha;

                    var style = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 13,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        wordWrap = false
                    };

                    GUIContent content = new GUIContent(ft.Text);
                    Vector2 size = style.CalcSize(content);
                    float boxWidth = Mathf.Max(size.x + 24f, 150f);
                    float boxHeight = Mathf.Max(size.y + 6f, 24f);

                    float clampedX = Mathf.Clamp(sp.x, boxWidth * 0.5f + 10f, Screen.width - boxWidth * 0.5f - 10f);
                    float clampedY = Mathf.Clamp(Screen.height - sp.y, boxHeight * 0.5f + 10f, Screen.height - boxHeight * 0.5f - 10f);

                    Rect boxRect = new Rect(clampedX - boxWidth * 0.5f, clampedY - boxHeight * 0.5f, boxWidth, boxHeight);

                    Color prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.02f, 0.035f, 0.06f, 0.90f * alpha);
                    GUI.Box(boxRect, GUIContent.none);
                    GUI.backgroundColor = prevBg;

                    style.normal.textColor = new Color(0f, 0f, 0f, 0.95f * alpha);
                    GUI.Label(new Rect(boxRect.x + 1, boxRect.y + 1, boxRect.width, boxRect.height), ft.Text, style);

                    style.normal.textColor = c;
                    GUI.Label(boxRect, ft.Text, style);
                }
            }
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------
        // Brouillard de guerre asymétrique : invisibilité des non-détectés.
        // ------------------------------------------------------------------

        private bool _hiddenByFog;

        /// <summary>Vrai quand cet avatar est masqué par le brouillard local.</summary>
        public bool IsHiddenByFog => _hiddenByFog;

        /// <summary>
        /// Masque / révèle l'avatar sans toucher à la logique (pathfinding, PV, PA).
        /// Masqué : modèle 3D + arme équipée désactivés, collider insensible au clic
        /// (impossible de cibler un ennemi invisible), textes flottants purgés.
        /// La logique (position, tour, IA, MJ) continue de tourner normalement.
        /// </summary>
        public void SetHiddenByFog(bool hidden)
        {
            if (_hiddenByFog == hidden) return;
            _hiddenByFog = hidden;
            try
            {
                if (_modelRoot != null) _modelRoot.gameObject.SetActive(!hidden);
                if (_equippedWeaponInstance != null) _equippedWeaponInstance.SetActive(!hidden);
                var col = GetComponent<Collider>();
                if (col != null) col.enabled = !hidden;
                if (hidden) _floatingTexts.Clear();
            }
            catch { /* ignore */ }
        }
    }
}
