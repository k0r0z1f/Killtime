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
    /// Permet la modulation instantanée de PA investis (Livre VI) sans confirmation intermédiaire,
    /// avec verrouillage strict anti-propagation interdisant tout clic-traversant vers le sol 3D.
    /// </summary>
    public class CombatContextMenuUI : MonoBehaviour
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
        private const float HubRadius = 68f;
        private const float DefenseHubRadius = 108f;
        private const float InnerRadius = 104f;
        private const float OuterRadius = 170f;
        private const float ActionNodeRadius = 20f;
        private const float CategoryNodeRadius = 14f;
        private const float DefenseSpotlightHalfSize = 78f;

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
        private ActionCategory? _hoveredCategory;
        private Vector2 _hoveredCategoryPos;
        private int _openFrame;

        // Barrière anti-propagation
        private bool _blockUntilMouseUp;
        private int _blockUntilFrame;

        // Allocation de PA bonus par action (Livre VI)
        private readonly Dictionary<CombatAction, int> _bonusAPMap = new();
        // Allocation de PE (Essoufflement) par action — déclaration aveugle (Livres II §7 + VI §24)
        private readonly Dictionary<CombatAction, int> _bonusPEMap = new();

        // État du mode Défense : le radial devient le menu de paramétrage de la
        // défense (remplace l'ancien modal séparé). La cible = le défenseur.
        private bool _isDefenseMode;
        private AttackDuel _defenseDuel;
        private TacticalUnit _defenseAttacker;
        private TacticalUnit _defenseDefender;
        private Action<SkillType, bool, int, int> _defenseCallback;
        private SkillType _defenseSkill = SkillType.Esquive;
        private SkillType _defenseParrySkill = SkillType.ManiementArmes;
        private string _defenseParryLabel = "Parade";
        private bool _defenseWants = true;
        private int _defenseBonusAP;
        private int _defenseBonusPE;
        private int _hoveredDefenseNode = -1;
        private Vector2 _defenseTargetGui;
        private bool _defenseHasAnchor;

        private static Texture2D _circleTex;
        private static Texture2D _ringTex;
        private static Texture2D _glowTex;
        private static Texture2D _pixelTex;
        private static readonly Dictionary<string, Texture2D> _glyphCache = new();

        private static readonly Color ColorArcaneCyan = new Color(0f, 0.92f, 1f, 1f);
        private static readonly Color ColorArcaneViolet = new Color(0.72f, 0.25f, 1f, 1f);
        private static readonly Color ColorArcaneEmerald = new Color(0.1f, 1f, 0.55f, 1f);
        private static readonly Color ColorArcaneAmber = new Color(1f, 0.75f, 0.1f, 1f);
        private static readonly Color ColorArcaneCrimson = new Color(1f, 0.25f, 0.35f, 1f);
        private static readonly Color ColorArcaneSlate = new Color(0.55f, 0.65f, 0.75f, 1f);
        private static readonly Color ColorHubBackground = new Color(0.02f, 0.04f, 0.07f, 0.94f);

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
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                ArmInputBlocker();
                CloseMenu();
                return;
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
                if (distFromCenter > OuterRadius + 45f)
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

            _isOpen = true;
            _hoveredAction = null;
            _hoveredCategory = null;
            _openFrame = Time.frameCount;
            _blockUntilMouseUp = false;
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
            _contextTarget = null;
            _hoveredAction = null;
            _hoveredCategory = null;
            _bonusAPMap.Clear();
            _bonusPEMap.Clear();
        }

        /// <summary>
        /// Ouvre le radial en mode Défense, centré sur le défenseur comme après un
        /// clic droit sur lui (Livres II §7 + VI §24.1, déclaration aveugle).
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
                _defenseSkill = weapon.AssociatedSkill;
                _defenseParrySkill = weapon.AssociatedSkill;
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
            _hoveredCategory = null;
            _openFrame = Time.frameCount;
            _blockUntilMouseUp = false;

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

        private void OnGUI()
        {
            Event e = Event.current;
            Vector2 mousePos = e.mousePosition;
            float distFromCenter = Vector2.Distance(mousePos, _centerPos);
            bool isInsideMenu = distFromCenter <= (OuterRadius + 45f);

            // Phase de blocage après fermeture : consomme les événements résiduels
            if (!_isOpen)
            {
                if (_blockUntilMouseUp && isInsideMenu)
                {
                    if (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ContextClick)
                    {
                        e.Use();
                    }
                }
                return;
            }

            if (_contextTarget == null) return;

            // Mode Défense : tout le paramétrage vit dans le radial, centré sur le
            // défenseur comme après un clic droit sur lui. Aucun modal séparé.
            if (_isDefenseMode)
            {
                if (_defenseDefender == null || _defenseDuel == null)
                {
                    CloseMenu();
                    return;
                }
                EnsureProceduralTextures();
                // Le menu suit la cible si la caméra bouge.
                _centerPos = DefenseScreenCenter(_defenseDefender);
                UpdateDefenseAnchor();
                DrawDefenseBackdrop();
                DrawDefenseHub();
                DrawDefenseRing(mousePos, e);
                if (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ContextClick || e.type == EventType.ScrollWheel)
                {
                    e.Use();
                }
                return;
            }

            EnsureProceduralTextures();
            var activeActor = GetActiveActor();
            if (activeActor == null) return;

            bool isCurrentTurnUnit = _turnManager != null
                && !_turnManager.IsInExploration
                && !_turnManager.IsCombatOver
                && _turnManager.ActiveUnit == _contextTarget
                && _contextTarget.IsPlayerControlled;

            DrawHoloBackdrop();
            DrawCenterHub(activeActor);
            DrawCategoryOrbit(mousePos, e, isCurrentTurnUnit);
            DrawActionOrbit(activeActor, mousePos, e, isCurrentTurnUnit);

            if (_hoveredCategory.HasValue)
            {
                DrawCategoryTooltip(_hoveredCategory.Value, _hoveredCategoryPos);
            }

            // Consommation systématique de tout clic résiduel dans l'anneau pour interdire la propagation
            if (isInsideMenu && (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ContextClick))
            {
                e.Use();
            }
        }

        private void DrawHoloBackdrop()
        {
            Color prevColor = GUI.color;

            GUI.color = new Color(0f, 0.7f, 1f, 0.12f);
            float glowSize = (OuterRadius + 40f) * 2f;
            GUI.DrawTexture(new Rect(_centerPos.x - glowSize * 0.5f, _centerPos.y - glowSize * 0.5f, glowSize, glowSize), _glowTex);

            GUI.color = new Color(0f, 0.85f, 1f, 0.30f);
            float ringOuterSize = OuterRadius * 2f;
            GUI.DrawTexture(new Rect(_centerPos.x - OuterRadius, _centerPos.y - OuterRadius, ringOuterSize, ringOuterSize), _ringTex);

            GUI.color = new Color(0f, 0.85f, 1f, 0.18f);
            float ringInnerSize = InnerRadius * 2f;
            GUI.DrawTexture(new Rect(_centerPos.x - InnerRadius, _centerPos.y - InnerRadius, ringInnerSize, ringInnerSize), _ringTex);

            GUI.color = prevColor;
        }

        private void DrawCenterHub(TacticalUnit activeActor)
        {
            Color prevColor = GUI.color;

            GUI.color = ColorHubBackground;
            GUI.DrawTexture(new Rect(_centerPos.x - HubRadius, _centerPos.y - HubRadius, HubRadius * 2f, HubRadius * 2f), _circleTex);

            int hoveredBonusAP = (_hoveredAction != null && _bonusAPMap.TryGetValue(_hoveredAction, out int b)) ? b : 0;
            bool hoveredCanAfford = false;
            if (_hoveredAction != null)
            {
                int totalCost = _hoveredAction.ActionPointCost + hoveredBonusAP;
                hoveredCanAfford = activeActor.Stats.CurrentActionPoints >= totalCost && _hoveredAction.CanExecute(activeActor, _contextTarget);
            }

            Color hubBorderCol = _hoveredAction != null
                ? (!hoveredCanAfford ? ColorArcaneCrimson : (hoveredBonusAP > 0 ? ColorArcaneAmber : ColorArcaneCyan))
                : ColorArcaneCyan;

            GUI.color = new Color(hubBorderCol.r, hubBorderCol.g, hubBorderCol.b, 0.85f);
            GUI.DrawTexture(new Rect(_centerPos.x - HubRadius, _centerPos.y - HubRadius, HubRadius * 2f, HubRadius * 2f), _ringTex);

            float usableSize = HubRadius * 1.52f;
            Rect hubRect = new Rect(_centerPos.x - usableSize * 0.5f, _centerPos.y - usableSize * 0.5f, usableSize, usableSize);

            GUILayout.BeginArea(hubRect);
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                clipping = TextClipping.Clip
            };
            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                wordWrap = true,
                richText = true
            };

            if (_hoveredAction != null)
            {
                int currentAP = activeActor.Stats.CurrentActionPoints;
                int totalCost = _hoveredAction.ActionPointCost + hoveredBonusAP;
                int remainingAP = currentAP - totalCost;
                bool hasEnoughAP = remainingAP >= 0;
                bool isExecutable = _hoveredAction.CanExecute(activeActor, _contextTarget);
                bool canAfford = hasEnoughAP && isExecutable;
                bool allowsVar = ActionAllowsVariableAP(_hoveredAction);

                titleStyle.normal.textColor = Color.white;
                GUILayout.Label(_hoveredAction.Title, titleStyle);

                int dist = activeActor.CurrentCoords.DistanceTo(_contextTarget.CurrentCoords);
                int maxRange = CombatActionRegistry.GetAttackMaxRange(activeActor);

                if (!isExecutable && dist > maxRange)
                {
                    GUI.color = ColorArcaneCrimson;
                    GUILayout.Label($"<b>HORS DE PORTÉE</b> ({dist} cases / max {maxRange})", bodyStyle);
                }
                else if (!hasEnoughAP)
                {
                    GUI.color = ColorArcaneCrimson;
                    GUILayout.Label($"<b>PA INSUFFISANTS</b> (Requis: {totalCost} | Dispo: {currentAP})", bodyStyle);
                }
                else if (hoveredBonusAP > 0)
                {
                    GUI.color = ColorArcaneAmber;
                    GUILayout.Label($"<b>⚡ {totalCost} PA</b> ({_hoveredAction.ActionPointCost} + {hoveredBonusAP} bonus) [{currentAP} ➔ {Mathf.Max(0, remainingAP)}]", bodyStyle);

                    GUI.color = new Color(1f, 0.88f, 0.4f, 0.95f);
                    GUILayout.Label($"<b>Duel aveugle :</b> mise cachée +{hoveredBonusAP} PA", bodyStyle);
                }
                else
                {
                    GUI.color = canAfford ? ColorArcaneEmerald : ColorArcaneCrimson;
                    string hint = allowsVar ? " (Molette: +PA)" : "";
                    GUILayout.Label($"<b>{_hoveredAction.ActionPointCost} PA</b>{hint} [{currentAP} ➔ {Mathf.Max(0, remainingAP)}]", bodyStyle);
                }

                // Déclaration aveugle : mise PE (Essoufflement) pour les attaques.
                // Cachée jusqu'à la révélation simultanée (Livres II §7 + VI §24).
                if (allowsVar && _hoveredAction.Category == ActionCategory.AttaqueEtPassesDarmes
                    && !_hoveredAction.Title.StartsWith("💣"))
                {
                    int hoveredPE = 0;
                    _bonusPEMap.TryGetValue(_hoveredAction, out hoveredPE);
                    int maxPE = activeActor.Stats.GetSpendablePE();
                    if (hoveredPE > maxPE) { hoveredPE = maxPE; _bonusPEMap[_hoveredAction] = hoveredPE; }

                    GUI.color = Color.white;
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUI.enabled = hoveredPE > 0;
                    if (GUILayout.Button("- PE", GUILayout.Width(44), GUILayout.Height(18)))
                    {
                        hoveredPE = Mathf.Max(0, hoveredPE - 1);
                        _bonusPEMap[_hoveredAction] = hoveredPE;
                    }
                    GUI.enabled = hoveredPE < maxPE;
                    if (GUILayout.Button("+ PE", GUILayout.Width(44), GUILayout.Height(18)))
                    {
                        hoveredPE = Mathf.Min(maxPE, hoveredPE + 1);
                        _bonusPEMap[_hoveredAction] = hoveredPE;
                    }
                    GUI.enabled = true;
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUI.color = hoveredPE > 0 ? ColorArcaneAmber : new Color(0.65f, 0.75f, 0.85f);
                    GUILayout.Label($"<b>PE : +{hoveredPE}</b> (ESS {activeActor.Stats.Essoufflement}/{activeActor.Stats.Attributes.Constitution})", bodyStyle);
                }

                GUI.color = new Color(0.85f, 0.92f, 1f, 0.85f);
                GUILayout.Label(_hoveredAction.Description, bodyStyle);
            }
            else
            {
                titleStyle.normal.textColor = _contextTarget.IsPlayerControlled ? ColorArcaneCyan : ColorArcaneCrimson;
                GUILayout.Label(_contextTarget.Stats.Name, titleStyle);

                GUI.color = ColorArcaneEmerald;
                GUILayout.Label($"❤️ {_contextTarget.Stats.CurrentHealth}/{_contextTarget.Stats.MaxHealth} PV", bodyStyle);

                GUI.color = ColorArcaneCyan;
                GUILayout.Label($"⚡ PA : {_contextTarget.Stats.CurrentActionPoints}  |  🛡️ {_contextTarget.Stats.EncaissementThreshold}", bodyStyle);

                if (_contextTarget.Stats.ActiveStatus != StatusEffect.None)
                {
                    GUI.color = ColorArcaneViolet;
                    GUILayout.Label($"[{_contextTarget.Stats.ActiveStatus}]", bodyStyle);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.EndArea();

            GUI.color = prevColor;
        }

        private void DrawCategoryOrbit(Vector2 mousePos, Event e, bool isCurrentTurnUnit)
        {
            var categories = GetAvailableCategories(isCurrentTurnUnit);
            int count = categories.Count;
            if (count == 0) return;

            _hoveredCategory = null;

            float step = count > 1 ? Mathf.Min(0.38f, (Mathf.PI * 0.65f) / (count - 1)) : 0f;
            float totalArc = step * (count - 1);
            float startAngle = -Mathf.PI * 0.5f - totalArc * 0.5f;

            for (int i = 0; i < count; i++)
            {
                ActionCategory cat = categories[i];
                float angle = count == 1 ? -Mathf.PI * 0.5f : startAngle + step * i;
                Vector2 nodePos = _centerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * InnerRadius;
                Rect nodeRect = new Rect(nodePos.x - CategoryNodeRadius, nodePos.y - CategoryNodeRadius, CategoryNodeRadius * 2f, CategoryNodeRadius * 2f);

                bool isHovered = nodeRect.Contains(mousePos);
                bool isSelected = (_selectedCategory == cat);
                Color catColor = GetCategoryColor(cat);

                if (isHovered)
                {
                    _hoveredCategory = cat;
                    _hoveredCategoryPos = nodePos;
                }

                Color prevCol = GUI.color;
                GUI.color = isSelected ? new Color(catColor.r, catColor.g, catColor.b, 0.90f) : new Color(0.04f, 0.08f, 0.12f, 0.90f);
                GUI.DrawTexture(nodeRect, _circleTex);

                GUI.color = isSelected ? Color.white : (isHovered ? catColor : new Color(catColor.r, catColor.g, catColor.b, 0.5f));
                GUI.DrawTexture(nodeRect, _ringTex);

                Texture2D icon = GetCategoryPlaceholderIcon(cat);
                GUI.color = isSelected ? Color.black : Color.white;
                Rect iconRect = new Rect(nodeRect.x + 3f, nodeRect.y + 3f, nodeRect.width - 6f, nodeRect.height - 6f);
                GUI.DrawTexture(iconRect, icon);

                GUI.color = prevCol;

                if (isHovered && e.type == EventType.MouseDown && e.button == 0)
                {
                    _selectedCategory = cat;
                    _hoveredAction = null;
                    ArmInputBlocker();
                    e.Use();
                }
            }
        }

        private void DrawCategoryTooltip(ActionCategory cat, Vector2 tabPos)
        {
            string label = GetCategoryDisplayName(cat);
            Color catColor = GetCategoryColor(cat);

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            Vector2 size = style.CalcSize(new GUIContent(label));
            size.x += 16f;
            size.y += 8f;

            float tipX = tabPos.x - size.x * 0.5f;
            float tipY = tabPos.y - CategoryNodeRadius - size.y - 6f;

            tipX = Mathf.Clamp(tipX, 6f, Screen.width - size.x - 6f);
            tipY = Mathf.Clamp(tipY, 6f, Screen.height - size.y - 6f);

            Rect tipRect = new Rect(tipX, tipY, size.x, size.y);

            Color prev = GUI.color;

            GUI.color = new Color(0.02f, 0.04f, 0.08f, 0.96f);
            GUI.DrawTexture(tipRect, _pixelTex);

            GUI.color = new Color(catColor.r, catColor.g, catColor.b, 0.85f);
            DrawHollowRect(tipRect, 1f);

            GUI.color = Color.white;
            GUI.Label(tipRect, label, style);

            GUI.color = prev;
        }

        private void DrawActionOrbit(TacticalUnit activeActor, Vector2 mousePos, Event e, bool isCurrentTurnUnit)
        {
            var actions = _cachedActions.FindAll(a => a.Category == _selectedCategory);
            // Fin de tour TOUJOURS visible sur le menu d'une unité alliée (comme
            // l'ancien panneau « Terminer le tour ») : active seulement quand c'est
            // réellement son tour en combat, grisée + explicative sinon.
            bool showEndTurn = _contextTarget != null && _contextTarget.IsPlayerControlled;
            bool canEndTurn = showEndTurn && isCurrentTurnUnit;

            int actionCount = actions.Count + (showEndTurn ? 1 : 0);
            if (actionCount == 0) return;

            float angleStep = (Mathf.PI * 2f) / actionCount;
            float initialAngle = -Mathf.PI * 0.5f;

            _hoveredAction = null;

            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                float angle = initialAngle + angleStep * i;
                Vector2 nodePos = _centerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * OuterRadius;
                Rect nodeRect = new Rect(nodePos.x - ActionNodeRadius, nodePos.y - ActionNodeRadius, ActionNodeRadius * 2f, ActionNodeRadius * 2f);

                bool isHovered = nodeRect.Contains(mousePos);
                if (isHovered) _hoveredAction = action;

                bool allowsVarAP = ActionAllowsVariableAP(action);
                int bonusAP = _bonusAPMap.TryGetValue(action, out int b) ? b : 0;
                int bonusPE = _bonusPEMap.TryGetValue(action, out int bpe) ? bpe : 0;
                int maxAffordableBonus = Mathf.Max(0, activeActor.Stats.CurrentActionPoints - action.ActionPointCost);
                if (bonusPE > activeActor.Stats.GetSpendablePE()) { bonusPE = activeActor.Stats.GetSpendablePE(); _bonusPEMap[action] = bonusPE; }

                // Modulation immédiate par molette ou clic droit
                if (isHovered && allowsVarAP)
                {
                    if (e.type == EventType.ScrollWheel)
                    {
                        if (e.delta.y < 0f && bonusAP < maxAffordableBonus)
                        {
                            bonusAP++;
                            _bonusAPMap[action] = bonusAP;
                        }
                        else if (e.delta.y > 0f && bonusAP > 0)
                        {
                            bonusAP--;
                            _bonusAPMap[action] = bonusAP;
                        }
                        e.Use();
                    }
                    else if (e.type == EventType.MouseDown && e.button == 1)
                    {
                        bonusAP = (bonusAP < maxAffordableBonus) ? bonusAP + 1 : 0;
                        _bonusAPMap[action] = bonusAP;
                        ArmInputBlocker();
                        e.Use();
                    }
                }

                int totalCost = action.ActionPointCost + bonusAP;
                bool canAfford = activeActor.Stats.CurrentActionPoints >= totalCost && action.CanExecute(activeActor, _contextTarget);
                bool hasStakes = bonusAP > 0 || bonusPE > 0;

                Color prevCol = GUI.color;

                // Halo de survol ou de surtension énergétique
                if (isHovered || hasStakes)
                {
                    Color haloCol = hasStakes ? ColorArcaneAmber : (canAfford ? ColorArcaneCyan : ColorArcaneCrimson);
                    GUI.color = new Color(haloCol.r, haloCol.g, haloCol.b, hasStakes ? 0.50f : 0.35f);
                    float haloSize = ActionNodeRadius * (hasStakes ? 3.1f : 2.8f);
                    GUI.DrawTexture(new Rect(nodePos.x - haloSize * 0.5f, nodePos.y - haloSize * 0.5f, haloSize, haloSize), _glowTex);
                }

                // Fond
                GUI.color = canAfford ? new Color(0.03f, 0.06f, 0.1f, 0.95f) : new Color(0.14f, 0.04f, 0.04f, 0.85f);
                GUI.DrawTexture(nodeRect, _circleTex);

                // Bordure : Cyan si disponible, Ambre si surtension, Blanc si survol, Rouge UNIQUEMENT si indisponible
                Color borderCol = !canAfford 
                    ? ColorArcaneCrimson 
                    : (hasStakes ? ColorArcaneAmber : (isHovered ? Color.white : ColorArcaneCyan));
                GUI.color = borderCol;
                GUI.DrawTexture(nodeRect, _ringTex);

                // Icône
                Texture2D glyph = GetActionPlaceholderIcon(action.Title, action.Category);
                GUI.color = canAfford ? (isHovered ? Color.white : new Color(0.9f, 0.95f, 1f, 0.9f)) : new Color(0.6f, 0.6f, 0.6f, 0.4f);
                Rect glyphRect = new Rect(nodeRect.x + 5f, nodeRect.y + 5f, nodeRect.width - 10f, nodeRect.height - 10f);
                GUI.DrawTexture(glyphRect, glyph);

                // Badge PA (+ PE déclarés en aveugle)
                if (totalCost > 0)
                {
                    float badgeWidth = hasStakes ? 30f : 15f;
                    Rect badgeRect = new Rect(nodePos.x + 4f, nodePos.y + 5f, badgeWidth, 12f);

                    GUI.color = canAfford ? (hasStakes ? ColorArcaneAmber : ColorArcaneCyan) : ColorArcaneCrimson;
                    GUI.DrawTexture(badgeRect, _circleTex);

                    GUI.color = Color.black;
                    var badgeStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 8,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter
                    };

                    string badgeLabel = bonusAP > 0 ? $"{totalCost}⚡" : totalCost.ToString();
                    if (bonusPE > 0) badgeLabel += $"+{bonusPE}";
                    GUI.Label(badgeRect, badgeLabel, badgeStyle);
                }

                GUI.color = prevCol;

                // Clic gauche : exécution immédiate avec blocage absolu du click-through
                if (isHovered && e.type == EventType.MouseDown && e.button == 0)
                {
                    ArmInputBlocker();
                    if (canAfford)
                    {
                        ExecuteActionWithInjectedAP(action, activeActor, _contextTarget, bonusAP, bonusPE);
                        CloseMenu();
                    }
                    e.Use();
                    return;
                }
            }

            if (showEndTurn)
            {
                float angle = initialAngle + angleStep * actions.Count;
                Vector2 nodePos = _centerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * OuterRadius;
                Rect nodeRect = new Rect(nodePos.x - ActionNodeRadius, nodePos.y - ActionNodeRadius, ActionNodeRadius * 2f, ActionNodeRadius * 2f);
                bool isHovered = nodeRect.Contains(mousePos);

                Color prevCol = GUI.color;
                GUI.color = !canEndTurn
                    ? new Color(0.12f, 0.12f, 0.12f, 0.85f)
                    : (isHovered ? ColorArcaneAmber : new Color(0.2f, 0.15f, 0.05f, 0.95f));
                GUI.DrawTexture(nodeRect, _circleTex);
                GUI.color = !canEndTurn
                    ? new Color(0.45f, 0.45f, 0.45f, 0.7f)
                    : (isHovered ? Color.white : ColorArcaneAmber);
                GUI.DrawTexture(nodeRect, _ringTex);

                Texture2D hourglassGlyph = GetGlyphTexture("GLYPH_ENDTURN");
                GUI.color = !canEndTurn
                    ? new Color(0.5f, 0.5f, 0.5f, 0.6f)
                    : (isHovered ? Color.white : ColorArcaneAmber);
                Rect glyphRect = new Rect(nodeRect.x + 5f, nodeRect.y + 5f, nodeRect.width - 10f, nodeRect.height - 10f);
                GUI.DrawTexture(glyphRect, hourglassGlyph);

                GUI.color = prevCol;

                if (isHovered && e.type == EventType.MouseDown && e.button == 0)
                {
                    ArmInputBlocker();
                    if (canEndTurn)
                    {
                        EndActiveUnitTurn();
                        CloseMenu();
                    }
                    else
                    {
                        _arena?.Log(EndTurnBlockedHint());
                    }
                    e.Use();
                }
            }
        }

        #region Mode Défense (paramétrage dans le radial, sans modal)

        private Vector2 DefenseScreenCenter(TacticalUnit defender)
        {
            var cam = Camera.main;
            if (cam == null || defender == null)
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector3 scr = cam.WorldToScreenPoint(defender.transform.position + Vector3.up * 0.9f);
            if (scr.z < 0f) return _centerPos;
            Vector2 gui = new Vector2(scr.x, Screen.height - scr.y);
            float m = OuterRadius + 50f;
            gui.x = Mathf.Clamp(gui.x, m, Mathf.Max(m, Screen.width - m));
            gui.y = Mathf.Clamp(gui.y, m, Mathf.Max(m, Screen.height - m));
            return gui;
        }

        private void UpdateDefenseAnchor()
        {
            _defenseHasAnchor = false;
            var cam = Camera.main;
            if (cam == null || _defenseDefender == null) return;
            Vector3 scr = cam.WorldToScreenPoint(_defenseDefender.transform.position + Vector3.up * 0.9f);
            if (scr.z < 0f) return;
            _defenseTargetGui = new Vector2(scr.x, Screen.height - scr.y);
            _defenseHasAnchor = true;
        }

        private int DefenseRequiredBase()
        {
            int required = Killtime.Core.Rules.CoreRulesConfig.Instance.BaseReactionAPCost;
            if (_defenseDefender.Stats.ActiveStatus.HasFlag(StatusEffect.Ralenti))
                required *= Killtime.Core.Rules.CoreRulesConfig.Instance.RalentiAPMultiplier;
            return required;
        }

        private bool DefenseCanUseActive()
        {
            if (_defenseDefender == null) return false;
            return _defenseDefender.Stats.CanDefendActively()
                && _defenseDefender.Stats.CurrentActionPoints >= DefenseRequiredBase();
        }

        private int DefenseMaxBonusAP()
        {
            if (_defenseDefender == null) return 0;
            return Mathf.Max(0, _defenseDefender.Stats.CurrentActionPoints - (_defenseWants ? DefenseRequiredBase() : 0));
        }

        private int DefenseMaxPE()
        {
            if (_defenseDefender == null) return 0;
            return Mathf.Max(0, _defenseDefender.Stats.GetSpendablePE());
        }

        private int DefensePEBonusPerPoint()
        {
            return Killtime.Core.Rules.CoreRulesConfig.Instance.DuelPEBonusPerPoint;
        }

        private bool DefenseCanParry()
        {
            if (_defenseDefender == null) return false;
            var weapon = _defenseDefender.Sheet?.GetEquippedWeapon();
            bool hasMeleeWeapon = weapon != null && weapon.RangeInTiles <= 1;
            return hasMeleeWeapon || _defenseDefender.Stats.HasSpecialization("Arts Martiaux");
        }

        private string DefenseSkillLabel()
        {
            if (!_defenseWants) return "Passif";
            if (_defenseSkill == SkillType.Esquive) return "Esquive";
            if (_defenseSkill == SkillType.DefenseCorporelle) return "Blocage";
            return _defenseParryLabel;
        }

        private int DefenseSelectedIndex()
        {
            if (!_defenseWants) return 3;
            if (_defenseSkill == SkillType.Esquive) return 0;
            if (_defenseSkill == SkillType.DefenseCorporelle) return 2;
            return 1;
        }

        private void ConfirmDefense()
        {
            if (!_isDefenseMode) return;
            bool canActive = DefenseCanUseActive();
            SkillType skill = _defenseSkill;
            bool wants = _defenseWants && canActive;
            int ap = wants ? Mathf.Min(_defenseBonusAP, DefenseMaxBonusAP()) : 0;
            int pe = wants ? Mathf.Min(_defenseBonusPE, DefenseMaxPE()) : 0;
            var cb = _defenseCallback;
            CloseMenu();
            ArmInputBlocker();
            cb?.Invoke(skill, wants, ap, pe);
        }

        /// <summary>Focus visuel : scène assombrie sauf autour du défenseur + anneau pulsant.</summary>
        private void DrawDefenseBackdrop()
        {
            if (!_defenseHasAnchor || _defenseDefender == null) return;
            Color prev = GUI.color;

            float hx = Mathf.Clamp(_defenseTargetGui.x - DefenseSpotlightHalfSize, 0f, Screen.width);
            float hy = Mathf.Clamp(_defenseTargetGui.y - DefenseSpotlightHalfSize, 0f, Screen.height);
            float hw = Mathf.Min(DefenseSpotlightHalfSize * 2f, Screen.width - hx);
            float hh = Mathf.Min(DefenseSpotlightHalfSize * 2f, Screen.height - hy);

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, hy), _pixelTex);
            GUI.DrawTexture(new Rect(0f, hy + hh, Screen.width, Mathf.Max(0f, Screen.height - hy - hh)), _pixelTex);
            GUI.DrawTexture(new Rect(0f, hy, hx, hh), _pixelTex);
            GUI.DrawTexture(new Rect(hx + hw, hy, Mathf.Max(0f, Screen.width - hx - hw), hh), _pixelTex);

            float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.08f;
            float glowSize = DefenseSpotlightHalfSize * 2.6f * pulse;
            GUI.color = new Color(1f, 0.75f, 0.1f, 0.22f);
            GUI.DrawTexture(new Rect(_defenseTargetGui.x - glowSize * 0.5f, _defenseTargetGui.y - glowSize * 0.5f, glowSize, glowSize), _glowTex);

            float ringSize = DefenseSpotlightHalfSize * 1.5f * pulse;
            GUI.color = new Color(1f, 0.75f, 0.1f, 0.95f);
            GUI.DrawTexture(new Rect(_defenseTargetGui.x - ringSize * 0.5f, _defenseTargetGui.y - ringSize * 0.5f, ringSize, ringSize), _ringTex);

            GUI.color = Color.white;
            var tagStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                fontStyle = FontStyle.Bold
            };
            tagStyle.normal.textColor = ColorArcaneAmber;
            Rect tagRect = new Rect(_defenseTargetGui.x - 110f, _defenseTargetGui.y + DefenseSpotlightHalfSize * 0.85f, 220f, 18f);
            GUI.Label(tagRect, $"◉ {_defenseDefender.Stats.Name} — DÉFENSEUR", tagStyle);

            GUI.color = prev;
        }

        /// <summary>Hub central : résumé de la mise aveugle + steppers PA/PE + confirmer.</summary>
        private void DrawDefenseHub()
        {
            Color prevColor = GUI.color;

            GUI.color = ColorHubBackground;
            GUI.DrawTexture(new Rect(_centerPos.x - DefenseHubRadius, _centerPos.y - DefenseHubRadius, DefenseHubRadius * 2f, DefenseHubRadius * 2f), _circleTex);

            bool canActive = DefenseCanUseActive();
            if (!canActive)
            {
                _defenseWants = false;
                _defenseBonusAP = 0;
                _defenseBonusPE = 0;
            }
            int maxBonus = DefenseMaxBonusAP();
            if (_defenseBonusAP > maxBonus) _defenseBonusAP = maxBonus;
            if (_defenseBonusAP < 0) _defenseBonusAP = 0;
            int maxPE = DefenseMaxPE();
            if (_defenseBonusPE > maxPE) _defenseBonusPE = maxPE;
            if (_defenseBonusPE < 0) _defenseBonusPE = 0;

            int requiredBase = DefenseRequiredBase();
            int availPA = _defenseDefender.Stats.CurrentActionPoints;
            int totalCost = _defenseWants ? requiredBase + _defenseBonusAP : 0;
            int remainPA = Mathf.Max(0, availPA - totalCost);
            int bonusTotal = _defenseWants ? _defenseBonusAP + _defenseBonusPE * DefensePEBonusPerPoint() : 0;

            GUI.color = _defenseWants
                ? new Color(ColorArcaneAmber.r, ColorArcaneAmber.g, ColorArcaneAmber.b, 0.9f)
                : new Color(ColorArcaneCyan.r, ColorArcaneCyan.g, ColorArcaneCyan.b, 0.9f);
            GUI.DrawTexture(new Rect(_centerPos.x - DefenseHubRadius, _centerPos.y - DefenseHubRadius, DefenseHubRadius * 2f, DefenseHubRadius * 2f), _ringTex);

            float usable = DefenseHubRadius * 1.5f;
            Rect hubRect = new Rect(_centerPos.x - usable * 0.5f, _centerPos.y - usable * 0.5f, usable, usable);

            GUILayout.BeginArea(hubRect);
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                clipping = TextClipping.Clip
            };
            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                wordWrap = true,
                richText = true,
                clipping = TextClipping.Clip
            };
            var rowStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 9,
                fontStyle = FontStyle.Bold
            };

            titleStyle.normal.textColor = Color.white;
            GUILayout.Label($"🛡️ {_defenseDefender.Stats.Name}", titleStyle);

            string partName = BodyPartInfo.GetInfo(_defenseDuel.TargetedPart).DisplayName;
            GUI.color = new Color(0.85f, 0.92f, 1f, 0.85f);
            GUILayout.Label($"{_defenseAttacker.Stats.Name} vise {partName}", bodyStyle);

            int shownIdx = _hoveredDefenseNode >= 0 ? _hoveredDefenseNode : DefenseSelectedIndex();
            string[] nodeNames = { "Esquive", _defenseParryLabel, "Blocage", "Passif", "Confirmer" };
            GUI.color = shownIdx == 4 ? ColorArcaneEmerald : ColorArcaneAmber;
            GUILayout.Label(shownIdx == 4 ? "✔ Révélation simultanée" : $"{nodeNames[shownIdx]} · +{bonusTotal}", bodyStyle);

            bool rowsEnabled = _defenseWants && canActive;
            GUI.color = Color.white;

            GUILayout.BeginHorizontal();
            rowStyle.normal.textColor = Color.white;
            GUILayout.Label($"PA +{_defenseBonusAP}", rowStyle, GUILayout.Width(52));
            GUI.enabled = rowsEnabled && _defenseBonusAP > 0;
            if (GUILayout.Button("−", GUILayout.Width(26), GUILayout.Height(18))) _defenseBonusAP = Mathf.Max(0, _defenseBonusAP - 1);
            GUI.enabled = rowsEnabled && _defenseBonusAP < maxBonus;
            if (GUILayout.Button("+", GUILayout.Width(26), GUILayout.Height(18))) _defenseBonusAP = Mathf.Min(maxBonus, _defenseBonusAP + 1);
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"PE +{_defenseBonusPE}", rowStyle, GUILayout.Width(52));
            GUI.enabled = rowsEnabled && _defenseBonusPE > 0;
            if (GUILayout.Button("−", GUILayout.Width(26), GUILayout.Height(18))) _defenseBonusPE = Mathf.Max(0, _defenseBonusPE - 1);
            GUI.enabled = rowsEnabled && _defenseBonusPE < maxPE;
            if (GUILayout.Button("+", GUILayout.Width(26), GUILayout.Height(18))) _defenseBonusPE = Mathf.Min(maxPE, _defenseBonusPE + 1);
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (!canActive)
            {
                GUI.color = ColorArcaneCrimson;
                GUILayout.Label("PA insuffisants → passif (0 PA)", bodyStyle);
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = remainPA >= 0 ? ColorArcaneCyan : ColorArcaneCrimson;
                GUILayout.Label($"Mise {totalCost} PA + {_defenseBonusPE} PE · [{availPA} ➔ {remainPA}]", bodyStyle);
                GUI.color = Color.white;
            }

            GUI.backgroundColor = ColorArcaneAmber;
            if (GUILayout.Button("✔ CONFIRMER", GUILayout.Height(24)))
            {
                ConfirmDefense();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.EndArea();

            GUI.color = prevColor;
        }

        /// <summary>Anneau : 4 skills défensifs + confirmer. Molette/clic droit = +PA.</summary>
        private void DrawDefenseRing(Vector2 mousePos, Event e)
        {
            bool canActive = DefenseCanUseActive();
            bool canParry = canActive && DefenseCanParry();
            string[] nodeNames = { "Esquive", _defenseParryLabel, "Blocage", "Passif", "Confirmer" };
            string[] glyphKeys = { "DEF_ESQUIVE", "DEF_PARADE", "DEF_BLOCAGE", "DEF_PASSIF", "DEF_CONFIRM" };
            float[] anglesDeg = { -90f, 0f, 90f, 180f, 45f };
            bool[] enabled = { canActive, canParry, canActive, true, true };
            int selectedIdx = DefenseSelectedIndex();

            _hoveredDefenseNode = -1;

            for (int i = 0; i < 5; i++)
            {
                float rad = anglesDeg[i] * Mathf.Deg2Rad;
                Vector2 nodePos = _centerPos + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * OuterRadius;
                Rect nodeRect = new Rect(nodePos.x - ActionNodeRadius, nodePos.y - ActionNodeRadius, ActionNodeRadius * 2f, ActionNodeRadius * 2f);

                bool isHovered = nodeRect.Contains(mousePos);
                if (isHovered) _hoveredDefenseNode = i;
                bool isSelected = (i == selectedIdx);
                bool isConfirm = (i == 4);

                int maxBonus = DefenseMaxBonusAP();
                if (isHovered && !isConfirm && enabled[i] && _defenseWants)
                {
                    if (e.type == EventType.ScrollWheel)
                    {
                        if (e.delta.y < 0f && _defenseBonusAP < maxBonus) _defenseBonusAP++;
                        else if (e.delta.y > 0f && _defenseBonusAP > 0) _defenseBonusAP--;
                        e.Use();
                    }
                    else if (e.type == EventType.MouseDown && e.button == 1)
                    {
                        _defenseBonusAP = (_defenseBonusAP < maxBonus) ? _defenseBonusAP + 1 : 0;
                        ArmInputBlocker();
                        e.Use();
                    }
                }

                bool hasStakes = isSelected && (_defenseBonusAP > 0 || _defenseBonusPE > 0);
                Color prevCol = GUI.color;

                if (isHovered || (isSelected && !isConfirm))
                {
                    Color haloCol = hasStakes ? ColorArcaneAmber : (isConfirm ? ColorArcaneEmerald : ColorArcaneCyan);
                    GUI.color = new Color(haloCol.r, haloCol.g, haloCol.b, hasStakes ? 0.5f : 0.35f);
                    float haloSize = ActionNodeRadius * (hasStakes ? 3.1f : 2.8f);
                    GUI.DrawTexture(new Rect(nodePos.x - haloSize * 0.5f, nodePos.y - haloSize * 0.5f, haloSize, haloSize), _glowTex);
                }

                GUI.color = enabled[i] ? new Color(0.03f, 0.06f, 0.1f, 0.95f) : new Color(0.14f, 0.04f, 0.04f, 0.85f);
                GUI.DrawTexture(nodeRect, _circleTex);

                Color borderCol = !enabled[i]
                    ? ColorArcaneCrimson
                    : (isConfirm ? ColorArcaneEmerald : (isSelected ? Color.white : (isHovered ? ColorArcaneAmber : ColorArcaneCyan)));
                GUI.color = borderCol;
                GUI.DrawTexture(nodeRect, _ringTex);

                Texture2D glyph = GetDefenseGlyph(glyphKeys[i]);
                GUI.color = enabled[i] ? (isHovered ? Color.white : new Color(0.9f, 0.95f, 1f, 0.9f)) : new Color(0.6f, 0.6f, 0.6f, 0.4f);
                Rect glyphRect = new Rect(nodeRect.x + 5f, nodeRect.y + 5f, nodeRect.width - 10f, nodeRect.height - 10f);
                GUI.DrawTexture(glyphRect, glyph);

                if (!isConfirm)
                {
                    Rect badgeRect = new Rect(nodePos.x + 4f, nodePos.y + 5f, 15f, 12f);
                    GUI.color = enabled[i] ? ColorArcaneCyan : ColorArcaneCrimson;
                    GUI.DrawTexture(badgeRect, _circleTex);
                    GUI.color = Color.black;
                    var badgeStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 8,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter
                    };
                    GUI.Label(badgeRect, i == 3 ? "0" : DefenseRequiredBase().ToString(), badgeStyle);
                }

                var tipStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                tipStyle.normal.textColor = enabled[i] ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                Rect tipRect = new Rect(nodePos.x - 50f, nodePos.y + ActionNodeRadius + 1f, 100f, 14f);
                GUI.Label(tipRect, nodeNames[i], tipStyle);

                GUI.color = prevCol;

                if (isHovered && e.type == EventType.MouseDown && e.button == 0)
                {
                    ArmInputBlocker();
                    if (isConfirm)
                    {
                        ConfirmDefense();
                    }
                    else if (!enabled[i])
                    {
                        if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                            Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Denied, 0.6f);
                    }
                    else if (i == 3)
                    {
                        _defenseWants = false;
                        _defenseBonusAP = 0;
                        _defenseBonusPE = 0;
                    }
                    else
                    {
                        _defenseWants = true;
                        _defenseSkill = i == 0 ? SkillType.Esquive : (i == 2 ? SkillType.DefenseCorporelle : _defenseParrySkill);
                    }
                    e.Use();
                    return;
                }
            }
        }

        private static Texture2D GetDefenseGlyph(string key)
        {
            if (_glyphCache.TryGetValue(key, out var cached)) return cached;
            int s = 32;
            var tex = CreateEmptyTexture(s);
            switch (key)
            {
                case "DEF_ESQUIVE":
                    DrawChevron(tex, 16, 12, 10, Color.white);
                    DrawChevron(tex, 16, 20, 10, Color.white);
                    break;
                case "DEF_PARADE":
                    DrawLine(tex, 8, 24, 24, 8, Color.white);
                    DrawLine(tex, 8, 8, 24, 24, Color.white);
                    break;
                case "DEF_BLOCAGE":
                    DrawHollowSquare(tex, 10, 10, 12, Color.white);
                    DrawDot(tex, 16, 16, 3, Color.white);
                    break;
                case "DEF_PASSIF":
                    DrawCircle(tex, 16, 16, 9, Color.white);
                    break;
                case "DEF_CONFIRM":
                    DrawLine(tex, 9, 17, 15, 23, Color.white);
                    DrawLine(tex, 15, 23, 24, 8, Color.white);
                    break;
            }
            tex.Apply();
            _glyphCache[key] = tex;
            return tex;
        }

        #endregion

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

        private static string GetCategoryDisplayName(ActionCategory cat)
        {
            return cat switch
            {
                ActionCategory.AttaqueEtPassesDarmes => "Attaque & Passes d'Armes",
                ActionCategory.CinquiemeForceEtSorts => "5e Force & Sorts",
                ActionCategory.TraumatologieEtSoins => "Traumatologie & Soins",
                ActionCategory.TactiqueEtOrdres => "Tactique & Ordres",
                ActionCategory.CommandesDev => "Commandes Développeur",
                _ => "Actions"
            };
        }

        private static Color GetCategoryColor(ActionCategory cat)
        {
            return cat switch
            {
                ActionCategory.AttaqueEtPassesDarmes => ColorArcaneCrimson,
                ActionCategory.CinquiemeForceEtSorts => ColorArcaneViolet,
                ActionCategory.TraumatologieEtSoins => ColorArcaneEmerald,
                ActionCategory.TactiqueEtOrdres => ColorArcaneAmber,
                ActionCategory.CommandesDev => ColorArcaneSlate,
                _ => ColorArcaneCyan
            };
        }

        private static void EnsureProceduralTextures()
        {
            if (_circleTex == null) _circleTex = GenerateCircleTexture(64, false);
            if (_ringTex == null) _ringTex = GenerateCircleTexture(64, true, 3.5f);
            if (_glowTex == null) _glowTex = GenerateGlowTexture(64);
            if (_pixelTex == null)
            {
                _pixelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _pixelTex.SetPixel(0, 0, Color.white);
                _pixelTex.Apply();
            }
        }

        private static Texture2D GenerateCircleTexture(int size, bool hollow, float strokeWidth = 3f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float maxR = center;
            float innerR = center - strokeWidth;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = 0f;

                    if (!hollow)
                    {
                        alpha = Mathf.Clamp01(maxR - dist + 0.5f);
                    }
                    else
                    {
                        float distOuter = maxR - dist + 0.5f;
                        float distInner = dist - innerR + 0.5f;
                        alpha = Mathf.Clamp01(Mathf.Min(distOuter, distInner));
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateGlowTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha = alpha * alpha;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D GetCategoryPlaceholderIcon(ActionCategory cat)
        {
            string key = "CAT_" + cat;
            if (_glyphCache.TryGetValue(key, out var cached)) return cached;

            int s = 32;
            var tex = CreateEmptyTexture(s);
            switch (cat)
            {
                case ActionCategory.AttaqueEtPassesDarmes:
                    DrawLine(tex, 16, 4, 16, 28, Color.white);
                    DrawLine(tex, 4, 16, 28, 16, Color.white);
                    DrawHollowSquare(tex, 10, 10, 12, Color.white);
                    break;
                case ActionCategory.CinquiemeForceEtSorts:
                    DrawDiamond(tex, 16, 16, 12, Color.white);
                    DrawDot(tex, 16, 16, 2, Color.white);
                    break;
                case ActionCategory.TraumatologieEtSoins:
                    DrawCross(tex, 16, 16, 10, 4, Color.white);
                    break;
                case ActionCategory.TactiqueEtOrdres:
                    DrawChevron(tex, 16, 10, 12, Color.white);
                    DrawChevron(tex, 16, 18, 12, Color.white);
                    break;
                case ActionCategory.CommandesDev:
                    DrawHollowSquare(tex, 8, 8, 16, Color.white);
                    DrawLine(tex, 8, 8, 24, 24, Color.white);
                    break;
            }
            tex.Apply();
            _glyphCache[key] = tex;
            return tex;
        }

        private static Texture2D GetActionPlaceholderIcon(string title, ActionCategory cat)
        {
            string lower = title.ToLowerInvariant();
            string key = "ACT_" + lower;
            if (_glyphCache.TryGetValue(key, out var cached)) return cached;

            int s = 32;
            var tex = CreateEmptyTexture(s);

            if (lower.Contains("tête") || lower.Contains("neutraliser"))
            {
                DrawCircle(tex, 16, 13, 8, Color.white);
                DrawHollowSquare(tex, 12, 20, 8, Color.white);
            }
            else if (lower.Contains("bras") || lower.Contains("désarmement"))
            {
                DrawLine(tex, 6, 26, 26, 6, Color.white);
                DrawLine(tex, 4, 22, 10, 28, Color.white);
            }
            else if (lower.Contains("jambes") || lower.Contains("faucher"))
            {
                DrawLine(tex, 16, 4, 10, 28, Color.white);
                DrawLine(tex, 16, 4, 22, 28, Color.white);
            }
            else if (lower.Contains("grenade"))
            {
                DrawCircle(tex, 16, 18, 8, Color.white);
                DrawLine(tex, 16, 10, 16, 5, Color.white);
                DrawDot(tex, 18, 5, 2, Color.white);
            }
            else if (lower.Contains("soin") || lower.Contains("suture"))
            {
                DrawCross(tex, 16, 16, 10, 4, Color.white);
            }
            else if (lower.Contains("défibrillation"))
            {
                DrawLine(tex, 6, 16, 12, 16, Color.white);
                DrawLine(tex, 12, 16, 15, 6, Color.white);
                DrawLine(tex, 15, 6, 18, 26, Color.white);
                DrawLine(tex, 18, 26, 21, 16, Color.white);
                DrawLine(tex, 21, 16, 26, 16, Color.white);
            }
            else if (lower.Contains("souffle"))
            {
                DrawChevron(tex, 10, 16, 8, Color.white);
                DrawChevron(tex, 18, 16, 8, Color.white);
            }
            else if (lower.Contains("ordre") || lower.Contains("couvrir"))
            {
                DrawChevron(tex, 16, 12, 10, Color.white);
                DrawChevron(tex, 16, 20, 10, Color.white);
            }
            else if (lower.Contains("intimidation"))
            {
                DrawDiamond(tex, 16, 16, 10, Color.white);
                DrawLine(tex, 10, 16, 22, 16, Color.white);
            }
            else if (lower.Contains("fiche"))
            {
                DrawHollowSquare(tex, 8, 6, 16, Color.white);
                DrawLine(tex, 12, 11, 20, 11, Color.white);
                DrawLine(tex, 12, 16, 20, 16, Color.white);
            }
            else if (lower.Contains("inventaire"))
            {
                DrawHollowSquare(tex, 7, 10, 18, Color.white);
                DrawHollowSquare(tex, 12, 5, 8, Color.white);
            }
            else
            {
                switch (cat)
                {
                    case ActionCategory.AttaqueEtPassesDarmes:
                        DrawCrosshair(tex, 16, 16, 10, Color.white);
                        break;
                    case ActionCategory.CinquiemeForceEtSorts:
                        DrawDiamond(tex, 16, 16, 11, Color.white);
                        DrawDot(tex, 16, 16, 3, Color.white);
                        break;
                    case ActionCategory.TraumatologieEtSoins:
                        DrawCross(tex, 16, 16, 9, 3, Color.white);
                        break;
                    default:
                        DrawDot(tex, 16, 16, 5, Color.white);
                        break;
                }
            }

            tex.Apply();
            _glyphCache[key] = tex;
            return tex;
        }

        private static Texture2D GetGlyphTexture(string key)
        {
            if (_glyphCache.TryGetValue(key, out var cached)) return cached;

            int s = 32;
            var tex = CreateEmptyTexture(s);
            if (key == "GLYPH_ENDTURN")
            {
                DrawLine(tex, 8, 6, 24, 6, Color.white);
                DrawLine(tex, 8, 26, 24, 26, Color.white);
                DrawLine(tex, 8, 6, 24, 26, Color.white);
                DrawLine(tex, 24, 6, 8, 26, Color.white);
            }
            tex.Apply();
            _glyphCache[key] = tex;
            return tex;
        }

        private static Texture2D CreateEmptyTexture(int s)
        {
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] clear = new Color32[s * s];
            for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(0, 0, 0, 0);
            tex.SetPixels32(clear);
            return tex;
        }

        private static void DrawHollowRect(Rect r, float stroke)
        {
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, stroke), _pixelTex);
            GUI.DrawTexture(new Rect(r.x, r.yMax - stroke, r.width, stroke), _pixelTex);
            GUI.DrawTexture(new Rect(r.x, r.y, stroke, r.height), _pixelTex);
            GUI.DrawTexture(new Rect(r.xMax - stroke, r.y, stroke, r.height), _pixelTex);
        }

        private static void DrawDot(Texture2D tex, int cx, int cy, int r, Color col)
        {
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r * r)
                        SetPixelSafe(tex, cx + x, cy + y, col);
        }

        private static void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color col)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                SetPixelSafe(tex, x0, y0, col);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private static void DrawHollowSquare(Texture2D tex, int x, int y, int size, Color col)
        {
            DrawLine(tex, x, y, x + size, y, col);
            DrawLine(tex, x, y + size, x + size, y + size, col);
            DrawLine(tex, x, y, x, y + size, col);
            DrawLine(tex, x + size, y, x + size, y + size, col);
        }

        private static void DrawCross(Texture2D tex, int cx, int cy, int length, int thickness, Color col)
        {
            int halfThick = thickness / 2;
            for (int x = cx - length; x <= cx + length; x++)
                for (int y = cy - halfThick; y <= cy + halfThick; y++)
                    SetPixelSafe(tex, x, y, col);

            for (int y = cy - length; y <= cy + length; y++)
                for (int x = cx - halfThick; x <= cx + halfThick; x++)
                    SetPixelSafe(tex, x, y, col);
        }

        private static void DrawCrosshair(Texture2D tex, int cx, int cy, int r, Color col)
        {
            DrawLine(tex, cx - r, cy, cx + r, cy, col);
            DrawLine(tex, cx, cy - r, cx, cy + r, col);
            DrawCircle(tex, cx, cy, r - 2, col);
        }

        private static void DrawDiamond(Texture2D tex, int cx, int cy, int r, Color col)
        {
            DrawLine(tex, cx - r, cy, cx + r, cy, col);
            DrawLine(tex, cx + r, cy, cx, cy + r, col);
            DrawLine(tex, cx, cy + r, cx - r, cy, col);
            DrawLine(tex, cx - r, cy, cx, cy - r, col);
        }

        private static void DrawChevron(Texture2D tex, int cx, int cy, int size, Color col)
        {
            DrawLine(tex, cx - size / 2, cy - size / 4, cx, cy + size / 4, col);
            DrawLine(tex, cx, cy + size / 4, cx + size / 2, cy - size / 4, col);
        }

        private static void DrawCircle(Texture2D tex, int cx, int cy, int r, Color col)
        {
            int x = r, y = 0;
            int radiusError = 1 - x;
            while (x >= y)
            {
                SetPixelSafe(tex, cx + x, cy + y, col);
                SetPixelSafe(tex, cx - x, cy + y, col);
                SetPixelSafe(tex, cx + x, cy - y, col);
                SetPixelSafe(tex, cx - x, cy - y, col);
                SetPixelSafe(tex, cx + y, cy + x, col);
                SetPixelSafe(tex, cx - y, cy + x, col);
                SetPixelSafe(tex, cx + y, cy - x, col);
                SetPixelSafe(tex, cx - y, cy - x, col);
                y++;
                if (radiusError < 0) radiusError += 2 * y + 1;
                else { x--; radiusError += 2 * (y - x + 1); }
            }
        }

        private static void SetPixelSafe(Texture2D tex, int x, int y, Color col)
        {
            if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
                tex.SetPixel(x, y, col);
        }
    }
}