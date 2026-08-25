/**
 * ============================================================================
 * KILLTIME UNIVERSE - REAL-TIME SYNCHRONIZED THEME & DEV SANCTUM ENGINE
 * ============================================================================
 * Supports persistent Dark / Light mode across all pages, zero-FOUC initial
 * load, real-time cross-tab sync via Storage & BroadcastChannel APIs,
 * universal floating + inline toggle widgets, AND the live floating Dev Sanctum
 * Task Engine side-drawer.
 */

(function () {
    'use strict';

    const THEME_STORAGE_KEY = 'killtime_theme';
    const THEME_CHANNEL_NAME = 'killtime_theme_sync';
    const TASK_STORAGE_KEY = 'hybris_task_engine';
    const TASK_CHANNEL_NAME = 'hybris_task_engine_sync';
    const DEFAULT_THEME = 'dark'; // Dark sci-fi aesthetic as default

    // 1. Resolve theme.css relative to this script
    function ensureThemeStylesheet() {
        if (document.querySelector('link[data-killtime-theme-css]')) {
            return;
        }
        try {
            const currentScript = document.currentScript;
            let cssUrl = 'theme.css';
            if (currentScript && currentScript.src) {
                cssUrl = new URL('theme.css', currentScript.src).href;
            } else {
                const isKilltimeSub = window.location.pathname.includes('/Killtime/');
                cssUrl = isKilltimeSub ? '/Killtime/theme.css' : '/theme.css';
            }
            const link = document.createElement('link');
            link.rel = 'stylesheet';
            link.href = cssUrl;
            link.setAttribute('data-killtime-theme-css', 'true');
            if (document.head) {
                document.head.appendChild(link);
            } else {
                document.addEventListener('DOMContentLoaded', () => document.head.appendChild(link));
            }
        } catch (e) {
            console.warn('[ThemeEngine] Could not auto-resolve stylesheet path:', e);
        }
    }

    // 2. Get stored or preferred theme
    function getSavedTheme() {
        try {
            const saved = localStorage.getItem(THEME_STORAGE_KEY);
            if (saved === 'dark' || saved === 'light') {
                return saved;
            }
        } catch (e) { }
        return DEFAULT_THEME;
    }

    // SVG Icons
    const MOON_ICON = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"></path>
    </svg>`;

    const SUN_ICON = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <circle cx="12" cy="12" r="5"></circle>
        <line x1="12" y1="1" x2="12" y2="3"></line>
        <line x1="12" y1="21" x2="12" y2="23"></line>
        <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"></line>
        <line x1="18.36" y1="18.36" x2="19.78" y2="19.78"></line>
        <line x1="1" y1="12" x2="3" y2="12"></line>
        <line x1="21" y1="12" x2="23" y2="12"></line>
        <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"></line>
        <line x1="18.36" y1="5.64" x2="19.78" y2="4.22"></line>
    </svg>`;

    const LIGHTNING_ICON = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>
    </svg>`;

    // 3. Apply theme to DOM immediately
    function applyTheme(theme, shouldBroadcast) {
        const root = document.documentElement;
        const body = document.body;

        root.setAttribute('data-theme', theme);
        root.classList.remove('dark-theme', 'light-theme');
        root.classList.add(theme === 'dark' ? 'dark-theme' : 'light-theme');

        if (body) {
            body.setAttribute('data-theme', theme);
            body.classList.remove('dark-theme', 'light-theme');
            body.classList.add(theme === 'dark' ? 'dark-theme' : 'light-theme');
        }

        // Update Floating Button & Header Buttons
        updateToggleButtons(theme);

        if (shouldBroadcast) {
            try {
                localStorage.setItem(THEME_STORAGE_KEY, theme);
            } catch (e) { }

            // Broadcast to other tabs
            if (typeof BroadcastChannel !== 'undefined') {
                try {
                    const bc = new BroadcastChannel(THEME_CHANNEL_NAME);
                    bc.postMessage({ theme: theme });
                    bc.close();
                } catch (e) { }
            }
        }

        // Trigger custom event
        window.dispatchEvent(new CustomEvent('killtime-theme-change', { detail: { theme } }));
    }

    // 4. Update Toggle Buttons UI
    function updateToggleButtons(theme) {
        const isDark = theme === 'dark';
        const floatingBtn = document.getElementById('theme-switch-floating-btn');
        if (floatingBtn) {
            floatingBtn.innerHTML = isDark ? SUN_ICON : MOON_ICON;
            floatingBtn.setAttribute('data-tooltip', isDark ? 'Switch to Light Mode' : 'Switch to Dark Mode');
            floatingBtn.setAttribute('aria-label', isDark ? 'Switch to Light Mode' : 'Switch to Dark Mode');
            floatingBtn.title = isDark ? 'Switch to Light Mode' : 'Switch to Dark Mode';
        }

        // Header / inline buttons
        const headerButtons = document.querySelectorAll('.theme-toggle, .theme-toggle-header-btn');
        headerButtons.forEach(btn => {
            const iconSpan = btn.querySelector('.theme-icon');
            const textSpan = btn.querySelector('.theme-label');
            if (iconSpan) {
                iconSpan.innerHTML = isDark ? SUN_ICON : MOON_ICON;
            }
            if (textSpan) {
                textSpan.textContent = isDark ? 'Light' : 'Dark';
            }
        });
    }

    // 5. Toggle Theme function (Global API)
    window.toggleKilltimeTheme = function () {
        const currentTheme = document.documentElement.getAttribute('data-theme') || getSavedTheme();
        const nextTheme = currentTheme === 'dark' ? 'light' : 'dark';
        applyTheme(nextTheme, true);
    };

    // 6. Setup cross-tab theme listeners
    window.addEventListener('storage', function (e) {
        if (e.key === THEME_STORAGE_KEY && e.newValue) {
            applyTheme(e.newValue, false);
        } else if (e.key === TASK_STORAGE_KEY) {
            renderSanctumTasks();
        }
    });

    if (typeof BroadcastChannel !== 'undefined') {
        try {
            const syncChannel = new BroadcastChannel(THEME_CHANNEL_NAME);
            syncChannel.onmessage = function (e) {
                if (e.data && e.data.theme) {
                    applyTheme(e.data.theme, false);
                }
            };
        } catch (e) { }

        try {
            const taskChannel = new BroadcastChannel(TASK_CHANNEL_NAME);
            taskChannel.onmessage = function (e) {
                if (e.data && e.data.type === 'task_update') {
                    renderSanctumTasks();
                }
            };
        } catch (e) { }
    }

    // =========================================================================
    // DEV SANCTUM FLOATING SIDE-DRAWER ENGINE
    // =========================================================================

    const SEED_TASKS = [
        {
            id: 'task_1',
            title: 'RC1 / Draft Synthesis',
            description: 'Extract & Format Chapter 16+ from Killtime_compressed.txt. Inject Trackboard matrices, Actor Icons, and Temporal Anchors.',
            status: 'friction',
            priority: 'critical'
        },
        {
            id: 'task_2',
            title: 'Cryptographic Labyrinth: Timeline-B',
            description: 'Implement a JS script replacing [LOCKED] tag with a blinking terminal requiring biometric variance (0.0001) input.',
            status: 'synthesis',
            priority: 'critical'
        },
        {
            id: 'task_3',
            title: 'The 4th Wall Breach: Text Selection',
            description: 'Engineer event listener on "Architect" or "Author" text. Upon highlight, CSS blackouts and displays: "I can feel you reading this. What kind of god does that make you?"',
            status: 'synthesis',
            priority: 'terminal'
        },
        {
            id: 'task_4',
            title: 'The Bleeding Pitch (GoFundMe)',
            description: 'Replace corporate semantics ("donations") with "critical resource injections". Transform rewards into "Classified Relics".',
            status: 'synthesis',
            priority: 'terminal'
        },
        {
            id: 'task_5',
            title: 'Architect Task Engine Deployment',
            description: 'Build a standalone JSON-backed task tracker for the Dev Sanctum. Ensure aesthetic alignment with the Labyrinth of Timelines.',
            status: 'zero',
            priority: 'baseline'
        }
    ];

    let sanctumActiveTab = 'all';

    function getSanctumTasks() {
        try {
            const stored = localStorage.getItem(TASK_STORAGE_KEY);
            if (stored) {
                const parsed = JSON.parse(stored);
                if (Array.isArray(parsed)) return parsed;
            }
        } catch (e) { }
        return SEED_TASKS;
    }

    function saveSanctumTasks(tasks) {
        try {
            localStorage.setItem(TASK_STORAGE_KEY, JSON.stringify(tasks));
            if (typeof BroadcastChannel !== 'undefined') {
                try {
                    const bc = new BroadcastChannel(TASK_CHANNEL_NAME);
                    bc.postMessage({ type: 'task_update' });
                    bc.close();
                } catch (e) { }
            }
        } catch (e) { }
    }

    function toggleSanctumDrawer() {
        const drawer = document.getElementById('dev-sanctum-drawer');
        const backdrop = document.getElementById('dev-sanctum-backdrop');
        if (!drawer || !backdrop) return;

        const isOpen = drawer.classList.contains('open');
        if (isOpen) {
            drawer.classList.remove('open');
            backdrop.classList.remove('open');
            drawer.setAttribute('aria-hidden', 'true');
        } else {
            drawer.classList.add('open');
            backdrop.classList.add('open');
            drawer.setAttribute('aria-hidden', 'false');
            renderSanctumTasks();
        }
    }

    window.toggleDevSanctum = toggleSanctumDrawer;

    function renderSanctumTasks() {
        const container = document.getElementById('sanctum-task-container');
        if (!container) return;

        const tasks = getSanctumTasks();

        // Update counts
        const countAll = tasks.length;
        const countSynthesis = tasks.filter(t => t.status === 'synthesis').length;
        const countFriction = tasks.filter(t => t.status === 'friction').length;
        const countZero = tasks.filter(t => t.status === 'zero').length;

        const elCountAll = document.getElementById('sanctum-count-all');
        const elCountSynthesis = document.getElementById('sanctum-count-synthesis');
        const elCountFriction = document.getElementById('sanctum-count-friction');
        const elCountZero = document.getElementById('sanctum-count-zero');

        if (elCountAll) elCountAll.textContent = countAll;
        if (elCountSynthesis) elCountSynthesis.textContent = countSynthesis;
        if (elCountFriction) elCountFriction.textContent = countFriction;
        if (elCountZero) elCountZero.textContent = countZero;

        const filteredTasks = sanctumActiveTab === 'all'
            ? tasks
            : tasks.filter(t => t.status === sanctumActiveTab);

        container.innerHTML = '';

        if (filteredTasks.length === 0) {
            container.innerHTML = `
                <div style="text-align: center; color: var(--text-muted); padding: 40px 20px; font-size: 13px;">
                    No tasks found in this vector state.
                </div>
            `;
            return;
        }

        filteredTasks.forEach(task => {
            const card = document.createElement('div');
            const pClass = task.priority === 'baseline' ? 'p-baseline' : task.priority === 'critical' ? 'p-critical' : 'p-terminal';
            const pLabel = task.priority === 'baseline' ? 'Class I' : task.priority === 'critical' ? 'Class III' : 'Class IV';

            card.className = `sanctum-card ${pClass}`;
            card.innerHTML = `
                <div style="display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 6px;">
                    <h4 class="sanctum-card-title">${escapeHtml(task.title)}</h4>
                    <span style="font-size: 10px; font-weight: bold; opacity: 0.8; letter-spacing: 0.5px;">${pLabel}</span>
                </div>
                <div class="sanctum-card-desc">${escapeHtml(task.description)}</div>
                <div class="sanctum-card-footer">
                    <div>
                        <select class="sanctum-status-select" data-id="${task.id}">
                            <option value="synthesis" ${task.status === 'synthesis' ? 'selected' : ''}>Synthesis</option>
                            <option value="friction" ${task.status === 'friction' ? 'selected' : ''}>Friction</option>
                            <option value="zero" ${task.status === 'zero' ? 'selected' : ''}>Absolute Zero</option>
                        </select>
                    </div>
                    <div class="sanctum-card-actions">
                        <button class="sanctum-card-btn sanctum-edit-btn" data-id="${task.id}" title="Edit Task">EDIT</button>
                        <button class="sanctum-card-btn sanctum-del-btn" data-id="${task.id}" title="Delete Task">DEL</button>
                    </div>
                </div>
            `;

            // Handle status change
            const select = card.querySelector('.sanctum-status-select');
            select.addEventListener('change', (e) => {
                const newStatus = e.target.value;
                const all = getSanctumTasks();
                const idx = all.findIndex(t => t.id === task.id);
                if (idx > -1) {
                    all[idx].status = newStatus;
                    saveSanctumTasks(all);
                    renderSanctumTasks();
                }
            });

            // Handle edit
            const editBtn = card.querySelector('.sanctum-edit-btn');
            editBtn.addEventListener('click', () => {
                promptEditTask(task.id);
            });

            // Handle delete
            const delBtn = card.querySelector('.sanctum-del-btn');
            delBtn.addEventListener('click', () => {
                if (confirm(`Delete task: "${task.title}"?`)) {
                    const all = getSanctumTasks().filter(t => t.id !== task.id);
                    saveSanctumTasks(all);
                    renderSanctumTasks();
                }
            });

            container.appendChild(card);
        });
    }

    function promptEditTask(id) {
        const all = getSanctumTasks();
        const task = all.find(t => t.id === id);
        if (!task) return;

        const newTitle = prompt("Edit Task Title:", task.title);
        if (newTitle === null) return;
        const newDesc = prompt("Edit Task Description:", task.description);
        if (newDesc === null) return;

        task.title = newTitle.trim() || task.title;
        task.description = newDesc.trim();
        saveSanctumTasks(all);
        renderSanctumTasks();
    }

    function promptAddTask() {
        const title = prompt("Initialize Task Nomenclature (Title):");
        if (!title || !title.trim()) return;
        const desc = prompt("Task Parameters (Description):", "") || "";

        const newTask = {
            id: 'task_' + Date.now(),
            title: title.trim(),
            description: desc.trim(),
            status: sanctumActiveTab === 'all' ? 'synthesis' : sanctumActiveTab,
            priority: 'critical'
        };

        const all = getSanctumTasks();
        all.unshift(newTask);
        saveSanctumTasks(all);
        renderSanctumTasks();
    }

    function exportSanctumJSON() {
        const tasks = getSanctumTasks();
        const dataStr = JSON.stringify(tasks, null, 2);
        const dataBlob = new Blob([dataStr], { type: 'application/json' });
        const url = URL.createObjectURL(dataBlob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `hybris_dev_sanctum_tasks_${new Date().toISOString().slice(0, 10)}.json`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function initDevSanctumDrawer() {
        if (document.getElementById('dev-sanctum-drawer')) return;
        const path = decodeURIComponent(window.location.pathname).toLowerCase();
        if (path.includes('architect_tasks.html') || document.querySelector('.board')) return;

        // 1. Floating Trigger Button (Bottom Left)
        const triggerBtn = document.createElement('button');
        triggerBtn.id = 'dev-sanctum-floating-btn';
        triggerBtn.className = 'dev-sanctum-floating-btn';
        triggerBtn.type = 'button';
        triggerBtn.setAttribute('data-tooltip', 'Dev Sanctum Task Engine');
        triggerBtn.setAttribute('aria-label', 'Open Dev Sanctum Task Engine');
        triggerBtn.innerHTML = `${LIGHTNING_ICON} <span>SANCTUM</span>`;
        triggerBtn.addEventListener('click', toggleSanctumDrawer);
        document.body.appendChild(triggerBtn);

        // 2. Backdrop Overlay
        const backdrop = document.createElement('div');
        backdrop.id = 'dev-sanctum-backdrop';
        backdrop.className = 'dev-sanctum-backdrop';
        backdrop.addEventListener('click', toggleSanctumDrawer);
        document.body.appendChild(backdrop);

        // 3. Side Drawer Panel
        function resolveAppUrl(pathFromRoot) {
            if (window.location.protocol.startsWith('http')) {
                return pathFromRoot.startsWith('/') ? pathFromRoot : '/' + pathFromRoot;
            }
            const path = window.location.pathname;
            let depth = 0;
            if (path.includes('/Killtime/lore/timelines/') || path.includes('/Killtime/lore/abilities/')) {
                depth = 3;
            } else if (path.includes('/Killtime/lore/') || path.includes('/Killtime/manuscripts/') || path.includes('/Killtime/chatgpt/') || path.includes('/Killtime/scripts_and_data/')) {
                depth = 2;
            } else if (path.includes('/Killtime/')) {
                depth = 1;
            }
            const prefix = '../'.repeat(depth);
            return prefix + (pathFromRoot.startsWith('/') ? pathFromRoot.slice(1) : pathFromRoot);
        }

        const baselineUrl = resolveAppUrl('/index.html');
        const truthUrl = resolveAppUrl('/Killtime/lore/timelines/truth.html');
        const fullSanctumUrl = resolveAppUrl('/Killtime/lore/architect_tasks.html');

        const drawer = document.createElement('aside');
        drawer.id = 'dev-sanctum-drawer';
        drawer.className = 'dev-sanctum-drawer';
        drawer.setAttribute('aria-hidden', 'true');
        drawer.innerHTML = `
            <div class="sanctum-drawer-header">
                <div class="sanctum-header-title-wrap">
                    <div class="sanctum-title">⚡ DEV_SANCTUM: TASK ENGINE</div>
                    <div class="sanctum-subtitle">THERMODYNAMIC FRICTION & CAUSAL HAZARDS</div>
                </div>
                <div class="sanctum-header-actions">
                    <button id="sanctum-btn-add" class="sanctum-btn sanctum-btn-primary" title="Initialize Task">+ Task</button>
                    <button id="sanctum-btn-export" class="sanctum-btn" title="Export JSON">Export</button>
                    <button id="sanctum-btn-import" class="sanctum-btn" title="Import JSON">Import</button>
                    <input type="file" id="sanctum-file-import" style="display:none" accept=".json">
                    <button id="sanctum-btn-close" class="sanctum-close-btn" title="Close Drawer">✕</button>
                </div>
            </div>

            <!-- Quick Navigation Jump Bar -->
            <div style="display: flex; gap: 8px; padding: 8px 16px; background: var(--bg-tertiary); border-bottom: 1px solid var(--border-color); font-size: 11px; justify-content: space-between; flex-wrap: wrap;">
                <a href="${truthUrl}" class="sanctum-fullscreen-link" style="color: #ef4444; font-weight: bold;">✦ [ The Labyrinth / Truths ]</a>
                <a href="${baselineUrl}" class="sanctum-fullscreen-link" style="color: var(--accent-primary);">⟵ [ Return to Baseline ]</a>
            </div>

            <div class="sanctum-tab-bar">
                <button class="sanctum-tab active" data-tab="all">ALL (<span id="sanctum-count-all">0</span>)</button>
                <button class="sanctum-tab" data-tab="synthesis">SYNTHESIS (<span id="sanctum-count-synthesis">0</span>)</button>
                <button class="sanctum-tab" data-tab="friction">FRICTION (<span id="sanctum-count-friction">0</span>)</button>
                <button class="sanctum-tab" data-tab="zero">ZERO (<span id="sanctum-count-zero">0</span>)</button>
            </div>

            <div class="sanctum-drawer-content" id="sanctum-task-container"></div>

            <div class="sanctum-drawer-footer" style="flex-direction: column; gap: 6px; align-items: center;">
                <div style="display: flex; gap: 14px; font-size: 11px; flex-wrap: wrap; justify-content: center;">
                    <a href="${truthUrl}" class="sanctum-fullscreen-link">✦ Truths (Labyrinth)</a>
                    <a href="${baselineUrl}" class="sanctum-fullscreen-link">✦ False Baseline</a>
                    <a href="${fullSanctumUrl}" class="sanctum-fullscreen-link" style="opacity: 0.85;">⤢ Full Board</a>
                </div>
            </div>
        `;

        document.body.appendChild(drawer);

        // Bind events
        document.getElementById('sanctum-btn-close').addEventListener('click', toggleSanctumDrawer);
        document.getElementById('sanctum-btn-add').addEventListener('click', promptAddTask);
        document.getElementById('sanctum-btn-export').addEventListener('click', exportSanctumJSON);

        const fileImport = document.getElementById('sanctum-file-import');
        document.getElementById('sanctum-btn-import').addEventListener('click', () => fileImport.click());
        fileImport.addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (!file) return;
            const reader = new FileReader();
            reader.onload = (evt) => {
                try {
                    const imported = JSON.parse(evt.target.result);
                    if (Array.isArray(imported)) {
                        saveSanctumTasks(imported);
                        renderSanctumTasks();
                        alert("Task variables imported successfully.");
                    } else {
                        alert("Invalid JSON format.");
                    }
                } catch (err) {
                    alert("Error parsing JSON: " + err.message);
                }
                fileImport.value = '';
            };
            reader.readAsText(file);
        });

        // Tab selection
        const tabs = drawer.querySelectorAll('.sanctum-tab');
        tabs.forEach(tab => {
            tab.addEventListener('click', () => {
                tabs.forEach(t => t.classList.remove('active'));
                tab.classList.add('active');
                sanctumActiveTab = tab.dataset.tab;
                renderSanctumTasks();
            });
        });

        // Close on Escape key
        window.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && drawer.classList.contains('open')) {
                toggleSanctumDrawer();
            }
        });
    }

    // 7. Detection for Truths & Architect Dossier Pages
    function isTruthsPage() {
        const path = decodeURIComponent(window.location.pathname).toLowerCase();
        if (path.includes('architect_tasks.html')) return false;
        return path.includes('truth.html') ||
            path.includes('/timelines/') ||
            path.includes('/abilities/') ||
            path.includes('classified') ||
            path.includes('thematic_') ||
            path.includes('capricius_');
    }

    // 8. Inject UI Elements
    function initUI() {
        if (!document.getElementById('theme-switch-floating-btn')) {
            const btn = document.createElement('button');
            btn.id = 'theme-switch-floating-btn';
            btn.className = 'theme-switch-floating-btn';
            btn.type = 'button';
            btn.addEventListener('click', window.toggleKilltimeTheme);
            document.body.appendChild(btn);
        }

        // Bind any explicit toggle elements
        const toggleElements = document.querySelectorAll('.theme-toggle, .theme-toggle-header-btn');
        toggleElements.forEach(el => {
            el.addEventListener('click', window.toggleKilltimeTheme);
        });

        // Ensure button visuals match current state
        const activeTheme = document.documentElement.getAttribute('data-theme') || getSavedTheme();
        updateToggleButtons(activeTheme);

        // Initialize Dev Sanctum Floating Side-Drawer ONLY on Truths pages
        if (isTruthsPage()) {
            initDevSanctumDrawer();
        }
    }

    // Immediate execution for Zero-FOUC
    ensureThemeStylesheet();
    const initialTheme = getSavedTheme();
    applyTheme(initialTheme, false);

    // Run DOM UI setup once DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initUI);
    } else {
        initUI();
    }
})();
