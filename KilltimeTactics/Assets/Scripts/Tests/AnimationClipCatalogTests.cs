#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Killtime.Tactics.Units;

namespace Killtime.Tests
{
    [TestFixture]
    public class AnimationClipCatalogTests
    {
        private static string CatalogJsonPath =>
            Path.Combine(Application.dataPath, "Resources/Data/AnimationClipCatalog.json");

        private static AnimationClipCatalogData LoadFromDisk()
        {
            Assert.IsTrue(File.Exists(CatalogJsonPath), $"Catalogue RD-023 introuvable : {CatalogJsonPath}");
            string json = File.ReadAllText(CatalogJsonPath);
            var data = AnimationClipCatalog.Parse(json);
            Assert.IsNotNull(data, "Parse du catalogue a retourné null.");
            return data;
        }

        [Test]
        public void Catalog_Parses_And_Validates()
        {
            var data = LoadFromDisk();
            var errors = AnimationClipCatalog.Validate(data);
            Assert.IsEmpty(errors, "Erreurs de validation RD-023 : " + string.Join(" | ", errors));
        }

        [Test]
        public void Catalog_Covers_All_Seven_GameDesign_Families()
        {
            var data = LoadFromDisk();
            foreach (var fam in AnimationClipCatalog.RequiredFamilies)
            {
                bool found = false;
                foreach (var e in data.entries)
                    if (e != null && e.category == fam) { found = true; break; }
                Assert.IsTrue(found, $"Famille game design sans entrée : {fam} (RD-023: Idle, marche, course, visée, tir, blessé, mort).");
            }
        }

        [Test]
        public void Catalog_Present_Clips_Match_Animator_States_Used_In_Code()
        {
            var data = LoadFromDisk();
            // États réellement joués via CrossFade/SetTrigger dans TacticalUnitVisual (hors futurs "à créer").
            string[] playedStates =
            {
                "Standing Idle", "Fight Idle", "Rifle Idle", "Weapon Fight Idle",
                "Walking", "Rifle Walk To Stop", "Firing Rifle",
                "Melee Attack", "Roundkick", "Body Block", "Falling Back Death", "Rifle Put Away"
            };
            foreach (var state in playedStates)
            {
                bool covered = false;
                foreach (var e in data.entries)
                {
                    if (e == null || e.animatorState == null) continue;
                    // Égalité stricte (ou préfixe "État (précision...)") : un simple
                    // Contains laisserait "Fight Idle" valider via "Weapon Fight Idle".
                    if (e.animatorState == state || e.animatorState.StartsWith(state + " (")) { covered = true; break; }
                }
                Assert.IsTrue(covered, $"État Animator joué en code mais absent du catalogue : {state}.");
            }
        }

        [Test]
        public void Catalog_Flags_Known_Gaps_For_RD024_RD025()
        {
            var data = LoadFromDisk();
            // ANM-004 : clip présent mais état jamais créé dans le Linker.
            AnimationClipEntry heavy = null;
            // ANM-007 : course manquante (bloquant ressenti).
            AnimationClipEntry run = null;
            foreach (var e in data.entries)
            {
                if (e == null) continue;
                if (e.id == "ANM-004") heavy = e;
                if (e.id == "ANM-007") run = e;
            }
            Assert.IsNotNull(heavy, "ANM-004 (garde arme lourde) manquante du catalogue.");
            Assert.AreEqual("present_non_cable", heavy.status, "ANM-004 doit être flaggée present_non_cable (état Weapon Fight Idle absent du Linker).");
            Assert.IsNotNull(run, "ANM-007 (course) manquante du catalogue.");
            Assert.AreEqual("missing", run.status, "ANM-007 doit être flaggée missing.");
            Assert.AreEqual("high", run.priority, "ANM-007 (course) est priorité haute.");
        }

        [Test]
        public void Catalog_Missing_High_Priority_Is_Actionable()
        {
            var data = LoadFromDisk();
            var missingHigh = new List<AnimationClipEntry>();
            foreach (var e in data.entries)
                if (e != null && e.IsMissing && e.IsHighPriority) missingHigh.Add(e);
            // Chaque manque P0 doit dire quoi produire et où brancher (action RD-024/RD-025).
            foreach (var e in missingHigh)
                Assert.IsFalse(string.IsNullOrEmpty(e.action), $"Manque P0 sans action RD-024/RD-025 : {e.id}.");
        }
    }
}
#endif
