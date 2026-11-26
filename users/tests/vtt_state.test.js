/* Tests unitaires état VTT persisté, permissions & anti-maphack (users/vtt_state.js) */
'use strict';

const { describe, it } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vttState = require('../vtt_state.js');

describe('vtt_state — Persistance atomique & mutations', () => {
  it('sauvegarde et recharge vtt_<tableId>.json avec fichier .bak', () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'vtt-state-'));
    try {
      const st = vttState.loadVttState(dir, 'tbl_alpha');
      assert.equal(st.tableId, 'tbl_alpha');
      assert.equal(st.scenes.length, 1);
      vttState.saveVttState(dir, st);
      assert.equal(fs.existsSync(vttState.vttFilePath(dir, 'tbl_alpha')), true);

      const mut = vttState.applyVttMutation(st, 'vtt_token', {
        action: 'add',
        token: { id: 'tok_1', name: 'Lucas', faction: 'pj', q: 2, r: 2, con: 4, maxPa: 9, pa: 9 }
      }, { userId: 'u_gm', isGM: true });
      assert.equal(mut.ok, true);
      vttState.saveVttState(dir, st);
      assert.equal(fs.existsSync(vttState.vttFilePath(dir, 'tbl_alpha') + '.bak'), true);

      const reloaded = vttState.loadVttState(dir, 'tbl_alpha');
      assert.equal(reloaded.scenes[0].tokens.length, 1);
      assert.equal(reloaded.scenes[0].tokens[0].name, 'Lucas');
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it('applique les droits GM vs Joueur, revendication et verrouillage tour/pause', () => {
    const st = vttState.createDefaultVttState('tbl_perm');
    const gm = { userId: 'u_gm', username: 'MJ', isGM: true };
    const p1 = { userId: 'u_p1', username: 'Alice', isGM: false };
    const p2 = { userId: 'u_p2', username: 'Bob', isGM: false };

    // Joueur ne peut pas créer de scène ni avancer le tour
    assert.equal(vttState.applyVttMutation(st, 'vtt_scene', { action: 'create', name: 'X' }, p1).ok, false);
    assert.equal(vttState.applyVttMutation(st, 'vtt_turn', { action: 'next_turn' }, p1).ok, false);

    // GM ajoute un pion PJ et un pion ennemi
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 't_alice', name: 'Mina', faction: 'pj', q: 1, r: 1 } }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 't_orc', name: 'Bandit', faction: 'enemy', q: 5, r: 5 } }, gm);

    // Joueur 1 revendique t_alice mais pas t_orc
    assert.equal(vttState.applyVttMutation(st, 'vtt_token', { action: 'claim', tokenId: 't_alice' }, p1).ok, true);
    assert.equal(vttState.applyVttMutation(st, 'vtt_token', { action: 'claim', tokenId: 't_orc' }, p1).ok, false);
    // Joueur 2 ne peut pas bouger t_alice
    assert.equal(vttState.applyVttMutation(st, 'vtt_token', { action: 'move', tokenId: 't_alice', q: 2, r: 2 }, p2).ok, false);

    // Pause MJ bloque mouvement joueur
    vttState.applyVttMutation(st, 'vtt_turn', { action: 'toggle_pause', paused: true }, gm);
    assert.equal(vttState.applyVttMutation(st, 'vtt_token', { action: 'move', tokenId: 't_alice', q: 2, r: 2 }, p1).code, 'paused');
    vttState.applyVttMutation(st, 'vtt_turn', { action: 'toggle_pause', paused: false }, gm);

    // Verrou tour actif bloque hors tour
    vttState.applyVttMutation(st, 'vtt_turn', { action: 'toggle_turn_lock', turnLock: true }, gm);
    vttState.applyVttMutation(st, 'vtt_turn', {
      action: 'start_combat',
      order: [{ tokenId: 't_orc', name: 'Bandit', init: 8 }, { tokenId: 't_alice', name: 'Mina', init: 4 }]
    }, gm);
    assert.equal(vttState.applyVttMutation(st, 'vtt_token', { action: 'move', tokenId: 't_alice', q: 2, r: 2 }, p1).code, 'not_your_turn');

    // Tour suivant -> c'est au tour de t_alice
    vttState.applyVttMutation(st, 'vtt_turn', { action: 'next_turn' }, gm);
    assert.equal(vttState.applyVttMutation(st, 'vtt_token', { action: 'move', tokenId: 't_alice', q: 2, r: 2 }, p1).ok, true);
  });

  it('filtre gmOnly, brouillard 360°, stats ennemies et mises duel secrètes côté joueur', () => {
    const st = vttState.createDefaultVttState('tbl_fog');
    const gm = { userId: 'u_gm', isGM: true };
    vttState.applyVttMutation(st, 'vtt_scene', { action: 'update', fogEnabled: true, sightRange: 6 }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 't_pj', name: 'Héros', faction: 'pj', ownerUserId: 'u_p1', q: 0, r: 0 } }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 't_near', name: 'Sbire proche', faction: 'enemy', q: 3, r: 0, con: 3, pa: 6, maxPa: 6 } }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 't_far', name: 'Sniper loin', faction: 'enemy', q: 15, r: 0 } }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 't_sec', name: 'Piège MJ', faction: 'enemy', q: 1, r: 0, gmOnly: true } }, gm);

    st.duels = { d1: { id: 'd1', attackerTokenId: 't_pj', defenderTokenId: 't_near', attackerBet: { effort: 3 }, defenderBet: { effort: 2 }, revealed: false } };

    const gmView = vttState.filterStateForViewer(st, { userId: 'u_gm', isGM: true });
    assert.equal(gmView.scenes[0].tokens.length, 4);

    const pView = vttState.filterStateForViewer(st, { userId: 'u_p1', isGM: false });
    const ids = pView.scenes[0].tokens.map((t) => t.id);
    assert.deepEqual(ids, ['t_pj', 't_near']); // t_far hors portée 6 et t_sec gmOnly masqués
    assert.equal(pView.scenes[0].tokens[1].statsMasked, true);
    assert.equal(pView.scenes[0].tokens[1].pa, undefined);
    assert.equal(pView.duels.d1.defenderBet, undefined);
    assert.equal(pView.duels.d1.defenderReady, true);
  });

  it('gère murs multi-cases, ouverture de porte, brouillard manuel et lumières', () => {
    const st = vttState.createDefaultVttState('tbl_wall');
    const gm = { userId: 'u_gm', isGM: true };
    vttState.applyVttMutation(st, 'vtt_scene', { action: 'update', fogEnabled: true, sightRange: 12 }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 't_pj', name: 'Lucas', faction: 'pj', ownerUserId: 'u_p1', q: 0, r: 0 } }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 't_en', name: 'Garde', faction: 'enemy', q: 4, r: 0 } }, gm);

    // Mur/porte fermée en (2,0) bloque la ligne de vue entre (0,0) et (4,0)
    vttState.applyVttMutation(st, 'vtt_wall', { action: 'add', id: 'door_1', kind: 'door', q: 2, r: 0, q2: 2, r2: 1, open: false }, gm);
    let pView = vttState.filterStateForViewer(st, { userId: 'u_p1', isGM: false });
    assert.equal(pView.scenes[0].tokens.some((t) => t.id === 't_en'), false);

    // Ouverture de la porte -> garde visible
    vttState.applyVttMutation(st, 'vtt_wall', { action: 'toggle_door', wallId: 'door_1' }, gm);
    pView = vttState.filterStateForViewer(st, { userId: 'u_p1', isGM: false });
    assert.equal(pView.scenes[0].tokens.some((t) => t.id === 't_en'), true);

    // Referme porte, puis pinceau MJ révèle (4,0)
    vttState.applyVttMutation(st, 'vtt_wall', { action: 'toggle_door', wallId: 'door_1' }, gm);
    vttState.applyVttMutation(st, 'vtt_fog', { action: 'reveal', cells: [{ q: 4, r: 0 }] }, gm);
    pView = vttState.filterStateForViewer(st, { userId: 'u_p1', isGM: false });
    assert.equal(pView.scenes[0].tokens.some((t) => t.id === 't_en'), true);

    // Lumière
    assert.equal(vttState.applyVttMutation(st, 'vtt_light', { action: 'add', q: 1, r: 1, range: 10 }, gm).ok, true);
    assert.equal(st.scenes[0].lights.length, 1);
  });

  it('résout Face-à-Face masqué, embuscade, réactions & inventaire drop/pickup', () => {
    const st = vttState.createDefaultVttState('tbl_combat');
    const gm = { userId: 'u_gm', isGM: true };
    const p1 = { userId: 'u_p1', isGM: false };
    const p2 = { userId: 'u_p2', isGM: false };

    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 't1', name: 'Lucas', faction: 'pj', ownerUserId: 'u_p1', q: 1, r: 1, pa: 8, maxPa: 8, con: 4 } }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 't2', name: 'Bandit', faction: 'enemy', ownerUserId: 'u_p2', q: 2, r: 1, pa: 6, maxPa: 6, con: 3, armor: 1 } }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 't3', name: 'Vigilant', faction: 'enemy', q: 3, r: 1, vigilant: true } }, gm);

    // Embuscade : t2 Surpris, t3 immunisé car vigilant
    vttState.applyVttMutation(st, 'vtt_turn', { action: 'ambush_round', ambusherFaction: 'pj' }, gm);
    assert.equal(st.scenes[0].tokens[1].statuses.includes('Surpris'), true);
    assert.equal(st.scenes[0].tokens[2].statuses.includes('Surpris'), false);

    // Réaction Libre puis PA
    assert.equal(vttState.applyVttMutation(st, 'vtt_reaction', { tokenId: 't1', kind: 'opportunite', useFreeReaction: true }, p1).ok, true);
    assert.equal(st.scenes[0].tokens[0].freeReactionUsed, true);
    assert.equal(vttState.applyVttMutation(st, 'vtt_reaction', { tokenId: 't1', kind: 'opportunite', useFreeReaction: true }, p1).ok, false);
    assert.equal(vttState.applyVttMutation(st, 'vtt_reaction', { tokenId: 't1', kind: 'guet', paCost: 1 }, p1).ok, true);
    assert.equal(st.scenes[0].tokens[0].pa, 7);

    // Duel masqué
    vttState.applyVttMutation(st, 'vtt_duel', { action: 'create', duelId: 'd_test', attackerTokenId: 't1', defenderTokenId: 't2', weaponDmg: 4, piercing: 1, targetZone: 'head' }, p1);
    vttState.applyVttMutation(st, 'vtt_duel', { action: 'commit', duelId: 'd_test', side: 'attacker', effortPa: 2, trainings: 1, die: 'd8', autoReveal: false }, p1);

    // P2 ne voit pas la mise de P1 avant révélation
    const viewP2 = vttState.filterStateForViewer(st, { userId: 'u_p2', isGM: false });
    assert.equal(viewP2.duels.d_test.attackerReady, true);
    assert.equal(viewP2.duels.d_test.attackerBet, undefined);

    // P2 commit -> auto-révélation
    vttState.applyVttMutation(st, 'vtt_duel', { action: 'commit', duelId: 'd_test', side: 'defender', effortPa: 1, passive: false, die: 'd4' }, p2);
    assert.equal(st.duels.d_test.revealed, true);
    assert.equal(st.scenes[0].tokens[0].pa, 5); // 7 - 2 effort

    // Inventaire add -> drop -> pickup
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add_item', tokenId: 't1', item: { id: 'it_1', nom: 'Pistolet', munitions: 6 } }, p1);
    assert.equal(st.scenes[0].tokens[0].inventory.length, 1);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'drop_item', tokenId: 't1', itemId: 'it_1' }, p1);
    assert.equal(st.scenes[0].tokens[0].inventory.length, 0);
    assert.equal(st.scenes[0].drops.length, 1);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'pickup_drop', tokenId: 't1', dropId: st.scenes[0].drops[0].id }, p1);
    assert.equal(st.scenes[0].tokens[0].inventory.length, 1);
    assert.equal(st.scenes[0].drops.length, 0);
  });

  it('gère hiddenCells, Défis §10, Horloge/Repos sûr/Pathologies, Véhicules, Audio & Scission', () => {
    const st = vttState.createDefaultVttState('tbl_it3');
    const gm = { userId: 'u_gm', isGM: true };

    // hiddenCells prime sur la portée automatique
    vttState.applyVttMutation(st, 'vtt_scene', { action: 'update', fogEnabled: true, sightRange: 12 }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 'tpj', name: 'PJ', faction: 'pj', ownerUserId: 'u_p1', q: 0, r: 0 } }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 'ten', name: 'Ennemi', faction: 'enemy', q: 2, r: 0 } }, gm);
    vttState.applyVttMutation(st, 'vtt_fog', { action: 'hide', cells: [{ q: 2, r: 0 }] }, gm);
    const viewHidden = vttState.filterStateForViewer(st, { userId: 'u_p1', isGM: false });
    assert.equal(viewHidden.scenes[0].tokens.some((t) => t.id === 'ten'), false);

    // Défi Hors-Combat §10
    vttState.applyVttMutation(st, 'vtt_challenge', { action: 'create', id: 'c1', title: 'Piratage', totalSteps: 4, maxRetries: 2 }, gm);
    vttState.applyVttMutation(st, 'vtt_challenge', { action: 'step', challengeId: 'c1', success: true }, gm);
    vttState.applyVttMutation(st, 'vtt_challenge', { action: 'step', challengeId: 'c1', success: true, retry: true }, gm);
    vttState.applyVttMutation(st, 'vtt_challenge', { action: 'finish', challengeId: 'c1', success: true }, gm);
    assert.equal(st.challenges[0].rewardCases, 3); // 2*2 - 1 = 3

    vttState.applyVttMutation(st, 'vtt_challenge', { action: 'create', id: 'c2', title: 'Infiltration' }, gm);
    vttState.applyVttMutation(st, 'vtt_challenge', { action: 'step', challengeId: 'c2', success: true }, gm);
    vttState.applyVttMutation(st, 'vtt_challenge', { action: 'finish', challengeId: 'c2', success: false }, gm);
    assert.equal(st.challenges[1].rewardCases, 0);

    // Horloge, Repos sûr & Pathologies
    vttState.applyVttMutation(st, 'vtt_token', { action: 'update', tokenId: 'tpj', patch: { pe: 2, zones: { torso: 3 }, statuses: ['Somnolent'] } }, gm);
    vttState.applyVttMutation(st, 'vtt_clock', { action: 'safe_rest', hours: 8 }, gm);
    assert.equal(st.clock.hour, 16);
    assert.equal(st.scenes[0].tokens[0].pe, 0);
    assert.equal(st.scenes[0].tokens[0].zones.torso, 1);
    assert.equal(st.scenes[0].tokens[0].statuses.includes('Somnolent'), false);

    vttState.applyVttMutation(st, 'vtt_clock', { action: 'pathology_add', id: 'p1', targetName: 'PJ', name: 'Fièvre', severity: 'II' }, gm);
    assert.equal(st.pathologies.length, 1);
    vttState.applyVttMutation(st, 'vtt_clock', { action: 'pathology_stage', pathoId: 'p1', delta: 1 }, gm);
    assert.equal(st.pathologies[0].stage, 2);

    // Véhicule & évacuation
    vttState.applyVttMutation(st, 'vtt_token', { action: 'add', token: { id: 'veh1', name: 'Pod', faction: 'vehicle', q: 1, r: 1 } }, gm);
    vttState.applyVttMutation(st, 'vtt_token', { action: 'evacuate_vehicle', tokenId: 'veh1' }, gm);
    const veh = st.scenes[0].tokens.find((t) => t.id === 'veh1');
    assert.equal(veh.statuses.includes('Déstabilisé'), true);
    assert.equal(veh.statuses.includes('À Terre'), true);

    // Scission de groupe & Audio
    vttState.applyVttMutation(st, 'vtt_scene', { action: 'create', name: 'Taverne' }, gm);
    const scn2Id = st.scenes[1].id;
    vttState.applyVttMutation(st, 'vtt_scene', { action: 'assign_player', targetUserId: 'u_p1', targetSceneId: scn2Id }, gm);
    const viewSplit = vttState.filterStateForViewer(st, { userId: 'u_p1', isGM: false });
    assert.equal(viewSplit.mySceneId, scn2Id);

    vttState.applyVttMutation(st, 'vtt_audio', { action: 'set_ambience', ambience: 'rain', playing: true }, gm);
    assert.equal(st.audio.ambience, 'rain');
    const sfxRes = vttState.applyVttMutation(st, 'vtt_audio', { action: 'sfx', sfx: 'laser' }, gm);
    assert.equal(sfxRes.ephemeral, true);
    assert.equal(sfxRes.delta.sfx, 'laser');
  });
});
