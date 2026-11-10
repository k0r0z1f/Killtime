#!/usr/bin/env python3
"""
Générateur de la page d'évolution globale du projet Killtime (toutes sphères) :
produit project_evolution.html à partir de data/project_evolution.json.

5 modes de visualisation interactifs :
  1. 🪐 Galaxie des Sphères (6 territoires orbitaux autour du Cœur Originel + sous-branches en corolles)
  2. 🌺 Fleur Globale (Corolle botanique unifiée à secteurs de sphères et 17 pétales)
  3. 🌊 Fleuve des Sphères (6 couloirs chronologiques parallèles + ponts verticaux inter-sphères)
  4. 🧠 Cortex Synaptique (Lobes neuronaux par sphère et axones causaux)
  5. 🌲 Arbre Causal & Rhizome (Arbre phylogénétique stratifié par les 6 Époques + mode physique libre)

Usage : python3 tools/generate_project_evolution.py
"""

import json
import os

MOIS_FR = {
    "01": "Janvier", "02": "Février", "03": "Mars", "04": "Avril",
    "05": "Mai", "06": "Juin", "07": "Juillet", "08": "Août",
    "09": "Septembre", "10": "Octobre", "11": "Novembre", "12": "Décembre"
}
MOIS_SHORT = {
    "01": "Jan", "02": "Fév", "03": "Mar", "04": "Avr", "05": "Mai",
    "06": "Juin", "07": "Juil", "08": "Août", "09": "Sep",
    "10": "Oct", "11": "Nov", "12": "Déc"
}


def build_html():
    with open("data/project_evolution.json", "r", encoding="utf-8") as f:
        raw_json_str = f.read()
    data = json.loads(raw_json_str)

    # Échapper § en \u00a7 dans le bloc JSON embarqué pour que check_liens.py
    # et decaler_chapitres.py n'interprètent pas les logs git historiques
    # comme des références actives de chapitres du codex. JSON.parse restitue §.
    safe_json_str = raw_json_str.replace("§", "\\u00a7")

    total_commits = data["total_commits"]
    n_spheres = data["totals"]["spheres"]
    n_branches = data["totals"]["branches"]
    n_milestones = data["totals"]["milestones"]
    n_bridges = data["totals"]["bridges"]

    start = data["date_range"]["start"]
    end = data["date_range"]["end"]
    sy, sm, sd = start.split("-")
    ey, em, ed = end.split("-")
    range_short = f"{int(sd)} {MOIS_SHORT[sm]} {sy} – {int(ed)} {MOIS_SHORT[em]} {ey}"
    range_long = f"{int(sd)} {MOIS_FR[sm]} {sy} au {int(ed)} {MOIS_FR[em]} {ey}"

    html_template = f'''<!DOCTYPE html>
<html lang="fr" data-theme="dark">
<head>
    <meta charset="UTF-8">
    <!-- Thème : application précoce (anti-flash) -->
    <script src="js/theme-init.js"></script>
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Killtime — Évolution Globale du Projet & Constellation des 6 Sphères</title>
    <meta name="description" content="Cartographie temporelle, florale et synaptique de tout l'écosystème Killtime ({total_commits} commits, {n_spheres} sphères : Narratif, Système RP, Killtime Tactics, Site Global, Atlas Cosmique, Pilotage).">

    <!-- Typographies Killtime -->
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link href="https://fonts.googleapis.com/css2?family=Cinzel:wght@600;700;800;900&family=JetBrains+Mono:wght@400;500;600;700&family=Plus+Jakarta+Sans:wght@300;400;500;600;700;800&display=swap" rel="stylesheet">

    <style>
        :root {{
            --bg-void: #030712;
            --bg-canvas: #050a17;
            --bg-surface: rgba(11, 17, 32, 0.85);
            --bg-surface-elevated: rgba(16, 25, 46, 0.94);
            --bg-surface-hover: rgba(28, 43, 75, 0.92);
            --border-subtle: rgba(255, 255, 255, 0.09);
            --border-glow: rgba(56, 189, 248, 0.38);

            --text-primary: #f8fafc;
            --text-secondary: #94a3b8;
            --text-muted: #64748b;
            --text-accent: #38bdf8;
            --text-gold: #fbbf24;

            /* Couleurs des 6 Sphères */
            --sphere-narratif: #f59e0b;
            --sphere-sysrp: #38bdf8;
            --sphere-tactics: #c084fc;
            --sphere-site: #10b981;
            --sphere-atlas: #f43f5e;
            --sphere-pilotage: #f97316;

            --font-title: 'Cinzel', serif;
            --font-body: 'Plus Jakarta Sans', sans-serif;
            --font-mono: 'JetBrains Mono', monospace;

            --ease-out: cubic-bezier(0.16, 1, 0.3, 1);
        }}

        * {{
            box-sizing: border-box;
            margin: 0;
            padding: 0;
            user-select: none;
            -webkit-font-smoothing: antialiased;
        }}

        body, html {{
            width: 100%;
            height: 100%;
            overflow: hidden;
            background-color: var(--bg-void);
            font-family: var(--font-body);
            color: var(--text-primary);
        }}

        #app-root {{
            position: relative;
            width: 100vw;
            height: 100vh;
            overflow: hidden;
            display: flex;
            flex-direction: column;
        }}

        /* ================== TOPBAR ================== */
        .topbar {{
            height: 62px;
            background: color-mix(in srgb, var(--bg-surface) 88%, transparent);
            backdrop-filter: blur(20px);
            -webkit-backdrop-filter: blur(20px);
            border-bottom: 1px solid var(--border-subtle);
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 0 18px;
            z-index: 60;
            flex-shrink: 0;
            gap: 12px;
        }}

        .topbar-left {{
            display: flex;
            align-items: center;
            gap: 14px;
            min-width: 0;
        }}

        .brand {{
            display: flex;
            align-items: center;
            gap: 11px;
            text-decoration: none;
            color: var(--text-primary);
            flex-shrink: 0;
        }}

        .brand-icon {{
            width: 36px;
            height: 36px;
            border-radius: 10px;
            background: linear-gradient(135deg, rgba(245, 158, 11, 0.25), rgba(56, 189, 248, 0.25), rgba(192, 132, 252, 0.25));
            border: 1px solid rgba(56, 189, 248, 0.45);
            display: flex;
            align-items: center;
            justify-content: center;
            box-shadow: 0 0 16px rgba(56, 189, 248, 0.22);
            color: #38bdf8;
        }}

        .brand-info h1 {{
            font-family: var(--font-title);
            font-size: 15px;
            letter-spacing: 0.04em;
            display: flex;
            align-items: center;
            gap: 8px;
            color: var(--text-primary);
            white-space: nowrap;
        }}

        .brand-badge {{
            font-family: var(--font-mono);
            font-size: 10px;
            padding: 2px 8px;
            border-radius: 999px;
            background: linear-gradient(135deg, rgba(245, 158, 11, 0.2), rgba(56, 189, 248, 0.2));
            border: 1px solid rgba(251, 191, 36, 0.45);
            color: #fbbf24;
            text-transform: uppercase;
            letter-spacing: 0.07em;
        }}

        .breadcrumbs {{
            display: flex;
            align-items: center;
            gap: 7px;
            font-size: 12px;
            color: var(--text-muted);
            border-left: 1px solid var(--border-subtle);
            padding-left: 14px;
            white-space: nowrap;
        }}

        .breadcrumbs a {{
            color: var(--text-secondary);
            text-decoration: none;
            transition: color 0.18s ease;
        }}
        .breadcrumbs a:hover {{
            color: var(--text-accent);
        }}
        .breadcrumbs .current {{
            color: var(--text-primary);
            font-weight: 600;
        }}

        .topbar-center {{
            display: flex;
            align-items: center;
            gap: 10px;
        }}

        .metrics-hud {{
            display: flex;
            align-items: center;
            gap: 12px;
            background: color-mix(in srgb, var(--text-primary) 5%, transparent);
            border: 1px solid var(--border-subtle);
            padding: 5px 14px;
            border-radius: 999px;
            font-size: 11.5px;
            color: var(--text-secondary);
            white-space: nowrap;
        }}

        .metric-item {{
            display: flex;
            align-items: center;
            gap: 4px;
        }}
        .metric-val {{
            color: var(--text-accent);
            font-weight: 700;
            font-family: var(--font-mono);
        }}

        .topbar-right {{
            display: flex;
            align-items: center;
            gap: 7px;
            flex-shrink: 0;
        }}

        .btn-ghost {{
            background: color-mix(in srgb, var(--text-primary) 6%, transparent);
            border: 1px solid var(--border-subtle);
            color: var(--text-secondary);
            padding: 6px 11px;
            border-radius: 8px;
            font-size: 11.5px;
            font-weight: 600;
            cursor: pointer;
            display: inline-flex;
            align-items: center;
            gap: 5px;
            transition: var(--ease-out) 0.2s;
            text-decoration: none;
            white-space: nowrap;
        }}

        .btn-ghost:hover {{
            background: color-mix(in srgb, var(--text-primary) 13%, transparent);
            color: var(--text-primary);
            border-color: var(--border-glow);
        }}

        .btn-ghost.highlight {{
            background: linear-gradient(135deg, rgba(56, 189, 248, 0.16), rgba(192, 132, 252, 0.16));
            border-color: rgba(56, 189, 248, 0.42);
            color: #f8fafc;
        }}

        .sound-toggle.active {{
            background: rgba(56, 189, 248, 0.15);
            border-color: rgba(56, 189, 248, 0.4);
            color: #38bdf8;
        }}

        /* ================== CANVAS WRAPPER ================== */
        #canvas-wrapper {{
            position: relative;
            flex: 1;
            width: 100%;
            height: calc(100% - 62px);
            overflow: hidden;
            background: radial-gradient(circle at 50% 48%, #091328 0%, #040814 65%, #02050c 100%);
        }}

        #neural-canvas {{
            position: absolute;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            display: block;
            cursor: grab;
        }}
        #neural-canvas:active {{
            cursor: grabbing;
        }}

        /* ================== FLOATING HUD PANELS ================== */
        /* Left Stack : View Modes + Layer Toggles */
        .hud-left-stack {{
            position: absolute;
            top: 14px;
            left: 16px;
            display: flex;
            flex-direction: column;
            gap: 8px;
            z-index: 25;
            pointer-events: none;
        }}

        .hud-view-modes, .hud-layers-bar {{
            pointer-events: auto;
            display: flex;
            flex-wrap: wrap;
            background: var(--bg-surface);
            backdrop-filter: blur(16px);
            -webkit-backdrop-filter: blur(16px);
            border: 1px solid var(--border-subtle);
            border-radius: 12px;
            padding: 4px;
            gap: 3px;
            box-shadow: 0 10px 28px rgba(0, 0, 0, 0.42);
        }}

        .mode-btn, .layer-btn {{
            background: transparent;
            border: 1px solid transparent;
            color: var(--text-secondary);
            padding: 6px 10px;
            border-radius: 8px;
            font-size: 11.5px;
            font-weight: 600;
            cursor: pointer;
            display: inline-flex;
            align-items: center;
            gap: 5px;
            transition: all 0.18s ease;
            white-space: nowrap;
        }}

        .mode-btn:hover, .layer-btn:hover {{
            color: var(--text-primary);
            background: rgba(255, 255, 255, 0.06);
        }}

        .mode-btn.active {{
            background: linear-gradient(135deg, rgba(56, 189, 248, 0.22), rgba(192, 132, 252, 0.22));
            color: var(--text-primary);
            border-color: rgba(56, 189, 248, 0.45);
            box-shadow: 0 0 12px rgba(56, 189, 248, 0.22);
        }}

        .layer-btn {{
            font-size: 11px;
            padding: 4px 9px;
            color: var(--text-muted);
        }}

        .layer-btn.active {{
            background: rgba(255, 255, 255, 0.08);
            color: var(--text-primary);
            border-color: rgba(255, 255, 255, 0.16);
        }}

        .layer-count {{
            font-family: var(--font-mono);
            font-size: 9.5px;
            padding: 1px 5px;
            border-radius: 999px;
            background: rgba(255, 255, 255, 0.07);
            color: var(--text-secondary);
        }}

        /* Center Stack : Spheres Dock + Sub-Branches Bar */
        .hud-center-dock {{
            position: absolute;
            top: 14px;
            left: 50%;
            transform: translateX(-50%);
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 6px;
            z-index: 25;
            max-width: 58vw;
            pointer-events: none;
        }}

        .spheres-bar {{
            pointer-events: auto;
            display: flex;
            align-items: center;
            gap: 5px;
            background: var(--bg-surface);
            backdrop-filter: blur(18px);
            -webkit-backdrop-filter: blur(18px);
            border: 1px solid var(--border-subtle);
            border-radius: 14px;
            padding: 5px 8px;
            box-shadow: 0 12px 32px rgba(0, 0, 0, 0.48);
            flex-wrap: wrap;
            justify-content: center;
        }}

        .sphere-pill {{
            background: rgba(255, 255, 255, 0.02);
            border: 1px solid transparent;
            color: var(--text-muted);
            padding: 5px 9px;
            border-radius: 10px;
            font-size: 11.5px;
            font-weight: 700;
            cursor: pointer;
            display: inline-flex;
            align-items: center;
            gap: 6px;
            transition: all 0.2s ease;
            white-space: nowrap;
            position: relative;
        }}

        .sphere-pill:hover {{
            color: var(--text-primary);
            background: rgba(255, 255, 255, 0.07);
        }}

        .sphere-pill.active {{
            background: rgba(255, 255, 255, 0.08);
            color: var(--text-primary);
            border-color: var(--sp-color, rgba(255,255,255,0.28));
            box-shadow: inset 0 0 12px color-mix(in srgb, var(--sp-color, #38bdf8) 18%, transparent);
        }}

        .sphere-pill.focused {{
            background: color-mix(in srgb, var(--sp-color, #38bdf8) 24%, rgba(15, 23, 42, 0.9));
            border-color: var(--sp-color, #38bdf8);
            box-shadow: 0 0 16px color-mix(in srgb, var(--sp-color, #38bdf8) 45%, transparent);
        }}

        .sphere-dot {{
            width: 8px;
            height: 8px;
            border-radius: 50%;
            display: inline-block;
            flex-shrink: 0;
        }}

        .sphere-count {{
            font-family: var(--font-mono);
            font-size: 10px;
            padding: 1px 5px;
            border-radius: 6px;
            background: rgba(0, 0, 0, 0.35);
            color: var(--text-secondary);
        }}

        .sphere-focus-btn {{
            font-size: 10px;
            opacity: 0.55;
            padding: 1px 4px;
            border-radius: 4px;
            transition: opacity 0.15s, background 0.15s;
        }}
        .sphere-pill:hover .sphere-focus-btn {{
            opacity: 1;
            background: rgba(255, 255, 255, 0.12);
        }}

        /* Sub-branches bar */
        .subbranches-bar {{
            pointer-events: auto;
            display: none;
            align-items: center;
            flex-wrap: wrap;
            justify-content: center;
            gap: 4px;
            background: rgba(11, 17, 32, 0.9);
            backdrop-filter: blur(16px);
            border: 1px solid var(--border-subtle);
            border-radius: 12px;
            padding: 5px 10px;
            box-shadow: 0 10px 26px rgba(0, 0, 0, 0.45);
            max-width: 56vw;
        }}
        .subbranches-bar.visible {{
            display: flex;
        }}

        .branch-chip {{
            background: transparent;
            border: 1px solid transparent;
            color: var(--text-muted);
            padding: 3px 8px;
            border-radius: 999px;
            font-size: 10.5px;
            font-weight: 600;
            cursor: pointer;
            display: inline-flex;
            align-items: center;
            gap: 4px;
            transition: all 0.15s ease;
        }}
        .branch-chip:hover {{
            color: var(--text-primary);
            background: rgba(255, 255, 255, 0.06);
        }}
        .branch-chip.active {{
            background: rgba(255, 255, 255, 0.07);
            color: var(--text-primary);
            border-color: rgba(255, 255, 255, 0.18);
        }}

        /* Search Bar (Top Right) */
        .hud-search-filter {{
            position: absolute;
            top: 14px;
            right: 16px;
            display: flex;
            align-items: center;
            gap: 8px;
            z-index: 25;
        }}

        .search-box {{
            position: relative;
            background: var(--bg-surface);
            backdrop-filter: blur(16px);
            -webkit-backdrop-filter: blur(16px);
            border: 1px solid var(--border-subtle);
            border-radius: 12px;
            display: flex;
            align-items: center;
            padding: 5px 12px;
            width: 230px;
            transition: all 0.25s ease;
            box-shadow: 0 10px 28px rgba(0, 0, 0, 0.4);
        }}

        .search-box:focus-within {{
            width: 295px;
            border-color: rgba(56, 189, 248, 0.55);
            box-shadow: 0 0 18px rgba(56, 189, 248, 0.25);
        }}

        .search-input {{
            background: transparent;
            border: none;
            outline: none;
            color: var(--text-primary);
            font-size: 12.5px;
            margin-left: 8px;
            width: 100%;
            font-family: var(--font-body);
        }}
        .search-input::placeholder {{
            color: var(--text-muted);
        }}

        .search-match-badge {{
            font-family: var(--font-mono);
            font-size: 10px;
            color: #fbbf24;
            background: rgba(251, 191, 36, 0.15);
            padding: 1px 6px;
            border-radius: 999px;
            display: none;
            white-space: nowrap;
        }}

        /* Camera Controls (Floating Bottom Right) */
        .camera-controls {{
            position: absolute;
            right: 16px;
            bottom: 148px;
            display: flex;
            flex-direction: column;
            gap: 6px;
            z-index: 25;
        }}

        .cam-btn {{
            width: 38px;
            height: 38px;
            background: var(--bg-surface);
            backdrop-filter: blur(16px);
            border: 1px solid var(--border-subtle);
            border-radius: 10px;
            color: var(--text-secondary);
            display: flex;
            align-items: center;
            justify-content: center;
            cursor: pointer;
            font-size: 15px;
            transition: all 0.2s ease;
            box-shadow: 0 6px 16px rgba(0, 0, 0, 0.35);
        }}
        .cam-btn:hover {{
            background: var(--bg-surface-elevated);
            color: var(--text-primary);
            border-color: rgba(56, 189, 248, 0.45);
        }}

        /* ================== BOTTOM TIMELINE & SPECTRUM HUD ================== */
        .bottom-timeline-panel {{
            position: absolute;
            bottom: 14px;
            left: 16px;
            right: 16px;
            background: var(--bg-surface);
            backdrop-filter: blur(22px);
            -webkit-backdrop-filter: blur(22px);
            border: 1px solid var(--border-subtle);
            border-radius: 16px;
            padding: 10px 18px 12px;
            display: flex;
            flex-direction: column;
            gap: 8px;
            z-index: 30;
            box-shadow: 0 16px 40px rgba(0, 0, 0, 0.55);
        }}

        .timeline-upper {{
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 12px;
            flex-wrap: wrap;
        }}

        .player-controls {{
            display: flex;
            align-items: center;
            gap: 8px;
        }}

        .btn-play-pause {{
            width: 38px;
            height: 38px;
            border-radius: 10px;
            border: 1px solid rgba(56, 189, 248, 0.5);
            background: linear-gradient(135deg, rgba(56, 189, 248, 0.32), rgba(192, 132, 252, 0.32));
            color: #fff;
            display: flex;
            align-items: center;
            justify-content: center;
            cursor: pointer;
            box-shadow: 0 0 15px rgba(56, 189, 248, 0.3);
            transition: all 0.2s ease;
        }}
        .btn-play-pause:hover {{
            transform: scale(1.05);
            border-color: #38bdf8;
            box-shadow: 0 0 20px rgba(56, 189, 248, 0.5);
        }}

        .speed-selector {{
            display: flex;
            background: rgba(255, 255, 255, 0.05);
            border-radius: 8px;
            padding: 2px;
            border: 1px solid var(--border-subtle);
        }}

        .speed-btn {{
            background: transparent;
            border: none;
            color: var(--text-muted);
            font-size: 11px;
            font-family: var(--font-mono);
            font-weight: 700;
            padding: 4px 7px;
            border-radius: 6px;
            cursor: pointer;
            transition: all 0.15s ease;
        }}
        .speed-btn:hover {{
            color: var(--text-primary);
        }}
        .speed-btn.active {{
            background: rgba(56, 189, 248, 0.25);
            color: #38bdf8;
        }}

        .timeline-status {{
            display: flex;
            align-items: center;
            gap: 12px;
            font-size: 12.5px;
        }}

        .current-date-badge {{
            font-family: var(--font-mono);
            font-size: 12.5px;
            color: #38bdf8;
            font-weight: 700;
            background: rgba(56, 189, 248, 0.1);
            border: 1px solid rgba(56, 189, 248, 0.3);
            padding: 3px 10px;
            border-radius: 8px;
        }}

        .active-epoch-badge {{
            font-size: 12px;
            color: var(--text-primary);
            font-weight: 600;
            display: flex;
            align-items: center;
            gap: 6px;
        }}

        .timeline-actions {{
            display: flex;
            align-items: center;
            gap: 6px;
        }}

        /* Spectre Temporel Multi-Sphères + Scrubber */
        .spectrum-slider-box {{
            position: relative;
            width: 100%;
            display: flex;
            flex-direction: column;
            gap: 3px;
        }}

        #timeline-spectrum {{
            width: 100%;
            height: 24px;
            border-radius: 6px;
            background: rgba(3, 7, 18, 0.65);
            border: 1px solid rgba(255, 255, 255, 0.06);
            cursor: pointer;
            display: block;
        }}

        .chrono-slider {{
            -webkit-appearance: none;
            width: 100%;
            height: 6px;
            border-radius: 999px;
            background: var(--border-subtle);
            outline: none;
            cursor: pointer;
        }}

        .chrono-slider::-webkit-slider-thumb {{
            -webkit-appearance: none;
            appearance: none;
            width: 16px;
            height: 16px;
            border-radius: 50%;
            background: #38bdf8;
            border: 2px solid #ffffff;
            cursor: pointer;
            box-shadow: 0 0 12px rgba(56, 189, 248, 0.85);
            transition: transform 0.15s ease;
        }}
        .chrono-slider::-webkit-slider-thumb:hover {{
            transform: scale(1.25);
        }}

        .epoch-markers-bar {{
            display: flex;
            justify-content: space-between;
            gap: 6px;
            padding: 0 2px;
        }}

        .epoch-mark {{
            flex: 1;
            font-size: 10.5px;
            color: var(--text-muted);
            cursor: pointer;
            font-weight: 600;
            padding: 3px 8px;
            border-radius: 6px;
            background: rgba(255, 255, 255, 0.02);
            border-top: 2px solid transparent;
            text-align: center;
            transition: all 0.2s;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }}
        .epoch-mark:hover {{
            color: var(--text-primary);
            background: rgba(255, 255, 255, 0.06);
        }}
        .epoch-mark.active {{
            color: var(--text-primary);
            font-weight: 700;
            background: rgba(56, 189, 248, 0.09);
            border-top-color: var(--ep-color, #38bdf8);
        }}

        /* ================== INSPECTOR DRAWER (RIGHT PANEL) ================== */
        #inspector-drawer {{
            position: absolute;
            top: 66px;
            right: -450px;
            width: 425px;
            bottom: 142px;
            background: var(--bg-surface-elevated);
            backdrop-filter: blur(24px);
            -webkit-backdrop-filter: blur(24px);
            border: 1px solid var(--border-subtle);
            border-radius: 18px;
            padding: 20px;
            display: flex;
            flex-direction: column;
            gap: 13px;
            z-index: 55;
            box-shadow: -15px 0 40px rgba(0, 0, 0, 0.65);
            transition: right 0.35s var(--ease-out);
            overflow-y: auto;
        }}

        #inspector-drawer.open {{
            right: 16px;
        }}

        .drawer-header {{
            display: flex;
            align-items: flex-start;
            justify-content: space-between;
            border-bottom: 1px solid var(--border-subtle);
            padding-bottom: 12px;
            gap: 10px;
        }}

        .drawer-badges-row {{
            display: flex;
            flex-wrap: wrap;
            gap: 6px;
            margin-bottom: 6px;
        }}

        .drawer-sphere-tag, .drawer-branch-tag {{
            display: inline-flex;
            align-items: center;
            gap: 5px;
            font-size: 10.5px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.06em;
            padding: 3px 8px;
            border-radius: 6px;
        }}

        .drawer-commit-num {{
            font-family: var(--font-mono);
            font-size: 11.5px;
            color: var(--text-muted);
            margin-bottom: 3px;
        }}

        .drawer-title {{
            font-family: var(--font-title);
            font-size: 16.5px;
            color: var(--text-primary);
            line-height: 1.35;
        }}

        .drawer-close-btn {{
            background: var(--bg-surface);
            border: 1px solid var(--border-subtle);
            color: var(--text-secondary);
            width: 30px;
            height: 30px;
            border-radius: 8px;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: all 0.2s ease;
            flex-shrink: 0;
        }}
        .drawer-close-btn:hover {{
            color: var(--text-primary);
            background: var(--bg-surface-hover);
        }}

        /* Intra-sphere step navigation */
        .drawer-sphere-nav {{
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 8px;
        }}
        .drawer-nav-btn {{
            flex: 1;
            background: rgba(255, 255, 255, 0.04);
            border: 1px solid var(--border-subtle);
            color: var(--text-secondary);
            padding: 6px 10px;
            border-radius: 8px;
            font-size: 11px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.18s ease;
            text-align: center;
        }}
        .drawer-nav-btn:hover:not(:disabled) {{
            background: rgba(56, 189, 248, 0.14);
            color: var(--text-primary);
            border-color: rgba(56, 189, 248, 0.4);
        }}
        .drawer-nav-btn:disabled {{
            opacity: 0.35;
            cursor: default;
        }}

        .drawer-milestone-banner {{
            display: none;
            font-size: 12px;
            font-weight: 700;
            color: #fef08a;
            background: linear-gradient(135deg, rgba(251, 191, 36, 0.16), rgba(245, 158, 11, 0.08));
            border: 1px solid rgba(251, 191, 36, 0.5);
            padding: 8px 12px;
            border-radius: 10px;
        }}

        /* Multi-sphere breakdown bar */
        .drawer-spheres-breakdown {{
            background: var(--bg-canvas);
            border: 1px solid var(--border-subtle);
            border-radius: 10px;
            padding: 10px 12px;
            display: flex;
            flex-direction: column;
            gap: 6px;
        }}

        .breakdown-bar {{
            height: 7px;
            width: 100%;
            border-radius: 999px;
            overflow: hidden;
            display: flex;
            background: rgba(255, 255, 255, 0.05);
        }}

        .breakdown-legend {{
            display: flex;
            flex-wrap: wrap;
            gap: 8px;
            font-size: 11px;
            color: var(--text-secondary);
        }}

        .drawer-meta-grid {{
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 8px;
            background: var(--bg-canvas);
            padding: 10px 12px;
            border-radius: 11px;
            border: 1px solid var(--border-subtle);
            font-size: 11.5px;
        }}

        .meta-field label {{
            color: var(--text-muted);
            display: block;
            margin-bottom: 2px;
            font-size: 9.5px;
            text-transform: uppercase;
        }}
        .meta-field span {{
            color: var(--text-primary);
            font-weight: 600;
            font-family: var(--font-mono);
        }}

        .drawer-summary {{
            font-size: 12.5px;
            line-height: 1.55;
            color: var(--text-secondary);
            background: var(--bg-canvas);
            padding: 11px 12px;
            border-radius: 10px;
            border-left: 3px solid #38bdf8;
        }}

        .drawer-section-title {{
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.08em;
            color: var(--text-secondary);
            margin-bottom: 6px;
            display: flex;
            align-items: center;
            justify-content: space-between;
        }}

        .files-list {{
            list-style: none;
            display: flex;
            flex-direction: column;
            gap: 5px;
            max-height: 185px;
            overflow-y: auto;
            padding-right: 4px;
        }}

        .file-item {{
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 8px;
            font-size: 11.5px;
            font-family: var(--font-mono);
            background: rgba(255, 255, 255, 0.03);
            border: 1px solid var(--border-subtle);
            border-radius: 7px;
            padding: 5px 9px;
        }}

        .file-badge {{
            font-size: 9px;
            padding: 2px 6px;
            border-radius: 4px;
            font-weight: 700;
            text-transform: uppercase;
            flex-shrink: 0;
        }}
        .badge-csharp {{ background: rgba(192, 132, 252, 0.2); color: #c084fc; }}
        .badge-markdown {{ background: rgba(56, 189, 248, 0.2); color: #38bdf8; }}
        .badge-json {{ background: rgba(251, 191, 36, 0.2); color: #fbbf24; }}
        .badge-html {{ background: rgba(16, 185, 129, 0.2); color: #34d399; }}
        .badge-js {{ background: rgba(234, 179, 8, 0.2); color: #fde047; }}
        .badge-css {{ background: rgba(244, 63, 94, 0.2); color: #fb7185; }}
        .badge-python {{ background: rgba(249, 115, 22, 0.2); color: #fb923c; }}
        .badge-shader {{ background: rgba(236, 72, 153, 0.2); color: #f472b6; }}
        .badge-manuscript {{ background: rgba(245, 158, 11, 0.2); color: #fbbf24; }}
        .badge-asset {{ background: rgba(45, 212, 191, 0.2); color: #2dd4bf; }}
        .badge-other {{ background: rgba(148, 163, 184, 0.2); color: #94a3b8; }}

        .causal-links-group {{
            display: flex;
            flex-direction: column;
            gap: 5px;
            max-height: 170px;
            overflow-y: auto;
        }}

        .causal-jump-btn {{
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 8px;
            background: rgba(255, 255, 255, 0.04);
            border: 1px solid var(--border-subtle);
            color: var(--text-secondary);
            padding: 7px 10px;
            border-radius: 8px;
            font-size: 11.5px;
            cursor: pointer;
            transition: all 0.18s ease;
            text-align: left;
        }}
        .causal-jump-btn:hover {{
            background: rgba(56, 189, 248, 0.14);
            color: var(--text-primary);
            border-color: rgba(56, 189, 248, 0.4);
        }}

        .btn-github-link {{
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 8px;
            background: linear-gradient(135deg, rgba(255, 255, 255, 0.08), rgba(255, 255, 255, 0.02));
            border: 1px solid var(--border-subtle);
            color: var(--text-primary);
            text-decoration: none;
            padding: 9px;
            border-radius: 10px;
            font-weight: 700;
            font-size: 12px;
            transition: all 0.2s ease;
            margin-top: auto;
        }}
        .btn-github-link:hover {{
            border-color: rgba(56, 189, 248, 0.5);
            background: rgba(56, 189, 248, 0.15);
        }}

        /* Tooltip on Canvas */
        #node-tooltip {{
            position: absolute;
            pointer-events: none;
            background: rgba(8, 13, 26, 0.94);
            backdrop-filter: blur(12px);
            border: 1px solid rgba(56, 189, 248, 0.45);
            border-radius: 10px;
            padding: 8px 13px;
            color: #fff;
            font-size: 12px;
            z-index: 100;
            box-shadow: 0 10px 28px rgba(0, 0, 0, 0.6);
            display: none;
            transform: translate(-50%, -118%);
            max-width: 360px;
        }}
        #node-tooltip .tt-sphere {{
            font-size: 10px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.06em;
            margin-bottom: 2px;
        }}
        #node-tooltip .tt-title {{
            font-weight: 700;
            color: #f8fafc;
            margin-bottom: 3px;
            line-height: 1.3;
        }}
        #node-tooltip .tt-sub {{
            color: var(--text-secondary);
            font-size: 10.5px;
            font-family: var(--font-mono);
        }}

        /* ================== MODALS (PANORAMA SPHÈRES & GUIDE) ================== */
        .modal-backdrop {{
            position: fixed;
            inset: 0;
            background: rgba(2, 6, 16, 0.76);
            backdrop-filter: blur(10px);
            display: none;
            align-items: center;
            justify-content: center;
            z-index: 200;
            padding: 24px;
        }}

        .modal-card {{
            width: min(1080px, 95vw);
            max-height: 88vh;
            overflow-y: auto;
            background: var(--bg-surface-elevated);
            border: 1px solid var(--border-glow);
            border-radius: 20px;
            padding: 26px;
            box-shadow: 0 24px 70px rgba(0, 0, 0, 0.85);
            display: flex;
            flex-direction: column;
            gap: 18px;
        }}

        .spheres-grid-modal {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(310px, 1fr));
            gap: 14px;
        }}

        .sphere-modal-card {{
            background: rgba(5, 10, 23, 0.85);
            border: 1px solid var(--border-subtle);
            border-top: 3px solid var(--sp-col, #38bdf8);
            border-radius: 14px;
            padding: 16px;
            display: flex;
            flex-direction: column;
            gap: 10px;
        }}

        .sphere-modal-header {{
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 8px;
        }}

        .sphere-modal-title {{
            font-family: var(--font-title);
            font-size: 15px;
            color: var(--text-primary);
            display: flex;
            align-items: center;
            gap: 8px;
        }}

        .sphere-modal-stats {{
            display: grid;
            grid-template-columns: repeat(3, 1fr);
            gap: 6px;
            background: rgba(255, 255, 255, 0.03);
            padding: 8px;
            border-radius: 8px;
            font-family: var(--font-mono);
            font-size: 11px;
            text-align: center;
        }}

        .sphere-modal-links {{
            display: flex;
            flex-wrap: wrap;
            gap: 6px;
            margin-top: auto;
        }}

        .sphere-link-pill {{
            font-size: 11px;
            font-weight: 600;
            color: var(--text-primary);
            background: rgba(255, 255, 255, 0.06);
            border: 1px solid var(--border-subtle);
            padding: 4px 9px;
            border-radius: 7px;
            text-decoration: none;
            transition: all 0.15s;
        }}
        .sphere-link-pill:hover {{
            border-color: var(--sp-col, #38bdf8);
            background: rgba(255, 255, 255, 0.12);
        }}

        @media (max-width: 1100px) {{
            .topbar-center {{ display: none; }}
            .hud-center-dock {{ max-width: 92vw; top: 62px; }}
        }}
        @media (max-width: 800px) {{
            .hud-center-dock {{ display: none; }}
            #inspector-drawer {{ width: 100%; right: -100%; bottom: 0; top: 62px; border-radius: 0; }}
            #inspector-drawer.open {{ right: 0; }}
        }}
    </style>
    <link rel="stylesheet" href="css/themes.css">
</head>
<body>

<div id="app-root">
    <!-- ================= TOPBAR ================= -->
    <header class="topbar">
        <div class="topbar-left">
            <a href="index.html" class="brand">
                <div class="brand-icon">
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="3" />
                        <circle cx="12" cy="12" r="9" stroke-dasharray="4 3" />
                        <path d="M12 2v2M12 20v2M2 12h2M20 12h2" />
                    </svg>
                </div>
                <div class="brand-info">
                    <h1>Killtime <span class="brand-badge">6 Sphères • Écosystème Complet</span></h1>
                </div>
            </a>
            <nav class="breadcrumbs">
                <a href="index.html">Codex</a>
                <span>/</span>
                <a href="Killtime/index.html">Univers</a>
                <span>/</span>
                <a href="roadmap.html">Roadmap</a>
                <span>/</span>
                <span class="current">Évolution Globale</span>
            </nav>
        </div>

        <div class="topbar-center">
            <div class="metrics-hud">
                <div class="metric-item">
                    <span>Commits:</span>
                    <span class="metric-val" id="metric-commits">{total_commits}</span>
                </div>
                <span>•</span>
                <div class="metric-item">
                    <span>Sphères:</span>
                    <span class="metric-val">{n_spheres}</span>
                </div>
                <span>•</span>
                <div class="metric-item">
                    <span>Branches:</span>
                    <span class="metric-val">{n_branches}</span>
                </div>
                <span>•</span>
                <div class="metric-item">
                    <span>Ponts:</span>
                    <span class="metric-val" style="color:#34d399">{n_bridges}</span>
                </div>
                <span>•</span>
                <div class="metric-item">
                    <span>Période:</span>
                    <span class="metric-val" style="color:#fbbf24">{range_short}</span>
                </div>
            </div>
        </div>

        <div class="topbar-right">
            <button class="btn-ghost highlight" id="spheresModalBtn" title="Panorama détaillé et statistiques des 6 sphères du projet">
                <span>📊</span>
                <span>Bilan des 6 Sphères</span>
            </button>
            <button class="btn-ghost sound-toggle active" id="audioToggleBtn" title="Activer / Couper la synthèse audio multi-sphères">
                <span id="audio-icon">🔊</span>
                <span>Audio</span>
            </button>
            <button class="btn-ghost" id="helpModalBtn" title="Guide des vues & des sphères">
                <span>ℹ️</span>
                <span>Guide</span>
            </button>
            <a href="tactics_evolution.html" class="btn-ghost" title="Voir la Fleur dédiée uniquement à Killtime Tactics">
                <span>🌺</span>
                <span>Fleur Tactics</span>
            </a>
            <a href="tactics_fleur_fichiers.html" class="btn-ghost" title="Voir la Fleur des Fichiers C#">
                <span>🌸</span>
                <span>Fleur Fichiers</span>
            </a>
            <a href="roadmap.html" class="btn-ghost" title="Retour au Kanban">
                <span>📋</span>
                <span>Roadmap</span>
            </a>
        </div>
    </header>

    <!-- ================= CANVAS WRAPPER ================= -->
    <div id="canvas-wrapper">
        <canvas id="neural-canvas"></canvas>

        <!-- Left Stack : View Modes + Layer Toggles -->
        <div class="hud-left-stack">
            <div class="hud-view-modes">
                <button class="mode-btn active" data-mode="galaxy" title="6 Nébuleuses de Sphères en orbite autour du Cœur Originel avec leurs corolles de sous-branches">
                    <span>🪐</span>
                    <span>Galaxie des Sphères</span>
                </button>
                <button class="mode-btn" data-mode="flower" title="Grande Fleur botanique unifiée avec secteurs de sphères et 17 pétales">
                    <span>🌺</span>
                    <span>Fleur Globale</span>
                </button>
                <button class="mode-btn" data-mode="stream" title="Fleuve chronologique à 6 couloirs parallèles par sphère et ponts verticaux">
                    <span>🌊</span>
                    <span>Fleuve des Sphères</span>
                </button>
                <button class="mode-btn" data-mode="neural" title="Cortex synaptique stellaire organisé par lobes de sphères">
                    <span>🧠</span>
                    <span>Cortex Synaptique</span>
                </button>
                <button class="mode-btn" data-mode="tree" title="Arbre phylogénétique stratifié par les 6 Grandes Époques">
                    <span>🌲</span>
                    <span>Arbre d'Époques</span>
                </button>
            </div>

            <div class="hud-layers-bar">
                <button class="layer-btn active" data-layer="territories" title="Afficher les halos, anneaux de jauge et noyaux des 6 Sphères">
                    <span>🪐</span>
                    <span>Territoires</span>
                </button>
                <button class="layer-btn active" data-layer="filiation" title="Vrilles organiques reliant les commits d'une même sous-branche">
                    <span>🌿</span>
                    <span>Vrilles</span>
                </button>
                <button class="layer-btn active" data-layer="bridges" title="Ponts inter-sphères reliant les commits qui synchronisent plusieurs sphères">
                    <span>🌈</span>
                    <span>Ponts Inter-Sphères</span>
                    <span class="layer-count">{n_bridges}</span>
                </button>
                <button class="layer-btn active" data-layer="synapses" title="Synapses causales et chaînes de jalons">
                    <span>⚡</span>
                    <span>Synapses</span>
                </button>
                <button class="layer-btn active" data-layer="milestones" title="Mettre en évidence les 50 jalons majeurs du projet">
                    <span>★</span>
                    <span>Jalons</span>
                    <span class="layer-count">{n_milestones}</span>
                </button>
            </div>
        </div>

        <!-- Center Stack : Spheres Selector + Sub-Branches -->
        <div class="hud-center-dock">
            <div class="spheres-bar" id="spheresBar">
                <!-- Généré dynamiquement -->
            </div>
            <div class="subbranches-bar" id="subbranchesBar">
                <!-- Généré dynamiquement -->
            </div>
        </div>

        <!-- Search Bar (Top Right) -->
        <div class="hud-search-filter">
            <div class="search-box">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" color="#64748b">
                    <circle cx="11" cy="11" r="8" />
                    <line x1="21" y1="21" x2="16.65" y2="16.65" />
                </svg>
                <input type="text" class="search-input" id="commitSearchInput" placeholder="Chercher livre, script, lore, hash..." autocomplete="off">
                <span class="search-match-badge" id="searchMatchBadge">0</span>
            </div>
        </div>

        <!-- Camera Controls (Floating Right) -->
        <div class="camera-controls">
            <button class="cam-btn" id="btnZoomIn" title="Zoom avant">+</button>
            <button class="cam-btn" id="btnZoomOut" title="Zoom arrière">−</button>
            <button class="cam-btn" id="btnResetView" title="Recentrer la galaxie complète">⌂</button>
            <button class="cam-btn" id="btnToggleBranchesBar" title="Afficher / Masquer le filtre des 17 sous-branches">🌿</button>
        </div>

        <!-- Tooltip -->
        <div id="node-tooltip">
            <div class="tt-sphere" id="tt-sphere">Sphère</div>
            <div class="tt-title" id="tt-title">Titre</div>
            <div class="tt-sub" id="tt-sub">Date & Hash</div>
        </div>

        <!-- ================= INSPECTOR DRAWER ================= -->
        <aside id="inspector-drawer">
            <div class="drawer-header">
                <div style="min-width:0;">
                    <div class="drawer-badges-row" id="drawerBadgesRow"></div>
                    <div class="drawer-commit-num" id="drawerCommitNum">COMMIT #1 • 7 Déc 2024</div>
                    <h2 class="drawer-title" id="drawerTitle">Titre du commit</h2>
                </div>
                <button class="drawer-close-btn" id="drawerCloseBtn" title="Fermer le volet">✕</button>
            </div>

            <div class="drawer-sphere-nav">
                <button class="drawer-nav-btn" id="btnPrevInSphere">◀ Préc. dans la sphère</button>
                <button class="drawer-nav-btn" id="btnNextInSphere">Suiv. dans la sphère ▶</button>
            </div>

            <div class="drawer-milestone-banner" id="drawerMilestoneBanner"></div>

            <div class="drawer-spheres-breakdown" id="drawerBreakdownBox">
                <div style="font-size:10.5px; font-weight:700; text-transform:uppercase; color:var(--text-muted); display:flex; justify-content:space-between;">
                    <span>Empreinte Multi-Sphères du Commit</span>
                    <span id="drawerBridgeLabel" style="color:#34d399;"></span>
                </div>
                <div class="breakdown-bar" id="drawerBreakdownBar"></div>
                <div class="breakdown-legend" id="drawerBreakdownLegend"></div>
            </div>

            <div class="drawer-meta-grid">
                <div class="meta-field">
                    <label>Auteur</label>
                    <span id="drawerAuthor">Alexis Lacasse</span>
                </div>
                <div class="meta-field">
                    <label>Hash Git</label>
                    <span id="drawerHash">a96b02a</span>
                </div>
                <div class="meta-field">
                    <label>Diff Lignes</label>
                    <span id="drawerDiff">+0 / -0</span>
                </div>
                <div class="meta-field">
                    <label>Fichiers</label>
                    <span id="drawerFilesCount">2 fichiers</span>
                </div>
            </div>

            <div class="drawer-summary" id="drawerSummary"></div>

            <div>
                <div class="drawer-section-title">
                    <span>Fichiers Modifiés & Sphères</span>
                    <span style="font-family: var(--font-mono); font-size: 10px;" id="drawerFileCategoryCount"></span>
                </div>
                <ul class="files-list" id="drawerFilesList"></ul>
            </div>

            <div id="drawerCausalSection">
                <div class="drawer-section-title">Ponts Inter-Sphères & Synapses</div>
                <div class="causal-links-group" id="drawerCausalList"></div>
            </div>

            <a href="#" target="_blank" rel="noopener" class="btn-github-link" id="drawerGithubBtn">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0024 12c0-6.63-5.37-12-12-12z"/>
                </svg>
                <span>Examiner le Commit sur GitHub ↗</span>
            </a>
        </aside>

        <!-- ================= BOTTOM TIMELINE & SPECTRUM ================= -->
        <div class="bottom-timeline-panel">
            <div class="timeline-upper">
                <div class="player-controls">
                    <button class="btn-play-pause" id="playPauseBtn" title="Lancer / Mettre en pause la croissance temporelle (Espace)">
                        <span id="play-icon">▶</span>
                    </button>
                    <button class="btn-ghost" id="btnStepBack" title="Reculer d'un commit (←)">⏮</button>
                    <button class="btn-ghost" id="btnStepForward" title="Avancer d'un commit (→)">⏭</button>
                    <button class="btn-ghost" id="btnPrevMilestone" title="Sauter au jalon majeur précédent">★◀</button>
                    <button class="btn-ghost" id="btnNextMilestone" title="Sauter au jalon majeur suivant">▶★</button>

                    <div class="speed-selector">
                        <button class="speed-btn" data-speed="0.5">0.5x</button>
                        <button class="speed-btn active" data-speed="1">1x</button>
                        <button class="speed-btn" data-speed="3">3x</button>
                        <button class="speed-btn" data-speed="8">8x</button>
                    </div>
                </div>

                <div class="timeline-status">
                    <div class="current-date-badge" id="hudCurrentDate">2024-12-07</div>
                    <div class="active-epoch-badge" id="hudActiveEpoch">
                        <span>Époque I : Genèse Littéraire</span>
                    </div>
                    <div style="font-family: var(--font-mono); font-size: 12px; color: var(--text-muted);">
                        <span id="hudActiveCommitIndex" style="color:var(--text-primary); font-weight:700;">{total_commits}</span> / {total_commits} commits
                    </div>
                </div>

                <div class="timeline-actions">
                    <button class="btn-ghost" id="btnRestartSeed" title="Recommencer depuis le commit #1 (Déc. 2024)">🌱 Graine #1</button>
                    <button class="btn-ghost" id="btnFullBloom" title="Déployer les {total_commits} commits">🌺 Tout Déployer</button>
                </div>
            </div>

            <div class="spectrum-slider-box">
                <canvas id="timeline-spectrum" title="Spectre temporel des 382 commits colorés par Sphère — Cliquez pour naviguer"></canvas>
                <input type="range" class="chrono-slider" id="timelineSlider" min="1" max="{total_commits}" value="{total_commits}" step="1">
            </div>

            <div class="epoch-markers-bar" id="epochMarkersBar"></div>
        </div>
    </div>
</div>

<!-- ================= MODALE PANORAMA DES 6 SPHÈRES ================= -->
<div class="modal-backdrop" id="spheres-modal">
    <div class="modal-card">
        <div style="display:flex; align-items:center; justify-content:space-between; border-bottom:1px solid var(--border-subtle); padding-bottom:14px;">
            <div>
                <h2 style="font-family:var(--font-title); font-size:20px; color:var(--text-primary); display:flex; align-items:center; gap:10px;">
                    <span>🪐</span>
                    <span>Architecture & Bilan des 6 Sphères du Projet Killtime</span>
                </h2>
                <p style="font-size:13px; color:var(--text-secondary); margin-top:4px;">
                    Du <strong>{range_long}</strong> • <strong>{total_commits} commits</strong> répartis en 6 grandes sphères interconnectées par <strong>{n_bridges} commits ponts</strong>.
                </p>
            </div>
            <button class="drawer-close-btn" id="spheresModalCloseBtn">✕</button>
        </div>

        <div class="spheres-grid-modal" id="spheresModalGrid">
            <!-- Généré dynamiquement -->
        </div>
    </div>
</div>

<!-- ================= MODALE GUIDE ================= -->
<div class="modal-backdrop" id="help-modal">
    <div class="modal-card" style="width:min(620px, 94vw);">
        <div style="display:flex; align-items:center; justify-content:space-between;">
            <h2 style="font-family:var(--font-title); color:#38bdf8; display:flex; align-items:center; gap:10px;">
                <span>🪐</span>
                <span>Guide de la Constellation Globale Killtime</span>
            </h2>
            <button class="drawer-close-btn" id="helpModalCloseBtn">✕</button>
        </div>
        <p style="font-size:13px; line-height:1.6; color:var(--text-secondary);">
            Cette page réunit l'évolution intégrale de tout le projet <strong>Killtime</strong> du <strong>{range_long}</strong> ({total_commits} commits) en séparant clairement ses <strong>6 grandes Sphères</strong> et leurs <strong>17 Sous-Branches</strong>.
        </p>
        <div style="background:var(--bg-canvas); border-radius:12px; padding:14px; border:1px solid var(--border-subtle); font-size:12.5px; display:flex; flex-direction:column; gap:9px;">
            <div>🪐 <strong>Galaxie des Sphères :</strong> Chaque sphère possède son propre noyau orbital, sa jauge circulaire de progression et ses pétales de sous-branches. Cliquez sur le cœur d'une sphère ou sur <strong>🎯</strong> dans la barre supérieure pour zoomer dessus.</div>
            <div>🌺 <strong>Fleur Globale :</strong> Grande corolle botanique divisée en 6 secteurs angulaires colorés par sphère.</div>
            <div>🌊 <strong>Fleuve des Sphères :</strong> 6 couloirs chronologiques parallèles (Déc. 2024 → Nov. 2026) reliés par des ponts verticaux lumineux lorsqu'un commit synchronise plusieurs sphères.</div>
            <div>🌈 <strong>Commits Ponts Multi-Sphères :</strong> Les commits qui touchent plusieurs sphères simultanément (ex. <em>Système RP ↔ Killtime Tactics</em>) portent un anneau bicolore et génèrent des arcs inter-sphères.</div>
            <div>📊 <strong>Spectre Temporel :</strong> L'histogramme coloré juste au-dessus du curseur temporel montre quelle sphère domine à chaque époque.</div>
            <div>⌨️ <strong>Raccourcis :</strong> <kbd style="background:var(--bg-surface);border:1px solid var(--border-subtle);padding:2px 6px;border-radius:4px;">Espace</kbd> Lecture/Pause, <kbd style="background:var(--bg-surface);border:1px solid var(--border-subtle);padding:2px 6px;border-radius:4px;">←</kbd>/<kbd style="background:var(--bg-surface);border:1px solid var(--border-subtle);padding:2px 6px;border-radius:4px;">→</kbd> Pas-à-pas, <kbd style="background:var(--bg-surface);border:1px solid var(--border-subtle);padding:2px 6px;border-radius:4px;">Échap</kbd> Réinitialiser le focus.</div>
        </div>
    </div>
</div>

<!-- DATASET EMBEDDED -->
<script id="evolution-data" type="application/json">
{safe_json_str}
</script>

<script>
(function() {{
    'use strict';

    // 1. CHARGEMENT DES DONNÉES
    let rawData;
    try {{
        rawData = JSON.parse(document.getElementById('evolution-data').textContent);
    }} catch (e) {{
        console.error("Erreur de parsing des données embarquées:", e);
        return;
    }}

    const {{ nodes, links, cross_links, bridge_links, spheres, branches, epochs }} = rawData;
    const TOTAL_COMMITS = nodes.length;
    const sphereKeys = Object.keys(spheres);
    const branchKeys = Object.keys(branches);

    // 2. ÉTAT GLOBAL DE L'APPLICATION
    const state = {{
        currentCommitIndex: TOTAL_COMMITS,
        isPlaying: false,
        playbackSpeed: 1.0,
        viewMode: 'galaxy', // 'galaxy', 'flower', 'stream', 'neural', 'tree'
        audioEnabled: true,
        selectedNode: null,
        hoveredNode: null,
        hoveredSphereHub: null,
        focusedSphere: null, // null ou sphereId
        activeSpheres: new Set(sphereKeys),
        activeBranches: new Set(branchKeys),
        searchQuery: '',
        matchedNodeIds: new Set(),
        layers: {{
            territories: true,
            filiation: true,
            bridges: true,
            synapses: true,
            milestones: true
        }},
        cam: {{
            x: 0,
            y: 0,
            targetX: 0,
            targetY: 0,
            zoom: 0.78,
            targetZoom: 0.78,
            isDragging: false,
            dragMoved: false,
            dragStartX: 0,
            dragStartY: 0,
            camStartX: 0,
            camStartY: 0
        }}
    }};

    // 3. SYNTHÈSE AUDIO MULTI-SPHÈRES (WEB AUDIO API)
    const AudioEngine = {{
        ctx: null,
        masterGain: null,
        init() {{
            if (this.ctx) return;
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            if (!AudioContext) return;
            this.ctx = new AudioContext();
            this.masterGain = this.ctx.createGain();
            this.masterGain.gain.setValueAtTime(0.16, this.ctx.currentTime);
            this.masterGain.connect(this.ctx.destination);
        }},
        resume() {{
            if (this.ctx && this.ctx.state === 'suspended') this.ctx.resume();
        }},
        playChime(sphereId) {{
            if (!state.audioEnabled) return;
            this.init();
            this.resume();
            if (!this.ctx) return;

            const now = this.ctx.currentTime;
            const sphereChords = {{
                narratif:    [392.00, 493.88, 587.33], // Sol Majeur (chaleureux)
                sysrp:       [523.25, 659.25, 783.99], // Do Majeur (cristallin)
                tactics:     [440.00, 554.37, 659.25], // La Majeur (héroïque)
                site_global: [349.23, 440.00, 523.25], // Fa Majeur (organique)
                atlas:       [587.33, 739.99, 880.00], // Ré Majeur (stellaire)
                pilotage:    [329.63, 415.30, 493.88]  // Mi Majeur (rythmique)
            }};

            const chord = sphereChords[sphereId] || [440, 554.37, 659.25];
            chord.forEach((f, i) => {{
                const osc = this.ctx.createOscillator();
                const gain = this.ctx.createGain();
                osc.type = i === 0 ? 'sine' : 'triangle';
                osc.frequency.setValueAtTime(f, now + i * 0.035);

                gain.gain.setValueAtTime(0.001, now);
                gain.gain.exponentialRampToValueAtTime(0.06 / chord.length, now + i * 0.035 + 0.02);
                gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.65 + i * 0.08);

                osc.connect(gain);
                gain.connect(this.masterGain);
                osc.start(now + i * 0.035);
                osc.stop(now + 0.72 + i * 0.08);
            }});
        }},
        playTick() {{
            if (!state.audioEnabled) return;
            this.init();
            this.resume();
            if (!this.ctx) return;

            const now = this.ctx.currentTime;
            const osc = this.ctx.createOscillator();
            const gain = this.ctx.createGain();
            osc.type = 'sine';
            osc.frequency.setValueAtTime(840, now);
            osc.frequency.exponentialRampToValueAtTime(220, now + 0.035);
            gain.gain.setValueAtTime(0.025, now);
            gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.035);
            osc.connect(gain);
            gain.connect(this.masterGain);
            osc.start(now);
            osc.stop(now + 0.04);
        }}
    }};

    // 4. PARTICULES & ONDES DE FLORAISON
    const particles = [];
    const shockwaves = [];

    class SynapticParticle {{
        constructor(p0, p1, color, lift = 0) {{
            this.p0 = p0;
            this.p1 = p1;
            this.color = color;
            this.lift = lift;
            this.progress = 0;
            this.speed = 0.010 + Math.random() * 0.014;
            this.size = 2.0 + Math.random() * 1.8;
        }}
        update() {{
            this.progress += this.speed;
            return this.progress < 1.0;
        }}
        draw(ctx) {{
            const t = this.progress;
            const midX = (this.p0.x + this.p1.x) * 0.5;
            const midY = (this.p0.y + this.p1.y) * 0.5 - this.lift;
            const inv = 1 - t;
            const x = inv * inv * this.p0.x + 2 * inv * t * midX + t * t * this.p1.x;
            const y = inv * inv * this.p0.y + 2 * inv * t * midY + t * t * this.p1.y;

            ctx.save();
            ctx.shadowColor = this.color;
            ctx.shadowBlur = 8;
            ctx.fillStyle = '#ffffff';
            ctx.beginPath();
            ctx.arc(x, y, this.size, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
        }}
    }}

    class BloomShockwave {{
        constructor(x, y, color) {{
            this.x = x;
            this.y = y;
            this.color = color;
            this.radius = 4;
            this.maxRadius = 48;
            this.alpha = 1.0;
        }}
        update() {{
            this.radius += 2.2;
            this.alpha = Math.max(0, 1.0 - (this.radius / this.maxRadius));
            return this.alpha > 0;
        }}
        draw(ctx) {{
            ctx.save();
            ctx.strokeStyle = this.color;
            ctx.globalAlpha = this.alpha;
            ctx.lineWidth = 2.0;
            ctx.shadowColor = this.color;
            ctx.shadowBlur = 10;
            ctx.beginPath();
            ctx.arc(this.x, this.y, this.radius, 0, Math.PI * 2);
            ctx.stroke();
            ctx.restore();
        }}
    }}

    const cosmicDust = [];
    for (let i = 0; i < 120; i++) {{
        cosmicDust.push({{
            x: (Math.random() - 0.5) * 3200,
            y: (Math.random() - 0.5) * 2600,
            size: Math.random() * 1.8 + 0.4,
            alpha: Math.random() * 0.55 + 0.15,
            twinkleSpeed: Math.random() * 0.025 + 0.008,
            phase: Math.random() * Math.PI * 2
        }});
    }}

    // 5. CALCUL DES 5 DISPOSITIONS SPATIALES (LAYOUTS MULTI-SPHÈRES)
    const layoutCoords = {{
        galaxy: {{}},
        flower: {{}},
        stream: {{}},
        neural: {{}},
        tree: {{}}
    }};

    // Centres des 6 Sphères pour chaque mode pertinent
    const sphereCenters = {{
        galaxy: {{}},
        flower: {{}},
        stream: {{}},
        neural: {{}},
        tree: {{}}
    }};

    function nodeRadius(node) {{
        const diff = (node.insertions || 0) + (node.deletions || 0);
        let r = 5.5 + Math.min(6.5, Math.log10(Math.max(10, diff)) * 1.45);
        if (node.milestone) r += 2.8;
        if (node.id === 1) r = 15;
        return r;
    }}

    function relaxLayout(coordMap, minGap = 5) {{
        const ids = nodes.map(n => n.id).filter(id => id !== 1);
        const R = {{}};
        ids.forEach(id => {{ R[id] = nodeRadius(nodes[id - 1]) + minGap; }});
        for (let iter = 0; iter < 55; iter++) {{
            for (let a = 0; a < ids.length; a++) {{
                const idA = ids[a];
                const pa = coordMap[idA];
                for (let b = a + 1; b < ids.length; b++) {{
                    const idB = ids[b];
                    const pb = coordMap[idB];
                    const dx = pb.x - pa.x;
                    const dy = pb.y - pa.y;
                    const minD = R[idA] + R[idB];
                    const d2 = dx * dx + dy * dy;
                    if (d2 < minD * minD && d2 > 0.01) {{
                        const d = Math.sqrt(d2);
                        const push = (minD - d) / d * 0.42;
                        pa.x -= dx * push;
                        pa.y -= dy * push;
                        pb.x += dx * push;
                        pb.y += dy * push;
                    }}
                }}
            }}
        }}
    }}

    function computeLayouts() {{
        const nodesBySphere = {{}};
        const nodesByBranch = {{}};
        sphereKeys.forEach(s => nodesBySphere[s] = []);
        branchKeys.forEach(b => nodesByBranch[b] = []);
        nodes.forEach(n => {{
            if (nodesBySphere[n.sphere]) nodesBySphere[n.sphere].push(n);
            if (nodesByBranch[n.branch]) nodesByBranch[n.branch].push(n);
        }});

        // --- MODE 1 : GALAXIE DES 6 SPHÈRES (Hexagone Orbital autour du Cœur #1) ---
        layoutCoords.galaxy[1] = {{ x: 0, y: 0 }};
        const galaxyRadius = 560;

        sphereKeys.forEach((spKey, sIdx) => {{
            const spAngle = (sIdx / sphereKeys.length) * Math.PI * 2 - Math.PI / 2;
            const cx = Math.cos(spAngle) * galaxyRadius;
            const cy = Math.sin(spAngle) * galaxyRadius;
            sphereCenters.galaxy[spKey] = {{ x: cx, y: cy, angle: spAngle, r: 235 }};

            // Sous-branches de cette sphère
            const spBranches = branchKeys.filter(b => branches[b].sphere === spKey);
            spBranches.forEach((bKey, bIdx) => {{
                const bNodes = nodesByBranch[bKey];
                const count = bNodes.length;
                // Éventail orienté vers l'extérieur de la galaxie
                const spreadSpan = Math.PI * 1.15;
                const subAngleBase = spAngle + ((bIdx - (spBranches.length - 1) / 2) / Math.max(1, spBranches.length)) * spreadSpan;

                bNodes.forEach((node, idx) => {{
                    if (node.id === 1) return;
                    const ratio = (idx + 1) / Math.max(count, 1);
                    const localR = 48 + Math.pow(ratio, 0.82) * 185;
                    const spiral = Math.sin(ratio * Math.PI) * 0.32 + idx * 0.045;
                    const a = subAngleBase + spiral;
                    layoutCoords.galaxy[node.id] = {{
                        x: cx + Math.cos(a) * localR,
                        y: cy + Math.sin(a) * localR
                    }};
                }});
            }});
        }});
        relaxLayout(layoutCoords.galaxy, 4);

        // --- MODE 2 : GRANDE FLEUR GLOBALE (Secteurs de Sphères & 17 Pétales) ---
        layoutCoords.flower[1] = {{ x: 0, y: 0 }};
        sphereKeys.forEach((spKey, sIdx) => {{
            const sectorStart = (sIdx / sphereKeys.length) * Math.PI * 2 - Math.PI / 2;
            const sectorSpan = (Math.PI * 2) / sphereKeys.length;
            const sectorMid = sectorStart + sectorSpan * 0.5;
            sphereCenters.flower[spKey] = {{
                x: Math.cos(sectorMid) * 420,
                y: Math.sin(sectorMid) * 420,
                startAngle: sectorStart + 0.05,
                endAngle: sectorStart + sectorSpan - 0.05,
                midAngle: sectorMid
            }};

            const spBranches = branchKeys.filter(b => branches[b].sphere === spKey);
            spBranches.forEach((bKey, bIdx) => {{
                const bNodes = nodesByBranch[bKey];
                const count = bNodes.length;
                const petalAngle = sectorStart + ((bIdx + 0.5) / spBranches.length) * sectorSpan;

                bNodes.forEach((node, idx) => {{
                    if (node.id === 1) return;
                    const ratio = Math.pow((idx + 1) / Math.max(count, 1), 0.85);
                    const r = 115 + ratio * 560;
                    const curl = Math.sin((idx / Math.max(count, 1)) * Math.PI) * 0.16 + idx * 0.012;
                    const angle = petalAngle + curl;
                    layoutCoords.flower[node.id] = {{
                        x: Math.cos(angle) * r,
                        y: Math.sin(angle) * r
                    }};
                }});
            }});
        }});
        relaxLayout(layoutCoords.flower, 4);

        // --- MODE 3 : FLEUVE DES SPHÈRES (Swimlanes Chronologiques Horizontales) ---
        const streamWidth = 1750;
        const streamLeft = -streamWidth / 2 + 90;
        const laneSpacing = 140;
        const topLaneY = -((sphereKeys.length - 1) * laneSpacing) / 2;

        sphereKeys.forEach((spKey, sIdx) => {{
            const laneY = topLaneY + sIdx * laneSpacing;
            sphereCenters.stream[spKey] = {{ x: streamLeft - 130, y: laneY, laneY }};
        }});

        nodes.forEach(node => {{
            const sIdx = sphereKeys.indexOf(node.sphere);
            const laneY = topLaneY + (sIdx >= 0 ? sIdx : 0) * laneSpacing;
            const spBranches = branchKeys.filter(b => branches[b].sphere === node.sphere);
            const bSubIdx = spBranches.indexOf(node.branch);
            const subOffset = (bSubIdx - (spBranches.length - 1) / 2) * 24;
            const t = (node.id - 1) / Math.max(1, TOTAL_COMMITS - 1);
            const x = streamLeft + t * (streamWidth - 120);
            const wave = Math.sin(node.id * 0.9) * 10;
            layoutCoords.stream[node.id] = {{
                x,
                y: laneY + subOffset + wave
            }};
        }});
        relaxLayout(layoutCoords.stream, 2.5);

        // --- MODE 4 : CORTEX SYNAPTIQUE (Lobes par Sphère) ---
        layoutCoords.neural[1] = {{ x: 0, y: 0 }};
        const lobeCoords = {{
            narratif:    {{ x: -460, y: -230 }},
            sysrp:       {{ x: 460,  y: -230 }},
            tactics:     {{ x: -480, y: 180  }},
            site_global: {{ x: 480,  y: 180  }},
            atlas:       {{ x: 0,    y: -390 }},
            pilotage:    {{ x: 0,    y: 370  }}
        }};
        sphereKeys.forEach(spKey => {{
            sphereCenters.neural[spKey] = lobeCoords[spKey] || {{ x: 0, y: 0 }};
        }});
        nodes.forEach(node => {{
            if (node.id === 1) return;
            const lobe = lobeCoords[node.sphere] || {{ x: 0, y: 0 }};
            const goldAngle = node.id * 2.39996;
            const r = 35 + (node.id % 19) * 9.5;
            layoutCoords.neural[node.id] = {{
                x: lobe.x + Math.cos(goldAngle) * r * 1.25,
                y: lobe.y + Math.sin(goldAngle) * r * 0.95
            }};
        }});
        relaxLayout(layoutCoords.neural, 4);

        // --- MODE 5 : ARBRE D'ÉPOQUES (Colonnes de Sphères × Hauteur Chronologique) ---
        const treeHeight = 1150;
        const startY = treeHeight / 2 - 40;
        const colSpacing = 245;

        sphereKeys.forEach((spKey, sIdx) => {{
            const colX = (sIdx - (sphereKeys.length - 1) / 2) * colSpacing;
            sphereCenters.tree[spKey] = {{ x: colX, y: startY + 75, colX }};
        }});

        nodes.forEach(node => {{
            const sIdx = sphereKeys.indexOf(node.sphere);
            const colX = (sIdx - (sphereKeys.length - 1) / 2) * colSpacing;
            const t = (node.id - 1) / Math.max(1, TOTAL_COMMITS - 1);
            const y = startY - t * treeHeight;
            const sway = Math.sin(node.id * 0.85) * 48;
            layoutCoords.tree[node.id] = {{
                x: colX + sway,
                y
            }};
        }});
        relaxLayout(layoutCoords.tree, 3);
    }}

    computeLayouts();

    // Positions courantes interpolées
    const currentPositions = {{}};
    nodes.forEach(n => {{
        const initPos = layoutCoords.galaxy[n.id] || {{ x: 0, y: 0 }};
        currentPositions[n.id] = {{
            x: initPos.x,
            y: initPos.y,
            scale: 1.0,
            hasBloomed: true,
            radius: nodeRadius(n)
        }};
    }});

    // 6. INITIALISATION CANVAS PRINCIPAL & SPECTRE TEMPOREL
    const canvas = document.getElementById('neural-canvas');
    const ctx = canvas.getContext('2d');
    const spectrumCanvas = document.getElementById('timeline-spectrum');
    const sCtx = spectrumCanvas.getContext('2d');
    let width = 0, height = 0;

    function resizeCanvas() {{
        const wrapper = document.getElementById('canvas-wrapper');
        const dpr = window.devicePixelRatio || 1;
        width = wrapper.clientWidth;
        height = wrapper.clientHeight;
        canvas.width = width * dpr;
        canvas.height = height * dpr;
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        const sRect = spectrumCanvas.getBoundingClientRect();
        spectrumCanvas.width = Math.max(300, sRect.width) * dpr;
        spectrumCanvas.height = 24 * dpr;
        sCtx.setTransform(dpr, 0, 0, dpr, 0, 0);
        drawTimelineSpectrum();
    }}
    window.addEventListener('resize', resizeCanvas);

    function drawTimelineSpectrum() {{
        const w = spectrumCanvas.clientWidth || 800;
        const h = 24;
        sCtx.clearRect(0, 0, w, h);

        const barW = Math.max(1.5, w / TOTAL_COMMITS);
        for (let i = 0; i < TOTAL_COMMITS; i++) {{
            const n = nodes[i];
            const isVis = (i + 1) <= state.currentCommitIndex && isNodeSphereActive(n);
            const spCol = spheres[n.sphere]?.color || '#38bdf8';
            const amp = Math.min(1, Math.log10(Math.max(10, n.insertions + n.deletions)) / 4.8);
            const barH = Math.max(4, amp * (h - 4));
            const x = (i / TOTAL_COMMITS) * w;

            sCtx.fillStyle = spCol;
            sCtx.globalAlpha = isVis ? (n.milestone ? 0.95 : 0.68) : 0.14;
            sCtx.fillRect(x, h - barH, Math.max(1.2, barW - 0.4), barH);
        }}

        // Curseur de position actuelle
        const curX = ((state.currentCommitIndex - 0.5) / TOTAL_COMMITS) * w;
        sCtx.globalAlpha = 1;
        sCtx.fillStyle = '#ffffff';
        sCtx.fillRect(curX - 1, 0, 2, h);
    }}

    spectrumCanvas.addEventListener('click', e => {{
        const rect = spectrumCanvas.getBoundingClientRect();
        const ratio = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
        const idx = Math.max(1, Math.min(TOTAL_COMMITS, Math.round(ratio * TOTAL_COMMITS)));
        updateTimelineUI(idx);
    }});

    // 7. FILTRES SPHÈRES & SOUS-BRANCHES
    function isNodeSphereActive(node) {{
        if (!state.activeSpheres.has(node.sphere)) return false;
        if (!state.activeBranches.has(node.branch)) return false;
        return true;
    }}

    const spheresBar = document.getElementById('spheresBar');
    const subbranchesBar = document.getElementById('subbranchesBar');

    function renderSpheresBar() {{
        spheresBar.innerHTML = '';

        // Bouton "Toutes les sphères"
        const allBtn = document.createElement('button');
        const allActive = state.activeSpheres.size === sphereKeys.length && !state.focusedSphere;
        allBtn.className = 'sphere-pill' + (allActive ? ' active' : '');
        allBtn.style.setProperty('--sp-color', '#38bdf8');
        allBtn.innerHTML = `<span>🌐</span><span>Toutes</span><span class="sphere-count">${{TOTAL_COMMITS}}</span>`;
        allBtn.title = "Afficher toutes les sphères et recentrer la galaxie";
        allBtn.addEventListener('click', () => {{
            state.focusedSphere = null;
            state.activeSpheres = new Set(sphereKeys);
            state.activeBranches = new Set(branchKeys);
            state.cam.targetX = 0;
            state.cam.targetY = 0;
            state.cam.targetZoom = state.viewMode === 'galaxy' ? 0.78 : 0.85;
            AudioEngine.playTick();
            renderSpheresBar();
            renderSubbranchesBar();
            drawTimelineSpectrum();
        }});
        spheresBar.appendChild(allBtn);

        sphereKeys.forEach(spKey => {{
            const sp = spheres[spKey];
            const isAct = state.activeSpheres.has(spKey);
            const isFoc = state.focusedSphere === spKey;
            const visibleCount = nodes.filter(n => n.sphere === spKey && n.id <= state.currentCommitIndex).length;

            const btn = document.createElement('button');
            btn.className = 'sphere-pill' + (isAct ? ' active' : '') + (isFoc ? ' focused' : '');
            btn.style.setProperty('--sp-color', sp.color);
            btn.title = `${{sp.label}} — Clic : activer/masquer • Clic sur 🎯 : isoler et zoomer sur cette sphère`;
            btn.innerHTML = `
                <span class="sphere-dot" style="background:${{sp.color}};box-shadow:0 0 8px ${{sp.color}}"></span>
                <span>${{sp.icon}} ${{sp.short_label}}</span>
                <span class="sphere-count" id="sp-count-${{spKey}}">${{visibleCount}}</span>
                <span class="sphere-focus-btn" data-focus="${{spKey}}" title="Isoler et centrer sur ${{sp.short_label}}">🎯</span>
            `;

            btn.addEventListener('click', e => {{
                const focusTarget = e.target.closest('[data-focus]');
                if (focusTarget) {{
                    e.stopPropagation();
                    focusOnSphere(spKey);
                    return;
                }}
                if (state.focusedSphere === spKey) {{
                    // Sortir du focus exclusif
                    state.focusedSphere = null;
                    state.activeSpheres = new Set(sphereKeys);
                }} else if (state.activeSpheres.has(spKey)) {{
                    if (state.activeSpheres.size > 1) state.activeSpheres.delete(spKey);
                }} else {{
                    state.activeSpheres.add(spKey);
                }}
                AudioEngine.playTick();
                renderSpheresBar();
                renderSubbranchesBar();
                drawTimelineSpectrum();
            }});

            btn.addEventListener('dblclick', () => focusOnSphere(spKey));
            spheresBar.appendChild(btn);
        }});
    }}

    function focusOnSphere(spKey) {{
        if (state.focusedSphere === spKey) {{
            // Toggle off focus -> show all
            state.focusedSphere = null;
            state.activeSpheres = new Set(sphereKeys);
            state.activeBranches = new Set(branchKeys);
            state.cam.targetX = 0;
            state.cam.targetY = 0;
            state.cam.targetZoom = state.viewMode === 'galaxy' ? 0.78 : 0.85;
        }} else {{
            state.focusedSphere = spKey;
            state.activeSpheres = new Set([spKey]);
            state.activeBranches = new Set(branchKeys.filter(b => branches[b].sphere === spKey));
            subbranchesBar.classList.add('visible');

            // Centrer la caméra sur le noyau de cette sphère
            const center = (sphereCenters[state.viewMode] && sphereCenters[state.viewMode][spKey]) || {{ x: 0, y: 0 }};
            const targetZ = state.viewMode === 'galaxy' ? 1.45 : 1.15;
            state.cam.targetZoom = targetZ;
            state.cam.targetX = -center.x * targetZ;
            state.cam.targetY = -center.y * targetZ;
            AudioEngine.playChime(spKey);
        }}
        renderSpheresBar();
        renderSubbranchesBar();
        drawTimelineSpectrum();
    }}

    function renderSubbranchesBar() {{
        subbranchesBar.innerHTML = '';
        const relevantBranches = branchKeys.filter(bKey => state.activeSpheres.has(branches[bKey].sphere));
        relevantBranches.forEach(bKey => {{
            const b = branches[bKey];
            const isAct = state.activeBranches.has(bKey);
            const chip = document.createElement('button');
            chip.className = 'branch-chip' + (isAct ? ' active' : '');
            chip.innerHTML = `<span class="sphere-dot" style="background:${{b.color}}"></span><span>${{b.icon}} ${{b.label}}</span>`;
            chip.addEventListener('click', () => {{
                if (state.activeBranches.has(bKey)) {{
                    if (state.activeBranches.size > 1) state.activeBranches.delete(bKey);
                }} else {{
                    state.activeBranches.add(bKey);
                }}
                AudioEngine.playTick();
                renderSubbranchesBar();
                drawTimelineSpectrum();
            }});
            subbranchesBar.appendChild(chip);
        }});
    }}

    document.getElementById('btnToggleBranchesBar').addEventListener('click', () => {{
        subbranchesBar.classList.toggle('visible');
        AudioEngine.playTick();
    }});

    // 8. INTERACTION SOURIS & CAMÉRA SUR LE CANVAS
    const tooltip = document.getElementById('node-tooltip');
    const ttSphere = document.getElementById('tt-sphere');
    const ttTitle = document.getElementById('tt-title');
    const ttSub = document.getElementById('tt-sub');

    function screenToWorld(sx, sy) {{
        return {{
            x: (sx - width / 2 - state.cam.x) / state.cam.zoom,
            y: (sy - height / 2 - state.cam.y) / state.cam.zoom
        }};
    }}

    function findNodeAtScreen(sx, sy) {{
        const {{ x, y }} = screenToWorld(sx, sy);
        for (let i = nodes.length - 1; i >= 0; i--) {{
            const n = nodes[i];
            if (n.id > state.currentCommitIndex) continue;
            if (!isNodeSphereActive(n)) continue;
            const pos = currentPositions[n.id];
            const dx = x - pos.x;
            const dy = y - pos.y;
            const hitR = (pos.radius || 8) + 6 / state.cam.zoom;
            if (dx * dx + dy * dy <= hitR * hitR) return n;
        }}
        return null;
    }}

    function findSphereHubAtScreen(sx, sy) {{
        if (state.viewMode !== 'galaxy') return null;
        const {{ x, y }} = screenToWorld(sx, sy);
        for (const spKey of sphereKeys) {{
            const c = sphereCenters.galaxy[spKey];
            if (!c) continue;
            const dx = x - c.x;
            const dy = y - c.y;
            if (dx * dx + dy * dy <= 34 * 34) return spKey;
        }}
        return null;
    }}

    canvas.addEventListener('mousedown', e => {{
        if (e.button !== 0) return;
        state.cam.isDragging = true;
        state.cam.dragMoved = false;
        state.cam.dragStartX = e.clientX;
        state.cam.dragStartY = e.clientY;
        state.cam.camStartX = state.cam.x;
        state.cam.camStartY = state.cam.y;
    }});

    window.addEventListener('mousemove', e => {{
        if (state.cam.isDragging) {{
            const dx = e.clientX - state.cam.dragStartX;
            const dy = e.clientY - state.cam.dragStartY;
            if (Math.abs(dx) > 3 || Math.abs(dy) > 3) state.cam.dragMoved = true;
            state.cam.targetX = state.cam.camStartX + dx;
            state.cam.targetY = state.cam.camStartY + dy;
            state.cam.x = state.cam.targetX;
            state.cam.y = state.cam.targetY;
            return;
        }}

        const rect = canvas.getBoundingClientRect();
        const sx = e.clientX - rect.left;
        const sy = e.clientY - rect.top;
        const hovered = findNodeAtScreen(sx, sy);
        const hub = hovered ? null : findSphereHubAtScreen(sx, sy);
        state.hoveredSphereHub = hub;

        if (hovered !== state.hoveredNode) {{
            state.hoveredNode = hovered;
            if (hovered) {{
                AudioEngine.playTick();
                const sp = spheres[hovered.sphere];
                const br = branches[hovered.branch];
                tooltip.style.display = 'block';
                tooltip.style.left = e.clientX + 'px';
                tooltip.style.top = e.clientY + 'px';
                ttSphere.textContent = `${{sp.icon}} ${{sp.short_label}} • ${{br.label}}`;
                ttSphere.style.color = sp.color;
                ttTitle.textContent = hovered.title;
                let sub = `#${{hovered.id}} • ${{hovered.date_display}} • ${{hovered.short}} (+${{hovered.insertions}}/-${{hovered.deletions}})`;
                if (hovered.is_bridge) sub += ` • 🌈 Pont (${{hovered.spheres_touched.length}} sphères)`;
                ttSub.textContent = sub;
                canvas.style.cursor = 'pointer';
            }} else if (hub) {{
                const sp = spheres[hub];
                tooltip.style.display = 'block';
                tooltip.style.left = e.clientX + 'px';
                tooltip.style.top = e.clientY + 'px';
                ttSphere.textContent = `NOYAU DE SPHÈRE`;
                ttSphere.style.color = sp.color;
                ttTitle.textContent = `${{sp.icon}} ${{sp.label}}`;
                ttSub.textContent = `Cliquez pour centrer & explorer cette sphère (${{sp.stats.primary_commits}} commits primaires)`;
                canvas.style.cursor = 'pointer';
            }} else {{
                tooltip.style.display = 'none';
                canvas.style.cursor = 'grab';
            }}
        }} else if (hovered || hub) {{
            tooltip.style.left = e.clientX + 'px';
            tooltip.style.top = e.clientY + 'px';
        }} else {{
            tooltip.style.display = 'none';
            canvas.style.cursor = 'grab';
        }}
    }});

    window.addEventListener('mouseup', () => {{
        state.cam.isDragging = false;
    }});

    canvas.addEventListener('click', e => {{
        if (state.cam.dragMoved) return;
        const rect = canvas.getBoundingClientRect();
        const sx = e.clientX - rect.left;
        const sy = e.clientY - rect.top;
        const clicked = findNodeAtScreen(sx, sy);
        if (clicked) {{
            selectNode(clicked);
            return;
        }}
        const hub = findSphereHubAtScreen(sx, sy);
        if (hub) {{
            focusOnSphere(hub);
        }}
    }});

    canvas.addEventListener('wheel', e => {{
        e.preventDefault();
        const zoomFactor = e.deltaY < 0 ? 1.14 : 0.88;
        state.cam.targetZoom = Math.max(0.22, Math.min(3.5, state.cam.targetZoom * zoomFactor));
    }}, {{ passive: false }});

    // 9. VOLET INSPECTEUR DE COMMIT MULTI-SPHÈRES
    const drawer = document.getElementById('inspector-drawer');
    const drawerBadgesRow = document.getElementById('drawerBadgesRow');
    const drawerCommitNum = document.getElementById('drawerCommitNum');
    const drawerTitle = document.getElementById('drawerTitle');
    const drawerMilestoneBanner = document.getElementById('drawerMilestoneBanner');
    const drawerBreakdownBar = document.getElementById('drawerBreakdownBar');
    const drawerBreakdownLegend = document.getElementById('drawerBreakdownLegend');
    const drawerBridgeLabel = document.getElementById('drawerBridgeLabel');
    const drawerAuthor = document.getElementById('drawerAuthor');
    const drawerHash = document.getElementById('drawerHash');
    const drawerDiff = document.getElementById('drawerDiff');
    const drawerFilesCount = document.getElementById('drawerFilesCount');
    const drawerSummary = document.getElementById('drawerSummary');
    const drawerFilesList = document.getElementById('drawerFilesList');
    const drawerFileCategoryCount = document.getElementById('drawerFileCategoryCount');
    const drawerCausalList = document.getElementById('drawerCausalList');
    const drawerGithubBtn = document.getElementById('drawerGithubBtn');
    const btnPrevInSphere = document.getElementById('btnPrevInSphere');
    const btnNextInSphere = document.getElementById('btnNextInSphere');

    function selectNode(node) {{
        state.selectedNode = node;
        if (node.id > state.currentCommitIndex) {{
            updateTimelineUI(node.id);
        }}
        AudioEngine.playChime(node.sphere);

        const pos = currentPositions[node.id];
        if (pos) {{
            state.cam.targetX = -pos.x * state.cam.targetZoom;
            state.cam.targetY = -pos.y * state.cam.targetZoom;
        }}

        const sp = spheres[node.sphere];
        const br = branches[node.branch];

        // Badges Sphère + Sous-branche + Sphères secondaires
        drawerBadgesRow.innerHTML = '';
        const spBadge = document.createElement('span');
        spBadge.className = 'drawer-sphere-tag';
        spBadge.style.cssText = `background:${{sp.color}}24; color:${{sp.color}}; border:1px solid ${{sp.color}}66;`;
        spBadge.textContent = `${{sp.icon}} ${{sp.short_label}}`;
        drawerBadgesRow.appendChild(spBadge);

        const brBadge = document.createElement('span');
        brBadge.className = 'drawer-branch-tag';
        brBadge.style.cssText = `background:rgba(255,255,255,0.06); color:#e2e8f0; border:1px solid rgba(255,255,255,0.15);`;
        brBadge.textContent = `${{br.icon}} ${{br.label}}`;
        drawerBadgesRow.appendChild(brBadge);

        const ep = epochs.find(e => e.id === node.epoch);
        drawerCommitNum.textContent = `COMMIT #${{node.id}} • ${{node.date_display}} • ${{ep ? ep.short_title : ''}}`;
        drawerTitle.textContent = node.title;

        // Navigation précédent/suivant dans la même sphère
        const sameSphereNodes = nodes.filter(n => n.sphere === node.sphere);
        const posInSphere = sameSphereNodes.findIndex(n => n.id === node.id);
        btnPrevInSphere.disabled = posInSphere <= 0;
        btnNextInSphere.disabled = posInSphere < 0 || posInSphere >= sameSphereNodes.length - 1;
        btnPrevInSphere.textContent = `◀ Préc. (${{sp.short_label}})`;
        btnNextInSphere.textContent = `Suiv. (${{sp.short_label}}) ▶`;
        btnPrevInSphere.onclick = () => {{ if (posInSphere > 0) selectNode(sameSphereNodes[posInSphere - 1]); }};
        btnNextInSphere.onclick = () => {{ if (posInSphere < sameSphereNodes.length - 1) selectNode(sameSphereNodes[posInSphere + 1]); }};

        // Bannière Jalon
        if (node.milestone) {{
            drawerMilestoneBanner.style.display = 'block';
            drawerMilestoneBanner.textContent = node.milestone;
        }} else {{
            drawerMilestoneBanner.style.display = 'none';
        }}

        // Barre de répartition multi-sphères
        drawerBreakdownBar.innerHTML = '';
        drawerBreakdownLegend.innerHTML = '';
        const weights = node.sphere_weights || {{ [node.sphere]: 1 }};
        const totalW = Object.values(weights).reduce((a, b) => a + b, 0) || 1;
        drawerBridgeLabel.textContent = node.is_bridge ? `🌈 Pont (${{node.spheres_touched.length}} sphères)` : 'Sphère pure';

        node.spheres_touched.forEach(sKey => {{
            const sInfo = spheres[sKey];
            if (!sInfo) return;
            const w = weights[sKey] || 1;
            const pct = Math.round((w / totalW) * 100);
            const seg = document.createElement('div');
            seg.style.cssText = `width:${{pct}}%; background:${{sInfo.color}}; height:100%;`;
            drawerBreakdownBar.appendChild(seg);

            const leg = document.createElement('span');
            leg.style.cssText = 'display:inline-flex; align-items:center; gap:4px;';
            leg.innerHTML = `<span class="sphere-dot" style="background:${{sInfo.color}}"></span><span>${{sInfo.short_label}} (${{pct}}%)</span>`;
            drawerBreakdownLegend.appendChild(leg);
        }});

        drawerAuthor.textContent = node.author;
        drawerHash.textContent = node.short;
        drawerDiff.textContent = `+${{node.insertions.toLocaleString('fr-FR')}} / -${{node.deletions.toLocaleString('fr-FR')}}`;
        drawerFilesCount.textContent = `${{node.files_count}} fichier(s)`;
        drawerSummary.textContent = node.summary;
        drawerSummary.style.borderLeftColor = sp.color;

        // Fichiers
        drawerFilesList.innerHTML = '';
        if (node.files && node.files.length > 0) {{
            drawerFileCategoryCount.textContent = `${{node.files.length}} affichés / ${{node.files_count}}`;
            node.files.forEach(f => {{
                const fSp = spheres[f.sphere] || sp;
                const li = document.createElement('li');
                li.className = 'file-item';
                li.innerHTML = `
                    <span style="display:flex;align-items:center;gap:6px;min-width:0;">
                        <span class="sphere-dot" style="background:${{fSp.color}}" title="${{fSp.short_label}}"></span>
                        <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="${{f.path}}">${{f.name}}</span>
                    </span>
                    <span class="file-badge badge-${{f.type}}">${{f.type}}</span>
                `;
                drawerFilesList.appendChild(li);
            }});
        }} else {{
            drawerFileCategoryCount.textContent = 'Métadonnées';
            drawerFilesList.innerHTML = '<li class="file-item" style="color:var(--text-muted)">Métadonnées de dépôt uniquement</li>';
        }}

        // Ponts inter-sphères & Synapses causales
        drawerCausalList.innerHTML = '';
        const rels = [];
        bridge_links.filter(bl => bl.source === node.id || bl.target === node.id).forEach(bl => {{
            const otherId = bl.source === node.id ? bl.target : bl.source;
            const other = nodes[otherId - 1];
            if (other) rels.push({{ icon: '🌈', other, reason: bl.reason }});
        }});
        cross_links.filter(cl => cl.source === node.id || cl.target === node.id).forEach(cl => {{
            const otherId = cl.source === node.id ? cl.target : cl.source;
            const other = nodes[otherId - 1];
            if (other) rels.push({{ icon: '⚡', other, reason: cl.reason }});
        }});

        if (rels.length > 0) {{
            rels.slice(0, 14).forEach(r => {{
                const otherSp = spheres[r.other.sphere];
                const btn = document.createElement('button');
                btn.className = 'causal-jump-btn';
                btn.innerHTML = `
                    <span style="min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">
                        ${{r.icon}} <strong style="color:${{otherSp.color}}">[${{otherSp.short_label}}]</strong> ${{r.other.title}}
                    </span>
                    <span style="font-family:var(--font-mono);font-size:10px;color:#38bdf8;flex-shrink:0;">#${{r.other.id}}</span>
                `;
                btn.addEventListener('click', () => selectNode(r.other));
                drawerCausalList.appendChild(btn);
            }});
        }} else {{
            drawerCausalList.innerHTML = '<div style="font-size:11.5px; color:var(--text-muted);">Filiation directe sur sa branche temporelle.</div>';
        }}

        drawerGithubBtn.href = `https://github.com/k0r0z1f/Killtime/commit/${{node.hash}}`;
        drawer.classList.add('open');
    }}

    document.getElementById('drawerCloseBtn').addEventListener('click', () => {{
        drawer.classList.remove('open');
        state.selectedNode = null;
    }});

    // 10. TIMELINE, ÉPOQUES & CONTRÔLES DE LECTURE
    const slider = document.getElementById('timelineSlider');
    const playPauseBtn = document.getElementById('playPauseBtn');
    const playIcon = document.getElementById('play-icon');
    const hudCurrentDate = document.getElementById('hudCurrentDate');
    const hudActiveEpoch = document.getElementById('hudActiveEpoch');
    const hudActiveCommitIndex = document.getElementById('hudActiveCommitIndex');
    const epochBar = document.getElementById('epochMarkersBar');

    function updateTimelineUI(commitIdx) {{
        commitIdx = Math.max(1, Math.min(TOTAL_COMMITS, commitIdx));
        state.currentCommitIndex = commitIdx;
        slider.value = commitIdx;
        hudActiveCommitIndex.textContent = commitIdx;

        const currNode = nodes[commitIdx - 1];
        if (currNode) {{
            hudCurrentDate.textContent = currNode.date_display;
            const ep = epochs.find(e => e.id === currNode.epoch);
            if (ep) {{
                hudActiveEpoch.textContent = ep.title;
                document.querySelectorAll('.epoch-mark').forEach(el => {{
                    el.classList.toggle('active', el.dataset.epoch === ep.id);
                }});
            }}
        }}

        // Mettre à jour les compteurs de sphères dans la barre
        sphereKeys.forEach(spKey => {{
            const cntEl = document.getElementById(`sp-count-${{spKey}}`);
            if (cntEl) {{
                cntEl.textContent = nodes.filter(n => n.sphere === spKey && n.id <= commitIdx).length;
            }}
        }});

        for (let i = 0; i < commitIdx; i++) {{
            const n = nodes[i];
            const p = currentPositions[n.id];
            if (!p.hasBloomed) {{
                p.hasBloomed = true;
                if (isNodeSphereActive(n)) {{
                    shockwaves.push(new BloomShockwave(p.x, p.y, spheres[n.sphere]?.color || '#38bdf8'));
                    if (state.isPlaying) AudioEngine.playChime(n.sphere);
                }}
            }}
        }}
        for (let i = commitIdx; i < nodes.length; i++) {{
            currentPositions[nodes[i].id].hasBloomed = false;
        }}

        drawTimelineSpectrum();
    }}

    slider.addEventListener('input', e => updateTimelineUI(parseInt(e.target.value, 10)));

    function togglePlay() {{
        state.isPlaying = !state.isPlaying;
        playIcon.textContent = state.isPlaying ? '❚❚' : '▶';
        if (state.isPlaying) {{
            AudioEngine.resume();
            if (state.currentCommitIndex >= TOTAL_COMMITS) updateTimelineUI(1);
        }}
    }}
    playPauseBtn.addEventListener('click', togglePlay);

    document.getElementById('btnStepForward').addEventListener('click', () => {{
        if (state.currentCommitIndex < TOTAL_COMMITS) updateTimelineUI(state.currentCommitIndex + 1);
    }});
    document.getElementById('btnStepBack').addEventListener('click', () => {{
        if (state.currentCommitIndex > 1) updateTimelineUI(state.currentCommitIndex - 1);
    }});
    document.getElementById('btnPrevMilestone').addEventListener('click', () => {{
        for (let i = state.currentCommitIndex - 2; i >= 0; i--) {{
            if (nodes[i].milestone && isNodeSphereActive(nodes[i])) {{
                updateTimelineUI(nodes[i].id);
                selectNode(nodes[i]);
                break;
            }}
        }}
    }});
    document.getElementById('btnNextMilestone').addEventListener('click', () => {{
        for (let i = state.currentCommitIndex; i < TOTAL_COMMITS; i++) {{
            if (nodes[i].milestone && isNodeSphereActive(nodes[i])) {{
                updateTimelineUI(nodes[i].id);
                selectNode(nodes[i]);
                break;
            }}
        }}
    }});
    document.getElementById('btnRestartSeed').addEventListener('click', () => {{
        state.isPlaying = false;
        playIcon.textContent = '▶';
        updateTimelineUI(1);
    }});
    document.getElementById('btnFullBloom').addEventListener('click', () => {{
        state.isPlaying = false;
        playIcon.textContent = '▶';
        updateTimelineUI(TOTAL_COMMITS);
    }});

    document.querySelectorAll('.speed-btn').forEach(btn => {{
        btn.addEventListener('click', () => {{
            document.querySelectorAll('.speed-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            state.playbackSpeed = parseFloat(btn.dataset.speed);
        }});
    }});

    // Époques
    epochs.forEach(ep => {{
        const span = document.createElement('span');
        span.className = 'epoch-mark';
        span.dataset.epoch = ep.id;
        span.style.setProperty('--ep-color', ep.color);
        span.textContent = ep.short_title;
        span.title = `${{ep.title}} (${{ep.start}} → ${{ep.end}})`;
        span.addEventListener('click', () => {{
            const lastInEpoch = [...nodes].reverse().find(n => n.epoch === ep.id);
            if (lastInEpoch) updateTimelineUI(lastInEpoch.id);
        }});
        epochBar.appendChild(span);
    }});

    // Sélecteurs de Mode de Vue
    document.querySelectorAll('.hud-view-modes .mode-btn').forEach(btn => {{
        btn.addEventListener('click', () => {{
            document.querySelectorAll('.hud-view-modes .mode-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            state.viewMode = btn.dataset.mode;
            state.cam.targetX = 0;
            state.cam.targetY = 0;
            state.cam.targetZoom = state.viewMode === 'stream' ? 0.68 : (state.viewMode === 'galaxy' ? 0.78 : 0.82);
            AudioEngine.playTick();
        }});
    }});

    // Interrupteurs de Couches Visuelles
    document.querySelectorAll('.layer-btn').forEach(btn => {{
        btn.addEventListener('click', () => {{
            const k = btn.dataset.layer;
            state.layers[k] = !state.layers[k];
            btn.classList.toggle('active', state.layers[k]);
            AudioEngine.playTick();
        }});
    }});

    // Recherche en direct
    const searchInput = document.getElementById('commitSearchInput');
    const searchMatchBadge = document.getElementById('searchMatchBadge');
    searchInput.addEventListener('input', e => {{
        const q = e.target.value.toLowerCase().trim();
        state.searchQuery = q;
        state.matchedNodeIds.clear();
        if (q.length > 0) {{
            nodes.forEach(n => {{
                const mTitle = n.title.toLowerCase().includes(q);
                const mSum = n.summary.toLowerCase().includes(q);
                const mHash = n.short.toLowerCase().includes(q);
                const mSphere = spheres[n.sphere].label.toLowerCase().includes(q);
                const mFiles = n.files.some(f => f.name.toLowerCase().includes(q) || f.path.toLowerCase().includes(q));
                if (mTitle || mSum || mHash || mSphere || mFiles) state.matchedNodeIds.add(n.id);
            }});
            searchMatchBadge.style.display = 'inline-block';
            searchMatchBadge.textContent = `${{state.matchedNodeIds.size}}`;
        }} else {{
            searchMatchBadge.style.display = 'none';
        }}
    }});

    // Caméra boutons
    document.getElementById('btnZoomIn').addEventListener('click', () => {{
        state.cam.targetZoom = Math.min(3.5, state.cam.targetZoom * 1.28);
    }});
    document.getElementById('btnZoomOut').addEventListener('click', () => {{
        state.cam.targetZoom = Math.max(0.22, state.cam.targetZoom * 0.78);
    }});
    document.getElementById('btnResetView').addEventListener('click', () => {{
        state.focusedSphere = null;
        state.activeSpheres = new Set(sphereKeys);
        state.activeBranches = new Set(branchKeys);
        state.cam.targetX = 0;
        state.cam.targetY = 0;
        state.cam.targetZoom = state.viewMode === 'galaxy' ? 0.78 : 0.82;
        renderSpheresBar();
        renderSubbranchesBar();
    }});

    // Audio Toggle
    const audioToggleBtn = document.getElementById('audioToggleBtn');
    const audioIcon = document.getElementById('audio-icon');
    audioToggleBtn.addEventListener('click', () => {{
        state.audioEnabled = !state.audioEnabled;
        audioToggleBtn.classList.toggle('active', state.audioEnabled);
        audioIcon.textContent = state.audioEnabled ? '🔊' : '🔇';
    }});

    // Modales (Panorama des 6 Sphères & Guide)
    const spheresModal = document.getElementById('spheres-modal');
    const spheresModalGrid = document.getElementById('spheresModalGrid');

    function populateSpheresModal() {{
        spheresModalGrid.innerHTML = '';
        sphereKeys.forEach(spKey => {{
            const sp = spheres[spKey];
            const st = sp.stats;
            const spBr = branchKeys.filter(b => branches[b].sphere === spKey).map(b => `${{branches[b].icon}} ${{branches[b].label}}`).join(' • ');
            const linksHtml = (sp.links || []).map(l => `<a href="${{l.href}}" class="sphere-link-pill">${{l.icon}} ${{l.label}} ↗</a>`).join('');

            const card = document.createElement('div');
            card.className = 'sphere-modal-card';
            card.style.setProperty('--sp-col', sp.color);
            card.innerHTML = `
                <div class="sphere-modal-header">
                    <div class="sphere-modal-title">
                        <span>${{sp.icon}}</span>
                        <span>${{sp.label}}</span>
                    </div>
                    <button class="btn-ghost" data-explore-sphere="${{spKey}}" style="padding:4px 9px;font-size:11px;border-color:${{sp.color}}66;color:${{sp.color}};">
                        🎯 Explorer
                    </button>
                </div>
                <div style="font-size:11.5px; color:var(--text-secondary); line-height:1.5;">${{sp.desc}}</div>
                <div class="sphere-modal-stats">
                    <div><div style="color:${{sp.color}};font-weight:700;font-size:14px;">${{st.primary_commits}}</div><div style="color:var(--text-muted);font-size:9.5px;">PRIMAIRES</div></div>
                    <div><div style="color:#f8fafc;font-weight:700;font-size:14px;">${{st.total_commits_touching}}</div><div style="color:var(--text-muted);font-size:9.5px;">AVEC PONTS</div></div>
                    <div><div style="color:#fbbf24;font-weight:700;font-size:14px;">★ ${{st.milestones_count}}</div><div style="color:var(--text-muted);font-size:9.5px;">JALONS</div></div>
                </div>
                <div style="font-size:11px; color:var(--text-muted);"><strong>Sous-branches :</strong> ${{spBr}}</div>
                <div class="sphere-modal-links">${{linksHtml}}</div>
            `;
            card.querySelector('[data-explore-sphere]').addEventListener('click', () => {{
                spheresModal.style.display = 'none';
                focusOnSphere(spKey);
            }});
            spheresModalGrid.appendChild(card);
        }});
    }}
    populateSpheresModal();

    document.getElementById('spheresModalBtn').addEventListener('click', () => spheresModal.style.display = 'flex');
    document.getElementById('spheresModalCloseBtn').addEventListener('click', () => spheresModal.style.display = 'none');
    spheresModal.addEventListener('click', e => {{ if (e.target === spheresModal) spheresModal.style.display = 'none'; }});

    const helpModal = document.getElementById('help-modal');
    document.getElementById('helpModalBtn').addEventListener('click', () => helpModal.style.display = 'flex');
    document.getElementById('helpModalCloseBtn').addEventListener('click', () => helpModal.style.display = 'none');
    helpModal.addEventListener('click', e => {{ if (e.target === helpModal) helpModal.style.display = 'none'; }});

    window.addEventListener('keydown', e => {{
        if (e.target.tagName === 'INPUT') return;
        if (e.code === 'Space') {{
            e.preventDefault();
            togglePlay();
        }} else if (e.code === 'ArrowRight') {{
            e.preventDefault();
            if (state.currentCommitIndex < TOTAL_COMMITS) updateTimelineUI(state.currentCommitIndex + 1);
        }} else if (e.code === 'ArrowLeft') {{
            e.preventDefault();
            if (state.currentCommitIndex > 1) updateTimelineUI(state.currentCommitIndex - 1);
        }} else if (e.code === 'Escape') {{
            spheresModal.style.display = 'none';
            helpModal.style.display = 'none';
            if (state.focusedSphere) {{
                state.focusedSphere = null;
                state.activeSpheres = new Set(sphereKeys);
                state.activeBranches = new Set(branchKeys);
                renderSpheresBar();
                renderSubbranchesBar();
            }}
        }}
    }});

    // 11. BOUCLE DE RENDU CANVAS PRINCIPALE
    let lastTime = performance.now();
    let playAccumulator = 0;

    function drawTerritories(ctx, now) {{
        if (!state.layers.territories) return;

        if (state.viewMode === 'galaxy') {{
            // Anneau orbital global reliant les 6 noyaux de sphères
            ctx.save();
            ctx.strokeStyle = 'rgba(148, 163, 184, 0.12)';
            ctx.lineWidth = 1.5;
            ctx.setLineDash([8, 8]);
            ctx.beginPath();
            ctx.arc(0, 0, 560, 0, Math.PI * 2);
            ctx.stroke();
            ctx.restore();

            sphereKeys.forEach(spKey => {{
                if (!state.activeSpheres.has(spKey)) return;
                const sp = spheres[spKey];
                const c = sphereCenters.galaxy[spKey];
                if (!c) return;

                const totalSp = sp.stats.primary_commits || 1;
                const activeSp = nodes.filter(n => n.sphere === spKey && n.id <= state.currentCommitIndex).length;
                const progressRatio = activeSp / totalSp;
                const isHoveredHub = state.hoveredSphereHub === spKey;

                ctx.save();
                // 1. Nébuleuse de territoire
                const grad = ctx.createRadialGradient(c.x, c.y, 15, c.x, c.y, c.r + 35);
                grad.addColorStop(0, sp.color + '28');
                grad.addColorStop(0.65, sp.color + '0d');
                grad.addColorStop(1, 'transparent');
                ctx.fillStyle = grad;
                ctx.beginPath();
                ctx.arc(c.x, c.y, c.r + 35, 0, Math.PI * 2);
                ctx.fill();

                // 2. Anneau frontière de la sphère
                ctx.strokeStyle = sp.color + '33';
                ctx.lineWidth = 1.2;
                ctx.setLineDash([5, 6]);
                ctx.beginPath();
                ctx.arc(c.x, c.y, c.r, 0, Math.PI * 2);
                ctx.stroke();
                ctx.setLineDash([]);

                // 3. Rayon énergétique depuis le Cœur Originel (0,0) vers le noyau de la sphère
                ctx.strokeStyle = sp.color + '38';
                ctx.lineWidth = 2;
                ctx.beginPath();
                ctx.moveTo(0, 0);
                ctx.lineTo(c.x, c.y);
                ctx.stroke();

                // 4. Noyau central de la sphère + Jauge circulaire de progression
                const hubR = isHoveredHub ? 32 : 27;
                ctx.fillStyle = '#070d1d';
                ctx.strokeStyle = sp.color + '66';
                ctx.lineWidth = 2;
                ctx.beginPath();
                ctx.arc(c.x, c.y, hubR, 0, Math.PI * 2);
                ctx.fill();
                ctx.stroke();

                // Arc de progression temporelle de la sphère
                if (progressRatio > 0) {{
                    ctx.strokeStyle = sp.color;
                    ctx.shadowColor = sp.color;
                    ctx.shadowBlur = 12;
                    ctx.lineWidth = 3.5;
                    ctx.beginPath();
                    ctx.arc(c.x, c.y, hubR + 4, -Math.PI / 2, -Math.PI / 2 + progressRatio * Math.PI * 2);
                    ctx.stroke();
                    ctx.shadowBlur = 0;
                }}

                // Icône & Titre de la Sphère
                ctx.font = '18px sans-serif';
                ctx.textAlign = 'center';
                ctx.textBaseline = 'middle';
                ctx.fillText(sp.icon, c.x, c.y);

                ctx.font = '700 13px "Cinzel", serif';
                ctx.fillStyle = sp.color;
                ctx.fillText(sp.label.toUpperCase(), c.x, c.y - c.r - 18);

                ctx.font = '600 11px "JetBrains Mono", monospace';
                ctx.fillStyle = '#cbd5e1';
                ctx.fillText(`${{activeSp}} / ${{totalSp}} commits • ${{sp.subtitle}}`, c.x, c.y - c.r - 2);
                ctx.restore();
            }});
        }} else if (state.viewMode === 'flower') {{
            sphereKeys.forEach(spKey => {{
                if (!state.activeSpheres.has(spKey)) return;
                const sp = spheres[spKey];
                const sec = sphereCenters.flower[spKey];
                if (!sec) return;

                ctx.save();
                // Secteur angulaire doux
                ctx.fillStyle = sp.color + '0a';
                ctx.strokeStyle = sp.color + '26';
                ctx.lineWidth = 1;
                ctx.beginPath();
                ctx.moveTo(0, 0);
                ctx.arc(0, 0, 710, sec.startAngle, sec.endAngle);
                ctx.closePath();
                ctx.fill();
                ctx.stroke();

                // Arc extérieur nominatif
                ctx.strokeStyle = sp.color;
                ctx.lineWidth = 3;
                ctx.beginPath();
                ctx.arc(0, 0, 715, sec.startAngle + 0.04, sec.endAngle - 0.04);
                ctx.stroke();

                const lx = Math.cos(sec.midAngle) * 755;
                const ly = Math.sin(sec.midAngle) * 755;
                ctx.font = '700 13px "Cinzel", serif';
                ctx.fillStyle = sp.color;
                ctx.textAlign = 'center';
                ctx.fillText(`${{sp.icon}} ${{sp.short_label.toUpperCase()}}`, lx, ly);
                ctx.restore();
            }});
        }} else if (state.viewMode === 'stream') {{
            const streamLeft = -1750 / 2 + 90;
            const streamRight = streamLeft + 1630;
            sphereKeys.forEach(spKey => {{
                if (!state.activeSpheres.has(spKey)) return;
                const sp = spheres[spKey];
                const sc = sphereCenters.stream[spKey];
                if (!sc) return;

                ctx.save();
                // Bande horizontale du couloir
                ctx.fillStyle = sp.color + '09';
                ctx.strokeStyle = sp.color + '28';
                ctx.lineWidth = 1;
                ctx.beginPath();
                ctx.roundRect(streamLeft - 190, sc.laneY - 56, 1860, 112, 14);
                ctx.fill();
                ctx.stroke();

                // Ligne centrale de couloir
                ctx.strokeStyle = sp.color + '44';
                ctx.lineWidth = 1.5;
                ctx.setLineDash([4, 6]);
                ctx.beginPath();
                ctx.moveTo(streamLeft, sc.laneY);
                ctx.lineTo(streamRight, sc.laneY);
                ctx.stroke();
                ctx.setLineDash([]);

                // Label de couloir à gauche
                ctx.font = '700 13px "Cinzel", serif';
                ctx.fillStyle = sp.color;
                ctx.textAlign = 'left';
                ctx.fillText(`${{sp.icon}} ${{sp.short_label}}`, streamLeft - 175, sc.laneY - 6);
                ctx.font = '500 10.5px "JetBrains Mono", monospace';
                ctx.fillStyle = '#94a3b8';
                const activeSp = nodes.filter(n => n.sphere === spKey && n.id <= state.currentCommitIndex).length;
                ctx.fillText(`${{activeSp}} / ${{sp.stats.primary_commits}} commits`, streamLeft - 175, sc.laneY + 14);
                ctx.restore();
            }});
        }} else if (state.viewMode === 'neural' || state.viewMode === 'tree') {{
            sphereKeys.forEach(spKey => {{
                if (!state.activeSpheres.has(spKey)) return;
                const sp = spheres[spKey];
                const sc = sphereCenters[state.viewMode][spKey];
                if (!sc) return;

                ctx.save();
                ctx.font = '700 13px "Cinzel", serif';
                ctx.fillStyle = sp.color;
                ctx.textAlign = 'center';
                if (state.viewMode === 'neural') {{
                    const grad = ctx.createRadialGradient(sc.x, sc.y, 10, sc.x, sc.y, 210);
                    grad.addColorStop(0, sp.color + '22');
                    grad.addColorStop(1, 'transparent');
                    ctx.fillStyle = grad;
                    ctx.beginPath();
                    ctx.arc(sc.x, sc.y, 210, 0, Math.PI * 2);
                    ctx.fill();
                    ctx.fillStyle = sp.color;
                    ctx.fillText(`${{sp.icon}} ${{sp.short_label.toUpperCase()}}`, sc.x, sc.y - 185);
                }} else {{
                    ctx.fillText(`${{sp.icon}} ${{sp.short_label.toUpperCase()}}`, sc.x, sc.y);
                }}
                ctx.restore();
            }});
        }}
    }}

    function renderLoop(now) {{
        const dt = (now - lastTime) / 1000;
        lastTime = now;

        if (state.isPlaying) {{
            playAccumulator += dt * state.playbackSpeed;
            if (playAccumulator >= 0.16) {{
                playAccumulator = 0;
                if (state.currentCommitIndex < TOTAL_COMMITS) {{
                    updateTimelineUI(state.currentCommitIndex + 1);
                }} else {{
                    state.isPlaying = false;
                    playIcon.textContent = '▶';
                }}
            }}
        }}

        // Inertie caméra
        state.cam.x += (state.cam.targetX - state.cam.x) * 0.12;
        state.cam.y += (state.cam.targetY - state.cam.y) * 0.12;
        state.cam.zoom += (state.cam.targetZoom - state.cam.zoom) * 0.12;

        // Interpolation des positions de nœuds
        const targetLayout = layoutCoords[state.viewMode] || layoutCoords.galaxy;
        const breath = Math.sin(now * 0.0018) * 3.5;

        nodes.forEach(n => {{
            const target = targetLayout[n.id] || {{ x: 0, y: 0 }};
            const curr = currentPositions[n.id];
            const sway = Math.sin(now * 0.0014 + n.id * 0.35) * 2.5;
            const destX = target.x + (n.id === 1 ? 0 : sway);
            const destY = target.y + (n.id === 1 ? 0 : breath * 0.4);

            curr.x += (destX - curr.x) * 0.10;
            curr.y += (destY - curr.y) * 0.10;

            const isVis = n.id <= state.currentCommitIndex && isNodeSphereActive(n);
            const targetScale = isVis ? 1.0 : 0.0;
            curr.scale += (targetScale - curr.scale) * 0.16;
        }});

        // Génération de particules sur les vrilles et sur les ponts inter-sphères
        if (Math.random() < 0.22 && state.currentCommitIndex > 1) {{
            if (state.layers.bridges && Math.random() < 0.45) {{
                const activeBridges = bridge_links.filter(b =>
                    b.target <= state.currentCommitIndex &&
                    state.activeSpheres.has(b.from_sphere) &&
                    state.activeSpheres.has(b.to_sphere)
                );
                if (activeBridges.length > 0) {{
                    const rb = activeBridges[Math.floor(Math.random() * activeBridges.length)];
                    particles.push(new SynapticParticle(
                        currentPositions[rb.source],
                        currentPositions[rb.target],
                        spheres[rb.to_sphere]?.color || '#38bdf8',
                        40
                    ));
                }}
            }} else if (state.layers.filiation) {{
                const activeLinks = links.filter(l => l.target <= state.currentCommitIndex && state.activeSpheres.has(l.sphere));
                if (activeLinks.length > 0) {{
                    const rl = activeLinks[Math.floor(Math.random() * activeLinks.length)];
                    particles.push(new SynapticParticle(
                        currentPositions[rl.source],
                        currentPositions[rl.target],
                        spheres[rl.sphere]?.color || '#38bdf8',
                        12
                    ));
                }}
            }}
        }}

        ctx.clearRect(0, 0, width, height);

        ctx.save();
        ctx.translate(width / 2 + state.cam.x, height / 2 + state.cam.y);
        ctx.scale(state.cam.zoom, state.cam.zoom);

        // 1. Poussière stellaire
        cosmicDust.forEach(star => {{
            const tw = Math.sin(now * star.twinkleSpeed + star.phase);
            ctx.fillStyle = `rgba(186, 215, 255, ${{star.alpha * (0.6 + 0.4 * tw)}})`;
            ctx.beginPath();
            ctx.arc(star.x, star.y, star.size, 0, Math.PI * 2);
            ctx.fill();
        }});

        // 2. Territoires des 6 Sphères
        drawTerritories(ctx, now);

        // 3. Cœur Originel (Commit #1)
        const seedPos = currentPositions[1];
        if (seedPos && state.viewMode !== 'stream' && state.viewMode !== 'tree') {{
            const pulse = 20 + Math.sin(now * 0.003) * 4;
            const rGrad = ctx.createRadialGradient(seedPos.x, seedPos.y, 4, seedPos.x, seedPos.y, pulse * 3.8);
            rGrad.addColorStop(0, 'rgba(251, 191, 36, 0.45)');
            rGrad.addColorStop(0.5, 'rgba(56, 189, 248, 0.22)');
            rGrad.addColorStop(1, 'transparent');
            ctx.fillStyle = rGrad;
            ctx.beginPath();
            ctx.arc(seedPos.x, seedPos.y, pulse * 3.8, 0, Math.PI * 2);
            ctx.fill();
        }}

        // 4. Vrilles de Filiation Intra-Sphère (par sous-branche)
        if (state.layers.filiation) {{
            branchKeys.forEach(bKey => {{
                if (!state.activeBranches.has(bKey)) return;
                const bInfo = branches[bKey];
                if (!state.activeSpheres.has(bInfo.sphere)) return;
                const bNodes = nodes.filter(n => n.branch === bKey && n.id <= state.currentCommitIndex);
                if (bNodes.length === 0) return;

                const origin = (state.viewMode === 'galaxy' && sphereCenters.galaxy[bInfo.sphere])
                    ? sphereCenters.galaxy[bInfo.sphere]
                    : currentPositions[bNodes[0].id];

                ctx.save();
                ctx.strokeStyle = bInfo.color;
                ctx.shadowColor = bInfo.color;
                ctx.shadowBlur = 8;
                ctx.lineWidth = 1.9;
                ctx.globalAlpha = 0.48;

                ctx.beginPath();
                ctx.moveTo(origin.x, origin.y);
                for (let i = 0; i < bNodes.length; i++) {{
                    const p = currentPositions[bNodes[i].id];
                    const prev = i === 0 ? origin : currentPositions[bNodes[i - 1].id];
                    const cpx = (prev.x + p.x) * 0.5;
                    const cpy = (prev.y + p.y) * 0.5;
                    ctx.quadraticCurveTo(cpx, cpy, p.x, p.y);
                }}
                ctx.stroke();
                ctx.restore();
            }});
        }}

        // 5. Ponts Inter-Sphères (Bridge Links)
        if (state.layers.bridges) {{
            bridge_links.forEach(bl => {{
                if (bl.source > state.currentCommitIndex || bl.target > state.currentCommitIndex) return;
                if (!state.activeSpheres.has(bl.from_sphere) || !state.activeSpheres.has(bl.to_sphere)) return;
                const n0 = nodes[bl.source - 1];
                const n1 = nodes[bl.target - 1];
                if (!isNodeSphereActive(n0) || !isNodeSphereActive(n1)) return;

                const isHot = (state.hoveredNode && (state.hoveredNode.id === bl.source || state.hoveredNode.id === bl.target)) ||
                              (state.selectedNode && (state.selectedNode.id === bl.source || state.selectedNode.id === bl.target));

                // N'afficher au repos qu'un échantillon ou tous en surbrillance pour garder le graphe limpide
                if (!isHot && bl.target % 3 !== 0 && state.focusedSphere === null) return;

                const p0 = currentPositions[bl.source];
                const p1 = currentPositions[bl.target];
                const col0 = spheres[bl.from_sphere]?.color || '#38bdf8';
                const col1 = spheres[bl.to_sphere]?.color || '#c084fc';

                ctx.save();
                const grad = ctx.createLinearGradient(p0.x, p0.y, p1.x, p1.y);
                grad.addColorStop(0, col0);
                grad.addColorStop(1, col1);
                ctx.strokeStyle = grad;
                ctx.globalAlpha = isHot ? 0.92 : 0.22;
                ctx.lineWidth = isHot ? 2.2 : 1.1;
                ctx.setLineDash(isHot ? [] : [5, 5]);
                if (isHot) {{
                    ctx.shadowColor = col1;
                    ctx.shadowBlur = 10;
                }}

                ctx.beginPath();
                ctx.moveTo(p0.x, p0.y);
                const midX = (p0.x + p1.x) * 0.5;
                const midY = (p0.y + p1.y) * 0.5 - (state.viewMode === 'stream' ? 0 : 42);
                ctx.quadraticCurveTo(midX, midY, p1.x, p1.y);
                ctx.stroke();
                ctx.restore();
            }});
        }}

        // 6. Synapses Causales
        if (state.layers.synapses) {{
            cross_links.forEach(cl => {{
                if (cl.source > state.currentCommitIndex || cl.target > state.currentCommitIndex) return;
                const n0 = nodes[cl.source - 1];
                const n1 = nodes[cl.target - 1];
                if (!n0 || !n1 || !isNodeSphereActive(n0) || !isNodeSphereActive(n1)) return;

                const isHot = (state.hoveredNode && (state.hoveredNode.id === cl.source || state.hoveredNode.id === cl.target)) ||
                              (state.selectedNode && (state.selectedNode.id === cl.source || state.selectedNode.id === cl.target));

                const p0 = currentPositions[cl.source];
                const p1 = currentPositions[cl.target];

                ctx.save();
                ctx.strokeStyle = isHot ? '#38bdf8' : 'rgba(192, 132, 252, 0.28)';
                ctx.lineWidth = isHot ? 2.0 : 1.0;
                ctx.setLineDash(isHot ? [] : [3, 4]);
                ctx.beginPath();
                ctx.moveTo(p0.x, p0.y);
                ctx.quadraticCurveTo((p0.x + p1.x) * 0.5, (p0.y + p1.y) * 0.5 - 24, p1.x, p1.y);
                ctx.stroke();
                ctx.restore();
            }});
        }}

        // 7. Particules & Ondes de choc
        for (let i = particles.length - 1; i >= 0; i--) {{
            if (!particles[i].update()) particles.splice(i, 1);
            else particles[i].draw(ctx);
        }}
        for (let i = shockwaves.length - 1; i >= 0; i--) {{
            if (!shockwaves[i].update()) shockwaves.splice(i, 1);
            else shockwaves[i].draw(ctx);
        }}

        // 8. Nœuds de Commits
        nodes.forEach(node => {{
            if (node.id > state.currentCommitIndex) return;
            const pos = currentPositions[node.id];
            if (pos.scale <= 0.02) return;

            const sp = spheres[node.sphere];
            const br = branches[node.branch];
            const isSelected = state.selectedNode && state.selectedNode.id === node.id;
            const isHovered = state.hoveredNode && state.hoveredNode.id === node.id;
            const isSearchMatch = state.searchQuery.length > 0 && state.matchedNodeIds.has(node.id);
            const isSearchMuted = state.searchQuery.length > 0 && !isSearchMatch;

            ctx.save();
            ctx.translate(pos.x, pos.y);
            ctx.scale(pos.scale, pos.scale);

            ctx.globalAlpha = isSearchMuted ? 0.10 : 1.0;

            let r = pos.radius || 7;
            if (isSelected || isHovered) r *= 1.38;
            if (isSearchMatch) r *= 1.45;

            const mainCol = isSearchMatch ? '#fbbf24' : (isSelected ? '#ffffff' : sp.color);
            ctx.shadowColor = mainCol;
            ctx.shadowBlur = (isSelected || isHovered || isSearchMatch) ? 20 : 8;

            // Anneau externe bicolore si commit pont multi-sphères
            if (node.is_bridge && !isSearchMuted && state.layers.bridges) {{
                const secondSp = spheres[node.spheres_touched[1]];
                if (secondSp) {{
                    ctx.strokeStyle = secondSp.color;
                    ctx.lineWidth = 2.0;
                    ctx.beginPath();
                    ctx.arc(0, 0, r + 3.2, 0, Math.PI * 2);
                    ctx.stroke();
                }}
            }}

            // Disque principal de la sphère
            ctx.fillStyle = mainCol;
            ctx.beginPath();
            ctx.arc(0, 0, r, 0, Math.PI * 2);
            ctx.fill();

            // Cœur sombre
            ctx.fillStyle = '#050a17';
            ctx.beginPath();
            ctx.arc(0, 0, r * 0.62, 0, Math.PI * 2);
            ctx.fill();

            // Pupille colorée de la sous-branche
            ctx.fillStyle = isSelected ? '#ffffff' : (br?.color || mainCol);
            ctx.beginPath();
            ctx.arc(0, 0, r * 0.34, 0, Math.PI * 2);
            ctx.fill();

            // Couronne dorée pour les jalons majeurs (Milestones)
            if (node.milestone && state.layers.milestones && !isSearchMuted) {{
                ctx.strokeStyle = '#fde047';
                ctx.lineWidth = 1.6;
                ctx.setLineDash([3, 3]);
                ctx.beginPath();
                ctx.arc(0, 0, r + 6.5, 0, Math.PI * 2);
                ctx.stroke();
                ctx.setLineDash([]);
            }}

            // Étiquette
            if (state.cam.zoom > 1.15 || isSelected || isHovered || isSearchMatch || (node.milestone && state.layers.milestones && state.cam.zoom > 0.65)) {{
                ctx.font = '600 10px "JetBrains Mono", monospace';
                ctx.fillStyle = isSelected ? '#ffffff' : (node.milestone ? '#fde047' : '#cbd5e1');
                ctx.shadowBlur = 4;
                ctx.textAlign = 'center';
                const lbl = node.milestone ? `★ #${{node.id}}` : `#${{node.id}}`;
                ctx.fillText(lbl, 0, r + 13);
            }}

            ctx.restore();
        }});

        ctx.restore();
        requestAnimationFrame(renderLoop);
    }}

    renderSpheresBar();
    renderSubbranchesBar();
    resizeCanvas();
    updateTimelineUI(TOTAL_COMMITS);
    requestAnimationFrame(renderLoop);
}})();
</script>
    <script src="js/theme.js"></script>
</body>
</html>
'''

    with open("project_evolution.html", "w", encoding="utf-8") as f:
        f.write(html_template)
    print("project_evolution.html généré avec succès !")


if __name__ == "__main__":
    build_html()
