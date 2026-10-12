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
        // ================= TAB 3 : FORGE DE SORTS (LIVRE IV) =================
        private void DrawSpellForgeTab()
        {
            GUILayout.Label("<b>1. Presets Officiels du Codex (Livre IV §19.3) :</b>");
            GUILayout.BeginHorizontal(GUI.skin.box);
            if (GUILayout.Button("📖 Charger 'Ralentissement Gravitationnel' (3 XP / 3 PA)"))
            {
                var preset = ArcanotechWorkshop.CreateRalentissementGravitationnel();
                LoadSpellIntoForge(preset);
                _statusMessage = "Preset 'Ralentissement Gravitationnel' chargé dans l'Atelier.";
            }
            if (GUILayout.Button("📖 Charger 'Écrasement de Matière' (3 XP / 3 PA)"))
            {
                var preset = ArcanotechWorkshop.CreateEcrasementDeMatiere();
                LoadSpellIntoForge(preset);
                _statusMessage = "Preset 'Écrasement de Matière' chargé dans l'Atelier.";
            }
            if (GUILayout.Button("↺ Vider", GUILayout.Width(70)))
            {
                _spellModuleRanks.Clear();
                _customSpellName = "Nouveau Pouvoir";
                _customSpellDesc = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>2. Fiche Technique du Pouvoir (Livre IV §19.1) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom :", GUILayout.Width(100));
            _customSpellName = GUILayout.TextField(_customSpellName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Description :", GUILayout.Width(100));
            _customSpellDesc = GUILayout.TextField(_customSpellDesc);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Discipline :", GUILayout.Width(100));
            _customSpellDiscipline = (PsychicDiscipline)GUILayout.Toolbar((int)_customSpellDiscipline, Enum.GetNames(typeof(PsychicDiscipline)));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Stade Neuronal :", GUILayout.Width(100));
            int stageIdx = (int)_customSpellStage - 1;
            int newStageIdx = GUILayout.Toolbar(stageIdx, new[] { "Stade 1", "Stade 2", "Stade 3", "Stade 4", "Stade 5", "Stade 6" });
            _customSpellStage = (PsychicStage)(newStageIdx + 1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Compétence :", GUILayout.Width(100));
            int skillIdx = _customSpellSkill switch
            {
                SkillType.MagieElementale => 0,
                SkillType.MagiePrimale => 1,
                _ => 2
            };
            int newSkillIdx = GUILayout.Toolbar(skillIdx, new[] { "Magie Élémentale", "Magie Primale", "Magie de l'Esprit" });
            _customSpellSkill = newSkillIdx switch
            {
                0 => SkillType.MagieElementale,
                1 => SkillType.MagiePrimale,
                _ => SkillType.MagieEsprit
            };
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            var candidateSpell = BuildCandidateSpell();
            var activeCats = candidateSpell.GetActiveCategories();
            string catList = activeCats.Count > 0 ? string.Join(", ", activeCats) : "Aucune";

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>3. Catalogue Modulaire des Effets Achetables (Livre IV §19.2) :</b>");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Solde : <color=yellow><b>{_currentSheet.AvailableXP} XP Libre</b></color>");
            GUILayout.EndHorizontal();

            _spellForgeModulesScroll = GUILayout.BeginScrollView(_spellForgeModulesScroll, GUILayout.Height(210));
            DrawModuleCatalogGroup(PowerCategory.Offensif, "━━━ EFFETS OFFENSIFS (Attaques & Neutralisation) ━━━", "#FF4444");
            DrawModuleCatalogGroup(PowerCategory.Defensif, "━━━ EFFETS DÉFENSIFS (Protection & Résistance) ━━━", "#4488FF");
            DrawModuleCatalogGroup(PowerCategory.Utilitaire, "━━━ EFFETS UTILITAIRES & ÉTATS BÉNÉFIQUES ━━━", "#00FFAA");
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            GUILayout.Label("<b>4. Bilan Énergétique & Règle d'Or (XP = PA) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUI.color = Color.cyan;
            GUILayout.Label($"⚡ Coût d'Activation Combat : <b>{candidateSpell.ActionPointCost} PA</b>", GUILayout.Width(240));
            GUI.color = Color.yellow;
            GUILayout.Label($"🔮 Coût de Création : <b>{candidateSpell.CreationXpCost} XP</b>", GUILayout.Width(220));
            GUI.color = candidateSpell.HybridXpPenalty > 0 ? new Color(1f, 0.4f, 0.4f) : Color.gray;
            GUILayout.Label($"Pénalité Hybride : <b>+{candidateSpell.HybridXpPenalty} XP</b> ({activeCats.Count} cat.)", GUILayout.ExpandWidth(true));
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            int magicAttr = _currentSheet.BaseAttributes.Magie;
            int training = _currentSheet.GetSkill(_customSpellSkill)?.TrainingLevel ?? 0;
            int baseRange = NythariteSpell.CalculateBaseRange(magicAttr, training);
            GUILayout.Label($"<color=#94A3B8>Portée nominale : <b>{baseRange} cases</b> (MAG {magicAttr} + Entraînement {training}) | Catégories : <b>{catList}</b></color>");

            GUILayout.Space(4);
            bool canAfford = _currentSheet.AvailableXP >= candidateSpell.CreationXpCost && candidateSpell.ActionPointCost > 0;
            GUI.enabled = canAfford;
            GUI.backgroundColor = canAfford ? new Color(0.2f, 0.85f, 0.45f) : Color.gray;

            if (GUILayout.Button($"🔮 Forger & Apprendre '{candidateSpell.Name}' ({candidateSpell.CreationXpCost} XP ➔ {candidateSpell.ActionPointCost} PA)", GUILayout.Height(34)))
            {
                if (ArcanotechWorkshop.LearnSpell(_currentSheet, candidateSpell, out string msg))
                {
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = msg;
                }
                else
                {
                    _statusMessage = msg;
                }
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label($"<b>5. Sorts Mémorisés sur la Fiche ({_currentSheet.LearnedSpells.Count}) :</b>");

            if (_currentSheet.LearnedSpells.Count == 0)
            {
                GUILayout.Label("<color=gray><i>Aucun sort mémorisé. Forgez un sort modulaire ou chargez un preset ci-dessus.</i></color>");
            }

            for (int i = 0; i < _currentSheet.LearnedSpells.Count; i++)
            {
                var sp = _currentSheet.LearnedSpells[i];
                if (sp == null) continue;

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();

                int spTraining = _currentSheet.GetSkill(sp.AssociatedSkill)?.TrainingLevel ?? 0;
                int spRange = NythariteSpell.CalculateBaseRange(_currentSheet.BaseAttributes.Magie, spTraining);

                GUILayout.Label($"🔮 <b>{sp.Name}</b> <color=#00E5FF>[{sp.ActionPointCost} PA // {sp.CreationXpCost} XP]</color> — <i>{sp.Discipline} ({sp.Stage})</i>", GUILayout.ExpandWidth(true));

                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("✕ Oublier", GUILayout.Width(75), GUILayout.Height(20)))
                {
                    _currentSheet.LearnedSpells.RemoveAt(i);
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = $"Sort '{sp.Name}' oublié.";
                    break;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();

                var modSummary = new List<string>();
                if (sp.Modules != null)
                {
                    for (int m = 0; m < sp.Modules.Count; m++)
                    {
                        var sel = sp.Modules[m];
                        var def = ArcanotechWorkshop.GetModuleDefinition(sel.ModuleId);
                        if (def != null) modSummary.Add($"{def.DisplayName} x{sel.Rank}");
                    }
                }
                string modText = modSummary.Count > 0 ? string.Join(", ", modSummary) : "Aucun module";

                GUILayout.Label($"<color=#7090A0>Portée: <b>{spRange} cases</b> | Dégâts bruts: <b>{sp.BaseArcaneDamage}</b> | Effets: <i>{modText}</i></color>");
                if (!string.IsNullOrEmpty(sp.Description))
                {
                    GUILayout.Label($"<color=#94A3B8><i>« {sp.Description} »</i></color>");
                }
                GUILayout.EndVertical();
            }
        }
        private void DrawModuleCatalogGroup(PowerCategory category, string header, string hexColor)
        {
            GUILayout.Space(4);
            GUILayout.Label($"<color={hexColor}><b>{header}</b></color>");

            foreach (var def in ArcanotechWorkshop.GetAllModuleDefinitions())
            {
                if (def.Category != category) continue;

                _spellModuleRanks.TryGetValue(def.Id, out int currentRank);

                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>{def.DisplayName}</b> <color=#94A3B8>({def.UnitPointCost} pt{(def.UnitPointCost > 1 ? "s" : "")})</color>\n<size=10><color=#7090A0>{def.Description}</color></size>", GUILayout.ExpandWidth(true));

                if (def.IsStackable)
                {
                    GUI.enabled = currentRank > 0;
                    if (GUILayout.Button("-", GUILayout.Width(26), GUILayout.Height(22)))
                    {
                        _spellModuleRanks[def.Id] = Mathf.Max(0, currentRank - 1);
                    }
                    GUI.enabled = true;

                    GUILayout.Label($"<b>{currentRank}</b>", GUILayout.Width(22));

                    if (GUILayout.Button("+", GUILayout.Width(26), GUILayout.Height(22)))
                    {
                        _spellModuleRanks[def.Id] = currentRank + 1;
                    }
                }
                else
                {
                    bool active = currentRank > 0;
                    GUI.backgroundColor = active ? new Color(0.1f, 0.75f, 0.4f) : Color.white;
                    if (GUILayout.Button(active ? "✓ Actif" : "Ajouter", GUILayout.Width(70), GUILayout.Height(22)))
                    {
                        _spellModuleRanks[def.Id] = active ? 0 : 1;
                    }
                    GUI.backgroundColor = Color.white;
                }

                GUILayout.EndHorizontal();
            }
        }

        private void LoadSpellIntoForge(NythariteSpell spell)
        {
            _forge.LoadSpellIntoForge(spell);
        }

        private NythariteSpell BuildCandidateSpell()
        {
            return _forge.BuildCandidateSpell();
        }
    }
}
