using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Core.Character.Classes
{
    /// <summary>
    /// TIREUR DE PRÉCISION — Ranger / sniper (fusil + pistolet + arc).
    /// Piliers AGI 5 / INS 4. Frappe à distance, supprime, ne se fait jamais toucher.
    /// Fichier 2/8 des archétypes de base (un fichier par classe).
    /// </summary>
    public static class ClasseTireurPrecision
    {
        public static CharacterClassDefinition Build()
        {
            return new CharacterClassDefinition
            {
                ClassId = "tireur-precision",
                DisplayName = "Tireur de Précision",
                Tagline = "Ranger — fusil, pistolet, discrétion",
                Description = "Œil d'Hybris : sniper patient (Fusil de Précision) qui nettoie à distance puis se replie en Discrétion. Piliers AGI 5 / INS 4, jamais au contact.",
                IconGlyph = "🎯",
                ThemeColor = new Color(0.2f, 0.75f, 1f),
                SuggestedSpecies = SpeciesType.Humain,
                Profile = CharacterProfileType.HerosPJ,
                // 5/4 + secondaires 15 : FOR2 CON3 RAP3 INT3 ERU2 CHA2 = 15 ✓
                BaseAttributes = new Attributes(@for: 2, agi: 5, con: 3, rap: 3, @int: 3, eru: 2, cha: 2, ins: 4, mag: 0),
                BaseArmor = 1,
                StartingCreditsCE = 10000,
                StartingTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.Ballistique, 2),
                    new SkillTrainingStep(SkillType.Discretion, 1),
                    new SkillTrainingStep(SkillType.Observation, 1),
                    new SkillTrainingStep(SkillType.Esquive, 1),
                },
                StartingSpecializations = new List<string>
                {
                    "Fusil de Précision",
                    "Pistolet & Tir Rapide",
                },
                BuildPath = new List<string>
                {
                    // Branche sniper (cœur)
                    "Fusil de Précision",
                    "Fusil de Précision : Visée Millimétrique",
                    "Fusil de Précision : Perforation Hyper-Véloce",
                    "Fusil de Précision : Tir à Travers les Parois",
                    "Fusil de Précision : Tir de Suppression",
                    "Fusil de Précision : Calibre Anti-Matériel",
                    // Branche pistolet (contact)
                    "Pistolet & Tir Rapide",
                    "Pistolet & Tir Rapide : Dégainé Réflexe",
                    "Pistolet & Tir Rapide : Balle dans le Genou",
                    "Pistolet & Tir Rapide : Tir de Neutralisation",
                    "Pistolet & Tir Rapide : Akimbo Assaut",
                    "Pistolet & Tir Rapide : Trait d'Orichalque",
                    // Branche survie / fuite
                    "Course d'Endurance",
                    "Course d'Endurance : Second Souffle",
                    "Vigilance Réflexe",
                    "Vigilance Réflexe : Oeil de Lynx",
                    "Vigilance Réflexe : Détection Thermique",
                    "Vigilance Réflexe : Perception Panoramique 360°",
                    "Sens du Danger",
                    "Sens du Danger : Premiers Réflexes",
                },
                TargetTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.Ballistique, 3),
                    new SkillTrainingStep(SkillType.Discretion, 2),
                    new SkillTrainingStep(SkillType.Observation, 2),
                    new SkillTrainingStep(SkillType.Esquive, 2),
                    new SkillTrainingStep(SkillType.Athletisme, 1),
                },
                PlayTips = new List<string>
                {
                    "Règle d'or : ne jamais bouger avant un tir chirurgical (+2 Visée Millimétrique).",
                    "Priorité XP : Ballistique +3, puis Perforation Hyper-Véloce (ignore 4 armure).",
                    "Équipement : fusil de précision + pistolet de secours ; couvert total entre chaque tir.",
                },
            };
        }
    }
}
