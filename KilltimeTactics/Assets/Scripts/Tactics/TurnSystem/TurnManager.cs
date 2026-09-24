using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Tactics.TurnSystem
{
    public enum CombatOutcome
    {
        InProgress,
        Victory,
        Defeat
    }

    public enum TurnSystemMode
    {
        Exploration,
        CombatTurnBased
    }

    [Serializable]
    public struct InitiativeRollResult
    {
        public TacticalUnit Unit;
        public DiceType Die;
        public int RawRoll;
        public int Total;
    }

    public class TurnManager : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private List<TacticalUnit> _allUnits = new();

        public TurnSystemMode SystemMode { get; private set; } = TurnSystemMode.Exploration;
        public bool IsInExploration => SystemMode == TurnSystemMode.Exploration;

        public int CurrentRound { get; private set; } = 1;
        public TacticalUnit ActiveUnit { get; private set; }
        public bool IsCombatOver { get; private set; } = false;
        public CombatOutcome CurrentOutcome { get; private set; } = CombatOutcome.InProgress;

        /// <summary>
        /// Quand vrai, RegisterUnit se contente d'ajouter sans démarrer de round.
        /// Utilisé par CombatDevArena pendant les enregistrements en masse (Reset/Setup/Load)
        /// pour éviter un double StartNewRound qui lance l'IA en prématuré puis la coupe,
        /// laissant _isAttackInProgress / cinématique en état bloqué (surtout en FullAuto).
        /// </summary>
        public bool SuspendAutoStart { get; set; } = false;

        private int _activeUnitIndex = 0;

        // Jet d'initiative (Livre I §4.3) : DiceRoller dédié (injectable pour tests
        // déterministes). Les résultats sont stockés sur unit.Stats afin de vivre avec
        // l'unité (détruite → relevé détruit, aucun dictionnaire d'objets à purger).
        private DiceRoller _diceRoller = new DiceRoller();
        private bool _needsInitiativeRoll = true;

        public event Action<TacticalUnit> OnTurnStarted;
        public event Action<int> OnRoundStarted;
        public event Action<CombatOutcome> OnCombatEnded;
        public event Action<TacticalUnit, List<StatusEffect>> OnUnitStatusExpired;
        public event Action<IReadOnlyList<InitiativeRollResult>> OnInitiativeRolled;

        /// <summary>
        /// Ordre de passage actuel (après jet d'initiative).
        /// </summary>
        public IReadOnlyList<TacticalUnit> TurnOrder => _allUnits.AsReadOnly();

        public void SetDiceSeed(int seed) => _diceRoller = new DiceRoller(seed);
        public void SetDiceRoller(DiceRoller roller) => _diceRoller = roller ?? new DiceRoller();

        public bool HasInitiativeRolled(TacticalUnit unit)
        {
            return unit != null && unit.Stats != null
                && unit.Stats.InitiativeRollTotal != int.MinValue;
        }

        public int GetInitiativeTotal(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return int.MinValue;
            return unit.Stats.InitiativeRollTotal != int.MinValue
                ? unit.Stats.InitiativeRollTotal
                : int.MinValue;
        }

        public DiceType GetInitiativeDieFor(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return DiceType.D2;
            return unit.Stats.InitiativeRollTotal != int.MinValue
                ? unit.Stats.InitiativeDie
                : unit.Stats.GetInitiativeDie();
        }

        public static bool IsMultiplayerPlayerClient()
        {
            var room = Killtime.Multi.VTTRoomManager.Instance;
            return room != null && room.InRoom && !room.IsGM;
        }

        public static bool IsMultiplayerGM()
        {
            var room = Killtime.Multi.VTTRoomManager.Instance;
            return room != null && room.InRoom && room.IsGM;
        }

        private void Start()
        {
            if (IsMultiplayerPlayerClient()) return;
            if (_allUnits.Count > 0 && SystemMode == TurnSystemMode.CombatTurnBased)
            {
                StartNewRound();
            }
        }

        public void SetExplorationMode(bool active, TacticalUnit defaultUnit = null)
        {
            var ai = FindAnyObjectByType<Killtime.Tactics.AI.TacticalAIController>();

            if (active)
            {
                SystemMode = TurnSystemMode.Exploration;
                IsCombatOver = false;
                CurrentOutcome = CombatOutcome.InProgress;

                if (ai != null)
                {
                    ai.StopAITurn();
                }

                for (int i = 0; i < _allUnits.Count; i++)
                {
                    var u = _allUnits[i];
                    if (u != null && u.Stats != null)
                    {
                        u.Stats.CurrentActionPoints = u.Stats.MaxActionPoints;
                    }
                }

                if (defaultUnit != null && _allUnits.Contains(defaultUnit))
                {
                    ActiveUnit = defaultUnit;
                }
                else if (ActiveUnit == null && _allUnits.Count > 0)
                {
                    ActiveUnit = _allUnits.Find(u => u != null && u.IsPlayerControlled) ?? _allUnits[0];
                }

                if (ActiveUnit != null)
                {
                    OnTurnStarted?.Invoke(ActiveUnit);
                }

                if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                {
                    Killtime.Audio.KilltimeAudioManager.Instance.PlayMusic(Killtime.Audio.MusicMood.Explore, Killtime.Audio.MusicIntensity.Calm, true);
                }
            }
            else
            {
                EnterCombatMode();
            }
        }

        public void EnterCombatMode(TacticalUnit triggeringUnit = null)
        {
            if (SystemMode == TurnSystemMode.CombatTurnBased && !IsCombatOver) return;

            SystemMode = TurnSystemMode.CombatTurnBased;
            IsCombatOver = false;
            CurrentOutcome = CombatOutcome.InProgress;
            _needsInitiativeRoll = true;
            CurrentRound = 1;
            ResetDuoTechTracking();

            var ai = FindAnyObjectByType<Killtime.Tactics.AI.TacticalAIController>();
            if (ai != null)
            {
                ai.enabled = true;
                ai.LoadDevUIPrefs();
            }

            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
            {
                Killtime.Audio.KilltimeAudioManager.Instance.Play(Killtime.Audio.SoundId.Round_Start, 0.9f);
                Killtime.Audio.KilltimeAudioManager.Instance.PlayMusic(Killtime.Audio.MusicMood.Combat, Killtime.Audio.MusicIntensity.Intense, true);
            }

            StartNewRound();

            if (triggeringUnit != null && _allUnits.Contains(triggeringUnit))
            {
                int triggerIdx = _allUnits.IndexOf(triggeringUnit);
                if (triggerIdx >= 0)
                {
                    _activeUnitIndex = triggerIdx;
                    ActiveUnit = triggeringUnit;
                    OnTurnStarted?.Invoke(ActiveUnit);
                }
            }

            if (ActiveUnit != null)
            {
                ai?.TriggerAITurnIfApplicable();
            }
        }

        public void SetActiveUnitExplicit(TacticalUnit unit)
        {
            if (unit == null || !_allUnits.Contains(unit)) return;
            ActiveUnit = unit;
            _activeUnitIndex = _allUnits.IndexOf(unit);
            OnTurnStarted?.Invoke(ActiveUnit);
        }

        public void RegisterUnit(TacticalUnit unit)
        {
            if (unit == null || _allUnits.Contains(unit)) return;

            _allUnits.Add(unit);

            if (unit.Stats != null && unit.Stats.IsAlive)
            {
                unit.Stats.ResetTurn();
            }

            if (IsMultiplayerPlayerClient())
            {
                return;
            }

            if (IsInExploration)
            {
                if (ActiveUnit == null && unit.IsPlayerControlled)
                {
                    ActiveUnit = unit;
                    OnTurnStarted?.Invoke(ActiveUnit);
                }
                return;
            }

            if (IsCombatOver)
            {
                IsCombatOver = false;
                CurrentOutcome = CombatOutcome.InProgress;
                CurrentRound = 1;
                _needsInitiativeRoll = true;
                if (!SuspendAutoStart) StartNewRound();
                return;
            }

            if (ActiveUnit == null || !_allUnits.Contains(ActiveUnit))
            {
                if (!SuspendAutoStart) StartNewRound();
                return;
            }

            InsertUnitIntoCurrentRound(unit);
        }   

        private void InsertUnitIntoCurrentRound(TacticalUnit newUnit)
        {
            int currentIndex = _allUnits.IndexOf(ActiveUnit);
            if (currentIndex < 0) return;

            // Renfort mid-combat (choix mixte) : pas de re-jet général.
            // On roule l'initiative du nouvel arrivant puis on l'insère parmi les
            // unités restantes selon (initiative, puis RAP en tie-break).
            EnsureInitiativeFor(newUnit);

            int remainingStart = currentIndex + 1;
            int remainingCount = _allUnits.Count - remainingStart;

            if (remainingCount > 1)
            {
                _allUnits.Sort(remainingStart, remainingCount, Comparer<TacticalUnit>.Create((a, b) =>
                {
                    int initA = GetInitiativeTotal(a);
                    int initB = GetInitiativeTotal(b);
                    if (initA != initB) return initB.CompareTo(initA);
                    int rapA = (a != null && a.Stats != null) ? a.Stats.Attributes.Rapidite : 0;
                    int rapB = (b != null && b.Stats != null) ? b.Stats.Attributes.Rapidite : 0;
                    return rapB.CompareTo(rapA);
                }));
            }

            _activeUnitIndex = _allUnits.IndexOf(ActiveUnit);
        }

        /// <summary>
        /// Roule l'initiative d'une unité si absente des relevés (renfort mid-combat).
        /// Résultat stocké sur unit.Stats : il meurt avec l'unité, aucune purge nécessaire.
        /// </summary>
        private void EnsureInitiativeFor(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return;
            if (HasInitiativeRolled(unit)) return;

            var die = unit.Stats.GetInitiativeDie();
            unit.Stats.InitiativeDie = die;
            unit.Stats.InitiativeRollTotal = _diceRoller.Roll(die, 0, 0).RawRoll;
        }

        public void ClearUnits()
        {
            _allUnits.Clear();
            ActiveUnit = null;
            _activeUnitIndex = 0;
            _needsInitiativeRoll = true;
        }

        public void UnregisterUnit(TacticalUnit unit)
        {
            if (unit == null) return;

            bool wasActive = (ActiveUnit == unit);
            int idx = _allUnits.IndexOf(unit);
            if (idx >= 0)
            {
                _allUnits.RemoveAt(idx);
            }

            if (CheckCombatOver()) return;

            if (wasActive)
            {
                _activeUnitIndex = idx;
                while (_activeUnitIndex < _allUnits.Count
                    && (_allUnits[_activeUnitIndex] == null
                        || _allUnits[_activeUnitIndex].Stats == null
                        || !_allUnits[_activeUnitIndex].Stats.IsAlive))
                {
                    _activeUnitIndex++;
                }

                if (_activeUnitIndex >= _allUnits.Count)
                {
                    CurrentRound++;
                    StartNewRound();
                }
                else
                {
                    StartUnitTurn();
                }
            }
            else if (ActiveUnit != null)
            {
                _activeUnitIndex = _allUnits.IndexOf(ActiveUnit);
            }
        }

        public void ResetCombatState()
        {
            IsCombatOver = false;
            CurrentOutcome = CombatOutcome.InProgress;
            CurrentRound = 1;
            _activeUnitIndex = 0;
            _allUnits.RemoveAll(u => u == null);
            _needsInitiativeRoll = true;
            SystemMode = TurnSystemMode.Exploration;
            ResetDuoTechTracking();
            // Nouveau combat (ré)initialisé : aucun objet au sol (gourdin...) ne survit.
            try { DroppedWeaponPickup.ClearAllDropped(); } catch { }
        }

        /// <summary>
        /// Nouveau combat = compteurs Duo-Tech remis à zéro (limite "1 duo par
        /// personnage par round" repart à chaque affrontement).
        /// </summary>
        private void ResetDuoTechTracking()
        {
            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u != null && u.Stats != null)
                    u.Stats.LastDuoTechRound = 0;
            }
        }

        /// <summary>
        /// Force un nouveau jet d'initiative général (ex: bouton dev, nouveau combat).
        /// </summary>
        public void RerollInitiative()
        {
            _needsInitiativeRoll = true;
            StartNewRound();
        }

        /// <summary>
        /// Jet d'initiative général (Livre I §4.3) : chaque unité lance son dé
        /// (max RAP/AGI/INT → paliers), tri décroissant, tie-break RAP puis AGI/INT.
        /// Résultats stockés sur unit.Stats ; l'ordre reste figé pour les rounds suivants.
        /// </summary>
        private void RollInitiativeAndSort()
        {
            _allUnits.RemoveAll(u => u == null);

            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u == null || u.Stats == null) continue;
                var die = u.Stats.GetInitiativeDie();
                u.Stats.InitiativeDie = die;
                u.Stats.InitiativeRollTotal = _diceRoller.Roll(die, 0, 0).RawRoll;
            }

            _allUnits.Sort((a, b) =>
            {
                if (a == null || a.Stats == null) return 1;
                if (b == null || b.Stats == null) return -1;

                bool aIsTitan = a.FootprintType != TitanFootprintType.Single;
                bool bIsTitan = b.FootprintType != TitanFootprintType.Single;
                if (aIsTitan != bIsTitan) return bIsTitan.CompareTo(aIsTitan);

                int initA = GetInitiativeTotal(a);
                int initB = GetInitiativeTotal(b);
                if (initA != initB) return initB.CompareTo(initA);
                int rapCmp = b.Stats.Attributes.Rapidite.CompareTo(a.Stats.Attributes.Rapidite);
                if (rapCmp != 0) return rapCmp;
                int agiCmp = b.Stats.Attributes.Agilite.CompareTo(a.Stats.Attributes.Agilite);
                if (agiCmp != 0) return agiCmp;
                int intCmp = b.Stats.Attributes.Intelligence.CompareTo(a.Stats.Attributes.Intelligence);
                if (intCmp != 0) return intCmp;
                return string.Compare(a.Stats.Name, b.Stats.Name, StringComparison.Ordinal);
            });

            _needsInitiativeRoll = false;

            var results = new List<InitiativeRollResult>(_allUnits.Count);
            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u == null || u.Stats == null) continue;
                results.Add(new InitiativeRollResult
                {
                    Unit = u,
                    Die = u.Stats.InitiativeDie,
                    RawRoll = u.Stats.InitiativeRollTotal,
                    Total = u.Stats.InitiativeRollTotal
                });
            }
            OnInitiativeRolled?.Invoke(results);
        }

        public bool CheckCombatOver()
        {
            if (IsInExploration) return false;
            if (IsCombatOver) return true;
            if (_allUnits.Count == 0) return false;

            int playerUnitsAlive = 0;
            int enemyUnitsAlive = 0;

            foreach (var unit in _allUnits)
            {
                if (unit == null || unit.Stats == null) continue;

                if (unit.Stats.IsAlive)
                {
                    if (unit.IsPlayerControlled)
                        playerUnitsAlive++;
                    else
                        enemyUnitsAlive++;
                }
            }

            if (playerUnitsAlive == 0 || enemyUnitsAlive == 0)
            {
                IsCombatOver = true;
                CurrentOutcome = (playerUnitsAlive > 0) ? CombatOutcome.Victory : CombatOutcome.Defeat;
                OnCombatEnded?.Invoke(CurrentOutcome);
                if (IsMultiplayerGM())
                {
                    var payload = new Killtime.Multi.VTTTurnControlPayload
                    {
                        action = "combat_ended",
                        round = CurrentRound,
                        outcome = CurrentOutcome.ToString()
                    };
                    Killtime.Multi.VTTTableSync.Instance?.BroadcastTurnControl(payload);
                }
                return true;
            }

            return false;
        }

        public void StartNewRound()
        {
            if (IsInExploration) return;
            _allUnits.RemoveAll(u => u == null);

            if (CheckCombatOver()) return;

            if (_needsInitiativeRoll || CurrentRound <= 1)
            {
                // Début de combat / reset : jet d'initiative général (Livre I §4.3).
                RollInitiativeAndSort();
            }
            else
            {
                // Rounds suivants : ordre conservé (pas de re-tri, pas de re-jet).
                // Les renforts sans relevé reçoivent un jet individuel (sans reshuffle).
                for (int i = 0; i < _allUnits.Count; i++)
                {
                    EnsureInitiativeFor(_allUnits[i]);
                }
            }

            _activeUnitIndex = 0;

            // Sauter immédiatement les unités mortes au début du round
            while (_activeUnitIndex < _allUnits.Count
                && (_allUnits[_activeUnitIndex] == null
                    || _allUnits[_activeUnitIndex].Stats == null
                    || !_allUnits[_activeUnitIndex].Stats.IsAlive))
            {
                _activeUnitIndex++;
            }

            if (_activeUnitIndex >= _allUnits.Count)
            {
                if (CheckCombatOver()) return;

                CurrentRound++;
                StartNewRound();
                return;
            }

            OnRoundStarted?.Invoke(CurrentRound);
            BroadcastRoundStartIfGM();
            StartUnitTurn();
        }

        public void EndCurrentTurn()
        {
            if (IsInExploration) return;
            if (CheckCombatOver()) return;

            if (ActiveUnit != null && ActiveUnit.Stats != null)
            {
                var expired = ActiveUnit.Stats.TickTurnStatusDurations();
                if (expired.Count > 0)
                {
                    var vis = ActiveUnit.GetComponent<TacticalUnitVisual>();
                    for (int i = 0; i < expired.Count; i++)
                    {
                        vis?.SpawnFloatingText($"[{expired[i]}] Dissipé", new Color(0.4f, 0.9f, 1.0f));
                    }
                    OnUnitStatusExpired?.Invoke(ActiveUnit, expired);
                }
            }

            if (IsMultiplayerGM() && ActiveUnit != null)
            {
                var payload = new Killtime.Multi.VTTTurnControlPayload
                {
                    action = "end_turn",
                    round = CurrentRound,
                    activeUnitId = ActiveUnit.gameObject.name
                };
                Killtime.Multi.VTTTableSync.Instance?.BroadcastTurnControl(payload);
            }

            _activeUnitIndex++;

            // Trouver la prochaine unité en vie
            while (_activeUnitIndex < _allUnits.Count
                && (_allUnits[_activeUnitIndex] == null
                    || _allUnits[_activeUnitIndex].Stats == null
                    || !_allUnits[_activeUnitIndex].Stats.IsAlive))
            {
                _activeUnitIndex++;
            }

            if (_activeUnitIndex >= _allUnits.Count)
            {
                if (CheckCombatOver()) return;

                CurrentRound++;
                StartNewRound();
            }
            else
            {
                StartUnitTurn();
            }
        }

        private void StartUnitTurn()
        {
            if (_activeUnitIndex < 0 || _activeUnitIndex >= _allUnits.Count) return;

            ActiveUnit = _allUnits[_activeUnitIndex];

            // Sécurité absolue : si l'unité est morte ou invalide, passer immédiatement le tour
            if (ActiveUnit == null || ActiveUnit.Stats == null || !ActiveUnit.Stats.IsAlive)
            {
                EndCurrentTurn();
                return;
            }

            RefreshAndNotifyActiveUnitTurnStart(ActiveUnit);

            OnTurnStarted?.Invoke(ActiveUnit);

            if (IsMultiplayerGM() && ActiveUnit != null)
            {
                var arena = FindAnyObjectByType<CombatDevArena>();
                var curTarget = arena != null ? arena.CurrentTarget : null;

                var payload = new Killtime.Multi.VTTTurnControlPayload
                {
                    action = "turn_start",
                    round = CurrentRound,
                    activeUnitId = ActiveUnit.gameObject.name,
                    targetUnitId = curTarget != null ? curTarget.gameObject.name : "",
                    targetQ = curTarget != null ? curTarget.CurrentCoords.Q : 0,
                    targetR = curTarget != null ? curTarget.CurrentCoords.R : 0
                };
                Killtime.Multi.VTTTableSync.Instance?.BroadcastTurnControl(payload);
            }
        }

        /// <summary>
        /// Refresh individuel (Livres I §4 + VI §24) : PA remis au max AU DÉBUT
        /// DU TOUR PERSONNEL de l'unité active. Aucun refresh global au début
        /// du round : StartNewRound ne recharge personne, seul StartUnitTurn
        /// recharge l'unité dont l'initiative arrive. L'Essoufflement (Souffle)
        /// n'est PAS effacé ici : il se récupère via l'action Reprendre son
        /// Souffle (1 PA = 1 PE) jouée en début de son propre tour.
        /// </summary>
        private void RefreshAndNotifyActiveUnitTurnStart(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null || !unit.Stats.IsAlive) return;

            int prevAP = unit.Stats.CurrentActionPoints;
            unit.Stats.ResetTurn();

            if (unit.FootprintType == TitanFootprintType.Rosette7 || unit.FootprintType == TitanFootprintType.Colossus19)
            {
                unit.Stats.CurrentActionPoints = unit.Stats.MaxActionPoints * 2;
            }

            // Techniques de spécialisation (Livre III) : vieillissement des marques
            // (failles exposées, provocations) et expiration des bonus de ligne
            // « Tenir la Ligne ! » du donneur qui rejoue son tour.
            Killtime.Core.Combat.SkillTechniqueState.OnUnitTurnStart(unit.Stats);

            int newAP = unit.Stats.CurrentActionPoints;
            int deltaAP = newAP - prevAP;

            var visual = unit.GetComponent<TacticalUnitVisual>();
            if (visual != null)
            {
                if (deltaAP > 0)
                {
                    visual.SpawnFloatingText($"⚡ +{deltaAP} PA ({newAP}/{unit.Stats.MaxActionPoints})", new Color(0.2f, 0.95f, 1.0f));
                }
                else
                {
                    visual.SpawnFloatingText($"⚡ PA COMPLETS ({newAP})", new Color(0.2f, 0.85f, 1.0f, 0.75f));
                }

                if (unit.Stats.Essoufflement > 0)
                {
                    visual.SpawnFloatingText($"🫁 SOUFFLE : {unit.Stats.Essoufflement} PE", new Color(1.0f, 0.72f, 0.15f));
                }
            }
        }

        private void BroadcastRoundStartIfGM()
        {
            if (!IsMultiplayerGM()) return;
            var entries = new List<Killtime.Multi.VTTInitiativeEntry>(_allUnits.Count);
            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u == null || u.Stats == null) continue;
                entries.Add(new Killtime.Multi.VTTInitiativeEntry
                {
                    unitId = u.gameObject.name,
                    die = u.Stats.InitiativeDie.ToString(),
                    rawRoll = u.Stats.InitiativeRollTotal,
                    total = u.Stats.InitiativeRollTotal,
                    rapidite = u.Stats.Attributes.Rapidite,
                    agilite = u.Stats.Attributes.Agilite,
                    intelligence = u.Stats.Attributes.Intelligence
                });
            }

            var payload = new Killtime.Multi.VTTTurnControlPayload
            {
                action = "round_start",
                round = CurrentRound,
                turnOrder = entries
            };
            Killtime.Multi.VTTTableSync.Instance?.BroadcastTurnControl(payload);
        }

        public void ApplyRemoteRoundStart(int round, List<Killtime.Multi.VTTInitiativeEntry> entries)
        {
            CurrentRound = round;
            IsCombatOver = false;
            CurrentOutcome = CombatOutcome.InProgress;
            _needsInitiativeRoll = false;

            if (entries != null && entries.Count > 0)
            {
                var map = new Dictionary<string, Killtime.Multi.VTTInitiativeEntry>();
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] != null && !string.IsNullOrEmpty(entries[i].unitId))
                    {
                        map[entries[i].unitId] = entries[i];
                    }
                }

                for (int i = 0; i < _allUnits.Count; i++)
                {
                    var u = _allUnits[i];
                    if (u != null && u.Stats != null && map.TryGetValue(u.gameObject.name, out var entry))
                    {
                        if (Enum.TryParse<DiceType>(entry.die, out var d))
                        {
                            u.Stats.InitiativeDie = d;
                        }
                        u.Stats.InitiativeRollTotal = entry.total;
                    }
                }

                _allUnits.Sort((a, b) =>
                {
                    if (a == null) return 1;
                    if (b == null) return -1;
                    int initA = GetInitiativeTotal(a);
                    int initB = GetInitiativeTotal(b);
                    if (initA != initB) return initB.CompareTo(initA);
                    int rapA = (a.Stats != null) ? a.Stats.Attributes.Rapidite : 0;
                    int rapB = (b.Stats != null) ? b.Stats.Attributes.Rapidite : 0;
                    return rapB.CompareTo(rapA);
                });
            }

            OnRoundStarted?.Invoke(CurrentRound);
        }

        public void ApplyRemoteTurnStart(string activeUnitId)
        {
            if (string.IsNullOrEmpty(activeUnitId)) return;
            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u != null && u.gameObject.name == activeUnitId)
                {
                    _activeUnitIndex = i;
                    ActiveUnit = u;
                    RefreshAndNotifyActiveUnitTurnStart(ActiveUnit);
                    OnTurnStarted?.Invoke(ActiveUnit);
                    return;
                }
            }
        }

        public void ApplyRemoteEndTurn(string activeUnitId)
        {
            if (ActiveUnit != null && ActiveUnit.Stats != null)
            {
                var expired = ActiveUnit.Stats.TickTurnStatusDurations();
                if (expired.Count > 0)
                {
                    var vis = ActiveUnit.GetComponent<TacticalUnitVisual>();
                    for (int i = 0; i < expired.Count; i++)
                    {
                        vis?.SpawnFloatingText($"[{expired[i]}] Dissipé", new Color(0.4f, 0.9f, 1.0f));
                    }
                    OnUnitStatusExpired?.Invoke(ActiveUnit, expired);
                }
            }
        }

        public void ApplyRemoteCombatEnded(string outcomeStr)
        {
            IsCombatOver = true;
            if (Enum.TryParse<CombatOutcome>(outcomeStr, out var outcome))
            {
                CurrentOutcome = outcome;
            }
            else
            {
                CurrentOutcome = CombatOutcome.InProgress;
            }
            OnCombatEnded?.Invoke(CurrentOutcome);
        }

        public void ApplyRemoteStateSync(int round, string activeUnitId, string outcomeStr)
        {
            if (round > 0) CurrentRound = round;
            if (!string.IsNullOrEmpty(activeUnitId))
            {
                for (int i = 0; i < _allUnits.Count; i++)
                {
                    if (_allUnits[i] != null && _allUnits[i].gameObject.name == activeUnitId)
                    {
                        ActiveUnit = _allUnits[i];
                        _activeUnitIndex = i;
                        break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(outcomeStr) && Enum.TryParse<CombatOutcome>(outcomeStr, out var outcome))
            {
                CurrentOutcome = outcome;
                IsCombatOver = (outcome != CombatOutcome.InProgress);
            }
        }
    }
}
