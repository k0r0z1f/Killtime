using UnityEngine;
using UnityEngine.EventSystems;
using Killtime.Tactics.Units;
using Killtime.Tactics.CombatUI;
using Killtime.UI;

namespace Killtime.CameraSystem
{
    /// <summary>
    /// Contrôleur de caméra tactique isométrique.
    /// Garantit que le point focal est toujours ancré AU SOL (Y = 0)
    /// et permet de zoomer jusqu'à ras des personnages (0.5m).
    /// </summary>
    public class TacticalCameraController : MonoBehaviour
    {
        [Header("Cible & Suivi")]
        [SerializeField] private Transform _target;
        [Tooltip("Hauteur au-dessus du sol : 0.6m cadre les personnages de plein pied")]
        [SerializeField] private Vector3 _pivotOffset = new Vector3(0, 0.6f, 0);

        [Header("Zoom Molette (Jusqu'au sol)")]
        [Tooltip("Distance minimale : 0.5m permet de descendre jusqu'au ras du sol")]
        [SerializeField] private float _minDistance = 0.5f;
        [Tooltip("Distance maximale : 35m vue d'ensemble")]
        [SerializeField] private float _maxDistance = 35.0f;
        [SerializeField] private float _currentDistance = 12.0f;
        [SerializeField] private float _zoomSensitivity = 1.8f;
        [SerializeField] private float _zoomSmoothTime = 0.05f;

        [Header("Angle Fixe & Stable")]
        [Tooltip("Angle de plongée constant (45° = vue isométrique pure orientée vers le sol)")]
        [SerializeField] private float _pitchAngle = 45.0f;
        [SerializeField] private float _currentYaw = 45.0f;

        [Header("Déplacement Clavier")]
        [SerializeField] private float _panSpeed = 16.0f;
        [SerializeField] private float _keyboardRotateSpeed = 90.0f;
        [SerializeField] private float _mouseOrbitSpeed = 2.5f;
        [SerializeField] private float _panSmoothTime = 0.08f;

        public bool IsLockedToTarget { get; set; } = true;

        private Vector3 _panPosition = Vector3.zero;
        private Vector3 _panVelocity;
        private float _targetDistance;
        private float _distanceVelocity;
        private float _nextCamPrefSave;

        private void OnValidate()
        {
            // Corrige automatiquement les valeurs de l'Inspecteur Unity
            if (_minDistance > 0.8f) _minDistance = 0.5f;
            if (_pitchAngle < 30f || _pitchAngle > 60f) _pitchAngle = 45.0f;
        }

        private void Start()
        {
            try { LoadCameraPrefs(); } catch { /* prefs optionnelles */ }
            _targetDistance = Mathf.Clamp(_currentDistance, _minDistance, _maxDistance);
            _currentDistance = _targetDistance;

            // 1. Si aucune cible n'est renseignée, trouver automatiquement l'unité du joueur
            if (_target == null)
            {
                AutoFindPlayerTarget();
            }

            // 2. Initialiser le point au SOL (Y = 0)
            if (_target != null)
            {
                _panPosition = new Vector3(_target.position.x, 0f, _target.position.z);
            }
            else
            {
                // Projeter la ligne de vue de la caméra sur le plan du sol (Y = 0)
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                Ray ray = new Ray(transform.position, transform.forward);
                if (groundPlane.Raycast(ray, out float enter))
                {
                    _panPosition = ray.GetPoint(enter);
                }
                else
                {
                    _panPosition = Vector3.zero;
                }
            }

            _panPosition.y = 0f; // Sécurité absolue : le point focal est TOUJOURS au niveau du sol

            // Ajuster le plan de découpe proche pour ne pas tronquer les modèles à 50cm
            var cam = GetComponent<UnityEngine.Camera>() ?? UnityEngine.Camera.main;
            if (cam != null)
            {
                cam.nearClipPlane = 0.03f;
            }
        }

        private void AutoFindPlayerTarget()
        {
            var units = FindObjectsByType<TacticalUnit>();
            foreach (var u in units)
            {
                if (u.IsPlayerControlled)
                {
                    FocusOn(u.transform);
                    return;
                }
            }

            if (units.Length > 0)
            {
                FocusOn(units[0].transform);
            }
        }

        private void Update()
        {
            if (Time.timeScale <= 0.0001f) return;

            HandleKeyboardMovement();
            HandleRotation();
            HandleZoom();
            ApplyCameraTransform();
            try { MaybeSaveCameraPrefs(); } catch { /* ignore */ }
        }

        private void LoadCameraPrefs()
        {
            var p = Killtime.UI.DevUIPreferences.Current;
            if (p == null) return;
            _currentDistance = Mathf.Clamp(p.CamDistance, _minDistance, _maxDistance);
            _currentYaw = p.CamYaw;
            _pitchAngle = Mathf.Clamp(p.CamPitch, 30f, 60f);
        }

        private void MaybeSaveCameraPrefs()
        {
            var p = Killtime.UI.DevUIPreferences.Current;
            if (p == null) return;
            bool changed =
                Mathf.Abs(p.CamDistance - _targetDistance) > 0.05f ||
                Mathf.Abs(p.CamYaw - _currentYaw) > 0.5f ||
                Mathf.Abs(p.CamPitch - _pitchAngle) > 0.1f;
            if (!changed) return;
            if (Time.realtimeSinceStartup < _nextCamPrefSave) return;
            p.CamDistance = _targetDistance;
            p.CamYaw = _currentYaw;
            p.CamPitch = _pitchAngle;
            Killtime.UI.DevUIPreferences.MarkDirty(1.5f);
            _nextCamPrefSave = Time.realtimeSinceStartup + 2f;
        }

        private void OnDisable()
        {
            try
            {
                var p = Killtime.UI.DevUIPreferences.Current;
                if (p != null)
                {
                    p.CamDistance = _targetDistance;
                    p.CamYaw = _currentYaw;
                    p.CamPitch = _pitchAngle;
                    Killtime.UI.DevUIPreferences.MarkDirty(0.2f);
                }
            }
            catch { /* ignore */ }
        }

        private void HandleKeyboardMovement()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            if (Mathf.Abs(h) > 0.1f || Mathf.Abs(v) > 0.1f)
            {
                IsLockedToTarget = false;

                Vector3 forward = Quaternion.Euler(0, _currentYaw, 0) * Vector3.forward;
                Vector3 right = Quaternion.Euler(0, _currentYaw, 0) * Vector3.right;
                Vector3 moveDir = (forward * v + right * h).normalized;

                _panPosition += moveDir * (_panSpeed * Time.deltaTime);
                _panPosition.y = 0f; // Maintient toujours le pan sur le plan du sol
            }
            else if (IsLockedToTarget && _target != null)
            {
                _panPosition = new Vector3(_target.position.x, 0f, _target.position.z);
            }
        }

        private void HandleRotation()
        {
            // Rotation touches Q / E
            if (Input.GetKey(KeyCode.Q)) _currentYaw -= _keyboardRotateSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) _currentYaw += _keyboardRotateSpeed * Time.deltaTime;

            // Rotation libre au maintien du clic droit — bloquée au survol UI
            // (sinon, orbiter depuis une fenêtre flottante fait tourner la carte derrière).
            if (Input.GetMouseButton(1))
            {
                if (IsPointerOverUI()) return;
                _currentYaw += Input.GetAxis("Mouse X") * _mouseOrbitSpeed;
            }
        }

        private void HandleZoom()
        {
            float scroll = 0f;

            if (!IsPointerOverUI())
            {
                scroll = Input.GetAxis("Mouse ScrollWheel");
            }

            // Raccourcis clavier (R = zoom vers le sol / F = dézoom vers le ciel)
            if (Input.GetKey(KeyCode.R)) scroll += 0.06f;
            if (Input.GetKey(KeyCode.F)) scroll -= 0.06f;

            if (Mathf.Abs(scroll) > 0.0001f)
            {
                // Zoom proportionnel : descend très vite quand on est haut, ralentit doucement au contact du sol
                float step = scroll * _zoomSensitivity * (_targetDistance * 0.35f + 0.8f);
                _targetDistance = Mathf.Clamp(_targetDistance - step, _minDistance, _maxDistance);
            }

            _currentDistance = Mathf.SmoothDamp(_currentDistance, _targetDistance, ref _distanceVelocity, _zoomSmoothTime);
        }

        private bool IsPointerOverUI()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return true;
            }

            // Registre central : les 6 fenêtres flottantes + panels HUD interactifs + resize en cours.
            // Remplace les anciens checks partiels (Chat/Menu/Toolbar) qui laissaient passer
            // le zoom/clic à travers CharacterDevWindow, MapEditor, CoreRules et VTTRoom.
            if (FloatingWindowChrome.IsPointerOverAnyWindow())
            {
                return true;
            }

            return false;
        }

        private void ApplyCameraTransform()
        {
            Quaternion rotation = Quaternion.Euler(_pitchAngle, _currentYaw, 0f);

            // Point visé : au sol + décalage pivot (hauteur de torse 0.6m)
            Vector3 groundPivot = new Vector3(_panPosition.x, 0f, _panPosition.z);
            Vector3 focusPoint = groundPivot + _pivotOffset;

            // Position calculée en reculant le long du rayon de visée
            Vector3 desiredPosition = focusPoint - (rotation * Vector3.forward * _currentDistance);

            // Sécurité : la caméra s'arrête juste au-dessus des dalles (15 cm minimum)
            if (desiredPosition.y < 0.15f)
            {
                desiredPosition.y = 0.15f;
            }

            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _panVelocity, _panSmoothTime);
            transform.rotation = rotation;
        }

        public void FocusOn(Transform newTarget)
        {
            _target = newTarget;
            IsLockedToTarget = true;
            if (newTarget != null)
            {
                _panPosition = new Vector3(newTarget.position.x, 0f, newTarget.position.z);
            }
        }

        /// <summary>
        /// Adopte une pose monde exacte (fin de cinématique) pour une reprise sans
        /// couture : PAS de retour en arrière, la caméra RESTE où le plan l'a laissée.
        /// Le pivot d'orbite est recalculé sous le point visé (intersection du rayon
        /// de vue avec le plan du pivot) pour que pan/zoom repartent sainement.
        /// followTarget != null : suivi verrouillé (ex : acteur tracké) ; sinon pan libre
        /// (sinon l'ancien verrou ramenait la caméra en arrière = "refocus" fantôme).
        /// </summary>
        public void AdoptWorldPose(Vector3 camPos, Vector3 euler, Transform followTarget = null)
        {
            float pitch = Mathf.Clamp(euler.x, 30f, 60f);
            float yaw = euler.y;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 dir = rot * Vector3.forward;

            float d = _targetDistance;
            if (dir.y < -0.05f)
            {
                float hit = (camPos.y - _pivotOffset.y) / (-dir.y);
                if (hit > 0.5f) d = hit;
            }
            d = Mathf.Clamp(d, _minDistance, _maxDistance);

            Vector3 focus = camPos + dir * d;
            _panPosition = new Vector3(focus.x, 0f, focus.z);
            _currentYaw = yaw;
            _pitchAngle = pitch;
            _targetDistance = d;
            _currentDistance = d;

            _panVelocity = Vector3.zero;
            _distanceVelocity = 0f;

            if (followTarget != null)
            {
                FocusOn(followTarget);
            }
            else
            {
                _target = null;
                IsLockedToTarget = false;
            }

            transform.position = camPos;
            transform.rotation = rot;
        }

        public void SetPitchAndDistance(float pitch, float distance)
        {
            _pitchAngle = Mathf.Clamp(pitch, 30f, 60f);
            _targetDistance = Mathf.Clamp(distance, _minDistance, _maxDistance);
            _currentDistance = _targetDistance;
        }
    }
}