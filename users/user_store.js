class UserStore {
    constructor() {
        this.storageKey = 'codex_user_database';
        this.users = [];
        this.init();
    }

    init() {
        const stored = localStorage.getItem(this.storageKey);
        if (stored) {
            try {
                this.users = JSON.parse(stored);
            } catch (e) {
                console.error('[UserStore] Erreur de parsing du stockage local:', e);
                this.seedDefaultUsers();
            }
        } else {
            this.seedDefaultUsers();
        }
    }

    seedDefaultUsers() {
        this.users = [
            {
                id: 'usr_architect_00',
                username: 'AlexisLacasse',
                email: 'architecte@killtime.universe',
                saltHex: '4f92d8a6b105c317e84a29df58b19a33',
                passwordHashHex: '6a43960350d032df4c4d293d69bf773c2419f9ef784d1bc9b27a3c3065b93d3a',
                role: 'Architecte',
                provider: 'local',
                createdAt: '2026-09-01T00:00:00.000Z',
                characters: ['char_01']
            },
            {
                id: 'usr_roger_01',
                username: 'RogerVardis',
                email: 'roger@vardis.net',
                saltHex: 'c8194b301f82d54e8031ab9823ef0912',
                passwordHashHex: 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
                role: 'Joueur',
                provider: 'local',
                createdAt: '2026-09-02T12:00:00.000Z',
                characters: []
            }
        ];
        this.persist();
    }

    persist() {
        localStorage.setItem(this.storageKey, JSON.stringify(this.users));
    }

    // Moteur SHA-256 standard autonome (FIPS 180-4)
    sha256Bytes(bytes) {
        const K = [
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
        ];
        const H = [
            0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
            0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19
        ];

        const bitLen = bytes.length * 8;
        const newLen = (((bytes.length + 8) >> 6) + 1) << 6;
        const padded = new Uint8Array(newLen);
        padded.set(bytes);
        padded[bytes.length] = 0x80;

        const view = new DataView(padded.buffer);
        view.setUint32(newLen - 4, bitLen, false);

        const W = new Uint32Array(64);

        for (let i = 0; i < newLen; i += 64) {
            for (let t = 0; t < 16; t++) {
                W[t] = view.getUint32(i + (t * 4), false);
            }
            for (let t = 16; t < 64; t++) {
                const s0 = ((W[t - 15] >>> 7) | (W[t - 15] << 25)) ^
                    ((W[t - 15] >>> 18) | (W[t - 15] << 14)) ^
                    (W[t - 15] >>> 3);
                const s1 = ((W[t - 2] >>> 17) | (W[t - 2] << 15)) ^
                    ((W[t - 2] >>> 19) | (W[t - 2] << 13)) ^
                    (W[t - 2] >>> 10);
                W[t] = (W[t - 16] + s0 + W[t - 7] + s1) >>> 0;
            }

            let a = H[0], b = H[1], c = H[2], d = H[3],
                e = H[4], f = H[5], g = H[6], h = H[7];

            for (let t = 0; t < 64; t++) {
                const S1 = ((e >>> 6) | (e << 26)) ^
                    ((e >>> 11) | (e << 21)) ^
                    ((e >>> 25) | (e << 7));
                const ch = (e & f) ^ ((~e) & g);
                const temp1 = (h + S1 + ch + K[t] + W[t]) >>> 0;
                const S0 = ((a >>> 2) | (a << 30)) ^
                    ((a >>> 13) | (a << 19)) ^
                    ((a >>> 22) | (a << 10));
                const maj = (a & b) ^ (a & c) ^ (b & c);
                const temp2 = (S0 + maj) >>> 0;

                h = g;
                g = f;
                f = e;
                e = (d + temp1) >>> 0;
                d = c;
                c = b;
                b = a;
                a = (temp1 + temp2) >>> 0;
            }

            H[0] = (H[0] + a) >>> 0;
            H[1] = (H[1] + b) >>> 0;
            H[2] = (H[2] + c) >>> 0;
            H[3] = (H[3] + d) >>> 0;
            H[4] = (H[4] + e) >>> 0;
            H[5] = (H[5] + f) >>> 0;
            H[6] = (H[6] + g) >>> 0;
            H[7] = (H[7] + h) >>> 0;
        }

        return H.map(x => x.toString(16).padStart(8, '0')).join('');
    }

    fallbackPbkdf2(password, saltUint8Array, iterations = 2000) {
        const enc = (typeof TextEncoder !== 'undefined') ? new TextEncoder() : {
            encode: (str) => {
                const utf8 = unescape(encodeURIComponent(str));
                const arr = new Uint8Array(utf8.length);
                for (let i = 0; i < utf8.length; i++) arr[i] = utf8.charCodeAt(i);
                return arr;
            }
        };

        const passBytes = enc.encode(password);
        const combined = new Uint8Array(passBytes.length + saltUint8Array.length);
        combined.set(passBytes);
        combined.set(saltUint8Array, passBytes.length);

        let hashHex = this.sha256Bytes(combined);
        for (let i = 1; i < iterations; i++) {
            const nextBytes = enc.encode(hashHex + password);
            hashHex = this.sha256Bytes(nextBytes);
        }
        return hashHex;
    }

    async hashPassword(password, saltUint8Array) {
        const isSecureCryptoAvailable = typeof window !== 'undefined' &&
            window.crypto &&
            window.crypto.subtle &&
            typeof window.crypto.subtle.importKey === 'function';

        if (isSecureCryptoAvailable) {
            try {
                const enc = new TextEncoder();
                const keyMaterial = await window.crypto.subtle.importKey(
                    'raw',
                    enc.encode(password),
                    { name: 'PBKDF2' },
                    false,
                    ['deriveBits']
                );

                const derivedBits = await window.crypto.subtle.deriveBits(
                    {
                        name: 'PBKDF2',
                        salt: saltUint8Array,
                        iterations: 100000,
                        hash: 'SHA-256'
                    },
                    keyMaterial,
                    256
                );

                return Array.from(new Uint8Array(derivedBits))
                    .map(b => b.toString(16).padStart(2, '0'))
                    .join('');
            } catch (e) {
                console.warn('[UserStore] Bascule sur le moteur de hachage autonome.');
            }
        }

        return this.fallbackPbkdf2(password, saltUint8Array);
    }

    generateSalt() {
        const salt = new Uint8Array(16);
        if (typeof window !== 'undefined' && window.crypto && typeof window.crypto.getRandomValues === 'function') {
            window.crypto.getRandomValues(salt);
        } else {
            for (let i = 0; i < 16; i++) {
                salt[i] = Math.floor(Math.random() * 256);
            }
        }
        return salt;
    }

    saltToHex(salt) {
        return Array.from(salt).map(b => b.toString(16).padStart(2, '0')).join('');
    }

    hexToSalt(hexString) {
        const bytes = new Uint8Array(hexString.length / 2);
        for (let i = 0; i < hexString.length; i += 2) {
            bytes[i / 2] = parseInt(hexString.substr(i, 2), 16);
        }
        return bytes;
    }

    findByUsernameOrEmail(identifier) {
        const term = identifier.toLowerCase().trim();
        return this.users.find(u => u.username.toLowerCase() === term || u.email.toLowerCase() === term);
    }

    findById(id) {
        return this.users.find(u => u.id === id);
    }

    async registerLocalUser({ username, email, password, role = 'Joueur' }) {
        if (!username || username.trim().length < 3) {
            throw new Error("L'identifiant doit contenir au moins 3 caractères.");
        }
        if (!email || !email.includes('@')) {
            throw new Error("Format d'adresse e-mail invalide.");
        }
        if (!password || password.length < 6) {
            throw new Error("Le mot de passe doit comporter au moins 6 caractères.");
        }

        const existing = this.findByUsernameOrEmail(username) || this.findByUsernameOrEmail(email);
        if (existing) {
            throw new Error("Cet identifiant ou cette adresse e-mail est déjà enregistré.");
        }

        const salt = this.generateSalt();
        const saltHex = this.saltToHex(salt);
        const passwordHashHex = await this.hashPassword(password, salt);

        const newUser = {
            id: 'usr_' + Date.now().toString(36) + Math.random().toString(36).substr(2, 5),
            username: username.trim(),
            email: email.trim().toLowerCase(),
            saltHex: saltHex,
            passwordHashHex: passwordHashHex,
            role: role,
            provider: 'local',
            createdAt: new Date().toISOString(),
            characters: []
        };

        this.users.push(newUser);
        this.persist();
        return this.sanitizeUser(newUser);
    }

    async authenticateLocalUser(identifier, password) {
        const user = this.findByUsernameOrEmail(identifier);
        if (!user) {
            throw new Error("Identifiant ou mot de passe incorrect.");
        }

        if (user.provider !== 'local') {
            throw new Error(`Ce compte est lié au fournisseur ${user.provider}.`);
        }

        const salt = this.hexToSalt(user.saltHex);
        const computedHash = await this.hashPassword(password, salt);

        if (computedHash !== user.passwordHashHex) {
            throw new Error("Identifiant ou mot de passe incorrect.");
        }

        return this.sanitizeUser(user);
    }

    registerOrLoginSocialUser({ provider, providerId, email, name, avatar }) {
        let user = this.users.find(u => u.provider === provider && u.providerId === providerId);

        if (!user && email) {
            user = this.users.find(u => u.email.toLowerCase() === email.toLowerCase());
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
                provider: provider,
                providerId: providerId,
                avatar: avatar || null,
                role: 'Joueur',
                createdAt: new Date().toISOString(),
                characters: []
            };
            this.users.push(user);
        }

        this.persist();
        return this.sanitizeUser(user);
    }

    sanitizeUser(user) {
        const { passwordHashHex, saltHex, ...safeUser } = user;
        return safeUser;
    }

    exportDatabaseJson() {
        return JSON.stringify(this.users, null, 2);
    }

    importDatabaseJson(jsonString) {
        const parsed = JSON.parse(jsonString);
        if (Array.isArray(parsed)) {
            this.users = parsed;
            this.persist();
            return true;
        }
        return false;
    }
}

window.CodexUserStore = new UserStore();