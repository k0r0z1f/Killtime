/* ==========================================================================
   MOTEUR GÉNÉRIQUE LIVRES III+ — RENDU DEPUIS data/livre_N.json.
   La page déclare <main class="main-content" data-livre="N" id="livre-N"> et
   ne garde que les conteneurs #lxEntete / #lxContenu.
   Refs gérées : table (colonnes + lignes HTML), cartes (spec-cards,
   grille 2/3), formule (badge + math + desc).
   Mécaniques extractibles : data.mecaniques.<id> + window.LivreData.
   Traductions = traduire les chaînes du JSON, structure inchangée.
   (Livres I-II gardent leurs moteurs dédiés vérifiés, inchangés.)
   ========================================================================== */
(function () {
    'use strict';

    function errBox(msg) {
        return '<div class="callout warning"><div class="callout-body">' + msg + '</div></div>';
    }

    function thead(cols) {
        return '<thead><tr>' + cols.map(function (c) {
            return (typeof c === 'object')
                ? '<th' + c.attrs + '>' + c.html + '</th>'
                : '<th>' + c + '</th>';
        }).join('') + '</tr></thead>';
    }

    function cell(tag, c) {
        return (typeof c === 'object')
            ? '<' + tag + c.attrs + '>' + c.html + '</' + tag + '>'
            : '<' + tag + '>' + c + '</' + tag + '>';
    }

    function renderTable(t) {
        var rows = t.lignes.map(function (cells) {
            return '<tr>' + cells.map(function (c) {
                return cell('td', c);
            }).join('') + '</tr>';
        }).join('');
        return '<div class="table-container"' + (t.extra || '') + '><table class="' + (t.classe || 'codex-table') + '">' +
            thead(t.colonnes) + '<tbody>' + rows + '</tbody></table></div>';
    }

    function renderCartes(c) {
        var cards = c.cartes.map(function (k) {
            var title = '<div class="spec-card-title">' + k.titre + '</div>' +
                (k.monde ? '<div class="spec-card-world">' + k.monde + '</div>' : '');
            var head = k.top === false ? title
                : '<div class="spec-card-top">' + title + '</div>';
            var ctag = k.corps_tag || 'p';
            var corps = ('corps_brut' in k) ? k.corps_brut
                : (('corps_html' in k || 'corps_tag' in k)
                ? '<' + ctag + (k.corps_attrs || '') + '>' + (k.corps_html || '') + '</' + ctag + '>'
                : '');
            var pieds = (k.pieds || []).map(function (p) {
            return '<span class="tag-pill ' + p.ton + '">' + p.texte + '</span>';
        }).join('');
        var foot = pieds ? '<div class="spec-card-footer">' + pieds + '</div>' : '';
            return '<div class="spec-card">' + head + corps + foot + '</div>';
        });
        var seps = c.seps || [];
        var body = cards[0] || '';
        for (var i = 1; i < cards.length; i++) {
            body += (seps[i - 1] || '') + cards[i];
        }
        return '<div class="cards-grid-' + (c.grille || 3) + '"' + (c.extra || '') + '>' +
            (c.prefix || '') + body + (c.suffix || '') + '</div>';
    }

    function renderFormule(f) {var tag = f.etiquette_tag || 'span';
        var label = '<' + tag + ' class="' + (f.etiquette_classe || 'formula-badge') + '">' +
            (f.etiquette_html !== undefined ? f.etiquette_html : (f.badge || '')) + '</' + tag + '>';
        return '<div class="formula-box' + (f.wide ? ' wide' : '') + '"' + (f.extra || '') + '>' + label +
            (((f.contenu === undefined || f.contenu) ? '<div class="formula-content">' : '') +
            '<div class="formula-math"' + (f.math_attrs || '') + '>' + f.math_html +
            '</div><div class="formula-desc"' + (f.desc_attrs || '') + '>' + f.desc_html + '</div>' +
            ((f.contenu === undefined || f.contenu) ? '</div>' : '')) + '</div>';
    }

    function renderBestiaire(e) {
        var stats = e.stats.map(function (s) {
            return '<div class="stat-cell"><span class="stat-cell-label">' + s.label_html +
                '</span><span class="stat-cell-val' + (s.highlight ? ' highlight' : '') +
                '">' + s.valeur_html + '</span></div>';
        }).join('');
        return '<div class="bestiary-card' + (e.classe ? ' ' + e.classe : '') + '">' +
            '<div class="bestiary-header"><div class="bestiary-title-group"><h4>' + e.nom_html +
            '</h4><div class="bestiary-meta-sub">' + e.meta_html + '</div></div>' +
            '<span class="danger-level-badge ' + e.rang_classe + '">' + e.rang_html +
            '</span></div><p>' + e.description_html + '</p>' +
            '<div class="stat-block-grid">' + stats + '</div>' + (e.suite_html || '') + '</div>';
    }

    function renderGrilleBestiaire(g) {
        return '<div class="cards-grid-' + (g.grille || 2) + '"' + (g.extra || '') + '>' +
            g.creatures.map(renderBestiaire).join('') + '</div>';
    }

    function renderRef(b, d) {
        if (b.type !== 'ref') return b.html;
        var dRef = (d.donnees && d.donnees[b.cible]) || null;
        if (!dRef) return errBox('Bloc inconnu : ' + b.cible);
        if (dRef.type === 'table') return renderTable(dRef);
        if (dRef.type === 'cartes') return renderCartes(dRef);
        if (dRef.type === 'formule') return renderFormule(dRef);
        if (dRef.type === 'bestiaire') return renderBestiaire(dRef);
        if (dRef.type === 'grille_bestiaire') return renderGrilleBestiaire(dRef);
        return errBox('Type inconnu : ' + b.cible);
    }

    async function boot() {
        var scope = document.querySelector('main[data-livre]');
        if (!scope) return;
        var n = scope.getAttribute('data-livre');
        var entete = document.getElementById('lxEntete');
        var contenu = document.getElementById('lxContenu');
        var d;
        try {
            var res = await fetch('data/livre_' + n + '.json', { cache: 'no-store' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            d = await res.json();
        } catch (e) {
            var msg = errBox('Données Livre ' + n + ' introuvables (data/livre_' + n + '.json).');
            if (entete) entete.innerHTML = msg;
            if (contenu) contenu.innerHTML = msg;
            return;
        }
        window.LivreData = d; // source unique : mecaniques via LivreData.mecaniques.<id>
        if (entete) entete.innerHTML = d.entete_html;
        if (contenu) contenu.innerHTML = d.sections.map(function (s) {
            var html = s.titre
                ? '<h2 id="' + s.id + '"><span class="section-symbol">' + s.symbole +
                  '</span> ' + s.titre + '</h2>'
                : ''; // section sans titre (préface in-article, Livre IX) : blocs seuls
            html += s.blocs.map(function (b) {
                return renderRef(b, d);
            }).join('\n');
            return html;
        }).join('\n');
        if (window.bindCodexTips) window.bindCodexTips(scope);
        // Livre IX/XII : outils interactifs branchés après rendu (js/tools.js, filtre bestiaire).
        ['bootTools', 'bootBestiaire'].forEach(function (hook) {
            if (window[hook]) {
                try { window[hook](); } catch (e2) { /* page lisible quand même */ }
            }
        });
        if (location.hash) {
            var target = document.getElementById(location.hash.slice(1));
            if (target && target.scrollIntoView) target.scrollIntoView();
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
})();
