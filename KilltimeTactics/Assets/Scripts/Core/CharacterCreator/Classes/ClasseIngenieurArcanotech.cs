using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Core.Character.Classes
{
    /// <summary>
    /// INGÉNIEUR ARCANOTECH — Technicien nytharite (surchauffe, réparation, grenades).
    /// Piliers INT 5 / AGI 4. Double les lasers, jamme les blindés.
    /// Fichier 6/8 des archétypes de base (un fichier par classe).
    /// </summary>
    public static class ClasseIngenieurArcanotech
    {
        public static CharacterClassDefinition Build()
        {
            return new CharacterClassDefinition
            {
                ClassId = "ingenieur-arcanotech",
                DisplayName = "Ingénieur Arcanotech",
                Tagline = "Tech — nytharite, lasers, explosifs",
                Description = "Sorcier des machines : surfréquence (double les lasers), stabilise les flux, répare sous le feu. Piliers INT 5 / AGI 4. Le multiplicateur de dégâts de l'escouade.",
                IconGlyph = "⚙️",
                ThemeColor = new Color(0.45f, 0.45f, 1f),
                SuggestedSpecies = SpeciesType.Nain,
                Profile = CharacterProfileType.HerosPJ,
                // 5/4 + secondaires 15 : FOR2 CON2 RAP3 ERU3 CHA2 INS3 = 15 ✓
                BaseAttributes = new Attributes(@for: 2, agi: 4, con: 2, rap: 3, @int: 5, eru: 3, cha: 2, ins: 3, mag: 0),
                BaseArmor = 1,
                StartingCreditsCE = 12000,
                StartingTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.IngenierieArcanotech, 2),
                    new SkillTrainingStep(SkillType.Ballistique, 1),
                    new SkillTrainingStep(SkillType.Subterfuge, 1),
                    new SkillTrainingStep(SkillType.TactiqueStrategie, 1),
                },
                StartingSpecializations = new List<string>
                {
                    "Surfréquence Nytharite",
                    "Grenadier d'Assaut",
                },
                BuildPath = new List<string>
                {
                    // Branche surfréquence (cœur)
                    "Surfréquence Nytharite",
                    "Surfréquence Nytharite : Stabilisateur de Flux",
                    "Surfréquence Nytharite : Décharge Harmonique",
                    "Surfréquence Nytharite : Résonance Infinie",
                    // Branche explosifs (anti-blindé)
                    "Grenadier d'Assaut",
                    "Grenadier d'Assaut : Calcul Balistique",
                    "Grenadier d'Assaut : Dégoupillage Éclair",
                    "Grenadier d'Assaut : Souffle Thermobarique",
                    // Branche analyse (support)
                    "Analyse de Faille",
                    "Analyse de Faille : Tir Coordonné",
                    "Analyse de Faille : Prédiction de Retraite",
                    "Analyse de Faille : Échec et Mat",
                    // Branche survie atelier
                    "Affûtage de Précision",
                    "Affûtage : Fil Monomoléculaire",
                    "Affûtage : Blindage Trempé",
                },
                TargetTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.IngenierieArcanotech, 3),
                    new SkillTrainingStep(SkillType.Ballistique, 2),
                    new SkillTrainingStep(SkillType.TactiqueStrategie, 2),
                    new SkillTrainingStep(SkillType.Subterfuge, 2),
                    new SkillTrainingStep(SkillType.Artisanat, 1),
                },
                PlayTips = new List<string>
                {
                    "Surfréquence : 2 PA = double puissance laser pendant 1 tour (3 tours avec Résonance Infinie).",
                    "Priorité XP : Ingénierie +3, puis Stabilisateur (plus aucun jam).",
                    "Équipement : fusil laser + grenades + kit de réparation ; toujours à couvert partiel.",
                },
            };
        }
    }
}
