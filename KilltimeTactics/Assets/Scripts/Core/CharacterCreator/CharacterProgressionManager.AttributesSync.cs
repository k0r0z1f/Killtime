using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    public partial class CharacterProgressionManager
    {
        // CharacterProgressionManager.AttributesSync.cs — Attributs, maintenance fiche (synchro, breakdown) + reset creation.

        public static int GetAttributeUpgradeCost(int currentVal)
        {
            if (currentVal < 1 || currentVal >= 10) return 0;
            return currentVal + 1;
        }

        public static List<SkillType> GetSkillsForAttribute(int attrIndex)
        {
            return attrIndex switch
            {
                0 => new List<SkillType> { SkillType.MainsNues, SkillType.ManiementArmes, SkillType.Athletisme, SkillType.DefenseCorporelle },
                1 => new List<SkillType> { SkillType.MainsNues, SkillType.ManiementArmes, SkillType.ArmesPercantes, SkillType.Ballistique, SkillType.Esquive, SkillType.Athletisme },
                2 => new List<SkillType> { SkillType.Athletisme, SkillType.DefenseCorporelle, SkillType.EndurancePhysique, SkillType.Cardio, SkillType.SystemeImmunitaire },
                3 => new List<SkillType> { SkillType.Esquive, SkillType.Athletisme },
                4 => new List<SkillType> { SkillType.TactiqueStrategie, SkillType.IngenierieArcanotech, SkillType.MedecineAvancee, SkillType.Academie, SkillType.Communication, SkillType.Intuition },
                5 => new List<SkillType> { SkillType.Academie, SkillType.MedecineAvancee, SkillType.IngenierieArcanotech },
                6 => new List<SkillType> { SkillType.Communication, SkillType.Intimidation, SkillType.Leadership },
                7 => new List<SkillType> { SkillType.Observation, SkillType.Intuition, SkillType.Artisanat },
                8 => new List<SkillType> { SkillType.MagiePrimale, SkillType.MagieElementale, SkillType.MagieEsprit },
                _ => new List<SkillType>()
            };
        }

        public static int GetSpendableForAttribute(CharacterSheet sheet, int attrIndex)
        {
            if (sheet == null) return 0;
            sheet.MigrateLegacyBluntSkill();
            var associated = GetSkillsForAttribute(attrIndex);

            int associatedXP = 0;
            int otherXP = 0;

            if (sheet.Skills != null)
            {
                for (int i = 0; i < sheet.Skills.Count; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null) continue;
                    int r = Math.Max(0, e.ReserveXP);
                    if (associated.Contains(e.Skill))
                        associatedXP += r;
                    else
                        otherXP += r;
                }
            }

            int free = Math.Max(0, sheet.AvailableXP);
            return associatedXP + free + (otherXP / OFF_SKILL_MULTIPLIER);
        }

        public static bool UpgradeAttribute(CharacterSheet sheet, int attrIndex, out string message)
        {
            message = null;
            if (sheet == null)
            {
                message = "Fiche invalide.";
                return false;
            }

            int currentVal = sheet.GetBaseAttributeValue(attrIndex);
            if (currentVal >= 10)
            {
                message = $"Plafond absolu de 10 déjà atteint pour cet attribut ({currentVal}/10).";
                return false;
            }

            int baseCost = GetAttributeUpgradeCost(currentVal);
            if (baseCost <= 0)
            {
                message = "Coût d'amélioration invalide.";
                return false;
            }

            sheet.MigrateLegacyBluntSkill();
            var associated = GetSkillsForAttribute(attrIndex);

            int need = baseCost;
            int takeAssociated = 0;
            int takeFree = 0;
            int takeOthers = 0;

            if (sheet.Skills != null)
            {
                for (int i = 0; i < sheet.Skills.Count && need > 0; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null || !associated.Contains(e.Skill)) continue;
                    int avail = Math.Max(0, e.ReserveXP);
                    int take = Math.Min(avail, need);
                    takeAssociated += take;
                    need -= take;
                }
            }

            int availFree = Math.Max(0, sheet.AvailableXP);
            takeFree = Math.Min(availFree, need);
            need -= takeFree;

            if (need > 0)
            {
                int needFromOthers = need * OFF_SKILL_MULTIPLIER;
                int availOthers = 0;
                if (sheet.Skills != null)
                {
                    for (int i = 0; i < sheet.Skills.Count; i++)
                    {
                        var e = sheet.Skills[i];
                        if (e == null || associated.Contains(e.Skill)) continue;
                        availOthers += Math.Max(0, e.ReserveXP);
                    }
                }

                if (availOthers < needFromOthers)
                {
                    int have = GetSpendableForAttribute(sheet, attrIndex);
                    string attrName = GetAttributeName(attrIndex);
                    message = $"XP insuffisant pour augmenter {attrName} de {currentVal} à {currentVal + 1} ({have}/{baseCost} XP mobilisables ; autres banques comptent x{OFF_SKILL_MULTIPLIER}).";
                    return false;
                }

                takeOthers = needFromOthers;
                need = 0;
            }

            int totalLevied = takeAssociated + takeFree + takeOthers;

            int restAssoc = takeAssociated;
            if (restAssoc > 0 && sheet.Skills != null)
            {
                for (int i = 0; i < sheet.Skills.Count && restAssoc > 0; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null || !associated.Contains(e.Skill)) continue;
                    int take = Math.Min(Math.Max(0, e.ReserveXP), restAssoc);
                    e.ReserveXP -= take;
                    restAssoc -= take;
                }
            }

            sheet.AvailableXP -= takeFree;

            int restOthers = takeOthers;
            if (restOthers > 0 && sheet.Skills != null)
            {
                for (int i = 0; i < sheet.Skills.Count && restOthers > 0; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null || associated.Contains(e.Skill)) continue;
                    int take = Math.Min(Math.Max(0, e.ReserveXP), restOthers);
                    e.ReserveXP -= take;
                    restOthers -= take;
                }
            }

            sheet.TotalSpentXP += totalLevied;

            int nextVal = currentVal + 1;
            sheet.SetBaseAttributeValue(attrIndex, nextVal);

            if (sheet.AttributeUpgradesPurchased == null || sheet.AttributeUpgradesPurchased.Length < 9)
            {
                var old = sheet.AttributeUpgradesPurchased;
                sheet.AttributeUpgradesPurchased = new int[9];
                if (old != null) Array.Copy(old, sheet.AttributeUpgradesPurchased, Math.Min(old.Length, 9));
            }
            sheet.AttributeUpgradesPurchased[attrIndex]++;

            string name = GetAttributeName(attrIndex);
            message = $"★ {name} augmenté à {nextVal} ! (Coût : {baseCost} XP payé via {totalLevied} XP prélevé(s)). XP Total : {sheet.TotalSpentXP}.";
            return true;
        }

        public static string GetAttributeName(int attrIndex)
        {
            return attrIndex switch
            {
                0 => "Force (FOR)",
                1 => "Agilité (AGI)",
                2 => "Constitution (CON)",
                3 => "Rapidité (RAP)",
                4 => "Intelligence (INT)",
                5 => "Érudition (ÉRU)",
                6 => "Charisme (CHA)",
                7 => "Instinct (INS)",
                8 => "Magie (5e Force)",
                _ => "Attribut"
            };
        }

        public static bool IsPresetStartingSpec(CharacterSheet sheet, string specName)
        {
            if (sheet == null || string.IsNullOrWhiteSpace(sheet.ActiveClassId) || string.IsNullOrWhiteSpace(specName))
                return false;
            try
            {
                var preset = Classes.CharacterClassCatalog.GetById(sheet.ActiveClassId);
                if (preset?.StartingSpecializations != null)
                {
                    return preset.StartingSpecializations.Contains(specName);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Synchronise et assainit l'état de progression de la fiche (Livre I §5) :
        /// - Impute les entraînements gratuits d'Érudition pour couvrir les niveaux d'entraînement existants.
        /// - Calcule le montant minimal d'XP réellement investi sur la fiche (niveaux payés,
        ///   spécialisations payantes, attributs en XP, sorts) et synchronise TotalSpentXP.
        /// </summary>
        public static void SynchronizeProgression(CharacterSheet sheet)
        {
            if (sheet == null) return;
            sheet.MigrateLegacyBluntSkill();

            int totalTrainings = sheet.GetTotalTrainingLevels();
            int freeBudget = sheet.GetFreeTrainingBudget();

            if (totalTrainings > 0 && freeBudget > 0)
            {
                sheet.FreeTrainingsUsed = Math.Clamp(Math.Max(sheet.FreeTrainingsUsed, Math.Min(totalTrainings, freeBudget)), 0, freeBudget);
            }
            else if (totalTrainings == 0)
            {
                sheet.FreeTrainingsUsed = 0;
            }

            int freeCovered = Math.Min(totalTrainings, sheet.FreeTrainingsUsed);
            int paidTrainings = Math.Max(0, totalTrainings - freeCovered);
            int paidTrainingsXP = paidTrainings * XP_COST_TRAINING;

            int paidSpecs = 0;
            if (sheet.UnlockedSpecializations != null)
            {
                for (int i = 0; i < sheet.UnlockedSpecializations.Count; i++)
                {
                    string spec = sheet.UnlockedSpecializations[i];
                    if (string.IsNullOrWhiteSpace(spec)) continue;

                    if (MinaCharacter.IsInnateUnlocked(spec, sheet)) continue;
                    if (LucasCharacter.IsInnateUnlocked(spec, sheet)) continue;
                    if (ThomasCharacter.IsInnateUnlocked(spec, sheet)) continue;
                    if (JohnCharacter.IsInnateUnlocked(spec, sheet)) continue;
                    if (ErikaCharacter.IsInnateUnlocked(spec, sheet)) continue;
                    if (VanceCharacter.IsInnateUnlocked(spec, sheet)) continue;
                    if (IsPresetStartingSpec(sheet, spec)) continue;

                    paidSpecs++;
                }
            }
            int paidSpecsXP = paidSpecs * XP_COST_SPECIALIZATION;

            int spellsXP = 0;
            if (sheet.LearnedSpells != null)
            {
                for (int i = 0; i < sheet.LearnedSpells.Count; i++)
                {
                    var sp = sheet.LearnedSpells[i];
                    if (sp != null) spellsXP += Math.Max(1, sp.CreationXpCost);
                }
            }

            int attributesXP = 0;
            if (sheet.AttributeUpgradesPurchased != null)
            {
                for (int i = 0; i < sheet.AttributeUpgradesPurchased.Length && i < 9; i++)
                {
                    int upgrades = sheet.AttributeUpgradesPurchased[i];
                    int currentVal = sheet.GetBaseAttributeValue(i);
                    for (int u = 0; u < upgrades; u++)
                    {
                        int rankAtUpgrade = currentVal - u;
                        attributesXP += Math.Max(1, rankAtUpgrade);
                    }
                }
            }

            int minSpentXP = paidTrainingsXP + paidSpecsXP + spellsXP + attributesXP;

            if (sheet.TotalSpentXP < minSpentXP)
            {
                sheet.TotalSpentXP = minSpentXP;
            }
        }

        public static string GetSpentXPBreakdownString(CharacterSheet sheet)
        {
            if (sheet == null) return "Fiche invalide";
            SynchronizeProgression(sheet);

            int totalTrainings = sheet.GetTotalTrainingLevels();
            int freeCovered = Math.Min(totalTrainings, sheet.FreeTrainingsUsed);
            int paidTrainings = Math.Max(0, totalTrainings - freeCovered);
            int paidTrainingsXP = paidTrainings * XP_COST_TRAINING;

            int paidSpecs = 0;
            int innateSpecs = 0;
            if (sheet.UnlockedSpecializations != null)
            {
                for (int i = 0; i < sheet.UnlockedSpecializations.Count; i++)
                {
                    string spec = sheet.UnlockedSpecializations[i];
                    if (string.IsNullOrWhiteSpace(spec)) continue;
                    if (MinaCharacter.IsInnateUnlocked(spec, sheet) || LucasCharacter.IsInnateUnlocked(spec, sheet) || ThomasCharacter.IsInnateUnlocked(spec, sheet) || JohnCharacter.IsInnateUnlocked(spec, sheet) || ErikaCharacter.IsInnateUnlocked(spec, sheet) || VanceCharacter.IsInnateUnlocked(spec, sheet) || IsPresetStartingSpec(sheet, spec))
                    {
                        innateSpecs++;
                        continue;
                    }
                    paidSpecs++;
                }
            }
            int paidSpecsXP = paidSpecs * XP_COST_SPECIALIZATION;

            int spellsXP = 0;
            int spellCount = 0;
            if (sheet.LearnedSpells != null)
            {
                for (int i = 0; i < sheet.LearnedSpells.Count; i++)
                {
                    var sp = sheet.LearnedSpells[i];
                    if (sp != null)
                    {
                        spellsXP += Math.Max(1, sp.CreationXpCost);
                        spellCount++;
                    }
                }
            }

            int attributesXP = 0;
            int attrUpgrades = 0;
            if (sheet.AttributeUpgradesPurchased != null)
            {
                for (int i = 0; i < sheet.AttributeUpgradesPurchased.Length && i < 9; i++)
                {
                    int upgrades = sheet.AttributeUpgradesPurchased[i];
                    attrUpgrades += upgrades;
                    int currentVal = sheet.GetBaseAttributeValue(i);
                    for (int u = 0; u < upgrades; u++)
                    {
                        int rankAtUpgrade = currentVal - u;
                        attributesXP += Math.Max(1, rankAtUpgrade);
                    }
                }
            }

            var parts = new List<string>();
            if (paidTrainingsXP > 0)
                parts.Add($"Entraînements : {paidTrainingsXP} XP ({paidTrainings} payé(s)" + (freeCovered > 0 ? $", {freeCovered} gratuit(s) ÉRU" : "") + ")");
            else if (freeCovered > 0)
                parts.Add($"Entraînements : 0 XP ({freeCovered} gratuit(s) ÉRU)");

            if (paidSpecsXP > 0)
                parts.Add($"Spécialisations : {paidSpecsXP} XP ({paidSpecs} payée(s)" + (innateSpecs > 0 ? $", {innateSpecs} innée(s)" : "") + ")");
            else if (innateSpecs > 0)
                parts.Add($"Spécialisations : 0 XP ({innateSpecs} innée(s))");

            if (attributesXP > 0)
                parts.Add($"Attributs : {attributesXP} XP ({attrUpgrades} palier(s))");

            if (spellsXP > 0)
                parts.Add($"Sorts : {spellsXP} XP ({spellCount} sort(s))");

            if (parts.Count == 0)
                return "0 XP dépensé (profil de départ)";

            return string.Join(" • ", parts) + $" = {sheet.TotalSpentXP} XP total";
        }


        // ------------------------------------------------------------------
        // RESET : remise à zéro de l'arbre de progression (création)
        // ------------------------------------------------------------------

        /// <summary>
        /// Remet à zéro l'arbre de progression du personnage (Livre I §5) :
        /// - tous les niveaux d'entraînement repassent à 0 (payants ET gratuits) ;
        /// - toutes les cases de progression sont effacées ;
        /// - tout l'XP PAYÉ est restauré en XP libre (remboursement au coût nominal) :
        ///   seuls les niveaux payés (entraînements), spécialisations et banques
        ///   liées non dépensées sont reversés dans <see cref="CharacterSheet.AvailableXP"/> ;
        /// - les entraînements GRATUITS d'Érudition ne sont jamais remboursés (0 XP dépensé,
        ///   hors XP total) : le compteur <c>FreeTrainingsUsed</c> est simplement remis à 0,
        ///   ce qui rend le budget à nouveau disponible ;
        /// - l'XP total (dépensé) est décrémenté des seuls montants payés, jamais en dessous de 0.
        /// Les sorts appris ne sont pas touchés sauf si <paramref name="includeSpells"/> est vrai
        /// (remboursement au coût de création nominal, sans surcoût croisé).
        /// Pensé pour la phase de création : permet de tout reprendre sans perdre d'XP payé.
        /// </summary>
        public static bool ResetProgressionTree(CharacterSheet sheet, out string message, bool includeSpells = false)
        {
            if (sheet == null)
            {
                message = "Fiche invalide.";
                return false;
            }

            int totalLevels = sheet.GetTotalTrainingLevels();
            int freeUsed = Math.Max(0, sheet.FreeTrainingsUsed);
            // Les gratuits sont imputés en premier : seuls les niveaux payés sont remboursés.
            int paidLevels = Math.Max(0, totalLevels - Math.Min(freeUsed, totalLevels));
            int freedSlots = Math.Min(freeUsed, totalLevels);
            int refundedTrainings = paidLevels * XP_COST_TRAINING;
            int refundedSpecs = 0;
            int refundedReserves = 0;
            int presetFreeSpecs = 0;

            // Package de création (archétype de classe) : les entraînements du preset
            // sont déjà comptés comme gratuits (FreeTrainingsUsed = total preset, voir
            // CharacterClassDefinition.ApplyToSheet) donc jamais remboursés ci-dessus.
            // Les spécialisations du preset sont gratuites aussi : on ne rembourse que
            // celles hors preset pour éviter tout farm XP (charger → reset → encaisser).
            if (!string.IsNullOrWhiteSpace(sheet.ActiveClassId))
            {
                try
                {
                    var preset = Classes.CharacterClassCatalog.GetById(sheet.ActiveClassId);
                    if (preset?.StartingSpecializations != null && sheet.UnlockedSpecializations != null)
                    {
                        for (int i = 0; i < preset.StartingSpecializations.Count; i++)
                        {
                            string s = preset.StartingSpecializations[i];
                            if (!string.IsNullOrWhiteSpace(s) && sheet.UnlockedSpecializations.Contains(s))
                                presetFreeSpecs++;
                        }
                    }
                }
                catch { presetFreeSpecs = 0; }
            }

            if (sheet.Skills != null)
            {
                for (int i = 0; i < sheet.Skills.Count; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null) continue;
                    e.TrainingLevel = 0;
                    e.ProgressTicks = 0;
                    if (e.ReserveXP > 0)
                    {
                        refundedReserves += e.ReserveXP;
                        e.ReserveXP = 0;
                    }
                    if (e.ProgressTicks < 0) e.ProgressTicks = 0;
                    if (e.ReserveXP < 0) e.ReserveXP = 0;
                }
            }
            sheet.FreeTrainingsUsed = 0;

            int freeSpecs = presetFreeSpecs;
            if (sheet.UnlockedSpecializations != null)
            {
                for (int i = 0; i < sheet.UnlockedSpecializations.Count; i++)
                {
                    string s = sheet.UnlockedSpecializations[i];
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (MinaCharacter.IsInnateUnlocked(s, sheet) || LucasCharacter.IsInnateUnlocked(s, sheet) || ThomasCharacter.IsInnateUnlocked(s, sheet) || JohnCharacter.IsInnateUnlocked(s, sheet) || ErikaCharacter.IsInnateUnlocked(s, sheet) || VanceCharacter.IsInnateUnlocked(s, sheet))
                        freeSpecs++;
                }
            }

            if (sheet.UnlockedSpecializations != null && sheet.UnlockedSpecializations.Count > 0)
            {
                int paidSpecs = Math.Max(0, sheet.UnlockedSpecializations.Count - Math.Max(0, freeSpecs));
                refundedSpecs = paidSpecs * XP_COST_SPECIALIZATION;
                sheet.UnlockedSpecializations.Clear();

                if (IsMina(sheet)) sheet.UnlockedSpecializations.Add(MinaCharacter.InnateSpecialization);
                if (IsLucas(sheet)) sheet.UnlockedSpecializations.Add(LucasCharacter.InnateSpecialization);
                if (IsThomas(sheet)) sheet.UnlockedSpecializations.Add(ThomasCharacter.InnateSpecialization);
                if (IsJohn(sheet)) sheet.UnlockedSpecializations.Add(JohnCharacter.InnateSpecialization);
                if (IsErika(sheet)) sheet.UnlockedSpecializations.Add(ErikaCharacter.InnateSpecialization);
                if (IsVance(sheet)) sheet.UnlockedSpecializations.Add(VanceCharacter.InnateSpecialization);
            }

            int refundedSpells = 0;
            if (includeSpells && sheet.LearnedSpells != null && sheet.LearnedSpells.Count > 0)
            {
                for (int i = 0; i < sheet.LearnedSpells.Count; i++)
                {
                    var sp = sheet.LearnedSpells[i];
                    if (sp != null) refundedSpells += Math.Max(1, sp.CreationXpCost);
                }
                sheet.LearnedSpells.Clear();
            }

            int refundedAttributes = 0;
            if (sheet.AttributeUpgradesPurchased != null && sheet.AttributeUpgradesPurchased.Length >= 9)
            {
                for (int i = 0; i < 9; i++)
                {
                    int count = sheet.AttributeUpgradesPurchased[i];
                    while (count > 0)
                    {
                        int currentVal = sheet.GetBaseAttributeValue(i);
                        refundedAttributes += currentVal;
                        sheet.SetBaseAttributeValue(i, Math.Max(1, currentVal - 1));
                        count--;
                    }
                    sheet.AttributeUpgradesPurchased[i] = 0;
                }
            }

            int spentRefund = refundedTrainings + refundedSpecs + refundedSpells + refundedAttributes;
            int totalRefund = spentRefund + refundedReserves;

            sheet.AvailableXP = Math.Max(0, sheet.AvailableXP) + totalRefund;
            sheet.TotalSpentXP = Math.Max(0, sheet.TotalSpentXP - spentRefund);

            message = $"Arbre de progression réinitialisé : entraînements à 0, cases effacées, {totalRefund} XP restaurés en XP libre " +
                      $"(entraînements payés {refundedTrainings} + spés {refundedSpecs}" +
                      (includeSpells ? $" + sorts {refundedSpells}" : "") +
                      (refundedAttributes > 0 ? $" + attributs {refundedAttributes}" : "") +
                      $" + banques liées {refundedReserves}). {freedSlots} entraînement(s) gratuit(s) rendus au budget Érudition. XP total (dépensé) : {sheet.TotalSpentXP}.";
            return true;
        }

    }
}
