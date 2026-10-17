// Mentions de des : layout, tooltips, breakdown (extrait de CombatHUD.cs).
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Audio;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Objectives;

namespace Killtime.UI
{
    public partial class CombatHUD : MonoBehaviour
    {
        // --- Tooltip des dés du feed : 1 s de survol sur une mention (D6, D12…)
        // --- => formule de calcul concise du niveau de dé.
        private const float DiceTooltipDelay = 1.0f;
        private readonly Dictionary<string, DiceLineLayout> _diceLayoutCache = new();
        private readonly List<DiceHitRect> _diceHitRects = new();
        private string _diceHoverKey;
        private float _diceHoverStart;
        private DiceHitRect _diceHoverHit;
        private bool _diceHoverValid;

        // =========================================================================
        // SYSTÈME DE LOGS — FEED LÉGER
        // =========================================================================
        /// <summary>Segment de ligne du feed après découpe (tags rich text préservés).</summary>
        private struct DiceLineSegment
        {
            public string Text; // texte à dessiner, tags inclus
            public float X, Y, W, H; // relatifs à l'origine du message
            public bool IsDie;
            public DiceType Die;
            // Contexte du jet (si la mention "Attaque/Défense {comp} : {dé}" a été reconnue) :
            public bool HasContext;
            public DieMentionRole Role;
            public string SkillName;
            public string Attacker;
            public string Defender;
            // Rôle du jet d'après la phase ("Phase 1/2 … lance {dé}"), pour hériter
            // ensuite du contexte du jet opposé de même rôle et même dé.
            public bool HasPhaseRole;
            public DieMentionRole PhaseRole;
        }

        /// <summary>Mise en page pré-calculée (et mise en cache) d'une ligne du feed.</summary>
        private class DiceLineLayout
        {
            public readonly List<DiceLineSegment> Segments = new();
            public float Height;
            public bool HasDice;
        }

        /// <summary>Zone survolable d'une mention de dé, en coordonnées du contenu scrollé.</summary>
        private struct DiceHitRect
        {
            public Rect Rect;
            public DiceType Die;
            public string Key;
            public bool HasContext;
            public DieMentionRole Role;
            public string SkillName;
            public string Attacker;
            public string Defender;
        }

        private static readonly Regex DiceTagStripRegex = new Regex("<[^>]*>", RegexOptions.Compiled);

        /// <summary>
        /// Hauteur d'une entrée du feed : layout segmenté si elle mentionne des dés
        /// (même mesure que la passe de dessin), sinon mesure Unity inchangée.
        /// </summary>
        private float GetLogEntryHeight(CombatLogEntry entry, float textW)
        {
            var layout = GetDiceLineLayout(entry.RawMessage, textW);
            if (layout.HasDice) return layout.Height;
            return Mathf.Max(20f, _terminalBodyStyle.CalcHeight(new GUIContent(entry.RawMessage), textW));
        }

        /// <summary>
        /// Découpe une ligne du feed en segments positionnés (retours à la ligne manuels),
        /// en repérant les mentions de dés. Résultat mis en cache (clé = message + largeur).
        /// </summary>
        private DiceLineLayout GetDiceLineLayout(string message, float availW)
        {
            var layout = new DiceLineLayout { Height = 20f };
            if (string.IsNullOrEmpty(message)) return layout;
            string key = message + "\n" + ((int)availW);
            if (_diceLayoutCache.TryGetValue(key, out var cached))
                return cached;
            layout = BuildDiceLineLayout(message, availW);
            if (_diceLayoutCache.Count > 300)
                _diceLayoutCache.Clear();
            _diceLayoutCache[key] = layout;
            return layout;
        }

        private DiceLineLayout BuildDiceLineLayout(string message, float availW)
        {
            var layout = new DiceLineLayout();
            float lineH = Mathf.Max(14f, _terminalBodyStyle.CalcSize(new GUIContent("Mg")).y);
            float spaceW = _terminalBodyStyle.CalcSize(new GUIContent(" ")).x;
            // Contexte des jets : noms des protagonistes (1re ligne) + pour chaque dé,
            // regard-arrière "Attaque/Défense {comp} :" (aucun décalage possible même
            // si un dé isolé apparaît ailleurs dans le message).
            string plainMessage = DiceTagStripRegex.Replace(message, "");
            string firstLine = plainMessage;
            int nl = plainMessage.IndexOf('\n');
            if (nl >= 0) firstLine = plainMessage.Substring(0, nl);
            var parties = DiceLogParser.ParseParties(firstLine);
            float y = 0f;
            string pendingTags = "";
            var linePlain = new StringBuilder(256);
            string[] lines = message.Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                float x = 0f;
                bool firstOnLine = true;
                linePlain.Length = 0;
                string[] tokens = lines[li].Split(' ');
                for (int ti = 0; ti < tokens.Length; ti++)
                {
                    string raw = tokens[ti];
                    if (raw.Length == 0) continue; // espaces multiples : repliés
                    string plain = DiceTagStripRegex.Replace(raw, "");
                    if (plain.Length == 0) { pendingTags += raw; continue; } // tag seul : reporté
                    string tok = pendingTags + raw;
                    pendingTags = "";
                    bool isDie = DiceTypeHints.TryParseLogToken(plain, out DiceType die);
                    float w = (isDie ? _terminalDiceStyle : _terminalBodyStyle).CalcSize(new GUIContent(tok)).x;
                    if (!firstOnLine && x + w > availW)
                    {
                        y += lineH;
                        x = 0f;
                        firstOnLine = true;
                    }
                    if (isDie) layout.HasDice = true;
                    var seg = new DiceLineSegment
                    {
                        Text = tok,
                        X = x,
                        Y = y,
                        W = w,
                        H = lineH,
                        IsDie = isDie,
                        Die = die
                    };
                    if (isDie && parties.HasParties
                        && DiceLogParser.TryParseTrailingMention(linePlain.ToString(), out var dieRole, out var dieSkill))
                    {
                        seg.HasContext = true;
                        seg.Role = dieRole;
                        seg.SkillName = dieSkill;
                        seg.Attacker = parties.Attacker;
                        seg.Defender = parties.Defender;
                    }
                    else if (isDie && DiceLogParser.TryParseTrailingPhaseRole(linePlain.ToString(), out var phaseRole))
                    {
                        seg.HasPhaseRole = true;
                        seg.PhaseRole = phaseRole;
                    }
                    layout.Segments.Add(seg);
                    linePlain.Append(plain).Append(' ');
                    x += w + spaceW;
                    firstOnLine = false;
                }
                y += lineH;
            }
            // Passe 2 : les dés des phases ("Phase 1/2 … lance {dé} → brut…", sans
            // mention de compétence) héritent du contexte du jet opposé de même
            // rôle et même dé (même variable dans CombatCalculator).
            for (int i = 0; i < layout.Segments.Count; i++)
            {
                var s = layout.Segments[i];
                if (!s.IsDie || s.HasContext || !s.HasPhaseRole) continue;
                for (int j = 0; j < layout.Segments.Count; j++)
                {
                    var c = layout.Segments[j];
                    if (c.IsDie && c.HasContext && c.Die == s.Die && c.Role == s.PhaseRole)
                    {
                        s.HasContext = true;
                        s.Role = c.Role;
                        s.SkillName = c.SkillName;
                        s.Attacker = c.Attacker;
                        s.Defender = c.Defender;
                        layout.Segments[i] = s;
                        break;
                    }
                }
            }
            layout.Height = Mathf.Max(20f, y);
            return layout;
        }

        /// <summary>Dessine une ligne contenant des mentions de dés, segment par segment.</summary>
        private void DrawDiceLineSegments(DiceLineLayout layout, CombatLogEntry entry, float yEntry)
        {
            int msgHash = entry.RawMessage != null ? entry.RawMessage.GetHashCode() : 0;
            for (int s = 0; s < layout.Segments.Count; s++)
            {
                var seg = layout.Segments[s];
                Rect r = new Rect(116f + seg.X, yEntry + seg.Y, seg.W + 2f, seg.H);
                GUI.Label(r, seg.Text, seg.IsDie ? _terminalDiceStyle : _terminalBodyStyle);
                // Seuls les dés à contexte expliqué sont survolables : aucun tooltip
                // générique ("D4 : 1-4 + mod") n'est affiché pour les autres.
                if (seg.IsDie && seg.HasContext)
                {
                    string key = msgHash + ":" + s;
                    _diceHitRects.Add(new DiceHitRect
                    {
                        Rect = r,
                        Die = seg.Die,
                        Key = key,
                        HasContext = seg.HasContext,
                        Role = seg.Role,
                        SkillName = seg.SkillName,
                        Attacker = seg.Attacker,
                        Defender = seg.Defender
                    });
                    if (key == _diceHoverKey)
                        DrawAccentLine(new Rect(r.x, r.y + r.height - 2f, seg.W, 1f), ColorCyanAccent, 0.9f);
                }
            }
        }

        /// <summary>
        /// Survol des mentions de dés : après 1 s immobile sur un dé, affiche sa
        /// formule de calcul (très concise) dans un tooltip près du curseur.
        /// </summary>
        private void DrawDiceHoverTooltip(Rect scrollRect)
        {
            Vector2 mouse = Event.current.mousePosition;
            Vector2 local = new Vector2(mouse.x - scrollRect.x + _logScroll.x,
                mouse.y - scrollRect.y + _logScroll.y);

            string hitKey = null;
            DiceHitRect hit = default;
            for (int i = 0; i < _diceHitRects.Count; i++)
            {
                if (_diceHitRects[i].Rect.Contains(local))
                {
                    hitKey = _diceHitRects[i].Key;
                    hit = _diceHitRects[i];
                    break;
                }
            }

            float now = Time.realtimeSinceStartup;
            if (hitKey == null || hitKey != _diceHoverKey)
            {
                _diceHoverKey = hitKey;
                _diceHoverStart = now;
                _diceHoverHit = hit;
                _diceHoverValid = hitKey != null;
                if (hitKey != null && hit.HasContext
                    && !TryResolveDieContext(hit, out _, out _, out string failReason))
                {
                    Debug.LogWarning($"[DiceTooltip] Aucun tooltip ({failReason}) "
                        + $"pour '{hit.SkillName}' ({hit.Role}) : {hit.Attacker} ➔ {hit.Defender}.");
                }
            }

            if (!_diceHoverValid || Event.current.type != EventType.Repaint)
                return;
            if (now - _diceHoverStart < DiceTooltipDelay)
                return;

            string text = BuildDieBreakdownText(_diceHoverHit);
            if (string.IsNullOrEmpty(text)) return;
            float padX = 10f, padY = 6f;
            float maxLine = 0f;
            foreach (string ln in text.Split('\n'))
                maxLine = Mathf.Max(maxLine, _terminalBodyStyle.CalcSize(new GUIContent(ln)).x);
            float w = Mathf.Clamp(maxLine + padX * 2f, 140f, 320f);
            float h = _terminalBodyStyle.CalcHeight(new GUIContent(text), w - padX * 2f) + padY * 2f;
            float x = Mathf.Clamp(mouse.x + 16f, 8f, Mathf.Max(8f, Screen.width - w - 8f));
            float y = mouse.y - h - 14f;
            if (y < 8f) y = mouse.y + 20f;
            Rect box = new Rect(x, y, w, h);

            DrawSoftPanel(box, new Color(0.01f, 0.02f, 0.035f, 0.96f));
            DrawAccentLine(new Rect(box.x, box.y, box.width, 2f), ColorCyanAccent, 0.9f);
            GUI.color = ColorTextBright;
            GUI.Label(new Rect(box.x + padX, box.y + padY, w - padX * 2f, h - padY * 2f), text, _terminalBodyStyle);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Retrouve une unité du combat par son nom tel que loggé : exact, puis
        /// insensible à la casse, puis par contenance (surnoms/tronquages).
        /// </summary>
        private TacticalUnit FindUnitByName(string name)
        {
            if (string.IsNullOrEmpty(name) || _turnManager == null) return null;
            var order = _turnManager.TurnOrder;
            for (int i = 0; i < order.Count; i++)
            {
                var u = order[i];
                if (u != null && u.Stats != null && u.Stats.Name == name) return u;
            }
            for (int i = 0; i < order.Count; i++)
            {
                var u = order[i];
                if (u != null && u.Stats != null
                    && string.Equals(u.Stats.Name, name, StringComparison.OrdinalIgnoreCase)) return u;
            }
            for (int i = 0; i < order.Count; i++)
            {
                var u = order[i];
                if (u == null || u.Stats == null || string.IsNullOrEmpty(u.Stats.Name)) continue;
                if (u.Stats.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf(u.Stats.Name, StringComparison.OrdinalIgnoreCase) >= 0) return u;
            }
            return null;
        }

        private static string TrainingPips(int training)
        {
            int full = Mathf.Clamp(training, 0, CharacterProgressionManager.MAX_SKILL_TRAINING);
            string pips = new string('●', full)
                + new string('○', CharacterProgressionManager.MAX_SKILL_TRAINING - full);
            if (training > CharacterProgressionManager.MAX_SKILL_TRAINING) pips += "+";
            return pips;
        }

        /// <summary>
        /// Résout le contexte d'un dé survolé (compétence + unité). False + raison
        /// si le tooltip doit retomber sur la formule concise du dé.
        /// </summary>
        private bool TryResolveDieContext(DiceHitRect hit, out SkillType skill, out TacticalUnit unit, out string failReason)
        {
            skill = default;
            unit = null;
            failReason = null;
            if (!hit.HasContext) { failReason = "mention Attaque/Défense non reconnue"; return false; }
            if (!SkillDefinitions.TryParseDisplayName(hit.SkillName, out skill))
            {
                failReason = $"compétence '{hit.SkillName}' non reconnue";
                return false;
            }
            bool isOffensive = hit.Role == DieMentionRole.Attack;
            string unitName = isOffensive ? hit.Attacker : hit.Defender;
            unit = FindUnitByName(unitName);
            if (unit == null || unit.Stats == null)
            {
                failReason = $"unité '{unitName}' introuvable au tour en cours";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Décompose pourquoi le dé est de ce type : compétence, rang de base
        /// (caracs), paliers, entraînements (icônes + niveau) et total.
        /// Null si le contexte est introuvable (aucun tooltip affiché dans ce cas).
        /// </summary>
        private string BuildDieBreakdownText(DiceHitRect hit)
        {
            if (!TryResolveDieContext(hit, out SkillType skill, out TacticalUnit unit, out _))
                return null;
            bool isOffensive = hit.Role == DieMentionRole.Attack;
            string unitName = isOffensive ? hit.Attacker : hit.Defender;

            var stats = unit.Stats;
            int rank = SkillDefinitions.GetBaseRank(skill, stats.Attributes, isOffensive);
            int steps = SkillDefinitions.CharacteristicSteps(rank);
            int training = stats.Sheet != null ? stats.Sheet.GetSkill(skill).TrainingLevel : 0;
            int total = steps + training;
            string label = DiceTypeHints.GetShortLabel(hit.Die);

            string text = $"{SkillDefinitions.GetDisplayName(skill)} — {unitName} : {label}\n"
                + $"Base {SkillDefinitions.DescribeBaseRank(skill, stats.Attributes, isOffensive)} → +{steps}\n"
                + $"Entraînement {TrainingPips(training)} {training}/{CharacterProgressionManager.MAX_SKILL_TRAINING} → +{training}\n"
                + $"Total {total} → {label}";
            if (stats.IsMagicAugmented(skill, isOffensive)) text += "\n★ Corps Augmenté";
            return text;
        }
    }
}
