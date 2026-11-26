'use strict';

/* ==========================================================================
   PRÉ-RENDU SERVEUR (SSR) POUR LE SEO — CODEX UNIVERSEL SYSTÈME RP
   Injecte le HTML sémantique dans les conteneurs vides des 11 Livres JSON
   et des 3 Appendices lors du service HTTP, avec cache invalidé par mtime.
   Le JS client continue ensuite d'hydrater et remplacer ces conteneurs.
   ========================================================================== */

const fs = require('fs');
const path = require('path');

const LIVRE_IDS = new Set([1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12]);
const cache = new Map();

function esc(val) {
    return String(val == null ? '' : val)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

/* ---------- Rendu générique Livres (miroir de js/livre.js) ---------- */

function thead(cols) {
    return '<thead><tr>' + (cols || []).map((c) => (
        (typeof c === 'object' && c !== null)
            ? '<th' + (c.attrs || '') + '>' + (c.html || '') + '</th>'
            : '<th>' + c + '</th>'
    )).join('') + '</tr></thead>';
}

function cell(tag, c) {
    return (typeof c === 'object' && c !== null)
        ? '<' + tag + (c.attrs || '') + '>' + (c.html || '') + '</' + tag + '>'
        : '<' + tag + '>' + c + '</' + tag + '>';
}

function renderTable(t) {
    const rows = (t.lignes || []).map((cells) => (
        '<tr>' + cells.map((c) => cell('td', c)).join('') + '</tr>'
    )).join('');
    return '<div class="table-container"' + (t.extra || '') + '><table class="' + (t.classe || 'codex-table') + '">' +
        thead(t.colonnes) + '<tbody>' + rows + '</tbody></table></div>';
}

function renderCartes(c) {
    const cards = (c.cartes || []).map((k) => {
        const title = '<div class="spec-card-title">' + (k.titre || '') + '</div>' +
            (k.monde ? '<div class="spec-card-world">' + k.monde + '</div>' : '');
        const head = k.top === false ? title : '<div class="spec-card-top">' + title + '</div>';
        const ctag = k.corps_tag || 'p';
        const corps = ('corps_brut' in k) ? k.corps_brut
            : (('corps_html' in k || 'corps_tag' in k)
                ? '<' + ctag + (k.corps_attrs || '') + '>' + (k.corps_html || '') + '</' + ctag + '>'
                : '');
        const pieds = (k.pieds || []).map((p) => (
            '<span class="tag-pill ' + (p.ton || '') + '">' + (p.texte || '') + '</span>'
        )).join('');
        const foot = pieds ? '<div class="spec-card-footer">' + pieds + '</div>' : '';
        return '<div class="spec-card">' + head + corps + foot + '</div>';
    });
    const seps = c.seps || [];
    let body = cards[0] || '';
    for (let i = 1; i < cards.length; i++) {
        body += (seps[i - 1] || '') + cards[i];
    }
    return '<div class="cards-grid-' + (c.grille || 3) + '"' + (c.extra || '') + '>' +
        (c.prefix || '') + body + (c.suffix || '') + '</div>';
}

function renderFormule(f) {
    const tag = f.etiquette_tag || 'span';
    const label = '<' + tag + ' class="' + (f.etiquette_classe || 'formula-badge') + '">' +
        (f.etiquette_html !== undefined ? f.etiquette_html : (f.badge || '')) + '</' + tag + '>';
    const wrap = f.contenu === undefined || f.contenu;
    return '<div class="formula-box' + (f.wide ? ' wide' : '') + '"' + (f.extra || '') + '>' + label +
        (wrap ? '<div class="formula-content">' : '') +
        '<div class="formula-math"' + (f.math_attrs || '') + '>' + (f.math_html || '') + '</div>' +
        '<div class="formula-desc"' + (f.desc_attrs || '') + '>' + (f.desc_html || '') + '</div>' +
        (wrap ? '</div>' : '') + '</div>';
}

function renderBestiaire(e) {
    const stats = (e.stats || []).map((s) => (
        '<div class="stat-cell"><span class="stat-cell-label">' + (s.label_html || '') +
        '</span><span class="stat-cell-val' + (s.highlight ? ' highlight' : '') + '">' +
        (s.valeur_html || '') + '</span></div>'
    )).join('');
    return '<div class="bestiary-card' + (e.classe ? ' ' + e.classe : '') + '">' +
        '<div class="bestiary-header"><div class="bestiary-title-group"><h4>' + (e.nom_html || '') +
        '</h4><div class="bestiary-meta-sub">' + (e.meta_html || '') + '</div></div>' +
        '<span class="danger-level-badge ' + (e.rang_classe || '') + '">' + (e.rang_html || '') +
        '</span></div><p>' + (e.description_html || '') + '</p>' +
        '<div class="stat-block-grid">' + stats + '</div>' + (e.suite_html || '') + '</div>';
}

function renderGrilleBestiaire(g) {
    return '<div class="cards-grid-' + (g.grille || 2) + '"' + (g.extra || '') + '>' +
        (g.creatures || []).map(renderBestiaire).join('') + '</div>';
}

function renderRef(b, d) {
    if (!b || b.type !== 'ref') return (b && b.html) || '';
    const dRef = (d.donnees && d.donnees[b.cible]) || null;
    if (!dRef) return ''; // Livres I-II utilisent des refs dédiées en JS
    if (dRef.type === 'table') return renderTable(dRef);
    if (dRef.type === 'cartes') return renderCartes(dRef);
    if (dRef.type === 'formule') return renderFormule(dRef);
    if (dRef.type === 'bestiaire') return renderBestiaire(dRef);
    if (dRef.type === 'grille_bestiaire') return renderGrilleBestiaire(dRef);
    return '';
}

function renderLivreHtml(rawHtml, d) {
    let out = rawHtml;
    if (d.entete_html) {
        out = out.replace(/(<header[^>]*\bid="lxEntete"[^>]*>)\s*(<\/header>)/, (_, open, close) => (
            open + d.entete_html + close
        ));
    }
    const sectionsHtml = (d.sections || []).map((s) => {
        const heading = s.titre
            ? '<h2 id="' + esc(s.id) + '"><span class="section-symbol">' + (s.symbole || '') +
              '</span> ' + s.titre + '</h2>'
            : '';
        const blocs = (s.blocs || []).map((b) => renderRef(b, d)).join('\n');
        return heading + blocs;
    }).join('\n');
    out = out.replace(/(<article[^>]*\bid="lxContenu"[^>]*>)\s*(<\/article>)/, (_, open, close) => (
        open + sectionsHtml + close
    ));
    return out;
}

/* ---------- Rendu sémantique des Appendices ---------- */

function renderActionsHtml(rawHtml, d) {
    const byCat = new Map();
    for (const a of (d.actions || [])) {
        if (!byCat.has(a.categorie)) byCat.set(a.categorie, []);
        byCat.get(a.categorie).push(a);
    }
    const sections = (d.categories || []).map((c) => {
        const items = (byCat.get(c.id) || []).map((a) => {
            const meta = [a.cadence, a.cout_pa].filter(Boolean).map(esc).join(', ');
            return '<li><strong>' + esc(a.nom) + '</strong>' +
                (meta ? ' (' + meta + ')' : '') +
                (a.resume ? ' — ' + esc(a.resume) : '') + '</li>';
        }).join('');
        return '<section class="ssr-seo-list" id="cat-' + esc(c.id) + '">' +
            '<h2>' + esc(c.code) + ' — ' + esc(c.nom) + '</h2>' +
            (c.desc ? '<p>' + esc(c.desc) + '</p>' : '') +
            '<ul>' + items + '</ul></section>';
    }).join('\n');
    return rawHtml.replace(/(<div[^>]*\bid="actionsContainer"[^>]*>)\s*(<\/div>)/, (_, open, close) => (
        open + sections + close
    ));
}

function renderGlossaireHtml(rawHtml, d) {
    const byDom = new Map();
    for (const tm of (d.termes || [])) {
        if (!byDom.has(tm.domaine)) byDom.set(tm.domaine, []);
        byDom.get(tm.domaine).push(tm);
    }
    const sections = (d.domaines || []).map((dm) => {
        const items = (byDom.get(dm.id) || []).map((tm) => {
            const aliases = Array.isArray(tm.alias) && tm.alias.length
                ? ' (' + tm.alias.map(esc).join(', ') + ')'
                : '';
            const links = (tm.liens || []).map((lk) => (
                '<a href="' + esc(lk.href) + '">' + esc(lk.label) + '</a>'
            )).join(', ');
            return '<li><strong>' + esc(tm.terme) + '</strong>' + aliases +
                (links ? ' : ' + links : '') + '</li>';
        }).join('');
        return '<section class="ssr-seo-list" id="dom-' + esc(dm.id) + '">' +
            '<h2>' + esc(dm.code) + ' — ' + esc(dm.nom) + '</h2>' +
            '<ul>' + items + '</ul></section>';
    }).join('\n');
    return rawHtml.replace(/(<div[^>]*\bid="glossContainer"[^>]*>)\s*(<\/div>)/, (_, open, close) => (
        open + sections + close
    ));
}

function renderObjetsHtml(rawHtml, d) {
    const byCat = new Map();
    for (const o of (d.objets || [])) {
        if (!byCat.has(o.categorie)) byCat.set(o.categorie, []);
        byCat.get(o.categorie).push(o);
    }
    const sections = (d.categories || []).map((c) => {
        const items = (byCat.get(c.id) || []).map((o) => (
            '<li><strong>' + esc(o.nom) + '</strong> [' + esc(o.epoque_label) + ']' +
            (o.dlph ? ' (' + esc(o.dlph) + ')' : '') +
            (o.resume ? ' — ' + esc(o.resume) : '') + '</li>'
        )).join('');
        return '<section class="ssr-seo-list" id="cat-' + esc(c.id) + '">' +
            '<h2>' + esc(c.code) + ' — ' + esc(c.nom) + '</h2>' +
            (c.desc ? '<p>' + esc(c.desc) + '</p>' : '') +
            '<ul>' + items + '</ul></section>';
    }).join('\n');
    return rawHtml.replace(/(<div[^>]*\bid="objetsContainer"[^>]*>)\s*(<\/div>)/, (_, open, close) => (
        open + sections + close
    ));
}

function resolveTarget(safePath) {
    const clean = String(safePath || '').replace(/\\/g, '/');
    const livreMatch = /^\/livre_(\d+)\.html$/i.exec(clean);
    if (livreMatch) {
        const n = parseInt(livreMatch[1], 10);
        if (!LIVRE_IDS.has(n)) return null;
        return {
            key: '/livre_' + n + '.html',
            htmlRel: 'livre_' + n + '.html',
            jsonRel: path.join('data', 'livre_' + n + '.json'),
            render: renderLivreHtml
        };
    }
    if (clean === '/appendice_actions.html') {
        return {
            key: clean,
            htmlRel: 'appendice_actions.html',
            jsonRel: path.join('data', 'appendice_actions.json'),
            render: renderActionsHtml
        };
    }
    if (clean === '/appendice_glossaire.html') {
        return {
            key: clean,
            htmlRel: 'appendice_glossaire.html',
            jsonRel: path.join('data', 'appendice_glossaire.json'),
            render: renderGlossaireHtml
        };
    }
    if (clean === '/appendice_objets.html') {
        return {
            key: clean,
            htmlRel: 'appendice_objets.html',
            jsonRel: path.join('data', 'appendice_objets.json'),
            render: renderObjetsHtml
        };
    }
    return null;
}

/**
 * Retourne { buffer, mtime } pré-rendu pour les pages Codex JSON, ou null.
 * Met en cache par chemin et invalide automatiquement dès que html/json change.
 */
function renderSsrHtml(publicDir, safePath) {
    const target = resolveTarget(safePath);
    if (!target) return null;
    try {
        const htmlPath = path.join(publicDir, target.htmlRel);
        const jsonPath = path.join(publicDir, target.jsonRel);
        const htmlStat = fs.statSync(htmlPath);
        const jsonStat = fs.statSync(jsonPath);
        const cacheKey = publicDir + '\0' + target.key;
        const hit = cache.get(cacheKey);
        if (hit && hit.htmlMtimeMs === htmlStat.mtimeMs && hit.jsonMtimeMs === jsonStat.mtimeMs) {
            return { buffer: hit.buffer, mtime: hit.mtime };
        }
        const rawHtml = fs.readFileSync(htmlPath, 'utf8');
        const data = JSON.parse(fs.readFileSync(jsonPath, 'utf8'));
        const rendered = target.render(rawHtml, data);
        const buffer = Buffer.from(rendered, 'utf8');
        const mtime = new Date(Math.max(htmlStat.mtimeMs, jsonStat.mtimeMs));
        cache.set(cacheKey, {
            htmlMtimeMs: htmlStat.mtimeMs,
            jsonMtimeMs: jsonStat.mtimeMs,
            buffer,
            mtime
        });
        return { buffer, mtime };
    } catch (e) {
        return null;
    }
}

module.exports = { renderSsrHtml };
