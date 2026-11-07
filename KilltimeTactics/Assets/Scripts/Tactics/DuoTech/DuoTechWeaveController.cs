using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;
using Killtime.CameraSystem;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Tactics.CombatUI;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics.DuoTech
{
    /// <summary>
    /// Duo-Tech — tissage souris (T1 Mina + onde loop + T2 Lucas).
    ///
    /// Séquence verrouillée :
    /// 1. Clic-drag = Trait 1 (Mina). Vecteur complet gardé (List<Vector3>).
    /// 2. Relâcher = l'onde démarre en loop sur T1 à la vitesse réelle de Mina
    ///    (partner.MoveSpeed). Prévisualisation du passage.
    /// 3. Nouveau clic-drag = Trait 2 (Lucas), tracé APRÈS l'onde, en anticipation.
    /// 4. Relâcher T2 = résolution immédiate (forme + synchro + dégâts).
    ///
    /// Clic droit / Échap = annulation (PA déjà payés perdus, pas de refund).
    /// La session n'expire jamais d'elle-même (ni fin de tour, ni fin de round).
    /// </summary>
    public partial class DuoTechWeaveController : MonoBehaviour
    {
        private enum WeavePhase { Idle, DrawingT1, WaitingT2, DrawingT2, Executing }

        private const float SampleStep = 0.15f;
        private const float MinLength = 1.5f;
        private const float LucasBeamSpeed = 12f;

        // Tolérances monde (1 case ≈ 1.7 unité).
        private const float SillageStraightTol = 0.6f;
        private const float SillageCatchDist = 1.0f;
        private const float SillageOverlapTol = 0.8f;
        private const float SillageCrossGap = 1.2f;
        private const float LassoTargetDist = 2.5f;
        private const float FournaiseTargetDist = 2.2f;
        private const int FournaiseIsolationRadius = 2;

        // RD-085 (Livre V §21.5) : 1 case ≈ 1.7 unité monde.
        private const float CaseWorld = 1.7f;
        private const float TrempeCatchDist = 2.5f;
        private const float RailgunMinLengthWorld = 4f * 1.7f;
        private const float RailgunStraightTol = 0.6f;
        private const float RailgunWidthTol = 1.0f;
        private const float RailgunTargetDist = 2.5f;
        private const float CisailleCrossGap = 1.2f;
        private const float CisailleCatchDist = 1.5f;
        private const float ParallaxeTargetDist = 2.5f;
        private const float ParallaxeMinDot = -0.8f;
        private const float UppercutTargetDist = 2.5f;
        private const float MirageMaxLengthWorld = 3.5f * 1.7f;

        private WeavePhase _phase = WeavePhase.Idle;
        private DuoTechDef _def;
        private TacticalUnit _initiator;
        private TacticalUnit _mina;
        private TacticalUnit _lucas;
        private CombatDevArena _arena;

        private readonly List<Vector3> _t1 = new();
        private readonly List<Vector3> _t2 = new();

        private LineRenderer _lineT1_Front;
        private LineRenderer _lineT1_Occluded;
        private LineRenderer _lineT2_Front;
        private LineRenderer _lineT2_Occluded;
        private GameObject _waveMarker;
        private GameObject _waveMarkerOccluded;
        private GameObject _waveMarkerGreen;
        private GameObject _waveMarkerGreenOccluded;
        private LineRenderer _lineSyncTrace_Front;
        private LineRenderer _lineSyncTrace_Occluded;
        private readonly List<Vector3> _syncTracePts = new();
        private float _waveArc;
        private float _t1Length;

        // Cibles actuellement incluses dans la trajectoire du Duo-Tech
        private readonly List<TacticalUnit> _highlightedTargets = new();
        private readonly Dictionary<TacticalUnit, GameObject> _targetRings = new();

        private Camera _cam;
        private readonly Plane _ground = new(Vector3.up, Vector3.zero);

        public bool IsWeaving => _phase != WeavePhase.Idle;

        private static DuoTechWeaveController _active;

        /// <summary>
        /// Vrai pendant dessin ET exécution : la sélection de cases et le
        /// déplacement doivent rester désactivés tant que c'est vrai.
        /// </summary>
        public static bool AnyWeaving => _active != null && _active.IsWeaving;

        public void BeginSession(DuoTechDef def, TacticalUnit initiator, TacticalUnit partner, CombatDevArena arena)
        {
            if (IsWeaving)
            {
                arena?.Log("Duo-Tech : un tissage est déjà en cours — terminez-le ou annulez (clic droit).");
                return;
            }
            _def = def;
            _initiator = initiator;
            _arena = arena;
            _mina = DuoTechRegistry.IsMinaMember(initiator.Stats) ? initiator : partner;
            _lucas = DuoTechRegistry.IsLucasMember(initiator.Stats) ? initiator : partner;

            _t1.Clear();
            _t2.Clear();
            _waveArc = 0f;
            _phase = WeavePhase.DrawingT1;
            _active = this;

            _cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            BuildVisuals();
            _arena?.Log($"✏️ <b>{def.Name}</b> : tracez <b>T1 (Mina, { MinaName() })</b> en clic-drag sur le sol, puis relâchez.");
        }

        public void CancelSession()
        {
            if (!IsWeaving) return;
            _arena?.Log("Duo-Tech annulé (PA déjà payés perdus).");
            EndSession();
        }

        private string MinaName() => _mina != null && _mina.Stats != null ? _mina.Stats.Name : "Mina";

        private void Update()
        {
            if (!IsWeaving) return;
            // Exécution (dash + cinématique) : aucune entrée, annulation impossible.
            if (_phase == WeavePhase.Executing) return;

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelSession();
                return;
            }

            switch (_phase)
            {
                case WeavePhase.DrawingT1:
                    if (Input.GetMouseButtonDown(0))
                    {
                        _t1.Clear();
                        ClearLine(_lineT1_Front, _lineT1_Occluded);
                        ClearLine(_lineSyncTrace_Front, _lineSyncTrace_Occluded);
                        if (_waveMarker != null) _waveMarker.SetActive(false);
                        if (_waveMarkerOccluded != null) _waveMarkerOccluded.SetActive(false);
                        if (_waveMarkerGreen != null) _waveMarkerGreen.SetActive(false);
                        if (_waveMarkerGreenOccluded != null) _waveMarkerGreenOccluded.SetActive(false);
                        ClearAllTargetHighlights();
                    }
                    if (Input.GetMouseButton(0)) SampleInto(_t1, _lineT1_Front, _lineT1_Occluded);
                    if (Input.GetMouseButtonUp(0)) FinishT1();
                    break;
                case WeavePhase.WaitingT2:
                    AnimateWave();
                    if (Input.GetMouseButtonDown(0))
                    {
                        _t2.Clear();
                        ClearLine(_lineT2_Front, _lineT2_Occluded);
                        _phase = WeavePhase.DrawingT2;
                    }
                    break;
                case WeavePhase.DrawingT2:
                    AnimateWave();
                    if (Input.GetMouseButton(0)) SampleInto(_t2, _lineT2_Front, _lineT2_Occluded);
                    if (Input.GetMouseButtonUp(0)) Resolve();
                    break;
            }

            // Mise à jour en temps réel des cibles incluses dans la trajectoire
            UpdateTargetHighlights();
        }

        private void EndSession()
        {
            _phase = WeavePhase.Idle;
            if (_active == this) _active = null;
            _def = null;
            _t1.Clear();
            _t2.Clear();
            if (_lineT1_Front != null) Destroy(_lineT1_Front.gameObject);
            if (_lineT1_Occluded != null) Destroy(_lineT1_Occluded.gameObject);
            if (_lineT2_Front != null) Destroy(_lineT2_Front.gameObject);
            if (_lineT2_Occluded != null) Destroy(_lineT2_Occluded.gameObject);
            if (_lineSyncTrace_Front != null) Destroy(_lineSyncTrace_Front.gameObject);
            if (_lineSyncTrace_Occluded != null) Destroy(_lineSyncTrace_Occluded.gameObject);
            if (_waveMarker != null) Destroy(_waveMarker);
            if (_waveMarkerOccluded != null) Destroy(_waveMarkerOccluded);
            if (_waveMarkerGreen != null) Destroy(_waveMarkerGreen);
            if (_waveMarkerGreenOccluded != null) Destroy(_waveMarkerGreenOccluded);
            _lineT1_Front = null;
            _lineT1_Occluded = null;
            _lineT2_Front = null;
            _lineT2_Occluded = null;
            _lineSyncTrace_Front = null;
            _lineSyncTrace_Occluded = null;
            _waveMarker = null;
            _waveMarkerOccluded = null;
            _waveMarkerGreen = null;
            _waveMarkerGreenOccluded = null;
            ClearAllTargetHighlights();
        }

        private void OnDisable()
        {
            // Sécurité : pas de lignes ou anneaux fantômes si l'objet est désactivé mid-tissage.
            if (_lineT1_Front != null) Destroy(_lineT1_Front.gameObject);
            if (_lineT1_Occluded != null) Destroy(_lineT1_Occluded.gameObject);
            if (_lineT2_Front != null) Destroy(_lineT2_Front.gameObject);
            if (_lineT2_Occluded != null) Destroy(_lineT2_Occluded.gameObject);
            if (_lineSyncTrace_Front != null) Destroy(_lineSyncTrace_Front.gameObject);
            if (_lineSyncTrace_Occluded != null) Destroy(_lineSyncTrace_Occluded.gameObject);
            if (_waveMarker != null) Destroy(_waveMarker);
            if (_waveMarkerOccluded != null) Destroy(_waveMarkerOccluded);
            if (_waveMarkerGreen != null) Destroy(_waveMarkerGreen);
            if (_waveMarkerGreenOccluded != null) Destroy(_waveMarkerGreenOccluded);
            ClearAllTargetHighlights();
            if (_active == this) _active = null;
        }
    }
}
