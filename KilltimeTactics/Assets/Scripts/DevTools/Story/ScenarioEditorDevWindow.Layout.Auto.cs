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

        // Auto-dispositions globales : scene 2 colonnes + mosaique (ex-Layout.cs, split sans perte).

        private void AutoLayoutGlobalScene(Rect? canvasRect = null)
        {
            bool hasNodes = _data.Nodes.Count > 0;
            bool hasGlobals = (_data.Triggers != null && _data.Triggers.Count > 0)
                || (_data.Interactables != null && _data.Interactables.Count > 0)
                || (_data.Actors != null && _data.Actors.Count > 0)
                || (_data.Cinematics != null && _data.Cinematics.Count > 0);
            if (!hasNodes && !hasGlobals) return;

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                EnsureDialogueGraphIntegrity(node);
                StorySceneData.EnsureNodeLists(node);
            }

            // Normalisation immédiate de tous les dialogues internes en 3 colonnes régulières
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                LayoutCardsInsideNode(_data.Nodes[n]);
            }

            const float startX = 60f;
            const float startY = 60f;
            const float gapX = 50f;
            const float gapY = 28f;

            float maxSceneH = 0f;
            if (hasNodes)
            {
                // Placement structuré en flux 2D compact
                var rootNode = _data.Nodes[0];
                rootNode.GraphPosX = startX;
                rootNode.GraphPosY = startY;
                LayoutCardsInsideNode(rootNode);
                Rect rootBox = ComputeNodeBoundingBox(rootNode);

                float col2X = rootBox.xMax + gapX;
                float col2CurY = startY;
                maxSceneH = rootBox.height;

                for (int n = 1; n < _data.Nodes.Count; n++)
                {
                    var node = _data.Nodes[n];
                    node.GraphPosX = col2X;
                    node.GraphPosY = col2CurY;
                    LayoutCardsInsideNode(node);
                    Rect b = ComputeNodeBoundingBox(node);

                    col2CurY += b.height + gapY;
                    maxSceneH = Mathf.Max(maxSceneH, col2CurY - startY);
                }
            }

            if (_data.Triggers != null && _data.Triggers.Count > 0)
            {
                float trigX = startX;
                float trigY = startY + maxSceneH + 36f;
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    trg.GraphPosX = trigX;
                    trg.GraphPosY = trigY;
                    Vector2 s = GetTriggerCardSize(trg);
                    trigX += s.x + 20f;
                }
                // Les cartes Interactables vivent sur la rangée sous les déclencheurs.
                if (_data.Interactables != null && _data.Interactables.Count > 0)
                {
                    GetSceneContentBounds(out float _, out float _, out float _, out float trigMaxY);
                    float interX = startX;
                    float interY = trigMaxY + 30f;
                    for (int i = 0; i < _data.Interactables.Count; i++)
                    {
                        var it = _data.Interactables[i];
                        if (it == null) continue;
                        it.GraphPosX = interX;
                        it.GraphPosY = interY;
                        Vector2 s = GetInteractableCardSize(it);
                        interX += s.x + 20f;
                    }
                }
            }
            else if (_data.Interactables != null && _data.Interactables.Count > 0)
            {
                float interX = startX;
                float interY = startY + maxSceneH + 36f;
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    it.GraphPosX = interX;
                    it.GraphPosY = interY;
                    Vector2 s = GetInteractableCardSize(it);
                    interX += s.x + 20f;
                }
            }
            // Les cartes Acteurs vivent sur la rangée sous les interactables.
            if (_data.Actors != null && _data.Actors.Count > 0)
            {
                float actorY = startY + maxSceneH + 36f;
                if ((_data.Triggers != null && _data.Triggers.Count > 0) || (_data.Interactables != null && _data.Interactables.Count > 0))
                {
                    GetSceneContentBounds(out float _, out float _, out float _, out float placedMaxY);
                    actorY = placedMaxY + 30f;
                }
                float actorX = startX;
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    a.GraphPosX = actorX;
                    a.GraphPosY = actorY;
                    Vector2 s = GetActorCardSize(a);
                    actorX += s.x + 20f;
                }
            }
            // Les cartes Cinématiques vivent sur la rangée sous les acteurs (entrée seule, sortie 🎬).
            if (_data.Cinematics != null && _data.Cinematics.Count > 0)
            {
                float cineY = startY + maxSceneH + 36f;
                if ((_data.Triggers != null && _data.Triggers.Count > 0) || (_data.Interactables != null && _data.Interactables.Count > 0) || (_data.Actors != null && _data.Actors.Count > 0))
                {
                    GetSceneContentBounds(out float _, out float _, out float _, out float placedMaxYCine);
                    cineY = placedMaxYCine + 30f;
                }
                float cineX = startX;
                for (int i = 0; i < _data.Cinematics.Count; i++)
                {
                    var c = _data.Cinematics[i];
                    if (c == null) continue;
                    c.GraphPosX = cineX;
                    c.GraphPosY = cineY;
                    Vector2 s = GetCinematicCardSize(c);
                    cineX += s.x + 20f;
                }
            }

            if (_data.Sounds != null && _data.Sounds.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float placedMaxYSnd);
                float sndX = startX;
                float sndY = placedMaxYSnd + 30f;
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) continue;
                    snd.GraphPosX = sndX;
                    snd.GraphPosY = sndY;
                    Vector2 s = GetSoundCardSize(snd);
                    sndX += s.x + 20f;
                }
            }

            if (_data.Ambiences != null && _data.Ambiences.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float placedMaxYAmb);
                float ambX = startX;
                float ambY = placedMaxYAmb + 30f;
                for (int i = 0; i < _data.Ambiences.Count; i++)
                {
                    var amb = _data.Ambiences[i];
                    if (amb == null) continue;
                    amb.GraphPosX = ambX;
                    amb.GraphPosY = ambY;
                    Vector2 s = GetAmbienceCardSize(amb);
                    ambX += s.x + 20f;
                }
            }

            _focusedNodeIndex = 0;
            FocusAllNodes(canvasRect);
        }

        private void AutoLayoutMosaic(Rect? canvasRect = null)
        {
            bool hasNodes = _data.Nodes.Count > 0;
            bool hasGlobals = (_data.Triggers != null && _data.Triggers.Count > 0)
                || (_data.Interactables != null && _data.Interactables.Count > 0)
                || (_data.Actors != null && _data.Actors.Count > 0)
                || (_data.Cinematics != null && _data.Cinematics.Count > 0);
            if (!hasNodes && !hasGlobals) return;
            Vector2 view = ResolveViewSize(canvasRect);
            float maxRowWidth = Mathf.Max(1800f, view.x / Mathf.Clamp(_zoom, 0.35f, 1f));
            const float gapX = 50f;
            const float gapY = 36f;
            const float startX = 60f;
            const float startY = 60f;

            float curX = startX;
            float curY = startY;
            float currentRowHeight = 0f;
            var placedBoxes = new List<Rect>();

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                EnsureDialogueGraphIntegrity(node);
                StorySceneData.EnsureNodeLists(node);

                node.GraphPosX = curX;
                node.GraphPosY = curY;
                LayoutCardsInsideNode(node);

                Rect box = ComputeNodeBoundingBox(node);
                if (curX + box.width > startX + maxRowWidth && curX > startX)
                {
                    curX = startX;
                    curY += currentRowHeight + gapY;
                    currentRowHeight = 0f;

                    node.GraphPosX = curX;
                    node.GraphPosY = curY;
                    LayoutCardsInsideNode(node);
                    box = ComputeNodeBoundingBox(node);
                }

                Rect candidate = new Rect(curX, curY, box.width, box.height);
                bool collision;
                int guard = 0;
                do
                {
                    collision = false;
                    for (int p = 0; p < placedBoxes.Count && guard < 64; p++)
                    {
                        if (placedBoxes[p].Overlaps(candidate))
                        {
                            candidate.y = placedBoxes[p].yMax + gapY;
                            collision = true;
                            guard++;
                            break;
                        }
                    }
                } while (collision);

                node.GraphPosX = candidate.x;
                node.GraphPosY = candidate.y;
                LayoutCardsInsideNode(node);
                box = ComputeNodeBoundingBox(node);

                placedBoxes.Add(box);
                curX += box.width + gapX;
                currentRowHeight = Mathf.Max(currentRowHeight, box.height);
            }

            if (_data.Triggers != null && _data.Triggers.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY);
                float trigX = startX;
                float trigY = maxY + 50f;
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    trg.GraphPosX = trigX;
                    trg.GraphPosY = trigY;
                    Vector2 s = GetTriggerCardSize(trg);
                    trigX += s.x + 30f;
                }
            }
            if (_data.Interactables != null && _data.Interactables.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY2);
                float interX = startX;
                float interY = maxY2 + 50f;
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    it.GraphPosX = interX;
                    it.GraphPosY = interY;
                    Vector2 s = GetInteractableCardSize(it);
                    interX += s.x + 30f;
                }
            }
            if (_data.Actors != null && _data.Actors.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY3);
                float actorX = startX;
                float actorY = maxY3 + 50f;
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    a.GraphPosX = actorX;
                    a.GraphPosY = actorY;
                    Vector2 s = GetActorCardSize(a);
                    actorX += s.x + 30f;
                }
            }
            if (_data.Cinematics != null && _data.Cinematics.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY4);
                float cineX = startX;
                float cineY = maxY4 + 50f;
                for (int i = 0; i < _data.Cinematics.Count; i++)
                {
                    var c = _data.Cinematics[i];
                    if (c == null) continue;
                    c.GraphPosX = cineX;
                    c.GraphPosY = cineY;
                    Vector2 s = GetCinematicCardSize(c);
                    cineX += s.x + 30f;
                }
            }

            if (_data.Sounds != null && _data.Sounds.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY5);
                float sndX = startX;
                float sndY = maxY5 + 50f;
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) continue;
                    snd.GraphPosX = sndX;
                    snd.GraphPosY = sndY;
                    Vector2 s = GetSoundCardSize(snd);
                    sndX += s.x + 30f;
                }
            }

            _focusedNodeIndex = 0;
            FocusAllNodes(canvasRect);
        }
    }
}
