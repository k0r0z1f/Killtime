using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Core.Character.Classes
{
    /// <summary>
    /// OMBRE INFILTRATRICE — Rogue (dague, jugulaire, discrétion, arc silencieux).
    /// Piliers AGI 5 / RAP 4. Tue sans alerte, saigne, disparaît.
    /// Fichier 7/8 des archétypes de base (un fichier par classe).
    /// </summary>
    public static class ClasseOmbreInfiltratrice
    {
        public static CharacterClassDefinition Build()
        {
            return new CharacterClassDefinition
            {
                ClassId = "ombre-infiltratrice",
                DisplayName = "Ombre Infiltratrice",
                Tagline = "Rogue — dague, jugulaire, arc",
                Description = "Lame de l'ombre : Escrime sans malus, Frappe Jugulaire qui saigne, Tir à l'Arc silencieux. Piliers AGI 5 / RAP 4. Le cauchemar des snipers isolés.",
                IconGlyph = "🗡️",
                ThemeColor = new Color(0.55f, 0.6f, 0.75f),
                SuggestedSpecies = SpeciesType.Taurien,
                Profile = CharacterProfileType.HerosPJ,
                // 5/4 + secondaires 15 : FOR2 CON2 INT3 ERU2 CHA3 INS3 = 15 ✓
                BaseAttributes = new Attributes(@for: 2, agi: 5, con: 2, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 0),
                BaseArmor = 0,
                StartingCreditsCE = 8000,
                StartingTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.ArmesPercantes, 2),
                    new SkillTrainingStep(SkillType.Discretion, 2),
                    new SkillTrainingStep(SkillType.Ballistique, 1),
                    new SkillTrainingStep(SkillType.Esquive, 1),
                },
                StartingSpecializations = new List<string>
                {
                    "Escrime",
                    "Frappe Jugulaire",
                },
                BuildPath = new List<string>
                {
                    // Branche escrime (duel)
                    "Escrime",
                    "Escrime : Riposte à l'Estoc",
                    "Escrime : Fleuret Éclair",
                    "Escrime : Frappe Fantôme",
                    "Escrime : Désarmement Fleuret",
                    "Escrime : Entaille Précise",
                    // Branche jugulaire (assassinat)
                    "Frappe Jugulaire",
                    "Frappe Jugulaire : Hémorragie Profonde",
                    "Frappe Jugulaire : Asphyxie Sanguine",
                    "Frappe Jugulaire : Tranche-Artère Silencieux",
                    // Branche arc (silencieux)
                    "Tir à l'Arc",
                    "Tir à l'Arc : Flèche Perforante",
                    "Tir à l'Arc : Tir en Cloche",
                    "Tir à l'Arc : Pluie d'Acier",
                    "Tir à l'Arc : Tir Instinctif",
                    "Tir à l'Arc : Chasseur Silencieux",
                    // Branche fuite
                    "Roulade de Décrochage",
                    "Roulade de Décrochage : Reprise d'Appui",
                    "Roulade de Décrochage : Ombre Évasive",
                    "Tromper",
                    "Tromper : Feinte Oratoire",
                },
                TargetTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.ArmesPercantes, 3),
                    new SkillTrainingStep(SkillType.Discretion, 3),
                    new SkillTrainingStep(SkillType.Ballistique, 2),
                    new SkillTrainingStep(SkillType.Esquive, 2),
                    new SkillTrainingStep(SkillType.Communication, 1),
                },
                PlayTips = new List<string>
                {
                    "Tranche-Artère : si dégâts nets > encaissement = mort instantanée sans jet de survie.",
                    "Priorité XP : Armes Perçantes +3, puis Hémorragie Profonde (saignement doublé).",
                    "Tactique : ombres + couvert, jamais deux tours au même endroit ; arc avant dague.",
                },
            };
        }
    }
}
