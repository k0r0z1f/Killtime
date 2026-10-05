using System;
using Killtime.Core.Inventory;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Fiche héroïque du Commandant Vance — Autorité du Commandant & Vétéran Cybernétique.
    /// Classe unique : toute la logique propre à Vance réside ici (délégation propre),
    /// sur le même modèle que MinaCharacter / LucasCharacter / ThomasCharacter / JohnCharacter / ErikaCharacter.
    ///
    /// PROFIL NARRATIF (Volume I, Scène 01 « Quadra-plen, Elara 1775 HSC ») :
    /// - Commandant d'une cellule insurrectionnelle de la Résistance à Nefris.
    ///   Vétéran pragmatique (48 ans) doté d'une prothèse cybernétique avancée au bras gauche.
    /// - Mundan aguerri (MAG 0) SANS restriction d'âme : là où Thomas est verrouillé
    ///   hors de toute magie et Mina/Lucas/Erika hors de leurs voies opposées, Vance peut
    ///   toucher à tout — mais ne maîtrise en propre que la voie du Commandement
    ///   (ordre de feu, tenue de ligne, œil tactique, tir d'ordre, bras cybernétique).
    ///   Le roc de l'escouade rebelle, pas le prodige.
    /// </summary>
    public static class VanceCharacter
    {
        public const string SheetIdPrefix = "beef5a1c";
        public const string InnateSpecialization = "Autorité du Commandant";

        public const string DisplayTag = "[Vance : Autorité du Commandant & Vétéran Cybernétique]";
        public const string SectorName = "L'AUTORITÉ DU COMMANDANT VANCE";
        public const string SectorSubtitle = "Voie Mundane : Ordre de Feu, Œil Tactique, Tir d'Ordre & Bras Cybernétique (MAG 0)";
        public const string RestrictionLabel = "🔒 Inaccessible à Vance (Voie du Commandant)";

        // ------------------------------------------------------------------
        // IDENTITÉ
        // ------------------------------------------------------------------

        public static bool IsVance(CharacterSheet sheet)
        {
            if (sheet == null) return false;
            // Nom exact uniquement ("Adjudant de Vance" n'est PAS Vance).
            if (!string.IsNullOrEmpty(sheet.Name)
                && (sheet.Name.Equals("Vance", StringComparison.OrdinalIgnoreCase)
                    || sheet.Name.Equals("Commandant Vance", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (!string.IsNullOrEmpty(sheet.ModelPrefabName)
                && (sheet.ModelPrefabName.Equals("Vance", StringComparison.OrdinalIgnoreCase)
                    || sheet.ModelPrefabName.Equals("Rebel_Commander", StringComparison.OrdinalIgnoreCase)
                    || sheet.ModelPrefabName.Equals("Commander_Vance", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (!string.IsNullOrEmpty(sheet.SheetId) && sheet.SheetId.StartsWith(SheetIdPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        // ------------------------------------------------------------------
        // RESTRICTIONS D'ÂME (AUCUNE — le commandant touche à tout, sans génie arcanique)
        // ------------------------------------------------------------------

        public static bool IsSkillForbidden(SkillType skill)
        {
            // Vance n'a aucune affinité arcanique innée (MAG 0) mais aucune interdiction :
            // c'est le roc mundan polyvalent. Les voies héroïques des autres
            // (Mina / Lucas / Thomas / John / Erika) lui restent fermées via les exclusivités.
            return false;
        }

        public static string ForbiddenSkillMessage(SkillType skill)
        {
            // Non utilisé (aucune compétence interdite), conservé pour symétrie d'API.
            return $"[VOIE DU COMMANDANT] Vance n'a aucune restriction d'âme : il peut s'entraîner à {skill}.";
        }

        public static string ExclusiveSpecializationMessage(string specializationName)
        {
            return $"[VOIE DU COMMANDANT EXCLUSIVE] La maîtrise '{specializationName}' exige l'autorité du Commandant Vance (cellule de Nefris, Quadra-plen 1775, bras cybernétique de vétéran).";
        }

        // ------------------------------------------------------------------
        // ENREGISTREMENT DES SPÉCIALISATIONS EXCLUSIVES DE VANCE
        // Appelé par CharacterProgressionManager.EnsureRegistryBuilt().
        // ------------------------------------------------------------------

        public static void RegisterSpecializations()
        {
            // --- INNÉ : Autorité du Commandant (Leadership — la voix qui tient la ligne) ---
            CharacterProgressionManager.RegisterSpec(SkillType.Leadership, "Autorité du Commandant", null, false, false,
                "Voix du vétéran de Nefris qui fait tenir la ligne (Inné — Vance).",
                "[Inné : Commandant] Les alliés à 4 cases de Vance ignorent Déstabilisé face aux charges et gagnent +1 au Seuil d'Encaissement tant que Vance est debout.",
                isVanceExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Leadership, "Autorité du Commandant : Ordre de Feu Coordonné", "Autorité du Commandant", false, false,
                "Un geste, une salve : toute la cellule frappe ensemble.",
                "Dépense 2 PA : désigne une cible à 8 cases — le prochain tir de chaque allié contre elle ce tour gagne +2 dégâts.",
                isVanceExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Leadership, "Autorité du Commandant : Tenir la Ligne de Nefris", "Autorité du Commandant : Ordre de Feu Coordonné", true, false,
                "Le mur de boucliers et de fusils qui ne rompt jamais.",
                "1 fois par combat (2 PA) : tous les alliés à 4 cases gagnent Couvert 3/4 mutuel et purgent Étourdi jusqu'à la fin du tour.",
                isVanceExclusive: true);

            // --- ŒIL DU CHAMP DE BATAILLE (Tactique/Stratégie — lire Nefris comme une carte) ---
            CharacterProgressionManager.RegisterSpec(SkillType.TactiqueStrategie, "Œil du Champ de Bataille", null, false, false,
                "Lecture froide du terrain : couloirs, holo-table, issues de secours.",
                "Dépense 1 PA : révèle embuscades et pièges à 6 cases et confère +2 en Observation d'escouade pendant 1 tour.",
                isVanceExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.TactiqueStrategie, "Œil du Champ de Bataille : Redéploiement Éclair", "Œil du Champ de Bataille", false, false,
                "La cellule glisse d'un couvert à l'autre sans rompre.",
                "Dépense 2 PA : jusqu'à 2 alliés à 4 cases se déplacent gratuitement de 2 cases sans attaque d'opportunité.",
                isVanceExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.TactiqueStrategie, "Œil du Champ de Bataille : Enveloppement du Quadra-plen", "Œil du Champ de Bataille : Redéploiement Éclair", true, false,
                "La manœuvre qui referme la salle de commandement sur l'ennemi.",
                "1 fois par combat (3 PA) : tous les ennemis encerclés à 3 cases de 2+ alliés subissent Déstabilisé et -2 à leurs attaques pendant 1 tour.",
                isVanceExclusive: true);

            // --- TIR D'ORDRE (Ballistique — la carabine montre l'exemple) ---
            CharacterProgressionManager.RegisterSpec(SkillType.Ballistique, "Tir d'Ordre", null, false, false,
                "Discipline de feu du commandant : viser, respirer, frapper.",
                "En garde (1 PA) : le premier tir de Vance après un ordre gagne +1 palier de dé et ignore 1 point d'armure.",
                isVanceExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Ballistique, "Tir d'Ordre : Salve de Suppression", "Tir d'Ordre", false, false,
                "Clouer l'ennemi sous un rideau de fer.",
                "Dépense 2 PA : zone de 2 cases à 8 cases — les ennemis pris dedans subissent Déstabilisé et ne peuvent charger ce tour.",
                isVanceExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Ballistique, "Tir d'Ordre : Sentence du Commandement", "Tir d'Ordre : Salve de Suppression", true, false,
                "La balle qui tranche le débat.",
                "1 fois par combat : tir gratuit à 8 cases (dégâts doublés, ignore 4 armure) contre l'ennemi qui a blessé un allié ce tour.",
                isVanceExclusive: true);

            // --- BRAS CYBERNÉTIQUE (EndurancePhysique — la prothèse du vétéran) ---
            CharacterProgressionManager.RegisterSpec(SkillType.EndurancePhysique, "Bras Cybernétique Renforcé", null, false, false,
                "Prothèse avancée du bras gauche : vérins, plaques et servos de Nefris.",
                "Confère +1 Seuil d'Encaissement et permet de parer une attaque de mêlée par tour sans arme (test EndurancePhysique).",
                isVanceExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.EndurancePhysique, "Bras Cybernétique Renforcé : Plaque Sous-cutanée de Nefris", "Bras Cybernétique Renforcé", false, false,
                "Blindage dermique tissé sous la peau du vétéran.",
                "Absorbe 2 dégâts bruts supplémentaires lors de tout blocage ou encaissement actif.",
                isVanceExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.EndurancePhysique, "Bras Cybernétique Renforcé : Protocole Dernier Rempart", "Bras Cybernétique Renforcé : Plaque Sous-cutanée de Nefris", true, false,
                "Le corps fait rempart quand la ligne va céder.",
                "1 fois par combat : si Vance tombe à 0 PV, il reste à 1 PV, purge Étourdi/Déstabilisé et gagne +2 Armure jusqu'à la fin du combat. Contrecoup : +1 Essoufflement.",
                isVanceExclusive: true);
        }

        // ------------------------------------------------------------------
        // MAÎTRISE INNÉE
        // ------------------------------------------------------------------

        public static bool IsInnateUnlocked(string specializationName, CharacterSheet sheet)
        {
            if (!IsVance(sheet)) return false;
            return string.Equals(specializationName, InnateSpecialization, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // FICHE HÉROÏQUE INTÉGRÉE (Vance_beef5a1c.json)
        // ------------------------------------------------------------------

        /// <summary>
        /// Construit la fiche héroïque officielle de Vance selon les règles
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
                    || fileName.StartsWith("Vance_", StringComparison.OrdinalIgnoreCase))
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

            // Répartition conforme HérosPJ (Livre I) — commandant vétéran / roc d'escouade :
            // Piliers : FOR 5 (vétéran + prothèse), CHA 4 (autorité naturelle)
            // Secondaires (somme = 15, max 3) : AGI 3, CON 3, RAP 2, INT 3, ÉRU 1, INS 3, MAG 0
            var sheet = new CharacterSheet
            {
                SheetId = "beef5a1c9d2e4a5f8b1f2a3c4d5e6f70",
                Name = "Vance",
                Age = 48,
                Gender = "Masculin",
                Species = SpeciesType.Humain,
                Profile = CharacterProfileType.HerosPJ,
                ModelPrefabName = "Vance",
                LoreNotes = "Commandant Vance, 48 ans. Commandant d'une cellule insurrectionnelle de la Résistance à Nefris, vétéran pragmatique de la salle de commandement du Quadra-plen (Elara 1775 HSC). Prothèse cybernétique avancée au bras gauche. Mundan aguerri (MAG 0) sans restriction d'âme : le roc qui fait tenir la ligne là où les prodiges se brisent.",
                BaseAttributes = new Attributes(
                    @for: 5, agi: 3, con: 3, rap: 2,
                    @int: 3, eru: 1, cha: 4, ins: 3,
                    mag: 0, vision: 3, ouie: 3, miracle: 1),
                BaseArmor = 2,
                AvailableXP = 0,
                TotalEarnedXP = 45,
                TotalSpentXP = 45,
                FreeTrainingsUsed = 1,
                CreditsCE = 8000
            };

            // Entraînements initiaux (Livre I §5 : 1 gratuit Érudition + 5 payants = 25 XP)
            sheet.GetSkill(SkillType.Leadership).TrainingLevel = 2;
            sheet.GetSkill(SkillType.TactiqueStrategie).TrainingLevel = 1;
            sheet.GetSkill(SkillType.Ballistique).TrainingLevel = 1;
            sheet.GetSkill(SkillType.EndurancePhysique).TrainingLevel = 1;
            sheet.GetSkill(SkillType.Communication).TrainingLevel = 1;

            // Spécialisations débloquées (Inné gratuit + 4 voies du commandant = 20 XP)
            sheet.UnlockedSpecializations.Add("Autorité du Commandant");
            sheet.UnlockedSpecializations.Add("Œil du Champ de Bataille");
            sheet.UnlockedSpecializations.Add("Tir d'Ordre");
            sheet.UnlockedSpecializations.Add("Bras Cybernétique Renforcé");
            sheet.UnlockedSpecializations.Add("Pistolet & Tir Rapide");

            // Équipement officiel — dotation catalogue Armurerie/Marché (Livre VIII) :
            // la carabine d'assaut de la cellule devient le fusil d'assaut laser
            // (cadence soutenue, tir d'ordre et suppression).
            if (!ArmoryCatalog.GiveLoadoutItem(sheet, "Fusil d'Assaut Laser", false))
            {
                // Repli si catalogue indisponible (ne devrait jamais arriver).
                sheet.Inventory.Add(new InventoryItem
                {
                    Name = "Carabine d'Assaut Résistance",
                    Type = ItemType.Weapon,
                    EquipSlot = ItemEquipSlot.MainHand,
                    IsEquipped = false,
                    BaseDamage = 7,
                    RangeInTiles = 8,
                    AssociatedSkill = SkillType.Ballistique,
                    WeightKg = 3.2f,
                    Description = "Carabine d'assaut de la cellule de Nefris, réglée par Vance pour le tir d'ordre et la suppression.",
                    PriceCE = 550
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