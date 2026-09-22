using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Core.Character.Classes
{
    /// <summary>
    /// Archétype de classe de base (Livre I — création rapide).
    /// Un preset chargeable qui applique attributs + entraînements + spécialisations
    /// de départ, puis sert de GUIDE de build : l'arbre reste 100% libre
    /// (comme un PJ normal), mais les étapes futures du build sont highlightées
    /// dans la Voûte Céleste et l'onglet Progression.
    ///
    /// Convention : un fichier par classe sous Classes/Classe*.cs.
    /// Chaque fichier expose une factory statique retournant un CharacterClassDefinition.
    /// </summary>
    [Serializable]
    public class CharacterClassDefinition
    {
        public string ClassId = "inconnu";
        public string DisplayName = "Classe Inconnue";
        public string Tagline = "";
        [TextArea(2, 6)]
        public string Description = "";
        public string IconGlyph = "⚔️";
        public Color ThemeColor = new Color(0.2f, 0.75f, 1f);

        public SpeciesType SuggestedSpecies = SpeciesType.Humain;
        public CharacterProfileType Profile = CharacterProfileType.HerosPJ;

        // Attributs de BASE (avant modificateurs d'espèce). Doivent respecter
        // les budgets Livre I (Héros : 1x5, 1x4, 6 autres ≤3, total secondaires 15 / 17 si MAG>0).
        public Attributes BaseAttributes = new Attributes(@for: 5, agi: 3, con: 4, rap: 3, @int: 2, eru: 3, cha: 1, ins: 3, mag: 0);

        public int BaseArmor = 1;
        public int StartingCreditsCE = 8000;

        /// <summary>Entraînements appliqués AU CHARGEMENT (niveau final de départ, 0-3).</summary>
        public List<SkillTrainingStep> StartingTrainings = new List<SkillTrainingStep>();

        /// <summary>Spécialisations débloquées AU CHARGEMENT (ordre parents-first). Sous-ensemble de BuildPath.</summary>
        public List<string> StartingSpecializations = new List<string>();

        /// <summary>
        /// Chemin complet du build, ORDONNÉ parents-first. StartingSpecializations doit en être
        /// le préfixe. Tout le reste = progression future highlightée (NEXT si parent acquis, FUTURE sinon).
        /// </summary>
        public List<string> BuildPath = new List<string>();

        /// <summary>Objectifs d'entraînement finaux (highlight des compétences). Ex: Ballistique → 3.</summary>
        public List<SkillTrainingStep> TargetTrainings = new List<SkillTrainingStep>();

        /// <summary>Notes de pilotage affichées dans le créateur (conseils PA, sorts, équipement).</summary>
        public List<string> PlayTips = new List<string>();

        // ------------------------------------------------------------------
        // APPLICATION (chargement rapide)
        // ------------------------------------------------------------------

        /// <summary>
        /// Applique le preset sur une fiche : attributs + reset arbre + package de départ
        /// (sans coût XP — package de création), puis arme le guide de build.
        /// L'arbre reste entièrement libre après chargement.
        /// </summary>
        public bool ApplyToSheet(CharacterSheet sheet, out string message)
        {
            if (sheet == null)
            {
                message = "Fiche invalide.";
                return false;
            }

            sheet.Profile = Profile;
            sheet.Species = SuggestedSpecies;
            sheet.BaseAttributes = BaseAttributes;
            sheet.BaseArmor = BaseArmor;
            sheet.CreditsCE = StartingCreditsCE;

            // Reset propre : on repart d'un arbre vierge (sans toucher aux sorts ni à l'XP libre).
            sheet.InitializeDefaultSkills();
            if (sheet.UnlockedSpecializations == null) sheet.UnlockedSpecializations = new List<string>();
            else sheet.UnlockedSpecializations.Clear();
            sheet.FreeTrainingsUsed = 0;
            if (sheet.Skills != null)
                for (int i = 0; i < sheet.Skills.Count; i++)
                    if (sheet.Skills[i] != null)
                    {
                        sheet.Skills[i].TrainingLevel = 0;
                        sheet.Skills[i].ProgressTicks = 0;
                        sheet.Skills[i].ReserveXP = 0;
                    }

            // 1. Entraînements de départ (application directe, sans dépense XP).
            // Package de création : TOUS comptés comme gratuits (même au-delà du
            // budget Érudition) pour que « Reset arbre » ne rembourse rien du preset
            // (voir ResetProgressionTree : les niveaux du preset ne sont jamais remboursés).
            int totalLevels = 0;
            if (StartingTrainings != null)
                for (int i = 0; i < StartingTrainings.Count; i++)
                {
                    var step = StartingTrainings[i];
                    if (step == null) continue;
                    var entry = sheet.GetSkill(step.Skill);
                    entry.TrainingLevel = Math.Clamp(step.Level, 0, CharacterProgressionManager.MAX_SKILL_TRAINING);
                    totalLevels += Math.Max(0, entry.TrainingLevel);
                }
            sheet.FreeTrainingsUsed = Math.Max(0, totalLevels);

            // 2. Spécialisations de départ (application directe, sans dépense XP,
            // en respectant l'ordre parents-first ; on ignore les prérequis manquants
            // en les chaînant : si un parent manque dans la liste, on l'ajoute).
            if (StartingSpecializations != null)
                for (int i = 0; i < StartingSpecializations.Count; i++)
                {
                    string spec = StartingSpecializations[i];
                    if (string.IsNullOrWhiteSpace(spec)) continue;
                    EnsureParentChain(sheet, spec);
                    if (!sheet.UnlockedSpecializations.Contains(spec))
                        sheet.UnlockedSpecializations.Add(spec);
                }

            // 3. Arme le guide de build (highlight des étapes futures).
            sheet.ActiveClassId = ClassId;
            sheet.BuildGuideEnabled = true;

            int specCount = sheet.UnlockedSpecializations.Count;
            message = $"{IconGlyph} Archétype '{DisplayName}' chargé : attributs Codex, {totalLevels} niveau(x) d'entraînement, {specCount} spécialisation(s) de départ. Guide de build activé ({BuildPath?.Count ?? 0} étapes) — arbre toujours libre, prochaines étapes surlignées en or.";
            return true;
        }

        private static void EnsureParentChain(CharacterSheet sheet, string spec)
        {
            // Remonte la chaîne de prérequis via le registre et ajoute les parents manquants.
            // Boucle bornée (profondeur max 8) pour éviter toute récursion infinie.
            string current = spec;
            var chain = new List<string>();
            for (int depth = 0; depth < 8; depth++)
            {
                if (!CharacterProgressionManager.TryGetParentSpecialization(current, out string parent)) break;
                if (string.IsNullOrWhiteSpace(parent)) break;
                chain.Add(parent);
                current = parent;
            }
            for (int i = chain.Count - 1; i >= 0; i--)
                if (!sheet.UnlockedSpecializations.Contains(chain[i]))
                    sheet.UnlockedSpecializations.Add(chain[i]);
        }

        // ------------------------------------------------------------------
        // GUIDE DE BUILD (highlight)
        // ------------------------------------------------------------------

        public enum GuideStepState
        {
            NotInBuild = 0,
            Acquired = 1,
            Next = 2,   // parent acquis (ou racine), à débloquer en priorité — highlight OR fort
            Future = 3  // parent manquant — highlight OR discret (chemin du build)
        }

        /// <summary>État d'une spécialisation vis-à-vis du guide de CETTE classe.</summary>
        public GuideStepState GetSpecState(CharacterSheet sheet, string specName)
        {
            if (sheet == null || string.IsNullOrWhiteSpace(specName)) return GuideStepState.NotInBuild;
            if (BuildPath == null || !BuildPath.Contains(specName)) return GuideStepState.NotInBuild;
            if (sheet.UnlockedSpecializations != null && sheet.UnlockedSpecializations.Contains(specName))
                return GuideStepState.Acquired;
            // NEXT si le parent est acquis (ou si racine sans parent).
            if (!CharacterProgressionManager.TryGetParentSpecialization(specName, out string parent)
                || string.IsNullOrWhiteSpace(parent))
                return GuideStepState.Next;
            if (sheet.UnlockedSpecializations != null && sheet.UnlockedSpecializations.Contains(parent))
                return GuideStepState.Next;
            return GuideStepState.Future;
        }

        /// <summary>Prochaines étapes déblocables (parents acquis). Ordre du BuildPath préservé.</summary>
        public List<string> GetNextSteps(CharacterSheet sheet)
        {
            var list = new List<string>();
            if (sheet == null || BuildPath == null) return list;
            for (int i = 0; i < BuildPath.Count; i++)
                if (GetSpecState(sheet, BuildPath[i]) == GuideStepState.Next)
                    list.Add(BuildPath[i]);
            return list;
        }

        public int GetAcquiredCount(CharacterSheet sheet)
        {
            if (sheet == null || BuildPath == null) return 0;
            int n = 0;
            for (int i = 0; i < BuildPath.Count; i++)
                if (sheet.UnlockedSpecializations != null && sheet.UnlockedSpecializations.Contains(BuildPath[i])) n++;
            return n;
        }

        public int GetTargetTraining(SkillType skill)
        {
            if (TargetTrainings == null) return 0;
            for (int i = 0; i < TargetTrainings.Count; i++)
                if (TargetTrainings[i] != null && TargetTrainings[i].Skill == skill)
                    return Math.Max(0, TargetTrainings[i].Level);
            return 0;
        }

        /// <summary>Vrai si la compétence a encore des niveaux de build à prendre (highlight).</summary>
        public bool IsSkillNext(CharacterSheet sheet, SkillType skill)
        {
            if (sheet == null) return false;
            int target = GetTargetTraining(skill);
            if (target <= 0) return false;
            return sheet.GetSkill(skill).TrainingLevel < target;
        }
    }

    [Serializable]
    public class SkillTrainingStep
    {
        public SkillType Skill;
        public int Level;

        public SkillTrainingStep() { Skill = SkillType.Athletisme; Level = 0; }
        public SkillTrainingStep(SkillType skill, int level)
        {
            Skill = skill;
            Level = Math.Clamp(level, 0, 3);
        }
    }
}
