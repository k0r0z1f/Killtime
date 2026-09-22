using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Core.Character.Classes
{
    /// <summary>
    /// CHEF TACTICIEN — Leader d'escouade (commandement, intimidation, tir coordonné).
    /// Piliers CHA 5 / INT 4. Donne des PA, verrouille, fait fuir les sbires.
    /// Fichier 8/8 des archétypes de base (un fichier par classe).
    /// </summary>
    public static class ClasseChefTacticien
    {
        public static CharacterClassDefinition Build()
        {
            return new CharacterClassDefinition
            {
                ClassId = "chef-tacticien",
                DisplayName = "Chef Tacticien",
                Tagline = "Leader — ordres, peur, tir coordonné",
                Description = "Voix de l'escouade : +1 PA à tous (Mener), critiques garantis (Échec et Mat), sbires en fuite (Intimider). Piliers CHA 5 / INT 4. Le multiplicateur d'équipe.",
                IconGlyph = "👑",
                ThemeColor = new Color(0.98f, 0.7f, 0.15f),
                SuggestedSpecies = SpeciesType.Humain,
                Profile = CharacterProfileType.HerosPJ,
                // 5/4 + secondaires 15 : FOR3 AGI2 CON3 RAP2 ERU3 INS2 = 15 ✓
                BaseAttributes = new Attributes(@for: 3, agi: 2, con: 3, rap: 2, @int: 4, eru: 3, cha: 5, ins: 2, mag: 0),
                BaseArmor = 1,
                StartingCreditsCE = 9000,
                StartingTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.Leadership, 2),
                    new SkillTrainingStep(SkillType.Intimidation, 1),
                    new SkillTrainingStep(SkillType.Ballistique, 1),
                    new SkillTrainingStep(SkillType.TactiqueStrategie, 1),
                },
                StartingSpecializations = new List<string>
                {
                    "Mener (Commandement)",
                    "Intimider",
                },
                BuildPath = new List<string>
                {
                    // Branche commandement (cœur)
                    "Mener (Commandement)",
                    "Mener : En avant !",
                    "Mener : Tenir la Ligne !",
                    "Mener : Galvanisation des Héros",
                    // Branche terreur (contrôle)
                    "Intimider",
                    "Intimider : Rugissement de Terreur",
                    "Intimider : Regard de Prédateur",
                    "Intimider : Paralysie Psychologique",
                    // Branche analyse (critiques)
                    "Analyse de Faille",
                    "Analyse de Faille : Tir Coordonné",
                    "Analyse de Faille : Prédiction de Retraite",
                    "Analyse de Faille : Échec et Mat",
                    // Branche pistolet (légitime défense)
                    "Pistolet & Tir Rapide",
                    "Pistolet & Tir Rapide : Dégainé Réflexe",
                    // Branche négociation (hors combat)
                    "Négocier",
                    "Négocier : Marché Noir Impérial",
                    "Bouclier Vivant",
                    "Bouclier Vivant : Interposition Réflexe",
                },
                TargetTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.Leadership, 3),
                    new SkillTrainingStep(SkillType.Intimidation, 2),
                    new SkillTrainingStep(SkillType.Communication, 2),
                    new SkillTrainingStep(SkillType.Ballistique, 2),
                    new SkillTrainingStep(SkillType.TactiqueStrategie, 2),
                },
                PlayTips = new List<string>
                {
                    "Galvanisation = 2 PA à toute l'escouade + purge mentale : à garder pour le tour décisif.",
                    "Priorité XP : Leadership +3, puis Tir Coordonné (+2 attaque à tous).",
                    "Placement : centre de l'escouade (3 cases de tous) ; pistolet, jamais première lame.",
                },
            };
        }
    }
}
