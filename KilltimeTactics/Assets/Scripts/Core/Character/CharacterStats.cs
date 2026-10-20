using System;
using Killtime.Core.Dice;
using Killtime.Core.Combat;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Gestion de l'état dynamique d'un personnage en combat (Livre I, V et VI).
    /// Intègre la réserve de PA, les seuils d'encaissement et l'arbre de résolution à 0 PV.
    /// </summary>
    [Serializable]
    public partial class CharacterStats
    {
        public string Name { get; set; }
        public Attributes Attributes { get; set; }

        public int MaxHealth { get; private set; }
        public int CurrentHealth { get; set; }
        public int EncaissementThreshold { get; private set; }

        public int MaxActionPoints { get; private set; }
        public int CurrentActionPoints { get; set; }

        public int Essoufflement { get; set; }

        private StatusEffect _activeStatus = StatusEffect.None;
        public StatusEffect ActiveStatus
        {
            get => _activeStatus;
            set
            {
                _activeStatus = value;
                SynchronizeDurationsFromActiveStatus();
            }
        }

        private readonly System.Collections.Generic.Dictionary<StatusEffect, int> _statusDurations = new();
        public System.Collections.Generic.IReadOnlyDictionary<StatusEffect, int> StatusDurations => _statusDurations;

        public int BaseArmorAbsorption { get; set; }
        public int AttacksThisTurn { get; private set; }
        public int MovesThisTurn { get; private set; }
        public bool HasUsedSecondeRespiration { get; set; }

        // Barrière ablative des champs de force portés (Livre VII §28.2) : absorbe
        // les dégâts avant l'armure et la chair. Rechargée hors combat / en début de combat.
        public int MaxShieldHP { get; private set; }
        public int CurrentShieldHP { get; set; }

        // Jet d'initiative (Livre I §4.3) : valeurs roulées par le TurnManager au début du
        // combat, stockées sur l'unité elle-même (meurt avec elle, pas de dictionnaire d'objets).
        public int InitiativeRollTotal { get; set; } = int.MinValue;
        public DiceType InitiativeDie { get; set; } = DiceType.D2;

        // Duo-Tech : round (TurnManager.CurrentRound) du dernier duo tissé, tous
        // rôles confondus (initiateur ou partenaire). 0 = aucun. Limite : 1 duo
        // par personnage par round. Remis à 0 à chaque nouveau combat.
        public int LastDuoTechRound { get; set; } = 0;

        // RD-085 (Livre V §21.5) : Mirage à Deux Temps — réactions défensives
        // gratuites (0 PA) gagnées sur Parfaite, valables jusqu'au prochain tour
        // personnel de Mina. Décrémenté par ConsumeFreeDefensiveReaction lors de
        // la prochaine défense active ; expiration du reliquat au MJ / TurnManager.
        public int FreeDefensiveReactions { get; set; } = 0;

        public void GrantFreeDefensiveReaction(int count = 1)
        {
            if (count <= 0 || !IsAlive) return;
            FreeDefensiveReactions += count;
        }

        public bool HasFreeDefensiveReaction => FreeDefensiveReactions > 0 && IsAlive;

        public bool ConsumeFreeDefensiveReaction()
        {
            if (!HasFreeDefensiveReaction) return false;
            FreeDefensiveReactions--;
            return true;
        }

        public bool IsDead { get; set; }
        public bool IsInLastBreath { get; set; }
        public FatalBlowResolution LastFatalBlowResolution { get; set; } = FatalBlowResolution.None;

        // RD-047 : Déroute, Reddition & Moral
        public bool IsPlayerControlled { get; set; }
        public bool IsSurvivor { get; set; }
        public bool HasTestedMorale { get; set; }
        public bool IsSurrendered { get; set; }

        public CharacterSheet Sheet { get; set; }

        // RD-038 : Charge & Sprint (Positionnement)
        public bool HasSprintedThisTurn => ChargeState.HasSprinted(this);
        public bool HasChargeBonus => ChargeState.HasChargeBonus(this);
        public bool HasChargeDefensePenalty => ChargeState.HasDefensePenalty(this);
        public int TilesMovedThisTurn => ChargeState.GetTilesMovedThisTurn(this);

        public void RegisterMove(int tilesMoved = 0)
        {
            MovesThisTurn++;
            if (tilesMoved > 0)
            {
                ChargeState.ApplyMovement(this, tilesMoved);
            }
        }

        public CharacterStats(string name, Attributes attributes, int baseArmor = 0, CharacterSheet sheet = null)
        {
            Name = name;
            Attributes = attributes;
            BaseArmorAbsorption = baseArmor;
            Sheet = sheet;
            ActiveStatus = StatusEffect.None;
            Essoufflement = 0;
            AttacksThisTurn = 0;
            IsDead = false;
            IsInLastBreath = false;

            RecalculateDerivedStats();
            ResetTurn();
        }

        public void RecalculateDerivedStats()
        {
            MaxActionPoints = Attributes.CalculateBaseActionPoints();
            EncaissementThreshold = Attributes.CalculateEncaissement();

            if (HasSpecialization("Éveil Chlorophyllien : Peau de Chlorophylle"))
            {
                BaseArmorAbsorption += 2;
            }

            if (HasSpecialization("Encaissement Élastique : Tissu Myo-Amortisseur"))
            {
                EncaissementThreshold += 4;
                BaseArmorAbsorption += 2;
            }
            else if (HasSpecialization("Encaissement Élastique"))
            {
                EncaissementThreshold += 2;
                BaseArmorAbsorption += 1;
            }

            if (HasSpecialization("Fibres Résilientes : Chair d'Inflexible"))
            {
                EncaissementThreshold += 8;
                BaseArmorAbsorption += 1;
            }
            else if (HasSpecialization("Fibres Résilientes : Carapace Sous-Cutanée"))
            {
                EncaissementThreshold += 6;
            }
            else if (HasSpecialization("Fibres Résilientes : Tissu Densifié"))
            {
                EncaissementThreshold += 4;
            }
            else if (HasSpecialization("Fibres Résilientes"))
            {
                EncaissementThreshold += 2;
            }

            // Condition de Fer (Endurance Physique) : cuirasse du marcheur infatigable.
            if (HasSpecialization("Condition de Fer : Mur de Chair"))
            {
                EncaissementThreshold += 2;
                BaseArmorAbsorption += 1;
            }
            else if (HasSpecialization("Condition de Fer : Dur à Cuire"))
            {
                EncaissementThreshold += 2;
            }

            MaxHealth = Attributes.CalculateLethalMaximum();
            CurrentHealth = MaxHealth;
            RecalcShieldMax();
        }

        /// <summary>
        /// Refresh individuel (Livres I §4 + VI §20) : appelé AU DÉBUT DU TOUR
        /// PERSONNEL de l'unité (quand son initiative arrive), jamais au début
        /// du round global. Remise au maximum SANS cumul : les PA restants sont
        /// écrasés. L'Essoufflement persiste : il se soigne via Reprendre son
        /// Souffle (1 PA = 1 PE) en début de son propre tour.
        /// </summary>
        public void ResetTurn()
        {
            if (IsInLastBreath)
            {
                CurrentActionPoints = (int)Math.Round(MaxActionPoints * Rules.CoreRulesConfig.Instance.LastBreathAPRatio);
            }
            else
            {
                CurrentActionPoints = MaxActionPoints;
            }

            AttacksThisTurn = 0;
            MovesThisTurn = 0;
            ChargeState.OnUnitTurnStart(this);

            if (IsAlive && CurrentHealth < MaxHealth)
            {
                if (HasSpecialization("Homéostasie Accélérée : Pulsion Phénix"))
                {
                    CurrentHealth = Math.Min(MaxHealth, CurrentHealth + 4);
                    // RD-083 : régénération arcanique ≠ Premiers Soins/Chirurgie —
                    // la blessure critique reste ouverte (Souffrant préservé).
                    bool keepSouffrant = IsSouffrant;
                    ClearAllStatus();
                    if (keepSouffrant) ApplyStatus(StatusEffect.Souffrant);
                }
                else if (HasSpecialization("Homéostasie Accélérée : Coagulation Flash"))
                {
                    CurrentHealth = Math.Min(MaxHealth, CurrentHealth + 3);
                    RemoveStatus(StatusEffect.Saignement);
                    RemoveStatus(StatusEffect.Empoisonne);
                    RemoveStatus(StatusEffect.EnFeu);
                }
                else if (HasSpecialization("Homéostasie Accélérée : Régénération Cellulaire Avancée"))
                {
                    CurrentHealth = Math.Min(MaxHealth, CurrentHealth + 2);
                    RemoveStatus(StatusEffect.Saignement);
                    RemoveStatus(StatusEffect.Empoisonne);
                }
                else if (HasSpecialization("Homéostasie Accélérée") || HasSpecialization("Régénération Métabolique"))
                {
                    CurrentHealth = Math.Min(MaxHealth, CurrentHealth + 1);
                    RemoveStatus(StatusEffect.Saignement);
                }
                else if (HasSpecialization("Condition de Fer"))
                {
                    // Endurance Physique : récupération du marcheur (sans cautérisation).
                    CurrentHealth = Math.Min(MaxHealth, CurrentHealth + 1);
                }
            }

            if (CurrentHealth > (int)Math.Ceiling(MaxHealth * 0.28f))
            {
                HasTestedMorale = false;
            }
        }
    }
}
