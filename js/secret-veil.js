/* ==========================================================================
   CODEX — VOILE PHOTOSENSIBLE DU LORE CACHÉ (« Le Voile de l'Architecte »)
   js/secret-veil.js
   --------------------------------------------------------------------------
   Quand on tente d'accéder aux pages secrètes (spirale tracée à la souris,
   mot « hybris », 4e mur…), le passage s'ouvre avec des flashs, des
   scintillements et des secousses plein écran. Ce voile prévient AVANT,
   dans le thème Killtime, et propose d'entrer SANS les effets (skip).

   Mémoire du choix :
     - pas connecté  → localStorage (« kt-secret-veil ») ;
     - connecté      → serveur (GET/PUT /api/prefs, user.prefs.secretVeil),
                       avec miroir localStorage en repli / hors-ligne.
   Le choix n'est mémorisé qu'après confirmation explicite (case cochée).
   « Rester ici » ne mémorise rien.

   Usage page secrete (ex. site Killtime) :
     const acces = await window.KTSecretVeil.requestAccess();
     if (!acces) return;           // l'usager reste ici
     if (acces === 'safe') { }     // aller direct, SANS flashs
     else { }                      // effets puis page secrete

   Tout élément <a data-secret href="…"> est aussi intercepté seul.
   Le mode sûr pose <html class="kt-no-flash" data-kt-motion="safe">
   (voir css/secret-veil.css). Les moteurs d'effets doivent tester
   window.KTSecretVeil.isSafe() avant tout flash.
   ========================================================================== */
(function () {
    'use strict';

    var LS_KEY = 'kt-secret-veil';
    var PREFS_URL = '/api/prefs';
    var CHOICES = { full: true, safe: true };

    var sessionChoice = null;   // choix « sans mémoriser », onglet courant
    var pendingModal = null;    // anti double-clic : une seule modale à la fois

    /* ---------- base API (même règle que users/auth.js) ---------- */
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

    function isLoggedIn() {
        try {
            return !!(window.CodexAuth && window.CodexAuth.currentUser && window.CodexAuth.currentUser.id);
        } catch (e) {
            return false;
        }
    }

    function validChoice(c) {
        return (c === 'full' || c === 'safe') ? c : null;
    }

    /* ---------- lecture / écriture ---------- */
    function readLocal() {
        try {
            var raw = window.localStorage.getItem(LS_KEY);
            if (!raw) return null;
            var parsed = JSON.parse(raw);
            return validChoice(parsed && parsed.choice);
        } catch (e) {
            return null;
        }
    }

    function writeLocal(choice) {
        try {
            if (!validChoice(choice)) window.localStorage.removeItem(LS_KEY);
            else window.localStorage.setItem(LS_KEY, JSON.stringify({ choice: choice, at: Date.now() }));
        } catch (e) {}
    }

    async function loadChoice() {
        if (validChoice(sessionChoice)) return sessionChoice;
        if (isLoggedIn()) {
            try {
                var res = await fetch(apiBase() + PREFS_URL, {
                    credentials: 'include',
                    cache: 'no-store',
                    headers: { 'ngrok-skip-browser-warning': 'true' }
                });
                if (res.ok) {
                    var data = await res.json();
                    var serverChoice = validChoice(data && data.prefs && data.prefs.secretVeil);
                    if (serverChoice) {
                        writeLocal(serverChoice);   // miroir hors-ligne
                        return serverChoice;
                    }
                }
            } catch (e) {}
        }
        return readLocal();
    }

    async function saveChoice(choice) {
        writeLocal(choice);   // toujours : non-connecté + miroir
        if (isLoggedIn()) {
            try {
                await fetch(apiBase() + PREFS_URL, {
                    method: 'PUT',
                    credentials: 'include',
                    headers: {
                        'Content-Type': 'application/json',
                        'ngrok-skip-browser-warning': 'true'
                    },
                    body: JSON.stringify({ prefs: { secretVeil: choice } })
                });
            } catch (e) {}
        }
    }

    async function resetChoice() {
        sessionChoice = null;
        writeLocal(null);
        applyMode(null);
        if (isLoggedIn()) {
            try {
                await fetch(apiBase() + PREFS_URL, {
                    method: 'PUT',
                    credentials: 'include',
                    headers: {
                        'Content-Type': 'application/json',
                        'ngrok-skip-browser-warning': 'true'
                    },
                    body: JSON.stringify({ prefs: { secretVeil: null } })
                });
            } catch (e) {}
        }
    }

    /* ---------- mode sûr ---------- */
    function applyMode(choice) {
        try {
            var root = document.documentElement;
            if (choice === 'safe') {
                root.classList.add('kt-no-flash');
                root.setAttribute('data-kt-motion', 'safe');
            } else {
                root.classList.remove('kt-no-flash');
                root.setAttribute('data-kt-motion', choice === 'full' ? 'full' : 'none');
            }
            window.dispatchEvent(new CustomEvent('kt:veil-change', { detail: { choice: choice } }));
        } catch (e) {}
    }

    function prefersReducedMotion() {
        try {
            return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
        } catch (e) {
            return false;
        }
    }

    /* ---------- modale ---------- */
    function el(tag, cls, text) {
        var node = document.createElement(tag);
        if (cls) node.className = cls;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }

    function showModal() {
        return new Promise(function (resolve) {
            var prevFocus = null;
            try {
                prevFocus = document.activeElement;
            } catch (e) {}

            var backdrop = el('div', 'kt-veil-backdrop');
            var card = el('div', 'kt-veil');
            card.setAttribute('role', 'alertdialog');
            card.setAttribute('aria-modal', 'true');
            card.setAttribute('aria-labelledby', 'ktVeilTitle');
            card.setAttribute('aria-describedby', 'ktVeilDesc');

            var sigil = el('div', 'kt-veil-sigil', '◉');
            sigil.setAttribute('aria-hidden', 'true');

            var title = el('h2', 'kt-veil-title', 'Le Voile de l’Architecte');
            title.id = 'ktVeilTitle';

            var desc = el('p', 'kt-veil-desc',
                'Tu touches au lore caché. Le passage s’ouvre avec des flashs vifs, ' +
                'des scintillements et des secousses d’écran. Si la lumière forte te fait ' +
                'du mal, entre sans les effets : tu arriveras au même endroit, en douceur.');
            desc.id = 'ktVeilDesc';

            var remember = el('label', 'kt-veil-remember');
            var box = document.createElement('input');
            box.type = 'checkbox';
            box.checked = true;
            remember.appendChild(box);
            remember.appendChild(document.createTextNode(' Se souvenir de mon choix'));

            var actions = el('div', 'kt-veil-actions');
            var btnSafe = el('button', 'kt-veil-btn kt-veil-safe', 'Entrer sans flashs');
            btnSafe.type = 'button';
            var btnFull = el('button', 'kt-veil-btn kt-veil-full', 'Entrer avec les effets');
            btnFull.type = 'button';
            var btnCancel = el('button', 'kt-veil-btn kt-veil-cancel', 'Rester ici');
            btnCancel.type = 'button';
            actions.appendChild(btnSafe);
            actions.appendChild(btnFull);
            actions.appendChild(btnCancel);

            var note = el('p', 'kt-veil-note',
                isLoggedIn()
                    ? 'Connecté : ton choix est gardé sur ton compte.'
                    : 'Pas connecté : ton choix est gardé dans ce navigateur.');

            card.appendChild(sigil);
            card.appendChild(title);
            card.appendChild(desc);
            card.appendChild(remember);
            card.appendChild(actions);
            card.appendChild(note);
            backdrop.appendChild(card);
            document.body.appendChild(backdrop);

            function done(decision) {
                try {
                    document.removeEventListener('keydown', onKey, true);
                    if (backdrop.parentNode) backdrop.parentNode.removeChild(backdrop);
                    if (prevFocus && prevFocus.focus) prevFocus.focus();
                } catch (e) {}
                resolve(decision);
            }

            function onKey(e) {
                if (e.key === 'Escape') {
                    e.stopPropagation();
                    done(null);
                }
            }
            document.addEventListener('keydown', onKey, true);

            backdrop.addEventListener('mousedown', function (e) {
                if (e.target === backdrop) done(null);
            });
            btnCancel.addEventListener('click', function () { done(null); });
            btnSafe.addEventListener('click', function () {
                done({ choice: 'safe', remember: box.checked });
            });
            btnFull.addEventListener('click', function () {
                done({ choice: 'full', remember: box.checked });
            });

            try {
                btnSafe.focus();
            } catch (e) {}
        });
    }

    /* ---------- entrée principale ---------- */
    function requestAccess() {
        if (pendingModal) return pendingModal;
        pendingModal = (async function () {
            var stored = await loadChoice();
            if (validChoice(stored)) {
                applyMode(stored);
                pendingModal = null;
                return stored;
            }
            var decision = await showModal();
            pendingModal = null;
            if (!decision || !validChoice(decision.choice)) return null;
            if (decision.remember) await saveChoice(decision.choice);
            else sessionChoice = decision.choice;
            applyMode(decision.choice);
            return decision.choice;
        })();
        return pendingModal;
    }

    function isSafe() {
        if (validChoice(sessionChoice)) return sessionChoice === 'safe';
        return readLocal() === 'safe';
    }

    /* ---------- interception des liens marqués data-secret ---------- */
    function bindSecretLinks() {
        document.addEventListener('click', function (e) {
            var t = null;
            try {
                t = e.target && e.target.closest ? e.target.closest('[data-secret]') : null;
            } catch (err) {
                t = null;
            }
            if (!t) return;
            if (t.tagName !== 'A' || !t.getAttribute('href')) return;
            e.preventDefault();
            e.stopPropagation();
            var href = t.getAttribute('href');
            requestAccess().then(function (choice) {
                if (!choice) return;
                try {
                    window.location.assign(href);
                } catch (err) {}
            });
        }, true);
    }

    /* ---------- init ---------- */
    function init() {
        // Applique tôt le mode sûr mémorisé en local (avant tout flash).
        try {
            if (readLocal() === 'safe') applyMode('safe');
        } catch (e) {}
        bindSecretLinks();
        try {
            window.addEventListener('codex:auth:login', function () { sessionChoice = null; });
            window.addEventListener('codex:auth:logout', function () {
                sessionChoice = null;
                var local = readLocal();
                applyMode(local);
            });
        } catch (e) {}
    }

    window.KTSecretVeil = {
        requestAccess: requestAccess,
        isSafe: isSafe,
        getChoice: function () { return validChoice(sessionChoice) || readLocal(); },
        reset: resetChoice
    };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
