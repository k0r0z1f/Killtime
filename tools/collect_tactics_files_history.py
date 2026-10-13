#!/usr/bin/env python3
"""
Collecteur amont de tactics_fleur_fichiers.html (manquant jusqu'au 2026-10-13).
1. Scanne le worktree (Scripts/**/*.cs, Shaders, scenes, JSON, asmdefs).
2. Retrace chaque fichier via `git log --follow --numstat` (LOC cumulee -> growth).
3. Recalcule coupling / heritage / partials / tests, hotspot, milestones, totaux.
4. Fichiers ScenarioEditor en cours d'edition (PIN) : etat HEAD commite, jamais worktree.
Ecrit data/tactics_files_history.json. Relancer tools/generate_tactics_fleur_fichiers.py ensuite.
Usage: python3 tools/collect_tactics_files_history.py
"""

import json
import os
import re
import subprocess
import sys
from collections import defaultdict
from datetime import datetime

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from generate_tactics_files import analyze_text  # noqa: E402

HIST_PATH = os.path.join(ROOT, "data", "tactics_files_history.json")
ASSETS = os.path.join(ROOT, "KilltimeTactics", "Assets")

# Fichiers en cours d'edition cote utilisateur : on ignore le worktree (mi-redaction),
# on fige sur l'etat HEAD commite. Les PIN non presents dans HEAD sont sautes.
PIN_INFIX = "ScenarioEditorDevWindow.Cards."

DIR_BRANCH_FALLBACK = {
    "Audio": "audio", "Story": "recit", "Multi": "multi", "WebGL": "multi",
    "Camera": "camera", "Shaders": "visuels", "Tools": "visuels",
    "DevTools": "ateliers", "Tactics": "tactique", "Core": "personnage",
    "UI": "interface", "Tests": "tests",
}
NAME_BRANCH_HINTS = [
    ("test", "tests"), ("arena", "arene"), ("audio", "audio"), ("music", "audio"),
    ("camera", "camera"), ("cinematic", "recit"), ("story", "recit"),
    ("shader", "visuels"), ("vtt", "multi"), ("character", "personnage"),
    ("combat", "combat"), ("spell", "combat"), ("damage", "combat"),
]
INHERIT_RE = re.compile(r"^\s*(?:public|internal|private|protected)?\s*(?:sealed|abstract|static)?\s*"
                        r"(?:partial\s+)?(class|struct|interface|enum)\s+(\w+)"
                        r"[\w\s<>,?]*:\s*([A-Za-z_][\w\s,<>\?]*?)(?:\s*where\s|\s*\{|\s*$)", re.M)
SKIP_BASES = {"MonoBehaviour", "ScriptableObject", "Editor", "EditorWindow",
              "object", "Attribute", "Exception", "EventArgs"}


def sh(*args):
    return subprocess.run(args, cwd=ROOT, capture_output=True, text=True).stdout


def head_blob(repo_path):
    """Contenu commite (HEAD) d'un fichier, ou None si absent de HEAD."""
    p = subprocess.run(["git", "show", f"HEAD:{repo_path}"], cwd=ROOT,
                       capture_output=True, text=True)
    return p.stdout if p.returncode == 0 else None


def worktree_status():
    modified, untracked = set(), set()
    for line in sh("git", "status", "--porcelain").splitlines():
        code, path = line[:2], line[3:].strip().strip('"')
        if path.startswith("KilltimeTactics/"):
            if code == "??":
                untracked.add(path)
            elif code.strip() in ("M", "A", "R", "T", "MM", "AM"):
                modified.add(path)
    return modified, untracked


def scan_worktree():
    """Liste (repo_path, full_disk_path)."""
    out = []
    scripts = os.path.join(ASSETS, "Scripts")
    for root, _d, fns in os.walk(scripts):
        for fn in fns:
            if fn.endswith((".cs", ".asmdef")):
                full = os.path.join(root, fn)
                rel = os.path.relpath(full, ROOT)
                out.append((rel.replace(os.sep, "/"), full))
    shaders = os.path.join(ASSETS, "Shaders")
    if os.path.isdir(shaders):
        for fn in os.listdir(shaders):
            if fn.endswith(".shader"):
                out.append((f"KilltimeTactics/Assets/Shaders/{fn}",
                            os.path.join(shaders, fn)))
    for rel in ["Main.unity", "Scenes/CombatDevArena.unity"]:
        full = os.path.join(ASSETS, rel)
        if os.path.exists(full):
            out.append((f"KilltimeTactics/Assets/{rel}", full))
    resources = os.path.join(ASSETS, "Resources")
    for root, _d, fns in os.walk(resources):
        for fn in fns:
            if fn.endswith(".json"):
                full = os.path.join(root, fn)
                rel = os.path.relpath(full, ROOT)
                out.append((rel.replace(os.sep, "/"), full))
    return sorted(out)


def file_history(repo_path):
    """Historique git (plus vieux -> plus recent) : [(hash, short, date, author, msg, ins, del)]."""
    log = sh("git", "log", "--follow", "--format=@@%H@%h@%ad@%an@%s",
             "--date=iso", "--numstat", "--", repo_path)
    commits, cur, stats = [], None, []
    for line in log.splitlines():
        if line.startswith("@@"):
            if cur:
                commits.append((cur, stats))
            h, shrt, date, author, msg = line[2:].split("@", 4)
            cur = {"hash": h, "short": shrt, "date": date,
                   "date_display": date[:10], "author": author, "raw_msg": msg}
            stats = []
        elif cur is not None and line.strip():
            parts = line.split("\t")
            if len(parts) >= 3:
                try:
                    stats.append((int(parts[0]), int(parts[1])))
                except ValueError:
                    pass
    if cur:
        commits.append((cur, stats))
    commits.reverse()
    return commits


def analyze_source(repo_path, disk_path, pinned, blob_cache):
    if pinned:
        text = blob_cache.get(repo_path, head_blob(repo_path))
        blob_cache[repo_path] = text
        if text is None:
            return None, 0
        import tempfile
        with tempfile.NamedTemporaryFile("w", suffix=".cs", delete=False,
                                         encoding="utf-8") as t:
            t.write(text)
            tmp = t.name
        st = analyze_text(tmp)
        os.unlink(tmp)
        size = len(text.encode("utf-8", "replace"))
        return st, size
    if disk_path.endswith(".json"):
        try:
            with open(disk_path, encoding="utf-8", errors="replace") as f:
                content = f.read()
            obj = json.loads(content)
            keys = len(obj) if isinstance(obj, (dict, list)) else 0
        except (OSError, ValueError):
            content, keys = "", 0
        loc = content.count("\n") + 1 if content else 0
        return {"loc": loc, "blank": 0, "comment": 0, "code": loc, "classes": [],
                "n_classes": 0, "namespace": None, "methods": 0,
                "properties": keys, "partial": False}, os.path.getsize(disk_path)
    st = analyze_text(disk_path)
    return st, os.path.getsize(disk_path)


def branch_for(repo_path, disk_rel_dir, old_branch, dir_votes):
    if old_branch:
        return old_branch
    votes = dir_votes.get(disk_rel_dir)
    if votes:
        return max(votes.items(), key=lambda kv: kv[1])[0]
    rel = repo_path.split("KilltimeTactics/Assets/")[-1]
    if rel.startswith("Scripts/"):
        top = rel.split("/")[1]
        if top in DIR_BRANCH_FALLBACK:
            return DIR_BRANCH_FALLBACK[top]
    low = repo_path.lower()
    for hint, br in NAME_BRANCH_HINTS:
        if hint in low:
            return br
    if "Resources/" in repo_path or repo_path.endswith((".unity", ".json")):
        return "donnees"
    if repo_path.endswith(".shader"):
        return "visuels"
    return "ateliers"


def main():
    with open(HIST_PATH, encoding="utf-8") as f:
        old = json.load(f)
    old_by_path = {n["repo_path"]: n for n in old["nodes"]}
    branches = old["branches"]
    epochs = [dict(e) for e in old["epochs"]]
    head_date = sh("git", "log", "-1", "--format=%ad", "--date=short").strip() or "2026-10-13"

    modified, untracked = worktree_status()
    print(f"Worktree: {len(modified)} modifies, {len(untracked)} untracked")

    scanned = scan_worktree()
    print(f"Fichiers scannes: {len(scanned)}")

    # votes de branche par dossier (memoire ancienne)
    dir_votes = defaultdict(lambda: defaultdict(int))
    for n in old["nodes"]:
        d = "/".join(n["repo_path"].split("/")[:-1])
        dir_votes[d][n["branch"]] += 1

    entries = []
    blob_cache = {}
    skipped = []
    for repo_path, disk in scanned:
        pinned = PIN_INFIX in repo_path
        if pinned and repo_path in untracked:
            skipped.append(repo_path)  # mi-redaction, absent de HEAD : ignore
            continue
        st, size = analyze_source(repo_path, disk, pinned, blob_cache)
        if st is None:
            skipped.append(repo_path)
            continue
        if pinned:
            state = "clean"
        elif repo_path in untracked:
            state = "untracked"
        elif repo_path in modified:
            state = "modified"
        else:
            state = "clean"
        hist = [] if state == "untracked" else file_history(repo_path)
        if not hist and state != "untracked":
            skipped.append(repo_path + " (sans historique)")
            continue
        entries.append((repo_path, disk, st, size, state, hist))

    print(f"Noeuds: {len(entries)}, ignores: {len(skipped)}")
    for s in skipped:
        print(f"  ignore: {s}")

    # ordre chronologique de creation
    def create_key(e):
        _rp, _d, _st, _sz, state, hist = e
        if state == "untracked":
            try:
                ts = os.path.getmtime(_d)
                return datetime.fromtimestamp(ts).strftime("%Y-%m-%d %H:%M:%S -0400")
            except OSError:
                return head_date + " 23:59:59 -0400"
        return hist[0][0]["date"]
    entries.sort(key=lambda e: (create_key(e), e[0]))

    nodes, commit_sets = [], {}
    code_tot = methods_tot = 0
    for idx, entry in enumerate(entries, start=1):
        repo_path, _d, st, size, state, hist = entry
        code_tot += st.get("code", st["loc"])
        methods_tot += st.get("methods", 0)
        title = repo_path.split("/")[-1]
        if state == "untracked":
            first = last = None
            n_commits, ins_tot, del_tot = 0, 0, 0
            growth, rows = [], []
            wk_date = create_key(entry)
            author, raw, date_full, date_disp = "Alexis Lacasse", "worktree (non commité)", \
                wk_date, wk_date[:10]
            sha = None
        else:
            # LOC cumulee : ancre = premier ins-del, puis sommes
            loc = None
            growth, rows = [], []
            ins_tot = del_tot = 0
            cset = set()
            for c, stats in hist:
                i = sum(a for a, _b in stats)
                d = sum(b for _a, b in stats)
                ins_tot += i
                del_tot += d
                loc = (i - d) if loc is None else (loc + i - d)
                growth.append([c["date"], max(loc, 0)])
                rows.append({"status": "C",
                             "name": f"{c['short']} • {c['date_display']}  +{i}/-{d}",
                             "path": (c["raw_msg"] or "")[:80], "type": "commit"})
                cset.add(c["hash"])
            commit_sets[idx] = cset
            first, last = hist[0][0], hist[-1][0]
            n_commits = len(hist)
            author, raw = first["author"], first["raw_msg"]
            date_full, date_disp = first["date"], first["date_display"]
            sha = last["hash"]
        old_n = old_by_path.get(repo_path)
        epoch = next((e["id"] for e in epochs if e["start"] <= date_disp <= e["end"]), None)
        if epoch is None:
            epoch = epochs[-1]["id"] if date_disp >= epochs[0]["start"] else epochs[0]["id"]
        branch = branch_for(repo_path, "/".join(repo_path.split("/")[:-1]),
                            old_n["branch"] if old_n else None, dir_votes)
        loc_now, n_cls = st["loc"], st["n_classes"]
        names = [c["name"] for c in st["classes"][:6]]
        if state == "untracked":
            summary = (f"{title} — {branches[branch]['label']}, {loc_now} lignes "
                       f"(non commité, en cours de rédaction).")
            last_short = None
        else:
            summary = (f"{title} — {branches[branch]['label']}, {loc_now} lignes, "
                       f"{n_cls} classes ({', '.join(names)}). Créé le {date_disp} dans "
                       f"{first['short']} « {(raw or '')[:60]} », retouché dans {n_commits} "
                       f"commit(s), dernier le {last['date_display']} ({last['short']}). "
                       f"Évolution cumulée +{ins_tot}/-{del_tot}.")
            last_short = last["short"]
        if old_n and state != "untracked":
            hotspot = old_n["hotspot"]
        else:
            hotspot = (n_commits >= 10 and loc_now >= 300)
        nodes.append({
            "id": idx, "hash": first["hash"] if first else None,
            "short": first["short"] if first else None,
            "date": date_full, "date_display": date_disp, "author": author,
            "raw_msg": raw, "title": title, "summary": summary, "branch": branch,
            "epoch": epoch, "milestone": None, "insertions": ins_tot,
            "deletions": del_tot, "files_count": n_commits, "files": rows,
            "loc": loc_now, "size": size, "n_commits": n_commits,
            "first_short": first["short"] if first else None,
            "last_short": last_short, "last_hash": sha,
            "repo_path": repo_path, "namespace": st["namespace"],
            "class_names": [c["name"] for c in st["classes"][:12]],
            "n_classes": st["n_classes"],
            "tests": [], "hotspot": hotspot, "growth": growth,
            "worktree": state,
        })

    by_id = {n["id"]: n for n in nodes}

    # --- liaisons ---
    links = [{"source": n["id"], "target": n["id"] + 1,
              "type": "chronological", "label": "Création suivante"}
             for n in nodes[:-1]]

    cross = []
    ids = sorted(commit_sets)
    for a in range(len(ids)):
        for b in range(a + 1, len(ids)):
            w = len(commit_sets[ids[a]] & commit_sets[ids[b]])
            if w >= 7:
                cross.append({"source": ids[a], "target": ids[b],
                              "type": "coupling", "reason": f"{w} commits communs",
                              "weight": w})
    cross.sort(key=lambda c: -c["weight"])
    print(f"coupling: {len(cross)}")

    # heritage : parse declarations, resolve vers fichiers definissant la classe
    class_to_ids = defaultdict(list)
    for n in nodes:
        for c in n["class_names"]:
            class_to_ids[c].append(n["id"])
    inherit = []
    for n in nodes:
        if not n["repo_path"].endswith(".cs") or n["worktree"] == "untracked":
            continue
        if PIN_INFIX in n["repo_path"]:
            text = head_blob(n["repo_path"])
        else:
            try:
                with open(os.path.join(ROOT, n["repo_path"]), encoding="utf-8",
                          errors="replace") as f:
                    text = f.read()
            except OSError:
                text = None
        if not text:
            continue
        for m in INHERIT_RE.finditer(text):
                _kind, child, bases = m.group(1), m.group(2), m.group(3)
                for base in [x.strip() for x in bases.split(",")]:
                    base = re.sub(r"<.*>", "", base).strip()
                    if not base or base in SKIP_BASES or base in ("where",):
                        continue
                    for tid in class_to_ids.get(base, []):
                        if tid == n["id"]:
                            continue
                        is_iface = base.startswith("I") and len(base) > 1 and base[1].isupper()
                        inherit.append({"source": n["id"], "target": tid,
                                        "type": "inherit",
                                        "label": f"{child} : {base}",
                                        "reason": ("implémente " if is_iface else "hérite de ") + base})
                        break
    # dedup
    seen, inherit_u = set(), []
    for l in inherit:
        k = (l["source"], l["target"])
        if k not in seen:
            seen.add(k)
            inherit_u.append(l)
    print(f"heritage: {len(inherit_u)}")

    # partials : membres pointes (Base.X.cs, Base.X.Y.cs) partageant le 1er segment ;
    # le socle non pointe est la racine implicite (sans lien).
    # Cible (hub) = creation la plus ancienne, ex aequo -> plus gros LOC.
    groups = defaultdict(list)
    for n in nodes:
        t = n["title"]
        if t.endswith(".cs") and "." in t[:-3].rstrip("."):
            stem = t[:-3]
            if "." in stem:
                groups[t.split(".")[0]].append(n)
    dotted_groups = {b: sorted(ms, key=lambda n: (n["date"], -n["loc"], n["repo_path"]))
                     for b, ms in groups.items() if len(ms) >= 2}
    partial = []
    for base, members in dotted_groups.items():
        tgt = members[0]
        for m in members[1:]:
            partial.append({"source": m["id"], "target": tgt["id"],
                            "type": "partial",
                            "label": f"{m['title']} → {tgt['title']}",
                            "reason": f"partiel de {base}"})
    print(f"partials: {len(partial)}")

    # tests : fichier de test -> tous les fichiers testes (>= 2 commits communs)
    def is_test(n):
        return "/Tests/" in n["repo_path"] or "test" in n["title"].lower()
    test_ids = [n["id"] for n in nodes if is_test(n)]
    tested_ids = [n["id"] for n in nodes if not is_test(n)]
    test_links = []
    for tid in test_ids:
        t = by_id[tid]
        tbase = t["title"][:-3] if t["title"].endswith(".cs") else t["title"]
        stem = re.sub(r"tests?$", "", tbase, flags=re.I).lower()
        for cid in tested_ids:
            c = by_id[cid]
            cbase = c["title"][:-3] if c["title"].endswith(".cs") else c["title"]
            w = len(commit_sets.get(tid, set()) & commit_sets.get(cid, set()))
            if w >= 2 or (stem and cbase.lower() == stem):
                test_links.append({"source": tid, "target": cid, "type": "test",
                                   "label": f"{t['title']} teste {by_id[cid]['title']}",
                                   "reason": f"{w} commits communs" if w >= 2 else "nom apparié"})
    print(f"tests: {len(test_links)}")
    for l in test_links:
        by_id[l["target"]]["tests"].append({"id": l["source"],
                                            "name": by_id[l["source"]]["title"]})

    # milestones : top-7 volume + tetes de groupes partiels >= 3
    for n in sorted(nodes, key=lambda n: -n["loc"])[:7]:
        n["milestone"] = "★ Top volume du projet"
    for base, members in dotted_groups.items():
        if len(members) >= 3:
            if not members[0]["milestone"]:
                members[0]["milestone"] = "🧩 Tête de monolithe découpé"

    totals = {
        "files": len(nodes),
        "loc": sum(n["loc"] for n in nodes),
        "code": code_tot,
        "size": sum(n["size"] for n in nodes),
        "classes": sum(n["n_classes"] for n in nodes),
        "methods": methods_tot,
        "modules": len(branches),
        "partial_groups": sum(1 for ms in groups.values() if len(ms) >= 2),
    }
    try:
        n_git_commits = int(sh("git", "rev-list", "--count", "HEAD", "--",
                               "KilltimeTactics").strip())
    except ValueError:
        n_git_commits = len(nodes)
    data = {
        "project": old["project"],
        "scope": (f"{len(nodes)} fichiers actuels, historique git complet "
                  f"({n_git_commits} commits KilltimeTactics)"),
        "total_commits": len(nodes),
        "date_range": {"start": nodes[0]["date_display"] if nodes else head_date,
                       "end": head_date},
        "totals": totals,
        "branches": branches,
        "epochs": epochs,
        "nodes": nodes,
        "links": links,
        "cross_links": cross,
        "inherit_links": inherit_u,
        "partial_links": partial,
        "test_links": test_links,
    }
    data["epochs"][-1]["end"] = head_date
    with open(HIST_PATH, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print(f"Ecrit {HIST_PATH}: {len(nodes)} fichiers, LOC={totals['loc']}, "
          f"coupling={len(cross)}, heritage={len(inherit_u)}, "
          f"partials={len(partial)}, tests={len(test_links)}")


if __name__ == "__main__":
    main()
