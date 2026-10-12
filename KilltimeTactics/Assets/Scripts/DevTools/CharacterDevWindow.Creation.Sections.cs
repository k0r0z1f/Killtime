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
        private void DrawIdentitySection()
        {
            GUILayout.Label("<b>1. Identité & Profil (Livre I) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom :", GUILayout.Width(90));
            _currentSheet.Name = GUILayout.TextField(_currentSheet.Name);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Âge / Sexe :", GUILayout.Width(90));
            string ageStr = GUILayout.TextField(_currentSheet.Age.ToString(), GUILayout.Width(50));
            if (int.TryParse(ageStr, out int a)) _currentSheet.Age = a;
            int genderIndex = GenderToIndex(_currentSheet.Gender);
            int newGenderIndex = GUILayout.Toolbar(genderIndex, new[] { "Masculin", "Féminin", "Autre" });
            if (newGenderIndex != genderIndex)
            {
                _currentSheet.Gender = new[] { "Masculin", "Féminin", "Autre" }[Mathf.Clamp(newGenderIndex, 0, 2)];
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Espèce :", GUILayout.Width(90));
            _currentSheet.Species = (SpeciesType)GUILayout.Toolbar((int)_currentSheet.Species, Enum.GetNames(typeof(SpeciesType)));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Profil :", GUILayout.Width(90));
            var prevProfile = _currentSheet.Profile;
            _currentSheet.Profile = (CharacterProfileType)GUILayout.Toolbar((int)_currentSheet.Profile, Enum.GetNames(typeof(CharacterProfileType)));
            if (_currentSheet.Profile != prevProfile)
            {
                ApplyDefaultProfileAttributes(_currentSheet);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Gabarit :", GUILayout.Width(90));
            _currentSheet.Footprint = (TitanFootprintType)GUILayout.Toolbar((int)_currentSheet.Footprint, new[] { "Normal (1x1)", "Titan (Rosette 7)", "Triangle (3)", "Colosse (19)" });
            GUILayout.EndHorizontal();

            if (_currentSheet.Footprint != TitanFootprintType.Single)
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
                int cellCount = TitanFootprint.GetOccupiedCoordinates(new HexCoordinates(0, 0), _currentSheet.Footprint).Count;
                GUILayout.Label($"⚠️ <b>Entité Colossale (Livre XI) :</b> Empreinte de {cellCount} hexagones. Nécessite une zone dégagée lors du spawn.", GUI.skin.box);
                GUI.color = Color.white;
            }

            DrawModelSelector();

            GUILayout.EndVertical();
        }

        private void DrawAttributesSection()
        {
            bool isAllocationValid = ValidateAttributeAllocation(_currentSheet, out string allocationStatus);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>2. Les 8 Attributs Fondamentaux (Plafond Absolu : 10) :</b>");
            GUILayout.FlexibleSpace();
            GUI.color = isAllocationValid ? Color.green : new Color(1.0f, 0.4f, 0.4f);
            GUILayout.Label(allocationStatus, GUI.skin.box);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(285));
            GUILayout.Label("<b>🐣 CRÉATION INITIALE (Budget Départ)</b>");
            GUILayout.Label("<color=#94A3B8><size=10>Piliers et points secondaires selon le profil.</size></color>");
            GUILayout.Space(4);

            _currentSheet.BaseAttributes.Force = DrawAttrRow("Force (FOR)", _currentSheet.BaseAttributes.Force, 0);
            _currentSheet.BaseAttributes.Agilite = DrawAttrRow("Agilité (AGI)", _currentSheet.BaseAttributes.Agilite, 1);
            _currentSheet.BaseAttributes.Constitution = DrawAttrRow("Constitution (CON)", _currentSheet.BaseAttributes.Constitution, 2);
            _currentSheet.BaseAttributes.Rapidite = DrawAttrRow("Rapidité (RAP)", _currentSheet.BaseAttributes.Rapidite, 3);
            _currentSheet.BaseAttributes.Intelligence = DrawAttrRow("Intelligence (INT)", _currentSheet.BaseAttributes.Intelligence, 4);
            _currentSheet.BaseAttributes.Erudition = DrawAttrRow("Érudition (ÉRU)", _currentSheet.BaseAttributes.Erudition, 5);
            _currentSheet.BaseAttributes.Charisme = DrawAttrRow("Charisme (CHA)", _currentSheet.BaseAttributes.Charisme, 6);
            _currentSheet.BaseAttributes.Instinct = DrawAttrRow("Instinct (INS)", _currentSheet.BaseAttributes.Instinct, 7);
            _currentSheet.BaseAttributes.Magie = DrawAttrRow("Magie (5e Force)", _currentSheet.BaseAttributes.Magie, 8);

            GUILayout.EndVertical();

            GUILayout.Space(6);

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>⚡ ÉVOLUTION EN JEU (Achat XP)</b>");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"<color=yellow><b>{_currentSheet.AvailableXP} XP Libre</b></color>");
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=#94A3B8><size=10>Coût : passer de N à N+1 = (N+1) XP (ex. 4➔5 = 5 XP, 9➔10 = 10 XP).</size></color>");
            GUILayout.Space(4);

            DrawAttrXpUpgradeRow("Force (FOR)", 0);
            DrawAttrXpUpgradeRow("Agilité (AGI)", 1);
            DrawAttrXpUpgradeRow("Constitution (CON)", 2);
            DrawAttrXpUpgradeRow("Rapidité (RAP)", 3);
            DrawAttrXpUpgradeRow("Intelligence (INT)", 4);
            DrawAttrXpUpgradeRow("Érudition (ÉRU)", 5);
            DrawAttrXpUpgradeRow("Charisme (CHA)", 6);
            DrawAttrXpUpgradeRow("Instinct (INS)", 7);
            DrawAttrXpUpgradeRow("Magie (5e Force)", 8);

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawVitalsSection()
        {
            var effective = _currentSheet.GetEffectiveAttributes();
            GUILayout.Space(6);
            GUILayout.Label("<b>3. Métriques Vitales Dérivées (Temps Réel) :</b>");
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUI.color = Color.cyan;
            GUILayout.Label($"⚡ PA : <b>{effective.CalculateBaseActionPoints()}</b>");
            GUI.color = Color.green;
            GUILayout.Label($"🛡️ Encaissement : <b>{effective.CalculateEncaissement()}</b>");
            GUI.color = Color.red;
            GUILayout.Label($"💀 Seuil Mort : <b>{effective.CalculateLethalMaximum()} PV</b>");
            GUI.color = Color.yellow;
            GUILayout.Label($"🫁 Souffle : <b>{effective.Constitution} tours</b>");
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }

        private void DrawProgressionSummarySection()
        {
            GUILayout.Space(6);
            GUILayout.Label("<b>4. Arbre de Progression (Livre I §5) :</b>");
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"Entraînements : <b>{CountTrainedLevels(_currentSheet)}</b> (gratuits <b>{Mathf.Max(0, _currentSheet.FreeTrainingsUsed)}/{_currentSheet.GetFreeTrainingBudget()}</b>, restants <b>{_currentSheet.GetFreeTrainingsRemaining()}</b>) | Spés : <b>{_currentSheet.UnlockedSpecializations.Count}</b> | Banques liées : <b>{_currentSheet.GetLinkedReserveTotal()} XP</b> | XP libre : <b>{_currentSheet.AvailableXP}</b> | <color=#FFD166>XP Total : <b>{_currentSheet.TotalSpentXP} XP</b></color>", GUILayout.ExpandWidth(true));
            GUI.enabled = _currentSheet.AvailableXP > 0;
            if (GUILayout.Button("↺ XP libre à 0", GUILayout.Width(110), GUILayout.Height(28)))
            {
                if (CharacterProgressionManager.ResetFreeXP(_currentSheet, out string freeMsg, out _))
                    _statusMessage = freeMsg;
                else
                    _statusMessage = freeMsg;
            }
            GUI.enabled = true;
            GUI.backgroundColor = new Color(0.95f, 0.45f, 0.2f);
            if (GUILayout.Button("♻️ Reset arbre (XP restaurés, entraînements à 0)", GUILayout.Width(320), GUILayout.Height(28)))
            {
                if (CharacterProgressionManager.ResetProgressionTree(_currentSheet, out string resetMsg))
                    _statusMessage = resetMsg;
                else
                    _statusMessage = resetMsg;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Label("<i>Érudition = entraînements gratuits (budget = ÉRU effective, +1 ÉRU = +1 gratuit). Gratuits : 0 XP, hors XP total. Appliquez-les dans l'onglet Progression (bouton Gratuit).</i>");
        }
    }
}
