using UnityEngine;
using Killtime.Tactics.CombatUI;

namespace Killtime.UI
{
    /// <summary>
    /// Calibration en jeu de la navigation orbitale (RD-164, temporaire).
    /// Règle OrbitTuning en direct : tester le radial, ajuster, puis figer les
    /// valeurs en constantes et supprimer cette fenêtre. Session uniquement.
    /// </summary>
    public class OrbitTuningWindow : FloatingWindow<OrbitTuningWindow>
    {
        protected override int WindowId => 900;
        protected override string Title => "Radial — Tuning orbite (Maj+F9)";
        protected override Vector2 MinSize => new Vector2(300f, 340f);
        protected override Rect DefaultRect => new Rect(640f, 120f, 320f, 420f);
        protected override KeyCode[] ToggleKeys => _toggleKeys;
        protected override bool RequireShift => true;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F9 };

        protected override void DrawContent()
        {
            GUILayout.Label("<b>Rotation aux bords</b>", new GUIStyle(GUI.skin.label) { richText = true });
            CombatContextMenuUI.OrbitTuning.EdgeInnerFactor = SliderRow("Seuil entrée (×rx)", CombatContextMenuUI.OrbitTuning.EdgeInnerFactor, 0.30f, 0.90f, "F2");
            CombatContextMenuUI.OrbitTuning.ReleaseFactor = SliderRow("Seuil sortie (×rx)", CombatContextMenuUI.OrbitTuning.ReleaseFactor, 0.30f, 0.90f, "F2");
            CombatContextMenuUI.OrbitTuning.EdgeOuterExtra = SliderRow("Marge externe (px)", CombatContextMenuUI.OrbitTuning.EdgeOuterExtra, 20f, 120f, "F0");
            CombatContextMenuUI.OrbitTuning.MaxSpeed = SliderRow("Vitesse max (°/s)", CombatContextMenuUI.OrbitTuning.MaxSpeed, 20f, 200f, "F0");
            GUILayout.Space(4);
            GUILayout.Label("<b>Animation</b>", new GUIStyle(GUI.skin.label) { richText = true });
            CombatContextMenuUI.OrbitTuning.RampMs = SliderRow("Rampe (ms)", CombatContextMenuUI.OrbitTuning.RampMs, 50f, 500f, "F0");
            CombatContextMenuUI.OrbitTuning.SnapMs = SliderRow("Snap ease-out (ms)", CombatContextMenuUI.OrbitTuning.SnapMs, 100f, 600f, "F0");
            GUILayout.Space(4);
            GUILayout.Label("<b>Cibles</b>", new GUIStyle(GUI.skin.label) { richText = true });
            CombatContextMenuUI.OrbitTuning.MinHit = SliderRow("Hitbox mini (px)", CombatContextMenuUI.OrbitTuning.MinHit, 20f, 48f, "F0");
            CombatContextMenuUI.OrbitTuning.DwellPause = GUILayout.Toggle(CombatContextMenuUI.OrbitTuning.DwellPause, " Pause au survol (dwell)");
            GUILayout.Space(6);
            if (GUILayout.Button("↺ Valeurs par défaut", GUILayout.Height(22))) ResetDefaults();
            GUILayout.Label("<i>Ouvrir le radial en jeu, viser un item nommé : stable &lt; 200 ms sous le curseur, 1 cran molette = 1 item.</i>",
                new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 10 });
        }

        private static float SliderRow(string label, float val, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} : <b>{val.ToString(fmt)}</b>", new GUIStyle(GUI.skin.label) { richText = true }, GUILayout.Width(190));
            float nv = GUILayout.HorizontalSlider(val, min, max);
            GUILayout.EndHorizontal();
            return nv;
        }

        private static void ResetDefaults()
        {
            CombatContextMenuUI.OrbitTuning.EdgeInnerFactor = 0.65f;
            CombatContextMenuUI.OrbitTuning.ReleaseFactor = 0.55f;
            CombatContextMenuUI.OrbitTuning.EdgeOuterExtra = 60f;
            CombatContextMenuUI.OrbitTuning.MaxSpeed = 85f;
            CombatContextMenuUI.OrbitTuning.RampMs = 180f;
            CombatContextMenuUI.OrbitTuning.SnapMs = 250f;
            CombatContextMenuUI.OrbitTuning.MinHit = 30f;
            CombatContextMenuUI.OrbitTuning.DwellPause = true;
        }
    }
}
