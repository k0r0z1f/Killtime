using System;
using Killtime.Core.Dice;
using Killtime.Core.Combat;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Gestion de l'état dynamique d'un personnage en combat (Livre I, VI et VII).
    /// Intègre la réserve de PA, les seuils d'encaissement et l'arbre de résolution à 0 PV.
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

        // Jet d'initiative (Livre I §4.3) : valeurs roulées par le TurnManager au début du
        // combat, stockées sur l'unité elle-même (meurt avec elle, pas de dictionnaire d'objets).
        public int InitiativeRollTotal { get; set; } = int.MinValue;
        public DiceType InitiativeDie { get; set; } = DiceType.D2;

        public bool IsDead { get; set; }
        public bool IsInLastBreath { get; set; }
        public FatalBlowResolution LastFatalBlowResolution { get; set; } = FatalBlowResolution.None;

        public CharacterSheet Sheet { get; set; }

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

        /// <summary>
        /// Dé de compétence (rang de base + entraînements). En offensif, applique la règle
        /// générale « Corps Augmenté » (Mains Nues : MAG substituable à FOR si éveillé).
        /// </summary>
        public DiceType GetSkillDie(SkillType skill, bool isOffensive = false)
        {
            int baseRank = SkillDefinitions.GetBaseRank(skill, Attributes, isOffensive);
            int training = Sheet != null ? Sheet.GetSkill(skill).TrainingLevel : 0;
            return SkillDefinitions.DieFromTotalSteps(
                SkillDefinitions.CharacteristicSteps(baseRank) + training);
        }

        /// <summary>
        /// Vrai si ce jet bénéficie du Corps Augmenté (Mains Nues offensif, MAG &gt; 0).
        /// Sert à l'affichage du log de combat.
        /// </summary>
        public bool IsMagicAugmented(SkillType skill, bool isOffensive)
        {
            return SkillDefinitions.UsesMagicAugmentation(skill, Attributes, isOffensive);
        }

        public int GetStatusModifier(SkillType skill, bool isOffensive = true)
        {
            if (ActiveStatus == StatusEffect.None) return 0;

            int mod = 0;

            // Livre VII : Déstabilisé (-2 à toutes les épreuves)
            if (ActiveStatus.HasFlag(StatusEffect.Destabilise)) mod -= 2;

            // Livre VII : Étourdi (-1 sur tous les jets)
            if (ActiveStatus.HasFlag(StatusEffect.Etourdi)) mod -= 1;

            // Livre VII : À Terre (-1 attaque, -2 défense/esquive)
            if (ActiveStatus.HasFlag(StatusEffect.ATerre))
            {
                mod += isOffensive ? -1 : -2;
            }

            // Livre VII : Aveugle (-4 tirs distance, -2 mêlée et défense)
            if (ActiveStatus.HasFlag(StatusEffect.Aveugle))
            {
                bool isRanged = (skill == SkillType.Ballistique || skill == SkillType.ProjectilesTir);
                mod += isRanged ? -4 : -2;
            }

            // Livre VII : Immobilisé (-2 esquive active)
            if (ActiveStatus.HasFlag(StatusEffect.Immobilise))
            {
                if (!isOffensive && (skill == SkillType.Esquive || skill == SkillType.Acrobatie)) mod -= 2;
            }

            // Livre VII : Agonisant (-2 détresse vitale / panique)
            if (ActiveStatus.HasFlag(StatusEffect.Agonisant)) mod -= 2;

            return mod;
        }

        public string GetStatusBreakdownString(SkillType skill, bool isOffensive = true)
        {
            if (ActiveStatus == StatusEffect.None) return string.Empty;

            var parts = new System.Collections.Generic.List<string>();
            if (ActiveStatus.HasFlag(StatusEffect.Destabilise)) parts.Add("Déstabilisé -2");
            if (ActiveStatus.HasFlag(StatusEffect.Etourdi)) parts.Add("Étourdi -1");
            if (ActiveStatus.HasFlag(StatusEffect.ATerre)) parts.Add(isOffensive ? "À Terre -1" : "À Terre -2");
            if (ActiveStatus.HasFlag(StatusEffect.Aveugle))
            {
                bool isRanged = (skill == SkillType.Ballistique || skill == SkillType.ProjectilesTir);
                parts.Add(isRanged ? "Aveugle -4" : "Aveugle -2");
            }
            if (ActiveStatus.HasFlag(StatusEffect.Immobilise) && !isOffensive && (skill == SkillType.Esquive || skill == SkillType.Acrobatie))
            {
                parts.Add("Immobilisé -2");
            }
            if (ActiveStatus.HasFlag(StatusEffect.Agonisant)) parts.Add("Agonisant -2");

            return parts.Count > 0 ? string.Join(", ", parts) : string.Empty;
        }

        /// <summary>
        /// RÈGLE CODEX (Livres I-III) : le jet = Dé de compétence + PA injectés + malus situationnels.
        /// Aucun modificateur de caractéristique n'est ajouté : les caracs fixent le dé via GetSkillDie.
        /// Retourne donc uniquement le malus d'états (Livre VII).
        /// </summary>
        public int GetSkillModifier(SkillType skill, bool isOffensive = true)
        {
            return GetStatusModifier(skill, isOffensive);
        }

        public int GetSkillRank(SkillType skill, bool isOffensive = false)
        {
            int baseRank = SkillDefinitions.GetBaseRank(skill, Attributes, isOffensive);
            int training = Sheet != null ? Sheet.GetSkill(skill).TrainingLevel : 0;
            return baseRank + training;
        }

        /// <summary>
        /// INITIATIVE (Livre I §4.3) : base = max(Rapidité, Agilité, Intelligence).
        /// Convertie en dé via l'échelle en paliers (Livre II §6) : 1-2 → d2, 3-4 → d4,
        /// 5-6 → d6, 7-8 → d8, 9-10 → d10. Jet effectué au début du combat (TurnManager).
        /// </summary>
        public int GetInitiativeBaseValue()
        {
            int m = Attributes.Rapidite;
            if (Attributes.Agilite > m) m = Attributes.Agilite;
            if (Attributes.Intelligence > m) m = Attributes.Intelligence;
            return m;
        }

        public DiceType GetInitiativeDie()
        {
            return SkillDefinitions.DieFromTotalSteps(
                SkillDefinitions.CharacteristicSteps(GetInitiativeBaseValue()));
        }

        /// <summary>
        /// Désigne la meilleure compétence de contact pour une attaque en mêlée
        /// (distance ≤ 1) : compare les dés effectifs offensifs (paliers carac, Corps Augmenté
        /// inclus, + entraînements). Ex : Mina frappe en Mains Nues (MAG 5 + 3 entraînements
        /// = 5 niveaux → D12) plutôt qu'au Maniement d'Arme non entraîné.
        /// Égalité → plus entraînée, puis Mains Nues.
        /// </summary>
        public SkillType GetBestMeleeAttackSkill()
        {
            SkillType[] candidates = new SkillType[]
            {
                SkillType.MainsNues,
                SkillType.ManiementArmes,
                SkillType.ArmesContondantes,
                SkillType.ArmesPercantes
            };

            SkillType best = candidates[0];
            DiceType bestDie = GetSkillDie(best, true);
            int bestTraining = Sheet != null ? Sheet.GetSkill(best).TrainingLevel : 0;

            for (int i = 1; i < candidates.Length; i++)
            {
                DiceType die = GetSkillDie(candidates[i], true);
                int training = Sheet != null ? Sheet.GetSkill(candidates[i]).TrainingLevel : 0;
                if ((int)die > (int)bestDie
                    || ((int)die == (int)bestDie && training > bestTraining))
                {
                    best = candidates[i];
                    bestDie = die;
                    bestTraining = training;
                }
            }

            return best;
        }

        public bool HasSpecialization(string specializationName)
        {
            return Sheet != null && Sheet.UnlockedSpecializations != null && Sheet.UnlockedSpecializations.Contains(specializationName);
        }

        public bool CanAttackWithSkill(SkillType skill)
        {
            return CanAttack(GetSkillDie(skill, true));
        }

        public bool CanDefendActively()
        {
            if (!IsAlive) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Inconscient)) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Sonne)) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Paralyse)) return false;
            return true;
        }

        public static bool IsPersistentStatus(StatusEffect effect)
        {
            return effect == StatusEffect.Inconscient
                || effect == StatusEffect.Agonisant
                || effect == StatusEffect.Saignement
                || effect == StatusEffect.Empoisonne
                || effect == StatusEffect.EnFeu
                || effect == StatusEffect.ChronoFracture;
        }

        public void ApplyStatus(StatusEffect effect, int durationInTurns = 1)
        {
            if (effect == StatusEffect.None) return;

            _activeStatus |= effect;

            var allEffects = (StatusEffect[])Enum.GetValues(typeof(StatusEffect));
            for (int i = 0; i < allEffects.Length; i++)
            {
                var flag = allEffects[i];
                if (flag == StatusEffect.None) continue;
                if (effect.HasFlag(flag))
                {
                    if (IsPersistentStatus(flag))
                    {
                        _statusDurations[flag] = -1;
                    }
                    else
                    {
                        int existing = _statusDurations.TryGetValue(flag, out int d) ? d : 0;
                        _statusDurations[flag] = Math.Max(existing, Math.Max(1, durationInTurns));
                    }
                }
            }
        }

        public void RemoveStatus(StatusEffect effect)
        {
            if (effect == StatusEffect.None) return;

            var allEffects = (StatusEffect[])Enum.GetValues(typeof(StatusEffect));
            for (int i = 0; i < allEffects.Length; i++)
            {
                var flag = allEffects[i];
                if (flag != StatusEffect.None && effect.HasFlag(flag))
                {
                    _statusDurations.Remove(flag);
                    _activeStatus &= ~flag;
                }
            }
        }

        public void ClearAllStatus()
        {
            _statusDurations.Clear();
            _activeStatus = StatusEffect.None;
        }

        private void SynchronizeDurationsFromActiveStatus()
        {
            var allEffects = (StatusEffect[])Enum.GetValues(typeof(StatusEffect));
            for (int i = 0; i < allEffects.Length; i++)
            {
                var flag = allEffects[i];
                if (flag == StatusEffect.None) continue;

                if (_activeStatus.HasFlag(flag))
                {
                    if (!_statusDurations.ContainsKey(flag))
                    {
                        _statusDurations[flag] = IsPersistentStatus(flag) ? -1 : 1;
                    }
                }
                else
                {
                    _statusDurations.Remove(flag);
                }
            }
        }

        public System.Collections.Generic.List<StatusEffect> TickTurnStatusDurations()
        {
            var expired = new System.Collections.Generic.List<StatusEffect>();
            var keys = new System.Collections.Generic.List<StatusEffect>(_statusDurations.Keys);

            for (int i = 0; i < keys.Count; i++)
            {
                var flag = keys[i];
                int rem = _statusDurations[flag];

                if (rem > 0)
                {
                    rem--;
                    if (rem <= 0)
                    {
                        expired.Add(flag);
                    }
                    else
                    {
                        _statusDurations[flag] = rem;
                    }
                }
            }

            for (int i = 0; i < expired.Count; i++)
            {
                _statusDurations.Remove(expired[i]);
                _activeStatus &= ~expired[i];
            }

            return expired;
        }

        public void RecalculateDerivedStats()
        {
            MaxActionPoints = Attributes.CalculateBaseActionPoints();
            EncaissementThreshold = Attributes.CalculateEncaissement();
            MaxHealth = Attributes.CalculateLethalMaximum();
            CurrentHealth = MaxHealth;
        }

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
        }

        public bool ConsumeActionPoints(int cost)
        {
            int effectiveCost = ActiveStatus.HasFlag(StatusEffect.Ralenti) 
                ? cost * Rules.CoreRulesConfig.Instance.RalentiAPMultiplier 
                : cost;

            if (CurrentActionPoints >= effectiveCost)
            {
                CurrentActionPoints -= effectiveCost;

                if (IsInLastBreath)
                {
                    Essoufflement++;
                    if (Essoufflement >= Attributes.Constitution)
                    {
                        IsInLastBreath = false;
                        IsDead = true;
                        ActiveStatus |= StatusEffect.Inconscient;
                    }
                }

                return true;
            }

            return false;
        }

        public int GetMaxAttacksAllowed(DiceType attackDie)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return attackDie switch
            {
                DiceType.TwoD6 or DiceType.TwoD8 or DiceType.TwoD10 or DiceType.TwoD12 or DiceType.TwoD12Plus10 => cfg.MultiDiceAttackActionLimit,
                _ => cfg.SingleAttackActionLimit
            };
        }

        public bool CanAttack(DiceType attackDie)
        {
            if (!IsAlive) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Inconscient) || 
                ActiveStatus.HasFlag(StatusEffect.Sonne) || 
                ActiveStatus.HasFlag(StatusEffect.Paralyse))
            {
                return false;
            }
            return AttacksThisTurn < GetMaxAttacksAllowed(attackDie);
        }

        public void RegisterAttack()
        {
            AttacksThisTurn++;
        }

        public bool TakeEmergencyBreath(int bonusAP = -1)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            int appliedBonus = (bonusAP < 0) ? cfg.EmergencyBreathBonusAP : bonusAP;

            if (Essoufflement >= Attributes.Constitution)
            {
                return false;
            }

            Essoufflement += cfg.EmergencyBreathEssoufflementCost;
            CurrentActionPoints += appliedBonus;
            return true;
        }

        public void RecoverBreath()
        {
            if (Essoufflement > 0)
            {
                Essoufflement--;
            }
        }

        public FatalBlowResolution EvaluateFatalBlow(BodyPart hitPart, int finalDamageDealt)
        {
            bool exceedsEncaissement = finalDamageDealt > EncaissementThreshold;

            if (exceedsEncaissement)
            {
                if (hitPart == BodyPart.Tete || hitPart == BodyPart.YeuxVisage)
                {
                    if (Attributes.PointsMiracle > 0)
                    {
                        var attrs = Attributes;
                        attrs.PointsMiracle--;
                        Attributes = attrs;
                        CurrentHealth = 1;
                        ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
                        LastFatalBlowResolution = FatalBlowResolution.MiracleSaved;
                        return FatalBlowResolution.MiracleSaved;
                    }

                    CurrentHealth = 0;
                    IsDead = true;
                    ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
                    LastFatalBlowResolution = FatalBlowResolution.InstantDeath;
                    return FatalBlowResolution.InstantDeath;
                }

                CurrentHealth = 0;
                ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
                if (hitPart == BodyPart.CoeurPoumons || hitPart == BodyPart.CouTrachee)
                {
                    ActiveStatus |= StatusEffect.Agonisant | StatusEffect.Saignement;
                }
                LastFatalBlowResolution = FatalBlowResolution.ForcedUnconscious;
                return FatalBlowResolution.ForcedUnconscious;
            }

            CurrentHealth = 0;
            LastFatalBlowResolution = FatalBlowResolution.EligibleForLastBreath;
            return FatalBlowResolution.EligibleForLastBreath;
        }

        public void ChooseSombrer()
        {
            IsInLastBreath = false;
            ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
            ActiveStatus &= ~StatusEffect.Agonisant;
        }

        public void ChooseLastBreath()
        {
            IsInLastBreath = true;
            ActiveStatus |= StatusEffect.ATerre | StatusEffect.Agonisant;
            ActiveStatus &= ~StatusEffect.Inconscient;
        }

        public bool IsAlive => !IsDead && (CurrentHealth > 0 || IsInLastBreath) && !ActiveStatus.HasFlag(StatusEffect.Inconscient);
    }
}