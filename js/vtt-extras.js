/* ==========================================================================
   VTT EXTRAS — Horloge/Repos/Pathologies, Défis §10, Sorts XP=PA, Audio WebAudio
   js/vtt-extras.js
   ========================================================================== */
(function () {
  'use strict';
  const esc = (s) => String(s || '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  /* ---------- Audio WebAudio synthétique (indépendant de tables-voice.js) ---------- */
  let audioCtx = null;
  let ambNodes = null;
  let curAmb = 'none';
  let localVol = 0.25;
  try {
    const v = parseFloat(localStorage.getItem('kt-vtt-amb-vol'));
    if (Number.isFinite(v)) localVol = Math.max(0, Math.min(1, v));
  } catch (e) {}

  function ensureAudioCtx() {
    const Ctx = window.AudioContext || window.webkitAudioContext;
    if (!Ctx) return null;
    if (!audioCtx) audioCtx = new Ctx();
    if (audioCtx.state === 'suspended') audioCtx.resume().catch(() => {});
    return audioCtx;
  }

  function stopAmbience() {
    if (ambNodes) {
      try { ambNodes.src.stop(); } catch (e) {}
      try { ambNodes.gain.disconnect(); } catch (e) {}
      ambNodes = null;
    }
    curAmb = 'none';
  }

  function startAmbience(kind) {
    if (!kind || kind === 'none') { stopAmbience(); return; }
    if (curAmb === kind && ambNodes) return;
    stopAmbience();
    const ctx = ensureAudioCtx();
    if (!ctx) return;
    const len = ctx.sampleRate * 2;
    const buf = ctx.createBuffer(1, len, ctx.sampleRate);
    const ch = buf.getChannelData(0);
    for (let i = 0; i < len; i++) ch[i] = (Math.random() * 2 - 1) * 0.25;
    const src = ctx.createBufferSource();
    src.buffer = buf;
    src.loop = true;
    const filt = ctx.createBiquadFilter();
    filt.type = kind === 'rain' ? 'bandpass' : 'lowpass';
    filt.frequency.value = kind === 'rain' ? 1200 : (kind === 'wind' ? 420 : (kind === 'station' ? 180 : 300));
    const gain = ctx.createGain();
    gain.gain.value = localVol * 0.35;
    src.connect(filt);
    filt.connect(gain);
    gain.connect(ctx.destination);
    src.start();
    ambNodes = { src, gain, filt };
    curAmb = kind;
  }

  function playSfx(kind) {
    if (localVol <= 0.01) return;
    const ctx = ensureAudioCtx();
    if (!ctx) return;
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    const now = ctx.currentTime;
    if (kind === 'laser') {
      osc.type = 'sawtooth';
      osc.frequency.setValueAtTime(920, now);
      osc.frequency.exponentialRampToValueAtTime(180, now + 0.18);
    } else if (kind === 'sword') {
      osc.type = 'triangle';
      osc.frequency.setValueAtTime(600, now);
      osc.frequency.exponentialRampToValueAtTime(120, now + 0.14);
    } else if (kind === 'explosion') {
      osc.type = 'square';
      osc.frequency.setValueAtTime(110, now);
      osc.frequency.exponentialRampToValueAtTime(32, now + 0.35);
    } else if (kind === 'alarm') {
      osc.type = 'sine';
      osc.frequency.setValueAtTime(880, now);
    } else {
      osc.type = 'sine';
      osc.frequency.setValueAtTime(520, now);
      osc.frequency.exponentialRampToValueAtTime(760, now + 0.12);
    }
    gain.gain.setValueAtTime(localVol * 0.35, now);
    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.35);
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.start(now);
    osc.stop(now + 0.36);
  }

  function syncAudioState(vtt) {
    const au = vtt && vtt.audio;
    if (!au || !au.playing || au.ambience === 'none') stopAmbience();
    else startAmbience(au.ambience);
  }

  function openAudioModal() {
    const back = document.createElement('div');
    back.className = 'vtt-modal-back';
    document.body.appendChild(back);
    function render() {
      const st = window.CodexVTT && window.CodexVTT.getState();
      const isGM = window.CodexVTT && window.CodexVTT.isGM();
      const au = (st && st.audio) || { ambience: 'none', playing: false };
      let h = '<div class="vtt-modal"><div class="vtt-modal-head"><h3>🔊 Ambiance &amp; SFX</h3><button class="vtt-btn" id="vttAuClose" type="button">✕</button></div>' +
        '<div class="tb-field"><label>Volume local ambiance/SFX (' + Math.round(localVol * 100) + '%)</label>' +
        '<input id="vttAuVol" type="range" min="0" max="1" step="0.05" value="' + localVol + '"></div>';
      if (isGM) {
        h += '<div class="vtt-card"><h4>Piste d’ambiance (synchronisée)</h4><div style="display:flex;gap:6px;flex-wrap:wrap;">' +
          [['none', 'Silence'], ['rain', '🌧️ Pluie'], ['wind', '🌬️ Vent'], ['station', '🛸 Station'], ['void', '🌀 Néant']].map(([k, lbl]) =>
            '<button class="vtt-btn' + (au.ambience === k && au.playing ? ' on' : '') + '" data-amb="' + k + '" type="button">' + lbl + '</button>'
          ).join('') + '</div></div>' +
          '<div class="vtt-card" style="margin-top:8px;"><h4>Bruitages SFX</h4><div style="display:flex;gap:6px;flex-wrap:wrap;">' +
          [['dice', '🎲 Dés'], ['sword', '⚔️ Lame'], ['laser', '🔫 Laser'], ['explosion', '💥 Explosion'], ['alarm', '🚨 Alarme'], ['chime', '✨ Sort']].map(([k, lbl]) =>
            '<button class="vtt-btn amber" data-sfx="' + k + '" type="button">' + lbl + '</button>'
          ).join('') + '</div></div>';
      } else {
        h += '<p class="tb-status">Ambiance MJ en cours : <strong>' + esc(au.playing ? au.ambience : 'Silence') + '</strong></p>';
      }
      h += '</div>';
      back.innerHTML = h;
      back.querySelector('#vttAuClose').addEventListener('click', () => { unsub(); back.remove(); });
      back.querySelector('#vttAuVol').addEventListener('input', (e) => {
        localVol = parseFloat(e.target.value);
        try { localStorage.setItem('kt-vtt-amb-vol', String(localVol)); } catch (err) {}
        if (ambNodes) ambNodes.gain.gain.value = localVol * 0.35;
      });
      back.querySelectorAll('[data-amb]').forEach((b) => b.addEventListener('click', () => {
        const k = b.getAttribute('data-amb');
        window.CodexVTT.sendOp('vtt_audio', { action: 'set_ambience', ambience: k, playing: k !== 'none' });
      }));
      back.querySelectorAll('[data-sfx]').forEach((b) => b.addEventListener('click', () => {
        window.CodexVTT.sendOp('vtt_audio', { action: 'sfx', sfx: b.getAttribute('data-sfx') });
      }));
    }
    const unsub = window.CodexVTT.on('state', render);
    back.addEventListener('click', (e) => { if (e.target === back) { unsub(); back.remove(); } });
    render();
  }

  /* ---------- Horloge, Météo, Repos sûr & Pathologies (Livre VI §25 & §53) ---------- */
  function openCampaignToolsModal() {
    const back = document.createElement('div');
    back.className = 'vtt-modal-back';
    document.body.appendChild(back);
    function render() {
      const st = window.CodexVTT && window.CodexVTT.getState();
      const isGM = window.CodexVTT && window.CodexVTT.isGM();
      const cl = (st && st.clock) || { day: 1, hour: 8, minute: 0, weather: 'P1', lighting: 'day' };
      const hh = String(cl.hour).padStart(2, '0'), mm = String(cl.minute).padStart(2, '0');
      const pats = (st && st.pathologies) || [];
      let h = '<div class="vtt-modal"><div class="vtt-modal-head"><h3>🕒 Horloge, Repos sûr &amp; Pathologies</h3><button class="vtt-btn" id="vttClkClose" type="button">✕</button></div>' +
        '<div class="vtt-card"><h4>Jour ' + cl.day + ' — ' + hh + ':' + mm + ' · Météo ' + esc(cl.weather) + ' (' + esc(cl.lighting) + ')</h4>';
      if (isGM) {
        h += '<div style="display:flex;gap:6px;flex-wrap:wrap;margin-bottom:8px;">' +
          '<button class="vtt-btn" data-adv="10">+10 min</button>' +
          '<button class="vtt-btn" data-adv="60">+1 h</button>' +
          '<button class="vtt-btn" data-adv="360">+6 h</button>' +
          '<button class="vtt-btn emerald" data-rest="6">🛏️ Repos 6h</button>' +
          '<button class="vtt-btn emerald" data-rest="8">🛏️ Repos 8h</button>' +
          '<button class="vtt-btn emerald" data-rest="10">🛏️ Repos 10h</button>' +
          '</div>' +
          '<div class="vtt-grid-2">' +
          '<select class="vtt-select" id="vttClkW"><option value="P1">P1 Négligeable (0)</option><option value="P2">P2 Gênant (-1, 8P)</option><option value="P3">P3 Violent (-2, 4P)</option><option value="P4">P4 Opaque (Aveugle)</option></select>' +
          '<select class="vtt-select" id="vttClkL"><option value="day">Jour</option><option value="night_clear">Nuit claire (8P)</option><option value="night_dark">Nuit noire (4P)</option></select>' +
          '</div>';
      }
      h += '</div><div class="vtt-card" style="margin-top:8px;"><h4>Pathologies actives (Livre VI §25.4 &amp; §53)</h4>';
      if (!pats.length) h += '<p class="tb-status">Aucune pathologie active.</p>';
      pats.forEach((pt) => {
        h += '<div class="vtt-zone-row"><span><strong>' + esc(pt.targetName) + '</strong> — ' + esc(pt.name) + ' (IG ' + esc(pt.severity) + ', Stade ' + pt.stage + '/6, Vir. ' + pt.virulence + ')</span>' +
          (isGM ? '<span><button class="vtt-mini-btn" data-pat-d="-1" data-pat-id="' + esc(pt.id) + '">-</button><button class="vtt-mini-btn" data-pat-d="1" data-pat-id="' + esc(pt.id) + '">+</button><button class="vtt-mini-btn" data-pat-rm="' + esc(pt.id) + '">✕</button></span>' : '') + '</div>';
      });
      if (isGM) {
        h += '<div style="display:flex;gap:4px;margin-top:6px;flex-wrap:wrap;">' +
          '<input class="vtt-select" id="vttPatWho" placeholder="Cible" style="flex:1;">' +
          '<input class="vtt-select" id="vttPatName" placeholder="Pathologie" style="flex:1;">' +
          '<select class="vtt-select" id="vttPatIg"><option value="I">IG I</option><option value="II">IG II</option><option value="III">IG III</option><option value="IV">IG IV</option><option value="V">IG V</option></select>' +
          '<button class="vtt-mini-btn" id="vttPatAdd" type="button">+ Ajouter</button></div>';
      }
      h += '</div></div>';
      back.innerHTML = h;
      back.querySelector('#vttClkClose').addEventListener('click', () => { unsub(); back.remove(); });
      if (isGM) {
        back.querySelector('#vttClkW').value = cl.weather || 'P1';
        back.querySelector('#vttClkL').value = cl.lighting || 'day';
        back.querySelector('#vttClkW').addEventListener('change', (e) => window.CodexVTT.sendOp('vtt_clock', { action: 'set_env', weather: e.target.value }));
        back.querySelector('#vttClkL').addEventListener('change', (e) => window.CodexVTT.sendOp('vtt_clock', { action: 'set_env', lighting: e.target.value }));
        back.querySelectorAll('[data-adv]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_clock', { action: 'advance', minutes: parseInt(b.getAttribute('data-adv'), 10) })));
        back.querySelectorAll('[data-rest]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_clock', { action: 'safe_rest', hours: parseInt(b.getAttribute('data-rest'), 10) })));
        back.querySelector('#vttPatAdd').addEventListener('click', () => {
          window.CodexVTT.sendOp('vtt_clock', {
            action: 'pathology_add',
            targetName: back.querySelector('#vttPatWho').value || 'PJ',
            name: back.querySelector('#vttPatName').value || 'Infection',
            severity: back.querySelector('#vttPatIg').value
          });
        });
        back.querySelectorAll('[data-pat-d]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_clock', { action: 'pathology_stage', pathoId: b.getAttribute('data-pat-id'), delta: parseInt(b.getAttribute('data-pat-d'), 10) })));
        back.querySelectorAll('[data-pat-rm]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_clock', { action: 'pathology_remove', pathoId: b.getAttribute('data-pat-rm') })));
      }
    }
    const unsub = window.CodexVTT.on('state', render);
    back.addEventListener('click', (e) => { if (e.target === back) { unsub(); back.remove(); } });
    render();
  }

  /* ---------- Tracker de Défis Hors-Combat (Livre II §10) ---------- */
  function openChallengeModal() {
    const back = document.createElement('div');
    back.className = 'vtt-modal-back';
    document.body.appendChild(back);
    function render() {
      const st = window.CodexVTT && window.CodexVTT.getState();
      const isGM = window.CodexVTT && window.CodexVTT.isGM();
      const list = (st && st.challenges) || [];
      let h = '<div class="vtt-modal"><div class="vtt-modal-head"><h3>🏆 Défis Hors-Combat (Livre II §10)</h3><button class="vtt-btn" id="vttChgClose" type="button">✕</button></div>' +
        '<p class="tb-status">Progression gelée pendant le défi : récompense finale = <code>(Étapes Réussies × 2) − Rattrapages</code> (min 2 si validé, 0 si échec).</p>';
      if (isGM) {
        h += '<div class="vtt-card" style="margin-bottom:8px;"><div style="display:flex;gap:6px;flex-wrap:wrap;">' +
          '<input class="vtt-select" id="vttChgTitle" placeholder="Nom du Défi (ex: Négociation Impériale)" style="flex:1;">' +
          '<input class="vtt-select" id="vttChgSteps" type="number" min="3" max="7" value="4" title="Étapes (3-7)" style="width:70px;">' +
          '<input class="vtt-select" id="vttChgRet" type="number" min="0" max="3" value="2" title="Rattrapages (0-3)" style="width:70px;">' +
          '<button class="vtt-btn on" id="vttChgNew" type="button">+ Lancer</button></div></div>';
      }
      if (!list.length) h += '<p class="tb-status">Aucun défi en cours.</p>';
      list.forEach((ch) => {
        h += '<div class="vtt-card" style="margin-bottom:6px;"><h4>' + esc(ch.title) + ' <span class="vtt-tag">' + esc(ch.status) + '</span></h4>' +
          '<div class="tb-status">Étapes réussies : <strong>' + ch.stepsOk + '/' + ch.totalSteps + '</strong> · Échecs : ' + ch.stepsFail + ' · Rattrapages : ' + ch.retriesUsed + '/' + ch.maxRetries +
          (ch.status !== 'active' ? (' · Récompense : <strong>' + ch.rewardCases + ' case(s)</strong>') : '') + '</div>' +
          (isGM && ch.status === 'active' ? (
            '<div style="display:flex;gap:4px;margin-top:6px;flex-wrap:wrap;">' +
            '<button class="vtt-mini-btn" data-cs-ok="' + esc(ch.id) + '" type="button">✓ Étape réussie</button>' +
            '<button class="vtt-mini-btn" data-cs-fail="' + esc(ch.id) + '" type="button">✕ Étape ratée</button>' +
            '<button class="vtt-mini-btn" data-cs-ret="' + esc(ch.id) + '" type="button">↻ Rattrapage</button>' +
            '<button class="vtt-btn emerald" data-cs-win="' + esc(ch.id) + '" type="button">🏆 Valider</button>' +
            '<button class="vtt-btn danger" data-cs-ko="' + esc(ch.id) + '" type="button">💀 Échec (0)</button>' +
            '</div>'
          ) : '') +
          (isGM ? '<button class="vtt-mini-btn" data-cs-del="' + esc(ch.id) + '" type="button" style="margin-top:4px;">🗑 Supprimer</button>' : '') +
          '</div>';
      });
      h += '</div>';
      back.innerHTML = h;
      back.querySelector('#vttChgClose').addEventListener('click', () => { unsub(); back.remove(); });
      if (isGM) {
        back.querySelector('#vttChgNew').addEventListener('click', () => {
          window.CodexVTT.sendOp('vtt_challenge', {
            action: 'create',
            title: back.querySelector('#vttChgTitle').value || 'Défi Hors-Combat',
            totalSteps: parseInt(back.querySelector('#vttChgSteps').value, 10) || 4,
            maxRetries: parseInt(back.querySelector('#vttChgRet').value, 10) || 2
          });
        });
        back.querySelectorAll('[data-cs-ok]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_challenge', { action: 'step', challengeId: b.getAttribute('data-cs-ok'), success: true })));
        back.querySelectorAll('[data-cs-fail]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_challenge', { action: 'step', challengeId: b.getAttribute('data-cs-fail'), success: false })));
        back.querySelectorAll('[data-cs-ret]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_challenge', { action: 'step', challengeId: b.getAttribute('data-cs-ret'), success: true, retry: true })));
        back.querySelectorAll('[data-cs-win]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_challenge', { action: 'finish', challengeId: b.getAttribute('data-cs-win'), success: true })));
        back.querySelectorAll('[data-cs-ko]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_challenge', { action: 'finish', challengeId: b.getAttribute('data-cs-ko'), success: false })));
        back.querySelectorAll('[data-cs-del]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_challenge', { action: 'delete', challengeId: b.getAttribute('data-cs-del') })));
      }
    }
    const unsub = window.CodexVTT.on('state', render);
    back.addEventListener('click', (e) => { if (e.target === back) { unsub(); back.remove(); } });
    render();
  }

  /* ---------- Lanceur de Sorts (XP = PA) & Techniques (Livre IV & III §15) ---------- */
  function openSpellCastModal(token, sceneId) {
    const back = document.createElement('div');
    back.className = 'vtt-modal-back';
    const mag = (token && token.attrs && token.attrs.mag) || 2;
    back.innerHTML = '<div class="vtt-modal">' +
      '<div class="vtt-modal-head"><h3>✨ Lancer Sort (XP = PA) / Technique</h3><button class="vtt-btn" id="vttSpClose" type="button">✕</button></div>' +
      '<div class="vtt-grid-2">' +
      '<div class="tb-field"><label>Nom du Sort / Technique</label><input id="vttSpName" value="Décharge Arcanique"></div>' +
      '<div class="tb-field"><label>Coût PA (= XP du sort)</label><input id="vttSpPa" type="number" min="1" max="12" value="3"></div>' +
      '<div class="tb-field"><label>Attribut MAG</label><input id="vttSpMag" type="number" min="0" max="10" value="' + mag + '"></div>' +
      '<div class="tb-field"><label>Entraînements Magie (E)</label><input id="vttSpTr" type="number" min="0" max="6" value="1"></div>' +
      '<div class="tb-field"><label>Effet / État appliqué</label><input id="vttSpEff" placeholder="Ex: En Feu 2, 4 dégâts"></div>' +
      '<div class="tb-field"><label>Portée calculée</label><input id="vttSpRange" readonly></div>' +
      '</div>' +
      '<button class="vtt-btn on" id="vttSpCast" type="button">✨ Déclencher (déduit PA + publie au chat)</button>' +
      '</div>';
    document.body.appendChild(back);
    const updRange = () => {
      const m = parseInt(back.querySelector('#vttSpMag').value, 10) || 0;
      const tr = parseInt(back.querySelector('#vttSpTr').value, 10) || 0;
      const p = m + m * tr;
      back.querySelector('#vttSpRange').value = p + ' cases (' + (p * 1.5) + ' m)';
    };
    updRange();
    back.querySelector('#vttSpMag').addEventListener('input', updRange);
    back.querySelector('#vttSpTr').addEventListener('input', updRange);
    back.querySelector('#vttSpClose').addEventListener('click', () => back.remove());
    back.addEventListener('click', (e) => { if (e.target === back) back.remove(); });
    back.querySelector('#vttSpCast').addEventListener('click', () => {
      const name = back.querySelector('#vttSpName').value.trim() || 'Sort';
      const paCost = parseInt(back.querySelector('#vttSpPa').value, 10) || 2;
      const tr = parseInt(back.querySelector('#vttSpTr').value, 10) || 0;
      const eff = back.querySelector('#vttSpEff').value.trim();
      const rangeStr = back.querySelector('#vttSpRange').value;
      if (token && sceneId && window.CodexVTT) {
        window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId, tokenId: token.id, patch: { pa: Math.max(0, (token.pa || 0) - paCost) } });
      }
      const rules = window.CodexVTTRules;
      const die = rules ? rules.skillDie(parseInt(back.querySelector('#vttSpMag').value, 10) || 2, tr) : 'd6';
      const roll = rules ? rules.executeRoll({ spec: rules.parseDieSpec(die), trainings: tr, reason: name }) : null;
      if (window.CodexTables && window.CodexTables.sendRichMessage) {
        window.CodexTables.sendRichMessage(
          '✨ ' + (token ? token.name + ' lance ' : '') + name + ' (' + paCost + ' PA, portée ' + rangeStr + ')' + (eff ? ' — ' + eff : ''),
          roll ? Object.assign({ kind: 'dice' }, roll) : { kind: 'action', action: { nom: name, cout_pa: paCost + ' PA', resume: eff } }
        );
      }
      back.remove();
    });
  }

  window.CodexVTTExtras = {
    syncAudioState,
    playSfx,
    openAudioModal,
    openCampaignToolsModal,
    openChallengeModal,
    openSpellCastModal
  };
})();
