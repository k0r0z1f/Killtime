#!/usr/bin/env python3
"""Pipeline generique JSON-ification Livres III+ (motif Livre II, RP-075).

Usage :
  python3 tools/livre_extract.py N --livre III --titre "..." [--titre-livre ...]
      [--entete header|prefix|none] [--sections h2|docsection]
      [--renames '{"t1":"nom",...}'] [--mecaniques meca_N.json]

- Extrait data/livre_N.json (entete, sections html/ref, donnees typees,
  tableaux, mecaniques optionnelles), squelettise livre_N.html
  (conteneurs lx*, data-livre, script js/livre.js), verifie la fidelite
  (reconstruction normalisee) et ecrit /tmp/opencode/livre_N_expected.json
  pour le test runtime (test_livre_runtime.js).
- Les grilles non spec-cards restent en HTML (avertissement).
- Idempotent : la source est fige dans /tmp/opencode/livre_N.html.orig
  au premier run.
"""
import argparse, json, os, re, sys

TMP = '/tmp/opencode'
os.makedirs(TMP, exist_ok=True)

def norm(s):
    s = re.sub(r'\s+', ' ', s).strip()
    s = re.sub(r'>\s+<', '><', s)
    s = re.sub(r'\s+</', '</', s)
    return re.sub(r'>\s+', '>', s)

def balanced(s, start, tag='div'):
    assert s.startswith('<' + tag, start), s[start:start + 60]
    o_re, c_re = re.compile(r'<' + tag + r'[\s>]'), re.compile(r'</' + tag + r'\s*>')
    depth, i = 0, start
    while True:
        m_o, m_c = o_re.search(s, i), c_re.search(s, i)
        if m_c is None:
            raise ValueError('div non ferme a %d' % start)
        if m_o is not None and m_o.start() < m_c.start():
            depth += 1
            i = m_o.end()
        else:
            depth -= 1
            i = m_c.end()
            if depth == 0:
                return s[start:i], i

def parse_table(div):
    mt = re.search(r'<table class="([^"]*)"', div)
    classe = mt.group(1) if mt else 'codex-table'
    ths = []
    for m in re.finditer(r'<th(?=[\s>])([^>]*)>(.*?)</th>', div, re.S):
        attrs, inner = m.group(1), m.group(2).strip()
        ths.append({'attrs': attrs, 'html': inner} if attrs.strip() else inner)
    tbody = re.search(r'<tbody>(.*?)</tbody>', div, re.S).group(1)
    rows = []
    for tr in re.findall(r'<tr>(.*?)</tr>', tbody, re.S):
        cells = []
        for m in re.finditer(r'<td(?=[\s>])([^>]*)>(.*?)</td>', tr, re.S):
            attrs, inner = m.group(1), m.group(2).strip()
            cells.append({'attrs': attrs, 'html': inner} if attrs.strip() else inner)
        rows.append(cells)
    return (ths, rows, classe)

def parse_cartes(div, grille):
    cards, seps, p = [], [], 0
    spans = []
    while True:
        i = div.find('<div class="spec-card">', p)
        if i < 0:
            break
        bloc, p = balanced(div, i)
        spans.append((i, bloc))
    assert spans
    open_end = div.index('>') + 1
    pre = div[open_end:spans[0][0]]
    suf = div[p:div.rfind('</div>')]
    for k in range(len(spans) - 1):
        between = div[spans[k][0] + len(spans[k][1]):spans[k + 1][0]]
        seps.append(between if between.strip() else '')
    for i, (pos, bloc) in enumerate(spans):
        mt = re.search(r'<div class="spec-card-title">(.*?)</div>', bloc, re.S)
        after_title = bloc[mt.end():] if mt else bloc
        mw = re.search(r'<div class="spec-card-world">(.*?)</div>', after_title, re.S)
        debut = (mt.end() if mt else 0) + (mw.end() if mw else 0)
        mfoot = re.search(r'<div class="spec-card-footer">', bloc)
        fin = mfoot.start() if mfoot else bloc.rfind('</div>')
        seg = bloc[debut:fin]
        card = {'titre': mt.group(1).strip() if mt else '',
                'top': '<div class="spec-card-top">' in bloc}
        if mw:
            card['monde'] = mw.group(1).strip()
        m1 = re.fullmatch(r'\s*<(p|ul|ol)([^>]*)>(.*)</\1>\s*', seg, re.S)
        if m1 and m1.group(1) == 'p' and '</p>' not in m1.group(3):
            if m1.group(2).strip():
                card['corps_attrs'] = m1.group(2)
            card['corps_html'] = m1.group(3).strip()
        elif m1 and m1.group(1) in ('ul', 'ol') and '</%s>' % m1.group(1) not in \
                m1.group(3).replace('</li>', ''):
            card['corps_tag'] = m1.group(1)
            if m1.group(2).strip():
                card['corps_attrs'] = m1.group(2)
            card['corps_html'] = m1.group(3).strip()
        elif seg.strip():
            card['corps_brut'] = seg
        mf = re.search(r'<div class="spec-card-footer">(.*?)</div>', bloc, re.S)
        card['pieds'] = [{'ton': a.strip(), 'texte': b.strip()} for a, b in
                         re.findall(r'class="tag-pill (\w+)">(.*?)</span>', mf.group(1), re.S)] if mf else []
        cards.append(card)
    out = {'type': 'cartes', 'grille': grille, 'cartes': cards}
    if pre.strip():
        out['prefix'] = pre
    if any(s for s in seps):
        out['seps'] = seps
    if suf.strip():
        out['suffix'] = suf
    return out

def parse_formule(div):
    m = re.search(r'<(span|div) class="(formula-badge|formula-label)"([^>]*)>(.*?)</\1>', div, re.S)
    assert m, 'etiquette formule introuvable'
    reste = div[m.end():]
    mc = re.search(r'<div class="formula-content">', reste)
    if mc:
        ci, _ = balanced(div, m.end() + mc.start())
        scope = div[m.end() + mc.start():]
    else:
        scope = div[m.end():div.rfind('</div>')]
    mm = re.search(r'<div class="formula-math"([^>]*)>', scope)
    math, _ = balanced(scope, mm.start())
    math = re.sub(r'^<div class="formula-math"[^>]*>|</div>\s*$', '', math).strip()
    md = re.search(r'<div class="formula-desc"([^>]*)>', scope)
    desc, _ = balanced(scope, md.start())
    desc = re.sub(r'^<div class="formula-desc"[^>]*>|</div>\s*$', '', desc).strip()
    out = {'type': 'formule', 'etiquette_tag': m.group(1),
           'etiquette_classe': m.group(2), 'etiquette_html': m.group(4).strip(),
           'math_html': math, 'desc_html': desc}
    if mm.group(1).strip():
        out['math_attrs'] = mm.group(1)
    if md.group(1).strip():
        out['desc_attrs'] = md.group(1)
    if not mc:
        out['contenu'] = False
    return out

# --- rendu miroir (meme regles que js/livre.js) ---
def cell(tag, c):
    if isinstance(c, dict):
        return '<' + tag + c['attrs'] + '>' + c['html'] + '</' + tag + '>'
    return '<' + tag + '>' + c + '</' + tag + '>'

def render_table(t):
    th = '<thead><tr>' + ''.join(cell('th', c) for c in t['colonnes']) + '</tr></thead>'
    rows = ''.join('<tr>' + ''.join(cell('td', c) for c in r) + '</tr>' for r in t['lignes'])
    return '<div class="table-container"' + t.get('extra', '') + '><table class="' + t.get('classe', 'codex-table') + '">' + th + '<tbody>' + rows + '</tbody></table></div>'

def render_cartes(c):
    out = []
    for k in c['cartes']:
        title = ('<div class="spec-card-title">' + k['titre'] + '</div>' +
                 ('<div class="spec-card-world">' + k['monde'] + '</div>' if k.get('monde') else ''))
        head = title if k.get('top') is False else '<div class="spec-card-top">' + title + '</div>'
        tag = k.get('corps_tag', 'p')
        if 'corps_brut' in k:
            corps = k['corps_brut']
        else:
            corps = ('<' + tag + k.get('corps_attrs', '') + '>' + k.get('corps_html', '') +
                     '</' + tag + '>') if ('corps_html' in k or 'corps_tag' in k) else ''
        pieds = ''.join('<span class="tag-pill ' + p['ton'] + '">' + p['texte'] + '</span>'
                        for p in k.get('pieds', []))
        foot = '<div class="spec-card-footer">' + pieds + '</div>' if pieds else ''
        out.append('<div class="spec-card">' + head + corps + foot + '</div>')
    seps = c.get('seps') or []
    body = out[0] if out else ''
    for i, card in enumerate(out[1:]):
        body += (seps[i] if i < len(seps) else '') + card
    return ('<div class="cards-grid-' + str(c.get('grille') or 3) + '"' + c.get('extra', '') + '>' +
            c.get('prefix', '') + body + c.get('suffix', '') + '</div>')

def render_formule(f):
    tag = f.get('etiquette_tag', 'span')
    label = ('<' + tag + ' class="' + f.get('etiquette_classe', 'formula-badge') + '">' +
             f.get('etiquette_html', f.get('badge', '')) + '</' + tag + '>')
    inner = ('<div class="formula-math"' + f.get('math_attrs', '') + '>' + f['math_html'] +
             '</div><div class="formula-desc"' + f.get('desc_attrs', '') + '>' + f['desc_html'] + '</div>')
    if f.get('contenu', True):
        inner = '<div class="formula-content">' + inner + '</div>'
    return '<div class="formula-box"' + f.get('extra', '') + '>' + label + inner + '</div>'

def render_ref(b, donnees):
    if b['type'] != 'ref':
        return b['html']
    r = donnees[b['cible']]
    if r['type'] == 'table':
        return render_table(r)
    if r['type'] == 'cartes':
        return render_cartes(r)
    if r['type'] == 'formule':
        return render_formule(r)
    raise AssertionError(b['cible'])

def render_section(s, donnees):
    h = ('<h2 id="' + s['id'] + '"><span class="section-symbol">' + s['symbole'] +
         '</span> ' + s['titre'] + '</h2>') if s.get('titre') else ''
    return h + '\n'.join(render_ref(b, donnees) for b in s['blocs'])

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('n')
    ap.add_argument('--livre', required=True)
    ap.add_argument('--titre', required=True)
    ap.add_argument('--entete', default='header', choices=['header', 'prefix', 'none'])
    ap.add_argument('--sections', default='h2', choices=['h2', 'docsection'])
    ap.add_argument('--renames', default='{}')
    ap.add_argument('--mecaniques', default=None)
    a = ap.parse_args()
    renames = json.loads(a.renames)

    src = 'livre_%s.html' % a.n
    orig_path = os.path.join(TMP, 'livre_%s.html.orig' % a.n)
    if os.path.exists(orig_path):
        html = open(orig_path, encoding='utf-8').read()
    else:
        html = open(src, encoding='utf-8').read()
        open(orig_path, 'w', encoding='utf-8').write(html)

    # --- article ---
    m_art = re.search(r'<article class="[^"]*">(.*?)</article>', html, re.S)
    assert m_art, 'article introuvable'
    article = m_art.group(1)

    # --- entete ---
    entete_html = ''
    if a.entete == 'header':
        m = re.search(r'<header class="doc-header">(.*?)</header>', html, re.S)
        assert m, 'doc-header introuvable'
        entete_html = m.group(1)

    # --- decoupage sections ---
    sections = []  # (id, symbole|None, titre|None, body)
    if a.sections == 'h2':
        h2s = [(m.group(1), m.start(), m.end())
               for m in re.finditer(r'<h2 id="(chap-\d+)">(.*?)</h2>', article, re.S)]
        assert h2s, 'aucun h2 chap-N'
        for i, (cid, hs, he) in enumerate(h2s):
            end = h2s[i + 1][1] if i + 1 < len(h2s) else len(article)
            hm = re.search(r'<span class="section-symbol">(.*?)</span>\s*(.*?)\s*</h2>\s*$',
                           article[hs:he], re.S)
            assert hm, 'h2 sans symbole : ' + cid
            sections.append((cid, hm.group(1).strip(), hm.group(2).strip(), article[he:end]))
        prefix = article[:h2s[0][1]]
    else:
        secs = [(m.group(1), m.start(), m.end())
                for m in re.finditer(r'<section class="doc-section" id="(chap-\d+)">', article)]
        assert secs, 'aucune doc-section'
        for i, (cid, ss, se) in enumerate(secs):
            end = secs[i + 1][1] if i + 1 < len(secs) else len(article)
            raw = article[se:end]
            close = raw.rfind('</section>')
            assert close > 0
            sections.append((cid, None, None, raw[:close]))
        prefix = article[:secs[0][1]]

    if a.entete == 'prefix':
        assert prefix.strip(), 'prefixe vide'
        sections_data = [{'id': 'entete', 'symbole': None, 'titre': None,
                          'blocs': [{'type': 'html', 'html': prefix}]}]
    else:
        if prefix.strip() and a.sections == 'h2':
            print('AVERTISSEMENT : contenu avant le 1er h2 ignore (%d car).' % len(prefix))
        sections_data = []

    # --- refs dans l'ordre du document ---
    donnees, tableaux, counters = {}, {}, {'t': 0, 'c': 0, 'f': 0}
    for cid, sym, titre, body in sections:
        blocs, pos = [], 0
        pat = re.compile(r'<div class="(?:table-container|cards-grid-(\d)|formula-box)"([^>]*)>')
        while True:
            m = pat.search(body, pos)
            if not m:
                break
            idx = m.start()
            extra = m.group(2) or ''
            grille = int(m.group(1)) if m.group(1) else None
            kind = 'table' if 'table-container' in m.group(0) else (
                'cartes' if 'cards-grid' in m.group(0) else 'formule')
            if body[pos:idx].strip():
                blocs.append({'type': 'html', 'html': body[pos:idx]})
            if kind == 'table':
                counters['t'] += 1
                name = renames.get('t%d' % counters['t'], 'table%d' % counters['t'])
                bloc, pos = balanced(body, idx)
                cols, rows, classe = parse_table(bloc)
                entry = {'type': 'table', 'colonnes': cols, 'lignes': rows}
                if classe != 'codex-table':
                    entry['classe'] = classe
                tableaux[name] = {'colonnes': cols}
            elif kind == 'cartes':
                counters['c'] += 1
                name = renames.get('c%d' % counters['c'], 'cartes%d' % counters['c'])
                bloc, pos = balanced(body, idx)
                if '<div class="spec-card">' not in bloc:
                    print('AVERTISSEMENT %s : grille non spec-cards gardee en HTML.' % cid)
                    blocs.append({'type': 'html', 'html': bloc})
                    counters['c'] -= 1
                    continue
                entry = parse_cartes(bloc, grille)
            else:
                counters['f'] += 1
                name = renames.get('f%d' % counters['f'], 'formule%d' % counters['f'])
                bloc, pos = balanced(body, idx)
                entry = parse_formule(bloc)
            if extra.strip():
                entry['extra'] = extra
            donnees[name] = entry
            blocs.append({'type': 'ref', 'cible': name})
        if body[pos:].strip():
            blocs.append({'type': 'html', 'html': body[pos:]})
        sections_data.append({'id': cid, 'symbole': sym, 'titre': titre, 'blocs': blocs})

    data = {'livre': a.livre, 'titre': a.titre, 'version': 1, 'updated': '2026-10-31',
            'source': src, 'entete_html': entete_html, 'sections': sections_data,
            'donnees': donnees, 'tableaux': tableaux}
    if a.mecaniques:
        mec = json.load(open(a.mecaniques, encoding='utf-8'))
        data['mecaniques'] = mec.get('mecaniques', mec)

    with open('data/livre_%s.json' % a.n, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
        f.write('\n')
    print('JSON OK : %d sections, donnees=%s' % (len(sections_data), sorted(donnees)))

    # --- squelettisation ---
    new = html
    if a.entete == 'header':
        old_h = '<header class="doc-header">' + entete_html + '</header>'
        assert old_h in new
        new = new.replace(old_h, '<header class="doc-header" id="lxEntete">\n                </header>', 1)
    old_a = m_art.group(0)
    tag = re.match(r'<article class="[^"]*">', old_a).group(0)
    new = new.replace(old_a, tag[:-1] + ' id="lxContenu">\n                </article>', 1)
    m_main = re.search(r'<main class="main-content"([^>]*)>', new)
    assert m_main
    if 'data-livre' not in m_main.group(0):
        new = new.replace(m_main.group(0),
                          '<main class="main-content" data-livre="%s"%s>' % (a.n, m_main.group(1)), 1)
    old_s = '    <script src="js/codex.js"></script>\n'
    assert old_s in new
    if 'js/livre.js' not in new:
        new = new.replace(old_s, old_s + '    <script src="js/livre.js"></script>\n', 1)
    open(src, 'w', encoding='utf-8').write(new)
    print('page squelettisee OK')

    # --- fidelite ---
    ok = True
    if a.entete == 'header':
        ok = norm(entete_html) == norm(re.search(
            r'<header class="doc-header">(.*?)</header>', html, re.S).group(1))
        print('entete', 'fidele' if ok else 'DIVERGENTE')
    exp_sections = []
    bodies = [b for _, _, _, b in sections]
    aligned = ([prefix] if (a.entete == 'prefix') else []) + bodies
    for s, orig_body in zip(sections_data, aligned):
        if a.entete == 'prefix' and s['id'] == 'entete':
            same = norm(prefix) == norm(s['blocs'][0]['html'])
        elif a.sections == 'h2':
            hm = re.search(r'<h2 id="%s">.*?</h2>' % s['id'], article, re.S)
            same = norm(hm.group(0) + orig_body) == norm(render_section(s, donnees))
        else:
            # docsection : le rendu omet la balise <section> elle-meme
            same = norm(orig_body) == norm(render_section(s, donnees))
        print(s['id'], 'fidele' if same else 'DIVERGENT')
        ok = ok and same
    # --- attentes pour test runtime ---
    exp = {'entete': entete_html,
           'contenu': '\n'.join(render_section(s, donnees) for s in sections_data)}
    json.dump(exp, open(os.path.join(TMP, 'livre_%s_expected.json' % a.n), 'w', encoding='utf-8'),
              ensure_ascii=False)
    sys.exit(0 if ok else 1)

if __name__ == '__main__':
    main()
