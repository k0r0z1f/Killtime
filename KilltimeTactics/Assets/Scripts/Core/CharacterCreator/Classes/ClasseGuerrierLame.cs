using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Core.Character.Classes
{
    /// <summary>
    /// GUERRIER LAME — Fighter de première ligne (épée + bouclier / marteau).
    /// Piliers FOR 5 / CON 4. Encaisse, pare sans malus, riposte et renverse.
    /// Fichier 1/8 des archétypes de base (un fichier par classe).
    /// </summary>
    public static class ClasseGuerrierLame
    {
        public static CharacterClassDefinition Build()
        {
            return new CharacterClassDefinition
            {
                ClassId = "guerrier-lame",
                DisplayName = "Guerrier Lame",
                Tagline = "Fighter — épée, parade, riposte",
                Description = "Fantassin de ligne d'Hybris : encaisse (CON 4), pare sans malus (Maniement de l'Épée / Bloquer) et punit en riposte. Idéal pour découvrir le duel aveugle et la localisation.",
                IconGlyph = "⚔️",
                ThemeColor = new Color(0.95f, 0.25f, 0.35f),
                SuggestedSpecies = SpeciesType.Mikyai,
                Profile = CharacterProfileType.HerosPJ,
                BaseAttributes = new Attributes(@for: 5, agi: 3, con: 4, rap: 3, @int: 2, eru: 3, cha: 1, ins: 3, mag: 0),
                BaseArmor = 2,
                StartingCreditsCE = 8000,
                StartingTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.ManiementArmes, 2),
                    new SkillTrainingStep(SkillType.DefenseCorporelle, 1),
                    new SkillTrainingStep(SkillType.Athletisme, 1),
                    new SkillTrainingStep(SkillType.EndurancePhysique, 1),
                },
                StartingSpecializations = new List<string>
                {
                    "Maniement de l'Épée",
                    "Bloquer",
                },
                BuildPath = new List<string>
                {
                    // Branche lame (riposte)
                    "Maniement de l'Épée",
                    "Maniement de l'Épée : Riposte Éclair",
                    "Maniement de l'Épée : Tranchant Tempétueux",
                    "Maniement de l'Épée : Lame de Ligne Temporelle Zéro",
                    // Branche garde haute (anti-tir)
                    "Maniement de l'Épée : Garde Haute Impériale",
                    "Maniement de l'Épée : Lame Miroir",
                    "Maniement de l'Épée : Dôme de Parade",
                    // Branche bouclier (tank)
                    "Bloquer",
                    "Bloquer : Mur de Bouclier",
                    "Bloquer : Heurt de Bouclier",
                    "Bloquer : Forteresse Impénétrable",
                    // Branche marteau (option anti-blindé)
                    "Armes Contondantes",
                    "Marteau de Guerre",
                    "Marteau de Guerre : Écrasement Osseux",
                    "Marteau de Guerre : Brise-Crâne",
                    "Marteau de Guerre : Cataclysme de Fer",
                    // Fondation survie
                    "Condition de Fer",
                    "Condition de Fer : Dur à Cuire",
                    "Condition de Fer : Mur de Chair",
                },
                TargetTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.ManiementArmes, 3),
                    new SkillTrainingStep(SkillType.DefenseCorporelle, 3),
                    new SkillTrainingStep(SkillType.Athletisme, 2),
                    new SkillTrainingStep(SkillType.EndurancePhysique, 2),
                    new SkillTrainingStep(SkillType.Intimidation, 1),
                },
                PlayTips = new List<string>
                {
                    "Réflexe : parade à l'épée (sans malus) puis riposte — ne jamais esquiver en armure lourde.",
                    "Priorité XP : Maniement d'Arme +3 avant de creuser la branche marteau.",
                    "Équipement : épée longue + pavois ; viser Torse puis Tête (Brise-Crâne).",
                },
            };
        }
    }
}
