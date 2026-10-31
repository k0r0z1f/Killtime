/* ==========================================================================
   LIVRE I — RENDU DEPUIS data/livre_1.json (source unique, tout le chapitre).
   Données règles (lexique, espèces, budgets...) + contenu texte (sections,
   paragraphes, encadrés, exemples) : la page ne garde que les conteneurs.
   Traductions = traduire les chaînes du JSON, structure inchangée.
   ========================================================================== */
(function () {
    'use strict';

    function errBox(msg) {
        return '<div class="callout warning"><div class="callout-body">' + msg + '</div></div>';
    }

    function lexiqueRows(rows) {
        return rows.map(function (r) {
            return '<tr><td>' + r.terme_html + '</td>' +
                '<td><span class="pill-badge ' + r.ton + '">' + r.symbole + '</span></td>' +
                '<td>' + r.def_html + '</td></tr>';
        }).join('');
    }

    function echellesCards(echelles) {
        return echelles.map(function (e) {
            var body = e.niveaux.map(function (n) {
                return '<strong>' + n.nom + '</strong> : ' + n.desc;
            }).join('<br>');
            var foot = e.pied.indexOf('Niveaux') === 0
                ? '<span class="tag-pill cyan">' + e.pied + '</span>'
                : '<span class="tag-pill amber">' + e.pied + '</span>';
            return '<div class="spec-card"><div class="spec-card-top">' +
                '<div class="spec-card-title">' + e.titre + '</div>' +
                '<div class="spec-card-world">' + e.monde + '</div></div>' +
                '<p>' + body + '</p>' +
                '<div class="spec-card-footer">' + foot + '</div></div>';
        }).join('');
    }

    function especesCards(especes) {
        return especes.map(function (s) {
            var dons = s.dons.map(function (g) {
                return '<strong>' + g.id + '. ' + g.nom + ' :</strong> ' + g.effet_html;
            }).join('<br>');
            return '<div class="spec-card"><div class="spec-card-top">' +
                '<div class="spec-card-title">' + s.nom + '</div>' +
                '<div class="spec-card-world">Origine : ' + s.origine + '</div></div>' +
                '<p>' + s.description + '</p>' +
                '<p><strong>Don racial — choisissez-en 1 :</strong><br>' + dons + '</p>' +
                '<div class="spec-card-footer">' +
                '<span class="tag-pill cyan">Tech : ' + s.tech + '</span>' +
                '<span class="tag-pill ' + s.magie_ton + '">Magie : ' + s.magie + '</span>' +
                '<span class="tag-pill amber">Sens min : ' + s.sens_min + '</span></div></div>';
        }).join('');
    }

    function profilsRows(profils) {
        return profils.map(function (p) {
            var piliers = p.piliers
                ? (p.piliers[0].valeur === p.piliers[1].valeur
                    ? '2 attributs piliers à <span class="pill-badge ' + p.piliers[0].ton + '">' + p.piliers[0].valeur + '</span>'
                    : '1 attribut pilier à <span class="pill-badge ' + p.piliers[0].ton + '">' + p.piliers[0].valeur + '</span> + ' +
                      '1 attribut pilier à <span class="pill-badge ' + p.piliers[1].ton + '">' + p.piliers[1].valeur + '</span>')
                : p.piliers_texte;
            return '<tr><td><strong>' + p.nom + '</strong></td>' +
                '<td>' + piliers + '</td>' +
                '<td>' + p.secondaires_html + '</td>' +
                '<td>' + p.limites_html + '</td>' +
                '<td>' + p.eveille_html + '</td></tr>';
        }).join('');
    }

    function attributsCards(attributs) {
        var groupes = {};
        attributs.forEach(function (a) {
            (groupes[a.groupe] = groupes[a.groupe] || []).push(a);
        });
        var titres = { physiques: 'Attributs Physiques', mentaux: 'Attributs Mentaux & Sociaux' };
        return Object.keys(groupes).map(function (g) {
            var body = groupes[g].map(function (a) {
                return '<strong>' + a.nom + ' (' + a.id + ')</strong> : ' + a.role;
            }).join('<br>');
            return '<div class="spec-card"><div class="spec-card-title">' + titres[g] + '</div>' +
                '<p>' + body + '</p></div>';
        }).join('');
    }

    function sensItems(items) {
        return items.map(function (it) {
            return '<li><strong>' + it.titre + '</strong> : ' + it.texte + '</li>';
        }).join('');
    }

    function coutsRows(rows) {
        return rows.map(function (r) {
            return '<tr><td><strong>' + r.amelioration + '</strong></td>' +
                '<td><span class="pill-badge ' + r.ton_lie + '">' + r.cout_lie + '</span></td>' +
                '<td><span class="pill-badge ' + r.ton_croise + '">' + r.cout_croise + '</span></td>' +
                '<td>' + r.effet_html + '</td></tr>';
        }).join('');
    }

    function seuilsCards(seuils) {
        return seuils.map(function (s) {
            return '<div class="spec-card"><div class="spec-card-title"><span>' + s.nom +
                '</span><span class="pill-badge ' + s.ton + '">' + s.badge + '</span></div>' +
                '<p>' + s.desc_html + '</p></div>';
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
        if (k === 'coutsXP') k = 'couts';
        switch (k) {
            case 'lexique':
                return '<div class="table-container"><table class="codex-table">' +
                    thead(d.tableaux.lexique.colonnes) +
                    '<tbody>' + lexiqueRows(d.lexique) + '</tbody></table></div>';
            case 'echelles':
                return '<div class="cards-grid-2">' + echellesCards(d.echelles) + '</div>';
            case 'especes':
                return '<div class="cards-grid-3">' + especesCards(d.especes) + '</div>';
            case 'profils':
                return '<div class="table-container"><table class="codex-table">' +
                    thead(d.tableaux.profils.colonnes) +
                    '<tbody>' + profilsRows(d.profils) + '</tbody></table></div>';
            case 'attributs':
                return '<div class="cards-grid-2">' + attributsCards(d.attributs) + '</div>';
            case 'sens':
                return '<ul>' + sensItems(d.sens) + '</ul>';
            case 'formulePA':
                return '<div class="formula-box"><span class="formula-badge">' + d.pa.badge +
                    '</span><div class="formula-content"><div class="formula-math">' + d.pa.formule_html +
                    '</div><div class="formula-desc">' + d.pa.desc_html + '</div></div></div>';
            case 'seuils':
                return '<div class="cards-grid-2">' + seuilsCards(d.seuils) + '</div>';
            case 'couts':
                return '<div class="table-container"><table class="codex-table">' +
                    thead(d.tableaux.couts.colonnes) +
                    '<tbody>' + coutsRows(d.couts_xp) + '</tbody></table></div>';
            default:
                return errBox('Bloc inconnu : ' + cible);
        }
    }

    async function boot() {
        var scope = document.getElementById('livre-1');
        if (!scope) return;
        var entete = document.getElementById('lxEntete');
        var contenu = document.getElementById('lxContenu');
        var d;
        try {
            var res = await fetch('data/livre_1.json', { cache: 'no-store' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            d = await res.json();
        } catch (e) {
            var msg = errBox('Données Livre I introuvables (data/livre_1.json).');
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
