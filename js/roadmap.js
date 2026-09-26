/* Roadmaps v3 multi-boards — Killtime / Systeme RP
   Stockage : { boards: [{id,name,desc,tasks[]}], activeId }.
   Source : data/roadmaps.json (migration auto depuis data/roadmap.json v1/v2).
   Autosave : localStorage instantané + PUT /api/roadmaps debounced.
   Drag : pointer events (souris + tactile), reorder intra-colonne + inter-colonnes. */
(function () {
  'use strict';
  const LS_KEY = 'kt-roadmap-v1';
  const JSON_URL = 'data/roadmaps.json';
  const LEGACY_URL = 'data/roadmap.json';
  const STATUSES = ['todo', 'in_progress', 'done'];
  const STATUS_LABEL = { todo: 'À faire', in_progress: 'En cours', done: 'Terminé' };
  const PRIO_LABEL = { high: 'Haute', medium: 'Moyenne', low: 'Basse' };
  const PRIO_W = { high: 0, medium: 1, low: 2 };
  const WIP_LIMIT = 6;

  let store = { boards: [], activeId: null };
  let views = {}; // boardId -> {search,epic,prio,sort,collapsed}
  let editingId = null;
  let createdInModal = false;
  let modalPrio = 'medium';
  let flashId = null;
  let diskMode = 'unknown'; // unknown | ok | local
  let diskTimer = null;
  let liveTimer = null;
  let saveWatchdog = null;
  let tabEditing = null; // boardId en cours de renommage, ou 'new'

  const $ = (id) => document.getElementById(id);
  const els = {};
  function cacheEls() {
    ['rmTabs', 'rmBoardDesc', 'rmSearch', 'rmEpicFilter', 'rmPrioFilter', 'rmSort', 'rmAdd', 'rmExport',
     'rmImport', 'rmImportBtn', 'rmReset', 'rmBoard', 'col-todo', 'col-in_progress', 'col-done',
     'cTodo', 'cProg', 'cDone', 'rmCountLine', 'stTotal', 'stTodo', 'stProg', 'stDone', 'stPct', 'rmBar',
     'rmBoardName', 'rmEpics', 'inspTotal', 'inspDirty', 'rmSaveState', 'rmSaveLabel', 'rmBackdrop',
     'rmModalTitle', 'rmLive', 'fTitle', 'fDetails', 'fEpic', 'fPrioSeg', 'fStatus', 'fPlaceholder',
     'fPrim', 'fAsset', 'rmCancel', 'rmSave', 'rmToast', 'rmToastMsg', 'rmToastBtn', 'epicList'
    ].forEach((id) => { els[id] = $(id); });
  }

  /* ---------- boards ---------- */
  function board() { return store.boards.find((b) => b.id === store.activeId) || store.boards[0] || null; }
  function tasks() { const b = board(); return b ? b.tasks : []; }
  function view() {
    const b = board();
    if (!b) return { search: '', epic: '', prio: '', sort: 'manual', collapsed: {} };
    if (!views[b.id]) views[b.id] = { search: '', epic: '', prio: '', sort: 'manual', collapsed: {} };
    return views[b.id];
  }

  /* ---------- toast (+ action undo) ---------- */
  function toast(msg, action) {
    els.rmToastMsg.textContent = msg;
    const btn = els.rmToastBtn;
    btn.style.display = 'none';
    btn.onclick = null;
    clearTimeout(toast._t);
    if (action) {
      btn.textContent = action.label;
      btn.style.display = '';
      btn.onclick = () => { els.rmToast.classList.remove('show'); action.fn(); };
      els.rmToast.classList.add('show');
      toast._t = setTimeout(() => els.rmToast.classList.remove('show'), 6000);
    } else {
      els.rmToast.classList.add('show');
      toast._t = setTimeout(() => els.rmToast.classList.remove('show'), 2200);
    }
  }

  /* ---------- autosave ---------- */
  function setSaveState(mode, label) {
    els.rmSaveState.classList.remove('saving', 'saved', 'local');
    if (mode) els.rmSaveState.classList.add(mode);
    els.rmSaveLabel.textContent = label;
  }
  function nowHM() { return new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit', second: '2-digit' }); }

  function payload() {
    return { version: 3, updated: new Date().toISOString().slice(0, 10), activeId: store.activeId, boards: store.boards };
  }
  function persistLocal() {
    try { localStorage.setItem(LS_KEY, JSON.stringify({ v: 3, store, views })); }
    catch (e) { toast('Sauvegarde locale impossible'); }
  }

  async function pushDisk() {
    clearTimeout(saveWatchdog);
    if (diskMode === 'local') {
      els.inspDirty.textContent = 'oui (local)';
      setSaveState('local', 'Sauvé local • ' + nowHM() + ' — Exporter pour git');
      els.rmSaveState.title = 'Disque indisponible (serveur Node sans endpoint /api/roadmaps ?). Cliquez la pastille pour exporter.';
      return;
    }
    try {
      const ctrl = new AbortController();
      const to = setTimeout(() => ctrl.abort(), 4000);
      const res = await fetch('/api/roadmaps', {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload()), signal: ctrl.signal
      });
      clearTimeout(to);
      if (!res.ok) throw new Error('HTTP ' + res.status);
      diskMode = 'ok';
      els.inspDirty.textContent = 'non';
      setSaveState('saved', 'Autosavé disque • ' + nowHM());
      els.rmSaveState.title = 'data/roadmaps.json écrit sur disque (.bak conservé).';
    } catch (e) {
      diskMode = 'local';
      els.inspDirty.textContent = 'oui (local)';
      setSaveState('local', 'Sauvé local • ' + nowHM() + ' — Exporter pour git');
      els.rmSaveState.title = 'Disque indisponible (' + (e.message || e) + '). localStorage OK. Cliquez la pastille pour exporter.';
    }
  }

  function armWatchdog() {
    clearTimeout(saveWatchdog);
    saveWatchdog = setTimeout(() => {
      if (els.rmSaveState.classList.contains('saving')) {
        diskMode = 'local';
        els.inspDirty.textContent = 'oui (local)';
        setSaveState('local', 'Sauvé local • ' + nowHM() + ' — Exporter pour git');
        els.rmSaveState.title = 'Timeout disque : repli local. Cliquez la pastille pour exporter.';
      }
    }, 7000);
  }

  function commit(label) {
    persistLocal();
    els.inspDirty.textContent = 'oui (local)';
    if (diskMode === 'local') {
      clearTimeout(diskTimer);
      setSaveState('local', 'Sauvé local • ' + nowHM() + ' — Exporter pour git');
      if (label) toast(label);
      return;
    }
    setSaveState('saving', 'Enregistrement…');
    clearTimeout(diskTimer);
    diskTimer = setTimeout(pushDisk, 800);
    armWatchdog();
    if (label) toast(label);
  }

  /* ---------- modèle ---------- */
  function uid() {
    let n = 0;
    store.boards.forEach((b) => b.tasks.forEach((t) => {
      const k = parseInt(String(t.id || '').replace(/\D/g, ''), 10);
      if (Number.isFinite(k)) n = Math.max(n, k);
    }));
    return 'RD-' + String(n + 1).padStart(3, '0');
  }
  function siblings(status) {
    return tasks().filter((t) => t.status === status).sort((a, b) => (a.order || 0) - (b.order || 0));
  }
  function renumber(status) {
    siblings(status).forEach((t, i) => { t.order = i; });
  }
  function cleanTask(t, i) {
    return {
      id: t.id || ('RD-' + String(i + 1).padStart(3, '0')),
      title: t.title || '(sans titre)',
      epic: t.epic || 'Divers',
      status: STATUSES.includes(t.status) ? t.status : 'todo',
      priority: ['high', 'medium', 'low'].includes(t.priority) ? t.priority : 'medium',
      order: Number.isFinite(t.order) ? t.order : i,
      scene: t.scene || '',
      placeholderId: t.placeholderId || '',
      primitive: t.primitive || '',
      pos: Array.isArray(t.pos) ? t.pos : null,
      scale: Array.isArray(t.scale) ? t.scale : null,
      suggestedAsset: t.suggestedAsset || '',
      details: t.details || ''
    };
  }
  function cleanBoard(b, i) {
    const tasks = Array.isArray(b.tasks) ? b.tasks.map(cleanTask) : [];
    const bd = {
      id: b.id || ('board-' + Date.now().toString(36) + '-' + i),
      name: b.name || ('Roadmap ' + (i + 1)),
      desc: b.desc || '',
      tasks
    };
    STATUSES.forEach((s) => tasks.filter((t) => t.status === s)
      .sort((a, c) => (a.order || 0) - (c.order || 0)).forEach((t, k) => { t.order = k; }));
    return bd;
  }

  function adoptStore(doc) {
    store.boards = doc.boards.map(cleanBoard);
    store.activeId = doc.activeId && store.boards.some((b) => b.id === doc.activeId)
      ? doc.activeId : (store.boards[0] && store.boards[0].id);
  }
  // migration v1/v2 mono-board -> v3
  function migrateSingle(doc) {
    const bd = cleanBoard({
      id: 'board-prefabs-scene',
      name: (doc.board && doc.board.replace(/^Killtime Tactics — Roadmap /, '')) || 'Prefabs scènes',
      desc: 'Remplacement des placeholders de scène par les assets finaux.',
      tasks: doc.tasks || []
    }, 0);
    store.boards = [bd];
    store.activeId = bd.id;
  }

  async function load() {
    let fromLocal = false;
    try {
      const raw = localStorage.getItem(LS_KEY);
      if (raw) {
        const data = JSON.parse(raw);
        if (data && data.v === 3 && data.store && Array.isArray(data.store.boards) && data.store.boards.length) {
          adoptStore(data.store);
          if (data.views) views = data.views;
          fromLocal = true;
        } else if (data && Array.isArray((data.v === 2 ? data.state : data || {}).tasks)) {
          const doc = data.v === 2 ? data.state : data; // migration v1/v2
          migrateSingle(doc);
          if (data.v === 2 && data.view) views[store.activeId] = Object.assign({ search: '', epic: '', prio: '', sort: 'manual', collapsed: {} }, data.view);
          fromLocal = true;
        }
      }
    } catch (e) { /* fallback fichiers */ }
    if (!store.boards.length) {
      try {
        const res = await fetch(JSON_URL, { cache: 'no-store' });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const doc = await res.json();
        if (Array.isArray(doc.boards) && doc.boards.length) adoptStore(doc);
        else if (Array.isArray(doc.tasks)) migrateSingle(doc);
        else throw new Error('format inconnu');
        if (doc.activeId) store.activeId = doc.activeId;
      } catch (e1) {
        try { // repli legacy v1/v2
          const res = await fetch(LEGACY_URL, { cache: 'no-store' });
          if (!res.ok) throw new Error('HTTP ' + res.status);
          migrateSingle(await res.json());
        } catch (e2) {
          toast('Impossible de charger les roadmaps');
          store.boards = [{ id: 'board-1', name: 'Roadmap 1', desc: '', tasks: [] }];
          store.activeId = 'board-1';
        }
      }
    }
    els.inspDirty.textContent = fromLocal ? 'oui (local)' : 'non';
    syncViewControls();
    render();
    setSaveState('', fromLocal ? 'Modifs locales — autosave actif' : 'Prêt — autosave actif');
  }

  /* ---------- filtres / tri (par board) ---------- */
  function syncViewControls() {
    const v = view();
    els.rmSearch.value = v.search || '';
    els.rmPrioFilter.value = v.prio || '';
    els.rmSort.value = v.sort || 'manual';
  }
  function filtered() {
    const v = view();
    const q = (v.search || '').toLowerCase().trim();
    return tasks().filter((t) => {
      if (v.epic && t.epic !== v.epic) return false;
      if (v.prio && t.priority !== v.prio) return false;
      if (!q) return true;
      return [t.id, t.title, t.epic, t.placeholderId, t.primitive, t.suggestedAsset, t.details, t.scene]
        .join(' ').toLowerCase().includes(q);
    });
  }
  function filtersActive() {
    const v = view();
    return !!(v.search || v.epic || v.prio);
  }
  function sortList(list) {
    const v = view();
    const arr = list.slice();
    if (v.sort === 'priority') arr.sort((a, b) => PRIO_W[a.priority] - PRIO_W[b.priority] || (a.order || 0) - (b.order || 0));
    else if (v.sort === 'id') arr.sort((a, b) => String(a.id).localeCompare(String(b.id)));
    else arr.sort((a, b) => (a.order || 0) - (b.order || 0));
    return arr;
  }

  /* ---------- onglets ---------- */
  function openCount(b) {
    return b.tasks.filter((t) => t.status !== 'done').length;
  }
  function renderTabs() {
    els.rmTabs.innerHTML = '';
    store.boards.forEach((b) => {
      const tab = document.createElement('div');
      tab.className = 'rm-tab' + (b.id === store.activeId ? ' on' : '');
      if (tabEditing === b.id) {
        tab.innerHTML = '<input value="">';
        const inp = tab.querySelector('input');
        inp.value = b.name;
        inp.addEventListener('click', (e) => e.stopPropagation());
        inp.addEventListener('keydown', (e) => {
          e.stopPropagation();
          if (e.key === 'Enter') finishRename(b.id, inp.value);
          else if (e.key === 'Escape') { tabEditing = null; renderTabs(); }
        });
        inp.addEventListener('blur', () => { if (tabEditing === b.id) finishRename(b.id, inp.value); });
        setTimeout(() => { inp.focus(); inp.select(); }, 30);
      } else {
        const lbl = document.createElement('span');
        lbl.className = 'lbl'; lbl.textContent = b.name; lbl.title = 'Double-clic pour renommer';
        tab.appendChild(lbl);
        const cnt = document.createElement('span');
        cnt.className = 'cnt'; cnt.textContent = openCount(b);
        tab.appendChild(cnt);
        const ed = document.createElement('button');
        ed.className = 'edit'; ed.textContent = '✎'; ed.title = 'Renommer cette roadmap';
        ed.addEventListener('click', (e) => { e.stopPropagation(); tabEditing = b.id; renderTabs(); });
        tab.appendChild(ed);
        if (store.boards.length > 1) {
          const x = document.createElement('button');
          x.className = 'x'; x.textContent = '×'; x.title = 'Supprimer cette roadmap';
          x.addEventListener('click', (e) => { e.stopPropagation(); deleteBoard(b.id); });
          tab.appendChild(x);
        }
        tab.addEventListener('click', () => switchBoard(b.id));
        lbl.addEventListener('dblclick', (e) => { e.stopPropagation(); tabEditing = b.id; renderTabs(); });
      }
      els.rmTabs.appendChild(tab);
    });
    const add = document.createElement('div');
    add.className = 'rm-tab-add';
    if (tabEditing === 'new') {
      add.innerHTML = '<input placeholder="Nom de la roadmap…">';
      const inp = add.querySelector('input');
      inp.addEventListener('click', (e) => e.stopPropagation());
      inp.addEventListener('keydown', (e) => {
        e.stopPropagation();
        if (e.key === 'Enter') finishAdd(inp.value);
        else if (e.key === 'Escape') { tabEditing = null; renderTabs(); }
      });
      inp.addEventListener('blur', () => { if (tabEditing === 'new') finishAdd(inp.value, true); });
      setTimeout(() => inp.focus(), 30);
    } else {
      add.textContent = '+ Nouvelle roadmap';
      add.addEventListener('click', () => { tabEditing = 'new'; renderTabs(); });
    }
    els.rmTabs.appendChild(add);
  }
  function finishAdd(name, fromBlur) {
    name = (name || '').trim();
    if (!name) {
      if (!fromBlur) toast('Nom vide — roadmap non créée');
      tabEditing = null; renderTabs(); return;
    }
    const b = { id: 'board-' + Date.now().toString(36), name, desc: '', tasks: [] };
    store.boards.push(b);
    store.activeId = b.id;
    tabEditing = null;
    commit();
    syncViewControls(); render();
    toast('Roadmap « ' + name + ' » créée');
  }
  function finishRename(id, name) {
    name = (name || '').trim();
    const b = store.boards.find((x) => x.id === id);
    tabEditing = null;
    if (b && name && name !== b.name) {
      b.name = name;
      commit();
    }
    render();
  }
  function switchBoard(id) {
    if (id === store.activeId) return;
    store.activeId = id;
    flashId = null;
    persistLocal();
    syncViewControls(); render();
  }
  function deleteBoard(id) {
    if (store.boards.length <= 1) { toast('Impossible : une roadmap minimum'); return; }
    const idx = store.boards.findIndex((b) => b.id === id);
    if (idx === -1) return;
    const [gone] = store.boards.splice(idx, 1);
    delete views[id];
    if (store.activeId === id) store.activeId = store.boards[Math.max(0, idx - 1)].id;
    commit();
    syncViewControls(); render();
    toast('Roadmap « ' + gone.name + ' » supprimée (' + gone.tasks.length + ' tâches)', {
      label: 'Annuler', fn: () => {
        store.boards.splice(Math.min(idx, store.boards.length), 0, gone);
        store.activeId = gone.id;
        commit();
        syncViewControls(); render();
        toast('Roadmap restaurée');
      }
    });
  }

  /* ---------- cartes ---------- */
  function cardEl(t) {
    const d = document.createElement('div');
    d.className = 'rm-card pr-' + t.priority;
    d.dataset.id = t.id;
    if (flashId === t.id) { d.classList.add('flash'); }
    const meta = [t.placeholderId ? ('⛶ ' + t.placeholderId) : '', t.primitive || '', t.suggestedAsset ? ('→ ' + t.suggestedAsset) : '']
      .filter(Boolean).join(' · ');
    d.innerHTML =
      '<div class="rm-card-top"><span class="rm-id"></span><span class="rm-epic"></span><span class="rm-prio"></span><span class="rm-grip" title="Glisser pour réordonner">⋮⋮</span></div>' +
      '<h4></h4>' +
      (t.details ? '<p></p>' : '') +
      (meta ? '<div class="rm-card-meta"></div>' : '') +
      '<div class="rm-card-actions">' +
      '<button class="rm-mini" data-act="left" title="Vers la gauche">←</button>' +
      '<button class="rm-mini" data-act="right" title="Vers la droite">→</button>' +
      '<button class="rm-mini" data-act="edit">Éditer</button>' +
      '<button class="rm-mini del" data-act="del">Suppr.</button>' +
      '</div>';
    d.querySelector('.rm-id').textContent = t.id;
    d.querySelector('.rm-epic').textContent = t.epic;
    const pr = d.querySelector('.rm-prio');
    pr.textContent = PRIO_LABEL[t.priority]; pr.classList.add(t.priority);
    d.querySelector('h4').textContent = t.title;
    if (t.details) d.querySelector('p').textContent = t.details;
    if (meta) d.querySelector('.rm-card-meta').textContent = meta;
    d.querySelector('.rm-grip').addEventListener('pointerdown', (e) => dragStart(e, t.id, d));
    d.querySelectorAll('button').forEach((b) => {
      b.addEventListener('click', (e) => {
        e.stopPropagation();
        const act = b.dataset.act;
        if (act === 'edit') openModal(t.id);
        else if (act === 'del') delTask(t.id);
        else if (act === 'left') moveTask(t.id, -1);
        else if (act === 'right') moveTask(t.id, +1);
      });
    });
    return d;
  }

  /* ---------- drag & drop (pointer, souris + tactile) ---------- */
  let drag = null;
  function dragStart(e, id, cardElmt) {
    if (view().sort !== 'manual') { toast('Repassez en « Tri : manuel » pour réordonner'); return; }
    if (filtersActive()) { toast('Effacez les filtres pour réordonner'); return; }
    e.preventDefault();
    drag = { id, src: cardElmt, x0: e.clientX, y0: e.clientY, active: false, ghost: null, hint: null, target: null, scrollT: null };
    window.addEventListener('pointermove', dragMove, { passive: false });
    window.addEventListener('pointerup', dragEnd, { once: true });
    window.addEventListener('pointercancel', dragCancel, { once: true });
  }
  function dragActivate() {
    const r = drag.src.getBoundingClientRect();
    const g = drag.src.cloneNode(true);
    g.classList.add('rm-ghost');
    g.style.width = r.width + 'px';
    document.body.appendChild(g);
    const hint = document.createElement('div');
    hint.className = 'rm-drop-hint';
    hint.style.height = r.height + 'px';
    drag.ghost = g; drag.hint = hint;
    drag.src.classList.add('drag-src');
    drag.active = true;
    drag.scrollT = setInterval(dragAutoScroll, 60);
    positionGhost(drag.x0, drag.y0);
    updateDropTarget(drag.x0, drag.y0);
  }
  function positionGhost(x, y) {
    if (drag.ghost) { drag.ghost.style.left = (x + 12) + 'px'; drag.ghost.style.top = (y - 20) + 'px'; }
  }
  function colBodies() {
    return STATUSES.map((s) => ({ status: s, body: els['col-' + s], rect: els['col-' + s].getBoundingClientRect() }));
  }
  function updateDropTarget(x, y) {
    drag.target = null;
    document.querySelectorAll('.rm-col').forEach((c) => c.classList.remove('dragover'));
    if (drag.hint.parentNode) drag.hint.parentNode.removeChild(drag.hint);
    for (const { status, body, rect } of colBodies()) {
      if (x >= rect.left - 12 && x <= rect.right + 12 && y >= rect.top - 24 && y <= rect.bottom + 24) {
        const cards = [...body.querySelectorAll('.rm-card:not(.drag-src)')];
        let idx = cards.length;
        for (let i = 0; i < cards.length; i++) {
          const cr = cards[i].getBoundingClientRect();
          if (y < cr.top + cr.height / 2) { idx = i; break; }
        }
        drag.target = { status, index: idx };
        body.closest('.rm-col').classList.add('dragover');
        if (idx >= cards.length) body.appendChild(drag.hint);
        else body.insertBefore(drag.hint, cards[idx]);
        break;
      }
    }
  }
  function dragAutoScroll() {
    if (!drag || !drag.active) return;
    const { clientX: x, clientY: y } = drag.last || {};
    if (x === undefined) return;
    if (y < 110) window.scrollBy(0, -14);
    else if (y > window.innerHeight - 110) window.scrollBy(0, 14);
    for (const { body, rect } of colBodies()) {
      if (x >= rect.left && x <= rect.right) {
        if (y > rect.top && y < rect.top + 70) body.scrollTop -= 12;
        else if (y < rect.bottom && y > rect.bottom - 70) body.scrollTop += 12;
      }
    }
  }
  function dragMove(e) {
    if (!drag) return;
    if (e.cancelable) e.preventDefault();
    drag.last = { clientX: e.clientX, clientY: e.clientY };
    if (!drag.active) {
      if (Math.hypot(e.clientX - drag.x0, e.clientY - drag.y0) < 7) return;
      dragActivate();
    }
    positionGhost(e.clientX, e.clientY);
    updateDropTarget(e.clientX, e.clientY);
  }
  function dragCleanup() {
    window.removeEventListener('pointermove', dragMove);
    if (drag) {
      clearInterval(drag.scrollT);
      if (drag.ghost) drag.ghost.remove();
      if (drag.hint.parentNode) drag.hint.parentNode.removeChild(drag.hint);
      drag.src.classList.remove('drag-src');
      document.querySelectorAll('.rm-col').forEach((c) => c.classList.remove('dragover'));
    }
  }
  function dragCancel() { dragCleanup(); drag = null; }
  function dragEnd() {
    if (!drag) return;
    const wasActive = drag.active;
    const target = drag.target;
    const id = drag.id;
    dragCleanup(); drag = null;
    if (!wasActive || !target) return;
    const t = tasks().find((x) => x.id === id);
    if (!t) return;
    const from = t.status;
    const sibs = siblings(target.status).filter((x) => x.id !== id);
    t.status = target.status;
    const idx = Math.min(Math.max(target.index, 0), sibs.length);
    sibs.splice(idx, 0, t);
    sibs.forEach((x, i) => { x.order = i; });
    if (from !== target.status) renumber(from);
    flashId = id;
    commit(id + ' déplacé → ' + STATUS_LABEL[target.status]);
    render();
  }

  /* ---------- rendu ---------- */
  function render() {
    const b = board();
    if (!b) return;
    const v = view();
    renderTabs();
    if (els.rmBoardName) els.rmBoardName.textContent = b.name;
    els.rmBoardDesc.textContent = (b.desc ? b.desc + ' — ' : '') + 'Attrapez la poignée ⋮⋮ pour réordonner dans et entre colonnes.';

    const cur = v.epic || '';
    const epics = [...new Set(b.tasks.map((t) => t.epic))].sort();
    els.rmEpicFilter.innerHTML = '<option value="">Tous épics</option>' +
      epics.map((e) => '<option value="' + e.replace(/"/g, '&quot;') + '">' + e.replace(/</g, '&lt;') + '</option>').join('');
    if (epics.includes(cur)) els.rmEpicFilter.value = cur; else v.epic = '';
    els.epicList.innerHTML = epics.map((e) => '<option value="' + e.replace(/"/g, '&quot;') + '">').join('');

    els.rmBoard.classList.toggle('locked-order', v.sort !== 'manual');

    const list = filtered();
    const by = { todo: [], in_progress: [], done: [] };
    list.forEach((t) => by[t.status].push(t));
    STATUSES.forEach((s) => {
      const col = els['col-' + s];
      col.innerHTML = '';
      const items = sortList(by[s]);
      if (!items.length) {
        const e = document.createElement('div');
        e.className = 'rm-empty';
        e.textContent = 'Aucune tâche — glissez une carte ici ou ajout rapide ↓';
        col.appendChild(e);
      } else items.forEach((t) => col.appendChild(cardEl(t)));
      const colBox = col.closest('.rm-col');
      colBox.classList.toggle('collapsed', !!v.collapsed[s]);
      colBox.querySelector('.rm-collapse').textContent = v.collapsed[s] ? '+' : '–';
      colBox.classList.toggle('wip-warn', s === 'in_progress' && by[s].length > WIP_LIMIT);
    });

    const total = b.tasks.length;
    const ndone = b.tasks.filter((t) => t.status === 'done').length;
    const ntodo = b.tasks.filter((t) => t.status === 'todo').length;
    const nprog = b.tasks.filter((t) => t.status === 'in_progress').length;
    const pct = total ? Math.round((ndone / total) * 100) : 0;
    els.cTodo.textContent = by.todo.length; els.cProg.textContent = by.in_progress.length; els.cDone.textContent = by.done.length;
    els.stTotal.textContent = total; els.stTodo.textContent = ntodo;
    els.stProg.textContent = nprog; els.stDone.textContent = ndone;
    els.stPct.textContent = pct + '%'; els.rmBar.style.width = pct + '%';
    els.inspTotal.textContent = total;
    els.rmCountLine.textContent = (filtersActive() || v.sort !== 'manual')
      ? list.length + ' / ' + total + ' affichées' : total + ' tâches';

    els.rmEpics.innerHTML = '';
    epics.forEach((ep) => {
      const all = b.tasks.filter((t) => t.epic === ep);
      const dn = all.filter((t) => t.status === 'done').length;
      const p = all.length ? Math.round((dn / all.length) * 100) : 0;
      const row = document.createElement('div');
      row.className = 'rm-epic-row';
      row.innerHTML = '<div class="lbl"><span></span><strong></strong></div><div class="rm-epic-bar"><div></div></div>';
      row.querySelector('span').textContent = ep;
      row.querySelector('strong').textContent = dn + '/' + all.length;
      row.querySelector('.rm-epic-bar > div').style.width = p + '%';
      row.style.cursor = 'pointer';
      row.title = 'Filtrer sur cet épic';
      row.addEventListener('click', () => {
        v.epic = (v.epic === ep) ? '' : ep;
        els.rmEpicFilter.value = v.epic;
        persistLocal(); render();
      });
      els.rmEpics.appendChild(row);
    });
    flashId = null;
  }

  /* ---------- actions tâche ---------- */
  function moveTask(id, dir) {
    const t = tasks().find((x) => x.id === id);
    if (!t) return;
    const i = STATUSES.indexOf(t.status);
    const j = Math.min(2, Math.max(0, i + dir));
    const from = t.status;
    if (i === j) return;
    t.status = STATUSES[j];
    t.order = siblings(t.status).length;
    renumber(from); renumber(t.status);
    flashId = id;
    commit(id + ' → ' + STATUS_LABEL[t.status]);
    render();
  }

  function delTask(id) {
    const arr = tasks();
    const idx = arr.findIndex((x) => x.id === id);
    if (idx === -1) return;
    const [gone] = arr.splice(idx, 1);
    renumber(gone.status);
    commit();
    render();
    toast(id + ' supprimée', { label: 'Annuler', fn: () => {
      arr.splice(Math.min(idx, arr.length), 0, gone);
      renumber(gone.status);
      flashId = id;
      commit(id + ' restaurée');
      render();
    }});
  }

  function quickAdd(status, title) {
    title = (title || '').trim();
    if (!title) return;
    const b = board();
    const t = {
      id: uid(), title, epic: view().epic || 'Divers', status,
      priority: 'medium', order: siblings(status).length,
      scene: '', placeholderId: '', primitive: '',
      pos: null, scale: null, suggestedAsset: '', details: ''
    };
    b.tasks.push(t);
    flashId = t.id;
    commit(t.id + ' créée');
    render();
  }

  /* ---------- modale (édition live + autosave) ---------- */
  function liveChips(t) {
    els.rmLive.innerHTML = '';
    const mk = (txt, cls) => {
      const s = document.createElement('span');
      s.className = cls; s.textContent = txt;
      els.rmLive.appendChild(s);
    };
    mk(t.id, 'rm-id');
    mk(t.epic || 'Divers', 'rm-epic');
    const p = document.createElement('span');
    p.className = 'rm-prio ' + modalPrio; p.textContent = PRIO_LABEL[modalPrio];
    els.rmLive.appendChild(p);
  }

  function applyModalLive() {
    const t = tasks().find((x) => x.id === editingId);
    if (!t) return;
    const newStatus = els.fStatus.value;
    t.title = els.fTitle.value.trim() || '(sans titre)';
    t.details = els.fDetails.value.trim();
    t.epic = els.fEpic.value.trim() || 'Divers';
    t.priority = modalPrio;
    t.placeholderId = els.fPlaceholder.value.trim();
    t.primitive = els.fPrim.value;
    t.suggestedAsset = els.fAsset.value.trim();
    if (newStatus !== t.status) {
      const from = t.status;
      t.status = newStatus;
      t.order = siblings(newStatus).length;
      renumber(from); renumber(newStatus);
    }
    liveChips(t);
    clearTimeout(liveTimer);
    liveTimer = setTimeout(() => { commit(); render(); liveChips(t); }, 500);
  }

  function openModal(id, presetStatus) {
    const arr = tasks();
    const t = id ? arr.find((x) => x.id === id) : null;
    createdInModal = !t;
    if (t) {
      editingId = t.id;
      modalPrio = t.priority;
    } else {
      const nt = {
        id: uid(), title: '', epic: view().epic || 'Divers', status: presetStatus || 'todo',
        priority: 'medium', order: 0, scene: '',
        placeholderId: '', primitive: 'Cube', pos: null, scale: null, suggestedAsset: '', details: ''
      };
      nt.order = siblings(nt.status).length;
      arr.push(nt);
      editingId = nt.id;
      modalPrio = 'medium';
    }
    const cur = arr.find((x) => x.id === editingId);
    els.rmModalTitle.textContent = createdInModal ? 'Nouvelle tâche ' + cur.id : 'Éditer ' + cur.id;
    els.fTitle.value = cur.title === '(sans titre)' ? '' : cur.title;
    els.fDetails.value = cur.details;
    els.fEpic.value = cur.epic === 'Divers' && createdInModal && !view().epic ? '' : cur.epic;
    els.fStatus.value = cur.status;
    els.fPlaceholder.value = cur.placeholderId;
    els.fPrim.value = cur.primitive || 'Cube';
    els.fAsset.value = cur.suggestedAsset;
    syncSeg();
    liveChips(cur);
    els.rmBackdrop.classList.add('open');
    setTimeout(() => els.fTitle.focus(), 60);
  }

  function syncSeg() {
    els.fPrioSeg.querySelectorAll('button').forEach((b) => {
      b.classList.toggle('on', b.dataset.v === modalPrio);
    });
  }

  function closeModal(finished) {
    const arr = tasks();
    const t = arr.find((x) => x.id === editingId);
    clearTimeout(liveTimer);
    if (t && createdInModal && !t.title?.trim() && !t.details?.trim() && !t.placeholderId?.trim() && !t.suggestedAsset?.trim()
        && t.title !== '(sans titre)') {
      const i = arr.findIndex((x) => x.id === t.id);
      if (i !== -1) arr.splice(i, 1);
      renumber(t.status);
      commit();
    } else if (t) {
      if (!t.title.trim()) t.title = '(sans titre)';
      flashId = t.id;
      commit();
    }
    els.rmBackdrop.classList.remove('open');
    editingId = null; createdInModal = false;
    render();
    if (t && finished) toast(t.id + ' autosauvegardée ✓');
    else if (!finished && t && t.title) toast('Brouillon autosauvegardé');
  }

  /* ---------- export / import / reset ---------- */
  function exportJson() {
    const blob = new Blob([JSON.stringify(payload(), null, 2)], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'roadmaps.json';
    document.body.appendChild(a); a.click();
    setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 500);
    toast('roadmaps.json exporté');
  }

  function importJson(file) {
    const r = new FileReader();
    r.onload = () => {
      try {
        const data = JSON.parse(r.result);
        if (Array.isArray(data.boards) && data.boards.length) {
          if (!confirm('Remplacer les ' + store.boards.length + ' roadmaps actuelles par les ' + data.boards.length + ' importées ?')) return;
          adoptStore(data);
          if (data.activeId) store.activeId = data.activeId;
          commit(data.boards.length + ' roadmaps importées');
        } else if (Array.isArray(data.tasks)) {
          // legacy mono-board -> ajoutée comme nouvelle roadmap
          const bd = cleanBoard({ name: data.board || ('Import ' + new Date().toISOString().slice(0, 10)), desc: '', tasks: data.tasks }, store.boards.length);
          // réindexe les ids en collision
          const taken = new Set();
          store.boards.forEach((b) => b.tasks.forEach((t) => taken.add(t.id)));
          bd.tasks.forEach((t) => { if (taken.has(t.id)) { t.id = uid(); taken.add(t.id); } });
          store.boards.push(bd);
          store.activeId = bd.id;
          commit('Roadmap « ' + bd.name + ' » importée');
        } else throw new Error('ni boards[] ni tasks[]');
        syncViewControls(); render();
      } catch (e) { toast('Import invalide : ' + e.message); }
    };
    r.readAsText(file);
  }

  function isTyping() {
    const a = document.activeElement;
    return a && (a.tagName === 'INPUT' || a.tagName === 'TEXTAREA' || a.tagName === 'SELECT');
  }

  document.addEventListener('DOMContentLoaded', () => {
    cacheEls();
    if (!els['col-todo']) return;
    els.rmSearch.addEventListener('input', () => { view().search = els.rmSearch.value; persistLocal(); renderTabs(); render(); });
    els.rmEpicFilter.addEventListener('change', () => { view().epic = els.rmEpicFilter.value; persistLocal(); render(); });
    els.rmPrioFilter.addEventListener('change', () => { view().prio = els.rmPrioFilter.value; persistLocal(); render(); });
    els.rmSort.addEventListener('change', () => { view().sort = els.rmSort.value; persistLocal(); render(); });
    els.rmAdd.addEventListener('click', () => openModal(null));
    document.querySelectorAll('[data-qa]').forEach((inp) => {
      const go = () => { quickAdd(inp.dataset.qa, inp.value); inp.value = ''; inp.focus(); };
      inp.addEventListener('keydown', (e) => { if (e.key === 'Enter') go(); });
      const btn = document.querySelector('[data-qa-btn="' + inp.dataset.qa + '"]');
      if (btn) btn.addEventListener('click', go);
    });
    document.querySelectorAll('.rm-collapse').forEach((b) => {
      b.addEventListener('click', () => {
        const v = view();
        v.collapsed[b.dataset.col] = !v.collapsed[b.dataset.col];
        persistLocal(); render();
      });
    });
    ['fTitle', 'fDetails', 'fEpic', 'fStatus', 'fPlaceholder', 'fPrim', 'fAsset'].forEach((id) => {
      els[id].addEventListener('input', applyModalLive);
      els[id].addEventListener('change', applyModalLive);
    });
    els.fPrioSeg.querySelectorAll('button').forEach((b) => {
      b.addEventListener('click', () => { modalPrio = b.dataset.v; syncSeg(); applyModalLive(); });
    });
    els.rmCancel.addEventListener('click', () => closeModal(false));
    els.rmSave.addEventListener('click', () => closeModal(true));
    els.rmBackdrop.addEventListener('click', (e) => { if (e.target === els.rmBackdrop) closeModal(false); });
    els.rmExport.addEventListener('click', exportJson);
    els.rmImportBtn.addEventListener('click', () => els.rmImport.click());
    els.rmImport.addEventListener('change', (e) => { if (e.target.files[0]) importJson(e.target.files[0]); e.target.value = ''; });
    els.rmReset.addEventListener('click', async () => {
      if (!confirm('Réinitialiser depuis data/roadmaps.json ? (perd les modifs locales)')) return;
      localStorage.removeItem(LS_KEY);
      diskMode = 'unknown';
      store = { boards: [], activeId: null };
      await load();
      toast('Roadmaps réinitialisées');
    });
    els.rmSaveState.style.cursor = 'pointer';
    els.rmSaveState.title = 'État de l’autosave — cliquez pour exporter le JSON';
    els.rmSaveState.addEventListener('click', () => {
      if (diskMode === 'local') exportJson();
      else toast(els.rmSaveLabel.textContent);
    });
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') {
        if (tabEditing) { tabEditing = null; renderTabs(); return; }
        if (els.rmBackdrop.classList.contains('open')) closeModal(false);
        return;
      }
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter' && els.rmBackdrop.classList.contains('open')) { closeModal(true); return; }
      if (isTyping() || e.ctrlKey || e.metaKey || e.altKey) return;
      if (e.key === 'n' || e.key === 'N') { e.preventDefault(); openModal(null); }
      else if (e.key === '/') { e.preventDefault(); els.rmSearch.focus(); els.rmSearch.select(); }
    });
    load();
  });
})();
