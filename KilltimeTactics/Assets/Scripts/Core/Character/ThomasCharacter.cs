using System;
using Killtime.Core.Inventory;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Fiche héroïque de Thomas (Thomas-0) — L'Ancre Inébranlable & Commandement Tectonique.
    /// Classe unique : toute la logique propre à Thomas réside ici (délégation propre).
    ///
    /// RÈGLE FONDAMENTALE DU CODEX (Livre I & Roman Vol. I Ch. 3, 10, 14, 15) :
    /// - Thomas n'est PAS immunisé à toutes les magies : les sorts ennemis, la géomancie adverse
    ///   ou les déflagrations arcaniques extérieures l'affectent selon les règles normales de combat.
    /// - En revanche, Thomas constitue un ZÉRO ABSOLU pour les fréquences arcaniques de MINA et LUCAS :
    ///   la télépathie de Lucas et l'énergie cinétique/végétale de Mina glissent sur lui sans le blesser,
    ///   servant d'isolant et de paratonnerre symbiotique à leurs anomalies.
    /// - Leader et commandant né : synergies tactiques de phalange, bonus de front et autorité martiale.
    /// - Zéro affinité magique native (MAG 0) : build d'athlète lourd, de protecteur et de tacticien de mêlée.
    /// </summary>
    public static class ThomasCharacter
    {
        public const string SheetIdPrefix = "f3b8d91a";
        public const string InnateSpecialization = "Ancre Symbiotique";
        public const string NeuralOverwriteSpecialization = "Restructuration Neurale : Protocole Zéro";

        public const string DisplayTag = "[Thomas-0 : Ancre de Fer & Commandement]";
        public const string SectorName = "L'ANCRE INÉBRANLABLE DE THOMAS";
        public const string SectorSubtitle = "Soma Terrestre : Densité Tectonique, Commandement de Front & Ancre Symbiotique";
        public const string RestrictionLabel = "🔒 Inaccessible à Thomas (Voie Martiale & Terrestre Pure)";

        // ------------------------------------------------------------------
        // IDENTITÉ
        // ------------------------------------------------------------------

        public static bool IsThomas(CharacterSheet sheet)
        {
            if (sheet == null) return false;
            if (!string.IsNullOrEmpty(sheet.Name) && sheet.Name.IndexOf("Thomas", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(sheet.ModelPrefabName) && sheet.ModelPrefabName.IndexOf("Thomas", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(sheet.SheetId) && sheet.SheetId.StartsWith(SheetIdPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        // ------------------------------------------------------------------
        // RESTRICTIONS D'ÂME (ZÉRO MAGIE NATIVE)
        // ------------------------------------------------------------------

        public static bool IsSkillForbidden(SkillType skill)
        {
            return skill == SkillType.MagieElementale || skill == SkillType.MagiePrimale || skill == SkillType.MagieEsprit;
        }

        public static string ForbiddenSkillMessage(SkillType skill)
        {
            return $"[RESTRICTION D'ÂME] Thomas ne possède aucune étincelle arcanique innée (MAG 0). Sa puissance repose exclusivement sur l'acier, le souffle, le commandement et l'impact terrestre.";
        }

        public static string ExclusiveSpecializationMessage(string specializationName)
        {
            return $"[DENSITÉ TECTONIQUE EXCLUSIVE] La maîtrise '{specializationName}' exige la physiologie d'ancre et l'autorité naturelle de Thomas-0.";
        }

        // ------------------------------------------------------------------
        // RÈGLE SPÉCIALE : IMMUNITÉ SYMBIOTIQUE CIBLÉE (MINA & LUCAS SEULEMENT)
        // ------------------------------------------------------------------

        /// <summary>
        /// Vérifie si Thomas est immunisé contre une attaque, altération ou interférence magique.
        /// RÈGLE CODEX : Vrai UNIQUEMENT si le lanceur est Mina ou Lucas.
        /// Les attaques magiques ennemies (ex: mages adverses, géomancie, Disciples) lui infligent
        /// des dégâts normaux.
        /// </summary>
        public static bool IsImmuneToCasterMagic(CharacterSheet targetSheet, CharacterSheet casterSheet)
        {
            if (!IsThomas(targetSheet)) return false;
            if (casterSheet == null) return false;

            return MinaCharacter.IsMina(casterSheet) || LucasCharacter.IsLucas(casterSheet);
        }

        /// <summary>
        /// Évalue si une tentative de sonde ou d'intrusion mentale sur Thomas est automatiquement
        /// annulée par son vide acoustique symbiotique.
        /// </summary>
        public static bool BlocksTelepathicIntrusion(CharacterSheet targetSheet, CharacterSheet proberSheet)
        {
            if (!IsThomas(targetSheet)) return false;
            if (proberSheet == null) return false;

            // Lucas ne peut ni sonder ni blesser l'esprit de Thomas sans un feedback violent.
            return LucasCharacter.IsLucas(proberSheet);
        }

        // ------------------------------------------------------------------
        // ENREGISTREMENT DES SPÉCIALISATIONS EXCLUSIVES DE THOMAS
        // Appelé par CharacterProgressionManager.EnsureRegistryBuilt().
        // ------------------------------------------------------------------

        public static void RegisterSpecializations()
        {
            // --- INNÉ : L'Ancre Symbiotique ---
            CharacterProgressionManager.RegisterSpec(SkillType.DefenseCorporelle, "Ancre Symbiotique", null, false, false,
                "Vide acoustique et neutralité cinétique absolue face à Mina et Lucas (Inné — Thomas).",
                "[Inné : Ancre] Immunité totale aux dégâts collatéraux, projections et altérations mentales issus de Mina et Lucas. Tout allié adjacent à Thomas gagne +1 en Seuil d'Encaissement.",
                isThomasExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.DefenseCorporelle, "Ancre Symbiotique : Paratonnerre Cinétique", "Ancre Symbiotique", false, false,
                "Canalisation des ondes de choc alliées.",
                "Les attaques de zone de Mina ou Lucas touchant l'hexagone de Thomas n'infligent 0 dégât à Thomas et augmentent sa réserve de PA de +1 au tour suivant.",
                isThomasExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.DefenseCorporelle, "Ancre Symbiotique : Rempart Tri-Fusion", "Ancre Symbiotique : Paratonnerre Cinétique", true, false,
                "Harmonie martiale de la Ligne Temporelle Zéro.",
                "Tant que Mina ou Lucas se trouvent à 2 cases ou moins de Thomas, Thomas gagne +2 Armure naturelle et peut intercepter gratuitement une attaque dirigée contre eux par tour.",
                isThomasExclusive: true);

            // --- LEADERSHIP & COMMANDEMENT TECTONIQUE ---
            CharacterProgressionManager.RegisterSpec(SkillType.Leadership, "Commandement Tectonique", null, false, false,
                "Autorité naturelle de chef d'escouade forgée dans les rues de Brum'korath.",
                "Dépense 2 PA : ordonne une manœuvre coordonnée. Tous les alliés à 4 cases gagnent +1 PA de mouvement immédiat.",
                isThomasExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Leadership, "Commandement Tectonique : Formez le Coin !", "Commandement Tectonique", false, false,
                "Discipline de phalange face aux assauts massifs (Brum'korath Ch. 14).",
                "Les alliés adjacents à Thomas bénéficient d'un Couvert 3/4 mutuel et ignorent l'état Déstabilisé face aux charges.",
                isThomasExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Leadership, "Commandement Tectonique : Pas de Retraite !", "Commandement Tectonique : Formez le Coin !", false, false,
                "Inflexibilité morale sous le feu et les déflagrations.",
                "Purge immédiatement la panique et le statut Étourdi sur tous les alliés situés à moins de 3 cases.",
                isThomasExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.Leadership, "Commandement Tectonique : Ligne Inflexible", "Commandement Tectonique : Pas de Retraite !", true, false,
                "Contre-offensive générale synchronisée.",
                "1 fois par combat (3 PA) : chaque allié dans un rayon de 4 cases déclenche une riposte ou contre-attaque immédiate gratuite sur la prochaine cible qui l'attaque ce tour.",
                isThomasExclusive: true);

            // --- MAÎTRISE MARTIALE : IMPACT DE BEDROCK ---
            CharacterProgressionManager.RegisterSpec(SkillType.ManiementArmes, "Impact de Bedrock", "Marteau de Guerre", false, false,
                "Transfert de masse pure brisant les armures titaniques (Roman Ch. 15).",
                "Sur toute frappe contondante réussie avec différentiel Delta >= 2, fracture l'armure de la cible de -2 pour le reste du combat et applique Sonné.",
                isThomasExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.ManiementArmes, "Impact de Bedrock : Enclume Mortelle", "Impact de Bedrock", true, false,
                "Écrasement destructeur de titans.",
                "Ignore totalement l'armure physique balistique ou métallique des unités lourdes et inflige +6 dégâts bruts.",
                isThomasExclusive: true);

            // --- VOLUME I (CH. 16-17) : RESTRUCTURATION NEURALE DE LA RELIQUE ---
            CharacterProgressionManager.RegisterSpec(SkillType.EndurancePhysique, "Restructuration Neurale : Protocole Zéro", null, false, false,
                "Greffe de la relique sombre polygonale sur l'avant-bras droit (Volume I Ch. 16-17).",
                "Les réflexes moteurs s'exécutent au niveau spinal : confère +1 palier de dé en Parade et annule la pénalité de PA liée au statut Ralenti.",
                isThomasExclusive: true);

            CharacterProgressionManager.RegisterSpec(SkillType.EndurancePhysique, "Restructuration Neurale : Volonté d'Inflexible", "Restructuration Neurale : Protocole Zéro", true, false,
                "Endurance cognitive absolue face aux chocs critiques.",
                "Thomas peut poursuivre le combat jusqu'à -5 PV avant de sombrer dans l'inconscience. Son plafond d'Essoufflement effectif augmente de +2.",
                isThomasExclusive: true);
        }

        // ------------------------------------------------------------------
        // MAÎTRISE INNÉE
        // ------------------------------------------------------------------

        public static bool IsInnateUnlocked(string specializationName, CharacterSheet sheet)
        {
            if (!IsThomas(sheet)) return false;
            return string.Equals(specializationName, InnateSpecialization, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // FICHE HÉROÏQUE INTÉGRÉE (Thomas_f3b8d91a.json)
        // ------------------------------------------------------------------

        /// <summary>
        /// Construit la fiche héroïque officielle de Thomas (Thomas-0) selon les règles
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
                    || fileName.StartsWith("Thomas_", StringComparison.OrdinalIgnoreCase))
                {
                    var loaded = CharacterStorageService.LoadCharacter(filePath);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }

            // Répartition conforme HérosPJ (Livre I) :
            // Piliers : FOR 5, CON 4
            // Secondaires (somme = 15, max 3) : AGI 3, RAP 3, INT 2, ÉRU 1, CHA 3, INS 3, MAG 0
            // PA = Max(3, 2) + 3 + Min(1..5) = 3 + 3 + 1 = 7 PA.
            // Encaissement = 4 * 2 = 8. Seuil Mort = 4 * 5 = 20 PV.
            var sheet = new CharacterSheet
            {
                SheetId = "f3b8d91a48c140df9e72bb3a19e07502",
                Name = "Thomas",
                Age = 17,
                Gender = "Masculin",
                Species = SpeciesType.Humain,
                Profile = CharacterProfileType.HerosPJ,
                ModelPrefabName = "Thomas",
                LoreNotes = "Thomas-0, 17 ans. Athlète naturel, pilier défensif et commandant né de l'escouade. Ne possède aucune magie innée (MAG 0), mais constitue une ancre symbiotique absolue contre les décharges de Mina et Lucas (leur magie glisse sur lui sans le blesser). A brisé le Disciple de la Terre à Brum'korath en projetant une dalle de granit de plusieurs centaines de livres. Porteur de la 3e relique polygonale (Protocole Zéro).",
                BaseAttributes = new Attributes(
                    @for: 5, agi: 3, con: 4, rap: 3,
                    @int: 2, eru: 1, cha: 3, ins: 3,
                    mag: 0, vision: 3, ouie: 3, miracle: 1),
                BaseArmor = 2,
                AvailableXP = 0,
                TotalEarnedXP = 60,
                TotalSpentXP = 60,
                FreeTrainingsUsed = 1,
                CreditsCE = 8000
            };

            // Entraînements initiaux (Livre I §5 : 1 gratuit Érudition + 7 payants = 35 XP)
            sheet.GetSkill(SkillType.ManiementArmes).TrainingLevel = 2;
            sheet.GetSkill(SkillType.DefenseCorporelle).TrainingLevel = 2;
            sheet.GetSkill(SkillType.Leadership).TrainingLevel = 2;
            sheet.GetSkill(SkillType.Athletisme).TrainingLevel = 1;
            sheet.GetSkill(SkillType.EndurancePhysique).TrainingLevel = 1;

            // Spécialisations débloquées (Inné gratuit + 5 spécialisations payantes = 25 XP)
            sheet.UnlockedSpecializations.Add("Ancre Symbiotique");
            sheet.UnlockedSpecializations.Add("Armes Contondantes");
            sheet.UnlockedSpecializations.Add("Marteau de Guerre");
            sheet.UnlockedSpecializations.Add("Bloquer");
            sheet.UnlockedSpecializations.Add("Bloquer : Mur de Bouclier");
            sheet.UnlockedSpecializations.Add("Mener (Commandement)");

            // Équipement officiel
            sheet.Inventory.Add(new InventoryItem
            {
                Name = "Épée Bâtarde des Marches",
                Type = ItemType.Weapon,
                EquipSlot = ItemEquipSlot.MainHand,
                IsEquipped = true,
                BaseDamage = 8,
                RangeInTiles = 1,
                AssociatedSkill = SkillType.ManiementArmes,
                WeightKg = 2.6f,
                Description = "Lame d'acier lourd forgée pour fendre les armures et briser les gardes.",
                PriceCE = 450
            });

            sheet.Inventory.Add(new InventoryItem
            {
                Name = "Pavois d'Acier de Brum'korath",
                Type = ItemType.Weapon,
                EquipSlot = ItemEquipSlot.OffHand,
                IsEquipped = true,
                BaseDamage = 3,
                RangeInTiles = 1,
                AssociatedSkill = SkillType.DefenseCorporelle,
                WeightKg = 5.2f,
                Description = "Bouclier lourd capable d'absorber les chocs tectoniques et d'ériger un couvert pour l'escouade.",
                PriceCE = 500
            });

            return sheet;
        }
    }
}