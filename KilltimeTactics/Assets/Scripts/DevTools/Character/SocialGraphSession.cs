using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.DevTools.Character
{
    /// <summary>
    /// Session du graphe social de la fiche active (onglet Relations).
    /// Encapsule le cache + CharacterRelationshipStorageService.
    /// La window garde uniquement le binding IMGUI (sliders, toolbars).
    /// </summary>
    public sealed class SocialGraphSession
    {
        public CharacterSocialGraph Current { get; set; }
        public string LastLoadedSheetId { get; set; } = string.Empty;

        public string NewRelationKnownSince { get; set; } = "Temple de Brum'korath";
        public string NewRelationLocation { get; set; } = "Secteur Zéro";

        public void ResetForNewSheet()
        {
            Current = null;
            LastLoadedSheetId = string.Empty;
        }

        public void InvalidateCache()
        {
            LastLoadedSheetId = string.Empty;
        }

        public CharacterSocialGraph EnsureLoaded(CharacterSheet sheet)
        {
            if (sheet == null) return Current;
            if (Current == null || LastLoadedSheetId != sheet.SheetId)
            {
                Current = CharacterRelationshipStorageService.LoadSocialGraph(sheet);
                LastLoadedSheetId = sheet.SheetId;
            }
            return Current;
        }

        public List<CharacterSheet> CollectCandidates(CharacterSheet sheet)
        {
            var candidates = new List<CharacterSheet>();
            if (sheet == null) return candidates;
            var savedFiles = CharacterStorageService.GetSavedCharacterFiles();
            for (int i = 0; i < savedFiles.Count; i++)
            {
                var s = CharacterStorageService.LoadCharacter(savedFiles[i]);
                if (s != null && s.SheetId != sheet.SheetId)
                {
                    candidates.Add(s);
                }
            }
            return candidates;
        }

        public string Introduce(CharacterSheet sheet, CharacterSheet other)
        {
            if (sheet == null || other == null) return "Introduction impossible.";
            CharacterRelationshipStorageService.IntroduceCharacters(
                sheet, other, RelationshipLinkType.NeutreInconnu, NewRelationLocation, NewRelationKnownSince);
            Current = CharacterRelationshipStorageService.LoadSocialGraph(sheet);
            LastLoadedSheetId = sheet.SheetId;
            return $"'{sheet.Name}' et '{other.Name}' introduits mutuellement !";
        }

        public string RemoveAt(int index)
        {
            if (Current == null || index < 0 || index >= Current.Relationships.Count)
                return "Relation introuvable.";
            var rel = Current.Relationships[index];
            string name = rel != null ? rel.TargetCharacterName : "?";
            Current.Relationships.RemoveAt(index);
            CharacterRelationshipStorageService.SaveSocialGraph(Current);
            return $"Relation avec '{name}' dissoute.";
        }

        public string Save(CharacterSheet sheet)
        {
            if (Current == null) return "Aucun graphe à sauvegarder.";
            CharacterRelationshipStorageService.SaveSocialGraph(Current);
            string id = sheet != null ? sheet.SheetId : LastLoadedSheetId;
            string shortId = !string.IsNullOrEmpty(id) && id.Length >= 8 ? id.Substring(0, 8) : id;
            string name = sheet != null ? sheet.Name : Current.OwnerCharacterName;
            return $"Graphe relationnel de '{name}' sauvegardé sous Relationships/Social_{shortId}.json";
        }
    }
}
