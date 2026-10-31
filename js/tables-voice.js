/* Voix live des tables de groupe — salon vocal WebRTC (mesh P2P).
   Inspiré du VTTVoiceManager KT : modes VAD / PTT / continu, gate + hangover,
   passe-haut 80 Hz, limiteur, volumes par joueur, muet/sourdine, pastille
   "parle". Signalisation via REST + SSE (serveur), média en P2P (jamais loggé).
   Règle serveur : un joueur muté ne reçoit plus le vocal ; seul un reset pour
   tout le monde (MJ/helper) rouvre. Offreur déterministe = plus petit userId. */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const esc = (s) => String(s || '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
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
  const post = (p, body) => api(p, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body || {}) });

  // Constantes reprises du KT (VTTVoiceManager / VoiceDspProcessor).
  const HANGOVER_MS = 420;       // _vadHangoverSec = 0.42 s
  const DEF_THRESHOLD = 0.008;   // GateThreshold ≈ -42 dB
  const RTC_CONF = { iceServers: [{ urls: 'stun:stun.l.google.com:19302' }] };

  const S = {
    tableId: null, me: null, members: [], myRole: 'joueur',
    inVoice: false, iAmMuted: false, peers: [],
    micOn: true, listening: true, mode: 'vad', pttKey: 'v', pttHeld: false,
    inGain: 1.0, outVol: 1.0, threshold: DEF_THRESHOLD,
    peerVol: {}, peerLocalMute: {}, peerSpeaking: {},
    ctx: null, micStream: null, outTrack: null, analyser: null, vadData: null,
    hangoverUntil: 0, speaking: false, pcs: new Map(), loop: null, poll: null, watch: null,
  };
  const isOfferer = (a, b) => String(a || '') < String(b || '');
  const isManager = () => S.myRole === 'mj' || S.myRole === 'helper' || S.myRole === 'admin';
  const peerOf = (uid) => S.peers.find((p) => p && p.userId === uid) || null;

  function status(msg, cls) {
    const e = $('tbVStatus');
    if (!e) return;
    e.textContent = msg || '';
    e.className = 'tb-status' + (cls ? ' ' + cls : '');
  }

  async function refreshMe() {
    try {
      const r = await api('/api/auth/me', { cache: 'no-store' });
      if (r.ok) { S.me = (await r.json()).user; return true; }
    } catch (e) {}
    return false;
  }

  async function refreshTable() {
    if (!S.tableId) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(S.tableId) + '?limit=1', { cache: 'no-store' });
      if (!r.ok) return;
      const t = (await r.json()).table;
      S.members = t.members || [];
      S.myRole = t.myRole || 'joueur';
    } catch (e) {}
  }

  async function refreshVoice() {
    if (!S.tableId) return;
    try {
      const r = await api('/api/tables/' + encodeURIComponent(S.tableId) + '/voice', { cache: 'no-store' });
      if (!r.ok) return;
      const d = await r.json();
      S.inVoice = !!d.inVoice;
      S.iAmMuted = !!d.iAmMuted;
      S.peers = d.peers || [];
    } catch (e) {}
  }

  /* ---------- capture micro + DSP léger (passe-haut, gain, limiteur) ---------- */
  async function buildCapture() {
    const stream = await navigator.mediaDevices.getUserMedia({
      audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: false },
    });
    const Ctx = window.AudioContext || window.webkitAudioContext;
    const ctx = new Ctx();
    if (ctx.state === 'suspended') { try { await ctx.resume(); } catch (e) {} }
    const src = ctx.createMediaStreamSource(stream);
    const hp = ctx.createBiquadFilter(); // KT : passe-haut 80 Hz, Q 0.707
    hp.type = 'highpass'; hp.frequency.value = 80; hp.Q.value = 0.7071;
    const gain = ctx.createGain(); gain.gain.value = S.inGain;
    gain.gain.setTargetAtTime(S.inGain, ctx.currentTime, 0.02);
    const comp = ctx.createDynamicsCompressor(); // KT : plafond anti-cris
    comp.threshold.value = -12; comp.knee.value = 12; comp.ratio.value = 12;
    comp.attack.value = 0.005; comp.release.value = 0.075;
    const analyser = ctx.createAnalyser(); analyser.fftSize = 1024;
    const dest = ctx.createMediaStreamDestination();
    src.connect(hp); hp.connect(gain); gain.connect(comp); comp.connect(analyser); analyser.connect(dest);
    S.ctx = ctx; S.micStream = stream;
    S.outTrack = dest.stream.getAudioTracks()[0];
    S.analyser = analyser; S.vadData = new Float32Array(analyser.fftSize);
    S._gainNode = gain;
    return true;
  }

  function rmsLocal() {
    if (!S.analyser || !S.vadData) return 0;
    S.analyser.getFloatTimeDomainData(S.vadData);
    let sum = 0;
    for (let i = 0; i < S.vadData.length; i++) sum += S.vadData[i] * S.vadData[i];
    return Math.sqrt(sum / S.vadData.length);
  }

  function vadTick() {
    if (!S.inVoice || !S.outTrack) return;
    const now = performance.now();
    const level = rmsLocal();
    const gateOpen = level > S.threshold;
    if (gateOpen) S.hangoverUntil = now + HANGOVER_MS;
    const held = S.pttHeld;
    let want = false;
    if (S.mode === 'cont') want = true;
    else if (S.mode === 'ptt') want = held;
    else want = gateOpen || now < S.hangoverUntil;
    S.speaking = want && S.micOn && !S.iAmMuted;
    S.outTrack.enabled = S.speaking;
    S._level = level;
  }

  /* ---------- mesh WebRTC ---------- */
  function ensurePC(peerId) {
    let rec = S.pcs.get(peerId);
    if (rec && rec.pc && rec.pc.signalingState !== 'closed') return rec;
    const pc = new RTCPeerConnection(RTC_CONF);
    if (S.outTrack) { try { pc.addTrack(S.outTrack, new MediaStream([S.outTrack])); } catch (e) {} }
    rec = { pc, audioEl: null, gain: null, analyser: null, vad: null };
    pc.onicecandidate = (ev) => {
      if (ev.candidate) sendSignal(peerId, 'ice', JSON.stringify(ev.candidate));
    };
    pc.ontrack = (ev) => {
      try {
        const stream = ev.streams && ev.streams[0];
        if (!stream) return;
        // Re-négociation : remplacer l'ancien graphe, pas l'empiler.
        try { if (rec.nodes) rec.nodes.forEach((n) => { try { n.disconnect(); } catch (e) {} }); } catch (e) {}
        try { if (rec.audioEl) rec.audioEl.remove(); } catch (e) {}
        const src = S.ctx.createMediaStreamSource(stream);
        const g = S.ctx.createGain(); g.gain.value = peerGain(peerId);
        const an = S.ctx.createAnalyser(); an.fftSize = 512;
        src.connect(g); g.connect(an); an.connect(S.ctx.destination);
        const el = document.createElement('audio');
        el.style.display = 'none'; el.muted = true; // sortie via WebAudio (gains), pas l'élément
        document.body.appendChild(el);
        rec.gain = g; rec.analyser = an; rec.vad = new Float32Array(an.fftSize);
        rec.nodes = [src, g, an]; rec.audioEl = el;
        applyPeerOutput(peerId);
      } catch (e) {}
    };
    pc.onconnectionstatechange = () => {
      if (pc.connectionState === 'failed') { try { pc.restartIce(); } catch (e) {} }
    };
    S.pcs.set(peerId, rec);
    return rec;
  }

  function closePC(peerId) {
    const rec = S.pcs.get(peerId);
    if (!rec) return;
    try { rec.pc.close(); } catch (e) {}
    try { if (rec.nodes) rec.nodes.forEach((n) => { try { n.disconnect(); } catch (e) {} }); } catch (e) {}
    try { if (rec.audioEl) rec.audioEl.remove(); } catch (e) {}
    S.pcs.delete(peerId);
  }

  function closeAllPCs() {
    [...S.pcs.keys()].forEach(closePC);
  }

  function peerGain(peerId) {
    if (S.peerLocalMute[peerId]) return 0;
    const v = Number(S.peerVol[peerId]);
    return Number.isFinite(v) ? v : 1.0;
  }

  function applyPeerOutput(peerId) {
    const rec = S.pcs.get(peerId);
    if (!rec || !rec.gain || !S.ctx) return;
    const p = peerOf(peerId);
    const serverMuted = !!(p && p.muted);
    const g = (serverMuted || !S.listening) ? 0 : peerGain(peerId) * S.outVol;
    rec.gain.gain.setTargetAtTime(g, S.ctx.currentTime, 0.03);
  }

  function applyAllOutputs() {
    [...S.pcs.keys()].forEach(applyPeerOutput);
  }

  async function sendSignal(toUserId, kind, data) {
    if (!S.tableId || !S.inVoice) return;
    try {
      const r = await post('/api/tables/' + encodeURIComponent(S.tableId) + '/voice-signal', { toUserId, kind, data });
      if (!r.ok && r.status === 403) { await refreshVoice(); render(); }
    } catch (e) {}
  }

  async function offerTo(peerId) {
    if (peerOf(peerId) && peerOf(peerId).muted) return; // muté : pas de vocal
    if (S.iAmMuted) return;
    const rec = ensurePC(peerId);
    try {
      const offer = await rec.pc.createOffer({ offerToReceiveAudio: true });
      await rec.pc.setLocalDescription(offer);
      sendSignal(peerId, 'offer', JSON.stringify(offer));
    } catch (e) {}
  }

  async function onSignal(d) {
    if (!d || d.tableId !== S.tableId || !S.inVoice) return;
    const from = d.fromUserId;
    if (!from || from === (S.me && S.me.id)) return;
    if (S.iAmMuted) return;
    const p = peerOf(from);
    if (p && p.muted) return; // expéditeur muté : on ignore son vocal
    try {
      if (d.kind === 'offer') {
        // Glare (double offre simultanée) : la règle offreur tranche —
        // le plus petit userId garde son offre, l'autre bascule.
        const rec0 = S.pcs.get(from);
        if (rec0 && rec0.pc && rec0.pc.signalingState === 'have-local-offer') {
          if (S.me && isOfferer(S.me.id, from)) return; // mon offre gagne, j'ignore la leur
          closePC(from); // leur offre gagne, je repars d'une PC neuve
        }
        const rec = ensurePC(from);
        await rec.pc.setRemoteDescription(new RTCSessionDescription(JSON.parse(d.data)));
        const answer = await rec.pc.createAnswer();
        await rec.pc.setLocalDescription(answer);
        sendSignal(from, 'answer', JSON.stringify(answer));
      } else if (d.kind === 'answer') {
        const rec = S.pcs.get(from);
        if (rec) await rec.pc.setRemoteDescription(new RTCSessionDescription(JSON.parse(d.data)));
      } else if (d.kind === 'ice') {
        const rec = S.pcs.get(from);
        if (rec) { try { await rec.pc.addIceCandidate(new RTCIceCandidate(JSON.parse(d.data))); } catch (e) {} }
      }
    } catch (e) {}
  }

  function onPresence(d) {
    if (!d || d.tableId !== S.tableId) return;
    S.peers = d.peers || S.peers;
    if (d.left) closePC(d.left);
    // Nouveau pair : j'offre si c'est à moi (plus petit id offre).
    if (d.joined && S.inVoice && !S.iAmMuted && S.me && d.joined !== S.me.id) {
      const p = peerOf(d.joined);
      if (p && !p.muted && isOfferer(S.me.id, d.joined) && !S.pcs.has(d.joined)) offerTo(d.joined);
    }
    render();
  }

  async function onMute(d) {
    if (!d || d.tableId !== S.tableId) return;
    await refreshVoice();
    if (d.userId) {
      if (d.muted) closePC(d.userId); // plus de vocal vers / depuis un muté
      else if (S.inVoice && !S.iAmMuted && S.me && d.userId !== S.me.id) {
        const p = peerOf(d.userId);
        if (p && isOfferer(S.me.id, d.userId)) offerTo(d.userId); // démuré : on rétablit
      }
    }
    applyAllOutputs();
    render();
  }

  async function onReset() {
    await refreshVoice();
    // Reset pour tout le monde : on rétablit les liens manquants (règle offreur).
    if (S.inVoice && !S.iAmMuted && S.me) {
      for (const p of S.peers) {
        if (!p || p.userId === S.me.id || p.muted) continue;
        if (!S.pcs.has(p.userId) && isOfferer(S.me.id, p.userId)) offerTo(p.userId);
      }
    }
    applyAllOutputs();
    render();
  }

  // L'utilisateur manipule les réglages voix : ne pas écraser le DOM
  // sous ses doigts (le sondage ne fait que réconcilier l'état).
  function voiceUiBusy() {
    const a = document.activeElement;
    return !!(a && $('tbVoice') && $('tbVoice').contains(a) && /^(INPUT|SELECT|TEXTAREA|BUTTON)$/.test(a.tagName));
  }

  // Réconciliation sans SSE (repli sondage) : ferme les pairs partis,
  // offre aux nouveaux selon la règle offreur, sans doublon.
  async function reconcileMesh() {
    if (!S.inVoice || !S.me || S.iAmMuted) return;
    const ids = new Set(S.peers.map((p) => p && p.userId).filter(Boolean));
    [...S.pcs.keys()].forEach((uid) => { if (!ids.has(uid)) closePC(uid); });
    for (const p of S.peers) {
      if (!p || p.userId === S.me.id || p.muted) continue;
      if (!S.pcs.has(p.userId) && isOfferer(S.me.id, p.userId)) await offerTo(p.userId);
    }
  }

  async function voicePollTick() {
    if (!S.inVoice) return;
    await refreshVoice();
    await refreshTable();
    await reconcileMesh();
    applyAllOutputs();
    if (!voiceUiBusy()) render();
  }

  /* ---------- actions ---------- */
  async function join() {
    if (!S.tableId || S.inVoice) return;
    if (!window.isSecureContext && window.location.hostname !== 'localhost' && window.location.hostname !== '127.0.0.1') {
      status('Micro impossible sans HTTPS (ou localhost).', 'err'); return;
    }
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      status('Micro non pris en charge par ce navigateur.', 'err'); return;
    }
    status('Demande micro…');
    try {
      await buildCapture();
    } catch (e) {
      status('Micro refusé ou indisponible.', 'err'); return;
    }
    try {
      const r = await post('/api/tables/' + encodeURIComponent(S.tableId) + '/voice-join', {});
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      await refreshVoice();
      await refreshTable();
      // J'offre aux pairs présents si c'est à moi (plus petit id offre).
      for (const p of S.peers) {
        if (!p || p.userId === S.me.id || p.muted) continue;
        if (isOfferer(S.me.id, p.userId)) await offerTo(p.userId);
      }
      clearInterval(S.loop); S.loop = setInterval(vadTick, 100);
      clearInterval(S.poll); S.poll = setInterval(voicePollTick, 5000);
      render();
      if (S.iAmMuted) status('Vous êtes muté : le MJ doit faire un reset vocal.', 'err');
      else status('En voix ✓', 'ok');
    } catch (e) {
      stopTracks();
      render();
      status(e.message, 'err'); return;
    }
  }

  async function leave(sendLeave) {
    clearInterval(S.loop); S.loop = null;
    clearInterval(S.poll); S.poll = null;
    closeAllPCs();
    stopTracks();
    if (sendLeave !== false && S.tableId && S.inVoice) {
      try { await post('/api/tables/' + encodeURIComponent(S.tableId) + '/voice-leave', {}); } catch (e) {}
    }
    S.inVoice = false; S.speaking = false;
    await refreshVoice();
    render();
  }

  function stopTracks() {
    try { if (S.micStream) S.micStream.getTracks().forEach((t) => t.stop()); } catch (e) {}
    S.micStream = null; S.outTrack = null; S.analyser = null;
    try { if (S.ctx) S.ctx.close(); } catch (e) {}
    S.ctx = null;
  }

  async function mutePeer(uid, muted) {
    try {
      const r = await post('/api/tables/' + encodeURIComponent(S.tableId) + '/voice-mute', { userId: uid, muted });
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      await refreshVoice(); render();
    } catch (e) { status(e.message, 'err'); }
  }

  async function resetAll() {
    if (!confirm('Reset vocal pour tout le monde ? Tous les mutés recevront à nouveau le vocal.')) return;
    try {
      const r = await post('/api/tables/' + encodeURIComponent(S.tableId) + '/voice-reset', {});
      const d = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(d.error || ('Erreur ' + r.status));
      await refreshVoice(); render();
      status('Voix rouverte pour tout le monde ✓', 'ok');
    } catch (e) { status(e.message, 'err'); }
  }

  /* ---------- rendu ---------- */
  function render() {
    const box = $('tbVoice');
    if (!box) return;
    if (!S.me) { box.innerHTML = '<p class="tb-status">Connecte-toi pour la voix.</p>'; return; }
    if (!S.tableId) { box.innerHTML = '<p class="tb-status">Ouvre une table pour la voix.</p>'; return; }
    const mgr = isManager();
    const mutedNames = S.peers.filter((p) => p.muted).map((p) => p.username);
    let h = '<div class="tb-row" style="align-items:center;gap:8px;flex-wrap:wrap;">';
    h += S.inVoice
      ? '<button class="tb-btn danger" id="tbVLeave" type="button">🔇 Quitter la voix</button>'
      : '<button class="tb-btn primary" id="tbVJoin" type="button">🎙️ Rejoindre la voix</button>';
    if (S.inVoice) {
      h += '<button class="tb-btn" id="tbVMic" type="button">' + (S.micOn ? '🎙️ Micro actif' : '🚫 Micro coupé') + '</button>';
      h += '<button class="tb-btn" id="tbVListen" type="button">' + (S.listening ? '🔊 Écoute' : '🔇 Sourdine') + '</button>';
      const modes = [['vad', '🎙️ VAD'], ['ptt', '⚡ PTT'], ['cont', '〰️ Continu']];
      h += '<span class="tb-status">Mode :</span>';
      modes.forEach(([m, lab]) => {
        h += '<button class="tb-btn' + (S.mode === m ? ' primary' : '') + '" data-vmode="' + m + '" type="button">' + lab + '</button>';
      });
    }
    h += '</div>';
    if (S.iAmMuted) {
      h += '<div class="tb-restr">🔕 Tu es muté pour la voix : tu ne reçois plus le vocal. Demande au MJ un reset pour tout le monde.</div>';
    } else if (mutedNames.length) {
      h += '<div class="tb-restr">🔕 Ne reçoivent pas le vocal : ' + esc(mutedNames.join(', '));
      if (mgr) h += ' <button class="tb-btn" id="tbVReset" type="button" title="Rouvrir la voix à tout le monde">↺ Reset vocal</button>';
      h += '</div>';
    } else if (mgr && S.inVoice) {
      h += '<div class="tb-restr">🔊 Tout le monde reçoit le vocal.</div>';
    }
    if (S.inVoice) {
      const lvl = Math.round((S._level || 0) * 100);
      const speak = S.speaking ? '<span style="color:#6ee7b7;">● PARLE</span>' : '<span style="color:var(--text-muted);">○ silencieux</span>';
      h += '<div class="tb-status" id="tbVLevel">Niveau micro : ' + lvl + '% · ' + speak + (S.mode === 'ptt' ? ' · touche [' + esc(S.pttKey.toUpperCase()) + ']' : '') + '</div>';
      h += '<div class="tb-field"><label>Gain micro (' + S.inGain.toFixed(1) + 'x)</label>' +
        '<input id="tbVGain" type="range" min="0.2" max="5" step="0.1" value="' + S.inGain + '"></div>';
      h += '<div class="tb-field"><label>Volume voix (' + Math.round(S.outVol * 100) + '%)</label>' +
        '<input id="tbVVol" type="range" min="0" max="2" step="0.05" value="' + S.outVol + '"></div>';
      h += '<div class="tb-field"><label>Seuil VAD (' + Math.round(20 * Math.log10(Math.max(0.0001, S.threshold))) + ' dB)</label>' +
        '<input id="tbVThr" type="range" min="0.005" max="0.12" step="0.001" value="' + S.threshold + '"></div>';
      if (S.mode === 'ptt') {
        h += '<div class="tb-field"><label>Touche PTT</label><select id="tbVPtt">' +
          [['v', 'V'], [' ', 'Espace'], ['t', 'T'], ['c', 'C']].map(([k, n]) =>
            '<option value="' + esc(k) + '"' + (S.pttKey === k ? ' selected' : '') + '>' + n + '</option>').join('') + '</select></div>';
      }
      h += '<div class="tb-field"><label>En voix (' + S.peers.length + ')</label><div>';
      S.peers.forEach((p) => {
        if (!p) return;
        const self = p.userId === S.me.id;
        const dot = self
          ? (S.speaking ? '<span style="color:#6ee7b7;">●</span>' : '<span style="color:var(--text-muted);">○</span>')
          : '<span data-vspeaks="' + esc(p.userId) + '" style="color:var(--text-muted);">○</span>';
        h += '<div class="tb-member">' + dot + '<span>' + esc(p.username) + (self ? ' (vous)' : '') + '</span>';
        if (p.muted) h += '<span class="tb-badge priv">muté</span>';
        if (!self) {
          const pv = Math.round(peerGain(p.userId) * 100);
          h += '<input data-vvol="' + esc(p.userId) + '" type="range" min="0" max="150" step="5" value="' + pv + '" style="max-width:90px;" title="Volume local">';
          h += '<button class="tb-btn" data-vloc="' + esc(p.userId) + '" type="button" title="Sourdine locale (pour toi seul)">' + (S.peerLocalMute[p.userId] ? '🔇' : '🔈') + '</button>';
          if (mgr) h += '<button class="tb-btn" data-vmute="' + esc(p.userId) + '" type="button" title="' + (p.muted ? 'Démuter (reçoit à nouveau)' : 'Muter (ne reçoit plus le vocal)') + '">' + (p.muted ? '🔔' : '🔕') + '</button>';
        } else {
          h += '<button class="tb-btn" data-vmute="' + esc(p.userId) + '" type="button" title="Couper mon micro (je ne reçois/émets plus)">' + (p.muted ? '🔔 démuter' : '🚫 me muter') + '</button>';
        }
        h += '</div>';
      });
      h += '</div></div>';
      if (mgr && mutedNames.length) h += '<button class="tb-btn" id="tbVReset2" type="button">↺ Reset vocal pour tout le monde</button>';
    } else {
      h += '<p class="tb-status">Voix live : rejoins le salon, parle en VAD / PTT / continu. Micro HTTPS ou localhost requis.</p>';
    }
    h += '<div class="tb-status" id="tbVStatus"></div>';
    box.innerHTML = h;
    bind(box);
  }

  function bind(box) {
    const on = (id, fn) => { const e = $(id); if (e) e.addEventListener('click', fn); };
    on('tbVJoin', join);
    on('tbVLeave', () => leave(true));
    on('tbVMic', () => { S.micOn = !S.micOn; render(); });
    on('tbVListen', () => { S.listening = !S.listening; applyAllOutputs(); render(); });
    on('tbVReset', resetAll);
    on('tbVReset2', resetAll);
    box.querySelectorAll('[data-vmode]').forEach((b) => b.addEventListener('click', () => { S.mode = b.getAttribute('data-vmode'); render(); }));
    const g = $('tbVGain');
    if (g) g.addEventListener('input', () => {
      S.inGain = Number(g.value) || 1.0;
      if (S._gainNode && S.ctx) S._gainNode.gain.setTargetAtTime(S.inGain, S.ctx.currentTime, 0.02);
      const lab = g.parentElement && g.parentElement.querySelector('label');
      if (lab) lab.textContent = 'Gain micro (' + S.inGain.toFixed(1) + 'x)';
    });
    const v = $('tbVVol');
    if (v) v.addEventListener('input', () => {
      S.outVol = Number(v.value);
      applyAllOutputs();
      const lab = v.parentElement && v.parentElement.querySelector('label');
      if (lab) lab.textContent = 'Volume voix (' + Math.round(S.outVol * 100) + '%)';
    });
    const t = $('tbVThr');
    if (t) t.addEventListener('input', () => {
      S.threshold = Number(t.value) || DEF_THRESHOLD;
      const lab = t.parentElement && t.parentElement.querySelector('label');
      if (lab) lab.textContent = 'Seuil VAD (' + Math.round(20 * Math.log10(Math.max(0.0001, S.threshold))) + ' dB)';
    });
    const pk = $('tbVPtt');
    if (pk) pk.addEventListener('change', () => { S.pttKey = pk.value || 'v'; render(); });
    box.querySelectorAll('[data-vvol]').forEach((s) => s.addEventListener('input', () => {
      const uid = s.getAttribute('data-vvol');
      S.peerVol[uid] = (Number(s.value) || 100) / 100;
      applyPeerOutput(uid);
    }));
    box.querySelectorAll('[data-vloc]').forEach((b) => b.addEventListener('click', () => {
      const uid = b.getAttribute('data-vloc');
      S.peerLocalMute[uid] = !S.peerLocalMute[uid];
      applyPeerOutput(uid); render();
    }));
    box.querySelectorAll('[data-vmute]').forEach((b) => b.addEventListener('click', () => {
      const uid = b.getAttribute('data-vmute');
      const p = peerOf(uid);
      mutePeer(uid, !(p && p.muted));
    }));
  }

  /* ---------- pastilles "parle" + niveau local (léger, sans re-rendu) ---------- */
  // Indicateur dans la table : pastille ●/○ devant chaque membre en voix,
  // directement dans la liste des membres (#tbManage, gérée par tables.js).
  function patchMemberDots() {
    const manage = $('tbManage');
    if (!manage) return;
    if (!S.inVoice || !S.me) {
      manage.querySelectorAll('[data-vdot]').forEach((d) => d.remove());
      return;
    }
    const byName = {};
    byName[S.me.username] = { speaking: S.speaking, muted: S.iAmMuted, self: true };
    S.peers.forEach((p) => {
      if (!p) return;
      byName[p.username] = { speaking: !!S.peerSpeaking[p.userId], muted: !!p.muted, self: p.userId === S.me.id };
    });
    manage.querySelectorAll('.tb-member').forEach((row) => {
      const span = row.querySelector('span');
      if (!span) return;
      const name = (span.textContent || '').trim();
      const st = byName[name];
      let dot = row.querySelector('[data-vdot]');
      if (!st) { if (dot) dot.remove(); return; }
      if (!dot) {
        dot = document.createElement('span');
        dot.setAttribute('data-vdot', '1');
        dot.title = 'Voix live';
        dot.style.marginRight = '2px';
        row.insertBefore(dot, row.firstChild);
      }
      const color = st.muted ? '#f87171' : (st.speaking ? '#6ee7b7' : 'var(--text-muted)');
      dot.textContent = st.speaking ? '●' : '○';
      dot.style.color = color;
      dot.title = st.muted ? 'Muté (ne reçoit plus le vocal)' : (st.speaking ? 'Parle' : 'En voix');
    });
  }

  setInterval(() => {
    if (!S.inVoice) return;
    const lv = $('tbVLevel');
    if (lv) {
      const pct = Math.round((S._level || 0) * 100);
      lv.innerHTML = 'Niveau micro : ' + pct + '% · ' +
        (S.speaking ? '<span style="color:#6ee7b7;">● PARLE</span>' : '<span style="color:var(--text-muted);">○ silencieux</span>') +
        (S.mode === 'ptt' ? ' · touche [' + esc(S.pttKey.toUpperCase()) + ']' : '');
    }
    S.pcs.forEach((rec, uid) => {
      const dot = document.querySelector('[data-vspeaks="' + uid + '"]');
      if (!dot || !rec.analyser || !rec.vad) return;
      rec.analyser.getFloatTimeDomainData(rec.vad);
      let sum = 0;
      for (let i = 0; i < rec.vad.length; i++) sum += rec.vad[i] * rec.vad[i];
      const speaking = Math.sqrt(sum / rec.vad.length) > 0.02;
      S.peerSpeaking[uid] = speaking;
      dot.textContent = speaking ? '●' : '○';
      dot.style.color = speaking ? '#6ee7b7' : 'var(--text-muted)';
    });
    patchMemberDots();
  }, 400);

  /* ---------- PTT clavier ---------- */
  window.addEventListener('keydown', (e) => {
    if (S.mode !== 'ptt' || !S.inVoice || e.repeat) return;
    const tag = (document.activeElement && document.activeElement.tagName) || '';
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
    // Espace/Entrée sur un bouton : laisser le clic natif, pas le PTT.
    if (tag === 'BUTTON' && (e.key === ' ' || e.key === 'Enter')) return;
    if (e.key.toLowerCase() === S.pttKey.toLowerCase()) {
      if (e.key === ' ') e.preventDefault();
      S.pttHeld = true;
    }
  });
  window.addEventListener('keyup', (e) => {
    if (e.key.toLowerCase() === (S.pttKey || 'v').toLowerCase()) S.pttHeld = false;
  });

  /* ---------- bus SSE (via auth.js) ---------- */
  window.addEventListener('codex:voice-signal', (e) => onSignal(e.detail));
  window.addEventListener('codex:voice-presence', (e) => onPresence(e.detail));
  window.addEventListener('codex:voice-mute', (e) => onMute(e.detail));
  window.addEventListener('codex:voice-reset', (e) => onReset(e.detail));

  /* ---------- suivi table active ---------- */
  S.watch = setInterval(async () => {
    const cur = window.KT_ACTIVE_TABLE || null;
    if (cur !== S.tableId) {
      if (S.tableId && S.inVoice) await leave(true);
      S.tableId = cur;
      S.pcs.clear(); S.peerVol = {}; S.peerLocalMute = {};
      if (cur) { await refreshTable(); await refreshVoice(); }
      render();
    }
  }, 1000);

  document.addEventListener('DOMContentLoaded', async () => {
    if (!$('tbVoice')) return;
    S.tableId = window.KT_ACTIVE_TABLE || null;
    if (await refreshMe()) {
      if (S.tableId) { await refreshTable(); await refreshVoice(); }
      render();
    } else {
      render();
    }
  });
})();
