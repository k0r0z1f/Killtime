/* ==========================================================================
   FABRIQUE DE TECHNIQUES — ATELIER PUZZLE (Livre III chap-14, Livre IX chap-40).
   Moteur pur FabriqueEngine.compute (inchangé : châssis + 1-3 pièces,
   paliers en filière, M1-M7, hybride auto, croisé ×2, plancher 4,
   signature 7-8, plafond PA 6, incompatibilités, prérequis) + initFabrique()
   qui monte le puzzle depuis data/livre_3.json (mecaniques.fabrique).
   Puzzle : 1 ensemble (châssis) + pièces qui s'emboîtent dans 3 logements.
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

function initFabrique() {
    var host = document.getElementById('fabChassis');
    if (!host) return;
    var CAT = window.CodexData && window.CodexData.l3 &&
              window.CodexData.l3.mecaniques && window.CodexData.l3.mecaniques.fabrique;
    if (!CAT) {
        host.innerHTML = '<div class="callout warning"><div class="callout-body">' +
            'Données de la Fabrique introuvables (data/livre_3.json). Ouvre cette page via le site, pas en fichier seul.' +
            '</div></div>';
        return;
    }

    /* --- État : source unique, le DOM ne fait que l'afficher. --- */
    var state = { chassis: null, qty: {}, order: [],
                  narrow: 0, range: 0, zone: false, dur: 0, cross: false };
    var boardEl = document.getElementById('fabBoard');

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
    function toast(msg) { if (window.showToast) window.showToast(msg); }

    /* Présence réelle sur le plateau (tops + étages auto-inclus). */
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

    /* Pourquoi cette pièce ne s'emboîte pas (ou avec un avertissement). */
    function lockInfo(p) {
        var id = p.id;
        var k = KIND[id];
        var q = state.qty[id] | 0;
        if (!state.chassis) return { lock: true, reason: 'Choisis d\u2019abord un ensemble.' };
        if (q > 0 && !(k && k.t === 'n')) {
            if (CHAIN_KEY[id]) {
                var cur = null;
                state.order.forEach(function (t) { if (CHAIN_KEY[t] === CHAIN_KEY[id]) cur = t; });
                if (cur && spanOf(cur) >= spanOf(id))
                    return { lock: true, reason: 'Déjà incluse dans ' + cur + '.' };
            } else {
                return { lock: true, reason: 'Déjà sur le plateau (pièce unique).' };
            }
        }
        if (k && k.t === 'n' && q >= k.max)
            return { lock: true, reason: 'Maximum ×' + k.max + ' atteint.' };
        if (id === 'O17' && state.chassis !== 'C3')
            return { lock: true, reason: 'O17 ne s\u2019emboîte que sur l\u2019ensemble Réaction.' };
        if (id === 'D8' && !present('D1') && !present('D3'))
            return { lock: true, reason: 'Demande D1 ou D3 sur le plateau.' };
        if (id === 'U11' && (state.qty.U11 | 0) === 0) {
            var anyS = false;
            document.querySelectorAll('input[data-sub][data-pid="U11"]').forEach(function (c) { if (c.checked) anyS = true; });
            if (!anyS) return { lock: true, reason: 'Coche d\u2019abord un palier (p3, p4 ou 2×).' };
        }
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
        /* Avertissements souples : la pièce rentre, mais le MJ tiquera. */
        if (id === 'O12' && !present('O10') && !present('O11'))
            return { lock: false, soft: 'Demande O10 ou O11 sur le plateau.' };
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
        if (!p) return;
        var info = lockInfo(p);
        if (info.lock) { toast(info.reason); render(); return; }
        var k = KIND[id];
        if (CHAIN_KEY[id]) {
            var idx = -1;
            state.order.forEach(function (t, i) { if (CHAIN_KEY[t] === CHAIN_KEY[id]) idx = i; });
            if (idx >= 0) {
                var old = state.order[idx];
                if (spanOf(old) >= spanOf(id)) { toast('Déjà incluse.'); return; }
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
        render();
    }
    function removePiece(id) {
        delete state.qty[id];
        state.order = state.order.filter(function (t) { return t !== id; });
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

    /* --- Lecture des variantes + finitions (dans les cartes). --- */
    function readVariants(sel) {
        var r19 = document.querySelector('input[name="fab_O19"]:checked');
        sel.o19 = r19 ? r19.value : 'partiel';
        var s7 = document.querySelector('select[data-pid="U7"]');
        sel.u7 = s7 ? s7.value : '5';
        sel.u11 = {};
        document.querySelectorAll('input[data-sub][data-pid="U11"]').forEach(function (c) {
            if (c.checked) sel.u11[c.dataset.sub] = true;
        });
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
    function buildSel() {
        var sel = { chassis: state.chassis, qty: {}, o19: 'partiel', u7: '5', u11: {},
                    narrow: 0, range: 0, zone: false, dur: 0, cross: false };
        Object.keys(state.qty).forEach(function (id) { sel.qty[id] = state.qty[id]; });
        if ((sel.qty.U11 | 0) > 0) {
            var any = false;
            document.querySelectorAll('input[data-sub][data-pid="U11"]').forEach(function (c) { if (c.checked) any = true; });
            if (!any) delete sel.qty.U11;
        }
        return readVariants(sel);
    }

    /* --- 1. Ensembles (châssis). --- */
    var ENS_SUB = { C0: 'Passif', C2: 'Action', C3: 'Réaction', C4: 'Opportunité' };
    function buildEnsembles() {
        host.innerHTML = '';
        CAT.chassis.forEach(function (c) {
            var b = el('button', 'fab-ensemble' + (state.chassis === c.id ? ' sel' : ''), '');
            b.type = 'button';
            b.dataset.ens = c.id;
            b.setAttribute('aria-pressed', state.chassis === c.id ? 'true' : 'false');
            b.innerHTML = '<span class="fab-ens-top"><span class="fab-ens-id">' + esc(c.id) +
                ' · ' + esc(ENS_SUB[c.id] || '') + '</span>' +
                '<span class="fab-ens-cost">' + esc(c.cout_xp) + ' XP + ' + esc(c.cout_pa) + ' PA</span></span>' +
                '<strong>' + esc(c.nom) + '</strong>' +
                '<small>' + esc(c.cadre) + '</small>' +
                '<span class="fab-ens-shape"><i></i><i></i><i></i></span>';
            host.appendChild(b);
        });
    }

    /* --- 3. Casier à pièces. --- */
    function metaLine(p) {
        var bits = [];
        if (p.seuil) bits.push('<b>Seuil :</b> ' + esc(p.seuil));
        if (p.portee) bits.push('<b>Portée :</b> ' + esc(p.portee));
        if (p.prerequis && p.prerequis.length)
            bits.push('<b class="rq">Demande :</b> <span class="rq">' + esc(p.prerequis.join(' · ')) + '</span>');
        if (p.incompatible && p.incompatible.length)
            bits.push('<b class="rf">Refuse :</b> <span class="rf">' + esc(p.incompatible.join(' · ')) + '</span>');
        var tail = p.cumul ? 'Cumulable' : 'Pièce unique';
        if (p.majeure) tail += ' · ★ majeure';
        bits.push(tail);
        return bits.join('<br>');
    }
    function buildTray() {
        ['offensif', 'defensif', 'utilitaire', 'social'].forEach(function (fam) {
            var box = document.getElementById('fabFam-' + fam);
            if (!box) return;
            box.innerHTML = '';
            box.appendChild(el('div', 'fab-tray-title', FAM_LABEL[fam]));
            CAT.pieces.filter(function (p) { return p.famille === fam; }).forEach(function (p) {
                var card = el('div', 'fab-piece fam-' + fam, '');
                card.dataset.card = p.id;
                card.draggable = true;
                var h = '<div class="fab-piece-top"><strong>' + esc(p.nom) + '</strong>' +
                    '<span class="fab-piece-cost">' + esc(p.cout_xp) + ' XP + ' + esc(p.cout_pa) + ' PA</span></div>' +
                    '<div class="fab-piece-id">' + esc(p.id) + (p.majeure ? ' · ★ majeure' : '') + '</div>' +
                    '<div class="fab-piece-text">' + esc(p.cond || p.texte || '') + '</div>' +
                    '<div class="fab-piece-meta">' + metaLine(p) + '</div>';
                if (p.id === 'O19') {
                    h += '<div class="fab-variant">' + KIND.O19.opts.map(function (o, i) {
                        return '<label><input type="radio" name="fab_O19" value="' + o.v + '"' +
                            (i === 0 ? ' checked' : '') + ' class="fab-input"> ' + esc(o.label) + '</label>';
                    }).join('') + '</div>';
                }
                if (p.id === 'U7') {
                    h += '<div class="fab-variant"><label>Palier <select data-pid="U7" class="tool-select fab-input">' +
                        KIND.U7.opts.map(function (o) {
                            return '<option value="' + o.v + '">' + esc(o.label) + '</option>';
                        }).join('') + '</select></label></div>';
                }
                if (p.id === 'U11') {
                    h += '<div class="fab-variant">' + KIND.U11.opts.map(function (o) {
                        return '<label><input type="checkbox" data-sub="' + o.v + '" data-pid="U11" class="fab-input"> ' +
                            esc(o.label) + '</label>';
                    }).join('') + '<span style="opacity:.75">Coche un palier pour l\u2019emboîter (base 2 XP).</span></div>';
                }
                h += '<div class="fab-piece-foot"><button type="button" class="fab-fit-btn" data-fit="' + esc(p.id) + '">⤓ Emboîter</button>' +
                    '<span class="fab-lock" data-lock="' + esc(p.id) + '" hidden></span>' +
                    '<span class="fab-soft" data-soft="' + esc(p.id) + '" hidden></span></div>';
                card.innerHTML = h;
                box.appendChild(card);
            });
        });
    }

    /* --- 4. Finitions (modificateurs). --- */
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

    /* --- Variantes : U11 coche un palier = pièce emboîtée. --- */
    function syncU11() {
        var any = false;
        document.querySelectorAll('input[data-sub][data-pid="U11"]').forEach(function (c) { if (c.checked) any = true; });
        if (any && (state.qty.U11 | 0) === 0) {
            var info = lockInfo(P('U11'));
            if (info.lock) {
                document.querySelectorAll('input[data-sub][data-pid="U11"]').forEach(function (c) { c.checked = false; });
                toast(info.reason);
                return;
            }
            state.order.push('U11');
            state.qty.U11 = 1;
        }
        if (!any && (state.qty.U11 | 0) > 0) {
            delete state.qty.U11;
            state.order = state.order.filter(function (t) { return t !== 'U11'; });
        }
    }

    /* --- Rendu. --- */
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
            var r = document.querySelector('input[name="fab_O19"]:checked');
            var v = r ? r.value : 'partiel';
            var o = null;
            KIND.O19.opts.forEach(function (x) { if (x.v === v) o = x; });
            return o ? o.label : '';
        }
        if (id === 'U7') {
            var s = document.querySelector('select[data-pid="U7"]');
            return 'palier ' + (s ? s.value : '5') + ' PV';
        }
        if (id === 'U11') {
            var on = [];
            document.querySelectorAll('input[data-sub][data-pid="U11"]').forEach(function (c) { if (c.checked) on.push(c.dataset.sub); });
            return on.length ? 'paliers ' + on.join(', ') : 'base';
        }
        return '';
    }

    function renderBoard(sel, r) {
        boardEl.innerHTML = '';
        var ch = sel.chassis ? C(sel.chassis) : null;
        boardEl.appendChild(el('div', 'fab-board-name',
            ch ? '◆ ' + esc(ch.nom) + ' — ' + esc(ch.cout_xp) + ' XP + ' + esc(ch.cout_pa) + ' PA' : '◆ Choisis un ensemble ci-dessus'));
        var slots = el('div', 'fab-slots', '');
        if (!state.order.length) {
            var emp = el('div', 'fab-board-empty',
                'Tes pièces emboîtées apparaissent ici.<br>Clique <strong>Emboîter</strong> ou glisse une pièce du casier.');
            slots.appendChild(emp);
        }
        state.order.forEach(function (id) {
            var p = P(id);
            if (!p) return;
            var box = el('div', 'fab-placed fam-' + p.famille, '');
            box.dataset.slot = id;
            var k = KIND[id];
            var line = null;
            r.lines.forEach(function (l) { if (l.id === id) line = l; });
            var cost = line ? (line.xp + ' XP + ' + line.pa + ' PA') : (p.cout_xp + ' XP + ' + p.cout_pa + ' PA');
            var head = '<div class="fab-placed-head"><strong>' + esc(p.nom) + '</strong>' +
                '<span class="fab-placed-cost">' + esc(cost) + '</span></div>';
            var sub = esc(p.cond || p.texte || '');
            var vs = variantSummary(id);
            if (vs) sub += ' — ' + esc(vs);
            if ((state.qty[id] | 0) > 1) sub += ' — ×' + (state.qty[id] | 0);
            var flags = '<div class="fab-placed-flags">';
            chainOf(id).forEach(function (x) { flags += '<span class="fab-flag">' + esc(x) + '</span>'; });
            if (p.majeure) flags += '<span class="fab-flag star">★ majeure</span>';
            if (p.prerequis && p.prerequis.length)
                flags += '<span class="fab-flag req">demande ' + esc(p.prerequis.join(', ')) + '</span>';
            if (p.incompatible && p.incompatible.length)
                flags += '<span class="fab-flag no">refuse ' + esc(p.incompatible.join(', ')) + '</span>';
            flags += '</div>';
            var tiers = '';
            if (chainOf(id).length > 1) {
                tiers = '<div class="fab-chain">' + chainOf(id).map(function (x) {
                    var q = P(x);
                    return '<div class="fab-chain-tier"><b>' + esc(x) + '</b> — ' + esc(q ? q.nom : '') + '</div>';
                }).join('') + '</div>';
            }
            var ctl = '<div class="fab-placed-ctl">';
            if (k && k.t === 'n')
                ctl += '<span class="fab-stepper"><button type="button" data-dec="' + esc(id) + '" aria-label="Retirer un cran">−</button>' +
                    '<span>×' + (state.qty[id] | 0) + ' / ' + k.max + '</span>' +
                    '<button type="button" data-inc="' + esc(id) + '" aria-label="Ajouter un cran">+</button></span>';
            ctl += '<button type="button" class="fab-mini-btn" data-unfit="' + esc(id) + '">⤴ Retirer</button></div>';
            var sock = '<div class="fab-sockets">';
            for (var i = 0; i < 3; i++)
                sock += '<i class="' + (i < placedSpan() ? 'on' : '') + '"></i>';
            sock += '</div>';
            box.innerHTML = head + '<div class="fab-placed-sub">' + sub + '</div>' + flags + tiers + ctl + sock;
            slots.appendChild(box);
        });
        boardEl.appendChild(slots);
    }

    function renderTrayLocks() {
        document.querySelectorAll('.fab-piece[data-card]').forEach(function (card) {
            var p = P(card.dataset.card);
            if (!p) return;
            var info = lockInfo(p);
            var btn = card.querySelector('[data-fit]');
            var lk = card.querySelector('[data-lock]');
            var sf = card.querySelector('[data-soft]');
            var isPlaced = (state.qty[p.id] | 0) > 0 && !(KIND[p.id] && KIND[p.id].t === 'n');
            card.classList.toggle('placed', isPlaced);
            card.classList.toggle('locked', !!info.lock);
            if (btn) btn.disabled = !!info.lock;
            if (btn) btn.textContent = isPlaced ? '✓ Emboîtée' : '⤓ Emboîter';
            if (lk) {
                lk.hidden = !info.lock;
                if (info.lock) lk.textContent = '🔒 ' + info.reason;
            }
            if (sf) {
                sf.hidden = !(info.soft && !info.lock);
                if (info.soft) sf.textContent = '⚠ ' + info.soft;
            }
        });
        document.querySelectorAll('.fab-ensemble').forEach(function (b) {
            var on = b.dataset.ens === state.chassis;
            b.classList.toggle('sel', on);
            b.setAttribute('aria-pressed', on ? 'true' : 'false');
        });
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
        if (d.o19) {
            var r = document.querySelector('input[name="fab_O19"][value="' + d.o19 + '"]');
            if (r) r.checked = true;
        }
        if (d.u7) {
            var s = document.querySelector('select[data-pid="U7"]');
            if (s) s.value = d.u7;
        }
        if (d.u11) {
            document.querySelectorAll('input[data-sub][data-pid="U11"]').forEach(function (c) {
                c.checked = !!(d.u11 && d.u11[c.dataset.sub]);
            });
        }
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

    function render() {
        var sel = buildSel();
        var r = FabriqueEngine.compute(CAT, sel);
        /* Opportunité : déclencheur étroit obligatoire (table Livre III §14). */
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
        renderBoard(sel, r);
        renderTrayLocks();
        var name = (document.getElementById('fabName') || {}).value || '';
        var desc = (document.getElementById('fabDesc') || {}).value || '';
        prev.name.textContent = name.trim() || 'Technique Maison';
        var fams = r.familles.map(function (f) { return f.toUpperCase(); }).join(' / ') || '—';
        prev.type.textContent = fams + (r.status === 'signature' ? ' — SIGNATURE (accord MJ)' : r.status === 'invalide' ? ' — À CORRIGER' : '');
        prev.desc.textContent = desc.trim() || 'Décris ton geste : ce qu\u2019on voit, ce qu\u2019on entend.';
        prev.pa.textContent = r.pa + ' PA';
        prev.xp.textContent = 'Création : ' + r.xp + ' XP' + (r.crossed ? ' (croisé ×2)' : '');
        if (prev.badge) {
            prev.badge.textContent = r.xp + ' XP · ' + r.pa + ' PA' +
                (r.status === 'signature' ? ' · SIGNATURE' : r.status === 'invalide' ? ' · À CORRIGER' : ' · ' + placedSpan() + '/3 logements');
        }
        prev.list.innerHTML = '';
        if (!r.lines.length) prev.list.innerHTML = '<li><em>Choisis un ensemble puis 1 à 3 pièces.</em></li>';
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
        suture: { chassis: 'C0', qty: { U1: 1 }, name: 'Suture Réflexe (canonique)', desc: 'Les premiers soins d\u2019urgence passent de 3 à 2 PA.' },
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
        var na = document.getElementById('fabNarrow');
        if (na) na.value = String(p.narrow || 0);
        var fn = document.getElementById('fabName');
        if (fn) fn.value = p.name;
        var fd = document.getElementById('fabDesc');
        if (fd) fd.value = p.desc;
        render();
    }

    /* --- Événements (délégués : le plateau est re-rendu à chaque coup). --- */
    var root = document.getElementById('fabPuzzle') || document;
    root.addEventListener('click', function (ev) {
        var t = ev.target;
        function up(sel) { return t.closest ? t.closest(sel) : null; }
        var ens = up('.fab-ensemble');
        if (ens && ens.dataset.ens) {
            state.chassis = (state.chassis === ens.dataset.ens) ? null : ens.dataset.ens;
            render();
            return;
        }
        var fit = up('[data-fit]');
        if (fit) { addPiece(fit.dataset.fit); return; }
        var unfit = up('[data-unfit]');
        if (unfit) { removePiece(unfit.dataset.unfit); return; }
        var dec = up('[data-dec]');
        if (dec) { bumpQty(dec.dataset.dec, -1); return; }
        var inc = up('[data-inc]');
        if (inc) { bumpQty(inc.dataset.inc, 1); return; }
        var pre = up('[data-preset]');
        if (pre) { applyPreset(pre.dataset.preset); return; }
        var flt = up('.fab-filter');
        if (flt) {
            document.querySelectorAll('.fab-filter').forEach(function (b) { b.classList.remove('sel'); });
            flt.classList.add('sel');
            var fam = flt.dataset.fam;
            ['offensif', 'defensif', 'utilitaire', 'social'].forEach(function (f) {
                var box = document.getElementById('fabFam-' + f);
                if (box) box.classList.toggle('hidden', fam !== 'all' && fam !== f);
            });
            return;
        }
        if (t.id === 'fabBtnReset') {
            state.chassis = null;
            state.qty = {};
            state.order = [];
            document.querySelectorAll('input[data-sub][data-pid="U11"]').forEach(function (c) { c.checked = false; });
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
            var r19 = document.querySelector('input[name="fab_O19"][value="partiel"]');
            if (r19) r19.checked = true;
            var s7 = document.querySelector('select[data-pid="U7"]');
            if (s7) s7.value = '5';
            try { localStorage.removeItem(SAVE_KEY); } catch (e) { /* vide */ }
            render();
            return;
        }
        if (t.id === 'fabBtnCopy' || (t.closest && t.closest('#fabBtnCopy'))) {
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
        }
    });
    root.addEventListener('change', function (ev) {
        var t = ev.target;
        if (t && t.dataset && t.dataset.sub && t.dataset.pid === 'U11') syncU11();
        render();
    });
    root.addEventListener('input', function (ev) {
        var t = ev.target;
        if (!t || !t.classList || !t.classList.contains('fab-input')) {
            if (t && (t.id === 'fabName' || t.id === 'fabDesc')) render();
            return;
        }
        if (t.dataset && t.dataset.sub && t.dataset.pid === 'U11') syncU11();
        render();
    });

    /* --- Glisser-déposer : la pièce du casier vers le plateau. --- */
    document.addEventListener('dragstart', function (ev) {
        var card = ev.target && ev.target.closest ? ev.target.closest('.fab-piece[data-card]') : null;
        if (!card) return;
        try { ev.dataTransfer.setData('text/fab-piece', card.dataset.card); } catch (e) { /* presse-papier D&D indisponible */ }
        try { ev.dataTransfer.effectAllowed = 'copy'; } catch (e) { /* vieux moteur */ }
    });
    boardEl.addEventListener('dragover', function (ev) {
        ev.preventDefault();
        boardEl.classList.add('fab-drop-hint');
    });
    boardEl.addEventListener('dragleave', function () { boardEl.classList.remove('fab-drop-hint'); });
    boardEl.addEventListener('drop', function (ev) {
        ev.preventDefault();
        boardEl.classList.remove('fab-drop-hint');
        var id = null;
        try { id = ev.dataTransfer.getData('text/fab-piece'); } catch (e) { /* presse-papier D&D indisponible */ }
        if (id && P(id)) addPiece(id);
    });

    /* --- Montage initial. --- */
    load();
    buildEnsembles();
    buildTray();
    buildModifs();
    restoreInputs();
    /* U11 restaurée depuis la sauvegarde. */
    (function () {
        var any = false;
        document.querySelectorAll('input[data-sub][data-pid="U11"]').forEach(function (c) { if (c.checked) any = true; });
        if (any && (state.qty.U11 | 0) === 0) { state.order.push('U11'); state.qty.U11 = 1; }
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
