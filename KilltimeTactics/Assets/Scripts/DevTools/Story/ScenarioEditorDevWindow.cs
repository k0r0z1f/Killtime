using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;

namespace Killtime.Story
{
    public partial class ScenarioEditorDevWindow : FloatingWindow<ScenarioEditorDevWindow>
    {
        protected override int WindowId => 991;
        protected override string Title => "Éditeur de Scènes Narratives & Graphe Nodal (Shift+F2)";
        protected override Vector2 MinSize => new Vector2(980f, 640f);
        protected override Rect DefaultRect => new Rect(20f, 40f, Mathf.Min(1280f, Screen.width - 40f), Mathf.Min(840f, Screen.height - 60f));
        protected override KeyCode[] ToggleKeys => new[] { KeyCode.F2 };
        protected override bool RequireShift => true;

        private readonly string[] _tabs = { "📋 Scène & Fichier", "🎬 Nœuds & Graphe Nodal" };
        private int _selectedTab = 1;
        private Vector2 _scroll;
        private bool _showRawJsonPreview = false;

        private StorySceneData _data = new();
        private string _activeFilePath = "";

        private Vector2 _graphPan = new Vector2(80f, 80f);
        private float _zoom = 1.0f;
        private string _draggingCardKey = null;
        private string _resizingCardKey = null;
        private string _draggingNodeId = null;
        private Vector2 _dragOffset;
        private Vector2 _dragNodeStartMouse;
        private bool _isPanning = false;
        private Vector2 _panStartMouse;
        private Vector2 _panStartOffset;

        private struct WireConnectionDraft
        {
            public bool IsActive;
            public bool FromChoice;
            public bool FromAutoCheck;
            public bool IsAutoSuccess;
            public bool IsEvent;
            public bool IsTrigger;
            public bool IsInteractable;
            public bool IsNode;
            public bool IsNodeChoice;
            public int NodeChoiceIndex;
            public bool FromConsequence;
            public bool ConsLinkToCons;
            public bool FromChallengeLink;
            public bool ChallengeLinkIsSuccess;
            // Liaison vers cinématique : n'importe quelle carte → carte 🎬 (entrée seule).
            // SourceCineList = référence directe de la liste CinematicIds de la carte source.
            public bool IsCinematicLink;
            public List<string> SourceCineList;
            // Sortie "fin →" d'une carte 🎬 vers n'importe quelle carte (chaînage explicite).
            public bool IsCinematicOut;
            public SceneCinematicData SourceCinematic;
            // Branchement son 🔊 : depuis le BAS d'une carte (sauf frames de nœuds)
            // vers une carte son. Le lien vit sur la carte son (SourceCardId).
            public bool IsSoundLink;
            public string SourceSoundLinkCardId;
            // Branchement arrêt son 🔊 : depuis le BAS d'une carte son vers le bas
            // d'une carte cible (miroir de l'arrêt d'ambiance). Le lien vit sur la
            // carte son (StopSourceCardId).
            public bool IsSoundStopLink;
            public SceneSoundData SourceSoundStop;
            // Branchement arrêt ambiance 🌌 : depuis le BAS d'une carte ambiance vers le bas d'une carte cible.
            public bool IsAmbienceStopLink;
            public SceneAmbienceTrackData SourceAmbienceTrack;
            // Sortie "Parler à" d'une carte 👥 Acteur vers une réplique (LineId)
            // ou un nœud (NodeId) — TalkEntries[SourceTalkIndex].TargetId.
            public bool IsActorTalk;
            public SceneActorSpawnData SourceActor;
            public int SourceTalkIndex;
            public SceneDialogueLineData SourceLine;
            public SceneDialogueChoiceData SourceChoice;
            public SceneDialogueChallengeData SourceChallenge;
            public SceneEventData SourceEvent;
            public SceneTriggerData SourceTrigger;
            public SceneInteractableSpawnData SourceInteractable;
            public SceneNodeData SourceNode;
            public SceneConsequenceData SourceConsequence;
            public Vector2 StartPos;
        }

        private WireConnectionDraft _wireDraft;
        private readonly Dictionary<string, Vector2> _cachedInputSockets = new();
        private readonly Dictionary<string, Vector2> _cachedTriggerOutSockets = new();
        private readonly Dictionary<string, Vector2> _cachedInteractableOutSockets = new();
        private readonly Dictionary<string, Vector2> _cachedActorInSockets = new();
        private readonly Dictionary<string, Vector2> _cachedActorTalkOutSockets = new(StringComparer.OrdinalIgnoreCase);
        // Entrées des cartes cinématiques 🎬 (input seul, jamais de sortie).
        private readonly Dictionary<string, Vector2> _cachedCinematicInputSockets = new(StringComparer.OrdinalIgnoreCase);
        // Cartes son 🔊 : entrées (haut, reçoivent le fil du bas des cartes).
        // Les sorties (bas des cartes sources) sont résolues à la volée par
        // TryGetSoundSourceBottom (positions monde, pas de cache à invalider).
        private readonly Dictionary<string, Vector2> _cachedSoundInputSockets = new(StringComparer.OrdinalIgnoreCase);
        // Sorties d'arrêt des cartes son 🔊 (bas, fil rouge vers la carte cible).
        private readonly Dictionary<string, Vector2> _cachedSoundStopSockets = new(StringComparer.OrdinalIgnoreCase);
        // Pré-écoutes de cartes son 🔊 (éditeur uniquement, jamais sérialisées).
        private readonly Dictionary<string, AudioSource> _soundPreviewSources = new(StringComparer.OrdinalIgnoreCase);
        // Panneaux audio (banque / audit / monitor) : état purement éditeur.
        private bool _showBankBrowser;
        private string _bankSearch = "";
        private Vector2 _bankScroll;
        private float _bankTestVolume = 0.8f;
        private AudioSource _bankPreviewSrc;
        private string _bankPreviewCue = "";
        private bool _showAudioAudit;
        private Vector2 _auditScroll;
        private readonly List<string> _auditLines = new();
        private int _auditErrorCount;
        private bool _auditDirty = true;
        private bool _showAudioMonitor;
        private Vector2 _monitorScroll;
        // Cartes ambiance 🌌 : entrées supérieures et sorties d'arrêt inférieures.
        private readonly Dictionary<string, Vector2> _cachedAmbienceInputSockets = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector2> _cachedAmbienceStopSockets = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string text, float lastVal)> _floatInputBuffers = new();
        private bool _hasInitializedLayout = false;

        // --- Quick-create : drop d'un lien dans le vide → menu contextuel (style Blueprint) ---
        // Le brouillon actif est copié ici au moment du MouseUp dans le vide, avec la
        // position monde du drop et la position écran pour ancrer la popup.
        private enum QuickCreateKind { Dialogue, Event, Consequence, Node, Cinematic, Sound, Ambience }
        private bool _quickCreateOpen;
        private WireConnectionDraft _quickCreateDraft;
        private Vector2 _quickCreateWorldPos;
        private Vector2 _quickCreateScreenPos;
        private string _quickCreateSearch = "";
        private Vector2 _quickCreateScroll;
        private Rect _quickCreatePanelRect;

        // --- Anti-vol de drag (cartes chevauchantes) ---
        // Une carte peinte AVANT une autre peut recouvrir son header : son champ texte,
        // créé plus tôt dans la passe IMGUI, vole alors le MouseDown destiné au
        // déplacement (clic sur le header => sélection de texte derrière).
        // La pré-passe ci-dessous est purement mathématique (aucun appel GUI : elle ne
        // perturbe pas la séquence des IDs de contrôles) : elle réserve le geste sur le
        // MouseDown, et la prise effective a lieu au premier MouseDrag (seuil 6px).
        private object _lateDragTarget;
        private object _lateResizeTarget;
        private Vector2 _lateDownWorld;
        private Vector2 _lateDownScreen;

        // Seuil de zoom en dessous duquel les contrôles d'édition internes sont remplacés par un rendu LOD rapide.
        private const float CardLodThreshold = 0.42f;

        // Couleur unique des ports/wires cinématiques + hauteur standard de la rangée 🎬.
        private static readonly Color CinePortCol = new Color(1f, 0.35f, 0.85f);
        private const float CineRowH = 20f;
        // Position verticale (relative carte) des ports de sortie 🎬 sur le flanc droit.
        private const float CinePortTopCards = 46f;
        private const float ActorCinePortTop = 26f;
        // Sorties "Parler à" 💬 des cartes 👥 Acteur (TalkEntries) : une rangée
        // de 20px par entrée après l'en-tête, port cyan sur le flanc droit au
        // milieu de chaque rangée (comme les sorties de répliques).
        private const float ActorTalkFirstRowTop = 170f;
        private const float ActorTalkRowH = 20f;
        private static readonly Color TalkPortCol = new Color(0f, 0.85f, 1f);
        // Position verticale (relative frame) du port 🎬 du nœud (sous NextNodeId, au-dessus des choix).
        private const float NodeCinePortTop = 50f;
        // Couleur unique des ports/fils son 🔊 + hauteur standard de la rangée 🔊.
        // Le port de sortie est au BAS des cartes (jamais sur les frames de nœuds).
        private static readonly Color SoundPortCol = new Color(1f, 0.6f, 0.1f);
        // Couleur du port/fil d'ARRÊT son 🔊 (bas de carte son → bas de cible).
        private static readonly Color SoundStopPortCol = new Color(0.95f, 0.3f, 0.3f);
        private const float SoundRowH = 20f;
        // Couleur unique des ports/fils ambiance 🌌.
        private static readonly Color AmbiencePortCol = new Color(0.18f, 0.88f, 0.72f);

        // --- API publique pour le mode éditeur cinématique (CinematicEditorDevWindow) ---
        public StorySceneData ActiveSceneData => _data;
        public string ActiveFilePath => _activeFilePath;
        // Déploie la scène EN COURS D'ÉDITION (y compris non sauvegardée) vers le
        // runtime 3D. Utilisé par le mode éditeur cinématique pour garantir que la
        // carte live correspond exactement à ce qu'on édite (WYSIWYG START/END).
        public List<SceneCinematicData> AllCinematics
        {
            get
            {
                if (_data == null) return new List<SceneCinematicData>();
                _data.Cinematics ??= new List<SceneCinematicData>();
                return _data.Cinematics;
            }
        }
        public SceneCinematicData FindCinematic(string cinematicId) => _data?.FindCinematic(cinematicId);

        // Vue : suivi du dernier cadrage pour recadrer quand la fenêtre est redimensionnée.
        // _fitZoom = zoom posé par le dernier cadrage programmatique (Cadrer/Mosaïque/focus).
        // Tant que l'utilisateur ne zoome pas AU-DESSUS (édition rapprochée), un redimensionnement
        // ré-applique l'intention de cadrage au lieu de laisser le contenu tronqué hors champ.
        private float _fitZoom = 1.0f;
        private bool _lastViewWasAll = true;

        // Infobulle lisible au survol (espace écran, hors matrice zoomée) : à petit zoom les
        // champs sont illisibles, le survol du header d'une carte montre son contenu en clair.
        private bool _hasHoverCard;
        private string _hoverTitle = "";
        private string _hoverBody = "";
        private Vector2 _hoverScreenPos;
        // Position écran stable pour les infobulles : les cartes sont dessinées dans un BeginGroup.
        private Vector2 _graphMouseScreenPos;

        private readonly List<string> _availableCharacterFiles = new();
        private readonly List<string> _availableMapFiles = new();
        private readonly List<string> _availableSceneFiles = new();

        // Drag & Drop pour la réorganisation des scènes dans la liste
        private int _draggedSceneIndex = -1;
        private int _dropTargetSceneIndex = -1;
        private bool _isDraggingSceneItem = false;
        private Vector2 _dragSceneMouseStart;
        private readonly List<Rect> _sceneRowRects = new();
        private bool _pauseOnDeploy = true;

        // Spot libre pour une NOUVELLE carte : ne touche JAMAIS aux cartes existantes.
        // L'ajout (+ Dialogue / + Événement / + Conséq.) place la carte sous le contenu
        // existant, alignée sur la colonne la plus à gauche, sans regrouper/re-griller
        // les positions manuelles de l'utilisateur. Seuls Auto-Disposition / Mosaïque
        // appellent LayoutCardsInsideNode (regroupement explicite voulu).

        // ------------------------------------------------------------------
        // Entrées / sorties de nœud (routage explicite : vide = fin du nœud).
        // - Carte sans lien entrant = point d'entrée (input vert, plus gros).
        // - Carte sans lien sortant = point de sortie (output principal rouge, plus gros).
        // - Plusieurs entrées dialogue = erreur utilisateur (carré vert, cadre rouge).
        // Comparaisons insensibles à la casse, sans allocation (éditeur 60fps).
        // ------------------------------------------------------------------

        private static readonly Color EntryGreen = new Color(0.2f, 1f, 0.4f);
        private static readonly Color ExitRed = new Color(1f, 0.25f, 0.25f);

        // Rangée 🎬 d'une carte : résumé des cinématiques liées + effacement global.
        // Hauteur fixe CineRowH : à comptabiliser dans chaque mesure de taille de carte.

        // Port de sortie 🎬 : drag vers une carte = lier.
        // - list != null : sortie multi-liens d'une carte normale vers des cartes 🎬.
        // - cineOwner != null : sortie "fin →" (unique) d'une carte 🎬 vers n'importe quelle carte.

        // ------------------------------------------------------------------
        // Cartes son 🔊 : rangée + port bas + carte + fils (miroir du système 🎬).
        // Le lien vit sur la carte son (SourceCardId) : les cartes sources gardent
        // leurs données intactes. Les frames de nœuds (rectangles groupes) sont
        // exclues : aucune rangée ni port son sur DrawNodeFrame/DrawNodeSection.
        // ------------------------------------------------------------------

        // Rangée 🔊 d'une carte source : résumé des sons branchés depuis son bas
        // + effacement global. Hauteur fixe SoundRowH : à comptabiliser dans
        // chaque mesure de taille de carte (comme CineRowH).

        // Port de sortie 🔊 : au BAS des cartes (jamais sur les frames de nœuds).
        // Drag vers une carte son = brancher (SourceCardId) ; drop dans le vide
        // = créer une carte son branchée ici.

        // Centre du port d'entrée d'une carte son (haut, reçoit le fil du bas).

        // Centre du port de sortie d'ARRÊT d'une carte son (bas, vers la cible).

        // Input : normal 14px couleur d'origine, entrée = carré vert 20px,
        // erreur multi-entrées = cadre rouge 24px autour du carré vert.

        // ------------------------------------------------------------------
        // Sections repliables des cartes/frames — TOUT est éditable dans le graphe.
        // État purement éditeur (HashSet en mémoire) : RIEN n'est sérialisé en JSON.
        // Règle d'or : chaque bloc détail a sa mesure (lignes × 20px) utilisée à la
        // FOIS par les calculs de taille et par le dessin, dans le même ordre.
        // ------------------------------------------------------------------
        private readonly HashSet<string> _expandedSections = new();
        // Bouton ▸/▾ qui bascule la section. Retourne l'état après clic.

        // --- Listes d'effets en GUI absolu (lignes de 20px) ---
        // En-tête (toujours si alwaysHeader, sinon seulement si ouvert) + lignes si ouvert.

        // --- Prérequis en GUI absolu : 1 (toggle+type) + 0/1 (kind) + 0/1 (acteur) ---

        // --- Ambiance en GUI absolu : 2 + 0/1 (musique) ---

        // --- Récompenses en GUI absolu : 2 + M (items suppl.) ; picker si supporté ---
        // picker: null = sans loupe ; sinon OpenItemPicker est appelé (challenge).

        // --- Test auto en GUI absolu : 1 + (11+M+S+F si actif) ---

        // DÉ-HARDCODÉ : l'édition du focus caméra des répliques est retirée
        // (remplacée par des cartes 🎬 à tracking). Champs conservés en JSON, ignorés au runtime.

        // --- Détail défi (carte) : récompenses + effets + combat : 5+M+S+F ---

        // Tailles de cartes UNIFIÉES : une seule source de vérité pour éviter
        // frame trop petite / clipping / wires désalignés (310 vs 340 vs 360 selon les méthodes).
        private const float DlgCardDefaultW = 360f;
        private const float EvtCardDefaultW = 360f;
        private const float EvtCardDefaultH = 155f;
        private const float ConsCardDefaultW = 360f;
        private const float ConsCardDefaultH = 250f;
        private const float TrigCardDefaultW = 340f;
        private const float TrigCardDefaultH = 215f;
        private const float InterCardDefaultW = 340f;
        private const float InterCardDefaultH = 380f;
        private const float ActorCardDefaultW = 340f;
        private const float ActorCardDefaultH = 268f;
        private const float CineCardDefaultW = 360f;
        private const float CineCardDefaultH = 190f;
        private const float SndCardDefaultW = 300f;
        private const float SndCardDefaultH = 340f;

        // Nombre de lignes de config d'un défi (sans les lignes-liens).

        // Toutes les lignes de détail sous la rangée principale d'un choix :
        // config défi + liens ✔/✕, méta (prérequis/effets), détails repliables
        // (prérequis / effets / défi : préfixés APRÈS, les offsets de fils restent justes).
        // Doit correspondre EXACTEMENT à l'ordre de dessin dans DrawDialogueCard.

        // Y (relatif au haut de la carte) de la rangée principale de chaque choix.
        // Doit rester identique entre GetDialogueCardSize, DrawDialogueCard et DrawConnectedGraphWires.

        // Lignes de résumé de la réplique (sous les choix) : prérequis / test auto / ambiance.

        // Hauteur carte conséquence : 30 (haut) + 22 (ID/titre) + 20 (toggles)
        // + dialogue éventuel (22 locuteur + 44 speech) + bloc fx (1 + N si ouvert)
        // + 22 (liens) + 22 (+XP/+CE) + 20 × (1 + N items) + 10 (marge).

        // Offset Y (relatif carte) de la rangée →Diag/→Cons. Source unique pour
        // le dessin (DrawConsequenceCard) et les fils (DrawConnectedGraphWires).

        // Carte cinématique 🎬 : 30 (haut) + 22 (ID/titre) + 20 (skip/vitesse) + 20 (bandes noires/masquer chat) + 20 × plans
        // + 20 (sortie "fin →") + 22 (boutons) + marge.

        // Origine du wire de sortie "fin →" d'une carte 🎬 : flanc droit, rangée dédiée.

        // Origine du wire de sortie 🔊 d'une carte source : BAS centré de la carte.
        // Balaye tous les types porteurs (sauf frames de nœuds) : les doublons d'id
        // inter-types suivent la même convention que le reste du graphe (id unique attendu).

        // Écriture sécurisée de la cible d'une rangée Parler à (index né du drag,
        // la liste a pu bouger entre-temps : suppression de rangée, undo...).

        // Centre du port de sortie 💬 de la rangée i (doit matcher l'ordre de
        // dessin de DrawActorCard : 6 rangées fixes de 20px après le header 30px,
        // puis l'en-tête 💬 20px, puis une rangée 20px par entrée).

        private Vector2 _lastCanvasSize = new Vector2(1100f, 600f);
        private int _focusedNodeIndex = 0;

        // ---------------------------------------------------------------------
        // Transformation du graphe sans GUI.matrix : le clipping reste toujours
        // dans l'espace écran du BeginGroup. Toutes les coordonnées du graphe
        // sont conservées en espace monde et converties seulement au rendu.
        // ---------------------------------------------------------------------

        private readonly Dictionary<string, Vector2> _cachedNodeInputSockets = new();

        // Moteur de recherche d'item du catalogue (récompenses des cartes).
        // État purement éditeur : rien n'est sérialisé dans le JSON de scène.
        private SceneDialogueChoiceData _itemPickerTarget = null;
        private SceneConsequenceData _itemPickerCons = null;
        private int _itemPickerEntry = -1; // -1 = récompense principale, >= 0 = AdditionalItems[i]
        private string _itemPickerSearch = "";
        private int _itemPickerCat = -1; // -1 = toutes catégories
        private Vector2 _itemPickerScroll;
        private bool _itemPickerFocusSearch = false;
        // Vrai quand le sélecteur catalogue est ouvert et que la souris est dessus :
        // le panneau est modal, tout le graphe derrière devient inerte (pas de drag,
        // pas de wire, pas de zoom, boutons/champs sans effet).
        private bool _pickerCapturesMouse = false;

        // Dropdown des locuteurs (champs Loc: des cartes) : alimenté par les acteurs
        // de la scène + les noms déjà utilisés. Pur état éditeur.
        private string _actorDropKey = null;
        private Rect _actorDropAnchorWorld = new Rect(-10000f, -10000f, 0f, 0f);
        private Rect _actorDropWorld = new Rect(-10000f, -10000f, 0f, 0f);
        private System.Action<string> _actorDropSetter = null;
        private string _actorDropCurrent = "";
        // Défilement + index clavier de la dropdown locuteurs (sinon le bas de
        // liste est inatteignable quand il y a beaucoup d'acteurs).
        private Vector2 _actorDropScroll = Vector2.zero;
        private int _actorDropIndex = -1;

        private static GUIStyle _hoverBodyStyle;

        // --- Ports de sortie des frames de nœud : source unique de vérité partagée
        // entre le dessin (DrawNodeFrame) et les wires (DrawGlobalSceneWires).
        // - Sortie principale (NextNodeId) : flanc droit, yMin+18.
        // - Sorties choix majeurs : flanc droit, une par choix, espacées de 36px.

        // --- Tags de bord "bords" : quand la cible d'un lien est hors-champ, un tag
        // cliquable "→ id" reste ancré au bord du viewport dans sa direction.
        // Clic = cadrer le nœud cible. Pendant un drag de wire, drop sur le tag = lier.
        // Pur état éditeur, calculé en coordonnées pures (aucun cache de sockets).

        private readonly List<OffscreenLinkTag> _offscreenTags = new();

        // Wires vers cinématiques 🎬 : une carte source (n'importe quel type) vers
        // l'entrée d'une carte cinématique. Couleur magenta, jamais bloquants.

        // ------------------------------------------------------------------
        // Quick-create : menu contextuel à la Unreal/Blender quand un lien est
        // lâché dans le vide. Filtré par type de sortie pour ne proposer que
        // des cartes compatibles + actions (rompre le lien / annuler).
        // ------------------------------------------------------------------

        private bool _quickCreateFocusSearch;

        // ------------------------------------------------------------------
        // Anti-vol de drag : pré-passe + prise différée (cartes chevauchantes).
        // Ordre de peinture (du fond vers le dessus) : frames de nœuds, puis par
        // nœud dialogues/événements/conséquences, déclencheurs, interactables,
        // acteurs, cinématiques. On balaye en sens INVERSE : le premier header ou
        // poignée contenant le point est l'élément légitime (le plus au-dessus).
        // Tout appel GUI est interdit ici (stabilité des IDs de contrôles).
        // ------------------------------------------------------------------

        // Prise différée : vrai quand le MouseDown a été réservé pour cet objet et que
        // la souris a dépassé le seuil (slop 6px écran). Appelé depuis les blocs de
        // prise inline : la prise utilise alors les IDs générés dans CETTE passe (pas
        // de décalage de séquence) avec l'offset du point d'appui d'origine (pas de saut).

        // ------------------------------------------------------------------
        // Section détails du nœud (ex-inspecteur) : dessinée SOUS les cartes,
        // incluse dans la bounding box. Repliée par défaut si le nœud a des cartes.
        // ------------------------------------------------------------------
        private readonly Dictionary<string, bool> _nodeSecState = new();

        // ------------------------------------------------------------------
        // Carte Interactable : objet physique persistant niveau scène (comme un
        // Trigger global, PAS un Nœud narratif). Source seule : aucun port
        // d'entrée, une sortie teal vers le Nœud cible (TriggerNodeId).
        // Réponse à la question nodes-vs-cartes : les Nœuds = étapes narratives
        // (frames avec dialogues/events/conséquences) ; les Interactables = sources
        // ponctuelles → cartes flottantes globales, même rangée que les Triggers.
        // ------------------------------------------------------------------

        // Nombre de références à un acteur dans la scène (locuteur, cible, déclencheur).
        // Match sur ActorId OU DisplayName, insensible à la casse (comme BuildActorCandidates).

        // ------------------------------------------------------------------
        // Carte Acteur : pion déployé niveau scène (comme un Interactable global,
        // PAS un Nœud narratif). Cible seule : aucun port de sortie, un port
        // d'entrée visuel à gauche (wire Nœud → Acteur quand SpawnOnNodeId est
        // renseigné). Couleur faction : bleu PJ, rouge PNJ.
        // La carte édite identité + placement + spawn ; les stats fines
        // (FOR/AGI/… de l'EmbeddedSheet) se règlent hors éditeur.
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // Fiche complète d'un acteur : résout la fiche effective EXACTEMENT comme
        // le runtime (JsonStorySceneController.SpawnSingleActor) puis l'ouvre dans
        // le Créateur de Personnages (F1) en lecture/édition complète.
        // Priorité : 1) fichier disque (CharacterSheetFileName) 2) fiche embarquée
        // 3) fiche par défaut générée (Nom + Modèle + Arme de la carte).
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // Carte Cinématique 🎬 : carte globale avec UNE entrée (liée depuis
        // n'importe quelle autre carte) et AUCUNE sortie. Lecture runtime
        // fire-and-forget : la carte suivante s'enclenche sans attendre la fin.
        // Les plans se règlent dans le détail (⤴ Éditer → mode éditeur cinématique
        // sur la carte de combat : capture START/END caméra + pions, save JSON).
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // Panneaux audio : banque (pré-écoute), audit (validation), monitor.
        // GUILayout écran (jamais les helpers Graph* monde) : dessinés au-dessus
        // du canvas, dans DrawNodesAndDialoguesTab.
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // Carte son 🔊 : feuille du graphe (entrée seule en haut, jamais de sortie).
        // Branchée depuis le BAS d'une carte source (fil orange) : démarre un son
        // avec délai et volume, au début OU à la fin de la carte liée.
        // ------------------------------------------------------------------

        private static Texture2D _pureWhiteTex;
        private static Texture2D PureWhiteTex
        {
            get
            {
                if (_pureWhiteTex == null)
                {
                    _pureWhiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _pureWhiteTex.SetPixel(0, 0, Color.white);
                    _pureWhiteTex.Apply();
                }
                return _pureWhiteTex;
            }
        }

        private bool _confirmDeleteScene = false;
    }
}
