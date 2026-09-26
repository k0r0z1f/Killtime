using System;
using System.Collections.Generic;

namespace Killtime.Core.Character
{
    public enum RelationshipLinkType
    {
        NeutreInconnu = 0,
        SymbioteLigneZero = 1,
        FraterniteDarmes = 2,
        HierarchieMilitaire = 3,
        DetteDhonneur = 4,
        RivaliteMartiale = 5,
        MefianceInstinctive = 6,
        HostiliteDeclaree = 7,
        MentorEleve = 8
    }

    [Serializable]
    public class CharacterRelationshipEntry
    {
        public string TargetSheetId;
        public string TargetCharacterName;
        public int Affinity;
        public int Trust;
        public RelationshipLinkType LinkType;
        public string KnownSince;
        public string FirstMetLocation;
        public string Notes;
        public int InteractionsCount;

        public CharacterRelationshipEntry()
        {
            TargetSheetId = string.Empty;
            TargetCharacterName = "Inconnu";
            Affinity = 0;
            Trust = 50;
            LinkType = RelationshipLinkType.NeutreInconnu;
            KnownSince = "Non consigné";
            FirstMetLocation = "Inconnue";
            Notes = string.Empty;
            InteractionsCount = 1;
        }

        public CharacterRelationshipEntry(string targetSheetId, string targetName, RelationshipLinkType linkType, int affinity, int trust, string knownSince, string location)
        {
            TargetSheetId = targetSheetId ?? string.Empty;
            TargetCharacterName = string.IsNullOrWhiteSpace(targetName) ? "Inconnu" : targetName;
            LinkType = linkType;
            Affinity = Math.Clamp(affinity, -100, 100);
            Trust = Math.Clamp(trust, 0, 100);
            KnownSince = string.IsNullOrWhiteSpace(knownSince) ? "Non consigné" : knownSince;
            FirstMetLocation = string.IsNullOrWhiteSpace(location) ? "Inconnue" : location;
            Notes = string.Empty;
            InteractionsCount = 1;
        }
    }

    [Serializable]
    public class CharacterSocialGraph
    {
        public string OwnerSheetId;
        public string OwnerCharacterName;
        public List<CharacterRelationshipEntry> Relationships = new();

        public CharacterSocialGraph()
        {
            OwnerSheetId = string.Empty;
            OwnerCharacterName = string.Empty;
            Relationships = new List<CharacterRelationshipEntry>();
        }

        public CharacterSocialGraph(string ownerSheetId, string ownerName)
        {
            OwnerSheetId = ownerSheetId ?? string.Empty;
            OwnerCharacterName = ownerName ?? string.Empty;
            Relationships = new List<CharacterRelationshipEntry>();
        }

        public CharacterRelationshipEntry GetRelationship(string targetSheetId)
        {
            if (string.IsNullOrEmpty(targetSheetId) || Relationships == null) return null;
            return Relationships.Find(r => r != null && string.Equals(r.TargetSheetId, targetSheetId, StringComparison.OrdinalIgnoreCase));
        }

        public void AddOrUpdateRelationship(CharacterRelationshipEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.TargetSheetId)) return;
            Relationships ??= new List<CharacterRelationshipEntry>();

            int idx = Relationships.FindIndex(r => r != null && string.Equals(r.TargetSheetId, entry.TargetSheetId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                Relationships[idx] = entry;
            }
            else
            {
                Relationships.Add(entry);
            }
        }
    }
}