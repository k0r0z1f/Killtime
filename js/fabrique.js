/* ==========================================================================
   FABRIQUE DE TECHNIQUES — ATELIER GRAPHE (Livre III chap-14, Livre IX chap-40).
   Moteur pur FabriqueEngine.compute (châssis + 1-3 pièces,
   M1-M4/M7 par carte effet, M6 hybride auto, croisé ×2
   par carte, signature 7-8, plafond PA 6, incompatibilités, prérequis) + initFabrique()
   qui monte le graphe depuis data/livre_3.json (mecaniques.fabrique).
   Graphe : 1 ensemble (racine ◆) (châssis) + 1 à 3 pièces dans 3 logements.
   Modificateurs sur la carte effet (pas hors graphe) : Portée totale 1 à 3 cases
   (base 1 gratuite, +2 XP/case), Zone M3
   (O7 O20 O22 O23 O24 O25 O26 O27 O28 — O7 + zone
   refusé règle 8), Durée M4 (O9/O20 « durée cumulable », 2 crans max), M7 ×2 (toute carte).
   ========================================================================== */
(function () {
'use strict';

function toInt(v) {
    var n = parseInt(String(v).replace('−', '-'), 10);
    return isNaN(n) ? 0 : n;
}

/* Nombre : '3' -> {b:3,d:0} ; '+2' -> {b:0,d:2} (delta de palier). */
function fabNum(v) {
    var s = String(v == null ? '0' : v).trim().replace('−', '-');
    var m = s.match(/^\+(\d+)/);
    if (m) return { b: 0, d: parseInt(m[1], 10) };
    return { b: toInt(s), d: 0 };
}

/* Pièces à coût variable : comportement spécial par id. */
var KIND = {
    O1:  { t: 'n', max: 5, unit: 'crans', plural: [['dégât', 'dégâts']] },
    O8:  { t: 'n', max: 5, unit: 'points' },
    O9:  { t: 'n', max: 5, unit: 'points' },
    O13: { t: 'n', max: 3, unit: 'cases' },
    O15: { t: 'n', max: 5, unit: 'cibles', plural: [['dégât', 'dégâts']] },
    D8:  { t: 'n', max: 3, unit: 'malus' },
    S1:  { t: 'n', max: 3, unit: 'crans', plural: [['épreuve', 'épreuves']] },
    U7: { t: 'palier', opts: [
        { v: '5', label: '5 PV + purge (inclus)', dx: 0 },
        { v: '10', label: '10 PV (+2 XP)', dx: 2 },
        { v: '15', label: '15 PV + immunité 3 tours (+3 XP)', dx: 3 }
    ]},
    U11: { t: 'paliers', opts: [
        { v: 'p3', label: '3 ESS (+2 XP)', dx: 2, dpa: 0 },
        { v: 'p4', label: '4 ESS + 1 PA immédiat (+2 XP + 1 PA)', dx: 2, dpa: 1 },
        { v: 'p2x', label: '2×/combat (+2 XP)', dx: 2, dpa: 0 }
    ]},
    O28: { t: 'palier', opts: [
        { v: '2', label: '2 résiduels (inclus)', dx: 0 },
        { v: '3', label: '3 résiduels (+1 XP)', dx: 1 },
        { v: '4', label: '4 résiduels (+2 XP)', dx: 2 }
    ]}
};

/* Nom/texte affiché avec cumul : '+1 dégât' ×5 → '+5 dégâts'. */
function scaledPieceText(s, id, q) {
    var out = String(s == null ? '' : s);
    if ((q | 0) > 1) {
        var done = false;
        out = out.replace(/\+(\d+)/, function (m, n) {
            done = true;
            return '+' + (parseInt(n, 10) * (q | 0));
        });
        if (done) {
            var pl = (KIND[id] && KIND[id].plural) || [];
            pl.forEach(function (pair) {
                out = out.replace(new RegExp(pair[0] + '(?!s)', 'g'), pair[1]);
            });
        }
        /* "Repousse 1 case" x3 -> "Repousse 3 cases" (KIND unit: 'cases'). */
        var n = q | 0;
        out = out.split('d’1 case').join('de ' + n + ' cases');
        out = out.split("d'1 case").join('de ' + n + ' cases');
        out = out.split('1 case').join(n + ' cases');
    }
    return out;
}

/* Plus de palier remplaçant : objet conservé pour compatibilité. */
var REPLACE = {};

/* Compatibilité des modificateurs par carte effet (M1-M4/M7 sur la carte, pas hors graphe).
   Portée : base 1 case gratuite, +1 case (+2 XP, total 2) ou +2 cases (+4 XP, total 3 max).
   Zone M3 : dégâts, statuts ou repoussée (O7 O20 O22 O23 O24 O25 O26 O27 O28) ; O7 + zone
   refusé règle 8 (Absolus multi-cible, comme O7 + O15).
   Durée M4 : là où la table dit « durée cumulable » (O9, O20), 2 crans max.
   Hors-compétence M7 ×2 : toute carte (pièce d'une autre compétence). */
var MOD_RANGE_EXCL = { O1: 'dégât instantané mono-attaque, sans portée propre', O13: 'repoussée : la distance, c’est la quantité (x3)', O18: 'passif de critique, pas de portée', O20: 'statut délivré par la frappe, sans portée propre', O21: 'statut délivré par la frappe, sans portée propre', O22: 'statut délivré par la frappe, sans portée propre', O23: 'statut délivré par la frappe, sans portée propre', O24: 'statut délivré par la frappe, sans portée propre', O25: 'statut délivré par la frappe, sans portée propre', O26: 'statut délivré par la frappe, sans portée propre', O27: 'statut délivré par la frappe, sans portée propre', O28: 'saignement posé par la frappe, sans portée propre' };
var MOD_ZONE_IDS = { O7: 1, O20: 1, O22: 1, O23: 1, O24: 1, O25: 1, O26: 1, O27: 1, O28: 1 };
var MOD_DUR_IDS = { O9: 1, O20: 1 };
function modRangeOk(p) {
    if (!p) return { ok: false, reason: 'Carte inconnue.' };
    if (p.famille !== 'offensif') return { ok: false, reason: 'Portée : frappes à distance et ondes (offensif) uniquement.' };
    if (MOD_RANGE_EXCL[p.id]) return { ok: false, reason: 'Portée : ' + p.id + ' ' + MOD_RANGE_EXCL[p.id] + '.' };
    return { ok: true };
}
function modZoneOk(p) {
    if (!p) return { ok: false, reason: 'Carte inconnue.' };
    if (p.id === 'O15') return { ok: false, reason: 'Zone : O15 est déjà multi-cible.' };
    if (!MOD_ZONE_IDS[p.id]) return { ok: false, reason: 'Zone : dégâts, statuts ou repoussée uniquement (O7 O20 O22 O23 O24 O25 O26 O27 O28).' };
    return { ok: true };
}
function modDurOk(p) {
    if (!p) return { ok: false, reason: 'Carte inconnue.' };
    if (!MOD_DUR_IDS[p.id]) return { ok: false, reason: 'Durée : là où la table dit « durée cumulable » (O9, O20).' };
    return { ok: true };
}
function modCrossOk() { return { ok: true }; }

var FabriqueEngine = {
    KIND: KIND,
    REPLACE: REPLACE,

    pieceById: function (CAT, id) {
        for (var i = 0; i < CAT.pieces.length; i++)
            if (CAT.pieces[i].id === id) return CAT.pieces[i];
        return null;
    },

    compute: function (CAT, sel) {
        var errors = [], warnings = [], lines = [], fams = {};
        var auto = {};

        function P(id) {
            for (var i = 0; i < CAT.pieces.length; i++)
                if (CAT.pieces[i].id === id) return CAT.pieces[i];
            return null;
        }
        function C(id) {
            for (var i = 0; i < CAT.chassis.length; i++)
                if (CAT.chassis[i].id === id) return CAT.chassis[i];
            return null;
        }

        /* 1. Châssis (+ compétence entraînée liée, obligatoire). */
        var ch = sel.chassis ? C(sel.chassis) : null;
        if (!ch) {
            errors.push('Choisis un ensemble : il dit quand ta technique part.');
        } else {
            lines.push({ id: 'chassis', label: 'Ensemble — ' + ch.nom, xp: toInt(ch.cout_xp), pa: toInt(ch.cout_pa), kind: 'chassis' });
            if (!sel.ensSkill) errors.push('Lie ton ensemble (◆) à une compétence entraînée.');
        }

        /* 2. Pré-requis auto (chaînes de pièces, aucune pour l'instant). */
        var qty = sel.qty || {};
        function has(id) { return (qty[id] | 0) > 0 || !!auto[id]; }
        Object.keys(REPLACE).forEach(function (top) {
            if ((qty[top] | 0) > 0) REPLACE[top].forEach(function (pre) { auto[pre] = true; });
        });

        /* 3. Lignes de pièces. */
        var counted = 0;
        Object.keys(qty).forEach(function (id) {
            if ((qty[id] | 0) <= 0) return;
            var p = P(id);
            if (!p) return;
            counted++;
            fams[p.famille] = true;
        });
        Object.keys(auto).forEach(function (id) {
            var p = P(id);
            if (p) { counted++; fams[p.famille] = true; }
        });

        function chainCost(ids) {
            var xp = 0, pa = 0;
            ids.forEach(function (id) {
                var p = P(id);
                var xn = fabNum(p.cout_xp), pn = fabNum(p.cout_pa);
                xp += xn.b + xn.d; pa += pn.b + pn.d;
            });
            return { xp: xp, pa: pa };
        }

        var handled = {};
        Object.keys(qty).forEach(function (id) {
            var q = qty[id] | 0;
            if (q <= 0 || handled[id]) return;
            var p = P(id);
            if (!p) return;
            if (REPLACE[id]) {
                var chain = [id].concat(REPLACE[id]);
                var c = chainCost(chain);
                lines.push({ id: id, label: p.nom + ' (chaîne ' + chain.slice().reverse().join(' → ') + ', prérequis inclus)', xp: c.xp, pa: c.pa, kind: 'piece', fam: p.famille });
                chain.forEach(function (x) { handled[x] = true; });
                return;
            }
            handled[id] = true;
            if (id === 'U7') {
                var dx = 0;
                (KIND.U7.opts).forEach(function (o) { if (o.v === sel.u7) dx = o.dx; });
                lines.push({ id: id, label: p.nom + ' — palier ' + (sel.u7 || '5') + ' PV', xp: 2 + dx, pa: 2, kind: 'piece', fam: p.famille });
                return;
            }
            if (id === 'U11') {
                var ux = 2, up = 0, on = [];
                (KIND.U11.opts).forEach(function (o) {
                    if (sel.u11 && sel.u11[o.v]) { ux += o.dx; up += o.dpa; on.push(o.v); }
                });
                lines.push({ id: id, label: p.nom + (on.length ? ' (paliers ' + on.join(', ') + ')' : ' (base)'), xp: ux, pa: up, kind: 'piece', fam: p.famille });
                return;
            }
            if (id === 'O28') {
                var dx28 = 0;
                (KIND.O28.opts).forEach(function (o) { if (o.v === sel.o28) dx28 = o.dx; });
                lines.push({ id: id, label: p.nom + ' — ' + (sel.o28 || '2') + ' résiduels', xp: 2 + dx28, pa: 1, kind: 'piece', fam: p.famille });
                return;
            }
            var k = KIND[id];
            if (k && k.t === 'n') {
                var xn = fabNum(p.cout_xp), pn = fabNum(p.cout_pa);
                var nm = scaledPieceText(p.nom, id, q);
                lines.push({ id: id, label: nm + (nm === String(p.nom) ? ' ×' + q : ''), xp: (xn.b + xn.d) * q, pa: (pn.b + pn.d) * q, kind: 'piece', fam: p.famille });
                return;
            }
            var xe = fabNum(p.cout_xp), pe = fabNum(p.cout_pa);
            lines.push({ id: id, label: p.nom, xp: xe.b + xe.d, pa: pe.b + pe.d, kind: 'piece', fam: p.famille });
        });
        Object.keys(auto).forEach(function (id) {
            if (handled[id]) return;
            var p = P(id);
            var xn = fabNum(p.cout_xp), pn = fabNum(p.cout_pa);
            lines.push({ id: id, label: p.nom + ' (prérequis auto-inclus)', xp: xn.b + xn.d, pa: pn.b + pn.d, kind: 'piece', fam: p.famille, locked: true });
        });

        /* 4. Règles de comptage : 1 châssis + 1 à 3 pièces. */
        if (counted < 1) errors.push('1 pièce minimum (un ensemble seul ne suffit pas).');
        if (counted > 3) errors.push('3 pièces maximum : au-delà, ce sont deux techniques sœurs.');
        if ((has('D1') || has('D3')) === false && has('D8'))
            errors.push('Ignore un malus (D8) exige D1 ou D3.');
        if (has('U5'))
            warnings.push('Téléport 2 cases (U5) : prérequis narratif = Roulade ou esquive Δ≥2.');
        if (has('O18') && counted < 3)
            warnings.push('Critique élargi (O18) : prérequis = 3 pièces de la même arme.');
        if (has('O8') && has('O9'))
            warnings.push('Règle 5 : un seul bonus chiffré par jet — O8 et O9 ne se cumulent pas.');
        /* Incompatibilités. */
        var INCOMP = [['O7', 'O15'], ['D6', 'D1'], ['D6', 'D2'], ['D6', 'D3'], ['D6', 'D4']];
        INCOMP.forEach(function (pair) {
            if (has(pair[0]) && has(pair[1]))
                errors.push('Incompatible : ' + pair[0] + ' ⊕ ' + pair[1] + ' (règle 8).');
        });

        /* 5. Conditions étroites M5 (−1 XP, plancher 1 XP/pièce). */
        var narrow = sel.narrow | 0;
        var elig = lines.filter(function (l) { return l.kind === 'piece' && l.xp > 1; })
                        .sort(function (a, b) { return b.xp - a.xp; });
        if (narrow > elig.length)
            errors.push('Trop de conditions étroites : ' + elig.length + ' pièce(s) éligible(s) max (1 XP plancher).');
        else {
            for (var i = 0; i < narrow; i++) {
                elig[i].xp = Math.max(1, elig[i].xp - 1);
                elig[i].narrow = true;
            }
        }

        /* 6. Modificateurs par carte effet (portée/zone/durée/M7 sur la carte, pas hors graphe).
           sel.mods = { [id]: { range: 0|2|4, zone: bool, dur: 0..2, cross: bool } }.
           Sans sel.mods (vieux tests) : repli global historique. */
        var crossedIds = [];
        if (sel.mods) {
            Object.keys(qty).forEach(function (id) {
                if ((qty[id] | 0) <= 0) return;
                var p = P(id);
                if (!p) return;
                var m = sel.mods[id] || {};
                var rg = m.range | 0;
                if (rg < 0 || rg > 2) {
                    errors.push('Portée de ' + id + ' : base 1, 3 max (+2 XP/case).');
                    rg = 0;
                }
                if (rg) {
                    var rok = modRangeOk(p);
                    if (!rok.ok) errors.push('Portée ' + (1 + rg) + ' cases incompatible avec ' + id + ' (' + p.nom + ') : ' + rok.reason);
                    else lines.push({ id: 'M1', label: 'Portée ' + (1 + rg) + ' cases — ' + id + ' ' + p.nom, xp: rg * 2, pa: 0, kind: 'mod', piece: id });
                }
                if (m.zone) {
                    var zok = modZoneOk(p);
                    if (!zok.ok) errors.push('Zone incompatible avec ' + id + ' (' + p.nom + ') : ' + zok.reason);
                    else if (id === 'O7') errors.push('Incompatible : O7 + zone (règle 8, Absolus jamais multi-cible).');
                    else lines.push({ id: 'M3', label: 'Forme de zone — ' + id + ' ' + p.nom, xp: 2, pa: 1, kind: 'mod', piece: id });
                }
                var du = Math.max(0, Math.min(2, m.dur | 0));
                if ((m.dur | 0) !== du) errors.push('Durée de ' + id + ' : 0 à 2 crans max.');
                if (du > 0) {
                    var dok = modDurOk(p);
                    if (!dok.ok) errors.push('Durée +' + du + ' incompatible avec ' + id + ' (' + p.nom + ') : ' + dok.reason);
                    else lines.push({ id: 'M4', label: 'Durée +' + du + ' tour(s) — ' + id + ' ' + p.nom, xp: du, pa: du, kind: 'mod', piece: id });
                }
                if (m.cross) crossedIds.push(id);
            });
            /* M7 par carte : la carte (pièce + ses modificateurs) paie double. */
            crossedIds.forEach(function (cid) {
                lines.forEach(function (l) {
                    if ((l.kind === 'piece' && l.id === cid) || (l.kind === 'mod' && l.piece === cid)) {
                        l.xp = l.xp * 2;
                        l.crossed = true;
                    }
                });
            });
            /* Sur critique liée : −1 XP par cumul (M5) — O1, O8, O13, O15. */
            ['O1', 'O8', 'O13', 'O15'].forEach(function (cid) {
                var lk = sel.plink && sel.plink[cid];
                if (lk && lk.t === 'cond' && lk.id === 'critique' && (qty[cid] | 0) > 0) {
                    lines.forEach(function (l) {
                        if (l.kind === 'piece' && l.id === cid) {
                            l.xp = Math.max(1, l.xp - (qty[cid] | 0));
                            l.narrow = (qty[cid] | 0);
                        }
                    });
                }
            });
        } else {
            if (sel.range === 2) lines.push({ id: 'M1', label: 'Portée +2 cases', xp: 1, pa: 0, kind: 'mod' });
            if (sel.range === 4) lines.push({ id: 'M2', label: 'Portée +4 cases', xp: 2, pa: 0, kind: 'mod' });
            if (sel.zone) lines.push({ id: 'M3', label: 'Forme de zone', xp: 2, pa: 1, kind: 'mod' });
            if ((sel.dur | 0) > 0) lines.push({ id: 'M4', label: 'Durée +' + sel.dur + ' tour(s)', xp: sel.dur | 0, pa: sel.dur | 0, kind: 'mod' });
        }

        /* 7. Hybride auto (M6). */
        var famKeys = Object.keys(fams);
        if (famKeys.length > 1)
            lines.push({ id: 'M6', label: 'Hybride (' + famKeys.join(' + ') + ')', xp: 1, pa: 0, kind: 'mod' });

        /* 8. Totaux. */
        var xp = 0, pa = 0;
        lines.forEach(function (l) { xp += l.xp; pa += l.pa; });
        var crossed = crossedIds.length > 0;
        if (!sel.mods && sel.cross) { xp = xp * 2; crossed = true; }

        var status = 'standard';
        if (xp >= 7) {
            status = 'signature';
            warnings.push('Signature (7 XP et plus) : une seule par personnage, accord MJ obligatoire.');
        }
        if (pa > 6) errors.push('Activation ' + pa + ' PA > 6 : ça doit tenir dans un tour.');
        if (ch && ch.id === 'C0' && pa > 0)
            errors.push('Passif conditionnel : 0 PA — ni pièce ni finition à activation (pas de jet, pas d’activation).');
        if (ch && ch.id === 'C0' && counted > 0 && !(sel.conds && sel.conds.length))
            errors.push('Passif conditionnel : au moins 1 prérequis (carte ci-dessus).');
        if (status !== 'signature' && xp >= 4 && xp <= 6) status = 'standard';

        return { xp: xp, pa: pa, lines: lines, errors: errors, warnings: warnings,
                 familles: famKeys, crossed: crossed,
                 status: errors.length ? 'invalide' : status };
    }
};

/* Nom automatique live : fragments par carte + suffixes de modificateurs.
   Pur : suggestName(CAT, sel) -> string. sel = {chassis, qty, order?, mods?, plink?, u7?}. */
var NAME_BASE = {
    O1: 'Frappe Lourde',
    O7: 'Lame Absolue', O8: 'Visée Sûre', O9: 'Élan Précis',
    O13: 'Balayage', O15: 'Frappe en Écho',
    O18: 'Œil Critique',
    O20: 'Entrave', O21: 'Étourdissement', O22: 'Feinte',
    O23: 'Fauchage', O24: 'Chaîne', O25: 'Assommoir',
    O26: 'Tétanie', O27: 'Panique', O28: 'Hémorragie',
    D1: 'Lame Miroir', D2: 'Rempart', D3: 'Peau de Fer', D4: 'Peau de Pierre',
    D5: 'Immunité', D6: 'Garde Absolue', D7: 'Veille', D8: 'Assise',
    U1: 'Geste Économe', U2: 'Second Souffle', U3: 'Élan Collectif',
    U4: 'Pas Glissé', U5: 'Pas de Brume', U6: 'Foulée Sûre',
    U7: 'Suture', U8: 'Œil Vital', U9: 'Regard Clair', U10: 'Réflexe Prêt',
    U11: 'Souffle Maîtrisé', U12: 'Effort Forcé',
    S1: 'Voix Assurée', S2: 'Intimidation', S3: 'Parole de Trêve',
    S4: 'Marchandage', S5: 'Lame Silencieuse', S6: 'Ordre Bref'
};
var QTY_WORD = { 2: 'Double', 3: 'Triple', 4: 'Quadruple', 5: 'Quintuple' };
function nameHas(s, words) {
    var n = String(s || '').toLowerCase();
    return words.some(function (w) { return n.indexOf(w) >= 0; });
}
function suggestName(CAT, sel) {
    sel = sel || {};
    var qty = sel.qty || {};
    var known = null;
    if (CAT && CAT.pieces) {
        known = {};
        CAT.pieces.forEach(function (p) { known[p.id] = true; });
    }
    var tops = (sel.order && sel.order.length ? sel.order : Object.keys(qty))
        .filter(function (id) { return (qty[id] | 0) > 0 && (!known || known[id]); });
    if (!tops.length) return 'Technique Maison';
    var bases = tops.map(function (id) {
        var b = NAME_BASE[id] || id;
        if (id === 'U7') {
            if (sel.u7 === '10') b = 'Suture Profonde';
            else if (sel.u7 === '15') b = 'Suture Totale';
            else b = 'Suture Légère';
        }
        var q = qty[id] | 0;
        if (q > 1 && QTY_WORD[q]) b = QTY_WORD[q] + ' ' + b;
        return b;
    });
    var name;
    if (bases.length === 1) name = bases[0];
    else if (bases.length === 2) name = bases[0] + ' et ' + bases[1];
    else name = bases[0] + ', ' + bases[1] + ' et ' + bases[2];
    /* Ensemble : Réaction et Opportunité signent le nom, sauf si la carte le dit déjà. */
    if (sel.chassis === 'C3' &&
        !nameHas(name, ['riposte', 'miroir', 'contre', 'revers']))
        name = 'Riposte : ' + name;
    if (sel.chassis === 'C4' && !nameHas(name, ['éclair', 'sursaut', 'pas', 'bond', 'élan', 'réflexe']))
        name = 'Éclair : ' + name;
    /* Modificateurs posés sur les cartes : un suffixe chacun, une seule fois. */
    var mods = sel.mods || {};
    var hasRange = false, hasZone = false, hasDur = false, hasCross = false;
    tops.forEach(function (id) {
        var m = mods[id];
        if (!m) return;
        if ((m.range | 0) > 0) hasRange = true;
        if (m.zone) hasZone = true;
        if ((m.dur | 0) > 0) hasDur = true;
        if (m.cross) hasCross = true;
    });
    if (hasRange) name += ' à Distance';
    if (hasZone && !nameHas(name, ['tourbillon', 'zone', 'éclat'])) name += ' en Tourbillon';
    if (hasDur) name += ' Tenace';
    var plink = sel.plink || {};
    var hasCrit = tops.some(function (id) {
        return (id === 'O1' || id === 'O13' || id === 'O15') &&
            plink[id] && plink[id].t === 'cond' && plink[id].id === 'critique';
    });
    if (hasCrit) name += ' sur Critique';
    if (hasCross) name += ' — Hors-compétence';
    return name;
}

/* Description narrative automatique : 2e personne, phrases courtes,
   assemblée depuis l'ensemble + le texte des cartes + les modifs + les conditions.
   Pur : suggestDesc(CAT, sel) -> string ('' si rien à décrire). */var DESC_CHASSIS = {
    C0: 'Toujours actif quand la condition tient.',
    C2: 'Pendant ton tour, tu agis.',
    C3: 'Hors de ton tour, 1/round.',
    C4: '0 PA, 1/round, quand le déclencheur tombe.'
};
var DESC_U7 = { '5': ' (5 PV + purge 1 état).', '10': ' (10 PV + purge 1 état).', '15': ' (15 PV + immunité 3 tours).' };
var DESC_U11 = { p3: '3 ESS', p4: '4 ESS + 1 PA', p2x: '2×/combat' };
function suggestDesc(CAT, sel) {
    sel = sel || {};
    var qty = sel.qty || {};
    var tops = (sel.order && sel.order.length ? sel.order : Object.keys(qty))
        .filter(function (id) { return (qty[id] | 0) > 0; });
    if (!sel.chassis && !tops.length) return '';
    var out = [];
    if (sel.chassis && DESC_CHASSIS[sel.chassis]) out.push(DESC_CHASSIS[sel.chassis]);
    var pieces = (CAT && CAT.pieces) || [];
    function pieceById(id) {
        for (var i = 0; i < pieces.length; i++)
            if (pieces[i].id === id) return pieces[i];
        return null;
    }
    var mods = sel.mods || {};
    tops.forEach(function (id) {
        var p = pieceById(id);
        if (!p) return;
        var s = scaledPieceText(p.texte || p.nom, id, qty[id] | 0);
        if (id === 'U7') s = s.replace(/\.\s*$/, '') + (DESC_U7[sel.u7] || DESC_U7['5']);
        if (id === 'O28') s = s.replace(/2 à 4/, sel.o28 || '2');
        if (id === 'U11') {
            var subs = Object.keys(sel.u11 || {}).filter(function (v) { return sel.u11[v] && DESC_U11[v]; });
            if (subs.length) s = s.replace(/\.\s*$/, '') + ' (paliers : ' + subs.map(function (v) { return DESC_U11[v]; }).join(', ') + ').';
        }
        out.push(s);
        var m = mods[id];
        if (m) {
            if ((m.range | 0) > 0) out.push('Portée ' + ((m.range | 0) + 1) + ' cases.');
            if (m.zone) out.push('En cône, en ligne ou en rayon +1.');
            if ((m.dur | 0) > 0) out.push('L’effet tient +' + (m.dur | 0) + ' tour(s).');
        }
    });
    var conds = sel.conds || sel.conditions || [];
    var cnames = conds.map(function (cid) {
        var cd = (typeof conditionById === 'function') ? conditionById(cid) : null;
        return cd ? cd.nom : cid;
    }).filter(Boolean);
    if (cnames.length) out.push('Condition : ' + cnames.join(', ') + '.');
    return out.join(' ');
}

/* Liens mécaniques façon Livres I-III : les termes de règles affichés par
   l'atelier deviennent des ancres rule-ref (tooltip via bindCodexTips).
   Paires (href, tip) reprises verbatim des Livres I-III — rien d'inventé.
   Pur : fabLinkText(s) -> html (échappé + lié, une seule passe). */
var FAB_REFS = [
    { k: 'encaissement', href: 'livre_6.html#sec-24-2', tip: 'Encaissement = CON × 2 : seuil comparé aux dégâts du coup (§24.2).' },
    { k: 'essoufflement', href: 'livre_1.html#sec-4-2', tip: 'Essoufflement : 1 point = des PA d’urgence ; ne part pas tout seul, action dédiée pour souffler (§4.2).' },
    { k: 'ralentie', href: 'livre_6.html#sec-25-1', tip: 'Ralenti : toute action coûte le DOUBLE de PA (§25.1).' },
    { k: 'ralenti', href: 'livre_6.html#sec-25-1', tip: 'Ralenti : toute action coûte le DOUBLE de PA (§25.1).' },
    { k: 'à terre', href: 'livre_6.html#sec-25-1', tip: 'À Terre : −1 ec en attaque, −2 en défense. Déplacement +1 PA/case (1 case = 2 PA). Se relever coûte des PA.' },
    { k: 'déstabilisée', href: 'livre_6.html#sec-25-1', tip: 'Déstabilisé : −2 ec aux épreuves, déplacement rampant (1 case = 2 PA).' },
    { k: 'déstabilisé', href: 'livre_6.html#sec-25-1', tip: 'Déstabilisé : −2 ec aux épreuves, déplacement rampant (1 case = 2 PA).' },
    { k: 'étourdie', href: 'livre_6.html#sec-25-1', tip: 'Étourdi : −1 ec sur tous les jets.' },
    { k: 'étourdi', href: 'livre_6.html#sec-25-1', tip: 'Étourdi : −1 ec sur tous les jets.' },
    { k: 'absolus', href: 'livre_6.html#chap-24', tip: 'Dégâts Absolus : ignorent armure et Constitution, directs sur la jauge vitale (§24.1).' },
    { k: 'absolue', href: 'livre_6.html#chap-24', tip: 'Dégâts Absolus : ignorent armure et Constitution, directs sur la jauge vitale (§24.1).' },
    { k: 'absolu', href: 'livre_6.html#chap-24', tip: 'Dégâts Absolus : ignorent armure et Constitution, directs sur la jauge vitale (§24.1).' },
    { k: 'sonnée', href: 'livre_6.html#sec-25-1', tip: 'Sonné : aucune action. La créature tombe immédiatement à terre.' },
    { k: 'sonné', href: 'livre_6.html#sec-25-1', tip: 'Sonné : aucune action. La créature tombe immédiatement à terre.' },
    { k: 'immobilisée', href: 'livre_6.html#sec-25-1', tip: 'Immobilisé : impossible de faire la moindre action de mouvement (§25.1).' },
    { k: 'immobilisé', href: 'livre_6.html#sec-25-1', tip: 'Immobilisé : impossible de faire la moindre action de mouvement (§25.1).' },
    { k: 'paralysée', href: 'livre_6.html#sec-25-1', tip: 'Paralysé : aucune action ni réaction, incapacité totale (§25.1).' },
    { k: 'paralysé', href: 'livre_6.html#sec-25-1', tip: 'Paralysé : aucune action ni réaction, incapacité totale (§25.1).' },
    { k: 'agonisante', href: 'livre_6.html#sec-25-1', tip: 'Agonisant : aucune action raisonnée, fuite de 6 cases (1d8). Persiste jusqu’à stabilisation médicale.' },
    { k: 'agonisant', href: 'livre_6.html#sec-25-1', tip: 'Agonisant : aucune action raisonnée, fuite de 6 cases (1d8). Persiste jusqu’à stabilisation médicale.' },
    { k: 'saignement', href: 'livre_6.html#sec-25-1bis', tip: 'Saignement : PV directs en début de tour, puis −1/tour. Tout soin le stoppe (§25.1bis).' },
    { k: 'saigne', href: 'livre_6.html#sec-25-1bis', tip: 'Saignement : PV directs en début de tour, puis −1/tour. Tout soin le stoppe (§25.1bis).' },
    { k: 'résiduels', href: 'livre_6.html#sec-25-1bis', tip: 'Saignement : PV directs en début de tour, puis −1/tour. Tout soin le stoppe (§25.1bis).' },
    { k: 'résiduel', href: 'livre_6.html#sec-25-1bis', tip: 'Saignement : PV directs en début de tour, puis −1/tour. Tout soin le stoppe (§25.1bis).' },
    { k: 'esquive', href: 'livre_3.html#chap-11', tip: 'Esquive : éviter un coup au dernier moment (AGI/RAP/INS, §11.2).' },
    { k: 'critique', href: 'livre_2.html#chap-8', tip: 'Critique : ton dé affiche son max (6 sur d6, 8 sur d8). Second jet sur la table (§8.1).' },
    { k: 'pa', href: 'livre_1.html#chap-4', tip: 'PA : Max(Agi,Int) + Rap + Min. Total = Tirage + PA − malus, sans bonus de carac (§4).' },
    { k: 'xp', href: 'livre_1.html#sec-5-3', tip: 'Spécialisation : 5 XP de la bonne banque ou du libre, au tarif normal (§5.3).' }
];
var FAB_TERM_RE = /Encaissement|Essoufflement|essoufflement|Ralentie|Ralenti|À Terre|à terre|Déstabilisée|Déstabilisé|Étourdie|Étourdi|étourdie|étourdi|Sonnée|Sonné|sonnée|sonné|Immobilisée|Immobilisé|immobilisée|immobilisé|Paralysée|Paralysé|paralysée|paralysé|Agonisante|Agonisant|agonisante|agonisant|Saignement|saignement|Saigne|saigne|Résiduels|Résiduel|résiduels|résiduel|Absolus|Absolue|Absolu|absolus|absolue|absolu|Esquive|esquive|Critique|critique|\bPA\b|\bXP\b/g;
function fabRefFor(m) {
    var k = String(m).toLowerCase();
    for (var i = 0; i < FAB_REFS.length; i++)
        if (FAB_REFS[i].k === k) return FAB_REFS[i];
    return null;
}
function escHtml(s) {
    return String(s == null ? '' : s)
        .replace(/&/g, '&amp;').replace(/</g, '&lt;')
        .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
function fabLinkText(s) {
    return escHtml(s).replace(FAB_TERM_RE, function (m) {
        var r = fabRefFor(m);
        if (!r) return m;
        return '<a class="rule-ref" href="' + r.href + '" data-tip="' + r.tip.replace(/"/g, '&quot;') + '">' + m + '</a>';
    });
}
/* Lie les nœuds texte d'un périmètre (hors contrôles et liens existants),
   puis attache les tooltips. Idempotent : relançable à chaque rendu. */
function linkifyFab(scope) {
    if (!scope || !scope.querySelectorAll) return;
    var walker = document.createTreeWalker(scope, NodeFilter.SHOW_TEXT, null, false);
    var nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);
    nodes.forEach(function (tn) {
        var p = tn.parentNode;
        if (!p || !p.replaceChild) return;
        if (p.closest && p.closest('a,button,input,select,textarea')) return;
        var s = tn.nodeValue;
        if (!s) return;
        FAB_TERM_RE.lastIndex = 0;
        if (!FAB_TERM_RE.test(s)) return;
        FAB_TERM_RE.lastIndex = 0;
        var html = fabLinkText(s);
        var tmp = document.createElement('span');
        tmp.innerHTML = html;
        var frag = document.createDocumentFragment();
        while (tmp.firstChild) frag.appendChild(tmp.firstChild);
        p.replaceChild(frag, tn);
    });
    if (window.bindCodexTips) window.bindCodexTips(scope);
}

/* ---------------------------------------------------------------- PUZZLE */
// Chaînes de pièces à prérequis inclus (aucune pour l'instant).
var CHAINS = {};
var CHAIN_KEY = {};
var INCOMP = [['O7', 'O15'], ['D6', 'D1'], ['D6', 'D2'], ['D6', 'D3'], ['D6', 'D4']];
var FAM_LABEL = { offensif: 'Offensif', defensif: 'Défensif', utilitaire: 'Utilitaire', social: 'Social' };
var SAVE_KEY = 'fabrique-puzzle-v1';

function chainOf(id) { return CHAINS[id] || [id]; }
function spanOf(id) { return chainOf(id).length; }
/* Prérequis-conditions : cartes à part, hors logements, hors monnaie (0 XP). */
var CONDITIONS = [
    { id: 'contact', nom: 'Au contact', texte: 'Ça ne marche qu\u2019au contact.' },
    { id: 'arme', nom: 'Arme liée en main', texte: 'Ton arme liée est en main.' },
    { id: 'immobile', nom: 'Immobile', texte: 'Tu ne te déplaces pas ce tour.' },
    { id: 'critique', nom: 'Sur critique', texte: 'Seulement sur un critique.' },
    { id: 'delta', nom: 'Si Δ≥2', texte: 'Seulement si tu gagnes de 2 ou plus.' },
    { id: 'fois', nom: '1/combat', texte: 'Une seule fois par combat.' },
    { id: 'essouf', nom: 'Avec essoufflement', texte: 'Tu prends 1 essoufflement.' },
    { id: 'compet', nom: 'Compétence entraînée', texte: 'Tu as l\u2019entraînement requis.' }
];
/* Compétences de base (Livre III, arbres FOR/AGI/CON/INT-ÉRU/CHA/INS-SAG). */
var SKILLS = [
    ['FOR', ['Mains Nues', "Maniement d'Arme", 'Athlétisme', 'Défense Corporelle']],
    ['AGI', ['Acrobatie', 'Discrétion', 'Esquive', 'Armes Perçantes & Fines', 'Ballistique / Tir', 'Conduite & Pilotage', 'Subterfuge Manuel']],
    ['CON', ['Endurance Physique', 'Cardio', 'Système Immunitaire']],
    ['INT-ÉRU', ['Académie', 'Premiers Soins', 'Médecine Avancée', 'Ingénierie & Arcanotech', 'Penser / Stratégie']],
    ['CHA', ['Communication', 'Intimidation', 'Leadership']],
    ['INS-SAG', ['Observation', 'Écoute', 'Intuition', 'Nature & Survie', 'Artisanat / Connaissance']]
];
var DEFAULT_SKILL = "Maniement d'Arme";
function conditionById(id) {
    for (var i = 0; i < CONDITIONS.length; i++)
        if (CONDITIONS[i].id === id) return CONDITIONS[i];
    return null;
}
/* PA d'activation d'une pièce (base). U11 : selon la variante, gérée dans le menu du nœud. */
function piecePA(p) {
    if (!p || p.id === 'U11') return 0;
    var pn = fabNum(p.cout_pa);
    return pn.b + pn.d;
}
/* IDs de pièces cités dans les prérequis ('D1 ou D3' → D1, D3 ; narratif → rien). */
function prereqPieceIds(CAT, p) {
    var ids = [];
    (p.prerequis || []).forEach(function (s) {
        var m = String(s).match(/[A-Z]+\d+/g);
        (m || []).forEach(function (id) {
            var found = null;
            for (var i = 0; i < CAT.pieces.length; i++)
                if (CAT.pieces[i].id === id) found = CAT.pieces[i];
            if (found && ids.indexOf(id) < 0) ids.push(id);
        });
    });
    return ids;
}

function normSearch(s) {
    var n = String(s == null ? '' : s).toLowerCase();
    if (n.normalize) n = n.normalize('NFD').replace(/[̀-ͯ]/g, '');
    return n;
}

function initFabrique() {
    var graphEl = document.getElementById('fabGraph');
    if (!graphEl) return;
    var CAT = window.CodexData && window.CodexData.l3 &&
              window.CodexData.l3.mecaniques && window.CodexData.l3.mecaniques.fabrique;
    if (!CAT) {
        graphEl.innerHTML = '<div class="callout warning"><div class="callout-body">' +
            'Données de la Fabrique introuvables (data/livre_3.json). Ouvre cette page via le site, pas en fichier seul.' +
            '</div></div>';
        return;
    }

    /* --- État : source unique, le DOM ne fait que l'afficher. --- */
    var state = { chassis: null, ensSkill: '', qty: {}, order: [], conds: [], plink: {}, condSkill: "Maniement d'Arme",
                  u7: '5', u11: {}, o28: '2', mods: {} };
    var menu = null; /* {kind:'slot'|'piece'|'root', id, search} */
    var lastAuto = ''; /* dernier nom automatique proposé : le champ se remplit seul tant qu'il le suit */
    var lastDesc = ''; /* idem pour la description narrative */
    var nodesEl = document.getElementById('fabNodes');
    var wiresEl = document.getElementById('fabWires');
    var ctxEl = document.getElementById('fabCtx');

    function esc(s) { return String(s == null ? '' : s); }
    function el(tag, cls, html) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (html != null) e.innerHTML = html;
        return e;
    }
    function P(id) {
        for (var i = 0; i < CAT.pieces.length; i++)
            if (CAT.pieces[i].id === id) return CAT.pieces[i];
        return null;
    }
    function C(id) {
        for (var i = 0; i < CAT.chassis.length; i++)
            if (CAT.chassis[i].id === id) return CAT.chassis[i];
        return null;
    }
    function toast(m) { if (window.showToast) window.showToast(m); }

    /* Présence réelle sur le graphe (tops + étages auto-inclus). */
    function present(id) {
        if ((state.qty[id] | 0) > 0) return true;
        for (var i = 0; i < state.order.length; i++) {
            var ch = chainOf(state.order[i]);
            if (ch.indexOf(id) >= 0) return true;
        }
        return false;
    }
    function placedSpan() {
        var n = 0;
        state.order.forEach(function (id) { n += spanOf(id); });
        return n;
    }
    function incompWith(id) {
        for (var i = 0; i < INCOMP.length; i++) {
            var a = INCOMP[i][0], b = INCOMP[i][1];
            if (id === a && present(b)) return b;
            if (id === b && present(a)) return a;
        }
        return null;
    }
    /* Prérequis-pièces manquants, par groupes d'alternatives (D1 ou D3 = un seul suffit). */
    function missingGroups(p) {
        var groups = [];
        (p.prerequis || []).forEach(function (s) {
            var ids = [];
            var m = String(s).match(/[A-Z]+\d+/g) || [];
            (m || []).forEach(function (id) {
                if (P(id) && ids.indexOf(id) < 0) ids.push(id);
            });
            if (ids.length && !ids.some(present)) groups.push(ids);
        });
        return groups;
    }

    /* Pourquoi ce choix n'est pas possible ici (ou avec un avertissement). */
    function lockInfo(p) {
        var id = p.id;
        var k = KIND[id];
        var q = state.qty[id] | 0;
        if (!state.chassis) return { lock: true, reason: 'Choisis d\u2019abord un ensemble (◆).' };
        if (state.chassis === 'C0' && piecePA(p) > 0)
            return { lock: true, reason: 'Ne se branche pas sur un ensemble passif (0 PA, pas d\u2019activation).' };
        if (q > 0 && !(k && k.t === 'n')) {
            if (CHAIN_KEY[id]) {
                var cur = null;
                state.order.forEach(function (t) { if (CHAIN_KEY[t] === CHAIN_KEY[id]) cur = t; });
                if (cur && spanOf(cur) >= spanOf(id))
                    return { lock: true, reason: 'Déjà incluse dans ' + cur + '.' };
            } else {
                return { lock: true, reason: 'Déjà sur le graphe (pièce unique).' };
            }
        }
        if (k && k.t === 'n' && q >= k.max)
            return { lock: true, reason: 'Maximum ×' + k.max + ' atteint.' };
        if (id === 'D8' && !present('D1') && !present('D3'))
            return { lock: false, soft: 'Demande D1 ou D3 : choisis la pièce, le menu proposera.' };
        var bad = incompWith(id);
        if (bad) return { lock: true, reason: 'Refuse ' + bad + ' (règle 8).' };
        var need = spanOf(id);
        if (CHAIN_KEY[id]) {
            state.order.forEach(function (t) {
                if (CHAIN_KEY[t] === CHAIN_KEY[id]) need = spanOf(id) - spanOf(t);
            });
            if (need < 0) need = 0;
        }
        if (placedSpan() + need > 3)
            return { lock: true, reason: 'Plus de logement libre (3 max).' };
        if (id === 'O18' && placedSpan() + need < 3)
            return { lock: false, soft: 'Demande 3 pièces de la même arme.' };
        if (id === 'U5')
            return { lock: false, soft: 'Prérequis narratif : Roulade ou esquive Δ≥2.' };
        return { lock: false };
    }

    function doPlace(id) {
        var p = P(id);
        if (!p) return false;
        var info = lockInfo(p);
        if (info.lock) { toast(info.reason); return false; }
        var k = KIND[id];
        if (CHAIN_KEY[id]) {
            var idx = -1;
            state.order.forEach(function (t, i) { if (CHAIN_KEY[t] === CHAIN_KEY[id]) idx = i; });
            if (idx >= 0) {
                var old = state.order[idx];
                if (spanOf(old) >= spanOf(id)) { toast('Déjà incluse.'); return false; }
                delete state.qty[old];
                if (state.mods[old]) { state.mods[id] = state.mods[old]; delete state.mods[old]; }
                state.order[idx] = id;
                state.qty[id] = 1;
            } else {
                state.order.push(id);
                state.qty[id] = 1;
            }
        } else if (k && k.t === 'n') {
            if ((state.qty[id] | 0) === 0) state.order.push(id);
            state.qty[id] = (state.qty[id] | 0) + 1;
        } else {
            state.order.push(id);
            state.qty[id] = 1;
        }
        return true;
    }
    /* Prérequis encore manquants pour cet effet : pièces-piliers et, sur passif
       sans condition, les 8 cartes conditions. */
    function needFor(id) {
        var p = P(id);
        if (!p) return [];
        var out = [];
        missingGroups(p).forEach(function (g) {
            g.forEach(function (mid) { out.push({ t: 'piece', id: mid }); });
        });
        if (state.chassis === 'C0' && state.conds.length === 0) {
            CONDITIONS.forEach(function (cd) { out.push({ t: 'cond', id: cd.id }); });
        }
        return out;
    }
    function firstNeed() {
        var q = menu ? normSearch(menu.search) : '';
        var list = (menu && menu.id) ? needFor(menu.id) : [];
        for (var i = 0; i < list.length; i++) {
            var o = list[i];
            var nm = o.t === 'cond' ? (conditionById(o.id).nom + ' ' + o.id) : (P(o.id).nom + ' ' + o.id);
            if (q && normSearch(nm).indexOf(q) < 0) continue;
            if (o.t === 'piece') {
                var mp = P(o.id);
                if (!mp || lockInfo(mp).lock) continue;
            }
            return o;
        }
        return null;
    }
    function addPiece(id, keepOpen) {
        if (!doPlace(id)) return false;
        if (!keepOpen) {
            var needs = needFor(id);
            if (needs.length) openMenu('need', id);
            else closeMenu();
        }
        render();
        return true;
    }
    function removePiece(id) {
        delete state.qty[id];
        delete state.mods[id];
        delete state.plink[id];
        Object.keys(state.plink).forEach(function (eid) {
            if (state.plink[eid].t === 'piece' && state.plink[eid].id === id) delete state.plink[eid];
        });
        state.order = state.order.filter(function (t) { return t !== id; });
        closeMenu();
        render();
    }
    function setChassis(id) {
        fullReset(id === state.chassis ? null : id);
        if (id) toast('Ensemble ' + id + ' : graphe reconstruit.');
    }
    /* Remise à zéro complète (nom/description conservés) : changer d'ensemble refait le graphe. */
    function fullReset(chassisId) {
        state.chassis = chassisId || null;
        state.qty = {};
        state.order = [];
        state.conds = [];
        state.plink = {};
        state.u7 = '5'; state.u11 = {}; state.o28 = '2';
        state.condSkill = DEFAULT_SKILL;
        state.ensSkill = '';
        state.u11auto = false;
        state.mods = {};
        try { localStorage.removeItem(SAVE_KEY); } catch (e) { /* vide */ }
        closeMenu();
        render();
    }
    function bumpQty(id, d) {
        var k = KIND[id];
        var max = (k && k.t === 'n') ? k.max : 1;
        var v = (state.qty[id] | 0) + d;
        if (v <= 0) { removePiece(id); return; }
        if (v > max) return;
        state.qty[id] = v;
        render();
    }
    function removeCond(id) {
        state.conds = state.conds.filter(function (x) { return x !== id; });
        Object.keys(state.plink).forEach(function (eid) {
            if (state.plink[eid].t === 'cond' && state.plink[eid].id === id) delete state.plink[eid];
        });
        closeMenu();
        render();
    }
    function toggleU11(v) {
        state.u11[v] = !state.u11[v];
        var any = false;
        Object.keys(state.u11).forEach(function (x) { if (state.u11[x]) any = true; });
        if (any && (state.qty.U11 | 0) === 0) {
            var info = lockInfo(P('U11'));
            if (info.lock) {
                state.u11[v] = false;
                toast(info.reason);
                render();
                return;
            }
            state.order.push('U11');
            state.qty.U11 = 1;
            state.u11auto = true;
        }
        if (!any && (state.qty.U11 | 0) > 0 && state.u11auto) {
            delete state.qty.U11;
            delete state.plink.U11;
            state.order = state.order.filter(function (t) { return t !== 'U11'; });
            state.u11auto = false;
        }
        if (present('U11') && needFor('U11').length) openMenu('need', 'U11');
        render();
    }

    /* --- Lecture des modificateurs : sur chaque carte effet (state.mods), pas hors graphe.
       Les prérequis-conditions sont hors monnaie : ni coût, ni réduction. --- */
    function getMod(id) {
        var m = state.mods[id] || {};
        var rg = m.range | 0;
        if (rg < 0 || rg > 2) rg = 0;
        return { range: rg, zone: !!m.zone, dur: Math.max(0, Math.min(2, m.dur | 0)), cross: !!m.cross };
    }
    function setMod(id, patch) {
        var m = { range: 0, zone: false, dur: 0, cross: false };
        if (state.mods[id]) {
            m.range = state.mods[id].range | 0;
            m.zone = !!state.mods[id].zone;
            m.dur = state.mods[id].dur | 0;
            m.cross = !!state.mods[id].cross;
        }
        if (patch.range !== undefined) {
            var pv = patch.range | 0;
            m.range = Math.max(0, Math.min(2, pv));
        }
        if (patch.zone !== undefined) m.zone = !!patch.zone;
        if (patch.dur !== undefined) m.dur = Math.max(0, Math.min(2, patch.dur | 0));
        if (patch.cross !== undefined) m.cross = !!patch.cross;
        if (!m.range && !m.zone && !m.dur && !m.cross) delete state.mods[id];
        else state.mods[id] = m;
    }
    function buildSel() {
        var sel = { chassis: state.chassis, ensSkill: state.ensSkill || '', qty: {}, u7: state.u7, u11: {}, o28: state.o28,
                    narrow: 0, mods: {} };
        sel.conds = state.conds.slice();
        sel.plink = {};
        Object.keys(state.plink).forEach(function (eid) {
            if ((state.qty[eid] | 0) > 0) {
                var L = state.plink[eid];
                if (L && ((L.t === 'cond' && state.conds.indexOf(L.id) >= 0) ||
                           (L.t === 'piece' && (state.qty[L.id] | 0) > 0))) sel.plink[eid] = { t: L.t, id: L.id };
            }
        });
        Object.keys(state.qty).forEach(function (id) { sel.qty[id] = state.qty[id]; });
        sel.order = state.order.filter(function (id) { return (state.qty[id] | 0) > 0; });
        Object.keys(state.u11).forEach(function (v) { if (state.u11[v]) sel.u11[v] = true; });
        Object.keys(state.mods).forEach(function (id) {
            if ((state.qty[id] | 0) > 0) sel.mods[id] = getMod(id);
        });
        return sel;
    }

    /* --- 2. Finitions : rappel, les modificateurs vivent sur les cartes effet. --- */
    function buildModifs() {
        var mod = document.getElementById('fabModifs');
        if (!mod) return;
        mod.innerHTML = '';
        var n = el('div', 'fab-mod', '<b>Sur la carte effet, pas ici</b>');
        var nh = el('span', '', 'Clique une carte effet du graphe : <strong>Portée</strong> totale 1 à 3 (base 1 gratuite, +2 XP/case), ' +
            '<strong>Zone</strong> M3 (+2 XP +1 PA : cône, ligne ou rayon +1), <strong>Durée</strong> M4 (+1 XP +1 PA par cran, 2 max), ' +
            '<strong>Hors-compétence</strong> M7 (×2 XP). Seuls les compatibles sont proposés. ' +
            'Les <strong>Prérequis</strong> sont les cartes de la file ci-dessus (gratuites, hors logements).');
        nh.style.cssText = 'font-size:0.72rem;opacity:.75;display:block;margin-top:4px;';
        n.appendChild(nh);
        mod.appendChild(n);
    }

    /* Coût affiché sur le nœud : total réel pour les cumulables (×n), base sinon. */
    function nodeCost(p, id, isPa) {
        var k = KIND[id];
        var q = state.qty[id] | 0;
        if (k && k.t === 'n' && q > 1) {
            var n = fabNum(isPa ? p.cout_pa : p.cout_xp);
            return String((n.b + n.d) * q);
        }
        return isPa ? p.cout_pa : p.cout_xp;
    }
    function nodeCostXp(p, id) { return nodeCost(p, id, false); }
    function nodeCostPa(p, id) { return nodeCost(p, id, true); }
    /* Badges des modificateurs posés sur une carte (miroir de l'engine, sans validation). */
    function modFlags(id) {
        var m = getMod(id);
        var out = [];
        if (m.range > 0) out.push('portée ' + (m.range + 1) + ' cases (+' + (m.range * 2) + ' XP)');
        if (m.zone) out.push('zone (+2 XP +1 PA)');
        if (m.dur) out.push('+' + m.dur + ' tour' + (m.dur > 1 ? 's' : '') + ' (+' + m.dur + ' XP +' + m.dur + ' PA)');
        if (m.cross) out.push('×2 hors-compétence');
        var lk = state.plink[id];
        if (lk && lk.t === 'cond' && lk.id === 'critique' && (id === 'O1' || id === 'O8' || id === 'O13' || id === 'O15')) {
            var nq = state.qty[id] | 0;
            if (nq > 0) out.push('critique (−' + nq + ' XP)');
        }
        return out;
    }

    /* --- Rendu du graphe : racine ◆ + nœuds pièces + logements ⊕, reliés. --- */
    var FAM_COLOR = { offensif: '#f43f5e', defensif: '#38bdf8', utilitaire: '#10b981', social: '#f59e0b' };
    var prev = {
        badge: document.getElementById('fabBudgetBadge'),
        name: document.getElementById('fabCardName'),
        type: document.getElementById('fabCardType'),
        desc: document.getElementById('fabCardDesc'),
        pa: document.getElementById('fabCardPa'),
        xp: document.getElementById('fabCardXp'),
        list: document.getElementById('fabCardEffectsList'),
        check: document.getElementById('fabChecklist')
    };

    function variantSummary(id) {
        if (id === 'U7') return 'palier ' + state.u7 + ' PV';
        if (id === 'U11') {
            var on = Object.keys(state.u11).filter(function (v) { return state.u11[v]; });
            return on.length ? 'paliers ' + on.join(', ') : 'base';
        }
        return '';
    }

    function renderGraph(sel, r) {
        nodesEl.innerHTML = '';
        wiresEl.innerHTML = '';
        var ch = sel.chassis ? C(sel.chassis) : null;
        /* Coût réel par carte : lignes fiche (pièce + ses modificateurs − critique). */
        var costByPiece = {};
        if (r && r.lines) r.lines.forEach(function (l) {
            if (l.kind === 'piece') costByPiece[l.id] = { xp: l.xp, pa: l.pa };
            else if (l.kind === 'mod' && l.piece && costByPiece[l.piece]) {
                costByPiece[l.piece].xp += l.xp;
                costByPiece[l.piece].pa += l.pa;
            }
        });
        var flow = el('div', 'fab-flow', '');
        var rootcol = el('div', 'fab-rootcol', '');
        var branchcol = el('div', 'fab-branchcol', '');
        /* Racine ◆. */
        var root = el('div', 'fab-rootnode', '');
        root.setAttribute('role', 'button');
        root.setAttribute('tabindex', '0');
        root.dataset.node = 'root';
        var dots = '<span class="fab-dots">';
        for (var i = 0; i < 3; i++) dots += '<i class="' + (i < placedSpan() ? 'on' : '') + '"></i>';
        dots += '</span>';
        if (ch) {
            var ensSkillTxt = sel.ensSkill ? esc(sel.ensSkill) : '⚠ choisis ta compétence entraînée';
            root.innerHTML = '<span class="fab-ens-top"><span class="fab-ens-id">◆ ' + esc(ch.id) + '</span>' +
                '<span class="fab-ens-cost">' + esc(ch.cout_xp) + ' XP + ' + esc(ch.cout_pa) + ' PA</span></span>' +
                '<strong>' + esc(ch.nom) + '</strong><small>' + esc(ch.cadre) + '</small>' +
                '<small>Compétence : ' + ensSkillTxt + '</small>' + dots;
        } else {
            root.innerHTML = '<span class="fab-ens-top"><span class="fab-ens-id">◆ Ensemble</span></span>' +
                '<strong>Choisis ton ensemble</strong><small>Clique ici : il dit quand ta technique part.</small>' + dots;
        }
        rootcol.appendChild(root);
        flow.appendChild(rootcol);
        /* Nœuds pièces + logements ⊕. */
        if (!ch && !state.order.length) {
            var hint = el('div', 'fab-graph-hint',
                'Clique <strong>◆</strong> pour choisir ton ensemble — puis <strong>⊕</strong> pour brancher une pièce.<br>' +
                'Le menu ne propose que les choix possibles ici.');
            branchcol.appendChild(hint);
        }
        /* File des prérequis : cartes à part, avant les effets (0 XP, hors logements).
           Pas d'ajout manuel : elles apparaissent quand un effet les exige. */
        var lane = el('div', 'fab-preq-lane', '');
        lane.appendChild(el('div', 'fab-preq-title', 'Prérequis (conditions · 0 XP · hors logements)'));
        state.conds.forEach(function (cid) {
            var cd = conditionById(cid);
            if (!cd) return;
            var card = el('div', 'fab-preq', '');
            card.setAttribute('role', 'button');
            card.setAttribute('tabindex', '0');
            card.dataset.node = 'preq';
            card.dataset.id = cid;
            var sub = esc(cd.texte);
            if (cid === 'compet') sub += ' — <strong>' + esc(state.condSkill) + '</strong>';
            card.innerHTML = '<strong>' + esc(cd.nom) + '</strong>' +
                '<span class="fab-preq-cost">0 XP</span>' +
                '<small>' + sub + '</small>';
            lane.appendChild(card);
        });
        if (!state.conds.length)
            lane.appendChild(el('div', 'fab-preq-empty', 'Elles apparaîtront ici quand un effet les exigera.'));
        branchcol.appendChild(lane);
        state.order.forEach(function (id) {
            var p = P(id);
            if (!p) return;
            var box = el('div', 'fab-placed fam-' + p.famille, '');
            box.setAttribute('role', 'button');
            box.setAttribute('tabindex', '0');
            box.dataset.node = 'piece';
            box.dataset.id = id;
            var qq = state.qty[id] | 0;
            var nm = scaledPieceText(p.nom, id, qq);
            var ec = costByPiece[id];
            var costTxt = ec ? (ec.xp + ' XP + ' + ec.pa + ' PA') : (nodeCostXp(p, id) + ' XP + ' + nodeCostPa(p, id) + ' PA');
            var head = '<div class="fab-placed-head"><strong>' + esc(nm) + '</strong>' +
                '<span class="fab-placed-cost">' + esc(costTxt) + '</span></div>';
            var sub = esc(p.cond || scaledPieceText(p.texte, id, qq) || '');
            if (id === 'O28') sub = sub.replace(/2 à 4/, state.o28 || '2');
            var vs = variantSummary(id);
            if (vs) sub += ' — ' + esc(vs);
            if (qq > 1 && sub.indexOf('×' + qq) < 0 && nm === String(p.nom)) sub += ' — ×' + qq;
            var flags = '<div class="fab-placed-flags">';
            chainOf(id).forEach(function (x) { flags += '<span class="fab-flag">' + esc(x) + '</span>'; });
            modFlags(id).forEach(function (t) { flags += '<span class="fab-flag mod">' + esc(t) + '</span>'; });
            flags += '</div>';
            var tiers = '';
            if (chainOf(id).length > 1) {
                tiers = '<div class="fab-chain">' + chainOf(id).map(function (x) {
                    var q = P(x);
                    return '<div class="fab-chain-tier"><b>' + esc(x) + '</b> — ' + esc(q ? q.nom : '') + '</div>';
                }).join('') + '</div>';
            }
            box.innerHTML = head + '<div class="fab-placed-sub">' + sub + '</div>' + flags + tiers +
                '<div class="fab-placed-sub" style="opacity:.55">Clique pour régler ou retirer.</div>';
            branchcol.appendChild(box);
        });
        for (var s = placedSpan(); s < 3; s++) {
            var slot = el('button', 'fab-slot-empty', '<span class="fab-plus">⊕</span><span>Logement libre — clique pour brancher une pièce.</span>');
            slot.type = 'button';
            slot.dataset.node = 'slot';
            branchcol.appendChild(slot);
        }
        flow.appendChild(branchcol);
        nodesEl.appendChild(flow);
        /* Fils : courbes racine → chaque rangée. */
        if (!ch) return;
        try {
            var gr = graphEl.getBoundingClientRect();
            if (gr.width <= 0) return;
            wiresEl.setAttribute('viewBox', '0 0 ' + gr.width + ' ' + gr.height);
            var rb = root.getBoundingClientRect();
            var rx = rb.right - gr.left, ry = rb.top + rb.height / 2 - gr.top;
            var elByKey = { 'root:': root };
            var items = [];
            for (var ri = 0; ri < branchcol.children.length; ri++) {
                var row = branchcol.children[ri];
                if (row.classList && row.classList.contains('fab-preq-lane')) {
                    for (var ci = 0; ci < row.children.length; ci++) {
                        var cell = row.children[ci];
                        if (cell.dataset && cell.dataset.node === 'preq') {
                            elByKey['preq:' + cell.dataset.id] = cell;
                            items.push({ elm: cell, from: 'root:', color: '#f59e0b', dash: false });
                        }
                    }
                } else if (row.dataset && (row.dataset.node === 'piece' || row.dataset.node === 'slot')) {
                    items.push(row);
                }
            }
            items.forEach(function (it) {
                var elm = it.elm || it;
                var bb = elm.getBoundingClientRect();
                var x2 = bb.left - gr.left, y2 = bb.top + bb.height / 2 - gr.top;
                var x1 = rx, y1 = ry;
                var stroke = null, dash = false;
                if (it.elm) {
                    stroke = it.color;
                } else {
                    var nd = elm.dataset.node;
                    if (nd === 'slot') { dash = true; }
                    else if (nd === 'piece') {
                        var pid = elm.dataset.id;
                        var pp = pid ? P(pid) : null;
                        stroke = pp ? FAM_COLOR[pp.famille] : null;
                        var tgt = linkTarget(pid);
                        if (tgt) {
                            var src = elByKey[tgt.node + ':' + tgt.id];
                            if (src && src !== elm) {
                                var sb = src.getBoundingClientRect();
                                x1 = sb.right - gr.left; y1 = sb.top + sb.height / 2 - gr.top;
                                if (tgt.node === 'preq') stroke = '#f59e0b';
                            }
                        }
                    }
                }
                var path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
                path.setAttribute('d', 'M ' + x1 + ' ' + y1 + ' C ' + (x1 + 50) + ' ' + y1 + ', ' + (x2 - 50) + ' ' + y2 + ', ' + x2 + ' ' + y2);
                path.setAttribute('fill', 'none');
                path.setAttribute('stroke', stroke || 'rgba(255,255,255,0.22)');
                path.setAttribute('stroke-width', '2');
                if (dash) path.setAttribute('stroke-dasharray', '5 4');
                wiresEl.appendChild(path);
            });
        } catch (e) { /* mesure indisponible : graphe sans fils */ }
    }

    /* Prérequis dont dépend cet effet : {t:'cond'|'piece', id} ou null. */
    function linkTarget(id) {
        var L = state.plink[id];
        if (!L) return null;
        if (L.t === 'cond' && state.conds.indexOf(L.id) >= 0) return { node: 'preq', id: L.id };
        if (L.t === 'piece' && present(L.id)) return { node: 'piece', id: L.id };
        return null;
    }

    /* --- Menu contextuel : que les choix possibles ici. --- */
    function openMenu(kind, id, x, y) {
        menu = { kind: kind, id: id || null, search: '' };
        renderMenu(x, y, true);
    }
    function closeMenu() {
        if (!menu) return;
        menu = null;
        if (ctxEl) { ctxEl.hidden = true; ctxEl.innerHTML = ''; }
    }
    function slotOptions() {
        return CAT.pieces.map(function (p) { return { p: p, info: lockInfo(p) }; });
    }
    function firstLegal() {
        var q = menu ? normSearch(menu.search) : '';
        var opts = slotOptions();
        for (var i = 0; i < opts.length; i++) {
            if (q && normSearch(opts[i].p.nom + ' ' + opts[i].p.id).indexOf(q) < 0) continue;
            if (!opts[i].info.lock) return opts[i].p.id;
        }
        return null;
    }
    function optRow(o, fam) {
        var p = o.p, info = o.info;
        var b = el('div', 'fab-opt fam-' + (fam || p.famille) + (info.lock ? ' is-locked' : ''), '');
        var h = '<span class="fab-opt-top"><strong>' + esc(p.nom) + '</strong>' +
            '<span class="fab-opt-cost">' + esc(p.id) + ' · ' + esc(p.cout_xp) + ' XP + ' + esc(p.cout_pa) + ' PA</span></span>';
        if (p.prerequis && p.prerequis.length)
            h += '<div class="fab-opt-req"><span class="rq">Demande ' + esc(p.prerequis.join(', ')) + '</span></div>';
        if (p.incompatible && p.incompatible.length)
            h += '<div class="fab-opt-req"><span class="rf">Refuse ' + esc(p.incompatible.join(', ')) + '</span></div>';
        if (info.lock) {
            h += '<div class="fab-opt-lock">🔒 ' + esc(info.reason) + '</div>';
        } else if (info.soft) {
            b.dataset.pick = p.id;
            b.tabIndex = 0;
            h += '<div class="fab-opt-soft">⚠ ' + esc(info.soft) + '</div>';
        } else {
            b.dataset.pick = p.id;
            b.tabIndex = 0;
        }
        b.innerHTML = h;
        return b;
    }
    function renderMenu(x, y, focus) {
        if (!menu || !ctxEl) return;
        ctxEl.innerHTML = '';
        var head = el('div', 'fab-ctx-head', '');
        var title = menu.kind === 'root' ? '◆ Ensemble → ?' :
                    menu.kind === 'piece' ? esc(menu.id || '') + ' → ?' :
                    menu.kind === 'need' ? 'Prérequis pour ' + esc(menu.id || '') + ' → ?' :
                    menu.kind === 'preq' ? 'Prérequis ' + esc(menu.id || '') + ' → ?' : '⊕ Logement → ?';
        head.innerHTML = '<span class="fab-ctx-title">' + title + '</span>';
        var x0 = el('button', 'fab-ctx-close', '✕');
        x0.type = 'button';
        head.appendChild(x0);
        ctxEl.appendChild(head);
        var list = el('div', 'fab-ctx-list', '');

        if (menu.kind === 'root') {
            var s = document.createElement('input');
            s.className = 'fab-ctx-search';
            s.setAttribute('data-search', '1');
            s.placeholder = 'Filtrer…';
            s.value = menu.search;
            ctxEl.appendChild(s);
            list.appendChild(el('div', 'fab-ctx-group', 'Ensembles (un seul)'));
            var q = normSearch(menu.search);
            CAT.chassis.forEach(function (c) {
                if (q && normSearch(c.nom + ' ' + c.id).indexOf(q) < 0) return;
                var b = el('button', 'fab-opt', '');
                b.type = 'button';
                b.dataset.ensPick = c.id;
                b.innerHTML = '<span class="fab-opt-top"><strong>' + esc(c.nom) + '</strong>' +
                    '<span class="fab-opt-cost">' + esc(c.id) + ' · ' + esc(c.cout_xp) + ' XP + ' + esc(c.cout_pa) + ' PA</span></span>' +
                    '<div class="fab-opt-sub">' + esc(c.cadre) + '</div>' +
                    (state.chassis === c.id ? '<div class="fab-opt-soft">◆ actuel — clique pour retirer</div>' : '');
                list.appendChild(b);
            });
            list.appendChild(el('div', 'fab-ctx-sec', 'Compétence liée (entraînée, obligatoire)'));
            (function () {
                var sv = el('div', 'fab-ctx-var', '');
                var slab = el('label', '', 'Compétence ');
                slab.style.cssText = 'font-size:0.78rem;display:flex;gap:6px;align-items:baseline;';
                var ssel = document.createElement('select');
                ssel.className = 'tool-select fab-input';
                ssel.setAttribute('data-ens-skill', '1');
                var op0 = document.createElement('option');
                op0.value = ''; op0.textContent = '— choisis —';
                if (!state.ensSkill) op0.selected = true;
                ssel.appendChild(op0);
                SKILLS.forEach(function (grp) {
                    var og = document.createElement('optgroup');
                    og.label = grp[0];
                    grp[1].forEach(function (nm) {
                        var op = document.createElement('option');
                        op.value = nm; op.textContent = nm;
                        if (state.ensSkill === nm) op.selected = true;
                        og.appendChild(op);
                    });
                    ssel.appendChild(og);
                });
                slab.appendChild(ssel);
                sv.appendChild(slab);
                if (!state.ensSkill) sv.appendChild(el('div', 'fab-ctx-foot', 'Tout pouvoir est lié à une compétence entraînée.'));
                list.appendChild(sv);
            })();
            list.appendChild(el('div', 'fab-ctx-foot', 'Échap = annuler · Entrée = premier'));
        } else if (menu.kind === 'slot') {
            var s2 = document.createElement('input');
            s2.className = 'fab-ctx-search';
            s2.setAttribute('data-search', '1');
            s2.placeholder = 'Filtrer les 39 pièces…';
            s2.value = menu.search;
            ctxEl.appendChild(s2);
            var q2 = normSearch(menu.search);
            var groups = { offensif: [], defensif: [], utilitaire: [], social: [] };
            slotOptions().forEach(function (o) {
                if (q2 && normSearch(o.p.nom + ' ' + o.p.id).indexOf(q2) < 0) return;
                groups[o.p.famille].push(o);
            });
            Object.keys(groups).forEach(function (k) {
                groups[k].sort(function (a, b) {
                    var la = a.info.lock ? 1 : 0, lb = b.info.lock ? 1 : 0;
                    if (la !== lb) return la - lb;
                    var sa = (a.info.soft ? 1 : 0), sb = (b.info.soft ? 1 : 0);
                    if (sa !== sb) return sa - sb;
                    return a.p.id < b.p.id ? -1 : 1;
                });
            });
            var shown = 0;
            ['offensif', 'defensif', 'utilitaire', 'social'].forEach(function (fam) {
                if (!groups[fam].length) return;
                shown += groups[fam].length;
                list.appendChild(el('div', 'fab-ctx-group', FAM_LABEL[fam]));
                groups[fam].forEach(function (o) { list.appendChild(optRow(o, fam)); });
            });
            if (!shown) list.appendChild(el('div', 'fab-ctx-empty', '(aucun choix pour ce filtre — efface ta recherche)'));
            list.appendChild(el('div', 'fab-ctx-foot', 'Échap = annuler · Entrée = premier choix possible'));
        } else if (menu.kind === 'need') {
            /* Prérequis exigé par l'effet qui vient d'être posé : le joueur en choisit un.
               La carte part en output de l'ensemble, l'effet en output de la carte. */
            var effP = P(menu.id);
            if (!effP) { closeMenu(); return; }
            list.appendChild(el('div', 'fab-opt-sub', '« ' + esc(effP.nom) + ' » exige un prérequis — choisis-en un.'));
            var s4 = document.createElement('input');
            s4.className = 'fab-ctx-search';
            s4.setAttribute('data-search', '1');
            s4.placeholder = 'Filtrer…';
            s4.value = menu.search;
            ctxEl.appendChild(s4);
            var q4 = normSearch(menu.search);
            var needs = needFor(menu.id);
            var nshown = 0;
            var nGroups = { piece: [], cond: [] };
            needs.forEach(function (o) { nGroups[o.t].push(o); });
            if (nGroups.piece.length) {
                list.appendChild(el('div', 'fab-ctx-group', 'Pièces requises'));
                nGroups.piece.forEach(function (o) {
                    var mp = P(o.id);
                    if (!mp) return;
                    if (q4 && normSearch(mp.nom + ' ' + o.id).indexOf(q4) < 0) return;
                    nshown++;
                    var li = lockInfo(mp);
                    var nb = el('div', 'fab-opt fam-' + mp.famille + (li.lock ? ' is-locked' : ''), '');
                    var nh = '<span class="fab-opt-top"><strong>' + esc(mp.nom) + '</strong>' +
                        '<span class="fab-opt-cost">' + esc(o.id) + ' · ' + esc(mp.cout_xp) + ' XP + ' + esc(mp.cout_pa) + ' PA</span></span>';
                    if (mp.prerequis && mp.prerequis.length)
                        nh += '<div class="fab-opt-req"><span class="rq">Demande ' + esc(mp.prerequis.join(', ')) + '</span></div>';
                    if (li.lock) {
                        nh += '<div class="fab-opt-lock">🔒 ' + esc(li.reason) + '</div>';
                    } else {
                        nb.dataset.needpick = 'piece:' + o.id;
                        nb.tabIndex = 0;
                        if (li.soft) nh += '<div class="fab-opt-soft">⚠ ' + esc(li.soft) + '</div>';
                    }
                    nb.innerHTML = nh;
                    list.appendChild(nb);
                });
            }
            if (nGroups.cond.length) {
                list.appendChild(el('div', 'fab-ctx-group', 'Conditions (gratuites, hors logements)'));
                nGroups.cond.forEach(function (o) {
                    var cd = conditionById(o.id);
                    if (!cd) return;
                    if (q4 && normSearch(cd.nom + ' ' + o.id).indexOf(q4) < 0) return;
                    nshown++;
                    var placed = state.conds.indexOf(o.id) >= 0;
                    var cb2 = el('div', 'fab-opt fam-preq' + (placed ? ' is-locked' : ''), '');
                    var sub2 = esc(cd.texte);
                    if (o.id === 'compet') sub2 += ' — ' + esc(state.condSkill);
                    var ch2 = '<span class="fab-opt-top"><strong>' + esc(cd.nom) + '</strong>' +
                        '<span class="fab-opt-cost">0 XP</span></span>' +
                        '<div class="fab-opt-sub">' + sub2 + '</div>';
                    if (placed) {
                        ch2 += '<div class="fab-opt-lock">✓ déjà branchée — clique pour lier l\u2019effet</div>';
                        cb2.dataset.needpick = 'cond:' + o.id;
                        cb2.tabIndex = 0;
                    } else {
                        cb2.dataset.needpick = 'cond:' + o.id;
                        cb2.tabIndex = 0;
                    }
                    cb2.innerHTML = ch2;
                    list.appendChild(cb2);
                });
            }
            if (!nshown) list.appendChild(el('div', 'fab-ctx-empty', '(rien à choisir — reviens en arrière)'));
            list.appendChild(el('div', 'fab-ctx-foot', 'Échap = annuler (la fiche signalera le manque) · Entrée = premier'));
        } else if (menu.kind === 'preq') {
            var cdp = conditionById(menu.id);
            if (!cdp) { closeMenu(); return; }
            list.appendChild(el('div', 'fab-opt-sub', esc(cdp.nom) + ' — ' + esc(cdp.texte)));
            if (menu.id === 'compet') {
                list.appendChild(el('div', 'fab-ctx-sec', 'Entraînement requis'));
                var slab = el('label', '', 'Compétence ');
                slab.style.cssText = 'font-size:0.78rem;display:flex;gap:6px;align-items:baseline;';
                var ssel = document.createElement('select');
                ssel.className = 'tool-select fab-input';
                ssel.setAttribute('data-condskill', '1');
                SKILLS.forEach(function (grp) {
                    var og = document.createElement('optgroup');
                    og.label = grp[0];
                    grp[1].forEach(function (nm) {
                        var op = document.createElement('option');
                        op.value = nm; op.textContent = nm;
                        if (state.condSkill === nm) op.selected = true;
                        og.appendChild(op);
                    });
                    ssel.appendChild(og);
                });
                slab.appendChild(ssel);
                var sv = el('div', 'fab-ctx-var', '');
                sv.appendChild(slab);
                list.appendChild(sv);
            }
            list.appendChild(el('div', 'fab-ctx-sec', 'Nœud'));
            var prr = el('div', 'fab-ctx-row', '');
            var pdel = el('button', 'fab-mini-btn', '⤴ Retirer ce prérequis');
            pdel.type = 'button';
            pdel.dataset.uncond = cdp.id;
            prr.appendChild(pdel);
            list.appendChild(prr);
            list.appendChild(el('div', 'fab-ctx-foot', '0 XP · hors logements'));
        } else if (menu.kind === 'piece') {
            var p = P(menu.id);
            if (!p) { closeMenu(); return; }
            var info = lockInfo(p);
            var k = KIND[p.id];
            if (k && (p.id === 'U7' || p.id === 'U11' || p.id === 'O28')) {
                list.appendChild(el('div', 'fab-ctx-sec', 'Variante'));
                var vv = el('div', 'fab-ctx-var', '');
                if (p.id === 'U7') {
                    var lab = el('label', '', 'Palier ');
                    var sel7 = document.createElement('select');
                    sel7.className = 'tool-select fab-input';
                    sel7.setAttribute('data-var-u7', '1');
                    KIND.U7.opts.forEach(function (o) {
                        var op = document.createElement('option');
                        op.value = o.v; op.textContent = o.label;
                        if (state.u7 === o.v) op.selected = true;
                        sel7.appendChild(op);
                    });
                    lab.appendChild(sel7);
                    vv.appendChild(lab);
                } else if (p.id === 'O28') {
                    var lab28 = el('label', '', 'Résiduels ');
                    var sel28 = document.createElement('select');
                    sel28.className = 'tool-select fab-input';
                    sel28.setAttribute('data-var-o28', '1');
                    KIND.O28.opts.forEach(function (o) {
                        var op28 = document.createElement('option');
                        op28.value = o.v; op28.textContent = o.label;
                        if ((state.o28 || '2') === o.v) op28.selected = true;
                        sel28.appendChild(op28);
                    });
                    lab28.appendChild(sel28);
                    vv.appendChild(lab28);
                } else if (p.id === 'U11') {
                    var c0u = state.chassis === 'C0';
                    KIND.U11.opts.forEach(function (o) {
                        var dis = (c0u && (o.dpa | 0) > 0) ? ' disabled title="+1 PA : pas sur un ensemble passif."' : '';
                        var l = el('label', '', '<input type="checkbox" data-var-u11="1" data-sub="' + o.v + '"' +
                            (state.u11[o.v] ? ' checked' : '') + dis + '> ' + esc(o.label));
                        vv.appendChild(l);
                    });
                    if (c0u) vv.appendChild(el('div', 'fab-ctx-foot', 'Palier +1 PA : pas sur un passif.'));
                }
                list.appendChild(vv);
            }
            if (k && k.t === 'n') {
                list.appendChild(el('div', 'fab-ctx-sec', 'Quantité (cumulable ×' + k.max + ')'));
                var row = el('div', 'fab-ctx-row', '');
                row.innerHTML = '<span class="fab-stepper"><button type="button" data-dec="' + esc(p.id) + '">−</button>' +
                    '<span>×' + (state.qty[p.id] | 0) + ' / ' + k.max + '</span>' +
                    '<button type="button" data-inc="' + esc(p.id) + '">+</button></span>';
                list.appendChild(row);
            }
            /* Modificateurs sur cette carte : seuls les compatibles sont montrés.
               L'incompatible est masqué (pas grisé) ; mention si aucun M1–M4. */
            list.appendChild(el('div', 'fab-ctx-sec', 'Modificateurs de cette carte'));
            (function () {
                var mv = el('div', 'fab-ctx-var', '');
                var cur = getMod(p.id);
                var c0 = state.chassis === 'C0';
                var shown = 0;
                if (modRangeOk(p).ok) {
                    shown++;
                    var rlab = el('label', '', 'Portée totale (1–3, base 1 gratuite, +2 XP/case) ');
                    var rn = document.createElement('input');
                    rn.type = 'number'; rn.min = '1'; rn.max = '3'; rn.value = String(cur.range + 1);
                    rn.className = 'tool-input fab-input'; rn.style.width = '70px';
                    rn.setAttribute('data-mod-range', '1'); rn.setAttribute('data-mid', p.id);
                    rlab.appendChild(rn);
                    mv.appendChild(rlab);
                }
                if (modZoneOk(p).ok) {
                    shown++;
                    var zdis = c0 ? ' disabled title="+1 PA : pas sur un ensemble passif."' : '';
                    var zl = el('label', '', '<input type="checkbox" data-mod-zone="1" data-mid="' + esc(p.id) + '"' +
                        (cur.zone ? ' checked' : '') + zdis + '> Zone M3 : cône, ligne ou rayon +1 (+2 XP +1 PA)');
                    mv.appendChild(zl);
                    if (c0) mv.appendChild(el('div', 'fab-ctx-foot', 'Zone (+1 PA) : pas sur un passif.'));
                }
                if (modDurOk(p).ok) {
                    shown++;
                    var dlab = el('label', '', 'Durée M4 (+1 XP +1 PA par cran, 2 max) ');
                    var dn = document.createElement('input');
                    dn.type = 'number'; dn.min = '0'; dn.max = '2'; dn.value = String(cur.dur);
                    dn.className = 'tool-input fab-input'; dn.style.width = '70px';
                    dn.setAttribute('data-mod-dur', '1'); dn.setAttribute('data-mid', p.id);
                    if (c0) { dn.disabled = true; dn.title = '+1 PA : pas sur un ensemble passif.'; }
                    dlab.appendChild(dn);
                    mv.appendChild(dlab);
                    if (c0) mv.appendChild(el('div', 'fab-ctx-foot', 'Durée (+1 PA) : pas sur un passif.'));
                }
                if (!shown) mv.appendChild(el('div', 'fab-ctx-foot', 'Aucun modificateur M1–M4 compatible avec cette carte.'));
                var xl = el('label', '', '<input type="checkbox" data-mod-cross="1" data-mid="' + esc(p.id) + '"' +
                    (cur.cross ? ' checked' : '') + '> Hors-compétence M7 : cette carte paie double (×2 XP)');
                mv.appendChild(xl);
                list.appendChild(mv);
            })();
            if (p.id === 'O13' || p.id === 'O1' || p.id === 'O8' || p.id === 'O15') {
                list.appendChild(el('div', 'fab-ctx-sec', 'Condition optionnelle (M5)'));
                (function () {
                    var cv = el('div', 'fab-ctx-var', '');
                    var linked = !!(state.plink[p.id] && state.plink[p.id].t === 'cond' && state.plink[p.id].id === 'critique');
                    var cl = el('label', '', '<input type="checkbox" data-opt-cond="critique" data-mid="' + esc(p.id) + '"' +
                        (linked ? ' checked' : '') + '> Sur critique : −1 XP par cumul');
                    cv.appendChild(cl);
                    cv.appendChild(el('div', 'fab-ctx-foot', 'Lie la carte Prérequis Sur critique (lane du haut).'));
                    list.appendChild(cv);
                })();
            }
            list.appendChild(el('div', 'fab-ctx-sec', 'Nœud'));
            var rr2 = el('div', 'fab-ctx-row', '');
            var del = el('button', 'fab-mini-btn', '⤴ Retirer ce nœud');
            del.type = 'button';
            del.dataset.unfit = p.id;
            rr2.appendChild(del);
            list.appendChild(rr2);
            /* Prérequis manquants : cartes à rebrancher sans quitter le menu. */
            {
                var miss = [];
                missingGroups(p).forEach(function (g) {
                    g.forEach(function (mid) { if (miss.indexOf(mid) < 0) miss.push(mid); });
                });
                if (miss.length) {
                    list.appendChild(el('div', 'fab-ctx-sec', 'Prérequis à rebrancher'));
                    var pr = el('div', 'fab-ctx-row', '');
                    miss.forEach(function (mid) {
                        var mp = P(mid);
                        var pb = el('button', 'fab-preq-btn', '⤓ ' + mid + ' — ' + (mp ? mp.nom : ''));
                        pb.type = 'button';
                        pb.dataset.prepick = mid;
                        pr.appendChild(pb);
                    });
                    list.appendChild(pr);
                }
            }
            /* Prérequis encore exigé : rouvre le menu imposé. */
            if (needFor(p.id).length) {
                list.appendChild(el('div', 'fab-ctx-sec', 'Prérequis manquant'));
                var nr = el('div', 'fab-ctx-row', '');
                var nb = el('button', 'fab-preq-btn', '⊕ Choisir le prérequis requis');
                nb.type = 'button';
                nb.dataset.needfor = p.id;
                nr.appendChild(nb);
                list.appendChild(nr);
            }
            var meta = [];
            if (p.seuil) meta.push('Seuil : ' + p.seuil);
            if (p.portee) meta.push('Portée : ' + p.portee);
            if (p.prerequis && p.prerequis.length) meta.push('Demande : ' + p.prerequis.join(', '));
            if (p.incompatible && p.incompatible.length) meta.push('Refuse : ' + p.incompatible.join(', '));
            list.appendChild(el('div', 'fab-ctx-foot', esc(meta.join(' · '))));
            if (info.soft) {
                var w = el('div', '', '');
                w.style.cssText = 'color:var(--temporal-amber);font-size:0.74rem;margin-top:4px;';
                w.textContent = '⚠ ' + info.soft;
                list.appendChild(w);
            }
        }
        ctxEl.appendChild(list);
        /* Position : près du clic, rabattu dans la fenêtre. */
        if (typeof x === 'number' && typeof y === 'number') {
            ctxEl.hidden = false;
            ctxEl.style.left = Math.max(8, Math.min(x, window.innerWidth - 345)) + 'px';
            ctxEl.style.top = Math.max(8, Math.min(y, window.innerHeight - 120)) + 'px';
            try {
                var b = ctxEl.getBoundingClientRect();
                if (b.bottom > window.innerHeight - 8)
                    ctxEl.style.top = Math.max(8, window.innerHeight - b.height - 8) + 'px';
                if (b.right > window.innerWidth - 8)
                    ctxEl.style.left = Math.max(8, window.innerWidth - b.width - 8) + 'px';
            } catch (e) { /* mesure indisponible */ }
        } else {
            ctxEl.hidden = false;
        }
        var si = ctxEl.querySelector('[data-search]');
        if (focus && si && si.focus) {
            try {
                si.focus();
                var v = si.value || '';
                si.setSelectionRange(v.length, v.length);
            } catch (e) { /* focus indisponible */ }
        }
        linkifyFab(ctxEl);
    }

    /* --- Rendu. --- */
    function render() {
        var sel = buildSel();
        var r = FabriqueEngine.compute(CAT, sel);
        if (sel.chassis === 'C4') {
            var narrow = sel.narrow | 0;
            var hasSeuil = false;
            Object.keys(sel.qty).forEach(function (id) {
                var p = P(id);
                if (p && p.seuil) hasSeuil = true;
                chainOf(id).forEach(function (x) { var q = P(x); if (q && q.seuil) hasSeuil = true; });
            });
            if (!narrow && !hasSeuil)
                r.warnings.push('Opportunité : ajoute un déclencheur étroit (Δ≥2, critique, esquive…) ou une carte Prérequis.');
        }
        if (menu && menu.kind === 'piece' && !present(menu.id)) menu = null;
        if (menu && menu.kind === 'need' && !present(menu.id)) menu = null;
        if (menu && menu.kind === 'preq' && state.conds.indexOf(menu.id) < 0) menu = null;
        renderGraph(sel, r);
        renderMenu();
        /* Nom automatique live : le champ suit la suggestion tant qu'il n'est pas personnalisé. */
        var nameInput = document.getElementById('fabName');
        var suggestion = '';
        try { suggestion = FabriqueEngine.suggestName(CAT, sel); } catch (e) { suggestion = 'Technique Maison'; }
        if (nameInput) {
            if (!nameInput.value || nameInput.value === lastAuto) nameInput.value = suggestion;
            lastAuto = suggestion;
            var hint = document.getElementById('fabNameAuto');
            if (hint) {
                hint.innerHTML = '';
                if (nameInput.value === suggestion) {
                    hint.textContent = 'Nom automatique — renomme à ta main si tu veux, je continuerai de proposer à côté.';
                } else {
                    hint.appendChild(document.createTextNode('Suggestion : '));
                    var b = document.createElement('strong');
                    b.textContent = suggestion;
                    hint.appendChild(b);
                    hint.appendChild(document.createTextNode(' '));
                    var ap = document.createElement('button');
                    ap.type = 'button';
                    ap.className = 'fab-mini-btn';
                    ap.setAttribute('data-apply-auto', '1');
                    ap.textContent = '↻ Reprendre';
                    hint.appendChild(ap);
                }
            }
        }
        var name = (nameInput || {}).value || '';
        /* Description narrative automatique : même suivi que le nom. */
        var descInput = document.getElementById('fabDesc');
        var descSug = '';
        try { descSug = FabriqueEngine.suggestDesc(CAT, sel); } catch (e) { descSug = ''; }
        if (descInput) {
            if (!descInput.value || descInput.value === lastDesc) descInput.value = descSug;
            lastDesc = descSug;
            var dhint = document.getElementById('fabDescAuto');
            if (dhint) {
                dhint.innerHTML = '';
                if (!descSug) {
                    dhint.textContent = '';
                } else if (descInput.value === descSug) {
                    dhint.textContent = 'Description automatique — réécris à ta main si tu veux, je continuerai de proposer à côté.';
                } else {
                    dhint.appendChild(document.createTextNode('Suggestion : '));
                    var db = document.createElement('em');
                    db.textContent = descSug;
                    dhint.appendChild(db);
                    dhint.appendChild(document.createTextNode(' '));
                    var dap = document.createElement('button');
                    dap.type = 'button';
                    dap.className = 'fab-mini-btn';
                    dap.setAttribute('data-apply-desc', '1');
                    dap.textContent = '↻ Reprendre';
                    dhint.appendChild(dap);
                }
            }
        }
        var desc = (descInput || {}).value || '';
        prev.name.textContent = name.trim() || 'Technique Maison';
        var fams = r.familles.map(function (f) { return f.toUpperCase(); }).join(' / ') || '—';
        prev.type.textContent = fams + (r.status === 'signature' ? ' — SIGNATURE (accord MJ)' : r.status === 'invalide' ? ' — À CORRIGER' : '');
        prev.desc.textContent = desc.trim() || 'Décris ton geste : ce qu’on voit, ce qu’on entend.';
        prev.pa.textContent = r.pa + ' PA';
        prev.xp.textContent = 'Création : ' + r.xp + ' XP' + (r.crossed ? ' (croisé ×2)' : '');
        if (prev.badge) {
            prev.badge.textContent = r.xp + ' XP · ' + r.pa + ' PA' +
                (r.status === 'signature' ? ' · SIGNATURE' : r.status === 'invalide' ? ' · À CORRIGER' : ' · ' + placedSpan() + '/3 logements');
        }
        prev.list.innerHTML = '';
        if (!r.lines.length) prev.list.innerHTML = '<li><em>Clique ◆ puis ⊕ pour monter ton graphe.</em></li>';
        r.lines.forEach(function (l) {
            var li = document.createElement('li');
            li.textContent = l.label + ' — ' + l.xp + ' XP + ' + l.pa + ' PA' + (l.narrow ? ' (−' + (typeof l.narrow === 'number' ? l.narrow : 1) + ' étroite)' : '');
            prev.list.appendChild(li);
        });
        if (prev.check) {
        prev.check.innerHTML = '';
        function ck(ok, txt) {
            var li = document.createElement('li');
            li.textContent = (ok ? '✓ ' : '✗ ') + txt;
            li.style.color = ok ? 'var(--matrix-emerald)' : 'var(--vital-ruby)';
            prev.check.appendChild(li);
        }
        ck(r.errors.length === 0, r.errors.length ? r.errors[0] : (r.status === 'signature' ? 'Montage valide : signature, fais signer ton MJ.' : 'Montage valide : ' + r.xp + ' XP / ' + r.pa + ' PA, ' + placedSpan() + '/3 logements.'));
        r.errors.slice(1).forEach(function (e) { ck(false, e); });
        r.warnings.forEach(function (w) {
            var li = document.createElement('li');
            li.textContent = '⚠ ' + w;
            li.style.color = 'var(--temporal-amber)';
            prev.check.appendChild(li);
        });
        }
        save();
        var pz = document.getElementById('fabPuzzle');
        if (pz) linkifyFab(pz);
        return r;
    }

    /* --- Presets (montages vérifiés du Livre III §14). --- */
    var PRESETS = {
        suture: { chassis: 'C0', ensSkill: 'Premiers Soins', qty: { U1: 1 }, conds: ['contact'], plink: { U1: { t: 'cond', id: 'contact' } }, name: 'Suture Réflexe (canonique)', desc: 'Les premiers soins d’urgence passent de 3 à 2 PA.' },
        miroir: { chassis: 'C3', ensSkill: "Maniement d'Arme", qty: { D1: 1 }, conds: ['contact'], plink: { D1: { t: 'cond', id: 'contact' } }, name: 'Lame Miroir (canonique)', desc: 'Tu pares les tirs laser au contact (Delta ≥ 0).' },
        fente: { chassis: 'C2', ensSkill: "Maniement d'Arme", qty: { O8: 1, O20: 1 }, name: 'Fente Basse', desc: 'Tu passes sous la garde et tu fauches le jarret.' }
    };
    function applyPreset(key) {
        var p = PRESETS[key];
        if (!p) return;
        state.chassis = p.chassis;
        state.qty = {};
        state.order = [];
        Object.keys(p.qty).forEach(function (id) {
            state.qty[id] = p.qty[id];
            state.order.push(id);
        });
        state.u7 = '5'; state.u11 = {}; state.o28 = '2';
        state.mods = {};
        state.ensSkill = p.ensSkill || '';
        Object.keys(p.mods || {}).forEach(function (id) {
            if (P(id)) state.mods[id] = p.mods[id];
        });
        state.conds = (p.conds || []).filter(function (id) { return !!conditionById(id); });
        state.plink = {};
        Object.keys(p.plink || {}).forEach(function (eid) {
            var L = p.plink[eid];
            if (L && (state.qty[eid] | 0) > 0) {
                if (L.t === 'cond' && state.conds.indexOf(L.id) >= 0) state.plink[eid] = { t: 'cond', id: L.id };
                if (L.t === 'piece' && present(L.id)) state.plink[eid] = { t: 'piece', id: L.id };
            }
        });
        var fn = document.getElementById('fabName');
        if (fn) fn.value = p.name;
        var fd = document.getElementById('fabDesc');
        if (fd) fd.value = p.desc;
        closeMenu();
        render();
    }

    function save() {
        try {
            var sel = buildSel();
            localStorage.setItem(SAVE_KEY, JSON.stringify({
                chassis: state.chassis, qty: state.qty, order: state.order, conds: state.conds, plink: state.plink,
                condSkill: state.condSkill,
                ensSkill: state.ensSkill || '',
                u7: sel.u7, u11: sel.u11, o28: sel.o28,
                mods: sel.mods,
                name: (document.getElementById('fabName') || {}).value || '',
                desc: (document.getElementById('fabDesc') || {}).value || ''
            }));
        } catch (e) { /* stockage indisponible */ }
    }
    function load() {
        try {
            var raw = localStorage.getItem(SAVE_KEY);
            if (!raw) return;
            var d = JSON.parse(raw);
            if (!d || typeof d !== 'object') return;
            if (d.qty && (d.qty.O14 | 0) > 0) {
                /* O14 supprimée : devient Repousse x3 + Sur critique liée. */
                delete d.qty.O14;
                if (d.mods) delete d.mods.O14;
                if (d.plink) delete d.plink.O14;
                d.qty.O13 = 3;
                if (d.order && d.order.indexOf && d.order.indexOf('O13') < 0) d.order.push('O13');
                d.conds = d.conds || [];
                if (d.conds.indexOf('critique') < 0) d.conds.push('critique');
                d.plink = d.plink || {};
                d.plink.O13 = { t: 'cond', id: 'critique' };
            }
            if (d.chassis && C(d.chassis)) state.chassis = d.chassis;
            if (d.qty) {
                Object.keys(d.qty).forEach(function (id) {
                    if (P(id)) state.qty[id] = Math.max(0, d.qty[id] | 0);
                });
            }
            if (d.order && d.order.forEach) {
                d.order.forEach(function (id) {
                    if (P(id) && (state.qty[id] | 0) > 0 && state.order.indexOf(id) < 0) state.order.push(id);
                });
            }
            if (typeof d.u7 === 'string') state.u7 = d.u7;
            if (typeof d.o28 === 'string') state.o28 = d.o28;
            if (d.u11 && typeof d.u11 === 'object') state.u11 = d.u11;
            if (typeof d.ensSkill === 'string' && d.ensSkill) {
                var okEns = false;
                SKILLS.forEach(function (grp) { if (grp[1].indexOf(d.ensSkill) >= 0) okEns = true; });
                if (okEns) state.ensSkill = d.ensSkill;
            } else if (!d.ensSkill && d.conds && d.conds.indexOf('compet') >= 0 && typeof d.condSkill === 'string' && d.condSkill) {
                /* Migration : l'ancienne carte 'Compétence entraînée' devient le lien d'ensemble. */
                var okMig = false;
                SKILLS.forEach(function (grp) { if (grp[1].indexOf(d.condSkill) >= 0) okMig = true; });
                if (okMig) state.ensSkill = d.condSkill;
            }
            if (d.mods && typeof d.mods === 'object') {
                Object.keys(d.mods).forEach(function (id) {
                    if (!P(id) || (state.qty[id] | 0) <= 0) return;
                    var m = d.mods[id] || {};
                    var rg = m.range | 0;
                    state.mods[id] = {
                        range: Math.max(0, Math.min(2, rg)),
                        zone: !!m.zone, dur: Math.max(0, Math.min(2, m.dur | 0)), cross: !!m.cross
                    };
                    if (!state.mods[id].range && !state.mods[id].zone && !state.mods[id].dur && !state.mods[id].cross)
                        delete state.mods[id];
                });
            } else if ((d.range | 0) || d.zone || (d.dur | 0) || d.cross) {
                /* Migration : les anciens modificateurs globaux rejoignent la 1re carte compatible. */
                var olds = { range: d.range | 0, zone: !!d.zone, dur: d.dur | 0, cross: !!d.cross };
                var targets = state.order.filter(function (id) { return P(id); });
                if (olds.range && !(olds.range === 2 || olds.range === 4)) olds.range = 0;
                olds.dur = Math.max(0, Math.min(2, olds.dur));
                var placed = false;
                targets.forEach(function (id) {
                    if (placed) return;
                    var pp = P(id);
                    var ok = true;
                    if (olds.range && !modRangeOk(pp).ok) ok = false;
                    if (olds.zone && !modZoneOk(pp).ok) ok = false;
                    if (olds.dur && !modDurOk(pp).ok) ok = false;
                    if (ok && (olds.range || olds.zone || olds.dur)) {
                        state.mods[id] = { range: olds.range, zone: olds.zone, dur: olds.dur, cross: false };
                        placed = true;
                    }
                });
                if (olds.cross && !placed && targets.length) {
                    targets.forEach(function (id) {
                        var cur = state.mods[id] || { range: 0, zone: false, dur: 0, cross: false };
                        cur.cross = true;
                        state.mods[id] = cur;
                    });
                } else if (olds.cross && placed) {
                    Object.keys(state.mods).forEach(function (id) { state.mods[id].cross = true; });
                }
            }
            if (typeof d.condSkill === 'string' && d.condSkill) {
                var okSkill = false;
                SKILLS.forEach(function (grp) { if (grp[1].indexOf(d.condSkill) >= 0) okSkill = true; });
                if (okSkill) state.condSkill = d.condSkill;
            }
            if (d.conds && d.conds.forEach) {
                d.conds.forEach(function (id) {
                    if (conditionById(id) && state.conds.indexOf(id) < 0) state.conds.push(id);
                });
            } else if ((d.narrow | 0) > 0) {
                /* Migration des sauvegardes à compteur : les premières conditions. */
                for (var mi = 0; mi < CONDITIONS.length && state.conds.length < (d.narrow | 0); mi++)
                    state.conds.push(CONDITIONS[mi].id);
            }
            if (d.plink && typeof d.plink === 'object') {
                Object.keys(d.plink).forEach(function (eid) {
                    var L = d.plink[eid];
                    if (!L || (state.qty[eid] | 0) <= 0) return;
                    if (L.t === 'cond' && state.conds.indexOf(L.id) >= 0) state.plink[eid] = { t: 'cond', id: L.id };
                    if (L.t === 'piece' && present(L.id)) state.plink[eid] = { t: 'piece', id: L.id };
                });
            }
            var fn = document.getElementById('fabName');
            if (fn && d.name) fn.value = d.name;
            var fd = document.getElementById('fabDesc');
            if (fd && d.desc) fd.value = d.desc;
            state._rest = d;
        } catch (e) { /* sauvegarde illisible */ }
    }
    function restoreInputs() {
        state._rest = null;
    }

    /* --- Événements (délégués). --- */
    var root = document.getElementById('fabPuzzle') || document;
    function up(t, sel) { return (t && t.closest) ? t.closest(sel) : null; }
    root.addEventListener('click', function (ev) {
        var t = ev.target;
        var pre = up(t, '[data-prepick]');
        if (pre) {
            if (addPiece(pre.dataset.prepick, true) && menu && menu.kind === 'piece')
                state.plink[menu.id] = { t: 'piece', id: pre.dataset.prepick };
            render();
            return;
        }
        var unc = up(t, '[data-uncond]');
        if (unc) { removeCond(unc.dataset.uncond); return; }
        var nd = up(t, '[data-needpick]');
        if (nd) {
            var effId = menu && menu.id;
            var parts = String(nd.dataset.needpick).split(':');
            var ok2 = false;
            if (parts[0] === 'cond') {
                if (conditionById(parts[1])) {
                    if (state.conds.indexOf(parts[1]) < 0) state.conds.push(parts[1]);
                    ok2 = true;
                }
            } else {
                ok2 = doPlace(parts[1]);
            }
            if (ok2 && effId) {
                state.plink[effId] = { t: parts[0], id: parts[1] };
                if (needFor(effId).length) openMenu('need', effId);
                else closeMenu();
            }
            render();
            return;
        }
        var nfd = up(t, '[data-needfor]');
        if (nfd) { openMenu('need', nfd.dataset.needfor, ev.clientX, ev.clientY); return; }
        var pick = up(t, '[data-pick]');
        if (pick) { addPiece(pick.dataset.pick); return; }
        var ens = up(t, '[data-ens-pick]');
        if (ens) { setChassis(ens.dataset.ensPick); return; }
        var unfit = up(t, '[data-unfit]');
        if (unfit) { removePiece(unfit.dataset.unfit); return; }
        var dec = up(t, '[data-dec]');
        if (dec) { bumpQty(dec.dataset.dec, -1); return; }
        var inc = up(t, '[data-inc]');
        if (inc) { bumpQty(inc.dataset.inc, 1); return; }
        if (up(t, '[data-preset]')) { applyPreset(up(t, '[data-preset]').dataset.preset); return; }
        if (up(t, '.fab-ctx-close')) { closeMenu(); return; }
        if (up(t, '[data-apply-auto]') || t.id === 'fabBtnAuto' || up(t, '#fabBtnAuto')) {
            var ni = document.getElementById('fabName');
            if (ni && lastAuto) { ni.value = lastAuto; render(); }
            return;
        }
        if (up(t, '[data-apply-desc]') || t.id === 'fabBtnDesc' || up(t, '#fabBtnDesc')) {
            var di = document.getElementById('fabDesc');
            if (di && lastDesc) { di.value = lastDesc; render(); }
            return;
        }
        var node = up(t, '[data-node]');
        if (node) {
            openMenu(node.dataset.node, node.dataset.id || null, ev.clientX, ev.clientY);
            return;
        }
        if (t.id === 'fabBtnReset' || up(t, '#fabBtnReset')) {
            fullReset(null);
            return;
        }
        if (t.id === 'fabBtnCopy' || up(t, '#fabBtnCopy')) {
            var r = render();
            var condNames = state.conds.map(function (cid) {
                var cd = conditionById(cid);
                if (!cd) return cid;
                return cd.id === 'compet' ? cd.nom + ' (' + state.condSkill + ')' : cd.nom;
            });
            var txt = '=== TECHNIQUE MAISON (Fabrique, Livre III §14) ===\n' +
                'Nom : ' + prev.name.textContent + '\n' +
                'Nature : ' + prev.type.textContent + '\n' +
                'Compétence liée : ' + (state.ensSkill || '—') + ' (entraînée)\n' +
                'Activation : ' + r.pa + ' PA · Création : ' + r.xp + ' XP' + (r.crossed ? ' (croisé)' : '') + '\n\n' +
                'Description : ' + prev.desc.textContent + '\n\n' +
                'Prérequis : ' + (condNames.length ? condNames.join(', ') + ' (gratuits)' : 'aucun') + '\n\nDétail :\n' +
                r.lines.map(function (l) {
                    var lk = state.plink[l.id];
                    var via = '';
                    if (lk) {
                        if (lk.t === 'cond') {
                            var cd = conditionById(lk.id);
                            via = ' — via ' + (cd ? cd.nom : lk.id);
                        } else {
                            via = ' — via ' + lk.id;
                        }
                    }
                    return '- ' + l.label + ' (' + l.xp + ' XP + ' + l.pa + ' PA)' + via;
                }).join('\n') +
                (r.errors.length ? '\n\nÀ corriger :\n' + r.errors.map(function (e) { return '- ' + e; }).join('\n') : '') +
                (r.warnings.length ? '\n\nNotes MJ :\n' + r.warnings.map(function (w) { return '- ' + w; }).join('\n') : '') +
                '\n============================================';
            if (navigator.clipboard) navigator.clipboard.writeText(txt).then(function () {
                toast('Fiche de technique copiée !');
            });
            return;
        }
        if (menu && !up(t, '.fab-ctx')) closeMenu();
    });
    /* Clic droit sur le graphe = même menu, comme un nœud KT. */
    graphEl.addEventListener('contextmenu', function (ev) {
        ev.preventDefault();
        var node = up(ev.target, '[data-node]');
        if (node) openMenu(node.dataset.node, node.dataset.id || null, ev.clientX, ev.clientY);
        else openMenu('slot', null, ev.clientX, ev.clientY);
    });
    root.addEventListener('change', function (ev) {
        var t = ev.target;
        if (!t || !t.dataset) return;
        if (t.dataset.condskill) { state.condSkill = t.value; render(); return; }
        if (t.dataset.ensSkill) { state.ensSkill = t.value || ''; render(); return; }
        if (t.dataset.varU7) { state.u7 = t.value; render(); return; }
        if (t.dataset.varO28) { state.o28 = t.value; render(); return; }
        if (t.dataset.varU11) { toggleU11(t.dataset.sub); return; }
        if (t.dataset.modRange !== undefined) {
            var mr = t.dataset.mid;
            if (mr && P(mr)) { setMod(mr, { range: (parseInt(t.value, 10) || 1) - 1 }); render(); }
            return;
        }
        if (t.dataset.modZone !== undefined) {
            var mz = t.dataset.mid;
            if (mz && P(mz)) { setMod(mz, { zone: !!t.checked }); render(); }
            return;
        }
        if (t.dataset.modDur !== undefined) {
            var md = t.dataset.mid;
            if (md && P(md)) {
                var v = Math.max(0, Math.min(2, parseInt(t.value, 10) || 0));
                t.value = String(v);
                setMod(md, { dur: v }); render();
            }
            return;
        }
        if (t.dataset.modCross !== undefined) {
            var mx = t.dataset.mid;
            if (mx && P(mx)) { setMod(mx, { cross: !!t.checked }); render(); }
            return;
        }
        if (t.dataset.optCond !== undefined) {
            var mo = t.dataset.mid;
            if (mo && P(mo)) {
                if (t.checked) {
                    if (state.conds.indexOf(t.dataset.optCond) < 0) state.conds.push(t.dataset.optCond);
                    state.plink[mo] = { t: 'cond', id: t.dataset.optCond };
                } else if (state.plink[mo] && state.plink[mo].t === 'cond' && state.plink[mo].id === t.dataset.optCond) {
                    delete state.plink[mo];
                    var still = false;
                    Object.keys(state.plink).forEach(function (e) {
                        if (state.plink[e].t === 'cond' && state.plink[e].id === t.dataset.optCond) still = true;
                    });
                    if (!still) state.conds = state.conds.filter(function (x) { return x !== t.dataset.optCond; });
                }
                render();
            }
            return;
        }
        if (t.classList && t.classList.contains('fab-input')) { render(); return; }
    });
    root.addEventListener('input', function (ev) {
        var t = ev.target;
        if (!t) return;
        if (t.dataset && t.dataset.search !== undefined && menu) {
            menu.search = t.value || '';
            renderMenu(undefined, undefined, true);
            return;
        }
        /* Listes et cases : on attend le 'change' (commit). Re-rendre ici
           détruirait le contrôle en pleine interaction et le 'change'
           partirait dans le vide. */
        if (t.dataset && (t.dataset.modDur !== undefined || t.dataset.modRange !== undefined ||
            t.dataset.modZone !== undefined || t.dataset.modCross !== undefined)) return;
        if (t.tagName === 'SELECT' || t.type === 'checkbox' || t.type === 'radio') return;
        if (t.classList && t.classList.contains('fab-input')) { render(); return; }
        if (t && (t.id === 'fabName' || t.id === 'fabDesc')) render();
    });
    document.addEventListener('keydown', function (ev) {
        if (ev.key === 'Escape' && menu) { closeMenu(); return; }
        if ((ev.key === 'Enter' || ev.key === ' ') && ev.target && ev.target.dataset && (ev.target.dataset.pick || ev.target.dataset.needpick)) {
            ev.target.click();
            ev.preventDefault();
            return;
        }
        if ((ev.key === 'Enter' || ev.key === ' ') && ev.target && ev.target.dataset && ev.target.dataset.node) {
            var b = ev.target.getBoundingClientRect();
            openMenu(ev.target.dataset.node, ev.target.dataset.id || null, b.left + 20, b.top + 20);
            ev.preventDefault();
            return;
        }
        if (ev.key === 'Enter' && menu && (menu.kind === 'slot' || menu.kind === 'root' || menu.kind === 'need')) {
            var ae = document.activeElement;
            if (ae && ae.dataset && ae.dataset.search !== undefined) {
                if (menu.kind === 'slot') {
                    var first = firstLegal();
                    if (first) addPiece(first);
                } else if (menu.kind === 'need') {
                    var firstN = firstNeed();
                    if (firstN) {
                        var btn = ctxEl.querySelector('[data-needpick="' + firstN.t + ':' + firstN.id + '"]');
                        if (btn) btn.click();
                    }
                } else {
                    var firstCh = null;
                    var q = normSearch(menu.search);
                    for (var i = 0; i < CAT.chassis.length; i++) {
                        if (!q || normSearch(CAT.chassis[i].nom + ' ' + CAT.chassis[i].id).indexOf(q) >= 0) {
                            firstCh = CAT.chassis[i].id;
                            break;
                        }
                    }
                    if (firstCh) setChassis(firstCh);
                }
                ev.preventDefault();
            }
        }
    });

    /* --- Montage initial. --- */
    load();
    buildModifs();
    restoreInputs();
    /* U11 restaurée depuis la sauvegarde. */
    (function () {
        var any = false;
        Object.keys(state.u11).forEach(function (v) { if (state.u11[v]) any = true; });
        if (any && (state.qty.U11 | 0) === 0 && P('U11')) {
            state.order.push('U11');
            state.qty.U11 = 1;
            state.u11auto = true;
        }
    })();
    render();
    if (window.__FABDEBUG) window.__FABDEBUG.state = state;
}

FabriqueEngine.modRangeOk = modRangeOk;
FabriqueEngine.modZoneOk = modZoneOk;
FabriqueEngine.modDurOk = modDurOk;
FabriqueEngine.modCrossOk = modCrossOk;
FabriqueEngine.suggestName = suggestName;
FabriqueEngine.NAME_BASE = NAME_BASE;
FabriqueEngine.suggestDesc = suggestDesc;
FabriqueEngine.fabLinkText = fabLinkText;
if (typeof window !== 'undefined') {
    window.FabriqueEngine = FabriqueEngine;
    window.initFabrique = initFabrique;
}
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { FabriqueEngine: FabriqueEngine };
}
})();
