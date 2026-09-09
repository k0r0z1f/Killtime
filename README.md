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

## ⚖️ Licence & Droits

- **Auteur & Architecte** : Alexis Lacasse ([@k0r0z1f](https://github.com/k0r0z1f))
- **Univers Narratif Associé** : *The Hybris Saga / Killtime Universe*
- **Licence** : GNU General Public License v3.0 ([Killtime/LICENSE](Killtime/LICENSE))
