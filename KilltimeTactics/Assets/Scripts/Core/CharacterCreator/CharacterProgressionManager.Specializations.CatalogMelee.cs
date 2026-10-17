using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    public partial class CharacterProgressionManager
    {
        // CharacterProgressionManager.Specializations.CatalogMelee.cs — Catalogue melee : MainsNues, ManiementArmes, ArmesPercantes, Ballistique.

        private static void RegisterMeleeCatalog()
        {
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux", null, false, false,
                "Maîtrise complète du corps-à-corps à mains nues et pieds.",
                "Parade sans malus non-exclusif. Immunité à la contrainte Canon Entravé. Refoulement (1 case) sur choc traumatique ou Delta >= 4.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Enchaînement Fluide", "Arts Martiaux", false, false,
                "Transition réflexe après un impact au corps-à-corps.",
                "La seconde attaque de mêlée du tour bénéficie d'une réduction de 1 PA.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Frappe Téléportée", "Arts Martiaux : Enchaînement Fluide", false, false,
                "Bond cinétique instantané vers la cible.",
                "Permet un déplacement gratuit d'une case vers l'adversaire avant la frappe si PA >= 2.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Déphasage Cinétique", "Arts Martiaux : Frappe Téléportée", true, false,
                "Déphasage transdimensionnel à l'impact.",
                "Sur coup critique, ignore totalement la parade et les contre-attaques adverses.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Onde Tellurique", "Arts Martiaux", false, false,
                "Projection d'onde de choc résiduelle.",
                "Sur coup critique à mains nues, inflige l'état Déstabilisé à tous les ennemis adjacents.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Brise-Blindage", "Arts Martiaux : Onde Tellurique", false, false,
                "Impact focalisé destructeur d'alliages.",
                "Les attaques à mains nues ignorent 2 points d'armure physique.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Paume de Brum'korath", "Arts Martiaux : Brise-Blindage", true, false,
                "Frappe martiale ancestrale du Temple englouti.",
                "Sur coup critique, inflige un Choc Traumatique instantané et projette la cible de 3 cases.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Balayage Rotatif", "Arts Martiaux", false, false,
                "Balayage circulaire des appuis adverses.",
                "Sur différentiel net Delta >= 2, fait chuter la cible À Terre.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Clé d'Articulation", "Arts Martiaux : Balayage Rotatif", false, false,
                "Prise de soumission au contact.",
                "Au contact, applique Immobilisé et inflige 3 dégâts bruts sans réduction d'armure.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Rupture Ligamentaire", "Arts Martiaux : Clé d'Articulation", true, false,
                "Torsion destructrice des tendons.",
                "Applique l'état Paralysé pendant 1 tour si la cible est déjà Déstabilisée.");

            // --- MAGIE PRIMORDIALE : PHASE 1 (VOLUME I — ATAVISME CINÉTIQUE & SOMA-OVERDRIVE) ---
            // NOTE : "Corps Augmenté" et sa résonance (innés héroïques) sont enregistrés
            // dans MinaCharacter.RegisterSpecializations() — voir fin de EnsureRegistryBuilt().

            RegisterSpec(SkillType.MagiePrimale, "Leviers Densifiés", null, false, false,
                "Renforcement osseux et focalisation de la pression (Phase 1).",
                "Les attaques de mêlée à mains nues ignorent 1 point d'armure physique (Perforant 1).");
            RegisterSpec(SkillType.MagiePrimale, "Leviers Densifiés : Perforation Osseuse", "Leviers Densifiés", false, false,
                "Pénétration osseuse chirurgicale.",
                "La perforation d'armure à mains nues passe à 2 points d'armure ignorés.");
            RegisterSpec(SkillType.MagiePrimale, "Leviers Densifiés : Pointe Cristalline", "Leviers Densifiés : Perforation Osseuse", true, false,
                "Densification nanométrique des phalanges.",
                "Ignore 3 points d'armure et convertit tous les dégâts en Dégâts Absolus.");
            RegisterSpec(SkillType.MagiePrimale, "Leviers Densifiés : Ancrage Myologique", "Leviers Densifiés", false, false,
                "Stabilité structurelle inébranlable.",
                "Immunise le porteur au refoulement et au statut À Terre face aux frappes de contact.");
            RegisterSpec(SkillType.MagiePrimale, "Leviers Densifiés : Poigne Titanesque", "Leviers Densifiés : Ancrage Myologique", false, false,
                "Prise musculaire indécrochable.",
                "Permet de désarmer automatiquement l'assaillant sur différentiel net Delta >= 3.");

            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture", null, false, false,
                "Coup de pied d'arrêt à transfert cinétique lourd (Phase 1 — Atavisme).",
                "Tout coup réussi (Delta >= 0) refoule la cible de 2 cases. Collision contre obstacle inflige 3 dégâts et [Sonné].");
            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture : Impact Dévastateur", "Teep de Rupture", false, false,
                "Amplification de la projection contre les parois.",
                "Les dégâts d'impact contre obstacle passent à 6 PV et appliquent également Déstabilisé pendant 2 tours.");
            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture : Onde de Choc Linéaire", "Teep de Rupture : Impact Dévastateur", false, false,
                "Propagation cinétique axiale.",
                "La distance de refoulement est portée à 3 cases.");
            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture : Brise-Châssis Titan", "Teep de Rupture : Onde de Choc Linéaire", true, false,
                "Impact démesuré destructeur de machines lourdes.",
                "Refoulement porté à 4 cases et désactivation immédiate des boucliers énergétiques touchés.");
            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture : Interception d'Assaut", "Teep de Rupture", false, false,
                "Réflexe d'arrêt d'élan entrant.",
                "Déclenche un Teep de Rupture automatique en réaction contre tout ennemi tentant d'entrer au contact direct.");
            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture : Rebond Mural", "Teep de Rupture : Interception d'Assaut", false, false,
                "Reprise d'appui après refoulement.",
                "Accorde 1 case de replacement gratuit sans coût de PA après avoir projeté une cible.");

            RegisterSpec(SkillType.MainsNues, "Perforation de Silicate", null, false, false,
                "Fissuration des blindages rocheux et synthétiques denses.",
                "Ignore 2 points d'armure et convertit les dégâts en Dégâts Absolus face aux carapaces de pierre.");
            RegisterSpec(SkillType.MainsNues, "Perforation de Silicate : Fracture Sismique", "Perforation de Silicate", false, false,
                "Onde de brisure souterraine.",
                "Ignore 3 points d'armure et détruit les couvertures rocheuses adjacentes à la cible.");
            RegisterSpec(SkillType.MainsNues, "Perforation de Silicate : Cœur d'Adamas", "Perforation de Silicate : Fracture Sismique", true, false,
                "Maîtrise ultime des pressions tectoniques.",
                "Ignore 4 points d'armure et inflige Sonné sur tout différentiel net Delta >= 2.");

            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée", null, false, false,
                "Escrime militaire lourde et précision du tranchant.",
                "Annule le malus de parade non-exclusive. Autorise la contre-attaque immédiate en réaction.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Riposte Éclair", "Maniement de l'Épée", false, false,
                "Contre-attaque sans perte d'assiette.",
                "La contre-attaque en réaction ne subit aucun malus de riposte.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Tranchant Tempétueux", "Maniement de l'Épée : Riposte Éclair", false, false,
                "Vitesse d'exécution tranchante.",
                "Confère +2 Dégâts bruts sur toute riposte réussie avec une lame.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Lame de Ligne Temporelle Zéro", "Maniement de l'Épée : Tranchant Tempétueux", true, false,
                "Synchronisation causale absolue de la garde (Ligne Temporelle Zéro).",
                "Sur réussite critique en parade, annule totalement l'attaque ennemie et inflige les dégâts complets de l'arme en retour.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Garde Haute Impériale", "Maniement de l'Épée", false, false,
                "Posture défensive protégeant le torse et la tête.",
                "Confère +2 au jet de parade contre les frappes lourdes et tirs ciblés.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Lame Miroir", "Maniement de l'Épée : Garde Haute Impériale", false, false,
                "Déviation des rayonnements thermiques.",
                "Permet de parer les tirs de fusils laser au contact sur différentiel net Delta >= 0.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Dôme de Parade", "Maniement de l'Épée : Lame Miroir", true, false,
                "Balisage cinétique protecteur d'escouade.",
                "Permet d'interposer sa lame pour parer une attaque ciblant un allié adjacent.");

            RegisterSpec(SkillType.ManiementArmes, "Fente de Rupture", null, false, false,
                "Frappe d'estoc perforante dans les défauts de blindage.",
                "Dépense +1 PA à l'assaut pour ignorer 2 points d'armure physique balistique ou métallique.");
            RegisterSpec(SkillType.ManiementArmes, "Fente de Rupture : Pointe Chirurgicale", "Fente de Rupture", false, false,
                "Guidage ultra-précis de la lame.",
                "Ignore 3 points d'armure au lieu de 2 lors d'une frappe ciblée.");
            RegisterSpec(SkillType.ManiementArmes, "Fente de Rupture : Transpercement Traversant", "Fente de Rupture : Pointe Chirurgicale", false, false,
                "Frappe perforant la première cible de part en part.",
                "Ignore 4 points d'armure et blesse également l'ennemi situé immédiatement derrière la cible.");
            RegisterSpec(SkillType.ManiementArmes, "Fente de Rupture : Fissure Causal", "Fente de Rupture : Transpercement Traversant", true, false,
                "Fente transperçant les strates temporelles.",
                "Inflige le statut Chrono-Fracture et draine instantanément 2 PA de la réserve adverse.");

            RegisterSpec(SkillType.ManiementArmes, "Bloquer", null, false, false,
                "Interposition de bouclier ou garde hermétique.",
                "Permet de bloquer sans malus de dé non-exclusif.");
            RegisterSpec(SkillType.ManiementArmes, "Bloquer : Mur de Bouclier", "Bloquer", false, false,
                "Absorption cinétique sur pavois.",
                "Absorbe 3 dégâts bruts supplémentaires lors d'un blocage actif.");
            RegisterSpec(SkillType.ManiementArmes, "Bloquer : Heurt de Bouclier", "Bloquer : Mur de Bouclier", false, false,
                "Frappe contondante en riposte directe.",
                "Après un blocage réussi, inflige 4 dégâts d'impact et repousse l'adversaire de 1 case.");
            RegisterSpec(SkillType.ManiementArmes, "Bloquer : Forteresse Impénétrable", "Bloquer : Heurt de Bouclier", true, false,
                "Bastion mécanique infranchissable.",
                "Immunité totale aux coups critiques et aux altérations Déstabilisé et À Terre pendant la garde.");

            RegisterSpec(SkillType.ManiementArmes, "Armes Contondantes", null, false, false,
                "Maîtrise des armes de choc : marteaux, masses, gourdins et fléaux.",
                "Annule le malus de parade non-exclusive au Maniement d'Arme. Prérequis du Marteau de Guerre.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre", "Armes Contondantes", false, false,
                "Impact tellurique écrasant.",
                "Sur succès net, la cible tombe À Terre, subit +2 Dégâts bruts et convertit les dégâts en Dégâts Absolus.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre : Écrasement Osseux", "Marteau de Guerre", false, false,
                "Broie les armures de plaques.",
                "Confère +3 Dégâts bruts additionnels contre toute cible portant une armure lourde.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre : Brise-Crâne", "Marteau de Guerre : Écrasement Osseux", false, false,
                "Visée haute commotrice.",
                "Une frappe ciblée à la Tête applique Sonné et Étourdi sans subir le malus de visée habituel.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre : Cataclysme de Fer", "Marteau de Guerre : Brise-Crâne", true, false,
                "Libération d'énergie de choc colossale.",
                "Confère +5 Dégâts bruts et projette une onde de choc renversant toutes les unités adjacentes.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre : Onde de Faille", "Marteau de Guerre", false, false,
                "Fissuration du sol par le marteau.",
                "Crée une zone de terrain difficile sur 2 cases autour du point d'impact.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre : Onde Sismique Réverbérante", "Marteau de Guerre : Onde de Faille", false, false,
                "Résonance acoustique destructrice.",
                "Applique le statut Déstabilisé à toute cible située dans un rayon de 2 cases.");

            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre", null, false, false,
                "Maîtrise des haches de bataille, cognées et bardiches.",
                "Annule le malus de parade non-exclusive au Maniement d'Arme. Sur succès net, inflige +2 Dégâts bruts.");
            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre : Fente du Bûcheron", "Hache de Guerre", false, false,
                "Abattage en puissance dans les plaques.",
                "Confère +3 Dégâts bruts additionnels contre toute cible portant une armure lourde.");
            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre : Brise-Garde", "Hache de Guerre : Fente du Bûcheron", false, false,
                "Crochet ascendant sous la garde adverse.",
                "Confère +4 Dégâts bruts et fait chuter la cible À Terre sur différentiel net Delta >= 2.");
            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre : Exécution du Bourreau", "Hache de Guerre : Brise-Garde", true, false,
                "Coup de billot sans appel.",
                "Confère +5 Dégâts bruts et convertit les dégâts en Dégâts Absolus sur coup critique.");
            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre : Croc de Désarmement", "Hache de Guerre", false, false,
                "Crochet du bec de la hache dans la garde adverse.",
                "Désarme la cible sur différentiel net Delta >= 3.");
            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre : Arrache-Bouclier", "Hache de Guerre : Croc de Désarmement", false, false,
                "Arrachement brutal des protections.",
                "Une parade adverse réussie de justesse (différentiel -1 ou 0) est convertie en touche franche.");

            RegisterSpec(SkillType.ArmesPercantes, "Escrime", null, false, false,
                "Jeu de lame rapide et précis à la rapière ou dague.",
                "Annule la punition de visée chirurgicale vers les membres vitaux légers. Parade sans malus non-exclusif.");
            RegisterSpec(SkillType.ArmesPercantes, "Escrime : Riposte à l'Estoc", "Escrime", false, false,
                "Riposte réflexe immédiate après parade.",
                "Autorise une contre-attaque immédiate pour 1 PA de réaction.");
            RegisterSpec(SkillType.ArmesPercantes, "Escrime : Fleuret Éclair", "Escrime : Riposte à l'Estoc", false, false,
                "Agilité de pointe dans le duel.",
                "Confère +2 en initiative réflexe et autorise une parade sans dépense de PA une fois par tour.");
            RegisterSpec(SkillType.ArmesPercantes, "Escrime : Frappe Fantôme", "Escrime : Fleuret Éclair", true, false,
                "Désaxement visuel imperceptible.",
                "L'attaque ignore la défense active adverse si la cible a déjà consommé des PA ce tour.");
            RegisterSpec(SkillType.ArmesPercantes, "Escrime : Désarmement Fleuret", "Escrime", false, false,
                "Torsion de garde métallique.",
                "Désarme la cible sur différentiel net Delta >= 2.");
            RegisterSpec(SkillType.ArmesPercantes, "Escrime : Entaille Précise", "Escrime : Désarmement Fleuret", false, false,
                "Section des tendons de préhension.",
                "Applique un malus de -2 à toutes les attaques de la cible pendant 2 tours.");

            RegisterSpec(SkillType.ArmesPercantes, "Frappe Jugulaire", null, false, false,
                "Visée chirurgicale de la gorge et des carotides.",
                "Sur Delta >= 3, déclenche l'état Saignement (dégâts résiduels par tour égaux aux entraînements).");
            RegisterSpec(SkillType.ArmesPercantes, "Frappe Jugulaire : Hémorragie Profonde", "Frappe Jugulaire", false, false,
                "Lacération artérielle majeure.",
                "Les dégâts résiduels de Saignement sont doublés chaque tour.");
            RegisterSpec(SkillType.ArmesPercantes, "Frappe Jugulaire : Asphyxie Sanguine", "Frappe Jugulaire : Hémorragie Profonde", false, false,
                "Obstruction des voies respiratoires.",
                "Applique l'état Asphyxie en plus du Saignement, drainant 1 PA de réserve par tour.");
            RegisterSpec(SkillType.ArmesPercantes, "Frappe Jugulaire : Tranche-Artère Silencieux", "Frappe Jugulaire : Asphyxie Sanguine", true, false,
                "Assassinat clinique d'infiltrateur.",
                "Si les dégâts nets dépassent l'encaissement, provoque la mort instantanée sans jet de survie.");

            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast", null, false, false,
                "Maîtrise des piques, hallebardes et lances d'arrêt.",
                "Les attaques à 2 cases d'allonge ne subissent aucun malus. Prérequis des techniques de hast.");
            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast : Arrêt de Charge", "Arme de Hast", false, false,
                "Pique plantée face à l'assaut.",
                "La pointe trouve les défauts : ignore 2 points d'armure physique balistique ou métallique.");
            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast : Mur de Piques", "Arme de Hast : Arrêt de Charge", false, false,
                "Hérisson d'acier discipliné.",
                "Ignore 3 points d'armure au lieu de 2 et repousse l'assaillant d'1 case sur succès net.");
            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast : Phalange d'Acier", "Arme de Hast : Mur de Piques", true, false,
                "Sarre de la garde impériale.",
                "Ignore 4 points d'armure et blesse également l'ennemi situé immédiatement derrière la cible.");
            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast : Fauchage Latéral", "Arme de Hast", false, false,
                "Balayage des appuis à pleine allonge.",
                "Sur différentiel net Delta >= 2, fait chuter la cible À Terre.");
            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast : Crochet de Hallebarde", "Arme de Hast : Fauchage Latéral", false, false,
                "Croc du bec de faucon dans les étriers et les gardes.",
                "Désarme la cible ou la tire à terre sur différentiel net Delta >= 2.");

            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide", null, false, false,
                "Cadence réflexe à une main en tir rapproché.",
                "Sur réussite nette, applique Ralenti. Sur 5 ou 6 brut, fait chuter la cible À Terre.");
            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide : Dégainé Réflexe", "Pistolet & Tir Rapide", false, false,
                "Tir instinctif dès la première seconde d'engagement.",
                "Le premier tir de pistolet de chaque tour coûte 1 PA de moins (minimum 1 PA).");
            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide : Akimbo Assaut", "Pistolet & Tir Rapide : Dégainé Réflexe", false, false,
                "Tir simultané avec deux armes de poing.",
                "Permet de faire feu avec deux pistolets pour 3 PA totaux sur la même cible.");
            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide : Trait d'Orichalque", "Pistolet & Tir Rapide : Akimbo Assaut", true, false,
                "Balistique énergétique surcadencée.",
                "Les tirs de pistolet ignorent le couvert partiel et transpercent jusqu'à 2 cibles alignées.");
            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide : Balle dans le Genou", "Pistolet & Tir Rapide", false, false,
                "Tir ciblé d'amputation de mobilité.",
                "Visée chirurgicale des Jambes sans malus de visée, infligeant l'état Ralenti garanti.");
            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide : Tir de Neutralisation", "Pistolet & Tir Rapide : Balle dans le Genou", false, false,
                "Désarmement à distance.",
                "Visée chirurgicale du Bras Droit désarme la cible sur tout différentiel net Delta >= 1.");

            RegisterSpec(SkillType.Ballistique, "Fusil de Précision", null, false, false,
                "Tir longue portée et perforation à fort grossissement.",
                "Portée accrue de 4 cases et bonus de critique augmenté.");
            RegisterSpec(SkillType.Ballistique, "Fusil de Précision : Visée Millimétrique", "Fusil de Précision", false, false,
                "Stabilisation respiratoire du tireur.",
                "Confère +2 au jet d'attaque chirurgicale si aucun déplacement n'a été effectué ce tour.");
            RegisterSpec(SkillType.Ballistique, "Fusil de Précision : Perforation Hyper-Véloce", "Fusil de Précision : Visée Millimétrique", false, false,
                "Munitions au noyau de tungstène lourd.",
                "Ignore jusqu'à 4 points d'armure balistique ou métallique.");
            RegisterSpec(SkillType.Ballistique, "Fusil de Précision : Tir à Travers les Parois", "Fusil de Précision : Perforation Hyper-Véloce", true, false,
                "Capteurs optiques à rayons X et faisceau perforant.",
                "Permet de cibler une unité située derrière un couvert total avec seulement -2 de malus au jet.");
            RegisterSpec(SkillType.Ballistique, "Fusil de Précision : Tir de Suppression", "Fusil de Précision", false, false,
                "Tir forçant l'ennemi à se terrer.",
                "Applique Déstabilisé et draine 1 PA de réserve défensive sur la cible.");
            RegisterSpec(SkillType.Ballistique, "Fusil de Précision : Calibre Anti-Matériel", "Fusil de Précision : Tir de Suppression", false, false,
                "Frappe perforante anti-véhicules.",
                "Confère +4 Dégâts bruts contre les tourelles, droïdes et structures fortifiées.");

            RegisterSpec(SkillType.Ballistique, "Grenadier d'Assaut", null, false, false,
                "Spécialisation dans les explosifs et lanceurs de zone.",
                "Dispersion réduite et portée de lancer étendue de 2 cases.");
            RegisterSpec(SkillType.Ballistique, "Grenadier d'Assaut : Calcul Balistique", "Grenadier d'Assaut", false, false,
                "Correction de trajectoire parabolique.",
                "Réduit la dispersion des grenades d'une case supplémentaire (dispersion minimale 0).");
            RegisterSpec(SkillType.Ballistique, "Grenadier d'Assaut : Dégoupillage Éclair", "Grenadier d'Assaut : Calcul Balistique", false, false,
                "Préparation immédiate de l'explosif.",
                "Lancer une grenade coûte 1 PA de moins (1 PA à la main, 2 PA au lanceur).");
            RegisterSpec(SkillType.Ballistique, "Grenadier d'Assaut : Souffle Thermobarique", "Grenadier d'Assaut : Dégoupillage Éclair", true, false,
                "Amorçage de dépression atmosphérique.",
                "Étend le rayon d'effet de souffle de +1 case pour toutes les grenades lancées.");

            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc", null, false, false,
                "Maîtrise des arcs composites, arcs longs et arbalètes.",
                "Tirs silencieux : ne révèlent pas la position du tireur. Portée accrue de 2 cases.");
            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc : Flèche Perforante", "Tir à l'Arc", false, false,
                "Pointe bodkin contre les blindages.",
                "Ignore jusqu'à 2 points d'armure balistique ou métallique.");
            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc : Tir en Cloche", "Tir à l'Arc : Flèche Perforante", false, false,
                "Trajectoire parabolique par-dessus les couverts.",
                "Ignore le couvert partiel et étend la portée de 4 cases supplémentaires.");
            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc : Pluie d'Acier", "Tir à l'Arc : Tir en Cloche", true, false,
                "Volée de saturation.",
                "Une attaque réussie blesse également une seconde cible adjacente à la première.");
            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc : Tir Instinctif", "Tir à l'Arc", false, false,
                "Décoche sans visée, au jugé.",
                "Le premier tir à l'arc de chaque tour coûte 1 PA de moins (minimum 1 PA).");
            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc : Chasseur Silencieux", "Tir à l'Arc : Tir Instinctif", false, false,
                "Fantôme des bois d'Hybris.",
                "Abattre une cible isolée ne déclenche aucune alerte ; +2 en Discrétion après un tir réussi.");

        }
    }
}
