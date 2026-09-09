using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Core.Character;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// Gestionnaire visuel 3D pour une unité tactique.
    /// Génère le modèle 3D procédural (corps, visière, pointeur d'orientation),
    /// les barres de vie/PA en billboard au-dessus de la tête,
    /// et les textes de combat flottants (dégâts, esquives, statuts).
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
        private MaterialPropertyBlock _propBlock;

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
            BuildProceduralAvatar();
        }

        private void Update()
        {
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

        private void BuildProceduralAvatar()
        {
            if (_modelRoot != null) Destroy(_modelRoot.gameObject);

            _modelRoot = new GameObject("AvatarModel").transform;
            _modelRoot.SetParent(transform, false);

            // Extraire le matériau URP par défaut d'une primitive temporaire
            var tempPrim = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tempPrim.hideFlags = HideFlags.HideAndDontSave;
            var mat = new Material(tempPrim.GetComponent<MeshRenderer>().sharedMaterial);
            DestroyImmediate(tempPrim);

            // 1. Corps principal (Capsule)
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "BodyCapsule";
            body.transform.SetParent(_modelRoot, false);
            body.transform.localPosition = new Vector3(0, 1.0f * _unitScale, 0);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f) * _unitScale;

            // Retirer le collider physique de l'avatar pour laisser le contrôle à la grille
            var capsuleCol = body.GetComponent<Collider>();
            if (capsuleCol != null) Destroy(capsuleCol);

            _bodyRenderer = body.GetComponent<MeshRenderer>();
            _bodyRenderer.material = mat;
            _currentBodyColor = _unit.IsPlayerControlled ? new Color(0.15f, 0.45f, 0.85f) : new Color(0.85f, 0.25f, 0.2f);
            ApplyBodyColor(_currentBodyColor);

            // 2. Visière / Tête
            var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor";
            visor.transform.SetParent(_modelRoot, false);
            visor.transform.localPosition = new Vector3(0, 1.45f * _unitScale, 0.28f * _unitScale);
            visor.transform.localScale = new Vector3(0.45f, 0.15f, 0.25f) * _unitScale;

            var visorCol = visor.GetComponent<Collider>();
            if (visorCol != null) Destroy(visorCol);

            var visorRend = visor.GetComponent<MeshRenderer>();
            visorRend.material = mat;
            var visorProp = new MaterialPropertyBlock();
            Color vColor = _unit.IsPlayerControlled ? new Color(0.0f, 0.95f, 1.0f) : new Color(1.0f, 0.75f, 0.1f);
            visorProp.SetColor("_BaseColor", vColor);
            visorProp.SetColor("_Color", vColor);
            visorProp.SetColor("_EmissionColor", vColor * 1.5f);
            visorRend.SetPropertyBlock(visorProp);

            // 3. Pointeur de visée avant (petit cône/cylindre d'orientation)
            var pointer = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pointer.name = "AimPointer";
            pointer.transform.SetParent(_modelRoot, false);
            pointer.transform.localPosition = new Vector3(0, 0.05f, 0.55f * _unitScale);
            pointer.transform.localRotation = Quaternion.Euler(90, 0, 0);
            pointer.transform.localScale = new Vector3(0.12f, 0.15f, 0.12f) * _unitScale;

            var pointerCol = pointer.GetComponent<Collider>();
            if (pointerCol != null) Destroy(pointerCol);

            var pointerRend = pointer.GetComponent<MeshRenderer>();
            pointerRend.material = mat;
            var pointerProp = new MaterialPropertyBlock();
            pointerProp.SetColor("_BaseColor", Color.white);
            pointerProp.SetColor("_Color", Color.white);
            pointerRend.SetPropertyBlock(pointerProp);
        }

        private void ApplyBodyColor(Color color)
        {
            if (_bodyRenderer != null)
            {
                _propBlock.SetColor("_BaseColor", color);
                _propBlock.SetColor("_Color", color);
                _bodyRenderer.SetPropertyBlock(_propBlock);
            }
        }

        public void TriggerHitFlash()
        {
            _hitFlashTimer = 0.25f;
            ApplyBodyColor(Color.white);
        }

        private void UpdateHitFlash()
        {
            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer -= Time.deltaTime;
                if (_hitFlashTimer <= 0f)
                {
                    ApplyBodyColor(_currentBodyColor);
                }
            }
        }

        private void UpdateKOAnimation()
        {
            if (_unit.Stats != null && !_unit.Stats.IsAlive)
            {
                // Animation de chute au sol (couché sur le dos)
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
            _floatingTexts.Add(new FloatingText
            {
                Text = message,
                Color = color,
                WorldPos = transform.position + Vector3.up * 2.2f,
                Lifetime = 1.6f,
                MaxLifetime = 1.6f
            });
        }

        private void UpdateFloatingTexts()
        {
            for (int i = _floatingTexts.Count - 1; i >= 0; i--)
            {
                var ft = _floatingTexts[i];
                ft.Lifetime -= Time.deltaTime;
                ft.WorldPos += Vector3.up * (0.6f * Time.deltaTime);

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

            // 1. Rendu de l'Overhead Billboard
            Vector3 headWorldPos = transform.position + Vector3.up * 2.2f;
            Vector3 screenPos = cam.WorldToScreenPoint(headWorldPos);

            if (screenPos.z > 0.5f) // Devant la caméra
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

            // Fond discret
            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(rect);

            // Nom de l'unité
            GUI.color = _unit.IsPlayerControlled ? new Color(0.4f, 0.8f, 1f) : new Color(1f, 0.4f, 0.4f);
            GUILayout.Label($"<b>{stats.Name}</b>", new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter });

            // Barre de vie (PV)
            float hpPct = Mathf.Clamp01((float)stats.CurrentHealth / Mathf.Max(1, stats.MaxHealth));
            Color hpColor = hpPct > 0.5f ? Color.green : (hpPct > 0.25f ? Color.yellow : Color.red);
            GUI.color = hpColor;
            GUILayout.Label($"PV: {stats.CurrentHealth}/{stats.MaxHealth} | PA: {stats.CurrentActionPoints}/{stats.MaxActionPoints}", 
                new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter });

            // Badges de statuts actifs
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
            foreach (var ft in _floatingTexts)
            {
                Vector3 sp = cam.WorldToScreenPoint(ft.WorldPos);
                if (sp.z > 0.5f)
                {
                    float alpha = Mathf.Clamp01(ft.Lifetime / (ft.MaxLifetime * 0.5f));
                    Color c = ft.Color;
                    c.a = alpha;
                    GUI.color = c;

                    var style = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 14,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter
                    };

                    GUI.Label(new Rect(sp.x - 120, Screen.height - sp.y - 20, 240, 40), ft.Text, style);
                }
            }
            GUI.color = Color.white;
        }
    }
}
