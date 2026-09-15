using System.Collections;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Tactics
{
    public class LaserBeamFX : MonoBehaviour
    {
        private LineRenderer _line;
        private Light _muzzleLight;
        private Light _impactLight;
        private GameObject _sparksRoot;

        public static void Spawn(Vector3 start, Vector3 end, Color laserColor, Color coreColor, float width, float duration, bool isHit)
        {
            var go = new GameObject("[FX] LaserBeam");
            var fx = go.AddComponent<LaserBeamFX>();
            fx.Initialize(start, end, laserColor, coreColor, width, duration, isHit);
        }

        private void Initialize(Vector3 start, Vector3 end, Color laserColor, Color coreColor, float width, float duration, bool isHit)
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.SetPosition(0, start);
            _line.SetPosition(1, end);
            _line.startWidth = width;
            _line.endWidth = width * 0.75f;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

            Material mat = new Material(shader);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", coreColor);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", coreColor);
            _line.material = mat;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(coreColor, 0f), new GradientColorKey(laserColor, 0.4f), new GradientColorKey(laserColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.95f, 0.8f), new GradientAlphaKey(0f, 1f) }
            );
            _line.colorGradient = gradient;

            var lightMuzzleGo = new GameObject("MuzzleLight");
            lightMuzzleGo.transform.SetParent(transform, false);
            lightMuzzleGo.transform.position = start;
            _muzzleLight = lightMuzzleGo.AddComponent<Light>();
            _muzzleLight.type = LightType.Point;
            _muzzleLight.range = 2.5f;
            _muzzleLight.intensity = 4.0f;
            _muzzleLight.color = laserColor;

            var lightImpactGo = new GameObject("ImpactLight");
            lightImpactGo.transform.SetParent(transform, false);
            lightImpactGo.transform.position = end;
            _impactLight = lightImpactGo.AddComponent<Light>();
            _impactLight.type = LightType.Point;
            _impactLight.range = isHit ? 3.0f : 2.0f;
            _impactLight.intensity = isHit ? 5.0f : 3.0f;
            _impactLight.color = isHit ? laserColor : new Color(1.0f, 0.55f, 0.2f);

            SpawnImpactSparks(end, isHit, laserColor);

            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.PlayAt(SoundId.Weapon_Laser_Fire, start, 0.9f);
                KilltimeAudioManager.Instance.PlayAt(SoundId.Weapon_Laser_Impact, end, isHit ? 0.95f : 0.75f);
            }

            StartCoroutine(FadeRoutine(duration));
        }

        private void SpawnImpactSparks(Vector3 point, bool isHit, Color color)
        {
            _sparksRoot = new GameObject("Sparks");
            _sparksRoot.transform.SetParent(transform, false);
            _sparksRoot.transform.position = point;

            int count = isHit ? 6 : 8;
            Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            Color sparkColor = isHit ? color : new Color(1.0f, 0.7f, 0.2f);

            for (int i = 0; i < count; i++)
            {
                var sparkGo = new GameObject($"Spark_{i}");
                sparkGo.transform.SetParent(_sparksRoot.transform, false);

                var lr = sparkGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.material = new Material(s);
                lr.startWidth = 0.025f;
                lr.endWidth = 0.005f;

                Vector3 dir = isHit
                    ? (Random.insideUnitSphere + Vector3.up * 0.5f).normalized
                    : (Random.insideUnitSphere + Vector3.up * 1.2f).normalized;

                float len = Random.Range(0.2f, 0.6f);
                lr.SetPosition(0, point);
                lr.SetPosition(1, point + dir * len);

                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(sparkColor, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
                );
                lr.colorGradient = grad;
            }
        }

        private IEnumerator FadeRoutine(float duration)
        {
            float elapsed = 0f;
            float startWidth = _line.startWidth;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                _line.startWidth = Mathf.Lerp(startWidth, 0f, t);
                _line.endWidth = Mathf.Lerp(startWidth * 0.75f, 0f, t);

                if (_muzzleLight != null) _muzzleLight.intensity = Mathf.Lerp(4f, 0f, t * 2f);
                if (_impactLight != null) _impactLight.intensity = Mathf.Lerp(5f, 0f, t);

                yield return null;
            }

            Destroy(gameObject);
        }
    }
}