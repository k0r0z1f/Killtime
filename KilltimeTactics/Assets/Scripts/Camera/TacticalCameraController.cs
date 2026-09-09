using UnityEngine;

namespace Killtime.CameraSystem
{
    /// <summary>
    /// Contrôleur de caméra isométrique tactique (Tradition Fallout / Wasteland).
    /// Permet le déplacement au sol (pan), la rotation orbitale par pas de 45° ou libre,
    /// et le zoom progressif tout en maintenant l'angle de plongée tactique.
    /// </summary>
    public class TacticalCameraController : MonoBehaviour
    {
        [Header("Cible & Suivi")]
        [SerializeField] private Transform _target;
        [SerializeField] private Vector3 _offset = new Vector3(0, 0, 0);

        [Header("Paramètres Isométriques")]
        [SerializeField] private float _pitchAngle = 50.0f; // Angle de plongée
        [SerializeField] private float _currentYaw = 45.0f;  // Angle isométrique standard
        [SerializeField] private float _moveSpeed = 15.0f;
        [SerializeField] private float _rotationSpeed = 90.0f;

        [Header("Zoom")]
        [SerializeField] private float _zoomDistance = 18.0f;
        [SerializeField] private float _minZoom = 6.0f;
        [SerializeField] private float _maxZoom = 30.0f;
        [SerializeField] private float _zoomSpeed = 5.0f;

        [Header("Lissage")]
        [SerializeField] private float _smoothTime = 0.15f;

        private Vector3 _currentVelocity;
        private Vector3 _panPosition;

        public bool IsLockedToTarget { get; set; } = true;

        private void Start()
        {
            if (_target != null)
            {
                _panPosition = _target.position;
            }
            else
            {
                _panPosition = transform.position;
            }
        }

        private void Update()
        {
            HandleInput();
            ApplyCameraTransform();
        }

        private void HandleInput()
        {
            // 1. Déplacement au clavier (WASD / Flèches)
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            if (Mathf.Abs(h) > 0.1f || Mathf.Abs(v) > 0.1f)
            {
                IsLockedToTarget = false;

                // Calcul du mouvement relatif à l'orientation actuelle de la caméra
                Vector3 forward = Quaternion.Euler(0, _currentYaw, 0) * Vector3.forward;
                Vector3 right = Quaternion.Euler(0, _currentYaw, 0) * Vector3.right;
                Vector3 moveDir = (forward * v + right * h).normalized;

                _panPosition += moveDir * (_moveSpeed * Time.deltaTime);
            }
            else if (IsLockedToTarget && _target != null)
            {
                _panPosition = _target.position;
            }

            // 2. Rotation de la caméra (touches Q/E)
            if (Input.GetKey(KeyCode.Q))
            {
                _currentYaw -= _rotationSpeed * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.E))
            {
                _currentYaw += _rotationSpeed * Time.deltaTime;
            }

            // 3. Zoom molette
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _zoomDistance = Mathf.Clamp(_zoomDistance - scroll * _zoomSpeed * 10f, _minZoom, _maxZoom);
            }
        }

        private void ApplyCameraTransform()
        {
            Quaternion rotation = Quaternion.Euler(_pitchAngle, _currentYaw, 0);
            Vector3 targetPosition = _panPosition + _offset - (rotation * Vector3.forward * _zoomDistance);

            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref _currentVelocity, _smoothTime);
            transform.rotation = rotation;
        }

        public void FocusOn(Transform newTarget)
        {
            _target = newTarget;
            IsLockedToTarget = true;
        }

        public void SetPitchAndDistance(float pitch, float distance)
        {
            _pitchAngle = pitch;
            _zoomDistance = Mathf.Clamp(distance, _minZoom, _maxZoom);
        }
    }
}
