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
        private void DrawCineLinkRow(ref float y, float x, float w, List<string> list)
        {
            GraphLabel(new Rect(x, y, 26f, 18f), "🎬");
            bool has = list != null && list.Count > 0;
            GUI.color = has ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            GraphLabel(new Rect(x + 28f, y, Mathf.Max(40f, w - 28f - 26f), 18f),
                has ? StorySceneData.GetCineSummary(list) : "(drag port → 🎬)");
            GUI.color = Color.white;
            if (has && GraphButton(new Rect(x + w - 22f, y, 22f, 18f), "⊘")) list.Clear();
            y += CineRowH;
        }
        private void DrawCineOutPort(Vector2 centerWorld, List<string> list, Vector2 mouseWorld, SceneCinematicData cineOwner = null)
        {
            bool has = cineOwner != null
                ? !string.IsNullOrWhiteSpace(cineOwner.NextTargetId)
                : (list != null && list.Count > 0);
            Color col = has ? CinePortCol : new Color(CinePortCol.r, CinePortCol.g, CinePortCol.b, 0.35f);
            Rect port = new Rect(centerWorld.x - 7f, centerWorld.y - 7f, 14f, 14f);
            DrawGraphSolidRect(port, col);
            if (cineOwner == null && list == null) return;
            if (GraphPrimaryDown(mouseWorld, port))
            {
                if (cineOwner != null)
                {
                    _wireDraft = new WireConnectionDraft
                    {
                        IsActive = true,
                        IsCinematicOut = true,
                        SourceCinematic = cineOwner,
                        StartPos = centerWorld
                    };
                }
                else
                {
                    _wireDraft = new WireConnectionDraft
                    {
                        IsActive = true,
                        IsCinematicLink = true,
                        SourceCineList = list,
                        StartPos = centerWorld
                    };
                }
                Event.current.Use();
            }
        }
        private static Vector2 GetCinematicOutCenter(SceneCinematicData cine)
        {
            Vector2 s = GetCinematicCardSize(cine);
            return new Vector2(cine.GraphPosX + s.x, cine.GraphPosY + CinePortTopCards);
        }
        private void DrawCineWireList(Vector2 from, List<string> ids)
        {
            if (ids == null || ids.Count == 0) return;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(ids[i])) continue;
                if (_cachedCinematicInputSockets.TryGetValue(ids[i], out var targetIn))
                    DrawBezierWire(from, targetIn, new Color(1f, 0.35f, 0.85f, 0.95f), 3f);
            }
        }

    }
}
