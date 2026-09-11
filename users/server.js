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

    if (req.method === 'GET' || req.method === 'HEAD') {
        return serveStatic(res, parsedUrl.pathname);
    }

    res.writeHead(405, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Méthode non autorisée.');
});

server.listen(PORT, () => {
    console.log(`[Codex Auth Engine] Serveur actif sur http://localhost:${PORT}`);
});