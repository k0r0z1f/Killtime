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
    res.setHeader('Access-Control-Allow-Headers', 'Origin, X-Requested-With, Content-Type, Accept, Authorization');
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

function serveStatic(res, pathname) {
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
                res.writeHead(200, { 'Content-Type': contentType });
                fs.createReadStream(filePath).pipe(res);
            });
        }
        if (err || !stats.isFile()) {
            res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
            return res.end('Fichier introuvable.');
        }

        const ext = path.extname(filePath).toLowerCase();
        const contentType = MIME_TYPES[ext] || 'application/octet-stream';
        res.writeHead(200, { 'Content-Type': contentType });
        fs.createReadStream(filePath).pipe(res);
    });
}

const server = http.createServer(async (req, res) => {
    setCors(req, res);

    if (req.method === 'OPTIONS') {
        res.writeHead(200);
        return res.end();
    }

    const parsedUrl = new URL(req.url, `http://${req.headers.host || 'localhost'}`);
    const cleanPath = parsedUrl.pathname.replace(/\/+$/, '').toLowerCase();
    const isRoute = (endpoint) => cleanPath === endpoint || cleanPath.endsWith(endpoint);

    console.log(`[HTTP ${req.method}] ${cleanPath}`);

    if (isRoute('/api/auth/register')) {
        if (req.method === 'GET') {
            return sendJson(res, 200, { status: 'online', endpoint: '/api/auth/register', method: 'POST' });
        }
        if (req.method === 'POST') {
            try {
                const body = await parseBody(req);
                const { username, email, password, role } = body;

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
                    role: role || 'Joueur',
                    provider: 'local',
                    createdAt: new Date().toISOString(),
                    characters: []
                };

                users.push(newUser);
                saveUsers(users);

                const token = 'srv_' + Buffer.from(`${newUser.id}:${Date.now()}`).toString('base64');
                const cookie = `codex_auth_token=${token}; Path=/; Max-Age=604800; SameSite=Lax`;

                const { saltHex, passwordHashHex, ...safeUser } = newUser;
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

                const { saltHex, passwordHashHex, ...safeUser } = user;
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

                const { saltHex, passwordHashHex, ...safeUser } = user;
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
                const { saltHex, passwordHashHex, ...safeUser } = user;
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
                const { saltHex, passwordHashHex, ...safeUser } = user;
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
                const { saltHex, passwordHashHex, ...safeUser } = user;
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

    if (req.method === 'GET' || req.method === 'HEAD') {
        return serveStatic(res, parsedUrl.pathname);
    }

    res.writeHead(405, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Méthode non autorisée.');
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
const vttRooms = new Map();   // CODE -> { code, createdAt, gmId, members: Map(clientId -> {username, role, userId}) }
let vttClientSeq = 0;

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
            members: new Map()
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
        // Garde-fou GM minimal : seul le GM peut émettre les ops de contrôle de table.
        const gmOnlyOps = new Set(['turn_control', 'scene_control', 'room_settings', 'map_load']);
        if (gmOnlyOps.has(op) && conn.role !== 'gm' && room.gmId !== conn.id) {
            vttSendError(conn, `Op "${op}" réservée au GM.`, 'forbidden');
            return;
        }
        room.lastActive = Date.now();
        // Pour les paquets voix, on évite d'émettre l'écho à l'expéditeur afin d'économiser sa bande passante.
        const exceptSender = (op === 'voice') ? conn.id : null;
        vttBroadcast(room, {
            type: 'op',
            from: conn.id,
            fromName: conn.username,
            fromRole: conn.role,
            op,
            payload: (msg.payload !== undefined ? msg.payload : {}),
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
