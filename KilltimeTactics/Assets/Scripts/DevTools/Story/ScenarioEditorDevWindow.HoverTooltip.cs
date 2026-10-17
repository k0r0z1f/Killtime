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

        private void DrawCardHoverTooltip(Rect canvasRect)
        {
            if (string.IsNullOrEmpty(_hoverBody) && string.IsNullOrEmpty(_hoverTitle)) return;
            if (_hoverBodyStyle == null)
            {
                _hoverBodyStyle = new GUIStyle(GUI.skin.label);
                _hoverBodyStyle.wordWrap = true;
                _hoverBodyStyle.richText = true;
            }

            float tipW = Mathf.Min(360f, canvasRect.width - 20f);
            if (tipW < 160f) return;
            float bodyH = _hoverBodyStyle.CalcHeight(new GUIContent(_hoverBody), tipW - 16f);
            bodyH = Mathf.Min(bodyH, 220f);
            float tipH = 26f + bodyH + 10f;

            Vector2 anchor = _hoverScreenPos + new Vector2(18f, 20f);
            if (anchor.x + tipW > canvasRect.xMax - 6f) anchor.x = canvasRect.xMax - 6f - tipW;
            if (anchor.y + tipH > canvasRect.yMax - 6f) anchor.y = _hoverScreenPos.y - 8f - tipH;
            if (anchor.x < canvasRect.x + 6f) anchor.x = canvasRect.x + 6f;
            if (anchor.y < canvasRect.y + 6f) anchor.y = canvasRect.y + 6f;
            Rect tip = new Rect(anchor.x, anchor.y, tipW, tipH);

            DrawSolidRect(tip, new Color(0.03f, 0.05f, 0.08f, 0.96f));
            Color prev = GUI.color;
            GUI.color = new Color(0.0f, 0.85f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(tip.x, tip.y, tip.width, 2f), PureWhiteTex);
            GUI.color = prev;
            GUI.Label(new Rect(tip.x + 8f, tip.y + 5f, tip.width - 16f, 20f), $"<b>{_hoverTitle}</b>");
            GUI.Label(new Rect(tip.x + 8f, tip.y + 26f, tip.width - 16f, bodyH), _hoverBody, _hoverBodyStyle);
        }
    }
}
