/* ==========================================================================
   APPENDICE B : GLOSSAIRE DES MÉCANIQUES (js/appendice_glossaire.js)
   Rend les entrées depuis data/appendice_glossaire.json : uniquement le terme
   et ses liens directs vers les Livres (aucune définition, aucun résumé).
   ========================================================================== */
(function () {
    'use strict';

    var DATA = null;
    var DOMAIN_MAP = {};
    var ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('');
    var ROMAN = { 1: 'I', 2: 'II', 3: 'III', 4: 'IV', 5: 'V', 6: 'VI', 7: 'VII', 8: 'VIII', 9: 'IX', 10: 'X', 11: 'XI', 12: 'XII' };

    var state = {
        query: '',
        letter: 'all',
        domaine: 'all',
        book: 'all',
        sort: 'nom-asc',
        groupBy: 'alpha',
        layout: 'cols-2'
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
            .replace(/[Œœ]/g, 'oe')
            .toLowerCase()
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '');
    }

    function firstLetter(terme) {
        var n = norm(terme).trim();
        if (!n) return '#';
        var ch = n.charAt(0).toUpperCase();
        return (ch >= 'A' && ch <= 'Z') ? ch : '#';
    }

    function romanBook(n) {
        return 'Livre ' + (ROMAN[n] || n);
    }

    function errBox(msg) {
        return '<div class="callout vital"><div class="callout-body">' + esc(msg) + '</div></div>';
    }

    function buildSearchIndex(t) {
        var parts = [t.terme];
        if (Array.isArray(t.alias)) parts = parts.concat(t.alias);
        if (DOMAIN_MAP[t.domaine]) parts.push(DOMAIN_MAP[t.domaine].nom);
        (t.liens || []).forEach(function (lk) {
            if (lk && lk.label) parts.push(lk.label);
        });
        t._search = norm(parts.filter(Boolean).join(' '));
        t._letter = firstLetter(t.terme);
    }

    function hasBook(t, bookStr) {
        var links = t.liens || [];
        for (var i = 0; i < links.length; i++) {
            if (String(links[i].livre) === bookStr) return true;
        }
        return false;
    }

    function matches(t) {
        if (state.domaine !== 'all' && t.domaine !== state.domaine) return false;
        if (state.letter !== 'all' && t._letter !== state.letter) return false;
        if (state.book !== 'all' && !hasBook(t, String(state.book))) return false;
        if (state.query) {
            var tokens = norm(state.query).split(/\s+/).filter(Boolean);
            for (var i = 0; i < tokens.length; i++) {
                if (t._search.indexOf(tokens[i]) === -1) return false;
            }
        }
        return true;
    }

    function primaryBook(t) {
        return (t.liens && t.liens[0] && t.liens[0].livre) || 99;
    }

    function sortTerms(list) {
        var copy = list.slice();
        copy.sort(function (a, b) {
            if (state.sort === 'nom-desc') {
                return b.terme.localeCompare(a.terme, 'fr', { sensitivity: 'base' });
            }
            if (state.sort === 'livre') {
                var la = primaryBook(a);
                var lb = primaryBook(b);
                if (la !== lb) return la - lb;
            }
            return a.terme.localeCompare(b.terme, 'fr', { sensitivity: 'base' });
        });
        return copy;
    }

    function groupTerms(list) {
        if (state.groupBy === 'none') {
            return [{
                key: 'all',
                idAttr: '',
                code: '',
                title: '',
                ton: 'cyan',
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
                    title: meta.title || '',
                    ton: meta.ton || 'cyan',
                    sortRank: meta.sortRank,
                    items: []
                };
                orderedKeys.push(key);
            }
            buckets[key].items.push(item);
        }

        list.forEach(function (t) {
            if (state.groupBy === 'alpha') {
                var L = t._letter;
                var idx = ALPHABET.indexOf(L);
                pushItem(L, {
                    idAttr: 'lettre-' + L,
                    code: L,
                    title: '',
                    ton: 'cyan',
                    sortRank: idx === -1 ? 99 : idx
                }, t);
            } else if (state.groupBy === 'domaine') {
                var d = DOMAIN_MAP[t.domaine];
                pushItem(t.domaine, {
                    idAttr: 'dom-' + t.domaine,
                    code: d ? d.code : '',
                    title: d ? d.nom : t.domaine,
                    ton: d ? d.ton : 'cyan',
                    sortRank: d ? d._order : 99
                }, t);
            } else if (state.groupBy === 'livre') {
                var ln = (state.book !== 'all') ? Number(state.book) : primaryBook(t);
                pushItem('livre-' + ln, {
                    code: 'SOURCE',
                    title: romanBook(ln),
                    ton: 'violet',
                    sortRank: ln
                }, t);
            }
        });

        orderedKeys.sort(function (ka, kb) {
            return buckets[ka].sortRank - buckets[kb].sortRank;
        });

        return orderedKeys.map(function (k) { return buckets[k]; });
    }

    function renderEntry(t) {
        var dom = DOMAIN_MAP[t.domaine] || { ton: 'cyan' };
        var linksHtml = (t.liens || []).map(function (lk, idx) {
            var cls = idx === 0 ? 'gloss-link primary' : 'gloss-link';
            var suffix = idx === 0 ? ' ↗' : '';
            return '<a class="' + cls + '" href="' + esc(lk.href) + '">' + esc(lk.label) + suffix + '</a>';
        }).join('');

        return (
            '<div class="gloss-entry" id="term-' + esc(t.id) + '" data-ton="' + esc(dom.ton) + '">' +
                '<span class="gloss-term">' + esc(t.terme) + '</span>' +
                '<div class="gloss-entry-right">' +
                    '<div class="gloss-links">' + linksHtml + '</div>' +
                    '<button type="button" class="gloss-anchor-btn js-copy-term" data-term-id="' + esc(t.id) + '" title="Copier l\'ancre">#</button>' +
                '</div>' +
            '</div>'
        );
    }

    function render() {
        if (!DATA) return;
        var filtered = DATA.termes.filter(matches);
        var sorted = sortTerms(filtered);
        var groups = groupTerms(sorted);

        if (els.alphaBar) {
            els.alphaBar.querySelectorAll('button[data-letter]').forEach(function (btn) {
                btn.classList.toggle('active', btn.dataset.letter === state.letter);
            });
        }
        if (els.domPills) {
            els.domPills.querySelectorAll('button[data-dom]').forEach(function (btn) {
                btn.classList.toggle('active', btn.dataset.dom === state.domaine);
            });
        }
        if (els.cols2 && els.cols1) {
            els.cols2.classList.toggle('active', state.layout === 'cols-2');
            els.cols1.classList.toggle('active', state.layout === 'cols-1');
            els.cols2.setAttribute('aria-pressed', state.layout === 'cols-2' ? 'true' : 'false');
            els.cols1.setAttribute('aria-pressed', state.layout === 'cols-1' ? 'true' : 'false');
        }
        if (els.searchClear) {
            els.searchClear.hidden = !state.query;
        }

        if (els.countBar) {
            var groupLabel = els.groupBy && els.groupBy.options[els.groupBy.selectedIndex]
                ? els.groupBy.options[els.groupBy.selectedIndex].text
                : state.groupBy;
            var sortLabel = els.sort && els.sort.options[els.sort.selectedIndex]
                ? els.sort.options[els.sort.selectedIndex].text
                : state.sort;
            els.countBar.innerHTML =
                '<span><strong>' + filtered.length + '</strong> / ' + DATA.termes.length + ' terme' + (DATA.termes.length > 1 ? 's' : '') + ' affiché' + (filtered.length > 1 ? 's' : '') + '</span>' +
                '<span class="actions-stats-meta">Tri : ' + esc(sortLabel) + ' &bull; Groupe : ' + esc(groupLabel) + '</span>';
        }
        if (els.telShown) {
            els.telShown.textContent = filtered.length + ' / ' + DATA.termes.length;
        }

        if (!filtered.length) {
            els.container.innerHTML =
                '<div class="actions-empty-state">' +
                    '<h4>Aucun terme ne correspond aux filtres actifs</h4>' +
                    '<button type="button" class="action-reset-btn" id="glossEmptyResetBtn">Réinitialiser</button>' +
                '</div>';
            var emptyBtn = document.getElementById('glossEmptyResetBtn');
            if (emptyBtn) emptyBtn.addEventListener('click', resetFilters);
            return;
        }

        var listCls = 'gloss-list' + (state.layout === 'cols-1' ? ' single-col' : '');
        els.container.innerHTML = groups.map(function (g) {
            var head = '';
            if (state.groupBy !== 'none') {
                head =
                    '<div class="gloss-group-header" data-ton="' + esc(g.ton) + '">' +
                        '<h2' + (g.idAttr ? ' id="' + esc(g.idAttr) + '"' : '') + ' class="gloss-group-heading">' +
                            (g.code ? '<span class="section-symbol">' + esc(g.code) + '</span>' : '') +
                            (g.title ? ' <span>' + esc(g.title) + '</span>' : '') +
                            '<span class="gloss-group-count">' + g.items.length + '</span>' +
                        '</h2>' +
                    '</div>';
            }
            return '<section class="gloss-group">' + head + '<div class="' + listCls + '">' + g.items.map(renderEntry).join('') + '</div></section>';
        }).join('');
    }

    function syncInputsFromState() {
        if (els.search) els.search.value = state.query;
        if (els.sort) els.sort.value = state.sort;
        if (els.groupBy) els.groupBy.value = state.groupBy;
        if (els.filterBook) els.filterBook.value = state.book;
    }

    function resetFilters() {
        state.query = '';
        state.letter = 'all';
        state.domaine = 'all';
        state.book = 'all';
        syncInputsFromState();
        render();
    }

    function handleHash() {
        var h = (location.hash || '').slice(1);
        if (!h || !DATA) return;

        if (h.indexOf('dom-') === 0) {
            var domId = h.slice(4);
            if (DOMAIN_MAP[domId]) {
                var needsDom = (state.groupBy !== 'domaine' || state.letter !== 'all' || (state.domaine !== 'all' && state.domaine !== domId));
                if (state.groupBy !== 'domaine') state.groupBy = 'domaine';
                if (state.letter !== 'all') state.letter = 'all';
                if (state.domaine !== 'all' && state.domaine !== domId) state.domaine = 'all';
                if (needsDom) {
                    syncInputsFromState();
                    render();
                }
            }
            var targetDom = document.getElementById(h);
            if (targetDom && targetDom.scrollIntoView) targetDom.scrollIntoView();
            return;
        }

        if (h.indexOf('lettre-') === 0) {
            var letter = h.slice(7);
            var needsAlpha = (state.groupBy !== 'alpha' || (state.letter !== 'all' && state.letter !== letter));
            if (state.groupBy !== 'alpha') state.groupBy = 'alpha';
            if (state.letter !== 'all' && state.letter !== letter) state.letter = 'all';
            if (needsAlpha) {
                syncInputsFromState();
                render();
            }
            var targetLetter = document.getElementById(h);
            if (targetLetter && targetLetter.scrollIntoView) targetLetter.scrollIntoView();
            return;
        }

        if (h.indexOf('term-') === 0) {
            var targetTerm = document.getElementById(h);
            if (!targetTerm) {
                resetFilters();
                targetTerm = document.getElementById(h);
            }
            if (targetTerm) {
                if (targetTerm.scrollIntoView) targetTerm.scrollIntoView();
                targetTerm.classList.remove('highlight-pulse');
                void targetTerm.offsetWidth;
                targetTerm.classList.add('highlight-pulse');
            }
        }
    }

    function populateAlphaBar() {
        if (!els.alphaBar || !DATA) return;
        var counts = {};
        DATA.termes.forEach(function (t) {
            counts[t._letter] = (counts[t._letter] || 0) + 1;
        });
        var html = '<button type="button" class="gloss-alpha-btn active" data-letter="all">Toutes</button>';
        ALPHABET.forEach(function (L) {
            var dis = !counts[L] ? ' disabled' : '';
            html += '<button type="button" class="gloss-alpha-btn" data-letter="' + L + '"' + dis + '>' + L + '</button>';
        });
        els.alphaBar.innerHTML = html;
    }

    function populateDomPills() {
        if (!els.domPills || !DATA) return;
        var counts = {};
        DATA.termes.forEach(function (t) {
            counts[t.domaine] = (counts[t.domaine] || 0) + 1;
        });
        var html = '<button type="button" class="actions-cat-pill active" data-dom="all">Tous <span>' + DATA.termes.length + '</span></button>';
        DATA.domaines.forEach(function (d) {
            html += '<button type="button" class="actions-cat-pill" data-dom="' + esc(d.id) + '" data-ton="' + esc(d.ton) + '">' +
                esc(d.nav || d.nom) + ' <span>' + (counts[d.id] || 0) + '</span></button>';
        });
        els.domPills.innerHTML = html;
    }

    function populateTelemetry() {
        if (!DATA) return;
        var total = DATA.termes.length;
        var totalLinks = 0;
        var multi = 0;
        var books = {};
        DATA.termes.forEach(function (t) {
            var liens = t.liens || [];
            totalLinks += liens.length;
            if (liens.length > 1) multi++;
            liens.forEach(function (lk) {
                if (lk && lk.livre) books[lk.livre] = true;
            });
        });
        var setTxt = function (id, val) {
            var el = document.getElementById(id);
            if (el) el.textContent = String(val);
        };
        setTxt('glossBadgeCount', total + ' Mécaniques');
        setTxt('telGlossTotal', total + ' termes');
        setTxt('telGlossShown', total + ' / ' + total);
        setTxt('telGlossLinks', totalLinks + ' liens');
        setTxt('telGlossMulti', multi);
        setTxt('telGlossDomaines', DATA.domaines.length + ' domaines');
        setTxt('telGlossLivres', Object.keys(books).length + ' Livres');
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
        if (els.alphaBar) {
            els.alphaBar.addEventListener('click', function (e) {
                var btn = e.target.closest('button[data-letter]');
                if (!btn || btn.disabled) return;
                state.letter = btn.dataset.letter;
                render();
            });
        }
        if (els.domPills) {
            els.domPills.addEventListener('click', function (e) {
                var btn = e.target.closest('button[data-dom]');
                if (!btn) return;
                state.domaine = btn.dataset.dom;
                render();
            });
        }
        if (els.groupBy) {
            els.groupBy.addEventListener('change', function () {
                state.groupBy = els.groupBy.value;
                render();
            });
        }
        if (els.sort) {
            els.sort.addEventListener('change', function () {
                state.sort = els.sort.value;
                render();
            });
        }
        if (els.filterBook) {
            els.filterBook.addEventListener('change', function () {
                state.book = els.filterBook.value;
                render();
            });
        }
        if (els.cols2) {
            els.cols2.addEventListener('click', function () {
                state.layout = 'cols-2';
                render();
            });
        }
        if (els.cols1) {
            els.cols1.addEventListener('click', function () {
                state.layout = 'cols-1';
                render();
            });
        }
        if (els.resetBtn) {
            els.resetBtn.addEventListener('click', resetFilters);
        }
        if (els.container) {
            els.container.addEventListener('click', function (e) {
                var copyBtn = e.target.closest('.js-copy-term');
                if (!copyBtn) return;
                var tid = copyBtn.getAttribute('data-term-id');
                var url = location.href.split('#')[0] + '#term-' + tid;
                try {
                    history.replaceState(null, '', '#term-' + tid);
                } catch (err) { /* ignore file:// */ }
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
            });
        }
        window.addEventListener('hashchange', handleHash);
    }

    async function boot() {
        els.container = document.getElementById('glossContainer');
        if (!els.container) return;

        els.search = document.getElementById('glossSearch');
        els.searchClear = document.getElementById('glossSearchClear');
        els.alphaBar = document.getElementById('glossAlphaBar');
        els.groupBy = document.getElementById('glossGroupBy');
        els.sort = document.getElementById('glossSort');
        els.filterBook = document.getElementById('glossFilterBook');
        els.cols2 = document.getElementById('glossCols2');
        els.cols1 = document.getElementById('glossCols1');
        els.resetBtn = document.getElementById('glossResetBtn');
        els.domPills = document.getElementById('glossDomPills');
        els.countBar = document.getElementById('glossCountBar');
        els.telShown = document.getElementById('telGlossShown');

        try {
            var res = await fetch('data/appendice_glossaire.json', { cache: 'no-store' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            DATA = await res.json();
        } catch (err) {
            els.container.innerHTML = errBox('Impossible de charger le glossaire (data/appendice_glossaire.json).');
            return;
        }

        DATA.domaines.forEach(function (d, idx) {
            d._order = idx;
            DOMAIN_MAP[d.id] = d;
        });
        DATA.termes.forEach(function (t, idx) {
            t._order = idx;
            buildSearchIndex(t);
        });

        populateAlphaBar();
        populateDomPills();
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

