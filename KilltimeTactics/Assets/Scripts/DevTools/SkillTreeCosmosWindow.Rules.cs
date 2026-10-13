using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Character.Classes;
using Killtime.Core.Dice;
using Killtime.Tactics.Units;

namespace Killtime.UI
{
    public partial class SkillTreeCosmosWindow : FloatingWindow<SkillTreeCosmosWindow>
    {
        private void SaveSheet()
        {
            if (_sheet == null) return;
            CharacterStorageService.SaveCharacter(_sheet);
            if (_boundUnit != null)
            {
                _boundUnit.NotifyInventoryChanged(saveToDisk: false);
            }
        }

        private bool IsNodeUnlocked(CosmosNode node)
        {
            if (_sheet == null || node == null) return false;
            if (node.IsSpecialization || node.IsImprovement)
            {
                // Maîtrises innées des fiches héroïques (voir MinaCharacter / LucasCharacter / JohnCharacter / ErikaCharacter / VanceCharacter).
                if (MinaCharacter.IsInnateUnlocked(node.SpecializationName, _sheet))
                    return true;
                if (LucasCharacter.IsInnateUnlocked(node.SpecializationName, _sheet))
                    return true;
                if (JohnCharacter.IsInnateUnlocked(node.SpecializationName, _sheet))
                    return true;
                if (ErikaCharacter.IsInnateUnlocked(node.SpecializationName, _sheet))
                    return true;
                if (VanceCharacter.IsInnateUnlocked(node.SpecializationName, _sheet))
                    return true;

                return _sheet.UnlockedSpecializations != null &&
                       _sheet.UnlockedSpecializations.Contains(node.SpecializationName);
            }
            return _sheet.GetSkill(node.Skill).TrainingLevel > 0;
        }

        private int GetSkillTrainingLevel(CosmosNode node)
        {
            if (_sheet == null || node.IsSpecialization) return 0;
            return _sheet.GetSkill(node.Skill).TrainingLevel;
        }

        private string GetSkillDieString(SkillType skill)
        {
            if (_sheet == null) return "D4";
            int baseRank = SkillDefinitions.GetBaseRank(skill, _sheet.GetEffectiveAttributes(), false);
            int training = _sheet.GetSkill(skill).TrainingLevel;
            var die = SkillDefinitions.DieFromTotalSteps(SkillDefinitions.CharacteristicSteps(baseRank) + training);
            return DiceTypeHints.GetShortLabel(die);
        }

        private static string TrainingPips(int training)
        {
            int full = Mathf.Clamp(training, 0, CharacterProgressionManager.MAX_SKILL_TRAINING);
            return new string('●', full) + new string('○', CharacterProgressionManager.MAX_SKILL_TRAINING - full);
        }
    }
}
