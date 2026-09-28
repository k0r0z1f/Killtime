using UnityEngine;

[SelectionBase]
[DisallowMultipleComponent]
[AddComponentMenu("Killtime/Space/Planet Nefris Rotation (22h)")]
public class PlanetOrbitalController : MonoBehaviour
{
    [Header("Hierarchy Bindings")]
    [Tooltip("Si vide : auto-trouvé via Planet_Surface / Planet_Clouds / Planet_Atmosphere.")]
    [SerializeField] private Transform surfaceTransform;
    [SerializeField] private Transform cloudsTransform;
    [SerializeField] private Transform atmosphereTransform;

    [Header("Nefris — Rotation sidérale")]
    [Tooltip("Durée d'un jour sidéral de Nefris, en heures jeu.")]
    [Min(0.01f)]
    [SerializeField] private float rotationPeriodHours = 22f;

    [Tooltip("Accélération temporelle : secondes de jeu par seconde réelle. 22h réelles = invisible, donc on accélère. Ex: 120 => tour complet en ~11 min (cinématique lent) ; 3600 => tour en 22s (demo).")]
    [Min(0f)]
    [SerializeField] private float timeScale = 120f;

    [Tooltip("Cocher pour une vitesse 1:1 temps réel (tour en 22h vraies). Ignore timeScale.")]
    [SerializeField] private bool realTimeMode = false;

    [Header("Nuages — rotation différentielle")]
    [Tooltip("Oui : les nuages tournent différemment (recommandé 1.2 à 1.5). 1.0 = synchrone avec la surface.")]
    [Range(0.1f, 4f)]
    [SerializeField] private float cloudSpeedMultiplier = 1.35f;

    [Tooltip("Dérive latitudinale lente des nuages (deg/sec jeu), donne un cisaillement réaliste.")]
    [Range(-2f, 2f)]
    [SerializeField] private float cloudShearDegrees = 0.15f;

    [Tooltip("Direction : +1 prograde (même sens que surface), -1 rétrograde.")]
    [SerializeField] private float cloudDirection = 1f;

    [Header("Axial Tilt")]
    [Tooltip("Inclinaison axiale en degrés (ex: 23.4° pour la Terre).")]
    [Range(-90f, 90f)]
    [SerializeField] private float axialTilt = 18.5f;

    [Header("Orbital Light")]
    [SerializeField] private Light mainSunLight;

    [Header("Debug")]
    [SerializeField] private bool useUnscaledTime = false;
    [SerializeField] private bool showDebugLogs = false;

    // Vitesse réelle calculée (deg / seconde réelle), exposée pour debug.
    public float SurfaceDegPerRealSecond { get; private set; }
    public float CloudsDegPerRealSecond { get; private set; }

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
        if (surfaceTransform == null || cloudsTransform == null)
            LocateChildSpheres();
        ApplyAxialTilt();
    }

    private void OnValidate()
    {
        rotationPeriodHours = Mathf.Max(0.01f, rotationPeriodHours);
        timeScale = Mathf.Max(0f, timeScale);
        if (Application.isPlaying) return;
        // Preview éditeur : applique le tilt sans Play
        // (sans rotation continue pour ne pas polluer la scène)
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
        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (delta <= 0f) return;

        float effectiveTimeScale = realTimeMode ? 1f : timeScale;
        // 360° par période : deg/sec jeu -> x timeScale = deg/sec réelle
        float surfaceDegPerSec = (360f / (rotationPeriodHours * 3600f)) * effectiveTimeScale;
        float cloudDegPerSec = surfaceDegPerSec * cloudSpeedMultiplier * cloudDirection;

        SurfaceDegPerRealSecond = surfaceDegPerSec;
        CloudsDegPerRealSecond = cloudDegPerSec;

        if (surfaceTransform != null)
        {
            surfaceTransform.Rotate(Vector3.up, surfaceDegPerSec * delta, Space.Self);
        }

        if (cloudsTransform != null)
        {
            cloudsTransform.Rotate(Vector3.up, cloudDegPerSec * delta, Space.Self);
            // Cisaillement : petite oscillation / dérive sur l'axe local pour éviter
            // un mouvement parfaitement rigide (imite les bandes de circulation).
            if (Mathf.Abs(cloudShearDegrees) > 0.0001f)
            {
                float shear = cloudShearDegrees * effectiveTimeScale / (rotationPeriodHours * 3600f) * 360f;
                // shear est déjà très petit ; on l'applique sur Z local en complément
                cloudsTransform.Rotate(Vector3.forward, shear * delta * 0.1f, Space.Self);
            }
        }

        // L'atmosphère (rim fresnel) ne tourne pas : shader view-dependent.
        // On la laisse fixe pour éviter du swimming. Rien à faire.

        if (showDebugLogs && Time.frameCount % 300 == 0)
        {
            Debug.Log($"[Planet_Nefris] surface {surfaceDegPerSec:F4}°/s, nuages {cloudDegPerSec:F4}°/s (x{cloudSpeedMultiplier}), tour surface en {rotationPeriodHours * 3600f / Mathf.Max(1f, effectiveTimeScale):F1}s réelles.");
        }
    }

    /// <summary>
    /// Change l'accélération à runtime (ex: bouton pause / x1 / x100).
    /// </summary>
    public void SetTimeScale(float newTimeScale)
    {
        timeScale = Mathf.Max(0f, newTimeScale);
    }

    /// <summary>
    /// Temps estimé (secondes réelles) pour un tour complet de la surface avec les réglages actuels.
    /// </summary>
    public float GetRealSecondsPerRotation()
    {
        float eff = realTimeMode ? 1f : Mathf.Max(0.001f, timeScale);
        return rotationPeriodHours * 3600f / eff;
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
