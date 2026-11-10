#!/usr/bin/env python3
"""Garde-fou durable des Livres : liens internes + numerotation des chapitres.

Usage : python3 tools/check_liens.py
Sortie : liste d'erreurs (exit 1) ou OK (exit 0).

Verifie :
  1. Numerotation globale : les chapitres chap-N (tous Livres + page
     statique Livre IX) forment une suite continue sans trou ni doublon,
     dans l'ordre de lecture (livre_1..12, livre_9 statique a sa place).
     Tout id non standard (ex : chap-7b) est signale : une insertion de
     chapitre impose un renumber, pas un suffixe.
  2. Symboles : pour id chap-N, symbole attendu "N." ou "Chapitre N.".
  3. Ancres : chaque href="livre_M.html#ancre" et href="#ancre" (JSON,
     markdown content/, HTML, JS) doit pointer vers une ancre existante
     (id de section ou id="..." du contenu).
  4. References §N : chaque §N (N>=1) doit designer un chapitre existant.
  5. Cibles : chaque frontmatter content/ (sections.X.blocs.Y.html,
     especes.Z.description) doit resoudre dans le JSON.
  6. Kanban RP : titres "chap. N" et suggestedAsset "#ancre" doivent
     designer un chapitre / une ancre existants (details = logs dates,
     non verifies).

Perimetre : data/livre_*.json, data/atlas_mondes.json, content/, *.html racine,
js/*.js, data/kanban_rp.json. Exclus : Killtime/ (lore), summaries/, users/,
tools/, KilltimeTactics/, roadmaps/tactics (autres scopes).
"""
import json
import os
import re
import sys
import glob

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ERREURS = []


def err(msg):
    ERREURS.append(msg)


def lire(p):
    with open(os.path.join(RACINE, p), encoding='utf-8') as f:
        return f.read()


# ---------- 1. collecte chapitres + ancres ----------
# connu : (livre, symbole, titre) ; ancres[cle_livre] = set(ids)
chapitres = {}   # num -> [(fichier, id)]
ancres = {}      # 'livre_N' ou 'livre_9' -> set ancres
ordre_livres = []

for n in [1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12]:
    p = 'data/livre_%d.json' % n
    d = json.loads(lire(p))
    cle = 'livre_%d' % n
    ordre_livres.append(cle)
    anc = set()
    for s in d.get('sections', []):
        sid = s['id']
        anc.add(sid)
        m = re.fullmatch(r'chap-(\d+)', sid)
        if m:
            chapitres.setdefault(int(m.group(1)), []).append((p, sid))
            sym = s.get('symbole')
            if sym not in ('%s.' % m.group(1), 'Chapitre %s.' % m.group(1)):
                err('%s: symbole inattendu %r pour %s' % (p, sym, sid))
        elif sid not in ('entete', 'chap-fiche'):
            err('%s: id de section non standard %r' % (p, sid))
        for b in s.get('blocs', []):
            for a in re.finditer(r'id="([\w-]+)"', b.get('html') or ''):
                anc.add(a.group(1))
    # effet_html / mecaniques : aussi des ancres id="..." possibles
    for a in re.finditer(r'id="([\w-]+)"', json.dumps(d, ensure_ascii=False)):
        anc.add(a.group(1))
    ancres[cle] = anc

# Livre IX : page statique
html9 = lire('livre_9.html')
anc9 = set(re.findall(r'id="([\w-]+)"', html9))
ancres['livre_9'] = anc9
ordre_livres.append('livre_9')
for m in re.finditer(r'<section[^>]*id="(chap-\d+)"', html9):
    chapitres.setdefault(int(m.group(1).split('-')[1]), []).append(('livre_9.html', m.group(1)))
for m in re.finditer(r'<div class="section-badge">Chapitre (\d+)</div>', html9):
    pass  # coherence verifiee via sections ci-dessus

# suite continue ?
nums = sorted(chapitres)
if nums != list(range(1, max(nums) + 1)):
    manq = [i for i in range(1, max(nums) + 1) if i not in chapitres]
    err('numerotation non continue : max=%d, manquants=%s' % (max(nums), manq))
for num, lst in chapitres.items():
    if len(lst) > 1:
        err('chapitre %d defini %d fois : %s' % (num, len(lst), lst))

EXISTE_CHAP = set(nums)
FICHIERS_LIVRES = {'livre_%d' % n for n in [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]}

# ---------- 1b. blocs ref -> donnees (moteur generique III+ ; I-II ont
# leurs moteurs dedies : livre_1.js / livre_2.js resolvent lx* en JS) --
for n in [3, 4, 5, 6, 7, 8, 10, 11, 12]:
    d = json.loads(lire('data/livre_%d.json' % n))
    donnees = set((d.get('donnees') or {}).keys())
    for s in d.get('sections', []):
        for b in s.get('blocs', []):
            if b.get('type') == 'ref' and b.get('cible') not in donnees:
                err('livre_%d %s: bloc ref sans donnees (%s)'
                    % (n, s['id'], b.get('cible')))


# ---------- 2. balayage des textes ----------
HREF = re.compile(r'href="(livre_\d+\.html|#[^"]*?)"')
HREF_LIVRE = re.compile(r'href="(livre_(\d+)\.html)#([\w-]+)"')
HREF_PAGE = re.compile(r'href="(index|tables|fiche_pj|roadmap|financement|admin|kanban_[a-z]+|atlas_[a-z0-9]+|tactics_[a-z_]+|project_evolution)\.html(#[^"]*)?"')
SECT = re.compile(r'§(\d+)')

CONTEXTE = []  # (nom, cle_livre_ou_None, texte)


def ajouter(nom, cle, texte):
    CONTEXTE.append((nom, cle, texte))


for p in sorted(glob.glob('data/livre_*.json', root_dir=RACINE)):
    ajouter(p, p.split('/')[-1].replace('.json', ''), lire(p))
for p in sorted(glob.glob('content/**/*.md', root_dir=RACINE, recursive=True)):
    if os.path.basename(p) == 'README.md':
        continue
    ajouter(p, None, lire(p))
for p in sorted(glob.glob('*.html', root_dir=RACINE)):
    m = re.fullmatch(r'(livre_\d+)\.html', p)
    ajouter(p, m.group(1) if m else None, lire(p))
for p in sorted(glob.glob('js/*.js', root_dir=RACINE)):
    ajouter(p, None, lire(p))
ajouter('data/atlas_mondes.json', None, lire('data/atlas_mondes.json'))
ajouter('atlas_mondes.html', None, lire('atlas_mondes.html'))

for nom, cle, txt in CONTEXTE:
    for m in HREF_LIVRE.finditer(txt):
        nlivre, ancre = int(m.group(2)), m.group(3)
        if 'livre_%d' % nlivre not in FICHIERS_LIVRES:
            err('%s: livre cible inconnu %s' % (nom, m.group(1)))
            continue
        if ancre not in ancres.get('livre_%d' % nlivre, set()):
            err('%s: ancre morte %s' % (nom, m.group(0)))
    for m in HREF.finditer(txt):
        h = m.group(1)
        if h.startswith('#'):
            if cle and h[1:] not in ancres.get(cle, set()) and not h.startswith('#sec-'):
                # liens intra-livre_*.html statiques : ancres de la page elle-meme
                if cle.startswith('livre_'):
                    err('%s: ancre locale morte %s' % (nom, h))
        # livre_N.html sans ancre : existence du fichier
        else:
            if not os.path.exists(os.path.join(RACINE, h)):
                err('%s: fichier cible manquant %s' % (nom, h))
    for m in SECT.finditer(txt):
        if int(m.group(1)) not in EXISTE_CHAP:
            # ecarte les faux positifs type "§" suivi d'un nombre non-chapitre :
            # aucun connu dans le perimetre ; tout ecart est une erreur.
            err('%s: reference §%s sans chapitre' % (nom, m.group(1)))

# ---------- 2b. solution durable §2 : le numero doit coller a son ancre --
# Regle : un §N "nu" (hors <a>) est accepte seul ou en reference multiple,
# mais des qu'une phrase contient AUSSI une ancre, l'une des deux doit
# designer le meme chapitre, sinon c'est une derive (numero corrige d'un
# cote, ancre de l'autre). Les titres kanban sont exemptes (navigation +
# suggestedAsset) ; details dates aussi (logs).
A_TAG = re.compile(r'<a\b(?:[^>"\']|"[^"]*"|\'[^\']*\')*>.*?</a>', re.S)
COMMENT = re.compile(r'<!--.*?-->', re.S)
PHRASE = re.compile(r'[^.!?]+[.!?]?')

for nom, cle, txt in CONTEXTE:
    if nom.endswith('.html') or nom.endswith('.md') or nom.endswith('.json') or nom.endswith('.js'):
        # On remplace chaque <a>...</a> par un marqueur de longueur EGALE
        # (positions preservees) qui re-emet ses chapitres ancres ET ses
        # § (texte du lien + data-tip), pour les traiter uniformement.
        def _marque(m):
            tag = m.group(0)
            nums = set()
            for h in re.finditer(r'#(?:chap-(\d+)\b|sec-(\d+)-)', tag):
                nums.add(int(h.group(1) or h.group(2)))
            couvre = set()
            for s in re.finditer(r'§(\d+)', tag):
                couvre.add(int(s.group(1)))
            marque = '⟦%s⟧%s' % (','.join(map(str, sorted(nums))),
                                 ''.join(' §%d' % n for n in sorted(couvre)))
            if len(marque) > len(tag):
                return marque  # lien anormalement court : tant pis pour l'offset
            return marque + ' ' * (len(tag) - len(marque))

        marque_txt = A_TAG.sub(_marque, COMMENT.sub(
            lambda m: ' ' * len(m.group(0)), txt))
        for ph in PHRASE.finditer(marque_txt):
            seg = ph.group(0)
            bare = [int(m.group(1)) for m in SECT.finditer(seg)
                    if int(m.group(1)) in EXISTE_CHAP]
            if not bare:
                continue
            ancres_ph = set()
            for mk in re.finditer(r'⟦([\d,]*)⟧', seg):
                for x in mk.group(1).split(','):
                    if x.strip().isdigit():
                        ancres_ph.add(int(x.strip()))
            if not ancres_ph:
                continue  # § seul : ecart assume, existence verifiee en 2.
            if not any(n in ancres_ph for n in bare):
                err('%s: §%s contredit l\'ancre voisine (%s)' % (
                    nom, '/'.join(map(str, bare)),
                    '/'.join(map(str, sorted(ancres_ph)))))
        for ph in PHRASE.finditer(marque_txt):
            seg = ph.group(0)
            bare = [int(m.group(1)) for m in SECT.finditer(seg)
                    if int(m.group(1)) in EXISTE_CHAP]
            if not bare:
                continue
            ancres_ph = set()
            for mk in re.finditer(r'⟦([\d,]*)⟧', seg):
                for x in mk.group(1).split(','):
                    if x.strip().isdigit():
                        ancres_ph.add(int(x.strip()))
            if not ancres_ph:
                continue  # § seul : ecart assume, existence verifiee en 2.
            if not any(n in ancres_ph for n in bare):
                err('%s: §%s contredit l\'ancre voisine (%s)' % (
                    nom, '/'.join(map(str, bare)),
                    '/'.join(map(str, sorted(ancres_ph)))))

# ---------- 2c. coherence des navigations + Livre IX ----------
# Chaque entree de menu "N. ..." doit porter le bon numero (== ancre) et
# le bon prefixe (# = page du livre lui-meme).
ENTREE_NAV = re.compile(
    r'<li><a href="((?:livre_\d+\.html)?)#chap-(\d+)"[^>]*><span>\s*(\d+)\.', re.S)
for nom, cle, txt in CONTEXTE:
    if not nom.endswith('.html'):
        continue
    for m in ENTREE_NAV.finditer(txt):
        prefixe, n_ancre, n_label = m.group(1), int(m.group(2)), int(m.group(3))
        if n_ancre != n_label:
            err('%s: menu %d etiquete %d' % (nom, n_ancre, n_label))
        if n_ancre not in EXISTE_CHAP:
            err('%s: menu vers chapitre inexistant %d' % (nom, n_ancre))
            continue
        if prefixe == '':
            if cle is None or 'chap-%d' % n_ancre not in ancres.get(cle, set()):
                # TOC locale (livre_9) ou page sans livre : verifie l'appartenance
                if not (nom == 'livre_9.html' and 35 <= n_ancre <= 60):
                    err('%s: entree #%d hors de son livre' % (nom, n_ancre))
        else:
            livre_cle = prefixe.replace('.html', '')
            if 'chap-%d' % n_ancre not in ancres.get(livre_cle, set()):
                err('%s: entree %s hors de son livre' % (nom, m.group(0)[:60]))
# Livre IX statique : badges et commentaires contre ids de sections.
for m in re.finditer(r'<section[^>]*id="chap-(\d+)"', html9):
    seg = html9[m.start():m.start() + 800]
    b = re.search(r'<div class="section-badge">Chapitre (\d+)</div>', seg)
    if not b or int(b.group(1)) != int(m.group(1)):
        err('livre_9.html: badge incoherent pour chap-%s' % m.group(1))
for m in re.finditer(r'<!--\s*CHAPITRE\s+(\d+)\s*==', html9):
    suivant = re.search(r'id="chap-(\d+)"', html9[m.start():m.start() + 2000])
    if not suivant or int(suivant.group(1)) != int(m.group(1)):
        err('livre_9.html: commentaire CHAPITRE %s obsolete' % m.group(1))

# ---------- 3. cibles content/ ----------
for p in sorted(glob.glob('content/**/*.md', root_dir=RACINE, recursive=True)):
    if os.path.basename(p) == 'README.md':
        continue
    txt = lire(p)
    m = re.match(r'^---\n(.*?)\n---\n(.*)$', txt, re.S)
    if not m:
        err('%s: frontmatter manquant' % p)
        continue
    import yaml
    meta = yaml.safe_load(m.group(1))
    try:
        nlivre = int(meta.get('livre'))
        cible = meta.get('cible')
        d = json.loads(lire('data/livre_%d.json' % nlivre))
        mm = re.fullmatch(r'especes\.([A-Za-z0-9_-]+)\.description', cible or '')
        if mm:
            if mm.group(1) not in [e['id'] for e in d.get('especes', [])]:
                err('%s: espece cible inconnue %s' % (p, cible))
            continue
        mm = re.fullmatch(r'sections\.([A-Za-z0-9_-]+)\.blocs\.(\d+)\.html', cible or '')
        if not mm:
            err('%s: cible non supportee %r' % (p, cible))
            continue
        secs = [s for s in d['sections'] if s['id'] == mm.group(1)]
        if not secs or int(mm.group(2)) >= len(secs[0]['blocs']):
            err('%s: bloc cible inexistant %s' % (p, cible))
    except Exception as e:  # noqa: BLE001
        err('%s: cible illisible (%s)' % (p, e))

# ---------- 4. kanban RP (titres + suggestedAsset, pas les logs) ----------
try:
    kb = json.loads(lire('data/kanban_rp.json'))
    for board in kb.get('boards', []):
        for t in board.get('tasks', []):
            for m in re.finditer(r'chap\.?\s*(\d+)', t.get('title', ''), re.I):
                if int(m.group(1)) not in EXISTE_CHAP:
                    err('kanban %s: titre sans chapitre (%s)' % (t.get('id'), t.get('title')))
            sa = t.get('suggestedAsset', '')
            m = re.search(r'(livre_\d+\.html)?#([\w-]+)', sa)
            if m:
                cle2 = (m.group(1) or 'index').replace('.html', '')
                if m.group(1) and cle2 in ancres and m.group(2) not in ancres[cle2]:
                    err('kanban %s: suggestedAsset mort (%s)' % (t.get('id'), sa))
except Exception as e:  # noqa: BLE001
    err('kanban illisible (%s)' % e)

# ---------- sortie ----------
if ERREURS:
    print('CHECK_LIENS ECHEC : %d erreur(s)' % len(ERREURS))
    for e in ERREURS[:60]:
        print('  - %s' % e)
    if len(ERREURS) > 60:
        print('  ... +%d' % (len(ERREURS) - 60))
    sys.exit(1)
print('CHECK_LIENS OK : chapitres 1-%d continus, liens et § verifies (%d textes).'
      % (max(nums), len(CONTEXTE)))
