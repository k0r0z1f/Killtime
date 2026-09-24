# 🎮 Killtime Tactics — Moteur Tactique Isométrique 3D, Cinématique & VTT Multijoueur

> **Moteur de RPG Tactique Isométrique dans la tradition de *Fallout 1 & 2* et *Wasteland***  
> *Propulsé par la causalité intégrale du Codex Universel Killtime (Livres I à XI) — Compatible Unity 6 LTS (6000.0+) & WebGL Natif.*

---

## 🌟 Architecture & Fonctionnalités Majeures

### 1. Grille Hexagonale 3D & Couvert Balistique (Livre VI §25.3)
- **Topologie Axiale & Cubique** : Coordonnées $(q, r, s)$ avec $q + r + s = 0$.
- **Pathfinding A\* indexé sur les PA** : Coûts de mouvement déduits de la réserve de combat de 10 secondes.
- **Cône de Visée Cyrus-Beck 3D Exact** : Évaluation à 28 rayons échantillonnés sur la silhouette adverse depuis le coin le plus avantageux de l'attaquant :
  - **À découvert** ($\ge 90\%$ visible) : modificateur $0$.
  - **Moitié visible** ($45\% \le \text{visibilité} < 90\%$) : $-1$ au jet d'attaque.
  - **Aux trois-quarts couvert** ($0\% < \text{visibilité} < 45\%$) : $-2$ au jet d'attaque.
  - **Totalement couvert / Non visible** ($0\%$) : attaque directe impossible.
  - **Au contact direct** ($\le 1$ case) : couvert ignoré par règle d'engagement rapproché.
- **Obstacles 3D Volumétriques (`PropObstacleRegistry`)** : Prise en compte réelle des boîtes englobantes des accessoires (piliers, caisses, barricades) et du relief pour l'occlusion des tirs.

### 2. Brouillard de Guerre Asymétrique (Livre VI §25.4)
- **Vision à 360° Omnidirectionnelle** : Aucun cône frontal restreint, le regard couvre tout le périmètre tactique.
- **Portée Environnementale Découplée** : Preset court ($6$ cases en intérieur/bunker) et long ($12$ cases en extérieur dégagé) indépendant des attributs.
- **Occlusion par Murs Pleins & Relief** : Masque visuel complet (`HexGridVisualizer`) des cases masquées derrière un mur *Full*.
- **Révélation Sensorielle & Tactique** :
  - Automatique lors de l'entrée dans le champ d'au moins un membre de l'escouade.
  - Épreuve d'**Observation active** vs **Discrétion** (action à $1$ PA).
  - Détonations et **tirs bruyants** révélant le tireur dans son rayon d'Ouïe (D6) jusqu'à la fin du round suivant.
  - Détection thermique traversant les parois jusqu'à mi-portée.
- **Sécurité Anti-Maphack VTT** : Filtrage côté serveur (`unit_claim`) des paquets de mouvement pour les clients non-GM.

### 3. Résolution des Combats & Duel Aveugle (Livres I, II, VI, VII)
- **Économie de Points d'Action** :
  $$\text{PA} = \max(\text{AGI}, \text{INT}) + \text{RAP} + \min(\text{FOR}, \text{AGI}, \text{CON}, \text{RAP}, \text{INT}, \text{ÉRU}, \text{CHA}, \text{INS})$$
- **Vitalité, Choc & Létalité** :
  - Seuil d'Encaissement : $\text{CON} \times 2$.
  - Maximum Létal : $\text{CON} \times 5$.
  - Essoufflement Maximum (PE) : plafonné strictement à la $\text{CON}$.
  - Souffle d'urgence : $+2$ PA immédiats contre $+1$ PE.
- **Séquence Officielle du Duel Aveugle (Livres II §7 + VI §24.1)** :
  1. *Déclaration Attaquant* : Cible, zone anatomique visée, mise masquée de PA bonus et de PE (débit immédiat).
  2. *Déclaration Défenseur* : Compétence défensive ou encaissement passif, mise masquée de PA et PE sans connaître le jet adverse.
  3. *Révélation Simultanée* : Lancer simultané des dés polyédriques, calcul du différentiel net ($\Delta$).
- **Ciblage Anatomique Chirurgical (Livre VI, Chap. 26)** :
  - Tête / Crâne, Yeux / Visage, Cou / Trachée, Cœur / Poumons, Torse, Bras Droit, Bras Gauche, Jambes.
  - Déviation automatique sur zone adjacente en cas d'égalité stricte ($\Delta = 0$).
- **16 Altérations d'État (Livre VII)** : Déstabilisé, Étourdi, Immobilisé, Paralysé, Sonné, À terre, Ralenti, Agonisant, Inconscient, Aveugle, Sourd, Asphyxie, Empoisonné, En Feu, Saignement, Chrono-Fracture.
- **Physique Réelle des Armes Lâchées (`DroppedWeaponPickup`)** : Dès qu'une unité tombe inconsciente ou meurt, ses armes équipées sont éjectées de l'inventaire et chutent au sol sous simulation PhysX rigide (ramassables ou projetables au pied).

### 4. Duo-Tech Tri-Fusion (Lucas-0 & Mina-0)
- **Actions Jumelées Uniques** : Ne consomment pas l'action d'attaque standard du tour.
- **Paiement Atomique Immédiat** : Débit synchrone des PA du binôme sans endettement.
- **Tissage Vectoriel Dynamique** :
  - *Trait 1 (Mina)* : Tracé au sol à la souris, départ d'une onde cinétique en boucle à vitesse réelle.
  - *Trait 2 (Lucas)* : Tracé en anticipation de l'onde pour caler la fenêtre de synchro.
- **Matrice des Techniques** :
  - *Sillage Igné* : Dash de ligne de Mina ($4$ bruts par cible) + frappe thermique de Lucas (évaluation *Perfect*, *Risky* avec friendly fire, ou *Late*).
  - *Lacet de Nytharite* : Boucle d'entrave immobilisant une cible sans dégât direct.
  - *Fournaise à Retardement* : Délimitation triangulaire autour d'un ennemi isolé pour implosion ciblée ($5$ bruts, Étourdi, $-2$ PA).
- **Limite Causale** : $1$ technique Duo-Tech par combattant par round.

### 5. Armurerie Complète, Balistique & Grenades (Livre VIII)
- **Notation DLPH Universelle** : Dégâts, Livres (poids), Portée (cases), Mains ($1$H / $2$H).
- **Catalogue Intégral** : Épées métalliques, marteaux, haches, piques, arcs, épées laser, fusils laser (prefabs réels), armures légères/lourdes, générateurs de champ de force, trousses médicales et rations.
- **Système Balistique de Grenades (Livre VIII §31.3)** :
  - Lancer main ($8$ cases, $2$ PA) vs Lance-grenades dédié ($20$ cases, $3$ PA).
  - Jet de précision Ballistique (SD $10$) avec dispersion hexagonale d'écart en cas d'échec.
  - Atténuation radiale du souffle ($-25\%$ par case, plancher à $25\%$) + éclats de shrapnels secondaires.

### 6. Atelier Arcanotech & Cinquième Force (Livre IV)
- **Règle d'Or Inviolable** : $\text{Coût de Création en XP} = \text{Coût d'Activation en PA}$ ($1\text{ XP} = 1\text{ PA}$).
- **Pénalité Hybride Modulaire** : $+1$ XP par catégorie additionnelle (Offensif, Défensif, Utilitaire) sans altérer le coût PA en combat.
- **Portée Balistique Magique** :
  $$\text{Portée de Base} = \text{MAG} + \text{Niveau d'Entraînement}$$

### 7. Campagne Narrative, Éditeur Nodal & Overworld (Livres X & XI)
- **Éditeur de Graphe Nodal (Touche `F8`)** : Conception visuelle de scènes scénarisées en JSON pur, gestion des répliques, choix sous contrainte, défis de compétences opposés et conséquences conditionnelles.
- **Lecteur de Scènes de Campagne (Touche `F7`)** : Exécution fluide avec transitions cinématiques plein écran, letterboxing et cartes de titre.
- **Réseau de Secteurs d'Hybris (Touche `F12`)** : Carte planétaire hexagonale normée, calcul d'itinéraires Dijkstra en miles et jours de voyage, brouillard d'overworld et verrous d'exploration.
- **Le Fleuve du Temps 3D (Touche `F11`)** : Visualisation tridimensionnelle des lignes causales et snapshots d'état tactique rembobinables.

### 8. Table Virtuelle Multijoueur (VTT), VoIP & Visio IA
- **Protocole WebSocket Haute Résilience (`/ws/vtt`)** : Réplication synchrone des mouvements, jets de dés, annuaire de salles de jeu publiques et autorité stricte du Maître du Jeu (GM).
- **VoIP Basse Latence Intégrée** : Codecs compressés IMA-ADPCM ($16\text{ kHz}$ / $8\text{ kHz}$), G.711 $\mu$-law et Raw PCM avec Noise Gate, limiteur anti-crête, égaliseur et modulateur vocal.
- **Visio Webcam & Flou d'Arrière-Plan IA (Touche `F6`)** : Intégration de modèles de segmentation ONNX via Unity Sentis pour détacher les participants et flouter le décor en temps réel.
- **Fenêtres Pop-Out Détachables** : Extraction des flux vidéo individuels en fenêtres flottantes avec liseré réactif de prise de parole.

### 9. Rendu Audio Procédural & Caméléon Système (Touche `F9`)
- **Synthèse 100% Procédurale** : Couverture sonore intégrale sans assets audio externes (compatible WebGL léger).
- **Musique Adaptative à 3 Paliers** : Transition dynamique Calme $\leftrightarrow$ Tendu $\leftrightarrow$ Intense pilotée par la télémétrie des PV et des menaces.
- **Caméléon Système (Linux PipeWire)** : Analyse fréquentielle NSDF et émulation de synthétiseurs calés en direct sur le flux audio du système d'exploitation.

---

## ⌨️ Raccourcis Clavier & Suite Développeur

| Raccourci | Interface / Fonctionnalité | Description |
| :--- | :--- | :--- |
| **`Tab`** / **`~`** | **Dev Arena — Combat & VATS** | Console d'engagement, pilotage IA, télémétrie et logs |
| **`F1`** | **Créateur de Personnages** | Création de fiches héroïques, attribution d'attributs et presets |
| **`F2`** | **Éditeur de Cartes 3D** | Peinture de sols, hauteurs, plafonds, pose d'obstacles et props 3D |
| **`F3`** | **Table des Règles du Codex** | Ajustement en direct des multiplicateurs, seuils et constantes mathématiques |
| **`F4`** | **Room Multijoueur VTT** | Gestion de session, code de table, présence et chat réseau |
| **`F5`** / **`I`** | **Inventaire & Armurerie** | Gestion du sac, dotation d'équipements et marché d'achat/revente CE |
| **`F6`** | **Salon Vidéo Visio** | Galerie de webcams avec flou Sentis et pop-outs individuels |
| **`F7`** | **Directeur de Scène** | Lecteur interactif de dialogues, embranchements et objectifs narratifs |
| **`F8`** | **Éditeur Nodal de Scènes** | Graphe d'écriture des nœuds de dialogue et déclencheurs de combat |
| **`F9`** | **Mixage Audio & Caméléon** | Niveaux sonores, déclencheurs d'ambiance et capture PipeWire |
| **`F10`** | **Weapon Grip Lab** | Calibrage précis des sockets d'armes en main sur les mannequins 3D |
| **`F11`** | **Fleuve du Temps 3D** | Navigation chronologique spatiale et rembobinage de causalité |
| **`F12`** | **Carte du Monde d'Hybris** | Navigateur et éditeur du réseau de secteurs planétaires |
| **`F`** | **Mode Free Look** | Décrochage de la caméra isométrique vers l'inspection 3D libre |
| **`Q` / `E`** | **Rotation Caméra** | Rotation orbitale tactique à 360° par pas fluide |
| **`Espace`** | **Fin de Tour** | Validation et passage du tour de combat pour l'unité active |
| **`M`** | **Coupure Audio** | Bascule générale du son muet / actif |

---

## 🗂️ Structure du Projet

```
KilltimeTactics/
├── ProjectSettings/ # Configuration moteur Unity 6 LTS
├── Packages/manifest.json # URP, Sentis/InferenceEngine, InputSystem, TextMeshPro
└── Assets/
├── Plugins/
│ └── WebGL/ # Passerelles JavaScript natives (.jslib)
├── Resources/
│ ├── Animations/ # Clips d'animation humanoïdes (Male / Female)
│ ├── Characters/ # Prefabs des avatars 3D
│ ├── Guns/ # Modèles d'armes à feu et fusils laser
│ ├── Grounds/ # Textures de terrain et sols tactiques
│ ├── Maps/ # Cartes par défaut (Killzone_Alpha3)
│ ├── Objects/ # Éléments de décor et props 3D
│ └── Scenarios/ # Scènes narratives JSON
└── Scripts/
├── Core/ # Moteur de règles déterministe (C# Pur agnostique)
│ ├── Arcanotech/ # Moteur de sorts modulaires, disciplines, modules (Livre IV)
│ ├── Character/ # Attributs, fiches PJ/PNJ, progression organique (Livre I)
│ │ ├── Classes/ # 8 Archétypes de classe de base et guides de build
│ │ ├── LucasCharacter.cs # Voie du Vide Calculant & Architecture Neurale
│ │ ├── MinaCharacter.cs # Voie de l'Atavisme Cinétique & Éveil Primordial
│ │ └── ThomasCharacter.cs# Voie de l'Ancre Inébranlable & Commandement
│ ├── Chrono/ # Snapshots causals, calendrier d'Hybris, Fleuve du Temps (Livre V)
│ ├── Combat/ # Résolution duel aveugle, ciblage anatomique, Duo-Tech, grenades
│ ├── Dice/ # Lanceur probabiliste, échelle de dés (d2 à 2d12+10)
│ ├── Inventory/ # Catalogue d'armurerie Livre VIII, presets et profils de grip
│ ├── Rules/ # Configuration persistante des constantes du Codex
│ └── World/ # Registre géographique officiel des secteurs d'Hybris
├── Tactics/ # Couche tactique Unity 3D
│ ├── AI/ # Contrôleur d'IA militaire, doctrines et analyse de jeu
│ ├── Chrono/ # Rendu 3D isolé du Fleuve du Temps
│ ├── CombatUI/ # Anneau contextuel holographique et sélections
│ ├── DuoTech/ # Contrôleur de tracé vectoriel souris et effets de flamme
│ ├── Grid/ # Grille axiale, évaluation de couvert Cyrus-Beck, obstacles 3D
│ ├── Lighting/ # Éclairage dynamique et entités Pixie autonomes
│ ├── TurnSystem/ # Gestionnaire de tours de 10s, initiative et victoires
│ ├── Units/ # Avatars physiques, animateur hybride et armes au sol
│ └── Visibility/ # Brouillard de guerre asymétrique à 360° et détection
├── Story/ # Scénarisation et overworld
│ ├── Data/ # Structures de données sérialisables et dépôts JSON
│ ├── Scenes/ # Contrôleur d'exécution de scènes narratives
│ └── WorldMap/ # Éditeur et visualiseur de la carte planétaire
├── Camera/ # Contrôleur isométrique au sol (Y=0) et directeur cinématique
├── Audio/ # Synthèse procédurale, musique adaptative et Caméléon PipeWire
├── Multi/ # Pile réseau Virtual Tabletop (VTT)
│ ├── Protocol/ # Sérialisation des opérations VTT et contrats JSON
│ ├── Video/ # Streaming vidéo, fenêtres pop-out et masquage IA Sentis
│ └── Voice/ # Moteur VoIP, codecs ADPCM/G.711, DSP et rééchantillonneur
├── UI/ # Fenêtres flottantes dev (F1 à F12), HUD et affichage VATS
└── Tests/ # Bancs de tests unitaires et d'intégration NUnit
```

---

## 🚀 Installation & Lancement

1. **Installer l'Environnement** :
   - Installez **Unity Hub**.
   - Téléchargez et installez **Unity 6 LTS (6000.0+)** avec les modules :
     - *Linux Build Support* (ou *Windows Build Support* selon votre OS hôte).
     - *WebGL Build Support*.

2. **Ouvrir le Projet** :
   - Lancez Unity Hub.
   - Cliquez sur **Add** $\rightarrow$ **Add project from disk**.
   - Sélectionnez le dossier racine du projet `KilltimeTactics`.

3. **Lancer la Simulation** :
   - Ouvrez la scène principale de combat dans le dossier `Assets/Scenes/`.
   - Cliquez sur le bouton **Play ▶️** dans l'éditeur.
   - Utilisez **`Tab`** pour ouvrir la console tactique de combat, **`F1`** pour inspecter les héros ou **`F8`** pour modifier la narration.
