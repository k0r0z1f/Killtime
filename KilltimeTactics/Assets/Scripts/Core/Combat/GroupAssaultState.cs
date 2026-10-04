using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Attaques combinées de groupe (RD-084, Livre VI §25.2) :
    /// deux combattants ou plus synchronisent leurs assauts pour briser une
    /// défense impénétrable. Tous les participants délèguent et calent leur
    /// initiative sur celle du membre le plus lent du groupe, puis chaque
    /// participant effectue son épreuve : les résultats totaux s'additionnent
    /// face à l'unique défense de la cible (dépassement massif pulvérisant
    /// armure et encaissement).
    ///
    /// État volontairement transitoire : la synchronisation est instantanée
    /// (résolue au tour du membre le plus lent, aucun report inter-round).
    /// Le registre ci-dessous ne sert qu'à la traçabilité (dernier groupe
    /// synchronisé) et au cycle de vie TurnManager (purge reset/sortie).
    /// Keyé sur CharacterStats (couche Core, joueur + IA, sans dépendance Unity).
    /// </summary>
    public static class GroupAssaultState
    {
        private sealed class GroupRecord
        {
            public List<CharacterStats> Members = new();
            public CharacterStats Slowest;
            public int SlowestInitiative;
            public int Round;
        }

        private static readonly List<GroupRecord> _groups = new();

        /// <summary>
        /// Initiative du membre le plus lent (minimum des totaux d'initiative
        /// roulés). Les participants sans jet enregistré (int.MinValue) sont
        /// ignorés ; si aucun n'a roulé, retourne int.MinValue.
        /// </summary>
        public static int GetSlowestInitiative(IReadOnlyList<CharacterStats> participants)
        {
            if (participants == null || participants.Count == 0) return int.MinValue;
            int slowest = int.MaxValue;
            bool found = false;
            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                if (p == null) continue;
                if (p.InitiativeRollTotal == int.MinValue) continue;
                found = true;
                if (p.InitiativeRollTotal < slowest) slowest = p.InitiativeRollTotal;
            }
            return found ? slowest : int.MinValue;
        }

        /// <summary>
        /// Retrouve le membre le plus lent du groupe (null si aucun jet enregistré).
        /// </summary>
        public static CharacterStats GetSlowestMember(IReadOnlyList<CharacterStats> participants)
        {
            if (participants == null || participants.Count == 0) return null;
            CharacterStats slowest = null;
            int best = int.MaxValue;
            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                if (p == null || p.InitiativeRollTotal == int.MinValue) continue;
                if (p.InitiativeRollTotal < best)
                {
                    best = p.InitiativeRollTotal;
                    slowest = p;
                }
            }
            return slowest;
        }

        /// <summary>
        /// Valide qu'un groupe peut frapper à l'unisson : au moins 2 membres
        /// (config), tous vivants et en état d'attaquer, avec assez de PA pour
        /// le coût d'attaque de base chacun.
        /// </summary>
        public static bool TryValidateGroup(
            IReadOnlyList<CharacterStats> participants,
            int baseApCost,
            int minMembers,
            out string error)
        {
            error = null;
            if (participants == null || participants.Count < minMembers)
            {
                error = $"Attaque combinée impossible : {minMembers} attaquants minimum requis (présents : {(participants == null ? 0 : participants.Count)}).";
                return false;
            }
            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                if (p == null) { error = "Attaque combinée impossible : membre null."; return false; }
                if (!p.IsAlive) { error = $"{p.Name} est hors de combat : attaque combinée impossible."; return false; }
                if (p.CurrentActionPoints < baseApCost)
                {
                    error = $"{p.Name} n'a pas assez de PA ({p.CurrentActionPoints}/{baseApCost}) pour frapper à l'unisson !";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Enregistre une synchronisation (traçabilité + tests). L'initiative
        /// d'exécution est celle du membre le plus lent.
        /// </summary>
        public static int RegisterSynchronizedGroup(IReadOnlyList<CharacterStats> participants, int round = 0)
        {
            var record = new GroupRecord { Round = round };
            if (participants != null)
            {
                for (int i = 0; i < participants.Count; i++)
                {
                    if (participants[i] != null) record.Members.Add(participants[i]);
                }
            }
            record.Slowest = GetSlowestMember(record.Members);
            record.SlowestInitiative = GetSlowestInitiative(record.Members);
            _groups.Add(record);
            return record.SlowestInitiative;
        }

        public static int SynchronizedGroupCount => _groups.Count;

        /// <summary>Retire toute trace impliquant cette unité (sortie de carte).</summary>
        public static void RemoveUnit(CharacterStats unit)
        {
            if (unit == null) return;
            for (int i = _groups.Count - 1; i >= 0; i--)
            {
                var g = _groups[i];
                if (g == null) { _groups.RemoveAt(i); continue; }
                g.Members.Remove(unit);
                if (g.Slowest == unit) g.Slowest = GetSlowestMember(g.Members);
                if (g.Members.Count == 0) _groups.RemoveAt(i);
            }
        }

        public static void ClearAll() => _groups.Clear();
    }
}
