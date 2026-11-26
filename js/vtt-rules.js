/* ==========================================================================
   SYSTÈME RP — MOTEUR DE RÈGLES PARTAGÉ VTT (Node.js + Navigateur UMD)
   js/vtt-rules.js — zéro dépendance.
   Source canonique : Livres I, II, V, VI (baseline 6f497f7 + Codex v2.5).
   ========================================================================== */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) {
    module.exports = factory();
  } else {
    root.CodexVTTRules = factory();
  }
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  const DICE_SCALE = ['d2', 'd4', 'd6', 'd8', 'd10', 'd12', '2d6', '2d8', '2d10', '2d12', '2d12+10'];

  const AUTO_SUCCESS_MAX = {
    d2: 0,
    d4: 0,
    d6: 2,
    d8: 3,
    d10: 4,
    d12: 5,
    '2d6': 5,
    '2d8': 7,
    '2d10': 9,
    '2d12': 11,
    '2d12+10': 16
  };

  // Livre II chap. 9 — Table universelle des critiques (paliers 1 à 24).
  const CRIT_TABLE = [
    { val: 1, offensif: 'Cible À Terre', defensif: 'Attaquant À Terre', social: 'Interlocuteur déstabilisé', utilitaire: 'Tâche sans bruit' },
    { val: 2, offensif: 'Cible repoussée d’1 case', defensif: 'Pas de côté : repli gratuit d’1 case sans réaction', social: 'Ascendant psychologique', utilitaire: 'Gain d’élan cinétique' },
    { val: 3, offensif: 'Petite brèche : ignore 1 pt d’armure', defensif: '+1 Défense bonus (épreuve en cours)', social: 'Confiance immédiate', utilitaire: 'Précision chirurgicale' },
    { val: 4, offensif: '+1 ec attaquant (épreuve en cours)', defensif: '+1 ec cible (épreuve en cours)', social: '+1 ec persuasion (épreuve en cours)', utilitaire: '+1 ec tâche (épreuve en cours)' },
    { val: 5, offensif: '+2 Dégâts directs', defensif: '+2 Défense bonus (épreuve en cours)', social: 'L’interlocuteur cède', utilitaire: 'Économie de matériel' },
    { val: 6, offensif: 'Cible Étourdie (−1 ec) jusqu’à fin prochain tour', defensif: 'Attaquant Étourdi (−1 ec) jusqu’à fin prochain tour', social: 'Interlocuteur sidéré', utilitaire: 'Découverte inattendue' },
    { val: 8, offensif: 'Récupère 1 PA', defensif: 'Récupère 1 PA', social: 'Récupère 1 PA', utilitaire: 'Récupère 1 PA' },
    { val: 9, offensif: 'Récupère 1 Essoufflement', defensif: 'Récupère 1 Essoufflement', social: 'Regain de sang-froid', utilitaire: 'Regain de vigueur' },
    { val: 10, offensif: 'Cible Déstabilisée (−2 ec) jusqu’à fin prochain tour', defensif: 'Attaquant Déstabilisé jusqu’à fin prochain tour', social: 'Opposition neutralisée', utilitaire: 'Ralentissement temporel (1 à 5 tours)' },
    { val: 12, offensif: 'Cible Ralentie (×2 PA, +1 PA/case) jusqu’à fin prochain tour', defensif: 'Attaquant Ralenti jusqu’à fin prochain tour', social: 'Effondrement de l’argument', utilitaire: 'Composant réparé/sauvé' },
    { val: 16, offensif: 'Récupère 2 PA', defensif: 'Récupère 2 PA', social: 'Récupère 2 PA', utilitaire: 'Récupère 2 PA' },
    { val: 20, offensif: '3 Dégâts Résiduels', defensif: 'Contre-attaque auto (frappe d’opportunité 0 PA, hors réaction)', social: 'Ralliement inconditionnel', utilitaire: 'Survoltage arcanique total' },
    { val: 24, offensif: 'Cible Inconsciente jusqu’à fin prochain tour', defensif: 'Attaquant Inconscient jusqu’à fin prochain tour', social: 'Soumission absolue', utilitaire: 'Succès mirifique critique' }
  ];

  const BODY_ZONES = [
    { id: 'head', label: 'Tête / Cou', effect: 'Étourdi (−1 ec sur tous les jets)', escalate: 'Sonné 1 tour (À Terre, aucune action)' },
    { id: 'torso', label: 'Torse / Dos', effect: 'Déstabilisé (−2 ec, rampant +1 PA/case)', escalate: 'Cumulable avec les autres états' },
    { id: 'lArm', label: 'Bras gauche', effect: 'Désarçonné / arme lâchée (ou −2 ec)', escalate: 'Membre inutilisable jusqu’à soin' },
    { id: 'rArm', label: 'Bras droit', effect: 'Désarçonné / arme lâchée (ou −2 ec)', escalate: 'Membre inutilisable jusqu’à soin' },
    { id: 'lLeg', label: 'Jambe gauche', effect: 'À Terre + Ralenti (+1 PA/case)', escalate: 'Immobilisé jusqu’à soin' },
    { id: 'rLeg', label: 'Jambe droite', effect: 'À Terre + Ralenti (+1 PA/case)', escalate: 'Immobilisé jusqu’à soin' }
  ];

  const STATUS_PREJUDICIAL = [
    'À Terre', 'Affaibli', 'Assourdi', 'Aveugle', 'Canon Entravé',
    'Confus', 'Déstabilisé', 'Dissimulé', 'Étourdi', 'Immobilisé',
    'Inconscient', 'Insensible', 'Paralysé', 'Paniqué', 'Ralenti',
    'Sonné', 'Somnolent', 'Souffrant', 'Surpris'
  ];

  const STATUS_BENEFICIAL = [
    'Accéléré', 'Camouflé', 'Concentré', 'En transe',
    'Fortifié', 'Invisible', 'Survolté'
  ];

  const STATUS_DOT = ['Saignement', 'En Feu', 'Empoisonné', 'Asphyxie'];

  function clamp(n, min, max) {
    const v = Number(n);
    if (!Number.isFinite(v)) return min;
    return Math.max(min, Math.min(max, v));
  }

  function attrToStep(val) {
    const v = clamp(Math.round(Number(val) || 1), 1, 10);
    if (v <= 2) return 0;
    if (v <= 4) return 1;
    if (v <= 6) return 2;
    if (v <= 8) return 3;
    return 4;
  }

  function skillDie(baseRank, trainings) {
    const baseStep = attrToStep(baseRank);
    const tr = clamp(Math.trunc(Number(trainings) || 0), 0, 6);
    const idx = clamp(baseStep + tr, 0, DICE_SCALE.length - 1);
    return DICE_SCALE[idx];
  }

  function parseDieSpec(spec) {
    const raw = String(spec || '').trim().toLowerCase().replace(/\s+/g, '');
    if (!raw) return null;
    const m = /^(\d*)d(\d+)([+-]\d+)?$/.exec(raw);
    if (!m) return null;
    const count = m[1] === '' ? 1 : parseInt(m[1], 10);
    const faces = parseInt(m[2], 10);
    const mod = m[3] ? parseInt(m[3], 10) : 0;
    if (!Number.isFinite(count) || !Number.isFinite(faces) || !Number.isFinite(mod)) return null;
    if (count < 1 || count > 20 || faces < 2 || faces > 1000 || Math.abs(mod) > 1000) return null;
    const baseKey = (count === 1 ? 'd' + faces : count + 'd' + faces) + (mod ? (mod > 0 ? '+' + mod : String(mod)) : '');
    const canonKey = DICE_SCALE.includes(baseKey) ? baseKey : null;
    return { count, faces, mod, label: (count === 1 ? '1d' : count + 'd') + faces + (mod ? (mod > 0 ? '+' + mod : String(mod)) : ''), scaleKey: canonKey };
  }

  // Statistiques dérivées (Livre I §4 — post Audit F3 : 7 attributs + Magie séparée)
  function computeDerivedStats(attrs, opts) {
    const a = attrs || {};
    const f = clamp(a.for ?? a.FOR ?? 2, 1, 10);
    const agi = clamp(a.agi ?? a.AGI ?? 2, 1, 10);
    const con = clamp(a.con ?? a.CON ?? 2, 1, 10);
    const rap = clamp(a.rap ?? a.RAP ?? 2, 1, 10);
    const intl = clamp(a.int ?? a.INT ?? 2, 1, 10);
    const cha = clamp(a.cha ?? a.CHA ?? 2, 1, 10);
    const ins = clamp(a.ins ?? a.INS ?? a.sag ?? 2, 1, 10);
    const mag = clamp(a.mag ?? a.MAG ?? 0, 0, 10);

    const min7 = Math.min(f, agi, con, rap, intl, cha, ins);
    const maxPa = Math.max(agi, intl) + rap + min7;
    const cap = con * 2;
    const lethal = con * 5;
    const maxPe = con + (opts && opts.colonialEndurance ? 1 : 0);
    const initAttr = Math.max(rap, agi, intl);
    const initDie = DICE_SCALE[attrToStep(initAttr)];
    return {
      attrs: { for: f, agi, con, rap, int: intl, cha, ins, mag },
      maxPa,
      cap,
      lethal,
      maxPe,
      initDie
    };
  }

  // Parse "/r ...", "/roll ...", "/jet ...", "/secret ..."
  // Exemples :
  //   /r 1d8 +2 -1 vs 5 Observation
  //   /jet 2d6 e:2 m:1 diff:6 entr:2 Attaque épée
  //   /secret d10 vs 4 Discrétion
  function parseRollCommand(text) {
    const raw = String(text || '').trim();
    const headMatch = /^\/(r|roll|jet|secret)\b(.*)$/i.exec(raw);
    if (!headMatch) return null;
    const cmdName = headMatch[1].toLowerCase();
    const secret = cmdName === 'secret';
    let rest = (headMatch[2] || '').trim();
    if (!rest) return { error: 'Précise un dé (ex: /r 1d8 +2 vs 5 Observation).' };

    // Extraire la formule de dé initiale (sinon défaut 1d6 si seulement modificateurs ? Non, exiger le dé).
    const dieMatch = /^(\d*\s*[dD]\s*\d+(?:[+-]\d+)?)\b(.*)$/.exec(rest);
    if (!dieMatch) return { error: 'Formule de dé invalide. Ex: /r 1d8 +2 -1 vs 5 Observation' };
    const spec = parseDieSpec(dieMatch[1]);
    if (!spec) return { error: 'Dé hors limites (1-20 dés, d2-d1000).' };
    rest = (dieMatch[2] || '').trim();

    let effort = 0;
    let malus = 0;
    let target = null;
    let trainings = null;
    const reasonParts = [];

    const tokens = rest.match(/"[^"]*"|'[^']*'|\S+/g) || [];
    for (let i = 0; i < tokens.length; i++) {
      const tk = tokens[i];
      const low = tk.toLowerCase();
      if ((low === 'vs' || low === 'diff' || low === 'sd' || low === 'seuil') && i + 1 < tokens.length && /^-?\d+$/.test(tokens[i + 1])) {
        target = clamp(parseInt(tokens[++i], 10), 0, 99);
      } else if (/^(?:vs|diff|sd|seuil)[:=](\d+)$/i.test(tk)) {
        target = clamp(parseInt(RegExp.$1, 10), 0, 99);
      } else if (/^(?:e|eff|effort|pa|pe)[:=](\d+)$/i.test(tk)) {
        effort += clamp(parseInt(RegExp.$1, 10), 0, 20);
      } else if (/^(?:m|malus)[:=](\d+)$/i.test(tk)) {
        malus += clamp(parseInt(RegExp.$1, 10), 0, 50);
      } else if (/^(?:entr|tr|entrainement|entrainements)[:=](\d+)$/i.test(tk)) {
        trainings = clamp(parseInt(RegExp.$1, 10), 0, 6);
      } else if (/^\+(\d+)$/.test(tk)) {
        effort += clamp(parseInt(RegExp.$1, 10), 0, 20);
      } else if (/^-(\d+)$/.test(tk)) {
        malus += clamp(parseInt(RegExp.$1, 10), 0, 50);
      } else {
        reasonParts.push(tk.replace(/^["']|["']$/g, ''));
      }
    }

    return {
      spec,
      effort,
      malus,
      target,
      trainings,
      secret,
      reason: reasonParts.join(' ').trim().slice(0, 120)
    };
  }

  function executeRoll(input, rngFn) {
    const rng = typeof rngFn === 'function' ? rngFn : Math.random;
    const cmd = typeof input === 'string' ? parseRollCommand(input) : input;
    if (!cmd || cmd.error) return { error: (cmd && cmd.error) || 'Commande invalide.' };
    const spec = cmd.spec || parseDieSpec(cmd.formula || '1d6');
    if (!spec) return { error: 'Formule invalide.' };

    const trainings = cmd.trainings === null || cmd.trainings === undefined ? null : clamp(cmd.trainings, 0, 6);
    const maxEffort = trainings === null ? null : (1 + trainings);
    let effort = clamp(cmd.effort || 0, 0, 30);
    let effortClamped = false;
    if (maxEffort !== null && effort > maxEffort) {
      effort = maxEffort;
      effortClamped = true;
    }
    const malus = clamp(cmd.malus || 0, 0, 50);
    const target = cmd.target === null || cmd.target === undefined ? null : clamp(cmd.target, 0, 99);

    const rolls = [];
    let dieSum = 0;
    for (let i = 0; i < spec.count; i++) {
      const r = Math.min(spec.faces, Math.max(1, Math.floor(rng() * spec.faces) + 1));
      rolls.push(r);
      dieSum += r;
    }

    const total = dieSum + spec.mod + effort - malus;
    const isCritical = rolls.some((r) => r === spec.faces);
    const effTrainings = trainings === null ? 0 : trainings;
    const isFumble = spec.count === 1 && effTrainings === 0 && rolls[0] === 1;
    const autoMax = spec.scaleKey && (spec.scaleKey in AUTO_SUCCESS_MAX) ? AUTO_SUCCESS_MAX[spec.scaleKey] : null;
    const autoSuccess = target !== null && autoMax !== null && target <= autoMax;
    const delta = target !== null ? total - target : null;
    const success = target !== null ? (autoSuccess || total >= target) : null;

    const parts = ['[' + rolls.join(' + ') + ']'];
    if (spec.mod) parts.push((spec.mod > 0 ? '+ ' : '− ') + Math.abs(spec.mod));
    if (effort) parts.push('+ ' + effort + ' effort');
    if (malus) parts.push('− ' + malus + ' malus');
    const detail = parts.join(' ') + ' = ' + total;

    return {
      formula: spec.scaleKey || spec.label,
      rolls,
      faces: spec.faces,
      count: spec.count,
      mod: spec.mod,
      effort,
      effortClamped,
      maxEffort,
      trainings,
      malus,
      total,
      roll: total,
      target,
      delta,
      success,
      autoSuccess,
      isCritical,
      isFumble,
      secret: !!cmd.secret,
      reason: String(cmd.reason || '').slice(0, 120),
      detail
    };
  }

  // Table des critiques chap. 9 : arrondi au palier égal ou inférieur le plus proche.
  function lookupCritTable(rollVal) {
    const v = Math.max(1, Math.trunc(Number(rollVal) || 1));
    let picked = CRIT_TABLE[0];
    for (let i = 0; i < CRIT_TABLE.length; i++) {
      if (CRIT_TABLE[i].val <= v) picked = CRIT_TABLE[i];
      else break;
    }
    return picked;
  }

  // Coût en PA par case de 1,5 m (Livre V + Livre II §8.4)
  function computeStepApCost(opts) {
    const o = opts || {};
    let cost = 1;
    if (o.difficult) cost += 1;
    if (o.ralenti) cost += 1;
    if (o.prone) cost += 1;
    if (o.overload) cost += 1;
    cost = Math.min(4, cost);
    cost += Math.max(0, Math.trunc(Number(o.armorEnc) || 0));
    return cost;
  }

  // Résolution Face-à-Face (Livre V §20.2, Audit F1 & Audit F4)
  function resolveFaceToFace(params) {
    const p = params || {};
    const atkTr = clamp(p.atkTrainings ?? 0, 0, 6);
    const defTr = clamp(p.defTrainings ?? 0, 0, 6);
    const atkEffort = Math.min(clamp(p.atkEffort || 0, 0, 20), 1 + atkTr);
    const defEffort = Math.min(clamp(p.defEffort || 0, 0, 20), 1 + defTr);
    const atkMalus = clamp(p.atkMalus || 0, 0, 50);
    const defMalus = clamp(p.defMalus || 0, 0, 50);

    const atkTotal = Math.trunc(Number(p.atkRoll) || 0) + atkEffort - atkMalus;
    let defTotal = 0;
    if (!p.defPassive) {
      defTotal = Math.trunc(Number(p.defRoll) || 0) + defEffort - defMalus - (p.defNoPa ? 2 : 0);
    }

    const delta = atkTotal - defTotal;
    const hit = delta >= 0;
    const superficial = delta === 0;
    const rawDamage = delta > 0 ? Math.max(0, Math.trunc(Number(p.weaponDmg) || 0) + delta) : 0;
    const effArmor = Math.max(0, Math.trunc(Number(p.armor) || 0) - Math.trunc(Number(p.piercing) || 0));
    const afterArmor = Math.max(0, rawDamage - effArmor);
    const shield = Math.max(0, Math.trunc(Number(p.shieldAbsorb) || 0));
    const finalDamage = Math.max(0, afterArmor - shield);

    const xpEligible = hit && !p.targetIsPassive && !p.targetIncapacitated && !p.defPassive;

    return {
      atkTotal,
      defTotal,
      delta,
      hit,
      superficial,
      rawDamage,
      effArmor,
      finalDamage,
      xpEligible
    };
  }

  // Évalue les 6 zones corporelles & seuils Livre VI §23
  function evaluateBodyDamage(token) {
    const t = token || {};
    const con = clamp(t.con ?? 3, 1, 10);
    const cap = con * 2;
    const lethal = con * 5;
    const zones = t.zones || {};
    let totalDamage = 0;
    const traumaZones = [];
    for (let i = 0; i < BODY_ZONES.length; i++) {
      const z = BODY_ZONES[i];
      const dmg = Math.max(0, Math.trunc(Number(zones[z.id]) || 0));
      totalDamage += dmg;
      if (dmg > cap) {
        traumaZones.push({
          id: z.id,
          label: z.label,
          damage: dmg,
          excess: dmg - cap,
          shockDiff: 3 + (dmg - cap),
          effect: z.effect
        });
      }
    }
    const lethalReached = totalDamage >= lethal;
    let healthLabel = 'Indemne';
    if (lethalReached || (Array.isArray(t.statuses) && t.statuses.includes('Inconscient'))) {
      healthLabel = 'Hors combat';
    } else if (traumaZones.length > 0 || totalDamage >= lethal * 0.65) {
      healthLabel = 'Critique';
    } else if (totalDamage >= Math.max(2, Math.ceil(lethal * 0.25))) {
      healthLabel = 'Blessé';
    } else if (totalDamage > 0) {
      healthLabel = 'Égratigné';
    }

    return {
      con,
      cap,
      lethal,
      totalDamage,
      traumaZones,
      lethalReached,
      healthLabel
    };
  }

  // Début de tour personnel : résiduels −1 avant recharge PA, annulation Accéléré <-> Ralenti.
  function tickTurnStart(token) {
    if (!token || typeof token !== 'object') return { dotDamage: 0, logs: [] };
    const logs = [];
    let dotDamage = 0;
    const dots = token.dots && typeof token.dots === 'object' ? token.dots : (token.dots = {});
    for (let i = 0; i < STATUS_DOT.length; i++) {
      const name = STATUS_DOT[i];
      const val = Math.max(0, Math.trunc(Number(dots[name]) || 0));
      if (val > 0) {
        dotDamage += val;
        if (!token.zones) token.zones = { head: 0, torso: 0, lArm: 0, rArm: 0, lLeg: 0, rLeg: 0 };
        token.zones.torso = Math.max(0, Math.trunc(Number(token.zones.torso) || 0) + val);
        const nextVal = val - 1;
        if (nextVal > 0) dots[name] = nextVal;
        else delete dots[name];
        logs.push(name + ' : ' + val + ' dégât(s) (passe à ' + nextVal + ')');
      }
    }

    if (Array.isArray(token.statuses)) {
      const hasAcc = token.statuses.includes('Accéléré');
      const hasRal = token.statuses.includes('Ralenti');
      if (hasAcc && hasRal) {
        token.statuses = token.statuses.filter((s) => s !== 'Accéléré' && s !== 'Ralenti');
        logs.push('Annulation mutuelle : Accéléré ↔ Ralenti');
      }
    }

    const maxPa = Math.max(1, Math.trunc(Number(token.maxPa) || 6));
    token.pa = maxPa;
    token.freeReactionUsed = false;
    return { dotDamage, logs };
  }

  return {
    DICE_SCALE,
    AUTO_SUCCESS_MAX,
    CRIT_TABLE,
    BODY_ZONES,
    STATUS_PREJUDICIAL,
    STATUS_BENEFICIAL,
    STATUS_DOT,
    attrToStep,
    skillDie,
    parseDieSpec,
    computeDerivedStats,
    parseRollCommand,
    executeRoll,
    lookupCritTable,
    computeStepApCost,
    resolveFaceToFace,
    evaluateBodyDamage,
    tickTurnStart
  };
}));
