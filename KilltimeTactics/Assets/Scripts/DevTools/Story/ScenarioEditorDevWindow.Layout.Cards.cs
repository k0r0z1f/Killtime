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

        // Layout intra-noeud : disposition des cartes + spot libre (ex-Layout.cs, split sans perte).

        private void LayoutCardsInsideNode(SceneNodeData node)
        {
            if (node == null) return;
            float cardStartX = node.GraphPosX + 20f;
            float cardStartY = node.GraphPosY + 48f;
            const int maxCardsPerRow = 3;
            const float gapCardsX = 18f;
            const float gapCardsY = 18f;

            float curY = cardStartY;
            int totalDialogues = node.Dialogues != null ? node.Dialogues.Count : 0;

            for (int rowStart = 0; rowStart < totalDialogues; rowStart += maxCardsPerRow)
            {
                int rowEnd = Mathf.Min(rowStart + maxCardsPerRow, totalDialogues);
                float rowHeight = 0f;
                for (int d = rowStart; d < rowEnd; d++)
                {
                    Vector2 s = GetDialogueCardSize(node.Dialogues[d]);
                    rowHeight = Mathf.Max(rowHeight, s.y);
                }

                for (int d = rowStart; d < rowEnd; d++)
                {
                    int col = d - rowStart;
                    node.Dialogues[d].GraphPosX = cardStartX + col * (DlgCardDefaultW + gapCardsX);
                    node.Dialogues[d].GraphPosY = curY;
                }

                curY += rowHeight + gapCardsY;
            }

            int totalEvents = node.Events != null ? node.Events.Count : 0;
            float curEvtY = curY;
            if (totalEvents > 0)
            {
                float evtStartY = (totalDialogues > 0) ? curY + 10f : cardStartY;
                curEvtY = evtStartY;

                for (int rowStart = 0; rowStart < totalEvents; rowStart += maxCardsPerRow)
                {
                    int rowEnd = Mathf.Min(rowStart + maxCardsPerRow, totalEvents);
                    float rowHeight = 0f;
                    for (int e = rowStart; e < rowEnd; e++)
                    {
                        Vector2 s = GetEventCardSize(node.Events[e]);
                        rowHeight = Mathf.Max(rowHeight, s.y);
                    }

                    for (int e = rowStart; e < rowEnd; e++)
                    {
                        int col = e - rowStart;
                        node.Events[e].GraphPosX = cardStartX + col * (EvtCardDefaultW + gapCardsX);
                        node.Events[e].GraphPosY = curEvtY;
                    }

                    curEvtY += rowHeight + 14f;
                }
            }

            int totalCons = node.Consequences != null ? node.Consequences.Count : 0;
            if (totalCons > 0)
            {
                float consStartY = (totalDialogues > 0 || totalEvents > 0) ? curEvtY + 10f : cardStartY;
                float curConsY = consStartY;

                for (int rowStart = 0; rowStart < totalCons; rowStart += maxCardsPerRow)
                {
                    int rowEnd = Mathf.Min(rowStart + maxCardsPerRow, totalCons);
                    float rowHeight = 0f;
                    for (int k = rowStart; k < rowEnd; k++)
                    {
                        Vector2 s = GetConsequenceCardSize(node.Consequences[k]);
                        rowHeight = Mathf.Max(rowHeight, s.y);
                    }

                    for (int k = rowStart; k < rowEnd; k++)
                    {
                        int col = k - rowStart;
                        node.Consequences[k].GraphPosX = cardStartX + col * (ConsCardDefaultW + gapCardsX);
                        node.Consequences[k].GraphPosY = curConsY;
                    }

                    curConsY += rowHeight + 14f;
                }
            }
        }
        private void ComputeFreeSpotForNewCard(SceneNodeData node, out float x, out float y)
        {
            float baseX = (node != null ? node.GraphPosX : 60f) + 20f;
            float baseY = (node != null ? node.GraphPosY : 60f) + 48f;
            if (node == null)
            {
                x = baseX;
                y = baseY;
                return;
            }

            bool has = false;
            float minLeft = float.MaxValue;
            float maxBottom = float.MinValue;

            if (node.Dialogues != null)
            {
                for (int i = 0; i < node.Dialogues.Count; i++)
                {
                    var d = node.Dialogues[i];
                    if (d == null) continue;
                    Vector2 s = GetDialogueCardSize(d);
                    minLeft = Mathf.Min(minLeft, d.GraphPosX);
                    maxBottom = Mathf.Max(maxBottom, d.GraphPosY + s.y);
                    has = true;
                }
            }
            if (node.Events != null)
            {
                for (int i = 0; i < node.Events.Count; i++)
                {
                    var e = node.Events[i];
                    if (e == null) continue;
                    Vector2 s = GetEventCardSize(e);
                    minLeft = Mathf.Min(minLeft, e.GraphPosX);
                    maxBottom = Mathf.Max(maxBottom, e.GraphPosY + s.y);
                    has = true;
                }
            }
            if (node.Consequences != null)
            {
                for (int i = 0; i < node.Consequences.Count; i++)
                {
                    var k = node.Consequences[i];
                    if (k == null) continue;
                    Vector2 s = GetConsequenceCardSize(k);
                    minLeft = Mathf.Min(minLeft, k.GraphPosX);
                    maxBottom = Mathf.Max(maxBottom, k.GraphPosY + s.y);
                    has = true;
                }
            }

            if (!has)
            {
                x = baseX;
                y = baseY;
                return;
            }

            x = (minLeft != float.MaxValue) ? minLeft : baseX;
            y = maxBottom + 18f;
        }

        private void AutoLayoutNodeGraph(SceneNodeData node)
        {
            float curX = 60f;
            float curY = 80f;
            for (int i = 0; i < node.Dialogues.Count; i++)
            {
                node.Dialogues[i].GraphPosX = curX;
                node.Dialogues[i].GraphPosY = curY;
                Vector2 s = GetDialogueCardSize(node.Dialogues[i]);
                curX += Mathf.Max(370f, s.x + 35f);
                if ((i + 1) % 3 == 0)
                {
                    curX = 60f;
                    curY += 290f;
                }
            }

            curX = 60f;
            curY += 340f;
            for (int e = 0; e < node.Events.Count; e++)
            {
                node.Events[e].GraphPosX = curX;
                node.Events[e].GraphPosY = curY;
                Vector2 s = GetEventCardSize(node.Events[e]);
                curX += Mathf.Max(370f, s.x + 35f);
            }
        }
    }
}
