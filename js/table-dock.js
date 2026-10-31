/* Table en cours — panneau latéral sur tout le site (sauf tables.html).
   Version allégée du chat : lecture + envoi texte public sur la table active
   (localStorage kt-tables-active). Images/MPs/gestion -> page Tables.
   Chargé par users/auth.js (ensureTableDock). Autonome, polling 5 s. */
(function () {
  'use strict';
  if (window.KT_DOCK_LOADED) return;
  window.KT_DOCK_LOADED = true;
  try {
    if (/tables\.html$/.test(window.location.pathname || '') || document.getElementById('tbApp')) return;
  } catch (e) { return; }

  const $ = (id) => document.getElementById(id);
  const apiBase = () => {
    try {
      if (typeof window.CODEX_API_BASE === 'string') return window.CODEX_API_BASE.replace(/\/+$/, '');
      if (window.location.protocol === 'file:') return 'http://localhost:3000';
      if (window.location.port === '8000') return window.location.protocol + '//' + window.location.hostname + ':3000';
      const h = window.location.hostname;
      if ((h === 'localhost' || h === '127.0.0.1') && window.location.port && window.location.port !== '3000') return 'http://localhost:3000';
    } catch (e) {}
    return '';
  };
  const api = (p, opt) => fetch(apiBase() + p, Object.assign({ credentials: 'include', headers: { 'ngrok-skip-browser-warning': 'true' } }, opt || {}));
  const esc = (s) => String(s || '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const tablesPrefix = () => {
    try {
      const path = window.location.pathname || '';
      if (path.includes('/manuscripts/') || path.includes('/lore/') || path.includes('/chatgpt/')) return '../../../';
      if (path.includes('/Killtime/')) return '../';
    } catch (e) {}
    return '';
  };

  let me = null;
  let tables = [];
  let activeId = null;
  let activeDoc = null;
  let lastTs = 0;
  let pollTimer = null;
  let fabTimer = null;

  /* ---------- filtre personnel (même format que js/tables.js) ---------- */
  function getHide() {
    try {
      const h = JSON.parse(localStorage.getItem('kt-tables-hide') || '{}') || {};
      Object.keys(h).forEach((t) => {
        if (Array.isArray(h[t])) { const o = {}; h[t].forEach((id) => { if (id) o[id] = 0; }); h[t] = o; }
        else if (h[t] && typeof h[t] !== 'object') delete h[t];
      });
      return h;
    } catch (e) { return {}; }
  }
  function msgVisible(m) {
    try {
      const t = getHide()[activeId];
      if (!t || t[m.fromId] === undefined) return true;
      return (m.createdAt || 0) <= t[m.fromId]; // historique conservé
    } catch (e) { return true; }
  }
  // Envoi restreint hérité : masqués perso + exclus serveur, limités aux membres.
  function effectiveExcept() {
    const out = new Set();
    try {
      Object.keys(getHide()[activeId] || {}).forEach((id) => out.add(id));
      ((activeDoc && activeDoc.myInherited) || []).forEach((id) => out.add(id));
    } catch (e) {}
    const members = {};
    try { (activeDoc.members || []).forEach((m) => { if (m && m.userId) members[m.userId] = true; }); } catch (e) {}
    return [...out].filter((id) => id && id !== (me && me.id) && members[id]).slice(0, 200);
  }

  function setStatus(msg, cls) {
    const e = $('kdStatus');
    if (!e) return;
    e.textContent = msg || '';
    e.className = 'kd-status' + (cls ? ' ' + cls : '');
  }

  /* ---------- pastille impayés du bouton latéral ---------- */
  function unreadCount() {
    try {
      const un = JSON.parse(localStorage.getItem('kt-tables-unread') || '{}') || {};
      let n = 0;
      Object.keys(un).forEach((k) => { n += Number(un[k]) || 0; });
      return n;
    } catch (e) { return 0; }
  }
  function updateFab() {
    const fab = $('ktDockFab');
    if (!fab) return;
    const n = unreadCount();
    fab.classList.toggle('has-unread', n > 0);
    const dot = fab.querySelector('.kd-dot');
    if (dot) dot.textContent = n > 9 ? '9+' : String(n);
  }

  function markRead() {
    if (!activeId) return;
    try {
      const lr = JSON.parse(localStorage.getItem('kt-tables-lastread') || '{}') || {};
      lr[activeId] = Math.max(lr[activeId] || 0, lastTs);
      localStorage.setItem('kt-tables-lastread', JSON.stringify(lr));
      const un = JSON.parse(localStorage.getItem('kt-tables-unread') || '{}') || {};
      if (un[activeId]) { delete un[activeId]; localStorage.setItem('kt-tables-unread', JSON.stringify(un)); }
      if (window.CodexAuth) window.CodexAuth.updateTablesBadge();
    } catch (e) {}
    updateFab();
  }

  /* ---------- rendu ---------- */
  function msgHtml(m) {
    const priv = m.private ? ' <span class="pb">privé</span>' : '';
    const dt = new Date(m.createdAt).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
    let body = m.text ? '<div>' + esc(m.text).replace(/\n/g, '<br>') + '</div>' : '';
    if (m.image && m.image.kind === 'url' && /^https?:\/\//.test(m.image.url || '')) {
      body += '<img src="' + esc(m.image.url) + '" loading="lazy" alt="image partagée">';
    } else if (m.image && m.image.kind === 'upload' && /^data:image\//.test(m.image.dataUrl || '')) {
      body += '<img src="' + m.image.dataUrl + '" loading="lazy" alt="image partagée">';
    } else if (m.image) {
      body += '<div class="kd-empty">🖼 image partagée (voir page Tables)</div>';
    }
    return '<div class="h"><span class="w">' + esc(m.fromName) + '</span>' + priv +
      '<span>' + esc(dt) + '</span></div>' + body;
  }

  function render(stick) {
    const title = $('kdTitle');
    const box = $('kdMsgs');
    const sel = $('kdPick');
    const comp = $('kdCompose');
    if (!box) return;
    if (sel) {
      sel.innerHTML = '';
      tables.forEach((t) => {
        const o = document.createElement('option');
        o.value = t.id;
        o.textContent = t.name;
        if (t.id === activeId) o.selected = true;
        sel.appendChild(o);
      });
    }
    if (title) title.textContent = activeDoc ? activeDoc.name : 'Table en cours';
    if (comp) comp.style.display = activeDoc ? '' : 'none';
    if (!activeDoc) {
      box.innerHTML = tables.length
        ? '<p class="kd-empty">Choisis une table ci-dessus.</p>'
        : '<p class="kd-empty">Aucune table. Crée-la depuis la page Tables.</p>';
      return;
    }
    markRead();
    const shown = (activeDoc.messages || []).filter(msgVisible).slice(-50);
    box.innerHTML = '';
    if (!shown.length) box.innerHTML = '<p class="kd-empty">Aucun message. Lance la discussion !</p>';
    shown.forEach((m) => {
      const d = document.createElement('div');
      d.className = 'kd-msg' + (m.private ? ' priv' : '');
      d.innerHTML = msgHtml(m);
      box.appendChild(d);
    });
    if (stick || box.scrollHeight - box.scrollTop < 600) box.scrollTop = box.scrollHeight;
    updateFab();
  }

  function renderLoggedOut() {
    const box = $('kdMsgs');
    const comp = $('kdCompose');
    const title = $('kdTitle');
    if (title) title.textContent = 'Table en cours';
    if (comp) comp.style.display = 'none';
    if (box) {
      box.innerHTML = '';
      const d = document.createElement('div');
      d.className = 'kd-center';
      d.innerHTML = '<p>Connecte-toi pour suivre le chat de ta table en direct.</p>';
      const b = document.createElement('button');
      b.type = 'button';
      b.textContent = 'Connexion';
      b.addEventListener('click', () => {
        const t = document.getElementById('authModalTrigger');
        if (t) t.click();
        else if (window.CodexAuth) window.CodexAuth.openModal();
      });
      d.appendChild(b);
      box.appendChild(d);
    }
    const sel = $('kdPick');
    if (sel) sel.innerHTML = '';
  }

  /* ---------- réseau ---------- */
  async function refreshMe() {
    try {
      if (window.CodexAuth && window.CodexAuth.currentUser) { me = window.CodexAuth.currentUser; return true; }
      const r = await api('/api/auth/me', { cache: 'no-store' });
      if (r.ok) { me = (await r.json()).user; return true; }
    } catch (e) {}
    me = null;
    return false;
  }

  async function loadTables() {
    try {
      const r = await api('/api/tables', { cache: 'no-store' });
      if (!r.ok) throw new Error();
      tables = ((await r.json()).tables) || [];
    } catch (e) { tables = []; }
    let stored = null;
    try { stored = localStorage.getItem('kt-tables-active'); } catch (e) {}
    if (stored && tables.some((t) => t.id === stored)) openTable(stored);
    else if (tables.length) openTable(tables[0].id);
    else { activeId = null; activeDoc = null; render(); }
  }

  async function openTable(id) {
    activeId = id;
    try { window.KT_ACTIVE_TABLE = id; } catch (e) {}
    try { localStorage.setItem('kt-tables-active', id); } catch (e) {}
    lastTs = 0;
    await fetchTable();
    clearInterval(pollTimer);
    pollTimer = setInterval(fetchQuiet, 5000);
  }

  async function fetchTable() {
    setStatus('Chargement…');
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '?limit=50', { cache: 'no-store' });
      if (!r.ok) throw new Error('Table inaccessible (' + r.status + ')');
      activeDoc = (await r.json()).table;
      (activeDoc.messages || []).forEach((m) => { if (m.createdAt > lastTs) lastTs = m.createdAt; });
      render(true);
      setStatus('À jour ✓', 'ok');
    } catch (e) {
      setStatus(e.message, 'err');
    }
  }

  async function fetchQuiet() {
    if (!activeId || !isOpen() || document.visibilityState === 'hidden') return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/messages?since=' + lastTs, { cache: 'no-store' });
      if (!r.ok) return;
      const d = await r.json();
      if (!activeDoc) return;
      if (Array.isArray(d.myInherited)) activeDoc.myInherited = d.myInherited;
      const fresh = (d.messages || []).filter((m) => m.createdAt > lastTs);
      if (!fresh.length) return;
      activeDoc.messages = (activeDoc.messages || []).concat(fresh);
      fresh.forEach((m) => { if (m.createdAt > lastTs) lastTs = m.createdAt; });
      render(true);
    } catch (e) {}
  }

  async function send() {
    const ta = $('kdText');
    if (!ta || !activeId) return;
    const text = ta.value.trim();
    if (!text) { setStatus('Message vide.', 'err'); return; }
    const btn = $('kdSend');
    if (btn) btn.disabled = true;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/messages', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text, toExcept: effectiveExcept() })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      activeDoc.messages.push(d.message);
      if (d.message.createdAt > lastTs) lastTs = d.message.createdAt;
      ta.value = '';
      render(true);
      setStatus('Envoyé ✓', 'ok');
    } catch (e) {
      setStatus(e.message, 'err');
    }
    if (btn) btn.disabled = false;
  }

  /* ---------- panneau ---------- */
  function isOpen() {
    const p = $('ktDock');
    return !!(p && p.classList.contains('open'));
  }
  function setOpen(open) {
    const p = $('ktDock');
    if (!p) return;
    p.classList.toggle('open', !!open);
    try { localStorage.setItem('kt-dock-open', open ? '1' : '0'); } catch (e) {}
    if (open && activeId) fetchTable();
    if (open) markRead();
  }

  function build() {
    if ($('ktDock') || $('ktDockFab')) return;
    const fab = document.createElement('button');
    fab.id = 'ktDockFab';
    fab.type = 'button';
    fab.title = 'Ouvrir le chat de la table en cours';
    fab.innerHTML = '<span>💬 Table</span><span class="kd-dot"></span>';
    fab.addEventListener('click', () => setOpen(!isOpen()));
    document.body.appendChild(fab);

    const p = document.createElement('aside');
    p.id = 'ktDock';
    p.setAttribute('aria-label', 'Chat de la table en cours');
    p.innerHTML =
      '<div class="kd-head"><span class="kd-title" id="kdTitle">Table en cours</span>' +
      '<button id="kdFull" type="button" title="Ouvrir la page Tables complète">⧉</button>' +
      '<button id="kdHide" type="button" title="Masquer le panneau">✕</button></div>' +
      '<div class="kd-pick"><select id="kdPick" aria-label="Choisir la table"></select></div>' +
      '<div id="kdMsgs"></div>' +
      '<div class="kd-compose" id="kdCompose">' +
      '<textarea id="kdText" maxlength="2000" placeholder="Écris ici… (Entrée = envoyer)"></textarea>' +
      '<div class="kd-row"><button id="kdSend" type="button">Envoyer</button><span class="kd-status" id="kdStatus"></span></div></div>' +
      '<div class="kd-foot">Images, MPs et masquages : <a id="kdFootLink" href="#">page Tables</a></div>';
    document.body.appendChild(p);

    const fullUrl = () => tablesPrefix() + 'tables.html' + (activeId ? '?table=' + encodeURIComponent(activeId) : '');
    $('kdHide').addEventListener('click', () => setOpen(false));
    $('kdFull').addEventListener('click', () => { window.location.href = fullUrl(); });
    $('kdFootLink').addEventListener('click', (e) => { e.preventDefault(); window.location.href = fullUrl(); });
    $('kdPick').addEventListener('change', (e) => openTable(e.target.value));
    $('kdSend').addEventListener('click', send);
    $('kdText').addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(); }
    });
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && isOpen()) setOpen(false);
    });
    window.addEventListener('storage', (e) => {
      if (e.key === 'kt-tables-unread') updateFab();
    });
  }

  async function boot() {
    build();
    let open = false;
    try { open = localStorage.getItem('kt-dock-open') === '1'; } catch (e) {}
    const ok = await refreshMe();
    if (!ok) { renderLoggedOut(); updateFab(); return; }
    await loadTables();
    updateFab();
    clearInterval(fabTimer);
    fabTimer = setInterval(updateFab, 15000);
    if (open && activeId) setOpen(true);
  }

  function init() {
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
    window.addEventListener('codex:auth:login', () => boot());
    window.addEventListener('codex:auth:logout', () => {
      me = null; tables = []; activeId = null; activeDoc = null;
      clearInterval(pollTimer);
      try { window.KT_ACTIVE_TABLE = null; } catch (e) {}
      renderLoggedOut();
      updateFab();
    });
  }

  init();
})();
