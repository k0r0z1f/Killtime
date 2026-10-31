/* ==========================================================================
   TABLES VOIX LIVE — règles serveur (zéro dépendance, testable)
   users/tables_voice.js — testable via `node --test`.

   Inspiré du système voix KT (KilltimeTactics VTTVoiceManager) :
   - VAD / PTT / continu : décision d'émission côté client (piste WebRTC
     activée ou non), le serveur ne fait que de la signalisation.
   - Muet (mute) : un joueur muté pour la voix NE REÇOIT PLUS le vocal
     (ni signalisation, ni média : les pairs ferment / ne créent pas de
     PeerConnection vers lui) et n'émet plus. Seul un reset pour tout le
     monde (MJ/helper) rouvre : `voice-reset` vide la liste des mutés.
   - Transport : signalisation REST + SSE (pas de WS dédié), média P2P
     WebRTC (le serveur ne voit jamais l'audio).

   Règle déterministe anti-glare : pour toute paire (A, B), c'est le plus
   petit userId (ordre lexicographique) qui initie l'offre.
   ========================================================================== */
'use strict';

// Types de signalisation autorisés (SDP + ICE uniquement).
const SIGNAL_KINDS = new Set(['offer', 'answer', 'ice']);

// Plafonds de taille du champ `data` (caractères) par type.
const SIGNAL_LIMITS = {
  offer: 16384,
  answer: 16384,
  ice: 4096,
};
const DEFAULT_SIGNAL_LIMIT = 4096;

function isSignalKind(kind) {
  return SIGNAL_KINDS.has(String(kind || ''));
}

function signalLimit(kind) {
  const lim = SIGNAL_LIMITS[String(kind || '')];
  return Number.isFinite(lim) ? lim : DEFAULT_SIGNAL_LIMIT;
}

// Valide un payload de signalisation. Retourne la chaîne assainie ou null.
function sanitizeSignal(kind, data) {
  if (!isSignalKind(kind)) return null;
  if (typeof data !== 'string') return null;
  const s = data.replace(/[\u0000-\u001F\u007F]/g, '').trim();
  if (!s) return null;
  if (s.length > signalLimit(kind)) return null;
  return s;
}

// Rôles autorisés à muter / reset la voix des autres.
function isVoiceManagerRole(role) {
  return role === 'mj' || role === 'helper' || role === 'admin';
}

// L'initiateur de l'offre WebRTC entre deux pairs (anti-glare).
function isOfferer(selfId, otherId) {
  return String(selfId || '') < String(otherId || '');
}

/* Règle centrale : un joueur muté pour la voix ne reçoit plus le vocal.
   - voiceMutedIds : ids mutés côté voix (serveur, table-wide).
   - senderExceptIds : exclus d'envoi de l'expéditeur (facultatif).
   L'auteur n'est jamais bloqué vers lui-même (pas d'écho de toute façon). */
function canReceiveVoice({ voiceMutedIds, senderExceptIds, recipientId, senderId }) {
  const rid = String(recipientId || '');
  if (!rid) return false;
  if (rid === String(senderId || '')) return true;
  const muted = Array.isArray(voiceMutedIds) ? voiceMutedIds : [];
  if (muted.includes(rid)) return false;
  const except = Array.isArray(senderExceptIds) ? senderExceptIds : [];
  if (except.includes(rid)) return false;
  return true;
}

// Filtre une liste de destinataires candidats selon la règle ci-dessus.
function filterVoiceRecipients({ voiceMutedIds, senderExceptIds, senderId, candidateIds }) {
  const out = [];
  const list = Array.isArray(candidateIds) ? candidateIds : [];
  for (const id of list) {
    const rid = String(id || '');
    if (!rid || rid === String(senderId || '')) continue;
    if (canReceiveVoice({ voiceMutedIds, senderExceptIds, recipientId: rid, senderId })) {
      out.push(rid);
    }
  }
  return out;
}

module.exports = {
  SIGNAL_KINDS,
  SIGNAL_LIMITS,
  DEFAULT_SIGNAL_LIMIT,
  isSignalKind,
  signalLimit,
  sanitizeSignal,
  isVoiceManagerRole,
  isOfferer,
  canReceiveVoice,
  filterVoiceRecipients,
};
