/* ==========================================================================
   VTT HUB POLICY — autorité GM + validation des ops (zéro dépendance)
   users/vtt_hub_policy.js — testable via `node --test`.

   Parité stricte avec KilltimeTactics/Assets/Scripts/Multi/VTTProtocol.cs :
     IsGMOp(op) <=> turn_control | combat_action | scene_control |
                   room_settings | map_load | worldmap
   Ouverts (joueurs) : unit_move | chat | dice | voice | video |
                       map_request | action_request | unit_claim

   RD-066 : VTT /ws/vtt rooms + autorité GM
   - Moves : unit_move relayé + filtrage anti-maphack (vtt_visibility.js),
     revendication auto de l'acteur (cohérence du brouillard).
   - Dés : jet client relayé mais VALIDÉ (roll entier, formula/detail/reason
     tronqués, cohérence optionnelle via parse de "NdF(+mod)").
   - Annuaire : /api/vtt/rooms expose round/tour suivis côté hub.
   - Sync tour/round : trackRound() mémorise round/action/unité active.
   ========================================================================== */
'use strict';

const GM_OPS = new Set([
  'turn_control',
  'combat_action',
  'scene_control',
  'room_settings',
  'map_load',
  'worldmap',
]);

const OPEN_OPS = new Set([
  'unit_move',
  'chat',
  'dice',
  'voice',
  'video',
  'map_request',
  'action_request',
  'unit_claim',
]);

// Limites de payload JSON sérialisé par op (octets). Au-delà : rejet 413.
const OP_PAYLOAD_LIMITS = {
  chat: 4 * 1024,
  dice: 4 * 1024,
  unit_move: 32 * 1024,
  combat_action: 64 * 1024,
  turn_control: 256 * 1024,
  scene_control: 8 * 1024,
  room_settings: 8 * 1024,
  map_load: 480 * 1024,
  map_request: 1024,
  action_request: 16 * 1024,
  unit_claim: 4 * 1024,
  worldmap: 16 * 1024,
  voice: 512 * 1024,
  video: 512 * 1024,
};
const DEFAULT_OP_LIMIT = 64 * 1024;

function isGMOp(op) {
  return GM_OPS.has(String(op || ''));
}

function opPayloadLimit(op) {
  const lim = OP_PAYLOAD_LIMITS[String(op || '')];
  return Number.isFinite(lim) ? lim : DEFAULT_OP_LIMIT;
}

function isGMConn(room, conn) {
  if (!room || !conn) return false;
  if (conn.role === 'gm') return true;
  return room.gmId != null && room.gmId === conn.id;
}

// Autorité : les ops GM-only émises par un joueur sont rejetées.
function shouldRejectOp(room, conn, op) {
  if (!op || typeof op !== 'string' || op.length > 64) {
    return { reject: true, code: 'bad_request', message: 'Champ "op" manquant ou invalide.' };
  }
  if (isGMOp(op) && !isGMConn(room, conn)) {
    return { reject: true, code: 'forbidden', message: `Op "${op}" réservée au GM.` };
  }
  return { reject: false };
}

function stripControls(s) {
  return String(s == null ? '' : s).replace(/[\u0000-\u001F\u007F]/g, '');
}

function sanitizeChat(text) {
  const clean = stripControls(text).trim().slice(0, 500);
  return { text: clean };
}

// Parse minimal "NdF", "D100", "2D6+3", "1d20-1" -> {count, faces, mod} ou null.
function parseDiceFormula(formula) {
  const m = /^\s*(\d*)\s*[dD]\s*(\d+)\s*([+-]\s*\d+)?\s*$/.exec(String(formula || ''));
  if (!m) return null;
  const count = m[1] === '' ? 1 : parseInt(m[1], 10);
  const faces = parseInt(m[2], 10);
  const mod = m[3] ? parseInt(m[3].replace(/\s+/g, ''), 10) : 0;
  if (!Number.isFinite(count) || !Number.isFinite(faces) || !Number.isFinite(mod)) return null;
  if (count < 1 || count > 100 || faces < 2 || faces > 1000) return null;
  if (Math.abs(mod) > 10000) return null;
  return { count, faces, mod };
}

function diceRange(formula) {
  const p = parseDiceFormula(formula);
  if (!p) return null;
  return { min: p.count * 1 + p.mod, max: p.count * p.faces + p.mod };
}

// Valide un payload de dés. Retourne le payload normalisé ou null si invalide.
// Un roll hors plage de la formule est CLAMPÉ (anti-triche douce) plutôt que
// rejeté : les logs restent publics, la valeur reste plausible.
function validateDice(payload) {
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) return null;
  const roll = Number(payload.roll);
  if (!Number.isFinite(roll)) return null;
  const formula = stripControls(payload.formula).slice(0, 32);
  const detail = stripControls(payload.detail).slice(0, 300);
  const reason = stripControls(payload.reason).slice(0, 120);
  const range = formula ? diceRange(formula) : null;
  let r = Math.trunc(roll);
  if (range) r = Math.max(range.min, Math.min(range.max, r));
  else r = Math.max(-100000, Math.min(100000, r));
  return {
    roll: r,
    formula,
    detail,
    reason,
    isCritical: !!payload.isCritical,
    isFumble: !!payload.isFumble,
  };
}

// Mémorise round/tour depuis les ops turn_control (GM). Utilisé par
// l'annuaire (/api/vtt/rooms) et /api/vtt/status.
function trackRound(room, op, payload) {
  if (!room || op !== 'turn_control' || !payload || typeof payload !== 'object') return;
  const action = String(payload.action || '').slice(0, 32);
  const round = Number(payload.round);
  if (Number.isFinite(round)) room.currentRound = Math.max(1, Math.min(999, Math.trunc(round)));
  if (typeof payload.activeUnitId === 'string' && payload.activeUnitId) {
    room.activeUnitId = payload.activeUnitId.slice(0, 64);
  }
  if (action) room.lastTurnAction = action;
  room.lastActive = Date.now();
}

function roomDirectoryEntry(room) {
  const gm = room.members.get(room.gmId);
  return {
    code: room.code,
    tableName: room.tableName || room.code,
    buildVersion: room.buildVersion || 'unknown',
    gmName: gm ? gm.username : '?',
    players: room.members.size,
    maxPlayers: room.maxPlayers || 6,
    createdAt: room.createdAt,
    lastActive: room.lastActive || room.createdAt,
    currentRound: room.currentRound || 1,
    activeUnitId: room.activeUnitId || null,
    lastTurnAction: room.lastTurnAction || null,
  };
}

module.exports = {
  GM_OPS,
  OPEN_OPS,
  OP_PAYLOAD_LIMITS,
  DEFAULT_OP_LIMIT,
  isGMOp,
  opPayloadLimit,
  isGMConn,
  shouldRejectOp,
  sanitizeChat,
  parseDiceFormula,
  diceRange,
  validateDice,
  trackRound,
  roomDirectoryEntry,
};
