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
        private void DrawEntryInputPort(Rect cardRect, Color normalColor, bool isEntry, bool entryError)
        {
            if (!isEntry)
            {
                DrawGraphSolidRect(new Rect(cardRect.x - 7f, cardRect.y + 19f, 14f, 14f), normalColor);
                return;
            }
            if (entryError)
                DrawGraphSolidRect(new Rect(cardRect.x - 12f, cardRect.y + 14f, 24f, 24f), ExitRed);
            DrawGraphSolidRect(new Rect(cardRect.x - 10f, cardRect.y + 16f, 20f, 20f), EntryGreen);
        }
        private static string ActorTalkSocketKey(string actorId, int entryIndex)
        {
            return $"{actorId}#{entryIndex}";
        }
        private static void SetActorTalkTarget(SceneActorSpawnData a, int entryIndex, string targetId)
        {
            if (a?.TalkEntries == null || entryIndex < 0 || entryIndex >= a.TalkEntries.Count) return;
            var te = a.TalkEntries[entryIndex];
            if (te == null) return;
            te.TargetId = targetId ?? "";
        }
        private static Vector2 GetActorTalkOutCenter(SceneActorSpawnData a, int entryIndex)
        {
            Vector2 s = GetActorCardSize(a);
            return new Vector2(a.GraphPosX + s.x, a.GraphPosY + ActorTalkFirstRowTop + entryIndex * ActorTalkRowH + 9f);
        }
        private static Vector2 GetNodeMainOutCenter(Rect nodeBox)
        {
            return new Vector2(nodeBox.xMax, nodeBox.yMin + 18f);
        }

        private static Vector2 GetNodeChoiceOutCenter(Rect nodeBox, int choiceIndex)
        {
            return new Vector2(nodeBox.xMax, nodeBox.yMin + 60f + (choiceIndex * 36f) + 14f);
        }

        private static Rect GetNodeChoicePortRect(Rect nodeBox, int choiceIndex)
        {
            Vector2 c = GetNodeChoiceOutCenter(nodeBox, choiceIndex);
            return new Rect(c.x - 7f, c.y - 7f, 14f, 14f);
        }

        private static bool IsOverNodeChoicePort(Rect nodeBox, SceneNodeData node, Vector2 mouseWorld)
        {
            if (node == null || node.Choices == null) return false;
            for (int c = 0; c < node.Choices.Count; c++)
            {
                if (node.Choices[c] == null) continue;
                if (GetNodeChoicePortRect(nodeBox, c).Contains(mouseWorld)) return true;
            }
            return false;
        }

        private int FindNodeIndex(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || _data == null || _data.Nodes == null) return -1;
            return _data.Nodes.FindIndex(n => n != null && string.Equals(n.NodeId, nodeId, System.StringComparison.OrdinalIgnoreCase));
        }
        private struct OffscreenLinkTag
        {
            public string TargetNodeId;
            public int TargetNodeIndex;
            public string Label;
            public Color Color;
            public Vector2 WorldPos;
            public Rect ScreenRect;
        }

    }
}
