using Killtime.Core.Character;
using Killtime.Core.Inventory;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// RD-201 — Canon Entravé (Livre V §22.2) : au contact d'un combattant
    /// conscient (distance 1), une arme longue (portée &gt; 1, compétence de tir,
    /// ou deux mains) ne peut pas tirer normalement. Le tireur choisit :
    /// 1. Tir de rétention (2 PA, malus -2 conservé),
    /// 2. Tir visé (3 PA : +1 PA annule le malus et autorise la visée membre),
    /// 3. Crosse (2 PA, Maniement contondant 3-4 bruts, Déstabilisé si &gt; CON x2),
    /// 4. Mains Nues (2 PA, exemption totale, Refoulement).
    /// Le Décrochage (1 PA, déjà codé) rompt le contact et lève la condition.
    /// Pur C# (aucune dépendance Unity) : testable en EditMode.
    /// </summary>
    public enum CanonEntraveOption
    {
        None,
        RetentionShot,
        AimedShot,
        Buttstroke,
        UnarmedStrike
    }

    public static class CanonEntraveRules
    {
        // Coûts PA Livre V §22.2 (avant malus armure lourde / Ralenti).
        public const int RetentionShotAPCost = 2;
        public const int AimedShotAPCost = 3;
        public const int ButtstrokeAPCost = 2;
        public const int UnarmedAPCost = 2;

        /// <summary>Malus de tir générique au contact entravé.</summary>
        public const int EntravePenalty = -2;

        /// <summary>Dégâts de base d'un coup à mains nues au contact (base
        /// générique du duel, faute de valeur Livre chiffrée).</summary>
        public const int UnarmedBaseDamage = 5;

        /// <summary>
        /// Vrai si le combattant porte une arme longue au sens du Canon
        /// Entravé (même prédicat que TacticalUnit.IsCanonEntrave, sans le
        /// balayage des voisins : portée &gt; 1, compétence de tir, deux mains).
        /// </summary>
        public static bool HasLongWeapon(CharacterStats stats)
        {
            return HasLongWeapon(stats?.Sheet?.GetEquippedWeapon());
        }

        /// <summary>Vrai si cette arme est longue au sens du Canon Entravé.</summary>
        public static bool HasLongWeapon(InventoryItem weapon)
        {
            if (weapon == null) return false;
            return weapon.RangeInTiles > 1
                || weapon.AssociatedSkill == SkillType.Ballistique
                || weapon.AssociatedSkill == SkillType.ProjectilesTir
                || weapon.EquipSlot == ItemEquipSlot.TwoHands;
        }

        /// <summary>
        /// Vrai si la situation remplit la condition Livre V §22.2 : cible
        /// consciente (capable de défense active) au contact (distance ≤ 1)
        /// et attaquant porteur d'une arme longue.
        /// </summary>
        public static bool IsEntraveSituation(CharacterStats attacker, CharacterStats defender, int distanceTiles)
        {
            if (attacker == null || defender == null) return false;
            if (distanceTiles > 1) return false;
            if (!defender.CanDefendActively()) return false;
            return HasLongWeapon(attacker);
        }

        /// <summary>
        /// Dégâts bruts d'un coup de crosse : 4 avec une arme à deux mains
        /// (crosse de fusil), 3 sinon. Lecture documentée de « 3-4 bruts ».
        /// </summary>
        public static int ButtstrokeBaseDamage(InventoryItem weapon)
        {
            if (weapon != null && weapon.EquipSlot == ItemEquipSlot.TwoHands) return 4;
            return 3;
        }

        /// <summary>
        /// Crosse : Déstabilisé si les dégâts bruts (base + différentiel,
        /// avant armure) dépassent CON x2, soit le seuil d'encaissement
        /// (CON x EncaissementMultiplier = CON x2 au Codex).
        /// </summary>
        public static bool ButtstrokeDestabilises(int rawBrutDamage, CharacterStats defender)
        {
            if (defender == null) return false;
            return rawBrutDamage > defender.EncaissementThreshold;
        }

        /// <summary>
        /// Applique l'effet crosse (RD-201) : Déstabilisé (1 tour) si les
        /// bruts dépassent CON x2. Retourne vrai si appliqué.
        /// </summary>
        public static bool ApplyButtstrokeDestabilise(CharacterStats defender, int rawBrutDamage, ref StatusEffect inflictedStatus)
        {
            if (!ButtstrokeDestabilises(rawBrutDamage, defender)) return false;
            inflictedStatus |= StatusEffect.Destabilise;
            defender.ApplyStatus(StatusEffect.Destabilise, 1);
            return true;
        }

        /// <summary>
        /// Tir possible avec cette arme ? Miroir pur de la logique registre /
        /// arène (RD-033 + RD-038) : sans munitions, toujours vrai (arc,
        /// arme sans chargeur) ; sinon ni enrayé ni vide, et pas de sprint.
        /// </summary>
        public static bool CanFireShot(InventoryItem weapon, bool sprintBlocked)
        {
            if (weapon == null || weapon.AmmoCapacity <= 0) return true;
            if (sprintBlocked) return false;
            return !weapon.Jammed && weapon.AmmoRemaining > 0;
        }

        /// <summary>
        /// RD-201 : choix de l'option de contact. Le tir prime quand il est
        /// possible et payé (visé si ≥ 3 PA, rétention si ≥ 2 PA) ; sinon
        /// repli crosse (arme longue) ou mains nues. Jamais de tir sans
        /// munitions ni sans PA suffisants.
        /// </summary>
        public static CanonEntraveOption ChooseContactOption(
            int currentAP, bool canFireShot, bool hasLongWeapon, bool canUnarmedAttack)
        {
            if (currentAP < RetentionShotAPCost) return CanonEntraveOption.None;
            if (canFireShot && currentAP >= AimedShotAPCost) return CanonEntraveOption.AimedShot;
            if (canFireShot) return CanonEntraveOption.RetentionShot;
            if (hasLongWeapon) return CanonEntraveOption.Buttstroke;
            if (canUnarmedAttack) return CanonEntraveOption.UnarmedStrike;
            return CanonEntraveOption.None;
        }
    }
}
