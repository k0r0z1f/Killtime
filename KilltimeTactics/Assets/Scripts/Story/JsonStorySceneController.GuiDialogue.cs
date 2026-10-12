using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.CameraSystem;
using Killtime.UI;
using Killtime.Audio;
using Killtime.Story.Data;


namespace Killtime.Story.Scenes
{
    public partial class JsonStorySceneController
    {
        private void DrawDialogueBox(ScenarioNode node)
        {
            var dataNode = State._sceneData.FindNode(node.Id);
            bool hasDialogues = dataNode != null && dataNode.Dialogues.Count > 0 && State._currentDialogueIndex < dataNode.Dialogues.Count;

            var line = hasDialogues ? dataNode.Dialogues[State._currentDialogueIndex] : null;
            bool isReactionActive = State._activeReactionChoice != null && State._activeReactionPromptLine != null;
            bool hasChoices = !isReactionActive && line != null && line.Choices.Count > 0;

            float boxWidth = Mathf.Min(920f, Screen.width - 40f);
            float baseHeight = 150f;
            if (hasChoices)
            {
                baseHeight += line.Choices.Count * 34f;
            }
            else if (isReactionActive)
            {
                baseHeight += 20f;
            }

            float boxHeight = Mathf.Min(baseHeight, Screen.height - 120f);
            float targetY = Screen.height - boxHeight - 20f;
            float offscreenY = Screen.height + 25f;
            float eased = Mathf.SmoothStep(0f, 1f, State._chatAnimProgress);
            float currentY = Mathf.Lerp(offscreenY, targetY, eased);

            Rect rect = new Rect((Screen.width - boxWidth) * 0.5f, currentY, boxWidth, boxHeight);
            State._dialogueRect = State._chatAnimProgress > 0.05f ? rect : Rect.zero;

            Color prevGuiColor = GUI.color;
            GUI.color = new Color(prevGuiColor.r, prevGuiColor.g, prevGuiColor.b, prevGuiColor.a * eased);

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(rect);
            GUILayout.Space(6);

            if (isReactionActive)
            {
                var promptLine = State._activeReactionPromptLine;
                var reaction = State._activeReactionChoice;

                GUILayout.BeginHorizontal();
                GUI.color = Color.cyan;
                GUILayout.Label($"<b>【 {promptLine.SpeakerId} 】</b>", GUILayout.Width(180));
                GUI.color = Color.white;

                if (!string.IsNullOrEmpty(reaction.ReactionStageDirection))
                {
                    GUILayout.Label($"<color=grey><i>({reaction.ReactionStageDirection})</i></color>");
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("<color=#00E5FF>[RÉACTION]</color>", GUILayout.Width(90));
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(State._activeChallengeSummary))
                {
                    GUILayout.BeginHorizontal();
                    GUI.color = State._activeChallengeSummary.StartsWith("✔") ? Color.green : new Color(1f, 0.45f, 0.35f);
                    GUILayout.Label($"<b>{State._activeChallengeSummary}</b>");
                    GUI.color = Color.white;
                    if (!string.IsNullOrEmpty(State._activeChallengeRewards))
                    {
                        GUI.color = Color.yellow;
                        GUILayout.Label($"🎁 {State._activeChallengeRewards}");
                        GUI.color = Color.white;
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(4);
                GUILayout.Label($"« {reaction.ReactionSpeech} »", GUILayout.ExpandHeight(true));

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                if (GUILayout.Button("Continuer (Espace) ➔", GUILayout.Width(200), GUILayout.Height(30)))
                {
                    ConfirmReactionAndAdvance();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
            else if (hasDialogues)
            {
                if (line.IsNarration || string.IsNullOrEmpty(line.SpeakerId) || string.Equals(line.SpeakerId, "Narrateur", StringComparison.OrdinalIgnoreCase))
                {
                    GUILayout.BeginHorizontal();
                    GUI.color = new Color(0.95f, 0.82f, 0.45f);
                    GUILayout.Label("<b>📜 NARRATION</b>", GUILayout.Width(180));
                    GUI.color = Color.white;

                    if (!string.IsNullOrEmpty(line.StageDirection))
                    {
                        GUILayout.Label($"<color=grey><i>({line.StageDirection})</i></color>");
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"<color=grey>{State._currentDialogueIndex + 1} / {dataNode.Dialogues.Count}</color>", GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4);
                    GUILayout.Label($"<color=#E0E8F0><i>{line.Speech}</i></color>");
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUI.color = Color.cyan;
                    GUILayout.Label($"<b>【 {line.SpeakerId} 】</b>", GUILayout.Width(180));
                    GUI.color = Color.white;

                    if (!string.IsNullOrEmpty(line.StageDirection))
                    {
                        GUILayout.Label($"<color=grey><i>({line.StageDirection})</i></color>");
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"<color=grey>{State._currentDialogueIndex + 1} / {dataNode.Dialogues.Count}</color>", GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4);
                    GUILayout.Label($"« {line.Speech} »");
                }

                if (hasChoices)
                {
                    GUILayout.Space(6);
                    GUILayout.Label("<color=#FFD54F><b>Faites votre choix :</b></color>");
                    for (int c = 0; c < line.Choices.Count; c++)
                    {
                        var choice = line.Choices[c];
                        bool isMet = choice.Prerequisite == null || !choice.Prerequisite.HasPrerequisite || choice.Prerequisite.IsMet(State._playerParty, State._turnManager?.ActiveUnit, State._director);

                        bool hasChallenge = choice.Challenge != null && choice.Challenge.HasChallenge;
                        bool isOpposed = hasChallenge && choice.Challenge.IsOpposed;
                        string prefix = !isMet
                            ? $"🔒 [{choice.Prerequisite.GetSummary()}] "
                            : (hasChallenge
                                ? (isOpposed
                                    ? $"⚔️ [{choice.Challenge.GetShortLabel()}] "
                                    : $"🎲 [{choice.Challenge.GetShortLabel()}] ")
                                : "➤  ");
                        if (isMet && !string.IsNullOrEmpty(choice.NextLineId) && dataNode != null && dataNode.FindConsequence(choice.NextLineId) != null)
                            prefix += "🎁 ";

                        string btnLabel = $"{prefix}{choice.Label}";

                        GUI.enabled = isMet;
                        GUI.backgroundColor = !isMet
                            ? Color.gray
                            : (hasChallenge
                                ? (isOpposed ? new Color(1.0f, 0.55f, 0.25f) : new Color(1.0f, 0.78f, 0.25f))
                                : new Color(0.25f, 0.75f, 1.0f));

                        if (GUILayout.Button(btnLabel, GUILayout.Height(30)))
                        {
                            SelectDialogueChoice(choice, line);
                        }

                        GUI.backgroundColor = Color.white;
                        GUI.enabled = true;
                    }
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();

                    // Liaison vide = fin du nœud : retour de conversation prioritaire,
                    // sinon bouton Suivant vers la cible routée ou boutons d'actions.
                    if (!string.IsNullOrEmpty(line.NextLineId))
                    {
                        if (GUILayout.Button("Suivant (Espace) ➔", GUILayout.Width(170), GUILayout.Height(28)))
                        {
                            AdvanceDialogueOrNode();
                        }
                    }
                    else if (HasActiveTalkReturn())
                    {
                        GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                        if (GUILayout.Button("↩ Revenir (Espace)", GUILayout.Width(190), GUILayout.Height(28)))
                        {
                            TryReturnFromTalk();
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    else
                    {
                        DrawActionButtons(node);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label($"<b>{node.Title}</b> — <color=orange>{node.Location}</color>");
                GUILayout.Label(node.Body);
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                DrawActionButtons(node);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
            GUI.color = prevGuiColor;
        }

        private void DrawActionButtons(ScenarioNode node)
        {
            if (HasActiveTalkReturn())
            {
                GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                if (GUILayout.Button("↩ Revenir à la conversation", GUILayout.Width(240), GUILayout.Height(28)))
                {
                    TryReturnFromTalk();
                }
                GUI.backgroundColor = Color.white;
                return;
            }

            if (node.Kind == ScenarioNodeKind.Briefing)
            {
                GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                if (GUILayout.Button($"➔ {node.ContinueLabel}", GUILayout.Width(240), GUILayout.Height(28)))
                {
                    if (TryConsumePendingJump()) { GUI.backgroundColor = Color.white; return; }
                    if (TryReturnFromTalk()) { GUI.backgroundColor = Color.white; return; }
                    if (!TryFireReadyTrigger() && !State._director.Continue()) State._director.CompleteActiveScenario();
                }
                GUI.backgroundColor = Color.white;
            }
            else if (node.Kind == ScenarioNodeKind.Choice)
            {
                // Choix explicite : le joueur choisit, le saut différé attend
                // (pas de consommation au dessin, seulement au clic Continuer
                // ci-dessous ou en sortie de dialogue).
                if (TryFireReadyTrigger()) return;
                if (node.Choices != null && node.Choices.Count > 0)
                {
                    foreach (var choice in node.Choices)
                    {
                        if (GUILayout.Button(choice.Label, GUILayout.Height(28)))
                        {
                            State._director.Choose(choice.Id);
                        }
                    }
                }
                else
                {
                    GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                    string label = !string.IsNullOrWhiteSpace(node.ContinueLabel) ? node.ContinueLabel : "Continuer";
                    if (GUILayout.Button($"➔ {label}", GUILayout.Width(240), GUILayout.Height(28)))
                    {
                        if (TryConsumePendingJump()) { GUI.backgroundColor = Color.white; return; }
                        if (!TryFireReadyTrigger() && !State._director.Continue()) TryReturnFromTalk();
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
            else if (node.Kind == ScenarioNodeKind.Objective)
            {
                bool complete = State._director.AreRequiredObjectivesComplete();
                GUI.enabled = complete;
                GUI.backgroundColor = complete ? new Color(0.25f, 0.85f, 0.45f) : Color.gray;
                if (GUILayout.Button(complete ? $"✔ {node.ContinueLabel}" : "Objectifs requis non validés", GUILayout.Width(260), GUILayout.Height(28)))
                {
                    if (TryConsumePendingJump()) { GUI.backgroundColor = Color.white; GUI.enabled = true; return; }
                    if (!TryFireReadyTrigger() && !State._director.Continue()) TryReturnFromTalk();
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }
            else if (node.Kind == ScenarioNodeKind.Resolution)
            {
                GUI.backgroundColor = new Color(0.2f, 0.9f, 0.5f);
                string btnText = string.IsNullOrWhiteSpace(node.ContinueLabel) ? "✔ Conclure & Scène Suivante ➔" : $"✔ {node.ContinueLabel} ➔";
                if (GUILayout.Button(btnText, GUILayout.Width(280), GUILayout.Height(28)))
                {
                    if (TryConsumePendingJump()) { GUI.backgroundColor = Color.white; return; }
                    // Un "Parler à" atterri sur une résolution revient d'abord à
                    // la conversation principale au lieu de clore la scène.
                    if (!TryFireReadyTrigger() && !TryReturnFromTalk()) State._director.CompleteActiveScenario();
                }
                GUI.backgroundColor = Color.white;
            }
        }
    }
}
