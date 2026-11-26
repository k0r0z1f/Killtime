#!/usr/bin/env python3
"""Menus lateraux generes depuis les JSON (solution durable §1).

Source unique : data/livre_*.json (sections : id + nav) + le bloc L.IX de
livre_9.html lui-meme (page statique sans JSON).
Plus aucun numero / libelle de chapitre saisi dans les 16 pages.

Conventions preservees a l'identique :
  - page du livre lui-meme : hrefs "#chap-N" (scrollspy) ;
  - autres pages : 1re section -> "livre_M.html" nu, sinon "livre_M.html#chap-N" ;
  - chap-fiche : libelle sans numero ;
  - sous-entrees ad hoc (ex : ex-sous-chapitres 21.x de livre_5, numeros
    perimes) : SUPPRIMEES, la TOC de page + le scrollspy couvrent la
    navigation intra-page ;
  - libelle "Défis Hors-Combat" (canon : titre JSON du chap-9).

Usage :
  python3 tools/build_nav.py --build   # regenere les 16 pages
  python3 tools/build_nav.py --check   # verifie (exit 1 si obsolete)
"""
import json
import os
import re
import sys

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GROUPES = {'L.I': 1, 'L.II': 2, 'L.III': 3, 'L.IV': 4, 'L.V': 5, 'L.VI': 6,
           'L.VII': 7, 'L.VIII': 8, 'L.IX': 9, 'L.X': 10, 'L.XI': 11, 'L.XII': 12}
RE_GROUPE = re.compile(
    r'(group-tag">L\.([IVX]+)</span>.*?<ul class="nav-items-list">)(.*?)(</ul>)',
    re.S)
RE_APPENDICE = re.compile(
    r'(group-tag">A\.I</span>.*?<ul class="nav-items-list">)(.*?)(</ul>)',
    re.S)
RE_APPENDICE_B = re.compile(
    r'(group-tag">A\.II</span>.*?<ul class="nav-items-list">)(.*?)(</ul>)',
    re.S)
RE_APPENDICE_C = re.compile(
    r'(group-tag">A\.III</span>.*?<ul class="nav-items-list">)(.*?)(</ul>)',
    re.S)
RE_PILOTAGE = re.compile(
    r'(group-tag">◆</span>.*?<span>Pilotage</span>.*?<ul class="nav-items-list">)(.*?)(</ul>)',
    re.S)


def charger_livre(n):
    with open(os.path.join(RACINE, 'data', 'livre_%d.json' % n), encoding='utf-8') as f:
        return json.load(f)


def chapitres_livre(n):
    """[(id, numero_affiche, libelle)] — numero calcule depuis le symbole
    (lui-meme regenere au build depuis l'ordre des sections)."""
    d = charger_livre(n)
    out = []
    for s in d['sections']:
        if not s.get('titre'):
            continue
        if s['id'] == 'chap-fiche':
            out.append((s['id'], None, s['nav']))
            continue
        m = re.fullmatch(r'(?:Chapitre )?([\dA-Z]+)\.', s.get('symbole') or '')
        if not m or 'nav' not in s:
            raise ValueError('section sans nav/symbole : %s' % s['id'])
        out.append((s['id'], m.group(1), s['nav']))
    return out


def chapitres_l9():
    """Livre IX statique : numeros/badges depuis les <section>, libelles
    depuis le propre menu de livre_9.html (source unique)."""
    t = open(os.path.join(RACINE, 'livre_9.html'), encoding='utf-8').read()
    i = t.find('group-tag">L.IX<')
    ul = t[t.find('<ul class="nav-items-list">', i):t.find('</ul>', i)]
    labels = {}
    for m in re.finditer(r'href="#(chap-\d+)"[^>]*><span>(.*?)</span>', ul, re.S):
        labels[m.group(1)] = re.sub(r'\s+', ' ', m.group(2)).strip()
        labels[m.group(1)] = re.sub(r'^\d+\.\s*', '', labels[m.group(1)])
    out = []
    for m in re.finditer(r'<section[^>]*id="(chap-\d+)"', t):
        sid = m.group(1)
        out.append((sid, sid.split('-')[1], labels[sid]))
    return out


def entrees(n, page):
    """Rendu des <li> d'un livre pour une page cible."""
    items = chapitres_l9() if n == 9 else chapitres_livre(n)
    propre = (page == 'livre_%d.html' % n)
    lignes = []
    for k, (sid, num, label) in enumerate(items):
        if propre:
            href = '#%s' % sid
        elif n == 9 or k > 0:
            href = 'livre_%d.html#%s' % (n, sid)
        else:
            href = 'livre_%d.html' % n
        texte = '%s. %s' % (num, label) if num else label
        lignes.append(
            '                            <li><a href="%s" class="nav-item-link">'
            '<span>%s</span></a></li>' % (href, texte))
    return '\n'.join(lignes) + '\n                        '


def entrees_appendice(page):
    """Rendu des <li> du groupe Appendice A (A.I) depuis data/appendice_actions.json."""
    p = os.path.join(RACINE, 'data', 'appendice_actions.json')
    if not os.path.exists(p):
        return ''
    with open(p, encoding='utf-8') as f:
        d = json.load(f)
    propre = (page == 'appendice_actions.html')
    lignes = [
        '                            <li><a href="%s" class="nav-item-link">'
        '<span>Toutes les Actions (Filtres)</span></a></li>' % (
            '#compendium-actions' if propre else 'appendice_actions.html'
        )
    ]
    for c in d.get('categories', []):
        href = ('#cat-%s' % c['id']) if propre else ('appendice_actions.html#cat-%s' % c['id'])
        lignes.append(
            '                            <li><a href="%s" class="nav-item-link">'
            '<span>%s</span></a></li>' % (href, c['nav']))
    return '\n'.join(lignes) + '\n                        '


def entrees_appendice_glossaire(page):
    """Rendu des <li> du groupe Appendice B (A.II) depuis data/appendice_glossaire.json."""
    p = os.path.join(RACINE, 'data', 'appendice_glossaire.json')
    if not os.path.exists(p):
        return ''
    with open(p, encoding='utf-8') as f:
        d = json.load(f)
    propre = (page == 'appendice_glossaire.html')
    lignes = [
        '                            <li><a href="%s" class="nav-item-link">'
        '<span>Tous les Termes (A–Z)</span></a></li>' % (
            '#glossaire-mecaniques' if propre else 'appendice_glossaire.html'
        )
    ]
    for dm in d.get('domaines', []):
        href = ('#dom-%s' % dm['id']) if propre else ('appendice_glossaire.html#dom-%s' % dm['id'])
        lignes.append(
            '                            <li><a href="%s" class="nav-item-link">'
            '<span>%s</span></a></li>' % (href, dm['nav']))
    return '\n'.join(lignes) + '\n                        '


def entrees_appendice_objets(page):
    """Rendu des <li> du groupe Appendice C (A.III) depuis data/appendice_objets.json."""
    p = os.path.join(RACINE, 'data', 'appendice_objets.json')
    if not os.path.exists(p):
        return ''
    with open(p, encoding='utf-8') as f:
        d = json.load(f)
    propre = (page == 'appendice_objets.html')
    lignes = [
        '                            <li><a href="%s" class="nav-item-link">'
        '<span>Tous les Objets (Filtres)</span></a></li>' % (
            '#compendium-objets' if propre else 'appendice_objets.html'
        )
    ]
    for c in d.get('categories', []):
        href = ('#cat-%s' % c['id']) if propre else ('appendice_objets.html#cat-%s' % c['id'])
        lignes.append(
            '                            <li><a href="%s" class="nav-item-link">'
            '<span>%s</span></a></li>' % (href, c['nav']))
    return '\n'.join(lignes) + '\n                        '


PILOTAGE_LIENS = [
    ('roadmap.html', 'Roadmap Projet (Kanban)'),
    ('financement.html', 'Financement Kickstarter'),
    ('kanban_rp.html', 'Revue Système RP (Kanban)'),
    ('kanban_narratif.html', 'Projet Narratif (Kanban)'),
    ('tables.html', 'Tables de groupe (Chat + MPs)'),
    ('tactics_index.html', 'Killtime Tactics (Base)'),
    ('tactics_evolution.html', 'Évolution Tactique (Fleur Git)'),
    ('project_evolution.html', 'Évolution Projet (6 Sphères)'),
    ('doc_viewer.html?doc=whats-new', "What's New (Système RP)"),
    ('doc_viewer.html?doc=baseline', 'Mécaniques de Base (6f497f7)'),
    ('doc_viewer.html?doc=readme', 'README Projet'),
]


def entrees_pilotage(page):
    lignes = []
    for href, titre in PILOTAGE_LIENS:
        cls = 'nav-item-link active' if href == page else 'nav-item-link'
        lignes.append(
            '                            <li><a href="%s" class="%s">'
            '<span>%s</span></a></li>' % (href, cls, titre))
    return '\n'.join(lignes) + '\n                        '


def pages_cibles():
    out = []
    for fn in sorted(os.listdir(RACINE)):
        if not fn.endswith('.html'):
            continue
        t = open(os.path.join(RACINE, fn), encoding='utf-8').read()
        if 'group-tag">L.' in t:
            out.append(fn)
    return out


def regenere(page):
    p = os.path.join(RACINE, page)
    t = open(p, encoding='utf-8').read()

    def _sub(m):
        n = GROUPES['L.' + m.group(2)]
        return m.group(1) + '\n' + entrees(n, page) + m.group(4)

    out = RE_GROUPE.sub(_sub, t)
    if RE_PILOTAGE.search(out):
        out = RE_PILOTAGE.sub(
            lambda m: m.group(1) + '\n' + entrees_pilotage(page) + m.group(3),
            out
        )
    if RE_APPENDICE.search(out):
        out = RE_APPENDICE.sub(
            lambda m: m.group(1) + '\n' + entrees_appendice(page) + m.group(3),
            out
        )
    if RE_APPENDICE_B.search(out):
        out = RE_APPENDICE_B.sub(
            lambda m: m.group(1) + '\n' + entrees_appendice_glossaire(page) + m.group(3),
            out
        )
    if RE_APPENDICE_C.search(out):
        out = RE_APPENDICE_C.sub(
            lambda m: m.group(1) + '\n' + entrees_appendice_objets(page) + m.group(3),
            out
        )
    return out


def main():
    if len(sys.argv) != 2 or sys.argv[1] not in ('--build', '--check'):
        print('usage: build_nav.py (--build|--check)')
        return 2
    sales = []
    for page in pages_cibles():
        nouveau = regenere(page)
        p = os.path.join(RACINE, page)
        ancien = open(p, encoding='utf-8').read()
        if nouveau != ancien:
            if sys.argv[1] == '--build':
                open(p, 'w', encoding='utf-8').write(nouveau)
            sales.append(page)
    if sales:
        print('%s : %d page(s)' % ('OBSOLETE' if sys.argv[1] == '--check' else 'REGENERE', len(sales)))
        for s in sales:
            print('  - %s' % s)
        return 1 if sys.argv[1] == '--check' else 0
    print('nav OK : %d page(s) synchronisee(s)' % len(pages_cibles()))
    return 0


if __name__ == '__main__':
    sys.exit(main())
