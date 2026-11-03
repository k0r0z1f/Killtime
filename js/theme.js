/* ==========================================================================
   CODEX UNIVERSEL DU SYSTÈME RP — SÉLECTEUR & ÉDITEUR DE THÈMES
   - applique / persiste le thème selon l'utilisateur connecté (/api/prefs +
     stockage par compte) ou dans localStorage (« codex-theme ») si non connecté ;
   - propose 6 thèmes prédéfinis + 1 thème « Personnalisé » (custom) avec
     éditeur de palette en direct ;
   - transforme le bouton #themeToggle en menu multi-thèmes ;
   - injecte un bouton flottant sur les pages qui n'en ont pas ;
   - suit la préférence système tant que l'utilisateur n'a rien choisi.
   L'application initiale (anti-flash) est faite par js/theme-init.js.
   ========================================================================== */
(function () {
    'use strict';

    var PRESETS = window.CODEX_THEME_PRESETS || {
        dark:     { bgVoid: '#06080d', bgSurface: '#111827', textPrimary: '#f1f5f9', textSecondary: '#94a3b8', arcaneCyan: '#38bdf8', temporalAmber: '#f59e0b', vitalRuby: '#f43f5e', matrixEmerald: '#10b981', voidViolet: '#8b5cf6' },
        light:    { bgVoid: '#f8fafc', bgSurface: '#ffffff', textPrimary: '#0f172a', textSecondary: '#334155', arcaneCyan: '#0284c7', temporalAmber: '#d97706', vitalRuby: '#e11d48', matrixEmerald: '#059669', voidViolet: '#7c3aed' },
        sepia:    { bgVoid: '#f3ead5', bgSurface: '#fbf5e4', textPrimary: '#3a2a15', textSecondary: '#5b4527', arcaneCyan: '#0f766e', temporalAmber: '#b45309', vitalRuby: '#b91c1c', matrixEmerald: '#15803d', voidViolet: '#6d28d9' },
        night:    { bgVoid: '#030b1c', bgSurface: '#0b2144', textPrimary: '#e8f1ff', textSecondary: '#a9c1e8', arcaneCyan: '#60a5fa', temporalAmber: '#fbbf24', vitalRuby: '#fb7185', matrixEmerald: '#34d399', voidViolet: '#a78bfa' },
        emerald:  { bgVoid: '#03100a', bgSurface: '#0a2117', textPrimary: '#e8fff3', textSecondary: '#a7d8c2', arcaneCyan: '#34d399', temporalAmber: '#f59e0b', vitalRuby: '#fb7185', matrixEmerald: '#10b981', voidViolet: '#a78bfa' },
        contrast: { bgVoid: '#000000', bgSurface: '#0b0b0b', textPrimary: '#ffffff', textSecondary: '#e8e8e8', arcaneCyan: '#00e5ff', temporalAmber: '#ffff00', vitalRuby: '#ff5c5c', matrixEmerald: '#00ff9d', voidViolet: '#d8b4fe' }
    };

    var UTILS = window.CODEX_CUSTOM_THEME_UTILS;

    var THEMES = [
        { id: 'dark',     label: 'Sombre arcanique', shortLabel: 'Sombre',    swatch: ['#06080d', '#38bdf8'] },
        { id: 'light',    label: 'Clair',            shortLabel: 'Clair',     swatch: ['#f8fafc', '#0284c7'] },
        { id: 'sepia',    label: 'Sépia parchemin',  shortLabel: 'Sépia',     swatch: ['#f3ead5', '#9a3412'] },
        { id: 'night',    label: 'Nuit bleue',       shortLabel: 'Nuit',      swatch: ['#040a17', '#60a5fa'] },
        { id: 'emerald',  label: 'Émeraude matrice', shortLabel: 'Émeraude',  swatch: ['#03100a', '#34d399'] },
        { id: 'contrast', label: 'Contraste élevé',  shortLabel: 'Contraste', swatch: ['#000000', '#ffff00'] },
        { id: 'custom',   label: 'Personnalisé',     shortLabel: 'Perso',     swatch: ['#090d16', '#38bdf8'] }
    ];

    var CUSTOM_FIELDS = [
        { key: 'bgVoid',        label: 'Fond profond' },
        { key: 'bgSurface',     label: 'Panneaux' },
        { key: 'textPrimary',   label: 'Texte 1er' },
        { key: 'textSecondary', label: 'Texte 2nd' },
        { key: 'arcaneCyan',    label: 'Accent cyan' },
        { key: 'temporalAmber', label: 'Or & titres' },
        { key: 'vitalRuby',     label: 'Rubis alerte' },
        { key: 'matrixEmerald', label: 'Émeraude' },
        { key: 'voidViolet',    label: 'Violet vide' }
    ];

    var IDS = THEMES.map(function (t) { return t.id; });
    var KEY = 'codex-theme';
    var CUSTOM_KEY = 'codex-custom-theme';
    var PREFS_URL = '/api/prefs';
    var root = document.documentElement;
    var mql = window.matchMedia ? window.matchMedia('(prefers-color-scheme: light)') : null;

    var menu = null;
    var menuTrigger = null;
    var customPanel = null;
    var customToggleBtn = null;
    var noteEl = null;
    var remoteSaveTimer = null;

    /* ---------- Utilitaires repli si theme-init.js absent ---------- */
    function getActiveSessionUser() {
        if (UTILS && UTILS.getActiveSessionUser) return UTILS.getActiveSessionUser();
        try {
            if (window.CodexAuth && window.CodexAuth.currentUser && window.CodexAuth.currentUser.id) {
                return window.CodexAuth.currentUser;
            }
            var raw = localStorage.getItem('codex_active_session');
            if (!raw) return null;
            var parsed = JSON.parse(raw);
            return (parsed && parsed.id) ? parsed : null;
        } catch (e) {
            return null;
        }
    }

    function getActiveUserId() {
        var u = getActiveSessionUser();
        return u ? String(u.id) : null;
    }

    function themeKeyFor(uid) {
        return uid ? (KEY + ':user:' + uid) : KEY;
    }

    function customKeyFor(uid) {
        return uid ? (CUSTOM_KEY + ':user:' + uid) : CUSTOM_KEY;
    }

    function apiBase() {
        try {
            if (window.CODEX_API_BASE) return String(window.CODEX_API_BASE).replace(/\/+$/, '');
            if (window.location.protocol === 'file:') return 'http://localhost:3000';
            if (window.location.port === '8000') {
                return window.location.protocol + '//' + window.location.hostname + ':3000';
            }
            var h = window.location.hostname;
            if ((h === 'localhost' || h === '127.0.0.1') && window.location.port !== '3000') {
                return 'http://localhost:3000';
            }
        } catch (e) {}
        return '';
    }

    function byId(id) {
        for (var i = 0; i < THEMES.length; i++) {
            if (THEMES[i].id === id) return THEMES[i];
        }
        return null;
    }

    function sanitizeCustom(raw, fallbackPresetId) {
        if (UTILS && UTILS.sanitizeCustomTheme) {
            return UTILS.sanitizeCustomTheme(raw, fallbackPresetId);
        }
        var base = (fallbackPresetId && PRESETS[fallbackPresetId]) ? PRESETS[fallbackPresetId] : PRESETS.dark;
        var src = (raw && typeof raw === 'object') ? raw : {};
        return {
            bgVoid: src.bgVoid || base.bgVoid,
            bgSurface: src.bgSurface || base.bgSurface,
            textPrimary: src.textPrimary || base.textPrimary,
            textSecondary: src.textSecondary || base.textSecondary,
            arcaneCyan: src.arcaneCyan || base.arcaneCyan,
            temporalAmber: src.temporalAmber || base.temporalAmber,
            vitalRuby: src.vitalRuby || base.vitalRuby,
            matrixEmerald: src.matrixEmerald || base.matrixEmerald,
            voidViolet: src.voidViolet || base.voidViolet
        };
    }

    function readSaved(explicitUid) {
        var uid = (explicitUid !== undefined) ? explicitUid : getActiveUserId();
        if (UTILS && UTILS.readSavedTheme) {
            return UTILS.readSavedTheme(uid);
        }
        var saved = null;
        try {
            saved = localStorage.getItem(themeKeyFor(uid));
        } catch (e) {
            return null;
        }
        if (saved === 'auto' || saved === 'system') return null;
        return IDS.indexOf(saved) !== -1 ? saved : null;
    }

    function readCustom(explicitUid) {
        var uid = (explicitUid !== undefined) ? explicitUid : getActiveUserId();
        if (UTILS && UTILS.readSavedCustomTheme) {
            return UTILS.readSavedCustomTheme(uid);
        }
        try {
            var str = localStorage.getItem(customKeyFor(uid));
            if (str) return sanitizeCustom(JSON.parse(str));
        } catch (e) {}
        return sanitizeCustom(null);
    }

    function syncSessionAndUserStorePrefs(uid, patch) {
        if (!uid || !patch) return;
        try {
            var raw = localStorage.getItem('codex_active_session');
            if (raw) {
                var sessionUser = JSON.parse(raw);
                if (sessionUser && sessionUser.id === uid) {
                    sessionUser.prefs = Object.assign({}, sessionUser.prefs || {}, patch);
                    localStorage.setItem('codex_active_session', JSON.stringify(sessionUser));
                }
            }
            if (window.CodexAuth && window.CodexAuth.currentUser && window.CodexAuth.currentUser.id === uid) {
                window.CodexAuth.currentUser.prefs = Object.assign({}, window.CodexAuth.currentUser.prefs || {}, patch);
            }
            if (window.CodexUserStore && typeof window.CodexUserStore.updateUserPrefs === 'function') {
                window.CodexUserStore.updateUserPrefs(uid, patch);
            }
        } catch (e) {}
    }

    function pushRemotePrefs(patch, debounceMs) {
        var uid = getActiveUserId();
        if (!uid) return;
        if (remoteSaveTimer) {
            clearTimeout(remoteSaveTimer);
            remoteSaveTimer = null;
        }
        var send = function () {
            remoteSaveTimer = null;
            try {
                if (typeof fetch !== 'function') return;
                fetch(apiBase() + PREFS_URL, {
                    method: 'PUT',
                    credentials: 'include',
                    headers: {
                        'Content-Type': 'application/json',
                        'ngrok-skip-browser-warning': 'true'
                    },
                    body: JSON.stringify({ prefs: patch })
                }).catch(function () {});
            } catch (e) {}
        };
        if (debounceMs && debounceMs > 0) {
            remoteSaveTimer = setTimeout(send, debounceMs);
        } else {
            send();
        }
    }

    function writeSaved(id) {
        var uid = getActiveUserId();
        try {
            localStorage.setItem(themeKeyFor(uid), id);
        } catch (e) {
            /* mode privé / stockage bloqué : le thème reste appliqué pour la page. */
        }
        if (uid) {
            syncSessionAndUserStorePrefs(uid, { theme: id });
            pushRemotePrefs({ theme: id }, 0);
        }
    }

    function writeCustom(customObj, debounceMs) {
        var uid = getActiveUserId();
        var clean = sanitizeCustom(customObj);
        try {
            localStorage.setItem(customKeyFor(uid), JSON.stringify(clean));
        } catch (e) {}
        if (uid) {
            syncSessionAndUserStorePrefs(uid, { theme: current(), customTheme: clean });
            pushRemotePrefs({ theme: current(), customTheme: clean }, debounceMs || 0);
        }
        return clean;
    }

    function systemTheme() {
        return mql && mql.matches ? 'light' : 'dark';
    }

    function current() {
        var id = root.getAttribute('data-theme');
        return IDS.indexOf(id) !== -1 ? id : 'dark';
    }

    function updateCustomSwatch() {
        var c = readCustom();
        var customEntry = byId('custom');
        if (customEntry) {
            customEntry.swatch = [c.bgVoid, c.arcaneCyan];
        }
        if (menu) {
            var sw = menu.querySelector('[data-theme-option="custom"] .theme-swatch');
            if (sw) {
                sw.style.background = 'linear-gradient(135deg, ' + c.bgVoid + ' 0 48%, ' + c.arcaneCyan + ' 48% 100%)';
            }
        }
    }

    function updateTrigger(btn, id) {
        var theme = byId(id) || THEMES[0];
        var label = 'Thème : ' + theme.label + ' — changer';
        btn.title = label;
        btn.setAttribute('aria-label', label);
    }

    function updateNoteText() {
        if (!noteEl) return;
        var user = getActiveSessionUser();
        if (user && user.id) {
            var name = user.username || user.email || 'compte actif';
            noteEl.textContent = 'Connecté (' + name + ') : thème lié à votre compte';
        } else {
            noteEl.textContent = 'Non connecté : préférence enregistrée sur cet appareil (localStorage)';
        }
    }

    function syncCustomPanelInputs() {
        if (!customPanel) return;
        var c = readCustom();
        for (var i = 0; i < CUSTOM_FIELDS.length; i++) {
            var key = CUSTOM_FIELDS[i].key;
            var val = c[key] || '#000000';
            var input = customPanel.querySelector('input[data-custom-color="' + key + '"]');
            var hexSpan = customPanel.querySelector('[data-custom-hex="' + key + '"]');
            if (input && input.value.toLowerCase() !== val.toLowerCase()) {
                input.value = val;
            }
            if (hexSpan) {
                hexSpan.textContent = val;
            }
        }
    }

    function syncUI() {
        var id = current();
        updateCustomSwatch();
        var triggers = document.querySelectorAll('#themeToggle');
        for (var i = 0; i < triggers.length; i++) updateTrigger(triggers[i], id);
        if (menu) {
            var options = menu.querySelectorAll('[data-theme-option]');
            for (var j = 0; j < options.length; j++) {
                options[j].setAttribute('aria-checked', options[j].getAttribute('data-theme-option') === id ? 'true' : 'false');
            }
            updateNoteText();
            syncCustomPanelInputs();
        }
    }

    function applyDomTheme(id, customPalette) {
        root.setAttribute('data-theme', id);
        if (id === 'custom') {
            var palette = customPalette ? sanitizeCustom(customPalette) : readCustom();
            if (UTILS && UTILS.applyCustomVars) {
                UTILS.applyCustomVars(root, palette);
            }
        } else {
            if (UTILS && UTILS.clearCustomVars) {
                UTILS.clearCustomVars(root);
            }
        }
    }

    function apply(id, persist) {
        if (IDS.indexOf(id) === -1) return false;
        applyDomTheme(id);
        if (persist) writeSaved(id);
        syncUI();
        try {
            document.dispatchEvent(new CustomEvent('codex:theme-change', {
                detail: {
                    theme: id,
                    customTheme: id === 'custom' ? readCustom() : null,
                    scope: getActiveUserId() ? 'user' : 'local'
                }
            }));
        } catch (e) {
            /* navigateur sans CustomEvent : pas bloquant. */
        }
        return true;
    }

    function setCustom(patch, applyNow, debounceMs) {
        var currentCustom = readCustom();
        var merged = sanitizeCustom(Object.assign({}, currentCustom, patch || {}));
        if (applyNow !== false) {
            applyDomTheme('custom', merged);
            var uid = getActiveUserId();
            try {
                localStorage.setItem(themeKeyFor(uid), 'custom');
            } catch (e) {}
        } else if (current() === 'custom') {
            applyDomTheme('custom', merged);
        }
        writeCustom(merged, debounceMs || 0);
        syncUI();
        try {
            document.dispatchEvent(new CustomEvent('codex:theme-change', {
                detail: {
                    theme: current(),
                    customTheme: merged,
                    scope: getActiveUserId() ? 'user' : 'local'
                }
            }));
        } catch (e) {}
        return merged;
    }

    function resetCustom() {
        var def = sanitizeCustom(UTILS ? UTILS.DEFAULT_CUSTOM : PRESETS.dark);
        return setCustom(def, current() === 'custom', 0);
    }

    function toggleCustomPanel(forceOpen) {
        if (!customPanel || !customToggleBtn) return;
        var nextOpen = (typeof forceOpen === 'boolean') ? forceOpen : customPanel.hidden;
        customPanel.hidden = !nextOpen;
        customToggleBtn.setAttribute('aria-expanded', nextOpen ? 'true' : 'false');
        var chevron = customToggleBtn.querySelector('.theme-custom-chevron');
        if (chevron) chevron.textContent = nextOpen ? '▴' : '▾';
        if (nextOpen) syncCustomPanelInputs();
        if (menuTrigger && menu && !menu.hidden) positionMenu(menuTrigger);
    }

    function buildCustomPanel() {
        var panel = document.createElement('div');
        panel.className = 'theme-custom-panel';
        panel.id = 'themeCustomPanel';
        panel.hidden = true;

        var presetsWrap = document.createElement('div');
        presetsWrap.className = 'theme-custom-presets';
        var presetsTitle = document.createElement('div');
        presetsTitle.className = 'theme-custom-presets-label';
        presetsTitle.textContent = 'Base à copier :';
        presetsWrap.appendChild(presetsTitle);

        THEMES.forEach(function (t) {
            if (t.id === 'custom' || !PRESETS[t.id]) return;
            var chip = document.createElement('button');
            chip.type = 'button';
            chip.className = 'theme-preset-chip';
            chip.setAttribute('data-preset-copy', t.id);
            chip.textContent = t.shortLabel || t.label;
            chip.title = 'Initialiser le thème personnalisé depuis « ' + t.label + ' »';
            chip.addEventListener('click', function (e) {
                e.stopPropagation();
                var base = Object.assign({ basePreset: t.id }, PRESETS[t.id]);
                setCustom(base, true, 0);
            });
            presetsWrap.appendChild(chip);
        });
        panel.appendChild(presetsWrap);

        var grid = document.createElement('div');
        grid.className = 'theme-custom-grid';

        CUSTOM_FIELDS.forEach(function (field) {
            var labelWrap = document.createElement('label');
            labelWrap.className = 'theme-color-field';

            var input = document.createElement('input');
            input.type = 'color';
            input.className = 'theme-color-input';
            input.setAttribute('data-custom-color', field.key);
            input.setAttribute('aria-label', field.label);

            var meta = document.createElement('span');
            meta.className = 'theme-color-meta';

            var lbl = document.createElement('span');
            lbl.className = 'theme-color-label';
            lbl.textContent = field.label;

            var hex = document.createElement('span');
            hex.className = 'theme-color-hex';
            hex.setAttribute('data-custom-hex', field.key);
            hex.textContent = '#000000';

            input.addEventListener('input', function () {
                var patch = {};
                patch[field.key] = input.value;
                setCustom(patch, true, 250);
            });
            input.addEventListener('change', function () {
                var patch = {};
                patch[field.key] = input.value;
                setCustom(patch, true, 0);
            });

            meta.appendChild(lbl);
            meta.appendChild(hex);
            labelWrap.appendChild(input);
            labelWrap.appendChild(meta);
            grid.appendChild(labelWrap);
        });

        panel.appendChild(grid);

        var actions = document.createElement('div');
        actions.className = 'theme-custom-actions';

        var resetBtn = document.createElement('button');
        resetBtn.type = 'button';
        resetBtn.className = 'theme-custom-btn';
        resetBtn.id = 'themeCustomReset';
        resetBtn.textContent = 'Réinitialiser';
        resetBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            resetCustom();
        });

        var applyBtn = document.createElement('button');
        applyBtn.type = 'button';
        applyBtn.className = 'theme-custom-btn';
        applyBtn.id = 'themeCustomApply';
        applyBtn.textContent = 'Activer ce thème';
        applyBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            apply('custom', true);
        });

        actions.appendChild(resetBtn);
        actions.appendChild(applyBtn);
        panel.appendChild(actions);

        return panel;
    }

    function buildMenu() {
        if (menu) return menu;

        menu = document.createElement('div');
        menu.className = 'theme-menu';
        menu.id = 'themeMenu';
        menu.setAttribute('role', 'menu');
        menu.setAttribute('aria-label', 'Choisir un thème');
        menu.hidden = true;

        updateCustomSwatch();

        THEMES.forEach(function (theme) {
            var option = document.createElement('button');
            option.type = 'button';
            option.className = 'theme-option';
            option.setAttribute('role', 'menuitemradio');
            option.setAttribute('data-theme-option', theme.id);
            option.setAttribute('aria-checked', 'false');
            option.tabIndex = -1;

            var swatch = document.createElement('span');
            swatch.className = 'theme-swatch';
            swatch.setAttribute('aria-hidden', 'true');
            swatch.style.background = 'linear-gradient(135deg, ' + theme.swatch[0] + ' 0 48%, ' + theme.swatch[1] + ' 48% 100%)';

            var label = document.createElement('span');
            label.textContent = theme.label;

            var check = document.createElement('span');
            check.className = 'theme-check';
            check.setAttribute('aria-hidden', 'true');
            check.textContent = '✓';

            option.appendChild(swatch);
            option.appendChild(label);
            option.appendChild(check);
            menu.appendChild(option);
        });

        customToggleBtn = document.createElement('button');
        customToggleBtn.type = 'button';
        customToggleBtn.className = 'theme-custom-toggle';
        customToggleBtn.id = 'themeCustomToggle';
        customToggleBtn.setAttribute('aria-expanded', 'false');
        customToggleBtn.setAttribute('aria-controls', 'themeCustomPanel');
        customToggleBtn.innerHTML = '<span>🎨 Configurer mon thème…</span><span class="theme-custom-chevron" aria-hidden="true">▾</span>';
        customToggleBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            toggleCustomPanel();
        });
        menu.appendChild(customToggleBtn);

        customPanel = buildCustomPanel();
        menu.appendChild(customPanel);

        var separator = document.createElement('div');
        separator.className = 'theme-separator';
        menu.appendChild(separator);

        noteEl = document.createElement('div');
        noteEl.className = 'theme-menu-note';
        noteEl.id = 'themeMenuNote';
        updateNoteText();
        menu.appendChild(noteEl);

        // Voile photosensible : réinitialiser le choix mémorisé, si le
        // module est chargé sur cette page (js/secret-veil.js).
        if (window.KTSecretVeil && typeof window.KTSecretVeil.reset === 'function') {
            var veilSep = document.createElement('div');
            veilSep.className = 'theme-separator';
            menu.appendChild(veilSep);

            var veilReset = document.createElement('button');
            veilReset.type = 'button';
            veilReset.className = 'theme-option';
            veilReset.setAttribute('role', 'menuitem');

            var veilLabel = document.createElement('span');
            veilLabel.textContent = 'Voile photosensible : réinitialiser mon choix';

            veilReset.appendChild(veilLabel);
            veilReset.addEventListener('click', function () {
                window.KTSecretVeil.reset();
                closeMenu();
                if (menuTrigger) menuTrigger.focus();
            });
            menu.appendChild(veilReset);
        }

        menu.addEventListener('click', function (event) {
            var option = event.target.closest('[data-theme-option]');
            if (!option) return;
            var chosen = option.getAttribute('data-theme-option');
            var trigger = menuTrigger;
            apply(chosen, true);
            if (chosen === 'custom' && customPanel && customPanel.hidden) {
                toggleCustomPanel(true);
                return;
            }
            closeMenu();
            if (trigger) trigger.focus();
        });

        menu.addEventListener('keydown', function (event) {
            var options = Array.prototype.slice.call(menu.querySelectorAll('[data-theme-option]'));
            var index = options.indexOf(document.activeElement);
            if (index === -1) return;
            var next = null;
            if (event.key === 'ArrowDown' || event.key === 'ArrowRight') next = options[(index + 1) % options.length];
            else if (event.key === 'ArrowUp' || event.key === 'ArrowLeft') next = options[(index - 1 + options.length) % options.length];
            else if (event.key === 'Home') next = options[0];
            else if (event.key === 'End') next = options[options.length - 1];
            if (next) {
                event.preventDefault();
                next.focus();
            }
        });

        document.body.appendChild(menu);
        syncUI();
        return menu;
    }

    function positionMenu(trigger) {
        if (!trigger || !menu) return;
        var rect = trigger.getBoundingClientRect();
        var gap = 8;
        var width = menu.offsetWidth || 248;
        var height = menu.offsetHeight || 320;
        var left = rect.right - width;
        var top = rect.bottom + gap;

        left = Math.max(8, Math.min(left, window.innerWidth - width - 8));
        if (top + height > window.innerHeight - 8) top = Math.max(8, rect.top - height - gap);

        menu.style.left = Math.round(left) + 'px';
        menu.style.top = Math.round(top) + 'px';
    }

    function openMenu(trigger) {
        buildMenu();
        menuTrigger = trigger;
        menu.hidden = false;
        trigger.setAttribute('aria-expanded', 'true');
        syncUI();
        positionMenu(trigger);

        var checked = menu.querySelector('[data-theme-option][aria-checked="true"]') || menu.querySelector('[data-theme-option]');
        if (checked) {
            window.requestAnimationFrame(function () {
                checked.focus();
            });
        }
    }

    function closeMenu() {
        if (!menu || menu.hidden) return;
        menu.hidden = true;
        if (menuTrigger) {
            menuTrigger.setAttribute('aria-expanded', 'false');
            menuTrigger = null;
        }
    }

    function bindTrigger(trigger) {
        if (trigger.getAttribute('data-theme-bound') === '1') return;
        trigger.setAttribute('data-theme-bound', '1');
        trigger.setAttribute('aria-haspopup', 'menu');
        trigger.setAttribute('aria-expanded', 'false');

        trigger.addEventListener('click', function (event) {
            event.preventDefault();
            event.stopPropagation();
            if (menu && !menu.hidden && menuTrigger === trigger) closeMenu();
            else openMenu(trigger);
        });

        trigger.addEventListener('keydown', function (event) {
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault();
                openMenu(trigger);
            }
        });
    }

    function makeTriggerButton(className) {
        var trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.id = 'themeToggle';
        trigger.className = className;
        trigger.innerHTML = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">' +
            '<circle cx="12" cy="12" r="9"></circle>' +
            '<path d="M12 3a9 9 0 0 0 0 18z" fill="currentColor" stroke="none"></path>' +
            '<path d="M12 8v8M8 12h8"></path>' +
            '</svg>';
        return trigger;
    }

    function mountMissingButton() {
        var host = document.querySelector('.topbar-right') || document.querySelector('.controls-top');
        var trigger;
        if (host) {
            trigger = makeTriggerButton('theme-btn theme-toggle-inline');
            host.appendChild(trigger);
        } else if (document.body) {
            trigger = makeTriggerButton('theme-fab');
            document.body.appendChild(trigger);
        } else {
            return null;
        }
        bindTrigger(trigger);
        updateTrigger(trigger, current());
        return trigger;
    }

    /* ---------- Synchronisation utilisateur connecté / serveur ---------- */
    function syncFromUserAccount(loginUser) {
        var uid = (loginUser && loginUser.id) ? String(loginUser.id) : getActiveUserId();
        if (!uid) return;

        // 1. Si l'objet user de login/session embarque déjà prefs, les appliquer en local
        var sessionUser = loginUser || getActiveSessionUser();
        var embeddedPrefs = (sessionUser && sessionUser.prefs && typeof sessionUser.prefs === 'object') ? sessionUser.prefs : null;
        if (embeddedPrefs) {
            if (embeddedPrefs.customTheme && typeof embeddedPrefs.customTheme === 'object') {
                try {
                    localStorage.setItem(customKeyFor(uid), JSON.stringify(sanitizeCustom(embeddedPrefs.customTheme)));
                } catch (e) {}
            }
            if (embeddedPrefs.theme && IDS.indexOf(embeddedPrefs.theme) !== -1) {
                try {
                    localStorage.setItem(themeKeyFor(uid), embeddedPrefs.theme);
                } catch (e2) {}
            }
        }

        // 2. Appliquer ce qu'on connaît déjà localement pour cet utilisateur
        var localUserTheme = readSaved(uid);
        if (localUserTheme) {
            applyDomTheme(localUserTheme);
            syncUI();
        } else {
            // Première connexion sans préférence enregistrée sur ce compte :
            // si un choix existait en non-connecté, l'adopter pour ce compte.
            var anonTheme = readSaved(null);
            var anonCustom = null;
            try {
                var rawAnonCustom = localStorage.getItem(CUSTOM_KEY);
                if (rawAnonCustom) anonCustom = sanitizeCustom(JSON.parse(rawAnonCustom));
            } catch (e3) {}
            if (anonCustom) {
                try { localStorage.setItem(customKeyFor(uid), JSON.stringify(anonCustom)); } catch (e4) {}
            }
            if (anonTheme) {
                try { localStorage.setItem(themeKeyFor(uid), anonTheme); } catch (e5) {}
                syncSessionAndUserStorePrefs(uid, {
                    theme: anonTheme,
                    customTheme: anonCustom || readCustom(uid)
                });
                applyDomTheme(anonTheme);
                syncUI();
            }
        }

        // 3. Interroger GET /api/prefs sur le serveur s'il est disponible
        try {
            if (typeof fetch !== 'function') return;
            fetch(apiBase() + PREFS_URL, {
                credentials: 'include',
                cache: 'no-store',
                headers: { 'ngrok-skip-browser-warning': 'true' }
            }).then(function (res) {
                return res.ok ? res.json() : null;
            }).then(function (data) {
                if (!data || !data.prefs || getActiveUserId() !== uid) return;
                var p = data.prefs;
                var hasServerCustom = p.customTheme && typeof p.customTheme === 'object';
                var hasServerTheme = p.theme && IDS.indexOf(p.theme) !== -1;

                if (hasServerCustom) {
                    var cleanCt = sanitizeCustom(p.customTheme);
                    try { localStorage.setItem(customKeyFor(uid), JSON.stringify(cleanCt)); } catch (e) {}
                    syncSessionAndUserStorePrefs(uid, { customTheme: cleanCt });
                }
                if (hasServerTheme) {
                    try { localStorage.setItem(themeKeyFor(uid), p.theme); } catch (e2) {}
                    syncSessionAndUserStorePrefs(uid, { theme: p.theme });
                    applyDomTheme(p.theme);
                    syncUI();
                } else {
                    // Le serveur n'avait pas encore de thème pour cet utilisateur :
                    // pousser le choix local du compte s'il existe.
                    var curUserTheme = readSaved(uid);
                    if (curUserTheme) {
                        pushRemotePrefs({
                            theme: curUserTheme,
                            customTheme: curUserTheme === 'custom' ? readCustom(uid) : undefined
                        }, 0);
                    }
                }
            }).catch(function () {});
        } catch (e) {}
    }

    function onLogout() {
        var anonTheme = readSaved(null) || systemTheme();
        applyDomTheme(anonTheme);
        syncUI();
    }

    function init() {
        var triggers = document.querySelectorAll('#themeToggle');
        if (triggers.length) {
            for (var i = 0; i < triggers.length; i++) bindTrigger(triggers[i]);
        } else {
            mountMissingButton();
        }

        // S'assurer que les variables custom sont bien posées si data-theme="custom"
        if (current() === 'custom') {
            applyDomTheme('custom');
        }
        syncUI();

        if (getActiveUserId()) {
            syncFromUserAccount();
        }

        window.addEventListener('codex:auth:login', function (event) {
            var u = event && event.detail && event.detail.user ? event.detail.user : null;
            syncFromUserAccount(u);
        });

        window.addEventListener('codex:auth:logout', function () {
            onLogout();
        });

        document.addEventListener('click', function (event) {
            if (!menu || menu.hidden) return;
            if (event.target.closest('.theme-menu') || event.target.closest('#themeToggle')) return;
            closeMenu();
        });

        document.addEventListener('keydown', function (event) {
            if (event.key !== 'Escape' || !menu || menu.hidden) return;
            var trigger = menuTrigger;
            closeMenu();
            if (trigger) trigger.focus();
        });

        window.addEventListener('resize', closeMenu, { passive: true });
        window.addEventListener('scroll', function (event) {
            if (!menu || menu.hidden) return;
            if (event && event.target && menu.contains && menu.contains(event.target)) return;
            closeMenu();
        }, { passive: true, capture: true });

        window.addEventListener('storage', function (event) {
            var uid = getActiveUserId();
            var activeThemeKey = themeKeyFor(uid);
            var activeCustomKey = customKeyFor(uid);

            if (event.key === activeThemeKey || (!uid && event.key === KEY)) {
                if (!event.newValue || event.newValue === 'auto' || event.newValue === 'system') {
                    apply(systemTheme(), false);
                } else if (IDS.indexOf(event.newValue) !== -1) {
                    apply(event.newValue, false);
                }
            } else if (event.key === activeCustomKey || (!uid && event.key === CUSTOM_KEY)) {
                if (current() === 'custom') {
                    applyDomTheme('custom');
                }
                syncUI();
            } else if (event.key === 'codex_active_session') {
                var newUid = getActiveUserId();
                var nextTheme = readSaved(newUid) || systemTheme();
                applyDomTheme(nextTheme);
                syncUI();
            }
        });

        if (mql) {
            var onSystemChange = function () {
                if (!readSaved()) apply(systemTheme(), false);
            };
            if (mql.addEventListener) mql.addEventListener('change', onSystemChange);
            else if (mql.addListener) mql.addListener(onSystemChange);
        }
    }

    window.CodexTheme = {
        themes: IDS.slice(),
        list: THEMES.map(function (t) { return { id: t.id, label: t.label }; }),
        presets: JSON.parse(JSON.stringify(PRESETS)),
        current: current,
        apply: function (id) { return apply(id, true); },
        getCustom: function () { return readCustom(); },
        setCustom: function (patch, applyNow) { return setCustom(patch, applyNow !== false, 0); },
        resetCustom: resetCustom,
        storageScope: function () { return getActiveUserId() ? 'user' : 'local'; }
    };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
