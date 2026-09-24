// <copyright file="FogOfWarManager.cs" company="Killtime Tactics">
// Brouillard de guerre asymétrique — chef d'orchestre Unity.
// Solo : l'escouade (unités IsPlayerControlled vivantes) voit à 360° jusqu'à
//   la portée d'ambiance ; les cases hors vue ou derrière un mur Full sont
//   assombries (HexGridVisualizer) et les ennemis non détectés sont masqués
//   (TacticalUnitVisual.SetHiddenByFog).
// VTT joueur : même masque mais calculé depuis MES avatars (unit_claim) ; le
//   serveur filtre en outre les paquets unit_move (anti map-hack mémoire).
// Le MJ (GM) voit toujours tout (arbitre).
// </copyright>
using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Multi;

namespace Killtime.Tactics.Visibility
{
    /// <summary>
    /// Portée de vue environnementale (Livre VI §25.4) : la distance de vision
    /// ne dépend jamais des stats — courte portée en intérieur / obstacles
    /// denses, longue portée en extérieur dégagé (météo claire).
    /// </summary>
    public enum FogRangePreset
    {
        CourtePortee = 0,  // Intérieur, obstacles denses : 6 cases.
        LonguePortee = 1,  // Extérieur dégagé, météo claire : 12 cases.
    }

    /// <summary>État de détection d'un ennemi par l'escouade locale.</summary>
    [Serializable]
    public class FogDetectionEntry
    {
        public string UnitId;
        public HexCoordinates LastKnownCoords;
        public bool EverDetected;
        public int NoiseRevealUntilRound = -1;   // révélation au bruit (tirs)
        public int ManualRevealUntilRound = -1;  // jet d'Observation réussi
    }

    /// <summary>
    /// Gestionnaire du brouillard de guerre. Un seul par scène (singleton souple).
    /// Rafraîchit le masque sur : déplacement / téléportation / début de tour /
    /// round / chargement de carte / op VTT / tick lent (0.3 s, capteurs de
    /// rotation).
    /// </summary>
    [DisallowMultipleComponent]
    public class FogOfWarManager : MonoBehaviour
    {
        public static FogOfWarManager Instance { get; private set; }

        /// <summary>
        /// Amorçage automatique : garantit un gestionnaire par scène de combat
        /// sans édition manuelle de la scène. Si la scène contient déjà un
        /// FogOfWarManager (réglages d'inspecteur), il fait foi.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            try
            {
                if (Instance != null) return;
                if (FindAnyObjectByType<FogOfWarManager>() != null) return;
                var go = new GameObject("[KT] FogOfWar");
                go.AddComponent<FogOfWarManager>();
            }
            catch { /* fog optionnel */ }
        }

        [Header("Brouillard de guerre")]
        [Tooltip("Interrupteur global. À faux : tout est visible (comportement historique).")]
        [SerializeField] private bool _fogEnabled = true;
        [Tooltip("Le MJ voit toujours tout, même avec le brouillard actif.")]
        [SerializeField] private bool _gmSeesAll = true;
        [Tooltip("Coût en PA d'une tentative d'Observation active contre une cible.")]
        [SerializeField] private int _observationApCost = 1;

        [Header("Portée de Vue (Environnement, Livre VI §25.4)")]
        [Tooltip("Preset d'ambiance : Courte (intérieur/obstacles, 6 cases) ou Longue (extérieur/météo claire, 12 cases).")]
        [SerializeField] private FogRangePreset _rangePreset = FogRangePreset.LonguePortee;
        [Tooltip("Portée personnalisée (cases). -1 = utilise le preset. Le MJ peut imposer la sienne à la table VTT.")]
        [SerializeField] private int _customSightRange = -1;

        /// <summary>
        /// Portée effective (cases) : ordre MJ reçu en VTT > personnalisée > preset.
        /// Jamais issue des stats (Vision = jets de compétences).
        /// </summary>
        public int CurrentSightRange
        {
            get
            {
                if (_gmSightRangeOverride > 0) return _gmSightRangeOverride;
                if (_customSightRange > 0) return _customSightRange;
                return _rangePreset == FogRangePreset.CourtePortee
                    ? FogOfWarSystem.ShortSightRange
                    : FogOfWarSystem.LongSightRange;
            }
        }

        private int _gmSightRangeOverride = -1;

        /// <summary>Le MJ impose la portée d'ambiance à la table (reçue via state_sync).</summary>
        public void ApplyGMSightRange(int sightRange)
        {
            _gmSightRangeOverride = sightRange > 0 ? sightRange : -1;
            RefreshFog("gm-range");
        }

        /// <summary>Avatars possédés par CE client en VTT (unit_claim). Vide = repli solo.</summary>
        public HashSet<string> LocalClaimedUnitIds { get; } = new(StringComparer.Ordinal);

        private readonly Dictionary<string, FogDetectionEntry> _detections = new(StringComparer.Ordinal);
        private TacticalHexGrid _grid;
        private HexGridVisualizer _visualizer;
        private TurnManager _turnManager;
        private CombatCalculator _calc;
        private float _nextSlowRefresh;
        // PERF : le tick de sécurité ne recalcule que si marqué dirty.
        private bool _fogDirty = true;
        // Cache visuels (évite GetComponent par ennemi à chaque refresh).
        private readonly Dictionary<TacticalUnit, TacticalUnitVisual> _visualCache = new();
        private int _visualCacheUnitCount = -1;

        public bool FogEnabled
        {
            get => _fogEnabled;
            set { _fogEnabled = value; RefreshFog("toggle"); }
        }

        public IReadOnlyDictionary<string, FogDetectionEntry> Detections => _detections;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _calc = new CombatCalculator(new DiceRoller());
            EnsureDependencies();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            EnsureDependencies();
            TacticalUnit.OnAnyUnitMoved += HandleUnitMoved;
            TacticalUnit.OnAnyUnitTeleported += HandleUnitTeleported;
            if (_turnManager != null)
            {
                _turnManager.OnTurnStarted += HandleTurnStarted;
                _turnManager.OnRoundStarted += HandleRoundStarted;
            }
            SubscribeVtt();
        }

        private bool _vttSubscribed;

        private void SubscribeVtt()
        {
            if (_vttSubscribed) return;
            var sync = VTTTableSync.Instance ?? FindAnyObjectByType<VTTTableSync>();
            if (sync == null) return;
            sync.OnRemoteMoveApplied += HandleRemoteVttChanged;
            sync.OnRemoteCombatActionApplied += HandleRemoteCombatAction;
            sync.OnRemoteMapApplied += HandleRemoteMapChanged;
            sync.OnRemoteTurnControlApplied += HandleRemoteTurnControl;
            _vttSubscribed = true;
        }

        private void UnsubscribeVtt()
        {
            if (!_vttSubscribed) return;
            _vttSubscribed = false;
            try
            {
                var sync = VTTTableSync.Instance ?? FindAnyObjectByType<VTTTableSync>();
                if (sync == null) return;
                sync.OnRemoteMoveApplied -= HandleRemoteVttChanged;
                sync.OnRemoteCombatActionApplied -= HandleRemoteCombatAction;
                sync.OnRemoteMapApplied -= HandleRemoteMapChanged;
                sync.OnRemoteTurnControlApplied -= HandleRemoteTurnControl;
            }
            catch { /* ignore */ }
        }

        private void OnDisable()
        {
            TacticalUnit.OnAnyUnitMoved -= HandleUnitMoved;
            TacticalUnit.OnAnyUnitTeleported -= HandleUnitTeleported;
            if (_turnManager != null)
            {
                _turnManager.OnTurnStarted -= HandleTurnStarted;
                _turnManager.OnRoundStarted -= HandleRoundStarted;
            }
            UnsubscribeVtt();
            // Lève tout masque à la destruction pour ne jamais laisser la scène aveugle.
            try { _visualizer?.ClearFogOfWar(); } catch { /* ignore */ }
        }

        private void Start()
        {
            SubscribeVtt(); // le TableSync peut apparaître après OnEnable.
            RefreshFog("start");
        }

        private void EnsureDependencies()
        {
            if (_grid == null) _grid = GetComponent<TacticalHexGrid>() ?? FindAnyObjectByType<TacticalHexGrid>();
            if (_visualizer == null) _visualizer = GetComponent<HexGridVisualizer>() ?? FindAnyObjectByType<HexGridVisualizer>();
            if (_turnManager == null) _turnManager = GetComponent<TurnManager>() ?? FindAnyObjectByType<TurnManager>();
            // Réabonnement tardif si le TurnManager est apparu après OnEnable.
            if (_turnManager != null && isActiveAndEnabled)
            {
                _turnManager.OnTurnStarted -= HandleTurnStarted;
                _turnManager.OnRoundStarted -= HandleRoundStarted;
                _turnManager.OnTurnStarted += HandleTurnStarted;
                _turnManager.OnRoundStarted += HandleRoundStarted;
            }
        }

        private void Update()
        {
            // Filet de sécurité LENT (poses/retraits d'obstacles, spawns sans
            // événement) : ne recalcule que si marqué dirty — le 360° KT rend
            // le suivi de cap inutile, aucun recalcul par frame/0,3 s.
            if (Time.unscaledTime >= _nextSlowRefresh)
            {
                _nextSlowRefresh = Time.unscaledTime + 2.0f;
                RefreshFog("tick");
            }
        }

        // ------------------------------------------------------------------
        // Déclencheurs
        // ------------------------------------------------------------------

        private void HandleUnitMoved(TacticalUnit unit, List<HexCoordinates> path, int apCost) => RefreshFog("move");
        private void HandleUnitTeleported(TacticalUnit unit, HexCoordinates coords) => RefreshFog("teleport");
        private void HandleTurnStarted(TacticalUnit unit) => RefreshFog("turn");
        private void HandleRoundStarted(int round) => RefreshFog("round");
        private void HandleRemoteVttChanged(TacticalUnit unit, string from, string role) => RefreshFog("vtt");
        private void HandleRemoteMapChanged(string mapName, string from) => RefreshFog("map");
        private void HandleRemoteTurnControl(VTTTurnControlPayload payload) => RefreshFog("turn-sync");

        private void HandleRemoteCombatAction(VTTCombatActionPayload p)
        {
            if (p == null) return;
            // Un tir / grenade / sort distant révèle son auteur (bruit).
            if (p.action == "attack" || p.action == "grenade" || p.action == "spell")
            {
                var sync = VTTTableSync.Instance;
                var shooter = sync != null ? sync.FindUnit(p.actorId) : null;
                if (shooter != null) ApplyNoiseReveal(shooter);
            }
            RefreshFog("vtt-combat");
        }

        // ------------------------------------------------------------------
        // API publique : revendication VTT, bruit, observation
        // ------------------------------------------------------------------

        /// <summary>
        /// Déclare les avatars possédés par CE client (miroir du unit_claim serveur).
        /// </summary>
        public void ClaimLocalUnits(IEnumerable<string> unitIds)
        {
            LocalClaimedUnitIds.Clear();
            if (unitIds == null) return;
            foreach (var id in unitIds)
                if (!string.IsNullOrEmpty(id)) LocalClaimedUnitIds.Add(id);
            RefreshFog("claim");
        }

        /// <summary>
        /// Révélation au bruit : un tir bruyant expose le tireur aux ennemis dans
        /// son rayon d'Ouïe jusqu'à la fin du round suivant (sauf mur Full).
        /// Appelé par l'arène après chaque attaque / grenade locale.
        /// </summary>
        public void NotifyLoudShot(TacticalUnit shooter)
        {
            if (shooter == null) return;
            ApplyNoiseReveal(shooter);
            RefreshFog("noise");
        }

        private void ApplyNoiseReveal(TacticalUnit shooter)
        {
            if (shooter?.Stats == null) return;
            int round = _turnManager != null ? _turnManager.CurrentRound : 1;
            string id = shooter.gameObject.name;
            if (!_detections.TryGetValue(id, out var entry))
            {
                entry = new FogDetectionEntry { UnitId = id };
                _detections[id] = entry;
            }
            entry.LastKnownCoords = shooter.CurrentCoords;
            entry.EverDetected = true;
            entry.NoiseRevealUntilRound = round + FogOfWarSystem.NoiseRevealExtraRounds;
        }

        /// <summary>
        /// Tentative d'Observation active : duel Observation (observateur) vs
        /// Discretion (cible). Coûte _observationApCost PA (perdus même en cas
        /// d'échec). Réussite => cible révélée jusqu'à la fin du round suivant
        /// (jamais à travers un mur Full, sauf Thermique). Progression organique
        /// gérée par le calculateur.
        /// </summary>
        public bool TryActiveObservation(TacticalUnit observer, TacticalUnit target, out string log)
        {
            log = "";
            if (observer == null || target == null || observer.Stats == null || target.Stats == null)
            {
                log = "⚠️ Observation impossible : observateur ou cible invalide.";
                return false;
            }
            if (_grid == null) EnsureDependencies();
            if (!observer.Stats.ConsumeActionPoints(_observationApCost))
            {
                log = $"⚠️ {observer.Stats.Name} n'a pas assez de PA pour observer (coût {_observationApCost}).";
                return false;
            }
            int dist = observer.CurrentCoords.DistanceTo(target.CurrentCoords);
            CoverType cover = _grid != null
                ? CoverSystem.EvaluateCover(observer.CurrentCoords, target.CurrentCoords, _grid)
                : CoverType.None;
            bool thermal = false;
            try { thermal = observer.Stats.HasSpecialization(FogOfWarSystem.SpecThermal); } catch { /* ignore */ }
            if (cover == CoverType.Full && !thermal)
            {
                log = $"👁️ {observer.Stats.Name} observe {target.Stats.Name} : <b>mur total — observation impossible</b> (PA perdu).";
                RefreshFog("observation");
                return false;
            }
            var duel = FogOfWarSystem.ResolveDetectionDuel(_calc, observer.Stats, target.Stats, dist, cover);
            bool success = duel != null && duel.AttackerWins;
            log = duel != null ? duel.CombatLog : "⚠️ Duel de détection illisible.";
            if (success)
            {
                int round = _turnManager != null ? _turnManager.CurrentRound : 1;
                string id = target.gameObject.name;
                if (!_detections.TryGetValue(id, out var entry))
                {
                    entry = new FogDetectionEntry { UnitId = id };
                    _detections[id] = entry;
                }
                entry.LastKnownCoords = target.CurrentCoords;
                entry.EverDetected = true;
                entry.ManualRevealUntilRound = round + FogOfWarSystem.NoiseRevealExtraRounds;
                log += $"\n👁️ <b>{target.Stats.Name} REPÉRÉ</b> (révélé jusqu'à la fin du round {entry.ManualRevealUntilRound}).";
            }
            else
            {
                log += $"\n👁️ <b>{target.Stats.Name} reste invisible</b> (jet d'Observation manqué).";
            }
            RefreshFog("observation");
            return success;
        }

        // ------------------------------------------------------------------
        // Calcul du masque
        // ------------------------------------------------------------------

        /// <summary>
        /// Vrai si le client local est MJ (voit tout) — jamais de masque.
        /// </summary>
        public bool LocalSeesAll()
        {
            if (!_fogEnabled) return true;
            if (_gmSeesAll)
            {
                try
                {
                    var room = VTTRoomManager.Instance;
                    if (room != null && room.InRoom && room.IsGM) return true;
                    if (TurnManager.IsMultiplayerGM()) return true;
                }
                catch { /* ignore */ }
            }
            return false;
        }

        /// <summary>Observateurs locaux : escouade solo ou avatars revendiqués en VTT.
        /// PERF : tableau pré-récupéré optionnel (un seul FindObjectsByType par refresh).</summary>
        public List<TacticalUnit> GetLocalObservers(TacticalUnit[] all = null)
        {
            var list = new List<TacticalUnit>();
            all ??= FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
            bool useClaims = LocalClaimedUnitIds.Count > 0;
            for (int i = 0; i < all.Length; i++)
            {
                var u = all[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                if (useClaims)
                {
                    if (LocalClaimedUnitIds.Contains(u.gameObject.name)) list.Add(u);
                }
                else if (u.IsPlayerControlled)
                {
                    list.Add(u);
                }
            }
            // Garde-fou : jamais d'escouade vide => repli solo (tout IsPlayer).
            if (useClaims && list.Count == 0)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    var u = all[i];
                    if (u != null && u.Stats != null && u.Stats.IsAlive && u.IsPlayerControlled)
                        list.Add(u);
                }
            }
            return list;
        }

        /// <summary>
        /// Ennemis pour le client local : toute unité vivante qui n'est pas un
        /// observateur local (en solo : IsPlayerControlled == false).
        /// </summary>
        public List<TacticalUnit> GetLocalEnemies(TacticalUnit[] all = null, HashSet<TacticalUnit> observerSet = null)
        {
            observerSet ??= new HashSet<TacticalUnit>(GetLocalObservers(all));
            var list = new List<TacticalUnit>();
            all ??= FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
            {
                var u = all[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                if (observerSet.Contains(u)) continue;
                // En solo strict, les alliés PJ non-observateurs restent visibles ;
                // en VTT avec claims, seules MES unités observent, les autres PJ sont
                // traités comme brouillés (asymétrique par client).
                if (LocalClaimedUnitIds.Count == 0 && u.IsPlayerControlled) continue;
                list.Add(u);
            }
            return list;
        }

        public static float FacingYawDeg(TacticalUnit u)
        {
            if (u == null) return 0f;
            try
            {
                Vector3 fwd = u.transform.forward;
                if (fwd.sqrMagnitude < 1e-6f) return u.transform.rotation.eulerAngles.y;
                return Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            }
            catch { return 0f; }
        }

        /// <summary>
        /// Vrai si au moins un observateur local voit la cible à 360°
        /// (portée d'ambiance + murs). Le cap est ignoré (KT, 360°).
        /// </summary>
        public bool IsEnemyInAnySight(TacticalUnit target, List<TacticalUnit> observers = null)
        {
            if (target == null) return false;
            if (_grid == null) EnsureDependencies();
            if (_grid == null) return true; // sans grille : pas de masque.
            observers ??= GetLocalObservers();
            foreach (var obs in observers)
            {
                if (obs == null || obs.Stats == null) continue;
                var p = FogOfWarSystem.ResolveObserverParams(obs.Stats, CurrentSightRange);
                if (FogOfWarSystem.IsCellVisible(
                    obs.CurrentCoords, FacingYawDeg(obs), target.CurrentCoords,
                    c => _grid.GetNode(c), p, _grid.HexRadius))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Vrai si l'ennemi doit être affiché (champ 360° actuel OU révélation
        /// persistante bruit/observation non expirée avec LOS bruit : pas de mur Full).
        /// </summary>
        public bool IsEnemyVisible(TacticalUnit enemy, List<TacticalUnit> observers = null)
        {
            if (enemy == null) return false;
            if (LocalSeesAll()) return true;
            if (enemy.Stats == null || !enemy.Stats.IsAlive) return true; // cadavres visibles.
            if (IsEnemyInAnySight(enemy, observers)) return true;

            string id = enemy.gameObject.name;
            int round = _turnManager != null ? _turnManager.CurrentRound : 1;
            if (_detections.TryGetValue(id, out var entry))
            {
                bool persisted = entry.NoiseRevealUntilRound >= round
                    || entry.ManualRevealUntilRound >= round;
                if (persisted)
                {
                    // La persistance ne traverse jamais un mur Full : on vérifie
                    // qu'au moins un observateur a une ligne bruit (portée Ouïe).
                    if (IsNoiseAudible(enemy, observers)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Le tireur révélé au bruit est audible si un observateur est dans le
        /// rayon d'Ouïe SANS mur Full entre les deux (ligne fog à un rayon,
        /// pas le CoverSystem 28 rayons — PERF).
        /// </summary>
        private bool IsNoiseAudible(TacticalUnit noisy, List<TacticalUnit> observers = null)
        {
            if (_grid == null || noisy == null) return false;
            observers ??= GetLocalObservers();
            foreach (var obs in observers)
            {
                if (obs == null || obs.Stats == null) continue;
                int ouie = Mathf.Clamp(obs.Stats.Attributes.Ouie, 1, 6);
                int dist = obs.CurrentCoords.DistanceTo(noisy.CurrentCoords);
                if (dist > ouie && dist > CurrentSightRange)
                    continue;
                if (dist <= 1) return true;
                if (FogOfWarSystem.HasFogLineOfSight(obs.CurrentCoords, noisy.CurrentCoords,
                    c => _grid.GetNode(c), _grid.HexRadius, ignoreWalls: false)) return true;
            }
            return false;
        }

        /// <summary>Reconstruit le masque : tuiles assombries + ennemis masqués.
        /// PERF : le tick ne recalcule que si marqué dirty (les événements —
        /// déplacement, tour, round, carte, bruit, observation — rafraîchissent
        /// déjà en direct). La ligne de mire fog est à un rayon sans allocation.
        /// </summary>
        public void RefreshFog(string reason = "")
        {
            if (reason == "tick")
            {
                if (!_fogDirty) return;
            }
            else
            {
                _fogDirty = true;
            }
            if (_grid == null || _visualizer == null) EnsureDependencies();
            if (_grid == null || _visualizer == null) return;

            if (LocalSeesAll())
            {
                _visualizer.ClearFogOfWar();
                SetAllEnemiesHidden(false);
                return;
            }

            // PERF : un seul balayage d'unités par refresh, observateurs partagés.
            var all = FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
            RebuildVisualCacheIfNeeded(all);
            var observers = GetLocalObservers(all);
            var specs = new List<(HexCoordinates coords, float yawDeg, FogVisionParams vision)>(observers.Count);
            for (int i = 0; i < observers.Count; i++)
            {
                var o = observers[i];
                if (o == null || o.Stats == null) continue;
                specs.Add((o.CurrentCoords, FacingYawDeg(o), FogOfWarSystem.ResolveObserverParams(o.Stats, CurrentSightRange)));
            }
            // Phase de déploiement (aucun observateur vivant) : pas de masque,
            // sinon la carte serait noire avant même le placement des unités.
            if (specs.Count == 0)
            {
                _visualizer.ClearFogOfWar();
                SetAllEnemiesHidden(false);
                return;
            }
            var visible = FogOfWarSystem.ComputeSquadVisibleCells(
                specs, c => _grid.GetNode(c), _grid.HexRadius);
            _fogDirty = false;
            _visualizer.SetFogOfWar(visible, true);

            // Mémorise la dernière position connue des détectés encore visibles.
            var observerSet = new HashSet<TacticalUnit>(observers);
            var enemies = GetLocalEnemies(all, observerSet);
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == null) continue;
                bool vis = IsEnemyVisible(e, observers);
                if (!_visualCache.TryGetValue(e, out var rend) || rend == null)
                {
                    rend = e.GetComponent<TacticalUnitVisual>();
                    _visualCache[e] = rend;
                }
                rend?.SetHiddenByFog(!vis);
                if (vis)
                {
                    string id = e.gameObject.name;
                    if (!_detections.TryGetValue(id, out var entry))
                    {
                        entry = new FogDetectionEntry { UnitId = id };
                        _detections[id] = entry;
                    }
                    entry.LastKnownCoords = e.CurrentCoords;
                    entry.EverDetected = true;
                }
            }
        }

        private void SetAllEnemiesHidden(bool hidden)
        {
            try
            {
                var all = FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
                RebuildVisualCacheIfNeeded(all);
                for (int i = 0; i < all.Length; i++)
                {
                    var u = all[i];
                    if (u == null) continue;
                    if (!_visualCache.TryGetValue(u, out var vis) || vis == null)
                    {
                        vis = u.GetComponent<TacticalUnitVisual>();
                        _visualCache[u] = vis;
                    }
                    vis?.SetHiddenByFog(hidden);
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Invalide le cache visuels quand la liste d'unités change (spawn/destroy).
        /// </summary>
        private void RebuildVisualCacheIfNeeded(TacticalUnit[] all = null)
        {
            all ??= FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude);
            if (all.Length == _visualCacheUnitCount && _visualCache.Count > 0) return;
            _visualCache.Clear();
            for (int i = 0; i < all.Length; i++)
            {
                var u = all[i];
                if (u == null || _visualCache.ContainsKey(u)) continue;
                _visualCache[u] = u.GetComponent<TacticalUnitVisual>();
            }
            _visualCacheUnitCount = all.Length;
        }

        /// <summary>Dernière position connue d'un ennemi (fantôme HUD), null si jamais vu.</summary>
        public HexCoordinates? GetLastKnownPosition(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return null;
            if (_detections.TryGetValue(unitId, out var entry) && entry.EverDetected)
                return entry.LastKnownCoords;
            return null;
        }

        /// <summary>
        /// Révélation au bruit active ? (tirs bruyants : rayon d'Ouïe jusqu'à la
        /// fin du round suivant). Consulté aussi par l'IA pour la symétrie du
        /// brouillard : l'IA entend les tirs du joueur comme le joueur entend les siens.
        /// </summary>
        public bool IsNoiseActive(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return false;
            int round = _turnManager != null ? _turnManager.CurrentRound : 1;
            return _detections.TryGetValue(unitId, out var entry)
                && entry.NoiseRevealUntilRound >= round;
        }

        /// <summary>Jet d'Observation réussi encore actif ? (jusqu'à fin du round suivant).</summary>
        public bool IsManuallyRevealed(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return false;
            int round = _turnManager != null ? _turnManager.CurrentRound : 1;
            return _detections.TryGetValue(unitId, out var entry)
                && entry.ManualRevealUntilRound >= round;
        }
    }
}
