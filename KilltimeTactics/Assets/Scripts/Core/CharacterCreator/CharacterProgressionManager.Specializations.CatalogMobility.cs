using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    public partial class CharacterProgressionManager
    {
        // CharacterProgressionManager.Specializations.CatalogMobility.cs — Catalogue mobilite/survie : Pas Invisibles, Elan, Esquive, Athletisme, encaissement, immunite.

        private static void RegisterMobilityCatalog()
        {
            RegisterSpec(SkillType.MagiePrimale, "Protocole des Pas Invisibles", null, false, false,
                "Dissipation mathématique des ondes de choc au sol (Phase 1 — Atavisme).",
                "Le premier déplacement de chaque tour coûte 0 PA au lieu de 1.");
            RegisterSpec(SkillType.MagiePrimale, "Protocole des Pas Invisibles : Dissipation d'Échappement", "Protocole des Pas Invisibles", false, false,
                "Extension de la foulée furtive.",
                "Le premier déplacement à 0 PA s'étend jusqu'à 2 cases au lieu d'une.");
            RegisterSpec(SkillType.MagiePrimale, "Protocole des Pas Invisibles : Pas Fantôme", "Protocole des Pas Invisibles : Dissipation d'Échappement", false, false,
                "Translation sans masse inertielle.",
                "Le premier déplacement s'étend à 3 cases sans consommer de PA.");
            RegisterSpec(SkillType.MagiePrimale, "Protocole des Pas Invisibles : Célérité Quantique", "Protocole des Pas Invisibles : Pas Fantôme", true, false,
                "Glissement quantique à travers l'espace de combat.",
                "Le premier déplacement s'étend à 4 cases et permet de traverser librement les cases occupées par des ennemis.");

            RegisterSpec(SkillType.MagiePrimale, "Élan Sans Drag", null, false, false,
                "Fluidité hydrodynamique absolue en mouvement (Phase 1).",
                "Ignore les pénalités de terrain difficile (Ralenti, boue, gravats). Vitesse accrue à 5.5 m/s.");
            RegisterSpec(SkillType.MagiePrimale, "Élan Sans Drag : Friction Zéro", "Élan Sans Drag", false, false,
                "Glisse parfaite sur le sol tactique.",
                "La vitesse de déplacement atteint 7.0 m/s et immunise aux dégâts de chute.");
            RegisterSpec(SkillType.MagiePrimale, "Élan Sans Drag : Glissade Balistique", "Élan Sans Drag : Friction Zéro", false, false,
                "Tir d'appui pendant la course.",
                "Permet d'effectuer un tir ou une attaque pendant un déplacement sans interrompre la course.");
            RegisterSpec(SkillType.MagiePrimale, "Élan Sans Drag : Évasion Inertielle", "Élan Sans Drag : Glissade Balistique", true, false,
                "Conservation cinétique totale.",
                "Vitesse portée à 8.5 m/s et immunité absolue aux zones d'acide, de ronces et de flammes.");

            RegisterSpec(SkillType.MagiePrimale, "Décélération Contrôlée", null, false, false,
                "Gestion instantanée des vecteurs d'inertie négative (Phase 1 — Atavisme).",
                "[Phase 1 : Vecteur] Élimine le risque de fracture fémorale et les traumatismes cinétiques de freinage brusque. Immunité aux dégâts de choc contre parois.");
            RegisterSpec(SkillType.MagiePrimale, "Décélération Contrôlée : Freinage Magnétique", "Décélération Contrôlée", false, false,
                "Inversion polaire instantanée des appuis.",
                "Permet d'effectuer un virage à 180° sans perte de vitesse et octroie +1 PA réflexe après une course.");
            RegisterSpec(SkillType.MagiePrimale, "Décélération Contrôlée : Ancrage Cinétique Absolu", "Décélération Contrôlée : Freinage Magnétique", true, false,
                "Verrouillage gravitationnel plantaire.",
                "Immunité totale au refoulement et à l'altération Déstabilisé consécutive à une projection.");

            RegisterSpec(SkillType.MagiePrimale, "Réflexes Myotatiques", null, false, false,
                "Arc réflexe proprioceptif ultra-rapide (Phase 1 — Atavisme).",
                "[Phase 1 : Trajectoire] Confère +1 palier de dé effectif permanent sur tous les jets d'Esquive active.");
            RegisterSpec(SkillType.MagiePrimale, "Réflexes Myotatiques : Synapses Hyper-Accélérées", "Réflexes Myotatiques", false, false,
                "Anticipation myologique réflexe.",
                "Confère +2 en Initiative réflexe et +1 supplémentaire sur tout jet de défense opposée.");

            RegisterSpec(SkillType.MagiePrimale, "Roulade de Décrochage", null, false, false,
                "Évitement avec translation spatiale rapide.",
                "Sur esquive réussie avec Delta >= 2, accorde 1 case de recul gratuit sans consommer de PA.");
            RegisterSpec(SkillType.Esquive, "Roulade de Décrochage : Reprise d'Appui", "Roulade de Décrochage", false, false,
                "Rebond immédiat après le décrochage.",
                "Accorde +1 PA de réserve pour le tour suivant après une esquive parfaite.");
            RegisterSpec(SkillType.Esquive, "Roulade de Décrochage : Ombre Évasive", "Roulade de Décrochage : Reprise d'Appui", false, false,
                "Disparition des lignes de mire directes.",
                "L'esquive réussie empêche les ennemis de cibler le personnage jusqu'à son prochain tour.");
            RegisterSpec(SkillType.Esquive, "Roulade de Décrochage : Pas Déphasé", "Roulade de Décrochage : Ombre Évasive", true, false,
                "Translation temporelle d'évitement.",
                "Une esquive réussie téléporte instantanément le personnage de 2 cases dans une direction au choix.");
            RegisterSpec(SkillType.Esquive, "Roulade de Décrochage : Esquive Réactive", "Roulade de Décrochage", false, false,
                "Souplesse réflexe sous altération.",
                "Le coût d'esquive en réaction ne peut être doublé par l'altération Ralenti.");
            RegisterSpec(SkillType.Esquive, "Roulade de Décrochage : Contre-Poussée", "Roulade de Décrochage : Esquive Réactive", false, false,
                "Dévier le centre de gravité adverse.",
                "Repousse l'assaillant de 1 case après une esquive réussie au contact.");

            RegisterSpec(SkillType.Esquive, "Acrobatie d'Évitement", null, false, false,
                "Voltige de combat et franchissement aérien.",
                "Permet de franchir les tranchées et demi-couverts sans dépense de PA additionnelle.");
            RegisterSpec(SkillType.Esquive, "Acrobatie d'Évitement : Saut Périlleux", "Acrobatie d'Évitement", false, false,
                "Échappée par les hauteurs.",
                "Permet de monter sur les barricades ou obstacles mi-hauteur sans coût de mouvement.");
            RegisterSpec(SkillType.Esquive, "Acrobatie d'Évitement : Vrille Balistique", "Acrobatie d'Évitement : Saut Périlleux", false, false,
                "Esquive des tirs en trajectoire hélicoïdale.",
                "Confère +2 en esquive contre toutes les attaques de fusils et de grenades.");

            RegisterSpec(SkillType.Athletisme, "Course d'Endurance", null, false, false,
                "Sprint, fond et gestion du souffle en mouvement.",
                "Le premier déplacement de chaque tour coûte 1 PA de moins (minimum 0).");
            RegisterSpec(SkillType.Athletisme, "Course d'Endurance : Second Souffle", "Course d'Endurance", false, false,
                "Récupération de fond du coureur.",
                "L'action Reprendre son Souffle efface 2 points d'essoufflement au lieu d'1.");
            RegisterSpec(SkillType.Athletisme, "Course d'Endurance : Cœur de Marathon", "Course d'Endurance : Second Souffle", true, false,
                "Pompe de marathonien.",
                "Le Souffle d'Urgence fournit +3 PA immédiats au lieu de +2.");
            RegisterSpec(SkillType.Athletisme, "Franchissement", null, false, false,
                "Escalade, nage de combat et progression en terrain hostile.",
                "Ignore les pénalités de terrain difficile (boue, gravats, pentes) et les coûts de franchissement.");
            RegisterSpec(SkillType.Athletisme, "Franchissement : Escalade Assurée", "Franchissement", false, false,
                "Murs, façades et cordes.",
                "Franchit les tranchées, murets et obstacles mi-hauteur sans dépense de PA additionnelle.");
            RegisterSpec(SkillType.Athletisme, "Franchissement : Nage de Combat", "Franchissement : Escalade Assurée", false, false,
                "Apnée et progression palmée.",
                "Aucune épreuve requise en eau agitée ; retient son souffle pendant 2 × Constitution en tours.");

            RegisterSpec(SkillType.DefenseCorporelle, "Bouclier Vivant", null, false, false,
                "Protection des équipiers au détriment de sa propre chair.",
                "Permet d'interposer son corps pour intercepter un tir ciblant un allié adjacent.");
            RegisterSpec(SkillType.DefenseCorporelle, "Bouclier Vivant : Interposition Réflexe", "Bouclier Vivant", false, false,
                "Interception sans temps de latence.",
                "L'interposition coûte 1 PA au lieu de 2.");
            RegisterSpec(SkillType.DefenseCorporelle, "Bouclier Vivant : Dévier le Calibre", "Bouclier Vivant : Interposition Réflexe", false, false,
                "Déviation oblique des munitions.",
                "Réduit de moitié les dégâts des tirs interceptés.");
            RegisterSpec(SkillType.DefenseCorporelle, "Bouclier Vivant : Mur Inébranlable", "Bouclier Vivant : Dévier le Calibre", true, false,
                "Ancrage tellurique face aux déflagrations.",
                "Encaisse les explosions de grenades sans subir de refoulement ni d'altération Déstabilisé.");

            RegisterSpec(SkillType.MagiePrimale, "Fibres Résilientes", null, false, false,
                "Densification arcanique des tissus cellulaires (Phase 1 — Atavisme).",
                "Le seuil d'encaissement de choc traumatique passe de CON x 2 à (CON x 2) + 2.");
            RegisterSpec(SkillType.MagiePrimale, "Fibres Résilientes : Tissu Densifié", "Fibres Résilientes", false, false,
                "Matrice cellulaire blindée.",
                "Le seuil d'encaissement est augmenté de +4 au lieu de +2.");
            RegisterSpec(SkillType.MagiePrimale, "Fibres Résilientes : Carapace Sous-Cutanée", "Fibres Résilientes : Tissu Densifié", false, false,
                "Tissage conjonctif ultra-dense.",
                "Le seuil d'encaissement est augmenté de +6 par rapport à la valeur nominale.");
            RegisterSpec(SkillType.MagiePrimale, "Fibres Résilientes : Chair d'Inflexible", "Fibres Résilientes : Carapace Sous-Cutanée", true, false,
                "Physiologie invincible aux traumatismes ouverts.",
                "Le seuil d'encaissement est augmenté de +8 et le personnage gagne +1 point d'armure naturelle permanente.");

            RegisterSpec(SkillType.MagiePrimale, "Encaissement Élastique", null, false, false,
                "Déformation viscoélastique des tissus conjonctifs (Phase 1 — Atavisme).",
                "[Phase 1 : Matrice] Absorption des micro-fractures cinétiques : augmente le seuil d'encaissement de +2 et accorde +1 Armure d'encaissement passive.");
            RegisterSpec(SkillType.MagiePrimale, "Encaissement Élastique : Tissu Myo-Amortisseur", "Encaissement Élastique", false, false,
                "Réseau amortisseur myofibrillaire.",
                "Le seuil d'encaissement augmente de +4 additionnels et absorbe 2 dégâts bruts sur tout coup critique subi.");
            RegisterSpec(SkillType.MagiePrimale, "Encaissement Élastique : Dissipation Plastique", "Encaissement Élastique : Tissu Myo-Amortisseur", true, false,
                "Dissipation intégrale des vibrations internes.",
                "Immunité complète au statut Sonné provoqué par une collision ou un projectile lourd.");

            RegisterSpec(SkillType.MagiePrimale, "Régénération Métabolique", null, false, false,
                "Mitose réparatrice continue des tissus (Phase 1 — Atavisme).",
                "[Phase 1 : Homéostasie] Régénère automatiquement 1 PV au début de chaque tour et cautérise immédiatement l'état Saignement.");

            RegisterSpec(SkillType.MagiePrimale, "Homéostasie Accélérée", null, false, false,
                "Mitose réparatrice continue des tissus (Soma).",
                "Régénère automatiquement 1 PV au début de chaque tour et cautérise le Saignement.");
            RegisterSpec(SkillType.MagiePrimale, "Homéostasie Accélérée : Régénération Cellulaire Avancée", "Homéostasie Accélérée", false, false,
                "Poussée régénératrice d'élite.",
                "Régénère 2 PV par tour et purge également le statut Empoisonné.");
            RegisterSpec(SkillType.MagiePrimale, "Homéostasie Accélérée : Coagulation Flash", "Homéostasie Accélérée : Régénération Cellulaire Avancée", false, false,
                "Cicatrisation instantanée des brûlures.",
                "Régénère 3 PV par tour et purge immédiatement le statut En Feu.");
            RegisterSpec(SkillType.MagiePrimale, "Homéostasie Accélérée : Pulsion Phénix", "Homéostasie Accélérée : Coagulation Flash", true, false,
                "Survie cellulaire absolue.",
                "Régénère 4 PV par tour et ressuscite automatiquement à 1 PV une fois par combat si terrassé.");

            RegisterSpec(SkillType.MagiePrimale, "Seconde Respiration", null, false, false,
                "Ventilation cellulaire d'urgence (Soma).",
                "1 fois par combat : l'action de reprise de souffle est gratuite (0 PA) et efface 2 points d'essoufflement.");
            RegisterSpec(SkillType.Cardio, "Seconde Respiration : Rechargement Anaérobie", "Seconde Respiration", false, false,
                "Conversion de l'acide lactique en énergie.",
                "Efface 3 points d'essoufflement au lieu de 2 lors de la reprise de souffle.");
            RegisterSpec(SkillType.Cardio, "Seconde Respiration : Poumons d'Acier", "Seconde Respiration : Rechargement Anaérobie", false, false,
                "Hyperventilation oxygénée.",
                "Efface 4 points d'essoufflement et octroie +1 PA immédiat.");
            RegisterSpec(SkillType.Cardio, "Seconde Respiration : Coeur de Berserker", "Seconde Respiration : Poumons d'Acier", true, false,
                "Pompage d'adrénaline continu.",
                "Utilisable 2 fois par combat et confère l'immunité au statut Ralenti.");

            RegisterSpec(SkillType.Cardio, "Poussée Cardiovasculaire", null, false, false,
                "Sur-régime cardiaque volontaire pour forcer des actions d'urgence.",
                "Permet de forcer 1 PA d'action en prenant un point d'essoufflement.");

            RegisterSpec(SkillType.EndurancePhysique, "Condition de Fer", null, false, false,
                "Marches forcées, veilles et privations encaissées.",
                "Régénère 1 PV au début de chaque tour personnel si blessé et conscient.");
            RegisterSpec(SkillType.EndurancePhysique, "Condition de Fer : Dur à Cuire", "Condition de Fer", false, false,
                "Encaisse sans broncher.",
                "Le seuil d'encaissement de choc traumatique est augmenté de +2.");
            RegisterSpec(SkillType.EndurancePhysique, "Condition de Fer : Mur de Chair", "Condition de Fer : Dur à Cuire", true, false,
                "Rempart vivant de la ligne.",
                "Le seuil d'encaissement est augmenté de +2 et confère +1 point d'armure naturelle permanente.");
            RegisterSpec(SkillType.EndurancePhysique, "Trempe de Fer", null, false, false,
                "Douleur, peur et privations apprivoisées.",
                "Le malus de Déstabilisé passe de -2 à -1 sur tous les jets.");
            RegisterSpec(SkillType.EndurancePhysique, "Trempe de Fer : Ignorer la Douleur", "Trempe de Fer", false, false,
                "Blessures graves mises en sourdine.",
                "Le malus d'À Terre en attaque (-1) est ignoré.");
            RegisterSpec(SkillType.EndurancePhysique, "Trempe de Fer : Esprit de Granit", "Trempe de Fer : Ignorer la Douleur", false, false,
                "Mental de roc sous le choc.",
                "Le malus d'Étourdi (-1) est ignoré sur tous les jets.");
            RegisterSpec(SkillType.Cardio, "Poussée Cardiovasculaire : Redline Stabilisé", "Poussée Cardiovasculaire", false, false,
                "Régulation de la surchauffe métabolique.",
                "Atteindre le plafond d'essoufflement n'applique plus le statut Ralenti.");
            RegisterSpec(SkillType.Cardio, "Poussée Cardiovasculaire : Injection d'Adrénaline", "Poussée Cardiovasculaire : Redline Stabilisé", false, false,
                "Pic d'énergie décuplé.",
                "La poussée octroie +2 PA au lieu de +1 pour chaque point d'essoufflement engagé.");

            RegisterSpec(SkillType.SystemeImmunitaire, "Immunité Toxique", null, false, false,
                "Anticorps arcaniques et neutralisation des poisons.",
                "Confère l'immunité complète au statut Empoisonné.");
            RegisterSpec(SkillType.SystemeImmunitaire, "Immunité Toxique : Neutralisation des Poisons", "Immunité Toxique", false, false,
                "Filtrage biologique hépatique renforcé.",
                "Divise par deux les dégâts de toutes les toxines subies.");
            RegisterSpec(SkillType.SystemeImmunitaire, "Immunité Toxique : Filtrage des Gaz de Guerre", "Immunité Toxique : Neutralisation des Poisons", false, false,
                "Barrière bronchique imperméable.",
                "Immunité complète aux effets de grenades lacrymogènes et neurotoxiques.");
            RegisterSpec(SkillType.SystemeImmunitaire, "Immunité Toxique : Sang Antidote Universel", "Immunité Toxique : Filtrage des Gaz de Guerre", true, false,
                "Plasma biologique thérapeutique.",
                "Immunise tous les alliés situés à moins de 2 cases contre les gaz et toxines.");

        }
    }
}
