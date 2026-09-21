using UnityEngine;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// Normalise la taille d'affichage de n'importe quel prefab de personnage
    /// à la taille d'unité standard du jeu, sans aucune valeur hardcodée par prefab.
    ///
    /// Principe : après instanciation (avec le scale auteur du prefab), on mesure
    /// la hauteur monde réelle du modèle tel qu'affiché, puis on applique le facteur
    /// qui ramène cette hauteur à <see cref="StandardUnitHeight"/> * unitScale.
    /// - prefab trop petit (ex : 10 cm) -> agrandi automatiquement (x18).
    /// - prefab trop grand (ex : 10 m) -> réduit automatiquement (x0.18).
    /// - prefab déjà plausible (ex : 1.82 m) -> inchangé (facteur 1, zéro régression).
    ///
    /// MESURE : on utilise les bounds au repos (sharedMesh, = pose de spawn car aucune
    /// animation n'a encore joué) transformées par des matrices monde RECOMPOSÉES À LA
    /// MAIN depuis les TRS locaux. Deux pièges prouvés par les logs (Player.log : un
    /// bake same-frame mesurait 'Soldier' à 34 m et 'Mina' à 159 m pour ~1.8 m affichés,
    /// ce qui rapetissait toutes les unités) sont ainsi évités :
    /// 1. SkinnedMeshRenderer.BakeMesh() juste après Instantiate peut retourner la pose
    ///    de bind non skinnée -> mesure gonflée ~20-90x. Jamais de BakeMesh ici.
    /// 2. Les matrices monde moteur (localToWorldMatrix, Renderer.bounds) peuvent ne pas
    ///    encore intégrer la hiérarchie à la frame d'instanciation -> on recompose nous-mêmes.
    /// </summary>
    public static class CharacterModelScaleNormalizer
    {
        /// <summary>Hauteur monde cible d'une unité humanoïde à unitScale 1 (référence boss : 1.82 m).</summary>
        public const float StandardUnitHeight = 1.8f;

        /// <summary>Plage de tailles jugée plausible pour un personnage : dedans, on respecte le scale auteur.</summary>
        public const float MinPlausibleHeight = 1.2f;
        public const float MaxPlausibleHeight = 2.4f;

        private const float MinValidHeight = 1e-4f;
        private const float MinFactor = 1e-3f;
        private const float MaxFactor = 1e3f;

        /// <summary>
        /// Recompose la matrice monde d'un Transform à la main (produit des TRS locaux
        /// jusqu'à la racine). Toujours valide, même à la frame d'instanciation.
        /// </summary>
        public static Matrix4x4 ComposeWorldMatrix(Transform t)
        {
            Matrix4x4 m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            Transform p = t.parent;
            while (p != null)
            {
                m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
                p = p.parent;
            }
            return m;
        }

        /// <summary>
        /// Mesure la hauteur monde (axe Y) du modèle tel qu'instancié, via les meshes
        /// au repos (== pose affichée au spawn). Retourne 0 si non mesurable.
        /// </summary>
        public static float MeasureModelHeight(GameObject instance)
        {
            if (instance == null) return 0f;

            bool hasBounds = false;
            Bounds worldBounds = default;

            // Meshes statiques : bounds du mesh partagé, replacées à la main dans le monde.
            var meshFilters = instance.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < meshFilters.Length; i++)
            {
                var mf = meshFilters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds b = TransformBounds(mf.sharedMesh.bounds, ComposeWorldMatrix(mf.transform));
                if (!IsValidBounds(b)) continue;
                if (!hasBounds) { worldBounds = b; hasBounds = true; }
                else worldBounds.Encapsulate(b);
            }

            // Meshes skinnés : bounds AU REPOS du mesh partagé (= pose de spawn, aucune
            // animation jouée). Volontairement PAS de BakeMesh (non fiable same-frame).
            var skinned = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
            for (int i = 0; i < skinned.Length; i++)
            {
                var smr = skinned[i];
                if (smr == null || smr.sharedMesh == null) continue;
                Bounds b = TransformBounds(smr.sharedMesh.bounds, ComposeWorldMatrix(smr.transform));
                if (!IsValidBounds(b)) continue;
                if (!hasBounds) { worldBounds = b; hasBounds = true; }
                else worldBounds.Encapsulate(b);
            }

            if (!hasBounds) return 0f;
            float h = worldBounds.size.y;
            return float.IsNaN(h) || float.IsInfinity(h) ? 0f : h;
        }

        /// <summary>
        /// Calcule le facteur d'échelle à appliquer (multiplicateur du localScale actuel).
        /// Retourne 1 si le modèle est déjà à une taille plausible ou non mesurable (no-op sûr :
        /// en cas de doute on garde le scale auteur, on ne rapetisse jamais sur une mesure douteuse).
        /// </summary>
        public static float ComputeScaleFactor(GameObject instance, float unitScale = 1f, float targetHeight = StandardUnitHeight)
        {
            if (instance == null) return 1f;
            float scale = Mathf.Max(unitScale, 1e-3f);
            float measured = MeasureModelHeight(instance);
            if (measured <= MinValidHeight)
            {
                Debug.LogWarning($"[CharacterModelScaleNormalizer] '{instance.name}' : géométrie non mesurable, scale auteur conservé.");
                return 1f;
            }

            // Taille plausible : on fait confiance à l'auteur du prefab, aucune correction.
            if (measured >= MinPlausibleHeight * scale && measured <= MaxPlausibleHeight * scale)
                return 1f;

            float target = Mathf.Max(targetHeight, MinValidHeight) * scale;
            float factor = target / measured;
            if (float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f) return 1f;
            if (factor < MinFactor || factor > MaxFactor)
            {
                Debug.LogWarning($"[CharacterModelScaleNormalizer] '{instance.name}' : facteur extrême ({factor:0.###}), clampé.");
                factor = Mathf.Clamp(factor, MinFactor, MaxFactor);
            }
            else
            {
                Debug.Log($"[CharacterModelScaleNormalizer] '{instance.name}' : {measured:0.##} m -> {target:0.##} m (x{factor:0.###}).");
            }
            return factor;
        }

        /// <summary>
        /// Applique la normalisation à l'instance. Retourne le facteur appliqué (1 = inchangé).
        /// </summary>
        public static float NormalizeToUnitHeight(GameObject instance, float unitScale = 1f, float targetHeight = StandardUnitHeight)
        {
            if (instance == null) return 1f;
            float factor = ComputeScaleFactor(instance, unitScale, targetHeight);
            if (!Mathf.Approximately(factor, 1f))
                instance.transform.localScale = instance.transform.localScale * factor;
            return factor;
        }

        /// <summary>
        /// Échelle monde (lossy) d'un Transform, calculée à la main depuis les TRS locaux.
        /// Toujours valide même à la frame d'instanciation (contrairement à lossyScale).
        /// </summary>
        public static Vector3 ExtractWorldScale(Transform t)
        {
            if (t == null) return Vector3.one;
            Matrix4x4 m = ComposeWorldMatrix(t);
            return new Vector3(
                new Vector3(m.m00, m.m10, m.m20).magnitude,
                new Vector3(m.m01, m.m11, m.m21).magnitude,
                new Vector3(m.m02, m.m12, m.m22).magnitude);
        }

        /// <summary>
        /// Impose une échelle MONDE à un objet déjà parenté, en compensant l'échelle de
        /// ses parents (ex : arme en main d'un personnage x18 -> l'arme garde sa taille
        /// réelle au lieu d'hériter du x18). Générique : aucun hardcode par modèle.
        /// </summary>
        public static void SetWorldScale(Transform t, Vector3 desiredWorldScale)
        {
            if (t == null) return;
            if (t.parent == null)
            {
                t.localScale = desiredWorldScale;
                return;
            }
            Vector3 parentScale = ExtractWorldScale(t.parent);
            t.localScale = new Vector3(
                Mathf.Abs(parentScale.x) > 1e-6f ? desiredWorldScale.x / parentScale.x : desiredWorldScale.x,
                Mathf.Abs(parentScale.y) > 1e-6f ? desiredWorldScale.y / parentScale.y : desiredWorldScale.y,
                Mathf.Abs(parentScale.z) > 1e-6f ? desiredWorldScale.z / parentScale.z : desiredWorldScale.z);
        }

        private static Bounds TransformBounds(Bounds local, Matrix4x4 m)
        {
            Vector3 center = m.MultiplyPoint3x4(local.center);
            Vector3 ext = local.extents;
            Vector3 axisX = m.MultiplyVector(new Vector3(ext.x, 0f, 0f));
            Vector3 axisY = m.MultiplyVector(new Vector3(0f, ext.y, 0f));
            Vector3 axisZ = m.MultiplyVector(new Vector3(0f, 0f, ext.z));
            Vector3 worldExt = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, worldExt * 2f);
        }

        private static bool IsValidBounds(Bounds b)
        {
            Vector3 s = b.size;
            return !(float.IsNaN(s.x) || float.IsNaN(s.y) || float.IsNaN(s.z)
                || float.IsInfinity(s.x) || float.IsInfinity(s.y) || float.IsInfinity(s.z))
                && s.x >= 0f && s.y >= 0f && s.z >= 0f;
        }
    }
}
