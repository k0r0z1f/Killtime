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
            var rule = Rules.CoreRulesConfig.Instance.GetPartRule(part);
            string name = part switch
            {
                BodyPart.Tete => "Tête / Crâne",
                BodyPart.YeuxVisage => "Yeux / Visage",
                BodyPart.CouTrachee => "Cou / Trachée",
                BodyPart.CoeurPoumons => "Cœur / Poumons",
                BodyPart.Torse => "Torse / Abdomen",
                BodyPart.BrasDroit => "Bras Droit (Arme)",
                BodyPart.BrasGauche => "Bras Gauche (Garde)",
                BodyPart.Jambes => "Jambes",
                _ => "Torse"
            };

            float weight = part switch
            {
                BodyPart.Tete => 0.15f,
                BodyPart.YeuxVisage => 0.10f,
                BodyPart.CouTrachee => 0.10f,
                BodyPart.CoeurPoumons => 0.20f,
                BodyPart.Torse => 0.40f,
                BodyPart.BrasDroit => 0.25f,
                BodyPart.BrasGauche => 0.25f,
                BodyPart.Jambes => 0.30f,
                _ => 0.40f
            };

            return new BodyPartInfo
            {
                Part = part,
                DisplayName = name,
                DifficultyModifier = rule.DifficultyModifier,
                CriticalDamageMultiplier = rule.CriticalDamageMultiplier,
                HitProbabilityWeight = weight
            };
        }
    }
}
