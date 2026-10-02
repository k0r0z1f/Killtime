using System;

namespace Killtime.Core.Dice
{
    public enum CriticalEffectCategory
    {
        Offensive,
        Defensive,
        Social,
        Utility
    }

    public struct CriticalConsequence
    {
        public int Threshold;
        public int RawRoll;
        public string Title;
        public string Description;
        public CriticalEffectCategory Category;
    }

    /// <summary>
    /// Moteur probabiliste du Système RP (Livre II).
    /// Gère l'échelle évolutive des dés, les réussites critiques au score maximal,
    /// les échecs critiques sur le 1 naturel, et la confrontation aux Seuils de Difficulté (0-34).
    /// </summary>
    public class DiceRoller
    {
        private readonly Random _random;

        public DiceRoller(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>
        /// Tirage d'un dé brut sans modificateur ni seuil (Livre II §8.1).
        /// </summary>
        public virtual int RollRaw(DiceType dieType)
        {
            return dieType switch
            {
                DiceType.D2 => _random.Next(1, 3),
                DiceType.D3 => _random.Next(1, 4),
                DiceType.D4 => _random.Next(1, 5),
                DiceType.D6 => _random.Next(1, 7),
                DiceType.D8 => _random.Next(1, 9),
                DiceType.D10 => _random.Next(1, 11),
                DiceType.D12 => _random.Next(1, 13),
                DiceType.D20 => _random.Next(1, 21),
                DiceType.TwoD6 => _random.Next(1, 7) + _random.Next(1, 7),
                DiceType.TwoD8 => _random.Next(1, 9) + _random.Next(1, 9),
                DiceType.TwoD10 => _random.Next(1, 11) + _random.Next(1, 11),
                DiceType.TwoD12 => _random.Next(1, 13) + _random.Next(1, 13),
                DiceType.TwoD12Plus10 => _random.Next(1, 13) + _random.Next(1, 13) + 10,
                _ => _random.Next(1, 7),
            };
        }

        /// <summary>
        /// Évalue le palier atteint dans la Table Universelle des Conséquences Critiques (Livre II §8.2).
        /// </summary>
        public static int GetCriticalThreshold(int consequenceRoll)
        {
            if (consequenceRoll >= 24) return 24;
            if (consequenceRoll >= 20) return 20;
            if (consequenceRoll >= 16) return 16;
            if (consequenceRoll >= 12) return 12;
            if (consequenceRoll >= 10) return 10;
            if (consequenceRoll >= 9) return 9;
            if (consequenceRoll >= 8) return 8;
            if (consequenceRoll >= 6) return 6;
            if (consequenceRoll >= 5) return 5;
            if (consequenceRoll >= 4) return 4;
            if (consequenceRoll >= 3) return 3;
            if (consequenceRoll >= 2) return 2;
            return 1;
        }

        /// <summary>
        /// Lookup universel des conséquences critiques d2-d24 (Livre II §8.2).
        /// </summary>
        public static CriticalConsequence LookupCriticalConsequence(int consequenceRoll, CriticalEffectCategory category = CriticalEffectCategory.Offensive)
        {
            int threshold = GetCriticalThreshold(consequenceRoll);
            string title;
            string desc;

            switch (category)
            {
                case CriticalEffectCategory.Defensive:
                    switch (threshold)
                    {
                        case 1: title = "Cible à terre"; desc = "L'assaillant perd l'équilibre et s'écroule à terre (Statut À Terre)."; break;
                        case 2: title = "+1 EC Cible"; desc = "+1 EC bonus pour la cible."; break;
                        case 3: title = "Défense impénétrable"; desc = "Défense absolue impénétrable neutralisant tout assaut."; break;
                        case 4: title = "+1 EC Défenseur"; desc = "+1 EC pour l'attaquant / défenseur."; break;
                        case 5: title = "+2 Défense"; desc = "+2 Défense bonus."; break;
                        case 6: title = "Cible étourdie en retour"; desc = "Cible étourdie en retour (-1 EC pendant 1 tour)."; break;
                        case 8: title = "Récupère 1 effet"; desc = "Récupère 1 effet (dissipe 1 altération)."; break;
                        case 9: title = "Récupère 1 Essoufflement"; desc = "Récupère 1 point d'Essoufflement."; break;
                        case 10: title = "Attaquant ralenti"; desc = "Attaquant ralenti (PA doublés pendant 1 tour)."; break;
                        case 12: title = "Attaquant déstabilisé"; desc = "Attaquant déstabilisé (-2 EC pendant 1 tour)."; break;
                        case 16: title = "Récupère 2 PA"; desc = "Récupère 2 Points d'Action immédiatement."; break;
                        case 20: title = "Contre-attaque automatique"; desc = "Contre-attaque automatique immédiate."; break;
                        case 24:
                        default: title = "Désarçonnement fatal"; desc = "Désarçonnement fatal ennemi (mise hors d'état)."; break;
                    }
                    break;

                case CriticalEffectCategory.Social:
                    switch (threshold)
                    {
                        case 1: title = "Interlocuteur déstabilisé"; desc = "L'interlocuteur est pris au dépourvu."; break;
                        case 2: title = "Ascendant psychologique"; desc = "Ascendant psychologique net sur l'échange."; break;
                        case 3: title = "Confiance immédiate"; desc = "Incite une confiance immédiate."; break;
                        case 4: title = "+1 EC Persuasion"; desc = "+1 EC en persuasion."; break;
                        case 5: title = "L'interlocuteur cède"; desc = "L'interlocuteur cède sur un point clé."; break;
                        case 6: title = "Interlocuteur sidéré"; desc = "Interlocuteur sidéré et sans répartie."; break;
                        case 8: title = "Récupère 1 effet"; desc = "Récupère 1 effet."; break;
                        case 9: title = "Regain de sang-froid"; desc = "Regain de sang-froid immédiat."; break;
                        case 10: title = "Opposition neutralisée"; desc = "Opposition rhétorique neutralisée."; break;
                        case 12: title = "Effondrement argumentaire"; desc = "Effondrement de l'argument adverse."; break;
                        case 16: title = "Récupère 2 PA"; desc = "Récupère 2 PA d'initiative sociale."; break;
                        case 20: title = "Ralliement inconditionnel"; desc = "Ralliement inconditionnel de l'auditoire."; break;
                        case 24:
                        default: title = "Soumission absolue"; desc = "Soumission absolue de l'interlocuteur."; break;
                    }
                    break;

                case CriticalEffectCategory.Utility:
                    switch (threshold)
                    {
                        case 1: title = "Action silencieuse"; desc = "Tâche accomplie sans aucun bruit."; break;
                        case 2: title = "Gain d'élan cinétique"; desc = "Gain d'élan cinétique pour l'enchaînement."; break;
                        case 3: title = "Précision chirurgicale"; desc = "Précision chirurgicale sur l'outil."; break;
                        case 4: title = "+1 EC Tâche"; desc = "+1 EC pour la tâche en cours."; break;
                        case 5: title = "Économie de matériel"; desc = "Économie de matériel et de ressources."; break;
                        case 6: title = "Découverte inattendue"; desc = "Découverte inattendue ou indice caché."; break;
                        case 8: title = "Récupère 1 effet"; desc = "Récupère 1 effet."; break;
                        case 9: title = "Regain de vigueur"; desc = "Regain de vigueur immédiat."; break;
                        case 10: title = "Ralentissement temporel"; desc = "Ralentissement temporel / tâche exécutée en moitié de temps."; break;
                        case 12: title = "Composant sauvé"; desc = "Composant réparé ou sauvé de la destruction."; break;
                        case 16: title = "Récupère 2 PA"; desc = "Récupère 2 PA."; break;
                        case 20: title = "Survoltage total"; desc = "Survoltage arcanique total."; break;
                        case 24:
                        default: title = "Succès mirifique"; desc = "Succès mirifique critique."; break;
                    }
                    break;

                case CriticalEffectCategory.Offensive:
                default:
                    switch (threshold)
                    {
                        case 1: title = "Cible à terre"; desc = "La cible tombe à terre (Statut À Terre)."; break;
                        case 2: title = "Cible repoussée"; desc = "Cible repoussée d'1 case (Knockback)."; break;
                        case 3: title = "Avantage"; desc = "Avantage contre la cible pour l'action."; break;
                        case 4: title = "+1 EC Attaquant"; desc = "+1 EC pour l'attaquant."; break;
                        case 5: title = "+2 Dégâts"; desc = "+2 Dégâts directs."; break;
                        case 6: title = "Cible étourdie"; desc = "Cible étourdie (-1 EC pendant 1 tour)."; break;
                        case 8: title = "Récupère 1 effet"; desc = "Récupère 1 effet (dissipe 1 altération)."; break;
                        case 9: title = "Récupère 1 Essoufflement"; desc = "Récupère 1 point d'Essoufflement."; break;
                        case 10: title = "Cible ralentie"; desc = "Cible ralentie (PA doublés pendant 1 tour)."; break;
                        case 12: title = "Cible déstabilisée"; desc = "Cible déstabilisée (-2 EC pendant 1 tour)."; break;
                        case 16: title = "Récupère 2 PA"; desc = "Récupère 2 PA immédiatement."; break;
                        case 20: title = "3 Dégâts Résiduels"; desc = "3 Dégâts Résiduels continus."; break;
                        case 24:
                        default: title = "Cible Inconsciente"; desc = "Cible Inconsciente (K.O.)."; break;
                    }
                    break;
            }

            return new CriticalConsequence
            {
                Threshold = threshold,
                RawRoll = consequenceRoll,
                Title = title,
                Description = desc,
                Category = category
            };
        }

        /// <summary>
        /// Lance un jet de conséquence de même calibre et résout l'effet critique (Livre II §8.1 & §8.2).
        /// </summary>
        public virtual CriticalConsequence RollCriticalConsequence(DiceType dieType, CriticalEffectCategory category = CriticalEffectCategory.Offensive)
        {
            int rawRoll = RollRaw(dieType);
            return LookupCriticalConsequence(rawRoll, category);
        }

        public virtual DiceRollResult Roll(DiceType dieType, int modifier = 0, int targetDC = 10)
        {
            int rawRoll;
            bool isCriticalSuccess = false;
            bool isCriticalFailure = false;

            switch (dieType)
            {
                case DiceType.D2:
                    rawRoll = _random.Next(1, 3);
                    isCriticalSuccess = (rawRoll == 2);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D3:
                    rawRoll = _random.Next(1, 4);
                    isCriticalSuccess = (rawRoll == 3);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D4:
                    rawRoll = _random.Next(1, 5);
                    isCriticalSuccess = (rawRoll == 4);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D6:
                    rawRoll = _random.Next(1, 7);
                    isCriticalSuccess = (rawRoll == 6);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D8:
                    rawRoll = _random.Next(1, 9);
                    isCriticalSuccess = (rawRoll == 8);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D10:
                    rawRoll = _random.Next(1, 11);
                    isCriticalSuccess = (rawRoll == 10);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D12:
                    rawRoll = _random.Next(1, 13);
                    isCriticalSuccess = (rawRoll == 12);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D20:
                    rawRoll = _random.Next(1, 21);
                    isCriticalSuccess = (rawRoll == 20);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.TwoD6:
                    int r6a = _random.Next(1, 7);
                    int r6b = _random.Next(1, 7);
                    rawRoll = r6a + r6b;
                    isCriticalSuccess = (r6a == 6 || r6b == 6);
                    isCriticalFailure = (r6a == 1 && r6b == 1);
                    break;
                case DiceType.TwoD8:
                    int r8a = _random.Next(1, 9);
                    int r8b = _random.Next(1, 9);
                    rawRoll = r8a + r8b;
                    isCriticalSuccess = (r8a == 8 || r8b == 8);
                    isCriticalFailure = (r8a == 1 && r8b == 1);
                    break;
                case DiceType.TwoD10:
                    int r10a = _random.Next(1, 11);
                    int r10b = _random.Next(1, 11);
                    rawRoll = r10a + r10b;
                    isCriticalSuccess = (r10a == 10 || r10b == 10);
                    isCriticalFailure = (r10a == 1 && r10b == 1);
                    break;
                case DiceType.TwoD12:
                    int r12a = _random.Next(1, 13);
                    int r12b = _random.Next(1, 13);
                    rawRoll = r12a + r12b;
                    isCriticalSuccess = (r12a == 12 || r12b == 12);
                    isCriticalFailure = (r12a == 1 && r12b == 1);
                    break;
                case DiceType.TwoD12Plus10:
                    int d1 = _random.Next(1, 13);
                    int d2 = _random.Next(1, 13);
                    rawRoll = d1 + d2 + 10;
                    isCriticalSuccess = (d1 == 12 || d2 == 12);
                    isCriticalFailure = (d1 == 1 && d2 == 1);
                    break;
                default:
                    rawRoll = _random.Next(1, 7);
                    break;
            }

            int total = rawRoll + modifier;
            int differential = total - targetDC;
            bool isSuccess = (differential >= 0) || isCriticalSuccess;

            if (isCriticalFailure)
            {
                isSuccess = false;
            }

            return new DiceRollResult
            {
                DieType = dieType,
                RawRoll = rawRoll,
                Modifier = modifier,
                Total = total,
                TargetDC = targetDC,
                Differential = differential,
                IsSuccess = isSuccess,
                IsCriticalSuccess = isCriticalSuccess,
                IsCriticalFailure = isCriticalFailure
            };
        }
    }
}
