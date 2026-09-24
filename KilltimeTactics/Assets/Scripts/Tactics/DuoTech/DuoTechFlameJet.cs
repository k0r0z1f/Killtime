using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Tactics.DuoTech
{
    /// <summary>
    /// Duo-Tech — jet de feu placeholder de Lucas (flamethrower).
    /// Suit la ligne T2 dessinée en exécution : la tête avance depuis Lucas,
    /// la traînée se révèle progressivement, braises + lumière au passage,
    /// détonation à l'arrivée. S'auto-détruit (aucun asset requis).
    /// </summary>
    public class DuoTechFlameJet : MonoBehaviour
    {
        private LineRenderer _line;
        private GameObject _head;
        private Light _headLight;
        private readonly List<GameObject> _embers = new();

        private List<Vector3> _path = new();
        private float _totalLen;
        private float _duration = 0.55f;

        public static void Spawn(List<Vector3> path, float duration = 0.55f)
        {
            if (path == null || path.Count < 2 || duration <= 0f) return;
            var go = new GameObject("[FX] DuoFlameJet");
            var fx = go.AddComponent<DuoTechFlameJet>();
            fx.Initialize(new List<Vector3>(path), duration);
        }

        private void Initialize(List<Vector3> path, float duration)
        {
            _path = path;
            _duration = duration;
            _totalLen = 0f;
            for (int i = 1; i < _path.Count; i++)
                _totalLen += Vector3.Distance(_path[i - 1], _path[i]);

            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 0;
            _line.startWidth = 0.38f;
            _line.endWidth = 0.14f;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Sprites/Default")
                      ?? Shader.Find("Standard");
            if (shader != null)
            {
                var mat = new Material(shader);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                else if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                mat.renderQueue = 4000; // Par-dessus tuiles et unités, comme les traits du tissage.
                _line.material = mat;
            }

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f),
                    new GradientColorKey(new Color(1f, 0.6f, 0.15f), 0.35f),
                    new GradientColorKey(new Color(0.9f, 0.25f, 0.05f), 0.8f),
                    new GradientColorKey(new Color(0.5f, 0.08f, 0.02f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.6f),
                    new GradientAlphaKey(0.7f, 0.85f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            _line.colorGradient = gradient;

            _head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _head.transform.localScale = Vector3.one * 0.45f;
            var col = _head.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var headRend = _head.GetComponent<Renderer>();
            if (headRend != null && shader != null)
            {
                var hm = new Material(shader);
                if (hm.HasProperty("_BaseColor")) hm.SetColor("_BaseColor", new Color(1f, 0.9f, 0.6f));
                else if (hm.HasProperty("_Color")) hm.SetColor("_Color", new Color(1f, 0.9f, 0.6f));
                hm.renderQueue = 4000;
                headRend.material = hm;
            }

            _headLight = _head.AddComponent<Light>();
            _headLight.type = LightType.Point;
            _headLight.range = 3.5f;
            _headLight.intensity = 5f;
            _headLight.color = new Color(1f, 0.55f, 0.15f);

            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            // Temps non-scalé : le jet reste synchro même sous le ralenti
            // dramatique (timeScale 0.45) du plan cinématique.
            float elapsed = 0f;
            float emberTimer = 0f;

            while (elapsed < _duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float headArc = _totalLen * Mathf.Clamp01(elapsed / _duration);
                RevealUpTo(headArc);

                Vector3 headPos = PointAlong(_path, headArc);
                _head.transform.position = headPos;
                _headLight.intensity = 4f + Mathf.Sin(elapsed * 40f) * 1.5f;

                emberTimer += Time.unscaledDeltaTime;
                if (emberTimer >= 0.06f)
                {
                    emberTimer = 0f;
                    SpawnEmber(headPos);
                }
                yield return null;
            }

            RevealUpTo(_totalLen);
            if (KilltimeAudioManager.Instance != null && _path.Count > 0)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Grenade_Explosion_Frag, _path[_path.Count - 1], 0.7f);

            // Fondu rapide puis nettoyage.
            float fade = 0f;
            float w0 = _line.startWidth;
            float w1 = _line.endWidth;
            while (fade < 0.25f)
            {
                fade += Time.unscaledDeltaTime;
                float t = fade / 0.25f;
                _line.startWidth = Mathf.Lerp(w0, 0f, t);
                _line.endWidth = Mathf.Lerp(w1, 0f, t);
                if (_headLight != null) _headLight.intensity = Mathf.Lerp(5f, 0f, t);
                yield return null;
            }

            Destroy(gameObject);
        }

        private void RevealUpTo(float arc)
        {
            var pts = new List<Vector3> { _path[0] };
            float acc = 0f;
            for (int i = 1; i < _path.Count && pts.Count < 64; i++)
            {
                float seg = Vector3.Distance(_path[i - 1], _path[i]);
                if (acc + seg <= arc)
                {
                    pts.Add(_path[i]);
                    acc += seg;
                }
                else
                {
                    float t = seg > 1e-6f ? (arc - acc) / seg : 0f;
                    pts.Add(Vector3.Lerp(_path[i - 1], _path[i], t));
                    break;
                }
            }
            _line.positionCount = pts.Count;
            _line.SetPositions(pts.ToArray());
        }

        private static Vector3 PointAlong(List<Vector3> pts, float s)
        {
            if (pts.Count == 0) return Vector3.zero;
            if (s <= 0f) return pts[0];
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                if (acc + seg >= s)
                {
                    float t = seg > 1e-6f ? (s - acc) / seg : 0f;
                    return Vector3.Lerp(pts[i - 1], pts[i], t);
                }
                acc += seg;
            }
            return pts[pts.Count - 1];
        }

        private void SpawnEmber(Vector3 at)
        {
            var ember = new GameObject("Ember");
            ember.transform.SetParent(transform);
            var lr = ember.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) lr.material = new Material(shader);
            lr.startWidth = 0.05f;
            lr.endWidth = 0.005f;

            Vector3 dir = (Random.insideUnitSphere + Vector3.up * 1.2f).normalized;
            float len = Random.Range(0.25f, 0.7f);
            lr.SetPosition(0, at);
            lr.SetPosition(1, at + dir * len);

            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.5f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            lr.colorGradient = grad;

            _embers.Add(ember);
            StartCoroutine(FadeEmber(ember));
        }

        private IEnumerator FadeEmber(GameObject ember)
        {
            yield return new WaitForSeconds(0.3f);
            _embers.Remove(ember);
            if (ember != null) Destroy(ember);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _embers.Count; i++)
                if (_embers[i] != null) Destroy(_embers[i]);
            _embers.Clear();
        }
    }
}
