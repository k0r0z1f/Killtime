using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    public partial class CharacterProgressionManager
    {
        // CharacterProgressionManager.HeroAccess.cs — Registre heroique : SpecializationDetail, champs statiques, acces 6 heros.

        [Serializable]
        public class SpecializationDetail
        {
            public string Name;
            public SkillType SourceSkill;
            public string ParentSpecialization;
            public bool IsImprovement;
            public bool IsHidden;
            public bool IsVolume2;
            public bool IsMinaExclusive;
            public bool IsLucasExclusive;
            public bool IsThomasExclusive;
            public bool IsJohnExclusive;
            public bool IsErikaExclusive;
            public bool IsVanceExclusive;
            public string Description;
            public string MechanicalEffect;
        }

        private static readonly Dictionary<string, SpecializationDetail> _registry = new(StringComparer.OrdinalIgnoreCase);
        private static bool _registryBuilt = false;

        public static readonly Dictionary<string, SkillType> SpecializationSources = new(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, string> SpecializationPrerequisites = new(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> HiddenSpecializations = new(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> MinaExclusiveSpecializations = new(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> LucasExclusiveSpecializations = new(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> ThomasExclusiveSpecializations = new(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> JohnExclusiveSpecializations = new(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> ErikaExclusiveSpecializations = new(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> VanceExclusiveSpecializations = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsMina(CharacterSheet sheet)
        {
            // Délégation propre : logique héroïque dans MinaCharacter.
            return MinaCharacter.IsMina(sheet);
        }

        public static bool IsLucas(CharacterSheet sheet)
        {
            // Délégation propre : logique héroïque dans LucasCharacter.
            return LucasCharacter.IsLucas(sheet);
        }

        public static bool IsThomas(CharacterSheet sheet)
        {
            // Délégation propre : logique héroïque dans ThomasCharacter.
            return ThomasCharacter.IsThomas(sheet);
        }

        public static bool IsJohn(CharacterSheet sheet)
        {
            // Délégation propre : logique héroïque dans JohnCharacter.
            return JohnCharacter.IsJohn(sheet);
        }

        public static bool IsErika(CharacterSheet sheet)
        {
            // Délégation propre : logique héroïque dans ErikaCharacter.
            return ErikaCharacter.IsErika(sheet);
        }

        public static bool IsVance(CharacterSheet sheet)
        {
            // Délégation propre : logique héroïque dans VanceCharacter.
            return VanceCharacter.IsVance(sheet);
        }

        public static bool IsSkillAccessible(CharacterSheet sheet, SkillType skill)
        {
            // Délégation propre : restrictions d'âme dans MinaCharacter / LucasCharacter / ThomasCharacter / JohnCharacter / ErikaCharacter / VanceCharacter.
            if (sheet != null && MinaCharacter.IsMina(sheet))
            {
                if (MinaCharacter.IsSkillForbidden(skill))
                    return false;
            }
            if (sheet != null && LucasCharacter.IsLucas(sheet))
            {
                if (LucasCharacter.IsSkillForbidden(skill))
                    return false;
            }
            if (sheet != null && ThomasCharacter.IsThomas(sheet))
            {
                if (ThomasCharacter.IsSkillForbidden(skill))
                    return false;
            }
            if (sheet != null && JohnCharacter.IsJohn(sheet))
            {
                if (JohnCharacter.IsSkillForbidden(skill))
                    return false;
            }
            if (sheet != null && ErikaCharacter.IsErika(sheet))
            {
                if (ErikaCharacter.IsSkillForbidden(skill))
                    return false;
            }
            if (sheet != null && VanceCharacter.IsVance(sheet))
            {
                if (VanceCharacter.IsSkillForbidden(skill))
                    return false;
            }
            return true;
        }

        public static bool IsMinaExclusiveSpecialization(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return false;
            return MinaExclusiveSpecializations.Contains(name.Trim());
        }

        public static bool IsLucasExclusiveSpecialization(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return false;
            return LucasExclusiveSpecializations.Contains(name.Trim());
        }

        public static bool IsThomasExclusiveSpecialization(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return false;
            return ThomasExclusiveSpecializations.Contains(name.Trim());
        }

        public static bool IsJohnExclusiveSpecialization(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return false;
            return JohnExclusiveSpecializations.Contains(name.Trim());
        }

        public static bool IsErikaExclusiveSpecialization(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return false;
            return ErikaExclusiveSpecializations.Contains(name.Trim());
        }

        public static bool IsVanceExclusiveSpecialization(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return false;
            return VanceExclusiveSpecializations.Contains(name.Trim());
        }

        static CharacterProgressionManager()
        {
            EnsureRegistryBuilt();
        }

    }
}
