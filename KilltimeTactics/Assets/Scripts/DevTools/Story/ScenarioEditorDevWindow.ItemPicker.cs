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
    }
}
