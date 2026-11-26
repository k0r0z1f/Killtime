/* Tests SSR & SEO : pré-rendu serveur des Livres/Appendices + robots.txt + Killtime canonical */
'use strict';

const { describe, it, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const http = require('node:http');
const net = require('node:net');
const os = require('node:os');
const path = require('node:path');
const ssrCodex = require('../ssr_codex');

const PUBLIC_DIR = path.resolve(__dirname, '..', '..');

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

describe('ssr_codex — Rendu unitaire & intégration SEO', () => {
  let tmpDir;
  let port;
  let proc;

  before(async () => {
    tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ssr-srv-'));
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

  it('pré-rend les 11 Livres JSON sans bloc inconnu ni erreur', () => {
    for (const n of [1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12]) {
      const res = ssrCodex.renderSsrHtml(PUBLIC_DIR, `/livre_${n}.html`);
      assert.ok(res && res.buffer.length > 0, `livre_${n}.html non rendu`);
      const html = res.buffer.toString('utf8');
      assert.ok(html.includes('<h2 id="chap-'), `livre_${n}.html sans chapitre SSR`);
      assert.equal(html.includes('Bloc inconnu'), false, `livre_${n}.html contient Bloc inconnu`);
    }
    assert.equal(ssrCodex.renderSsrHtml(PUBLIC_DIR, '/livre_9.html'), null);
  });

  it('pré-rend les 3 Appendices avec leurs entrées sémantiques', () => {
    const act = ssrCodex.renderSsrHtml(PUBLIC_DIR, '/appendice_actions.html').buffer.toString('utf8');
    assert.ok(act.includes('class="ssr-seo-list"'));
    assert.ok(act.includes('Frapper au Corps-à-Corps'));

    const glo = ssrCodex.renderSsrHtml(PUBLIC_DIR, '/appendice_glossaire.html').buffer.toString('utf8');
    assert.ok(glo.includes('class="ssr-seo-list"'));
    assert.ok(glo.includes('Tour de 10 Secondes'));

    const obj = ssrCodex.renderSsrHtml(PUBLIC_DIR, '/appendice_objets.html').buffer.toString('utf8');
    assert.ok(obj.includes('class="ssr-seo-list"'));
    assert.ok(obj.includes('Épée Métal Courte'));
  });

  it('sert /appendice_objets.html, /livre_1.html et /livre_5.html en SSR HTTP 200 et gère 304', async () => {
    for (const [p, marker] of [
      ['/appendice_objets.html', 'Épée Métal Courte'],
      ['/livre_1.html', '<h2 id="chap-1"'],
      ['/livre_5.html', '<h2 id="chap-'],
    ]) {
      const r = await httpGet(port, p);
      assert.equal(r.status, 200, p);
      assert.ok(r.body.includes(`<link rel="canonical" href="https://killtime.studio${p}">`), p);
      assert.ok(r.body.includes(marker), `${p} sans contenu SSR`);
      assert.ok(r.headers['last-modified'], `${p} sans Last-Modified`);

      const r304 = await httpGet(port, p, { 'If-Modified-Since': r.headers['last-modified'] });
      assert.equal(r304.status, 304, `${p} attendu 304`);
    }
  });

  it('sert robots.txt resserré sans bloquer /data/ et Killtime/index.html avec canonical valide', async () => {
    const rb = await httpGet(port, '/robots.txt');
    assert.equal(rb.status, 200);
    for (const rule of [
      'Disallow: /doc_viewer.html',
      'Disallow: /*.docx$',
      'Disallow: /Killtime/Killtime/chatgpt/',
      'Disallow: /Killtime/Killtime/lore/',
      'Disallow: /Killtime/Killtime/manuscripts/',
    ]) {
      assert.ok(rb.body.includes(rule), `robots.txt manque ${rule}`);
    }
    assert.equal(rb.body.includes('Disallow: /data/'), false);

    const kt = await httpGet(port, '/Killtime/index.html');
    assert.equal(kt.status, 200);
    assert.ok(kt.body.includes('<link rel="canonical" href="https://killtime.studio/Killtime/index.html">'));
  });
});
