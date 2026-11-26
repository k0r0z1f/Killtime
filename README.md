# 🎲 Système de Jeu RP — Codex Universel

> **Moteur de Jeu de Rôle Tactique, Chrono-Physique & Cosmologie Vivante**  
> *Le cadre de règles, de physique arcanique et de mécanique universelle qui englobe la saga narrative Killtime.*

[![Statut](https://img.shields.io/badge/Version-0.8.0_B%C3%AAta_Codex-38bdf8.svg)](WHATS_NEW.md)
[![Livres](https://img.shields.io/badge/Livres-I_%C3%A0_XII-f59e0b.svg)](#)
[![Appendices](https://img.shields.io/badge/Appendices-A_%C2%B7_B_%C2%B7_C-0ea5e9.svg)](appendice_actions.html)
[![Univers](https://img.shields.io/badge/Lore-Killtime_Universe-10b981.svg)](Killtime/index.html)
[![Web_App](https://img.shields.io/badge/Plateforme-Web_Autonome-8b5cf6.svg)](index.html)
[![Licence](https://img.shields.io/badge/Licence-GPLv3-blue.svg)](Killtime/LICENSE)
[![What's New](https://img.shields.io/badge/What%27s_New-v0.8.0-ef4444.svg)](WHATS_NEW.md)
[![Baseline](https://img.shields.io/badge/Baseline-6f497f7-6366f1.svg)](docs/mecaniques_systeme_rp_6f497f7.md)

---

## 🌌 Vue d'Ensemble & Périmètre du Projet

Ce dépôt héberge le **Système de Jeu RP (Codex Universel)**, un moteur complet de jeu de rôle sur table assisté par le web (web-native, avec serveur Node.js optionnel pour les comptes, les fiches et les tables VTT).

### 📖 Comment le Système RP englobe l'Histoire Principale (*Killtime*)

Ce dépôt s'articule autour d'une hiérarchie claire :

1. **Le Système RP (Niveau Racine)** :  
   Il définit les **fondations physiques, mathématiques et cosmologiques** du monde :
   - L'économie d'action tactique au dixième de seconde (Points d'Action - PA sur tours de 10 secondes).
   - Les lois scientifiques de la **Cinquième Force** (magie unifiée, cristaux de Nytharite, moteurs arcaniques).
   - La topologie du **Fleuve du Temps** (lois strictes de la chrono-causalité, réécriture sans paradoxe).
   - Les règles de survie, de combat viscéral, les **19 états préjudiciables** et l'échelle de dés vivante (`d2` à `2d12+10`).

2. **L'Histoire Principale (*Killtime* — sous-dossier `Killtime/`)** :  
   Les romans, manuscrits et récits (*The Hybris Saga*, tomes I et II, timelines de Lucas, Mina et Thomas) constituent **l'incarnation narrative directe** de ces règles.
   - Les prouesses des personnages (la void-thermodynamique de Lucas, les décharges cinétiques de Mina, l'immunité d'ancrage de Thomas) découlent fidèlement des mécaniques de ce Codex.
   - Un README narratif spécifique est disponible dans le sous-dossier dédié : [`Killtime/README.md`](Killtime/README.md).

3. **Documentation de Référence & Suivi des Évolutions (Racine & Menu *Pilotage*)** :  
   - [`WHATS_NEW.md`](WHATS_NEW.md) : journal détaillé des évolutions mécaniques et plateforme (`6f497f7 ➔ 6fd7604`).
   - [`docs/mecaniques_systeme_rp_6f497f7.md`](docs/mecaniques_systeme_rp_6f497f7.md) : référence complète des mécaniques du système RP au commit `6f497f7`.
   - Tous deux, ainsi que ce README, sont consultables directement dans le lecteur intégré [`doc_viewer.html`](doc_viewer.html?doc=whats-new) depuis la rubrique **Pilotage** du Codex.

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
- **Le Tour Tactique de 10 Secondes** : Pas de temps mort ni de passivité. Chaque action (foulée, manœuvre à `2 PA`, attaque ciblée, parade réflexe, canalisation) coûte des **Points d'Action (PA)** dérivés des **8 Attributs Majeurs** (`FOR`, `AGI`, `CON`, `INT`, `RAP`, `PER`, `VOL`, `MAG`) selon **`Max(AGI, INT) + RAP + Min(attributs > 0)`** (`MAG = 0` exclue du minimum pour les non-éveillés).
- **Seuils Corporels Réalistes** : Gestion de l'Encaissement (`≥ CON × 2`), du Seuil Critique, de la Létalité Totale (`≥ CON × 5`) et du plafond d'Essoufflement (`CON`). Localisation chirurgicale des dégâts (jugulaire, tête, membres, blindages).

### 2. La Cinquième Force : Magie Scientifique & Maîtrises
- La magie est une force fondamentale unifiée avec la matière et l'énergie, canalisable par les résonateurs de **Nytharite**, structurée autour de **3 Voies** (`Magie Élémentale`, `Magie Primale`, `Magie de l'Esprit`) regroupant **10 éléments/canaux** et **`330` spécialisations** (`2 PA` par défaut en combat).
- **Atelier de Sorts Modulaire** : Équilibre mathématique absolu : `Coût en XP de création = Coût en PA d'exécution en combat`.
- **Arbres de Compétences & Fabrique (`Livre III`)** : 3 branches totalisant **`198` spécialisations de compétences** et une Fabrique de techniques personnalisées.

### 3. Chrono-Causalité & Fleuve du Temps
- Le voyage temporel suit des lois causales strictes le long de gradients d'entropie (Timelines 0, A, B, C, D).
- Clairsentience sur 8 échelons et propagation amont/aval sans rupture logique.

### 4. L'Échelle Polyédrique Vivante
- Système de dés dynamiques allant du dé d'amateur **d2** jusqu'au dé d'élite **d12** (et combinaisons avancées `2d12+10`).
- Tables critiques dédiées pour chaque calibre (`d2` à `d24`) et échelle de difficultés de `0` à `34`.

---

## 📚 Architecture du Codex (12 Livres, 53 Chapitres & 3 Appendices)

L'intégralité du corpus de règles est distribuée sous forme de documents web dédiés légers et ultra-rapides :

| Volume | Intitulé & Thématique | Contenu Clé |
| :--- | :--- | :--- |
| **[index.html](index.html)** | **Préface : Le Souffle & Le Temps** | Manifeste fondateur, philosophie du système et télémétrie |
| **[Livre I](livre_1.html)** | **Fondations & Création** | **8 Attributs Majeurs**, espèces, calcul des PA, feuille de PJ type (`§1–§5`) |
| **[Livre II](livre_2.html)** | **Paliers de Dés, Difficultés & Critiques** | Échelle `d2`–`2d12+10`, Seuils de Difficulté (`0–34`), Critiques (`§6–§10`) |
| **[Livre III](livre_3.html)** | **Arbres de Compétences** | 3 Branches (Physique, Mentale, Sociale), **`198` Spécialisations** & Fabrique (`§11–§15`) |
| **[Livre IV](livre_4.html)** | **5e Force & Magie** | Résonance de la nytharite, moteurs arcaniques, **3 Voies / `330` spécialisations**, Atelier `XP = PA` (`§16–§19`) |
| **[Livre V](livre_5.html)** | **Combat Tactique** | Tour de 10s, manœuvres tactiques (`2 PA`), localisation, véhicules (`§20–§22`) |
| **[Livre VI](livre_6.html)** | **Santé & États** | **`19` États préjudiciables**, encaissement, premiers soins (`§23–§25`) |
| **[Livre VII](livre_7.html)** | **Historiques** | Enfances (d6), métiers (d12), événement marquant, ancrage lore (`§26–§29`) |
| **[Livre VIII](livre_8.html)** | **Arsenal** | Armes métalliques, lames laser, blindages et pharmacopée (`§30–§32`) |
| **[Livre IX](livre_9.html)** | **Outils Web Interactifs** | Fiche PJ, Créateur de Sorts, Lanceur de Dés, Chronotrames (`§33–§38`) |
| **[Livre X](livre_10.html)** | **Temps & Causalité** | Topologie des chronotrames, clairsentience (8 échelons) (`§39–§42`) |
| **[Livre XI](livre_11.html)** | **Atlas des Mondes** | Les 36 Élianes, Hybris, Vardis, Nefris, Cléia, Karkjiue (`§43–§47`) |
| **[Livre XII](livre_12.html)** | **Bestiaire** | Faune, automates impériaux, Disciples, The Minulican, Bosses (`§48–§53`) |

### Appendices & Pilotage

| Module / Page | Périmètre & Fonctionnalités |
| :--- | :--- |
| **[Appendice A — Répertoire des Actions](appendice_actions.html)** | `101` actions structurées en `7` catégories |
| **[Appendice B — Glossaire Universel](appendice_glossaire.html)** | `296` termes répartis sur `9` domaines |
| **[Appendice C — Catalogue des Objets](appendice_objets.html)** | `140` fiches `DLPH`, `10` catégories (`C.1–C.10`), `13` propriétés et `6` kits (`E0–E5`) |
| **[Lecteur de Documentation (`doc_viewer.html`)](doc_viewer.html?doc=whats-new)** | Vue intégrée dans *Pilotage* pour [What's New](WHATS_NEW.md), [Baseline `6f497f7`](docs/mecaniques_systeme_rp_6f497f7.md) et [README](README.md) |
| **Pilotage, Fiches & VTT** | [`roadmap.html`](roadmap.html), [`kanban_rp.html`](kanban_rp.html) (Revue Système RP & Board VTT Web), [`kanban_narratif.html`](kanban_narratif.html), [`financement.html`](financement.html), [`tables.html`](tables.html) (Tables de groupe, Chat, MPs, Vocal WebRTC), [`fiche_pj.html`](fiche_pj.html), [`atlas_mondes.html`](atlas_mondes.html) / [`atlas_3d.html`](atlas_3d.html) |

---

## 🗂️ Structure du Dépôt

```
.
├── README.md                              # [Ce fichier] Présentation globale du Système RP
├── WHATS_NEW.md                           # Journal des évolutions (6f497f7 ➔ 6fd7604)
├── docs/
│   └── mecaniques_systeme_rp_6f497f7.md   # Référence détaillée des mécaniques (baseline 6f497f7)
├── index.html                             # Interface racine du Codex (Préface & Hub)
├── livre_1.html ... livre_12.html         # Les 12 Livres du Système RP (53 chapitres)
├── appendice_actions.html                 # Appendice A : Répertoire des Actions (101)
├── appendice_glossaire.html               # Appendice B : Glossaire Universel (296)
├── appendice_objets.html                  # Appendice C : Catalogue des Objets (140 fiches DLPH)
├── doc_viewer.html                        # Lecteur Markdown intégré (menu Pilotage)
├── tables.html & fiche_pj.html            # Tables de jeu VTT (chat, vocal WebRTC) & fiches PJ
├── roadmap.html & kanban_*.html           # Pilotage projet, Revue RP & Board VTT Web
├── financement.html & atlas_*.html        # Financement & Atlas interactifs (2D / 3D)
├── admin.html                             # Interface d'administration locale (127.0.0.1:3100)
├── content/                               # Sources Markdown modulaires des Livres
├── data/                                  # Bases JSON (livre_*.json, appendice_*.json, kanban_rp.json, roadmaps.json, atlas_mondes.json...)
├── css/                                   # Design System (style.css, themes.css, secret-veil.css, table-dock.css)
├── js/                                    # Moteurs client (codex.js, livre.js, tools.js [Livre IX], fabrique.js, doc_viewer.js, appendice_*.js, tables*.js, kanban_*.js, roadmap.js)
├── users/                                 # Serveur Node.js auth/comptes/API/VTT (server.js : public 3000 + admin 127.0.0.1:3100)
├── tools/                                 # Générateurs & validateurs Python (build_livres.py, build_nav.py, build_appendice_objets.py, check_liens.py...)
├── start_all.sh, cycleServer.sh, start.sh # Scripts de lancement (stack complète + HTTP statique)
└── Killtime/                              # Sous-projet : Univers narratif & Histoire principale
    ├── README.md                          # README spécifique à la saga narrative (Story & Lore)
    ├── index.html                         # Portail officiel de l'univers Killtime
    └── Killtime/                          # Manuscrits (manuscripts/), dossiers (lore/) & métriques (scripts_and_data/)
```

---

## 🌐 English Executive Summary

> **The Universal RP System** is a tabletop & interactive web RPG engine designed around high-tactical action economy, unified scientific magic (The Fifth Force), and timeline causality mechanics across **12 Books / 53 chapters** and **3 interactive Appendices**.  
> 
> While the subfolder [`Killtime/`](Killtime/) focuses on the **Main Story** (the narrative saga of Lucas, Mina, and Thomas across multiple timelines), this root repository houses the **complete underlying RP System ruleset** that englobes and governs that universe (see also [`WHATS_NEW.md`](WHATS_NEW.md) and [`docs/mecaniques_systeme_rp_6f497f7.md`](docs/mecaniques_systeme_rp_6f497f7.md)).

- **10s Tactical Turns & 8 Major Attributes**: Action Points scale via `Max(AGI, INT) + RAP + Min(attributes > 0)` with **`19` pre-defined conditions**, `2 AP` tactical maneuvers, and surgical hit locations.
- **Unified Arcane Physics & Mastery**: Nytharite crystal resonance across **3 Paths (`330` arcane specializations)**, **`198` skill specializations**, and an exact `XP creation cost = AP combat cost` spellcrafting equation.
- **Chrono-Causality**: Topology-driven time travel through the River of Time across Timelines 0, A, B, C, and D (8-tier Clairsentience).
- **Polyhedral Dice Progression & Web Platform**: Fluid scaling from `d2` to `2d12+10` with custom critical tables (`0–34` difficulty scale), **`140` DLPH equipment entries**, built-in Markdown doc viewer, and Node.js VTT tables.

---

## 🚀 Démarrage Rapide

Trois modes de lancement sont disponibles selon vos besoins :

```bash
# 1. Stack complète (HTTP Ruby :8000 + Node auth/VTT :3000 & admin :3100 + tunnel cloudflared)
bash start_all.sh

# 2. Serveur Node.js seul (comptes, API, tables VTT WebSocket & admin locale)
node users/server.js
# ➔ Public : http://localhost:3000/  |  Admin (loopback) : http://127.0.0.1:3100/admin.html

# 3. Consultation statique pure (sans dépendance Node)
bash start.sh
# ou : python3 -m http.server 8000
# ➔ http://localhost:8000/
```

### Build & Vérification du Codex

```bash
python3 tools/build_livres.py
python3 tools/build_nav.py --build
python3 tools/check_liens.py
```

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
