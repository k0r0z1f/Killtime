/* ==========================================================================
   FABRIQUE DE TECHNIQUES — ATELIER GRAPHE (Livre III chap-14, Livre IX chap-40).
   Moteur pur FabriqueEngine.compute (inchangé : châssis + 1-3 pièces,
   paliers en filière, M1-M7, hybride auto, croisé ×2, plancher 4,
   signature 7-8, plafond PA 6, incompatibilités, prérequis) + initFabrique()
   qui monte le graphe depuis data/livre_3.json (mecaniques.fabrique).
   Graphe : 1 ensemble (racine ◆) (châssis) + pièces qui s'emboîtent dans 3 logements.
   Les filières (O4→O5→O6, O13→O14) sont des grosses pièces : elles prennent
   2-3 logements d'un coup, prérequis inclus.
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
    O1:  { t: 'n', max: 2, unit: 'crans' },
    O13: { t: 'n', max: 3, unit: 'cases' },
    D8:  { t: 'n', max: 3, unit: 'malus' },
    S1:  { t: 'n', max: 3, unit: 'crans' },
    O19: { t: 'radio', opts: [
        { v: 'partiel', label: 'Partiel ignoré (2 XP)', xp: 2, pa: 0 },
        { v: 'total', label: 'Total à −2 (3 XP)', xp: 3, pa: 0 },
        { v: 'suppression', label: 'Cible sans couvert 1 tour (3 XP + 1 PA)', xp: 3, pa: 1 }
    ]},
    U7: { t: 'palier', opts: [
        { v: '5', label: '5 PV + purge (inclus)', dx: 0 },
        { v: '10', label: '10 PV (+2 XP)', dx: 2 },
        { v: '15', label: '15 PV + immunité 3 tours (+3 XP)', dx: 3 }
    ]},
    U11: { t: 'paliers', opts: [
        { v: 'p3', label: '3 ESS (+2 XP)', dx: 2, dpa: 0 },
        { v: 'p4', label: '4 ESS + 1 PA immédiat (+2 XP + 1 PA)', dx: 2, dpa: 1 },
        { v: 'p2x', label: '2×/combat (+2 XP)', dx: 2, dpa: 0 }
    ]}
};

/* Paliers qui REMPLACENT leur prérequis (filière payée une fois). */
var REPLACE = { O5: ['O4'], O6: ['O5', 'O4'], O14: ['O13'] };

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
        var majeures = 0, auto = {};

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

        /* 1. Châssis. */
        var ch = sel.chassis ? C(sel.chassis) : null;
        if (!ch) {
            errors.push('Choisis un ensemble : il dit quand ta technique part.');
        } else {
            lines.push({ id: 'chassis', label: 'Ensemble — ' + ch.nom, xp: toInt(ch.cout_xp), pa: toInt(ch.cout_pa), kind: 'chassis' });
        }

        /* 2. Pré-requis auto (filières linéaires). */
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
            if (p.majeure) majeures++;
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
                lines.push({ id: id, label: p.nom + ' (filière ' + chain.slice().reverse().join(' → ') + ', prérequis inclus)', xp: c.xp, pa: c.pa, kind: 'piece', fam: p.famille });
                chain.forEach(function (x) { handled[x] = true; });
                return;
            }
            handled[id] = true;
            if (id === 'O19') {
                var opt = null;
                (KIND.O19.opts).forEach(function (o) { if (o.v === sel.o19) opt = o; });
                opt = opt || KIND.O19.opts[0];
                lines.push({ id: id, label: p.nom + ' — ' + opt.label, xp: opt.xp, pa: opt.pa, kind: 'piece', fam: p.famille });
                return;
            }
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
            var k = KIND[id];
            if (k && k.t === 'n') {
                var xn = fabNum(p.cout_xp), pn = fabNum(p.cout_pa);
                lines.push({ id: id, label: p.nom + ' ×' + q, xp: (xn.b + xn.d) * q, pa: (pn.b + pn.d) * q, kind: 'piece', fam: p.famille });
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
        if (has('O17') && sel.chassis !== 'C3')
            errors.push('Contre-attaque / interception (O17) exige l\u2019ensemble Réaction.');
        if ((has('D1') || has('D3')) === false && has('D8'))
            errors.push('Ignore un malus (D8) exige D1 ou D3 (filière défensive).');
        if (has('O12') && !(has('O10') || has('O11')))
            warnings.push('Statut critique (O12) : prérequis filière = un statut de la même famille (O10 ou O11).');
        if (has('U5'))
            warnings.push('Téléport 2 cases (U5) : prérequis narratif = Roulade ou esquive Δ≥2.');
        if (has('O18') && counted < 3)
            warnings.push('Critique élargi (O18) : prérequis filière = 3 pièces de la même arme.');
        if (has('O8') && has('O9'))
            warnings.push('Règle 5 : un seul bonus chiffré par jet — O8 et O9 ne se cumulent pas.');
        if (ch && ch.id === 'C0') {
            var activePA = 0;
            lines.forEach(function (l) { if (l.kind === 'piece') activePA += l.pa; });
            if (activePA > 0)
                warnings.push('Ensemble passif + pièces à PA : passe en action principale, ou justifie auprès du MJ.');
        }
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

        /* 6. Modificateurs. */
        if (sel.range === 2) lines.push({ id: 'M1', label: 'Portée +2 cases', xp: 1, pa: 0, kind: 'mod' });
        if (sel.range === 4) lines.push({ id: 'M2', label: 'Portée +4 cases', xp: 2, pa: 0, kind: 'mod' });
        if (sel.zone) lines.push({ id: 'M3', label: 'Forme de zone', xp: 2, pa: 1, kind: 'mod' });
        if ((sel.dur | 0) > 0) lines.push({ id: 'M4', label: 'Durée +' + sel.dur + ' tour(s)', xp: sel.dur | 0, pa: sel.dur | 0, kind: 'mod' });

        /* 7. Hybride auto (M6). */
        var famKeys = Object.keys(fams);
        if (famKeys.length > 1)
            lines.push({ id: 'M6', label: 'Hybride (' + famKeys.join(' + ') + ')', xp: 1, pa: 0, kind: 'mod' });

        /* 8. Totaux. */
        var xp = 0, pa = 0;
        lines.forEach(function (l) { xp += l.xp; pa += l.pa; });
        var crossed = false;
        if (sel.cross) { xp = xp * 2; crossed = true; }
        var floored = false;
        if (xp < 4 && (lines.length > 0)) { xp = 4; floored = true; }

        var status = 'standard';
        if (xp >= 7 && xp <= 8) {
            status = 'signature';
            warnings.push('Signature 7–8 XP : une seule par personnage, accord MJ obligatoire.');
            if (majeures > 2) errors.push('2 majeures maximum, même en signature.');
        } else if (majeures > 1) {
            errors.push(majeures + ' majeures : une seule par technique standard (règle 8).');
        }
        if (xp > 8) errors.push('Plafond 8 XP : découpe en deux techniques sœurs.');
        if (pa > 6) errors.push('Activation ' + pa + ' PA > 6 : ça doit tenir dans un tour.');
        if (status !== 'signature' && xp >= 4 && xp <= 6) status = 'standard';
        if (floored) warnings.push('Plancher appliqué : une technique coûte toujours 4 XP minimum.');

        return { xp: xp, pa: pa, lines: lines, errors: errors, warnings: warnings,
                 familles: famKeys, majeures: majeures, crossed: crossed,
                 status: errors.length ? 'invalide' : status };
    }
};

/* ---------------------------------------------------------------- PUZZLE */
// Filières = grosses pièces (prérequis inclus, 2-3 logements).
var CHAINS = { O5: ['O4', 'O5'], O6: ['O4', 'O5', 'O6'], O14: ['O13', 'O14'] };
var CHAIN_KEY = { O4: 'perce', O5: 'perce', O6: 'perce', O13: 'proj', O14: 'proj' };
var INCOMP = [['O7', 'O15'], ['D6', 'D1'], ['D6', 'D2'], ['D6', 'D3'], ['D6', 'D4']];
var FAM_LABEL = { offensif: 'Offensif', defensif: 'Défensif', utilitaire: 'Utilitaire', social: 'Social' };
var SAVE_KEY = 'fabrique-puzzle-v1';

function chainOf(id) { return CHAINS[id] || [id]; }
function spanOf(id) { return chainOf(id).length; }

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
    var state = { chassis: null, qty: {}, order: [],
                  o19: 'partiel', u7: '5', u11: {},
                  narrow: 0, range: 0, zone: false, dur: 0, cross: false };
    var menu = null; /* {kind:'slot'|'piece'|'root', id, search} */
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
    function majeuresCount() {
        var n = 0;
        state.order.forEach(function (id) {
            chainOf(id).forEach(function (x) { var p = P(x); if (p && p.majeure) n++; });
        });
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

    /* Pourquoi ce choix n'est pas possible ici (ou avec un avertissement). */
    function lockInfo(p) {
        var id = p.id;
        var k = KIND[id];
        var q = state.qty[id] | 0;
        if (!state.chassis) return { lock: true, reason: 'Choisis d\u2019abord un ensemble (◆).' };
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
        if (id === 'O17' && state.chassis !== 'C3')
            return { lock: true, reason: 'O17 ne se branche que sur l\u2019ensemble Réaction.' };
        if (id === 'D8' && !present('D1') && !present('D3'))
            return { lock: true, reason: 'Demande D1 ou D3 sur le graphe.' };
        var bad = incompWith(id);
        if (bad) return { lock: true, reason: 'Refuse ' + bad + ' (règle 8).' };
        if (p.majeure && majeuresCount() >= 2)
            return { lock: true, reason: '2 majeures maximum, même en signature.' };
        var need = spanOf(id);
        if (CHAIN_KEY[id]) {
            state.order.forEach(function (t) {
                if (CHAIN_KEY[t] === CHAIN_KEY[id]) need = spanOf(id) - spanOf(t);
            });
            if (need < 0) need = 0;
        }
        if (placedSpan() + need > 3)
            return { lock: true, reason: 'Plus de logement libre (3 max).' };
        if (id === 'O12' && !present('O10') && !present('O11'))
            return { lock: false, soft: 'Demande O10 ou O11 sur le graphe.' };
        if (id === 'O18' && placedSpan() + need < 3)
            return { lock: false, soft: 'Demande 3 pièces de la même arme.' };
        if (id === 'U5')
            return { lock: false, soft: 'Prérequis narratif : Roulade ou esquive Δ≥2.' };
        if (p.majeure && majeuresCount() >= 1)
            return { lock: false, soft: '2e majeure = signature 7–8 XP, MJ obligatoire.' };
        return { lock: false };
    }

    function addPiece(id) {
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
        closeMenu();
        render();
        return true;
    }
    function removePiece(id) {
        delete state.qty[id];
        state.order = state.order.filter(function (t) { return t !== id; });
        closeMenu();
        render();
    }
    function setChassis(id) {
        state.chassis = (state.chassis === id) ? null : id;
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
            state.order = state.order.filter(function (t) { return t !== 'U11'; });
            state.u11auto = false;
        }
        render();
    }

    /* --- Lecture des finitions (dans le DOM, construites une fois). --- */
    function buildSel() {
        var sel = { chassis: state.chassis, qty: {}, o19: state.o19, u7: state.u7, u11: {},
                    narrow: 0, range: 0, zone: false, dur: 0, cross: false };
        Object.keys(state.qty).forEach(function (id) { sel.qty[id] = state.qty[id]; });
        Object.keys(state.u11).forEach(function (v) { if (state.u11[v]) sel.u11[v] = true; });
        var rr = document.querySelector('input[name="fabRange"]:checked');
        sel.range = rr ? (parseInt(rr.value, 10) || 0) : 0;
        var zo = document.getElementById('fabZone');
        sel.zone = !!(zo && zo.checked);
        var du = document.getElementById('fabDur');
        sel.dur = du ? Math.max(0, Math.min(2, parseInt(du.value, 10) || 0)) : 0;
        var na = document.getElementById('fabNarrow');
        sel.narrow = na ? Math.max(0, parseInt(na.value, 10) || 0) : 0;
        var cx = document.getElementById('fabCross');
        sel.cross = !!(cx && cx.checked);
        return sel;
    }

    /* --- 2. Finitions (modificateurs). --- */
    function buildModifs() {
        var mod = document.getElementById('fabModifs');
        if (!mod) return;
        mod.innerHTML = '';
        var r = el('div', 'fab-mod', '<b>Portée (M1/M2)</b>');
        [['0', 'Aucune (+0)'], ['2', '+2 cases (+1 XP)'], ['4', '+4 cases (+2 XP)']].forEach(function (o, i) {
            var l = el('label', '', '<input type="radio" name="fabRange" value="' + o[0] + '"' +
                (i === 0 ? ' checked' : '') + ' class="fab-input"> ' + o[1]);
            r.appendChild(l);
        });
        mod.appendChild(r);
        var z = el('div', 'fab-mod', '<b>Zone (M3 · +2 XP +1 PA)</b>');
        var zl = el('label', '', '<input type="checkbox" id="fabZone" class="fab-input"> Forme de zone : cône, ligne ou rayon +1');
        z.appendChild(zl);
        mod.appendChild(z);
        var d = el('div', 'fab-mod', '<b>Durée +1 tour (M4 · +1 XP +1 PA par cran)</b>');
        var dn = document.createElement('input');
        dn.type = 'number'; dn.min = '0'; dn.max = '2'; dn.value = '0'; dn.id = 'fabDur';
        dn.className = 'tool-input fab-input'; dn.style.width = '90px';
        d.appendChild(dn);
        mod.appendChild(d);
        var n = el('div', 'fab-mod', '<b>Conditions étroites (M5 · −1 XP chacune)</b>');
        var nr = document.createElement('input');
        nr.type = 'number'; nr.min = '0'; nr.max = '4'; nr.value = '0'; nr.id = 'fabNarrow';
        nr.className = 'tool-input fab-input'; nr.style.width = '90px';
        n.appendChild(nr);
        var nh = el('span', '', 'Critique, Δ≥2, immobile, 1/combat… (plancher 1 XP/pièce)');
        nh.style.cssText = 'font-size:0.72rem;opacity:.75;display:block;margin-top:4px;';
        n.appendChild(nh);
        mod.appendChild(n);
        var cr = el('div', 'fab-mod', '<b>Hors-compétence (M7 · ×2 XP)</b>');
        var cl = el('label', '', '<input type="checkbox" id="fabCross" class="fab-input"> Banque croisée (comme le croisé ×2)');
        cr.appendChild(cl);
        mod.appendChild(cr);
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
        if (id === 'O19') {
            var o = null;
            KIND.O19.opts.forEach(function (x) { if (x.v === state.o19) o = x; });
            return o ? o.label : '';
        }
        if (id === 'U7') return 'palier ' + state.u7 + ' PV';
        if (id === 'U11') {
            var on = Object.keys(state.u11).filter(function (v) { return state.u11[v]; });
            return on.length ? 'paliers ' + on.join(', ') : 'base';
        }
        return '';
    }

    function renderGraph(sel) {
        nodesEl.innerHTML = '';
        wiresEl.innerHTML = '';
        var ch = sel.chassis ? C(sel.chassis) : null;
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
            root.innerHTML = '<span class="fab-ens-top"><span class="fab-ens-id">◆ ' + esc(ch.id) + '</span>' +
                '<span class="fab-ens-cost">' + esc(ch.cout_xp) + ' XP + ' + esc(ch.cout_pa) + ' PA</span></span>' +
                '<strong>' + esc(ch.nom) + '</strong><small>' + esc(ch.cadre) + '</small>' + dots;
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
        state.order.forEach(function (id) {
            var p = P(id);
            if (!p) return;
            var box = el('div', 'fab-placed fam-' + p.famille, '');
            box.setAttribute('role', 'button');
            box.setAttribute('tabindex', '0');
            box.dataset.node = 'piece';
            box.dataset.id = id;
            var head = '<div class="fab-placed-head"><strong>' + esc(p.nom) + '</strong>' +
                '<span class="fab-placed-cost">' + esc(p.cout_xp) + ' XP + ' + esc(p.cout_pa) + ' PA</span></div>';
            var sub = esc(p.cond || p.texte || '');
            var vs = variantSummary(id);
            if (vs) sub += ' — ' + esc(vs);
            if ((state.qty[id] | 0) > 1) sub += ' — ×' + (state.qty[id] | 0);
            var flags = '<div class="fab-placed-flags">';
            chainOf(id).forEach(function (x) { flags += '<span class="fab-flag">' + esc(x) + '</span>'; });
            if (p.majeure) flags += '<span class="fab-flag star">★ majeure</span>';
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
            var x1 = rb.right - gr.left, y1 = rb.top + rb.height / 2 - gr.top;
            var rows = branchcol.children;
            for (var ri = 0; ri < rows.length; ri++) {
                var bb = rows[ri].getBoundingClientRect();
                var x2 = bb.left - gr.left, y2 = bb.top + bb.height / 2 - gr.top;
                var path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
                var isSlot = rows[ri].dataset && rows[ri].dataset.node === 'slot';
                var fam = null;
                if (!isSlot) {
                    var pid = rows[ri].dataset ? rows[ri].dataset.id : null;
                    var pp = pid ? P(pid) : null;
                    fam = pp ? FAM_COLOR[pp.famille] : null;
                }
                path.setAttribute('d', 'M ' + x1 + ' ' + y1 + ' C ' + (x1 + 50) + ' ' + y1 + ', ' + (x2 - 50) + ' ' + y2 + ', ' + x2 + ' ' + y2);
                path.setAttribute('fill', 'none');
                path.setAttribute('stroke', fam || 'rgba(255,255,255,0.22)');
                path.setAttribute('stroke-width', '2');
                if (isSlot) path.setAttribute('stroke-dasharray', '5 4');
                wiresEl.appendChild(path);
            }
        } catch (e) { /* mesure indisponible : graphe sans fils */ }
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
        var b = el('button', 'fab-opt fam-' + (fam || p.famille), '');
        b.type = 'button';
        var h = '<span class="fab-opt-top"><strong>' + esc(p.nom) + '</strong>' +
            '<span class="fab-opt-cost">' + esc(p.id) + ' · ' + esc(p.cout_xp) + ' XP + ' + esc(p.cout_pa) + ' PA</span></span>';
        if (p.prerequis && p.prerequis.length)
            h += '<div class="fab-opt-req"><span class="rq">Demande ' + esc(p.prerequis.join(', ')) + '</span></div>';
        if (p.incompatible && p.incompatible.length)
            h += '<div class="fab-opt-req"><span class="rf">Refuse ' + esc(p.incompatible.join(', ')) + '</span></div>';
        if (info.lock) {
            b.disabled = true;
            h += '<div class="fab-opt-lock">🔒 ' + esc(info.reason) + '</div>';
        } else if (info.soft) {
            b.dataset.pick = p.id;
            h += '<div class="fab-opt-soft">⚠ ' + esc(info.soft) + '</div>';
        } else {
            b.dataset.pick = p.id;
        }
        b.innerHTML = h;
        return b;
    }
    function renderMenu(x, y, focus) {
        if (!menu || !ctxEl) return;
        ctxEl.innerHTML = '';
        var head = el('div', 'fab-ctx-head', '');
        var title = menu.kind === 'root' ? '◆ Ensemble → ?' :
                    menu.kind === 'piece' ? esc(menu.id || '') + ' → ?' : '⊕ Logement → ?';
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
            list.appendChild(el('div', 'fab-ctx-foot', 'Échap = annuler · Entrée = premier'));
        } else if (menu.kind === 'slot') {
            var s2 = document.createElement('input');
            s2.className = 'fab-ctx-search';
            s2.setAttribute('data-search', '1');
            s2.placeholder = 'Filtrer les 45 pièces…';
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
        } else if (menu.kind === 'piece') {
            var p = P(menu.id);
            if (!p) { closeMenu(); return; }
            var info = lockInfo(p);
            var k = KIND[p.id];
            if (k && (p.id === 'O19' || p.id === 'U7' || p.id === 'U11')) {
                list.appendChild(el('div', 'fab-ctx-sec', 'Variante'));
                var vv = el('div', 'fab-ctx-var', '');
                if (p.id === 'O19') {
                    KIND.O19.opts.forEach(function (o) {
                        var l = el('label', '', '<input type="radio" name="fabm_O19" value="' + o.v + '"' +
                            (state.o19 === o.v ? ' checked' : '') + ' data-var-o19="1"> ' + esc(o.label));
                        vv.appendChild(l);
                    });
                } else if (p.id === 'U7') {
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
                } else if (p.id === 'U11') {
                    KIND.U11.opts.forEach(function (o) {
                        var l = el('label', '', '<input type="checkbox" data-var-u11="1" data-sub="' + o.v + '"' +
                            (state.u11[o.v] ? ' checked' : '') + '> ' + esc(o.label));
                        vv.appendChild(l);
                    });
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
            list.appendChild(el('div', 'fab-ctx-sec', 'Nœud'));
            var rr2 = el('div', 'fab-ctx-row', '');
            var del = el('button', 'fab-mini-btn', '⤴ Retirer ce nœud');
            del.type = 'button';
            del.dataset.unfit = p.id;
            rr2.appendChild(del);
            list.appendChild(rr2);
            var meta = [];
            if (p.seuil) meta.push('Seuil : ' + p.seuil);
            if (p.portee) meta.push('Portée : ' + p.portee);
            if (p.prerequis && p.prerequis.length) meta.push('Demande : ' + p.prerequis.join(', '));
            if (p.incompatible && p.incompatible.length) meta.push('Refuse : ' + p.incompatible.join(', '));
            if (p.majeure) meta.push('★ majeure');
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
                r.warnings.push('Opportunité : ajoute un déclencheur étroit (Δ≥2, critique, esquive…) ou une condition M5.');
        }
        if (menu && menu.kind === 'piece' && !present(menu.id)) menu = null;
        renderGraph(sel);
        renderMenu();
        var name = (document.getElementById('fabName') || {}).value || '';
        var desc = (document.getElementById('fabDesc') || {}).value || '';
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
            li.textContent = l.label + ' — ' + l.xp + ' XP + ' + l.pa + ' PA' + (l.narrow ? ' (−1 étroite)' : '');
            prev.list.appendChild(li);
        });
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
        save();
        return r;
    }

    /* --- Presets (montages vérifiés du Livre III §14). --- */
    var PRESETS = {
        hache: { chassis: 'C0', qty: { O2: 1 }, name: 'Hache de Guerre (canonique)', desc: 'Sur succès net : +2 dégâts.' },
        suture: { chassis: 'C0', qty: { U1: 1 }, name: 'Suture Réflexe (canonique)', desc: 'Les premiers soins d’urgence passent de 3 à 2 PA.' },
        miroir: { chassis: 'C3', qty: { D1: 1 }, narrow: 1, name: 'Lame Miroir (canonique)', desc: 'Tu pares les tirs laser au contact (Delta ≥ 0).' },
        fente: { chassis: 'C2', qty: { O8: 1, O10: 1 }, name: 'Fente Basse', desc: 'Tu passes sous la garde et tu fauches le jarret.' }
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
        state.o19 = 'partiel'; state.u7 = '5'; state.u11 = {};
        var na = document.getElementById('fabNarrow');
        if (na) na.value = String(p.narrow || 0);
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
                chassis: state.chassis, qty: state.qty, order: state.order,
                o19: sel.o19, u7: sel.u7, u11: sel.u11,
                narrow: sel.narrow, range: sel.range, zone: sel.zone, dur: sel.dur, cross: sel.cross,
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
            if (typeof d.o19 === 'string') state.o19 = d.o19;
            if (typeof d.u7 === 'string') state.u7 = d.u7;
            if (d.u11 && typeof d.u11 === 'object') state.u11 = d.u11;
            var fn = document.getElementById('fabName');
            if (fn && d.name) fn.value = d.name;
            var fd = document.getElementById('fabDesc');
            if (fd && d.desc) fd.value = d.desc;
            state._rest = d;
        } catch (e) { /* sauvegarde illisible */ }
    }
    function restoreInputs() {
        var d = state._rest;
        if (!d) return;
        var rr = document.querySelector('input[name="fabRange"][value="' + (d.range | 0) + '"]');
        if (rr) rr.checked = true;
        var zo = document.getElementById('fabZone');
        if (zo) zo.checked = !!d.zone;
        var du = document.getElementById('fabDur');
        if (du) du.value = String(d.dur | 0);
        var na = document.getElementById('fabNarrow');
        if (na) na.value = String(d.narrow | 0);
        var cx = document.getElementById('fabCross');
        if (cx) cx.checked = !!d.cross;
        state._rest = null;
    }

    /* --- Événements (délégués). --- */
    var root = document.getElementById('fabPuzzle') || document;
    function up(t, sel) { return (t && t.closest) ? t.closest(sel) : null; }
    root.addEventListener('click', function (ev) {
        var t = ev.target;
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
        var node = up(t, '[data-node]');
        if (node) {
            openMenu(node.dataset.node, node.dataset.id || null, ev.clientX, ev.clientY);
            return;
        }
        if (t.id === 'fabBtnReset' || up(t, '#fabBtnReset')) {
            state.chassis = null;
            state.qty = {};
            state.order = [];
            state.o19 = 'partiel'; state.u7 = '5'; state.u11 = {};
            state.u11auto = false;
            var rr0 = document.querySelector('input[name="fabRange"][value="0"]');
            if (rr0) rr0.checked = true;
            ['fabZone', 'fabCross'].forEach(function (id) {
                var c = document.getElementById(id);
                if (c) c.checked = false;
            });
            ['fabDur', 'fabNarrow'].forEach(function (id) {
                var n = document.getElementById(id);
                if (n) n.value = '0';
            });
            try { localStorage.removeItem(SAVE_KEY); } catch (e) { /* vide */ }
            closeMenu();
            render();
            return;
        }
        if (t.id === 'fabBtnCopy' || up(t, '#fabBtnCopy')) {
            var r = render();
            var txt = '=== TECHNIQUE MAISON (Fabrique, Livre III §14) ===\n' +
                'Nom : ' + prev.name.textContent + '\n' +
                'Nature : ' + prev.type.textContent + '\n' +
                'Activation : ' + r.pa + ' PA · Création : ' + r.xp + ' XP' + (r.crossed ? ' (croisé)' : '') + '\n\n' +
                'Description : ' + prev.desc.textContent + '\n\nDétail :\n' +
                r.lines.map(function (l) { return '- ' + l.label + ' (' + l.xp + ' XP + ' + l.pa + ' PA)'; }).join('\n') +
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
        if (t.dataset.varO19) { state.o19 = t.value; render(); return; }
        if (t.dataset.varU7) { state.u7 = t.value; render(); return; }
        if (t.dataset.varU11) { toggleU11(t.dataset.sub); return; }
    });
    root.addEventListener('input', function (ev) {
        var t = ev.target;
        if (!t) return;
        if (t.dataset && t.dataset.search !== undefined && menu) {
            menu.search = t.value || '';
            renderMenu(undefined, undefined, true);
            return;
        }
        if (t.classList && t.classList.contains('fab-input')) { render(); return; }
        if (t && (t.id === 'fabName' || t.id === 'fabDesc')) render();
    });
    document.addEventListener('keydown', function (ev) {
        if (ev.key === 'Escape' && menu) { closeMenu(); return; }
        if ((ev.key === 'Enter' || ev.key === ' ') && ev.target && ev.target.dataset && ev.target.dataset.node) {
            var b = ev.target.getBoundingClientRect();
            openMenu(ev.target.dataset.node, ev.target.dataset.id || null, b.left + 20, b.top + 20);
            ev.preventDefault();
            return;
        }
        if (ev.key === 'Enter' && menu && (menu.kind === 'slot' || menu.kind === 'root')) {
            var ae = document.activeElement;
            if (ae && ae.dataset && ae.dataset.search !== undefined) {
                if (menu.kind === 'slot') {
                    var first = firstLegal();
                    if (first) addPiece(first);
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
}

if (typeof window !== 'undefined') {
    window.FabriqueEngine = FabriqueEngine;
    window.initFabrique = initFabrique;
}
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { FabriqueEngine: FabriqueEngine };
}
})();
