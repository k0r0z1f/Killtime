using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Core.Character.Classes
{
    /// <summary>
    /// MOINE CINÉTIQUE — Corps-à-corps éveillé (Arts Martiaux + Corps Augmenté).
    /// Piliers FOR 5 / AGI 4, MAG 3 : la Magie remplace FOR/AGI en offensif.
    /// Fichier 4/8 des archétypes de base (un fichier par classe).
    /// </summary>
    public static class ClasseMoineCinetique
    {
        public static CharacterClassDefinition Build()
        {
            return new CharacterClassDefinition
            {
                ClassId = "moine-cinetique",
                DisplayName = "Moine Cinétique",
                Tagline = "Moine — poings, vitesse, refoulement",
                Description = "Poing éveillé (MAG 3, Corps Augmenté) : frappe au dé de Magie, enchaîne, téléporte sa frappe et refoule de 2 cases. Piliers FOR 5 / AGI 4. Le duel d'esquive incarné.",
                IconGlyph = "🥋",
                ThemeColor = new Color(0.2f, 0.85f, 1f),
                SuggestedSpecies = SpeciesType.Humain,
                Profile = CharacterProfileType.HerosPJ,
                // MAG>0 → secondaires 17 : CON3 RAP3 INT3 ERU3 CHA2 INS3 = 17 ✓
                BaseAttributes = new Attributes(@for: 5, agi: 4, con: 3, rap: 3, @int: 3, eru: 3, cha: 2, ins: 3, mag: 3),
                BaseArmor = 0,
                StartingCreditsCE = 6000,
                StartingTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.MainsNues, 2),
                    new SkillTrainingStep(SkillType.Esquive, 2),
                    new SkillTrainingStep(SkillType.Athletisme, 1),
                    new SkillTrainingStep(SkillType.EndurancePhysique, 1),
                },
                StartingSpecializations = new List<string>
                {
                    "Arts Martiaux",
                    "Leviers Densifiés",
                },
                BuildPath = new List<string>
                {
                    // Branche enchaînement (vitesse)
                    "Arts Martiaux",
                    "Arts Martiaux : Enchaînement Fluide",
                    "Arts Martiaux : Frappe Téléportée",
                    "Arts Martiaux : Déphasage Cinétique",
                    // Branche onde (zone)
                    "Arts Martiaux : Onde Tellurique",
                    "Arts Martiaux : Brise-Blindage",
                    "Arts Martiaux : Paume de Brum'korath",
                    // Branche clés (contrôle)
                    "Arts Martiaux : Balayage Rotatif",
                    "Arts Martiaux : Clé d'Articulation",
                    "Arts Martiaux : Rupture Ligamentaire",
                    // Branche densité (perforation)
                    "Leviers Densifiés",
                    "Leviers Densifiés : Perforation Osseuse",
                    "Leviers Densifiés : Pointe Cristalline",
                    "Leviers Densifiés : Ancrage Myologique",
                    "Leviers Densifiés : Poigne Titanesque",
                    // Branche teep (refoulement)
                    "Teep de Rupture",
                    "Teep de Rupture : Impact Dévastateur",
                    "Teep de Rupture : Onde de Choc Linéaire",
                    "Teep de Rupture : Interception d'Assaut",
                    "Teep de Rupture : Rebond Mural",
                    // Fondation mobilité
                    "Roulade de Décrochage",
                    "Roulade de Décrochage : Reprise d'Appui",
                },
                TargetTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.MainsNues, 3),
                    new SkillTrainingStep(SkillType.Esquive, 3),
                    new SkillTrainingStep(SkillType.Athletisme, 2),
                    new SkillTrainingStep(SkillType.MagiePrimale, 2),
                    new SkillTrainingStep(SkillType.EndurancePhysique, 1),
                },
                PlayTips = new List<string>
                {
                    "Corps Augmenté : en offensif mains nues, MAG remplace (FOR+AGI)/2 — montez MAG avant FOR.",
                    "Priorité XP : Mains Nues +3 (= d12 avec MAG 5), puis Enchaînement Fluide (-1 PA).",
                    "Combo : Teep (refoule 2 cases) → obstacle = 3 dégâts + Sonné gratuit.",
                },
            };
        }
    }
}
