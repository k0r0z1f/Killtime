using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Killtime.Tactics.Lighting
{
    [DisallowMultipleComponent]
    public class TacticalPixieLight : MonoBehaviour
    {
        [Header("Émission Lumineuse & Spectre")]
        [SerializeField] [Range(0.05f, 2.0f)] private float _colorCycleSpeed = 0.28f;
        [SerializeField] [Range(0.6f, 1.0f)] private float _colorSaturation = 0.96f;
        [SerializeField] [Range(0.8f, 1.0f)] private float _colorValue = 1.0f;
        [SerializeField] [Range(6.0f, 30.0f)] private float _lightRange = 16.0f;
        [SerializeField] [Range(4.0f, 60.0f)] private float _lightIntensity = 24.0f;

        [Header("Navigation Aléatoire Carte Complète")]
        [SerializeField] private float _baseFlightSpeed = 4.6f;
        [SerializeField] private float _steeringAgility = 2.8f;
        [SerializeField] private float _hoverHeight = 1.80f;
        [SerializeField] private float _arrivalDistanceThreshold = 1.1f;
        [SerializeField] private float _maxWaypointStuckTime = 8.0f;

        [Header("Oscillations & Turbulences")]
        [SerializeField] private float _waveFrequency = 2.6f;
        [SerializeField] private float _waveAmplitude = 0.38f;
        [SerializeField] private float _flutterNoiseScale = 0.45f;

        [Header("Poussières Féeriques")]
        [SerializeField] [Range(20, 60)] private int _maxDustMotes = 40;
        [SerializeField] private float _moteLifetime = 1.20f;
        [SerializeField] private float _moteSpawnInterval = 0.035f;

        private Light _pointLight;
        private MeshRenderer _coreRenderer;

        private TacticalHexGrid _grid;
        private Vector3 _currentWaypoint;
        private Vector3 _currentVelocity = Vector3.zero;
        private float _activeSpeed;
        private float _activeHoverHeight;

        private float _waypointTimer = 0f;
        private float _moteSpawnTimer = 0f;
        private float _seedOffset;

        private Material _litEmissiveMat;
        private Material _moteEmissiveMat;
        private MaterialPropertyBlock _motePropBlock;

        private Mesh _sphereMesh;
        private Mesh _diamondMesh;

        private struct DustMote
        {
            public GameObject Object;
            public Transform Transform;
            public MeshRenderer Renderer;
            public Vector3 Velocity;
            public float LifeTimer;
            public float InitialScale;
            public bool IsActive;
        }

        private DustMote[] _dustPool;
        private GameObject _dustPoolRoot;
        private int _poolIndex = 0;

        public Color CurrentColor { get; private set; }

        private void Awake()
        {
            _seedOffset = UnityEngine.Random.Range(0f, 10000f);
            _activeSpeed = _baseFlightSpeed * UnityEngine.Random.Range(0.85f, 1.25f);
            _activeHoverHeight = _hoverHeight * UnityEngine.Random.Range(0.90f, 1.20f);
            _colorCycleSpeed = _colorCycleSpeed * UnityEngine.Random.Range(0.85f, 1.15f);

            _motePropBlock = new MaterialPropertyBlock();

            EnsurePhysicalInertia();
            InitializeMeshes();
            InitializeMaterials();
            InitializeComponents();
            InitializeProceduralDustPool();
            FindGridReference();

            _currentWaypoint = PickRandomMapDestination();
            _currentVelocity = (transform.position - _currentWaypoint).normalized * _activeSpeed;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateColorSpectrum();
            UpdateRandomMapExploration(dt);
            UpdateProceduralDust(dt);
        }

        private void OnDestroy()
        {
            if (_litEmissiveMat != null) Destroy(_litEmissiveMat);
            if (_moteEmissiveMat != null) Destroy(_moteEmissiveMat);
            if (_sphereMesh != null) Destroy(_sphereMesh);
            if (_diamondMesh != null) Destroy(_diamondMesh);

            if (_dustPoolRoot != null)
            {
                Destroy(_dustPoolRoot);
            }
        }

        private void EnsurePhysicalInertia()
        {
            gameObject.layer = 2; // Ignore Raycast

            var colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Destroy(colliders[i]);
            }
        }

        private void FindGridReference()
        {
            if (_grid == null)
            {
                _grid = FindAnyObjectByType<TacticalHexGrid>();
            }
        }

        private void InitializeMeshes()
        {
            _sphereMesh = GenerateSphereMesh(0.24f, 16, 16);
            _diamondMesh = CreateDiamondMesh(0.12f);
        }

        private void InitializeMaterials()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Killtime/TacticalLit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Sprites/Default");

            _litEmissiveMat = new Material(litShader)
            {
                name = "PixieLitEmissive_Instance"
            };

            _litEmissiveMat.EnableKeyword("_EMISSION");
            _litEmissiveMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            Shader unlitShader = Shader.Find("Sprites/Default")
                              ?? Shader.Find("Universal Render Pipeline/Unlit")
                              ?? litShader;

            _moteEmissiveMat = new Material(unlitShader)
            {
                name = "PixieMote_Instance"
            };

            if (_moteEmissiveMat.HasProperty("_Surface")) _moteEmissiveMat.SetFloat("_Surface", 1.0f);
            _moteEmissiveMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _moteEmissiveMat.SetInt("_DstBlend", (int)BlendMode.One);
            _moteEmissiveMat.SetInt("_ZWrite", 0);
            _moteEmissiveMat.renderQueue = (int)RenderQueue.Transparent + 50;
        }

        private void InitializeComponents()
        {
            var coreObj = new GameObject("Pixie_Core");
            coreObj.transform.SetParent(transform, false);
            coreObj.layer = 2;

            var mfCore = coreObj.AddComponent<MeshFilter>();
            mfCore.sharedMesh = _sphereMesh;

            _coreRenderer = coreObj.AddComponent<MeshRenderer>();
            _coreRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _coreRenderer.receiveShadows = false;
            _coreRenderer.sharedMaterial = _litEmissiveMat;

            _pointLight = GetComponent<Light>();
            if (_pointLight == null)
            {
                _pointLight = gameObject.AddComponent<Light>();
            }

            _pointLight.type = LightType.Point;
            _pointLight.range = _lightRange;
            _pointLight.intensity = _lightIntensity;
            _pointLight.shadows = LightShadows.None;
            _pointLight.cullingMask = ~0;
        }

        private void InitializeProceduralDustPool()
        {
            _dustPool = new DustMote[_maxDustMotes];

            _dustPoolRoot = new GameObject("[Pixie_DustPool]");
            _dustPoolRoot.transform.SetParent(null);
            _dustPoolRoot.layer = 2;

            for (int i = 0; i < _maxDustMotes; i++)
            {
                var moteObj = new GameObject($"DustMote_{i:00}");
                moteObj.transform.SetParent(_dustPoolRoot.transform);
                moteObj.layer = 2;

                var mf = moteObj.AddComponent<MeshFilter>();
                mf.sharedMesh = _diamondMesh;

                var mr = moteObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _moteEmissiveMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;

                moteObj.SetActive(false);

                _dustPool[i] = new DustMote
                {
                    Object = moteObj,
                    Transform = moteObj.transform,
                    Renderer = mr,
                    Velocity = Vector3.zero,
                    LifeTimer = 0f,
                    InitialScale = UnityEngine.Random.Range(0.65f, 1.25f),
                    IsActive = false
                };
            }
        }

        private static Mesh GenerateSphereMesh(float radius, int latSegments, int lonSegments)
        {
            var mesh = new Mesh { name = "PixieSphereMesh" };
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int lat = 0; lat <= latSegments; lat++)
            {
                float theta = lat * Mathf.PI / latSegments;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= lonSegments; lon++)
                {
                    float phi = lon * 2 * Mathf.PI / lonSegments;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);

                    Vector3 normal = new Vector3(cosPhi * sinTheta, cosTheta, sinPhi * sinTheta);
                    normals.Add(normal);
                    vertices.Add(normal * radius);
                    uvs.Add(new Vector2((float)lon / lonSegments, 1f - ((float)lat / latSegments)));
                }
            }

            int stride = lonSegments + 1;
            for (int lat = 0; lat < latSegments; lat++)
            {
                for (int lon = 0; lon < lonSegments; lon++)
                {
                    int current = lat * stride + lon;
                    int next = current + stride;

                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(current + 1);

                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateDiamondMesh(float scale)
        {
            Mesh mesh = new Mesh { name = "PixieDiamondMesh" };

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(0f, 0.5f, 0f) * scale,
                new Vector3(-0.35f, 0f, 0f) * scale,
                new Vector3(0f, 0f, 0.35f) * scale,
                new Vector3(0.35f, 0f, 0f) * scale,
                new Vector3(0f, 0f, -0.35f) * scale,
                new Vector3(0f, -0.5f, 0f) * scale
            };

            int[] triangles = new int[]
            {
                0, 1, 2,  0, 2, 3,  0, 3, 4,  0, 4, 1,
                5, 2, 1,  5, 3, 2,  5, 4, 3,  5, 1, 4
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void UpdateColorSpectrum()
        {
            float hue = Mathf.Repeat(Time.unscaledTime * _colorCycleSpeed + (_seedOffset * 0.001f), 1.0f);
            CurrentColor = Color.HSVToRGB(hue, _colorSaturation, _colorValue);

            if (_pointLight != null)
            {
                _pointLight.color = CurrentColor;
                _pointLight.range = _lightRange;
                _pointLight.intensity = _lightIntensity;
            }

            Color hdrCore = CurrentColor * 4.2f;

            if (_litEmissiveMat != null)
            {
                if (_litEmissiveMat.HasProperty("_BaseColor")) _litEmissiveMat.SetColor("_BaseColor", CurrentColor);
                if (_litEmissiveMat.HasProperty("_Color")) _litEmissiveMat.SetColor("_Color", CurrentColor);
                if (_litEmissiveMat.HasProperty("_EmissionColor")) _litEmissiveMat.SetColor("_EmissionColor", hdrCore);
            }
        }

        private void UpdateRandomMapExploration(float dt)
        {
            _waypointTimer += dt;

            float horizontalDist = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(_currentWaypoint.x, _currentWaypoint.z)
            );

            if (horizontalDist <= _arrivalDistanceThreshold || _waypointTimer >= _maxWaypointStuckTime)
            {
                _currentWaypoint = PickRandomMapDestination();
                _waypointTimer = 0f;
            }

            Vector3 toTarget = _currentWaypoint - transform.position;
            Vector3 desiredDirection = new Vector3(toTarget.x, 0f, toTarget.z).normalized;

            float timeVal = Time.unscaledTime * 1.8f + _seedOffset;
            float noiseX = (Mathf.PerlinNoise(timeVal, 0.15f) - 0.5f) * 2f;
            float noiseZ = (Mathf.PerlinNoise(0.85f, timeVal) - 0.5f) * 2f;
            Vector3 flutter = new Vector3(noiseX, 0f, noiseZ) * _flutterNoiseScale;

            Vector3 targetHorizontalVelocity = (desiredDirection + flutter).normalized * _activeSpeed;
            _currentVelocity = Vector3.Lerp(_currentVelocity, targetHorizontalVelocity, dt * _steeringAgility);

            Vector3 newPos = transform.position + _currentVelocity * dt;

            float groundElevation = 0f;
            if (_grid != null && _grid.TryGetNodeAtWorldPosition(newPos, out var node))
            {
                groundElevation = node.Elevation;
            }

            float wave = Mathf.Sin((Time.unscaledTime + _seedOffset) * _waveFrequency) * _waveAmplitude;
            newPos.y = Mathf.Lerp(transform.position.y, groundElevation + _activeHoverHeight + wave, dt * 4.5f);

            transform.position = newPos;

            Vector3 flatDir = new Vector3(_currentVelocity.x, 0f, _currentVelocity.z);
            if (flatDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flatDir), dt * 6.5f);
            }
        }

        private Vector3 PickRandomMapDestination()
        {
            if (_grid != null && _grid.Nodes != null && _grid.Nodes.Count > 0)
            {
                int maxRadius = _grid.GridRadius;

                for (int attempt = 0; attempt < 12; attempt++)
                {
                    int q = UnityEngine.Random.Range(-maxRadius, maxRadius + 1);
                    int rMin = Mathf.Max(-maxRadius, -q - maxRadius);
                    int rMax = Mathf.Min(maxRadius, -q + maxRadius);
                    int r = UnityEngine.Random.Range(rMin, rMax + 1);

                    var coords = new HexCoordinates(q, r);
                    var node = _grid.GetNode(coords);

                    if (node != null)
                    {
                        Vector3 candPos = node.WorldPosition;
                        float dist = Vector2.Distance(
                            new Vector2(transform.position.x, transform.position.z),
                            new Vector2(candPos.x, candPos.z)
                        );

                        if (dist >= 4.0f || attempt >= 10)
                        {
                            return new Vector3(candPos.x, candPos.y + _activeHoverHeight, candPos.z);
                        }
                    }
                }
            }

            float fallbackRadius = 9.0f;
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float distR = UnityEngine.Random.Range(3.0f, fallbackRadius);

            return new Vector3(Mathf.Cos(angle) * distR, _activeHoverHeight, Mathf.Sin(angle) * distR);
        }

        private void UpdateProceduralDust(float dt)
        {
            _moteSpawnTimer += dt;
            if (_moteSpawnTimer >= _moteSpawnInterval)
            {
                _moteSpawnTimer = 0f;
                SpawnDustMote();
            }

            for (int i = 0; i < _dustPool.Length; i++)
            {
                if (!_dustPool[i].IsActive) continue;

                _dustPool[i].LifeTimer -= dt;
                if (_dustPool[i].LifeTimer <= 0f)
                {
                    _dustPool[i].IsActive = false;
                    _dustPool[i].Object.SetActive(false);
                    continue;
                }

                float t = _dustPool[i].LifeTimer / _moteLifetime;
                _dustPool[i].Transform.position += _dustPool[i].Velocity * dt;
                _dustPool[i].Transform.localScale = Vector3.one * (_dustPool[i].InitialScale * t);
                _dustPool[i].Transform.Rotate(Vector3.up, 180f * dt);

                Color dustCol = new Color(CurrentColor.r, CurrentColor.g, CurrentColor.b, t * 0.95f);
                _motePropBlock.SetColor("_Color", dustCol);
                _motePropBlock.SetColor("_BaseColor", dustCol);
                _dustPool[i].Renderer.SetPropertyBlock(_motePropBlock);
            }
        }

        private void SpawnDustMote()
        {
            int index = _poolIndex;
            _poolIndex = (_poolIndex + 1) % _dustPool.Length;

            Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * 0.14f;
            Vector3 spawnPos = transform.position + randomOffset;
            Vector3 driftVel = (UnityEngine.Random.insideUnitSphere * 0.14f) - (Vector3.up * 0.06f);

            _dustPool[index].IsActive = true;
            _dustPool[index].LifeTimer = _moteLifetime;
            _dustPool[index].Velocity = driftVel;
            _dustPool[index].Transform.position = spawnPos;
            _dustPool[index].Transform.localScale = Vector3.one * _dustPool[index].InitialScale;
            _dustPool[index].Transform.rotation = UnityEngine.Random.rotation;
            _dustPool[index].Object.SetActive(true);
        }

        public static TacticalPixieLight SpawnPixie()
        {
            Vector3 spawnPos = new Vector3(0f, 1.75f, 0f);

            var tm = FindAnyObjectByType<TurnManager>();
            if (tm != null && tm.ActiveUnit != null)
            {
                spawnPos = tm.ActiveUnit.transform.position + Vector3.up * 1.75f;
            }
            else
            {
                var units = FindObjectsByType<TacticalUnit>();
                for (int i = 0; i < units.Length; i++)
                {
                    if (units[i] != null && units[i].IsPlayerControlled)
                    {
                        spawnPos = units[i].transform.position + Vector3.up * 1.75f;
                        break;
                    }
                }
            }

            if (spawnPos == new Vector3(0f, 1.75f, 0f) && UnityEngine.Camera.main != null)
            {
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                Ray ray = new Ray(UnityEngine.Camera.main.transform.position, UnityEngine.Camera.main.transform.forward);
                if (groundPlane.Raycast(ray, out float enter))
                {
                    spawnPos = ray.GetPoint(enter) + Vector3.up * 1.75f;
                }
            }

            var go = new GameObject("Tactical_Pixie_Light");
            go.transform.position = spawnPos;
            var pixie = go.AddComponent<TacticalPixieLight>();

            Debug.Log($"<color=#00E5FF>[TacticalPixieLight]</color> Pixie en vol libre déployée en {spawnPos}.");
            return pixie;
        }

#if UNITY_EDITOR
        [MenuItem("Killtime/Lumières/Instancier Pixie de Test")]
        public static void SpawnTestPixieMenu()
        {
            var pixie = SpawnPixie();
            Selection.activeGameObject = pixie.gameObject;
            Undo.RegisterCreatedObjectUndo(pixie.gameObject, "Create Tactical Pixie Light");
        }
#endif
    }
}