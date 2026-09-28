using UnityEngine;

/// <summary>
/// Petit rotateur générique pour les planètes placeholder / hologrammes
/// (ex: Viewport_NefrisOrbit > Planet_Nefris_BurningInVoid dans l'intro).
/// Le vrai prefab Planet_Nefris utilise PlanetOrbitalController (22h + nuages).
/// </summary>
[AddComponentMenu("Killtime/Space/Planet Simple Spin")]
public class PlanetSimpleSpin : MonoBehaviour
{
    [Tooltip("Degrés par seconde réelle. 2°/s = tour en 3 min. Valeur négative = sens inverse.")]
    public float degreesPerSecond = 0.5f;

    [Tooltip("Axer la rotation sur Y local (jour sidéral).")]
    public bool useLocalAxis = true;

    private void Update()
    {
        float d = degreesPerSecond * Time.deltaTime;
        if (Mathf.Abs(d) < 0.000001f) return;
        if (useLocalAxis)
            transform.Rotate(Vector3.up, d, Space.Self);
        else
            transform.Rotate(Vector3.up, d, Space.World);
    }
}
