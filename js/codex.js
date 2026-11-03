/* ==========================================================================
   CODEX UNIVERSEL DU SYSTÈME RP — LOGIQUE INTERACTIVE V2.5
   Navigation, Accordéon, Recherche, Filtres, Thème & Scrollspy
   ========================================================================== */

document.addEventListener('DOMContentLoaded', () => {
    // 1. Gestion de l'accordéon dans la sidebar (état persisté : un groupe
    // refermé le reste après navigation, fini les réouvertures à chaque page).
    // Exécuté immédiatement (pas en DOMContentLoaded) pour éviter le flash
    // ouvert → fermé : le script est en fin de body, la sidebar existe déjà.
    const navKey = t => 'codex-nav:' + t;
    const restoreNav = () => {
        document.querySelectorAll('.nav-group').forEach(group => {
            const tag = group.querySelector('.group-tag');
            const id = tag ? tag.textContent.trim() : null;
            if (id) {
                try {
                    const saved = localStorage.getItem(navKey(id));
                    if (saved === '0') group.classList.remove('expanded');
                    else if (saved === '1') group.classList.add('expanded');
                } catch (e) {}
                group.dataset.navId = id;
            }
        });
    };
    restoreNav();
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', restoreNav);
    }
    document.querySelectorAll('.nav-group-header').forEach(header => {
        header.addEventListener('click', () => {
            const group = header.parentElement;
            group.classList.toggle('expanded');
            const id = group.dataset.navId;
            if (id) {
                try {
                    localStorage.setItem(navKey(id), group.classList.contains('expanded') ? '1' : '0');
                } catch (e) {}
            }
        });
    });

    // 2. Filtrage en direct de la table des matières
    const filterInput = document.getElementById('filterInput');
    if (filterInput) {
        filterInput.addEventListener('input', (e) => {
            const query = e.target.value.toLowerCase().trim();
            document.querySelectorAll('.nav-group').forEach(group => {
                if (!query) {
                    group.style.display = '';
                } else {
                    const text = group.textContent.toLowerCase();
                    if (text.includes(query)) {
                        group.style.display = 'block';
                        group.classList.add('expanded');
                    } else {
                        group.style.display = 'none';
                    }
                }
            });
        });
    }

    // 3. Raccourci clavier Ctrl+K / Cmd+K pour recherche
    window.addEventListener('keydown', (e) => {
        if ((e.ctrlKey || e.metaKey) && e.key && e.key.toLowerCase() === 'k') {
            e.preventDefault();
            if (filterInput) {
                filterInput.focus();
                filterInput.select();
            }
        }
    });

    const searchTrigger = document.getElementById('searchTrigger');
    if (searchTrigger && filterInput) {
        searchTrigger.addEventListener('click', () => {
            filterInput.focus();
            filterInput.select();
        });
    }

    // 4. Menu mobile avec backdrop interactif
    const mobileToggle = document.getElementById('mobileToggle');
    const sidebar = document.getElementById('sidebar');
    
    // Création dynamique du backdrop s'il n'existe pas
    let backdrop = document.querySelector('.sidebar-backdrop');
    if (!backdrop) {
        backdrop = document.createElement('div');
        backdrop.className = 'sidebar-backdrop';
        document.body.appendChild(backdrop);
    }

    function closeSidebar() {
        if (sidebar) sidebar.classList.remove('mobile-open');
        if (backdrop) backdrop.classList.remove('active');
    }

    function toggleSidebar() {
        if (sidebar) {
            sidebar.classList.toggle('mobile-open');
            if (backdrop) backdrop.classList.toggle('active', sidebar.classList.contains('mobile-open'));
        }
    }

    if (mobileToggle && sidebar) {
        mobileToggle.addEventListener('click', (e) => {
            e.stopPropagation();
            toggleSidebar();
        });

        backdrop.addEventListener('click', closeSidebar);

        // Fermer automatiquement le menu après clic sur un lien (sur mobile)
        document.querySelectorAll('.sidebar-nav a').forEach(link => {
            link.addEventListener('click', () => {
                if (window.innerWidth <= 960) {
                    closeSidebar();
                }
            });
        });
    }

    // 5. Thèmes : gérés par js/theme-init.js (application anti-flash) et
    // js/theme.js (menu multi-thèmes, persistance, préférence système).
    // Ne pas réintroduire ici de bascule binaire dark/light.

    // 6. Scrollspy automatique pour surligner les ancres actives
    // (panneau droit + menu lateral gauche).
    const updateSpy = () => {
        const headings = document.querySelectorAll('h2[id], section[id]');
        let currentId = '';
        headings.forEach(heading => {
            const top = heading.offsetTop - 110;
            if (window.scrollY >= top) {
                currentId = heading.getAttribute('id');
            }
        });

        if (currentId) {
            document.querySelectorAll('.toc-on-page a').forEach(link => {
                link.style.color = '';
                link.style.borderLeftColor = 'transparent';
                if (link.getAttribute('href') === '#' + currentId) {
                    link.style.color = 'var(--arcane-cyan)';
                    link.style.borderLeftColor = 'var(--arcane-cyan)';
                }
            });
            const page = location.pathname.split('/').pop();
            document.querySelectorAll('.nav-items-list .nav-item-link').forEach(link => {
                const href = link.getAttribute('href') || '';
                const ici = href === '#' + currentId || href === page + '#' + currentId;
                link.classList.toggle('active', ici);
            });
        }
    };
    window.addEventListener('scroll', updateSpy, { passive: true });
    window.addEventListener('hashchange', updateSpy);
    updateSpy();
});

/* ==========================================================================
   7. FENÊTRE DÉTACHÉE — boutons [data-popout]
   La fiche PJ vit dans sa page autonome (fiche_pj.html, mêmes IDs, même
   moteur js/tools.js) : <button data-popout="fiche_pj.html"> l'ouvre dans
   une fenêtre popup centrée. Les deux fiches sont synchronisées en temps
   réel (BroadcastChannel + localStorage, même origine, dans les deux sens).
   ========================================================================== */
/* ==========================================================================
   8. TOOLTIP RULE-REF — niveau body (pas de rognage dans les tableaux).
   Le tooltip CSS (.rule-ref::after) est rogné par .table-container
   (overflow-x: auto). Ici on affiche un div fixe au niveau body, positionné
   depuis le rectangle de l'ancre, avec bascule haut/bas et cadrage viewport.
   ========================================================================== */
let codexTipEl = null;
function ensureCodexTip() {
    if (!codexTipEl) {
        codexTipEl = document.createElement('div');
        codexTipEl.className = 'codex-tip';
        codexTipEl.hidden = true;
        document.body.appendChild(codexTipEl);
        const hide = () => { codexTipEl.hidden = true; };
        window.addEventListener('scroll', hide, { capture: true, passive: true });
        window.addEventListener('resize', hide);
    }
    return codexTipEl;
}
// Attache les tooltips aux ancres .rule-ref (appelable après un rendu
// dynamique, ex. js/livre_1.js). Les ancres déjà liées sont ignorées.
function bindCodexTips(scope) {
    const root = scope || document;
    const refs = root.querySelectorAll('.rule-ref[data-tip]:not([data-tip-bound])');
    if (!refs.length) return;
    document.documentElement.classList.add('has-js-tip');
    const tip = ensureCodexTip();
    const place = (anchor) => {
        tip.textContent = anchor.getAttribute('data-tip');
        tip.hidden = false;
        const r = anchor.getBoundingClientRect();
        const w = Math.min(300, window.innerWidth - 16);
        tip.style.maxWidth = w + 'px';
        const h = tip.offsetHeight;
        let top = r.top - h - 10; // au-dessus par défaut
        if (top < 8) top = r.bottom + 10; // pas la place : en dessous
        let left = r.left + r.width / 2 - tip.offsetWidth / 2;
        left = Math.max(8, Math.min(left, window.innerWidth - tip.offsetWidth - 8));
        tip.style.top = Math.max(8, top) + 'px';
        tip.style.left = left + 'px';
    };
    const hide = () => { tip.hidden = true; };
    refs.forEach(a => {
        a.setAttribute('data-tip-bound', '1');
        a.addEventListener('mouseenter', () => {
            if (!window.matchMedia('(hover: hover)').matches) return;
            place(a);
        });
        a.addEventListener('mouseleave', hide);
        a.addEventListener('focus', () => place(a));
        a.addEventListener('blur', hide);
        a.addEventListener('click', hide);
    });
}
window.bindCodexTips = bindCodexTips;
document.addEventListener('DOMContentLoaded', () => {
    bindCodexTips(document);
});
document.addEventListener('click', (e) => {
    // Délégation (pas de binding figé au chargement) : les boutons vivent
    // dans du contenu rendu dynamiquement (livre_*.html squelettisés).
    const btn = e.target && e.target.closest ? e.target.closest('[data-popout]') : null;
    if (!btn || btn.disabled) return;
    e.preventDefault();
    const url = btn.getAttribute('data-popout');
    if (!url) return;
    const w = 1180, h = 940;
    const left = Math.max(0, (window.screenX || 0) + ((window.outerWidth || w) - w) / 2);
    const top = Math.max(0, (window.screenY || 0) + ((window.outerHeight || h) - h) / 2);
    const features = 'width=' + w + ',height=' + h + ',left=' + Math.round(left) + ',top=' + Math.round(top) +
        ',resizable=yes,scrollbars=yes,menubar=no,toolbar=no,location=no,status=no';
    const win = window.open(url, 'codex_popout_fiche', features);
    if (win) {
        try { win.focus(); } catch (err) { /* ignore */ }
    } else {
        window.open(url, '_blank'); // bloqueur de popups : repli nouvel onglet
    }
});
