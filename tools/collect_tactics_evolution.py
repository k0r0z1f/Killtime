#!/usr/bin/env python3
"""
Collecteur amont de tactics_evolution.html (manquant jusqu'au 2026-10-13).
1. Charge data/tactics_evolution.json existant (noeuds cures 1..N preserves verbatim).
2. Ajoute les commits git posterieurs au dernier hash connu (1 noeud = 1 commit).
3. Recalcule date_range, epochs (ep6 etendue), links chronologiques + causaux.
Ecrit data/tactics_evolution.json. Relancer tools/generate_tactics_evolution.py ensuite.
Usage: python3 tools/collect_tactics_evolution.py
"""

import json
import os
import re
import subprocess
from collections import Counter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EVO_PATH = os.path.join(ROOT, "data", "tactics_evolution.json")
REPO = "https://github.com/k0r0z1f/Killtime"
SCOPE_PREFIX = "KilltimeTactics/"
TRIVIAL_MSGS = {"update", "updates", "pdate", "uypdate", "refactor", "refactors", "UPDATE"}

DIR_BRANCH = [
    ("Audio", "audio"), ("Story", "story"), ("Multi", "network"),
    ("WebGL", "network"), ("Camera", "visuals"), ("Shaders", "visuals"),
    ("Tools", "visuals"), ("DevTools", "tools"), ("Tactics", "tactics"),
    ("Core", "core"),
]


def sh(*args):
    return subprocess.run(args, cwd=ROOT, capture_output=True, text=True).stdout


def file_type(path):
    if path.endswith(".cs"):
        return "csharp"
    if path.endswith(".shader"):
        return "shader"
    if path.endswith(".unity"):
        return "scene"
    if path.endswith(".json"):
        return "json"
    if path.endswith(".asmdef"):
        return "asmdef"
    return "other"


def branch_of(paths):
    votes = Counter()
    for p in paths:
        if p.endswith(".meta") or "/Tests/" in p or p.startswith("Assets/Scripts/Tests"):
            continue
        rel = p[len("Assets/"):] if p.startswith("Assets/") else p
        top = rel.split("/")[1] if rel.startswith("Scripts/") else rel.split("/")[0]
        for key, br in DIR_BRANCH:
            if top == key:
                votes[br] += 1
                break
    if not votes:
        return "core"
    return votes.most_common(1)[0][0]


def derive_title(raw_msg, added_partials):
    msg = (raw_msg or "").strip()
    if msg and msg.lower() not in TRIVIAL_MSGS and len(msg) > 15:
        return msg[:72]
    if added_partials:
        base, members = max(added_partials.items(), key=lambda kv: len(kv[1]))
        others = sorted({m.split(".")[1] for m in members if "." in m and len(m.split(".")) > 2})
        suffix = " : " + " & ".join(others[:2]) if others else ""
        tag = ""
        m = re.search(r"RD-\d+", msg or "")
        if m:
            tag = f" ({m.group(0)})"
        return f"{base} en {len(members)} Partiels{suffix}{tag}"
    return msg[:72] if msg else "Commit"


def derive_summary(short, date_display, ins, dels, files, raw_msg):
    names = [f["name"] for f in files if f["type"] == "csharp"][:3]
    detail = ", ".join(names) if names else f"{len(files)} fichiers"
    return (f"{short} ({date_display}) : {detail} — +{ins}/-{dels}. "
            f"« {raw_msg.strip()[:90]} »")


def collect_new_commits(after_hash):
    log = sh("git", "log", "--reverse", "--name-status", "--format=@@%H@%h@%ad@%an@%s",
             "--date=iso", after_hash + "..HEAD", "--", "KilltimeTactics")
    commits = []
    cur = None
    for line in log.splitlines():
        if line.startswith("@@"):
            if cur:
                commits.append(cur)
            h, shrt, date, author, msg = line[2:].split("@", 4)
            cur = {"hash": h, "short": shrt, "date": date,
                   "date_display": date[:10], "author": author,
                   "raw_msg": msg, "files": []}
        elif cur is not None and line.strip():
            parts = line.split("\t")
            status = parts[0][0]
            if status in ("R", "C") and len(parts) == 3:
                for p in parts[1:]:
                    p = p.strip()
                    if p and p.startswith(SCOPE_PREFIX):
                        cur["files"].append({"status": status, "raw": p})
            else:
                p = parts[-1].strip()
                if p and p.startswith(SCOPE_PREFIX):
                    cur["files"].append({"status": status, "raw": p})
    if cur:
        commits.append(cur)
    return commits


def enrich(commit):
    numstat = sh("git", "show", "--numstat", "--format=", commit["hash"], "--", "KilltimeTactics")
    ins = dels = 0
    stats = {}
    for line in numstat.splitlines():
        parts = line.split("\t")
        if len(parts) < 3:
            continue
        try:
            i, d = int(parts[0]), int(parts[1])
        except ValueError:
            continue
        ins += i
        dels += d
        for p in parts[2:]:
            p = p.strip().strip("{}")
            if SCOPE_PREFIX in p:
                p = p[p.index(SCOPE_PREFIX):]
                stats[p] = (i, d)
    files = []
    seen = set()
    for f in commit["files"]:
        full = f["raw"]
        disp = full[len(SCOPE_PREFIX):] if full.startswith(SCOPE_PREFIX) else full
        if disp in seen:
            continue
        seen.add(disp)
        name = disp.split("/")[-1]
        # convention historique : chemins relatifs a KilltimeTactics/ (Assets/...)
        files.append({"status": f["status"], "name": name, "path": disp,
                      "type": file_type(disp)})
    return files, ins, dels


def main():
    with open(EVO_PATH, encoding="utf-8") as f:
        data = json.load(f)
    nodes = data["nodes"]
    last_hash = nodes[-1]["hash"]
    print(f"Noeuds cures charges: {len(nodes)}, dernier={nodes[-1]['short']}")

    new_commits = collect_new_commits(last_hash)
    print(f"Nouveaux commits depuis {nodes[-1]['short']}: {len(new_commits)}")
    if not new_commits:
        print("Rien a ajouter.")
        return

    next_id = len(nodes) + 1
    for c in new_commits:
        files, ins, dels = enrich(c)
        paths = [f["path"] for f in files]
        added = [f for f in files if f["status"] == "A" and f["type"] == "csharp"]
        partials = {}
        for f in added:
            if "." in f["name"][:-3]:
                partials.setdefault(f["name"].split(".")[0], []).append(f["name"])
        partials = {k: v for k, v in partials.items() if len(v) >= 3}
        branch = branch_of(paths)
        epoch = next((e["id"] for e in data["epochs"] if e["start"] <= c["date_display"] <= e["end"]), None)
        if epoch is None and c["date_display"] > data["epochs"][-1]["end"]:
            epoch = data["epochs"][-1]["id"]
        milestone = None
        m = re.search(r"RD-\d+", c["raw_msg"] or "")
        if m and ("refactor" in c["raw_msg"].lower() or "perf" in c["raw_msg"].lower()
                  or "fix" in c["raw_msg"].lower()):
            detail = c["raw_msg"][:60]
            milestone = f"🛠️ {m.group(0)} : {detail}"
        node = {
            "id": next_id,
            "hash": c["hash"],
            "short": c["short"],
            "date": c["date"],
            "date_display": c["date_display"],
            "author": c["author"],
            "raw_msg": c["raw_msg"],
            "title": derive_title(c["raw_msg"], partials),
            "summary": derive_summary(c["short"], c["date_display"], ins, dels, files, c["raw_msg"]),
            "branch": branch,
            "epoch": epoch,
            "milestone": milestone,
            "insertions": ins,
            "deletions": dels,
            "files_count": len(files),
            "files": files,
        }
        nodes.append(node)
        print(f"  + #{next_id} {c['short']} [{branch}] {node['title']} ({len(files)} f, +{ins}/-{dels})")
        next_id += 1

    # Floraison complete -> dernier noeud
    for n in nodes:
        if (n.get("milestone") or "").startswith("🌸 Floraison complète"):
            n["milestone"] = None
    nodes[-1]["milestone"] = f"🌸 Floraison complète : {len(nodes)} commits"

    # date_range + derniere epoque etendue a la date du dernier noeud
    data["date_range"]["end"] = nodes[-1]["date_display"]
    last_ep = data["epochs"][-1]
    last_ep["end"] = nodes[-1]["date_display"]
    ep6 = next((e for e in data["epochs"] if e["id"] == "ep6"), None)
    if ep6 is not None and "RD-138" not in ep6["desc"]:
        ep6["desc"] += (" Découpage audio (KilltimeAudioManager, ProceduralAudioFactory, "
                        "SystemAudioChameleon), SkillTreeCosmosWindow (RD-138), VTTRoomWindow, "
                        "HexGridVisualizer, voûte céleste LOD/culling (RD-139).")

    # links chronologiques reconstruits
    data["links"] = [{"source": n["id"], "target": n["id"] + 1,
                      "type": "chronological", "label": "Chronologie Git"}
                     for n in nodes[:-1]]
    # causaux : chaque nouveau noeud prolonge le precedent de meme branche
    for a, b in zip(nodes, nodes[1:]):
        if b["id"] > len(nodes) - len(new_commits) and a["branch"] == b["branch"]:
            data.setdefault("cross_links", []).append({
                "source": a["id"], "target": b["id"], "type": "causal",
                "reason": f"{a['title'][:40]} vers {b['title'][:40]}"})

    data["total_commits"] = len(nodes)
    with open(EVO_PATH, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print(f"Ecrit {EVO_PATH}: {len(nodes)} commits, {nodes[0]['date_display']} -> {nodes[-1]['date_display']}")


if __name__ == "__main__":
    main()
