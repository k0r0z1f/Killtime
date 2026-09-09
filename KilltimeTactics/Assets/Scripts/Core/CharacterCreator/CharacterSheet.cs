using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Fiche technique intégrale d'un protagoniste (PJ, PNJ régulier ou sbire).
    /// Sérialisable en JSON pour stockage disque ou transmission WebGL.
    /// </summary>
    [Serializable]
    public class CharacterSheet
    {
        public string SheetId = Guid.NewGuid().ToString("N");
        public string Name = "Nouveau Personnage";
        public int Age = 25;
        public string Gender = "Indéterminé";
        public SpeciesType Species = SpeciesType.Humain;
        public CharacterProfileType Profile = CharacterProfileType.HerosPJ;
        public string LoreNotes = "";

        public Attributes BaseAttributes = new Attributes(3, 3, 3, 3, 3, 3, 2, 2, 0, 3, 3, 1);
        public int BaseArmor = 1;
        public int AvailableXP = 0;
        public int TotalEarnedXP = 0;

        public List<SkillProgressionEntry> Skills = new();
        public List<string> UnlockedSpecializations = new();
        public List<NythariteSpell> LearnedSpells = new();

        public CharacterSheet()
        {
            InitializeDefaultSkills();
        }

        public void InitializeDefaultSkills()
        {
            Skills.Clear();
            foreach (SkillType skill in Enum.GetValues(typeof(SkillType)))
            {
                Skills.Add(new SkillProgressionEntry(skill, 0));
            }
        }

        public SkillProgressionEntry GetSkill(SkillType type)
        {
            var entry = Skills.Find(s => s.Skill == type);
            if (entry == null)
            {
                entry = new SkillProgressionEntry(type, 0);
                Skills.Add(entry);
            }
            return entry;
        }

        /// <summary>
        /// Applique les modificateurs de l'espèce aux attributs de base.
        /// </summary>
        public Attributes GetEffectiveAttributes()
        {
            var (dFor, dAgi, dCon, dRap, dInt, dEru, dCha, dIns, minSense) = SpeciesRules.GetModifiers(Species);
            return new Attributes(
                @for: Mathf.Max(1, BaseAttributes.Force + dFor),
                agi: Mathf.Max(1, BaseAttributes.Agilite + dAgi),
                con: Mathf.Max(1, BaseAttributes.Constitution + dCon),
                rap: Mathf.Max(1, BaseAttributes.Rapidite + dRap),
                @int: Mathf.Max(1, BaseAttributes.Intelligence + dInt),
                eru: Mathf.Max(1, BaseAttributes.Erudition + dEru),
                cha: Mathf.Max(1, BaseAttributes.Charisme + dCha),
                ins: Mathf.Max(1, BaseAttributes.Instinct + dIns),
                mag: BaseAttributes.Magie,
                vision: Mathf.Max(minSense, BaseAttributes.Vision),
                ouie: Mathf.Max(minSense, BaseAttributes.Ouie),
                miracle: BaseAttributes.PointsMiracle
            );
        }

        /// <summary>
        /// Génère le composant CharacterStats prêt pour le combat.
        /// </summary>
        public CharacterStats ToCombatStats()
        {
            var effective = GetEffectiveAttributes();
            var stats = new CharacterStats(Name, effective, BaseArmor);
            return stats;
        }
    }
}