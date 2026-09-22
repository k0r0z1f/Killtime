using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Chrono;

namespace Killtime.Tactics.Chrono
{
    public class RiverEventNode3D : MonoBehaviour
    {
        public int EventIndex;
        public RiverEventData Data;
        public Vector3 TargetWorldPosition;
    }

    public class RiverOfTime3DScene : MonoBehaviour
    {
        public const float EraCenterYear = 1772.0f;
        private const float SceneOriginY = 5000f;
        private const float TrackSpacingY = 12.0f;
        private const float DepthLayerZ = 5.0f;

        private Camera _renderCam;
        private RenderTexture _renderTex;
        private Transform _sceneRoot;
        private RiverProjectData _projectData;

        private float _timeScaleX = 180.0f; // 1 an = 180m -> 1 jour = 0.6m
        private Vector3 _cameraFocusPoint;
        private Vector3 _cameraFocusVelocity;
        private float _cameraDistance = 70.0f;
        private float _targetDistance = 70.0f;
        private float _distanceVelocity;

        private float _camPitch = 16.0f;
        private float _camYaw = 0.0f;

        private readonly List<RiverEventNode3D> _spawnedNodes = new();
        private readonly List<GameObject> _spawnedVisuals = new();

        private Material _lineMaterial;
        private Material _nodeMaterial;

        public Camera RenderCamera => _renderCam;
        public RenderTexture TargetTexture => _renderTex;
        public IReadOnlyList<RiverEventNode3D> SpawnedNodes => _spawnedNodes;
        public float TimeScaleX => _timeScaleX;

        public void Initialize(int width, int height)
        {
            EnsureMaterials();
            EnsureRenderTexture(width, height);
            CreateIsolatedScene();
        }

        public void ResizeTexture(int width, int height)
        {
            if (width < 32 || height < 32) return;
            if (_renderTex != null && _renderTex.width == width && _renderTex.height == height) return;

            if (_renderTex != null)
            {
                if (_renderCam != null) _renderCam.targetTexture = null;
                _renderTex.Release();
                Destroy(_renderTex);
            }

            _renderTex = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "[Chrono] RiverRenderTexture",
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear
            };

            if (_renderCam != null) _renderCam.targetTexture = _renderTex;
        }

        private void EnsureMaterials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color");

            if (_lineMaterial == null)
            {
                _lineMaterial = new Material(shader) { name = "M_ChronoLine" };
            }
            if (_nodeMaterial == null)
            {
                _nodeMaterial = new Material(shader) { name = "M_ChronoNode" };
            }
        }

        private void EnsureRenderTexture(int width, int height)
        {
            width = Mathf.Max(64, width);
            height = Mathf.Max(64, height);

            if (_renderTex == null)
            {
                _renderTex = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = "[Chrono] RiverRenderTexture",
                    antiAliasing = 2,
                    filterMode = FilterMode.Bilinear
                };
            }
        }

        private void CreateIsolatedScene()
        {
            if (_sceneRoot != null) return;

            var rootObj = new GameObject("[Chrono] River_Isolated_Realm");
            rootObj.transform.position = new Vector3(0f, SceneOriginY, 0f);
            DontDestroyOnLoad(rootObj);
            _sceneRoot = rootObj.transform;

            var camObj = new GameObject("[Chrono] River_Camera");
            camObj.transform.SetParent(_sceneRoot);
            _renderCam = camObj.AddComponent<Camera>();
            _renderCam.clearFlags = CameraClearFlags.SolidColor;
            _renderCam.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            _renderCam.nearClipPlane = 0.5f;
            _renderCam.farClipPlane = 4000f;
            _renderCam.fieldOfView = 48f;
            _renderCam.targetTexture = _renderTex;

            var lightObj = new GameObject("[Chrono] Ambient_Light");
            lightObj.transform.SetParent(_sceneRoot);
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.8f, 0.9f, 1.0f);
            light.intensity = 1.3f;
            lightObj.transform.rotation = Quaternion.Euler(50f, -25f, 0f);

            _cameraFocusPoint = new Vector3(0f, SceneOriginY, 0f);
        }

        public void BuildWorld(RiverProjectData data)
        {
            _projectData = data;
            ClearVisuals();

            if (_projectData == null || _sceneRoot == null) return;

            foreach (var kvp in _projectData.Timelines)
            {
                BuildTimelineVisual(kvp.Key, kvp.Value);
            }

            for (int i = 0; i < _projectData.Events.Count; i++)
            {
                BuildEventNode(i, _projectData.Events[i]);
            }

            FocusOnDate(1772.0066f);
        }

        private void ClearVisuals()
        {
            for (int i = 0; i < _spawnedVisuals.Count; i++)
            {
                if (_spawnedVisuals[i] != null) Destroy(_spawnedVisuals[i]);
            }
            _spawnedVisuals.Clear();
            _spawnedNodes.Clear();
        }

        public float ValToWorldX(float val)
        {
            return (val - EraCenterYear) * _timeScaleX;
        }

        public float WorldXToVal(float worldX)
        {
            return EraCenterYear + (worldX / _timeScaleX);
        }

        public Vector3 GetTimelinePosition(string timelineName, float floatVal)
        {
            float x = ValToWorldX(floatVal);
            float y = SceneOriginY;
            float z = 0f;

            if (_projectData != null && _projectData.Timelines.TryGetValue(timelineName, out var tData))
            {
                y = SceneOriginY + (tData.Y / 110f) * TrackSpacingY;
                z = (tData.Y / 110f) * DepthLayerZ;
            }

            return new Vector3(x, y, z);
        }

        private void BuildTimelineVisual(string name, RiverTimelineData tData)
        {
            float yPos = SceneOriginY + (tData.Y / 110f) * TrackSpacingY;
            float zPos = (tData.Y / 110f) * DepthLayerZ;

            float minVal = tData.StartVal;
            float maxVal = minVal;

            if (tData.Parent == null)
            {
                minVal = -50f;
                maxVal = 1800f;
            }
            else
            {
                maxVal = minVal + 60f;
            }

            foreach (var ev in _projectData.Events)
            {
                if (ev.LineName == name)
                {
                    if (ev.FloatVal < minVal) minVal = ev.FloatVal;
                    if (ev.FloatVal > maxVal) maxVal = ev.FloatVal;
                }
            }

            maxVal += 40f;
            minVal -= 10f;

            float minX = ValToWorldX(minVal);
            float maxX = ValToWorldX(maxVal);

            var lineObj = new GameObject($"[Line] {name}");
            lineObj.transform.SetParent(_sceneRoot);
            _spawnedVisuals.Add(lineObj);

            var lineRenderer = lineObj.AddComponent<LineRenderer>();
            lineRenderer.material = _lineMaterial;
            lineRenderer.startColor = tData.Color;
            lineRenderer.endColor = tData.Color;
            lineRenderer.startWidth = 1.4f;
            lineRenderer.endWidth = 1.4f;
            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, new Vector3(minX, yPos, zPos));
            lineRenderer.SetPosition(1, new Vector3(maxX, yPos, zPos));

            if (tData.Parent != null && _projectData.Timelines.TryGetValue(tData.Parent, out var parentData))
            {
                BuildJumpArc(name, tData, parentData);
            }
        }

        private void BuildJumpArc(string branchName, RiverTimelineData branchData, RiverTimelineData parentData)
        {
            RiverEventData triggerEvent = null;
            for (int i = 0; i < _projectData.Events.Count; i++)
            {
                var ev = _projectData.Events[i];
                if (ev.Type == "trigger" && ev.TargetBranch == branchName)
                {
                    triggerEvent = ev;
                    break;
                }
            }

            float triggerVal = triggerEvent != null ? triggerEvent.FloatVal : branchData.StartVal;
            Vector3 p0 = GetTimelinePosition(parentData.Name, triggerVal);
            Vector3 p3 = GetTimelinePosition(branchName, branchData.StartVal);

            float deltaX = p3.x - p0.x;
            float midY = (p0.y + p3.y) * 0.5f + (p3.y >= p0.y ? 9.0f : -9.0f);
            float midZ = Mathf.Max(p0.z, p3.z) + 8.0f;

            float cruise = Mathf.Clamp(Mathf.Abs(deltaX) * 0.35f, 6.0f, 60.0f) * Mathf.Sign(deltaX);
            Vector3 p1 = new Vector3(p0.x + cruise, midY, midZ);
            Vector3 p2 = new Vector3(p3.x - cruise, midY, midZ);

            var arcObj = new GameObject($"[JumpArc] {parentData.Name} -> {branchName}");
            arcObj.transform.SetParent(_sceneRoot);
            _spawnedVisuals.Add(arcObj);

            var line = arcObj.AddComponent<LineRenderer>();
            line.material = _lineMaterial;
            line.startColor = branchData.Color;
            line.endColor = branchData.Color;
            line.startWidth = 1.0f;
            line.endWidth = 1.0f;
            line.useWorldSpace = true;

            const int segments = 32;
            line.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 pt = CalculateCubicBezierPoint(t, p0, p1, p2, p3);
                line.SetPosition(i, pt);
            }
        }

        private static Vector3 CalculateCubicBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            return (uu * u * p0) + (3f * uu * t * p1) + (3f * u * tt * p2) + (tt * t * p3);
        }

        private void BuildEventNode(int index, RiverEventData ev)
        {
            Vector3 pos = GetTimelinePosition(ev.LineName, ev.FloatVal);

            var stemObj = new GameObject($"[Stem_{index}]");
            stemObj.transform.SetParent(_sceneRoot);
            _spawnedVisuals.Add(stemObj);

            var stemLine = stemObj.AddComponent<LineRenderer>();
            stemLine.material = _lineMaterial;
            stemLine.startColor = new Color(ev.Color.r, ev.Color.g, ev.Color.b, 0.45f);
            stemLine.endColor = ev.Color;
            stemLine.startWidth = 0.4f;
            stemLine.endWidth = 0.4f;
            stemLine.useWorldSpace = true;
            stemLine.positionCount = 2;
            stemLine.SetPosition(0, pos);
            stemLine.SetPosition(1, pos + Vector3.up * 4.0f);

            var nodeObj = GameObject.CreatePrimitive(ev.Type == "trigger" ? PrimitiveType.Sphere : PrimitiveType.Cube);
            nodeObj.name = $"[Event_{index}] {ev.Name}";
            nodeObj.transform.SetParent(_sceneRoot);
            nodeObj.transform.position = pos + Vector3.up * 4.0f;

            if (ev.Type == "trigger")
            {
                nodeObj.transform.localScale = Vector3.one * 2.8f;
            }
            else
            {
                nodeObj.transform.localScale = new Vector3(2.2f, 3.2f, 2.2f);
                nodeObj.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            }

            var renderer = nodeObj.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(_nodeMaterial) { color = ev.Color };
                renderer.material = mat;
            }

            var nodeComp = nodeObj.AddComponent<RiverEventNode3D>();
            nodeComp.EventIndex = index;
            nodeComp.Data = ev;
            nodeComp.TargetWorldPosition = nodeObj.transform.position;

            _spawnedNodes.Add(nodeComp);
            _spawnedVisuals.Add(nodeObj);
        }

        public void UpdateCameraControls(Vector2 dragDelta, float zoomDelta, bool isPanning, bool isRotating)
        {
            if (isPanning)
            {
                Vector3 right = _renderCam.transform.right;
                Vector3 up = _renderCam.transform.up;
                _cameraFocusPoint -= (right * dragDelta.x + up * dragDelta.y) * (_cameraDistance * 0.0022f);
            }
            else if (isRotating)
            {
                _camYaw += dragDelta.x * 0.25f;
                _camPitch = Mathf.Clamp(_camPitch - dragDelta.y * 0.25f, -80f, 80f);
            }

            if (Mathf.Abs(zoomDelta) > 0.0001f)
            {
                _targetDistance = Mathf.Clamp(_targetDistance - zoomDelta * (_targetDistance * 0.22f), 15f, 500f);
            }

            _cameraDistance = Mathf.SmoothDamp(_cameraDistance, _targetDistance, ref _distanceVelocity, 0.05f);

            Quaternion rot = Quaternion.Euler(_camPitch, _camYaw, 0f);
            Vector3 targetCamPos = _cameraFocusPoint - (rot * Vector3.forward * _cameraDistance);

            _renderCam.transform.rotation = rot;
            _renderCam.transform.position = targetCamPos;
        }

        public void FocusOnEvent(int eventIndex)
        {
            if (eventIndex >= 0 && eventIndex < _spawnedNodes.Count)
            {
                var node = _spawnedNodes[eventIndex];
                if (node != null)
                {
                    _cameraFocusPoint = node.TargetWorldPosition;
                    _targetDistance = 38f;
                }
            }
        }

        public void FocusOnDate(float floatVal)
        {
            float worldX = ValToWorldX(floatVal);
            float trackY = SceneOriginY + 24.0f;
            _cameraFocusPoint = new Vector3(worldX, trackY, 0f);
            _targetDistance = 60f;
        }

        public void FocusOnMainEra()
        {
            FocusOnDate(1772.04f);
            _targetDistance = 75f;
        }

        public RiverEventNode3D RaycastNode(Vector2 viewportNormalized)
        {
            if (_renderCam == null) return null;

            Ray ray = _renderCam.ViewportPointToRay(new Vector3(viewportNormalized.x, viewportNormalized.y, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, 4000f))
            {
                return hit.collider.GetComponent<RiverEventNode3D>();
            }
            return null;
        }

        private void OnDestroy()
        {
            if (_renderTex != null)
            {
                if (_renderCam != null) _renderCam.targetTexture = null;
                _renderTex.Release();
                Destroy(_renderTex);
            }

            if (_sceneRoot != null)
            {
                Destroy(_sceneRoot.gameObject);
            }
        }
    }
}