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
        private void CountActorReferences(SceneActorSpawnData a, out int lines, out int events, out int triggers)
        {
            lines = 0; events = 0; triggers = 0;
            if (a == null || _data == null) return;
            bool Match(string v)
            {
                if (string.IsNullOrWhiteSpace(v)) return false;
                return string.Equals(v.Trim(), a.ActorId?.Trim(), System.StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(a.DisplayName) && string.Equals(v.Trim(), a.DisplayName.Trim(), System.StringComparison.OrdinalIgnoreCase));
            }
            if (_data.Nodes != null)
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
                            if (Match(line.SpeakerId)) lines++;
                        }
                    }
                    if (node.Consequences != null)
                    {
                        for (int k = 0; k < node.Consequences.Count; k++)
                        {
                            var cons = node.Consequences[k];
                            if (cons == null) continue;
                            if (cons.HasDialogue && Match(cons.SpeakerId)) lines++;
                        }
                    }
                    if (node.Events != null)
                    {
                        for (int e = 0; e < node.Events.Count; e++)
                        {
                            var ev = node.Events[e];
                            if (ev == null) continue;
                            if (Match(ev.TargetActorId)) events++;
                        }
                    }
                }
            }
            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    if (trg.Kind == SceneTriggerKind.ActorCondition && Match(trg.ActorId)) triggers++;
                }
            }
        }
        private void DrawActorCard(SceneActorSpawnData a, int index, Vector2 mouseWorld)
        {
            if (a == null) return;
            if (string.IsNullOrEmpty(a.ActorId)) a.ActorId = MakeUniqueActorId();
            string spawnDisplay = a.SpawnInitially
                ? "● INIT"
                : (string.IsNullOrEmpty(a.SpawnOnNodeId) ? "○ (jamais spawné)" : $"○ si {a.SpawnOnNodeId}");

            Vector2 size = GetActorCardSize(a);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(a.GraphPosX, a.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"actor_{index}_{a.ActorId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            bool lateResizeActor = IsLateResizeFor(a);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeActor)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeActor) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                a.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 300f, 750f);
                a.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 220f, 500f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            bool lateDragActor = IsLateDragFor(a);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragActor)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragActor ? _lateDownWorld : mouseWorld) - new Vector2(a.GraphPosX, a.GraphPosY);
                if (lateDragActor) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                a.GraphPosX = mouseWorld.x - _dragOffset.x;
                a.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            Color factionCol = a.IsPlayer ? new Color(0.0f, 0.85f, 1.0f) : new Color(1.0f, 0.4f, 0.4f);
            Color headerCol = a.IsPlayer ? new Color(0.10f, 0.32f, 0.55f) : new Color(0.45f, 0.15f, 0.15f);
            DrawGraphSolidRect(cardRect, new Color(0.06f, 0.08f, 0.12f, 0.95f));
            DrawGraphSolidRect(headerRect, headerCol);

            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(factionCol.r, factionCol.g, factionCol.b, 0.8f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            // Port d'entrée visuel (spawn) : pas de drag, la cible se règle via "Si nœud".
            DrawGraphSolidRect(new Rect(cardRect.x - 7f, cardRect.y + 19f, 14f, 14f), factionCol);

            string headerName = string.IsNullOrEmpty(a.DisplayName) ? "(sans nom)" : a.DisplayName;
            string aCineBadge = (a.CinematicIds != null && a.CinematicIds.Count > 0) ? $" 🎬×{a.CinematicIds.Count}" : "";
            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>👥 {headerName}</b> [{a.ActorId}]{aCineBadge}");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                string doomedActorId = a != null ? a.ActorId : "";
                if (_data.Actors != null) _data.Actors.RemoveAt(index);
                PurgeSoundReferences(doomedActorId);
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

            CountActorReferences(a, out int refLines, out int refEvents, out int refTriggers);
            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"👥 {headerName}  ({a.ActorId})";
                string talkInfo = "(aucune sortie 💬)";
                if (a.TalkEntries != null && a.TalkEntries.Count > 0)
                {
                    var bits = new System.Collections.Generic.List<string>();
                    for (int bti = 0; bti < a.TalkEntries.Count; bti++)
                    {
                        var bte = a.TalkEntries[bti];
                        if (bte == null || string.IsNullOrWhiteSpace(bte.TargetId)) continue;
                        string who = string.IsNullOrWhiteSpace(bte.SpeakerId) ? "*" : bte.SpeakerId.Trim();
                        bits.Add($"{who}➔{bte.TargetId.Trim()}");
                    }
                    if (bits.Count > 0) talkInfo = "💬 " + string.Join(", ", bits);
                }
                _hoverBody = $"{a.GetSummary()}\nFiche : {(string.IsNullOrEmpty(a.CharacterSheetFileName) ? "(Défaut)" : a.CharacterSheetFileName)}\nRépliques : {refLines} · Cibles : {refEvents} · Triggers : {refTriggers}\nParler à : {talkInfo}";
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;

            if (_zoom < CardLodThreshold)
            {
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 32f, innerW * 0.7f, 8f), new Color(factionCol.r, factionCol.g, factionCol.b, 0.18f));
                if (a.TalkEntries != null)
                {
                    for (int ti = 0; ti < a.TalkEntries.Count; ti++)
                    {
                        var te = a.TalkEntries[ti];
                        if (te == null) continue;
                        Vector2 talkCenter = GetActorTalkOutCenter(a, ti);
                        bool hasTalk = !string.IsNullOrWhiteSpace(te.TargetId);
                        Color col = hasTalk ? TalkPortCol : new Color(TalkPortCol.r, TalkPortCol.g, TalkPortCol.b, 0.35f);
                        Rect talkPort = new Rect(talkCenter.x - 7f, talkCenter.y - 7f, 14f, 14f);
                        DrawGraphSolidRect(talkPort, col);
                        if (GraphPrimaryDown(mouseWorld, talkPort))
                        {
                            _wireDraft = new WireConnectionDraft
                            {
                                IsActive = true,
                                IsActorTalk = true,
                                SourceActor = a,
                                SourceTalkIndex = ti,
                                StartPos = talkCenter
                            };
                            evt.Use();
                        }
                    }
                }
            }
            else
            {
                float y = cardRect.y + 30f;

                GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
                a.ActorId = GraphTextField(new Rect(innerX + 26f, y, 100f, 18f), a.ActorId ?? "");
                GUI.color = a.SpawnInitially ? new Color(0.4f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.75f);
                GraphLabel(new Rect(innerX + 130f, y, Mathf.Max(40f, innerW - 130f), 18f), spawnDisplay);
                GUI.color = Color.white;
                y += 20f;

                GraphLabel(new Rect(innerX, y, 36f, 18f), "Nom:");
                a.DisplayName = GraphTextField(new Rect(innerX + 38f, y, Mathf.Max(40f, innerW - 38f), 18f), a.DisplayName ?? "");
                y += 20f;

                // Faction + position hex + orientation sur une ligne compacte.
                a.IsPlayer = GraphToggle(new Rect(innerX, y, 52f, 18f), a.IsPlayer, "PJ");
                GraphLabel(new Rect(innerX + 54f, y, 16f, 18f), "Q:");
                int.TryParse(GraphTextField(new Rect(innerX + 70f, y, 36f, 18f), a.Q.ToString()), out a.Q);
                GraphLabel(new Rect(innerX + 110f, y, 14f, 18f), "R:");
                int.TryParse(GraphTextField(new Rect(innerX + 124f, y, 36f, 18f), a.R.ToString()), out a.R);
                GraphLabel(new Rect(innerX + 164f, y, 30f, 18f), "Ori:");
                float.TryParse(GraphTextField(new Rect(innerX + 196f, y, 40f, 18f), a.FacingAngle.ToString("0")), out a.FacingAngle);
                y += 20f;

                // Spawn : initiale ou différé sur nœud (avec cycleur de nœuds).
                a.SpawnInitially = GraphToggle(new Rect(innerX, y, 78f, 18f), a.SpawnInitially, "Initiale");
                GraphLabel(new Rect(innerX + 80f, y, 58f, 18f), "Si nœud:");
                {
                    bool hasNodes = _data.Nodes != null && _data.Nodes.Count > 0;
                    float arrowsW = hasNodes ? 44f : 0f;
                    float fldW = Mathf.Max(40f, innerW - 80f - 58f - arrowsW);
                    a.SpawnOnNodeId = GraphTextField(new Rect(innerX + 140f, y, fldW, 18f), a.SpawnOnNodeId ?? "");
                    if (hasNodes)
                    {
                        int curIdx = _data.Nodes.FindIndex(n => n != null && string.Equals(n.NodeId, a.SpawnOnNodeId, System.StringComparison.OrdinalIgnoreCase));
                        if (GraphButton(new Rect(innerX + innerW - 42f, y, 20f, 18f), "◀"))
                        {
                            curIdx = (curIdx < 0 ? _data.Nodes.Count - 1 : (curIdx - 1 + _data.Nodes.Count) % _data.Nodes.Count);
                            a.SpawnOnNodeId = _data.Nodes[curIdx] != null ? _data.Nodes[curIdx].NodeId : "";
                        }
                        if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                        {
                            curIdx = (curIdx < 0 ? 0 : (curIdx + 1) % _data.Nodes.Count);
                            a.SpawnOnNodeId = _data.Nodes[curIdx] != null ? _data.Nodes[curIdx].NodeId : "";
                        }
                    }
                }
                y += 20f;

                // Fiche personnage (cycleur catalogue des fiches disque + bouton fiche complète).
                GraphLabel(new Rect(innerX, y, 42f, 18f), "Fiche:");
                {
                    int charIdx = Mathf.Max(0, _availableCharacterFiles.IndexOf(a.CharacterSheetFileName));
                    if (GraphButton(new Rect(innerX + 44f, y, 20f, 18f), "◀"))
                    {
                        charIdx = (charIdx - 1 + _availableCharacterFiles.Count) % _availableCharacterFiles.Count;
                        a.CharacterSheetFileName = charIdx == 0 ? "" : _availableCharacterFiles[charIdx];
                    }
                    string charDisplay = string.IsNullOrEmpty(a.CharacterSheetFileName) ? "(Défaut)" : a.CharacterSheetFileName;
                    GraphLabel(new Rect(innerX + 66f, y, Mathf.Max(40f, innerW - 66f - 22f - 32f), 18f), $"<b>{charDisplay}</b>");
                    GUI.backgroundColor = new Color(0.2f, 0.6f, 1.0f);
                    if (GraphButton(new Rect(innerX + innerW - 20f - 30f, y, 28f, 18f), "📜"))
                    {
                        OpenActorSheet(a);
                    }
                    GUI.backgroundColor = Color.white;
                    if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                    {
                        charIdx = (charIdx + 1) % _availableCharacterFiles.Count;
                        a.CharacterSheetFileName = charIdx == 0 ? "" : _availableCharacterFiles[charIdx];
                    }
                }
                y += 20f;

                GraphLabel(new Rect(innerX, y, 38f, 18f), "Mod:");
                a.ModelPrefabName = GraphTextField(new Rect(innerX + 40f, y, 110f, 18f), a.ModelPrefabName ?? "");
                GraphLabel(new Rect(innerX + 154f, y, 42f, 18f), "Arme:");
                a.EquippedWeaponName = GraphTextField(new Rect(innerX + 198f, y, Mathf.Max(40f, innerW - 198f), 18f), a.EquippedWeaponName ?? "");
                y += 20f;

                // Sorties "Parler à" 💬 : une rangée par interlocuteur (Qui). Le Qui
                // filtre celui qui parle à l'acteur (vide = n'importe qui) ; la cible
                // est une réplique (LineId) ou un nœud (NodeId, enclenché).
                // Drag du port cyan d'une rangée = lier sa cible.
                if (a.TalkEntries == null) a.TalkEntries = new System.Collections.Generic.List<SceneActorTalkEntry>();
                {
                    int talkCount = a.TalkEntries.Count;
                    GraphLabel(new Rect(innerX, y, Mathf.Max(40f, innerW - 30f), 18f), $"💬 Parler à ({talkCount})");
                    GUI.backgroundColor = new Color(0f, 0.75f, 1f);
                    if (GraphButton(new Rect(innerX + innerW - 26f, y, 26f, 18f), "+"))
                        a.TalkEntries.Add(new SceneActorTalkEntry());
                    GUI.backgroundColor = Color.white;
                    y += 20f;

                    for (int ti = 0; ti < a.TalkEntries.Count; ti++)
                    {
                        var te = a.TalkEntries[ti];
                        if (te == null) { a.TalkEntries.RemoveAt(ti); ti--; continue; }
                        GraphLabel(new Rect(innerX, y, 30f, 18f), "Qui:");
                        const float quiW = 104f;
                        te.SpeakerId = GraphTextField(new Rect(innerX + 32f, y, quiW, 18f), te.SpeakerId ?? "");
                        Rect dropBtn = new Rect(innerX + 34f + quiW, y, 20f, 18f);
                        string dropKey = $"actor_talk_{index}_{a.ActorId}_{ti}";
                        if (GraphButton(dropBtn, "▼"))
                        {
                            if (_actorDropKey == dropKey) _actorDropKey = null;
                            else OpenActorDropdown(dropKey, dropBtn, te.SpeakerId ?? "", v => te.SpeakerId = v);
                        }
                        float tgtX = innerX + 34f + quiW + 22f;
                        GraphLabel(new Rect(tgtX, y, 16f, 18f), "→");
                        const float delW = 22f;
                        te.TargetId = GraphTextField(new Rect(tgtX + 18f, y, Mathf.Max(30f, innerW - (tgtX + 18f - innerX) - delW - 2f), 18f), te.TargetId ?? "");
                        if (GraphButton(new Rect(innerX + innerW - delW, y, delW, 18f), "✕"))
                        {
                            a.TalkEntries.RemoveAt(ti);
                            ti--;
                            continue;
                        }
                        y += 20f;

                        // Port de sortie 💬 de la rangée (cyan), sur le flanc droit.
                        Vector2 talkCenter = GetActorTalkOutCenter(a, ti);
                        bool hasTalk = !string.IsNullOrWhiteSpace(te.TargetId);
                        Color col = hasTalk ? TalkPortCol : new Color(TalkPortCol.r, TalkPortCol.g, TalkPortCol.b, 0.35f);
                        Rect talkPort = new Rect(talkCenter.x - 7f, talkCenter.y - 7f, 14f, 14f);
                        DrawGraphSolidRect(talkPort, col);
                        if (GraphPrimaryDown(mouseWorld, talkPort))
                        {
                            _wireDraft = new WireConnectionDraft
                            {
                                IsActive = true,
                                IsActorTalk = true,
                                SourceActor = a,
                                SourceTalkIndex = ti,
                                StartPos = talkCenter
                            };
                            evt.Use();
                        }
                    }
                }

                // Sortie cinématique 🎬 : jouée au spawn différé (fire-and-forget).
                DrawCineLinkRow(ref y, innerX, innerW, a.CinematicIds);
                DrawSoundLinkRow(ref y, innerX, innerW, a.ActorId);

                if (y + 4f <= cardRect.yMax)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.55f);
                    string talkSummary = "";
                    if (a.TalkEntries != null && a.TalkEntries.Count > 0)
                    {
                        var sbits = new System.Collections.Generic.List<string>();
                        for (int sti = 0; sti < a.TalkEntries.Count; sti++)
                        {
                            var ste = a.TalkEntries[sti];
                            if (ste == null || string.IsNullOrWhiteSpace(ste.TargetId)) continue;
                            string who = string.IsNullOrWhiteSpace(ste.SpeakerId) ? "*" : ste.SpeakerId.Trim();
                            sbits.Add($"{who}➔{ste.TargetId.Trim()}");
                            if (sbits.Count >= 3) break;
                        }
                        if (sbits.Count > 0) talkSummary = " · 💬" + string.Join(", ", sbits);
                    }
                    GraphLabel(new Rect(innerX, y, innerW, 18f), $"{a.GetSummary()} · ◀{refLines} ▶{refEvents} ▼{refTriggers}{talkSummary}");
                    GUI.color = Color.white;
                }
            }

            // Port de sortie cinématique 🎬 : flanc droit (magenta). La carte acteur
            // garde son port d'entrée (spawn) à gauche : input + output 🎬 coexistent.
            DrawCineOutPort(new Vector2(cardRect.xMax, cardRect.y + ActorCinePortTop), a.CinematicIds, mouseWorld);
            DrawSoundOutPort(new Vector2(cardRect.x + cardRect.width * 0.5f, cardRect.yMax), a.ActorId, mouseWorld);
        }
        private CharacterSheet ResolveActorSheet(SceneActorSpawnData a, out string sourceLabel)
        {
            sourceLabel = "Fiche par défaut générée";
            if (a == null) return new CharacterSheet();

            if (!string.IsNullOrEmpty(a.CharacterSheetFileName))
            {
                try
                {
                    string charFolder = Path.Combine(Application.persistentDataPath, "Characters");
                    string sheetPath = Path.Combine(charFolder,
                        a.CharacterSheetFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                            ? a.CharacterSheetFileName
                            : $"{a.CharacterSheetFileName}.json");
                    if (File.Exists(sheetPath))
                    {
                        var loaded = CharacterStorageService.LoadCharacter(sheetPath);
                        if (loaded != null)
                        {
                            // Preview : migre en mémoire les objets hors-catalogue
                            // (sans écrire sur le disque — la sauvegarde reste explicite).
                            ArmoryCatalog.MigrateLegacyScene01Items(loaded);
                            sourceLabel = $"Fichier '{a.CharacterSheetFileName}'";
                            return loaded;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[ScenarioEditor] Fiche introuvable : '{sheetPath}' — repli embarqué/défaut.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ScenarioEditor] Lecture fiche '{a.CharacterSheetFileName}' : {ex.Message}");
                }
            }

            if (a.EmbeddedSheet != null && !string.IsNullOrEmpty(a.EmbeddedSheet.Name))
            {
                try
                {
                    var copy = JsonUtility.FromJson<CharacterSheet>(JsonUtility.ToJson(a.EmbeddedSheet));
                    if (copy != null)
                    {
                        sourceLabel = "Fiche embarquée (JSON scène)";
                        return copy;
                    }
                }
                catch { /* repli défaut ci-dessous */ }
            }

            // Fallback héroïque (sans JSON, miroir du runtime JsonStorySceneController) :
            // "john.json" / "Operative_John" sans fichier disque → fiche héroïque intégrée.
            if (IsJohnActor(a))
            {
                sourceLabel = "Fiche héroïque intégrée [John : Passeur du Creuset]";
                return JohnCharacter.BuildHeroicSheet();
            }

            // Fallback héroïque (sans JSON, miroir du runtime) :
            // "erika.json" / "Cleyan_Erika" sans fichier disque → fiche héroïque intégrée.
            if (IsErikaActor(a))
            {
                sourceLabel = "Fiche héroïque intégrée [Erika de Cleya : Flamme Cleyane]";
                return ErikaCharacter.BuildHeroicSheet();
            }

            // Fallback héroïque (sans JSON, miroir du runtime) :
            // "vance.json" / "Vance" / "Rebel_Commander" / "Commander_Vance" sans fichier disque → fiche héroïque intégrée.
            if (IsVanceActor(a))
            {
                sourceLabel = "Fiche héroïque intégrée [Vance : Autorité du Commandant]";
                return VanceCharacter.BuildHeroicSheet();
            }

            var sheet = new CharacterSheet
            {
                Name = string.IsNullOrEmpty(a.DisplayName) ? a.ActorId : a.DisplayName,
                BaseAttributes = new Attributes(3, 3, 3, 3, 2, 2, 1, 2, 0),
                BaseArmor = a.BaseArmor,
                Profile = a.IsPlayer ? CharacterProfileType.HerosPJ : CharacterProfileType.PnjNormal,
                ModelPrefabName = a.ModelPrefabName ?? ""
            };

            // Arme de la carte : clone catalogue si le nom existe, équivalent officiel
            // si ancien nom hors-catalogue, sinon placeholder (miroir du runtime).
            // Dotation déséquipée ; ajoutée seulement si aucune arme en poche (anti-doublon).
            if (!string.IsNullOrEmpty(a.EquippedWeaponName) && !ArmoryCatalog.SheetHasWeapon(sheet))
            {
                var weapon = ArmoryCatalog.ResolveSceneWeapon(a.EquippedWeaponName, a.ActorId, equipped: false);
                sheet.AddItem(weapon);
                if (sheet.GetSkill(weapon.AssociatedSkill).TrainingLevel == 0)
                    sheet.GetSkill(weapon.AssociatedSkill).TrainingLevel = 1;
            }
            return sheet;
        }

        /// <summary>
        /// Détecte l'acteur John (miroir du runtime) : "john.json", nom ou modèle contenant "John".
        /// </summary>
        private static bool IsJohnActor(SceneActorSpawnData a)
        {
            if (a == null) return false;
            if (!string.IsNullOrEmpty(a.CharacterSheetFileName)
                && a.CharacterSheetFileName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(a.DisplayName)
                && a.DisplayName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(a.ModelPrefabName)
                && a.ModelPrefabName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (a.EmbeddedSheet != null && JohnCharacter.IsJohn(a.EmbeddedSheet))
                return true;
            return false;
        }

        /// <summary>
        /// Détecte l'actrice Erika (miroir du runtime) : "erika.json", nom ou modèle contenant "Erika".
        /// </summary>
        private static bool IsErikaActor(SceneActorSpawnData a)
        {
            if (a == null) return false;
            if (!string.IsNullOrEmpty(a.CharacterSheetFileName)
                && a.CharacterSheetFileName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(a.DisplayName)
                && a.DisplayName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(a.ModelPrefabName)
                && a.ModelPrefabName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (a.EmbeddedSheet != null && ErikaCharacter.IsErika(a.EmbeddedSheet))
                return true;
            return false;
        }

        /// <summary>
        /// Détecte le Commandant Vance (miroir du runtime) : "vance.json", nom exact
        /// "Vance"/"Commandant Vance" ("Adjudant de Vance" exclu),
        /// ou modèles exacts "Vance" / "Rebel_Commander" / "Commander_Vance".
        /// </summary>
        private static bool IsVanceActor(SceneActorSpawnData a)
        {
            if (a == null) return false;
            if (!string.IsNullOrEmpty(a.CharacterSheetFileName)
                && a.CharacterSheetFileName.IndexOf("vance", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(a.DisplayName)
                && (a.DisplayName.Equals("Vance", StringComparison.OrdinalIgnoreCase)
                    || a.DisplayName.Equals("Commandant Vance", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (!string.IsNullOrEmpty(a.ModelPrefabName)
                && (a.ModelPrefabName.Equals("vance", StringComparison.OrdinalIgnoreCase)
                    || a.ModelPrefabName.Equals("rebel_commander", StringComparison.OrdinalIgnoreCase)
                    || a.ModelPrefabName.Equals("commander_vance", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (a.EmbeddedSheet != null && VanceCharacter.IsVance(a.EmbeddedSheet))
                return true;
            return false;
        }

        private void OpenActorSheet(SceneActorSpawnData a)
        {
            if (a == null) return;
            CharacterSheet sheet = ResolveActorSheet(a, out string source);
            string ctx = $"👥 {a.DisplayName} [{a.ActorId}] · {source} · {a.GetSummary()}";
            CharacterDevWindow.OpenForSheet(sheet, ctx);
        }
    }
}
