/* Brouillard de guerre asymétrique — tests du filtre serveur (zéro dépendance).
   Livre VI §25.4 : 360° (pas de cône), portée d'ambiance, murs Full.
   Exécution : node --test users/tests/vtt_visibility.test.js */
'use strict';

const { describe, it } = require('node:test');
const assert = require('node:assert/strict');
const vis = require('../vtt_visibility.js');

describe('hexDistance (axiale)', () => {
    it('origine = 0, voisin = 1', () => {
        assert.equal(vis.hexDistance(0, 0, 0, 0), 0);
        assert.equal(vis.hexDistance(0, 0, 1, 0), 1);
        assert.equal(vis.hexDistance(0, 0, 1, -1), 1);
        assert.equal(vis.hexDistance(0, 0, 3, -1), 3);
    });
});

describe('isInCone (helper pur, inopérant en jeu : KT = 360°)', () => {
    it('panoramique voit tout, même case toujours vue', () => {
        assert.equal(vis.isInCone(0, 0, 0, 2, 0, true), true);
        assert.equal(vis.isInCone(1, 1, 123, 1, 1, false), true);
    });
});

describe('isCellVisible (360° : le cap est ignoré)', () => {
    const noCovers = new Map();
    it('aucune direction de vision : est comme sud', () => {
        // Portée d'ambiance 6 : (2,0) plein est VISIBLE quel que soit le cap.
        assert.equal(vis.isCellVisible({ q: 0, r: 0, vision: 6 }, { q: 2, r: 0 }, noCovers), true);
        assert.equal(vis.isCellVisible({ q: 0, r: 0, yaw: 180, vision: 6 }, { q: 2, r: 0 }, noCovers), true);
        assert.equal(vis.isCellVisible({ q: 0, r: 0, yaw: 0, vision: 6 }, { q: 0, r: 2 }, noCovers), true);
    });
    it('portée environnementale courte 6 / longue 12', () => {
        assert.equal(vis.isCellVisible({ q: 0, r: 0, vision: 6 }, { q: 0, r: 6 }, noCovers), true);
        assert.equal(vis.isCellVisible({ q: 0, r: 0, vision: 6 }, { q: 0, r: 7 }, noCovers), false);
        assert.equal(vis.isCellVisible({ q: 0, r: 0, vision: 12 }, { q: 0, r: 10 }, noCovers), true);
        assert.equal(vis.isCellVisible({ q: 0, r: 0, vision: 12 }, { q: 0, r: 13 }, noCovers), false);
    });
    it('contact toujours visible', () => {
        assert.equal(vis.isCellVisible({ q: 0, r: 0, vision: 6 }, { q: 1, r: 0 }, noCovers), true);
    });
    it('mur Full intermediaire bloque', () => {
        const obs = { q: 0, r: -1, vision: 12 };
        const covers = new Map([['0,0', 2], ['0,1', 2], ['0,2', 2]]);
        assert.equal(vis.isCellVisible(obs, { q: 0, r: 3 }, covers), false);
        assert.equal(vis.isCellVisible(obs, { q: 0, r: 3 }, noCovers), true);
    });
    it('thermique voit jusqu à Portee/2 derriere un Full', () => {
        const obs = { q: 0, r: -1, vision: 12, thermal: true };
        const covers = new Map([['0,0', 2], ['0,1', 2], ['0,2', 2]]);
        assert.equal(vis.isCellVisible(obs, { q: 0, r: 5 }, covers), true); // dist 6 <= 6
        assert.equal(vis.isCellVisible(obs, { q: 0, r: 6 }, covers), false); // dist 7 > 6
    });
    it('couvert Half ne bloque jamais la vue', () => {
        const obs = { q: 0, r: -1, vision: 12 };
        const covers = new Map([['0,1', 1]]);
        assert.equal(vis.isCellVisible(obs, { q: 0, r: 3 }, covers), true);
    });
});

describe('isBlockedByWall', () => {
    it('sans mur = false, avec mur Full = true', () => {
        assert.equal(vis.isBlockedByWall(0, -1, 0, 3, new Map()), false);
        assert.equal(vis.isBlockedByWall(0, -1, 0, 3, new Map([['0,1', 2]])), true);
    });
    it('les extremites ne bloquent pas', () => {
        // Full sur la case cible elle-meme : n'empeche pas de la voir.
        assert.equal(vis.isBlockedByWall(0, 0, 0, 2, new Map([['0,2', 2]])), false);
    });
});

describe('isVisibleToAny (escouade)', () => {
    it('union des champs 360°, vide = false', () => {
        assert.equal(vis.isVisibleToAny([], { q: 0, r: 1 }, new Map()), false);
        const squad = [
            { q: 0, r: 0, vision: 6 },
            { q: 5, r: 0, vision: 6 },
        ];
        assert.equal(vis.isVisibleToAny(squad, { q: 0, r: 2 }, new Map()), true);
        assert.equal(vis.isVisibleToAny(squad, { q: 5, r: 7 }, new Map()), false);
    });
});
