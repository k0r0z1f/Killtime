#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Combat;
using Killtime.Core.Arcanotech;
using Killtime.Core.Chrono;
using Killtime.Tactics.Grid;

namespace Killtime.Tests
{
    public partial class CoreRulesTests
    {
        [Test]
        public void TestMagicAugmentedBody_ReplacesForceWithMagicOnOffensiveUnarmed()
        {
            // Règle générale « Corps Augmenté » : Mains Nues offensif, MAG > 0 →
            // FOR substituée par MAG (meilleure des deux), entraînements inchangés.
            var attr = new Attributes(@for: 3, agi: 5, con: 3, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 5);
            var sheet = new CharacterSheet { Name = "Mina", BaseAttributes = attr };
            sheet.GetSkill(SkillType.MainsNues).TrainingLevel = 3;
            var stats = sheet.ToCombatStats();

            Assert.IsTrue(stats.IsMagicAugmented(SkillType.MainsNues, true));
            Assert.IsFalse(stats.IsMagicAugmented(SkillType.MainsNues, false));
            Assert.IsFalse(stats.IsMagicAugmented(SkillType.ManiementArmes, true));

            // Offensif (paliers) : MAG 5 → +2 ; +3 entraînements = 5 niveaux → D12.
            Assert.AreEqual(8, stats.GetSkillRank(SkillType.MainsNues, true));
            Assert.AreEqual(DiceType.D12, stats.GetSkillDie(SkillType.MainsNues, true));
            // Défensif : inchangé, valeur 4 → +1 ; +3 = 4 niveaux → D10.
            Assert.AreEqual(7, stats.GetSkillRank(SkillType.MainsNues, false));
            Assert.AreEqual(DiceType.D10, stats.GetSkillDie(SkillType.MainsNues, false));

            // Non-éveillé (MAG 0) : offensif = défensif, aucune augmentation.
            var plainAttr = new Attributes(@for: 5, agi: 5, con: 4, rap: 3, @int: 2, eru: 3, cha: 1, ins: 3, mag: 0);
            var plainSheet = new CharacterSheet { Name = "Brute", BaseAttributes = plainAttr };
            plainSheet.GetSkill(SkillType.MainsNues).TrainingLevel = 1;
            var plainStats = plainSheet.ToCombatStats();

            Assert.IsFalse(plainStats.IsMagicAugmented(SkillType.MainsNues, true));
            Assert.AreEqual(plainStats.GetSkillDie(SkillType.MainsNues, false),
                plainStats.GetSkillDie(SkillType.MainsNues, true));
        }

        [Test]
        public void TestEmergencyBreath_IncreasesActionPointsAndEssoufflement()
        {
            var attr = new Attributes(3, 2, 3, 4, 3, 1);
            var stats = new CharacterStats("Testeur", attr);

            stats.ConsumeActionPoints(7);
            Assert.AreEqual(0, stats.CurrentActionPoints);
            Assert.AreEqual(0, stats.Essoufflement);

            bool success = stats.TakeEmergencyBreath(2);
            Assert.IsTrue(success);
            Assert.AreEqual(2, stats.CurrentActionPoints);
            Assert.AreEqual(1, stats.Essoufflement);
        }

        [Test]
        public void TestNythariteSpell_CreationCostEqualsActionPointCost_MatchesLivreIV()
        {
            var spell = new NythariteSpell("Décharge Cinétique", PsychicDiscipline.Telekinesie, PsychicStage.Stade2_AccesProfond, costXpPa: 4);

            Assert.AreEqual(4, spell.CreationXpCost);
            Assert.AreEqual(4, spell.ActionPointCost);
        }

        [Test]
        public void TestSkillDiceExamples_MainsNuesD10_EsquiveD8_MatchesCodex()
        {
            // Exemple canonique du Codex : Mains Nues +2 entr., FOR 5 (+2) = 4 niveaux -> d10.
            // Esquive 1 entr., réf. 5 (+2) = 3 niveaux -> d8.
            var attr = new Attributes(@for: 5, agi: 5, con: 4, rap: 5, @int: 2, eru: 3, cha: 1, ins: 3);
            var sheet = new CharacterSheet { BaseAttributes = attr };
            sheet.GetSkill(SkillType.MainsNues).TrainingLevel = 2;
            sheet.GetSkill(SkillType.Esquive).TrainingLevel = 1;
            var stats = sheet.ToCombatStats();

            // Mains Nues offensif : (5+5+1)/2 = 5 -> +2 paliers + 2 entr. = 4 -> D10.
            Assert.AreEqual(DiceType.D10, stats.GetSkillDie(SkillType.MainsNues, true));
            // Esquive : (5+5+1)/2 = 5 -> +2 paliers + 1 entr. = 3 -> D8.
            Assert.AreEqual(DiceType.D8, stats.GetSkillDie(SkillType.Esquive, false));
        }

        [Test]
        public void TestMina_Volume2_SpecializationStoryLock()
        {
            var attr = new Attributes(@for: 3, agi: 5, con: 3, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 5);
            var sheet = new CharacterSheet { Name = "Mina", BaseAttributes = attr, AvailableXP = 20 };

            Assert.IsTrue(CharacterProgressionManager.IsVolume2Specialization("Barricade de Racines"));
            Assert.IsFalse(CharacterProgressionManager.IsVolume2Specialization("Teep de Rupture"));

            bool unlockedWithoutFlag = CharacterProgressionManager.UnlockSpecialization(sheet, "Barricade de Racines", out string msgLock);
            Assert.IsFalse(unlockedWithoutFlag);
            StringAssert.Contains("VERROU CAUSAL", msgLock);

            bool unlockedPhase1 = CharacterProgressionManager.UnlockSpecialization(sheet, "Teep de Rupture", out string msgPhase1);
            Assert.IsTrue(unlockedPhase1, msgPhase1);
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Teep de Rupture"));

            // Sous-amélioration : impossible d'acquérir sans la spécialisation parente
            bool subWithoutParent = CharacterProgressionManager.UnlockSpecialization(sheet, "Maniement de l'Épée : Riposte Éclair", out string msgNoParent);
            Assert.IsFalse(subWithoutParent);
            StringAssert.Contains("requiert la spécialisation parente", msgNoParent);

            // Acquisition de la spécialisation parente puis de son amélioration
            sheet.AvailableXP = 20;
            bool parentUnlocked = CharacterProgressionManager.UnlockSpecialization(sheet, "Maniement de l'Épée", out _);
            Assert.IsTrue(parentUnlocked);

            bool subWithParent = CharacterProgressionManager.UnlockSpecialization(sheet, "Maniement de l'Épée : Riposte Éclair", out string msgWithParent);
            Assert.IsTrue(subWithParent, msgWithParent);
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Maniement de l'Épée : Riposte Éclair"));
        }

        [Test]
        public void TestWeaponSpecializationTrees_AxeHastBow()
        {
            var sheet = new CharacterSheet { Name = "Roger", AvailableXP = 40 };

            // Sources de compétence : Hache → Maniement, Hast → Perçantes, Arc → Ballistique.
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Hache de Guerre", out var axeSrc));
            Assert.AreEqual(SkillType.ManiementArmes, axeSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Hache de Guerre : Brise-Garde", out var axeSub));
            Assert.AreEqual(SkillType.ManiementArmes, axeSub);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Arme de Hast : Mur de Piques", out var hastSrc));
            Assert.AreEqual(SkillType.ArmesPercantes, hastSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Tir à l'Arc : Tir en Cloche", out var bowSrc));
            Assert.AreEqual(SkillType.Ballistique, bowSrc);

            // Filiation des améliorations.
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Hache de Guerre : Fente du Bûcheron", out var axeParent));
            Assert.AreEqual("Hache de Guerre", axeParent);
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Arme de Hast : Phalange d'Acier", out var hastParent));
            Assert.AreEqual("Arme de Hast : Mur de Piques", hastParent);
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Tir à l'Arc : Pluie d'Acier", out var bowParent));
            Assert.AreEqual("Tir à l'Arc : Tir en Cloche", bowParent);

            // Prérequis : pas de Brise-Garde sans Fente du Bûcheron.
            Assert.IsFalse(CharacterProgressionManager.UnlockSpecialization(sheet, "Hache de Guerre : Brise-Garde", out string noParent));
            StringAssert.Contains("requiert la spécialisation parente", noParent);

            // Chaîne complète Hache (5 XP par maîtrise, payés en XP libre).
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Hache de Guerre", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Hache de Guerre : Fente du Bûcheron", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Hache de Guerre : Brise-Garde", out string ok));
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Hache de Guerre : Brise-Garde"), ok);

            // Parade : la Hache compte comme spécialisation défensive du Maniement d'Arme.
            Assert.IsTrue(SkillDefinitions.HasDefensiveSpecialization(sheet, SkillType.ManiementArmes));
        }

        [Test]
        public void TestAthleticsBranch_CourseAndCrossing()
        {
            var sheet = new CharacterSheet { Name = "Roger", AvailableXP = 40 };

            // Sources : tout l'arbre relève de l'Athlétisme.
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Course d'Endurance", out var courseSrc));
            Assert.AreEqual(SkillType.Athletisme, courseSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Course d'Endurance : Cœur de Marathon", out var heartSrc));
            Assert.AreEqual(SkillType.Athletisme, heartSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Franchissement : Nage de Combat", out var swimSrc));
            Assert.AreEqual(SkillType.Athletisme, swimSrc);

            // Filiation et secret du capstone.
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Course d'Endurance : Second Souffle", out var breathParent));
            Assert.AreEqual("Course d'Endurance", breathParent);
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Franchissement : Escalade Assurée", out var climbParent));
            Assert.AreEqual("Franchissement", climbParent);
            Assert.IsTrue(CharacterProgressionManager.IsHiddenSpecialization("Course d'Endurance : Cœur de Marathon"));
            Assert.IsFalse(CharacterProgressionManager.IsHiddenSpecialization("Franchissement"));

            // Prérequis : pas de Second Souffle sans Course d'Endurance.
            Assert.IsFalse(CharacterProgressionManager.UnlockSpecialization(sheet, "Course d'Endurance : Second Souffle", out string noParent));
            StringAssert.Contains("requiert la spécialisation parente", noParent);

            // Chaîne complète (5 XP par maîtrise, payés en XP libre).
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Course d'Endurance", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Course d'Endurance : Second Souffle", out string ok));
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Course d'Endurance : Second Souffle"), ok);
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Franchissement", out _));
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Franchissement"));
        }

        [Test]
        public void TestEnduranceAndIntuitionBranches_RegistryAndUnlocks()
        {
            var sheet = new CharacterSheet { Name = "Roger", AvailableXP = 40 };

            // Sources : Endurance Physique vs Intuition.
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Condition de Fer : Mur de Chair", out var wallSrc));
            Assert.AreEqual(SkillType.EndurancePhysique, wallSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Trempe de Fer : Esprit de Granit", out var gritSrc));
            Assert.AreEqual(SkillType.EndurancePhysique, gritSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Sens du Danger : Premiers Réflexes", out var reflexSrc));
            Assert.AreEqual(SkillType.Intuition, reflexSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Instinct du Chasseur : Piégeur", out var trapSrc));
            Assert.AreEqual(SkillType.Intuition, trapSrc);

            // Filiation et secrets des capstones.
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Condition de Fer : Dur à Cuire", out var toughParent));
            Assert.AreEqual("Condition de Fer", toughParent);
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Trempe de Fer : Ignorer la Douleur", out var painParent));
            Assert.AreEqual("Trempe de Fer", painParent);
            Assert.IsTrue(CharacterProgressionManager.IsHiddenSpecialization("Condition de Fer : Mur de Chair"));
            Assert.IsTrue(CharacterProgressionManager.IsHiddenSpecialization("Sens du Danger : Clairvoyance du Vétéran"));
            Assert.IsFalse(CharacterProgressionManager.IsHiddenSpecialization("Trempe de Fer"));

            // Prérequis : pas de Mur de Chair sans Dur à Cuire.
            Assert.IsFalse(CharacterProgressionManager.UnlockSpecialization(sheet, "Condition de Fer : Mur de Chair", out string noParent));
            StringAssert.Contains("requiert la spécialisation parente", noParent);

            // Chaînes complètes (5 XP par maîtrise, payés en XP libre).
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Condition de Fer", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Condition de Fer : Dur à Cuire", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Condition de Fer : Mur de Chair", out string wallOk));
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Condition de Fer : Mur de Chair"), wallOk);
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Sens du Danger", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Sens du Danger : Premiers Réflexes", out string reflexOk));
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Sens du Danger : Premiers Réflexes"), reflexOk);

            // Effets mécaniques : Sens du Danger intègre l'Instinct à l'initiative.
            // RAP 5, AGI 4, INT 2, INS 8 → sans la spé : base 5 (D6) ; avec : base 8 (D8, D10 avec Premiers Réflexes).
            var keenAttr = new Attributes(@for: 4, agi: 4, con: 4, rap: 5, @int: 2, eru: 2, cha: 2, ins: 8);
            var plain = new CharacterSheet { Name = "Plaine", BaseAttributes = keenAttr };
            Assert.AreEqual(5, plain.ToCombatStats().GetInitiativeBaseValue());
            Assert.AreEqual(DiceType.D6, plain.ToCombatStats().GetInitiativeDie());

            var scout = new CharacterSheet { Name = "Éclaireur", BaseAttributes = keenAttr, AvailableXP = 40 };
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(scout, "Sens du Danger", out _));
            Assert.AreEqual(8, scout.ToCombatStats().GetInitiativeBaseValue());
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(scout, "Sens du Danger : Premiers Réflexes", out string reflexOk2));
            Assert.IsTrue(scout.UnlockedSpecializations.Contains("Sens du Danger : Premiers Réflexes"), reflexOk2);
            Assert.AreEqual(DiceType.D10, scout.ToCombatStats().GetInitiativeDie());
        }

        [Test]
        public void TestExhaustiveSpecializationTree_BranchingAndCheatUnlocks()
        {
            var sheet = new CharacterSheet { AvailableXP = 0 };

            // Vérification de la complétude du catalogue
            var allSpecs = new System.Collections.Generic.List<CharacterProgressionManager.SpecializationDetail>(CharacterProgressionManager.GetAllSpecializations());
            Assert.GreaterOrEqual(allSpecs.Count, 65, "L'arborescence doit comporter au moins 65 maîtrises et spécialisations réparties sur les 7 Piliers.");

            // Échec du déblocage normal d'un secret sans XP et sans prérequis
            bool normalUnlock = CharacterProgressionManager.UnlockSpecialization(sheet, "Arts Martiaux : Paume de Brum'korath", out string failMsg);
            Assert.IsFalse(normalUnlock);
            StringAssert.Contains("Prérequis", failMsg);

            // Déblocage immédiat via le mode Dev Cheat
            bool cheatUnlock = CharacterProgressionManager.UnlockSpecialization(sheet, "Arts Martiaux : Paume de Brum'korath", out string cheatMsg, forceFree: true);
            Assert.IsTrue(cheatUnlock);
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Arts Martiaux : Paume de Brum'korath"));
            StringAssert.Contains("[CHEAT]", cheatMsg);

            // Vérification de la détection des secrets cachés
            Assert.IsTrue(CharacterProgressionManager.IsHiddenSpecialization("Maniement de l'Épée : Lame de Ligne Temporelle Zéro"));
            Assert.IsFalse(CharacterProgressionManager.IsHiddenSpecialization("Arts Martiaux : Enchaînement Fluide"));
        }

        [Test]
        public void TestMina_ExclusivePrimordialMagic_And_BlockedOtherMagic()
        {
            var minaSheet = new CharacterSheet
            {
                Name = "Mina",
                ModelPrefabName = "Mina",
                AvailableXP = 40
            };

            var defaultSheet = new CharacterSheet
            {
                Name = "Roger",
                AvailableXP = 40
            };

            Assert.IsTrue(CharacterProgressionManager.IsMina(minaSheet));
            Assert.IsFalse(CharacterProgressionManager.IsMina(defaultSheet));

            // Mina n'a pas accès à la Magie Élémentale ni à la Magie de l'Esprit
            Assert.IsTrue(CharacterProgressionManager.IsSkillAccessible(minaSheet, SkillType.MagiePrimale));
            Assert.IsFalse(CharacterProgressionManager.IsSkillAccessible(minaSheet, SkillType.MagieElementale));
            Assert.IsFalse(CharacterProgressionManager.IsSkillAccessible(minaSheet, SkillType.MagieEsprit));

            bool trainElem = CharacterProgressionManager.TrainSkill(minaSheet, SkillType.MagieElementale, out string msgElem);
            Assert.IsFalse(trainElem);
            StringAssert.Contains("RESTRICTION D'ÂME", msgElem);

            bool trainEsprit = CharacterProgressionManager.TrainSkill(minaSheet, SkillType.MagieEsprit, out string msgEsprit);
            Assert.IsFalse(trainEsprit);
            StringAssert.Contains("RESTRICTION D'ÂME", msgEsprit);

            bool unlockElemSpec = CharacterProgressionManager.UnlockSpecialization(minaSheet, "Incinération Pyrocinétique", out string msgSpecElem);
            Assert.IsFalse(unlockElemSpec);
            StringAssert.Contains("RESTRICTION D'ÂME", msgSpecElem);

            // Le personnage par défaut conserve l'accès complet
            Assert.IsTrue(CharacterProgressionManager.IsSkillAccessible(defaultSheet, SkillType.MagieElementale));
            Assert.IsTrue(CharacterProgressionManager.IsSkillAccessible(defaultSheet, SkillType.MagieEsprit));

            // Les maîtrises exclusives de Mina sont bloquées pour le personnage par défaut
            bool defaultMinaSpec = CharacterProgressionManager.UnlockSpecialization(defaultSheet, "Éveil Chlorophyllien", out string msgDefMina);
            Assert.IsFalse(defaultMinaSpec);
            StringAssert.Contains("AFFINITÉ BIOLOGIQUE EXCLUSIVE", msgDefMina);

            // Mina peut débloquer ses maîtrises primordiales étendues (Volume II)
            bool minaPrimale = CharacterProgressionManager.UnlockSpecialization(minaSheet, "Barricade de Racines", out _, forceFree: true);
            Assert.IsTrue(minaPrimale);
            Assert.IsTrue(minaSheet.UnlockedSpecializations.Contains("Barricade de Racines"));
        }
    }
}
#endif