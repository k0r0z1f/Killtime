using System;

namespace Killtime.Core.Dice
{
    /// <summary>
    /// Moteur probabiliste du Système RP (Livre II).
    /// Gère l'échelle évolutive des dés, les réussites critiques au score maximal,
    /// les échecs critiques sur le 1 naturel, et la confrontation aux Seuils de Difficulté (0-34).
    /// </summary>
    public class DiceRoller
    {
        private readonly Random _random;

        public DiceRoller(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public DiceRollResult Roll(DiceType dieType, int modifier = 0, int targetDC = 10)
        {
            int rawRoll;
            bool isCriticalSuccess = false;
            bool isCriticalFailure = false;

            switch (dieType)
            {
                case DiceType.D2:
                    rawRoll = _random.Next(1, 3);
                    isCriticalSuccess = (rawRoll == 2);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D3:
                    rawRoll = _random.Next(1, 4);
                    isCriticalSuccess = (rawRoll == 3);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D4:
                    rawRoll = _random.Next(1, 5);
                    isCriticalSuccess = (rawRoll == 4);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D6:
                    rawRoll = _random.Next(1, 7);
                    isCriticalSuccess = (rawRoll == 6);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D8:
                    rawRoll = _random.Next(1, 9);
                    isCriticalSuccess = (rawRoll == 8);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D10:
                    rawRoll = _random.Next(1, 11);
                    isCriticalSuccess = (rawRoll == 10);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D12:
                    rawRoll = _random.Next(1, 13);
                    isCriticalSuccess = (rawRoll == 12);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.D20:
                    rawRoll = _random.Next(1, 21);
                    isCriticalSuccess = (rawRoll == 20);
                    isCriticalFailure = (rawRoll == 1);
                    break;
                case DiceType.TwoD6:
                    int r6a = _random.Next(1, 7);
                    int r6b = _random.Next(1, 7);
                    rawRoll = r6a + r6b;
                    isCriticalSuccess = (r6a == 6 || r6b == 6);
                    isCriticalFailure = (r6a == 1 && r6b == 1);
                    break;
                case DiceType.TwoD8:
                    int r8a = _random.Next(1, 9);
                    int r8b = _random.Next(1, 9);
                    rawRoll = r8a + r8b;
                    isCriticalSuccess = (r8a == 8 || r8b == 8);
                    isCriticalFailure = (r8a == 1 && r8b == 1);
                    break;
                case DiceType.TwoD10:
                    int r10a = _random.Next(1, 11);
                    int r10b = _random.Next(1, 11);
                    rawRoll = r10a + r10b;
                    isCriticalSuccess = (r10a == 10 || r10b == 10);
                    isCriticalFailure = (r10a == 1 && r10b == 1);
                    break;
                case DiceType.TwoD12:
                    int r12a = _random.Next(1, 13);
                    int r12b = _random.Next(1, 13);
                    rawRoll = r12a + r12b;
                    isCriticalSuccess = (r12a == 12 || r12b == 12);
                    isCriticalFailure = (r12a == 1 && r12b == 1);
                    break;
                case DiceType.TwoD12Plus10:
                    int d1 = _random.Next(1, 13);
                    int d2 = _random.Next(1, 13);
                    rawRoll = d1 + d2 + 10;
                    isCriticalSuccess = (d1 == 12 || d2 == 12);
                    isCriticalFailure = (d1 == 1 && d2 == 1);
                    break;
                default:
                    rawRoll = _random.Next(1, 7);
                    break;
            }

            int total = rawRoll + modifier;
            int differential = total - targetDC;
            bool isSuccess = (differential >= 0) || isCriticalSuccess;

            if (isCriticalFailure)
            {
                isSuccess = false;
            }

            return new DiceRollResult
            {
                DieType = dieType,
                RawRoll = rawRoll,
                Modifier = modifier,
                Total = total,
                TargetDC = targetDC,
                Differential = differential,
                IsSuccess = isSuccess,
                IsCriticalSuccess = isCriticalSuccess,
                IsCriticalFailure = isCriticalFailure
            };
        }
    }
}
