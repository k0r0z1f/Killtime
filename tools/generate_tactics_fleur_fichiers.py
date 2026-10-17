#!/usr/bin/env python3
"""
Fleur des Fichiers : meme moteur floral/neural que tactics_evolution,
mais 1 noeud = 1 fichier actuel avec son historique git complet.
Produit tactics_fleur_fichiers.html depuis data/tactics_files_history.json.
"""

import json, os

MOIS_FR = {"01": "Janvier", "02": "Février", "03": "Mars", "04": "Avril",
           "05": "Mai", "06": "Juin", "07": "Juillet", "08": "Août",
           "09": "Septembre", "10": "Octobre", "11": "Novembre", "12": "Décembre"}
MOIS_SHORT = {"01": "Jan", "02": "Fév", "03": "Mar", "04": "Avr", "05": "Mai",
              "06": "Juin", "07": "Juil", "08": "Août", "09": "Sep",
              "10": "Oct", "11": "Nov", "12": "Déc"}

def build_html():
    with open('data/tactics_files_history.json', 'r', encoding='utf-8') as f:
        tactics_json_str = f.read()
    data = json.loads(tactics_json_str)
    total_commits = data["total_commits"]
    total_minus_one = total_commits - 1
    n_modules = len(data["branches"])
    n_loc = data["totals"]["loc"]
    n_loc_fr = f"{n_loc:,}".replace(",", " ")
    start = data["date_range"]["start"]  # YYYY-MM-DD
    end = data["date_range"]["end"]
    sy, sm, sd = start.split("-")
    ey, em, ed = end.split("-")
    range_short = f"{int(sd)} {MOIS_SHORT[sm]} - {int(ed)} {MOIS_SHORT[em]} {ey}"
    range_long = f"{int(sd)} {MOIS_FR[sm]} {sy} au {int(ed)} {MOIS_FR[em]} {ey}"
    n_coupling = len(data["cross_links"])
    n_inherit = len(data.get("inherit_links", []))
    n_partial = len(data.get("partial_links", []))
    n_test = len(data.get("test_links", []))

    html_template = f'''<!DOCTYPE html>
<html lang="fr" data-theme="dark">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Killtime Tactics — La Fleur des Fichiers</title>
    <meta name="description" content="Fleur neuronale des fichiers de Killtime Tactics : {total_commits} fichiers avec historique git complet, pétales par module, synapses de co-évolution et chronologie de création.">

    <!-- Typographies Killtime -->
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link href="https://fonts.googleapis.com/css2?family=Cinzel:wght@600;700;800;900&family=JetBrains+Mono:wght@400;500;600;700&family=Plus+Jakarta+Sans:wght@300;400;500;600;700;800&display=swap" rel="stylesheet">

    <style>
        :root {{
            --bg-void: #030712;
            --bg-canvas: #050a17;
            --bg-surface: rgba(11, 17, 32, 0.82);
            --bg-surface-elevated: rgba(18, 28, 51, 0.92);
            --bg-surface-hover: rgba(30, 45, 78, 0.9);
            --border-subtle: rgba(255, 255, 255, 0.09);
            --border-glow: rgba(56, 189, 248, 0.35);

            --text-primary: #f8fafc;
            --text-secondary: #94a3b8;
            --text-muted: #64748b;
            --text-accent: #38bdf8;
            --text-gold: #fbbf24;

            /* Branches Colors */
            --branch-story: #c084fc;
            --branch-tactics: #fbbf24;
            --branch-core: #38bdf8;
            --branch-visuals: #f43f5e;
            --branch-network: #34d399;
            --branch-audio: #a855f7;
            --branch-tools: #f97316;

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

        /* ================== APPLICATION LAYOUT ================== */
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
            height: 64px;
            background: rgba(6, 10, 20, 0.78);
            backdrop-filter: blur(20px);
            -webkit-backdrop-filter: blur(20px);
            border-bottom: 1px solid var(--border-subtle);
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 0 20px;
            z-index: 50;
            flex-shrink: 0;
        }}

        .topbar-left {{
            display: flex;
            align-items: center;
            gap: 16px;
        }}

        .brand {{
            display: flex;
            align-items: center;
            gap: 12px;
            text-decoration: none;
            color: var(--text-primary);
        }}

        .brand-icon {{
            width: 36px;
            height: 36px;
            border-radius: 10px;
            background: linear-gradient(135deg, rgba(56, 189, 248, 0.25), rgba(192, 132, 252, 0.25));
            border: 1px solid rgba(56, 189, 248, 0.4);
            display: flex;
            align-items: center;
            justify-content: center;
            box-shadow: 0 0 15px rgba(56, 189, 248, 0.2);
            color: #38bdf8;
            font-size: 18px;
        }}

        .brand-info h1 {{
            font-family: var(--font-title);
            font-size: 15px;
            letter-spacing: 0.05em;
            display: flex;
            align-items: center;
            gap: 8px;
            color: #ffffff;
        }}

        .brand-badge {{
            font-family: var(--font-mono);
            font-size: 10px;
            padding: 2px 7px;
            border-radius: 999px;
            background: rgba(192, 132, 252, 0.18);
            border: 1px solid rgba(192, 132, 252, 0.4);
            color: #e9d5ff;
            text-transform: uppercase;
            letter-spacing: 0.08em;
        }}

        .breadcrumbs {{
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: 12px;
            color: var(--text-muted);
            border-left: 1px solid var(--border-subtle);
            padding-left: 16px;
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
            gap: 12px;
        }}

        /* Metrics Pill in Topbar */
        .metrics-hud {{
            display: flex;
            align-items: center;
            gap: 14px;
            background: rgba(255, 255, 255, 0.03);
            border: 1px solid var(--border-subtle);
            padding: 4px 14px;
            border-radius: 999px;
            font-size: 12px;
            color: var(--text-secondary);
        }}

        .metric-item {{
            display: flex;
            align-items: center;
            gap: 5px;
        }}
        .metric-val {{
            color: #38bdf8;
            font-weight: 700;
            font-family: var(--font-mono);
        }}

        .topbar-right {{
            display: flex;
            align-items: center;
            gap: 10px;
        }}

        .btn-ghost {{
            background: rgba(255, 255, 255, 0.05);
            border: 1px solid var(--border-subtle);
            color: var(--text-secondary);
            padding: 7px 12px;
            border-radius: 8px;
            font-size: 12px;
            font-weight: 600;
            cursor: pointer;
            display: inline-flex;
            align-items: center;
            gap: 6px;
            transition: var(--ease-out) 0.2s;
            text-decoration: none;
        }}

        .btn-ghost:hover {{
            background: rgba(255, 255, 255, 0.12);
            color: var(--text-primary);
            border-color: rgba(255, 255, 255, 0.25);
        }}

        .sound-toggle.active {{
            background: rgba(56, 189, 248, 0.15);
            border-color: rgba(56, 189, 248, 0.4);
            color: #38bdf8;
        }}

        /* ================== CANVAS CONTAINER ================== */
        #canvas-wrapper {{
            position: relative;
            flex: 1;
            width: 100%;
            height: calc(100% - 64px);
            overflow: hidden;
            background: radial-gradient(circle at 50% 50%, #080f22 0%, #030712 85%);
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
        /* View Mode Switcher (Top Left) */
        .hud-view-modes {{
            position: absolute;
            top: 16px;
            left: 20px;
            display: flex;
            background: var(--bg-surface);
            backdrop-filter: blur(16px);
            -webkit-backdrop-filter: blur(16px);
            border: 1px solid var(--border-subtle);
            border-radius: 12px;
            padding: 4px;
            gap: 4px;
            z-index: 20;
            box-shadow: 0 10px 30px rgba(0, 0, 0, 0.4);
        }}

        .mode-btn {{
            background: transparent;
            border: none;
            color: var(--text-secondary);
            padding: 7px 12px;
            border-radius: 8px;
            font-size: 12px;
            font-weight: 600;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 6px;
            transition: all 0.2s ease;
        }}

        .mode-btn:hover {{
            color: var(--text-primary);
            background: rgba(255, 255, 255, 0.06);
        }}

        .mode-btn.active {{
            background: linear-gradient(135deg, rgba(56, 189, 248, 0.2), rgba(192, 132, 252, 0.2));
            color: #fff;
            border: 1px solid rgba(56, 189, 248, 0.4);
            box-shadow: 0 0 12px rgba(56, 189, 248, 0.25);
        }}

        /* Couches de liaisons (sous le sélecteur de vue) */
        .hud-links {{
            position: absolute;
            top: 66px;
            left: 20px;
            display: flex;
            background: var(--bg-surface);
            backdrop-filter: blur(16px);
            -webkit-backdrop-filter: blur(16px);
            border: 1px solid var(--border-subtle);
            border-radius: 12px;
            padding: 4px;
            gap: 4px;
            z-index: 20;
            box-shadow: 0 10px 30px rgba(0, 0, 0, 0.4);
            max-width: calc(100vw - 40px);
            overflow-x: auto;
        }}
        .link-count {{
            font-family: var(--font-mono);
            font-size: 10px;
            opacity: 0.65;
        }}

        /* Search & Filter Bar (Top Right) */
        .hud-search-filter {{
            position: absolute;
            top: 16px;
            right: 20px;
            display: flex;
            align-items: center;
            gap: 10px;
            z-index: 20;
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
            padding: 4px 12px;
            width: 250px;
            transition: all 0.25s ease;
            box-shadow: 0 10px 30px rgba(0, 0, 0, 0.4);
        }}

        .search-box:focus-within {{
            width: 320px;
            border-color: rgba(56, 189, 248, 0.5);
            box-shadow: 0 0 18px rgba(56, 189, 248, 0.25);
        }}

        .search-input {{
            background: transparent;
            border: none;
            outline: none;
            color: var(--text-primary);
            font-size: 13px;
            margin-left: 8px;
            width: 100%;
            font-family: var(--font-body);
        }}

        .search-input::placeholder {{
            color: var(--text-muted);
        }}

        /* Branch Filter Pills (Top center under topbar) */
        .branch-pills-bar {{
            position: absolute;
            top: 16px;
            left: 50%;
            transform: translateX(-50%);
            display: flex;
            gap: 6px;
            background: var(--bg-surface);
            backdrop-filter: blur(16px);
            -webkit-backdrop-filter: blur(16px);
            border: 1px solid var(--border-subtle);
            border-radius: 999px;
            padding: 5px 8px;
            z-index: 20;
            box-shadow: 0 10px 30px rgba(0, 0, 0, 0.4);
            max-width: 90vw;
            overflow-x: auto;
        }}

        .branch-pill {{
            background: transparent;
            border: 1px solid transparent;
            color: var(--text-muted);
            padding: 4px 10px;
            border-radius: 999px;
            font-size: 11px;
            font-weight: 700;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 5px;
            transition: all 0.2s ease;
            white-space: nowrap;
        }}

        .branch-pill:hover {{
            color: var(--text-secondary);
            background: rgba(255, 255, 255, 0.05);
        }}

        .branch-pill.active {{
            background: rgba(255, 255, 255, 0.08);
            color: var(--text-primary);
            border-color: currentColor;
        }}

        .branch-dot {{
            width: 8px;
            height: 8px;
            border-radius: 50%;
            display: inline-block;
        }}

        /* Navigation Controls (Floating Bottom Right) */
        .camera-controls {{
            position: absolute;
            right: 20px;
            bottom: 120px;
            display: flex;
            flex-direction: column;
            gap: 6px;
            z-index: 20;
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
            transition: all 0.2s ease;
            box-shadow: 0 6px 16px rgba(0, 0, 0, 0.35);
        }}
        .cam-btn:hover {{
            background: var(--bg-surface-elevated);
            color: var(--text-primary);
            border-color: rgba(56, 189, 248, 0.4);
        }}

        /* ================== BOTTOM TIMELINE & PLAYER HUD ================== */
        .bottom-timeline-panel {{
            position: absolute;
            bottom: 16px;
            left: 20px;
            right: 20px;
            background: var(--bg-surface);
            backdrop-filter: blur(20px);
            -webkit-backdrop-filter: blur(20px);
            border: 1px solid var(--border-subtle);
            border-radius: 16px;
            padding: 12px 20px;
            display: flex;
            flex-direction: column;
            gap: 10px;
            z-index: 30;
            box-shadow: 0 16px 40px rgba(0, 0, 0, 0.5);
        }}

        .timeline-upper {{
            display: flex;
            align-items: center;
            justify-content: space-between;
        }}

        .player-controls {{
            display: flex;
            align-items: center;
            gap: 12px;
        }}

        .btn-play-pause {{
            width: 40px;
            height: 40px;
            border-radius: 10px;
            border: 1px solid rgba(56, 189, 248, 0.5);
            background: linear-gradient(135deg, rgba(56, 189, 248, 0.3), rgba(192, 132, 252, 0.3));
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
            padding: 4px 8px;
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
            gap: 16px;
            font-size: 13px;
        }}

        .current-date-badge {{
            font-family: var(--font-mono);
            font-size: 13px;
            color: #38bdf8;
            font-weight: 700;
            background: rgba(56, 189, 248, 0.1);
            border: 1px solid rgba(56, 189, 248, 0.3);
            padding: 3px 10px;
            border-radius: 8px;
        }}

        .active-epoch-badge {{
            font-size: 12px;
            color: #e2e8f0;
            display: flex;
            align-items: center;
            gap: 6px;
        }}

        .timeline-actions {{
            display: flex;
            align-items: center;
            gap: 8px;
        }}

        /* Scrub Slider Track */
        .slider-wrapper {{
            position: relative;
            width: 100%;
            display: flex;
            align-items: center;
        }}

        .chrono-slider {{
            -webkit-appearance: none;
            width: 100%;
            height: 6px;
            border-radius: 999px;
            background: rgba(255, 255, 255, 0.12);
            outline: none;
            cursor: pointer;
            transition: background 0.2s;
        }}

        .chrono-slider::-webkit-slider-thumb {{
            -webkit-appearance: none;
            appearance: none;
            width: 18px;
            height: 18px;
            border-radius: 50%;
            background: #38bdf8;
            border: 2px solid #ffffff;
            cursor: pointer;
            box-shadow: 0 0 12px rgba(56, 189, 248, 0.8);
            transition: transform 0.15s ease;
        }}

        .chrono-slider::-webkit-slider-thumb:hover {{
            transform: scale(1.25);
        }}

        /* Epoch markers on timeline */
        .epoch-markers-bar {{
            display: flex;
            justify-content: space-between;
            padding: 0 4px;
            margin-top: 2px;
        }}

        .epoch-mark {{
            font-size: 10px;
            color: var(--text-muted);
            cursor: pointer;
            font-weight: 600;
            transition: color 0.2s;
        }}
        .epoch-mark:hover {{
            color: #38bdf8;
        }}
        .epoch-mark.active {{
            color: #38bdf8;
            font-weight: 800;
        }}

        /* ================== INSPECTOR DRAWER (RIGHT PANEL) ================== */
        #inspector-drawer {{
            position: absolute;
            top: 72px;
            right: -420px;
            width: 400px;
            bottom: 24px;
            background: var(--bg-surface-elevated);
            backdrop-filter: blur(24px);
            -webkit-backdrop-filter: blur(24px);
            border: 1px solid var(--border-subtle);
            border-radius: 20px;
            padding: 24px;
            display: flex;
            flex-direction: column;
            gap: 16px;
            z-index: 60;
            box-shadow: -15px 0 40px rgba(0, 0, 0, 0.6);
            transition: right 0.35s var(--ease-out);
            overflow-y: auto;
        }}

        #inspector-drawer.open {{
            right: 20px;
        }}

        .drawer-header {{
            display: flex;
            align-items: flex-start;
            justify-content: space-between;
            border-bottom: 1px solid var(--border-subtle);
            padding-bottom: 16px;
        }}

        .drawer-branch-tag {{
            display: inline-flex;
            align-items: center;
            gap: 6px;
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.08em;
            padding: 3px 8px;
            border-radius: 6px;
            margin-bottom: 8px;
        }}

        .drawer-commit-num {{
            font-family: var(--font-mono);
            font-size: 12px;
            color: var(--text-muted);
        }}

        .drawer-title {{
            font-family: var(--font-title);
            font-size: 18px;
            color: #fff;
            line-height: 1.35;
        }}

        .drawer-close-btn {{
            background: rgba(255, 255, 255, 0.06);
            border: 1px solid var(--border-subtle);
            color: var(--text-secondary);
            width: 32px;
            height: 32px;
            border-radius: 8px;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: all 0.2s ease;
            flex-shrink: 0;
        }}
        .drawer-close-btn:hover {{
            color: #fff;
            background: rgba(255, 255, 255, 0.15);
        }}

        .drawer-meta-grid {{
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 10px;
            background: rgba(0, 0, 0, 0.25);
            padding: 12px;
            border-radius: 12px;
            border: 1px solid var(--border-subtle);
            font-size: 12px;
        }}

        .meta-field label {{
            color: var(--text-muted);
            display: block;
            margin-bottom: 2px;
            font-size: 10px;
            text-transform: uppercase;
        }}
        .meta-field span {{
            color: var(--text-primary);
            font-weight: 600;
            font-family: var(--font-mono);
        }}

        .drawer-summary {{
            font-size: 13px;
            line-height: 1.6;
            color: #cbd5e1;
            background: rgba(255, 255, 255, 0.02);
            padding: 12px;
            border-radius: 10px;
            border-left: 3px solid #38bdf8;
        }}

        .drawer-section-title {{
            font-size: 12px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.08em;
            color: var(--text-secondary);
            margin-bottom: 8px;
            display: flex;
            align-items: center;
            justify-content: space-between;
        }}

        .files-list {{
            list-style: none;
            display: flex;
            flex-direction: column;
            gap: 6px;
            max-height: 220px;
            overflow-y: auto;
            padding-right: 4px;
        }}

        .file-item {{
            display: flex;
            align-items: center;
            justify-content: space-between;
            font-size: 12px;
            font-family: var(--font-mono);
            background: rgba(255, 255, 255, 0.03);
            border: 1px solid var(--border-subtle);
            border-radius: 8px;
            padding: 6px 10px;
            transition: all 0.15s ease;
        }}
        .file-item:hover {{
            background: rgba(255, 255, 255, 0.07);
        }}

        .file-badge {{
            font-size: 9px;
            padding: 2px 6px;
            border-radius: 4px;
            font-weight: 700;
            text-transform: uppercase;
        }}
        .badge-csharp {{ background: rgba(56, 189, 248, 0.2); color: #38bdf8; }}
        .badge-shader {{ background: rgba(244, 63, 94, 0.2); color: #f43f5e; }}
        .badge-json {{ background: rgba(251, 191, 36, 0.2); color: #fbbf24; }}
        .badge-scene {{ background: rgba(168, 85, 247, 0.2); color: #a855f7; }}
        .badge-asset {{ background: rgba(52, 211, 153, 0.2); color: #34d399; }}
        .badge-commit {{ background: rgba(251, 191, 36, 0.2); color: #fbbf24; }}

        .causal-links-group {{
            display: flex;
            flex-direction: column;
            gap: 6px;
        }}

        .causal-jump-btn {{
            display: flex;
            align-items: center;
            justify-content: space-between;
            background: rgba(255, 255, 255, 0.04);
            border: 1px solid var(--border-subtle);
            color: var(--text-secondary);
            padding: 8px 12px;
            border-radius: 8px;
            font-size: 12px;
            cursor: pointer;
            transition: all 0.2s ease;
            text-align: left;
        }}
        .causal-jump-btn:hover {{
            background: rgba(56, 189, 248, 0.15);
            color: #fff;
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
            padding: 10px;
            border-radius: 10px;
            font-weight: 700;
            font-size: 12px;
            transition: all 0.2s ease;
            margin-top: auto;
        }}
        .btn-github-link:hover {{
            border-color: rgba(56, 189, 248, 0.5);
            background: rgba(56, 189, 248, 0.15);
            box-shadow: 0 0 15px rgba(56, 189, 248, 0.2);
        }}

        /* Tooltip on Canvas */
        #node-tooltip {{
            position: absolute;
            pointer-events: none;
            background: rgba(10, 16, 30, 0.92);
            backdrop-filter: blur(12px);
            border: 1px solid rgba(56, 189, 248, 0.4);
            border-radius: 10px;
            padding: 8px 14px;
            color: #fff;
            font-size: 12px;
            z-index: 100;
            box-shadow: 0 8px 24px rgba(0, 0, 0, 0.5);
            display: none;
            transform: translate(-50%, -120%);
            white-space: nowrap;
        }}

        #node-tooltip .tt-title {{
            font-weight: 700;
            color: #38bdf8;
            margin-bottom: 2px;
        }}
        #node-tooltip .tt-sub {{
            color: var(--text-secondary);
            font-size: 11px;
            font-family: var(--font-mono);
        }}

        /* Help Modal */
        #help-modal {{
            position: fixed;
            top: 0;
            left: 0;
            width: 100vw;
            height: 100vh;
            background: rgba(0, 0, 0, 0.7);
            backdrop-filter: blur(8px);
            display: none;
            align-items: center;
            justify-content: center;
            z-index: 200;
        }}

        .modal-content {{
            width: 540px;
            background: var(--bg-surface-elevated);
            border: 1px solid var(--border-glow);
            border-radius: 20px;
            padding: 28px;
            box-shadow: 0 20px 60px rgba(0, 0, 0, 0.8);
            display: flex;
            flex-direction: column;
            gap: 16px;
        }}

        /* Responsive Breakpoints */
        @media (max-width: 900px) {{
            .topbar-center {{ display: none; }}
            .branch-pills-bar {{ display: none; }}
            #inspector-drawer {{ width: 100%; right: -100%; border-radius: 0; }}
            #inspector-drawer.open {{ right: 0; }}
        }}
    </style>
</head>
<body>

<div id="app-root">
    <!-- ================= TOPBAR ================= -->
    <header class="topbar">
        <div class="topbar-left">
            <a href="index.html" class="brand">
                <div class="brand-icon">
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polygon points="12 2 2 7 12 12 22 7 12 2" />
                        <polyline points="2 17 12 22 22 17" />
                        <polyline points="2 12 12 17 22 12" />
                    </svg>
                </div>
                <div class="brand-info">
                    <h1>Killtime Tactics <span class="brand-badge">Fleur des Fichiers</span></h1>
                </div>
            </a>
            <nav class="breadcrumbs">
                <a href="index.html">Codex</a>
                <span>/</span>
                <a href="Killtime/index.html">Univers</a>
                <span>/</span>
                <a href="tactics_evolution.html">Évolution Git</a>
                <span>/</span>
                <span class="current">Fleur des Fichiers</span>
            </nav>
        </div>

        <div class="topbar-center">
            <div class="metrics-hud">
                <div class="metric-item">
                    <span>Fichiers:</span>
                    <span class="metric-val" id="metric-commits">{total_commits}</span>
                </div>
                <span>•</span>
                <div class="metric-item">
                    <span>Lignes:</span>
                    <span class="metric-val" id="metric-scripts">{n_loc_fr}</span>
                </div>
                <span>•</span>
                <div class="metric-item">
                    <span>Modules:</span>
                    <span class="metric-val">{n_modules}</span>
                </div>
                <span>•</span>
                <div class="metric-item">
                    <span>Période:</span>
                    <span class="metric-val" style="color:#fbbf24">{range_short}</span>
                </div>
            </div>
        </div>

        <div class="topbar-right">
            <button class="btn-ghost sound-toggle active" id="audioToggleBtn" title="Activer / Couper la synthèse audio spatiale">
                <span id="audio-icon">🔊</span>
                <span>Audio Synth</span>
            </button>
            <button class="btn-ghost" id="helpModalBtn" title="Guide des commandes & Métaphore">
                <span>ℹ️</span>
                <span>Guide</span>
            </button>
            <a href="tactics_evolution.html" class="btn-ghost" title="Voir la fleur des commits Git">
                <span>🌺</span>
                <span>Évolution Git</span>
            </a>
            <a href="tactics_files.html" class="btn-ghost" title="Explorer la cartographie des fichiers">
                <span>🗂️</span>
                <span>Fichiers</span>
            </a>
            <a href="atlas_3d.html" class="btn-ghost" title="Explorer l'Atlas 3D">
                <span>🌐</span>
                <span>Atlas 3D</span>
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

        <!-- View Mode Switcher -->
        <div class="hud-view-modes">
            <button class="mode-btn active" data-mode="flower" title="Corolle florale avec pétales de branches en spirale">
                <span>🌺</span>
                <span>Fleur Temporelle</span>
            </button>
            <button class="mode-btn" data-mode="neural" title="Cortex cérébral stellaire avec axones et synapses">
                <span>🧠</span>
                <span>Réseau Synaptique</span>
            </button>
            <button class="mode-btn" data-mode="tree" title="Arbre phylogénétique de causalité stratifié par époques">
                <span>🌲</span>
                <span>Arbre de Causalité</span>
            </button>
            <button class="mode-btn" data-mode="force" title="Rhizome gravitationnel interactif avec nœuds physiques">
                <span>🌀</span>
                <span>Rhizome Libre</span>
            </button>
        </div>

        <!-- Link Layers Switcher -->
        <div class="hud-links" id="linksBar">
            <button class="mode-btn link-toggle active" data-layer="coupling" title="Fichiers modifiés ensemble dans les mêmes commits">
                <span>🔗</span>
                <span>Synapses</span>
                <span class="link-count">{n_coupling}</span>
            </button>
            <button class="mode-btn link-toggle active" data-layer="inherit" title="Classe fille vers classe de base / interface du projet">
                <span>🧬</span>
                <span>Héritage</span>
                <span class="link-count">{n_inherit}</span>
            </button>
            <button class="mode-btn link-toggle active" data-layer="partial" title="Fichiers partiels vers le fichier principal de la classe">
                <span>🧩</span>
                <span>Partials</span>
                <span class="link-count">{n_partial}</span>
            </button>
            <button class="mode-btn link-toggle" data-layer="test" title="Fichiers de test vers fichiers testés">
                <span>🧪</span>
                <span>Tests</span>
                <span class="link-count">{n_test}</span>
            </button>
        </div>

        <!-- Branch Pills (Top Center) -->
        <div class="branch-pills-bar" id="branchPillsBar">
            <!-- Dynamically generated branch filter buttons -->
        </div>

        <!-- Search Bar (Top Right) -->
        <div class="hud-search-filter">
            <div class="search-box">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" color="#64748b">
                    <circle cx="11" cy="11" r="8" />
                    <line x1="21" y1="21" x2="16.65" y2="16.65" />
                </svg>
                <input type="text" class="search-input" id="commitSearchInput" placeholder="Chercher fichier, classe, commit..." autocomplete="off">
            </div>
        </div>

        <!-- Camera Controls (Floating Right) -->
        <div class="camera-controls">
            <button class="cam-btn" id="btnZoomIn" title="Zoom avant">+</button>
            <button class="cam-btn" id="btnZoomOut" title="Zoom arrière">−</button>
            <button class="cam-btn" id="btnResetView" title="Recentrer la fleur">⌂</button>
            <button class="cam-btn" id="btnToggleBloomAll" title="Floraison complète / instantanée">🌸</button>
        </div>

        <!-- Tooltip -->
        <div id="node-tooltip">
            <div class="tt-title" id="tt-title">Titre</div>
            <div class="tt-sub" id="tt-sub">Date & Hash</div>
        </div>

        <!-- ================= INSPECTOR DRAWER ================= -->
        <aside id="inspector-drawer">
            <div class="drawer-header">
                <div>
                    <span class="drawer-branch-tag" id="drawerBranchTag">Story & Cinématiques</span>
                    <div class="drawer-commit-num" id="drawerCommitNum">FICHIER #1 • créé le 8 Sep 2026</div>
                    <h2 class="drawer-title" id="drawerTitle">Nom du fichier</h2>
                </div>
                <button class="drawer-close-btn" id="drawerCloseBtn" title="Fermer le volet">✕</button>
            </div>

            <div class="drawer-meta-grid">
                <div class="meta-field">
                    <label>Créé par</label>
                    <span id="drawerAuthor">Alexis Lacasse</span>
                </div>
                <div class="meta-field">
                    <label>Créé dans</label>
                    <span id="drawerHash">59a92f8</span>
                </div>
                <div class="meta-field">
                    <label>Évolution cumulée</label>
                    <span id="drawerDiff">+2240 / -0</span>
                </div>
                <div class="meta-field">
                    <label>Commits</label>
                    <span id="drawerFilesCount">29 fichiers</span>
                </div>
            </div>

            <div class="drawer-summary" id="drawerSizeNow" style="border-left-color:#fbbf24;">
                📏 — lignes à la date affichée.
            </div>

            <div id="drawerHotspot" style="display:none;font-size:12px;font-weight:700;color:#fda4af;background:rgba(244,63,94,.12);border:1px solid rgba(244,63,94,.5);padding:8px 12px;border-radius:10px;">
                ⚠️ HOTSPOT
            </div>

            <div id="drawerWorktree" style="display:none;font-size:12px;font-weight:700;color:#fcd34d;background:rgba(251,191,36,.10);border:1px solid rgba(251,191,36,.45);padding:8px 12px;border-radius:10px;">
                WORKTREE
            </div>

            <div id="drawerTestsWrap" style="display:none;">
                <div class="drawer-section-title">Couverture de tests</div>
                <div class="causal-links-group" id="drawerTestsList"></div>
            </div>

            <div class="drawer-summary" id="drawerSummary">
                Historique complet du fichier : création, commits, volumes.
            </div>

            <div>
                <div class="drawer-section-title">
                    <span>Historique des commits</span>
                    <span style="font-family: var(--font-mono); font-size: 10px;" id="drawerFileCategoryCount"></span>
                </div>
                <ul class="files-list" id="drawerFilesList">
                    <!-- Populated dynamically -->
                </ul>
            </div>

            <div id="drawerCausalSection">
                <div class="drawer-section-title">Relations & Liaisons</div>
                <div class="causal-links-group" id="drawerCausalList">
                    <!-- Populated dynamically -->
                </div>
            </div>

            <a href="#" target="_blank" rel="noopener" class="btn-github-link" id="drawerGithubBtn">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0024 12c0-6.63-5.37-12-12-12z"/>
                </svg>
                <span>Voir le fichier sur GitHub ↗</span>
            </a>
        </aside>

        <!-- ================= BOTTOM TIMELINE & PLAYER ================= -->
        <div class="bottom-timeline-panel">
            <div class="timeline-upper">
                <div class="player-controls">
                    <button class="btn-play-pause" id="playPauseBtn" title="Lancer / Mettre en pause l'éclosion des fichiers (Espace)">
                        <span id="play-icon">▶</span>
                    </button>
                    <button class="btn-ghost" id="btnStepBack" title="Fichier précédent">⏮</button>
                    <button class="btn-ghost" id="btnStepForward" title="Fichier suivant">⏭</button>

                    <div class="speed-selector">
                        <button class="speed-btn" data-speed="0.5">0.5x</button>
                        <button class="speed-btn active" data-speed="1">1x</button>
                        <button class="speed-btn" data-speed="2">2x</button>
                        <button class="speed-btn" data-speed="5">5x</button>
                    </div>
                </div>

                <div class="timeline-status">
                    <div class="current-date-badge" id="hudCurrentDate">8 Sep 2026</div>
                    <div class="active-epoch-badge" id="hudActiveEpoch">
                        <span>Époque I : Fondation & Arène</span>
                    </div>
                    <div style="font-family: var(--font-mono); font-size: 12px; color: var(--text-muted);">
                        <span id="hudActiveCommitIndex" style="color:#fff; font-weight:700;">1</span> / {total_commits} fichiers
                    </div>
                </div>

                <div class="timeline-actions">
                    <button class="btn-ghost" id="btnRestartSeed" title="Recommencer depuis la graine">🌱 Graine</button>
                    <button class="btn-ghost" id="btnFullBloom" title="Floraison complète immédiate">🌺 Tout Déployer</button>
                </div>
            </div>

            <!-- Scrubber Track -->
            <div class="slider-wrapper">
                <input type="range" class="chrono-slider" id="timelineSlider" min="1" max="{total_commits}" value="1" step="1">
            </div>

            <div class="epoch-markers-bar" id="epochMarkersBar">
                <!-- Populated dynamically -->
            </div>
        </div>
    </div>
</div>

<!-- ================= HELP & LORE MODAL ================= -->
<div id="help-modal">
    <div class="modal-content">
        <h2 style="font-family: var(--font-title); color: #38bdf8; display: flex; align-items: center; gap: 10px;">
            <span>🌺</span>
            <span>La Fleur des Fichiers de Killtime Tactics</span>
        </h2>
        <p style="font-size: 13px; line-height: 1.6; color: #cbd5e1;">
            Chaque <strong>pétale est un fichier</strong> du code maison, qui éclot à la date de sa <strong>création git</strong> ({total_commits} fichiers, {range_long}). La couleur indique son module, la chronologie sa naissance.
        </p>
        <div style="background: rgba(0,0,0,0.3); border-radius: 12px; padding: 14px; border: 1px solid var(--border-subtle); font-size: 12px; display: flex; flex-direction: column; gap: 8px;">
            <div>🌸 <strong>Pétales métier :</strong> 13 domaines (Personnage, Combat, Tactique, Récit, Ateliers, Arène, Interface, Multijoueur, Audio, Caméra, Tests, Données, Visuels) — regroupés par usage, pas par dossier.</div>
            <div>🔗 <strong>4 couches de liaisons commutables</strong> (interrupteurs sous les vues) : <strong>Synapses</strong> violettes = co-évolution (épaisseur = commits communs), <strong>Héritage</strong> cyan fléché = classe fille → parente, <strong>Partials</strong> pointillés = satellite → fichier principal, <strong>Tests</strong> verts = test → testé.</div>
            <div>🔥 <strong>Halo de churn :</strong> l'auréole orange pulse selon l'instabilité (nombre de commits). Anneau rouge tournant = ⚠️ hotspot (gros et instable).</div>
            <div>🧪 <strong>Lunes de tests :</strong> les satellites verts orbitent les fichiers couverts par des tests — cliquez un fichier pour sauter vers ses tests.</div>
            <div>📏 <strong>Taille vivante :</strong> le rayon de chaque nœud = ses lignes réelles à la date affichée — les fichiers gonflent et maigrissent pendant la lecture.</div>
            <div>🎵 <strong>Synthèse Sonore :</strong> Harmonie céleste pentatonique générée en temps réel à chaque floraison.</div>
            <div>🕹️ <strong>Navigation :</strong> Glissez pour déplacer la vue, molette pour zoomer, cliquez sur un nœud pour voir l'historique complet du fichier.</div>
            <div>⌨️ <strong>Raccourcis :</strong> <kbd style="background: #1e293b; padding: 2px 5px; border-radius: 4px;">Espace</kbd> Play/Pause, <kbd style="background: #1e293b; padding: 2px 5px; border-radius: 4px;">←</kbd> / <kbd style="background: #1e293b; padding: 2px 5px; border-radius: 4px;">→</kbd> Reculer/Avancer.</div>
        </div>
        <button class="btn-ghost" id="helpModalCloseBtn" style="align-self: flex-end; padding: 8px 18px; color: #fff; border-color: #38bdf8;">
            Compris, explorer l'arbre !
        </button>
    </div>
</div>

<!-- DATASET EMBEDDED -->
<script id="evolution-data" type="application/json">
{tactics_json_str}
</script>

<script>
/* ==========================================================================
   MOTEUR D'ANIMATION NEURONALE & FLORALE — KILLTIME TACTICS
   ========================================================================== */

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

    const {{ nodes, links, cross_links, branches, epochs,
            inherit_links = [], partial_links = [], test_links = [] }} = rawData;
    const TOTAL_COMMITS = {total_commits};

    // 2. ÉTAT GLOBAL DE L'APPLICATION
    const state = {{
        currentCommitIndex: 1, // 1 à {total_commits}
        targetCommitIndex: 1,
        progressFloat: 1.0,    // Interpolation fluide
        isPlaying: false,
        playbackSpeed: 1.0,    // 0.5, 1, 2, 5
        viewMode: 'flower',    // 'flower', 'neural', 'tree', 'force'
        audioEnabled: true,
        selectedNode: null,
        hoveredNode: null,
        activeBranches: new Set(Object.keys(branches)), // toutes actives par défaut
        searchQuery: '',
        matchedNodeIds: new Set(),
        showLinks: {{ coupling: true, inherit: true, partial: true, test: false }},
        
        // Caméra & Navigation
        cam: {{
            x: 0,
            y: 0,
            targetX: 0,
            targetY: 0,
            zoom: 1.0,
            targetZoom: 1.0,
            isDragging: false,
            dragStartX: 0,
            dragStartY: 0,
            camStartX: 0,
            camStartY: 0
        }}
    }};

    // 3. MOTEUR AUDIO PROCÉDURAL (WEB AUDIO API)
    const AudioEngine = {{
        ctx: null,
        masterGain: null,
        init() {{
            if (this.ctx) return;
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            if (!AudioContext) return;
            this.ctx = new AudioContext();
            this.masterGain = this.ctx.createGain();
            this.masterGain.gain.setValueAtTime(0.18, this.ctx.currentTime);
            this.masterGain.connect(this.ctx.destination);
        }},
        resume() {{
            if (this.ctx && this.ctx.state === 'suspended') {{
                this.ctx.resume();
            }}
        }},
        playChime(branchId) {{
            if (!state.audioEnabled) return;
            this.init();
            this.resume();
            if (!this.ctx) return;

            const now = this.ctx.currentTime;
            const baseChords = [
                [523.25, 659.25, 783.99], [440.00, 554.37, 659.25],
                [392.00, 493.88, 587.33], [587.33, 739.99, 880.00],
                [349.23, 440.00, 523.25], [659.25, 830.61, 987.77],
                [329.63, 415.30, 493.88]
            ];
            const freqs = {{}};
            Object.keys(branches).forEach((k, i) => {{
                const shift = Math.pow(2, Math.floor(i / baseChords.length) * 3 / 12);
                freqs[k] = baseChords[i % baseChords.length].map(f => +(f * shift).toFixed(2));
            }});

            const chord = freqs[branchId] || [440, 554, 659];
            chord.forEach((f, i) => {{
                const osc = this.ctx.createOscillator();
                const gain = this.ctx.createGain();
                
                osc.type = i === 0 ? 'sine' : 'triangle';
                osc.frequency.setValueAtTime(f, now + i * 0.04);

                gain.gain.setValueAtTime(0.001, now);
                gain.gain.exponentialRampToValueAtTime(0.07 / chord.length, now + i * 0.04 + 0.02);
                gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.8 + i * 0.1);

                osc.connect(gain);
                gain.connect(this.masterGain);

                osc.start(now + i * 0.04);
                osc.stop(now + 0.9 + i * 0.1);
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
            osc.frequency.setValueAtTime(800, now);
            osc.frequency.exponentialRampToValueAtTime(200, now + 0.04);

            gain.gain.setValueAtTime(0.03, now);
            gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.04);

            osc.connect(gain);
            gain.connect(this.masterGain);

            osc.start(now);
            osc.stop(now + 0.045);
        }}
    }};

    // 4. SYSTÈME DE PARTICULES & PULSATIONS
    const particles = [];
    const shockwaves = [];

    class ActionPotentialParticle {{
        constructor(path, color) {{
            this.path = path; // array of {{x, y}}
            this.color = color;
            this.progress = 0;
            this.speed = 0.008 + Math.random() * 0.012;
            this.size = 2.0 + Math.random() * 2.0;
        }}
        update() {{
            this.progress += this.speed;
            return this.progress < 1.0;
        }}
        draw(ctx) {{
            if (this.path.length < 2) return;
            const idx = Math.min(Math.floor(this.progress * (this.path.length - 1)), this.path.length - 2);
            const subT = (this.progress * (this.path.length - 1)) - idx;
            const p0 = this.path[idx];
            const p1 = this.path[idx + 1];
            const x = p0.x + (p1.x - p0.x) * subT;
            const y = p0.y + (p1.y - p0.y) * subT;

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
            this.maxRadius = 55;
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

    // Poussière céleste d'arrière-plan
    const cosmicDust = [];
    for (let i = 0; i < 90; i++) {{
        cosmicDust.push({{
            x: (Math.random() - 0.5) * 2400,
            y: (Math.random() - 0.5) * 2400,
            size: Math.random() * 1.8 + 0.4,
            alpha: Math.random() * 0.6 + 0.2,
            twinkleSpeed: Math.random() * 0.03 + 0.01,
            phase: Math.random() * Math.PI * 2
        }});
    }}

    // 5. CALCULS DES DISPOSITIONS (LAYOUT CALCULATOR)
    // Coordonnées cibles des nœuds pour chaque mode
    const layoutCoords = {{
        flower: {{}},
        neural: {{}},
        tree: {{}},
        force: {{}}
    }};

    function computeLayouts() {{
        // Pétales répartis uniformément entre les modules (14)
        const branchKeys = Object.keys(branches);
        const branchAngles = {{}};
        branchKeys.forEach((k, i) => {{ branchAngles[k] = (i / branchKeys.length) * Math.PI * 2 - Math.PI / 2; }});
        const branchOffset = {{}};
        const branchHemi = {{}};
        branchKeys.forEach((k, i) => {{
            const a = (i / branchKeys.length) * Math.PI * 2;
            branchOffset[k] = {{ x: Math.cos(a) * 680, y: Math.sin(a) * 560 }};
            branchHemi[k] = i % 2 === 0 ? 1 : -1;
        }});
        const branchSpread = {{}};
        branchKeys.forEach((k, i) => {{ branchSpread[k] = (i - (branchKeys.length - 1) / 2) * 95; }});
        const fallbackBranch = branchKeys[0];

        // Regrouper par branche
        const nodesByBranch = {{}};
        Object.keys(branches).forEach(b => nodesByBranch[b] = []);
        nodes.forEach(n => {{
            if (nodesByBranch[n.branch]) {{
                nodesByBranch[n.branch].push(n);
            }} else {{
                nodesByBranch[fallbackBranch].push(n);
            }}
        }});

        // --- MODE 1: FLEUR TEMPORELLE (Botanique & Pétales en Fibonacci) ---
        // Le nœud 1 est au cœur (0, 0)
        layoutCoords.flower[1] = {{ x: 0, y: 0 }};

        Object.keys(nodesByBranch).forEach(bKey => {{
            const bNodes = nodesByBranch[bKey];
            const baseAngle = branchAngles[bKey] || 0;
            const count = bNodes.length;

            bNodes.forEach((node, idx) => {{
                if (node.id === 1) return; // graine
                // Distance radiale grandissante (éventail élargi anti-tassement)
                const distRatio = Math.pow((idx + 1) / Math.max(count, 1), 0.85);
                const r = 150 + distRatio * 640;
                // Courbure organique en pétale (resserrée pour limiter le recouvrement)
                const curl = Math.sin((idx / Math.max(count, 1)) * Math.PI) * 0.18 + (idx * 0.02);
                const angle = baseAngle + curl;

                layoutCoords.flower[node.id] = {{
                    x: Math.cos(angle) * r,
                    y: Math.sin(angle) * r
                }};
            }});
        }});

        // --- MODE 2: RÉSEAU SYNAPTIQUE (Cortex Cérébral & Axones Rayonnants) ---
        layoutCoords.neural[1] = {{ x: 0, y: 0 }};
        nodes.forEach(node => {{
            if (node.id === 1) return;
            const bKey = node.branch;
            let hemisphere = branchHemi[bKey] ?? 1; // alternance droite / gauche

            const bOff = branchOffset[bKey] || {{ x: 0, y: 0 }};

            const jitterX = Math.sin(node.id * 1.7) * 45;
            const jitterY = Math.cos(node.id * 2.3) * 45;
            const progressionRatio = node.id / TOTAL_COMMITS;

            layoutCoords.neural[node.id] = {{
                x: bOff.x + jitterX + (hemisphere * progressionRatio * 110),
                y: bOff.y + jitterY + ((progressionRatio - 0.5) * 220)
            }};
        }});

        // --- MODE 3: ARBRE DE CAUSALITÉ (Fractal Chronologique Étage par Étage) ---
        // Nœud 1 aux racines en bas — hauteur proportionnelle au nombre de nœuds
        const totalHeight = Math.max(850, (TOTAL_COMMITS - 1) * 16);
        const startY = totalHeight / 2;
        nodes.forEach(node => {{
            const t = (node.id - 1) / (TOTAL_COMMITS - 1);
            const y = startY - (t * totalHeight);
            
            // Écartement horizontal selon le module
            const spread = branchSpread[node.branch] ?? 0;

            const sway = Math.sin(node.id * 0.6) * 45;
            layoutCoords.tree[node.id] = {{
                x: spread + sway,
                y: y
            }};
        }});

        // --- MODE 4: RHIZOME LIBRE (Force Initial Spacing) ---
        nodes.forEach(node => {{
            const ang = (node.id / TOTAL_COMMITS) * Math.PI * 2 + Math.random() * 0.4;
            const dist = 120 + (node.id / TOTAL_COMMITS) * 560;
            layoutCoords.force[node.id] = {{
                x: Math.cos(ang) * dist,
                y: Math.sin(ang) * dist,
                vx: 0,
                vy: 0
            }};
        }});

        // Détassement initial des trois layouts structurés (le rhizome reste libre)
        relaxTargets(layoutCoords.flower);
        relaxTargets(layoutCoords.neural);
        relaxTargets(layoutCoords.tree);
    }}

    computeLayouts();

    // Positions animées des nœuds en temps réel (interpolation fluide)
    const currentPositions = {{}};
    nodes.forEach(n => {{
        const initPos = layoutCoords.flower[n.id] || {{ x: 0, y: 0 }};
        currentPositions[n.id] = {{
            x: initPos.x,
            y: initPos.y,
            scale: n.id === 1 ? 1.0 : 0.0,
            bloomProgress: n.id === 1 ? 1.0 : 0.0,
            hasBloomed: n.id === 1,
            size: 0 // rayon lissé (lignes du fichier à la date affichée)
        }};
    }});

    // Taille d'un fichier à une date : LOC réelle après le dernier commit <= date
    function sizeAtDate(node, dateStr) {{
        const g = node.growth || [];
        let loc = g.length ? g[0][1] : (node.loc || 0);
        for (let i = 0; i < g.length; i++) {{
            if (g[i][0] <= dateStr) loc = g[i][1];
            else break;
        }}
        return loc;
    }}
    function radiusForLoc(loc) {{
        return Math.min(30, 3.0 + 0.30 * Math.sqrt(Math.max(0, loc)));
    }}

    // Détassement initial des cibles : relaxation libre intra-pétale avec
    // rayons pleine taille (stables dans le temps), bridée autour de l'ancre.
    // La répulsion continue (boucle de rendu) absorbe ensuite les résidus
    // quand les nœuds gonflent avec la date.
    function relaxTargets(layoutMap) {{
        const byBranch = {{}};
        Object.keys(layoutMap).forEach(id => {{
            const n = nodes[id - 1];
            if (!n) return;
            (byBranch[n.branch] = byBranch[n.branch] || []).push(+id);
        }});
        Object.values(byBranch).forEach(ids => {{
            const R = {{}};
            ids.forEach(id => {{ R[id] = radiusForLoc(nodes[id - 1].loc) + 5; }});
            for (let it = 0; it < 150; it++) {{
                for (let a = 0; a < ids.length; a++) {{
                    for (let b = a + 1; b < ids.length; b++) {{
                        const pa = layoutMap[ids[a]], pb = layoutMap[ids[b]];
                        const dx = pb.x - pa.x, dy = pb.y - pa.y;
                        const minD = R[ids[a]] + R[ids[b]];
                        const d2 = dx * dx + dy * dy;
                        if (d2 < minD * minD && d2 > 0.01) {{
                            const d = Math.sqrt(d2), p = (minD - d) / d * 0.5;
                            pa.x -= dx * p; pa.y -= dy * p;
                            pb.x += dx * p; pb.y += dy * p;
                        }}
                    }}
                }}
            }}
        }});
    }}

    // 6. INITIALISATION DU CANVAS
    const canvas = document.getElementById('neural-canvas');
    const ctx = canvas.getContext('2d');
    let width = 0;
    let height = 0;

    function resizeCanvas() {{
        const wrapper = document.getElementById('canvas-wrapper');
        const dpr = window.devicePixelRatio || 1;
        width = wrapper.clientWidth;
        height = wrapper.clientHeight;
        canvas.width = width * dpr;
        canvas.height = height * dpr;
        ctx.scale(dpr, dpr);
    }}
    window.addEventListener('resize', resizeCanvas);
    resizeCanvas();

    // 7. INTERACTION UTILISATEUR & ÉVÉNEMENTS CANVAS
    const tooltip = document.getElementById('node-tooltip');
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
            if (!state.activeBranches.has(n.branch)) continue;
            const pos = currentPositions[n.id];
            const dx = x - pos.x;
            const dy = y - pos.y;
            const hitRadius = ((currentPositions[n.id].size || 12) + 8) / state.cam.zoom + 6;
            if (dx * dx + dy * dy <= hitRadius * hitRadius) {{
                return n;
            }}
        }}
        return null;
    }}

    canvas.addEventListener('mousedown', e => {{
        if (e.button !== 0) return;
        state.cam.isDragging = true;
        state.cam.dragStartX = e.clientX;
        state.cam.dragStartY = e.clientY;
        state.cam.camStartX = state.cam.x;
        state.cam.camStartY = state.cam.y;
    }});

    window.addEventListener('mousemove', e => {{
        if (state.cam.isDragging) {{
            const dx = e.clientX - state.cam.dragStartX;
            const dy = e.clientY - state.cam.dragStartY;
            state.cam.targetX = state.cam.camStartX + dx;
            state.cam.targetY = state.cam.camStartY + dy;
            state.cam.x = state.cam.targetX;
            state.cam.y = state.cam.targetY;
        }} else {{
            const rect = canvas.getBoundingClientRect();
            const sx = e.clientX - rect.left;
            const sy = e.clientY - rect.top;
            const hovered = findNodeAtScreen(sx, sy);
            
            if (hovered !== state.hoveredNode) {{
                state.hoveredNode = hovered;
                if (hovered) {{
                    AudioEngine.playTick();
                    tooltip.style.display = 'block';
                    tooltip.style.left = e.clientX + 'px';
                    tooltip.style.top = e.clientY + 'px';
                    ttTitle.textContent = hovered.title;
                    let sub = `#${{hovered.id}} • créé le ${{hovered.date_display}} • ${{hovered.n_commits}} commits`;
                    if (hovered.hotspot) sub += ' • ⚠️ HOTSPOT';
                    if (hovered.tests && hovered.tests.length) sub += ` • 🧪 ${{hovered.tests.length}} test(s)`;
                    ttSub.textContent = sub;
                    canvas.style.cursor = 'pointer';
                }} else {{
                    tooltip.style.display = 'none';
                    canvas.style.cursor = 'grab';
                }}
            }} else if (hovered) {{
                tooltip.style.left = e.clientX + 'px';
                tooltip.style.top = e.clientY + 'px';
            }}
        }}
    }});

    window.addEventListener('mouseup', () => {{
        state.cam.isDragging = false;
    }});

    canvas.addEventListener('click', e => {{
        const rect = canvas.getBoundingClientRect();
        const sx = e.clientX - rect.left;
        const sy = e.clientY - rect.top;
        const clicked = findNodeAtScreen(sx, sy);
        if (clicked) {{
            selectNode(clicked);
        }}
    }});

    canvas.addEventListener('wheel', e => {{
        e.preventDefault();
        const zoomFactor = e.deltaY < 0 ? 1.15 : 0.87;
        const newZoom = Math.max(0.25, Math.min(3.5, state.cam.targetZoom * zoomFactor));
        state.cam.targetZoom = newZoom;
    }}, {{ passive: false }});

    // 8. INSPECTEUR DE NŒUD CAUSAL
    const drawer = document.getElementById('inspector-drawer');
    const drawerBranchTag = document.getElementById('drawerBranchTag');
    const drawerCommitNum = document.getElementById('drawerCommitNum');
    const drawerTitle = document.getElementById('drawerTitle');
    const drawerAuthor = document.getElementById('drawerAuthor');
    const drawerHash = document.getElementById('drawerHash');
    const drawerDiff = document.getElementById('drawerDiff');
    const drawerFilesCount = document.getElementById('drawerFilesCount');
    const drawerSummary = document.getElementById('drawerSummary');
    const drawerFilesList = document.getElementById('drawerFilesList');
    const drawerFileCategoryCount = document.getElementById('drawerFileCategoryCount');
    const drawerCausalList = document.getElementById('drawerCausalList');
    const drawerGithubBtn = document.getElementById('drawerGithubBtn');

    function selectNode(node) {{
        state.selectedNode = node;
        AudioEngine.playChime(node.branch);

        // Centrage fluide de la caméra sur le nœud
        const pos = currentPositions[node.id];
        if (pos) {{
            state.cam.targetX = -pos.x * state.cam.zoom;
            state.cam.targetY = -pos.y * state.cam.zoom;
        }}

        // Mise à jour de l'inspecteur
        const bInfo = branches[node.branch] || {{ label: node.branch, color: '#38bdf8' }};
        drawerBranchTag.textContent = `${{bInfo.icon || '◆'}} ${{bInfo.label}}`;
        drawerBranchTag.style.background = `${{bInfo.color}}26`;
        drawerBranchTag.style.color = bInfo.color;
        drawerBranchTag.style.border = `1px solid ${{bInfo.color}}66`;

        drawerCommitNum.textContent = `FICHIER #${{node.id}} • créé le ${{node.date_display}}`;
        drawerTitle.textContent = node.title;
        drawerAuthor.textContent = node.author;
        drawerHash.textContent = node.first_short || node.short;
        drawerDiff.textContent = `+${{node.insertions}} / -${{node.deletions}}`;
        drawerFilesCount.textContent = `${{node.files_count}} commits`;
        drawerSummary.textContent = node.summary;

        // Historique des commits du fichier
        drawerFilesList.innerHTML = '';
        if (node.files && node.files.length > 0) {{
            drawerFileCategoryCount.textContent = `${{node.files.length}} affichés / ${{node.files_count}}`;
            node.files.forEach(f => {{
                const li = document.createElement('li');
                li.className = 'file-item';
                const badgeClass = `badge-${{f.type}}`;
                li.innerHTML = `
                    <span style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 220px;" title="${{f.path}}">${{f.name}}</span>
                    <span class="file-badge ${{badgeClass}}">commit</span>
                `;
                drawerFilesList.appendChild(li);
            }});
        }} else {{
            drawerFileCategoryCount.textContent = 'Aucun commit';
            drawerFilesList.innerHTML = '<li class="file-item" style="color:var(--text-muted)">Historique non retracé</li>';
        }}

        // Relations du fichier : toutes couches confondues (clic = saut)
        drawerCausalList.innerHTML = '';
        const relations = [];
        const pushRel = (cl, icon, text) => {{
            const otherId = cl.source === node.id ? cl.target : cl.source;
            const otherNode = nodes.find(n => n.id === otherId);
            if (otherNode) relations.push({{ cl, otherNode, icon, text }});
        }};
        cross_links.filter(cl => cl.source === node.id || cl.target === node.id)
            .forEach(cl => pushRel(cl, '🔗', `<strong>${{nodes.find(n => n.id === (cl.source === node.id ? cl.target : cl.source)).title}}</strong> <em style="opacity:.7;font-size:11px;">(${{cl.reason}})</em>`));
        inherit_links.filter(cl => cl.source === node.id || cl.target === node.id)
            .forEach(cl => pushRel(cl, '🧬', cl.source === node.id
                ? `hérite de <strong>${{nodes.find(n => n.id === cl.target).title}}</strong>`
                : `parent de <strong>${{nodes.find(n => n.id === cl.source).title}}</strong>`));
        partial_links.filter(cl => cl.source === node.id || cl.target === node.id)
            .forEach(cl => pushRel(cl, '🧩', cl.source === node.id
                ? `partiel de <strong>${{nodes.find(n => n.id === cl.target).title}}</strong>`
                : `agrège <strong>${{nodes.find(n => n.id === cl.source).title}}</strong>`));
        test_links.filter(cl => cl.source === node.id || cl.target === node.id)
            .forEach(cl => pushRel(cl, '🧪', cl.source === node.id
                ? `teste <strong>${{nodes.find(n => n.id === cl.target).title}}</strong>`
                : `testé par <strong>${{nodes.find(n => n.id === cl.source).title}}</strong>`));
        if (relations.length > 0) {{
            relations.slice(0, 24).forEach(r => {{
                const btn = document.createElement('button');
                btn.className = 'causal-jump-btn';
                btn.title = r.cl.reason || '';
                btn.innerHTML = `
                    <span>${{r.icon}} ${{r.text}}</span>
                    <span style="font-family:var(--font-mono); font-size:10px; color:#38bdf8;">#${{r.otherNode.id}}</span>
                `;
                btn.addEventListener('click', () => selectNode(r.otherNode));
                drawerCausalList.appendChild(btn);
            }});
            if (relations.length > 24) {{
                const more = document.createElement('div');
                more.style.cssText = 'font-size:11px;color:var(--text-muted);text-align:center;';
                more.textContent = `+ ${{relations.length - 24}} autres relations`;
                drawerCausalList.appendChild(more);
            }}
        }} else {{
            drawerCausalList.innerHTML = '<div style="font-size:12px; color:var(--text-muted);">Aucune relation marquée — fichier indépendant.</div>';
        }}

        // Lien GitHub figé sur le hash du dernier commit (jamais de 404) ;
        // les fichiers non commités n'ont pas de lien (bouton masqué).
        if (node.last_hash && node.repo_path) {{
            drawerGithubBtn.style.display = '';
            drawerGithubBtn.href = `https://github.com/k0r0z1f/Killtime/blob/${{node.last_hash}}/${{node.repo_path}}`;
            drawerGithubBtn.querySelector('span').textContent = `Voir le fichier @ ${{node.last_short || node.last_hash.slice(0, 7)}} sur GitHub ↗`;
        }} else {{
            drawerGithubBtn.style.display = 'none';
        }}
        refreshSizeNow();

        // Bannière état worktree (modifié / non commité)
        const wtEl = document.getElementById('drawerWorktree');
        if (node.worktree === 'modified') {{
            wtEl.style.display = '';
            wtEl.innerHTML = `✎ <strong>MODIFIÉ hors commit</strong> — GitHub montre la version ${{node.last_short || 'commitée'}} ; les chiffres reflètent le disque.`;
        }} else if (node.worktree === 'untracked') {{
            wtEl.style.display = '';
            wtEl.innerHTML = `🆕 <strong>NON COMMITÉ</strong> — absent de GitHub pour l'instant ; sera lié après commit.`;
        }} else {{
            wtEl.style.display = 'none';
        }}

        // Bannière hotspot (gros x instable)
        const hsEl = document.getElementById('drawerHotspot');
        if (node.hotspot) {{
            hsEl.style.display = '';
            hsEl.innerHTML = `⚠️ <strong>HOTSPOT</strong> — ${{node.n_commits}} commits × ${{node.loc}} lignes : zone à risque, candidate à la surveillance ou au découpage.`;
        }} else {{
            hsEl.style.display = 'none';
        }}

        // Tests couvrant ce fichier (saut direct)
        const twEl = document.getElementById('drawerTestsWrap');
        const tlEl = document.getElementById('drawerTestsList');
        tlEl.innerHTML = '';
        if (node.tests && node.tests.length > 0) {{
            twEl.style.display = '';
            node.tests.forEach(t => {{
                const b = document.createElement('button');
                b.className = 'causal-jump-btn';
                b.innerHTML = `
                    <span>🧪 <strong>${{t.name}}</strong></span>
                    <span style="font-family:var(--font-mono); font-size:10px; color:#34d399;">#${{t.id}}</span>
                `;
                b.addEventListener('click', () => {{
                    const tn = nodes.find(n => n.id === t.id);
                    if (tn) selectNode(tn);
                }});
                tlEl.appendChild(b);
            }});
        }} else {{
            twEl.style.display = 'none';
        }}

        drawer.classList.add('open');
    }}

    // Taille du fichier sélectionné à la date affichée sur la timeline
    function refreshSizeNow() {{
        const el = document.getElementById('drawerSizeNow');
        if (!el) return;
        if (!state.selectedNode) return;
        const d = (nodes[state.currentCommitIndex - 1] || nodes[0]).date_display;
        el.textContent = `📏 ${{sizeAtDate(state.selectedNode, (nodes[state.currentCommitIndex - 1] || nodes[0]).date).toLocaleString('fr-FR')}} lignes au ${{d}} (sur ${{(state.selectedNode.loc || 0).toLocaleString('fr-FR')}} actuelles).`;
    }}

    document.getElementById('drawerCloseBtn').addEventListener('click', () => {{
        drawer.classList.remove('open');
        state.selectedNode = null;
    }});

    // 9. CONTRÔLE DE LA CHRONOLOGIE & SCRUBBER
    const slider = document.getElementById('timelineSlider');
    const playPauseBtn = document.getElementById('playPauseBtn');
    const playIcon = document.getElementById('play-icon');
    const hudCurrentDate = document.getElementById('hudCurrentDate');
    const hudActiveEpoch = document.getElementById('hudActiveEpoch');
    const hudActiveCommitIndex = document.getElementById('hudActiveCommitIndex');

    function updateTimelineUI(commitIdx) {{
        state.currentCommitIndex = commitIdx;
        slider.value = commitIdx;
        hudActiveCommitIndex.textContent = commitIdx;

        const currNode = nodes[commitIdx - 1];
        if (currNode) {{
            hudCurrentDate.textContent = currNode.date_display;
            const ep = epochs.find(e => e.id === currNode.epoch);
            if (ep) {{
                hudActiveEpoch.textContent = ep.title;
            }}
        }}

        // Déclenchement floraison et sons si avancement
        for (let i = 0; i < commitIdx; i++) {{
            const n = nodes[i];
            const p = currentPositions[n.id];
            if (!p.hasBloomed) {{
                p.hasBloomed = true;
                shockwaves.push(new BloomShockwave(p.x, p.y, branches[n.branch]?.color || '#38bdf8'));
                if (state.isPlaying) {{
                    AudioEngine.playChime(n.branch);
                }}
            }}
        }}

        // Réinitialiser les nœuds après le commit actif si on recule
        for (let i = commitIdx; i < nodes.length; i++) {{
            const n = nodes[i];
            currentPositions[n.id].hasBloomed = false;
        }}

        // Actualiser la taille affichée dans l'inspecteur (date courante)
        refreshSizeNow();
    }}

    slider.addEventListener('input', e => {{
        const val = parseInt(e.target.value, 10);
        updateTimelineUI(val);
    }});

    function togglePlay() {{
        state.isPlaying = !state.isPlaying;
        playIcon.textContent = state.isPlaying ? '❚❚' : '▶';
        if (state.isPlaying) {{
            AudioEngine.resume();
            if (state.currentCommitIndex >= TOTAL_COMMITS) {{
                updateTimelineUI(1);
            }}
        }}
    }}
    playPauseBtn.addEventListener('click', togglePlay);

    document.getElementById('btnStepForward').addEventListener('click', () => {{
            if (state.currentCommitIndex < TOTAL_COMMITS) updateTimelineUI(state.currentCommitIndex + 1);
    }});
    document.getElementById('btnStepBack').addEventListener('click', () => {{
        if (state.currentCommitIndex > 1) updateTimelineUI(state.currentCommitIndex - 1);
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

    // Sélecteur de vitesse
    document.querySelectorAll('.speed-btn').forEach(btn => {{
        btn.addEventListener('click', () => {{
            document.querySelectorAll('.speed-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            state.playbackSpeed = parseFloat(btn.dataset.speed);
        }});
    }});

    // Sélecteur de mode de vue
    document.querySelectorAll('.hud-view-modes .mode-btn').forEach(btn => {{
        btn.addEventListener('click', () => {{
            document.querySelectorAll('.hud-view-modes .mode-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            state.viewMode = btn.dataset.mode;
            AudioEngine.playTick();
        }});
    }});

    // Interrupteurs des couches de liaisons
    document.querySelectorAll('.link-toggle').forEach(btn => {{
        btn.addEventListener('click', () => {{
            const k = btn.dataset.layer;
            state.showLinks[k] = !state.showLinks[k];
            btn.classList.toggle('active', state.showLinks[k]);
            AudioEngine.playTick();
        }});
    }});

    // Marqueurs d'époques cliquables
    const epochBar = document.getElementById('epochMarkersBar');
    epochs.forEach((ep, idx) => {{
        const span = document.createElement('span');
        span.className = 'epoch-mark';
        span.textContent = `Époque ${{idx + 1}}`;
        span.title = `${{ep.title}} (${{ep.start}} à ${{ep.end}})`;
        span.addEventListener('click', () => {{
            const firstNodeInEpoch = nodes.find(n => n.epoch === ep.id);
            if (firstNodeInEpoch) {{
                updateTimelineUI(firstNodeInEpoch.id);
            }}
        }});
        epochBar.appendChild(span);
    }});

    // Pilules de filtrage des branches
    const branchPillsBar = document.getElementById('branchPillsBar');
    Object.keys(branches).forEach(bKey => {{
        const b = branches[bKey];
        const btn = document.createElement('button');
        btn.className = 'branch-pill active';
        btn.innerHTML = `<span class="branch-dot" style="background:${{b.color}}"></span><span>${{b.label}}</span>`;
        btn.addEventListener('click', () => {{
            if (state.activeBranches.has(bKey)) {{
                if (state.activeBranches.size > 1) state.activeBranches.delete(bKey);
            }} else {{
                state.activeBranches.add(bKey);
            }}
            btn.classList.toggle('active', state.activeBranches.has(bKey));
        }});
        branchPillsBar.appendChild(btn);
    }});

    // Recherche de commits en direct
    const searchInput = document.getElementById('commitSearchInput');
    searchInput.addEventListener('input', e => {{
        const q = e.target.value.toLowerCase().trim();
        state.searchQuery = q;
        state.matchedNodeIds.clear();
        if (q.length > 0) {{
            nodes.forEach(n => {{
                const matchTitle = n.title.toLowerCase().includes(q);
                const matchSummary = n.summary.toLowerCase().includes(q);
                const matchHash = n.short.toLowerCase().includes(q);
                const matchFiles = n.files.some(f => f.name.toLowerCase().includes(q) || f.path.toLowerCase().includes(q));
                if (matchTitle || matchSummary || matchHash || matchFiles) {{
                    state.matchedNodeIds.add(n.id);
                }}
            }});
        }}
    }});

    // Contrôles de caméra
    document.getElementById('btnZoomIn').addEventListener('click', () => {{
        state.cam.targetZoom = Math.min(3.5, state.cam.targetZoom * 1.3);
    }});
    document.getElementById('btnZoomOut').addEventListener('click', () => {{
        state.cam.targetZoom = Math.max(0.25, state.cam.targetZoom * 0.75);
    }});
    document.getElementById('btnResetView').addEventListener('click', () => {{
        state.cam.targetX = 0;
        state.cam.targetY = 0;
        state.cam.targetZoom = 1.0;
    }});
    document.getElementById('btnToggleBloomAll').addEventListener('click', () => {{
        updateTimelineUI(TOTAL_COMMITS);
    }});

    // Audio Toggle
    const audioToggleBtn = document.getElementById('audioToggleBtn');
    const audioIcon = document.getElementById('audio-icon');
    audioToggleBtn.addEventListener('click', () => {{
        state.audioEnabled = !state.audioEnabled;
        audioToggleBtn.classList.toggle('active', state.audioEnabled);
        audioIcon.textContent = state.audioEnabled ? '🔊' : '🔇';
    }});

    // Help Modal
    const helpModal = document.getElementById('helpModalBtn');
    const modalEl = document.getElementById('help-modal');
    helpModal.addEventListener('click', () => modalEl.style.display = 'flex');
    document.getElementById('helpModalCloseBtn').addEventListener('click', () => modalEl.style.display = 'none');
    modalEl.addEventListener('click', e => {{
        if (e.target === modalEl) modalEl.style.display = 'none';
    }});

    // Raccourcis clavier
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
        }}
    }});

    // 10. BOUCLE DE RENDU CANVAS PRINCIPALE
    let lastTime = performance.now();
    let playAccumulator = 0;

    function renderLoop(now) {{
        const dt = (now - lastTime) / 1000;
        lastTime = now;

        // Lecture temporelle automatique
        if (state.isPlaying) {{
            playAccumulator += dt * state.playbackSpeed;
            if (playAccumulator >= 0.35) {{ // un fichier toutes les 0.35s à vitesse 1x
                playAccumulator = 0;
                if (state.currentCommitIndex < TOTAL_COMMITS) {{
                    updateTimelineUI(state.currentCommitIndex + 1);
                }} else {{
                    state.isPlaying = false;
                    playIcon.textContent = '▶';
                }}
            }}
        }}

        // Mise à jour de la caméra (inertie fluide)
        state.cam.x += (state.cam.targetX - state.cam.x) * 0.12;
        state.cam.y += (state.cam.targetY - state.cam.y) * 0.12;
        state.cam.zoom += (state.cam.targetZoom - state.cam.zoom) * 0.12;

        // Mise à jour des positions des nœuds vers la cible du mode actif
        const targetLayout = layoutCoords[state.viewMode] || layoutCoords.flower;
        const breathWave = Math.sin(now * 0.0018) * 6;
        const nowDateStr = (nodes[state.currentCommitIndex - 1] || nodes[0]).date;
        // En rhizome libre, l'ancre est faible : la répulsion domine et démêle
        const anchorK = state.viewMode === 'force' ? 0.03 : 0.09;

        nodes.forEach(n => {{
            const target = targetLayout[n.id] || {{ x: 0, y: 0 }};
            const curr = currentPositions[n.id];
            
            // Ondulation organique de respiration
            const sway = Math.sin(now * 0.0015 + n.id * 0.4) * 4;
            const destX = target.x + (n.id === 1 ? 0 : sway);
            const destY = target.y + (n.id === 1 ? 0 : breathWave * 0.5);

            curr.x += (destX - curr.x) * anchorK;
            curr.y += (destY - curr.y) * anchorK;

            // Éclosion / Scale du nœud selon la visibilité
            const isVisible = n.id <= state.currentCommitIndex;
            const targetScale = isVisible ? 1.0 : 0.0;
            curr.scale += (targetScale - curr.scale) * 0.15;

            // Taille organique : lignes réelles du fichier à la date affichée
            const targetSize = radiusForLoc(sizeAtDate(n, nowDateStr));
            curr.size += (targetSize - curr.size) * 0.12;
        }});

        // Anti-collision : répulsion douce entre nœuds visibles (rayons dynamiques).
        // L'attraction vers les cibles du layout (0.09/frame) fait contrepoids :
        // les nœuds se décalent juste assez pour ne plus se recouvrir, sans dérive.
        const sepList = [];
        for (let si = 0; si < nodes.length; si++) {{
            const sn = nodes[si];
            if (sn.id > state.currentCommitIndex) continue;
            if (!state.activeBranches.has(sn.branch)) continue;
            if (currentPositions[sn.id].scale < 0.4) continue;
            sepList.push(sn);
        }}
        for (let sa = 0; sa < sepList.length; sa++) {{
            const na = sepList[sa], pa = currentPositions[na.id];
            const ra = (pa.size || 9) + 3;
            for (let sb = sa + 1; sb < sepList.length; sb++) {{
                const nb = sepList[sb], pb = currentPositions[nb.id];
                const dx0 = pb.x - pa.x, dy0 = pb.y - pa.y;
                const minD = ra + (pb.size || 9) + 3;
                const d2 = dx0 * dx0 + dy0 * dy0;
                if (d2 < minD * minD && d2 > 0.01) {{
                    const d = Math.sqrt(d2);
                    const push = (minD - d) / d * 0.3;
                    const px = dx0 * push, py = dy0 * push;
                    pa.x -= px; pa.y -= py;
                    pb.x += px; pb.y += py;
                }}
            }}
        }}

        // Génération de paquets d'influx nerveux (action potentials)
        if (Math.random() < 0.12 && state.currentCommitIndex > 1) {{
            // Choisir un lien actif au hasard
            const activeLinks = links.filter(l => l.target <= state.currentCommitIndex);
            if (activeLinks.length > 0) {{
                const rLink = activeLinks[Math.floor(Math.random() * activeLinks.length)];
                const p0 = currentPositions[rLink.source];
                const p1 = currentPositions[rLink.target];
                const bCol = branches[nodes[rLink.target - 1]?.branch]?.color || '#38bdf8';
                particles.push(new ActionPotentialParticle([p0, p1], bCol));
            }}
        }}

        // Nettoyage canvas
        ctx.clearRect(0, 0, width, height);

        // Transformation globale de caméra
        ctx.save();
        ctx.translate(width / 2 + state.cam.x, height / 2 + state.cam.y);
        ctx.scale(state.cam.zoom, state.cam.zoom);

        // --- 1. RENDU DE LA POUSSIÈRE COSMIQUE ---
        cosmicDust.forEach(star => {{
            const twinkle = Math.sin(now * star.twinkleSpeed + star.phase);
            ctx.fillStyle = `rgba(186, 215, 255, ${{star.alpha * (0.6 + 0.4 * twinkle)}})`;
            ctx.beginPath();
            ctx.arc(star.x, star.y, star.size, 0, Math.PI * 2);
            ctx.fill();
        }});

        // --- 2. RENDU DU CŒUR TEMPOREL (LA GRAINE : FICHIER #1) ---
        const seedPos = currentPositions[1];
        if (seedPos) {{
            const seedPulse = 18 + Math.sin(now * 0.003) * 4;
            // Halo externe
            const radGrad = ctx.createRadialGradient(seedPos.x, seedPos.y, 4, seedPos.x, seedPos.y, seedPulse * 3.5);
            radGrad.addColorStop(0, 'rgba(56, 189, 248, 0.45)');
            radGrad.addColorStop(0.5, 'rgba(192, 132, 252, 0.2)');
            radGrad.addColorStop(1, 'rgba(56, 189, 248, 0)');
            ctx.fillStyle = radGrad;
            ctx.beginPath();
            ctx.arc(seedPos.x, seedPos.y, seedPulse * 3.5, 0, Math.PI * 2);
            ctx.fill();

            // Anneaux concentriques d'énergie
            ctx.strokeStyle = 'rgba(56, 189, 248, 0.3)';
            ctx.lineWidth = 1.5;
            ctx.beginPath();
            ctx.arc(seedPos.x, seedPos.y, seedPulse * 1.8, 0, Math.PI * 2);
            ctx.stroke();
        }}

        // --- 3. RENDU DES BRANCHES & VRILLES FLORALES (BEZIER SPLINES) ---
        // Regrouper les nœuds visibles par branche
        Object.keys(branches).forEach(bKey => {{
            if (!state.activeBranches.has(bKey)) return;
            const bInfo = branches[bKey];
            const bNodes = nodes.filter(n => n.branch === bKey && n.id <= state.currentCommitIndex);
            if (bNodes.length === 0) return;

            ctx.save();
            ctx.strokeStyle = bInfo.color;
            ctx.shadowColor = bInfo.color;
            ctx.shadowBlur = 12;
            ctx.lineWidth = 2.4;
            ctx.globalAlpha = 0.55;

            ctx.beginPath();
            ctx.moveTo(seedPos.x, seedPos.y);

            // Relier successivement les nœuds de la même branche
            for (let i = 0; i < bNodes.length; i++) {{
                const p = currentPositions[bNodes[i].id];
                if (i === 0) {{
                    // Courbe depuis la graine
                    const cpx = (seedPos.x + p.x) / 2 + Math.sin(bNodes[i].id) * 30;
                    const cpy = (seedPos.y + p.y) / 2;
                    ctx.quadraticCurveTo(cpx, cpy, p.x, p.y);
                }} else {{
                    const prevP = currentPositions[bNodes[i - 1].id];
                    const cpx = (prevP.x + p.x) / 2;
                    const cpy = (prevP.y + p.y) / 2;
                    ctx.quadraticCurveTo(cpx, cpy, p.x, p.y);
                }}
            }}
            ctx.stroke();
            ctx.restore();
        }});

        // --- 4. RENDU DES LIENS CHRONOLOGIQUES & SYNAPSES CAUSALES ---
        // Couches de liaisons commutables : synapses, héritage, partials, tests
        const LAYERS = [
            {{ key: 'coupling', list: cross_links, color: 'rgba(192, 132, 252, 0.35)', hot: '#38bdf8', baseW: 0.8, wPer: 0.22, wMax: 4, dash: [4, 4], lift: 35, arrow: false, alpha: 1.0 }},
            {{ key: 'inherit', list: inherit_links, color: 'rgba(125, 211, 252, 0.55)', hot: '#7dd3fc', baseW: 1.6, wPer: 0, wMax: 0, dash: [], lift: 60, arrow: true, alpha: 0.95 }},
            {{ key: 'partial', list: partial_links, color: 'rgba(45, 212, 191, 0.5)', hot: '#2dd4bf', baseW: 1.2, wPer: 0, wMax: 0, dash: [2, 4], lift: 20, arrow: false, alpha: 0.9 }},
            {{ key: 'test', list: test_links, color: 'rgba(52, 211, 153, 0.4)', hot: '#34d399', baseW: 1.1, wPer: 0, wMax: 0, dash: [5, 5], lift: 48, arrow: false, alpha: 0.55 }}
        ];
        LAYERS.forEach(L => {{
            if (!state.showLinks[L.key]) return;
            L.list.forEach(cl => {{
                if (cl.source > state.currentCommitIndex || cl.target > state.currentCommitIndex) return;
                const sourceNode = nodes[cl.source - 1];
                const targetNode = nodes[cl.target - 1];
                if (!sourceNode || !targetNode) return;
                if (!state.activeBranches.has(sourceNode.branch) || !state.activeBranches.has(targetNode.branch)) return;

                const p0 = currentPositions[cl.source];
                const p1 = currentPositions[cl.target];
                const isHoveredOrSelected = (state.hoveredNode && (state.hoveredNode.id === cl.source || state.hoveredNode.id === cl.target)) ||
                                            (state.selectedNode && (state.selectedNode.id === cl.source || state.selectedNode.id === cl.target));

                ctx.save();
                ctx.globalAlpha = L.alpha;
                const wExtra = L.wPer ? Math.min(L.wMax, (cl.weight || 2) * L.wPer) : 0;
                ctx.strokeStyle = isHoveredOrSelected ? L.hot : L.color;
                ctx.lineWidth = L.baseW + wExtra + (isHoveredOrSelected ? 1.0 : 0);
                ctx.setLineDash(isHoveredOrSelected ? [] : L.dash);
                if (isHoveredOrSelected) {{
                    ctx.shadowColor = L.hot;
                    ctx.shadowBlur = 10;
                }}

                ctx.beginPath();
                ctx.moveTo(p0.x, p0.y);
                // Courbe arquée (hauteur propre à chaque couche pour limiter les recouvrements)
                const midX = (p0.x + p1.x) / 2;
                const midY = (p0.y + p1.y) / 2 - L.lift;
                ctx.quadraticCurveTo(midX, midY, p1.x, p1.y);
                ctx.stroke();
                // Pointe de flèche vers le parent (couche héritage)
                if (L.arrow) {{
                    const ang = Math.atan2(p1.y - midY, p1.x - midX);
                    const s = 7;
                    ctx.beginPath();
                    ctx.moveTo(p1.x, p1.y);
                    ctx.lineTo(p1.x - s * Math.cos(ang - 0.42), p1.y - s * Math.sin(ang - 0.42));
                    ctx.moveTo(p1.x, p1.y);
                    ctx.lineTo(p1.x + s * Math.cos(ang + 0.42), p1.y - s * Math.sin(ang + 0.42));
                    ctx.stroke();
                }}
                ctx.restore();
            }});
        }});

        // --- 5. PARTICULES EN VOYAGE SUR LES BRANCHES ---
        for (let i = particles.length - 1; i >= 0; i--) {{
            if (!particles[i].update()) {{
                particles.splice(i, 1);
            }} else {{
                particles[i].draw(ctx);
            }}
        }}

        // --- 6. ONDES DE CHOC (SHOCKWAVES DE FLORAISON) ---
        for (let i = shockwaves.length - 1; i >= 0; i--) {{
            if (!shockwaves[i].update()) {{
                shockwaves.splice(i, 1);
            }} else {{
                shockwaves[i].draw(ctx);
            }}
        }}

        // --- 7. RENDU DES NŒUDS DE COMMITS (BOURGEONS & FLEURS) ---
        nodes.forEach(node => {{
            if (node.id > state.currentCommitIndex) return;
            const bKey = node.branch;
            const isBranchActive = state.activeBranches.has(bKey);
            const bInfo = branches[bKey] || {{ color: '#38bdf8' }};
            const pos = currentPositions[node.id];
            if (pos.scale <= 0.01) return;

            const isSelected = state.selectedNode && state.selectedNode.id === node.id;
            const isHovered = state.hoveredNode && state.hoveredNode.id === node.id;
            const isSearchMatch = state.searchQuery.length > 0 && state.matchedNodeIds.has(node.id);
            const isSearchMuted = state.searchQuery.length > 0 && !isSearchMatch;

            ctx.save();
            ctx.translate(pos.x, pos.y);
            ctx.scale(pos.scale, pos.scale);

            let nodeAlpha = isBranchActive ? 1.0 : 0.15;
            if (isSearchMuted) nodeAlpha = 0.12;
            ctx.globalAlpha = nodeAlpha;

            // Déterminer la taille : rayon lissé = lignes à la date affichée
            let baseRadius = currentPositions[node.id].size || radiusForLoc(node.loc);
            if (node.milestone) baseRadius += 3;
            if (node.id === 1) baseRadius = Math.max(baseRadius, 15);
            if (isSelected || isHovered) baseRadius *= 1.35;
            if (isSearchMatch) baseRadius *= 1.4;

            // Halo lumineux
            const glowColor = isSearchMatch ? '#fbbf24' : (isSelected ? '#ffffff' : bInfo.color);
            ctx.shadowColor = glowColor;
            ctx.shadowBlur = (isSelected || isHovered || isSearchMatch) ? 22 : 10;

            // Halo de churn : pulsation orange proportionnelle à l'instabilité
            const churnN = node.n_commits || 0;
            if (!isSearchMuted && churnN >= 2 && pos.scale > 0.3) {{
                const heat = Math.min(1, churnN / 14);
                const pulse = 0.5 + 0.5 * Math.sin(now * 0.004 + node.id * 1.3);
                ctx.save();
                ctx.globalAlpha = nodeAlpha * (0.15 + 0.4 * heat * (0.4 + 0.6 * pulse));
                ctx.strokeStyle = '#fb923c';
                ctx.lineWidth = 1.5 + 3.5 * heat;
                ctx.beginPath();
                ctx.arc(0, 0, baseRadius + 5 + 4 * heat, 0, Math.PI * 2);
                ctx.stroke();
                ctx.restore();
            }}

            // Cercle externe / Pétale
            ctx.fillStyle = glowColor;
            ctx.beginPath();
            ctx.arc(0, 0, baseRadius, 0, Math.PI * 2);
            ctx.fill();

            // Cœur du nœud
            ctx.fillStyle = '#060a14';
            ctx.beginPath();
            ctx.arc(0, 0, baseRadius * 0.65, 0, Math.PI * 2);
            ctx.fill();

            // Point central brillant
            ctx.fillStyle = isSelected ? '#ffffff' : glowColor;
            ctx.beginPath();
            ctx.arc(0, 0, baseRadius * 0.35, 0, Math.PI * 2);
            ctx.fill();

            // Couronne florale pour les milestones
            if (node.milestone && !isSearchMuted) {{
                ctx.strokeStyle = '#ffffff';
                ctx.lineWidth = 1.6;
                ctx.setLineDash([3, 3]);
                ctx.beginPath();
                ctx.arc(0, 0, baseRadius + 6, 0, Math.PI * 2);
                ctx.stroke();
                ctx.setLineDash([]);
            }}

            // Anneau hotspot : fourmis rouges en marche (gros x instable)
            if (node.hotspot && !isSearchMuted) {{
                ctx.save();
                ctx.globalAlpha = nodeAlpha * 0.95;
                ctx.strokeStyle = '#f43f5e';
                ctx.lineWidth = 2;
                ctx.setLineDash([7, 5]);
                ctx.lineDashOffset = -now * 0.02;
                ctx.shadowColor = '#f43f5e';
                ctx.shadowBlur = 8;
                ctx.beginPath();
                ctx.arc(0, 0, baseRadius + 11, 0, Math.PI * 2);
                ctx.stroke();
                ctx.restore();
            }}

            // Lunes de tests : satellites verts en orbite (fichiers testés)
            const moons = node.tests || [];
            const nMoons = Math.min(moons.length, 3);
            for (let mi = 0; mi < nMoons; mi++) {{
                if (pos.scale < 0.5) break;
                const ang = now * 0.0012 * (mi % 2 ? -1 : 1) + node.id * 0.7 + mi * 2.1;
                const orb = baseRadius + 11 + (node.hotspot ? 6 : 0);
                ctx.save();
                ctx.globalAlpha = nodeAlpha;
                ctx.fillStyle = '#34d399';
                ctx.shadowColor = '#34d399';
                ctx.shadowBlur = 8;
                ctx.beginPath();
                ctx.arc(Math.cos(ang) * orb, Math.sin(ang) * orb, 2.6, 0, Math.PI * 2);
                ctx.fill();
                ctx.restore();
            }}

            // Libellé court
            if (state.cam.zoom > 0.85 || isSelected || isHovered || node.milestone || isSearchMatch) {{
                ctx.font = '600 11px "JetBrains Mono", monospace';
                ctx.fillStyle = isSelected ? '#ffffff' : '#cbd5e1';
                ctx.shadowBlur = 4;
                ctx.textAlign = 'center';
                const labelText = node.milestone ? `★ #${{node.id}}` : `#${{node.id}}`;
                ctx.fillText(labelText, 0, baseRadius + 14);
            }}

            ctx.restore();
        }});

        ctx.restore();

        requestAnimationFrame(renderLoop);
    }}

    // Démarrage de la boucle d'animation
    requestAnimationFrame(renderLoop);

    // Initialisation : afficher le dernier commit par défaut si souhaité, ou démarrer la graine
    updateTimelineUI(TOTAL_COMMITS); // Montre l'arbre complet au chargement pour un effet WOW immédiat !

}})();
</script>
</body>
</html>
'''
    with open('tactics_fleur_fichiers.html', 'w', encoding='utf-8') as f:
        f.write(html_template)
    print("tactics_fleur_fichiers.html généré avec succès!")

if __name__ == '__main__':
    build_html()
