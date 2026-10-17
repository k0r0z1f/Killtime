using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    public partial class CharacterProgressionManager
    {
        // CharacterProgressionManager.Specializations.Unlock.cs — UnlockSpecialization et LearnModularSpell.

        /// <summary>
        /// Débloque une spécialisation ou une amélioration de spécialisation (5 XP).
        /// Tarif normal si payé depuis la réserve de la compétence source (ou XP libre), doublé sinon.
        /// </summary>
        public static bool UnlockSpecialization(CharacterSheet sheet, string specializationName, out string message, SkillType? associatedSkill = null, bool forceFree = false)
        {
            if (sheet == null || string.IsNullOrWhiteSpace(specializationName))
            {
                message = "Spécialisation invalide.";
                return false;
            }
            if (sheet.UnlockedSpecializations.Contains(specializationName))
            {
                message = "Spécialisation déjà acquise.";
                return false;
            }
            sheet.MigrateLegacyBluntSkill();

            SkillType target = associatedSkill ?? (TryGetSpecializationSource(specializationName, out SkillType s) ? s : (SkillType?)null) ?? SkillType.Academie;

            if (IsMina(sheet) && MinaCharacter.IsSkillForbidden(target))
            {
                message = MinaCharacter.ForbiddenSkillMessage(target);
                return false;
            }

            if (IsLucas(sheet) && LucasCharacter.IsSkillForbidden(target))
            {
                message = LucasCharacter.ForbiddenSkillMessage(target);
                return false;
            }

            if (IsThomas(sheet) && ThomasCharacter.IsSkillForbidden(target))
            {
                message = ThomasCharacter.ForbiddenSkillMessage(target);
                return false;
            }

            if (IsJohn(sheet) && JohnCharacter.IsSkillForbidden(target))
            {
                message = JohnCharacter.ForbiddenSkillMessage(target);
                return false;
            }

            if (IsErika(sheet) && ErikaCharacter.IsSkillForbidden(target))
            {
                message = ErikaCharacter.ForbiddenSkillMessage(target);
                return false;
            }

            if (IsVance(sheet) && VanceCharacter.IsSkillForbidden(target))
            {
                message = VanceCharacter.ForbiddenSkillMessage(target);
                return false;
            }

            if (!IsMina(sheet) && IsMinaExclusiveSpecialization(specializationName))
            {
                message = MinaCharacter.ExclusiveSpecializationMessage(specializationName);
                return false;
            }

            if (!IsLucas(sheet) && IsLucasExclusiveSpecialization(specializationName))
            {
                message = LucasCharacter.ExclusiveSpecializationMessage(specializationName);
                return false;
            }

            if (!IsThomas(sheet) && IsThomasExclusiveSpecialization(specializationName))
            {
                message = ThomasCharacter.ExclusiveSpecializationMessage(specializationName);
                return false;
            }

            if (!IsJohn(sheet) && IsJohnExclusiveSpecialization(specializationName))
            {
                message = JohnCharacter.ExclusiveSpecializationMessage(specializationName);
                return false;
            }

            if (!IsErika(sheet) && IsErikaExclusiveSpecialization(specializationName))
            {
                message = ErikaCharacter.ExclusiveSpecializationMessage(specializationName);
                return false;
            }

            if (!IsVance(sheet) && IsVanceExclusiveSpecialization(specializationName))
            {
                message = VanceCharacter.ExclusiveSpecializationMessage(specializationName);
                return false;
            }

            if (forceFree || MinaCharacter.IsInnateUnlocked(specializationName, sheet))
            {
                sheet.UnlockedSpecializations.Add(specializationName);
                message = specializationName.Equals(MinaCharacter.InnateSpecialization, StringComparison.OrdinalIgnoreCase)
                    ? $"★ Maîtrise innée [{MinaCharacter.InnateSpecialization}] synchronisée sans dépense d'XP !"
                    : $"[CHEAT] Maîtrise '{specializationName}' débloquée immédiatement sans coût !";
                return true;
            }

            if (forceFree || LucasCharacter.IsInnateUnlocked(specializationName, sheet))
            {
                sheet.UnlockedSpecializations.Add(specializationName);
                message = $"★ Maîtrise innée [{LucasCharacter.InnateSpecialization}] synchronisée sans dépense d'XP !";
                return true;
            }

            if (forceFree || ThomasCharacter.IsInnateUnlocked(specializationName, sheet))
            {
                sheet.UnlockedSpecializations.Add(specializationName);
                message = $"★ Maîtrise innée [{ThomasCharacter.InnateSpecialization}] synchronisée sans dépense d'XP !";
                return true;
            }

            if (forceFree || JohnCharacter.IsInnateUnlocked(specializationName, sheet))
            {
                sheet.UnlockedSpecializations.Add(specializationName);
                message = $"★ Maîtrise innée [{JohnCharacter.InnateSpecialization}] synchronisée sans dépense d'XP !";
                return true;
            }

            if (forceFree || ErikaCharacter.IsInnateUnlocked(specializationName, sheet))
            {
                sheet.UnlockedSpecializations.Add(specializationName);
                message = $"★ Maîtrise innée [{ErikaCharacter.InnateSpecialization}] synchronisée sans dépense d'XP !";
                return true;
            }

            if (forceFree || VanceCharacter.IsInnateUnlocked(specializationName, sheet))
            {
                sheet.UnlockedSpecializations.Add(specializationName);
                message = $"★ Maîtrise innée [{VanceCharacter.InnateSpecialization}] synchronisée sans dépense d'XP !";
                return true;
            }

            if (SpecializationPrerequisites.TryGetValue(specializationName, out string reqPre))
            {
                if (!sheet.UnlockedSpecializations.Contains(reqPre))
                {
                    message = $"Prérequis non satisfait : requiert la spécialisation parente '{reqPre}'.";
                    return false;
                }
            }
            else if (TryGetParentSpecialization(specializationName, out string parentSpec))
            {
                if (!sheet.UnlockedSpecializations.Contains(parentSpec))
                {
                    message = $"Prérequis non satisfait : requiert la spécialisation parente '{parentSpec}'.";
                    return false;
                }
            }

            if (IsVolume2Specialization(specializationName))
            {
                if (!IsVolume2Unlocked())
                {
                    message = $"[VERROU CAUSAL] '{specializationName}' requiert l'éveil du Volume II (Flag : FLAG_MINALIA_AWAKENED).";
                    return false;
                }
            }

            if (!SpendForSkill(sheet, target, XP_COST_SPECIALIZATION, out string funding))
            {
                string src = SkillDefinitions.GetDisplayName(target);
                message = $"XP insuffisant : {funding} (coût {XP_COST_SPECIALIZATION} XP liés {src} ou libre, {XP_COST_SPECIALIZATION * OFF_SKILL_MULTIPLIER} si croisé).";
                return false;
            }

            sheet.UnlockedSpecializations.Add(specializationName);
            message = $"Spécialisation '{specializationName}' débloquée (source {SkillDefinitions.GetDisplayName(target)}) ! [{funding}]";
            return true;
        }

        /// <summary>
        /// Apprend un sort conçu dans l'Atelier Arcanotech (Règle : Coût XP = Coût PA).
        /// Affinité par défaut : Magie Élémentale. Payé depuis une autre voie → coût doublé.
        /// </summary>
        public static bool LearnModularSpell(CharacterSheet sheet, NythariteSpell spell, out string message, SkillType? affinitySkill = null)
        {
            if (sheet == null || spell == null)
            {
                message = "Sort invalide.";
                return false;
            }
            SkillType target = affinitySkill ?? SkillType.MagieElementale;
            int cost = Math.Max(1, spell.CreationXpCost);

            if (IsLucas(sheet) && target == SkillType.MagiePrimale)
            {
                message = LucasCharacter.ForbiddenSpellMessage();
                return false;
            }

            if (IsErika(sheet) && (target == SkillType.MagiePrimale || target == SkillType.MagieEsprit))
            {
                message = ErikaCharacter.ForbiddenSpellMessage();
                return false;
            }

            if (IsThomas(sheet))
            {
                message = "[RESTRICTION D'ÂME] Thomas ne possède aucune affinité arcanique (MAG 0) et ne peut canaliser ni graver aucun sort modulaire.";
                return false;
            }

            if (!SpendForSkill(sheet, target, cost, out string funding))
            {
                message = $"XP insuffisant : {funding} pour canaliser {spell.Name} (coût {cost} XP liés {SkillDefinitions.GetDisplayName(target)} ou libre, {cost * OFF_SKILL_MULTIPLIER} si croisé).";
                return false;
            }

            sheet.LearnedSpells.Add(spell);
            message = $"Sort '{spell.Name}' gravé dans les voies neurales ! (Coût Combat : {spell.ActionPointCost} PA). [{funding}]";
            return true;
        }

    }
}
