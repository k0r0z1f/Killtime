using System;
using Killtime.Core.Inventory;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Fiche héroïque d'Erika de Cleya — La Flamme Cleyane & Lame d'Escorte.
    /// Classe unique : toute la logique propre à Erika réside ici (délégation propre),
    /// sur le même modèle que MinaCharacter / LucasCharacter / ThomasCharacter / JohnCharacter.
    ///
    /// PROFIL NARRATIF (Volume I, Scène 01 « Le creuset atomique » + manuscrit) :
    /// - Représentante Cleyane à la Résistance : yeux aiguisés à l'intensité tranquille
    ///   de son peuple, cheveux tentaculaires courts (nuque) aux reflets de braise,
    ///   nature de feu à peine contenue, main instinctive sur sa lame.
    /// - Binôme de John à bord du Starlight Voyager : elle soutient, renégocie ou suit
    ///   (routes Vardis / indépendante / impériale), protectrice du Messie Brisé.
    /// - Mage élémentaliste du feu (MAG 5) : là où Lucas SOUSTRAIT (froid calculé),
    ///   Erika EMBRASE (pyromancie Cleyane frontale). Aucune affinité primale ni spirituelle :
    ///   seule la flamme élémentale résonne en elle (miroir de Mina, qui ne jure que par le primal).
    /// </summary>
    public static class ErikaCharacter
    {
        public const string SheetIdPrefix = "4b9e2d7f";
        public const string InnateSpecialization = "Flamme Cleyane";

        public const string DisplayTag = "[Erika de Cleya : Flamme Cleyane & Lame d'Escorte]";
        public const string SectorName = "LA FLAMME CLEYANE D'ERIKA";
        public const string SectorSubtitle = "Cinquième Force (MAG) : Pyromancie Frontale & Lame d'Escorte (Cleyane)";
        public const string RestrictionLabel = "🔒 Inaccessible à Erika (Voie Cleyane du Feu)";

        // ------------------------------------------------------------------
        // IDENTITÉ
        // ------------------------------------------------------------------

        public static bool IsErika(CharacterSheet sheet)
        {
            if (sheet == null) return false;
            if (!string.IsNullOrEmpty(sheet.Name) && sheet.Name.IndexOf("Erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(sheet.ModelPrefabName) && sheet.ModelPrefabName.IndexOf("Erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(sheet.SheetId) && sheet.SheetId.StartsWith(SheetIdPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        // ------------------------------------------------------------------
        // RESTRICTIONS D'ÂME (FEU ÉLÉMENTAL UNIQUEMENT)
        // ------------------------------------------------------------------

        public static bool IsSkillForbidden(SkillType skill)
        {
            return skill == SkillType.MagiePrimale || skill == SkillType.MagieEsprit;
        }

        public static string ForbiddenSkillMessage(SkillType skill)
        {
            return $"[RESTRICTION D'ÂME] Erika ne possède aucune affinité primale (chair, végétal) ni spirituelle (télépathie). Seule la flamme élémentale Cleyane résonne en elle.";
        }

        public static string ForbiddenSpellMessage()
        {
            return $"[RESTRICTION D'ÂME] Erika ne peut graver aucun sort primal (chair, végétal) ni spirituel (télépathie, télékinésie). Sa voie est la flamme élémentale.";
        }

        public static string ExclusiveSpecializationMessage(string specializationName)
        {
            return $"[FLAMME CLEYANE EXCLUSIVE] La maîtrise '{specializationName}' exige le sang-chaud et la pyromancie frontale d'Erika de Cleya (tendrils de braise, lame d'escorte du Starlight).";
        }

        // ------------------------------------------------------------------
        // ENREGISTREMENT DES SPÉCIALISATIONS EXCLUSIVES D'ERIKA
        // Appelé par CharacterProgressionManager.EnsureRegistryBuilt().
        // ------------------------------------------------------------------

        public static void RegisterSpecializations()
        {
            // --- INNÉ : Flamme Cleyane (Magie Élémentale — feu frontal, pas soustraction) ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagieElementale, "Flamme Cleyane", null, false, false,
                "Pyromancie frontale du peuple de Cléia (Inné — Erika).",
                "[Inné : Braise] Les épreuves de Magie Élémentale de feu gagnent +1 palier de dé et les dégâts de feu d'Erika ignorent 2 points d'armure.",
                isErikaExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.MagieElementale, "Flamme Cleyane : Doigts de Braise", "Flamme Cleyane", false, false,
                "Étincelles au bout des doigts, colère contenue.",
                "Attaque à 4 cases (1 PA) : 3 dégâts de feu + applique En Feu sur Delta >= 2.",
                isErikaExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.MagieElementale, "Flamme Cleyane : Fournaise Cleyane", "Flamme Cleyane : Doigts de Braise", true, false,
                "Le feu dans les yeux devient tempête.",
                "1 fois par combat (3 PA) : zone de 2 cases, 8 dégâts de feu, En Feu sur tous, purge Gelé/Ralenti des alliés. Contrecoup : +1 Essoufflement.",
                isErikaExclusive: true);

            // --- VOILE DE CENDRES (défense & diversion par le feu) ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagieElementale, "Voile de Cendres", null, false, false,
                "Cendre et fumée chaude plutôt que flamme vive.",
                "Dépense 1 PA : écran de cendres sur un hexagone à 4 cases — Couvert 3/4 pendant 1 tour.",
                isErikaExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.MagieElementale, "Voile de Cendres : Rideau Aveuglant", "Voile de Cendres", false, false,
                "Mur gris qui pique les yeux et brouille les viseurs.",
                "Le voile impose -2 à toutes les attaques adverses le traversant et purge l'état En Feu des alliés abrités.",
                isErikaExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.MagieElementale, "Voile de Cendres : Cendre Mémoire", "Voile de Cendres : Rideau Aveuglant", true, false,
                "La cendre garde l'empreinte de la bataille.",
                "1 fois par combat : le voile révèle positions et intentions (comme Lecture du Braconnier) en plus de protéger, pendant 2 tours.",
                isErikaExclusive: true);

            // --- LAME D'ESCORTE (la main instinctive sur sa lame) ---
            CharacterProgressionManager.RegisterSpec(SkillType.ManiementArmes, "Lame d'Escorte", null, false, false,
                "Escrime rapprochée de garde du corps (convoyeur du Messie Brisé).",
                "Garde (1 PA) : la première attaque de mêlée visant un allié adjacent est parée gratuitement (test ManiementArmes).",
                isErikaExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.ManiementArmes, "Lame d'Escorte : Riposte Enflammée", "Lame d'Escorte", false, false,
                "La parade porte la braise.",
                "Toute parade réussie de la Lame d'Escorte rend 3 dégâts de feu à l'assaillant et applique En Feu sur critique.",
                isErikaExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.ManiementArmes, "Lame d'Escorte : Jugement du Creuset", "Lame d'Escorte : Riposte Enflammée", true, false,
                "La sentence de l'escorte ne rate jamais deux fois.",
                "1 fois par combat : contre-attaque automatique dévastatrice (dégâts doublés, ignore 4 armure) contre l'ennemi qui a blessé un allié ce tour.",
                isErikaExclusive: true);

            // --- SANG-CHAUD CLEYAN (la physiologie qui brille sous la colère) ---
            CharacterProgressionManager.RegisterSpec(SkillType.EndurancePhysique, "Sang-Chaud Cleyan", null, false, false,
                "Le sang Cleyan court chaud : les tendrils se hérissent, la peau luit.",
                "Ignore les malus de froid et de Gelé ; +1 Seuil d'Encaissement quand Erika est En Feu (sans en subir les dégâts).",
                isErikaExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.EndurancePhysique, "Sang-Chaud Cleyan : Fièvre Combative", "Sang-Chaud Cleyan", false, false,
                "La colère contenue devient carburant.",
                "Chaque statut En Feu purgé (subi ou allié) rend +1 PA immédiat à Erika (max +2 par tour).",
                isErikaExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.EndurancePhysique, "Sang-Chaud Cleyan : Cœur de Fournaise", "Sang-Chaud Cleyan : Fièvre Combative", true, false,
                "Le cœur bat comme un réacteur du Starlight.",
                "1 fois par combat : purge tous les statuts, restaure 2 PA et embrase la lame (+4 dégâts de feu pendant 2 tours).",
                isErikaExclusive: true);
        }

        // ------------------------------------------------------------------
        // MAÎTRISE INNÉE
        // ------------------------------------------------------------------

        public static bool IsInnateUnlocked(string specializationName, CharacterSheet sheet)
        {
            if (!IsErika(sheet)) return false;
            return string.Equals(specializationName, InnateSpecialization, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // FICHE HÉROÏQUE INTÉGRÉE (Erika_4b9e2d7f.json)
        // ------------------------------------------------------------------

        /// <summary>
        /// Construit la fiche héroïque officielle d'Erika (miroir éveillé de Mina/Lucas :
        /// 26 pts hors MAG + MAG 5, build de pyromancienne d'escorte).
        /// </summary>
        public static CharacterSheet BuildHeroicSheet()
        {
            var savedFiles = CharacterStorageService.GetSavedCharacterFiles();
            for (int i = 0; i < savedFiles.Count; i++)
            {
                string filePath = savedFiles[i];
                string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                if (fileName.IndexOf(SheetIdPrefix, StringComparison.OrdinalIgnoreCase) >= 0
                    || fileName.StartsWith("Erika_", StringComparison.OrdinalIgnoreCase))
                {
                    var loaded = CharacterStorageService.LoadCharacter(filePath);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }

            // Répartition éveillée (miroir Mina/Lucas : 26 pts hors MAG + MAG 5) :
            // Piliers : AGI 4 (lame), RAP 4 (réflexes), INT 4 (tactique d'escorte)
            // Secondaires : FOR 2, CON 3, ÉRU 3, CHA 3, INS 3 (+ MAG 5)
            // Espèce Cleien : FOR 1 / CON 2 effectifs, INT 5 / ÉRU 4 / INS 4 effectifs.
            var sheet = new CharacterSheet
            {
                SheetId = "4b9e2d7fa1c04e6b8d2f5a3c7e9b1064",
                Name = "Erika de Cleya",
                Age = 25,
                Gender = "Féminin",
                Species = SpeciesType.Cleien,
                Profile = CharacterProfileType.HerosPJ,
                ModelPrefabName = "Erika",
                LoreNotes = "Erika de Cleya, 25 ans. Représentante Cleyane à la Résistance, binôme de John à bord du Starlight Voyager. Yeux aiguisés à l'intensité tranquille de son peuple, tendrils courts aux reflets de braise, nature de feu à peine contenue. Pyromancienne frontale (là où Lucas soustrait le froid, elle embrase) et lame d'escorte du Messie Brisé.",
                BaseAttributes = new Attributes(
                    @for: 2, agi: 4, con: 3, rap: 4,
                    @int: 4, eru: 3, cha: 3, ins: 3,
                    mag: 5, vision: 3, ouie: 3, miracle: 1),
                BaseArmor = 1,
                AvailableXP = 0,
                TotalEarnedXP = 30,
                TotalSpentXP = 30,
                FreeTrainingsUsed = 3,
                CreditsCE = 8000
            };

            // Entraînements initiaux (Livre I §5 : budget Érudition effective 4 —
            // 6 niveaux au total dont 4 gratuits + 2 payants = 10 XP).
            sheet.GetSkill(SkillType.MagieElementale).TrainingLevel = 2;
            sheet.GetSkill(SkillType.ManiementArmes).TrainingLevel = 1;
            sheet.GetSkill(SkillType.Intuition).TrainingLevel = 1;
            sheet.GetSkill(SkillType.Discretion).TrainingLevel = 1;
            sheet.GetSkill(SkillType.EndurancePhysique).TrainingLevel = 1;

            // Spécialisations débloquées (Inné gratuit + 4 voies Cleyanes payantes = 20 XP).
            sheet.UnlockedSpecializations.Add("Flamme Cleyane");
            sheet.UnlockedSpecializations.Add("Voile de Cendres");
            sheet.UnlockedSpecializations.Add("Lame d'Escorte");
            sheet.UnlockedSpecializations.Add("Sang-Chaud Cleyan");
            sheet.UnlockedSpecializations.Add("Maniement de l'Épée");

            // Équipement officiel
            sheet.Inventory.Add(new InventoryItem
            {
                Name = "Lame d'Escorte Cleyane",
                Type = ItemType.Weapon,
                EquipSlot = ItemEquipSlot.MainHand,
                IsEquipped = true,
                BaseDamage = 6,
                RangeInTiles = 1,
                AssociatedSkill = SkillType.ManiementArmes,
                WeightKg = 1.4f,
                Description = "Lame courte Cleyane au fil constellé de braise, portée par Erika depuis les guerres clandestines.",
                PriceCE = 400
            });

            return sheet;
        }
    }
}
