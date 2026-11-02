/* ==========================================================================
   FABRIQUE DE TECHNIQUES (Livre III chap-14, Livre IX chap-39).
   Moteur pur FabriqueEngine.compute(sel, CAT) + initFabrique() qui monte
   l'atelier depuis data/livre_3.json (mecaniques.fabrique).
   Règles : standard 4-6 XP, signature 7-8 XP (MJ), activation ≤ 6 PA,
   plancher 4 XP, 1 châssis, 2-4 pièces, paliers en filière, incompatibilités.
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
            errors.push('Choisis un châssis : il dit quand ta technique part.');
        } else {
            lines.push({ id: 'chassis', label: 'Châssis — ' + ch.nom, xp: toInt(ch.cout_xp), pa: toInt(ch.cout_pa), kind: 'chassis' });
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
        if (counted < 1) errors.push('1 pièce minimum (châssis seul ne suffit pas).');
        if (counted > 3) errors.push('3 pièces maximum : au-delà, ce sont deux techniques sœurs.');
        if (sel.chassis === 'C3' || (ch && ch.id === 'C3')) {
            /* Réaction : rien de plus, le +2 est dans le châssis. */
        }
        if (has('O17') && sel.chassis !== 'C3')
            errors.push('Contre-attaque / interception (O17) exige le châssis Réaction.');
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
                warnings.push('Châssis passif + pièces à PA : passe en action principale, ou justifie auprès du MJ.');
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

/* ---------------------------------------------------------------- DOM */
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

    function esc(s) { return String(s == null ? '' : s); }
    function el(tag, cls, html) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (html != null) e.innerHTML = html;
        return e;
    }

    /* --- Châssis (radios). --- */
    CAT.chassis.forEach(function (c) {
        var lab = el('label', '', '');
        lab.style.cssText = 'display:flex;gap:8px;align-items:flex-start;font-size:0.85rem;margin-bottom:8px;cursor:pointer;';
        var r = document.createElement('input');
        r.type = 'radio'; r.name = 'fabChassis'; r.value = c.id; r.className = 'fab-input';
        var txt = el('span', '', '<strong>' + esc(c.nom) + '</strong> <span class="tool-label-hint">' +
            esc(c.cout_xp) + ' XP + ' + esc(c.cout_pa) + ' PA</span><br><span style="opacity:.8">' + esc(c.cadre) + '</span>');
        lab.appendChild(r); lab.appendChild(txt);
        host.appendChild(lab);
    });

    /* --- Familles. --- */
    var FAMS = [['offensif', 'Effets Offensifs', 'var(--vital-ruby)'],
                ['defensif', 'Effets Défensifs', 'var(--arcane-cyan)'],
                ['utilitaire', 'Effets Utilitaires', 'var(--matrix-emerald)'],
                ['social', 'Effets Sociaux', 'var(--temporal-amber)']];
    FAMS.forEach(function (fm) {
        var box = document.getElementById('fabFam-' + fm[0]);
        if (!box) return;
        CAT.pieces.filter(function (p) { return p.famille === fm[0]; }).forEach(function (p) {
            var wrap = el('div', 'tool-form-group', '');
            var k = FabriqueEngine.KIND[p.id];
            var head = '<span><strong>' + esc(p.nom) + '</strong> <span class="tool-label-hint">' +
                esc(p.cout_xp) + ' XP + ' + esc(p.cout_pa) + ' PA</span></span>' +
                '<span class="tool-label-hint">' + esc((p.seuil ? 'Seuil ' + p.seuil + ' · ' : '') +
                (p.prerequis && p.prerequis.length ? '↳ ' + p.prerequis.join(', ') + ' · ' : '') +
                (p.majeure ? '★ majeure' : 'pièce')) + '</span>';
            var lab = el('label', 'tool-label', head);
            wrap.appendChild(lab);
            if (k && k.t === 'n') {
                var n = document.createElement('input');
                n.type = 'number'; n.min = '0'; n.max = String(k.max); n.value = '0';
                n.className = 'tool-input fab-input'; n.dataset.pid = p.id; n.dataset.qty = '1';
                if (k.unit) n.title = k.unit;
                wrap.appendChild(n);
            } else if (k && k.t === 'radio') {
                k.opts.forEach(function (o, i) {
                    var l = el('label', '', '<input type="radio" name="fab_' + p.id + '" value="' + o.v + '"' +
                        (i === 0 ? ' checked' : '') + ' class="fab-input"> ' + esc(o.label));
                    l.style.cssText = 'display:block;font-size:0.82rem;margin:3px 0;';
                    wrap.appendChild(l);
                });
                var hid = document.createElement('input');
                hid.type = 'hidden'; hid.dataset.pid = p.id; hid.dataset.opt = '1';
                wrap.appendChild(hid);
            } else if (k && k.t === 'palier') {
                var s = document.createElement('select');
                s.className = 'tool-select fab-input'; s.dataset.pid = p.id; s.dataset.opt = '1';
                k.opts.forEach(function (o) {
                    var op = document.createElement('option');
                    op.value = o.v; op.textContent = o.label;
                    s.appendChild(op);
                });
                wrap.appendChild(s);
            } else if (k && k.t === 'paliers') {
                k.opts.forEach(function (o) {
                    var l = el('label', '', '<input type="checkbox" data-sub="' + o.v + '" data-pid="' + p.id + '" class="fab-input"> ' + esc(o.label));
                    l.style.cssText = 'display:block;font-size:0.82rem;margin:3px 0;';
                    wrap.appendChild(l);
                });
                var on = document.createElement('input');
                on.type = 'checkbox'; on.className = 'fab-input'; on.dataset.pid = p.id;
                on.title = 'Activer ' + p.nom;
                var lon = el('label', '', '');
                lon.style.cssText = 'display:block;font-size:0.82rem;margin-top:4px;';
                lon.appendChild(on);
                lon.appendChild(document.createTextNode(' Activer (' + p.cout_xp + ' XP + ' + p.cout_pa + ' PA)'));
                wrap.appendChild(lon);
            } else {
                var c = document.createElement('input');
                c.type = 'checkbox'; c.className = 'fab-input'; c.dataset.pid = p.id;
                wrap.insertBefore(c, wrap.firstChild);
                lab.style.marginLeft = '6px';
                var row = el('div', '', '');
                row.style.cssText = 'display:flex;gap:8px;align-items:flex-start;';
                row.appendChild(c); row.appendChild(lab);
                wrap.innerHTML = '';
                wrap.appendChild(row);
                var hint = el('div', '', esc(p.cond || p.texte || ''));
                hint.style.cssText = 'font-size:0.78rem;opacity:.75;margin:2px 0 0 26px;';
                wrap.appendChild(hint);
            }
            box.appendChild(wrap);
        });
        box.querySelectorAll('.tool-form-group').forEach(function (g) {
            g.style.cssText += 'border-bottom:1px solid rgba(255,255,255,.06);padding-bottom:8px;margin-bottom:8px;';
        });
    });

    /* --- Modificateurs. --- */
    var mod = document.getElementById('fabModifs');
    function modRow(label, inner) {
        var w = el('div', 'tool-form-group', '');
        w.appendChild(el('label', 'tool-label', '<span>' + label + '</span>'));
        w.appendChild(inner);
        mod.appendChild(w);
    }
    var r0 = el('div', '', '');
    [['0', 'Aucune (+0)'], ['2', 'Portée +2 cases (+1 XP)'], ['4', 'Portée +4 cases (+2 XP)']].forEach(function (o, i) {
        var l = el('label', '', '<input type="radio" name="fabRange" value="' + o[0] + '"' + (i === 0 ? ' checked' : '') + ' class="fab-input"> ' + o[1]);
        l.style.cssText = 'display:block;font-size:0.82rem;margin:3px 0;';
        r0.appendChild(l);
    });
    modRow('Portée (M1/M2 — l\'une ou l\'autre)', r0);
    var z = el('label', '', '<input type="checkbox" id="fabZone" class="fab-input"> Forme de zone : cône, ligne ou rayon +1 (+2 XP + 1 PA)');
    z.style.cssText = 'font-size:0.82rem;';
    modRow('Zone (M3)', z);
    var dn = document.createElement('input');
    dn.type = 'number'; dn.min = '0'; dn.max = '2'; dn.value = '0'; dn.id = 'fabDur';
    dn.className = 'tool-input fab-input';
    modRow('Durée +1 tour (M4, +1 XP + 1 PA par cran — durées cumulables seulement)', dn);
    var nr = document.createElement('input');
    nr.type = 'number'; nr.min = '0'; nr.max = '4'; nr.value = '0'; nr.id = 'fabNarrow';
    nr.className = 'tool-input fab-input';
    modRow('Conditions étroites (M5, −1 XP chacune — critique, Δ≥2, immobile, 1/combat…)', nr);
    var cr = el('label', '', '<input type="checkbox" id="fabCross" class="fab-input"> Banque croisée (M7, ×2 XP — comme le croisé ×2)');
    cr.style.cssText = 'font-size:0.82rem;';
    modRow('Hors-compétence (M7)', cr);

    /* --- Collecte. --- */
    function collect() {
        var sel = { chassis: null, qty: {}, o19: 'partiel', u7: '5', u11: {},
                    narrow: 0, range: 0, zone: false, dur: 0, cross: false };
        var rc = document.querySelector('input[name="fabChassis"]:checked');
        if (rc) sel.chassis = rc.value;
        document.querySelectorAll('#fabFam-offensif .fab-input, #fabFam-defensif .fab-input, #fabFam-utilitaire .fab-input, #fabFam-social .fab-input').forEach(function (inp) {
            if (inp.type === 'number') {
                if ((parseInt(inp.value, 10) || 0) > 0) sel.qty[inp.dataset.pid] = parseInt(inp.value, 10) || 0;
            } else if (inp.type === 'checkbox' && !inp.dataset.sub) {
                if (inp.checked) sel.qty[inp.dataset.pid] = 1;
            } else if (inp.type === 'checkbox' && inp.dataset.sub) {
                if (inp.checked) {
                    sel.qty[inp.dataset.pid] = 1;
                    sel.u11[inp.dataset.sub] = true;
                }
            }
        });
        ['O19', 'U7'].forEach(function (pid) {
            var r = document.querySelector('input[name="fab_' + pid + '"]:checked');
            var s = document.querySelector('select[data-pid="' + pid + '"]');
            if (r && pid === 'O19') sel.o19 = r.value;
            if (s && pid === 'U7') sel.u7 = s.value;
        });
        var rr = document.querySelector('input[name="fabRange"]:checked');
        if (rr) sel.range = parseInt(rr.value, 10) || 0;
        var zo = document.getElementById('fabZone');
        if (zo) sel.zone = zo.checked;
        var du = document.getElementById('fabDur');
        if (du) sel.dur = Math.max(0, Math.min(2, parseInt(du.value, 10) || 0));
        var na = document.getElementById('fabNarrow');
        if (na) sel.narrow = Math.max(0, parseInt(na.value, 10) || 0);
        var cx = document.getElementById('fabCross');
        if (cx) sel.cross = cx.checked;
        /* O19/U7 cochés via leurs fieldsets : présence du fieldset = actif si chassis choisi.
           Par défaut O19 inactif sauf case dédiée : on l'active via sa radio fieldset visible.
           Règle simple : O19/U7/U11 actifs ssi leur input principal est coché/renseigné. */
        return sel;
    }

    /* O19/U7 : case d'activation dédiée (leurs fieldsets sont toujours visibles). */
    function armSpecials() {
        ['O19', 'U7'].forEach(function (pid) {
            var box = document.querySelector('[data-pid="' + pid + '"]');
            if (!box) return;
            var wrap = box.closest('.tool-form-group');
            if (!wrap || wrap.querySelector('[data-arm]')) return;
            var l = document.createElement('label');
            l.style.cssText = 'display:block;font-size:0.82rem;margin-top:4px;color:var(--temporal-amber);';
            l.innerHTML = '<input type="checkbox" data-arm="' + pid + '" class="fab-input"> Inclure cette pièce';
            wrap.insertBefore(l, wrap.firstChild);
        });
    }
    armSpecials();

    function collect2() {
        var sel = collect();
        sel.qty = {};
        document.querySelectorAll('.fab-input').forEach(function (inp) {
            if (inp.dataset && inp.dataset.arm) {
                if (inp.checked) sel.qty[inp.dataset.arm] = 1;
            }
        });
        /* nombres + checkboxes simples */
        document.querySelectorAll('#fabFam-offensif .fab-input, #fabFam-defensif .fab-input, #fabFam-utilitaire .fab-input, #fabFam-social .fab-input').forEach(function (inp) {
            if (inp.type === 'number' && inp.dataset.qty) {
                var v = parseInt(inp.value, 10) || 0;
                if (v > 0) sel.qty[inp.dataset.pid] = v;
            } else if (inp.type === 'checkbox' && inp.dataset.pid && !inp.dataset.sub && !inp.dataset.arm && !inp.dataset.opt) {
                if (inp.checked) sel.qty[inp.dataset.pid] = 1;
            } else if (inp.type === 'checkbox' && inp.dataset.sub) {
                if (inp.checked) {
                    sel.qty[inp.dataset.pid] = 1;
                    sel.u11[inp.dataset.sub] = true;
                }
            }
        });
        return sel;
    }

    /* --- Rendu. --- */
    var prev = {
        name: document.getElementById('fabCardName'),
        type: document.getElementById('fabCardType'),
        desc: document.getElementById('fabCardDesc'),
        pa: document.getElementById('fabCardPa'),
        xp: document.getElementById('fabCardXp'),
        list: document.getElementById('fabCardEffectsList'),
        check: document.getElementById('fabChecklist')
    };

    function render() {
        var sel = collect2();
        var r = FabriqueEngine.compute(CAT, sel);
        var name = (document.getElementById('fabName') || {}).value || '';
        var desc = (document.getElementById('fabDesc') || {}).value || '';
        prev.name.textContent = name.trim() || 'Technique Maison';
        var fams = r.familles.map(function (f) { return f.toUpperCase(); }).join(' / ') || '—';
        prev.type.textContent = fams + (r.status === 'signature' ? ' — SIGNATURE (accord MJ)' : r.status === 'invalide' ? ' — À CORRIGER' : '');
        prev.desc.textContent = desc.trim() || 'Décris ton geste : ce qu’on voit, ce qu’on entend.';
        prev.pa.textContent = r.pa + ' PA';
        prev.xp.textContent = 'Création : ' + r.xp + ' XP' + (r.crossed ? ' (croisé ×2)' : '');
        prev.list.innerHTML = '';
        if (!r.lines.length) prev.list.innerHTML = '<li><em>Choisis un châssis puis 2 à 4 pièces.</em></li>';
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
        ck(r.errors.length === 0, r.errors.length ? r.errors[0] : (r.status === 'signature' ? 'Montage valide : signature, fais signer ton MJ.' : 'Montage valide : ' + r.xp + ' XP / ' + r.pa + ' PA.'));
        r.errors.slice(1).forEach(function (e) { ck(false, e); });
        r.warnings.forEach(function (w) {
            var li = document.createElement('li');
            li.textContent = '⚠ ' + w;
            li.style.color = 'var(--temporal-amber)';
            prev.check.appendChild(li);
        });
        return r;
    }

    document.querySelectorAll('.fab-input').forEach(function (inp) {
        inp.addEventListener('input', render);
        inp.addEventListener('change', render);
    });
    ['fabName', 'fabDesc'].forEach(function (id) {
        var n = document.getElementById(id);
        if (n) n.addEventListener('input', render);
    });

    /* --- Presets (montages vérifiés du Livre III §14). --- */
    var PRESETS = {
        hache: { chassis: 'C0', qty: { O2: 1 }, name: 'Hache de Guerre (canonique)', desc: 'Sur succès net : +2 dégâts.' },
        suture: { chassis: 'C0', qty: { U1: 1 }, name: 'Suture Réflexe (canonique)', desc: 'Les premiers soins d’urgence passent de 3 à 2 PA.' },
        miroir: { chassis: 'C3', qty: { D1: 1 }, narrow: 1, name: 'Lame Miroir (canonique)', desc: 'Tu pares les tirs laser au contact (Delta ≥ 0).' },
        fente: { chassis: 'C2', qty: { O8: 1, O10: 1 }, name: 'Fente Basse', desc: 'Visée Jambes sans malus ; touche ? la cible est Ralentie 1 tour.' }
    };
    function applyPreset(key) {
        var p = PRESETS[key];
        if (!p) return;
        document.querySelectorAll('input[name="fabChassis"]').forEach(function (r) { r.checked = (r.value === p.chassis); });
        document.querySelectorAll('.fab-input').forEach(function (inp) {
            if (inp.type === 'number' && inp.dataset.qty) inp.value = '0';
            if (inp.type === 'checkbox' && inp.dataset.pid && !inp.dataset.sub) inp.checked = false;
        });
        Object.keys(p.qty).forEach(function (pid) {
            var n = document.querySelector('.fab-input[data-pid="' + pid + '"][data-qty]');
            var c = document.querySelector('.fab-input[data-pid="' + pid + '"]:not([data-qty]):not([data-sub]):not([data-opt]):not([data-arm])');
            if (n) n.value = String(p.qty[pid]);
            else if (c) c.checked = true;
        });
        var na = document.getElementById('fabNarrow');
        if (na) na.value = String(p.narrow || 0);
        var fn = document.getElementById('fabName');
        if (fn) fn.value = p.name;
        var fd = document.getElementById('fabDesc');
        if (fd) fd.value = p.desc;
        render();
    }
    document.querySelectorAll('[data-preset]').forEach(function (b) {
        b.addEventListener('click', function () { applyPreset(b.dataset.preset); });
    });
    var rs = document.getElementById('fabBtnReset');
    if (rs) rs.addEventListener('click', function () {
        document.querySelectorAll('.fab-input').forEach(function (inp) {
            if (inp.type === 'number') inp.value = '0';
            if (inp.type === 'checkbox') inp.checked = false;
            if (inp.type === 'radio' && inp.name === 'fabRange' && inp.value === '0') inp.checked = true;
            if (inp.name === 'fab_O19' && inp.value === 'partiel') inp.checked = true;
        });
        var u7s = document.querySelector('select[data-pid="U7"]');
        if (u7s) u7s.value = '5';
        document.querySelectorAll('input[name="fabChassis"]').forEach(function (r) { r.checked = false; });
        render();
    });

    var cp = document.getElementById('fabBtnCopy');
    if (cp) cp.addEventListener('click', function () {
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
            if (window.showToast) window.showToast('Fiche de technique copiée !');
        });
    });

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
