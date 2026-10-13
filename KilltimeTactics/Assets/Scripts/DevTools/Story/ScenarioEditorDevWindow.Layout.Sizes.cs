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

        // Mesures des cartes : tailles + hauteurs necessaires (ex-Layout.cs, split sans perte).

        private static Vector2 GetSoundCardSize(SceneSoundData snd)
        {
            if (snd == null) return new Vector2(SndCardDefaultW, SndCardDefaultH);
            float w = snd.CardWidth > 100f ? snd.CardWidth : SndCardDefaultW;
            float h = snd.CardHeight > 80f ? snd.CardHeight : SndCardDefaultH;
            // ID + Titre + Son + Durée/Délai/Vol + Trigger + Pitch/Loop + Fades +
            // Prio/Cooldown + Spatial + Source + Stop + Tester/Stopper.
            float needed = 30f + 20f * 8f + 22f * 3f + 10f;
            if (h < needed) h = needed;
            return new Vector2(w, h);
        }

        private static Vector2 GetAmbienceCardSize(SceneAmbienceTrackData amb)
        {
            if (amb == null) return new Vector2(320f, 240f);
            float w = amb.CardWidth > 120f ? amb.CardWidth : 320f;
            float h = amb.CardHeight > 100f ? amb.CardHeight : 240f;
            float needed = 250f;
            if (h < needed) h = needed;
            return new Vector2(w, h);
        }

        private static float DlgCardDefaultH(int choiceCount) => 215f + choiceCount * 28f;
        private static int GetChallengeConfigRows(SceneDialogueChallengeData ch)
        {
            if (ch == null || !ch.HasChallenge) return 0;
            return ch.IsOpposed ? 2 : 1;
        }
        private int GetChoiceDetailRows(SceneDialogueChoiceData ch)
        {
            if (ch == null) return 0;
            int rows = 0;
            var chal = ch.Challenge;
            if (chal != null && chal.HasChallenge)
                rows += GetChallengeConfigRows(chal) + 2; // config + liens ✔/✕
            bool hasPre = ch.Prerequisite != null && ch.Prerequisite.HasPrerequisite;
            bool hasFx = ch.Effects != null && ch.Effects.Count > 0;
            if (hasPre || hasFx) rows += 1; // méta 🔒/fx
            rows += MeasurePrereqDetail(ch.Prerequisite, SectionOpen(_expandedSections, ch, "pre"));
            rows += MeasureEffectsBlock(ch.Effects, SectionOpen(_expandedSections, ch, "fx"), false);
            if (chal != null)
                rows += MeasureChallengeDetail(chal, SectionOpen(_expandedSections, ch, "chd"));
            return rows;
        }
        private void GetDialogueChoiceRowOffsets(SceneDialogueLineData line, List<float> outOffsets)
        {
            outOffsets.Clear();
            if (line == null || line.Choices == null) return;
            float y = 100f;
            for (int c = 0; c < line.Choices.Count; c++)
            {
                outOffsets.Add(y);
                y += 22f;
                y += GetChoiceDetailRows(line.Choices[c]) * 20f;
            }
        }

        private float GetDialogueCardNeededHeight(SceneDialogueLineData line)
        {
            int cc = line != null && line.Choices != null ? line.Choices.Count : 0;
            float needed = 132f + cc * 22f + 24f;
            if (line != null && line.Choices != null)
            {
                for (int c = 0; c < line.Choices.Count; c++)
                {
                    needed += GetChoiceDetailRows(line.Choices[c]) * 20f;
                }
                needed += GetDialogueLineSummaryRows(line) * 18f;
            }
            needed += 20f; // strip détails 🔒🔊🎲
            needed += CineRowH; // rangée sortie 🎬 (DrawCineLinkRow)
            needed += SoundRowH; // rangée sortie 🔊 (DrawSoundLinkRow)
            if (line != null)
            {
                needed += MeasurePrereqDetail(line.Prerequisite, SectionOpen(_expandedSections, line, "pre")) * 20f;
                needed += MeasureAmbienceDetail(line.Ambience, SectionOpen(_expandedSections, line, "amb")) * 20f;
                needed += MeasureAutoDetail(line.AutoSkillCheck, SectionOpen(_expandedSections, line, "auto")) * 20f;
            }
            return needed;
        }
        private static int GetDialogueLineSummaryRows(SceneDialogueLineData line)
        {
            if (line == null) return 0;
            int rows = 0;
            if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite) rows++;
            if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck) rows++;
            if (line.Ambience != null && line.Ambience.HasAmbience) rows++;
            return rows;
        }

        private Vector2 GetDialogueCardSize(SceneDialogueLineData line)
        {
            if (line == null) return new Vector2(DlgCardDefaultW, DlgCardDefaultH(0));
            float w = line.CardWidth > 100f ? line.CardWidth : DlgCardDefaultW;
            float h = line.CardHeight > 80f ? line.CardHeight : DlgCardDefaultH(line.Choices != null ? line.Choices.Count : 0);
            // Hauteur minimale pour que le contenu GUI absolu ne déborde jamais de la carte.
            float needed = GetDialogueCardNeededHeight(line);
            if (needed > h) h = needed;
            return new Vector2(w, h);
        }

        private float GetEventCardNeededHeight(SceneEventData ev)
        {
            // ID 20 + Kind 22 + strip 20 + détails + rangée 🎬 20 + rangée 🔊 20 + Tester 20 + marges.
            float needed = 124f + CineRowH + SoundRowH;
            if (ev != null)
            {
                needed += MeasurePrereqDetail(ev.Prerequisite, SectionOpen(_expandedSections, ev, "pre")) * 20f;
                needed += MeasureAmbienceDetail(ev.Ambience, SectionOpen(_expandedSections, ev, "amb")) * 20f;
                needed += MeasureAutoDetail(ev.AutoSkillCheck, SectionOpen(_expandedSections, ev, "auto")) * 20f;
                needed += MeasureEffectsBlock(ev.Effects, SectionOpen(_expandedSections, ev, "fx"), true) * 20f;
            }
            return needed;
        }

        private Vector2 GetEventCardSize(SceneEventData ev)
        {
            if (ev == null) return new Vector2(EvtCardDefaultW, EvtCardDefaultH);
            float w = ev.CardWidth > 100f ? ev.CardWidth : EvtCardDefaultW;
            float h = ev.CardHeight > 80f ? ev.CardHeight : EvtCardDefaultH;
            float needed = GetEventCardNeededHeight(ev);
            if (needed > h) h = needed;
            return new Vector2(w, h);
        }
        private float GetConsequenceCardNeededHeight(SceneConsequenceData cons)
        {
            float needed = 30f + 22f + 20f + 22f + 22f + 20f + CineRowH + SoundRowH + 10f;
            if (cons != null)
            {
                if (cons.HasDialogue) needed += 22f + 44f;
                int extras = cons.Rewards?.AdditionalItems != null ? cons.Rewards.AdditionalItems.Count : 0;
                needed += Mathf.Max(0, extras) * 20f;
            }
            needed += MeasureEffectsBlock(cons != null ? cons.Effects : null, SectionOpen(_expandedSections, cons, "fx"), true) * 20f;
            return needed;
        }

        private Vector2 GetConsequenceCardSize(SceneConsequenceData cons)
        {
            if (cons == null) return new Vector2(ConsCardDefaultW, ConsCardDefaultH);
            float w = cons.CardWidth > 100f ? cons.CardWidth : ConsCardDefaultW;
            float h = cons.CardHeight > 80f ? cons.CardHeight : ConsCardDefaultH;
            float needed = GetConsequenceCardNeededHeight(cons);
            if (needed > h) h = needed;
            return new Vector2(w, h);
        }
        private float GetConsLinkRowTop(SceneConsequenceData cons)
        {
            float top = 72f + ((cons != null && cons.HasDialogue) ? 66f : 0f);
            top += MeasureEffectsBlock(cons != null ? cons.Effects : null, SectionOpen(_expandedSections, cons, "fx"), true) * 20f;
            return top;
        }

        private static Vector2 GetTriggerCardSize(SceneTriggerData trg)
        {
            if (trg == null) return new Vector2(TrigCardDefaultW, TrigCardDefaultH);
            float w = trg.CardWidth > 100f ? trg.CardWidth : TrigCardDefaultW;
            float h = trg.CardHeight > 80f ? trg.CardHeight : TrigCardDefaultH;
            float needed = (trg.Kind == SceneTriggerKind.ActorCondition ? 238f : 196f) + CineRowH + SoundRowH;
            if (h < needed) h = needed;
            return new Vector2(w, h);
        }
        private static Vector2 GetCinematicCardSize(SceneCinematicData cine)
        {
            if (cine == null) return new Vector2(CineCardDefaultW, CineCardDefaultH);
            float w = cine.CardWidth > 100f ? cine.CardWidth : CineCardDefaultW;
            float h = cine.CardHeight > 80f ? cine.CardHeight : CineCardDefaultH;
            int n = cine.Shots != null ? cine.Shots.Count : 0;
            float needed = 30f + 22f + 20f + 20f + 20f + 20f * n + CineRowH + SoundRowH + 22f + 10f;
            if (h < needed) h = needed;
            return new Vector2(w, h);
        }

        private static Vector2 GetInteractableCardSize(SceneInteractableSpawnData it)
        {
            if (it == null) return new Vector2(InterCardDefaultW, InterCardDefaultH);
            float w = it.CardWidth > 100f ? it.CardWidth : InterCardDefaultW;
            float h = it.CardHeight > 80f ? it.CardHeight : InterCardDefaultH;
            if (h < InterCardDefaultH + SoundRowH) h = InterCardDefaultH + SoundRowH;
            return new Vector2(w, h);
        }

        private static Vector2 GetActorCardSize(SceneActorSpawnData a)
        {
            if (a == null) return new Vector2(ActorCardDefaultW, ActorCardDefaultH);
            float w = a.CardWidth > 100f ? a.CardWidth : ActorCardDefaultW;
            float h = a.CardHeight > 80f ? a.CardHeight : ActorCardDefaultH;
            int n = a.TalkEntries != null ? a.TalkEntries.Count : 0;
            // En-tête 💬 + une rangée par sortie : la carte grandit avec le
            // nombre d'interlocuteurs (défaut = place pour 1 sortie).
            float needed = ActorCardDefaultH + SoundRowH + ActorTalkRowH * Mathf.Max(0, n - 1);
            if (h < needed) h = needed;
            return new Vector2(w, h);
        }
    }
}
