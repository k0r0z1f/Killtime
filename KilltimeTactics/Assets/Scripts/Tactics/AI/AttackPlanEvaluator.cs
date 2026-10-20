using System;
using UnityEngine;
using Killtime.Core.Combat;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// Plan d'attaque prédictif (déplacé depuis TacticalAIController, phase 1 bis).
    /// Champs identiques à l'ancien struct privé imbriqué.
    /// </summary>
    public struct TacticalAttackPlan
    {
        public BodyPart TargetedPart;
        public bool CancelPenalty;
        public int BonusAP;
        public int BonusPE;
        public double ExpectedNetDamage;
        public bool ExceedsEncaissement;
        public bool IsLethal;
        public float UtilityScore;
    }

    /// <summary>
    /// Entrées de l'optimiseur : le contrôleur réunit tout ce qui dépend
    /// d'Unity/singletons/arène (dés, couvert, nuit, arme, élévation, config PE)
    /// puis appelle Evaluate() qui est une optimisation pure.
    /// </summary>
    [Serializable]
    public struct AttackPlanInput
    {
        public double BaseAttExpected;
        public double BaseDefExpected;
        public int WeaponDmg;
        public int TargetArmor;
        public int TargetEncaissement;
        public int TargetHp;
        public int AvailableAP;
        public int ReserveNeeded;
        public int SpendablePE;
        public int PeValue; // DuelPEBonusPerPoint (config), 1 par défaut
        public int MaxAttacks;
        public int AttacksThisTurn;
        public bool IsRanged;
        public TacticalPosture Posture;
        public int TargetRapidite;
        public bool TargetHasATerre;
        public int TargetCurrentAP;
    }

    /// <summary>
    /// Optimisation du plan d'attaque extraite verbatim de
    /// TacticalAIController.CalculateBestAttackPlan (boucle parties × visée).
    /// Pur C# : la difficulté de visée est injectée (l'original lisait
    /// BodyPartInfo.GetInfo, adossé au singleton CoreRulesConfig).
    /// Formule d'utilité inchangée : net*8 + létal 150 + encaissement 60 +
    /// bonus de partie − coûts (4/PA, 10/PE).
    /// </summary>
    public static class AttackPlanEvaluator
    {
        private static readonly BodyPart[] PartsToEvaluate =
            { BodyPart.Torse, BodyPart.Tete, BodyPart.Jambes, BodyPart.BrasDroit };

        public static TacticalAttackPlan Evaluate(
            in AttackPlanInput input, Func<BodyPart, int> aimDifficulty)
        {
            TacticalAttackPlan bestPlan = default;
            bestPlan.UtilityScore = float.MinValue;

            for (int p = 0; p < PartsToEvaluate.Length; p++)
            {
                BodyPart part = PartsToEvaluate[p];
                int partDifficulty = aimDifficulty(part);

                for (int cancel = 0; cancel < 2; cancel++)
                {
                    bool cancelAim = (cancel == 1);
                    if (!input.IsRanged && cancelAim) continue;
                    if (partDifficulty == 0 && cancelAim) continue;

                    int baseCost = cancelAim ? 3 : 2;
                    if (input.AvailableAP < baseCost) continue;

                    // Sanctuarisation de 2 PA si une seconde attaque est possible ce tour (Livre II & V)
                    bool canAttackAgain = (input.AttacksThisTurn + 1 < input.MaxAttacks)
                        && (input.AvailableAP >= baseCost + 2 + input.ReserveNeeded);
                    int apReservedForNextAttack = canAttackAgain ? 2 : 0;
                    int spendableBonusAP = Mathf.Max(0, input.AvailableAP - baseCost - input.ReserveNeeded - apReservedForNextAttack);

                    int aimMod = cancelAim ? 0 : partDifficulty;
                    double currentAttRoll = input.BaseAttExpected + aimMod;

                    // Optimisation de l'injection : quel bonus assure le franchissement d'encaissement ?
                    int chosenBonusAP = 0;
                    int chosenBonusPE = 0;

                    for (int injectAP = 0; injectAP <= Mathf.Min(spendableBonusAP, 3); injectAP++)
                    {
                        double diff = (currentAttRoll + injectAP) - input.BaseDefExpected;
                        if (diff <= 0) continue;

                        double raw = input.WeaponDmg + diff;
                        double net = Math.Max(0, raw - input.TargetArmor);

                        chosenBonusAP = injectAP;
                        if (net > input.TargetEncaissement || net >= input.TargetHp)
                        {
                            break;
                        }
                    }

                    // Injection de PE si létalité accessible ou provocation de choc.
                    // RD-IA07 : 1 PE vaut DuelPEBonusPerPoint (config), pas +1 fixe.
                    if (input.SpendablePE > 0 && input.Posture == TacticalPosture.AllInLethal)
                    {
                        double testDiff = (currentAttRoll + chosenBonusAP + input.PeValue) - input.BaseDefExpected;
                        if (testDiff > 0 && (input.WeaponDmg + testDiff - input.TargetArmor) >= input.TargetHp)
                        {
                            chosenBonusPE = 1;
                        }
                    }

                    double finalDiff = (currentAttRoll + chosenBonusAP + chosenBonusPE * input.PeValue) - input.BaseDefExpected;
                    double finalRaw = finalDiff > 0 ? (input.WeaponDmg + finalDiff) : 0;
                    double finalNet = Math.Max(0, finalRaw - input.TargetArmor);

                    bool exceedsEnc = finalNet > input.TargetEncaissement;
                    bool isLethal = finalNet >= input.TargetHp;

                    float score = (float)finalNet * 8f;

                    if (isLethal) score += 150f;
                    if (exceedsEnc) score += 60f;

                    if (part == BodyPart.Tete)
                    {
                        if (exceedsEnc) score += 80f; // Mort instantanée
                        if (!cancelAim) score -= 25f; // Malus de -2 pénalisant
                    }
                    else if (part == BodyPart.Jambes)
                    {
                        if (input.TargetRapidite > 4 && !input.TargetHasATerre)
                        {
                            score += 45f; // Clouage tactique des unités rapides
                        }
                    }
                    else if (part == BodyPart.BrasDroit)
                    {
                        if (input.TargetCurrentAP >= 3) score += 30f;
                    }

                    score -= (baseCost + chosenBonusAP) * 4f;
                    score -= chosenBonusPE * 10f;

                    if (score > bestPlan.UtilityScore)
                    {
                        bestPlan.TargetedPart = part;
                        bestPlan.CancelPenalty = cancelAim;
                        bestPlan.BonusAP = chosenBonusAP;
                        bestPlan.BonusPE = chosenBonusPE;
                        bestPlan.ExpectedNetDamage = finalNet;
                        bestPlan.ExceedsEncaissement = exceedsEnc;
                        bestPlan.IsLethal = isLethal;
                        bestPlan.UtilityScore = score;
                    }
                }
            }

            return bestPlan;
        }
    }
}
