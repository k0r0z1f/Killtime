/* ==========================================================================
   VTT COMPENDIUMS — Fiches PJ, Livre VI États, Bestiaire XII, Objets DLPH, Actions A, Atlas
   js/vtt-compendium.js
   ========================================================================== */
(function () {
  'use strict';
  const esc = (s) => String(s || '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

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

  const cache = new Map();
  async function loadJson(relPath) {
    if (cache.has(relPath)) return cache.get(relPath);
    const r = await fetch(relPath, { cache: 'force-cache' });
    if (!r.ok) throw new Error('Chargement ' + relPath + ' impossible');
    const d = await r.json();
    cache.set(relPath, d);
    return d;
  }

  let statusIndex = null;
  async function ensureStatuses() {
    if (statusIndex) return statusIndex;
    try {
      const d = await loadJson('data/livre_6.json');
      const m = (d && d.mecaniques) || {};
      const map = new Map();
      (m.etats_prejudiciables || []).forEach((e) => {
        if (e && e.etat) map.set(e.etat, { kind: 'prej', desc: e.description || '', detail: e.malus || '' });
      });
      (m.etats_benefiques || []).forEach((e) => {
        if (e && e.etat) map.set(e.etat, { kind: 'ben', desc: e.description || '', detail: e.bonus || '' });
      });
      (m.etats_dot || []).forEach((e) => {
        if (e && e.etat) map.set(e.etat, { kind: 'dot', desc: e.tick || '', detail: e.soin || '' });
      });
      statusIndex = map;
      return map;
    } catch (e) {
      return new Map();
    }
  }

  function getStatusTip(name) {
    if (!statusIndex) return '';
    const e = statusIndex.get(name);
    if (!e) return '';
    return [e.desc, e.detail].filter(Boolean).join(' — ');
  }

  let activeSubTab = 'pj';
  let pjCache = { myCharacters: [], linked: [] };

  async function refreshCharacters(tableId) {
    if (!tableId) return pjCache;
    try {
      const r = await fetch(apiBase() + '/api/tables/' + encodeURIComponent(tableId) + '/characters', {
        credentials: 'include',
        cache: 'no-store',
        headers: { 'ngrok-skip-browser-warning': 'true' }
      });
      if (r.ok) {
        const d = await r.json();
        pjCache = { myCharacters: d.myCharacters || [], linked: d.linked || [] };
      }
    } catch (e) {}
    return pjCache;
  }

  async function linkCharacter(tableId, pjId, tokenId) {
    const r = await fetch(apiBase() + '/api/tables/' + encodeURIComponent(tableId) + '/characters/link', {
      method: 'PUT',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json', 'ngrok-skip-browser-warning': 'true' },
      body: JSON.stringify({ pjId, tokenId: tokenId || null })
    });
    if (r.ok) await refreshCharacters(tableId);
    return r.ok;
  }

  function parseLeadingInt(str, fallback) {
    const m = /(\d+)/.exec(String(str || ''));
    return m ? parseInt(m[1], 10) : fallback;
  }

  function spawnPjToken(pj, sceneId, existingTokenId) {
    const data = (pj && pj.data) || {};
    const rules = window.CodexVTTRules;
    const derived = rules ? rules.computeDerivedStats(data.attributes || {}, {
      colonialEndurance: data.race === 'humain' && Number(data.gift) === 1
    }) : { maxPa: 7, maxPe: 3, attrs: { con: 3 } };
    const name = data.name || pj.label || 'Héros';
    if (existingTokenId) {
      window.CodexVTT.sendOp('vtt_token', {
        action: 'update',
        sceneId,
        tokenId: existingTokenId,
        patch: {
          name,
          pjId: pj.id,
          con: derived.attrs.con,
          maxPa: derived.maxPa,
          maxPe: derived.maxPe,
          attrs: derived.attrs
        }
      });
    } else {
      window.CodexVTT.sendOp('vtt_token', {
        action: 'add',
        fromPjLink: true,
        sceneId,
        token: {
          name,
          faction: 'pj',
          pjId: pj.id,
          con: derived.attrs.con,
          maxPa: derived.maxPa,
          pa: derived.maxPa,
          maxPe: derived.maxPe,
          pe: 0,
          attrs: derived.attrs,
          color: '#38bdf8',
          q: 2,
          r: 2
        }
      });
    }
  }

  function spawnCreature(c, scene) {
    const st = c.stats || {};
    const con = parseLeadingInt(st['Constitution'], 3);
    const pa = parseLeadingInt(st["Points d'Action"], 6);
    const armor = parseLeadingInt(st['Armure'] || st['Protection'], 0);
    const baseName = (c.nom || 'Créature').split('—')[0].trim().slice(0, 32);
    const count = (scene.tokens || []).filter((t) => (t.name || '').startsWith(baseName)).length + 1;
    const name = baseName + ' #' + count;
    const col = c.classe === 'boss' ? '#8b5cf6' : (c.classe === 'elite' ? '#f59e0b' : '#f43f5e');
    window.CodexVTT.sendOp('vtt_token', {
      action: 'add',
      sceneId: scene.id,
      token: {
        name,
        faction: 'enemy',
        size: c.classe === 'boss' ? 2 : 1,
        con,
        maxPa: pa,
        pa,
        maxPe: con,
        pe: 0,
        armor,
        color: col,
        notes: c.rang || '',
        q: 6,
        r: 4
      }
    });
  }

  async function renderCompendium(container, opts) {
    if (!container) return;
    const vtt = window.CodexVTT ? window.CodexVTT.getState() : null;
    const isGM = window.CodexVTT ? window.CodexVTT.isGM() : false;
    const tableId = window.CodexVTT ? window.CodexVTT.getTableId() : null;
    const sc = vtt && vtt.scenes ? (vtt.scenes.find((s) => s.id === vtt.activeSceneId) || vtt.scenes[0]) : null;
    const selTokId = opts && opts.selectedTokenId;
    const selTok = sc && sc.tokens.find((t) => t.id === selTokId);

    const tabs = [
      ['pj', '👤 PJ'],
      ...(isGM ? [['bestiaire', '🐉 Bestiaire']] : []),
      ['objets', '🎒 Objets'],
      ['actions', '⚡ Actions'],
      ...(isGM ? [['atlas', '🌍 Atlas']] : [])
    ];
    if (!tabs.some((t) => t[0] === activeSubTab)) activeSubTab = 'pj';

    let h = '<div class="vtt-chat-tools">' +
      tabs.map(([k, lbl]) => '<button class="vtt-flt-btn' + (activeSubTab === k ? ' on' : '') + '" data-cmp-tab="' + k + '" type="button">' + lbl + '</button>').join('') +
      '</div><div id="vttCmpBody"><p class="tb-status">Chargement…</p></div>';
    container.innerHTML = h;

    container.querySelectorAll('[data-cmp-tab]').forEach((b) => {
      b.addEventListener('click', () => {
        activeSubTab = b.getAttribute('data-cmp-tab');
        renderCompendium(container, opts);
      });
    });

    const body = container.querySelector('#vttCmpBody');
    if (!sc) { body.innerHTML = '<p class="tb-status">Ouvre une table d’abord.</p>'; return; }

    if (activeSubTab === 'pj') {
      await refreshCharacters(tableId);
      const list = pjCache.myCharacters || [];
      let bh = '<div class="vtt-card"><h4>Mes Fiches PJ</h4>';
      if (!list.length) {
        bh += '<p class="tb-status">Aucune fiche trouvée. Crée ta fiche sur <a href="fiche_pj.html" target="_blank">fiche_pj.html</a>.</p>';
      } else {
        list.forEach((pj) => {
          const d = pj.data || {};
          const linked = vtt && vtt.linkedCharacters && Object.values(vtt.linkedCharacters).some((l) => l && l.pjId === pj.id);
          bh += '<div class="vtt-zone-row"><span><strong>' + esc(d.name || pj.label) + '</strong> <small>(' + esc(d.race || '?') + ')</small></span>' +
            '<span style="display:flex;gap:4px;">' +
            '<button class="vtt-mini-btn" data-pj-link="' + esc(pj.id) + '" type="button">' + (linked ? '✓ Lié' : '🔗 Lier') + '</button>' +
            '<button class="vtt-mini-btn" data-pj-spawn="' + esc(pj.id) + '" type="button">🎯 Pion</button>' +
            '</span></div>';
        });
      }
      bh += '</div>';
      if (isGM && (pjCache.linked || []).length) {
        bh += '<div class="vtt-card"><h4>Fiches liées à la table</h4>';
        pjCache.linked.forEach((item) => {
          const d = (item.pj && item.pj.data) || {};
          bh += '<div class="vtt-zone-row"><span>' + esc(item.username) + ' — <strong>' + esc(d.name || '?') + '</strong></span>' +
            '<button class="vtt-mini-btn" data-pj-lnk-spawn="' + esc(item.pj.id) + '" type="button">+ Pion</button></div>';
        });
        bh += '</div>';
      }
      body.innerHTML = bh;
      body.querySelectorAll('[data-pj-link]').forEach((b) => b.addEventListener('click', async () => {
        await linkCharacter(tableId, b.getAttribute('data-pj-link'), selTokId);
        renderCompendium(container, opts);
      }));
      body.querySelectorAll('[data-pj-spawn]').forEach((b) => b.addEventListener('click', async () => {
        const pj = list.find((x) => x.id === b.getAttribute('data-pj-spawn'));
        if (!pj) return;
        await linkCharacter(tableId, pj.id, selTok && selTok.pjId === pj.id ? selTok.id : null);
        spawnPjToken(pj, sc.id, selTok && selTok.pjId === pj.id ? selTok.id : null);
      }));
      body.querySelectorAll('[data-pj-lnk-spawn]').forEach((b) => b.addEventListener('click', () => {
        const found = (pjCache.linked || []).find((x) => x.pj && x.pj.id === b.getAttribute('data-pj-lnk-spawn'));
        if (found && found.pj) spawnPjToken(found.pj, sc.id, null);
      }));
      return;
    }

    if (activeSubTab === 'bestiaire') {
      const d12 = await loadJson('data/livre_12.json');
      const creatures = (d12 && d12.mecaniques && d12.mecaniques.creatures) || [];
      body.innerHTML = '<input class="vtt-select" id="vttBestQ" placeholder="Filtrer bestiaire (Bandit, Automate, Bête…)" style="width:100%;margin-bottom:6px;">' +
        '<div id="vttBestList"></div>';
      const renderList = (q) => {
        const term = (q || '').toLowerCase();
        const filtered = creatures.filter((c) => !term || (c.nom || '').toLowerCase().includes(term) || (c.rang || '').toLowerCase().includes(term));
        const listEl = body.querySelector('#vttBestList');
        listEl.innerHTML = filtered.slice(0, 20).map((c, idx) =>
          '<div class="vtt-zone-row"><div><strong>' + esc(c.nom) + '</strong><div class="tb-status">' + esc(c.rang || '') + ' · ' + esc((c.stats && c.stats["Points d'Action"]) || '') + '</div></div>' +
          '<button class="vtt-mini-btn" data-sp-cr="' + idx + '" type="button">+ Placer</button></div>'
        ).join('');
        listEl.querySelectorAll('[data-sp-cr]').forEach((b) => b.addEventListener('click', () => {
          spawnCreature(filtered[parseInt(b.getAttribute('data-sp-cr'), 10)], sc);
        }));
      };
      renderList('');
      body.querySelector('#vttBestQ').addEventListener('input', (e) => renderList(e.target.value));
      return;
    }

    if (activeSubTab === 'objets') {
      const dObj = await loadJson('data/appendice_objets.json');
      const objets = dObj.objets || [];
      const kits = dObj.presets_kit || [];
      body.innerHTML = '<div class="tb-status" style="margin-bottom:6px;">Cible : <strong>' + esc(selTok ? selTok.name : 'Aucun pion sélectionné') + '</strong></div>' +
        '<div style="display:flex;gap:4px;margin-bottom:6px;"><select class="vtt-select" id="vttKitSel" style="flex:1;">' +
        kits.map((k, i) => '<option value="' + i + '">' + esc(k.nom) + '</option>').join('') +
        '</select><button class="vtt-mini-btn" id="vttAddKitBtn" type="button">+ Kit</button></div>' +
        '<input class="vtt-select" id="vttObjQ" placeholder="Rechercher objet DLPH (épée, fusil, medkit…)" style="width:100%;margin-bottom:6px;">' +
        '<div id="vttObjList"></div>';
      body.querySelector('#vttAddKitBtn').addEventListener('click', () => {
        if (!selTok) { alert('Sélectionne d’abord un pion.'); return; }
        const kit = kits[parseInt(body.querySelector('#vttKitSel').value, 10)];
        if (!kit) return;
        const items = (kit.items || []).map((ref) => {
          const full = objets.find((o) => o.id === ref.id) || { id: ref.id, nom: ref.id };
          return {
            objId: full.id, nom: full.nom, dlph: full.dlph || '', categorie: full.categorie || '',
            degats: full.degats || 0, perforante: full.perforante || 0, protection: full.protection || 0,
            encombrement_pa: full.encombrement_pa || 0, poids_livres: full.poids_livres || 0,
            munitions: full.munitions || null, munitionsMax: full.munitions || null,
            quantite: ref.qty || 1, etatObjet: 'Neuf'
          };
        });
        window.CodexVTT.sendOp('vtt_token', { action: 'add_item', sceneId: sc.id, tokenId: selTok.id, items });
      });
      const renderObjs = (q) => {
        const term = (q || '').toLowerCase();
        const list = objets.filter((o) => !term || (o.nom || '').toLowerCase().includes(term) || (o.categorie || '').toLowerCase().includes(term));
        const el = body.querySelector('#vttObjList');
        el.innerHTML = list.slice(0, 18).map((o, i) =>
          '<div class="vtt-zone-row"><div><strong>' + esc(o.nom) + '</strong><div class="tb-status">' + esc(o.dlph || o.categorie || '') + '</div></div>' +
          '<span style="display:flex;gap:4px;">' +
          (selTok ? '<button class="vtt-mini-btn" data-eq-obj="' + i + '" type="button">+ Pion</button>' : '') +
          (isGM ? '<button class="vtt-mini-btn" data-drp-obj="' + i + '" type="button">⬇ Sol</button>' : '') +
          '</span></div>'
        ).join('');
        el.querySelectorAll('[data-eq-obj]').forEach((b) => b.addEventListener('click', () => {
          const o = list[parseInt(b.getAttribute('data-eq-obj'), 10)];
          window.CodexVTT.sendOp('vtt_token', {
            action: 'add_item', sceneId: sc.id, tokenId: selTok.id,
            item: {
              objId: o.id, nom: o.nom, dlph: o.dlph || '', categorie: o.categorie || '',
              degats: o.degats || 0, perforante: o.perforante || 0, protection: o.protection || 0,
              encombrement_pa: o.encombrement_pa || 0, poids_livres: o.poids_livres || 0,
              munitions: o.munitions || null, munitionsMax: o.munitions || null,
              quantite: 1, etatObjet: 'Neuf'
            }
          });
        }));
        el.querySelectorAll('[data-drp-obj]').forEach((b) => b.addEventListener('click', () => {
          const o = list[parseInt(b.getAttribute('data-drp-obj'), 10)];
          window.CodexVTT.sendOp('vtt_drop', {
            action: 'add', sceneId: sc.id, name: o.nom, dlph: o.dlph || '', q: 3, r: 3,
            item: { objId: o.id, nom: o.nom, dlph: o.dlph || '', degats: o.degats || 0, perforante: o.perforante || 0, poids_livres: o.poids_livres || 0, etatObjet: 'Neuf' }
          });
        }));
      };
      renderObjs('');
      body.querySelector('#vttObjQ').addEventListener('input', (e) => renderObjs(e.target.value));
      return;
    }

    if (activeSubTab === 'actions') {
      const dAct = await loadJson('data/appendice_actions.json');
      const actions = dAct.actions || [];
      const cats = dAct.categories || [];
      body.innerHTML = '<div style="display:flex;gap:4px;margin-bottom:6px;">' +
        '<select class="vtt-select" id="vttActCat" style="flex:1;"><option value="">Toutes catégories</option>' +
        cats.map((c) => '<option value="' + esc(c.id) + '">' + esc(c.code + ' ' + c.nom) + '</option>').join('') +
        '</select></div>' +
        '<input class="vtt-select" id="vttActQ" placeholder="Filtrer les 101 actions…" style="width:100%;margin-bottom:6px;">' +
        '<div id="vttActList"></div>';
      const renderActs = () => {
        const cat = body.querySelector('#vttActCat').value;
        const term = (body.querySelector('#vttActQ').value || '').toLowerCase();
        const list = actions.filter((a) => (!cat || a.categorie === cat) && (!term || (a.nom || '').toLowerCase().includes(term) || (a.resume || '').toLowerCase().includes(term)));
        const el = body.querySelector('#vttActList');
        el.innerHTML = list.slice(0, 18).map((a, i) =>
          '<div class="vtt-zone-row" title="' + esc(a.procedure || a.resume || '') + '"><div><strong>' + esc(a.nom) + '</strong> <span class="vtt-tag">' + esc(a.cout_pa || '?') + '</span>' +
          '<div class="tb-status">' + esc((a.resume || '').slice(0, 72)) + '</div></div>' +
          '<button class="vtt-mini-btn" data-use-act="' + i + '" type="button">▶</button></div>'
        ).join('');
        el.querySelectorAll('[data-use-act]').forEach((b) => b.addEventListener('click', () => {
          const a = list[parseInt(b.getAttribute('data-use-act'), 10)];
          if (selTok && Number.isFinite(a.cout_pa_tri) && a.cout_pa_tri > 0) {
            window.CodexVTT.sendOp('vtt_token', { action: 'update', sceneId: sc.id, tokenId: selTok.id, patch: { pa: Math.max(0, (selTok.pa || 0) - a.cout_pa_tri) } });
          }
          if (window.CodexTables && window.CodexTables.sendRichMessage) {
            window.CodexTables.sendRichMessage(
              '⚡ ' + (selTok ? selTok.name + ' : ' : '') + a.nom + ' (' + (a.cout_pa || '?') + ')',
              { kind: 'action', action: { nom: a.nom, cout_pa: a.cout_pa, resume: a.resume } }
            );
          }
        }));
      };
      renderActs();
      body.querySelector('#vttActCat').addEventListener('change', renderActs);
      body.querySelector('#vttActQ').addEventListener('input', renderActs);
      return;
    }

    if (activeSubTab === 'atlas') {
      body.innerHTML = '<div class="vtt-card"><h4>Atlas des Mondes — Hybris</h4>' +
        '<p class="tb-status">Projette les 55 repères et 18 routes d’Hybris (<code>data/atlas_mondes.json</code>) comme scène d’exploration dans cette table.</p>' +
        '<button class="vtt-btn on" id="vttBtnLoadAtlas" type="button">🌍 Projeter Carte d’Hybris</button></div>';
      body.querySelector('#vttBtnLoadAtlas').addEventListener('click', async () => {
        const am = await loadJson('data/atlas_mondes.json');
        window.CodexVTT.sendOp('vtt_scene', {
          action: 'load_atlas',
          name: 'Atlas — Hybris',
          landmarks: am.hybris_registry || [],
          routes: am.hybris_routes || []
        });
      });
    }
  }

  ensureStatuses();

  window.CodexVTTCompendium = {
    ensureStatuses,
    getStatusTip,
    renderCompendium,
    refreshCharacters,
    spawnPjToken
  };
})();
