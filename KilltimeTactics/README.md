# 🎮 Killtime Tactics — Moteur de Jeu Isométrique 3D & Cinématique (Unity)

> **Moteur de RPG Tactique Isométrique dans la tradition de *Fallout 1 & 2* et *Wasteland***  
> *Propulsé par les règles intégrales du Système RP Killtime (Codex Universel) & Prêt pour l'Export WebGL.*

---

## 🌟 Caractéristiques Clés du Moteur

1. **Grille Hexagonale Tactique 3D (Style Fallout)** :
   - Calcul des déplacements basé sur les coordonnées axiales et cubiques $(q, r, s)$.
   - Algorithme de pathfinding A* indexé directement sur les **Points d'Action (PA)** du tour de 10 secondes.
   - Gestion des types de couverture (None, Half, Full).

2. **Caméra Hybride (Isométrique ↔ Free Look ↔ Cinématique)** :
   - **Vue Isométrique Tactique** : Pitch incliné à 50°, rotation orbitale 360° (touches `Q` / `E`), pan (`Z/Q/S/D` ou Flèches) et zoom progressif molette.
   - **Mode Free Look (Touche `F`)** : Décrochage total de la caméra pour inspecter librement l'arène 3D à la première/troisième personne.
   - **Plans Cinématiques d'Action (Killcam / V.A.T.S.)** : Transition fluide vers une contre-plongée dramatique lors d'un tir ciblé, avec effet bullet-time au moment de l'impact puis retour en vue tactique.

3. **Intégration Fidèle du Système RP (Codex Universel)** :
   - **Économie de PA (Livre I & VI)** : `PA = Max(Agi, Int) + Rap + Min(Principaux)`.
   - **Vitalité & Encaissement** : Encaissement à `CON × 2`, Seuil Létal à `CON × 5`, et gestion de l'**Essoufflement d'urgence**.
   - **Ciblage Anatomique Chirurgical (Livre VI, Chap. 26)** : Tête, Yeux, Cou/Trachée, Cœur/Poumons, Torse, Bras (Arme/Garde) et Jambes. Gestion des malus de visée, annulation par dépense de PA, et déviation en cas d'égalité.
   - **16 États & Altérations (Livre VII)** : Déstabilisé, Étourdi, Paralysé, Sonné, À terre, Ralenti, Agonisant, Inconscient, Asphyxie, Saignement, etc.
   - **Cinquième Force & Nytharite (Livre IV)** : Formule stricte d'équivalence `Coût en XP = Coût en PA`.
   - **Chrono-Causalité & Fleuve du Temps (Livre V)** : Snapshots tactiques à chaque seconde et capacité de rembobinage sans paradoxe.

4. **Passerelle WebGL Intégrée (`.jslib`)** :
   - Prêt pour tourner directement dans le navigateur au sein du portail web Killtime.
   - Communication bidirectionnelle C# ↔ JavaScript pour échanger les fiches de personnage du Livre IX et les logs de combat.

---

## 🗂️ Structure du Projet

```
KilltimeTactics/
├── .gitignore                       # Règles d'exclusion officielles Unity
├── ProjectSettings/
│   └── ProjectVersion.txt           # Version Unity recommandée (Unity 6 LTS)
├── Packages/
│   └── manifest.json                # URP, Cinemachine 3, InputSystem, TextMeshPro
└── Assets/
    ├── Plugins/
    │   └── WebGL/
    │       └── KilltimeWebBridge.jslib # Passerelle JavaScript native
    └── Scripts/
        ├── Core/                    # Moteur de règles RP C# pur (Agnostique d'Unity)
        │   ├── Dice/                # DiceRoller.cs, DiceType.cs (Échelle d2 à 2d12+10)
        │   ├── Character/           # Attributes.cs, CharacterStats.cs, StatusEffect.cs
        │   ├── Combat/              # BodyPart.cs, CombatCalculator.cs, DamageResult.cs
        │   ├── Arcanotech/          # NythariteSpell.cs, PsychicTier.cs
        │   └── Chrono/              # TimeSnapshot.cs, TimelineBranch.cs
        ├── Tactics/                 # Couche de jeu tactique Unity
        │   ├── Grid/                # HexCoordinates.cs, HexNode.cs, TacticalHexGrid.cs, HexPathfinder.cs
        │   ├── Units/               # TacticalUnit.cs
        │   ├── TurnSystem/          # TurnManager.cs (Tour de 10s & initiatives)
        │   └── CombatEncounterController.cs # Chef d'orchestre de l'arène
        ├── Camera/                  # TacticalCameraController.cs, CinematicDirector.cs
        ├── UI/                      # CombatHUD.cs (Interface de ciblage et PA)
        ├── WebGL/                   # WebBridgeManager.cs
        └── Tests/                   # CoreRulesTests.cs (Validation NUnit)
```

---

## 🚀 Comment Ouvrir & Lancer le Projet

1. **Installer Unity** :
   - Téléchargez **Unity Hub** (version Linux disponible via AppImage ou dépôt officiel).
   - Installez la version **Unity 6 (6000.0 LTS)** avec le module **WebGL Build Support** et **Linux Build Support**.
2. **Ajouter le Projet** :
   - Ouvrez Unity Hub.
   - Cliquez sur **Add** -> **Add project from disk**.
   - Sélectionnez le dossier `/home/wa/Killtime/KilltimeTactics`.
3. **Lancer la Scène de Combat** :
   - Dans la hiérarchie de la scène, ajoutez un objet vide contenant le script `CombatEncounterController`.
   - Cliquez sur **Play ▶️** pour vous déplacer sur la grille hexagonale, cibler les membres d'un adversaire et admirer les transitions cinématiques !
