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
        /// <summary>État guide d'un nœud spé/amélioration (NotInBuild si aucun guide).</summary>
        private CharacterClassDefinition.GuideStepState GetGuideSpecState(CosmosNode node)
        {
            if (node == null || (!node.IsSpecialization && !node.IsImprovement)) return CharacterClassDefinition.GuideStepState.NotInBuild;
            var guide = _frameGuide ?? CharacterClassCatalog.GetActiveGuide(_sheet);
            if (guide == null) return CharacterClassDefinition.GuideStepState.NotInBuild;
            return guide.GetSpecState(_sheet, node.SpecializationName);
        }

        /// <summary>Vrai si le nœud compétence a encore des entraînements cibles dans le guide.</summary>
        private bool IsGuideNextSkill(CosmosNode node)
        {
            if (node == null || node.IsSpecialization || node.IsImprovement) return false;
            var guide = _frameGuide ?? CharacterClassCatalog.GetActiveGuide(_sheet);
            if (guide == null) return false;
            return guide.IsSkillNext(_sheet, node.Skill);
        }

        /// <summary>Suffixe guide dans le footer : rappelle si le nœud est la prochaine étape du build.</summary>
        private string GetGuideFooterSuffix(CosmosNode node)
        {
            if (node == null || _sheet == null) return "";
            var guide = _frameGuide ?? CharacterClassCatalog.GetActiveGuide(_sheet);
            if (guide == null) return "";
            if (node.IsSpecialization || node.IsImprovement)
            {
                var st = guide.GetSpecState(_sheet, node.SpecializationName);
                if (st == CharacterClassDefinition.GuideStepState.Next)
                    return $" <color=#FFD166><b>◆ Prochaine étape de votre build « {guide.DisplayName} » — arbre libre, mais c'est la suite recommandée.</b></color>";
                if (st == CharacterClassDefinition.GuideStepState.Future)
                    return $" <color=#8A6D2B>◇ Étape future du build « {guide.DisplayName} » (débloquez d'abord le parent).</color>";
                if (st == CharacterClassDefinition.GuideStepState.Acquired)
                    return $" <color=#00FF88>★ Étape de votre build « {guide.DisplayName} » déjà acquise.</color>";
                return "";
            }
            else
            {
                if (guide.IsSkillNext(_sheet, node.Skill))
                {
                    int tgt = guide.GetTargetTraining(node.Skill);
                    int cur = _sheet.GetSkill(node.Skill).TrainingLevel;
                    return $" <color=#FFD166><b>◆ Entraînement recommandé : {cur}/+{tgt} pour le build « {guide.DisplayName} ».</b></color>";
                }
                return "";
            }
        }

        /// <summary>
        /// Suffixe d'en-tête : caractéristiques associées pour une compétence,
        /// compétence source pour une spécialisation / amélioration.
        /// </summary>
        private static string GetCaracsHeaderSuffix(CosmosNode node)
        {
            if (node == null) return "";
            string caracs = SkillDefinitions.GetAssociatedAttributeNames(node.Skill);
            if (!node.IsSpecialization && !node.IsImprovement)
                return $" <color=#FFD27F>[{caracs}]</color>";
            return $" <color=#94A3B8>(Source : {SkillDefinitions.GetDisplayName(node.Skill)} [{caracs}])</color>";
        }

        /// <summary>
        /// Description enrichie : pour une compétence avec fiche liée, ajoute le
        /// rang de base calculé sur les attributs effectifs (ex « (FOR 4+AGI 6)/2=5 »),
        /// plus la variante offensive éveillée (Corps Augmenté, « MAG 5 ★ »).
        /// </summary>
        private string GetInspectionDescription(CosmosNode node)
        {
            if (node == null) return "";
            string desc = node.Description ?? "";
            if ((node.IsSpecialization || node.IsImprovement) || _sheet == null) return desc;
            var eff = _sheet.GetEffectiveAttributes();
            desc += $" — <color=#00E5FF>Base : {SkillDefinitions.DescribeBaseRank(node.Skill, eff, false)}</color>";
            if (SkillDefinitions.UsesMagicAugmentation(node.Skill, eff, true))
                desc += $" — <color=#F0ABFC>Offensif éveillé : {SkillDefinitions.DescribeBaseRank(node.Skill, eff, true)}</color>";
            return desc;
        }
    }
}
