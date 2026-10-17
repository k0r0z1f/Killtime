using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;

namespace Killtime.DevTools.Character
{
    /// <summary>
    /// Résolution genrée des clips d'animation + fallback Resources.
    /// Extrait à l'identique de CharacterDevWindow (et dupliqué dans
    /// TacticalUnitVisual — unification volontairement reportée à un run dédié).
    /// </summary>
    public static class AnimationClipResolver
    {
        public static string ResolveGenderKey(string sheetGender, string modelPrefabName)
        {
            if (!string.IsNullOrEmpty(sheetGender))
            {
                string g = sheetGender.Trim().ToLowerInvariant();
                if (g.StartsWith("m") || g.Contains("masc") || g.Contains("male") || g.Contains("homme"))
                    return "Male";
                if (g.StartsWith("f") || g.Contains("fem") || g.Contains("fém") || g.Contains("woman") || g.Contains("girl"))
                    return "Female";
            }

            if (!string.IsNullOrEmpty(modelPrefabName))
            {
                string m = modelPrefabName.ToLowerInvariant();
                if (m.Contains("mina") || m.Contains("wife") || m.Contains("female") || m.Contains("femelle") || m.Contains("woman") || m.Contains("girl"))
                    return "Female";
                if (m.Contains("soldier") || m.Contains("hitman") || m.Contains("boss") || m.Contains("male") || m.Contains("homme") || m.Contains("thomas") || m.Contains("lucas"))
                    return "Male";
            }

            return "Female";
        }

        public static string ResolveGenderKey(CharacterSheet sheet)
        {
            if (sheet == null) return "Female";
            return ResolveGenderKey(sheet.Gender, sheet.ModelPrefabName);
        }

        public static int GenderToIndex(string gender)
        {
            if (string.IsNullOrEmpty(gender)) return 2;
            string g = gender.Trim().ToLowerInvariant();
            if (g.StartsWith("m") || g.Contains("masc") || g.Contains("male") || g.Contains("homme"))
                return 0;
            if (g.StartsWith("f") || g.Contains("fem") || g.Contains("fém") || g.Contains("woman") || g.Contains("girl"))
                return 1;
            return 2;
        }

        public static AnimationClip LoadClip(string clipName)
        {
            return LoadClip(clipName, "Female");
        }

        public static AnimationClip LoadClip(string clipName, string genderKey)
        {
            AnimationClip clip = null;

            var candidatePaths = new List<string>();
            if (!string.IsNullOrEmpty(genderKey))
                candidatePaths.Add($"Animations/{genderKey}/{clipName}");
            if (!string.Equals(genderKey, "Female", System.StringComparison.OrdinalIgnoreCase))
                candidatePaths.Add($"Animations/Female/{clipName}");
            candidatePaths.Add($"Animations/{clipName}");
            candidatePaths.Add(clipName);

            foreach (var path in candidatePaths)
            {
                var subClips = Resources.LoadAll<AnimationClip>(path);
                if (subClips != null && subClips.Length > 0)
                {
                    for (int i = 0; i < subClips.Length; i++)
                    {
                        if (subClips[i] != null && !subClips[i].name.StartsWith("__preview__"))
                        {
                            clip = subClips[i];
                            break;
                        }
                    }
                }
                if (clip != null) break;

                clip = Resources.Load<AnimationClip>(path);
                if (clip != null) break;
            }

            return clip;
        }
    }
}
