namespace Killtime.Core.Dice
{
    /// <summary>
    /// Échelle polyédrique vivante du Système RP Killtime (Livre II).
    /// Du dé d'amateur (d2) à l'élite cosmique (2d12+10).
    /// </summary>
    public enum DiceType
    {
        D2,
        D3,
        D4,
        D6,
        D8,
        D10,
        D12,
        D20,
        TwoD6,
        TwoD8,
        TwoD10,
        TwoD12,
        TwoD12Plus10
    }

    /// <summary>
    /// Aide-mémoire très concis du calcul de chaque niveau de dé, affiché en
    /// tooltip au survol des mentions (D6, D12, TwoD6…) dans le feed des actions.
    /// Partagé entre le HUD et les éventuels autres lecteurs de logs.
    /// </summary>
    public static class DiceTypeHints
    {
        /// <summary>Libellé court d'affichage ("TwoD6" → "2D6").</summary>
        public static string GetShortLabel(DiceType die)
        {
            switch (die)
            {
                case DiceType.TwoD6: return "2D6";
                case DiceType.TwoD8: return "2D8";
                case DiceType.TwoD10: return "2D10";
                case DiceType.TwoD12: return "2D12";
                case DiceType.TwoD12Plus10: return "2D12+10";
                default: return die.ToString();
            }
        }

        /// <summary>Formule ultra-concise : plage du tirage + modificateurs = Total.</summary>
        public static string GetShortHint(DiceType die)
        {
            switch (die)
            {
                case DiceType.D2: return "D2 : 1-2 + mod = Total";
                case DiceType.D3: return "D3 : 1-3 + mod = Total";
                case DiceType.D4: return "D4 : 1-4 + mod = Total";
                case DiceType.D6: return "D6 : 1-6 + mod = Total";
                case DiceType.D8: return "D8 : 1-8 + mod = Total";
                case DiceType.D10: return "D10 : 1-10 + mod = Total";
                case DiceType.D12: return "D12 : 1-12 + mod = Total";
                case DiceType.D20: return "D20 : 1-20 + mod = Total";
                case DiceType.TwoD6: return "2D6 : 2-12 + mod = Total";
                case DiceType.TwoD8: return "2D8 : 2-16 + mod = Total";
                case DiceType.TwoD10: return "2D10 : 2-20 + mod = Total";
                case DiceType.TwoD12: return "2D12 : 2-24 + mod = Total";
                case DiceType.TwoD12Plus10: return "2D12+10 : 12-34 + mod = Total";
                default: return die.ToString();
            }
        }

        /// <summary>
        /// Reconnaît une mention de dé telle qu'affichée dans les logs
        /// ("D6", "TwoD6", "2d6,", "2D12+10"… : casse et ponctuation tolérées).
        /// </summary>
        public static bool TryParseLogToken(string token, out DiceType die)
        {
            die = DiceType.D6;
            if (string.IsNullOrEmpty(token)) return false;
            string t = token.Trim().Trim(',', '.', ':', ';', '!', '?', '(', ')', '[', ']', '"', '\'').ToUpperInvariant();
            switch (t)
            {
                case "D2": die = DiceType.D2; return true;
                case "D3": die = DiceType.D3; return true;
                case "D4": die = DiceType.D4; return true;
                case "D6": die = DiceType.D6; return true;
                case "D8": die = DiceType.D8; return true;
                case "D10": die = DiceType.D10; return true;
                case "D12": die = DiceType.D12; return true;
                case "D20": die = DiceType.D20; return true;
                case "TWOD6":
                case "2D6": die = DiceType.TwoD6; return true;
                case "TWOD8":
                case "2D8": die = DiceType.TwoD8; return true;
                case "TWOD10":
                case "2D10": die = DiceType.TwoD10; return true;
                case "TWOD12":
                case "2D12": die = DiceType.TwoD12; return true;
                case "TWOD12PLUS10":
                case "2D12+10": die = DiceType.TwoD12Plus10; return true;
                default: return false;
            }
        }
    }

    /// <summary>
    /// Résultat détaillé d'un lancer de dé sous les règles du Codex.
    /// </summary>
    public struct DiceRollResult
    {
        public DiceType DieType;
        public int RawRoll;
        public int Modifier;
        public int Total;
        public int TargetDC;
        public int Differential; // Total - TargetDC
        public bool IsSuccess;
        public bool IsCriticalSuccess;
        public bool IsCriticalFailure;

        public override string ToString()
        {
            string crit = IsCriticalSuccess ? " [CRITIQUE SUCCÈS!]" : (IsCriticalFailure ? " [ÉCHEC CRITIQUE!]" : "");
            return $"{DieType} => Tirage: {RawRoll} + Mod({Modifier}) = {Total} vs SD({TargetDC}) | Diff: {Differential}{crit}";
        }
    }
}
