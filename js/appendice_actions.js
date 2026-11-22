/* ==========================================================================
   APPENDICE A : COMPENDIUM INTERACTIF DES ACTIONS (js/appendice_actions.js)
   Rend les cartes d'actions depuis data/appendice_actions.json avec
   recherche plein texte, tri, regroupement, filtres cumulables et ancres.
   ========================================================================== */
(function () {
    'use strict';

    var DATA = null;
    var CATEGORY_MAP = {};
    var TYPE_ORDER = { 'Offensif': 1, 'Défensif': 2, 'Utilitaire': 3, 'Social': 4, 'Hybride': 5 };
    var CADENCE_ORDER = {
        principale: 1,
        micro: 2,
        manoeuvre: 3,
        reaction_def: 4,
        reaction_libre: 5,
        reaction_autre: 6,
        gratuite: 7,
        modificateur: 8,
        hors_combat: 9
    };
    var COST_LABELS = {
        '0': 'Gratuit (0 PA)',
        '1': '1 PA',
        '2': '2 PA',
        '3-4': '3–4+ PA',
        'variable': 'Variable / Spécial',
        'hors_combat': 'Hors-Combat (Durée / Scène)'
    };
    var COST_ORDER = { '0': 1, '1': 2, '2': 3, '3-4': 4, 'variable': 5, 'hors_combat': 6 };

    var state = {
        query: '',
        categorie: 'all',
        type: 'all',
        cadence: 'all',
        cost: 'all',
        context: 'all',
        book: 'all',
        silentOnly: false,
        freeReactOnly: false,
        rollMode: 'all', // all | roll | noroll
        sort: 'categorie',
        groupBy: 'categorie',
        density: 'detailed' // detailed | compact | closed
    };

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
            .toLowerCase()
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '');
    }

    function errBox(msg) {
        return '<div class="callout vital"><div class="callout-body">' + esc(msg) + '</div></div>';
    }

    function typeTone(t) {
        if (t === 'Offensif') return 'ruby';
        if (t === 'Défensif') return 'cyan';
        if (t === 'Utilitaire') return 'emerald';
        if (t === 'Social') return 'amber';
        return 'violet';
    }

    function costClass(g) {
        if (g === '0') return 'cost-zero';
        if (g === '1') return 'cost-one';
        if (g === '2') return 'cost-two';
        if (g === '3-4') return 'cost-high';
        if (g === 'variable') return 'cost-var';
        return 'cost-hc';
    }

    function romanBook(n) {
        var map = { 1: 'I', 2: 'II', 3: 'III', 4: 'IV', 5: 'V', 6: 'VI', 7: 'VII', 8: 'VIII', 9: 'IX', 10: 'X', 11: 'XI', 12: 'XII' };
        return 'Livre ' + (map[n] || n);
    }

    function buildSearchIndex(a) {
        var parts = [
            a.nom,
            a.cadence,
            a.cout_pa,
            a.contexte,
            a.timing,
            a.portee,
            a.jet,
            a.limite,
            a.ralenti_effet,
            a.resume,
            a.conditions,
            a.modificateurs,
            a.source && a.source.label
        ];
        if (Array.isArray(a.types)) parts = parts.concat(a.types);
        if (Array.isArray(a.procedure)) parts = parts.concat(a.procedure);
        if (Array.isArray(a.competences)) parts = parts.concat(a.competences);
        if (Array.isArray(a.attributs)) parts = parts.concat(a.attributs);
        if (Array.isArray(a.tags)) parts = parts.concat(a.tags);
        if (CATEGORY_MAP[a.categorie]) parts.push(CATEGORY_MAP[a.categorie].nom);
        a._search = norm(parts.filter(Boolean).join(' '));
    }

    function matches(a) {
        if (state.categorie !== 'all' && a.categorie !== state.categorie) return false;
        if (state.type !== 'all' && (!a.types || a.types.indexOf(state.type) === -1)) return false;
        if (state.cadence !== 'all' && a.cadence_cle !== state.cadence) return false;
        if (state.cost !== 'all' && a.cout_pa_groupe !== state.cost) return false;
        if (state.context !== 'all') {
            if (state.context === 'Combat' && a.contexte === 'Hors-Combat') return false;
            if (state.context === 'Hors-Combat' && a.contexte === 'Combat') return false;
            if (state.context === 'Combat & Hors-Combat' && a.contexte !== 'Combat & Hors-Combat') return false;
        }
        if (state.book !== 'all' && String(a.source && a.source.livre) !== String(state.book)) return false;
        if (state.silentOnly && !a.silencieux) return false;
        if (state.freeReactOnly && !a.consomme_reaction_libre) return false;
        if (state.rollMode === 'roll' && !a.requiert_jet) return false;
        if (state.rollMode === 'noroll' && a.requiert_jet) return false;
        if (state.query) {
            var tokens = norm(state.query).split(/\s+/).filter(Boolean);
            for (var i = 0; i < tokens.length; i++) {
                if (a._search.indexOf(tokens[i]) === -1) return false;
            }
        }
        return true;
    }

    function sortActions(list) {
        var copy = list.slice();
        copy.sort(function (a, b) {
            if (state.sort === 'nom-asc') {
                return a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' });
            }
            if (state.sort === 'nom-desc') {
                return b.nom.localeCompare(a.nom, 'fr', { sensitivity: 'base' });
            }
            if (state.sort === 'cout-asc') {
                if (a.cout_pa_tri !== b.cout_pa_tri) return a.cout_pa_tri - b.cout_pa_tri;
                return a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' });
            }
            if (state.sort === 'cout-desc') {
                if (a.cout_pa_tri !== b.cout_pa_tri) return b.cout_pa_tri - a.cout_pa_tri;
                return a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' });
            }
            if (state.sort === 'type') {
                var ta = TYPE_ORDER[a.types && a.types[0]] || 99;
                var tb = TYPE_ORDER[b.types && b.types[0]] || 99;
                if (ta !== tb) return ta - tb;
                return a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' });
            }
            if (state.sort === 'cadence') {
                var ca = CADENCE_ORDER[a.cadence_cle] || 99;
                var cb = CADENCE_ORDER[b.cadence_cle] || 99;
                if (ca !== cb) return ca - cb;
                return a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' });
            }
            if (state.sort === 'livre') {
                var la = (a.source && a.source.livre) || 99;
                var lb = (b.source && b.source.livre) || 99;
                if (la !== lb) return la - lb;
                return a.nom.localeCompare(b.nom, 'fr', { sensitivity: 'base' });
            }
            // 'categorie' : ordre canonique des catégories puis ordre d'apparition
            var cia = CATEGORY_MAP[a.categorie] ? CATEGORY_MAP[a.categorie]._order : 99;
            var cib = CATEGORY_MAP[b.categorie] ? CATEGORY_MAP[b.categorie]._order : 99;
            if (cia !== cib) return cia - cib;
            return a._order - b._order;
        });
        return copy;
    }

    function groupActions(list) {
        if (state.groupBy === 'none') {
            return [{
                key: 'all',
                idAttr: '',
                code: '',
                title: 'Toutes les Actions',
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
                    sortRank: (typeof meta.sortRank === 'number') ? meta.sortRank : 99,
                    items: []
                };
                orderedKeys.push(key);
            }
            buckets[key].items.push(item);
        }

        list.forEach(function (a) {
            if (state.groupBy === 'categorie') {
                var c = CATEGORY_MAP[a.categorie];
                pushItem(a.categorie, {
                    idAttr: 'cat-' + a.categorie,
                    code: c ? c.code : '',
                    title: c ? c.nom : a.categorie,
                    ton: c ? c.ton : 'cyan',
                    desc: c ? c.desc : '',
                    sortRank: c ? c._order : 99
                }, a);
            } else if (state.groupBy === 'cadence') {
                pushItem(a.cadence_cle, {
                    code: 'CADENCE',
                    title: a.cadence,
                    ton: 'amber',
                    sortRank: CADENCE_ORDER[a.cadence_cle] || 99
                }, a);
            } else if (state.groupBy === 'cout') {
                pushItem(a.cout_pa_groupe, {
                    code: 'COÛT PA',
                    title: COST_LABELS[a.cout_pa_groupe] || a.cout_pa_groupe,
                    ton: 'cyan',
                    sortRank: COST_ORDER[a.cout_pa_groupe] || 99
                }, a);
            } else if (state.groupBy === 'type') {
                var primaryType = (a.types && a.types[0]) || 'Autre';
                pushItem(primaryType, {
                    code: 'TYPE',
                    title: primaryType,
                    ton: typeTone(primaryType),
                    sortRank: TYPE_ORDER[primaryType] || 99
                }, a);
            } else if (state.groupBy === 'livre') {
                var ln = (a.source && a.source.livre) || 99;
                pushItem('livre-' + ln, {
                    code: 'SOURCE',
                    title: romanBook(ln),
                    ton: 'violet',
                    sortRank: ln
                }, a);
            }
        });

        orderedKeys.sort(function (ka, kb) {
            return buckets[ka].sortRank - buckets[kb].sortRank;
        });

        return orderedKeys.map(function (k) { return buckets[k]; });
    }

    function renderCard(a) {
        var cat = CATEGORY_MAP[a.categorie] || { code: '', nom: a.categorie, ton: 'cyan' };
        var isCompact = state.density === 'compact';
        var isClosed = state.density === 'closed';

        var typePills = (a.types || []).map(function (t) {
            return '<span class="tag-pill ' + typeTone(t) + '">' + esc(t) + '</span>';
        }).join('');

        var propPills = '';
        propPills += '<span class="tag-pill">' + esc(a.contexte) + '</span>';
        if (a.consomme_reaction_libre) propPills += '<span class="tag-pill amber">Réaction libre</span>';
        if (a.silencieux) propPills += '<span class="tag-pill emerald">Silencieux</span>';
        if (!a.requiert_jet) propPills += '<span class="tag-pill">Sans jet</span>';

        var compChips = (a.competences || []).map(function (c) {
            return '<span class="action-skill-chip">' + esc(c) + '</span>';
        }).join('');
        var attrChips = (a.attributs || []).map(function (at) {
            return '<span class="action-attr-chip">' + esc(at) + '</span>';
        }).join('');

        var procHtml = (a.procedure || []).map(function (p) {
            return '<li>' + esc(p) + '</li>';
        }).join('');

        var refsHtml = '';
        if (a.source && a.source.href) {
            refsHtml += '<a class="action-src-link primary" href="' + esc(a.source.href) + '">' + esc(a.source.label) + ' ↗</a>';
        }
        (a.refs || []).forEach(function (r) {
            refsHtml += '<a class="action-src-link" href="' + esc(r.href) + '">' + esc(r.label) + '</a>';
        });

        return (
            '<article class="action-card' + (isCompact ? ' compact' : '') + (isClosed ? ' closed' : '') + '" id="act-' + esc(a.id) + '" data-ton="' + esc(cat.ton) + '">' +
                '<div class="action-card-head">' +
                    '<div class="action-title-wrap">' +
                        '<div class="action-cat-kicker">' + esc(cat.code) + ' · ' + esc(cat.nom) + '</div>' +
                        '<h4>' + esc(a.nom) + '</h4>' +
                    '</div>' +
                    '<div class="action-badges-right">' +
                        '<span class="action-cost-badge ' + costClass(a.cout_pa_groupe) + '">' + esc(a.cout_pa) + '</span>' +
                        '<span class="action-cadence-badge">' + esc(a.cadence) + '</span>' +
                    '</div>' +
                '</div>' +
                '<div class="action-pills-row">' + typePills + propPills + '</div>' +
                '<p class="action-summary">' + esc(a.resume) + '</p>' +
                '<div class="action-meta-grid">' +
                    '<div class="action-meta-cell"><span class="action-meta-label">Timing</span><span class="action-meta-val">' + esc(a.timing) + '</span></div>' +
                    '<div class="action-meta-cell"><span class="action-meta-label">Portée</span><span class="action-meta-val">' + esc(a.portee) + '</span></div>' +
                    '<div class="action-meta-cell"><span class="action-meta-label">Jet / Épreuve</span><span class="action-meta-val">' + esc(a.jet) + '</span></div>' +
                    '<div class="action-meta-cell"><span class="action-meta-label">Limite</span><span class="action-meta-val">' + esc(a.limite) + '</span></div>' +
                '</div>' +
                '<div class="action-details">' +
                    '<div class="action-detail-block">' +
                        '<div class="action-detail-title">Résolution &amp; Procédure</div>' +
                        '<ul class="action-proc-list">' + procHtml + '</ul>' +
                    '</div>' +
                    '<div class="action-detail-cols">' +
                        '<div class="action-detail-block">' +
                            '<div class="action-detail-title">Conditions &amp; Limites</div>' +
                            '<p>' + esc(a.conditions) + '</p>' +
                        '</div>' +
                        '<div class="action-detail-block">' +
                            '<div class="action-detail-title">Modificateurs, Spés &amp; Ralenti</div>' +
                            '<p>' + esc(a.modificateurs) + '</p>' +
                            '<div class="action-ralenti-note"><strong>Sous Ralenti :</strong> ' + esc(a.ralenti_effet) + '</div>' +
                        '</div>' +
                    '</div>' +
                    ((compChips || attrChips)
                        ? '<div class="action-chips-row">' + compChips + attrChips + '</div>'
                        : '') +
                '</div>' +
                '<div class="action-card-footer">' +
                    '<div class="action-refs-wrap">' + refsHtml + '</div>' +
                    '<div class="action-card-actions">' +
                        ((isCompact || isClosed) ? '<button type="button" class="action-mini-btn js-toggle-details" aria-expanded="false">' + (isClosed ? 'Ouvrir ▾' : 'Détails ▾') + '</button>' : '') +
                        '<button type="button" class="action-mini-btn js-copy-link" data-action-id="' + esc(a.id) + '" title="Copier le lien direct vers cette action">Lien #</button>' +
                    '</div>' +
                '</div>' +
            '</article>'
        );
    }

    function render() {
        if (!DATA) return;
        var filtered = DATA.actions.filter(matches);
        var sorted = sortActions(filtered);
        var groups = groupActions(sorted);

        // Update category pills counts & active states
        if (els.catPills) {
            els.catPills.querySelectorAll('button[data-cat]').forEach(function (btn) {
                btn.classList.toggle('active', btn.dataset.cat === state.categorie);
            });
        }

        // Update toggle buttons
        if (els.toggleSilent) {
            els.toggleSilent.classList.toggle('active', state.silentOnly);
            els.toggleSilent.setAttribute('aria-pressed', state.silentOnly ? 'true' : 'false');
        }
        if (els.toggleFreeReact) {
            els.toggleFreeReact.classList.toggle('active', state.freeReactOnly);
            els.toggleFreeReact.setAttribute('aria-pressed', state.freeReactOnly ? 'true' : 'false');
        }
        if (els.rollButtons) {
            els.rollButtons.forEach(function (btn) {
                var active = btn.dataset.roll === state.rollMode;
                btn.classList.toggle('active', active);
                btn.setAttribute('aria-pressed', active ? 'true' : 'false');
            });
        }
        if (els.densityDetailed && els.densityCompact) {
            els.densityDetailed.classList.toggle('active', state.density === 'detailed');
            els.densityCompact.classList.toggle('active', state.density === 'compact');
            if (els.densityClosed) els.densityClosed.classList.toggle('active', state.density === 'closed');
        }
        if (els.searchClear) {
            els.searchClear.hidden = !state.query;
        }

        // Stats bar
        if (els.countBar) {
            var groupLabel = els.groupBy && els.groupBy.options[els.groupBy.selectedIndex]
                ? els.groupBy.options[els.groupBy.selectedIndex].text
                : state.groupBy;
            var sortLabel = els.sort && els.sort.options[els.sort.selectedIndex]
                ? els.sort.options[els.sort.selectedIndex].text
                : state.sort;
            els.countBar.innerHTML =
                '<span><strong>' + filtered.length + '</strong> / ' + DATA.actions.length + ' action' + (DATA.actions.length > 1 ? 's' : '') + ' affichée' + (filtered.length > 1 ? 's' : '') + '</span>' +
                '<span class="actions-stats-meta">Tri : ' + esc(sortLabel) + ' &bull; Groupe : ' + esc(groupLabel) + '</span>';
        }
        if (els.telShown) {
            els.telShown.textContent = filtered.length + ' / ' + DATA.actions.length;
        }

        if (!filtered.length) {
            els.container.innerHTML =
                '<div class="actions-empty-state">' +
                    '<h4>Aucune action ne correspond aux critères actifs</h4>' +
                    '<p>Essayez d’élargir votre recherche ou de réinitialiser les filtres combinés.</p>' +
                    '<button type="button" class="action-reset-btn" id="emptyResetBtn">Tout réinitialiser</button>' +
                '</div>';
            var emptyBtn = document.getElementById('emptyResetBtn');
            if (emptyBtn) emptyBtn.addEventListener('click', resetFilters);
            return;
        }

        var gridCls = 'actions-grid' + (state.density === 'compact' ? ' compact-grid' : (state.density === 'closed' ? ' closed-grid' : ''));
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

        if (window.bindCodexTips) window.bindCodexTips(els.container);
    }

    function syncInputsFromState() {
        if (els.search) els.search.value = state.query;
        if (els.sort) els.sort.value = state.sort;
        if (els.groupBy) els.groupBy.value = state.groupBy;
        if (els.filterType) els.filterType.value = state.type;
        if (els.filterCadence) els.filterCadence.value = state.cadence;
        if (els.filterCost) els.filterCost.value = state.cost;
        if (els.filterContext) els.filterContext.value = state.context;
        if (els.filterBook) els.filterBook.value = state.book;
    }

    function resetFilters() {
        state.query = '';
        state.categorie = 'all';
        state.type = 'all';
        state.cadence = 'all';
        state.cost = 'all';
        state.context = 'all';
        state.book = 'all';
        state.silentOnly = false;
        state.freeReactOnly = false;
        state.rollMode = 'all';
        syncInputsFromState();
        render();
    }

    function toggleCardExpanded(card, forceOpen) {
        if (!card) return;
        var expanded;
        if (forceOpen === true) {
            card.classList.add('is-expanded');
            expanded = true;
        } else {
            expanded = card.classList.toggle('is-expanded');
        }
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
                var needsRerender = (state.groupBy !== 'categorie' || (state.categorie !== 'all' && state.categorie !== catId));
                if (state.groupBy !== 'categorie') state.groupBy = 'categorie';
                if (state.categorie !== 'all' && state.categorie !== catId) state.categorie = 'all';
                if (needsRerender) {
                    syncInputsFromState();
                    render();
                }
            }
            var targetCat = document.getElementById(h);
            if (targetCat && targetCat.scrollIntoView) targetCat.scrollIntoView();
            return;
        }

        if (h.indexOf('act-') === 0) {
            var targetAct = document.getElementById(h);
            if (!targetAct) {
                resetFilters();
                targetAct = document.getElementById(h);
            }
            if (targetAct) {
                if (state.density === 'compact' || state.density === 'closed') {
                    toggleCardExpanded(targetAct, true);
                }
                if (targetAct.scrollIntoView) targetAct.scrollIntoView();
                targetAct.classList.remove('highlight-pulse');
                void targetAct.offsetWidth;
                targetAct.classList.add('highlight-pulse');
            }
        }
    }

    function populateCatPills() {
        if (!els.catPills || !DATA) return;
        var counts = {};
        DATA.actions.forEach(function (a) {
            counts[a.categorie] = (counts[a.categorie] || 0) + 1;
        });
        var html = '<button type="button" class="actions-cat-pill active" data-cat="all">Toutes <span>' + DATA.actions.length + '</span></button>';
        DATA.categories.forEach(function (c) {
            html += '<button type="button" class="actions-cat-pill" data-cat="' + esc(c.id) + '" data-ton="' + esc(c.ton) + '">' +
                esc(c.nav || c.nom) + ' <span>' + (counts[c.id] || 0) + '</span></button>';
        });
        els.catPills.innerHTML = html;
    }

    function populateTelemetry() {
        if (!DATA) return;
        var total = DATA.actions.length;
        var principales = 0, micro = 0, reactions = 0, gratuites = 0, horsCombat = 0;
        var books = {};
        DATA.actions.forEach(function (a) {
            if (a.cadence_cle === 'principale') principales++;
            else if (a.cadence_cle === 'micro') micro++;
            else if (a.cadence_cle.indexOf('reaction') === 0) reactions++;
            else if (a.cadence_cle === 'gratuite') gratuites++;
            else if (a.cadence_cle === 'hors_combat') horsCombat++;
            if (a.source && a.source.livre) books[a.source.livre] = true;
        });
        var setTxt = function (id, val) {
            var el = document.getElementById(id);
            if (el) el.textContent = String(val);
        };
        setTxt('telTotal', total + ' fiches');
        setTxt('telPrincipales', principales);
        setTxt('telMicro', micro);
        setTxt('telReactions', reactions);
        setTxt('telGratuites', gratuites);
        setTxt('telHorsCombat', horsCombat);
        setTxt('telLivres', Object.keys(books).length + ' Livres');
        setTxt('telShown', total + ' / ' + total);
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
                if (els.search) {
                    els.search.value = '';
                    els.search.focus();
                }
                render();
            });
        }
        if (els.sort) {
            els.sort.addEventListener('change', function () {
                state.sort = els.sort.value;
                render();
            });
        }
        if (els.groupBy) {
            els.groupBy.addEventListener('change', function () {
                state.groupBy = els.groupBy.value;
                render();
            });
        }
        if (els.filterType) {
            els.filterType.addEventListener('change', function () {
                state.type = els.filterType.value;
                render();
            });
        }
        if (els.filterCadence) {
            els.filterCadence.addEventListener('change', function () {
                state.cadence = els.filterCadence.value;
                render();
            });
        }
        if (els.filterCost) {
            els.filterCost.addEventListener('change', function () {
                state.cost = els.filterCost.value;
                render();
            });
        }
        if (els.filterContext) {
            els.filterContext.addEventListener('change', function () {
                state.context = els.filterContext.value;
                render();
            });
        }
        if (els.filterBook) {
            els.filterBook.addEventListener('change', function () {
                state.book = els.filterBook.value;
                render();
            });
        }
        if (els.catPills) {
            els.catPills.addEventListener('click', function (e) {
                var btn = e.target.closest('button[data-cat]');
                if (!btn) return;
                state.categorie = btn.dataset.cat;
                render();
            });
        }
        if (els.toggleSilent) {
            els.toggleSilent.addEventListener('click', function () {
                state.silentOnly = !state.silentOnly;
                render();
            });
        }
        if (els.toggleFreeReact) {
            els.toggleFreeReact.addEventListener('click', function () {
                state.freeReactOnly = !state.freeReactOnly;
                render();
            });
        }
        if (els.rollButtons) {
            els.rollButtons.forEach(function (btn) {
                btn.addEventListener('click', function () {
                    state.rollMode = btn.dataset.roll;
                    render();
                });
            });
        }
        if (els.densityDetailed) {
            els.densityDetailed.addEventListener('click', function () {
                state.density = 'detailed';
                render();
            });
        }
        if (els.densityCompact) {
            els.densityCompact.addEventListener('click', function () {
                state.density = 'compact';
                render();
            });
        }
        if (els.densityClosed) {
            els.densityClosed.addEventListener('click', function () {
                state.density = 'closed';
                render();
            });
        }
        if (els.resetBtn) {
            els.resetBtn.addEventListener('click', resetFilters);
        }
        if (els.container) {
            els.container.addEventListener('click', function (e) {
                var toggleBtn = e.target.closest('.js-toggle-details');
                if (toggleBtn) {
                    toggleCardExpanded(toggleBtn.closest('.action-card'));
                    return;
                }
                var copyBtn = e.target.closest('.js-copy-link');
                if (copyBtn) {
                    var aid = copyBtn.getAttribute('data-action-id');
                    var url = location.href.split('#')[0] + '#act-' + aid;
                    try {
                        history.replaceState(null, '', '#act-' + aid);
                    } catch (err) { /* ignore file:// */ }
                    if (navigator.clipboard && navigator.clipboard.writeText) {
                        navigator.clipboard.writeText(url).catch(function () {});
                    }
                    var orig = copyBtn.textContent;
                    copyBtn.textContent = 'Copié ✓';
                    copyBtn.classList.add('copied');
                    setTimeout(function () {
                        copyBtn.textContent = orig;
                        copyBtn.classList.remove('copied');
                    }, 1500);
                    return;
                }
                if (!e.target.closest('a, button')) {
                    var closedHead = e.target.closest('.action-card.closed .action-card-head');
                    if (closedHead) {
                        toggleCardExpanded(closedHead.closest('.action-card'));
                    }
                }
            });
        }
        window.addEventListener('hashchange', handleHash);
    }

    async function boot() {
        els.container = document.getElementById('actionsContainer');
        if (!els.container) return;

        els.search = document.getElementById('actionSearch');
        els.searchClear = document.getElementById('actionSearchClear');
        els.sort = document.getElementById('actionSort');
        els.groupBy = document.getElementById('actionGroupBy');
        els.filterType = document.getElementById('actionFilterType');
        els.filterCadence = document.getElementById('actionFilterCadence');
        els.filterCost = document.getElementById('actionFilterCost');
        els.filterContext = document.getElementById('actionFilterContext');
        els.filterBook = document.getElementById('actionFilterBook');
        els.catPills = document.getElementById('actionCatPills');
        els.toggleSilent = document.getElementById('actionToggleSilent');
        els.toggleFreeReact = document.getElementById('actionToggleFreeReact');
        els.rollButtons = document.querySelectorAll('[data-roll]');
        els.densityDetailed = document.getElementById('actionDensityDetailed');
        els.densityCompact = document.getElementById('actionDensityCompact');
        els.densityClosed = document.getElementById('actionDensityClosed');
        els.resetBtn = document.getElementById('actionResetBtn');
        els.countBar = document.getElementById('actionCountBar');
        els.telShown = document.getElementById('telShown');

        try {
            var res = await fetch('data/appendice_actions.json', { cache: 'no-store' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            DATA = await res.json();
        } catch (err) {
            els.container.innerHTML = errBox('Impossible de charger le compendium (data/appendice_actions.json).');
            return;
        }

        DATA.categories.forEach(function (c, idx) {
            c._order = idx;
            CATEGORY_MAP[c.id] = c;
        });
        DATA.actions.forEach(function (a, idx) {
            a._order = idx;
            buildSearchIndex(a);
        });

        populateCatPills();
        populateTelemetry();
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
