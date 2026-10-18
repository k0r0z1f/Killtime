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
        private void FinalizeGlobalWireDraft(Vector2 mousePos, Vector2 mouseScreenPos)
        {
            // Cible nœud la plus proche du drop (auto-lien exclu), sinon tag de bord
            // hors-champ sous le curseur. Faux si rien d'acceptable sous la souris.
            bool TryPickNodeTarget(Vector2 at, string selfId, out string picked)
            {
                picked = null;
                float best = 36f;
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    if (string.Equals(kvp.Key, selfId, System.StringComparison.OrdinalIgnoreCase)) continue;
                    float dist = Vector2.Distance(kvp.Value, at);
                    if (dist < best)
                    {
                        best = dist;
                        picked = kvp.Key;
                    }
                }
                if (!string.IsNullOrEmpty(picked)) return true;
                for (int i = 0; i < _offscreenTags.Count; i++)
                {
                    var tag = _offscreenTags[i];
                    if (string.Equals(tag.TargetNodeId, selfId, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (tag.ScreenRect.Contains(_graphMouseScreenPos))
                    {
                        picked = tag.TargetNodeId;
                        return true;
                    }
                }
                return false;
            }

            void PlayWireSoundLocal()
            {
                if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                {
                    Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Filter, 0.6f);
                }
            }

            bool IsClickWithoutDrag()
            {
                return Vector2.Distance(mousePos, _wireDraft.StartPos) * _zoom <= 6f;
            }

            // Drop dans le vide (style éditeur moderne) : ouvre le menu contextuel de
            // création rapide au lieu d'effacer / d'ignorer. Simple clic = annulation.
            void OpenMenuForVoid()
            {
                if (IsClickWithoutDrag()) return;
                OpenQuickCreateMenu(_wireDraft, mousePos, mouseScreenPos);
            }

            // Carte → cinématique 🎬 : drop sur l'entrée d'une carte cinématique =
            // ajouter le lien (multi-liens autorisés), drop dans le vide = menu
            // contextuel (créer une 🎬 et lier). On n'efface jamais d'un simple raté.
            if (_wireDraft.IsCinematicLink && _wireDraft.SourceCineList != null)
            {
                float bestCine = 34f;
                string pickedCine = null;
                foreach (var kvp in _cachedCinematicInputSockets)
                {
                    float dist = Vector2.Distance(kvp.Value, mousePos);
                    if (dist < bestCine)
                    {
                        bestCine = dist;
                        pickedCine = kvp.Key;
                    }
                }
                if (!string.IsNullOrEmpty(pickedCine))
                {
                    StorySceneData.AddCineLink(_wireDraft.SourceCineList, pickedCine);
                    PlayWireSoundLocal();
                }
                else
                {
                    OpenMenuForVoid();
                }
                return;
            }

            // Branchement son 🔊 : drop sur l'entrée d'une carte son (ou sur son corps) = la lier à la
            // carte source (SourceCardId) ; drop dans le vide = créer une carte son
            // branchée ici (position du drop). On n'efface jamais d'un simple raté.
            if (_wireDraft.IsSoundLink && !string.IsNullOrWhiteSpace(_wireDraft.SourceSoundLinkCardId))
            {
                if (IsClickWithoutDrag()) return;
                string srcId = _wireDraft.SourceSoundLinkCardId;
                float bestSnd = 34f;
                string pickedSnd = null;
                foreach (var kvp in _cachedSoundInputSockets)
                {
                    float dist = Vector2.Distance(kvp.Value, mousePos);
                    if (dist < bestSnd)
                    {
                        bestSnd = dist;
                        pickedSnd = kvp.Key;
                    }
                }
                if (string.IsNullOrEmpty(pickedSnd) && _data != null && _data.Sounds != null)
                {
                    for (int s = 0; s < _data.Sounds.Count; s++)
                    {
                        var sc = _data.Sounds[s];
                        if (sc == null) continue;
                        Vector2 sz = GetSoundCardSize(sc);
                        if (new Rect(sc.GraphPosX, sc.GraphPosY, sz.x, sz.y).Contains(mousePos))
                        {
                            pickedSnd = sc.SoundId;
                            break;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(pickedSnd))
                {
                    var snd = _data != null ? _data.FindSound(pickedSnd) : null;
                    if (snd != null) snd.SourceCardId = srcId;
                    PlayWireSoundLocal();
                }
                else
                {
                    // Possibilité de brancher l'entrée d'une carte ambiance depuis le bas d'une carte source
                    float bestAmb = 34f;
                    string pickedAmb = null;
                    foreach (var kvp in _cachedAmbienceInputSockets)
                    {
                        float dist = Vector2.Distance(kvp.Value, mousePos);
                        if (dist < bestAmb)
                        {
                            bestAmb = dist;
                            pickedAmb = kvp.Key;
                        }
                    }
                    if (!string.IsNullOrEmpty(pickedAmb))
                    {
                        var amb = _data != null ? _data.FindAmbience(pickedAmb) : null;
                        if (amb != null) amb.StartSourceCardId = srcId;
                        PlayWireSoundLocal();
                        return;
                    }

                    if (_data != null)
                    {
                        _data.Sounds ??= new System.Collections.Generic.List<SceneSoundData>();
                        int sndIdx = _data.Sounds.Count + 1;
                        var created = new SceneSoundData
                        {
                            // Unicité garantie (les suppressions rendent Count+1 réutilisable).
                            SoundId = MakeUniqueSoundId(),
                            Title = $"Son {sndIdx}",
                            SourceCardId = srcId,
                            GraphPosX = mousePos.x - SndCardDefaultW * 0.5f,
                            GraphPosY = mousePos.y - 30f
                        };
                        _data.Sounds.Add(created);
                        PlayWireSoundLocal();
                    }
                }
                return;
            }

            // Liaison arrêt ambiance 🌌 : depuis le bas de la carte ambiance vers le bas d'une carte cible.
            // Parité avec l'arrêt son (RD-119) : même picker partagé TryPickSoundStopTarget
            // (répliques, events, conséquences, déclencheurs, interactables, acteurs, cinématiques, nœuds).
            if (_wireDraft.IsAmbienceStopLink && _wireDraft.SourceAmbienceTrack != null)
            {
                if (IsClickWithoutDrag()) return;
                var amb = _wireDraft.SourceAmbienceTrack;
                if (_data.Ambiences == null || !_data.Ambiences.Contains(amb)) return;
                if (TryPickSoundStopTarget(mousePos, out string pickedTarget))
                {
                    amb.StopSourceCardId = pickedTarget;
                    PlayWireSoundLocal();
                }
                return;
            }

            // Liaison arrêt son 🔊 : depuis le bas de la carte son vers le bas d'une
            // carte cible (tous types : nœud, réplique, event, conséquence, déclencheur,
            // interactable, acteur, cinématique). Miroir de l'arrêt d'ambiance.
            if (_wireDraft.IsSoundStopLink && _wireDraft.SourceSoundStop != null)
            {
                if (IsClickWithoutDrag()) return;
                var sndStop = _wireDraft.SourceSoundStop;
                if (_data.Sounds == null || !_data.Sounds.Contains(sndStop)) return;
                if (TryPickSoundStopTarget(mousePos, out string pickedStop))
                {
                    sndStop.StopSourceCardId = pickedStop;
                    PlayWireSoundLocal();
                }
                return;
            }

            // Sortie "fin →" d'une carte 🎬 : drop sur une carte (cinématique, réplique,
            // conséquence, événement ou nœud) = chaîner. Auto-lien refusé (boucle infinie).
            // Drop dans le vide = menu contextuel (créer + chaîner, ou rompre le lien).
            if (_wireDraft.IsCinematicOut && _wireDraft.SourceCinematic != null)
            {
                var src = _wireDraft.SourceCinematic;
                // Simple clic sans drag = annulation (pas d'effacement accidentel).
                if (IsClickWithoutDrag()) return;
                if (_data.Cinematics == null || !_data.Cinematics.Contains(src)) return;
                string picked = null;
                float best = 34f;
                foreach (var kvp in _cachedCinematicInputSockets)
                {
                    if (string.Equals(kvp.Key, src.CinematicId, StringComparison.OrdinalIgnoreCase)) continue;
                    float dist = Vector2.Distance(kvp.Value, mousePos);
                    if (dist < best) { best = dist; picked = kvp.Key; }
                }
                if (string.IsNullOrEmpty(picked))
                {
                    best = 34f;
                    foreach (var kvp in _cachedInputSockets)
                    {
                        float dist = Vector2.Distance(kvp.Value, mousePos);
                        if (dist < best) { best = dist; picked = kvp.Key; }
                    }
                }
                if (string.IsNullOrEmpty(picked))
                {
                    best = 36f;
                    foreach (var kvp in _cachedNodeInputSockets)
                    {
                        float dist = Vector2.Distance(kvp.Value, mousePos);
                        if (dist < best) { best = dist; picked = kvp.Key; }
                    }
                }
                if (!string.IsNullOrEmpty(picked))
                {
                    src.NextTargetId = picked;
                    PlayWireSoundLocal();
                }
                else
                {
                    OpenMenuForVoid();
                }
                return;
            }

            // Nœud → nœud (sortie principale NextNodeId) : drop sur l'entrée d'un
            // nœud = lier (auto-lien refusé), drop sur un tag de bord = lier la cible
            // hors-champ, drop dans le vide = menu (créer un nœud, ou rompre le lien).
            if (_wireDraft.IsNode && _wireDraft.SourceNode != null)
            {
                var src = _wireDraft.SourceNode;
                // Simple clic sans drag = annulation (pas d'effacement accidentel).
                if (IsClickWithoutDrag()) return;
                if (_data.Nodes.Contains(src))
                {
                    if (TryPickNodeTarget(mousePos, src.NodeId, out string picked))
                    {
                        src.NextNodeId = picked;
                        PlayWireSoundLocal();
                    }
                    else
                    {
                        OpenMenuForVoid();
                    }
                }
                return;
            }
            // Choix majeur → nœud : même routage vers Choices[i].NextNodeId.
            if (_wireDraft.IsNodeChoice && _wireDraft.SourceNode != null)
            {
                var src = _wireDraft.SourceNode;
                int ci = _wireDraft.NodeChoiceIndex;
                bool valid = _data.Nodes.Contains(src) && src.Choices != null && ci >= 0 && ci < src.Choices.Count && src.Choices[ci] != null;
                // Simple clic sans drag = annulation (pas d'effacement accidentel).
                if (!valid) return;
                if (IsClickWithoutDrag()) return;
                if (TryPickNodeTarget(mousePos, src.NodeId, out string picked))
                {
                    src.Choices[ci].NextNodeId = picked;
                    PlayWireSoundLocal();
                }
                else
                {
                    OpenMenuForVoid();
                }
                return;
            }
            // Déclencheur : déposé sur l'entrée d'un nœud, sa sortie entre dans ce nœud.
            // Vide = menu (créer un nœud cible).
            if (_wireDraft.IsTrigger && _wireDraft.SourceTrigger != null)
            {
                if (IsClickWithoutDrag()) return;
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    if (Vector2.Distance(kvp.Value, mousePos) < 36f)
                    {
                        _wireDraft.SourceTrigger.TargetNodeId = kvp.Key;
                        PlayWireSoundLocal();
                        return;
                    }
                }
                OpenMenuForVoid();
                return;
            }
            // Interactable : même routage, la cible est TriggerNodeId (saut runtime existant).
            // Vide = menu (créer un nœud cible).
            if (_wireDraft.IsInteractable && _wireDraft.SourceInteractable != null)
            {
                if (IsClickWithoutDrag()) return;
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    if (Vector2.Distance(kvp.Value, mousePos) < 36f)
                    {
                        _wireDraft.SourceInteractable.TriggerNodeId = kvp.Key;
                        PlayWireSoundLocal();
                        return;
                    }
                }
                OpenMenuForVoid();
                return;
            }
            // Acteur 👥 → réplique 💬 / nœud 🟧 : drop sur l'entrée d'une réplique
            // (LineId) ou d'un nœud (NodeId, enclenché) = lier la rangée
            // (TalkEntries[i].TargetId). Vide = menu (créer + lier).
            // Simple clic = annulation.
            if (_wireDraft.IsActorTalk && _wireDraft.SourceActor != null)
            {
                if (IsClickWithoutDrag()) return;
                var srcActor = _wireDraft.SourceActor;
                int srcIdx = _wireDraft.SourceTalkIndex;
                if (_data.Actors == null || !_data.Actors.Contains(srcActor)
                    || srcActor.TalkEntries == null || srcIdx < 0
                    || srcIdx >= srcActor.TalkEntries.Count || srcActor.TalkEntries[srcIdx] == null) return;
                float bestTalk = 32f;
                string pickedTalk = null;
                foreach (var kvp in _cachedInputSockets)
                {
                    if (!SceneContainsLine(kvp.Key)) continue;
                    float dist = Vector2.Distance(kvp.Value, mousePos);
                    if (dist < bestTalk)
                    {
                        bestTalk = dist;
                        pickedTalk = kvp.Key;
                    }
                }
                if (!string.IsNullOrEmpty(pickedTalk))
                {
                    srcActor.TalkEntries[srcIdx].TargetId = pickedTalk;
                    PlayWireSoundLocal();
                }
                else if (TryPickNodeTarget(mousePos, "", out string pickedNode) && _data.FindNode(pickedNode) != null)
                {
                    srcActor.TalkEntries[srcIdx].TargetId = pickedNode;
                    PlayWireSoundLocal();
                }
                else
                {
                    OpenMenuForVoid();
                }
                return;
            }

            string targetId = null;

            // 1. Socket de dialogue/événement/conséquence LA PLUS PROCHE (pas la première
            // du dictionnaire : en graphe dense plusieurs sockets sont dans le rayon).
            float bestDist = 32f;
            foreach (var kvp in _cachedInputSockets)
            {
                float dist = Vector2.Distance(kvp.Value, mousePos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    targetId = kvp.Key;
                }
            }

            // 2. Test de collision avec les sockets de Nœud complet
            // (conservé pour compat : un drop précis sur un nœud lie quand même,
            // mais le menu quick-create ne propose un nœud que pour les sorties
            // globales — les sorties de cartes créent des cartes sœurs).
            if (string.IsNullOrEmpty(targetId))
            {
                float bestNodeDist = 36f;
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    float dist = Vector2.Distance(kvp.Value, mousePos);
                    if (dist < bestNodeDist)
                    {
                        bestNodeDist = dist;
                        targetId = kvp.Key;
                    }
                }
            }

            // Vide = menu contextuel (créer une carte sœur + lier).
            if (string.IsNullOrEmpty(targetId))
            {
                OpenMenuForVoid();
                return;
            }

            if (_wireDraft.IsEvent && _wireDraft.SourceEvent != null)
            {
                _wireDraft.SourceEvent.NextEventId = targetId;
            }
            else if (_wireDraft.FromChallengeLink && _wireDraft.SourceChallenge != null)
            {
                // La sortie ✔/✕ accepte une conséquence (prioritaire) ou un dialogue normal.
                // Le wire fait foi : le champ concurrent est effacé pour éviter toute ambiguïté.
                if (SceneContainsConsequence(targetId))
                {
                    if (_wireDraft.ChallengeLinkIsSuccess)
                    {
                        _wireDraft.SourceChallenge.SuccessConsequenceId = targetId;
                        _wireDraft.SourceChallenge.SuccessNextLineId = "";
                    }
                    else
                    {
                        _wireDraft.SourceChallenge.FailureConsequenceId = targetId;
                        _wireDraft.SourceChallenge.FailureNextLineId = "";
                    }
                }
                else if (SceneContainsLine(targetId))
                {
                    if (_wireDraft.ChallengeLinkIsSuccess)
                    {
                        _wireDraft.SourceChallenge.SuccessNextLineId = targetId;
                        _wireDraft.SourceChallenge.SuccessConsequenceId = "";
                    }
                    else
                    {
                        _wireDraft.SourceChallenge.FailureNextLineId = targetId;
                        _wireDraft.SourceChallenge.FailureConsequenceId = "";
                    }
                }
            }
            else if (_wireDraft.FromConsequence && _wireDraft.SourceConsequence != null)
            {
                // Les deux sorties (→Diag et →Cons) acceptent répliques comme conséquences.
                // Le wire fait foi : le champ concurrent est effacé pour éviter toute ambiguïté.
                if (SceneContainsConsequence(targetId))
                {
                    _wireDraft.SourceConsequence.NextConsequenceId = targetId;
                    _wireDraft.SourceConsequence.NextLineId = "";
                }
                else if (SceneContainsLine(targetId))
                {
                    _wireDraft.SourceConsequence.NextLineId = targetId;
                    _wireDraft.SourceConsequence.NextConsequenceId = "";
                }
            }
            else if (_wireDraft.FromChoice && _wireDraft.SourceChoice != null)
            {
                _wireDraft.SourceChoice.NextLineId = targetId;
            }
            else if (_wireDraft.SourceLine != null)
            {
                _wireDraft.SourceLine.NextLineId = targetId;
            }

            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
            {
                Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Filter, 0.6f);
            }
        }

        private void DrawConnectedGraphWires(SceneNodeData node, Rect visibleWorld)
        {
            for (int d = 0; d < node.Dialogues.Count; d++)
            {
                var line = node.Dialogues[d];
                Vector2 size = GetDialogueCardSize(line);
                Vector2 cardPos = new Vector2(line.GraphPosX, line.GraphPosY);

                if (!string.IsNullOrEmpty(line.NextLineId)
                    && _cachedInputSockets.TryGetValue(line.NextLineId, out var targetIn))
                {
                    Vector2 outSocket = new Vector2(cardPos.x + size.x, cardPos.y + 26f);
                    DrawBezierWire(outSocket, targetIn, new Color(0.0f, 0.85f, 1.0f, 0.95f), 3.5f);
                }

                // Sortie 🎬 de la réplique : flanc droit, rangée dédiée.
                DrawCineWireList(new Vector2(cardPos.x + size.x, cardPos.y + CinePortTopCards), line.CinematicIds);

                var choiceRowYs = new List<float>();
                GetDialogueChoiceRowOffsets(line, choiceRowYs);
                for (int c = 0; c < line.Choices.Count; c++)
                {
                    var ch = line.Choices[c];
                    if (ch == null) continue;
                    if (!string.IsNullOrEmpty(ch.NextLineId)
                        && _cachedInputSockets.TryGetValue(ch.NextLineId, out var chTargetIn))
                    {
                        // Aligné sur la rangée GUI réelle (cf. DrawDialogueCard : +100 + extras).
                        float rowY = (c >= 0 && c < choiceRowYs.Count)
                            ? cardPos.y + choiceRowYs[c] + 10f
                            : cardPos.y + 110f + (c * 22f);
                        Vector2 chOutSocket = new Vector2(cardPos.x + size.x, rowY);
                        bool wVs = ch.Challenge != null && ch.Challenge.HasChallenge && ch.Challenge.IsOpposed;
                        bool wChal = ch.Challenge != null && ch.Challenge.HasChallenge;
                        DrawBezierWire(chOutSocket, chTargetIn, wVs ? new Color(1f, 0.55f, 0.2f, 0.95f) : (wChal ? new Color(1f, 0.85f, 0.3f, 0.95f) : new Color(1.0f, 0.8f, 0.2f, 0.95f)), 3.0f);
                    }

                    // Liens du défi : ✔ et ✕ vers cartes Conséquence OU répliques directes.
                    if (ch.Challenge != null && ch.Challenge.HasChallenge)
                    {
                        float linkTop = (c >= 0 && c < choiceRowYs.Count)
                            ? choiceRowYs[c] + 22f + GetChallengeConfigRows(ch.Challenge) * 20f
                            : 122f + (c * 22f);
                        string okId = !string.IsNullOrEmpty(ch.Challenge.SuccessConsequenceId)
                            ? ch.Challenge.SuccessConsequenceId : ch.Challenge.SuccessNextLineId;
                        if (!string.IsNullOrEmpty(okId)
                            && _cachedInputSockets.TryGetValue(okId, out var okIn))
                        {
                            DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + linkTop + 10f), okIn, new Color(0.35f, 1f, 0.55f, 0.95f), 3.0f);
                        }
                        string koId = !string.IsNullOrEmpty(ch.Challenge.FailureConsequenceId)
                            ? ch.Challenge.FailureConsequenceId : ch.Challenge.FailureNextLineId;
                        if (!string.IsNullOrEmpty(koId)
                            && _cachedInputSockets.TryGetValue(koId, out var koIn))
                        {
                            DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + linkTop + 30f), koIn, new Color(1f, 0.4f, 0.35f, 0.95f), 3.0f);
                        }
                    }
                }

                if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
                {
                    if (!string.IsNullOrEmpty(line.AutoSkillCheck.SuccessNextLineId)
                        && _cachedInputSockets.TryGetValue(line.AutoSkillCheck.SuccessNextLineId, out var sIn))
                    {
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + 70f), sIn, Color.green, 2.5f);
                    }
                    if (!string.IsNullOrEmpty(line.AutoSkillCheck.FailureNextLineId)
                        && _cachedInputSockets.TryGetValue(line.AutoSkillCheck.FailureNextLineId, out var fIn))
                    {
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + 90f), fIn, new Color(1f, 0.35f, 0.35f), 2.5f);
                    }
                }
            }

            for (int e = 0; e < node.Events.Count; e++)
            {
                var ev = node.Events[e];
                Vector2 size = GetEventCardSize(ev);
                Vector2 cardPos = new Vector2(ev.GraphPosX, ev.GraphPosY);
                if (!string.IsNullOrEmpty(ev.NextEventId)
                    && _cachedInputSockets.TryGetValue(ev.NextEventId, out var targetIn))
                {
                    Vector2 outSocket = new Vector2(cardPos.x + size.x, cardPos.y + 26f);
                    DrawBezierWire(outSocket, targetIn, new Color(0.85f, 0.45f, 1.0f, 0.95f), 3.5f);
                }
                // Sortie 🎬 de l'événement.
                DrawCineWireList(new Vector2(cardPos.x + size.x, cardPos.y + CinePortTopCards), ev.CinematicIds);
            }

            if (node.Consequences != null)
            {
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cons = node.Consequences[k];
                    if (cons == null) continue;
                    Vector2 size = GetConsequenceCardSize(cons);
                    Vector2 cardPos = new Vector2(cons.GraphPosX, cons.GraphPosY);
                    if (!string.IsNullOrEmpty(cons.NextLineId)
                        && _cachedInputSockets.TryGetValue(cons.NextLineId, out var lineIn))
                    {
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + 26f), lineIn, new Color(0.0f, 0.85f, 1.0f, 0.95f), 3.0f);
                    }
                    if (!string.IsNullOrEmpty(cons.NextConsequenceId)
                        && _cachedInputSockets.TryGetValue(cons.NextConsequenceId, out var consIn))
                    {
                        float nextRowTop = GetConsLinkRowTop(cons);
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + nextRowTop + 10f), consIn, new Color(0.4f, 1.0f, 0.5f, 0.95f), 3.0f);
                    }
                    // Sortie 🎬 de la conséquence.
                    DrawCineWireList(new Vector2(cardPos.x + size.x, cardPos.y + CinePortTopCards), cons.CinematicIds);
                }
            }
        }
    }
}
