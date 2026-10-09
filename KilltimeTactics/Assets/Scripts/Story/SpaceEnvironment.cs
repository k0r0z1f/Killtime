using UnityEngine;

namespace Killtime.Story.Scenes
{
    /// <summary>
    /// Ciel étoilé spatial procédural (skybox suiveuse).
    /// - Sphère inversée centrée sur la caméra chaque frame => parallaxe nulle (infini).
    /// - Rayon = 90% du far clip caméra (reste visible à 1M, suit le near adaptatif).
    /// - Shader sans texture, sans scintillement (physique : pas d'atmosphère).
    /// - _StarExposure simule l'adaptation œil/caméra : 1 = nuit profonde,
    ///   ~0.25 = planète/soleil brillant dans le champ (seules les brillantes restent).
    /// - _WarpAmount (effet supraluminique) : 1 = singularité (écran noir
    ///   + 1px central, temps dilaté), 0 = voûte normale. Piloté par les plans
    ///   cinématiques cochés « warp » (palier noir puis point qui grossit →
    ///   stries de ralentissement → voûte reformée).
    /// Usage JSON : placeholder Id "Starfield" (Scale.x=densité, Scale.y=exposition, Scale.z=taille).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Killtime/Space/Space Environment (Starfield)")]
    public class SpaceEnvironment : MonoBehaviour
    {
        private const string ShaderName = "Killtime/Space/SpaceStarfield";
        // NOTE : ne doit PAS commencer par "[Environment]" — CleanStrayEnvironments
        // détruit tout enfant "[Environment]*" non courant lors du Build.
        private const string RootName = "[Space_Skybox]";

        [Header("Ciel")]
        [Range(0f, 2f)] public float starDensity = 1f;
        [Range(0f, 1f)] public float starExposure = 1f;
        [Range(0.5f, 3f)] public float starSize = 1f;
        [Range(0f, 1f)] public float milkyWay = 0.35f;
        public Color backgroundColor = new Color(0.005f, 0.008f, 0.02f, 1f);

        [Header("Effet supraluminique (warp)")]
        [Tooltip("0 = voûte normale, 1 = singularité (noir + 1px, temps dilaté). Piloté par les cinématiques (case « warp ») : palier noir puis point qui grossit → stries → voûte normale.")]
        [Range(0f, 1f)] public float warpAmount = 0f;

        [Header("Suivi caméra")]
        [Tooltip("Si coché, le fond caméra est forcé au noir spatial.")]
        public bool forceBlackBackground = true;

        private Material _mat;
        private MeshRenderer _renderer;
        private static readonly int P_Density = Shader.PropertyToID("_StarDensity");
        private static readonly int P_Exposure = Shader.PropertyToID("_StarExposure");
        private static readonly int P_Size = Shader.PropertyToID("_StarSize");
        private static readonly int P_Milky = Shader.PropertyToID("_MilkyWayIntensity");
        private static readonly int P_Bg = Shader.PropertyToID("_BackgroundColor");
        private static readonly int P_Warp = Shader.PropertyToID("_WarpAmount");
        private static readonly int P_WarpCenter = Shader.PropertyToID("_WarpCenter");

        public static SpaceEnvironment Ensure(Transform parent, float density = 1f, float exposure = 1f, float size = 1f, float initialWarp = 0f)
        {
            float initW = Mathf.Clamp01(initialWarp);
            SpaceEnvironment existing = FindAnyObjectByType<SpaceEnvironment>();
            if (existing != null)
            {
                existing.starDensity = density;
                existing.starExposure = exposure;
                existing.starSize = size;
                existing.warpAmount = initW;
                existing.ApplyProperties();
                return existing;
            }

            GameObject root = new GameObject(RootName);
            if (parent != null) root.transform.SetParent(parent, false);
            var env = root.AddComponent<SpaceEnvironment>();
            env.starDensity = density;
            env.starExposure = exposure;
            env.starSize = size;
            env.warpAmount = initW;
            env.BuildSphere();
            env.ApplyProperties();
            return env;
        }

        public static void Clear()
        {
            var all = FindObjectsByType<SpaceEnvironment>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject != null)
                    Destroy(all[i].gameObject);
            }
        }

        public static void SetExposureForAll(float exposure)
        {
            var all = FindObjectsByType<SpaceEnvironment>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                all[i].starExposure = Mathf.Clamp01(exposure);
                all[i].ApplyProperties();
            }
        }

        /// <summary>
        /// Effet supraluminique global (0 = voûte normale, 1 = point central).
        /// Appelé chaque frame par CinematicDirector pendant les plans cochés
        /// « warp », remis à 0 en fin/abort/stop (la voûte se reforme normale).
        /// </summary>
        public static void SetWarpForAll(float warp)
        {
            float w = Mathf.Clamp01(warp);
            var all = FindObjectsByType<SpaceEnvironment>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (Mathf.Approximately(all[i].warpAmount, w)) continue;
                all[i].warpAmount = w;
                all[i].ApplyProperties();
            }
        }

        public static float GetWarpForAll()
        {
            var all = FindObjectsByType<SpaceEnvironment>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null) return Mathf.Clamp01(all[i].warpAmount);
            }
            return 0f;
        }

        private void Awake()
        {
            if (_renderer == null) BuildSphere();
            ApplyProperties();
        }

        private void OnValidate()
        {
            ApplyProperties();
        }

        private void BuildSphere()
        {
            // Idempotent : Awake() construit déjà la sphère avant qu'Ensure() la redemande.
            if (_renderer != null) { ApplyProperties(); return; }
            // Sphère unité procédurale (pas de collider, pas d'asset requis).
            GameObject sphere = new GameObject("StarfieldSphere");
            sphere.transform.SetParent(transform, false);
            sphere.transform.localPosition = Vector3.zero;

            var filter = sphere.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildUnitSphere(32, 20);

            _renderer = sphere.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.allowOcclusionWhenDynamic = false;

            Shader sh = null;
            // 1) Asset Resources (inclus dans les builds : le shader ne peut pas être strippé).
            Material baseMat = Resources.Load<Material>("Materials/Space_Starfield");
            if (baseMat != null)
            {
                _mat = Object.Instantiate(baseMat);
            }
            else
            {
                // 2) Repli éditeur : recherche par nom (ne marche que si le shader est importé).
                sh = Shader.Find(ShaderName);
                if (sh != null) _mat = new Material(sh);
            }
            if (_mat == null)
            {
                // 3) Dernier repli SANS throw : sphère invisible plutôt que crash
                // (un new Material(null) lançait ArgumentNullException dans le build).
                Debug.LogError("[SpaceEnvironment] Ni 'Materials/Space_Starfield' ni le shader '" + ShaderName + "' trouvés : ciel désactivé.");
                Object.Destroy(sphere);
                _renderer = null;
                return;
            }
            _renderer.sharedMaterial = _mat;
            if (_mat != null && warpAmount > 0.001f && _mat.HasProperty(P_Warp))
            {
                _mat.SetFloat(P_Warp, warpAmount);
            }
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) cam = FindAnyObjectByType<Camera>();
            if (cam == null || _renderer == null) return;

            // Suit la caméra (origine = infini, pas de parallaxe).
            transform.position = cam.transform.position;

            // Rayon sous le far clip (jamais au-dessus : sinon la sphère est clippée).
            float far = Mathf.Max(1000f, cam.farClipPlane);
            float radius = Mathf.Min(far * 0.9f, 900000f);
            radius = Mathf.Max(radius, 50f);
            transform.localScale = Vector3.one * radius;

            // Centre du warp = forward caméra (point central de l'écran).
            // Mis à jour chaque frame : le point reste centré même si la
            // caméra voyage pendant le plan (ex : scène 01, 1000km → base).
            if (_mat != null && warpAmount > 0.001f && _mat.HasProperty(P_WarpCenter))
            {
                try
                {
                    Vector3 fwd = cam.transform.forward;
                    if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
                    _mat.SetVector(P_WarpCenter, new Vector4(fwd.x, fwd.y, fwd.z, 0f));
                }
                catch { /* ignore */ }
            }

            if (forceBlackBackground)
            {
                // URP : SolidColor + noir spatial (la sphère fait le reste).
                if (cam.clearFlags != CameraClearFlags.SolidColor)
                    cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.005f, 0.008f, 0.02f, 1f);
            }
        }

        public void ApplyProperties()
        {
            if (_mat == null) return;
            if (_mat.HasProperty(P_Density)) _mat.SetFloat(P_Density, starDensity);
            if (_mat.HasProperty(P_Exposure)) _mat.SetFloat(P_Exposure, Mathf.Clamp01(starExposure));
            if (_mat.HasProperty(P_Size)) _mat.SetFloat(P_Size, starSize);
            if (_mat.HasProperty(P_Milky)) _mat.SetFloat(P_Milky, milkyWay);
            if (_mat.HasProperty(P_Bg)) _mat.SetColor(P_Bg, backgroundColor);
            if (_mat.HasProperty(P_Warp)) _mat.SetFloat(P_Warp, Mathf.Clamp01(warpAmount));
            // Centre initial : forward actuel si dispo, sinon +Z (le LateUpdate
            // le recale chaque frame dès que warp > 0).
            if (_mat.HasProperty(P_WarpCenter))
            {
                try
                {
                    Camera cam = Camera.main;
                    if (cam == null) cam = FindAnyObjectByType<Camera>();
                    Vector3 fwd = (cam != null && cam.transform.forward.sqrMagnitude > 1e-8f)
                        ? cam.transform.forward.normalized : Vector3.forward;
                    _mat.SetVector(P_WarpCenter, new Vector4(fwd.x, fwd.y, fwd.z, 0f));
                }
                catch { /* ignore */ }
            }
        }

        private static Mesh BuildUnitSphere(int lonSegs, int latSegs)
        {
            Mesh m = new Mesh { name = "SpaceStarfieldSphere" };
            var verts = new System.Collections.Generic.List<Vector3>((lonSegs + 1) * (latSegs + 1));
            var tris = new System.Collections.Generic.List<int>(lonSegs * latSegs * 6);
            for (int lat = 0; lat <= latSegs; lat++)
            {
                float theta = Mathf.PI * lat / latSegs;
                float sinT = Mathf.Sin(theta), cosT = Mathf.Cos(theta);
                for (int lon = 0; lon <= lonSegs; lon++)
                {
                    float phi = 2f * Mathf.PI * lon / lonSegs;
                    verts.Add(new Vector3(Mathf.Sin(phi) * sinT, cosT, Mathf.Cos(phi) * sinT));
                }
            }
            for (int lat = 0; lat < latSegs; lat++)
            {
                for (int lon = 0; lon < lonSegs; lon++)
                {
                    int a = lat * (lonSegs + 1) + lon;
                    int b = a + lonSegs + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            }
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
