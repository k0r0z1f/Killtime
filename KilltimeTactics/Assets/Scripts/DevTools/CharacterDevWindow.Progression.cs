using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Killtime.Core.Character;
using Killtime.Core.Character.Classes;
using Killtime.Core.Arcanotech;
using Killtime.Core.Inventory;
using Killtime.DevTools.Character;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    public partial class CharacterDevWindow
    {
        // ================= TAB 1 : PROGRESSION =================
        private void DrawProgressionTab()
        {
            DrawProgressionHeader();

            var activeGuide = CharacterClassCatalog.GetActiveGuide(_currentSheet);
            DrawSkillTreesSection(activeGuide);

            GUILayout.Space(8);

            DrawSpecsCheatBar();
            DrawSpecializationsSection(activeGuide);
        }
    }
}
