#!/usr/bin/env python3
"""
Cartographie visuelle des fichiers du projet Killtime Tactics.
1. Scanne le code maison -> data/tactics_files.json
2. Genere tactics_files.html (treemap canvas + HUD statistique).
Perimetre : Scripts/**/*.cs, Shaders, scenes maison, JSON Resources, asmdefs
(hors Packages tiers, binaires, .meta). Relancer pour mettre a jour.
"""

import json
import os
import re
import subprocess
import tempfile

# Fichiers ScenarioEditor en cours d'edition : jamais lus depuis le disque
# (mi-redaction). Les suivis sont analyses depuis HEAD, les non-suivis sautes.
PIN_INFIX = "ScenarioEditorDevWindow.Cards."

CLASS_RE = re.compile(r"^\s*(?:public|internal|private|protected)?\s*(?:sealed|abstract|static)?\s*(?:partial\s+)?(class|struct|interface|enum)\s+(\w+)", re.M)
NS_RE = re.compile(r"^\s*namespace\s+([\w.]+)", re.M)
METHOD_RE = re.compile(r"^\s*(?:public|private|protected|internal)[\w\s<>\[\],?]*\s+\w+\s*\([^;]*\)\s*(?:\{|=>|where)", re.M)
PROP_RE = re.compile(r"^\s*public[\w\s<>\[\],?]*\s+\w+\s*\{\s*get", re.M)

MODULE_COLORS = {
    "Core": "#38bdf8", "Tactics": "#fbbf24", "Story": "#c084fc",
    "UI": "#f43f5e", "DevTools": "#f97316", "Multi": "#34d399",
    "Audio": "#a855f7", "Camera": "#22d3ee", "Tests": "#94a3b8",
    "Tools": "#e879f9", "WebGL": "#4ade80", "Shaders": "#fb7185",
    "Scenes": "#facc15", "Data": "#2dd4bf",
}

MODULE_LABELS = {
    "Core": "Cœur & Règles", "Tactics": "Tactique & Combat", "Story": "Récit & Cinématiques",
    "UI": "Interface", "DevTools": "Outils Dev", "Multi": "Multijoueur VTT",
    "Audio": "Audio", "Camera": "Caméra", "Tests": "Tests", "Tools": "Outils",
    "WebGL": "WebGL", "Shaders": "Shaders", "Scenes": "Scènes", "Data": "Données JSON",
}


def analyze_text(path):
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            text = f.read()
    except OSError:
        return None
    lines = text.split("\n")
    if lines and lines[-1] == "":
        lines.pop()
    loc = len(lines)
    blank = sum(1 for l in lines if not l.strip())
    comment = sum(1 for l in lines if l.strip().startswith(("//", "/*", "*", "*/", "#")))
    code = loc - blank - comment
    classes = [{"kind": m.group(1), "name": m.group(2)} for m in CLASS_RE.finditer(text)]
    ns = NS_RE.search(text)
    return {
        "loc": loc, "blank": blank, "comment": comment, "code": max(code, 0),
        "classes": classes[:12], "n_classes": len(classes),
        "namespace": ns.group(1) if ns else None,
        "methods": len(METHOD_RE.findall(text)),
        "properties": len(PROP_RE.findall(text)),
        "partial": "partial" in text[:2000] and path.endswith(".cs"),
    }


def collect():
    assets = "KilltimeTactics/Assets"
    try:
        tracked = set(subprocess.run(["git", "ls-files"], capture_output=True,
                                     text=True).stdout.splitlines())
    except OSError:
        tracked = set()
    files = []

    def analyze_pinned(repo_path):
        """Analyse le blob HEAD (ou None si absent de HEAD)."""
        p = subprocess.run(["git", "show", f"HEAD:{repo_path}"], capture_output=True,
                           text=True)
        if p.returncode != 0:
            return None, 0
        with tempfile.NamedTemporaryFile("w", suffix=".cs", delete=False,
                                         encoding="utf-8") as t:
            t.write(p.stdout)
            tmp = t.name
        try:
            st = analyze_text(tmp)
        finally:
            os.unlink(tmp)
        return st, len(p.stdout.encode("utf-8", "replace"))

    for root, _d, fns in os.walk(os.path.join(assets, "Scripts")):
        for fn in fns:
            if not fn.endswith(".cs"):
                continue
            full = os.path.join(root, fn)
            rel = os.path.relpath(full, os.path.join(assets, "Scripts"))
            parts = rel.split(os.sep)
            module = parts[0] if len(parts) > 1 else "racine"
            subdir = parts[1] if len(parts) > 2 else ""
            repo_path = "KilltimeTactics/Assets/Scripts/" + rel.replace(os.sep, "/")
            if PIN_INFIX in repo_path:
                if repo_path not in tracked:
                    continue
                st, size = analyze_pinned(repo_path)
                if st is None:
                    continue
            else:
                st = analyze_text(full)
                if st is None:
                    continue
                size = os.path.getsize(full)
            base = fn[:-3]
            group = base.split(".")[0] if "." in base else None
            files.append({
                "name": fn, "dir": os.path.dirname(rel).replace(os.sep, "/"),
                "module": module, "subdir": subdir, "type": "csharp",
                "repo_path": repo_path,
                "size": size, "partial_group": group, **st,
            })
    for root, _d, fns in os.walk(os.path.join(assets, "Shaders")):
        for fn in fns:
            if not fn.endswith(".shader"):
                continue
            full = os.path.join(root, fn)
            st = analyze_text(full)
            files.append({"name": fn, "dir": "Shaders", "module": "Shaders",
                          "subdir": "", "type": "shader",
                          "repo_path": "KilltimeTactics/Assets/Shaders/" + fn,
                          "size": os.path.getsize(full),
                          "partial_group": None, **st})
    for rel in ["Main.unity", "Scenes/CombatDevArena.unity"]:
        full = os.path.join(assets, rel)
        if os.path.exists(full):
            st = analyze_text(full)
            files.append({"name": os.path.basename(rel), "dir": os.path.dirname(rel) or "racine",
                          "module": "Scenes", "subdir": "", "type": "scene",
                          "repo_path": "KilltimeTactics/Assets/" + rel,
                          "size": os.path.getsize(full), "partial_group": None, **st})
    for root, _d, fns in os.walk(os.path.join(assets, "Resources")):
        for fn in fns:
            if not fn.endswith(".json"):
                continue
            full = os.path.join(root, fn)
            try:
                with open(full, encoding="utf-8", errors="replace") as f:
                    content = f.read()
                obj = json.loads(content)
                keys = len(obj) if isinstance(obj, (dict, list)) else 0
            except (OSError, ValueError):
                content, keys = "", 0
            loc = content.count("\n") + 1 if content else 0
            sub = os.path.relpath(root, os.path.join(assets, "Resources")).replace(os.sep, "/").strip("./")
            files.append({"name": fn, "dir": ("Resources/" + sub) if sub else "Resources",
                          "module": "Data", "subdir": "", "type": "json",
                          "repo_path": "KilltimeTactics/Assets/Resources/" + (sub + "/" if sub else "") + fn,
                          "size": os.path.getsize(full), "loc": loc,
                          "blank": 0, "comment": 0, "code": loc,
                          "classes": [], "n_classes": 0, "namespace": None,
                          "methods": 0, "properties": keys, "partial": False,
                          "partial_group": None})
    for root, _d, fns in os.walk(os.path.join(assets, "Scripts")):
        for fn in fns:
            if not fn.endswith(".asmdef"):
                continue
            full = os.path.join(root, fn)
            st = analyze_text(full)
            files.append({"name": fn, "dir": os.path.dirname(os.path.relpath(full, os.path.join(assets, "Scripts"))).replace(os.sep, "/"),
                          "module": "Core", "subdir": "", "type": "asmdef",
                          "repo_path": "KilltimeTactics/Assets/Scripts/" + os.path.relpath(full, os.path.join(assets, "Scripts")).replace(os.sep, "/"),
                          "size": os.path.getsize(full), "partial_group": None, **st})
    return files


def scan():
    files = collect()
    files.sort(key=lambda f: f["loc"], reverse=True)
    for i, f in enumerate(files):
        f["id"] = i + 1
    # Liens GitHub figes : sha + etat worktree depuis l'historique fleur (si present)
    try:
        with open("data/tactics_files_history.json", encoding="utf-8") as fh:
            hmap = {n["repo_path"]: n for n in json.load(fh)["nodes"]}
    except (OSError, ValueError, KeyError):
        hmap = {}
    for f in files:
        hn = hmap.get(f.get("repo_path", ""))
        f["github_sha"] = hn["last_hash"] if hn else None
        f["worktree"] = hn["worktree"] if hn else "unknown"
    modules = {}
    for f in files:
        m = modules.setdefault(f["module"], {"id": f["module"], "files": 0, "loc": 0,
                                              "code": 0, "size": 0, "classes": 0, "methods": 0})
        m["files"] += 1
        m["loc"] += f["loc"]
        m["code"] += f["code"]
        m["size"] += f["size"]
        m["classes"] += f["n_classes"]
        m["methods"] += f["methods"]
    for m in modules.values():
        m["color"] = MODULE_COLORS.get(m["id"], "#94a3b8")
        m["label"] = MODULE_LABELS.get(m["id"], m["id"])
    groups = {}
    for f in files:
        if f["partial_group"]:
            g = groups.setdefault(f["partial_group"], {"name": f["partial_group"],
                                                       "module": f["module"], "files": [], "loc": 0})
            g["files"].append({"name": f["name"], "loc": f["loc"], "id": f["id"]})
            g["loc"] += f["loc"]
    partial_groups = sorted([g for g in groups.values() if len(g["files"]) >= 2],
                            key=lambda g: g["loc"], reverse=True)
    for g in partial_groups:
        g["files"].sort(key=lambda x: x["loc"], reverse=True)
        g["color"] = MODULE_COLORS.get(g["module"], "#94a3b8")
    data = {
        "project": "Killtime Tactics — Cartographie des fichiers",
        "scope": "Scripts/**/*.cs, Shaders, scenes maison, JSON Resources, asmdefs (hors Packages tiers, binaires, .meta)",
        "totals": {
            "files": len(files),
            "loc": sum(f["loc"] for f in files),
            "code": sum(f["code"] for f in files),
            "size": sum(f["size"] for f in files),
            "classes": sum(f["n_classes"] for f in files),
            "methods": sum(f["methods"] for f in files),
            "modules": len(modules),
            "partial_groups": len(partial_groups),
        },
        "modules": sorted(modules.values(), key=lambda m: m["loc"], reverse=True),
        "partial_groups": partial_groups,
        "files": files,
    }
    with open("data/tactics_files.json", "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    return data


def build_html(data):
    files_json_str = json.dumps(data, ensure_ascii=False)
    t = data["totals"]

    def fr(n):
        return f"{n:,}".replace(",", " ")

    html_template = f'''<!DOCTYPE html>
<html lang="fr" data-theme="dark">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Killtime Tactics — Cartographie des Fichiers</title>
    <meta name="description" content="Treemap interactive du code maison de Killtime Tactics : {t["files"]} fichiers, {fr(t["loc"])} lignes, {t["classes"]} classes, {t["partial_groups"]} groupes partiels.">

    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link href="https://fonts.googleapis.com/css2?family=Cinzel:wght@600;700;800;900&family=JetBrains+Mono:wght@400;500;600;700&family=Plus+Jakarta+Sans:wght@300;400;500;600;700;800&display=swap" rel="stylesheet">

    <style>
        :root {{
            --bg-void: #030712;
            --bg-surface: rgba(11, 17, 32, 0.82);
            --bg-elevated: rgba(18, 28, 51, 0.92);
            --border-subtle: rgba(255, 255, 255, 0.09);
            --border-glow: rgba(56, 189, 248, 0.35);
            --text-primary: #f8fafc;
            --text-secondary: #94a3b8;
            --text-muted: #64748b;
            --text-accent: #38bdf8;
            --font-title: 'Cinzel', serif;
            --font-body: 'Plus Jakarta Sans', sans-serif;
            --font-mono: 'JetBrains Mono', monospace;
        }}
        * {{ box-sizing: border-box; margin: 0; padding: 0; -webkit-font-smoothing: antialiased; }}
        html, body {{ width: 100%; height: 100%; overflow: hidden; background: var(--bg-void); font-family: var(--font-body); color: var(--text-primary); }}
        #app-root {{ display: flex; flex-direction: column; width: 100vw; height: 100vh; }}

        .topbar {{ height: 64px; flex-shrink: 0; background: rgba(6,10,20,.78); backdrop-filter: blur(20px); border-bottom: 1px solid var(--border-subtle); display: flex; align-items: center; justify-content: space-between; padding: 0 20px; z-index: 50; }}
        .topbar-left {{ display: flex; align-items: center; gap: 16px; }}
        .brand {{ display: flex; align-items: center; gap: 12px; text-decoration: none; color: var(--text-primary); }}
        .brand-icon {{ width: 36px; height: 36px; border-radius: 10px; background: linear-gradient(135deg, rgba(56,189,248,.25), rgba(251,191,36,.25)); border: 1px solid rgba(251,191,36,.4); display: flex; align-items: center; justify-content: center; font-size: 18px; }}
        .brand-info h1 {{ font-family: var(--font-title); font-size: 15px; letter-spacing: .05em; display: flex; align-items: center; gap: 8px; color: #fff; }}
        .brand-badge {{ font-family: var(--font-mono); font-size: 10px; padding: 2px 7px; border-radius: 999px; background: rgba(251,191,36,.15); border: 1px solid rgba(251,191,36,.4); color: #fde68a; text-transform: uppercase; letter-spacing: .08em; }}
        .breadcrumbs {{ display: flex; align-items: center; gap: 8px; font-size: 12px; color: var(--text-muted); border-left: 1px solid var(--border-subtle); padding-left: 16px; }}
        .breadcrumbs a {{ color: var(--text-secondary); text-decoration: none; }}
        .breadcrumbs a:hover {{ color: var(--text-accent); }}
        .breadcrumbs .current {{ color: var(--text-primary); font-weight: 600; }}
        .metrics-hud {{ display: flex; align-items: center; gap: 14px; background: rgba(255,255,255,.03); border: 1px solid var(--border-subtle); padding: 4px 14px; border-radius: 999px; font-size: 12px; color: var(--text-secondary); }}
        .metric-item {{ display: flex; align-items: center; gap: 5px; }}
        .metric-val {{ color: #38bdf8; font-weight: 700; font-family: var(--font-mono); }}
        .topbar-right {{ display: flex; align-items: center; gap: 10px; }}
        .btn-ghost {{ background: rgba(255,255,255,.05); border: 1px solid var(--border-subtle); color: var(--text-secondary); padding: 7px 12px; border-radius: 8px; font-size: 12px; font-weight: 600; cursor: pointer; display: inline-flex; align-items: center; gap: 6px; text-decoration: none; transition: all .2s ease; }}
        .btn-ghost:hover {{ background: rgba(255,255,255,.12); color: var(--text-primary); }}

        #main-layout {{ flex: 1; display: flex; min-height: 0; }}

        /* ---------- Sidebar ---------- */
        #sidebar {{ width: 320px; flex-shrink: 0; background: rgba(6,10,20,.6); border-right: 1px solid var(--border-subtle); overflow-y: auto; padding: 16px; display: flex; flex-direction: column; gap: 18px; }}
        .side-section {{ background: var(--bg-surface); border: 1px solid var(--border-subtle); border-radius: 14px; padding: 14px; }}
        .side-title {{ font-size: 11px; font-weight: 800; text-transform: uppercase; letter-spacing: .08em; color: var(--text-secondary); margin-bottom: 12px; display: flex; justify-content: space-between; align-items: center; }}
        .side-title .hint {{ font-weight: 400; text-transform: none; letter-spacing: 0; color: var(--text-muted); font-size: 10px; }}
        #donut-wrap {{ display: flex; gap: 12px; align-items: center; }}
        #donut {{ flex-shrink: 0; }}
        #donut-center {{ font-family: var(--font-mono); }}
        #donut-legend {{ display: flex; flex-direction: column; gap: 3px; font-size: 11px; max-height: 150px; overflow-y: auto; flex: 1; }}
        .legend-row {{ display: flex; align-items: center; gap: 6px; color: var(--text-secondary); cursor: pointer; padding: 1px 4px; border-radius: 4px; }}
        .legend-row:hover {{ background: rgba(255,255,255,.06); }}
        .legend-row.off {{ opacity: .35; }}
        .legend-dot {{ width: 9px; height: 9px; border-radius: 3px; flex-shrink: 0; }}
        .legend-row .pct {{ margin-left: auto; font-family: var(--font-mono); color: var(--text-muted); }}
        .bar-row {{ margin-bottom: 8px; cursor: pointer; }}
        .bar-row:last-child {{ margin-bottom: 0; }}
        .bar-top {{ display: flex; justify-content: space-between; font-size: 11px; margin-bottom: 3px; }}
        .bar-top .bname {{ color: var(--text-primary); font-weight: 600; display: flex; align-items: center; gap: 6px; }}
        .bar-top .bval {{ font-family: var(--font-mono); color: var(--text-muted); }}
        .bar-track {{ height: 8px; background: rgba(255,255,255,.07); border-radius: 999px; overflow: hidden; }}
        .bar-fill {{ height: 100%; border-radius: 999px; transition: width .4s ease; }}
        .bar-row.off {{ opacity: .35; }}
        .topfile-row {{ display: flex; align-items: center; gap: 8px; font-size: 11px; font-family: var(--font-mono); padding: 4px 6px; border-radius: 6px; cursor: pointer; color: var(--text-secondary); }}
        .topfile-row:hover {{ background: rgba(56,189,248,.12); color: #fff; }}
        .topfile-row .rank {{ color: var(--text-muted); width: 18px; flex-shrink: 0; }}
        .topfile-row .tloc {{ margin-left: auto; color: var(--text-accent); flex-shrink: 0; }}
        .topfile-row .tname {{ overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }}
        .group-card {{ border: 1px solid var(--border-subtle); border-radius: 10px; padding: 8px 10px; margin-bottom: 8px; cursor: pointer; transition: border-color .2s; }}
        .group-card:hover {{ border-color: var(--border-glow); }}
        .group-card:last-child {{ margin-bottom: 0; }}
        .group-head {{ display: flex; justify-content: space-between; font-size: 12px; font-weight: 700; margin-bottom: 6px; }}
        .group-head .gloc {{ font-family: var(--font-mono); color: var(--text-muted); font-weight: 400; font-size: 11px; }}
        .stack-bar {{ display: flex; height: 10px; border-radius: 999px; overflow: hidden; background: rgba(255,255,255,.05); }}
        .stack-bar div {{ height: 100%; }}
        .group-sub {{ font-size: 10px; color: var(--text-muted); font-family: var(--font-mono); margin-top: 4px; }}

        /* ---------- Map area ---------- */
        #map-area {{ flex: 1; position: relative; display: flex; flex-direction: column; min-width: 0; background: radial-gradient(circle at 50% 40%, #080f22 0%, #030712 85%); }}
        #map-toolbar {{ display: flex; align-items: center; gap: 10px; padding: 12px 16px; flex-shrink: 0; flex-wrap: wrap; }}
        .crumb-btn {{ background: var(--bg-surface); border: 1px solid var(--border-subtle); color: var(--text-primary); font-size: 12px; font-weight: 700; padding: 6px 12px; border-radius: 8px; cursor: pointer; }}
        .crumb-btn:hover {{ border-color: var(--border-glow); }}
        .crumb-sep {{ color: var(--text-muted); }}
        .crumb-current {{ font-size: 12px; color: var(--text-accent); font-weight: 700; }}
        .search-box {{ margin-left: auto; background: var(--bg-surface); border: 1px solid var(--border-subtle); border-radius: 10px; display: flex; align-items: center; padding: 5px 12px; width: 240px; }}
        .search-box:focus-within {{ border-color: rgba(56,189,248,.5); }}
        .search-input {{ background: transparent; border: none; outline: none; color: var(--text-primary); font-size: 12px; margin-left: 8px; width: 100%; font-family: var(--font-body); }}
        .size-toggle {{ display: flex; background: rgba(255,255,255,.05); border-radius: 8px; padding: 2px; border: 1px solid var(--border-subtle); }}
        .size-btn {{ background: transparent; border: none; color: var(--text-muted); font-size: 11px; font-family: var(--font-mono); font-weight: 700; padding: 5px 10px; border-radius: 6px; cursor: pointer; }}
        .size-btn.active {{ background: rgba(251,191,36,.2); color: #fbbf24; }}
        #pills-bar {{ display: flex; gap: 6px; padding: 0 16px 10px; overflow-x: auto; flex-shrink: 0; }}
        .pill {{ background: transparent; border: 1px solid var(--border-subtle); color: var(--text-muted); padding: 4px 10px; border-radius: 999px; font-size: 11px; font-weight: 700; cursor: pointer; white-space: nowrap; display: flex; align-items: center; gap: 5px; }}
        .pill .dot {{ width: 8px; height: 8px; border-radius: 50%; }}
        .pill.off {{ opacity: .35; }}
        #treemap-wrap {{ flex: 1; position: relative; margin: 0 16px 16px; border: 1px solid var(--border-subtle); border-radius: 16px; overflow: hidden; min-height: 0; }}
        #treemap {{ position: absolute; inset: 0; width: 100%; height: 100%; display: block; }}
        #map-tooltip {{ position: absolute; pointer-events: none; background: rgba(10,16,30,.94); border: 1px solid rgba(251,191,36,.4); border-radius: 10px; padding: 8px 12px; font-size: 12px; z-index: 40; display: none; max-width: 320px; box-shadow: 0 8px 24px rgba(0,0,0,.5); }}
        #map-tooltip .tt-title {{ font-weight: 700; color: #fbbf24; font-family: var(--font-mono); font-size: 12px; word-break: break-all; }}
        #map-tooltip .tt-sub {{ color: var(--text-secondary); font-size: 11px; font-family: var(--font-mono); margin-top: 2px; }}
        #map-hint {{ position: absolute; bottom: 10px; left: 12px; font-size: 11px; color: var(--text-muted); background: rgba(6,10,20,.7); padding: 4px 10px; border-radius: 8px; border: 1px solid var(--border-subtle); z-index: 10; }}

        /* ---------- Inspector ---------- */
        #inspector {{ position: absolute; top: 76px; right: -430px; width: 400px; bottom: 16px; background: rgba(18,28,51,.95); backdrop-filter: blur(24px); border: 1px solid var(--border-subtle); border-radius: 20px; padding: 22px; display: flex; flex-direction: column; gap: 14px; z-index: 60; box-shadow: -15px 0 40px rgba(0,0,0,.6); transition: right .35s cubic-bezier(.16,1,.3,1); overflow-y: auto; }}
        #inspector.open {{ right: 16px; }}
        .insp-head {{ border-bottom: 1px solid var(--border-subtle); padding-bottom: 14px; display: flex; justify-content: space-between; gap: 10px; }}
        .insp-module {{ display: inline-flex; align-items: center; gap: 6px; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .08em; padding: 3px 8px; border-radius: 6px; margin-bottom: 8px; }}
        .insp-title {{ font-family: var(--font-mono); font-size: 14px; color: #fff; word-break: break-all; line-height: 1.4; }}
        .insp-path {{ font-family: var(--font-mono); font-size: 11px; color: var(--text-muted); margin-top: 4px; }}
        .insp-close {{ background: rgba(255,255,255,.06); border: 1px solid var(--border-subtle); color: var(--text-secondary); width: 32px; height: 32px; border-radius: 8px; cursor: pointer; flex-shrink: 0; }}
        .insp-close:hover {{ color: #fff; background: rgba(255,255,255,.15); }}
        .meta-grid {{ display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }}
        .meta-cell {{ background: rgba(0,0,0,.25); border: 1px solid var(--border-subtle); border-radius: 10px; padding: 8px 10px; }}
        .meta-cell label {{ display: block; font-size: 10px; text-transform: uppercase; color: var(--text-muted); margin-bottom: 2px; }}
        .meta-cell span {{ font-family: var(--font-mono); font-weight: 700; font-size: 14px; color: var(--text-primary); }}
        .comp-bar {{ display: flex; height: 14px; border-radius: 999px; overflow: hidden; background: rgba(255,255,255,.05); }}
        .comp-legend {{ display: flex; gap: 12px; font-size: 11px; color: var(--text-secondary); margin-top: 6px; }}
        .comp-legend i {{ display: inline-block; width: 9px; height: 9px; border-radius: 3px; margin-right: 4px; }}
        .sec-title {{ font-size: 11px; font-weight: 800; text-transform: uppercase; letter-spacing: .08em; color: var(--text-secondary); }}
        .class-chips {{ display: flex; flex-wrap: wrap; gap: 6px; }}
        .chip {{ font-family: var(--font-mono); font-size: 11px; background: rgba(56,189,248,.12); border: 1px solid rgba(56,189,248,.35); color: #7dd3fc; padding: 3px 8px; border-radius: 6px; }}
        .chip.kind-struct, .chip.kind-enum {{ background: rgba(251,191,36,.1); border-color: rgba(251,191,36,.35); color: #fcd34d; }}
        .chip.kind-interface {{ background: rgba(192,132,252,.1); border-color: rgba(192,132,252,.35); color: #d8b4fe; }}
        .sib-btn {{ display: block; width: 100%; text-align: left; background: rgba(255,255,255,.04); border: 1px solid var(--border-subtle); color: var(--text-secondary); padding: 6px 10px; border-radius: 8px; font-size: 11px; font-family: var(--font-mono); cursor: pointer; margin-bottom: 5px; }}
        .sib-btn:hover {{ border-color: var(--border-glow); color: #fff; }}

        #help-modal {{ position: fixed; inset: 0; background: rgba(0,0,0,.7); backdrop-filter: blur(8px); display: none; align-items: center; justify-content: center; z-index: 200; }}
        .modal-content {{ width: 560px; max-width: 92vw; background: rgba(18,28,51,.97); border: 1px solid var(--border-glow); border-radius: 20px; padding: 28px; display: flex; flex-direction: column; gap: 14px; }}
        @media (max-width: 1100px) {{ #sidebar {{ display: none; }} #inspector {{ width: calc(100% - 32px); }} }}
    </style>
</head>
<body>
<div id="app-root">
    <header class="topbar">
        <div class="topbar-left">
            <a href="index.html" class="brand">
                <div class="brand-icon">🗂️</div>
                <div class="brand-info"><h1>Killtime Tactics <span class="brand-badge">Cartographie Fichiers</span></h1></div>
            </a>
            <nav class="breadcrumbs">
                <a href="index.html">Codex</a><span>/</span>
                <a href="tactics_evolution.html">Évolution Git</a><span>/</span>
                <span class="current">Fichiers du Projet</span>
            </nav>
        </div>
        <div class="topbar-center">
            <div class="metrics-hud">
                <div class="metric-item"><span>Fichiers:</span><span class="metric-val" id="m-files">{t["files"]}</span></div><span>•</span>
                <div class="metric-item"><span>Lignes:</span><span class="metric-val" id="m-loc">{fr(t["loc"])}</span></div><span>•</span>
                <div class="metric-item"><span>Classes:</span><span class="metric-val" id="m-classes">{t["classes"]}</span></div><span>•</span>
                <div class="metric-item"><span>Méthodes:</span><span class="metric-val" id="m-methods">{fr(t["methods"])}</span></div><span>•</span>
                <div class="metric-item"><span>Groupes partiels:</span><span class="metric-val" style="color:#fbbf24">{t["partial_groups"]}</span></div>
            </div>
        </div>
        <div class="topbar-right">
            <button class="btn-ghost" id="helpBtn">ℹ️ Guide</button>
            <a href="tactics_fleur_fichiers.html" class="btn-ghost">🌸 Fleur Fichiers</a>
            <a href="tactics_evolution.html" class="btn-ghost">🌺 Évolution Git</a>
            <a href="roadmap.html" class="btn-ghost">📋 Roadmap</a>
        </div>
    </header>

    <div id="main-layout">
        <aside id="sidebar">
            <div class="side-section">
                <div class="side-title"><span>LOC par module</span><span class="hint">clic = filtrer</span></div>
                <div id="donut-wrap">
                    <svg id="donut" width="120" height="120" viewBox="0 0 120 120"></svg>
                    <div id="donut-legend"></div>
                </div>
            </div>
            <div class="side-section">
                <div class="side-title"><span>Modules — volume</span><span class="hint" id="mod-hint"></span></div>
                <div id="module-bars"></div>
            </div>
            <div class="side-section">
                <div class="side-title"><span>Top fichiers</span><span class="hint">par lignes</span></div>
                <div id="top-files"></div>
            </div>
            <div class="side-section">
                <div class="side-title"><span>Monolithes découpés</span><span class="hint">clic = localiser</span></div>
                <div id="groups"></div>
            </div>
            <div class="side-section" style="font-size:11px;color:var(--text-muted);line-height:1.6;">
                Périmètre : Scripts C#, shaders, scènes maison, JSON Resources, asmdefs.<br>
                Exclus : Packages tiers, textures, modèles, <span style="font-family:var(--font-mono)">.meta</span>, Library.<br>
                <span style="font-family:var(--font-mono)">Relancer tools/generate_tactics_files.py</span> pour actualiser.
            </div>
        </aside>

        <section id="map-area">
            <div id="map-toolbar">
                <button class="crumb-btn" id="crumbAll" style="display:none">← Tous les modules</button>
                <span class="crumb-current" id="crumbLabel">Tous les modules — surface = lignes de code</span>
                <div class="search-box">
                    <span style="color:#64748b">🔍</span>
                    <input type="text" class="search-input" id="searchInput" placeholder="Chercher fichier, classe, dossier..." autocomplete="off">
                </div>
                <div class="size-toggle">
                    <button class="size-btn active" data-size="loc">LOC</button>
                    <button class="size-btn" data-size="size">Octets</button>
                </div>
            </div>
            <div id="pills-bar"></div>
            <div id="treemap-wrap">
                <canvas id="treemap"></canvas>
                <div id="map-tooltip"><div class="tt-title"></div><div class="tt-sub"></div></div>
                <div id="map-hint">Survolez pour détailler • Cliquez un fichier pour l'inspecter • Cliquez un module pour zoomer</div>
            </div>
            <aside id="inspector">
                <div class="insp-head">
                    <div><span class="insp-module" id="iModule">Module</span><div class="insp-title" id="iTitle">—</div><div class="insp-path" id="iPath">—</div></div>
                    <button class="insp-close" id="iClose">✕</button>
                </div>
                <div class="meta-grid">
                    <div class="meta-cell"><label>Lignes</label><span id="iLoc">—</span></div>
                    <div class="meta-cell"><label>Taille</label><span id="iSize">—</span></div>
                    <div class="meta-cell"><label>Classes</label><span id="iClasses">—</span></div>
                    <div class="meta-cell"><label>Méthodes</label><span id="iMethods">—</span></div>
                </div>
                <div><div class="sec-title" style="margin-bottom:6px">Composition des lignes</div><div class="comp-bar"><div id="cCode" style="background:#38bdf8"></div><div id="cComment" style="background:#a855f7"></div><div id="cBlank" style="background:rgba(255,255,255,.15)"></div></div><div class="comp-legend"><span><i style="background:#38bdf8"></i>Code <b id="lCode"></b></span><span><i style="background:#a855f7"></i>Commentaires <b id="lComment"></b></span><span><i style="background:rgba(255,255,255,.25)"></i>Vides <b id="lBlank"></b></span></div></div>
                <div><div class="sec-title" style="margin-bottom:6px">Espace de noms</div><div class="chip" id="iNs">—</div></div>
                <div><div class="sec-title" style="margin-bottom:6px">Classes / structs / enums</div><div class="class-chips" id="iClasses2"></div></div>
                <div id="iSibWrap" style="display:none"><div class="sec-title" style="margin-bottom:6px">Fichiers du même groupe partiel</div><div id="iSibs"></div></div>
                <a href="#" target="_blank" rel="noopener" id="iGithub" class="btn-ghost" style="justify-content:center;display:none">Voir le fichier sur GitHub ↗</a>
            </aside>
        </section>
    </div>
</div>

<div id="help-modal">
    <div class="modal-content">
        <h2 style="font-family:var(--font-title);color:#fbbf24">🗂️ Cartographie des Fichiers</h2>
        <p style="font-size:13px;line-height:1.6;color:#cbd5e1">Chaque <strong>rectangle = un fichier</strong> du code maison. Sa <strong>surface</strong> est proportionnelle à son nombre de lignes (ou ses octets), sa <strong>couleur</strong> à son module. Les rectangles sont regroupés par module via l'algorithme <span style="font-family:var(--font-mono)">squarify</span>.</p>
        <div style="background:rgba(0,0,0,.3);border-radius:12px;padding:14px;border:1px solid var(--border-subtle);font-size:12px;display:flex;flex-direction:column;gap:8px;">
            <div>🔍 <strong>Recherche :</strong> surligne en doré les fichiers, classes et dossiers correspondants.</div>
            <div>🏷️ <strong>Pastilles & légende :</strong> cliquez pour masquer / réafficher un module.</div>
            <div>🔎 <strong>Zoom :</strong> cliquez l'en-tête d'un module pour n'afficher que lui.</div>
            <div>📄 <strong>Inspecteur :</strong> cliquez un fichier — composition code/commentaires/vides, classes, namespace, frères partiels, lien GitHub figé au commit.</div>
            <div>🧩 <strong>Monolithes découpés :</strong> les 12 groupes <span style="font-family:var(--font-mono)">partial</span> issus des refactors, avec la part de chaque fichier.</div>
        </div>
        <button class="btn-ghost" id="helpClose" style="align-self:flex-end;color:#fff;border-color:#fbbf24">Compris, explorer !</button>
    </div>
</div>

<script id="files-data" type="application/json">
{files_json_str}
</script>

<script>
(function() {{
'use strict';
let DATA;
try {{ DATA = JSON.parse(document.getElementById('files-data').textContent); }}
catch (e) {{ console.error('JSON invalide', e); return; }}
const FILES = DATA.files, MODULES = DATA.modules, GROUPS = DATA.partial_groups, TOTALS = DATA.totals;
const byId = {{}};
FILES.forEach(f => byId[f.id] = f);
const modOf = {{}};
MODULES.forEach(m => modOf[m.id] = m);

const state = {{ zoomModule: null, sizeMode: 'loc', query: '', active: new Set(MODULES.map(m => m.id)), selectedId: null, hover: null, rects: [] }};
const fmt = n => n.toLocaleString('fr-FR');
const fmtB = n => n > 1048576 ? (n/1048576).toFixed(1)+' Mo' : n > 1024 ? (n/1024).toFixed(1)+' ko' : n+' o';
function shade(hex, amt) {{
    const v = parseInt(hex.slice(1), 16);
    let r = (v>>16)+amt, g = ((v>>8)&255)+amt, b = (v&255)+amt;
    r = Math.max(0,Math.min(255,r)); g = Math.max(0,Math.min(255,g)); b = Math.max(0,Math.min(255,b));
    return '#'+((r<<16)|(g<<8)|b).toString(16).padStart(6,'0');
}}
const valOf = f => state.sizeMode === 'loc' ? f.loc : f.size;
const matchOf = f => {{
    if (!state.query) return true;
    const q = state.query.toLowerCase();
    return f.name.toLowerCase().includes(q) || f.dir.toLowerCase().includes(q) ||
           (f.namespace||'').toLowerCase().includes(q) || f.classes.some(c => c.name.toLowerCase().includes(q));
}};

/* ---------- Squarify ---------- */
function squarify(items, x, y, w, h) {{
    const out = [];
    if (!items.length || w <= 0 || h <= 0) return out;
    const total = items.reduce((s,i) => s+i.value, 0);
    if (total <= 0) return out;
    const scale = (w*h)/total;
    let row = [], rowSum = 0, cx = x, cy = y, cw = w, ch = h;
    const worst = (r, s) => {{
        if (!r.length) return Infinity;
        const mx = Math.max(...r.map(i => i.value*scale)), mn = Math.min(...r.map(i => i.value*scale));
        const side = Math.min(cw, ch);
        return Math.max((side*side*mx)/(s*s), (s*s)/(side*side*mn));
    }};
    const layoutRow = () => {{
        const horizontal = cw >= ch;
        const thickness = rowSum*scale / (horizontal ? cw : ch);
        let off = 0;
        row.forEach(it => {{
            const len = (it.value*scale) / thickness;
            out.push(horizontal ? {{...it, x: cx+off, y: cy, w: len, h: thickness}}
                               : {{...it, x: cx, y: cy+off, w: thickness, h: len}});
            off += len;
        }});
        if (horizontal) {{ cy += thickness; ch -= thickness; }} else {{ cx += thickness; cw -= thickness; }}
        row = []; rowSum = 0;
    }};
    const sorted = [...items].sort((a,b) => b.value - a.value);
    sorted.forEach(it => {{
        if (worst([...row, it], rowSum + it.value*scale) <= worst(row, rowSum) || !row.length) {{
            row.push(it); rowSum += it.value*scale;
        }} else {{ layoutRow(); row.push(it); rowSum += it.value*scale; }}
    }});
    if (row.length) layoutRow();
    return out;
}}

/* ---------- Canvas ---------- */
const canvas = document.getElementById('treemap');
const wrap = document.getElementById('treemap-wrap');
const ctx = canvas.getContext('2d');
const tooltip = document.getElementById('map-tooltip');
function resize() {{
    const r = wrap.getBoundingClientRect(), dpr = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, r.width*dpr); canvas.height = Math.max(1, r.height*dpr);
    draw();
}}
new ResizeObserver(resize).observe(wrap);

function computeLayout() {{
    const W = canvas.width, H = canvas.height;
    state.rects = [];
    const vis = f => state.active.has(f.module);
    if (state.zoomModule) {{
        const fs = FILES.filter(f => f.module === state.zoomModule && vis(f) && valOf(f) > 0);
        squarify(fs.map(f => ({{value: valOf(f), file: f}})), 4, 4, W-8, H-8)
            .forEach(r => state.rects.push({{kind:'file', file: r.file, x: r.x, y: r.y, w: r.w, h: r.h}}));
    }} else {{
        const mods = MODULES.filter(m => state.active.has(m.id));
        const mRects = squarify(mods.map(m => ({{value: FILES.filter(f => f.module===m.id && vis(f)).reduce((s,f)=>s+valOf(f),0), mod: m}})), 4, 4, W-8, H-8);
        mRects.forEach(mr => {{
            state.rects.push({{kind:'module', mod: mr.mod, x: mr.x, y: mr.y, w: mr.w, h: mr.h}});
            const fs = FILES.filter(f => f.module === mr.mod.id && vis(f) && valOf(f) > 0);
            squarify(fs.map(f => ({{value: valOf(f), file: f}})), mr.x+3, mr.y+27, mr.w-6, mr.h-30)
                .forEach(r => state.rects.push({{kind:'file', file: r.file, x: r.x, y: r.y, w: r.w, h: r.h}}));
        }});
    }}
}}

function draw() {{
    computeLayout();
    const dpr = window.devicePixelRatio || 1;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    const S = dpr;
    // fonds modules
    state.rects.filter(r => r.kind === 'module').forEach(r => {{
        ctx.save();
        ctx.fillStyle = 'rgba(255,255,255,0.025)';
        ctx.strokeStyle = r.mod.color + '55';
        ctx.lineWidth = 1.5*S;
        roundRect(r.x, r.y, r.w, r.h, 8*S); ctx.fill(); ctx.stroke();
        ctx.fillStyle = r.mod.color;
        ctx.fillRect(r.x+8*S, r.y+8*S, 10*S, 10*S);
        ctx.fillStyle = '#e2e8f0';
        ctx.font = `700 ${{12*S}}px "Plus Jakarta Sans", sans-serif`;
        ctx.textBaseline = 'middle';
        const label = `${{r.mod.label}} — ${{r.mod.files}} fichiers • ${{fmt(r.mod.loc)}} lignes`;
        ctx.fillText(label, r.x+24*S, r.y+14*S, Math.max(0, r.w-30*S));
        ctx.restore();
    }});
    // fichiers
    const fileRects = state.rects.filter(r => r.kind === 'file');
    const maxV = Math.max(...fileRects.map(r => valOf(r.file)), 1);
    fileRects.forEach(r => {{
        const f = r.file, m = modOf[f.module];
        const t = Math.log(1+valOf(f)) / Math.log(1+maxV);
        ctx.save();
        ctx.fillStyle = shade(m.color, -70 + Math.round(t*55));
        if (state.query && !matchOf(f)) ctx.globalAlpha = 0.14;
        ctx.strokeStyle = 'rgba(3,7,18,0.9)';
        ctx.lineWidth = 1*S;
        roundRect(r.x+0.5, r.y+0.5, Math.max(1,r.w-1), Math.max(1,r.h-1), 3*S); ctx.fill(); ctx.stroke();
        if (state.query && matchOf(f)) {{
            ctx.strokeStyle = '#fbbf24'; ctx.lineWidth = 2*S;
            roundRect(r.x+1, r.y+1, Math.max(1,r.w-2), Math.max(1,r.h-2), 3*S); ctx.stroke();
        }}
        if (state.hover === f.id || state.selectedId === f.id) {{
            ctx.strokeStyle = '#ffffff'; ctx.lineWidth = 2*S;
            roundRect(r.x+1, r.y+1, Math.max(1,r.w-2), Math.max(1,r.h-2), 3*S); ctx.stroke();
        }}
        if (r.w > 74*S && r.h > 30*S) {{
            ctx.fillStyle = 'rgba(255,255,255,0.92)';
            ctx.font = `${{10*S}}px "JetBrains Mono", monospace`;
            ctx.textBaseline = 'top';
            ctx.fillText(f.name, r.x+5*S, r.y+4*S, r.w-10*S);
            if (r.h > 46*S) {{
                ctx.fillStyle = 'rgba(255,255,255,0.6)';
                ctx.fillText(fmt(valOf(f)) + (state.sizeMode==='loc'?' l.':''), r.x+5*S, r.y+18*S, r.w-10*S);
            }}
        }} else if (r.w > 26*S && r.h > 22*S) {{
            ctx.fillStyle = 'rgba(255,255,255,0.5)';
            ctx.font = `${{9*S}}px "JetBrains Mono", monospace`;
            ctx.textBaseline = 'top';
            ctx.fillText('•', r.x+4*S, r.y+3*S);
        }}
        ctx.restore();
    }});
}}

function roundRect(x, y, w, h, r) {{
    r = Math.min(r, w/2, h/2);
    ctx.beginPath();
    ctx.moveTo(x+r, y);
    ctx.arcTo(x+w, y, x+w, y+h, r);
    ctx.arcTo(x+w, y+h, x, y+h, r);
    ctx.arcTo(x, y+h, x, y, r);
    ctx.arcTo(x, y, x+w, y, r);
    ctx.closePath();
}}

function hit(mx, my) {{
    const r = canvas.getBoundingClientRect(), dpr = window.devicePixelRatio || 1;
    const x = (mx-r.left)*dpr, y = (my-r.top)*dpr;
    for (let i = state.rects.length-1; i >= 0; i--) {{
        const q = state.rects[i];
        if (x >= q.x && x <= q.x+q.w && y >= q.y && y <= q.y+q.h) return q;
    }}
    return null;
}}

canvas.addEventListener('mousemove', e => {{
    const q = hit(e.clientX, e.clientY);
    const wrapR = wrap.getBoundingClientRect();
    if (q && q.kind === 'file') {{
        state.hover = q.file.id;
        canvas.style.cursor = 'pointer';
        tooltip.style.display = 'block';
        tooltip.querySelector('.tt-title').textContent = q.file.name;
        const m = modOf[q.file.module];
        tooltip.querySelector('.tt-sub').textContent =
            `${{m.label}} • ${{fmt(q.file.loc)}} lignes • ${{fmtB(q.file.size)}} • ${{q.file.n_classes}} classes`;
        let tx = e.clientX - wrapR.left + 16, ty = e.clientY - wrapR.top - 10;
        if (tx + 300 > wrapR.width) tx -= 320;
        tooltip.style.left = tx+'px'; tooltip.style.top = Math.max(4,ty-50)+'px';
    }} else if (q && q.kind === 'module') {{
        state.hover = null;
        canvas.style.cursor = 'zoom-in';
        tooltip.style.display = 'block';
        tooltip.querySelector('.tt-title').textContent = q.mod.label;
        tooltip.querySelector('.tt-sub').textContent = `${{q.mod.files}} fichiers • ${{fmt(q.mod.loc)}} lignes — cliquer pour zoomer`;
        tooltip.style.left = (e.clientX-wrapR.left+16)+'px'; tooltip.style.top = (e.clientY-wrapR.top-10)+'px';
    }} else {{
        state.hover = null; canvas.style.cursor = 'default'; tooltip.style.display = 'none';
    }}
    draw();
}});
canvas.addEventListener('mouseleave', () => {{ tooltip.style.display = 'none'; state.hover = null; draw(); }});
canvas.addEventListener('click', e => {{
    const q = hit(e.clientX, e.clientY);
    if (!q) return;
    if (q.kind === 'file') selectFile(q.file.id);
    else if (q.kind === 'module' && !state.zoomModule) setZoom(q.mod.id);
}});

/* ---------- Zoom / filtres / recherche ---------- */
const crumbAll = document.getElementById('crumbAll');
const crumbLabel = document.getElementById('crumbLabel');
function setZoom(modId) {{
    state.zoomModule = modId;
    crumbAll.style.display = modId ? '' : 'none';
    crumbLabel.textContent = modId
        ? `${{modOf[modId].label}} — ${{modOf[modId].files}} fichiers • surface = ${{state.sizeMode==='loc'?'lignes de code':'octets'}}`
        : `Tous les modules — surface = ${{state.sizeMode==='loc'?'lignes de code':'octets'}}`;
    draw();
}}
crumbAll.addEventListener('click', () => setZoom(null));

function toggleModule(id) {{
    if (state.active.has(id)) {{ if (state.active.size > 1) state.active.delete(id); }}
    else state.active.add(id);
    if (state.zoomModule && !state.active.has(state.zoomModule)) setZoom(null);
    renderSide(); draw();
}}
document.getElementById('searchInput').addEventListener('input', e => {{
    state.query = e.target.value.trim();
    draw();
}});
document.querySelectorAll('.size-btn').forEach(b => b.addEventListener('click', () => {{
    document.querySelectorAll('.size-btn').forEach(x => x.classList.remove('active'));
    b.classList.add('active');
    state.sizeMode = b.dataset.size;
    setZoom(state.zoomModule);
}}));

const pillsBar = document.getElementById('pills-bar');
MODULES.forEach(m => {{
    const b = document.createElement('button');
    b.className = 'pill'; b.dataset.mod = m.id;
    b.innerHTML = `<span class="dot" style="background:${{m.color}}"></span>${{m.label}} <span style="opacity:.6;font-family:var(--font-mono)">${{m.files}}</span>`;
    b.addEventListener('click', () => toggleModule(m.id));
    pillsBar.appendChild(b);
}});

/* ---------- Sidebar ---------- */
function renderSide() {{
    // Donut
    const svg = document.getElementById('donut');
    const total = MODULES.filter(m => state.active.has(m.id)).reduce((s,m) => s+m.loc, 0) || 1;
    const R = 46, C = 2*Math.PI*R;
    let acc = 0, html = '';
    MODULES.forEach(m => {{
        if (!state.active.has(m.id)) return;
        const frac = m.loc/total, len = frac*C;
        html += `<circle cx="60" cy="60" r="${{R}}" fill="none" stroke="${{m.color}}" stroke-width="16" stroke-dasharray="${{len.toFixed(1)}} ${{(C-len).toFixed(1)}}" stroke-dashoffset="${{(-acc*C).toFixed(1)}}" transform="rotate(-90 60 60)" opacity="0.9"><title>${{m.label}} — ${{fmt(m.loc)}} lignes</title></circle>`;
        acc += frac;
    }});
    html += `<text x="60" y="56" text-anchor="middle" fill="#fff" font-size="15" font-weight="700" font-family="JetBrains Mono, monospace">${{TOTALS.files}}</text><text x="60" y="72" text-anchor="middle" fill="#64748b" font-size="10" font-family="JetBrains Mono, monospace">fichiers</text>`;
    svg.innerHTML = html;
    // Legende
    document.getElementById('donut-legend').innerHTML = MODULES.map(m =>
        `<div class="legend-row ${{state.active.has(m.id)?'':'off'}}" data-mod="${{m.id}}"><span class="legend-dot" style="background:${{m.color}}"></span>${{m.label}}<span class="pct">${{(100*m.loc/TOTALS.loc).toFixed(1)}}%</span></div>`
    ).join('');
    document.querySelectorAll('.legend-row').forEach(r => r.addEventListener('click', () => toggleModule(r.dataset.mod)));
    // Barres modules
    const mx = Math.max(...MODULES.map(m => m.loc));
    document.getElementById('mod-hint').textContent = `${{state.active.size}}/${{MODULES.length}} actifs`;
    document.getElementById('module-bars').innerHTML = MODULES.map(m =>
        `<div class="bar-row ${{state.active.has(m.id)?'':'off'}}" data-mod="${{m.id}}"><div class="bar-top"><span class="bname"><span class="legend-dot" style="background:${{m.color}}"></span>${{m.label}}</span><span class="bval">${{m.files}} f. • ${{fmt(m.loc)}} l.</span></div><div class="bar-track"><div class="bar-fill" style="width:${{(100*m.loc/mx).toFixed(1)}}%;background:${{m.color}}"></div></div></div>`
    ).join('');
    document.querySelectorAll('.bar-row').forEach(r => r.addEventListener('click', () => toggleModule(r.dataset.mod)));
    document.querySelectorAll('.pill').forEach(p => p.classList.toggle('off', !state.active.has(p.dataset.mod)));
    // Top fichiers
    document.getElementById('top-files').innerHTML = FILES.slice(0, 15).map((f, i) =>
        `<div class="topfile-row" data-id="${{f.id}}"><span class="rank">${{i+1}}</span><span class="tname" title="${{f.dir}}/${{f.name}}">${{f.name}}</span><span class="tloc">${{fmt(f.loc)}}</span></div>`
    ).join('');
    document.querySelectorAll('.topfile-row').forEach(r => r.addEventListener('click', () => selectFile(+r.dataset.id)));
    // Groupes partiels
    document.getElementById('groups').innerHTML = GROUPS.map((g, i) => {{
        const spans = g.files.map((f, j) => `<div style="width:${{(100*f.loc/g.loc).toFixed(1)}}%;background:${{shade(g.color, -60+Math.round(70*j/Math.max(1,g.files.length-1)))}}" title="${{f.name}} — ${{fmt(f.loc)}} lignes"></div>`).join('');
        return `<div class="group-card" data-g="${{i}}"><div class="group-head"><span>${{g.name}}</span><span class="gloc">${{g.files.length}} f. • ${{fmt(g.loc)}} l.</span></div><div class="stack-bar">${{spans}}</div><div class="group-sub">${{g.files.slice(0,3).map(f=>f.name).join(' · ')}}${{g.files.length>3?' · …':''}}</div></div>`;
    }}).join('');
    document.querySelectorAll('.group-card').forEach(c => c.addEventListener('click', () => {{
        const g = GROUPS[+c.dataset.g];
        if (!state.active.has(g.module)) toggleModule(g.module);
        if (state.zoomModule !== g.module) setZoom(g.module);
        selectFile(g.files[0].id);
    }}));
}}

/* ---------- Inspecteur ---------- */
const insp = document.getElementById('inspector');
function selectFile(id) {{
    const f = byId[id];
    if (!f) return;
    state.selectedId = id;
    const m = modOf[f.module];
    const tag = document.getElementById('iModule');
    tag.textContent = m.label;
    tag.style.background = m.color+'22'; tag.style.border = `1px solid ${{m.color}}66`; tag.style.color = m.color;
    document.getElementById('iTitle').textContent = f.name;
    document.getElementById('iPath').textContent = (f.module==='Data'||f.module==='Shaders'||f.module==='Scenes' ? 'Assets/' : 'Assets/Scripts/') + f.dir + '/' + f.name;
    document.getElementById('iLoc').textContent = fmt(f.loc);
    document.getElementById('iSize').textContent = fmtB(f.size);
    document.getElementById('iClasses').textContent = f.n_classes;
    document.getElementById('iMethods').textContent = f.type==='json' ? f.properties+' clés' : fmt(f.methods);
    const tot = f.loc || 1;
    document.getElementById('cCode').style.width = (100*f.code/tot)+'%';
    document.getElementById('cComment').style.width = (100*f.comment/tot)+'%';
    document.getElementById('cBlank').style.width = (100*f.blank/tot)+'%';
    document.getElementById('lCode').textContent = fmt(f.code);
    document.getElementById('lComment').textContent = fmt(f.comment);
    document.getElementById('lBlank').textContent = fmt(f.blank);
    document.getElementById('iNs').textContent = f.namespace || (f.type==='json' ? 'JSON — '+f.properties+' clés racine' : '— (global)');
    document.getElementById('iClasses2').innerHTML = f.classes.length
        ? f.classes.map(c => `<span class="chip kind-${{c.kind}}">${{c.name}}</span>`).join('')
        : '<span style="font-size:12px;color:var(--text-muted)">Aucune déclaration détectée</span>';
    const sibWrap = document.getElementById('iSibWrap');
    if (f.partial_group && GROUPS.some(g => g.name === f.partial_group)) {{
        const g = GROUPS.find(g => g.name === f.partial_group);
        sibWrap.style.display = '';
        document.getElementById('iSibs').innerHTML = g.files.map(s =>
            `<button class="sib-btn" data-id="${{s.id}}">${{s.id===id?'▸ ':'&nbsp;&nbsp;'}}${{s.name}} — ${{fmt(s.loc)}} l.</button>`).join('');
        document.querySelectorAll('.sib-btn').forEach(b => b.addEventListener('click', () => selectFile(+b.dataset.id)));
    }} else sibWrap.style.display = 'none';
    const gh = document.getElementById('iGithub');
    if (f.github_sha && f.repo_path) {{
        gh.style.display = '';
        gh.href = `https://github.com/k0r0z1f/Killtime/blob/${{f.github_sha}}/${{f.repo_path}}`;
        gh.textContent = f.worktree === 'modified'
            ? `Voir la version commitée sur GitHub ↗ (${{f.github_sha.slice(0, 7)}})`
            : 'Voir le fichier sur GitHub ↗';
        gh.title = f.worktree === 'modified'
            ? 'Le disque contient des modifs non commitées ; GitHub montre la version commitée'
            : `Ouvrir ${{f.repo_path}} au commit ${{f.github_sha.slice(0, 7)}}`;
    }} else {{
        gh.style.display = 'none';
    }}
    insp.classList.add('open');
    draw();
}}
document.getElementById('iClose').addEventListener('click', () => {{ insp.classList.remove('open'); state.selectedId = null; draw(); }});
document.addEventListener('keydown', e => {{ if (e.key === 'Escape') {{ insp.classList.remove('open'); state.selectedId = null; draw(); }} }});

/* ---------- Modale ---------- */
const modal = document.getElementById('help-modal');
document.getElementById('helpBtn').addEventListener('click', () => modal.style.display = 'flex');
document.getElementById('helpClose').addEventListener('click', () => modal.style.display = 'none');
modal.addEventListener('click', e => {{ if (e.target === modal) modal.style.display = 'none'; }});

renderSide();
resize();
}})();
</script>
</body>
</html>
'''
    with open("tactics_files.html", "w", encoding="utf-8") as f:
        f.write(html_template)
    print("tactics_files.html genere avec succes!")


def main():
    data = scan()
    t = data["totals"]
    print(f"JSON: fichiers={t['files']} LOC={t['loc']} classes={t['classes']}")
    build_html(data)


if __name__ == "__main__":
    main()

