using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

// Partial move-only de CombatDevToolbar (facade : CombatDevToolbar.cs) — RD-189.
// Paramétrage terrain des presets objectifs, dev-only, aucune règle modifiée.
namespace Killtime.UI
{
    public partial class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        // RD-189 : paramétrage terrain des presets objectifs (dev-only, aucune règle).
        private int _objSurviveRounds = 3;
        private int _objExtractRadius = 1;
        private int _objExtractQ = 3;
        private int _objExtractR = 0;
        private bool _objRequireAll = true;
        private int _objVipIndex = 0;
        private int _objEscortDestRadius = 0;
        private bool _objEscortNoDest = false;
        private string _objTerminalId = "Console_Terminal";
        private int _objHackCount = 1;
        private int _objTerminalIndex = 0;
        private readonly List<TacticalInteractable> _objTerminalCache = new();
        private bool _objTerminalCacheDirty = true;

        // RD-189 : presets objectifs paramétrables (dev-only, sans règle).
        // Survie N rounds, extraction au point ciblé ou centrée custom (Q/R + rayon),
        // escorte VIP au choix (destination = cible ou aucune), hack terminal au choix
        // (référence scène ou ID + nombre requis), toggle requireAllLiving.
        private void DrawObjectiveParamsSection()
        {
            if (_turnManager == null) return;

            GUILayout.Space(4);
            GUILayout.Label("<b>⚙️ Paramétrage terrain (RD-189, dev-only) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            string targetLabel = _arena != null && _arena.CurrentTarget != null
                ? $"({_arena.CurrentTarget.CurrentCoords.Q},{_arena.CurrentTarget.CurrentCoords.R})"
                : "(aucune cible)";
            GUILayout.Label($"<i>Point ciblé (au clic) : {targetLabel} — cible active ou joueur.</i>");

            GUILayout.BeginHorizontal();
            GUILayout.Label($"⏳ Survie : {_objSurviveRounds} round(s)", GUILayout.Width(150));
            int wantRounds = Mathf.RoundToInt(GUILayout.HorizontalSlider(_objSurviveRounds, 1f, 10f));
            if (wantRounds != _objSurviveRounds) _objSurviveRounds = wantRounds;
            if (GUILayout.Button("▶ Survie custom", GUILayout.Width(130)))
            {
                _turnManager.SetupSurviveCustom(_objSurviveRounds);
                _turnManager.CheckCombatOver();
                _arena?.Log($"⏳ <b>Objectif</b> : survie {_objSurviveRounds} round(s) posée.");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            _objRequireAll = GUILayout.Toggle(_objRequireAll, "🚀 Extraction : exiger toute l'escouade vivante (requireAllLiving)");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Rayon zone : {_objExtractRadius}", GUILayout.Width(110));
            int wantRadius = Mathf.RoundToInt(GUILayout.HorizontalSlider(_objExtractRadius, 0f, 3f));
            if (wantRadius != _objExtractRadius) _objExtractRadius = wantRadius;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Centre custom Q : {_objExtractQ}", GUILayout.Width(130));
            int wantQ = Mathf.RoundToInt(GUILayout.HorizontalSlider(_objExtractQ, -6f, 6f));
            if (wantQ != _objExtractQ) _objExtractQ = wantQ;
            GUILayout.Label($"R : {_objExtractR}", GUILayout.Width(50));
            int wantR = Mathf.RoundToInt(GUILayout.HorizontalSlider(_objExtractR, -6f, 6f));
            if (wantR != _objExtractR) _objExtractR = wantR;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"🚀 Extraction ici {targetLabel} R={_objExtractRadius}"))
            {
                var center = _arena != null && _arena.CurrentTarget != null
                    ? _arena.CurrentTarget.CurrentCoords
                    : (_arena != null && _arena.PlayerUnit != null ? _arena.PlayerUnit.CurrentCoords : new HexCoordinates(3, 0));
                _turnManager.SetupExtractionCustom(center, _objExtractRadius, _objRequireAll);
                _turnManager.CheckCombatOver();
                _arena?.Log($"🚀 <b>Objectif</b> : extraction en ({center.Q},{center.R}) R={_objExtractRadius} (tout le monde : {_objRequireAll}).");
            }
            if (GUILayout.Button($"📍 Extraction ({_objExtractQ},{_objExtractR})"))
            {
                var center = new HexCoordinates(_objExtractQ, _objExtractR);
                _turnManager.SetupExtractionCustom(center, _objExtractRadius, _objRequireAll);
                _turnManager.CheckCombatOver();
                _arena?.Log($"🚀 <b>Objectif</b> : extraction custom en ({center.Q},{center.R}) R={_objExtractRadius} (tout le monde : {_objRequireAll}).");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            var units = _arena != null ? _arena.AllDebugUnits() : null;
            if (units != null && units.Count > 0)
            {
                _objVipIndex = Mathf.Clamp(_objVipIndex, 0, units.Count - 1);
                var vip = units[_objVipIndex];
                string vipName = (vip != null && vip.Stats != null) ? vip.Stats.Name : "—";
                GUILayout.Label($"🛡️ VIP : <b>{vipName}</b> (index {_objVipIndex})");
                GUILayout.BeginHorizontal();
                for (int i = 0; i < units.Count; i++)
                {
                    var cand = units[i];
                    if (cand == null || cand.Stats == null) continue;
                    string nm = cand.Stats.Name ?? ("Unité " + i);
                    if (nm.Length > 12) nm = nm.Substring(0, 12);
                    if (i == _objVipIndex) GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                    if (GUILayout.Button(nm, GUILayout.Height(24))) _objVipIndex = i;
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Destination R : {_objEscortDestRadius}", GUILayout.Width(120));
                int wantDestR = Mathf.RoundToInt(GUILayout.HorizontalSlider(_objEscortDestRadius, 0f, 3f));
                if (wantDestR != _objEscortDestRadius) _objEscortDestRadius = wantDestR;
                _objEscortNoDest = GUILayout.Toggle(_objEscortNoDest, "sans destination (VIP vivant)");
                GUILayout.EndHorizontal();
                if (GUILayout.Button($"🛡️ Escorte {vipName} → destination {targetLabel}" + (_objEscortNoDest ? " (ignorée)" : "")))
                {
                    if (vip == null || vip.Stats == null)
                    {
                        _arena?.Log("🛡️ <b>Objectif</b> : aucun VIP valide sélectionné.");
                    }
                    else
                    {
                        List<HexCoordinates> dest = null;
                        if (!_objEscortNoDest)
                        {
                            var dc = _arena != null && _arena.CurrentTarget != null
                                ? _arena.CurrentTarget.CurrentCoords
                                : (_arena != null && _arena.PlayerUnit != null ? _arena.PlayerUnit.CurrentCoords : new HexCoordinates(4, 0));
                            dest = TurnManager.BuildObjectiveDisc(dc, _objEscortDestRadius);
                        }
                        _turnManager.SetupEscortCustom(vip, dest);
                        _turnManager.CheckCombatOver();
                        _arena?.Log($"🛡️ <b>Objectif</b> : escorte VIP {vipName}" + (_objEscortNoDest ? " (sans destination)." : $" → destination {targetLabel} R={_objEscortDestRadius}."));
                    }
                }
            }
            else
            {
                GUILayout.Label("<i>Aucune unité en arène pour l'escorte.</i>");
            }

            GUILayout.Space(2);
            if (_objTerminalCacheDirty) RefreshObjectiveTerminalCache();
            _objTerminalCache.RemoveAll(t => t == null);
            _objTerminalIndex = _objTerminalCache.Count == 0 ? 0 : Mathf.Clamp(_objTerminalIndex, 0, _objTerminalCache.Count - 1);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"💻 Terminaux scannés : {_objTerminalCache.Count}", GUILayout.Width(170));
            if (GUILayout.Button("↻ Scanner", GUILayout.Width(90))) RefreshObjectiveTerminalCache();
            GUILayout.Label($"Requis : {_objHackCount}", GUILayout.Width(80));
            int wantHack = Mathf.RoundToInt(GUILayout.HorizontalSlider(_objHackCount, 1f, 3f));
            if (wantHack != _objHackCount) _objHackCount = wantHack;
            GUILayout.EndHorizontal();
            if (_objTerminalCache.Count > 0)
            {
                _objTerminalIndex = Mathf.Clamp(_objTerminalIndex, 0, _objTerminalCache.Count - 1);
                var sel = _objTerminalIndex >= 0 && _objTerminalIndex < _objTerminalCache.Count ? _objTerminalCache[_objTerminalIndex] : null;
                string selName = sel != null ? sel.ObjectName : "—";
                GUILayout.Label($"Terminal : <b>{selName}</b> (index {_objTerminalIndex})");
                GUILayout.BeginHorizontal();
                for (int i = 0; i < _objTerminalCache.Count; i++)
                {
                    var t = _objTerminalCache[i];
                    if (t == null) continue;
                    string nm = t.ObjectName ?? ("Terminal " + i);
                    if (nm.Length > 14) nm = nm.Substring(0, 14);
                    if (i == _objTerminalIndex) GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                    if (GUILayout.Button(nm, GUILayout.Height(24))) _objTerminalIndex = i;
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndHorizontal();
                if (GUILayout.Button($"💻 Hacker {selName} x{_objHackCount}"))
                {
                    _turnManager.SetupHackTerminalCustom(sel != null ? sel.ObjectName : _objTerminalId, _objHackCount, sel);
                    _turnManager.CheckCombatOver();
                    _arena?.Log($"💻 <b>Objectif</b> : pirater {selName} x{_objHackCount}.");
                }
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("ID custom :", GUILayout.Width(80));
            _objTerminalId = GUILayout.TextField(_objTerminalId ?? "", GUILayout.Width(150));
            if (GUILayout.Button($"💻 Hacker ID x{_objHackCount}"))
            {
                _turnManager.SetupHackTerminalCustom(_objTerminalId, _objHackCount, null);
                _turnManager.CheckCombatOver();
                _arena?.Log($"💻 <b>Objectif</b> : pirater ID '{_objTerminalId}' x{_objHackCount}.");
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void RefreshObjectiveTerminalCache()
        {
            _objTerminalCache.Clear();
            try
            {
                var found = UnityEngine.Object.FindObjectsByType<TacticalInteractable>(FindObjectsInactive.Exclude);
                if (found != null) _objTerminalCache.AddRange(found);
            }
            catch { /* ignore */ }
            _objTerminalCacheDirty = false;
            if (_objTerminalCache.Count == 0) _objTerminalIndex = 0;
            else _objTerminalIndex = Mathf.Clamp(_objTerminalIndex, 0, _objTerminalCache.Count - 1);
        }
    }
}
