using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// Registre officiel des entités colossales et Boss du Codex (Livre XI).
    /// </summary>
    public static class TitanBossCatalog
    {
        public static CharacterSheet BuildTheBeastSheet()
        {
            var attrs = new Attributes(
                @for: 7,
                agi: 5,
                con: 6,
                rap: 4,
                @int: 4,
                eru: 2,
                cha: 1,
                ins: 6,
                mag: 0
            );
            attrs.PointsMiracle = 3;

            var sheet = new CharacterSheet
            {
                Name = "La Bête d'Hybris",
                BaseAttributes = attrs,
                BaseArmor = 0,
                Profile = CharacterProfileType.PnjBoss,
                Footprint = TitanFootprintType.Rosette7,
                ModelPrefabName = "TheBeast",
                Species = SpeciesType.Humain,
                Gender = "Monstre",
                Age = 1200,
                LoreNotes = "[Espèce: Faune Tellurique Légendaire] Gardien initiatique millénaire des Falaises d'Haliriel. Colosse de 4 mètres à fourrure dense et crocs sabres."
            };

            sheet.GetSkill(SkillType.MainsNues).TrainingLevel = 3;
            sheet.GetSkill(SkillType.Athletisme).TrainingLevel = 2;
            sheet.GetSkill(SkillType.Observation).TrainingLevel = 2;

            sheet.UnlockedSpecializations.Add("Morsure Sabre");
            sheet.UnlockedSpecializations.Add("Balayage de Griffes en Arc");
            sheet.UnlockedSpecializations.Add("Mémoire des Flammes");
            sheet.UnlockedSpecializations.Add("Charge Tellurique des Falaises");

            return sheet;
        }

        public static CharacterSheet BuildDiscipleDeTerreSheet()
        {
            var attrs = new Attributes(
                @for: 9,
                agi: 2,
                con: 9,
                rap: 2,
                @int: 2,
                eru: 1,
                cha: 1,
                ins: 4,
                mag: 4
            );
            attrs.PointsMiracle = 0;

            var sheet = new CharacterSheet
            {
                Name = "Disciple de Terre de Capricius",
                BaseAttributes = attrs,
                BaseArmor = 6,
                Profile = CharacterProfileType.PnjBoss,
                Footprint = TitanFootprintType.Rosette7,
                ModelPrefabName = "DiscipleDeTerre",
                Species = SpeciesType.Humain,
                Gender = "Inorganique",
                Age = 100000,
                LoreNotes = "[Espèce: Missile Biologique d'Extinction] Titan lithosphérique de 3 mètres de roche noire et magma forgé pour purger les anomalies temporelles."
            };

            sheet.GetSkill(SkillType.MainsNues).TrainingLevel = 4;
            sheet.GetSkill(SkillType.DefenseCorporelle).TrainingLevel = 3;

            sheet.UnlockedSpecializations.Add("Poing Tectonique");
            sheet.UnlockedSpecializations.Add("Régénération Lithosphérique");
            sheet.UnlockedSpecializations.Add("Carapace de Magma");
            sheet.UnlockedSpecializations.Add("Vulnérabilité Choc Thermique");

            return sheet;
        }

        public static CharacterSheet BuildMinulicanSheet()
        {
            var attrs = new Attributes(
                @for: 6,
                agi: 6,
                con: 6,
                rap: 5,
                @int: 4,
                eru: 2,
                cha: 1,
                ins: 5,
                mag: 9
            );
            attrs.PointsMiracle = 3;

            var sheet = new CharacterSheet
            {
                Name = "Le Minulican",
                BaseAttributes = attrs,
                BaseArmor = 2,
                Profile = CharacterProfileType.PnjBoss,
                Footprint = TitanFootprintType.Rosette7,
                ModelPrefabName = "Minulican",
                Species = SpeciesType.Humain,
                Gender = "Néant",
                Age = 0,
                LoreNotes = "[Espèce: Entité Cosmique Chaotique] Le Loup Quantique du Néant traquant la variance biogénomique à travers le continuum."
            };

            sheet.GetSkill(SkillType.MainsNues).TrainingLevel = 3;
            sheet.GetSkill(SkillType.Esquive).TrainingLevel = 2;

            sheet.UnlockedSpecializations.Add("Éclairs de Néant en Chaîne");
            sheet.UnlockedSpecializations.Add("Absorption d'Énergie Thermique");
            sheet.UnlockedSpecializations.Add("Saut Quantique Déphasé");

            return sheet;
        }
    }
}