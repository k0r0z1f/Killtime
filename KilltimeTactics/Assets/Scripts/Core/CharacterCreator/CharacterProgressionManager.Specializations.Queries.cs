using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    public partial class CharacterProgressionManager
    {
        // CharacterProgressionManager.Specializations.Queries.cs — RegisterSpec et requetes (detail, listes, parents, Volume II, sources).

        public static void RegisterSpec(SkillType skill, string name, string parent, bool isHidden, bool isVol2, string desc, string mechanic, bool isMinaExclusive = false, bool isLucasExclusive = false, bool isThomasExclusive = false, bool isJohnExclusive = false, bool isErikaExclusive = false, bool isVanceExclusive = false)
        {
            var detail = new SpecializationDetail
            {
                Name = name,
                SourceSkill = skill,
                ParentSpecialization = parent,
                IsImprovement = !string.IsNullOrEmpty(parent),
                IsHidden = isHidden,
                IsVolume2 = isVol2,
                IsMinaExclusive = isMinaExclusive,
                IsLucasExclusive = isLucasExclusive,
                IsThomasExclusive = isThomasExclusive,
                IsJohnExclusive = isJohnExclusive,
                IsErikaExclusive = isErikaExclusive,
                IsVanceExclusive = isVanceExclusive,
                Description = desc,
                MechanicalEffect = mechanic
            };

            _registry[name] = detail;
            SpecializationSources[name] = skill;

            if (!string.IsNullOrEmpty(parent))
            {
                SpecializationPrerequisites[name] = parent;
            }

            if (isHidden)
            {
                HiddenSpecializations.Add(name);
            }

            if (isMinaExclusive)
            {
                MinaExclusiveSpecializations.Add(name);
            }

            if (isLucasExclusive)
            {
                LucasExclusiveSpecializations.Add(name);
            }

            if (isThomasExclusive)
            {
                ThomasExclusiveSpecializations.Add(name);
            }

            if (isJohnExclusive)
            {
                JohnExclusiveSpecializations.Add(name);
            }

            if (isErikaExclusive)
            {
                ErikaExclusiveSpecializations.Add(name);
            }

            if (isVanceExclusive)
            {
                VanceExclusiveSpecializations.Add(name);
            }
        }

        public static SpecializationDetail GetSpecializationDetail(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _registry.TryGetValue(name.Trim(), out var detail) ? detail : null;
        }

        public static IEnumerable<SpecializationDetail> GetAllSpecializations(CharacterSheet sheet = null)
        {
            EnsureRegistryBuilt();
            bool forMina = sheet != null && IsMina(sheet);
            bool forLucas = sheet != null && IsLucas(sheet);
            bool forThomas = sheet != null && IsThomas(sheet);
            bool forJohn = sheet != null && IsJohn(sheet);
            bool forErika = sheet != null && IsErika(sheet);
            bool forVance = sheet != null && IsVance(sheet);

            foreach (var detail in _registry.Values)
            {
                if (forMina)
                {
                    if (MinaCharacter.IsSkillForbidden(detail.SourceSkill)) continue;
                    if (detail.IsLucasExclusive || detail.IsThomasExclusive || detail.IsJohnExclusive || detail.IsErikaExclusive || detail.IsVanceExclusive) continue;
                }
                else if (forLucas)
                {
                    if (LucasCharacter.IsSkillForbidden(detail.SourceSkill)) continue;
                    if (detail.IsMinaExclusive || detail.IsThomasExclusive || detail.IsJohnExclusive || detail.IsErikaExclusive || detail.IsVanceExclusive) continue;
                }
                else if (forThomas)
                {
                    if (ThomasCharacter.IsSkillForbidden(detail.SourceSkill)) continue;
                    if (detail.IsMinaExclusive || detail.IsLucasExclusive || detail.IsJohnExclusive || detail.IsErikaExclusive || detail.IsVanceExclusive) continue;
                }
                else if (forJohn)
                {
                    if (JohnCharacter.IsSkillForbidden(detail.SourceSkill)) continue;
                    if (detail.IsMinaExclusive || detail.IsLucasExclusive || detail.IsThomasExclusive || detail.IsErikaExclusive || detail.IsVanceExclusive) continue;
                }
                else if (forErika)
                {
                    if (ErikaCharacter.IsSkillForbidden(detail.SourceSkill)) continue;
                    if (detail.IsMinaExclusive || detail.IsLucasExclusive || detail.IsThomasExclusive || detail.IsJohnExclusive || detail.IsVanceExclusive) continue;
                }
                else if (forVance)
                {
                    if (VanceCharacter.IsSkillForbidden(detail.SourceSkill)) continue;
                    if (detail.IsMinaExclusive || detail.IsLucasExclusive || detail.IsThomasExclusive || detail.IsJohnExclusive || detail.IsErikaExclusive) continue;
                }
                else
                {
                    // Les personnages réguliers n'ont pas accès aux voies exclusives des héros
                    if (detail.IsMinaExclusive || detail.IsLucasExclusive || detail.IsThomasExclusive || detail.IsJohnExclusive || detail.IsErikaExclusive || detail.IsVanceExclusive)
                        continue;
                }

                yield return detail;
            }
        }

        public static List<SpecializationDetail> GetSpecializationsForSkill(SkillType skill)
        {
            EnsureRegistryBuilt();
            var list = new List<SpecializationDetail>();
            foreach (var detail in _registry.Values)
            {
                if (detail.SourceSkill == skill)
                {
                    list.Add(detail);
                }
            }
            return list;
        }

        public static bool IsHiddenSpecialization(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return false;
            return HiddenSpecializations.Contains(name.Trim());
        }

        public static bool TryGetParentSpecialization(string specializationName, out string parentName)
        {
            EnsureRegistryBuilt();
            parentName = null;
            if (string.IsNullOrWhiteSpace(specializationName)) return false;
            string clean = specializationName.Trim();
            if (SpecializationPrerequisites.TryGetValue(clean, out parentName)) return true;
            int colonIdx = clean.IndexOf(" : ", StringComparison.Ordinal);
            if (colonIdx > 0)
            {
                parentName = clean.Substring(0, colonIdx).Trim();
                return true;
            }
            return false;
        }

        public static bool IsVolume2Unlocked()
        {
            var director = Story.ScenarioDirector.Instance;
            return director != null && director.State != null && director.State.HasFlag("FLAG_MINALIA_AWAKENED");
        }

        public static bool IsVolume2Specialization(string specializationName)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(specializationName)) return false;
            string s = specializationName.Trim();
            if (_registry.TryGetValue(s, out var detail)) return detail.IsVolume2;
            if (TryGetParentSpecialization(s, out string parent))
            {
                if (_registry.TryGetValue(parent, out var pDetail)) return pDetail.IsVolume2;
            }
            return false;
        }

        public static bool TryGetSpecializationSource(string specializationName, out SkillType source)
        {
            EnsureRegistryBuilt();
            source = default;
            if (string.IsNullOrWhiteSpace(specializationName)) return false;
            string clean = specializationName.Trim();
            if (SpecializationSources.TryGetValue(clean, out source)) return true;
            if (SpecializationPrerequisites.TryGetValue(clean, out string pre))
            {
                return TryGetSpecializationSource(pre, out source);
            }
            if (TryGetParentSpecialization(clean, out string parent))
            {
                return TryGetSpecializationSource(parent, out source);
            }
            return false;
        }

    }
}
