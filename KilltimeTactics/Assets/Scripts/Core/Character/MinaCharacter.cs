using System;
using Killtime.Core.Inventory;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Fiche héroïque de Mina (Mina-0) — Atavisme cinétique & Éveil Primordial.
    /// Extrait des fichiers de base : toute la logique spécifique à Mina vit ici.
    /// Les fichiers de base délèguent vers cette classe (délégation propre).
    /// </summary>
    public static class MinaCharacter
    {
        public const string SheetIdPrefix = "a7679993";
        public const string InnateSpecialization = "Corps Augmenté";
        public const string HeartOfBloomSpecialization = "Catalyse Tissulaire : Cœur d'Éclosion de Mina-0";
        public const string AvatarSpecialization = "Éveil Chlorophyllien : Avatar de Minalia Primordiale";

        public const string DisplayTag = "[Mina-0 : Atavisme & Éveil Primordial]";
        public const string SectorName = "LE ROYAUME PRIMORDIAL DE MINALIA";
        public const string SectorSubtitle = "Cinquième Force (MAG) : Atavisme Corporel (Phase 1) & Biokinésie Végétale (Phase 2)";
        public const string RestrictionLabel = "🔒 Inaccessible à Mina (Affinité Primordiale Pure)";

        // ------------------------------------------------------------------
        // IDENTITÉ
        // ------------------------------------------------------------------

        public static bool IsMina(CharacterSheet sheet)
        {
            if (sheet == null) return false;
            if (!string.IsNullOrEmpty(sheet.Name) && sheet.Name.IndexOf("Mina", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(sheet.ModelPrefabName) && sheet.ModelPrefabName.IndexOf("Mina", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(sheet.SheetId) && sheet.SheetId.StartsWith(SheetIdPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        // ------------------------------------------------------------------
        // RESTRICTIONS D'ÂME
        // ------------------------------------------------------------------

        public static bool IsSkillForbidden(SkillType skill)
        {
            return skill == SkillType.MagieElementale || skill == SkillType.MagieEsprit;
        }

        public static string ForbiddenSkillMessage(SkillType skill)
        {
            return $"[RESTRICTION D'ÂME] Mina ne possède aucune affinité avec la magie élémentale ou de l'esprit. Seule la Magie Primordiale résonne en elle.";
        }

        public static string ExclusiveSpecializationMessage(string specializationName)
        {
            return $"[AFFINITÉ BIOLOGIQUE EXCLUSIVE] La maîtrise '{specializationName}' est intimement liée à la physiologie cellulaire de Mina-0.";
        }

        // ------------------------------------------------------------------
        // ENREGISTREMENT DES SPÉCIALISATIONS EXCLUSIVES (MAGIE PRIMORDIALE)
        // Appelé par CharacterProgressionManager.EnsureRegistryBuilt().
        // ------------------------------------------------------------------

        public static void RegisterSpecializations()
        {
            // --- Inné Phase 1 : Corps Augmenté ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Corps Augmenté", null, false, false,
                "Résonance de la Cinquième Force dans les fibres musculaires (Inné — Mina).",
                "[Phase 1 : Impact] Substitue Magie (MAG 5) à Force (FOR 3) pour toutes les attaques à mains nues, propulsant le dé offensif à D12.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Corps Augmenté : Résonance Cinétique Pure", "Corps Augmenté", false, false,
                "Transfert d'énergie quantique intégrale à l'impact.",
                "Ajoute +2 Dégâts bruts sur toute frappe à mains nues réussie.",
                isMinaExclusive: true);

            // --- Extension exclusive : Barricade de Racines (Minalia) ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Barricade de Racines : Rempart de Silice Végétale", "Barricade de Racines : Épines de Granit", false, true,
                "Intégration minérale dans le bois arcanique de Mina.",
                "Réduit de 2 dégâts bruts tous les tirs ennemis traversant l'hexagone barricadé.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Barricade de Racines : Palissade Chlorophyllienne", "Barricade de Racines : Rempart de Silice Végétale", false, true,
                "Extension continue du rempart sur l'axe défensif.",
                "La barricade se déploie sur 3 cases contiguës conférant un Couvert 3/4 complet.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Barricade de Racines : Arbre-Citadelle de Minalia", "Barricade de Racines : Palissade Chlorophyllienne", true, true,
                "Sanctuaire protecteur vivant de Mina-0.",
                "Érige un dôme végétal absorbant tous les tirs et soignant les alliés abrités de 2 PV par tour.",
                isMinaExclusive: true);

            // --- Extension exclusive : Vignes Constrictrices ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Vignes Constrictrices : Vrilles Souterraines", "Vignes Constrictrices : Étau Végétal", false, true,
                "Progression des lianes sous la dalle tactique.",
                "Permet d'entraver une cible jusqu'à 6 cases sans nécessiter de ligne de vue directe.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Vignes Constrictrices : Épines Neurotoxiques", "Vignes Constrictrices : Vrilles Souterraines", false, true,
                "Sécrétion de toxines stupéfiantes.",
                "Applique Étourdi et Paralysé à la cible entravée pendant 1 tour.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Vignes Constrictrices : Lierre Causal de Brum'korath", "Vignes Constrictrices : Épines Neurotoxiques", true, true,
                "Entrave temporelle végétale absolue.",
                "Enlace la cible dans une boucle temporelle de racines, drainant 3 PA au début de son tour.",
                isMinaExclusive: true);

            // --- Extension exclusive : Catalyse Tissulaire ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Catalyse Tissulaire : Régénération Osmotique", "Catalyse Tissulaire : Surcroît Vital", false, true,
                "Diffusion cellulaire en chaîne.",
                "Le soin se propage automatiquement à un second allié situé à moins de 2 cases.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Catalyse Tissulaire : Cautérisation Sèveuse", "Catalyse Tissulaire : Régénération Osmotique", false, true,
                "Enduit cicatrisant thermique.",
                "Purge immédiatement les états En Feu, Asphyxie et Empoisonné en déposant une résine protectrice.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Catalyse Tissulaire : Cœur d'Éclosion de Mina-0", "Catalyse Tissulaire : Cautérisation Sèveuse", true, true,
                "Éveil cellulaire phénix exclusif à Mina.",
                "1 fois par combat : si Mina tombe à 0 PV, elle éclôt immédiatement avec 50% de ses PV max et tous statuts purgés.",
                isMinaExclusive: true);

            // --- Extension exclusive : Champ de Ronces ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Champ de Ronces : Rosier Noir d'Hybris", "Champ de Ronces : Ronces Neurotoxiques", false, true,
                "Éclosion de corolles aux spores d'ombre.",
                "Libère un brouillard de spores occultant la vision et imposant -2 à toutes les attaques adverses.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Champ de Ronces : Tapis de Racines Épineuses", "Champ de Ronces : Rosier Noir d'Hybris", false, true,
                "Herse végétale dense.",
                "Double le coût en PA pour tout déplacement ennemi franchissant la zone.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Champ de Ronces : Jardin des Tourments d'Hybris", "Champ de Ronces : Tapis de Racines Épineuses", true, true,
                "Sanctuaire floral léthal de Minalia.",
                "Zone de 3 cases de rayon : inflige 6 dégâts absolus par case franchie et fait chuter les cibles À Terre.",
                isMinaExclusive: true);

            // --- Extension exclusive : Greffe Cinétique ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Greffe Cinétique : Pont Végétal Aérien", "Greffe Cinétique : Osmose d'Escouade", false, true,
                "Lianes porteuses suspendues.",
                "Permet de franchir les gouffres ou obstacles sans toucher le sol ni subir d'interception.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Greffe Cinétique : Racines d'Accélération", "Greffe Cinétique : Pont Végétal Aérien", false, true,
                "Transfert métabolique direct.",
                "Confère +2 PA d'action réflexe immédiate à un allié désigné au contact.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Greffe Cinétique : Réseau Mycélien Primordial", "Greffe Cinétique : Racines d'Accélération", true, true,
                "Symbiose totale de l'escouade de Mina.",
                "Relie tous les alliés : les dégâts subis peuvent être partagés librement et la vitesse de course augmente de +2 m/s.",
                isMinaExclusive: true);

            // --- Éveil Chlorophyllien : Avatar de Minalia (Volume II) ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Éveil Chlorophyllien", null, false, true,
                "Affinité cellulaire végétale exclusive de Mina-0 (Volume II).",
                "Augmente de +2 l'absorption et confère la photosynthèse arcanique.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Éveil Chlorophyllien : Photosynthèse Arcanique", "Éveil Chlorophyllien", false, true,
                "Assimilation d'énergie radiative.",
                "Octroie +2 PA au début de chaque tour si le combat se déroule en milieu éclairé ou naturel.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Éveil Chlorophyllien : Peau de Chlorophylle", "Éveil Chlorophyllien : Photosynthèse Arcanique", false, true,
                "Épiderme renforcé de cellulose cristalline.",
                "Confère +2 d'armure naturelle permanente et une immunité totale aux radiations spatiales.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Éveil Chlorophyllien : Floraison Cinétique", "Éveil Chlorophyllien : Peau de Chlorophylle", false, true,
                "Exsudation d'épines sous l'effort.",
                "Chaque point d'essoufflement engagé fait jaillir une ronce défensive infligeant 3 dégâts aux attaquants au contact.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Éveil Chlorophyllien : Pollen Hypnotique", "Éveil Chlorophyllien : Floraison Cinétique", false, true,
                "Aura sédative de Minalia.",
                "Les assaillants situés à moins de 2 cases subissent un malus permanent de -2 à toutes leurs attaques.",
                isMinaExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagiePrimale, "Éveil Chlorophyllien : Avatar de Minalia Primordiale", "Éveil Chlorophyllien : Pollen Hypnotique", true, true,
                "Incarnation primordiale totale de Mina-0.",
                "Le dé de Magie Primale passe au palier suprême (2d12+10) et toutes les frappes à mains nues infligent +10 dégâts d'épines.",
                isMinaExclusive: true);
        }

        // ------------------------------------------------------------------
        // BONUS DE COMBAT
        // ------------------------------------------------------------------

        /// <summary>
        /// Bonus de dégâts de l'Avatar de Minalia (+10 en Mains Nues).
        /// Appelé par CombatCalculator (délégation propre).
        /// </summary>
        public static int GetAvatarDamageBonus(CharacterStats attacker, SkillType attackSkill)
        {
            if (attacker == null) return 0;
            if (attackSkill != SkillType.MainsNues) return 0;
            if (!attacker.HasSpecialization(AvatarSpecialization)) return 0;
            return 10;
        }

        /// <summary>
        /// Déclenche le Cœur d'Éclosion (1 fois par combat) : restaure 50% PV max + purge.
        /// Retourne true si déclenché. Appelé par CharacterStats.EvaluateFatalBlow.
        /// </summary>
        public static bool TryTriggerHeartOfBloom(CharacterStats stats)
        {
            if (stats == null) return false;
            if (stats.HasUsedHeartOfBloom) return false;
            if (!stats.HasSpecialization(HeartOfBloomSpecialization)) return false;
            stats.HasUsedHeartOfBloom = true;
            stats.CurrentHealth = Math.Max(1, stats.MaxHealth / 2);
            stats.ClearAllStatus();
            return true;
        }

        /// <summary>
        /// Maîtrise innée : Corps Augmenté est toujours considéré débloqué pour Mina.
        /// </summary>
        public static bool IsInnateUnlocked(string specializationName, CharacterSheet sheet)
        {
            if (!IsMina(sheet)) return false;
            return string.Equals(specializationName, InnateSpecialization, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // FICHE HÉROÏQUE INTÉGRÉE (transférée du disque : Mina_a7679993.json)
        // ------------------------------------------------------------------

        /// <summary>
        /// Construit la fiche héroïque de Mina à partir des données disque les plus
        /// récentes (Mina_a7679993.json). Fiche intégrée : chargeable depuis la
        /// fenêtre des fichiers, tableau séparé des fichiers disque.
        /// </summary>
        public static CharacterSheet BuildHeroicSheet()
        {
            var savedFiles = CharacterStorageService.GetSavedCharacterFiles();
            for (int i = 0; i < savedFiles.Count; i++)
            {
                string filePath = savedFiles[i];
                string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                if (fileName.IndexOf(SheetIdPrefix, StringComparison.OrdinalIgnoreCase) >= 0
                    || fileName.StartsWith("Mina_", StringComparison.OrdinalIgnoreCase))
                {
                    var loaded = CharacterStorageService.LoadCharacter(filePath);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }

            var sheet = new CharacterSheet
            {
                SheetId = "a7679993424d48908f3c4cb84e03eff7",
                Name = "Mina",
                Age = 25,
                Gender = "Indéterminé",
                Species = SpeciesType.Humain,
                Profile = CharacterProfileType.HerosPJ,
                ModelPrefabName = "Mina",
                LoreNotes = "",
                BaseAttributes = new Attributes(
                    @for: 3, agi: 5, con: 3, rap: 4,
                    @int: 3, eru: 2, cha: 3, ins: 3,
                    mag: 5, vision: 3, ouie: 3, miracle: 1),
                BaseArmor = 1,
                AvailableXP = 0,
                TotalEarnedXP = 30,
                TotalSpentXP = 0,
                FreeTrainingsUsed = 0,
                CreditsCE = 8000
            };

            sheet.GetSkill(SkillType.MainsNues).TrainingLevel = 3;
            sheet.GetSkill(SkillType.Athletisme).TrainingLevel = 1;
            sheet.GetSkill(SkillType.Acrobatie).TrainingLevel = 1;

            sheet.UnlockedSpecializations.Add("Arts Martiaux");
            sheet.UnlockedSpecializations.Add("Protocole des Pas Invisibles");
            sheet.UnlockedSpecializations.Add("Réflexes Myotatiques");
            sheet.UnlockedSpecializations.Add("Fibres Résilientes");
            sheet.UnlockedSpecializations.Add("Régénération Métabolique");

            sheet.Inventory.Add(new InventoryItem
            {
                Name = "SciFiGunLight_Rad",
                PrefabPath = "SciFiGunLight_Rad",
                Type = ItemType.Weapon,
                EquipSlot = ItemEquipSlot.MainHand,
                IsEquipped = true,
                BaseDamage = 7,
                RangeInTiles = 10,
                AssociatedSkill = SkillType.Ballistique,
                WeightKg = 2.4f,
                Description = "Arme balistique importée depuis Resources/Guns/SciFiGunLight_Rad.",
                PriceCE = 500
            });

            return sheet;
        }
    }
}
