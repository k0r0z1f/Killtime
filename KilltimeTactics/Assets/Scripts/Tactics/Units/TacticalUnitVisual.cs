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
                targetShader = Shader.Find("Universal Render Pipeline/Lit")
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

            // 1. Corps principal
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

            // 2. Visière
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

            // 3. Pointeur d'orientation
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

        private void OnGUI()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null || _unit == null || _unit.Stats == null) return;

            Vector3 headWorldPos = transform.position + Vector3.up * 2.2f;
            Vector3 screenPos = cam.WorldToScreenPoint(headWorldPos);

            if (screenPos.z > 0.5f)
            {
                float uiX = screenPos.x;
                float uiY = Screen.height - screenPos.y;

                DrawOverheadHUD(uiX, uiY);
                DrawFloatingCombatTexts(cam);
            }
        }

        private void DrawOverheadHUD(float x, float y)
        {
            var stats = _unit.Stats;
            float width = 160;
            float height = 48;
            Rect rect = new Rect(x - width * 0.5f, y - height, width, height);

            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(rect);

            GUI.color = _unit.IsPlayerControlled ? new Color(0.4f, 0.8f, 1f) : new Color(1f, 0.4f, 0.4f);
            GUILayout.Label($"<b>{stats.Name}</b>", new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter });

            float hpPct = Mathf.Clamp01((float)stats.CurrentHealth / Mathf.Max(1, stats.MaxHealth));
            Color hpColor = hpPct > 0.5f ? Color.green : (hpPct > 0.25f ? Color.yellow : Color.red);
            GUI.color = hpColor;
            GUILayout.Label($"PV: {stats.CurrentHealth}/{stats.MaxHealth} | PA: {stats.CurrentActionPoints}/{stats.MaxActionPoints}", 
                new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter });

            if (stats.ActiveStatus != StatusEffect.None)
            {
                GUI.color = Color.magenta;
                GUILayout.Label($"[{stats.ActiveStatus}]", new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.MiddleCenter });
            }

            GUI.color = Color.white;
            GUILayout.EndArea();
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