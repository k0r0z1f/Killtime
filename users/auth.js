/* ==========================================================================
   CODEX UNIVERSEL — AUTHENTICATION ENGINE & UI CONTROLLER
   users/auth.js
   ========================================================================== */

(function () {
    const SESSION_COOKIE_NAME = 'codex_auth_token';
    const SESSION_STORAGE_KEY = 'codex_active_session';

    /**
     * Détermine la racine d'API cible selon le contexte d'exécution.
     */
    const getApiBase = () => {
        // 1. Surcharge globale facultative
        if (window.CODEX_API_BASE) {
            return window.CODEX_API_BASE.replace(/\/+$/, '');
        }

        // 2. Fichier local ouvert directement
        if (window.location.protocol === 'file:') {
            return 'http://localhost:3000';
        }

        // 3. Application servie sur le port 8000 (cible l'API Node sur le port 3000)
        if (window.location.port === '8000') {
            return `${window.location.protocol}//${window.location.hostname}:3000`;
        }

        // 4. Environnement dev localhost classique
        if ((window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1') && window.location.port !== '3000') {
            return 'http://localhost:3000';
        }

        // 5. Même origine par défaut
        return '';
    };

    class CodexAuthManager {
        constructor() {
            this.currentUser = null;
            this.restoreSession();
            this.checkOAuthCallback();
            this.setupUI();
        }

        setupUI() {
            const run = () => {
                this.injectModal();
                this.mountTopbarWidget();
            };

            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', run);
            } else {
                run();
            }
        }

        setCookie(name, value, days = 7) {
            const date = new Date();
            date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
            const expires = '; expires=' + date.toUTCString();
            document.cookie = `${name}=${encodeURIComponent(value)}${expires}; path=/; SameSite=Lax`;
        }

        getCookie(name) {
            const nameEQ = name + '=';
            const ca = document.cookie.split(';');
            for (let i = 0; i < ca.length; i++) {
                let c = ca[i];
                while (c.charAt(0) === ' ') c = c.substring(1, c.length);
                if (c.indexOf(nameEQ) === 0) return decodeURIComponent(c.substring(nameEQ.length, c.length));
            }
            return null;
        }

        deleteCookie(name) {
            document.cookie = `${name}=; Path=/; Expires=Thu, 01 Jan 1970 00:00:01 GMT; SameSite=Lax`;
        }

        restoreSession() {
            const token = this.getCookie(SESSION_COOKIE_NAME);
            const localSession = localStorage.getItem(SESSION_STORAGE_KEY);

            if (token && localSession) {
                try {
                    const parsed = JSON.parse(localSession);
                    if (parsed && parsed.id) {
                        this.currentUser = parsed;
                        return;
                    }
                } catch (e) {
                    this.clearSession();
                }
            } else if (localSession) {
                try {
                    this.currentUser = JSON.parse(localSession);
                    this.setCookie(SESSION_COOKIE_NAME, 'ck_' + btoa(this.currentUser.id + ':' + Date.now()), 7);
                } catch (e) {
                    this.clearSession();
                }
            }
        }

        saveSession(user) {
            this.currentUser = user;
            const currentCookie = this.getCookie(SESSION_COOKIE_NAME);
            // Conserve le cookie serveur 'srv_' s'il existe, sinon enregistre un token client 'ck_'
            if (!currentCookie || !currentCookie.startsWith('srv_')) {
                const token = 'ck_' + btoa(user.id + ':' + Date.now());
                this.setCookie(SESSION_COOKIE_NAME, token, 7);
            }

            localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(user));
            this.mountTopbarWidget();
            window.dispatchEvent(new CustomEvent('codex:auth:login', { detail: { user } }));
        }

        clearSession() {
            const token = this.getCookie(SESSION_COOKIE_NAME) || '';
            this.currentUser = null;
            this.deleteCookie(SESSION_COOKIE_NAME);
            localStorage.removeItem(SESSION_STORAGE_KEY);

            // N'envoie un appel serveur que si un token de session serveur actif ('srv_') existait
            if (token.startsWith('srv_')) {
                const apiBase = getApiBase();
                fetch(`${apiBase}/api/auth/logout`, {
                    method: 'POST',
                    credentials: 'include'
                }).catch(() => { /* Silencieux si serveur injoignable */ });
            }

            this.mountTopbarWidget();
            window.dispatchEvent(new CustomEvent('codex:auth:logout'));
        }

        checkOAuthCallback() {
            const hash = window.location.hash.substring(1);
            const params = new URLSearchParams(hash || window.location.search);

            // Retour OAuth serveur (Google, etc.) — le serveur redirige avec #oauth_success=<base64 user>
            if (params.has('oauth_success')) {
                try {
                    const userJson = atob(decodeURIComponent(params.get('oauth_success')));
                    const user = JSON.parse(userJson);
                    this.saveSession(user);
                    console.log('[CodexAuth] OAuth réussi pour', user.username);
                } catch (e) {
                    console.error('[CodexAuth] Erreur parsing OAuth callback:', e);
                }
                // Nettoyer l'URL en conservant pathname + search
                window.history.replaceState({}, document.title, window.location.pathname + window.location.search);
                return;
            }

            // Erreur OAuth
            if (params.has('oauth_error')) {
                console.warn('[CodexAuth] Erreur OAuth:', params.get('oauth_error'));
                window.history.replaceState({}, document.title, window.location.pathname + window.location.search);
                return;
            }

            // Discord implicit flow (legacy)
            if (params.has('access_token') && window.location.href.includes('discord')) {
                this.fetchDiscordUser(params.get('access_token'));
            }
        }

        async handleSocialPayload(payload) {
            try {
                const res = await fetch(`${getApiBase()}/api/auth/social`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'include',
                    body: JSON.stringify(payload)
                });

                const contentType = res.headers.get('content-type') || '';
                if (!contentType.includes('application/json')) {
                    throw new Error("Réponse serveur non JSON.");
                }

                if (res.ok) {
                    const data = await res.json();
                    if (window.CodexUserStore) {
                        window.CodexUserStore.registerOrLoginSocialUser(payload);
                    }
                    this.saveSession(data.user);
                    this.closeModal();
                    return;
                }
            } catch (e) {
                console.warn('[CodexAuth] Serveur distant injoignable. Repli local.');
            }

            if (window.CodexUserStore) {
                const user = window.CodexUserStore.registerOrLoginSocialUser(payload);
                this.saveSession(user);
                this.closeModal();
            }
        }

        async fetchDiscordUser(accessToken) {
            try {
                const res = await fetch('https://discord.com/api/users/@me', {
                    headers: { Authorization: `Bearer ${accessToken}` }
                });
                if (res.ok) {
                    const data = await res.json();
                    const payload = {
                        provider: 'discord',
                        providerId: data.id,
                        email: data.email,
                        name: data.username,
                        avatar: data.avatar ? `https://cdn.discordapp.com/avatars/${data.id}/${data.avatar}.png` : null
                    };
                    await this.handleSocialPayload(payload);
                    window.history.replaceState({}, document.title, window.location.pathname);
                }
            } catch (e) {
                console.error('[OAuth Discord] Échec :', e);
            }
        }

        loginWithGoogle() {
            // Vraie redirection OAuth 2.0 vers Google avec préservation de la page de retour
            const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
            window.location.href = `${getApiBase()}/api/oauth/google?return=${returnUrl}`;
        }

        loginWithDiscord() {
            // Vraie redirection OAuth 2.0 vers Discord avec préservation de la page de retour
            const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
            window.location.href = `${getApiBase()}/api/oauth/discord?return=${returnUrl}`;
        }

        loginWithGitHub() {
            // Vraie redirection OAuth 2.0 vers GitHub avec préservation de la page de retour
            const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
            window.location.href = `${getApiBase()}/api/oauth/github?return=${returnUrl}`;
        }

        mountTopbarWidget() {
            let slot = document.getElementById('authTopbarSlot');

            if (!slot) {
                // 1. Chercher dans la topbar Codex
                const topbarRight = document.querySelector('.topbar-right');
                if (topbarRight) {
                    slot = document.createElement('div');
                    slot.id = 'authTopbarSlot';
                    const themeBtn = document.getElementById('themeToggle');
                    if (themeBtn) {
                        topbarRight.insertBefore(slot, themeBtn);
                    } else {
                        topbarRight.appendChild(slot);
                    }
                } else {
                    // 2. Chercher dans les contrôles du site Killtime
                    const topControls = document.querySelector('.top-controls');
                    if (topControls) {
                        slot = document.createElement('div');
                        slot.id = 'authTopbarSlot';
                        topControls.insertBefore(slot, topControls.firstChild);
                    } else {
                        return;
                    }
                }
            }

            slot.innerHTML = '';

            let codexPrefix = '';
            const path = window.location.pathname;
            if (path.includes('/manuscripts/') || path.includes('/lore/') || path.includes('/chatgpt/')) {
                codexPrefix = '../../../';
            } else if (path.includes('/Killtime/')) {
                codexPrefix = '../';
            }
            const isEnglish = document.documentElement.lang === 'en';

            if (this.currentUser) {
                const initial = (this.currentUser.username || 'U').charAt(0).toUpperCase();
                const menuWrapper = document.createElement('div');
                menuWrapper.className = 'user-profile-menu';

                menuWrapper.innerHTML = `
                    <button class="user-profile-btn" id="userMenuBtn" type="button">
                        <div class="user-avatar-circle">${initial}</div>
                        <span class="user-profile-name">${this.currentUser.username}</span>
                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <polyline points="6 9 12 15 18 9"/>
                        </svg>
                    </button>
                    <div class="user-dropdown-panel" id="userDropdownPanel">
                        <div class="user-dropdown-header">
                            <span class="user-dropdown-role">${this.currentUser.role || (isEnglish ? 'Player' : 'Joueur')}</span>
                            <div class="user-dropdown-email">${this.currentUser.email}</div>
                        </div>
                        <a href="${codexPrefix}livre_9.html#chap-33" class="user-dropdown-item">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                                <circle cx="12" cy="7" r="4"/>
                            </svg>
                            <span>${isEnglish ? 'My Character Sheets (Codex)' : 'Mes Fiches PJ (Codex)'}</span>
                        </a>
                        <button class="user-dropdown-item" id="exportUserDataBtn" type="button">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                                <polyline points="7 10 12 15 17 10"/>
                                <line x1="12" y1="15" x2="12" y2="3"/>
                            </svg>
                            <span>${isEnglish ? 'JSON Backup' : 'Sauvegarde JSON'}</span>
                        </button>
                        <button class="user-dropdown-item logout" id="logoutBtn" type="button">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/>
                                <polyline points="16 17 21 12 16 7"/>
                                <line x1="21" y1="12" x2="9" y2="12"/>
                            </svg>
                            <span>${isEnglish ? 'Logout' : 'Déconnexion'}</span>
                        </button>
                    </div>
                `;

                slot.appendChild(menuWrapper);

                const menuBtn = menuWrapper.querySelector('#userMenuBtn');
                const panel = menuWrapper.querySelector('#userDropdownPanel');

                menuBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    panel.classList.toggle('open');
                });

                document.addEventListener('click', () => panel.classList.remove('open'));

                menuWrapper.querySelector('#logoutBtn').addEventListener('click', () => {
                    this.clearSession();
                });

                menuWrapper.querySelector('#exportUserDataBtn').addEventListener('click', () => {
                    if (window.CodexUserStore) {
                        const dataStr = "data:text/json;charset=utf-8," + encodeURIComponent(window.CodexUserStore.exportDatabaseJson());
                        const dlAnchor = document.createElement('a');
                        dlAnchor.setAttribute("href", dataStr);
                        dlAnchor.setAttribute("download", `codex_users_${Date.now()}.json`);
                        dlAnchor.click();
                    }
                });
            } else {
                const btn = document.createElement('button');
                btn.className = 'auth-trigger-btn';
                btn.id = 'authModalTrigger';
                btn.type = 'button';
                const triggerText = isEnglish ? 'Login' : 'Connexion';
                btn.innerHTML = `
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"/>
                        <polyline points="10 17 15 12 10 7"/>
                        <line x1="15" y1="12" x2="3" y2="12"/>
                    </svg>
                    <span>${triggerText}</span>
                `;
                btn.addEventListener('click', () => this.openModal());
                slot.appendChild(btn);
            }
        }

        injectModal() {
            if (document.getElementById('codexAuthModal')) return;

            const modalWrapper = document.createElement('div');
            modalWrapper.id = 'codexAuthModal';
            modalWrapper.className = 'auth-modal-backdrop';

            modalWrapper.innerHTML = `
                <div class="auth-modal-window">
                    <div class="auth-modal-header">
                        <div class="auth-modal-title">
                            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
                                <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                            </svg>
                            <span>Accès Réseau Codex</span>
                        </div>
                        <button class="auth-modal-close" id="authCloseBtn" type="button">&times;</button>
                    </div>

                    <div class="auth-modal-body">
                        <div class="auth-alert-box" id="authAlertBox"></div>

                        <div class="auth-tabs">
                            <button class="auth-tab-btn active" id="tabLoginBtn" type="button">Connexion</button>
                            <button class="auth-tab-btn" id="tabRegisterBtn" type="button">Créer un Compte</button>
                        </div>

                        <form class="auth-form" id="loginForm">
                            <div class="auth-field">
                                <label for="authLoginId">Identifiant ou E-mail</label>
                                <div class="auth-input-wrapper">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
                                    <input type="text" id="authLoginId" class="auth-input" placeholder="ex: AlexisLacasse" required autocomplete="username">
                                </div>
                            </div>

                            <div class="auth-field">
                                <label for="authLoginPass">Mot de Passe</label>
                                <div class="auth-input-wrapper">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
                                    <input type="password" id="authLoginPass" class="auth-input" placeholder="••••••••" required autocomplete="current-password">
                                </div>
                            </div>

                            <button type="submit" class="auth-submit-btn">
                                <span>Déverrouiller la Session</span>
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"/></svg>
                            </button>
                        </form>

                        <form class="auth-form" id="registerForm" style="display: none;">
                            <div class="auth-field">
                                <label for="authRegUser">Nom d'Usager / Indicatif</label>
                                <div class="auth-input-wrapper">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
                                    <input type="text" id="authRegUser" class="auth-input" placeholder="Min. 3 lettres..." required autocomplete="username">
                                </div>
                            </div>

                            <div class="auth-field">
                                <label for="authRegEmail">Adresse E-mail</label>
                                <div class="auth-input-wrapper">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/></svg>
                                    <input type="email" id="authRegEmail" class="auth-input" placeholder="nom@monde.net" required autocomplete="email">
                                </div>
                            </div>

                            <div class="auth-field">
                                <label for="authRegPass">Mot de Passe (PBKDF2 100k)</label>
                                <div class="auth-input-wrapper">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
                                    <input type="password" id="authRegPass" class="auth-input" placeholder="Min. 6 caractères..." required autocomplete="new-password">
                                </div>
                            </div>

                            <button type="submit" class="auth-submit-btn">
                                <span>Créer Mon Compte</span>
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>
                            </button>
                        </form>

                        <div class="auth-divider">Ou Connexion Réseau Tiers</div>

                        <div class="auth-social-buttons">
                            <button type="button" class="social-btn google" id="btnGoogleAuth">
                                <svg width="16" height="16" viewBox="0 0 24 24"><path fill="#4285F4" d="M23.745 12.27c0-.7-.06-1.4-.19-2.07H12v4.51h6.6c-.29 1.52-1.14 2.82-2.4 3.68v3.05h3.88c2.27-2.09 3.66-5.17 3.66-9.17Z"/><path fill="#34A853" d="M12 24c3.24 0 5.95-1.08 7.93-2.91l-3.88-3.05c-1.08.72-2.45 1.16-4.05 1.16-3.12 0-5.77-2.1-6.72-4.93H1.25v3.15C3.26 21.36 7.33 24 12 24Z"/><path fill="#FBBC05" d="M5.28 14.27c-.25-.72-.38-1.49-.38-2.27s.13-1.55.38-2.27V6.58H1.25C.45 8.17 0 9.99 0 12s.45 3.83 1.25 5.42l4.03-3.15Z"/><path fill="#EA4335" d="M12 4.75c1.77 0 3.35.61 4.6 1.8l3.42-3.42C17.95 1.19 15.24 0 12 0 7.33 0 3.26 2.64 1.25 6.58l4.03 3.15c.95-2.83 3.6-4.98 6.72-4.98Z"/></svg>
                                <span>Continuer avec Google</span>
                            </button>

                            <button type="button" class="social-btn discord" id="btnDiscordAuth">
                                <svg width="16" height="16" fill="currentColor" viewBox="0 0 24 24"><path d="M20.317 4.37a19.791 19.791 0 0 0-4.885-1.515.074.074 0 0 0-.079.037c-.21.375-.444.864-.608 1.25a18.27 18.27 0 0 0-5.487 0 12.64 12.64 0 0 0-.617-1.25.077.077 0 0 0-.079-.037A19.736 19.736 0 0 0 3.677 4.37a.07.07 0 0 0-.032.027C.533 9.046-.32 13.58.099 18.057a.082.082 0 0 0 .031.057 19.9 19.9 0 0 0 5.993 3.03.078.078 0 0 0 .084-.028c.462-.63.874-1.295 1.226-1.994.021-.041.001-.09-.041-.106a13.107 13.107 0 0 1-1.872-.892.077.077 0 0 1-.008-.128 10.2 10.2 0 0 0 .372-.292.074.074 0 0 1 .077-.01c3.929 1.793 8.18 1.793 12.061 0a.074.074 0 0 1 .078.01c.12.098.246.198.373.292a.077.077 0 0 1-.006.127 12.299 12.299 0 0 1-1.873.894.077.077 0 0 0-.041.107c.36.698.772 1.362 1.225 1.993a.076.076 0 0 0 .084.028 19.839 19.839 0 0 0 6.002-3.03.077.077 0 0 0 .032-.054c.5-5.177-.838-9.674-3.549-13.66a.061.061 0 0 0-.031-.028zM8.02 15.33c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.956-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.956 2.418-2.157 2.418zm7.975 0c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.955-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.946 2.418-2.157 2.418z"/></svg>
                                <span>Continuer avec Discord</span>
                            </button>

                            <button type="button" class="social-btn github" id="btnGithubAuth">
                                <svg width="16" height="16" fill="currentColor" viewBox="0 0 24 24"><path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0 0 24 12c0-6.63-5.37-12-12-12Z"/></svg>
                                <span>Continuer avec GitHub</span>
                            </button>
                        </div>
                    </div>
                </div>
            `;

            document.body.appendChild(modalWrapper);
            this.bindModalEvents(modalWrapper);
        }

        bindModalEvents(modal) {
            const closeBtn = modal.querySelector('#authCloseBtn');
            const tabLogin = modal.querySelector('#tabLoginBtn');
            const tabReg = modal.querySelector('#tabRegisterBtn');
            const loginForm = modal.querySelector('#loginForm');
            const regForm = modal.querySelector('#registerForm');
            const alertBox = modal.querySelector('#authAlertBox');

            const showAlert = (msg, isSuccess = false) => {
                alertBox.textContent = msg;
                alertBox.className = 'auth-alert-box ' + (isSuccess ? 'success' : 'error');
            };

            const hideAlert = () => {
                alertBox.className = 'auth-alert-box';
                alertBox.textContent = '';
            };

            closeBtn.addEventListener('click', () => this.closeModal());
            modal.addEventListener('click', (e) => {
                if (e.target === modal) this.closeModal();
            });

            tabLogin.addEventListener('click', () => {
                tabLogin.classList.add('active');
                tabReg.classList.remove('active');
                loginForm.style.display = 'flex';
                regForm.style.display = 'none';
                hideAlert();
            });

            tabReg.addEventListener('click', () => {
                tabReg.classList.add('active');
                tabLogin.classList.remove('active');
                regForm.style.display = 'flex';
                loginForm.style.display = 'none';
                hideAlert();
            });

            // Connexion sécurisée avec repli automatique
            loginForm.addEventListener('submit', async (e) => {
                e.preventDefault();
                hideAlert();
                const id = document.getElementById('authLoginId').value.trim();
                const pass = document.getElementById('authLoginPass').value;

                let apiError = null;

                try {
                    const res = await fetch(`${getApiBase()}/api/auth/login`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ identifier: id, password: pass })
                    });

                    const contentType = res.headers.get('content-type') || '';
                    if (contentType.includes('application/json')) {
                        const data = await res.json();
                        if (res.ok) {
                            this.saveSession(data.user);
                            this.closeModal();
                            return;
                        } else {
                            apiError = data.error || 'Identifiants invalides.';
                        }
                    } else {
                        apiError = 'Serveur distant indisponible.';
                    }
                } catch (networkErr) {
                    apiError = networkErr.message;
                }

                // Repli sur le stockage local (CodexUserStore)
                if (window.CodexUserStore) {
                    try {
                        const user = await window.CodexUserStore.authenticateLocalUser(id, pass);
                        this.saveSession(user);
                        this.closeModal();
                        return;
                    } catch (localErr) {
                        showAlert(localErr.message || apiError, false);
                        return;
                    }
                }

                showAlert(apiError || 'Échec de la connexion.', false);
            });

            // Création de compte sécurisée avec synchronisation et repli
            regForm.addEventListener('submit', async (e) => {
                e.preventDefault();
                hideAlert();
                const user = document.getElementById('authRegUser').value.trim();
                const email = document.getElementById('authRegEmail').value.trim();
                const pass = document.getElementById('authRegPass').value;

                let apiError = null;

                try {
                    const res = await fetch(`${getApiBase()}/api/auth/register`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            username: user,
                            email: email,
                            password: pass,
                            role: 'Joueur'
                        })
                    });

                    const contentType = res.headers.get('content-type') || '';
                    if (contentType.includes('application/json')) {
                        const data = await res.json();
                        if (res.ok) {
                            if (window.CodexUserStore) {
                                try {
                                    await window.CodexUserStore.registerLocalUser({
                                        username: user,
                                        email: email,
                                        password: pass,
                                        role: 'Joueur'
                                    });
                                } catch (e) { /* sync locale */ }
                            }
                            this.saveSession(data.user);
                            this.closeModal();
                            return;
                        } else {
                            apiError = data.error || "Erreur lors de l'enregistrement.";
                        }
                    } else {
                        apiError = 'Serveur distant indisponible.';
                    }
                } catch (networkErr) {
                    apiError = networkErr.message;
                }

                // Repli local en cas d'absence de serveur
                if (window.CodexUserStore) {
                    try {
                        const newUser = await window.CodexUserStore.registerLocalUser({
                            username: user,
                            email: email,
                            password: pass,
                            role: 'Joueur'
                        });
                        this.saveSession(newUser);
                        this.closeModal();
                        return;
                    } catch (localErr) {
                        showAlert(localErr.message || apiError, false);
                        return;
                    }
                }

                showAlert(apiError || "Impossible de créer le compte.", false);
            });

            modal.querySelector('#btnGoogleAuth').addEventListener('click', () => this.loginWithGoogle());
            modal.querySelector('#btnDiscordAuth').addEventListener('click', () => this.loginWithDiscord());
            modal.querySelector('#btnGithubAuth').addEventListener('click', () => this.loginWithGitHub());
        }

        openModal() {
            const modal = document.getElementById('codexAuthModal');
            if (modal) {
                modal.classList.add('active');
                const input = document.getElementById('authLoginId');
                if (input) input.focus();
            }
        }

        closeModal() {
            const modal = document.getElementById('codexAuthModal');
            if (modal) modal.classList.remove('active');
        }
    }

    window.CodexAuth = new CodexAuthManager();
})();