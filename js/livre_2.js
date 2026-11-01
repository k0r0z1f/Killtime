/* ==========================================================================
   LIVRE II — RENDU DEPUIS data/livre_2.json (source unique, tout le chapitre).
   Données règles (échelle, crans, duels, seuils, réussites auto, critiques,
   formule défi) + contenu texte (sections, paragraphes, encadrés, exemples) :
   la page ne garde que les conteneurs.
   Traductions = traduire les chaînes du JSON, structure inchangée.
   Mécaniques extractibles une par une : data.mecaniques.<id> (données
   brutes sans HTML) + window.Livre2Data au runtime, sans toucher au rendu.
   Moteur calqué sur js/livre_1.js (RP-086) ; factorisation d'un moteur
   générique prévue quand les Livres III+ seront JSON-ifiés.
   ========================================================================== */
(function () {
    'use strict';

    function errBox(msg) {
        return '<div class="callout warning"><div class="callout-body">' + msg + '</div></div>';
    }

    function cransRows(rows) {
        return rows.map(function (r) {
            var de = r.de_ton
                ? '<span class="pill-badge ' + r.de_ton + '">' + r.de_html + '</span>'
                : r.de_html;
            return '<tr><td>' + r.valeur_html + '</td>' +
                '<td><span class="pill-badge ' + r.crans_ton + '">' + r.crans_texte + '</span></td>' +
                '<td>' + de + '</td>' +
                '<td>' + r.trad_html + '</td></tr>';
        }).join('');
    }

    function duelsRows(rows) {
        return rows.map(function (r) {
            return '<tr><td>' + r.face_html + '</td>' +
                '<td>' + r.victoire_html + '</td>' +
                '<td>' + r.critique_html + '</td>' +
                '<td>' + r.table_html + '</td></tr>';
        }).join('');
    }

    function seuilsRows(rows) {
        return rows.map(function (r) {
            return '<tr><td><span class="pill-badge ' + r.seuil_ton + '">' + r.seuil_texte + '</span></td>' +
                '<td>' + r.degre_html + '</td>' +
                '<td>' + r.auto_html + '</td>' +
                '<td>' + r.exploit_html + '</td></tr>';
        }).join('');
    }

    function reussitesCards(cards) {
        return cards.map(function (c) {
            return '<div class="spec-card"><div class="spec-card-top">' +
                '<div class="spec-card-title">' + c.titre + '</div>' +
                '<div class="spec-card-world">' + c.monde + '</div></div>' +
                '<p>' + c.corps_html + '</p>' +
                '<div class="spec-card-footer">' +
                '<span class="tag-pill ' + c.pied_ton + '">' + c.pied_texte + '</span></div></div>';
        }).join('');
    }

    function critiquesRows(rows) {
        return rows.map(function (r) {
            return '<tr><td><strong>' + r.resultat + '</strong></td>' +
                '<td>' + r.offensif_html + '</td>' +
                '<td>' + r.defensif_html + '</td>' +
                '<td>' + r.social_html + '</td>' +
                '<td>' + r.utilitaire_html + '</td></tr>';
        }).join('');
    }

    function thead(cols) {
        return '<thead><tr>' + cols.map(function (c) {
            return '<th>' + c + '</th>';
        }).join('') + '</tr></thead>';
    }

    function renderRef(cible, d) {
        var k = cible.replace(/^lx/, '');
        k = k.charAt(0).toLowerCase() + k.slice(1);
        switch (k) {
            case 'crans':
                return '<div class="table-container"><table class="codex-table">' +
                    thead(d.tableaux.crans.colonnes) +
                    '<tbody>' + cransRows(d.crans) + '</tbody></table></div>';
            case 'duels':
                return '<div class="table-container"><table class="codex-table">' +
                    thead(d.tableaux.duels.colonnes) +
                    '<tbody>' + duelsRows(d.duels) + '</tbody></table></div>';
            case 'seuils':
                return '<div class="table-container"><table class="codex-table">' +
                    thead(d.tableaux.seuils.colonnes) +
                    '<tbody>' + seuilsRows(d.seuils) + '</tbody></table></div>';
            case 'reussitesAuto':
                return '<div class="cards-grid-3">' + reussitesCards(d.reussites_auto) + '</div>';
            case 'critiques':
                return '<div class="table-container"><table class="codex-table">' +
                    thead(d.tableaux.critiques.colonnes) +
                    '<tbody>' + critiquesRows(d.critiques) + '</tbody></table></div>';
            case 'formuleDefi':
                return '<div class="formula-box wide"><span class="formula-badge">' + d.defis.formule.badge +
                    '</span><div class="formula-content"><div class="formula-math">' + d.defis.formule.math_html +
                    '</div><div class="formula-desc">' + d.defis.formule.desc_html + '</div></div></div>';
            default:
                return errBox('Bloc inconnu : ' + cible);
        }
    }

    async function boot() {
        var scope = document.getElementById('livre-2');
        if (!scope) return;
        var entete = document.getElementById('lxEntete');
        var contenu = document.getElementById('lxContenu');
        var d;
        try {
            var res = await fetch('data/livre_2.json', { cache: 'no-store' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            d = await res.json();
            window.Livre2Data = d; // source unique : mecaniques via Livre2Data.mecaniques.<id>
        } catch (e) {
            var msg = errBox('Données Livre II introuvables (data/livre_2.json).');
            if (entete) entete.innerHTML = msg;
            if (contenu) contenu.innerHTML = msg;
            return;
        }
        if (entete) entete.innerHTML = d.entete_html;
        contenu.innerHTML = d.sections.map(function (s) {
            var html = '<h2 id="' + s.id + '"><span class="section-symbol">' + s.symbole +
                '</span> ' + s.titre + '</h2>';
            html += s.blocs.map(function (b) {
                return b.type === 'ref' ? renderRef(b.cible, d) : b.html;
            }).join('\n');
            return html;
        }).join('\n');
        if (window.bindCodexTips) window.bindCodexTips(scope);
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
