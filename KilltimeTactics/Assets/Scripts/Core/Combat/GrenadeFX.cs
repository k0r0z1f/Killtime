using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;
using Killtime.Core.Inventory;

namespace Killtime.Tactics
{
    /// <summary>
    /// Secousse de caméra procédurale (explosions). S'applique en LateUpdate
    /// (après TacticalCameraController.Update) puis revient à zéro sans dérive.
    /// </summary>
    public class GrenadeCameraShake : MonoBehaviour
    {
        private static GrenadeCameraShake _instance;
        private float _trauma;

        public static void Shake(float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (Camera.main == null) return;
            if (_instance == null)
            {
                var go = new GameObject("[FX] GrenadeCameraShake");
                _instance = go.AddComponent<GrenadeCameraShake>();
            }
            _instance._trauma = Mathf.Max(_instance._trauma, strength);
            if (!_instance.isActiveAndEnabled) _instance.enabled = true;
        }

        private void LateUpdate()
        {
            if (_trauma <= 0.001f)
            {
                _trauma = 0f;
                return;
            }
            var cam = Camera.main;
            if (cam != null)
            {
                float s = _trauma * _trauma * 0.55f;
                cam.transform.position += new Vector3(
                    UnityEngine.Random.Range(-s, s),
                    UnityEngine.Random.Range(-s * 0.6f, s * 0.6f),
                    UnityEngine.Random.Range(-s, s));
            }
            _trauma = Mathf.MoveTowards(_trauma, 0f, Time.deltaTime * 1.6f);
        }
    }

    /// <summary>
    /// Projectile de grenade : arc parabolique main/lanceur -&gt; point d'impact,
    /// rotation du corps, sifflement, rebond sonore, puis callback d'explosion.
    /// 100% procédural, sans asset externe.
    /// </summary>
    public class GrenadeProjectile : MonoBehaviour
    {
        private Vector3 _start;
        private Vector3 _target;
        private float _duration;
        private float _arcHeight;
        private GameObject _body;
        private Action _onImpact;
        private float _elapsed;
        private bool _bounced;

        public static void Launch(Vector3 start, Vector3 target, InventoryItem grenade, bool withLauncher, Action onImpact)
        {
            var go = new GameObject("[FX] GrenadeProjectile");
            var p = go.AddComponent<GrenadeProjectile>();
            p.Setup(start, target, grenade, withLauncher, onImpact);
        }

        private void Setup(Vector3 start, Vector3 target, InventoryItem grenade, bool withLauncher, Action onImpact)
        {
            _start = start;
            _target = target;
            _onImpact = onImpact;
            float dist = Vector3.Distance(start, target);
            // Main : cloche haute et lente. Lanceur : tendu et rapide.
            _duration = withLauncher ? Mathf.Clamp(0.35f + dist * 0.03f, 0.4f, 1.1f)
                                     : Mathf.Clamp(0.55f + dist * 0.07f, 0.6f, 1.4f);
            _arcHeight = withLauncher ? Mathf.Clamp(dist * 0.12f, 0.8f, 2.2f)
                                      : Mathf.Clamp(dist * 0.25f, 1.2f, 4.5f);

            _body = ArmoryPlaceholderFactory.ResolveOrBuild(grenade, transform);
            _body.transform.localPosition = Vector3.zero;
            _body.transform.localScale *= 1.4f;

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayGrenadeThrow(start, withLauncher);
        }

        private IEnumerator Start()
        {
            _elapsed = 0f;
            Vector3 ctrl = (_start + _target) * 0.5f + Vector3.up * _arcHeight * 2f;
            while (_elapsed < _duration)
            {
                _elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_elapsed / _duration);
                // Bézier quadratique.
                Vector3 a = Vector3.Lerp(_start, ctrl, t);
                Vector3 b = Vector3.Lerp(ctrl, _target, t);
                transform.position = Vector3.Lerp(a, b, t);
                if (_body != null)
                    _body.transform.Rotate(Vector3.right, 540f * Time.deltaTime, Space.Self);
                // Rebond sonore à 85% (touche le sol avant la détonation).
                if (!_bounced && t >= 0.85f)
                {
                    _bounced = true;
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Grenade_Bounce, transform.position, 0.7f);
                }
                yield return null;
            }
            transform.position = _target;
            _onImpact?.Invoke();
            if (_body != null) Destroy(_body);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Explosion procédurale : flash lumineux, boule de feu, onde de choc au sol,
    /// colonne de fumée, traceurs de shrapnels, cratère de suie, shake caméra.
    /// Couleurs pilotées par GrenadeKind (frag, plasma, cryo, flash, fumi, gaz, EMP...).
    /// </summary>
    public class GrenadeExplosionFX : MonoBehaviour
    {
        public static void Detonate(Vector3 groundPos, InventoryItem grenade, int blastRadiusTiles, float hexRadius = 1f)
        {
            var go = new GameObject("[FX] GrenadeExplosion");
            go.transform.position = groundPos;
            var fx = go.AddComponent<GrenadeExplosionFX>();
            fx.Setup(groundPos, grenade, blastRadiusTiles, hexRadius);
        }

        private InventoryItem _grenade;
        private float _blastWorldRadius = 3f;
        private Color _fireColor = new Color(1f, 0.5f, 0.1f);
        private Color _smokeColor = new Color(0.25f, 0.25f, 0.28f);
        private bool _isFlash;
        private bool _isSmoke;
        private bool _isHeavy;

        private void Setup(Vector3 pos, InventoryItem grenade, int blastRadiusTiles, float hexRadius)
        {
            _grenade = grenade;
            string kind = grenade?.GrenadeKind ?? "Fragmentation";
            _blastWorldRadius = Mathf.Max(1.5f, blastRadiusTiles * hexRadius * 1.15f);
            ResolvePalette(kind);
            _isHeavy = (grenade != null && (grenade.DamageDiceCount >= 3 || grenade.BaseDamage >= 12))
                || blastRadiusTiles >= 3 || kind.Contains("Thermobar") || kind.Contains("Plasma");

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayGrenadeDetonation(pos, kind, _isHeavy);
            GrenadeCameraShake.Shake(_isFlash ? 0.25f : (_isHeavy ? 0.85f : 0.55f));

            StartCoroutine(ExplosionRoutine(pos));
        }

        private void ResolvePalette(string kind)
        {
            string k = (kind ?? "").ToLowerInvariant();
            if (k.Contains("plasma")) { _fireColor = new Color(0.55f, 0.25f, 1f); _smokeColor = new Color(0.3f, 0.15f, 0.5f); }
            else if (k.Contains("cryo") || k.Contains("gel")) { _fireColor = new Color(0.5f, 0.85f, 1f); _smokeColor = new Color(0.6f, 0.8f, 0.95f); }
            else if (k.Contains("flash") || k.Contains("stun") || k.Contains("sonique")) { _fireColor = Color.white; _smokeColor = new Color(0.9f, 0.9f, 0.9f); _isFlash = true; }
            else if (k.Contains("fumi") || k.Contains("smoke")) { _fireColor = new Color(0.8f, 0.8f, 0.82f); _smokeColor = new Color(0.55f, 0.56f, 0.6f); _isSmoke = true; }
            else if (k.Contains("gaz") || k.Contains("gas")) { _fireColor = new Color(0.6f, 0.9f, 0.25f); _smokeColor = new Color(0.35f, 0.55f, 0.2f); _isSmoke = true; }
            else if (k.Contains("emp") || k.Contains("iem") || k.Contains("ion")) { _fireColor = new Color(0.3f, 0.7f, 1f); _smokeColor = new Color(0.2f, 0.35f, 0.55f); }
            else if (k.Contains("grav")) { _fireColor = new Color(0.7f, 0.3f, 1f); _smokeColor = new Color(0.35f, 0.2f, 0.5f); }
            else if (k.Contains("incend") || k.Contains("thermobar") || k.Contains("thermite")) { _fireColor = new Color(1f, 0.32f, 0.05f); _smokeColor = new Color(0.2f, 0.18f, 0.18f); }
            else if (k.Contains("exercice") || k.Contains("inerte")) { _fireColor = new Color(0.7f, 0.7f, 0.7f); _smokeColor = new Color(0.6f, 0.6f, 0.6f); }
        }

        private IEnumerator ExplosionRoutine(Vector3 pos)
        {
            // --- Flash lumineux ---
            var lightGo = new GameObject("Flash");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.position = pos + Vector3.up * 1f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = _isFlash ? Color.white : _fireColor;
            light.range = 4f + _blastWorldRadius * 2.2f;
            light.intensity = _isFlash ? 10f : 8f;

            // --- Boule de feu ---
            GameObject fireball = null;
            if (!_isSmoke || _isHeavy)
            {
                fireball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fireball.name = "Fireball";
                fireball.transform.SetParent(transform, false);
                fireball.transform.position = pos + Vector3.up * 0.7f;
                var col = fireball.GetComponent<Collider>(); if (col != null) Destroy(col);
                var rend = fireball.GetComponent<Renderer>();
                rend.sharedMaterial = MakeMat(_fireColor * 1.8f, _fireColor);
            }

            // --- Onde de choc au sol ---
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Shockwave";
            ring.transform.SetParent(transform, false);
            ring.transform.position = pos + Vector3.up * 0.08f;
            var ringCol = ring.GetComponent<Collider>(); if (ringCol != null) Destroy(ringCol);
            var ringRend = ring.GetComponent<Renderer>();
            ringRend.sharedMaterial = MakeMat(new Color(_fireColor.r, _fireColor.g, _fireColor.b, 0.55f), _fireColor, true);

            // --- Traceurs de shrapnels ---
            SpawnShrapnelTracers(pos);

            float dur = _isFlash ? 0.45f : (_isSmoke ? 0.8f : 0.65f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                if (fireball != null)
                {
                    float s = Mathf.Lerp(0.4f, _blastWorldRadius * 1.1f, 1f - (1f - k) * (1f - k));
                    fireball.transform.localScale = Vector3.one * s;
                    var m = (fireball.GetComponent<Renderer>()?.sharedMaterial);
                    if (m != null && m.HasProperty("_BaseColor"))
                    {
                        Color c = m.GetColor("_BaseColor");
                        c.a = 1f - k;
                        m.SetColor("_BaseColor", c);
                    }
                }
                float rs = Mathf.Lerp(0.5f, _blastWorldRadius * 2.2f, k);
                ring.transform.localScale = new Vector3(rs, 0.06f, rs);
                if (light != null) light.intensity = Mathf.Lerp(8f, 0f, k * 1.4f);
                yield return null;
            }
            if (fireball != null) Destroy(fireball);
            Destroy(ring);

            // --- Colonne de fumée ---
            StartCoroutine(SmokeRoutine(pos));
            // --- Cratère de suie ---
            if (!_isFlash) StartCoroutine(ScorchRoutine(pos));

            // --- Flash plein écran (flashbang) ---
            if (_isFlash) StartCoroutine(ScreenFlashRoutine());

            // La lumière s'éteint en fondu.
            float lt = 0f;
            while (lt < 0.5f && light != null)
            {
                lt += Time.deltaTime;
                light.intensity = Mathf.Lerp(2f, 0f, lt / 0.5f);
                yield return null;
            }
            Destroy(gameObject, _isSmoke ? 7f : 9f);
        }

        private void SpawnShrapnelTracers(Vector3 pos)
        {
            if (_isSmoke && !_isHeavy) return;
            int count = _isHeavy ? 16 : 10;
            Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            for (int i = 0; i < count; i++)
            {
                var sparkGo = new GameObject($"Shrapnel_{i}");
                sparkGo.transform.SetParent(transform, false);
                var lr = sparkGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.material = new Material(s);
                lr.startWidth = 0.03f;
                lr.endWidth = 0.006f;
                Vector3 dir = (UnityEngine.Random.insideUnitSphere + Vector3.up * 0.7f).normalized;
                float len = UnityEngine.Random.Range(_blastWorldRadius * 0.8f, _blastWorldRadius * 1.8f);
                Vector3 a = pos + Vector3.up * 0.8f;
                lr.SetPosition(0, a);
                lr.SetPosition(1, a + dir * len);
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_fireColor, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                lr.colorGradient = grad;
                StartCoroutine(FadeLineRoutine(lr, 0.5f));
            }
        }

        private IEnumerator FadeLineRoutine(LineRenderer lr, float dur)
        {
            float t = 0f;
            while (t < dur && lr != null)
            {
                t += Time.deltaTime;
                float k = 1f - t / dur;
                lr.startWidth = 0.03f * k;
                yield return null;
            }
            if (lr != null) Destroy(lr.gameObject);
        }

        private IEnumerator SmokeRoutine(Vector3 pos)
        {
            int puffs = _isSmoke ? 10 : 6;
            var puffsList = new List<GameObject>(puffs);
            for (int i = 0; i < puffs; i++)
            {
                var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                puff.name = $"Smoke_{i}";
                puff.transform.SetParent(transform, false);
                puff.transform.position = pos + new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(0.3f, 1.2f),
                    UnityEngine.Random.Range(-1f, 1f));
                puff.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.7f, 1.4f);
                var col = puff.GetComponent<Collider>(); if (col != null) Destroy(col);
                var rend = puff.GetComponent<Renderer>();
                Color c = _smokeColor; c.a = 0.55f;
                rend.sharedMaterial = MakeMat(c, _smokeColor, true);
                puffsList.Add(puff);
            }
            float dur = _isSmoke ? 5.5f : 2.6f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                for (int i = 0; i < puffsList.Count; i++)
                {
                    var p = puffsList[i];
                    if (p == null) continue;
                    p.transform.position += Vector3.up * Time.deltaTime * (0.7f + i * 0.06f);
                    p.transform.localScale += Vector3.one * Time.deltaTime * 0.55f;
                }
                if (k > 0.55f)
                {
                    float fade = 1f - (k - 0.55f) / 0.45f;
                    for (int i = 0; i < puffsList.Count; i++)
                    {
                        var p = puffsList[i];
                        if (p == null) continue;
                        var m = p.GetComponent<Renderer>()?.sharedMaterial;
                        if (m != null && m.HasProperty("_BaseColor"))
                        {
                            Color c = m.GetColor("_BaseColor");
                            c.a = 0.55f * fade;
                            m.SetColor("_BaseColor", c);
                        }
                    }
                }
                yield return null;
            }
            for (int i = 0; i < puffsList.Count; i++)
                if (puffsList[i] != null) Destroy(puffsList[i]);
        }

        private IEnumerator ScorchRoutine(Vector3 pos)
        {
            var scorch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            scorch.name = "Scorch";
            scorch.transform.SetParent(transform, false);
            scorch.transform.position = pos + Vector3.up * 0.02f;
            float r = _blastWorldRadius * 1.15f;
            scorch.transform.localScale = new Vector3(r, 0.02f, r);
            var col = scorch.GetComponent<Collider>(); if (col != null) Destroy(col);
            var rend = scorch.GetComponent<Renderer>();
            rend.sharedMaterial = MakeMat(new Color(0.02f, 0.02f, 0.02f, 0.75f), Color.black, true);
            yield return new WaitForSeconds(7f);
            float t = 0f;
            while (t < 1.5f && scorch != null)
            {
                t += Time.deltaTime;
                var m = scorch.GetComponent<Renderer>()?.sharedMaterial;
                if (m != null && m.HasProperty("_BaseColor"))
                {
                    Color c = m.GetColor("_BaseColor");
                    c.a = 0.75f * (1f - t / 1.5f);
                    m.SetColor("_BaseColor", c);
                }
                yield return null;
            }
            if (scorch != null) Destroy(scorch);
        }

        private IEnumerator ScreenFlashRoutine()
        {
            var overlay = new GameObject("[FX] FlashOverlay");
            var comp = overlay.AddComponent<FlashOverlay>();
            comp.Show();
            yield return new WaitForSeconds(0.7f);
            Destroy(overlay);
        }

        private static Material MakeMat(Color color, Color emissive, bool transparent = false)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Standard")
                          ?? Shader.Find("Unlit/Color")
                          ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", emissive);
                mat.EnableKeyword("_EMISSION");
            }
            if (transparent)
            {
                Color c = color; c.a = Mathf.Clamp01(c.a <= 0.01f ? 0.55f : c.a);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
            }
            return mat;
        }

        private class FlashOverlay : MonoBehaviour
        {
            private float _t;
            public void Show() { _t = 0f; }
            private void Update() { _t += Time.deltaTime; if (_t > 0.6f) Destroy(gameObject); }
            private void OnGUI()
            {
                float a = Mathf.Clamp01(1f - _t / 0.6f) * 0.85f;
                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = prev;
            }
        }
    }
}
