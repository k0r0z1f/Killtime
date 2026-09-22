using System;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Fiche héroïque de Lucas (Lucas-0) — Vide Calculant & Architecture Neurale.
    /// Extrait des fichiers de base : toute la logique spécifique à Lucas vit ici.
    /// Les fichiers de base délèguent vers cette classe (délégation propre).
    /// Miroir de Mina (corps/cinétique/végétal) : Lucas n'a aucune affinité primale.
    /// Sa voie est Élémentale-inversée (froid calculé) + Esprit (télépathie 6 stades, calcul spatial).
    /// Lucas ne crée pas de feu comme un magicien normal : il SOUSTRAIT (pression, chaleur).
    /// </summary>
    public static class LucasCharacter
    {
        public const string SheetIdPrefix = "c4a5ca1a";
        public const string InnateSpecialization = "Vide Calculant";

        public const string DisplayTag = "[Lucas-0 : Vide Calculant & Architecture Neurale]";
        public const string SectorName = "LE VIDE CALCULANT DE LUCAS";
        public const string SectorSubtitle = "Cinquième Force (MAG) : Soustraction Thermodynamique & Architecture Neurale (6 stades)";
        public const string RestrictionLabel = "🔒 Inaccessible à Lucas (Vide Calculant Pur)";

        // ------------------------------------------------------------------
        // IDENTITÉ
        // ------------------------------------------------------------------

        public static bool IsLucas(CharacterSheet sheet)
        {
            if (sheet == null) return false;
            if (!string.IsNullOrEmpty(sheet.Name) && sheet.Name.IndexOf("Lucas", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(sheet.ModelPrefabName) && sheet.ModelPrefabName.IndexOf("Lucas", StringComparison.OrdinalIgnoreCase) >= 0)
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
            return skill == SkillType.MagiePrimale;
        }

        public static string ForbiddenSkillMessage(SkillType skill)
        {
            return $"[RESTRICTION D'ÂME] Lucas ne possède aucune affinité primale (végétal, chair, cinétique corporelle). Seuls le Vide calculant et l'Esprit résonnent en lui.";
        }

        public static string ForbiddenSpellMessage()
        {
            return $"[RESTRICTION D'ÂME] Lucas ne peut graver aucun sort primal (végétal, chair). Sa voie est le Vide calculant (Élémental-inversé) et l'Esprit.";
        }

        public static string ExclusiveSpecializationMessage(string specializationName)
        {
            return $"[ARCHITECTURE NEURALE EXCLUSIVE] La maîtrise '{specializationName}' exige le Vide calculant de Lucas-0 (soustraction thermodynamique + 6 stades télépathiques).";
        }

        // ------------------------------------------------------------------
        // ENREGISTREMENT DES SPÉCIALISATIONS EXCLUSIVES (VIDE + ESPRIT)
        // Appelé par CharacterProgressionManager.EnsureRegistryBuilt().
        // ------------------------------------------------------------------

        public static void RegisterSpecializations()
        {
            // --- Magie du Vide Calculant : thermodynamique inversée ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagieElementale, "Vide Calculant", null, false, false,
                "Soustraction thermodynamique calculée (Inné — Lucas).",
                "[Inné : Soustraction] Les épreuves de Magie Élémentale utilisent (MAG+INT)/2 et les dégâts thermiques deviennent des Dégâts Absolus de froid calculé (ignorent l'armure).",
                isLucasExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagieElementale, "Vide Calculant : Chute de Pression", "Vide Calculant", false, false,
                "Raréfaction de l'air ciblé.",
                "Cible à 4 cases : 4 dégâts Absolus + malus de -2 à sa prochaine attaque (pression étouffante).",
                isLucasExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagieElementale, "Vide Calculant : Dôme de Vide", "Vide Calculant", false, false,
                "Paroi d'air raréfié.",
                "Fait jaillir instantanément un Couvert 3/4 sur un hexagone ciblé à 4 cases pendant 1 tour.",
                isLucasExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagieElementale, "Vide Calculant : Zéro Absolu", "Vide Calculant : Chute de Pression", false, false,
                "Soustraction thermique totale.",
                "Cible à 4 cases : 8 dégâts Absolus, applique Ralenti pendant 2 tours et purge l'état En Feu.",
                isLucasExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagieElementale, "Vide Calculant : Carnage Somnambulique", "Vide Calculant : Zéro Absolu", true, false,
                "Le subconscient prend les commandes quand le conscient sature.",
                "1 fois par combat, si Lucas tombe à 0 PA ou subit un Choc Traumatique : Zéro Absolu gratuit en zone de 2 cases (12 dégâts Absolus, cibles À Terre). Contrecoup : +2 Essoufflement et Sonné si Mina est à plus de 3 cases.",
                isLucasExclusive: true);

            // --- Architecture Neurale : Pare-feu Psychologique (6 stades) ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagieEsprit, "Pare-feu Psychologique", null, false, false,
                "Cloisonnement mental forgé à l'Institut (Stades 1-2 — Lucas).",
                "Immunité à la surprise mentale et +2 contre Intimidation et Tromper. Prérequis de toute la voie neurale.",
                isLucasExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagieEsprit, "Pare-feu Psychologique : Lecture de Surface", "Pare-feu Psychologique", false, false,
                "Écoute du réseau modal par défaut (Stade 2).",
                "Dépense 2 PA : révèle les intentions de la cible et confère +2 en Observation contre elle pendant 1 tour.",
                isLucasExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagieEsprit, "Pare-feu Psychologique : Suggestions Brèves", "Pare-feu Psychologique : Lecture de Surface", false, false,
                "Aiguillages synaptiques subtils (Stade 3).",
                "Cible à 4 cases : perd 1 PA de réserve et subit Déstabilisé pendant 1 tour (automatique sur sbire, requiert Delta >= 2 contre PJ/PNJ majeur).",
                isLucasExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagieEsprit, "Pare-feu Psychologique : Réécriture Synaptique", "Pare-feu Psychologique : Suggestions Brèves", false, false,
                "Recâblage ciblé des circuits (Stades 4-5).",
                "Prend le contrôle d'un sbire pendant 1 tour (4 PA + 1 Essoufflement). Les PNJ majeurs résistent sauf Delta >= 4.",
                isLucasExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagieEsprit, "Pare-feu Psychologique : Possession Intégrale", "Pare-feu Psychologique : Réécriture Synaptique", true, false,
                "Contrôle moteur, sensoriel et cognitif total (Stade 6).",
                "1 fois par combat : contrôle total d'une cible pendant 1 tour. Contrecoup : Déstabilisé + saignement psychique (1 PV/tour pendant 2 tours, purgeable par Mina au contact).",
                isLucasExclusive: true);

            // --- Calcul spatial & lien symbiotique avec Mina ---
            CharacterProgressionManager.RegisterSpec(SkillType.MagieEsprit, "Arrêt Vectoriel", null, false, false,
                "Télékinésie mathématique à distance (contrepartie du Teep de Mina — Lucas).",
                "Réaction (1 PA) : interrompt le déplacement d'un ennemi à 4 cases (figé sur place) et lui inflige 3 dégâts Absolus s'il fonçait au contact.",
                isLucasExclusive: true);
            CharacterProgressionManager.RegisterSpec(SkillType.MagieEsprit, "Thermostat Inversé", null, false, false,
                "La chaleur de Mina régule le froid de Lucas (lien symbiotique).",
                "Si Mina est à 3 cases ou moins : les pouvoirs du Vide coûtent 1 PA de moins (minimum 1) et aucun contrecoup ne s'applique. Si Mina est absente ou à plus de 6 cases : tout échec de Magie Élémentale inflige 3 dégâts Absolus de backlash à Lucas (Sonné si critique).",
                isLucasExclusive: true);
        }

        // ------------------------------------------------------------------
        // LIEN SYMBIOTIQUE (THERMOSTAT INVERSÉ)
        // ------------------------------------------------------------------

        /// <summary>
        /// Réduction de coût du Vide si Mina est à 3 cases ou moins.
        /// </summary>
        public static int GetVoidCostReduction(bool minaWithin3Cases)
        {
            return minaWithin3Cases ? 1 : 0;
        }

        /// <summary>
        /// Backlash si Mina est absente ou à plus de 6 cases.
        /// </summary>
        public static bool HasVoidBacklash(bool minaAbsentOrBeyond6Cases, bool elementalFailure)
        {
            return minaAbsentOrBeyond6Cases && elementalFailure;
        }

        /// <summary>
        /// Maîtrise innée : Vide Calculant est toujours considéré débloqué pour Lucas.
        /// </summary>
        public static bool IsInnateUnlocked(string specializationName, CharacterSheet sheet)
        {
            if (!IsLucas(sheet)) return false;
            return string.Equals(specializationName, InnateSpecialization, StringComparison.OrdinalIgnoreCase);
        }
    }
}
