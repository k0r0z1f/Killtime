using System;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Gestion de l'état dynamique d'un personnage en combat (Livre I, VI et VII).
    /// </summary>
    [Serializable]
    public class CharacterStats
    {
        public string Name { get; set; }
        public Attributes Attributes { get; set; }

        public int MaxHealth { get; private set; }
        public int CurrentHealth { get; set; }
        public int EncaissementThreshold { get; private set; }

        public int MaxActionPoints { get; private set; }
        public int CurrentActionPoints { get; set; }

        public int Essoufflement { get; set; }
        public StatusEffect ActiveStatus { get; set; }

        public int BaseArmorAbsorption { get; set; }

        public CharacterStats(string name, Attributes attributes, int baseArmor = 0)
        {
            Name = name;
            Attributes = attributes;
            BaseArmorAbsorption = baseArmor;
            ActiveStatus = StatusEffect.None;
            Essoufflement = 0;

            RecalculateDerivedStats();
            ResetTurn();
        }

        public void RecalculateDerivedStats()
        {
            MaxActionPoints = Attributes.CalculateBaseActionPoints();
            EncaissementThreshold = Attributes.CalculateEncaissement();
            MaxHealth = Attributes.CalculateLethalMaximum();
            CurrentHealth = MaxHealth;
        }

        /// <summary>
        /// Réinitialise les PA au début d'un nouveau round de 10 secondes.
        /// L'essoufflement reste persistant tant qu'une action de récupération n'est pas effectuée.
        /// </summary>
        public void ResetTurn()
        {
            CurrentActionPoints = MaxActionPoints;
            
            // Si le personnage est Ralenti, ses PA effectifs sont impactés
            if (ActiveStatus.HasFlag(StatusEffect.Ralenti))
            {
                // En alternative au double coût des actions, ou réduction
            }
        }

        /// <summary>
        /// Dépense de PA pour effectuer une action tactique.
        /// </summary>
        public bool ConsumeActionPoints(int cost)
        {
            int effectiveCost = ActiveStatus.HasFlag(StatusEffect.Ralenti) ? cost * 2 : cost;

            if (CurrentActionPoints >= effectiveCost)
            {
                CurrentActionPoints -= effectiveCost;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Recours à l'Essoufflement d'urgence (Livre I & VI) :
        /// Lorsque le personnage n'a plus de PA mais doit absolument agir,
        /// il gagne 1 point d'Essoufflement pour obtenir des PA d'urgence (ex: 2 PA).
        /// </summary>
        public bool TakeEmergencyBreath(int bonusAP = 2)
        {
            if (Essoufflement >= Attributes.Constitution)
            {
                // Épuisement critique : le corps refuse d'aller au-delà de sa Constitution
                return false;
            }

            Essoufflement += 1;
            CurrentActionPoints += bonusAP;
            return true;
        }

        /// <summary>
        /// Action de récupération pour dissiper l'essoufflement.
        /// </summary>
        public void RecoverBreath()
        {
            if (Essoufflement > 0)
            {
                Essoufflement--;
            }
        }

        public bool IsAlive => CurrentHealth > 0 && !ActiveStatus.HasFlag(StatusEffect.Inconscient);
    }
}
