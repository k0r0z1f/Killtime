using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;

namespace Killtime.Story
{
    public partial class ScenarioEditorDevWindow : FloatingWindow<ScenarioEditorDevWindow>
    {

        private Vector2 ResolveViewSize(Rect? canvasRect)
        {
            if (canvasRect.HasValue && canvasRect.Value.width > 100f && canvasRect.Value.height > 100f)
                return new Vector2(canvasRect.Value.width, canvasRect.Value.height);
            if (_lastCanvasSize.x > 100f && _lastCanvasSize.y > 100f)
                return _lastCanvasSize;
            return new Vector2(Mathf.Max(800f, _windowRect.width - 24f), Mathf.Max(420f, _windowRect.height - 140f));
        }

        private void FocusNodeByIndex(int index, Rect? canvasRect = null)
        {
            if (_data.Nodes.Count == 0) return;
            // Navigation circulaire : on peut parcourir tous les nœuds avec ◀ ▶ sans cul-de-sac.
            int n = _data.Nodes.Count;
            _focusedNodeIndex = ((index % n) + n) % n;
            CenterViewOnNode(_data.Nodes[_focusedNodeIndex], canvasRect);
        }

        private void ClampPanToContent(Rect? canvasRect = null)
        {
            if (!GetSceneContentBounds(out float minX, out float minY, out float maxX, out float maxY)) return;
            Vector2 view = ResolveViewSize(canvasRect);

            const float edgePadding = 40f;
            float contentW = (maxX - minX) * _zoom;
            float contentH = (maxY - minY) * _zoom;

            if (contentW <= view.x - edgePadding * 2f)
            {
                float idealX = (view.x - (minX + maxX) * _zoom) * 0.5f;
                _graphPan.x = Mathf.Clamp(_graphPan.x, idealX - 180f, idealX + 180f);
            }
            else
            {
                float minPanX = view.x - edgePadding - maxX * _zoom;
                float maxPanX = edgePadding - minX * _zoom;
                _graphPan.x = Mathf.Clamp(_graphPan.x, minPanX, maxPanX);
            }

            if (contentH <= view.y - edgePadding * 2f)
            {
                float idealY = (view.y - (minY + maxY) * _zoom) * 0.5f;
                _graphPan.y = Mathf.Clamp(_graphPan.y, idealY - 140f, idealY + 140f);
            }
            else
            {
                float minPanY = view.y - edgePadding - maxY * _zoom;
                float maxPanY = edgePadding - minY * _zoom;
                _graphPan.y = Mathf.Clamp(_graphPan.y, minPanY, maxPanY);
            }
        }

        private void CenterViewOnNode(SceneNodeData node, Rect? canvasRect = null)
        {
            if (node == null) return;
            Rect box = ComputeNodeBoundingBox(node);
            Vector2 view = ResolveViewSize(canvasRect);
            _lastCanvasSize = view;

            float fitZX = (view.x - 80f) / Mathf.Max(1f, box.width);
            float fitZY = (view.y - 80f) / Mathf.Max(1f, box.height);
            _zoom = Mathf.Clamp(Mathf.Min(1.0f, fitZX, fitZY), 0.35f, 1.0f);
            _graphPan = new Vector2(
                (view.x * 0.5f) - box.center.x * _zoom,
                (view.y * 0.5f) - box.center.y * _zoom
            );
            _lastViewWasAll = false;
            _fitZoom = _zoom;
        }

        private void ZoomAroundCenter(float newZoom, Rect? canvasRect = null)
        {
            newZoom = Mathf.Clamp(newZoom, 0.12f, 2.0f);
            Vector2 view = ResolveViewSize(canvasRect);
            Vector2 centerScreen = new Vector2(view.x * 0.5f, view.y * 0.5f);
            Vector2 worldUnderCenter = (centerScreen - _graphPan) / _zoom;
            _zoom = newZoom;
            _graphPan = centerScreen - worldUnderCenter * _zoom;
            ClampPanToContent(canvasRect);
        }

        private void FocusAllNodes(Rect? canvasRect = null)
        {
            if (!GetSceneContentBounds(out float minX, out float minY, out float maxX, out float maxY)) return;

            float width = maxX - minX;
            float height = maxY - minY;
            if (width <= 0f || height <= 0f) return;

            Vector2 view = ResolveViewSize(canvasRect);
            _lastCanvasSize = view;

            const float padView = 50f;
            float targetZoomX = (view.x - padView * 2f) / width;
            float targetZoomY = (view.y - padView * 2f) / height;
            _zoom = Mathf.Clamp(Mathf.Min(targetZoomX, targetZoomY), 0.15f, 1.0f);

            _graphPan = new Vector2(
                (view.x * 0.5f) - (minX + width * 0.5f) * _zoom,
                (view.y * 0.5f) - (minY + height * 0.5f) * _zoom
            );
            _lastViewWasAll = true;
            _fitZoom = _zoom;
        }

        private void RecenterNode(SceneNodeData node, Rect? canvasRect = null)
        {
            // Recentrage SANS toucher au zoom (édition rapprochée préservée au redimensionnement).
            if (node == null) return;
            Rect box = ComputeNodeBoundingBox(node);
            Vector2 view = ResolveViewSize(canvasRect);
            _lastCanvasSize = view;
            _graphPan = new Vector2(
                (view.x * 0.5f) - box.center.x * _zoom,
                (view.y * 0.5f) - box.center.y * _zoom
            );
            ClampPanToContent(canvasRect);
        }

        private static float SanitizeCoord(float v, float fallback)
        {
            return (float.IsNaN(v) || float.IsInfinity(v)) ? fallback : Mathf.Clamp(v, -20000f, 20000f);
        }
        private Vector2 GraphPoint(Vector2 world)
        {
            return _graphPan + world * _zoom;
        }

        private Rect GraphRect(Rect worldRect)
        {
            Vector2 p = GraphPoint(worldRect.position);
            return new Rect(p.x, p.y, worldRect.width * _zoom, worldRect.height * _zoom);
        }

        private bool GraphPrimaryDown(Vector2 mouseWorld, Rect worldRect)
        {
            if (_pickerCapturesMouse) return false;
            Event evt = Event.current;
            return evt.type == EventType.MouseDown && evt.button == 0 && worldRect.Contains(mouseWorld);
        }

        private void GraphLabel(Rect worldRect, string text)
        {
            GUI.Label(GraphRect(worldRect), text);
        }

        private void GraphLabel(Rect worldRect, string text, GUIStyle style)
        {
            GUI.Label(GraphRect(worldRect), text, style);
        }

        private bool GraphButton(Rect worldRect, string text)
        {
            bool pressed = GUI.Button(GraphRect(worldRect), text);
            return pressed && !_pickerCapturesMouse;
        }

        private string GraphTextField(Rect worldRect, string text)
        {
            string result = GUI.TextField(GraphRect(worldRect), text);
            return _pickerCapturesMouse ? text : result;
        }

        private string GraphTextArea(Rect worldRect, string text)
        {
            string result = GUI.TextArea(GraphRect(worldRect), text);
            return _pickerCapturesMouse ? text : result;
        }

        private bool GraphToggle(Rect worldRect, bool value, string text)
        {
            bool result = GUI.Toggle(GraphRect(worldRect), value, text);
            return _pickerCapturesMouse ? value : result;
        }

        private int GraphToolbar(Rect worldRect, int selected, string[] contents)
        {
            int result = GUI.Toolbar(GraphRect(worldRect), selected, contents);
            return _pickerCapturesMouse ? selected : result;
        }

        private void DrawGraphSolidRect(Rect worldRect, Color color)
        {
            DrawSolidRect(GraphRect(worldRect), color);
        }

        private float DrawFloatInput(Rect worldRect, string key, float value, float min, float max)
        {
            if (!_floatInputBuffers.TryGetValue(key, out var state)
                || (Mathf.Abs(state.lastVal - value) > 0.0001f && !state.text.EndsWith(".") && !state.text.EndsWith(",")))
            {
                state = (value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), value);
                _floatInputBuffers[key] = state;
            }

            string newText = GraphTextField(worldRect, state.text);
            if (newText != state.text)
            {
                float parsedVal = value;
                if (float.TryParse(newText.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    parsedVal = Mathf.Clamp(parsed, min, max);
                }
                _floatInputBuffers[key] = (newText, parsedVal);
                return parsedVal;
            }

            return value;
        }

        private void DrawNodeGraphCanvas(Rect canvasRect)
        {
            _lastCanvasSize = new Vector2(canvasRect.width, canvasRect.height);
            _hasHoverCard = false;

            Event evt = Event.current;
            Vector2 mouseLocal = evt.mousePosition - canvasRect.min;
            Vector2 mouseWorld = (mouseLocal - _graphPan) / Mathf.Max(0.0001f, _zoom);
            _graphMouseScreenPos = evt.mousePosition;
            // Tags de bord AVANT toute interaction : un tag sous le curseur bloque le
            // pan (clic = cadrer la cible) et sert de cible de drop pendant un drag.
            CollectOffscreenLinkTags(canvasRect);
            _pickerCapturesMouse = (IsItemPickerOpen() && GetItemPickerRect(canvasRect).Contains(evt.mousePosition))
                || (_actorDropKey != null && WorldToScreenRect(canvasRect, _actorDropWorld).Contains(evt.mousePosition))
                || IsQuickCreateOpen();

            // Clic hors du dropdown locuteurs => le refermer (sauf sur son bouton ▼).
            if (_actorDropKey != null && evt.type == EventType.MouseDown
                && !_actorDropWorld.Contains(mouseWorld) && !_actorDropAnchorWorld.Contains(mouseWorld))
                _actorDropKey = null;

            // Popup quick-create MODALE : interceptée AVANT tout le graphe pour que le
            // fond (cartes, champs, tags, HUD) ne puisse jamais voler le clic — sinon la
            // popup semble "incliquable" et ne se referme plus. Les clics dans le panneau
            // traversent (le graphe les ignore via _pickerCapturesMouse) et sont traités
            // au dessin du menu plus bas ; les clics hors panneau ferment ici même.
            if (_quickCreateOpen)
            {
                Rect qcEarly = ComputeQuickCreateRect(canvasRect);
                _quickCreatePanelRect = qcEarly;
                if (evt.type == EventType.MouseDown && !qcEarly.Contains(evt.mousePosition))
                {
                    CloseQuickCreateMenu();
                    evt.Use();
                }
            }

            // Anti-vol de drag : réserve un MouseDown sur header/poignée avant que les
            // champs texte peints avant (carte derrière) ne le volent. Pur calcul.
            SwallowOverlappedHeaderPress(canvasRect, mouseWorld, _graphMouseScreenPos);

            // Zoom centré sur le curseur. IMPORTANT : aucun scale de GUI.matrix n'est utilisé
            // pour le graphe. Le clipping du BeginGroup reste donc en coordonnées écran 1:1.
            if (evt.type == EventType.ScrollWheel && canvasRect.Contains(evt.mousePosition) && !_pickerCapturesMouse)
            {
                float zoomFactor = evt.delta.y > 0 ? 0.88f : 1.14f;
                float oldZoom = _zoom;
                _zoom = Mathf.Clamp(_zoom * zoomFactor, 0.15f, 2.0f);
                _graphPan = mouseLocal - (mouseLocal - _graphPan) * (_zoom / Mathf.Max(0.0001f, oldZoom));
                ClampPanToContent(canvasRect);
                evt.Use();
            }

            // Panoramique fluide (Clic droit et molette universels ; Clic gauche uniquement sur le fond vide).
            bool mouseOverTag = false;
            for (int tg = 0; tg < _offscreenTags.Count; tg++)
            {
                if (_offscreenTags[tg].ScreenRect.Contains(evt.mousePosition)) { mouseOverTag = true; break; }
            }
            bool isOverInteractiveElement = (_draggingCardKey != null || _draggingNodeId != null || _resizingCardKey != null || _wireDraft.IsActive || mouseOverTag || IsMouseOverAnyNodeOrCard(mouseWorld));
            bool pickerBlocksMouse = _pickerCapturesMouse;
            if (evt.type == EventType.MouseDown && canvasRect.Contains(evt.mousePosition))
            {
                if (evt.button == 1 || evt.button == 2 || (evt.button == 0 && !isOverInteractiveElement && !pickerBlocksMouse))
                {
                    _isPanning = true;
                    _panStartMouse = evt.mousePosition;
                    _panStartOffset = _graphPan;
                    evt.Use();
                }
            }
            else if (evt.type == EventType.MouseDrag && _isPanning)
            {
                _graphPan = _panStartOffset + (evt.mousePosition - _panStartMouse);
                ClampPanToContent(canvasRect);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && _isPanning)
            {
                _isPanning = false;
                evt.Use();
            }

            ClampPanToContent(canvasRect);

            // Le groupe de clipping reste strictement à 1:1.
            // Le zoom/pan sont appliqués explicitement par GraphRect()/GraphPoint().
            GUI.BeginGroup(canvasRect);
            DrawGraphGridBackground(new Rect(0f, 0f, canvasRect.width, canvasRect.height));

            _cachedInputSockets.Clear();
            _cachedNodeInputSockets.Clear();
            _cachedTriggerOutSockets.Clear();
            _cachedInteractableOutSockets.Clear();
            _cachedActorInSockets.Clear();
            _cachedActorTalkOutSockets.Clear();
            _cachedCinematicInputSockets.Clear();
            _cachedSoundInputSockets.Clear();
            _cachedAmbienceInputSockets.Clear();
            _cachedAmbienceStopSockets.Clear();

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                EnsureDialogueGraphIntegrity(node);
                StorySceneData.EnsureNodeLists(node);

                node.GraphPosX = SanitizeCoord(node.GraphPosX, 60f + n * 500f);
                node.GraphPosY = SanitizeCoord(node.GraphPosY, 220f);
                node.CinematicIds = StorySceneData.EnsureCineList(node.CinematicIds);
                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var dl = node.Dialogues[d];
                    if (dl == null) continue;
                    dl.GraphPosX = SanitizeCoord(dl.GraphPosX, node.GraphPosX + 20f);
                    dl.GraphPosY = SanitizeCoord(dl.GraphPosY, node.GraphPosY + 50f);
                    dl.CinematicIds = StorySceneData.EnsureCineList(dl.CinematicIds);
                }
                for (int e = 0; e < node.Events.Count; e++)
                {
                    var ev0 = node.Events[e];
                    if (ev0 == null) continue;
                    ev0.GraphPosX = SanitizeCoord(ev0.GraphPosX, node.GraphPosX + 20f);
                    ev0.GraphPosY = SanitizeCoord(ev0.GraphPosY, node.GraphPosY + 300f);
                    ev0.CinematicIds = StorySceneData.EnsureCineList(ev0.CinematicIds);
                }
                if (node.Consequences == null) node.Consequences = new System.Collections.Generic.List<SceneConsequenceData>();
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cs0 = node.Consequences[k];
                    if (cs0 == null) continue;
                    cs0.GraphPosX = SanitizeCoord(cs0.GraphPosX, node.GraphPosX + 20f);
                    cs0.GraphPosY = SanitizeCoord(cs0.GraphPosY, node.GraphPosY + 560f);
                    cs0.CinematicIds = StorySceneData.EnsureCineList(cs0.CinematicIds);
                }

                Rect box = ComputeNodeBoundingBox(node);
                _cachedNodeInputSockets[node.NodeId] = new Vector2(box.xMin, box.yMin + 18f);

                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var line = node.Dialogues[d];
                    if (string.IsNullOrEmpty(line.LineId)) line.LineId = $"line_{d + 1}";
                    _cachedInputSockets[line.LineId] = new Vector2(line.GraphPosX, line.GraphPosY + 26f);
                }

                for (int e = 0; e < node.Events.Count; e++)
                {
                    var ev = node.Events[e];
                    if (string.IsNullOrEmpty(ev.EventId)) ev.EventId = $"evt_{e + 1}";
                    _cachedInputSockets[ev.EventId] = new Vector2(ev.GraphPosX, ev.GraphPosY + 26f);
                }

                if (node.Consequences == null) node.Consequences = new System.Collections.Generic.List<SceneConsequenceData>();
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cons = node.Consequences[k];
                    if (cons == null) continue;
                    if (string.IsNullOrEmpty(cons.ConsequenceId)) cons.ConsequenceId = $"cons_{k + 1}";
                    _cachedInputSockets[cons.ConsequenceId] = new Vector2(cons.GraphPosX, cons.GraphPosY + 26f);
                }
            }

            if (_data.Triggers == null) _data.Triggers = new System.Collections.Generic.List<SceneTriggerData>();
            {
                var seenTriggerIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    trg.GraphPosX = SanitizeCoord(trg.GraphPosX, 60f + t * 380f);
                    trg.GraphPosY = SanitizeCoord(trg.GraphPosY, 900f);
                    trg.CinematicIds = StorySceneData.EnsureCineList(trg.CinematicIds);
                    if (string.IsNullOrEmpty(trg.TriggerId)) trg.TriggerId = $"trig_{t + 1}";
                    if (!seenTriggerIds.Add(trg.TriggerId)) trg.TriggerId = $"{trg.TriggerId}_{t + 1}";
                    Vector2 tsize = GetTriggerCardSize(trg);
                    _cachedTriggerOutSockets[trg.TriggerId] = new Vector2(trg.GraphPosX + tsize.x, trg.GraphPosY + 26f);
                }
            }

            if (_data.Interactables == null) _data.Interactables = new System.Collections.Generic.List<SceneInteractableSpawnData>();
            {
                var seenInterIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    it.GraphPosX = SanitizeCoord(it.GraphPosX, 60f + i * 380f);
                    it.GraphPosY = SanitizeCoord(it.GraphPosY, 1250f);
                    it.CinematicIds = StorySceneData.EnsureCineList(it.CinematicIds);
                    // Les anciennes scènes ont souvent GraphPos = 0,0 → on les décale
                    // en cascade pour éviter l'empilement avant le prochain Auto-Layout.
                    if (it.GraphPosX <= 1f && it.GraphPosY <= 1f)
                    {
                        it.GraphPosX = 60f + i * 380f;
                        it.GraphPosY = 1250f;
                    }
                    if (string.IsNullOrEmpty(it.InteractableId)) it.InteractableId = $"obj_{i + 1}";
                    if (!seenInterIds.Add(it.InteractableId)) it.InteractableId = $"{it.InteractableId}_{i + 1}";
                    Vector2 isize = GetInteractableCardSize(it);
                    _cachedInteractableOutSockets[it.InteractableId] = new Vector2(it.GraphPosX + isize.x, it.GraphPosY + 26f);
                }
            }

            if (_data.Actors == null) _data.Actors = new System.Collections.Generic.List<SceneActorSpawnData>();
            {
                var seenActorIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    a.GraphPosX = SanitizeCoord(a.GraphPosX, 60f + i * 380f);
                    a.GraphPosY = SanitizeCoord(a.GraphPosY, 1600f);
                    a.CinematicIds = StorySceneData.EnsureCineList(a.CinematicIds);
                    // Anciennes scènes : GraphPos = 0,0 → cascade sous les interactables.
                    if (a.GraphPosX <= 1f && a.GraphPosY <= 1f)
                    {
                        a.GraphPosX = 60f + i * 380f;
                        a.GraphPosY = 1600f;
                    }
                    if (string.IsNullOrEmpty(a.ActorId)) a.ActorId = $"actor_{i + 1}";
                    if (!seenActorIds.Add(a.ActorId)) a.ActorId = $"{a.ActorId}_{i + 1}";
                    Vector2 asize = GetActorCardSize(a);
                    _cachedActorInSockets[a.ActorId] = new Vector2(a.GraphPosX, a.GraphPosY + 26f);
                    if (a.TalkEntries != null)
                    {
                        for (int ti = 0; ti < a.TalkEntries.Count; ti++)
                            _cachedActorTalkOutSockets[ActorTalkSocketKey(a.ActorId, ti)] = GetActorTalkOutCenter(a, ti);
                    }
                }
            }

            if (_data.Cinematics == null) _data.Cinematics = new System.Collections.Generic.List<SceneCinematicData>();
            {
                var seenCineIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _data.Cinematics.Count; i++)
                {
                    var c = _data.Cinematics[i];
                    if (c == null) continue;
                    c.GraphPosX = SanitizeCoord(c.GraphPosX, 60f + i * 400f);
                    c.GraphPosY = SanitizeCoord(c.GraphPosY, 1950f);
                    if (c.GraphPosX <= 1f && c.GraphPosY <= 1f)
                    {
                        c.GraphPosX = 60f + i * 400f;
                        c.GraphPosY = 1950f;
                    }
                    if (string.IsNullOrEmpty(c.CinematicId)) c.CinematicId = $"cine_{i + 1}";
                    if (!seenCineIds.Add(c.CinematicId)) c.CinematicId = $"{c.CinematicId}_{i + 1}";
                    c.Shots ??= new System.Collections.Generic.List<SceneCinematicShotData>();
                    _cachedCinematicInputSockets[c.CinematicId] = new Vector2(c.GraphPosX, c.GraphPosY + 26f);
                }
            }

            if (_data.Sounds == null) _data.Sounds = new System.Collections.Generic.List<SceneSoundData>();
            {
                var seenSndIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) continue;
                    snd.GraphPosX = SanitizeCoord(snd.GraphPosX, 60f + i * 340f);
                    snd.GraphPosY = SanitizeCoord(snd.GraphPosY, 2300f);
                    if (snd.GraphPosX <= 1f && snd.GraphPosY <= 1f)
                    {
                        snd.GraphPosX = 60f + i * 340f;
                        snd.GraphPosY = 2300f;
                    }
                    if (string.IsNullOrEmpty(snd.SoundId)) snd.SoundId = $"snd_{i + 1}";
                    if (!seenSndIds.Add(snd.SoundId)) snd.SoundId = $"{snd.SoundId}_{i + 1}";
                    _cachedSoundInputSockets[snd.SoundId] = GetSoundInCenter(snd);
                    _cachedSoundStopSockets[snd.SoundId] = GetSoundStopCenter(snd);
                }
            }

            if (_data.Ambiences == null) _data.Ambiences = new System.Collections.Generic.List<SceneAmbienceTrackData>();
            {
                var seenAmbIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _data.Ambiences.Count; i++)
                {
                    var amb = _data.Ambiences[i];
                    if (amb == null) continue;
                    amb.GraphPosX = SanitizeCoord(amb.GraphPosX, 60f + i * 350f);
                    amb.GraphPosY = SanitizeCoord(amb.GraphPosY, 2600f);
                    if (string.IsNullOrEmpty(amb.AmbienceId)) amb.AmbienceId = $"amb_{i + 1}";
                    if (!seenAmbIds.Add(amb.AmbienceId)) amb.AmbienceId = $"{amb.AmbienceId}_{i + 1}";
                    _cachedAmbienceInputSockets[amb.AmbienceId] = GetAmbienceInCenter(amb);
                    _cachedAmbienceStopSockets[amb.AmbienceId] = GetAmbienceStopCenter(amb);
                }
            }

            Rect visibleWorld = GetVisibleWorldRect(canvasRect);

            // Passe 1 : frames et connexions globales.
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                Rect nodeBox = ComputeNodeBoundingBox(_data.Nodes[n]);
                if (visibleWorld.Overlaps(nodeBox))
                    DrawNodeFrame(_data.Nodes[n], n, mouseWorld);
            }

            DrawGlobalSceneWires(visibleWorld);

            if (_wireDraft.IsActive)
            {
                DrawBezierWire(_wireDraft.StartPos, mouseWorld, Color.yellow, 3.5f, _wireDraft.IsSoundLink || _wireDraft.IsSoundStopLink);
                if (evt.type == EventType.MouseUp && evt.button == 0 && !_pickerCapturesMouse)
                {
                    // NOTE : _graphMouseScreenPos est en coordonnées zone (capturé avant
                    // BeginGroup), contrairement à evt.mousePosition ici (relatif au groupe).
                    // La popup est dessinée après EndGroup : ancre en coordonnées zone.
                    FinalizeGlobalWireDraft(mouseWorld, _graphMouseScreenPos);
                    _wireDraft.IsActive = false;
                    evt.Use();
                }
            }

            // Passe 2 : cartes internes par-dessus les fils.
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                Rect nodeBox = ComputeNodeBoundingBox(node);
                if (!visibleWorld.Overlaps(nodeBox)) continue;

                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var dlg = node.Dialogues[d];
                    Vector2 size = GetDialogueCardSize(dlg);
                    if (visibleWorld.Overlaps(new Rect(dlg.GraphPosX, dlg.GraphPosY, size.x, size.y)))
                        DrawDialogueCard(dlg, d, node, mouseWorld);
                }
                for (int e = 0; e < node.Events.Count; e++)
                {
                    var ev = node.Events[e];
                    Vector2 size = GetEventCardSize(ev);
                    if (visibleWorld.Overlaps(new Rect(ev.GraphPosX, ev.GraphPosY, size.x, size.y)))
                        DrawEventCard(ev, e, node, mouseWorld);
                }
                if (node.Consequences != null)
                {
                    for (int k = 0; k < node.Consequences.Count; k++)
                    {
                        var cons = node.Consequences[k];
                        if (cons == null) continue;
                        Vector2 size = GetConsequenceCardSize(cons);
                        if (visibleWorld.Overlaps(new Rect(cons.GraphPosX, cons.GraphPosY, size.x, size.y)))
                            DrawConsequenceCard(cons, k, node, mouseWorld);
                    }
                }
            }

            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    Vector2 tsize = GetTriggerCardSize(trg);
                    if (visibleWorld.Overlaps(new Rect(trg.GraphPosX, trg.GraphPosY, tsize.x, tsize.y)))
                        DrawTriggerCard(trg, t, mouseWorld);
                }
            }

            if (_data.Interactables != null)
            {
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    Vector2 isize = GetInteractableCardSize(it);
                    if (visibleWorld.Overlaps(new Rect(it.GraphPosX, it.GraphPosY, isize.x, isize.y)))
                        DrawInteractableCard(it, i, mouseWorld);
                }
            }

            if (_data.Actors != null)
            {
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    Vector2 asize = GetActorCardSize(a);
                    if (visibleWorld.Overlaps(new Rect(a.GraphPosX, a.GraphPosY, asize.x, asize.y)))
                        DrawActorCard(a, i, mouseWorld);
                }
            }

            if (_data.Cinematics != null)
            {
                for (int i = 0; i < _data.Cinematics.Count; i++)
                {
                    var c = _data.Cinematics[i];
                    if (c == null) continue;
                    Vector2 csize = GetCinematicCardSize(c);
                    if (visibleWorld.Overlaps(new Rect(c.GraphPosX, c.GraphPosY, csize.x, csize.y)))
                        DrawCinematicCard(c, i, mouseWorld);
                }
            }

            if (_data.Sounds != null)
            {
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) continue;
                    Vector2 ssize = GetSoundCardSize(snd);
                    if (visibleWorld.Overlaps(new Rect(snd.GraphPosX, snd.GraphPosY, ssize.x, ssize.y)))
                        DrawSoundCard(snd, i, mouseWorld);
                }
            }

            if (_data.Ambiences != null)
            {
                for (int i = 0; i < _data.Ambiences.Count; i++)
                {
                    var amb = _data.Ambiences[i];
                    if (amb == null) continue;
                    Vector2 asize = GetAmbienceCardSize(amb);
                    if (visibleWorld.Overlaps(new Rect(amb.GraphPosX, amb.GraphPosY, asize.x, asize.y)))
                        DrawAmbienceCard(amb, i, mouseWorld);
                }
            }

            // Dropdown locuteurs par-dessus tout le graphe.
            DrawActorDropdownPopup(canvasRect);

            // Le seul clip du graphe est terminé ici, avant tous les overlays écran.
            GUI.EndGroup();

            DrawZoomControlsOverlay(canvasRect);
            DrawOffscreenLinkTags();

            if (_hasHoverCard && !IsItemPickerOpen() && !_quickCreateOpen && Event.current.type == EventType.Repaint)
                DrawCardHoverTooltip(canvasRect);

            DrawItemPickerPanel(canvasRect);
            DrawQuickCreateMenu(canvasRect);
        }

        private static string NormalizeForSearch(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string decomposed = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(decomposed.Length);
            for (int i = 0; i < decomposed.Length; i++)
            {
                char ch = decomposed[i];
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        private Rect WorldToScreenRect(Rect canvasRect, Rect world)
        {
            Vector2 p = GraphPoint(world.position);
            return new Rect(canvasRect.x + p.x, canvasRect.y + p.y, world.width * _zoom, world.height * _zoom);
        }

        private void ComputeVisibleNodes(Rect canvasRect, out int fully, out int partial, out int total)
        {
            total = _data.Nodes.Count;
            fully = 0;
            partial = 0;
            if (total == 0) return;
            Rect viewWorld = GetVisibleWorldRect(canvasRect);
            for (int i = 0; i < total; i++)
            {
                Rect b = ComputeNodeBoundingBox(_data.Nodes[i]);
                // "Visible" = ENTIÈREMENT dans le champ. Un nœud à cheval sur le bord compte
                // comme partiel (c'est exactement le cas "tronqué" signalé).
                if (viewWorld.Overlaps(b))
                {
                    if (viewWorld.xMin <= b.xMin && viewWorld.yMin <= b.yMin
                        && viewWorld.xMax >= b.xMax && viewWorld.yMax >= b.yMax)
                        fully++;
                    else
                        partial++;
                }
            }
        }

        private Rect GetVisibleWorldRect(Rect canvasRect)
        {
            float zoom = Mathf.Max(0.0001f, _zoom);
            Vector2 wMin = (Vector2.zero - _graphPan) / zoom;
            Vector2 wMax = (new Vector2(canvasRect.width, canvasRect.height) - _graphPan) / zoom;
            return Rect.MinMaxRect(wMin.x, wMin.y, wMax.x, wMax.y);
        }

        private static bool IsWorldRectVisible(Rect content, Rect viewport)
        {
            return viewport.Overlaps(content);
        }

        private void DrawZoomControlsOverlay(Rect canvasRect)
        {
            // HUD ancré en HAUT-GAUCHE du canvas (zone toujours visible) et dessiné en GUI
            // absolu : aucun layout, aucun chevauchement, aucune dépendance au bas de fenêtre.
            const float hudH = 26f;
            float hudW = Mathf.Min(620f, canvasRect.width - 20f);
            if (hudW < 300f || canvasRect.height < 120f) return;
            Rect hud = new Rect(canvasRect.x + 10f, canvasRect.y + 8f, hudW, hudH);
            DrawSolidRect(hud, new Color(0.02f, 0.04f, 0.07f, 0.90f));

            float x = hud.x + 6f;
            float y = hud.y + 3f;
            const float h = 20f;
            Color prevBg = GUI.backgroundColor;

            GUI.Label(new Rect(x, y, 74f, h), $"<b>{Mathf.RoundToInt(_zoom * 100f)}%</b>");
            x += 78f;
            if (GUI.Button(new Rect(x, y, 26f, h), "−")) ZoomAroundCenter(_zoom - 0.15f);
            x += 30f;
            if (GUI.Button(new Rect(x, y, 26f, h), "+")) ZoomAroundCenter(_zoom + 0.15f);
            x += 30f;
            if (GUI.Button(new Rect(x, y, 48f, h), "100%")) ZoomAroundCenter(1.0f);
            x += 52f;
            GUI.backgroundColor = new Color(1.0f, 0.75f, 0.2f);
            if (GUI.Button(new Rect(x, y, 86f, h), "Cadrer Tout")) FocusAllNodes();
            GUI.backgroundColor = prevBg;
            x += 90f;

            // Stepper nœud par nœud : garantit qu'on peut voir TOUS les nœuds un par un
            // à zoom lisible, même quand la scène complète ne tient pas à l'écran.
            if (_data.Nodes.Count > 0)
            {
                _focusedNodeIndex = Mathf.Clamp(_focusedNodeIndex, 0, _data.Nodes.Count - 1);
                if (GUI.Button(new Rect(x, y, 26f, h), "◀")) FocusNodeByIndex(_focusedNodeIndex - 1);
                x += 30f;
                GUI.Label(new Rect(x, y, 52f, h), $"<b>{_focusedNodeIndex + 1}/{_data.Nodes.Count}</b>");
                x += 56f;
                if (GUI.Button(new Rect(x, y, 26f, h), "▶")) FocusNodeByIndex(_focusedNodeIndex + 1);
                x += 30f;
            }

            // Compteur live : preuve à l'écran que tous les nœuds sont joignables.
            // Vert = tous ENTIERS dans le champ. Orange = chevauchants ou hors champ
            // → Cadrer Tout ou ◀ ▶.
            float rem = hud.xMax - 4f - x;
            if (rem > 110f)
            {
                ComputeVisibleNodes(canvasRect, out int full, out int part, out int tot);
                if (full == tot)
                {
                    GUI.Label(new Rect(x, y, rem, h), $"<color=#7CFF9B><b>{full}/{tot} visibles</b></color>");
                }
                else
                {
                    GUI.Label(new Rect(x, y, rem, h), $"<color=#FFC94D><b>{full}/{tot} +{part} bords</b></color>");
                }
            }
        }

        private void DrawGraphGridBackground(Rect rect)
        {
            DrawSolidRect(rect, new Color(0.035f, 0.045f, 0.06f, 1f));
            float baseGrid = _zoom < 0.35f ? 128f : (_zoom < 0.65f ? 64f : 32f);
            float gridSize = baseGrid * _zoom;
            float offX = ((_graphPan.x % gridSize) + gridSize) % gridSize;
            float offY = ((_graphPan.y % gridSize) + gridSize) % gridSize;

            Color gridSub = new Color(1f, 1f, 1f, 0.025f);
            Color gridMain = new Color(0.0f, 0.8f, 1f, 0.06f);

            // Lignes majeures stables : indexées en espace monde, pas en espace écran
            // (l'ancien modulo écran faisait sauter les lignes fortes pendant le pan).
            float worldLeft = (0f - _graphPan.x) / _zoom;
            float worldTop = (0f - _graphPan.y) / _zoom;
            int idx0x = Mathf.FloorToInt(worldLeft / baseGrid);
            int idx0y = Mathf.FloorToInt(worldTop / baseGrid);
            int cols = Mathf.CeilToInt(rect.width / gridSize) + 2;
            int rows = Mathf.CeilToInt(rect.height / gridSize) + 2;
            for (int i = 0; i <= cols; i++)
            {
                float x = rect.x + offX + (i - 1) * gridSize;
                if (x < rect.x || x > rect.xMax) continue;
                bool isMajor = ((idx0x + i) % 4) == 0;
                DrawSolidRect(new Rect(x, rect.y, 1f, rect.height), isMajor ? gridMain : gridSub);
            }
            for (int j = 0; j <= rows; j++)
            {
                float y = rect.y + offY + (j - 1) * gridSize;
                if (y < rect.y || y > rect.yMax) continue;
                bool isMajor = ((idx0y + j) % 4) == 0;
                DrawSolidRect(new Rect(rect.x, y, rect.width, 1f), isMajor ? gridMain : gridSub);
            }
        }

        private static string TrimHover(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ").Replace("\r", "").Trim();
            return s.Length > max ? s.Substring(0, max) + "…" : s;
        }

        private void DrawBezierWire(Vector2 start, Vector2 end, Color color, float width = 3.5f, bool vertical = false)
        {
            // Wires sont également en espace monde; conversion unique au rendu.
            start = GraphPoint(start);
            end = GraphPoint(end);
            Vector2 startTan;
            Vector2 endTan;
            if (vertical)
            {
                float dy = end.y - start.y;
                float tangentDist = Mathf.Clamp(Mathf.Abs(dy) * 0.5f, 40f, 220f);
                startTan = start + new Vector2(0f, tangentDist);
                endTan = end - new Vector2(0f, -tangentDist);
            }
            else
            {
                float dx = end.x - start.x;
                float tangentDist = Mathf.Clamp(Mathf.Abs(dx) * 0.5f, 40f, 220f);
                startTan = start + new Vector2(tangentDist, 0f);
                endTan = end - new Vector2(tangentDist, 0f);
            }

            float approxLength = Vector2.Distance(start, startTan) + Vector2.Distance(startTan, endTan) + Vector2.Distance(endTan, end);
            float stepDist = _zoom < CardLodThreshold ? 5.5f : 2.0f;
            int minSteps = _zoom < CardLodThreshold ? 8 : 24;
            int maxSteps = _zoom < CardLodThreshold ? 45 : 180;
            int steps = Mathf.Clamp(Mathf.RoundToInt(approxLength / stepDist), minSteps, maxSteps);
            float dotSize = _zoom < CardLodThreshold ? Mathf.Max(2f, width * 0.75f) : width;

            Color prevCol = GUI.color;
            GUI.color = color;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = 1f - t;
                Vector2 p = (u * u * u) * start + (3f * u * u * t) * startTan + (3f * u * t * t) * endTan + (t * t * t) * end;
                GUI.DrawTexture(new Rect(p.x - dotSize * 0.5f, p.y - dotSize * 0.5f, dotSize, dotSize), PureWhiteTex);
            }

            GUI.color = prevCol;

            Vector2 mid = 0.125f * start + 0.375f * startTan + 0.375f * endTan + 0.125f * end;
            DrawSolidRect(new Rect(mid.x - 4f, mid.y - 4f, 8f, 8f), color);
        }

        private static void DrawSolidRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, PureWhiteTex);
            GUI.color = prev;
        }
    }
}
