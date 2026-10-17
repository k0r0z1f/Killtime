using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;

namespace Killtime.Story
{
    public partial class ScenarioEditorDevWindow : FloatingWindow<ScenarioEditorDevWindow>
    {

        private bool IsQuickCreateOpen() => _quickCreateOpen;

        private void OpenQuickCreateMenu(WireConnectionDraft draft, Vector2 worldPos, Vector2 screenPos)
        {
            if (!IsQuickCreateDraftStillValid(draft)) return;
            _quickCreateDraft = draft;
            _quickCreateDraft.IsActive = true;
            _quickCreateWorldPos = new Vector2(Mathf.Round(worldPos.x), Mathf.Round(worldPos.y));
            _quickCreateScreenPos = screenPos;
            _quickCreateSearch = "";
            _quickCreateScroll = Vector2.zero;
            _quickCreateOpen = true;
            _quickCreateFocusSearch = true;
            // Un seul popup à la fois : le dropdown locuteurs recouvrirait la popup
            // (dessiné avant) et volerait ses clics.
            _actorDropKey = null;
        }

        private void CloseQuickCreateMenu()
        {
            _quickCreateOpen = false;
            _quickCreateDraft = new WireConnectionDraft();
            _quickCreateSearch = "";
        }

        private bool IsQuickCreateDraftStillValid(WireConnectionDraft draft)
        {
            if (_data == null) return false;
            if (draft.IsCinematicLink) return draft.SourceCineList != null;
            if (draft.IsCinematicOut) return draft.SourceCinematic != null && _data.Cinematics != null && _data.Cinematics.Contains(draft.SourceCinematic);
            if (draft.IsActorTalk) return draft.SourceActor != null && _data.Actors != null && _data.Actors.Contains(draft.SourceActor);
            if (draft.IsNode || draft.IsNodeChoice) return draft.SourceNode != null && _data.Nodes != null && _data.Nodes.Contains(draft.SourceNode);
            if (draft.IsTrigger) return draft.SourceTrigger != null && _data.Triggers != null && _data.Triggers.Contains(draft.SourceTrigger);
            if (draft.IsInteractable) return draft.SourceInteractable != null && _data.Interactables != null && _data.Interactables.Contains(draft.SourceInteractable);
            if (draft.IsEvent) return draft.SourceEvent != null && FindParentNodeOfEvent(draft.SourceEvent) != null;
            if (draft.FromChallengeLink) return draft.SourceChallenge != null && FindParentNodeOfChallenge(draft.SourceChallenge) != null;
            if (draft.FromConsequence) return draft.SourceConsequence != null && FindParentNodeOfConsequence(draft.SourceConsequence) != null;
            if (draft.FromChoice) return draft.SourceChoice != null && FindParentNodeOfChoice(draft.SourceChoice) != null;
            if (draft.SourceLine != null) return FindParentNodeOfLine(draft.SourceLine) != null;
            return false;
        }

        private struct QuickCreateOption
        {
            public QuickCreateKind Kind;
            public string Label;
            public string Hint;
            public Color Accent;
        }

        private List<QuickCreateOption> GetQuickCreateOptions(WireConnectionDraft draft)
        {
            var list = new List<QuickCreateOption>(5);
            void Add(QuickCreateKind k, string label, string hint, Color c) => list.Add(new QuickCreateOption { Kind = k, Label = label, Hint = hint, Accent = c });
            Color dlgCol = new Color(0f, 0.85f, 1f);
            Color evtCol = new Color(0.85f, 0.45f, 1f);
            Color consCol = new Color(0.35f, 1f, 0.55f);
            Color nodeCol = new Color(1f, 0.65f, 0.15f);
            Color cineCol = new Color(1f, 0.35f, 0.85f);
            Color sndCol = new Color(1f, 0.6f, 0.1f);
            Color ambCol = new Color(0.18f, 0.88f, 0.72f);

            if (draft.IsCinematicLink)
            {
                Add(QuickCreateKind.Cinematic, "🎬 Cinématique", "Nouvelle carte 🎬 liée", cineCol);
                return list;
            }
            if (draft.IsCinematicOut)
            {
                Add(QuickCreateKind.Cinematic, "🎬 Cinématique", "Enchaîner une 🎬", cineCol);
                Add(QuickCreateKind.Dialogue, "💬 Réplique", "Chaîner une réplique", dlgCol);
                Add(QuickCreateKind.Event, "▲ Événement", "Chaîner un événement", evtCol);
                Add(QuickCreateKind.Consequence, "◆ Conséquence", "Chaîner une conséquence", consCol);
                Add(QuickCreateKind.Node, "🟧 Nœud", "Sauter vers un nœud", nodeCol);
                return list;
            }
            if (draft.IsActorTalk)
            {
                Add(QuickCreateKind.Dialogue, "💬 Réplique", "Réplique de discussion (Parler à)", dlgCol);
                Add(QuickCreateKind.Node, "🟧 Nœud", "Enclencher un nœud (Parler à)", nodeCol);
                return list;
            }
            if (draft.IsNode || draft.IsNodeChoice || draft.IsTrigger || draft.IsInteractable)
            {
                Add(QuickCreateKind.Node, "🟧 Nœud", "Nouveau nœud cible", nodeCol);
                return list;
            }
            if (draft.FromChallengeLink || draft.FromConsequence)
            {
                Add(QuickCreateKind.Dialogue, "💬 Réplique", "Continuer vers une réplique", dlgCol);
                Add(QuickCreateKind.Consequence, "◆ Conséquence", "Enchaîner une conséquence", consCol);
                return list;
            }
            // Sorties principales de cartes (réplique / choix / événement) :
            // création de cartes sœurs dans le même nœud (visibles + exécutées).
            Add(QuickCreateKind.Dialogue, "💬 Réplique", "Nouvelle réplique liée", dlgCol);
            Add(QuickCreateKind.Event, "▲ Événement", "Nouvelle action scénique", evtCol);
            Add(QuickCreateKind.Consequence, "◆ Conséquence", "Récompense / effet", consCol);
            Add(QuickCreateKind.Sound, "🔊 Son", "Nouvelle carte son (à brancher)", sndCol);
            Add(QuickCreateKind.Ambience, "🌌 Ambiance", "Nouvelle carte ambiance (à brancher)", ambCol);
            return list;
        }

        private string GetQuickCreateTitle(WireConnectionDraft draft)
        {
            if (draft.IsCinematicLink) return "Lien 🎬 → ?";
            if (draft.IsCinematicOut) return "Fin 🎬 → ?";
            if (draft.IsActorTalk) return "Acteur 💬 → ?";
            if (draft.IsNode) return "Nœud → ?";
            if (draft.IsNodeChoice) return "Choix nœud → ?";
            if (draft.IsTrigger) return "Déclencheur → ?";
            if (draft.IsInteractable) return "Interactable → ?";
            if (draft.IsEvent) return "Événement → ?";
            if (draft.FromChallengeLink) return draft.ChallengeLinkIsSuccess ? "Défi ✔ → ?" : "Défi ✕ → ?";
            if (draft.FromConsequence) return draft.ConsLinkToCons ? "Conséquence ◆→ ?" : "Conséquence → ?";
            if (draft.FromChoice) return "Choix → ?";
            return "Réplique → ?";
        }

        private bool QuickCreateHasBreakableLink(WireConnectionDraft draft)
        {
            if (draft.IsCinematicOut && draft.SourceCinematic != null) return !string.IsNullOrWhiteSpace(draft.SourceCinematic.NextTargetId);
            if (draft.IsNode && draft.SourceNode != null) return !string.IsNullOrWhiteSpace(draft.SourceNode.NextNodeId);
            if (draft.IsNodeChoice && draft.SourceNode != null && draft.SourceNode.Choices != null && draft.NodeChoiceIndex >= 0 && draft.NodeChoiceIndex < draft.SourceNode.Choices.Count)
                return !string.IsNullOrWhiteSpace(draft.SourceNode.Choices[draft.NodeChoiceIndex].NextNodeId);
            // Les sorties de cartes conservent leur lien si le menu est annulé :
            // "rompre" n'a de sens que pour les sorties globales qui effaçaient avant.
            return false;
        }

        private void BreakQuickCreateLink(WireConnectionDraft draft)
        {
            if (draft.IsCinematicOut && draft.SourceCinematic != null) draft.SourceCinematic.NextTargetId = "";
            else if (draft.IsNode && draft.SourceNode != null) draft.SourceNode.NextNodeId = "";
            else if (draft.IsNodeChoice && draft.SourceNode != null && draft.SourceNode.Choices != null && draft.NodeChoiceIndex >= 0 && draft.NodeChoiceIndex < draft.SourceNode.Choices.Count && draft.SourceNode.Choices[draft.NodeChoiceIndex] != null)
                draft.SourceNode.Choices[draft.NodeChoiceIndex].NextNodeId = "";
        }

        private Rect ComputeQuickCreateRect(Rect canvasRect)
        {
            const float w = 320f;
            var opts = GetQuickCreateOptions(_quickCreateDraft);
            string search = (_quickCreateSearch ?? "").Trim();
            int visibleCount = 0;
            if (opts != null)
            {
                if (string.IsNullOrEmpty(search)) visibleCount = opts.Count;
                else
                {
                    string normSearch = NormalizeForSearch(search);
                    for (int i = 0; i < opts.Count; i++)
                        if (NormalizeForSearch(opts[i].Label).Contains(normSearch) || NormalizeForSearch(opts[i].Hint).Contains(normSearch)) visibleCount++;
                }
            }
            float h = 34f + 26f + Mathf.Max(1, visibleCount) * 36f + 8f;
            if (QuickCreateHasBreakableLink(_quickCreateDraft)) h += 30f;
            h += 32f;
            h = Mathf.Min(h, Mathf.Max(180f, canvasRect.height - 40f));
            Vector2 anchor = _quickCreateScreenPos;
            if (anchor.x <= -5000f) anchor = new Vector2(canvasRect.x + canvasRect.width * 0.5f, canvasRect.y + canvasRect.height * 0.4f);
            float x = Mathf.Clamp(anchor.x + 12f, canvasRect.x + 6f, canvasRect.xMax - w - 6f);
            float y = Mathf.Clamp(anchor.y - 20f, canvasRect.y + 34f, canvasRect.yMax - h - 6f);
            if (w + 12f >= canvasRect.width - 12f) x = canvasRect.x + 6f;
            return new Rect(x, y, Mathf.Min(w, canvasRect.width - 12f), h);
        }

        private void DrawQuickCreateMenu(Rect canvasRect)
        {
            if (!_quickCreateOpen) return;
            if (!IsQuickCreateDraftStillValid(_quickCreateDraft)) { CloseQuickCreateMenu(); return; }
            Event evt = Event.current;
            var allOpts = GetQuickCreateOptions(_quickCreateDraft);
            string search = (_quickCreateSearch ?? "").Trim();
            var visible = new List<QuickCreateOption>(allOpts.Count);
            if (string.IsNullOrEmpty(search)) visible.AddRange(allOpts);
            else
            {
                string ns = NormalizeForSearch(search);
                for (int i = 0; i < allOpts.Count; i++)
                    if (NormalizeForSearch(allOpts[i].Label).Contains(ns) || NormalizeForSearch(allOpts[i].Hint).Contains(ns)) visible.Add(allOpts[i]);
            }

            Rect panel = ComputeQuickCreateRect(canvasRect);
            _quickCreatePanelRect = panel;

            DrawSolidRect(panel, new Color(0.02f, 0.03f, 0.05f, 0.97f));
            DrawSolidRect(new Rect(panel.x, panel.y, panel.width, 2f), new Color(1f, 0.78f, 0.2f, 0.95f));
            DrawSolidRect(new Rect(panel.x, panel.yMax - 1f, panel.width, 1f), new Color(1f, 1f, 1f, 0.15f));
            DrawSolidRect(new Rect(panel.x, panel.y, 1f, panel.height), new Color(1f, 1f, 1f, 0.15f));
            DrawSolidRect(new Rect(panel.xMax - 1f, panel.y, 1f, panel.height), new Color(1f, 1f, 1f, 0.15f));

            float x = panel.x + 10f;
            float y = panel.y + 8f;
            float w = panel.width - 20f;

            GUI.Label(new Rect(x, y, w - 30f, 20f), $"<b>⚡ {GetQuickCreateTitle(_quickCreateDraft)}</b> <color=grey>· créer</color>");
            if (GUI.Button(new Rect(panel.xMax - 30f, y, 22f, 20f), "✕")) { CloseQuickCreateMenu(); evt.Use(); return; }
            y += 24f;

            GUI.SetNextControlName("QuickCreateSearch");
            string newSearch = GUI.TextField(new Rect(x, y, w, 20f), _quickCreateSearch ?? "");
            if (newSearch != _quickCreateSearch) { _quickCreateSearch = newSearch; }
            if (_quickCreateFocusSearch)
            {
                GUI.FocusControl("QuickCreateSearch");
                _quickCreateFocusSearch = false;
            }
            y += 26f;

            float listH = panel.height - (y - panel.y) - (QuickCreateHasBreakableLink(_quickCreateDraft) ? 30f : 0f) - 32f;
            Rect listRect = new Rect(x, y, w, Mathf.Max(40f, listH));
            Rect viewRect = new Rect(0f, 0f, w - 16f, visible.Count * 36f);
            _quickCreateScroll = GUI.BeginScrollView(listRect, _quickCreateScroll, viewRect, false, false);
            if (visible.Count == 0)
            {
                GUI.Label(new Rect(4f, 4f, viewRect.width - 8f, 28f), "<color=grey>(aucun type compatible — Entrée pour annuler)</color>");
            }
            for (int i = 0; i < visible.Count; i++)
            {
                var opt = visible[i];
                Rect row = new Rect(0f, i * 36f, viewRect.width, 34f);
                Color prev = GUI.color;
                GUI.color = new Color(opt.Accent.r, opt.Accent.g, opt.Accent.b, 0.22f);
                GUI.DrawTexture(row, PureWhiteTex);
                GUI.color = prev;
                DrawSolidRect(new Rect(row.x, row.y, 3f, row.height), opt.Accent);
                if (GUI.Button(new Rect(row.x + 6f, row.y + 2f, row.width - 12f, 20f), $"<b>{opt.Label}</b>")) { CreateQuickCreateCard(opt.Kind); GUI.EndScrollView(); evt.Use(); return; }
                GUI.Label(new Rect(row.x + 8f, row.y + 20f, row.width - 16f, 14f), $"<color=grey><size=11>{opt.Hint}</size></color>");
            }
            GUI.EndScrollView();
            y += listRect.height + 4f;

            if (QuickCreateHasBreakableLink(_quickCreateDraft))
            {
                GUI.backgroundColor = new Color(1f, 0.4f, 0.35f);
                if (GUI.Button(new Rect(x, y, w, 22f), "⊘ Rompre le lien existant")) { BreakQuickCreateLink(_quickCreateDraft); CloseQuickCreateMenu(); GUI.backgroundColor = Color.white; evt.Use(); return; }
                GUI.backgroundColor = Color.white;
                y += 28f;
            }
            GUI.Label(new Rect(x, y, w, 18f), "<color=grey>Echap / clic hors menu = annuler (lien conservé)</color>");

            if (evt.type == EventType.KeyDown)
            {
                if (evt.keyCode == KeyCode.Escape) { CloseQuickCreateMenu(); evt.Use(); return; }
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    if (visible.Count == 1) { CreateQuickCreateCard(visible[0].Kind); evt.Use(); return; }
                    if (visible.Count > 1 && string.IsNullOrEmpty(search)) { CreateQuickCreateCard(visible[0].Kind); evt.Use(); return; }
                }
            }
            // Clic hors menu = annuler sans toucher au lien existant.
            // (Cas nominal déjà intercepté en tête de canvas ; garde-fou si le rect
            // a bougé entre-temps.)
            if (evt.type == EventType.MouseDown && !panel.Contains(evt.mousePosition))
            {
                CloseQuickCreateMenu();
                evt.Use();
                return;
            }
        }
    }
}
