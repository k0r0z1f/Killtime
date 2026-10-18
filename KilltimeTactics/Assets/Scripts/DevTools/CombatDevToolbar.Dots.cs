using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Units;
using Killtime.Core.Character;

// Partial de CombatDevToolbar (façade : CombatDevToolbar.cs).
// RD-191 : DoT calibrés / soins / résurrection (dev-only, 0 règle modifiée).
// Chemins existants : ApplyResidualDamage, Heal, ApplyMajorCare, ApplyResurrection,
// TickTurnStatus. Aucune règle modifiée.
namespace Killtime.UI
{
    public partial class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        // RD-191 : état local de la section DoT/soins (sélecteur indépendant).
        private int _dotUnitIndex = 0;
        private int _dotKindIndex = 0;
        private int _dotPool = 3;
        private bool _dotAggravating = false;
        private int _dotHealAmount = 5;

        private static readonly StatusEffect[] DotKinds =
        {
            StatusEffect.Saignement,
            StatusEffect.Empoisonne,
            StatusEffect.EnFeu,
            StatusEffect.Asphyxie
        };

        private void DrawDotCareSection()
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>☠️ DoT Calibrés / Soins / Résurrection (RD-191, dev-only) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            var units = _arena != null ? _arena.AllDebugUnits() : null;
            if (units == null || units.Count == 0)
            {
                GUILayout.Label("<i>Aucune unité en arène.</i>");
                GUILayout.EndVertical();
                return;
            }
            _dotUnitIndex = Mathf.Clamp(_dotUnitIndex, 0, units.Count - 1);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < units.Count; i++)
            {
                var cand = units[i];
                if (cand == null || cand.Stats == null) continue;
                string nm = cand.Stats.Name ?? ("Unité " + i);
                if (nm.Length > 12) nm = nm.Substring(0, 12);
                if (i == _dotUnitIndex) GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                if (GUILayout.Button(nm, GUILayout.Height(24))) _dotUnitIndex = i;
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            var unit = units[_dotUnitIndex];
            if (unit == null || unit.Stats == null)
            {
                GUILayout.EndVertical();
                return;
            }
            var st = unit.Stats;

            // Type de DoT.
            GUILayout.BeginHorizontal();
            GUILayout.Label("DoT :", GUILayout.Width(40));
            for (int d = 0; d < DotKinds.Length; d++)
            {
                if (d == _dotKindIndex) GUI.backgroundColor = new Color(0.9f, 0.4f, 0.2f);
                if (GUILayout.Button(DotKinds[d].ToString(), GUILayout.Height(24))) _dotKindIndex = d;
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            // Pool résiduel 1-10 + aggravation.
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Pool : {_dotPool}", GUILayout.Width(60));
            _dotPool = Mathf.RoundToInt(GUILayout.HorizontalSlider(_dotPool, 1f, 10f));
            _dotAggravating = GUILayout.Toggle(_dotAggravating, "Aggravé (+1/tour)", GUILayout.Width(140));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("☠️ Poser DoT")) _arena?.ApplyDebugDot(unit, DotKinds[_dotKindIndex], _dotPool, _dotAggravating);
            if (GUILayout.Button("⏩ Forcer tick")) _arena?.DebugForceDotTickUnit(unit);
            GUILayout.EndHorizontal();

            // Readouts pools résiduels + statuts.
            GUILayout.BeginHorizontal();
            GUILayout.Label($"🩸 Saignement:{st.GetResidualDamage(StatusEffect.Saignement)} ☣️ Empoisonné:{st.GetResidualDamage(StatusEffect.Empoisonne)} 🔥 EnFeu:{st.GetResidualDamage(StatusEffect.EnFeu)} 😮‍💨 Asphyxie:{st.GetResidualDamage(StatusEffect.Asphyxie)}");
            GUILayout.EndHorizontal();
            GUILayout.Label($"❤️ PV : {st.CurrentHealth}/{st.MaxHealth} — <b>Souffrant</b> : {(st.IsSouffrant ? "<color=#FF4444>ACTIF</color>" : "off")}");

            // Soins : montant + simple vs majeurs (seuil CON×2).
            int threshold = st.GetMajorCareThreshold();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Soin : {_dotHealAmount}", GUILayout.Width(60));
            _dotHealAmount = Mathf.RoundToInt(GUILayout.HorizontalSlider(_dotHealAmount, 1f, 20f));
            GUILayout.EndHorizontal();
            GUILayout.Label($"<i>Seuil Soins Majeurs (CON×2) = <b>{threshold}</b> — en dessous : Souffrant PERSISTE.</i>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("💉 Soin simple")) _arena?.DebugHealUnit(unit, _dotHealAmount);
            if (GUILayout.Button("🩹 Soins majeurs")) _arena?.DebugMajorCareUnit(unit, _dotHealAmount);
            GUILayout.EndHorizontal();

            // Résurrection + séquelle.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🕊️ Réanimer (1 PV)")) _arena?.DebugResurrectUnit(unit, 1);
            bool seq = st.HasResurrectionSequelae();
            int rest = st.GetResurrectionSequelaeRemainingDays();
            GUILayout.Label(seq ? $"⚖️ Séquelle : <b>-1 ec</b> ({rest}j restants)" : "⚖️ Séquelle : aucune");
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }
    }
}
