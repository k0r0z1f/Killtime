/* RD-066 — autorité GM + validation des ops du hub VTT.
   Exécution : node --test users/tests/vtt_hub_policy.test.js */
'use strict';

const { describe, it } = require('node:test');
const assert = require('node:assert/strict');
const policy = require('../vtt_hub_policy.js');

describe('isGMOp (parité VTTProtocol.IsGMOp Unity)', () => {
    it('GM-only : turn_control, combat_action, scene_control, room_settings, map_load, worldmap', () => {
        for (const op of ['turn_control', 'combat_action', 'scene_control', 'room_settings', 'map_load', 'worldmap']) {
            assert.equal(policy.isGMOp(op), true, op);
        }
    });
    it('ouverts : unit_move, chat, dice, voice, video, map_request, action_request, unit_claim', () => {
        for (const op of ['unit_move', 'chat', 'dice', 'voice', 'video', 'map_request', 'action_request', 'unit_claim']) {
            assert.equal(policy.isGMOp(op), false, op);
        }
    });
});

describe('shouldRejectOp (autorité GM)', () => {
    const gmRoom = { gmId: 'c_gm' };
    const gmConn = { id: 'c_gm', role: 'gm' };
    const playerConn = { id: 'c_p1', role: 'player' };
    it('un joueur ne peut pas émettre combat_action ni worldmap', () => {
        assert.equal(policy.shouldRejectOp(gmRoom, playerConn, 'combat_action').reject, true);
        assert.equal(policy.shouldRejectOp(gmRoom, playerConn, 'worldmap').reject, true);
        assert.equal(policy.shouldRejectOp(gmRoom, playerConn, 'turn_control').reject, true);
    });
    it('le GM peut tout émettre, les joueurs gardent moves/dés/chat', () => {
        assert.equal(policy.shouldRejectOp(gmRoom, gmConn, 'combat_action').reject, false);
        assert.equal(policy.shouldRejectOp(gmRoom, playerConn, 'unit_move').reject, false);
        assert.equal(policy.shouldRejectOp(gmRoom, playerConn, 'dice').reject, false);
        assert.equal(policy.shouldRejectOp(gmRoom, playerConn, 'chat').reject, false);
        assert.equal(policy.shouldRejectOp(gmRoom, playerConn, 'action_request').reject, false);
    });
    it('op vide ou trop longue rejetée', () => {
        assert.equal(policy.shouldRejectOp(gmRoom, gmConn, '').reject, true);
        assert.equal(policy.shouldRejectOp(gmRoom, gmConn, 'x'.repeat(65)).reject, true);
    });
});

describe('sanitizeChat', () => {
    it('tronque à 500 et strippe les contrôles', () => {
        const out = policy.sanitizeChat('hello\u0000world');
        assert.equal(out.text, 'helloworld');
        assert.equal(policy.sanitizeChat('x'.repeat(600)).text.length, 500);
        assert.equal(policy.sanitizeChat('  hi  ').text, 'hi');
    });
});

describe('validateDice (anti-triche douce)', () => {
    it('roll valide conservé', () => {
        const out = policy.validateDice({ roll: 73, formula: '1D100', detail: '(73)', reason: 'Attaque' });
        assert.equal(out.roll, 73);
        assert.equal(out.formula, '1D100');
    });
    it('roll hors plage clampé sur la formule', () => {
        assert.equal(policy.validateDice({ roll: 999, formula: '1D100' }).roll, 100);
        assert.equal(policy.validateDice({ roll: -5, formula: '2D6+3' }).roll, 5); // min 2+3
        assert.equal(policy.validateDice({ roll: 99, formula: '2D6+3' }).roll, 15); // max 12+3
    });
    it('sans formule : roll tronqué entier borné', () => {
        assert.equal(policy.validateDice({ roll: 3.7 }).roll, 3);
    });
    it('payload invalide rejeté', () => {
        assert.equal(policy.validateDice(null), null);
        assert.equal(policy.validateDice({}), null);
        assert.equal(policy.validateDice({ roll: 'x' }), null);
    });
    it('champs tronqués (detail 300, reason 120, formula 32)', () => {
        const out = policy.validateDice({ roll: 1, formula: 'x'.repeat(50), detail: 'y'.repeat(400), reason: 'z'.repeat(200) });
        assert.equal(out.formula.length, 32);
        assert.equal(out.detail.length, 300);
        assert.equal(out.reason.length, 120);
    });
});

describe('parseDiceFormula', () => {
    it('parse NdF+mod', () => {
        assert.deepEqual(policy.parseDiceFormula('1D100'), { count: 1, faces: 100, mod: 0 });
        assert.deepEqual(policy.parseDiceFormula('2d6+3'), { count: 2, faces: 6, mod: 3 });
        assert.deepEqual(policy.parseDiceFormula('D20'), { count: 1, faces: 20, mod: 0 });
        assert.equal(policy.parseDiceFormula('zzz'), null);
        assert.equal(policy.parseDiceFormula('0D6'), null);
    });
});

describe('opPayloadLimit', () => {
    it('quotas par op (chat 4K, map_load 480K)', () => {
        assert.equal(policy.opPayloadLimit('chat'), 4096);
        assert.equal(policy.opPayloadLimit('map_load'), 480 * 1024);
        assert.equal(policy.opPayloadLimit('unknown_op'), policy.DEFAULT_OP_LIMIT);
    });
});

describe('trackRound + roomDirectoryEntry (sync tour/round)', () => {
    it('mémorise round, unité active et action', () => {
        const room = { members: new Map(), currentRound: 1 };
        policy.trackRound(room, 'turn_control', { action: 'round_start', round: 3, activeUnitId: 'Roger' });
        assert.equal(room.currentRound, 3);
        assert.equal(room.activeUnitId, 'Roger');
        assert.equal(room.lastTurnAction, 'round_start');
    });
    it('annuaire expose round/tour', () => {
        const room = {
            code: 'ABC123', tableName: 'T', buildVersion: '0.3.1',
            members: new Map([['c1', { username: 'GM', role: 'gm' }]]),
            gmId: 'c1', maxPlayers: 6, createdAt: 1, lastActive: 2,
            currentRound: 4, activeUnitId: 'Mina', lastTurnAction: 'turn_start',
        };
        const e = policy.roomDirectoryEntry(room);
        assert.equal(e.currentRound, 4);
        assert.equal(e.activeUnitId, 'Mina');
        assert.equal(e.lastTurnAction, 'turn_start');
        assert.equal(e.code, 'ABC123');
    });
});
