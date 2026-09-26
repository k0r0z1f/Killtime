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
            covers: new Map()
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
        const payload = (msg.payload !== undefined ? msg.payload : {});

        // Revendication d'avatars : memorisee, jamais diffusee (hub seul).
        if (op === 'unit_claim') {
            const ids = payload && Array.isArray(payload.unitIds) ? payload.unitIds : [];
            vttLearnClaims(room, conn.id, ids);
            vttSendJson(conn, { type: 'op', from: 'hub', fromName: 'hub', fromRole: 'gm', op: 'unit_claim', payload: { ok: true }, at: Date.now() });
            return;
        }

        // Apprentissage passif de possession : un joueur declare son acteur.
        if (op === 'action_request' && payload && typeof payload.actorId === 'string' && payload.actorId) {
            vttLearnClaims(room, conn.id, [payload.actorId]);
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
