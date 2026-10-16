using UnityEngine;

namespace Killtime.Story.Scenes
{
    /// <summary>
    /// RD-022 : voyant lumineux intermittent.
    /// L'ancien placeholder JunctionBox_Light_West n'existe plus ; l'équivalent
    /// actuel est Sas_Voyant_Rouge (sphère émissive + Point rouge, ParentToEnvironment).
    /// Fait pulser la Light et l'émission du Renderer en onde carrée.
    /// Branché par convention dans SceneEnvironmentLibrary (Id contenant "Voyant").
    /// </summary>
    public class SasVoyantBlink : MonoBehaviour
    {
        [Tooltip("Fréquence de clignotement en Hz. <= 0 = allumé fixe.")]
        public float blinkFrequency = 1.2f;

        [Tooltip("Facteur d'atténuation pendant la phase éteinte (0 = noir, 1 = fixe).")]
        [Range(0f, 1f)]
        public float offDimFactor = 0.12f;

        private Light _light;
        private Material _matInstance;
        private float _baseIntensity;
        private Color _baseEmission = Color.black;
        private bool _hasEmission;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            _light = GetComponent<Light>();
            if (_light != null) _baseIntensity = _light.intensity;

            var renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                // Instance propre au voyant (pas de fuite vers les matériaux partagés).
                _matInstance = renderer.material;
                if (_matInstance != null && _matInstance.HasProperty(EmissionColorId))
                {
                    _hasEmission = true;
                    _baseEmission = _matInstance.GetColor(EmissionColorId);
                }
            }

            if (_light == null && !_hasEmission) enabled = false;
        }

        private void Update()
        {
            bool on = blinkFrequency <= 0f || (Time.time * blinkFrequency) % 1f < 0.5f;
            float k = on ? 1f : Mathf.Clamp01(offDimFactor);
            if (_light != null) _light.intensity = _baseIntensity * k;
            if (_hasEmission) _matInstance.SetColor(EmissionColorId, _baseEmission * k);
        }
    }
}
