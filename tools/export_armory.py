#!/usr/bin/env python3
"""Exporte ArmoryCatalog.cs (Add/AddGrenade/AddLauncher) vers Armory.json.
Usage: python3 export_armory.py
Lit:  KilltimeTactics/Assets/Scripts/Core/Inventory/ArmoryCatalog.cs
Écrit: KilltimeTactics/Assets/Resources/Data/Armory.json
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "KilltimeTactics/Assets/Scripts/Core/Inventory/ArmoryCatalog.cs"
DST = ROOT / "KilltimeTactics/Assets/Resources/Data/Armory.json"
LIVRES_KG = 0.45
MAX_RANGE = 20


def split_top(s):
    """Découpe sur les virgules de niveau 0 (hors guillemets)."""
    parts, cur, in_str = [], [], False
    i = 0
    while i < len(s):
        c = s[i]
        if c == '"' and (i == 0 or s[i - 1] != '\\'):
            in_str = not in_str
            cur.append(c)
        elif c == ',' and not in_str:
            parts.append(''.join(cur).strip())
            cur = []
        else:
            cur.append(c)
        i += 1
    parts.append(''.join(cur).strip())
    return parts


def parse_val(tok):
    """Parse un token C# simple -> python."""
    tok = tok.strip()
    if tok.startswith('"') and tok.endswith('"'):
        return tok[1:-1]
    if tok.endswith('f') and re.fullmatch(r'-?\d+(\.\d+)?f', tok):
        return float(tok[:-1])
    if re.fullmatch(r'-?\d+', tok):
        return int(tok)
    if re.fullmatch(r'-?\d+\.\d+', tok):
        return float(tok)
    if tok in ('true', 'false'):
        return tok == 'true'
    m = re.fullmatch(r'(ItemType|SkillType|ItemRarity)\.(\w+)', tok)
    if m:
        return m.group(2)
    raise ValueError(f"token inconnu: {tok!r}")


def parse_call_args(inner):
    """Retourne (positionnels[list], nommés[dict])."""
    pos, named = [], {}
    for tok in split_top(inner):
        m = re.match(r'^(\w+)\s*:\s*(.+)$', tok)
        if m and not tok.startswith('"'):
            named[m.group(1)] = parse_val(m.group(2))
        else:
            pos.append(parse_val(tok))
    return pos, named


def equip_slot(mains, typ):
    if mains == "2H":
        return "TwoHands"
    return "MainHand" if typ == "Weapon" else "None"


def conv_range(portee):
    return max(1, portee) if portee <= MAX_RANGE else MAX_RANGE


def entry_add(pos, named):
    (name, dlph, dmg, livres, portee, mains, price, typ, skill,
     category, placeholder, desc) = pos[:12]
    rest = pos[12:]
    rarity, prefab = "Courant", ""
    for r in rest:
        if r in ("Courant", "Militaire", "Elite", "Legendaire", "Prototype"):
            rarity = r
        elif isinstance(r, str):
            prefab = r
        else:
            raise ValueError(f"reste inattendu: {r!r} ({name})")
    rng = conv_range(portee)
    if category == "Grenades":
        rng = 8
    return {
        "name": name, "dlph": dlph, "dmg": dmg,
        "weightKg": round(livres * LIVRES_KG, 2),
        "rangeTiles": rng, "priceCE": price, "type": typ, "skill": skill,
        "slot": equip_slot(mains, typ), "category": category,
        "placeholder": placeholder, "prefab": prefab, "desc": desc,
        "rarity": rarity,
        "armor": named.get("armor", 0), "shield": named.get("shield", 0),
        "heal": named.get("heal", 0), "bonusEc": named.get("bonusEc", 0),
        "stackable": named.get("stackable", False),
        "qty": 1, "apMod": named.get("apMod", 0),
        "isGrenade": False, "isLauncher": False, "era": "", "kind": "",
        "blast": 0, "dice": 0, "shrapnel": 0, "statuses": "",
        "zoneTurns": 0, "launcherCompatible": False,
        "launcherRangeBonus": 0, "accuracyBonus": 0,
        "ammoCapacity": named.get("ammoCapacity", 0),
        "ammoType": named.get("ammoType", ""),
        "reloadCost": named.get("reloadCost", 2),
        "heavyAmmo": named.get("heavyAmmo", False),
    }


def entry_grenade(pos, named):
    (name, dlph, flat, dice, livres, price, era, kind, blast,
     shrap, statuses, zone, desc) = pos[:13]
    rest = pos[13:]
    rarity = "Courant"
    for r in rest:
        if r in ("Courant", "Militaire", "Elite", "Legendaire", "Prototype"):
            rarity = r
        else:
            raise ValueError(f"reste inattendu: {r!r} ({name})")
    return {
        "name": name, "dlph": dlph, "dmg": flat,
        "weightKg": round(livres * LIVRES_KG, 2),
        "rangeTiles": 8, "priceCE": price, "type": "Weapon",
        "skill": "Ballistique", "slot": "MainHand", "category": "Grenades",
        "placeholder": "Grenade", "prefab": "", "desc": desc,
        "rarity": rarity,
        "armor": 0, "shield": 0, "heal": 0, "bonusEc": 0,
        "stackable": True, "qty": 1, "apMod": 0,
        "isGrenade": True, "isLauncher": False, "era": era, "kind": kind,
        "blast": blast, "dice": dice, "shrapnel": shrap,
        "statuses": statuses or "", "zoneTurns": zone,
        "launcherCompatible": named.get("launcherCompatible", True),
        "launcherRangeBonus": 0,
        "accuracyBonus": named.get("accuracyBonus", 0),
        "ammoCapacity": 0, "ammoType": "", "reloadCost": 2, "heavyAmmo": False,
    }


def entry_launcher(pos, named):
    (name, dlph, livres, price, range_bonus, acc, desc) = pos[:7]
    rest = pos[7:]
    rarity = "Militaire"
    for r in rest:
        if r in ("Courant", "Militaire", "Elite", "Legendaire", "Prototype"):
            rarity = r
        else:
            raise ValueError(f"reste inattendu: {r!r} ({name})")
    return {
        "name": name, "dlph": dlph, "dmg": 0,
        "weightKg": round(livres * LIVRES_KG, 2),
        "rangeTiles": min(MAX_RANGE, 8 + max(0, range_bonus)),
        "priceCE": price, "type": "Weapon", "skill": "Ballistique",
        "slot": "TwoHands", "category": "Lance-Grenades",
        "placeholder": "GrenadeLauncher", "prefab": "", "desc": desc,
        "rarity": rarity,
        "armor": 0, "shield": 0, "heal": 0, "bonusEc": 0,
        "stackable": False, "qty": 1,
        "apMod": named.get("apMod", 0),
        "isGrenade": False, "isLauncher": True, "era": "", "kind": "",
        "blast": 0, "dice": 0, "shrapnel": 0, "statuses": "",
        "zoneTurns": 0, "launcherCompatible": False,
        "launcherRangeBonus": range_bonus, "accuracyBonus": acc,
        "ammoCapacity": 0, "ammoType": "", "reloadCost": 2, "heavyAmmo": False,
    }


def patch_legacy(e):
    """Réplique PatchLegacyGrenades() pour les 5 grenades génériques historiques."""
    if e["category"] != "Grenades" or e["isGrenade"]:
        return
    e["isGrenade"] = True
    e["stackable"] = True
    e["era"] = "Moderne"
    e["launcherCompatible"] = True
    e["blast"] = 2
    e["shrapnel"] = 2
    e["statuses"] = "Destabilise,Saignement"
    e["zoneTurns"] = 0
    e["accuracyBonus"] = 0
    e["placeholder"] = "Grenade"
    nm = e["name"]
    if "1d10" in nm:
        e["kind"] = "Fragmentation"; e["dice"] = 1
        e["blast"] = 1; e["shrapnel"] = 1
    elif "2d10" in nm:
        e["kind"] = "Fragmentation"; e["dice"] = 2
    elif "3d10" in nm:
        e["kind"] = "Fragmentation"; e["dice"] = 3; e["shrapnel"] = 3
    elif "4d10" in nm:
        e["kind"] = "Explosive"; e["dice"] = 4
        e["blast"] = 2; e["shrapnel"] = 2
        e["statuses"] = "Destabilise,ATerre"
    elif "5d10" in nm:
        e["kind"] = "Thermobarique"; e["dice"] = 5
        e["blast"] = 3; e["shrapnel"] = 2
        e["statuses"] = "EnFeu,Asphyxie,Destabilise"
    else:
        e["kind"] = "Fragmentation"; e["dice"] = 2


def main():
    text = SRC.read_text(encoding="utf-8")
    items, in_catalog = [], False
    for lineno, line in enumerate(text.splitlines(), 1):
        s = line.strip()
        if "private static void BuildCatalog()" in line:
            in_catalog = True
            continue
        if not in_catalog:
            continue
        if s.startswith("PatchLegacyGrenades();"):
            break
        m = re.match(r'^(Add|AddGrenade|AddLauncher)\((.*)\);$', s)
        if not m:
            continue
        kind, inner = m.group(1), m.group(2)
        try:
            pos, named = parse_call_args(inner)
            if kind == "Add":
                e = entry_add(pos, named)
            elif kind == "AddGrenade":
                e = entry_grenade(pos, named)
            else:
                e = entry_launcher(pos, named)
            items.append(e)
        except Exception as ex:
            print(f"L{lineno}: {ex}\n  {s[:160]}", file=sys.stderr)
            sys.exit(1)

    for e in items:
        patch_legacy(e)

    names = [e["name"] for e in items]
    dupes = sorted({n for n in names if names.count(n) > 1})
    if dupes:
        print(f"DOUBLONS: {dupes}", file=sys.stderr)
        sys.exit(1)

    data = {
        "version": 1,
        "source": ("Arsenal Killtime Livre VIII §30-32 — export automatique de "
                   "ArmoryCatalog.cs. Fichier éditable (UTF-8) ; un override "
                   "persistentDataPath/Armory.json ou la fenêtre Armurerie (M) "
                   "prend le pas. Re-générable via tools/export_armory.py."),
        "items": items,
    }
    DST.parent.mkdir(parents=True, exist_ok=True)
    DST.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n",
                   encoding="utf-8")

    cats = {}
    for e in items:
        cats[e["category"]] = cats.get(e["category"], 0) + 1
    print(f"OK: {len(items)} items -> {DST}")
    for c, n in sorted(cats.items()):
        print(f"  {c}: {n}")


if __name__ == "__main__":
    main()
