# Mécaniques Détaillées du Système RP — Baseline au commit `6f497f7`

> **Commit de référence (inclus) :** `6f497f72c92b6d89dc7840fa7dbb92c28a947cdf` (2026-11-23, Codex v2.4)  
> **Objet :** Document de référence exhaustif (« baseline ») des mécaniques du Système RP (Livres I à XII, Livre IX Outils Web, Appendice A, Appendice B) à cet état exact, destiné à servir de point de comparaison pour le document *What's New* des commits subséquents.  
> **Sources lues :** Exclusivement depuis l'arbre git du commit `6f497f72c92b6d89dc7840fa7dbb92c28a947cdf`.

---

## Périmètre & Jalons d'Audit Inclus dans cette Baseline

À la date du commit `6f497f72c92b6d89dc7840fa7dbb92c28a947cdf`, le Codex comprend :
- **Livres I à VIII** (`livre_1` à `livre_8`) et **Livres X à XII** (`livre_10` à `livre_12`) ;
- **Livre IX** (`livre_9.html`, `fiche_pj.html`, `js/tools.js`) : 6 outils web interactifs (sans `data/livre_9.json`) ;
- **Appendice A** (`data/appendice_actions.json`, v2.4, 2026-11-22) : **101 actions** réparties en 7 catégories (`A.1` à `A.7`) ;
- **Appendice B** (`data/appendice_glossaire.json`, v2.4, 2026-11-22) : **300 termes** indexés sur 9 domaines (`B.1` à `B.9`).

### Audits et révisions majeures déjà intégrés jusqu'à `6f497f7`
1. **Livre V & Livre VI (1er check mécanique complet)** : 19 états préjudiciables/bénéfiques/tactiques (ajout du 19e état *Affaibli*), *Ralenti* à `+1 PA/case` (Option A), *Se cacher* & *Discrétion*, *Surpris* vs *Tour d'Embuscade* (1×/combat), *Observation* seuil 4 (auto à `1d12`), *Choc Traumatique* par 6 zones (`> CON × 2`), *Seuil Létal* (`CON × 5`) sans PV avec arbre *Mort / Inconscient / Sombrer / Dernier Souffle* et *Agonie*, *Point de Miracle*, *Repos sûr* (6/8/10 h, veille de soignant, délai 12 h), *Premiers Soins* Diff 6, 5 états des objets.
2. **Livre VI §25.4 & Livre XII §53** : Générateur modulaire de pathologies (Indice de Gravité `I–V`, 1 à 6 stades hybrides, Virulence sur l'échelle §7.1) et décalage de numérotation chap. 27–53 → 26–52 (puis chap. 53 Pathologies).
3. **Livre VII (1er check mécanique complet)** : Époques `[E0–E5]` fixées par le MJ, **36 Enfances** (6 tables d6 `E0–E5`), **48 Métiers** (6 tables d8 `E0–E5`), Événement marquant optionnel (`d12`), §29.2 Ancrage purement narratif (0 mécanique).
4. **Livre VIII (Refonte Arsenal chap. 30–32)** : Code universel `DLPH`, portage et budgets de départ `[E0–E5]`, armes conventionnelles, laser, lourdes, explosifs, arcanotech/implants, blindages/encombrement, boucliers/champs de force, pharmacopée et matériel.
5. **Audit F1 (Plafond d'effort)** : `Total = dé + effort − malus` avec `effort ≤ 1 + entraînements` (incluant PA, PE, *Action en Progression*, *Survolté* et *En transe*).
6. **Audit F3 (7 Attributs & Bagage)** : Retrait d'*Érudition (ÉRU)* des caractéristiques majeures (8 → **7 attributs majeurs** : `FOR, AGI, CON, RAP, INT, CHA, INS` + `Magie` séparée) ; **Bagage** fixé une fois à la création entre **3 et 5** entraînements (`0 XP`, non farmable).
7. **Audit F4 (Anti-farm XP)** : Jets à enjeu obligatoire (cible passive / `0 PA` / `Défense 0` / *Paralysé* / *Inconscient* = `0 case` ; défense à `0 PA` sans mise = `0 case` ; 2e jet même action même scène = `0 case`), *drills* entre compagnons quotaés, zéro fiat MJ de veille, plafond de réserve d'XP lié (`≤ niveau + 2`, surplus converti 2:1 en XP libre).
8. **Audit Défis (`6f497f7`)** : Gel des coches individuelles pendant les étapes d'un Défi Hors-Combat (§10), attribution unique de la récompense finale `(Étapes Réussies × 2) − Rattrapages` (min 2 si validé), **échec total = 0 case** (cases d'étapes perdues).

---

## Sommaire

1. [Partie 1 — Fondations, Création & Progression (Livre I, §1–§5 & Fiche PJ)](#partie-1--fondations-création--progression-livre-i-15--fiche-pj)
2. [Partie 2 — Dés, Difficultés, Environnements, Critiques & Défis (Livre II, §6–§10)](#partie-2--dés-difficultés-environnements-critiques--défis-livre-ii-610)
3. [Partie 3 — Arbres de Compétences & Fabrique de Techniques (Livre III, §11–§15)](#partie-3--arbres-de-compétences--fabrique-de-techniques-livre-iii-1115)
4. [Partie 4 — Cinquième Force & Magie (Livre IV, §16–§19)](#partie-4--cinquième-force--magie-livre-iv-1619)
5. [Partie 5 — Combat Tactique & Véhicules (Livre V, §20–§22)](#partie-5--combat-tactique--véhicules-livre-v-2022)
6. [Partie 6 — Santé, États, Soins & Pathologies (Livre VI, §23–§25)](#partie-6--santé-états-soins--pathologies-livre-vi-2325)
7. [Partie 7 — Historiques & Arsenal (Livres VII & VIII, §26–§32)](#partie-7--historiques--arsenal-livres-vii--viii-2632)
8. [Partie 8 — Temps & Causalité (Livre X, §39–§42)](#partie-8--temps--causalité-livre-x-3942)
9. [Partie 9 — Cadre Univers, Bestiaire, Pathologies, Outils & Appendices (Livres XI, XII, IX & Appendices A–B)](#partie-9--cadre-univers-bestiaire-pathologies-outils--appendices-livres-xi-xii-ix--appendices-ab)

---

## Partie 1 — Fondations, Création & Progression (Livre I, §1–§5 & Fiche PJ)

### §1. Principes Fondamentaux & Lexique
- **Tour de 10 secondes (`10 s`)** : Dès qu'un affrontement ou une scène tendue démarre, le temps se découpe en tours de 10 s. Chacun agit à son rang d'initiative et dépense ses **Points d'Action (PA)** ; il peut tout dépenser pendant son tour ou en conserver en réserve pour réagir (parades, esquives, prises de coup).
- **Règle d'Or (Test de Compétence)** : On ne lance jamais de jet sur une caractéristique brute isolée. Tout jet teste une **compétence** précise. Aucun modificateur de caractéristique n'est ajouté au dé ; les attributs fixent uniquement le rang de base (et donc le dé lancé).
  - Formule universelle : `Total = Dé de compétence + effort injecté (PA/PE, plafonné à 1 + entraînements) − malus cumulés`.
- **Règle des Dons** : Sauf précision contraire, un don racial s'active gratuitement (`0 PA`, pas d'action) à tout moment dès que ses conditions sont réunies.
- **Lexique fondamental (`7` entrées clés)** :
  - `PA` (*Points d'Action*) : réserve d'énergie par tour, rechargée au maximum **au début du tour personnel** de la créature, sans cumul d'un tour à l'autre.
  - `EC` (*Épreuve de Caractéristique*) : test contre un seuil fixe du MJ ou de l'environnement.
  - `VS` (*Épreuve Opposée / Face-à-Face*) : confrontation directe entre deux compétences (ex. *Maniement d'Arme* vs *Esquive* ou *Défense Corporelle*).
  - `ESS` (*Essoufflement*) : conversion vitale de réserve d'endurance en PA d'urgence (`1 pt = 1 PA`).
  - `CAP` (*Seuil d'Encaissement*) : `CON × 2` par zone anatomique.
  - `MAX` (*Maximum / Seuil Létal*) : `CON × 5` en dégâts cumulés totaux.
  - `PM` (*Points de Miracle*) : réserve de destin héroïque (`1 PM` au départ pour tout PJ, `0` pour un sbire).

### §2. Cosmologie, Échelles & Espèces Jouables

#### 2.1 Deux Échelles Universelles
1. **Échelle Technologique** (6 paliers représentés dans `echelles[0].niveaux`, pied indiqué `"Niveaux 0 à 6"`, correspondant aux codes `[E0–E5]` des Livres VII–VIII) :
   - **Primitive (`E0`)** : pierre, os, bronze.
   - **Médiévale (`E1`)** : acier, armures métalliques, armes de trait.
   - **Moderne (`E2`)** : balistique à poudre, puces, réseaux.
   - **Avancée (`E3`)** : terraformation, cybernétique, voyages interstellaires.
   - **Très Avancée (`E4`)** : sphères de Dyson, téléportation, cuves de régénération arcanique.
   - **Démiurgique (`E5`)** : manipulation directe du continuum espace-temps.
2. **Affinité Magique des Espèces** (5 degrés de perméabilité à la 5e Force) : *Impossible* → *Limitée* → *Normale* → *Inhérente* → *Démiurgique*.

#### 2.2 Les 7 Espèces Jouables
Chaque joueur choisit **1 don racial définitif parmi les 5** de son espèce (activation gratuite à `0 PA` sauf mention contraire).

| Espèce | Origine | Tech | Affinité Magie | Sens min (Vision/Ouïe) | Les 5 Dons raciaux (résumé mécanique exact) |
| :--- | :--- | :--- | :--- | :---: | :--- |
| **Humain** | Multiples Mondes | `E0 ➔ E4` | Impossible à Limitée | `3` | **A. Endurance Coloniale** : `+1` réserve d'Essoufflement (au-delà de `CON`) ; ignore le climat léger (§8.1).<br>**B. Touche-à-tout** : `+1 Bagage` à la création (compétence non-magique).<br>**C. Instinct de Survie** : `+2` Nature & Survie ; `+1` Observation.<br>**D. Réseau Colonial** : `+2` Communication en zone civilisée ; 1 contact par station/colonie.<br>**E. Sang-Froid** : `1×/session`, annule `1` Essoufflement ou ignore un échec de moral/peur. |
| **Derek** | Planète Hybris | Avancée d'Hybris | Limitée | `3` | **A. Ossature Dense** : `+2` Encaissement ; `-1` dégâts contondants et chutes.<br>**B. Sang des Profondeurs** : vision nocturne ; `-2` dégâts de froid, résiduels froid `-1`, `+2` CON vs froid ; *Aveugle* 1 tour en entrant dans une lumière vive (pas si déjà présent).<br>**C. Maître-Fondeur** : `+2` Forger (Artisanat) ; armure forgée par soi `+1` protection (une seule portée à la fois).<br>**D. Sens de la Pierre** : sent danger roche à ~30 m (épreuve Observation : gaz inflammable, plafond/pilier instable, poches) ; `+2` Nature & Survie en souterrain.<br>**E. Gueule de Granit** : `+2` CON vs gaz et poisons des mines ; apnée `4 × CON` tours. |
| **Taurien** | Planète Hybris | Avancée | Normale | `3` | **A. Accord Sylvestre** : `-1 PA` et `+1` aux jets de Magie Élémentale.<br>**B. Grâce Féline** : `+1` Esquive en milieu naturel ; `-2` dégâts de chute.<br>**C. Sens des Flux** : sent eau/vie/magie à ~50 m (Observation) ; `+2` Observation en nature, `-2` en milieu pollué/artificiel.<br>**D. Pas Feuillu** : `+2` Discrétion en nature ; ignore terrains difficiles naturels.<br>**E. Sève Vive** : `1×/jour`, au contact du sol naturel, purge `1` Essoufflement ou stoppe `1` résiduel de valeur `≤ 2`. |
| **Cléien** | Planète Cléia | Impériale Avancée | Inhérente | `4` | **A. Sang-Chaud de Cléia** : `-2` dégâts feu, résiduel *En Feu* `-1`, `+2` CON vs chaleur ; `-1` Encaissement, `+1` dégât froid subi.<br>**B. Résonance Arcanique** : `+1` jets de Magie ; chaque réussite en Magie coche `2` cases de progression ; réussite critique en Magie coûte `+1` Essoufflement.<br>**C. Fille du désert** : `+2` Nature & Survie en terrain aride ; ignore terrains difficiles sableux ; `-2` aux jets en milieu clos et artificiel.<br>**D. Frugalité Cleyane** : besoins eau/nourriture ÷2 ; `+2` CON vs privation.<br>**E. Silhouette Mineure** : `+1` Discrétion et Esquive vs créatures taille humaine ou plus ; `-1` dégât infligé à Mains Nues et armes de contact. |
| **Mikyai** | Karkjiue | Impériale Avancée | Impossible à Limitée | `3` | **A. Écailles de Karkjiue** : `+1` Armure naturelle vs tranchant/perforant ; `+2` Encaissement ; `+1` dégât froid subi, `-2` CON vs froid.<br>**B. Régénération Karkjiue** : `1×/combat`, réduit de `1` la valeur d'un résiduel posé ; stoppe sans soin un *Saignement* de valeur `1`.<br>**C. Technicien-Guerrier** : `+2` Armes Lourdes (Ballistique) ; `+2` Ingénierie pour réparer matériel impérial ; ignore `1` pt d'encombrement d'armure.<br>**D. Armes Naturelles** : griffes/crocs (`2` dégâts, jamais désarmé) ; `+1` Athlétisme pour grimper.<br>**E. Queue Préhensile** : 3e main utilitaire (`≤ 5 kg`, jamais d'attaque) ; `+1` Athlétisme pour grimper et nager. |
| **Vardien / Frontaliers** | Vardis & Bordures | Hybride Récup | Variable | `3` | **A. Trempe des Bordures** : `+2` Intimidation (intimider et résister) ; `+1` épreuves de moral et peur.<br>**B. Gueule des Mines** : `+2` Système Immunitaire (toxines, gaz, radiations faibles) ; réduit de `1` la valeur des résiduels *Empoisonné* et *Asphyxie*.<br>**C. Système Récup** : `+2` bricoler/récupérer/saboter ; réparations à moitié prix ; `-1` Communication en milieu impérial.<br>**D. Ne Lâche Rien** : `1×/session`, au Seuil Létal (`CON × 5`) sur un coup `≤ Encaissement`, reste conscient et agit 1 tour en *Dernier Souffle* sans son coût d'Essoufflement.<br>**E. Contrebandier** : `+2` Discrétion (falsification, passage douanes) ; 1 contact marché noir par zone. |
| **Nain** | Montagnes Naines de l'Ouest | Vapeur ➔ Magi-Tech | Limitée à Normale | `3` | **A. Mémoire des Cages** : `-2` dégâts d'explosions et d'éboulements ; `+2` CON vs projections et ondes de choc.<br>**B. Sang des Forges** : `+2` CON vs chaleur et fumées ; `-1` dégât de feu subi ; ignore le climat chaud léger (§8.1).<br>**C. Oreille de la Surfréquence** : sent nytharite en résonance et moteurs actifs à ~50 m (Observation) ; `+2` Observation pour repérer arcanotech dissimulée, en panne ou piégée.<br>**D. Pas des Crêtes** : `+2` Athlétisme (grimper/montagne) ; ignore terrains difficiles escarpés, neige et glace.<br>**E. Longue Mémoire** : `+2` Académie (histoire, métallurgie, généalogies) ; 1 contact érudit/artisan par cité naine. |

> *Note d'état `6f497f7` :* Dans `mecaniques.especes_par_nom["Vardien / Frontaliers"]`, le don A indique encore `+1 Essoufflement max` alors que le tableau principal `especes` et les fiches indiquent `+1 aux épreuves de moral et de peur`.

### §3. Création de Personnage en 6 Étapes

1. **Étape 1 & 2 — Identité, Origine & Historique** : Nom, âge, sexe, allure, monde d'origine, espèce + 1 don racial, puis Historique complet selon le **Livre VII** (1 Enfance `d6` + 1 Métier `d8` + Événement marquant `d12` optionnel + Ancrage).
2. **Étape 3 — Profil & Budget de Points de Départ** :

| Profil | Piliers Majeurs | Points Secondaires | Total (non éveillé / éveillé) | Limites à la création | Option Éveillé (Magie) |
| :--- | :--- | :---: | :---: | :--- | :--- |
| **Héros PJ** | `5` et `4` sur 2 attributs choisis | `12` pts sur les 5 autres attributs | `21` pts / `23` pts | Min `1`, Max `3` par attribut secondaire (Plafond en jeu : `10`) | `+2` pts (`14` pts) avec au moins `1` pt placé en Magie |
| **PNJ d'Élite / Vétéran** | `4` et `4` sur 2 attributs choisis | `12` pts sur les 5 autres attributs | `20` pts / `22` pts | Min `1`, Max `3` par attribut secondaire (Plafond en jeu : `10`) | `+2` pts (`14` pts) avec au moins `1` pt en Magie |
| **PNJ Sbire / Fretin** | Aucun pilier d'office | `11` pts sur les 7 attributs | `11` pts / `13` pts | Min `1`, Max `3` par attribut (jamais `0`) | `+2` pts (`13` pts) avec au moins `1` pt en Magie |

3. **Étape 4 — Les 7 Attributs Majeurs (Post-Audit F3)** :
   - **Physiques (4)** : `FOR` (Force), `AGI` (Agilité), `CON` (Constitution), `RAP` (Rapidité d'exécution).
   - **Mentaux (3)** : `INT` (Intelligence), `CHA` (Charisme), `INS` (Instinct / Sagesse).
   - *Note Audit F3 :* L'ancien 8e attribut *Érudition (ÉRU)* a été retiré des caractéristiques majeures (8 → 7 attributs). Les attributs naturels ne peuvent jamais être à `0` (min `1`) et culminent au **plafond absolu de `10`**.
4. **Étape 5 — Perceptions & Atouts Spéciaux** :
   - **Vision** : `1d6` à la création, relevé au `sens_min` de l'espèce (`3`, ou `4` pour Cléien).
   - **Ouïe** : `1d6` à la création, relevé au `sens_min` de l'espèce (`3`, ou `4` pour Cléien).
   - **Magie** : `0` par défaut. Si éveillé à la création : budget secondaire augmenté de `+2` (`14` pour Héros/Élite, `13` pour Sbire) et au moins `1` point investi dans `Magie`.
   - **Points de Miracle (`PM`)** : `1 PM` pour tout PJ (`0` pour un sbire).
5. **Étape 6 — Bagage (Premières Compétences Entraînées)** :
   - **Base :** `3` entraînements de départ.
   - **Bonus possibles (max total `5`) :** `+1` via le don Humain *Touche-à-tout* (compétence non-magique) et `+1` si l'historique Livre VII (Enfance `d6` + Métier `d8`) est tiré au **hasard pur** sans choix manuel.
   - **Effet :** Chaque entraînement augmente le dé de la compétence de `+1` palier et élève son **Effort max** (`1 + entraînements`). Plafond `3` par compétence au départ (`+3` déblocables quand le dé atteint `2d6`, max `6`).
   - **Coût & Immutabilité :** Coûte `0 XP` (exclu de l'*XP Total*). Fixé une seule fois à la création — aucune montée d'attribut en jeu (pas même `INT`) n'offre d'entraînement gratuit.

### §4. Statistiques Dérivées, PA & Survie

| Statistique | Formule exacte | Règles associées |
| :--- | :--- | :--- |
| **Points d'Action (`PA`)** | `Max(AGI, INT) + RAP + Min(7 attributs)` | À la création, `Min(7 attributs)` correspond toujours à `Min(secondaires)`. Rechargé intégralement au début du tour personnel, sans cumul d'un tour à l'autre. |
| **Quota d'Attaque** | `1` attaque / tour (`2` dès le palier `2d6+`) | L'arme/compétence offensive utilisée doit avoir atteint `2d6` ou plus pour débloquer la 2e attaque. |
| **Plafond d'Effort par jet** | `1 + entraînements` | Inclut ensemble : PA misés, PE convertis, *Action en Progression*, *Survolté* et *En transe*. (`0` entr. = `+1`, `1` = `+2`, `2` = `+3`, `3` = `+4`, `6` élite `2d6+` = `+7`). |
| **Seuil d'Encaissement (`CAP`)** | `CON × 2` | Par zone anatomique (6 zones). Si cumul zone `> CON × 2` : jet gratuit (`0 PA`) d'*Endurance Physique* Diff `3 + (dégâts − CON×2)` ou effet de zone (§23). |
| **Seuil / Maximum Létal (`MAX`)** | `CON × 5` | Somme de tous les dégâts cumulés. À l'atteinte : arbre de résolution *Mort / Inconscient / Sombrer / Dernier Souffle* selon si le dernier coup dépasse `CAP` (§23.2). |
| **Réserve d'Essoufflement (`ESS` / `PE`)** | `CON` (`CON + 1` avec *Endurance Coloniale*) | `1 PE = 1 PA` d'urgence quand la réserve de PA est vide. Ne disparaît pas tout seul : `1 PA` à son tour ou en réaction libre (sans attaque) efface `1 PE`. Dépasser le plafond = *À Terre* et *Inconscient* jusqu'à la fin du prochain tour. |
| **Initiative** | `Max(RAP, AGI, INT)` converti en palier de dé (§6) | Tirée une fois avant le round 1 ; ordre fixe pour tout le combat. **Départage d'égalité :** 1) Héros avant PNJ, 2) plus gros dé d'initiative, 3) choix des joueurs (entre héros) ou du MJ (entre PNJ). |

### §5. Progression & Dépense d'XP (Post-Audit F4)
- **Piste de progression** : Chaque compétence possède une piste de longueur égale à son **rang de base** (`1` à `10` cases). Piste pleine → remise à zéro + **`+1 XP lié`** dans la réserve de cette compétence.
- **Conditions strictes d'enjeu (Audit F4 — Anti-farm XP)** :
  - `1` réussite à enjeu = `1` case cochée (même sur un critique ; `0` case sur un échec).
  - En Face-à-Face : seul le vainqueur du duel coche `1` case.
  - **Exclusions (`0 case`) :** attaque contre cible à `Défense 0`, consentante, *Paralysé* ou *Inconscient* ; défense réflexe à `0 PA` sans mise ; 2e jet pour la même action dans la même scène ; jet sans enjeu (hors adversaire actif ou hors seuil `≥ base` assorti d'une conséquence réelle à l'échec).
- **Entraînement entre compagnons (*Drills*)** : `1` jet = `4 heures` d'exercices intensifs ; max **`1 case / jour / binôme`** ; max **`1 XP lié / semaine`** via drills (toutes compétences confondues) ; le formateur doit avoir un **niveau `≥` élève** ; seul l'élève coche sur réussite contre un seuil `≥ base`. Zéro fiat MJ de veille.
- **Plafond anti-farm de réserve d'XP lié** : La réserve d'XP lié d'une compétence ne peut excéder son niveau de base de plus de 2 (`réserve ≤ niveau + 2`). À `niveau + 2`, les `2` points en excès sont convertis en **`1 XP libre`** (taux `2:1`) et la réserve retombe au niveau de la compétence.
- **Priorité de paiement & Tarifs** :
  - Priorité : `XP lié de la compétence/attribut` (`1:1`) → `XP libre` (`1:1`) → `XP lié d'une autre réserve` (**tarif croisé `×2`**).

| Amélioration | Coût Lié / Libre | Coût Croisé | Règles et plafonds |
| :--- | :--- | :--- | :--- |
| **+1 Entraînement de Compétence** | `5 XP` | `10 XP` | `+1` palier de dé permanent et `+1` Effort max. Plafond `3`, puis `+3` déblocables à `2d6` (max `6`). |
| **Débloquer une Spécialisation** | `5 XP` | `10 XP` | Une par une ; arborescence avec prérequis (indentation `↳`). |
| **Façonner un Pouvoir Arcanique** | `X XP = X PA` | `2X XP` | Modules dans sa voie d'affinité : `1 XP = 1 PA` ; modules d'une autre voie : `2X XP`. |
| **Augmenter une Caractéristique** | `(N + 1) XP` | `2 × (N + 1) XP` | Coûte le score d'arrivée (ex. `4 → 5` = `5 XP`, `9 → 10` = `10 XP`). Plafond `10`. Recalcule PA, CAP, MAX, ESS et pistes (0 entraînement offert). |

- **XP Total** : Somme de toute l'XP investie dans les améliorations du personnage au **prix normal** (hors surcharges `×2`, hors réserves en poche, hors Bagage de création qui vaut `0 XP`). *Note d'état `6f497f7` : `progression.xp_total` dans le JSON indique « surcharges ×2 incluses » tandis que le texte HTML de `§5.4` précise « chaque amélioration y compte à son prix normal (même celles que vous avez payées double en croisé) ».*

---

## Partie 2 — Dés, Difficultés, Environnements, Critiques & Défis (Livre II, §6–§10)

### §6. L'Échelle des Dés & Crans
- **Séquence officielle (11 paliers)** : `d2 ➔ d4 ➔ d6 ➔ d8 ➔ d10 ➔ d12 ➔ 2d6 ➔ 2d8 ➔ 2d10 ➔ 2d12 ➔ 2d12+10`.
- **Conversion Caractéristique / Rang de base → Dé sans entraînement** :
  - `1–2` : `+0` cran → `1d2`
  - `3–4` : `+1` cran → `1d4`
  - `5–6` : `+2` crans → `1d6`
  - `7–8` : `+3` crans → `1d8`
  - `9–10` : `+4` crans → `1d10` (plafond naturel sans entraînement)
- **Entraînements** : Chaque entraînement ajoute `+1` cran à partir du rang de base. Avec base `9–10` (`1d10`) + `6` entraînements : `1d10 → 1d12 → 2d6 → 2d8 → 2d10 → 2d12 → 2d12+10`.
- **Chances en Face-à-Face (repères)** : `1d4 vs 1d2` = `83 %` ; `1d6 vs 1d4` = `75 %` ; `1d8 vs 1d6` = `64 %` ; `1d10 vs 1d8` = `61 %` ; `1d12 vs 1d6` = `83,5 %`.

### §7. Épreuves, Seuils de Difficulté (0 à 34) & Face-à-Face

#### 7.1 Échelle des Seuils & Réussites Automatiques
Si la compétence atteint le palier de dé requis, l'épreuve de seuil inférieur ou égal est réussie automatiquement sans lancer les dés :

| Seuil | Degré de Difficulté | Tirage repère | Réussite Auto dès |
| :---: | :--- | :--- | :--- |
| `0` | Réussite Automatique | Aucun jet | Tous |
| `1–2` | Très Facile | `1d4 - 3` | `d6` et + |
| `3` | Facile | `1d4 - 1` | `d8` et + |
| `4–5` | Difficile / Très Difficile | `1d4 + 1` | `d12` et + |
| `6–7` | Extrêmement Difficile | `1d4 + 3` | `2d8` et + |
| `8–9` | Formidable | `1d6 + 4` | `2d10` et + |
| `10–11` | Héroïque | `1d6 + 5` | `2d12` et + |
| `12–15` | Prodigieux | `1d8 + 6` | `2d12+10` |
| `16–19` | Légendaire | `1d10 + 8` | — |
| `20–22` | Mythique | `1d12 + 10` | — |
| `34` | Plafond Cosmique | `2d12 + 10` | — |

*(Table `reussites_auto` détaillée par dé : `d4` → `0` ; `d6` → `0–2` ; `d8` → `0–3` ; `d10` → `0–4` ; `d12` & `2d6` → `0–5` ; `2d8` → `0–7` ; `2d10` → `0–9` ; `2d12` → `0–11` ; `2d12+10` → `0–16`.)*

#### 7.2 Résolution du Face-à-Face (Épreuve Opposée)
1. **Mises simultanées secrètes** : L'attaquant et le défenseur placent en secret leurs jetons d'effort (`PA` et `PE`, somme totale plafonnée à `1 + entraînements` de la compétence engagée), puis révèlent et lancent simultanément. *(Variante sans jetons : déclaration ouverte de l'attaquant puis du défenseur).*
2. **Défense à sec de PA** : Sans PA, le défenseur peut lancer son dé avec une pénalité réflexe de **`-2`**, ou choisir la **Défense Passive (`0 PA`, score = `0`)**. Son armure s'applique toujours.
3. **Différentiel Net (`Δ = Total attaquant − Total défenseur`)** :
   - **`Δ < 0` :** Échec complet (coup esquivé ou paré).
   - **`Δ = 0` :** **Touche superficielle / Égalité gagnante pour l'initiateur** — pas de dégâts de base, mais les effets spéciaux de l'attaque ou l'action initiée (prise de lutte, libération, détection, tromperie) s'appliquent. L'adversaire doit battre strictement l'initiateur pour l'arrêter.
   - **`Δ > 0` :** **Touche franche** — `Dégâts bruts = Dégâts de base de l'arme/coup + Δ` (avant armure et comparaison avec `Encaissement`).

### §8. Environnements, Terrains & Cumul des Malus
- **Règle de cumul des malus (4 couches, zéro double compte)** :
  1. **Climat / Météo / Nuit** : **1 seul malus par jet** (le plus fort l'emporte ; si météo `P4` opaque applique *Aveugle*, seul *Aveugle* s'applique sans `-2` météo). Pour la distance de vue, le plafond le plus restrictif l'emporte.
  2. **Traits / Milieux raciaux (§8.5)** : s'additionnent séparément.
  3. **Malus tactiques** (couvert, visée, *Canon Entravé* compté 1 seule fois — Livre V §21.3 / §22).
  4. **Tous les états actifs** (Livre VI §24).
- **8.1 Climat & Météo (4 paliers)** :
  - Climat léger (`-1` sans bridage de vue : vent modéré, froid mordant, chaleur lourde ; ignoré par *Endurance Coloniale* et *Sang des Forges* pour chaleur légère) ; canicule / vent violent (`-2`, sans bridage).
  - Météo à visibilité réduite : **`P1` Négligeable** (`0`, vue illimitée : pluie légère/modérée, neige, brume) ; **`P2` Gênant** (`-1`, vue `8` cases max : pluie forte, blizzard, brouillard) ; **`P3` Violent** (`-2`, vue `4` cases max : trombe, tempête, voile blanc, brouillard dense, tempête de sable) ; **`P4` Opaque** (état *Aveugle* : voile blanc total, brouillard opaque, sable extrême).
- **8.2 Chutes** : `H` cases de hauteur = **`H - 2` dégâts** (min `0`), **armure ignorée**, comparés à `Encaissement` (aux jambes). Réductions avant comparaison : *Ossature Dense* (Derek) `-1`, *Grâce Féline* (Taurien) `-2`.
- **8.3 Souterrain** : *Sens de la Pierre* (`+2` Nature & Survie, sent ~30 m). Dangers types : gaz inflammables (`4` dégâts de zone + risque *En Feu*), éboulement (`3` dégâts contondants, Esquive/Athlétisme SD `4` pour éviter), poches (gaz/eau/minerai).
- **8.4 Terrains difficiles** : Coût `+1 PA/case` (soit **`2 PA/case`** de base), cumulable avec *À Terre* / *Déstabilisé* (`+1`), *Ralenti* (`+1`), *Encombrement* (`max 4 PA/case` sans armure). Sprint et Charge interdits. Nage en eau profonde et escalade : `2 PA/case` (+ épreuve FOR si agitée/raide). Ignoré par *Franchissement*, *Pas Feuillu* (naturels), *Fille du désert* (sableux).
- **8.5 Les 6 Milieux** : *Naturel*, *Artificiel*, *Pollué*, *Aride*, *Souterrain*, *Clos*.
- **8.6 Nuit & Lumière** :
  - **Nuit claire** : vue `8` cases, `0` malus tir, `-1` Observation au-delà de `4` cases.
  - **Nuit noire** : vue `4` cases, `-1` aux tirs sans lampe ni vision thermique.
  - **Lampe allumée** : annule bridage et malus de nuit pour soi et cibles éclairées, mais trahit la position à toute ligne de mire directe (furtivité brisée).
  - **Vision thermique** (spé ou lunettes) : ignore la nuit sans se trahir, perce fumée et murs légers à demi-portée.
  - **Vision nocturne** (Derek) : ignore nuit, mais *Aveugle* 1 tour à l'entrée brutale en lumière vive.
  - **Éblouissement** (flash/grenade/éclat) : applique *Aveugle* 1 tour (`2` tours à `≤ 3` cases). Le *Masque Filtrant* protège des flashs/gaz.

### §9. Les Critiques & Table Universelle (1 à 24)
- **Déclenchement** :
  - **Réussite Critique** : Obtenir le **maximum naturel** sur au moins un dé du jet (ex. un `6` sur `1d6` ou sur l'un des dés de `2d6`).
  - **Échec Critique** : Obtenir un **`1` naturel** — **impossible dès que la compétence a au moins `1` entraînement** ou lance plusieurs dés (`2d6+`).
  - **Face-à-Face** : Même si son total perd le duel, un **max naturel critique toujours** (conséquence uniquement, sans dégâts ni effet de spé si pas de touche). Double max = les deux critiquent, l'initiateur d'abord.
- **Jet de Conséquence** : Relancer son palier de dé complet et lire la table au palier égal ou **inférieur le plus proche** (`7→6`, `11→10`, `13–15→12`, `17–19→16`, `21–23→20`, `25+→24`). En **Échec Critique**, lire la table à l'envers (bonus→malus, cible↔lanceur).

| Résultat | Effet Offensif | Effet Défensif | Effet Social | Effet Utilitaire |
| :---: | :--- | :--- | :--- | :--- |
| **1** | Cible *À Terre* | Attaquant *À Terre* | Interlocuteur déstabilisé | Tâche sans bruit |
| **2** | Cible repoussée d'`1` case | Pas de côté : repli gratuit d'`1` case sans réaction | Ascendant psychologique | Gain d'élan cinétique |
| **3** | Petite brèche : ignore `1` pt d'armure | `+1` Défense bonus (épreuve en cours) | Confiance immédiate | Précision chirurgicale |
| **4** | `+1 EC` attaquant (épreuve en cours) | `+1 EC` cible (épreuve en cours) | `+1 EC` persuasion (épreuve en cours) | `+1 EC` tâche (épreuve en cours) |
| **5** | **`+2` Dégâts directs** | **`+2` Défense bonus** (épreuve en cours) | L'interlocuteur cède | Économie de matériel |
| **6** | Cible *Étourdie* (`-1 EC`) jusqu'à fin prochain tour | Attaquant *Étourdi* (`-1 EC`) jusqu'à fin prochain tour | Interlocuteur sidéré | Découverte inattendue |
| **8** | **Récupère `1 PA`** | **Récupère `1 PA`** | **Récupère `1 PA`** | **Récupère `1 PA`** |
| **9** | **Récupère `1` Essoufflement** | **Récupère `1` Essoufflement** | Regain de sang-froid | Regain de vigueur |
| **10** | Cible *Déstabilisée* (`-2 EC`) jusqu'à fin prochain tour | Attaquant *Déstabilisé* jusqu'à fin prochain tour | Opposition neutralisée | Ralentissement temporel (`1 à 5` tours) |
| **12** | Cible *Ralentie* (`×2` PA, `+1 PA/case`) jusqu'à fin prochain tour | Attaquant *Ralenti* jusqu'à fin prochain tour | Effondrement de l'argument | Composant réparé/sauvé |
| **16** | **Récupère `2 PA`** | **Récupère `2 PA`** | **Récupère `2 PA`** | **Récupère `2 PA`** |
| **20** | **`3` Dégâts Résiduels** | Contre-attaque auto (équivalent frappe d'opportunité `0 PA`, sans consommer la réaction du round) | Ralliement inconditionnel | Survoltage arcanique total |
| **24** | Cible *Inconsciente* jusqu'à fin prochain tour | Attaquant *Inconscient* jusqu'à fin prochain tour | Soumission absolue | Succès mirifique critique |

### §10. Les Défis Hors-Combat (Post-Audit `6f497f7`)
- **Structure** : Suite de **`3` à `7` étapes** (compétence imposée ou proposée par les joueurs), avec `0` à **`3` tentatives de rattrapage** possibles accordées par le MJ.
- **Économie des PA et PE** : Les PA ne se rechargent pas entre les étapes ! Chaque mise d'effort (PA ou PE, toujours plafonnée à `1 + entraînements` par jet) est déduite de la réserve globale pour toute la durée du défi, et l'Essoufflement pris reste jusqu'au dénouement.
- **Règles de progression (Audit `6f497f7` — Anti-double comptage)** :
  1. **Progression gelée pendant le défi** : les jets d'étapes individuels ne cochent **aucune** case de progression (exception à Livre I §5.1).
  2. **Récompense finale unique** : seule la récompense finale est répartie entre les compétences réellement mobilisées si le défi réussit :
     `Récompense (en cases) = (Étapes Réussies × 2) − Nombre de Rattrapages` (avec un **minimum de `2` cases** si le défi est validé).
  3. **Échec total = `0`** : si le défi échoue globalement, **0 case** n'est gagnée et toutes les réussites d'étapes intermédiaires sont perdues.

---

## Partie 3 — Arbres de Compétences & Fabrique de Techniques (Livre III, §11–§15)

### §11. Architecture & Typologie des Compétences
- **Calcul du Rang de Base** : Moyenne des attributs liés **arrondie au supérieur** (`ceil`). Si un seul attribut est lié, sa valeur directe s'applique.
  - Ex. : `Maniement d'Arme (FOR, AGI)` avec `FOR 5, AGI 4` → `(5+4)/2 = 4,5 → 5` (`1d6`, piste de `5` cases).
- **Les 5 catégories fonctionnelles d'action** : *Offensif*, *Défensif*, *Utilitaire*, *Social*, *Hybride*.
- **Défense Non-Exclusive** :
  - Parer avec une compétence 100 % défensive (*Défense Corporelle*, *Esquive*) : plein dé, sans malus.
  - Parer avec une compétence non exclusivement défensive (*Maniement d'Arme*, *Mains Nues*, *Armes Perçantes*) : **`-1` niveau de dé** sur la parade, **sauf** si le personnage possède la spécialisation dédiée (*Épée*, *Arts Martiaux*, *Bloquer*, *Escrime*, etc.) qui annule ce malus.

### §12–§13. Inventaire des 6 Arbres (`27` Compétences de Base)

| Arbre | Compétence (`27` au total) | Attributs liés | Type | Spécialisations & actions notables |
| :--- | :--- | :--- | :--- | :--- |
| **FOR** (4) | **Mains Nues** | `FOR, AGI` | Off, Déf, Util | *Arts Martiaux* (→ *Enchaînement Fluide* → *Frappe Téléportée* → *Déphasage Cinétique* / *Onde Tellurique* → *Brise-Blindage* → *Paume de Brum'korath* / *Balayage Rotatif* → *Clé d'Articulation* → *Rupture Ligamentaire*), *Perforation de Silicate* (→ *Fracture Sismique* → *Cœur d'Adamas*). |
| **FOR** | **Maniement d'Arme** | `FOR, AGI` | Off, Déf | *Épée* (→ *Riposte Éclair*, *Garde Haute Impériale*, *Lame Miroir*, *Dôme de Parade*…), *Fente de Rupture* (→ *Pointe Chirurgicale* → *Transpercement Traversant* → *Fissure Causal*), *Bloquer* (commune avec Déf. Corporelle), *Armes Contondantes / Marteau de Guerre*, *Hache de Guerre*, *Zweihander*. |
| **FOR** | **Athlétisme** | `CON, FOR, AGI` | Util | *Course d'Endurance* (→ *Second Souffle* → *Cœur de Marathon*), *Franchissement* (→ *Escalade Assurée* → *Nage de Combat*). Nage/escalade sans Franchissement = `2 PA/case` (+ jet FOR si difficile). |
| **FOR** | **Défense Corporelle** | `FOR` | Déf | *Bloquer* (→ *Mur de Bouclier* → *Heurt de Bouclier* → *Forteresse Impénétrable*), *Bouclier Vivant* (→ *Interposition Réflexe* → *Dévier le Calibre* → *Mur Inébranlable*). Armures Lourdes : en dessous de la FOR requise, jet FOR toutes les 10 min ou épuisement. |
| **AGI** (7) | **Acrobatie** | `AGI, RAP` | Util, Déf | Saut acrobatique, Équilibre, Se balancer, Chute contrôlée. |
| **AGI** | **Discrétion** | `AGI` ou `(AGI, INT)` | Util | *Déplacement silencieux*, *Camouflage*, *Ombres*. |
| **AGI** | **Esquive** | `AGI, RAP` ou `INS` | Déf Pur | *Roulade de Décrochage* (→ *Reprise d'Appui* → *Ombre Évasive* → *Pas Déphasé* / *Esquive Réactive* → *Contre-Poussée*), *Acrobatie d'Évitement* (→ *Saut Périlleux* → *Vrille Balistique*). |
| **AGI** | **Armes Perçantes & Fines** | `AGI` | Off, Déf | *Escrime / Rapière*, *Frappe Jugulaire* (→ *Hémorragie Profonde*, *Asphyxie Sanguine*, *Tranche-Artère Silencieux*), *Arme de Hast* (→ *Arrêt de Charge*, *Mur de Piques*, *Phalange d'Acier*, *Fauchage Latéral*, *Crochet de Hallebarde*). |
| **AGI** | **Ballistique / Tir** | `AGI` ou `(AGI, Vision)` | Off | *Pistolet & Tir Rapide* (→ *Dégainé Réflexe*, *Akimbo Assaut*, *Trait d'Orichalque*, *Balle dans le Genou*, *Tir de Neutralisation*), *Fusil de Précision*, *Grenadier d'Assaut*, *Tir à l'Arc*, *Armes Lourdes (Canons)*. |
| **AGI** | **Conduite & Pilotage** | `AGI, RAP` | Util | Véhicules terrestres, Pods arcaniques, Vaisseaux, Montures. |
| **AGI** | **Subterfuge Manuel** | `AGI, INT` | Util | Crochetage mécanique, Vol à la tire, Sabotage. |
| **CON** (3) | **Endurance Physique** | `CON, FOR` | Util, Déf | *Condition de Fer* (→ *Dur à Cuire* → *Mur de Chair*), *Trempe de Fer* (→ *Ignorer la Douleur* → *Esprit de Granit*). |
| **CON** | **Cardio** | `CON` | Util | *Seconde Respiration* (→ *Rechargement Anaérobie* → *Poumons d'Acier* → *Cœur de Berserker*), *Poussée Cardiovasculaire* (→ *Redline Stabilisé* → *Injection d'Adrénaline*). |
| **CON** | **Système Immunitaire** | `CON` | Déf | *Immunité Toxique* (→ *Neutralisation des Poisons* → *Filtrage des Gaz de Guerre* → *Sang Antidote Universel*). |
| **INT** (5) | **Académie** | `INT` | Util | Histoire des mondes, Linguistique, Économie impériale. |
| **INT** | **Premiers Soins** | `INS` | Util | Stabilisation d'urgence, Pose de garrot, Soins de terrain. |
| **INT** | **Médecine Avancée** | `INT, INS` | Util | *Chirurgie* (→ *Diagnostic Vital*, *Suture Réflexe*, *Greffe d'Urgence*), *Apothicaire de Guerre* (→ *Stimulant Neuro-Accélérateur*, *Sérum Antidote*, *Cocktail Panacée*). |
| **INT** | **Ingénierie & Arcanotech** | `INT, AGI` | Util | *Surfréquence Nytharite* (→ *Stabilisateur de Flux* → *Décharge Harmonique* → *Résonance Infinie*), Réparation réacteurs, Piratage. |
| **INT** | **Penser / Stratégie** | `INT` | Util, Déf | *Analyse de Faille* (→ *Tir Coordonné* → *Prédiction de Retraite* → *Échec et Mat*). |
| **CHA** (3) | **Communication** | `CHA, INT` | Social | *Tromper* (→ *Feinte Oratoire*, *Faux Signalement*, *Mascarade Totale*), *Négocier* (→ *Marché Noir Impérial*, *Cessez-le-Feu*, *Rachat d'Allégeance*), *Charmer*. |
| **CHA** | **Intimidation** | `CHA` ou `(CHA, FOR)` | Social, Off | *Intimider* (→ *Rugissement de Terreur*, *Regard de Prédateur*, *Paralysie Psychologique*). |
| **CHA** | **Leadership** | `CHA, INT` | Social, Util | *Mener (Commandement)* (→ *En avant !*, *Tenir la Ligne !*, *Galvanisation des Héros*). |
| **INS/SAG** (5) | **Observation** | `Vision, Ouïe` | Util, Déf | *Vigilance Réflexe* (→ *Œil de Lynx* → *Détection Thermique* → *Perception Panoramique 360°*). |
| **INS/SAG** | **Écoute** | `Ouïe` | Util | Perception des bruits lointains, Écho acoustique, Auscultation. |
| **INS/SAG** | **Intuition** | `INT, INS` | Util, Déf | *Sens du Danger* (→ *Premiers Réflexes* → *Clairvoyance du Vétéran*), *Instinct du Chasseur* (→ *Pistage* → *Piégeur*), *Sphère de Résonance* (→ *Écho Myotatique* → *Champ Prédictif* → *Omniprésence Synaptique*). |
| **INS/SAG** | **Nature & Survie** | `INS, INT` | Util | Traque, Orientation, Botanique sauvage, Construction d'abris. |
| **INS/SAG** | **Artisanat / Connaissance** | `SAG, INT` | Util | *Affûtage de Précision* (→ *Fil Monomoléculaire* → *Blindage Trempé* → *Acier de Maître d'Hybris*). |

> *Note d'état `6f497f7` :* `Premiers Soins` est classée dans l'arbre `INT` tout en ayant `INS` comme attribut lié ; `Artisanat / Connaissance` note `SAG, INT` (`SAG` = `INS`). Les textes de `livre_3` conservent deux références résiduelles « `Livre I §5` / `Livre III §10.1` » dans `calcul_rang_base` JSON alors que le chapitre est `§11.1`.

### §14. Compendium des Spécialisations (`166` entrées JSON)
- Tarif uniforme : **`5 XP`** dans la banque liée (ou XP libre), **`10 XP`** en croisé.
- Prérequis hiérarchiques : chaque niveau indenté (`↳` / `→`) exige l'achat de sa spécialisation parente.
- Toutes les spécialisations de combat confèrent de fait la **parade sans malus** avec l'arme/approche correspondante.
- Quelques repères de puissance notables du compendium : *Lame Miroir* (pare les tirs laser au contact sur `Δ ≥ 0`), *Armes Lourdes (Canons)* (`+100` dégâts de masse vs structures/véhicules), *Suture Réflexe* (Premiers Soins `3 → 2 PA`), *Vigilance Réflexe* (immunité surprise/embuscade), *Perception Panoramique 360°* (immunité embuscades + bonus surprise permanent), *Sens du Danger* (ajoute `INS` à la base d'Initiative).

### §15. Fabrique de Techniques Maison (Puzzle `4–6 XP`)
Système alternatif/complémentaire au compendium, basé sur la **Règle des Deux Monnaies** : on additionne **Ensemble `◆` + Pièces (1 à 3) + Modificateurs** en XP à la création (`4–6 XP` standard ; **`7 XP et +` = Technique Signature**, 1 seule max par personnage avec accord MJ), et on paie à table uniquement les **PA actifs** (`≤ 6 PA` par activation).

#### Ensembles `◆` (Châssis — `1` par technique, lié à une compétence entraînée)
| ID | Cadre | Coût XP | Coût PA | Règle d'activation |
| :--- | :--- | :---: | :---: | :--- |
| **`C0`** | Passif conditionnel | `3 XP` | `0 PA` strict | Toujours actif quand la condition tient. Exige au moins `1` carte Condition gratuite. Aucune pièce/finition à PA autorisée. |
| **`C2`** | Action principale | `1 XP` | `1 PA` + PA pièces | Pendant son tour. |
| **`C3`** | Réaction | `1 XP` | `+2 PA` + PA pièces | Hors de son tour, `1/round` max. |
| **`C4`** | Opportunité | `2 XP` | `0 PA` | `1/round` max, déclencheur étroit obligatoire (`Δ ≥ 2`, critique, esquive…). |

#### Les 9 Règles d'Assemblage & Incompatibilités
1. `1` seul Ensemble `◆` lié à une compétence entraînée.
2. `1` à `3` pièces max (`3` logements ; les `8` cartes Conditions — contact, arme en main, immobile, critique, `Δ ≥ 2`, `1/combat`, essoufflement, entraînement — sont gratuites et hors logements).
3. Budget : `4–6 XP` standard ; `7+ XP` = Signature (`1/perso`, accord MJ).
4. **Activation `≤ 6 PA` max** (ensemble inclus). `C0` = `0 PA` strict.
5. **1 seul bonus chiffré par jet** (`O8` et `O9` ne se cumulent pas : le meilleur s'applique).
6. Chaque effet porte son seuil propre (`sur touche`, `Δ ≥ 0`, `Δ ≥ 3`, `succès net`, `parade réussie`, `critique`, `condition`). En attaque ratée, rien ne part.
7. Paliers internes = variantes au choix (non empilables) : `U7` (`-5/-10/-15` dégâts), `O28` (`2/3/4` résiduels), `U11` (`3 ESS` / `4 ESS + 1 PA` / `2×/combat`). Prérequis spécifiques : `D8` exige `D1` ou `D3` ; `O18` exige `3` pièces de la même arme.
8. **Incompatibilités strictes** : `O7` (Absolus) `⊕` `O15` (Seconde cible) et `O7` `⊕` Zone `M3` ; `D6` (Garde totale) `⊕` `D1, D2, D3, D4`.
9. Contraintes cadres : `C3` `+2 PA` (`1/round`), `C4` `0 PA` (`1/round` + déclencheur étroit), `C0` `≥ 1` prérequis.

#### Catalogue des 43 Pièces (`O`, `D`, `U`, `S`) & 6 Modificateurs (`M1–M7`)
- **Pièces Offensives (16)** :
  - `O1` `+1 dégât, cette attaque` (`2 XP, 1 PA`, cumul `×5` ; `-1 XP`/cumul si condition critique).
  - `O7` `Dégâts Absolus` (`3 XP, 0 PA`, sur critique/condition, `⊕ O15` & Zone).
  - `O8` `+1 au jet, cette attaque` (`2 XP, 0 PA`, cumul `×5`, `-1 XP`/cumul si critique).
  - `O9` `+1 à ton prochain jet` (`1 XP, 1 PA`, cumul `×5`, durée `+1 tour` possible).
  - `O13` `Repousse 1 case` (`2 XP, 0 PA`, cumul, succès net, `-1 XP`/case si critique).
  - `O15` `Seconde cible (+1 dégât auto)` (`2 XP, 1 PA`, cumul `×5`, `⊕ O7` & Zone).
  - `O18` `Critique élargi (max et max-1)` (`4 XP, 0 PA`, arme liée, préreq. `3` pièces même arme).
  - `O20` `Ralenti` (`1 XP, 1 PA`, `Δ ≥ 0`, durée cumulable), `O21` `Étourdi` (`1 XP, 1 PA`), `O22` `Déstabilisé` (`1 XP, 1 PA`), `O23` `À Terre` (`2 XP, 1 PA`), `O24` `Immobilisé` (`2 XP, 1 PA`), `O25` `Sonné` (`2 XP, 1 PA`), `O26` `Paralysé` (`3 XP, 1 PA`), `O27` `Paniqué` (`2 XP, 1 PA`), `O28` `Saignement` (`2 XP, 1 PA` pour `2` résiduels ; `+1 XP` pour `3`, `+2 XP` pour `4`).
- **Pièces Défensives (9)** : `D1` `Parade sans malus` (`2 XP, 0 PA`), `D2` `Absorbe 3 / dégâts ÷2` (`3 XP, 0 PA`), `D3` `+2 Encaissement` (`3 XP, 0 PA`), `D4` `+1 armure naturelle permanente` (`4 XP, 0 PA`), `D5` `Immunité 1 statut` (`3 XP, 0 PA`), `D6` `Immunité totale de garde` (`5 XP, 0 PA`, `⊕ D1–D4`), `D7` `Immunité embuscade & dos` (`3 XP, 0 PA`), `D8` `Ignore un malus` (`2 XP, 0 PA`, préreq. `D1/D3`, cumulable), `D9` `Repousse 1 case sur parade` (`2 XP, 0 PA`, cumulable).
- **Pièces Utilitaires (12)** : `U1` `-1 PA action précise` (`3 XP, 0 PA`), `U2` `+1 PA de réserve tour suivant` (`3 XP, 0 PA`), `U3` `+2 PA escouade / +3 PA mvt` (`4 XP, 2 PA`), `U4` `Mouvement gratuit 1 case` (`2 XP, 0 PA`), `U5` `Téléport 2 cases sur esquive Δ≥2` (`4 XP, 0 PA`), `U6` `Ignore terrain difficile` (`2 XP, 0 PA`), `U7` `Soin + purge` (`2 XP, 2 PA` pour `4–5` dégâts + `1` état ; `+2 XP` = `10` dégâts, `+3 XP` = `15` dégâts + immunité 3 tours), `U8` `Lecture vitale` (`2 XP, 1 PA`), `U9` `Révèle mises 1/combat` (`3 XP, 1 PA`), `U10` `Initiative réglée` (`2 XP, 0 PA`), `U11` `Souffle maîtrisé` (`2 XP, 0 PA`), `U12` `Force 1 PA contre 1 ESS` (`2 XP, -1* PA`).
- **Pièces Sociales (6)** : `S1` `+1 épreuve sociale` (`1 XP, 0 PA`, cumul `×3`), `S2` `Fuite d'un sbire Δ≥3` (`3 XP, 1 PA`), `S3` `Conversion / cessez-le-feu 1/combat` (`4 XP, 2 PA`), `S4` `Prix et marché noir` (`2 XP, 0 PA`), `S5` `Frappe silencieuse` (`2 XP, 0 PA`), `S6` `Ordre simple` (`3 XP, 1 PA`).
- **Modificateurs / Finitions (6)** : `M1` Portée `2 à 3` cases (`+2 XP/case, +0 PA`, offensifs `O7, O8, O9, O15` uniquement), `M3` Forme de zone cône/ligne/rayon `+1` (`+2 XP, +1 PA`), `M4` Durée `+1 tour` (`+1 XP, +1 PA`, max 2 crans), `M5` Condition étroite (`-1 XP`/cumul sur `O1, O8, O13, O15` si critique lié, min `1 XP`/pièce), `M6` Hybride 2 familles (`+1 XP, +0 PA`), `M7` Hors-compétence (`×2 XP`).

---

## Partie 4 — Cinquième Force & Magie (Livre IV, §16–§19)

### §16–§17. Physique de la Cinquième Force & Moteurs Magiques
- **Organe interne & Résonance (§16.1)** : L'incantation sans machine repose sur un organe interne (sous le sternum) ; les individus nés sans organe utilisent la magitek.
- **Vulnérabilités & Contre-mesures** :
  1. **Saturation / Échauffement Arcanique** : Pousser trop sans relâcher génère une surchauffe encaissée par le lanceur ou le moteur.
  2. **Champs d'Interférence Destructifs (Anti-Magie)** : Brouillent la résonance ; l'organe décroche (sort éteint) et les moteurs magiques calent net.
- **Moteur Magique à Nytharite (§17 — 4 composants)** :
  1. *Chambre de Résonance du Cœur* (cristal de nytharite taillé : génération primaire sans carburant).
  2. *Stabilisateurs de Champ* (anneaux directeurs : vecteur et contrôle).
  3. *Nœuds d'Infusion Élémentaire* (Air pour portance, Feu pour poussée).
  4. *Recyclage d'Énergie Dynamique* (rendement quasi-infini, autonomie de plusieurs mois).

### §18. Voies Arcaniques, Portée & Liens Télépathiques
- **Les 3 Compétences de Magie (Voies)** :
  - **Magie Élémentale** (*Feu, Air, Eau, Terre* — Offensif, Défensif, Utilitaire).
  - **Magie Primale** (*Vie, Création, Destruction* — Offensif, Défensif, Utilitaire).
  - **Magie de l'Esprit (Psyché)** (*Télépathie, Télékinésie, Clairsentience* — Offensif, Défensif, Social, Utilitaire).
  - **Rang de base des 3 voies (§18.1)** : `ceil((MAG + INT) / 2)`. Chaque entraînement augmente le dé d'`1` cran (et la 1re spécialisation d'une branche exige `1` entraînement dans la compétence de base).
- **Portée des Sorts & Malus de Distance (tranché RP-137)** :
  - **Portée sans malus :** `Portée (cases) = MAG + (MAG × Entraînements dans la compétence de base)` (le premier rang est offert ; les spécialisations comme Feu ne comptent pas dans `Entraînements`).
  - **Au-delà de la portée :** **`-1` niveau de dé par tranche de `MAG` cases** supplémentaires.
- **Liens Télépathiques & Résonance Symbiotique (§18.2)** :
  - **Tissage hors combat** : `4 heures` d'entraînement commun (comme *drills*), puis jet de Télépathie du télépathe contre Diff MJ qui doit être **dépassée de `+2` ou plus** (échec → réessai le lendemain après repos). Si réussi : **`5 XP` payés par chaque membre** (télépathe compris ; un ajout ultérieur coûte `5 XP` au seul nouveau membre).
  - **Effets permanents en combat** :
    1. *Communication Instantanée* : pensée directe à `0 PA`, inaudible des ennemis.
    2. *Synchronisation Tactique* : à **`< 3` cases** d'un autre membre du lien, chacun gagne **`+1 EC`**.
    3. *Ancrage Thermodynamique* : en cas de surchauffe arcanique, un autre membre du lien peut absorber à sa place l'Essoufflement causé par la surchauffe (à `0 PA`).
  - **Rupture** : Ni la distance ni l'anti-magie ne détruisent le lien (il dort) ; il ne s'éteint qu'à la mort d'un membre ou par relâchement volontaire du télépathe.

### §19. Atelier de Création Modulaire de Pouvoirs & Compendium Arcane
- **Règle d'Or de Création** : **`Coût de Création (XP) = Coût d'Activation (PA)`** (`1 XP = 1 PA` ; tarif `×2 XP` en croisé si modules hors voie d'affinité — Livre I §5.3).
- **Exceptions de calcul entre XP et PA (§19.1–§19.2)** :
  - **Sort en Réaction** : **`+2 XP` à la création et `+2 PA` à l'activation** hors de son tour (ex. *Mur Réflexe* : bouclier 4 pts `3` + Réaction `2` = `5 XP` et `5 PA`).
  - **Pouvoirs Hybrides** : **`+1 XP` par catégorie supplémentaire**, qui **ne compte pas dans le coût en PA** en combat (ex. *Injonction Douce* : `4 XP`, `3 PA`).
- **Catalogue des Modules (`32` briques : `18` offensives, `13` défensives/utilitaires, `1` sociale)** :
  - *Offensifs :* `1 pt` (Teinte élémentaire, `+1` Offensif cumulable, `+2` Dégâts directs cumulables, Cible perd `2 PA` cumulable, Cible *Débalancée* 1 tour) ; `2 pts` (*À Terre*, Repousse `1` case cumulable, `-1` prochaine épreuve cible cumulable) ; `3 pts` (`2` Dégâts Résiduels, *Déstabilisé* 1 tour, *Immobilisé* 1 tour, `+1` Dégât Absolu zone `1` case) ; `4 pts` (*Ralenti* 1 tour, Rayon `+1` case cumulable) ; `6 pts` (`3` Dégâts Résiduels) ; `7 pts` (*Paralysé* 1 tour) ; `8 pts` (*Sonné* 1 tour) ; `10 pts` (`4` Dégâts Résiduels).
  - *Défensifs & Utilitaires :* `1 pt` (`+1` Défensif/Utilitaire) ; `2 pts` (Soin `4` dégâts cumulable, Portée `+MAG` cases cumulable, Dissiper `1` état) ; `3 pts` (`+2` Résistance Résiduelle, `+2` armure naturelle 1 tour, Bouclier `4` dégâts cumulable, État *Rapide* 1 tour) ; `4 pts` (État *Survolté* 1 tour : `+1 EC, +3 PA`) ; `7 pts` (État *En Transe* 1 tour : `+2 EC, +5 PA`, ou État *En Vol* 1 tour) ; `8 pts` (État *Accéléré* 1 tour) ; `9 pts` (État *Lévitation* 1 tour).
  - *Sociaux :* `1 pt` (`+1` Épreuve Sociale cumulable).
- **Compendium des Spécialisations Arcanes (§19.4)** : `329` entrées dans `specialisations_arcanes` (tarif standard `5 XP` lié / `10 XP` croisé), organisées par chaînes prérequises (`↳`) sur les 3 voies.
  - *Note d'état `6f497f7` :* `nb_specialisations` vaut `46` dans le JSON alors que la liste contient `329` spécialisations ; le module offensif à `1 pt` mentionne encore le terme *« Débalancée »* (remplacé ailleurs par *Déstabilisé* à `3 pts`) et le module *Ralenti* à `4 pts` indique encore « toute action coûte double PA » sans le `+1 PA/case` de Livre VI §24.1 ; les textes de `fabrique` renvoient encore à « Livre IV §20 » au lieu de `§19.1` ; `tableaux` contient une clé orpheline `stades_neuronaux` non affichée dans le livre.

## Partie 5 — Combat Tactique & Véhicules (Livre V, §20–§22)

### §20. Le Tour de 10 Secondes & L'Économie des Points d'Action
- **Séquence du combat (§20)** :
  1. **Tour d'Embuscade (optionnel, `1` seule fois max par combat, avant l'initiative)** : Offre `1` tour complet avant le jet d'initiative. Seul l'instant déclencheur inflige l'état *Surpris* (`Défense Passive 0`) ; l'état se dissipe aussitôt après. Une créature ayant bénéficié du Tour d'Embuscade **ne récupère pas ses PA** avant le début de son premier tour d'initiative normal. Une fois l'initiative lancée, il n'y a plus de Tour d'Embuscade (les attaques depuis *Dissimulé / Caché* appliquent simplement *Surpris*).
  2. **Initiative (une fois avant le round 1)** : `Max(RAP, AGI, INT)` (ou inclut `INS` avec *Sens du Danger*) converti en dé (§6). Ordre fixe pour tout le combat. Chacun démarre plein de PA et avec sa réaction libre (sauf bénéficiaire du Tour d'Embuscade).
  3. **Début du tour personnel** : 1) Résolution puis décroissance (`-1`) des dégâts résiduels ; 2) Recharge complète des **PA** (sans cumul) ; 3) Recharge de la **Réaction Libre** (`1/cycle`).
  4. **Milieu du tour** : Actions dans l'ordre souhaité. Hors de son tour : défenses illimitées tant que payées + `1` réaction libre/cycle.
  5. **Fin du tour personnel** : Expiration des effets ancrés « fin du tour » (*Guet*, *Décrochage*, états « jusqu'à la fin du prochain tour »).

#### 20.1 Postures (`0 PA`, pendant son tour, `1` active à la fois, persistante)
- **Défaut** : `0` bonus / `0` malus.
- **Préventive** : Requise pour *Prise de coup* (§20.7) ; **`-1` en attaque**.
- **Défensive** : **`+1` en défense**, **`-1` en attaque**.
- **Offensive** : **`+1` en attaque**, **`-1` en défense**.

#### 20.2 Économie des PA, Face-à-Face & Cibles Hors d'État
- **Actions Principales (`1/tour`, `2/tour` dès `2d6+`)** : Frapper (`2 PA`), Tirer (`2 PA` + `+1 PA`/malus de visée Niv. 2 annulé), Sort offensif, Pouvoir, Prise/Traînée/Étranglement (`2 PA`), Course & Poursuite (`4 PA`). La 2e action principale (débloquée quand la compétence offensive atteint `2d6+`) se joue après la 1re action et après le tour d'un autre combattant.
- **Mises d'effort (`1 PA` ou `1 PE = +1`)** : Plafonnées à **`1 + entraînements`** de la compétence utilisée (PA, PE, *Action en Progression*, *Survolté* et *En transe* inclus — Audit F1). Mises simultanées secrètes (poing fermé / variante ouverte).
- **Défenses hors tour** : Illimitées tant que payées (`1 PA` base + mises masquées). À `0 PA`, le défenseur peut lancer son dé à **`-2`**, ou opter pour la **Défense Passive** (`0 PA`, score `0`).
- **Cible hors d'état de se défendre** :
  - *Inconscient* : Pas de jet — inflige les **dégâts maximum** (dés au max + bonus), puis armure/Encaissement/Seuil Létal.
  - *Paralysé* : Jet d'attaque contre **Défense Passive imposée (`0`)**.
  - *Sonné* : Tombe *À Terre*, mais peut parer/réagir à **`-2` en défense**.
- **Guet (`2 PA` réservés pendant son tour)** : Arme à distance, cône `360°`. Déclenche `1` tir automatique en Face-à-Face (*Ballistique vs Esquive*, **mises à `0`**) sur le premier ennemi qui agit dans le rayon. Expire à la fin du prochain tour du guetteur (annulé si *Étourdi, Paralysé, Sonné, Inconscient*, ou cible en couvert total).

#### 20.3 Essoufflement (`ESS` / `PE`)
- Conversion d'urgence `1 PE = 1 PA` quand la réserve de PA est vide (mises masquées autorisées dans le plafond `1 + entr.`). Plafond volontaire = `CON` (`CON + 1` avec *Endurance Coloniale*).
- Dépassement forcé du plafond : le corps lâche → *À Terre* et *Inconscient* jusqu'à la fin du prochain tour, où l'Essoufflement retombe à `0`.
- **Reprendre son souffle (`1 PA = -1 PE`)** : Illimité pendant son tour, ou `1/cycle` hors tour via la Réaction Libre.

#### 20.4 Effets Spéciaux Déclenchés sur Épreuve (`9` effets)
- **Repousser** (`Δ ≥ 1`, mêlée/magie) : repousse de `Δ` cases (max `1` case par niveau d'entraînement).
- **Refoulement** (*Arts Martiaux* au contact + Choc `> CON × 2`, Critique ou `Δ ≥ 4`) : repousse d'`1` case (`Distance = 2`, brise *Canon Entravé*) ; si bloqué par obstacle → *Déstabilisé*.
- **Attirer** (`Δ ≥ 1`, grappin/télékinésie) : attire selon `Δ` ou entraînement.
- **Saigner** (`Δ ≥ 5`, arme tranchante/perforante) : dégâts résiduels = niveau d'entraînement.
- **Brûler** (`Δ ≥ 5`, feu/plasma) : *En Feu* (`1 à 3` tours).
- **Désarmer** (`Δ ≥ 5` et `3` entr. martiaux) : l'arme vole à `2` cases au sol.
- **Assommer** (`Δ ≥ 10` et visée crâne Niv. 2) : *Inconscient* `1` tour min.
- **Posséder** (`Δ ≥ 10` en Magie de l'Esprit) : contrôle `1` tour + amnésie totale.
- **Désintégrer** (`Δ ≥ 20` et Spé Destruction 6) : mort instantanée, vaporisation totale.

#### 20.5 Barème des Coûts en PA & Cumul par Case
- **Formule de coût de déplacement par case (`1 case = 1,5 m`)** :
  `1 (base) + 1 (Terrain difficile ou nage/escalade sans Franchissement) + 1 (Accroupi, À Terre, Déstabilisé ou Déplacement furtif — non cumulatifs entre eux ; 0 en furtif avec Déplacement silencieux) + 1 (Ralenti) + Encombrement (§32.1)` → jusqu'à `4 PA/case` sans armure (`6 PA/case` avec Encombrement `+2`).
- **Hors déplacement sous *Ralenti*** : `(Coût base + Encombrement) × 2` sur toute action (attaquer, tirer, sort, réaction, se relever, se cacher). *Accéléré* et *Ralenti* s'annulent mutuellement et immédiatement.
- **Micro-actions (`1 PA`, sans limite par tour)** : Se déplacer `1` case, dégainer/ranger, recharger, ouvrir/fermer une porte non verrouillée, ramasser un objet (`≤ 1` case), utiliser potion/objet/seringue, se relever, Observation active, reprendre son souffle (`1 PA/PE`), secouer un allié inconscient.
- **Gratuit (`0 PA`)** : Parler (`2` phrases / ~`30` mots gratuites par cycle, puis `1 PA/phrase`), lâcher un objet en main, s'accroupir / se mettre *À Terre*, changer de posture.
- **Action à Progression** : Pendant son tour, miser jusqu'au prix de base de l'attaque ; chaque `PA` misé vaut **`+2` au jet** au prochain tour (compte dans le plafond `1 + entr.`), mais impose **`-1` en défense** entre-temps et se perd si impossible à jouer au 2e tour ou si un coup dépasse `CAP`.

#### 20.6–20.9 Opportunités, Garde du Corps, Lutte & Charge
- **Opportunités (§20.6)** : Quitter une case adjacente à un ennemi déclenche `1` réaction d'opportunité (tourner autour sans s'écarter ne déclenche rien). Consomme l'unique **Réaction Libre** du cycle (annulée si *Étourdi, Paralysé, Sonné, Inconscient*). Posture choisie gratuitement à son tour parmi `4` options :
  - **Frappe (`0 PA` + RL)** : Face-à-Face auto (mises à `0`).
  - **Balayage (`0 PA` + RL)** : vise Jambes (*À Terre* + *Ralenti* si choc traumatique).
  - **Poursuite (`1 PA` + RL)** : suit d'`1` case sans attaque ni nouvelle opportunité.
  - **Blocage (`1 PA` + RL)** : Face-à-Face *Athlétisme vs Athlétisme* (mises à `0`) ; si égalité ou victoire du bloqueur, annule le pas (PA non consommés remboursés).
  - **Décrochage (`1 PA` d'activation)** : couvre tout le tour ; toute case d'écartement payée normalement ne déclenche aucune opportunité.
- **Garde du Corps & Prise de Coup (§20.7)** : Requiert la **Posture Préventive** (`-1` attaque) et un allié à `≤ 1` case. Dépense **`1 PA` + Réaction Libre** (`2 PA` base sous *Ralenti*) pour rediriger l'attaque vers soi.
- **Lutte (§20.8)** :
  - *Prise (`2 PA`, contact)* : Face-à-Face *Athlétisme/Mains Nues vs Athlétisme/Défense Corporelle* → égalité ou victoire pose *Immobilisé* tant que l'étreinte tient. Rupture auto si l'un des deux meurt, tombe *Inconscient*, est *Sonné*, *Paralysé* ou *Étourdi*, ou si l'assaillant bouge seul. Relâchement volontaire : `0 PA`.
  - *Se libérer (`2 PA`)* : Face-à-Face *Athlétisme/Mains Nues/Acrobatie vs Athlétisme/Mains Nues* (égalité libère).
  - *Traîner le corps (`2 PA/case`)* : déplace le lutteur + victime/allié d'`1` case (le traîneur s'expose aux opportunités, pas la victime).
  - *Étranglement (`2 PA`, victime en prise)* : Face-à-Face *Athlétisme vs Endurance Physique* → égalité ou victoire inflige **`+2 PE` et `Asphyxie 2`**.
- **Charge d'Assaut (§20.9)** : Déplacement d'au moins `3` cases d'affilée (terrain difficile interdit sans *Franchissement*) jusqu'au contact + frappe de mêlée : **`+2` dégâts** sur la frappe et **`-1` en défense** jusqu'au début de son prochain tour.

### §21. Poursuites, Attaques de Groupe, Couvert, Furtivité & Techniques-Duo
- **Course & Poursuite (§21.1)** : Départ impossible si un ennemi est à `< 4` cases (démarrage à `4` cases d'écart). Coûte **`4 PA` (Action Principale)** par tour, remplace tout autre déplacement :
  - Fuyant : avance de **`30` cases (`45 m`)** (divisé par `2` = `15` cases si *Ralenti* ou terrain difficile) et note son jet d'*Athlétisme* (PA/PE permis).
  - Chaque poursuivant (`4 PA`) ne compte pas ses cases : il oppose son jet d'*Athlétisme* au score du fuyant (`gagne → -1 case`, `égalité → 0`, `perd → +1 case`).
  - **Issues** : *Fuite réussie* à **`≥ 8` cases** d'écart ; *Plaquage* automatique (`0 PA`) dès **`1` case** d'écart (*Athlétisme vs Athlétisme*, victoire = *À Terre*, fin de poursuite) ; *Limite d'endurance* à partir du **9e tour** (`> 8` tours) : jet gratuit (`0 PA`) d'*Endurance Physique* Diff `4` (`+1/tour` ensuite) ou rattrapé à `1` case.
- **Attaques Combinées de Groupe (§21.2)** : `2` à **`3` attaquants max**, chacun à portée valide, payant **Action Principale + `2 PA`** au rang d'initiative du plus lent sur la même zone (*Torse* par défaut). Sommation des jets d'attaque contre l'unique défense adverse ; dégâts totaux = somme des armes + `Δ` net, **armure soustraite une seule fois**.
- **Couvert & Ligne de Mire (§21.3)** : Lecture depuis le meilleur coin de case vers silhouette (`1,8 m`).
  - *À découvert* (`0`) ; *À moitié couvert* (**`-1`** attaque) ; *Trois-quarts couvert* (**`-2`** attaque) ; *Couvert total* (attaque directe interdite, `0 PA` dépensé). Au contact (`≤ 1` case) à découvert, le couvert est ignoré.
  - *Effets de zone* (grenades/souffles) : le couvert réduit les dégâts au lieu d'interdire (**`-2`** moitié, **`-3`** trois-quarts, **`-4`** couvert total et bloque les éclats ; tir lobé ignore couverts partiels).
- **Brouillard de Guerre Asymétrique, Observation & Discrétion (§21.4)** :
  - En combat : **vision à `360°`** sans limite chiffrée (bornée par murs/relief/météo/nuit ; `0` bonus « par-derrière »). Hors combat : créature non alertée a un angle mort permettant l'approche furtive (*Discrétion vs Observation/Écoute* → *Tour d'Embuscade*).
  - **Observation active (`1 PA`)** : Seuil **`4`** (auto dès `1d12`) contre un non visible sous couvert ; contre *Dissimulé / Caché*, **jet lancé obligatoire** comparé secrètement par le MJ (`Δ ≥ 0` révèle).
  - **Se cacher (`2 PA`, ou `1 PA` avec *Ombres*)** : Requiert d'être hors de vue (couvert total, nuit noire, fumée ; `3/4` avec *Camouflage*). Jet de *Discrétion* avec **plancher `4`** tant que l'abri masque la vue → état *Dissimulé / Caché*. Anti-yo-yo : impossible à découvert ou au même endroit si déjà vu sans changer de position hors de vue ou diversion.
  - **Déplacement furtif** : `+1 PA/case` (`+0` avec *Déplacement silencieux*). Rupture si attaque, case à découvert, lampe allumée, repérage ou action bruyante. Micro-actions silencieuses préservent l'état.
- **Techniques-Duo / Résonance Dyadique (§21.5 — `9` manœuvres)** : Ne consomme pas d'action principale (`1/cycle/perso`). Test simultané de **caractéristique nue** (`AGI` / `INT` / `FOR`) + PA injectés + malus d'états vs `ND` (max naturel = succès auto du partenaire ; aucune case d'XP cochée). `2` réussites = *Parfaite* ; `1` = *Semi* ; `0` = *Ratée* (PA perdus).
  - Les 9 duos : *Sillage Igné* (`8 PA, ND 5, AGI+INT`), *Lacet de Nytharite* (`5 PA, ND 5, AGI+INT`), *Fournaise à Retardement* (`7 PA, ND 5, AGI+INT`), *Trempe Inversée* (`9 PA, ND 6, AGI+INT`), *Canon à rails artisanal* (`11 PA, ND 7, FOR+INT`), *Cisaille Entropique* (`9 PA, ND 6, AGI+INT`), *Parallaxe* (`6 PA, ND 5, AGI+INT`), *Mirage à Deux Temps* (`4 PA, ND 4, AGI+INT`), *Uppercut Thermobarique* (`9 PA, ND 6, FOR+INT`).

### §22. Localisation Anatomique, Canon Entravé & Combat de Véhicules
- **Visée à 2 Niveaux (§22.1 — Zéro déviation aléatoire)** :
  - **Niveau 1 (`0` malus)** : `6` zones larges (*Tête/Cou*, *Torse/Dos* par défaut, *Bras G*, *Bras D*, *Jambe G*, *Jambe D*). Sert au suivi d'Encaissement (`CON × 2` par zone) et à l'arbre du Seuil Létal.
  - **Niveau 2 (`-1` ou `-2` au jet, annulable à `+1 PA` par point)** : `16` points vitaux répartis dans les 6 zones (*Yeux/Visage `-2`*, *Trachée `-2`*, *Carotide `-2`*, *Crâne `-1`*, *Cœur `-2`*, *Poumons `-1`*, *Plexus `-1`*, *Abdomen `-1`*, *Colonne `-2`*, *Artère brachiale `-2`*, *Poignet `-1`*, *Épaule `-1`*, *Artère fémorale `-2`*, *Genou `-1`*, *Tendon d'Achille `-2`*, *Périnée `-1`*). Ne s'active que si `Δ > 0` **et** au moins `1` dégât passe l'armure (`Δ = 0` ou `0` dégât → pas d'effet de point vital).
  - **Formule dégâts réels** : `Dégâts Subis = (Dégâts Arme + Δ Touche) − Armure`. À *Mains Nues* : `0 + Δ`, mais inflige **`+1 PE`** sur touche franche.
- **Canon Entravé & Manœuvres au Contact (§22.2)** : Fusil, carabine ou arme à `2H`/portée `> 1` avec ennemi adjacent (`≤ 1` case) : `-2` au tir (*Tir en Rétention* `2 PA`), ou *Tir Visé de Rétention* (`3 PA`, annule `-2` et débloque visée), *Coup de Crosse* (`2 PA`, `3–4` dégâts, *Déstabilisé* si `> CAP`), *Frappe Martiale* (`2 PA`, plein dé + *Refoulement*), *Décrochage* (`1 PA` + pas).
- **Combat de Véhicules (§22.3)** : Conduire = Action Principale ; embarquer/débarquer = `2 PA`. Zones **Essentielles** (rupture = hors service, équipage éjecté *Déstabilisé* et *À Terre* + dégâts si explosion) vs **Superficielles** (perte de sous-système). **5 paliers de blindage/calibres** : `1–10` (vélo, pistolets), `11–50` (charrette, mêlée lourde), `51–1 000` (jet/char, canons), `1 001–10 000` (porte-avions/croiseur, lasers arcaniques/torpilles), `10 001+` (stations orbitales/mégastructures, frappe nucléaire/orbitale).

---

## Partie 6 — Santé, États, Soins & Pathologies (Livre VI, §23–§25)

### §23. Seuils Physiologiques, Typologie des Dégâts & Seuil Létal
- **Typologie des Dégâts (§23.1)** :
  1. **Directs (Physiques)** : Réduits par l'Armure (si `≤ 0` après armure, `0` dégât mais effets spéciaux hors point vital actifs).
  2. **Résiduels (Continus)** : Ignorent armure et boucliers physiques. Infligent leur score en début de tour, puis décroissent de `1` (`3 → 2 → 1 → 0`). Si **aggravés**, infligent le **double** et croissent de `+1/tour`.
  3. **Absolus (Arcaniques)** : Ignorent totalement l'armure physique.
- **Choc Traumatique par Zone (`> CON × 2`)** : Dès que le cumul d'une zone dépasse strictement `CON × 2`, jet gratuit (`0 PA`, mises permises) d'*Endurance Physique* Diff `3 + (dégâts zone − CON × 2)` :
  - **Réussite** : pas d'effet ; aucun autre jet tant que la zone ne reprend pas de dégâts.
  - **Échec** : pose l'état **Souffrant** + effet de zone jusqu'au prochain soin : *Tête/Cou* → *Étourdi* (escalade en *Sonné* 1 tour si déjà *Étourdi* par ce seuil ou dépassement `≥ +3`) ; *Torse/Dos* → *Déstabilisé* ; *Bras G/D* → bras inutilisable (lâche ce qu'il tient, pas de `2H`) ; *Jambe G/D* → *Ralenti* (si les 2 jambes : *À Terre* + *Immobilisé*).
- **Arbre de Résolution du Seuil Létal (`≥ CON × 5`, §23.2)** : On compare les dégâts du **coup fatal** au Seuil d'Encaissement (`CON × 2`) :
  - **Branche 1 (`Coup fatal > CON × 2` — Traumatisme incompressible, 0 choix)** :
    - *Tête / Crâne* : **Mort Instantanée** (sauf `1 PM` → *Inconscient* et *À Terre*).
    - *Cœur ou Cou* : **Agonie critique** — *Inconscient* et *À Terre* (non réveillable par secousse) ; au début de chaque tour suivant, jet gratuit (`0 PA`) d'*Endurance Physique* Diff **`4` (`+1/tour`)**, **mort au 1er échec** sans stabilisation (*Premiers Soins* Diff `6` ou soin actif ; `1 PM` utilisable).
    - *Torse & Membres (hors Cœur)* : *Inconscient* et *À Terre* (état stable, réveillable).
  - **Branche 2 (`Coup fatal ≤ CON × 2` — Choc contrôlé)** — le joueur choisit :
    - **Sombrer** : *Inconscient* et *À Terre* stable (souvent ignoré des ennemis, mais vulnérable sans défense à un coup de grâce).
    - **Dernier Souffle** : reste conscient (*À Terre* et *Agonisant*), garde ses PA ; **chaque épreuve jetée coûte `+1 Essoufflement` direct** (gestes sans jet gratuits). **Mort par arrêt cardiaque si `Essoufflement > CON`**.
- **Point de Miracle (§23.3)** : Tout PJ commence avec `1 PM` (`0` pour sbires). `0 PA`, dévie un coup mortel ou échec fatal en Agonie → survit *Inconscient* et *À Terre*. Ne revient jamais au repos : rachetable pour **`20 XP`** ou jalon narratif MJ.
- **Résistances & Vulnérabilités Raciales (§23.4)** : Résistance réduit dégâts directs de l'élément (après armure) et fait démarrer ses résiduels à `-1` (min `1`, non cumulable entre sources). Vulnérabilité ajoute `+1` dégât subi après réductions et `-2` aux jets de `CON` contre cet élément.

### §24. Compendium Officiel des 19 États, Statuts Vitaux & Objets
- **Règle de cumul** : Des états **différents** se cumulent entre eux ; un même état appliqué plusieurs fois ne se cumule pas avec lui-même (hors paliers *Affamé/Assoiffé/Somnolent* et escalades §23).

#### 24.1 Les 19 États Préjudiciables
1. **Canon Entravé** : arme longue/2H avec ennemi adjacent → `-2` tir (annulable `+1 PA`) ; parade à l'arme `-1` cran de dé sans spé.
2. **Assourdi** : *Écoute* impossible ; `-2 Observation` hors champ de vision.
3. **Étourdi** : `-1 EC` sur tous les jets (annule aussi les opportunités/guet).
4. **Affaibli** *(19e état)* : `-1 EC` sur tous les jets, cumulable avec *Étourdi* (retour à la vie §25.3, séquelle maladie §25.4).
5. **Accroupi** : `-1 EC` mêlée/jet/Athlétisme/Acrobatie, `-1` défense ; tir/magie sans malus ; déplacement `+1 PA/case`, se relever `1 PA`.
6. **À terre** : `-1 EC` mêlée/Athlétisme/Acrobatie, `-2 EC` armes de jet (arc impossible), `-2` défense ; tir/magie sans malus ; ramper `+1 PA/case` (non cumulatif avec *Déstabilisé*), se relever `1 PA`.
7. **Déstabilisé** : `-2 EC` aux épreuves, déplacement rampant `+1 PA/case` (non cumulatif avec *À Terre*).
8. **Affamé** (paliers) : Niv. 1 (`48 h`) `-1 EC` ; Niv. 2 (`1 sem.`) `-2 EC`, guérison repos ÷2 ; Niv. 3 (`2 sem.`) `-3 EC`, `0` guérison repos, `1` dégât direct/jour ; Niv. 4 (`3 sem.`) *Mort* (`1` ration/jour = `-1` niveau).
9. **Somnolent** (paliers) : Niv. 1 (`24 h`) `-1 EC` ; Niv. 2 (`48 h`) `-2 EC`, `0` guérison/ESS hors sommeil ; Niv. 3 (`72 h+`) `-3 EC`, jet d'*Endurance Physique* Diff `4` (`+1/6h`) toutes les `6 h` et fin de combat ou *Inconscient* (sommeil `6–8 h` enlève `1` niveau).
10. **Assoiffé** (paliers) : Niv. 1 (`24 h`) `-1 EC` ; Niv. 2 (`48 h`) `-2 EC`, `0` guérison/ESS au repos ; Niv. 3 (`72 h`) `-3 EC`, *Ralenti*, `0` guérison repos, `2` dégâts directs/jour ; Niv. 4 (`4 jours`) *Mort* (`2 L` enlèvent `1` niveau aussitôt, `1×/avant repos`, puis sommeil lève le reste).
11. **Ralenti** : actions hors déplacement **`×2 PA`** ; déplacement **`+1 PA/case`** (cumulable). S'annule mutuellement avec *Accéléré*.
12. **Aveugle** : `-4` attaques à distance, `-2` mêlée et défense, vision nulle.
13. **Immobilisé** : aucune action de mouvement possible.
14. **Ligoté** : mains/bras entravés (aucune action manuelle), `-2` défense (*Esquive* seule) et Athlétisme/Acrobatie ; cumulable avec *Immobilisé*.
15. **Surpris** : défense tombe à `0` (Défense Passive imposée) sur l'instant déclencheur uniquement ; se dissipe aussitôt après.
16. **Paniqué** : aucune action volontaire ; fuit de `6` cases en direction `1d8` ; défense possible à `-2`.
17. **Sonné** : aucune action ; tombe immédiatement *À Terre* ; peut encore parer à `-2`.
18. **Paralysé** : incapacité totale (0 action, 0 réaction, défense imposée à `0`).
19. **Agonisant** : *À Terre* ; suit l'Agonie critique (jet Diff `4 + 1/tour`) ou le Dernier Souffle (`+1 PE/épreuve`) jusqu'à *Premiers Soins* Diff `6`.

#### 24.2 Particularités des 4 États à Dégâts Résiduels
- **Saignement** : ne décroît pas en début de tour si la victime a effectué *Course*, *Charge* ou dépensé de l'*Essoufflement* ; tout soin l'arrête.
- **Empoisonné** : réduit de **moitié (`÷2`)** tous les soins de dégâts reçus ; exige antidote/Médecine/purge.
- **En Feu** : frappe d'abord les boucliers énergétiques ou magiques (armure et bouclier physique ignorés) ; extinction immédiate pour **`X PA`** (= score *En Feu* courant).
- **Asphyxie** : fait perdre **`1 PA`** sur la réserve une fois rechargée ; exige air pur, rupture d'étranglement ou *Premiers Soins* (`3 PA`).

#### 24.3–24.5 États Bénéfiques (`7`), Statuts Vitaux (`3`) & États d'Objets (`5`)
- **7 États Bénéfiques** : *Survolté* (`+1 EC` dans plafond effort, `+3 PA`), *En transe* (`+2 EC` dans plafond effort, `+5 PA`), *Rapide* (`+3 PA` dédiés au déplacement), *Accéléré* (coût déplacement `÷2` arrondi inf., min `1 PA`, annule *Ralenti*), *En Vol* (coût déplacement `÷2`, élan avant obligatoire sinon décrochage/chute, `-1 EC` sans maîtrise), *Lévitation* (vol stationnaire 3D à `1 PA/case`, chute si neutralisé), *Dissimulé / Caché* (inciblable en attaque directe tant que non repéré, 1re attaque applique *Surpris*).
- **3 Statuts Vitaux** : *Inconscient* (**lâche immédiatement tout ce qu'il tient en main**, 0 action/réaction, dégâts max auto sans jet si attaqué, réveil `1 PA` secousse ou soins) ; *Souffrant* (maintient l'effet traumatique de zone, non levé par le repos sûr, levé par tout soin actif) ; *Mort* (trépas clinique, lâche objets en main).
- **5 États des Objets** : *Parfait État* (`100 %`, revente `50 %`) ; *Endommagé* (fonctionnel, revente `25 %`, réparable au calme) ; *Enrayé* (hors service temporaire, réparable en combat pour **`2 PA`**) ; *Détruit* (hors service, atelier + ~`25 %` prix neuf + jet Artisanat/Ingénierie Diff `5/7/9`, revente `10 %`) ; *Anéanti* (irréparable).

### §25. Premiers Soins, Repos Sûr, Résurrection & Maladies
- **Premiers Soins d'Urgence (§25.1)** : **`3 PA`** (`2 PA` avec *Suture Réflexe*) au contact (`1` case). Stabilise un *Agonisant* sur **Premiers Soins Diff `6`** (→ *Inconscient* stable). Tout soin actif lève *Souffrant* et *Saignement*.
- **Convalescence & Repos Sûr (§25.2)** : En lieu sûr (jour ou nuit, **délai minimal de `12 h`** après la fin du repos sûr précédent) :
  - Durée : **`6 h`** base ; **`8 h`** avec tours de garde (relais de `2 h`) **ou** veille de soignant (`2 h` automatiques, 0 jet) ; **`10 h`** avec les deux.
  - Récupération : efface **`CON` dégâts** (doublé **`×2` = `2 × CON`** avec veille d'un soignant ayant `≥ 1` entr. Premiers Soins / Médecine Avancée ou sort/pouvoir de soin). Vide l'Essoufflement, réveille un stabilisé, stoppe *Saignement*, recharge les dons `1×/jour`. Ne lève jamais *Souffrant*, poisons/maladies ni *PM*.
- **Résurrection & Poids du Trépas (§25.3)** : Tout être ramené de la mort subit l'état **Affaibli** (`-1 EC` cumulable sur tous les jets) pendant **`10 − CON` jours réels**.
- **Maladies & Pathologies — Bases Joueur (§25.4, renvoi Livre XII §53)** :
  - `6` natures (*bactérienne, virale, parasitaire/fongique, toxique/prion, arcanique, nanotech/cyber* — Automates immunisés aux 4 organiques).
  - Exposition : jet gratuit (`0 PA`, sans mises de PA libres hors combat ; PE volontaire/dons/auto permis) de **Système Immunitaire (`CON`)** vs Virulence (`3` à `18+`).
  - `1` à `6` stades ; les personnages fragiles (`CON 1–2`) démarrent à `+1` stade initial sur souches `≥ 2` stades. À chaque échéance, jet de transition (réussite = stade `-1`, échec = stade `+1`). Diagnostic : *Académie / Premiers Soins / Médecine Avancée* vs Virulence (min `4`, `+2` avec *Diagnostic Vital*). Immunité après guérison : `1d6` mois par défaut.

## Partie 7 — Historiques & Arsenal (Livres VII & VIII, §26–§32)

### Livre VII (§26–§29) — Historiques & Passés (Étape 2)
- **Procédure (§26)** : L'historique comprend `4` types d'entrées (`1` choix max par type), dont **seule l'Enfance est obligatoire** :
  1. *A. Ancrage (`§29.2`, optionnel)* : 3 questions narratives (`0` mécanique).
  2. *B. Enfance (`chap. 27`, obligatoire, `1d6`)* : `1` aptitude mineure (`+2` à `1` compétence étroite dans son contexte).
  3. *C. Métier (`chap. 28`, optionnel, `1d8`)* : `1` aptitude majeure (`+2` à `2` compétences + `1` atout concret sans bonus chiffré + `1` attache narrative optionnelle).
  4. *D. Événement Marquant (`§29.1`, optionnel, `1d12`)* : `1` effet **`1×/session`** contre **`1` séquelle réelle** (refuser/contourner la séquelle fait perdre définitivement l'effet).
- **Règles de table (§26.1–§26.2)** :
  - **Tirage historique pur (`Enfance d6` + `Métier d8` aux dés sans choix manuel)** : confère **`+1 Bagage`** à la création (plafond total `5`, Livre I Étape 6).
  - **Pas de cumul, on élargit** : si Enfance/Métier donne `+2` sur une compétence déjà couverte par un don racial ou l'autre entrée, le second `+2` glisse vers la compétence voisine la plus proche (validation MJ).
  - **Époque de campagne `[E0–E5]`** : fixée par le MJ (*E0 Primitive*, *E1 Médiévale*, *E2 Moderne*, *E3 Avancée*, *E4 Très Avancée*, *E5 Démiurgique*) ; tout écart d'époque exige une justification MJ.
- **Tables d'Enfances (`36` entrées = 6 tables `d6` `E0–E5`, chap. 27)** :
  - `E0` : *Clan & Tribu* (`+2 Intuition`), *Piste & Gibier* (`+2 Discrétion`), *Grottes & Falaises* (`+2 Athlétisme`), *Nomades & Steppes* (`+2 Nature & Survie`), *Taille & Bronze* (`+2 Artisanat`), *Herbes & Foyer* (`+2 Premiers Soins`).
  - `E1` : *Forge & Guilde* (`+2 Artisanat`), *Ruelles & Faubourgs* (`+2 Subterfuge Manuel`), *Glèbe & Lisière* (`+2 Endurance Physique`), *Foire & Caravane* (`+2 Communication`), *Abbaye & Scriptorium* (`+2 Académie`), *Donjon & Remparts* (`+2 Observation`).
  - `E2` : *Ports & Entrepôts* (`+2 Subterfuge Manuel` + ignore malus d'exiguïté), *Rues & Bas-fonds* (`+2 Observation` urbain), *Ferme & Frontière* (`+2 Nature & Survie`), *Quartier & Foyer* (`+2 Intuition`), *Cité & École* (`+2 Académie`), *Caserne & Ordre* (`+2 Athlétisme`).
  - `E3` : *Soutes & Docks* (`+2 Subterfuge Manuel` + ignore malus d'exiguïté), *Dôme & Terraformation* (`+2 Système Immunitaire`), *Pont & Navette* (`+2 Acrobatie`), *Mégacité & Bas-niveaux* (`+2 Ingénierie`), *Convoi & Astroport* (`+2 Communication`), *Écoute & Passerelle* (`+2 Écoute`).
  - `E4` : *Arcs de Dyson* (`+2 Observation`), *Relais & Portails* (`+2 Conduite & Pilotage`), *Atelier de Nytharite* (`+2 Ingénierie`, 0 accès 5e Force), *Cuves & Bio-sanctuaire* (`+2 Premiers Soins`), *Enclave & Protocole* (`+2 Leadership`), *Ombres de la Sphère* (`+2 Discrétion`).
  - `E5` : *Jardins de Post-Matière* (`+2 Acrobatie`), *Marges du Continuum* (`+2 Intuition`, 0 clairsentience), *Observatoire des Trames* (`+2 Penser / Stratégie`), *Voile & Silence* (`+2 Discrétion`), *Chœur & Concile* (`+2 Communication`), *Bastion des Lois* (`+2 Endurance Physique`).
- **Tables de Métiers (`48` entrées = 6 tables `d8` `E0–E5`, chap. 28)** :
  - `E0` : *Chasseur de Traque* (*Nature/Ballistique*), *Guerrier Tribal* (*Maniement/Intimidation*), *Guérisseur des Herbes* (*Premiers Soins/Nature*), *Tailleur & Fondeur* (*Artisanat/Endurance*), *Éclaireur des Confins* (*Observation/Discrétion*), *Conteur & Gardien* (*Intuition/Communication*), *Troc & Caravane* (*Communication/Endurance*), *Chef de Meute* (*Leadership/Athlétisme*).
  - `E1` : *Homme d'Armes* (*Maniement/Observation*), *Apothicaire* (*Premiers Soins/Artisanat*), *Forgeron & Maître-Artisan* (*Artisanat/Endurance*), *Veneur & Pisteur* (*Nature/Ballistique*), *Marchand de Guilde* (*Communication/Intuition*), *Scribe & Érudit* (*Académie/Observation*), *Espion & Coupe-Bourse* (*Discrétion/Subterfuge*), *Capitaine & Héraut* (*Leadership/Intimidation*).
  - `E2` : *Milicien* (*Ballistique/Observation*), *Médic* (*Premiers Soins/Observation*), *Mécano* (*Ingénierie/Subterfuge*), *Pilote & Chauffeur* (*Conduite/Observation*), *Négociant* (*Communication/Intuition*), *Infiltré* (*Discrétion/Communication*), *Archiviste* (*Académie/Observation*), *Officier* (*Leadership/Intimidation*).
  - `E3` : *Fusilier Orbital* (*Ballistique/Endurance*), *Médecin de Bord* (*Médecine Avancée/Premiers Soins*), *Ingénieur de Coque* (*Ingénierie/Subterfuge*), *Pilote Stellaire* (*Conduite/Observation*), *Prospecteur Planétaire* (*Nature/Endurance*), *Courtier de Fret* (*Communication/Intuition*), *Opérateur Fantôme* (*Discrétion/Ingénierie*), *Commandant de Quart* (*Leadership/Penser*).
  - `E4` : *Sentinelle de Portail* (*Ballistique/Observation*), *Bio-Régénérateur* (*Médecine Avancée/Système Immunitaire*), *Ingénieur Nytharite* (*Ingénierie/Observation*), *Navigateur de Transit* (*Conduite/Penser*), *Émissaire de Secteur* (*Communication/Intuition*), *Archiviste Cristallin* (*Académie/Observation*), *Saboteur de Sphère* (*Discrétion/Subterfuge*), *Stratège de Flotte* (*Leadership/Penser*).
  - `E5` : *Gardien de Seuil* (*Maniement/Défense Corporelle*), *Modeleur de Post-Matière* (*Artisanat/Ingénierie*), *Indexeur de Trames* (*Penser/Académie*), *Restaurateur Vital* (*Médecine Avancée/Premiers Soins*), *Éclaireur des Plis* (*Acrobatie/Intuition*), *Voix du Concile* (*Communication/Intuition*), *Effaceur d'Échos* (*Discrétion/Subterfuge*), *Arbitre des Lois* (*Leadership/Intimidation*).
- **Événements Marquants (`d12`, §29.1)** : `1` *Frôlé la mort* (ignore 1 échec Choc Traumatique ou `-1 PE` / `-2` sang-froid) ; `2` *Dette de vie* (`+2` protéger allié / créancier) ; `3` *Trahison subie* (relance *Intuition* / traître actif) ; `4` *Long exil* (`+2 Nature & Survie` inconnu / `-2` 1er contact) ; `5` *Contact 5e Force* (1 question fermée résonance / `-2 Discrétion` face détecteurs magiques) ; `6` *Miracle inexpliqué* (ignore 1 échec moral/peur / culte curieux) ; `7` *Guerre vue de l'intérieur* (`+2` sang-froid violence / test `INS` ou `+1 PE` au fracas) ; `8` *Crime impuni* (`+2 Discrétion/Subterfuge` traces / témoin existe) ; `9` *Mentor perdu* (`+2` domaine mentor / œuvre inachevée) ; `10` *Écho temporel* (1 question fermée danger immédiat / `+1 PE` migraine par usage) ; `11` *Ruine & misère* (`+2` improviser / `-2` 1er contact milieu huppé) ; `12` *Gloire locale* (`+2 Communication` / rival jaloux).
- **Ancrage (§29.2)** : Époque + monde + 3 questions narratives, **`0` mécanique**.

### Livre VIII (§30–§32) — Arsenal & Arcanotech `[E0–E5]`

#### §30. Standard DLPH, Portage, Budgets & Propriétés
- **Code Universel `DLPH`** : `D` = Dégâts de base ; `L` = Poids en livres impériales (`1 L ≈ 0,45 kg`) ; `P` = Portée tactique en cases (`1 case = 1,5 m`) ; `H` = Maniement (`1H`, `2H`, `1H/2H`).
- **Portage (`charge_portage`)** :
  - `≤ FOR × 15 L` : mouvement libre.
  - `(FOR × 15 + 1 L)` à `FOR × 25 L` : **Surchargé** (`+1 PA` à chaque action et à chaque case de déplacement, cumulable avec encombrement d'armure, hors mises aveugles).
  - `> FOR × 25 L` : cloué sur place sans aide/monture.
- **Budgets de Départ `[E0–E5]` (en Crédits Impériaux `CE`)** : `E0` `600 CE` · `E1` `1 500 CE` · `E2` `4 000 CE` · `E3` `12 000 CE` · `E4` `30 000 CE` · `E5` `75 000 CE`.
- **Revente & Réparation** : Revente `50 %` (Parfait État), `25 %` (Endommagé), `10 %` (Détruit). Réparation d'un objet *Détruit* : atelier + ~`25 %` du prix neuf + jet d'Artisanat/Ingénierie Diff `5` (`E0–E2`), `7` (`E3–E4`), `9` (`E5`) (moitié prix pour *Système Récup* Vardien).
- **Propriétés Tactiques (`13` clés)** : *Allonge (`2P`)* (frappe & opportunité à 2 cases) ; *Arme de jet (`XP`)* (`+1` case par `FOR > 3` max `+4`, `-1` Accroupi / `-2` À Terre) ; *Perforante (`X`)* (ignore `X` d'armure physique, jamais les champs énergétiques) ; *Zone (`R` cases)* ; *Silencieuse* ; *Dissimulable (`+2` pour cacher sur soi)* ; *Polyvalente (`1H/2H`, `+1` dégât à 2H)* ; *Non létale* (peut stopper à *Inconscient*) ; *Rechargement lent (`X PA` au lieu de `1 PA`)* ; *Cadence (`+1 PA` & `3` mun. → `+1` dégât)* ; *Canon Entravé (`-2 EC` au tir si ennemi adjacent)* ; *Brûlure / État* ; *Anti-Matériel*.
- **Mains Nues (défaut)** : `1D - 0L - 1P - 1H`, *Non létale* (*Armes Naturelles* Mikyai : `2D` sans pouvoir être désarmé ; note Livre V §22.1 indique `0 + Δ` pour les poings standards).

#### §30.3–§31.4 Panorama Chiffré de l'Armement
| Catégorie | Nb modèles | Époque | Plage Dégâts (`D`) | Plage Portée (`P`) | Plage Prix (`CE`) | Propriétés marquantes |
| :--- | :---: | :---: | :---: | :---: | :---: | :--- |
| **Épées & Lames de Guerre** (*Maniement*) | `7` | `E0–E2` | `3D` – `7D` | `1P` – `2P` | `500` – `3 000` | *Perforante 1*, *Allonge 2P* (Espadon Maître `7D, 2P`). |
| **Armes Perçantes, Fines & Hast** | `7` | `E0–E5` | `2D` – `5D` | `1P` – `2P` (jet `6P–10P`) | `150` – `1 500` | *Dissimulable*, *Perforante 1*, `+1` parade (Fleuret), *Allonge 2P*, *Arrêt de charge +1D* (Pique). |
| **Choc, Haches & Capture** (*Maniement* / *Ballistique*) | `9` | `E0–E4` | `0D` – `6D` | `1P` – `2P` (jet `6P`) | `50` – `5 000` | *Non létale*, *Perforante 1–2*, *Étourdi Δ≥2* (Matraque Paralysante), *Immobilisé* (Filet/Bolas). |
| **Trait & Jet Dédiées** (*Ballistique*) | `8` | `E0–E2` | `2D` – `6D` | `6P` – `45P` | `40` – `2 500` | *Silencieuse*, *Perforante 1–2*, *Rechargement lent 2–3 PA* (Arbalètes). |
| **Balistique à Poudre** (*Ballistique*) | `9` | `E2–E4` | `1D` – `7D` | `12P` – `90P` | `800` – `18 000` | *Cadence*, *Perforante 1–3*, *Anti-Matériel*, `+1D à ≤4 cases` (Pompe), modérateur son `+500 CE`. |
| **Épées Laser (Plasma)** | `9` | `E3–E5` | `3D` – `6D` | `1P` – `2P` | `6 000` – `100 000` | *Perforante 2–4* (bute sur bouclier énergétique), *Allonge 2P*, *En Feu 2 sur critique* (`6D`), cellule nytharite longue durée. |
| **Fusils Laser** (*Ballistique*) | `5` | `E3–E5` | `3D` – `7D` | `30P` – `100P` | `10 000` – `200 000` | *Perforante 1–3*, *Cadence*, *Canon Entravé* (2H), **Fusil « Deglazer »** (`+1 EC` matériel, `7D, 100P, Perf. 3, 200 000 CE`). |
| **Armes Lourdes & Anti-Matériel** | `5` | `E2–E5` | `5D` – `10D+2d10` | `18P` – `100P` | `12 000` – `160 000` | *Anti-Matériel*, *Perforante 1–5*, Lance-Roquettes (`10D+2d10, Zone 2, Perf 4`), Canon Plasma (`8D, Perf 4, En Feu 3 crit.`), Canon Rail (`9D, 100P, Perf 5`). |
| **Grenades Explosives** (`8P` + bonus FOR) | `5` | `E2–E4` | `10 + 1d10` à `10 + 5d10` | `8P` (`Rayon 1–3`) | `2 000` – `7 500` | *Déstabilisé*, *Saignement 2* (Frag), *À Terre* (Brèche), *En Feu 3 + Asphyxie 2 + Déstabilisé* (Thermobarique). |
| **Grenades Tactiques & Arcanotech** | `8` | `E1–E5` | `0D` à `8D + 2d10` | `Rayon 1–2` | `350` – `35 000` | Incendiaire, Fumigène (*Aveuglé*), Flash (*Aveuglé/Assourdi/Étourdi*), Gaz (*Aveuglé/Asphyxie 2*), IEM (*Paralysé automates, -15 champ*), Cryo, Graviton, Stase Chronale (`Paralysé, 0D`). |
- **Munitions & Arcanotech (§31.4)** : Charge Laser Haute Densité (`+1 Dégât`, `900 CE`), Munitions Perforantes (`+1 Perforante`, `450 CE`), Cellule Nytharite Standard (`1 500 CE`, recharge champ niv. 1–2 en `2 PA`) & Pure (`8 000 CE`, recharge champ niv. 3–5 en `2 PA`). Implants (`E3–E4`, poids `0`) : *Optique Cybernétique* (`+1 Observation` nuit/longue distance, sensible IEM), *Interface Neurale* (`-1` couvert léger, annule coût visée micro-analyse), *Plaque Sous-Dermique* (`Prot 1, 0 PA`, non cumulable avec armure lourde `≥ 5`). *Module Arcanotech Pré-Gravé* (`3` charges d'un sort `≤ 3 PA`, `18 000 CE`).

#### §32. Blindages, Boucliers, Pharmacopée & Matériel
- **Armures Corporelles (`10` modèles, `1` seule portée, §32.1)** :
  - *Légères 1–4* : `Prot 1–4`, **Encombrement `0 PA`**, `4–7 L`, `2 000–20 000 CE`.
  - *Lourdes 3–7* : `Prot 3–5` (`+1 PA`), `Prot 6–7` (`+2 PA`), *Lourde 7 Optimisée* (`Prot 7`, `+1 PA`, `125 000 CE`). Encombrement ignoré par les mises aveugles ; doublé hors déplacement sous *Ralenti* `(action + encombrement) × 2` ; Mikyai *Technicien-Guerrier* ignore `1` pt d'encombrement.
- **Boucliers Physiques (`1H`, §32.2)** : *Targe* (`+1` parade, `0 PA`), *Écu* (`+2` parade, `0 PA`), *Pavois* (`+2` parade, `+1 PA`, demi-couvert frontal, pare balles légères), *Bouclier Énergétique de Bras* (`+2` parade, `0 PA`, pare lasers légers et absorbe jusqu'à `5` dégâts d'*En Feu* de face).
- **Champs de Force Personnels (`1 L, 0 PA`, §32.2)** : `5` paliers cumulatifs en propriétés (`Absorbe 10/20/30/40/50 dégâts` avant armure et chair y compris *En Feu* ; Niv. 2 recharge hors combat en `1 min` ; Niv. 3 protège radiations/vide). Combinaisons Spatiales : *Légère 2–3* (`0 PA`, `48 h` d'air), *Lourde 3–6* (`1–2 PA`).
- **Pharmacopée & Matériel Médical (§32.3 — usage `1 PA`)** :
  - *Stimulants (non cumulables, contrecoup `+2 PE` à la fin ; 2e dose = `+2 PE` + jet *Système Immunitaire* Diff `6` ou *Empoisonné 2`)* : **Antidouleurs** (`+5 Seuil Létal, +2 Encaissement`, `2 h`, attention au dépassement fatal de `CON × 5` à l'expiration !) ; **Stimulant « Vif »** (état *Rapide*, `4 h`).
  - *Seringues* : `-5` (`900 CE`), `-10` (`2 000 CE`), `-20` (`5 000 CE`), généralisée `-10` (`2 500 CE`).
  - *Matériel médical* : Bandage compressif (`1 PA`, stoppe *Saignement*, `-2` dégâts, lève *Souffrant*), Attelle rigide, Trousse premiers soins (`+1`), Coffret chirurgie, Antidote universel (`1 PA`, dissipe *Empoisonné*), Antibiotiques (`+2` bactérien), Antiviraux (`+2` viral/parasitaire), Purge Arcanique / Correctif Cyber (`3 000 CE`).
- **Matériel de terrain (§32.4)** : *Masque Filtrant* (protège gaz/*Asphyxie* + neutralise éblouissement flash/gaz), Trousse de crochetage (`+1` Subterfuge), Caisse à outils, Valise Arcanotech, Tenue thermique (annule 1 palier climat), Menottes (Diff `6` acier / `9` magnétique), Radio (`5 km`), etc.

---

## Partie 8 — Temps & Causalité (Livre X, §39–§42)

### §39–§40. Lois de Causalité, Topologie & Distorsions
- **Propagation vers l'Avant vs Boucle Arrière (§39.1)** : Toute transmission d'information/matière vers le passé (*Boucle Arrière*) est une impasse entropique (cisaillement temporel, divergence incontrôlable). La voie stable est la **Propagation vers l'Avant** (exil dans le passé + ancrage physique qui mûrit à travers les siècles).
- **Effet Papillon Inversé & Théorème de l'Ancre Causale Descendante (§39.2)** :
  `Stabilité Causalité = (Masse Inerte × Temps Décantation) / Δ Perturbation ≥ 0.9999`.
  Repose sur `3` piliers : *L'Ancre Minérale* (inertie géologique millénaire), *Le Courant Vivant* (culture/lignée orale), *La Capsule Biométrique* (stase + verrou ADN à tolérance `0.0001%`).
- **Trous de Ver Spatiaux (§39.3)** : Raccourci purement spatial entre deux points contemporains (même front causal) → transit quasi-instantané sans friction ni paradoxe.
- **Goulots d'Étranglement Causals (§40.1–§40.2)** : Nœuds de convergence inaltérables (les PJ ne peuvent empêcher un goulot, seulement changer l'état et la préparation avec lesquels ils le traversent).
- **Échelle de Variance & Distorsions Temporelles (§40.3)** :
  - *Zone Neutre (`Variance ≤ 0.0001%`)* : `0` changement macroscopique, `0` séquelle.
  - *Onde Mineure* (vie locale / lieu secondaire) : souvenirs oscillent, **migraines chronales (`-1 PA` au 1er tour de chaque scène pendant `24 h`)**.
  - *Tension Majeure* (faction / bataille retardée) : dérive de phase de plusieurs mois, **épreuve de Sagesse Diff `18` ou perte temporaire d'`1` Dé de Constitution**.
  - *Rupture Causalité* (tentative de briser un Goulot) : rejet spatio-temporel, bulle de stase, **Dégâts Absolus = `CON × 3` + bannissement dimensionnel**.

### §41. Clairsentience & Prémonition (`8` Échelons)
| Échelon | Dénomination | Capacités & bonus mécaniques | Limites & pénalités |
| :---: | :--- | :--- | :--- |
| **1** | *Entrée — Visions Inconscientes* | Flashs spontanés involontaires | Aucune maîtrise ni activation volontaire |
| **2** | *Basique — Visions Partielles* | Bribes passé d'objet touché / futur (`quelques s`) | Flou, confusion souvenir/vision |
| **3** | *Intermédiaire — Visions Dirigées* | Sonder personne/porte/carrefour (`dans l'heure`) | `1 min` immobilité + concentration ; portée immédiate |
| **4** | *Avancé — Prédictions Court Terme* | Heures/jours ; **`+2 Initiative` et `+1 Dé en Esquive` au 1er tour d'embuscade** | Portée globale difficile, biais émotionnel |
| **5** | *Maîtrise — Rétro & Précognition Étendue* | Mois à années | Épuisement mental sévère, fusion souvenirs |
| **6** | *Expert — Arborescence des Futurs* | Voit simultanément les branches probabilistes d'une cible | **Saturation cognitive (`-2 PA` d'hésitation si échec test `INT`)** |
| **7** | *Élite — Feuilletage Temporel* | Perçoit chronotrames superposées, identifie la micro-variable de bascule | Instabilité psychique, risque dissociation d'identité |
| **8** | *Suprême — Vision Temporelle Infinie* | Omniprésence chronale passée/présente/future/fantôme | **Coma immédiat si la vision dépasse `10 secondes`** |

- **Techniques d'Anticipation en Combat (§41.2, à partir d'Éch. 3+)** :
  1. **Lecture d'Impact Prédictive (`2 PA`, Réaction)** : À l'annonce d'une attaque, accorde **`+1` Palier de Dé en Esquive ou Parade** contre cette attaque spécifique.
  2. **Verrouillage Probabiliste (`3 PA`, Action Principale, Éch. 6+ selon Appendice A)** : Désigne `1` case ; si la cible s'y rend ou y reste, toutes les attaques alliées contre elle ont une **réduction de difficulté de `-4`** jusqu'au tour suivant.
- **Simulateur de Chronotrames (`8` horizons)** : De `2 PA` (`quelques s`, risque `0 %`) à `4 PA` (`1–10 min`, `5 %`), `6 PA` (`1–24 h`, `15 %`), `8 PA` (`1 sem.–1 mois`, `30 %`), `12 PA` (`1 an–1 déc.`, `50 %`), `16 PA` (`1 siècle`, `75 %`), `20 PA` (`1 millénaire`, `90 %`), `25+ PA` (`temps géologique`, `99 %`).

### §42. Mécaniques Chronales en Jeu de Rôle
- **Règle des 3 empreintes (Reliques & Capsules Temporelles)** : *Empreinte biométrique* (ADN à marge `< 0.0001%`), *Journal de bord fermé* (erreurs passées, pas l'avenir omniscient), *Reliquaire physique* (ossements/équipement d'une itération antérieure).
- **Méthode MJ — Graphe Acyclique Dirigé (DAG)** : Nœuds = Goulots inévitables, arêtes = chemins libres modifiant ressources, alliances et état psychologique.
- **Les 4 Lois Temporelles (§42.2)** : `1.` *Vecteur Aval* (retour arrière `100×` plus énergivore) ; `2.` *Ancrage Inversé* ; `3.` *Inaltérabilité du Goulot* ; `4.` *Résistance Synaptique* (au-delà d'Échelon 4, consomme des PA et impose des malus prolongés).


---

## Partie 9 — Cadre Univers, Bestiaire, Pathologies, Outils & Appendices (Livres XI, XII, IX & Appendices A–B)

### Livre XI (§43–§47) — Cosmologie, Échelles Mondiales & Cadre Systémique

- **Hiérarchie Cosmique en `7` Niveaux Enveloppants** : `1. Univers` > `2. 36 Zones des Élianes` > `3. Galaxies` > `4. Systèmes Planétaires` > `5. Planètes & Lunes` > `6. Zones Géographiques` > `7. Villes & Points d'Intérêt`.
- **Genèse & Réseau des `36` Zones des Élianes (§43.1–§43.2)** :
  - Rupture causée par *Capricius* il y a `20 Ga` : surproduction toxique d'*Eau Sombre* submergeant le *Fleuve du Temps*, vouant l'univers à un effondrement thermodynamique d'ici `1 000 ans` (`100 000 ans` d'équations psychohistoriques et déploiement des *Disciples Cosmiques* sur toute anomalie divergente).
  - Les `36` Zones occupent en moyenne `~1,2 × 10⁴ Gly³` (~`23 Gly` de côté) dans un univers observable de `~93 Gly` de diamètre, hormis la **Zone 02 (*Aurion Nexus* / *Capricius*)**, dilatée aux dépens de l'anneau `05–10` et de la **Zone 01 (*Hybris Sector* / *Aethelgard*)**. *Hybris* se situe à `~1 milliard d'a.-l.` d'Aurion (`z ≈ 0,07`) : transit uniquement via relais sub-spatiaux, Fleuve ou trous de ver.
  - Les `5` entrées référencées dans `elianes` : `01` *Aethelgard* (Hybris Sector, gravité résonante & 5e Force primordiale, *Stable*), `02` *Capricius* (Aurion Nexus, Eau Sombre & Psychohistoire, *Critique*), `03` *Selene-Vor* (Cléia Astra, Magie inhérente, *Actif*), `04` *Goroth-Kahn* (Karkjiue Rim, cinétique brute & densité tellurique, *Contrôlé*), `05–36` (*Les 32 Autres Archontes*, partiellement submergés).
- **Galaxies (`3`) & Systèmes Stellaires (`5`) (§44)** :
  - *Galaxies* : **Galaxie d'Aurion / Empire Central** (Zone 02, fondée il y a `22 Ma`, maillage tentaculaire sur corridors rentables laissant les mondes non stratégiques intouchés), **Galaxie Principale / Voie Centrale** (Zone 01, expansion humaine ; Terre primitive du XXIe siècle intouchée), **La Galaxie Sans Étoiles** (faille causaliste hors-cartes).
  - *Systèmes* : **Hybris-Prime** (naine jaune *Hybris-Alpha*), **Janus** (binaire rouge-orangée *Vardis-Major* : *Vardis*, *Nefris*), **Veyla** (*Veyla-Mère* + compagne *Veyla-Sœur* : *Cléia* + satellites capteurs), **Karkjiue** (géante rouge *Kark-Sol* : *Karkjiue* + bassins miniers), **Système Solaire (Sol)** (Terre, Mars, stations Jupiter — hors contact).
- **Paramètres Systémiques des `5` Planètes Majeures (§45)** :

| Planète | Système / Affil. | Technologie | Magie (5e Force) | Gravité | Atmosphère & Contraintes | Factions / Peuples |
| :--- | :--- | :--- | :--- | :---: | :--- | :--- |
| **Hybris** *(An 1771 HSC)* | Hybris-Prime / Indép. | Médiévale à Avancée (`3–4`) | Normale à Inhérente | `1.02 g` | Azote-Oxygène pure | Humains (~3 000 déportés An 0), Nains, Tauriens, Dereks indigènes |
| **Vardis** | Janus / Empire | Avancée (`4–5`) | Limitée (Réglementée) | `1.15 g` *(Lourde)* | Aride, poussières industrielles | Garnison Impériale, Résistance d'Atraxia, Tribus Thaluns |
| **Nefris** | Janus / Ruines | Hétérogène (Prim. → Avancée) | Normale (Tribale) | `0.94 g` *(Légère)* | Toxique localement **(Masque requis)** | Mercenaires, tribus locales, ingénieurs impériaux, Base Astéroïde |
| **Cléia** | Veyla / Matriarcat | Très Avancée (`5`) | Inhérente (Instinctive) | `0.88 g` | Aride tempérée, faible hydrologie | Cléiennes (`100 %` féminine, cités-tubes minérales) |
| **Karkjiue** | Karkjiue / Protectorat | Avancée (Blindages & Balistique) | Impossible *(rares exc. limitées)* | `1.28 g` *(Très lourde)* | Volcanique, basaltes brûlants | Castes militaires Mikyais (divisions de choc impériales) |

- **Mécaniques Lunaires d'Hybris & Structures Clés (§45.3–§47)** :
  - **Nyth-1 (Blanche Lune Arcanique)** : Orbite rapide **`~9 jours`**, régolithe de poussière de nytharite. À chaque *Nuit Blanche* (pleine lune) : **la 5e Force monte d'un cran** (sorts et moteurs suralimentés, minage faste, mais Eau Sombre amplifiée ; les galeries naines « chantent » et attirent les Dereks).
  - **Storm-2 (Orageuse Lune Électrostatique)** : Orbite externe lente, conjonctions tous les **`~3 ans`**. À chaque *Conjonction d'Orage* : **tempêtes arcaniques, pannes d'arcanotech et téléportations instables**.
  - **Vaisseaux & Bastions (§45.3, §46–§47)** : *L'Aeschylus* (vaisseau pirate warp à boucliers déflecteurs et FTL, volé à Ferron), *Base Astéroïde de Nefris* (QG insurgé), *Continent Est / Terres Interdites* d'Hybris (barrière de brumes chronales et vieillissement accéléré), et les cités/lieux tactiques (*Brum'korath*, *Haliriel*, *Kingston*, *Le Guetteur*, *Village Nythari*, *Portes Naines*, *Atraxia*, *Nexus d'Aurion*, *Bases clandestines A13 & B5*, *Poste Céleste de l'Aeschylus*).

---

### Livre XII (§48–§53) — Bestiaire, Calibrage & Générateur Modulaire de Pathologies

#### §48. Rangs de Menace & Table de Calibrage
- **3 Rangs de Menace (§48.1)** :
  1. **Rang I — Minions (*« Petites Merdes »*)** : `11` pts d'attributs (`max 3`), `0 PM`. Dès qu'un Minion subit une blessure **`> CON × 2`** (Encaissement) ou reçoit un **coup critique net**, il est immédiatement hors de combat sans jet d'agonie.
  2. **Rang II — Réguliers** : `2` attributs à `4` + `12` pts partagés (`max 3`), `0–1 PM`, Bagage `3` entraînements de départ, réactions complètes et équipement standard.
  3. **Rang III — Élites & Boss** : attributs `5` à `8+`, `2–3 PM`, immunité à la plupart des états mentaux mineurs, attaques de zone et actions spéciales hors tour.

| Niveau de Menace (§48.2) | Composition Type | Ratio de PA | Objectif Tactique |
| :--- | :--- | :---: | :--- |
| **Facile / Échauffement** | `4–6` Minions OU `1` Régulier + `2` Minions | `0.8 × PA des PJ` | Entamer quelques PA et ressources sans risque vital |
| **Standard / Équilibré** | `3–4` Réguliers (ex. Patrouille Bandits 1–3) | `1.0 × PA des PJ` | Duel tactique, gestion du terrain et des couverts |
| **Périlleux / Embuscade** | `1` Élite (ex. Illumo) + `4` Réguliers OU `8` Minions organisés | `1.3 × PA des PJ` | Danger de mort, gestion impérative des soins d'urgence |
| **Létal / Boss Cosmique** | `1` Boss Suprême (*Bête*, *Minulican*, *Disciples*) | `Spécial (PA doublés + Actions Légendaires)` | Survie collective, exploitation des faiblesses et synergies |

#### §49–§52. Inventaire des `14` Fiches du Bestiaire (`creatures`)

| Créature | Rang / Classe | PA | CAP / MAX | Armure / PM | Caractéristiques & Capacités Signature |
| :--- | :--- | :---: | :---: | :---: | :--- |
| **Bandit #1 (Épéiste)** | Rang II Régulier | `7` | `6 / 15` | `0` PM | `FOR 4 (1d6), CHA 4 (1d6)` ; Épée `2D` sur `1d6` ; *Coup au Cœur* (`3 PA, -1, dégâts ×2 torse`) ; *Parade Riposte* (`2 PA, Δ≥2 = contre gratuit`). |
| **Bandit #2 (Arcaniste)** | Rang II Régulier | `8` | `6 / 15` | `0` PM | `MAG 7 (1d8), INT/INS 4` ; *Étreinte de Gravité* (`2 PA, 12m, 3 résiduels + À Terre`) ; *Souffle Igné* (`3 PA, cône 4m, 4 feu + Enflammé 2/t`) ; *Bouclier Répulsion* (`2 PA réact, +3 déf`). |
| **Bandit #3 (Tireur)** | Rang II Régulier | `10` | `6 / 15` | `0` PM | `AGI/RAP 4` ; Carabine `3D, portée 25, 1d6` ; *Tir Préparé* (`4 PA` interruption couvert/sort) ; *Prise d'Otage* (`6 PA` sur cible à terre/essoufflée). |
| **La « Petite Merde »** | Rang I Minion | `5` | `4 / 10` | `0` PM | Attaque `1d2` (gourdin `1D` / pistolet `2D`) ; neutralisé d'office dès `≥ 4` dégâts en une attaque. |
| **Illumo, le Tueur Fou** | Rang III Élite | `10` | `8 / 20` | `2` PM | `FOR/AGI 5, MAG Feu 8 (1d10)` ; Dague `3D + 2 brûlure rés.` ; *Combustion Spontanée* (`4 PA, 18m, sans projectile, 5 + Enflammé`) ; *Foulée de Cendres* (`2 PA, TP 6 cases + Aveuglé 1t`). |
| **La Bête d'Hybris** | Rang III Boss | `13` | `12 / 30` | `3` PM | `FOR 7 (1d8+2), CON/INS 6` ; *Morsure Sabre* (`3 PA, 5D, Hémorragie/Saignement si ≥5 nets`) ; *Balayage Griffes* (`4 PA, 3 adj., 4D + proj. 4m/À Terre`) ; *Mémoire des Flammes* (`-2` feu cumulatif) ; *Charge Tellurique* (`5 PA, 10 cases, 3D zone`). |
| **Les Dereks (Indigène)** | Rang II Régulier | `6` | `10 / —` | Arm. Nat. `3` | `FOR/CON 5, Magie Tellurique 4` ; *Peau de Granit* (`-3` physiques) ; *Séisme Souterrain* (`3 PA, 3 cases, AGI sinon À Terre & -2 PA`). |
| **Le Minulican** | Boss Fléau | `14` | `12 / 30` | `3` PM | `MAG 9 (1d10)` ; *Éclairs de Néant* (`4 PA, 3 cibles 15 cases, 5D + Déstabilisé -2 EC`) ; *Absorption Thermique* (`2 PA réact, feu/plasma → ½ soin`) ; *Saut Quantique* (`3 PA, TP +2 EC`). |
| **Disciple de Terre** | Boss Cosmique *(1re gén., ~1 Ga, choc 1772 HSC)* | `5` | `18 / 45` | Arm. Magmatique `6` | `FOR/CON 9 (1d10)` ; *Poing Tectonique* (`3 PA, 7D`) ; *Régén. Lithosphérique* (`+3/t` sur roche) ; **Faiblesse *Choc Thermique*** (alternance froid/feu → perd armure + dégâts `×2` pendant `2` tours). |
| **Disciple de l'Air** | Boss Cosmique *(2e gén., ~1620 HSC)* | `6` | `14 / 38` | Voile `5` | `AGI/RAP 9 (1d10)` ; *Cisaille Stratosphérique* (`3 PA, ligne 6 cases, 6D, ignore armures non scellées`) ; *Déplacement Sursaut* (`2 PA`) ; **Faiblesse *Saturation Particulaire*** (poussière/brume/sable → perd voile + `×2` 2 tours). |
| **Disciple du Feu** | Boss Cosmique *(2e gén., dormant)* | `5` | `18 / 45` | Couronne Fusion `6` | `FOR 8, CON 9` ; *Lance Solaire* (`3 PA, 7D + enflamme terrain 2t`) ; *Régén. Fournaise* (`+3/t` chaleur) ; **Faiblesse *Asphyxie*** (vide/immersion/sable → perd armure & régén. 2 tours). |
| **Disciple de l'Eau** | Boss Cosmique *(2e gén., dormant)* | `5` | `16 / 42` | Enveloppe Abyssale `5` | `AGI/CON 8` ; *Étreinte Abyssale* (`3 PA, 3 cases, 6D + À Terre`) ; *Infiltration* (`2 PA` eau↔eau) ; **Faiblesse *Assèchement*** (chaleur sèche/sel/sable → perd armure + `×2` 2 tours). |
| **Drone Surveillance** | Rang II Automate | `8` | Coque `8` | Vol `12 cases/t` | `AGI 4` ; *Flash / Impulsion* (`3 cases, CON sinon Sonné 1t`) ; *Balise* (`Tour 2` appelle `2` Drones en `3` tours). |
| **Molosse Bionique** | Rang II Automate | `9` | `—` | `—` | `FOR/AGI/CON 4` ; *Morsure Hydraulique* (`3 PA, 3D` + verrou *Immobilisé* & `2` dégâts auto/début de tour). |

#### §53. Générateur Modulaire de Pathologies
- **Formule de l'Indice de Gravité (`IG`)** :
  `IG = Nature + Transmission + Virulence + Cadence/Incubation + Stades (+1 dès le 2e, max 6) + Effets + Modulateurs`
  - *Règle anti-double comptage (Mode Remplacement)* : un même effet reconduit à l'identique d'un stade à l'autre ne compte qu'une fois dans l'IG ; s'il monte d'un palier (ex. `-1 EC → -2 EC`), on ajoute uniquement la différence entre les deux paliers.
- **5 Classes de Gravité** :
  - **Classe I — Bénigne (`IG 1–8`)** : `1–2` stades légers, repos/soin léger.
  - **Classe II — Commune (`IG 9–16`)** : `2–3` stades, malus nets ou contagion rapide ; Premiers Soins & repos sous veille.
  - **Classe III — Sévère (`IG 17–26`)** : `3–4` stades, états lourds/crises ; soignant entraîné + kit/remède dédié.
  - **Classe IV — Critique (`IG 27–37`)** : `4–6` stades dégénératifs ou foudroyants ; Médecine Avancée, chirurgie ou atelier.
  - **Classe V — Fléau (`IG 38+`)** : peste majeure, cumul ou souche mutante/arcanique ; quarantaine, cure dédiée ou rituel majeur.
- **Nature de l'Agent (`1d6`, §53.1)** *(Automates immunisés à `1–4`, exposés à `5–6`)* :
  - `1` **Bactérien (`+0 IG`)** — Premiers Soins/Médecine + antibiotiques (seringue seule guérit `0` infection).
  - `2` **Viral (`+1 IG`)** — Repos sûr + veille de soignant ; vaccin préventif (antibiotiques inefficaces).
  - `3` **Parasitaire / Fongique (`+1 IG`)** — Extraction/chirurgie ou antifongique dédié.
  - `4` **Toxique / Prion (`+2 IG`)** — Antidote/purge/dialyse ; non contagieux par défaut, séquelles fréquentes.
  - `5` **Arcanique (`+3 IG`)** — Magie curative / rituel de purge + catalyseur (médecine courante stabilise sans guérir).
  - `6` **Nanotech / Cyber (`+2 IG`)** — Atelier *Ingénierie & Arcanotech* + purge IEM/électromagnétique.
- **Transmission, Virulence & Cadence (§53.2)** :
  - *Vecteurs* : *Non transmissible* (`+0`), *Sang/eau/nourriture* (`+1`, ébullition/filtrage annule), *Contact/gouttelettes* (`+2`, gants/quarantaine `+2` ou `0` jet), *Animal/zone/arcanique* (`+2`, 1 jet/scène), *Aérien aérosol/spores* (`+3`, *Masque Filtrant* `+2 à +4` ou immunité si étanche). *Modulateurs* : **Contagion silencieuse** (`+2`, contagieux dès incubation), **Vecteur supplémentaire** (`+2`).
  - *6 Paliers de Virulence (`Diff CON`, calés §7.1)* : **Bénigne** `Diff 3 (+0 IG, auto 1d8)`, **Commune** `Diff 5 (+1 IG, auto 1d12)`, **Sévère** `Diff 7 (+2 IG, auto 2d8)`, **Grave** `Diff 10 (+3 IG, auto 2d12)`, **Mortelle** `Diff 14 (+4 IG)`, **Cataclysmique** `Diff 18 (+5 IG)`.
  - *Incubation* : *Immédiate/Aiguë* (`min–1d6 h, +1 IG`), *Standard* (`1d6 j, +0`), *Lente* (`1d6 sem. ou 1 épisode, +0`).
  - *Cadence* (l'IG retient la plus rapide des stades actifs) : *Foudroyante* (`1d6 h/stade, +3`), *Rapide* (`12–24 h/stade, +2`), *Standard* (`2–3 j/stade, +1`), *Lente/Chronique* (`1–2 sem. ou 1 épisode/stade, +0`).
- **Stades (`1–6`) & 5 Paliers d'Effets (§53.3)** :
  - *Modes* : **Remplacement (`+0 IG`)** (effets du stade courant seuls) vs **Cumulatif (`+2 IG` sur `2–3` stades, `+3 IG` sur `4–6` stades)** (cumule tous les stades précédents sans additionner deux fois le même état).
  - *Transition par défaut* : jet gratuit (`0 PA`, sans mises libres de PA hors combat ; PE volontaire/dons/auto permis) de **Système Immunitaire (`CON`) vs Virulence** à chaque échéance (`Réussite = stade − 1` ou guérison depuis S1 ; `Échec = stade + 1`). Dégâts pathologiques par cycle = dégâts directs ignorant armure/bouclier ; résiduels de stade posent leurs effets persistants et déclenchent leurs dégâts/tour en scène tendue/combat.
  - *5 Paliers d'Effets* :
    1. **Léger (`+1 IG`)** : `-1 EC` ciblé, état léger (*Étourdi/Affaibli/Assourdi*/priv. 1), plancher `+1 ESS`, `1` dégât direct/cycle.
    2. **Modéré (`+2 IG`)** : `-2 EC` ciblé, Repos sûr `÷2`, état modéré (*Déstabilisé/Ralenti/Aveugle/À Terre*), drain `-1 PA max` (ou `-1 CAP/ESS`), `2–3` dégâts directs/cycle ou Résiduel `1–2` (*Empoisonné 1–2, Saignement 2, En Feu 2*), *Souffrant pathologique*.
    3. **Sévère (`+3 IG`)** : blocage total Repos sûr (`0` dégât, ESS conservé), état sévère (*Immobilisé/Paniqué/Sonné 1t*/priv. 2–3), drain `-2 PA max` ou `-2 CAP` (min 1), `4–5` dégâts directs/cycle ou *Asphyxie 2*.
    4. **Critique (`+4 IG`)** : *Paralysé* / coma *Inconscient* (non réveillable par secousse), `CON` dégâts directs/cycle ou Résiduel aggravé en crise/combat (`×2, +1/t`), crise d'*Agonie* (Diff `4 + 1/t`).
    5. **Terminal (`+5 IG`)** : Seuil Létal `CON × 5` à l'échéance.
- **12 Modulateurs de Pathologie (§53.4)** : *Auto-limitée (`-1`)*, *Mode Cumulatif 2–3 stades (`+2`)*, *Mode Cumulatif 4–6 stades (`+3`)*, *Sans rémission spontanée (`+2`, réussite stabilise sans faire reculer sans traitement)*, *Escalade rapide (`+2`, échec `≥ 4` ou critique = `+2` stades)*, *Symptômes masqués (`+1`, `+2 Diff` diagnostic)*, *Résistance au traitement (`+2`, labo/rituel/atelier ou `2` soins pour `-1` stade)*, *Porteur sain (`+1`, contagieux `1d6 j` sans symptôme)*, *Chronicité (`+1`, S1/S2 permanent + réveil sur stress)*, *Séquelles durables (`+1`, *Affaibli* `10 − CON` j ou `-1 CAP`)*, *Souche mutante (`+2`, `1d6` par foyer : `5` Virulence `+2` max `22`, `6` nouveau vecteur/résistance)*, *Immunité courte/nulle (`+1`, supprime les `1d6 mois` d'immunité par défaut)*.
- **Diagnostic, Traitement & Exemples (§53.5–§53.6)** :
  - *Diagnostic* : *Académie / Premiers Soins / Médecine Avancée* Diff = `max(4, Virulence)` (`+2` si *Symptômes masqués*, `+2` sans labo/rituel/atelier pour Arcanique/Cyber, `+2` au jet avec *Diagnostic Vital*).
  - *Traitement adapté (`3 PA` / 1 scène)* : Réussite simple = `+2` prochaine transition (+ stabilise *Agonie* / suspend résiduel 1 scène) ; marge `3+` ou remède spécifique = rémission immédiate `-1 stade` (`2` soins si *Résistance*). `CON 1–2` démarre à `+1` stade si souche `≥ 2` stades ; *Affamé/Assoiffé/Somnolent* pénalisent *Système Immunitaire*.
  - *4 Exemples Assemblés* : **Rhume des Cales** (`Classe I, IG 4`, Viral, Contact, Vir. 3, 1 stade 2j) ; **Fièvre des Brumes** (`Classe II, IG 16`, Viral, Aérien, Vir. 5, 3 stades) ; **Fièvre des Tranchées** (`Classe III, IG 24`, Bactérien, Sang/eau, Vir. 7, 4 stades aiguë 1d6h) ; **Peste de Nynth** (`Classe V, IG 45`, Arcanique, Aérien + silencieuse, Vir. 10, 6 stades cumulatifs, mutante, sans rémission spontanée).

---

### Livre IX (`livre_9.html`, `fiche_pj.html`, `js/tools.js`, `js/fabrique.js`) — Suite d'Outils Web Interactifs

Architecture `vanilla JS` zéro dépendance chargeant en priorité `data/livre_{1,2,3,4,7,10}.json` dans `CodexData` avec miroirs de repli intégrés, répartie en **`6` modules** (chap. `33` à `38`) :

1. **Chap. 33 — Créateur de Fiche PJ Dynamique (`initCharacterSheet`) & Fenêtre Détachée (`fiche_pj.html`)** :
   - Gestion multi-fiches jusqu'à **`12` onglets** (`rp_codex_characters_v1` + compatibilité `rp_codex_character`, synchro `BroadcastChannel`, pont `FabriqueCharStore` par onglet, autosave serveur si connecté sinon `localStorage`, export texte/Markdown & JSON).
   - Validateur temps réel des budgets *Héros PJ*, *PNJ Élite* et *PNJ Sbire* (`clampCreationRanges`, option Éveillé `+2` pts si `Magie ≥ 1`, échange automatique des valeurs lors du changement de piliers).
   - `computeBagage(race, giftIdx)` applique `min(5, 3 + mech.bagage + pureRoll)` (+1 don Humain *Touche-à-tout*, +1 *Tirage historique pur*) ; `giftMechanics` applique en temps réel `+2 CAP` (*Derek A*, *Mikyai A*) et `+1 ESS` (*Humain A*, plus résiduel *Vardien A*).
   - `refreshSkillDice(eff)` calcule `skillBaseScore = Math.ceil(moyenne)` sur les 27 compétences (y compris `vision`/`ouie`) ; règle `data-mag` (*Corps Augmenté* portée sur `Mains Nues` dans `livre_9.html`) remplace `score` par `Math.max(score, ext.mag)` dès `ext.mag > 0`, puis `stepDie(baseDie, lvl)` et affiche l'effort max `1 + lvl`.
2. **Chap. 34 — Atelier de Création Modulaire de Sorts (`initSpellWorkshop`)** :
   - Implémente `1 XP = 1 PA` sur `4` attributs sources (*Magie*, *Force*, *Agilité*, *Intelligence*) et `3` catégories (*Offensif*, *Défensif/Utilitaire*, *Social*). Surcoût hybride `+1 XP` par catégorie supplémentaire au-delà de la 1re, exclu du coût PA d'activation (`activationPa = totalEffectCost`). Export carte presse-papier.
3. **Chap. 35 — Lanceur Polyédrique & Critiques (`initDiceRoller`)** :
   - Échelle `1d2` à `2d12+10` + effort injecté (`+1/PA` ou `PE`) + modificateur situationnel vs `ND` (`0–34`) ou jet opposé. Détecte réussite critique (`r === cfg.sides`) et échec critique (`r === 1 && cfg.count === 1`), tire le *Jet de Conséquence* sur `1..Math.min(24, cfg.sides * cfg.count)` et consulte la table canonique rabattue (`13` paliers sur `1..24`).
4. **Chap. 36 — Résolveur de Challenges (`initChallengeResolver`)** :
   - Pilote `3` à `7` étapes avec `0` à `3` rattrapages/étape (`defiParams()` depuis `livre_2.json`). Calcule `reward = (successes × 2) − totalRetries`, appliqué uniquement à la fin avec plancher `minimumValide = 2`, et force **`reward = 0`** si `successes === 0` ou si `pending === 0 && successes < Math.ceil(challengeData.length / 2)`.
5. **Chap. 37 — Simulateur de Chronotrames (`initClairsentienceSimulator`)** :
   - Affiche pour les `8` paliers (*Quelques secondes* → *Temps géologique*) l'horizon temporel, le coût de maintien (`2 PA` → `25+ PA`), l'état des goulots et le risque de paradoxe (`0 %` → `99 %`).
6. **Chap. 38 — Fabrique de Techniques Maison (`js/fabrique.js`, `FabriqueEngine`)** :
   - Éditeur nodal interactif (`◆` Châssis `C0–C4` + `1` à `3` logements `⊕` reliés à une compétence entraînée de la fiche). Implémente le filtrage de familles compatibles par compétence (`competences_par_nom`), les modificateurs sur carte (`Portée 1–3`, `Zone M3`, `Durée M4`, `M7 ×2`, hybride `M6` auto), les `11` pièces à paramètres variables (`KIND` : `O1, O8, O9, O13, D9, O15, D8, S1, U7, U11, O28`), les `3` montages pré-vérifiés (*Suture Réflexe 6 XP*, *Lame Miroir 3 XP*, *Fente Basse 4 XP*), le plafond `6 PA` et la mention **Signature MJ** dès `7 XP`.

---

### Appendice A (`data/appendice_actions.json`, v2.4) — Compendium des `101` Actions

- **Schéma JSON d'une entrée (`27` clés)** : `id`, `nom`, `categorie`, `types`, `cadence`, `cadence_cle`, `cout_pa`, `cout_pa_groupe`, `cout_pa_tri`, `contexte`, `timing`, `portee`, `jet`, `requiert_jet`, `consomme_reaction_libre`, `silencieux`, `competences`, `attributs`, `limite`, `ralenti_effet`, `resume`, `procedure`, `conditions`, `modificateurs`, `source`, `refs`, `tags`.
- **Répartition (`101` actions sur `7` catégories)** : `A.1 offensif (21)` · `A.2 defensif (15)` · `A.3 mouvement (13)` · `A.4 micro (15)` · `A.5 soins (10)` · `A.6 arcanes (11)` · `A.7 collectif (16)`. Total : `6` actions avec `consomme_reaction_libre = true` (`prise-de-coup`, `frappe-opportunite`, `balayage-opportunite`, `poursuite-opportunite`, `blocage-opportunite`, `reprendre-souffle`).

#### A.1 Actions Offensives (`21`)
| ID | Nom | PA | Cadence | Timing | Règle / Résolution clé |
| :--- | :--- | :---: | :--- | :--- | :--- |
| `frapper-cac` | Frapper au Corps-à-Corps | `2 PA` | Principale | Son tour | VS Esquive/Parade ; contact (`2P` allonge) ; `1/t` (`2` dès `2d6+`). |
| `tirer-distance` | Tirer à Distance | `2 PA` | Principale | Son tour | Ballistique VS Esquive/Parade spé. ; `1/t` (`2` dès `2d6+`). |
| `visee-chirurgicale` | Visée Chirurgicale (Niv. 2) | `+0 à +2 PA` *(ou `-1/-2`)* | Modificateur | Phase 1 att. | Points vitaux dans les 6 zones larges ; `1/attaque`. |
| `charge-assaut` | Charge d'Assaut | `≥3 PA + 2 PA` | Principale | Son tour | `≥3` cases d'élan en ligne ; impossible sous *Ralenti*. |
| `action-progression` | Action en Progression | `1–2 PA (N) + coût (N+1)` | Manœuvre | Tour `N → N+1` | `+2` au jet par PA misé (plafonné dans `1 + entr.`). |
| `lancer-grenade` | Lancer une Grenade | `2 PA` *(1 PA Dégoup.)* | Principale | Son tour | Épreuve Ballistique (`30P` / `20` cases, `+2` *Grenadier*). |
| `tir-retention` | Tir en Rétention | `2 PA` | Principale | Son tour | Arme longue avec ennemi adjacent : Ballistique à `-2`. |
| `tir-vise-retention` | Tir Visé de Rétention | `3 PA` | Principale | Son tour | Annule `-2` de *Canon Entravé* et débloque visée. |
| `coup-crosse` | Coup de Crosse ou de Canon | `2 PA` | Principale | Son tour | Maniement d'Arme au contact (`3–4` dégâts, *Déstabilisé* si `>CAP`). |
| `frappe-martiale-contact` | Frappe Martiale au Contact | `2 PA` | Principale | Son tour | *Mains Nues* plein dé sans *Canon Entravé* + *Refoulement*. |
| `prise-cac` | Prise au Corps-à-Corps (Lutte) | `2 PA` | Principale | Son tour | Athlétisme/Mains Nues VS ; pose *Immobilisé*. |
| `etranglement` | Étranglement (sur Prise) | `2 PA` | Principale | Son tour | Athlétisme vs Endurance Physique ; pose *Asphyxie*. |
| `frapper-cible-hors-etat` | Frapper Cible Hors d'État | `2 PA` *(ou coût)* | Principale | Son tour | *Inconscient* : 0 jet ; *Paralysé* : vs `0` ; *Sonné* : vs Parade `-2`. |
| `attaque-depuis-cache` | Attaque depuis Caché | `2 PA` *(ou sort)* | Principale | Tour / Embusc. | VS Défense Passive (`0`, pose *Surpris*) sur 1re attaque non repérée. |
| `seconde-action-principale` | Seconde Action Principale | Coût normal (`2+ PA`) | Principale | Après tour d'un autre | Requiert compétence offensive `≥ 2d6` ; max `2` actions/tour. |
| `effet-repousser-refoulement` | Repousser & Refoulement | `0 PA` | Déclenchée | Sur touche | `Δ ≥ 1`, Choc (`> CON×2`) ou Critique. |
| `effet-attirer` | Attirer (Grappin / Télékinésie) | `0 PA` | Déclenchée | Sur touche | `Δ ≥ 1`. |
| `effet-desarmer` | Désarmer un Adversaire | `0 PA` | Déclenchée | Sur touche | `Δ ≥ 5 + 3 entr.` martiaux ou Visée Niv. 2 *Poignet*. |
| `effet-assommer` | Assommer | `0 PA` | Déclenchée | Sur touche | `Δ ≥ 10` + Visée *Crâne* (Niv. 2). |
| `effet-saigner-bruler` | Déclencher Saigner / Brûler | `0 PA` | Déclenchée | Sur touche | `Δ ≥ 5` avec lame/perforant ou feu/plasma. |
| `tour-embuscade` | Déclencher Tour d'Embuscade | Réserve complète | Principale | Avant Init. R1 | Tour d'avance, sans recharge avant son rang d'Init. (`1×/combat`). |

#### A.2 Défenses, Postures & Réactions (`15`)
| ID | Nom | PA | Cadence | Réact. Libre | Règle clé |
| :--- | :--- | :---: | :--- | :---: | :--- |
| `parade` | Parer | `1 PA` *(ou `0 PA` à `-2`)* | Réaction Déf. | False | Illimité / cycle ; ×2 PA base sous *Ralenti*. |
| `esquive` | Esquiver | `1 PA` *(ou `0 PA` à `-2`)* | Réaction Déf. | False | Illimité / cycle ; ×2 PA sous *Ralenti* (hors *Esquive Réactive*). |
| `defense-passive` | Défense Passive | `0 PA` | Réaction Déf. | False | Défense = `0` volontaire ou imposée (*Surpris / Paralysé*). |
| `changer-posture` | Prendre/Changer de Posture | `0 PA` | Gratuite | False | `1` posture active à la fois (*Préventive/Défensive/Offensive*). |
| `guet` | Se Mettre en Guet (`360°`) | `2 PA` réservés | Manœuvre | False | Déclenche 1 tir auto (mises à `0`) ; expire fin prochain tour. |
| `prise-de-coup` | Prise de Coup (Garde du Corps) | `1 PA` | Réact. Libre | **True** | Protège allié à `≤ 1` case ; `1/cycle`. |
| `frappe-opportunite` | Frappe d'Opportunité | `0 PA` | Réact. Libre | **True** | Sur retrait sans *Décrochage* ; `1/cycle`, mises à `0`. |
| `balayage-opportunite` | Balayage d'Opportunité | `0 PA` | Réact. Libre | **True** | Vise Jambes → *À Terre* & *Ralenti* ; `1/cycle`, mises à `0`. |
| `poursuite-opportunite` | Poursuite d'Opportunité | `1 PA` | Réact. Libre | **True** | Suit de `1` case max sans attaque ; `1/cycle`. |
| `blocage-opportunite` | Blocage d'Opportunité | `1 PA` | Réact. Libre | **True** | Athlétisme VS Athlétisme (mises `0`) annule le pas ; `1/cycle`. |
| `choisir-posture-opportunite` | Régler Posture d'Opportunité | `0 PA` | Gratuite | False | Sélectionne gratuitement le type d'opportunité actif. |
| `se-liberer-prise` | Se Libérer d'une Prise | `2 PA` | Manœuvre | False | Athlétisme/Mains Nues/Acrobatie VS lutte ; dissipe *Immobilisé*. |
| `relacher-prise` | Relâcher Volontairement Prise | `0 PA` | Gratuite | False | À tout moment. |
| `se-liberer-liens` | Briser ou Crocheter Liens | Variable | Manœuvre | False | Épreuve Force/Athlétisme ou Subterfuge/Acrobatie (*Ligoté*). |
| `garder-reserve-passer` | Conserver PA / Fin de Tour | `0 PA` | Gratuite | False | Conserve PA pour parades/esquives/mises hors tour. |

#### A.3 Mouvement, Poursuite & Furtivité (`13`)
| ID | Nom | PA | Cadence | Timing / Règle clé |
| :--- | :--- | :---: | :--- | :--- |
| `se-deplacer` | Pas Tactique (`1` case = `1,5 m`) | `1 PA / case` | Micro-Action | Son tour ; `+1 PA/case` sous *Ralenti* ou terrain difficile. |
| `decrochage` | Décrochage (Retrait Sécurisé) | `1 PA` d'activation | Manœuvre | Couvre tout le tour contre les opportunités (`2 PA` sous *Ralenti*). |
| `accroupir-a-terre` | S'Accroupir / Se Mettre À Terre | `0 PA` | Gratuite | Son tour ; se relever coûte `1 PA` (`2 PA` sous *Ralenti*). |
| `se-relever` | Se Relever | `1 PA` | Micro-Action | Son tour (`2 PA` base sous *Ralenti* + enc. `×2`). |
| `trainer-corps` | Traîner un Corps | `2 PA / case` | Manœuvre | Corps en prise, consentant ou inconscient (`+1 PA/case` sous *Ralenti*). |
| `nager-escalader` | Nager ou Escalader | `2 PA / case` *(1 PA Franch.)* | Manœuvre | Son tour ; épreuve Force si milieu périlleux. |
| `acrobatie-saut-chute` | Acrobatie, Saut & Chute | `1–2 PA` | Manœuvre | Épreuve Acrobatie ou Chute (`H − 2` dégâts hors armure). |
| `voler-leviter` | Voler ou Léviter (3D) | `÷2 PA` (Vol) / `1 PA/case` (Lév.) | Manœuvre | Ignore sol ; `-1 EC` en vol sans maîtrise. |
| `fuir-poursuivre` | Course & Poursuite | `4 PA` | Principale | Fuyant `+30` cases (`÷2` sous *Ralenti*) ; poursuivant `±1` case via Athlétisme. |
| `plaquage-poursuite` | Plaquage en Poursuite | `0 PA` | Déclenchée | Automatique dès arrivée à `1` case : Athlétisme VS Athlétisme. |
| `se-cacher` | Se Cacher | `2 PA` *(1 PA Ombres)* | Manœuvre | Hors de vue ; Discrétion secrète plancher `4`. |
| `deplacement-furtif` | Déplacement Furtif | `+1 PA / case` | Manœuvre | Total `2 PA/case` (`+0` *Déplacement silencieux*) tant que hors de vue. |
| `approche-furtive` | Approche Furtive (Hors Combat) | Hors combat | Hors-Combat | Discrétion VS Observation/Écoute pour ouvrir un Tour d'Embuscade. |

#### A.4 Micro-Actions, Équipement & Effort (`15`)
| ID | Nom | PA | Cadence | Règle clé |
| :--- | :--- | :---: | :--- | :--- |
| `mise-effort-pa-pe` | Mise d'Effort (`+1/PA` ou `PE`) | `1 PA` ou `1 PE` | Modificateur | Plafond `1 + entraînements` (PA, PE, Progression, *Survolté*, *En transe*). |
| `prise-essoufflement` | Prise d'Essoufflement | `0 PA` (`+1 PE = +1 PA`) | Modificateur | Quand PA vides ; plafond volontaire = `CON` (`+1` *Endurance Coloniale*). |
| `reprendre-souffle` | Reprendre son Souffle | `1 PA / PE` *(0 PA Cardio 1×)* | Micro / **Réact. Libre (`True`)** | Illimité son tour ; `1/cycle` hors tour en Réaction Libre. |
| `degainer-ranger` | Dégainer ou Ranger | `1 PA` | Micro-Action | Son tour (`×2` sous *Ralenti*). |
| `recharger-arme` | Recharger une Arme | `1 PA` | Micro-Action | Arme à feu/arc/arbalète (`×2` sous *Ralenti* ou propriété *Rechargement lent*). |
| `ouvrir-fermer-porte` | Ouvrir / Fermer une Porte | `1 PA` | Micro-Action | Porte non verrouillée. |
| `ramasser-objet` | Ramasser un Objet au Sol | `1 PA` | Micro-Action | Sa case ou adjacente (`≤ 1` case). |
| `lacher-objet` | Lâcher un Objet en Main | `0 PA` | Gratuite | Possible même en *Dernier Souffle*. |
| `utiliser-potion-objet` | Utiliser Potion / Seringue / Objet | `1 PA` | Micro-Action | Soi ou contact (`1` case). |
| `prendre-stimulant` | Prendre un Stimulant | `1 PA` | Micro-Action | *Antidouleurs* / *Vif* (règles overdose 2e dose). |
| `observation-ecoute-active` | Observation / Écoute Active | `1 PA` | Micro-Action | Seuil `4` (auto `1d12`) couvert total, ou VS Discrétion si *Dissimulé*. |
| `allumer-eteindre-lampe` | Allumer / Éteindre une Lampe | `1 PA` | Micro-Action | Cône/rayon lumineux annule malus nuit au prix de la visibilité. |
| `parler-combat` | Parler en Combat | `0 PA` (`2` phrases) / `+1 PA` | Gratuite | `~30 mots` gratuits / cycle ; `1 PA` par phrase supplémentaire. |
| `desenrayer-objet` | Désenrayer / Réparer Terrain | `2 PA` | Manœuvre | Remet en service un objet *Enrayé*. |
| `pousser-soulever` | Pousser, Soulever ou Forcer | Variable | Manœuvre | Épreuve Force/Athlétisme ou VS Force. |

#### A.5 Soins, Survie & Gestion des États (`10`)
| ID | Nom | PA / Coût | Cadence | Règle clé |
| :--- | :--- | :---: | :--- | :--- |
| `premiers-soins` | Premiers Soins d'Urgence | `3 PA` *(2 PA Suture)* | Manœuvre | Contact ; *Premiers Soins* Diff `6` pour *Agonisant* ; lève *Souffrant* & *Saignement*. |
| `secouer-allie-inconscient` | Secouer un Allié Inconscient | `1 PA` | Micro-Action | Réveille un inconscient stabilisé (sauf Agonie / coma pathologique). |
| `extinction-feu` | Extinction du Feu | `X PA` (= score *En Feu*) | Manœuvre | Soi ou allié adjacent (`X × 2` sous *Ralenti*). |
| `epreuve-choc-traumatique` | Épreuve de Choc Traumatique | `0 PA` (mises admises) | Déclenchée | Zone `> CON × 2` → *Endurance Physique* Diff `3 + excès`. |
| `seuil-letal-sombrer-dernier-souffle` | Choix au Seuil Létal | `0 PA` | Déclenchée | À `≥ CON × 5` : *Sombrer* / *Dernier Souffle* / *Agonie* (`Diff 4 + 1/t`). |
| `point-de-miracle` | Dépenser un Point de Miracle | `0 PA` | Déclenchée | Survit à coup mortel ou échec d'Agonie (`1 PM` départ PJ ; `0` sbire). |
| `diagnostic-traitement-pathologie` | Diagnostiquer & Traiter Pathologie | `3 PA` / Scène | Manœuvre | Diff = `Virulence` (`min 4` diagnostic). |
| `resurrection` | Résurrection | Spécial | Hors-Combat | Inflige *Affaibli* pendant `10 − CON` jours réels. |
| `nuit-sure-convalescence` | Repos Sûr & Convalescence | `6 / 8 / 10 h` | Hors-Combat | Délai min `12 h` depuis le précédent ; efface `CON` (`×2` veille soignant). |
| `alimenter-hydrater` | S'Alimenter & S'Hydrater | Hors combat (`1` ration / `2 L`) | Hors-Combat | Lève `1` niveau *Affamé* (`1×/j`) ou *Assoiffé* (`1×/repos`). |

#### A.6 Cinquième Force, Pouvoirs, Dons & Chrono (`11`)
| ID | Nom | PA / Coût | Cadence | Règle clé |
| :--- | :--- | :---: | :--- | :--- |
| `lancer-sort-offensif` | Lancer un Sort Offensif | `XP = PA` | Principale | Portée `MAG + MAG×entr.` ; `1/t` ; VS Esquive ou Parade. |
| `lancer-sort-utilitaire-defensif` | Lancer Sort Utilitaire/Déf./Social | `XP = PA` | Principale | Sans jet (bouclier/soin) ou épreuve/VS. |
| `lancer-sort-reaction` | Lancer un Sort en Réaction | Sort `+ 2 PA` | Déclenchée | Hors tour (`+2 XP` en création + `+2 PA` en jeu). |
| `activer-specialisation-technique` | Activer Spé ou Technique Maison | `0 à 6 PA` max | Principale | Cadres `C0–C4` (`1/round` max pour `C3–C4`). |
| `activer-don-racial` | Activer un Don Racial | `0 PA` | Gratuite | À volonté dès conditions remplies. |
| `tisser-lien-telepathique` | Tisser Lien Télépathique | `4 h + 5 XP` chacun | Hors-Combat | Épreuve *Télépathie* à `Diff MJ + 2`. |
| `communiquer-lien-telepathique` | Communiquer par Lien | `0 PA` | Gratuite | Instantané, silencieux, hors anti-magie. |
| `ancrage-thermodynamique` | Ancrage Thermodynamique | `0 PA` | Déclenchée | Absorbe surchauffe d'Essoufflement d'un membre lié. |
| `lecture-impact-predictive` | Lecture d'Impact Prédictive | `2 PA` | Réaction Déf. | *Clairsentience Éch. 3+* : `+1` palier de dé Esquive/Parade. |
| `verrouillage-probabiliste` | Verrouillage Probabiliste | `3 PA` | Principale | *Clairsentience Éch. 6+* : `-4` Diff sur attaques alliées (`1` case). |
| `vision-dirigee` | Vision Dirigée | `1 min` immobilité | Hors-Combat | *Clairsentience Éch. 3+* : sonde passé/futur proche (`1 h`). |

#### A.7 Collectif, Véhicules, Compétences & Hors-Combat (`16`)
| ID | Nom | PA / Coût | Cadence | Règle clé |
| :--- | :--- | :---: | :--- | :--- |
| `attaque-combinee-groupe` | Attaque Combinée de Groupe | Principale `+ 2 PA` chacun | Principale | `2–3` attaquants max ; somme jets vs 1 défense ; armure soustraite `1×`. |
| `technique-duo` | Technique-Duo (Dyadique) | `4–11 PA` cumulés | Manœuvre | `9` duos ; test carac nue vs `ND` ; `1/cycle/perso`, ne consomme pas d'action principale. |
| `conduire-vehicule` | Conduire / Manœuvrer Véhicule | Action Principale | Principale | Épreuve *Conduite & Pilotage* si manœuvre délicate. |
| `embarquer-debarquer` | Embarquer / Débarquer | `2 PA` | Manœuvre | Sans jet à l'arrêt (`4 PA` sous *Ralenti*). |
| `jet-initiative` | Jet d'Initiative | `0 PA` (`1×` avant R1) | Déclenchée | Meilleur de `RAP, AGI, INT` (`+ Sens du Danger INS`) ; initialise PA & Réact. Libre. |
| `action-sociale` | Action Sociale | Variable (`1 PA` Feinte) | Manœuvre | *Communication* VS *Intuition/Instinct*. |
| `intimider-menacer` | Intimider / Menacer | Variable | Principale | *Intimidation* VS *Sang-Froid/Volonté/Intimidation*. |
| `mener-commander` | Mener / Commander | `2 PA` *(spé Mener)* | Manœuvre | Alliés à `< 3` cases (PA bonus, encaissement, ralliement). |
| `analyser-faille` | Analyser une Faille | `2 PA` | Manœuvre | Cible visible : perce Encaissement & guide alliés. |
| `ingenierie-surfrequence` | Surfréquence, Ingénierie & Piratage | `2 PA` *(Surfréq.)* / Var. | Manœuvre | Surfréquence armes/boucliers (`1t`, `3t` *Résonance Infinie*), piratage, réparations. |
| `subterfuge-manuel` | Subterfuge Manuel | Variable | Manœuvre | Crochetage, sabotage, vol à la tire (`VS Observation/Perception`). |
| `nature-pistage-piegeage` | Nature, Survie, Pistage & Pièges | Hors combat / Scène | Hors-Combat | Traque, dangers miniers, abris, pièges immobilisants. |
| `artisanat-forger-affuter` | Artisanat (Forger / Réparer) | Hors combat (atelier) | Hors-Combat | Réparer objet *Détruit*, affûter, fabriquer. |
| `savoir-intuition` | Savoir (*Académie*) & *Intuition* | Variable | Manœuvre | Connaissances ou lecture d'intentions. |
| `defi-hors-combat` | Défi Hors-Combat par Étapes | Réserve PA globale du défi | Hors-Combat | `3–7` étapes, `0–3` rattrapages ; échec total = `0` case. |
| `entrainer-compagnons` | S'Entraîner entre Compagnons | `4 h` | Hors-Combat | `Max 1 case/j/binôme` ; `max 1 XP lié/semaine` ; formateur `niveau ≥ élève`. |

---

### Appendice B (`data/appendice_glossaire.json`, v2.4) — Glossaire des Mécaniques (`300` termes)

- **Structure d'indexation à `6f497f7`** : Chaque entrée possède `id`, `terme`, `domaine`, `alias` et `liens` (renvois vers les chapitres sources des Livres I–XII), sans champ autonome de texte de définition.
- **Couverture par domaine (`300` termes au total)** :

| Code Domaine | Intitulé | Nb Termes | Contenu indexé |
| :--- | :--- | :---: | :--- |
| **B.1 (`fondations`)** | Fondations, Création & XP | `34` | Cycle `10 s`, PA, Règle d'Or, Échelles, 7 Espèces, Dons, Budgets, 7 Attributs, Bagage, Progression & Anti-farm. |
| **B.2 (`epreuves`)** | Dés, Épreuves & Environnements | `32` | Échelle & crans de dés, Seuils `0–34`, Face-à-Face, Différentiel, Jetons masqués, Critiques, Météo P1–P4, Visibilité, Défis. |
| **B.3 (`competences`)** | Compétences & Fabrique | `26` | Rangs de base, Arbres FOR/AGI/CON/INT/CHA/INS, Spécialisations, Fabrique, 9 règles, Châssis `C0–C4`, Signature, Pièces & Modificateurs. |
| **B.4 (`magie`)** | 5e Force & Magie | `22` | Organe interne, Saturation/Surchauffe, Anti-magie, Nytharite & Moteur, 3 Voies, Portée, Lien Télépathique, Atelier `XP=PA`. |
| **B.5 (`combat`)** | Combat Tactique & Véhicules | `74` | Tour/Embuscade, Postures, Défenses/Réaction Libre, Guet, ESS/PE, Effets sur `Δ`, Opportunités, Lutte, Charge, Poursuite, Furtivité, 9 Duos, Localisation/Visée, Canon Entravé, Véhicules. |
| **B.6 (`sante`)** | Santé, Dégâts & Pathologies | `31` | Encaissement, Choc Traumatique, Typologie dégâts, Seuil Létal & branches, Point de Miracle, Résistances, Premiers Soins, Repos Sûr, Résurrection, Pathologies IG `I–V`. |
| **B.7 (`etats`)** | États, Altérations & Statuts | `38` | Les `19` états préjudiciables (`Canon Entravé` … `Agonisant`), les `7` bénéfiques, les `3` statuts vitaux, les `5` états objets + sous-entrées résiduelles (`Saignement, Empoisonné, En Feu, Asphyxie`). |
| **B.8 (`equipement`)** | Historiques & Arsenal | `19` | Historiques `[E0–E5]`, Enfance/Métier/Atout/Attache/Événement/Ancrage, `DLPH`, Armes & Laser, Blindages/Encombrement, Champs de force, Pharmacopée. |
| **B.9 (`causalite`)** | Temps, Causalité, Outils & MJ | `24` | Propagation Avant, Effet Papillon Inversé, Goulots, Distorsions, Clairsentience `8` échelons, DAG, `4` Lois Temporelles, Rangs Bestiaire, `6` Outils Livre IX. |

---

### Récapitulatif des Écarts Résiduels Internes à la Baseline `6f497f7`

Tableau consolidé des légères incohérences internes encore présentes dans les sources du Codex au commit `6f497f7` (base utile pour tracer les correctifs dans les commits suivants) :

| Source (`6f497f7`) | Écart observé dans les fichiers | Référence canonique majoritaire |
| :--- | :--- | :--- |
| **Livre I (`especes_par_nom` & `tools.js` `giftMechanics`)** | Don A du *Vardien* donne `+1 Essoufflement max` en JS et JSON secondaire ; malus Cléien `-1 Encaissement` du Don A absent de `giftMechanics`. | `especes[5].dons[0]` : `+1 aux épreuves de moral et de peur` ; `Cléien Don A` : `-1 Encaissement`. |
| **Livre I (`progression.xp_total` JSON)** | Indique « surcharges `×2` incluses ». | HTML `§5.4` & Créateur Livre IX : prix normal même en croisé (`×2` non compté). |
| **Livre I (`couts_xp`) & Livre III (`calcul_rang_base` / `fabrique`)** | Liens/références anciens restés vers `Livre III §13` (spécialisations), `Livre III §10.1` (rang de base) et `Livre IV §20` (atelier). | Chapitres réels : `§14` (spé.), `§11.1` (rang de base) et `§19.1` (atelier). |
| **Livre III (`Premiers Soins`, `Artisanat`)** | *Premiers Soins* rangée sous l'arbre `INT` mais liée à `INS` ; *Artisanat* note `SAG, INT`. | `SAG` = ancien sigle d'`INS` (*Instinct / Sagesse*). |
| **Livre IV (`livre_4.json` & `livre_9.html`)** | `nb_specialisations: 46` vs `329` entrées réelles ; clé orpheline `stades_neuronaux` dans `tableaux` ; module à `1 pt` nommé *Cible Débalancée* et module *Cible Ralentie* à `4 pts` disant « double PA » sans `+1 PA/case`. | États canoniques Livre VI `§24.1` : *Déstabilisé* (`3 pts`) et *Ralenti* (`×2` hors déplacement + `+1 PA/case`). |
| **Livre VIII (`chap-30`, `grenades_tactiques`)** | *Mains Nues* noté `1D - 0L - 1P - 1H` au §30 ; `grenades_tactiques` écrit `Aveuglé`. | Livre V `§22.1` : *Mains Nues* `0 + Δ` (`+1 PE` sur touche franche) ; Livre VI `§24.1` : état *Aveugle*. |
| **Livre IX (`livre_9.html`, `tools.js`)** | `initDiceRoller` affiche « *Impact dévié* » sur `isOpposed && diff === 0` ; chap. 35 texte parle d'ajouter des PA *après* le jet ; chap. 37 renvoie à `Livre X §34–36` ; pied de page Livre IX indique `Livre VIII` / `11 LIVRES HOMOLOGUÉS`. | Livre V `§22.1` (zéro déviation) ; Livre V `§20.2` (mises masquées avant le lancer) ; Livre X `§39–42` ; Codex à 12 Livres. |
| **Livre XI (`chap-45`)** | Mentionne « *Échelle Technologique (1 à 6)* » (Hybris `3–4`, Vardis `4–5`, Cléia `5`). | Livre I `§2.1` & Livres VII–VIII : `6` paliers notés `[E0–E5]`. |
| **Livre XII (`creatures`)** | En-tête UI annonce `Toutes les Menaces (15+)` pour `14` fiches (`nb_creatures: 14`) ; profils historiques non réaudités gardent d'anciens dés (`1d2+1`, `1d4+1`, `1d6+1`), anciens libellés (`Enflammé`, `Hémorragie`, `Aveuglé`) et d'anciens coûts/stats (*Carabine* `3D, portée 25`, *Épée longue* `2D`, *Coup au Cœur* `3 PA`, *Tir Préparé* `4 PA`, *Charge/Saut* `6`/`10` cases). | Échelle `1d2`–`2d12+10` (Livre II `§6.1`), états Livre VI (`En Feu`, `Saignement`, `Aveugle`), arsenal Livre VIII & coûts Livre V. |
