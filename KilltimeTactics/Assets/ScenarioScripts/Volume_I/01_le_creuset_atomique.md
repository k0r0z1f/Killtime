# Scène 01 — Le creuset atomique

**Campagne :** Volume I — *The Awakening Storm*  
**Référence canonique :** Chapitre 1, « The Atomic Crucible »  
**Format :** prologue jouable / tutoriel narratif  
**Lieu :** base de la Résistance, astéroïde en orbite de Nefris  
**Personnages jouables :** John, Erika; Syn est une alliée de soutien  
**Durée cible :** 10–15 minutes

## Fonction

Le joueur découvre que la Résistance ne demande pas un transport mais le sauvetage d'un enfant anormal. Le choix final fixe la première route de campagne. La voie du manuscrit est l'acceptation du contrat, mais les deux autres réponses sont des commencements valides.

## État initial

Dans le cœur humide et métallique d'un astéroïde, Nefris brûle au loin. Des hologrammes projettent des routes vers Vardis. John attend près du mur, Erika observe le conseil, et un coordinateur à bras cybernétique protège un datapad contenant le contrat.

```
route_initiale = non_definie
confiance_john_erika = 0
statut_lucas = detenue_par_resistance
brouilleur = instable
```

## Déroulé

### 1. Cinématique — La proposition

**COORDINATEUR**  
Traverser les lignes impériales est déjà suicidaire. Y faire passer notre invité… c'est autre chose.

**JOHN**  
Vous ne payez pas un passage. Vous payez quelqu'un qui revient vivant.

**COORDINATEUR**  
Et votre prix est un vaisseau militaire.

**ERIKA**  
Ce n'est pas un transport. Si nous comprenons ce qu'il est, nous pourrons changer la forme de cette guerre.

Le coordinateur active le champ de signature et fait glisser le datapad jusqu'à John.

**COORDINATEUR**  
Ramenez notre messie.

### 2. Choix majeur — Le contrat

Le contrôle passe au joueur. Il peut inspecter le datapad, parler à Erika et écouter la porte blindée derrière laquelle se trouve l'invité. Puis il sélectionne une réponse.

#### A. Signer le contrat — voie du manuscrit

**JOHN**  
Je le ramène vivant. Et mon vaisseau m'attend au retour.

- `route_initiale = vardis`
- `statut_lucas = protege_par_john_erika`
- La Résistance fournit le *Starlight Voyager* et attend des comptes.
- **Scène suivante :** traversée furtive puis descente vers Vardis.

#### B. Renégocier — voie indépendante

**JOHN**  
Je transporte un enfant, pas votre arme. Le contrat dit qu'il choisit où il va une fois hors de portée impériale.

Erika soutient John; le coordinateur accepte sous la menace d'annuler l'opération.

- `route_initiale = independante`
- `confiance_john_erika += 1`
- `statut_lucas = protege_sans_faction`
- Le groupe reçoit moins de matériel, mais aucune balise de la Résistance.
- **Scène suivante :** fuite vers une route civile; Vardis devient une destination possible, non obligatoire.

#### C. Refuser — voie impériale

**JOHN**  
Gardez votre guerre. Je ne livre pas un enfant au prochain laboratoire qui le réclamera.

Le coordinateur déclenche le verrouillage de sécurité. Erika doit décider si elle suit John ou reste pour libérer l'invité depuis l'intérieur.

- `route_initiale = imperiale`
- `statut_lucas = cible_a_extraire`
- John commence avec peu de ressources; la Résistance devient une faction instable.
- **Scène suivante :** évasion de la base et extraction de Lucas sous pression.

> Le choix C ne ferme pas Erika définitivement: son départ ou son maintien dans la Résistance dépend de la façon dont le joueur lui a parlé avant le refus.

### 3. Phase jouable — Préparer ou fuir le hangar

**Carte :** salle de commandement → couloir sécurisé → hangar. Trois points interactifs: réserves, kit d'urgence, brouilleur.

| Route | Objectif principal | Pression |
|---|---|---|
| Vardis | Préparer le *Starlight Voyager*. | Le quai sera verrouillé dans 6 minutes. |
| Indépendante | Préparer le vaisseau sans installer la balise de la Résistance. | Les réserves sont réduites et Syn signale une inspection. |
| Impériale | Atteindre le couloir sécurisé et sortir Lucas de la base. | Drones de sécurité et portes qui se ferment. |

**Tutoriels intégrés**

- Déplacement et caméra: traverser le hangar et examiner Nefris.
- Interaction: préparer le matériel ou contourner un verrou.
- Ordre allié: demander à Erika de vérifier les sangles médicales ou de maintenir une porte.
- Inspection: le brouilleur signale une fluctuation, prémisse de la tempête sur Vardis.

### 4. Dialogue de pression

Quel que soit le chemin, Syn transmet la même lecture.

**SYN**  
Activité ionosphérique anormale à l'approche de Vardis. Modèle compatible avec une pré-tempête. Fiabilité: soixante-deux pour cent.

**ERIKA**  
Soixante-deux, c'est assez pour ne pas l'ignorer.

**JOHN**  
Alors on évite le ciel. Ou on apprend ce qu'il veut nous dire.

La dernière phrase varie avec la route choisie: partir vite, partir libre, ou refuser de laisser le ciel décider.

### 5. Révélation de fin

La porte sécurisée s'ouvre — par autorisation, compromis ou force. Une lumière blanche révèle un enfant attaché à un brancard. Quand il relève les yeux, les lampes du hangar vacillent et les instruments de Syn déraillent une seconde.

**SYN**  
Anomalie de champ. Origine: passager.

**ERIKA** *(bas)*  
Lucas.

**JOHN**  
Alors c'est lui qui choisira la suite.

**FIN :** le vaisseau quitte l'astéroïde — ou, dans la route impériale, le hangar s'ouvre sur l'alarme et la fuite. L'image de Vardis sous les éclairs clôt la scène.

## Résultats persistants

| Route | Gain immédiat | Dette / risque |
|---|---|---|
| Vardis | Vaisseau, Syn, matériel de Résistance. | Contrôle politique de la Résistance. |
| Indépendante | Liberté de route, confiance d'Erika. | Ressources limitées, peu d'alliés. |
| Impériale | Lucas sauvé d'une chaîne de contrôle. | Base hostile, poursuite active, Erika potentiellement séparée. |

**Entrée de journal :** « Une chance ne vaut rien si quelqu'un d'autre en écrit le prix. »

## Notes de réalisation

- Aucun combat requis pour les routes Vardis et indépendante: la tension vient du temps, des préparatifs et de la décision.
- La route impériale sert de tutoriel de furtivité/évasion, pas de massacre de la Résistance.
- Ne pas montrer les pouvoirs de Lucas au-delà de la pulsation finale; le joueur doit protéger une inconnue avant de connaître son potentiel.
