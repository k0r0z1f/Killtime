using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Inventory;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// Arme (ou objet tenu) tombée au sol après K.O. / inconscience / mort.
    /// Physique réelle (Rigidbody PhysX : gravité, rebond, friction) + ramassage tactique.
    /// Créée par <see cref="TacticalUnitVisual.DropHeldItemsWithPhysics"/> au moment où
    /// l'unité bascule en <c>Inconscient</c> / mort. L'item est RETIRÉ de l'inventaire
    /// du porteur : l'objet au sol est l'unique exemplaire (loot possible).
    /// </summary>
    public class DroppedWeaponPickup : MonoBehaviour
    {
        [Header("Butin au sol")]
        [Tooltip("Exemplaire unique retiré de l'inventaire du porteur K.O.")]
        public InventoryItem DroppedItem;

        [Tooltip("Nom du porteur qui a lâché l'arme (log / fluff).")]
        public string FormerOwnerName = "";

        [Header("Ramassage")]
        [Tooltip("Distance max (m) pour ramasser l'arme au sol.")]
        public float PickupRadius = 1.2f;

        [Header("Physique réelle")]
        [Tooltip("Une fois l'arme immobile au sol, on la fige (kinematic) pour économiser la physique et éviter les micro-roulis infinis.")]
        public bool FreezeWhenSettled = true;

        [Tooltip("Vitesse sous laquelle l'arme est considérée posée (m/s).")]
        public float SettleVelocityThreshold = 0.15f;

        [Tooltip("Temps d'immobilité avant gel (s).")]
        public float SettleDelay = 0.8f;

        [Tooltip("Friction du métal / polymère au sol.")]
        [Range(0f, 1f)] public float GroundFriction = 0.6f;

        [Tooltip("Rebond : une arme métallique rebondit un peu sur un sol dur (0 = aucun, 1 = bille).")]
        [Range(0f, 0.6f)] public float Bounciness = 0.25f;

        private Rigidbody _rb;
        private float _lowSpeedTimer;
        private bool _settled;
        private float _nextProximityScan;
        private static readonly List<DroppedWeaponPickup> _all = new();

        private void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
        private void OnDisable() { _all.Remove(this); }

        public static IReadOnlyList<DroppedWeaponPickup> AllDropped => _all;

        /// <summary>
        /// Nettoyage global des armes/objets au sol (gourdins, lames, fusils...).
        /// À appeler à chaque (ré)initialisation de combat : ResetArena, ClearAllUnits,
        /// SetupArenaUnits, LoadUnitsFromMap, ApplyLoadedMap, CleanupScene.
        /// Détruit les GameObjects (différé) et purge le registre statique pour
        /// éviter les fantômes d'un combat précédent (cf. screenshot : 3 gourdins).
        /// </summary>
        public static void ClearAllDropped()
        {
            try
            {
                var scenePickups = Object.FindObjectsByType<DroppedWeaponPickup>(FindObjectsInactive.Include);
                for (int i = 0; i < scenePickups.Length; i++)
                {
                    var p = scenePickups[i];
                    if (p == null) continue;
                    try
                    {
                        p.DroppedItem = null;
                        if (p.gameObject != null)
                        {
                            p.gameObject.SetActive(false);
                            Object.Destroy(p.gameObject);
                        }
                    }
                    catch { /* nettoyage jamais bloquant */ }
                }
            }
            catch { /* FindObjects peut échouer en teardown */ }
            _all.Clear();
        }

        /// <summary>
        /// Initialise le butin + garantit une physique réelle (Rigidbody + colliders actifs).
        /// À appeler juste après détachement de la main (world transform déjà préservée).
        /// </summary>
        public void Initialize(InventoryItem item, string formerOwner, Vector3 initialVelocity, Vector3 initialAngularVelocity)
        {
            DroppedItem = item;
            FormerOwnerName = formerOwner ?? "";
            EnsureRealPhysics();
            if (_rb != null)
            {
                _rb.linearVelocity = initialVelocity;
                _rb.angularVelocity = initialAngularVelocity;
            }
        }

        private void Awake()
        {
            EnsureRealPhysics();
        }

        /// <summary>
        /// Active colliders + Rigidbody avec masse réelle (poids fiche, Livre VIII).
        /// Idempotent : réutilisable si l'objet est re-lâché.
        /// </summary>
        public void EnsureRealPhysics()
        {
            // 1) Réactive les colliders (désactivés quand l'arme était en main).
            var cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                {
                    cols[i].enabled = true;
                    // Un trigger ne fait jamais tomber : on veut un vrai contact sol.
                    if (cols[i].isTrigger) cols[i].isTrigger = false;
                }
            }

            // 2) Garantit au moins un collider : sinon l'arme traverse le sol.
            if (cols == null || cols.Length == 0)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                // Taille estimée depuis les renderers (monde -> local).
                var renderers = GetComponentsInChildren<Renderer>();
                if (renderers != null && renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                    Vector3 worldSize = b.size;
                    Vector3 lossy = transform.lossyScale;
                    box.size = new Vector3(
                        worldSize.x / Mathf.Max(1e-4f, Mathf.Abs(lossy.x)),
                        worldSize.y / Mathf.Max(1e-4f, Mathf.Abs(lossy.y)),
                        worldSize.z / Mathf.Max(1e-4f, Mathf.Abs(lossy.z)));
                    box.center = transform.InverseTransformPoint(b.center);
                }
                else
                {
                    box.size = new Vector3(0.08f, 0.08f, 0.7f);
                }
                ApplyPhysicMaterial(box);
            }
            else
            {
                for (int i = 0; i < cols.Length; i++)
                    ApplyPhysicMaterial(cols[i]);
            }

            // 3) Les Animator d'arme (recul, glow laser) ne doivent pas bouger l'objet au sol.
            var anims = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < anims.Length; i++)
                if (anims[i] != null) anims[i].enabled = false;

            // 4) Rigidbody : vraie chute (gravité + inertie).
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            float massKg = (DroppedItem != null && DroppedItem.WeightKg > 0.01f) ? DroppedItem.WeightKg : 1.2f;
            _rb.mass = Mathf.Clamp(massKg, 0.3f, 10f);
            _rb.useGravity = true;
            _rb.isKinematic = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            // Une arme qui tombe ne doit pas glisser comme sur de la glace.
            _rb.linearDamping = 0.15f;
            _rb.angularDamping = 1.2f;
            _rb.sleepThreshold = 0.15f;
            _rb.WakeUp();

            _settled = false;
            _lowSpeedTimer = 0f;
            gameObject.layer = 0; // Default : collide avec le sol (MeshCollider des tuiles).
        }

        private void ApplyPhysicMaterial(Collider col)
        {
            if (col == null) return;
            // Partagé à la volée : pas d'asset à créer dans le projet.
            var mat = new PhysicsMaterial("DroppedWeapon_Metal")
            {
                dynamicFriction = GroundFriction,
                staticFriction = Mathf.Clamp01(GroundFriction + 0.1f),
                bounciness = Bounciness,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Average
            };
            col.material = mat;
        }

        private void FixedUpdate()
        {
            if (_settled || !FreezeWhenSettled || _rb == null || _rb.isKinematic) return;
            float speed = _rb.linearVelocity.magnitude;
            float spin = _rb.angularVelocity.magnitude;
            if (speed < SettleVelocityThreshold && spin < 0.6f)
            {
                _lowSpeedTimer += Time.fixedDeltaTime;
                if (_lowSpeedTimer >= SettleDelay)
                {
                    // Posée : on fige pour éviter roulis/jitter et économiser la physique.
                    // La pose RESTE celle donnée par la simulation (rotation réelle au sol).
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                    _rb.isKinematic = true;
                    _settled = true;
                }
            }
            else
            {
                _lowSpeedTimer = 0f;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Petit "clang" métallique proportionnel à l'impact (physique audible).
            if (collision == null || collision.relativeVelocity.magnitude < 1.2f) return;
            try
            {
                var audio = Killtime.Audio.KilltimeAudioManager.Instance;
                if (audio != null)
                {
                    float vol = Mathf.Clamp01(collision.relativeVelocity.magnitude / 8f);
                    audio.PlayAt(Killtime.Audio.SoundId.Grenade_Bounce, transform.position, Mathf.Max(0.25f, vol));
                }
            }
            catch { /* audio optionnel */ }
        }

        // --- Ramassage -------------------------------------------------------

        /// <summary>Vrai si cette unité consciente peut ramasser (distance + état).</summary>
        public bool CanBePickedUpBy(TacticalUnit unit)
        {
            if (unit == null || DroppedItem == null) return false;
            if (unit.Stats == null || !unit.Stats.IsAlive) return false;
            if (unit.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient)) return false;
            if (unit.Stats.ActiveStatus.HasFlag(StatusEffect.Paralyse)) return false;
            if (unit.Stats.ActiveStatus.HasFlag(StatusEffect.Sonne)) return false;
            float d = Vector3.Distance(unit.transform.position, transform.position);
            return d <= PickupRadius + 0.35f;
        }

        /// <summary>
        /// Transfère l'arme du sol vers l'inventaire de l'unité (équipe si main libre).
        /// Retourne false si trop loin / inconscient / inventaire plein (jamais plein actuellement).
        /// </summary>
        public bool TryPickup(TacticalUnit unit)
        {
            if (!CanBePickedUpBy(unit)) return false;
            var sheet = unit.GetOrBuildSheet();
            if (sheet == null) return false;

            DroppedItem.IsEquipped = false;
            sheet.AddItem(DroppedItem);
            // Équipe auto seulement si aucune arme en main (ne vole pas l'arme courante).
            if (sheet.GetEquippedWeapon() == null && DroppedItem.Type == ItemType.Weapon)
                sheet.EquipItem(DroppedItem.ItemId);

            string pickedName = DroppedItem.Name;
            DroppedItem = null;
            unit.NotifyInventoryChanged(saveToDisk: true);

            var visual = unit.GetComponent<TacticalUnitVisual>();
            if (visual != null) visual.SpawnFloatingText($"⚔ {pickedName} ramassée", Color.green);

            Destroy(gameObject);
            return true;
        }

        /// <summary>
        /// Ramassage de la plus proche arme au sol à portée (appel UI / touche d'interaction).
        /// </summary>
        public static bool TryPickupNearest(TacticalUnit unit, out string message)
        {
            message = "";
            if (unit == null) { message = "Aucune unité."; return false; }
            DroppedWeaponPickup best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _all.Count; i++)
            {
                var d = _all[i];
                if (d == null || d.DroppedItem == null) continue;
                if (!d.CanBePickedUpBy(unit)) continue;
                float dist = Vector3.Distance(unit.transform.position, d.transform.position);
                if (dist < bestD) { bestD = dist; best = d; }
            }
            if (best == null)
            {
                message = "Aucune arme au sol à portée.";
                return false;
            }
            bool ok = best.TryPickup(unit);
            message = ok ? $"⚔ {best.DroppedItem?.Name ?? "Arme"} ramassée." : "Ramassage impossible.";
            // best est détruit en cas de succès : message déjà copié avant ? on reformule :
            if (ok) message = "⚔ Arme ramassée.";
            return ok;
        }

        /// <summary>
        /// Shoote / kicke l'objet plus loin : impulsion physique depuis le botteur.
        /// Direction = du botteur vers l'objet (au sol) + petit lob vertical.
        /// Réveille le Rigidbody (dé-fige) et relance le timer de stabilisation.
        /// Retourne false si trop loin / K.O. / gelé.
        /// </summary>
        public bool TryKickFurther(TacticalUnit kicker, float kickForce = 4.5f)
        {
            if (kicker == null || DroppedItem == null) return false;
            if (kicker.Stats == null || !kicker.Stats.IsAlive) return false;
            if (kicker.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient)) return false;
            if (kicker.Stats.ActiveStatus.HasFlag(StatusEffect.Paralyse)) return false;
            float d = Vector3.Distance(kicker.transform.position, transform.position);
            // Tolérance un peu plus large que le ramassage (coup de pied allonge la jambe).
            if (d > PickupRadius + 1.0f) return false;

            // S'assure que la physique est active (l'objet figé est kinematic).
            EnsureRealPhysics();
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            if (_rb == null) return false;
            _rb.isKinematic = false;
            _settled = false;
            _lowSpeedTimer = 0f;
            _rb.WakeUp();

            Vector3 dir = transform.position - kicker.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = kicker.transform.forward;
            dir.Normalize();
            float massRef = Mathf.Clamp(_rb.mass, 0.3f, 10f);
            float force = kickForce * Mathf.Clamp(1.6f / massRef, 0.6f, 1.4f);
            _rb.AddForce(dir * force + Vector3.up * (force * 0.35f), ForceMode.Impulse);
            _rb.AddTorque(new Vector3(
                UnityEngine.Random.Range(-4f, 4f),
                UnityEngine.Random.Range(-3f, 3f),
                UnityEngine.Random.Range(-5f, 5f)), ForceMode.Impulse);

            try
            {
                var audio = Killtime.Audio.KilltimeAudioManager.Instance;
                if (audio != null)
                    audio.PlayAt(Killtime.Audio.SoundId.Grenade_Bounce, transform.position + Vector3.up * 0.4f, 0.6f);
            }
            catch { }
            var visual = kicker.GetComponent<TacticalUnitVisual>();
            if (visual != null) visual.SpawnFloatingText($"🦵 {DroppedItem.Name} shooté !", Color.yellow);
            return true;
        }

        /// <summary>
        /// Détruit l'objet au sol (jeté à la poubelle tactique). Log fluff via l'acteur.
        /// </summary>
        public void DestroyPickup(TacticalUnit actor = null)
        {
            string name = DroppedItem != null ? DroppedItem.Name : "Objet";
            DroppedItem = null;
            if (actor != null)
            {
                var visual = actor.GetComponent<TacticalUnitVisual>();
                if (visual != null) visual.SpawnFloatingText($"🗑 {name} détruit", Color.red);
            }
            try { Destroy(gameObject); }
            catch { }
        }

        private void OnGUI()
        {
            // Étiquette discrète au-dessus de l'arme au sol (nom + portée de ramassage).
            if (DroppedItem == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 0.35f);
            if (sp.z < 0.3f) return;
            float dist = Vector3.Distance(cam.transform.position, transform.position);
            if (dist > 14f) return; // évite le spam à l'échelle de l'arène.

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            string label = $"⚔ {DroppedItem.Name} — clic droit";
            Vector2 size = style.CalcSize(new GUIContent(label));
            float w = Mathf.Max(size.x + 16f, 110f);
            Rect r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - 14f, w, 20f);
            Color bg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.85f);
            GUI.Box(r, GUIContent.none);
            GUI.backgroundColor = bg;
            style.normal.textColor = new Color(1f, 0.85f, 0.4f);
            GUI.Label(r, label, style);
        }
    }
}
