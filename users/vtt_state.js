/* ==========================================================================
   VTT STATE — Persistance atomique vtt_<tableId>.json, mutations & anti-maphack
   users/vtt_state.js — zéro dépendance.
   ========================================================================== */
'use strict';

const fs = require('fs');
const path = require('path');
const vttVis = require('./vtt_visibility.js');
const vttRules = require('../js/vtt-rules.js');

const MAX_VTT_FILE_BYTES = 16 * 1024 * 1024;
const VALID_FACTIONS = new Set(['pj', 'ally', 'npc', 'enemy', 'vehicle']);
const VALID_ZONE_KINDS = new Set(['fire', 'smoke', 'gas', 'stasis', 'difficult']);
const VALID_WALL_KINDS = new Set(['wall', 'door', 'window', 'cover_half', 'cover_3q', 'cover_full']);
const VALID_ITEM_STATES = new Set(['Neuf', 'Usé', 'Endommagé', 'Hors service', 'Détruit']);

function genId(prefix) {
  return prefix + '_' + Date.now().toString(36) + Math.floor(Math.random() * 46656).toString(36);
}

function cleanStr(s, maxLen) {
  return String(s == null ? '' : s).replace(/[\u0000-\u001F\u007F]/g, '').trim().slice(0, maxLen || 120);
}

function num(v, fallback, min, max) {
  const n = Number(v);
  if (!Number.isFinite(n)) return fallback;
  if (min !== undefined && n < min) return min;
  if (max !== undefined && n > max) return max;
  return n;
}

function vttFilePath(tablesDir, tableId) {
  return path.join(tablesDir, 'vtt_' + String(tableId || '').toLowerCase() + '.json');
}

function createDefaultScene(id, name) {
  return {
    id: id || 'scn_default',
    name: cleanStr(name || 'Plateau Principal', 60) || 'Plateau Principal',
    kind: 'tactical',
    gridType: 'hex',
    cellSize: 54,
    cols: 28,
    rows: 20,
    offsetX: 0,
    offsetY: 0,
    gridOpacity: 0.35,
    mapUrl: '',
    mapDataUrl: '',
    fogEnabled: false,
    sightRange: 12,
    ambientDarkness: 0,
    tokens: [],
    walls: [],
    zones: [],
    drops: [],
    drawings: [],
    lights: [],
    revealedCells: [],
    hiddenCells: [],
    landmarks: [],
    routes: []
  };
}

function normalizeScene(sc) {
  const s = (sc && typeof sc === 'object') ? sc : {};
  const def = createDefaultScene(cleanStr(s.id || genId('scn'), 40), s.name);
  def.kind = s.kind === 'atlas' ? 'atlas' : 'tactical';
  def.gridType = s.gridType === 'square' ? 'square' : 'hex';
  def.cellSize = num(s.cellSize, 54, 20, 180);
  def.cols = Math.trunc(num(s.cols, 28, 6, 120));
  def.rows = Math.trunc(num(s.rows, 20, 6, 120));
  def.offsetX = num(s.offsetX, 0, -2000, 2000);
  def.offsetY = num(s.offsetY, 0, -2000, 2000);
  def.gridOpacity = num(s.gridOpacity, 0.35, 0, 1);
  def.mapUrl = typeof s.mapUrl === 'string' ? s.mapUrl.slice(0, 2048) : '';
  def.mapDataUrl = typeof s.mapDataUrl === 'string' ? s.mapDataUrl.slice(0, 5000000) : '';
  def.fogEnabled = !!s.fogEnabled;
  def.sightRange = Math.trunc(num(s.sightRange, 12, 1, 32));
  def.ambientDarkness = num(s.ambientDarkness, 0, 0, 1);
  def.tokens = Array.isArray(s.tokens) ? s.tokens : [];
  def.walls = Array.isArray(s.walls) ? s.walls : [];
  def.zones = Array.isArray(s.zones) ? s.zones : [];
  def.drops = Array.isArray(s.drops) ? s.drops : [];
  def.drawings = Array.isArray(s.drawings) ? s.drawings : [];
  def.lights = Array.isArray(s.lights) ? s.lights : [];
  def.revealedCells = Array.isArray(s.revealedCells) ? s.revealedCells : [];
  def.hiddenCells = Array.isArray(s.hiddenCells) ? s.hiddenCells : [];
  def.landmarks = Array.isArray(s.landmarks) ? s.landmarks : [];
  def.routes = Array.isArray(s.routes) ? s.routes : [];
  return def;
}

function createDefaultVttState(tableId) {
  const sc = createDefaultScene('scn_default', 'Scène 1 — Rencontre');
  return {
    version: 1,
    tableId: String(tableId || '').toLowerCase(),
    updatedAt: Date.now(),
    activeSceneId: sc.id,
    paused: false,
    turnLock: false,
    hideEnemyStats: true,
    combat: {
      active: false,
      round: 1,
      turnIndex: 0,
      order: [],
      activeTokenId: null
    },
    clock: {
      day: 1,
      hour: 8,
      minute: 0,
      weather: 'P1',
      lighting: 'day'
    },
    scenes: [sc],
    duels: {},
    handouts: [],
    linkedCharacters: {},
    challenges: [],
    pathologies: [],
    playerScenes: {},
    audio: { ambience: 'none', playing: false }
  };
}

function normalizeState(raw, tableId) {
  if (!raw || typeof raw !== 'object') return createDefaultVttState(tableId);
  const st = createDefaultVttState(tableId);
  st.updatedAt = Number(raw.updatedAt) || Date.now();
  st.paused = !!raw.paused;
  st.turnLock = !!raw.turnLock;
  st.hideEnemyStats = raw.hideEnemyStats !== false;
  if (raw.combat && typeof raw.combat === 'object') {
    st.combat = {
      active: !!raw.combat.active,
      round: Math.max(1, Math.trunc(Number(raw.combat.round) || 1)),
      turnIndex: Math.max(0, Math.trunc(Number(raw.combat.turnIndex) || 0)),
      order: Array.isArray(raw.combat.order) ? raw.combat.order : [],
      activeTokenId: typeof raw.combat.activeTokenId === 'string' ? raw.combat.activeTokenId : null
    };
  }
  if (raw.clock && typeof raw.clock === 'object') {
    st.clock = {
      day: Math.max(1, Math.trunc(Number(raw.clock.day) || 1)),
      hour: Math.trunc(num(raw.clock.hour, 8, 0, 23)),
      minute: Math.trunc(num(raw.clock.minute, 0, 0, 59)),
      weather: cleanStr(raw.clock.weather || 'P1', 12),
      lighting: cleanStr(raw.clock.lighting || 'day', 20)
    };
  }
  if (Array.isArray(raw.scenes) && raw.scenes.length > 0) {
    st.scenes = raw.scenes.map(normalizeScene);
  }
  st.activeSceneId = st.scenes.some((s) => s.id === raw.activeSceneId) ? raw.activeSceneId : st.scenes[0].id;
  st.duels = (raw.duels && typeof raw.duels === 'object' && !Array.isArray(raw.duels)) ? raw.duels : {};
  st.handouts = Array.isArray(raw.handouts) ? raw.handouts : [];
  st.linkedCharacters = (raw.linkedCharacters && typeof raw.linkedCharacters === 'object') ? raw.linkedCharacters : {};
  st.challenges = Array.isArray(raw.challenges) ? raw.challenges : [];
  st.pathologies = Array.isArray(raw.pathologies) ? raw.pathologies : [];
  st.playerScenes = (raw.playerScenes && typeof raw.playerScenes === 'object' && !Array.isArray(raw.playerScenes)) ? raw.playerScenes : {};
  st.audio = (raw.audio && typeof raw.audio === 'object') ? { ambience: cleanStr(raw.audio.ambience || 'none', 24), playing: !!raw.audio.playing } : { ambience: 'none', playing: false };
  return st;
}

function loadVttState(tablesDir, tableId) {
  try { fs.mkdirSync(tablesDir, { recursive: true }); } catch (e) {}
  const file = vttFilePath(tablesDir, tableId);
  if (!fs.existsSync(file)) return createDefaultVttState(tableId);
  try {
    const raw = JSON.parse(fs.readFileSync(file, 'utf-8'));
    return normalizeState(raw, tableId);
  } catch (e) {
    return createDefaultVttState(tableId);
  }
}

function saveVttState(tablesDir, state) {
  try { fs.mkdirSync(tablesDir, { recursive: true }); } catch (e) {}
  state.updatedAt = Date.now();
  const file = vttFilePath(tablesDir, state.tableId);
  const json = JSON.stringify(state);
  if (Buffer.byteLength(json, 'utf8') > MAX_VTT_FILE_BYTES) return false;
  const tmp = file + '.tmp';
  fs.writeFileSync(tmp, json);
  try { if (fs.existsSync(file)) fs.copyFileSync(file, file + '.bak'); } catch (e) {}
  fs.renameSync(tmp, file);
  return true;
}

function getActiveScene(state, sceneId) {
  const id = sceneId || state.activeSceneId;
  return state.scenes.find((s) => s.id === id) || state.scenes[0];
}

function sanitizeInventoryItem(raw) {
  const it = (raw && typeof raw === 'object') ? raw : {};
  return {
    id: cleanStr(it.id || genId('itm'), 48),
    objId: cleanStr(it.objId || '', 64),
    nom: cleanStr(it.nom || it.name || 'Objet', 64),
    dlph: cleanStr(it.dlph || '', 32),
    categorie: cleanStr(it.categorie || '', 32),
    degats: Math.trunc(num(it.degats, 0, 0, 200)),
    perforante: Math.trunc(num(it.perforante, 0, 0, 30)),
    protection: Math.trunc(num(it.protection, 0, 0, 30)),
    encombrement_pa: Math.trunc(num(it.encombrement_pa, 0, 0, 10)),
    poids_livres: num(it.poids_livres, 0, 0, 500),
    munitions: it.munitions === null || it.munitions === undefined ? null : Math.trunc(num(it.munitions, 0, 0, 9999)),
    munitionsMax: it.munitionsMax === null || it.munitionsMax === undefined ? null : Math.trunc(num(it.munitionsMax, 0, 0, 9999)),
    quantite: Math.trunc(num(it.quantite, 1, 1, 999)),
    etatObjet: VALID_ITEM_STATES.has(it.etatObjet) ? it.etatObjet : 'Neuf'
  };
}

function sanitizeTokenInput(p, existing) {
  const base = existing ? { ...existing } : {
    id: cleanStr(p.id || genId('tok'), 48) || genId('tok'),
    name: 'Pion',
    faction: 'pj',
    q: 2,
    r: 2,
    size: 1,
    color: '#38bdf8',
    avatar: '',
    ownerUserId: null,
    pjId: null,
    con: 3,
    pa: 6,
    maxPa: 6,
    pe: 0,
    maxPe: 3,
    armor: 0,
    armorEnc: 0,
    thermal: false,
    vision: 6,
    lightOn: false,
    lightRange: 10,
    vigilant: false,
    statuses: [],
    dots: {},
    zones: { head: 0, torso: 0, lArm: 0, rArm: 0, lLeg: 0, rLeg: 0 },
    attrs: null,
    inventory: [],
    xpTicks: 0,
    auraRadius: 0,
    auraColor: '#38bdf8',
    gmOnly: false,
    hidden: false,
    freeReactionUsed: false,
    notes: ''
  };
  if (p.name !== undefined) base.name = cleanStr(p.name, 48) || base.name;
  if (p.faction !== undefined && VALID_FACTIONS.has(p.faction)) base.faction = p.faction;
  if (p.q !== undefined) base.q = Math.trunc(num(p.q, base.q, -100, 300));
  if (p.r !== undefined) base.r = Math.trunc(num(p.r, base.r, -100, 300));
  if (p.size !== undefined) base.size = Math.trunc(num(p.size, base.size, 1, 5));
  if (p.color !== undefined && /^#[0-9a-fA-F]{3,8}$/.test(String(p.color))) base.color = String(p.color);
  if (p.avatar !== undefined) base.avatar = typeof p.avatar === 'string' ? p.avatar.slice(0, 2048) : '';
  if (p.ownerUserId !== undefined) base.ownerUserId = p.ownerUserId ? cleanStr(p.ownerUserId, 64) : null;
  if (p.pjId !== undefined) base.pjId = p.pjId ? cleanStr(p.pjId, 64) : null;
  if (p.con !== undefined) base.con = Math.trunc(num(p.con, base.con, 1, 10));
  if (p.maxPa !== undefined) base.maxPa = Math.trunc(num(p.maxPa, base.maxPa, 1, 30));
  if (p.pa !== undefined) base.pa = Math.trunc(num(p.pa, base.pa, 0, 40));
  if (p.maxPe !== undefined) base.maxPe = Math.trunc(num(p.maxPe, base.maxPe, 1, 15));
  if (p.pe !== undefined) base.pe = Math.trunc(num(p.pe, base.pe, 0, 20));
  if (p.armor !== undefined) base.armor = Math.trunc(num(p.armor, base.armor, 0, 30));
  if (p.armorEnc !== undefined) base.armorEnc = Math.trunc(num(p.armorEnc, base.armorEnc, 0, 6));
  if (p.thermal !== undefined) base.thermal = !!p.thermal;
  if (p.vision !== undefined) base.vision = Math.trunc(num(p.vision, base.vision, 1, 32));
  if (p.lightOn !== undefined) base.lightOn = !!p.lightOn;
  if (p.lightRange !== undefined) base.lightRange = Math.trunc(num(p.lightRange, 10, 0, 32));
  if (p.vigilant !== undefined) base.vigilant = !!p.vigilant;
  if (Array.isArray(p.statuses)) base.statuses = [...new Set(p.statuses.map((x) => cleanStr(x, 32)).filter(Boolean))].slice(0, 24);
  if (p.dots && typeof p.dots === 'object') {
    const cleanDots = {};
    for (const k of vttRules.STATUS_DOT) {
      const val = Math.trunc(num(p.dots[k], 0, 0, 20));
      if (val > 0) cleanDots[k] = val;
    }
    base.dots = cleanDots;
  }
  if (p.zones && typeof p.zones === 'object') {
    const z = { ...base.zones };
    for (const bz of vttRules.BODY_ZONES) {
      if (p.zones[bz.id] !== undefined) z[bz.id] = Math.trunc(num(p.zones[bz.id], z[bz.id] || 0, 0, 200));
    }
    base.zones = z;
  }
  if (p.attrs && typeof p.attrs === 'object') {
    base.attrs = {
      for: Math.trunc(num(p.attrs.for, 3, 1, 10)),
      agi: Math.trunc(num(p.attrs.agi, 3, 1, 10)),
      con: Math.trunc(num(p.attrs.con, 3, 1, 10)),
      rap: Math.trunc(num(p.attrs.rap, 3, 1, 10)),
      int: Math.trunc(num(p.attrs.int, 3, 1, 10)),
      cha: Math.trunc(num(p.attrs.cha, 3, 1, 10)),
      ins: Math.trunc(num(p.attrs.ins, 3, 1, 10)),
      mag: Math.trunc(num(p.attrs.mag, 0, 0, 10))
    };
  }
  if (Array.isArray(p.inventory)) {
    base.inventory = p.inventory.slice(0, 40).map(sanitizeInventoryItem);
  }
  if (p.xpTicks !== undefined) base.xpTicks = Math.trunc(num(p.xpTicks, 0, 0, 9999));
  if (p.auraRadius !== undefined) base.auraRadius = num(p.auraRadius, 0, 0, 20);
  if (p.auraColor !== undefined && /^#[0-9a-fA-F]{3,8}$/.test(String(p.auraColor))) base.auraColor = String(p.auraColor);
  if (p.vehicle && typeof p.vehicle === 'object') {
    const pv = base.vehicle || { essentialDmg: 0, essentialMax: 15, superficialDmg: 0, superficialMax: 20, caliberClass: 'Léger', passengers: [] };
    base.vehicle = {
      essentialDmg: Math.trunc(num(p.vehicle.essentialDmg, pv.essentialDmg, 0, 500)),
      essentialMax: Math.trunc(num(p.vehicle.essentialMax, pv.essentialMax, 1, 500)),
      superficialDmg: Math.trunc(num(p.vehicle.superficialDmg, pv.superficialDmg, 0, 500)),
      superficialMax: Math.trunc(num(p.vehicle.superficialMax, pv.superficialMax, 1, 500)),
      caliberClass: cleanStr(p.vehicle.caliberClass || pv.caliberClass || 'Léger', 32),
      passengers: Array.isArray(p.vehicle.passengers) ? p.vehicle.passengers.slice(0, 16).map((x) => cleanStr(x, 48)) : (pv.passengers || [])
    };
  } else if (base.faction === 'vehicle' && !base.vehicle) {
    base.vehicle = { essentialDmg: 0, essentialMax: 15, superficialDmg: 0, superficialMax: 20, caliberClass: 'Léger', passengers: [] };
  }
  if (p.gmOnly !== undefined) base.gmOnly = !!p.gmOnly;
  if (p.hidden !== undefined) base.hidden = !!p.hidden;
  if (p.freeReactionUsed !== undefined) base.freeReactionUsed = !!p.freeReactionUsed;
  if (p.notes !== undefined) base.notes = cleanStr(p.notes, 500);
  return base;
}

function buildCoversMap(scene) {
  const covers = new Map();
  if (!scene || !Array.isArray(scene.walls)) return covers;
  for (const w of scene.walls) {
    if (!w) continue;
    if (w.kind === 'door' && w.open) continue;
    if (w.kind === 'window' || w.kind === 'cover_half' || w.kind === 'cover_3q') continue;
    if (Array.isArray(w.cells) && w.cells.length > 0) {
      for (const c of w.cells) {
        if (c && Number.isFinite(c.q) && Number.isFinite(c.r)) covers.set(c.q + ',' + c.r, vttVis.COVER_FULL);
      }
    } else if (Number.isFinite(w.q) && Number.isFinite(w.r)) {
      covers.set(w.q + ',' + w.r, vttVis.COVER_FULL);
    }
  }
  return covers;
}

function getObserversForViewer(scene, viewerId) {
  const sight = vttVis.clampSightRange(scene.sightRange, vttVis.LONG_SIGHT_RANGE);
  const owned = [];
  const party = [];
  for (const t of scene.tokens) {
    if (!t || !Number.isFinite(t.q) || !Number.isFinite(t.r)) continue;
    if (t.gmOnly) continue;
    const obsRange = t.lightOn ? Math.max(sight, t.lightRange || 10) : sight;
    const obs = { q: t.q, r: t.r, vision: obsRange, thermal: !!t.thermal };
    if (viewerId && t.ownerUserId === viewerId) owned.push(obs);
    if (t.faction === 'pj' || t.faction === 'ally') party.push(obs);
  }
  return owned.length > 0 ? owned : party;
}

function filterTokenForPlayer(token, hideEnemyStats) {
  if (!hideEnemyStats || token.faction !== 'enemy') return { ...token };
  const ev = vttRules.evaluateBodyDamage(token);
  return {
    id: token.id,
    name: token.name,
    faction: token.faction,
    q: token.q,
    r: token.r,
    size: token.size,
    color: token.color,
    avatar: token.avatar,
    lightOn: !!token.lightOn,
    lightRange: token.lightRange || 10,
    statuses: Array.isArray(token.statuses) ? token.statuses.slice() : [],
    dots: token.dots ? { ...token.dots } : {},
    auraRadius: token.auraRadius,
    auraColor: token.auraColor,
    healthLabel: ev.healthLabel,
    statsMasked: true
  };
}

function filterStateForViewer(state, viewer) {
  const v = viewer || {};
  const isGM = !!v.isGM;
  const userId = v.userId || null;

  if (isGM) {
    return JSON.parse(JSON.stringify(state));
  }

  const copy = {
    version: state.version,
    tableId: state.tableId,
    updatedAt: state.updatedAt,
    activeSceneId: (userId && state.playerScenes && state.playerScenes[userId]) || state.activeSceneId,
    mySceneId: (userId && state.playerScenes && state.playerScenes[userId]) || state.activeSceneId,
    paused: state.paused,
    turnLock: state.turnLock,
    hideEnemyStats: state.hideEnemyStats,
    combat: JSON.parse(JSON.stringify(state.combat)),
    clock: { ...state.clock },
    handouts: (state.handouts || []).filter((h) => h && (!h.targetUserIds || !h.targetUserIds.length || h.targetUserIds.includes(userId))),
    linkedCharacters: state.linkedCharacters || {},
    challenges: state.challenges || [],
    pathologies: state.pathologies || [],
    audio: state.audio || { ambience: 'none', playing: false },
    duels: {},
    scenes: []
  };

  for (const [did, duel] of Object.entries(state.duels || {})) {
    if (!duel) continue;
    if (duel.revealed) {
      copy.duels[did] = JSON.parse(JSON.stringify(duel));
    } else {
      let myBet = null;
      if (userId && duel.attackerOwnerId === userId && duel.attackerBet) myBet = { side: 'attacker', ...duel.attackerBet };
      else if (userId && duel.defenderOwnerId === userId && duel.defenderBet) myBet = { side: 'defender', ...duel.defenderBet };
      copy.duels[did] = {
        id: duel.id,
        sceneId: duel.sceneId,
        attackerTokenId: duel.attackerTokenId,
        defenderTokenId: duel.defenderTokenId,
        attackerName: duel.attackerName,
        defenderName: duel.defenderName,
        attackerOwnerId: duel.attackerOwnerId,
        defenderOwnerId: duel.defenderOwnerId,
        weaponName: duel.weaponName,
        weaponDmg: duel.weaponDmg,
        piercing: duel.piercing,
        targetZone: duel.targetZone,
        aimLevel: duel.aimLevel,
        atkDie: duel.atkDie,
        defDie: duel.defDie,
        atkTrainings: duel.atkTrainings,
        defTrainings: duel.defTrainings,
        attackerReady: !!duel.attackerBet,
        defenderReady: !!duel.defenderBet,
        myBet,
        revealed: false
      };
    }
  }

  for (const sc of state.scenes || []) {
    const covers = sc.fogEnabled ? buildCoversMap(sc) : null;
    const observers = sc.fogEnabled ? getObserversForViewer(sc, userId) : [];
    const revealedSet = new Set(sc.revealedCells || []);
    const hiddenSet = new Set(sc.hiddenCells || []);

    const visibleTokens = [];
    for (const tok of sc.tokens || []) {
      if (!tok) continue;
      if (tok.gmOnly) continue;
      if (tok.hidden && tok.ownerUserId !== userId) continue;
      if (sc.fogEnabled && tok.ownerUserId !== userId && tok.faction !== 'pj' && tok.faction !== 'ally') {
        const cellKey = tok.q + ',' + tok.r;
        if (hiddenSet.has(cellKey)) continue;
        const manualCell = revealedSet.has(cellKey);
        if (!manualCell) {
          if (observers.length === 0) continue;
          if (!vttVis.isVisibleToAny(observers, { q: tok.q, r: tok.r }, covers)) continue;
        }
      }
      visibleTokens.push(filterTokenForPlayer(tok, state.hideEnemyStats));
    }

    const visibleZones = (sc.zones || []).filter((z) => {
      if (!z || z.gmOnly) return false;
      if (sc.fogEnabled && observers.length > 0 && Number.isFinite(z.q) && Number.isFinite(z.r)) {
        return revealedSet.has(z.q + ',' + z.r) || vttVis.isVisibleToAny(observers, { q: z.q, r: z.r }, covers);
      }
      return true;
    });

    copy.scenes.push({
      id: sc.id,
      name: sc.name,
      kind: sc.kind || 'tactical',
      gridType: sc.gridType,
      cellSize: sc.cellSize,
      cols: sc.cols,
      rows: sc.rows,
      offsetX: sc.offsetX,
      offsetY: sc.offsetY,
      gridOpacity: sc.gridOpacity,
      mapUrl: sc.mapUrl,
      mapDataUrl: sc.mapDataUrl,
      fogEnabled: sc.fogEnabled,
      sightRange: sc.sightRange,
      ambientDarkness: sc.ambientDarkness,
      tokens: visibleTokens,
      walls: (sc.walls || []).filter((w) => w && !w.gmOnly),
      zones: visibleZones,
      drops: sc.drops || [],
      drawings: (sc.drawings || []).filter((d) => d && !d.gmOnly),
      lights: (sc.lights || []).filter((l) => l && !l.gmOnly),
      revealedCells: sc.revealedCells || [],
      hiddenCells: sc.hiddenCells || [],
      landmarks: sc.landmarks || [],
      routes: sc.routes || []
    });
  }

  return copy;
}

function advanceTurn(state, dir) {
  const sc = getActiveScene(state);
  const order = state.combat.order || [];
  if (!order.length) return;
  const len = order.length;
  let nextIdx = state.combat.turnIndex + dir;
  let roundChanged = false;
  if (nextIdx >= len) {
    nextIdx = 0;
    state.combat.round += 1;
    roundChanged = true;
  } else if (nextIdx < 0) {
    nextIdx = len - 1;
    state.combat.round = Math.max(1, state.combat.round - 1);
  }
  state.combat.turnIndex = nextIdx;
  const entry = order[nextIdx];
  state.combat.activeTokenId = entry ? entry.tokenId : null;

  if (dir > 0 && state.combat.activeTokenId) {
    const tok = sc.tokens.find((t) => t.id === state.combat.activeTokenId);
    if (tok) vttRules.tickTurnStart(tok);
  }
  if (roundChanged && dir > 0) {
    sc.zones = (sc.zones || []).filter((z) => {
      if (!z || z.turnsLeft === null || z.turnsLeft === undefined) return true;
      z.turnsLeft = Math.trunc(Number(z.turnsLeft)) - 1;
      return z.turnsLeft > 0;
    });
  }
}

function resolveDuelInState(state, duel, rngFn) {
  const sc = getActiveScene(state, duel.sceneId);
  const atkTok = sc.tokens.find((t) => t.id === duel.attackerTokenId);
  const defTok = sc.tokens.find((t) => t.id === duel.defenderTokenId);

  const ab = duel.attackerBet || { effortPa: 0, effortPe: 0, malus: 0, die: duel.atkDie || 'd6', trainings: duel.atkTrainings || 0 };
  const db = duel.defenderBet || { effortPa: 0, effortPe: 0, malus: 0, passive: true, noPa: false, die: duel.defDie || 'd6', trainings: duel.defTrainings || 0 };

  const atkAimMalus = duel.aimLevel === 2 ? 2 : 0;
  const atkRollRes = vttRules.executeRoll({
    spec: vttRules.parseDieSpec(ab.die || duel.atkDie || 'd6') || vttRules.parseDieSpec('d6'),
    effort: 0,
    malus: 0,
    trainings: ab.trainings ?? duel.atkTrainings ?? 0
  }, rngFn);

  const defRollRes = db.passive ? { total: 0, rolls: [], isCritical: false, isFumble: false } : vttRules.executeRoll({
    spec: vttRules.parseDieSpec(db.die || duel.defDie || 'd6') || vttRules.parseDieSpec('d6'),
    effort: 0,
    malus: 0,
    trainings: db.trainings ?? duel.defTrainings ?? 0
  }, rngFn);

  const defStatuses = (defTok && Array.isArray(defTok.statuses)) ? defTok.statuses : [];
  const targetIncap = defStatuses.includes('Paralysé') || defStatuses.includes('Inconscient');

  const res = vttRules.resolveFaceToFace({
    atkRoll: atkRollRes.total,
    atkEffort: (ab.effortPa || 0) + (ab.effortPe || 0),
    atkMalus: (ab.malus || 0) + atkAimMalus,
    atkTrainings: ab.trainings ?? duel.atkTrainings ?? 0,
    defRoll: defRollRes.total,
    defEffort: (db.effortPa || 0) + (db.effortPe || 0),
    defMalus: db.malus || 0,
    defTrainings: db.trainings ?? duel.defTrainings ?? 0,
    defPassive: !!db.passive,
    defNoPa: !!db.noPa,
    weaponDmg: duel.weaponDmg || 0,
    armor: defTok ? (defTok.armor || 0) : 0,
    piercing: duel.piercing || 0,
    targetIsPassive: !!db.passive,
    targetIncapacitated: targetIncap
  });

  if (atkTok) {
    atkTok.pa = Math.max(0, (atkTok.pa || 0) - (ab.effortPa || 0));
    atkTok.pe = Math.min(atkTok.maxPe || 10, (atkTok.pe || 0) + (ab.effortPe || 0));
  }
  let bodyEval = null;
  if (defTok) {
    if (!db.passive) {
      defTok.pa = Math.max(0, (defTok.pa || 0) - (db.effortPa || 0));
      defTok.pe = Math.min(defTok.maxPe || 10, (defTok.pe || 0) + (db.effortPe || 0));
    }
    const zoneId = vttRules.BODY_ZONES.some((z) => z.id === duel.targetZone) ? duel.targetZone : 'torso';
    if (!defTok.zones) defTok.zones = { head: 0, torso: 0, lArm: 0, rArm: 0, lLeg: 0, rLeg: 0 };
    if (res.finalDamage > 0) {
      defTok.zones[zoneId] = Math.max(0, (defTok.zones[zoneId] || 0) + res.finalDamage);
    }
    bodyEval = vttRules.evaluateBodyDamage(defTok);
  }

  duel.revealed = true;
  duel.result = {
    ...res,
    atkRoll: atkRollRes,
    defRoll: defRollRes,
    targetZone: duel.targetZone || 'torso',
    weaponName: duel.weaponName || 'Attaque',
    bodyEval
  };
  return duel.result;
}

function applyVttMutation(state, op, payload, actor) {
  const p = (payload && typeof payload === 'object') ? payload : {};
  const isGM = !!(actor && actor.isGM);
  const userId = (actor && actor.userId) || null;
  const sc = getActiveScene(state, p.sceneId);

  if (op === 'vtt_ping') {
    return {
      ok: true,
      changed: false,
      ephemeral: true,
      delta: {
        op: 'vtt_ping',
        sceneId: sc.id,
        q: Math.trunc(num(p.q, 0, -200, 400)),
        r: Math.trunc(num(p.r, 0, -200, 400)),
        x: num(p.x, 0, -50000, 50000),
        y: num(p.y, 0, -50000, 50000),
        color: /^#[0-9a-fA-F]{3,8}$/.test(String(p.color || '')) ? p.color : '#38bdf8',
        label: cleanStr(p.label || (actor && actor.username) || '', 32),
        fromName: (actor && actor.username) || '?'
      }
    };
  }

  if (op === 'vtt_focus_camera') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Réservé au MJ.' };
    return {
      ok: true,
      changed: false,
      ephemeral: true,
      delta: {
        op: 'vtt_focus_camera',
        sceneId: sc.id,
        x: num(p.x, 0, -50000, 50000),
        y: num(p.y, 0, -50000, 50000),
        zoom: num(p.zoom, 1, 0.25, 3)
      }
    };
  }

  if (op === 'vtt_scene') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Gestion des scènes réservée au MJ.' };
    const act = p.action || 'update';
    if (act === 'create') {
      const created = createDefaultScene(genId('scn'), p.name || ('Scène ' + (state.scenes.length + 1)));
      if (p.gridType === 'square' || p.gridType === 'hex') created.gridType = p.gridType;
      state.scenes.push(created);
      state.activeSceneId = created.id;
      return { ok: true, changed: true };
    }
    if (act === 'load_atlas') {
      let atlasSc = state.scenes.find((s) => s.kind === 'atlas');
      if (!atlasSc) {
        atlasSc = createDefaultScene('scn_atlas', cleanStr(p.name || 'Atlas — Hybris', 60));
        atlasSc.kind = 'atlas';
        atlasSc.gridType = 'hex';
        atlasSc.cols = 34;
        atlasSc.rows = 24;
        state.scenes.push(atlasSc);
      }
      if (Array.isArray(p.landmarks)) atlasSc.landmarks = p.landmarks.slice(0, 120);
      if (Array.isArray(p.routes)) atlasSc.routes = p.routes.slice(0, 80);
      state.activeSceneId = atlasSc.id;
      return { ok: true, changed: true };
    }
    if (act === 'select') {
      const found = state.scenes.find((s) => s.id === p.targetSceneId);
      if (!found) return { ok: false, code: 'not_found', error: 'Scène introuvable.' };
      state.activeSceneId = found.id;
      return { ok: true, changed: true };
    }
    if (act === 'assign_player') {
      const targetUid = cleanStr(p.targetUserId || '', 64);
      if (!targetUid) return { ok: false, code: 'bad_request', error: 'targetUserId requis.' };
      if (!state.playerScenes) state.playerScenes = {};
      if (!p.targetSceneId) {
        delete state.playerScenes[targetUid];
      } else {
        const found = state.scenes.find((s) => s.id === p.targetSceneId);
        if (!found) return { ok: false, code: 'not_found', error: 'Scène introuvable.' };
        state.playerScenes[targetUid] = found.id;
      }
      return { ok: true, changed: true };
    }
    if (act === 'delete') {
      if (state.scenes.length <= 1) return { ok: false, code: 'bad_request', error: 'Impossible de supprimer la dernière scène.' };
      state.scenes = state.scenes.filter((s) => s.id !== (p.targetSceneId || sc.id));
      if (!state.scenes.some((s) => s.id === state.activeSceneId)) state.activeSceneId = state.scenes[0].id;
      return { ok: true, changed: true };
    }
    if (act === 'update') {
      if (p.name !== undefined) sc.name = cleanStr(p.name, 60) || sc.name;
      if (p.gridType === 'hex' || p.gridType === 'square') sc.gridType = p.gridType;
      if (p.cellSize !== undefined) sc.cellSize = num(p.cellSize, sc.cellSize, 20, 180);
      if (p.cols !== undefined) sc.cols = Math.trunc(num(p.cols, sc.cols, 6, 120));
      if (p.rows !== undefined) sc.rows = Math.trunc(num(p.rows, sc.rows, 6, 120));
      if (p.offsetX !== undefined) sc.offsetX = num(p.offsetX, sc.offsetX, -2000, 2000);
      if (p.offsetY !== undefined) sc.offsetY = num(p.offsetY, sc.offsetY, -2000, 2000);
      if (p.gridOpacity !== undefined) sc.gridOpacity = num(p.gridOpacity, sc.gridOpacity, 0, 1);
      if (p.mapUrl !== undefined) sc.mapUrl = String(p.mapUrl || '').trim().slice(0, 2048);
      if (p.clearMap) { sc.mapUrl = ''; sc.mapDataUrl = ''; }
      if (p.fogEnabled !== undefined) sc.fogEnabled = !!p.fogEnabled;
      if (p.sightRange !== undefined) sc.sightRange = Math.trunc(num(p.sightRange, sc.sightRange, 1, 32));
      if (p.ambientDarkness !== undefined) sc.ambientDarkness = num(p.ambientDarkness, sc.ambientDarkness, 0, 1);
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action scène inconnue.' };
  }

  if (op === 'vtt_settings') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Réservé au MJ.' };
    if (p.paused !== undefined) state.paused = !!p.paused;
    if (p.turnLock !== undefined) state.turnLock = !!p.turnLock;
    if (p.hideEnemyStats !== undefined) state.hideEnemyStats = !!p.hideEnemyStats;
    if (p.clock && typeof p.clock === 'object') {
      state.clock = {
        day: Math.max(1, Math.trunc(num(p.clock.day, state.clock.day, 1, 9999))),
        hour: Math.trunc(num(p.clock.hour, state.clock.hour, 0, 23)),
        minute: Math.trunc(num(p.clock.minute, state.clock.minute, 0, 59)),
        weather: cleanStr(p.clock.weather || state.clock.weather, 12),
        lighting: cleanStr(p.clock.lighting || state.clock.lighting, 20)
      };
    }
    return { ok: true, changed: true };
  }

  if (op === 'vtt_fog') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Brouillard réservé au MJ.' };
    const act = p.action || 'reveal';
    const rev = new Set(sc.revealedCells || []);
    const hid = new Set(sc.hiddenCells || []);
    if (act === 'reveal' && Array.isArray(p.cells)) {
      for (const c of p.cells.slice(0, 200)) {
        if (!c || !Number.isFinite(Number(c.q)) || !Number.isFinite(Number(c.r))) continue;
        const k = Math.trunc(Number(c.q)) + ',' + Math.trunc(Number(c.r));
        rev.add(k);
        hid.delete(k);
      }
      sc.revealedCells = [...rev].slice(0, 4000);
      sc.hiddenCells = [...hid].slice(0, 4000);
      return { ok: true, changed: true };
    }
    if (act === 'hide' && Array.isArray(p.cells)) {
      for (const c of p.cells.slice(0, 200)) {
        if (!c || !Number.isFinite(Number(c.q)) || !Number.isFinite(Number(c.r))) continue;
        const k = Math.trunc(Number(c.q)) + ',' + Math.trunc(Number(c.r));
        rev.delete(k);
        hid.add(k);
      }
      sc.revealedCells = [...rev].slice(0, 4000);
      sc.hiddenCells = [...hid].slice(0, 4000);
      return { ok: true, changed: true };
    }
    if (act === 'reset') {
      sc.revealedCells = [];
      sc.hiddenCells = [];
      return { ok: true, changed: true };
    }
    if (act === 'toggle') {
      sc.fogEnabled = p.fogEnabled !== undefined ? !!p.fogEnabled : !sc.fogEnabled;
      return { ok: true, changed: true };
    }
    if (act === 'sight') {
      sc.sightRange = Math.trunc(num(p.sightRange, 12, 1, 32));
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action brouillard inconnue.' };
  }

  if (op === 'vtt_light') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Lumières réservées au MJ.' };
    const act = p.action || 'add';
    if (act === 'add') {
      if (sc.lights.length >= 100) return { ok: false, code: 'limit', error: 'Trop de sources lumineuses.' };
      sc.lights.push({
        id: cleanStr(p.id || genId('lgt'), 48),
        q: Math.trunc(num(p.q, 0, -100, 300)),
        r: Math.trunc(num(p.r, 0, -100, 300)),
        x: num(p.x, 0, -50000, 50000),
        y: num(p.y, 0, -50000, 50000),
        range: Math.trunc(num(p.range, 10, 1, 32)),
        color: /^#[0-9a-fA-F]{3,8}$/.test(String(p.color || '')) ? p.color : '#fbbf24',
        label: cleanStr(p.label || 'Torche 10P', 40),
        on: p.on !== false,
        gmOnly: !!p.gmOnly
      });
      return { ok: true, changed: true };
    }
    if (act === 'toggle') {
      const l = sc.lights.find((x) => x.id === p.lightId);
      if (l) l.on = !l.on;
      return { ok: true, changed: !!l };
    }
    if (act === 'delete') {
      sc.lights = sc.lights.filter((x) => x.id !== p.lightId);
      return { ok: true, changed: true };
    }
    if (act === 'clear') {
      sc.lights = [];
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action lumière inconnue.' };
  }

  if (op === 'vtt_token') {
    const act = p.action || 'update';
    if (act === 'add') {
      if (!isGM && !p.fromPjLink) return { ok: false, code: 'forbidden', error: 'Ajout de pion réservé au MJ.' };
      if (sc.tokens.length >= 200) return { ok: false, code: 'limit', error: 'Limite de 200 pions par scène.' };
      const rawTok = { ...(p.token || p) };
      if (!isGM) {
        rawTok.faction = 'pj';
        rawTok.ownerUserId = userId;
        rawTok.gmOnly = false;
      }
      const tok = sanitizeTokenInput(rawTok, null);
      sc.tokens.push(tok);
      return { ok: true, changed: true };
    }
    const tid = cleanStr(p.tokenId || (p.token && p.token.id) || '', 48);
    const idx = sc.tokens.findIndex((t) => t.id === tid);
    if (idx === -1) return { ok: false, code: 'not_found', error: 'Pion introuvable.' };
    const tok = sc.tokens[idx];

    if (act === 'delete') {
      if (!isGM) return { ok: false, code: 'forbidden', error: 'Suppression réservée au MJ.' };
      sc.tokens.splice(idx, 1);
      state.combat.order = (state.combat.order || []).filter((o) => o.tokenId !== tid);
      if (state.combat.activeTokenId === tid) state.combat.activeTokenId = state.combat.order[0] ? state.combat.order[0].tokenId : null;
      return { ok: true, changed: true };
    }

    if (act === 'claim') {
      if (tok.faction !== 'pj' && tok.faction !== 'ally' && !isGM) {
        return { ok: false, code: 'forbidden', error: 'Seuls les pions PJ/Alliés peuvent être revendiqués.' };
      }
      if (tok.ownerUserId && tok.ownerUserId !== userId && !isGM) {
        return { ok: false, code: 'forbidden', error: 'Pion déjà revendiqué par un autre joueur.' };
      }
      tok.ownerUserId = p.unclaim ? null : (isGM && p.targetUserId ? cleanStr(p.targetUserId, 64) : userId);
      return { ok: true, changed: true };
    }

    const canControl = isGM || (userId && tok.ownerUserId === userId);
    if (!canControl) return { ok: false, code: 'forbidden', error: 'Ce pion ne vous appartient pas.' };

    if (act === 'move') {
      if (!isGM) {
        if (state.paused) return { ok: false, code: 'paused', error: 'Plateau en pause par le MJ.' };
        if (state.turnLock && state.combat.active && state.combat.activeTokenId !== tok.id) {
          return { ok: false, code: 'not_your_turn', error: 'Verrou tour actif : attendez votre tour.' };
        }
      }
      tok.q = Math.trunc(num(p.q, tok.q, -100, 300));
      tok.r = Math.trunc(num(p.r, tok.r, -100, 300));
      if (p.spendAp && Number.isFinite(Number(p.apCost))) {
        const cost = Math.max(0, Math.trunc(Number(p.apCost)));
        tok.pa = Math.max(0, tok.pa - cost);
      }
      return { ok: true, changed: true };
    }

    if (act === 'add_item') {
      if (!Array.isArray(tok.inventory)) tok.inventory = [];
      const items = Array.isArray(p.items) ? p.items : [p.item || p];
      for (const rawIt of items) {
        if (tok.inventory.length >= 40) break;
        tok.inventory.push(sanitizeInventoryItem(rawIt));
      }
      return { ok: true, changed: true };
    }

    if (act === 'update_item') {
      const it = (tok.inventory || []).find((x) => x.id === p.itemId);
      if (!it) return { ok: false, code: 'not_found', error: 'Objet introuvable.' };
      if (p.munitions !== undefined && it.munitions !== null) it.munitions = Math.trunc(num(p.munitions, it.munitions, 0, 9999));
      if (p.quantite !== undefined) it.quantite = Math.trunc(num(p.quantite, it.quantite, 1, 999));
      if (p.etatObjet !== undefined && VALID_ITEM_STATES.has(p.etatObjet)) it.etatObjet = p.etatObjet;
      return { ok: true, changed: true };
    }

    if (act === 'remove_item') {
      tok.inventory = (tok.inventory || []).filter((x) => x.id !== p.itemId);
      return { ok: true, changed: true };
    }

    if (act === 'drop_item') {
      const iIdx = (tok.inventory || []).findIndex((x) => x.id === p.itemId);
      if (iIdx === -1) return { ok: false, code: 'not_found', error: 'Objet introuvable.' };
      const it = tok.inventory[iIdx];
      tok.inventory.splice(iIdx, 1);
      sc.drops.push({
        id: genId('drp'),
        name: it.nom,
        dlph: it.dlph || '',
        item: it,
        q: tok.q,
        r: tok.r,
        notes: it.etatObjet || ''
      });
      return { ok: true, changed: true };
    }

    if (act === 'pickup_drop') {
      const dIdx = (sc.drops || []).findIndex((d) => d.id === p.dropId);
      if (dIdx === -1) return { ok: false, code: 'not_found', error: 'Objet au sol introuvable.' };
      const drp = sc.drops[dIdx];
      const dist = vttVis.hexDistance(tok.q, tok.r, drp.q, drp.r);
      if (!isGM && dist > 1) return { ok: false, code: 'too_far', error: 'Trop loin pour ramasser (distance ≤ 1).' };
      sc.drops.splice(dIdx, 1);
      if (!Array.isArray(tok.inventory)) tok.inventory = [];
      tok.inventory.push(sanitizeInventoryItem(drp.item || { nom: drp.name, dlph: drp.dlph }));
      if (state.combat && state.combat.active && p.spendAp !== false) {
        tok.pa = Math.max(0, (tok.pa || 0) - 1);
      }
      return { ok: true, changed: true };
    }

    if (act === 'tick_xp') {
      tok.xpTicks = Math.max(0, Math.trunc(Number(tok.xpTicks) || 0)) + 1;
      return { ok: true, changed: true };
    }

    if (act === 'evacuate_vehicle') {
      const passIds = (tok.vehicle && Array.isArray(tok.vehicle.passengers)) ? tok.vehicle.passengers : [];
      const targets = [tok, ...sc.tokens.filter((t) => passIds.includes(t.id))];
      for (const tg of targets) {
        if (!Array.isArray(tg.statuses)) tg.statuses = [];
        if (!tg.statuses.includes('Déstabilisé')) tg.statuses.push('Déstabilisé');
        if (!tg.statuses.includes('À Terre')) tg.statuses.push('À Terre');
      }
      return { ok: true, changed: true };
    }

    if (act === 'update') {
      const patch = (p.patch && typeof p.patch === 'object') ? { ...p.patch } : { ...p };
      if (!isGM) {
        delete patch.gmOnly;
        delete patch.ownerUserId;
        delete patch.faction;
      }
      sc.tokens[idx] = sanitizeTokenInput(patch, tok);
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action pion inconnue.' };
  }

  if (op === 'vtt_reaction') {
    const tid = cleanStr(p.tokenId || '', 48);
    const tok = sc.tokens.find((t) => t.id === tid);
    if (!tok) return { ok: false, code: 'not_found', error: 'Pion introuvable.' };
    if (!isGM && tok.ownerUserId !== userId) return { ok: false, code: 'forbidden', error: 'Ce pion ne vous appartient pas.' };
    const useFree = !!p.useFreeReaction;
    const paCost = Math.max(0, Math.trunc(num(p.paCost, 1, 0, 10)));
    if (useFree) {
      if (tok.freeReactionUsed) return { ok: false, code: 'bad_request', error: 'Réaction Libre déjà consommée ce cycle.' };
      tok.freeReactionUsed = true;
    } else {
      if ((tok.pa || 0) < paCost) return { ok: false, code: 'bad_request', error: 'PA insuffisants (' + tok.pa + '/' + paCost + ').' };
      tok.pa = Math.max(0, (tok.pa || 0) - paCost);
    }
    return { ok: true, changed: true };
  }

  if (op === 'vtt_duel') {
    const act = p.action || 'create';
    if (act === 'create') {
      const atkTok = sc.tokens.find((t) => t.id === p.attackerTokenId);
      const defTok = sc.tokens.find((t) => t.id === p.defenderTokenId);
      if (!atkTok || !defTok) return { ok: false, code: 'not_found', error: 'Combattant introuvable.' };
      if (!isGM && atkTok.ownerUserId !== userId) {
        return { ok: false, code: 'forbidden', error: 'Vous devez contrôler l’attaquant ou être MJ.' };
      }
      const duelId = cleanStr(p.duelId || genId('duel'), 48);
      state.duels[duelId] = {
        id: duelId,
        sceneId: sc.id,
        attackerTokenId: atkTok.id,
        defenderTokenId: defTok.id,
        attackerName: atkTok.name,
        defenderName: defTok.name,
        attackerOwnerId: atkTok.ownerUserId || null,
        defenderOwnerId: defTok.ownerUserId || null,
        weaponName: cleanStr(p.weaponName || 'Attaque', 48),
        weaponDmg: Math.trunc(num(p.weaponDmg, 2, 0, 100)),
        piercing: Math.trunc(num(p.piercing, 0, 0, 30)),
        targetZone: vttRules.BODY_ZONES.some((z) => z.id === p.targetZone) ? p.targetZone : 'torso',
        aimLevel: p.aimLevel === 2 ? 2 : 1,
        atkDie: cleanStr(p.atkDie || 'd6', 16),
        defDie: cleanStr(p.defDie || 'd6', 16),
        atkTrainings: Math.trunc(num(p.atkTrainings, 0, 0, 6)),
        defTrainings: Math.trunc(num(p.defTrainings, 0, 0, 6)),
        attackerBet: null,
        defenderBet: null,
        revealed: false,
        result: null,
        createdAt: Date.now()
      };
      return { ok: true, changed: true };
    }
    const duel = state.duels[p.duelId];
    if (!duel) return { ok: false, code: 'not_found', error: 'Duel introuvable.' };

    if (act === 'commit') {
      const side = p.side === 'defender' ? 'defender' : 'attacker';
      const ownerId = side === 'attacker' ? duel.attackerOwnerId : duel.defenderOwnerId;
      if (!isGM && ownerId !== userId) {
        return { ok: false, code: 'forbidden', error: 'Vous ne contrôlez pas ce camp.' };
      }
      const tr = Math.trunc(num(p.trainings, side === 'attacker' ? duel.atkTrainings : duel.defTrainings, 0, 6));
      const maxEff = 1 + tr;
      let ePa = Math.trunc(num(p.effortPa, 0, 0, maxEff));
      let ePe = Math.trunc(num(p.effortPe, 0, 0, maxEff));
      if (ePa + ePe > maxEff) {
        ePe = Math.max(0, maxEff - ePa);
      }
      const bet = {
        effortPa: ePa,
        effortPe: ePe,
        malus: Math.trunc(num(p.malus, 0, 0, 30)),
        passive: !!p.passive,
        noPa: !!p.noPa,
        die: cleanStr(p.die || (side === 'attacker' ? duel.atkDie : duel.defDie), 16),
        trainings: tr
      };
      if (side === 'attacker') duel.attackerBet = bet;
      else duel.defenderBet = bet;

      if (duel.attackerBet && duel.defenderBet && p.autoReveal !== false) {
        resolveDuelInState(state, duel);
      }
      return { ok: true, changed: true };
    }

    if (act === 'reveal') {
      if (!isGM && !(duel.attackerBet && duel.defenderBet)) {
        return { ok: false, code: 'forbidden', error: 'Attente des deux mises ou révélation MJ.' };
      }
      resolveDuelInState(state, duel);
      return { ok: true, changed: true };
    }

    if (act === 'close') {
      delete state.duels[p.duelId];
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action duel inconnue.' };
  }

  if (op === 'vtt_turn') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Contrôle des tours réservé au MJ.' };
    const act = p.action || '';
    if (act === 'start_combat') {
      state.combat.active = true;
      state.combat.round = Math.max(1, Math.trunc(num(p.round, 1, 1, 999)));
      if (Array.isArray(p.order)) state.combat.order = p.order;
      if (!state.combat.order.length) {
        state.combat.order = sc.tokens.filter((t) => !t.gmOnly).map((t) => ({ tokenId: t.id, name: t.name, init: 6, faction: t.faction }));
      }
      state.combat.turnIndex = 0;
      state.combat.activeTokenId = state.combat.order[0] ? state.combat.order[0].tokenId : null;
      return { ok: true, changed: true };
    }
    if (act === 'ambush_round') {
      const ambFaction = p.ambusherFaction || 'pj';
      const targetIds = Array.isArray(p.targetIds) ? new Set(p.targetIds) : null;
      for (const t of sc.tokens) {
        if (t.gmOnly) continue;
        if (targetIds ? !targetIds.has(t.id) : (t.faction === ambFaction || t.vigilant)) continue;
        if (!Array.isArray(t.statuses)) t.statuses = [];
        if (!t.statuses.includes('Surpris')) t.statuses.push('Surpris');
      }
      if (!state.combat.active) {
        state.combat.active = true;
        state.combat.round = 1;
        state.combat.order = sc.tokens.filter((t) => !t.gmOnly).map((t) => ({
          tokenId: t.id,
          name: t.name,
          init: t.faction === ambFaction ? 10 : 1,
          faction: t.faction
        })).sort((a, b) => b.init - a.init);
        state.combat.turnIndex = 0;
        state.combat.activeTokenId = state.combat.order[0] ? state.combat.order[0].tokenId : null;
      }
      return { ok: true, changed: true };
    }
    if (act === 'end_combat') {
      state.combat.active = false;
      state.combat.activeTokenId = null;
      return { ok: true, changed: true };
    }
    if (act === 'set_order') {
      if (!Array.isArray(p.order)) return { ok: false, code: 'bad_request', error: 'order[] requis.' };
      state.combat.order = p.order.slice(0, 100).map((e) => ({
        tokenId: cleanStr(e.tokenId, 48),
        name: cleanStr(e.name, 48),
        init: num(e.init, 0, -20, 99),
        die: cleanStr(e.die || 'd6', 16),
        faction: cleanStr(e.faction || 'pj', 16)
      })).filter((e) => e.tokenId);
      state.combat.turnIndex = Math.min(state.combat.turnIndex, Math.max(0, state.combat.order.length - 1));
      state.combat.activeTokenId = state.combat.order[state.combat.turnIndex] ? state.combat.order[state.combat.turnIndex].tokenId : null;
      return { ok: true, changed: true };
    }
    if (act === 'next_turn') {
      advanceTurn(state, 1);
      return { ok: true, changed: true };
    }
    if (act === 'prev_turn') {
      advanceTurn(state, -1);
      return { ok: true, changed: true };
    }
    if (act === 'set_active') {
      const i = (state.combat.order || []).findIndex((o) => o.tokenId === p.tokenId);
      if (i >= 0) {
        state.combat.turnIndex = i;
        state.combat.activeTokenId = state.combat.order[i].tokenId;
      }
      return { ok: true, changed: true };
    }
    if (act === 'reset_pa') {
      for (const tok of sc.tokens) {
        tok.pa = tok.maxPa;
        tok.freeReactionUsed = false;
      }
      return { ok: true, changed: true };
    }
    if (act === 'toggle_pause') {
      state.paused = p.paused !== undefined ? !!p.paused : !state.paused;
      return { ok: true, changed: true };
    }
    if (act === 'toggle_turn_lock') {
      state.turnLock = p.turnLock !== undefined ? !!p.turnLock : !state.turnLock;
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action tour inconnue.' };
  }

  if (op === 'vtt_zone') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Zones réservées au MJ.' };
    const act = p.action || 'add';
    if (act === 'add') {
      if (sc.zones.length >= 120) return { ok: false, code: 'limit', error: 'Trop de zones.' };
      const kind = VALID_ZONE_KINDS.has(p.kind) ? p.kind : 'smoke';
      sc.zones.push({
        id: cleanStr(p.id || genId('zn'), 48),
        kind,
        label: cleanStr(p.label || kind, 40),
        q: Math.trunc(num(p.q, 0, -100, 300)),
        r: Math.trunc(num(p.r, 0, -100, 300)),
        radius: Math.trunc(num(p.radius, 1, 0, 12)),
        turnsLeft: p.turnsLeft === null || p.turnsLeft === undefined ? null : Math.trunc(num(p.turnsLeft, 3, 1, 99)),
        gmOnly: !!p.gmOnly
      });
      return { ok: true, changed: true };
    }
    if (act === 'delete') {
      sc.zones = sc.zones.filter((z) => z.id !== p.zoneId);
      return { ok: true, changed: true };
    }
    if (act === 'clear') {
      sc.zones = [];
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action zone inconnue.' };
  }

  if (op === 'vtt_drop') {
    const act = p.action || 'add';
    if (act === 'add') {
      if (!isGM) return { ok: false, code: 'forbidden', error: 'Dépôt direct réservé au MJ.' };
      sc.drops.push({
        id: cleanStr(p.id || genId('drp'), 48),
        name: cleanStr(p.name || 'Objet', 60),
        dlph: cleanStr(p.dlph || '', 24),
        item: p.item ? sanitizeInventoryItem(p.item) : null,
        q: Math.trunc(num(p.q, 0, -100, 300)),
        r: Math.trunc(num(p.r, 0, -100, 300)),
        notes: cleanStr(p.notes || '', 200)
      });
      return { ok: true, changed: true };
    }
    if (act === 'pickup' || act === 'delete') {
      if (!isGM && act === 'delete') return { ok: false, code: 'forbidden', error: 'Réservé au MJ.' };
      sc.drops = sc.drops.filter((d) => d.id !== p.dropId);
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action drop inconnue.' };
  }

  if (op === 'vtt_draw') {
    const act = p.action || 'add';
    if (act === 'clear') {
      if (!isGM) return { ok: false, code: 'forbidden', error: 'Effacer tout réservé au MJ.' };
      sc.drawings = [];
      return { ok: true, changed: true };
    }
    if (act === 'delete') {
      const target = sc.drawings.find((d) => d.id === p.drawId);
      if (!target) return { ok: true, changed: false };
      if (!isGM && target.authorId !== userId) return { ok: false, code: 'forbidden', error: 'Ce tracé ne vous appartient pas.' };
      sc.drawings = sc.drawings.filter((d) => d.id !== p.drawId);
      return { ok: true, changed: true };
    }
    if (act === 'add') {
      if (sc.drawings.length >= 300) sc.drawings.shift();
      const pts = Array.isArray(p.points) ? p.points.slice(0, 400).map((pt) => ({ x: num(pt.x, 0, -50000, 50000), y: num(pt.y, 0, -50000, 50000) })) : [];
      sc.drawings.push({
        id: cleanStr(p.id || genId('drw'), 48),
        kind: ['free', 'line', 'circle', 'rect', 'text'].includes(p.kind) ? p.kind : 'free',
        color: /^#[0-9a-fA-F]{3,8}$/.test(String(p.color || '')) ? p.color : '#f59e0b',
        width: num(p.width, 3, 1, 24),
        points: pts,
        text: p.text ? cleanStr(p.text, 120) : '',
        gmOnly: isGM && !!p.gmOnly,
        authorId: userId
      });
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action dessin inconnue.' };
  }

  if (op === 'vtt_wall') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Murs et couverts réservés au MJ.' };
    const act = p.action || 'add';
    if (act === 'clear') {
      sc.walls = [];
      return { ok: true, changed: true };
    }
    if (act === 'delete') {
      sc.walls = sc.walls.filter((w) => w.id !== p.wallId);
      return { ok: true, changed: true };
    }
    if (act === 'toggle_door') {
      const w = sc.walls.find((x) => x.id === p.wallId && x.kind === 'door');
      if (!w) return { ok: false, code: 'not_found', error: 'Porte introuvable.' };
      if (p.locked !== undefined) w.locked = !!p.locked;
      else if (!w.locked) w.open = !w.open;
      return { ok: true, changed: true };
    }
    if (act === 'toggle_lock') {
      const w = sc.walls.find((x) => x.id === p.wallId && x.kind === 'door');
      if (!w) return { ok: false, code: 'not_found', error: 'Porte introuvable.' };
      w.locked = !w.locked;
      if (w.locked) w.open = false;
      return { ok: true, changed: true };
    }
    if (act === 'add') {
      const kind = VALID_WALL_KINDS.has(p.kind) ? p.kind : 'wall';
      const q1 = Math.trunc(num(p.q, 0, -100, 300));
      const r1 = Math.trunc(num(p.r, 0, -100, 300));
      let cells = [];
      if (Array.isArray(p.cells) && p.cells.length > 0) {
        cells = p.cells.slice(0, 120).map((c) => ({ q: Math.trunc(num(c.q, 0, -100, 300)), r: Math.trunc(num(c.r, 0, -100, 300)) }));
      } else if (Number.isFinite(Number(p.q2)) && Number.isFinite(Number(p.r2))) {
        const q2 = Math.trunc(Number(p.q2));
        const r2 = Math.trunc(Number(p.r2));
        cells = [{ q: q1, r: r1 }, ...vttVis.hexesAlongSegment(q1, r1, q2, r2)];
        if (q2 !== q1 || r2 !== r1) cells.push({ q: q2, r: r2 });
      } else {
        cells = [{ q: q1, r: r1 }];
      }
      sc.walls.push({
        id: cleanStr(p.id || genId('wl'), 48),
        kind,
        q: q1,
        r: r1,
        cells,
        x1: num(p.x1, 0, -50000, 50000),
        y1: num(p.y1, 0, -50000, 50000),
        x2: num(p.x2, 0, -50000, 50000),
        y2: num(p.y2, 0, -50000, 50000),
        open: !!p.open,
        locked: !!p.locked,
        gmOnly: !!p.gmOnly
      });
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action mur inconnue.' };
  }

  if (op === 'vtt_handout') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Handouts réservés au MJ.' };
    const act = p.action || 'create';
    if (act === 'create') {
      if (state.handouts.length >= 60) state.handouts.shift();
      const ho = {
        id: cleanStr(p.id || genId('ho'), 48),
        title: cleanStr(p.title || 'Indice', 80),
        text: cleanStr(p.text || '', 2000),
        imageUrl: typeof p.imageUrl === 'string' ? p.imageUrl.slice(0, 2048) : '',
        targetUserIds: Array.isArray(p.targetUserIds) ? p.targetUserIds.slice(0, 24).map((x) => cleanStr(x, 64)) : [],
        createdAt: Date.now()
      };
      state.handouts.push(ho);
      return { ok: true, changed: true };
    }
    if (act === 'show') {
      const ho = state.handouts.find((x) => x.id === p.handoutId);
      if (!ho) return { ok: false, code: 'not_found', error: 'Handout introuvable.' };
      return {
        ok: true,
        changed: false,
        ephemeral: true,
        targetUserIds: (ho.targetUserIds && ho.targetUserIds.length) ? ho.targetUserIds : null,
        delta: { op: 'vtt_handout_show', handout: ho }
      };
    }
    if (act === 'delete') {
      state.handouts = state.handouts.filter((x) => x.id !== p.handoutId);
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action handout inconnue.' };
  }

  if (op === 'vtt_challenge') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Défis réservés au MJ.' };
    if (!Array.isArray(state.challenges)) state.challenges = [];
    const act = p.action || 'create';
    if (act === 'create') {
      const ch = {
        id: cleanStr(p.id || genId('chg'), 48),
        title: cleanStr(p.title || 'Défi Hors-Combat', 80),
        totalSteps: Math.trunc(num(p.totalSteps, 4, 3, 7)),
        maxRetries: Math.trunc(num(p.maxRetries, 2, 0, 3)),
        stepsOk: 0,
        stepsFail: 0,
        retriesUsed: 0,
        status: 'active', // active | success | failed
        rewardCases: 0,
        skills: Array.isArray(p.skills) ? p.skills.slice(0, 10).map((s) => cleanStr(s, 40)) : []
      };
      state.challenges.push(ch);
      return { ok: true, changed: true };
    }
    const ch = state.challenges.find((x) => x.id === p.challengeId);
    if (!ch) return { ok: false, code: 'not_found', error: 'Défi introuvable.' };
    if (act === 'step') {
      if (ch.status !== 'active') return { ok: false, code: 'bad_request', error: 'Défi déjà terminé.' };
      if (p.success) ch.stepsOk += 1;
      else ch.stepsFail += 1;
      if (p.retry) ch.retriesUsed = Math.min(ch.maxRetries, ch.retriesUsed + 1);
      if (p.skill) {
        const sk = cleanStr(p.skill, 40);
        if (sk && !ch.skills.includes(sk)) ch.skills.push(sk);
      }
      return { ok: true, changed: true };
    }
    if (act === 'finish') {
      const ok = !!p.success;
      ch.status = ok ? 'success' : 'failed';
      ch.rewardCases = ok ? Math.max(2, (ch.stepsOk * 2) - ch.retriesUsed) : 0;
      return { ok: true, changed: true };
    }
    if (act === 'delete') {
      state.challenges = state.challenges.filter((x) => x.id !== p.challengeId);
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action défi inconnue.' };
  }

  if (op === 'vtt_clock') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Horloge et repos réservés au MJ.' };
    const act = p.action || 'advance';
    if (act === 'advance') {
      const mins = Math.trunc(num(p.minutes, 60, -1440, 14400));
      let totalM = (state.clock.day - 1) * 1440 + state.clock.hour * 60 + state.clock.minute + mins;
      if (totalM < 0) totalM = 0;
      state.clock.day = Math.floor(totalM / 1440) + 1;
      const rem = totalM % 1440;
      state.clock.hour = Math.floor(rem / 60);
      state.clock.minute = rem % 60;
      return { ok: true, changed: true };
    }
    if (act === 'set_env') {
      if (p.weather) state.clock.weather = cleanStr(p.weather, 12);
      if (p.lighting) state.clock.lighting = cleanStr(p.lighting, 20);
      return { ok: true, changed: true };
    }
    if (act === 'safe_rest') {
      const hours = Math.trunc(num(p.hours, 8, 1, 24));
      let totalM = (state.clock.day - 1) * 1440 + state.clock.hour * 60 + state.clock.minute + hours * 60;
      state.clock.day = Math.floor(totalM / 1440) + 1;
      state.clock.hour = Math.floor((totalM % 1440) / 60);
      state.clock.minute = totalM % 60;
      const heal = hours >= 10 ? 4 : (hours >= 8 ? 2 : 1);
      for (const tok of sc.tokens) {
        if (tok.faction !== 'pj' && tok.faction !== 'ally') continue;
        tok.pa = tok.maxPa;
        tok.pe = 0;
        tok.freeReactionUsed = false;
        tok.statuses = (tok.statuses || []).filter((s) => s !== 'Somnolent' && s !== 'Souffrant');
        if (tok.zones) {
          for (const k of Object.keys(tok.zones)) {
            if (tok.zones[k] > 0) tok.zones[k] = Math.max(0, tok.zones[k] - heal);
          }
        }
      }
      return { ok: true, changed: true };
    }
    if (act === 'pathology_add') {
      if (!Array.isArray(state.pathologies)) state.pathologies = [];
      state.pathologies.push({
        id: cleanStr(p.id || genId('pat'), 48),
        targetName: cleanStr(p.targetName || 'Patient', 48),
        name: cleanStr(p.name || 'Infection', 60),
        severity: cleanStr(p.severity || 'II', 8),
        stage: Math.trunc(num(p.stage, 1, 1, 6)),
        virulence: Math.trunc(num(p.virulence, 4, 1, 24))
      });
      return { ok: true, changed: true };
    }
    if (act === 'pathology_stage') {
      const pat = (state.pathologies || []).find((x) => x.id === p.pathoId);
      if (!pat) return { ok: false, code: 'not_found', error: 'Pathologie introuvable.' };
      pat.stage = Math.trunc(num(pat.stage + (p.delta || 1), pat.stage, 0, 6));
      if (pat.stage === 0) state.pathologies = state.pathologies.filter((x) => x.id !== p.pathoId);
      return { ok: true, changed: true };
    }
    if (act === 'pathology_remove') {
      state.pathologies = (state.pathologies || []).filter((x) => x.id !== p.pathoId);
      return { ok: true, changed: true };
    }
    return { ok: false, code: 'bad_request', error: 'Action horloge inconnue.' };
  }

  if (op === 'vtt_audio') {
    if (!isGM) return { ok: false, code: 'forbidden', error: 'Audio d’ambiance réservé au MJ.' };
    const act = p.action || 'set_ambience';
    if (act === 'set_ambience') {
      state.audio = {
        ambience: cleanStr(p.ambience || 'none', 24),
        playing: p.playing !== undefined ? !!p.playing : (p.ambience && p.ambience !== 'none')
      };
      return { ok: true, changed: true };
    }
    if (act === 'sfx') {
      return {
        ok: true,
        changed: false,
        ephemeral: true,
        delta: { op: 'vtt_sfx', sfx: cleanStr(p.sfx || 'dice', 24) }
      };
    }
    return { ok: false, code: 'bad_request', error: 'Action audio inconnue.' };
  }

  return { ok: false, code: 'unsupported_op', error: 'Op VTT inconnue : ' + op };
}

module.exports = {
  MAX_VTT_FILE_BYTES,
  vttFilePath,
  createDefaultScene,
  createDefaultVttState,
  loadVttState,
  saveVttState,
  getActiveScene,
  buildCoversMap,
  getObserversForViewer,
  filterStateForViewer,
  applyVttMutation,
  resolveDuelInState
};
