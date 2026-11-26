/* Tests d'intégration serveur HTTP + WebSocket /ws/vtt unifié (users/server.js) */
'use strict';

const { describe, it, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const crypto = require('node:crypto');
const fs = require('node:fs');
const net = require('node:net');
const os = require('node:os');
const path = require('node:path');

function getFreePort() {
  return new Promise((resolve, reject) => {
    const s = net.createServer();
    s.listen(0, '127.0.0.1', () => {
      const p = s.address().port;
      s.close(() => resolve(p));
    });
    s.on('error', reject);
  });
}

function connectWs(port, cookieHeader) {
  return new Promise((resolve, reject) => {
    const sock = net.createConnection({ host: '127.0.0.1', port }, () => {
      const key = crypto.randomBytes(16).toString('base64');
      const lines = [
        'GET /ws/vtt HTTP/1.1',
        `Host: 127.0.0.1:${port}`,
        'Upgrade: websocket',
        'Connection: Upgrade',
        `Sec-WebSocket-Key: ${key}`,
        'Sec-WebSocket-Version: 13',
      ];
      if (cookieHeader) lines.push(`Cookie: ${cookieHeader}`);
      lines.push('', '');
      sock.write(lines.join('\r\n'));
    });

    let handshakeDone = false;
    let buf = Buffer.alloc(0);
    const queue = [];
    const waiters = [];

    function dispatchMsg(obj) {
      if (waiters.length) {
        const w = waiters.shift();
        w.resolve(obj);
      } else {
        queue.push(obj);
      }
    }

    function parseFrames() {
      while (buf.length >= 2) {
        const op = buf[0] & 0x0f;
        let len = buf[1] & 0x7f;
        let off = 2;
        if (len === 126) {
          if (buf.length < off + 2) break;
          len = buf.readUInt16BE(off);
          off += 2;
        } else if (len === 127) {
          if (buf.length < off + 8) break;
          len = Number(buf.readBigUInt64BE(off));
          off += 8;
        }
        if (buf.length < off + len) break;
        const payload = buf.slice(off, off + len);
        buf = buf.slice(off + len);
        if (op === 0x1) {
          try { dispatchMsg(JSON.parse(payload.toString('utf8'))); } catch (e) {}
        }
      }
    }

    const client = {
      send(obj) {
        const data = Buffer.from(JSON.stringify(obj), 'utf8');
        const mask = crypto.randomBytes(4);
        let hdr;
        if (data.length < 126) {
          hdr = Buffer.alloc(2);
          hdr[0] = 0x81;
          hdr[1] = 0x80 | data.length;
        } else {
          hdr = Buffer.alloc(4);
          hdr[0] = 0x81;
          hdr[1] = 0x80 | 126;
          hdr.writeUInt16BE(data.length, 2);
        }
        const masked = Buffer.allocUnsafe(data.length);
        for (let i = 0; i < data.length; i++) masked[i] = data[i] ^ mask[i % 4];
        sock.write(Buffer.concat([hdr, mask, masked]));
      },
      nextMsg(predicate, timeoutMs = 3000) {
        return new Promise((res, rej) => {
          const checkQueue = () => {
            for (let i = 0; i < queue.length; i++) {
              if (!predicate || predicate(queue[i])) {
                return queue.splice(i, 1)[0];
              }
            }
            return null;
          };
          const found = checkQueue();
          if (found) return res(found);
          const timer = setTimeout(() => rej(new Error('WS timeout')), timeoutMs);
          const pump = () => {
            waiters.push({
              resolve(m) {
                if (!predicate || predicate(m)) {
                  clearTimeout(timer);
                  res(m);
                } else {
                  queue.push(m);
                  pump();
                }
              }
            });
          };
          pump();
        });
      },
      close() {
        try { sock.destroy(); } catch (e) {}
      }
    };

    sock.on('data', (chunk) => {
      buf = Buffer.concat([buf, chunk]);
      if (!handshakeDone) {
        const idx = buf.indexOf('\r\n\r\n');
        if (idx === -1) return;
        const head = buf.slice(0, idx).toString('utf8');
        buf = buf.slice(idx + 4);
        handshakeDone = true;
        if (!head.includes('101 Switching Protocols')) return reject(new Error('WS handshake failed: ' + head));
        resolve(client);
      }
      parseFrames();
    });
    sock.on('error', reject);
  });
}

describe('vtt_server_integration — Auth cookie WS, Table<->VTT, anti-maphack & Unity', () => {
  let tmpDir;
  let port;
  let proc;
  let baseUrl;
  let gmCookie;
  let p1Cookie;
  let outCookie;
  let tableId;

  before(async () => {
    tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vtt-srv-'));
    port = await getFreePort();
    baseUrl = `http://127.0.0.1:${port}`;
    const usersDb = path.join(tmpDir, 'users.json');
    const rolesDb = path.join(tmpDir, 'roles.json');
    const tablesDir = path.join(tmpDir, 'tables');
    fs.writeFileSync(usersDb, '[]');

    proc = spawn(process.execPath, [path.join(__dirname, '..', 'server.js')], {
      env: {
        ...process.env,
        PORT: String(port),
        DISABLE_ADMIN_SERVER: '1',
        USERS_DB_PATH: usersDb,
        ROLES_PATH: rolesDb,
        TABLES_DIR: tablesDir
      },
      stdio: ['ignore', 'pipe', 'pipe']
    });

    await new Promise((resolve, reject) => {
      const t = setTimeout(() => reject(new Error('Server start timeout')), 5000);
      proc.stdout.on('data', (d) => {
        if (String(d).includes('Serveur actif')) { clearTimeout(t); resolve(); }
      });
      proc.stderr.on('data', (d) => process.stderr.write(d));
    });

    const reg = async (username, email) => {
      const r = await fetch(`${baseUrl}/api/auth/register`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, email, password: 'Password123!' })
      });
      assert.equal(r.status, 200);
      const sc = r.headers.get('set-cookie');
      return sc.split(';')[0];
    };
    gmCookie = await reg('MaitreJeu', 'mj@test.local');
    p1Cookie = await reg('JoueurUn', 'j1@test.local');
    outCookie = await reg('Externe', 'out@test.local');

    // Créer une table et inviter JoueurUn
    const cr = await fetch(`${baseUrl}/api/tables`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Cookie: gmCookie },
      body: JSON.stringify({ name: 'Table Test VTT' })
    });
    assert.equal(cr.status, 201);
    tableId = (await cr.json()).table.id;

    const inv = await fetch(`${baseUrl}/api/tables/${tableId}/members`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Cookie: gmCookie },
      body: JSON.stringify({ user: 'JoueurUn' })
    });
    assert.equal(inv.status, 201);
  });

  after(() => {
    if (proc) proc.kill();
    if (tmpDir) fs.rmSync(tmpDir, { recursive: true, force: true });
  });

  it('unifie Table <-> WS via cookie srv_, bloque non-membre et garde gmOnly secret', async () => {
    const wsOut = await connectWs(port, outCookie);
    wsOut.send({ type: 'join_table', tableId });
    const errOut = await wsOut.nextMsg((m) => m.type === 'error');
    assert.equal(errOut.code, 'forbidden');
    wsOut.close();

    const wsGm = await connectWs(port, gmCookie);
    const wsP1 = await connectWs(port, p1Cookie);

    wsGm.send({ type: 'join_table', tableId });
    const joinedGm = await wsGm.nextMsg((m) => m.type === 'table_joined');
    assert.equal(joinedGm.role, 'gm');
    assert.equal(joinedGm.isGM, true);

    wsP1.send({ type: 'join_table', tableId });
    const joinedP1 = await wsP1.nextMsg((m) => m.type === 'table_joined');
    assert.equal(joinedP1.role, 'player');
    assert.equal(joinedP1.isGM, false);

    // GM ajoute un pion public et un pion secret gmOnly
    wsGm.send({ type: 'vtt_op', op: 'vtt_token', payload: { action: 'add', token: { id: 'tok_pj', name: 'Lucas', faction: 'pj', q: 1, r: 1 } } });
    await wsP1.nextMsg((m) => m.type === 'vtt_state' && m.state.scenes[0].tokens.some((t) => t.id === 'tok_pj'));

    wsGm.send({ type: 'vtt_op', op: 'vtt_token', payload: { action: 'add', token: { id: 'tok_trap', name: 'Piege', faction: 'enemy', q: 2, r: 2, gmOnly: true } } });
    const p1AfterTrap = await wsP1.nextMsg((m) => m.type === 'vtt_state' && m.op === 'vtt_token');
    assert.equal(p1AfterTrap.state.scenes[0].tokens.some((t) => t.id === 'tok_trap'), false);

    // Joueur tente op GM -> forbidden
    wsP1.send({ type: 'vtt_op', op: 'vtt_turn', payload: { action: 'next_turn' } });
    const p1Err = await wsP1.nextMsg((m) => m.type === 'error');
    assert.equal(p1Err.code, 'forbidden');

    // Joueur revendique tok_pj et le déplace -> persisté sur disque
    wsP1.send({ type: 'vtt_op', op: 'vtt_token', payload: { action: 'claim', tokenId: 'tok_pj' } });
    await wsGm.nextMsg((m) => m.type === 'vtt_state');
    wsP1.send({ type: 'vtt_op', op: 'vtt_token', payload: { action: 'move', tokenId: 'tok_pj', q: 4, r: 5 } });
    const gmSawMove = await wsGm.nextMsg((m) => m.type === 'vtt_state' && m.state.scenes[0].tokens.find((t) => t.id === 'tok_pj')?.q === 4);
    assert.equal(gmSawMove.state.scenes[0].tokens.find((t) => t.id === 'tok_pj').r, 5);

    // Vérification REST après mutation
    const vttRes = await fetch(`${baseUrl}/api/tables/${tableId}/vtt`, { headers: { Cookie: p1Cookie } });
    const vttJson = await vttRes.json();
    assert.equal(vttJson.state.scenes[0].tokens.some((t) => t.id === 'tok_trap'), false);
    assert.equal(vttJson.state.scenes[0].tokens.find((t) => t.id === 'tok_pj').q, 4);

    wsGm.close();
    wsP1.close();
  });

  it('protège les jets /secret MJ dans le chat de table', async () => {
    const r = await fetch(`${baseUrl}/api/tables/${tableId}/messages`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Cookie: gmCookie },
      body: JSON.stringify({
        text: '🔒 Jet secret',
        meta: { kind: 'dice', secret: true, formula: '1d8', total: 7, roll: 7, isCritical: false }
      })
    });
    assert.equal(r.status, 201);

    const gmChat = await (await fetch(`${baseUrl}/api/tables/${tableId}`, { headers: { Cookie: gmCookie } })).json();
    const p1Chat = await (await fetch(`${baseUrl}/api/tables/${tableId}`, { headers: { Cookie: p1Cookie } })).json();
    assert.equal(gmChat.table.messages.some((m) => m.meta && m.meta.secret), true);
    assert.equal(p1Chat.table.messages.some((m) => m.meta && m.meta.secret), false);
  });

  it('conserve la compatibilité Unity create_room + /api/vtt/rooms', async () => {
    const wsUnity = await connectWs(port);
    wsUnity.send({ type: 'create_room', username: 'UnityGM', tableName: 'Salle Unity' });
    const created = await wsUnity.nextMsg((m) => m.type === 'room_created');
    assert.ok(created.code);

    const roomsRes = await (await fetch(`${baseUrl}/api/vtt/rooms`)).json();
    assert.equal(roomsRes.rooms.some((r) => r.code === created.code), true);
    wsUnity.close();
  });

  it('gère duel masqué WS, porte bloquante + handout ciblé', async () => {
    const wsGm = await connectWs(port, gmCookie);
    const wsP1 = await connectWs(port, p1Cookie);

    wsGm.send({ type: 'join_table', tableId });
    await wsGm.nextMsg((m) => m.type === 'table_joined');
    wsP1.send({ type: 'join_table', tableId });
    await wsP1.nextMsg((m) => m.type === 'table_joined');

    // Activer brouillard + porte fermée entre tok_pj (4,5) et ennemi (8,5)
    wsGm.send({ type: 'vtt_op', op: 'vtt_scene', payload: { action: 'update', fogEnabled: true, sightRange: 12 } });
    await wsP1.nextMsg((m) => m.type === 'vtt_state');
    wsGm.send({ type: 'vtt_op', op: 'vtt_token', payload: { action: 'add', token: { id: 'tok_en', name: 'Sniper', faction: 'enemy', q: 8, r: 5 } } });
    await wsP1.nextMsg((m) => m.type === 'vtt_state');
    wsGm.send({ type: 'vtt_op', op: 'vtt_wall', payload: { action: 'add', id: 'd_ws', kind: 'door', q: 6, r: 5, q2: 6, r2: 6, open: false } });
    const p1Blocked = await wsP1.nextMsg((m) => m.type === 'vtt_state' && m.op === 'vtt_wall');
    assert.equal(p1Blocked.state.scenes[0].tokens.some((t) => t.id === 'tok_en'), false);

    // Ouvrir la porte -> tok_en visible
    wsGm.send({ type: 'vtt_op', op: 'vtt_wall', payload: { action: 'toggle_door', wallId: 'd_ws' } });
    const p1Open = await wsP1.nextMsg((m) => m.type === 'vtt_state' && m.op === 'vtt_wall');
    assert.equal(p1Open.state.scenes[0].tokens.some((t) => t.id === 'tok_en'), true);

    // Handout créé et projeté
    wsGm.send({ type: 'vtt_op', op: 'vtt_handout', payload: { action: 'create', id: 'ho_1', title: 'Plan Secret', text: 'Code 42' } });
    await wsP1.nextMsg((m) => m.type === 'vtt_state' && m.op === 'vtt_handout');
    wsGm.send({ type: 'vtt_op', op: 'vtt_handout', payload: { action: 'show', handoutId: 'ho_1' } });
    const ev = await wsP1.nextMsg((m) => m.type === 'vtt_event' && m.op === 'vtt_handout');
    assert.equal(ev.payload.handout.title, 'Plan Secret');

    // Défi, Horloge & SFX temps réel via WS
    wsGm.send({ type: 'vtt_op', op: 'vtt_challenge', payload: { action: 'create', id: 'chg_ws', title: 'SAS' } });
    const stChg = await wsP1.nextMsg((m) => m.type === 'vtt_state' && m.op === 'vtt_challenge');
    assert.equal(stChg.state.challenges.length, 1);

    wsGm.send({ type: 'vtt_op', op: 'vtt_audio', payload: { action: 'sfx', sfx: 'explosion' } });
    const evSfx = await wsP1.nextMsg((m) => m.type === 'vtt_event' && m.op === 'vtt_audio');
    assert.equal(evSfx.payload.sfx, 'explosion');

    wsGm.close();
    wsP1.close();
  });
});
