using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Progressions organiques (Livre I §5), tables anatomiques (§26.1) et
    /// prédicats d'armes RD-082. Que des traitements sans état de duel :
    /// dépend uniquement du contexte (dés) — jamais des autres services.
    /// Corps copiés à l'identique depuis l'ancien CombatCalculator.
    /// </summary>
    public sealed class CombatProgressionService
    {
        private readonly CombatDuelContext _ctx;

        public CombatProgressionService(CombatDuelContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        /// <summary>
        /// Progression organique (Livre I §5) : sur réussite, coche +1 case dans la
        /// compétence utilisée. Retourne le suffixe de log (vide si rien / échec / sans fiche).
        /// </summary>
        internal string ApplySkillProgression(CharacterStats user, SkillType skill, bool isOffensive)
        {
            if (user == null || user.Sheet == null) return string.Empty;
            if (!CharacterProgressionManager.RegisterSuccessfulSkillUse(user.Sheet, skill, out string prog, out _, isOffensive))
                return string.Empty;
            if (string.IsNullOrEmpty(prog)) return string.Empty;
            return $"\n   {prog}";
        }

        internal string ApplyOpposedProgression(CharacterStats attacker, SkillType attackSkill, bool attOffensive, CharacterStats defender, SkillType defenseSkill, bool defenderPlays, int differential)
        {
            if (differential >= 0)
                return ApplySkillProgression(attacker, attackSkill, attOffensive);
            if (defenderPlays)
                return ApplySkillProgression(defender, defenseSkill, false);
            return string.Empty;
        }

        /// <summary>
        /// Rétro-compatibilité : redirige vers la table d6 exacte du Livre VI §26.1.
        /// </summary>
        private BodyPart GetAdjacentBodyPart(BodyPart targeted)
        {
            return ResolveAnatomicalDeflection(targeted, out _, out _);
        }

        public static bool IsUpperBodyPart(BodyPart part)
        {
            return part == BodyPart.Tete
                || part == BodyPart.YeuxVisage
                || part == BodyPart.CouTrachee
                || part == BodyPart.CoeurPoumons
                || part == BodyPart.Torse;
        }

        /// <summary>
        /// Table de déviation d4/d6 chirurgicale officielle (Livre VI §26.1).
        /// </summary>
        public BodyPart ResolveAnatomicalDeflection(BodyPart targeted, out int rollDie, out string medicalConsequence)
        {
            rollDie = _ctx.DiceRoller != null ? _ctx.DiceRoller.Roll(DiceType.D6, 0, 0).RawRoll : _ctx.Random.Next(1, 7);
            if (rollDie < 1 || rollDie > 6) rollDie = _ctx.Random.Next(1, 7);

            bool isUpper = IsUpperBodyPart(targeted);

            if (isUpper)
            {
                switch (rollDie)
                {
                    case 1:
                        medicalConsequence = "Cécité partielle, hémorragie faciale, désorientation";
                        return BodyPart.YeuxVisage;
                    case 2:
                        medicalConsequence = "Risque d'asphyxie, perte d'essoufflement immédiate";
                        return BodyPart.CouTrachee;
                    case 3:
                        medicalConsequence = "Commotion, épreuve de Constitution pour rester conscient";
                        return BodyPart.Tete;
                    case 4:
                        medicalConsequence = "Dégâts massifs, perforation, incapacité respiratoire";
                        return BodyPart.CoeurPoumons;
                    case 5:
                    case 6:
                    default:
                        medicalConsequence = "Encaissement standard";
                        return BodyPart.Torse;
                }
            }
            else
            {
                switch (rollDie)
                {
                    case 1:
                        medicalConsequence = "Impact claviculaire / épaule, désorientation";
                        return targeted == BodyPart.BrasGauche ? BodyPart.BrasGauche : (targeted == BodyPart.BrasDroit ? BodyPart.BrasDroit : BodyPart.Torse);
                    case 2:
                        medicalConsequence = "Impact bras droit (porteur d'arme)";
                        return BodyPart.BrasDroit;
                    case 3:
                        medicalConsequence = "Impact bras gauche (bouclier / parade)";
                        return BodyPart.BrasGauche;
                    case 4:
                        medicalConsequence = "Abdomen et côtes heurtés, incapacité respiratoire";
                        return BodyPart.Torse;
                    case 5:
                    case 6:
                    default:
                        medicalConsequence = "Encaissement standard, perte de motricité si jambe brisée";
                        return BodyPart.Jambes;
                }
            }
        }

        /// <summary>
        /// RD-082 (Livre VII §28.4) : Vérifie si l'attaque utilise une arme tranchante ou perçante.
        /// </summary>
        public static bool IsSlashingOrPiercingAttack(SkillType skill, Killtime.Core.Inventory.InventoryItem weapon, CharacterStats attacker)
        {
            SkillType baseSkill = SkillDefinitions.ResolveBaseSkill(skill);
            if (baseSkill == SkillType.ArmesPercantes) return true;

            if (weapon != null)
            {
                string desc = $"{weapon.Name} {weapon.Description} {weapon.Category}".ToLowerInvariant();
                if (desc.Contains("marteau") || desc.Contains("masse") || desc.Contains("gourdin") || desc.Contains("matraque") || desc.Contains("bâton") || desc.Contains("baton"))
                {
                    return false;
                }

                if (desc.Contains("tranchant") || desc.Contains("perçant") || desc.Contains("percant") ||
                    desc.Contains("épée") || desc.Contains("epee") || desc.Contains("lame") ||
                    desc.Contains("dague") || desc.Contains("hache") || desc.Contains("lance") ||
                    desc.Contains("pique") || desc.Contains("couteau") || desc.Contains("rapière") ||
                    desc.Contains("rapiere") || desc.Contains("baïonnette") || desc.Contains("baionnette") ||
                    desc.Contains("flèche") || desc.Contains("fleche") || desc.Contains("carreau"))
                {
                    return true;
                }
            }

            if (baseSkill == SkillType.ManiementArmes)
            {
                if (attacker != null && (attacker.HasSpecialization("Armes Contondantes") || attacker.HasSpecialization("Marteau de Guerre")))
                {
                    return false;
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// RD-082 (Livre VII §28.4) : Vérifie si l'attaque utilise une énergie thermique, feu ou plasma.
        /// </summary>
        public static bool IsFireOrPlasmaAttack(SkillType skill, Killtime.Core.Inventory.InventoryItem weapon, CharacterStats attacker)
        {
            if (skill == SkillType.MagieElementale || skill == SkillType.MagiePrimale)
            {
                if (attacker != null && (attacker.HasSpecialization("Feu") || attacker.HasSpecialization("Flamme") || attacker.HasSpecialization("Pyromancie") || attacker.HasSpecialization("Thermique")))
                    return true;
            }

            if (weapon != null)
            {
                string desc = $"{weapon.Name} {weapon.Description} {weapon.Category} {weapon.AmmoType} {weapon.GrenadeKind}".ToLowerInvariant();
                if (desc.Contains("feu") || desc.Contains("flamme") || desc.Contains("plasma") ||
                    desc.Contains("incendiaire") || desc.Contains("thermique") || desc.Contains("pyro") ||
                    desc.Contains("laser") || desc.Contains("brûl") || desc.Contains("brul") ||
                    desc.Contains("thermobarique"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// RD-082 (Livre VII §28.4) : Vérifie si l'assaillant possède la spécialisation Destruction de Rang 6.
        /// </summary>
        public static bool HasDestructionTier6(CharacterStats attacker)
        {
            if (attacker == null) return false;
            if (attacker.HasSpecialization("Destruction 6")
                || attacker.HasSpecialization("Destruction : Rang 6")
                || attacker.HasSpecialization("Destruction : Stade 6")
                || attacker.HasSpecialization("Voie de la Destruction : Rang 6")
                || attacker.HasSpecialization("Voie de la Destruction 6")
                || attacker.HasSpecialization("Destruction"))
            {
                return true;
            }

            if (attacker.Sheet?.UnlockedSpecializations != null)
            {
                for (int i = 0; i < attacker.Sheet.UnlockedSpecializations.Count; i++)
                {
                    string spec = attacker.Sheet.UnlockedSpecializations[i];
                    if (string.IsNullOrEmpty(spec)) continue;
                    if (spec.IndexOf("Destruction", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (spec.Contains("6") || spec.IndexOf("VI", StringComparison.OrdinalIgnoreCase) >= 0 || spec.Equals("Destruction", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }

            return false;
        }

        internal StatusEffect DetermineInflictedStatus(BodyPart hitPart)
        {
            return hitPart switch
            {
                BodyPart.Tete => StatusEffect.Sonne | StatusEffect.Etourdi,
                BodyPart.YeuxVisage => StatusEffect.Aveugle | StatusEffect.Saignement,
                BodyPart.CouTrachee => StatusEffect.Asphyxie,
                BodyPart.CoeurPoumons => StatusEffect.Agonisant | StatusEffect.Saignement,
                BodyPart.Torse => StatusEffect.Destabilise,
                BodyPart.BrasDroit or BodyPart.BrasGauche => StatusEffect.Destabilise,
                BodyPart.Jambes => StatusEffect.ATerre | StatusEffect.Ralenti,
                _ => StatusEffect.Destabilise
            };
        }
    }
}
