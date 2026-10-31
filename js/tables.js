/* Tables de groupe — chat persistant + images + MPs (REST /api/tables).
   Session via users/auth.js (cookie srv_). Polling 5 s sur la table active.
   Rôles : mj (voit tout, gère), helper (co-gère, voit tout), joueur (défaut). */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  // Même origine par défaut ; si la page vient du serveur statique (:8000)
  // ou d'un fichier local, l'API Node est sur :3000 (même hôte).
  // window.CODEX_API_BASE permet de forcer (tests, ports custom).
  const apiBase = () => {
    try {
      if (typeof window.CODEX_API_BASE === 'string') {
        return window.CODEX_API_BASE.replace(/\/+$/, '');
      }
      if (window.location.protocol === 'file:') return 'http://localhost:3000';
      if (window.location.port === '8000') {
        return window.location.protocol + '//' + window.location.hostname + ':3000';
      }
      const h = window.location.hostname;
      if ((h === 'localhost' || h === '127.0.0.1') && window.location.port && window.location.port !== '3000') {
        return 'http://localhost:3000';
      }
    } catch (e) {}
    return '';
  };
  const api = (p, opt) => fetch(apiBase() + p, Object.assign({ credentials: 'include', headers: { 'ngrok-skip-browser-warning': 'true' } }, opt || {}));
  const esc = (s) => String(s || '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  let me = null;
  let tables = [];
  let activeId = null;
  let activeDoc = null;
  let lastTs = 0;
  let pollTimer = null;
  let pendingImageData = null;
  let friends = { friends: [], incoming: [], outgoing: [] };
  let online = [];
  let onlineTimer = null;

  function status(el, msg, cls) {
    const e = $(el);
    if (!e) return;
    e.textContent = msg || '';
    e.className = 'tb-status' + (cls ? ' ' + cls : '');
  }

  function roleBadge(r) {
    return '<span class="tb-badge ' + esc(r || 'joueur') + '">' + esc(r || 'joueur') + '</span>';
  }

  async function refreshMe() {
    try {
      const r = await api('/api/auth/me', { cache: 'no-store' });
      if (r.ok) { me = (await r.json()).user; return true; }
    } catch (e) {}
    me = null;
    return false;
  }

  async function loadTables() {
    status('tbListStatus', 'Chargement…');
    try {
      const r = await api('/api/tables', { cache: 'no-store' });
      if (!r.ok) throw new Error(r.status === 401 ? 'Connexion requise.' : 'Erreur ' + r.status);
      const d = await r.json();
      tables = d.tables || [];
      // Purge les repères des tables disparues (supprimées) : pas de
      // fantômes dans la pastille ni de repères morts.
      try {
        const ids = {};
        tables.forEach((t) => { ids[t.id] = true; });
        ['kt-tables-lastread', 'kt-tables-unread', 'kt-tables-hide', 'kt-tables-inherit'].forEach((k) => {
          let m = null;
          try { m = JSON.parse(localStorage.getItem(k) || '{}'); } catch (e) { m = {}; }
          if (!m || typeof m !== 'object') return;
          let touched = false;
          Object.keys(m).forEach((id) => { if (!ids[id]) { delete m[id]; touched = true; } });
          if (touched) localStorage.setItem(k, JSON.stringify(m));
        });
        try { localStorage.removeItem('kt-tables-inherit'); } catch (e) {}
        if (window.CodexAuth) window.CodexAuth.updateTablesBadge();
      } catch (e) {}
      renderList();
      status('tbListStatus', tables.length ? '' : 'Aucune table. Crée la tienne !', 'ok');
    } catch (e) {
      status('tbListStatus', e.message, 'err');
    }
  }

  function renderList() {
    const box = $('tbList');
    box.innerHTML = '';
    tables.forEach((t) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'tb-item' + (t.id === activeId ? ' on' : '');
      b.innerHTML = '<div class="nm">' + esc(t.name) + roleBadge(t.role) + '</div>' +
        '<div class="meta">' + esc(t.mjName) + ' · ' + t.members + ' membres · ' + t.messages + ' msgs</div>';
      b.addEventListener('click', () => openTable(t.id));
      box.appendChild(b);
    });
  }

  async function openTable(id) {
    activeId = id;
    try { window.KT_ACTIVE_TABLE = id; } catch (e) {}
    try { localStorage.setItem('kt-tables-active', id); } catch (e) {}
    lastTs = 0;
    renderList();
    await fetchTable();
    clearInterval(pollTimer);
    pollTimer = setInterval(fetchTableQuiet, 5000);
  }

  // Table vue = marquée lue : efface la pastille et cale le repère (le
  // watcher global ne popupe plus ces messages-là).
  function markTableRead() {
    if (!activeId) return;
    try {
      const lr = JSON.parse(localStorage.getItem('kt-tables-lastread') || '{}');
      lr[activeId] = Math.max(lr[activeId] || 0, lastTs);
      localStorage.setItem('kt-tables-lastread', JSON.stringify(lr));
      const un = JSON.parse(localStorage.getItem('kt-tables-unread') || '{}');
      if (un[activeId]) {
        delete un[activeId];
        localStorage.setItem('kt-tables-unread', JSON.stringify(un));
      }
      if (window.CodexAuth) window.CodexAuth.updateTablesBadge();
    } catch (e) {}
  }

  // Éjecté ou table supprimée (403/404 au poll ou à l'ouverture) : on ne
  // reste pas sur une vue figée — on sort proprement avec un message clair.
  function handleTableGone(statusCode) {
    const was = activeId;
    clearInterval(pollTimer);
    activeId = null; activeDoc = null;
    try { window.KT_ACTIVE_TABLE = null; } catch (e) {}
    try { localStorage.removeItem('kt-tables-active'); } catch (e) {}
    loadTables(); renderChat(); renderManage();
    status('tbChatStatus',
      statusCode === 404 ? 'Cette table a été supprimée.' : 'Vous ne faites plus partie de cette table.',
      'err');
    return was;
  }

  async function fetchTable() {
    status('tbChatStatus', 'Chargement…');
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '?limit=200', { cache: 'no-store' });
      if (!r.ok) {
        if ((r.status === 403 || r.status === 404) && activeId) { handleTableGone(r.status); return; }
        throw new Error('Erreur ' + r.status);
      }
      const d = await r.json();
      activeDoc = d.table;
      activeDoc.messages.forEach((m) => {
        if (m.createdAt > lastTs) lastTs = m.createdAt;
        // Héritage à l'ouverture : uniquement les fils encore chauds (< 10 min),
        // pas tout l'historique.
        if (msgVisible(m) && (m.createdAt || 0) > Date.now() - 600000) {
          mergeSendExcept(exceptFromMsg(m));
        }
      });
      renderChat();
      renderManage();
      status('tbChatStatus', 'À jour ✓', 'ok');
    } catch (e) {
      status('tbChatStatus', e.message, 'err');
    }
  }

  async function fetchTableQuiet() {
    if (!activeId) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/messages?since=' + lastTs, { cache: 'no-store' });
      if (!r.ok) {
        if ((r.status === 403 || r.status === 404) && activeId) handleTableGone(r.status);
        return;
      }
      const d = await r.json();
      if (!activeDoc) return;
      // État d'envoi serveur toujours frais (même sans nouveau message visible) :
      // la prochaine réponse hérite sans attendre une réouverture.
      if (Array.isArray(d.myInherited)) activeDoc.myInherited = d.myInherited;
      const fresh = (d.messages || []).filter((m) => m.createdAt > lastTs);
      if (!fresh.length) { renderSendScope(); return; }
      activeDoc.messages = activeDoc.messages.concat(fresh);
      fresh.forEach((m) => {
        if (m.createdAt > lastTs) lastTs = m.createdAt;
        // Fusion immédiate (zéro délai) : confirmée par le serveur au poll suivant.
        if (msgVisible(m)) mergeSendExcept(exceptFromMsg(m));
      });
      renderChat(true);
    } catch (e) {}
  }

  function msgHtml(m) {
    const priv = m.private ? '<span class="tb-badge priv">privé → ' + esc(m.toName || '') + '</span>' : '';
    let ava = avaImg(m.fromAvatar);
    // Clic avatar = sendto/dontsendto : choisir qui reçoit TES futurs messages
    // (et masquer ses messages chez toi). Personnel, invisible aux autres.
    if (m.fromId !== (me && me.id)) {
      ava = '<button class="tb-avbtn" data-xt="' + esc(m.fromId) + '" type="button" title="Ne plus échanger : tes futurs messages ne lui seront plus envoyés (et réciproquement). Cliquer à nouveau pour rétablir.">' + (ava || '👤') + '</button>';
    }
    let body = m.text ? '<div>' + esc(m.text).replace(/\n/g, '<br>') + '</div>' : '';
    if (m.image && m.image.kind === 'url') body += '<img src="' + esc(m.image.url) + '" loading="lazy" alt="image partagée">';
    if (m.image && m.image.kind === 'upload') body += '<img src="' + m.image.dataUrl + '" loading="lazy" alt="image partagée">';
    const canDel = me && (m.fromId === me.id || ['mj', 'helper', 'admin'].includes(activeDoc.myRole));
    const del = canDel ? '<button class="del" data-del="' + esc(m.id) + '" title="Supprimer">✕</button>' : '';
    const dt = new Date(m.createdAt).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
    let restr = '';
    if (m.receivedBy && m.receivedBy.length) {
      restr = '<div class="tb-restr" title="Envoyé à un sous-groupe (choix de l’envoyeur)">📨 Reçu par : ' +
        esc(m.receivedBy.join(', ')) + '</div>';
    }
    return '<div class="head"><span class="who">' + ava + esc(m.fromName) + '</span>' + roleBadge(m.fromRole) + priv +
      '<span>' + esc(dt) + '</span>' + del + '</div>' + body + restr;
  }

  function renderChat(stick) {
    const box = $('tbMessages');
    $('tbChatTitle').textContent = activeDoc ? activeDoc.name : 'Choisis une table';
    $('tbComposer').style.display = activeDoc ? '' : 'none';
    if (!activeDoc) { box.innerHTML = ''; renderSendScope(); return; }
    markTableRead();
    renderSendScope();
    box.innerHTML = '';
    // Filtre personnel : seuls les messages POSTÉRIEURS au masquage sont
    // cachés — l'historique déjà loggé reste visible.
    const shown = (activeDoc.messages || []).filter(msgVisible);
    if (!shown.length) box.innerHTML = '<p class="tb-status">Aucun message. Lance la discussion !</p>';
    shown.forEach((m) => {
      const d = document.createElement('div');
      d.className = 'tb-msg' + (m.private ? ' priv' : '');
      d.innerHTML = msgHtml(m);
      box.appendChild(d);
    });
    box.querySelectorAll('[data-del]').forEach((btn) => {
      btn.addEventListener('click', () => deleteMessage(btn.getAttribute('data-del')));
    });
    box.querySelectorAll('[data-xt]').forEach((btn) => {
      btn.addEventListener('click', () => toggleHide(btn.getAttribute('data-xt')));
    });
    // Destinataires MP : membres sauf moi.
    const sel = $('tbTo');
    sel.innerHTML = '<option value="">Public (tout le monde)</option>';
    activeDoc.members.forEach((mb) => {
      if (me && mb.userId === me.id) return;
      const o = document.createElement('option');
      o.value = mb.userId;
      o.textContent = '🔒 ' + mb.username + ' (' + mb.role + ')';
      sel.appendChild(o);
    });
    if (stick) box.scrollTop = box.scrollHeight;
    else if (box.scrollHeight - box.scrollTop < 600) box.scrollTop = box.scrollHeight;
  }

  function renderManage() {
    const box = $('tbManage');
    if (!activeDoc) { box.innerHTML = '<p class="tb-status">Sélectionne une table.</p>'; return; }
    const t = activeDoc;
    const manager = ['mj', 'helper', 'admin'].includes(t.myRole);
    const isMj = t.myRole === 'mj' || t.myRole === 'admin';
    let h = '<div class="tb-field"><label>MJ</label><div>' + esc(t.mjName) + '</div></div>';
    h += '<div class="tb-field"><label>Membres (' + t.members.length + ')</label><div>';
    t.members.forEach((mb) => {
      const hidden = mb.userId !== (me && me.id) && isHidden(t.id, mb.userId);
      h += '<div class="tb-member' + (hidden ? ' hidden-me' : '') + '"><span>' + avaImg(mb.avatar) + esc(mb.username) + '</span>' + roleBadge(mb.role);
      if (hidden) h += '<span class="tb-badge priv" title="Masqué pour toi seul">masqué pour toi</span>';
      if (mb.userId !== (me && me.id)) {
        h += '<button class="tb-btn" data-hide="' + esc(mb.userId) + '" type="button" title="' +
          (hidden ? 'Rétablir les échanges' : 'Ne plus échanger (tes futurs messages ne lui seront plus envoyés, et réciproquement)') + '">' +
          (hidden ? '👁️' : '👁️‍🗨️') + '</button>';
      }
      if (manager && mb.userId !== t.mjId) {
        h += '<select data-role="' + esc(mb.userId) + '">' +
          ['joueur', 'helper'].map((r) => '<option value="' + r + '"' + (mb.role === r ? ' selected' : '') + '>' + r + '</option>').join('') +
          (isMj ? '<option value="mj"' + (mb.role === 'mj' ? ' selected' : '') + '>mj (transfert)</option>' : '') + '</select>';
        h += '<button class="kick" data-kick="' + esc(mb.userId) + '" title="Exclure">✕</button>';
      }
      h += '</div>';
    });
    h += '</div></div>';
    if (manager) {
      h += '<div class="tb-field"><label>Lien d’invitation (copiable)</label>' +
        '<div style="display:flex;gap:6px;"><input id="tbInviteLink" readonly placeholder="Aucun lien — génère-le">' +
        '<button class="tb-btn" id="tbCopyLink" type="button" title="Copier">⧉</button></div></div>';
      h += '<button class="tb-btn" id="tbGenLink" type="button">Générer le lien</button> ';
      h += '<button class="tb-btn" id="tbRegenLink" type="button" title="Ancien lien invalidé">Régénérer</button> ';
      h += '<button class="tb-btn danger" id="tbRevokeLink" type="button">Révoquer</button>';
      h += '<div class="tb-field" style="margin-top:10px;"><label>Membres max (2-24)</label>' +
        '<div style="display:flex;gap:6px;"><input id="tbMaxM" type="number" min="2" max="24" value="' + (t.maxMembers || 12) + '">' +
        '<button class="tb-btn" id="tbMaxMBtn" type="button">OK</button></div></div>';
      h += '<div class="tb-field"><label>Mot de passe ' + (t.hasPassword ? '(défini 🔒)' : '(aucun)') + '</label>' +
        '<div class="tb-row"><input id="tbPassSet" type="password" maxlength="64" placeholder="Nouveau mot de passe">' +
        '<button class="tb-btn" id="tbPassSetBtn" type="button">OK</button></div></div>';
      h += '<div class="tb-row"><button class="tb-btn" id="tbPassClear" type="button">Retirer le mot de passe</button></div>';
      h += '<div class="tb-field" style="margin-top:10px;"><label style="display:flex;gap:6px;align-items:center;">' +
        '<input type="checkbox" id="tbListed" ' + (t.listed === false ? '' : 'checked') + ' style="width:auto;"> Visible dans l’annuaire</label></div>';
      h += '<div class="tb-status" id="tbLinkStatus"></div>';
      h += '<hr style="border:none;border-top:1px solid var(--border-subtle);margin:12px 0;">';
      h += '<div class="tb-field"><label>Inviter (pseudo ou e-mail)</label><input id="tbInvite" placeholder="ex : RogerVardis"></div>';
      h += '<button class="tb-btn" id="tbInviteBtn" type="button">Inviter</button>';
      h += '<hr style="border:none;border-top:1px solid var(--border-subtle);margin:12px 0;">';
      h += '<div class="tb-field"><label>Renommer</label><input id="tbRename" value="' + esc(t.name) + '" maxlength="40"></div>';
      h += '<button class="tb-btn" id="tbRenameBtn" type="button">Renommer</button> ';
      if (isMj) h += '<button class="tb-btn danger" id="tbDeleteBtn" type="button">Supprimer la table</button>';
      h += '<div class="tb-status" id="tbManageStatus"></div>';
    } else {
      h += '<p class="tb-status">Gestion réservée au MJ / helper.</p>';
      h += '<button class="tb-btn" id="tbLeaveBtn" type="button">Quitter la table</button><div class="tb-status" id="tbManageStatus"></div>';
    }
    box.innerHTML = h;
    // Masquage personnel : pour tout le monde (pas seulement le staff).
    box.querySelectorAll('[data-hide]').forEach((btn) => {
      btn.addEventListener('click', () => toggleHide(btn.getAttribute('data-hide')));
    });
    if (manager) {
      box.querySelectorAll('[data-role]').forEach((sel) => {
        sel.addEventListener('change', () => setRole(sel.getAttribute('data-role'), sel.value));
      });
      box.querySelectorAll('[data-kick]').forEach((btn) => {
        btn.addEventListener('click', () => kickMember(btn.getAttribute('data-kick')));
      });
      fillInviteLink();
      const gen = $('tbGenLink');
      if (gen) gen.addEventListener('click', () => ensureInviteLink(false));
      const reg = $('tbRegenLink');
      if (reg) reg.addEventListener('click', () => {
        if (confirm('Régénérer ? L’ancien lien ne marchera plus.')) ensureInviteLink(true);
      });
      const rev = $('tbRevokeLink');
      if (rev) rev.addEventListener('click', () => revokeInviteLink());
      const cp = $('tbCopyLink');
      if (cp) cp.addEventListener('click', copyInviteLink);
      const mm = $('tbMaxMBtn');
      if (mm) mm.addEventListener('click', setMaxMembers);
      const ps = $('tbPassSetBtn');
      if (ps) ps.addEventListener('click', () => setTablePassword(false));
      const pc = $('tbPassClear');
      if (pc) pc.addEventListener('click', () => setTablePassword(true));
      const li = $('tbListed');
      if (li) li.addEventListener('change', () => setTableListed(li.checked));
      const inv = $('tbInviteBtn');
      if (inv) inv.addEventListener('click', inviteMember);
      const rn = $('tbRenameBtn');
      if (rn) rn.addEventListener('click', renameTable);
      const del = $('tbDeleteBtn');
      if (del) del.addEventListener('click', deleteTable);
    } else {
      const lv = $('tbLeaveBtn');
      if (lv) lv.addEventListener('click', leaveTable);
    }
    renderFriends();
    renderOnline();
  }

  async function sendMessage() {
    const text = $('tbText').value.trim();
    const imageUrl = $('tbImageUrl').value.trim();
    const toUserId = $('tbTo').value || null;
    if (!text && !imageUrl && !pendingImageData) { status('tbChatStatus', 'Message vide.', 'err'); return; }
    // Ma liste d'envoi : mes futurs messages ne sont livrés qu'aux non-masqués.
    // (MP nominatif : livré à son destinataire quoi qu'il arrive.)
    let toExcept = effectiveExcept();
    $('tbSend').disabled = true;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/messages', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text, imageUrl, imageData: pendingImageData, toUserId, toExcept })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      activeDoc.messages.push(d.message);
      if (d.message.createdAt > lastTs) lastTs = d.message.createdAt;
      $('tbText').value = ''; $('tbImageUrl').value = ''; $('tbFile').value = ''; pendingImageData = null;
      renderChat(true);
      status('tbChatStatus', 'Envoyé ✓', 'ok');
    } catch (e) {
      status('tbChatStatus', e.message, 'err');
    }
    $('tbSend').disabled = false;
  }

  async function deleteMessage(mid) {
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/messages/' + encodeURIComponent(mid), { method: 'DELETE' });
      if (!r.ok) throw new Error((await r.json().catch(() => ({}))).error || ('Erreur ' + r.status));
      activeDoc.messages = activeDoc.messages.filter((m) => m.id !== mid);
      renderChat();
    } catch (e) { status('tbChatStatus', e.message, 'err'); }
  }

  async function inviteMember() {
    const v = $('tbInvite').value.trim();
    if (!v) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/members', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ user: v })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      status('tbManageStatus', d.member.username + ' invité ✓', 'ok');
      await fetchTable(); await loadTables();
    } catch (e) { status('tbManageStatus', e.message, 'err'); }
  }

  async function setRole(uid, role) {
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/members/' + encodeURIComponent(uid), {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ role })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      status('tbManageStatus', 'Rôle → ' + role + ' ✓', 'ok');
      await fetchTable(); await loadTables();
    } catch (e) { status('tbManageStatus', e.message, 'err'); fetchTable(); }
  }

  async function kickMember(uid) {
    if (!confirm('Exclure ce membre ?')) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/members/' + encodeURIComponent(uid), { method: 'DELETE' });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      await fetchTable(); await loadTables();
    } catch (e) { status('tbManageStatus', e.message, 'err'); }
  }

  async function leaveTable() {
    if (!confirm('Quitter cette table ?')) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/members/' + encodeURIComponent(me.id), { method: 'DELETE' });
      if (!r.ok) throw new Error((await r.json().catch(() => ({}))).error || ('Erreur ' + r.status));
      activeId = null; activeDoc = null;
      try { window.KT_ACTIVE_TABLE = null; } catch (e) {}
      try { localStorage.removeItem('kt-tables-active'); } catch (e) {}
      clearInterval(pollTimer);
      await loadTables(); renderChat(); renderManage();
    } catch (e) { status('tbManageStatus', e.message, 'err'); }
  }

  async function renameTable() {
    const v = $('tbRename').value.trim();
    if (!v) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId), {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: v })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      status('tbManageStatus', 'Renommée ✓', 'ok');
      await fetchTable(); await loadTables();
    } catch (e) { status('tbManageStatus', e.message, 'err'); }
  }

  async function deleteTable() {
    if (!confirm('Supprimer définitivement cette table et son log ?')) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId), { method: 'DELETE' });
      if (!r.ok) throw new Error((await r.json().catch(() => ({}))).error || ('Erreur ' + r.status));
      activeId = null; activeDoc = null;
      try { window.KT_ACTIVE_TABLE = null; } catch (e) {}
      try { localStorage.removeItem('kt-tables-active'); } catch (e) {}
      clearInterval(pollTimer);
      await loadTables(); renderChat(); renderManage();
    } catch (e) { status('tbManageStatus', e.message, 'err'); }
  }

  // Downscale générique : fichier image -> dataURL (max px, qualité jpeg).
  function downscaleImage(file, maxPx, mime, quality, cb) {
    if (!file || !/^image\/(png|jpe?g|gif|webp)$/.test(file.type)) { cb(null, 'Format image non pris en charge.'); return; }
    const img = new Image();
    const url = URL.createObjectURL(file);
    img.onload = () => {
      try {
        let w = img.naturalWidth, h = img.naturalHeight;
        const k = Math.min(1, maxPx / Math.max(w, h));
        w = Math.max(1, Math.round(w * k)); h = Math.max(1, Math.round(h * k));
        const cv = document.createElement('canvas');
        cv.width = w; cv.height = h;
        cv.getContext('2d').drawImage(img, 0, 0, w, h);
        URL.revokeObjectURL(url);
        cb(cv.toDataURL(mime, quality), null);
      } catch (e) { URL.revokeObjectURL(url); cb(null, 'Lecture image impossible.'); }
    };
    img.onerror = () => { URL.revokeObjectURL(url); cb(null, 'Lecture image impossible.'); };
    img.src = url;
  }
  function avaImg(avatar, cls) {
    if (!avatar) return '';
    return '<img class="' + (cls || 'tb-ava') + '" src="' + avatar + '" alt="" loading="lazy">';
  }
  // Upload message : downscale → max 1024 px → dataURL (plafond images).
  function handleFile(file) {
    pendingImageData = null;
    if (!file) return;
    downscaleImage(file, 1024, file.type === 'image/png' ? 'image/png' : 'image/jpeg', 0.85, (dataUrl, err) => {
      if (err || !dataUrl) { status('tbChatStatus', err || 'Lecture image impossible.', 'err'); return; }
      if (dataUrl.length > 2800000) {
        status('tbChatStatus', 'Image trop lourde même réduite.', 'err');
        return;
      }
      pendingImageData = dataUrl;
      status('tbChatStatus', 'Image prête (' + Math.round(dataUrl.length / 1024) + ' Ko).', 'ok');
    });
  }

  /* ---------- lien d'invitation copiable ---------- */
  function inviteAbsUrl(code) {
    return window.location.origin + window.location.pathname + '?invite=' + encodeURIComponent(code);
  }
  function fillInviteLink() {
    const inp = $('tbInviteLink');
    if (!inp || !activeDoc) return;
    inp.value = activeDoc.inviteCode ? inviteAbsUrl(activeDoc.inviteCode) : '';
  }
  async function ensureInviteLink(regen) {
    if (!activeId) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/invite', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ regen: !!regen })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      if (activeDoc) activeDoc.inviteCode = d.code;
      fillInviteLink();
      status('tbLinkStatus', 'Lien prêt — copie-le ✓', 'ok');
    } catch (e) { status('tbLinkStatus', e.message, 'err'); }
  }
  async function revokeInviteLink() {
    if (!activeId || !confirm('Révoquer le lien ? Il ne marchera plus.')) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/invite', { method: 'DELETE' });
      if (!r.ok) throw new Error((await r.json().catch(() => ({}))).error || ('Erreur ' + r.status));
      if (activeDoc) activeDoc.inviteCode = null;
      fillInviteLink();
      status('tbLinkStatus', 'Lien révoqué.', 'ok');
    } catch (e) { status('tbLinkStatus', e.message, 'err'); }
  }
  async function copyInviteLink() {
    const inp = $('tbInviteLink');
    if (!inp || !inp.value) { status('tbLinkStatus', 'Génère d’abord le lien.', 'err'); return; }
    try {
      await navigator.clipboard.writeText(inp.value);
      status('tbLinkStatus', 'Copié ✓', 'ok');
    } catch (e) {
      inp.select();
      try { document.execCommand('copy'); status('tbLinkStatus', 'Copié ✓', 'ok'); }
      catch (e2) { status('tbLinkStatus', 'Copie manuelle : Ctrl+C.', 'err'); }
    }
  }
  async function setMaxMembers() {
    const v = parseInt(($('tbMaxM') || {}).value, 10);
    if (!activeId || !Number.isFinite(v)) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId), {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ maxMembers: v })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      status('tbLinkStatus', 'Max → ' + v + ' ✓', 'ok');
      await fetchTable();
    } catch (e) { status('tbLinkStatus', e.message, 'err'); }
  }
  async function joinWithCode(code) {
    try {
      const r = await api('/api/tables/join', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      await loadTables();
      openTable(d.table.id);
      status('tbChatStatus', d.already ? 'Déjà membre — table ouverte.' : 'Bienvenue à la table ✓', 'ok');
    } catch (e) {
      status('tbChatStatus', 'Invitation : ' + e.message, 'err');
    }
    try { window.history.replaceState({}, document.title, window.location.pathname); } catch (e) {}
  }

  /* ---------- mon avatar ---------- */
  let pendingAvatarData = null;
  function renderAvatar() {
    const box = $('tbAvatar');
    if (!box) return;
    const cur = (me && me.avatar) || null;
    box.innerHTML =
      '<div style="display:flex;align-items:center;margin-bottom:8px;">' +
      (cur ? avaImg(cur, 'tb-ava big') : '<span class="tb-ava big" style="display:inline-flex;align-items:center;justify-content:center;font-weight:800;">' + esc((me && me.username || '?').charAt(0).toUpperCase()) + '</span>') +
      '<span class="tb-status">Vu en chat + popups</span></div>' +
      '<div class="tb-field"><label>URL d’image</label><div class="tb-row"><input id="tbAvaUrl" placeholder="https://...">' +
      '<button class="tb-btn" id="tbAvaUrlBtn" type="button">OK</button></div></div>' +
      '<div class="tb-field"><label>…ou fichier (réduit à 256 px)</label><input id="tbAvaFile" type="file" accept="image/png,image/jpeg,image/gif,image/webp"></div>' +
      '<div class="tb-row"><button class="tb-btn" id="tbAvaClear" type="button">Retirer</button></div>' +
      '<div class="tb-status" id="tbAvaStatus"></div>';
    $('tbAvaUrlBtn').addEventListener('click', () => saveAvatar({ imageUrl: $('tbAvaUrl').value.trim() }));
    $('tbAvaFile').addEventListener('change', (e) => {
      const f = e.target.files[0];
      if (!f) return;
      downscaleImage(f, 256, 'image/jpeg', 0.8, (dataUrl, err) => {
        if (err || !dataUrl) { status('tbAvaStatus', err || 'Lecture impossible.', 'err'); return; }
        if (dataUrl.length > 700000) { status('tbAvaStatus', 'Avatar trop lourd.', 'err'); return; }
        pendingAvatarData = dataUrl;
        saveAvatar({ imageData: dataUrl });
      });
    });
    $('tbAvaClear').addEventListener('click', () => saveAvatar({ clear: true }));
  }
  async function saveAvatar(payload) {
    if (!payload.imageUrl && !payload.imageData && !payload.clear) {
      status('tbAvaStatus', 'URL ou fichier requis.', 'err'); return;
    }
    try {
      const r = await api('/api/auth/avatar', {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload)
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      me = d.user;
      try {
        const ls = JSON.parse(localStorage.getItem('codex_active_session') || '{}');
        ls.avatar = me.avatar || null;
        localStorage.setItem('codex_active_session', JSON.stringify(ls));
      } catch (e) {}
      if (window.CodexAuth) window.CodexAuth.mountTopbarWidget();
      renderAvatar();
      status('tbAvaStatus', payload.clear ? 'Avatar retiré.' : 'Avatar à jour ✓', 'ok');
      if (activeId) fetchTable();
    } catch (e) { status('tbAvaStatus', e.message, 'err'); }
  }

  /* ---------- annuaire des tables ---------- */
  let directory = [];
  async function loadDirectory() {
    status('tbDirStatus', 'Chargement…');
    try {
      const r = await api('/api/tables/directory', { cache: 'no-store' });
      if (!r.ok) throw new Error('Erreur ' + r.status);
      directory = ((await r.json()).tables) || [];
      renderDirectory();
      status('tbDirStatus', directory.length ? '' : 'Aucune table listée.');
    } catch (e) { status('tbDirStatus', e.message, 'err'); }
  }
  function renderDirectory() {
    const box = $('tbDirectory');
    if (!box) return;
    box.innerHTML = '';
    directory.forEach((t) => {
      if (t.isMember) return; // déjà dedans : voir « Mes tables »
      const d = document.createElement('div');
      d.className = 'tb-item';
      d.innerHTML = '<div class="nm">' + (t.hasPassword ? '🔒 ' : '') + esc(t.name) + '</div>' +
        '<div class="meta">' + esc(t.mjName) + ' · ' + t.members + ' membres · ' + t.messages + ' msgs</div>' +
        (t.hasPassword ? '<div class="tb-row" style="margin-top:6px;"><input type="password" data-dpass="' + esc(t.id) + '" placeholder="Mot de passe" maxlength="64"></div>' : '');
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'tb-btn';
      btn.style.marginTop = '6px';
      btn.textContent = 'Rejoindre';
      btn.addEventListener('click', () => joinDirectory(t.id));
      d.appendChild(btn);
      box.appendChild(d);
    });
    if (!box.children.length) box.innerHTML = '<p class="tb-status">Rien à rejoindre — crée ta table !</p>';
  }
  async function joinDirectory(id) {
    const inp = document.querySelector('[data-dpass="' + id + '"]');
    const password = inp ? inp.value : '';
    try {
      const r = await api('/api/tables/' + encodeURIComponent(id) + '/join', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      await loadTables();
      await loadDirectory();
      openTable(id);
    } catch (e) { status('tbDirStatus', e.message, 'err'); }
  }

  /* ---------- réglages table (mot de passe, annuaire) ---------- */
  async function setTablePassword(clear) {
    if (!activeId) return;
    const v = clear ? '' : (($('tbPassSet') || {}).value || '');
    if (!clear && !v) { status('tbLinkStatus', 'Mot de passe vide.', 'err'); return; }
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId), {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password: v })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      if ($('tbPassSet')) $('tbPassSet').value = '';
      status('tbLinkStatus', clear ? 'Mot de passe retiré.' : 'Mot de passe défini ✓', 'ok');
      await fetchTable();
    } catch (e) { status('tbLinkStatus', e.message, 'err'); }
  }
  async function setTableListed(listed) {
    if (!activeId) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId), {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ listed })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      status('tbLinkStatus', listed ? 'Table visible dans l’annuaire.' : 'Table masquée de l’annuaire.', 'ok');
      await fetchTable();
      await loadDirectory();
    } catch (e) { status('tbLinkStatus', e.message, 'err'); fetchTable(); }
  }

  /* ---------- masquage personnel (sendto / dontsendto) ----------
     Clic avatar/nom = choisir qui reçoit TES futurs messages (et masquer
     LEURS FUTURS messages chez toi — l'historique déjà loggé reste visible).
     Stocké en local : {tableId: {userId: mutedSince}}. Invisible aux autres. */
  function getHide() {
    try {
      const h = JSON.parse(localStorage.getItem('kt-tables-hide') || '{}') || {};
      let fixed = false;
      Object.keys(h).forEach((t) => {
        if (Array.isArray(h[t])) {
          // Migration ancien format (liste sans date) : masqué depuis toujours.
          const o = {};
          h[t].forEach((id) => { if (id) o[id] = 0; });
          h[t] = o; fixed = true;
        } else if (h[t] && typeof h[t] !== 'object') {
          delete h[t]; fixed = true;
        }
      });
      if (fixed) { try { localStorage.setItem('kt-tables-hide', JSON.stringify(h)); } catch (e) {} }
      return h;
    } catch (e) { return {}; }
  }
  // Timestamp du masquage (null = pas masqué). Un message n'est masqué
  // à l'affichage que s'il est POSTÉRIEUR au clic.
  function muteSince(tableId, uid) {
    const t = getHide()[tableId];
    if (!t || t[uid] === undefined) return null;
    return t[uid];
  }
  // Un message n'est pris en compte (affichage + héritage) que s'il est
  // visible : pas masqué, ou antérieur au masquage (historique conservé).
  function msgVisible(m) {
    const s = muteSince(activeId, m.fromId);
    return s === null || (m.createdAt || 0) <= s;
  }
  // Exclus d'un message : toExcept (serveur récent) ou dérivés de
  // receivedBy (noms des destinataires croisés aux membres actuels).
  // Permet l'héritage même si le serveur n'envoie pas encore toExcept.
  function exceptFromMsg(m) {
    if (!m || !activeDoc) return [];
    if (Array.isArray(m.toExcept) && m.toExcept.length) return m.toExcept.slice(0, 200);
    if (Array.isArray(m.receivedBy) && m.receivedBy.length) {
      const byName = {};
      (activeDoc.members || []).forEach((x) => { if (x) byName[x.username] = x.userId; });
      const got = new Set([m.fromId]);
      m.receivedBy.forEach((n) => { if (byName[n]) got.add(byName[n]); });
      return (activeDoc.members || [])
        .filter((x) => x && !got.has(x.userId)).map((x) => x.userId).slice(0, 200);
    }
    return [];
  }
  // Mémorise les exclus pour mes prochains envois (union, 200 max).
  function mergeSendExcept(ids) {
    if (!activeId || !Array.isArray(ids) || !ids.length) return;
    const cur = new Set((activeDoc && activeDoc.myInherited) || []);
    let changed = false;
    ids.forEach((id) => { id = String(id || '').slice(0, 64); if (id && !cur.has(id)) { cur.add(id); changed = true; } });
    if (changed && activeDoc) activeDoc.myInherited = [...cur].slice(0, 200);
  }
  function isHidden(tableId, uid) { return muteSince(tableId, uid) !== null; }
  function toggleHide(uid) {
    if (!activeId || !uid || (me && uid === me.id)) return;
    const h = getHide();
    const tmap = h[activeId] || (h[activeId] = {});
    const muting = tmap[uid] === undefined;
    if (muting) tmap[uid] = Date.now(); else delete tmap[uid];
    if (Object.keys(tmap).length === 0) delete h[activeId];
    try { localStorage.setItem('kt-tables-hide', JSON.stringify(h)); } catch (e) {}
    try {
      const tgt = (activeDoc.members || []).find((m) => m.userId === uid);
      status('tbManageStatus', muting
        ? ('🔕 Échanges coupés avec ' + (tgt ? tgt.username : '?') + ' (futurs messages ; historique conservé).')
        : '🔔 Échanges rétablis.', 'ok');
    } catch (e) {}
    renderChat(true);
    renderManage();
  }

  /* ---------- héritage d'exclusion (répondre = re-muter, serveur) ----------
     Quand je reçois un message restreint, le serveur ajoute ses exclus à MA
     liste d'envoi (doc.inherited[moi]) : mon PROCHAIN message les exclut
     automatiquement. Le bouton ↺ (send-reset) rouvre l'envoi + efface aussi
     mes masqués perso de cette table. */
  // Exclus hérités (serveur) pour la table active.
  function inheritedExcept() {
    try {
      const l = (activeDoc && activeDoc.myInherited) || [];
      return Array.isArray(l) ? l.slice(0, 200) : [];
    } catch (e) { return []; }
  }
  async function resetSend() {
    if (!activeId) return;
    // Reset total : héritage serveur + masqués perso de cette table.
    // (« Remettre tout le monde pas muted ».) Tolère un vieux serveur
    // sans /send-reset (404) : le local est quand même nettoyé.
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/send-reset', { method: 'POST' });
      if (!r.ok && r.status !== 404) {
        const d = await r.json().catch(() => ({}));
        throw new Error(d.error || ('Erreur ' + r.status));
      }
    } catch (e) { status('tbChatStatus', e.message, 'err'); return; }
    try {
      const hm = getHide();
      if (hm[activeId]) { delete hm[activeId]; }
      localStorage.setItem('kt-tables-hide', JSON.stringify(hm));
    } catch (e) {}
    try { localStorage.removeItem('kt-tables-inherit'); } catch (e) {}
    status('tbChatStatus', 'Envoi à nouveau ouvert à tout le monde ✓', 'ok');
    await fetchTable();
  }
  // Liste d'envoi effective : mes masqués + exclus hérités (serveur),
  // limités aux membres.
  function effectiveExcept() {
    const out = new Set();
    try {
      const hide = getHide()[activeId] || {};
      Object.keys(hide).forEach((id) => out.add(id));
      inheritedExcept().forEach((id) => out.add(id));
    } catch (e) {}
    const memberIds = {};
    try { (activeDoc.members || []).forEach((m) => { if (m && m.userId) memberIds[m.userId] = true; }); } catch (e) {}
    return [...out].filter((id) => id && id !== (me && me.id) && memberIds[id]).slice(0, 200);
  }
  function renderSendScope() {
    const box = $('tbSendScope');
    if (!box || !activeDoc) { if (box) box.innerHTML = ''; return; }
    let names = [];
    try {
      const byId = {};
      (activeDoc.members || []).forEach((m) => { if (m) byId[m.userId] = m.username; });
      names = effectiveExcept().map((id) => byId[id] || '?');
    } catch (e) {}
    if (!names.length) { box.innerHTML = ''; return; }
    box.innerHTML = '<div class="tb-restr">🔕 Envoi restreint — ne recevront pas : ' + esc(names.join(', ')) +
      ' <button class="tb-btn" id="tbUnmuteAll" type="button" title="Réouvrir l’envoi à tout le monde">↺ Tout le monde</button></div>';
    const b = $('tbUnmuteAll');
    if (b) b.addEventListener('click', resetSend);
  }
  async function loadFriends() {
    try {
      const r = await api('/api/friends', { cache: 'no-store' });
      if (!r.ok) throw new Error();
      friends = await r.json();
    } catch (e) { friends = { friends: [], incoming: [], outgoing: [] }; }
    renderFriends();
    renderOnline();
  }
  function renderFriends() {
    const box = $('tbFriends');
    if (!box) return;
    let h = '<div class="tb-field"><div style="display:flex;gap:6px;">' +
      '<input id="tbFriendAdd" placeholder="Pseudo ou e-mail">' +
      '<button class="tb-btn" id="tbFriendAddBtn" type="button">＋</button></div></div>';
    if ((friends.incoming || []).length) {
      h += '<div class="tb-field"><label>Demandes reçues</label>';
      friends.incoming.forEach((f) => {
        h += '<div class="tb-member"><span>' + avaImg(f.avatar) + esc(f.username) + '</span>' +
          '<button class="tb-btn" data-acc="' + esc(f.userId) + '" type="button">OK</button>' +
          '<button class="kick" data-dec="' + esc(f.userId) + '" type="button" title="Refuser">✕</button></div>';
      });
      h += '</div>';
    }
    if ((friends.friends || []).length) {
      h += '<div class="tb-field"><label>Amis (' + friends.friends.length + ')</label>';
      friends.friends.forEach((f) => {
        h += '<div class="tb-member"><span>' + esc(f.username) + '</span>';
        if (activeDoc && ['mj', 'helper', 'admin'].includes(activeDoc.myRole)) {
          h += '<button class="tb-btn" data-tinv="' + esc(f.username) + '" type="button" title="Inviter sur la table active">→ table</button>';
        }
        h += '<button class="kick" data-rm="' + esc(f.userId) + '" type="button" title="Retirer">✕</button></div>';
      });
      h += '</div>';
    } else if (!(friends.incoming || []).length) {
      h += '<p class="tb-status">Aucun ami pour l’instant.</p>';
    }
    if ((friends.outgoing || []).length) {
      h += '<div class="tb-field"><label>Envoyées</label>';
      friends.outgoing.forEach((f) => {
        h += '<div class="tb-member"><span>' + avaImg(f.avatar) + esc(f.username) + '</span>' +
          '<button class="kick" data-can="' + esc(f.userId) + '" type="button" title="Annuler">✕</button></div>';
      });
      h += '</div>';
    }
    h += '<div class="tb-status" id="tbFriendStatus"></div>';
    box.innerHTML = h;
    $('tbFriendAddBtn').addEventListener('click', friendRequest);
    $('tbFriendAdd').addEventListener('keydown', (e) => { if (e.key === 'Enter') friendRequest(); });
    box.querySelectorAll('[data-acc]').forEach((b) => b.addEventListener('click', () => friendAct('accept', b.getAttribute('data-acc'))));
    box.querySelectorAll('[data-dec]').forEach((b) => b.addEventListener('click', () => friendAct('decline', b.getAttribute('data-dec'))));
    box.querySelectorAll('[data-can]').forEach((b) => b.addEventListener('click', () => friendAct('cancel', b.getAttribute('data-can'))));
    box.querySelectorAll('[data-rm]').forEach((b) => b.addEventListener('click', () => friendRemove(b.getAttribute('data-rm'))));
    box.querySelectorAll('[data-tinv]').forEach((b) => b.addEventListener('click', () => inviteFriendToTable(b.getAttribute('data-tinv'))));
  }
  async function friendRequest() {
    const v = ($('tbFriendAdd') || {}).value || '';
    if (!v.trim()) return;
    try {
      const r = await api('/api/friends/request', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ user: v.trim() })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      status('tbFriendStatus', d.linked ? 'Ami ajouté ✓' : 'Demande envoyée ✓', 'ok');
      await loadFriends();
    } catch (e) { status('tbFriendStatus', e.message, 'err'); }
  }
  async function friendAct(action, userId) {
    try {
      const r = await api('/api/friends/' + action, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userId })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      await loadFriends();
    } catch (e) { status('tbFriendStatus', e.message, 'err'); }
  }
  async function friendRemove(userId) {
    if (!confirm('Retirer cet ami ?')) return;
    try {
      const r = await api('/api/friends/' + encodeURIComponent(userId), { method: 'DELETE' });
      if (!r.ok) throw new Error((await r.json().catch(() => ({}))).error || ('Erreur ' + r.status));
      await loadFriends();
    } catch (e) { status('tbFriendStatus', e.message, 'err'); }
  }
  async function inviteFriendToTable(username) {
    if (!activeId) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/members', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ user: username })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      status('tbManageStatus', username + ' invité ✓', 'ok');
      await fetchTable(); await loadTables();
    } catch (e) { status('tbManageStatus', e.message, 'err'); }
  }

  /* ---------- en ligne (LAN) ---------- */
  function isFriend(uid) {
    return (friends.friends || []).some((f) => f.userId === uid);
  }
  async function loadOnline() {
    try {
      const r = await api('/api/presence', { cache: 'no-store' });
      if (!r.ok) throw new Error();
      const d = await r.json();
      online = (d.online || []).filter((o) => o.userId !== (me && me.id));
    } catch (e) { online = []; }
    renderOnline();
  }
  function renderOnline() {
    const box = $('tbOnline');
    if (!box) return;
    const st = $('tbOnlineStatus');
    if (!online.length) {
      box.innerHTML = '';
      if (st) st.textContent = 'Personne d’autre en ligne pour l’instant.';
      return;
    }
    if (st) st.textContent = online.length + ' en ligne.';
    let h = '';
    online.forEach((o) => {
      h += '<div class="tb-member"><span>🟢 ' + avaImg(o.avatar) + esc(o.username) + '</span>';
      if (!isFriend(o.userId)) {
        h += '<button class="tb-btn" data-oadd="' + esc(o.username) + '" type="button" title="Ajouter en ami">＋ ami</button>';
      }
      if (activeDoc && ['mj', 'helper', 'admin'].includes(activeDoc.myRole)) {
        h += '<button class="tb-btn" data-otinv="' + esc(o.username) + '" type="button" title="Inviter sur la table active">→ table</button>';
      }
      h += '</div>';
    });
    box.innerHTML = h;
    box.querySelectorAll('[data-oadd]').forEach((b) => b.addEventListener('click', async () => {
      const u = b.getAttribute('data-oadd');
      try {
        const r = await api('/api/friends/request', {
          method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ user: u })
        });
        const d = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
        status('tbFriendStatus', 'Demande à ' + u + ' ✓', 'ok');
        await loadFriends();
      } catch (e) { status('tbFriendStatus', e.message, 'err'); }
    }));
    box.querySelectorAll('[data-otinv]').forEach((b) => b.addEventListener('click', () => inviteFriendToTable(b.getAttribute('data-otinv'))));
  }

  async function init() {
    const ok = await refreshMe();
    $('tbWall').style.display = ok ? 'none' : '';
    $('tbApp').style.display = ok ? '' : 'none';
    if (!ok) return;
    $('tbCreate').addEventListener('click', async () => {
      const name = $('tbNewName').value.trim();
      const desc = $('tbNewDesc').value.trim();
      const password = ($('tbNewPass') || {}).value ? $('tbNewPass').value.slice(0, 64) : '';
      const listed = !$('tbNewListed') || $('tbNewListed').checked;
      if (!name) { status('tbListStatus', 'Nom requis.', 'err'); return; }
      try {
        const r = await api('/api/tables', {
          method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name, desc, password, listed })
        });
        const d = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
        $('tbNewName').value = ''; $('tbNewDesc').value = '';
        if ($('tbNewPass')) $('tbNewPass').value = '';
        await loadTables();
        await loadDirectory();
        openTable(d.table.id);
      } catch (e) { status('tbListStatus', e.message, 'err'); }
    });
    $('tbSend').addEventListener('click', sendMessage);
    $('tbText').addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(); }
    });
    $('tbFile').addEventListener('change', (e) => handleFile(e.target.files[0]));
    renderAvatar();
    await loadTables();
    await loadDirectory();
    await loadFriends();
    await loadOnline();
    clearInterval(onlineTimer);
    onlineTimer = setInterval(loadOnline, 30000);
    // Liens profonds : ?invite=CODE (rejoindre) ou ?table=ID (ouvrir).
    // Sinon on rouvre la table mémorisée (sélection persistante).
    try {
      const q = new URLSearchParams(window.location.search);
      const code = (q.get('invite') || '').trim();
      const tid = (q.get('table') || '').trim();
      let stored = null;
      try { stored = localStorage.getItem('kt-tables-active'); } catch (e) {}
      if (code) joinWithCode(code);
      else if (tid && tables.some((t) => t.id === tid)) openTable(tid);
      else if (stored && tables.some((t) => t.id === stored)) openTable(stored);
      else if (stored) { try { localStorage.removeItem('kt-tables-active'); } catch (e) {} }
    } catch (e) {}
  }

  document.addEventListener('DOMContentLoaded', () => {
    init();
    const lb = $('tbLoginBtn');
    if (lb) lb.addEventListener('click', () => {
      if (window.CodexAuth) window.CodexAuth.openModal();
    });
  });
  window.addEventListener('codex:auth:login', () => location.reload());
  window.addEventListener('codex:auth:logout', () => location.reload());
})();
