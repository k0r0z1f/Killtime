/* ==========================================================================
   VTT UI — Contrôleur Salle Mixte, Tracker Initiative, Fiche Pion 6 zones, Dés
   js/vtt-ui.js
   ========================================================================== */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const esc = (s) => String(s || '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  let engine = null;
  let vttActive = false;
  let selectedTokenId = null;

  // Emplacements d'origine pour restaurer proprement #tbMessages/#tbComposer/#tbVoice/#tbManage
  const homeSlots = {};
  function rememberHome(id) {
    const el = $(id);
    if (!el || homeSlots[id]) return;
    const marker = document.createComment('home:' + id);
    el.parentNode.insertBefore(marker, el);
    homeSlots[id] = marker;
  }

  function mountDomPanelsToVtt() {
    ['tbMessages', 'tbUploadState', 'tbComposer', 'tbChatStatus', 'tbVoice', 'tbManage'].forEach(rememberHome);
    const cm = $('vttChatMount');
    if (cm) {
      if ($('tbUploadState')) cm.appendChild($('tbUploadState'));
      if ($('tbMessages')) cm.appendChild($('tbMessages'));
      if ($('tbComposer')) cm.appendChild($('tbComposer'));
      if ($('tbChatStatus')) cm.appendChild($('tbChatStatus'));
    }
    const vm = $('vttVoiceMount');
    if (vm && $('tbVoice')) vm.appendChild($('tbVoice'));
    const mm = $('vttManageMount');
    if (mm && $('tbManage')) mm.appendChild($('tbManage'));
  }

  function restoreDomPanelsFromVtt() {
    Object.keys(homeSlots).forEach((id) => {
      const el = $(id);
      const marker = homeSlots[id];
      if (el && marker && marker.parentNode) {
        marker.parentNode.insertBefore(el, marker.nextSibling);
      }
    });
  }

  function setVttMode(on) {
    vttActive = !!on;
    document.body.classList.toggle('vtt-active', vttActive);
    const btnChat = $('btnModeChat');
    const btnVtt = $('btnModeVtt');
    if (btnChat) btnChat.classList.toggle('on', !vttActive);
    if (btnVtt) btnVtt.classList.toggle('on', vttActive);
    if (vttActive) {
      mountDomPanelsToVtt();
      if (engine) setTimeout(() => { engine.dirty = true; engine.fitToScene(); }, 60);
    } else {
      restoreDomPanelsFromVtt();
    }
    try { localStorage.setItem('kt-tables-mode', vttActive ? 'vtt' : 'chat'); } catch (e) {}
  }

  function activeScene(st) {
    if (!st || !Array.isArray(st.scenes)) return null;
    return st.scenes.find((s) => s.id === st.activeSceneId) || st.scenes[0];
  }

  function renderTopAndInit() {
    const st = window.CodexVTT ? window.CodexVTT.getState() : null;
    const isGM = window.CodexVTT ? window.CodexVTT.isGM() : false;
    const sc = activeScene(st);

    const pauseBan = $('vttPauseBanner');
    if (pauseBan) pauseBan.style.display = (st && st.paused) ? '' : 'none';

    const sel = $('vttSceneSelect');
    if (sel && st) {
      sel.innerHTML = st.scenes.map((s) => '<option value="' + esc(s.id) + '"' + (s.id === st.activeSceneId ? ' selected' : '') + '>' + esc(s.name) + '</option>').join('');
      sel.disabled = !isGM;
    }

    const emptyEl = $('vttEmptyBoard');
    if (emptyEl) {
      emptyEl.style.display = (sc && (!sc.tokens || sc.tokens.length === 0)) ? '' : 'none';
    }

    ['vttBtnSceneNew', 'vttBtnSceneCfg', 'vttBtnPause', 'vttBtnTurnLock', 'vttBtnAmbush', 'vttBtnFocusAll', 'vttBtnStartCombat', 'vttBtnNextTurn', 'vttBtnPrevTurn', 'vttBtnResetPa'].forEach((id) => {
      const b = $(id);
      if (b) b.style.display = isGM ? '' : 'none';
    });
    if ($('vttBtnPause') && st) $('vttBtnPause').classList.toggle('amber', !!st.paused);
    if ($('vttBtnTurnLock') && st) $('vttBtnTurnLock').classList.toggle('on', !!st.turnLock);
    if ($('vttBtnStartCombat') && st) {
      $('vttBtnStartCombat').textContent = (st.combat && st.combat.active) ? '⏹ Fin combat' : '⚔️ Combat';
      $('vttBtnStartCombat').classList.toggle('danger', !!(st.combat && st.combat.active));
    }

    // Tracker d'initiative
    const strip = $('vttInitStrip');
    if (strip && st) {
      const c = st.combat || { active: false, round: 1, order: [], activeTokenId: null };
      const secs = (c.round || 1) * 10;
      let h = '<span class="vtt-round-badge">Tour ' + (c.round || 1) + ' (' + secs + ' s)</span>';
      if (!c.active || !c.order || !c.order.length) {
        h += '<span class="tb-status">Hors combat — prêt sur ' + esc(sc ? sc.name : '') + '</span>';
      } else {
        c.order.forEach((o) => {
          const tok = sc && sc.tokens.find((t) => t.id === o.tokenId);
          const col = (tok && tok.color) || '#38bdf8';
          const act = o.tokenId === c.activeTokenId;
          h += '<button type="button" class="vtt-init-chip' + (act ? ' active' : '') + '" data-init-tok="' + esc(o.tokenId) + '">' +
            '<span class="dot" style="background:' + esc(col) + '"></span>' +
            esc(o.name) + ' <code>' + esc(o.init) + '</code>' +
            (tok && !tok.statsMasked ? ' <span>(' + (tok.pa || 0) + 'PA)</span>' : '') +
            '</button>';
        });
      }
      strip.innerHTML = h;
      strip.querySelectorAll('[data-init-tok]').forEach((b) => {
        b.addEventListener('click', () => {
          const tid = b.getAttribute('data-init-tok');
          selectedTokenId = tid;
          if (engine) {
            engine.selectedTokenId = tid;
            const tok = sc && sc.tokens.find((t) => t.id === tid);
            if (tok) {
              const w = engine.cellToWorld(tok.q, tok.r);
              engine.focusOn(w.x, w.y);
            }
            engine.dirty = true;
          }
          renderTokenPane();
        });
      });
    }
  }

  function renderTokenPane() {
    const box = $('vttPaneTokens');
    if (!box) return;
    const st = window.CodexVTT ? window.CodexVTT.getState() : null;
    const isGM = window.CodexVTT ? window.CodexVTT.isGM() : false;
    const sc = activeScene(st);
    const rules = window.CodexVTTRules;
    const me = window.CodexTables && window.CodexTables.getMe ? window.CodexTables.getMe() : null;
    const myId = me && me.id;

    let h = '';
    if (isGM && sc) {
      h += '<div class="vtt-card"><h4>Nouveau pion</h4>' +
        '<div class="vtt-grid-2">' +
        '<input class="vtt-select" id="vttNewTokName" placeholder="Nom">' +
        '<select class="vtt-select" id="vttNewTokFac"><option value="pj">PJ</option><option value="ally">Allié</option><option value="npc">PNJ</option><option value="enemy">Ennemi</option><option value="vehicle">Véhicule</option></select>' +
        '<input class="vtt-select" id="vttNewTokCon" type="number" min="1" max="10" value="3" title="CON">' +
        '<input class="vtt-select" id="vttNewTokPa" type="number" min="1" max="25" value="7" title="PA max">' +
        '</div>' +
        '<div style="display:flex;gap:6px;margin-top:6px;align-items:center;">' +
        '<input id="vttNewTokCol" type="color" value="#38bdf8" style="width:36px;height:28px;border:none;background:none;">' +
        '<label style="font-size:11px;display:flex;gap:4px;align-items:center;"><input type="checkbox" id="vttNewTokGmOnly"> Calque MJ</label>' +
        '<button class="vtt-btn emerald" id="vttBtnAddTok" type="button" style="margin-left:auto;">+ Ajouter</button>' +
        '</div></div>';
    }

    const tok = sc && sc.tokens.find((t) => t.id === selectedTokenId);
    if (!tok) {
      h += '<div class="vtt-card"><p class="tb-status">Clique sur un pion du plateau pour afficher sa fiche tactique (PA, PE, 6 zones, états).</p>';
      if (sc && sc.tokens.length) {
        h += '<div style="display:flex;flex-wrap:wrap;gap:4px;margin-top:6px;">';
        sc.tokens.forEach((t) => {
          h += '<button class="vtt-mini-btn" data-pick-tok="' + esc(t.id) + '" type="button">' + esc(t.name) + '</button>';
        });
        h += '</div>';
      }
      h += '</div>';
      box.innerHTML = h;
    } else {
      const canEdit = isGM || (myId && tok.ownerUserId === myId);
      const canClaim = !tok.ownerUserId && (tok.faction === 'pj' || tok.faction === 'ally');
      const ev = (!tok.statsMasked && rules) ? rules.evaluateBodyDamage(tok) : null;

      h += '<div class="vtt-card"><h4>' + esc(tok.name) + ' <span class="tb-badge">' + esc(tok.faction) + '</span></h4>';
      if (canClaim) {
        h += '<button class="vtt-btn on" id="vttTokClaim" type="button" style="margin-bottom:8px;">✋ Revendiquer ce pion</button>';
      }
      if (tok.statsMasked) {
        h += '<p class="tb-status">État observé : <strong>' + esc(tok.healthLabel || 'Inconnu') + '</strong></p>';
      } else {
        h += '<div style="display:flex;gap:8px;flex-wrap:wrap;margin-bottom:8px;font-size:12px;">' +
          '<div><strong>PA:</strong> ' + (tok.pa || 0) + '/' + (tok.maxPa || 6) +
          (canEdit ? ' <button class="vtt-mini-btn" data-pa="-1">-1</button><button class="vtt-mini-btn" data-pa="1">+1</button>' : '') + '</div>' +
          '<div><strong>PE:</strong> ' + (tok.pe || 0) + '/' + (tok.maxPe || 3) +
          (canEdit ? ' <button class="vtt-mini-btn" data-pe="-1">-1</button><button class="vtt-mini-btn" data-pe="1">+1</button>' : '') + '</div>' +
          '<div><strong>Armure:</strong> ' + (tok.armor || 0) + ' · <strong>CON:</strong> ' + (tok.con || 3) + '</div>' +
          '</div>';

        if (ev) {
          if (ev.lethalReached) {
            h += '<div class="vtt-alert">☠️ SEUIL LÉTAL ATTEINT (' + ev.totalDamage + '/' + ev.lethal + ') — Arbre Dernier Souffle / Mort (§23.2)</div>';
          } else if (ev.traumaZones.length) {
            h += '<div class="vtt-alert">⚠️ Choc Traumatique (&gt; CON×2 = ' + ev.cap + ') : ' +
              ev.traumaZones.map((z) => z.label + ' (Diff ' + z.shockDiff + ')').join(', ') + '</div>';
          }
          h += '<div style="margin-top:8px;">';
          rules.BODY_ZONES.forEach((bz) => {
            const val = (tok.zones && tok.zones[bz.id]) || 0;
            const trauma = val > ev.cap;
            h += '<div class="vtt-zone-row' + (trauma ? ' trauma' : '') + '">' +
              '<span>' + esc(bz.label) + '</span>' +
              '<span><strong>' + val + '</strong>/' + ev.cap +
              (canEdit ? ' <button class="vtt-mini-btn" data-zd="' + bz.id + '" data-dv="-1">-</button><button class="vtt-mini-btn" data-zd="' + bz.id + '" data-dv="1">+</button><button class="vtt-mini-btn" data-zd="' + bz.id + '" data-dv="3">+3</button>' : '') +
              '</span></div>';
          });
          h += '<div class="tb-status">Cumul total : ' + ev.totalDamage + ' / ' + ev.lethal + ' (' + esc(ev.healthLabel) + ')</div></div>';
        }
      }

      // États + Résiduels + Toggles sensoriels
      h += '<div style="margin-top:8px;"><strong style="font-size:11px;">États actifs</strong><div class="vtt-status-pills">';
      (tok.statuses || []).forEach((s) => {
        const tip = (window.CodexVTTCompendium && window.CodexVTTCompendium.getStatusTip(s)) || s;
        h += '<span class="vtt-st-pill" title="' + esc(tip) + '">' + esc(s) + (canEdit ? ' <button data-rm-st="' + esc(s) + '">✕</button>' : '') + '</span>';
      });
      h += '</div>';
      if (canEdit && rules) {
        const allSt = [...rules.STATUS_PREJUDICIAL, ...rules.STATUS_BENEFICIAL];
        h += '<div style="display:flex;gap:4px;margin-top:6px;">' +
          '<select class="vtt-select" id="vttAddStatusSel" style="flex:1;">' +
          allSt.map((s) => '<option value="' + esc(s) + '">' + esc(s) + '</option>').join('') +
          '</select><button class="vtt-mini-btn" id="vttAddStatusBtn" type="button">+ État</button></div>';

        h += '<div style="margin-top:8px;"><strong style="font-size:11px;">Dégâts Résiduels (−1/tour)</strong><div class="vtt-grid-2" style="margin-top:4px;">';
        rules.STATUS_DOT.forEach((dn) => {
          const dv = (tok.dots && tok.dots[dn]) || 0;
          h += '<div class="vtt-zone-row" style="margin-bottom:0;"><span>' + esc(dn) + '</span><span><strong>' + dv + '</strong> <button class="vtt-mini-btn" data-dot="' + esc(dn) + '" data-dv="-1">-</button><button class="vtt-mini-btn" data-dot="' + esc(dn) + '" data-dv="1">+</button></span></div>';
        });
        h += '</div></div>';

        h += '<div style="display:flex;gap:6px;flex-wrap:wrap;margin-top:8px;">' +
          '<button class="vtt-mini-btn' + (tok.lightOn ? ' on' : '') + '" id="vttTokLight" type="button">🔦 Lampe 10P</button>' +
          '<button class="vtt-mini-btn' + (tok.thermal ? ' on' : '') + '" id="vttTokTherm" type="button">👁️ Thermique</button>' +
          '<button class="vtt-mini-btn' + (tok.vigilant ? ' on' : '') + '" id="vttTokVig" type="button">🛡️ Vigilant</button>' +
          '</div>';
      }
      h += '</div>';

      // Réactions hors tour & Face-à-Face & XP
      if (canEdit && !tok.statsMasked) {
        h += '<div style="margin-top:10px;border-top:1px dashed var(--border-subtle);padding-top:8px;">' +
          '<strong style="font-size:11px;">Réactions hors tour</strong>' +
          '<div style="display:flex;gap:4px;flex-wrap:wrap;margin-top:4px;">' +
          '<button class="vtt-mini-btn" data-react="guet" data-pa="1" type="button">Guet (1PA)</button>' +
          '<button class="vtt-mini-btn" data-react="opportunite" data-pa="2" data-free="' + (!tok.freeReactionUsed ? '1' : '0') + '" type="button">' + (!tok.freeReactionUsed ? 'Opport. (Libre)' : 'Opport. (2PA)') + '</button>' +
          '<button class="vtt-mini-btn" data-react="prise_de_coup" data-pa="1" type="button">Prise coup (1PA)</button>' +
          '<button class="vtt-mini-btn" data-react="contre_sort" data-pa="2" type="button">Contre-sort (2PA)</button>' +
          '</div>' +
          '<div style="display:flex;gap:6px;margin-top:6px;align-items:center;">' +
          '<button class="vtt-btn amber" id="vttTokDuelBtn" type="button">⚔️ Face-à-Face</button>' +
          '<span class="tb-status">XP : <strong>' + (tok.xpTicks || 0) + '</strong> <button class="vtt-mini-btn" id="vttTokXpBtn" type="button">+1</button></span>' +
          '</div></div>';

        // Inventaire DLPH & Portage
        const inv = tok.inventory || [];
        const totalL = inv.reduce((acc, it) => acc + (Number(it.poids_livres) || 0) * (it.quantite || 1), 0);
        const capFor = ((tok.attrs && tok.attrs.for) || tok.con || 3) * 15;
        h += '<div style="margin-top:10px;border-top:1px dashed var(--border-subtle);padding-top:8px;">' +
          '<strong style="font-size:11px;">Inventaire DLPH (' + totalL.toFixed(1) + ' / ' + capFor + ' L)</strong>';
        if (!inv.length) h += '<div class="tb-status">Vide — ajoute depuis 📚 Comp.</div>';
        inv.forEach((it) => {
          h += '<div class="vtt-zone-row" style="flex-direction:column;align-items:stretch;gap:3px;">' +
            '<div style="display:flex;justify-content:space-between;align-items:center;"><span><strong>' + esc(it.nom) + '</strong> ×' + (it.quantite || 1) + '</span>' +
            '<span><button class="vtt-mini-btn" data-it-drop="' + esc(it.id) + '" type="button" title="Lâcher au sol">⬇</button> <button class="vtt-mini-btn" data-it-del="' + esc(it.id) + '" type="button">✕</button></span></div>' +
            '<div style="display:flex;gap:4px;align-items:center;font-size:11px;">' +
            (it.munitions !== null && it.munitions !== undefined ? ('Mun: <strong>' + it.munitions + '</strong> <button class="vtt-mini-btn" data-it-ammo="' + esc(it.id) + '" data-dv="-1">-</button><button class="vtt-mini-btn" data-it-ammo="' + esc(it.id) + '" data-dv="1">+</button>') : '') +
            '<select class="vtt-select" data-it-state="' + esc(it.id) + '" style="margin-left:auto;padding:2px 4px;font-size:10px;">' +
            ['Neuf', 'Usé', 'Endommagé', 'Hors service', 'Détruit'].map((stN) => '<option value="' + stN + '"' + (it.etatObjet === stN ? ' selected' : '') + '>' + stN + '</option>').join('') +
            '</select></div></div>';
        });
        const nearDrops = (sc.drops || []).filter((d) => Math.abs(d.q - tok.q) <= 1 && Math.abs(d.r - tok.r) <= 1);
        nearDrops.forEach((d) => {
          h += '<div class="vtt-zone-row"><span>🎒 ' + esc(d.name) + '</span><button class="vtt-mini-btn" data-pick-drop="' + esc(d.id) + '" type="button">✋ Ramasser (1PA)</button></div>';
        });
        h += '</div>';
      }

      // Section Véhicule (Livre V §22.3)
      if (tok.faction === 'vehicle' && tok.vehicle) {
        const v = tok.vehicle;
        h += '<div style="margin-top:10px;border-top:1px dashed var(--border-subtle);padding-top:8px;">' +
          '<strong style="font-size:11px;">🚜 Véhicule (' + esc(v.caliberClass || 'Léger') + ')</strong>' +
          '<div class="vtt-zone-row"><span>Zones Essentielles</span><span><strong>' + v.essentialDmg + '</strong>/' + v.essentialMax +
          ' <button class="vtt-mini-btn" data-veh="ess" data-dv="-1">-</button><button class="vtt-mini-btn" data-veh="ess" data-dv="1">+</button></span></div>' +
          '<div class="vtt-zone-row"><span>Zones Superficielles</span><span><strong>' + v.superficialDmg + '</strong>/' + v.superficialMax +
          ' <button class="vtt-mini-btn" data-veh="sup" data-dv="-1">-</button><button class="vtt-mini-btn" data-veh="sup" data-dv="1">+</button></span></div>' +
          '<button class="vtt-btn danger" id="vttVehEvac" type="button" style="width:100%;margin-top:4px;">🚨 Évacuation (Déstabilisé + À Terre)</button>' +
          '</div>';
      }

      if (isGM) {
        h += '<div style="display:flex;gap:6px;margin-top:10px;flex-wrap:wrap;">' +
          '<button class="vtt-mini-btn" id="vttTokToggleGm" type="button">' + (tok.gmOnly ? '👁 Révéler aux joueurs' : '🔒 Mettre calque MJ') + '</button>' +
          '<button class="vtt-mini-btn" id="vttTokDel" type="button" style="color:var(--vital-ruby);">🗑 Supprimer</button>' +
          '</div>';
      }
      h += '</div>';
      box.innerHTML = h;
    }

    // Bindings pane Tokens
    const addBtn = $('vttBtnAddTok');
    if (addBtn) addBtn.addEventListener('click', () => {
      const name = ($('vttNewTokName').value || '').trim() || 'Pion';
      const faction = $('vttNewTokFac').value;
      const con = parseInt($('vttNewTokCon').value, 10) || 3;
      const maxPa = parseInt($('vttNewTokPa').value, 10) || 7;
      const color = $('vttNewTokCol').value || '#38bdf8';
      const gmOnly = !!($('vttNewTokGmOnly') && $('vttNewTokGmOnly').checked);
      window.CodexVTT.sendOp('vtt_token', {
        action: 'add', sceneId: sc.id,
        token: { name, faction, con, maxPa, pa: maxPa, maxPe: con, pe: 0, color, gmOnly, q: 3, r: 3 }
      });
    });

    box.querySelectorAll('[data-pick-tok]').forEach((b) => b.addEventListener('click', () => {
      selectedTokenId = b.getAttribute('data-pick-tok');
      if (engine) { engine.selectedTokenId = selectedTokenId; engine.dirty = true; }
      renderTokenPane();
    }));

    if (tok) {
      const cl = $('vttTokClaim');
      if (cl) cl.addEventListener('click', () => window.CodexVTT.sendOp('vtt_token', { action: 'claim', sceneId: sc.id, tokenId: tok.id }));
      box.querySelectorAll('[data-pa]').forEach((b) => b.addEventListener('click', () => {
        const d = parseInt(b.getAttribute('data-pa'), 10);
        window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: tok.id, patch: { pa: Math.max(0, (tok.pa || 0) + d) } });
      }));
      box.querySelectorAll('[data-pe]').forEach((b) => b.addEventListener('click', () => {
        const d = parseInt(b.getAttribute('data-pe'), 10);
        window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: tok.id, patch: { pe: Math.max(0, (tok.pe || 0) + d) } });
      }));
      box.querySelectorAll('[data-zd]').forEach((b) => b.addEventListener('click', () => {
        const zid = b.getAttribute('data-zd');
        const dv = parseInt(b.getAttribute('data-dv'), 10);
        const nz = Object.assign({}, tok.zones || {});
        nz[zid] = Math.max(0, (nz[zid] || 0) + dv);
        window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: tok.id, patch: { zones: nz } });
      }));
      box.querySelectorAll('[data-rm-st]').forEach((b) => b.addEventListener('click', () => {
        const stName = b.getAttribute('data-rm-st');
        window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: tok.id, patch: { statuses: (tok.statuses || []).filter((x) => x !== stName) } });
      }));
      const addSt = $('vttAddStatusBtn');
      if (addSt) addSt.addEventListener('click', () => {
        const val = $('vttAddStatusSel').value;
        const next = [...new Set([...(tok.statuses || []), val])];
        window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: tok.id, patch: { statuses: next } });
      });
      box.querySelectorAll('[data-dot]').forEach((b) => b.addEventListener('click', () => {
        const dn = b.getAttribute('data-dot');
        const dv = parseInt(b.getAttribute('data-dv'), 10);
        const nd = Object.assign({}, tok.dots || {});
        const nv = Math.max(0, (nd[dn] || 0) + dv);
        if (nv > 0) nd[dn] = nv; else delete nd[dn];
        window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: tok.id, patch: { dots: nd } });
      }));
      if ($('vttTokLight')) $('vttTokLight').addEventListener('click', () => window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: tok.id, patch: { lightOn: !tok.lightOn } }));
      if ($('vttTokTherm')) $('vttTokTherm').addEventListener('click', () => window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: tok.id, patch: { thermal: !tok.thermal } }));
      if ($('vttTokVig')) $('vttTokVig').addEventListener('click', () => window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: tok.id, patch: { vigilant: !tok.vigilant } }));
      box.querySelectorAll('[data-react]').forEach((b) => b.addEventListener('click', () => {
        if (window.CodexVTTCombat) {
          window.CodexVTTCombat.triggerReaction(tok, b.getAttribute('data-react'), parseInt(b.getAttribute('data-pa'), 10), b.getAttribute('data-free') === '1');
        }
      }));
      if ($('vttTokDuelBtn')) $('vttTokDuelBtn').addEventListener('click', () => {
        if (window.CodexVTTCombat) window.CodexVTTCombat.openDuelModal(tok.id, null);
      });
      if ($('vttTokXpBtn')) $('vttTokXpBtn').addEventListener('click', () => window.CodexVTT.sendOp('vtt_token', { action: 'tick_xp', sceneId: sc.id, tokenId: tok.id }));
      box.querySelectorAll('[data-it-drop]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_token', { action: 'drop_item', sceneId: sc.id, tokenId: tok.id, itemId: b.getAttribute('data-it-drop') })));
      box.querySelectorAll('[data-it-del]').forEach((b) => b.addEventListener('click', () => window.CodexVTT.sendOp('vtt_token', { action: 'remove_item', sceneId: sc.id, tokenId: tok.id, itemId: b.getAttribute('data-it-del') })));
      box.querySelectorAll('[data-it-ammo]').forEach((b) => b.addEventListener('click', () => {
        const iid = b.getAttribute('data-it-ammo');
        const it = (tok.inventory || []).find((x) => x.id === iid);
        if (it) window.CodexVTT.sendOp('vtt_token', { action: 'update_item', sceneId: sc.id, tokenId: tok.id, itemId: iid, munitions: Math.max(0, (it.munitions || 0) + parseInt(b.getAttribute('data-dv'), 10)) });
      }));
      box.querySelectorAll('[data-it-state]').forEach((sel) => sel.addEventListener('change', () => {
        window.CodexVTT.sendOp('vtt_token', { action: 'update_item', sceneId: sc.id, tokenId: tok.id, itemId: sel.getAttribute('data-it-state'), etatObjet: sel.value });
      }));
      box.querySelectorAll('[data-pick-drop]').forEach((b) => b.addEventListener('click', () => {
        window.CodexVTT.sendOp('vtt_token', { action: 'pickup_drop', sceneId: sc.id, tokenId: tok.id, dropId: b.getAttribute('data-pick-drop') });
      }));
      box.querySelectorAll('[data-veh]').forEach((b) => b.addEventListener('click', () => {
        const k = b.getAttribute('data-veh');
        const dv = parseInt(b.getAttribute('data-dv'), 10);
        const nv = Object.assign({}, tok.vehicle);
        if (k === 'ess') nv.essentialDmg = Math.max(0, (nv.essentialDmg || 0) + dv);
        else nv.superficialDmg = Math.max(0, (nv.superficialDmg || 0) + dv);
        window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: tok.id, patch: { vehicle: nv } });
      }));
      if ($('vttVehEvac')) $('vttVehEvac').addEventListener('click', () => {
        window.CodexVTT.sendOp('vtt_token', { action: 'evacuate_vehicle', sceneId: sc.id, tokenId: tok.id });
      });
      const tg = $('vttTokToggleGm');
      if (tg) tg.addEventListener('click', () => {
        window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: tok.id, patch: { gmOnly: !tok.gmOnly } });
      });
      const td = $('vttTokDel');
      if (td) td.addEventListener('click', () => {
        if (confirm('Supprimer ' + tok.name + ' ?')) {
          window.CodexVTT.sendOp('vtt_token', { action: 'delete', sceneId: sc.id, tokenId: tok.id });
          selectedTokenId = null;
        }
      });
    }
  }

  function renderDicePane() {
    const box = $('vttPaneDice');
    const rules = window.CodexVTTRules;
    if (!box || !rules) return;
    const isGM = window.CodexVTT ? window.CodexVTT.isGM() : false;

    let h = '<div class="vtt-card"><h4>Lanceur SysRP (Livre II §6)</h4>' +
      '<div class="vtt-grid-2">' +
      '<select class="vtt-select" id="vttRollDie">' +
      rules.DICE_SCALE.map((d) => '<option value="' + d + '"' + (d === 'd6' ? ' selected' : '') + '>' + d + '</option>').join('') +
      '</select>' +
      '<input class="vtt-select" id="vttRollTarget" type="number" min="0" max="34" placeholder="Seuil (0-34)">' +
      '<input class="vtt-select" id="vttRollEffort" type="number" min="0" max="15" value="0" placeholder="Effort PA/PE">' +
      '<input class="vtt-select" id="vttRollMalus" type="number" min="0" max="30" value="0" placeholder="Malus">' +
      '<input class="vtt-select" id="vttRollTrain" type="number" min="0" max="6" value="0" title="Entraînements (plafond 1+entr)">' +
      '<input class="vtt-select" id="vttRollReason" placeholder="Action / Compétence">' +
      '</div>' +
      '<div style="display:flex;gap:8px;align-items:center;margin-top:8px;">' +
      (isGM ? '<label style="font-size:11px;display:flex;gap:4px;align-items:center;"><input type="checkbox" id="vttRollSecret"> 🔒 Secret MJ</label>' : '') +
      '<button class="vtt-btn on" id="vttBtnRollNow" type="button" style="margin-left:auto;">🎲 Lancer au chat</button>' +
      '</div></div>';

    if (isGM) {
      const doc = window.CodexTables && window.CodexTables.getActiveDoc ? window.CodexTables.getActiveDoc() : null;
      const members = (doc && doc.members) || [];
      h += '<div class="vtt-card"><h4>Demande d’épreuve MJ</h4>' +
        '<div class="vtt-grid-2">' +
        '<input class="vtt-select" id="vttChkSkill" placeholder="Compétence (ex: Observation)">' +
        '<input class="vtt-select" id="vttChkTarget" type="number" min="0" max="34" value="4" placeholder="Difficulté">' +
        '</div>' +
        '<div style="display:flex;gap:6px;margin-top:6px;">' +
        '<select class="vtt-select" id="vttChkTo" style="flex:1;"><option value="">Tous les joueurs</option>' +
        members.map((m) => '<option value="' + esc(m.userId) + '">' + esc(m.username) + '</option>').join('') +
        '</select><button class="vtt-btn amber" id="vttBtnAskCheck" type="button">📩 Envoyer</button></div></div>';
    }

    h += '<div class="vtt-card"><h4>Table des Critiques (Livre II chap. 9)</h4>' +
      '<p class="tb-status">Relance ton palier de dé pour lire la conséquence (palier égal ou inférieur).</p>' +
      '<button class="vtt-btn" id="vttBtnOpenCritModal" type="button">⚡ Ouvrir la Table Complète</button></div>';

    box.innerHTML = h;

    $('vttBtnRollNow').addEventListener('click', () => {
      const formula = $('vttRollDie').value;
      const tRaw = $('vttRollTarget').value.trim();
      const target = tRaw === '' ? null : parseInt(tRaw, 10);
      const effort = parseInt($('vttRollEffort').value, 10) || 0;
      const malus = parseInt($('vttRollMalus').value, 10) || 0;
      const trainings = parseInt($('vttRollTrain').value, 10) || 0;
      const reason = $('vttRollReason').value.trim();
      const secret = !!($('vttRollSecret') && $('vttRollSecret').checked);
      const res = rules.executeRoll({ spec: rules.parseDieSpec(formula), effort, malus, target, trainings, secret, reason });
      if (window.CodexTables && window.CodexTables.sendRichMessage) {
        const summary = (secret ? '🔒 [Secret MJ] ' : '') + '🎲 ' + res.formula + (res.reason ? ' (' + res.reason + ')' : '') + ' → ' + res.total;
        window.CodexTables.sendRichMessage(summary, Object.assign({ kind: 'dice' }, res));
      }
    });

    const askBtn = $('vttBtnAskCheck');
    if (askBtn) askBtn.addEventListener('click', () => {
      const skill = $('vttChkSkill').value.trim() || 'Épreuve';
      const target = parseInt($('vttChkTarget').value, 10) || 4;
      const toUid = $('vttChkTo').value || null;
      const toName = toUid ? $('vttChkTo').selectedOptions[0].textContent : null;
      if (window.CodexTables && window.CodexTables.sendRichMessage) {
        window.CodexTables.sendRichMessage(
          '📋 Épreuve demandée : ' + skill + ' (Diff ' + target + ')' + (toName ? ' → ' + toName : ''),
          { kind: 'check_request', check: { skill, die: 'd6', target, toUserId: toUid, toName } }
        );
      }
    });

    $('vttBtnOpenCritModal').addEventListener('click', () => openCritModal(null));
  }

  function openCritModal(initialRoll) {
    const rules = window.CodexVTTRules;
    if (!rules) return;
    const hit = initialRoll ? rules.lookupCritTable(initialRoll) : null;
    const back = document.createElement('div');
    back.className = 'vtt-modal-back';
    back.innerHTML = '<div class="vtt-modal">' +
      '<div class="vtt-modal-head"><h3>⚡ Table des Critiques (Livre II chap. 9)</h3>' +
      '<button class="vtt-btn" id="vttCritClose" type="button">✕</button></div>' +
      '<div style="display:flex;gap:8px;align-items:center;margin-bottom:10px;flex-wrap:wrap;">' +
      '<select class="vtt-select" id="vttCritRollDie">' +
      rules.DICE_SCALE.map((d) => '<option value="' + d + '"' + (d === 'd8' ? ' selected' : '') + '>' + d + '</option>').join('') +
      '</select><button class="vtt-btn amber" id="vttCritRollBtn" type="button">🎲 Tirer la conséquence</button>' +
      '<span id="vttCritRes" class="tb-status"></span></div>' +
      '<table class="vtt-crit-table"><thead><tr><th>Dé</th><th>Offensif</th><th>Défensif</th><th>Social</th><th>Utilitaire</th></tr></thead><tbody>' +
      rules.CRIT_TABLE.map((row) => '<tr class="' + (hit && hit.val === row.val ? 'hit' : '') + '"><td><strong>' + row.val + '</strong></td><td>' + esc(row.offensif) + '</td><td>' + esc(row.defensif) + '</td><td>' + esc(row.social) + '</td><td>' + esc(row.utilitaire) + '</td></tr>').join('') +
      '</tbody></table></div>';
    document.body.appendChild(back);
    back.querySelector('#vttCritClose').addEventListener('click', () => back.remove());
    back.addEventListener('click', (e) => { if (e.target === back) back.remove(); });
    back.querySelector('#vttCritRollBtn').addEventListener('click', () => {
      const d = back.querySelector('#vttCritRollDie').value;
      const res = rules.executeRoll({ spec: rules.parseDieSpec(d), reason: 'Conséquence Critique' });
      const entry = rules.lookupCritTable(res.total);
      back.querySelector('#vttCritRes').textContent = 'Tirage ' + res.total + ' (palier ' + entry.val + ') : ' + entry.offensif;
      back.querySelectorAll('tbody tr').forEach((tr, i) => {
        tr.classList.toggle('hit', rules.CRIT_TABLE[i].val === entry.val);
      });
    });
  }

  function openSceneModal() {
    const st = window.CodexVTT && window.CodexVTT.getState();
    const sc = activeScene(st);
    if (!sc) return;
    const back = document.createElement('div');
    back.className = 'vtt-modal-back';
    back.innerHTML = '<div class="vtt-modal">' +
      '<div class="vtt-modal-head"><h3>⚙ Configuration de la Scène</h3><button class="vtt-btn" id="vttScClose" type="button">✕</button></div>' +
      '<div class="vtt-grid-2">' +
      '<div class="tb-field"><label>Nom</label><input id="vttScName" value="' + esc(sc.name) + '"></div>' +
      '<div class="tb-field"><label>Grille (1 case = 1,5 m)</label><select id="vttScGrid"><option value="hex"' + (sc.gridType === 'hex' ? ' selected' : '') + '>Hexagonale</option><option value="square"' + (sc.gridType === 'square' ? ' selected' : '') + '>Carrée</option></select></div>' +
      '<div class="tb-field"><label>Taille case (px)</label><input id="vttScCell" type="number" min="20" max="180" value="' + sc.cellSize + '"></div>' +
      '<div class="tb-field"><label>Opacité grille (0-1)</label><input id="vttScOp" type="number" step="0.05" min="0" max="1" value="' + (sc.gridOpacity ?? 0.35) + '"></div>' +
      '<div class="tb-field"><label>Colonnes</label><input id="vttScCols" type="number" min="6" max="120" value="' + sc.cols + '"></div>' +
      '<div class="tb-field"><label>Lignes</label><input id="vttScRows" type="number" min="6" max="120" value="' + sc.rows + '"></div>' +
      '<div class="tb-field"><label>Offset X</label><input id="vttScOx" type="number" value="' + (sc.offsetX || 0) + '"></div>' +
      '<div class="tb-field"><label>Offset Y</label><input id="vttScOy" type="number" value="' + (sc.offsetY || 0) + '"></div>' +
      '<div class="tb-field"><label>Portée vue (6 courte / 12 longue)</label><input id="vttScSight" type="number" min="1" max="32" value="' + (sc.sightRange || 12) + '"></div>' +
      '<div class="tb-field"><label style="display:flex;gap:6px;align-items:center;margin-top:22px;"><input type="checkbox" id="vttScFog"' + (sc.fogEnabled ? ' checked' : '') + ' style="width:auto;"> Brouillard 360° actif</label></div>' +
      '</div>' +
      '<div class="tb-field"><label>Battlemap URL (http/https)</label><input id="vttScUrl" value="' + esc(sc.mapUrl || '') + '" placeholder="https://..."></div>' +
      '<div class="tb-field"><label>…ou fichier battlemap local (PNG/JPG/WebP ≤ 3,5 Mo)</label><input id="vttScFile" type="file" accept="image/png,image/jpeg,image/webp"></div>' +
      '<div style="display:flex;gap:8px;justify-content:flex-end;margin-top:12px;">' +
      '<button class="vtt-btn danger" id="vttScClearMap" type="button">Retirer carte</button>' +
      '<button class="vtt-btn on" id="vttScSave" type="button">Enregistrer</button>' +
      '</div></div>';
    document.body.appendChild(back);
    back.querySelector('#vttScClose').addEventListener('click', () => back.remove());
    back.querySelector('#vttScClearMap').addEventListener('click', async () => {
      await window.CodexVTT.uploadMap(sc.id, { clear: true });
      back.remove();
    });
    back.querySelector('#vttScSave').addEventListener('click', async () => {
      await window.CodexVTT.sendOp('vtt_scene', {
        action: 'update',
        sceneId: sc.id,
        name: back.querySelector('#vttScName').value,
        gridType: back.querySelector('#vttScGrid').value,
        cellSize: parseFloat(back.querySelector('#vttScCell').value),
        gridOpacity: parseFloat(back.querySelector('#vttScOp').value),
        cols: parseInt(back.querySelector('#vttScCols').value, 10),
        rows: parseInt(back.querySelector('#vttScRows').value, 10),
        offsetX: parseFloat(back.querySelector('#vttScOx').value),
        offsetY: parseFloat(back.querySelector('#vttScOy').value),
        sightRange: parseInt(back.querySelector('#vttScSight').value, 10),
        fogEnabled: back.querySelector('#vttScFog').checked
      });
      const urlVal = back.querySelector('#vttScUrl').value.trim();
      const file = back.querySelector('#vttScFile').files[0];
      if (file) {
        const rd = new FileReader();
        rd.onload = async () => {
          await window.CodexVTT.uploadMap(sc.id, { imageData: rd.result });
          back.remove();
        };
        rd.readAsDataURL(file);
        return;
      } else if (urlVal && urlVal !== sc.mapUrl) {
        await window.CodexVTT.uploadMap(sc.id, { imageUrl: urlVal });
      }
      back.remove();
    });
  }

  function renderMacroBar() {
    const bar = $('vttMacroBar');
    if (!bar) return;
    const st = window.CodexVTT ? window.CodexVTT.getState() : null;
    const sc = activeScene(st);
    const tok = sc && sc.tokens.find((t) => t.id === selectedTokenId);
    let saved = [];
    try { saved = JSON.parse(localStorage.getItem('kt-vtt-macros') || '[]') || []; } catch (e) {}
    let h = '';
    if (tok && Array.isArray(tok.inventory)) {
      tok.inventory.filter((it) => it.degats > 0).slice(0, 3).forEach((it, idx) => {
        h += '<button class="vtt-flt-btn" data-mac-wep="' + idx + '" type="button">⚔️ ' + esc(it.nom) + ' (' + it.degats + ')</button>';
      });
    }
    saved.forEach((m, idx) => {
      h += '<button class="vtt-flt-btn" data-mac-usr="' + idx + '" type="button" title="' + esc(m.cmd) + '">' + esc(m.label) + '</button>';
    });
    h += '<button class="vtt-flt-btn" id="vttAddMacroBtn" type="button">+ Macro</button>';
    bar.innerHTML = h;
    bar.querySelectorAll('[data-mac-wep]').forEach((b) => b.addEventListener('click', () => {
      const it = tok.inventory.filter((x) => x.degats > 0)[parseInt(b.getAttribute('data-mac-wep'), 10)];
      if (it && window.CodexVTTRules && window.CodexTables && window.CodexTables.sendRichMessage) {
        const res = window.CodexVTTRules.executeRoll({ spec: window.CodexVTTRules.parseDieSpec('d6'), reason: it.nom + ' (' + tok.name + ')' });
        window.CodexTables.sendRichMessage('⚔️ ' + tok.name + ' — ' + it.nom + ' → ' + res.total, Object.assign({ kind: 'dice' }, res));
      }
    }));
    bar.querySelectorAll('[data-mac-usr]').forEach((b) => b.addEventListener('click', () => {
      const m = saved[parseInt(b.getAttribute('data-mac-usr'), 10)];
      if (!m) return;
      const ta = $('tbText');
      if (ta) { ta.value = m.cmd; const sb = $('tbSend'); if (sb) sb.click(); }
    }));
    const addBtn = $('vttAddMacroBtn');
    if (addBtn) addBtn.addEventListener('click', () => {
      const label = prompt('Libellé macro (ex: Esquive) :');
      if (!label || !label.trim()) return;
      const cmd = prompt('Commande / texte (ex: /r d8 +1 Esquive) :', '/r d6');
      if (!cmd || !cmd.trim()) return;
      saved.push({ label: label.trim().slice(0, 20), cmd: cmd.trim().slice(0, 120) });
      try { localStorage.setItem('kt-vtt-macros', JSON.stringify(saved.slice(-12))); } catch (e) {}
      renderMacroBar();
    });
  }

  function init() {
    const canvas = $('vttCanvas');
    if (!canvas || !window.CodexVTTCanvas) return;

    engine = new window.CodexVTTCanvas(canvas);
    engine.onSelectToken = (tok) => {
      selectedTokenId = tok ? tok.id : null;
      renderTokenPane();
    };
    engine.onHoverCell = (cell) => {
      const el = $('vttHudCoords');
      if (el && cell) el.textContent = 'Case (' + cell.q + ', ' + cell.r + ') · 1P = 1,5 m';
    };
    engine.onOp = (op, payload) => {
      if (window.CodexVTT) window.CodexVTT.sendOp(op, payload);
    };

    if (window.CodexVTT) {
      window.CodexVTT.on('state', (d) => {
        const me = window.CodexTables && window.CodexTables.getMe ? window.CodexTables.getMe() : null;
        engine.setState(d.state, { isGM: d.isGM, userId: me && me.id });
        renderTopAndInit();
        renderTokenPane();
        renderDicePane();
        renderMacroBar();
        if (window.CodexVTTExtras) window.CodexVTTExtras.syncAudioState(d.state);
        if (window.CodexVTTCompendium && $('vttPaneComp') && $('vttPaneComp').classList.contains('on')) {
          window.CodexVTTCompendium.renderCompendium($('vttPaneComp'), { selectedTokenId });
        }
      });
      window.CodexVTT.on('event', (msg) => {
        if (msg.op === 'vtt_ping' && msg.payload) engine.addPing(msg.payload);
        if (msg.op === 'vtt_focus_camera' && msg.payload) engine.focusOn(msg.payload.x, msg.payload.y, msg.payload.zoom);
        if (msg.op === 'vtt_handout' && msg.payload && msg.payload.op === 'vtt_handout_show' && window.CodexVTTCombat) {
          window.CodexVTTCombat.showHandoutOverlay(msg.payload.handout);
        }
        if (msg.op === 'vtt_audio' && msg.payload && msg.payload.op === 'vtt_sfx' && window.CodexVTTExtras) {
          window.CodexVTTExtras.playSfx(msg.payload.sfx);
        }
      });
      window.CodexVTT.on('status', (d) => {
        const pill = $('vttWsStatus');
        if (!pill) return;
        const map = { connected: ['WS Connecté', 'ok'], connecting: ['WS Connexion…', 'warn'], reconnecting: ['WS Reconnexion…', 'warn'], offline: ['WS Hors ligne', 'err'] };
        const [txt, cls] = map[d.status] || ['WS —', ''];
        pill.textContent = txt;
        pill.className = 'vtt-ws-pill ' + cls;
      });
    }

    // Boutons mode Chat <-> VTT
    if ($('btnModeChat')) $('btnModeChat').addEventListener('click', () => setVttMode(false));
    if ($('btnModeVtt')) $('btnModeVtt').addEventListener('click', () => setVttMode(true));
    if ($('vttBtnBackTables')) $('vttBtnBackTables').addEventListener('click', () => setVttMode(false));
    if ($('vttBtnToggleSide')) $('vttBtnToggleSide').addEventListener('click', () => {
      $('vttMain').classList.toggle('sidebar-collapsed');
      setTimeout(() => { engine.dirty = true; }, 40);
    });

    // Outils barre gauche
    document.querySelectorAll('[data-vtt-tool]').forEach((b) => {
      b.addEventListener('click', () => {
        document.querySelectorAll('[data-vtt-tool]').forEach((x) => x.classList.remove('on'));
        b.classList.add('on');
        const tool = b.getAttribute('data-vtt-tool');
        engine.setTool(tool);
        const sub = $('vttToolSubbar');
        if (sub) {
          if (tool === 'measure') {
            sub.style.display = 'flex';
            sub.innerHTML = '<span>Gabarit :</span>' +
              '<select class="vtt-select" id="vttTplSel"><option value="line">Ligne / Règle</option><option value="reach2">Allonge 2P</option><option value="blast1">Grenade Zone 1</option><option value="blast2">Zone 2</option><option value="blast3">Zone 3</option><option value="cone">Cône</option></select>';
            sub.querySelector('#vttTplSel').addEventListener('change', (e) => { engine.templateMode = e.target.value; });
          } else if (tool === 'draw') {
            sub.style.display = 'flex';
            sub.innerHTML = '<select class="vtt-select" id="vttDrawK"><option value="free">Crayon</option><option value="line">Ligne</option><option value="circle">Cercle</option><option value="rect">Rect</option></select>' +
              '<input type="color" id="vttDrawC" value="' + engine.drawColor + '">' +
              (window.CodexVTT.isGM() ? '<button class="vtt-mini-btn" id="vttDrawClr" type="button">Effacer tout</button>' : '');
            sub.querySelector('#vttDrawK').addEventListener('change', (e) => { engine.drawKind = e.target.value; });
            sub.querySelector('#vttDrawC').addEventListener('change', (e) => { engine.drawColor = e.target.value; });
            const clr = sub.querySelector('#vttDrawClr');
            if (clr) clr.addEventListener('click', () => window.CodexVTT.sendOp('vtt_draw', { action: 'clear', sceneId: engine.scene.id }));
          } else if (tool === 'zone') {
            sub.style.display = 'flex';
            sub.innerHTML = '<select class="vtt-select" id="vttZoneK"><option value="smoke">Fumée</option><option value="fire">Feu</option><option value="gas">Gaz</option><option value="stasis">Stase</option><option value="difficult">Terrain difficile</option></select>';
            sub.querySelector('#vttZoneK').addEventListener('change', (e) => { engine.zoneKind = e.target.value; });
          } else if (tool === 'wall') {
            sub.style.display = 'flex';
            sub.innerHTML = '<select class="vtt-select" id="vttWallK"><option value="wall">Mur Total</option><option value="door">Porte</option><option value="window">Fenêtre</option><option value="cover_half">Couvert Demi</option><option value="cover_3q">Couvert 3/4</option></select>';
            sub.querySelector('#vttWallK').addEventListener('change', (e) => { engine.wallKind = e.target.value; });
          } else if (tool === 'fog') {
            sub.style.display = 'flex';
            sub.innerHTML = '<select class="vtt-select" id="vttFogM"><option value="reveal">Révéler (pinceau)</option><option value="hide">Masquer</option></select>' +
              '<button class="vtt-mini-btn" id="vttFogReset" type="button">Réinitialiser</button>';
            sub.querySelector('#vttFogM').addEventListener('change', (e) => { engine.fogMode = e.target.value; });
            sub.querySelector('#vttFogReset').addEventListener('click', () => window.CodexVTT.sendOp('vtt_fog', { action: 'reset', sceneId: engine.scene.id }));
          } else {
            sub.style.display = 'none';
          }
        }
      });
    });

    // Onglets panneau droit
    document.querySelectorAll('[data-vtt-tab]').forEach((b) => {
      b.addEventListener('click', () => {
        const t = b.getAttribute('data-vtt-tab');
        document.querySelectorAll('[data-vtt-tab]').forEach((x) => x.classList.toggle('on', x === b));
        document.querySelectorAll('.vtt-side-pane').forEach((p) => p.classList.toggle('on', p.id === 'vttPane' + t));
        if (t === 'Comp' && window.CodexVTTCompendium) {
          window.CodexVTTCompendium.renderCompendium($('vttPaneComp'), { selectedTokenId });
        }
      });
    });

    if ($('vttBtnAmbush')) $('vttBtnAmbush').addEventListener('click', () => window.CodexVTT.sendOp('vtt_turn', { action: 'ambush_round', ambusherFaction: 'pj' }));
    if ($('vttBtnDuel')) $('vttBtnDuel').addEventListener('click', () => { if (window.CodexVTTCombat) window.CodexVTTCombat.openDuelModal(selectedTokenId, null); });
    if ($('vttBtnHandouts')) $('vttBtnHandouts').addEventListener('click', () => { if (window.CodexVTTCombat) window.CodexVTTCombat.openHandoutsModal(); });
    if ($('vttBtnClock')) $('vttBtnClock').addEventListener('click', () => { if (window.CodexVTTExtras) window.CodexVTTExtras.openCampaignToolsModal(); });
    if ($('vttBtnChallenge')) $('vttBtnChallenge').addEventListener('click', () => { if (window.CodexVTTExtras) window.CodexVTTExtras.openChallengeModal(); });
    if ($('vttBtnSpell')) $('vttBtnSpell').addEventListener('click', () => {
      if (window.CodexVTTExtras) {
        const st = window.CodexVTT.getState();
        const sc = activeScene(st);
        const tok = sc && sc.tokens.find((t) => t.id === selectedTokenId);
        window.CodexVTTExtras.openSpellCastModal(tok, sc && sc.id);
      }
    });
    if ($('vttBtnAudio')) $('vttBtnAudio').addEventListener('click', () => { if (window.CodexVTTExtras) window.CodexVTTExtras.openAudioModal(); });
    document.querySelectorAll('[data-mob-view]').forEach((b) => {
      b.addEventListener('click', () => {
        const v = b.getAttribute('data-mob-view');
        document.querySelectorAll('[data-mob-view]').forEach((x) => x.classList.toggle('on', x === b));
        const main = $('vttMain');
        if (!main) return;
        if (v === 'map') {
          main.classList.add('mob-map');
          main.classList.remove('mob-side');
          if (engine) setTimeout(() => { engine.dirty = true; }, 30);
        } else {
          main.classList.remove('mob-map');
          main.classList.add('mob-side');
          const tabBtn = document.querySelector('[data-vtt-tab="' + v + '"]');
          if (tabBtn) tabBtn.click();
        }
      });
    });
    renderMacroBar();
    if ($('vttEmptyPjBtn')) $('vttEmptyPjBtn').addEventListener('click', () => {
      const btn = document.querySelector('[data-vtt-tab="Comp"]');
      if (btn) btn.click();
    });
    if ($('vttEmptyTokBtn')) $('vttEmptyTokBtn').addEventListener('click', () => {
      const btn = document.querySelector('[data-vtt-tab="Tokens"]');
      if (btn) btn.click();
    });

    // Topbar actions
    if ($('vttSceneSelect')) $('vttSceneSelect').addEventListener('change', (e) => window.CodexVTT.sendOp('vtt_scene', { action: 'select', targetSceneId: e.target.value }));
    if ($('vttBtnSceneNew')) $('vttBtnSceneNew').addEventListener('click', () => {
      const n = prompt('Nom de la nouvelle scène :', 'Scène ' + ((engine.vtt && engine.vtt.scenes.length || 1) + 1));
      if (n && n.trim()) window.CodexVTT.sendOp('vtt_scene', { action: 'create', name: n.trim() });
    });
    if ($('vttBtnSceneCfg')) $('vttBtnSceneCfg').addEventListener('click', openSceneModal);
    if ($('vttBtnFit')) $('vttBtnFit').addEventListener('click', () => engine.fitToScene());
    if ($('vttBtnZoomIn')) $('vttBtnZoomIn').addEventListener('click', () => { engine.cam.zoom = Math.min(3, engine.cam.zoom * 1.2); engine.dirty = true; });
    if ($('vttBtnZoomOut')) $('vttBtnZoomOut').addEventListener('click', () => { engine.cam.zoom = Math.max(0.25, engine.cam.zoom * 0.82); engine.dirty = true; });
    if ($('vttBtnPause')) $('vttBtnPause').addEventListener('click', () => window.CodexVTT.sendOp('vtt_turn', { action: 'toggle_pause' }));
    if ($('vttBtnTurnLock')) $('vttBtnTurnLock').addEventListener('click', () => window.CodexVTT.sendOp('vtt_turn', { action: 'toggle_turn_lock' }));
    if ($('vttBtnFocusAll')) $('vttBtnFocusAll').addEventListener('click', () => {
      const rect = canvas.getBoundingClientRect();
      const center = engine.screenToWorld(rect.width / 2, rect.height / 2);
      window.CodexVTT.sendOp('vtt_focus_camera', { sceneId: engine.scene && engine.scene.id, x: center.x, y: center.y, zoom: engine.cam.zoom });
    });
    if ($('vttBtnStartCombat')) $('vttBtnStartCombat').addEventListener('click', () => {
      const st = window.CodexVTT.getState();
      if (st && st.combat && st.combat.active) {
        window.CodexVTT.sendOp('vtt_turn', { action: 'end_combat' });
      } else {
        const sc = activeScene(st);
        const rules = window.CodexVTTRules;
        const order = ((sc && sc.tokens) || []).filter((t) => !t.gmOnly).map((t) => {
          const r = rules ? rules.executeRoll({ spec: rules.parseDieSpec('d6') }) : { total: 3 };
          return { tokenId: t.id, name: t.name, init: r.total, die: 'd6', faction: t.faction };
        }).sort((a, b) => b.init - a.init);
        window.CodexVTT.sendOp('vtt_turn', { action: 'start_combat', round: 1, order });
      }
    });
    if ($('vttBtnNextTurn')) $('vttBtnNextTurn').addEventListener('click', () => window.CodexVTT.sendOp('vtt_turn', { action: 'next_turn' }));
    if ($('vttBtnPrevTurn')) $('vttBtnPrevTurn').addEventListener('click', () => window.CodexVTT.sendOp('vtt_turn', { action: 'prev_turn' }));
    if ($('vttBtnResetPa')) $('vttBtnResetPa').addEventListener('click', () => window.CodexVTT.sendOp('vtt_turn', { action: 'reset_pa' }));

    // Ouverture auto en mode VTT si ?mode=vtt
    try {
      const q = new URLSearchParams(window.location.search);
      if (q.get('mode') === 'vtt') setVttMode(true);
    } catch (e) {}
  }

  document.addEventListener('DOMContentLoaded', init);

  window.CodexVTTUI = {
    setVttMode,
    openCritModal,
    openSceneModal,
    isVttActive: () => vttActive
  };
})();
