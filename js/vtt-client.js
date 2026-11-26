/* ==========================================================================
   VTT CLIENT — Connexion WebSocket unifiée (/ws/vtt -> join_table) + REST fallback
   js/vtt-client.js
   ========================================================================== */
(function () {
  'use strict';

  const apiBase = () => {
    try {
      if (typeof window.CODEX_API_BASE === 'string') return window.CODEX_API_BASE.replace(/\/+$/, '');
      if (window.location.protocol === 'file:') return 'http://localhost:3000';
      if (window.location.port === '8000') return window.location.protocol + '//' + window.location.hostname + ':3000';
      const h = window.location.hostname;
      if ((h === 'localhost' || h === '127.0.0.1') && window.location.port && window.location.port !== '3000') {
        return 'http://localhost:3000';
      }
    } catch (e) {}
    return '';
  };

  function wsUrl() {
    const base = apiBase();
    if (base) {
      return base.replace(/^http:/i, 'ws:').replace(/^https:/i, 'wss:') + '/ws/vtt';
    }
    const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    return proto + '//' + window.location.host + '/ws/vtt';
  }

  const listeners = new Map();
  function emit(ev, detail) {
    const set = listeners.get(ev);
    if (set) {
      for (const fn of set) {
        try { fn(detail); } catch (e) {}
      }
    }
    try { window.dispatchEvent(new CustomEvent('codex:vtt:' + ev, { detail })); } catch (e) {}
  }

  const state = {
    tableId: null,
    ws: null,
    status: 'offline', // offline | connecting | connected | reconnecting
    role: 'player',
    tableRole: 'joueur',
    isGM: false,
    members: [],
    vtt: null,
    retryCount: 0,
    retryTimer: null,
    pingTimer: null
  };

  function setStatus(s) {
    state.status = s;
    emit('status', { status: s, tableId: state.tableId });
  }

  function clearTimers() {
    if (state.retryTimer) { clearTimeout(state.retryTimer); state.retryTimer = null; }
    if (state.pingTimer) { clearInterval(state.pingTimer); state.pingTimer = null; }
  }

  async function fetchRestState(tableId) {
    const tid = tableId || state.tableId;
    if (!tid) return null;
    try {
      const r = await fetch(apiBase() + '/api/tables/' + encodeURIComponent(tid) + '/vtt', {
        credentials: 'include',
        cache: 'no-store',
        headers: { 'ngrok-skip-browser-warning': 'true' }
      });
      if (!r.ok) return null;
      const d = await r.json();
      if (d && d.state && state.tableId === tid) {
        state.isGM = !!d.isGM;
        state.tableRole = d.tableRole || state.tableRole;
        state.role = state.isGM ? 'gm' : 'player';
        state.vtt = d.state;
        emit('state', { state: state.vtt, op: 'rest_sync', isGM: state.isGM });
      }
      return d;
    } catch (e) {
      return null;
    }
  }

  function scheduleReconnect() {
    if (!state.tableId) return;
    clearTimers();
    const delays = [1000, 2000, 4000, 8000, 10000];
    const wait = delays[Math.min(state.retryCount, delays.length - 1)];
    state.retryCount += 1;
    setStatus('reconnecting');
    state.retryTimer = setTimeout(() => {
      if (state.tableId) openSocket(state.tableId);
    }, wait);
  }

  function closeSocket() {
    clearTimers();
    const old = state.ws;
    state.ws = null;
    if (old) {
      try { old.onclose = null; old.onerror = null; old.onmessage = null; old.close(); } catch (e) {}
    }
    setStatus('offline');
  }

  function openSocket(tableId) {
    closeSocket();
    if (!tableId) return;
    setStatus('connecting');
    let ws;
    try {
      ws = new WebSocket(wsUrl());
    } catch (e) {
      scheduleReconnect();
      return;
    }
    state.ws = ws;

    ws.onopen = () => {
      state.retryCount = 0;
      try {
        ws.send(JSON.stringify({ type: 'join_table', tableId }));
      } catch (e) {}
      state.pingTimer = setInterval(() => {
        if (ws.readyState === 1) {
          try { ws.send(JSON.stringify({ type: 'ping' })); } catch (e) {}
        }
      }, 20000);
    };

    ws.onmessage = (ev) => {
      let msg;
      try { msg = JSON.parse(ev.data); } catch (e) { return; }
      if (!msg || typeof msg !== 'object') return;
      if (msg.type === 'table_joined') {
        state.role = msg.role || 'player';
        state.tableRole = msg.tableRole || 'joueur';
        state.isGM = !!msg.isGM;
        state.members = Array.isArray(msg.members) ? msg.members : [];
        if (msg.state) state.vtt = msg.state;
        setStatus('connected');
        emit('joined', msg);
        emit('state', { state: state.vtt, op: 'table_joined', isGM: state.isGM });
      } else if (msg.type === 'vtt_state') {
        if (msg.state) state.vtt = msg.state;
        emit('state', { state: state.vtt, op: msg.op, from: msg.from, fromName: msg.fromName, isGM: state.isGM });
      } else if (msg.type === 'vtt_event') {
        emit('event', msg);
      } else if (msg.type === 'presence') {
        state.members = Array.isArray(msg.members) ? msg.members : [];
        emit('presence', msg);
      } else if (msg.type === 'error') {
        emit('error', msg);
      }
    };

    ws.onclose = () => {
      if (state.ws === ws) {
        state.ws = null;
        scheduleReconnect();
      }
    };
    ws.onerror = () => {};
  }

  async function connectTable(tableId) {
    const tid = String(tableId || '').trim().toLowerCase();
    if (!tid) return disconnect();
    if (state.tableId === tid && state.ws && state.ws.readyState <= 1) return;
    state.tableId = tid;
    state.retryCount = 0;
    await fetchRestState(tid);
    openSocket(tid);
  }

  function disconnect() {
    state.tableId = null;
    state.vtt = null;
    state.members = [];
    closeSocket();
  }

  async function sendOp(op, payload) {
    if (!state.tableId) return { ok: false, error: 'Aucune table active.' };
    if (state.ws && state.ws.readyState === 1) {
      try {
        state.ws.send(JSON.stringify({ type: 'vtt_op', op, payload: payload || {} }));
        return { ok: true, via: 'ws' };
      } catch (e) {}
    }
    try {
      const r = await fetch(apiBase() + '/api/tables/' + encodeURIComponent(state.tableId) + '/vtt/op', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'ngrok-skip-browser-warning': 'true' },
        body: JSON.stringify({ op, payload: payload || {} })
      });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) {
        emit('error', { message: d.error || ('Erreur ' + r.status), code: d.code });
        return { ok: false, error: d.error };
      }
      if (d.state) {
        state.vtt = d.state;
        emit('state', { state: state.vtt, op, isGM: state.isGM });
      }
      return { ok: true, via: 'rest' };
    } catch (e) {
      return { ok: false, error: e.message };
    }
  }

  async function uploadMap(sceneId, body) {
    if (!state.tableId) return { ok: false, error: 'Aucune table active.' };
    const r = await fetch(apiBase() + '/api/tables/' + encodeURIComponent(state.tableId) + '/vtt/map', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json', 'ngrok-skip-browser-warning': 'true' },
      body: JSON.stringify(Object.assign({ sceneId }, body || {}))
    });
    const d = await r.json().catch(() => ({}));
    if (!r.ok) return { ok: false, error: d.error || ('Erreur ' + r.status) };
    if (d.state) {
      state.vtt = d.state;
      emit('state', { state: state.vtt, op: 'vtt_scene', isGM: state.isGM });
    }
    return { ok: true, state: d.state };
  }

  window.addEventListener('codex:table-opened', (e) => {
    const tid = e && e.detail && e.detail.tableId;
    if (tid) connectTable(tid);
  });
  window.addEventListener('codex:table-closed', () => disconnect());

  window.CodexVTT = {
    connectTable,
    disconnect,
    sendOp,
    uploadMap,
    fetchRestState,
    getState: () => state.vtt,
    getTableId: () => state.tableId,
    getStatus: () => state.status,
    isGM: () => state.isGM,
    getRole: () => state.tableRole,
    getMembers: () => state.members,
    on: (ev, fn) => {
      if (!listeners.has(ev)) listeners.set(ev, new Set());
      listeners.get(ev).add(fn);
      return () => listeners.get(ev).delete(fn);
    }
  };
})();
