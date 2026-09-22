using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Core.Character.Classes
{
    /// <summary>
    /// MÉDECIN CHIRURGIEN — Soutien vital (soins, antidotes, triage).
    /// Piliers ÉRU 5 / INT 4. Ramène les agonisants, purge les états.
    /// Fichier 5/8 des archétypes de base (un fichier par classe).
    /// </summary>
    public static class ClasseMedecinChirurgien
    {
        public static CharacterClassDefinition Build()
        {
            return new CharacterClassDefinition
            {
                ClassId = "medecin-chirurgien",
                DisplayName = "Médecin Chirurgien",
                Tagline = "Soutien — soins, pharma, survie",
                Description = "Sauveur de l'escouade : sutures à 2 PA, sérums, panacée à 15 PV. Piliers ÉRU 5 / INT 4. Ne tue pas — empêche les alliés de mourir.",
                IconGlyph = "⚕️",
                ThemeColor = new Color(0.1f, 0.85f, 0.45f),
                SuggestedSpecies = SpeciesType.Humain,
                Profile = CharacterProfileType.HerosPJ,
                // 5/4 + secondaires 15 : FOR2 AGI2 CON3 RAP2 CHA3 INS3 = 15 ✓
                BaseAttributes = new Attributes(@for: 2, agi: 2, con: 3, rap: 2, @int: 4, eru: 5, cha: 3, ins: 3, mag: 0),
                BaseArmor = 1,
                StartingCreditsCE = 9000,
                StartingTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.MedecineAvancee, 2),
                    new SkillTrainingStep(SkillType.PremiersSoins, 2),
                    new SkillTrainingStep(SkillType.Academie, 1),
                    new SkillTrainingStep(SkillType.Observation, 1),
                },
                StartingSpecializations = new List<string>
                {
                    "Chirurgie",
                    "Apothicaire de Guerre",
                },
                BuildPath = new List<string>
                {
                    // Branche chirurgie (soins d'urgence)
                    "Chirurgie",
                    "Chirurgie : Diagnostic Vital",
                    "Chirurgie : Suture Réflexe",
                    "Chirurgie : Greffe d'Urgence",
                    // Branche pharma (buffs / purges)
                    "Apothicaire de Guerre",
                    "Apothicaire de Guerre : Stimulant Neuro-Accélérateur",
                    "Apothicaire de Guerre : Sérum Antidote",
                    "Apothicaire de Guerre : Cocktail Panacée",
                    // Branche survie (ne jamais tomber)
                    "Condition de Fer",
                    "Condition de Fer : Dur à Cuire",
                    "Trempe de Fer",
                    "Trempe de Fer : Ignorer la Douleur",
                    "Immunité Toxique",
                    "Immunité Toxique : Neutralisation des Poisons",
                    // Branche terrain
                    "Franchissement",
                    "Course d'Endurance",
                },
                TargetTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.MedecineAvancee, 3),
                    new SkillTrainingStep(SkillType.PremiersSoins, 3),
                    new SkillTrainingStep(SkillType.Academie, 2),
                    new SkillTrainingStep(SkillType.EndurancePhysique, 1),
                    new SkillTrainingStep(SkillType.Observation, 1),
                },
                PlayTips = new List<string>
                {
                    "Suture Réflexe = soins à 2 PA au lieu de 3 : premier achat obligatoire.",
                    "Priorité XP : Médecine +3, puis Sérum Antidote (purge tout au contact).",
                    "Placement : 2 cases derrière le tank, jamais en première ligne ; garrot + stimulant en bandoulière.",
                },
            };
        }
    }
}
