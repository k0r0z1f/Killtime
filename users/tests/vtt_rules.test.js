/* Tests unitaires du moteur de règles partagé SysRP (js/vtt-rules.js) */
'use strict';

const { describe, it } = require('node:test');
const assert = require('node:assert/strict');
const rules = require('../../js/vtt-rules.js');

describe('vtt-rules — Échelle des dés & stats dérivées', () => {
  it('convertit rang et entraînements sur l’échelle d2..2d12+10', () => {
    assert.equal(rules.skillDie(2, 0), 'd2');
    assert.equal(rules.skillDie(4, 0), 'd4');
    assert.equal(rules.skillDie(5, 0), 'd6');
    assert.equal(rules.skillDie(9, 0), 'd10');
    assert.equal(rules.skillDie(9, 6), '2d12+10');
    assert.equal(rules.skillDie(10, 6), '2d12+10');
  });

  it('calcule PA, Encaissement CON×2, Létal CON×5, PE et Initiative', () => {
    const st = rules.computeDerivedStats({ for: 5, agi: 4, con: 4, rap: 3, int: 2, cha: 2, ins: 3, mag: 0 });
    // PA = Max(4, 2) + 3 + Min(5,4,4,3,2,2,3) = 4 + 3 + 2 = 9
    assert.equal(st.maxPa, 9);
    assert.equal(st.cap, 8);
    assert.equal(st.lethal, 20);
    assert.equal(st.maxPe, 4);
    assert.equal(st.initDie, 'd4');

    const stCol = rules.computeDerivedStats({ for: 3, agi: 3, con: 4, rap: 5, int: 3, cha: 3, ins: 3 }, { colonialEndurance: true });
    assert.equal(stCol.maxPe, 5);
    assert.equal(stCol.initDie, 'd6');
  });
});

describe('vtt-rules — Parser /r /jet /secret & Audit F1/Critiques', () => {
  it('parse /r, /jet et /secret avec effort, malus, seuil et raison', () => {
    const c1 = rules.parseRollCommand('/r 1d8 +2 -1 vs 5 Observation');
    assert.equal(c1.secret, false);
    assert.equal(c1.spec.faces, 8);
    assert.equal(c1.effort, 2);
    assert.equal(c1.malus, 1);
    assert.equal(c1.target, 5);
    assert.equal(c1.reason, 'Observation');

    const c2 = rules.parseRollCommand('/secret 2d6 e:4 entr:2 diff:6 "Sort de feu"');
    assert.equal(c2.secret, true);
    assert.equal(c2.spec.count, 2);
    assert.equal(c2.spec.faces, 6);
    assert.equal(c2.effort, 4);
    assert.equal(c2.trainings, 2);
    assert.equal(c2.target, 6);
    assert.equal(c2.reason, 'Sort de feu');
  });

  it('plafonne l’effort à 1 + entraînements (Audit F1)', () => {
    const res = rules.executeRoll('/r 1d6 e:5 entr:1 vs 5 Attaque', () => 0.5); // roll=4
    assert.equal(res.effort, 2);
    assert.equal(res.maxEffort, 2);
    assert.equal(res.effortClamped, true);
    assert.equal(res.total, 6);
    assert.equal(res.success, true);
  });

  it('détecte Critique (max naturel) et Échec Critique (1 sans entraînement)', () => {
    const crit = rules.executeRoll('/r 1d6', () => 0.99);
    assert.equal(crit.rolls[0], 6);
    assert.equal(crit.isCritical, true);
    assert.equal(crit.isFumble, false);

    const fumble = rules.executeRoll('/r 1d6', () => 0.0);
    assert.equal(fumble.rolls[0], 1);
    assert.equal(fumble.isFumble, true);

    const noFumbleTrained = rules.executeRoll('/r 1d6 entr:1', () => 0.0);
    assert.equal(noFumbleTrained.isFumble, false);

    const noFumbleMulti = rules.executeRoll('/r 2d6', () => 0.0);
    assert.equal(noFumbleMulti.isFumble, false);
  });

  it('applique la réussite automatique par palier de dé', () => {
    const auto = rules.executeRoll('/r d8 vs 3 Discrétion', () => 0.0); // roll=1 mais auto <=3
    assert.equal(auto.autoSuccess, true);
    assert.equal(auto.success, true);
  });

  it('lit la table des critiques chap. 9 avec arrondi inférieur', () => {
    assert.equal(rules.lookupCritTable(7).val, 6);
    assert.equal(rules.lookupCritTable(11).val, 10);
    assert.equal(rules.lookupCritTable(15).val, 12);
    assert.equal(rules.lookupCritTable(24).val, 24);
  });
});

describe('vtt-rules — Face-à-Face, déplacement, 6 zones & tick tour', () => {
  it('résout Face-à-Face (Δ<0, Δ=0, Δ>0) et éligibilité XP Audit F4', () => {
    const miss = rules.resolveFaceToFace({ atkRoll: 3, defRoll: 5, weaponDmg: 4 });
    assert.equal(miss.hit, false);
    assert.equal(miss.finalDamage, 0);
    assert.equal(miss.xpEligible, false);

    const sup = rules.resolveFaceToFace({ atkRoll: 5, defRoll: 5, weaponDmg: 4 });
    assert.equal(sup.hit, true);
    assert.equal(sup.superficial, true);
    assert.equal(sup.finalDamage, 0);
    assert.equal(sup.xpEligible, true);

    const hit = rules.resolveFaceToFace({ atkRoll: 8, atkEffort: 1, defRoll: 4, weaponDmg: 4, armor: 3, piercing: 1 });
    assert.equal(hit.delta, 5);
    assert.equal(hit.rawDamage, 9);
    assert.equal(hit.effArmor, 2);
    assert.equal(hit.finalDamage, 7);
    assert.equal(hit.xpEligible, true);

    const passive = rules.resolveFaceToFace({ atkRoll: 6, defPassive: true, weaponDmg: 3 });
    assert.equal(passive.xpEligible, false);
  });

  it('calcule coût de déplacement par case selon terrain/Ralenti/armure', () => {
    assert.equal(rules.computeStepApCost({}), 1);
    assert.equal(rules.computeStepApCost({ difficult: true, ralenti: true }), 3);
    assert.equal(rules.computeStepApCost({ difficult: true, ralenti: true, prone: true, overload: true, armorEnc: 1 }), 5);
  });

  it('évalue les 6 zones corporelles (Choc > CON×2 et Létal >= CON×5)', () => {
    const ev = rules.evaluateBodyDamage({ con: 3, zones: { head: 7, torso: 8, lArm: 0, rArm: 0, lLeg: 0, rLeg: 0 } });
    assert.equal(ev.cap, 6);
    assert.equal(ev.lethal, 15);
    assert.equal(ev.traumaZones.length, 2);
    assert.equal(ev.traumaZones[0].shockDiff, 4);
    assert.equal(ev.lethalReached, true);
    assert.equal(ev.healthLabel, 'Hors combat');
  });

  it('décrémente résiduels avant recharge PA et annule Accéléré <-> Ralenti', () => {
    const tok = {
      maxPa: 8, pa: 1,
      statuses: ['Accéléré', 'Ralenti', 'Étourdi'],
      dots: { Saignement: 2, 'En Feu': 1 },
      zones: { head: 0, torso: 0, lArm: 0, rArm: 0, lLeg: 0, rLeg: 0 },
      freeReactionUsed: true
    };
    const res = rules.tickTurnStart(tok);
    assert.equal(res.dotDamage, 3);
    assert.equal(tok.zones.torso, 3);
    assert.equal(tok.dots.Saignement, 1);
    assert.equal(tok.dots['En Feu'], undefined);
    assert.deepEqual(tok.statuses, ['Étourdi']);
    assert.equal(tok.pa, 8);
    assert.equal(tok.freeReactionUsed, false);
  });
});
