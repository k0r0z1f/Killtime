/* ==========================================================================
   CODEX UNIVERSEL — AUTHENTICATION & STATIC HTTP ENGINE (ZERO DEPENDENCY)
   users/server.js
   Exécution : node users/server.js
   ========================================================================== */

const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

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

function serveStatic(res, pathname) {
    let safePath = path.normalize(decodeURI(pathname)).replace(/^(\.\.[\/\\])+/, '');
    if (safePath === '/' || safePath === '\\' || safePath === '') {
        safePath = '/index.html';
    }

    const filePath = path.join(PUBLIC_DIR, safePath);

    if (!filePath.startsWith(PUBLIC_DIR)) {
        res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
        return res.end('Accès interdit.');
    }

    fs.stat(filePath, (err, stats) => {
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

    if (req.method === 'GET' || req.method === 'HEAD') {
        return serveStatic(res, parsedUrl.pathname);
    }

    res.writeHead(405, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('Méthode non autorisée.');
});

server.listen(PORT, () => {
    console.log(`[Codex Auth Engine] Serveur actif sur http://localhost:${PORT}`);
});