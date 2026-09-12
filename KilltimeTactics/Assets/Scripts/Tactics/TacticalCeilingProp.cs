using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Killtime.Tactics.Grid
{
    /// <summary>
    /// Contrôleur visuel et physique pour tout élément fixé au plafond (lampes, tuyaux, poutres).
    /// Assure la synchronisation exacte de transparence avec le plafond et interdit tout blocage de clic.
    /// </summary>
    public class TacticalCeilingProp : MonoBehaviour
    {
        private static readonly List<TacticalCeilingProp> _allCeilingProps = new();

        private readonly List<Renderer> _renderers = new();
        private readonly List<Material> _transparentMaterials = new();
        private readonly List<Color> _originalColors = new();
        private readonly List<Light> _attachedLights = new();
        private readonly List<float> _originalLightIntensities = new();
        private MaterialPropertyBlock _propBlock;
        private bool _isInitialized = false;

        private void Awake()
        {
            if (!_isInitialized)
            {
                Initialize();
            }
        }

        private void OnEnable()
        {
            if (!_allCeilingProps.Contains(this))
            {
                _allCeilingProps.Add(this);
            }
        }

        private void OnDisable()
        {
            _allCeilingProps.Remove(this);
        }

        private void OnDestroy()
        {
            _allCeilingProps.Remove(this);
            for (int i = 0; i < _transparentMaterials.Count; i++)
            {
                if (_transparentMaterials[i] != null)
                {
                    Destroy(_transparentMaterials[i]);
                }
            }
            _transparentMaterials.Clear();
            _originalColors.Clear();
            _attachedLights.Clear();
            _originalLightIntensities.Clear();
        }

        public void Initialize()
        {
            if (_isInitialized) return;

            if (_propBlock == null)
            {
                _propBlock = new MaterialPropertyBlock();
            }

            // 1. Inhibition absolue des raycasts et clics
            var colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            gameObject.layer = 2; // Layer 2 = Ignore Raycast
            foreach (Transform child in transform)
            {
                child.gameObject.layer = 2;
            }

            // 2. Récupération du matériau transparent officiel du plafond
            var visualizer = FindAnyObjectByType<HexGridVisualizer>();
            Material masterCeilingMat = visualizer != null ? visualizer.CeilingMaterial : null;

            _renderers.Clear();
            _transparentMaterials.Clear();
            _originalColors.Clear();

            _renderers.AddRange(GetComponentsInChildren<Renderer>(true));

            for (int i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;

                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;

                var currentMats = r.sharedMaterials;
                var newMats = new Material[currentMats.Length];

                for (int m = 0; m < currentMats.Length; m++)
                {
                    var orig = currentMats[m];

                    Material transMat;
                    if (masterCeilingMat != null)
                    {
                        transMat = new Material(masterCeilingMat)
                        {
                            name = (orig != null ? orig.name : "PropMat") + "_CeilingGlass"
                        };
                    }
                    else
                    {
                        Shader fallbackShader = Shader.Find("Universal Render Pipeline/Unlit")
                                             ?? Shader.Find("Unlit/Transparent")
                                             ?? Shader.Find("Standard");
                        transMat = new Material(fallbackShader);
                    }

                    if (orig != null)
                    {
                        if (orig.HasProperty("_BaseMap") && orig.GetTexture("_BaseMap") != null)
                            transMat.SetTexture("_BaseMap", orig.GetTexture("_BaseMap"));
                        if (orig.HasProperty("_MainTex") && orig.GetTexture("_MainTex") != null)
                            transMat.SetTexture("_MainTex", orig.GetTexture("_MainTex"));
                    }

                    transMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    transMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    transMat.SetInt("_ZWrite", 0);
                    transMat.SetInt("_Cull", (int)CullMode.Off);
                    transMat.renderQueue = (int)RenderQueue.Transparent + 105;

                    _transparentMaterials.Add(transMat);
                    newMats[m] = transMat;
                }

                r.sharedMaterials = newMats;
            }

            _attachedLights.Clear();
            _originalLightIntensities.Clear();
            var lights = GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l != null)
                {
                    _attachedLights.Add(l);
                    _originalLightIntensities.Add(l.intensity > 0f ? l.intensity : 1f);
                    l.enabled = true;
                }
            }

            _isInitialized = true;
        }

        public void ApplyVisualAlpha(float alpha, bool isVisible)
        {
            if (!_isInitialized) Initialize();
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

            float effectivePropAlpha = (alpha <= 0.035f) ? 0.35f : alpha;
            Color ceilingColor = new Color(0.12f, 0.75f, 0.98f, effectivePropAlpha);

            for (int rIdx = 0; rIdx < _renderers.Count; rIdx++)
            {
                var r = _renderers[rIdx];
                if (r == null) continue;

                r.enabled = isVisible;
                if (!isVisible) continue;

                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat != null)
                    {
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ceilingColor);
                        if (mat.HasProperty("_Color")) mat.SetColor("_Color", ceilingColor);
                    }
                }

                _propBlock.SetColor("_BaseColor", ceilingColor);
                _propBlock.SetColor("_Color", ceilingColor);
                r.SetPropertyBlock(_propBlock);
            }

            if (_attachedLights.Count == 0)
            {
                var lights = GetComponentsInChildren<Light>(true);
                for (int i = 0; i < lights.Length; i++)
                {
                    var l = lights[i];
                    if (l != null)
                    {
                        _attachedLights.Add(l);
                        _originalLightIntensities.Add(l.intensity > 0f ? l.intensity : 1f);
                    }
                }
            }

            for (int i = 0; i < _attachedLights.Count; i++)
            {
                var l = _attachedLights[i];
                if (l == null) continue;

                l.enabled = true;
                if (i < _originalLightIntensities.Count && _originalLightIntensities[i] > 0f)
                {
                    l.intensity = _originalLightIntensities[i];
                }
            }
        }

        public static void UpdateAllVisualModes(CeilingVisualMode mode, float alpha, bool isVisible)
        {
            for (int i = _allCeilingProps.Count - 1; i >= 0; i--)
            {
                if (_allCeilingProps[i] != null)
                {
                    _allCeilingProps[i].ApplyVisualAlpha(alpha, isVisible);
                }
            }
        }
    }
}