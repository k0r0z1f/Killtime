'use strict';
const { describe, it } = require('node:test');
const assert = require('node:assert/strict');
const v = require('../tables_voice.js');

describe('tables_voice — signalisation', () => {
  it('kinds offer/answer/ice acceptés, le reste rejeté', () => {
    for (const k of ['offer', 'answer', 'ice']) assert.equal(v.isSignalKind(k), true);
    for (const k of ['', 'join', 'leave', 'mute', 'reset', 'video', null]) {
      assert.equal(v.isSignalKind(k), false);
    }
  });

  it('sanitizeSignal refuse vide / trop gros / mauvais type', () => {
    assert.equal(v.sanitizeSignal('offer', ''), null);
    assert.equal(v.sanitizeSignal('offer', null), null);
    assert.equal(v.sanitizeSignal('bogus', 'x'), null);
    assert.equal(v.sanitizeSignal('ice', 'x'.repeat(5000)), null);
    assert.equal(v.sanitizeSignal('offer', '  abc  '), 'abc');
  });

  it('limites SDP suffisantes pour une offre complète', () => {
    assert.ok(v.signalLimit('offer') >= 8000);
    assert.ok(v.signalLimit('ice') >= 1024);
  });
});

describe('tables_voice — mute vocal', () => {
  it('un joueur muté ne reçoit plus le vocal', () => {
    assert.equal(v.canReceiveVoice({
      voiceMutedIds: ['u2'], senderExceptIds: [], recipientId: 'u2', senderId: 'u1',
    }), false);
  });

  it('non muté reçoit', () => {
    assert.equal(v.canReceiveVoice({
      voiceMutedIds: [], senderExceptIds: [], recipientId: 'u2', senderId: 'u1',
    }), true);
  });

  it('reset (liste vide) rouvre pour tout le monde', () => {
    assert.equal(v.canReceiveVoice({
      voiceMutedIds: [], senderExceptIds: [], recipientId: 'u2', senderId: 'u1',
    }), true);
    assert.equal(v.canReceiveVoice({
      voiceMutedIds: [], senderExceptIds: [], recipientId: 'u3', senderId: 'u1',
    }), true);
  });

  it('exclus d’envoi non livrés', () => {
    assert.equal(v.canReceiveVoice({
      voiceMutedIds: [], senderExceptIds: ['u2'], recipientId: 'u2', senderId: 'u1',
    }), false);
  });

  it('filterVoiceRecipients exclut mutés + auteur', () => {
    assert.deepEqual(v.filterVoiceRecipients({
      voiceMutedIds: ['u3'], senderExceptIds: [], senderId: 'u1',
      candidateIds: ['u1', 'u2', 'u3', 'u4'],
    }), ['u2', 'u4']);
  });

  it('destinataire vide jamais livré', () => {
    assert.equal(v.canReceiveVoice({
      voiceMutedIds: [], senderExceptIds: [], recipientId: '', senderId: 'u1',
    }), false);
  });
});

describe('tables_voice — rôles & offreur', () => {
  it('mj/helper/admin gèrent la voix, joueur non', () => {
    assert.equal(v.isVoiceManagerRole('mj'), true);
    assert.equal(v.isVoiceManagerRole('helper'), true);
    assert.equal(v.isVoiceManagerRole('admin'), true);
    assert.equal(v.isVoiceManagerRole('joueur'), false);
    assert.equal(v.isVoiceManagerRole(null), false);
  });

  it('offreur déterministe = plus petit userId (anti-glare)', () => {
    assert.equal(v.isOfferer('a', 'b'), true);
    assert.equal(v.isOfferer('b', 'a'), false);
    assert.equal(v.isOfferer('a', 'a'), false);
  });
});
