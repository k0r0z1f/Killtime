# Notes internes — Partenariat & diffusion (ne pas diffuser)

Dernière mise à jour : 2026-10-21

## Contexte porteur
- Alumni AEC Programmeur de jeux vidéo, Collège de Bois-de-Boulogne (Formation continue, 18 mois / 1590h).
- Connaît les profs (si encore en poste). Réputation : « one-man army obsessif » = atout pour un milieu de stage.
- Rythme : **50-60h/sem sur le projet**, dont **<5h sanctuarisées** diffusion/financement (kanban financement).
- Budget : 0 $. Chaque dépense doit être justifiée au dollar près.

## Décisions actées
1. **Nom légal : « Atelier Killtime »** (FR, requis Charte) / version anglaise « Killtime ». Plan B si refus REQ : nom sans « kill » (ex : L'Atelier du Sablier).
2. **Immatriculation : 41 $ (tarif REQ 2026, personne physique)**, droits annuels ~40$/an exemptés les 2 premières années. = FK-001/FK-019.
3. **Vérification OPIC (CIPO) + registre REQ** pour « Killtime » avant usage commercial. Non fait.
4. **Mandat stage prioritaire : Mandat A** (tranche démo tech Unity jouable 5-10 min). Chantier > 1 stage : **évaluation sur travail accompli, pas sur livraison**.
5. **Offre d'encadrement** : suivi journalier (commits GitHub) + hebdo complet + aide/dispo + éval finale. Ajouter : éval mi-stage, visite enseignant, temps rapport (standard AEC — voir dossier section 4).
6. **Stratégie diffusion** : gratuit d'abord (Pages + itch.io + 1 post/sem), revenus après audience (PDF → print → Tipeee → VTT), Kickstarter en dernier (seuil 30% J1-J3).
7. **Ne pas faire maintenant** : Discord (communauté vide = coût), Kickstarter, pub payée, « stagiaire » hors programme scolaire (illégal au QC).

## Portes BdeB (à valider avec coordonnateur)
- Option 1 : stage Étape 6 (3 mois) — exige NEQ + critères milieu d'accueil (assurance/CNESST ? supervision ? distanciel accepté ?).
- Option 2 : projet d'équipe Étape 5 (Réalisation d'un jeu vidéo) — moins de paperasse, prof garde l'encadrement.
- Contacts : [NOM PROF RÉALISATION/MÉTHODO] — [EMAIL] — message prêt, en attente lien Pages + NEQ.

## Kanban financement (data/financement.json, 21 tâches)
- Vitrine : FK-011 (Pages), FK-012 (itch.io), FK-013 (1 post/sem).
- Revenus : FK-014 (PDF), FK-015 (print), FK-016 (Tipeee), FK-017 (VTT).
- Partenaires : FK-018 (message profs), FK-019 (NEQ + critères), FK-020 (projet équipe Étape 5), FK-021 (kit playtest).
- Rappel : roadmaps.json = Unity uniquement, ne pas y mettre ces tâches.

## Fichiers liés
- `dossier_partenaires.md` : dossier outward-facing (6 sections, champs [À REMPLIR] : lien Pages, NEQ, dispo).
- Message profs : rédigé (dans historique 2026-10-21), envoi bloqué par lien Pages (FK-011) + choix mandat (fait : A).

## Prochaines actions (ordre)
1. FK-011 : déployer Pages → donne le lien pour message + posts.
2. FK-019 : REQ 41 $ + appel coordonnateur (critères milieu).
3. FK-018 : envoyer message profs.
4. FK-021 : kit playtest + annonce.

## Message prof (finalisé 2026-10-21 — en attente lien Pages + NEQ)
> Objet : Proposition de milieu de stage / projet d'équipe — jeu tactique Unity (alumni AEC jeux vidéo)
>
> Bonjour [PRÉNOM],
>
> On se connaît : [TON NOM], alumni de l'AEC Programmeur de jeux vidéo. Depuis ma sortie, je développe toujours le même projet, à temps plein (50-60h/sem) : Killtime — un système de jeu de rôle tactique publié en 12 livres web gratuits, avec un jeu vidéo tactique Unity en cours de développement.
>
> Concret, pas une idée : [LIEN PAGES — 12 livres en ligne], code source public : github.com/k0r0z1f/Killtime, démo technique 60s disponible en vidéo sur demande.
>
> Je propose d'accueillir un stagiaire (Étape 6) ou de servir de base à un projet d'équipe (Étape 5). Mandat : faire avancer une tranche démo Unity jouable 5-10 min (escouade 2v2, couvert, IA ennemie, conditions de victoire) — gameplay C#, UI combat, debug, builds.
>
> Cadre que je garantis : objectifs écrits avant démarrage, alignés sur les compétences du programme ; suivi journalier traçable (commits GitHub) + revue hebdo complète ; disponibilité réelle ; évaluations mi-stage et finale sur vos formulaires ; temps réservé pour le rapport. Le chantier dépasse un stage : l'évaluation porterait sur le travail accompli, aucun risque de stage « raté ».
>
> Côté administratif : immatriculation travailleur autonome « Atelier Killtime » [EN COURS / NEQ : XXXXX], convention tripartite signée, entente de propriété intellectuelle avant démarrage (contributions sous licence du projet, crédit nominatif).
>
> Serait-il possible de se parler 20 minutes cette semaine ? J'aimerais valider avec vous les critères de milieu d'accueil et voir si la formule stage ou projet d'équipe convient le mieux.
>
> Dossier de présentation complet : [LIEN / PIÈCE JOINTE dossier_partenaires.md]
>
> Merci,
> [TON NOM] — [TÉLÉPHONE]

## Convention commits (décidée 2026-10-21 — repo = vitrine)
- Fini les « update ». Format : `type: description courte` (ex : `feat: couvert léger 2v2`, `fix: LOS travers murs`, `docs: chapitre PA`, `test: RD-174 sprint`).
- Historique existant : on ne réécrit pas (repo public), on part propre d'ici.
- En équipe/stage : PR + revue avant merge, jamais de push direct sur main.
