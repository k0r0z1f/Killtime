using System.Collections.Generic;

namespace Killtime.Story
{
    /// <summary>
    /// Catalogue provisoire des scènes jouables. Il fournit un point d'entrée
    /// immédiatement testable dans la DevUI; le contenu sera déplacé vers des assets
    /// de scénario lorsque les cartes et les cinématiques seront produites.
    /// </summary>
    public static class ScenarioCatalog
    {
        public const string AtomicCrucibleId = "volume_1_scene_01_atomic_crucible";

        private static readonly Dictionary<string, ScenarioDefinition> Definitions = new()
        {
            { AtomicCrucibleId, BuildAtomicCrucible() }
        };

        public static IEnumerable<ScenarioDefinition> All => Definitions.Values;

        public static ScenarioDefinition Find(string id)
        {
            Definitions.TryGetValue(id, out var definition);
            return definition;
        }

        private static ScenarioDefinition BuildAtomicCrucible()
        {
            return new ScenarioDefinition
            {
                Id = AtomicCrucibleId,
                Volume = "Volume I — The Awakening Storm",
                Title = "01 — Le creuset atomique",
                CanonReference = "Chapitre 1 — The Atomic Crucible",
                FirstNodeId = "briefing",
                Nodes = new List<ScenarioNode>
                {
                    new()
                    {
                        Id = "briefing",
                        Kind = ScenarioNodeKind.Briefing,
                        Title = "La proposition",
                        Location = "Base de la Résistance — astéroïde en orbite de Nefris",
                        Body = "La salle de commandement vibre au rythme des machines. Nefris brûle derrière les hublots. " +
                               "Le coordinateur fait glisser un datapad vers John.\n\n" +
                               "COORDINATEUR — Traverser les lignes impériales est déjà suicidaire. Y faire passer notre invité… c'est autre chose.\n\n" +
                               "JOHN — Vous ne payez pas un passage. Vous payez quelqu'un qui revient vivant.\n\n" +
                               "ERIKA — Ce n'est pas un transport. Si nous comprenons ce qu'il est, nous pouvons changer la forme de cette guerre.",
                        ContinueLabel = "Examiner le contrat",
                        NextNodeId = "contract"
                    },
                    new()
                    {
                        Id = "contract",
                        Kind = ScenarioNodeKind.Choice,
                        Title = "Le prix de l'espoir",
                        Location = "Salle de commandement",
                        Body = "Le champ de signature du datapad attend. Derrière la porte blindée, quelqu'un respire trop vite. " +
                               "Le coordinateur ne donne qu'une instruction: « Ramenez notre messie. »\n\n" +
                               "Le choix de John ne définit pas seulement ce départ: il décide qui pense posséder l'avenir de Lucas.",
                        Choices = new List<ScenarioChoice>
                        {
                            new()
                            {
                                Id = "sign_contract",
                                Label = "Signer le contrat",
                                Consequence = "Voie du manuscrit: la Résistance finance le départ vers Vardis, mais exigera son dû.",
                                NextNodeId = "hangar_vardis",
                                Effects = new List<ScenarioEffect>
                                {
                                    new() { Type = ScenarioEffectType.SetRoute, Route = StoryRoute.Vardis },
                                    new() { Type = ScenarioEffectType.AddFlag, Key = "lucas_protected_by_john_erika" },
                                    new() { Type = ScenarioEffectType.AddFlag, Key = "resistance_contract_signed" },
                                    new() { Type = ScenarioEffectType.AddJournal, Text = "La Résistance a acheté une chance. Le prix reste à payer." }
                                }
                            },
                            new()
                            {
                                Id = "renegotiate_contract",
                                Label = "Renégocier la mission",
                                Consequence = "Voie indépendante: Erika soutient John; moins de matériel, mais aucune balise de la Résistance.",
                                NextNodeId = "hangar_independent",
                                Effects = new List<ScenarioEffect>
                                {
                                    new() { Type = ScenarioEffectType.SetRoute, Route = StoryRoute.Independent },
                                    new() { Type = ScenarioEffectType.AddFlag, Key = "lucas_protected_without_faction" },
                                    new() { Type = ScenarioEffectType.AddInteger, Key = "john_erika_trust", Amount = 1 },
                                    new() { Type = ScenarioEffectType.AddJournal, Text = "John a refusé de transformer Lucas en arme. La fuite sera plus pauvre, mais libre." }
                                }
                            },
                            new()
                            {
                                Id = "refuse_contract",
                                Label = "Refuser et extraire Lucas",
                                Consequence = "Voie impériale: la base verrouille ses portes. La Résistance devient instable et l'évasion commence immédiatement.",
                                NextNodeId = "escape_imperial",
                                Effects = new List<ScenarioEffect>
                                {
                                    new() { Type = ScenarioEffectType.SetRoute, Route = StoryRoute.Imperial },
                                    new() { Type = ScenarioEffectType.AddFlag, Key = "lucas_is_extraction_target" },
                                    new() { Type = ScenarioEffectType.AddFlag, Key = "resistance_alerted" },
                                    new() { Type = ScenarioEffectType.AddJournal, Text = "John a refusé le marché. Désormais, même ses alliés peuvent devenir des geôliers." }
                                }
                            }
                        }
                    },
                    BuildHangarNode(
                        "hangar_vardis",
                        "Préparer le Starlight Voyager",
                        "Le contrat est signé. Syn termine les diagnostics pendant qu'Erika vérifie les sangles médicales. Le quai sera verrouillé dans six minutes.",
                        new List<ScenarioObjective>
                        {
                            Objective("vardis_reach_hangar", "Rejoindre le hangar", "Apprendre déplacement et caméra."),
                            Objective("vardis_inspect_supplies", "Vérifier les réserves", "Confirmer les kits d'urgence et les sangles médicales."),
                            Objective("vardis_check_jammer", "Examiner le brouilleur", "Choisir de stabiliser la signature ou de préserver le condensateur.")
                        }),
                    BuildHangarNode(
                        "hangar_independent",
                        "Partir sans balise",
                        "Erika obtient un compromis in extremis. Syn doit préparer le Voyager sans la balise de la Résistance, tandis qu'une inspection approche.",
                        new List<ScenarioObjective>
                        {
                            Objective("independent_reach_hangar", "Rejoindre le hangar", "Apprendre déplacement et caméra."),
                            Objective("independent_remove_beacon", "Retirer la balise", "Couper le traçage de la Résistance avant le départ."),
                            Objective("independent_check_jammer", "Examiner le brouilleur", "Préparer une sortie à faible signature.")
                        }),
                    BuildHangarNode(
                        "escape_imperial",
                        "La base se referme",
                        "Le refus déclenche l'alarme. Les portes se ferment une à une. John doit atteindre le couloir sécurisé avant les drones.",
                        new List<ScenarioObjective>
                        {
                            Objective("imperial_reach_corridor", "Atteindre le couloir sécurisé", "Tutoriel de déplacement sous pression."),
                            Objective("imperial_hold_door", "Ordonner à Erika de maintenir la porte", "Tutoriel d'ordre allié."),
                            Objective("imperial_bypass_lock", "Contourner le verrou médical", "Extraire la cible avant l'arrivée des drones.")
                        }),
                    new()
                    {
                        Id = "reveal_lucas",
                        Kind = ScenarioNodeKind.Resolution,
                        Title = "La cargaison",
                        Location = "Couloir sécurisé / rampe du Starlight Voyager",
                        Body = "La porte s'ouvre — par autorisation, compromis ou force. Dans la lumière blanche, un enfant attaché à un brancard relève les yeux. " +
                               "Les lampes du hangar vacillent; Syn tourne la tête vers ses instruments.\n\n" +
                               "SYN — Anomalie de champ. Origine: passager.\n\n" +
                               "ERIKA, bas — Lucas.\n\n" +
                               "JOHN — Alors c'est lui qui choisira la suite.\n\n" +
                               "Au loin, Vardis est couvert d'éclairs silencieux.",
                        ContinueLabel = "Clore la scène 01",
                        EnterEffects = new List<ScenarioEffect>
                        {
                            new() { Type = ScenarioEffectType.AddFlag, Key = "lucas_revealed" },
                            new() { Type = ScenarioEffectType.AddJournal, Text = "Lucas a été révélé. La tempête sur Vardis a déjà commencé à répondre." }
                        }
                    }
                }
            };
        }

        private static ScenarioNode BuildHangarNode(string id, string title, string body, List<ScenarioObjective> objectives)
        {
            return new ScenarioNode
            {
                Id = id,
                Kind = ScenarioNodeKind.Objective,
                Title = title,
                Location = "Hangar de la base-astéroïde",
                Body = body,
                ContinueLabel = "Ouvrir le couloir sécurisé",
                NextNodeId = "reveal_lucas",
                Objectives = objectives
            };
        }

        private static ScenarioObjective Objective(string id, string label, string detail)
        {
            return new ScenarioObjective { Id = id, Label = label, Detail = detail };
        }
    }
}
