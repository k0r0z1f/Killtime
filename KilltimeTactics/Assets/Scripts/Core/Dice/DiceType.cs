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
