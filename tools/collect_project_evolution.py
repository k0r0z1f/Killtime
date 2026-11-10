#!/usr/bin/env python3
"""
Collecteur de l'évolution complète du projet Killtime (toutes sphères).
Analyse l'intégralité de l'historique Git du dépôt depuis le commit #1 (déc. 2024)
et répartit chaque commit dans les 6 grandes Sphères du projet et leurs 17 Sous-Branches :
  1. narratif    : Killtime Narratif & Lore (Manuscrits, Tomes I-II, Overviews, River of Time)
  2. sysrp       : Système RP & Codex (Livres I-XII, Fiche PJ, Fabrique Puzzle, Pipeline MD/JSON)
  3. tactics     : Killtime Tactics Unity (Combat Grid, Story, Cosmos 3D/Shaders, VTT & Audio)
  4. site_global : Site Global & Tables Live (Portail, Navigation/Thèmes, Tables WebRTC/Chat, Admin/RBAC)
  5. atlas       : Atlas Cosmique & Mondes (Atlas des 111 Mondes, Atlas 3D des 36 Zones, Cartes d'Hybris)
  6. pilotage    : Pilotage, Kanbans & Outillage (4 Kanbans, Kickstarter, Fleurs Git, Cartographie)

Produit : data/project_evolution.json
Usage   : python3 tools/collect_project_evolution.py
"""

import json
import os
import re
import subprocess
from collections import Counter, defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TAC_EVO_PATH = os.path.join(ROOT, "data", "tactics_evolution.json")
OUT_PATH = os.path.join(ROOT, "data", "project_evolution.json")
REPO = "https://github.com/k0r0z1f/Killtime"

# ==============================================================================
# 1. DÉFINITION DES 6 SPHÈRES DU PROJET & DE LEURS 17 SOUS-BRANCHES
# ==============================================================================

SPHERES = {
    "narratif": {
        "id": "narratif",
        "label": "Killtime Narratif & Lore",
        "short_label": "Killtime Narratif",
        "subtitle": "Tomes I & II • Lore d'Hybris • River of Time",
        "icon": "🌌",
        "color": "#f59e0b",
        "color_alt": "#fbbf24",
        "glow": "rgba(245, 158, 11, 0.55)",
        "desc": "Genèse littéraire depuis décembre 2024 : manuscrits des Tomes I (The Awakening Storm) et II, encyclopédie des Overviews (protagonistes Lucas, Mina, Thomas, Erika, continents d'Hybris, Empire, Vardis), moteur temporel River of Time et portail de l'Univers.",
        "links": [
            {"label": "Univers Killtime", "href": "Killtime/index.html", "icon": "🌌"},
            {"label": "Kanban Narratif", "href": "kanban_narratif.html", "icon": "📋"}
        ]
    },
    "sysrp": {
        "id": "sysrp",
        "label": "Système RP & Codex",
        "short_label": "Système RP",
        "subtitle": "12 Livres • 53 Chapitres • Fiche PJ • Fabrique Puzzle",
        "icon": "📖",
        "color": "#38bdf8",
        "color_alt": "#22d3ee",
        "glow": "rgba(56, 189, 248, 0.55)",
        "desc": "Le jeu de rôle sur table complet en 12 Livres (Manuel du Joueur L.I–IX et Maître du Jeu L.X–XII), 329 spécialisations de 5e Force, créateur de Fiche de PJ multi-onglets, Atelier Puzzle de la Fabrique de Techniques et pipeline source unique Markdown → JSON.",
        "links": [
            {"label": "Codex (Préface)", "href": "index.html", "icon": "📖"},
            {"label": "Fiche de PJ", "href": "fiche_pj.html", "icon": "🧬"},
            {"label": "Atelier Fabrique", "href": "livre_9.html#chap-40", "icon": "🧩"},
            {"label": "Revue RP", "href": "kanban_rp.html", "icon": "✅"}
        ]
    },
    "tactics": {
        "id": "tactics",
        "label": "Killtime Tactics (Unity)",
        "short_label": "Killtime Tactics",
        "subtitle": "Combat HexGrid • Cinématiques • IA • VTT & Audio",
        "icon": "⚔️",
        "color": "#c084fc",
        "color_alt": "#a855f7",
        "glow": "rgba(192, 132, 252, 0.55)",
        "desc": "Adaptation tactique 3D sous Unity 6 : arène de combat sur grille hexagonale, résolution des dés et dégâts Titans, moteur de scénarios JSON, cinématiques, voûte céleste des compétences, salon VTT multijoueur WebSocket et synthèse musicale procédurale.",
        "links": [
            {"label": "Base Tactics", "href": "tactics_index.html", "icon": "⚔️"},
            {"label": "Fleur Tactique", "href": "tactics_evolution.html", "icon": "🌺"},
            {"label": "Fleur Fichiers", "href": "tactics_fleur_fichiers.html", "icon": "🌸"},
            {"label": "Cartographie C#", "href": "tactics_files.html", "icon": "🗂️"}
        ]
    },
    "site_global": {
        "id": "site_global",
        "label": "Site Global & Tables Live",
        "short_label": "Site Global",
        "subtitle": "Portail Unifié • Tables WebRTC/Chat • Thèmes & RBAC",
        "icon": "🌐",
        "color": "#10b981",
        "color_alt": "#34d399",
        "glow": "rgba(16, 185, 129, 0.55)",
        "desc": "Infrastructure et coquille web unifiée : navigation globale avec scrollspy, 7 thèmes visuels et protection photosensible, Tables de groupe en direct (chat persistant, voix live WebRTC, MPs, dock latéral), authentification et administration RBAC.",
        "links": [
            {"label": "Tables de Groupe", "href": "tables.html", "icon": "💬"},
            {"label": "Portail Codex", "href": "index.html", "icon": "🏛️"},
            {"label": "Administration", "href": "admin.html", "icon": "🛡️"}
        ]
    },
    "atlas": {
        "id": "atlas",
        "label": "Atlas Cosmique & Mondes",
        "short_label": "Atlas & Cosmos",
        "subtitle": "111 Mondes • 9 Vues Dynamiques • Atlas 3D (36 Zones)",
        "icon": "🪐",
        "color": "#f43f5e",
        "color_alt": "#fb7185",
        "glow": "rgba(244, 63, 94, 0.55)",
        "desc": "Cartographie astrophysique et planaire de l'univers : Atlas des 111 Mondes (tableau 20 colonnes + 9 vues analytiques, carte 2D d'Hybris à 47 marqueurs, souches de vie), Atlas 3D interactif des 36 Zones et générateurs stellaires.",
        "links": [
            {"label": "Atlas des 111 Mondes", "href": "atlas_mondes.html", "icon": "🪐"},
            {"label": "Atlas 3D (36 Zones)", "href": "atlas_3d.html", "icon": "🌐"},
            {"label": "Cosmologie (L.XI)", "href": "livre_11.html", "icon": "✨"}
        ]
    },
    "pilotage": {
        "id": "pilotage",
        "label": "Pilotage, Kanbans & Outillage",
        "short_label": "Pilotage & Outils",
        "subtitle": "4 Kanbans • Financement Kickstarter • Fleurs Git",
        "icon": "📋",
        "color": "#f97316",
        "color_alt": "#fb923c",
        "glow": "rgba(249, 115, 22, 0.55)",
        "desc": "Tour de contrôle et outillage d'ingénierie : Roadmaps Kanbans spécialisées (Tactics Unity, Revue Système RP, Projet Narratif, Financement Kickstarter), visualisations organiques Git et scripts de vérification continue.",
        "links": [
            {"label": "Roadmap Tactics", "href": "roadmap.html", "icon": "📋"},
            {"label": "Kanban Système RP", "href": "kanban_rp.html", "icon": "📖"},
            {"label": "Financement", "href": "financement.html", "icon": "🚀"},
            {"label": "Kanban Narratif", "href": "kanban_narratif.html", "icon": "🌌"}
        ]
    }
}

BRANCHES = {
    # 1. Narratif (3 sous-branches)
    "nar_manuscrits": {
        "id": "nar_manuscrits", "sphere": "narratif",
        "label": "Manuscrits & Tomes I–II", "icon": "📜",
        "color": "#fbbf24", "glow": "rgba(251, 191, 36, 0.6)"
    },
    "nar_lore": {
        "id": "nar_lore", "sphere": "narratif",
        "label": "Lore, Factions & Personnages", "icon": "🏛️",
        "color": "#f59e0b", "glow": "rgba(245, 158, 11, 0.6)"
    },
    "nar_chrono": {
        "id": "nar_chrono", "sphere": "narratif",
        "label": "River of Time & Portail Univers", "icon": "⏳",
        "color": "#eab308", "glow": "rgba(234, 179, 8, 0.6)"
    },
    # 2. Système RP (3 sous-branches)
    "rp_livres": {
        "id": "rp_livres", "sphere": "sysrp",
        "label": "Livres I–XII & Mécaniques", "icon": "📘",
        "color": "#38bdf8", "glow": "rgba(56, 189, 248, 0.6)"
    },
    "rp_fabrique": {
        "id": "rp_fabrique", "sphere": "sysrp",
        "label": "Fiche PJ & Fabrique Puzzle", "icon": "🧩",
        "color": "#22d3ee", "glow": "rgba(34, 211, 238, 0.6)"
    },
    "rp_pipeline": {
        "id": "rp_pipeline", "sphere": "sysrp",
        "label": "Pipeline Markdown/JSON & Checks", "icon": "⚙️",
        "color": "#0ea5e9", "glow": "rgba(14, 165, 233, 0.6)"
    },
    # 3. Killtime Tactics (4 sous-branches)
    "tac_combat": {
        "id": "tac_combat", "sphere": "tactics",
        "label": "Combat Grid, Calculateur & IA", "icon": "⚔️",
        "color": "#c084fc", "glow": "rgba(192, 132, 252, 0.6)"
    },
    "tac_story": {
        "id": "tac_story", "sphere": "tactics",
        "label": "Story, Scénarios & Cinématiques", "icon": "🎬",
        "color": "#a855f7", "glow": "rgba(168, 85, 247, 0.6)"
    },
    "tac_world": {
        "id": "tac_world", "sphere": "tactics",
        "label": "Cosmos 3D, Shaders & Éditeurs", "icon": "🗺️",
        "color": "#e879f9", "glow": "rgba(232, 121, 249, 0.6)"
    },
    "tac_multi": {
        "id": "tac_multi", "sphere": "tactics",
        "label": "VTT Réseau & Audio Procédural", "icon": "🔊",
        "color": "#d946ef", "glow": "rgba(217, 70, 239, 0.6)"
    },
    # 4. Site Global (3 sous-branches)
    "web_portail": {
        "id": "web_portail", "sphere": "site_global",
        "label": "Portail, Navigation & Thèmes", "icon": "🏛️",
        "color": "#34d399", "glow": "rgba(52, 211, 153, 0.6)"
    },
    "web_tables": {
        "id": "web_tables", "sphere": "site_global",
        "label": "Tables Live, Chat & WebRTC", "icon": "💬",
        "color": "#10b981", "glow": "rgba(16, 185, 129, 0.6)"
    },
    "web_auth": {
        "id": "web_auth", "sphere": "site_global",
        "label": "Comptes, RBAC & Administration", "icon": "🛡️",
        "color": "#059669", "glow": "rgba(5, 150, 105, 0.6)"
    },
    # 5. Atlas Cosmique & Mondes (2 sous-branches)
    "atl_mondes": {
        "id": "atl_mondes", "sphere": "atlas",
        "label": "Atlas des 111 Mondes & 9 Vues", "icon": "🪐",
        "color": "#f43f5e", "glow": "rgba(244, 63, 94, 0.6)"
    },
    "atl_cosmos3d": {
        "id": "atl_cosmos3d", "sphere": "atlas",
        "label": "Atlas 3D des 36 Zones & Hybris", "icon": "🌌",
        "color": "#fb7185", "glow": "rgba(251, 113, 133, 0.6)"
    },
    # 6. Pilotage, Kanbans & Outillage (2 sous-branches)
    "pil_kanbans": {
        "id": "pil_kanbans", "sphere": "pilotage",
        "label": "Roadmaps, Kanbans & Kickstarter", "icon": "📋",
        "color": "#f97316", "glow": "rgba(249, 115, 22, 0.6)"
    },
    "pil_metriques": {
        "id": "pil_metriques", "sphere": "pilotage",
        "label": "Fleurs Git, Cartographie & Outils", "icon": "🌸",
        "color": "#fb923c", "glow": "rgba(251, 146, 60, 0.6)"
    }
}

EPOCHS = [
    {
        "id": "ep1",
        "title": "Époque I : Genèse Littéraire & Archives d'Hybris",
        "short_title": "Époque I • Genèse Littéraire",
        "start": "2024-12-07",
        "end": "2025-05-23",
        "color": "#f59e0b",
        "desc": "Fondation de l'univers Killtime : rédaction des manuscrits originels (Killtime.docx, Draft 2), construction des Overviews géopolitiques (Altheris, Empire, Vardis, The Shelter, Nythari) et dossiers évolutifs de Lucas, Mina, Thomas et Erika."
    },
    {
        "id": "ep2",
        "title": "Époque II : Tomes I & II, River of Time & Portail Univers",
        "short_title": "Époque II • River of Time",
        "start": "2026-06-20",
        "end": "2026-09-08",
        "color": "#eab308",
        "desc": "Publication HTML de Volume I (The Awakening Storm) et Volume II, création du moteur chronologique River of Time, restructuration de l'archive dans Killtime/ et mise en ligne du portail narratif jusqu'aux chapitres RC1 16."
    },
    {
        "id": "ep3",
        "title": "Époque III : Naissance du Système RP & Fondation de Killtime Tactics",
        "short_title": "Époque III • Codex & Tactics",
        "start": "2026-09-08",
        "end": "2026-09-23",
        "color": "#c084fc",
        "desc": "Éclosion simultanée du Codex Système RP (Livres de règles) et du jeu Unity Killtime Tactics : arène de combat, registre d'utilisateurs, premier Atlas 3D, salon VTT WebSocket, musique adaptative et intégration du River of Time en jeu."
    },
    {
        "id": "ep4",
        "title": "Époque IV : Cartographie d'Hybris, Arcanotech & Cinématiques",
        "short_title": "Époque IV • Hybris & Arcanotech",
        "start": "2026-09-24",
        "end": "2026-10-09",
        "color": "#f43f5e",
        "desc": "Éditeur de carte mondiale d'Hybris, matrice des relations, générateurs planétaires & shaders, effets sonores procéduraux, calculateur de dégâts Titans, Erika en jeu, et création de la Roadmap Kanban et de la Fleur Git."
    },
    {
        "id": "ep5",
        "title": "Époque V : Refactoring Modulaire Unity, Atlas des Mondes & Kanbans",
        "short_title": "Époque V • Modularité & Atlas",
        "start": "2026-10-10",
        "end": "2026-10-21",
        "color": "#f97316",
        "desc": "Découpage des monolithes Unity en services et classes partielles (<800l), 434+ tests EditMode verts, Fleur des Fichiers, Atlas des 111 Mondes (9 vues), Cosmologie des 36 Zones et Kanbans Kickstarter & Narratif."
    },
    {
        "id": "ep6",
        "title": "Époque VI : Révolution du Codex (JSON/MD), Tables Live & Fabrique Puzzle",
        "short_title": "Époque VI • Codex JSON & Tables",
        "start": "2026-10-30",
        "end": "2026-11-09",
        "color": "#38bdf8",
        "desc": "Migration des 12 Livres en source unique Markdown/JSON avec garde-fous, Tables de groupe en direct (Chat + Voix WebRTC), Admin RBAC, Fiche PJ multi-onglets, Atelier Puzzle de la Fabrique de Techniques, 329 spécialisations de 5e Force et réalignement Livre V / Unity."
    }
]

# Overrides curés pour des commits précis dont le titre/sphère mérite une mise en valeur spéciale
CURATED_OVERRIDES = {
    "a96b02a": {
        "sphere": "site_global", "branch": "web_portail",
        "title": "Graine Originelle du Dépôt Killtime (Initial Commit)",
        "milestone": "🌱 Graine Originelle : Fondation du dépôt GitHub Killtime"
    },
    "bca932a": {
        "sphere": "narratif", "branch": "nar_manuscrits",
        "title": "Importation Initiale : Manuscrit Killtime, Archives & 164 Fichiers",
        "milestone": "📜 Importation fondatrice : Manuscrit Killtime, Overviews & Tables"
    },
    "fa81cc8": {
        "sphere": "narratif", "branch": "nar_manuscrits",
        "title": "Synchronisation Manuscrit Principal (Killtime.docx & HTML)"
    },
    "076fb9a": {
        "sphere": "atlas", "branch": "atl_cosmos3d",
        "title": "Cartographie d'Altheris, Continent Est & Terres Centrales (85 fichiers)",
        "milestone": "🗺️ Première grande cartographie d'Hybris (Altheris & Continents)"
    },
    "9a1ac5e": {
        "sphere": "narratif", "branch": "nar_lore",
        "title": "Overviews des Chapitres 26.1 à 26.5 & Synthèse Globale"
    },
    "1e46c53": {
        "sphere": "narratif", "branch": "nar_lore",
        "title": "Overviews Narratifs des Chapitres 27.2, 27.3 & 27.4"
    },
    "7bde94a": {
        "sphere": "atlas", "branch": "atl_cosmos3d",
        "title": "Illustrations Géographiques : Aranel, Haut-Quartier & Temple Elfique"
    },
    "209d72e": {
        "sphere": "narratif", "branch": "nar_chrono",
        "title": "Compendium des Conversations & Indexation JSON (29 fichiers)",
        "milestone": "🗂️ Création du Chat Compendium JSON & Archives Narratives"
    },
    "d82bd84": {
        "sphere": "atlas", "branch": "atl_cosmos3d",
        "title": "Cartographie du Sanctuaire (The Shelter 1771-31) & Terres du Sud"
    },
    "9c20e46": {
        "sphere": "narratif", "branch": "nar_lore",
        "title": "Portail du Groupe des Protagonistes (party.html)"
    },
    "985250a": {
        "sphere": "narratif", "branch": "nar_manuscrits",
        "title": "Ouverture du Second Manuscrit (Killtime Draft 2.docx)",
        "milestone": "✍️ Lancement de Killtime Draft 2"
    },
    "91eea9d": {
        "sphere": "narratif", "branch": "nar_manuscrits",
        "title": "Publication de Volume I (The Awakening Storm) & Volume II",
        "milestone": "📚 Publication HTML des Tomes I & II de Killtime"
    },
    "8423d8d": {
        "sphere": "narratif", "branch": "nar_chrono",
        "title": "Création du Moteur Temporel River of Time (River of Time.json)",
        "milestone": "⏳ Naissance du River of Time (Chronologie Causale)"
    },
    "dc48c16": {
        "sphere": "atlas", "branch": "atl_cosmos3d",
        "title": "Carte d'Hybris 2035 & Release Candidate Chapitre 11"
    },
    "a2c9616": {
        "sphere": "narratif", "branch": "nar_chrono",
        "title": "Script Python River of Time & Révision RC1 (Chapitres 1–11)"
    },
    "8c6f7d2": {
        "sphere": "narratif", "branch": "nar_lore",
        "title": "Portraits Officiels de Lucas, Mina & Thomas + River of Time"
    },
    "f6ca7f3": {
        "sphere": "narratif", "branch": "nar_lore",
        "title": "Système de Capacités Narratives (abilities/) & Compression Texte Intégral"
    },
    "8affac3": {
        "sphere": "site_global", "branch": "web_portail",
        "title": "Mise en Ligne du Premier Portail Web (index.html)",
        "milestone": "🌐 Premier Portail Web Principal (index.html)"
    },
    "ed67060": {
        "sphere": "narratif", "branch": "nar_chrono",
        "title": "Architecture Moteurs & Données Narratives (scripts_and_data/)",
        "milestone": "⚙️ Structuration modulaire scripts_and_data/ & manuscripts/"
    },
    "71dc23c": {
        "sphere": "narratif", "branch": "nar_manuscrits",
        "title": "Dossier Dédié des Manuscrits HTML & PDF (manuscripts/)"
    },
    "36b409c": {
        "sphere": "site_global", "branch": "web_portail",
        "title": "Système de Thèmes Clair/Sombre (theme.css, theme.js) & Moteurs"
    },
    "c0e0a61": {
        "sphere": "narratif", "branch": "nar_manuscrits",
        "title": "Release Candidate RC1 Chapitre 15 & Mise à Jour Globale"
    },
    "af1196c": {
        "sphere": "site_global", "branch": "web_portail",
        "title": "Architecture Unifiée : Sous-Site Killtime/ & Serveurs (start.sh, cycleServer.sh)",
        "milestone": "🏛️ Architecture Serveur & Séparation du Sous-Site Killtime/"
    },
    "ae251b7": {
        "sphere": "narratif", "branch": "nar_chrono",
        "title": "Consolidation Complète de l'Univers Killtime (659 fichiers)"
    },
    "1bfeebd": {
        "sphere": "narratif", "branch": "nar_lore",
        "title": "Intégration du Journal Intime de Lucas (Lucas Journal)"
    },
    "8ee4fc6": {
        "sphere": "narratif", "branch": "nar_manuscrits",
        "title": "Publication Web RC1 Chapitre 16 & Synchronisation Portail"
    },
    "0a6761a": {
        "sphere": "sysrp", "branch": "rp_livres",
        "title": "Fondation du Codex Système RP : Livres de Règles, Styles & Moteur JS",
        "milestone": "📖 Naissance du Codex Système RP (Livres de Règles)"
    },
    "13ebf8e": {
        "sphere": "sysrp", "branch": "rp_livres",
        "title": "Documentation du Système RP & Intégration au Portail Principal"
    },
    "59a92f8": {
        "sphere": "tactics", "branch": "tac_combat",
        "title": "Fondation de Killtime Tactics sous Unity & Arène de Combat",
        "milestone": "⚔️ Naissance du projet Unity Killtime Tactics"
    },
    "bc6f264": {
        "sphere": "site_global", "branch": "web_auth",
        "title": "Registre des Utilisateurs & Authentification sur les Livres du Codex",
        "milestone": "🛡️ Système de Comptes Utilisateurs (Users Registry)"
    },
    "0cdbd2f": {
        "sphere": "atlas", "branch": "atl_cosmos3d",
        "title": "Création de l'Atlas 3D Spatial (atlas_3d.html) & Progression Héros",
        "milestone": "🌌 Lancement de l'Atlas 3D Cosmique (atlas_3d.html)"
    },
    "d02cc4a": {
        "sphere": "site_global", "branch": "web_portail",
        "title": "Optimisation Galerie : 128 Miniatures (thumbs/) & Configuration .env"
    },
    "0edec96": {
        "sphere": "atlas", "branch": "atl_cosmos3d",
        "title": "Enrichissement de l'Atlas 3D & Synchronisation Livre I / Univers"
    },
    "a4f6503": {
        "sphere": "tactics", "branch": "tac_multi",
        "title": "Multijoueur WebSocket VTT & Alignement Règles Livres I–XI",
        "milestone": "🌐 Pont Réseau WebSocket VTT & Règles Codex"
    },
    "498d80c": {
        "sphere": "tactics", "branch": "tac_story",
        "title": "Éditeur de Scénarios (ScenarioEditorDevWindow) & Structures StorySceneData"
    },
    "3da57b8": {
        "sphere": "tactics", "branch": "tac_story",
        "title": "Classes de Personnages KT & Intégration du River of Time dans Unity",
        "milestone": "🌊 Convergence Narratif → Unity : River of Time intégré en jeu"
    },
    "d6b36b3": {
        "sphere": "atlas", "branch": "atl_cosmos3d",
        "title": "Éditeur de Carte Mondiale d'Hybris (World Map Editor)",
        "milestone": "🗺️ Éditeur de Carte Mondiale d'Hybris en 3D"
    },
    "5cdc657": {
        "sphere": "site_global", "branch": "web_portail",
        "title": "Publication du Manifeste IA & Architecture Documentaire (README.md)"
    },
    "2c02208": {
        "sphere": "pilotage", "branch": "pil_kanbans",
        "title": "Structuration de la Base de Données Kanban (data/roadmaps.json)"
    },
    "a93645c": {
        "sphere": "pilotage", "branch": "pil_metriques",
        "title": "Lancement de la Roadmap Kanban & de la Fleur Git (tactics_evolution.html)",
        "milestone": "🌺 Création de la Fleur Neuronale Git & Roadmap Kanban"
    },
    "f61456a": {
        "sphere": "tactics", "branch": "tac_combat",
        "title": "Refactoring Majeur : CombatCalculator (2892l) Découpé en 7 Services (RD-123)",
        "milestone": "🛠️ RD-123 : Découpage du monolithe CombatCalculator en 7 services"
    },
    "967bbb4": {
        "sphere": "pilotage", "branch": "pil_kanbans",
        "title": "Kanban Roadmap : Validation RD-123 (Services CombatCalculator)"
    },
    "c270e71": {
        "sphere": "pilotage", "branch": "pil_kanbans",
        "title": "Stabilisation Suite de Tests EditMode : 434 Tests Verts (RD-124)",
        "milestone": "🧪 RD-124 : 434 Tests EditMode Unity 100% Verts"
    },
    "1e26778": {
        "sphere": "pilotage", "branch": "pil_metriques",
        "title": "Création de la Fleur des Fichiers & Cartographie C# + Audio en 6 Partiels",
        "milestone": "🌸 Lancement de la Fleur des Fichiers (tactics_fleur_fichiers.html)"
    },
    "70ae5cd": {
        "sphere": "tactics", "branch": "tac_world",
        "title": "Refactoring Voûte Céleste : SkillTreeCosmosWindow en 8 Partiels (RD-138)"
    },
    "07fe415": {
        "sphere": "tactics", "branch": "tac_world",
        "title": "Optimisation Voûte Céleste : LOD Zoom-Out, Culling & Cache (RD-139)"
    },
    "59fc506": {
        "sphere": "tactics", "branch": "tac_combat",
        "title": "Découpage de CharacterStats en Partiels Thématiques & Mise à Jour Fleurs"
    },
    "6279820": {
        "sphere": "tactics", "branch": "tac_combat",
        "title": "Menu Contextuel de Combat Découpé en Partiels (Attack, Defense, Glyphs, Hub)"
    },
    "8120194": {
        "sphere": "tactics", "branch": "tac_combat",
        "title": "Affinage des Menus Contextuels de Combat & Synchronisation Kanban"
    },
    "efa637d": {
        "sphere": "tactics", "branch": "tac_world",
        "title": "Fenêtre de Réglage Orbital (OrbitTuningWindow) & Menus de Combat"
    },
    "08c471b": {
        "sphere": "tactics", "branch": "tac_multi",
        "title": "Moteur Musical Génératif : Batteries, Synthés & Théorie Harmonique"
    },
    "92d0792": {
        "sphere": "pilotage", "branch": "pil_metriques",
        "title": "Mise à Jour de l'Historique de la Fleur des Fichiers"
    },
    "70d6392": {
        "sphere": "tactics", "branch": "tac_world",
        "title": "Importation du Pack d'Animations de Course Féminines & Masculines (FBX)"
    },
    "101f6dd": {
        "sphere": "tactics", "branch": "tac_story",
        "title": "Directeur de Démo Alpha 3 (DemoDirectorAlpha3) & Constructeur d'Intro"
    },
    "a0c5f63": {
        "sphere": "tactics", "branch": "tac_story",
        "title": "Cartes d'Acteurs & Cinématiques de l'Éditeur de Scénario + Fleur Fichiers"
    },
    "82b69c3": {
        "sphere": "tactics", "branch": "tac_combat",
        "title": "Système de Véhicules, Grenades & Boss Titans IA JSON (RD-051..RD-186)",
        "milestone": "🤖 Boss Titans IA JSON, Véhicules & Grenades en Arène"
    },
    "680befe": {
        "sphere": "pilotage", "branch": "pil_metriques",
        "title": "Collecteurs Automatiques Amont (Fleur Git & Fleur Fichiers) + Profils de Zones"
    },
    "90097af": {
        "sphere": "tactics", "branch": "tac_combat",
        "title": "Service de Résolution des Duels, Destin (Fate) & Débogage Chrono en Arène"
    },
    "eabbfb6": {
        "sphere": "tactics", "branch": "tac_combat",
        "title": "Barres d'Outils & Contrôleurs d'Arène pour DuoTech et Titans"
    },
    "7efb71a": {
        "sphere": "atlas", "branch": "atl_mondes",
        "title": "Création de l'Atlas des Mondes (atlas_mondes.html) & Scénarios d'Arène",
        "milestone": "🪐 Naissance de l'Atlas des Mondes (atlas_mondes.html)"
    },
    "5d1218b": {
        "sphere": "atlas", "branch": "atl_mondes",
        "title": "Cosmologie Canon : 36 Zones, Empire 22 Ma & Disciples (RD-200)",
        "milestone": "🌌 RD-200 : Cosmologie des 36 Zones & Empire 22 Ma"
    },
    "2e52fc6": {
        "sphere": "tactics", "branch": "tac_world",
        "title": "Portail d'Entrée Killtime Tactics (tactics_index.html) & Sauvegarde Atlas"
    },
    "d3057f9": {
        "sphere": "sysrp", "branch": "rp_livres",
        "title": "Restructuration du Codex : 8 Livres Joueur + 3 Livres MJ & Alignement Unity",
        "milestone": "📚 Restructuration Majeure des Livres du Codex & Sync Unity"
    },
    "452944d": {
        "sphere": "pilotage", "branch": "pil_kanbans",
        "title": "Lancement du Kanban Financement Kickstarter (financement.html)",
        "milestone": "🚀 Création du Kanban Financement Kickstarter"
    },
    "c5eab8b": {
        "sphere": "pilotage", "branch": "pil_kanbans",
        "title": "Kanban Projet Narratif (kanban_narratif.html) & Règles d'Entrave Unity",
        "milestone": "📋 Création du Kanban Narratif & Miroir Architect Tasks"
    },
    "29a1dbf": {
        "sphere": "atlas", "branch": "atl_mondes",
        "title": "Atlas des 111 Mondes (9 Vues Interactives) & Synchronisation Globale"
    },
    "7b99519": {
        "sphere": "sysrp", "branch": "rp_fabrique",
        "title": "Créateur de PJ Système RP : Onglets Multiples, Sauvegarde Auto & Fiche Détachée",
        "milestone": "🧬 Créateur de PJ Multi-Onglets avec Sauvegarde Serveur"
    },
    "5fd6c72": {
        "sphere": "sysrp", "branch": "rp_fabrique",
        "title": "Fiche de PJ : Aide à la Saisie & Intégration des 27 Compétences du Livre III"
    },
    "e18d068": {
        "sphere": "pilotage", "branch": "pil_kanbans",
        "title": "Mise à Jour des Tâches du Kanban Revue Système RP"
    },
    "3e816b4": {
        "sphere": "pilotage", "branch": "pil_kanbans",
        "title": "Planification & Suivi de la Roadmap Système RP (data/kanban_rp.json)"
    },
    "af25f29": {
        "sphere": "site_global", "branch": "web_auth",
        "title": "Console d'Administration Locale (admin.html) & Contrôle d'Accès RBAC",
        "milestone": "🛡️ Console Admin Locale & Permissions RBAC par Rôle"
    },
    "48ac5de": {
        "sphere": "site_global", "branch": "web_tables",
        "title": "Tables de Groupe Live (tables.html) : Chat Persistant, MPs, Images & Amis",
        "milestone": "💬 Lancement des Tables de Groupe Live (Chat, MPs & Amis)"
    },
    "4f53934": {
        "sphere": "site_global", "branch": "web_tables",
        "title": "Tables de Groupe : Salons Vocaux Live WebRTC & Contrôles Serveur",
        "milestone": "🎙️ Voix Live WebRTC intégrée aux Tables de Groupe"
    },
    "903549f": {
        "sphere": "pilotage", "branch": "pil_kanbans",
        "title": "Kanban Système RP : Validation RP-066 (Voix Live WebRTC des Tables)"
    },
    "3155074": {
        "sphere": "site_global", "branch": "web_tables",
        "title": "Dock Latéral de Chat Persistant sur Toutes les Pages du Site"
    },
    "0b6b8f4": {
        "sphere": "sysrp", "branch": "rp_pipeline",
        "title": "Migration du Livre I en Source Unique JSON (RP-086) & Dons d'Espèces"
    },
    "2962114": {
        "sphere": "sysrp", "branch": "rp_pipeline",
        "title": "Pipeline Markdown (content/) → JSON pour le Livre I & Outils de Build",
        "milestone": "⚙️ Pipeline Source Unique Markdown (content/) → JSON → HTML"
    },
    "904a5af": {
        "sphere": "sysrp", "branch": "rp_pipeline",
        "title": "Migration Complète des Livres IV à XII en Markdown (content/) — RP-101..108",
        "milestone": "📘 Les 12 Livres unifiés sous le pipeline Markdown/JSON"
    },
    "a809fcd": {
        "sphere": "atlas", "branch": "atl_mondes",
        "title": "Alignement des Créateurs, Outils & Atlas sur les JSON des Livres II–XII"
    },
    "9210feb": {
        "sphere": "sysrp", "branch": "rp_fabrique",
        "title": "Livre III Chap. 14 : Fabrique de Techniques & Décalage Livre IV (RD-212..214)",
        "milestone": "⚒️ Création du Chapitre de la Fabrique de Techniques"
    },
    "2ad0b18": {
        "sphere": "sysrp", "branch": "rp_fabrique",
        "title": "Livre IX Chap. 39 : Atelier Interactif de la Fabrique de Techniques (RD-215)"
    },
    "cb59c68": {
        "sphere": "sysrp", "branch": "rp_fabrique",
        "title": "Atelier Fabrique : Système de Puzzle à Ensembles & Pièces Emboîtées (RD-219)",
        "milestone": "🧩 Atelier Puzzle de la Fabrique de Techniques (Pièces Emboîtées)"
    },
    "4a71425": {
        "sphere": "sysrp", "branch": "rp_fabrique",
        "title": "Atelier Fabrique : Graphe Nodal Interactif & Menus Contextuels Intelligents"
    },
    "862135a": {
        "sphere": "sysrp", "branch": "rp_fabrique",
        "title": "Fabrique Puzzle Intégrée en Popout à la Fiche PJ & Tri par Personnage"
    },
    "73dd3ec": {
        "sphere": "site_global", "branch": "web_portail",
        "title": "7 Thèmes Visuels, Voile Photosensible (task_eng_05) & Règles Moral/Nuit",
        "milestone": "🎨 7 Thèmes Visuels & Gate d'Accessibilité Photosensible"
    },
    "dcc0460": {
        "sphere": "pilotage", "branch": "pil_kanbans",
        "title": "Planification à 6 Mois : 23 Tâches RP, Jalons Alpha/Bêta Unity & Kickstarter",
        "milestone": "🗺️ Feuille de Route Unifiée à 6 Mois (RP + Unity + Kickstarter)"
    },
    "1234b67": {
        "sphere": "sysrp", "branch": "rp_livres",
        "title": "Livre IV : Expansion des 4 Voies Élémentaires (131 Spécialisations & Arbres)"
    },
    "0beccaf": {
        "sphere": "sysrp", "branch": "rp_livres",
        "title": "Livre IV : Expansion de la Voie Primale (Vie, Création, Destruction — 242 Spés)"
    },
    "d82ff1d": {
        "sphere": "sysrp", "branch": "rp_livres",
        "title": "Livre IV : Expansion de la Voie de l'Esprit (TK, TP, Clairvoyance — 329 Spés)",
        "milestone": "✨ Compendium Complet des 329 Spécialisations de 5e Force"
    },
    "98906a7": {
        "sphere": "sysrp", "branch": "rp_pipeline",
        "title": "Numérotation Continue (Chapitres 1–53) & Synchronisation Codex / Unity / Kanbans"
    },
    "5b8b6b4": {
        "sphere": "site_global", "branch": "web_portail",
        "title": "Navigation Codex : Bouton Tout Replier/Déplier & Ouverture Sans Flash"
    },
    "4786a6e": {
        "sphere": "sysrp", "branch": "rp_livres",
        "title": "Livre V §20.3 : Barème PA Rebalancé (1,5m, Micro-Actions, Réactions) & Sync Unity"
    },
    "14686c4": {
        "sphere": "sysrp", "branch": "rp_livres",
        "title": "Livre V §§20.7–20.8 : Unification de la Lutte, Charge & Réalignement RD-231",
        "milestone": "🌸 Floraison Complète : 382 Commits à travers les 6 Sphères"
    }
}


def sh(*args):
    return subprocess.run(args, cwd=ROOT, capture_output=True, text=True).stdout


def file_type_of(path):
    pl = path.lower()
    if pl.endswith(".cs"):
        return "csharp"
    if pl.endswith(".md"):
        return "markdown"
    if pl.endswith(".json") or pl.endswith(".json.bak"):
        return "json"
    if pl.endswith(".html"):
        return "html"
    if pl.endswith(".js"):
        return "js"
    if pl.endswith(".css"):
        return "css"
    if pl.endswith(".py") or pl.endswith(".sh"):
        return "python"
    if pl.endswith(".shader"):
        return "shader"
    if pl.endswith(".unity") or pl.endswith(".prefab"):
        return "scene"
    if pl.endswith((".docx", ".odt", ".pdf", ".txt")):
        return "manuscript"
    if pl.endswith((".jpg", ".png", ".ico", ".fbx")):
        return "asset"
    return "other"


def classify_file_sphere_and_branch(path):
    """Retourne (sphere, branch) pour un chemin de fichier du dépôt."""
    p = path.strip().strip('"')
    pl = p.lower()
    if p.startswith(".~lock") or "/.~lock" in p:
        return None, None

    # 1. Killtime Tactics (Unity)
    if p.startswith("KilltimeTactics/") or p == "run_kt.sh":
        if any(k in p for k in ["/Audio/", "/Multi/", "/WebGL/", "VTT", "Music", "Audio"]):
            return "tactics", "tac_multi"
        if any(k in p for k in ["/Story/", "Scenario", "Cinematic", "Story", "/Demo/"]):
            return "tactics", "tac_story"
        if any(k in p for k in ["/Camera/", "/Shaders/", "/Tools/", "Cosmos", "Starfield", "WorldMap", "Planet", "Orbit", "HexGrid"]):
            return "tactics", "tac_world"
        return "tactics", "tac_combat"

    # 2. Atlas Cosmique & Mondes
    if "atlas_mondes" in pl:
        return "atlas", "atl_mondes"
    if "atlas_3d" in pl or "hybris 2035" in pl or "hybris_history" in pl:
        return "atlas", "atl_cosmos3d"

    # 3. Pilotage, Kanbans & Outillage
    if p in ("tactics_evolution.html", "tactics_files.html", "tactics_fleur_fichiers.html",
             "data/tactics_evolution.json", "data/tactics_files.json",
             "data/tactics_files_history.json", "data/tactics_git_raw.json") or (
             p.startswith("tools/") and "tactics" in pl):
        return "pilotage", "pil_metriques"
    if p in ("roadmap.html", "financement.html", "kanban_rp.html", "kanban_narratif.html",
             "data/roadmaps.json", "data/financement.json", "data/kanban_rp.json", "data/kanban_narratif.json",
             "dossier_partenaires.md", "GoFundMe.html") or p.startswith("data/roadmaps.json") or (
             p.startswith("js/") and any(k in pl for k in ["kanban", "roadmap", "financement"])):
        return "pilotage", "pil_kanbans"

    # 4. Système RP & Codex
    if p == "fiche_pj.html" or "fiche_pj" in pl or "fabrique" in pl:
        return "sysrp", "rp_fabrique"
    if p.startswith("tools/") and any(k in pl for k in ["livre", "nav", "liens", "chapitre", "armory"]):
        return "sysrp", "rp_pipeline"
    if p.startswith("content/") or p.startswith("data/livre_"):
        return "sysrp", "rp_pipeline"
    if p.startswith("livre_") or p.startswith("js/livre_"):
        return "sysrp", "rp_livres"

    # 5. Site Global & Tables Live
    if p == "tables.html" or p.startswith("data/tables/") or p.startswith("data/friends") or "tables" in pl:
        if "killtime tables archive" not in pl:
            return "site_global", "web_tables"
    if p == "admin.html" or p.startswith("users/") or p in ("js/admin.js", "js/auth.js", ".env.example"):
        return "site_global", "web_auth"
    if p in ("index.html", "tactics_index.html", "README.md", "LICENSE", ".gitignore",
             "favicon.ico", "start.sh", "cycleServer.sh", "theme.css", "theme.js") or (
             p.startswith("css/") or p in ("js/codex.js", "js/theme.js", "js/theme-init.js")):
        return "site_global", "web_portail"

    # 6. Killtime Narratif & Lore
    if any(k in pl for k in ["manuscript", "volume i", "volume ii", "killtime.docx", "killtime.html",
                             "killtime fr.html", "draft 2", "killtime_compressed", "excerpt"]):
        return "narratif", "nar_manuscrits"
    if any(k in pl for k in ["river of time", "timelines", "conversations.html", "chat-compendium",
                             "chatgpt/", "scripts_and_data/", "chat retriever"]):
        return "narratif", "nar_chrono"
    if (p.startswith("Killtime/") or p.startswith("lore/") or p.startswith("overview/")
            or p.startswith("img/") or p.startswith("abilities/") or "overview" in pl
            or p == "party.html" or p.endswith((".jpg", ".png", ".docx", ".odt", ".pdf"))):
        return "narratif", "nar_lore"

    return "site_global", "web_portail"


def map_tactics_branch(old_br):
    return {
        "tactics": "tac_combat",
        "core": "tac_combat",
        "story": "tac_story",
        "visuals": "tac_world",
        "tools": "tac_world",
        "network": "tac_multi",
        "audio": "tac_multi",
    }.get(old_br, "tac_combat")


def humanize_commit_title(idx, c, primary_sphere, branch, tac_node):
    """Produit un titre clair, informatif et en français pour chaque commit."""
    short = c["short"]
    raw = (c["raw_msg"] or "").strip()

    # 1. Override explicite curé
    if short in CURATED_OVERRIDES and "title" in CURATED_OVERRIDES[short]:
        return CURATED_OVERRIDES[short]["title"]

    # 2. Si présent dans tactics_evolution.json avec un vrai titre curé
    if tac_node:
        t_title = (tac_node.get("title") or "").strip()
        if t_title and t_title.lower() not in ("update", "updates", "refactor", "refactor and update", "pdate", "uypdate"):
            return t_title

    clean_files = [p for _, p in c["files"] if not p.startswith(".~lock") and "/.~lock" not in p]
    statuses = [st for st, p in c["files"] if not p.startswith(".~lock") and "/.~lock" not in p]

    # 3. Commits de suppression / réorganisation GitHub Web (17 août & 26 août 2026)
    if raw.startswith("Delete "):
        target = raw[len("Delete "):].strip()
        if "directory" in target:
            dname = target.replace("directory", "").strip()
            return f"Restructuration Archive : Nettoyage du dossier racine {dname}/ ({len(clean_files)} f.)"
        if ".~lock" in target:
            return "Nettoyage Dépôt : Suppression de verrou bureautique (.lock)"
        return f"Restructuration Dépôt : Migration / Retrait de {target}"

    # 4. Commits "Add files via upload" (Juin – Août 2026)
    if raw.lower() == "add files via upload":
        if any("Volume I" in p for p in clean_files):
            if any("River of Time" in p for p in clean_files):
                return "Mise à jour de Volume I (The Awakening Storm) & River of Time"
            return "Mise à jour du Manuscrit HTML Volume I : The Awakening Storm"
        if any(p.startswith("lore/") or p.startswith('"lore/') for p in clean_files):
            sub = next((p.split("/")[1] for p in clean_files if "/" in p), "Lore")
            return f"Importation Encyclopédie Lore : Dossier {sub} ({len(clean_files)} fichiers)"
        if any(p.startswith("img/") or p.startswith('"img/') for p in clean_files):
            return f"Importation Galerie Illustrée d'Hybris ({len(clean_files)} illustrations)"
        if any(p.startswith("chatgpt/") or "chat " in p for p in clean_files):
            return f"Importation Archives de Sessions Narratives ({len(clean_files)} logs)"
        if clean_files == ["index.html"]:
            return "Mise à jour du Portail Principal (index.html)"
        if clean_files:
            names = ", ".join(p.split("/")[-1] for p in clean_files[:2])
            return f"Importation : {names} ({len(clean_files)} fichiers)"

    # 5. Commits "Update .~lock..." ou "Update X, Y, and N more files..." (Déc 2024 – Mai 2025)
    if raw.startswith("Update "):
        if not clean_files:
            return "Sauvegarde de Session Manuscrit (Verrou d'édition)"
        has_docx = any("Killtime.docx" in p for p in clean_files)
        has_html = any("Killtime.html" in p for p in clean_files)
        has_conv = any("Conversations.html" in p or "chat-compendium" in p for p in clean_files)
        overviews = [p.split("/")[-1].replace(" Overview.html", "") for p in clean_files if "Overview.html" in p and p != "Overview.html"]
        imgs = [p.split("/")[-1].rsplit(".", 1)[0] for p in clean_files if p.endswith((".jpg", ".png"))]
        chats = [p for p in clean_files if p.startswith("chatgpt/")]

        if overviews and len(overviews) <= 3 and not has_docx:
            return f"Dossiers Lore & Overviews : {', '.join(overviews[:3])}"
        if overviews and has_docx:
            return f"Manuscrit Killtime & Overviews ({overviews[0]} +{len(overviews)-1} fiches)" if len(overviews) > 1 else f"Manuscrit Killtime & Fiche {overviews[0]}"
        if imgs and not has_docx and len(imgs) <= 3:
            return f"Illustrations des Personnages & Lieux : {', '.join(imgs)}"
        if has_conv and chats:
            return f"Archives de Conversations & Compendium ({len(chats)} sessions, {len(clean_files)} f.)"
        if has_docx and has_html and len(clean_files) <= 4:
            return "Rédaction Manuscrit Killtime ( Synchronisation DOCX & HTML )"
        if has_docx and len(clean_files) == 1:
            return "Avancement Rédactionnel du Manuscrit Principal (Killtime.docx)"
        if clean_files == ["Overview.html"]:
            return "Mise à jour du Tableau Synoptique Général (Overview.html)"
        if clean_files == ["README.md"]:
            return "Mise à jour de la Présentation du Dépôt (README.md)"
        if clean_files == ["Killtime.html"]:
            return "Mise à jour de l'Édition Web du Manuscrit (Killtime.html)"
        return f"Évolution Manuscrit & Lore d'Hybris ({len(clean_files)} fichiers mis à jour)"

    # 6. Messages courts (rc1, gitignore, fix, update...)
    rl = raw.lower()
    if rl.startswith("rc1 "):
        chap = raw[4:].strip()
        return f"Révision Manuscrit RC1 — Chapitre {chap}"
    if rl == "website rc1 16":
        return "Mise à jour du Site Narratif Killtime — RC1 Chapitre 16"
    if rl == "before magic wand":
        return "Point de Sauvegarde du Portail avant Refonte Interactive"
    if rl == "site updates":
        return "Mise à jour de l'Interface du Site Narratif & Portail"
    if rl in ("updates 16", "16.1"):
        return f"Manuscrit & Lore : Avancement Chapitre {raw}"
    if rl == "gitignore":
        return "Configuration des Règles d'Exclusion Git (.gitignore)"
    if rl == "fix":
        return "Ajustement des Règles d'Agent & Paramètres de l'Espace de Travail"
    if rl == "ds":
        return "Mise à jour de Volume I : The Awakening Storm"

    return raw[:88] if raw else f"Commit #{idx} ({short})"


def derive_summary(idx, c, title, sphere, branch, spheres_touched, ins, dels, clean_files, tac_node):
    if tac_node and tac_node.get("summary") and not tac_node["summary"].endswith("« update »"):
        base_sum = tac_node["summary"]
    else:
        top_names = [p.split("/")[-1] for p in clean_files[:4]]
        files_str = ", ".join(top_names) if top_names else "métadonnées"
        if len(clean_files) > 4:
            files_str += f" (+{len(clean_files) - 4} autres)"
        base_sum = f"Commit #{idx} ({c['short']} • {c['date_display']}) : {title}. Fichiers : {files_str} (+{ins} / -{dels} lignes)."

    if len(spheres_touched) > 1:
        sp_labels = [SPHERES[s]["short_label"] for s in spheres_touched if s in SPHERES]
        base_sum += f" [Pont Inter-Sphères : {' ↔ '.join(sp_labels)}]"
    return base_sum


def collect():
    # 1. Charger les nœuds curés de tactics_evolution.json
    tac_by_hash = {}
    if os.path.exists(TAC_EVO_PATH):
        with open(TAC_EVO_PATH, encoding="utf-8") as f:
            tac_data = json.load(f)
            for n in tac_data.get("nodes", []):
                tac_by_hash[n["hash"]] = n

    # 2. Lire tous les commits avec --name-status en une seule passe
    log_status = sh("git", "log", "--reverse", "--name-status",
                    "--format=@@%H|%h|%ad|%an|%s", "--date=iso")
    commits = []
    cur = None
    for line in log_status.splitlines():
        if line.startswith("@@"):
            if cur:
                commits.append(cur)
            H, h, d, a, s = line[2:].split("|", 4)
            cur = {
                "hash": H, "short": h, "date": d,
                "date_display": d[:10], "author": a,
                "raw_msg": s, "files": []
            }
        elif cur is not None and line.strip():
            parts = line.split("\t")
            st = parts[0][0]
            p = parts[-1].strip().strip('"')
            cur["files"].append((st, p))
    if cur:
        commits.append(cur)

    # 3. Lire tous les --numstat en une seule passe rapide
    log_numstat = sh("git", "log", "--reverse", "--numstat", "--format=@@%H")
    numstats_by_hash = {}
    cur_h = None
    for line in log_numstat.splitlines():
        if line.startswith("@@"):
            cur_h = line[2:].strip()
            numstats_by_hash[cur_h] = [0, 0]
        elif cur_h and line.strip():
            parts = line.split("\t")
            if len(parts) >= 2:
                try:
                    numstats_by_hash[cur_h][0] += int(parts[0])
                    numstats_by_hash[cur_h][1] += int(parts[1])
                except ValueError:
                    pass

    # 4. Construire les nœuds enrichis
    nodes = []
    sphere_stats = {k: {
        "primary_commits": 0,
        "total_commits_touching": 0,
        "insertions": 0,
        "deletions": 0,
        "files_touched": 0,
        "milestones_count": 0,
        "first_date": None,
        "last_date": None,
    } for k in SPHERES}

    for idx, c in enumerate(commits, 1):
        H = c["hash"]
        short = c["short"]
        tac_node = tac_by_hash.get(H)
        ins, dels = numstats_by_hash.get(H, [0, 0])

        # Classifier chaque fichier du commit
        sp_counter = Counter()
        br_counter = Counter()
        enriched_files = []
        seen_paths = set()
        clean_paths = []

        for st, p in c["files"]:
            if p in seen_paths:
                continue
            seen_paths.add(p)
            sp, br = classify_file_sphere_and_branch(p)
            if sp is None:
                continue
            clean_paths.append(p)
            sp_counter[sp] += 1
            br_counter[br] += 1
            if len(enriched_files) < 25 and not p.endswith(".meta"):
                enriched_files.append({
                    "status": st,
                    "name": p.split("/")[-1],
                    "path": p,
                    "type": file_type_of(p),
                    "sphere": sp
                })

        ml = (c["raw_msg"] or "").lower()

        # Déterminer la sphère principale et la sous-branche
        if short in CURATED_OVERRIDES:
            primary_sphere = CURATED_OVERRIDES[short]["sphere"]
            primary_branch = CURATED_OVERRIDES[short]["branch"]
        elif any(k in ml for k in ["cosmologie", "atlas_mondes", "atlas_3d"]):
            primary_sphere = "atlas"
            primary_branch = "atl_mondes" if "mondes" in ml or "cosmologie" in ml else "atl_cosmos3d"
        elif any(k in ml for k in ["tables", "webrtc", "voix live", "dock latéral"]):
            primary_sphere = "site_global"
            primary_branch = "web_tables"
        elif any(k in ml for k in ["admin", "rbac", "users.json", "users registry"]):
            primary_sphere = "site_global"
            primary_branch = "web_auth"
        elif any(k in ml for k in ["nav codex", "bouton tout replier", "7 themes"]):
            primary_sphere = "site_global"
            primary_branch = "web_portail"
        elif ml.startswith("kanban") or "mise à jour roadmap" in ml:
            primary_sphere = "pilotage"
            primary_branch = "pil_kanbans"
        elif any(k in ml for k in ["fabrique", "fiche pj", "createur pj", "atelier puzzle"]):
            primary_sphere = "sysrp"
            primary_branch = "rp_fabrique"
        elif any(k in ml for k in ["en json", "textes en markdown", "content/", "renumber", "cascade 20-54", "insertion chapitres"]):
            primary_sphere = "sysrp"
            primary_branch = "rp_pipeline"
        elif any(k in ml for k in ["livre ", "livres ", "chap.", "chap ", "chap-", "sysrp", "face-à-face", "formule regle", "fusion chap", "rp-0", "rp-1"]):
            primary_sphere = "sysrp"
            primary_branch = "rp_livres"
        elif tac_node:
            primary_sphere = "tactics"
            primary_branch = map_tactics_branch(tac_node.get("branch", "tactics"))
        elif sp_counter:
            primary_sphere = sp_counter.most_common(1)[0][0]
            # Choisir la sous-branche majoritaire au sein de cette sphère
            valid_brs = [(br, cnt) for br, cnt in br_counter.items() if BRANCHES[br]["sphere"] == primary_sphere]
            if valid_brs:
                primary_branch = max(valid_brs, key=lambda x: x[1])[0]
            else:
                primary_branch = next(k for k, v in BRANCHES.items() if v["sphere"] == primary_sphere)
        else:
            if "killtime" in ml:
                primary_sphere, primary_branch = "narratif", "nar_manuscrits"
            else:
                primary_sphere, primary_branch = "site_global", "web_portail"

        # Garantir que la sphère principale est dans sp_counter
        if primary_sphere not in sp_counter:
            sp_counter[primary_sphere] = 1

        # Ordonner spheres_touched avec primary_sphere en tête
        spheres_touched = [primary_sphere] + [s for s, _ in sp_counter.most_common() if s != primary_sphere]

        # Déterminer l'époque
        d_str = c["date_display"]
        if d_str <= "2025-05-23":
            epoch_id = "ep1"
        elif d_str < "2026-09-08" or (d_str == "2026-09-08" and idx <= 168):
            epoch_id = "ep2"
        elif d_str <= "2026-09-23":
            epoch_id = "ep3"
        elif d_str <= "2026-10-09":
            epoch_id = "ep4"
        elif d_str <= "2026-10-21":
            epoch_id = "ep5"
        else:
            epoch_id = "ep6"

        # Titre & Milestone
        title = humanize_commit_title(idx, c, primary_sphere, primary_branch, tac_node)
        milestone = None
        if short in CURATED_OVERRIDES and "milestone" in CURATED_OVERRIDES[short]:
            milestone = CURATED_OVERRIDES[short]["milestone"]
        elif tac_node and tac_node.get("milestone"):
            ms = tac_node["milestone"]
            if not ms.startswith("🌸 Floraison complète"):
                milestone = ms

        summary = derive_summary(
            idx, c, title, primary_sphere, primary_branch,
            spheres_touched, ins, dels, clean_paths, tac_node
        )

        node = {
            "id": idx,
            "hash": H,
            "short": short,
            "date": c["date"],
            "date_display": d_str,
            "author": c["author"],
            "raw_msg": c["raw_msg"],
            "title": title,
            "summary": summary,
            "sphere": primary_sphere,
            "spheres_touched": spheres_touched,
            "sphere_weights": dict(sp_counter),
            "is_bridge": len(spheres_touched) > 1,
            "branch": primary_branch,
            "epoch": epoch_id,
            "milestone": milestone,
            "insertions": ins,
            "deletions": dels,
            "files_count": len(clean_paths) if clean_paths else len(c["files"]),
            "files": enriched_files
        }
        nodes.append(node)

        # Mise à jour des statistiques de sphères
        st_obj = sphere_stats[primary_sphere]
        st_obj["primary_commits"] += 1
        st_obj["insertions"] += ins
        st_obj["deletions"] += dels
        st_obj["files_touched"] += node["files_count"]
        if milestone:
            st_obj["milestones_count"] += 1
        for sp_t in spheres_touched:
            sphere_stats[sp_t]["total_commits_touching"] += 1
            if sphere_stats[sp_t]["first_date"] is None or d_str < sphere_stats[sp_t]["first_date"]:
                sphere_stats[sp_t]["first_date"] = d_str
            if sphere_stats[sp_t]["last_date"] is None or d_str > sphere_stats[sp_t]["last_date"]:
                sphere_stats[sp_t]["last_date"] = d_str

    # Injecter les stats dans SPHERES
    for sp_id, sp_info in SPHERES.items():
        sp_stats = sphere_stats[sp_id]
        sp_stats["branches_count"] = sum(1 for b in BRANCHES.values() if b["sphere"] == sp_id)
        sp_info["stats"] = sp_stats

    # 5. Construire les liens :
    # - links : filiation intra-branche (chaque nœud relié au précédent de la même sous-branche ou sphère)
    # - cross_links : synapses causales intra-sphère
    # - bridge_links : ponts inter-sphères (quand un commit hybride relie sa sphère principale au dernier nœud d'une autre sphère touchée)
    links = []
    cross_links = []
    bridge_links = []

    last_in_branch = {}
    last_in_sphere = {}

    for n in nodes:
        nid = n["id"]
        sp = n["sphere"]
        br = n["branch"]

        if br in last_in_branch:
            links.append({
                "source": last_in_branch[br],
                "target": nid,
                "sphere": sp,
                "branch": br,
                "type": "branch_filiation"
            })
        elif sp in last_in_sphere:
            links.append({
                "source": last_in_sphere[sp],
                "target": nid,
                "sphere": sp,
                "branch": br,
                "type": "sphere_root"
            })
        elif nid > 1:
            links.append({
                "source": 1,
                "target": nid,
                "sphere": sp,
                "branch": br,
                "type": "origin_seed"
            })

        # Ponts inter-sphères (si ce commit touche d'autres sphères déjà actives)
        if len(n["spheres_touched"]) > 1:
            for other_sp in n["spheres_touched"][1:3]:  # jusqu'aux 2 principales sphères secondaires
                if other_sp in last_in_sphere:
                    target_other = last_in_sphere[other_sp]
                    if target_other != nid:
                        bridge_links.append({
                            "source": target_other,
                            "target": nid,
                            "from_sphere": other_sp,
                            "to_sphere": sp,
                            "reason": f"Pont {SPHERES[other_sp]['short_label']} ↔ {SPHERES[sp]['short_label']} : {n['title'][:55]}"
                        })

        last_in_branch[br] = nid
        last_in_sphere[sp] = nid

    # Ajouter les cross_links causaux curés de tactics_evolution (mappés par hash -> nouvel id)
    hash_to_id = {n["hash"]: n["id"] for n in nodes}
    if os.path.exists(TAC_EVO_PATH):
        with open(TAC_EVO_PATH, encoding="utf-8") as f:
            tac_data = json.load(f)
            id_to_hash_tac = {n["id"]: n["hash"] for n in tac_data.get("nodes", [])}
            for cl in tac_data.get("cross_links", []):
                h_src = id_to_hash_tac.get(cl["source"])
                h_tgt = id_to_hash_tac.get(cl["target"])
                if h_src in hash_to_id and h_tgt in hash_to_id:
                    cross_links.append({
                        "source": hash_to_id[h_src],
                        "target": hash_to_id[h_tgt],
                        "type": "causal",
                        "reason": cl.get("reason", "Lien causal Tactics")
                    })

    # Ajouter aussi des liens causaux entre jalons d'une même sphère
    milestones_by_sphere = defaultdict(list)
    for n in nodes:
        if n["milestone"]:
            milestones_by_sphere[n["sphere"]].append(n)
    for sp, ms_nodes in milestones_by_sphere.items():
        for a, b in zip(ms_nodes, ms_nodes[1:]):
            cross_links.append({
                "source": a["id"],
                "target": b["id"],
                "type": "milestone_chain",
                "reason": f"Progression {SPHERES[sp]['short_label']} : {a['title'][:36]} → {b['title'][:36]}"
            })

    total_ins = sum(n["insertions"] for n in nodes)
    total_dels = sum(n["deletions"] for n in nodes)
    total_milestones = sum(1 for n in nodes if n["milestone"])
    total_bridges = sum(1 for n in nodes if n["is_bridge"])

    payload = {
        "project": "Killtime — Écosystème Complet",
        "repository": REPO,
        "total_commits": len(nodes),
        "totals": {
            "commits": len(nodes),
            "spheres": len(SPHERES),
            "branches": len(BRANCHES),
            "epochs": len(EPOCHS),
            "milestones": total_milestones,
            "bridges": total_bridges,
            "insertions": total_ins,
            "deletions": total_dels
        },
        "date_range": {
            "start": nodes[0]["date_display"],
            "end": nodes[-1]["date_display"]
        },
        "spheres": SPHERES,
        "branches": BRANCHES,
        "epochs": EPOCHS,
        "nodes": nodes,
        "links": links,
        "cross_links": cross_links,
        "bridge_links": bridge_links
    }

    with open(OUT_PATH, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=1)

    print(f"Ecrit {OUT_PATH} :")
    print(f"  - Commits totaux : {len(nodes)} ({nodes[0]['date_display']} -> {nodes[-1]['date_display']})")
    print(f"  - Jalons (milestones) : {total_milestones} | Commits ponts multi-spheres : {total_bridges}")
    print(f"  - Liens : {len(links)} filiations, {len(cross_links)} synapses, {len(bridge_links)} ponts inter-spheres")
    for sp_id, sp in SPHERES.items():
        st = sp["stats"]
        print(f"  * [{sp['icon']} {sp['short_label']:18s}] primaires={st['primary_commits']:3d} | totaux={st['total_commits_touching']:3d} | jalons={st['milestones_count']:2d} | {st['first_date']} -> {st['last_date']}")


if __name__ == "__main__":
    collect()
