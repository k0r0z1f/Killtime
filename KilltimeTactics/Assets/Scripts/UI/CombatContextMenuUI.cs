// Facade du menu contextuel radial (split en partiels : voir CombatContextMenuUI.*.cs).
// API publique inchangee : Instance, CurrentInjectedAP/PE, IsDefenseOpen, OpenMenu/OpenDefenseMenu/CloseMenu,
// EnsureInstance, GetActiveActor, ExecuteActionWithInjectedAP, EndActiveUnitTurn.
using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Interface diégétique circulaire holographique (Anneau Holo-Tactique Arcanotech).
    /// Permet la modulation instantanée de PA investis (Livre V) sans confirmation intermédiaire,
    /// avec verrouillage strict anti-propagation interdisant tout clic-traversant vers le sol 3D.
    /// </summary>
    public partial class CombatContextMenuUI : MonoBehaviour
    {
        public static CombatContextMenuUI Instance { get; private set; }

        /// <summary>
        /// PA additionnels investis pour l'action en cours d'exécution.
        /// </summary>
        public static int CurrentInjectedAP { get; private set; }

        /// <summary>
        /// PE (Essoufflement) additionnels déclarés pour l'action en cours (duel aveugle).
        /// </summary>
        public static int CurrentInjectedPE { get; private set; }

        private const int WindowId = 777;
        private const float HubRadius = 90f;
        private const float DefenseHubRadius = 108f;
        private const float InnerRadius = 124f;
        private const float OuterRadius = 186f;
        private const float ActionNodeRadius = 20f;
        private const float CategoryNodeRadius = 14f;
        private const float DefenseSpotlightHalfSize = 78f;

        // Style diégétique RD-155 (rendu seul) : anneau runique + pips PA.
        private const int RuneTickCount = 24;
        private const int MaxPAPips = 12;

        // Orbite inclinée RD-156 : ellipse monde, choix à l'avant, rotation aux bords.
        private const float OrbitTilt = 0.42f;
        private const float OrbitRadiusX = 170f;

        // Navigation orbitale RD-164 (mutable : sliders dev en jeu via
        // OrbitTuningWindow, figés en constantes après calibration).
        internal static class OrbitTuning
        {
            public static float EdgeInnerFactor = 0.65f; // seuil entrée bande (× rx)
            public static float ReleaseFactor = 0.55f;   // seuil sortie (hystérésis)
            public static float EdgeOuterExtra = 60f;    // sortie externe (rx + px)
            public static float MaxSpeed = 85f;          // °/s au bord externe
            public static float RampMs = 180f;           // attaque/relâche vitesse
            public static float SnapMs = 250f;           // snap ease-out (ms)
            public static float MinHit = 30f;            // hitbox mini nœuds (px)
            public static bool DwellPause = true;        // pause au survol nœud
        }

        // Ardoise vide RD-158 : seul l'anneau des éléments est dessiné en attaque.
        // Le hub central + halos sont masqués mais le code est conservé tel quel
        // (hub/PA/PE réutilisés plus tard) — passer à true pour les revoir.
        private static readonly bool ShowCenterHub = false;

        /// <summary>
        /// Vrai tant que le radial est ouvert en mode paramétrage de défense
        /// (déclaration aveugle du défenseur, sans modal séparé).
        /// </summary>
        public static bool IsDefenseOpen => Instance != null && Instance._isOpen && Instance._isDefenseMode;

        [Header("Systèmes")]
        [SerializeField] private TacticalSelectionManager _selectionManager;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CombatDevArena _arena;

        private bool _isOpen;
        private Vector2 _centerPos;
        private TacticalUnit _contextTarget;
        private List<CombatAction> _cachedActions = new();
        private ActionCategory _selectedCategory = ActionCategory.AttaqueEtPassesDarmes;
        private CombatAction _hoveredAction;
        // Navigation orbitale RD-157 : vrai = l'anneau montre les catégories,
        // faux = il montre les items de _selectedCategory (+ sablier).
        private bool _showOrbitCategories = true;
        private int _openFrame;

        // Barrière anti-propagation
        private bool _blockUntilMouseUp;
        private int _blockUntilFrame;

        // Allocation de PA bonus par action (Livre V)
        private readonly Dictionary<CombatAction, int> _bonusAPMap = new();
        // Allocation de PE (Essoufflement) par action — déclaration aveugle (Livres II §7 + VI §20)
        private readonly Dictionary<CombatAction, int> _bonusPEMap = new();

        // État du mode Défense : le radial devient le menu de paramétrage de la
        // défense (remplace l'ancien modal séparé). La cible = le défenseur.
        private bool _isDefenseMode;
        private AttackDuel _defenseDuel;
        private TacticalUnit _defenseAttacker;
        private TacticalUnit _defenseDefender;
        private Action<SkillType, bool, int, int> _defenseCallback;
        private List<TacticalUnit> _eligibleProtectors = new();
        private TacticalUnit _interceptedBy;
        public TacticalUnit InterceptedBy => _interceptedBy;
        private SkillType _defenseSkill = SkillType.Esquive;
        private SkillType _defenseParrySkill = SkillType.ManiementArmes;
        private string _defenseParryLabel = "Parade";
        private bool _defenseWants = true;
        private int _defenseBonusAP;
        private int _defenseBonusPE;
        private int _hoveredDefenseNode = -1;
        private Vector2 _defenseTargetGui;
        private bool _defenseHasAnchor;

        // Ancrage diégétique du radial d'attaque (RD-155) : projection écran de
        // la cible (anneau au sol + thereline vers le hub), miroir de l'ancre défense.
        private Vector2 _attackTargetGui;
        private Vector2 _attackGroundGui;
        private bool _attackHasAnchor;
        // Centre courant de l'orbite (mis à jour chaque frame) : les zones de
        // clic/fermeture le prennent en compte en plus du hub.
        private Vector2 _orbitCenterGui;

        // Moteur d'orbite inclinée RD-156/164 : angle courant, snap ease-out
        // vers la cible, rampe de vélocité, hystérésis de bande, dwell au
        // survol (attaque + défense ont chacun leur état).
        private float _orbitAngle;
        private float _orbitTargetAngle;
        private bool _orbitHasTarget;
        private float _orbitSnapFrom;
        private float _orbitSnapT;
        private float _orbitVel;
        private bool _orbitWasInBand;
        private bool _orbitHoverHold;
        private bool _frontCanAfford;
        private bool _frontIsEndTurn;
        private bool _frontCanEndTurn;
        private float _defOrbitAngle;
        private float _defOrbitTargetAngle;
        private bool _defOrbitHasTarget;
        private float _defSnapFrom;
        private float _defSnapT;
        private float _defVel;
        private bool _defWasInBand;
        private bool _defHoverHold;

        // Snap ease-out RD-164 : mémorise le départ pour interpoler.
        private void SnapAttackTo(int index, int count)
        {
            _orbitTargetAngle = OrbitSnapTarget(_orbitAngle, count, index);
            _orbitSnapFrom = _orbitAngle;
            _orbitSnapT = 0f;
            _orbitHasTarget = true;
        }

        private void SnapDefenseTo(int index, int count)
        {
            _defOrbitTargetAngle = OrbitSnapTarget(_defOrbitAngle, count, index);
            _defSnapFrom = _defOrbitAngle;
            _defSnapT = 0f;
            _defOrbitHasTarget = true;
        }

        private static Texture2D _circleTex;
        private static Texture2D _ringTex;
        private static Texture2D _glowTex;
        private static Texture2D _pixelTex;
        private static Texture2D _runeTickTex;
        private static readonly Dictionary<string, Texture2D> _glyphCache = new();

        private static readonly Color ColorArcaneCyan = new Color(0f, 0.92f, 1f, 1f);
        private static readonly Color ColorArcaneViolet = new Color(0.72f, 0.25f, 1f, 1f);
        private static readonly Color ColorArcaneEmerald = new Color(0.1f, 1f, 0.55f, 1f);
        private static readonly Color ColorArcaneAmber = new Color(1f, 0.75f, 0.1f, 1f);
        private static readonly Color ColorArcaneCrimson = new Color(1f, 0.25f, 0.35f, 1f);
        private static readonly Color ColorArcaneSlate = new Color(0.55f, 0.65f, 0.75f, 1f);
        private static readonly Color ColorHubBackground = new Color(0.02f, 0.03f, 0.06f, 0.97f);

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            if (_selectionManager == null) _selectionManager = FindAnyObjectByType<TacticalSelectionManager>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();

            if (_selectionManager != null)
            {
                _selectionManager.OnOpenContextMenuRequested += OpenMenu;
                _selectionManager.OnCloseContextMenuRequested += HandleExternalCloseRequest;
            }

            EnsureProceduralTextures();
            FloatingWindowChrome.RegisterWindow(WindowId, GetBoundingRect, IsBlockingInput);
            FloatingWindowChrome.RegisterExtraBlocker(IsMouseOverMenuArea);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                FloatingWindowChrome.UnregisterWindow(WindowId);
                FloatingWindowChrome.UnregisterExtraBlocker(IsMouseOverMenuArea);
                Instance = null;
            }

            if (_selectionManager != null)
            {
                _selectionManager.OnOpenContextMenuRequested -= OpenMenu;
                _selectionManager.OnCloseContextMenuRequested -= HandleExternalCloseRequest;
            }
        }

        /// <summary>
        /// Fermeture demandée de l'extérieur (changement de sélection, etc.) :
        /// ignorée en mode Défense, qui ne se clôt que par confirmation explicite.
        /// </summary>
        private void HandleExternalCloseRequest()
        {
            if (_isDefenseMode) return;
            CloseMenu();
        }

        private void Update()
        {
            if (!_isOpen) return;

            // Mode Défense : choix obligatoire (comme l'ancien modal) — Échap refusé,
            // Entrée/Espace = confirmer. Le passif (0 PA) reste toujours disponible.
            if (_isDefenseMode)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                        Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Denied, 0.6f);
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
                {
                    ConfirmDefense();
                    return;
                }
                // Orbite inclinée RD-156 : ←/→ fait tourner l'anneau de défense.
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.Q)) DefOrbitStepKeyboard(-1);
                else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) DefOrbitStepKeyboard(1);
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                ArmInputBlocker();
                CloseMenu();
                return;
            }
            else
            {
                // Orbite inclinée RD-156 : ←/→ tourne l'anneau, Entrée exécute l'avant.
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.A)) OrbitStepKeyboard(-1);
                else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) OrbitStepKeyboard(1);
                // Navigation RD-157 : Backspace remonte aux catégories.
                if (Input.GetKeyDown(KeyCode.Backspace)) BackToCategories();
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    ExecuteFrontAction();
                }
            }

            if (Time.frameCount > _openFrame && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
            {
                // Mode Défense : choix obligatoire jusqu'à confirmation — naviguer
                // dans la carte (clic, drag caméra) ne doit JAMAIS le fermer, sinon
                // l'attaque en attente reste bloquée pour toujours. Seul
                // ConfirmDefense (Entrée/Espace/clic de validation) le clôt.
                if (_isDefenseMode) return;
                Vector2 mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                float distFromCenter = Vector2.Distance(mouseGui, _centerPos);
                float distFromOrbit = Vector2.Distance(mouseGui, _orbitCenterGui);
                if (distFromCenter > OuterRadius + 45f && distFromOrbit > OrbitRadiusX + 60f)
                {
                    ArmInputBlocker();
                    CloseMenu();
                }
            }
        }

        public void OpenMenu(TacticalUnit target, Vector2 screenPos)
        {
            if (target == null) return;

            _contextTarget = target;

            // Synchronisation immédiate de la cible d'arène et du réticule 3D
            if (_arena != null && !target.IsPlayerControlled)
            {
                _arena.SelectTarget(target);
            }

            var activeUnit = GetActiveActor();

            _cachedActions = CombatActionRegistry.GetAvailableActions(activeUnit, target, _arena);
            _bonusAPMap.Clear();
            _bonusPEMap.Clear();

            bool isCurrentTurnUnit = _turnManager != null
                && !_turnManager.IsInExploration
                && !_turnManager.IsCombatOver
                && _turnManager.ActiveUnit == _contextTarget
                && _contextTarget.IsPlayerControlled;

            var availableCats = GetAvailableCategories(isCurrentTurnUnit);

            if (isCurrentTurnUnit && availableCats.Contains(ActionCategory.TactiqueEtOrdres))
            {
                _selectedCategory = ActionCategory.TactiqueEtOrdres;
            }
            else if (!target.IsPlayerControlled && availableCats.Contains(ActionCategory.AttaqueEtPassesDarmes))
            {
                _selectedCategory = ActionCategory.AttaqueEtPassesDarmes;
            }
            else if (!availableCats.Contains(_selectedCategory))
            {
                _selectedCategory = availableCats.Count > 0 ? availableCats[0] : ActionCategory.AttaqueEtPassesDarmes;
            }

            float safeMargin = OuterRadius + 50f;
            float clampedX = Mathf.Clamp(screenPos.x, safeMargin, Screen.width - safeMargin);
            float clampedY = Mathf.Clamp(screenPos.y, safeMargin, Screen.height - safeMargin);
            _centerPos = new Vector2(clampedX, clampedY);
            _orbitCenterGui = _centerPos;

            _isOpen = true;
            _hoveredAction = null;
            _showOrbitCategories = true;
            _openFrame = Time.frameCount;
            _blockUntilMouseUp = false;
            _orbitAngle = 0f;
            _orbitHasTarget = false;
            _orbitSnapT = 0f;
            _orbitVel = 0f;
            _orbitWasInBand = false;
            _orbitHoverHold = false;
            _frontCanAfford = false;
            _frontIsEndTurn = false;
            _frontCanEndTurn = false;
        }

        public void CloseMenu()
        {
            _isOpen = false;
            _isDefenseMode = false;
            _defenseDuel = null;
            _defenseAttacker = null;
            _defenseDefender = null;
            _defenseCallback = null;
            _hoveredDefenseNode = -1;
            _defenseHasAnchor = false;
            _attackHasAnchor = false;
            _orbitHasTarget = false;
            _orbitVel = 0f;
            _orbitWasInBand = false;
            _orbitHoverHold = false;
            _defOrbitHasTarget = false;
            _defVel = 0f;
            _defWasInBand = false;
            _defHoverHold = false;
            _frontCanAfford = false;
            _frontIsEndTurn = false;
            _frontCanEndTurn = false;
            _eligibleProtectors.Clear();
            _interceptedBy = null;
            _contextTarget = null;
            _hoveredAction = null;
            _showOrbitCategories = true;
            _bonusAPMap.Clear();
            _bonusPEMap.Clear();
        }

        /// <summary>
        /// Ouvre le radial en mode Défense, centré sur le défenseur comme après un
        /// clic droit sur lui (Livres II §7 + VI §20.1, déclaration aveugle).
        /// Remplace l'ancien modal séparé : tout le paramétrage vit dans le menu
        /// contextuel, avec focus verrouillé sur la cible.
        /// </summary>
        public void OpenDefenseMenu(
            AttackDuel duel,
            TacticalUnit attacker,
            TacticalUnit defender,
            Action<SkillType, bool, int, int> onDecision)
        {
            if (duel == null || attacker == null || defender == null) return;

            _defenseDuel = duel;
            _defenseAttacker = attacker;
            _defenseDefender = defender;
            _defenseCallback = onDecision;
            _contextTarget = defender;
            _eligibleProtectors = CombatTechniqueRegistry.GetEligibleProtectors(defender);
            _interceptedBy = null;

            // Focus logique sur la cible : réticule d'arène + surlignage de sélection.
            if (_arena != null) _arena.SelectTarget(defender);
            if (_selectionManager != null) _selectionManager.SelectSingleUnit(defender);

            // Compétence défensive par défaut + parade liée à l'arme équipée.
            _defenseSkill = defender.Stats.Attributes.Agilite >= defender.Stats.Attributes.Force
                ? SkillType.Esquive
                : SkillType.DefenseCorporelle;
            _defenseParrySkill = SkillType.ManiementArmes;
            _defenseParryLabel = "Parade";
            var weapon = defender.Sheet?.GetEquippedWeapon();
            if (weapon != null && weapon.RangeInTiles <= 1 && weapon.AssociatedSkill != SkillType.Ballistique)
            {
                // Valeur legacy ArmesContondantes rabattue sur Maniement d'Arme.
                _defenseSkill = SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill);
                _defenseParrySkill = SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill);
                _defenseParryLabel = $"Parade ({weapon.Name})";
            }

            _defenseWants = DefenseCanUseActive();
            _defenseBonusAP = 0;
            _defenseBonusPE = 0;
            _hoveredDefenseNode = -1;
            _bonusAPMap.Clear();
            _bonusPEMap.Clear();

            // Centre du radial = position écran du défenseur, comme un clic droit sur lui.
            _centerPos = DefenseScreenCenter(defender);
            UpdateDefenseAnchor();

            _isDefenseMode = true;
            _isOpen = true;
            _hoveredAction = null;
            _openFrame = Time.frameCount;
            _blockUntilMouseUp = false;
            _defOrbitAngle = 0f;
            _defOrbitHasTarget = false;
            _defSnapT = 0f;
            _defVel = 0f;
            _defWasInBand = false;
            _defHoverHold = false;

            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.Trauma_Shock, 0.75f);
        }

        /// <summary>Garantit une instance utilisable pour le mode Défense.</summary>
        public static CombatContextMenuUI EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[UI] CombatContextMenu");
            return go.AddComponent<CombatContextMenuUI>();
        }

        public TacticalUnit GetActiveActor()
        {
            // En combat tactique, l'attaquant fait foi : c'est l'unité active du tour
            // quand elle est contrôlée par le joueur. SANS condition de distance :
            // l'ancienne garde "au contact uniquement" faisait basculer la cible de
            // derrière (distance 2+) vers "l'unité joueur la plus proche", souvent une
            // autre unité à 0 PA — d'où "PA insuffisants" alors que l'attaquant est plein.
            // L'affichage ET l'exécution (ExecuteAttack) doivent partager ce même acteur.
            if (_turnManager != null && !_turnManager.IsInExploration && !_turnManager.IsCombatOver
                && _turnManager.ActiveUnit != null && _turnManager.ActiveUnit.IsPlayerControlled
                && _turnManager.ActiveUnit.Stats != null && _turnManager.ActiveUnit.Stats.IsAlive)
            {
                return _turnManager.ActiveUnit;
            }

            // 1. Unité sélectionnée manuellement par le joueur (exploration / hors tour)
            if (_selectionManager != null && _selectionManager.PrimarySelected != null && _selectionManager.PrimarySelected.IsPlayerControlled && _selectionManager.PrimarySelected.Stats.IsAlive)
            {
                return _selectionManager.PrimarySelected;
            }

            // 2. Unité active du tour si contrôlée par le joueur (repli exploration)
            if (_turnManager != null && _turnManager.ActiveUnit != null && _turnManager.ActiveUnit.IsPlayerControlled && _turnManager.ActiveUnit.Stats.IsAlive)
            {
                return _turnManager.ActiveUnit;
            }

            // 3. Identification de l'unité joueur la plus proche de la cible (priorité au contact direct <= 1 case)
            TacticalUnit closestPlayer = null;
            int minDistance = int.MaxValue;

            if (_arena != null)
            {
                if (_arena.PlayerUnit != null && _arena.PlayerUnit.Stats != null && _arena.PlayerUnit.Stats.IsAlive)
                {
                    int d = _contextTarget != null ? _arena.PlayerUnit.CurrentCoords.DistanceTo(_contextTarget.CurrentCoords) : 0;
                    closestPlayer = _arena.PlayerUnit;
                    minDistance = d;
                }

                if (_arena.AdditionalPlayers != null)
                {
                    for (int i = 0; i < _arena.AdditionalPlayers.Count; i++)
                    {
                        var ally = _arena.AdditionalPlayers[i];
                        if (ally != null && ally.Stats != null && ally.Stats.IsAlive && ally.IsPlayerControlled)
                        {
                            int d = _contextTarget != null ? ally.CurrentCoords.DistanceTo(_contextTarget.CurrentCoords) : 0;
                            if (d < minDistance)
                            {
                                minDistance = d;
                                closestPlayer = ally;
                            }
                        }
                    }
                }
            }

            if (closestPlayer != null) return closestPlayer;
            if (_turnManager != null && _turnManager.ActiveUnit != null) return _turnManager.ActiveUnit;

            return _contextTarget;
        }

        private void ArmInputBlocker()
        {
            _blockUntilMouseUp = true;
            _blockUntilFrame = Time.frameCount + 2;
        }

        private bool IsBlockingInput()
        {
            if (_isOpen) return true;
            if (_blockUntilMouseUp)
            {
                if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && Time.frameCount > _blockUntilFrame)
                {
                    _blockUntilMouseUp = false;
                    return false;
                }
                return true;
            }
            return false;
        }

        private bool IsMouseOverMenuArea(Vector2 mouseGui)
        {
            // Tant que le menu contextuel est affiché (ou en cours de fermeture sur ce frame),
            // l'ensemble de l'environnement 3D est neutralisé : un clic extérieur ne fait que congédier le menu.
            return IsBlockingInput();
        }

        private Rect GetBoundingRect()
        {
            float size = (OuterRadius + 45f) * 2f;
            return new Rect(_centerPos.x - size * 0.5f, _centerPos.y - size * 0.5f, size, size);
        }

        public static bool ActionAllowsVariableAP(CombatAction action)
        {
            if (action == null) return false;
            if (action.Category == ActionCategory.AttaqueEtPassesDarmes) return true;
            if (action.Title.IndexOf("parade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                action.Title.IndexOf("esquive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                action.Title.IndexOf("défense", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return false;
        }

        private List<ActionCategory> GetAvailableCategories(bool isCurrentTurnUnit)
        {
            var list = new List<ActionCategory>();
            ActionCategory[] all =
            {
                ActionCategory.AttaqueEtPassesDarmes,
                ActionCategory.CinquiemeForceEtSorts,
                ActionCategory.TraumatologieEtSoins,
                ActionCategory.TactiqueEtOrdres,
                ActionCategory.TechniquesDeSpecialisation,
                ActionCategory.CommandesDev
            };

            for (int i = 0; i < all.Length; i++)
            {
                ActionCategory cat = all[i];
                bool hasActions = _cachedActions.Exists(a => a.Category == cat);
                if (cat == ActionCategory.TactiqueEtOrdres && isCurrentTurnUnit)
                {
                    hasActions = true;
                }

                if (hasActions)
                {
                    list.Add(cat);
                }
            }

            return list;
        }


        private void ExecuteActionWithInjectedAP(CombatAction action, TacticalUnit actor, TacticalUnit target, int bonusAP, int bonusPE = 0)
        {
            CurrentInjectedAP = bonusAP;
            CurrentInjectedPE = bonusPE;
            try
            {
                action.Execution?.Invoke(actor, target);
            }
            finally
            {
                CurrentInjectedAP = 0;
                CurrentInjectedPE = 0;
            }
        }

        private void EndActiveUnitTurn()
        {
            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
            {
                Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.Turn_End, 0.7f);
            }

            if (TurnManager.IsMultiplayerPlayerClient())
            {
                Killtime.Multi.VTTTableSync.Instance?.RequestEndTurn();
            }
            else
            {
                _turnManager?.EndCurrentTurn();
            }
        }

        /// <summary>
        /// Explique pourquoi la fin de tour (grisée) n'est pas utilisable en l'état.
        /// Non, il n'est PAS nécessaire d'avoir attaqué d'abord : il faut que ce soit
        /// le tour de l'unité en mode combat.
        /// </summary>
        private string EndTurnBlockedHint()
        {
            string unitName = _contextTarget != null ? _contextTarget.Stats.Name : "L'unité";
            if (_turnManager == null)
                return $"⏳ <b>{unitName}</b> : pas de gestionnaire de tour actif.";
            if (_turnManager.IsCombatOver)
                return $"⏳ <b>{unitName}</b> : combat terminé, aucun tour à finir.";
            if (_turnManager.IsInExploration)
                return $"⏳ <b>{unitName}</b> : exploration libre, aucun tour en cours — attaquez pour passer en combat !";
            var active = _turnManager.ActiveUnit;
            if (active == null || active != _contextTarget)
                return $"⏳ <b>{unitName}</b> : ce n'est pas son tour (tour de <b>{(active != null ? active.Stats.Name : "?")}</b>).";
            return $"⏳ <b>{unitName}</b> : fin de tour indisponible pour le moment.";
        }
    }
}