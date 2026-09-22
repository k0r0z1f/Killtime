using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Core.Character.Classes
{
    /// <summary>
    /// MAGE ÉLÉMENTALISTE — Éveillé de la 5e Force (feu / air / eau / terre + esprit).
    /// Piliers INT 5 / ÉRU 4, MAG 4. Artillerie arcanique (XP = PA).
    /// Fichier 3/8 des archétypes de base (un fichier par classe).
    /// </summary>
    public static class ClasseMageElementaliste
    {
        public static CharacterClassDefinition Build()
        {
            return new CharacterClassDefinition
            {
                ClassId = "mage-elementaliste",
                DisplayName = "Mage Élémentaliste",
                Tagline = "Mage — feu, air, terre, esprit",
                Description = "Éveillé (MAG 4) : incinère (Pyro), cisaille (Aéro), enterre (Géo) et verrouille (Télékinésie). Piliers INT 5 / ÉRU 4. Fragile au contact — à protéger.",
                IconGlyph = "🔥",
                ThemeColor = new Color(0.75f, 0.35f, 1f),
                SuggestedSpecies = SpeciesType.Cleien,
                Profile = CharacterProfileType.HerosPJ,
                // MAG>0 → secondaires 17 : FOR3 AGI3 CON3 RAP3 CHA2 INS3 = 17 ✓
                BaseAttributes = new Attributes(@for: 3, agi: 3, con: 3, rap: 3, @int: 5, eru: 4, cha: 2, ins: 3, mag: 4),
                BaseArmor = 0,
                StartingCreditsCE = 8000,
                StartingTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.MagieElementale, 2),
                    new SkillTrainingStep(SkillType.MagieEsprit, 1),
                    new SkillTrainingStep(SkillType.TactiqueStrategie, 1),
                    new SkillTrainingStep(SkillType.Esquive, 1),
                },
                StartingSpecializations = new List<string>
                {
                    "Incinération Pyrocinétique",
                    "Aéromancie",
                },
                BuildPath = new List<string>
                {
                    // Branche feu (dégâts)
                    "Incinération Pyrocinétique",
                    "Incinération : Flamme Bleue",
                    "Incinération : Fournaise Déferlante",
                    "Incinération : Nova Thermique",
                    // Branche air (contrôle)
                    "Aéromancie",
                    "Aéromancie : Lame de Vent",
                    "Aéromancie : Tempête de Lames",
                    "Aéromancie : Courant Porteur",
                    // Branche terre (encaissement / zone)
                    "Géomancie",
                    "Géomancie : Poing Tellurique",
                    "Géomancie : Séisme Localisé",
                    "Géomancie : Peau de Pierre",
                    // Branche eau (entrave)
                    "Hydromancie",
                    "Hydromancie : Étreinte Abyssale",
                    "Hydromancie : Raz-de-Marée",
                    "Hydromancie : Brume Aveuglante",
                    // Branche esprit (défense)
                    "Télékinésie",
                    "Télékinésie : Projection d'Objets",
                    "Télékinésie : Barrière Cinétique",
                    "Télékinésie : Fissure Gravifique",
                },
                TargetTrainings = new List<SkillTrainingStep>
                {
                    new SkillTrainingStep(SkillType.MagieElementale, 3),
                    new SkillTrainingStep(SkillType.MagieEsprit, 2),
                    new SkillTrainingStep(SkillType.TactiqueStrategie, 1),
                    new SkillTrainingStep(SkillType.Esquive, 2),
                },
                PlayTips = new List<string>
                {
                    "Loi d'airain : Coût XP = Coût PA (Livre IV) — forgez peu de sorts, mais décisifs.",
                    "Priorité XP : Magie Élémentale +3, puis Flamme Bleue (ignore armure).",
                    "Placement : 4 cases derrière la ligne, Barrière Cinétique contre les snipers.",
                },
            };
        }
    }
}
