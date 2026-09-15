using UnityEngine;

namespace Killtime.Tactics
{
    [DisallowMultipleComponent]
    public class LaserRifleWeapon : MonoBehaviour
    {
        [Header("Points d'Émission")]
        [Tooltip("Point précis à l'extrémité du canon. Si vide, utilise la position de l'arme.")]
        [SerializeField] private Transform _muzzlePoint;

        [Header("Paramètres du Rayon")]
        [SerializeField] private Color _laserColor = new Color(0.0f, 0.95f, 1.0f, 1.0f);
        [SerializeField] private Color _coreColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
        [SerializeField, Range(0.02f, 0.3f)] private float _beamWidth = 0.08f;
        [SerializeField, Range(0.05f, 0.5f)] private float _beamDuration = 0.16f;
        [SerializeField, Range(0.5f, 4.0f)] private float _missDeviationRadius = 1.8f;

        public Transform MuzzlePoint => _muzzlePoint;
        public Color LaserColor => _laserColor;

        public void FireLaser(Vector3 targetCenter, bool isHit, Vector3? forceHitPoint = null)
        {
            Vector3 origin = _muzzlePoint != null ? _muzzlePoint.position : transform.position + transform.forward * 0.4f;
            Vector3 endPoint;

            if (isHit)
            {
                endPoint = forceHitPoint ?? targetCenter;
            }
            else
            {
                Vector3 toTarget = (targetCenter - origin).normalized;
                Vector3 sideOffset = Vector3.Cross(toTarget, Vector3.up).normalized * (Random.value > 0.5f ? 1f : -1f) * Random.Range(0.8f, _missDeviationRadius);
                Vector3 groundOffset = toTarget * Random.Range(-0.5f, 1.5f);

                Vector3 missTarget = targetCenter + sideOffset + groundOffset;
                missTarget.y = 0.02f;

                Ray ray = new Ray(origin, (missTarget - origin).normalized);
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

                if (groundPlane.Raycast(ray, out float enter))
                {
                    endPoint = ray.GetPoint(enter);
                    endPoint.y = 0.02f;
                }
                else
                {
                    endPoint = missTarget;
                }
            }

            LaserBeamFX.Spawn(origin, endPoint, _laserColor, _coreColor, _beamWidth, _beamDuration, isHit);
        }
    }
}