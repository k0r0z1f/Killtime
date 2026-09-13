using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Killtime.Tactics.Units;
using Killtime.Core.Character;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// Gestionnaire visuel 3D pour une unité tactique.
    /// Assure la création d'avatars procéduraux protégés contre les shaders manquants,
    /// les barres de vie en billboard et les textes de dégâts flottants.
    /// </summary>
    [RequireComponent(typeof(TacticalUnit))]
    public class TacticalUnitVisual : MonoBehaviour
    {
        [Header("Équipe & Thème")]
        [SerializeField] private Color _bodyColor = new Color(0.2f, 0.45f, 0.9f);
        [SerializeField] private Color _visorColor = new Color(0.0f, 0.9f, 1.0f);
        [SerializeField] private float _unitScale = 1.0f;

        private TacticalUnit _unit;
        private Transform _modelRoot;
        private MeshRenderer _bodyRenderer;
        private MeshRenderer _teamDiskRenderer;
        private readonly List<Renderer> _customModelRenderers = new();
        private MaterialPropertyBlock _propBlock;
        private bool _isCustomModel = false;

        private readonly List<FloatingText> _floatingTexts = new();
        private float _hitFlashTimer = 0f;
        private Color _currentBodyColor;

        private static readonly Dictionary<StatusEffect, Texture2D> _statusIconCache = new();
        private StatusVisualInfo? _hoveredStatusInfo;
        private static readonly StatusEffect[] _allStatusEffects = (StatusEffect[])System.Enum.GetValues(typeof(StatusEffect));

        private struct StatusVisualInfo
        {
            public StatusEffect Status;
            public string Tag;
            public string Name;
            public string Description;
            public Color PrimaryColor;
        }

        private class FloatingText
        {
            public string Text;
            public Color Color;
            public Vector3 WorldPos;
            public float Lifetime;
            public float MaxLifetime;
        }

        private void Awake()
        {
            _unit = GetComponent<TacticalUnit>();
            _propBlock = new MaterialPropertyBlock();

            string modelName = _unit != null && _unit.Sheet != null ? _unit.Sheet.ModelPrefabName : null;
            if (!string.IsNullOrEmpty(modelName))
            {
                ApplyCustomModel(modelName);
            }
            else
            {
                BuildProceduralAvatar();
            }
        }

        private void Update()
        {
            if (Time.timeScale <= 0.0001f) return;

            UpdateFloatingTexts();
            UpdateHitFlash();
            UpdateKOAnimation();
        }

        public void SetColor(Color bodyColor, Color visorColor)
        {
            _bodyColor = bodyColor;
            _visorColor = visorColor;
            _currentBodyColor = bodyColor;
            ApplyBodyColor(bodyColor);
        }

        private Material CreateSafeUnitMaterial(string matName)
        {
            Shader targetShader = null;

            var currentRP = GraphicsSettings.currentRenderPipeline;
            if (currentRP != null)
            {
                string rpName = currentRP.GetType().Name;
                if (rpName.Contains("Universal") || rpName.Contains("URP"))
                {
                    targetShader = Shader.Find("Universal Render Pipeline/Lit")
                                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                                ?? Shader.Find("Universal Render Pipeline/Unlit");
                }
                else if (rpName.Contains("HighDefinition") || rpName.Contains("HDRP"))
                {
                    targetShader = Shader.Find("HDRP/Lit")
                                ?? Shader.Find("HDRP/Unlit");
                }
            }

            if (targetShader == null)
            {
                targetShader = Shader.Find("Killtime/TacticalLit")
                            ?? Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Standard")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("Sprites/Default");
            }

            return new Material(targetShader) { name = matName };
        }

        public bool ApplyCustomModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName) || modelName == "(Procédural)")
            {
                BuildProceduralAvatar();
                return true;
            }

            string cleanName = modelName.StartsWith("Characters/") ? modelName.Substring("Characters/".Length) : modelName;
            GameObject prefab = Resources.Load<GameObject>($"Characters/{cleanName}") ?? Resources.Load<GameObject>(cleanName);

            if (prefab == null)
            {
                Debug.LogWarning($"[TacticalUnitVisual] Modèle introuvable sous Resources/Characters/{cleanName}. Repli sur l'avatar procédural.");
                BuildProceduralAvatar();
                return false;
            }

            if (_modelRoot != null) Destroy(_modelRoot.gameObject);

            _modelRoot = new GameObject("AvatarModel").transform;
            _modelRoot.SetParent(transform, false);

            GameObject instance = Instantiate(prefab, _modelRoot);
            instance.name = cleanName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * _unitScale;

            var colliders = instance.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            _customModelRenderers.Clear();
            _customModelRenderers.AddRange(instance.GetComponentsInChildren<Renderer>());
            _bodyRenderer = instance.GetComponentInChildren<MeshRenderer>();

            Material baseMat = CreateSafeUnitMaterial("UnitFactionRing_Mat");
            var disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disk.name = "FactionRing";
            disk.transform.SetParent(_modelRoot, false);
            disk.transform.localPosition = new Vector3(0, 0.02f, 0);
            disk.transform.localScale = new Vector3(0.95f, 0.02f, 0.95f) * _unitScale;

            var diskCol = disk.GetComponent<Collider>();
            if (diskCol != null) Destroy(diskCol);

            _teamDiskRenderer = disk.GetComponent<MeshRenderer>();
            _teamDiskRenderer.sharedMaterial = baseMat;

            _isCustomModel = true;
            _currentBodyColor = _unit != null && _unit.IsPlayerControlled 
                ? new Color(0.15f, 0.45f, 0.85f) 
                : new Color(0.85f, 0.25f, 0.2f);

            ApplyBodyColor(_currentBodyColor);
            return true;
        }

        public void BuildProceduralAvatar()
        {
            _isCustomModel = false;
            _customModelRenderers.Clear();
            _teamDiskRenderer = null;

            if (_modelRoot != null) Destroy(_modelRoot.gameObject);

            _modelRoot = new GameObject("AvatarModel").transform;
            _modelRoot.SetParent(transform, false);

            Material baseMat = CreateSafeUnitMaterial("UnitAvatar_Mat");

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "BodyCapsule";
            body.transform.SetParent(_modelRoot, false);
            body.transform.localPosition = new Vector3(0, 1.0f * _unitScale, 0);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f) * _unitScale;

            var capsuleCol = body.GetComponent<Collider>();
            if (capsuleCol != null) Destroy(capsuleCol);

            _bodyRenderer = body.GetComponent<MeshRenderer>();
            _bodyRenderer.sharedMaterial = baseMat;
            _currentBodyColor = _unit != null && _unit.IsPlayerControlled ? new Color(0.15f, 0.45f, 0.85f) : new Color(0.85f, 0.25f, 0.2f);
            ApplyBodyColor(_currentBodyColor);

            var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor";
            visor.transform.SetParent(_modelRoot, false);
            visor.transform.localPosition = new Vector3(0, 1.45f * _unitScale, 0.28f * _unitScale);
            visor.transform.localScale = new Vector3(0.45f, 0.15f, 0.25f) * _unitScale;

            var visorCol = visor.GetComponent<Collider>();
            if (visorCol != null) Destroy(visorCol);

            var visorRend = visor.GetComponent<MeshRenderer>();
            visorRend.sharedMaterial = baseMat;
            var visorProp = new MaterialPropertyBlock();
            Color vColor = _unit != null && _unit.IsPlayerControlled ? new Color(0.0f, 0.95f, 1.0f) : new Color(1.0f, 0.75f, 0.1f);
            visorProp.SetColor("_BaseColor", vColor);
            visorProp.SetColor("_Color", vColor);
            visorProp.SetColor("_EmissionColor", vColor * 1.5f);
            visorRend.SetPropertyBlock(visorProp);

            var pointer = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pointer.name = "AimPointer";
            pointer.transform.SetParent(_modelRoot, false);
            pointer.transform.localPosition = new Vector3(0, 0.05f, 0.55f * _unitScale);
            pointer.transform.localRotation = Quaternion.Euler(90, 0, 0);
            pointer.transform.localScale = new Vector3(0.12f, 0.15f, 0.12f) * _unitScale;

            var pointerCol = pointer.GetComponent<Collider>();
            if (pointerCol != null) Destroy(pointerCol);

            var pointerRend = pointer.GetComponent<MeshRenderer>();
            pointerRend.sharedMaterial = baseMat;
            var pointerProp = new MaterialPropertyBlock();
            pointerProp.SetColor("_BaseColor", Color.white);
            pointerProp.SetColor("_Color", Color.white);
            pointerRend.SetPropertyBlock(pointerProp);
        }

        private void ApplyBodyColor(Color color)
        {
            if (_teamDiskRenderer != null)
            {
                _propBlock.SetColor("_BaseColor", color);
                _propBlock.SetColor("_Color", color);
                _teamDiskRenderer.SetPropertyBlock(_propBlock);
            }

            if (!_isCustomModel && _bodyRenderer != null)
            {
                _propBlock.SetColor("_BaseColor", color);
                _propBlock.SetColor("_Color", color);
                _bodyRenderer.SetPropertyBlock(_propBlock);
            }
        }

        public void TriggerHitFlash()
        {
            _hitFlashTimer = 0.25f;

            if (_isCustomModel)
            {
                _propBlock.SetColor("_BaseColor", Color.white);
                _propBlock.SetColor("_Color", Color.white);
                for (int i = 0; i < _customModelRenderers.Count; i++)
                {
                    if (_customModelRenderers[i] != null)
                    {
                        _customModelRenderers[i].SetPropertyBlock(_propBlock);
                    }
                }
            }
            else
            {
                ApplyBodyColor(Color.white);
            }
        }

        private void UpdateHitFlash()
        {
            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer -= Time.deltaTime;
                if (_hitFlashTimer <= 0f)
                {
                    if (_isCustomModel)
                    {
                        for (int i = 0; i < _customModelRenderers.Count; i++)
                        {
                            if (_customModelRenderers[i] != null)
                            {
                                _customModelRenderers[i].SetPropertyBlock(null);
                            }
                        }
                    }
                    ApplyBodyColor(_currentBodyColor);
                }
            }
        }

        private void UpdateKOAnimation()
        {
            if (_unit.Stats != null && !_unit.Stats.IsAlive)
            {
                Quaternion targetRot = Quaternion.Euler(-80, transform.rotation.eulerAngles.y, 0);
                if (_modelRoot != null)
                {
                    _modelRoot.localRotation = Quaternion.Slerp(_modelRoot.localRotation, targetRot, Time.deltaTime * 6f);
                    _modelRoot.localPosition = Vector3.Lerp(_modelRoot.localPosition, new Vector3(0, -0.4f, 0), Time.deltaTime * 6f);
                }
            }
            else
            {
                if (_modelRoot != null)
                {
                    _modelRoot.localRotation = Quaternion.Slerp(_modelRoot.localRotation, Quaternion.identity, Time.deltaTime * 6f);
                    _modelRoot.localPosition = Vector3.Lerp(_modelRoot.localPosition, Vector3.zero, Time.deltaTime * 6f);
                }
            }
        }

        public void SpawnFloatingText(string message, Color color)
        {
            float stackOffset = _floatingTexts.Count * 0.42f;
            _floatingTexts.Add(new FloatingText
            {
                Text = message,
                Color = color,
                WorldPos = transform.position + Vector3.up * (2.2f + stackOffset),
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
                ft.WorldPos += Vector3.up * (0.22f * Time.unscaledDeltaTime);

                if (ft.Lifetime <= 0f)
                {
                    _floatingTexts.RemoveAt(i);
                }
            }
        }

        private Vector3 GetHeadWorldPosition()
        {
            if (_modelRoot != null)
            {
                Vector3 p = _modelRoot.TransformPoint(new Vector3(0, 1.85f * _unitScale, 0));
                if (_unit.Stats != null && !_unit.Stats.IsAlive)
                {
                    p.y = Mathf.Max(p.y, transform.position.y + 0.75f);
                }
                return p;
            }
            return transform.position + Vector3.up * (1.85f * _unitScale);
        }

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

                DrawOverheadHUD(uiX, uiY);
                DrawCircularStatusHalo(cam);
                DrawFloatingCombatTexts(cam);

                if (_hoveredStatusInfo.HasValue)
                {
                    DrawStatusTooltip(_hoveredStatusInfo.Value, Event.current.mousePosition);
                    _hoveredStatusInfo = null;
                }
            }
        }

        private void DrawOverheadHUD(float x, float y)
        {
            var stats = _unit.Stats;
            float width = 150;
            float height = 36;
            Rect rect = new Rect(x - width * 0.5f, y - height, width, height);

            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(rect);

            GUI.color = _unit.IsPlayerControlled ? new Color(0.4f, 0.8f, 1f) : new Color(1f, 0.4f, 0.4f);
            GUILayout.Label($"<b>{stats.Name}</b>", new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter });

            float hpPct = Mathf.Clamp01((float)stats.CurrentHealth / Mathf.Max(1, stats.MaxHealth));
            Color hpColor = hpPct > 0.5f ? Color.green : (hpPct > 0.25f ? Color.yellow : Color.red);
            GUI.color = hpColor;
            GUILayout.Label($"PV: {stats.CurrentHealth}/{stats.MaxHealth} | PA: {stats.CurrentActionPoints}/{stats.MaxActionPoints}", 
                new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.MiddleCenter });

            GUI.color = Color.white;
            GUILayout.EndArea();
        }

        private void DrawCircularStatusHalo(UnityEngine.Camera cam)
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

                GUI.DrawTexture(badgeRect, iconTex);

                if (badgeRect.Contains(Event.current.mousePosition))
                {
                    _hoveredStatusInfo = info;
                }
            }
        }

        private void DrawStatusTooltip(StatusVisualInfo info, Vector2 mousePos)
        {
            float width = 230f;
            float height = 46f;
            float x = Mathf.Clamp(mousePos.x + 16f, 10f, Screen.width - width - 10f);
            float y = Mathf.Clamp(mousePos.y - 24f, 10f, Screen.height - height - 10f);
            Rect rect = new Rect(x, y, width, height);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.012f, 0.022f, 0.040f, 0.96f);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            Color prevCol = GUI.color;
            GUI.color = info.PrimaryColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), Texture2D.whiteTexture);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            titleStyle.normal.textColor = info.PrimaryColor;
            GUI.Label(new Rect(rect.x + 10f, rect.y + 4f, rect.width - 18f, 18f), $"[{info.Tag}] {info.Name}", titleStyle);

            var descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                wordWrap = true
            };
            descStyle.normal.textColor = new Color(0.80f, 0.88f, 0.96f, 0.92f);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 22f, rect.width - 18f, 22f), info.Description, descStyle);

            GUI.color = prevCol;
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

                    Color bg = new Color(0.02f, 0.04f, 0.07f, 0.96f);
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

                case StatusEffect.ChronoFracture:
                    float dDial = Mathf.Sqrt(u * u + v * v);
                    bool dial = dDial <= 0.36f && !(dDial <= 0.26f);
                    bool handH = Mathf.Abs(v) <= 0.035f && u >= 0f && u <= 0.24f;
                    bool handV = Mathf.Abs(u) <= 0.035f && v >= 0f && v <= 0.26f;
                    bool fracture = Mathf.Abs(u - v * 0.5f) <= 0.04f && u >= 0.10f;
                    return (dial || handH || handV || fracture) ? 1f : 0f;

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
                StatusEffect.ChronoFracture => new StatusVisualInfo { Status = status, Tag = "CHR", Name = "CHRONO-FRACTURE", Description = "Déphasage causal du Fleuve du Temps", PrimaryColor = new Color(0f, 0.95f, 1f) },
                _ => new StatusVisualInfo { Status = status, Tag = "ALT", Name = "ALTÉRATION", Description = "Statut actif", PrimaryColor = Color.yellow }
            };
        }

        private void DrawFloatingCombatTexts(UnityEngine.Camera cam)
        {
            for (int i = 0; i < _floatingTexts.Count; i++)
            {
                var ft = _floatingTexts[i];
                Vector3 sp = cam.WorldToScreenPoint(ft.WorldPos);
                if (sp.z > 0.3f)
                {
                    float alpha = Mathf.Clamp01(ft.Lifetime / (ft.MaxLifetime * 0.35f));
                    Color c = ft.Color;
                    c.a = alpha;

                    float clampedX = Mathf.Clamp(sp.x, 160, Screen.width - 160);
                    float clampedY = Mathf.Clamp(Screen.height - sp.y - (i * 28), 40, Screen.height - 45);

                    Rect boxRect = new Rect(clampedX - 150, clampedY - 15, 300, 28);

                    Color prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.04f, 0.05f, 0.09f, 0.88f * alpha);
                    GUI.Box(boxRect, GUIContent.none);
                    GUI.backgroundColor = prevBg;

                    var style = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 15,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter
                    };

                    style.normal.textColor = new Color(0f, 0f, 0f, 0.9f * alpha);
                    GUI.Label(new Rect(boxRect.x + 1, boxRect.y + 1, boxRect.width, boxRect.height), ft.Text, style);

                    style.normal.textColor = c;
                    GUI.Label(boxRect, ft.Text, style);
                }
            }
            GUI.color = Color.white;
        }
    }
}