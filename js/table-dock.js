/* Table en cours — panneau latéral sur tout le site (sauf tables.html).
   Chat de la table active (localStorage kt-tables-active) : lecture, envoi
   texte + fichiers, liste des joueurs + mutes personnels, envoi restreint
   + reset, présence d'upload.
   MPs/gestion -> page Tables.
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
  let pendingDockImage = null;
  let pendingDockFile = null;
  let pendingDockFileName = '';
  let pendingDockFileKind = 'fichier';

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

  function isHidden(uid) {
    try { const t = getHide()[activeId]; return !!(t && t[uid] !== undefined); }
    catch (e) { return false; }
  }
  function memberById(id) {
    try { return (activeDoc.members || []).find((m) => m && m.userId === id) || null; }
    catch (e) { return null; }
  }
  // Couper / rétablir les échanges avec un joueur (perso, futurs messages ;
  // l'historique reste visible). Même format que js/tables.js.
  function toggleHide(uid) {
    if (!activeId || !uid || (me && uid === me.id)) return;
    const h = getHide();
    const tm = h[activeId] || (h[activeId] = {});
    const muting = tm[uid] === undefined;
    if (muting) tm[uid] = Date.now(); else delete tm[uid];
    if (Object.keys(tm).length === 0) delete h[activeId];
    try { localStorage.setItem('kt-tables-hide', JSON.stringify(h)); } catch (e) {}
    const tgt = memberById(uid);
    setStatus(muting
      ? ('🔕 Échanges coupés avec ' + (tgt ? tgt.username : '?') + ' (futurs messages).')
      : '🔔 Échanges rétablis.', muting ? '' : 'ok');
    render(true);
  }
  // Reset total : héritage serveur + masqués perso de cette table.
  async function resetSend() {
    if (!activeId) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/send-reset', { method: 'POST' });
      if (!r.ok && r.status !== 404) {
        const d = await r.json().catch(() => ({}));
        throw new Error(d.error || ('Erreur ' + r.status));
      }
    } catch (e) { setStatus(e.message, 'err'); return; }
    try {
      const hm = getHide();
      if (hm[activeId]) delete hm[activeId];
      localStorage.setItem('kt-tables-hide', JSON.stringify(hm));
    } catch (e) {}
    setStatus('Envoi à nouveau ouvert à tout le monde ✓', 'ok');
    await fetchTable();
  }
  function exceptNames() {
    const names = [];
    try {
      const byId = {};
      (activeDoc.members || []).forEach((m) => { if (m) byId[m.userId] = m.username; });
      effectiveExcept().forEach((id) => names.push(byId[id] || '?'));
    } catch (e) {}
    return names;
  }

  /* ---------- pièce jointe PDF/audio (base64 direct, plafond ~10 Mo) ---------- */
  // Extensions audio courantes -> MIME (quand le type du File est vide).
  const DOCK_AUDIO_EXT = { mp3: 'audio/mpeg', wav: 'audio/wav', wave: 'audio/wav', ogg: 'audio/ogg', oga: 'audio/ogg', opus: 'audio/opus', m4a: 'audio/mp4', aac: 'audio/aac', flac: 'audio/flac', aif: 'audio/aiff', aiff: 'audio/aiff' };
  const DOCK_VIDEO_EXT = { mp4: 'video/mp4', webm: 'video/webm', ogv: 'video/ogg', mov: 'video/quicktime', mkv: 'video/x-matroska', avi: 'video/x-msvideo', m4v: 'video/x-m4v' };
  function dockMimeOf(file) {
    const t = (file && file.type) || '';
    if (t === 'application/ogg') return 'audio/ogg'; // normalisé pour le lecteur
    if (t === 'application/pdf' || t.indexOf('audio/') === 0 || t.indexOf('video/') === 0) return t;
    const m = /\.([a-z0-9]+)$/i.exec((file && file.name) || '');
    if (!m) return null;
    const ext = m[1].toLowerCase();
    if (DOCK_AUDIO_EXT[ext]) return DOCK_AUDIO_EXT[ext];
    if (DOCK_VIDEO_EXT[ext]) return DOCK_VIDEO_EXT[ext];
    if (ext === 'pdf') return 'application/pdf';
    return null;
  }
  function dockKindLabel(mime) {
    if (mime === 'application/pdf') return 'PDF';
    if ((mime || '').indexOf('video/') === 0) return 'Vidéo';
    return 'Audio';
  }
  function fileDockToDataUrl(file, cb) {
    try {
      if (!file) { cb(null, 'Aucun fichier.'); return; }
      const name = String(file.name || 'document').slice(0, 120);
      const mime = dockMimeOf(file) || 'application/pdf';
      if (file.size > 10485760) { cb(null, 'Fichier trop lourd (max ~10 Mo).'); return; }
      const rd = new FileReader();
      rd.onload = () => {
        try {
          const bytes = new Uint8Array(rd.result);
          let bin = '';
          const CH = 8192;
          for (let i = 0; i < bytes.length; i += CH) {
            bin += String.fromCharCode.apply(null, bytes.subarray(i, i + CH));
          }
          const dataUrl = 'data:' + mime + ';base64,' + btoa(bin);
          if (dataUrl.length > 14000000) { cb(null, 'Fichier trop lourd (max ~10 Mo).'); return; }
          cb({ name, dataUrl, mime }, null);
        } catch (err) { cb(null, 'Lecture fichier impossible.'); }
      };
      rd.onerror = () => cb(null, 'Lecture fichier impossible.');
      rd.readAsArrayBuffer(file);
    } catch (err) { cb(null, 'Envoi fichier indisponible ici.'); }
  }
  function isDockDoc(file) {
    return !!dockMimeOf(file);
  }
  // Pièces jointes paresseuses : cache mémoire + fetch binaire (cache HTTP
  // navigateur via l'en-tête immutable du serveur).
  const dockAttachCache = new Map(); // mid -> blobURL
  function dockCacheAttach(mid, url) {
    if (!mid || !url) return;
    dockAttachCache.set(mid, url);
    if (dockAttachCache.size > 100) dockAttachCache.delete(dockAttachCache.keys().next().value);
  }
  function dockDataUrlToBlob(dataUrl) {
    const bin = atob(dataUrl.split(',')[1]);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    const m = /^data:([^;,]+)/.exec(dataUrl || '');
    return new Blob([bytes], { type: m ? m[1] : 'application/octet-stream' });
  }
  function dockDataUrlToBlobUrl(dataUrl) {
    return URL.createObjectURL(dockDataUrlToBlob(dataUrl));
  }
  async function fetchDockAttachment(mid) {
    if (dockAttachCache.has(mid)) return dockAttachCache.get(mid);
    const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/messages/' + encodeURIComponent(mid) + '/attachment', { cache: 'force-cache' });
    if (!r.ok) throw new Error('Pièce jointe indisponible (' + r.status + ')');
    const blob = await r.blob();
    const url = URL.createObjectURL(blob);
    dockCacheAttach(mid, url);
    storeDockAttach(mid, blob);
    return url;
  }
  // Cache persistant (Cache Storage API) : clé synthétique stable par
  // table+message, contenu immuable. Vérifiable sans réseau.
  const DOCK_ATT_CACHE_NAME = 'kt-attachments-v1';
  function dockAttachKey(mid) {
    return 'https://kt-attachments.local/' + encodeURIComponent(activeId || '') + '/' + encodeURIComponent(mid);
  }
  async function storeDockAttach(mid, blob) {
    try {
      if (!('caches' in window) || !blob) return;
      const c = await caches.open(DOCK_ATT_CACHE_NAME);
      try {
        const keys = await c.keys();
        if (keys.length > 100) {
          for (const k of keys.slice(0, keys.length - 100)) { try { await c.delete(k); } catch (e) {} }
        }
      } catch (e) {}
      await c.put(dockAttachKey(mid), new Response(blob, { headers: { 'Content-Type': blob.type || 'application/octet-stream' } }));
    } catch (e) {}
  }
  async function cachedDockAttachUrl(mid) {
    try {
      if (dockAttachCache.has(mid)) return dockAttachCache.get(mid);
      if (!('caches' in window)) return null;
      const c = await caches.open(DOCK_ATT_CACHE_NAME);
      const res = await c.match(dockAttachKey(mid));
      if (!res) return null;
      const url = URL.createObjectURL(await res.blob());
      dockCacheAttach(mid, url);
      return url;
    } catch (e) { return null; }
  }
  function dockAttachSize(f) {
    return (f && f.bytes) ? ' (' + Math.max(1, Math.round(f.bytes / 1024)) + ' Ko)' : '';
  }
  function dockAttachHtml(m) {
    const f = m && ((m.image && m.image.kind === 'upload') ? m.image : m.file);
    if (!f || f.kind !== 'upload') return '';
    const mime = f.mime || '';
    const cached = dockAttachCache.get(m.id);
    const isImg = mime.indexOf('image/') === 0;
    const isAudio = mime.indexOf('audio/') === 0;
    const isVideo = mime.indexOf('video/') === 0;
    const isPdf = mime === 'application/pdf';
    if (cached && (isImg || isAudio || isVideo)) {
      if (isImg) return '<img src="' + cached + '" loading="lazy" alt="image partagée">';
      if (isAudio) return '<audio class="kd-audio" controls preload="metadata" src="' + cached + '"></audio>';
      return '<video class="kd-video" controls preload="metadata" src="' + cached + '"></video>';
    }
    const icon = isImg ? '🖼' : (isAudio ? '🎵' : (isVideo ? '🎬' : '📄'));
    const name = f.name || (isImg ? 'image partagée' : 'document');
    const action = isPdf ? 'Ouvrir' : 'Charger';
    return '<button class="kd-attach" data-load="' + esc(m.id) + '" type="button" title="' + action + ' la pièce jointe">' +
      icon + ' ' + esc(name) + dockAttachSize(f) + ' — ' + action.toLowerCase() + '</button>';
  }
  // Remplace le bouton « charger » par le média inline. false si ignoré.
  function swapDockBtnForMedia(btn, f, url) {
    if (!btn || !btn.parentElement) return false;
    const mime = (f && f.mime) || '';
    const isImg = mime.indexOf('image/') === 0;
    const isAudio = mime.indexOf('audio/') === 0;
    const isVideo = mime.indexOf('video/') === 0;
    if (!(isImg || isAudio || isVideo)) return false;
    let el = null;
    if (isImg) {
      el = document.createElement('img');
      el.src = url; el.loading = 'lazy'; el.alt = 'image partagée';
    } else {
      el = document.createElement(isAudio ? 'audio' : 'video');
      el.src = url; el.controls = true; el.preload = 'metadata';
      el.className = isAudio ? 'kd-audio' : 'kd-video';
    }
    btn.parentElement.replaceChild(el, btn);
    return true;
  }
  async function loadDockAttachment(mid, btn) {
    const m = activeDoc && (activeDoc.messages || []).find((x) => x && x.id === mid);
    const f = m && ((m.image && m.image.kind === 'upload') ? m.image : m.file);
    if (!m || !f) return;
    const orig = btn ? btn.innerHTML : '';
    try {
      if (btn) { btn.disabled = true; btn.textContent = 'Chargement…'; }
      const url = await fetchDockAttachment(mid);
      const mime = f.mime || '';
      if (mime === 'application/pdf') {
        window.open(url, '_blank', 'noopener');
        if (btn) { btn.disabled = false; btn.innerHTML = orig; }
        return;
      }
      if (!swapDockBtnForMedia(btn, f, url) && btn) {
        btn.disabled = false; btn.innerHTML = orig;
      }
    } catch (e) {
      if (btn) { btn.disabled = false; btn.innerHTML = orig; }
      setStatus('Pièce jointe : ' + e.message, 'err');
    }
  }
  // Déjà en cache (mémoire ou persistant) : afficher sans attendre un clic.
  const dockAttachChecked = new Set();
  function autoShowDockCached() {
    if (!activeDoc) return;
    (activeDoc.messages || []).forEach((m) => {
      if (!m || dockAttachChecked.has(m.id)) return;
      const f = (m.image && m.image.kind === 'upload') ? m.image : m.file;
      if (!f || f.kind !== 'upload' || (f.mime || '') === 'application/pdf') return;
      dockAttachChecked.add(m.id);
      if (dockAttachCache.has(m.id)) return; // déjà rendu en média
      cachedDockAttachUrl(m.id).then((url) => {
        if (!url || !activeDoc) return;
        if (!activeDoc.messages.some((x) => x && x.id === m.id)) return;
        let btn = null;
        try { btn = document.querySelector('[data-load="' + m.id + '"]'); } catch (e) {}
        if (btn) swapDockBtnForMedia(btn, f, url);
      });
    });
  }

  /* ---------- GIF/APNG : lus en brut pour conserver l'animation ---------- */
  function readRawDockImage(file, cb) {
    try {
      if (!file) { cb(null, 'Aucun fichier.'); return; }
      if (file.size > 10485760) { cb(null, 'Image trop lourde (max ~10 Mo).'); return; }
      const rd = new FileReader();
      rd.onload = () => {
        try {
          let dataUrl = String(rd.result || '');
          const lowName = String(file.name || '').toLowerCase();
          const mExt = /\.gif$/.test(lowName) ? 'gif' : (/\.apng$/.test(lowName) ? 'apng' : null);
          if (/^data:application\/octet-stream;base64,/.test(dataUrl) && mExt) {
            dataUrl = 'data:image/' + mExt + ';base64,' + dataUrl.split(',')[1];
          }
          if (!/^data:image\/(gif|apng);base64,/.test(dataUrl)) { cb(null, 'Lecture image impossible.'); return; }
          if (dataUrl.length > 14000000) { cb(null, 'Image trop lourde (max ~10 Mo).'); return; }
          cb(dataUrl, null);
        } catch (err) { cb(null, 'Lecture image impossible.'); }
      };
      rd.onerror = () => cb(null, 'Lecture image impossible.');
      rd.readAsDataURL(file);
    } catch (err) { cb(null, 'Envoi image indisponible ici.'); }
  }
  /* ---------- image par glisser-déposer (downscale 1024 px, plafond) ---------- */
  function downscaleDockImage(file, cb) {
    try {
      if (!file || !/^image\/(png|jpe?g|gif|webp|apng)$/.test(file.type || '')) {
        cb(null, 'Format image non pris en charge.');
        return;
      }
      if (file.type === 'image/gif' || file.type === 'image/apng') {
        // Animé : pas de canvas (ne garderait que la 1re frame).
        readRawDockImage(file, cb);
        return;
      }
      const img = new Image();
      const url = URL.createObjectURL(file);
      img.onload = () => {
        try {
          let w = img.naturalWidth, h = img.naturalHeight;
          const k = Math.min(1, 1024 / Math.max(w, h));
          w = Math.max(1, Math.round(w * k));
          h = Math.max(1, Math.round(h * k));
          const cv = document.createElement('canvas');
          cv.width = w; cv.height = h;
          cv.getContext('2d').drawImage(img, 0, 0, w, h);
          URL.revokeObjectURL(url);
          cb(cv.toDataURL(file.type === 'image/png' ? 'image/png' : 'image/jpeg', 0.85), null);
        } catch (err) { URL.revokeObjectURL(url); cb(null, 'Lecture image impossible.'); }
      };
      img.onerror = () => { URL.revokeObjectURL(url); cb(null, 'Lecture image impossible.'); };
      img.src = url;
    } catch (err) { cb(null, 'Envoi image indisponible ici.'); }
  }
  function handleDockFile(file) {
    pendingDockImage = null;
    pendingDockFile = null;
    pendingDockFileName = '';
    pendingDockFileKind = 'fichier';
    if (!file) return;
    if (isDockDoc(file)) {
      fileDockToDataUrl(file, (doc, err) => {
        if (err || !doc) { setStatus(err || 'Lecture fichier impossible.', 'err'); return; }
        pendingDockFile = doc.dataUrl;
        pendingDockFileName = doc.name;
        pendingDockFileKind = (doc.mime || '').indexOf('video/') === 0 ? 'video' : 'fichier';
        setStatus(dockKindLabel(doc.mime) + ' prêt (' + Math.round(doc.dataUrl.length / 1024) + ' Ko) — Envoyer pour l’envoyer.', 'ok');
      });
      return;
    }
    downscaleDockImage(file, (dataUrl, err) => {
      if (err || !dataUrl) {
        // Dernier recours : GIF/APNG à type vide (extension seule).
        if (/\.gif$/i.test(file.name || '') || /\.apng$/i.test(file.name || '')) {
          readRawDockImage(file, (raw, err2) => {
            if (err2 || !raw) { setStatus(err2 || 'Lecture image impossible.', 'err'); return; }
            pendingDockImage = raw;
            pendingDockFileKind = 'image';
            setStatus('GIF prêt (' + Math.round(raw.length / 1024) + ' Ko, animation conservée) — Envoyer pour l’envoyer.', 'ok');
          });
          return;
        }
        setStatus(err || 'Lecture image impossible.', 'err');
        return;
      }
      if (dataUrl.length > 14000000) { setStatus('Image trop lourde même réduite.', 'err'); return; }
      pendingDockImage = dataUrl;
      pendingDockFileKind = 'image';
      const anim = /^data:image\/(gif|apng);base64,/.test(dataUrl) ? ', animation conservée' : '';
      setStatus('Image prête (' + Math.round(dataUrl.length / 1024) + ' Ko' + anim + ') — Envoyer pour l’envoyer.', 'ok');
    });
  }
  /* ---------- présence d'upload (qui envoie un fichier) ---------- */
  async function notifyDockUpload(state, kind) {
    if (!activeId) return;
    try {
      await api('/api/tables/' + encodeURIComponent(activeId) + '/upload-state', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ state, kind: kind || 'fichier' })
      });
    } catch (e) {}
  }
  async function fetchDockUploads() {
    if (!activeId) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/uploads', { cache: 'no-store' });
      if (!r.ok) return;
      renderDockUploads(((await r.json()).uploads) || []);
    } catch (e) {}
  }
  function dockUploadKindLabel(kind) {
    if (kind === 'image') return 'une image';
    if (kind === 'video') return 'une vidéo';
    return 'un fichier';
  }
  // Visibilité minimale : une fois aperçue, l'anim reste affichée au moins
  // 5 s (un transfert court serait sinon invisible entre deux polls).
  let dockUploadHold = {};
  const DOCK_UPLOAD_MIN_VISIBLE_MS = 5000;
  function renderDockUploads(list) {
    const box = $('kdUploadState');
    if (!box) return;
    const now = Date.now();
    const live = {};
    (list || []).forEach((u) => {
      if (!u || !u.userId) return;
      live[u.userId] = true;
      if (!dockUploadHold[u.userId]) dockUploadHold[u.userId] = { until: now + DOCK_UPLOAD_MIN_VISIBLE_MS, u };
      else dockUploadHold[u.userId].u = u;
    });
    Object.keys(dockUploadHold).forEach((uid) => {
      if (!live[uid] && dockUploadHold[uid].until <= now) delete dockUploadHold[uid];
    });
    const rows = Object.keys(dockUploadHold).map((uid) => dockUploadHold[uid].u);
    if (!rows.length) { box.innerHTML = ''; box.style.display = 'none'; return; }
    box.style.display = '';
    box.innerHTML = rows.map((u) => {
      const mine = u.userId === (me && me.id);
      const label = mine ? '📤 Envoi en cours' : '📤 ' + esc(u.username) + ' envoie';
      return '<span class="kd-uploading">' + label + ' ' + dockUploadKindLabel(u.kind) +
        '<span class="kd-dots"><span>.</span><span>.</span><span>.</span></span></span>';
    }).join(' ');
  }
  function bindDockDrop(el) {
    if (!el || !el.addEventListener) return;
    el.addEventListener('dragover', (e) => {
      e.preventDefault();
      el.classList.add('kd-dragover');
    });
    el.addEventListener('dragleave', (e) => {
      if (e.preventDefault) e.preventDefault();
      el.classList.remove('kd-dragover');
    });
    el.addEventListener('drop', (e) => {
      if (e.preventDefault) e.preventDefault();
      el.classList.remove('kd-dragover');
      const f = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
      if (!activeDoc) { setStatus('Choisis une table d’abord.', 'err'); return; }
      if (f) handleDockFile(f);
      else setStatus('Aucun fichier déposé.', 'err');
    });
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
  function avaOk(u) {
    return typeof u === 'string' && (/^https?:\/\//.test(u) || /^data:image\//.test(u));
  }
  function avaHtml(avatar, name) {
    if (avaOk(avatar)) return '<img class="kd-ava" src="' + esc(avatar) + '" alt="" loading="lazy">';
    return '<span class="kd-ava none">' + esc((name || '?').charAt(0).toUpperCase()) + '</span>';
  }
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
    body += dockAttachHtml(m);
    return '<div class="h"><span class="w">' + avaHtml(m.fromAvatar, m.fromName) + esc(m.fromName) + '</span>' + priv +
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
    box.querySelectorAll('[data-load]').forEach((btn) => {
      btn.addEventListener('click', () => loadDockAttachment(btn.getAttribute('data-load'), btn));
    });
    autoShowDockCached();
    if (stick || box.scrollHeight - box.scrollTop < 600) stickDockBottom(box);
    renderMembers();
    renderScope();
    updateFab();
  }
  function stickDockBottom(box) {
    try { box.scrollTop = box.scrollHeight; box._stuck = true; } catch (e) {}
  }

  let membersOpen = false;
  function dockMembersSig(members) {
    try {
      return (members || []).map((m) => [m && m.userId, m && m.username, m && m.role, m && m.avatar].join('|')).sort().join(';');
    } catch (e) { return ''; }
  }
  function renderMembers() {
    const toggle = $('kdMToggle');
    const list = $('kdMembers');
    if (!toggle || !list) return;
    const members = (activeDoc && activeDoc.members) || [];
    toggle.textContent = '👥 Joueurs (' + members.length + ')' + (membersOpen ? ' ▾' : ' ▸');
    list.style.display = membersOpen ? '' : 'none';
    if (!membersOpen) return;
    list.innerHTML = '';
    members.forEach((mb) => {
      if (!mb) return;
      const isMe = me && mb.userId === me.id;
      const hidden = !isMe && isHidden(mb.userId);
      const row = document.createElement('div');
      row.className = 'kd-mrow';
      row.innerHTML = avaHtml(mb.avatar, mb.username) +
        '<span class="kd-mname">' + esc(mb.username) + (isMe ? ' (toi)' : '') + '</span>' +
        '<span class="kd-role">' + esc(mb.role || 'joueur') + '</span>' +
        (hidden ? '<span class="kd-hidden-tag">masqué pour toi</span>' : '');
      if (!isMe) {
        const b = document.createElement('button');
        b.type = 'button';
        b.className = 'kd-mute' + (hidden ? ' off' : '');
        b.title = hidden ? 'Rétablir les échanges' : 'Ne plus échanger (tes futurs messages ne lui seront plus envoyés, et réciproquement)';
        b.textContent = hidden ? '👁️' : '👁️‍🗨️';
        b.addEventListener('click', () => toggleHide(mb.userId));
        row.appendChild(b);
      }
      list.appendChild(row);
    });
    if (!members.length) list.innerHTML = '<p class="kd-empty">Aucun membre.</p>';
  }

  function renderScope() {
    const box = $('kdScope');
    if (!box || !activeDoc) { if (box) box.innerHTML = ''; return; }
    const names = exceptNames();
    if (!names.length) { box.innerHTML = ''; return; }
    box.innerHTML = '';
    const d = document.createElement('div');
    d.className = 'kd-scope';
    d.innerHTML = '<span>🔕 Ne recevront pas : ' + esc(names.join(', ')) + '</span> ';
    const b = document.createElement('button');
    b.type = 'button';
    b.title = 'Réouvrir l’envoi à tout le monde';
    b.textContent = '↺ Tout le monde';
    b.addEventListener('click', resetSend);
    d.appendChild(b);
    box.appendChild(d);
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
    if (activeId && activeId !== id && (pendingDockImage || pendingDockFile)) {
      await notifyDockUpload('done');
    }
    activeId = id;
    dockAttachChecked.clear();
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
      fetchDockUploads();
      setStatus('À jour ✓', 'ok');
    } catch (e) {
      setStatus(e.message, 'err');
    }
  }

  async function fetchQuiet() {
    if (!activeId || !isOpen() || document.visibilityState === 'hidden') return;
    fetchDockUploads();
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/messages?since=' + lastTs, { cache: 'no-store' });
      if (!r.ok) return;
      const d = await r.json();
      if (!activeDoc) return;
      if (Array.isArray(d.myInherited)) activeDoc.myInherited = d.myInherited;
      // Membres frais (arrivées/départs/rôles/avatars) sans rouvrir.
      if (Array.isArray(d.members) && dockMembersSig(d.members) !== dockMembersSig(activeDoc.members)) {
        activeDoc.members = d.members;
        renderMembers();
        renderScope();
      }
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
    if (!text && !pendingDockImage && !pendingDockFile) { setStatus('Message vide (texte, image, PDF ou audio déposé).', 'err'); return; }
    const btn = $('kdSend');
    if (btn) btn.disabled = true;
    // Fichier joint : on signale le début du transfert (visible par tous
    // pendant le POST) + affichage optimiste immédiat pour soi.
    const sendingFile = !!(pendingDockImage || pendingDockFile);
    if (sendingFile && me) {
      renderDockUploads([{ userId: me.id, username: me.username, kind: pendingDockFileKind, startedAt: Date.now() }]);
      await notifyDockUpload('start', pendingDockFileKind);
    }
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/messages', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text, imageData: pendingDockImage, fileData: pendingDockFile, fileName: pendingDockFileName, toExcept: effectiveExcept() })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      activeDoc.messages.push(d.message);
      if (d.message.createdAt > lastTs) lastTs = d.message.createdAt;
      // L'envoyeur revoit son média sans retélécharger (cache local).
      try {
        if (pendingDockImage) { dockCacheAttach(d.message.id, dockDataUrlToBlobUrl(pendingDockImage)); storeDockAttach(d.message.id, dockDataUrlToBlob(pendingDockImage)); }
        if (pendingDockFile) { dockCacheAttach(d.message.id, dockDataUrlToBlobUrl(pendingDockFile)); storeDockAttach(d.message.id, dockDataUrlToBlob(pendingDockFile)); }
      } catch (e) {}
      ta.value = '';
      pendingDockImage = null;
      pendingDockFile = null;
      pendingDockFileName = '';
      notifyDockUpload('done');
      render(true);
      setStatus('Envoyé ✓', 'ok');
    } catch (e) {
      if (sendingFile) notifyDockUpload('done');
      setStatus(e.message, 'err');
    }
    if (btn) btn.disabled = false;
  }

  /* ---------- partage sélection Codex → chat (table en cours) ----------
     Sélection dans la page (codex RP) → bouton flottant « Envoyer au chat ».
     Envoie le texte cité vers POST /messages de la table active (même
     endpoint que le dock), avec source (titre + page). Respecte l'envoi
     restreint (toExcept) comme le composer. */
  const SHARE_MIN_LEN = 4;
  const SHARE_MAX_TEXT = 2000;
  function shareBtn() {
    let b = $('kdShareSel');
    if (b) return b;
    b = document.createElement('button');
    b.id = 'kdShareSel';
    b.type = 'button';
    b.title = 'Envoyer la sélection au chat de la table en cours';
    b.textContent = '💬 Envoyer au chat';
    b.style.display = 'none';
    b.addEventListener('click', (e) => {
      if (e.preventDefault) e.preventDefault();
      if (e.stopPropagation) e.stopPropagation();
      sendShare();
    });
    document.body.appendChild(b);
    return b;
  }
  function shareSelText() {
    try {
      const s = window.getSelection();
      if (!s || s.isCollapsed) return '';
      const t = String(s.toString() || '').replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, '').trim();
      return t;
    } catch (e) { return ''; }
  }
  function shareSelOk() {
    try {
      const s = window.getSelection();
      if (!s || s.isCollapsed || !s.anchorNode) return false;
      let n = s.anchorNode;
      if (n && n.nodeType === 3) n = n.parentElement;
      if (!n || !n.closest) return true;
      if (n.closest('#ktDock, #ktDockFab, #kdShareSel, input, textarea, select, [contenteditable="true"]')) return false;
      return true;
    } catch (e) { return true; }
  }
  function hideShare() {
    const b = $('kdShareSel');
    if (b) b.style.display = 'none';
  }
  function updateShare() {
    const b = shareBtn();
    const t = shareSelOk() ? shareSelText() : '';
    if (!t || t.length < SHARE_MIN_LEN) { b.style.display = 'none'; return; }
    let rect = null;
    try {
      const s = window.getSelection();
      if (s && s.rangeCount) rect = s.getRangeAt(0).getBoundingClientRect();
    } catch (e) {}
    b.style.display = '';
    if (rect && rect.width + rect.height > 0) {
      const vw = window.innerWidth || 800;
      const top = Math.max(8, Math.min(window.innerHeight - 48, rect.top - 44));
      const left = Math.max(8, Math.min(vw - 190, rect.left + rect.width / 2 - 90));
      b.style.top = top + 'px';
      b.style.left = left + 'px';
    } else {
      b.style.top = '';
      b.style.left = '';
      b.classList.add('kd-share-fallback');
    }
  }
  let shareDeb = null;
  function scheduleShare() {
    if (shareDeb) clearTimeout(shareDeb);
    shareDeb = setTimeout(updateShare, 120);
  }
  function buildShareText(sel) {
    let title = '';
    try { title = String(document.title || '').trim().slice(0, 90); } catch (e) {}
    let ref = '';
    try {
      const page = String(window.location.pathname || '').split('/').pop() || 'codex';
      ref = page + (window.location.hash || '');
    } catch (e) {}
    const head = '📖 ' + (title ? title + ' — ' : '') + ref;
    const maxSel = Math.max(200, SHARE_MAX_TEXT - head.length - 12);
    let q = String(sel || '').trim().replace(/\n{3,}/g, '\n\n');
    if (q.length > maxSel) q = q.slice(0, maxSel - 1).trimEnd() + '…';
    return (head + '\n« ' + q + ' »').slice(0, SHARE_MAX_TEXT);
  }
  async function sendShare() {
    const b = $('kdShareSel');
    const sel = shareSelText();
    if (!sel || sel.length < SHARE_MIN_LEN) { hideShare(); return; }
    if (!me) {
      setStatus('Connecte-toi pour envoyer au chat.', 'err');
      setOpen(true);
      try {
        const t = document.getElementById('authModalTrigger');
        if (t) t.click();
        else if (window.CodexAuth) window.CodexAuth.openModal();
      } catch (e) {}
      return;
    }
    if (!activeId || !activeDoc) {
      setStatus('Aucune table active — rejoins une table depuis la page Tables.', 'err');
      setOpen(true);
      return;
    }
    const text = buildShareText(sel);
    if (b) { b.disabled = true; b.textContent = 'Envoi…'; }
    try {
      const r = await api('/api/tables/' + encodeURIComponent(activeId) + '/messages', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text, toExcept: effectiveExcept() })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      try {
        activeDoc.messages.push(d.message);
        if (d.message.createdAt > lastTs) lastTs = d.message.createdAt;
      } catch (e) {}
      try { if (window.getSelection) window.getSelection().removeAllRanges(); } catch (e) {}
      hideShare();
      setOpen(true);
      render(true);
      setStatus('Extrait envoyé au chat ✓', 'ok');
    } catch (e) {
      setStatus(e.message, 'err');
    }
    if (b) { b.disabled = false; b.textContent = '💬 Envoyer au chat'; }
  }
  function initShare() {
    if (initShare.done) return;
    initShare.done = true;
    shareBtn();
    document.addEventListener('mouseup', scheduleShare);
    document.addEventListener('touchend', scheduleShare);
    try { document.addEventListener('selectionchange', scheduleShare); } catch (e) {}
    document.addEventListener('mousedown', (e) => {
      try {
        if (e && e.target && e.target.closest && e.target.closest('#kdShareSel')) return;
      } catch (err) {}
      hideShare();
    });
    window.addEventListener('scroll', hideShare, { capture: true, passive: true });
    window.addEventListener('resize', hideShare);
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') hideShare();
      else if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
        try {
          if ($('kdShareSel') && $('kdShareSel').style.display !== 'none') sendShare();
        } catch (err) {}
      }
    });
  }

  /* ---------- largeur ajustable (bordure, persistée) ---------- */
  const DOCK_W_KEY = 'kt-dock-width';
  const DOCK_W_MIN = 280, DOCK_W_MAX = 640, DOCK_W_DEF = 360;
  function dockWidth() {
    try {
      const v = parseInt(localStorage.getItem(DOCK_W_KEY), 10);
      if (Number.isFinite(v)) return Math.min(DOCK_W_MAX, Math.max(DOCK_W_MIN, v));
    } catch (e) {}
    return DOCK_W_DEF;
  }
  function applyDockWidth(v) {
    const p = $('ktDock');
    if (p) p.style.width = 'min(' + v + 'px, 94vw)';
  }
  function clampDockWidth(v) {
    v = parseInt(v, 10);
    if (!Number.isFinite(v)) return DOCK_W_DEF;
    return Math.min(DOCK_W_MAX, Math.max(DOCK_W_MIN, v));
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
      '<div class="kd-sec"><button id="kdMToggle" type="button">👥 Joueurs</button><div id="kdMembers"></div></div>' +
      '<div id="kdUploadState" style="display:none"></div>' +
      '<div id="kdMsgs"></div>' +
      '<div id="kdScope"></div>' +
      '<div class="kd-compose" id="kdCompose">' +
      '<textarea id="kdText" maxlength="2000" placeholder="Écris ici… (Entrée = envoyer, ou dépose image/PDF/audio/vidéo)"></textarea>' +
      '<div class="kd-row"><button id="kdSend" type="button">Envoyer</button><span class="kd-status" id="kdStatus"></span></div></div>' +
      '<div class="kd-foot">MPs et gestion : <a id="kdFootLink" href="#">page Tables</a></div>' +
      '<div id="kdGrip" title="Redimensionner (glisser)"></div>';
    document.body.appendChild(p);

    // Rattrapage scroll : les médias chargent après le rendu et grandissent ;
    // on rattrape si on avait mis en bas (peu importe la distance).
    const kdBox = $('kdMsgs');
    const kdKeepBottom = () => { if (kdBox._stuck !== false) stickDockBottom(kdBox); };
    kdBox.addEventListener('scroll', (e) => {
      if (e && e.isTrusted === false) return;
      try {
        kdBox._stuck = (kdBox.scrollHeight - kdBox.scrollTop - (kdBox.clientHeight || 0)) < 40;
      } catch (err) {}
    });
    kdBox.addEventListener('load', kdKeepBottom, true);
    kdBox.addEventListener('loadedmetadata', kdKeepBottom, true);

    const fullUrl = () => tablesPrefix() + 'tables.html' + (activeId ? '?table=' + encodeURIComponent(activeId) : '');
    $('kdHide').addEventListener('click', () => setOpen(false));
    $('kdFull').addEventListener('click', () => { window.location.href = fullUrl(); });
    $('kdFootLink').addEventListener('click', (e) => { e.preventDefault(); window.location.href = fullUrl(); });
    $('kdPick').addEventListener('change', (e) => openTable(e.target.value));
    $('kdMToggle').addEventListener('click', () => {
      membersOpen = !membersOpen;
      renderMembers();
    });
    applyDockWidth(dockWidth());
    // Redimensionnement à la souris sur le bord gauche (glisser).
    $('kdGrip').addEventListener('pointerdown', (e) => {
      if (e.button !== undefined && e.button !== 0) return;
      if (e.preventDefault) e.preventDefault();
      const grip = $('kdGrip');
      if (grip && grip.classList) grip.classList.add('drag');
      const vw = () => window.innerWidth || DOCK_W_MAX;
      const move = (ev) => applyDockWidth(clampDockWidth(vw() - (ev.clientX || 0)));
      const up = (ev) => {
        window.removeEventListener('pointermove', move);
        if (grip && grip.classList) grip.classList.remove('drag');
        try { localStorage.setItem(DOCK_W_KEY, String(clampDockWidth(vw() - (ev.clientX || 0)))); } catch (err) {}
      };
      window.addEventListener('pointermove', move);
      window.addEventListener('pointerup', up, { once: true });
    });
    $('kdSend').addEventListener('click', send);
    initShare();
    bindDockDrop($('kdMsgs'));
    bindDockDrop($('kdCompose'));
    $('kdText').addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(); }
    });
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && isOpen()) setOpen(false);
    });
    // Clic ailleurs dans le site (hors panneau/bouton/partage) : referme
    // le panneau. Exclusions : intérieur du dock, bouton latéral (toggle),
    // bouton flottant « Envoyer au chat » (rouvre le panneau à l'envoi).
    document.addEventListener('pointerdown', (e) => {
      if (!isOpen()) return;
      try {
        if (e && e.button !== undefined && e.button !== 0) return;
        const t = e && e.target;
        if (!t || !t.closest) return;
        if (t.closest('#ktDock, #ktDockFab, #kdShareSel')) return;
      } catch (err) { return; }
      setOpen(false);
    }, { capture: true });
    window.addEventListener('storage', (e) => {
      if (e.key === 'kt-tables-unread') updateFab();
    });
    // Présence d'upload poussée par SSE (via users/auth.js).
    window.addEventListener('codex:upload-state', (e) => {
      const d = (e && e.detail) || {};
      if (d.tableId && d.tableId === activeId) renderDockUploads(d.uploads || []);
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
      if (activeId) notifyDockUpload('done');
      me = null; tables = []; activeId = null; activeDoc = null;
      clearInterval(pollTimer);
      try { window.KT_ACTIVE_TABLE = null; } catch (e) {}
      renderLoggedOut();
      updateFab();
    });
  }

  init();
})();
