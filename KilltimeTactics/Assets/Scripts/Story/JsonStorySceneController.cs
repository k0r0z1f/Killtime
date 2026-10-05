using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.CameraSystem;
using Killtime.UI;
using Killtime.Audio;
using Killtime.Story.Data;

namespace Killtime.Story.Scenes
{
    public class JsonStorySceneController : MonoBehaviour, IStorySceneController
    {
        private TacticalHexGrid _grid;
        private TurnManager _turnManager;
        private CombatDevArena _arena;
        private ScenarioDirector _director;
        private StorySceneData _sceneData;

        private readonly Dictionary<string, TacticalUnit> _spawnedActors = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<TacticalUnit> _playerParty = new();
        private readonly List<TacticalInteractable> _interactables = new();
        private readonly HashSet<string> _firedTriggerIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activatedInteractableIds = new(StringComparer.OrdinalIgnoreCase);
        // Cartes à payload (événements, conséquences) déjà exécutées depuis l'entrée du
        // nœud courant. Évite les doubles payloads quand une chaîne "fin →" revient sur
        // une carte déjà résolue (ex : event rejoué à la fin de sa propre cinématique amont).
        // Vidé à chaque entrée de nœud (revenir = rejouer, comportement normal).
        private readonly HashSet<string> _resolvedCardIdsThisNode = new(StringComparer.OrdinalIgnoreCase);
        // Throttle session de l'erreur "CinematicDirector absent" (log jeu 1x, console à chaque fois).
        private static bool _warnedMissingCinematicDirector = false;
        // Nœud courant au moment de chaque activation : un trigger InteractableActivated
        // scoped sur un nœud source ne tire que si l'activation a eu lieu PENDANT ce
        // nœud (sinon une utilisation précoce au nœud précédent skippe instantanément
        // le nœud source dès son entrée). Triggers globaux (Source vide) : inchangés.
        private readonly Dictionary<string, string> _activationNodeIds = new(StringComparer.OrdinalIgnoreCase);
        // Positions du groupe à l'entrée du nœud courant : la validation par proximité
        // exige un déplacement (ou un hors-portée initial), sinon un spawn sur l'objectif
        // valide gratuitement dès la première frame et skippe le nœud.
        private readonly Dictionary<TacticalUnit, HexCoordinates> _nodeEnterCoords = new();

        private bool _isSceneInitialized = false;
        private int _currentDialogueIndex = 0;
        private string _lastNodeId = "";
        private float _chatAnimProgress = 0f;
        private ScenarioNode _lastActiveNode = null;
        private SceneDialogueChoiceData _activeReactionChoice = null;
        private SceneDialogueLineData _activeReactionPromptLine = null;
        private string _activeChallengeSummary = "";
        private string _activeChallengeRewards = "";
        // File d'attente des textes interactables vers le chat milieu :
        // si holotable + datapad se valident coup sur coup, le 2e ne doit pas
        // être perdu quand une réaction est déjà affichée.
        private readonly Queue<PendingMiddleChatMessage> _pendingMiddleChat = new();
        private struct PendingMiddleChatMessage
        {
            public string Speaker;
            public string StageDirection;
            public string Speech;
            public string ActionLabel;
            public string DisplayName;
            public string Rewards;
        }

        // Rects IMGUI du chat de scène : servent de bloqueurs clics 3D via FloatingWindowChrome.
        private Rect _dialogueRect = Rect.zero;
        private Rect _partyBarRect = Rect.zero;
        private bool _hasPartyBar;
        private readonly List<Rect> _interactButtonRects = new();

        // Popups monde ancrés aux interactables : le SuccessLog part dans le journal
        // latéral + dans un popup au-dessus de l'objet (jamais dans le chat central,
        // pour ne pas hijacker la conversation en cours). Info = fondu auto + ✕ manuel,
        // saut = persistant avec [Suivre ➔] / [Rester].
        private readonly List<InteractablePopup> _activePopups = new();
        private const float InteractablePopupAutoCloseDelay = 7f;
        private const float InteractablePopupFadeDuration = 1f;
        private sealed class InteractablePopup
        {
            public string InteractableId = "";
            public string DisplayName = "";
            public string Message = "";
            public string TargetNodeId = "";
            public bool HasJump;
            public float CreatedUnscaledTime;
            // Dernier rect dessiné (coordonnées GUI) : sert au survol qui met
            // le fondu auto en pause.
            public Rect LastScreenRect;
        }
        // Saut différé : un interactable avec TriggerNodeId activé pendant une
        // conversation ne saute plus immédiatement ; la cible attend ici jusqu'au
        // clic [Suivre] ou à la sortie naturelle du nœud.
        private string _pendingNodeJump = "";
        private string _pendingNodeJumpSource = "";
        // Réplique "Parler à" en attente après un saut de nœud : TalkToActor a
        // enclenché un nœud différent du courant pour jouer une réplique précise ;
        // consommée par UpdateNodeState une fois l'entrée du nœud rejouée.
        private string _pendingTalkLineId = "";
        // Point de retour "Parler à" : nœud (+ réplique) d'origine et nœud
        // d'atterrissage. À la fin naturelle du nœud d'atterrissage (bouton
        // "Passer à l'action" sans suite), on revient à la conversation
        // d'origine au lieu de clore la scène (cf. TryReturnFromTalk).
        private string _talkReturnNodeId = "";
        private string _talkReturnLineId = "";
        private string _talkLandingNodeId = "";
        private bool _isReturningFromTalk = false;
        // Boîtes d'info personnages : masquées par défaut en mode scène,
        // valeur précédente restaurée à la sortie (touche N ou dev UI pour forcer).
        private bool _overheadHudOverridden;
        private bool _savedOverheadHud = true;

        public StorySceneData SceneData => _sceneData;

        public void SetSceneData(StorySceneData data)
        {
            _sceneData = data;
        }

        public IReadOnlyDictionary<string, TacticalUnit> SpawnedActors => _spawnedActors;
        public IReadOnlyList<TacticalInteractable> SpawnedInteractables => _interactables;

        /// <summary>
        /// Déclenche les cinématiques liées à une carte en FIRE-AND-FORGET :
        /// retour immédiat, la carte suivante s'enclenche sans attendre la fin
        /// de la cinématique. Les ids inconnus sont ignorés avec un avertissement.
        /// À la FIN de chaque cinématique, sa sortie "fin →" (NextTargetId) est
        /// résolue via ExecuteCinematicChain (chaînage explicite : cine, nœud,
        /// réplique, conséquence ou événement). chainVisited casse les boucles A→B→A.
        /// </summary>
        public void FireLinkedCinematics(List<string> cinematicIds, HashSet<string> chainVisited = null)
        {
            if (cinematicIds == null || cinematicIds.Count == 0 || _sceneData == null) return;
            var director = FindAnyObjectByType<CinematicDirector>();
            if (director == null)
            {
                Debug.LogWarning("[JsonStorySceneController] CinematicDirector introuvable : cinématiques ignorées (ajoutez CinematicDirector sur la caméra).");
                // Erreur VISIBLE en jeu (1x par session) : sinon la cinématique "ne joue
                // pas" sans aucune explication (symptôme trompeur).
                if (!_warnedMissingCinematicDirector)
                {
                    _warnedMissingCinematicDirector = true;
                    CombatHUD.Instance?.AddAdvancedLog("<color=red><b>[CINÉMATIQUE] Aucun CinematicDirector dans la scène : cinématique ignorée (ajoutez CinematicDirector sur la caméra).</b></color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.red);
                }
                return;
            }
            bool firedOnce = false;
            for (int i = 0; i < cinematicIds.Count; i++)
            {
                string id = cinematicIds[i];
                if (string.IsNullOrWhiteSpace(id)) continue;
                var cine = _sceneData.FindCinematic(id);
                if (cine == null)
                {
                    Debug.LogWarning($"[JsonStorySceneController] Cinématique introuvable : '{id}'.");
                    continue;
                }
                var captured = cine;
                var visited = chainVisited;
                // Batch même-frame : la 1re préempte la ciné en cours (raccord
                // fluide), les suivantes s'enfilent derrière (pas de cut en cascade).
                bool preempt = !firedOnce;
                firedOnce = true;
                director.PlaySceneCinematic(captured, () => ExecuteCinematicChain(captured, visited), preempt);
                _warnedMissingCinematicDirector = false;
                CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>Cinématique :</b> {cine.Title} [{cine.CinematicId}]", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", new Color(1f, 0.5f, 0.9f));
            }
        }

        /// <summary>
        /// Chaînage "fin →" d'une carte cinématique : résout NextTargetId à la fin
        /// de la lecture (skip inclus ; jamais en cas d'abort — Stop n'invoque pas).
        /// Ordre : autre cinématique (enchaînée, anti-boucle) → nœud (saut, sauf
        /// vers le nœud courant) → réplique/conséquence du nœud courant → événement
        /// (exécution ponctuelle, tous nœuds). Id inconnu : avertissement, rien d'autre.
        /// </summary>
        private void ExecuteCinematicChain(SceneCinematicData cine, HashSet<string> visited)
        {
            if (cine == null || _sceneData == null) return;
            string targetId = (cine.NextTargetId ?? "").Trim();
            if (string.IsNullOrEmpty(targetId)) return;

            // 1. Autre cinématique : enchaîner (anti-boucle A→B→A).
            var nextCine = _sceneData.FindCinematic(targetId);
            if (nextCine != null)
            {
                visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!visited.Add(targetId))
                {
                    Debug.LogWarning($"[JsonStorySceneController] Boucle de cinématiques détectée vers '{targetId}' : chaîne interrompue.");
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[CINÉMATIQUE] Boucle détectée vers '{targetId}' : chaîne interrompue.</color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.gray);
                    return;
                }
                CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>Chaîne :</b> {cine.CinematicId} ─fin→ {targetId}", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", new Color(1f, 0.5f, 0.9f));
                FireLinkedCinematics(new List<string> { targetId }, visited);
                return;
            }

            // 2. Nœud : saut (si cible = nœud courant, mise en scène de la réplique d'entrée).
            if (_sceneData.FindNode(targetId) != null)
            {
                string currentId = _director != null && _director.CurrentNode != null ? _director.CurrentNode.Id : "";
                if (string.Equals(currentId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    StageCurrentLine();
                    return;
                }
                CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>Chaîne :</b> {cine.CinematicId} ─fin→ nœud {targetId}", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", new Color(1f, 0.5f, 0.9f));
                _director?.GoToNode(targetId);
                return;
            }

            // 3. Réplique / conséquence du nœud courant (routage explicite existant).
            // firePredecessors=false : la cinématique amont vient de se terminer, inutile
            // (et bouclant) de la rejouer — idem pour l'événement ci-dessous.
            // Une conséquence déjà résolue ce nœud stoppe la chaîne (anti double-payload).
            var dataNodeNow = _director != null ? _sceneData.FindNode(_director.CurrentNode?.Id) : null;
            if (dataNodeNow != null && dataNodeNow.FindConsequence(targetId) != null
                && _resolvedCardIdsThisNode.Contains(targetId))
            {
                return;
            }
            if (TryAdvanceToId(targetId, null, false)) return;

            // 4. Événement (tous nœuds) : exécution ponctuelle, sauf déjà résolu ce
            // nœud (l'entrée l'a déjà joué + sa cinématique amont vient de finir).
            if (_sceneData.Nodes != null)
            {
                for (int n = 0; n < _sceneData.Nodes.Count; n++)
                {
                    var node = _sceneData.Nodes[n];
                    if (node == null) continue;
                    var evt = node.FindEvent(targetId);
                    if (evt != null)
                    {
                        if (_resolvedCardIdsThisNode.Contains(targetId)) return;
                        ExecuteScenicEvent(evt, false);
                        return;
                    }
                }
            }

            Debug.LogWarning($"[JsonStorySceneController] Cible 'fin →' introuvable : '{targetId}' (cinématique '{cine.CinematicId}').");
        }

        public bool TryGetSpawnedInteractable(string interactableId, out TacticalInteractable prop)
        {
            prop = null;
            if (string.IsNullOrWhiteSpace(interactableId)) return false;
            for (int i = 0; i < _interactables.Count; i++)
            {
                var p = _interactables[i];
                if (p == null) continue;
                if (string.Equals(p.gameObject.name, "Interactable_" + interactableId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.ObjectName, interactableId, StringComparison.OrdinalIgnoreCase))
                {
                    prop = p;
                    return true;
                }
            }
            return false;
        }

        private void OnEnable()
        {
            FloatingWindowChrome.RegisterExtraBlocker(IsPointerOverSceneChat);
            RegisterSceneHudExclusionZones();
        }

        private void OnDisable()
        {
            FloatingWindowChrome.UnregisterExtraBlocker(IsPointerOverSceneChat);
            UnregisterSceneHudExclusionZones();
        }

        // Zones d'exclusion story (barre party, boîte dialogue) : en mode story scene
        // le HUD a des boutons de plus que le HUD combat ; sans ça les vignettes
        // dockées et les ouvertures de fenêtres les recouvrent.
        private System.Func<Rect> _partyBarZone;
        private System.Func<Rect> _dialogueBoxZone;

        private void RegisterSceneHudExclusionZones()
        {
            _partyBarZone ??= () => (_hasPartyBar && _partyBarRect.width > 0f && _partyBarRect.height > 0f)
                ? _partyBarRect : Rect.zero;
            _dialogueBoxZone ??= () => (_dialogueRect.width > 0f && _dialogueRect.height > 0f)
                ? _dialogueRect : Rect.zero;
            FloatingWindowChrome.RegisterExclusionZone(_partyBarZone);
            FloatingWindowChrome.RegisterExclusionZone(_dialogueBoxZone);
        }

        private void UnregisterSceneHudExclusionZones()
        {
            if (_partyBarZone != null) FloatingWindowChrome.UnregisterExclusionZone(_partyBarZone);
            if (_dialogueBoxZone != null) FloatingWindowChrome.UnregisterExclusionZone(_dialogueBoxZone);
        }

        /// <summary>
        /// Bloqueur central : survol du chat de scène (boîte dialogue, barre party,
        /// boutons d'interaction projetés) => la carte 3D ne reçoit aucun clic.
        /// Enregistré dans FloatingWindowChrome : couvre sélection, déplacement,
        /// zoom et orbit (tous les handlers 3D interrogent ce registre).
        /// </summary>
        private bool IsPointerOverSceneChat(Vector2 mouseGui)
        {
            if (CombatHUD.IsPaused) return false;
            if (_hasPartyBar && _partyBarRect.width > 0f && _partyBarRect.height > 0f && _partyBarRect.Contains(mouseGui))
                return true;
            if (_dialogueRect.width > 0f && _dialogueRect.height > 0f && _dialogueRect.Contains(mouseGui))
                return true;
            for (int i = 0; i < _interactButtonRects.Count; i++)
            {
                Rect r = _interactButtonRects[i];
                if (r.width > 0f && r.height > 0f && r.Contains(mouseGui))
                    return true;
            }
            // Filet live : avant la première OnGUI (rects encore vides), recalcule
            // le rect théorique pour ne laisser passer aucun clic.
            if ((_dialogueRect.width <= 0f || _dialogueRect.height <= 0f) && _director != null && _director.CurrentNode != null && _chatAnimProgress > 0.05f)
            {
                Rect live = ComputeDialogueRect();
                if (live.width > 0f && live.height > 0f && live.Contains(mouseGui))
                    return true;
            }
            return false;
        }

        private Rect ComputeDialogueRect()
        {
            float boxWidth = Mathf.Min(920f, Screen.width - 40f);
            float baseHeight = 150f;
            try
            {
                bool isReactionActive = _activeReactionChoice != null && _activeReactionPromptLine != null;
                var dataNode = _sceneData != null && _director != null ? _sceneData.FindNode(_director.CurrentNode?.Id) : null;
                bool hasDialogues = dataNode != null && dataNode.Dialogues != null && dataNode.Dialogues.Count > 0 && _currentDialogueIndex >= 0 && _currentDialogueIndex < dataNode.Dialogues.Count;
                var line = hasDialogues ? dataNode.Dialogues[_currentDialogueIndex] : null;
                bool hasChoices = !isReactionActive && line != null && line.Choices != null && line.Choices.Count > 0;
                if (hasChoices) baseHeight += line.Choices.Count * 34f;
                else if (isReactionActive) baseHeight += 20f;
            }
            catch { /* ignore */ }
            if (Screen.width <= 0 || Screen.height <= 0) return Rect.zero;
            float boxHeight = Mathf.Min(baseHeight, Screen.height - 120f);
            if (boxWidth <= 0f || boxHeight <= 0f) return Rect.zero;
            return new Rect((Screen.width - boxWidth) * 0.5f, Screen.height - boxHeight - 20f, boxWidth, boxHeight);
        }

        public IEnumerator InitializeSceneRoutine(TacticalHexGrid grid, TurnManager turnManager, CombatDevArena arena, ScenarioDirector director)
        {
            _isSceneInitialized = false;
            _grid = grid;
            _turnManager = turnManager;
            _arena = arena;
            _director = director;

            if (_sceneData == null)
            {
                Debug.LogError("[JsonStorySceneController] Aucune donnée de scène injectée !");
                yield break;
            }

            _firedTriggerIds.Clear();
            _activatedInteractableIds.Clear();
            _activationNodeIds.Clear();
            _nodeEnterCoords.Clear();
            _pendingMiddleChat.Clear();
            _activePopups.Clear();
            _pendingNodeJump = "";
            _pendingNodeJumpSource = "";
            _pendingTalkLineId = "";
            _talkReturnNodeId = "";
            _talkReturnLineId = "";
            _talkLandingNodeId = "";
            // Mode scène : boîtes d'info personnages masquées par défaut.
            if (!_overheadHudOverridden)
            {
                _overheadHudOverridden = true;
                _savedOverheadHud = TacticalUnitVisual.ShowOverheadHUD;
            }
            TacticalUnitVisual.ShowOverheadHUD = false;
            _chatAnimProgress = 0f;
            _lastActiveNode = null;
            _activeReactionChoice = null;
            _activeReactionPromptLine = null;
            _activeChallengeSummary = "";
            _activeChallengeRewards = "";

            if (_arena != null)
            {
                _arena.ClearAllUnits();
            }

            yield return LoadLinkedMapRoutine();

            if (_arena != null)
            {
                _arena.ClearAllUnits();
            }

            SpawnActors();
            SpawnInteractables();

            if (_turnManager != null)
            {
                _turnManager.ClearUnits();
                TacticalUnit leadUnit = _playerParty.Count > 0 ? _playerParty[0] : null;
                _turnManager.SetExplorationMode(true, leadUnit);

                for (int i = 0; i < _playerParty.Count; i++)
                {
                    _turnManager.RegisterUnit(_playerParty[i]);
                    _playerParty[i]?.GetComponent<TacticalUnitVisual>()?.SetCombatStance(false);
                }
            }

            if (_arena != null && _playerParty.Count > 0)
            {
                var allies = new List<TacticalUnit>(_playerParty);
                allies.RemoveAt(0);
                _arena.RegisterSceneParty(_playerParty[0], allies);
            }

            if (KilltimeAudioManager.Instance != null)
            {
                StorySceneManager.ResolveInitialSceneMusic(_sceneData, out var targetMood, out var targetIntensity);
                if (KilltimeAudioManager.Instance.CurrentMood != targetMood || KilltimeAudioManager.Instance.CurrentIntensityLevel != targetIntensity)
                {
                    KilltimeAudioManager.Instance.PlayMusic(targetMood, targetIntensity, forceRestart: false);
                }
            }

            CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>{_sceneData.Title}</b> [{_sceneData.Volume}] initialisée.", LogCategory.MovementAndTurns, "[SCÈNE]", Color.cyan);

            _isSceneInitialized = true;
            UpdateNodeState();
        }

        private IEnumerator LoadLinkedMapRoutine()
        {
            var mapEditor = MapEditorDevWindow.Instance ?? FindAnyObjectByType<MapEditorDevWindow>();
            if (mapEditor == null)
            {
                mapEditor = new GameObject("[UI] MapEditorDevWindow").AddComponent<MapEditorDevWindow>();
            }

            if (_sceneData.EmbeddedMap != null && _sceneData.EmbeddedMap.ModifiedTiles.Count > 0)
            {
                var cachedUnits = _sceneData.EmbeddedMap.PlacedUnits;
                _sceneData.EmbeddedMap.PlacedUnits = null;
                try
                {
                    mapEditor.ApplyLoadedMap(_sceneData.EmbeddedMap);
                }
                finally
                {
                    _sceneData.EmbeddedMap.PlacedUnits = cachedUnits;
                }
                yield return null;
            }
            else if (!string.IsNullOrEmpty(_sceneData.LinkedMapName))
            {
                string mapFolder = Path.Combine(Application.persistentDataPath, "Maps");
                string mapPath = Path.Combine(mapFolder, $"{_sceneData.LinkedMapName}.json");

                if (!File.Exists(mapPath))
                {
                    mapPath = Path.Combine(mapFolder, _sceneData.LinkedMapName);
                }

                if (File.Exists(mapPath))
                {
                    string json = File.ReadAllText(mapPath);
                    var mapData = JsonUtility.FromJson<TacticalMapSaveData>(json);
                    if (mapData != null)
                    {
                        mapData.PlacedUnits = null;
                        mapEditor.ApplyLoadedMap(mapData, mapPath);
                        yield return null;
                    }
                }
            }

            bool hasEnvironment = !string.IsNullOrEmpty(_sceneData.EnvironmentId);
            bool hasPlaceholders = _sceneData.EnvironmentPlaceholders != null && _sceneData.EnvironmentPlaceholders.Count > 0;

            if (hasEnvironment || hasPlaceholders)
            {
                if (SceneEnvironmentLibrary.Build(_sceneData.EnvironmentId, transform, _grid, _sceneData.EnvironmentPlaceholders, _sceneData.Lighting))
                {
                    yield return null;
                }
            }
        }

        private void SpawnActors()
        {
            _spawnedActors.Clear();
            _playerParty.Clear();

            for (int i = 0; i < _sceneData.Actors.Count; i++)
            {
                var actorData = _sceneData.Actors[i];
                if (actorData == null) continue;
                if (!actorData.SpawnInitially && !string.IsNullOrEmpty(actorData.SpawnOnNodeId)) continue;

                SpawnSingleActor(actorData);
            }
            SeparateStackedUnits();
        }

        /// <summary>
        /// Détecte l'acteur John (Passeur du Creuset) sans fichier disque :
        /// couvre "john.json", DisplayName "John" et modèles "John" / "Operative_John".
        /// </summary>
        private static bool IsJohnActor(SceneActorSpawnData actorData)
        {
            if (actorData == null) return false;
            if (!string.IsNullOrEmpty(actorData.CharacterSheetFileName)
                && actorData.CharacterSheetFileName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(actorData.DisplayName)
                && actorData.DisplayName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(actorData.ModelPrefabName)
                && actorData.ModelPrefabName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (actorData.EmbeddedSheet != null && JohnCharacter.IsJohn(actorData.EmbeddedSheet))
                return true;
            return false;
        }

        /// <summary>
        /// Détecte l'actrice Erika de Cleya sans fichier disque :
        /// couvre "erika.json", DisplayName "Erika" et modèles "Erika" / "Cleyan_Erika".
        /// </summary>
        private static bool IsErikaActor(SceneActorSpawnData actorData)
        {
            if (actorData == null) return false;
            if (!string.IsNullOrEmpty(actorData.CharacterSheetFileName)
                && actorData.CharacterSheetFileName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(actorData.DisplayName)
                && actorData.DisplayName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(actorData.ModelPrefabName)
                && actorData.ModelPrefabName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (actorData.EmbeddedSheet != null && ErikaCharacter.IsErika(actorData.EmbeddedSheet))
                return true;
            return false;
        }

        /// <summary>
        /// Détecte le Commandant Vance sans fichier disque :
        /// couvre "vance.json", DisplayName exact "Vance"/"Commandant Vance"
        /// ("Adjudant de Vance" n'est PAS Vance) et modèles exacts
        /// "Vance" / "Rebel_Commander" / "Commander_Vance".
        /// </summary>
        private static bool IsVanceActor(SceneActorSpawnData actorData)
        {
            if (actorData == null) return false;
            if (!string.IsNullOrEmpty(actorData.CharacterSheetFileName)
                && actorData.CharacterSheetFileName.IndexOf("vance", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(actorData.DisplayName)
                && (actorData.DisplayName.Equals("Vance", StringComparison.OrdinalIgnoreCase)
                    || actorData.DisplayName.Equals("Commandant Vance", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (!string.IsNullOrEmpty(actorData.ModelPrefabName)
                && (actorData.ModelPrefabName.Equals("vance", StringComparison.OrdinalIgnoreCase)
                    || actorData.ModelPrefabName.Equals("rebel_commander", StringComparison.OrdinalIgnoreCase)
                    || actorData.ModelPrefabName.Equals("commander_vance", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (actorData.EmbeddedSheet != null && VanceCharacter.IsVance(actorData.EmbeddedSheet))
                return true;
            return false;
        }

        public TacticalUnit SpawnSingleActor(SceneActorSpawnData actorData)
        {
            if (actorData == null) return null;
            if (_spawnedActors.TryGetValue(actorData.ActorId, out var existing) && existing != null)
                return existing;

            CharacterSheet sheet = null;
            string charFolder = Path.Combine(Application.persistentDataPath, "Characters");

            if (!string.IsNullOrEmpty(actorData.CharacterSheetFileName))
            {
                string sheetPath = Path.Combine(charFolder, actorData.CharacterSheetFileName.EndsWith(".json") ? actorData.CharacterSheetFileName : $"{actorData.CharacterSheetFileName}.json");
                if (File.Exists(sheetPath))
                {
                    sheet = CharacterStorageService.LoadCharacter(sheetPath);
                    // Répare les objets hors-catalogue des fiches disque (ex. scène 01 :
                    // "Carabine d'Assaut Résistance") vers leurs équivalents Armurerie/Marché.
                    if (sheet != null && ArmoryCatalog.MigrateLegacyScene01Items(sheet) > 0)
                        CharacterStorageService.SaveCharacter(sheet);
                }
            }

            if (sheet == null && actorData.EmbeddedSheet != null && !string.IsNullOrEmpty(actorData.EmbeddedSheet.Name))
            {
                sheet = JsonUtility.FromJson<CharacterSheet>(JsonUtility.ToJson(actorData.EmbeddedSheet));
            }

            // Fallback héroïque (sans JSON) : les scènes 01/02 référencent "john.json" /
            // "Operative_John" sans fichier disque — on résout la fiche héroïque intégrée
            // (voir JohnCharacter, même pattern que Mina/Lucas/Thomas dans le dev-spawn).
            // Aucun john.json à générer : le code reconnaît John directement.
            if (sheet == null && IsJohnActor(actorData))
            {
                sheet = JohnCharacter.BuildHeroicSheet();
            }

            // Fallback héroïque (sans JSON) : même pattern pour Erika de Cleya
            // ("erika.json" / "Cleyan_Erika" sans fichier disque).
            if (sheet == null && IsErikaActor(actorData))
            {
                sheet = ErikaCharacter.BuildHeroicSheet();
            }

            // Fallback héroïque (sans JSON) : même pattern pour le Commandant Vance
            // ("vance.json" / "Vance" / "Rebel_Commander" / "Commander_Vance" sans fichier disque).
            if (sheet == null && IsVanceActor(actorData))
            {
                sheet = VanceCharacter.BuildHeroicSheet();
            }

            if (sheet == null)
            {
                sheet = new CharacterSheet
                {
                    Name = actorData.DisplayName,
                    BaseAttributes = new Attributes(3, 3, 3, 3, 2, 2, 1, 2, 0),
                    BaseArmor = actorData.BaseArmor,
                    Profile = actorData.IsPlayer ? CharacterProfileType.HerosPJ : CharacterProfileType.PnjNormal,
                    ModelPrefabName = actorData.ModelPrefabName
                };
            }

            // Arme de la carte : clone catalogue si le nom existe (catégorie/prix/poids
            // réels), équivalent officiel si ancien nom hors-catalogue, sinon placeholder.
            // Dotation déséquipée (au joueur de s'équiper) ; ajoutée seulement si la fiche
            // n'a aucune arme en poche — jamais de doublon avec l'inventaire existant.
            if (!string.IsNullOrEmpty(actorData.EquippedWeaponName) && !ArmoryCatalog.SheetHasWeapon(sheet))
            {
                var weapon = ArmoryCatalog.ResolveSceneWeapon(actorData.EquippedWeaponName, actorData.ActorId, equipped: false);
                sheet.AddItem(weapon);
                if (sheet.GetSkill(weapon.AssociatedSkill).TrainingLevel == 0)
                    sheet.GetSkill(weapon.AssociatedSkill).TrainingLevel = 1;
            }

            var coords = new HexCoordinates(actorData.Q, actorData.R);
            var go = new GameObject($"Actor_{actorData.ActorId}");
            var unit = go.AddComponent<TacticalUnit>();
            unit.InitializeFromSheet(sheet, coords, _grid, actorData.IsPlayer);

            // Spawn refusé + fallback épuisé : pas de fantôme sans position
            // logique (invisible aux contrôles d'occupation, traversable).
            if (!unit.HasLogicalPosition)
            {
                Debug.LogError($"[JsonStorySceneController] Spawn impossible pour '{actorData.ActorId}' vers ({coords.Q}, {coords.R}) : zone saturée — acteur ignoré.");
                CombatHUD.Instance?.AddAdvancedLog($"<color=red>[SCÈNE] Spawn impossible pour '{actorData.DisplayName}' : zone saturée.</color>", LogCategory.MovementAndTurns, "[SCÈNE]", Color.red);
                Destroy(go);
                return null;
            }

            var visual = unit.GetComponent<TacticalUnitVisual>();
            if (visual != null)
            {
                visual.SetColor(actorData.PrimaryColor, actorData.AccentColor);
                visual.SetCombatStance(actorData.StartInCombatStance);
            }

            if (actorData.FacingAngle != 0f)
            {
                unit.transform.rotation = Quaternion.Euler(0f, actorData.FacingAngle, 0f);
            }

            _spawnedActors[actorData.ActorId] = unit;

            if (actorData.IsPlayer)
            {
                _playerParty.Add(unit);
            }

            return unit;
        }

        public void SpawnActorsForNode(string nodeId, bool isCombat = false)
        {
            if (_sceneData == null || _sceneData.Actors == null || string.IsNullOrEmpty(nodeId)) return;

            var dataNode = _sceneData.FindNode(nodeId);
            bool isCombatNode = isCombat || (dataNode != null && dataNode.TriggerCombatOnEnter) || (_turnManager != null && !_turnManager.IsInExploration);

            for (int i = 0; i < _sceneData.Actors.Count; i++)
            {
                var actorData = _sceneData.Actors[i];
                if (actorData == null) continue;
                if (_spawnedActors.ContainsKey(actorData.ActorId)) continue;
                if (!string.Equals(actorData.SpawnOnNodeId, nodeId, StringComparison.OrdinalIgnoreCase)) continue;

                var unit = SpawnSingleActor(actorData);
                if (unit != null)
                {
                    // Cinématiques liées à l'acteur (spawn différé) : non-bloquantes.
                    FireLinkedCinematics(actorData.CinematicIds);
                    _turnManager?.RegisterUnit(unit);
                    if (!actorData.IsPlayer && isCombatNode)
                    {
                        _arena?.RegisterHostileUnit(unit);
                        _arena?.SelectTarget(unit);
                    }
                }
            }
            SeparateStackedUnits();
        }

        /// <summary>
        /// Retrouve la fiche de spawn (carte 👥 Acteur) associée à une unité live.
        /// 1) lookup inverse _spawnedActors, 2) repli par nom (ActorId / DisplayName).
        /// </summary>
        public SceneActorSpawnData FindActorDataForUnit(TacticalUnit unit)
        {
            if (unit == null || _sceneData == null || _sceneData.Actors == null) return null;
            foreach (var kvp in _spawnedActors)
            {
                if (kvp.Value == unit)
                {
                    var d = _sceneData.FindActor(kvp.Key);
                    if (d != null) return d;
                    break;
                }
            }
            if (unit.Stats != null && !string.IsNullOrWhiteSpace(unit.Stats.Name))
            {
                string n = unit.Stats.Name.Trim();
                for (int i = 0; i < _sceneData.Actors.Count; i++)
                {
                    var a = _sceneData.Actors[i];
                    if (a == null) continue;
                    if (string.Equals(a.ActorId?.Trim(), n, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrWhiteSpace(a.DisplayName) && string.Equals(a.DisplayName.Trim(), n, StringComparison.OrdinalIgnoreCase)))
                        return a;
                }
            }
            return null;
        }

        private string ResolveTalkSpeakerDisplay(string talkSpeakerId, SceneActorSpawnData targetActor, TacticalUnit targetUnit)
        {
            if (!string.IsNullOrWhiteSpace(talkSpeakerId) && _sceneData != null && _sceneData.Actors != null)
            {
                string want = talkSpeakerId.Trim();
                for (int i = 0; i < _sceneData.Actors.Count; i++)
                {
                    var a = _sceneData.Actors[i];
                    if (a == null) continue;
                    if (string.Equals(a.ActorId?.Trim(), want, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrWhiteSpace(a.DisplayName) ? a.ActorId : a.DisplayName;
                    if (!string.IsNullOrWhiteSpace(a.DisplayName) && string.Equals(a.DisplayName.Trim(), want, StringComparison.OrdinalIgnoreCase))
                        return a.DisplayName;
                }
                return want;
            }
            if (targetActor != null && !string.IsNullOrWhiteSpace(targetActor.DisplayName)) return targetActor.DisplayName;
            if (targetUnit != null && targetUnit.Stats != null && !string.IsNullOrWhiteSpace(targetUnit.Stats.Name)) return targetUnit.Stats.Name;
            return "???";
        }

        /// <summary>
        /// Action "Parler à" (menu contextuel carte) : enclenche la sortie 💬 de
        /// la carte 👥 Acteur de la cible qui correspond à celui qui parle.
        /// - L'entrée dont le Qui correspond au talker (ActorId / DisplayName /
        ///   nom, insensible à la casse) gagne ; sinon l'entrée sans Qui (défaut,
        ///   n'importe qui).
        /// - Cible = NodeId → le nœud est enclenché (saut).
        /// - Cible = LineId du nœud courant → saut dialogue normal (choix /
        ///   défis / chaînage préservés, via TryAdvanceToId).
        /// - Cible = LineId d'un autre nœud → saut vers ce nœud (enclenché) puis
        ///   réplique en attente (_pendingTalkLineId).
        /// - Sans entrée correspondante : bavardage d'ambiance par défaut.
        /// </summary>
        public bool TalkToActor(TacticalUnit talker, TacticalUnit target)
        {
            if (target == null || target.Stats == null || !target.Stats.IsAlive) return false;
            string talkerName = (talker != null && talker.Stats != null && !string.IsNullOrWhiteSpace(talker.Stats.Name))
                ? talker.Stats.Name : "Escouade";
            var actorData = FindActorDataForUnit(target);
            var entries = actorData != null ? actorData.TalkEntries : null;

            SceneActorTalkEntry match = null;
            SceneActorTalkEntry fallback = null;
            if (entries != null && entries.Count > 0)
            {
                var talkerActor = FindActorDataForUnit(talker);
                string k1 = talkerActor != null ? (talkerActor.ActorId ?? "").Trim() : "";
                string k2 = talkerActor != null ? (talkerActor.DisplayName ?? "").Trim() : "";
                string k3 = talker != null && talker.Stats != null ? (talker.Stats.Name ?? "").Trim() : "";
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e == null || string.IsNullOrWhiteSpace(e.TargetId)) continue;
                    string who = (e.SpeakerId ?? "").Trim();
                    if (string.IsNullOrEmpty(who)) { fallback ??= e; continue; }
                    if ((!string.IsNullOrEmpty(k1) && string.Equals(who, k1, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrEmpty(k2) && string.Equals(who, k2, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrEmpty(k3) && string.Equals(who, k3, StringComparison.OrdinalIgnoreCase)))
                    {
                        match = e;
                        break;
                    }
                }
                match ??= fallback;
            }

            if (match == null)
            {
                string speaker = ResolveTalkSpeakerDisplay("", actorData, target);
                string targetName = target.Stats.Name;
                CombatHUD.Instance?.AddAdvancedLog($"💬 <b>{talkerName}</b> parle à <b>{targetName}</b> — <b>[{speaker}]</b> <i>hoche la tête — rien à signaler pour l'instant.</i> <color=grey>(carte 👥 sans sortie 💬 pour {talkerName} : ajoutez une rangée Parler à dans l'éditeur)</color>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
                target.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💬 …", Color.cyan);
                return true;
            }

            string targetId = (match.TargetId ?? "").Trim();

            // Origine de la conversation (avant tout saut) : sert à armer le
            // retour vers la conversation principale (ArmTalkReturn).
            string originNodeId = _director != null && _director.CurrentNode != null ? _director.CurrentNode.Id : "";
            string originLineId = "";
            var originDataNode = !string.IsNullOrEmpty(originNodeId) && _sceneData != null ? _sceneData.FindNode(originNodeId) : null;
            if (originDataNode != null && originDataNode.Dialogues != null
                && _currentDialogueIndex >= 0 && _currentDialogueIndex < originDataNode.Dialogues.Count)
                originLineId = originDataNode.Dialogues[_currentDialogueIndex]?.LineId ?? "";

            // 1. Cible = nœud : le nœud est enclenché (saut).
            if (_sceneData != null && _sceneData.FindNode(targetId) != null)
            {
                CombatHUD.Instance?.AddAdvancedLog($"💬 <b>{talkerName}</b> parle à <b>{target.Stats.Name}</b> ➔ nœud <b>{targetId}</b>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
                _pendingTalkLineId = "";
                if (string.Equals(originNodeId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    StageCurrentLine();
                    return true;
                }
                if (_director != null && _director.GoToNode(targetId))
                {
                    ArmTalkReturn(originNodeId, originLineId, targetId);
                    return true;
                }
                return false;
            }

            // 2. Cible = réplique : retrouver son nœud propriétaire.
            SceneDialogueLineData line = null;
            SceneNodeData ownerNode = null;
            if (_sceneData != null && _sceneData.Nodes != null)
            {
                for (int n = 0; n < _sceneData.Nodes.Count && line == null; n++)
                {
                    var node = _sceneData.Nodes[n];
                    if (node == null) continue;
                    var l = node.FindDialogue(targetId);
                    if (l != null) { line = l; ownerNode = node; }
                }
            }
            if (line == null)
            {
                Debug.LogWarning($"[JsonStorySceneController] Parler à : cible introuvable '{targetId}' (ni nœud, ni réplique).");
                CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[PARLER] Cible introuvable : '{targetId}'.</color>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.gray);
                target.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💬 …?", Color.gray);
                return false;
            }

            if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite
                && !line.Prerequisite.IsMet(_playerParty, talker, _director))
            {
                CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[PARLER] Prérequis non satisfait pour '{targetId}' ({line.Prerequisite.GetSummary()}).</color>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.gray);
                return false;
            }

            if (string.IsNullOrWhiteSpace(line.SpeakerId))
            {
                line.SpeakerId = ResolveTalkSpeakerDisplay("", actorData, target);
            }

            // Cinématiques / ambiance / tests auto : joués par StageCurrentLine
            // lors du staging (même nœud ou nœud enclenché) — pas ici (sinon
            // doublons). Seul le repli direct ci-dessous les joue lui-même.
            if (ownerNode != null && string.Equals(ownerNode.NodeId, originNodeId, StringComparison.OrdinalIgnoreCase))
            {
                CombatHUD.Instance?.AddAdvancedLog($"💬 <b>{talkerName}</b> parle à <b>{target.Stats.Name}</b> ➔ <b>{targetId}</b>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
                _pendingTalkLineId = "";
                ArmTalkReturn(originNodeId, originLineId, originNodeId);
                return TryAdvanceToId(targetId, null, false);
            }

            // Réplique d'un autre nœud : on enclenche le nœud puis la réplique
            // en attente (consommée par UpdateNodeState après l'entrée).
            if (ownerNode != null && _director != null && _director.GoToNode(ownerNode.NodeId))
            {
                CombatHUD.Instance?.AddAdvancedLog($"💬 <b>{talkerName}</b> parle à <b>{target.Stats.Name}</b> ➔ nœud <b>{ownerNode.NodeId}</b> ➔ <b>{targetId}</b>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
                _pendingTalkLineId = targetId;
                ArmTalkReturn(originNodeId, originLineId, ownerNode.NodeId);
                return true;
            }

            // Repli sans saut (saut refusé) : affichage direct, sans changer de nœud.
            _pendingTalkLineId = "";
            FireLinkedCinematics(line.CinematicIds);
            ApplyAmbienceSettings(line.Ambience, line.SoundCueId);
            string speakerShow = string.IsNullOrWhiteSpace(line.SpeakerId)
                ? ResolveTalkSpeakerDisplay("", actorData, target) : line.SpeakerId;
            string stage = !string.IsNullOrWhiteSpace(line.StageDirection) ? $" <i>({line.StageDirection})</i>" : "";
            CombatHUD.Instance?.AddAdvancedLog($"💬 <b>{talkerName}</b> parle à <b>{target.Stats.Name}</b> — <b>[{speakerShow}]</b>{stage} « {line.Speech} »", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
            target.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"💬 {line.Speech}", Color.cyan);
            if (line.Choices != null && line.Choices.Count > 0)
                CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[PARLER] {line.Choices.Count} choix disponibles dans le nœud '{ownerNode?.NodeId}'.</color>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.gray);
            else if (!string.IsNullOrWhiteSpace(line.NextLineId))
            {
                var next = ownerNode != null ? ownerNode.FindDialogue(line.NextLineId) : null;
                if (next != null && !string.IsNullOrWhiteSpace(next.Speech))
                    CombatHUD.Instance?.AddAdvancedLog($"💬 <b>[{next.SpeakerId}]</b> « {next.Speech} »", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
            }
            return true;
        }

        private bool HasActiveTalkReturn()
        {
            if (string.IsNullOrWhiteSpace(_talkReturnNodeId)) return false;
            string currentId = _director != null && _director.CurrentNode != null ? _director.CurrentNode.Id : "";
            return string.Equals(currentId, _talkLandingNodeId, StringComparison.OrdinalIgnoreCase);
        }

        // Mémorise le retour vers la conversation principale après un saut
        // "Parler à" (origine = nœud + réplique courants AVANT le saut).
        private void ArmTalkReturn(string originNodeId, string originLineId, string landingNodeId)
        {
            _talkReturnNodeId = "";
            _talkReturnLineId = "";
            _talkLandingNodeId = "";
            if (string.IsNullOrEmpty(originNodeId) || string.IsNullOrEmpty(landingNodeId)) return;
            _talkReturnNodeId = originNodeId;
            _talkReturnLineId = originLineId ?? "";
            _talkLandingNodeId = landingNodeId;
        }

        // Fin naturelle du nœud d'atterrissage "Parler à" : revient au nœud
        // d'origine (et à sa réplique) sans déclencher les prédécesseurs ni réinitialiser le nœud.
        private bool TryReturnFromTalk()
        {
            if (string.IsNullOrWhiteSpace(_talkReturnNodeId)) return false;
            string retNode = _talkReturnNodeId;
            string retLine = _talkReturnLineId;
            string landing = _talkLandingNodeId;
            _talkReturnNodeId = "";
            _talkReturnLineId = "";
            _talkLandingNodeId = "";
            if (_director == null || _sceneData == null) return false;
            string currentId = _director.CurrentNode?.Id;
            if (!string.Equals(currentId, landing, StringComparison.OrdinalIgnoreCase)) return false;
            if (_sceneData.FindNode(retNode) == null) return false;

            CombatHUD.Instance?.AddAdvancedLog($"💬 <b>Retour</b> vers la conversation (<b>{retNode}</b>).", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);

            // Cas intra-nœud
            if (string.Equals(currentId, retNode, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(retLine))
                {
                    TryAdvanceToId(retLine, null, false);
                }
                return true;
            }

            // Cas inter-nœuds : armement de l'immunité aux cinématiques d'intro
            _isReturningFromTalk = true;
            if (!string.IsNullOrEmpty(retLine)) _pendingTalkLineId = retLine;
            if (!_director.GoToNode(retNode))
            {
                _isReturningFromTalk = false;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Séparation anti-empilement au spawn : deux avatars vivants ne
        /// partagent jamais une case (données de scène avec mêmes Q,R, fallback
        /// partiel...). Les doublons sont relocalisés vers la case libre la
        /// plus proche, jamais laissés empilés.
        /// </summary>
        private void SeparateStackedUnits()
        {
            if (_grid == null) return;
            var units = new List<TacticalUnit>(_spawnedActors.Values);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive || !u.HasLogicalPosition) continue;
                bool stacked = false;
                for (int j = 0; j < units.Count; j++)
                {
                    if (i == j) continue;
                    var o = units[j];
                    if (o == null || o.Stats == null || !o.Stats.IsAlive || !o.HasLogicalPosition) continue;
                    var oc = o.OccupiedCoords;
                    for (int c = 0; c < oc.Count; c++)
                    {
                        if (u.Occupies(oc[c])) { stacked = true; break; }
                    }
                    if (stacked) break;
                }
                if (!stacked) continue;
                var from = u.CurrentCoords;
                // Les unités déjà relocalisées sont prises en compte via la
                // présence physique (TeleportTo refuse les cases occupées).
                if (_grid.TryFindNearestFreeFootprint(from, u.FootprintType, out var free, 12, u)
                    && !free.Equals(from)
                    && u.TeleportTo(free, _grid))
                {
                    string who = u.Stats != null ? u.Stats.Name : u.name;
                    Debug.Log($"[JsonStorySceneController] '{who}' séparé : ({from.Q}, {from.R}) ➔ ({free.Q}, {free.R}).");
                    CombatHUD.Instance?.AddAdvancedLog($"🔀 <b>{who}</b> replacé sur case libre ({free.Q}, {free.R}).", LogCategory.MovementAndTurns, "[SCÈNE]", Color.gray);
                }
                else
                {
                    Debug.LogWarning($"[JsonStorySceneController] Empilement non résolu en ({from.Q}, {from.R}) : aucune case libre à proximité.");
                }
            }
        }

        private void SpawnInteractables()
        {
            for (int i = 0; i < _interactables.Count; i++)
            {
                if (_interactables[i] != null) Destroy(_interactables[i].gameObject);
            }
            _interactables.Clear();

            for (int i = 0; i < _sceneData.Interactables.Count; i++)
            {
                var data = _sceneData.Interactables[i];
                if (data == null) continue;

                var go = new GameObject($"Interactable_{data.InteractableId}");
                var interactable = go.AddComponent<TacticalInteractable>();
                interactable.Configure(data.DisplayName, data.ActionLabel, new HexCoordinates(data.Q, data.R), data.Radius);

                interactable.OnInteractionTriggered += (unit) =>
                {
                    HandleInteractableResolved(unit, data);
                };

                _interactables.Add(interactable);
            }
        }

        /// <summary>
        /// Point central de résolution d'un interactable (touche E ou bouton [E]).
        /// Ne coupe jamais la conversation : log latéral + popup monde, et saut
        /// différé si TriggerNodeId pendant un dialogue (voir _pendingNodeJump).
        /// </summary>
        private void HandleInteractableResolved(TacticalUnit unit, SceneInteractableSpawnData data)
        {
            if (data == null) return;
            if (!string.IsNullOrEmpty(data.InteractableId))
            {
                MarkInteractableActivated(data.InteractableId);
            }
            // Cinématiques liées à l'interactable : non-bloquantes.
            FireLinkedCinematics(data.CinematicIds);
            if (!string.IsNullOrEmpty(data.CompletionObjectiveId))
            {
                _director?.SetObjectiveComplete(data.CompletionObjectiveId, true);
            }
            string actorName = unit != null && unit.Stats != null && !string.IsNullOrEmpty(unit.Stats.Name)
                ? unit.Stats.Name
                : (!string.IsNullOrEmpty(data.DisplayName) ? data.DisplayName : "Escouade");
            if (!string.IsNullOrWhiteSpace(data.SuccessLog))
            {
                CombatHUD.Instance?.AddAdvancedLog($"✔ {actorName} : {data.SuccessLog}", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
            }
            bool hasJump = !string.IsNullOrWhiteSpace(data.TriggerNodeId);
            if (!string.IsNullOrWhiteSpace(data.SuccessLog) || hasJump)
            {
                ShowInteractablePopup(data, hasJump);
            }
            if (hasJump)
            {
                if (IsConversationBusy())
                {
                    // Conversation en cours : on diffère, on propose, on ne coupe pas.
                    _pendingNodeJump = data.TriggerNodeId.Trim();
                    _pendingNodeJumpSource = data.InteractableId ?? "";
                    CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>Suite disponible :</b> {data.DisplayName} ➔ {data.TriggerNodeId} (finissez la conversation, puis [Suivre]).", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                }
                else if (!ExecuteInteractableNodeJump(data.TriggerNodeId))
                {
                    // Cible inconnue : déjà signalé dans Execute, le popup reste
                    // en mode info (sans saut effectif).
                    RemoveInteractableJumpOffer(data.InteractableId);
                }
            }
            if (unit != null)
            {
                unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"✔ {data.ActionLabel}", Color.green);
            }
        }

        /// <summary>
        /// Conversation en cours = une réplique est affichée (ou un choix / une
        /// réaction attend une validation). Hors dialogue (nœud objectif sans
        /// répliques), le saut reste immédiat comme avant.
        /// </summary>
        private bool IsConversationBusy()
        {
            // Choix en attente ou réaction affichée : dialogue non terminé.
            if (CurrentLineHasPendingChoice()) return true;
            var dataNode = _sceneData != null && _director != null ? _sceneData.FindNode(_director.CurrentNode?.Id) : null;
            if (dataNode == null || dataNode.Dialogues == null || dataNode.Dialogues.Count == 0) return false;
            return _currentDialogueIndex >= 0 && _currentDialogueIndex < dataNode.Dialogues.Count;
        }

        private void ShowInteractablePopup(SceneInteractableSpawnData data, bool hasJump)
        {
            if (data == null) return;
            for (int i = _activePopups.Count - 1; i >= 0; i--)
            {
                var p = _activePopups[i];
                if (p != null && string.Equals(p.InteractableId, data.InteractableId, StringComparison.OrdinalIgnoreCase))
                    _activePopups.RemoveAt(i);
            }
            _activePopups.Add(new InteractablePopup
            {
                InteractableId = data.InteractableId ?? "",
                DisplayName = data.DisplayName ?? "",
                Message = data.SuccessLog ?? "",
                TargetNodeId = hasJump ? (data.TriggerNodeId ?? "").Trim() : "",
                HasJump = hasJump && !string.IsNullOrWhiteSpace(data.TriggerNodeId),
                CreatedUnscaledTime = Time.unscaledTime
            });
        }

        private void RemoveInteractableJumpOffer(string interactableId)
        {
            for (int i = _activePopups.Count - 1; i >= 0; i--)
            {
                var p = _activePopups[i];
                if (p == null || !p.HasJump) continue;
                if (string.IsNullOrEmpty(interactableId)
                    || string.Equals(p.InteractableId, interactableId, StringComparison.OrdinalIgnoreCase))
                    _activePopups.RemoveAt(i);
            }
        }

        /// <summary>
        /// Exécute le saut vers le nœud cible (consentement explicite ou sortie
        /// naturelle). Retourne false si la cible est introuvable.
        /// </summary>
        private bool ExecuteInteractableNodeJump(string targetNodeId)
        {
            string target = (targetNodeId ?? "").Trim();
            if (string.IsNullOrEmpty(target) || _sceneData == null) return false;
            if (_sceneData.FindNode(target) == null)
            {
                // Cible peut être un id de choix scénario (fallback Choose historique).
                if (_director != null && _director.Choose(target))
                {
                    ClearPendingNodeJump();
                    return true;
                }
                Debug.LogWarning($"[JsonStorySceneController] Interactable : nœud cible introuvable '{target}'.");
                CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[ACTION] Cible introuvable : '{target}'.</color>", LogCategory.MovementAndTurns, "[ACTION]", Color.gray);
                return false;
            }
            string currentId = _director != null && _director.CurrentNode != null ? _director.CurrentNode.Id : "";
            if (string.Equals(currentId, target, StringComparison.OrdinalIgnoreCase))
            {
                // Saut vers le nœud courant : pas de MoveTo (sans effet), on
                // remet juste en scène la réplique et on solde le différé.
                ClearPendingNodeJump();
                StageCurrentLine();
                return true;
            }
            bool ok = _director != null && _director.GoToNode(target);
            if (!ok && _director != null) ok = _director.Choose(target);
            if (ok)
            {
                CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>Saut :</b> ➔ {target}", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                ClearPendingNodeJump();
                RemoveInteractableJumpOffer(null);
            }
            return ok;
        }

        private void ClearPendingNodeJump()
        {
            _pendingNodeJump = "";
            _pendingNodeJumpSource = "";
        }

        /// <summary>
        /// Consomme le saut différé à la sortie naturelle du nœud (bouton
        /// Continuer / fin de dialogue). Prioritaire sur les triggers et le
        /// chaînage normal : c'était l'intention directe du joueur (touche E).
        /// Jamais sur un nœud Objectif aux requis incomplets (pas de fuite).
        /// </summary>
        private bool TryConsumePendingJump()
        {
            if (string.IsNullOrWhiteSpace(_pendingNodeJump)) return false;
            var node = _director != null ? _director.CurrentNode : null;
            if (node != null && node.Kind == ScenarioNodeKind.Objective && !_director.AreRequiredObjectivesComplete())
                return false;
            return ExecuteInteractableNodeJump(_pendingNodeJump);
        }

        private void FollowPopupJump(InteractablePopup popup)
        {
            if (popup == null || string.IsNullOrWhiteSpace(popup.TargetNodeId)) return;
            ExecuteInteractableNodeJump(popup.TargetNodeId);
        }

        private void DismissPopup(InteractablePopup popup)
        {
            if (popup == null) return;
            _activePopups.Remove(popup);
            if (popup.HasJump
                && !string.IsNullOrEmpty(_pendingNodeJumpSource)
                && string.Equals(_pendingNodeJumpSource, popup.InteractableId, StringComparison.OrdinalIgnoreCase))
            {
                ClearPendingNodeJump();
                CombatHUD.Instance?.AddAdvancedLog("<color=grey>[ACTION] Suite ignorée (reste sur place).</color>", LogCategory.MovementAndTurns, "[ACTION]", Color.gray);
            }
        }

        private void PruneInteractablePopups()
        {
            if (_activePopups.Count == 0) return;
            float now = Time.unscaledTime;
            // Souris en coordonnées GUI (origine en haut à gauche, comme les Rect IMGUI).
            Vector2 mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            for (int i = _activePopups.Count - 1; i >= 0; i--)
            {
                var p = _activePopups[i];
                if (p == null) { _activePopups.RemoveAt(i); continue; }
                // Les offres de saut persistent jusqu'à [Suivre]/[Rester].
                if (p.HasJump) continue;
                // Survol du popup : le timer de fondu est mis en pause (la
                // création est repoussée d'une frame, donc le compteur fige
                // tant que la souris reste dessus).
                if (p.LastScreenRect.width > 0f && p.LastScreenRect.Contains(mouseGui))
                {
                    p.CreatedUnscaledTime += Time.unscaledDeltaTime;
                    continue;
                }
                if (now - p.CreatedUnscaledTime >= InteractablePopupAutoCloseDelay + InteractablePopupFadeDuration)
                    _activePopups.RemoveAt(i);
            }
        }

        private void Update()
        {
            if (!_isSceneInitialized) return;
            UpdateChatAnimation();
            if (CombatHUD.IsPaused || (StorySceneManager.Instance != null && StorySceneManager.Instance.IsTransitioning)) return;
            PruneInteractablePopups();

            var director = FindAnyObjectByType<CinematicDirector>();
            if (director != null && director.IsPlayingSceneCinematic)
            {
                if (director.CurrentPlayingSceneCinematic == null || director.CurrentPlayingSceneCinematic.HideSceneChat)
                {
                    return;
                }
                // Si la cinématique active ne masque pas le chat, on autorise l'avancement du dialogue
                HandleDialoguesInput();
                return;
            }

            HandlePartyInput();
            HandleDialoguesInput();
            CheckProximityObjectives();
            UpdateNodeState();
        }

        private bool ShouldShowSceneChat()
        {
            if (!_isSceneInitialized) return false;
            if (CombatHUD.IsPaused) return false;
            if (StorySceneManager.Instance != null && StorySceneManager.Instance.IsTransitioning) return false;

            var director = FindAnyObjectByType<CinematicDirector>();
            if (director != null && director.IsPlayingSceneCinematic)
            {
                if (director.CurrentPlayingSceneCinematic == null || director.CurrentPlayingSceneCinematic.HideSceneChat)
                {
                    return false;
                }
            }

            return _director != null && _director.CurrentNode != null;
        }

        private void UpdateChatAnimation()
        {
            float target = ShouldShowSceneChat() ? 1f : 0f;
            if (!Mathf.Approximately(_chatAnimProgress, target))
            {
                _chatAnimProgress = Mathf.MoveTowards(_chatAnimProgress, target, Time.unscaledDeltaTime * 2.85f);
            }
        }

        private void HandlePartyInput()
        {
            if (_turnManager == null || !_turnManager.IsInExploration) return;

            if (Input.GetKeyDown(KeyCode.Alpha1) && _playerParty.Count > 0) SelectOperative(_playerParty[0]);
            if (Input.GetKeyDown(KeyCode.Alpha2) && _playerParty.Count > 1) SelectOperative(_playerParty[1]);
            if (Input.GetKeyDown(KeyCode.Alpha3) && _playerParty.Count > 2) SelectOperative(_playerParty[2]);
            if (Input.GetKeyDown(KeyCode.Alpha4) && _playerParty.Count > 3) SelectOperative(_playerParty[3]);
            if (Input.GetKeyDown(KeyCode.Tab) && _playerParty.Count > 1) CyclePartyMember();

            if (Input.GetKeyDown(KeyCode.E) && _turnManager.ActiveUnit != null)
            {
                InteractWithNearest(_turnManager.ActiveUnit);
            }
        }

        private void SelectOperative(TacticalUnit unit)
        {
            if (unit == null || _arena == null) return;
            _arena.SwitchActiveExplorer(unit);
        }

        private void CyclePartyMember()
        {
            int idx = _playerParty.IndexOf(_turnManager.ActiveUnit);
            int next = (idx + 1) % _playerParty.Count;
            SelectOperative(_playerParty[next]);
        }

        private void InteractWithNearest(TacticalUnit unit)
        {
            for (int i = 0; i < _interactables.Count; i++)
            {
                var target = _interactables[i];
                if (target != null && target.CanInteract(unit))
                {
                    target.TryInteract(unit, out string log);
                    if (!string.IsNullOrEmpty(log))
                    {
                        CombatHUD.Instance?.AddAdvancedLog(log, LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                    }
                    return;
                }
            }
        }

        private void HandleDialoguesInput()
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                if (_activeReactionChoice != null)
                {
                    ConfirmReactionAndAdvance();
                    return;
                }

                var dataNode = _sceneData != null && _director != null ? _sceneData.FindNode(_director.CurrentNode?.Id) : null;
                if (dataNode != null && _currentDialogueIndex >= 0 && _currentDialogueIndex < dataNode.Dialogues.Count)
                {
                    var currentLine = dataNode.Dialogues[_currentDialogueIndex];
                    if (currentLine != null && currentLine.Choices.Count > 0)
                    {
                        return;
                    }
                }

                AdvanceDialogueOrNode();
            }
        }

        private void CheckProximityObjectives()
        {
            if (_director == null) return;
            var node = _director.CurrentNode;
            if (node == null || node.Kind != ScenarioNodeKind.Objective) return;

            // Garde anti-skip d'entrée : Update() appelle CheckProximity AVANT
            // UpdateNodeState. Sur la frame d'arrivée sur un nouveau nœud,
            // _nodeEnterCoords contient encore les positions d'entrée du nœud
            // PRÉCÉDENT. Si le joueur a bougé pendant le nœud précédent
            // (ex : 12 dialogues = le temps de se pré-positionner sur
            // l'holotable, qui est déjà à portée du spawn), moved=true et la
            // validation passe instantanément, puis le trigger saute la
            // narration. Avec un nœud intermédiaire vide (pas le temps de
            // bouger), moved=false + wasInRange=true => skip, par chance.
            // On refresh donc le snapshot et on saute la validation sur la
            // frame de transition, quelle que soit la longueur du chaînage.
            if (!string.Equals(_lastNodeId, node.Id, StringComparison.OrdinalIgnoreCase))
            {
                _nodeEnterCoords.Clear();
                for (int p = 0; p < _playerParty.Count; p++)
                {
                    var u = _playerParty[p];
                    if (u != null) _nodeEnterCoords[u] = u.CurrentCoords;
                }
                return;
            }

            for (int p = 0; p < _playerParty.Count; p++)
            {
                var member = _playerParty[p];
                if (member == null) continue;

                for (int i = 0; i < _sceneData.Interactables.Count; i++)
                {
                    var it = _sceneData.Interactables[i];
                    if (!string.IsNullOrEmpty(it.CompletionObjectiveId) && !_director.IsObjectiveComplete(it.CompletionObjectiveId))
                    {
                        var target = new HexCoordinates(it.Q, it.R);
                        if (member.CurrentCoords.DistanceTo(target) <= it.Radius)
                        {
                            // Pas de validation gratuite : un membre immobile depuis l'entrée
                            // du nœud et déjà à portée (spawn sur l'objectif) ne valide pas.
                            // Il faut s'éloigner/revenir, bouger, ou activer à la main (E).
                            bool hasEntry = _nodeEnterCoords.TryGetValue(member, out var enterCoords);
                            bool moved = !hasEntry || !enterCoords.Equals(member.CurrentCoords);
                            bool wasInRange = hasEntry && enterCoords.DistanceTo(target) <= it.Radius;
                            if (!moved && wasInRange) continue;
                            _director.SetObjectiveComplete(it.CompletionObjectiveId, true);
                            if (!string.IsNullOrEmpty(it.InteractableId))
                            {
                                MarkInteractableActivated(it.InteractableId);
                            }
                            string memberName = member.Stats != null ? member.Stats.Name : "Escouade";
                            if (!string.IsNullOrWhiteSpace(it.SuccessLog))
                            {
                                CombatHUD.Instance?.AddAdvancedLog($"✔ {memberName} : {it.SuccessLog}", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                                // Validation par proximité : info non-bloquante (log +
                                // popup monde), jamais de hijack du chat central ni
                                // de saut immédiat (le saut se fait à la touche E).
                                ShowInteractablePopup(it, false);
                            }
                            member.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"✔ Objectif Validé", Color.green);
                        }
                    }
                }
            }
        }

        private void MarkInteractableActivated(string interactableId)
        {
            if (string.IsNullOrEmpty(interactableId)) return;
            _activatedInteractableIds.Add(interactableId);
            try { _activationNodeIds[interactableId] = _director != null && _director.CurrentNode != null ? _director.CurrentNode.Id : ""; }
            catch { _activationNodeIds[interactableId] = ""; }
        }

        /// <summary>
        /// HÉRITÉ (plus aucun appelant) : l'ancien chemin envoyait le SuccessLog
        /// dans le chat central via le slot Réaction, ce qui cassait le flot de
        /// conversation (une réplique sautée au Continuer). Conservé pour compat,
        /// le chemin actuel est log latéral + popup monde (ShowInteractablePopup).
        /// </summary>
        private void ShowInteractableSuccessInMiddleChat(TacticalUnit unit, SceneInteractableSpawnData data)
        {
            if (unit == null || data == null) return;
            if (string.IsNullOrWhiteSpace(data.SuccessLog)) return;

            string speaker = !string.IsNullOrEmpty(unit.Stats?.Name)
                ? unit.Stats.Name
                : (!string.IsNullOrEmpty(data.DisplayName) ? data.DisplayName : "Escouade");
            var msg = new PendingMiddleChatMessage
            {
                Speaker = speaker,
                StageDirection = data.ActionLabel ?? "",
                Speech = data.SuccessLog,
                ActionLabel = data.ActionLabel ?? "",
                DisplayName = data.DisplayName ?? "",
                Rewards = !string.IsNullOrEmpty(data.CompletionObjectiveId) ? "✔ Objectif validé" : ""
            };
            if (_activeReactionChoice != null)
            {
                _pendingMiddleChat.Enqueue(msg);
                return;
            }
            ShowMiddleChatMessage(msg);
        }

        // Sentinelle : info interactable non-destructive. Le Continuer déjà
        // présent (Suivant dialogue, bouton objectif...) doit rester visible
        // et suivre le prochain chat : on dismiss sans avancer dialogue/nœud,
        // on retrouve l'état sous-jacent (_currentDialogueIndex inchangé).
        private const string InteractableInfoReturnSentinel = "__INTERACTABLE_INFO__";

        private void ShowMiddleChatMessage(PendingMiddleChatMessage msg)
        {
            var prompt = new SceneDialogueLineData
            {
                SpeakerId = msg.Speaker,
                StageDirection = msg.StageDirection
            };
            SetupReactionDisplay(prompt, msg.Speech, msg.ActionLabel, InteractableInfoReturnSentinel, $"🔧 {msg.DisplayName}", msg.Rewards);
        }

        private bool CurrentLineHasPendingChoice()
        {
            if (_activeReactionChoice != null) return true;
            var dataNode = _sceneData != null && _director != null ? _sceneData.FindNode(_director.CurrentNode?.Id) : null;
            if (dataNode == null || dataNode.Dialogues == null) return false;
            if (_currentDialogueIndex < 0 || _currentDialogueIndex >= dataNode.Dialogues.Count) return false;
            var line = dataNode.Dialogues[_currentDialogueIndex];
            return line != null && line.Choices != null && line.Choices.Count > 0;
        }

        private bool IsTriggerConditionMet(SceneTriggerData trigger)
        {
            if (trigger == null) return false;
            switch (trigger.Kind)
            {
                case SceneTriggerKind.InteractableActivated:
                    if (string.IsNullOrEmpty(trigger.InteractableId) || !_activatedInteractableIds.Contains(trigger.InteractableId))
                        return false;
                    // Trigger scopé sur un nœud : l'activation doit dater de ce nœud,
                    // sinon une utilisation précoce skippe le nœud dès son entrée.
                    if (!string.IsNullOrEmpty(trigger.SourceNodeId)
                        && (!_activationNodeIds.TryGetValue(trigger.InteractableId, out string atNode)
                            || !string.Equals(atNode, trigger.SourceNodeId, StringComparison.OrdinalIgnoreCase)))
                        return false;
                    return true;

                case SceneTriggerKind.ObjectiveCompleted:
                    return !string.IsNullOrEmpty(trigger.ObjectiveId) && _director != null && _director.IsObjectiveComplete(trigger.ObjectiveId);

                case SceneTriggerKind.CampaignFlagSet:
                {
                    if (string.IsNullOrEmpty(trigger.FlagKey) || _director == null || _director.State == null) return false;
                    bool has = _director.State.HasFlag(trigger.FlagKey);
                    return trigger.FlagMustBeSet ? has : !has;
                }

                case SceneTriggerKind.ActorCondition:
                {
                    if (string.IsNullOrEmpty(trigger.ActorId)) return false;
                    if (!_spawnedActors.TryGetValue(trigger.ActorId, out var unit) || unit == null || unit.Stats == null) return false;
                    if (trigger.ActorCondition == SceneActorConditionKind.HasStatus)
                    {
                        if (!System.Enum.TryParse<StatusEffect>(trigger.StatusName, true, out var status) || status == StatusEffect.None) return false;
                        return unit.Stats.ActiveStatus.HasFlag(status);
                    }
                    int max = Mathf.Max(1, unit.Stats.MaxHealth);
                    float pct = (float)unit.Stats.CurrentHealth / max * 100f;
                    return pct < Mathf.Clamp(trigger.HPPercentThreshold, 1, 100);
                }

                default:
                    return false;
            }
        }

        private bool TryFireReadyTrigger()
        {
            if (_sceneData == null || _sceneData.Triggers == null || _director == null) return false;
            string currentId = _director.CurrentNode?.Id;
            if (string.IsNullOrEmpty(currentId)) return false;

            for (int t = 0; t < _sceneData.Triggers.Count; t++)
            {
                var trg = _sceneData.Triggers[t];
                if (trg == null || string.IsNullOrEmpty(trg.TargetNodeId)) continue;
                if (string.Equals(trg.TargetNodeId, currentId, StringComparison.OrdinalIgnoreCase)) continue;
                if (trg.OneShot && _firedTriggerIds.Contains(trg.TriggerId)) continue;
                if (!string.IsNullOrEmpty(trg.SourceNodeId)
                    && !string.Equals(trg.SourceNodeId, currentId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsTriggerConditionMet(trg)) continue;
                if (!_director.GoToNode(trg.TargetNodeId)) continue;

                // Cinématiques liées au déclencheur : non-bloquantes (le saut a déjà eu lieu).
                FireLinkedCinematics(trg.CinematicIds);

                if (trg.OneShot) _firedTriggerIds.Add(trg.TriggerId);
                var target = _sceneData.FindNode(trg.TargetNodeId);
                CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>Déclencheur :</b> {trg.Label} ➔ {(target != null ? target.Title : trg.TargetNodeId)}", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.yellow);
                return true;
            }
            return false;
        }

        private void UpdateNodeState()
        {
            if (_director == null) return;
            var node = _director.CurrentNode;
            if (node == null) return;

            if (_lastNodeId != node.Id)
            {
                _lastNodeId = node.Id;
                _activeReactionChoice = null;
                _activeReactionPromptLine = null;
                _activeChallengeSummary = "";
                _activeChallengeRewards = "";

                bool isReturn = _isReturningFromTalk;
                _isReturningFromTalk = false;

                var dataNode = _sceneData != null ? _sceneData.FindNode(node.Id) : null;

                if (isReturn && !string.IsNullOrEmpty(_pendingTalkLineId))
                {
                    // Retour d'une conversation "Parler à" : reprise directe de la réplique
                    // d'origine SANS rejouer l'intro, les événements ou cinématiques prédécesseurs.
                    string pendingTalk = _pendingTalkLineId;
                    _pendingTalkLineId = "";
                    int targetIdx = dataNode != null ? dataNode.FindDialogueIndex(pendingTalk) : -1;
                    _currentDialogueIndex = targetIdx >= 0 ? targetIdx : 0;
                    StageCurrentLine();
                }
                else
                {
                    // Entrée standard dans un nouveau nœud
                    _currentDialogueIndex = FindEntryDialogueIndex(dataNode);
                    bool hasPredecessor = FirePredecessorCinematics(node.Id);
                    if (dataNode != null && _currentDialogueIndex >= 0 && _currentDialogueIndex < dataNode.Dialogues.Count)
                    {
                        var entryLine = dataNode.Dialogues[_currentDialogueIndex];
                        if (entryLine != null && FirePredecessorCinematics(entryLine.LineId))
                            hasPredecessor = true;
                    }

                    _resolvedCardIdsThisNode.Clear();
                    _nodeEnterCoords.Clear();
                    for (int p = 0; p < _playerParty.Count; p++)
                    {
                        var u = _playerParty[p];
                        if (u != null) _nodeEnterCoords[u] = u.CurrentCoords;
                    }
                    if (dataNode != null)
                    {
                        SpawnActorsForNode(node.Id, dataNode.TriggerCombatOnEnter);
                        FireLinkedCinematics(dataNode.CinematicIds);
                        ExecuteNodeEvents(dataNode);

                        if (dataNode.TriggerCombatOnEnter && _turnManager != null && _turnManager.IsInExploration)
                        {
                            _turnManager.EnterCombatMode();
                        }

                        for (int p = 0; p < _playerParty.Count; p++)
                        {
                            var u = _playerParty[p];
                            if (u != null && !_nodeEnterCoords.ContainsKey(u))
                                _nodeEnterCoords[u] = u.CurrentCoords;
                        }
                    }

                    if (!hasPredecessor)
                    {
                        StageCurrentLine();
                    }

                    if (!string.IsNullOrEmpty(_pendingTalkLineId))
                    {
                        string pendingTalk = _pendingTalkLineId;
                        _pendingTalkLineId = "";
                        if (dataNode != null && dataNode.FindDialogueIndex(pendingTalk) >= 0)
                            TryAdvanceToId(pendingTalk, null);
                    }
                }
            }

            // Déclencheurs d'entrée : évalués en continu pendant l'exploration
            // des nœuds Objectif (actions carte, conditions d'acteurs, flags...).
            // Les nœuds de dialogue/choix les évaluent à leur sortie (boutons).
            if (node.Kind == ScenarioNodeKind.Objective
                && _activeReactionChoice == null
                && !CurrentLineHasPendingChoice())
            {
                TryFireReadyTrigger();
            }
        }

        // Mise en scène de la réplique courante : prérequis, cinématiques liées (cartes 🎬),
        // ambiance sonore/musicale, tests auto. DÉ-HARDCODÉ : plus aucun focus caméra direct
        // (l'ancien CameraFocusActorId/Pitch/Distance est ignoré — migré en cartes 🎬 à tracking).
        private void StageCurrentLine()
        {
            var dataNode = _sceneData.FindNode(_director.CurrentNode?.Id);
            if (dataNode == null || dataNode.Dialogues.Count == 0 || _currentDialogueIndex >= dataNode.Dialogues.Count) return;

            var line = dataNode.Dialogues[_currentDialogueIndex];

            // Évaluation des prérequis : si non satisfaits, passage immédiat à la réplique suivante
            if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite)
            {
                if (!line.Prerequisite.IsMet(_playerParty, _turnManager?.ActiveUnit, _director))
                {
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[PRÉREQUIS NON SATISFAIT] Réplique '{line.LineId}' ignorée ({line.Prerequisite.GetSummary()}).</color>", LogCategory.MovementAndTurns, "[NARRATION]", Color.gray);
                    AdvanceDialogueOrNode();
                    return;
                }
            }

            // Cinématiques liées à la réplique : non-bloquantes (le dialogue continue aussitôt).
            FireLinkedCinematics(line.CinematicIds);

            // Gestion de l'ambiance et des cues audio
            ApplyAmbienceSettings(line.Ambience, line.SoundCueId);

            // Gestion des tests de compétence automatiques
            if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
            {
                ExecuteAutoSkillCheck(line);
            }
        }

        private void ApplyAmbienceSettings(SceneAmbienceData ambience, string fallbackSoundCueId)
        {
            string sound = ambience != null && !string.IsNullOrEmpty(ambience.SoundCueId) ? ambience.SoundCueId : fallbackSoundCueId;
            if (!string.IsNullOrEmpty(sound) && KilltimeAudioManager.Instance != null)
            {
                if (Enum.TryParse<SoundId>(sound, out var soundEnum))
                {
                    KilltimeAudioManager.Instance.PlayUI(soundEnum, 0.55f);
                }
            }

            if (ambience != null && ambience.HasAmbience)
            {
                if (ambience.ChangeMusic && KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.PlayMusic(ambience.MusicMood, ambience.MusicIntensity, false);
                }

                // DÉ-HARDCODÉ : l'ancien CameraShakeIntensity est ignoré (migré en cartes 🎬
                // à plan relatif + effet HandheldShake). Son / musique / alarme conservés.

                if (ambience.TriggerAlarm)
                {
                    CombatHUD.Instance?.AddAdvancedLog("🚨 <b>ALERTE SYSTÈME :</b> Sirènes enclenchées dans le secteur.", LogCategory.Combat, "[ALARME]", Color.red);
                }
            }
        }

        private void ExecuteAutoSkillCheck(SceneDialogueLineData line)
        {
            var check = line.AutoSkillCheck;
            TacticalUnit actorUnit = null;

            if (!string.IsNullOrEmpty(check.SpecificActorId) && _spawnedActors.TryGetValue(check.SpecificActorId, out var specificUnit))
            {
                actorUnit = specificUnit;
            }
            else if (_turnManager != null && _turnManager.ActiveUnit != null && _turnManager.ActiveUnit.IsPlayerControlled)
            {
                actorUnit = _turnManager.ActiveUnit;
            }
            else if (_playerParty.Count > 0)
            {
                actorUnit = _playerParty[0];
            }

            var sheet = actorUnit != null ? actorUnit.Sheet : null;
            string actorName = sheet != null ? sheet.Name : "Protagoniste";
            Attributes effectiveAttr = sheet != null ? sheet.GetEffectiveAttributes() : new Attributes();

            int rank = SkillDefinitions.GetBaseRank(check.RequiredSkill, effectiveAttr, false);
            int steps = SkillDefinitions.CharacteristicSteps(rank);
            int training = sheet != null ? sheet.GetSkill(check.RequiredSkill).TrainingLevel : 0;
            DiceType die = SkillDefinitions.DieFromTotalSteps(steps + training);

            var roller = new DiceRoller();
            var result = roller.Roll(die, modifier: 0, targetDC: check.TargetDC);

            string skillName = SkillDefinitions.GetDisplayName(check.RequiredSkill);
            string outcomeStr = result.IsSuccess ? "SUCCÈS PASSİF" : "ÉCHEC PASSİF";
            Color col = result.IsSuccess ? Color.green : new Color(1.0f, 0.45f, 0.2f);

            CombatHUD.Instance?.AddAdvancedLog(
                $"🎲 <b>[TEST AUTO]</b> {actorName} teste <b>{skillName}</b> ({die}) contre SD {check.TargetDC} ➔ Résultat: <b>{result.Total}</b> ({result.Differential:+0;-0;0}) — <color=#{ColorUtility.ToHtmlStringRGB(col)}>{outcomeStr}</color>",
                LogCategory.Combat,
                "[PASSİF]",
                col
            );

            actorUnit?.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"{outcomeStr} ({result.Total} vs SD {check.TargetDC})", col);

            if (result.IsSuccess)
            {
                if (check.SuccessEffects != null && check.SuccessEffects.Count > 0)
                {
                    _director?.ApplyEffects(check.SuccessEffects);
                }

                if (!string.IsNullOrEmpty(check.SuccessSpeech))
                {
                    line.Speech = check.SuccessSpeech;
                    if (!string.IsNullOrEmpty(check.SuccessStageDirection)) line.StageDirection = check.SuccessStageDirection;
                }

                if (!string.IsNullOrEmpty(check.SuccessNextLineId))
                {
                    line.NextLineId = check.SuccessNextLineId;
                }
            }
            else
            {
                if (check.FailureEffects != null && check.FailureEffects.Count > 0)
                {
                    _director?.ApplyEffects(check.FailureEffects);
                }

                if (!string.IsNullOrEmpty(check.FailureSpeech))
                {
                    line.Speech = check.FailureSpeech;
                    if (!string.IsNullOrEmpty(check.FailureStageDirection)) line.StageDirection = check.FailureStageDirection;
                }

                if (!string.IsNullOrEmpty(check.FailureNextLineId))
                {
                    line.NextLineId = check.FailureNextLineId;
                }
            }
        }

        public void ExecuteNodeEvents(SceneNodeData nodeData)
        {
            if (nodeData == null || nodeData.Events.Count == 0) return;

            for (int i = 0; i < nodeData.Events.Count; i++)
            {
                ExecuteScenicEvent(nodeData.Events[i]);
            }
        }

        public void ExecuteScenicEvent(SceneEventData evt, bool firePredecessors = true)
        {
            if (evt == null) return;

            // Cinématiques placées en amont de l'événement (sauf arrivée via chaîne).
            if (firePredecessors) FirePredecessorCinematics(evt.EventId);

            // Prérequis de l'événement
            if (evt.Prerequisite != null && evt.Prerequisite.HasPrerequisite)
            {
                if (!evt.Prerequisite.IsMet(_playerParty, _turnManager?.ActiveUnit, _director))
                {
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[ÉVÉNEMENT IGNORÉ] '{evt.Title}' ({evt.Prerequisite.GetSummary()}).</color>", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.gray);
                    return;
                }
            }

            // Marque la carte comme résolue pour ce nœud (anti double-payload via chaîne).
            if (!string.IsNullOrEmpty(evt.EventId)) _resolvedCardIdsThisNode.Add(evt.EventId);

            // Cinématiques liées à l'événement : non-bloquantes.
            FireLinkedCinematics(evt.CinematicIds);

            // Ambiance & Audio
            ApplyAmbienceSettings(evt.Ambience, "");

            // Action spécifique de l'événement
            switch (evt.Kind)
            {
                case SceneEventKind.TriggerCombat:
                    SpawnActorsForNode(_director?.CurrentNode?.Id, true);
                    _turnManager?.EnterCombatMode();
                    CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>ÉVÉNEMENT :</b> Combat déclenché [{evt.Title}].", LogCategory.Combat, "[COMBAT]", Color.red);
                    break;

                case SceneEventKind.SpawnEnemies:
                    SpawnActorsForNode(_director?.CurrentNode?.Id, true);
                    _turnManager?.EnterCombatMode();
                    CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>ÉVÉNEMENT :</b> Déploiement d'unités [{evt.Title}].", LogCategory.Combat, "[RENFORTS]", Color.red);
                    break;

                // DÉ-HARDCODÉS : kinds remplacés par des cartes 🎬 (tracking / effet relatif).
                // Ignorés avec un avertissement de migration (1x par event pour ne pas spammer).
                case SceneEventKind.CameraFocus:
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[OBSOLÈTE] Événement '{evt.Title}' : kind CameraFocus ignoré — migrer vers une carte 🎬 à tracking.</color>", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.gray);
                    break;

                case SceneEventKind.CameraShake:
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[OBSOLÈTE] Événement '{evt.Title}' : kind CameraShake ignoré — migrer vers une carte 🎬 à effet.</color>", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.gray);
                    break;

                case SceneEventKind.ObjectiveUpdate:
                    if (!string.IsNullOrEmpty(evt.CompletionObjectiveId))
                    {
                        _director?.SetObjectiveComplete(evt.CompletionObjectiveId, true);
                    }
                    break;

                case SceneEventKind.GrantRewards:
                    if (evt.Rewards != null && _playerParty.Count > 0 && _playerParty[0].Sheet != null)
                    {
                        var s = _playerParty[0].Sheet;
                        if (evt.Rewards.EarnCredits > 0) s.EarnCredits(evt.Rewards.EarnCredits);
                        if (evt.Rewards.EarnXP > 0) { s.AvailableXP += evt.Rewards.EarnXP; s.TotalEarnedXP += evt.Rewards.EarnXP; }
                        if (!string.IsNullOrEmpty(evt.Rewards.ItemRewardName))
                            s.AddItem(new InventoryItem { Name = evt.Rewards.ItemRewardName, Type = evt.Rewards.ItemRewardType });
                    }
                    break;
            }

            // Effets de campagne de l'événement
            if (evt.Effects != null && evt.Effects.Count > 0)
            {
                _director?.ApplyEffects(evt.Effects);
            }

            CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>ÉVÉNEMENT EXÉCUTÉ :</b> {evt.Title}", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.yellow);
        }

        private void AdvanceDialogueOrNode()
        {
            AdvanceDialogueOrNodeInternal(null, false);
        }

        private void AdvanceDialogueOrNodeInternal(HashSet<string> visited, bool skipLink = false)
        {
            var dataNode = _sceneData.FindNode(_director.CurrentNode?.Id);
            if (dataNode != null && _currentDialogueIndex >= 0 && _currentDialogueIndex < dataNode.Dialogues.Count)
            {
                var currentLine = dataNode.Dialogues[_currentDialogueIndex];
                if (!skipLink && !string.IsNullOrEmpty(currentLine.NextLineId))
                {
                    if (TryAdvanceToId(currentLine.NextLineId, visited)) return;
                }

                // Liaison vide ou cible introuvable = fin du nœud (pas de fallback
                // séquentiel : le routage est explicite via les fenêtres normales).
            }

            var node = _director.CurrentNode;
            if (node == null) return;

            if (node.Kind == ScenarioNodeKind.Resolution)
            {
                if (TryConsumePendingJump()) return;
                if (TryReturnFromTalk()) return;
                if (!TryFireReadyTrigger()) _director.CompleteActiveScenario();
            }
            else if (node.Kind == ScenarioNodeKind.Objective && !_director.AreRequiredObjectivesComplete())
            {
                if (TryReturnFromTalk()) return;
                if (!TryFireReadyTrigger())
                    CombatHUD.Instance?.AddAdvancedLog("🎯 <b>Objectifs requis incomplets.</b> Terminez les objectifs principaux pour poursuivre.", LogCategory.MovementAndTurns, "[OBJECTIF]", Color.yellow);
            }
            else
            {
                if (TryConsumePendingJump()) return;
                if (TryReturnFromTalk()) return;
                if (!TryFireReadyTrigger() && !_director.Continue()) _director.CompleteActiveScenario();
            }
        }

        private void SelectDialogueChoice(SceneDialogueChoiceData choice, SceneDialogueLineData promptLine)
        {
            if (choice == null || promptLine == null) return;

            if (choice.Challenge != null && choice.Challenge.HasChallenge)
            {
                ExecuteSkillChallenge(choice, promptLine);
                return;
            }

            if (choice.Effects != null && choice.Effects.Count > 0 && _director != null)
            {
                _director.ApplyEffects(choice.Effects);
            }

            CombatHUD.Instance?.AddAdvancedLog($"💬 [Choix] {choice.Label}", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.white);

            // Routage direct vers fenêtre normale : pas d'écran de réaction intermédiaire.
            // Choix sans cible = fin du nœud (routage explicite, pas de séquentiel).
            if (!string.IsNullOrEmpty(choice.NextLineId))
            {
                if (TryAdvanceToId(choice.NextLineId, null)) return;
            }

            AdvanceDialogueOrNode();
        }

        private void GrantRewardItem(CharacterSheet sheet, string itemName, ItemType itemType, int quantity, ref string rewardsSummary)
        {
            if (sheet == null || string.IsNullOrEmpty(itemName)) return;
            int qty = Mathf.Max(1, quantity);
            var catalogDef = ArmoryCatalog.GetByName(itemName);
            InventoryItem rewardItem;
            if (catalogDef != null)
            {
                rewardItem = catalogDef.Clone();
                rewardItem.Quantity = qty;
                rewardItem.IsEquipped = false;
            }
            else
            {
                rewardItem = new InventoryItem
                {
                    Name = itemName,
                    Type = itemType,
                    PriceCE = 100,
                    Quantity = qty
                };
            }
            sheet.AddItem(rewardItem);
            rewardsSummary += qty > 1 ? $"[{itemName} x{qty}]  " : $"[{itemName}]  ";
        }

        // Avance vers une réplique ou une conséquence (carte 🎁). Retourne false si l'id
        // est vide, introuvable, ou si une chaîne de conséquences boucle (anti-blocage).
        // Avance vers une réplique ou une conséquence, en jouant d'abord les
        // cinématiques placées en amont (sauf si firePredecessors=false : arrivée
        // via chaîne "fin →", dont la cinématique vient de se terminer).
        private bool TryAdvanceToId(string id, HashSet<string> visited, bool firePredecessors = true)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var dataNode = _sceneData.FindNode(_director.CurrentNode?.Id);
            if (dataNode == null) return false;

            int targetIdx = dataNode.FindDialogueIndex(id);
            if (targetIdx >= 0)
            {
                // Garde anti-boucle : ré-atterrir sur la réplique courante (ex : fin
                // de sa propre cinématique amont) ne rejoue ni les amonts ni la mise
                // en scène (qui rejouerait les cinématiques d'entrée de la réplique).
                bool changed = _currentDialogueIndex != targetIdx;
                _currentDialogueIndex = targetIdx;
                if (changed && firePredecessors) FirePredecessorCinematics(id);
                StageCurrentLine();
                return true;
            }

            var cons = dataNode.FindConsequence(id);
            if (cons != null)
            {
                SceneDialogueLineData prompt = null;
                if (_currentDialogueIndex >= 0 && _currentDialogueIndex < dataNode.Dialogues.Count)
                    prompt = dataNode.Dialogues[_currentDialogueIndex];
                return ExecuteConsequenceChain(cons, prompt, visited ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase), firePredecessors);
            }

            return false;
        }

        /// <summary>
        /// Cinématiques placées EN AMONT d'une carte (leur sortie "fin →" pointe vers
        /// cardId) : jouées fire-and-forget quand la carte est atteinte. L'auto-lien
        /// est exclu (l'éditeur l'interdit déjà). Les doublons sont dédupliqués.
        /// </summary>
        private bool FirePredecessorCinematics(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId) || _sceneData == null || _sceneData.Cinematics == null) return false;
            List<string> ids = null;
            for (int i = 0; i < _sceneData.Cinematics.Count; i++)
            {
                var c = _sceneData.Cinematics[i];
                if (c == null || string.IsNullOrWhiteSpace(c.NextTargetId)) continue;
                if (!string.Equals(c.NextTargetId.Trim(), cardId.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(c.CinematicId, cardId, StringComparison.OrdinalIgnoreCase)) continue;
                ids ??= new List<string>();
                bool dup = false;
                for (int k = 0; k < ids.Count; k++)
                    if (string.Equals(ids[k], c.CinematicId, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                if (!dup) ids.Add(c.CinematicId);
            }
            if (ids != null && ids.Count > 0)
            {
                FireLinkedCinematics(ids);
                return true;
            }
            return false;
        }

        // Entrée du nœud = première carte dialogue sans lien entrant (routage explicite).
        // Repli sur 0 si aucune (cycle) ou liste vide.
        public static int FindEntryDialogueIndex(SceneNodeData dataNode)
        {
            if (dataNode == null || dataNode.Dialogues == null || dataNode.Dialogues.Count == 0) return 0;
            if (dataNode.Dialogues.Count == 1) return 0;
            var incoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < dataNode.Dialogues.Count; i++)
            {
                var l = dataNode.Dialogues[i];
                if (l == null) continue;
                if (!string.IsNullOrEmpty(l.NextLineId)) incoming.Add(l.NextLineId);
                if (l.Choices != null)
                {
                    for (int c = 0; c < l.Choices.Count; c++)
                    {
                        var ch = l.Choices[c];
                        if (ch == null) continue;
                        if (!string.IsNullOrEmpty(ch.NextLineId)) incoming.Add(ch.NextLineId);
                        var chal = ch.Challenge;
                        if (chal != null && chal.HasChallenge)
                        {
                            if (!string.IsNullOrEmpty(chal.SuccessNextLineId)) incoming.Add(chal.SuccessNextLineId);
                            if (!string.IsNullOrEmpty(chal.FailureNextLineId)) incoming.Add(chal.FailureNextLineId);
                        }
                    }
                }
                if (l.AutoSkillCheck != null && l.AutoSkillCheck.HasAutoCheck)
                {
                    if (!string.IsNullOrEmpty(l.AutoSkillCheck.SuccessNextLineId)) incoming.Add(l.AutoSkillCheck.SuccessNextLineId);
                    if (!string.IsNullOrEmpty(l.AutoSkillCheck.FailureNextLineId)) incoming.Add(l.AutoSkillCheck.FailureNextLineId);
                }
            }
            if (dataNode.Consequences != null)
            {
                for (int k = 0; k < dataNode.Consequences.Count; k++)
                {
                    var cons = dataNode.Consequences[k];
                    if (cons == null) continue;
                    if (!string.IsNullOrEmpty(cons.NextLineId)) incoming.Add(cons.NextLineId);
                }
            }
            for (int i = 0; i < dataNode.Dialogues.Count; i++)
            {
                var l = dataNode.Dialogues[i];
                if (l == null || string.IsNullOrEmpty(l.LineId)) continue;
                if (!incoming.Contains(l.LineId)) return i;
            }
            return 0;
        }

        private void ApplyConsequencePayload(SceneConsequenceData cons, out string rewardsSummary)
        {
            rewardsSummary = "";
            if (cons == null) return;

            // Marque la carte comme résolue pour ce nœud (anti double-payload via chaîne).
            if (!string.IsNullOrEmpty(cons.ConsequenceId)) _resolvedCardIdsThisNode.Add(cons.ConsequenceId);

            // Cinématiques liées à la conséquence : non-bloquantes.
            FireLinkedCinematics(cons.CinematicIds);

            TacticalUnit unit = null;
            if (_turnManager != null && _turnManager.ActiveUnit != null && _turnManager.ActiveUnit.IsPlayerControlled)
                unit = _turnManager.ActiveUnit;
            else if (_playerParty.Count > 0)
                unit = _playerParty[0];
            var sheet = unit != null ? unit.Sheet : null;

            var rew = cons.Rewards;
            if (rew != null && sheet != null)
            {
                if (rew.EarnCredits > 0)
                {
                    sheet.EarnCredits(rew.EarnCredits);
                    rewardsSummary += $"+{rew.EarnCredits} CE  ";
                }
                if (rew.EarnXP > 0)
                {
                    sheet.AvailableXP += rew.EarnXP;
                    sheet.TotalEarnedXP += rew.EarnXP;
                    rewardsSummary += $"+{rew.EarnXP} XP  ";
                }
                if (!string.IsNullOrEmpty(rew.ItemRewardName))
                    GrantRewardItem(sheet, rew.ItemRewardName, rew.ItemRewardType, rew.ItemRewardQuantity, ref rewardsSummary);
                if (rew.AdditionalItems != null)
                {
                    for (int i = 0; i < rew.AdditionalItems.Count; i++)
                    {
                        var extra = rew.AdditionalItems[i];
                        if (extra == null || string.IsNullOrEmpty(extra.ItemName)) continue;
                        GrantRewardItem(sheet, extra.ItemName, extra.ItemRewardType, extra.Quantity, ref rewardsSummary);
                    }
                }
                if (!string.IsNullOrEmpty(rew.CompletionObjectiveId))
                {
                    _director?.SetObjectiveComplete(rew.CompletionObjectiveId, true);
                    rewardsSummary += "✔ Objectif validé  ";
                }
            }

            if (cons.Effects != null && cons.Effects.Count > 0 && _director != null)
                _director.ApplyEffects(cons.Effects);

            string who = sheet != null ? sheet.Name : "Escouade";
            string fxTag = cons.Effects != null && cons.Effects.Count > 0 ? $" (+{cons.Effects.Count} effet(s))" : "";
            CombatHUD.Instance?.AddAdvancedLog(
                $"🎁 <b>[CONSÉQUENCE]</b> {who} — <b>{cons.Title}</b>{(string.IsNullOrEmpty(rewardsSummary) ? "" : $" : {rewardsSummary}")}{fxTag}",
                LogCategory.MovementAndTurns,
                "[BUTIN]",
                Color.yellow
            );

            if (cons.TriggersCombat)
            {
                SpawnActorsForNode(_director?.CurrentNode?.Id, true);
                _turnManager?.EnterCombatMode();
                CombatHUD.Instance?.AddAdvancedLog("🚨 <b>CONSÉQUENCE :</b> Combat déclenché !", LogCategory.Combat, "[ALERTE]", Color.red);
            }
        }

        // Exécute une conséquence et sa chaîne (cons → cons). Retourne true si le flux a
        // progressé (saut de réplique, affichage dialogue, avance d'index).
        private bool ExecuteConsequenceChain(SceneConsequenceData first, SceneDialogueLineData promptLine, HashSet<string> visited, bool firePredecessors = true)
        {
            if (first == null) return false;
            visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dataNode = _sceneData.FindNode(_director.CurrentNode?.Id);
            if (dataNode == null) return false;

            // Cinématiques placées en amont de la conséquence d'entrée (sauf arrivée
            // via chaîne "fin →", dont la cinématique vient de se terminer).
            if (firePredecessors) FirePredecessorCinematics(first.ConsequenceId);

            var cons = first;
            int guard = 0;
            while (cons != null && guard++ < 8)
            {
                if (!visited.Add(cons.ConsequenceId)) return false;

                ApplyConsequencePayload(cons, out string rewardsSummary);
                string onwardId = !string.IsNullOrEmpty(cons.NextLineId) ? cons.NextLineId : cons.NextConsequenceId;

                if (cons.HasDialogue && !string.IsNullOrWhiteSpace(cons.Speech))
                {
                    var displayPrompt = new SceneDialogueLineData
                    {
                        SpeakerId = !string.IsNullOrEmpty(cons.SpeakerId) ? cons.SpeakerId : (promptLine != null ? promptLine.SpeakerId : "???"),
                        StageDirection = cons.StageDirection ?? ""
                    };
                    SetupReactionDisplay(displayPrompt, cons.Speech, cons.StageDirection, onwardId, $"🎁 {cons.Title}", rewardsSummary);
                    return true;
                }

                if (!string.IsNullOrEmpty(cons.NextConsequenceId))
                {
                    var next = dataNode.FindConsequence(cons.NextConsequenceId);
                    if (next != null) { cons = next; continue; }
                }
                if (!string.IsNullOrEmpty(cons.NextLineId))
                {
                    int idx = dataNode.FindDialogueIndex(cons.NextLineId);
                    if (idx >= 0)
                    {
                        bool changed = _currentDialogueIndex != idx;
                        _currentDialogueIndex = idx;
                        if (changed) FirePredecessorCinematics(cons.NextLineId);
                        StageCurrentLine();
                        return true;
                    }
                }

                // Cul-de-sac : avancer sans rejouer le lien d'origine (skipLink).
                AdvanceDialogueOrNodeInternal(visited, true);
                return true;
            }
            return true;
        }

        private void ExecuteSkillChallenge(SceneDialogueChoiceData choice, SceneDialogueLineData promptLine)
        {
            var challenge = choice.Challenge;

            TacticalUnit actorUnit = null;
            if (!string.IsNullOrEmpty(challenge.SpecificActorId) && _spawnedActors.TryGetValue(challenge.SpecificActorId, out var specificUnit))
            {
                actorUnit = specificUnit;
            }
            else if (_turnManager != null && _turnManager.ActiveUnit != null && _turnManager.ActiveUnit.IsPlayerControlled)
            {
                actorUnit = _turnManager.ActiveUnit;
            }
            else if (_playerParty.Count > 0)
            {
                actorUnit = _playerParty[0];
            }

            var sheet = actorUnit != null ? actorUnit.Sheet : null;
            string actorName = sheet != null ? sheet.Name : "Protagoniste";
            Attributes effectiveAttr = sheet != null ? sheet.GetEffectiveAttributes() : new Attributes();

            int rank = SkillDefinitions.GetBaseRank(challenge.RequiredSkill, effectiveAttr, false);
            int steps = SkillDefinitions.CharacteristicSteps(rank);
            int training = sheet != null ? sheet.GetSkill(challenge.RequiredSkill).TrainingLevel : 0;
            DiceType skillDie = SkillDefinitions.DieFromTotalSteps(steps + training);

            string skillName = SkillDefinitions.GetDisplayName(challenge.RequiredSkill);
            var roller = new DiceRoller();

            DiceRollResult result;
            string vsLabel;
            string oppSkillName = "";
            string oppName = "";
            int oppTotal = 0;

            if (challenge.IsOpposed)
            {
                // Défi opposé aveugle (Livre II §7) : mises masquées des deux camps,
                // les deux dés sont lancés ensemble et révélés simultanément.
                TacticalUnit oppUnit = null;
                if (!string.IsNullOrEmpty(challenge.OpposedActorId))
                    _spawnedActors.TryGetValue(challenge.OpposedActorId, out oppUnit);
                else if (promptLine != null && !string.IsNullOrEmpty(promptLine.SpeakerId))
                    _spawnedActors.TryGetValue(promptLine.SpeakerId, out oppUnit);

                oppSkillName = SkillDefinitions.GetDisplayName(challenge.OpposedSkill);
                if (oppUnit != null && oppUnit.Sheet != null)
                {
                    var oppSheet = oppUnit.Sheet;
                    oppName = !string.IsNullOrEmpty(oppSheet.Name) ? oppSheet.Name : challenge.OpposedActorId;
                    Attributes oppAttr = oppSheet.GetEffectiveAttributes();
                    int oppRank = SkillDefinitions.GetBaseRank(challenge.OpposedSkill, oppAttr, false);
                    int oppTraining = oppSheet.GetSkill(challenge.OpposedSkill).TrainingLevel;
                    DiceType oppDie = SkillDefinitions.DieFromTotalSteps(SkillDefinitions.CharacteristicSteps(oppRank) + oppTraining);
                    var oppRoll = roller.Roll(oppDie, modifier: challenge.OpposedBonus, targetDC: 0);
                    oppTotal = oppRoll.Total;
                    // Mémorise le brut pour le log.
                    result = roller.Roll(skillDie, modifier: 0, targetDC: oppTotal);
                    vsLabel = $"{actorName} [{skillName} {skillDie}={result.RawRoll} → {result.Total}] vs {oppName} [{oppSkillName} {oppDie}={oppRoll.RawRoll}{(challenge.OpposedBonus != 0 ? $" +{challenge.OpposedBonus}" : "")} → {oppTotal}]";
                }
                else
                {
                    oppName = !string.IsNullOrEmpty(challenge.OpposedActorId) ? challenge.OpposedActorId : "Opposition";
                    oppTotal = challenge.TargetDC + challenge.OpposedBonus;
                    result = roller.Roll(skillDie, modifier: 0, targetDC: oppTotal);
                    vsLabel = $"{actorName} [{skillName} {skillDie}={result.RawRoll} → {result.Total}] vs {oppName} [{oppSkillName} fixe {oppTotal}]";
                }
            }
            else
            {
                result = roller.Roll(skillDie, modifier: 0, targetDC: challenge.TargetDC);
                vsLabel = $"{actorName} teste <b>{skillName}</b> ({skillDie}) contre SD {challenge.TargetDC}";
            }

            string outcomeTag = result.IsSuccess ? (result.IsCriticalSuccess ? "RÉUSSITE CRITIQUE" : "SUCCÈS") : (result.IsCriticalFailure ? "ÉCHEC CRITIQUE" : "ÉCHEC");
            Color outcomeCol = result.IsSuccess ? (result.IsCriticalSuccess ? new Color(1f, 0.85f, 0.2f) : Color.green) : (result.IsCriticalFailure ? Color.red : new Color(1f, 0.5f, 0.2f));

            string defiTag = challenge.IsOpposed ? "DÉFI OPPOSÉ AVEUGLE" : "DÉFI";
            string detailRes = challenge.IsOpposed
                ? $"Révélation simultanée (mises cachées): <b>{result.Total} vs {oppTotal}</b> (Diff: {result.Differential:+0;-0;0})"
                : $"Résultat: <b>{result.Total}</b> (Brut {result.RawRoll}, Diff: {result.Differential:+0;-0;0})";

            CombatHUD.Instance?.AddAdvancedLog(
                $"{(challenge.IsOpposed ? "⚔️" : "🎲")} <b>[{defiTag}]</b> {vsLabel} ➔ {detailRes} — <color=#{ColorUtility.ToHtmlStringRGB(outcomeCol)}>{outcomeTag}</color>",
                LogCategory.Combat,
                "[DÉFI]",
                outcomeCol
            );

            string floatVs = challenge.IsOpposed ? $"{result.Total} vs {oppTotal}" : $"{result.Total} vs SD {challenge.TargetDC}";
            actorUnit?.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                $"{outcomeTag} ({floatVs})",
                outcomeCol
            );

            if (result.IsSuccess)
            {
                if (KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Filter, 0.7f);
                }

                if (challenge.SuccessEffects != null && challenge.SuccessEffects.Count > 0)
                {
                    _director?.ApplyEffects(challenge.SuccessEffects);
                }
                if (choice.Effects != null && choice.Effects.Count > 0)
                {
                    _director?.ApplyEffects(choice.Effects);
                }

                string rewardsSummary = "";
                if (challenge.SuccessRewards != null)
                {
                    var rew = challenge.SuccessRewards;
                    if (rew.EarnCredits > 0 && sheet != null)
                    {
                        sheet.EarnCredits(rew.EarnCredits);
                        rewardsSummary += $"+{rew.EarnCredits} CE  ";
                    }
                    if (rew.EarnXP > 0 && sheet != null)
                    {
                        sheet.AvailableXP += rew.EarnXP;
                        sheet.TotalEarnedXP += rew.EarnXP;
                        rewardsSummary += $"+{rew.EarnXP} XP  ";
                    }
                    if (!string.IsNullOrEmpty(rew.ItemRewardName) && sheet != null)
                    {
                        GrantRewardItem(sheet, rew.ItemRewardName, rew.ItemRewardType, rew.ItemRewardQuantity, ref rewardsSummary);
                    }
                    if (rew.AdditionalItems != null)
                    {
                        for (int ri = 0; ri < rew.AdditionalItems.Count; ri++)
                        {
                            var extra = rew.AdditionalItems[ri];
                            if (extra == null || string.IsNullOrEmpty(extra.ItemName)) continue;
                            GrantRewardItem(sheet, extra.ItemName, extra.ItemRewardType, extra.Quantity, ref rewardsSummary);
                        }
                    }
                    if (!string.IsNullOrEmpty(rew.CompletionObjectiveId))
                    {
                        _director?.SetObjectiveComplete(rew.CompletionObjectiveId, true);
                        rewardsSummary += "✔ Objectif validé  ";
                    }
                }

                if (!string.IsNullOrEmpty(rewardsSummary))
                {
                    CombatHUD.Instance?.AddAdvancedLog($"🎁 <b>Récompenses obtenues :</b> {rewardsSummary}", LogCategory.MovementAndTurns, "[BUTIN]", Color.yellow);
                }

                string targetLineId = !string.IsNullOrWhiteSpace(challenge.SuccessConsequenceId) ? challenge.SuccessConsequenceId
                    : (!string.IsNullOrWhiteSpace(challenge.SuccessNextLineId) ? challenge.SuccessNextLineId : choice.NextLineId);
                string successVs = challenge.IsOpposed ? $"{result.Total} vs {oppTotal}" : $"{result.Total} vs SD {challenge.TargetDC}";
                string successSkill = challenge.IsOpposed ? $"{skillName} vs {oppSkillName}" : skillName;

                // Routage direct vers fenêtre normale (pas de réaction intermédiaire).
                CombatHUD.Instance?.AddAdvancedLog($"✔ {outcomeTag} ({successVs}) — {successSkill}{(string.IsNullOrEmpty(rewardsSummary) ? "" : $" 🎁 {rewardsSummary}")}", LogCategory.Combat, "[DÉFI]", Color.green);
                if (!TryAdvanceToId(targetLineId, null))
                    AdvanceDialogueOrNode();
            }
            else
            {
                if (KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.Play(SoundId.Impact_DeepBoom, 0.6f);
                }
                // Conservé : micro-secousse d'IMPACT (feedback d'échec, pas un cadrage
                // ni un travel — les défis n'ont pas de sortie vers cartes 🎬).
                GrenadeCameraShake.Shake(0.2f);

                if (challenge.FailureEffects != null && challenge.FailureEffects.Count > 0)
                {
                    _director?.ApplyEffects(challenge.FailureEffects);
                }

                if (challenge.FailureTriggersCombat)
                {
                    SpawnActorsForNode(_director?.CurrentNode?.Id, true);
                    _turnManager?.EnterCombatMode();
                    CombatHUD.Instance?.AddAdvancedLog("🚨 <b>ÉCHEC CRITIQUE :</b> Alerte déclenchée ! Déploiement d'urgence hostile.", LogCategory.Combat, "[ALERTE]", Color.red);
                }

                string targetLineId = !string.IsNullOrWhiteSpace(challenge.FailureConsequenceId) ? challenge.FailureConsequenceId
                    : (!string.IsNullOrWhiteSpace(challenge.FailureNextLineId) ? challenge.FailureNextLineId : choice.NextLineId);
                string failVs = challenge.IsOpposed ? $"{result.Total} vs {oppTotal}" : $"{result.Total} vs SD {challenge.TargetDC}";
                string failSkill = challenge.IsOpposed ? $"{skillName} vs {oppSkillName}" : skillName;

                // Routage direct vers fenêtre normale (pas de réaction intermédiaire).
                CombatHUD.Instance?.AddAdvancedLog($"✕ {outcomeTag} ({failVs}) — {failSkill}", LogCategory.Combat, "[DÉFI]", new Color(1f, 0.5f, 0.2f));
                if (!TryAdvanceToId(targetLineId, null))
                    AdvanceDialogueOrNode();
            }
        }

        private void SetupReactionDisplay(SceneDialogueLineData promptLine, string speech, string stageDirection, string nextLineId, string challengeSummary = "", string rewardsText = "")
        {
            _activeReactionChoice = new SceneDialogueChoiceData
            {
                ReactionSpeech = speech,
                ReactionStageDirection = stageDirection,
                NextLineId = nextLineId
            };
            _activeReactionPromptLine = promptLine;
            _activeChallengeSummary = challengeSummary;
            _activeChallengeRewards = rewardsText;

            // DÉ-HARDCODÉ : plus de focus caméra direct ici (migré en cartes 🎬 à tracking).

            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Filter, 0.45f);
            }

            string stage = !string.IsNullOrEmpty(stageDirection) ? $" <i>({stageDirection})</i>" : "";
            CombatHUD.Instance?.AddAdvancedLog($"<b>[{promptLine.SpeakerId}]</b>{stage} {speech}", LogCategory.MovementAndTurns, "[RÉPONSE]", Color.cyan);
        }

        private void ConfirmReactionAndAdvance()
        {
            if (_activeReactionChoice == null) return;

            var choice = _activeReactionChoice;
            _activeReactionChoice = null;
            _activeReactionPromptLine = null;
            _activeChallengeSummary = "";
            _activeChallengeRewards = "";

            // File d'attente du chat milieu (ex : holotable puis datapad) :
            // afficher le message suivant avant de reprendre le flux normal.
            if (_pendingMiddleChat.Count > 0)
            {
                ShowMiddleChatMessage(_pendingMiddleChat.Dequeue());
                return;
            }

            // Sentinelle info interactable (hérité) : dismiss pur, sans avancer
            // dialogue ni nœud — on retrouve l'état sous-jacent inchangé.
            if (string.Equals(choice.NextLineId, InteractableInfoReturnSentinel, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(choice.NextLineId))
            {
                if (TryAdvanceToId(choice.NextLineId, null)) return;
            }

            // Sans cible = fin du nœud (routage explicite).
            AdvanceDialogueOrNodeInternal(null, true);
        }

        private void OnGUI()
        {
            if (!_isSceneInitialized) return;
            if (CombatHUD.IsPaused || (StorySceneManager.Instance != null && StorySceneManager.Instance.IsTransitioning)) return;

            var director = FindAnyObjectByType<CinematicDirector>();
            bool hideByCine = director != null && director.IsPlayingSceneCinematic
                && (director.CurrentPlayingSceneCinematic == null || director.CurrentPlayingSceneCinematic.HideSceneChat);

            if (hideByCine && _chatAnimProgress <= 0.001f)
            {
                _dialogueRect = Rect.zero;
                _partyBarRect = Rect.zero;
                _hasPartyBar = false;
                _interactButtonRects.Clear();
                return;
            }

            _interactButtonRects.Clear();
            if (!hideByCine)
            {
                DrawPartySwitchBar();
                DrawInteractablesWorldOverlay();
                DrawInteractablePopups();
            }

            var node = _director != null ? _director.CurrentNode : null;
            if (node != null)
            {
                _lastActiveNode = node;
            }

            var nodeToDraw = node ?? _lastActiveNode;
            if (nodeToDraw == null || _chatAnimProgress <= 0.001f)
            {
                _dialogueRect = Rect.zero;
                return;
            }

            DrawDialogueBox(nodeToDraw);
        }

        private void DrawPartySwitchBar()
        {
            if (_turnManager == null || !_turnManager.IsInExploration || _playerParty.Count <= 1)
            {
                _hasPartyBar = false;
                _partyBarRect = Rect.zero;
                return;
            }

            float barWidth = _playerParty.Count * 110f;
            Rect rect = new Rect(24f, 96f, barWidth, 38f);
            _partyBarRect = rect;
            _hasPartyBar = true;
            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < _playerParty.Count; i++)
            {
                var unit = _playerParty[i];
                bool active = (_turnManager.ActiveUnit == unit);
                GUI.backgroundColor = active ? new Color(0.2f, 0.8f, 1f) : Color.white;
                if (GUILayout.Button($"[{i + 1}] {unit.Stats.Name}", GUILayout.Height(28)))
                {
                    SelectOperative(unit);
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawInteractablesWorldOverlay()
        {
            if (UnityEngine.Camera.main == null || _turnManager == null || _turnManager.ActiveUnit == null) return;

            var activeUnit = _turnManager.ActiveUnit;

            for (int i = 0; i < _interactables.Count; i++)
            {
                var it = _interactables[i];
                if (it == null || !it.CanInteract(activeUnit)) continue;

                var node = _grid?.GetNode(it.Coordinates);
                Vector3 worldPos = node != null ? node.WorldPosition + Vector3.up * 1.2f : it.transform.position;
                Vector3 screenPos = UnityEngine.Camera.main.WorldToScreenPoint(worldPos);

                if (screenPos.z > 0)
                {
                    float y = Screen.height - screenPos.y;
                    Rect r = new Rect(screenPos.x - 90f, y - 24f, 180f, 26f);
                    _interactButtonRects.Add(r);
                    GUI.backgroundColor = new Color(0.2f, 0.9f, 0.5f);
                    if (GUI.Button(r, $"[E] {it.ActionLabel}"))
                    {
                        it.TryInteract(activeUnit, out string log);
                        if (!string.IsNullOrEmpty(log))
                            CombatHUD.Instance?.AddAdvancedLog(log, LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
        }

        /// <summary>
        /// Popups monde des interactables : ancrés au-dessus de l'objet,
        /// bloqueurs de clics 3D, info en fondu auto, offre de saut persistante.
        /// </summary>
        private void DrawInteractablePopups()
        {
            if (_activePopups.Count == 0 || UnityEngine.Camera.main == null) return;
            var snapshot = _activePopups.ToArray();
            var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
            for (int s = 0; s < snapshot.Length; s++)
            {
                var popup = snapshot[s];
                if (popup == null || !_activePopups.Contains(popup)) continue;
                if (!TryGetSpawnedInteractable(popup.InteractableId, out var prop) || prop == null) continue;

                var hexNode = _grid?.GetNode(prop.Coordinates);
                Vector3 worldPos = hexNode != null
                    ? hexNode.WorldPosition + Vector3.up * 2.1f
                    : prop.transform.position + Vector3.up * 2.1f;
                Vector3 sp = UnityEngine.Camera.main.WorldToScreenPoint(worldPos);
                if (sp.z <= 0f) { popup.LastScreenRect = Rect.zero; continue; }

                float w = 300f;
                float msgH = string.IsNullOrEmpty(popup.Message) ? 0f : wrap.CalcHeight(new GUIContent(popup.Message), w - 20f);
                msgH = Mathf.Clamp(msgH, 0f, 120f);
                float h = Mathf.Clamp(34f + msgH + (popup.HasJump ? 64f : 12f), 80f, 230f);
                float x = Mathf.Clamp(sp.x - w * 0.5f, 8f, Mathf.Max(8f, Screen.width - w - 8f));
                float yBase = Screen.height - sp.y;
                float y = yBase - h - 34f;
                if (y < 60f) y = yBase + 26f;
                Rect r = new Rect(x, y, w, h);
                // Le popup NE bloque les clics 3D que sur ses boutons (voir
                // BlockPopupButton) : le titre et le message laissent passer,
                // sinon un popup persistant recouvre les personnages derrière
                // et les rend insélectionnables.
                popup.LastScreenRect = r;

                float alpha = 1f;
                if (!popup.HasJump)
                {
                    float remain = InteractablePopupAutoCloseDelay + InteractablePopupFadeDuration
                        - (Time.unscaledTime - popup.CreatedUnscaledTime);
                    if (remain <= 0f) { popup.LastScreenRect = Rect.zero; continue; } // purgé par PruneInteractablePopups
                    if (remain <= InteractablePopupFadeDuration)
                        alpha = Mathf.Clamp01(remain / InteractablePopupFadeDuration);
                }

                Color prevColor = GUI.color;
                Color prevBg = GUI.backgroundColor;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Box(r, GUIContent.none);
                Rect area = new Rect(r.x + 8f, r.y + 4f, r.width - 16f, r.height - 8f);
                GUILayout.BeginArea(area);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"🔧 {popup.DisplayName}", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(20)))
                {
                    DismissPopup(popup);
                }
                BlockPopupButton(area);
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(popup.Message))
                {
                    GUILayout.Label(popup.Message, wrap);
                }
                if (popup.HasJump)
                {
                    GUILayout.Space(4);
                    GUILayout.BeginHorizontal();
                    GUI.backgroundColor = new Color(0.25f, 0.85f, 0.45f);
                    if (GUILayout.Button($"Suivre ➔ {popup.TargetNodeId}", GUILayout.Height(26)))
                    {
                        FollowPopupJump(popup);
                    }
                    BlockPopupButton(area);
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("Rester", GUILayout.Width(70), GUILayout.Height(26)))
                    {
                        DismissPopup(popup);
                    }
                    BlockPopupButton(area);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndArea();
                GUI.color = prevColor;
                GUI.backgroundColor = prevBg;
            }
        }

        /// <summary>
        /// Seuls les boutons du popup bloquent les clics 3D (la carte derrière
        /// reste cliquable : sélection et déplacement possibles sous le popup).
        /// </summary>
        private void BlockPopupButton(Rect area)
        {
            try
            {
                Rect last = GUILayoutUtility.GetLastRect();
                last.x += area.x;
                last.y += area.y;
                if (last.width > 0f && last.height > 0f)
                    _interactButtonRects.Add(last);
            }
            catch { /* ignore */ }
        }

        private void DrawDialogueBox(ScenarioNode node)
        {
            var dataNode = _sceneData.FindNode(node.Id);
            bool hasDialogues = dataNode != null && dataNode.Dialogues.Count > 0 && _currentDialogueIndex < dataNode.Dialogues.Count;

            var line = hasDialogues ? dataNode.Dialogues[_currentDialogueIndex] : null;
            bool isReactionActive = _activeReactionChoice != null && _activeReactionPromptLine != null;
            bool hasChoices = !isReactionActive && line != null && line.Choices.Count > 0;

            float boxWidth = Mathf.Min(920f, Screen.width - 40f);
            float baseHeight = 150f;
            if (hasChoices)
            {
                baseHeight += line.Choices.Count * 34f;
            }
            else if (isReactionActive)
            {
                baseHeight += 20f;
            }

            float boxHeight = Mathf.Min(baseHeight, Screen.height - 120f);
            float targetY = Screen.height - boxHeight - 20f;
            float offscreenY = Screen.height + 25f;
            float eased = Mathf.SmoothStep(0f, 1f, _chatAnimProgress);
            float currentY = Mathf.Lerp(offscreenY, targetY, eased);

            Rect rect = new Rect((Screen.width - boxWidth) * 0.5f, currentY, boxWidth, boxHeight);
            _dialogueRect = _chatAnimProgress > 0.05f ? rect : Rect.zero;

            Color prevGuiColor = GUI.color;
            GUI.color = new Color(prevGuiColor.r, prevGuiColor.g, prevGuiColor.b, prevGuiColor.a * eased);

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(rect);
            GUILayout.Space(6);

            if (isReactionActive)
            {
                var promptLine = _activeReactionPromptLine;
                var reaction = _activeReactionChoice;

                GUILayout.BeginHorizontal();
                GUI.color = Color.cyan;
                GUILayout.Label($"<b>【 {promptLine.SpeakerId} 】</b>", GUILayout.Width(180));
                GUI.color = Color.white;

                if (!string.IsNullOrEmpty(reaction.ReactionStageDirection))
                {
                    GUILayout.Label($"<color=grey><i>({reaction.ReactionStageDirection})</i></color>");
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("<color=#00E5FF>[RÉACTION]</color>", GUILayout.Width(90));
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_activeChallengeSummary))
                {
                    GUILayout.BeginHorizontal();
                    GUI.color = _activeChallengeSummary.StartsWith("✔") ? Color.green : new Color(1f, 0.45f, 0.35f);
                    GUILayout.Label($"<b>{_activeChallengeSummary}</b>");
                    GUI.color = Color.white;
                    if (!string.IsNullOrEmpty(_activeChallengeRewards))
                    {
                        GUI.color = Color.yellow;
                        GUILayout.Label($"🎁 {_activeChallengeRewards}");
                        GUI.color = Color.white;
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(4);
                GUILayout.Label($"« {reaction.ReactionSpeech} »", GUILayout.ExpandHeight(true));

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                if (GUILayout.Button("Continuer (Espace) ➔", GUILayout.Width(200), GUILayout.Height(30)))
                {
                    ConfirmReactionAndAdvance();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
            else if (hasDialogues)
            {
                if (line.IsNarration || string.IsNullOrEmpty(line.SpeakerId) || string.Equals(line.SpeakerId, "Narrateur", StringComparison.OrdinalIgnoreCase))
                {
                    GUILayout.BeginHorizontal();
                    GUI.color = new Color(0.95f, 0.82f, 0.45f);
                    GUILayout.Label("<b>📜 NARRATION</b>", GUILayout.Width(180));
                    GUI.color = Color.white;

                    if (!string.IsNullOrEmpty(line.StageDirection))
                    {
                        GUILayout.Label($"<color=grey><i>({line.StageDirection})</i></color>");
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"<color=grey>{_currentDialogueIndex + 1} / {dataNode.Dialogues.Count}</color>", GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4);
                    GUILayout.Label($"<color=#E0E8F0><i>{line.Speech}</i></color>");
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUI.color = Color.cyan;
                    GUILayout.Label($"<b>【 {line.SpeakerId} 】</b>", GUILayout.Width(180));
                    GUI.color = Color.white;

                    if (!string.IsNullOrEmpty(line.StageDirection))
                    {
                        GUILayout.Label($"<color=grey><i>({line.StageDirection})</i></color>");
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"<color=grey>{_currentDialogueIndex + 1} / {dataNode.Dialogues.Count}</color>", GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4);
                    GUILayout.Label($"« {line.Speech} »");
                }

                if (hasChoices)
                {
                    GUILayout.Space(6);
                    GUILayout.Label("<color=#FFD54F><b>Faites votre choix :</b></color>");
                    for (int c = 0; c < line.Choices.Count; c++)
                    {
                        var choice = line.Choices[c];
                        bool isMet = choice.Prerequisite == null || !choice.Prerequisite.HasPrerequisite || choice.Prerequisite.IsMet(_playerParty, _turnManager?.ActiveUnit, _director);

                        bool hasChallenge = choice.Challenge != null && choice.Challenge.HasChallenge;
                        bool isOpposed = hasChallenge && choice.Challenge.IsOpposed;
                        string prefix = !isMet
                            ? $"🔒 [{choice.Prerequisite.GetSummary()}] "
                            : (hasChallenge
                                ? (isOpposed
                                    ? $"⚔️ [{choice.Challenge.GetShortLabel()}] "
                                    : $"🎲 [{choice.Challenge.GetShortLabel()}] ")
                                : "➤  ");
                        if (isMet && !string.IsNullOrEmpty(choice.NextLineId) && dataNode != null && dataNode.FindConsequence(choice.NextLineId) != null)
                            prefix += "🎁 ";

                        string btnLabel = $"{prefix}{choice.Label}";

                        GUI.enabled = isMet;
                        GUI.backgroundColor = !isMet
                            ? Color.gray
                            : (hasChallenge
                                ? (isOpposed ? new Color(1.0f, 0.55f, 0.25f) : new Color(1.0f, 0.78f, 0.25f))
                                : new Color(0.25f, 0.75f, 1.0f));

                        if (GUILayout.Button(btnLabel, GUILayout.Height(30)))
                        {
                            SelectDialogueChoice(choice, line);
                        }

                        GUI.backgroundColor = Color.white;
                        GUI.enabled = true;
                    }
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();

                    // Liaison vide = fin du nœud : retour de conversation prioritaire,
                    // sinon bouton Suivant vers la cible routée ou boutons d'actions.
                    if (!string.IsNullOrEmpty(line.NextLineId))
                    {
                        if (GUILayout.Button("Suivant (Espace) ➔", GUILayout.Width(170), GUILayout.Height(28)))
                        {
                            AdvanceDialogueOrNode();
                        }
                    }
                    else if (HasActiveTalkReturn())
                    {
                        GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                        if (GUILayout.Button("↩ Revenir (Espace)", GUILayout.Width(190), GUILayout.Height(28)))
                        {
                            TryReturnFromTalk();
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    else
                    {
                        DrawActionButtons(node);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label($"<b>{node.Title}</b> — <color=orange>{node.Location}</color>");
                GUILayout.Label(node.Body);
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                DrawActionButtons(node);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
            GUI.color = prevGuiColor;
        }

        private void DrawActionButtons(ScenarioNode node)
        {
            if (HasActiveTalkReturn())
            {
                GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                if (GUILayout.Button("↩ Revenir à la conversation", GUILayout.Width(240), GUILayout.Height(28)))
                {
                    TryReturnFromTalk();
                }
                GUI.backgroundColor = Color.white;
                return;
            }

            if (node.Kind == ScenarioNodeKind.Briefing)
            {
                GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                if (GUILayout.Button($"➔ {node.ContinueLabel}", GUILayout.Width(240), GUILayout.Height(28)))
                {
                    if (TryConsumePendingJump()) { GUI.backgroundColor = Color.white; return; }
                    if (TryReturnFromTalk()) { GUI.backgroundColor = Color.white; return; }
                    if (!TryFireReadyTrigger() && !_director.Continue()) _director.CompleteActiveScenario();
                }
                GUI.backgroundColor = Color.white;
            }
            else if (node.Kind == ScenarioNodeKind.Choice)
            {
                // Choix explicite : le joueur choisit, le saut différé attend
                // (pas de consommation au dessin, seulement au clic Continuer
                // ci-dessous ou en sortie de dialogue).
                if (TryFireReadyTrigger()) return;
                if (node.Choices != null && node.Choices.Count > 0)
                {
                    foreach (var choice in node.Choices)
                    {
                        if (GUILayout.Button(choice.Label, GUILayout.Height(28)))
                        {
                            _director.Choose(choice.Id);
                        }
                    }
                }
                else
                {
                    GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                    string label = !string.IsNullOrWhiteSpace(node.ContinueLabel) ? node.ContinueLabel : "Continuer";
                    if (GUILayout.Button($"➔ {label}", GUILayout.Width(240), GUILayout.Height(28)))
                    {
                        if (TryConsumePendingJump()) { GUI.backgroundColor = Color.white; return; }
                        if (!TryFireReadyTrigger() && !_director.Continue()) TryReturnFromTalk();
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
            else if (node.Kind == ScenarioNodeKind.Objective)
            {
                bool complete = _director.AreRequiredObjectivesComplete();
                GUI.enabled = complete;
                GUI.backgroundColor = complete ? new Color(0.25f, 0.85f, 0.45f) : Color.gray;
                if (GUILayout.Button(complete ? $"✔ {node.ContinueLabel}" : "Objectifs requis non validés", GUILayout.Width(260), GUILayout.Height(28)))
                {
                    if (TryConsumePendingJump()) { GUI.backgroundColor = Color.white; GUI.enabled = true; return; }
                    if (!TryFireReadyTrigger() && !_director.Continue()) TryReturnFromTalk();
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }
            else if (node.Kind == ScenarioNodeKind.Resolution)
            {
                GUI.backgroundColor = new Color(0.2f, 0.9f, 0.5f);
                string btnText = string.IsNullOrWhiteSpace(node.ContinueLabel) ? "✔ Conclure & Scène Suivante ➔" : $"✔ {node.ContinueLabel} ➔";
                if (GUILayout.Button(btnText, GUILayout.Width(280), GUILayout.Height(28)))
                {
                    if (TryConsumePendingJump()) { GUI.backgroundColor = Color.white; return; }
                    // Un "Parler à" atterri sur une résolution revient d'abord à
                    // la conversation principale au lieu de clore la scène.
                    if (!TryFireReadyTrigger() && !TryReturnFromTalk()) _director.CompleteActiveScenario();
                }
                GUI.backgroundColor = Color.white;
            }
        }

        public void CleanupScene()
        {
            _isSceneInitialized = false;
            _chatAnimProgress = 0f;
            _lastActiveNode = null;
            Killtime.Tactics.Units.DroppedWeaponPickup.ClearAllDropped();
            foreach (var kvp in _spawnedActors)
            {
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            }
            _spawnedActors.Clear();
            _playerParty.Clear();

            for (int i = 0; i < _interactables.Count; i++)
            {
                if (_interactables[i] != null) Destroy(_interactables[i].gameObject);
            }
            _interactables.Clear();

            _grid?.ClearOccupancy();
            _turnManager?.ClearUnits();
            _activePopups.Clear();
            ClearPendingNodeJump();
            if (_overheadHudOverridden)
            {
                _overheadHudOverridden = false;
                TacticalUnitVisual.ShowOverheadHUD = _savedOverheadHud;
            }

            if (gameObject != null) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            FloatingWindowChrome.UnregisterExtraBlocker(IsPointerOverSceneChat);
            _dialogueRect = Rect.zero;
            _partyBarRect = Rect.zero;
            _hasPartyBar = false;
            _interactButtonRects.Clear();
            CleanupScene();
        }
    }
}