using System;
using System.Collections;
using UnityEngine;
using Killtime.Tactics.Units;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Contrat offert par la façade aux services (V2 : corrige F2/F4).
    /// Les services dépendent de cette interface, jamais du CombatDevArena
    /// concret → pas de cycle. La façade reste le seul MonoBehaviour
    /// (seul habilité à StartCoroutine / s'abonner aux events).
    /// </summary>
    public interface IArenaHost
    {
        void ArenaLog(string message);
        Coroutine ArenaStartCoroutine(IEnumerator routine);
        void RequestStopAllCoroutines();
        void RequestAutoTargetNextAlive();
        void RequestSelectTarget(TacticalUnit unit);
        void NotifyTargetChanged(TacticalUnit unit);
        void RequestMusicUpdate();
        void RequestCombatOverCheck();
    }
}
