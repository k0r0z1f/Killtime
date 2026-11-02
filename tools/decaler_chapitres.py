#!/usr/bin/env python3
"""Decalage mecanique de la numerotation des chapitres (outil durable).

Usage : python3 tools/decaler_chapitres.py --depuis 8 [--pas 1]
Insere un "trou" a partir du chapitre N : tout chap-M / sec-M- / §M
(M >= N) passe a M+pas. Apres execution :
  - creer la nouvelle section (id chap-N) dans le JSON + ses .md ;
  - python3 tools/build_livres.py --build <livres> ;
  - python3 tools/build_nav.py --build ;
  - python3 tools/check_liens.py (doit etre vert).

Corpus traite : data/livre_*.json, data/atlas_mondes.json,
content/**/*.md, *.html racine, js/*.js, data/kanban_rp.json,
data/roadmaps.json, KilltimeTactics/Assets/Scripts/**/*.cs.
Exclus (volontaire) : Killtime/ (lore, passe kt-lore-safe dediee),
summaries/ (historique), users/ et tools/ (refs < N).

Particularites :
  - atlas : pre-passe qui aligne le § du titre sur le chap de l'url
    (corrige les paires obsoletes), puis decalage commun ;
  - kanban RP : titres + suggestedAsset par mapping semantique
    (libelle -> section), details non-done en mecanique ;
  - fourchettes §A-B : les deux bornes sont decalees ;
  - Unity : decalage uniforme prod + tests (texte uniquement).
"""
import json
import os
import re
import sys

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAPPORT = []

LIVRES_JSON = [2, 3, 4, 5, 6, 7, 8, 10, 11, 12]


def lire(p):
    with open(os.path.join(RACINE, p), encoding='utf-8') as f:
        return f.read()


def ecrire(p, txt):
    with open(os.path.join(RACINE, p), 'w', encoding='utf-8') as f:
        f.write(txt)


def fichiers_corpus():
    out = (['data/livre_%d.json' % n for n in LIVRES_JSON]
           + ['data/atlas_mondes.json', 'atlas_mondes.html'])
    out += [p for p in sorted(os.listdir(RACINE)) if p.endswith('.html')]
    out += ['js/' + p for p in sorted(os.listdir(os.path.join(RACINE, 'js')))
            if p.endswith('.js')]
    import glob
    out += sorted(glob.glob('content/**/*.md', root_dir=RACINE, recursive=True))
    return sorted(set(out))


RNG = re.compile(r'§(\d+)((?:\.\d+[a-z]*)?)\s*-\s*§?(\d+)((?:\.\d+[a-z]*)?)')


def decale_texte(txt, depuis, pas):
    # Fourchettes §A-B : tokenisees (digits seules ne matchent jamais),
    # bornes decalees a la fin.
    plages = []

    def _plage(m):
        plages.append((int(m.group(1)), m.group(2) or '',
                       int(m.group(3)), m.group(4) or ''))
        return '\x00%d\x00' % (len(plages) - 1)

    txt = RNG.sub(_plage, txt)
    # Deux passes via marqueurs TMP : insensible au sens (+1 comme -1),
    # rejouable sans risque de re-decalage.
    for m in range(60, depuis - 1, -1):
        # TOC / libelles "href="#chap-N">N." (ensemble, une passe)
        txt = re.sub(r'(#chap-%d">)%d\.' % (m, m), r'\g<1>TMP-%d.' % m, txt)
        txt = re.sub(r'\bchap-%d\b' % m, 'chap-TMP-%d' % m, txt)
        txt = re.sub(r'\bsec-%d-' % m, 'sec-TMP-%d-' % m, txt)
        txt = re.sub(r'§%d(?!\d)' % m, '§TMP-%d' % m, txt)
        txt = re.sub(r'\bChapitre %d\b' % m, 'Chapitre TMP-%d' % m, txt)
        txt = re.sub(r'\bCHAPITRE %d\b' % m, 'CHAPITRE TMP-%d' % m, txt)
        txt = re.sub(r'(\b[Cc]hap\.?\s+)%d\b' % m, r'\g<1>TMP-%d' % m, txt)
        txt = re.sub(r'(<h[34][^>]*>)\s*%d\.' % m, r'\g<1>TMP-%d.' % m, txt)
    for m in range(60, depuis - 1, -1):
        txt = txt.replace('chap-TMP-%d' % m, 'chap-%d' % (m + pas))
        txt = txt.replace('sec-TMP-%d-' % m, 'sec-%d-' % (m + pas))
        txt = txt.replace('§TMP-%d' % m, '§%d' % (m + pas))
        txt = txt.replace('Chapitre TMP-%d' % m, 'Chapitre %d' % (m + pas))
        txt = txt.replace('CHAPITRE TMP-%d' % m, 'CHAPITRE %d' % (m + pas))
        txt = txt.replace('TMP-%d.' % m, '%d.' % (m + pas))
        txt = re.sub(r'TMP-%d\b' % m, str(m + pas), txt)
    for i, (a, sa, b, sb) in enumerate(plages):
        a2 = a + pas if a >= depuis else a
        b2 = b + pas if b >= depuis else b
        txt = txt.replace('\x00%d\x00' % i, '§%d%s-%d%s' % (a2, sa, b2, sb))
    return txt


def main():
    depuis, pas = 8, 1
    args = sys.argv[1:]
    if '--depuis' in args:
        depuis = int(args[args.index('--depuis') + 1])
    if '--pas' in args:
        pas = int(args[args.index('--pas') + 1])
    phases = set(range(7))
    if '--phases' in args:
        phases = set(int(x) for x in args[args.index('--phases') + 1].split(','))
    do = lambda n, fn: fn() if n in phases else None

    # 0. atlas : le § du titre suit le chap de l'url (paires obsoletes).
    def _ph0():
        for p in ('data/atlas_mondes.json', 'atlas_mondes.html'):
            txt = lire(p)

            def _paire(m):
                titre, url = m.group(1), m.group(2)
                mu = re.search(r'#chap-(\d+)\b', url)
                mt = re.search(r'§(\d+)(?!\d)', titre)
                if mu and mt and mu.group(1) != mt.group(1):
                    RAPPORT.append('%s : titre §%s realigne sur url chap-%s'
                                   % (p, mt.group(1), mu.group(1)))
                    titre = re.sub(r'§%s(?!\d)' % mt.group(1), '§%s' % mu.group(1), titre)
                return '"titre": "%s", "url": "%s"' % (titre, url)

            txt2 = re.sub(r'"titre": "((?:[^"\\]|\\.)*)", "url": "((?:[^"\\]|\\.)*)"',
                          _paire, txt)
            if txt2 != txt:
                ecrire(p, txt2)


    do(0, _ph0)
    # 1. corpus texte (JSON livres + atlas + md + html + js).
    def _ph1():
        for p in fichiers_corpus():
            if p in ('data/kanban_rp.json', 'data/roadmaps.json'):
                continue
            txt = lire(p)
            txt2 = decale_texte(txt, depuis, pas)
            if txt2 != txt:
                ecrire(p, txt2)
        RAPPORT.append('corpus texte decale (+%d depuis %d)' % (pas, depuis))


    do(1, _ph1)
    # 2. renommage des dossiers content/.../sections/chap-N (via TMP).
    def _ph2():
        import glob
        for base in sorted(glob.glob('content/livre_*', root_dir=RACINE)):
            secs = os.path.join(RACINE, base, 'sections')
            if not os.path.isdir(secs):
                continue
            for name in os.listdir(secs):
                m = re.fullmatch(r'chap-(\d+)', name)
                if m and int(m.group(1)) >= depuis:
                    os.rename(os.path.join(secs, name),
                              os.path.join(secs, 'chap-TMP-%s' % m.group(1)))
            for name in os.listdir(secs):
                m = re.fullmatch(r'chap-TMP-(\d+)', name)
                if m:
                    dst = 'chap-%d' % (int(m.group(1)) + pas)
                    RAPPORT.append('dossier %s -> %s' % (name, dst))
                    os.rename(os.path.join(secs, name), os.path.join(secs, dst))


    do(2, _ph2)
    # 3. symboles regeneres (import du moteur de build).
    def _ph3():
        sys.path.insert(0, os.path.join(RACINE, 'tools'))
        from build_livres import charger_json, ecrire_json, regenerer_symboles
        for n in LIVRES_JSON:
            d = charger_json(str(n))
            regenerer_symboles(d)
            ecrire_json(str(n), d)
        RAPPORT.append('symboles regeneres')


    do(3, _ph3)
    # 4. kanban RP : semantique (libelle -> section post-decalage).
    def _ph4():
        kb = json.loads(lire('data/kanban_rp.json'))
        navmap = {}
        for n in [1] + LIVRES_JSON:
            d = json.loads(lire('data/livre_%d.json' % n))
            for s in d['sections']:
                if s.get('nav'):
                    navmap[s['nav']] = (n, s['id'])
        t9 = lire('livre_9.html')
        i9 = t9.find('group-tag">L.IX<')
        ul9 = t9[t9.find('<ul class="nav-items-list">', i9):t9.find('</ul>', i9)]
        for m in re.finditer(r'href="#(chap-\d+)"[^>]*><span>(.*?)</span>', ul9, re.S):
            navmap[re.sub(r'^\d+\.\s*', '', re.sub(r'\s+', ' ', m.group(2)).strip())] = (9, m.group(1))

        OVERRIDES = {  # tache -> (nouv. section (livre, id) | None, asset force)
            'RP-011': ((2, 'chap-10'), None),
            'RP-025': ((6, 'chap-25'), 'livre_6.html#chap-25'),
            'RP-053': ((7, 'chap-32'), None),
            'RP-069': (None, 'livre_6.html#chap-25'),
            'RP-070': (None, 'livre_5.html#sec-22-4'),
            'RP-071': (None, 'livre_6.html#chap-26'),
            'RP-125': (None, 'livre_6.html#chap-26'),
            'RP-126': (None, 'livre_8.html#chap-35'),
            'RP-127': (None, 'livre_2.html#chap-9'),
            'RP-031': (None, 'livre_9.html#chap-36'),
            'RP-032': (None, 'livre_9.html#chap-37'),
            'RP-033': (None, 'livre_9.html#chap-38'),
            'RP-034': (None, 'livre_9.html#chap-39'),
            'RP-035': (None, 'livre_9.html#chap-40'),
            'RP-055': (None, 'livre_9.html#chap-36'),
            'RP-056': (None, 'livre_9.html#chap-36'),
            'RP-057': (None, 'livre_9.html#chap-36'),
            'RP-058': (None, 'livre_9.html#chap-36'),
            'RP-059': (None, 'livre_9.html#chap-36'),
            'RP-061': (None, 'livre_9.html#chap-36'),
            'RP-062': (None, 'livre_9.html#chap-36'),
        }
        for board in kb.get('boards', []):
            for t in board.get('tasks', []):
                tid = t.get('id')
                # titre : numero semantique (libelle), pas mecanique.
                mt = re.match(r'^Relire\s+(?:chap\.?\s*\d+\.?\s*)?(.*)$', t.get('title', ''), re.I)
                mapped = False
                if mt and re.search(r'chap\.?\s*\d+', t.get('title', ''), re.I):
                    reste = mt.group(1).strip()
                    cible = None
                    if reste in navmap:
                        cible = navmap[reste]
                    else:
                        prefix = reste.split(':')[0].strip()
                        for nav, sec in navmap.items():
                            if nav.startswith(prefix + ' ') or nav == prefix:
                                cible = sec
                                break
                    if tid in OVERRIDES and OVERRIDES[tid][0]:
                        cible = OVERRIDES[tid][0]
                    if cible:
                        nlivre, sid = cible
                        num = re.fullmatch(r'chap-(\d+)', sid).group(1)
                        t['title'] = re.sub(r'chap\.?\s*\d+', 'chap. %s' % num,
                                            t['title'], count=1, flags=re.I)
                        t['suggestedAsset'] = 'livre_%d.html#%s' % (nlivre, sid)
                        RAPPORT.append('kanban %s -> chap. %s' % (tid, num))
                        mapped = True
                    else:
                        RAPPORT.append('kanban %s : SANS MAPPING (%s)' % (tid, reste[:60]))
                        mapped = False
                # "Créateur chap. 34 :" (outils L9, done) -> chap. 36.
                if re.search(r'Créateur chap\.', t.get('title', '')):
                    t['title'] = re.sub(r'chap\.\s*\d+', 'chap. 36', t['title'])
                # Normalisation : "Challenges" -> "Défis" (canon : titre JSON).
                t['title'] = t['title'].replace('Challenges Hors-Combat', 'Défis Hors-Combat')
                t['title'] = re.sub(r'§(\d+)(?!\d)',
                                    lambda m: '§%d' % (int(m.group(1)) + pas)
                                    if int(m.group(1)) >= depuis else m.group(0),
                                    t['title'])
                if tid in OVERRIDES and OVERRIDES[tid][1]:
                    t['suggestedAsset'] = OVERRIDES[tid][1]
                elif not mapped and re.search(r'#(chap-\d+|sec-\d+-)', t.get('suggestedAsset', '')):
                    def _a(m):
                        pre, num = m.group(1), int(m.group(3))
                        return pre + str(num + pas) if num >= depuis else m.group(0)
                    t['suggestedAsset'] = re.sub(r'(#(chap|sec)-)(\d+)', _a,
                                                t['suggestedAsset'])
                if t.get('status') != 'done':
                    t['details'] = decale_texte(t.get('details', ''), depuis, pas)
        ecrire('data/kanban_rp.json', json.dumps(kb, ensure_ascii=False, indent=1) + '\n')
        RAPPORT.append('kanban RP remappe')


    do(4, _ph4)
    # 5. roadmaps Unity : mecanique (titres + details non-done).
    def _ph5():
        rm = json.loads(lire('data/roadmaps.json'))
        for board in rm.get('boards', []):
            for t in board.get('tasks', []):
                t['title'] = decale_texte(t.get('title', ''), depuis, pas)
                if 'suggestedAsset' in t:
                    t['suggestedAsset'] = decale_texte(t.get('suggestedAsset', ''), depuis, pas)
                if t.get('status') != 'done':
                    t['details'] = decale_texte(t.get('details', ''), depuis, pas)
        ecrire('data/roadmaps.json', json.dumps(rm, ensure_ascii=False, indent=1) + '\n')
        RAPPORT.append('roadmaps decale (mecanique)')


    do(5, _ph5)
    # 6. Unity : uniforme prod + tests (texte uniquement).
    def _ph6():
        import glob as _g
        ncs = 0
        for p in _g.glob('KilltimeTactics/Assets/Scripts/**/*.cs', root_dir=RACINE, recursive=True):
            txt = lire(p)
            txt2 = decale_texte(txt, depuis, pas)
            if txt2 != txt:
                ecrire(p, txt2)
                ncs += 1
        RAPPORT.append('Unity : %d .cs retouches (commentaires + textes)' % ncs)


    do(6, _ph6)
    print('\n'.join(RAPPORT))


if __name__ == '__main__':
    main()
