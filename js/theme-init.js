/* ==========================================================================
   CODEX UNIVERSEL DU SYSTÈME RP — INITIALISATION DU THÈME
   Script bloquant chargé dans <head>, avant les feuilles de style, pour
   appliquer le thème sauvegardé (utilisateur connecté ou localStorage, sinon
   préférence système) ainsi que la palette personnalisée sans aucun flash.
   La logique d'interface et de synchronisation vit dans js/theme.js.
   ========================================================================== */
(function () {
    'use strict';

    var THEMES = ['dark', 'light', 'sepia', 'night', 'emerald', 'contrast', 'custom'];
    window.CODEX_THEMES = THEMES;

    var PRESETS = {
        dark: {
            bgVoid: '#06080d',
            bgSurface: '#111827',
            textPrimary: '#f1f5f9',
            textSecondary: '#94a3b8',
            arcaneCyan: '#38bdf8',
            temporalAmber: '#f59e0b',
            vitalRuby: '#f43f5e',
            matrixEmerald: '#10b981',
            voidViolet: '#8b5cf6'
        },
        light: {
            bgVoid: '#f8fafc',
            bgSurface: '#ffffff',
            textPrimary: '#0f172a',
            textSecondary: '#334155',
            arcaneCyan: '#0284c7',
            temporalAmber: '#d97706',
            vitalRuby: '#e11d48',
            matrixEmerald: '#059669',
            voidViolet: '#7c3aed'
        },
        sepia: {
            bgVoid: '#f3ead5',
            bgSurface: '#fbf5e4',
            textPrimary: '#3a2a15',
            textSecondary: '#5b4527',
            arcaneCyan: '#0f766e',
            temporalAmber: '#b45309',
            vitalRuby: '#b91c1c',
            matrixEmerald: '#15803d',
            voidViolet: '#6d28d9'
        },
        night: {
            bgVoid: '#030b1c',
            bgSurface: '#0b2144',
            textPrimary: '#e8f1ff',
            textSecondary: '#a9c1e8',
            arcaneCyan: '#60a5fa',
            temporalAmber: '#fbbf24',
            vitalRuby: '#fb7185',
            matrixEmerald: '#34d399',
            voidViolet: '#a78bfa'
        },
        emerald: {
            bgVoid: '#03100a',
            bgSurface: '#0a2117',
            textPrimary: '#e8fff3',
            textSecondary: '#a7d8c2',
            arcaneCyan: '#34d399',
            temporalAmber: '#f59e0b',
            vitalRuby: '#fb7185',
            matrixEmerald: '#10b981',
            voidViolet: '#a78bfa'
        },
        contrast: {
            bgVoid: '#000000',
            bgSurface: '#0b0b0b',
            textPrimary: '#ffffff',
            textSecondary: '#e8e8e8',
            arcaneCyan: '#00e5ff',
            temporalAmber: '#ffff00',
            vitalRuby: '#ff5c5c',
            matrixEmerald: '#00ff9d',
            voidViolet: '#d8b4fe'
        }
    };

    var DEFAULT_CUSTOM = {
        bgVoid: '#090d16',
        bgSurface: '#131c2e',
        textPrimary: '#f1f5f9',
        textSecondary: '#94a3b8',
        arcaneCyan: '#38bdf8',
        temporalAmber: '#f59e0b',
        vitalRuby: '#f43f5e',
        matrixEmerald: '#10b981',
        voidViolet: '#8b5cf6'
    };

    var COLOR_KEYS = [
        'bgVoid', 'bgCanvas', 'bgSurface', 'bgSurfaceElevated', 'bgSurfaceHover',
        'textPrimary', 'textSecondary', 'textMuted', 'textAccent', 'textGold',
        'arcaneCyan', 'temporalAmber', 'vitalRuby', 'voidViolet', 'matrixEmerald'
    ];

    var CSS_VAR_NAMES = [
        '--bg-void', '--bg-canvas', '--bg-surface', '--bg-surface-elevated', '--bg-surface-hover',
        '--border-subtle', '--border-glow', '--border-accent',
        '--text-primary', '--text-secondary', '--text-muted', '--text-accent', '--text-gold',
        '--arcane-cyan', '--arcane-cyan-glow', '--on-arcane-cyan',
        '--temporal-amber', '--temporal-glow',
        '--vital-ruby', '--vital-ruby-glow',
        '--void-violet', '--matrix-emerald', '--matrix-glow'
    ];

    function normalizeHex(val) {
        if (typeof val !== 'string') return null;
        var s = val.trim().toLowerCase();
        if (/^#[0-9a-f]{6}$/.test(s)) return s;
        if (/^#[0-9a-f]{3}$/.test(s)) {
            return '#' + s[1] + s[1] + s[2] + s[2] + s[3] + s[3];
        }
        return null;
    }

    function parseHex(hex) {
        var h = normalizeHex(hex) || '#000000';
        return {
            r: parseInt(h.slice(1, 3), 16),
            g: parseInt(h.slice(3, 5), 16),
            b: parseInt(h.slice(5, 7), 16)
        };
    }

    function toHexByte(n) {
        var c = Math.max(0, Math.min(255, Math.round(n)));
        return (c < 16 ? '0' : '') + c.toString(16);
    }

    function mixHex(hex1, hex2, weight2) {
        var c1 = parseHex(hex1);
        var c2 = parseHex(hex2);
        var w = Math.max(0, Math.min(1, Number(weight2) || 0));
        var w1 = 1 - w;
        return '#' +
            toHexByte(c1.r * w1 + c2.r * w) +
            toHexByte(c1.g * w1 + c2.g * w) +
            toHexByte(c1.b * w1 + c2.b * w);
    }

    function rgba(hex, alpha) {
        var c = parseHex(hex);
        return 'rgba(' + c.r + ', ' + c.g + ', ' + c.b + ', ' + alpha + ')';
    }

    function luminance(hex) {
        var c = parseHex(hex);
        return (0.299 * c.r + 0.587 * c.g + 0.114 * c.b) / 255;
    }

    function sanitizeCustomTheme(raw, fallbackPresetId) {
        var base = (fallbackPresetId && PRESETS[fallbackPresetId]) ? PRESETS[fallbackPresetId] : DEFAULT_CUSTOM;
        var src = (raw && typeof raw === 'object') ? raw : {};
        if (src.basePreset && PRESETS[src.basePreset]) {
            base = PRESETS[src.basePreset];
        }
        var out = {
            bgVoid: normalizeHex(src.bgVoid) || base.bgVoid,
            bgSurface: normalizeHex(src.bgSurface) || base.bgSurface,
            textPrimary: normalizeHex(src.textPrimary) || base.textPrimary,
            textSecondary: normalizeHex(src.textSecondary) || base.textSecondary,
            arcaneCyan: normalizeHex(src.arcaneCyan) || base.arcaneCyan,
            temporalAmber: normalizeHex(src.temporalAmber) || base.temporalAmber,
            vitalRuby: normalizeHex(src.vitalRuby) || base.vitalRuby,
            matrixEmerald: normalizeHex(src.matrixEmerald) || base.matrixEmerald,
            voidViolet: normalizeHex(src.voidViolet) || base.voidViolet
        };
        for (var i = 0; i < COLOR_KEYS.length; i++) {
            var k = COLOR_KEYS[i];
            if (!(k in out) && src[k]) {
                var nh = normalizeHex(src[k]);
                if (nh) out[k] = nh;
            }
        }
        if (src.basePreset && PRESETS[src.basePreset]) out.basePreset = src.basePreset;
        if (typeof src.name === 'string' && src.name.trim()) out.name = src.name.trim().slice(0, 32);
        return out;
    }

    function resolveCustomVars(custom) {
        var c = sanitizeCustomTheme(custom);
        var isLight = luminance(c.bgSurface) > 0.5;
        var bgVoid = c.bgVoid;
        var bgSurface = c.bgSurface;
        var bgCanvas = c.bgCanvas || mixHex(bgVoid, bgSurface, 0.5);
        var bgSurfaceElevated = c.bgSurfaceElevated || mixHex(bgSurface, c.textPrimary, isLight ? 0.08 : 0.06);
        var bgSurfaceHover = c.bgSurfaceHover || mixHex(bgSurface, c.textPrimary, isLight ? 0.15 : 0.12);
        var textPrimary = c.textPrimary;
        var textSecondary = c.textSecondary;
        var textMuted = c.textMuted || mixHex(textSecondary, bgSurface, 0.38);
        var arcaneCyan = c.arcaneCyan;
        var temporalAmber = c.temporalAmber;
        var vitalRuby = c.vitalRuby;
        var voidViolet = c.voidViolet;
        var matrixEmerald = c.matrixEmerald;
        var textAccent = c.textAccent || arcaneCyan;
        var textGold = c.textGold || temporalAmber;
        var onArcaneCyan = luminance(arcaneCyan) > 0.52 ? '#06080d' : '#ffffff';

        return {
            colorScheme: isLight ? 'light' : 'dark',
            vars: {
                '--bg-void': bgVoid,
                '--bg-canvas': bgCanvas,
                '--bg-surface': bgSurface,
                '--bg-surface-elevated': bgSurfaceElevated,
                '--bg-surface-hover': bgSurfaceHover,
                '--border-subtle': isLight ? rgba(textPrimary, 0.14) : rgba('#ffffff', 0.10),
                '--border-glow': rgba(arcaneCyan, 0.30),
                '--border-accent': rgba(temporalAmber, 0.35),
                '--text-primary': textPrimary,
                '--text-secondary': textSecondary,
                '--text-muted': textMuted,
                '--text-accent': textAccent,
                '--text-gold': textGold,
                '--arcane-cyan': arcaneCyan,
                '--arcane-cyan-glow': rgba(arcaneCyan, isLight ? 0.22 : 0.35),
                '--on-arcane-cyan': onArcaneCyan,
                '--temporal-amber': temporalAmber,
                '--temporal-glow': rgba(temporalAmber, 0.25),
                '--vital-ruby': vitalRuby,
                '--vital-ruby-glow': rgba(vitalRuby, 0.25),
                '--void-violet': voidViolet,
                '--matrix-emerald': matrixEmerald,
                '--matrix-glow': rgba(matrixEmerald, 0.25)
            }
        };
    }

    function applyCustomVars(rootEl, custom) {
        if (!rootEl || !rootEl.style) return;
        var resolved = resolveCustomVars(custom);
        rootEl.style.colorScheme = resolved.colorScheme;
        var vars = resolved.vars;
        for (var k in vars) {
            if (Object.prototype.hasOwnProperty.call(vars, k)) {
                rootEl.style.setProperty(k, vars[k]);
            }
        }
    }

    function clearCustomVars(rootEl) {
        if (!rootEl || !rootEl.style) return;
        rootEl.style.removeProperty('color-scheme');
        for (var i = 0; i < CSS_VAR_NAMES.length; i++) {
            rootEl.style.removeProperty(CSS_VAR_NAMES[i]);
        }
    }

    function getActiveSessionUser() {
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

    function themeKeyFor(userId) {
        return userId ? ('codex-theme:user:' + userId) : 'codex-theme';
    }

    function customKeyFor(userId) {
        return userId ? ('codex-custom-theme:user:' + userId) : 'codex-custom-theme';
    }

    function readUserStorePrefs(userId) {
        if (!userId) return null;
        try {
            var sessionUser = getActiveSessionUser();
            if (sessionUser && sessionUser.id === userId && sessionUser.prefs && typeof sessionUser.prefs === 'object') {
                return sessionUser.prefs;
            }
            var dbRaw = localStorage.getItem('codex_user_database');
            if (!dbRaw) return null;
            var users = JSON.parse(dbRaw);
            if (!Array.isArray(users)) return null;
            for (var i = 0; i < users.length; i++) {
                if (users[i] && users[i].id === userId && users[i].prefs && typeof users[i].prefs === 'object') {
                    return users[i].prefs;
                }
            }
        } catch (e) {}
        return null;
    }

    function readSavedTheme(userId) {
        var saved = null;
        try {
            saved = localStorage.getItem(themeKeyFor(userId));
        } catch (e) {
            saved = null;
        }
        if ((!saved || THEMES.indexOf(saved) === -1) && userId) {
            var prefs = readUserStorePrefs(userId);
            if (prefs && prefs.theme && THEMES.indexOf(prefs.theme) !== -1) {
                saved = prefs.theme;
            }
        }
        if (saved === 'auto' || saved === 'system') return null;
        return (saved && THEMES.indexOf(saved) !== -1) ? saved : null;
    }

    function readSavedCustomTheme(userId) {
        var raw = null;
        try {
            var str = localStorage.getItem(customKeyFor(userId));
            if (str) raw = JSON.parse(str);
        } catch (e) {
            raw = null;
        }
        if (!raw && userId) {
            var prefs = readUserStorePrefs(userId);
            if (prefs && prefs.customTheme && typeof prefs.customTheme === 'object') {
                raw = prefs.customTheme;
            } else {
                try {
                    var anonStr = localStorage.getItem('codex-custom-theme');
                    if (anonStr) raw = JSON.parse(anonStr);
                } catch (e2) {}
            }
        }
        return sanitizeCustomTheme(raw);
    }

    window.CODEX_THEME_PRESETS = PRESETS;
    window.CODEX_CUSTOM_THEME_UTILS = {
        DEFAULT_CUSTOM: DEFAULT_CUSTOM,
        COLOR_KEYS: COLOR_KEYS,
        CSS_VAR_NAMES: CSS_VAR_NAMES,
        normalizeHex: normalizeHex,
        sanitizeCustomTheme: sanitizeCustomTheme,
        resolveCustomVars: resolveCustomVars,
        applyCustomVars: applyCustomVars,
        clearCustomVars: clearCustomVars,
        getActiveSessionUser: getActiveSessionUser,
        getActiveUserId: getActiveUserId,
        themeKeyFor: themeKeyFor,
        customKeyFor: customKeyFor,
        readSavedTheme: readSavedTheme,
        readSavedCustomTheme: readSavedCustomTheme
    };

    var uid = getActiveUserId();
    var initial = readSavedTheme(uid);
    if (!initial && uid) {
        initial = readSavedTheme(null);
    }
    if (!initial || THEMES.indexOf(initial) === -1) {
        var light = false;
        try {
            light = !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches);
        } catch (e) {
            light = false;
        }
        initial = light ? 'light' : 'dark';
    }

    var root = document.documentElement;
    root.setAttribute('data-theme', initial);
    if (initial === 'custom') {
        applyCustomVars(root, readSavedCustomTheme(uid));
    } else {
        clearCustomVars(root);
    }
})();
