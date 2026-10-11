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

        private bool IsItemPickerOpen()
        {
            return _itemPickerTarget != null || _itemPickerCons != null;
        }

        private SceneDialogueRewardData PickerRewards()
        {
            if (_itemPickerTarget != null) return _itemPickerTarget.Challenge?.SuccessRewards;
            if (_itemPickerCons != null) return _itemPickerCons.Rewards;
            return null;
        }

        private string PickerOwnerLabel()
        {
            if (_itemPickerTarget != null) return _itemPickerTarget.Label ?? "";
            if (_itemPickerCons != null) return _itemPickerCons.Title ?? "";
            return "";
        }

        private void OpenItemPicker(SceneDialogueChoiceData choice, int entryIndex = -1)
        {
            if (choice == null) return;
            choice.Challenge ??= new SceneDialogueChallengeData();
            choice.Challenge.SuccessRewards ??= new SceneDialogueRewardData();
            StorySceneData.EnsureRewardDefaults(choice.Challenge.SuccessRewards);
            if (entryIndex >= 0)
            {
                var list = choice.Challenge.SuccessRewards.AdditionalItems;
                if (list == null || entryIndex >= list.Count) return;
            }
            _itemPickerTarget = choice;
            _itemPickerCons = null;
            _itemPickerEntry = entryIndex;
            _itemPickerSearch = GetPickerItemName();
            _itemPickerCat = -1;
            _itemPickerScroll = Vector2.zero;
            _itemPickerFocusSearch = true;
        }

        private void OpenItemPicker(SceneConsequenceData cons, int entryIndex = -1)
        {
            if (cons == null) return;
            cons.Rewards ??= new SceneDialogueRewardData();
            StorySceneData.EnsureRewardDefaults(cons.Rewards);
            if (entryIndex >= 0)
            {
                var list = cons.Rewards.AdditionalItems;
                if (list == null || entryIndex >= list.Count) return;
            }
            _itemPickerCons = cons;
            _itemPickerTarget = null;
            _itemPickerEntry = entryIndex;
            _itemPickerSearch = GetPickerItemName();
            _itemPickerCat = -1;
            _itemPickerScroll = Vector2.zero;
            _itemPickerFocusSearch = true;
        }

        private bool IsPickerOpenForCons(SceneConsequenceData cons, int entry)
        {
            return cons != null && ReferenceEquals(_itemPickerCons, cons) && _itemPickerEntry == entry;
        }

        private string GetPickerItemName()
        {
            var rew = PickerRewards();
            if (rew == null) return "";
            if (_itemPickerEntry >= 0)
            {
                if (rew.AdditionalItems == null || _itemPickerEntry >= rew.AdditionalItems.Count) return "";
                return rew.AdditionalItems[_itemPickerEntry]?.ItemName ?? "";
            }
            return rew.ItemRewardName ?? "";
        }

        private void SetPickerItem(InventoryItem def)
        {
            var rew = PickerRewards();
            if (rew == null || def == null) return;
            if (_itemPickerEntry >= 0)
            {
                if (rew.AdditionalItems == null || _itemPickerEntry >= rew.AdditionalItems.Count) return;
                var entry = rew.AdditionalItems[_itemPickerEntry];
                if (entry == null) return;
                entry.ItemName = def.Name;
                entry.ItemRewardType = def.Type;
            }
            else
            {
                rew.ItemRewardName = def.Name;
                rew.ItemRewardType = def.Type;
            }
            CloseItemPicker();
        }

        private void ClearPickerItem()
        {
            var rew = PickerRewards();
            if (rew == null) return;
            if (_itemPickerEntry >= 0)
            {
                if (rew.AdditionalItems != null && _itemPickerEntry < rew.AdditionalItems.Count)
                    rew.AdditionalItems.RemoveAt(_itemPickerEntry);
            }
            else
            {
                rew.ItemRewardName = "";
            }
            CloseItemPicker();
        }

        private void CloseItemPicker()
        {
            _itemPickerTarget = null;
            _itemPickerCons = null;
        }

        private bool ValidateItemPickerTarget()
        {
            if (_data == null || _data.Nodes == null) return false;
            if (_itemPickerTarget != null)
            {
                if (_itemPickerTarget.Challenge == null) return false;
                if (_itemPickerEntry >= 0)
                {
                    var list = _itemPickerTarget.Challenge.SuccessRewards?.AdditionalItems;
                    if (list == null || _itemPickerEntry >= list.Count) return false;
                }
            }
            else if (_itemPickerCons != null)
            {
                if (_itemPickerEntry >= 0)
                {
                    var list = _itemPickerCons.Rewards?.AdditionalItems;
                    if (list == null || _itemPickerEntry >= list.Count) return false;
                }
            }
            else return false;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                if (_itemPickerTarget != null && node.Dialogues != null)
                {
                    for (int d = 0; d < node.Dialogues.Count; d++)
                    {
                        var line = node.Dialogues[d];
                        if (line == null || line.Choices == null) continue;
                        for (int c = 0; c < line.Choices.Count; c++)
                        {
                            if (ReferenceEquals(line.Choices[c], _itemPickerTarget)) return true;
                        }
                    }
                }
                if (_itemPickerCons != null && node.Consequences != null)
                {
                    for (int k = 0; k < node.Consequences.Count; k++)
                    {
                        if (ReferenceEquals(node.Consequences[k], _itemPickerCons)) return true;
                    }
                }
            }
            return false;
        }

        private Rect GetItemPickerRect(Rect canvasRect)
        {
            float pw = Mathf.Min(540f, canvasRect.width - 30f);
            float ph = Mathf.Min(430f, canvasRect.height - 70f);
            return new Rect(canvasRect.x + (canvasRect.width - pw) * 0.5f, canvasRect.y + (canvasRect.height - ph) * 0.5f, pw, ph);
        }

        private void DrawItemPickerPanel(Rect canvasRect)
        {
            if (!IsItemPickerOpen()) return;
            if (!ValidateItemPickerTarget()) { CloseItemPicker(); return; }

            Rect panel = GetItemPickerRect(canvasRect);
            if (panel.width < 300f || panel.height < 220f) return;
            Event evt = Event.current;

            DrawSolidRect(panel, new Color(0.02f, 0.03f, 0.05f, 0.97f));
            DrawSolidRect(new Rect(panel.x, panel.y, panel.width, 2f), new Color(0.35f, 1f, 0.55f, 0.9f));

            float x = panel.x + 10f;
            float y = panel.y + 10f;
            float w = panel.width - 20f;

            GUI.Label(new Rect(x, y, w - 40f, 22f), $"<b>🎁 Catalogue — récompense</b> <color=grey>« {PickerOwnerLabel()} »{(_itemPickerEntry >= 0 ? $" (item #{_itemPickerEntry + 2})" : "")}</color>");
            if (GUI.Button(new Rect(panel.xMax - 34f, y, 26f, 22f), "✕")) { CloseItemPicker(); return; }
            y += 26f;

            string current = GetPickerItemName();
            if (string.IsNullOrEmpty(current)) current = "(aucun)";
            GUI.Label(new Rect(x, y, w - 90f, 20f), $"Actuel : <b>{current}</b>");
            GUI.backgroundColor = new Color(0.85f, 0.35f, 0.35f);
            if (GUI.Button(new Rect(panel.xMax - 94f, y, 84f, 20f), "Effacer"))
            {
                ClearPickerItem();
                GUI.backgroundColor = Color.white;
                return;
            }
            GUI.backgroundColor = Color.white;
            y += 24f;

            GUI.Label(new Rect(x, y, 30f, 20f), "🔍");
            GUI.SetNextControlName("ItemPickerSearch");
            _itemPickerSearch = GUI.TextField(new Rect(x + 32f, y, w - 32f - 190f, 20f), _itemPickerSearch ?? "");
            if (_itemPickerFocusSearch) { GUI.FocusControl("ItemPickerSearch"); _itemPickerFocusSearch = false; }
            float cx = x + w - 186f;
            if (GUI.Button(new Rect(cx, y, 24f, 20f), "◀"))
            {
                _itemPickerCat--;
                if (_itemPickerCat < -1) _itemPickerCat = ArmoryCatalog.Categories.Length - 1;
                _itemPickerScroll = Vector2.zero;
            }
            string catLabel = _itemPickerCat < 0 ? "Toutes" : ArmoryCatalog.Categories[_itemPickerCat];
            GUI.Label(new Rect(cx + 26f, y, 132f, 20f), $"<b>{catLabel}</b>");
            if (GUI.Button(new Rect(cx + 160f, y, 24f, 20f), "▶"))
            {
                _itemPickerCat++;
                if (_itemPickerCat >= ArmoryCatalog.Categories.Length) _itemPickerCat = -1;
                _itemPickerScroll = Vector2.zero;
            }
            y += 24f;

            // Filtrage : normalisé sans accents, préfixe d'abord, puis contenu, puis catégorie.
            string normSearch = NormalizeForSearch(_itemPickerSearch);
            var matches = new List<KeyValuePair<int, InventoryItem>>();
            var all = ArmoryCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def == null || string.IsNullOrEmpty(def.Name)) continue;
                if (_itemPickerCat >= 0 && !string.Equals(def.Category, ArmoryCatalog.Categories[_itemPickerCat], StringComparison.OrdinalIgnoreCase)) continue;
                if (normSearch.Length > 0)
                {
                    string nName = NormalizeForSearch(def.Name);
                    int score;
                    if (nName.StartsWith(normSearch, StringComparison.Ordinal)) score = 0;
                    else if (nName.Contains(normSearch)) score = 1;
                    else if (NormalizeForSearch(def.Category ?? "").Contains(normSearch)) score = 2;
                    else continue;
                    matches.Add(new KeyValuePair<int, InventoryItem>(score, def));
                }
                else
                {
                    matches.Add(new KeyValuePair<int, InventoryItem>(3, def));
                }
            }
            matches.Sort((a, b) =>
            {
                int d = a.Key.CompareTo(b.Key);
                return d != 0 ? d : string.Compare(a.Value.Name, b.Value.Name, StringComparison.OrdinalIgnoreCase);
            });
            if (matches.Count > 200) matches.RemoveRange(200, matches.Count - 200);

            GUI.Label(new Rect(x, y, w, 18f), $"<color=grey>{matches.Count} résultat(s) — Entrée = premier, Échap = fermer</color>");
            y += 20f;

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape) { CloseItemPicker(); evt.Use(); return; }
            if (evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && matches.Count > 0)
            {
                SetPickerItem(matches[0].Value);
                evt.Use();
                return;
            }

            Rect listRect = new Rect(x, y, w, panel.yMax - 8f - y);
            if (listRect.height < 40f) return;
            float rowH = 22f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, matches.Count * rowH);
            _itemPickerScroll = GUI.BeginScrollView(listRect, _itemPickerScroll, viewRect);
            for (int i = 0; i < matches.Count; i++)
            {
                var def = matches[i].Value;
                if (GUI.Button(new Rect(0f, i * rowH, viewRect.width, rowH - 2f),
                    $"{def.Name} — <color=grey>{def.Category} · {def.PriceCE} CE · {def.Rarity}</color>"))
                {
                    SetPickerItem(def);
                    GUI.EndScrollView();
                    return;
                }
            }
            if (matches.Count == 0)
                GUI.Label(new Rect(0f, 0f, viewRect.width, 22f), "<color=grey>(aucun résultat — modifiez la recherche)</color>");
            GUI.EndScrollView();
        }

        private void OpenActorDropdown(string key, Rect anchorWorld, string current, System.Action<string> setter)
        {
            _actorDropKey = key;
            _actorDropAnchorWorld = anchorWorld;
            _actorDropCurrent = current ?? "";
            _actorDropSetter = setter;
            _actorDropScroll = Vector2.zero;
            _actorDropIndex = -1;
        }

        private System.Collections.Generic.List<string> BuildActorCandidates()
        {
            var list = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            System.Action<string> add = n =>
            {
                if (string.IsNullOrWhiteSpace(n)) return;
                n = n.Trim();
                if (seen.Add(n)) list.Add(n);
            };
            if (_data != null && _data.Actors != null)
            {
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    add(a.DisplayName);
                    if (!string.IsNullOrWhiteSpace(a.ActorId) && !string.Equals(a.ActorId, a.DisplayName, System.StringComparison.OrdinalIgnoreCase))
                        add(a.ActorId);
                }
            }
            if (_data != null && _data.Nodes != null)
            {
                for (int n = 0; n < _data.Nodes.Count; n++)
                {
                    var node = _data.Nodes[n];
                    if (node == null) continue;
                    if (node.Dialogues != null)
                    {
                        for (int d = 0; d < node.Dialogues.Count; d++)
                        {
                            var line = node.Dialogues[d];
                            if (line == null) continue;
                            add(line.SpeakerId);
                        }
                    }
                    if (node.Consequences != null)
                    {
                        for (int k = 0; k < node.Consequences.Count; k++)
                        {
                            var cons = node.Consequences[k];
                            if (cons == null) continue;
                            add(cons.SpeakerId);
                        }
                    }
                }
            }
            if (list.Count > 40) list.RemoveRange(40, list.Count - 40);
            return list;
        }

        private void DrawActorDropdownPopup(Rect canvasRect)
        {
            if (_actorDropKey == null) return;
            Event evt = Event.current;
            var candidates = BuildActorCandidates();

            const float rowH = 20f;
            const float popW = 210f;
            // Hauteur bornée + défilement : sans ça le bas de liste est
            // inatteignable (popup plus haute que le canvas, sans scroll).
            const int maxVisibleRows = 12;
            int rows = candidates.Count > 0 ? candidates.Count : 1;
            float fullH = rows * rowH + 6f;
            float viewH = Mathf.Min(fullH, maxVisibleRows * rowH + 6f);

            // Cadrage dans la zone monde visible (le BeginGroup clippe tout ce
            // qui dépasse) : on rabat en X, et on bascule au-dessus de l'ancre
            // s'il n'y a pas la place en dessous.
            Rect visWorld = GetVisibleWorldRect(canvasRect);
            float popX = Mathf.Clamp(_actorDropAnchorWorld.x, visWorld.xMin + 4f, Mathf.Max(visWorld.xMin + 4f, visWorld.xMax - popW - 4f));
            float popY = _actorDropAnchorWorld.yMax + 2f;
            if (popY + viewH > visWorld.yMax - 4f)
                popY = _actorDropAnchorWorld.y - viewH - 2f;
            popY = Mathf.Clamp(popY, visWorld.yMin + 4f, Mathf.Max(visWorld.yMin + 4f, visWorld.yMax - viewH - 4f));
            Rect pop = new Rect(popX, popY, popW, viewH);
            _actorDropWorld = pop;

            DrawGraphSolidRect(pop, new Color(0.03f, 0.05f, 0.08f, 0.97f));
            DrawGraphSolidRect(new Rect(pop.x, pop.y, pop.width, 1.5f), new Color(0.0f, 0.85f, 1f, 0.9f));
            DrawGraphSolidRect(new Rect(pop.x, pop.yMax - 1f, pop.width, 1f), new Color(1f, 1f, 1f, 0.15f));
            DrawGraphSolidRect(new Rect(pop.x, pop.y, 1f, pop.height), new Color(1f, 1f, 1f, 0.15f));
            DrawGraphSolidRect(new Rect(pop.xMax - 1f, pop.y, 1f, pop.height), new Color(1f, 1f, 1f, 0.15f));

            if (candidates.Count == 0)
            {
                GUI.Label(GraphRect(new Rect(pop.x + 6f, pop.y + 3f, popW - 12f, rowH)), "<color=grey>(aucun acteur — saisie libre)</color>");
            }
            else
            {
                // Navigation clavier : index courant initialisé sur la valeur active.
                if (_actorDropIndex < 0 || _actorDropIndex >= candidates.Count)
                {
                    _actorDropIndex = 0;
                    for (int k = 0; k < candidates.Count; k++)
                    {
                        if (string.Equals(candidates[k], _actorDropCurrent, System.StringComparison.OrdinalIgnoreCase))
                        {
                            _actorDropIndex = k;
                            break;
                        }
                    }
                }
                float zoom = Mathf.Max(0.0001f, _zoom);
                float rowHs = rowH * zoom;
                Rect viewScreen = GraphRect(pop);
                // Réserve la barre de défilement quand le contenu déborde.
                float sbW = fullH > viewH + 0.5f ? 16f : 0f;
                Rect contentScreen = new Rect(0f, 0f, Mathf.Max(10f, viewScreen.width - sbW - 8f), candidates.Count * rowHs);
                // Zone de scroll intérieure (marges de 3px comme avant).
                Rect scrollScreen = new Rect(viewScreen.x + 3f * zoom, viewScreen.y + 3f * zoom, viewScreen.width - 6f * zoom, viewScreen.height - 6f * zoom);
                _actorDropScroll = GUI.BeginScrollView(scrollScreen, _actorDropScroll, contentScreen, false, false);
                for (int i = 0; i < candidates.Count; i++)
                {
                    Rect r = new Rect(0f, i * rowHs, contentScreen.width, rowHs - 2f * zoom);
                    bool isCurrent = string.Equals(candidates[i], _actorDropCurrent, System.StringComparison.OrdinalIgnoreCase);
                    string label = (i == _actorDropIndex ? "► " : (isCurrent ? "► " : "")) + candidates[i];
                    // Survol souris = sélection clavier (Entrée valide la ligne survolée).
                    if (evt.type == EventType.Repaint && r.Contains(Event.current.mousePosition))
                        _actorDropIndex = i;
                    // Bouton brut (pas de wrapper) : la popup reste cliquable en mode modal.
                    if (GUI.Button(r, label))
                    {
                        _actorDropSetter?.Invoke(candidates[i]);
                        _actorDropKey = null;
                        GUI.EndScrollView();
                        evt.Use();
                        return;
                    }
                }
                GUI.EndScrollView();
                // Maintient la ligne clavier visible après un déplacement flèches.
                float visTop = _actorDropScroll.y;
                float visH = Mathf.Max(1f, scrollScreen.height);
                float selTop = _actorDropIndex * rowHs;
                if (selTop < visTop) _actorDropScroll.y = selTop;
                else if (selTop + rowHs > visTop + visH) _actorDropScroll.y = selTop + rowHs - visH;
            }

            if (evt.type == EventType.KeyDown)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    _actorDropKey = null;
                    evt.Use();
                }
                else if (candidates.Count > 0 && (evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.UpArrow))
                {
                    if (_actorDropIndex < 0) _actorDropIndex = 0;
                    else _actorDropIndex = (evt.keyCode == KeyCode.DownArrow)
                        ? Mathf.Min(candidates.Count - 1, _actorDropIndex + 1)
                        : Mathf.Max(0, _actorDropIndex - 1);
                    _actorDropCurrent = candidates[_actorDropIndex];
                    evt.Use();
                }
                else if (candidates.Count > 0 && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
                {
                    int pick = (_actorDropIndex >= 0 && _actorDropIndex < candidates.Count) ? _actorDropIndex : 0;
                    _actorDropSetter?.Invoke(candidates[pick]);
                    _actorDropKey = null;
                    evt.Use();
                }
            }
        }

        private void DrawCardHoverTooltip(Rect canvasRect)
        {
            if (string.IsNullOrEmpty(_hoverBody) && string.IsNullOrEmpty(_hoverTitle)) return;
            if (_hoverBodyStyle == null)
            {
                _hoverBodyStyle = new GUIStyle(GUI.skin.label);
                _hoverBodyStyle.wordWrap = true;
                _hoverBodyStyle.richText = true;
            }

            float tipW = Mathf.Min(360f, canvasRect.width - 20f);
            if (tipW < 160f) return;
            float bodyH = _hoverBodyStyle.CalcHeight(new GUIContent(_hoverBody), tipW - 16f);
            bodyH = Mathf.Min(bodyH, 220f);
            float tipH = 26f + bodyH + 10f;

            Vector2 anchor = _hoverScreenPos + new Vector2(18f, 20f);
            if (anchor.x + tipW > canvasRect.xMax - 6f) anchor.x = canvasRect.xMax - 6f - tipW;
            if (anchor.y + tipH > canvasRect.yMax - 6f) anchor.y = _hoverScreenPos.y - 8f - tipH;
            if (anchor.x < canvasRect.x + 6f) anchor.x = canvasRect.x + 6f;
            if (anchor.y < canvasRect.y + 6f) anchor.y = canvasRect.y + 6f;
            Rect tip = new Rect(anchor.x, anchor.y, tipW, tipH);

            DrawSolidRect(tip, new Color(0.03f, 0.05f, 0.08f, 0.96f));
            Color prev = GUI.color;
            GUI.color = new Color(0.0f, 0.85f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(tip.x, tip.y, tip.width, 2f), PureWhiteTex);
            GUI.color = prev;
            GUI.Label(new Rect(tip.x + 8f, tip.y + 5f, tip.width - 16f, 20f), $"<b>{_hoverTitle}</b>");
            GUI.Label(new Rect(tip.x + 8f, tip.y + 26f, tip.width - 16f, bodyH), _hoverBody, _hoverBodyStyle);
        }
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

        private SceneNodeData FindParentNodeOfLine(SceneDialogueLineData line)
        {
            if (_data?.Nodes == null || line == null) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node?.Dialogues == null) continue;
                for (int d = 0; d < node.Dialogues.Count; d++)
                    if (ReferenceEquals(node.Dialogues[d], line)) return node;
            }
            return null;
        }

        private SceneNodeData FindParentNodeOfChoice(SceneDialogueChoiceData choice)
        {
            if (_data?.Nodes == null || choice == null) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node?.Dialogues == null) continue;
                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var line = node.Dialogues[d];
                    if (line?.Choices == null) continue;
                    for (int c = 0; c < line.Choices.Count; c++)
                        if (ReferenceEquals(line.Choices[c], choice)) return node;
                }
            }
            return null;
        }

        private SceneNodeData FindParentNodeOfChallenge(SceneDialogueChallengeData chal)
        {
            if (_data?.Nodes == null || chal == null) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node?.Dialogues == null) continue;
                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var line = node.Dialogues[d];
                    if (line?.Choices == null) continue;
                    for (int c = 0; c < line.Choices.Count; c++)
                        if (line.Choices[c] != null && ReferenceEquals(line.Choices[c].Challenge, chal)) return node;
                }
            }
            return null;
        }

        private SceneNodeData FindParentNodeOfEvent(SceneEventData ev)
        {
            if (_data?.Nodes == null || ev == null) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node?.Events == null) continue;
                for (int e = 0; e < node.Events.Count; e++)
                    if (ReferenceEquals(node.Events[e], ev)) return node;
            }
            return null;
        }

        private SceneNodeData FindParentNodeOfConsequence(SceneConsequenceData cons)
        {
            if (_data?.Nodes == null || cons == null) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node?.Consequences == null) continue;
                for (int k = 0; k < node.Consequences.Count; k++)
                    if (ReferenceEquals(node.Consequences[k], cons)) return node;
            }
            return null;
        }

        private SceneNodeData FindParentNodeForDraft(WireConnectionDraft draft)
        {
            if (draft.SourceLine != null)
            {
                var p = FindParentNodeOfLine(draft.SourceLine);
                if (p != null) return p;
            }
            if (draft.SourceChoice != null)
            {
                var p = FindParentNodeOfChoice(draft.SourceChoice);
                if (p != null) return p;
            }
            if (draft.SourceChallenge != null)
            {
                var p = FindParentNodeOfChallenge(draft.SourceChallenge);
                if (p != null) return p;
            }
            if (draft.SourceEvent != null)
            {
                var p = FindParentNodeOfEvent(draft.SourceEvent);
                if (p != null) return p;
            }
            if (draft.SourceConsequence != null)
            {
                var p = FindParentNodeOfConsequence(draft.SourceConsequence);
                if (p != null) return p;
            }
            return null;
        }

        private string MakeUniqueLineId(SceneNodeData parent)
        {
            int nodeIdx = parent != null ? Mathf.Max(0, _data.Nodes.IndexOf(parent)) : 0;
            int baseCount = parent?.Dialogues != null ? parent.Dialogues.Count : 0;
            for (int attempt = 1; attempt < 500; attempt++)
            {
                string cand = $"line_{nodeIdx + 1}_{baseCount + attempt}";
                if (!SceneContainsLine(cand) && !_cachedInputSockets.ContainsKey(cand)) return cand;
            }
            return $"line_{nodeIdx + 1}_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueEventId(SceneNodeData parent)
        {
            int nodeIdx = parent != null ? Mathf.Max(0, _data.Nodes.IndexOf(parent)) : 0;
            int baseCount = parent?.Events != null ? parent.Events.Count : 0;
            for (int attempt = 1; attempt < 500; attempt++)
            {
                string cand = $"evt_{nodeIdx + 1}_{baseCount + attempt}";
                bool exists = false;
                if (_data?.Nodes != null)
                    for (int n = 0; n < _data.Nodes.Count && !exists; n++)
                    {
                        var nd = _data.Nodes[n];
                        if (nd?.Events == null) continue;
                        for (int e = 0; e < nd.Events.Count; e++)
                            if (nd.Events[e] != null && string.Equals(nd.Events[e].EventId, cand, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                    }
                if (!exists && !_cachedInputSockets.ContainsKey(cand)) return cand;
            }
            return $"evt_{nodeIdx + 1}_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueConsId(SceneNodeData parent)
        {
            int nodeIdx = parent != null ? Mathf.Max(0, _data.Nodes.IndexOf(parent)) : 0;
            int baseCount = parent?.Consequences != null ? parent.Consequences.Count : 0;
            for (int attempt = 1; attempt < 500; attempt++)
            {
                string cand = $"cons_{nodeIdx + 1}_{baseCount + attempt}";
                if (!SceneContainsConsequence(cand) && !_cachedInputSockets.ContainsKey(cand)) return cand;
            }
            return $"cons_{nodeIdx + 1}_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueNodeId()
        {
            int baseCount = _data.Nodes != null ? _data.Nodes.Count : 0;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string cand = $"node_{baseCount + attempt}";
                if (_data.FindNode(cand) == null && !_cachedNodeInputSockets.ContainsKey(cand)) return cand;
            }
            return $"node_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueCineId()
        {
            int baseCount = _data.Cinematics != null ? _data.Cinematics.Count : 0;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string cand = $"cine_{baseCount + attempt}";
                if (_data.FindCinematic(cand) == null && !_cachedCinematicInputSockets.ContainsKey(cand)) return cand;
            }
            return $"cine_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueSoundId()
        {
            int baseCount = _data.Sounds != null ? _data.Sounds.Count : 0;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string cand = $"snd_{baseCount + attempt}";
                if (_data.FindSound(cand) == null && !_cachedSoundInputSockets.ContainsKey(cand)) return cand;
            }
            return $"snd_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueAmbienceId()
        {
            int baseCount = _data.Ambiences != null ? _data.Ambiences.Count : 0;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string cand = $"amb_{baseCount + attempt}";
                if (_data.FindAmbience(cand) == null && !_cachedAmbienceInputSockets.ContainsKey(cand)) return cand;
            }
            return $"amb_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private SceneNodeData FindBestHostNodeForCinematicCard(Vector2 dropWorld)
        {
            if (_data?.Nodes == null || _data.Nodes.Count == 0) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                Rect box = ComputeNodeBoundingBox(node);
                if (box.Contains(dropWorld)) return node;
            }
            SceneNodeData best = null;
            float bestDist = float.MaxValue;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                Rect box = ComputeNodeBoundingBox(node);
                float d = Vector2.Distance(box.center, dropWorld);
                if (d < bestDist) { bestDist = d; best = node; }
            }
            return bestDist <= 600f ? best : null;
        }

        private void PlayQuickCreateLinkSound()
        {
            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Filter, 0.6f);
        }

        private void CreateQuickCreateCard(QuickCreateKind kind)
        {
            var draft = _quickCreateDraft;
            Vector2 drop = _quickCreateWorldPos;
            if (!IsQuickCreateDraftStillValid(draft)) { CloseQuickCreateMenu(); return; }

            // --- Sorties globales vers un nouveau nœud ---
            if (kind == QuickCreateKind.Node)
            {
                string newNodeId = MakeUniqueNodeId();
                var newNode = new SceneNodeData
                {
                    NodeId = newNodeId,
                    Title = "Nouveau Nœud",
                    GraphPosX = drop.x,
                    GraphPosY = drop.y - 18f
                };
                _data.Nodes.Add(newNode);
                if (draft.IsNode && draft.SourceNode != null) draft.SourceNode.NextNodeId = newNodeId;
                else if (draft.IsNodeChoice && draft.SourceNode != null && draft.SourceNode.Choices != null && draft.NodeChoiceIndex >= 0 && draft.NodeChoiceIndex < draft.SourceNode.Choices.Count && draft.SourceNode.Choices[draft.NodeChoiceIndex] != null) draft.SourceNode.Choices[draft.NodeChoiceIndex].NextNodeId = newNodeId;
                else if (draft.IsTrigger && draft.SourceTrigger != null) draft.SourceTrigger.TargetNodeId = newNodeId;
                else if (draft.IsInteractable && draft.SourceInteractable != null) draft.SourceInteractable.TriggerNodeId = newNodeId;
                else if (draft.IsCinematicOut && draft.SourceCinematic != null) draft.SourceCinematic.NextTargetId = newNodeId;
                else if (draft.IsActorTalk && draft.SourceActor != null) SetActorTalkTarget(draft.SourceActor, draft.SourceTalkIndex, newNodeId);
                PlayQuickCreateLinkSound();
                CloseQuickCreateMenu();
                return;
            }

            // --- Nouvelle cinématique globale ---
            if (kind == QuickCreateKind.Cinematic)
            {
                _data.Cinematics ??= new List<SceneCinematicData>();
                string newCineId = MakeUniqueCineId();
                var cine = new SceneCinematicData
                {
                    CinematicId = newCineId,
                    Title = $"Cinématique {_data.Cinematics.Count + 1}",
                    GraphPosX = drop.x + 30f,
                    GraphPosY = drop.y - 26f
                };
                cine.Shots.Add(new SceneCinematicShotData { ShotId = "shot_1", Label = "Plan 1" });
                _data.Cinematics.Add(cine);
                if (draft.IsCinematicLink && draft.SourceCineList != null) StorySceneData.AddCineLink(draft.SourceCineList, newCineId);
                else if (draft.IsCinematicOut && draft.SourceCinematic != null) draft.SourceCinematic.NextTargetId = newCineId;
                PlayQuickCreateLinkSound();
                CloseQuickCreateMenu();
                return;
            }

            // --- Nouvelle carte son / ambiance (globale, non liée : à brancher au fil) ---
            if (kind == QuickCreateKind.Sound)
            {
                _data.Sounds ??= new List<SceneSoundData>();
                string newSndId = MakeUniqueSoundId();
                _data.Sounds.Add(new SceneSoundData
                {
                    SoundId = newSndId,
                    Title = $"Son {_data.Sounds.Count + 1}",
                    GraphPosX = drop.x - SndCardDefaultW * 0.5f,
                    GraphPosY = drop.y - 30f
                });
                PlayQuickCreateLinkSound();
                CloseQuickCreateMenu();
                return;
            }
            if (kind == QuickCreateKind.Ambience)
            {
                _data.Ambiences ??= new List<SceneAmbienceTrackData>();
                string newAmbId = MakeUniqueAmbienceId();
                _data.Ambiences.Add(new SceneAmbienceTrackData
                {
                    AmbienceId = newAmbId,
                    Title = $"Ambiance {_data.Ambiences.Count + 1}",
                    PresetId = "amb_asteroid_base",
                    GraphPosX = drop.x - 160f,
                    GraphPosY = drop.y - 30f
                });
                PlayQuickCreateLinkSound();
                CloseQuickCreateMenu();
                return;
            }

            // --- Nouvelles cartes sœurs (même nœud que la source) ---
            SceneNodeData parent = FindParentNodeForDraft(draft);
            if (parent == null && (draft.IsCinematicOut || draft.IsActorTalk))
            {
                parent = FindBestHostNodeForCinematicCard(drop);
                if (parent == null)
                {
                    string hostNodeId = MakeUniqueNodeId();
                    parent = new SceneNodeData
                    {
                        NodeId = hostNodeId,
                        Title = "Nouveau Nœud",
                        GraphPosX = drop.x - 40f,
                        GraphPosY = drop.y - 60f
                    };
                    _data.Nodes.Add(parent);
                }
            }
            if (parent == null) { CloseQuickCreateMenu(); return; }
            parent.Dialogues ??= new List<SceneDialogueLineData>();
            parent.Events ??= new List<SceneEventData>();
            parent.Consequences ??= new List<SceneConsequenceData>();

            Vector2 cardPos = new Vector2(drop.x + 30f, drop.y - 26f);
            string newId = "";
            if (kind == QuickCreateKind.Dialogue)
            {
                newId = MakeUniqueLineId(parent);
                parent.Dialogues.Add(new SceneDialogueLineData
                {
                    LineId = newId,
                    SpeakerId = "",
                    Speech = "Nouvelle réplique...",
                    GraphPosX = cardPos.x,
                    GraphPosY = cardPos.y
                });
            }
            else if (kind == QuickCreateKind.Event)
            {
                newId = MakeUniqueEventId(parent);
                parent.Events.Add(new SceneEventData
                {
                    EventId = newId,
                    Title = "Action Scénique",
                    GraphPosX = cardPos.x,
                    GraphPosY = cardPos.y
                });
            }
            else
            {
                newId = MakeUniqueConsId(parent);
                parent.Consequences.Add(new SceneConsequenceData
                {
                    ConsequenceId = newId,
                    Title = "Récompense",
                    GraphPosX = cardPos.x,
                    GraphPosY = cardPos.y
                });
            }

            // Câblage auto vers la nouvelle carte (le wire fait foi, les champs
            // concurrents sont effacés comme lors d'un drop classique).
            if (draft.IsActorTalk && draft.SourceActor != null)
            {
                if (kind == QuickCreateKind.Dialogue) SetActorTalkTarget(draft.SourceActor, draft.SourceTalkIndex, newId);
            }
            else if (draft.IsCinematicOut && draft.SourceCinematic != null)
            {
                draft.SourceCinematic.NextTargetId = newId;
            }
            else if (draft.IsEvent && draft.SourceEvent != null)
            {
                draft.SourceEvent.NextEventId = newId;
            }
            else if (draft.FromChallengeLink && draft.SourceChallenge != null)
            {
                bool toCons = kind == QuickCreateKind.Consequence;
                if (draft.ChallengeLinkIsSuccess)
                {
                    draft.SourceChallenge.SuccessConsequenceId = toCons ? newId : "";
                    draft.SourceChallenge.SuccessNextLineId = toCons ? "" : newId;
                }
                else
                {
                    draft.SourceChallenge.FailureConsequenceId = toCons ? newId : "";
                    draft.SourceChallenge.FailureNextLineId = toCons ? "" : newId;
                }
            }
            else if (draft.FromConsequence && draft.SourceConsequence != null)
            {
                if (kind == QuickCreateKind.Consequence)
                {
                    draft.SourceConsequence.NextConsequenceId = newId;
                    draft.SourceConsequence.NextLineId = "";
                }
                else
                {
                    draft.SourceConsequence.NextLineId = newId;
                    draft.SourceConsequence.NextConsequenceId = "";
                }
            }
            else if (draft.FromChoice && draft.SourceChoice != null)
            {
                draft.SourceChoice.NextLineId = newId;
            }
            else if (draft.SourceLine != null)
            {
                draft.SourceLine.NextLineId = newId;
            }

            PlayQuickCreateLinkSound();
            CloseQuickCreateMenu();
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
        private void SwallowOverlappedHeaderPress(Rect canvasRect, Vector2 mouseWorld, Vector2 mouseScreen)
        {
            Event evt = Event.current;
            if (evt == null) return;
            // Fin de geste : purger les réservations (aucun Use : les nettoyages
            // inline des drags doivent toujours voir ce MouseUp).
            if (evt.type == EventType.MouseUp)
            {
                _lateDragTarget = null;
                _lateResizeTarget = null;
                return;
            }
            if (evt.type != EventType.MouseDown || evt.button != 0 || evt.clickCount >= 2) return;
            if (_pickerCapturesMouse) return;
            if (!string.IsNullOrEmpty(_draggingCardKey) || !string.IsNullOrEmpty(_resizingCardKey) || !string.IsNullOrEmpty(_draggingNodeId)) return;
            if (_lateDragTarget != null || _lateResizeTarget != null) return;
            if (!canvasRect.Contains(mouseScreen)) return;
            if (_data == null || _data.Nodes == null) return;

            Rect vis = GetVisibleWorldRect(canvasRect);

            // Cartes globales, de la plus au-dessus vers le fond.
            if (_data.Cinematics != null)
            {
                for (int i = _data.Cinematics.Count - 1; i >= 0; i--)
                {
                    var c = _data.Cinematics[i];
                    if (c == null) continue;
                    Vector2 s = GetCinematicCardSize(c);
                    if (!vis.Overlaps(new Rect(c.GraphPosX, c.GraphPosY, s.x, s.y))) continue;
                    if (HitCardGrip(c.GraphPosX, c.GraphPosY, s, mouseWorld)) { ClaimLateResize(c, mouseWorld, mouseScreen, evt); return; }
                    if (HitCardHeader(c.GraphPosX, c.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(c, mouseWorld, mouseScreen, evt); return; }
                }
            }
            if (_data.Actors != null)
            {
                for (int i = _data.Actors.Count - 1; i >= 0; i--)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    Vector2 s = GetActorCardSize(a);
                    if (!vis.Overlaps(new Rect(a.GraphPosX, a.GraphPosY, s.x, s.y))) continue;
                    if (HitCardGrip(a.GraphPosX, a.GraphPosY, s, mouseWorld)) { ClaimLateResize(a, mouseWorld, mouseScreen, evt); return; }
                    if (HitCardHeader(a.GraphPosX, a.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(a, mouseWorld, mouseScreen, evt); return; }
                }
            }
            if (_data.Interactables != null)
            {
                for (int i = _data.Interactables.Count - 1; i >= 0; i--)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    Vector2 s = GetInteractableCardSize(it);
                    if (!vis.Overlaps(new Rect(it.GraphPosX, it.GraphPosY, s.x, s.y))) continue;
                    if (HitCardGrip(it.GraphPosX, it.GraphPosY, s, mouseWorld)) { ClaimLateResize(it, mouseWorld, mouseScreen, evt); return; }
                    if (HitCardHeader(it.GraphPosX, it.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(it, mouseWorld, mouseScreen, evt); return; }
                }
            }
            if (_data.Triggers != null)
            {
                for (int t = _data.Triggers.Count - 1; t >= 0; t--)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    Vector2 s = GetTriggerCardSize(trg);
                    if (!vis.Overlaps(new Rect(trg.GraphPosX, trg.GraphPosY, s.x, s.y))) continue;
                    if (HitCardGrip(trg.GraphPosX, trg.GraphPosY, s, mouseWorld)) { ClaimLateResize(trg, mouseWorld, mouseScreen, evt); return; }
                    if (HitCardHeader(trg.GraphPosX, trg.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(trg, mouseWorld, mouseScreen, evt); return; }
                }
            }

            // Cartes internes, nœuds en sens inverse, conséquences/événements/dialogues inversés.
            for (int n = _data.Nodes.Count - 1; n >= 0; n--)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                if (node.Consequences != null)
                {
                    for (int k = node.Consequences.Count - 1; k >= 0; k--)
                    {
                        var cons = node.Consequences[k];
                        if (cons == null) continue;
                        Vector2 s = GetConsequenceCardSize(cons);
                        if (!vis.Overlaps(new Rect(cons.GraphPosX, cons.GraphPosY, s.x, s.y))) continue;
                        if (HitCardGrip(cons.GraphPosX, cons.GraphPosY, s, mouseWorld)) { ClaimLateResize(cons, mouseWorld, mouseScreen, evt); return; }
                        if (HitCardHeader(cons.GraphPosX, cons.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(cons, mouseWorld, mouseScreen, evt); return; }
                    }
                }
                if (node.Events != null)
                {
                    for (int e = node.Events.Count - 1; e >= 0; e--)
                    {
                        var evd = node.Events[e];
                        if (evd == null) continue;
                        Vector2 s = GetEventCardSize(evd);
                        if (!vis.Overlaps(new Rect(evd.GraphPosX, evd.GraphPosY, s.x, s.y))) continue;
                        if (HitCardGrip(evd.GraphPosX, evd.GraphPosY, s, mouseWorld)) { ClaimLateResize(evd, mouseWorld, mouseScreen, evt); return; }
                        if (HitCardHeader(evd.GraphPosX, evd.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(evd, mouseWorld, mouseScreen, evt); return; }
                    }
                }
                if (node.Dialogues != null)
                {
                    for (int d = node.Dialogues.Count - 1; d >= 0; d--)
                    {
                        var dlg = node.Dialogues[d];
                        if (dlg == null) continue;
                        Vector2 s = GetDialogueCardSize(dlg);
                        if (!vis.Overlaps(new Rect(dlg.GraphPosX, dlg.GraphPosY, s.x, s.y))) continue;
                        if (HitCardGrip(dlg.GraphPosX, dlg.GraphPosY, s, mouseWorld)) { ClaimLateResize(dlg, mouseWorld, mouseScreen, evt); return; }
                        if (HitCardHeader(dlg.GraphPosX, dlg.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(dlg, mouseWorld, mouseScreen, evt); return; }
                    }
                }
            }

            // Frames de nœuds (peintes en premier = tout en dessous). Miroir exact de
            // isOverHeaderDragZone dans DrawNodeFrame (boutons d'actions et port de
            // sortie exclus : un port reste un port, le bouton ✕ reste cliquable).
            for (int n = _data.Nodes.Count - 1; n >= 0; n--)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                Rect box = ComputeNodeBoundingBox(node);
                if (!vis.Overlaps(box)) continue;
                Rect header = new Rect(box.x, box.y, box.width, 38f);
                Rect actions = new Rect(header.xMax - 360f, header.y + 5f, 350f, 26f);
                Rect outSock = new Rect(box.xMax - 9f, box.y + 10f, 18f, 18f);
                if (header.Contains(mouseWorld) && !actions.Contains(mouseWorld) && !outSock.Contains(mouseWorld))
                {
                    ClaimLateDrag(node, mouseWorld, mouseScreen, evt);
                    return;
                }
            }
        }

        private static bool HitCardHeader(float gx, float gy, float gw, Vector2 mouseWorld)
        {
            // Miroir de dragHeaderRect : (x, y, W-26, 26). Le bouton ✕ (26px à droite)
            // reste cliquable, les ports du flanc droit restent des ports.
            return new Rect(gx, gy, gw - 26f, 26f).Contains(mouseWorld);
        }

        private static bool HitCardGrip(float gx, float gy, Vector2 size, Vector2 mouseWorld)
        {
            // Miroir de resizeGripRect : carré 14px en bas à droite.
            return new Rect(gx + size.x - 14f, gy + size.y - 14f, 14f, 14f).Contains(mouseWorld);
        }

        private void ClaimLateDrag(object target, Vector2 mouseWorld, Vector2 mouseScreen, Event evt)
        {
            _lateDragTarget = target;
            _lateResizeTarget = null;
            _lateDownWorld = mouseWorld;
            _lateDownScreen = mouseScreen;
            evt.Use();
        }

        private void ClaimLateResize(object target, Vector2 mouseWorld, Vector2 mouseScreen, Event evt)
        {
            _lateResizeTarget = target;
            _lateDragTarget = null;
            _lateDownWorld = mouseWorld;
            _lateDownScreen = mouseScreen;
            evt.Use();
        }
        private bool IsLateDragFor(object o)
        {
            if (!ReferenceEquals(_lateDragTarget, o)) return false;
            if (!string.IsNullOrEmpty(_draggingCardKey) || !string.IsNullOrEmpty(_draggingNodeId)) return false;
            var e = Event.current;
            if (e == null || (e.type != EventType.MouseDrag && e.type != EventType.MouseMove)) return false;
            return Vector2.Distance(_graphMouseScreenPos, _lateDownScreen) > 6f;
        }

        private bool IsLateResizeFor(object o)
        {
            if (!ReferenceEquals(_lateResizeTarget, o)) return false;
            if (!string.IsNullOrEmpty(_resizingCardKey) || !string.IsNullOrEmpty(_draggingCardKey)) return false;
            var e = Event.current;
            if (e == null || (e.type != EventType.MouseDrag && e.type != EventType.MouseMove)) return false;
            return Vector2.Distance(_graphMouseScreenPos, _lateDownScreen) > 6f;
        }
        private void DrawAudioToolbar()
        {
            _data.AudioSettings ??= new SceneAudioSettings();
            var settings = _data.AudioSettings;
            int sndCount = _data.Sounds != null ? _data.Sounds.Count : 0;
            int ambCount = _data.Ambiences != null ? _data.Ambiences.Count : 0;
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"<b>🔊 Audio</b> ({sndCount} 🔊 + {ambCount} 🌌)", GUILayout.Width(160));
            GUI.backgroundColor = _showBankBrowser ? new Color(0.35f, 0.7f, 1f) : Color.white;
            if (GUILayout.Button("🎵 Banque", GUILayout.Width(95), GUILayout.Height(20))) _showBankBrowser = !_showBankBrowser;
            GUI.backgroundColor = _showAudioAudit ? new Color(1f, 0.8f, 0.3f) : Color.white;
            string auditLabel = _auditDirty ? "🔍 Audit" : (_auditErrorCount > 0 ? $"🔍 Audit ({_auditErrorCount})" : "🔍 Audit ✔");
            if (GUILayout.Button(auditLabel, GUILayout.Width(105), GUILayout.Height(20))) { _showAudioAudit = !_showAudioAudit; if (_showAudioAudit) _auditDirty = true; }
            GUI.backgroundColor = _showAudioMonitor ? new Color(0.4f, 1f, 0.6f) : Color.white;
            if (GUILayout.Button("🎛 Monitor", GUILayout.Width(95), GUILayout.Height(20))) _showAudioMonitor = !_showAudioMonitor;
            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();
            bool newMute = GUILayout.Toggle(settings.Muted, "🔇 Muet", GUILayout.Width(70));
            if (newMute != settings.Muted) { settings.Muted = newMute; if (newMute) KilltimeAudioManager.Instance?.StopAllSceneSounds(0.1f); }
            GUILayout.Label("Master", GUILayout.Width(48));
            settings.MasterVolumeScale = GUILayout.HorizontalSlider(Mathf.Clamp(settings.MasterVolumeScale, 0f, 2f), 0f, 2f, GUILayout.Width(90));
            settings.MasterVolumeScale = Mathf.Clamp(settings.MasterVolumeScale, 0f, 2f);
            GUILayout.Label(settings.MasterVolumeScale.ToString("0.0"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
        }

        private void DrawBankBrowserPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>🎵 Banque sons/fx</b> <color=grey>(pré-écoute 2D — les réglages carte s'appliquent au runtime)</color>", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("■ Stop", GUILayout.Width(80), GUILayout.Height(20))) StopBankPreview();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Filtre :", GUILayout.Width(50));
            _bankSearch = GUILayout.TextField(_bankSearch ?? "", GUILayout.Width(200));
            GUILayout.Label("Vol test :", GUILayout.Width(60));
            _bankTestVolume = GUILayout.HorizontalSlider(Mathf.Clamp(_bankTestVolume, 0f, 2f), 0f, 2f, GUILayout.Width(100));
            _bankTestVolume = Mathf.Clamp(_bankTestVolume, 0f, 2f);
            GUILayout.Label(_bankTestVolume.ToString("0.0"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            var all = Killtime.Audio.SoundBankCatalog.GetAll();
            _bankScroll = GUILayout.BeginScrollView(_bankScroll, GUILayout.Height(140));
            if (all != null)
            {
                string f = (_bankSearch ?? "").Trim();
                for (int i = 0; i < all.Count; i++)
                {
                    var e = all[i];
                    if (e == null || string.IsNullOrWhiteSpace(e.id)) continue;
                    if (!string.IsNullOrEmpty(f)
                        && e.id.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                        && (e.label ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    GUILayout.BeginHorizontal();
                    bool playing = string.Equals(_bankPreviewCue, e.id, StringComparison.OrdinalIgnoreCase)
                        && _bankPreviewSrc != null && _bankPreviewSrc.isPlaying;
                    GUI.backgroundColor = playing ? new Color(0.2f, 0.75f, 0.4f) : Color.white;
                    if (GUILayout.Button(playing ? "■" : "▶", GUILayout.Width(30), GUILayout.Height(20)))
                    {
                        if (playing) StopBankPreview();
                        else PreviewBankEntry(e.id);
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.Label($"<b>{e.DisplayName()}</b> <color=grey>[{e.id}] {e.duration:0.0}s vol {e.volume:0.0}</color>", GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void PreviewBankEntry(string bankId)
        {
            StopBankPreview();
            if (string.IsNullOrWhiteSpace(bankId) || KilltimeAudioManager.Instance == null) return;
            var entry = Killtime.Audio.SoundBankCatalog.Find(bankId);
            if (entry == null) return;
            var clip = Killtime.Audio.ProceduralAudioFactory.GetBankClip(entry, entry.duration);
            if (clip == null) return;
            _bankPreviewSrc = KilltimeAudioManager.Instance.PlayBankUI(entry.id, clip, _bankTestVolume * entry.volume);
            _bankPreviewCue = entry.id;
        }

        private void StopBankPreview()
        {
            _bankPreviewCue = "";
            if (_bankPreviewSrc != null)
            {
                try { _bankPreviewSrc.Stop(); } catch { /* ignore */ }
                _bankPreviewSrc = null;
            }
        }

        private void DrawAudioAuditPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            string title = _auditDirty ? "<b>🔍 Audit audio</b> <color=grey>(à analyser)</color>"
                : $"<b>🔍 Audit audio</b> ({_auditErrorCount} erreur(s))";
            GUILayout.Label(title, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Analyser", GUILayout.Width(90), GUILayout.Height(20))) BuildAudioAudit();
            if (GUILayout.Button("Réparer auto", GUILayout.Width(110), GUILayout.Height(20))) RepairAudioLinks();
            GUILayout.EndHorizontal();
            _auditScroll = GUILayout.BeginScrollView(_auditScroll, GUILayout.Height(140));
            if (_auditDirty)
            {
                GUILayout.Label("<color=grey>Cliquez « Analyser » : liens morts, cues inconnus, orphelines, doublons, loops infinies, conflits legacy.</color>");
            }
            else if (_auditLines.Count == 0)
            {
                GUILayout.Label("<color=#7CFF9B><b>✔ Aucun problème détecté.</b></color>");
            }
            else
            {
                for (int i = 0; i < _auditLines.Count; i++)
                    GUILayout.Label(_auditLines[i]);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private bool AudioCardIdExists(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            return TryGetSoundSourceBottom(id, out _);
        }

        private static bool IsKnownSoundCue(string cue)
        {
            if (string.IsNullOrWhiteSpace(cue)) return false;
            if (Killtime.Audio.SoundBankCatalog.Find(cue) != null) return true;
            return System.Enum.TryParse<Killtime.Audio.SoundId>(cue.Trim(), out _);
        }

        private void BuildAudioAudit()
        {
            _auditLines.Clear();
            _auditErrorCount = 0;
            if (_data == null) { _auditDirty = false; return; }
            void Err(string s) { _auditLines.Add($"<color=#FF6B6B>✕ {s}</color>"); _auditErrorCount++; }
            void Warn(string s) { _auditLines.Add($"<color=#FFC94D>⚠ {s}</color>"); }
            void Info(string s) { _auditLines.Add($"<color=grey>• {s}</color>"); }

            var seenSnd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_data.Sounds != null)
            {
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) { Err($"Son #{i} : entrée nulle."); continue; }
                    string sid = string.IsNullOrWhiteSpace(snd.SoundId) ? $"#{i}" : snd.SoundId;
                    if (!string.IsNullOrWhiteSpace(snd.SoundId) && !seenSnd.Add(snd.SoundId))
                        Err($"[{sid}] doublon de SoundId (renommage auto conseillé).");
                    if (string.IsNullOrWhiteSpace(snd.SoundCueId))
                        Warn($"[{sid}] aucun son configuré (SoundCueId vide).");
                    else if (!IsKnownSoundCue(snd.SoundCueId))
                        Err($"[{sid}] SoundCueId inconnu : '{snd.SoundCueId}' (ni banque ni SoundId).");
                    if (!string.IsNullOrWhiteSpace(snd.SourceCardId) && !AudioCardIdExists(snd.SourceCardId))
                        Err($"[{sid}] lien mort : source '{snd.SourceCardId}' introuvable.");
                    if (!string.IsNullOrWhiteSpace(snd.StopSourceCardId) && !AudioCardIdExists(snd.StopSourceCardId))
                        Err($"[{sid}] lien d'arrêt mort : cible '{snd.StopSourceCardId}' introuvable.");
                    if (string.IsNullOrWhiteSpace(snd.SourceCardId))
                        Warn($"[{sid}] orpheline (← non liée) : ne jouera jamais.");
                    if (snd.Loop && snd.DurationSeconds < 0.05f && string.IsNullOrWhiteSpace(snd.StopSourceCardId)
                        && (_data.AudioSettings == null || (!_data.AudioSettings.StopAllSoundsOnNodeChange && !_data.AudioSettings.StopAllSoundsOnSceneExit)))
                        Err($"[{sid}] loop infini sans arrêt (ni ■→, ni kill-switch) : fuira.");
                }
            }
            var seenAmb = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_data.Ambiences != null)
            {
                for (int i = 0; i < _data.Ambiences.Count; i++)
                {
                    var amb = _data.Ambiences[i];
                    if (amb == null) { Err($"Ambiance #{i} : entrée nulle."); continue; }
                    string aid = string.IsNullOrWhiteSpace(amb.AmbienceId) ? $"#{i}" : amb.AmbienceId;
                    if (!string.IsNullOrWhiteSpace(amb.AmbienceId) && !seenAmb.Add(amb.AmbienceId))
                        Err($"[{aid}] doublon d'AmbienceId.");
                    if (Killtime.Audio.AmbienceCatalog.Find(amb.PresetId) == null)
                        Err($"[{aid}] preset inconnu : '{amb.PresetId}'.");
                    if (!string.IsNullOrWhiteSpace(amb.StartSourceCardId) && !AudioCardIdExists(amb.StartSourceCardId))
                        Err($"[{aid}] lien mort : entrée '{amb.StartSourceCardId}' introuvable.");
                    if (!string.IsNullOrWhiteSpace(amb.StopSourceCardId) && !AudioCardIdExists(amb.StopSourceCardId))
                        Err($"[{aid}] lien d'arrêt mort : cible '{amb.StopSourceCardId}' introuvable.");
                    if (string.IsNullOrWhiteSpace(amb.StartSourceCardId) && string.IsNullOrWhiteSpace(amb.StopSourceCardId))
                        Warn($"[{aid}] orpheline (ni entrée ni arrêt) : ne jouera jamais.");
                }
            }
            // Conflits legacy : cue legacy + carte 🔊 liée (ignoré si PreferCards).
            bool preferCards = _data.AudioSettings == null || _data.AudioSettings.PreferCardsOverLegacy;
            if (_data.Nodes != null)
            {
                for (int n = 0; n < _data.Nodes.Count; n++)
                {
                    var node = _data.Nodes[n];
                    if (node == null || node.Dialogues == null) continue;
                    for (int d = 0; d < node.Dialogues.Count; d++)
                    {
                        var line = node.Dialogues[d];
                        if (line == null || string.IsNullOrWhiteSpace(line.SoundCueId)) continue;
                        if (_data.HasLinkedSounds(line.LineId))
                            Info($"[{line.LineId}] cue legacy '{line.SoundCueId}' + carte 🔊 : {(preferCards ? "legacy ignoré (PreferCards)" : "DOUBLE JEU — les deux jouent")}.");
                        else if (!IsKnownSoundCue(line.SoundCueId))
                            Warn($"[{line.LineId}] cue legacy inconnu : '{line.SoundCueId}'.");
                    }
                }
            }
            _auditDirty = false;
        }

        private void RepairAudioLinks()
        {
            if (_data == null) return;
            // 1) Liens morts → vidés (démarrage + arrêts, sons + ambiances).
            if (_data.Sounds != null)
            {
                var usedSnd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) continue;
                    if (!string.IsNullOrWhiteSpace(snd.SourceCardId) && !AudioCardIdExists(snd.SourceCardId))
                        snd.SourceCardId = "";
                    if (!string.IsNullOrWhiteSpace(snd.StopSourceCardId) && !AudioCardIdExists(snd.StopSourceCardId))
                        snd.StopSourceCardId = "";
                    if (string.IsNullOrWhiteSpace(snd.SoundId)) snd.SoundId = $"snd_{i + 1}";
                    string baseId = snd.SoundId;
                    int suffix = 2;
                    while (!usedSnd.Add(snd.SoundId)) snd.SoundId = $"{baseId}_{suffix++}";
                }
            }
            if (_data.Ambiences != null)
            {
                var usedAmb = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _data.Ambiences.Count; i++)
                {
                    var amb = _data.Ambiences[i];
                    if (amb == null) continue;
                    if (!string.IsNullOrWhiteSpace(amb.StartSourceCardId) && !AudioCardIdExists(amb.StartSourceCardId))
                        amb.StartSourceCardId = "";
                    if (!string.IsNullOrWhiteSpace(amb.StopSourceCardId) && !AudioCardIdExists(amb.StopSourceCardId))
                        amb.StopSourceCardId = "";
                    if (string.IsNullOrWhiteSpace(amb.AmbienceId)) amb.AmbienceId = $"amb_{i + 1}";
                    string baseId = amb.AmbienceId;
                    int suffix = 2;
                    while (!usedAmb.Add(amb.AmbienceId)) amb.AmbienceId = $"{baseId}_{suffix++}";
                }
            }
            BuildAudioAudit();
        }

        private void DrawAudioMonitorPanel()
        {
            _data.AudioSettings ??= new SceneAudioSettings();
            var s = _data.AudioSettings;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>🎛 Monitor audio scène</b>", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("■ Stop sons", GUILayout.Width(100), GUILayout.Height(20)))
                KilltimeAudioManager.Instance?.StopAllSceneSounds(0.2f);
            if (GUILayout.Button("■ Stop amb", GUILayout.Width(100), GUILayout.Height(20)))
                KilltimeAudioManager.Instance?.StopAllSceneAmbiences(0.5f);
            if (GUILayout.Button("■ Stop tests", GUILayout.Width(100), GUILayout.Height(20)))
            {
                StopAllSceneSoundPreviews();
                StopBankPreview();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            s.StopAllSoundsOnNodeChange = GUILayout.Toggle(s.StopAllSoundsOnNodeChange, "Kill inter-nœuds", GUILayout.Width(130));
            s.StopAllSoundsOnSceneExit = GUILayout.Toggle(s.StopAllSoundsOnSceneExit, "Kill sortie scène", GUILayout.Width(130));
            s.PreferCardsOverLegacy = GUILayout.Toggle(s.PreferCardsOverLegacy, "Cartes > legacy", GUILayout.Width(130));
            GUILayout.EndHorizontal();
            _monitorScroll = GUILayout.BeginScrollView(_monitorScroll, GUILayout.Height(110));
            var mgr = KilltimeAudioManager.Instance;
            if (mgr == null)
            {
                GUILayout.Label("<color=grey>AudioManager absent (pas de runtime) : les sources suivies apparaîtront en jeu.</color>");
            }
            else
            {
                var sounds = mgr.GetActiveSceneSoundKeys();
                var ambs = mgr.GetActiveSceneAmbienceKeys();
                if (sounds.Count == 0 && ambs.Count == 0)
                    GUILayout.Label("<color=grey>Aucune source suivie active.</color>");
                for (int i = 0; i < sounds.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"🔊 <b>{sounds[i]}</b>", GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("■", GUILayout.Width(30), GUILayout.Height(18)))
                        mgr.StopSceneSound(sounds[i], 0.2f);
                    GUILayout.EndHorizontal();
                }
                for (int i = 0; i < ambs.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"🌌 <b>{ambs[i]}</b>", GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("■", GUILayout.Width(30), GUILayout.Height(18)))
                        mgr.StopSceneAmbience(ambs[i], 0.5f);
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void StopAllSceneSoundPreviews()
        {
            var keys = new List<string>(_soundPreviewSources.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                if (_soundPreviewSources.TryGetValue(keys[i], out var src) && src != null)
                {
                    try { src.Stop(); } catch { /* ignore */ }
                }
            }
            _soundPreviewSources.Clear();
        }
        private void DrawSoundCard(SceneSoundData snd, int index, Vector2 mouseWorld)
        {
            if (snd == null) return;
            if (string.IsNullOrEmpty(snd.SoundId)) snd.SoundId = $"snd_{index + 1}";

            Vector2 size = GetSoundCardSize(snd);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(snd.GraphPosX, snd.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"sound_{index}_{snd.SoundId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            bool lateResizeSnd = IsLateResizeFor(snd);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeSnd)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeSnd) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                snd.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 240f, 750f);
                snd.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 130f, 500f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            bool lateDragSnd = IsLateDragFor(snd);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragSnd)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragSnd ? _lateDownWorld : mouseWorld) - new Vector2(snd.GraphPosX, snd.GraphPosY);
                if (lateDragSnd) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                snd.GraphPosX = mouseWorld.x - _dragOffset.x;
                snd.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            DrawGraphSolidRect(cardRect, new Color(0.14f, 0.09f, 0.03f, 0.95f));
            DrawGraphSolidRect(headerRect, new Color(0.55f, 0.35f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(1f, 0.75f, 0.25f, 0.8f));

            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>🔊 {snd.Title}</b> [{snd.SoundId}]");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                StopSceneSoundPreview(snd);
                if (_data.Sounds != null) _data.Sounds.RemoveAt(index);
                if (!string.IsNullOrWhiteSpace(snd.SoundId))
                {
                    _cachedSoundInputSockets.Remove(snd.SoundId);
                    _cachedSoundStopSockets.Remove(snd.SoundId);
                }
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            if (_zoom < CardLodThreshold)
            {
                DrawGraphSolidRect(new Rect(cardRect.x + 8f, cardRect.y + 32f, (cardW - 16f) * 0.8f, 8f), new Color(1f, 0.6f, 0.1f, 0.14f));
            }
            else
            {
                float innerX = cardRect.x + 8f;
                float innerW = cardW - 16f;
                float y = cardRect.y + 30f;

                GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
                snd.SoundId = GraphTextField(new Rect(innerX + 26f, y, 100f, 18f), snd.SoundId ?? "");
                snd.Muted = GraphToggle(new Rect(innerX + 132f, y, Mathf.Max(60f, innerW - 132f), 18f), snd.Muted, "🔇 Muet");
                y += 20f;

                GraphLabel(new Rect(innerX, y, 34f, 18f), "Titre:");
                snd.Title = GraphTextField(new Rect(innerX + 36f, y, Mathf.Max(40f, innerW - 36f), 18f), snd.Title ?? "");
                y += 20f;

                GraphLabel(new Rect(innerX, y, 36f, 18f), "Son :");
                snd.SoundCueId = GraphTextField(new Rect(innerX + 38f, y, Mathf.Max(40f, innerW - 38f - 68f), 18f), snd.SoundCueId ?? "");
                // Sélecteur banque sons/fx 🔊 : parcourt les presets (id + label).
                var bankList = Killtime.Audio.SoundBankCatalog.GetAll();
                if (bankList != null && bankList.Count > 0)
                {
                    int curBank = -1;
                    for (int bi = 0; bi < bankList.Count; bi++)
                    {
                        var be = bankList[bi];
                        if (be == null || string.IsNullOrWhiteSpace(be.id)) continue;
                        if (string.Equals(be.id.Trim(), (snd.SoundCueId ?? "").Trim(), System.StringComparison.OrdinalIgnoreCase)) { curBank = bi; break; }
                    }
                    if (GraphButton(new Rect(innerX + innerW - 64f, y, 20f, 18f), "♪"))
                    {
                        PreviewSceneSound(snd);
                    }
                    if (GraphButton(new Rect(innerX + innerW - 42f, y, 20f, 18f), "◀"))
                    {
                        curBank = curBank < 0 ? bankList.Count - 1 : (curBank - 1 + bankList.Count) % bankList.Count;
                        snd.SoundCueId = bankList[curBank] != null ? bankList[curBank].id : snd.SoundCueId;
                    }
                    if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                    {
                        curBank = curBank < 0 ? 0 : (curBank + 1) % bankList.Count;
                        snd.SoundCueId = bankList[curBank] != null ? bankList[curBank].id : snd.SoundCueId;
                    }
                }
                y += 20f;

                GraphLabel(new Rect(innerX, y, 42f, 18f), "Durée:");
                string durTxt = GraphTextField(new Rect(innerX + 44f, y, 40f, 18f), snd.DurationSeconds > 0.001f ? snd.DurationSeconds.ToString("0.0") : "0");
                if (float.TryParse(durTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedDur))
                    snd.DurationSeconds = Mathf.Clamp(parsedDur, 0f, 60f);

                GraphLabel(new Rect(innerX + 90f, y, 22f, 18f), "+d:");
                string delTxt = GraphTextField(new Rect(innerX + 114f, y, 36f, 18f), snd.DelaySeconds.ToString("0.0"));
                if (float.TryParse(delTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedDel))
                    snd.DelaySeconds = Mathf.Clamp(parsedDel, 0f, 60f);

                GraphLabel(new Rect(innerX + 156f, y, 28f, 18f), "Vol:");
                if (snd.VolumeScale <= 0.001f && !snd.Muted) snd.VolumeScale = 1.0f;
                string volTxt = GraphTextField(new Rect(innerX + 186f, y, 38f, 18f), snd.VolumeScale.ToString("0.0"));
                if (float.TryParse(volTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedVol))
                    snd.VolumeScale = Mathf.Clamp(parsedVol, 0f, 2f);
                y += 20f;

                // Garde enum (JSON édité à la main) : GraphToolbar planterait sinon.
                int trigIdx = System.Enum.IsDefined(typeof(SoundTriggerPoint), snd.TriggerPoint) ? (int)snd.TriggerPoint : 0;
                snd.TriggerPoint = (SoundTriggerPoint)GraphToolbar(new Rect(innerX, y, innerW, 18f), trigIdx, new[] { "▶ Début carte", "■ Fin carte" });
                y += 22f;

                GraphLabel(new Rect(innerX, y, 26f, 18f), "Pit:");
                string pitTxt = GraphTextField(new Rect(innerX + 28f, y, 38f, 18f), snd.PitchScale.ToString("0.00"));
                if (float.TryParse(pitTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedPit))
                    snd.PitchScale = Mathf.Clamp(parsedPit, 0.25f, 3f);
                snd.Loop = GraphToggle(new Rect(innerX + 72f, y, 66f, 18f), snd.Loop, "Loop ∞");
                GraphLabel(new Rect(innerX + 142f, y, 36f, 18f), "Prio:");
                string priTxt = GraphTextField(new Rect(innerX + 180f, y, 36f, 18f), snd.Priority.ToString());
                if (int.TryParse(priTxt, out int parsedPri))
                    snd.Priority = Mathf.Clamp(parsedPri, 0, 255);
                GraphLabel(new Rect(innerX + 220f, y, 24f, 18f), "Cd:");
                string cdTxt = GraphTextField(new Rect(innerX + 246f, y, Mathf.Max(30f, innerW - 246f), 18f), snd.CooldownSeconds.ToString("0.0"));
                if (float.TryParse(cdTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedCd))
                    snd.CooldownSeconds = Mathf.Clamp(parsedCd, 0f, 30f);
                y += 20f;

                GraphLabel(new Rect(innerX, y, 30f, 18f), "FIn:");
                string finTxt = GraphTextField(new Rect(innerX + 32f, y, 34f, 18f), snd.FadeInDuration.ToString("0.0"));
                if (float.TryParse(finTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedFIn))
                    snd.FadeInDuration = Mathf.Clamp(parsedFIn, 0f, 20f);
                GraphLabel(new Rect(innerX + 72f, y, 38f, 18f), "FOut:");
                string foutTxt = GraphTextField(new Rect(innerX + 112f, y, 34f, 18f), snd.FadeOutDuration.ToString("0.0"));
                if (float.TryParse(foutTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedFOut))
                    snd.FadeOutDuration = Mathf.Clamp(parsedFOut, 0f, 20f);
                snd.Spatialized = GraphToggle(new Rect(innerX + 152f, y, Mathf.Max(60f, innerW - 152f), 18f), snd.Spatialized, "Spatial 3D");
                y += 20f;

                if (snd.Spatialized)
                {
                    GraphLabel(new Rect(innerX, y, 16f, 18f), "Q:");
                    int.TryParse(GraphTextField(new Rect(innerX + 18f, y, 32f, 18f), snd.Q.ToString()), out snd.Q);
                    GraphLabel(new Rect(innerX + 54f, y, 16f, 18f), "R:");
                    int.TryParse(GraphTextField(new Rect(innerX + 72f, y, 32f, 18f), snd.R.ToString()), out snd.R);
                    GraphLabel(new Rect(innerX + 108f, y, 44f, 18f), "Rayon:");
                    string rTxt = GraphTextField(new Rect(innerX + 154f, y, 38f, 18f), snd.Radius.ToString("0.#"));
                    if (float.TryParse(rTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pRad))
                        snd.Radius = Mathf.Clamp(pRad, 1f, 120f);
                    y += 20f;
                }

                bool linked = !string.IsNullOrWhiteSpace(snd.SourceCardId);
                GUI.color = linked ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                GraphLabel(new Rect(innerX, y, Mathf.Max(40f, innerW - 26f), 18f),
                    linked ? $"← {snd.SourceCardId}" : "(drag carte ↓ → ici)");
                GUI.color = Color.white;
                if (linked && GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "⊘")) snd.SourceCardId = "";
                y += 20f;

                bool hasStop = !string.IsNullOrWhiteSpace(snd.StopSourceCardId);
                GUI.color = hasStop ? new Color(1f, 0.6f, 0.6f) : new Color(1f, 1f, 1f, 0.45f);
                GraphLabel(new Rect(innerX, y, Mathf.Max(40f, innerW - 26f), 18f),
                    hasStop ? $"■ Stop → {snd.StopSourceCardId}" : "(drag sortie bas → bas cible)");
                GUI.color = Color.white;
                if (hasStop && GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "⊘")) snd.StopSourceCardId = "";
                y += 22f;

                float halfBtn = (innerW - 6f) * 0.5f;
                GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);
                if (GraphButton(new Rect(innerX, y, halfBtn, 20f), "▶ Tester"))
                {
                    PreviewSceneSound(snd);
                }
                GUI.backgroundColor = new Color(0.85f, 0.35f, 0.35f);
                if (GraphButton(new Rect(innerX + halfBtn + 6f, y, halfBtn, 20f), "■ Stopper"))
                {
                    StopSceneSoundPreview(snd);
                }
                GUI.backgroundColor = Color.white;
            }

            // Port d'entrée (haut) : reçoit le fil du bas des cartes sources.
            Vector2 inCenter = GetSoundInCenter(snd);
            _cachedSoundInputSockets[snd.SoundId] = inCenter;
            DrawGraphSolidRect(new Rect(inCenter.x - 7f, inCenter.y - 7f, 14f, 14f),
                !string.IsNullOrWhiteSpace(snd.SourceCardId) ? SoundPortCol : new Color(SoundPortCol.r, SoundPortCol.g, SoundPortCol.b, 0.35f));

            // Port de sortie d'ARRÊT (bas) : tire un fil vers le bas d'une carte
            // cible pour couper le son (loops, longues durées). Miroir ambiance.
            Vector2 stopCenter = GetSoundStopCenter(snd);
            _cachedSoundStopSockets[snd.SoundId] = stopCenter;
            Rect sndStopPort = new Rect(stopCenter.x - 7f, stopCenter.y - 7f, 14f, 14f);
            bool sndHasStop = !string.IsNullOrWhiteSpace(snd.StopSourceCardId);
            DrawGraphSolidRect(sndStopPort,
                sndHasStop ? SoundStopPortCol : new Color(SoundStopPortCol.r, SoundStopPortCol.g, SoundStopPortCol.b, 0.35f));

            if (GraphPrimaryDown(mouseWorld, sndStopPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsSoundStopLink = true,
                    SourceSoundStop = snd,
                    StartPos = stopCenter
                };
                evt.Use();
            }
        }

        /// <summary>
        /// Pré-écoute d'une carte son dans l'éditeur (2D, volume × master scène,
        /// pitch/loop/fades de la carte). Écrase la pré-écoute précédente de la carte.
        /// </summary>
        private void PreviewSceneSound(SceneSoundData snd)
        {
            if (snd == null || KilltimeAudioManager.Instance == null) return;
            StopSceneSoundPreview(snd);
            string cue = (snd.SoundCueId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cue)) return;
            float master = _data != null && _data.AudioSettings != null ? Mathf.Clamp(_data.AudioSettings.MasterVolumeScale, 0f, 2f) : 1f;
            float vol = Mathf.Clamp(snd.VolumeScale, 0f, 2f) * master;
            AudioSource src = null;
            var bankEntry = Killtime.Audio.SoundBankCatalog.Find(cue);
            if (bankEntry != null)
            {
                var clip = Killtime.Audio.ProceduralAudioFactory.GetBankClip(bankEntry, bankEntry.duration);
                if (clip != null)
                    src = KilltimeAudioManager.Instance.PlayBankSound(bankEntry.id, clip, vol * bankEntry.volume,
                        Mathf.Clamp(snd.PitchScale <= 0f ? 1f : snd.PitchScale, 0.25f, 3f),
                        snd.Loop, Mathf.Max(0f, snd.FadeInDuration), Mathf.Clamp(snd.Priority, 0, 255),
                        false, Vector3.zero, 15f, null);
            }
            else if (System.Enum.TryParse<Killtime.Audio.SoundId>(cue, out var soundEnum))
            {
                src = KilltimeAudioManager.Instance.PlayUI(soundEnum, vol);
            }
            else
            {
                CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[ÉDITEUR] SoundCueId inconnu : '{cue}'.</color>", LogCategory.MovementAndTurns, "[ÉDITEUR]", Color.gray);
            }
            if (src != null && !string.IsNullOrWhiteSpace(snd.SoundId))
                _soundPreviewSources[snd.SoundId] = src;
        }

        /// <summary>Stoppe la pré-écoute d'une carte son (éditeur).</summary>
        private void StopSceneSoundPreview(SceneSoundData snd)
        {
            if (snd == null || string.IsNullOrWhiteSpace(snd.SoundId)) return;
            if (_soundPreviewSources.TryGetValue(snd.SoundId, out var src))
            {
                _soundPreviewSources.Remove(snd.SoundId);
                if (src != null)
                {
                    try
                    {
                        if (Mathf.Approximately(snd.FadeOutDuration, 0f)) src.Stop();
                        else StartCoroutine(FadePreviewSourceRoutine(src, Mathf.Max(0f, snd.FadeOutDuration)));
                    }
                    catch { /* ignore */ }
                }
            }
        }

        private static System.Collections.IEnumerator FadePreviewSourceRoutine(AudioSource src, float duration)
        {
            if (src == null) yield break;
            float startVol = src.volume;
            float elapsed = 0f;
            duration = Mathf.Max(0.05f, duration);
            while (elapsed < duration && src != null && src.isPlaying)
            {
                elapsed += Time.unscaledDeltaTime;
                src.volume = Mathf.Lerp(startVol, 0f, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            if (src != null)
            {
                try { src.Stop(); } catch { /* ignore */ }
                src.volume = startVol;
            }
        }

        private void DrawAmbienceCard(SceneAmbienceTrackData amb, int index, Vector2 mouseWorld)
        {
            if (amb == null) return;
            if (string.IsNullOrEmpty(amb.AmbienceId)) amb.AmbienceId = $"amb_{index + 1}";

            Vector2 size = GetAmbienceCardSize(amb);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(amb.GraphPosX, amb.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"amb_{index}_{amb.AmbienceId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            bool lateResizeAmb = IsLateResizeFor(amb);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeAmb)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeAmb) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                amb.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 260f, 750f);
                amb.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 160f, 500f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            bool lateDragAmb = IsLateDragFor(amb);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragAmb)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragAmb ? _lateDownWorld : mouseWorld) - new Vector2(amb.GraphPosX, amb.GraphPosY);
                if (lateDragAmb) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                amb.GraphPosX = mouseWorld.x - _dragOffset.x;
                amb.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            DrawGraphSolidRect(cardRect, new Color(0.04f, 0.12f, 0.11f, 0.95f));
            DrawGraphSolidRect(headerRect, new Color(0.08f, 0.45f, 0.38f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1.5f), AmbiencePortCol);

            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>🌌 {amb.Title}</b> [{amb.AmbienceId}]");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                string doomed = amb.AmbienceId;
                if (_data.Ambiences != null) _data.Ambiences.RemoveAt(index);
                PurgeAmbienceReferences(doomed);
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float y = cardRect.y + 30f;

            GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
            amb.AmbienceId = GraphTextField(new Rect(innerX + 26f, y, 90f, 18f), amb.AmbienceId ?? "");
            GraphLabel(new Rect(innerX + 120f, y, 36f, 18f), "Titre:");
            amb.Title = GraphTextField(new Rect(innerX + 158f, y, Mathf.Max(40f, innerW - 158f), 18f), amb.Title ?? "");
            y += 20f;

            var presets = AmbienceCatalog.GetAll();
            int currentIdx = presets.FindIndex(p => string.Equals(p.id, amb.PresetId, StringComparison.OrdinalIgnoreCase));
            if (currentIdx < 0 && presets.Count > 0) currentIdx = 0;

            GraphLabel(new Rect(innerX, y, 50f, 18f), "Preset :");
            if (GraphButton(new Rect(innerX + 52f, y, 20f, 18f), "◀"))
            {
                currentIdx = (currentIdx - 1 + presets.Count) % presets.Count;
                amb.PresetId = presets[currentIdx].id;
                amb.VolumeScale = presets[currentIdx].defaultVolume;
                amb.Spatialized = presets[currentIdx].spatial;
                amb.Radius = presets[currentIdx].defaultRadius;
            }
            string curName = currentIdx >= 0 && currentIdx < presets.Count ? presets[currentIdx].displayName : (amb.PresetId ?? "(aucun)");
            GraphLabel(new Rect(innerX + 74f, y, Mathf.Max(60f, innerW - 74f - 24f), 18f), $"<b>{curName}</b>");
            if (GraphButton(new Rect(innerX + innerW - 22f, y, 20f, 18f), "▶"))
            {
                currentIdx = (currentIdx + 1) % presets.Count;
                amb.PresetId = presets[currentIdx].id;
                amb.VolumeScale = presets[currentIdx].defaultVolume;
                amb.Spatialized = presets[currentIdx].spatial;
                amb.Radius = presets[currentIdx].defaultRadius;
            }
            y += 22f;

            GraphLabel(new Rect(innerX, y, 32f, 18f), "Vol:");
            string volTxt = GraphTextField(new Rect(innerX + 34f, y, 38f, 18f), amb.VolumeScale.ToString("0.00"));
            if (float.TryParse(volTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pVol))
                amb.VolumeScale = Mathf.Clamp(pVol, 0f, 2f);

            GraphLabel(new Rect(innerX + 80f, y, 42f, 18f), "FadeIn:");
            string inTxt = GraphTextField(new Rect(innerX + 124f, y, 34f, 18f), amb.FadeInDuration.ToString("0.0"));
            if (float.TryParse(inTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pIn))
                amb.FadeInDuration = Mathf.Clamp(pIn, 0f, 20f);

            GraphLabel(new Rect(innerX + 166f, y, 48f, 18f), "FadeOut:");
            string outTxt = GraphTextField(new Rect(innerX + 216f, y, 34f, 18f), amb.FadeOutDuration.ToString("0.0"));
            if (float.TryParse(outTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pOut))
                amb.FadeOutDuration = Mathf.Clamp(pOut, 0f, 20f);
            y += 22f;

            amb.Spatialized = GraphToggle(new Rect(innerX, y, 95f, 18f), amb.Spatialized, "Spatial 3D");
            if (amb.Spatialized)
            {
                GraphLabel(new Rect(innerX + 100f, y, 16f, 18f), "Q:");
                int.TryParse(GraphTextField(new Rect(innerX + 118f, y, 30f, 18f), amb.Q.ToString()), out amb.Q);
                GraphLabel(new Rect(innerX + 152f, y, 16f, 18f), "R:");
                int.TryParse(GraphTextField(new Rect(innerX + 170f, y, 30f, 18f), amb.R.ToString()), out amb.R);
                GraphLabel(new Rect(innerX + 204f, y, 42f, 18f), "Rayon:");
                string rTxt = GraphTextField(new Rect(innerX + 248f, y, 36f, 18f), amb.Radius.ToString("0.#"));
                if (float.TryParse(rTxt.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pRad))
                    amb.Radius = Mathf.Clamp(pRad, 1f, 120f);
            }
            y += 22f;

            bool hasStart = !string.IsNullOrWhiteSpace(amb.StartSourceCardId);
            GUI.color = hasStart ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            GraphLabel(new Rect(innerX, y, Mathf.Max(40f, innerW - 26f), 18f),
                hasStart ? $"▶ In: ← {amb.StartSourceCardId}" : "(drag bas carte → entrée haut)");
            GUI.color = Color.white;
            if (hasStart && GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "⊘")) amb.StartSourceCardId = "";
            y += 20f;

            bool hasStop = !string.IsNullOrWhiteSpace(amb.StopSourceCardId);
            GUI.color = hasStop ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            GraphLabel(new Rect(innerX, y, Mathf.Max(40f, innerW - 26f), 18f),
                hasStop ? $"■ Out: ➔ {amb.StopSourceCardId}" : "(drag sortie bas → bas carte cible)");
            GUI.color = Color.white;
            if (hasStop && GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "⊘")) amb.StopSourceCardId = "";
            y += 22f;

            float halfBtn = (innerW - 6f) * 0.5f;
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.65f);
            if (GraphButton(new Rect(innerX, y, halfBtn, 20f), "▶ Tester"))
            {
                // Test fidèle : spatialisation + position hex réelles quand dispo
                // (repli 2D si aucune grille chargée ou hex introuvable : un test
                // spatial vers une position invalide serait un silence trompeur).
                Vector3 ambTestPos = Vector3.zero;
                bool ambTestSpatial = amb.Spatialized;
                if (ambTestSpatial)
                {
                    try
                    {
                        var testGrid = FindAnyObjectByType<TacticalHexGrid>();
                        var testNode = testGrid != null ? testGrid.GetNode(new HexCoordinates(amb.Q, amb.R)) : null;
                        if (testNode != null) ambTestPos = testNode.WorldPosition;
                        else ambTestSpatial = false;
                    }
                    catch { ambTestSpatial = false; }
                }
                KilltimeAudioManager.Instance?.PlaySceneAmbience(amb.AmbienceId, amb.PresetId, amb.VolumeScale, amb.FadeInDuration, ambTestSpatial, ambTestPos, amb.Radius);
            }
            GUI.backgroundColor = new Color(0.85f, 0.35f, 0.35f);
            if (GraphButton(new Rect(innerX + halfBtn + 6f, y, halfBtn, 20f), "■ Stopper"))
            {
                KilltimeAudioManager.Instance?.StopSceneAmbience(amb.AmbienceId, amb.FadeOutDuration);
            }
            GUI.backgroundColor = Color.white;

            // Port d'entrée (haut) : reçoit la connexion de départ (fil du bas d'une carte source).
            Vector2 inCenter = GetAmbienceInCenter(amb);
            _cachedAmbienceInputSockets[amb.AmbienceId] = inCenter;
            DrawGraphSolidRect(new Rect(inCenter.x - 7f, inCenter.y - 7f, 14f, 14f),
                hasStart ? AmbiencePortCol : new Color(AmbiencePortCol.r, AmbiencePortCol.g, AmbiencePortCol.b, 0.35f));

            // Port de sortie (bas) : tire un fil vers le bas d'une carte cible pour arrêter l'ambiance.
            Vector2 stopCenter = GetAmbienceStopCenter(amb);
            _cachedAmbienceStopSockets[amb.AmbienceId] = stopCenter;
            Rect stopPort = new Rect(stopCenter.x - 7f, stopCenter.y - 7f, 14f, 14f);
            Color stopCol = hasStop ? new Color(0.95f, 0.35f, 0.55f) : new Color(0.95f, 0.35f, 0.55f, 0.35f);
            DrawGraphSolidRect(stopPort, stopCol);

            if (GraphPrimaryDown(mouseWorld, stopPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsAmbienceStopLink = true,
                    SourceAmbienceTrack = amb,
                    StartPos = stopCenter
                };
                evt.Use();
            }
        }
    }
}
