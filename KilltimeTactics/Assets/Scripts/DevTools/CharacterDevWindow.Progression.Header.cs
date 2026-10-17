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
        private void DrawProgressionHeader()
        {
            CharacterProgressionManager.SynchronizeProgression(_currentSheet);

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"Personnage : <b>{_currentSheet.Name}</b> | XP Libre : <color=yellow><b>{_currentSheet.AvailableXP} XP</b></color> | Banque liée : <b>{_currentSheet.GetLinkedReserveTotal()} XP</b> | <color=#FFD166>XP TOTAL (dépensé) : <b>{_currentSheet.TotalSpentXP} XP</b></color>");
            if (GUILayout.Button("+10 XP", GUILayout.Width(70))) CharacterProgressionManager.GrantXP(_currentSheet, 10);
            if (GUILayout.Button("+50 XP", GUILayout.Width(70))) CharacterProgressionManager.GrantXP(_currentSheet, 50);
            GUI.enabled = _currentSheet.AvailableXP > 0;
            if (GUILayout.Button("↺ XP libre à 0", GUILayout.Width(110)))
            {
                if (CharacterProgressionManager.ResetFreeXP(_currentSheet, out string freeMsg, out _))
                    _statusMessage = freeMsg;
                else
                    _statusMessage = freeMsg;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#94A3B8><size=11>📊 <b>Ventilation XP Total :</b> {CharacterProgressionManager.GetSpentXPBreakdownString(_currentSheet)}</size></color>");
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("<i>Reset : restaure l'XP payé en libre, gratuits rendus au budget, entraînements à 0, cases effacées.</i>", GUILayout.ExpandWidth(true));
            GUI.backgroundColor = new Color(0.95f, 0.45f, 0.2f);
            if (GUILayout.Button("♻️ Reset arbre", GUILayout.Width(130)))
            {
                if (CharacterProgressionManager.ResetProgressionTree(_currentSheet, out string resetMsg))
                    _statusMessage = resetMsg;
                else
                    _statusMessage = resetMsg;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Label($"<i>Gratuits Érudition : <b>{_currentSheet.GetFreeTrainingsRemaining()}</b> restant(s) (budget <b>{_currentSheet.GetFreeTrainingBudget()}</b> = ÉRU effective, {Mathf.Max(0, _currentSheet.FreeTrainingsUsed)} consommé(s)). +1 ÉRU = +1 gratuit. Gratuits : 0 XP, hors XP total.</i>");
            GUILayout.Label("<i>Livre I §5 : 1 réussite = 1 case dans la compétence. Piste pleine (cases = base) = +1 XP lié. Dépense associée/libre au coût normal, croisée x2. XP total = dépensé (payé), jamais la banque ni les gratuits.</i>");
        }
    }
}
