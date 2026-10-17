using System;
using Killtime.Core.Character;

namespace Killtime.DevTools.Character
{
    /// <summary>
    /// Règles pures de répartition des attributs (Livre I).
    /// Extrait à l'identique de CharacterDevWindow — aucune logique modifiée,
    /// uniquement déplacée pour testabilité (zéro dépendance UnityEngine).
    /// </summary>
    public static class AttributeRulesService
    {
        public static bool CanDecrease(CharacterSheet sheet, int attrIndex)
        {
            if (sheet == null) return false;
            if (attrIndex == 8)
            {
                return sheet.BaseAttributes.Magie > 0;
            }

            var b = sheet.BaseAttributes;
            int[] core = { b.Force, b.Agilite, b.Constitution, b.Rapidite, b.Intelligence, b.Erudition, b.Charisme, b.Instinct };
            if (attrIndex >= 0 && attrIndex < 8)
            {
                return core[attrIndex] > 1;
            }

            return false;
        }

        public static bool CanIncrease(CharacterSheet sheet, int attrIndex)
        {
            if (sheet == null) return false;
            var b = sheet.BaseAttributes;
            int[] core = { b.Force, b.Agilite, b.Constitution, b.Rapidite, b.Intelligence, b.Erudition, b.Charisme, b.Instinct };

            if (attrIndex == 8)
            {
                if (b.Magie >= 10) return false;
                if (sheet.Profile == CharacterProfileType.PnjSbire && b.Magie >= 3) return false;
                if (sheet.Profile == CharacterProfileType.HerosPJ && b.Magie >= 5) return false;
                if (sheet.Profile == CharacterProfileType.PnjNormal && b.Magie >= 4) return false;
                return true;
            }

            if (attrIndex < 0 || attrIndex >= 8) return false;
            if (core[attrIndex] >= 10) return false;

            int[] candidate = (int[])core.Clone();
            candidate[attrIndex]++;

            return IsCandidateAllocationValid(candidate, sheet.Profile, b.Magie);
        }

        public static bool IsCandidateAllocationValid(int[] candidate, CharacterProfileType profile, int magie)
        {
            for (int i = 0; i < 8; i++)
            {
                if (candidate[i] > 10 || candidate[i] < 1) return false;
            }

            if (profile == CharacterProfileType.PnjBoss)
            {
                return true;
            }

            if (profile == CharacterProfileType.PnjSbire)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (candidate[i] > 3) return false;
                }

                int maxSbirePoints = magie > 0 ? 11 : 12;
                int currentTotal = 0;
                for (int i = 0; i < 8; i++) currentTotal += candidate[i];

                return currentTotal <= maxSbirePoints;
            }

            if (profile == CharacterProfileType.HerosPJ)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (candidate[i] > 5) return false;
                }

                int count5 = 0;
                int countGte4 = 0;
                int count4 = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (candidate[i] == 5) count5++;
                    if (candidate[i] >= 4) countGte4++;
                    if (candidate[i] == 4) count4++;
                }

                if (count5 > 1) return false;
                if (countGte4 > 2) return false;
                if (count5 == 1 && count4 > 1) return false;

                int maxTotal = magie > 0 ? 26 : 24;
                int sum = 0;
                for (int i = 0; i < 8; i++) sum += candidate[i];
                if (sum > maxTotal) return false;

                int[] sorted = (int[])candidate.Clone();
                Array.Sort(sorted);
                Array.Reverse(sorted);

                int secondarySum = 0;
                for (int i = 2; i < 8; i++)
                {
                    if (sorted[i] > 3) return false;
                    secondarySum += sorted[i];
                }

                int maxSecondary = magie > 0 ? 17 : 15;
                if (secondarySum > maxSecondary) return false;

                return true;
            }

            if (profile == CharacterProfileType.PnjNormal)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (candidate[i] > 4) return false;
                }

                int count4 = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (candidate[i] == 4) count4++;
                }
                if (count4 > 2) return false;

                int[] sorted = (int[])candidate.Clone();
                Array.Sort(sorted);
                Array.Reverse(sorted);

                int secondarySum = 0;
                for (int i = 2; i < 8; i++)
                {
                    if (sorted[i] > 3) return false;
                    secondarySum += sorted[i];
                }

                int maxSecondary = magie > 0 ? 17 : 15;
                if (secondarySum > maxSecondary) return false;

                int maxTotal = magie > 0 ? 25 : 23;
                int sum = 0;
                for (int i = 0; i < 8; i++) sum += candidate[i];
                if (sum > maxTotal) return false;

                return true;
            }

            return false;
        }

        public static bool ValidateAllocation(CharacterSheet sheet, out string statusMessage)
        {
            if (sheet == null)
            {
                statusMessage = "Aucune fiche.";
                return false;
            }
            var b = sheet.BaseAttributes;
            int[] core = { b.Force, b.Agilite, b.Constitution, b.Rapidite, b.Intelligence, b.Erudition, b.Charisme, b.Instinct };

            for (int i = 0; i < core.Length; i++)
            {
                if (core[i] < 1)
                {
                    statusMessage = "❌ Attribut < 1 interdit (Plancher vital : 1)";
                    return false;
                }
                if (core[i] > 10)
                {
                    statusMessage = "❌ Attribut > 10 interdit (Plafond absolu : 10)";
                    return false;
                }
            }

            int totalPurchased = 0;
            if (sheet.AttributeUpgradesPurchased != null)
            {
                for (int p = 0; p < sheet.AttributeUpgradesPurchased.Length; p++)
                    totalPurchased += Math.Max(0, sheet.AttributeUpgradesPurchased[p]);
            }
            if (totalPurchased > 0)
            {
                statusMessage = $"⭐ Profil Évolué (+{totalPurchased} pts achetés en XP)";
                return true;
            }

            if (sheet.Profile == CharacterProfileType.PnjBoss)
            {
                statusMessage = "👑 Boss : Attribution Libre (Plafond 10 respecté)";
                return true;
            }

            if (sheet.Profile == CharacterProfileType.PnjSbire)
            {
                int total = 0;
                for (int i = 0; i < core.Length; i++)
                {
                    if (core[i] > 3)
                    {
                        statusMessage = $"❌ Sbire : Attribut ({core[i]}) > 3 interdit";
                        return false;
                    }
                    total += core[i];
                }

                int expected = b.Magie > 0 ? 11 : 12;
                if (total == expected)
                {
                    statusMessage = $"✅ Sbire : {total}/{expected} pts alloués";
                    return true;
                }

                statusMessage = $"⚠️ Sbire : {total}/{expected} pts (Requis exact : {expected})";
                return false;
            }

            int count5 = 0;
            int count4 = 0;
            int secondarySum = 0;
            bool remainingOver3 = false;

            for (int i = 0; i < core.Length; i++)
            {
                if (sheet.Profile == CharacterProfileType.HerosPJ && core[i] == 5 && count5 == 0)
                {
                    count5++;
                }
                else if (core[i] == 4 && ((sheet.Profile == CharacterProfileType.HerosPJ && count4 == 0) || (sheet.Profile == CharacterProfileType.PnjNormal && count4 < 2)))
                {
                    count4++;
                }
                else
                {
                    if (core[i] > 3) remainingOver3 = true;
                    secondarySum += core[i];
                }
            }

            int targetSecondary = b.Magie > 0 ? 17 : 15;

            if (sheet.Profile == CharacterProfileType.HerosPJ)
            {
                if (count5 != 1 || count4 != 1)
                {
                    statusMessage = $"⚠️ Héros : Requis 1 pilier à 5 ({count5}/1) & 1 à 4 ({count4}/1)";
                    return false;
                }
                if (remainingOver3)
                {
                    statusMessage = "❌ Héros : Les 6 attributs restants doivent être &le; 3";
                    return false;
                }
                if (secondarySum != targetSecondary)
                {
                    statusMessage = $"⚠️ Héros : {secondarySum}/{targetSecondary} pts secondaires";
                    return false;
                }
                statusMessage = $"✅ Héros : Conforme Codex ({targetSecondary} pts)";
                return true;
            }

            if (sheet.Profile == CharacterProfileType.PnjNormal)
            {
                if (count4 != 2)
                {
                    statusMessage = $"⚠️ Soldat : Requis 2 piliers à 4 ({count4}/2)";
                    return false;
                }
                if (remainingOver3)
                {
                    statusMessage = "❌ Soldat : Les 6 attributs restants doivent être &le; 3";
                    return false;
                }
                if (secondarySum != targetSecondary)
                {
                    statusMessage = $"⚠️ Soldat : {secondarySum}/{targetSecondary} pts secondaires";
                    return false;
                }
                statusMessage = $"✅ Soldat : Conforme Codex ({targetSecondary} pts)";
                return true;
            }

            statusMessage = "Statut indéterminé";
            return false;
        }

        public static void ApplyDefaultProfileAttributes(CharacterSheet sheet)
        {
            if (sheet == null) return;
            switch (sheet.Profile)
            {
                case CharacterProfileType.HerosPJ:
                    sheet.BaseAttributes.Force = 5;
                    sheet.BaseAttributes.Constitution = 4;
                    sheet.BaseAttributes.Agilite = 3;
                    sheet.BaseAttributes.Rapidite = 3;
                    sheet.BaseAttributes.Intelligence = 2;
                    sheet.BaseAttributes.Erudition = 3;
                    sheet.BaseAttributes.Charisme = 1;
                    sheet.BaseAttributes.Instinct = 3;
                    sheet.BaseAttributes.Magie = 0;
                    break;
                case CharacterProfileType.PnjNormal:
                    sheet.BaseAttributes.Force = 4;
                    sheet.BaseAttributes.Constitution = 4;
                    sheet.BaseAttributes.Agilite = 3;
                    sheet.BaseAttributes.Rapidite = 3;
                    sheet.BaseAttributes.Intelligence = 2;
                    sheet.BaseAttributes.Erudition = 3;
                    sheet.BaseAttributes.Charisme = 1;
                    sheet.BaseAttributes.Instinct = 3;
                    sheet.BaseAttributes.Magie = 0;
                    break;
                case CharacterProfileType.PnjSbire:
                    sheet.BaseAttributes.Force = 2;
                    sheet.BaseAttributes.Agilite = 2;
                    sheet.BaseAttributes.Constitution = 2;
                    sheet.BaseAttributes.Rapidite = 2;
                    sheet.BaseAttributes.Intelligence = 1;
                    sheet.BaseAttributes.Erudition = 1;
                    sheet.BaseAttributes.Charisme = 1;
                    sheet.BaseAttributes.Instinct = 1;
                    sheet.BaseAttributes.Magie = 0;
                    break;
                case CharacterProfileType.PnjBoss:
                    sheet.BaseAttributes.Force = 7;
                    sheet.BaseAttributes.Constitution = 6;
                    sheet.BaseAttributes.Agilite = 5;
                    sheet.BaseAttributes.Rapidite = 4;
                    sheet.BaseAttributes.Intelligence = 4;
                    sheet.BaseAttributes.Erudition = 2;
                    sheet.BaseAttributes.Charisme = 2;
                    sheet.BaseAttributes.Instinct = 5;
                    sheet.BaseAttributes.Magie = 0;
                    break;
            }
        }

        public static int CountTrainedLevels(CharacterSheet sheet)
        {
            if (sheet == null || sheet.Skills == null) return 0;
            int total = 0;
            for (int i = 0; i < sheet.Skills.Count; i++)
                if (sheet.Skills[i] != null) total += Math.Max(0, sheet.Skills[i].TrainingLevel);
            return total;
        }
    }
}
