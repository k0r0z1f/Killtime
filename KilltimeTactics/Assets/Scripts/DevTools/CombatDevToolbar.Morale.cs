using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Units;
using Killtime.Core.Character;

// Partial de CombatDevToolbar (façade : CombatDevToolbar.cs).
// RD-192 : Moral / reddition / déroute à la demande (dev-only, 0 règle modifiée).
// Chemins existants : CheckMoraleAtThreshold (seuil 28 %, Max CHA/INS vs SD),
// IsSurrendered (exclusion ciblage), Agonisant (fuite panique ExecutePanickedFlee).
// Le rig 🎲 crit/raté RD-187 pilote l'issue du jet forcé. Aucune règle modifiée.
namespace Killtime.UI
{
    public partial class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        // RD-192 : état local de la section moral (sélecteur indépendant).
        private int _moraleUnitIndex = 0;

        private void DrawMoraleSection()
        {
            if (_arena == null) return;

            GUILayout.Space(8);
            GUILayout.Label("<b>😱 Moral / Reddition / Déroute (RD-192, dev-only) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            var units = _arena.AllDebugUnits();
            if (units == null || units.Count == 0)
            {
                GUILayout.Label("<i>Aucune unité en arène.</i>");
                GUILayout.EndVertical();
                return;
            }
            _moraleUnitIndex = Mathf.Clamp(_moraleUnitIndex, 0, units.Count - 1);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < units.Count; i++)
            {
                var cand = units[i];
                if (cand == null || cand.Stats == null) continue;
                string nm = cand.Stats.Name ?? ("Unité " + i);
                if (nm.Length > 12) nm = nm.Substring(0, 12);
                if (i == _moraleUnitIndex) GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                if (GUILayout.Button(nm, GUILayout.Height(24))) _moraleUnitIndex = i;
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            var unit = units[_moraleUnitIndex];
            if (unit == null || unit.Stats == null)
            {
                GUILayout.EndVertical();
                return;
            }
            var st = unit.Stats;

            int threshold = Mathf.CeilToInt(st.MaxHealth * 0.28f);
            string tested = st.HasTestedMorale ? "<color=#FFAA00>testé</color>" : "<color=#00FFAA>réarmé</color>";
            string surv = st.IsSurvivor ? "<color=#00FFAA>OUI</color>" : "non";
            string surr = st.IsSurrendered ? "<color=#FF4444>RENDU</color>" : "non";
            string ago = st.ActiveStatus.HasFlag(StatusEffect.Agonisant) ? "<color=#FF4444>ACTIF</color>" : "off";
            string side = unit.IsPlayerControlled ? "PJ" : "PNJ";
            GUILayout.Label($"❤️ PV {st.CurrentHealth}/{st.MaxHealth} (seuil 28 % = {threshold}) — {side} — moral : {tested} — Survivor : {surv} — rendu : {surr} — Agonisant : {ago}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🧠 Forcer jet moral")) _arena.DebugForceMoraleCheckUnit(unit);
            if (GUILayout.Button("🏳️ Forcer reddition")) _arena.DebugForceSurrenderUnit(unit);
            if (GUILayout.Button("😱 Forcer déroute")) _arena.DebugForceRoutUnit(unit);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string survBtn = st.IsSurvivor ? "🎭 Survivor : OUI → NON" : "🎭 Survivor : non → OUI";
            if (GUILayout.Button(survBtn)) _arena.DebugSetSurvivorUnit(unit, !st.IsSurvivor);
            if (GUILayout.Button("🔄 Réarmer moral")) _arena.DebugRearmMoraleUnit(unit);
            GUILayout.EndHorizontal();

            GUILayout.Label("<i>Jet réel Max(CHA,INS) vs SD — toggles 🎲 crit/raté (RD-187) = réussite/échec forcé. Reddition = Survivor + PNJ, sinon déroute. Réarmer au-dessus de 28 % (slider PV / soin) pour re-tester.</i>");

            GUILayout.EndVertical();
        }
    }
}
