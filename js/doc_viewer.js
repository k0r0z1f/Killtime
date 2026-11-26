/**
 * CODEX UNIVERSEL — LECTEUR MARKDOWN INTÉGRÉ (Pilotage)
 * Charge WHATS_NEW.md, docs/mecaniques_systeme_rp_6f497f7.md ou README.md
 * via ?doc=whats-new | baseline | readme et génère le sommaire live.
 */
(function () {
    'use strict';

    var DOCS = {
        'whats-new': {
            key: 'whats-new',
            file: 'WHATS_NEW.md',
            navLabel: "What's New (Système RP)",
            badgeTag: 'Pilotage — Nouveautés',
            badgeStatus: 'v0.8.0',
            title: "What's New — Changelog Système RP & Codex",
            lead: 'Journal versionné (SemVer) des évolutions mécaniques et plateforme depuis la baseline 6f497f7 jusqu’à la version courante.'
        },
        baseline: {
            key: 'baseline',
            file: 'docs/mecaniques_systeme_rp_6f497f7.md',
            navLabel: 'Mécaniques de Base (6f497f7)',
            badgeTag: 'Pilotage — Référence Baseline',
            badgeStatus: 'Commit 6f497f7 inclus',
            title: 'Mécaniques Détaillées — Baseline 6f497f7',
            lead: 'Document de référence complet des règles du Système RP (Livres I à XII, Outils Web Livre IX, Appendices A et B) figé au commit 6f497f7.'
        },
        readme: {
            key: 'readme',
            file: 'README.md',
            navLabel: 'README Projet',
            badgeTag: 'Pilotage — Vue Racine',
            badgeStatus: 'Dépôt GitHub',
            title: 'Système de Jeu RP — Codex Universel (README)',
            lead: 'Présentation générale du dépôt, architecture des 12 Livres, démarrage rapide et manifeste éditorial.'
        }
    };

    var MD_LINK_MAP = {
        'whats_new.md': 'doc_viewer.html?doc=whats-new',
        'README.md': 'doc_viewer.html?doc=readme',
        'readme.md': 'doc_viewer.html?doc=readme',
        'docs/mecaniques_systeme_rp_6f497f7.md': 'doc_viewer.html?doc=baseline',
        'summaries/mecaniques_systeme_rp_6f497f7.md': 'doc_viewer.html?doc=baseline'
    };

    function escapeHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function stripMdForSlug(text) {
        return String(text || '')
            .replace(/!\[([^\]]*)\]\([^)]+\)/g, '$1')
            .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
            .replace(/[`*_~]/g, '')
            .replace(/<[^>]+>/g, '');
    }

    function ghSlug(headingText) {
        var s = stripMdForSlug(headingText).trim().toLowerCase();
        try {
            s = s.replace(/[^\p{L}\p{N}_\s-]/gu, '');
        } catch (e) {
            s = s.replace(/[^\w\s-]/g, '');
        }
        return s.replace(/ /g, '-');
    }

    function rewriteUrl(href) {
        var raw = String(href || '').trim();
        if (!raw) return { url: '#', external: false };
        if (raw.charAt(0) === '#') return { url: raw, external: false };
        var clean = raw.replace(/^\.\//, '');
        var parts = clean.split('#');
        var base = parts[0];
        var hash = parts.length > 1 ? ('#' + parts.slice(1).join('#')) : '';
        var mapped = MD_LINK_MAP[base] || MD_LINK_MAP[base.toLowerCase()];
        if (mapped) {
            return { url: mapped + hash, external: false };
        }
        if (/^https?:\/\//i.test(raw)) {
            return { url: raw, external: true };
        }
        return { url: raw, external: false };
    }

    function formatInline(text, options) {
        var allowBr = !!(options && options.allowBr);
        var tokens = [];
        function hold(html) {
            var idx = tokens.length;
            tokens.push(html);
            return '\x00TOK' + idx + '\x00';
        }

        var s = String(text == null ? '' : text);

        // 1. Protéger les spans code inline avant tout échappement/formatage.
        s = s.replace(/`([^`\n]+)`/g, function (_, inner) {
            return hold('<code>' + escapeHtml(inner) + '</code>');
        });

        // 2. Préserver <br> / <br/> explicites si demandé (cellules de tableaux).
        if (allowBr) {
            s = s.replace(/<br\s*\/?>/gi, function () {
                return hold('<br>');
            });
        }

        // 3. Images cliquables [![alt](img)](href)
        s = s.replace(/\[!\[([^\]]*)\]\(([^)\s]+)\)\]\(([^)]+)\)/g, function (_, alt, src, href) {
            var r = rewriteUrl(href);
            var attrs = r.external ? ' target="_blank" rel="noopener noreferrer"' : '';
            return hold('<a href="' + escapeHtml(r.url) + '"' + attrs + '><img src="' + escapeHtml(src) + '" alt="' + escapeHtml(alt) + '"></a>');
        });

        // 4. Images simples ![alt](img)
        s = s.replace(/!\[([^\]]*)\]\(([^)\s]+)\)/g, function (_, alt, src) {
            return hold('<img src="' + escapeHtml(src) + '" alt="' + escapeHtml(alt) + '">');
        });

        // 5. Liens [label](href)
        s = s.replace(/\[([^\]]+)\]\(([^)]+)\)/g, function (_, label, href) {
            var r = rewriteUrl(href);
            var attrs = r.external ? ' target="_blank" rel="noopener noreferrer"' : '';
            var renderedLabel = formatInline(label, { allowBr: allowBr });
            return hold('<a href="' + escapeHtml(r.url) + '"' + attrs + '>' + renderedLabel + '</a>');
        });

        // 6. Échapper le reste du texte brut.
        s = escapeHtml(s);

        // 7. Gras + italique, gras, italique (sans casser snake_case).
        s = s.replace(/\*\*\*([^*\n]+?)\*\*\*/g, '<strong><em>$1</em></strong>');
        s = s.replace(/\*\*([^*\n]+?)\*\*/g, '<strong>$1</strong>');
        s = s.replace(/__([^_\n]+?)__/g, '<strong>$1</strong>');
        s = s.replace(/(^|[\s(>])\*([^*\s][^*\n]*?)\*(?=[\s).,;:!?<]|$)/g, '$1<em>$2</em>');
        s = s.replace(/(^|[\s(>])\*([^*\n]+?)\*(?=[\s).,;:!?<]|$)/g, '$1<em>$2</em>');
        s = s.replace(/\*([^*\n]+?)\*/g, '<em>$1</em>');
        s = s.replace(/(^|[\s(>])_([^_\s][^_\n]*?)_(?=[\s).,;:!?<]|$)/g, '$1<em>$2</em>');

        // 8. Rétablir les jetons protégés.
        s = s.replace(/\x00TOK(\d+)\x00/g, function (_, idx) {
            return tokens[parseInt(idx, 10)] || '';
        });
        return s;
    }

    function splitTableRow(line) {
        var raw = String(line || '').trim();
        if (raw.charAt(0) === '|') raw = raw.slice(1);
        if (raw.charAt(raw.length - 1) === '|') raw = raw.slice(0, -1);
        var cells = [];
        var cur = '';
        var inCode = false;
        for (var i = 0; i < raw.length; i++) {
            var ch = raw.charAt(i);
            if (ch === '`') {
                inCode = !inCode;
                cur += ch;
            } else if (ch === '\\' && raw.charAt(i + 1) === '|') {
                cur += '|';
                i++;
            } else if (ch === '|' && !inCode) {
                cells.push(cur.trim());
                cur = '';
            } else {
                cur += ch;
            }
        }
        cells.push(cur.trim());
        return cells;
    }

    function isTableSeparator(line) {
        if (!line || line.indexOf('|') < 0) return false;
        var cells = splitTableRow(line);
        if (!cells.length) return false;
        return cells.every(function (c) { return /^:?-{3,}:?$/.test(c.trim()); });
    }

    function parseAlignments(line) {
        return splitTableRow(line).map(function (c) {
            var t = c.trim();
            if (t.charAt(0) === ':' && t.charAt(t.length - 1) === ':') return 'center';
            if (t.charAt(t.length - 1) === ':') return 'right';
            return 'left';
        });
    }

    function matchListLine(line) {
        var m = /^( *)([-*+]|\d+\.)\s+(.*)$/.exec(line);
        if (!m) return null;
        return {
            indent: m[1].length,
            type: /^\d+\./.test(m[2]) ? 'ol' : 'ul',
            start: /^\d+\./.test(m[2]) ? parseInt(m[2], 10) : 1,
            text: m[3]
        };
    }

    function renderMarkdown(md) {
        var lines = String(md || '').replace(/\r\n?/g, '\n').split('\n');
        var out = [];
        var h2List = [];
        var slugCounts = {};
        var tableCount = 0;
        var i = 0;

        function nextId(rawHeading) {
            var base = ghSlug(rawHeading) || 'section';
            if (slugCounts[base] === undefined) {
                slugCounts[base] = 0;
                return base;
            }
            slugCounts[base] += 1;
            return base + '-' + slugCounts[base];
        }

        function parseListBlock(startIdx) {
            var items = [];
            var first = matchListLine(lines[startIdx]);
            var baseIndent = first.indent;
            var listType = first.type;
            var firstNum = first.start;
            var idx = startIdx;

            while (idx < lines.length) {
                var line = lines[idx];
                if (!line.trim()) {
                    // Saut d'une ligne vide seulement si l'élément suivant reste dans la même liste.
                    var nxt = idx + 1;
                    if (nxt < lines.length && matchListLine(lines[nxt]) && matchListLine(lines[nxt]).indent >= baseIndent) {
                        idx++;
                        continue;
                    }
                    break;
                }
                var lm = matchListLine(line);
                if (lm) {
                    if (lm.indent < baseIndent || (lm.indent === baseIndent && lm.type !== listType)) {
                        break;
                    }
                    if (lm.indent === baseIndent) {
                        items.push({ textLines: [lm.text], childrenHtml: '' });
                        idx++;
                    } else {
                        var sub = parseListBlock(idx);
                        if (items.length) {
                            items[items.length - 1].childrenHtml += sub.html;
                        } else {
                            items.push({ textLines: [''], childrenHtml: sub.html });
                        }
                        idx = sub.nextIndex;
                    }
                } else if (/^\s{2,}\S/.test(line) && items.length) {
                    items[items.length - 1].textLines.push(line.trim());
                    idx++;
                } else {
                    break;
                }
            }

            var tag = listType;
            var openAttr = (tag === 'ol' && firstNum !== 1) ? (' start="' + firstNum + '"') : '';
            var html = '<' + tag + openAttr + '>\n' + items.map(function (it) {
                var body = it.textLines.map(function (tl, k) {
                    return (k === 0 ? '' : '<br>') + formatInline(tl, { allowBr: true });
                }).join(' ');
                return '<li>' + body + it.childrenHtml + '</li>';
            }).join('\n') + '\n</' + tag + '>';

            return { html: html, nextIndex: idx };
        }

        while (i < lines.length) {
            var line = lines[i];

            // Lignes vides
            if (!line.trim()) {
                i++;
                continue;
            }

            // Bloc de code clôturé ```
            var fence = /^```([^\s`]*)\s*$/.exec(line.trim());
            if (fence) {
                var lang = fence[1];
                var codeLines = [];
                i++;
                while (i < lines.length && !/^```\s*$/.test(lines[i].trim())) {
                    codeLines.push(lines[i]);
                    i++;
                }
                if (i < lines.length) i++;
                var cls = lang ? ' class="language-' + escapeHtml(lang) + '"' : '';
                out.push('<pre class="doc-code-block"><code' + cls + '>' + escapeHtml(codeLines.join('\n')) + '</code></pre>');
                continue;
            }

            // Séparateur horizontal ---
            if (/^\s*(?:-{3,}|\*{3,}|_{3,})\s*$/.test(line)) {
                out.push('<hr>');
                i++;
                continue;
            }

            // Titres # ... ######
            var hm = /^(#{1,5})\s+(.+?)\s*$/.exec(line);
            if (hm) {
                var level = hm[1].length;
                var rawTitle = hm[2].replace(/\s+#+\s*$/, '');
                var id = nextId(rawTitle);
                out.push('<h' + level + ' id="' + escapeHtml(id) + '">' + formatInline(rawTitle) + '</h' + level + '>');
                if (level === 2) {
                    h2List.push({ id: id, text: stripMdForSlug(rawTitle).trim() });
                }
                i++;
                continue;
            }

            // Citation > ...
            if (/^\s*>/.test(line)) {
                var qLines = [];
                while (i < lines.length && /^\s*>/.test(lines[i])) {
                    qLines.push(lines[i].replace(/^\s*>\s?/, ''));
                    i++;
                }
                var qHtml = qLines.map(function (ql, k) {
                    var hardBreak = /\s{2}$/.test(ql);
                    var formatted = formatInline(ql.trimEnd(), { allowBr: true });
                    return formatted + (hardBreak && k < qLines.length - 1 ? '<br>' : ' ');
                }).join('');
                out.push('<blockquote><p>' + qHtml.trim() + '</p></blockquote>');
                continue;
            }

            // Tableau GFM
            if (line.indexOf('|') >= 0 && i + 1 < lines.length && isTableSeparator(lines[i + 1])) {
                var headers = splitTableRow(line);
                var aligns = parseAlignments(lines[i + 1]);
                i += 2;
                var rows = [];
                while (i < lines.length && lines[i].trim() && lines[i].indexOf('|') >= 0) {
                    rows.push(splitTableRow(lines[i]));
                    i++;
                }
                tableCount += 1;
                var thead = '<thead><tr>' + headers.map(function (h, ci) {
                    var al = aligns[ci] ? ' style="text-align:' + aligns[ci] + ';"' : '';
                    return '<th' + al + '>' + formatInline(h, { allowBr: true }) + '</th>';
                }).join('') + '</tr></thead>';
                var tbody = '<tbody>' + rows.map(function (r) {
                    return '<tr>' + headers.map(function (_, ci) {
                        var al = aligns[ci] ? ' style="text-align:' + aligns[ci] + ';"' : '';
                        return '<td' + al + '>' + formatInline(r[ci] || '', { allowBr: true }) + '</td>';
                    }).join('') + '</tr>';
                }).join('\n') + '</tbody>';
                out.push('<div class="table-container"><table class="codex-table">' + thead + tbody + '</table></div>');
                continue;
            }

            // Listes
            if (matchListLine(line)) {
                var listRes = parseListBlock(i);
                out.push(listRes.html);
                i = listRes.nextIndex;
                continue;
            }

            // Paragraphe (incluant groupes de badges consécutifs)
            var pLines = [];
            while (i < lines.length) {
                var pl = lines[i];
                if (!pl.trim() || /^\s*>/.test(pl) || /^(#{1,5})\s+/.test(pl) || /^```/.test(pl.trim()) ||
                    /^\s*(?:-{3,}|\*{3,}|_{3,})\s*$/.test(pl) || matchListLine(pl) ||
                    (pl.indexOf('|') >= 0 && i + 1 < lines.length && isTableSeparator(lines[i + 1]))) {
                    break;
                }
                pLines.push(pl);
                i++;
            }
            var isAllBadges = pLines.length > 0 && pLines.every(function (pl) {
                return /^\s*\[!\[[^\]]*\]\([^)]+\)\]\([^)]+\)\s*$/.test(pl);
            });
            if (isAllBadges) {
                out.push('<div class="doc-badge-strip">' + pLines.map(function (pl) {
                    return formatInline(pl.trim());
                }).join('') + '</div>');
            } else {
                var pHtml = pLines.map(function (pl, k) {
                    var hardBreak = /\s{2}$/.test(pl);
                    return formatInline(pl.trimEnd()) + (hardBreak && k < pLines.length - 1 ? '<br>' : ' ');
                }).join('');
                out.push('<p>' + pHtml.trim() + '</p>');
            }
        }

        return {
            html: out.join('\n'),
            h2List: h2List,
            lineCount: lines.length,
            tableCount: tableCount
        };
    }

    function currentDocKey() {
        try {
            var sp = new URLSearchParams(window.location.search);
            var d = (sp.get('doc') || 'whats-new').trim().toLowerCase();
            if (DOCS[d]) return d;
        } catch (e) {}
        return 'whats-new';
    }

    function updateUiMeta(meta) {
        document.title = meta.navLabel + ' | Codex Universel Système RP';
        var bc = document.getElementById('docViewerBreadcrumb');
        if (bc) bc.textContent = meta.navLabel;
        var bTag = document.getElementById('docBadgeTag');
        if (bTag) bTag.textContent = meta.badgeTag;
        var bFile = document.getElementById('docBadgeFile');
        if (bFile) bFile.textContent = meta.file;
        var bStatus = document.getElementById('docBadgeStatus');
        if (bStatus) bStatus.textContent = meta.badgeStatus;
        var titleEl = document.getElementById('docViewerTitle');
        if (titleEl) titleEl.textContent = meta.title;
        var leadEl = document.getElementById('docViewerLead');
        if (leadEl) leadEl.textContent = meta.lead;
        var rawLink = document.getElementById('docRawLink');
        if (rawLink) rawLink.href = meta.file;

        document.querySelectorAll('[data-doc-tab]').forEach(function (el) {
            el.classList.toggle('active', el.getAttribute('data-doc-tab') === meta.key);
        });

        // Marque l'entrée active dans le groupe Pilotage de la sidebar.
        var targetHref = 'doc_viewer.html?doc=' + meta.key;
        document.querySelectorAll('.sidebar .nav-item-link').forEach(function (a) {
            if (a.getAttribute('href') === targetHref) {
                a.classList.add('active');
            }
        });
    }

    function bootDocViewer() {
        var bodyEl = document.getElementById('docViewerBody');
        if (!bodyEl) return;

        var key = currentDocKey();
        var meta = DOCS[key];
        updateUiMeta(meta);

        var loadedMarkdown = '';
        var copyBtn = document.getElementById('docCopyBtn');
        if (copyBtn) {
            copyBtn.addEventListener('click', function () {
                if (!loadedMarkdown || !navigator.clipboard) return;
                navigator.clipboard.writeText(loadedMarkdown).then(function () {
                    var old = copyBtn.textContent;
                    copyBtn.textContent = 'Copié !';
                    setTimeout(function () { copyBtn.textContent = old; }, 1800);
                });
            });
        }

        fetch(meta.file, { cache: 'no-store' })
            .then(function (res) {
                if (!res.ok) throw new Error('HTTP ' + res.status);
                return res.text();
            })
            .then(function (md) {
                loadedMarkdown = md;
                var rendered = renderMarkdown(md);
                bodyEl.innerHTML = rendered.html;

                var tocEl = document.getElementById('docViewerToc');
                if (tocEl) {
                    var tocItems = ['<li><a href="#doc-viewer-root">Haut de page</a></li>'].concat(
                        rendered.h2List.map(function (h) {
                            return '<li><a href="#' + escapeHtml(h.id) + '">' + escapeHtml(h.text) + '</a></li>';
                        })
                    );
                    tocEl.innerHTML = tocItems.join('');
                }

                var fEl = document.getElementById('telDocFile');
                if (fEl) fEl.textContent = meta.file;
                var lEl = document.getElementById('telDocLines');
                if (lEl) lEl.textContent = rendered.lineCount + ' lignes';
                var sEl = document.getElementById('telDocSections');
                if (sEl) sEl.textContent = String(rendered.h2List.length);
                var tEl = document.getElementById('telDocTables');
                if (tEl) tEl.textContent = String(rendered.tableCount);

                if (window.location.hash && window.location.hash.length > 1) {
                    var target = document.getElementById(decodeURIComponent(window.location.hash.slice(1)));
                    if (target) target.scrollIntoView();
                }
            })
            .catch(function (err) {
                bodyEl.innerHTML = '<div class="doc-md-error"><strong>Impossible de charger <code>' +
                    escapeHtml(meta.file) + '</code></strong> (' + escapeHtml(err.message) + ').<br>' +
                    'Lancez le serveur local (<code>bash start.sh</code> ou <code>node users/server.js</code>), ou ouvrez directement ' +
                    '<a href="' + escapeHtml(meta.file) + '" target="_blank" rel="noopener noreferrer">' + escapeHtml(meta.file) + '</a>.</div>';
            });
    }

    if (typeof document !== 'undefined') {
        document.addEventListener('DOMContentLoaded', bootDocViewer);
    }

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = { renderMarkdown: renderMarkdown, ghSlug: ghSlug, DOCS: DOCS };
    }
})();
