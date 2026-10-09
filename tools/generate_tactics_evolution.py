#!/usr/bin/env python3
"""
Générateur de la page d'animation neuronale et florale pour l'évolution de Killtime Tactics.
Produit tactics_evolution.html avec dataset intégré et moteur canvas interactif.
"""

import json, os

def build_html():
    with open('data/tactics_evolution.json', 'r', encoding='utf-8') as f:
        tactics_json_str = f.read()

    html_template = f'''<!DOCTYPE html>
<html lang="fr" data-theme="dark">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Killtime Tactics — L'Arbre Causal & Fleur Neuronale</title>
    <meta name="description" content="Animation organique et réseau de neurones de l'évolution du jeu Killtime Tactics : 53 commits, branches florales, synapses causales et chronologie dynamique.">

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
                    <h1>Killtime Tactics <span class="brand-badge">Arbre & Fleur Git</span></h1>
                </div>
            </a>
            <nav class="breadcrumbs">
                <a href="index.html">Codex</a>
                <span>/</span>
                <a href="Killtime/index.html">Univers</a>
                <span>/</span>
                <a href="roadmap.html">Roadmap</a>
                <span>/</span>
                <span class="current">Évolution Neuronale</span>
            </nav>
        </div>

        <div class="topbar-center">
            <div class="metrics-hud">
                <div class="metric-item">
                    <span>Commits:</span>
                    <span class="metric-val" id="metric-commits">53</span>
                </div>
                <span>•</span>
                <div class="metric-item">
                    <span>Scripts C#:</span>
                    <span class="metric-val" id="metric-scripts">215</span>
                </div>
                <span>•</span>
                <div class="metric-item">
                    <span>Branches:</span>
                    <span class="metric-val">7</span>
                </div>
                <span>•</span>
                <div class="metric-item">
                    <span>Période:</span>
                    <span class="metric-val" style="color:#fbbf24">8 Sep - 9 Oct 2026</span>
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
                <input type="text" class="search-input" id="commitSearchInput" placeholder="Chercher script, commit, mot..." autocomplete="off">
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
                    <div class="drawer-commit-num" id="drawerCommitNum">COMMIT #1 • 8 Sep 2026</div>
                    <h2 class="drawer-title" id="drawerTitle">Titre de la fonctionnalité</h2>
                </div>
                <button class="drawer-close-btn" id="drawerCloseBtn" title="Fermer le volet">✕</button>
            </div>

            <div class="drawer-meta-grid">
                <div class="meta-field">
                    <label>Auteur</label>
                    <span id="drawerAuthor">Alexis Lacasse</span>
                </div>
                <div class="meta-field">
                    <label>Hash Git</label>
                    <span id="drawerHash">59a92f8</span>
                </div>
                <div class="meta-field">
                    <label>Diff Lignes</label>
                    <span id="drawerDiff">+2240 / -0</span>
                </div>
                <div class="meta-field">
                    <label>Fichiers Modifiés</label>
                    <span id="drawerFilesCount">29 fichiers</span>
                </div>
            </div>

            <div class="drawer-summary" id="drawerSummary">
                Description de l'évolution apportée dans ce commit.
            </div>

            <div>
                <div class="drawer-section-title">
                    <span>Fichiers & Scripts Clés</span>
                    <span style="font-family: var(--font-mono); font-size: 10px;" id="drawerFileCategoryCount"></span>
                </div>
                <ul class="files-list" id="drawerFilesList">
                    <!-- Populated dynamically -->
                </ul>
            </div>

            <div id="drawerCausalSection">
                <div class="drawer-section-title">Lien Causal & Synapses</div>
                <div class="causal-links-group" id="drawerCausalList">
                    <!-- Populated dynamically -->
                </div>
            </div>

            <a href="#" target="_blank" rel="noopener" class="btn-github-link" id="drawerGithubBtn">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0024 12c0-6.63-5.37-12-12-12z"/>
                </svg>
                <span>Examiner le Commit sur GitHub ↗</span>
            </a>
        </aside>

        <!-- ================= BOTTOM TIMELINE & PLAYER ================= -->
        <div class="bottom-timeline-panel">
            <div class="timeline-upper">
                <div class="player-controls">
                    <button class="btn-play-pause" id="playPauseBtn" title="Lancer / Mettre en pause la croissance temporelle (Espace)">
                        <span id="play-icon">▶</span>
                    </button>
                    <button class="btn-ghost" id="btnStepBack" title="Reculer d'un commit">⏮</button>
                    <button class="btn-ghost" id="btnStepForward" title="Avancer d'un commit">⏭</button>

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
                        <span id="hudActiveCommitIndex" style="color:#fff; font-weight:700;">1</span> / 53 commits
                    </div>
                </div>

                <div class="timeline-actions">
                    <button class="btn-ghost" id="btnRestartSeed" title="Recommencer depuis la graine">🌱 Graine</button>
                    <button class="btn-ghost" id="btnFullBloom" title="Floraison complète immédiate">🌺 Tout Déployer</button>
                </div>
            </div>

            <!-- Scrubber Track -->
            <div class="slider-wrapper">
                <input type="range" class="chrono-slider" id="timelineSlider" min="1" max="53" value="1" step="1">
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
            <span>La Fleur Neuronale de Killtime Tactics</span>
        </h2>
        <p style="font-size: 13px; line-height: 1.6; color: #cbd5e1;">
            Cette visualisation organique modélise la genèse et la floraison technologique du jeu <strong>Killtime Tactics</strong> dans Unity du <strong>8 Septembre 2026</strong> au <strong>9 Octobre 2026</strong> (53 commits).
        </p>
        <div style="background: rgba(0,0,0,0.3); border-radius: 12px; padding: 14px; border: 1px solid var(--border-subtle); font-size: 12px; display: flex; flex-direction: column; gap: 8px;">
            <div>🌸 <strong>Branches & Pétales :</strong> 7 domaines spécialisés (Story, Combat, Héros, Shaders, Multijoueur, Audio, Outils).</div>
            <div>⚡ <strong>Synapses Causalités :</strong> Des influx lumineux relient les commits interdépendants.</div>
            <div>🎵 <strong>Synthèse Sonore :</strong> Harmonie céleste pentatonique générée en temps réel à chaque floraison.</div>
            <div>🕹️ <strong>Navigation :</strong> Glissez pour déplacer la vue, molette pour zoomer, cliquez sur un nœud pour inspecter ses scripts.</div>
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

    const {{ nodes, links, cross_links, branches, epochs }} = rawData;

    // 2. ÉTAT GLOBAL DE L'APPLICATION
    const state = {{
        currentCommitIndex: 1, // 1 à 53
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
            const freqs = {{
                story: [523.25, 659.25, 783.99],   // C5, E5, G5
                tactics: [440.00, 554.37, 659.25], // A4, C#5, E5
                core: [392.00, 493.88, 587.33],    // G4, B4, D5
                visuals: [587.33, 739.99, 880.00], // D5, F#5, A5
                network: [349.23, 440.00, 523.25], // F4, A4, C5
                audio: [659.25, 830.61, 987.77],   // E5, G#5, B5
                tools: [329.63, 415.30, 493.88]    // E4, G#4, B4
            }};

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
        const branchAngles = {{
            story: -0.85,
            tactics: 0.12,
            core: 0.95,
            visuals: 1.85,
            network: 2.75,
            audio: 3.55,
            tools: -1.75
        }};

        // Regrouper par branche
        const nodesByBranch = {{}};
        Object.keys(branches).forEach(b => nodesByBranch[b] = []);
        nodes.forEach(n => {{
            if (nodesByBranch[n.branch]) {{
                nodesByBranch[n.branch].push(n);
            }} else {{
                nodesByBranch['core'].push(n);
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
                // Distance radiale grandissante
                const distRatio = Math.pow((idx + 1) / Math.max(count, 1), 0.85);
                const r = 130 + distRatio * 520;
                // Courbure organique en pétale
                const curl = Math.sin((idx / Math.max(count, 1)) * Math.PI) * 0.28 + (idx * 0.02);
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
            let hemisphere = 1; // 1 = droit, -1 = gauche
            if (['story', 'visuals', 'audio'].includes(bKey)) hemisphere = 1;
            else if (['tactics', 'core', 'network'].includes(bKey)) hemisphere = -1;
            
            const branchOffset = {{
                story: {{ x: 280, y: -180 }},
                visuals: {{ x: 380, y: 120 }},
                audio: {{ x: 220, y: 320 }},
                tactics: {{ x: -320, y: -150 }},
                core: {{ x: -260, y: 140 }},
                network: {{ x: -380, y: 300 }},
                tools: {{ x: 0, y: -380 }}
            }}[bKey] || {{ x: 0, y: 0 }};

            const jitterX = Math.sin(node.id * 1.7) * 80;
            const jitterY = Math.cos(node.id * 2.3) * 80;
            const progressionRatio = node.id / 53;

            layoutCoords.neural[node.id] = {{
                x: branchOffset.x + jitterX + (hemisphere * progressionRatio * 160),
                y: branchOffset.y + jitterY + ((progressionRatio - 0.5) * 200)
            }};
        }});

        // --- MODE 3: ARBRE DE CAUSALITÉ (Fractal Chronologique Étage par Étage) ---
        // Nœud 1 aux racines en bas
        const totalHeight = 850;
        const startY = 380;
        nodes.forEach(node => {{
            const t = (node.id - 1) / 52;
            const y = startY - (t * totalHeight);
            
            // Écartement horizontal selon la branche
            const branchSpread = {{
                story: 280,
                tactics: -260,
                core: -120,
                visuals: 380,
                network: -360,
                audio: 140,
                tools: 0
            }}[node.branch] || 0;

            const sway = Math.sin(node.id * 0.6) * 45;
            layoutCoords.tree[node.id] = {{
                x: branchSpread + sway,
                y: y
            }};
        }});

        // --- MODE 4: RHIZOME LIBRE (Force Initial Spacing) ---
        nodes.forEach(node => {{
            const ang = (node.id / 53) * Math.PI * 2 + Math.random() * 0.4;
            const dist = 100 + (node.id / 53) * 450;
            layoutCoords.force[node.id] = {{
                x: Math.cos(ang) * dist,
                y: Math.sin(ang) * dist,
                vx: 0,
                vy: 0
            }};
        }});
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
            hasBloomed: n.id === 1
        }};
    }});

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
            const hitRadius = (n.milestone ? 24 : 16) / state.cam.zoom + 6;
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
                    ttSub.textContent = `#${{hovered.id}} • ${{hovered.date_display}} • ${{hovered.short}}`;
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

        drawerCommitNum.textContent = `COMMIT #${{node.id}} • ${{node.date_display}}`;
        drawerTitle.textContent = node.title;
        drawerAuthor.textContent = node.author;
        drawerHash.textContent = node.short;
        drawerDiff.textContent = `+${{node.insertions}} / -${{node.deletions}}`;
        drawerFilesCount.textContent = `${{node.files_count}} fichiers`;
        drawerSummary.textContent = node.summary;

        // Fichiers modifiés
        drawerFilesList.innerHTML = '';
        if (node.files && node.files.length > 0) {{
            drawerFileCategoryCount.textContent = `${{node.files.length}} affichés`;
            node.files.forEach(f => {{
                const li = document.createElement('li');
                li.className = 'file-item';
                const badgeClass = `badge-${{f.type}}`;
                li.innerHTML = `
                    <span style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 260px;" title="${{f.path}}">${{f.name}}</span>
                    <span class="file-badge ${{badgeClass}}">${{f.type}}</span>
                `;
                drawerFilesList.appendChild(li);
            }});
        }} else {{
            drawerFileCategoryCount.textContent = 'Aucun script';
            drawerFilesList.innerHTML = '<li class="file-item" style="color:var(--text-muted)">Aucun fichier spécifique listé</li>';
        }}

        // Connexions causales
        drawerCausalList.innerHTML = '';
        const relevantCrossLinks = cross_links.filter(cl => cl.source === node.id || cl.target === node.id);
        if (relevantCrossLinks.length > 0) {{
            relevantCrossLinks.forEach(cl => {{
                const otherId = cl.source === node.id ? cl.target : cl.source;
                const otherNode = nodes.find(n => n.id === otherId);
                if (otherNode) {{
                    const btn = document.createElement('button');
                    btn.className = 'causal-jump-btn';
                    const isTarget = cl.source === node.id;
                    btn.innerHTML = `
                        <span>${{isTarget ? '➔ Impacte :' : '🠔 Issu de :'}} <strong>${{otherNode.title}}</strong></span>
                        <span style="font-family:var(--font-mono); font-size:10px; color:#38bdf8;">#${{otherNode.id}}</span>
                    `;
                    btn.addEventListener('click', () => selectNode(otherNode));
                    drawerCausalList.appendChild(btn);
                }}
            }});
        }} else {{
            drawerCausalList.innerHTML = '<div style="font-size:12px; color:var(--text-muted);">Lien causal direct sur la branche temporelle.</div>';
        }}

        drawerGithubBtn.href = `https://github.com/k0r0z1f/Killtime/commit/${{node.hash}}`;
        drawer.classList.add('open');
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
            if (state.currentCommitIndex >= 53) {{
                updateTimelineUI(1);
            }}
        }}
    }}
    playPauseBtn.addEventListener('click', togglePlay);

    document.getElementById('btnStepForward').addEventListener('click', () => {{
        if (state.currentCommitIndex < 53) updateTimelineUI(state.currentCommitIndex + 1);
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
        updateTimelineUI(53);
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
    document.querySelectorAll('.mode-btn').forEach(btn => {{
        btn.addEventListener('click', () => {{
            document.querySelectorAll('.mode-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            state.viewMode = btn.dataset.mode;
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
        updateTimelineUI(53);
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
            if (state.currentCommitIndex < 53) updateTimelineUI(state.currentCommitIndex + 1);
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
            if (playAccumulator >= 0.35) {{ // un commit toutes les 0.35s à vitesse 1x
                playAccumulator = 0;
                if (state.currentCommitIndex < 53) {{
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

        nodes.forEach(n => {{
            const target = targetLayout[n.id] || {{ x: 0, y: 0 }};
            const curr = currentPositions[n.id];
            
            // Ondulation organique de respiration
            const sway = Math.sin(now * 0.0015 + n.id * 0.4) * 4;
            const destX = target.x + (n.id === 1 ? 0 : sway);
            const destY = target.y + (n.id === 1 ? 0 : breathWave * 0.5);

            curr.x += (destX - curr.x) * 0.09;
            curr.y += (destY - curr.y) * 0.09;

            // Éclosion / Scale du nœud selon la visibilité
            const isVisible = n.id <= state.currentCommitIndex;
            const targetScale = isVisible ? 1.0 : 0.0;
            curr.scale += (targetScale - curr.scale) * 0.15;
        }});

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

        // --- 2. RENDU DU CŒUR TEMPOREL (LA GRAINE COMMIT #1) ---
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
        // Synapses causales croisées (cross links)
        cross_links.forEach(cl => {{
            if (cl.source > state.currentCommitIndex || cl.target > state.currentCommitIndex) return;
            const sourceNode = nodes[cl.source - 1];
            const targetNode = nodes[cl.target - 1];
            if (!state.activeBranches.has(sourceNode.branch) || !state.activeBranches.has(targetNode.branch)) return;

            const p0 = currentPositions[cl.source];
            const p1 = currentPositions[cl.target];
            const isHoveredOrSelected = (state.hoveredNode && (state.hoveredNode.id === cl.source || state.hoveredNode.id === cl.target)) ||
                                        (state.selectedNode && (state.selectedNode.id === cl.source || state.selectedNode.id === cl.target));

            ctx.save();
            ctx.strokeStyle = isHoveredOrSelected ? '#38bdf8' : 'rgba(192, 132, 252, 0.35)';
            ctx.lineWidth = isHoveredOrSelected ? 2.2 : 1.2;
            ctx.setLineDash(isHoveredOrSelected ? [] : [4, 4]);
            if (isHoveredOrSelected) {{
                ctx.shadowColor = '#38bdf8';
                ctx.shadowBlur = 10;
            }}

            ctx.beginPath();
            ctx.moveTo(p0.x, p0.y);
            // Courbe arquée pour symboliser le pont synaptique
            const midX = (p0.x + p1.x) / 2;
            const midY = (p0.y + p1.y) / 2 - 35;
            ctx.quadraticCurveTo(midX, midY, p1.x, p1.y);
            ctx.stroke();
            ctx.restore();
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

            // Déterminer la taille
            let baseRadius = node.milestone ? 14 : 9;
            if (node.id === 1) baseRadius = 18;
            if (isSelected || isHovered) baseRadius *= 1.35;
            if (isSearchMatch) baseRadius *= 1.4;

            // Halo lumineux
            const glowColor = isSearchMatch ? '#fbbf24' : (isSelected ? '#ffffff' : bInfo.color);
            ctx.shadowColor = glowColor;
            ctx.shadowBlur = (isSelected || isHovered || isSearchMatch) ? 22 : 10;

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
    updateTimelineUI(53); // Montre l'arbre complet au chargement pour un effet WOW immédiat !

}})();
</script>
</body>
</html>
'''
    with open('tactics_evolution.html', 'w', encoding='utf-8') as f:
        f.write(html_template)
    print("tactics_evolution.html généré avec succès!")

if __name__ == '__main__':
    build_html()
