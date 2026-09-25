# 🎲 Système de Jeu RP — Codex Universel

> **Moteur de Jeu de Rôle Tactique, Chrono-Physique & Cosmologie Vivante**  
> *Le cadre de règles, de physique arcanique et de mécanique universelle qui englobe la saga narrative Killtime.*

[![Statut](https://img.shields.io/badge/Version-2.5_Codex_Complet-38bdf8.svg)](#)
[![Livres](https://img.shields.io/badge/Livres-I_%C3%A0_XI-f59e0b.svg)](#)
[![Univers](https://img.shields.io/badge/Lore-Killtime_Universe-10b981.svg)](Killtime/index.html)
[![Web_App](https://img.shields.io/badge/Plateforme-Web_Autonome-8b5cf6.svg)](index.html)
[![Licence](https://img.shields.io/badge/Licence-GPLv3-blue.svg)](Killtime/LICENSE)

---

## 🌌 Vue d'Ensemble & Périmètre du Projet

Ce dépôt héberge le **Système de Jeu RP (Codex Universel)**, un moteur complet de jeu de rôle sur table assisté par le web (web-native & zero-dependency).

### 📖 Comment le Système RP englobe l'Histoire Principale (*Killtime*)

Ce dépôt s'articule autour d'une hiérarchie claire :

1. **Le Système RP (Niveau Racine)** :  
   Il définit les **fondations physiques, mathématiques et cosmologiques** du monde :
   - L'économie d'action tactique au millième de seconde (Points d'Action - PA).
   - Les lois scientifiques de la **Cinquième Force** (magie unifiée, cristaux de Nytharite, moteurs arcaniques).
   - La topologie du **Fleuve du Temps** (lois strictes de la chrono-causalité, réécriture sans paradoxe).
   - Les règles de survie, de combat viscéral, les 16 états préjudiciables et l'échelle de dés vivante (`d2` à `2d12+10`).

2. **L'Histoire Principale (*Killtime* — sous-dossier `Killtime/`)** :  
   Les romans, manuscrits et récits (*The Hybris Saga*, tomes I et II, timelines de Lucas, Mina et Thomas) constituent **l'incarnation narrative directe** de ces règles.
   - Les prouesses des personnages (la void-thermodynamique de Lucas, les décharges cinétiques de Mina, l'immunité d'ancrage de Thomas) découlent fidèlement des mécaniques de ce Codex.
   - Un README narratif spécifique est disponible dans le sous-dossier dédié : [`Killtime/README.md`](Killtime/README.md).

---

## ⚡ Piliers Mécaniques du Système

```
               ┌──────────────────────────────────────────────┐
               │         SYSTÈME RP (CODEX UNIVERSEL)         │
               │   Lois Physiques, Arcaniques & Temporelles   │
               └──────────────────────┬───────────────────────┘
                                      │
         ┌────────────────────────────┼────────────────────────────┐
         ▼                            ▼                            ▼
┌──────────────────┐        ┌──────────────────┐        ┌──────────────────┐
│ ÉCONOMIE DE PA   │        │ CINQUIÈME FORCE  │        │ FLEUVE DU TEMPS  │
│ Tour de 10s      │        │ Magie Unifiée    │        │ Topologie &      │
│ Gestion Souffle  │        │ Nytharite & Psi  │        │ Chrono-Causalité │
└──────────────────┘        └──────────────────┘        └──────────────────┘
         │                            │                            │
         └────────────────────────────┼────────────────────────────┘
                                      │
                                      ▼
               ┌──────────────────────────────────────────────┐
               │           HISTOIRE & LORE KILLTIME           │
               │    Saga Hybris, Personnages, Tomes I & II     │
               └──────────────────────────────────────────────┘
```

### 1. L'Harmonie du Risque & Économie des PA
- **Le Tour Tactique de 10 Secondes** : Pas de temps mort ni de passivité. Chaque action (foulée, attaque ciblée, parade réflexe, canalisation) coûte des **Points d'Action (PA)** dérivés des attributs fondamentaux (`Agi/Int + Rap + Min`).
- **Seuils Corporels Réalistes** : Gestion de l'Encaissement (`Constitution × 2`), du Seuil Critique et de la Létalité Totale (`Constitution × 5`). Localisation chirurgicale des dégâts (jugulaire, tête, membres, blindages).

### 2. La Cinquième Force : Magie Scientifique
- La magie n'est pas un arbitraire nébuleux mais une force fondamentale unifiée avec la matière et l'énergie, canalisable par les résonateurs de **Nytharite**.
- **6 Paliers d'Évolution Psychique** : Télépathie, Télékinésie, Pyrokynésie, Clairsentience, Biokinésie et Réécriture Synaptique.
- **Atelier de Sorts Modulaire** : Équilibre mathématique absolu : `Coût en XP de création = Coût en PA d'exécution en combat`.

### 3. Chrono-Causalité & Fleuve du Temps
- Le voyage temporel suit des lois causales strictes le long de gradients d'entropie (Timelines 0, A, B, C, D).
- Clairsentience sur 8 échelons et propagation amont/aval sans rupture logique.

### 4. L'Échelle Polyédrique Vivante
- Système de dés dynamiques allant du dé d'amateur **d2** jusqu'au dé d'élite **d12** (et combinaisons avancées `2d12+10`).
- Tables critiques dédiées pour chaque calibre (`d2` à `d24`) et échelle de difficultés de 0 à 34.

---

## 📚 Architecture du Codex (11 Livres)

L'intégralité du corpus de règles est distribuée sous forme de documents web dédiés légers et ultra-rapides :

| Volume | Intitulé & Thématique | Contenu Clé |
| :--- | :--- | :--- |
| **[index.html](index.html)** | **Préface : Le Souffle & Le Temps** | Manifeste fondateur, philosophie du système et télémétrie |
| **[Livre I](livre_1.html)** | **Bases & Création de Personnage** | 6 Attributs, espèces, calcul des PA, feuille de PJ type |
| **[Livre II](livre_2.html)** | **Paliers de Dés & Épreuves** | Échelle d2-d12+, Seuils de Difficulté (0-34), Critiques |
| **[Livre III](livre_3.html)** | **Arbres de Compétences** | 3 Branches (Physique, Mentale, Sociale), Spécialisations |
| **[Livre IV](livre_4.html)** | **5e Force & Magie** | Résonance de la nytharite, moteurs arcaniques, 6 stades psi |
| **[Livre V](livre_5.html)** | **Temps & Causalité** | Topologie des chronotrames, clairsentience (8 échelons) |
| **[Livre VI](livre_6.html)** | **Combat Tactique** | Tour de 10s, déplacements, localisation, véhicules |
| **[Livre VII](livre_7.html)** | **Santé, États & Blessures** | 16 États préjudiciables, encaissement, premiers soins |
| **[Livre VIII](livre_8.html)** | **Arsenal & Arcanotech** | Armes métalliques, lames laser, blindages et pharmacopée |
| **[Livre IX](livre_9.html)** | **Outils Web Interactifs** | Fiche PJ, Créateur de Sorts, Lanceur de Dés, Chronotrames |
| **[Livre X](livre_10.html)** | **Atlas des Mondes Connus** | Les 36 Élianes, Hybris, Vardis, Apaphis, Cléia, Karkjiue |
| **[Livre XI](livre_11.html)** | **Bestiaire & Menaces Cosmiques** | Faune, automates impériaux, Disciples, The Minulican, Bosses |

---

## 🗂️ Structure du Dépôt

```
.
├── README.md                      # [Ce fichier] Architecture globale du Système RP
├── index.html                     # Interface racine du Codex (Préface & Hub)
├── livre_1.html ... livre_11.html # Les 11 Livres officiels du Système RP
├── css/
│   └── style.css                  # Design System complet (Dark/Light, Glassmorphism)
├── js/
│   ├── codex.js                   # Moteur de navigation, recherche et thème
│   └── tools.js                   # Calculateurs interactifs du Livre IX
├── cycleServer.sh & start.sh      # Scripts de démarrage du serveur local
│
└── Killtime/                      # Sous-projet : Univers narratif & Histoire principale
    ├── README.md                  # README spécifique à la saga narrative (Story & Lore)
    ├── index.html                 # Portail officiel de l'univers Killtime
    ├── manuscripts/               # Manuscrits originaux (Vol I RC1, Vol II, Drafts)
    ├── lore/                      # Dossiers de personnages, missions, timelines & factions
    └── scripts_and_data/          # Métriques de progression, thèmes et compendiums
```

---

## 🌐 English Executive Summary

> **The Universal RP System** is a tabletop & interactive web RPG engine designed around high-tactical action economy, unified scientific magic (The Fifth Force), and timeline causality mechanics.  
> 
> While the subfolder [`Killtime/`](Killtime/) focuses on the **Main Story** (the narrative saga of Lucas, Mina, and Thomas across multiple timelines), this root repository houses the **complete underlying RP System ruleset** that englobes and governs that universe.

- **10s Tactical Turns**: Dynamic Action Points (AP) dictate movement, defense, and attacks without artificial waiting.
- **Unified Arcane Physics**: Nytharite crystal resonance, psychic tiers, and an exact `XP creation cost = AP combat cost` spellcrafting equation.
- **Chrono-Causality**: Topology-driven time travel through the River of Time across Timelines 0, A, B, C, and D.
- **Polyhedral Dice Progression**: Fluid scaling from `d2` to `2d12+10` with custom critical threshold tables.

---

## 🚀 Démarrage Rapide

Le site fonctionne de manière complètement autonome (sans compilation ni dépendances Node) :

```bash
# Lancement avec le script de boucle
bash cycleServer.sh

# Ou directement avec Ruby
bash start.sh

# Ou via Python
python3 -m http.server 8000
```

Puis ouvrez votre navigateur à l'adresse : **`http://localhost:8000/`**

---

## 🏗️ Note de l'Architecte : De l'Enclume et de l'Exosquelette

> Le romantisme littéraire aime entretenir une légende tenace : celle de l'auteur isolé face au vide de la feuille, attendant qu'une muse mystique vienne guider sa plume. Cette vision appartient au siècle passé. Elle confond l'acte primitif de gribouiller avec l'ingénierie d'un univers total.

Bâtir *Killtime* et la saga de l'*Hybris*, c'est administrer un système tentaculaire : onze livres de règles mécaniques, une topologie hexagonale, des équations d'attrition physique, cinq embranchements temporels interconnectés et une continuité qui lie un moteur de combat en C# à des centaines de pages de prose.

Quiconque a déjà mené un projet d'une telle envergure connaît la vérité du métier : concevoir une fresque monumentale exige une discipline de fer, une traçabilité maniaque des causes et des effets, des milliers d'heures d'audit de cohérence, de vérification chronologique et de ciselage syntaxique.

À ceux qui s'interrogent sur la présence de l'intelligence artificielle dans cette œuvre, voici le protocole exact de notre atelier.

### 1. L'exosquelette, pas le pilote

L'intelligence artificielle générative, dans les mains d'un faiseur de raccourcis, produit de la soupe statistique : des récits sans âme, des métaphores recyclées et des coquilles vides générées en un clic.

Dans notre atelier, la machine occupe une fonction radicalement différente : celle d'un exosquelette cognitif et d'un compilateur d'intégrité.

* **L'esprit humain commande :** Chaque blessure, chaque dilemme moral, chaque choix tactique, l'angoisse des protagonistes, la tragédie de Brum'korath et l'architecture globale proviennent exclusivement de la volonté, du vécu et de la vision de l'Architecte.
* **Le système vérifie et calibre :** La machine opère comme un copilote de données. Elle traque les dérives de vocabulaire, audite la balance des Points d'Action, cartographie les paradoxes entre les lignes temporelles de l'An 0 et de 1772, indexe les fiches matricules et soumet chaque paragraphe à une friction critique impitoyable.

Prétendre qu'un créateur s'affaiblit en s'équipant du meilleur analyseur de cohérence disponible équivaut à exiger d'un architecte moderne qu'il creuse les fondations d'une cathédrale avec une cuillère en bois sous prétexte que le labeur manuel confère davantage de mérite.

### 2. La souveraineté de l'intention

La machine ne ressent rien. Elle ignore le poids du sang versé par Wallace en quatre virgule deux secondes ; elle ignore la morsure du froid métrique de Lucas, le souffle vital de Mina ou la masse tellurique de Thomas. Elle est incapable d'engendrer le sens.

Le sens est le monopole absolu de l'humain.

Ce livre a été écrit à la sueur d'une direction éditoriale intraitable. Chaque terme conservé a subi l'épreuve du ciseau. Chaque phrase a été sculptée pour frapper avec la densité du roc. Le recours à l'assistance algorithmique a permis d'éliminer le gaspillage mental des tâches redondantes pour concentrer l'énergie créatrice là où elle est irremplaçable : dans l'impact narratif, l'innovation mécanique et la puissance brute de la mise en scène.

### 3. Le pacte d'acier

Nous refusons l'hypocrisie de ceux qui utilisent ces technologies dans l'ombre tout en proclamant leur pureté artisanale en façade.

*Killtime* assume son hybridation. Cette saga est le fruit d'une rencontre entre une vision organique inflexible et un appareil de traitement computationnel de pointe. Le résultat sous vos yeux est un édifice blindé, cohérent jusque dans ses moindres engrenages, délivré sans complaisance et sans compromis.

Aux nostalgiques de la rature solitaire : vous tenez entre vos mains la preuve qu'un outil ne remplace jamais le forgeron, mais qu'entre les mains d'un bâtisseur, un marteau plus lourd permet d'élever des citadelles indestructibles.

Le livre est ouvert. La causalité est en marche.

**— Alexis Lacasse**
*Architecte de la Saga Hybris*

---

## ⚖️ Licence & Droits

- **Auteur & Architecte** : Alexis Lacasse ([@k0r0z1f](https://github.com/k0r0z1f))
- **Univers Narratif Associé** : *The Hybris Saga / Killtime Universe*
- **Licence** : GNU General Public License v3.0 ([Killtime/LICENSE](Killtime/LICENSE))
