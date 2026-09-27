using UnityEngine;

[SelectionBase]
[DisallowMultipleComponent]
public class PlanetOrbitalController : MonoBehaviour
{
    [Header("Hierarchy Bindings")]
    [SerializeField] private Transform surfaceTransform;
    [SerializeField] private Transform cloudsTransform;
    [SerializeField] private Transform atmosphereTransform;

    [Header("Axial Tilt")]
    [Tooltip("Inclinaison axiale en degrés (ex: 23.4° pour la Terre).")]
    [Range(-90f, 90f)]
    [SerializeField] private float axialTilt = 18.5f;

    [Header("Rotation Velocities (deg/sec)")]
    [SerializeField] private float surfaceRotationSpeed = 0.25f;
    [SerializeField] private float cloudsDriftSpeed = 0.38f;

    [Header("Orbital Light")]
    [SerializeField] private Light mainSunLight;

    private void Reset()
    {
        LocateChildSpheres();
        if (mainSunLight == null)
        {
            mainSunLight = FindAnyObjectByType<Light>();
        }
    }

    private void Awake()
    {
        ApplyAxialTilt();
    }

    private void Update()
    {
        RotatePlanetaryLayers();
    }

    public void LocateChildSpheres()
    {
        Transform surface = transform.Find("Planet_Surface");
        if (surface != null) surfaceTransform = surface;

        Transform clouds = transform.Find("Planet_Clouds");
        if (clouds != null) cloudsTransform = clouds;

        Transform atmosphere = transform.Find("Planet_Atmosphere");
        if (atmosphere != null) atmosphereTransform = atmosphere;
    }

    private void ApplyAxialTilt()
    {
        transform.rotation = Quaternion.Euler(0f, 0f, axialTilt);
    }

    private void RotatePlanetaryLayers()
    {
        float delta = Time.deltaTime;

        if (surfaceTransform != null)
        {
            surfaceTransform.Rotate(Vector3.up, surfaceRotationSpeed * delta, Space.Self);
        }

        if (cloudsTransform != null)
        {
            cloudsTransform.Rotate(Vector3.up, cloudsDriftSpeed * delta, Space.Self);
        }
    }

    public void SetBombardmentIntensity(Material surfaceMaterial, float multiplier)
    {
        if (surfaceMaterial != null && surfaceMaterial.HasProperty("_BombardmentColor"))
        {
            Color baseColor = surfaceMaterial.GetColor("_BombardmentColor");
            surfaceMaterial.SetColor("_BombardmentColor", baseColor * multiplier);
        }
    }
}