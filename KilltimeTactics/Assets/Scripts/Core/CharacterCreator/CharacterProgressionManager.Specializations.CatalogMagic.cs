using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    public partial class CharacterProgressionManager
    {
        // CharacterProgressionManager.Specializations.CatalogMagic.cs — Catalogue magie (generique + Volume II) et delegation des 6 fiches heroiques.

        private static void RegisterMagicCatalog()
        {
            // --- Arborescence Primordiale Standard (Personnages par défaut) ---
            RegisterSpec(SkillType.MagiePrimale, "Barricade de Racines", null, false, true,
                "Mitose végétale accélérée de terrain.",
                "Fait jaillir instantanément un Couvert 3/4 sur un hexagone ciblé à 4 cases.");
            RegisterSpec(SkillType.MagiePrimale, "Barricade de Racines : Épines de Granit", "Barricade de Racines", false, true,
                "Croissance végétale agressive.",
                "La barricade inflige 3 dégâts de lacération à tout ennemi qui la franchit.");
            RegisterSpec(SkillType.MagiePrimale, "Barricade de Racines : Bastion Vivant", "Barricade de Racines : Épines de Granit", false, true,
                "Densification ligneuse armée.",
                "La barricade absorbe jusqu'à 25 PV de dégâts avant d'être détruite.");
            RegisterSpec(SkillType.MagiePrimale, "Barricade de Racines : Mur d'Orichalque Végétal", "Barricade de Racines : Bastion Vivant", true, true,
                "Forteresse végétale impénétrable.",
                "La barricade s'étend sur 3 cases contiguës et devient un Couvert Total indestructible.");

            RegisterSpec(SkillType.MagiePrimale, "Vignes Constrictrices", null, false, true,
                "Entrave végétale prédatrice.",
                "Applique l'état Immobilisé à une cible jusqu'à 4 cases.");
            RegisterSpec(SkillType.MagiePrimale, "Vignes Constrictrices : Étau Végétal", "Vignes Constrictrices", false, true,
                "Compression thoracique des lianes.",
                "Inflige le statut Asphyxie en plus d'Immobilisé, drainant 1 PE par tour.");
            RegisterSpec(SkillType.MagiePrimale, "Vignes Constrictrices : Moisson Sanguine", "Vignes Constrictrices : Étau Végétal", false, true,
                "Absorption chlorophyllienne vitale.",
                "Draine 3 PV par tour à la cible entravée et les transfère sous forme de soins au lanceur.");
            RegisterSpec(SkillType.MagiePrimale, "Vignes Constrictrices : Étranglement Toxique", "Vignes Constrictrices : Moisson Sanguine", true, true,
                "Constriction venimeuse mortelle.",
                "Applique les statuts Paralysé et Empoisonné permanent à la cible entravée.");

            RegisterSpec(SkillType.MagiePrimale, "Catalyse Tissulaire", null, false, true,
                "Toucher régénérateur d'allié.",
                "Touche un allié adjacent : soigne 5 PV et dissipe Déstabilisé et Étourdi.");
            RegisterSpec(SkillType.MagiePrimale, "Catalyse Tissulaire : Surcroît Vital", "Catalyse Tissulaire", false, true,
                "Irrigation cellulaire de pointe.",
                "Soigne 10 PV au lieu de 5 et purge également le statut Saignement.");
            RegisterSpec(SkillType.MagiePrimale, "Catalyse Tissulaire : Bouclier Végétal", "Catalyse Tissulaire : Surcroît Vital", false, true,
                "Écorce biologique protectrice.",
                "Accorde une armure naturelle d'écorce de +3 pendant 2 tours à l'allié soigné.");
            RegisterSpec(SkillType.MagiePrimale, "Catalyse Tissulaire : Transfusion Symbiotique", "Catalyse Tissulaire : Bouclier Végétal", true, true,
                "Transfert d'énergie régénératrice d'urgence.",
                "Ressuscite un allié tombé au contact avec 50% de ses PV maximaux.");

            RegisterSpec(SkillType.MagiePrimale, "Champ de Ronces", null, false, true,
                "Zone d'épines bioluminescentes.",
                "Zone de rayon 1 hexagone : applique Ralenti et 3 dégâts par case franchie.");
            RegisterSpec(SkillType.MagiePrimale, "Champ de Ronces : Ronces Neurotoxiques", "Champ de Ronces", false, true,
                "Venin végétal paralysant les muscles.",
                "Applique Empoisonné et Ralenti dans une zone de 2 cases.");
            RegisterSpec(SkillType.MagiePrimale, "Champ de Ronces : Enchevêtrement Explosif", "Champ de Ronces : Ronces Neurotoxiques", false, true,
                "Détonation végétale sur commande.",
                "Provoque l'explosion des ronces infligeant 8 dégâts de zone à toutes les unités présentes.");

            RegisterSpec(SkillType.MagiePrimale, "Greffe Cinétique", null, false, true,
                "Transmission d'élan à un allié au contact.",
                "Transfère son bonus de déplacement à un allié au contact (+2 PA mouvement).");
            RegisterSpec(SkillType.MagiePrimale, "Greffe Cinétique : Osmose d'Escouade", "Greffe Cinétique", false, true,
                "Réseau cinétique partagé.",
                "Transfère le bonus de déplacement à tous les alliés situés à moins de 2 cases.");
            RegisterSpec(SkillType.MagiePrimale, "Greffe Cinétique : Frappe Symbiotique", "Greffe Cinétique : Osmose d'Escouade", false, true,
                "Guidage arcanique d'impact.",
                "Permet à un allié adjacent d'exécuter son attaque avec le dé de Magie Primale du lanceur.");

            // --- EXTENSION EXCLUSIVE À MINA : voir MinaCharacter.RegisterSpecializations() ---
            // (Magie Primordiale profonde : Rempart de Silice, Palissade, Arbre-Citadelle,
            //  Vrilles, Épines Neurotoxiques, Lierre Causal, Régénération Osmotique,
            //  Cautérisation Sèveuse, Cœur d'Éclosion, Rosier Noir, Tapis, Jardin,
            //  Pont Végétal, Racines d'Accélération, Réseau Mycélien, Éveil Chlorophyllien...)

            RegisterSpec(SkillType.MagieElementale, "Incinération Pyrocinétique", null, false, false,
                "Combustion spontanée par excitation atomique.",
                "Projette un jet de flammes infligeant 6 dégâts de brûlure et l'état En Feu.");
            RegisterSpec(SkillType.MagieElementale, "Incinération : Flamme Bleue", "Incinération Pyrocinétique", false, false,
                "Plasma thermique pur.",
                "Les dégâts d'incinération ignorent totalement l'armure physique balistique.");
            RegisterSpec(SkillType.MagieElementale, "Incinération : Fournaise Déferlante", "Incinération : Flamme Bleue", false, false,
                "Expansion conique de la flamme.",
                "Le souffle embrase un cône de 3 hexagones adjacents.");
            RegisterSpec(SkillType.MagieElementale, "Incinération : Nova Thermique", "Incinération : Fournaise Déferlante", true, false,
                "Déflagration pyrocinétique suprême.",
                "Provoque une explosion thermique infligeant 12 dégâts absolus dans un rayon de 2 cases.");

            // --- MAGIE ÉLÉMENTALE : AIR, EAU, TERRE (GÉNÉRIQUE — MAGICIENS NORMAUX) ---
            // Le Feu est déjà couvert par l'Incinération Pyrocinétique. Ces trois branches
            // complètent le quatuor classique : vents cisaillants (contrôle), eaux lourdes
            // (entrave), pierre profonde (encaissement). Restrictions héroïques :
            // voir MinaCharacter.IsSkillForbidden / LucasCharacter (voie inversée exclusive).
            RegisterSpec(SkillType.MagieElementale, "Aéromancie", null, false, false,
                "Appel des vents cisaillants.",
                "Dépense 2 PA : rafale à 4 cases — refoule la cible d'1 case et lui impose -1 à sa prochaine attaque.");
            RegisterSpec(SkillType.MagieElementale, "Aéromancie : Lame de Vent", "Aéromancie", false, false,
                "Tranchant d'air supersonique.",
                "La rafale inflige 4 dégâts Absolus et ignore le couvert partiel.");
            RegisterSpec(SkillType.MagieElementale, "Aéromancie : Courant Porteur", "Aéromancie", false, false,
                "Vent ascendant d'escouade.",
                "Déplace un allié consentant d'1 case sans coût de PA ni attaque d'opportunité.");
            RegisterSpec(SkillType.MagieElementale, "Aéromancie : Tempête de Lames", "Aéromancie : Lame de Vent", true, false,
                "Ouragan de shrapnels aériens.",
                "Zone de 2 cases de rayon : 8 dégâts Absolus et cibles À Terre.");

            RegisterSpec(SkillType.MagieElementale, "Hydromancie", null, false, false,
                "Appel des eaux lourdes.",
                "Dépense 2 PA : jet à 4 cases — 3 dégâts Absolus et Ralenti pendant 1 tour.");
            RegisterSpec(SkillType.MagieElementale, "Hydromancie : Étreinte Abyssale", "Hydromancie", false, false,
                "Mâchoire d'eau glacée.",
                "Cible à 4 cases : Immobilisée pendant 1 tour (Delta >= 2 requis contre PJ/PNJ majeur).");
            RegisterSpec(SkillType.MagieElementale, "Hydromancie : Brume Aveuglante", "Hydromancie", false, false,
                "Brouillard opaque.",
                "Nappe sur 1 hexagone pendant 1 tour : -2 à toutes les attaques effectuées à travers.");
            RegisterSpec(SkillType.MagieElementale, "Hydromancie : Raz-de-Marée", "Hydromancie : Étreinte Abyssale", true, false,
                "Déferlante en ligne.",
                "Ligne de 3 cases : 8 dégâts Absolus et cibles À Terre.");

            RegisterSpec(SkillType.MagieElementale, "Géomancie", null, false, false,
                "Appel de la pierre profonde.",
                "Dépense 2 PA : secousse à 4 cases — Déstabilisé pendant 1 tour.");
            RegisterSpec(SkillType.MagieElementale, "Géomancie : Poing Tellurique", "Géomancie", false, false,
                "Uppercut de bedrock.",
                "Cible à 2 cases : 4 dégâts Absolus et À Terre sur Delta >= 2.");
            RegisterSpec(SkillType.MagieElementale, "Géomancie : Peau de Pierre", "Géomancie", false, false,
                "Cuirasse minérale.",
                "Soi ou allié au contact : +2 armure naturelle pendant 2 tours.");
            RegisterSpec(SkillType.MagieElementale, "Géomancie : Séisme Localisé", "Géomancie : Poing Tellurique", true, false,
                "Faille ouverte sous les appuis.",
                "Zone de 2 cases de rayon : 8 dégâts Absolus, cibles À Terre, terrain difficile pendant 2 tours.");

            RegisterSpec(SkillType.MagieEsprit, "Télékinésie", null, false, false,
                "Manipulation de vecteurs de force à distance.",
                "Permet de déplacer des objets ou projeter des ennemis à distance.");
            RegisterSpec(SkillType.MagieEsprit, "Télékinésie : Projection d'Objets", "Télékinésie", false, false,
                "Balistique de débris lourds.",
                "Projette un bloc ou caisse infligeant 6 dégâts contondants jusqu'à 6 cases.");
            RegisterSpec(SkillType.MagieEsprit, "Télékinésie : Barrière Cinétique", "Télékinésie : Projection d'Objets", false, false,
                "Bouclier télékinétique déflecteur.",
                "Arrête net tous les projectiles physiques et grenades pendant 1 tour.");
            RegisterSpec(SkillType.MagieEsprit, "Télékinésie : Fissure Gravifique", "Télékinésie : Barrière Cinétique", true, false,
                "Singularité gravitationnelle concentrée.",
                "Crée un puits de gravité attirant tous les ennemis dans un rayon de 3 cases et les projetant À Terre.");

            // --- MAGIE DE L'ESPRIT : TÉLÉPATHIE & CLAIRVOYANCE (GÉNÉRIQUE — MAGICIENS NORMAUX) ---
            // Voie accessible à tout éveillé à l'Esprit (héros, PNJ). Volontairement en deçà de
            // l'Architecture Neurale héroïque (6 stades, réécriture, possession — voir LucasCharacter) :
            // ici, écoute, lien et prescience — jamais de contrôle ni de réécriture.
            // Restrictions héroïques : voir MinaCharacter.IsSkillForbidden.
            RegisterSpec(SkillType.MagieEsprit, "Télépathie", null, false, false,
                "Écoute empathique des esprits proches.",
                "Dépense 2 PA : établit un lien silencieux avec un allié consentant à 6 cases pendant 1 tour et confère +1 en Intuition contre les mensonges.");
            RegisterSpec(SkillType.MagieEsprit, "Télépathie : Lien Empathique", "Télépathie", false, false,
                "Partage du fardeau émotionnel.",
                "Dépense 2 PA : purge l'état Déstabilisé d'un allié à 3 cases et lui rend 1 PA de réserve.");
            RegisterSpec(SkillType.MagieEsprit, "Télépathie : Sondage de Surface", "Télépathie : Lien Empathique", false, false,
                "Lecture des pensées de surface.",
                "Lit les intentions immédiates d'une cible consentante ou d'un sbire (Delta >= 2 requis contre PJ/PNJ majeur).");
            RegisterSpec(SkillType.MagieEsprit, "Télépathie : Injonction Impérieuse", "Télépathie : Sondage de Surface", true, false,
                "Ordre bref irrésistible.",
                "Impose à un sbire un ordre simple (lâcher son arme, fuir, s'immobiliser 1 tour). Contre un PJ/PNJ majeur, requiert Delta >= 3.");

            RegisterSpec(SkillType.MagieEsprit, "Clairvoyance", null, false, false,
                "Perception à travers les obstacles.",
                "Dépense 2 PA : perçoit à travers une paroi légère et gagne +2 en Observation pendant 1 tour.");
            RegisterSpec(SkillType.MagieEsprit, "Clairvoyance : Œil Distant", "Clairvoyance", false, false,
                "Projection sensorielle à distance.",
                "Projette sa perception jusqu'à 6 cases sans ligne de vue (immobile, 1 tour).");
            RegisterSpec(SkillType.MagieEsprit, "Clairvoyance : Prescience du Danger", "Clairvoyance : Œil Distant", false, false,
                "Frisson d'alerte pré-cognitif.",
                "Conserve une unique réaction défensive à -1 lors d'une embuscade (au lieu d'aucune).");
            RegisterSpec(SkillType.MagieEsprit, "Clairvoyance : Champ de Prescience", "Clairvoyance : Prescience du Danger", true, false,
                "Voile prémonitoire d'escouade.",
                "Dépense 3 PA : tous les alliés à 2 cases gagnent +2 en Esquive pendant 1 tour.");

            // --- FICHES HÉROÏQUES EXTRAITES (DÉLÉGATION PROPRE) ---
            // Tout le contenu vit dans LucasCharacter / MinaCharacter / ThomasCharacter / JohnCharacter / ErikaCharacter / VanceCharacter.
            MinaCharacter.RegisterSpecializations();
            LucasCharacter.RegisterSpecializations();
            ThomasCharacter.RegisterSpecializations();
            JohnCharacter.RegisterSpecializations();
            ErikaCharacter.RegisterSpecializations();
            VanceCharacter.RegisterSpecializations();

        }
    }
}
