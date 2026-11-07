using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;
using Killtime.Core.Character;

namespace Killtime.DevTools.Character
{
    /// <summary>
    /// État + construction du sort candidat de la forge (Livre IV §19).
    /// Extrait à l'identique de CharacterDevWindow : presets, fiche technique,
    /// catalogue modulaire, bilan XP=PA. La window ne garde que le binding IMGUI.
    /// </summary>
    [Serializable]
    public sealed class SpellForgeSession
    {
        public string CustomSpellName = "Onde de Choc Causal";
        public string CustomSpellDesc = "Impulsion de 5e Force comprimée.";
        public PsychicDiscipline CustomSpellDiscipline = PsychicDiscipline.Telekinesie;
        public PsychicStage CustomSpellStage = PsychicStage.Stade2_AccesProfond;
        public SkillType CustomSpellSkill = SkillType.MagieEsprit;

        public readonly Dictionary<ArcanotechModuleId, int> ModuleRanks = new();

        public void Clear(string defaultName = "Nouveau Pouvoir", string defaultDesc = "")
        {
            ModuleRanks.Clear();
            CustomSpellName = defaultName;
            CustomSpellDesc = defaultDesc;
        }

        public void ResetForNewSheet()
        {
            ModuleRanks.Clear();
            CustomSpellName = "Onde de Choc Causal";
            CustomSpellDesc = "Impulsion de 5e Force comprimée.";
        }

        public void LoadSpellIntoForge(NythariteSpell spell)
        {
            if (spell == null) return;
            CustomSpellName = spell.Name;
            CustomSpellDesc = spell.Description;
            CustomSpellDiscipline = spell.Discipline;
            CustomSpellStage = spell.Stage;
            CustomSpellSkill = spell.AssociatedSkill;

            ModuleRanks.Clear();
            if (spell.Modules != null)
            {
                for (int i = 0; i < spell.Modules.Count; i++)
                {
                    var m = spell.Modules[i];
                    if (m != null) ModuleRanks[m.ModuleId] = m.Rank;
                }
            }
        }

        public NythariteSpell BuildCandidateSpell()
        {
            var selections = new List<PowerModuleSelection>();
            foreach (var kvp in ModuleRanks)
            {
                if (kvp.Value > 0)
                {
                    selections.Add(new PowerModuleSelection(kvp.Key, kvp.Value));
                }
            }

            var spell = new NythariteSpell(
                name: string.IsNullOrWhiteSpace(CustomSpellName) ? "Sort sans nom" : CustomSpellName,
                description: CustomSpellDesc,
                skill: CustomSpellSkill,
                associatedAttribute: "Magie",
                discipline: CustomSpellDiscipline,
                stage: CustomSpellStage,
                modules: selections
            );

            return spell;
        }

        public int GetRank(ArcanotechModuleId id)
        {
            return ModuleRanks.TryGetValue(id, out int rank) ? rank : 0;
        }

        public void AddRank(ArcanotechModuleId id, int delta, int maxRank = 99)
        {
            int current = GetRank(id);
            int next = Math.Max(0, Math.Min(maxRank, current + delta));
            if (next <= 0) ModuleRanks.Remove(id);
            else ModuleRanks[id] = next;
        }

        /// <summary>
        /// Tente de forger+apprendre le sort candidat sur la fiche.
        /// Retourne le message à afficher dans _statusMessage.
        /// </summary>
        public bool TryForgeAndLearn(CharacterSheet sheet, out string message)
        {
            var candidate = BuildCandidateSpell();
            if (sheet == null)
            {
                message = "Aucune fiche active.";
                return false;
            }
            if (ArcanotechWorkshop.LearnSpell(sheet, candidate, out string msg))
            {
                SheetSessionService.SaveAndSyncUnits(sheet);
                message = msg;
                return true;
            }
            message = msg;
            return false;
        }
    }
}
