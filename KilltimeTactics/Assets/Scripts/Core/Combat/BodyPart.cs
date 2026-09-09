namespace Killtime.Core.Combat
{
    /// <summary>
    /// Localisation anatomique chirurgicale du Système RP (Livre VI, Chapitre 26).
    /// </summary>
    public enum BodyPart
    {
        Tete,           // Boîte crânienne (commotion, KO direct)
        YeuxVisage,     // Yeux / Visage (cécité, désorientation)
        CouTrachee,     // Cou / Trachée (asphyxie, perte d'essoufflement)
        CoeurPoumons,   // Cœur / Poumons (dégâts massifs, perforation)
        Torse,          // Torse / Abdomen (encaissement standard)
        BrasDroit,      // Bras droit porteur d'arme (désarmement, malus d'attaque)
        BrasGauche,     // Bras gauche défensif/bouclier (perte de parade)
        Jambes          // Jambes / Cuisses / Genoux (chute à terre, mobilité réduite)
    }

    /// <summary>
    /// Paramètres et modificateurs d'un membre ciblé.
    /// </summary>
    public struct BodyPartInfo
    {
        public BodyPart Part;
        public string DisplayName;
        public int DifficultyModifier; // Malus de visée (-1 standard)
        public int CriticalDamageMultiplier;
        public float HitProbabilityWeight;

        public static BodyPartInfo GetInfo(BodyPart part)
        {
            return part switch
            {
                BodyPart.Tete => new BodyPartInfo { Part = part, DisplayName = "Tête / Crâne", DifficultyModifier = -2, CriticalDamageMultiplier = 3, HitProbabilityWeight = 0.15f },
                BodyPart.YeuxVisage => new BodyPartInfo { Part = part, DisplayName = "Yeux / Visage", DifficultyModifier = -3, CriticalDamageMultiplier = 3, HitProbabilityWeight = 0.10f },
                BodyPart.CouTrachee => new BodyPartInfo { Part = part, DisplayName = "Cou / Trachée", DifficultyModifier = -3, CriticalDamageMultiplier = 3, HitProbabilityWeight = 0.10f },
                BodyPart.CoeurPoumons => new BodyPartInfo { Part = part, DisplayName = "Cœur / Poumons", DifficultyModifier = -2, CriticalDamageMultiplier = 2, HitProbabilityWeight = 0.20f },
                BodyPart.Torse => new BodyPartInfo { Part = part, DisplayName = "Torse / Abdomen", DifficultyModifier = 0, CriticalDamageMultiplier = 1, HitProbabilityWeight = 0.40f },
                BodyPart.BrasDroit => new BodyPartInfo { Part = part, DisplayName = "Bras Droit (Arme)", DifficultyModifier = -1, CriticalDamageMultiplier = 1, HitProbabilityWeight = 0.25f },
                BodyPart.BrasGauche => new BodyPartInfo { Part = part, DisplayName = "Bras Gauche (Garde)", DifficultyModifier = -1, CriticalDamageMultiplier = 1, HitProbabilityWeight = 0.25f },
                BodyPart.Jambes => new BodyPartInfo { Part = part, DisplayName = "Jambes", DifficultyModifier = -1, CriticalDamageMultiplier = 1, HitProbabilityWeight = 0.30f },
                _ => new BodyPartInfo { Part = part, DisplayName = "Torse", DifficultyModifier = 0, CriticalDamageMultiplier = 1, HitProbabilityWeight = 0.40f }
            };
        }
    }
}
