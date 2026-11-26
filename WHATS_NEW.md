# What's New — Évolutions du Système RP (`6f497f7` ➔ `6fd7604`)

> **Plage couverte :** Tout ce qui s'est ajouté ou modifié **après** le commit [`6f497f7`](https://github.com/k0r0z1f/Killtime/commit/6f497f72c92b6d89dc7840fa7dbb92c28a947cdf) (inclus dans la baseline locale `summaries/mecaniques_systeme_rp_6f497f7.md`) jusqu'à `HEAD` = [`6fd7604`](https://github.com/k0r0z1f/Killtime/commit/6fd760482aa3c20e5a616f1ce39b24bc11cdb67e).  
> **Périmètre exact (3 + 1 commits) :** Depuis `6f497f7`, le dépôt compte **3 commits d'audit mécanique `sysrp`** (`6fd7604`, `5146306`, `2a867a6` du 2026-11-25 soir) précédés d'**1 commit plateforme & compendium objets** (`76e6dcf` du 2026-11-25 03:56). Tous sont détaillés ci-dessous par ordre antéchronologique.

## Sommaire

- [Vue d'ensemble — Avant (`6f497f7`) ➔ Maintenant (`6fd7604`)](#vue-densemble--avant-6f497f7--maintenant-6fd7604)
- [1. Commit `6fd7604` — Livre IV Passe P2 & Livre V `§20.4` (`RP-185`, `RP-186`, `RP-218`)](#1-commit-6fd7604--livre-iv-passe-p2--livre-v-204-rp-185-rp-186-rp-218)
- [2. Commit `5146306` — Livre III Passe P1 (`RP-181`, `RP-182`, `RP-136`, `RP-183`, `RP-015`)](#2-commit-5146306--livre-iii-passe-p1-rp-181-rp-182-rp-136-rp-183-rp-015)
- [3. Commit `2a867a6` — Audits F2 & F5, Livre II `§8` & Livre III `§12.1` (`RP-241`, `RP-244`, `RP-131`, `RP-180`)](#3-commit-2a867a6--audits-f2--f5-livre-ii-8--livre-iii-121-rp-241-rp-244-rp-131-rp-180)
- [4. Commit `76e6dcf` — Appendice C Objets, Board VTT Web & Infra (`RP-250`, `RP-251`, `RP-252`, `VTT-001..045`)](#4-commit-76e6dcf--appendice-c-objets-board-vtt-web--infra-rp-250-rp-251-rp-252-vtt-001045)
- [5. État des écarts résiduels depuis `6f497f7`](#5-état-des-écarts-résiduels-depuis-6f497f7)

---

## Vue d'ensemble — Avant (`6f497f7`) ➔ Maintenant (`6fd7604`)

| Domaine / Règle | Baseline (`6f497f7`) | État actuel (`6fd7604`) | Commit |
| :--- | :--- | :--- | :---: |
| **Attributs & Budget Éveil** | `7` attributs majeurs + `Magie` séparée (`+2` pts secondaires si éveillé : `23 / 22 / 13`) | **`8` Attributs Majeurs** (`MAG` dans le budget unique : **`21 / 20 / 11`**, `0` toléré en `MAG` seul, autorisée en Pilier) | `2a867a6` |
| **Formule des PA** | `Max(AGI, INT) + RAP + Min(7 attributs)` | **`Max(AGI, INT) + RAP + Min(attributs > 0)`** (`Magie = 0` exclue) | `2a867a6` |
| **Plafond Anti-Farm XP lié** | `niveau + 2` avec conversion forcée `2:1` en XP libre | **`5 + niveau de base`**, **zéro conversion** en XP libre | `2a867a6` |
| **Jets physiques Livre III `§12.1`** | Plusieurs renvois à des jets de « Force brute » + seuil FOR sur armures lourdes | Tout passe par **`Athlétisme`**, **`Endurance Physique`** ou **`Lutte` (`§20.8`)** ; armures lourdes régies uniquement par l'Encombrement (`§32.1`) | `2a867a6` |
| **Compendium Spés Livre III (`§14`)** | `166` spécialisations (plusieurs compétences à `0` spé) | **`198` spécialisations** (`+35` ajouts, `-3` renommages/fusions, `44` parentés normalisées, `15+` révisions d'effets) | `5146306` |
| **Fabrique Livre III (`§15`)** | `C3/C4` notés `1/round` ; renvois `Livre IV §20` ; `D7`/`U12` anciens | `C3/C4` unifiés en **`1/cycle`**, renvois `Livre IV §19`, `D7` & `U12` (*Réserve d'urgence* `+2 PA / 1 PE`) révisés | `5146306` |
| **Compendium Arcanes Livre IV (`§19.4`)** | `329` entrées (`nb_specialisations: 46` erroné), `37` mentions d'`hex`, coût PA implicite | **`330` entrées** (`+Souffle Ardent` 33e Feu), `nb_specialisations: 330`, **`0` `hex`** (`cases`), coût **`2 PA` par défaut** explicite | `6fd7604` |
| **Atelier Sorts Livre IV & Livre IX** | Module `1 pt` *Cible Débalancée* ; `3 pts` `+2 Résistance Résiduelle` ; ex. *Écrasement* incohérent | `1 pt` = **`−1 à sa prochaine défense`** ; `3 pts` = **`Réduction Résiduelle (−2 dégâts résiduels)`** ; ex. *Écrasement* recomposé (`+2` dir. + *À Terre*) | `6fd7604` |
| **Livre V `§20.4`** | `9` effets spéciaux gratuits déclenchés sur différentiel `Δ` | **`2` manœuvres dédiées à `2 PA`** (*Bousculer / Repousser* & *Attirer*) + renvois propres sans doublon | `6fd7604` |
| **Appendice B (Glossaire)** | `300` termes | **`296` termes** (`4` doublons `effet-*` supprimés, `6` entrées renommées/recalées) | `6fd7604` |
| **Appendice C (Objets)** | Absent | **[`appendice_objets.html`](appendice_objets.html)** : **`140` fiches `DLPH`** (`10` catégories `C.1–C.10`, `6` kits `E0–E5`) | `76e6dcf` |
| **Kanban & Infrastructure** | `board-revue-rp` seul, tunnel `ngrok`, sans SEO | **`+board-vtt-web`** (`45` tâches `VTT-001..045`), tunnel **`cloudflared` (`killtime.studio`)**, `robots.txt` / `sitemap.xml` | `76e6dcf` |

---

## 1. Commit `6fd7604` — Livre IV Passe P2 & Livre V `§20.4` (`RP-185`, `RP-186`, `RP-218`)

*Date : 2026-11-25 20:44 · Tickets Kanban clôturés : `RP-185`, `RP-186`, `RP-218`.*  
*Fichiers clés : [livre_4.json](data/livre_4.json), [livre_5.json](data/livre_5.json), [livre_9.html](livre_9.html), [appendice_actions.json](data/appendice_actions.json), [appendice_glossaire.json](data/appendice_glossaire.json).*

### 1.1 Refonte du Livre V `§20.4` — Manœuvres de Déplacement Forcé (`2 PA`)
- L'ancienne section *« Les Effets Spéciaux Déclenchés sur Épreuve »* (9 effets gratuits sur différentiel `Δ`) est remplacée par ***« Manœuvres de Déplacement Forcé & Renvois d'Effets »*** :
  - **Bousculer / Repousser (`2 PA`)** : au contact — Face-à-Face *Athlétisme vs Athlétisme* (ou *Défense Corporelle*). Sur réussite (`Δ ≥ 0`), repousse la cible d'`1` case (`+1` case supplémentaire si `Δ ≥ 3`). Contre un obstacle infranchissable : état *Déstabilisé*. **Ne se déclenche jamais gratuitement sur une attaque normale.**
  - **Attirer (`2 PA`)** : nécessite grappin, chaîne ou télékinésie — Face-à-Face *Athlétisme* (ou *Magie de l'Esprit*) vs *Athlétisme* (ou *Esquive*). Sur réussite (`Δ ≥ 0`), attire la cible d'`1` case (`+1` case supplémentaire si `Δ ≥ 3`).
- Tous les autres effets tactiques renvoient désormais sans doublon vers leur règle dédiée :
  - *Refoulement* (Arts Martiaux) ➔ `§22.2` (reste gratuit sur choc `> CON×2`, critique ou `Δ ≥ 4`) ;
  - *Assommer*, *Désarmer*, *Saignement ciblé* ➔ Visée Niveau 2 (`§22.1`) et spécialisations Livre III `§14` ;
  - *Dégâts résiduels* (*Saignement*, *En Feu*, *Empoisonné*, *Asphyxie*) ➔ `Livre VI §24.2` (`−1/tour`) ;
  - *Contrôle mental* ➔ Télépathie (`Livre IV §19.4`) ; *Lutte, traînée, étranglement* ➔ `§20.8`.

### 1.2 Synchronisation des Appendices A & B
- **Appendice A (`appendice_actions.json`, `101` actions maintenues)** : `5` entrées mises à jour (`effet-repousser-refoulement` passe à `2 PA (Bousculer §20.4) / 0 PA (Refoulement §22.2)` en *Manœuvre / Action* ; `effet-attirer` passe à `2 PA` en *Manœuvre / Action* ; précisions de renvoi sur `effet-desarmer`, `effet-assommer`, `effet-saigner-bruler`).
- **Appendice B (`appendice_glossaire.json`, `300 ➔ 296` termes)** :
  - `4` entrées redondantes supprimées : `effet-saigner`, `effet-bruler`, `effet-posseder`, `effet-desintegrer`.
  - `6` entrées renommées/actualisées : `effets-speciaux-epreuve` ➔ *Manœuvres de Déplacement Forcé (§20.4)*, `effet-repousser` ➔ *Bousculer / Repousser*, `effet-attirer`, `effet-desarmer`, `effet-assommer`, `effet-refoulement`.

### 1.3 Livre IV `§19.4` — Compendium Arcanes (`330` spécialisations, `0` `hex`, `2 PA` par défaut)
- **Effectif corrigé et complété (`329 ➔ 330`)** : `nb_specialisations` passe de `46` à **`330`**, avec ajout d'une 33e spécialisation en *Feu* : **`Souffle Ardent`** (parente : *Incinération Pyrocinétique*, `Offensif` — cône de `2` cases devant soi, `4` dégâts de brûlure + *En Feu*).
- **3 renommages anti-homonymie** : `Pistage ➔ Pistage Tellurique`, `Sens de la Pierre ➔ Écho Lithique` (évite la collision avec le don Derek), `Anticipation ➔ Prescience Tactique`.
- **Coût en PA explicite en tête de compendium** : activer une spécialisation arcanique en combat coûte **`2 PA` par défaut** (action principale standard ; **`0 PA`** pour un effet passif pur ; coûts réduits ou renforcés `1 PA` / `3 PA` explicités sur la ligne).
- **Normalisation géométrique & filière** : toutes les occurrences d'`hex` (`37 ➔ 0`) sont converties en `cases` / `cases contiguës` ; `parente` ajoutée proprement dans `specialisations_par_nom` ; réécriture harmonisée au tutoiement.

### 1.4 Livre IV `§19.2–§19.3` & `livre_9.html` — Atelier Modulaire `XP = PA`
- **Module offensif `1 pt`** : *Cible Débalancée (1 tour)* ➔ **`La cible subit −1 à sa prochaine défense (parade ou esquive avant ton prochain tour)`** (cumulable ; aligné également dans le calculateur Chap. 34 de [`livre_9.html`](livre_9.html)).
- **Module défensif/utilitaire `3 pts`** : *+2 Résistance Résiduelle* ➔ **`Réduction Résiduelle (−2 dégâts résiduels) pendant 1 tour`** (durée cumulable ; aligné dans [`livre_9.html`](livre_9.html)).
- **Exemple *Écrasement de Matière* (`3 XP / 3 PA`) corrigé** : n'utilise plus *2 Dégâts Absolus* (qui coûtait 3 pts par créature à `1` case), mais **`+2 Dégâts directs (1 pt)` + `Cible À Terre (2 pts)`** (`1 + 2 = 3 XP / 3 PA`).

---

## 2. Commit `5146306` — Livre III Passe P1 (`RP-181`, `RP-182`, `RP-136`, `RP-183`, `RP-015`)

*Date : 2026-11-25 20:24 · Tickets clôturés : `RP-181`, `RP-182`, `RP-136`, `RP-183` (`RP-015` annoté `in_progress`).*  
*Fichiers clés : [livre_3.json](data/livre_3.json), [js/fabrique.js](js/fabrique.js), [appendice_actions.json](data/appendice_actions.json), `content/livre_3/`.*

### 2.1 Synchronisation Arbres `§12–§13` ↔ Compendium `§14` (`166 ➔ 198` spécialisations)
- **Ajout de `35` nouvelles spécialisations** (et `3` remplacements/renommages) pour doter toutes les compétences jusque-là orphelines et éclater les chaînes regroupées :
  - *Athlétisme* (scissions) : **`Second Souffle`** & **`Cœur de Marathon`** (sous *Course d'Endurance*) ; **`Escalade Assurée`** & **`Nage de Combat`** (sous *Franchissement*).
  - *Artisanat / Connaissance* : **`Fabriquer`**, **`Forger`**, **`Sculpter`**, **`Enseigner`** ; renommage *Acier de Maître d'Hybris* ➔ **`Acier de Maître`**.
  - *Écoute* : **`Perception des bruits lointains`**, **`Écho acoustique`**, **`Auscultation`**.
  - *Nature & Survie* : **`Traque`**, **`Orientation`**, **`Botanique sauvage`**, **`Construction d'abris`**.
  - *Acrobatie* : **`Saut acrobatique`**, **`Équilibre`**, **`Se balancer`**, **`Chute contrôlée`**.
  - *Conduite & Pilotage* : **`Véhicules terrestres`**, **`Pods arcaniques`**, **`Vaisseaux`**, **`Montures`**.
  - *Subterfuge Manuel* : **`Crochetage mécanique`**, **`Vol à la tire`**, **`Sabotage`**.
  - *Académie* : **`Histoire des mondes`**, **`Linguistique`**, **`Économie impériale`**.
  - *Premiers Soins* : **`Stabilisation d'urgence`**, **`Pose de garrot`**, **`Soins de terrain`**.
  - *Mains Nues* : *Paume de Brum'korath* ➔ **`Paume Brise-Roc`** (`RP-136`).
  - *Maniement d'Arme* : *Fissure Causal* ➔ **`Fissure Causale`**.
- Normalisation des liens `parente` directs sur `44` entrées du JSON.

### 2.2 Révisions mécaniques de spécialisations (`RP-182`, `RP-136`) & Sync Appendice A
| Spécialisation | Avant (`6f497f7`) | Maintenant (`5146306`) |
| :--- | :--- | :--- |
| **Bouclier Vivant** | Interpose son corps pour un allié adjacent | *Prise de coup* possible jusqu'à **`2` cases** (pas de côté inclus) ; la Posture Préventive **n'impose plus `-1` attaque** |
| **Interposition Réflexe** | Coûte `1 PA` au lieu de `2` | S'interposer coûte **`0 PA`** (+ Réaction Libre) et donne **`+1` à la parade** lors de l'interposition |
| **Course d'Endurance / Franchissement** | Portaient *Second Souffle*, *Cœur de Marathon*, *Escalade Assurée*, *Nage de Combat* en bloc | Réduites à leur effet de palier 1 ; les 4 évolutions deviennent des spécialisations à part entière |
| **Poussée Cardiovasculaire** | Force `1 PA` contre `1` Essoufflement | Plafond d'Essoufflement **`CON + 1`**, et `1×/combat` **`+1 PA` d'urgence sans PE** |
| **Redline Stabilisé** | Plafond d'essoufflement sans *Ralenti* | `1×/combat`, encaisse **`1` PE au-delà du plafond** sans tomber *À Terre* ni *Inconscient* |
| **Clairvoyance du Vétéran** | Immunité embuscades et « attaques de dos » | Immunité surprise/embuscades : agit dès le tour d'embuscade et **garde la Réaction Libre** |
| **Sphère de Résonance** | Immunité attaques de dos & embuscades (`3` cases) | Dans un rayon de **`3` cases**, détection automatique des cibles *Dissimulées/Cachées* **sans dépenser `1 PA` d'*Observation*** |
| **Perception Panoramique 360°** | Mention vague de « bonus de surprise permanent » | Immunité embuscades, **`+2` *Observation active*** vs *Caché*/couvert, **ignore malus d'attaques multiples / encerclement** |
| **Pistolet & Tir Rapide** | *À Terre* sur « `5` ou `6` au dé » | *À Terre* sur **`Δ ≥ 3` ou critique** |
| **Analyse de Faille** | Prochain coup ignore l'**Encaissement** adverse | Prochain coup ignore l'**armure** adverse (+ sync `analyser-faille` en Appendice A) |
| **Feinte Oratoire** | `1 PA` : « ennemi tourne le dos jusqu'au tour suivant » | `1 PA` Face-à-Face *Communication vs Intuition* : cible distraite, **`−2` à sa prochaine défense** (+ sync `action-sociale` en Appendice A) |
| **Paralysie Psychologique** | *Paralysé 1 tour* si « Volonté < CHA » | *Paralysé 1 tour* sur **`Δ ≥ 3` ou `INS < CHA`** (+ sync `intimider-menacer` en Appendice A) |
| **Acier de Maître** *(ex-d'Hybris)* | Critique dès `11` ou `12` | Critique dès **`max − 1`** sur le dé utilisé (+ sync `artisanat-forger-affuter` en Appendice A) |

### 2.3 Fabrique de Techniques Maison (`§15` & `js/fabrique.js`, `RP-183`)
- **Pièce `D7` (*Immunité embuscade*)** : suppression de « et dos » ➔ garde la Réaction Libre en embuscade et ignore le bonus d'attaque cachée.
- **Pièce `U12` (*Réserve d'urgence*, ex-*Force contre essoufflement*)** : devient **`1×/combat`, `+2 PA` d'action contre `1` point d'Essoufflement** (au lieu de `1 PA` contre `1` PE).
- **Cadence unifiée** : les ensembles **`C3` (Réaction)** et **`C4` (Opportunité)** passent de `1/round` à **`1/cycle`** dans `livre_3.json`, `content/livre_3/`, `js/fabrique.js` et `appendice_actions.json`.
- **Cohérence & ton** : correction des renvois obsolètes `Livre IV §20 ➔ Livre IV §19` dans `C3` et `M6`, et passage intégral des chapitres `11–15` au **tutoiement**.

---

## 3. Commit `2a867a6` — Audits F2 & F5, Livre II `§8` & Livre III `§12.1` (`RP-241`, `RP-244`, `RP-131`, `RP-180`)

*Date : 2026-11-25 19:59 · Tickets clôturés : `RP-241`, `RP-244`, `RP-131`, `RP-180`.*  
*Fichiers clés : [livre_1.json](data/livre_1.json), [livre_2.json](data/livre_2.json), [livre_3.json](data/livre_3.json), [livre_5.json](data/livre_5.json), [js/livre_1.js](js/livre_1.js), [js/tools.js](js/tools.js), [livre_9.html](livre_9.html), [fiche_pj.html](fiche_pj.html), Appendices A & B.*

### 3.1 Audit F2 — `8` Attributs Majeurs, Budget Unique & PA `Min(attributs > 0)` (`RP-241`)
- **Suppression du bonus gratuit `+2` pts d'Éveil** : la **Magie (`MAG`)** redevient le **8e Attribut Majeur** dans le même budget de départ unique :
  - **Héros PJ** : Piliers `5` et `4` + **`12` pts** sur les **`6` autres caractéristiques** (`21` pts total au lieu de `21 / 23`).
  - **PNJ d'Élite / Vétéran** : Piliers `4` et `4` + **`12` pts** sur les **`6` autres** (`20` pts total au lieu de `20 / 22`).
  - **PNJ Sbire / Fretin** : **`11` pts** sur l'ensemble des **`8` caractéristiques** (`11` pts total au lieu de `11 / 13`).
- **Règles de `MAG` à la création** : seule caractéristique pouvant être laissée à **`0`** (personnage non éveillé) ; **`Magie` autorisée en Pilier** (`5` ou `4`).
- **Nouvelle formule PA** : **`Max(AGI, INT) + RAP + Min(attributs > 0)`** (`Magie` exclue si elle vaut `0`, et hors Perceptions).
- **Mise à jour code & outils** : `CORE7 ➔ CORE8` dans [`js/tools.js`](js/tools.js) (suppression des budgets `14 / 13` d'Éveil), suppression de la colonne *Option Éveillé* dans [`js/livre_1.js`](js/livre_1.js), et synchronisation des libellés dans [`livre_9.html`](livre_9.html), [`fiche_pj.html`](fiche_pj.html) et `appendice_glossaire.json` (`8 attributs`).

### 3.2 Audit F5 — Plafond Anti-Farm `5 + niveau` Sans Conversion (`RP-244`)
- La réserve d'XP lié d'une compétence est désormais plafonnée à **`5 + niveau de base`** (au lieu de `niveau + 2`).
- **Suppression de la conversion forcée `2:1` en XP libre** : une fois la réserve au plafond (ex. `8 XP` pour base `3`), aucun XP lié supplémentaire ne s'accumule tant que le joueur n'en dépense pas à `1:1` (dans la compétence ou son attribut lié) ou `×2` ailleurs.
- Synchronisé dans `livre_1.json` (`content/livre_1/sections/chap-5/bloc-08.md`), `appendice_actions.json` (`entrainer-compagnons`) et `appendice_glossaire.json` (`plafond-anti-farm`, `xp-libre`).

### 3.3 Livre II `§8` — Environnements, Dangers & Nuit (`RP-131`)
- **Climat léger (`§8.1`)** : précise que le don Nain *Sang des Forges* ignore le climat chaud léger (en plus d'*Endurance Coloniale* Humain).
- **Chutes (`§8.2`)** : l'exemple ajoute le statut *Souffrant* en cas d'échec d'Encaissement aux jambes.
- **Souterrain (`§8.3`)** : *Gaz inflammables* applique **`4` dégâts de zone + `En Feu 2`** (*Esquive* seuil `4` pour éviter l'embrasement si abri possible) ; *Éboulement* inflige `3` dégâts contondants (`−2` via *Mémoire des Cages* Nain) avec un **seuil MJ gradué (`4` Moyen, `7` Difficile, `10` Très Difficile)** au lieu du seuil fixe `4`.
- **Terrains difficiles (`§8.4`)** : ajout du cumul avec *Accroupi*, ajout de *Pas des Crêtes* (Nain, escarpé/neige/glace), remplacement de *Sprint* par *Course*, et bascule des épreuves de nage/escalade agitée de *Force* vers **`Athlétisme`**.
- **Nuit & Lumière (`§8.6`)** : structuration complète dans `livre_2.json` (`nuit_lumiere`) ; *Nuit noire* étend le **`−1` à tous les jets visuels** (tirs, *Observation*, visée, conduite sans phares) ; *Lampe allumée* borne la suppression du malus jusqu'à **`10` cases** (`Livre VIII §32.4`).

### 3.4 Livre III `§12.1` — Suppression des Jets de « Force Brute » (`RP-180`)
- Plus aucun jet de Force brute isolé (alignement avec la Règle d'Or `§1` et Livre V) :
  - *Courir / Nager / Escalader* ➔ épreuve d'**`Athlétisme`** (`livre_3`, `livre_5`, `appendice_actions` `nager-escalader`) ;
  - *Pousser / Soulever* ➔ épreuve d'**`Athlétisme`** (effort explosif) ou d'**`Endurance Physique`** (effort prolongé), opposition en **`Athlétisme vs Athlétisme`** (`appendice_actions` `pousser-soulever`, `ouvrir-fermer-porte`) ;
  - *Agripper* ➔ renvoi direct vers la **`Lutte` (`Livre V §20.8`)** (`2 PA`, *Athlétisme / Mains Nues vs Athlétisme / Défense Corporelle*) ;
  - *Porter une Armure Lourde* ➔ **suppression du seuil de Force minimal** et du jet toutes les 10 minutes ; seul l'**`Encombrement` (`+1 PA` Prot 3–5, `+2 PA` Prot 6–7, réduit de `1` via *Technicien-Guerrier*)** et *Endurance Physique* en marche prolongée s'appliquent.

---

## 4. Commit `76e6dcf` — Appendice C Objets, Board VTT Web & Infra (`RP-250`, `RP-251`, `RP-252`, `VTT-001..045`)

*Date : 2026-11-25 03:56 · Commit intermédiaire entre la baseline `6f497f7` et la série des 3 audits `sysrp`.*

- **Appendice C : Compendium interactif des Objets (`RP-250`)** :
  - Nouveaux fichiers : [`appendice_objets.html`](appendice_objets.html), [`data/appendice_objets.json`](data/appendice_objets.json), [`js/appendice_objets.js`](js/appendice_objets.js) et générateur [`tools/build_appendice_objets.py`](tools/build_appendice_objets.py) (source canonique : `data/livre_8.json`).
  - Contient **`140` fiches d'objets** normalisées en code `DLPH` (`49` clés JSON par fiche) réparties en **`10` catégories (`C.1–C.10`)** : *C.1 Mêlée Lames & Fines (14)*, *C.2 Choc, Haches & Capture (9)*, *C.3 Trait & Jet (8)*, *C.4 Balistique à Poudre (10)*, *C.5 Laser & Confinement Plasma (14)*, *C.6 Armes Lourdes, Grenades & Explosifs (18)*, *C.7 Munitions, Nytharite & Implants (17)*, *C.8 Armures, Boucliers & Champs de Force (21)*, *C.9 Pharmacopée, Soins & Rations (17)*, *C.10 Terrain & Survie (12)*.
  - Inclut `13` propriétés, **`6` presets de kits de départ `[E0–E5]`**, comparateur côte-à-côte, calculateur d'encombrement/budget de kit, intégration globale au menu (`A.III` via [`tools/build_nav.py`](tools/build_nav.py)) et contrôle d'ancres (`#cat-*`, `#obj-*`) dans [`tools/check_liens.py`](tools/check_liens.py).
- **Tableau Kanban `board-vtt-web` (*VTT Web & Salle Joueurs*)** :
  - Ajouté dans [`data/kanban_rp.json`](data/kanban_rp.json) avec **`45` cartes (`VTT-001` à `VTT-045`)** dont `5` déjà marquées `done` (`VTT-001` chat SSE & tables, `VTT-002` voix WebRTC mesh, `VTT-003` dock chat global, `VTT-004` hub WebSocket `/ws/vtt`, `VTT-005` filtre serveur de visibilité `360°` anti-maphack) et `40` en `todo`.
- **Tunnel `cloudflared`, SEO & Admin locale (`RP-251`, `RP-252`)** :
  - [`start_all.sh`](start_all.sh) migre de `ngrok` vers **`cloudflared`** (`https://killtime.studio`).
  - Ajout de [`robots.txt`](robots.txt) et [`sitemap.xml`](sitemap.xml) (+ MIME `.txt`/`.xml` dans [`users/server.js`](users/server.js)).
  - Correctif admin locale (`127.0.0.1:3100`) dans [`users/server.js`](users/server.js) pour servir `/css/themes.css`, `/js/theme-init.js`, `/js/theme.js`, `/favicon.ico` et `/data/archives/`.

---

## 5. État des écarts résiduels depuis `6f497f7`

Suivi par rapport au tableau des écarts recensés dans la baseline `6f497f7` :

- **Corrigés entre `6f497f7` et `6fd7604` :**
  - `livre_4.json` : `nb_specialisations` recalé (`46 ➔ 330`), module *Cible Débalancée* remplacé par *−1 à la prochaine défense*, exemple *Écrasement de Matière* recomposé, `37` mentions d'`hex` remplacées par `cases`.
  - `livre_3.json` & `js/fabrique.js` : renvois `Livre IV §20 ➔ §19`, `1/round ➔ 1/cycle`, synchronisation complète des arbres `§12–§13` avec le compendium `§14` (`166 ➔ 198`) et suppression des jets de Force brute en `§12.1`.
  - `livre_5.json` : suppression des 9 effets gratuits de `§20.4` au profit de *Bousculer* / *Attirer* (`2 PA`) et renvois propres vers `§22.1`, `§22.2`, `§24.2`, `§20.8`, `L.IV §19.4`.
- **Toujours présents au commit `6fd7604` :**
  - `livre_1.json` (`especes_par_nom["Vardien / Frontaliers"].dons[0]` & `progression.xp_total`) et `tools.js` `giftMechanics` (`+1 ESS` Vardien don A, absence du `-1 CAP` Cléien don A).
  - `livre_3` (`calcul_rang_base` citant `§10.1` ; *Premiers Soins* sous l'arbre `INT` / *Artisanat* notant `SAG, INT`), `livre_4.json` (clé orpheline `stades_neuronaux` ; module *Ralenti* disant encore « double PA » sans `+1 PA/case`), `livre_8.json` (*Mains Nues* `1D` au `§30`, `Aveuglé` dans `grenades_tactiques`), `livre_9.html`/`tools.js` (`initDiceRoller` `"Impact dévié"` sur `Δ=0`, texte chap. 35/37 & pied `11 LIVRES`), `livre_11.json` (tech `1–6`), `livre_12.json` (`nb_creatures: 14` vs UI `15+` et fiches Bestiaire historiques non encore réauditées).
