/* ==========================================================================
   APPENDICE C : COMPENDIUM INTERACTIF DES OBJETS (js/appendice_objets.js)
   Rend les 140 objets canoniques depuis data/appendice_objets.json avec
   recherche plein texte, filtres cumulables, 4 vues (cartes/compact/fermées/
   tableau triable), comparateur côte-à-côte et calculateur de kit persistant.
   ========================================================================== */
(function () {
    'use strict';

    var DATA = null;
    var CATEGORY_MAP = {};
    var PROP_MAP = {};
    var OBJ_MAP = {};
    var BUDGET_MAP = {};
    var PRESET_MAP = {};

    var STORAGE_KIT = 'codex-objets-kit-v1';
    var STORAGE_FAVS = 'codex-objets-favs-v1';
    var MAX_COMPARE = 4;

    var PRICE_BUCKETS = [
        { key: '<=500', label: '≤ 500 CE (Courant / Survie)', rank: 1 },
        { key: '501-2000', label: '501 – 2 000 CE (Standard)', rank: 2 },
        { key: '2001-10000', label: '2 001 – 10 000 CE (Spécialisé)', rank: 3 },
        { key: '10001-50000', label: '10 001 – 50 000 CE (Militaire / Élite)', rank: 4 },
        { key: '>50000', label: '> 50 000 CE (Haute Technologie / Proto)', rank: 5 }
    ];

    var state = {
        query: '',
        epoch: 'all',        // all | E0..E5
        epochMode: 'exact',  // exact | upto
        categorie: 'all',
        family: 'all',
        skill: 'all',
        hands: 'all',
        enc: 'all',
        price: 'all',
        props: [],           // array of property IDs (AND logic)
        consumableOnly: false,
        noEncOnly: false,
        stealthOnly: false,
        piercingOnly: false,
        inBudgetOnly: false,
        favsOnly: false,
        sort: 'categorie',
        groupBy: 'categorie',
        view: 'detailed',    // detailed | compact | closed | table
        compareIds: [],
        compareOpen: false,
        kitOpen: false
    };

    var kit = {
        force: 3,
        epoch: 'E2',
        budget: 4000,
        techGuerrier: false,
        recupVardien: false,
        items: [] // { id, qty, equipe, variantIdx }
    };

    var favs = {};
    var els = {};

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function norm(s) {
        return String(s == null ? '' : s)
            .replace(/[Œœ]/g, 'oe')
            .toLowerCase()
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '');
    }

    function fmtNum(n) {
        var rounded = Math.round(n * 100) / 100;
        var parts = String(rounded).split('.');
        parts[0] = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
        return parts.join(',');
    }

    function fmtCE(n) {
        return fmtNum(Math.round(Number(n) || 0)) + ' CE';
    }

    function errBox(msg) {
        return '<div class="callout vital"><div class="callout-body">' + esc(msg) + '</div></div>';
    }

    function familyTone(fam) {
        if (fam === 'Arme de mêlée' || fam === 'Explosif / Zone') return 'ruby';
        if (fam === 'Arme à distance' || fam === 'Outil / Terrain' || fam === 'Stimulant / Ration') return 'amber';
        if (fam === 'Armure' || fam === 'Bouclier' || fam === 'Champ / Combinaison') return 'cyan';
        if (fam === 'Soin / Médical') return 'emerald';
        return 'violet';
    }

    function epochTone(e) {
        if (e === 'E0' || e === 'E1') return 'amber';
        if (e === 'E2' || e === 'E3') return 'cyan';
        return 'emerald';
    }

    function priceBucketKey(prix) {
        if (prix <= 500) return '<=500';
        if (prix <= 2000) return '501-2000';
        if (prix <= 10000) return '2001-10000';
        if (prix <= 50000) return '10001-50000';
        return '>50000';
    }

    function repairDiffForEpoch(epMin) {
        if (epMin <= 2) return 5;
        if (epMin <= 4) return 7;
        return 9;
    }

    function adjustedThrowRange(o) {
        if (!o.portee_jet_cases) return null;
        var bonus = Math.min(4, Math.max(0, (Number(kit.force) || 3) - 3));
        return o.portee_jet_cases + bonus;
    }

    function hasProp(o, propId) {
        var list = o.proprietes || [];
        for (var i = 0; i < list.length; i++) {
            if (list[i].id === propId) return true;
        }
        return false;
    }

    function saveKit() {
        try {
            localStorage.setItem(STORAGE_KIT, JSON.stringify(kit));
        } catch (e) {}
    }

    function loadKit() {
        try {
            var raw = localStorage.getItem(STORAGE_KIT);
            if (!raw) return;
            var parsed = JSON.parse(raw);
            if (parsed && typeof parsed === 'object') {
                kit.force = Math.min(10, Math.max(1, parseInt(parsed.force, 10) || 3));
                kit.epoch = BUDGET_MAP[parsed.epoch] ? parsed.epoch : 'E2';
                kit.budget = Math.max(0, parseInt(parsed.budget, 10) || 0);
                kit.techGuerrier = !!parsed.techGuerrier;
                kit.recupVardien = !!parsed.recupVardien;
                kit.items = Array.isArray(parsed.items) ? parsed.items.filter(function (it) {
                    return it && OBJ_MAP[it.id] && (parseInt(it.qty, 10) || 0) > 0;
                }).map(function (it) {
                    return {
                        id: it.id,
                        qty: Math.max(1, parseInt(it.qty, 10) || 1),
                        equipe: it.equipe !== false,
                        variantIdx: (typeof it.variantIdx === 'number' && it.variantIdx >= 0) ? it.variantIdx : -1
                    };
                }) : [];
            }
        } catch (e) {}
    }

    function saveFavs() {
        try {
            localStorage.setItem(STORAGE_FAVS, JSON.stringify(Object.keys(favs)));
        } catch (e) {}
    }

    function loadFavs() {
        try {
            var raw = localStorage.getItem(STORAGE_FAVS);
            if (!raw) return;
            var arr = JSON.parse(raw);
            if (Array.isArray(arr)) {
                favs = {};
                arr.forEach(function (id) {
                    if (OBJ_MAP[id]) favs[id] = true;
                });
            }
        } catch (e) {}
    }

    function kitStats() {
        var totalCE = 0;
        var totalL = 0;
        var armures = [];
        var champs = [];
        var boucliers = [];
        var armes2H = [];
        var maxProt = 0;
        var maxAbsorp = 0;
        var maxParade = 0;
        var rawEnc = 0;

        kit.items.forEach(function (entry) {
            var o = OBJ_MAP[entry.id];
            if (!o) return;
            var v = (entry.variantIdx >= 0 && o.variantes && o.variantes[entry.variantIdx]) ? o.variantes[entry.variantIdx] : null;
            var unitPrice = v ? v.prix_ce : o.prix_ce;
            var unitWeight = v ? v.poids_livres : o.poids_livres;
            var prot = (v && typeof v.protection === 'number') ? v.protection : (o.protection || 0);
            var enc = (v && typeof v.encombrement_pa === 'number') ? v.encombrement_pa : (o.encombrement_pa || 0);

            totalCE += unitPrice * entry.qty;
            totalL += unitWeight * entry.qty;

            if (entry.equipe) {
                if (o.emplacement === 'armure') {
                    armures.push(o.nom);
                    if (prot > maxProt) maxProt = prot;
                    rawEnc += enc;
                } else if (o.emplacement === 'champ') {
                    champs.push(o.nom);
                    if ((o.absorption || 0) > maxAbsorp) maxAbsorp = o.absorption || 0;
                    rawEnc += enc;
                } else if (o.emplacement === 'bouclier') {
                    boucliers.push(o.nom);
                    if ((o.bonus_parade || 0) > maxParade) maxParade = o.bonus_parade || 0;
                    rawEnc += enc;
                } else if (o.emplacement === 'implant' && prot) {
                    maxProt += prot;
                } else if (o.emplacement === 'arme' && o.mains === '2H') {
                    armes2H.push(o.nom);
                }
            }
        });

        var normalLimit = kit.force * (DATA ? DATA.regles_ref.charge_portage.normal_mult_for : 15);
        var maxLimit = kit.force * (DATA ? DATA.regles_ref.charge_portage.surcharge_mult_for : 25);
        var surchargeMalus = 0;
        var weightStatus = 'Libre';
        var weightTone = 'emerald';
        if (totalL > maxLimit) {
            weightStatus = 'Bloqué (> FOR×25)';
            weightTone = 'ruby';
        } else if (totalL > normalLimit) {
            surchargeMalus = 1;
            weightStatus = 'Surchargé (+1 PA)';
            weightTone = 'amber';
        }

        var armorEnc = Math.max(0, rawEnc - (kit.techGuerrier ? 1 : 0));
        var totalPaMalus = armorEnc + surchargeMalus;
        var remainingCE = kit.budget - totalCE;

        var alerts = [];
        if (remainingCE < 0) {
            alerts.push({ tone: 'ruby', text: 'Budget dépassé de ' + fmtCE(Math.abs(remainingCE)) + '.' });
        }
        if (totalL > maxLimit) {
            alerts.push({ tone: 'ruby', text: 'Charge > FOR×25 L (' + fmtNum(maxLimit) + ' L) : déplacement impossible sans aide ou monture.' });
        } else if (surchargeMalus > 0) {
            alerts.push({ tone: 'amber', text: 'Surchargé (> FOR×15 L) : +1 PA sur chaque action et chaque case de déplacement.' });
        }
        if (armures.length > 1) {
            alerts.push({ tone: 'ruby', text: '1 seule armure corporelle portable à la fois (' + armures.length + ' équipées).' });
        }
        if (champs.length > 1) {
            alerts.push({ tone: 'ruby', text: '1 seul champ de force actif à la fois (' + champs.length + ' équipés).' });
        }
        if (boucliers.length > 0 && armes2H.length > 0) {
            alerts.push({ tone: 'amber', text: 'Bouclier (1H) équipé en même temps qu\'une arme 2H : incompatibles simultanément en main.' });
        }

        return {
            totalCE: totalCE,
            remainingCE: remainingCE,
            totalL: Math.round(totalL * 100) / 100,
            totalKg: Math.round(totalL * 0.45 * 100) / 100,
            normalLimit: normalLimit,
            maxLimit: maxLimit,
            surchargeMalus: surchargeMalus,
            armorEnc: armorEnc,
            totalPaMalus: totalPaMalus,
            weightStatus: weightStatus,
            weightTone: weightTone,
            maxProt: maxProt,
            maxAbsorp: maxAbsorp,
            maxParade: maxParade,
            resaleCE: Math.round(totalCE * 0.5),
            alerts: alerts
        };
    }

    function buildSearchIndex(o) {
        var parts = [
            o.nom,
            o.sous_categorie,
            o.famille,
            o.epoque_label,
            o.dlph,
            o.degats_texte,
            o.portee_label,
            o.mains,
            o.munitions,
            o.prix_label,
            o.resume,
            o.equivalents
        ];
        if (Array.isArray(o.competences)) parts = parts.concat(o.competences);
        if (Array.isArray(o.proprietes)) parts = parts.concat(o.proprietes.map(function (p) { return p.label; }));
        if (Array.isArray(o.etats_infliges)) parts = parts.concat(o.etats_infliges);
        if (Array.isArray(o.etats_soignes)) parts = parts.concat(o.etats_soignes);
        if (Array.isArray(o.notes_regles)) parts = parts.concat(o.notes_regles);
        if (Array.isArray(o.variantes)) parts = parts.concat(o.variantes.map(function (v) { return v.nom + ' ' + v.note; }));
        if (Array.isArray(o.tags)) parts = parts.concat(o.tags);
        if (CATEGORY_MAP[o.categorie]) parts.push(CATEGORY_MAP[o.categorie].nom);
        o._search = norm(parts.filter(Boolean).join(' '));
    }

    function matches(o, remainingCE) {
        if (state.categorie !== 'all' && o.categorie !== state.categorie) return false;
        if (state.epoch !== 'all') {
            if (state.epochMode === 'exact') {
                if (!o.epoques || o.epoques.indexOf(state.epoch) === -1) return false;
            } else {
                var epN = parseInt(state.epoch.slice(1), 10);
                if (o.epoque_min > epN) return false;
            }
        }
        if (state.family !== 'all' && o.famille !== state.family) return false;
        if (state.skill !== 'all' && (!o.competences || o.competences.indexOf(state.skill) === -1)) return false;
        if (state.hands !== 'all') {
            if (state.hands === 'Outil') {
                if (o.mains !== 'Outil' && o.mains !== 'Sac' && o.mains !== 'Accessoire') return false;
            } else if (o.mains !== state.hands) {
                return false;
            }
        }
        if (state.enc !== 'all') {
            if (state.enc === 'armor-only') {
                if (o.emplacement !== 'armure' && o.emplacement !== 'bouclier' && o.emplacement !== 'champ') return false;
            } else {
                if (String(o.encombrement_pa) !== state.enc) return false;
            }
        }
        if (state.price !== 'all' && priceBucketKey(o.prix_ce) !== state.price) return false;
        if (state.props.length) {
            for (var i = 0; i < state.props.length; i++) {
                if (!hasProp(o, state.props[i])) return false;
            }
        }
        if (state.consumableOnly && !o.consommable) return false;
        if (state.noEncOnly && o.encombrement_pa !== 0) return false;
        if (state.stealthOnly && !hasProp(o, 'silencieuse') && !hasProp(o, 'dissimulable')) return false;
        if (state.piercingOnly && (!o.perforante || o.perforante <= 0)) return false;
        if (state.inBudgetOnly && o.prix_ce > remainingCE) return false;
        if (state.favsOnly && !favs[o.id]) return false;

        if (state.query) {
            var tokens = norm(state.query).split(/\s+/).filter(Boolean);
            for (var j = 0; j < tokens.length; j++) {
                if (o._search.indexOf(tokens[j]) === -1) return false;
            }
        }
        return true;
    }

    function sortObjects(list) {
        var copy = list.slice();
        copy.sort(function (a, b) {
            if (state.sort === 'nom-asc') return a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' });
            if (state.sort === 'nom-desc') return b.nom.localeCompare(a.nom, 'fr', { sensitivity: 'base' });
            if (state.sort === 'prix-asc') return (a.prix_ce - b.prix_ce) || a.nom.localeCompare(b.nom, 'fr');
            if (state.sort === 'prix-desc') return (b.prix_ce - a.prix_ce) || a.nom.localeCompare(b.nom, 'fr');
            if (state.sort === 'poids-asc') return (a.poids_livres - b.poids_livres) || a.nom.localeCompare(b.nom, 'fr');
            if (state.sort === 'poids-desc') return (b.poids_livres - a.poids_livres) || a.nom.localeCompare(b.nom, 'fr');
            if (state.sort === 'degats-desc') {
                var da = a.degats == null ? -1 : a.degats;
                var db = b.degats == null ? -1 : b.degats;
                if (da !== db) return db - da;
                return a.nom.localeCompare(b.nom, 'fr');
            }
            if (state.sort === 'portee-desc') {
                var pa = a.portee_cases == null ? -1 : a.portee_cases;
                var pb = b.portee_cases == null ? -1 : b.portee_cases;
                if (pa !== pb) return pb - pa;
                return a.nom.localeCompare(b.nom, 'fr');
            }
            if (state.sort === 'prot-desc') {
                var va = (a.protection || 0) * 10 + (a.absorption || 0) + (a.bonus_parade || 0) * 5;
                var vb = (b.protection || 0) * 10 + (b.absorption || 0) + (b.bonus_parade || 0) * 5;
                if (va !== vb) return vb - va;
                return a.nom.localeCompare(b.nom, 'fr');
            }
            if (state.sort === 'perf-desc') {
                if (a.perforante !== b.perforante) return b.perforante - a.perforante;
                return a.nom.localeCompare(b.nom, 'fr');
            }
            if (state.sort === 'epoque-asc') {
                if (a.epoque_min !== b.epoque_min) return a.epoque_min - b.epoque_min;
                return (a.prix_ce - b.prix_ce) || a.nom.localeCompare(b.nom, 'fr');
            }
            var cia = CATEGORY_MAP[a.categorie] ? CATEGORY_MAP[a.categorie]._order : 99;
            var cib = CATEGORY_MAP[b.categorie] ? CATEGORY_MAP[b.categorie]._order : 99;
            if (cia !== cib) return cia - cib;
            return a._order - b._order;
        });
        return copy;
    }

    function groupObjects(list) {
        if (state.groupBy === 'none') {
            return [{
                key: 'all',
                idAttr: '',
                code: '',
                title: 'Tous les Objets',
                ton: 'cyan',
                desc: '',
                items: list
            }];
        }

        var buckets = {};
        var orderedKeys = [];

        function pushItem(key, meta, item) {
            if (!buckets[key]) {
                buckets[key] = {
                    key: key,
                    idAttr: meta.idAttr || '',
                    code: meta.code || '',
                    title: meta.title,
                    ton: meta.ton || 'cyan',
                    desc: meta.desc || '',
                    sortRank: typeof meta.sortRank === 'number' ? meta.sortRank : 99,
                    items: []
                };
                orderedKeys.push(key);
            }
            buckets[key].items.push(item);
        }

        list.forEach(function (o) {
            if (state.groupBy === 'categorie') {
                var c = CATEGORY_MAP[o.categorie];
                pushItem(o.categorie, {
                    idAttr: 'cat-' + o.categorie,
                    code: c ? c.code : '',
                    title: c ? c.nom : o.categorie,
                    ton: c ? c.ton : 'cyan',
                    desc: c ? c.desc : '',
                    sortRank: c ? c._order : 99
                }, o);
            } else if (state.groupBy === 'epoque') {
                var ep = 'E' + o.epoque_min;
                var b = BUDGET_MAP[ep];
                pushItem(ep, {
                    code: ep,
                    title: b ? (b.nom + ' (' + fmtCE(b.budget_ce) + ' départ)') : ep,
                    ton: epochTone(ep),
                    desc: b ? ('Accès : ' + b.acces) : '',
                    sortRank: o.epoque_min
                }, o);
            } else if (state.groupBy === 'famille') {
                pushItem(o.famille, {
                    code: 'FAMILLE',
                    title: o.famille,
                    ton: familyTone(o.famille),
                    sortRank: o.famille.charCodeAt(0)
                }, o);
            } else if (state.groupBy === 'competence') {
                var sk = (o.competences && o.competences[0]) || 'Sans compétence requise';
                pushItem(sk, {
                    code: 'COMPÉTENCE',
                    title: sk,
                    ton: o.competences && o.competences.length ? 'cyan' : 'amber',
                    sortRank: o.competences && o.competences.length ? 1 : 99
                }, o);
            } else if (state.groupBy === 'prix') {
                var pk = priceBucketKey(o.prix_ce);
                var pb = null;
                for (var i = 0; i < PRICE_BUCKETS.length; i++) {
                    if (PRICE_BUCKETS[i].key === pk) { pb = PRICE_BUCKETS[i]; break; }
                }
                pushItem(pk, {
                    code: 'PRIX',
                    title: pb ? pb.label : pk,
                    ton: 'amber',
                    sortRank: pb ? pb.rank : 99
                }, o);
            }
        });

        orderedKeys.sort(function (ka, kb) {
            return buckets[ka].sortRank - buckets[kb].sortRank;
        });

        return orderedKeys.map(function (k) { return buckets[k]; });
    }

    function kitCountFor(id) {
        var n = 0;
        kit.items.forEach(function (it) { if (it.id === id) n += it.qty; });
        return n;
    }

    function isCompared(id) {
        return state.compareIds.indexOf(id) !== -1;
    }

    function statCellsFor(o) {
        var kg = Math.round(o.poids_livres * 0.45 * 100) / 100;
        var weightTxt = o.poids_livres === 0
            ? '— (Implant)'
            : (esc(o.poids_label) + ' (' + esc(fmtNum(kg)) + ' kg)');
        var cells = [];

        if (o.dlph || o.degats != null) {
            cells.push({ label: 'Dégâts', val: o.degats_texte || '—' });
            var adjJet = adjustedThrowRange(o);
            var porteeTxt = o.portee_label || '—';
            if (adjJet && adjJet !== o.portee_jet_cases) {
                porteeTxt += ' → ' + adjJet + 'P (FOR ' + kit.force + ')';
            }
            cells.push({ label: 'Portée', val: porteeTxt });
            cells.push({ label: 'Poids', val: weightTxt, rawHtml: true });
            if (o.munitions) {
                cells.push({ label: 'Mun. / Rech.', val: o.munitions + (o.rechargement_pa ? ' (' + o.rechargement_pa + ' PA)' : '') });
            } else {
                cells.push({ label: 'Maniement / Perf.', val: o.mains + (o.perforante ? ' • Perf. ' + o.perforante : '') });
            }
        } else if (o.emplacement === 'armure' || o.emplacement === 'bouclier' || o.emplacement === 'champ' || o.protection) {
            var defVal = '—';
            if (o.protection != null) {
                defVal = 'Prot ' + o.protection + (o.protection_max ? '–' + o.protection_max : '');
            } else if (o.absorption != null) {
                defVal = 'Absorbe ' + o.absorption + ' dégâts';
            } else if (o.bonus_parade != null) {
                defVal = '+' + o.bonus_parade + ' parade';
            }
            cells.push({ label: 'Défense', val: defVal });
            var encTxt = o.encombrement_max_pa != null
                ? ('+' + o.encombrement_pa + ' à +' + o.encombrement_max_pa + ' PA')
                : (o.encombrement_pa ? ('+' + o.encombrement_pa + ' PA/action') : '0 PA (aucun)');
            cells.push({ label: 'Encombrement', val: encTxt });
            cells.push({ label: 'Poids', val: weightTxt, rawHtml: true });
            cells.push({ label: 'Emplacement', val: o.mains + (o.duree ? ' • ' + o.duree : '') });
        } else {
            cells.push({ label: 'Usage / Coût PA', val: o.cout_pa || (o.duree ? 'Durée ' + o.duree : 'Équipement') });
            cells.push({ label: 'Poids', val: weightTxt, rawHtml: true });
            var effTxt = o.soin_degats ? ('−' + o.soin_degats + ' dégâts') : (o.duree || o.munitions || o.mains);
            cells.push({ label: 'Capacité / Durée', val: effTxt });
            cells.push({
                label: 'Compétence',
                val: (o.competences && o.competences.length) ? o.competences.join(', ') : 'Universel'
            });
        }
        return cells;
    }

    function renderCard(o) {
        var cat = CATEGORY_MAP[o.categorie] || { code: '', nom: o.categorie, ton: 'cyan' };
        var isCompact = state.view === 'compact';
        var isClosed = state.view === 'closed';
        var inKitQty = kitCountFor(o.id);
        var inCmp = isCompared(o.id);
        var isFav = !!favs[o.id];

        var pills = '';
        pills += '<span class="tag-pill ' + familyTone(o.famille) + '">' + esc(o.famille) + '</span>';
        pills += '<span class="tag-pill">' + esc(o.mains) + '</span>';
        if (o.consommable) pills += '<span class="tag-pill amber">Consommable</span>';
        if (o.encombrement_pa > 0) pills += '<span class="tag-pill ruby">Encombr. +' + esc(o.encombrement_pa) + ' PA</span>';

        (o.proprietes || []).forEach(function (p) {
            var pdef = PROP_MAP[p.id];
            var tip = pdef ? pdef.tip : p.label;
            pills += '<button type="button" class="obj-card-prop-pill rule-ref js-filter-prop" data-prop-id="' + esc(p.id) + '" data-tip="' + esc(tip) + '">' + esc(p.label) + '</button>';
        });

        var statsHtml = statCellsFor(o).map(function (c) {
            return '<div class="action-meta-cell"><span class="action-meta-label">' + esc(c.label) + '</span><span class="action-meta-val">' + (c.rawHtml ? c.val : esc(c.val)) + '</span></div>';
        }).join('');

        var statesHtml = '';
        if ((o.etats_infliges && o.etats_infliges.length) || (o.etats_soignes && o.etats_soignes.length)) {
            statesHtml += '<div class="action-chips-row">';
            (o.etats_infliges || []).forEach(function (st) {
                statesHtml += '<span class="obj-state-chip inflict">Inflige : ' + esc(st) + '</span>';
            });
            (o.etats_soignes || []).forEach(function (st) {
                statesHtml += '<span class="obj-state-chip heal">Soigne : ' + esc(st) + '</span>';
            });
            statesHtml += '</div>';
        }

        var notesHtml = '';
        if (o.notes_regles && o.notes_regles.length) {
            notesHtml = '<ul class="action-proc-list">' + o.notes_regles.map(function (n) {
                return '<li>' + esc(n) + '</li>';
            }).join('') + '</ul>';
        }

        var variantsHtml = '';
        if (o.variantes && o.variantes.length) {
            variantsHtml = '<div class="action-detail-block"><div class="action-detail-title">Variantes Officielles</div><ul class="obj-variants-list">' +
                o.variantes.map(function (v) {
                    return '<li><strong>' + esc(v.nom) + '</strong> (' + esc(v.epoque_label) + ' • ' + esc(fmtCE(v.prix_ce)) + ' • ' + esc(v.poids_livres) + ' L) — ' + esc(v.note) + '</li>';
                }).join('') + '</ul></div>';
        }

        var repRatio = kit.recupVardien ? 0.125 : 0.25;
        var repPrice = Math.round(o.prix_ce * repRatio);
        var repDiff = repairDiffForEpoch(o.epoque_min);
        var econHtml =
            '<div class="obj-econ-bar">' +
                '<span><strong>Revente :</strong> ' + esc(fmtCE(Math.round(o.prix_ce * 0.5))) + ' (neuf) / ' + esc(fmtCE(Math.round(o.prix_ce * 0.25))) + ' (endom.)</span>' +
                (o.consommable
                    ? '<span><strong>Usage :</strong> Consommable</span>'
                    : '<span><strong>Réparation Détruit :</strong> ' + esc(fmtCE(repPrice)) + (kit.recupVardien ? ' (Vardien)' : '') + ' • Diff ' + repDiff + '</span>') +
            '</div>';

        var compChips = (o.competences || []).map(function (c) {
            return '<span class="action-skill-chip">' + esc(c) + '</span>';
        }).join('');

        var refsHtml = '';
        if (o.source && o.source.href) {
            refsHtml += '<a class="action-src-link primary" href="' + esc(o.source.href) + '">' + esc(o.source.label) + ' ↗</a>';
        }
        (o.refs || []).forEach(function (r) {
            refsHtml += '<a class="action-src-link" href="' + esc(r.href) + '">' + esc(r.label) + '</a>';
        });

        var topRightBadge = o.dlph
            ? '<span class="obj-dlph-code">' + esc(o.dlph) + '</span>'
            : (o.protection != null
                ? '<span class="obj-dlph-code">Prot ' + esc(o.protection) + (o.protection_max ? '–' + esc(o.protection_max) : '') + '</span>'
                : (o.absorption != null
                    ? '<span class="obj-dlph-code">Abs. ' + esc(o.absorption) + '</span>'
                    : (o.bonus_parade != null
                        ? '<span class="obj-dlph-code">+' + esc(o.bonus_parade) + ' parade</span>'
                        : (o.soin_degats != null
                            ? '<span class="obj-dlph-code">−' + esc(o.soin_degats) + ' dégâts</span>'
                            : '<span class="obj-dlph-code">' + esc(o.poids_label) + '</span>'))));

        return (
            '<article class="action-card obj-card' + (isCompact ? ' compact' : '') + (isClosed ? ' closed' : '') + '" id="obj-' + esc(o.id) + '" data-ton="' + esc(cat.ton) + '">' +
                '<div class="action-card-head">' +
                    '<div class="action-title-wrap">' +
                        '<div class="action-cat-kicker">' + esc(cat.code) + ' · ' + esc(o.sous_categorie) + '</div>' +
                        '<h4>' + esc(o.nom) + '</h4>' +
                    '</div>' +
                    '<div class="action-badges-right">' +
                        '<div class="obj-badges-top">' +
                            '<span class="obj-epoch-badge" data-ep="' + esc(o.epoque_min) + '">' + esc(o.epoque_label) + '</span>' +
                            '<span class="obj-price-badge">' + esc(o.prix_label) + '</span>' +
                        '</div>' +
                        topRightBadge +
                    '</div>' +
                '</div>' +
                '<div class="action-pills-row">' + pills + '</div>' +
                '<p class="action-summary">' + esc(o.resume) + '</p>' +
                '<div class="action-meta-grid">' + statsHtml + '</div>' +
                '<div class="action-details">' +
                    (notesHtml ? '<div class="action-detail-block"><div class="action-detail-title">Règles &amp; Précisions</div>' + notesHtml + '</div>' : '') +
                    variantsHtml +
                    statesHtml +
                    (compChips ? '<div class="action-chips-row">' + compChips + '</div>' : '') +
                    econHtml +
                '</div>' +
                '<div class="action-card-footer">' +
                    '<div class="action-refs-wrap">' + refsHtml + '</div>' +
                    '<div class="action-card-actions">' +
                        '<button type="button" class="action-mini-btn js-add-kit' + (inKitQty ? ' is-in-kit' : '') + '" data-obj-id="' + esc(o.id) + '" title="Ajouter à Mon Kit">' +
                            (inKitQty ? ('+ Kit (' + inKitQty + ')') : '+ Kit') +
                        '</button>' +
                        '<button type="button" class="action-mini-btn js-toggle-cmp' + (inCmp ? ' is-compared' : '') + '" data-obj-id="' + esc(o.id) + '" title="Comparer côte-à-côte">⚖</button>' +
                        '<button type="button" class="action-mini-btn js-toggle-fav' + (isFav ? ' is-fav' : '') + '" data-obj-id="' + esc(o.id) + '" title="Marquer en favori">' + (isFav ? '★' : '☆') + '</button>' +
                        ((isCompact || isClosed) ? '<button type="button" class="action-mini-btn js-toggle-details" aria-expanded="false">' + (isClosed ? 'Ouvrir ▾' : 'Détails ▾') + '</button>' : '') +
                        '<button type="button" class="action-mini-btn js-copy-link" data-obj-id="' + esc(o.id) + '" title="Copier le lien direct">#</button>' +
                    '</div>' +
                '</div>' +
            '</article>'
        );
    }

    function renderTableGroup(g) {
        var head = '';
        if (state.groupBy !== 'none') {
            head =
                '<div class="actions-group-header" data-ton="' + esc(g.ton) + '">' +
                    '<h2' + (g.idAttr ? ' id="' + esc(g.idAttr) + '"' : '') + ' class="actions-group-heading">' +
                        (g.code ? '<span class="section-symbol">' + esc(g.code) + '</span> ' : '') +
                        esc(g.title) +
                        '<span class="actions-group-count">' + g.items.length + '</span>' +
                    '</h2>' +
                    (g.desc ? '<p class="actions-group-desc">' + esc(g.desc) + '</p>' : '') +
                '</div>';
        }

        function thSort(label, sortKey) {
            var active = state.sort === sortKey;
            return '<th><button type="button" class="obj-th-sort' + (active ? ' active' : '') + '" data-sort-key="' + esc(sortKey) + '">' + esc(label) + (active ? ' ▾' : '') + '</button></th>';
        }

        var rows = g.items.map(function (o) {
            var inKit = kitCountFor(o.id);
            var inCmp = isCompared(o.id);
            var isFav = !!favs[o.id];
            var statKey = o.dlph || (o.protection != null ? ('Prot ' + o.protection + (o.encombrement_pa ? ' (+' + o.encombrement_pa + ' PA)' : '')) : (o.absorption != null ? ('Abs. ' + o.absorption) : (o.bonus_parade != null ? ('+' + o.bonus_parade + ' parade') : (o.soin_degats ? ('−' + o.soin_degats + ' dég.') : '—'))));
            var propTags = (o.proprietes || []).slice(0, 3).map(function (p) { return p.label; }).join(', ');
            return (
                '<tr id="obj-' + esc(o.id) + '">' +
                    '<td><a class="obj-tbl-name" href="' + esc(o.source.href) + '">' + esc(o.nom) + '</a><div class="obj-tbl-sub">' + esc(o.famille) + (propTags ? ' • ' + esc(propTags) : '') + '</div></td>' +
                    '<td><span class="obj-epoch-badge" data-ep="' + esc(o.epoque_min) + '">' + esc(o.epoque_label) + '</span></td>' +
                    '<td><code>' + esc(statKey) + '</code></td>' +
                    '<td>' + esc(o.degats_texte || '—') + '</td>' +
                    '<td>' + esc(o.portee_label || '—') + '</td>' +
                    '<td>' + esc(o.poids_label) + '</td>' +
                    '<td class="obj-tbl-price">' + esc(o.prix_label) + '</td>' +
                    '<td class="obj-tbl-actions">' +
                        '<button type="button" class="action-mini-btn js-add-kit' + (inKit ? ' is-in-kit' : '') + '" data-obj-id="' + esc(o.id) + '">' + (inKit ? '+' + inKit : '+Kit') + '</button>' +
                        '<button type="button" class="action-mini-btn js-toggle-cmp' + (inCmp ? ' is-compared' : '') + '" data-obj-id="' + esc(o.id) + '">⚖</button>' +
                        '<button type="button" class="action-mini-btn js-toggle-fav' + (isFav ? ' is-fav' : '') + '" data-obj-id="' + esc(o.id) + '">' + (isFav ? '★' : '☆') + '</button>' +
                    '</td>' +
                '</tr>'
            );
        }).join('');

        return (
            '<section class="actions-group">' +
                head +
                '<div class="obj-table-wrap">' +
                    '<table class="data-table obj-data-table">' +
                        '<thead><tr>' +
                            thSort('Objet', state.sort === 'nom-asc' ? 'nom-desc' : 'nom-asc') +
                            thSort('Époque', 'epoque-asc') +
                            '<th>DLPH / Défense</th>' +
                            thSort('Dégâts', 'degats-desc') +
                            thSort('Portée', 'portee-desc') +
                            thSort('Poids', state.sort === 'poids-asc' ? 'poids-desc' : 'poids-asc') +
                            thSort('Prix (CE)', state.sort === 'prix-asc' ? 'prix-desc' : 'prix-asc') +
                            '<th>Actions</th>' +
                        '</tr></thead>' +
                        '<tbody>' + rows + '</tbody>' +
                    '</table>' +
                '</div>' +
            '</section>'
        );
    }

    function renderKitPanel() {
        if (!DATA) return;
        var st = kitStats();
        var itemCount = 0;
        kit.items.forEach(function (it) { itemCount += it.qty; });

        if (els.openKitBtn) {
            els.openKitBtn.classList.toggle('active', state.kitOpen);
            els.openKitBtn.setAttribute('aria-expanded', state.kitOpen ? 'true' : 'false');
        }
        if (els.kitCount) els.kitCount.textContent = String(itemCount);
        if (els.telKitBudget) els.telKitBudget.textContent = fmtNum(st.totalCE) + ' / ' + fmtNum(kit.budget) + ' CE';
        if (els.telKitWeight) els.telKitWeight.textContent = fmtNum(st.totalL) + ' / ' + fmtNum(st.normalLimit) + ' L';
        if (els.telKitStatus) els.telKitStatus.textContent = st.weightStatus;
        if (els.telKitEnc) els.telKitEnc.textContent = '+' + st.totalPaMalus + ' PA';

        if (!els.kitPanel) return;
        els.kitPanel.hidden = !state.kitOpen;
        if (!state.kitOpen) return;

        var budgetPct = kit.budget > 0 ? Math.min(100, Math.round((st.totalCE / kit.budget) * 100)) : (st.totalCE > 0 ? 100 : 0);
        var budgetTone = st.remainingCE < 0 ? 'ruby' : (budgetPct >= 85 ? 'amber' : 'emerald');
        var weightPct = st.maxLimit > 0 ? Math.min(100, Math.round((st.totalL / st.maxLimit) * 100)) : 0;

        var alertsHtml = st.alerts.map(function (a) {
            return '<div class="obj-kit-alert ' + esc(a.tone) + '">' + esc(a.text) + '</div>';
        }).join('');

        if (els.kitDashboard) {
            els.kitDashboard.innerHTML =
                '<div class="obj-kit-meters">' +
                    '<div class="obj-meter-card">' +
                        '<div class="obj-meter-top"><span>Budget CE (' + esc(kit.epoch) + ')</span><strong>' + esc(fmtCE(st.totalCE)) + ' / ' + esc(fmtCE(kit.budget)) + '</strong></div>' +
                        '<div class="obj-meter"><div class="obj-meter-fill ' + budgetTone + '" style="width:' + budgetPct + '%"></div></div>' +
                        '<div class="obj-meter-sub">' + (st.remainingCE >= 0 ? ('Reste : ' + esc(fmtCE(st.remainingCE))) : ('Excédent : −' + esc(fmtCE(Math.abs(st.remainingCE))))) + ' &bull; Revente sac (50 %) : ' + esc(fmtCE(st.resaleCE)) + '</div>' +
                    '</div>' +
                    '<div class="obj-meter-card">' +
                        '<div class="obj-meter-top"><span>Portage (FOR ' + esc(kit.force) + ')</span><strong>' + esc(fmtNum(st.totalL)) + ' L (' + esc(fmtNum(st.totalKg)) + ' kg)</strong></div>' +
                        '<div class="obj-meter"><div class="obj-meter-fill ' + st.weightTone + '" style="width:' + weightPct + '%"></div></div>' +
                        '<div class="obj-meter-sub">Normal ≤ ' + esc(fmtNum(st.normalLimit)) + ' L &bull; Max ≤ ' + esc(fmtNum(st.maxLimit)) + ' L &bull; <strong>' + esc(st.weightStatus) + '</strong></div>' +
                    '</div>' +
                    '<div class="obj-meter-card">' +
                        '<div class="obj-meter-top"><span>Défense &amp; Mobilité Équipée</span><strong>' + (st.totalPaMalus ? ('+' + st.totalPaMalus + ' PA/act.') : '0 PA malus') + '</strong></div>' +
                        '<div class="obj-kit-def-chips">' +
                            '<span class="tag-pill cyan">Armure : Prot ' + st.maxProt + '</span>' +
                            '<span class="tag-pill violet">Champ : Abs. ' + st.maxAbsorp + '</span>' +
                            '<span class="tag-pill amber">Bouclier : +' + st.maxParade + '</span>' +
                        '</div>' +
                        '<div class="obj-meter-sub">Enc. armure/bouclier : +' + st.armorEnc + ' PA &bull; Surcharge : +' + st.surchargeMalus + ' PA</div>' +
                    '</div>' +
                '</div>' +
                (alertsHtml ? '<div class="obj-kit-alerts">' + alertsHtml + '</div>' : '');
        }

        if (els.kitItemsList) {
            if (!kit.items.length) {
                els.kitItemsList.innerHTML = '<div class="obj-kit-empty">Votre kit est vide. Cliquez sur <strong>+ Kit</strong> sur une fiche objet ou cliquez sur <strong>Charger dotation type</strong> ci-dessus.</div>';
            } else {
                els.kitItemsList.innerHTML = kit.items.map(function (entry, idx) {
                    var o = OBJ_MAP[entry.id];
                    if (!o) return '';
                    var v = (entry.variantIdx >= 0 && o.variantes && o.variantes[entry.variantIdx]) ? o.variantes[entry.variantIdx] : null;
                    var unitCE = v ? v.prix_ce : o.prix_ce;
                    var unitL = v ? v.poids_livres : o.poids_livres;
                    var varSelect = '';
                    if (o.variantes && o.variantes.length) {
                        varSelect = '<select class="obj-kit-var-sel js-kit-variant" data-kit-idx="' + idx + '">' +
                            '<option value="-1"' + (entry.variantIdx < 0 ? ' selected' : '') + '>Base (' + esc(o.prix_label) + ')</option>' +
                            o.variantes.map(function (vr, vi) {
                                return '<option value="' + vi + '"' + (entry.variantIdx === vi ? ' selected' : '') + '>' + esc(vr.nom) + ' (' + esc(fmtCE(vr.prix_ce)) + ')</option>';
                            }).join('') +
                        '</select>';
                    }
                    var canEquip = (o.emplacement === 'arme' || o.emplacement === 'armure' || o.emplacement === 'bouclier' || o.emplacement === 'champ' || o.emplacement === 'implant');
                    return (
                        '<div class="obj-kit-row">' +
                            '<div class="obj-kit-row-main">' +
                                (canEquip
                                    ? '<label class="obj-kit-equip-chk" title="Équipé sur soi (compte pour protection/encombrement/conflits)"><input type="checkbox" class="js-kit-equip" data-kit-idx="' + idx + '"' + (entry.equipe ? ' checked' : '') + '><span>Équipé</span></label>'
                                    : '<span class="obj-kit-bag-tag">Sac</span>') +
                                '<a href="#obj-' + esc(o.id) + '" class="obj-kit-name">' + esc(o.nom) + '</a>' +
                                '<span class="obj-epoch-badge" data-ep="' + esc(o.epoque_min) + '">' + esc(o.epoque_label) + '</span>' +
                                varSelect +
                            '</div>' +
                            '<div class="obj-kit-row-right">' +
                                '<span class="obj-kit-sub">' + esc(fmtNum(unitL * entry.qty)) + ' L &bull; ' + esc(fmtCE(unitCE * entry.qty)) + '</span>' +
                                '<div class="obj-qty-ctrl">' +
                                    '<button type="button" class="action-mini-btn js-kit-dec" data-kit-idx="' + idx + '">−</button>' +
                                    '<span class="obj-qty-val">' + entry.qty + '</span>' +
                                    '<button type="button" class="action-mini-btn js-kit-inc" data-kit-idx="' + idx + '">+</button>' +
                                    '<button type="button" class="action-mini-btn obj-danger-btn js-kit-del" data-kit-idx="' + idx + '" title="Retirer">✕</button>' +
                                '</div>' +
                            '</div>' +
                        '</div>'
                    );
                }).join('');
            }
        }
    }

    function renderComparePanel() {
        if (els.compareCount) els.compareCount.textContent = state.compareIds.length + '/' + MAX_COMPARE;
        if (els.cmpBadgeCount) els.cmpBadgeCount.textContent = state.compareIds.length + ' / ' + MAX_COMPARE;
        if (els.openCompareBtn) {
            els.openCompareBtn.classList.toggle('active', state.compareOpen);
            els.openCompareBtn.setAttribute('aria-expanded', state.compareOpen ? 'true' : 'false');
        }
        if (!els.comparePanel) return;
        els.comparePanel.hidden = !state.compareOpen;
        if (!state.compareOpen || !els.compareTableWrap) return;

        var items = state.compareIds.map(function (id) { return OBJ_MAP[id]; }).filter(Boolean);
        if (!items.length) {
            els.compareTableWrap.innerHTML = '<div class="obj-kit-empty">Aucun objet sélectionné. Cliquez sur <strong>⚖</strong> sur 2 à 4 fiches pour comparer leurs statistiques côte à côte.</div>';
            return;
        }

        function bestVal(arr, getter, mode) {
            var vals = arr.map(getter).filter(function (v) { return typeof v === 'number' && !isNaN(v); });
            if (vals.length < 2) return null;
            return mode === 'min' ? Math.min.apply(null, vals) : Math.max.apply(null, vals);
        }

        var bestPrice = bestVal(items, function (o) { return o.prix_ce; }, 'min');
        var bestWeight = bestVal(items, function (o) { return o.poids_livres > 0 ? o.poids_livres : null; }, 'min');
        var bestDmg = bestVal(items, function (o) { return o.degats; }, 'max');
        var bestPerf = bestVal(items, function (o) { return o.perforante || null; }, 'max');
        var bestRange = bestVal(items, function (o) { return adjustedThrowRange(o) || o.portee_cases; }, 'max');
        var bestProt = bestVal(items, function (o) { return (o.protection || 0) + (o.absorption || 0) + (o.bonus_parade || 0); }, 'max');

        var colsHead = '<tr><th>Critère</th>' + items.map(function (o) {
            return '<th><div class="obj-cmp-head"><a href="#obj-' + esc(o.id) + '">' + esc(o.nom) + '</a><button type="button" class="action-mini-btn js-cmp-remove" data-obj-id="' + esc(o.id) + '">✕</button></div></th>';
        }).join('') + '</tr>';

        function row(label, fn, bestTarget, numGetter) {
            return '<tr><th>' + esc(label) + '</th>' + items.map(function (o) {
                var isBest = bestTarget != null && numGetter && numGetter(o) === bestTarget;
                return '<td' + (isBest ? ' class="is-best"' : '') + '>' + fn(o) + '</td>';
            }).join('') + '</tr>';
        }

        var body =
            row('Catégorie', function (o) { return esc(CATEGORY_MAP[o.categorie] ? CATEGORY_MAP[o.categorie].nav : o.categorie); }) +
            row('Époque', function (o) { return '<span class="obj-epoch-badge" data-ep="' + esc(o.epoque_min) + '">' + esc(o.epoque_label) + '</span>'; }) +
            row('Code DLPH', function (o) { return o.dlph ? ('<code>' + esc(o.dlph) + '</code>') : '—'; }) +
            row('Prix (CE)', function (o) { return esc(o.prix_label); }, bestPrice, function (o) { return o.prix_ce; }) +
            row('Poids', function (o) { return esc(o.poids_label); }, bestWeight, function (o) { return o.poids_livres > 0 ? o.poids_livres : null; }) +
            row('Dégâts', function (o) { return esc(o.degats_texte || '—'); }, bestDmg, function (o) { return o.degats; }) +
            row('Perforation', function (o) { return o.perforante ? ('Perf. ' + o.perforante) : '—'; }, bestPerf, function (o) { return o.perforante || null; }) +
            row('Portée', function (o) {
                var adj = adjustedThrowRange(o);
                return esc((o.portee_label || '—') + (adj && adj !== o.portee_jet_cases ? (' (FOR ' + kit.force + ' : ' + adj + 'P)') : ''));
            }, bestRange, function (o) { return adjustedThrowRange(o) || o.portee_cases; }) +
            row('Défense / Soin', function (o) {
                if (o.protection != null) return 'Prot ' + o.protection + (o.protection_max ? '–' + o.protection_max : '');
                if (o.absorption != null) return 'Absorbe ' + o.absorption;
                if (o.bonus_parade != null) return '+' + o.bonus_parade + ' parade';
                if (o.soin_degats != null) return '−' + o.soin_degats + ' dégâts';
                return '—';
            }, bestProt, function (o) { return (o.protection || 0) + (o.absorption || 0) + (o.bonus_parade || 0); }) +
            row('Encombrement', function (o) { return o.encombrement_pa ? ('+' + o.encombrement_pa + ' PA') : '0 PA'; }) +
            row('Maniement / Mun.', function (o) { return esc(o.mains + (o.munitions ? ' • ' + o.munitions : '')); }) +
            row('Propriétés', function (o) { return o.proprietes && o.proprietes.length ? esc(o.proprietes.map(function (p) { return p.label; }).join(', ')) : '—'; }) +
            row('Compétences', function (o) { return o.competences && o.competences.length ? esc(o.competences.join(', ')) : '—'; }) +
            row('Revente (50 %) / Rép. (25 %)', function (o) {
                return esc(fmtCE(Math.round(o.prix_ce * 0.5)) + ' / ' + (o.consommable ? '—' : fmtCE(Math.round(o.prix_ce * (kit.recupVardien ? 0.125 : 0.25)))));
            });

        els.compareTableWrap.innerHTML = '<table class="data-table obj-compare-table"><thead>' + colsHead + '</thead><tbody>' + body + '</tbody></table>';
    }

    function updateEpochUI() {
        if (els.epochBar) {
            els.epochBar.querySelectorAll('button[data-ep]').forEach(function (btn) {
                btn.classList.toggle('active', btn.dataset.ep === state.epoch);
            });
        }
        if (els.epochModeExact && els.epochModeUpto) {
            els.epochModeExact.classList.toggle('active', state.epochMode === 'exact');
            els.epochModeUpto.classList.toggle('active', state.epochMode === 'upto');
            els.epochModeExact.setAttribute('aria-pressed', state.epochMode === 'exact' ? 'true' : 'false');
            els.epochModeUpto.setAttribute('aria-pressed', state.epochMode === 'upto' ? 'true' : 'false');
        }
        if (els.epochBudgetInfo) {
            if (state.epoch === 'all') {
                els.epochBudgetInfo.textContent = 'Toutes époques [E0–E5]';
            } else {
                var b = BUDGET_MAP[state.epoch];
                els.epochBudgetInfo.textContent = b
                    ? (b.epoque + ' ' + b.nom + ' — Budget départ : ' + fmtCE(b.budget_ce))
                    : state.epoch;
            }
        }
    }

    function render() {
        if (!DATA) return;
        var st = kitStats();
        var filtered = DATA.objets.filter(function (o) { return matches(o, st.remainingCE); });
        var sorted = sortObjects(filtered);
        var groups = groupObjects(sorted);

        updateEpochUI();

        if (els.catPills) {
            els.catPills.querySelectorAll('button[data-cat]').forEach(function (btn) {
                btn.classList.toggle('active', btn.dataset.cat === state.categorie);
            });
        }
        if (els.propPills) {
            els.propPills.querySelectorAll('button[data-prop]').forEach(function (btn) {
                var act = state.props.indexOf(btn.dataset.prop) !== -1;
                btn.classList.toggle('active', act);
                btn.setAttribute('aria-pressed', act ? 'true' : 'false');
            });
        }

        var togglePairs = [
            [els.toggleConsumable, state.consumableOnly],
            [els.toggleNoEnc, state.noEncOnly],
            [els.toggleStealth, state.stealthOnly],
            [els.togglePiercing, state.piercingOnly],
            [els.toggleInBudget, state.inBudgetOnly],
            [els.toggleFavs, state.favsOnly]
        ];
        togglePairs.forEach(function (pair) {
            if (pair[0]) {
                pair[0].classList.toggle('active', pair[1]);
                pair[0].setAttribute('aria-pressed', pair[1] ? 'true' : 'false');
            }
        });
        if (els.favCount) els.favCount.textContent = String(Object.keys(favs).length);

        var viewMap = {
            detailed: els.viewDetailed,
            compact: els.viewCompact,
            closed: els.viewClosed,
            table: els.viewTable
        };
        Object.keys(viewMap).forEach(function (vk) {
            if (viewMap[vk]) viewMap[vk].classList.toggle('active', state.view === vk);
        });
        if (els.searchClear) els.searchClear.hidden = !state.query;

        renderKitPanel();
        renderComparePanel();

        if (els.countBar) {
            var groupLabel = els.groupBy && els.groupBy.options[els.groupBy.selectedIndex]
                ? els.groupBy.options[els.groupBy.selectedIndex].text : state.groupBy;
            var sortLabel = els.sort && els.sort.options[els.sort.selectedIndex]
                ? els.sort.options[els.sort.selectedIndex].text : state.sort;
            els.countBar.innerHTML =
                '<span><strong>' + filtered.length + '</strong> / ' + DATA.objets.length + ' objet' + (DATA.objets.length > 1 ? 's' : '') + ' affiché' + (filtered.length > 1 ? 's' : '') + '</span>' +
                '<span class="actions-stats-meta">Tri : ' + esc(sortLabel) + ' &bull; Groupe : ' + esc(groupLabel) + '</span>';
        }
        if (els.telShown) els.telShown.textContent = filtered.length + ' / ' + DATA.objets.length;

        if (!filtered.length) {
            els.container.innerHTML =
                '<div class="actions-empty-state">' +
                    '<h4>Aucun objet ne correspond aux critères actifs</h4>' +
                    '<p>Essayez d\'élargir l\'époque (mode ≤ Époque), de décocher une propriété ou de réinitialiser les filtres.</p>' +
                    '<button type="button" class="action-reset-btn" id="objEmptyResetBtn">Tout réinitialiser</button>' +
                '</div>';
            var eb = document.getElementById('objEmptyResetBtn');
            if (eb) eb.addEventListener('click', resetFilters);
            return;
        }

        if (state.view === 'table') {
            els.container.innerHTML = groups.map(renderTableGroup).join('');
        } else {
            var gridCls = 'actions-grid' + (state.view === 'compact' ? ' compact-grid' : (state.view === 'closed' ? ' closed-grid' : ''));
            els.container.innerHTML = groups.map(function (g) {
                var head = '';
                if (state.groupBy !== 'none') {
                    head =
                        '<div class="actions-group-header" data-ton="' + esc(g.ton) + '">' +
                            '<h2' + (g.idAttr ? ' id="' + esc(g.idAttr) + '"' : '') + ' class="actions-group-heading">' +
                                (g.code ? '<span class="section-symbol">' + esc(g.code) + '</span> ' : '') +
                                esc(g.title) +
                                '<span class="actions-group-count">' + g.items.length + '</span>' +
                            '</h2>' +
                            (g.desc ? '<p class="actions-group-desc">' + esc(g.desc) + '</p>' : '') +
                        '</div>';
                }
                return '<section class="actions-group">' + head + '<div class="' + gridCls + '">' + g.items.map(renderCard).join('') + '</div></section>';
            }).join('');
        }

        if (window.bindCodexTips) window.bindCodexTips(els.container);
    }

    function syncInputsFromState() {
        if (els.search) els.search.value = state.query;
        if (els.sort) els.sort.value = state.sort;
        if (els.groupBy) els.groupBy.value = state.groupBy;
        if (els.filterFamily) els.filterFamily.value = state.family;
        if (els.filterSkill) els.filterSkill.value = state.skill;
        if (els.filterHands) els.filterHands.value = state.hands;
        if (els.filterEnc) els.filterEnc.value = state.enc;
        if (els.filterPrice) els.filterPrice.value = state.price;
    }

    function syncKitInputs() {
        if (els.kitForce) els.kitForce.value = String(kit.force);
        if (els.kitEpoch) els.kitEpoch.value = kit.epoch;
        if (els.kitBudget) els.kitBudget.value = String(kit.budget);
        if (els.kitTechGuerrier) els.kitTechGuerrier.checked = kit.techGuerrier;
        if (els.kitRecupVardien) els.kitRecupVardien.checked = kit.recupVardien;
    }

    function resetFilters() {
        state.query = '';
        state.epoch = 'all';
        state.epochMode = 'exact';
        state.categorie = 'all';
        state.family = 'all';
        state.skill = 'all';
        state.hands = 'all';
        state.enc = 'all';
        state.price = 'all';
        state.props = [];
        state.consumableOnly = false;
        state.noEncOnly = false;
        state.stealthOnly = false;
        state.piercingOnly = false;
        state.inBudgetOnly = false;
        state.favsOnly = false;
        syncInputsFromState();
        render();
    }

    function toggleCardExpanded(card, forceOpen) {
        if (!card) return;
        var expanded = forceOpen === true
            ? (card.classList.add('is-expanded'), true)
            : card.classList.toggle('is-expanded');
        var btn = card.querySelector ? card.querySelector('.js-toggle-details') : null;
        if (btn) {
            btn.setAttribute('aria-expanded', expanded ? 'true' : 'false');
            btn.textContent = card.classList.contains('closed')
                ? (expanded ? 'Fermer ▴' : 'Ouvrir ▾')
                : (expanded ? 'Masquer ▴' : 'Détails ▾');
        }
    }

    function handleHash() {
        var h = (location.hash || '').slice(1);
        if (!h || !DATA) return;

        if (h.indexOf('cat-') === 0) {
            var catId = h.slice(4);
            if (CATEGORY_MAP[catId]) {
                var need = (state.groupBy !== 'categorie' || (state.categorie !== 'all' && state.categorie !== catId));
                if (state.groupBy !== 'categorie') state.groupBy = 'categorie';
                if (state.categorie !== 'all' && state.categorie !== catId) state.categorie = 'all';
                if (need) {
                    syncInputsFromState();
                    render();
                }
            }
            var tc = document.getElementById(h);
            if (tc && tc.scrollIntoView) tc.scrollIntoView();
            return;
        }

        if (h.indexOf('obj-') === 0) {
            var targetObj = document.getElementById(h);
            if (!targetObj) {
                resetFilters();
                targetObj = document.getElementById(h);
            }
            if (targetObj) {
                if (state.view === 'compact' || state.view === 'closed') {
                    toggleCardExpanded(targetObj, true);
                }
                if (targetObj.scrollIntoView) targetObj.scrollIntoView();
                targetObj.classList.remove('highlight-pulse');
                void targetObj.offsetWidth;
                targetObj.classList.add('highlight-pulse');
            }
        }
    }

    function populateStaticUI() {
        // Aide-mémoire drawer
        if (els.refBody && DATA.regles_ref) {
            var ref = DATA.regles_ref;
            var dlphHtml = (ref.code_dlph.segments || []).map(function (s) {
                return '<li><strong>[' + esc(s.lettre) + ']</strong> ' + esc(s.signification) + '</li>';
            }).join('');
            var budHtml = (ref.budgets_depart || []).map(function (b) {
                return '<tr><td><span class="obj-epoch-badge" data-ep="' + esc(b.epoque.slice(1)) + '">' + esc(b.epoque) + '</span> ' + esc(b.nom) + '</td><td><strong>' + esc(fmtCE(b.budget_ce)) + '</strong></td><td>' + esc(b.acces) + '</td></tr>';
            }).join('');
            var etatsHtml = (ref.etats_objets || []).map(function (e) {
                return '<li><strong>' + esc(e.nom) + '</strong> (' + esc(e.statut) + ') : ' + esc(e.effet) + '</li>';
            }).join('');
            els.refBody.innerHTML =
                '<div class="obj-ref-grid">' +
                    '<div class="obj-ref-card">' +
                        '<h4>Standard DLPH &amp; Portage</h4>' +
                        '<ul>' + dlphHtml + '</ul>' +
                        '<p class="obj-ref-note"><strong>Portage :</strong> ≤ <code>FOR × 15 L</code> libre • jusqu\'à <code>FOR × 25 L</code> surchargé (+1 PA/action &amp; case) • au-delà : bloqué.<br>' +
                        '<strong>Revente :</strong> 50 % neuf, 25 % endommagé, 10 % détruit.<br>' +
                        '<strong>Réparation Détruit :</strong> atelier + 25 % prix neuf (12,5 % Vardien) + Artisanat/Ingénierie (Diff 5 E0–E2, 7 E3–E4, 9 E5).</p>' +
                    '</div>' +
                    '<div class="obj-ref-card">' +
                        '<h4>Budgets de Départ [E0–E5]</h4>' +
                        '<table class="data-table obj-ref-table"><thead><tr><th>Époque</th><th>Budget</th><th>Accès</th></tr></thead><tbody>' + budHtml + '</tbody></table>' +
                    '</div>' +
                    '<div class="obj-ref-card obj-ref-card-wide">' +
                        '<h4>5 États des Objets (Livre VI §24.5)</h4>' +
                        '<ul>' + etatsHtml + '</ul>' +
                    '</div>' +
                '</div>';
        }

        // Epoch pills
        if (els.epochBar) {
            var epHtml = '<button type="button" class="obj-epoch-pill active" data-ep="all">Toutes <span>' + DATA.objets.length + '</span></button>';
            DATA.regles_ref.budgets_depart.forEach(function (b) {
                var cnt = DATA.objets.filter(function (o) { return o.epoques.indexOf(b.epoque) !== -1; }).length;
                epHtml += '<button type="button" class="obj-epoch-pill" data-ep="' + esc(b.epoque) + '">' + esc(b.epoque) + ' · ' + esc(b.nom) + ' <span>' + cnt + '</span></button>';
            });
            els.epochBar.innerHTML = epHtml;
        }

        // Category pills
        if (els.catPills) {
            var counts = {};
            DATA.objets.forEach(function (o) { counts[o.categorie] = (counts[o.categorie] || 0) + 1; });
            var cHtml = '<button type="button" class="actions-cat-pill active" data-cat="all">Toutes <span>' + DATA.objets.length + '</span></button>';
            DATA.categories.forEach(function (c) {
                cHtml += '<button type="button" class="actions-cat-pill" data-cat="' + esc(c.id) + '" data-ton="' + esc(c.ton) + '">' +
                    esc(c.nav) + ' <span>' + (counts[c.id] || 0) + '</span></button>';
            });
            els.catPills.innerHTML = cHtml;
        }

        // Property pills
        if (els.propPills) {
            var pCounts = {};
            DATA.objets.forEach(function (o) {
                (o.proprietes || []).forEach(function (p) {
                    pCounts[p.id] = (pCounts[p.id] || 0) + 1;
                });
            });
            els.propPills.innerHTML = DATA.proprietes.map(function (p) {
                return '<button type="button" class="obj-prop-pill rule-ref" data-prop="' + esc(p.id) + '" data-tip="' + esc(p.tip) + '" aria-pressed="false">' +
                    esc(p.nom) + ' <span>' + (pCounts[p.id] || 0) + '</span></button>';
            }).join('');
            if (window.bindCodexTips) window.bindCodexTips(els.propPills);
        }

        // Dynamic selects: Family & Skill
        var famSet = {};
        var skillSet = {};
        DATA.objets.forEach(function (o) {
            if (o.famille) famSet[o.famille] = (famSet[o.famille] || 0) + 1;
            (o.competences || []).forEach(function (sk) {
                skillSet[sk] = (skillSet[sk] || 0) + 1;
            });
        });
        if (els.filterFamily) {
            Object.keys(famSet).sort(function (a, b) { return a.localeCompare(b, 'fr'); }).forEach(function (f) {
                var opt = document.createElement('option');
                opt.value = f;
                opt.textContent = f + ' (' + famSet[f] + ')';
                els.filterFamily.appendChild(opt);
            });
        }
        if (els.filterSkill) {
            Object.keys(skillSet).sort(function (a, b) { return a.localeCompare(b, 'fr'); }).forEach(function (sk) {
                var opt = document.createElement('option');
                opt.value = sk;
                opt.textContent = sk + ' (' + skillSet[sk] + ')';
                els.filterSkill.appendChild(opt);
            });
        }

        // Telemetry
        var weapons = 0, defense = 0, gear = 0;
        DATA.objets.forEach(function (o) {
            if (o.famille === 'Arme de mêlée' || o.famille === 'Arme à distance' || o.famille === 'Explosif / Zone' || o.famille === 'Accessoire') weapons++;
            else if (o.famille === 'Armure' || o.famille === 'Bouclier' || o.famille === 'Champ / Combinaison') defense++;
            else gear++;
        });
        var setTxt = function (id, val) {
            var el = document.getElementById(id);
            if (el) el.textContent = String(val);
        };
        setTxt('objBadgeCount', DATA.objets.length + ' Objets Canoniques');
        setTxt('telObjTotal', DATA.objets.length + ' fiches');
        setTxt('telObjShown', DATA.objets.length + ' / ' + DATA.objets.length);
        setTxt('telObjWeapons', weapons);
        setTxt('telObjDefense', defense);
        setTxt('telObjGear', gear);
    }

    function addToKit(objId) {
        var o = OBJ_MAP[objId];
        if (!o) return;
        var found = null;
        for (var i = 0; i < kit.items.length; i++) {
            if (kit.items[i].id === objId && kit.items[i].variantIdx === -1) {
                found = kit.items[i];
                break;
            }
        }
        if (found) {
            found.qty += 1;
        } else {
            kit.items.push({ id: objId, qty: 1, equipe: true, variantIdx: -1 });
        }
        saveKit();
        render();
    }

    function toggleCompare(objId) {
        var idx = state.compareIds.indexOf(objId);
        if (idx !== -1) {
            state.compareIds.splice(idx, 1);
        } else {
            if (state.compareIds.length >= MAX_COMPARE) {
                state.compareIds.shift();
            }
            state.compareIds.push(objId);
            state.compareOpen = true;
        }
        render();
    }

    function toggleFav(objId) {
        if (favs[objId]) delete favs[objId];
        else favs[objId] = true;
        saveFavs();
        render();
    }

    function exportKitMarkdown() {
        var st = kitStats();
        var lines = [
            '### Kit Équipement (' + kit.epoch + ' — FOR ' + kit.force + ')',
            '- Budget : ' + fmtCE(st.totalCE) + ' / ' + fmtCE(kit.budget) + ' (reste ' + fmtCE(st.remainingCE) + ')',
            '- Portage : ' + fmtNum(st.totalL) + ' L (' + fmtNum(st.totalKg) + ' kg) — ' + st.weightStatus + ' (normal ≤ ' + fmtNum(st.normalLimit) + ' L, max ≤ ' + fmtNum(st.maxLimit) + ' L)',
            '- Encombrement effectif : +' + st.totalPaMalus + ' PA/action | Défense : Prot ' + st.maxProt + ' / Champ ' + st.maxAbsorp + ' / Bouclier +' + st.maxParade,
            ''
        ];
        kit.items.forEach(function (entry) {
            var o = OBJ_MAP[entry.id];
            if (!o) return;
            var v = (entry.variantIdx >= 0 && o.variantes && o.variantes[entry.variantIdx]) ? o.variantes[entry.variantIdx] : null;
            var nom = v ? v.nom : o.nom;
            var ce = v ? v.prix_ce : o.prix_ce;
            var l = v ? v.poids_livres : o.poids_livres;
            lines.push('- ' + entry.qty + '× **' + nom + '** [' + o.epoque_label + '] — ' + (o.dlph ? o.dlph + ' — ' : '') + fmtNum(l * entry.qty) + ' L — ' + fmtCE(ce * entry.qty));
        });
        return lines.join('\n');
    }

    function bindEvents() {
        if (els.search) {
            els.search.addEventListener('input', function () {
                state.query = els.search.value;
                render();
            });
        }
        if (els.searchClear) {
            els.searchClear.addEventListener('click', function () {
                state.query = '';
                if (els.search) { els.search.value = ''; els.search.focus(); }
                render();
            });
        }
        if (els.epochBar) {
            els.epochBar.addEventListener('click', function (e) {
                var btn = e.target.closest('button[data-ep]');
                if (!btn) return;
                state.epoch = btn.dataset.ep;
                render();
            });
        }
        if (els.epochModeExact) {
            els.epochModeExact.addEventListener('click', function () { state.epochMode = 'exact'; render(); });
        }
        if (els.epochModeUpto) {
            els.epochModeUpto.addEventListener('click', function () { state.epochMode = 'upto'; render(); });
        }
        if (els.sort) els.sort.addEventListener('change', function () { state.sort = els.sort.value; render(); });
        if (els.groupBy) els.groupBy.addEventListener('change', function () { state.groupBy = els.groupBy.value; render(); });
        if (els.filterFamily) els.filterFamily.addEventListener('change', function () { state.family = els.filterFamily.value; render(); });
        if (els.filterSkill) els.filterSkill.addEventListener('change', function () { state.skill = els.filterSkill.value; render(); });
        if (els.filterHands) els.filterHands.addEventListener('change', function () { state.hands = els.filterHands.value; render(); });
        if (els.filterEnc) els.filterEnc.addEventListener('change', function () { state.enc = els.filterEnc.value; render(); });
        if (els.filterPrice) els.filterPrice.addEventListener('change', function () { state.price = els.filterPrice.value; render(); });

        if (els.catPills) {
            els.catPills.addEventListener('click', function (e) {
                var btn = e.target.closest('button[data-cat]');
                if (!btn) return;
                state.categorie = btn.dataset.cat;
                render();
            });
        }
        if (els.propPills) {
            els.propPills.addEventListener('click', function (e) {
                var btn = e.target.closest('button[data-prop]');
                if (!btn) return;
                var pid = btn.dataset.prop;
                var idx = state.props.indexOf(pid);
                if (idx === -1) state.props.push(pid);
                else state.props.splice(idx, 1);
                render();
            });
        }

        if (els.toggleConsumable) els.toggleConsumable.addEventListener('click', function () { state.consumableOnly = !state.consumableOnly; render(); });
        if (els.toggleNoEnc) els.toggleNoEnc.addEventListener('click', function () { state.noEncOnly = !state.noEncOnly; render(); });
        if (els.toggleStealth) els.toggleStealth.addEventListener('click', function () { state.stealthOnly = !state.stealthOnly; render(); });
        if (els.togglePiercing) els.togglePiercing.addEventListener('click', function () { state.piercingOnly = !state.piercingOnly; render(); });
        if (els.toggleInBudget) els.toggleInBudget.addEventListener('click', function () { state.inBudgetOnly = !state.inBudgetOnly; render(); });
        if (els.toggleFavs) els.toggleFavs.addEventListener('click', function () { state.favsOnly = !state.favsOnly; render(); });

        if (els.viewDetailed) els.viewDetailed.addEventListener('click', function () { state.view = 'detailed'; render(); });
        if (els.viewCompact) els.viewCompact.addEventListener('click', function () { state.view = 'compact'; render(); });
        if (els.viewClosed) els.viewClosed.addEventListener('click', function () { state.view = 'closed'; render(); });
        if (els.viewTable) els.viewTable.addEventListener('click', function () { state.view = 'table'; render(); });

        if (els.openCompareBtn) els.openCompareBtn.addEventListener('click', function () { state.compareOpen = !state.compareOpen; renderComparePanel(); });
        if (els.cmpCloseBtn) els.cmpCloseBtn.addEventListener('click', function () { state.compareOpen = false; renderComparePanel(); });
        if (els.cmpClearBtn) els.cmpClearBtn.addEventListener('click', function () { state.compareIds = []; render(); });

        var openKitFn = function () {
            state.kitOpen = !state.kitOpen;
            renderKitPanel();
            if (state.kitOpen && els.kitPanel && els.kitPanel.scrollIntoView) {
                els.kitPanel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            }
        };
        if (els.openKitBtn) els.openKitBtn.addEventListener('click', openKitFn);
        if (els.telOpenKitBtn) {
            els.telOpenKitBtn.addEventListener('click', function () {
                state.kitOpen = true;
                renderKitPanel();
                if (els.kitPanel && els.kitPanel.scrollIntoView) els.kitPanel.scrollIntoView({ behavior: 'smooth', block: 'start' });
            });
        }
        if (els.kitCloseBtn) els.kitCloseBtn.addEventListener('click', function () { state.kitOpen = false; renderKitPanel(); });

        if (els.resetBtn) els.resetBtn.addEventListener('click', resetFilters);

        // Kit controls
        if (els.kitForce) {
            els.kitForce.addEventListener('input', function () {
                kit.force = Math.min(10, Math.max(1, parseInt(els.kitForce.value, 10) || 3));
                saveKit();
                render();
            });
        }
        if (els.kitEpoch) {
            els.kitEpoch.addEventListener('change', function () {
                kit.epoch = els.kitEpoch.value;
                var b = BUDGET_MAP[kit.epoch];
                if (b) {
                    kit.budget = b.budget_ce;
                    if (els.kitBudget) els.kitBudget.value = String(kit.budget);
                }
                saveKit();
                render();
            });
        }
        if (els.kitBudget) {
            els.kitBudget.addEventListener('input', function () {
                kit.budget = Math.max(0, parseInt(els.kitBudget.value, 10) || 0);
                saveKit();
                renderKitPanel();
                if (state.inBudgetOnly) render();
            });
        }
        if (els.kitTechGuerrier) {
            els.kitTechGuerrier.addEventListener('change', function () {
                kit.techGuerrier = els.kitTechGuerrier.checked;
                saveKit();
                renderKitPanel();
            });
        }
        if (els.kitRecupVardien) {
            els.kitRecupVardien.addEventListener('change', function () {
                kit.recupVardien = els.kitRecupVardien.checked;
                saveKit();
                render();
            });
        }
        if (els.kitLoadPresetBtn) {
            els.kitLoadPresetBtn.addEventListener('click', function () {
                var p = PRESET_MAP[kit.epoch];
                if (!p) return;
                kit.items = p.items.map(function (it) {
                    return { id: it.id, qty: it.qty, equipe: !!it.equipe, variantIdx: -1 };
                });
                var b = BUDGET_MAP[kit.epoch];
                if (b) {
                    kit.budget = b.budget_ce;
                    if (els.kitBudget) els.kitBudget.value = String(kit.budget);
                }
                saveKit();
                render();
            });
        }
        if (els.kitCopyBtn) {
            els.kitCopyBtn.addEventListener('click', function () {
                var txt = exportKitMarkdown();
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(txt).catch(function () {});
                }
                var orig = els.kitCopyBtn.textContent;
                els.kitCopyBtn.textContent = 'Copié ✓';
                els.kitCopyBtn.classList.add('copied');
                setTimeout(function () {
                    els.kitCopyBtn.textContent = orig;
                    els.kitCopyBtn.classList.remove('copied');
                }, 1400);
            });
        }
        if (els.kitExportBtn) {
            els.kitExportBtn.addEventListener('click', function () {
                var blob = new Blob([JSON.stringify(kit, null, 2)], { type: 'application/json' });
                var url = URL.createObjectURL(blob);
                var a = document.createElement('a');
                a.href = url;
                a.download = 'kit_codex_' + kit.epoch.toLowerCase() + '.json';
                document.body.appendChild(a);
                a.click();
                a.remove();
                setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
            });
        }
        if (els.kitImportBtn && els.kitImportFile) {
            els.kitImportBtn.addEventListener('click', function () { els.kitImportFile.click(); });
            els.kitImportFile.addEventListener('change', function () {
                var file = els.kitImportFile.files && els.kitImportFile.files[0];
                if (!file) return;
                var reader = new FileReader();
                reader.onload = function () {
                    try {
                        var parsed = JSON.parse(String(reader.result || '{}'));
                        if (!parsed || !Array.isArray(parsed.items)) throw new Error('Format invalide');
                        kit.force = Math.min(10, Math.max(1, parseInt(parsed.force, 10) || 3));
                        kit.epoch = BUDGET_MAP[parsed.epoch] ? parsed.epoch : kit.epoch;
                        kit.budget = Math.max(0, parseInt(parsed.budget, 10) || 0);
                        kit.techGuerrier = !!parsed.techGuerrier;
                        kit.recupVardien = !!parsed.recupVardien;
                        kit.items = parsed.items.filter(function (it) { return it && OBJ_MAP[it.id]; }).map(function (it) {
                            return {
                                id: it.id,
                                qty: Math.max(1, parseInt(it.qty, 10) || 1),
                                equipe: it.equipe !== false,
                                variantIdx: typeof it.variantIdx === 'number' ? it.variantIdx : -1
                            };
                        });
                        saveKit();
                        syncKitInputs();
                        render();
                    } catch (err) {
                        alert('Fichier JSON de kit invalide.');
                    }
                    els.kitImportFile.value = '';
                };
                reader.readAsText(file);
            });
        }
        if (els.kitClearBtn) {
            els.kitClearBtn.addEventListener('click', function () {
                kit.items = [];
                saveKit();
                render();
            });
        }

        // Delegation on Kit items list
        if (els.kitItemsList) {
            els.kitItemsList.addEventListener('click', function (e) {
                var dec = e.target.closest('.js-kit-dec');
                if (dec) {
                    var i1 = parseInt(dec.dataset.kitIdx, 10);
                    if (kit.items[i1]) {
                        if (kit.items[i1].qty > 1) kit.items[i1].qty -= 1;
                        else kit.items.splice(i1, 1);
                        saveKit();
                        render();
                    }
                    return;
                }
                var inc = e.target.closest('.js-kit-inc');
                if (inc) {
                    var i2 = parseInt(inc.dataset.kitIdx, 10);
                    if (kit.items[i2]) {
                        kit.items[i2].qty += 1;
                        saveKit();
                        render();
                    }
                    return;
                }
                var del = e.target.closest('.js-kit-del');
                if (del) {
                    var i3 = parseInt(del.dataset.kitIdx, 10);
                    kit.items.splice(i3, 1);
                    saveKit();
                    render();
                }
            });
            els.kitItemsList.addEventListener('change', function (e) {
                var chk = e.target.closest('.js-kit-equip');
                if (chk) {
                    var idx = parseInt(chk.dataset.kitIdx, 10);
                    if (kit.items[idx]) {
                        kit.items[idx].equipe = chk.checked;
                        saveKit();
                        renderKitPanel();
                    }
                    return;
                }
                var sel = e.target.closest('.js-kit-variant');
                if (sel) {
                    var vidx = parseInt(sel.dataset.kitIdx, 10);
                    if (kit.items[vidx]) {
                        kit.items[vidx].variantIdx = parseInt(sel.value, 10);
                        saveKit();
                        renderKitPanel();
                    }
                }
            });
        }

        // Delegation on Compare table
        if (els.compareTableWrap) {
            els.compareTableWrap.addEventListener('click', function (e) {
                var rem = e.target.closest('.js-cmp-remove');
                if (rem) {
                    toggleCompare(rem.dataset.objId);
                }
            });
        }

        // Delegation on main container
        if (els.container) {
            els.container.addEventListener('click', function (e) {
                var thBtn = e.target.closest('.obj-th-sort');
                if (thBtn) {
                    state.sort = thBtn.dataset.sortKey;
                    syncInputsFromState();
                    render();
                    return;
                }
                var propBtn = e.target.closest('.js-filter-prop');
                if (propBtn) {
                    var pid = propBtn.dataset.propId;
                    if (state.props.indexOf(pid) === -1) state.props.push(pid);
                    else state.props.splice(state.props.indexOf(pid), 1);
                    render();
                    return;
                }
                var addKitBtn = e.target.closest('.js-add-kit');
                if (addKitBtn) {
                    addToKit(addKitBtn.dataset.objId);
                    return;
                }
                var cmpBtn = e.target.closest('.js-toggle-cmp');
                if (cmpBtn) {
                    toggleCompare(cmpBtn.dataset.objId);
                    return;
                }
                var favBtn = e.target.closest('.js-toggle-fav');
                if (favBtn) {
                    toggleFav(favBtn.dataset.objId);
                    return;
                }
                var toggleBtn = e.target.closest('.js-toggle-details');
                if (toggleBtn) {
                    toggleCardExpanded(toggleBtn.closest('.obj-card'));
                    return;
                }
                var copyBtn = e.target.closest('.js-copy-link');
                if (copyBtn) {
                    var oid = copyBtn.getAttribute('data-obj-id');
                    var url = location.href.split('#')[0] + '#obj-' + oid;
                    try { history.replaceState(null, '', '#obj-' + oid); } catch (err) {}
                    if (navigator.clipboard && navigator.clipboard.writeText) {
                        navigator.clipboard.writeText(url).catch(function () {});
                    }
                    var orig = copyBtn.textContent;
                    copyBtn.textContent = '✓';
                    copyBtn.classList.add('copied');
                    setTimeout(function () {
                        copyBtn.textContent = orig;
                        copyBtn.classList.remove('copied');
                    }, 1200);
                    return;
                }
                if (!e.target.closest('a, button')) {
                    var closedHead = e.target.closest('.obj-card.closed .action-card-head');
                    if (closedHead) {
                        toggleCardExpanded(closedHead.closest('.obj-card'));
                    }
                }
            });
        }

        window.addEventListener('hashchange', handleHash);
    }

    function applyQueryParams() {
        try {
            var params = new URLSearchParams(location.search || '');
            if (params.get('q')) state.query = params.get('q');
            if (params.get('epoque') && BUDGET_MAP[params.get('epoque')]) state.epoch = params.get('epoque');
            if (params.get('cat') && CATEGORY_MAP[params.get('cat')]) state.categorie = params.get('cat');
        } catch (e) {}
    }

    async function boot() {
        els.container = document.getElementById('objetsContainer');
        if (!els.container) return;

        els.refBody = document.getElementById('objRefBody');
        els.search = document.getElementById('objSearch');
        els.searchClear = document.getElementById('objSearchClear');
        els.epochBar = document.getElementById('objEpochBar');
        els.epochModeExact = document.getElementById('objEpochModeExact');
        els.epochModeUpto = document.getElementById('objEpochModeUpto');
        els.epochBudgetInfo = document.getElementById('objEpochBudgetInfo');
        els.sort = document.getElementById('objSort');
        els.groupBy = document.getElementById('objGroupBy');
        els.filterFamily = document.getElementById('objFilterFamily');
        els.filterSkill = document.getElementById('objFilterSkill');
        els.filterHands = document.getElementById('objFilterHands');
        els.filterEnc = document.getElementById('objFilterEnc');
        els.filterPrice = document.getElementById('objFilterPrice');
        els.catPills = document.getElementById('objCatPills');
        els.propPills = document.getElementById('objPropPills');
        els.toggleConsumable = document.getElementById('objToggleConsumable');
        els.toggleNoEnc = document.getElementById('objToggleNoEnc');
        els.toggleStealth = document.getElementById('objToggleStealth');
        els.togglePiercing = document.getElementById('objTogglePiercing');
        els.toggleInBudget = document.getElementById('objToggleInBudget');
        els.toggleFavs = document.getElementById('objToggleFavs');
        els.favCount = document.getElementById('objFavCount');
        els.viewDetailed = document.getElementById('objViewDetailed');
        els.viewCompact = document.getElementById('objViewCompact');
        els.viewClosed = document.getElementById('objViewClosed');
        els.viewTable = document.getElementById('objViewTable');
        els.openCompareBtn = document.getElementById('objOpenCompareBtn');
        els.compareCount = document.getElementById('objCompareCount');
        els.openKitBtn = document.getElementById('objOpenKitBtn');
        els.kitCount = document.getElementById('objKitCount');
        els.resetBtn = document.getElementById('objResetBtn');
        els.countBar = document.getElementById('objCountBar');
        els.telShown = document.getElementById('telObjShown');

        els.kitPanel = document.getElementById('objKitPanel');
        els.kitCloseBtn = document.getElementById('kitCloseBtn');
        els.kitForce = document.getElementById('kitForce');
        els.kitEpoch = document.getElementById('kitEpoch');
        els.kitBudget = document.getElementById('kitBudget');
        els.kitTechGuerrier = document.getElementById('kitTechGuerrier');
        els.kitRecupVardien = document.getElementById('kitRecupVardien');
        els.kitLoadPresetBtn = document.getElementById('kitLoadPresetBtn');
        els.kitCopyBtn = document.getElementById('kitCopyBtn');
        els.kitExportBtn = document.getElementById('kitExportBtn');
        els.kitImportBtn = document.getElementById('kitImportBtn');
        els.kitImportFile = document.getElementById('kitImportFile');
        els.kitClearBtn = document.getElementById('kitClearBtn');
        els.kitDashboard = document.getElementById('kitDashboard');
        els.kitItemsList = document.getElementById('kitItemsList');

        els.comparePanel = document.getElementById('objComparePanel');
        els.cmpBadgeCount = document.getElementById('cmpBadgeCount');
        els.cmpClearBtn = document.getElementById('cmpClearBtn');
        els.cmpCloseBtn = document.getElementById('cmpCloseBtn');
        els.compareTableWrap = document.getElementById('objCompareTableWrap');

        els.telKitBudget = document.getElementById('telKitBudget');
        els.telKitWeight = document.getElementById('telKitWeight');
        els.telKitStatus = document.getElementById('telKitStatus');
        els.telKitEnc = document.getElementById('telKitEnc');
        els.telOpenKitBtn = document.getElementById('telOpenKitBtn');

        try {
            var res = await fetch('data/appendice_objets.json', { cache: 'no-store' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            DATA = await res.json();
        } catch (err) {
            els.container.innerHTML = errBox('Impossible de charger le compendium des objets (data/appendice_objets.json).');
            return;
        }

        DATA.categories.forEach(function (c, idx) {
            c._order = idx;
            CATEGORY_MAP[c.id] = c;
        });
        DATA.proprietes.forEach(function (p) { PROP_MAP[p.id] = p; });
        DATA.regles_ref.budgets_depart.forEach(function (b) { BUDGET_MAP[b.epoque] = b; });
        (DATA.presets_kit || []).forEach(function (p) { PRESET_MAP[p.epoque] = p; });
        DATA.objets.forEach(function (o, idx) {
            o._order = idx;
            OBJ_MAP[o.id] = o;
            buildSearchIndex(o);
        });

        loadKit();
        loadFavs();
        applyQueryParams();
        populateStaticUI();
        syncInputsFromState();
        syncKitInputs();
        bindEvents();
        render();
        handleHash();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
})();
