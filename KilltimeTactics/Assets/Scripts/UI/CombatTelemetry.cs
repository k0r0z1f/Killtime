// Telemetrie de combat (extraite de CombatHUD.cs, comportement inchange).
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
    public class CombatTelemetry
    {
        public int TotalAttacks;
        public int SuccessfulHits;
        public int CriticalHits;
        public int DeflectedOrParried;
        public int DamageDealtByAllies;
        public int DamageTakenByAllies;
        public int ArmorAbsorbedByAllies;
        public int ArmorAbsorbedByEnemies;
        public int TraumaShocks;
        public int EnemiesNeutralized;
        public int AlliesDown;
        public string TopDamageDealer = "—";
        public int TopDamageAmount = 0;
        private readonly Dictionary<string, int> _damageByUnit = new(StringComparer.OrdinalIgnoreCase);

        public void Reset()
        {
            TotalAttacks = 0;
            SuccessfulHits = 0;
            CriticalHits = 0;
            DeflectedOrParried = 0;
            DamageDealtByAllies = 0;
            DamageTakenByAllies = 0;
            ArmorAbsorbedByAllies = 0;
            ArmorAbsorbedByEnemies = 0;
            TraumaShocks = 0;
            EnemiesNeutralized = 0;
            AlliesDown = 0;
            TopDamageDealer = "—";
            TopDamageAmount = 0;
            _damageByUnit.Clear();
        }

        public void RecordAttack(string attacker, string defender, bool isAttackerPlayer, bool isDefenderPlayer,
            bool isHit, bool isCrit, bool isBlocked, int rawDmg, int armorAbsorbed, int finalDmg,
            bool causedShock, bool isFatal, int apCost)
        {
            TotalAttacks++;
            if (isHit)
            {
                SuccessfulHits++;
                if (isCrit) CriticalHits++;
                if (causedShock) TraumaShocks++;

                if (isAttackerPlayer)
                {
                    DamageDealtByAllies += finalDmg;
                    ArmorAbsorbedByEnemies += armorAbsorbed;
                    if (!string.IsNullOrEmpty(attacker))
                    {
                        int cur = _damageByUnit.TryGetValue(attacker, out int d) ? d : 0;
                        cur += finalDmg;
                        _damageByUnit[attacker] = cur;
                        if (cur > TopDamageAmount)
                        {
                            TopDamageAmount = cur;
                            TopDamageDealer = attacker;
                        }
                    }
                }
                if (isDefenderPlayer)
                {
                    DamageTakenByAllies += finalDmg;
                    ArmorAbsorbedByAllies += armorAbsorbed;
                }

                if (isFatal)
                {
                    if (isDefenderPlayer) AlliesDown++;
                    else EnemiesNeutralized++;
                }
            }
            else if (isBlocked)
            {
                DeflectedOrParried++;
            }
        }
    }
}
