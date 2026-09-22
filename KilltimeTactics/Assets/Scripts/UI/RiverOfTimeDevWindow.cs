using System;
using System.IO;
using UnityEngine;
using Killtime.Core.Chrono;
using Killtime.Tactics.Chrono;

namespace Killtime.UI
{
    public class RiverOfTimeDevWindow : FloatingWindow<RiverOfTimeDevWindow>
    {
        protected override int WindowId => 998;
        protected override string Title => "Fleuve du Temps (River of Time 3D)";
        protected override Vector2 MinSize => new Vector2(620f, 420f);
        protected override Rect DefaultRect => new Rect(60f, 96f, 1020f, 680f);
        protected override KeyCode[] ToggleKeys => new[] { KeyCode.F11 };

        private const string ResourcePath = "Data/River of Time";
        private const string DiskFileName = "River of Time.json";

        [SerializeField] private RiverOfTime3DScene _scene3D;
        private RiverProjectData _projectData;

        private int _selectedEventIndex = -1;
        private RiverEventNode3D _hoveredNode;

        private string _gotoYearInput = "1772";
        private string _gotoMonthInput = "1";
        private string _gotoDayInput = "3";
        private float _lastClickTime;

        private GUIStyle _badgeStyle;
        private GUIStyle _dayTitleStyle;

        protected override void OnAwake()
        {
            Ensure3DScene();
            LoadProjectData();
        }

        protected override void OnOpened()
        {
            Ensure3DScene();
            if (_projectData == null) LoadProjectData();
            _scene3D.BuildWorld(_projectData);
            _scene3D.FocusOnMainEra();
        }

        private void Ensure3DScene()
        {
            if (_scene3D == null)
            {
                _scene3D = FindAnyObjectByType<RiverOfTime3DScene>();
                if (_scene3D == null)
                {
                    var go = new GameObject("[Controller] RiverOfTime3DScene");
                    _scene3D = go.AddComponent<RiverOfTime3DScene>();
                }
                _scene3D.Initialize((int)_windowRect.width, (int)_windowRect.height);
            }
        }

        private void LoadProjectData()
        {
            string diskPath = Path.Combine(Application.persistentDataPath, DiskFileName);
            if (File.Exists(diskPath))
            {
                try
                {
                    string json = File.ReadAllText(diskPath);
                    _projectData = RiverProjectData.ParseJson(json);
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RiverOfTime] Lecture disque impossible : {e.Message}");
                }
            }

            var textAsset = Resources.Load<TextAsset>(ResourcePath);
            if (textAsset != null && !string.IsNullOrEmpty(textAsset.text))
            {
                _projectData = RiverProjectData.ParseJson(textAsset.text);
            }
            else
            {
                _projectData = new RiverProjectData();
            }
        }

        public void SaveProjectData()
        {
            if (_projectData == null) return;
            try
            {
                string diskPath = Path.Combine(Application.persistentDataPath, DiskFileName);
                File.WriteAllText(diskPath, _projectData.ToJson());
            }
            catch (Exception e)
            {
                Debug.LogError($"[RiverOfTime] Erreur de sauvegarde : {e.Message}");
            }
        }

        protected override void DrawContent()
        {
            if (_scene3D == null)
            {
                GUILayout.Label("Initialisation du Fleuve temporel 3D...");
                return;
            }

            EnsureStyles();
            DrawTopToolbar();

            float topBarHeight = 36f;
            float detailsPanelHeight = _selectedEventIndex >= 0 ? 115f : 0f;
            float vpWidth = Mathf.Max(100f, _windowRect.width);
            float vpHeight = Mathf.Max(100f, _windowRect.height - FloatingWindowChrome.TitleHeight - topBarHeight - detailsPanelHeight);

            _scene3D.ResizeTexture((int)vpWidth, (int)vpHeight);

            Rect viewportRect = GUILayoutUtility.GetRect(vpWidth, vpHeight);
            if (_scene3D.TargetTexture != null)
            {
                GUI.DrawTexture(viewportRect, _scene3D.TargetTexture, ScaleMode.StretchToFill);
            }

            DrawTimeRulerOverlay(viewportRect);
            DrawProjectedEventBadges(viewportRect);
            HandleViewportInput(viewportRect);

            if (_selectedEventIndex >= 0)
            {
                DrawSelectedEventDetails();
            }
        }

        private void EnsureStyles()
        {
            if (_badgeStyle == null)
            {
                _badgeStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 10,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (_dayTitleStyle == null)
            {
                _dayTitleStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }
        }

        private void DrawTopToolbar()
        {
            GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(30));

            GUI.backgroundColor = new Color(0.2f, 0.7f, 1.0f);
            if (GUILayout.Button("⚡ Ère 1772 (Ch. 1-17)", GUILayout.Width(150)))
            {
                _scene3D.FocusOnMainEra();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(8);
            GUILayout.Label("Saut Date : Y", GUILayout.Width(75));
            _gotoYearInput = GUILayout.TextField(_gotoYearInput, GUILayout.Width(45));
            GUILayout.Label("M", GUILayout.Width(15));
            _gotoMonthInput = GUILayout.TextField(_gotoMonthInput, GUILayout.Width(25));
            GUILayout.Label("D", GUILayout.Width(15));
            _gotoDayInput = GUILayout.TextField(_gotoDayInput, GUILayout.Width(25));

            if (GUILayout.Button("Téléporter", GUILayout.Width(85)))
            {
                if (int.TryParse(_gotoYearInput, out int y) &&
                    int.TryParse(_gotoMonthInput, out int m) &&
                    int.TryParse(_gotoDayInput, out int d))
                {
                    float floatVal = HybrisCalendar.DateToFloat(y, m, d);
                    _scene3D.FocusOnDate(floatVal);
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("💾 Sauvegarder", GUILayout.Width(95)))
            {
                SaveProjectData();
            }

            if (GUILayout.Button("🔄 Recharger", GUILayout.Width(85)))
            {
                LoadProjectData();
                _scene3D.BuildWorld(_projectData);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawTimeRulerOverlay(Rect vpRect)
        {
            if (_scene3D.RenderCamera == null) return;

            Rect rulerRect = new Rect(vpRect.x, vpRect.y, vpRect.width, 22f);
            Color prevCol = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.12f, 0.85f);
            GUI.DrawTexture(rulerRect, Texture2D.whiteTexture);
            GUI.color = prevCol;

            float leftVal = _scene3D.WorldXToVal(_scene3D.RenderCamera.ViewportToWorldPoint(new Vector3(0f, 0.5f, 40f)).x);
            float rightVal = _scene3D.WorldXToVal(_scene3D.RenderCamera.ViewportToWorldPoint(new Vector3(1f, 0.5f, 40f)).x);
            float minVal = Mathf.Min(leftVal, rightVal);
            float maxVal = Mathf.Max(leftVal, rightVal);

            int startYear = Mathf.FloorToInt(minVal);
            int endYear = Mathf.CeilToInt(maxVal);

            if (endYear - startYear > 80) return;

            for (int y = startYear; y <= endYear; y++)
            {
                Vector3 worldPt = new Vector3(_scene3D.ValToWorldX(y), _scene3D.RenderCamera.transform.position.y - 10f, 0f);
                Vector3 vp = _scene3D.RenderCamera.WorldToViewportPoint(worldPt);

                if (vp.z > 0 && vp.x >= 0f && vp.x <= 1f)
                {
                    float sx = vpRect.x + vp.x * vpRect.width;
                    Rect labelRect = new Rect(sx - 35f, vpRect.y + 2f, 70f, 18f);
                    GUI.Label(labelRect, $"An {y}", GUI.skin.label);
                }
            }
        }

        private void DrawProjectedEventBadges(Rect vpRect)
        {
            var nodes = _scene3D.SpawnedNodes;
            Camera cam = _scene3D.RenderCamera;
            if (cam == null || nodes == null) return;

            Event currentEvent = Event.current;
            Vector2 mousePos = currentEvent != null ? currentEvent.mousePosition : Vector2.zero;

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null) continue;

                Vector3 vp = cam.WorldToViewportPoint(node.TargetWorldPosition);
                if (vp.z <= 0f || vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.05f || vp.y > 1.05f)
                    continue;

                float sx = vpRect.x + vp.x * vpRect.width;
                float sy = vpRect.y + (1f - vp.y) * vpRect.height;

                bool isSelected = (_selectedEventIndex == node.EventIndex);
                bool isHovered = (_hoveredNode == node);

                string label = node.Data.Name;
                if (label.Length > 24) label = label.Substring(0, 22) + "…";
                if (!string.IsNullOrEmpty(node.Data.ChapterPart))
                {
                    label = $"[{node.Data.ChapterPart}] " + label;
                }

                float badgeW = Mathf.Clamp(label.Length * 7.5f + 18f, 90f, 220f);
                float badgeH = 22f;
                Rect badgeRect = new Rect(sx - badgeW * 0.5f, sy - 34f, badgeW, badgeH);

                Color prevBg = GUI.backgroundColor;
                Color prevCol = GUI.color;

                if (isSelected)
                {
                    GUI.backgroundColor = new Color(0.0f, 0.9f, 1.0f, 0.95f);
                    GUI.color = Color.white;
                }
                else if (isHovered)
                {
                    GUI.backgroundColor = new Color(1.0f, 0.85f, 0.2f, 0.95f);
                    GUI.color = Color.black;
                }
                else
                {
                    Color baseCol = node.Data.Color;
                    GUI.backgroundColor = new Color(baseCol.r * 0.45f, baseCol.g * 0.45f, baseCol.b * 0.45f, 0.88f);
                    GUI.color = Color.white;
                }

                if (GUI.Button(badgeRect, label, _badgeStyle))
                {
                    _selectedEventIndex = node.EventIndex;
                    _scene3D.FocusOnEvent(_selectedEventIndex);
                }

                GUI.backgroundColor = prevBg;
                GUI.color = prevCol;
            }
        }

        private void HandleViewportInput(Rect vpRect)
        {
            Event e = Event.current;
            if (e == null) return;

            Vector2 mousePos = e.mousePosition;
            bool inside = vpRect.Contains(mousePos);

            if (inside)
            {
                Vector2 normPos = new Vector2(
                    (mousePos.x - vpRect.x) / vpRect.width,
                    1f - (mousePos.y - vpRect.y) / vpRect.height
                );

                _hoveredNode = _scene3D.RaycastNode(normPos);

                if (e.type == EventType.ScrollWheel)
                {
                    _scene3D.UpdateCameraControls(Vector2.zero, -e.delta.y, false, false);
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag)
                {
                    if (e.button == 0)
                    {
                        _scene3D.UpdateCameraControls(e.delta, 0f, true, false);
                        e.Use();
                    }
                    else if (e.button == 1 || e.button == 2)
                    {
                        _scene3D.UpdateCameraControls(e.delta, 0f, false, true);
                        e.Use();
                    }
                }
                else if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (_hoveredNode != null)
                    {
                        _selectedEventIndex = _hoveredNode.EventIndex;

                        float now = Time.realtimeSinceStartup;
                        if (now - _lastClickTime < 0.3f)
                        {
                            _scene3D.FocusOnEvent(_selectedEventIndex);
                        }
                        _lastClickTime = now;
                        e.Use();
                    }
                }
            }
            else
            {
                _hoveredNode = null;
            }

            if (_hoveredNode != null && inside)
            {
                Rect tipRect = new Rect(mousePos.x + 14f, mousePos.y + 14f, 280f, 48f);
                GUI.Box(tipRect, "");
                GUI.Label(new Rect(tipRect.x + 6f, tipRect.y + 4f, 268f, 20f), $"<b>{_hoveredNode.Data.Name}</b>");
                GUI.Label(new Rect(tipRect.x + 6f, tipRect.y + 24f, 268f, 18f), $"[{_hoveredNode.Data.DateStr}] ({_hoveredNode.Data.LineName})");
            }
        }

        private void DrawSelectedEventDetails()
        {
            if (_projectData == null || _selectedEventIndex < 0 || _selectedEventIndex >= _projectData.Events.Count)
                return;

            var ev = _projectData.Events[_selectedEventIndex];

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Height(105));
            GUILayout.BeginHorizontal();

            GUI.color = ev.Color;
            GUILayout.Label($"<b>{ev.Name}</b>", GUILayout.Width(350));
            GUI.color = Color.white;

            GUILayout.Label($"Date : <b>{ev.DateStr}</b>", GUILayout.Width(130));
            GUILayout.Label($"Ligne : <b>{ev.LineName}</b>", GUILayout.Width(180));

            if (!string.IsNullOrEmpty(ev.ChapterPart))
            {
                GUI.color = Color.cyan;
                GUILayout.Label($"[Chapitre : {ev.ChapterPart}]", GUILayout.Width(180));
                GUI.color = Color.white;
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("🎯 Centrer", GUILayout.Width(85)))
            {
                _scene3D.FocusOnEvent(_selectedEventIndex);
            }

            if (ev.Type == "trigger" && !string.IsNullOrEmpty(ev.TargetBranch))
            {
                GUI.backgroundColor = new Color(0.2f, 0.85f, 1.0f);
                if (GUILayout.Button($"⚡ Suivre ({ev.TargetBranch})", GUILayout.Width(160)))
                {
                    if (_projectData.Timelines.TryGetValue(ev.TargetBranch, out var bData))
                    {
                        _scene3D.FocusOnDate(bData.StartVal);
                    }
                }
                GUI.backgroundColor = Color.white;
            }

            if (GUILayout.Button("✕", GUILayout.Width(28)))
            {
                _selectedEventIndex = -1;
            }

            GUILayout.EndHorizontal();

            GUILayout.Label($"Type : <i>{ev.Type}</i> | Valeur Temporelle : {ev.FloatVal:F4} | Causalité Hybris");

            GUILayout.EndVertical();
        }
    }
}