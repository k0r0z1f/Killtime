/* ==========================================================================
   CODEX UNIVERSEL DU SYSTÈME RP — LOGIQUE INTERACTIVE V2.5
   Navigation, Accordéon, Recherche, Filtres, Thème & Scrollspy
   ========================================================================== */

document.addEventListener('DOMContentLoaded', () => {
    // 1. Gestion de l'accordéon dans la sidebar
    document.querySelectorAll('.nav-group-header').forEach(header => {
        header.addEventListener('click', () => {
            const group = header.parentElement;
            group.classList.toggle('expanded');
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
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
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

    // 5. Thème toggle (avec persistance via localStorage)
    const themeToggle = document.getElementById('themeToggle');
    const savedTheme = localStorage.getItem('codex-theme');
    if (savedTheme) {
        document.documentElement.setAttribute('data-theme', savedTheme);
    }

    if (themeToggle) {
        themeToggle.addEventListener('click', () => {
            const currentTheme = document.documentElement.getAttribute('data-theme');
            const newTheme = currentTheme === 'light' ? 'dark' : 'light';
            document.documentElement.setAttribute('data-theme', newTheme);
            localStorage.setItem('codex-theme', newTheme);
        });
    }

    // 6. Scrollspy automatique pour surligner les ancres actives
    window.addEventListener('scroll', () => {
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
        }
    }, { passive: true });
});
