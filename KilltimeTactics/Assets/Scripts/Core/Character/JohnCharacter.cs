using System;
using Killtime.Core.Inventory;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Fiche héroïque de John — Le Passeur du Creuset Atomique.
    /// Classe unique : toute la logique propre à John réside ici (délégation propre),
    /// sur le même modèle que MinaCharacter / LucasCharacter / ThomasCharacter.
    ///
    /// PROFIL NARRATIF (Volume I, Scène 01 « Le creuset atomique ») :
    /// - Transporteur indépendant qui « revient vivant » : traverse les lignes impériales
    ///   là où les convois militaires se brisent. Pilote du Starlight Voyager.
    /// - Protecteur du « Messie Brisé » (Lucas enfant) avec Erika : négocie, renégocie
    ///   ou refuse le contrat de la Résistance (routes Vardis / indépendante / impériale).
    /// - Mundan polyvalent (MAG 0) SANS restriction d'âme : là où Thomas est verrouillé
    ///   hors de toute magie et Mina/Lucas hors de leurs voies opposées, John peut toucher
    ///   à tout — mais ne maîtrise en propre que la voie du Passeur (instinct, tir de
    ///   couverture, contrat, plan de vol). Le fiable de l'escouade, pas le prodige.
    /// </summary>
    public static class JohnCharacter
    {
        public const string SheetIdPrefix = "e8f2a419";
        public const string InnateSpecialization = "Revenir Vivant";

        public const string DisplayTag = "[John : Passeur du Creuset & Revenir Vivant]";
        public const string SectorName = "LE PASSEUR DU CREUSET ATOMIQUE";
        public const string SectorSubtitle = "Instinct Mundan : Survie, Tir de Couverture, Contrat & Plan de Vol (MAG 0)";
        public const string RestrictionLabel = "🔒 Inaccessible à John (Voie du Passeur)";

        // ------------------------------------------------------------------
        // IDENTITÉ
        // ------------------------------------------------------------------

        public static bool IsJohn(CharacterSheet sheet)
        {
            if (sheet == null) return false;
            if (!string.IsNullOrEmpty(sheet.Name) && sheet.Name.IndexOf("John", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(sheet.ModelPrefabName) && sheet.ModelPrefabName.IndexOf("John", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(sheet.SheetId) && sheet.SheetId.StartsWith(SheetIdPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        // ------------------------------------------------------------------
        // RESTRICTIONS D'ÂME (AUCUNE — le passeur touche à tout, sans génie arcanique)
        // ------------------------------------------------------------------

        public static bool IsSkillForbidden(SkillType skill)
        {
            // John n'a aucune affinité arcanique innée (MAG 0) mais aucune interdiction :
            // c'est le personnage de base polyvalent. Les voies héroïques des autres
            // (Mina / Lucas / Thomas) lui restent fermées via les exclusivités.
            return false;
        }

        public static string ForbiddenSkillMessage(SkillType skill)
        {
            // Non utilisé (aucune compétence interdite), conservé pour symétrie d'API.
            return $"[VOIE DU PASSEUR] John n'a aucune restriction d'âme : il peut s'entraîner à {skill}.";
        }

        public static string ExclusiveSpecializationMessage(string specializationName)
        {
            return $"[VOIE DU PASSEUR EXCLUSIVE] La maîtrise '{specializationName}' exige l'instinct du passeur de John (creuset atomique, Starlight Voyager, contrat du messie brisé).";
        }

        // ------------------------------------------------------------------
        // ENREGISTREMENT DES SPÉCIALISATIONS EXCLUSIVES DE JOHN
        // Appelé par CharacterProgressionManager.EnsureRegistryBuilt().
        // ------------------------------------------------------------------

        public static void RegisterSpecializations()
        {
            // --- INNÉ : Revenir Vivant (Intuition — sixième sens du passeur) ---
            CharacterProgressionManager.RegisterSpec(SkillType.Intuition, "Revenir Vivant", null, false, false,
                "Sixième sens du transporteur qui revient toujours (Inné — John).",
                "[Inné : Passeur] Une fois par tour, relance un jet d'Esquive ou de Discrétion raté d'un cran (garde le meilleur). Ne fonctionne que si John n'est ni Entravé ni À Terre.",
                isJohnExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Intuition, "Revenir Vivant : Lecture du Braconnier", "Revenir Vivant", false, false,
                "Lit le champ de bataille comme une piste de contrebande.",
                "Dépense 1 PA : révèle les embuscades et pièges à 4 cases et confère +2 en Observation contre eux pendant 1 tour.",
                isJohnExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Intuition, "Revenir Vivant : Passeur Insubmersible", "Revenir Vivant : Lecture du Braconnier", true, false,
                "Le contrat s'écrit, le passeur s'en sort.",
                "1 fois par combat : si John tombe à 0 PV, il reste à 1 PV, purge Étourdi/Déstabilisé et peut ramper gratuitement de 2 cases. Contrecoup : +1 Essoufflement.",
                isJohnExclusive: true);

            // --- TIR DE COUVERTURE DU PASSEUR (Ballistique — le hangar se défend) ---
            CharacterProgressionManager.RegisterSpec(SkillType.Ballistique, "Tir de Couverture du Passeur", null, false, false,
                "Discipline de tir du convoyeur sous pression (hangar, coursive, sas).",
                "En garde (1 PA) : la première cible ennemie qui entre dans sa ligne de tir à 8 cases subit un tir d'interception gratuit (dégâts normaux).",
                isJohnExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Ballistique, "Tir de Couverture du Passeur : Rideau de Fer du Hangar", "Tir de Couverture du Passeur", false, false,
                "Double pression sur la coursive.",
                "Le tir d'interception inflige Déstabilisé en plus des dégâts et couvre 2 cases de large au lieu d'une.",
                isJohnExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Ballistique, "Tir de Couverture du Passeur : Dernier Chargeur", "Tir de Couverture du Passeur : Rideau de Fer du Hangar", true, false,
                "Le chargeur qui ramène tout le monde.",
                "1 fois par combat : vide le chargeur en rafale (3 PA) — 3 tirs gratuits à 8 cases avec +2 dégâts chacun. Enrayement : Sonné au tour suivant sur échec critique.",
                isJohnExclusive: true);

            // --- CONTRAT DU PASSEUR (Communication — signer, renégocier, refuser) ---
            CharacterProgressionManager.RegisterSpec(SkillType.Communication, "Contrat du Passeur", null, false, false,
                "Art du deal sous blaster : fixer le prix du retour vivant.",
                "Hors combat : +2 en Négociation pour les contrats de transport et d'extraction. En combat : ordonne à un sbire de lâcher son arme ou reculer (Delta >= 2).",
                isJohnExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Communication, "Contrat du Passeur : Renégociation Forcée", "Contrat du Passeur", false, false,
                "La voie indépendante : le passager choisit où il va.",
                "Dépense 2 PA : un PNJ non-hostile à 4 cases devient neutre-bienveillant pendant 1 tour et révèle une information (mot de passe, patrouille, cache).",
                isJohnExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Communication, "Contrat du Passeur : Prix du Retour", "Contrat du Passeur : Renégociation Forcée", true, false,
                "« Vous ne payez pas un passage. Vous payez quelqu'un qui revient vivant. »",
                "1 fois par scénario : impose une renégociation totale — le groupe gagne un vaisseau, un sauf-conduit ou un paiement doublé, au prix d'une faction hostile future.",
                isJohnExclusive: true);

            // --- PLAN DE VOL DU STARLIGHT (Tactique/Stratégie — traverser les lignes) ---
            CharacterProgressionManager.RegisterSpec(SkillType.TactiqueStrategie, "Plan de Vol du Starlight", null, false, false,
                "Navigation de contrebande : routes civiles, balises éteintes, ciel d'orage.",
                "Le groupe ignore le premier contrôle, barrage ou inspection du scénario si John trace la route (préparation 2 PA).",
                isJohnExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.TactiqueStrategie, "Plan de Vol du Starlight : Route Sans Balise", "Plan de Vol du Starlight", false, false,
                "Vol sans traceur de la Résistance.",
                "La route tracée par John ne laisse aucune balise : +2 en Discrétion d'escouade pendant la traversée et immunité au pistage orbital pendant 1 tour.",
                isJohnExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.TactiqueStrategie, "Plan de Vol du Starlight : Traversée du Creuset", "Plan de Vol du Starlight : Route Sans Balise", true, false,
                "La manœuvre qui force le blocus impérial.",
                "1 fois par combat : exfiltration éclair — tous les alliés à 3 cases de John se déplacent gratuitement de 3 cases vers la sortie, sans attaque d'opportunité.",
                isJohnExclusive: true);
        }

        // ------------------------------------------------------------------
        // MAÎTRISE INNÉE
        // ------------------------------------------------------------------

        public static bool IsInnateUnlocked(string specializationName, CharacterSheet sheet)
        {
            if (!IsJohn(sheet)) return false;
            return string.Equals(specializationName, InnateSpecialization, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // FICHE HÉROÏQUE INTÉGRÉE (John_e8f2a419.json)
        // ------------------------------------------------------------------

        /// <summary>
        /// Construit la fiche héroïque officielle de John selon les règles
        /// exactes du Livre I (Profil HérosPJ : 1 pilier à 5, 1 à 4, 15 pts secondaires, max 3, MAG 0).
        /// Total = 24 points d'attributs stricts.
        /// </summary>
        public static CharacterSheet BuildHeroicSheet()
        {
            var savedFiles = CharacterStorageService.GetSavedCharacterFiles();
            for (int i = 0; i < savedFiles.Count; i++)
            {
                string filePath = savedFiles[i];
                string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                if (fileName.IndexOf(SheetIdPrefix, StringComparison.OrdinalIgnoreCase) >= 0
                    || fileName.StartsWith("John_", StringComparison.OrdinalIgnoreCase))
                {
                    var loaded = CharacterStorageService.LoadCharacter(filePath);
                    if (loaded != null)
                    {
                        // Migration : remplace les objets hors-catalogue des saves
                        // antérieures par leurs équivalents Armurerie/Marché.
                        if (ArmoryCatalog.MigrateLegacyScene01Items(loaded) > 0)
                            CharacterStorageService.SaveCharacter(loaded);
                        return loaded;
                    }
                }
            }

            // Répartition conforme HérosPJ (Livre I) — passeur/tireur/négociateur :
            // Piliers : AGI 5 (tir), RAP 4 (réflexes de pilote)
            // Secondaires (somme = 15, max 3) : FOR 3, CON 3, INT 2, ÉRU 1, CHA 3, INS 3, MAG 0
            // PA = Max(3, 2) + 3 + Min(1..5) = 3 + 3 + 1 = 7 PA (base mundan, voir CoreRules).
            var sheet = new CharacterSheet
            {
                SheetId = "e8f2a4197c3d4a5e9b1f2a3c4d5e6f708",
                Name = "John",
                Age = 34,
                Gender = "Masculin",
                Species = SpeciesType.Humain,
                Profile = CharacterProfileType.HerosPJ,
                ModelPrefabName = "John",
                LoreNotes = "John, 34 ans. Transporteur indépendant du Creuset Atomique, pilote du Starlight Voyager. « Vous ne payez pas un passage. Vous payez quelqu'un qui revient vivant. » Protecteur du Messie Brisé (Lucas enfant) avec Erika : signataire, renégociateur ou refuseur du contrat de la Résistance (routes Vardis / indépendante / impériale). Mundan polyvalent (MAG 0) sans restriction d'âme : fiable là où les prodiges se brisent.",
                BaseAttributes = new Attributes(
                    @for: 3, agi: 5, con: 3, rap: 4,
                    @int: 2, eru: 1, cha: 3, ins: 3,
                    mag: 0, vision: 3, ouie: 3, miracle: 1),
                BaseArmor = 1,
                AvailableXP = 0,
                TotalEarnedXP = 50,
                TotalSpentXP = 50,
                FreeTrainingsUsed = 1,
                CreditsCE = 8000
            };

            // Entraînements initiaux (Livre I §5 : 1 gratuit Érudition + 6 payants = 30 XP)
            sheet.GetSkill(SkillType.Ballistique).TrainingLevel = 2;
            sheet.GetSkill(SkillType.ConduitePilotage).TrainingLevel = 1;
            sheet.GetSkill(SkillType.Communication).TrainingLevel = 1;
            sheet.GetSkill(SkillType.Discretion).TrainingLevel = 1;
            sheet.GetSkill(SkillType.NatureSurvie).TrainingLevel = 1;
            sheet.GetSkill(SkillType.Intuition).TrainingLevel = 1;

            // Spécialisations débloquées (Inné gratuit + 4 voies du passeur = 20 XP)
            sheet.UnlockedSpecializations.Add("Revenir Vivant");
            sheet.UnlockedSpecializations.Add("Tir de Couverture du Passeur");
            sheet.UnlockedSpecializations.Add("Contrat du Passeur");
            sheet.UnlockedSpecializations.Add("Plan de Vol du Starlight");
            sheet.UnlockedSpecializations.Add("Pistolet & Tir Rapide");

            // Équipement officiel — dotation catalogue Armurerie/Marché (Livre VIII) :
            // le Passeur tire au pistolet laser (Tir de Couverture, hangars du Creuset).
            if (!ArmoryCatalog.GiveLoadoutItem(sheet, "Pistolet Léger Ivoire", false))
            {
                // Repli si catalogue indisponible (ne devrait jamais arriver).
                sheet.Inventory.Add(new InventoryItem
                {
                    Name = "Pistolet Lourd du Passeur",
                    Type = ItemType.Weapon,
                    EquipSlot = ItemEquipSlot.MainHand,
                    IsEquipped = false,
                    BaseDamage = 7,
                    RangeInTiles = 8,
                    AssociatedSkill = SkillType.Ballistique,
                    WeightKg = 1.6f,
                    Description = "Pistolet lourd de convoyeur, réglé pour le tir de couverture dans les hangars et coursives du Creuset.",
                    PriceCE = 450
                });
            }
            else
            {
                ArmoryCatalog.GiveLoadoutItem(sheet, "Charge Laser Standard (x10)", false);
            }

            return sheet;
        }
    }
}
