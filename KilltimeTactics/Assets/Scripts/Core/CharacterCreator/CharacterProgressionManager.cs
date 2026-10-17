// Point d'entree historique du moteur de progression (Livre I section 5).
// Decoupe en partials, sans changement d'API ni de logique :
// - CharacterProgressionManager.Funding.cs          : couts, FundingPlan, SpendForSkill
// - CharacterProgressionManager.OrganicTicks.cs     : GrantXP, coches, RegisterChallengeTicks, TrainSkill(+Free)
// - CharacterProgressionManager.AttributesSync.cs  : attributs, SynchronizeProgression, breakdown, ResetProgressionTree
// - CharacterProgressionManager.HeroAccess.cs       : SpecializationDetail, _registry, acces Mina/Lucas/Thomas/John/Erika/Vance
// - CharacterProgressionManager.Specializations.cs  : orchestrateur EnsureRegistryBuilt (delegue aux 4 catalogues)
// - CharacterProgressionManager.Specializations.CatalogMelee.cs    : catalogue melee (MainsNues, ManiementArmes, ArmesPercantes, Ballistique)
// - CharacterProgressionManager.Specializations.CatalogMobility.cs : catalogue mobilite/survie (Pas Invisibles, Esquive, Athletisme, encaissement, immunite)
// - CharacterProgressionManager.Specializations.CatalogSupport.cs  : catalogue soutien/social (medecine, arcanotech, tactique, communication, intuition, artisanat)
// - CharacterProgressionManager.Specializations.CatalogMagic.cs    : catalogue magie (generique + Volume II) + delegation 6 fiches heroiques
// - CharacterProgressionManager.Specializations.Queries.cs        : RegisterSpec, requetes (detail, listes, parents, Volume II, sources)
// - CharacterProgressionManager.Specializations.Unlock.cs         : UnlockSpecialization, LearnModularSpell
namespace Killtime.Core.Character
{
    /// <summary>
    /// Moteur d'evolution organique et de depense d'XP (Livre I section 5, Livres III &amp; IV).
    ///
    /// REGLE OFFICIELLE (Livre I section 5) :
    /// - Chaque REUSSITE d'epreuve de competence coche +1 case de progression DANS CETTE competence.
    /// - Taille de la piste = rang de base de la competence (ex. base 3 = 3 cases, base 5 = 5 cases).
    /// - Piste pleine = +1 XP LIE a cette competence (banque), piste remise a 0.
    /// - Depense au cout normal si l'XP provient de la competence amelioree (ou XP libre),
    ///   cout DOUBLE (x2) si l'XP provient d'une autre competence.
    /// - XP TOTAL du personnage = somme des XP DEPENSES en ameliorations (TotalSpentXP),
    ///   jamais les banques. Les banques (reserves liees + XP libre) sont l'argent en poche.
    /// (Documentation conservee ici ; la logique vit dans les partials ci-dessus.)
    /// </summary>
    public partial class CharacterProgressionManager
    {
    }
}
