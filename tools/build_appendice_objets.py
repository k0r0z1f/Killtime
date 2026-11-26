#!/usr/bin/env python3
"""Generateur deterministe de data/appendice_objets.json depuis data/livre_8.json
et data/livre_6.json.

Usage :
  python3 tools/build_appendice_objets.py --build   # regenere data/appendice_objets.json
  python3 tools/build_appendice_objets.py --check   # verifie (exit 1 si obsolete)
"""
import json
import os
import re
import sys
import unicodedata

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH_L8 = os.path.join(RACINE, 'data', 'livre_8.json')
PATH_L6 = os.path.join(RACINE, 'data', 'livre_6.json')
PATH_OUT = os.path.join(RACINE, 'data', 'appendice_objets.json')

EPOCHS_ORDER = ['E0', 'E1', 'E2', 'E3', 'E4', 'E5']
EPOCH_NAMES = {
    'E0': 'Primitive',
    'E1': 'Médiévale',
    'E2': 'Moderne',
    'E3': 'Avancée',
    'E4': 'Très Avancée',
    'E5': 'Démiurgique',
}

CATEGORIES = [
    {
        'id': 'melee-lames',
        'code': 'C.1',
        'nom': 'Mêlée : Épées, Lames & Fines',
        'nav': 'C.1 Mêlée : Lames & Fines',
        'ton': 'ruby',
        'desc': 'Lames de guerre à 1H/2H, dagues dissimulables, rapières de duel et armes d\'hast à allonge 2P [E0–E5].',
        'source_href': 'livre_8.html#sec-30-3',
    },
    {
        'id': 'melee-choc',
        'code': 'C.2',
        'nom': 'Mêlée : Choc, Haches & Capture',
        'nav': 'C.2 Mêlée : Choc & Haches',
        'ton': 'amber',
        'desc': 'Gourdins, bâtons de combat, masses perforantes, haches, marteaux lourds et armes non létales [E0–E4].',
        'source_href': 'livre_8.html#sec-30-3',
    },
    {
        'id': 'trait-jet',
        'code': 'C.3',
        'nom': 'Trait & Armes de Jet Dédiées',
        'nav': 'C.3 Trait & Armes de Jet',
        'ton': 'emerald',
        'desc': 'Frondes, javelots, couteaux de lancer, arcs de chasse ou de guerre et arbalètes silencieuses [E0–E2].',
        'source_href': 'livre_8.html#sec-30-4',
    },
    {
        'id': 'balistique',
        'code': 'C.4',
        'nom': 'Balistique à Poudre & Modérateur',
        'nav': 'C.4 Balistique à Poudre',
        'ton': 'amber',
        'desc': 'Pistolets, pistolets-mitrailleurs, fusils à pompe, carabines, fusils de précision, anti-matériel et modérateur de son [E2–E4].',
        'source_href': 'livre_8.html#sec-30-5',
    },
    {
        'id': 'laser-energie',
        'code': 'C.5',
        'nom': 'Armes Laser & Confinement Plasma',
        'nav': 'C.5 Armes Laser & Plasma',
        'ton': 'cyan',
        'desc': 'Épées laser à lame plasma perforante, fusils énergétiques de précision et Fusil « Deglazer » [E3–E5].',
        'source_href': 'livre_8.html#sec-31-1',
    },
    {
        'id': 'armes-lourdes',
        'code': 'C.6',
        'nom': 'Armes Lourdes, Grenades & Explosifs',
        'nav': 'C.6 Lourdes & Explosifs',
        'ton': 'ruby',
        'desc': 'Mitrailleuses, lance-grenades, lance-roquettes, canons plasma/rail, grenades explosives et tactiques [E1–E5].',
        'source_href': 'livre_8.html#sec-31-2',
    },
    {
        'id': 'munitions-arcanotech',
        'code': 'C.7',
        'nom': 'Munitions, Nytharite & Implants',
        'nav': 'C.7 Munitions & Arcanotech',
        'ton': 'violet',
        'desc': 'Carquois, cartouches, charges laser, cellules de nytharite, focaliseurs arcanotech et implants cybernétiques [E0–E5].',
        'source_href': 'livre_8.html#sec-31-4',
    },
    {
        'id': 'armures-boucliers',
        'code': 'C.8',
        'nom': 'Armures, Boucliers & Champs de Force',
        'nav': 'C.8 Armures, Boucliers & Champs',
        'ton': 'cyan',
        'desc': 'Armures légères et lourdes (encombrement 0–2 PA), boucliers actifs 1H, champs de force personnels et combinaisons spatiales [E0–E5].',
        'source_href': 'livre_8.html#sec-32-1',
    },
    {
        'id': 'pharmacie-medical',
        'code': 'C.9',
        'nom': 'Pharmacopée, Soins & Rations',
        'nav': 'C.9 Pharmacopée & Médical',
        'ton': 'emerald',
        'desc': 'Stimulants (Antidouleurs, Vif), seringues de soins, rations de campagne, trousses médicales, antidotes et remèdes ciblés [E0–E5].',
        'source_href': 'livre_8.html#sec-32-3',
    },
    {
        'id': 'terrain-survie',
        'code': 'C.10',
        'nom': 'Outillage, Optique & Matériel de Terrain',
        'nav': 'C.10 Outillage & Terrain',
        'ton': 'amber',
        'desc': 'Masque filtrant, crochetage, outils d\'entretien, valise arcanotech, cordes, optiques, radios, menottes et survie [E0–E5].',
        'source_href': 'livre_8.html#sec-32-4',
    },
]

PROP_DEFS = [
    ('allonge', 'Allonge', '2P', 'Frappe jusqu\'à 2 cases en mêlée (même par-dessus un allié) et menace 2 cases en opportunité.',
     'Allonge 2P : frappe et attaque d\'opportunité à 2 cases en mêlée (Livre VIII §30.2).'),
    ('arme_de_jet', 'Arme de jet', 'XP', 'Lancée à la main via Ballistique / Tir. Bonus portée : +1 case par FOR > 3 (max +4P). −1 ec Accroupi, −2 ec À Terre.',
     'Arme de jet : lancée via Ballistique / Tir, +1P par FOR > 3 (max +4P), −1 ec Accroupi / −2 ec À Terre.'),
    ('perforante', 'Perforante', 'Perf. X', 'Ignore jusqu\'à X points d\'armure physique (jamais les champs de force ni boucliers énergétiques).',
     'Perforante X : ignore X points d\'armure physique (jamais les champs énergétiques).'),
    ('zone', 'Zone', 'Zone R', 'Agit sur toute cible dans un rayon de R cases autour du point d\'impact (réduit ou bloqué selon couvert Livre V §21.3).',
     'Zone R : affecte toutes les cibles dans un rayon de R cases (couvert Livre V §21.3).'),
    ('silencieuse', 'Silencieuse', 'Discrète', 'N\'émet aucun bruit de détonation ; ne révèle pas automatiquement la position au-delà de quelques mètres.',
     'Silencieuse : aucune détonation, ne trahit pas automatiquement la position.'),
    ('dissimulable', 'Dissimulable', '+2 cache', 'Confère +2 aux épreuves de Discrétion ou Subterfuge pour cacher l\'arme sur soi.',
     'Dissimulable : +2 en Discrétion / Subterfuge pour dissimuler l\'objet sur soi.'),
    ('polyvalente', 'Polyvalente', '1H/2H', 'Utilisable à une main (bouclier possible) ou à deux mains pour infliger +1 Dégât.',
     'Polyvalente 1H/2H : +1 Dégât lorsqu\'elle est maniée à deux mains.'),
    ('non_letale', 'Non létale', 'Neutr.', 'L\'attaquant peut arrêter sa victime à Inconscient (0 dégât létal) quand elle atteint le Seuil Létal.',
     'Non létale : permet de neutraliser à Inconscient au Seuil Létal sans tuer.'),
    ('rechargement_lent', 'Rechargement lent', 'Rech. X PA', 'Remplace le rechargement standard à 1 PA par X PA (2 ou 3 PA).',
     'Rechargement lent X PA : remplace la micro-action de rechargement à 1 PA.'),
    ('cadence', 'Cadence', '+1 PA / 3 mun.', 'Dépense +1 PA et 3 munitions sur un tir pour gagner +1 Dégât (ou tir de suppression selon le MJ).',
     'Cadence : +1 PA et 3 munitions sur un tir pour +1 Dégât (ou suppression MJ).'),
    ('canon_entrave', 'Canon Entravé', 'Contact 1c', 'Subit −2 ec au tir si un ennemi conscient colle le tireur à 1 case (ou 3 PA pour Tir Visé de Rétention, Livre V §22.2).',
     'Canon Entravé : −2 ec au tir lorsqu\'un ennemi conscient est adjacent à 1 case (Livre V §22.2).'),
    ('brulure_etat', 'Brûlure / État', 'Effet', 'Pose un état officiel (Livre VI §24.1 ou §24.2) dès qu\'au moins 1 dégât passe ou sur seuil critique indiqué.',
     'Brûlure / État : applique un état officiel (En Feu, Étourdi, Immobilisé, etc.).'),
    ('anti_materiel', 'Anti-Matériel', 'Structure', 'Conçue pour percer les portes blindées, couverts renforcés et blindages de véhicules (Livre V §22.3).',
     'Anti-Matériel : perce structures, portes blindées et blindages de véhicules (Livre V §22.3).'),
]


def strip_html(s):
    if s is None:
        return ''
    txt = re.sub(r'<[^>]+>', '', str(s))
    repls = {
        '&nbsp;': ' ',
        '&times;': '×',
        '&amp;': '&',
        '&ndash;': '–',
        '&mdash;': '—',
        '&minus;': '−',
        '&le;': '≤',
        '&ge;': '≥',
        '&gt;': '>',
        '&lt;': '<',
        '&bull;': '•',
    }
    for k, v in repls.items():
        txt = txt.replace(k, v)
    return re.sub(r'\s+', ' ', txt).strip()


def slugify(text):
    s = unicodedata.normalize('NFKD', text)
    s = ''.join(ch for ch in s if not unicodedata.combining(ch))
    s = s.lower()
    s = re.sub(r'[^a-z0-9]+', '-', s).strip('-')
    return s


def fmt_ce(n):
    return f"{int(n):,}".replace(',', ' ') + ' CE'


def parse_epochs(ep_str):
    s = ep_str.replace('–', '-').replace('—', '-').strip()
    m = re.fullmatch(r'E(\d)-E(\d)', s)
    if m:
        a, b = int(m.group(1)), int(m.group(2))
        return [f'E{i}' for i in range(a, b + 1)], a, b, f'E{a}–E{b}'
    m = re.fullmatch(r'E(\d)', s)
    if m:
        a = int(m.group(1))
        return [f'E{a}'], a, a, f'E{a}'
    raise ValueError(f'Epoque invalide: {ep_str!r}')


def parse_properties(raw_props, desc='', extra_zone=0, extra_jet=None):
    """Mappe les chaînes de propriétés Livre VIII vers les 13 IDs canoniques."""
    found = {}
    perforante = 0
    zone_cases = int(extra_zone or 0)
    rechargement_pa = None
    portee_jet_cases = extra_jet

    tokens = list(raw_props or [])
    if desc:
        tokens.append(desc)

    joined = ' | '.join(str(t) for t in tokens)

    if re.search(r'\bAllonge\b', joined, re.I):
        found['allonge'] = 'Allonge 2P'
    m_jet = re.search(r'Arme de jet(?:\s*(\d+)P)?', joined, re.I)
    if m_jet:
        p = int(m_jet.group(1)) if m_jet.group(1) else (portee_jet_cases or 8)
        portee_jet_cases = p
        found['arme_de_jet'] = f'Arme de jet {p}P'
    m_perf = re.search(r'Perforante\s+(\d+)', joined, re.I)
    if m_perf:
        perforante = int(m_perf.group(1))
        found['perforante'] = f'Perforante {perforante}'
    m_zone = re.search(r'Zone\s+(\d+)', joined, re.I)
    if m_zone or zone_cases > 0:
        z = int(m_zone.group(1)) if m_zone else zone_cases
        zone_cases = z
        found['zone'] = f'Zone {z}'
    if re.search(r'\bSilencieuse\b', joined, re.I):
        found['silencieuse'] = 'Silencieuse'
    if re.search(r'\bDissimulable\b', joined, re.I):
        found['dissimulable'] = 'Dissimulable'
    if re.search(r'\bPolyvalente\b', joined, re.I):
        found['polyvalente'] = 'Polyvalente 1H/2H'
    if re.search(r'Non l[ée]tale', joined, re.I):
        found['non_letale'] = 'Non létale'
    m_rech = re.search(r'Rechargement lent\s+(\d+)\s*PA', joined, re.I)
    if m_rech:
        rechargement_pa = int(m_rech.group(1))
        found['rechargement_lent'] = f'Rechargement lent {rechargement_pa} PA'
    if re.search(r'\bCadence\b', joined, re.I):
        found['cadence'] = 'Cadence'
    if re.search(r'Canon Entrav[ée]', joined, re.I):
        # Attention : la description de Epée Métal Longue mentionne "Canon Entravé ... non applicable"
        if not re.search(r'Canon Entrav[ée][^.;]*non applicable', joined, re.I):
            found['canon_entrave'] = 'Canon Entravé'
    if re.search(r'\bAnti-Mat[ée]riel\b', joined, re.I):
        found['anti_materiel'] = 'Anti-Matériel'
    if re.search(r'Br[ûu]lure|En Feu|Étourdi|Immobilis[ée]|Asphyxie|Aveugl[ée]|Assourdi|Saignement|Ralenti|Paralys[ée]|D[ée]stabilis[ée]|À Terre', joined, re.I):
        # Ne déclencher brulure_etat que si ce n'est pas un rappel défensif ("absorbe En Feu")
        if not re.search(r'absorbe.*En Feu|prot[èe]ge|neutralis[ée]', joined, re.I):
            found['brulure_etat'] = 'Brûlure / État'

    order = [p[0] for p in PROP_DEFS]
    props = [{'id': pid, 'label': found[pid]} for pid in order if pid in found]
    return props, perforante, zone_cases, rechargement_pa, portee_jet_cases


def extract_states(text):
    known = [
        'Canon Entravé', 'À Terre', 'Accroupi', 'Déstabilisé', 'Aveuglé', 'Assourdi',
        'Ralenti', 'Immobilisé', 'Étourdi', 'Paralysé', 'Inconscient',
        'Saignement', 'Empoisonné', 'En Feu', 'Asphyxie', 'Souffrant', 'Rapide',
    ]
    out = []
    for st in known:
        if re.search(r'\b' + re.escape(st) + r'(?:\s+\d+)?', text):
            m = re.search(r'\b(' + re.escape(st) + r'(?:\s+\d+)?)', text)
            val = m.group(1) if m else st
            if val not in out:
                out.append(val)
    return out


def make_weapon_tags(nom, comp, props, etats, extra=()):
    tags = [nom, comp] + [p['label'] for p in props] + list(etats) + list(extra)
    return list(dict.fromkeys(t for t in tags if t))


def build_data():
    with open(PATH_L8, encoding='utf-8') as f:
        d8 = json.load(f)
    with open(PATH_L6, encoding='utf-8') as f:
        d6 = json.load(f)

    mec = d8['mecaniques']
    don = d8['donnees']

    # Correspondances nom -> résumé depuis donnees
    table_descs = {}
    table_equivs = {}
    for tkey in [
        'armes_lames', 'armes_fines', 'armes_choc_hast', 'armes_trait_jet',
        'armes_balistiques', 'epees_laser', 'fusils_laser', 'armes_lourdes',
        'grenades', 'grenades_tactiques', 'munitions_charges', 'equipement_arcanotech',
        'armures', 'boucliers_physiques', 'champs_force', 'materiel_medical', 'materiel_terrain'
    ]:
        tbl = don.get(tkey) or {}
        for row in tbl.get('lignes', []):
            name = strip_html(row[0])
            desc = strip_html(row[-1])
            table_descs[(tkey, name)] = desc
            if tkey == 'armures':
                table_equivs[name] = desc

    objets = []
    seen_ids = set()

    def add_obj(obj):
        oid = obj['id']
        if oid in seen_ids:
            raise ValueError(f'ID duplique: {oid}')
        seen_ids.add(oid)
        objets.append(obj)

    def ref_l8(sec, label):
        return {'label': label, 'href': f'livre_8.html#{sec}', 'livre': 8}

    # ---- 1 & 2. Mêlée : Lames & Fines + Choc & Haches ----
    def process_melee(tkey, cat_id, sec_id, sec_label):
        for it in mec[tkey]:
            nom = it['designation']
            desc = table_descs.get((tkey, nom), '')
            eps, emin, emax, elabel = parse_epochs(it['epoque'])
            extra_jet = None
            props, perf, zone, rech_pa, jet_c = parse_properties(it.get('proprietes'), desc, extra_jet=extra_jet)
            comp = it.get('competence') or "Maniement d'Arme"
            comps = [comp]
            if jet_c and 'Ballistique / Tir' not in comps:
                comps.append('Ballistique / Tir')
            etats = extract_states(' '.join(it.get('proprietes') or []) + ' ' + desc)
            # Pour Arme de jet sur mêlée (Dague, Lance, Hachette, Filet)
            if jet_c and comp != 'Ballistique / Tir':
                portee_lbl = f"{it['portee_cases']}P mêlée / {jet_c}P jet"
            else:
                portee_lbl = f"{it['portee_cases']}P ({round(it['portee_cases'] * 1.5, 1):g} m)".replace('.', ',')
            notes = []
            if 'polyvalente' in [p['id'] for p in props]:
                notes.append('Polyvalente : +1 Dégât lorsque maniée à deux mains (2H).')
            if jet_c:
                notes.append(f'Arme de jet ({jet_c}P) : +1 case par FOR > 3 (max +4P) ; −1 ec Accroupi, −2 ec À Terre.')
            if 'allonge' in [p['id'] for p in props]:
                notes.append('Allonge 2P : frappe à 2 cases même par-dessus un allié et menace 2 cases en opportunité.')
            if 'non_letale' in [p['id'] for p in props]:
                notes.append('Non létale : permet d\'arrêter la victime à Inconscient au Seuil Létal.')
            if perf:
                notes.append(f'Perforante {perf} : ignore jusqu\'à {perf} point(s) d\'armure physique (jamais les champs énergétiques).')
            refs = [
                {'label': 'Livre VIII §30.2 Propriétés', 'href': 'livre_8.html#sec-30-2'},
                {'label': 'Livre V §22.1 Attaque', 'href': 'livre_5.html#sec-22-1'},
            ]
            if jet_c:
                refs.append({'label': 'Livre III §12 Ballistique', 'href': 'livre_3.html#chap-12'})
            if 'non_letale' in [p['id'] for p in props] or etats:
                refs.append({'label': 'Livre VI §24 États', 'href': 'livre_6.html#chap-24'})

            add_obj({
                'id': slugify(nom),
                'nom': nom,
                'categorie': cat_id,
                'sous_categorie': {'armes_lames': 'Épées & Lames de Guerre', 'armes_fines': 'Armes Perçantes, Fines & Hast', 'armes_choc_hast': 'Choc, Haches & Capture'}[tkey],
                'famille': 'Arme à distance' if nom.startswith('Filet') else 'Arme de mêlée',
                'epoque_label': elabel,
                'epoques': eps,
                'epoque_min': emin,
                'epoque_max': emax,
                'dlph': it['dlph'],
                'degats': it['degats'],
                'degats_texte': f"{it['degats']}D",
                'bonus_ec': 0,
                'perforante': perf,
                'zone_cases': zone,
                'poids_livres': float(it['poids_livres']),
                'poids_label': f"{it['poids_livres']:g} L",
                'portee_cases': it['portee_cases'],
                'portee_jet_cases': jet_c,
                'portee_max_m': None,
                'portee_label': portee_lbl,
                'mains': it['mains'],
                'munitions': None,
                'rechargement_pa': rech_pa,
                'prix_ce': int(it['prix_ce']),
                'prix_max_ce': None,
                'prix_label': fmt_ce(it['prix_ce']),
                'protection': None,
                'protection_max': None,
                'absorption': None,
                'bonus_parade': 1 if '+1 parade' in ' '.join(it.get('proprietes') or []) else None,
                'encombrement_pa': 0,
                'encombrement_max_pa': None,
                'soin_degats': None,
                'cout_pa': '2 PA (attaque)',
                'duree': None,
                'competences': comps,
                'proprietes': props,
                'etats_infliges': etats,
                'etats_soignes': [],
                'consommable': False,
                'emplacement': 'arme',
                'resume': desc,
                'equivalents': None,
                'notes_regles': notes,
                'variantes': [],
                'tags': make_weapon_tags(nom, comp, props, etats, ('mêlée', 'DLPH')),
                'source': ref_l8(sec_id, sec_label),
                'refs': refs,
            })

    process_melee('armes_lames', 'melee-lames', 'sec-30-3', 'Livre VIII §30.3 Lames')
    process_melee('armes_fines', 'melee-lames', 'sec-30-3', 'Livre VIII §30.3 Fines & Hast')
    process_melee('armes_choc_hast', 'melee-choc', 'sec-30-3', 'Livre VIII §30.3 Choc & Haches')

    # ---- 3. Trait & Armes de Jet ----
    for it in mec['armes_trait_jet']:
        nom = it['designation']
        desc = table_descs.get(('armes_trait_jet', nom), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        props, perf, zone, rech_pa, jet_c = parse_properties(it.get('proprietes'), desc)
        is_thrown = any(p['id'] == 'arme_de_jet' for p in props)
        p_max_m = 70 if 'Arc Composite' in nom else None
        portee_lbl = f"{it['portee_cases']}P" + (f" ({p_max_m} m)" if p_max_m else f" ({round(it['portee_cases'] * 1.5, 1):g} m)".replace('.', ','))
        notes = []
        if is_thrown:
            notes.append(f'Arme de jet ({jet_c or it["portee_cases"]}P) : +1 case par FOR > 3 (max +4P) ; −1 ec Accroupi, −2 ec À Terre.')
        if 'Arc' in nom:
            notes.append('Impossible de tirer à l\'arc en étant À Terre (Livre VI §24.1).')
        if rech_pa:
            notes.append(f'Rechargement lent : {rech_pa} PA au lieu d\'1 PA.')
        else:
            notes.append('Rechargement standard : 1 PA (micro-action Livre V §20.4).')
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'trait-jet',
            'sous_categorie': 'Trait & Armes de Jet',
            'famille': 'Arme à distance',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': it['dlph'],
            'degats': it['degats'],
            'degats_texte': f"{it['degats']}D",
            'bonus_ec': 0,
            'perforante': perf,
            'zone_cases': zone,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': it['portee_cases'],
            'portee_jet_cases': jet_c if is_thrown else None,
            'portee_max_m': p_max_m,
            'portee_label': portee_lbl,
            'mains': it['mains'],
            'munitions': f"{it['munitions']} coup(s)" if it.get('munitions') else None,
            'rechargement_pa': rech_pa or (1 if 'Arc' in nom or 'Arbalète' in nom or 'Fronde' in nom else None),
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': '2 PA (tir/lancer)',
            'duree': None,
            'competences': ['Ballistique / Tir'],
            'proprietes': props,
            'etats_infliges': [],
            'etats_soignes': [],
            'consommable': False,
            'emplacement': 'arme',
            'resume': desc,
            'equivalents': None,
            'notes_regles': notes,
            'variantes': [],
            'tags': make_weapon_tags(nom, 'Ballistique / Tir', props, [], ('distance', 'trait', 'jet')),
            'source': ref_l8('sec-30-4', 'Livre VIII §30.4 Trait & Jet'),
            'refs': [
                {'label': 'Livre VIII §30.2 Propriétés', 'href': 'livre_8.html#sec-30-2'},
                {'label': 'Livre V §20.4 Recharger 1 PA', 'href': 'livre_5.html#sec-20-4'},
            ],
        })

    # ---- 4. Balistique à Poudre + Modérateur de son (§30.5) ----
    bal_m_map = {
        'Carabine à Verrou': 250,
        "Fusil d'Assaut Balistique": 200,
        'Fusil de Précision Balistique': 600,
        'Fusil Anti-Matériel': 1000,
    }
    for it in mec['armes_balistiques']:
        nom = it['designation']
        desc = table_descs.get(('armes_balistiques', nom), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        props, perf, zone, rech_pa, _ = parse_properties(it.get('proprietes'), desc)
        etats = extract_states(' '.join(it.get('proprietes') or []) + ' ' + desc)
        pm = bal_m_map.get(nom)
        portee_lbl = f"{it['portee_cases']}P" + (f" ({pm} m)" if pm else f" ({round(it['portee_cases'] * 1.5, 1):g} m)".replace('.', ','))
        notes = [f"Rechargement : {rech_pa or 1} PA (chargeur {it['munitions']} coups)."]
        if any(p['id'] == 'canon_entrave' for p in props):
            notes.append('Canon Entravé : −2 ec au tir si un ennemi conscient est à 1 case (Livre V §22.2).')
        if any(p['id'] == 'cadence' for p in props):
            notes.append('Cadence : +1 PA et 3 munitions sur un tir pour gagner +1 Dégât.')
        if not any(p['id'] == 'silencieuse' for p in props):
            notes.append('Compatible Modérateur de son (+500 CE) sur pistolet ou carabine à munitions subsoniques.')
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'balistique',
            'sous_categorie': 'Armes Balistiques à Poudre',
            'famille': 'Arme à distance',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': it['dlph'],
            'degats': it['degats'],
            'degats_texte': f"{it['degats']}D",
            'bonus_ec': 0,
            'perforante': perf,
            'zone_cases': zone,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': it['portee_cases'],
            'portee_jet_cases': None,
            'portee_max_m': pm,
            'portee_label': portee_lbl,
            'mains': it['mains'],
            'munitions': f"{it['munitions']} coups",
            'rechargement_pa': rech_pa or 1,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': '2 PA (tir)',
            'duree': None,
            'competences': ['Ballistique / Tir'],
            'proprietes': props,
            'etats_infliges': etats,
            'etats_soignes': [],
            'consommable': False,
            'emplacement': 'arme',
            'resume': desc,
            'equivalents': None,
            'notes_regles': notes,
            'variantes': [],
            'tags': make_weapon_tags(nom, 'Ballistique / Tir', props, etats, ('balistique', 'poudre', 'arme à feu')),
            'source': ref_l8('sec-30-5', 'Livre VIII §30.5 Balistique'),
            'refs': [
                {'label': 'Livre VIII §30.2 Propriétés', 'href': 'livre_8.html#sec-30-2'},
                {'label': 'Livre V §22.2 Canon Entravé', 'href': 'livre_5.html#sec-22-2'},
            ],
        })

    # Accessoire officiel de §30.5 : Modérateur de son
    add_obj({
        'id': 'moderateur-de-son',
        'nom': 'Modérateur de son',
        'categorie': 'balistique',
        'sous_categorie': 'Accessoire Balistique',
        'famille': 'Accessoire',
        'epoque_label': 'E2–E3',
        'epoques': ['E2', 'E3'],
        'epoque_min': 2,
        'epoque_max': 3,
        'dlph': None,
        'degats': None,
        'degats_texte': None,
        'bonus_ec': 0,
        'perforante': 0,
        'zone_cases': 0,
        'poids_livres': 0.4,
        'poids_label': '0.4 L',
        'portee_cases': None,
        'portee_jet_cases': None,
        'portee_max_m': None,
        'portee_label': None,
        'mains': 'Accessoire',
        'munitions': None,
        'rechargement_pa': None,
        'prix_ce': 500,
        'prix_max_ce': None,
        'prix_label': fmt_ce(500),
        'protection': None,
        'protection_max': None,
        'absorption': None,
        'bonus_parade': None,
        'encombrement_pa': 0,
        'encombrement_max_pa': None,
        'soin_degats': None,
        'cout_pa': 'Hors combat (pose)',
        'duree': None,
        'competences': ['Ballistique / Tir', 'Discrétion'],
        'proprietes': [{'id': 'silencieuse', 'label': 'Confère Silencieuse'}],
        'etats_infliges': [],
        'etats_soignes': [],
        'consommable': False,
        'emplacement': 'accessoire',
        'resume': 'Accessoire montable sur pistolet ou carabine à munitions subsoniques : supprime la détonation et confère la propriété Silencieuse (§30.5).',
        'equivalents': None,
        'notes_regles': [
            'Surcoût indicatif cité en Livre VIII §30.5 : +500 CE sur pistolet ou carabine à munitions subsoniques.',
        ],
        'variantes': [],
        'tags': ['Modérateur de son', 'Silencieux', 'Silencieuse', 'Ballistique', 'Discrétion', 'E2', 'E3'],
        'source': ref_l8('sec-30-5', 'Livre VIII §30.5 Modérateur de son'),
        'refs': [
            {'label': 'Livre VIII §30.2 Silencieuse', 'href': 'livre_8.html#sec-30-2'},
            {'label': 'Livre V §21.4 Furtivité', 'href': 'livre_5.html#sec-21-4'},
        ],
    })

    # ---- 5. Armes Laser & Plasma ----
    for it in mec['epees_laser']:
        nom = it['designation']
        desc = table_descs.get(('epees_laser', nom), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        props, perf, zone, rech_pa, _ = parse_properties(it.get('proprietes'), desc)
        etats = extract_states(' '.join(it.get('proprietes') or []) + ' ' + desc)
        notes = [
            f'Perforante {perf} : tranche les blindages physiques ordinaires mais bute normalement contre un champ de force ou bouclier énergétique (§31.1).',
            'Cellule de nytharite : alimente des semaines d\'escarmouches sans rechargement en combat.',
        ]
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'laser-energie',
            'sous_categorie': 'Épées Laser de Confinement Plasma',
            'famille': 'Arme de mêlée',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': it['dlph'],
            'degats': it['degats'],
            'degats_texte': f"{it['degats']}D",
            'bonus_ec': 0,
            'perforante': perf,
            'zone_cases': zone,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': it['portee_cases'],
            'portee_jet_cases': None,
            'portee_max_m': None,
            'portee_label': f"{it['portee_cases']}P ({round(it['portee_cases'] * 1.5, 1):g} m)".replace('.', ','),
            'mains': it['mains'],
            'munitions': 'Cellule nytharite (longue durée)',
            'rechargement_pa': rech_pa,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': 1 if '+1 parade' in ' '.join(it.get('proprietes') or []) else None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': '2 PA (attaque)',
            'duree': None,
            'competences': ["Maniement d'Arme"],
            'proprietes': props,
            'etats_infliges': etats,
            'etats_soignes': [],
            'consommable': False,
            'emplacement': 'arme',
            'resume': desc,
            'equivalents': None,
            'notes_regles': notes,
            'variantes': [],
            'tags': make_weapon_tags(nom, "Maniement d'Arme", props, etats, ('laser', 'plasma', 'épée laser')),
            'source': ref_l8('sec-31-1', 'Livre VIII §31.1 Épées Laser'),
            'refs': [
                {'label': 'Livre VIII §30.2 Perforante', 'href': 'livre_8.html#sec-30-2'},
                {'label': 'Livre VIII §32.2 Champs de Force', 'href': 'livre_8.html#sec-32-2'},
            ],
        })

    for it in mec['fusils_laser']:
        nom = it['modele']
        desc = table_descs.get(('fusils_laser', nom), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        props, perf, zone, rech_pa, _ = parse_properties(it.get('proprietes'), desc)
        pm = it.get('portee_max_m')
        portee_lbl = f"{it['portee_cases']}P" + (f" ({pm} m)" if pm else '')
        bonus_ec = int(it.get('bonus_ec', 0))
        notes = [
            'Faisceau laser : aucune dérive au vent ; accepte Charge Laser Standard (ou Haute Densité +1D pour modèles compatibles).',
            'Rechargement standard : 1 PA (micro-action Livre V §20.4).',
        ]
        if bonus_ec:
            notes.append(f'Prisme de nytharite pure : confère +{bonus_ec} ec matériel au tir.')
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'laser-energie',
            'sous_categorie': 'Fusils Laser & Armes Énergétiques',
            'famille': 'Arme à distance',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': it['dlph'],
            'degats': it['degats'],
            'degats_texte': (f"+{bonus_ec} ec · " if bonus_ec else "") + f"{it['degats']}D",
            'bonus_ec': bonus_ec,
            'perforante': perf,
            'zone_cases': zone,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': it['portee_cases'],
            'portee_jet_cases': None,
            'portee_max_m': pm,
            'portee_label': portee_lbl,
            'mains': it['mains'],
            'munitions': f"{it['munitions']} coups",
            'rechargement_pa': rech_pa or 1,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': '2 PA (tir)',
            'duree': None,
            'competences': ['Ballistique / Tir'],
            'proprietes': props,
            'etats_infliges': [],
            'etats_soignes': [],
            'consommable': False,
            'emplacement': 'arme',
            'resume': desc,
            'equivalents': None,
            'notes_regles': notes,
            'variantes': [],
            'tags': make_weapon_tags(nom, 'Ballistique / Tir', props, [], ('laser', 'énergie', 'nytharite')),
            'source': ref_l8('sec-31-2', 'Livre VIII §31.2 Fusils Laser'),
            'refs': [
                {'label': 'Livre VIII §31.4 Charges Laser', 'href': 'livre_8.html#sec-31-4'},
                {'label': 'Livre V §22.2 Canon Entravé', 'href': 'livre_5.html#sec-22-2'},
            ],
        })

    # ---- 6. Armes Lourdes & Explosifs ----
    for it in mec['armes_lourdes']:
        nom = it['designation']
        desc = table_descs.get(('armes_lourdes', nom), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        props, perf, zone, rech_pa, _ = parse_properties(it.get('proprietes'), desc)
        etats = extract_states(' '.join(it.get('proprietes') or []) + ' ' + desc)
        is_lg = 'Lance-Grenades' in nom
        deg_txt = 'Selon grenade' if is_lg else (f"{it['degats']}D+2d10" if '10D+2d10' in it['dlph'] else f"{it['degats']}D")
        pm = 300 if 'Mitrailleuse' in nom else (1000 if 'Canon Rail' in nom else None)
        portee_lbl = f"{it['portee_cases']}P" + (f" ({pm} m)" if pm else f" ({round(it['portee_cases'] * 1.5, 1):g} m)".replace('.', ','))
        variantes = []
        if is_lg:
            variantes = [
                {'nom': 'Modèle coup par coup (1 coup)', 'epoque_label': elabel, 'poids_livres': 8.0, 'prix_ce': 12000, 'note': '1 coup ; rechargement standard 1 PA.'},
                {'nom': 'Modèle à barillet (6 coups)', 'epoque_label': elabel, 'poids_livres': 8.0, 'prix_ce': 12000, 'note': '6 coups ; rechargement 2 PA.'},
            ]
        notes = [
            'Débloque +100 dégâts de masse contre structures et véhicules avec la spécialisation Armes Lourdes (Canons) (Livre III §14).',
        ]
        if is_lg:
            notes.append('Projette toute grenade à 18P (+10 cases par rapport au lancer à la main).')
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'armes-lourdes',
            'sous_categorie': 'Armes Lourdes & Anti-Matériel',
            'famille': 'Arme à distance',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': it['dlph'],
            'degats': it['degats'],
            'degats_texte': deg_txt,
            'bonus_ec': 0,
            'perforante': perf,
            'zone_cases': zone,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': it['portee_cases'],
            'portee_jet_cases': None,
            'portee_max_m': pm,
            'portee_label': portee_lbl,
            'mains': it['mains'],
            'munitions': f"{it['munitions']} coup(s)" if not is_lg else '1 ou 6 coups',
            'rechargement_pa': rech_pa or (2 if is_lg else 1),
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': '2 PA (tir)',
            'duree': None,
            'competences': ['Ballistique / Tir'],
            'proprietes': props,
            'etats_infliges': etats,
            'etats_soignes': [],
            'consommable': False,
            'emplacement': 'arme',
            'resume': desc,
            'equivalents': None,
            'notes_regles': notes,
            'variantes': variantes,
            'tags': make_weapon_tags(nom, 'Ballistique / Tir', props, etats, ('arme lourde', 'véhicule', 'anti-matériel')),
            'source': ref_l8('sec-31-2', 'Livre VIII §31.2 Armes Lourdes'),
            'refs': [
                {'label': 'Livre V §22.3 Véhicules & Calibres', 'href': 'livre_5.html#sec-22-3'},
                {'label': 'Livre III §14 Armes Lourdes', 'href': 'livre_3.html#chap-14'},
            ],
        })

    for it in mec['grenades']:
        nom = it['type']
        desc = table_descs.get(('grenades', nom), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        poids = 1.0 if '1.0L' in it['dlph'] else 0.5
        props, perf, zone, _, jet_c = parse_properties(['Arme de jet 8P', f"Zone {it['rayon_cases']}"], desc, extra_zone=it['rayon_cases'], extra_jet=8)
        etats = list(it.get('etats') or [])
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'armes-lourdes',
            'sous_categorie': 'Grenades Explosives',
            'famille': 'Explosif / Zone',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': it['dlph'],
            'degats': 10,
            'degats_texte': it['puissance'],
            'bonus_ec': 0,
            'perforante': perf,
            'zone_cases': it['rayon_cases'],
            'poids_livres': poids,
            'poids_label': f"{poids:g} L",
            'portee_cases': 8,
            'portee_jet_cases': 8,
            'portee_max_m': None,
            'portee_label': '8P (+FOR>3, max +4P ; 18P au lanceur)',
            'mains': '1H',
            'munitions': 'Usage unique',
            'rechargement_pa': None,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': '2 PA (1 PA Grenadier)',
            'duree': None,
            'competences': ['Ballistique / Tir'],
            'proprietes': props,
            'etats_infliges': etats,
            'etats_soignes': [],
            'consommable': True,
            'emplacement': 'consommable',
            'resume': desc,
            'equivalents': None,
            'notes_regles': [
                'Lancer à 2 PA (1 PA avec Grenadier d\'Assaut, Livre III §14) sur Ballistique / Tir.',
                'Couvert demi / trois-quarts / total réduit les dégâts et effets de zone (Livre V §21.3).',
            ],
            'variantes': [],
            'tags': make_weapon_tags(nom, 'Ballistique / Tir', props, etats, ('grenade', 'explosif', 'zone')),
            'source': ref_l8('sec-31-3', 'Livre VIII §31.3 Grenades'),
            'refs': [
                {'label': 'Livre V §21.3 Couvert & Zone', 'href': 'livre_5.html#sec-21-3'},
                {'label': 'Livre III §14 Grenadier d\'Assaut', 'href': 'livre_3.html#chap-14'},
                {'label': 'Livre VI §24 États', 'href': 'livre_6.html#chap-24'},
            ],
        })

    # Grenades tactiques : retrouver nom exact dans table_descs (Grenade IEM (Impulsion) vs Grenade IEM)
    gtac_poids = {
        'Pot Incendiaire': 1.5,
        'Grenade Fumigène': 1.0,
        'Grenade Étourdissante (Flash)': 0.6,
        'Grenade Lacrymogène / Gaz': 0.7,
        'Grenade IEM': 0.8,
        'Grenade Cryogénique': 0.8,
        'Grenade Graviton': 1.2,
        'Grenade de Stase Chronale': 0.8,
    }
    for it in mec['grenades_tactiques']:
        raw_nom = it['type']
        nom_tbl = 'Grenade IEM (Impulsion)' if raw_nom == 'Grenade IEM' else raw_nom
        desc = table_descs.get(('grenades_tactiques', nom_tbl), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        poids = gtac_poids.get(raw_nom, 0.8)
        deg_num = int(re.match(r'^(\d+)D', it['degats']).group(1))
        dlph = f"{deg_num}D - {poids:g}L - 8P - 1H"
        props, perf, zone, _, _ = parse_properties(
            ['Arme de jet 8P', f"Zone {it['rayon_cases']}"],
            desc + ' ' + ' '.join(it.get('etats') or []),
            extra_zone=it['rayon_cases'],
            extra_jet=8,
        )
        # Stase Chronale / Graviton / Cryo / IEM / Gaz / Flash posent toutes des états
        if not any(p['id'] == 'brulure_etat' for p in props):
            props.append({'id': 'brulure_etat', 'label': 'Brûlure / État'})
        etats = list(it.get('etats') or [])
        refs = [
            {'label': 'Livre V §21.3 Couvert & Zone', 'href': 'livre_5.html#sec-21-3'},
            {'label': 'Livre VI §24 États', 'href': 'livre_6.html#chap-24'},
        ]
        if 'Stase' in nom_tbl:
            refs.append({'label': 'Livre X Causalité', 'href': 'livre_10.html#chap-42'})
        if 'Fumigène' in nom_tbl or 'Flash' in nom_tbl or 'Gaz' in nom_tbl:
            refs.append({'label': 'Livre II §8.6 Masque Filtrant', 'href': 'livre_2.html#sec-8-6'})
        add_obj({
            'id': slugify(nom_tbl),
            'nom': nom_tbl,
            'categorie': 'armes-lourdes',
            'sous_categorie': 'Grenades Tactiques & Arcanotech',
            'famille': 'Explosif / Zone',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': dlph,
            'degats': deg_num,
            'degats_texte': it['degats'],
            'bonus_ec': 0,
            'perforante': perf,
            'zone_cases': it['rayon_cases'],
            'poids_livres': poids,
            'poids_label': f"{poids:g} L",
            'portee_cases': 8,
            'portee_jet_cases': 8,
            'portee_max_m': None,
            'portee_label': '8P (+FOR>3, max +4P ; 18P au lanceur)',
            'mains': '1H',
            'munitions': 'Usage unique',
            'rechargement_pa': None,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': '2 PA (1 PA Grenadier)',
            'duree': '2 tours' if any(w in desc for w in ('2 tours',)) else ('1 tour' if '1 tour' in desc else None),
            'competences': ['Ballistique / Tir'],
            'proprietes': props,
            'etats_infliges': etats,
            'etats_soignes': [],
            'consommable': True,
            'emplacement': 'consommable',
            'resume': desc,
            'equivalents': None,
            'notes_regles': [
                'Lancer à 2 PA (1 PA avec Grenadier d\'Assaut). Un Masque Filtrant protège contre fumée, gaz et flash associé (Livre II §8.6).',
            ],
            'variantes': [],
            'tags': make_weapon_tags(nom_tbl, 'Ballistique / Tir', props, etats, ('grenade', 'tactique', 'zone')),
            'source': ref_l8('sec-31-3', 'Livre VIII §31.3 Grenades Tactiques'),
            'refs': refs,
        })

    # ---- 7. Munitions, Nytharite & Implants ----
    for it in mec['munitions_charges']:
        nom_raw = it['designation']
        nom_tbl = nom_raw.replace('(x', '(×')
        desc = table_descs.get(('munitions_charges', nom_tbl), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        props = []
        perf = 0
        if 'Perforantes' in nom_tbl:
            props.append({'id': 'perforante', 'label': '+1 Perforante'})
            perf = 1
        notes = []
        if 'Cellule Nytharite' in nom_tbl:
            notes.append('Recharge un champ de force épuisé en 2 PA au combat (Livre VIII §32.2).')
        elif 'Carquois' in nom_tbl:
            notes.append('Environ 50 % des traits sont récupérables après combat.')
        else:
            notes.append('Recharger au combat coûte 1 PA (micro-action Livre V §20.4).')
        add_obj({
            'id': slugify(nom_tbl),
            'nom': nom_tbl,
            'categorie': 'munitions-arcanotech',
            'sous_categorie': 'Munitions, Charges & Cellules',
            'famille': 'Munition / Énergie',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': None,
            'degats': None,
            'degats_texte': '+1D (Haute Densité)' if 'Haute Densité' in nom_tbl else None,
            'bonus_ec': 0,
            'perforante': perf,
            'zone_cases': 0,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': None,
            'portee_jet_cases': None,
            'portee_max_m': None,
            'portee_label': None,
            'mains': 'Consommable',
            'munitions': None,
            'rechargement_pa': 2 if 'Cellule Nytharite' in nom_tbl else 1,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': '2 PA (champ)' if 'Cellule Nytharite' in nom_tbl else '1 PA (recharge)',
            'duree': None,
            'competences': ['Ballistique / Tir'] if 'Batterie' not in nom_tbl else ['Observation', 'Artisanat / Ingénierie'],
            'proprietes': props,
            'etats_infliges': [],
            'etats_soignes': [],
            'consommable': True,
            'emplacement': 'munition',
            'resume': desc,
            'equivalents': None,
            'notes_regles': notes,
            'variantes': [],
            'tags': [nom_tbl, 'Munitions', 'Recharge', 'Nytharite' if 'Nytharite' in nom_tbl else 'Énergie'],
            'source': ref_l8('sec-31-4', 'Livre VIII §31.4 Munitions & Charges'),
            'refs': [
                {'label': 'Livre V §20.4 Recharger 1 PA', 'href': 'livre_5.html#sec-20-4'},
                {'label': 'Livre VIII §32.2 Champs de Force', 'href': 'livre_8.html#sec-32-2'},
            ],
        })

    for it in mec['equipement_arcanotech']:
        nom = it['designation']
        desc = table_descs.get(('equipement_arcanotech', nom), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        is_implant = it['poids_livres'] == 0
        poids = float(it['poids_livres'])
        prot = it.get('protection')
        comps = []
        if 'Résonateur' in nom or 'Module' in nom:
            comps = ['5e Force / Magie']
        elif 'Brouilleur' in nom:
            comps = ['Discrétion']
        elif 'Optique' in nom:
            comps = ['Observation']
        elif 'Interface' in nom:
            comps = ['Ballistique / Tir']
        elif 'Éclat' in nom or 'Moteur' in nom:
            comps = ['Artisanat / Ingénierie']
        notes = []
        refs = [{'label': 'Livre IV §17 Cristaux & Nytharite', 'href': 'livre_4.html#chap-17'}]
        if is_implant:
            notes.append('Implant cybernétique [E3–E4] : poids nul, mais sensible aux impulsions IEM et exposé aux pathologies nanotech / cyber (Livre VI §25.4).')
            refs.append({'label': 'Livre VI §25.4 Pathologies Cyber', 'href': 'livre_6.html#sec-25-4'})
        if 'Plaque Sous-Dermique' in nom:
            notes.append('Confère Protection 1 (0 PA d\'encombrement), non cumulable avec une armure lourde ≥ 5.')
        if 'Module Arcanotech' in nom:
            notes.append('3 charges : tout porteur peut déclencher 1 sort pré-gravé de coût ≤ 3 PA (Livre IV §17).')
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'munitions-arcanotech',
            'sous_categorie': 'Implants Cybernétiques' if is_implant else 'Catalyseurs & Modules Arcanotech',
            'famille': 'Arcanotech / Implant',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': None,
            'degats': None,
            'degats_texte': None,
            'bonus_ec': 0,
            'perforante': 0,
            'zone_cases': 0,
            'poids_livres': poids,
            'poids_label': '— (Implant)' if is_implant else f'{poids:g} L',
            'portee_cases': None,
            'portee_jet_cases': None,
            'portee_max_m': None,
            'portee_label': None,
            'mains': 'Implant' if is_implant else 'Porté',
            'munitions': '3 charges' if '3 charges' in nom else None,
            'rechargement_pa': None,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': prot,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': None,
            'duree': None,
            'competences': comps,
            'proprietes': [],
            'etats_infliges': [],
            'etats_soignes': [],
            'consommable': '3 charges' in nom,
            'emplacement': 'implant' if is_implant else 'outil',
            'resume': desc,
            'equivalents': None,
            'notes_regles': notes,
            'variantes': [],
            'tags': [nom, 'Implant' if is_implant else 'Arcanotech', 'Nytharite', 'Cybernétique' if is_implant else '5e Force'] + comps,
            'source': ref_l8('sec-31-4', 'Livre VIII §31.4 Arcanotech & Implants'),
            'refs': refs,
        })

    # ---- 8. Armures, Boucliers & Champs de Force ----
    for it in mec['armures']:
        nom = it['designation']
        equiv = table_equivs.get(nom, '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        enc = int(it['encombrement_pa'])
        prot = int(it['protection'])
        notes = [
            '1 seule armure corporelle portée à la fois ; soustraite de chaque coup direct après le différentiel (jamais des dégâts résiduels ni absolus).',
        ]
        if enc > 0:
            notes.append(f'Encombrement +{enc} PA sur chaque action et case de déplacement (hors mises aveugles ; don Mikyai Technicien-Guerrier réduit de 1).')
        else:
            notes.append('Encombrement 0 PA : aucun malus de mobilité ni d\'action.')
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'armures-boucliers',
            'sous_categorie': f"Armures {it['categorie']}s",
            'famille': 'Armure',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': None,
            'degats': None,
            'degats_texte': None,
            'bonus_ec': 0,
            'perforante': 0,
            'zone_cases': 0,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': None,
            'portee_jet_cases': None,
            'portee_max_m': None,
            'portee_label': None,
            'mains': 'Porté',
            'munitions': None,
            'rechargement_pa': None,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': prot,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': enc,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': f'+{enc} PA/action' if enc else '0 PA',
            'duree': None,
            'competences': [],
            'proprietes': [],
            'etats_infliges': [],
            'etats_soignes': [],
            'consommable': False,
            'emplacement': 'armure',
            'resume': f"Armure {it['categorie'].lower()} offrant Protection {prot} et Encombrement +{enc} PA. Équivalents : {equiv}",
            'equivalents': equiv,
            'notes_regles': notes,
            'variantes': [],
            'tags': [nom, f'Protection {prot}', f'Encombrement {enc}', it['categorie'], equiv],
            'source': ref_l8('sec-32-1', 'Livre VIII §32.1 Armures'),
            'refs': [
                {'label': 'Livre V §22.1 Résolution d\'attaque', 'href': 'livre_5.html#sec-22-1'},
                {'label': 'Livre VI §23 Encaissement', 'href': 'livre_6.html#chap-23'},
                {'label': 'Livre I §2.2 Technicien-Guerrier', 'href': 'livre_1.html#chap-2'},
            ],
        })

    for it in mec['boucliers_physiques']:
        nom = it['designation']
        desc = table_descs.get(('boucliers_physiques', nom), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        enc = int(it['encombrement_pa'])
        parade = int(it['bonus_parade'])
        notes = [
            f'Défense active 1H : +{parade} en parade (Maniement d\'Arme ou Défense Corporelle). Incompatible avec les armes 2H.',
            'Avec la spécialisation Bloquer (Livre III §14), pare sans malus et débloque les ripostes de bouclier.',
        ]
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'armures-boucliers',
            'sous_categorie': 'Boucliers Actifs (1H)',
            'famille': 'Bouclier',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': None,
            'degats': None,
            'degats_texte': None,
            'bonus_ec': 0,
            'perforante': 0,
            'zone_cases': 0,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': None,
            'portee_jet_cases': None,
            'portee_max_m': None,
            'portee_label': None,
            'mains': it['mains'],
            'munitions': None,
            'rechargement_pa': None,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': 5 if 'Énergétique' in nom else None,
            'bonus_parade': parade,
            'encombrement_pa': enc,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': f'+{enc} PA/action' if enc else '0 PA',
            'duree': None,
            'competences': ['Défense Corporelle', "Maniement d'Arme"],
            'proprietes': [],
            'etats_infliges': [],
            'etats_soignes': [],
            'consommable': False,
            'emplacement': 'bouclier',
            'resume': desc,
            'equivalents': None,
            'notes_regles': notes,
            'variantes': [],
            'tags': [nom, 'Bouclier', f'+{parade} parade', '1H', 'Bloquer'],
            'source': ref_l8('sec-32-2', 'Livre VIII §32.2 Boucliers Physiques'),
            'refs': [
                {'label': 'Livre III §14 Bloquer', 'href': 'livre_3.html#chap-14'},
                {'label': 'Livre VI §24.2 En Feu', 'href': 'livre_6.html#sec-24-2'},
            ],
        })

    for it in mec['champs_force']:
        nom = it['equipement']
        desc = table_descs.get(('champs_force', nom), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        is_suit = 'Combinaison' in nom
        variantes = []
        if is_suit and 'Légère' in nom:
            prix_min, prix_max = 50000, 90000
            prix_lbl = '50 000 – 90 000 CE'
            prot, prot_max = 2, 3
            enc, enc_max = 0, None
            variantes = [
                {'nom': 'Combinaison Spatiale Légère (Blindage 2)', 'epoque_label': elabel, 'poids_livres': 15.0, 'prix_ce': 50000, 'protection': 2, 'encombrement_pa': 0, 'note': 'Blindage 2, 48 h d\'air.'},
                {'nom': 'Combinaison Spatiale Légère (Blindage 3)', 'epoque_label': elabel, 'poids_livres': 15.0, 'prix_ce': 90000, 'protection': 3, 'encombrement_pa': 0, 'note': 'Blindage 3, 48 h d\'air.'},
            ]
        elif is_suit and 'Lourde' in nom:
            prix_min, prix_max = 75000, 750000
            prix_lbl = '75 000 – 750 000 CE'
            prot, prot_max = 3, 6
            enc, enc_max = 1, 2
            variantes = [
                {'nom': 'Combinaison Spatiale Lourde (Blindage 3)', 'epoque_label': elabel, 'poids_livres': 40.0, 'prix_ce': 75000, 'protection': 3, 'encombrement_pa': 1, 'note': 'Blindage 3, encombrement +1 PA.'},
                {'nom': 'Combinaison Spatiale Lourde (Blindage 6)', 'epoque_label': elabel, 'poids_livres': 40.0, 'prix_ce': 750000, 'protection': 6, 'encombrement_pa': 2, 'note': 'Blindage 6, encombrement +2 PA.'},
            ]
        else:
            prix_min, prix_max = int(it['prix_ce']), None
            prix_lbl = fmt_ce(prix_min)
            prot, prot_max = None, None
            enc, enc_max = int(it['malus_pa']), None

        absorp = it.get('absorption_degats')
        notes = []
        if is_suit:
            notes.append('Tenue spatiale hermétique : survie en vide spatial, atmosphère toxique et gravité zéro (compte comme armure corporelle).')
        else:
            notes.append(f'Se cumule avec ton armure corporelle (1 seul champ actif à la fois). Absorbe {absorp} dégâts en premier (y compris En Feu).')
            notes.append('Tant que le champ absorbe tout le coup, aucun Choc Traumatique n\'est déclenché. Rechargeable au combat en 2 PA via Cellule de Nytharite.')
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'armures-boucliers',
            'sous_categorie': 'Combinaisons Spatiales' if is_suit else 'Champs de Force Personnels',
            'famille': 'Champ / Combinaison',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': None,
            'degats': None,
            'degats_texte': None,
            'bonus_ec': 0,
            'perforante': 0,
            'zone_cases': 0,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': None,
            'portee_jet_cases': None,
            'portee_max_m': None,
            'portee_label': None,
            'mains': 'Porté',
            'munitions': None,
            'rechargement_pa': 2 if not is_suit else None,
            'prix_ce': prix_min,
            'prix_max_ce': prix_max,
            'prix_label': prix_lbl,
            'protection': prot,
            'protection_max': prot_max,
            'absorption': absorp,
            'bonus_parade': None,
            'encombrement_pa': enc,
            'encombrement_max_pa': enc_max,
            'soin_degats': None,
            'cout_pa': f'+{enc} PA' if enc else '0 PA',
            'duree': '48 h d\'air' if is_suit else None,
            'competences': [],
            'proprietes': [],
            'etats_infliges': [],
            'etats_soignes': [],
            'consommable': False,
            'emplacement': 'armure' if is_suit else 'champ',
            'resume': desc,
            'equivalents': None,
            'notes_regles': notes,
            'variantes': variantes,
            'tags': [nom, 'Champ de force' if not is_suit else 'Combinaison spatiale', 'Vide spatial', 'Énergétique'],
            'source': ref_l8('sec-32-2', 'Livre VIII §32.2 Champs & Combinaisons'),
            'refs': [
                {'label': 'Livre VI §23 Choc Traumatique', 'href': 'livre_6.html#sec-23-encaissement'},
                {'label': 'Livre VI §24.2 En Feu', 'href': 'livre_6.html#sec-24-2'},
                {'label': 'Livre VIII §31.4 Cellule Nytharite', 'href': 'livre_8.html#sec-31-4'},
            ],
        })

    # ---- 9. Pharmacopée, Soins & Rations ----
    for it in mec['pharmacie']['stimulants']:
        nom = it['nom']
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        is_vif = 'Vif' in nom
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'pharmacie-medical',
            'sous_categorie': 'Stimulants & Régulateurs',
            'famille': 'Stimulant / Ration',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': None,
            'degats': None,
            'degats_texte': None,
            'bonus_ec': 0,
            'perforante': 0,
            'zone_cases': 0,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': None,
            'portee_jet_cases': None,
            'portee_max_m': None,
            'portee_label': None,
            'mains': 'Consommable',
            'munitions': '4 comprimés' if is_vif else '8 comprimés',
            'rechargement_pa': None,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': '1 PA',
            'duree': it['duree'],
            'competences': ['Système Immunitaire'],
            'proprietes': [],
            'etats_infliges': ['Rapide'] if is_vif else [],
            'etats_soignes': [],
            'consommable': True,
            'emplacement': 'consommable',
            'resume': f"Durée {it['duree']}. {it['effet']}.",
            'equivalents': None,
            'notes_regles': [
                'Non cumulable : prendre une 2e dose avant expiration inflige +2 Essoufflement et impose Système Immunitaire Diff 6 (ou Empoisonné 2).',
                'Contrecoup : +2 Essoufflement à l\'expiration normale (si dégâts > vrai Seuil Létal à la fin d\'Antidouleurs, l\'arbre de mort se déclenche).',
            ],
            'variantes': [],
            'tags': [nom, 'Stimulant', 'Essoufflement', 'Rapide' if is_vif else 'Seuil Létal'],
            'source': ref_l8('sec-32-3', 'Livre VIII §32.3 Stimulants'),
            'refs': [
                {'label': 'Livre VI §23.2 Seuil Létal', 'href': 'livre_6.html#sec-23-2'},
                {'label': 'Livre VI §24.3 Rapide', 'href': 'livre_6.html#sec-24-3'},
                {'label': 'Livre V §20.4 Micro-action 1 PA', 'href': 'livre_5.html#sec-20-4'},
            ],
        })

    for it in mec['pharmacie']['seringues_rations']:
        nom = it['nom'].replace('(-', '(−')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        soin = it.get('soin_degats')
        is_ser = soin is not None
        if is_ser:
            if 'généralisée' in nom:
                res = f"1 PA : soigne {soin} dégâts sur l'ensemble des membres blessés, lève Souffrant."
            elif soin == 20:
                res = f"1 PA : soin d'élite intensif (−{soin} dégâts), lève Souffrant et stoppe Saignement."
            else:
                res = f"1 PA : efface {soin} dégâts, lève Souffrant et stoppe Saignement."
            soignes = ['Souffrant', 'Saignement'] if 'généralisée' not in nom else ['Souffrant']
        else:
            res = {
                'Ration de Campagne': '1 journée de vivres compacts et durables pour une expédition.',
                'Ration + Eau': '1 journée complète de vivres et d\'hydratation en campagne.',
                "Poche d'Eau 1 L": '1 litre d\'eau potable en gourde souple ou poche pressurisée.',
            }[nom]
            soignes = []
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'pharmacie-medical',
            'sous_categorie': 'Seringues d\'Urgence' if is_ser else 'Rations & Hydratation',
            'famille': 'Soin / Médical' if is_ser else 'Stimulant / Ration',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': None,
            'degats': None,
            'degats_texte': None,
            'bonus_ec': 0,
            'perforante': 0,
            'zone_cases': 0,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': None,
            'portee_jet_cases': None,
            'portee_max_m': None,
            'portee_label': None,
            'mains': 'Consommable',
            'munitions': '1 dose' if is_ser else '1 ration',
            'rechargement_pa': None,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': soin,
            'cout_pa': '1 PA' if is_ser else 'Hors combat',
            'duree': None,
            'competences': ['Premiers Soins'] if is_ser else [],
            'proprietes': [],
            'etats_infliges': [],
            'etats_soignes': soignes,
            'consommable': True,
            'emplacement': 'consommable',
            'resume': res,
            'equivalents': None,
            'notes_regles': [
                'Les seringues lèvent Souffrant mais ne guérissent jamais les infections, poisons ni pathologies (Livre VI §25.4).'
            ] if is_ser else [
                'Prévient les malus d\'Affamé / Assoiffé (Livre VI §24.1).'
            ],
            'variantes': [],
            'tags': [nom, 'Soin' if is_ser else 'Ration', 'Survie'] + soignes,
            'source': ref_l8('sec-32-3', 'Livre VIII §32.3 Seringues & Rations'),
            'refs': [
                {'label': 'Livre VI §24.4 Souffrant', 'href': 'livre_6.html#sec-24-4'},
                {'label': 'Livre VI §25.4 Pathologies', 'href': 'livre_6.html#sec-25-4'},
            ] if is_ser else [
                {'label': 'Livre VI §24.1 Affamé / Assoiffé', 'href': 'livre_6.html#sec-24-1'},
            ],
        })

    for it in mec['materiel_medical']:
        nom = it['nom']
        desc = table_descs.get(('materiel_medical', nom), it.get('effet', ''))
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        is_cons = any(w in nom for w in ('Bandage', 'Antidote', 'Antibiotiques', 'Antiviraux', 'Purge'))
        soignes = []
        if 'Saignement' in desc:
            soignes.extend(['Saignement', 'Souffrant'])
        if 'Empoisonné' in desc:
            soignes.append('Empoisonné')
        if 'Ralenti' in desc:
            soignes.append('Ralenti')
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'pharmacie-medical',
            'sous_categorie': 'Matériel Médical & Remèdes',
            'famille': 'Soin / Médical',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': None,
            'degats': None,
            'degats_texte': None,
            'bonus_ec': 0,
            'perforante': 0,
            'zone_cases': 0,
            'poids_livres': float(it['poids_livres']),
            'poids_label': f"{it['poids_livres']:g} L",
            'portee_cases': None,
            'portee_jet_cases': None,
            'portee_max_m': None,
            'portee_label': None,
            'mains': 'Consommable' if is_cons else 'Outil',
            'munitions': '5 usages' if '5 usages' in nom else None,
            'rechargement_pa': None,
            'prix_ce': int(it['prix_ce']),
            'prix_max_ce': None,
            'prix_label': fmt_ce(it['prix_ce']),
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': 2 if 'Bandage' in nom else None,
            'cout_pa': '1 PA' if '1 PA' in desc else ('Hors combat' if 'hors combat' in desc.lower() else '3 PA (Premiers Soins)'),
            'duree': None,
            'competences': ['Premiers Soins', 'Système Immunitaire'] if 'Antibio' in nom or 'Antivir' in nom or 'Purge' in nom else ['Premiers Soins'],
            'proprietes': [],
            'etats_infliges': [],
            'etats_soignes': soignes,
            'consommable': is_cons,
            'emplacement': 'consommable' if is_cons else 'outil',
            'resume': desc,
            'equivalents': None,
            'notes_regles': [
                'Chaque nature pathologique (bactérienne, virale, parasitaire, arcanique, nanotech/cyber) exige le traitement adapté (Livre VI §25.4 / Livre XII §53).'
            ] if any(w in nom for w in ('Antibio', 'Antivir', 'Purge', 'Coffret')) else [],
            'variantes': [],
            'tags': [nom, 'Médical', 'Premiers Soins', 'Pathologie'] + soignes,
            'source': ref_l8('sec-32-3', 'Livre VIII §32.3 Matériel Médical'),
            'refs': [
                {'label': 'Livre VI §25 Premiers Soins & Maladies', 'href': 'livre_6.html#chap-25'},
                {'label': 'Livre XII §53 Pathologies', 'href': 'livre_12.html#chap-53'},
            ],
        })

    # ---- 10. Outillage & Matériel de Terrain ----
    terrain_extra = {
        'Masque Filtrant': {'comps': ['Système Immunitaire', 'Observation'], 'soignes': ['Asphyxie', 'Aveuglé'], 'refs': [{'label': 'Livre II §8.6 Visibilité', 'href': 'livre_2.html#sec-8-6'}]},
        'Trousse de Crochetage': {'comps': ['Subterfuge'], 'refs': [{'label': 'Livre III §13 Subterfuge', 'href': 'livre_3.html#chap-13'}]},
        "Caisse à Outils / Kit d'Entretien": {'comps': ['Artisanat / Ingénierie'], 'refs': [{'label': 'Livre VIII §30.1 Réparation', 'href': 'livre_8.html#sec-30-1'}, {'label': 'Livre VI §24.5 États Objets', 'href': 'livre_6.html#sec-24-5'}]},
        'Valise Arcanotech / Scanner': {'comps': ['Artisanat / Ingénierie', '5e Force / Magie']},
        'Pièges & Collets de Campagne': {'comps': ['Survie / Nature'], 'etats': ['Immobilisé']},
        'Corde 20 m / Grappin': {
            'comps': ['Athlétisme', 'Acrobatie'],
            'variantes': [
                {'nom': 'Corde 20 m / Grappin standard', 'epoque_label': 'E0–E4', 'poids_livres': 3.0, 'prix_ce': 60, 'note': 'Escalade et ligotage.'},
                {'nom': 'Grappin magnétique motorisé', 'epoque_label': 'E3–E4', 'poids_livres': 2.5, 'prix_ce': 900, 'note': 'Treuil motorisé portée 15 cases.'},
            ],
        },
        'Torche / Lampe Tactique': {
            'comps': ['Observation'],
            'variantes': [
                {'nom': 'Torche résineuse (1 h)', 'epoque_label': 'E0–E1', 'poids_livres': 0.5, 'prix_ce': 20, 'note': 'Éclairage 1 h.'},
                {'nom': 'Lampe Tactique', 'epoque_label': 'E2–E4', 'poids_livres': 0.5, 'prix_ce': 120, 'note': 'Dissipe l\'obscurité jusqu\'à 10 cases.'},
            ],
        },
        'Jumelles / Optiques Thermiques': {
            'comps': ['Observation'],
            'variantes': [
                {'nom': 'Jumelles optiques simples', 'epoque_label': 'E2–E4', 'poids_livres': 1.5, 'prix_ce': 400, 'note': '+1 Observation longue distance (de jour).'},
                {'nom': 'Optiques Thermiques', 'epoque_label': 'E2–E4', 'poids_livres': 1.5, 'prix_ce': 2500, 'note': '+1 Observation longue distance et vision nocturne/thermique.'},
            ],
        },
        'Tenue Thermique / Tente Pressurisée': {
            'comps': ['Survie / Nature'],
            'prix_max': 1500,
            'prix_lbl': '150 / 1 500 CE',
            'poids_lbl': '4–12 L',
            'refs': [{'label': 'Livre II §8.1 Climat', 'href': 'livre_2.html#sec-8-1'}],
            'variantes': [
                {'nom': 'Tenue Thermique isolante', 'epoque_label': 'E0–E4', 'poids_livres': 4.0, 'prix_ce': 150, 'note': 'Annule un palier de climat.'},
                {'nom': 'Tente Pressurisée (2 places)', 'epoque_label': 'E2–E4', 'poids_livres': 12.0, 'prix_ce': 1500, 'note': 'Abrite en milieu hostile ou dépressurisé.'},
            ],
        },
        'Radio Tactique (5 km)': {'comps': ['Observation']},
        'Menottes (Acier / Magnétiques)': {
            'comps': ['Subterfuge'],
            'etats': ['Ligoté'],
            'prix_max': 600,
            'prix_lbl': '150 / 600 CE',
            'variantes': [
                {'nom': 'Menottes en Acier', 'epoque_label': 'E1–E4', 'poids_livres': 0.6, 'prix_ce': 150, 'note': 'Évasion : Force ou Subterfuge Diff 6.'},
                {'nom': 'Menottes Magnétiques', 'epoque_label': 'E3–E4', 'poids_livres': 0.6, 'prix_ce': 600, 'note': 'Évasion : Force ou Subterfuge Diff 9.'},
            ],
        },
        'Sac à Dos Renforcé': {'comps': []},
    }

    for it in mec['materiel_terrain']:
        nom = it['nom']
        desc = table_descs.get(('materiel_terrain', nom), '')
        eps, emin, emax, elabel = parse_epochs(it['epoque'])
        ex = terrain_extra.get(nom, {})
        prix_ce = int(it['prix_ce'])
        prix_max = ex.get('prix_max')
        prix_lbl = ex.get('prix_lbl') or fmt_ce(prix_ce)
        poids = float(it['poids_livres'])
        poids_lbl = ex.get('poids_lbl') or f"{poids:g} L"
        add_obj({
            'id': slugify(nom),
            'nom': nom,
            'categorie': 'terrain-survie',
            'sous_categorie': 'Outillage & Matériel de Terrain',
            'famille': 'Outil / Terrain',
            'epoque_label': elabel,
            'epoques': eps,
            'epoque_min': emin,
            'epoque_max': emax,
            'dlph': None,
            'degats': 3 if 'Pièges' in nom else None,
            'degats_texte': '3D (piège)' if 'Pièges' in nom else None,
            'bonus_ec': 0,
            'perforante': 0,
            'zone_cases': 0,
            'poids_livres': poids,
            'poids_label': poids_lbl,
            'portee_cases': 10 if 'Torche' in nom else None,
            'portee_jet_cases': None,
            'portee_max_m': 5000 if 'Radio' in nom else None,
            'portee_label': '10P éclairage' if 'Torche' in nom else ('5 km' if 'Radio' in nom else None),
            'mains': 'Sac' if 'Sac à Dos' in nom else 'Outil',
            'munitions': None,
            'rechargement_pa': None,
            'prix_ce': prix_ce,
            'prix_max_ce': prix_max,
            'prix_label': prix_lbl,
            'protection': None,
            'protection_max': None,
            'absorption': None,
            'bonus_parade': None,
            'encombrement_pa': 0,
            'encombrement_max_pa': None,
            'soin_degats': None,
            'cout_pa': None,
            'duree': None,
            'competences': ex.get('comps', []),
            'proprietes': [],
            'etats_infliges': ex.get('etats', []),
            'etats_soignes': ex.get('soignes', []),
            'consommable': False,
            'emplacement': 'outil',
            'resume': desc,
            'equivalents': None,
            'notes_regles': [v['nom'] + ' (' + fmt_ce(v['prix_ce']) + ', ' + f"{v['poids_livres']:g} L" + ') : ' + v['note'] for v in ex.get('variantes', [])],
            'variantes': ex.get('variantes', []),
            'tags': [nom, 'Terrain', 'Survie', 'Outil'] + ex.get('comps', []),
            'source': ref_l8('sec-32-4', 'Livre VIII §32.4 Matériel de Terrain'),
            'refs': ex.get('refs', [{'label': 'Livre VIII §30.1 Portage', 'href': 'livre_8.html#sec-30-1'}]),
        })

    # ---- Budgets de départ (table donnees.budget_depart + mecaniques.budget_depart) ----
    budgets_depart = []
    for i, row in enumerate(don['budget_depart']['lignes']):
        mb = mec['budget_depart'][i]
        budgets_depart.append({
            'epoque': mb['epoque'],
            'nom': mb['nom'],
            'acces': strip_html(row[1]),
            'budget_ce': int(mb['budget_ce']),
            'dotation_type': strip_html(row[3]),
        })

    # ---- Presets de Kit (vérifiés <= budget_ce et valides à l'époque) ----
    presets_kit = [
        {
            'epoque': 'E0',
            'nom': 'Kit Éclaireur Primitive [E0]',
            'desc': 'Javelot de chasse, couteau/dague, targe de peau et vivres de base (570 / 600 CE).',
            'items': [
                {'id': 'javelot-de-chasse', 'qty': 1, 'equipe': True},
                {'id': 'couteau-dague', 'qty': 1, 'equipe': True},
                {'id': 'targe-bouclier-de-peau', 'qty': 1, 'equipe': True},
                {'id': 'ration-eau', 'qty': 1, 'equipe': False},
                {'id': 'poche-d-eau-1-l', 'qty': 1, 'equipe': False},
            ],
        },
        {
            'epoque': 'E1',
            'nom': 'Kit Homme d\'Armes Médiéval [E1]',
            'desc': 'Épée standard, écu de garnison, corde/grappin et bandage compressif (1 490 / 1 500 CE).',
            'items': [
                {'id': 'epee-metal-standard', 'qty': 1, 'equipe': True},
                {'id': 'ecu-bouclier-de-garnison', 'qty': 1, 'equipe': True},
                {'id': 'corde-20-m-grappin', 'qty': 1, 'equipe': False},
                {'id': 'bandage-compressif-garrot', 'qty': 1, 'equipe': False},
            ],
        },
        {
            'epoque': 'E2',
            'nom': 'Kit Agent Moderne [E2]',
            'desc': 'Pistolet léger + munitions, armure légère 1, masque filtrant, lampe tactique et trousse de secours (3 870 / 4 000 CE).',
            'items': [
                {'id': 'pistolet-leger-revolver', 'qty': 1, 'equipe': True},
                {'id': 'munitions-balistiques-standard-30', 'qty': 1, 'equipe': False},
                {'id': 'armure-legere-1', 'qty': 1, 'equipe': True},
                {'id': 'masque-filtrant', 'qty': 1, 'equipe': False},
                {'id': 'torche-lampe-tactique', 'qty': 1, 'equipe': False},
                {'id': 'trousse-de-premiers-soins-5-usages', 'qty': 1, 'equipe': False},
                {'id': 'bandage-compressif-garrot', 'qty': 1, 'equipe': False},
            ],
        },
        {
            'epoque': 'E3',
            'nom': 'Kit Patrouilleur Orbital [E3]',
            'desc': 'Épée laser standard 3D, armure légère 2 et seringue de soins −5 (11 900 / 12 000 CE).',
            'items': [
                {'id': 'epee-laser-standard-3d', 'qty': 1, 'equipe': True},
                {'id': 'armure-legere-2', 'qty': 1, 'equipe': True},
                {'id': 'seringue-de-soins-5-degats', 'qty': 1, 'equipe': False},
            ],
        },
        {
            'epoque': 'E4',
            'nom': 'Kit Opérateur Arcanotech [E4]',
            'desc': 'Pistolet tranquillisant, armure lourde 4, résonateur de nytharite, cellule standard et seringue −10 (29 700 / 30 000 CE).',
            'items': [
                {'id': 'pistolet-tranquillisant', 'qty': 1, 'equipe': True},
                {'id': 'armure-lourde-4', 'qty': 1, 'equipe': True},
                {'id': 'resonateur-focaliseur-nytharite', 'qty': 1, 'equipe': False},
                {'id': 'cellule-nytharite-standard', 'qty': 1, 'equipe': False},
                {'id': 'seringue-de-soins-10-degats', 'qty': 1, 'equipe': False},
            ],
        },
        {
            'epoque': 'E5',
            'nom': 'Kit Avant-Garde Démiurgique [E5]',
            'desc': 'Champ de Force Personnel 1 (absorbe 10 dégâts avant armure, 0 PA, 75 000 / 75 000 CE).',
            'items': [
                {'id': 'champ-de-force-personnel-1', 'qty': 1, 'equipe': True},
            ],
        },
    ]

    etats_objets = [
        {'nom': 'Parfait État', 'statut': 'Opérationnel', 'effet': 'Objet neuf ou réparé à 100 %. Rendement maximal. Revente à 50 % du neuf.'},
        {'nom': 'Endommagé', 'statut': 'Opérationnel', 'effet': 'A encaissé des impacts ou de l\'usure, conserve toutes ses fonctions nominales. Réparable sur le terrain avec une caisse à outils. Revente à 25 %.'},
        {'nom': 'Enrayé', 'statut': 'Hors-Service', 'effet': 'Inutilisable temporairement, déblocable sur le terrain pour 2 PA.'},
        {'nom': 'Détruit', 'statut': 'Hors-Service', 'effet': 'Inutilisable ; exige un atelier, 25 % du prix neuf en pièces (moitié prix pour un Vardien) et un jet d\'Artisanat/Ingénierie (Diff 5 E0–E2, 7 E3–E4, 9 E5). Revente à 10 %.'},
        {'nom': 'Anéanti', 'statut': 'Irrécupérable', 'effet': 'Totalement pulvérisé, irrécupérable et irréparable.'},
    ]
    # Vérifier présence dans livre_6 (garde-fou de cohérence)
    l6_blob = json.dumps(d6['mecaniques']['etats_objets'], ensure_ascii=False)
    for eo in etats_objets:
        if eo['nom'] not in l6_blob:
            raise ValueError(f"Etat objet manquant dans livre_6.json: {eo['nom']}")

    ver_path = os.path.join(RACINE, 'VERSION')
    ver = open(ver_path, encoding='utf-8').read().strip() if os.path.exists(ver_path) else '0.8.0'

    return {
        'titre': 'Appendice C : Compendium des Objets',
        'version': ver,
        'updated': '2026-11-23',
        'source_canon': 'data/livre_8.json',
        'regles_ref': {
            'code_dlph': mec['code_dlph'],
            'charge_portage': mec['charge_portage'],
            'budgets_depart': budgets_depart,
            'revente': {'neuf': 0.5, 'endommage': 0.25, 'detruit': 0.1},
            'reparation': {
                'pieces_ratio': 0.25,
                'vardien_ratio': 0.125,
                'diff_par_epoque': {'E0': 5, 'E1': 5, 'E2': 5, 'E3': 7, 'E4': 7, 'E5': 9},
                'enraye_pa': 2,
            },
            'etats_objets': etats_objets,
        },
        'categories': CATEGORIES,
        'proprietes': [
            {'id': pid, 'nom': nom, 'syntaxe': syn, 'effet': eff, 'tip': tip}
            for (pid, nom, syn, eff, tip) in PROP_DEFS
        ],
        'presets_kit': presets_kit,
        'objets': objets,
    }


def main():
    if len(sys.argv) != 2 or sys.argv[1] not in ('--build', '--check'):
        print('usage: build_appendice_objets.py (--build|--check)')
        return 2

    data = build_data()
    # Vérification interne rapide
    obj_map = {o['id']: o for o in data['objets']}
    budgets = {b['epoque']: b['budget_ce'] for b in data['regles_ref']['budgets_depart']}
    for p in data['presets_kit']:
        tot = 0
        for it in p['items']:
            o = obj_map.get(it['id'])
            if not o:
                raise ValueError(f"Preset {p['epoque']}: id inconnu {it['id']}")
            if p['epoque'] not in o['epoques']:
                raise ValueError(f"Preset {p['epoque']}: objet {it['id']} indisponible ({o['epoque_label']})")
            tot += o['prix_ce'] * it['qty']
        if tot > budgets[p['epoque']]:
            raise ValueError(f"Preset {p['epoque']}: depasse budget ({tot} > {budgets[p['epoque']]})")

    rendered = json.dumps(data, ensure_ascii=False, indent=1) + '\n'
    if sys.argv[1] == '--check':
        if not os.path.exists(PATH_OUT):
            print('OBSOLETE : data/appendice_objets.json manquant')
            return 1
        current = open(PATH_OUT, encoding='utf-8').read()
        if current != rendered:
            print('OBSOLETE : data/appendice_objets.json desynchronise avec data/livre_8.json')
            return 1
        print('OBJETS OK : %d objets synchronises avec livre_8.json' % len(data['objets']))
        return 0

    with open(PATH_OUT, 'w', encoding='utf-8') as f:
        f.write(rendered)
    print('GENERE : %s (%d objets, %d categories, %d proprietes, %d presets)' % (
        PATH_OUT, len(data['objets']), len(data['categories']),
        len(data['proprietes']), len(data['presets_kit'])
    ))
    return 0


if __name__ == '__main__':
    sys.exit(main())
