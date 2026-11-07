using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.CombatUI;
using Killtime.Tactics.Units;

namespace Killtime.UI
{
    // RD-194 : Duo-Tech MJ sans souris (dev-only, 0 règle modifiée).
    // Sélecteur initiateur (trio) + Duo (12 defs) + grade forcé, paiement
    // atomique + tampon visibles au log, résolution table via l'arène.
    // Tissage souris WeaveController inchangé. Appelée depuis
    // DrawCheatsAndToolsTab (1 ligne).
    public partial class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        private int _duoInitiatorIndex = 0;
        private int _duoDefIndex = 0;
        private int _duoOutcomeIndex = 0;
        private int _duoSillageIndex = 0;

        private void DrawDuoTechSection()
        {
            if (_arena == null) return;

            GUILayout.Space(8);
            GUILayout.Label("<b>🔥 Duo-Tech MJ sans souris (RD-194, Livre V §21.5) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            var initiators = _arena.ListDuoInitiators();
            if (initiators.Count == 0)
            {
                GUILayout.Label("<i>Aucun héros du trio en arène (spawn Thomas/Mina/Lucas requis).</i>");
                GUILayout.EndVertical();
                return;
            }
            _duoInitiatorIndex = Mathf.Clamp(_duoInitiatorIndex, 0, initiators.Count - 1);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < initiators.Count; i++)
            {
                var cand = initiators[i];
                if (cand == null || cand.Stats == null) continue;
                string nm = cand.Stats.Name ?? ("Héros " + i);
                if (nm.Length > 12) nm = nm.Substring(0, 12);
                if (i == _duoInitiatorIndex) GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                if (GUILayout.Button(nm, GUILayout.Height(24))) _duoInitiatorIndex = i;
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            var defs = DuoTechRegistry.GetAllDefs();
            _duoDefIndex = Mathf.Clamp(_duoDefIndex, 0, defs.Count - 1);
            for (int row = 0; row < defs.Count; row += 3)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < 3 && row + c < defs.Count; c++)
                {
                    int idx = row + c;
                    if (idx == _duoDefIndex) GUI.backgroundColor = new Color(1f, 0.6f, 0.2f);
                    if (GUILayout.Button(defs[idx].Name, GUILayout.Height(26))) _duoDefIndex = idx;
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndHorizontal();
            }

            var def = defs[_duoDefIndex];
            GUILayout.Label($"<i>{def.Description}</i>");
            GUILayout.Label($"Coût : <b>{CombatDevArena.DuoTechCostPreview(def)}</b> — ND {def.SyncND} ({def.SyncAttrs}) — {def.Condition}");
            bool isSillage = def.Id == DuoTechId.SillageIgne;

            if (isSillage)
            {
                var grades = new[] { "Perfect", "Risky", "Late", "Miss" };
                GUILayout.BeginHorizontal();
                GUILayout.Label("Grade :", GUILayout.Width(50));
                for (int g = 0; g < grades.Length; g++)
                {
                    if (g == _duoSillageIndex) GUI.backgroundColor = new Color(1f, 0.45f, 0.1f);
                    if (GUILayout.Button(grades[g], GUILayout.Height(24))) _duoSillageIndex = g;
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndHorizontal();
                GUILayout.Label($"<i>{CombatDevArena.SillageGradeLabel(CombatDevArena.SillageGradeFromIndex(_duoSillageIndex))}</i>");
            }
            else
            {
                var outcomes = new[] { "Parfaite", "Semi", "Ratée" };
                GUILayout.BeginHorizontal();
                GUILayout.Label("Issue :", GUILayout.Width(50));
                for (int o = 0; o < outcomes.Length; o++)
                {
                    if (o == _duoOutcomeIndex) GUI.backgroundColor = new Color(1f, 0.75f, 0.2f);
                    if (GUILayout.Button(outcomes[o], GUILayout.Height(24))) _duoOutcomeIndex = o;
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndHorizontal();
            }

            var initiator = initiators[_duoInitiatorIndex];
            if (initiator != null && initiator.Stats != null)
            {
                int round = _arena.CurrentDuoRound();
                string preview;
                if (def.Id == DuoTechId.TriFusion)
                {
                    preview = DuoTechPreviewTrio(initiator, def, round);
                }
                else if (def.Id == DuoTechId.RempartTellurique || def.Id == DuoTechId.ZeroTectonique)
                {
                    preview = DuoTechPreviewPair(initiator, def, round, true);
                }
                else
                {
                    preview = DuoTechPreviewPair(initiator, def, round, false);
                }
                GUILayout.Label(preview);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("⚡ ARMER + RÉSOUDRE (sans souris)", GUILayout.Height(30)))
            {
                if (isSillage)
                    _arena.TryExecuteDebugSillage(initiator, CombatDevArena.SillageGradeFromIndex(_duoSillageIndex), out _);
                else
                    _arena.TryExecuteDebugDuoTech(initiator, def.Id, CombatDevArena.DuoTechOutcomeFromIndex(_duoOutcomeIndex), out _);
            }
            if (GUILayout.Button("🤖 IA AUTO (solver → Sillage)", GUILayout.Height(30)))
            {
                _arena.TryExecuteDebugDuoTechAuto(out _);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("<i>Tissage souris inchangé (menu radial). Placement manuel : téléportez Mina/Lucas avant, barricade Rempart à poser par le MJ.</i>");

            GUILayout.EndVertical();
        }

        private string DuoTechPreviewPair(TacticalUnit initiator, DuoTechDef def, int round, bool thomasPair)
        {
            TacticalUnit partner = null;
            var all = _arena.AllDebugUnits();
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    var u = all[i];
                    if (u == null || u == initiator || u.Stats == null || !u.Stats.IsAlive) continue;
                    if (thomasPair)
                    {
                        if (def.Id == DuoTechId.RempartTellurique && DuoTechRegistry.AreThomasMina(initiator.Stats, u.Stats)) { partner = u; break; }
                        if (def.Id == DuoTechId.ZeroTectonique && DuoTechRegistry.AreThomasLucas(initiator.Stats, u.Stats)) { partner = u; break; }
                    }
                    else if (DuoTechRegistry.AreComplementary(initiator.Stats, u.Stats)) { partner = u; break; }
                }
            }
            if (partner == null) return "<color=#FF6666>⛔ Partenaire introuvable en vie.</color>";
            bool ok = thomasPair
                ? DuoTechRegistry.CanDeclarePair(initiator.Stats, partner.Stats, def, round, out string whyP)
                : DuoTechRegistry.CanDeclare(initiator.Stats, partner.Stats, def, round, out whyP);
            string tampon = round > 0 ? $" (tampon round {round} : {initiator.Stats.Name}={initiator.Stats.LastDuoTechRound}, {partner.Stats.Name}={partner.Stats.LastDuoTechRound})" : " (hors combat : pas de limite)";
            string pa = $"{initiator.Stats.Name} {initiator.Stats.CurrentActionPoints} PA / {partner.Stats.Name} {partner.Stats.CurrentActionPoints} PA";
            return ok ? $"<color=#66FF99>✅ Armable</color> : {pa}{tampon}" : $"<color=#FF6666>⛔ {whyP}</color> — {pa}{tampon}";
        }

        private string DuoTechPreviewTrio(TacticalUnit initiator, DuoTechDef def, int round)
        {
            var all = _arena.AllDebugUnits();
            TacticalUnit mina = null, lucas = null, thomas = null;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    var u = all[i];
                    if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                    if (DuoTechRegistry.IsMinaMember(u.Stats)) mina = u;
                    else if (DuoTechRegistry.IsLucasMember(u.Stats)) lucas = u;
                    else if (DuoTechRegistry.IsThomasMember(u.Stats)) thomas = u;
                }
            }
            if (mina == null || lucas == null || thomas == null)
                return "<color=#FF6666>⛔ Trio incomplet (Thomas + Mina + Lucas en vie exigés).</color>";
            var others = new List<TacticalUnit>(2);
            if (mina != initiator) others.Add(mina);
            if (lucas != initiator) others.Add(lucas);
            if (thomas != initiator) others.Add(thomas);
            if (others.Count != 2) return "<color=#FF6666>⛔ Initiateur hors trio.</color>";
            bool ok = DuoTechRegistry.CanDeclareTrio(initiator.Stats, others[0].Stats, others[1].Stats, def, round, out string why);
            string tampon = round > 0 ? $" (tampon round {round})" : " (hors combat : pas de limite)";
            string pa = $"{initiator.Stats.Name} {initiator.Stats.CurrentActionPoints} / {others[0].Stats.Name} {others[0].Stats.CurrentActionPoints} / {others[1].Stats.Name} {others[1].Stats.CurrentActionPoints} PA";
            return ok ? $"<color=#66FF99>✅ Armable</color> : {pa}{tampon}" : $"<color=#FF6666>⛔ {why}</color> — {pa}{tampon}";
        }
    }
}
