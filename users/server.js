/* ==========================================================================
   CODEX UNIVERSEL — AUTHENTICATION & STATIC HTTP ENGINE (ZERO DEPENDENCY)
   users/server.js
   Exécution : node users/server.js
   ========================================================================== */

const http = require('http');
const https = require('https');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { URL: NodeURL, URLSearchParams: NodeURLSearchParams } = require('url');

/* ---------- .env parser (zero dependency) ---------- */
const ENV_PATH = path.join(__dirname, '..', '.env');
if (fs.existsSync(ENV_PATH)) {
    const envContent = fs.readFileSync(ENV_PATH, 'utf-8');
    for (const line of envContent.split('\n')) {
        const trimmed = line.trim();
        if (!trimmed || trimmed.startsWith('#')) continue;
        const eqIdx = trimmed.indexOf('=');
        if (eqIdx === -1) continue;
        const key = trimmed.slice(0, eqIdx).trim();
        const val = trimmed.slice(eqIdx + 1).trim();
        if (key && !process.env[key]) process.env[key] = val;
    }
}

const GOOGLE_CLIENT_ID     = process.env.GOOGLE_CLIENT_ID || '';
const GOOGLE_CLIENT_SECRET = process.env.GOOGLE_CLIENT_SECRET || '';
const DISCORD_CLIENT_ID     = process.env.DISCORD_CLIENT_ID || '';
const DISCORD_CLIENT_SECRET = process.env.DISCORD_CLIENT_SECRET || '';
const GITHUB_CLIENT_ID      = process.env.GITHUB_CLIENT_ID || '';
const GITHUB_CLIENT_SECRET  = process.env.GITHUB_CLIENT_SECRET || '';
const OAUTH_CALLBACK_BASE   = (process.env.OAUTH_CALLBACK_BASE || '').replace(/\/+$/, '');

/* ---------- Administration (rôles serveur + page locale) ----------
   Seule source de vérité : les e-mails listés dans .env (ADMIN_EMAILS,
   séparés par des virgules). Le client ne peut jamais s'attribuer un
   rôle : le serveur recalcule role/isAdmin à chaque réponse.
   La page admin tourne sur un 2e écouteur lié à 127.0.0.1 uniquement
   (jamais exposé par ngrok qui ne relaie que le port public). */
const ADMIN_PORT = parseInt(process.env.ADMIN_PORT || '3100', 10);
const ADMIN_BIND = process.env.ADMIN_BIND || '127.0.0.1';
function getAdminEmails() {
    return String(process.env.ADMIN_EMAILS || '').split(',')
        .map(s => s.trim().toLowerCase()).filter(Boolean);
}
function isAdminEmail(email) {
    const e = String(email || '').trim().toLowerCase();
    return !!e && getAdminEmails().includes(e);
}
function toSafeUser(user) {
    const { saltHex, passwordHashHex, ...rest } = user;
    const admin = isAdminEmail(user.email);
    return { ...rest, role: roleNameFor(user), roleId: roleIdFor(user), isAdmin: admin, permissions: getUserPermissions(user) };
}
function getSessionUser(req) {
    const id = getSessionUserId(req);
    if (!id) return null;
    return loadUsers().find(u => u.id === id) || null;
}
function checkAdmin(req) {
    const u = getSessionUser(req);
    if (!u) return { status: 401, error: 'Connexion requise.' };
    if (!isAdminEmail(u.email)) return { status: 403, error: 'Réservé à l’administrateur du site.' };
    return { user: u };
}
function requirePerm(req, perm) {
    const u = getSessionUser(req);
    if (!u) return { status: 401, error: 'Connexion requise.' };
    if (!getUserPermissions(u).includes(perm)) return { status: 403, error: 'Permission requise : ' + perm + '.' };
    return { user: u };
}
function isLoopbackHost(req) {
    try {
        const host = String(req.headers.host || '').split(':')[0].replace(/^\[|\]$/g, '').toLowerCase();
        return host === 'localhost' || host === '127.0.0.1' || host === '::1';
    } catch (e) { return false; }
}
function persistAdminEmails(list) {
    const clean = [...new Set(list.map(s => String(s).trim().toLowerCase()).filter(s => s.includes('@')))];
    let lines = [];
    try { if (fs.existsSync(ENV_PATH)) lines = fs.readFileSync(ENV_PATH, 'utf-8').split('\n'); } catch (e) {}
    lines = lines.filter(l => !/^\s*ADMIN_EMAILS\s*=/.test(l));
    lines.push('ADMIN_EMAILS=' + clean.join(','));
    fs.writeFileSync(ENV_PATH, lines.join('\n'));
    process.env.ADMIN_EMAILS = clean.join(',');
    return clean;
}
// Écritures disque : une permission par kanban (les clients replient en
// localStorage sur 401/403, sans erreur visible).
const WRITE_GUARDS = [
    { route: '/api/roadmaps', perm: 'kanban.roadmaps' },
    { route: '/api/roadmap', perm: 'kanban.roadmaps' },
    { route: '/api/kanban-rp', perm: 'kanban.revue-rp' },
    { route: '/api/kanban-narratif', perm: 'kanban.narratif' },
    { route: '/api/financement', perm: 'kanban.financement' },
    { route: '/api/atlas-mondes', perm: 'atlas.write' }
];

/* ---------- RBAC : rôles + permissions (triggers) ----------
   ADMIN_EMAILS (.env) = super-admins : toutes les permissions, toujours.
   Les autres droits passent par des rôles créés dans la page admin
   (stockés dans users/roles.json) et assignés aux utilisateurs. */
const PERMISSIONS = [
    { id: 'admin.access', label: 'Accès page admin' },
    { id: 'kanban.roadmaps', label: 'Écrire kanban Tactics (roadmaps)' },
    { id: 'kanban.revue-rp', label: 'Écrire revue Système RP' },
    { id: 'kanban.narratif', label: 'Écrire projet narratif' },
    { id: 'kanban.financement', label: 'Écrire financement' },
    { id: 'atlas.write', label: 'Écrire atlas des mondes' },
    { id: 'users.manage', label: 'Gérer utilisateurs (liste, suppression, reset mot de passe)' },
    { id: 'roles.manage', label: 'Gérer rôles (créer, permissions, assignation)' }
];
const ALL_PERMS = PERMISSIONS.map(p => p.id);
const ROLES_PATH = path.join(__dirname, 'roles.json');
function defaultRolesDoc() {
    return {
        roles: [
            { id: 'architecte', name: 'Architecte', desc: 'Accès total (rôle système, non supprimable).', builtin: true, permissions: ALL_PERMS.slice() },
            { id: 'joueur', name: 'Joueur', desc: 'Aucun accès de gestion.', builtin: true, permissions: [] }
        ],
        assignments: {}
    };
}
function loadRoles() {
    try {
        if (!fs.existsSync(ROLES_PATH)) {
            const doc = defaultRolesDoc();
            fs.writeFileSync(ROLES_PATH, JSON.stringify(doc, null, 2));
            return doc;
        }
        const doc = JSON.parse(fs.readFileSync(ROLES_PATH, 'utf-8'));
        if (!doc || !Array.isArray(doc.roles)) return defaultRolesDoc();
        if (!doc.assignments || typeof doc.assignments !== 'object') doc.assignments = {};
        let migrated = false;
        doc.roles.forEach(r => {
            if (Array.isArray(r.permissions) && r.permissions.includes('config.write')) {
                r.permissions = [...new Set(r.permissions.filter(p => p !== 'config.write').concat(LEGACY_KANBAN_PERMS))];
                migrated = true;
            }
        });
        if (migrated) { try { fs.writeFileSync(ROLES_PATH, JSON.stringify(doc, null, 2)); } catch (e) {} }
        return doc;
    } catch (e) { return defaultRolesDoc(); }
}
const LEGACY_KANBAN_PERMS = ['kanban.roadmaps', 'kanban.revue-rp', 'kanban.narratif', 'kanban.financement', 'atlas.write'];
function saveRoles(doc) {
    try { if (fs.existsSync(ROLES_PATH)) fs.copyFileSync(ROLES_PATH, ROLES_PATH + '.bak'); } catch (e) {}
    fs.writeFileSync(ROLES_PATH, JSON.stringify(doc, null, 2));
}
function getUserPermissions(user) {
    if (!user) return [];
    if (isAdminEmail(user.email)) return ALL_PERMS.slice();
    const doc = loadRoles();
    const rid = doc.assignments[user.id];
    const role = doc.roles.find(r => r.id === rid);
    if (!role || !Array.isArray(role.permissions)) return [];
    return role.permissions.filter(p => ALL_PERMS.includes(p));
}
function roleIdFor(user) {
    if (!user) return 'joueur';
    if (isAdminEmail(user.email)) return 'architecte';
    const doc = loadRoles();
    const rid = doc.assignments[user.id];
    return doc.roles.some(r => r.id === rid) ? rid : 'joueur';
}
function roleNameFor(user) {
    const doc = loadRoles();
    const r = doc.roles.find(x => x.id === roleIdFor(user));
    return r ? r.name : 'Joueur';
}

const PORT = process.env.PORT || 3000;
const DB_PATH = path.join(__dirname, 'users.json');
const PUBLIC_DIR = path.join(__dirname, '..');

const MIME_TYPES = {
    '.html': 'text/html; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.js': 'application/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.gif': 'image/gif',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon',
    '.webp': 'image/webp',
    '.woff': 'font/woff',
    '.woff2': 'font/woff2',
    '.ttf': 'font/ttf'
};

function loadUsers() {
    if (!fs.existsSync(DB_PATH)) {
        fs.writeFileSync(DB_PATH, JSON.stringify([], null, 2));
    }
    try {
        return JSON.parse(fs.readFileSync(DB_PATH, 'utf-8'));
    } catch (e) {
        return [];
    }
}

function saveUsers(users) {
    fs.writeFileSync(DB_PATH, JSON.stringify(users, null, 2));
}

function hashPassword(password, salt) {
    // Accepte un Buffer ou convertit la chaîne hexadécimale en octets réels
    const saltBuffer = Buffer.isBuffer(salt) ? salt : Buffer.from(salt, 'hex');
    return crypto.pbkdf2Sync(password, saltBuffer, 100000, 32, 'sha256').toString('hex');
}

function setCors(req, res) {
    const origin = req.headers.origin;
    if (origin) {
        res.setHeader('Access-Control-Allow-Origin', origin);
        res.setHeader('Access-Control-Allow-Credentials', 'true');
    } else {
        res.setHeader('Access-Control-Allow-Origin', '*');
    }
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Origin, X-Requested-With, Content-Type, Accept, Authorization, ngrok-skip-browser-warning');
}

function sendJson(res, statusCode, data, setCookieHeader = null) {
    const headers = { 'Content-Type': 'application/json; charset=utf-8' };
    if (setCookieHeader) {
        headers['Set-Cookie'] = setCookieHeader;
    }
    res.writeHead(statusCode, headers);
    res.end(JSON.stringify(data));
}

function parseBody(req) {
    return new Promise((resolve, reject) => {
        let raw = '';
        req.on('data', chunk => { raw += chunk; });
        req.on('end', () => {
            if (!raw) return resolve({});
            try {
                resolve(JSON.parse(raw));
            } catch (e) {
                reject(new Error('Format JSON invalide.'));
            }
        });
        req.on('error', reject);
    });
}

function parseCookies(req) {
    const out = {};
    const header = req.headers && req.headers.cookie;
    if (!header) return out;
    for (const part of header.split(';')) {
        const idx = part.indexOf('=');
        if (idx === -1) continue;
        const k = part.slice(0, idx).trim();
        const v = part.slice(idx + 1).trim();
        if (k) { try { out[k] = decodeURIComponent(v); } catch (e) { out[k] = v; } }
    }
    return out;
}

function getSessionUserId(req) {
    // Jeton posé au login/register : 'srv_' + base64('userId:timestamp').
    // Vérifié contre users.json à chaque appel.
    try {
        const token = parseCookies(req).codex_auth_token || '';
        if (!token.startsWith('srv_')) return null;
        const decoded = Buffer.from(token.slice(4), 'base64').toString('utf8');
        const userId = decoded.split(':')[0];
        if (!userId) return null;
        const users = loadUsers();
        return users.some(u => u.id === userId) ? userId : null;
    } catch (e) { return null; }
}

/* ---------- HTTPS JSON helper (native, zero dep) ---------- */
function httpsRequest(url, options = {}) {
    return new Promise((resolve, reject) => {
        const parsed = new NodeURL(url);
        const reqOpts = {
            hostname: parsed.hostname,
            port: parsed.port || 443,
            path: parsed.pathname + parsed.search,
            method: options.method || 'GET',
            headers: options.headers || {}
        };
        const req = https.request(reqOpts, (res) => {
            let body = '';
            res.on('data', chunk => { body += chunk; });
            res.on('end', () => {
                try {
                    resolve({ status: res.statusCode, data: JSON.parse(body) });
                } catch (e) {
                    resolve({ status: res.statusCode, data: body });
                }
            });
        });
        req.on('error', reject);
        if (options.body) req.write(options.body);
        req.end();
    });
}

function parseReturnPath(state) {
    if (!state) return '/';
    try {
        const decoded = Buffer.from(state, 'base64url').toString('utf8');
        const obj = JSON.parse(decoded);
        if (obj && typeof obj.return === 'string' && obj.return.startsWith('/') && !obj.return.startsWith('//')) {
            return obj.return;
        }
    } catch (e) {
        // Fallback si ce n'est pas un JSON encodé
    }
    return '/';
}

function staticCacheHeaders(ext, stats) {
    const h = {};
    if (['.html', '.js', '.json', '.css'].includes(ext)) {
        // Pages + kanban + JSON : toujours revalidés, jamais de version périmée.
        h['Cache-Control'] = 'no-cache, must-revalidate';
    } else {
        h['Cache-Control'] = 'public, max-age=3600';
    }
    if (stats && stats.mtime) h['Last-Modified'] = stats.mtime.toUTCString();
    return h;
}

function isFresh(req, mtime) {
    try {
        const ims = req && req.headers && req.headers['if-modified-since'];
        if (!ims || !mtime) return false;
        const imsT = Math.floor(new Date(ims).getTime() / 1000);
        const fileT = Math.floor(new Date(mtime).getTime() / 1000);
        return Number.isFinite(imsT) && imsT >= fileT;
    } catch (e) { return false; }
}

function serveStatic(req, res, pathname) {
    let safePath = path.normalize(decodeURI(pathname)).replace(/^(\.\.[\/\\])+/, '');
    if (safePath === '/' || safePath === '\\' || safePath === '') {
        safePath = '/index.html';
    }

    let filePath = path.join(PUBLIC_DIR, safePath);

    if (!filePath.startsWith(PUBLIC_DIR)) {
        res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
        return res.end('Accès interdit.');
    }

    fs.stat(filePath, (err, stats) => {
        if (!err && stats.isDirectory()) {
            filePath = path.join(filePath, 'index.html');
            return fs.stat(filePath, (err2, stats2) => {
                if (err2 || !stats2.isFile()) {
                    res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
                    return res.end('Fichier introuvable.');
                }
                const ext = path.extname(filePath).toLowerCase();
                const contentType = MIME_TYPES[ext] || 'application/octet-stream';
                if (isFresh(req, stats2.mtime)) {
                    res.writeHead(304);
                    return res.end();
                }
                res.writeHead(200, Object.assign(
                    { 'Content-Type': contentType },
                    staticCacheHeaders(ext, stats2)
                ));
                fs.createReadStream(filePath).pipe(res);
            });
        }
        if (err || !stats.isFile()) {
            res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
            return res.end('Fichier introuvable.');
        }

        const ext = path.extname(filePath).toLowerCase();
        const contentType = MIME_TYPES[ext] || 'application/octet-stream';
        if (isFresh(req, stats.mtime)) {
            res.writeHead(304);
            return res.end();
        }
        res.writeHead(200, Object.assign(
            { 'Content-Type': contentType },
            staticCacheHeaders(ext, stats)
        ));
        fs.createReadStream(filePath).pipe(res);
    });
}

async function handleRequest(req, res, isAdminListener) {
    setCors(req, res);

    if (req.method === 'OPTIONS') {
        res.writeHead(200);
        return res.end();
    }

    const parsedUrl = new URL(req.url, `http://${req.headers.host || 'localhost'}`);
    const cleanPath = parsedUrl.pathname.replace(/\/+$/, '').toLowerCase();
    const isRoute = (endpoint) => cleanPath === endpoint || cleanPath.endsWith(endpoint);

    console.log(`[HTTP ${req.method}] ${cleanPath}`);

    if (cleanPath === '/health' || cleanPath === '/api/health') {
        return sendJson(res, 200, { status: 'ok' });
    }

    if (isRoute('/api/auth/register')) {
        if (req.method === 'GET') {
            return sendJson(res, 200, { status: 'online', endpoint: '/api/auth/register', method: 'POST' });
        }
        if (req.method === 'POST') {
            try {
                const body = await parseBody(req);
                const { username, email, password } = body;

                if (!username || !email || !password) {
                    return sendJson(res, 400, { error: 'Champs requis manquants.' });
                }

                const users = loadUsers();
                const exists = users.find(u =>
                    u.username.toLowerCase() === username.toLowerCase() ||
                    u.email.toLowerCase() === email.toLowerCase()
                );

                if (exists) {
                    return sendJson(res, 409, { error: 'Cet usager ou cet e-mail existe déjà.' });
                }

                const saltBuffer = crypto.randomBytes(16);
                const salt = saltBuffer.toString('hex');
                const hash = hashPassword(password, saltBuffer);

                const newUser = {
                    id: 'usr_' + Date.now().toString(36),
                    username: username.trim(),
                    email: email.trim().toLowerCase(),
                    saltHex: salt,
                    passwordHashHex: hash,
                    role: isAdminEmail(email) ? 'Architecte' : 'Joueur',
                    provider: 'local',
                    createdAt: new Date().toISOString(),
                    characters: []
                };

                users.push(newUser);
                saveUsers(users);

                const token = 'srv_' + Buffer.from(`${newUser.id}:${Date.now()}`).toString('base64');
                const cookie = `codex_auth_token=${token}; Path=/; Max-Age=604800; SameSite=Lax`;

                const safeUser = toSafeUser(newUser);
                return sendJson(res, 200, { success: true, user: safeUser }, cookie);
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    if (isRoute('/api/auth/login')) {
        if (req.method === 'POST') {
            try {
                const body = await parseBody(req);
                const { identifier, password } = body;

                if (!identifier || !password) {
                    return sendJson(res, 400, { error: 'Identifiant et mot de passe requis.' });
                }

                const users = loadUsers();
                const term = identifier.toLowerCase().trim();
                const user = users.find(u => u.username.toLowerCase() === term || u.email.toLowerCase() === term);

                if (!user || user.provider !== 'local') {
                    return sendJson(res, 401, { error: 'Identifiants invalides.' });
                }

                const computed = hashPassword(password, user.saltHex);
                if (computed !== user.passwordHashHex) {
                    return sendJson(res, 401, { error: 'Identifiants invalides.' });
                }

                const token = 'srv_' + Buffer.from(`${user.id}:${Date.now()}`).toString('base64');
                const cookie = `codex_auth_token=${token}; Path=/; Max-Age=604800; SameSite=Lax`;

                const safeUser = toSafeUser(user);
                return sendJson(res, 200, { success: true, user: safeUser }, cookie);
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    if (isRoute('/api/auth/social')) {
        if (req.method === 'POST') {
            try {
                const body = await parseBody(req);
                const { provider, providerId, email, name, avatar } = body;

                if (!provider || !providerId) {
                    return sendJson(res, 400, { error: 'Fournisseur ou identifiant manquant.' });
                }

                const users = loadUsers();
                let user = users.find(u => u.provider === provider && u.providerId === providerId);

                if (!user && email) {
                    user = users.find(u => u.email.toLowerCase() === email.toLowerCase());
                    if (user) {
                        user.provider = provider;
                        user.providerId = providerId;
                        if (avatar) user.avatar = avatar;
                    }
                }

                if (!user) {
                    const cleanName = (name || 'Operateur_' + Math.floor(Math.random() * 1000)).replace(/\s+/g, '_');
                    user = {
                        id: 'usr_' + provider + '_' + Date.now().toString(36),
                        username: cleanName,
                        email: email || `${cleanName.toLowerCase()}@${provider}.auth`,
                        role: 'Joueur',
                        provider: provider,
                        providerId: providerId,
                        avatar: avatar || null,
                        createdAt: new Date().toISOString(),
                        characters: []
                    };
                    users.push(user);
                }

                saveUsers(users);

                const token = 'srv_' + Buffer.from(`${user.id}:${Date.now()}`).toString('base64');
                const cookie = `codex_auth_token=${token}; Path=/; Max-Age=604800; SameSite=Lax`;

                const safeUser = toSafeUser(user);
                return sendJson(res, 200, { success: true, user: safeUser }, cookie);
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    if (isRoute('/api/auth/logout')) {
        if (req.method === 'POST') {
            const cookie = `codex_auth_token=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT; SameSite=Lax`;
            return sendJson(res, 200, { success: true }, cookie);
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    if (isRoute('/api/auth/me')) {
        if (req.method === 'GET') {
            const u = getSessionUser(req);
            if (!u) return sendJson(res, 401, { error: 'Connexion requise.' });
            return sendJson(res, 200, { success: true, user: toSafeUser(u) });
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    /* PUT /api/auth/avatar — avatar perso (URL http/https ou upload
       base64 ≤ ~512 Ko, affiché en chat + popups + en-ligne + amis).
       {imageUrl} | {imageData} | {clear:true}. */
    const AVATAR_MAX_CHARS = 700000;
    if (isRoute('/api/auth/avatar')) {
        if (req.method !== 'PUT' && req.method !== 'POST') {
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }
        const me = getSessionUser(req);
        if (!me) return sendJson(res, 401, { error: 'Connexion requise.' });
        try {
            const body = await parseBody(req);
            const users = loadUsers();
            const target = users.find(u => u.id === me.id);
            if (!target) return sendJson(res, 401, { error: 'Session invalide.' });
            if (body && body.clear) {
                delete target.avatar;
            } else {
                const imageUrl = String((body && body.imageUrl) || '').trim().slice(0, 2048);
                const imageData = String((body && body.imageData) || '');
                let av = null;
                if (imageUrl) {
                    let ok = false;
                    try {
                        const u = new NodeURL(imageUrl);
                        ok = (u.protocol === 'http:' || u.protocol === 'https:');
                    } catch (e) {}
                    if (!ok) return sendJson(res, 400, { error: 'URL d’avatar invalide (http/https requis).' });
                    av = imageUrl;
                } else if (imageData) {
                    const m = /^data:image\/(png|jpe?g|gif|webp);base64,[A-Za-z0-9+/=]+$/.exec(imageData);
                    if (!m) return sendJson(res, 400, { error: 'Avatar invalide (data:image/png|jpeg|gif|webp;base64).' });
                    if (imageData.length > AVATAR_MAX_CHARS) {
                        return sendJson(res, 413, { error: 'Avatar trop lourd (max ~512 Ko).' });
                    }
                    av = imageData;
                } else {
                    return sendJson(res, 400, { error: 'imageUrl, imageData ou clear requis.' });
                }
                target.avatar = av;
            }
            saveUsers(users);
            return sendJson(res, 200, { success: true, user: toSafeUser(target) });
        } catch (e) {
            return sendJson(res, 400, { error: e.message });
        }
    }

    /* ================================================================
       FICHES PERSONNAGES — sauvegarde serveur par utilisateur
       ----------------------------------------------------------------
       GET /api/characters : relit les onglets stockés (user.characters).
       PUT /api/characters : remplace user.characters par les onglets
       envoyés. Le client transfère son local vers ici à la connexion,
       puis pousse en autosave tant que la session est active.
       Auth : cookie srv_ (cf. login). Sans serveur, le client reste
       en mode "local seul" (localStorage).
       ================================================================ */
    if (isRoute('/api/characters')) {
        const sessionUserId = getSessionUserId(req);
        if (!sessionUserId) {
            return sendJson(res, 401, { error: 'Connexion requise.' });
        }
        const users = loadUsers();
        const user = users.find(u => u.id === sessionUserId);
        if (!user) {
            return sendJson(res, 401, { error: 'Session invalide.' });
        }
        if (req.method === 'GET') {
            const tabs = Array.isArray(user.characters) ? user.characters : [];
            return sendJson(res, 200, { success: true, tabs });
        }
        if (req.method === 'PUT' || req.method === 'POST') {
            try {
                const body = await parseBody(req);
                if (!body || !Array.isArray(body.tabs)) {
                    return sendJson(res, 400, { error: 'tabs[] manquant.' });
                }
                if (body.tabs.length > 24) {
                    return sendJson(res, 413, { error: 'Trop d\'onglets (max 24).' });
                }
                const json = JSON.stringify(body.tabs);
                if (json.length > 512 * 1024) {
                    return sendJson(res, 413, { error: 'Payload trop volumineux.' });
                }
                for (const t of body.tabs) {
                    if (!t || typeof t.id !== 'string' || typeof t.data !== 'object' || !t.data) {
                        return sendJson(res, 400, { error: 'Format d\'onglet invalide.' });
                    }
                }
                user.characters = body.tabs.map(t => ({
                    id: String(t.id).slice(0, 64),
                    label: typeof t.label === 'string' ? t.label.slice(0, 40) : '',
                    customLabel: !!t.customLabel,
                    updatedAt: Number(t.updatedAt) || Date.now(),
                    data: t.data
                }));
                saveUsers(users);
                console.log(`[Characters] sauvegarde serveur : ${user.username} (${user.characters.length} fiche(s)).`);
                return sendJson(res, 200, { success: true, tabs: user.characters.length });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    /* ================================================================
       GOOGLE OAUTH 2.0
       ================================================================ */

    // Étape 1 : Rediriger l'utilisateur vers Google
    if (isRoute('/api/oauth/google')) {
        if (req.method === 'GET') {
            if (!GOOGLE_CLIENT_ID) {
                return sendJson(res, 500, { error: 'GOOGLE_CLIENT_ID non configuré dans .env' });
            }
            const redirectUri = `${OAUTH_CALLBACK_BASE}/api/oauth/google/callback`;
            const returnParam = parsedUrl.searchParams.get('return') || '/';
            const state = Buffer.from(JSON.stringify({
                csrf: crypto.randomBytes(16).toString('hex'),
                return: returnParam
            })).toString('base64url');

            const params = new NodeURLSearchParams({
                client_id: GOOGLE_CLIENT_ID,
                redirect_uri: redirectUri,
                response_type: 'code',
                scope: 'openid email profile',
                access_type: 'online',
                state: state
            });
            const googleAuthUrl = `https://accounts.google.com/o/oauth2/v2/auth?${params.toString()}`;
            console.log(`[OAuth Google] Redirection vers Google (retour: ${returnParam})...`);
            res.writeHead(302, { Location: googleAuthUrl });
            return res.end();
        }
    }

    // Étape 2 : Callback — Google redirige ici avec un code
    if (isRoute('/api/oauth/google/callback')) {
        if (req.method === 'GET') {
            const code = parsedUrl.searchParams.get('code');
            const error = parsedUrl.searchParams.get('error');
            const state = parsedUrl.searchParams.get('state');
            const returnPath = parseReturnPath(state);

            if (error || !code) {
                console.error(`[OAuth Google] Erreur: ${error || 'code manquant'}`);
                res.writeHead(302, { Location: `${returnPath}#oauth_error=google_denied` });
                return res.end();
            }

            try {
                // Échanger le code contre un access_token
                const redirectUri = `${OAUTH_CALLBACK_BASE}/api/oauth/google/callback`;
                const tokenBody = new NodeURLSearchParams({
                    code: code,
                    client_id: GOOGLE_CLIENT_ID,
                    client_secret: GOOGLE_CLIENT_SECRET,
                    redirect_uri: redirectUri,
                    grant_type: 'authorization_code'
                }).toString();

                const tokenRes = await httpsRequest('https://oauth2.googleapis.com/token', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'Content-Length': Buffer.byteLength(tokenBody)
                    },
                    body: tokenBody
                });

                if (!tokenRes.data.access_token) {
                    console.error('[OAuth Google] Token invalide:', tokenRes.data);
                    res.writeHead(302, { Location: `${returnPath}#oauth_error=token_failed` });
                    return res.end();
                }

                // Récupérer le profil Google
                const profileRes = await httpsRequest('https://www.googleapis.com/oauth2/v2/userinfo', {
                    headers: { Authorization: `Bearer ${tokenRes.data.access_token}` }
                });

                const gProfile = profileRes.data;
                console.log(`[OAuth Google] Profil reçu: ${gProfile.name} (${gProfile.email})`);

                // Créer ou connecter l'utilisateur (réutilise la logique social existante)
                const users = loadUsers();
                let user = users.find(u => u.provider === 'google' && u.providerId === gProfile.id);

                if (!user && gProfile.email) {
                    user = users.find(u => u.email.toLowerCase() === gProfile.email.toLowerCase());
                    if (user) {
                        user.provider = 'google';
                        user.providerId = gProfile.id;
                        if (gProfile.picture) user.avatar = gProfile.picture;
                    }
                }

                if (!user) {
                    const cleanName = (gProfile.name || 'GoogleUser').replace(/\s+/g, '_');
                    user = {
                        id: 'usr_google_' + Date.now().toString(36),
                        username: cleanName,
                        email: gProfile.email || `${cleanName.toLowerCase()}@google.auth`,
                        role: 'Joueur',
                        provider: 'google',
                        providerId: gProfile.id,
                        avatar: gProfile.picture || null,
                        createdAt: new Date().toISOString(),
                        characters: []
                    };
                    users.push(user);
                }

                saveUsers(users);

                // Créer le token de session
                const token = 'srv_' + Buffer.from(`${user.id}:${Date.now()}`).toString('base64');
                const cookie = `codex_auth_token=${token}; Path=/; Max-Age=604800; SameSite=Lax`;

                // Encoder les infos user pour que le client les récupère
                const safeUser = toSafeUser(user);
                const userParam = encodeURIComponent(Buffer.from(JSON.stringify(safeUser)).toString('base64'));

                console.log(`[OAuth Google] Connexion réussie pour ${user.username}, redirection vers ${returnPath}`);
                res.writeHead(302, {
                    Location: `${returnPath}#oauth_success=${userParam}`,
                    'Set-Cookie': cookie
                });
                return res.end();

            } catch (err) {
                console.error('[OAuth Google] Erreur callback:', err);
                res.writeHead(302, { Location: `${returnPath}#oauth_error=server_error` });
                return res.end();
            }
        }
    }

    /* ================================================================
       DISCORD OAUTH 2.0
       ================================================================ */

    if (isRoute('/api/oauth/discord')) {
        if (req.method === 'GET') {
            if (!DISCORD_CLIENT_ID) {
                return sendJson(res, 500, { error: 'DISCORD_CLIENT_ID non configuré dans .env' });
            }
            const redirectUri = `${OAUTH_CALLBACK_BASE}/api/oauth/discord/callback`;
            const returnParam = parsedUrl.searchParams.get('return') || '/';
            const state = Buffer.from(JSON.stringify({
                csrf: crypto.randomBytes(16).toString('hex'),
                return: returnParam
            })).toString('base64url');

            const params = new NodeURLSearchParams({
                client_id: DISCORD_CLIENT_ID,
                redirect_uri: redirectUri,
                response_type: 'code',
                scope: 'identify email',
                state: state
            });
            console.log(`[OAuth Discord] Redirection vers Discord (retour: ${returnParam})...`);
            res.writeHead(302, { Location: `https://discord.com/oauth2/authorize?${params.toString()}` });
            return res.end();
        }
    }

    if (isRoute('/api/oauth/discord/callback')) {
        if (req.method === 'GET') {
            const code = parsedUrl.searchParams.get('code');
            const error = parsedUrl.searchParams.get('error');
            const state = parsedUrl.searchParams.get('state');
            const returnPath = parseReturnPath(state);

            if (error || !code) {
                console.error(`[OAuth Discord] Erreur: ${error || 'code manquant'}`);
                res.writeHead(302, { Location: `${returnPath}#oauth_error=discord_denied` });
                return res.end();
            }

            try {
                const redirectUri = `${OAUTH_CALLBACK_BASE}/api/oauth/discord/callback`;
                const tokenBody = new NodeURLSearchParams({
                    code: code,
                    client_id: DISCORD_CLIENT_ID,
                    client_secret: DISCORD_CLIENT_SECRET,
                    redirect_uri: redirectUri,
                    grant_type: 'authorization_code'
                }).toString();

                const tokenRes = await httpsRequest('https://discord.com/api/oauth2/token', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'Content-Length': Buffer.byteLength(tokenBody)
                    },
                    body: tokenBody
                });

                if (!tokenRes.data.access_token) {
                    console.error('[OAuth Discord] Token invalide:', tokenRes.data);
                    res.writeHead(302, { Location: `${returnPath}#oauth_error=token_failed` });
                    return res.end();
                }

                const profileRes = await httpsRequest('https://discord.com/api/users/@me', {
                    headers: { Authorization: `Bearer ${tokenRes.data.access_token}` }
                });

                const dProfile = profileRes.data;
                console.log(`[OAuth Discord] Profil reçu: ${dProfile.username}#${dProfile.discriminator}`);

                const users = loadUsers();
                let user = users.find(u => u.provider === 'discord' && u.providerId === dProfile.id);

                if (!user && dProfile.email) {
                    user = users.find(u => u.email.toLowerCase() === dProfile.email.toLowerCase());
                    if (user) {
                        user.provider = 'discord';
                        user.providerId = dProfile.id;
                        user.avatar = dProfile.avatar ? `https://cdn.discordapp.com/avatars/${dProfile.id}/${dProfile.avatar}.png` : user.avatar;
                    }
                }

                if (!user) {
                    user = {
                        id: 'usr_discord_' + Date.now().toString(36),
                        username: dProfile.username || 'DiscordUser',
                        email: dProfile.email || `${dProfile.username.toLowerCase()}@discord.auth`,
                        role: 'Joueur',
                        provider: 'discord',
                        providerId: dProfile.id,
                        avatar: dProfile.avatar ? `https://cdn.discordapp.com/avatars/${dProfile.id}/${dProfile.avatar}.png` : null,
                        createdAt: new Date().toISOString(),
                        characters: []
                    };
                    users.push(user);
                }

                saveUsers(users);

                const token = 'srv_' + Buffer.from(`${user.id}:${Date.now()}`).toString('base64');
                const cookie = `codex_auth_token=${token}; Path=/; Max-Age=604800; SameSite=Lax`;
                const safeUser = toSafeUser(user);
                const userParam = encodeURIComponent(Buffer.from(JSON.stringify(safeUser)).toString('base64'));

                console.log(`[OAuth Discord] Connexion réussie pour ${user.username}, redirection vers ${returnPath}`);
                res.writeHead(302, {
                    Location: `${returnPath}#oauth_success=${userParam}`,
                    'Set-Cookie': cookie
                });
                return res.end();

            } catch (err) {
                console.error('[OAuth Discord] Erreur callback:', err);
                res.writeHead(302, { Location: `${returnPath}#oauth_error=server_error` });
                return res.end();
            }
        }
    }

    /* ================================================================
       GITHUB OAUTH 2.0
       ================================================================ */

    if (isRoute('/api/oauth/github')) {
        if (req.method === 'GET') {
            if (!GITHUB_CLIENT_ID) {
                return sendJson(res, 500, { error: 'GITHUB_CLIENT_ID non configuré dans .env' });
            }
            const redirectUri = `${OAUTH_CALLBACK_BASE}/api/oauth/github/callback`;
            const returnParam = parsedUrl.searchParams.get('return') || '/';
            const state = Buffer.from(JSON.stringify({
                csrf: crypto.randomBytes(16).toString('hex'),
                return: returnParam
            })).toString('base64url');

            const params = new NodeURLSearchParams({
                client_id: GITHUB_CLIENT_ID,
                redirect_uri: redirectUri,
                scope: 'user:email',
                state: state
            });
            console.log(`[OAuth GitHub] Redirection vers GitHub (retour: ${returnParam})...`);
            res.writeHead(302, { Location: `https://github.com/login/oauth/authorize?${params.toString()}` });
            return res.end();
        }
    }

    if (isRoute('/api/oauth/github/callback')) {
        if (req.method === 'GET') {
            const code = parsedUrl.searchParams.get('code');
            const error = parsedUrl.searchParams.get('error');
            const state = parsedUrl.searchParams.get('state');
            const returnPath = parseReturnPath(state);

            if (error || !code) {
                console.error(`[OAuth GitHub] Erreur: ${error || 'code manquant'}`);
                res.writeHead(302, { Location: `${returnPath}#oauth_error=github_denied` });
                return res.end();
            }

            try {
                const redirectUri = `${OAUTH_CALLBACK_BASE}/api/oauth/github/callback`;
                const tokenBody = new NodeURLSearchParams({
                    code: code,
                    client_id: GITHUB_CLIENT_ID,
                    client_secret: GITHUB_CLIENT_SECRET,
                    redirect_uri: redirectUri
                }).toString();

                const tokenRes = await httpsRequest('https://github.com/login/oauth/access_token', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'Accept': 'application/json',
                        'Content-Length': Buffer.byteLength(tokenBody)
                    },
                    body: tokenBody
                });

                if (!tokenRes.data.access_token) {
                    console.error('[OAuth GitHub] Token invalide:', tokenRes.data);
                    res.writeHead(302, { Location: `${returnPath}#oauth_error=token_failed` });
                    return res.end();
                }

                const profileRes = await httpsRequest('https://api.github.com/user', {
                    headers: {
                        Authorization: `Bearer ${tokenRes.data.access_token}`,
                        'User-Agent': 'KilltimeCodex',
                        'Accept': 'application/json'
                    }
                });

                const ghProfile = profileRes.data;

                // GitHub ne retourne pas toujours l'email dans /user, on va le chercher
                let ghEmail = ghProfile.email;
                if (!ghEmail) {
                    try {
                        const emailRes = await httpsRequest('https://api.github.com/user/emails', {
                            headers: {
                                Authorization: `Bearer ${tokenRes.data.access_token}`,
                                'User-Agent': 'KilltimeCodex',
                                'Accept': 'application/json'
                            }
                        });
                        if (Array.isArray(emailRes.data)) {
                            const primary = emailRes.data.find(e => e.primary) || emailRes.data[0];
                            if (primary) ghEmail = primary.email;
                        }
                    } catch (e) { /* email optionnel */ }
                }

                console.log(`[OAuth GitHub] Profil reçu: ${ghProfile.login} (${ghEmail})`);

                const users = loadUsers();
                let user = users.find(u => u.provider === 'github' && u.providerId === String(ghProfile.id));

                if (!user && ghEmail) {
                    user = users.find(u => u.email.toLowerCase() === ghEmail.toLowerCase());
                    if (user) {
                        user.provider = 'github';
                        user.providerId = String(ghProfile.id);
                        if (ghProfile.avatar_url) user.avatar = ghProfile.avatar_url;
                    }
                }

                if (!user) {
                    user = {
                        id: 'usr_github_' + Date.now().toString(36),
                        username: ghProfile.login || 'GitHubUser',
                        email: ghEmail || `${ghProfile.login.toLowerCase()}@github.auth`,
                        role: 'Joueur',
                        provider: 'github',
                        providerId: String(ghProfile.id),
                        avatar: ghProfile.avatar_url || null,
                        createdAt: new Date().toISOString(),
                        characters: []
                    };
                    users.push(user);
                }

                saveUsers(users);

                const token = 'srv_' + Buffer.from(`${user.id}:${Date.now()}`).toString('base64');
                const cookie = `codex_auth_token=${token}; Path=/; Max-Age=604800; SameSite=Lax`;
                const safeUser = toSafeUser(user);
                const userParam = encodeURIComponent(Buffer.from(JSON.stringify(safeUser)).toString('base64'));

                console.log(`[OAuth GitHub] Connexion réussie pour ${user.username}, redirection vers ${returnPath}`);
                res.writeHead(302, {
                    Location: `${returnPath}#oauth_success=${userParam}`,
                    'Set-Cookie': cookie
                });
                return res.end();

            } catch (err) {
                console.error('[OAuth GitHub] Erreur callback:', err);
                res.writeHead(302, { Location: `${returnPath}#oauth_error=server_error` });
                return res.end();
            }
        }
    }

    if (isRoute('/api/vtt/rooms')) {
        if (req.method === 'GET') {
            // Annuaire public des tables (server browser sans tiers).
            // ?build=X.Y.Z filtre sur la version de build Unity (recommandé :
            // le client n'affiche que les tables compatibles avec son build).
            // Ne publie que les rooms `public` — jamais de clientIds ni d'IPs.
            const buildFilter = (parsedUrl.searchParams.get('build') || '').trim().slice(0, 32);
            const rooms = [];
            for (const room of vttRooms.values()) {
                if (room.visibility === 'private') continue;
                if (room.members.size === 0) continue;
                if (buildFilter && (room.buildVersion || 'unknown') !== buildFilter) continue;
                if (vttPolicy) rooms.push(vttPolicy.roomDirectoryEntry(room));
                else {
                    const gm = room.members.get(room.gmId);
                    rooms.push({
                        code: room.code,
                        tableName: room.tableName || room.code,
                        buildVersion: room.buildVersion || 'unknown',
                        gmName: gm ? gm.username : '?',
                        players: room.members.size,
                        maxPlayers: room.maxPlayers || 6,
                        createdAt: room.createdAt,
                        lastActive: room.lastActive || room.createdAt
                    });
                }
            }
            rooms.sort((a, b) => b.lastActive - a.lastActive);
            return sendJson(res, 200, {
                rooms: rooms.slice(0, 100),
                count: Math.min(rooms.length, 100),
                build: buildFilter || null
            });
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    if (isRoute('/api/vtt/status')) {
        if (req.method === 'GET') {
            return sendJson(res, 200, {
                online: true,
                service: 'vtt-room-hub',
                wsPath: '/ws/vtt',
                protocol: 1,
                rooms: vttRooms.size,
                publicRooms: [...vttRooms.values()].filter(r => r.visibility !== 'private').length,
                clients: vttClients.size,
                uptimeSec: Math.floor(process.uptime())
            });
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    /* ================================================================
       ÉCRITURES DISQUE — session admin exigée (401 sans login, 403 si non-admin).
       Les clients kanban replient en localStorage sur 401/403, sans erreur visible.
       ================================================================ */
    if ((req.method === 'PUT' || req.method === 'POST')) {
        const g = WRITE_GUARDS.find(g => isRoute(g.route));
        if (g) {
            const u = getSessionUser(req);
            if (!u) return sendJson(res, 401, { error: 'Connexion requise.' });
            if (!getUserPermissions(u).includes(g.perm)) return sendJson(res, 403, { error: 'Permission requise : ' + g.perm + '.' });
        }
    }

    /* ================================================================
       ROADMAP SOLO — autosave disque (PUT /api/roadmap)
       ----------------------------------------------------------------
       Le kanban roadmap.html pousse son état ici (debounced) quand le
       site est servi par ce serveur Node. Écrit data/roadmap.json avec
       une copie .bak. Sans ce serveur (Ruby/python/file://), le client
       bascule en mode "local seul" (localStorage + export manuel).
       ================================================================ */
    const ROADMAP_PATH = path.join(PUBLIC_DIR, 'data', 'roadmap.json');
    if (isRoute('/api/roadmap')) {
        if (req.method === 'PUT' || req.method === 'POST') {
            try {
                const body = await parseBody(req);
                if (!body || !Array.isArray(body.tasks)) {
                    return sendJson(res, 400, { error: 'tasks[] manquant.' });
                }
                const json = JSON.stringify(body, null, 2);
                if (json.length > 2 * 1024 * 1024) {
                    return sendJson(res, 413, { error: 'Payload trop volumineux.' });
                }
                try { fs.mkdirSync(path.dirname(ROADMAP_PATH), { recursive: true }); } catch (e) { /* ignore */ }
                try {
                    if (fs.existsSync(ROADMAP_PATH)) {
                        fs.copyFileSync(ROADMAP_PATH, ROADMAP_PATH + '.bak');
                    }
                } catch (e) { /* backup optionnel */ }
                fs.writeFileSync(ROADMAP_PATH, json);
                console.log(`[Roadmap] autosave disque : ${body.tasks.length} tâches.`);
                return sendJson(res, 200, { success: true, tasks: body.tasks.length });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    /* ROADMAPS MULTI-BOARDS — autosave disque (PUT /api/roadmaps) */
    const ROADMAPS_PATH = path.join(PUBLIC_DIR, 'data', 'roadmaps.json');
    if (isRoute('/api/roadmaps')) {
        if (req.method === 'GET' || req.method === 'HEAD') {
            // Lecture fraîche pour le kanban : jamais cachée (le client
            // réconcilie localStorage vs disque au chargement).
            try {
                if (!fs.existsSync(ROADMAPS_PATH)) {
                    return sendJson(res, 404, { error: 'roadmaps.json introuvable.' });
                }
                const st = fs.statSync(ROADMAPS_PATH);
                const raw = fs.readFileSync(ROADMAPS_PATH, 'utf-8');
                res.writeHead(200, {
                    'Content-Type': 'application/json; charset=utf-8',
                    'Cache-Control': 'no-store, must-revalidate',
                    'Last-Modified': st.mtime.toUTCString()
                });
                if (req.method === 'HEAD') return res.end();
                return res.end(raw);
            } catch (e) {
                return sendJson(res, 500, { error: e.message });
            }
        }
        if (req.method === 'PUT' || req.method === 'POST') {
            try {
                const body = await parseBody(req);
                if (!body || !Array.isArray(body.boards)) {
                    return sendJson(res, 400, { error: 'boards[] manquant.' });
                }
                const json = JSON.stringify(body, null, 2);
                if (json.length > 4 * 1024 * 1024) {
                    return sendJson(res, 413, { error: 'Payload trop volumineux.' });
                }
                try { fs.mkdirSync(path.dirname(ROADMAPS_PATH), { recursive: true }); } catch (e) { /* ignore */ }
                try {
                    if (fs.existsSync(ROADMAPS_PATH)) {
                        fs.copyFileSync(ROADMAPS_PATH, ROADMAPS_PATH + '.bak');
                    }
                } catch (e) { /* backup optionnel */ }
                fs.writeFileSync(ROADMAPS_PATH, json);
                const n = body.boards.reduce((m, b) => m + (Array.isArray(b.tasks) ? b.tasks.length : 0), 0);
                console.log(`[Roadmaps] autosave disque : ${body.boards.length} boards, ${n} tâches.`);
                return sendJson(res, 200, { success: true, boards: body.boards.length, tasks: n });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    /* FINANCEMENT KICKSTARTER — autosave disque (PUT /api/financement) */
    const FINANCEMENT_PATH = path.join(PUBLIC_DIR, 'data', 'financement.json');
    if (isRoute('/api/financement')) {
        if (req.method === 'GET' || req.method === 'HEAD') {
            // Lecture fraîche pour le kanban financement : jamais cachée (le client
            // réconcilie localStorage vs disque au chargement).
            try {
                if (!fs.existsSync(FINANCEMENT_PATH)) {
                    return sendJson(res, 404, { error: 'financement.json introuvable.' });
                }
                const st = fs.statSync(FINANCEMENT_PATH);
                const raw = fs.readFileSync(FINANCEMENT_PATH, 'utf-8');
                res.writeHead(200, {
                    'Content-Type': 'application/json; charset=utf-8',
                    'Cache-Control': 'no-store, must-revalidate',
                    'Last-Modified': st.mtime.toUTCString()
                });
                if (req.method === 'HEAD') return res.end();
                return res.end(raw);
            } catch (e) {
                return sendJson(res, 500, { error: e.message });
            }
        }
        if (req.method === 'PUT' || req.method === 'POST') {
            try {
                const body = await parseBody(req);
                if (!body || !Array.isArray(body.boards)) {
                    return sendJson(res, 400, { error: 'boards[] manquant.' });
                }
                const json = JSON.stringify(body, null, 2);
                if (json.length > 4 * 1024 * 1024) {
                    return sendJson(res, 413, { error: 'Payload trop volumineux.' });
                }
                try { fs.mkdirSync(path.dirname(FINANCEMENT_PATH), { recursive: true }); } catch (e) { /* ignore */ }
                try {
                    if (fs.existsSync(FINANCEMENT_PATH)) {
                        fs.copyFileSync(FINANCEMENT_PATH, FINANCEMENT_PATH + '.bak');
                    }
                } catch (e) { /* backup optionnel */ }
                fs.writeFileSync(FINANCEMENT_PATH, json);
                const n = body.boards.reduce((m, b) => m + (Array.isArray(b.tasks) ? b.tasks.length : 0), 0);
                console.log(`[Financement] autosave disque : ${body.boards.length} boards, ${n} tâches.`);
                return sendJson(res, 200, { success: true, boards: body.boards.length, tasks: n });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    /* REVUE SYSTÈME RP — kanban indépendant (PUT /api/kanban-rp), séparé du kanban Tactics */
    const KANBAN_RP_PATH = path.join(PUBLIC_DIR, 'data', 'kanban_rp.json');
    if (isRoute('/api/kanban-rp')) {
        if (req.method === 'GET' || req.method === 'HEAD') {
            // Lecture fraîche pour le kanban revue RP : jamais cachée (le client
            // réconcilie localStorage vs disque au chargement).
            try {
                if (!fs.existsSync(KANBAN_RP_PATH)) {
                    return sendJson(res, 404, { error: 'kanban_rp.json introuvable.' });
                }
                const st = fs.statSync(KANBAN_RP_PATH);
                const raw = fs.readFileSync(KANBAN_RP_PATH, 'utf-8');
                res.writeHead(200, {
                    'Content-Type': 'application/json; charset=utf-8',
                    'Cache-Control': 'no-store, must-revalidate',
                    'Last-Modified': st.mtime.toUTCString()
                });
                if (req.method === 'HEAD') return res.end();
                return res.end(raw);
            } catch (e) {
                return sendJson(res, 500, { error: e.message });
            }
        }
        if (req.method === 'PUT' || req.method === 'POST') {
            try {
                const body = await parseBody(req);
                if (!body || !Array.isArray(body.boards)) {
                    return sendJson(res, 400, { error: 'boards[] manquant.' });
                }
                const json = JSON.stringify(body, null, 2);
                if (json.length > 4 * 1024 * 1024) {
                    return sendJson(res, 413, { error: 'Payload trop volumineux.' });
                }
                try { fs.mkdirSync(path.dirname(KANBAN_RP_PATH), { recursive: true }); } catch (e) { /* ignore */ }
                try {
                    if (fs.existsSync(KANBAN_RP_PATH)) {
                        fs.copyFileSync(KANBAN_RP_PATH, KANBAN_RP_PATH + '.bak');
                    }
                } catch (e) { /* backup optionnel */ }
                fs.writeFileSync(KANBAN_RP_PATH, json);
                const n = body.boards.reduce((m, b) => m + (Array.isArray(b.tasks) ? b.tasks.length : 0), 0);
                console.log(`[KanbanRP] autosave disque : ${body.boards.length} boards, ${n} tâches.`);
                return sendJson(res, 200, { success: true, boards: body.boards.length, tasks: n });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    /* PROJET NARRATIF — kanban indépendant (PUT /api/kanban-narratif), source unique des 2 vues jumelles */
    const KANBAN_NARRATIF_PATH = path.join(PUBLIC_DIR, 'data', 'kanban_narratif.json');
    if (isRoute('/api/kanban-narratif')) {
        if (req.method === 'GET' || req.method === 'HEAD') {
            // Lecture fraîche pour le kanban narratif : jamais cachée (le client
            // réconcilie localStorage vs disque au chargement).
            try {
                if (!fs.existsSync(KANBAN_NARRATIF_PATH)) {
                    return sendJson(res, 404, { error: 'kanban_narratif.json introuvable.' });
                }
                const st = fs.statSync(KANBAN_NARRATIF_PATH);
                const raw = fs.readFileSync(KANBAN_NARRATIF_PATH, 'utf-8');
                res.writeHead(200, {
                    'Content-Type': 'application/json; charset=utf-8',
                    'Cache-Control': 'no-store, must-revalidate',
                    'Last-Modified': st.mtime.toUTCString()
                });
                if (req.method === 'HEAD') return res.end();
                return res.end(raw);
            } catch (e) {
                return sendJson(res, 500, { error: e.message });
            }
        }
        if (req.method === 'PUT' || req.method === 'POST') {
            try {
                const body = await parseBody(req);
                if (!body || !Array.isArray(body.boards)) {
                    return sendJson(res, 400, { error: 'boards[] manquant.' });
                }
                const json = JSON.stringify(body, null, 2);
                if (json.length > 4 * 1024 * 1024) {
                    return sendJson(res, 413, { error: 'Payload trop volumineux.' });
                }
                try { fs.mkdirSync(path.dirname(KANBAN_NARRATIF_PATH), { recursive: true }); } catch (e) { /* ignore */ }
                try {
                    if (fs.existsSync(KANBAN_NARRATIF_PATH)) {
                        fs.copyFileSync(KANBAN_NARRATIF_PATH, KANBAN_NARRATIF_PATH + '.bak');
                    }
                } catch (e) { /* backup optionnel */ }
                fs.writeFileSync(KANBAN_NARRATIF_PATH, json);
                const n = body.boards.reduce((m, b) => m + (Array.isArray(b.tasks) ? b.tasks.length : 0), 0);
                console.log(`[KanbanNarratif] autosave disque : ${body.boards.length} boards, ${n} tâches.`);
                return sendJson(res, 200, { success: true, boards: body.boards.length, tasks: n });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    /* ATLAS DES MONDES — autosave disque (PUT /api/atlas-mondes) + sonde (GET) */
    const ATLAS_PATH = path.join(PUBLIC_DIR, 'data', 'atlas_mondes.json');
    if (isRoute('/api/atlas-mondes')) {
        if (req.method === 'GET' || req.method === 'HEAD') {
            // Sonde de présence pour la pastille d'état de la page Atlas.
            try {
                const raw = fs.readFileSync(ATLAS_PATH, 'utf-8');
                const n = (JSON.parse(raw).mondes || []).length;
                if (req.method === 'HEAD') { res.writeHead(200); return res.end(); }
                return sendJson(res, 200, { success: true, endpoint: '/api/atlas-mondes', mondes: n });
            } catch (e) {
                return sendJson(res, 500, { error: e.message });
            }
        }
        if (req.method === 'PUT' || req.method === 'POST') {
            try {
                const body = await parseBody(req);
                if (!body || !Array.isArray(body.mondes)) {
                    return sendJson(res, 400, { error: 'mondes[] manquant.' });
                }
                const json = JSON.stringify(body, null, 2);
                if (json.length > 4 * 1024 * 1024) {
                    return sendJson(res, 413, { error: 'Payload trop volumineux.' });
                }
                try { fs.mkdirSync(path.dirname(ATLAS_PATH), { recursive: true }); } catch (e) { /* ignore */ }
                try {
                    if (fs.existsSync(ATLAS_PATH)) {
                        fs.copyFileSync(ATLAS_PATH, ATLAS_PATH + '.bak');
                    }
                } catch (e) { /* backup optionnel */ }
                fs.writeFileSync(ATLAS_PATH, json);
                console.log(`[Atlas] autosave disque : ${body.mondes.length} entrées.`);
                return sendJson(res, 200, { success: true, mondes: body.mondes.length });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        return sendJson(res, 405, { error: 'Méthode non autorisée.' });
    }

    /* ================================================================
       PRÉSENCE LAN — qui est connecté au site (groupe autour d'une table)
       ----------------------------------------------------------------
       Mémoire seule (éphémère, expire après 45 s sans heartbeat) :
       POST /api/presence (session requise) = heartbeat + renvoie la liste.
       GET  /api/presence = liste {userId, username, since}.
       Aucune IP exposée. Pensé pour le jeu sur wifi local : tout le
       groupe ouvre http://<IP-du-PC>:3000 et se voit en ligne.
       ================================================================ */
    const isPresenceRoute = cleanPath === '/api/presence';
    if (isPresenceRoute) {
        const me = getSessionUser(req);
        if (!me) return sendJson(res, 401, { error: 'Connexion requise.' });
        if (req.method !== 'GET' && req.method !== 'POST') {
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }
        if (req.method === 'POST') {
            vttPresence.set(me.id, { userId: me.id, username: me.username, at: Date.now() });
        }
        const cut = Date.now() - 45000;
        const avatars = buildAvatarMap();
        const online = [];
        for (const [id, p] of vttPresence) {
            if (!p || p.at < cut) { vttPresence.delete(id); continue; }
            online.push({ userId: id, username: p.username || '?', avatar: avatars[id] || null, since: p.at });
        }
        online.sort((a, b) => a.username.localeCompare(b.username, 'fr'));
        return sendJson(res, 200, { success: true, online });
    }

    /* ================================================================
       PUSH TABLES — flux instantané SSE (GET /api/tables/stream)
       ----------------------------------------------------------------
       Un EventSource par page connectée (avec cookies) reçoit chaque
       nouveau message dès sa publication : plus d'attente du cycle de
       sondage. Repli : le client sonde toutes les 45 s si SSE indisponible.
       ================================================================ */
    if (cleanPath === '/api/tables/stream') {
        if (req.method !== 'GET') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        const me = getSessionUser(req);
        if (!me) return sendJson(res, 401, { error: 'Connexion requise.' });
        if (sseSubs.size > 500) return sendJson(res, 503, { error: 'Trop d’abonnés, réessayez.' });
        res.writeHead(200, {
            'Content-Type': 'text/event-stream; charset=utf-8',
            'Cache-Control': 'no-cache, no-transform',
            'Connection': 'keep-alive',
            'X-Accel-Buffering': 'no'
        });
        res.write(': ok\n\n');
        const sub = { userId: me.id, email: me.email, res };
        sseSubs.add(sub);
        const hb = setInterval(() => { try { res.write(': ping\n\n'); } catch (e) {} }, 25000);
        req.on('close', () => {
            try { clearInterval(hb); } catch (e) {}
            sseSubs.delete(sub);
        });
        return;
    }

    /* ================================================================
       TABLES DE GROUPE — chat persistant + images + MPs (REST + JSON)
       ----------------------------------------------------------------
       Un groupe se crée une table : chat public, partage d'images (URL
       ou upload base64 ≤ ~10 Mo), messages privés (MJ <-> joueur).
       Log : un fichier JSON par table dans data/tables/table_<id>.json.
       Rôles de table : mj (créateur, voit tout), helper (co-gère, voit
       tout), joueur (défaut : public + ses propres MPs).
       Toutes les routes exigent une session (cookie srv_).
       ================================================================ */
    const TABLES_DIR = path.join(PUBLIC_DIR, 'data', 'tables');
    const TABLE_ID_RE = /^[a-z0-9_-]{1,32}$/i;
    const TABLE_MAX_MESSAGES = 5000;
    const TABLE_MAX_TEXT = 2000;
    const TABLE_MAX_UPLOAD_CHARS = 14000000; // ~10 Mo binaires en base64
    function tablesEnsureDir() {
        try { fs.mkdirSync(TABLES_DIR, { recursive: true }); } catch (e) {}
    }
    function tableFilePath(id) {
        return path.join(TABLES_DIR, 'table_' + String(id).toLowerCase() + '.json');
    }
    function tableCleanStr(s, max) {
        return String(s || '').replace(/[\u0000-\u001f\u007f]/g, '').trim().slice(0, max);
    }
    function loadTableDoc(id) {
        tablesEnsureDir();
        const file = tableFilePath(id);
        if (!fs.existsSync(file)) return null;
        try {
            const doc = JSON.parse(fs.readFileSync(file, 'utf-8'));
            if (!doc || doc.id !== String(id).toLowerCase()) return null;
            if (!Array.isArray(doc.members)) doc.members = [];
            if (!Array.isArray(doc.messages)) doc.messages = [];
            return doc;
        } catch (e) { return null; }
    }
    function saveTableDoc(doc) {
        tablesEnsureDir();
        doc.updatedAt = Date.now();
        const file = tableFilePath(doc.id);
        try { if (fs.existsSync(file)) fs.copyFileSync(file, file + '.bak'); } catch (e) {}
        const json = JSON.stringify(doc, null, 2);
        if (json.length > 64 * 1024 * 1024) return false; // garde-fou fichier (pièces jointes jusqu'à ~10 Mo)
        fs.writeFileSync(file, json);
        return true;
    }
    function tableRoleOf(doc, userId) {
        if (!doc || !userId) return null;
        const m = doc.members.find(m => m.userId === userId);
        return m ? m.role : null;
    }
    function tableCanManage(doc, user) {
        if (!doc || !user) return false;
        if (isAdminEmail(user.email)) return true;
        const r = tableRoleOf(doc, user.id);
        return r === 'mj' || r === 'helper';
    }
    function tableCanSeeMsg(doc, msg, user) {
        // Messages publics restreints par l'envoyeur (snapshot toExcept) :
        // chacun choisit à qui SES futurs messages sont envoyés (y compris
        // le MJ : aucune exception de rôle). L'auteur voit toujours les siens.
        // Les MPs (toUserId) sont nominatifs : toujours livrés au destinataire.
        if (!msg.toUserId) {
            if (msg.toExcept && msg.toExcept.includes(user.id) && user.id !== msg.fromId) {
                if (isAdminEmail(user.email)) return true;
                return false;
            }
            return true; // public
        }
        if (!user) return false;
        if (isAdminEmail(user.email)) return true;
        if (msg.fromId === user.id || msg.toUserId === user.id) return true;
        const r = tableRoleOf(doc, user.id);
        return r === 'mj' || r === 'helper';
    }
    // Métadonnées de pièce jointe sans les octets (dataUrl) : le contenu
    // se charge à la demande (GET .../messages/:mid/attachment).
    function tablePublicFile(f) {
        if (!f || f.kind !== 'upload') return f || null;
        const o = { kind: 'upload', mime: f.mime };
        if (f.name) o.name = String(f.name).slice(0, 120);
        if (f.bytes) o.bytes = f.bytes;
        return o;
    }
    function tablePublicMsg(msg, avatars, members) {
        const o = {
            id: msg.id, fromId: msg.fromId, fromName: msg.fromName, fromRole: msg.fromRole,
            fromAvatar: (avatars && avatars[msg.fromId]) || null,
            text: msg.text || '', image: msg.image && msg.image.kind === 'upload' ? tablePublicFile(msg.image) : (msg.image || null),
            file: tablePublicFile(msg.file),
            toUserId: msg.toUserId || null, toName: msg.toName || null,
            private: !!msg.toUserId, createdAt: msg.createdAt,
            // IDs exclus à l'envoi (pour l'héritage côté client : répondre
            // dans un fil restreint re-mute automatiquement les mêmes).
            toExcept: Array.isArray(msg.toExcept) ? msg.toExcept : []
        };
        // Message restreint par son envoyeur : liste de ceux qui l'ont reçu
        // (membres actuels hors exclus + auteur).
        if (msg.toExcept && msg.toExcept.length && Array.isArray(members)) {
            const ban = new Set(msg.toExcept);
            const got = members.filter(m => m && !ban.has(m.userId)).map(m => m.username);
            if (!got.includes(msg.fromName)) got.unshift(msg.fromName);
            o.receivedBy = got;
        } else {
            o.receivedBy = null;
        }
        return o;
    }
    // Pseudonymes + avatars : une seule lecture users.json par requête.
    function buildAvatarMap() {
        const map = {};
        try {
            for (const u of loadUsers()) {
                if (u && u.id && typeof u.avatar === 'string' && u.avatar) {
                    map[u.id] = u.avatar.slice(0, 700000);
                }
            }
        } catch (e) {}
        return map;
    }
    // Mot de passe de table optionnel (créateur) : sel + SHA-256, jamais exposé.
    function tableSetPassword(doc, plain) {
        if (!plain) { doc.pw = null; return; }
        const salt = crypto.randomBytes(8).toString('hex');
        const hash = crypto.createHash('sha256').update(salt + ':' + String(plain)).digest('hex');
        doc.pw = { salt, hash };
    }
    function tableCheckPassword(doc, plain) {
        if (!doc.pw || !doc.pw.salt || !doc.pw.hash) return true;
        try {
            const h = crypto.createHash('sha256').update(doc.pw.salt + ':' + String(plain || '')).digest('hex');
            return crypto.timingSafeEqual(Buffer.from(h, 'hex'), Buffer.from(doc.pw.hash, 'hex'));
        } catch (e) { return false; }
    }
    // Liste d'uploads en cours d'une table (expirés purgés).
    function tableUploadList(doc) {
        const now = Date.now();
        const st = tableUploads[doc.id] || {};
        const out = [];
        Object.keys(st).forEach((uid) => {
            const u = st[uid];
            if (!u || now - (u.startedAt || 0) > TABLE_UPLOAD_TTL_MS) { delete st[uid]; return; }
            const m = doc.members.find((x) => x && x.userId === uid);
            out.push({ userId: uid, username: (m && m.username) || u.username || '?', kind: u.kind || 'fichier', startedAt: u.startedAt });
        });
        out.sort((a, b) => String(a.username).localeCompare(String(b.username), 'fr'));
        return out;
    }
    // Push instantané : diffuse la présence d'upload aux membres connectés.
    function sseNotifyUploads(doc) {
        if (!doc || sseSubs.size === 0) return;
        const data = JSON.stringify({ tableId: doc.id, tableName: doc.name, uploads: tableUploadList(doc) });
        for (const sub of [...sseSubs]) {
            try {
                if (!sub || !sub.res) { sseSubs.delete(sub); continue; }
                if (!tableRoleOf(doc, sub.userId) && !isAdminEmail(sub.email)) continue;
                sub.res.write('event: upload-state\n' + 'data: ' + data + '\n\n');
            } catch (e) {
                try { sseSubs.delete(sub); } catch (_) {}
            }
        }
    }
    // Push instantané : diffuse le message aux abonnés SSE membres de la
    // table, en respectant la visibilité des MPs (MJ/helper voient tout).
    function sseNotifyTable(doc, msg) {
        if (!doc || !msg || sseSubs.size === 0) return;
        const avatars = buildAvatarMap();
        for (const sub of [...sseSubs]) {
            try {
                if (!sub || !sub.res) { sseSubs.delete(sub); continue; }
                if (!tableRoleOf(doc, sub.userId) && !isAdminEmail(sub.email)) continue;
                if (!tableCanSeeMsg(doc, msg, { id: sub.userId, email: sub.email })) continue;
                sub.res.write('event: table-message\n' +
                    'data: ' + JSON.stringify({ tableId: doc.id, tableName: doc.name, message: tablePublicMsg(msg, avatars, doc.members) }) + '\n\n');
            } catch (e) {
                try { sseSubs.delete(sub); } catch (_) {}
            }
        }
    }
    // Voix live : pousse un événement voix aux membres connectés de la table.
    // onlyUserId : ciblé (signalisation SDP/ICE) ; sinon broadcast table.
    // Seule la signalisation vocale est bloquée aux mutés (un muté ne
    // reçoit plus le vocal) ; les événements de contrôle (presence, mute,
    // reset) restent visibles pour que chacun suive l'état.
    function tableVoicePeersPayload(doc, st) {
        const out = [];
        for (const [uid, p] of st.peers) {
            const m = doc.members.find(x => x && x.userId === uid);
            out.push({ userId: uid, username: (m && m.username) || (p && p.username) || '?', muted: st.muted.has(uid), joinedAt: (p && p.joinedAt) || 0 });
        }
        out.sort((a, b) => a.username.localeCompare(b.username, 'fr'));
        return out;
    }
    function sseNotifyVoice(doc, event, obj, onlyUserId) {
        if (!doc || !event || sseSubs.size === 0) return;
        const data = JSON.stringify(Object.assign({ tableId: doc.id, tableName: doc.name }, obj || {}));
        const st = tableVoiceState(doc.id);
        for (const sub of [...sseSubs]) {
            try {
                if (!sub || !sub.res) { sseSubs.delete(sub); continue; }
                if (onlyUserId && sub.userId !== onlyUserId) continue;
                if (!onlyUserId && !tableRoleOf(doc, sub.userId) && !isAdminEmail(sub.email)) continue;
                // Signalisation vocale (offer/answer/ice) : jamais livrée à
                // un muté — un joueur muté ne reçoit plus le vocal.
                if (event === 'voice-signal' && st.muted.has(sub.userId)) continue;
                sub.res.write('event: ' + event + '\ndata: ' + data + '\n\n');
            } catch (e) {
                try { sseSubs.delete(sub); } catch (_) {}
            }
        }
    }
    function tableSummary(doc, user) {
        const role = (user && isAdminEmail(user.email)) ? 'admin' : tableRoleOf(doc, user && user.id);
        return {
            id: doc.id, name: doc.name, desc: doc.desc || '',
            mjName: doc.mjName || '?', role: role,
            members: doc.members.length, messages: doc.messages.length,
            hasPassword: !!(doc.pw && doc.pw.salt), listed: doc.listed !== false,
            createdAt: doc.createdAt, updatedAt: doc.updatedAt
        };
    }
    const isTablesRoute = cleanPath === '/api/tables' || cleanPath.startsWith('/api/tables/');
    if (isTablesRoute) {
        const me = getSessionUser(req);
        if (!me) return sendJson(res, 401, { error: 'Connexion requise.' });
        const myId = me.id;
        const sub = cleanPath.slice('/api/tables'.length); // '' | '/<id>' | '/<id>/...'
        const parts = sub.split('/').filter(Boolean);

        // GET /api/tables — tables dont je suis membre (+ toutes si admin site).
        if (parts.length === 0) {
            if (req.method === 'GET') {
                tablesEnsureDir();
                let files = [];
                try { files = fs.readdirSync(TABLES_DIR).filter(f => /^table_.*\.json$/.test(f)); } catch (e) {}
                const out = [];
                for (const f of files) {
                    try {
                        const doc = JSON.parse(fs.readFileSync(path.join(TABLES_DIR, f), 'utf-8'));
                        if (!doc || !doc.id) continue;
                        if (isAdminEmail(me.email) || tableRoleOf(doc, myId)) out.push(tableSummary(doc, me));
                    } catch (e) {}
                }
                out.sort((a, b) => (b.updatedAt || 0) - (a.updatedAt || 0));
                return sendJson(res, 200, { success: true, tables: out });
            }
            // POST /api/tables — créer une table (je deviens MJ).
            if (req.method === 'POST') {
                try {
                    const body = await parseBody(req);
                    const name = tableCleanStr(body && body.name, 40);
                    const desc = tableCleanStr(body && body.desc, 500);
                    if (!name) return sendJson(res, 400, { error: 'Nom de table requis (1-40 caractères).' });
                    const now = Date.now();
                    const id = 'tbl_' + now.toString(36) + Math.floor(Math.random() * 1296).toString(36);
                    const doc = {
                        id, name, desc, mjId: myId, mjName: me.username,
                        createdAt: now, updatedAt: now, maxMembers: 12, inviteCode: null,
                        listed: (body && body.listed === false) ? false : true, pw: null,
                        members: [{ userId: myId, username: me.username, role: 'mj', joinedAt: now }],
                        messages: []
                    };
                    if (body && body.password !== undefined) {
                        const p = String(body.password || '').slice(0, 64);
                        if (p) tableSetPassword(doc, p);
                    }
                    if (!saveTableDoc(doc)) return sendJson(res, 413, { error: 'Table trop volumineuse.' });
                    console.log(`[Tables] table créée : ${id} ("${name}") par ${me.username}.`);
                    return sendJson(res, 201, { success: true, table: tableSummary(doc, me) });
                } catch (e) {
                    return sendJson(res, 400, { error: e.message });
                }
            }
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }

        // POST /api/tables/join — rejoindre via code d'invitation (?invite=).
        if (parts.length === 1 && parts[0] === 'join') {
            if (req.method === 'POST') {
                try {
                    const body = await parseBody(req);
                    const code = String((body && body.code) || '').trim().slice(0, 64);
                    if (!code) return sendJson(res, 400, { error: 'Code d’invitation requis.' });
                    tablesEnsureDir();
                    let files = [];
                    try { files = fs.readdirSync(TABLES_DIR).filter(f => /^table_.*\.json$/.test(f)); } catch (e) {}
                    let found = null;
                    for (const f of files) {
                        try {
                            const d = JSON.parse(fs.readFileSync(path.join(TABLES_DIR, f), 'utf-8'));
                            if (d && d.id && d.inviteCode === code) { found = d; break; }
                        } catch (e) {}
                    }
                    if (!found) return sendJson(res, 404, { error: 'Code d’invitation invalide ou révoqué.' });
                    if (found.members.some(m => m.userId === myId)) {
                        return sendJson(res, 200, { success: true, table: tableSummary(found, me), already: true });
                    }
                    if (found.members.length >= (found.maxMembers || 12)) {
                        return sendJson(res, 403, { error: 'Table complète.' });
                    }
                    const users = loadUsers();
                    const u = users.find(x => x.id === myId);
                    found.members.push({ userId: myId, username: u ? u.username : me.username, role: 'joueur', joinedAt: Date.now() });
                    if (!saveTableDoc(found)) { found.members.pop(); return sendJson(res, 413, { error: 'Table trop volumineuse.' }); }
                    console.log(`[Tables] ${me.username} rejoint ${found.id} via invitation.`);
                    return sendJson(res, 200, { success: true, table: tableSummary(found, me) });
                } catch (e) {
                    return sendJson(res, 400, { error: e.message });
                }
            }
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }

        // GET /api/tables/directory — annuaire (tables listées ; les non
        // listées n'apparaissent qu'à leurs membres / admin). hasPassword
        // signale le cadenas, jamais le secret.
        if (parts.length === 1 && parts[0] === 'directory') {
            if (req.method !== 'GET') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            tablesEnsureDir();
            let files = [];
            try { files = fs.readdirSync(TABLES_DIR).filter(f => /^table_.*\.json$/.test(f)); } catch (e) {}
            const out = [];
            for (const f of files) {
                try {
                    const d = JSON.parse(fs.readFileSync(path.join(TABLES_DIR, f), 'utf-8'));
                    if (!d || !d.id) continue;
                    const member = isAdminEmail(me.email) || tableRoleOf(d, myId);
                    if (d.listed === false && !member) continue;
                    out.push(Object.assign(tableSummary(d, me), { isMember: !!member }));
                } catch (e) {}
            }
            out.sort((a, b) => (b.updatedAt || 0) - (a.updatedAt || 0));
            return sendJson(res, 200, { success: true, tables: out });
        }

        const tableId = String(parts[0] || '').toLowerCase();
        if (!TABLE_ID_RE.test(tableId)) return sendJson(res, 400, { error: 'Identifiant de table invalide.' });
        const doc = loadTableDoc(tableId);
        if (!doc) return sendJson(res, 404, { error: 'Table introuvable.' });
        const myRole = isAdminEmail(me.email) ? 'admin' : tableRoleOf(doc, myId);
        const isMember = !!myRole;

        // GET /api/tables/:id — détail + log filtré (?limit, ?since).
        if (parts.length === 1) {
            if (req.method === 'GET') {
                if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
                let limit = parseInt(parsedUrl.searchParams.get('limit'), 10);
                if (!Number.isFinite(limit)) limit = 200;
                limit = Math.max(1, Math.min(500, limit));
                const since = Number(parsedUrl.searchParams.get('since')) || 0;
                const avatars = buildAvatarMap();
                const visible = doc.messages
                    .filter(m => m.createdAt > since && tableCanSeeMsg(doc, m, me))
                    .slice(-limit)
                    .map(m => tablePublicMsg(m, avatars, doc.members));
                return sendJson(res, 200, {
                    success: true,
                    table: {
                        id: doc.id, name: doc.name, desc: doc.desc || '',
                        mjId: doc.mjId, mjName: doc.mjName || '?',
                        myRole: myRole, createdAt: doc.createdAt, updatedAt: doc.updatedAt,
                        maxMembers: doc.maxMembers || 12,
                        hasPassword: !!(doc.pw && doc.pw.salt), listed: doc.listed !== false,
                        myInherited: (doc.inherited && Array.isArray(doc.inherited[myId])) ? doc.inherited[myId] : [],
                        inviteCode: tableCanManage(doc, me) ? (doc.inviteCode || null) : null,
                        members: doc.members.map(m => ({ userId: m.userId, username: m.username, role: m.role, avatar: avatars[m.userId] || null, joinedAt: m.joinedAt })),
                        messages: visible
                    }
                });
            }
            // PUT /api/tables/:id — renommer / décrire / mot de passe / liste (MJ, helper, admin site).
            if (req.method === 'PUT' || req.method === 'POST') {
                if (!tableCanManage(doc, me)) return sendJson(res, 403, { error: 'Gestion réservée au MJ / helper.' });
                try {
                    const body = await parseBody(req);
                    if (body.name !== undefined) {
                        const name = tableCleanStr(body.name, 40);
                        if (!name) return sendJson(res, 400, { error: 'Nom invalide.' });
                        doc.name = name;
                    }
                    if (body.desc !== undefined) doc.desc = tableCleanStr(body.desc, 500);
                    if (body.listed !== undefined) doc.listed = !!body.listed;
                    if (body.password !== undefined) {
                        const p = String(body.password || '').slice(0, 64);
                        tableSetPassword(doc, p || null);
                    }
                    if (body.maxMembers !== undefined) {
                        const n = parseInt(body.maxMembers, 10);
                        if (!Number.isFinite(n) || n < 2 || n > 24) {
                            return sendJson(res, 400, { error: 'maxMembers invalide (2-24).' });
                        }
                        if (n < doc.members.length) {
                            return sendJson(res, 400, { error: 'maxMembers inférieur au nombre de membres.' });
                        }
                        doc.maxMembers = n;
                    }
                    if (!saveTableDoc(doc)) return sendJson(res, 413, { error: 'Table trop volumineuse.' });
                    console.log(`[Tables] ${doc.id} renommée par ${me.username}.`);
                    return sendJson(res, 200, { success: true, table: tableSummary(doc, me) });
                } catch (e) {
                    return sendJson(res, 400, { error: e.message });
                }
            }
            // DELETE /api/tables/:id — supprimer (MJ de la table ou admin site).
            if (req.method === 'DELETE') {
                if (!(doc.mjId === myId || isAdminEmail(me.email))) {
                    return sendJson(res, 403, { error: 'Suppression réservée au MJ de la table.' });
                }
                try { fs.unlinkSync(tableFilePath(doc.id)); } catch (e) {}
                try { fs.unlinkSync(tableFilePath(doc.id) + '.bak'); } catch (e) {}
                console.log(`[Tables] table supprimée : ${doc.id} par ${me.username}.`);
                return sendJson(res, 200, { success: true });
            }
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }

        // POST /api/tables/:id/messages — écrire (public ou MP via toUserId).
        if (parts.length === 2 && parts[1] === 'messages') {
            if (req.method === 'GET') {
                if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
                const since = Number(parsedUrl.searchParams.get('since')) || 0;
                const avatars = buildAvatarMap();
                const visible = doc.messages
                    .filter(m => m.createdAt > since && tableCanSeeMsg(doc, m, me))
                    .map(m => tablePublicMsg(m, avatars, doc.members));
                // Liste d'envoi héritée (serveur) : rafraîchie à chaque poll pour
                // que la prochaine réponse hérite sans attendre une réouverture.
                return sendJson(res, 200, { success: true, messages: visible,
                    myInherited: (doc.inherited && Array.isArray(doc.inherited[myId])) ? doc.inherited[myId] : [] });
            }
            if (req.method === 'POST' || req.method === 'PUT') {
                if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
                if (doc.messages.length >= TABLE_MAX_MESSAGES) {
                    return sendJson(res, 413, { error: 'Log plein (5000 messages). Le MJ doit purger.' });
                }
                try {
                    const body = await parseBody(req);
                    const text = tableCleanStr(body && body.text, TABLE_MAX_TEXT);
                    let image = null;
                    const imageUrl = String((body && body.imageUrl) || '').trim().slice(0, 2048);
                    const imageData = String((body && body.imageData) || '');
                    let file = null;
                    const fileData = String((body && body.fileData) || '');
                    const fileName = String((body && body.fileName) || 'document.pdf').slice(0, 120);
                    if (imageUrl) {
                        let ok = false;
                        try {
                            const u = new NodeURL(imageUrl);
                            ok = (u.protocol === 'http:' || u.protocol === 'https:');
                        } catch (e) {}
                        if (!ok) return sendJson(res, 400, { error: 'URL d’image invalide (http/https requis).' });
                        image = { kind: 'url', url: imageUrl };
                    } else if (imageData) {
                        const m = /^data:image\/(png|jpe?g|gif|webp|apng);base64,[A-Za-z0-9+/=]+$/.exec(imageData);
                        if (!m) return sendJson(res, 400, { error: 'Image invalide (data:image/png|jpeg|gif|webp|apng;base64).' });
                        if (imageData.length > TABLE_MAX_UPLOAD_CHARS) {
                            return sendJson(res, 413, { error: 'Image trop lourde (max ~10 Mo).' });
                        }
                        const bytes = Math.floor((imageData.length - imageData.indexOf(',') - 1) * 3 / 4);
                        image = { kind: 'upload', mime: 'image/' + m[1].replace('jpg', 'jpeg'), dataUrl: imageData, bytes };
                    }
                    if (fileData) {
                        const fm = /^data:(application\/pdf|application\/ogg|audio\/[A-Za-z0-9.+-]+|video\/[A-Za-z0-9.+-]+);base64,[A-Za-z0-9+/=]+$/.exec(fileData);
                        if (!fm) return sendJson(res, 400, { error: 'Fichier invalide (PDF, audio ou vidéo en base64).' });
                        if (fileData.length > TABLE_MAX_UPLOAD_CHARS) {
                            return sendJson(res, 413, { error: 'Fichier trop lourd (max ~10 Mo).' });
                        }
                        const fbytes = Math.floor((fileData.length - fileData.indexOf(',') - 1) * 3 / 4);
                        file = { kind: 'upload', mime: fm[1], name: fileName || 'document', dataUrl: fileData, bytes: fbytes };
                    }
                    if (!text && !image && !file) return sendJson(res, 400, { error: 'Message vide (texte, image, PDF, audio ou vidéo requis).' });
                    let toUserId = null, toName = null;
                    const rawTo = String((body && body.toUserId) || '').trim();
                    if (rawTo) {
                        const target = doc.members.find(m => m.userId === rawTo);
                        if (!target) return sendJson(res, 404, { error: 'Destinataire non membre de la table.' });
                        if (rawTo === myId) return sendJson(res, 400, { error: 'MP à soi-même inutile : écrivez en public.' });
                        toUserId = target.userId; toName = target.username;
                    }
                    const senderRole = myRole === 'admin' ? (tableRoleOf(doc, myId) || 'joueur') : myRole;
                    // Liste d'envoi personnelle : l'envoyeur choisit à qui SES
                    // futurs messages sont livrés (membres uniquement, auteur exclu
                    // d'office). Snapshot : les réintégrations ultérieures ne
                    // rouvrent pas l'historique. Les MPs restent nominatifs.
                    let toExcept = [];
                    if (Array.isArray(body.toExcept)) {
                        const memberIds = {};
                        doc.members.forEach(m => { if (m && m.userId) memberIds[m.userId] = true; });
                        const seen = {};
                        for (const raw of body.toExcept.slice(0, 200)) {
                            const id = String(raw || '').slice(0, 64);
                            if (id && id !== myId && memberIds[id] && !seen[id]) { seen[id] = true; toExcept.push(id); }
                        }
                    }
                    // MPs nominatifs : la liste d'envoi ne s'applique pas.
                    if (toUserId) toExcept = [];
                    // Héritage serveur : les destinataires (hors auteur et hors
                    // exclus) reçoivent ces exclus dans leur propre liste d'envoi
                    // pour LEUR prochain message (répondre = re-muter).
                    const inh = (doc.inherited && typeof doc.inherited === 'object') ? doc.inherited : (doc.inherited = {});
                    if (toExcept.length && !toUserId) {
                        for (const m of doc.members) {
                            const rid = m && m.userId;
                            if (!rid || rid === myId || toExcept.includes(rid)) continue;
                            const set = new Set(Array.isArray(inh[rid]) ? inh[rid] : []);
                            let changed = false;
                            for (const id of toExcept) { if (!set.has(id)) { set.add(id); changed = true; } }
                            if (changed) inh[rid] = [...set].slice(0, 200);
                        }
                    }
                    const msg = {
                        id: 'm_' + Date.now().toString(36) + Math.floor(Math.random() * 1296).toString(36),
                        fromId: myId, fromName: me.username, fromRole: senderRole,
                        text, image, file, toUserId, toName, toExcept, createdAt: Date.now()
                    };
                    doc.messages.push(msg);
                    // Upload terminé (message arrivé) : on efface la présence.
                    try {
                        const ust = tableUploads[doc.id];
                        if (ust && ust[myId]) { delete ust[myId]; sseNotifyUploads(doc); }
                    } catch (e) {}
                    if (!saveTableDoc(doc)) { doc.messages.pop(); return sendJson(res, 413, { error: 'Table trop volumineuse.' }); }
                    try { sseNotifyTable(doc, msg); } catch (e) {}
                    return sendJson(res, 201, { success: true, message: tablePublicMsg(msg, buildAvatarMap(), doc.members) });
                } catch (e) {
                    return sendJson(res, 400, { error: e.message });
                }
            }
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }

        // DELETE /api/tables/:id/messages/:mid — effacer (auteur ou MJ/helper).
        if (parts.length === 3 && parts[1] === 'messages') {
            if (req.method === 'DELETE') {
                const idx = doc.messages.findIndex(m => m.id === parts[2]);
                if (idx === -1) return sendJson(res, 404, { error: 'Message introuvable.' });
                const msg = doc.messages[idx];
                if (!(msg.fromId === myId || tableCanManage(doc, me))) {
                    return sendJson(res, 403, { error: 'Suppression réservée à l’auteur ou au MJ.' });
                }
                doc.messages.splice(idx, 1);
                if (!saveTableDoc(doc)) return sendJson(res, 413, { error: 'Table trop volumineuse.' });
                return sendJson(res, 200, { success: true });
            }
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }

        // GET /api/tables/:id/messages/:mid/attachment — octets d'une pièce
        // jointe (image, PDF, audio, vidéo). Membre + visibilité du message
        // requis. Contenu immuable -> cache navigateur longue durée.
        if (parts.length === 4 && parts[1] === 'messages' && parts[3] === 'attachment') {
            if (req.method !== 'GET') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
            const msg = doc.messages.find(m => m.id === parts[2]);
            if (!msg) return sendJson(res, 404, { error: 'Message introuvable.' });
            if (!tableCanSeeMsg(doc, msg, me)) return sendJson(res, 403, { error: 'Message non visible.' });
            const att = [msg.image, msg.file].find(f => f && f.kind === 'upload' && f.dataUrl);
            if (!att) return sendJson(res, 404, { error: 'Aucune pièce jointe.' });
            let buf = null;
            try {
                const b64 = String(att.dataUrl).split(',')[1] || '';
                if (!/^[A-Za-z0-9+/=]+$/.test(b64)) throw new Error('base64');
                buf = Buffer.from(b64, 'base64');
            } catch (e) { return sendJson(res, 500, { error: 'Pièce jointe illisible.' }); }
            res.writeHead(200, {
                'Content-Type': att.mime || 'application/octet-stream',
                'Content-Length': buf.length,
                'Cache-Control': 'public, max-age=31536000, immutable',
                'Vary': 'Origin'
            });
            return res.end(buf);
        }

        // POST /api/tables/:id/members — inviter (pseudo ou e-mail).
        if (parts.length === 2 && parts[1] === 'members') {
            if (req.method === 'POST') {
                if (!tableCanManage(doc, me)) return sendJson(res, 403, { error: 'Invitations réservées au MJ / helper.' });
                try {
                    const body = await parseBody(req);
                    const term = String((body && body.user) || '').trim().toLowerCase();
                    if (!term) return sendJson(res, 400, { error: 'Pseudo ou e-mail requis.' });
                    const users = loadUsers();
                    const target = users.find(u => u.username.toLowerCase() === term || u.email.toLowerCase() === term);
                    if (!target) return sendJson(res, 404, { error: 'Utilisateur introuvable.' });
                    if (doc.members.some(m => m.userId === target.id)) {
                        return sendJson(res, 409, { error: 'Déjà membre de la table.' });
                    }
                    if (doc.members.length >= (doc.maxMembers || 12)) {
                        return sendJson(res, 403, { error: 'Table complète (maxMembers atteint).' });
                    }
                    const rec = { userId: target.id, username: target.username, role: 'joueur', joinedAt: Date.now() };
                    doc.members.push(rec);
                    if (!saveTableDoc(doc)) { doc.members.pop(); return sendJson(res, 413, { error: 'Table trop volumineuse.' }); }
                    console.log(`[Tables] ${target.username} invité sur ${doc.id} par ${me.username}.`);
                    return sendJson(res, 201, { success: true, member: rec });
                } catch (e) {
                    return sendJson(res, 400, { error: e.message });
                }
            }
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }

        // PUT/DELETE /api/tables/:id/members/:uid — rôle ou exclusion.
        if (parts.length === 3 && parts[1] === 'members') {
            const uid = parts[2];
            const idx = doc.members.findIndex(m => m.userId === uid);
            if (idx === -1) return sendJson(res, 404, { error: 'Membre introuvable.' });
            // Sortie volontaire : tout membre non-MJ peut quitter sa table
            // (le MJ doit transférer son titre ou supprimer la table).
            if (req.method === 'DELETE' && uid === myId && doc.members[idx].role !== 'mj') {
                doc.members.splice(idx, 1);
                if (!saveTableDoc(doc)) return sendJson(res, 413, { error: 'Table trop volumineuse.' });
                return sendJson(res, 200, { success: true, left: true });
            }
            if (!tableCanManage(doc, me)) return sendJson(res, 403, { error: 'Gestion réservée au MJ / helper.' });
            if (req.method === 'PUT') {
                try {
                    const body = await parseBody(req);
                    const role = String((body && body.role) || '');
                    if (!['joueur', 'helper', 'mj'].includes(role)) {
                        return sendJson(res, 400, { error: 'Rôle invalide (joueur|helper|mj).' });
                    }
                    // Seul le MJ (ou admin site) touche au titre de MJ.
                    if (role === 'mj' || doc.members[idx].role === 'mj') {
                        if (!(doc.mjId === myId || isAdminEmail(me.email))) {
                            return sendJson(res, 403, { error: 'Transfert MJ réservé au MJ de la table.' });
                        }
                    }
                    if (role === 'mj') {
                        const oldMj = doc.members.find(m => m.role === 'mj');
                        if (oldMj) oldMj.role = 'helper';
                        doc.mjId = uid;
                    }
                    doc.members[idx].role = role;
                    const users = loadUsers();
                    const u = users.find(x => x.id === uid);
                    if (u) doc.members[idx].username = u.username;
                    if (!saveTableDoc(doc)) return sendJson(res, 413, { error: 'Table trop volumineuse.' });
                    console.log(`[Tables] ${uid} -> ${role} sur ${doc.id} (par ${me.username}).`);
                    return sendJson(res, 200, { success: true, member: doc.members[idx] });
                } catch (e) {
                    return sendJson(res, 400, { error: e.message });
                }
            }
            if (req.method === 'DELETE') {
                if (doc.members[idx].role === 'mj') {
                    return sendJson(res, 400, { error: 'Transférez le MJ avant d’exclure (ou supprimez la table).' });
                }
                if (uid === myId) return sendJson(res, 400, { error: 'Le MJ ne peut pas s’exclure : transférez ou supprimez.' });
                doc.members.splice(idx, 1);
                if (!saveTableDoc(doc)) return sendJson(res, 413, { error: 'Table trop volumineuse.' });
                return sendJson(res, 200, { success: true });
            }
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }

        // POST /api/tables/:id/invite — lire/créer le code (MJ/helper ; {regen:true} régénère).
        // DELETE /api/tables/:id/invite — révoquer le code.
        if (parts.length === 2 && parts[1] === 'invite') {
            if (!tableCanManage(doc, me)) {
                return sendJson(res, 403, { error: 'Gestion réservée au MJ / helper.' });
            }
            if (req.method === 'POST') {
                try {
                    const body = await parseBody(req).catch(() => ({}));
                    if (!doc.inviteCode || (body && body.regen)) {
                        doc.inviteCode = 'inv_' + crypto.randomBytes(9).toString('base64url');
                    }
                    if (!saveTableDoc(doc)) return sendJson(res, 413, { error: 'Table trop volumineuse.' });
                    return sendJson(res, 200, { success: true, code: doc.inviteCode });
                } catch (e) {
                    return sendJson(res, 400, { error: e.message });
                }
            }
            if (req.method === 'DELETE') {
                doc.inviteCode = null;
                if (!saveTableDoc(doc)) return sendJson(res, 413, { error: 'Table trop volumineuse.' });
                return sendJson(res, 200, { success: true });
            }
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }

        // POST /api/tables/:id/join — rejoindre depuis l'annuaire
        // (mot de passe si la table en a un).
        if (parts.length === 2 && parts[1] === 'join') {
            if (req.method !== 'POST') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            if (isMember) {
                return sendJson(res, 200, { success: true, table: tableSummary(doc, me), already: true });
            }
            // Non listée + sans mot de passe = sur invitation uniquement.
            // Non listée + mot de passe = lien + mot de passe (bouche-à-oreille).
            if (doc.listed === false && !(doc.pw && doc.pw.salt) && !isAdminEmail(me.email)) {
                return sendJson(res, 403, { error: 'Table non listée : demandez une invitation.' });
            }
            if (doc.members.length >= (doc.maxMembers || 12)) {
                return sendJson(res, 403, { error: 'Table complète.' });
            }
            try {
                const body = await parseBody(req).catch(() => ({}));
                if (!tableCheckPassword(doc, body && body.password)) {
                    return sendJson(res, 403, { error: 'Mot de passe incorrect.' });
                }
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
            const users = loadUsers();
            const u = users.find(x => x.id === myId);
            doc.members.push({ userId: myId, username: u ? u.username : me.username, role: 'joueur', joinedAt: Date.now() });
            if (!saveTableDoc(doc)) { doc.members.pop(); return sendJson(res, 413, { error: 'Table trop volumineuse.' }); }
            console.log(`[Tables] ${me.username} rejoint ${doc.id} via annuaire.`);
            return sendJson(res, 200, { success: true, table: tableSummary(doc, me) });
        }

        // POST /api/tables/:id/send-reset — rouvrir mon envoi (oublier les
        // exclus hérités). Les masqués personnels (local) se gèrent à l'œil.
        if (parts.length === 2 && parts[1] === 'send-reset') {
            if (req.method !== 'POST') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
            if (doc.inherited && doc.inherited[myId]) {
                delete doc.inherited[myId];
                if (!saveTableDoc(doc)) return sendJson(res, 413, { error: 'Table trop volumineuse.' });
            }
            return sendJson(res, 200, { success: true });
        }

        // POST /api/tables/:id/upload-state — signaler début/fin d'envoi de
        // fichier (animation « untel envoie… » chez les membres).
        if (parts.length === 2 && parts[1] === 'upload-state') {
            if (req.method !== 'POST') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
            const body = await parseBody(req);
            const state = String((body && body.state) || '');
            const st = tableUploads[doc.id] || (tableUploads[doc.id] = {});
            if (state === 'start') {
                st[myId] = { username: me.username, kind: String((body && body.kind) || 'fichier').slice(0, 16), startedAt: Date.now() };
            } else {
                delete st[myId];
            }
            sseNotifyUploads(doc);
            return sendJson(res, 200, { success: true, uploads: tableUploadList(doc) });
        }

        // GET /api/tables/:id/uploads — présence d'upload (repli sans SSE).
        if (parts.length === 2 && parts[1] === 'uploads') {
            if (req.method !== 'GET') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
            return sendJson(res, 200, { success: true, uploads: tableUploadList(doc) });
        }

        /* ------------------------------------------------------------
           VOIX LIVE — signalisation WebRTC (REST + SSE), média en P2P.
           Inspiré du VTTVoiceManager KT : VAD/PTT/continu + volumes côté
           client, autorité serveur pour mute/reset. Le serveur ne voit
           jamais l'audio : il relaie l'offre/réponse/ICE au destinataire.
           Un joueur muté pour la voix ne reçoit plus le vocal (ni signal
           ni média) ; seul un reset pour tout le monde rouvre.
           État éphémère (mémoire) : peers + muted par table.
           ------------------------------------------------------------ */
        // GET /api/tables/:id/voice — qui est en voix + qui est muté.
        if (parts.length === 2 && parts[1] === 'voice') {
            if (req.method !== 'GET') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
            const st = tableVoiceState(doc.id);
            return sendJson(res, 200, {
                success: true,
                inVoice: st.peers.has(myId),
                iAmMuted: st.muted.has(myId),
                peers: tableVoicePeersPayload(doc, st),
            });
        }

        // POST /api/tables/:id/voice-join | voice-leave — entrer/sortir.
        if (parts.length === 2 && (parts[1] === 'voice-join' || parts[1] === 'voice-leave')) {
            if (req.method !== 'POST') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
            const st = tableVoiceState(doc.id);
            if (parts[1] === 'voice-join') {
                if (st.muted.has(myId)) return sendJson(res, 403, { error: 'Vous êtes muté pour la voix : demandez au MJ un reset.', code: 'voice_muted' });
                if (st.peers.size >= Math.max(2, Math.min(24, doc.maxMembers || 12))) {
                    return sendJson(res, 403, { error: 'Salon vocal plein.' });
                }
                st.peers.set(myId, { username: me.username, joinedAt: Date.now() });
                sseNotifyVoice(doc, 'voice-presence', { peers: tableVoicePeersPayload(doc, st), joined: myId });
                console.log(`[Tables] ${me.username} rejoint la voix (${doc.id}).`);
            } else {
                st.peers.delete(myId);
                sseNotifyVoice(doc, 'voice-presence', { peers: tableVoicePeersPayload(doc, st), left: myId });
            }
            return sendJson(res, 200, { success: true, peers: tableVoicePeersPayload(doc, st) });
        }

        // POST /api/tables/:id/voice-signal — relais SDP/ICE ciblé.
        if (parts.length === 2 && parts[1] === 'voice-signal') {
            if (req.method !== 'POST') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
            const st = tableVoiceState(doc.id);
            if (!st.peers.has(myId)) return sendJson(res, 403, { error: 'Rejoignez d’abord le salon vocal.' });
            if (st.muted.has(myId)) return sendJson(res, 403, { error: 'Vous êtes muté : émission vocale coupée.', code: 'voice_muted' });
            try {
                const body = await parseBody(req);
                if (body && JSON.stringify(body).length > 32768) {
                    return sendJson(res, 413, { error: 'Signal trop volumineux.' });
                }
                const toUserId = String((body && body.toUserId) || '').slice(0, 64);
                const kind = String((body && body.kind) || '');
                const target = doc.members.find(m => m && m.userId === toUserId);
                if (!target) return sendJson(res, 404, { error: 'Destinataire non membre de la table.' });
                if (!st.peers.has(toUserId)) return sendJson(res, 404, { error: 'Destinataire hors du salon vocal.' });
                // Règle centrale : un joueur muté ne reçoit plus le vocal.
                if (st.muted.has(toUserId)) {
                    return sendJson(res, 403, { error: target.username + ' est muté : vocal non livré.', code: 'voice_muted' });
                }
                let data = null;
                if (tablesVoicePolicy) {
                    data = tablesVoicePolicy.sanitizeSignal(kind, body && body.data);
                    if (data === null) return sendJson(res, 400, { error: 'Signal invalide (kind offer|answer|ice, taille limitée).' });
                } else {
                    if (!['offer', 'answer', 'ice'].includes(kind) || typeof (body && body.data) !== 'string') {
                        return sendJson(res, 400, { error: 'Signal invalide.' });
                    }
                    data = String(body.data).slice(0, 16384);
                    if (!data) return sendJson(res, 400, { error: 'Signal vide.' });
                }
                sseNotifyVoice(doc, 'voice-signal', { fromUserId: myId, fromName: me.username, kind, data }, toUserId);
                return sendJson(res, 200, { success: true });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }

        // POST /api/tables/:id/voice-mute — muter (soi : micro ; MJ/helper :
        // n'importe qui, exclusion vocale : ne reçoit plus le vocal).
        if (parts.length === 2 && parts[1] === 'voice-mute') {
            if (req.method !== 'POST') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
            try {
                const body = await parseBody(req);
                const targetId = String((body && body.userId) || myId).slice(0, 64);
                const muted = !!(body && body.muted);
                const target = doc.members.find(m => m && m.userId === targetId);
                if (!target) return sendJson(res, 404, { error: 'Membre introuvable.' });
                const manager = (myRole === 'admin') || tableCanManage(doc, me);
                if (targetId !== myId && !manager) {
                    return sendJson(res, 403, { error: 'Seul le MJ / helper peut muter un autre joueur.' });
                }
                const st = tableVoiceState(doc.id);
                if (muted) st.muted.set(targetId, { by: myId, at: Date.now() });
                else st.muted.delete(targetId);
                sseNotifyVoice(doc, 'voice-mute', { userId: targetId, username: target.username, muted, by: myId });
                console.log(`[Tables] voix ${doc.id} : ${target.username} ${muted ? 'muté' : 'démuté'} par ${me.username}.`);
                return sendJson(res, 200, { success: true, peers: tableVoicePeersPayload(doc, st) });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }

        // POST /api/tables/:id/voice-reset — reset pour tout le monde
        // (MJ/helper) : vide la liste des mutés voix, tout le monde reçoit
        // à nouveau le vocal.
        if (parts.length === 2 && parts[1] === 'voice-reset') {
            if (req.method !== 'POST') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            if (!isMember) return sendJson(res, 403, { error: 'Table réservée à ses membres.' });
            if (!((myRole === 'admin') || tableCanManage(doc, me))) {
                return sendJson(res, 403, { error: 'Reset vocal réservé au MJ / helper.' });
            }
            const st = tableVoiceState(doc.id);
            st.muted.clear();
            sseNotifyVoice(doc, 'voice-reset', { by: myId, peers: tableVoicePeersPayload(doc, st) });
            console.log(`[Tables] voix ${doc.id} : reset pour tout le monde (par ${me.username}).`);
            return sendJson(res, 200, { success: true, peers: tableVoicePeersPayload(doc, st) });
        }

        return sendJson(res, 404, { error: 'Route tables inconnue.' });
    }

    /* ================================================================
       AMIS — liste d'amis par utilisateur (demandes + acceptation)
       ----------------------------------------------------------------
       Stockage auto : data/friends.json (+ .bak) :
       { "<userId>": { friends:[{userId,since}], incoming:[...], outgoing:[...] } }.
       Les pseudos sont résolus en direct depuis users.json à la lecture.
       ================================================================ */
    const FRIENDS_PATH = path.join(PUBLIC_DIR, 'data', 'friends.json');
    function loadFriendsDoc() {
        try {
            if (!fs.existsSync(FRIENDS_PATH)) return {};
            const d = JSON.parse(fs.readFileSync(FRIENDS_PATH, 'utf-8'));
            return (d && typeof d === 'object') ? d : {};
        } catch (e) { return {}; }
    }
    function saveFriendsDoc(d) {
        const json = JSON.stringify(d, null, 2);
        if (json.length > 4 * 1024 * 1024) return false;
        try { if (fs.existsSync(FRIENDS_PATH)) fs.copyFileSync(FRIENDS_PATH, FRIENDS_PATH + '.bak'); } catch (e) {}
        fs.writeFileSync(FRIENDS_PATH, json);
        return true;
    }
    function friendEntry(d, uid) {
        if (!d[uid] || typeof d[uid] !== 'object') d[uid] = { friends: [], incoming: [], outgoing: [] };
        const e = d[uid];
        if (!Array.isArray(e.friends)) e.friends = [];
        if (!Array.isArray(e.incoming)) e.incoming = [];
        if (!Array.isArray(e.outgoing)) e.outgoing = [];
        return e;
    }
    function friendName(uid) {
        const u = loadUsers().find(x => x.id === uid);
        return u ? u.username : '(parti)';
    }
    function friendListOf(d, uid, key, avatars) {
        return friendEntry(d, uid)[key]
            .map(r => ({ userId: r.userId, username: friendName(r.userId), avatar: (avatars && avatars[r.userId]) || null, since: r.since || 0 }))
            .sort((a, b) => a.username.localeCompare(b.username, 'fr'));
    }
    function friendLink(d, a, b) {
        const now = Date.now();
        const ea = friendEntry(d, a), eb = friendEntry(d, b);
        ea.friends = ea.friends.filter(r => r.userId !== b);
        eb.friends = eb.friends.filter(r => r.userId !== a);
        ea.friends.push({ userId: b, since: now });
        eb.friends.push({ userId: a, since: now });
        ea.incoming = ea.incoming.filter(r => r.userId !== b);
        ea.outgoing = ea.outgoing.filter(r => r.userId !== b);
        eb.incoming = eb.incoming.filter(r => r.userId !== a);
        eb.outgoing = eb.outgoing.filter(r => r.userId !== a);
    }
    function friendUnlink(d, a, b) {
        friendEntry(d, a).friends = friendEntry(d, a).friends.filter(r => r.userId !== b);
        friendEntry(d, b).friends = friendEntry(d, b).friends.filter(r => r.userId !== a);
    }
    const isFriendsRoute = cleanPath === '/api/friends' || cleanPath.startsWith('/api/friends/');
    if (isFriendsRoute) {
        const me = getSessionUser(req);
        if (!me) return sendJson(res, 401, { error: 'Connexion requise.' });
        const sub = cleanPath.slice('/api/friends'.length);
        const parts = sub.split('/').filter(Boolean);

        // GET /api/friends — mes amis + demandes reçues/envoyées.
        if (parts.length === 0) {
            if (req.method === 'GET') {
                const d = loadFriendsDoc();
                const avatars = buildAvatarMap();
                return sendJson(res, 200, {
                    success: true,
                    friends: friendListOf(d, me.id, 'friends', avatars),
                    incoming: friendListOf(d, me.id, 'incoming', avatars),
                    outgoing: friendListOf(d, me.id, 'outgoing', avatars)
                });
            }
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }

        // POST /api/friends/request {user} — demander en ami (pseudo ou e-mail).
        // POST /api/friends/accept|decline|cancel {userId}.
        if (parts.length === 1 && ['request', 'accept', 'decline', 'cancel'].includes(parts[0])) {
            if (req.method !== 'POST') return sendJson(res, 405, { error: 'Méthode non autorisée.' });
            try {
                const body = await parseBody(req);
                const action = parts[0];
                const d = loadFriendsDoc();
                const entry = friendEntry(d, me.id);
                if (action === 'request') {
                    const term = String((body && body.user) || '').trim().toLowerCase();
                    if (!term) return sendJson(res, 400, { error: 'Pseudo ou e-mail requis.' });
                    const target = loadUsers().find(u => u.username.toLowerCase() === term || u.email.toLowerCase() === term);
                    if (!target) return sendJson(res, 404, { error: 'Utilisateur introuvable.' });
                    if (target.id === me.id) return sendJson(res, 400, { error: 'Impossible de s’ajouter soi-même.' });
                    if (entry.friends.some(r => r.userId === target.id)) {
                        return sendJson(res, 409, { error: 'Déjà amis.' });
                    }
                    // Demande croisée : acceptation immédiate des deux côtés.
                    if (entry.incoming.some(r => r.userId === target.id)) {
                        friendLink(d, me.id, target.id);
                        if (!saveFriendsDoc(d)) return sendJson(res, 413, { error: 'Carnet trop volumineux.' });
                        return sendJson(res, 200, { success: true, linked: true, userId: target.id });
                    }
                    if (entry.outgoing.some(r => r.userId === target.id)) {
                        return sendJson(res, 409, { error: 'Demande déjà envoyée.' });
                    }
                    const now = Date.now();
                    entry.outgoing.push({ userId: target.id, since: now });
                    friendEntry(d, target.id).incoming.push({ userId: me.id, since: now });
                    if (!saveFriendsDoc(d)) return sendJson(res, 413, { error: 'Carnet trop volumineux.' });
                    console.log(`[Amis] demande : ${me.username} -> ${target.username}.`);
                    return sendJson(res, 200, { success: true, userId: target.id });
                }
                const uid = String((body && body.userId) || '').trim();
                if (!uid) return sendJson(res, 400, { error: 'userId requis.' });
                if (action === 'accept') {
                    if (!entry.incoming.some(r => r.userId === uid)) {
                        return sendJson(res, 404, { error: 'Demande introuvable.' });
                    }
                    friendLink(d, me.id, uid);
                    if (!saveFriendsDoc(d)) return sendJson(res, 413, { error: 'Carnet trop volumineux.' });
                    console.log(`[Amis] liés : ${me.username} <-> ${friendName(uid)}.`);
                    return sendJson(res, 200, { success: true, userId: uid });
                }
                if (action === 'decline') {
                    entry.incoming = entry.incoming.filter(r => r.userId !== uid);
                    friendEntry(d, uid).outgoing = friendEntry(d, uid).outgoing.filter(r => r.userId !== me.id);
                    if (!saveFriendsDoc(d)) return sendJson(res, 413, { error: 'Carnet trop volumineux.' });
                    return sendJson(res, 200, { success: true });
                }
                // cancel : retirer ma demande envoyée.
                entry.outgoing = entry.outgoing.filter(r => r.userId !== uid);
                friendEntry(d, uid).incoming = friendEntry(d, uid).incoming.filter(r => r.userId !== me.id);
                if (!saveFriendsDoc(d)) return sendJson(res, 413, { error: 'Carnet trop volumineux.' });
                return sendJson(res, 200, { success: true });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }

        // DELETE /api/friends/:userId — retirer un ami (des deux côtés).
        if (parts.length === 1) {
            if (req.method === 'DELETE') {
                const uid = parts[0];
                const d = loadFriendsDoc();
                friendUnlink(d, me.id, uid);
                if (!saveFriendsDoc(d)) return sendJson(res, 413, { error: 'Carnet trop volumineux.' });
                return sendJson(res, 200, { success: true });
            }
            return sendJson(res, 405, { error: 'Méthode non autorisée.' });
        }

        return sendJson(res, 404, { error: 'Route amis inconnue.' });
    }

    /* ================================================================
       ADMIN LOCAL — page + API, jamais servies par l'écouteur public.
       L'écouteur public répond 404 (ne révèle pas l'existence).
       L'écouteur admin (127.0.0.1:ADMIN_PORT) ne sert QUE ceci.
       ================================================================ */
    const isAdminPath = cleanPath === '/admin.html' || cleanPath === '/admin' || cleanPath.startsWith('/api/admin');
    if (isAdminPath && !isAdminListener) {
        res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
        return res.end('Fichier introuvable.');
    }
    if (isAdminListener) {
        if ((cleanPath === '/admin.html' || cleanPath === '/admin') && (req.method === 'GET' || req.method === 'HEAD')) {
            if (!isLoopbackHost(req)) {
                res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
                return res.end('Accès réservé à la machine locale.');
            }
            return serveStatic(req, res, '/admin.html');
        }
        if (cleanPath === '/api/admin/status' && req.method === 'GET') {
            const adm = requirePerm(req, 'admin.access');
            if (adm.status) return sendJson(res, adm.status, { error: adm.error });
            const users = loadUsers();
            return sendJson(res, 200, {
                success: true,
                admin: toSafeUser(adm.user),
                serverTime: new Date().toISOString(),
                uptimeSec: Math.floor(process.uptime()),
                node: process.version,
                ports: { public: PORT, admin: ADMIN_PORT },
                users: users.length,
                adminEmails: getAdminEmails(),
                rooms: vttRooms.size,
                env: {
                    google: !!(process.env.GOOGLE_CLIENT_ID && process.env.GOOGLE_CLIENT_SECRET),
                    discord: !!(process.env.DISCORD_CLIENT_ID && process.env.DISCORD_CLIENT_SECRET),
                    github: !!(process.env.GITHUB_CLIENT_ID && process.env.GITHUB_CLIENT_SECRET),
                    callbackBase: !!OAUTH_CALLBACK_BASE
                },
                protectedWrites: WRITE_GUARDS,
                catalog: PERMISSIONS
            });
        }
        if (cleanPath === '/api/admin/users' && req.method === 'GET') {
            const adm = requirePerm(req, 'users.manage');
            if (adm.status) return sendJson(res, adm.status, { error: adm.error });
            const list = loadUsers().map(u => {
                const s = toSafeUser(u);
                return {
                    id: s.id, username: s.username, email: s.email,
                    provider: s.provider || 'local', role: s.role, roleId: s.roleId,
                    isAdmin: s.isAdmin, permissions: s.permissions,
                    createdAt: s.createdAt,
                    characters: Array.isArray(u.characters) ? u.characters.length : 0
                };
            });
            return sendJson(res, 200, { success: true, users: list });
        }
        if (cleanPath === '/api/admin/set-admin' && (req.method === 'POST' || req.method === 'PUT')) {
            const adm = checkAdmin(req);
            if (adm.status) return sendJson(res, adm.status, { error: adm.error });
            try {
                const body = await parseBody(req);
                const email = String((body && body.email) || '').trim().toLowerCase();
                if (!email.includes('@')) return sendJson(res, 400, { error: 'E-mail invalide.' });
                let list = getAdminEmails();
                if (body.admin && !list.includes(email)) list.push(email);
                if (!body.admin) list = list.filter(e => e !== email);
                const updated = persistAdminEmails(list);
                console.log(`[Admin] allowlist : ${updated.join(', ') || '(vide)'}`);
                return sendJson(res, 200, { success: true, adminEmails: updated });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        if (cleanPath === '/api/admin/roles' && req.method === 'GET') {
            const adm = requirePerm(req, 'roles.manage');
            if (adm.status) return sendJson(res, adm.status, { error: adm.error });
            const doc = loadRoles();
            const counts = {};
            Object.values(doc.assignments).forEach(rid => { counts[rid] = (counts[rid] || 0) + 1; });
            return sendJson(res, 200, { success: true, catalog: PERMISSIONS,
                roles: doc.roles.map(r => ({ id: r.id, name: r.name, desc: r.desc || '',
                    builtin: !!r.builtin, permissions: r.permissions || [], users: counts[r.id] || 0 })) });
        }
        if (cleanPath === '/api/admin/role/save' && (req.method === 'POST' || req.method === 'PUT')) {
            const adm = requirePerm(req, 'roles.manage');
            if (adm.status) return sendJson(res, adm.status, { error: adm.error });
            try {
                const body = await parseBody(req);
                const name = String((body && body.name) || '').trim().slice(0, 40);
                if (!name) return sendJson(res, 400, { error: 'Nom requis.' });
                const doc = loadRoles();
                // Anti-escalade : on ne peut accorder que ses propres permissions (sauf allowlist).
                const grantable = isAdminEmail(adm.user.email) ? ALL_PERMS : getUserPermissions(adm.user);
                const perms = [...new Set((Array.isArray(body.permissions) ? body.permissions : [])
                    .filter(x => ALL_PERMS.includes(x) && grantable.includes(x)))];
                const id = String((body && body.id) || '').trim();
                if (id) {
                    const role = doc.roles.find(r => r.id === id);
                    if (!role) return sendJson(res, 404, { error: 'Rôle introuvable.' });
                    if (role.builtin) return sendJson(res, 400, { error: 'Rôle système non modifiable.' });
                    role.name = name;
                    role.desc = String((body && body.desc) || '').trim().slice(0, 200);
                    role.permissions = perms;
                    console.log(`[Admin] rôle modifié : ${role.id} (${perms.join(', ') || 'aucune'})`);
                } else {
                    let slug = name.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '')
                        .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 32) || ('role-' + Date.now().toString(36));
                    let n = 1; const base = slug;
                    while (doc.roles.some(r => r.id === slug)) slug = base + '-' + (++n);
                    doc.roles.push({ id: slug, name, desc: String((body && body.desc) || '').trim().slice(0, 200), permissions: perms });
                    console.log(`[Admin] rôle créé : ${slug} (${perms.join(', ') || 'aucune'})`);
                }
                saveRoles(doc);
                return sendJson(res, 200, { success: true });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        if (cleanPath === '/api/admin/role/delete' && (req.method === 'POST' || req.method === 'DELETE')) {
            const adm = requirePerm(req, 'roles.manage');
            if (adm.status) return sendJson(res, adm.status, { error: adm.error });
            try {
                const body = await parseBody(req);
                const id = String((body && body.id) || '');
                const doc = loadRoles();
                const role = doc.roles.find(r => r.id === id);
                if (!role) return sendJson(res, 404, { error: 'Rôle introuvable.' });
                if (role.builtin) return sendJson(res, 400, { error: 'Rôle système non supprimable.' });
                doc.roles = doc.roles.filter(r => r.id !== id);
                Object.keys(doc.assignments).forEach(uid => { if (doc.assignments[uid] === id) delete doc.assignments[uid]; });
                saveRoles(doc);
                console.log(`[Admin] rôle supprimé : ${id}`);
                return sendJson(res, 200, { success: true });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        if (cleanPath === '/api/admin/assign' && (req.method === 'POST' || req.method === 'PUT')) {
            const adm = requirePerm(req, 'roles.manage');
            if (adm.status) return sendJson(res, adm.status, { error: adm.error });
            try {
                const body = await parseBody(req);
                const userId = String((body && body.userId) || '');
                const roleId = String((body && body.roleId) || '');
                const users = loadUsers();
                const target = users.find(u => u.id === userId);
                if (!target) return sendJson(res, 404, { error: 'Utilisateur introuvable.' });
                if (target.id === adm.user.id && !isAdminEmail(adm.user.email)) return sendJson(res, 400, { error: 'Impossible de modifier votre propre rôle.' });
                const doc = loadRoles();
                if (roleId) {
                    const role = doc.roles.find(r => r.id === roleId);
                    if (!role) return sendJson(res, 404, { error: 'Rôle introuvable.' });
                    const grantable = isAdminEmail(adm.user.email) ? ALL_PERMS : getUserPermissions(adm.user);
                    const missing = (role.permissions || []).filter(x => !grantable.includes(x));
                    if (missing.length) return sendJson(res, 403, { error: 'Rôle hors de votre portée : ' + missing.join(', ') });
                    doc.assignments[userId] = roleId;
                } else {
                    delete doc.assignments[userId];
                }
                saveRoles(doc);
                console.log(`[Admin] rôle ${roleId || 'joueur (retiré)'} assigné à ${target.username}`);
                return sendJson(res, 200, { success: true, roleId: roleId || null });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        if (cleanPath === '/api/admin/delete-user' && (req.method === 'POST' || req.method === 'DELETE')) {
            const adm = requirePerm(req, 'users.manage');
            if (adm.status) return sendJson(res, adm.status, { error: adm.error });
            try {
                const body = await parseBody(req);
                const id = String((body && body.id) || '');
                const users = loadUsers();
                const target = users.find(u => u.id === id);
                if (!target) return sendJson(res, 404, { error: 'Utilisateur introuvable.' });
                if (target.id === adm.user.id) return sendJson(res, 400, { error: 'Impossible de supprimer votre propre compte.' });
                saveUsers(users.filter(u => u.id !== id));
                console.log(`[Admin] utilisateur supprimé : ${target.username} (${target.email})`);
                return sendJson(res, 200, { success: true, users: users.length - 1 });
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        if (cleanPath === '/api/admin/reset-password' && (req.method === 'POST' || req.method === 'PUT')) {
            const adm = requirePerm(req, 'users.manage');
            if (adm.status) return sendJson(res, adm.status, { error: adm.error });
            try {
                const body = await parseBody(req);
                const id = String((body && body.id) || '');
                const users = loadUsers();
                const target = users.find(u => u.id === id);
                if (!target) return sendJson(res, 404, { error: 'Utilisateur introuvable.' });
                if (target.provider !== 'local') return sendJson(res, 400, { error: 'Compte ' + target.provider + ' (OAuth) : aucun mot de passe à réinitialiser.' });
                let newPass = String((body && body.password) || '');
                let generated = false;
                if (!newPass) {
                    newPass = crypto.randomBytes(12).toString('base64url');
                    generated = true;
                }
                if (newPass.length < 6) return sendJson(res, 400, { error: '6 caractères minimum.' });
                const saltBuffer = crypto.randomBytes(16);
                target.saltHex = saltBuffer.toString('hex');
                target.passwordHashHex = hashPassword(newPass, saltBuffer);
                saveUsers(users);
                console.log(`[Admin] mot de passe réinitialisé : ${target.username} (${target.email})`);
                const out = { success: true, username: target.username };
                if (generated) out.tempPassword = newPass;
                return sendJson(res, 200, out);
            } catch (e) {
                return sendJson(res, 400, { error: e.message });
            }
        }
        if (cleanPath === '/api/admin/export-users' && req.method === 'GET') {
            const adm = requirePerm(req, 'users.manage');
            if (adm.status) return sendJson(res, adm.status, { error: adm.error });
            res.writeHead(200, {
                'Content-Type': 'application/json; charset=utf-8',
                'Content-Disposition': 'attachment; filename="users-backup.json"'
            });
            return res.end(JSON.stringify(loadUsers(), null, 2));
        }
        res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
        return res.end('Fichier introuvable.');
    }

    if (req.method === 'GET' || req.method === 'HEAD') {
        return serveStatic(req, res, parsedUrl.pathname);
    }

    res.writeHead(405, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Méthode non autorisée.');
}

const server = http.createServer((req, res) => {
    handleRequest(req, res, false).catch(() => {
        try { sendJson(res, 500, { error: 'Erreur interne.' }); } catch (_) {}
    });
});
const adminServer = http.createServer((req, res) => {
    handleRequest(req, res, true).catch(() => {
        try { sendJson(res, 500, { error: 'Erreur interne.' }); } catch (_) {}
    });
});

/* ==========================================================================
   VTT MULTIPLAYER HUB — Virtual Table for Killtime Tactics (ZERO DEPENDENCY)
   --------------------------------------------------------------------------
   Transport : WebSocket natif (RFC 6455) greffé sur le même serveur HTTP.
   Endpoint  : ws://<host>:<port>/ws/vtt   (wss derrière ngrok/reverse-proxy)

   Autorité  : hub relais + garde-fous GM. Pas de simulation côté serveur
   pour le MVP : le serveur gère rooms / présence / rôles et relaie les
   ops de table (`op`) aux membres de la room. La résolution des règles
   (PA, dés, combat) reste côté Unity (TurnManager / CombatCalculator).

   Protocole v1 (JSON texte, un objet par frame) :
     C -> S : hello | create_room | join_room | leave_room | set_role |
              kick | op | ping
     S -> C : welcome | room_created | room_joined | presence |
              role_updated | kicked | op | error | pong
   ========================================================================== */

const VTT_WS_PATH = '/ws/vtt';
const VTT_WS_GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';
const VTT_MAX_PAYLOAD = 512 * 1024; // 512 Ko max par message
const vttClients = new Map(); // clientId -> conn
const vttPresence = new Map(); // userId -> {userId, username, at} (heartbeat /api/presence, expire 45 s)
const sseSubs = new Set(); // abonnés push tables : {userId, email, res} (GET /api/tables/stream)
const vttRooms = new Map();   // CODE -> { code, createdAt, gmId, members: Map(clientId -> {username, role, userId}), units, clientUnits, covers }
let vttClientSeq = 0;

// Brouillard de guerre asymétrique : même règle portée 360°/murs que le client
// Unity (FogOfWarSystem). Le hub ne transmet la position d'un ennemi à un client
// que si elle est dans le champ d'au moins un de ses avatars (anti map-hack).
let vttVis = null;
try {
    vttVis = require('./vtt_visibility.js');
} catch (e) {
    console.warn('[VTT] vtt_visibility.js introuvable : filtrage asymétrique désactivé.');
}

// RD-066 : autorité GM + validation des ops (parité VTTProtocol.IsGMOp).
// combat_action et worldmap sont GM-only comme côté Unity ; unit_move/chat/
// dice restent ouverts (joueurs), avec validation de payload.
let vttPolicy = null;
try {
    vttPolicy = require('./vtt_hub_policy.js');
} catch (e) {
    console.warn('[VTT] vtt_hub_policy.js introuvable : garde-fous GM réduits.');
}

// Voix live des tables de groupe (sysRP, inspiré du VTTVoiceManager KT) :
// signalisation REST + SSE, média P2P WebRTC (le serveur ne voit jamais
// l'audio). Mémoire seule (éphémère) : tableId -> { peers, muted }.
// Un joueur muté pour la voix ne reçoit plus le vocal ; seul un reset
// pour tout le monde (MJ/helper) rouvre.
const tableVoice = new Map(); // tableId -> { peers: Map(userId -> {username, joinedAt}), muted: Map(userId -> {by, at}) }
let tablesVoicePolicy = null;
try {
    tablesVoicePolicy = require('./tables_voice.js');
} catch (e) {
    console.warn('[Tables] tables_voice.js introuvable : garde-fous voix réduits.');
}
function tableVoiceState(tableId) {
    const id = String(tableId || '').toLowerCase();
    let st = tableVoice.get(id);
    if (!st) {
        st = { peers: new Map(), muted: new Map() };
        tableVoice.set(id, st);
    }
    return st;
}

// Présence d'upload : qui est en train d'envoyer un fichier sur une table
// (animation « untel envoie… » chez tous les membres). Éphémère, expiré
// après 120 s, effacé à l'envoi du message.
const tableUploads = {}; // tableId -> { userId -> { username, kind, startedAt } }
const TABLE_UPLOAD_TTL_MS = 120000;

function vttGenClientId() {
    vttClientSeq += 1;
    return 'c_' + Date.now().toString(36) + '_' + vttClientSeq.toString(36) + Math.floor(Math.random() * 1296).toString(36);
}

function vttGenRoomCode() {
    const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    for (let attempt = 0; attempt < 50; attempt++) {
        let code = '';
        for (let i = 0; i < 6; i++) code += alphabet[Math.floor(Math.random() * alphabet.length)];
        if (!vttRooms.has(code)) return code;
    }
    return 'R' + Date.now().toString(36).toUpperCase().slice(-5);
}

function vttNormCode(code) {
    return String(code || '').trim().toUpperCase().slice(0, 12);
}

function vttSanitizeUsername(name, fallbackId) {
    let s = String(name || '').trim().slice(0, 24);
    if (!s) s = 'Invité-' + String(fallbackId || '0000').slice(-4);
    // Évite les injections dans les logs / le JSON : on garde du texte brut.
    return s.replace(/[\u0000-\u001F\u007F]/g, '');
}

function vttResolveUserFromToken(authToken) {
    // Format émis par ce même serveur : 'srv_' + base64(userId:timestamp)
    if (!authToken || typeof authToken !== 'string' || !authToken.startsWith('srv_')) return null;
    try {
        const decoded = Buffer.from(authToken.slice(4), 'base64').toString('utf8');
        const userId = decoded.split(':')[0];
        if (!userId) return null;
        const users = loadUsers();
        return users.find(u => u.id === userId) || null;
    } catch (e) {
        return null;
    }
}

function vttRoomMembersPayload(room) {
    const out = [];
    for (const [clientId, m] of room.members) {
        out.push({ clientId, username: m.username, role: m.role, userId: m.userId || null });
    }
    // GM en premier pour un affichage stable côté Unity.
    out.sort((a, b) => (a.role === 'gm' ? -1 : 1) - (b.role === 'gm' ? -1 : 1));
    return out;
}

function vttSendFrame(socket, data, opcode = 0x1) {
    if (socket.destroyed || !socket.writable) return false;
    const payload = Buffer.isBuffer(data) ? data : Buffer.from(String(data), 'utf8');
    const len = payload.length;
    let header;
    if (len < 126) {
        header = Buffer.allocUnsafe(2);
        header[0] = 0x80 | (opcode & 0x0f);
        header[1] = len;
    } else if (len < 65536) {
        header = Buffer.allocUnsafe(4);
        header[0] = 0x80 | (opcode & 0x0f);
        header[1] = 126;
        header.writeUInt16BE(len, 2);
    } else {
        header = Buffer.allocUnsafe(10);
        header[0] = 0x80 | (opcode & 0x0f);
        header[1] = 127;
        header.writeBigUInt64BE(BigInt(len), 2);
    }
    try {
        socket.write(Buffer.concat([header, payload]));
        return true;
    } catch (e) {
        return false;
    }
}

function vttSendJson(conn, obj) {
    if (!conn || !conn.socket) return false;
    try {
        return vttSendFrame(conn.socket, JSON.stringify(obj), 0x1);
    } catch (e) {
        return false;
    }
}

function vttSendError(conn, message, code = 'bad_request') {
    return vttSendJson(conn, { type: 'error', code, message: String(message || 'Requête invalide.') });
}

function vttBroadcast(room, obj, exceptId = null) {
    if (!room) return;
    for (const clientId of room.members.keys()) {
        if (exceptId && clientId === exceptId) continue;
        const conn = vttClients.get(clientId);
        if (conn) vttSendJson(conn, obj);
    }
}

/* ================================================================
   BROUILLARD DE GUERRE ASYMÉTRIQUE (filtrage serveur anti map-hack)
   ----------------------------------------------------------------
   Le hub mémorise les positions autoritaires (unit_move / combat_action /
   state_sync / map_load) et les avatars possédés par chaque client
   (unit_claim + action_request.actorId). À l'envoi, la position d'un ennemi
   n'est transmise à un destinataire que si elle est dans le champ 360° d'au
   moins un de ses avatars (portée d'ambiance de la room + murs Full).
   - unit_move (pure position) :paquet SUPPRIMÉ pour les aveugles (le fantôme
     local garde la dernière position connue).
   - combat_action (effets + logs) : transmis à tous, mais EXPURGÉ (positions
     à zéro, chemin retiré) pour les aveugles — les logs restent publics.
   - state_sync : entrées ennemies invisibles RETIRÉES par destinataire ;
     diffuse aussi la portée d'ambiance du MJ (sightRange).
   - map_load : snapshot initial transmis complet (pose publique de table),
     mais enregistré pour le filtrage des mouvements suivants.
   Sans vtt_visibility.js ou sans positions connues : repli ouvert (relais).
   ================================================================ */

function vttFogUnits(room) {
    if (!room.units) room.units = new Map(); // unitId -> {q,r,vision,yaw,pano,thermal,ouie,isPlayer}
    return room.units;
}

function vttFogClaims(room) {
    if (!room.clientUnits) room.clientUnits = new Map(); // clientId -> Set(unitId)
    return room.clientUnits;
}

function vttFogCovers(room) {
    if (!room.covers) room.covers = new Map(); // "q,r" -> coverInt
    return room.covers;
}

function vttNum(v, fallback) {
    const n = Number(v);
    return Number.isFinite(n) ? n : fallback;
}

function vttTrackUnit(room, unitId, fields) {
    if (!unitId || typeof unitId !== 'string') return;
    const units = vttFogUnits(room);
    const prev = units.get(unitId) || {};
    const next = { ...prev };
    if (fields.q !== undefined) next.q = vttNum(fields.q, prev.q ?? 0);
    if (fields.r !== undefined) next.r = vttNum(fields.r, prev.r ?? 0);
    if (fields.vision !== undefined) next.vision = vttNum(fields.vision, prev.vision ?? 6);
    if (fields.yaw !== undefined) next.yaw = vttNum(fields.yaw, prev.yaw ?? 0);
    if (fields.pano !== undefined) next.pano = fields.pano ? 1 : 0;
    if (fields.thermal !== undefined) next.thermal = fields.thermal ? 1 : 0;
    if (fields.ouie !== undefined) next.ouie = vttNum(fields.ouie, prev.ouie ?? 3);
    if (fields.isPlayer !== undefined) next.isPlayer = !!fields.isPlayer;
    units.set(unitId, next);
}

function vttLearnClaims(room, clientId, unitIds) {
    const claims = vttFogClaims(room);
    let set = claims.get(clientId);
    if (!set) {
        set = new Set();
        claims.set(clientId, set);
    }
    if (!Array.isArray(unitIds)) return set;
    for (const raw of unitIds.slice(0, 32)) {
        const id = String(raw || '').slice(0, 64);
        if (id) set.add(id);
    }
    return set;
}

// Observateurs d'un destinataire : avatars revendiqués (positions connues).
// Repli coopératif : sans revendication, toutes les unités IsPlayer connues.
// Portée = portée d'ambiance de la room (GM via state_sync, défaut longue).
function vttRoomSightRange(room) {
    if (vttVis && room && Number.isFinite(room.sightRange)) {
        return vttVis.clampSightRange(room.sightRange, vttVis.LONG_SIGHT_RANGE);
    }
    if (vttVis) return vttVis.LONG_SIGHT_RANGE;
    return 12;
}

function vttObserversFor(room, viewerId) {
    const units = vttFogUnits(room);
    const claims = vttFogClaims(room).get(viewerId);
    const sight = vttRoomSightRange(room);
    const out = [];
    const push = (id, st) => {
        if (st === undefined) return;
        if (!Number.isFinite(st.q) || !Number.isFinite(st.r)) return;
        out.push({
            q: st.q, r: st.r,
            vision: sight,
            thermal: !!st.thermal,
            ouie: vttNum(st.ouie, 3),
        });
    };
    if (claims && claims.size > 0) {
        for (const id of claims) {
            const st = units.get(id);
            if (st) push(id, st);
        }
        if (out.length > 0) return out;
    }
    // Repli strict : seules les unites de faction IsPlayer avérée observent.
    // (map_load et state_sync la renseignent ; l'inconnu n'observe jamais.)
    for (const [id, st] of units) {
        if (st.isPlayer === true) push(id, st);
    }
    return out;
}

function vttIsGMViewer(room, viewerId) {
    if (!room) return false;
    if (room.gmId === viewerId) return true;
    const m = room.members.get(viewerId);
    return !!m && m.role === 'gm';
}

// Le destinataire voit-il la case (q,r) ? GM = toujours. Sans module de
// visibilité ou sans observateur connu : repli ouvert (visible).
function vttCanViewerSee(room, viewerId, q, r) {
    if (vttIsGMViewer(room, viewerId)) return true;
    if (!vttVis) return true;
    const observers = vttObserversFor(room, viewerId);
    if (observers.length === 0) return true; // positions encore inconnues : ouvert.
    try {
        return vttVis.isVisibleToAny(observers, { q, r }, vttFogCovers(room));
    } catch (e) {
        return true;
    }
}

// Tir bruyant audible ? Un observateur dans son rayon d'Ouïe sans mur Full.
function vttIsAudibleTo(room, viewerId, q, r) {
    if (vttIsGMViewer(room, viewerId)) return true;
    if (!vttVis) return false;
    const observers = vttObserversFor(room, viewerId);
    const covers = vttFogCovers(room);
    for (const o of observers) {
        const d = vttVis.hexDistance(o.q, o.r, q, r);
        if (d <= 1) return true;
        if (d <= (o.ouie || 3)) {
            try {
                if (!vttVis.isBlockedByWall(o.q, o.r, q, r, covers)) return true;
            } catch (e) { return true; }
        }
    }
    return false;
}

function vttLearnMap(room, map) {
    if (!map || typeof map !== 'object') return;
    const covers = vttFogCovers(room);
    if (Array.isArray(map.ModifiedTiles)) {
        for (const t of map.ModifiedTiles.slice(0, 4000)) {
            if (!t || !Number.isFinite(t.Q) || !Number.isFinite(t.R)) continue;
            covers.set(t.Q + ',' + t.R, vttNum(t.Cover, 0));
        }
    }
    if (Array.isArray(map.PlacedProps)) {
        for (const p of map.PlacedProps.slice(0, 4000)) {
            if (!p || !Number.isFinite(p.Q) || !Number.isFinite(p.R)) continue;
            const c = vttNum(p.Cover, 2);
            const key = p.Q + ',' + p.R;
            if (c === 2) covers.set(key, 2);
            else if (!covers.has(key) && c !== 0) covers.set(key, c);
        }
    }
    if (Array.isArray(map.PlacedUnits)) {
        for (const u of map.PlacedUnits.slice(0, 64)) {
            if (!u || !u.UnitId) continue;
            let vision = 6;
            let ouie = 3;
            try {
                const attrs = u.Sheet && u.Sheet.BaseAttributes;
                if (attrs) {
                    if (Number.isFinite(attrs.Vision)) vision = attrs.Vision;
                    if (Number.isFinite(attrs.Ouie)) ouie = attrs.Ouie;
                }
            } catch (e) { /* repli */ }
            vttTrackUnit(room, String(u.UnitId), {
                q: u.Q, r: u.R, vision, ouie, isPlayer: u.IsPlayer !== false,
            });
        }
    }
}

// Envoi filtre par destinataire : selectPayload(viewerId) retourne le payload
// a transmettre, ou null pour ne rien envoyer (fantome local conserve).
function vttSendFogFiltered(room, senderConn, op, fallbackPayload, selectPayload) {
    if (!room) return;
    const at = Date.now();
    for (const viewerId of room.members.keys()) {
        let out;
        try {
            out = selectPayload(viewerId);
        } catch (e) {
            out = fallbackPayload;
        }
        if (out === null || out === undefined) continue; // aveugle : silence radio.
        const conn = vttClients.get(viewerId);
        if (conn) vttSendJson(conn, {
            type: 'op',
            from: senderConn.id,
            fromName: senderConn.username,
            fromRole: senderConn.role,
            op,
            payload: out,
            at
        });
    }
}

function vttBroadcastPresence(room) {
    vttBroadcast(room, {
        type: 'presence',
        code: room.code,
        tableName: room.tableName || room.code,
        visibility: room.visibility || 'public',
        maxPlayers: room.maxPlayers || 6,
        buildVersion: room.buildVersion || 'unknown',
        members: vttRoomMembersPayload(room)
    });
}

function vttLeaveRoom(conn, reason = 'leave') {
    const code = conn.roomCode;
    if (!code) return;
    const room = vttRooms.get(code);
    conn.roomCode = null;
    conn.role = null;
    if (!room) return;
    const wasGM = (room.gmId === conn.id);
    room.members.delete(conn.id);
    if (room.clientUnits) room.clientUnits.delete(conn.id);
    room.lastActive = Date.now();
    if (room.members.size === 0) {
        vttRooms.delete(code);
        console.log(`[VTT] Room ${code} fermée (${reason}, vide).`);
        return;
    }
    if (wasGM) {
        // Promotion déterministe : le membre le plus ancien devient GM.
        const nextId = room.members.keys().next().value;
        room.gmId = nextId;
        const next = room.members.get(nextId);
        if (next) next.role = 'gm';
        const nextConn = vttClients.get(nextId);
        if (nextConn) nextConn.role = 'gm';
        console.log(`[VTT] Room ${code} : GM parti, nouveau GM ${next ? next.username : nextId}.`);
        vttBroadcast(room, {
            type: 'role_updated',
            code,
            targetId: nextId,
            role: 'gm',
            reason: 'gm_left_promotion',
            members: vttRoomMembersPayload(room)
        });
    }
    vttBroadcastPresence(room);
}

function vttJoinRoom(conn, code, username) {
    code = vttNormCode(code);
    const room = vttRooms.get(code);
    if (!room) {
        vttSendError(conn, `Room introuvable : ${code}`, 'room_not_found');
        return;
    }
    if (conn.roomCode && conn.roomCode !== code) vttLeaveRoom(conn, 'switch_room');
    const limit = room.maxPlayers || 6;
    if (!room.members.has(conn.id) && room.members.size >= limit) {
        vttSendError(conn, `Table complète (${room.members.size}/${limit}).`, 'room_full');
        return;
    }
    const name = vttSanitizeUsername(username || conn.username, conn.id);
    conn.username = name;
    conn.roomCode = code;
    const isGM = (room.gmId === conn.id);
    conn.role = isGM ? 'gm' : 'player';
    room.members.set(conn.id, { username: name, role: conn.role, userId: conn.userId || null });
    if (!room.clientUnits) room.clientUnits = new Map();
    if (!room.clientUnits.has(conn.id)) room.clientUnits.set(conn.id, new Set());
    room.lastActive = Date.now();
    console.log(`[VTT] ${name} (${conn.id}) rejoint ${code} en tant que ${conn.role}.`);
    vttSendJson(conn, {
        type: 'room_joined',
        code,
        role: conn.role,
        tableName: room.tableName,
        visibility: room.visibility || 'public',
        maxPlayers: limit,
        buildVersion: room.buildVersion || 'unknown',
        members: vttRoomMembersPayload(room)
    });
    vttBroadcastPresence(room);
}

function vttHandleMessage(conn, msg) {
    if (!msg || typeof msg.type !== 'string') {
        vttSendError(conn, 'Message sans champ "type".');
        return;
    }
    const t = msg.type;

    if (t === 'hello') {
        const linked = msg.authToken ? vttResolveUserFromToken(msg.authToken) : null;
        if (linked) {
            conn.userId = linked.id;
            conn.username = vttSanitizeUsername(msg.username || linked.username, conn.id);
        } else if (msg.username) {
            conn.username = vttSanitizeUsername(msg.username, conn.id);
        }
        if (typeof msg.buildVersion === 'string' && msg.buildVersion.trim()) {
            conn.buildVersion = msg.buildVersion.trim().slice(0, 32);
        }
        vttSendJson(conn, {
            type: 'welcome',
            clientId: conn.id,
            username: conn.username,
            userId: conn.userId || null
        });
        return;
    }

    if (t === 'ping') {
        vttSendJson(conn, { type: 'pong', at: Date.now() });
        return;
    }

    if (t === 'create_room') {
        if (msg.username) conn.username = vttSanitizeUsername(msg.username, conn.id);
        const linked = msg.authToken ? vttResolveUserFromToken(msg.authToken) : null;
        if (linked && !conn.userId) {
            conn.userId = linked.id;
            if (!msg.username) conn.username = vttSanitizeUsername(linked.username, conn.id);
        }
        if (conn.roomCode) vttLeaveRoom(conn, 'create_new');
        const code = vttGenRoomCode();
        // Métadonnées d'annuaire : visibilité publique par défaut (server browser
        // sans tiers), le GM peut repasser en privé via `set_visibility`.
        let tableName = String(msg.tableName || '').replace(/[\u0000-\u001F\u007F]/g, '').trim().slice(0, 40);
        if (!tableName) tableName = `Table de ${conn.username}`;
        const visibility = (msg.visibility === 'private') ? 'private' : 'public';
        let maxPlayers = parseInt(msg.maxPlayers, 10);
        if (!Number.isFinite(maxPlayers)) maxPlayers = 6;
        maxPlayers = Math.max(2, Math.min(12, maxPlayers));
        const now = Date.now();
        const room = {
            code, createdAt: now, lastActive: now, gmId: conn.id,
            tableName, visibility, maxPlayers,
            buildVersion: conn.buildVersion || 'unknown',
            members: new Map(),
            // Brouillard asymétrique : état autoritaire (positions, murs, claims).
            units: new Map(),
            clientUnits: new Map(),
            covers: new Map(),
            // Sync tour/round (RD-066) : suivi hub pour l'annuaire.
            currentRound: 1,
            activeUnitId: null,
            lastTurnAction: null
        };
        vttRooms.set(code, room);
        conn.roomCode = code;
        conn.role = 'gm';
        room.members.set(conn.id, { username: conn.username, role: 'gm', userId: conn.userId || null });
        console.log(`[VTT] Room ${code} ("${tableName}", ${visibility}, build ${room.buildVersion}) créée par ${conn.username} (${conn.id}).`);
        vttSendJson(conn, {
            type: 'room_created', code, role: 'gm',
            tableName, visibility, maxPlayers, buildVersion: room.buildVersion
        });
        vttBroadcastPresence(room);
        return;
    }

    if (t === 'join_room') {
        vttJoinRoom(conn, msg.code, msg.username);
        return;
    }

    if (t === 'leave_room') {
        vttLeaveRoom(conn, 'leave');
        vttSendJson(conn, { type: 'room_left', code: msg.code || null });
        return;
    }

    if (t === 'set_role') {
        const room = vttRooms.get(conn.roomCode || '');
        if (!room) { vttSendError(conn, 'Vous n’êtes dans aucune room.', 'not_in_room'); return; }
        if (conn.role !== 'gm' && room.gmId !== conn.id) {
            vttSendError(conn, 'Seul le GM peut changer les rôles.', 'forbidden');
            return;
        }
        const targetId = String(msg.targetId || '');
        const role = String(msg.role || '');
        if (!room.members.has(targetId)) { vttSendError(conn, 'Membre introuvable.', 'member_not_found'); return; }
        if (role !== 'gm' && role !== 'player') { vttSendError(conn, 'Rôle invalide (gm|player).'); return; }
        if (role === 'gm') {
            // Transfert de GM : un seul GM à la fois (autorité claire de la table).
            const oldGM = room.gmId;
            if (room.members.has(oldGM)) room.members.get(oldGM).role = 'player';
            const oldConn = vttClients.get(oldGM);
            if (oldConn && oldConn.roomCode === room.code) oldConn.role = 'player';
            room.gmId = targetId;
        }
        room.members.get(targetId).role = role;
        const targetConn = vttClients.get(targetId);
        if (targetConn) targetConn.role = role;
        console.log(`[VTT] Room ${room.code} : ${targetId} -> ${role} (par ${conn.id}).`);
        vttBroadcast(room, {
            type: 'role_updated',
            code: room.code,
            targetId,
            role,
            members: vttRoomMembersPayload(room)
        });
        return;
    }

    if (t === 'kick') {
        const room = vttRooms.get(conn.roomCode || '');
        if (!room) { vttSendError(conn, 'Vous n’êtes dans aucune room.', 'not_in_room'); return; }
        if (conn.role !== 'gm' && room.gmId !== conn.id) {
            vttSendError(conn, 'Seul le GM peut expulser.', 'forbidden');
            return;
        }
        const targetId = String(msg.targetId || '');
        if (targetId === conn.id) { vttSendError(conn, 'Le GM ne peut pas s’expulser lui-même.'); return; }
        if (!room.members.has(targetId)) { vttSendError(conn, 'Membre introuvable.', 'member_not_found'); return; }
        const targetConn = vttClients.get(targetId);
        room.members.delete(targetId);
        if (targetConn) {
            targetConn.roomCode = null;
            targetConn.role = null;
            vttSendJson(targetConn, { type: 'kicked', code: room.code, reason: String(msg.reason || 'kicked_by_gm') });
        }
        console.log(`[VTT] Room ${room.code} : ${targetId} expulsé par ${conn.id}.`);
        vttBroadcastPresence(room);
        return;
    }

    if (t === 'set_visibility') {
        const room = vttRooms.get(conn.roomCode || '');
        if (!room) { vttSendError(conn, 'Vous n’êtes dans aucune room.', 'not_in_room'); return; }
        if (conn.role !== 'gm' && room.gmId !== conn.id) {
            vttSendError(conn, 'Seul le GM peut changer la visibilité.', 'forbidden');
            return;
        }
        const visibility = (msg.visibility === 'private') ? 'private' : 'public';
        room.visibility = visibility;
        room.lastActive = Date.now();
        console.log(`[VTT] Room ${room.code} : visibilité -> ${visibility} (par ${conn.id}).`);
        vttBroadcast(room, { type: 'visibility_updated', code: room.code, visibility });
        vttBroadcastPresence(room);
        return;
    }

    if (t === 'op') {
        // Relais d'opération de table (Virtual Table) : movement, dés, chat, tour...
        // Le serveur ne simule pas les règles, il authentifie l'expéditeur et diffuse.
        const room = vttRooms.get(conn.roomCode || '');
        if (!room) { vttSendError(conn, 'Rejoignez une room avant d’envoyer des ops.', 'not_in_room'); return; }
        const op = String(msg.op || '');
        if (!op || op.length > 64) { vttSendError(conn, 'Champ "op" manquant ou invalide.'); return; }
        // Garde-fou GM (RD-066, parité VTTProtocol.IsGMOp) : turn_control,
        // combat_action, scene_control, room_settings, map_load, worldmap,
        // drop_weapon, pickup_weapon, kick_weapon (RD-089).
        if (vttPolicy) {
            const verdict = vttPolicy.shouldRejectOp(room, conn, op);
            if (verdict.reject) { vttSendError(conn, verdict.message, verdict.code); return; }
        } else if (['turn_control', 'scene_control', 'room_settings', 'map_load', 'drop_weapon', 'pickup_weapon', 'kick_weapon'].includes(op)
            && conn.role !== 'gm' && room.gmId !== conn.id) {
            vttSendError(conn, `Op "${op}" réservée au GM.`, 'forbidden');
            return;
        }
        room.lastActive = Date.now();
        let payload = (msg.payload !== undefined ? msg.payload : {});
        // Garde-fou taille par op (anti-spam / anti-crash) — voice/vidéo gérés
        // au niveau frame WS (512 Ko), les autres ops ont leur quota dédié.
        if (vttPolicy && payload !== undefined) {
            try {
                const size = Buffer.byteLength(JSON.stringify(payload), 'utf8');
                if (size > vttPolicy.opPayloadLimit(op)) {
                    vttSendError(conn, `Payload "${op}" trop volumineux (${size} o).`, 'payload_too_large');
                    return;
                }
            } catch (e) { vttSendError(conn, 'Payload illisible.'); return; }
        }
        // Chat : texte assaini + tronqué (500 car.), jamais vide diffusé.
        if (op === 'chat' && vttPolicy) {
            const clean = vttPolicy.sanitizeChat(payload && payload.text);
            if (!clean.text) { vttSendError(conn, 'Message vide.'); return; }
            payload = clean;
        }
        // Dés : jet validé + clampé sur la formule (anti-triche douce).
        if (op === 'dice' && vttPolicy) {
            const sane = vttPolicy.validateDice(payload);
            if (!sane) { vttSendError(conn, 'Jet de dés invalide (roll entier requis).'); return; }
            payload = sane;
        }
        // Sync tour/round : le hub mémorise round + unité active (annuaire).
        if (op === 'turn_control' && vttPolicy) {
            try { vttPolicy.trackRound(room, op, payload); } catch (e) { /* ignore */ }
        }
        // Anti-spam : 30 ops / 5 s max par client (voice/vidéo exemptés :
        // flux temps réel légitime à haute fréquence).
        if (op !== 'voice' && op !== 'video') {
            const nowMs = Date.now();
            if (!Array.isArray(conn.opTimes)) conn.opTimes = [];
            conn.opTimes = conn.opTimes.filter((t) => nowMs - t < 5000);
            if (conn.opTimes.length >= 30) {
                vttSendError(conn, 'Trop de requêtes, ralentissez.', 'rate_limited');
                return;
            }
            conn.opTimes.push(nowMs);
        }

        // Revendication d'avatars : memorisee, jamais diffusee (hub seul).
        if (op === 'unit_claim') {
            const ids = payload && Array.isArray(payload.unitIds) ? payload.unitIds : [];
            vttLearnClaims(room, conn.id, ids);
            vttSendJson(conn, { type: 'op', from: 'hub', fromName: 'hub', fromRole: 'gm', op: 'unit_claim', payload: { ok: true }, at: Date.now() });
            return;
        }

        // Apprentissage passif de possession : un joueur declare son acteur.
        // unit_move auto-revendique aussi son acteur (cohérence du brouillard :
        // le hub connaît au moins un observateur pour l'émetteur).
        if (op === 'action_request' && payload && typeof payload.actorId === 'string' && payload.actorId) {
            vttLearnClaims(room, conn.id, [payload.actorId]);
        }
        if (op === 'unit_move' && payload && typeof payload === 'object') {
            const claimId = String(payload.actorId || payload.unitId || '').slice(0, 64);
            if (claimId) vttLearnClaims(room, conn.id, [claimId]);
        }

        // Snapshot initial : enregistre (murs + positions), transmis complet
        // (pose publique de table ; le filtrage porte sur les mouvements suivants).
        if (op === 'map_load' && payload && typeof payload === 'object') {
            try { vttLearnMap(room, payload.map); } catch (e) { /* ignore */ }
            vttBroadcast(room, {
                type: 'op',
                from: conn.id,
                fromName: conn.username,
                fromRole: conn.role,
                op,
                payload,
                at: Date.now()
            });
            return;
        }

        // Positions pures : filtrage strict par destinataire.
        if (op === 'unit_move') {
            try {
                const p = payload || {};
                const actorId = String(p.actorId || p.unitId || '');
                let q = Number.isFinite(p.destQ) ? p.destQ : p.q;
                let r = Number.isFinite(p.destR) ? p.destR : p.r;
                if (Array.isArray(p.path) && p.path.length > 0) {
                    const last = p.path[p.path.length - 1];
                    if (last && Number.isFinite(last.q)) { q = last.q; r = last.r; }
                }
                if (actorId && Number.isFinite(q) && Number.isFinite(r)) {
                    vttTrackUnit(room, actorId, {
                        q, r,
                        vision: p.vision, ouie: p.ouie,
                        yaw: (p.facingYaw !== undefined ? p.facingYaw : p.rotationY),
                        pano: p.pano, thermal: p.thermal,
                    });
                }
                vttSendFogFiltered(room, conn, op, payload, (viewerId) => {
                    if (viewerId === conn.id) return payload; // l'emetteur revoit son echo.
                    if (!Number.isFinite(q) || !Number.isFinite(r)) return payload;
                    return vttCanViewerSee(room, viewerId, q, r) ? payload : null; // aveugle : rien (fantome local).
                });
            } catch (e) {
                vttBroadcast(room, {
                    type: 'op', from: conn.id, fromName: conn.username, fromRole: conn.role,
                    op, payload, at: Date.now()
                });
            }
            return;
        }

        // Actions de combat : effets + logs publics, positions expurgees aux aveugles.
        if (op === 'combat_action') {
            try {
                const p = payload || {};
                if (p.action === 'move') {
                    const actorId = String(p.actorId || '');
                    const q = Number.isFinite(p.destQ) ? p.destQ : undefined;
                    const r = Number.isFinite(p.destR) ? p.destR : undefined;
                    if (actorId && Number.isFinite(q) && Number.isFinite(r)) {
                        vttTrackUnit(room, actorId, {
                            q, r, vision: p.vision, yaw: p.facingYaw, pano: p.pano, thermal: p.thermal,
                        });
                    }
                    vttSendFogFiltered(room, conn, op, payload, (viewerId) => {
                        if (viewerId === conn.id) return payload;
                        if (!Number.isFinite(q) || !Number.isFinite(r)) return payload;
                        return vttCanViewerSee(room, viewerId, q, r) ? payload : null;
                    });
                    return;
                }
                // Attaque / grenade / sort / souffle : on piste les repositionnements.
                if ((p.action === 'attack') && p.targetId && Number.isFinite(p.defenderNewQ) && Number.isFinite(p.defenderNewR)
                    && (p.defenderNewQ !== 0 || p.defenderNewR !== 0)) {
                    vttTrackUnit(room, String(p.targetId), { q: p.defenderNewQ, r: p.defenderNewR });
                }
                vttSendFogFiltered(room, conn, op, payload, (viewerId) => {
                    if (viewerId === conn.id) return payload;
                    if (vttIsGMViewer(room, viewerId)) return payload;
                    const actorPos = p.actorId ? vttFogUnits(room).get(String(p.actorId)) : null;
                    const tgtPos = p.targetId ? vttFogUnits(room).get(String(p.targetId)) : null;
                    const bangQ = (actorPos && Number.isFinite(actorPos.q)) ? actorPos.q : p.destQ;
                    const bangR = (actorPos && Number.isFinite(actorPos.r)) ? actorPos.r : p.destR;
                    let sees = false;
                    if (Number.isFinite(bangQ) && Number.isFinite(bangR)) {
                        sees = vttCanViewerSee(room, viewerId, bangQ, bangR)
                            || vttIsAudibleTo(room, viewerId, bangQ, bangR);
                    }
                    if (!sees && tgtPos && Number.isFinite(tgtPos.q)) {
                        sees = vttCanViewerSee(room, viewerId, tgtPos.q, tgtPos.r);
                    }
                    if (sees) return payload;
                    // Expurge : degats + logs conserves, positions a zero (le client
                    // ignore les Q/R nuls : pas de teleportation fantome).
                    return { ...p, destQ: 0, destR: 0, path: null, defenderNewQ: 0, defenderNewR: 0, actorRotationY: 0, defenderRotationY: 0 };
                });
            } catch (e) {
                vttBroadcast(room, {
                    type: 'op', from: conn.id, fromName: conn.username, fromRole: conn.role,
                    op, payload, at: Date.now()
                });
            }
            return;
        }

        // Snapshots d'etat : entrees ennemies invisibles retirees par destinataire.
        // La portee d'ambiance du MJ (sightRange) fait foi pour toute la room.
        if (op === 'turn_control' && payload && payload.action === 'state_sync' && Array.isArray(payload.unitStates)) {
            try {
                const sr = Number(payload.sightRange);
                if (Number.isFinite(sr)) room.sightRange = Math.max(1, Math.min(32, sr));
                for (const s of payload.unitStates) {
                    if (!s || !s.unitId) continue;
                    vttTrackUnit(room, String(s.unitId), {
                        q: s.q, r: s.r, vision: s.vision, ouie: s.ouie,
                        yaw: (s.facingYaw !== undefined ? s.facingYaw : s.rotationY),
                        pano: s.pano, thermal: s.thermal,
                        isPlayer: (s.isPlayer === 1 || s.isPlayer === true) ? true : (s.isPlayer === 0 || s.isPlayer === false ? false : undefined),
                    });
                }
                vttSendFogFiltered(room, conn, op, payload, (viewerId) => {
                    if (viewerId === conn.id) return payload;
                    if (vttIsGMViewer(room, viewerId)) return payload;
                    const kept = payload.unitStates.filter((s) => {
                        if (!s || !s.unitId) return true;
                        // Les revendiques, les hors de combat et les visibles restent.
                        const claims = vttFogClaims(room).get(viewerId);
                        if (claims && claims.has(String(s.unitId))) return true;
                        if (s.isAlive === false || s.isDead === true) return true;
                        if (!Number.isFinite(s.q) || !Number.isFinite(s.r)) return true;
                        return vttCanViewerSee(room, viewerId, s.q, s.r);
                    });
                    return { ...payload, unitStates: kept };
                });
            } catch (e) {
                vttBroadcast(room, {
                    type: 'op', from: conn.id, fromName: conn.username, fromRole: conn.role,
                    op, payload, at: Date.now()
                });
            }
            return;
        }
        // Pour les paquets voix, on évite d'émettre l'écho à l'expéditeur afin d'économiser sa bande passante.
        // RD-089 : drop_weapon / pickup_weapon / kick_weapon transitent ici en
        // relais brut (info publique de table : objets visibles, pas de filtre
        // brouillard ; les zones persistantes voyagent dans state_sync).
        const exceptSender = (op === 'voice') ? conn.id : null;
        vttBroadcast(room, {
            type: 'op',
            from: conn.id,
            fromName: conn.username,
            fromRole: conn.role,
            op,
            payload,
            at: Date.now()
        }, exceptSender);
        return;
    }

    vttSendError(conn, `Type de message inconnu : ${t}`, 'unknown_type');
}

function vttParseFrames(conn) {
    // Boucle de parsing non-bloquante sur le buffer accumulé de la socket.
    let buf = conn.buffer;
    while (buf.length >= 2) {
        const b0 = buf[0];
        const b1 = buf[1];
        const fin = (b0 >> 7) & 1;
        const opcode = b0 & 0x0f;
        const masked = (b1 >> 7) & 1;
        let payloadLen = b1 & 0x7f;
        let offset = 2;

        if (payloadLen === 126) {
            if (buf.length < offset + 2) break;
            payloadLen = buf.readUInt16BE(offset);
            offset += 2;
        } else if (payloadLen === 127) {
            if (buf.length < offset + 8) break;
            const big = buf.readBigUInt64BE(offset);
            if (big > BigInt(VTT_MAX_PAYLOAD)) {
                vttSendFrame(conn.socket, Buffer.from([0x03, 0xe9]), 0x8);
                conn.socket.destroy();
                return;
            }
            payloadLen = Number(big);
            offset += 8;
        }

        if (payloadLen > VTT_MAX_PAYLOAD) {
            vttSendFrame(conn.socket, Buffer.from([0x03, 0xe9]), 0x8);
            conn.socket.destroy();
            return;
        }

        let maskKey = null;
        if (masked) {
            if (buf.length < offset + 4) break;
            maskKey = buf.slice(offset, offset + 4);
            offset += 4;
        }
        if (buf.length < offset + payloadLen) break; // frame incomplète : attendre

        let payload = buf.slice(offset, offset + payloadLen);
        if (masked && maskKey) {
            const unmasked = Buffer.allocUnsafe(payloadLen);
            for (let i = 0; i < payloadLen; i++) unmasked[i] = payload[i] ^ maskKey[i % 4];
            payload = unmasked;
        }
        conn.buffer = buf.slice(offset + payloadLen);
        buf = conn.buffer;

        if (opcode === 0x8) { // close
            try { vttSendFrame(conn.socket, payload.slice(0, 2), 0x8); } catch (e) {}
            conn.socket.destroy();
            return;
        } else if (opcode === 0x9) { // ping -> pong
            vttSendFrame(conn.socket, payload, 0xA);
        } else if (opcode === 0xA) { // pong : ignore
        } else if (opcode === 0x0 || opcode === 0x1 || opcode === 0x2) {
            const isText = (opcode !== 0x0) ? (opcode === 0x1) : conn.fragIsText;
            if (!fin) {
                // Fragmentation : on accumule (cas rare avec nos petits JSON).
                if (opcode !== 0x0) { conn.fragOpcode = opcode; conn.fragIsText = (opcode === 0x1); }
                conn.fragParts.push(payload);
                let total = 0;
                for (const p of conn.fragParts) total += p.length;
                if (total > VTT_MAX_PAYLOAD) { conn.socket.destroy(); return; }
            } else {
                let full;
                if (conn.fragParts.length > 0) {
                    conn.fragParts.push(payload);
                    full = Buffer.concat(conn.fragParts);
                    conn.fragParts = [];
                } else {
                    full = payload;
                }
                if (isText || opcode === 0x1) {
                    let text = '';
                    try { text = full.toString('utf8'); } catch (e) {}
                    if (text.length > 0) {
                        try {
                            const obj = JSON.parse(text);
                            vttHandleMessage(conn, obj);
                        } catch (e) {
                            vttSendError(conn, 'JSON invalide.');
                        }
                    }
                }
                // Les frames binaires complètes sont ignorées pour le MVP (tout est JSON texte).
            }
        } else {
            conn.socket.destroy();
            return;
        }
    }
}

function vttAttachConnection(socket, req) {
    const id = vttGenClientId();
    const conn = {
        id,
        socket,
        buffer: Buffer.alloc(0),
        fragParts: [],
        fragOpcode: 0,
        fragIsText: true,
        username: 'Invité-' + id.slice(-4),
        userId: null,
        buildVersion: 'unknown',
        role: null,
        roomCode: null
    };
    vttClients.set(id, conn);
    socket.on('data', (chunk) => {
        try {
            conn.buffer = Buffer.concat([conn.buffer, chunk]);
            if (conn.buffer.length > VTT_MAX_PAYLOAD + 16) { socket.destroy(); return; }
            vttParseFrames(conn);
        } catch (e) {
            try { socket.destroy(); } catch (_) {}
        }
    });
    const cleanup = () => {
        if (!vttClients.has(id)) return;
        vttClients.delete(id);
        try { vttLeaveRoom(conn, 'disconnect'); } catch (e) {}
        console.log(`[VTT] Client ${id} déconnecté (rooms actives: ${vttRooms.size}).`);
    };
    socket.on('close', cleanup);
    socket.on('error', () => {});
    console.log(`[VTT] Client ${id} connecté via WS (${vttClients.size} en ligne).`);
    // Le client doit envoyer `hello` ; on ne force pas de welcome immédiat
    // pour laisser Unity/WebGL s'identifier avec son username + token.
}

server.on('upgrade', (req, socket, head) => {
    try {
        const url = new URL(req.url || '/', 'http://localhost');
        if (url.pathname !== VTT_WS_PATH) {
            socket.write('HTTP/1.1 404 Not Found\r\nConnection: close\r\n\r\n');
            socket.destroy();
            return;
        }
        const key = req.headers['sec-websocket-key'];
        const version = req.headers['sec-websocket-version'];
        if (!key || version !== '13') {
            socket.write('HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n');
            socket.destroy();
            return;
        }
        const accept = crypto.createHash('sha1').update(String(key).trim() + VTT_WS_GUID).digest('base64');
        const protocol = req.headers['sec-websocket-protocol'];
        const headers = [
            'HTTP/1.1 101 Switching Protocols',
            'Upgrade: websocket',
            'Connection: Upgrade',
            `Sec-WebSocket-Accept: ${accept}`
        ];
        // On ne négocie aucun sous-protocole pour rester compatible
        // ClientWebSocket (standalone) comme navigateur (WebGL).
        headers.push('', '');
        socket.write(headers.join('\r\n'));
        if (head && head.length > 0) {
            // head contient les octets déjà lus après les headers (rare mais possible).
            const tmp = { buffer: head };
            socket.unshift ? socket.unshift(head) : null;
        }
        vttAttachConnection(socket, req);
    } catch (e) {
        try { socket.destroy(); } catch (_) {}
    }
});

// Heartbeat : ping WS toutes les 30 s pour libérer les tables fantômes.
setInterval(() => {
    for (const conn of vttClients.values()) {
        try {
            if (!conn.socket || conn.socket.destroyed) continue;
            vttSendFrame(conn.socket, Buffer.alloc(0), 0x9);
        } catch (e) {}
    }
}, 30000).unref();

server.listen(PORT, () => {
    console.log(`[Codex Auth Engine] Serveur actif sur http://localhost:${PORT}`);
});
adminServer.listen(ADMIN_PORT, ADMIN_BIND, () => {
    console.log(`[Codex Admin] Interface locale sur http://${ADMIN_BIND}:${ADMIN_PORT}/admin.html (non exposée)`);
});
