#!/usr/bin/env python3
"""Markdown -> JSON pour les textes des Livres (pilote : Livre I).

Workflow :
  - Ecrire : content/livre_N/**.md (frontmatter + corps de texte).
  - Generer : python3 tools/build_livres.py --build 1
  - Verifier : python3 tools/build_livres.py --check 1   (JSON a jour ?)
  - Extraire : python3 tools/build_livres.py --extract 1 (JSON -> .md, usage unique)

Le JSON (data/livre_N.json) reste le format lu par le navigateur.
On ne l'edite plus a la main : il est regenere depuis content/.
Champs mecaniques (couts, seuils, stats) : restent en JSON, pas de .md.

Format d'un .md :
  ---
  livre: 1
  cible: especes.cleien.description      # ou sections.chap-1.blocs.0.html
  format: raw                            # raw = verbatim, md = markdown -> HTML
  ---
  <corps de texte, octet pour octet le contenu du champ>
"""
import json
import os
import re
import sys

import yaml

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONTENU = os.path.join(RACINE, 'content')


def chemin_json(n):
    return os.path.join(RACINE, 'data', 'livre_%s.json' % n)


def dossier_contenu(n):
    return os.path.join(CONTENU, 'livre_%s' % n)


def charger_json(n):
    with open(chemin_json(n), encoding='utf-8') as f:
        return json.load(f)


def ecrire_json(n, d):
    with open(chemin_json(n), 'w', encoding='utf-8') as f:
        json.dump(d, f, ensure_ascii=False, indent=1)
        f.write('\n')


def resoudre(d, cible):
    """Renvoie (conteneur, cle) pour une cible 'especes.<id>.description'
    ou 'sections.<sec>.blocs.<i>.html'. Leve KeyError si introuvable."""
    m = re.fullmatch(r'especes\.([A-Za-z0-9_-]+)\.description', cible)
    if m:
        for e in d['especes']:
            if e['id'] == m.group(1):
                return (e, 'description')
        raise KeyError(cible)
    m = re.fullmatch(r'sections\.([A-Za-z0-9_-]+)\.blocs\.(\d+)\.html', cible)
    if m:
        for s in d['sections']:
            if s['id'] == m.group(1):
                b = s['blocs'][int(m.group(2))]
                assert b.get('type') == 'html', cible
                return (b, 'html')
        raise KeyError(cible)
    raise KeyError('cible non supportee : %s' % cible)


def lire_md(path):
    with open(path, encoding='utf-8') as f:
        txt = f.read()
    m = re.match(r'^---\n(.*?)\n---\n(.*)$', txt, re.S)
    assert m, 'frontmatter manquant : %s' % path
    meta = yaml.safe_load(m.group(1))
    corps = m.group(2)
    if corps.endswith('\n'):
        corps = corps[:-1]  # le '\n' final POSIX n'appartient pas au champ
    return meta, corps


def convertir(corps, fmt):
    if fmt == 'raw':
        return corps
    if fmt == 'md':
        import markdown
        return markdown.markdown(corps)
    raise ValueError('format inconnu : %s' % fmt)


def iter_md(n):
    base = dossier_contenu(n)
    for root, _, files in os.walk(base):
        for fn in sorted(files):
            if fn.endswith('.md'):
                yield os.path.join(root, fn)


def cmd_build(n):
    d = charger_json(n)
    nb = 0
    for path in iter_md(n):
        meta, corps = lire_md(path)
        assert str(meta.get('livre')) == str(n), path
        conteneur, cle = resoudre(d, meta['cible'])
        conteneur[cle] = convertir(corps, meta.get('format', 'raw'))
        nb += 1
    ecrire_json(n, d)
    print('build livre_%s OK : %d texte(s) -> data/livre_%s.json' % (n, nb, n))


def cmd_check(n):
    d = charger_json(n)
    sales = []
    nb = 0
    for path in iter_md(n):
        meta, corps = lire_md(path)
        conteneur, cle = resoudre(d, meta['cible'])
        if conteneur[cle] != convertir(corps, meta.get('format', 'raw')):
            sales.append('%s (cible %s)' % (os.path.relpath(path, RACINE), meta['cible']))
        nb += 1
    if sales:
        print('JSON PAS A JOUR (%d texte(s), %d divergent(s)) :' % (nb, len(sales)))
        for s in sales:
            print('  - %s' % s)
        return 1
    print('check livre_%s OK : %d texte(s) synchronise(s)' % (n, nb))
    return 0


def cmd_extract(n, cibles):
    """Usage unique : cree les .md depuis le JSON. Ne pas relancer
    apres (ecraserait les retouches markdown)."""
    d = charger_json(n)
    for cible, sous_chemin, fmt in cibles:
        conteneur, cle = resoudre(d, cible)
        corps = conteneur[cle]
        assert isinstance(corps, str) and '---\n' not in corps.split('\n')[0], cible
        path = os.path.join(dossier_contenu(n), sous_chemin)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        if os.path.exists(path):
            print('existant, ignore : %s' % path)
            continue
        meta = {'livre': int(n), 'cible': cible, 'format': fmt}
        with open(path, 'w', encoding='utf-8') as f:
            f.write('---\n' + yaml.safe_dump(meta, allow_unicode=True,
                                             default_flow_style=False) + '---\n' + corps + '\n')
        print('extrait : %s' % os.path.relpath(path, RACINE))


def especes_ids(n):
    return [e['id'] for e in charger_json(n)['especes']]


def main():
    if len(sys.argv) != 3 or sys.argv[1] not in ('--build', '--check', '--extract'):
        print('usage: build_livres.py (--build|--check|--extract) N')
        return 2
    opt, n = sys.argv[1], sys.argv[2]
    if opt == '--build':
        cmd_build(n)
    elif opt == '--check':
        return cmd_check(n)
    else:
        if n == '1':
            ids = especes_ids(n)
            d = charger_json(n)
            secs = [(s['id'], len(s['blocs'])) for s in d['sections']]
            print('especes : %s' % ids)
            print('sections : %s' % secs)
            cibles = [('especes.%s.description' % e, 'especes/%s.md' % e, 'raw') for e in ids]
            # sections : tous les blocs html (brut verbatim) ; les refs restent en JSON
            for s in d['sections']:
                for i, b in enumerate(s['blocs']):
                    if b.get('type') == 'html':
                        cibles.append(('sections.%s.blocs.%d.html' % (s['id'], i),
                                       'sections/%s/bloc-%02d.md' % (s['id'], i), 'raw'))
            cmd_extract(n, cibles)
        else:
            print('extraction pilote limitee au livre 1 pour l instant')
            return 2
    return 0


if __name__ == '__main__':
    sys.exit(main())
