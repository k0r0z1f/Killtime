using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Rules;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Registre des techniques de spécialisation activables en combat (Livre III).
    /// Chaque technique correspond à une spécialisation du compendium
    /// (CharacterProgressionManager) dont l'effet mécanique est jouable au tour par tour.
    /// Utilisé à la fois par le menu contextuel joueur (CombatActionRegistry) et par
    /// l'IA tactique (TacticalAIController) : une seule implémentation, deux appelants.
    ///
    /// Coûts fixes (Livre III) : 2 PA par technique, débités via ConsumeActionPoints
    /// (Ralenti x2 appliqué automatiquement). Toute réussite coche la progression
    /// organique de la compétence source (Livre I §5).
    ///
    /// Façade : les constantes vivent ici, le contenu vit dans les partiels
    /// (Core, Martial, Command, Guard, Menu) — extrait sans modification.
    /// </summary>
    public static partial class CombatTechniqueRegistry
    {
        public const int CleCost = 2;
        public const int AnalyseCost = 2;
        public const int RugissementCost = 2;
        public const int RegardCost = 2;
        public const int MenerCost = 2;
        public const int TenirCost = 2;
        public const int InterceptCost = 1;
        public const int InterceptRange = 1;

        public const int CleRawDamage = 3;
        public const int AnalyseRange = 10;
        public const int RegardRange = 6;
        public const int AuraRange = 3;
        public const int TenirArmorBonus = 2;

        public const string SpecCle = "Arts Martiaux : Clé d'Articulation";
        public const string SpecRupture = "Arts Martiaux : Rupture Ligamentaire";
        public const string SpecAnalyse = "Analyse de Faille";
        public const string SpecTirCoordonne = "Analyse de Faille : Tir Coordonné";
        public const string SpecRugissement = "Intimider : Rugissement de Terreur";
        public const string SpecRegard = "Intimider : Regard de Prédateur";
        public const string SpecMener = "Mener (Commandement)";
        public const string SpecTenir = "Mener : Tenir la Ligne !";
        public const string SpecGardeDuCorps = "Garde du corps";
        public const string SpecCommandementTectonique = "Commandement Tectonique";
        public const string SpecPasDeRetraite = "Commandement Tectonique : Pas de Retraite !";
        public const string SpecChuteDePression = "Vide Calculant : Chute de Pression";
        public const string SpecSuggestionsBreves = "Pare-feu Psychologique : Suggestions Brèves";
        public const string SpecArretVectoriel = "Arrêt Vectoriel";
    }
}
