using UnityEngine;
using Killtime.Core.Combat;
using Killtime.Tactics.Units;
using Killtime.Tactics.Visibility;

// Partial de CombatDevToolbar (façade : CombatDevToolbar.cs).
// RD-190 : toggles terrain nuit / lampe / bruit (dev-only, 0 règle modifiée).
// Le bloc Éclairage (fullbright + pixies) est déplacé move-only depuis
// CombatDevToolbar.Cheats.cs pour garder ce fichier sous les 800 lignes.
namespace Killtime.UI
{
    public partial class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        // RD-190 : index de l'unité ciblée par les toggles nuit/lampe/bruit.
        private int _nightUnitIndex = 0;

        private void DrawNightSection()
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>💡 Éclairage & Atmosphère :</b>");

            string fullbrightLabel = _devFullbrightActive ? "💡 Lumière Globale : ACTIVÉE (Touche L)" : "💡 Lumière Globale : ÉTEINTE (Touche L)";
            GUI.backgroundColor = _devFullbrightActive ? new Color(1.0f, 0.85f, 0.35f) : Color.white;
            if (GUILayout.Button(fullbrightLabel, GUILayout.Height(32)))
            {
                ToggleDevFullbrightLight();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("✨ Invoquer Pixie de Test", GUILayout.Height(30)))
            {
                Killtime.Tactics.Lighting.TacticalPixieLight.SpawnPixie();
            }
            if (GUILayout.Button("🧹 Retirer Pixies", GUILayout.Height(30)))
            {
                var pixies = FindObjectsByType<Killtime.Tactics.Lighting.TacticalPixieLight>();
                for (int i = 0; i < pixies.Length; i++)
                {
                    Destroy(pixies[i].gameObject);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>🌙 Nuit / Lampe / Bruit (RD-190, dev-only) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            var fog = FogOfWarManager.Instance ?? FindAnyObjectByType<FogOfWarManager>();
            if (fog == null)
            {
                GUILayout.Label("<i>Pas de FogOfWarManager en scène (fog optionnel).</i>");
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            string nightLabel = fog.IsNight ? "🌙 Nuit : ACTIVÉE (portée bridée)" : "☀️ Nuit : ÉTEINTE (portée jour)";
            GUI.backgroundColor = fog.IsNight ? new Color(0.25f, 0.35f, 0.9f) : Color.white;
            if (GUILayout.Button(nightLabel, GUILayout.Height(30)))
            {
                fog.IsNight = !fog.IsNight;
                _arena?.Log(fog.IsNight
                    ? "🌙 <b>Nuit dev</b> : ACTIVÉE (vue bridée sans lampe/thermique, malus balistique -1)."
                    : "☀️ <b>Nuit dev</b> : éteinte (portée jour).");
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Portée nuit : {fog.NightSightRange} cases", GUILayout.Width(150));
            int wantRange = Mathf.RoundToInt(GUILayout.HorizontalSlider(fog.NightSightRange, 1f, 8f));
            if (wantRange != fog.NightSightRange) fog.NightSightRange = wantRange;
            GUILayout.EndHorizontal();
            GUILayout.Label($"<i>Portée jour : {fog.CurrentSightRange} cases (preset). Nuit sans lampe/thermique : min(jour, {fog.NightSightRange}). Malus balistique nuit sans lampe/thermique : -1 (DuelSetupService + estimation IA).</i>");

            GUILayout.Space(4);
            var units = _arena != null ? _arena.AllDebugUnits() : null;
            if (units == null || units.Count == 0)
            {
                GUILayout.Label("<i>Aucune unité en arène.</i>");
                GUILayout.EndVertical();
                return;
            }
            _nightUnitIndex = Mathf.Clamp(_nightUnitIndex, 0, units.Count - 1);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < units.Count; i++)
            {
                var cand = units[i];
                if (cand == null || cand.Stats == null) continue;
                string nm = cand.Stats.Name ?? ("Unité " + i);
                if (nm.Length > 12) nm = nm.Substring(0, 12);
                if (i == _nightUnitIndex) GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                if (GUILayout.Button(nm, GUILayout.Height(24))) _nightUnitIndex = i;
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            var unit = units[_nightUnitIndex];
            if (unit == null || unit.Stats == null)
            {
                GUILayout.EndVertical();
                return;
            }

            bool lampOn = unit.HasFlashlight;
            bool thermal = unit.HasThermalVision;
            bool duelLit = DuelSetupService.HasLampOrThermal(unit.Stats);
            string nightMod = (fog.IsNight && !duelLit) ? "-1" : "0";
            GUILayout.Label($"🔦 Lampe (fog) : <b>{(lampOn ? "ALLUMÉE" : "éteinte/absente")}</b> · Thermique : <b>{(thermal ? "oui" : "non")}</b> · Lampe/therm. (duel, inventaire/spé) : <b>{(duelLit ? "oui" : "non")}</b> → malus nuit prévu <b>{nightMod}</b>");
            GUILayout.Label("<i>La dotation mono-comportement (lampe allumée) brise le stealth en ligne de mire ; le malus duel -1 se lit dans l'inventaire et les spés (écart noté pour RD-185 si constaté en jeu).</i>");

            GUILayout.BeginHorizontal();
            bool wantLamp = GUILayout.Toggle(lampOn, "🔦 Lampe allumée");
            if (wantLamp != lampOn)
            {
                unit.FlashlightActive = wantLamp;
                if (wantLamp) unit.HasFlashlight = true;
                _arena?.Log(wantLamp
                    ? $"🔦 <b>Lampe dev</b> : {unit.Stats.Name} allumée (brise stealth en ligne de mire de nuit)."
                    : $"🔦 <b>Lampe dev</b> : {unit.Stats.Name} éteinte.");
            }
            if (GUILayout.Button("➕ Doter lampe", GUILayout.Width(110)))
            {
                unit.HasFlashlight = true;
                _arena?.Log($"🔦 <b>Lampe dev</b> : {unit.Stats.Name} dotée + allumée.");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string stealthLabel = unit.IsStealthed ? "🥷 Furtif : OUI — cliquer pour révéler" : "🥷 Furtif : non — cliquer pour masquer";
            if (GUILayout.Button(stealthLabel))
            {
                unit.SetStealth(!unit.IsStealthed);
                _arena?.Log(unit.IsStealthed
                    ? $"🥷 <b>Stealth dev</b> : {unit.Stats.Name} masqué (lampe adverse en ligne de mire = révélé)."
                    : $"🥷 <b>Stealth dev</b> : {unit.Stats.Name} révélé.");
            }
            if (GUILayout.Button("🔊 Simuler tir bruyant", GUILayout.Width(170)))
            {
                fog.NotifyLoudShot(unit);
                string uname = unit.gameObject != null ? unit.gameObject.name : "?";
                bool heard = fog.IsNoiseActive(uname);
                var last = fog.GetLastKnownPosition(uname);
                string lastStr = last.HasValue ? $"({last.Value.Q},{last.Value.R})" : "—";
                _arena?.Log($"🔊 <b>Bruit dev</b> : {unit.Stats.Name} en ({unit.CurrentCoords.Q},{unit.CurrentCoords.R}) — patrouille alertée (convergence IA vers la source, révélation {(heard ? "active" : "inaudible (mur Full / hors Ouïe)")}, dernière position {lastStr}).");
            }
            GUILayout.EndHorizontal();
            string uname2 = unit.gameObject != null ? unit.gameObject.name : "?";
            GUILayout.Label($"<i>Bruit actif : {(fog.IsNoiseActive(uname2) ? "oui (révélé jusqu'à fin round suivant, sauf mur Full)" : "non")} · Furtif : {(unit.IsStealthed ? "oui" : "non")}.</i>");

            GUILayout.EndVertical();
        }
    }
}
