/* Tests de régression SEO : redirections canoniques 301 (/index.html -> /, www -> apex) */
'use strict';

const { describe, it, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const http = require('node:http');
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

function httpGet(port, reqPath, headers = {}) {
  return new Promise((resolve, reject) => {
    const req = http.request({
      hostname: '127.0.0.1',
      port,
      path: reqPath,
      method: 'GET',
      headers,
    }, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => {
        resolve({
          status: res.statusCode,
          headers: res.headers,
          body: Buffer.concat(chunks).toString('utf8'),
        });
      });
    });
    req.on('error', reject);
    req.end();
  });
}

describe('canonical_redirect — Redirections 301 SEO', () => {
  let tmpDir;
  let port;
  let proc;

  before(async () => {
    tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'seo-srv-'));
    port = await getFreePort();
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
        TABLES_DIR: tablesDir,
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    });

    await new Promise((resolve, reject) => {
      const t = setTimeout(() => reject(new Error('Server start timeout')), 5000);
      proc.stdout.on('data', (d) => {
        if (String(d).includes('Serveur actif')) { clearTimeout(t); resolve(); }
      });
      proc.stderr.on('data', (d) => process.stderr.write(d));
    });
  });

  after(() => {
    if (proc) proc.kill();
    if (tmpDir) fs.rmSync(tmpDir, { recursive: true, force: true });
  });

  it('redirige GET /index.html vers / en 301 en conservant la query string', async () => {
    const r1 = await httpGet(port, '/index.html');
    assert.equal(r1.status, 301);
    assert.equal(r1.headers.location, '/');

    const r2 = await httpGet(port, '/index.html?utm=1');
    assert.equal(r2.status, 301);
    assert.equal(r2.headers.location, '/?utm=1');
  });

  it('sert / en 200 avec la balise canonical vers https://killtime.studio/', async () => {
    const r = await httpGet(port, '/');
    assert.equal(r.status, 200);
    assert.ok(r.body.includes('<link rel="canonical" href="https://killtime.studio/">'));
  });

  it('ne redirige pas /Killtime/index.html', async () => {
    const r = await httpGet(port, '/Killtime/index.html');
    assert.equal(r.status, 200);
  });

  it('redirige www.killtime.studio en 301 vers https://killtime.studio', async () => {
    const r1 = await httpGet(port, '/livre_1.html', { Host: 'www.killtime.studio' });
    assert.equal(r1.status, 301);
    assert.equal(r1.headers.location, 'https://killtime.studio/livre_1.html');

    const r2 = await httpGet(port, '/index.html', { Host: 'www.killtime.studio' });
    assert.equal(r2.status, 301);
    assert.equal(r2.headers.location, 'https://killtime.studio/');
  });
});
