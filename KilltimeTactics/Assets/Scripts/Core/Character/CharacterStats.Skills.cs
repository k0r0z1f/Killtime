using System;
using Killtime.Core.Dice;
using Killtime.Core.Combat;

namespace Killtime.Core.Character
{
    // Split move-only de CharacterStats — Skills : Jets, modifieurs, initiative, specialisations, Can*.
    public partial class CharacterStats
    {

        /// <summary>
        /// Dé de compétence (rang de base + entraînements). En offensif, applique la règle
        /// générale « Corps Augmenté » (Mains Nues : MAG substituable à FOR si éveillé).
        /// </summary>
        public DiceType GetSkillDie(SkillType skill, bool isOffensive = false)
        {
            int baseRank = SkillDefinitions.GetBaseRank(skill, Attributes, isOffensive);
            int training = Sheet != null ? Sheet.GetSkill(skill).TrainingLevel : 0;
            var die = SkillDefinitions.DieFromTotalSteps(
                SkillDefinitions.CharacteristicSteps(baseRank) + training);

            if (skill == SkillType.Esquive && HasSpecialization("Réflexes Myotatiques"))
            {
                die = SkillDefinitions.StepUpDie(die);
            }

            return die;
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
            // RD-083 : la séquelle de résurrection n'est pas un flag — elle s'applique
            // même sans aucun statut actif.
            if (ActiveStatus == StatusEffect.None && !HasResurrectionSequelae()) return 0;

            int mod = 0;

            // Livre VI : Déstabilisé (-2 à toutes les épreuves, -1 avec Trempe de Fer)
            if (ActiveStatus.HasFlag(StatusEffect.Destabilise))
                mod -= HasSpecialization("Trempe de Fer") ? 1 : 2;

            // Livre VI : Étourdi (-1 sur tous les jets, ignoré avec Esprit de Granit)
            if (ActiveStatus.HasFlag(StatusEffect.Etourdi) && !HasSpecialization("Trempe de Fer : Esprit de Granit")) mod -= 1;

            // Livre VI : À Terre (-1 attaque — ignoré avec Ignorer la Douleur, -2 défense/esquive)
            if (ActiveStatus.HasFlag(StatusEffect.ATerre))
            {
                if (isOffensive)
                    mod += HasSpecialization("Trempe de Fer : Ignorer la Douleur") ? 0 : -1;
                else
                    mod -= 2;
            }

            // Livre VI : Aveugle (-4 tirs distance, -2 mêlée et défense)
            if (ActiveStatus.HasFlag(StatusEffect.Aveugle))
            {
                bool isRanged = (skill == SkillType.Ballistique || skill == SkillType.ProjectilesTir);
                mod += isRanged ? -4 : -2;
            }

            // Livre VI : Immobilisé (-2 esquive active)
            if (ActiveStatus.HasFlag(StatusEffect.Immobilise))
            {
                if (!isOffensive && (skill == SkillType.Esquive || skill == SkillType.Acrobatie)) mod -= 2;
            }

            // Livre VI : Agonisant (-2 détresse vitale / panique)
            if (ActiveStatus.HasFlag(StatusEffect.Agonisant)) mod -= 2;

            // RD-083 (Livre VI §25.3) : Poids du Trépas — ressuscité affaibli :
            // malus sur TOUS les jets pendant (10 - CON) jours réels.
            mod += GetResurrectionSequelaeModifier();

            // Livre IV §19 : bénédictions arcanotech offensives (+EC au jet).
            // Survolte +1 EC, EnTranse +2 EC — offensif uniquement, cumulables.
            if (isOffensive)
            {
                if (ActiveStatus.HasFlag(StatusEffect.Survolte)) mod += 1;
                if (ActiveStatus.HasFlag(StatusEffect.EnTranse)) mod += 2;
            }

            return mod;
        }

        public string GetStatusBreakdownString(SkillType skill, bool isOffensive = true)
        {
            // RD-083 : afficher le Poids du Trépas même sans flag de statut actif.
            if (ActiveStatus == StatusEffect.None && !HasResurrectionSequelae()) return string.Empty;

            var parts = new System.Collections.Generic.List<string>();
            if (ActiveStatus.HasFlag(StatusEffect.Destabilise))
                parts.Add(HasSpecialization("Trempe de Fer") ? "Déstabilisé -1 (Trempe)" : "Déstabilisé -2");
            if (ActiveStatus.HasFlag(StatusEffect.Etourdi))
            {
                if (!HasSpecialization("Trempe de Fer : Esprit de Granit")) parts.Add("Étourdi -1");
            }
            if (ActiveStatus.HasFlag(StatusEffect.ATerre))
                parts.Add(isOffensive
                    ? (HasSpecialization("Trempe de Fer : Ignorer la Douleur") ? "À Terre 0 (Douleur ignorée)" : "À Terre -1")
                    : "À Terre -2");
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
            if (HasResurrectionSequelae()) parts.Add($"Poids du Trépas {GetResurrectionSequelaeModifier()}ec ({GetResurrectionSequelaeRemainingDays()}j restants)");
            if (isOffensive)
            {
                if (ActiveStatus.HasFlag(StatusEffect.Survolte)) parts.Add("Survolte +1ec");
                if (ActiveStatus.HasFlag(StatusEffect.EnTranse)) parts.Add("EnTranse +2ec");
            }

            return parts.Count > 0 ? string.Join(", ", parts) : string.Empty;
        }

        /// <summary>
        /// RÈGLE CODEX (Livres I-III) : le jet = Dé de compétence + PA injectés + malus situationnels.
        /// Aucun modificateur de caractéristique n'est ajouté : les caracs fixent le dé via GetSkillDie.
        /// Retourne donc uniquement le malus d'états (Livre VI).
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
        /// INITIATIVE (Livre I §4.3) : base = max(Rapidité, Agilité, Intelligence),
        /// étendue à l'Instinct avec la spécialisation Sens du Danger (Intuition).
        /// Convertie en dé via l'échelle en paliers (Livre II §6) : 1-2 → d2, 3-4 → d4,
        /// 5-6 → d6, 7-8 → d8, 9-10 → d10. Jet effectué au début du combat (TurnManager).
        /// </summary>
        public int GetInitiativeBaseValue()
        {
            int m = Attributes.Rapidite;
            if (Attributes.Agilite > m) m = Attributes.Agilite;
            if (Attributes.Intelligence > m) m = Attributes.Intelligence;
            if (HasSpecialization("Sens du Danger") && Attributes.Instinct > m) m = Attributes.Instinct;
            return m;
        }

        public DiceType GetInitiativeDie()
        {
            var die = SkillDefinitions.DieFromTotalSteps(
                SkillDefinitions.CharacteristicSteps(GetInitiativeBaseValue()));
            // Premiers Réflexes : +1 palier sur le jet d'Initiative.
            if (HasSpecialization("Sens du Danger : Premiers Réflexes"))
                die = SkillDefinitions.StepUpDie(die);
            return die;
        }

        /// <summary>
        /// Désigne la meilleure compétence de contact pour une attaque en mêlée
        /// (distance ≤ 1) : compare les dés effectifs offensifs (paliers carac, augmentation
        /// magique incluse, + entraînements). Ex : un corps augmenté frappe en Mains Nues
        /// (MAG 5 + 3 entraînements = 5 niveaux → D12) plutôt qu'au Maniement d'Arme non entraîné.
        /// Égalité → plus entraînée, puis Mains Nues.
        /// </summary>
        public SkillType GetBestMeleeAttackSkill()
        {
            SkillType[] candidates = new SkillType[]
            {
                SkillType.MainsNues,
                SkillType.ManiementArmes,
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
            if (!IsAlive || IsSurrendered) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Inconscient)) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Sonne)) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Paralyse)) return false;
            return true;
        }

        /// <summary>
        /// Vrai si l'unité est en état physique de se déplacer sur la grille.
        /// Faux si morte, inconsciente, sonnée, paralysée ou immobilisée (RD-039).
        /// </summary>
        public bool CanMove()
        {
            if (!IsAlive) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Inconscient)) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Sonne)) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Paralyse)) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Immobilise)) return false;
            if (Combat.GrappleState.IsGrappled(this)) return false;
            return true;
        }
    }
}
