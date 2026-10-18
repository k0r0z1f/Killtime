// InventoryDevWindow.Studio.cs — split move-only de InventoryDevWindow.cs (studio 3D, aperçu, styles).
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    public partial class InventoryDevWindow
    {

        // ================= STUDIO 3D =================
        private void DrawStudioPreviewPanel()
        {
            GUILayout.Label("<b>Inspection 3D (prefab réel ou placeholder) :</b>");
            Rect rect = GUILayoutUtility.GetRect(280, 200);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.015f, 0.035f, 0.065f, 0.98f);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            if (_previewRT != null) GUI.DrawTexture(rect, _previewRT, ScaleMode.ScaleToFit);

            Event e = Event.current;
            if (rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1))
                {
                    _isDraggingPreview = true;
                    _lastMousePos = e.mousePosition;
                    e.Use();
                }
            }

            if (_isDraggingPreview)
            {
                if (e.type == EventType.MouseDrag)
                {
                    _previewYaw -= (e.mousePosition.x - _lastMousePos.x) * 1.5f;
                    _previewPitch = Mathf.Clamp(_previewPitch + (e.mousePosition.y - _lastMousePos.y) * 1.2f, -60f, 60f);
                    _lastMousePos = e.mousePosition;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _isDraggingPreview = false;
                    e.Use();
                }
            }

            Rect labelRect = new Rect(rect.x, rect.y + rect.height - 18, rect.width, 16);
            var footerStyle = new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.MiddleCenter };
            footerStyle.normal.textColor = new Color(0.0f, 0.9f, 1.0f, 0.7f);
            var prev = GetCurrentPreviewItem();
            string tag = prev == null ? "—" : (string.IsNullOrEmpty(prev.PrefabPath) || ArmoryPlaceholderFactory.LoadRealPrefab(prev.PrefabPath) == null ? "🧱 placeholder procédural" : "🔫 prefab réel");
            GUI.Label(labelRect, $"↔ Pivoter (glisser) • {tag}", footerStyle);
        }

        private void EnsurePreviewStudio()
        {
            if (_previewRT == null)
            {
                _previewRT = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32) { name = "GunPreview_RT" };
                _previewRT.Create();
            }

            if (_previewStudioRoot == null)
            {
                _previewStudioRoot = new GameObject("[Studio] GunModelPreview");
                _previewStudioRoot.transform.position = new Vector3(6000f, -5000f, 6000f);

                var camGo = new GameObject("GunCamera");
                camGo.transform.SetParent(_previewStudioRoot.transform, false);
                _previewCam = camGo.AddComponent<Camera>();
                _previewCam.clearFlags = CameraClearFlags.SolidColor;
                _previewCam.backgroundColor = new Color(0.015f, 0.035f, 0.065f, 1.0f);
                _previewCam.fieldOfView = 32f;
                _previewCam.nearClipPlane = 0.05f;
                _previewCam.farClipPlane = 40f;
                _previewCam.targetTexture = _previewRT;
                _previewCam.enabled = false;

                var lightGo = new GameObject("GunKeyLight");
                lightGo.transform.SetParent(_previewStudioRoot.transform, false);
                var key = lightGo.AddComponent<Light>();
                key.type = LightType.Directional;
                key.color = new Color(0.95f, 0.98f, 1.0f);
                key.intensity = 1.4f;
                lightGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

                var rimGo = new GameObject("GunRimLight");
                rimGo.transform.SetParent(_previewStudioRoot.transform, false);
                var rim = rimGo.AddComponent<Light>();
                rim.type = LightType.Directional;
                rim.color = new Color(0.0f, 0.85f, 1.0f);
                rim.intensity = 0.85f;
                rimGo.transform.rotation = Quaternion.Euler(-20f, 150f, 0f);
            }
        }

        private void UpdatePreviewModel(InventoryItem item)
        {
            string key = item != null ? $"{item.Name}|{item.PrefabPath}|{item.PlaceholderKind}" : "__NONE__";
            if (key == _lastLoadedModelKey && _currentPreviewInstance != null) return;

            EnsurePreviewStudio();
            _lastLoadedModelKey = key;

            if (_currentPreviewInstance != null)
            {
                Destroy(_currentPreviewInstance);
                _currentPreviewInstance = null;
            }

            if (item != null)
                _currentPreviewInstance = ArmoryPlaceholderFactory.ResolveOrBuild(item, _previewStudioRoot.transform);
            else
                _currentPreviewInstance = ArmoryPlaceholderFactory.BuildByKind("Generic", _previewStudioRoot.transform, "Placeholder_Vide");

            _currentPreviewInstance.transform.localPosition = Vector3.zero;
            _currentPreviewInstance.transform.localRotation = Quaternion.Euler(_previewPitch, _previewYaw, 0f);
            FramePreviewCamera();
        }

        private void FramePreviewCamera()
        {
            if (_previewCam == null || _currentPreviewInstance == null) return;
            var renderers = _currentPreviewInstance.GetComponentsInChildren<Renderer>();
            Bounds bounds;
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            }
            else bounds = new Bounds(_currentPreviewInstance.transform.position, Vector3.one * 0.5f);

            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            maxDim = Mathf.Max(maxDim, 0.25f);
            float dist = (maxDim * 0.5f) / Mathf.Tan(_previewCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            dist *= 1.4f;
            Vector3 focus = bounds.center;
            _previewCam.transform.position = focus + new Vector3(0f, 0f, dist);
            _previewCam.transform.LookAt(focus);
        }

        private void RenderPreviewStudio()
        {
            if (_previewCam == null || _currentPreviewInstance == null) return;
            if (!_isDraggingPreview) _previewYaw = (_previewYaw + Time.unscaledDeltaTime * 18f) % 360f;
            _currentPreviewInstance.transform.localRotation = Quaternion.Euler(_previewPitch, _previewYaw, 0f);
            _previewCam.Render();
        }

        private void CleanupPreviewStudio()
        {
            if (_currentPreviewInstance != null) { Destroy(_currentPreviewInstance); _currentPreviewInstance = null; }
            if (_previewStudioRoot != null) { Destroy(_previewStudioRoot); _previewStudioRoot = null; }
            if (_previewRT != null) { _previewRT.Release(); Destroy(_previewRT); _previewRT = null; }
            _lastLoadedModelKey = "__NONE__";
        }
        private static GUIStyle RichLabel()
        {
            if (_richLabel == null)
                _richLabel = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 10 };
            return _richLabel;
        }
    }
}
