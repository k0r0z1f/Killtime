using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Killtime.Core.Dice
{
    /// <summary>Rôle du jet dans la ligne de log : attaque ou défense.</summary>
    public enum DieMentionRole
    {
        Attack,
        Defense
    }

    /// <summary>
    /// Mention de dé repérée dans une ligne du feed
    /// ("Attaque Armes Perçantes : D6", "Défense Esquive : D4"…).
    /// </summary>
    public struct DieMention
    {
        public DieMentionRole Role;
        public string SkillName;
        public DiceType Die;
    }

    /// <summary>Protagonistes d'une ligne de résolution (première ligne du message).</summary>
    public struct LogParties
    {
        public string Attacker;
        public string Defender;
        public bool HasParties;
    }

    /// <summary>
    /// Extrait d'un message du feed (texte brut, tags rich text déjà retirés) :
    /// les mentions de dés avec leur compétence, et les noms attaquant/défenseur.
    /// Les lignes sans jet opposé (Nytharite, quotas…) ne donnent ni mention ni parties :
    /// le tooltip retombe alors sur la formule concise du dé.
    /// </summary>
    public static class DiceLogParser
    {
        // Alternatives les plus longues d'abord ; \b final tranche D2 vs D12/D20.
        private const string DiePattern =
            @"TwoD12Plus10|TwoD12|TwoD10|TwoD8|TwoD6|2D12\+10|2D12|2D10|2D8|2D6|D20|D12|D10|D8|D6|D4|D3|D2";

        private static readonly Regex MentionRegex = new Regex(
            @"(Attaque|Défense)\s+(.+?)\s*:\s*(" + DiePattern + @")\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Regard-arrière : le texte qui précède IMMÉDIATEMENT un dé se termine par
        // "Attaque {comp} :" ou "Défense {comp} :". La compétence ne contient jamais
        // de ":" : le moteur retient donc le premier marqueur viable, ce qui donne
        // toujours la bonne compétence ("Défense Défense Corporelle : " → la paire,
        // pas "Corporelle" ; "Attaque A : … vs Défense E : " → "E").
        private static readonly Regex TrailingMentionRegex = new Regex(
            @"(Attaque|Défense)\s+([^:]+?)\s*:\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Rôle du jet d'après la dernière phase mentionnée
        // ("Phase 1 — Attaque : … lance D4", "Phase 2 — Défense : … lance D6").
        private static readonly Regex TrailingPhaseRegex = new Regex(
            @".*(Phase 1|Phase 2)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex VersusRegex = new Regex(
            @":\s*([^:\n]+?)\s*➔\s*([^(\n]+)",
            RegexOptions.Compiled);

        private static readonly Regex ParryRegex = new Regex(
            @"([^:\n]+?)\s+attaque, mais\s+([^:\n]+?)\s+neutralise",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Mentions "Attaque/Défense {compétence} : {dé}", dans l'ordre d'apparition.</summary>
        public static List<DieMention> ParseMentions(string plainMessage)
        {
            var mentions = new List<DieMention>();
            if (string.IsNullOrEmpty(plainMessage)) return mentions;
            foreach (Match m in MentionRegex.Matches(plainMessage))
            {
                if (!DiceTypeHints.TryParseLogToken(m.Groups[3].Value, out DiceType die))
                    continue;
                mentions.Add(new DieMention
                {
                    Role = m.Groups[1].Value.StartsWith("Déf", System.StringComparison.OrdinalIgnoreCase)
                        ? DieMentionRole.Defense
                        : DieMentionRole.Attack,
                    SkillName = m.Groups[2].Value.Trim(),
                    Die = die
                });
            }
            return mentions;
        }

        /// <summary>
        /// Regard-arrière : le fragment de texte qui précède immédiatement un dé se
        /// termine-t-il par "Attaque {comp} :" ou "Défense {comp} :" ? Utilisé au fil
        /// du layout pour associer chaque dé à sa compétence sans risque de décalage
        /// (un dé isolé ailleurs dans le message ne fausse plus l'appariement).
        /// </summary>
        public static bool TryParseTrailingMention(string plainFragment, out DieMentionRole role, out string skillName)
        {
            role = DieMentionRole.Attack;
            skillName = null;
            if (string.IsNullOrEmpty(plainFragment)) return false;
            Match m = TrailingMentionRegex.Match(plainFragment);
            if (!m.Success) return false;
            role = m.Groups[1].Value.StartsWith("Déf", System.StringComparison.OrdinalIgnoreCase)
                ? DieMentionRole.Defense
                : DieMentionRole.Attack;
            skillName = m.Groups[2].Value.Trim();
            return skillName.Length > 0;
        }

        /// <summary>
        /// Rôle d'un dé de phase ("…lance D4 → brut…") d'après la dernière phase
        /// mentionnée dans le fragment (Phase 1 = attaque, Phase 2 = défense).
        /// </summary>
        public static bool TryParseTrailingPhaseRole(string plainFragment, out DieMentionRole role)
        {
            role = DieMentionRole.Attack;
            if (string.IsNullOrEmpty(plainFragment)) return false;
            Match m = TrailingPhaseRegex.Match(plainFragment);
            if (!m.Success) return false;
            role = m.Groups[1].Value.EndsWith("1") ? DieMentionRole.Attack : DieMentionRole.Defense;
            return true;
        }

        /// <summary>Noms de l'attaquant et du défenseur depuis la première ligne.</summary>
        public static LogParties ParseParties(string plainFirstLine)
        {
            var parties = new LogParties();
            if (string.IsNullOrEmpty(plainFirstLine)) return parties;
            Match m = VersusRegex.Match(plainFirstLine);
            if (!m.Success) m = ParryRegex.Match(plainFirstLine);
            if (!m.Success) return parties;
            parties.Attacker = m.Groups[1].Value.Trim();
            parties.Defender = m.Groups[2].Value.Trim();
            parties.HasParties = parties.Attacker.Length > 0 && parties.Defender.Length > 0;
            return parties;
        }
    }
}
