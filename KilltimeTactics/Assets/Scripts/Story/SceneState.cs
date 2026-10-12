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
    internal struct PendingMiddleChatMessage
    {
        public string Speaker;
        public string StageDirection;
        public string Speech;
        public string ActionLabel;
        public string DisplayName;
        public string Rewards;
    }

    internal sealed class InteractablePopup
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

    /// <summary>
    /// État centralisé du contrôleur de scène (extrait move-only de la façade).
    /// Source unique de vérité : les partiels y accèdent via <c>State._champ</c>.
    /// </summary>
    internal sealed class SceneState
    {
        internal TacticalHexGrid _grid;

        internal TurnManager _turnManager;

        internal CombatDevArena _arena;

        internal ScenarioDirector _director;

        internal StorySceneData _sceneData;

        internal readonly Dictionary<string, TacticalUnit> _spawnedActors = new(StringComparer.OrdinalIgnoreCase);

        internal readonly List<TacticalUnit> _playerParty = new();

        internal readonly List<TacticalInteractable> _interactables = new();

        internal readonly HashSet<string> _firedTriggerIds = new(StringComparer.OrdinalIgnoreCase);

        internal readonly HashSet<string> _activatedInteractableIds = new(StringComparer.OrdinalIgnoreCase);

        // Consommation one-shot au niveau action (persiste à la session, pas de save disque en V1).
        internal readonly HashSet<string> _consumedActionKeys = new(StringComparer.OrdinalIgnoreCase);

        // Cartes à payload (événements, conséquences) déjà exécutées depuis l'entrée du
        // nœud courant. Évite les doubles payloads quand une chaîne "fin →" revient sur
        // une carte déjà résolue (ex : event rejoué à la fin de sa propre cinématique amont).
        // Vidé à chaque entrée de nœud (revenir = rejouer, comportement normal).
        internal readonly HashSet<string> _resolvedCardIdsThisNode = new(StringComparer.OrdinalIgnoreCase);

        // Nœud courant au moment de chaque activation : un trigger InteractableActivated
        // scoped sur un nœud source ne tire que si l'activation a eu lieu PENDANT ce
        // nœud (sinon une utilisation précoce au nœud précédent skippe instantanément
        // le nœud source dès son entrée). Triggers globaux (Source vide) : inchangés.
        internal readonly Dictionary<string, string> _activationNodeIds = new(StringComparer.OrdinalIgnoreCase);

        // Positions du groupe à l'entrée du nœud courant : la validation par proximité
        // exige un déplacement (ou un hors-portée initial), sinon un spawn sur l'objectif
        // valide gratuitement dès la première frame et skippe le nœud.
        internal readonly Dictionary<TacticalUnit, HexCoordinates> _nodeEnterCoords = new();

        internal bool _isSceneInitialized = false;

        internal int _currentDialogueIndex = 0;

        internal string _lastNodeId = "";

        // Suivi des cartes son 🔊 : fin de réplique (changement de mise en scène)
        // et fin de nœud (transition). Vide = rien à clore.
        internal string _stagedSoundLineId = "";

        internal string _activeSoundNodeId = "";

        // Anti-spam des cartes son 🔊 : dernier déclenchement par SoundId de carte.
        internal readonly Dictionary<string, float> _soundCooldowns = new(StringComparer.OrdinalIgnoreCase);

        // Génération de scène : invalide les sons en délai d'une scène précédente
        // (changement de scène pendant un DelaySeconds → aucun son fantôme).
        internal int _sceneGeneration;

        internal float _chatAnimProgress = 0f;

        internal ScenarioNode _lastActiveNode = null;

        internal SceneDialogueChoiceData _activeReactionChoice = null;

        internal SceneDialogueLineData _activeReactionPromptLine = null;

        internal string _activeChallengeSummary = "";

        internal string _activeChallengeRewards = "";

        // File d'attente des textes interactables vers le chat milieu :
        // si holotable + datapad se valident coup sur coup, le 2e ne doit pas
        // être perdu quand une réaction est déjà affichée.
        internal readonly Queue<PendingMiddleChatMessage> _pendingMiddleChat = new();

        // Rects IMGUI du chat de scène : servent de bloqueurs clics 3D via FloatingWindowChrome.
        internal Rect _dialogueRect = Rect.zero;

        internal Rect _partyBarRect = Rect.zero;

        internal bool _hasPartyBar;

        internal readonly List<Rect> _interactButtonRects = new();

        // Popups monde ancrés aux interactables : le SuccessLog part dans le journal
        // latéral + dans un popup au-dessus de l'objet (jamais dans le chat central,
        // pour ne pas hijacker la conversation en cours). Info = fondu auto + ✕ manuel,
        // saut = persistant avec [Suivre ➔] / [Rester].
        internal readonly List<InteractablePopup> _activePopups = new();

        // Saut différé : un interactable avec TriggerNodeId activé pendant une
        // conversation ne saute plus immédiatement ; la cible attend ici jusqu'au
        // clic [Suivre] ou à la sortie naturelle du nœud.
        internal string _pendingNodeJump = "";

        internal string _pendingNodeJumpSource = "";

        // Réplique "Parler à" en attente après un saut de nœud : TalkToActor a
        // enclenché un nœud différent du courant pour jouer une réplique précise ;
        // consommée par UpdateNodeState une fois l'entrée du nœud rejouée.
        internal string _pendingTalkLineId = "";

        // Point de retour "Parler à" : nœud (+ réplique) d'origine et nœud
        // d'atterrissage. À la fin naturelle du nœud d'atterrissage (bouton
        // "Passer à l'action" sans suite), on revient à la conversation
        // d'origine au lieu de clore la scène (cf. TryReturnFromTalk).
        internal string _talkReturnNodeId = "";

        internal string _talkReturnLineId = "";

        internal string _talkLandingNodeId = "";

        internal bool _isReturningFromTalk = false;

        // Boîtes d'info personnages : masquées par défaut en mode scène,
        // valeur précédente restaurée à la sortie (touche N ou dev UI pour forcer).
        internal bool _overheadHudOverridden;

        internal bool _savedOverheadHud = true;

        // Zones d'exclusion story (barre party, boîte dialogue) : en mode story scene
        // le HUD a des boutons de plus que le HUD combat ; sans ça les vignettes
        // dockées et les ouvertures de fenêtres les recouvrent.
        internal System.Func<Rect> _partyBarZone;

        internal System.Func<Rect> _dialogueBoxZone;
    }
}
