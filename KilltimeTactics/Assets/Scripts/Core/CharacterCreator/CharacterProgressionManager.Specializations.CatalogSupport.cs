using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    public partial class CharacterProgressionManager
    {
        // CharacterProgressionManager.Specializations.CatalogSupport.cs — Catalogue soutien/social : medecine, arcanotech, tactique, communication, intuition, artisanat.

        private static void RegisterSupportCatalog()
        {
            RegisterSpec(SkillType.MedecineAvancee, "Chirurgie", null, false, false,
                "Intervention chirurgicale de guerre en milieu hostile.",
                "Permet de refermer les blessures critiques et de lever l'état Souffrant.");
            RegisterSpec(SkillType.MedecineAvancee, "Chirurgie : Diagnostic Vital", "Chirurgie", false, false,
                "Analyse clinique immédiate.",
                "Permet de connaître exactement les PV, seuils et armure de toute unité examinée.");
            RegisterSpec(SkillType.MedecineAvancee, "Chirurgie : Suture Réflexe", "Chirurgie : Diagnostic Vital", false, false,
                "Gestes médicaux d'urgence sur le front.",
                "Le coût en PA des premiers soins d'urgence passe de 3 PA à 2 PA.");
            RegisterSpec(SkillType.MedecineAvancee, "Chirurgie : Greffe d'Urgence", "Chirurgie : Suture Réflexe", true, false,
                "Reconstitution cellulaire instantanée.",
                "Restaure immédiatement la fonctionnalité d'un membre détruit ou amputé en combat.");

            RegisterSpec(SkillType.MedecineAvancee, "Apothicaire de Guerre", null, false, false,
                "Synthèse chimique de terrain et stimulants de combat.",
                "Permet de composer des stimulants (Antidouleurs, Speed) sans laboratoire fixe.");
            RegisterSpec(SkillType.MedecineAvancee, "Apothicaire de Guerre : Stimulant Neuro-Accélérateur", "Apothicaire de Guerre", false, false,
                "Dopage de vitesse synaptique.",
                "Synthétise un stimulant accordant l'état Rapide (+3 PA de mouvement) pendant 4 heures.");
            RegisterSpec(SkillType.MedecineAvancee, "Apothicaire de Guerre : Sérum Antidote", "Apothicaire de Guerre : Stimulant Neuro-Accélérateur", false, false,
                "Contre-poison instantané.",
                "Purge immédiatement toutes les altérations biologiques actives sur un allié au contact.");
            RegisterSpec(SkillType.MedecineAvancee, "Apothicaire de Guerre : Cocktail Panacée", "Apothicaire de Guerre : Sérum Antidote", true, false,
                "Élixir de survie ultime.",
                "Soigne 15 PV et immunise contre toutes les altérations d'état pendant 3 tours.");

            RegisterSpec(SkillType.IngenierieArcanotech, "Surfréquence Nytharite", null, false, false,
                "Surcharge des circuits énergétiques à cristal de nytharite.",
                "Dépense 2 PA : double la puissance d'une arme laser ou d'un champ de force pendant 1 tour.");
            RegisterSpec(SkillType.IngenierieArcanotech, "Surfréquence Nytharite : Stabilisateur de Flux", "Surfréquence Nytharite", false, false,
                "Élimination des retours de flamme.",
                "L'équipement en surfréquence n'a plus aucun risque d'être endommagé ou jammé.");
            RegisterSpec(SkillType.IngenierieArcanotech, "Surfréquence Nytharite : Décharge Harmonique", "Surfréquence Nytharite : Stabilisateur de Flux", false, false,
                "Surplus de puissance plasmique.",
                "La surfréquence ajoute +3 Dégâts d'énergie pure à toutes les attaques du tour.");
            RegisterSpec(SkillType.IngenierieArcanotech, "Surfréquence Nytharite : Résonance Infinie", "Surfréquence Nytharite : Décharge Harmonique", true, false,
                "Couplage parfait avec la Cinquième Force.",
                "Maintient la surfréquence active pendant 3 tours consécutifs sans coût de recharge.");

            RegisterSpec(SkillType.TactiqueStrategie, "Analyse de Faille", null, false, false,
                "Détection des points faibles de blindage adverse.",
                "Dépense 2 PA : le prochain coup porté par vous ou un allié ignore l'encaissement adverse.");
            RegisterSpec(SkillType.TactiqueStrategie, "Analyse de Faille : Tir Coordonné", "Analyse de Faille", false, false,
                "Guidage balistique pour l'escouade.",
                "Tous les alliés bénéficient d'un bonus de +2 au jet d'attaque contre la cible analysée.");
            RegisterSpec(SkillType.TactiqueStrategie, "Analyse de Faille : Prédiction de Retraite", "Analyse de Faille : Tir Coordonné", false, false,
                "Anticipation géométrique des angles de fuite.",
                "Empêche la cible analysée de bénéficier de toute couverture pendant son prochain tour.");
            RegisterSpec(SkillType.TactiqueStrategie, "Analyse de Faille : Échec et Mat", "Analyse de Faille : Prédiction de Retraite", true, false,
                "Piège tactique absolu du Maître de Guerre.",
                "Toute attaque réussie contre la cible analysée inflige un coup critique garanti.");

            RegisterSpec(SkillType.Communication, "Tromper", null, false, false,
                "Feintes oratoires, diversion et duperie.",
                "Épreuve sociale opposée à l'Intuition adverse pour tromper un interlocuteur.");
            RegisterSpec(SkillType.Communication, "Tromper : Feinte Oratoire", "Tromper", false, false,
                "Détournement d'attention en plein combat.",
                "Dépense 1 PA : force un ennemi à tourner le dos jusqu'au tour suivant.");
            RegisterSpec(SkillType.Communication, "Tromper : Faux Signalement", "Tromper : Feinte Oratoire", false, false,
                "Infiltration et leurres radio.",
                "Provoque la dispersion des renforts ennemis hors du champ de bataille.");
            RegisterSpec(SkillType.Communication, "Tromper : Mascarade Totale", "Tromper : Faux Signalement", true, false,
                "Usurpation d'identité parfaite.",
                "Permet de traverser les zones ennemies sans déclencher aucune hostilité.");

            RegisterSpec(SkillType.Communication, "Négocier", null, false, false,
                "Marchandage institutionnel impérial.",
                "Réduit de 10% à 50% le prix d'achat des équipements en crédits CE.");
            RegisterSpec(SkillType.Communication, "Négocier : Marché Noir Impérial", "Négocier", false, false,
                "Accès aux filières clandestines.",
                "Débloque l'achat de prototypes et d'équipements de rareté Légendaire.");
            RegisterSpec(SkillType.Communication, "Négocier : Cessez-le-Feu", "Négocier : Marché Noir Impérial", false, false,
                "Trêve militaire temporaire.",
                "Suspend les attaques ennemies pendant 1 tour pour engager des pourparlers.");
            RegisterSpec(SkillType.Communication, "Négocier : Rachat d'Allégeance", "Négocier : Cessez-le-Feu", true, false,
                "Corruption et retournement de mercenaires.",
                "Permet de convertir un sbire ennemi en allié pour le reste du combat.");

            RegisterSpec(SkillType.Intimidation, "Intimider", null, false, false,
                "Ascendant psychologique et menaces de mort.",
                "Sur un différentiel net Delta >= 3, force un sbire à fuir le champ de bataille.");
            RegisterSpec(SkillType.Intimidation, "Intimider : Rugissement de Terreur", "Intimider", false, false,
                "Pression psychologique de zone.",
                "Applique l'état Déstabilisé à tous les ennemis situés à moins de 3 cases.");
            RegisterSpec(SkillType.Intimidation, "Intimider : Regard de Prédateur", "Intimider : Rugissement de Terreur", false, false,
                "Défi du champion en duel singulier.",
                "Empêche la cible désignée d'attaquer toute autre unité que vous.");
            RegisterSpec(SkillType.Intimidation, "Intimider : Paralysie Psychologique", "Intimider : Regard de Prédateur", true, false,
                "Terreur tétanisante absolue.",
                "Applique l'état Paralysé pendant 1 tour sur toute cible dont la Volonté est inférieure au Charisme de l'attaquant.");

            RegisterSpec(SkillType.Leadership, "Mener (Commandement)", null, false, false,
                "Coordination tactique et galvanisation des troupes.",
                "Dépense 2 PA : confère +1 PA réflexe à tous les alliés situés à moins de 3 cases.");
            RegisterSpec(SkillType.Leadership, "Mener : En avant !", "Mener (Commandement)", false, false,
                "Impulsion d'assaut coordonné.",
                "Confère +2 PA de déplacement à deux alliés désignés.");
            RegisterSpec(SkillType.Leadership, "Mener : Tenir la Ligne !", "Mener : En avant !", false, false,
                "Discipline de fer sous le feu.",
                "Augmente de +3 le seuil d'encaissement de tous les alliés proches pendant 1 tour.");
            RegisterSpec(SkillType.Leadership, "Mener : Galvanisation des Héros", "Mener : Tenir la Ligne !", true, false,
                "Inspiration cosmique de l'Ordre.",
                "Restaure 2 PA à toute l'escouade et purge immédiatement toutes les altérations mentales.");

            RegisterSpec(SkillType.Observation, "Vigilance Réflexe", null, false, false,
                "Vigilance sensorielle immédiate et détection des menaces.",
                "Immunise contre l'effet de surprise et conserve la réaction défensive complète lors d'une embuscade.");
            RegisterSpec(SkillType.Observation, "Vigilance Réflexe : Oeil de Lynx", "Vigilance Réflexe", false, false,
                "Acuité visuelle perçante dans l'obscurité.",
                "Confère +2 aux duels de détection (Observation) du brouillard de guerre (Livre V §21.4).");
            RegisterSpec(SkillType.Observation, "Vigilance Réflexe : Détection Thermique", "Vigilance Réflexe : Oeil de Lynx", false, false,
                "Vision infrarouge naturelle.",
                "Révèle les unités camouflées et invisibles à travers les parois légères.");
            RegisterSpec(SkillType.Observation, "Vigilance Réflexe : Perception Panoramique 360°", "Vigilance Réflexe : Détection Thermique", true, false,
                "Vigilance totale, sans angle mort (le combat KT voit déjà à 360°, §21.4).",
                "Immunité totale aux embuscades et bonus de surprise permanent.");

            RegisterSpec(SkillType.Intuition, "Sphère de Résonance", null, false, true,
                "Champ proprioceptif étendu (Volume II — Résonance).",
                "Immunité aux attaques de dos et embuscades dans un rayon de 3 cases.");
            RegisterSpec(SkillType.Intuition, "Sphère de Résonance : Écho Myotatique", "Sphère de Résonance", false, true,
                "Perception des vecteurs d'attaque.",
                "Permet d'ajuster gratuitement sa mise de +1 PA lors du Duel Aveugle en défense.");
            RegisterSpec(SkillType.Intuition, "Sphère de Résonance : Champ Prédictif", "Sphère de Résonance : Écho Myotatique", false, true,
                "Calcul prédictif des trajectoires.",
                "Anticipe la zone anatomique ciblée par l'attaquant et gagne +2 en esquive.");
            RegisterSpec(SkillType.Intuition, "Sphère de Résonance : Omniprésence Synaptique", "Sphère de Résonance : Champ Prédictif", true, true,
                "Clairsentience absolue du Fleuve du Temps.",
                "Révèle les mises de PA et PE de l'adversaire avant de déclarer sa propre défense.");

            RegisterSpec(SkillType.Intuition, "Sens du Danger", null, false, false,
                "Réagit avant même de comprendre la menace.",
                "L'Instinct compte dans la base d'Initiative (max Rapidité, Agilité, Intelligence, Instinct).");
            RegisterSpec(SkillType.Intuition, "Sens du Danger : Premiers Réflexes", "Sens du Danger", false, false,
                "Détente du félin acculé.",
                "Confère +1 palier de dé effectif sur le jet d'Initiative.");
            RegisterSpec(SkillType.Intuition, "Sens du Danger : Clairvoyance du Vétéran", "Sens du Danger : Premiers Réflexes", true, false,
                "Jamais surpris, jamais pris à revers.",
                "Immunité totale aux embuscades et aux attaques de dos.");
            RegisterSpec(SkillType.Intuition, "Instinct du Chasseur", null, false, false,
                "Pistage, affût et lecture du terrain giboyeux.",
                "Suit une piste fraîche sans épreuve et estime nombre, poids et allure de la proie.");
            RegisterSpec(SkillType.Intuition, "Instinct du Chasseur : Pistage", "Instinct du Chasseur", false, false,
                "Nez du limier d'Hybris.",
                "Confère +2 en NatureSurvie pour pister, même sur sol dur ou par nuit sans lune.");
            RegisterSpec(SkillType.Intuition, "Instinct du Chasseur : Piégeur", "Instinct du Chasseur : Pistage", false, false,
                "Collets, fosses et traquenards.",
                "Pose et détecte les pièges de campagne ; les proies prises sont Immobilisées.");

            RegisterSpec(SkillType.Artisanat, "Affûtage de Précision", null, false, false,
                "Forge d'acier et préparation mécanique des tranchants.",
                "Confère +1 Dégât brut aux 3 prochaines attaques réussies avec l'arme.");
            RegisterSpec(SkillType.Artisanat, "Affûtage : Fil Monomoléculaire", "Affûtage de Précision", false, false,
                "Aiguisage nanométrique extrême.",
                "Confère +2 Dégâts bruts permanents aux armes blanches équipées.");
            RegisterSpec(SkillType.Artisanat, "Affûtage : Blindage Trempé", "Affûtage : Fil Monomoléculaire", false, false,
                "Trempe thermique de plaques de blindage.",
                "Renforce les armures métalliques équipées de +1 point d'absorption permanent.");
            RegisterSpec(SkillType.Artisanat, "Affûtage : Acier de Maître d'Hybris", "Affûtage : Blindage Trempé", true, false,
                "Forge légendaire des Maîtres d'Armes.",
                "L'arme ne s'émousse jamais et inflige des coups critiques dès un résultat de 11 ou 12 sur les dés.");

        }
    }
}
