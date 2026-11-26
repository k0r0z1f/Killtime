/* ==========================================================================
   VTT COMBAT — Face-à-Face Masqué, Réactions hors tour, Audit F4 XP, Handouts
   js/vtt-combat.js
   ========================================================================== */
(function () {
  'use strict';
  const esc = (s) => String(s || '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  function activeScene(st) {
    if (!st || !Array.isArray(st.scenes)) return null;
    return st.scenes.find((s) => s.id === st.activeSceneId) || st.scenes[0];
  }

  function triggerReaction(token, kind, paCost, useFree) {
    if (!token || !window.CodexVTT) return;
    const sc = activeScene(window.CodexVTT.getState());
    if (!sc) return;
    const labels = {
      guet: '👁️ Guet / Overwatch',
      opportunite: '⚔️ Attaque d’Opportunité',
      prise_de_coup: '🛡️ Prise de coup',
      contre_sort: '✨ Contre-sort'
    };
    window.CodexVTT.sendOp('vtt_reaction', {
      sceneId: sc.id,
      tokenId: token.id,
      kind,
      paCost: paCost || 1,
      useFreeReaction: !!useFree
    });
    if (window.CodexTables && window.CodexTables.sendRichMessage) {
      window.CodexTables.sendRichMessage(
        (labels[kind] || kind) + ' — ' + token.name + (useFree ? ' (Réaction Libre)' : ' (' + (paCost || 1) + ' PA)'),
        { kind: 'action', action: { nom: labels[kind] || kind, cout_pa: useFree ? 'Réaction Libre' : (paCost + ' PA') } }
      );
    }
  }

  function renderDuelCardHtml(duel, sc, isGM, myUserId) {
    const rules = window.CodexVTTRules;
    if (!duel) return '';
    if (duel.revealed && duel.result) {
      const r = duel.result;
      const body = r.bodyEval;
      return '<div class="vtt-card" style="border-color:var(--temporal-amber);">' +
        '<h4>⚔️ Résultat Face-à-Face : ' + esc(duel.attackerName) + ' vs ' + esc(duel.defenderName) + '</h4>' +
        '<div style="font-size:12px;">' +
        '<div>Attaque : <strong>' + r.atkTotal + '</strong> (dé ' + (r.atkRoll ? r.atkRoll.total : '?') + ') vs Défense : <strong>' + r.defTotal + '</strong></div>' +
        '<div>Différentiel Net : <strong>Δ ' + (r.delta >= 0 ? '+' + r.delta : r.delta) + '</strong> · ' +
        (r.hit ? (r.superficial ? 'Touche superficielle (0 dégât)' : 'Dégâts bruts ' + r.rawDamage + ' → Finaux <strong>' + r.finalDamage + '</strong> (Armure eff. ' + r.effArmor + ')') : 'Esquivé / Paré !') +
        '</div>' +
        (body && body.traumaZones.length ? '<div class="vtt-alert" style="margin-top:6px;">⚠️ Choc Traumatique : ' + body.traumaZones.map((z) => z.label + ' (Diff ' + z.shockDiff + ')').join(', ') + '</div>' : '') +
        (body && body.lethalReached ? '<div class="vtt-alert" style="margin-top:4px;">☠️ Seuil Létal atteint !</div>' : '') +
        '</div>' +
        '<div style="display:flex;gap:6px;margin-top:8px;flex-wrap:wrap;">' +
        (r.xpEligible ? '<button class="vtt-btn emerald" data-duel-xp="' + esc(duel.id) + '" type="button">✨ Cocher +1 XP (Enjeu valide)</button>' : '<span class="tb-status">0 case XP (Audit F4)</span>') +
        '<button class="vtt-btn" data-duel-pub="' + esc(duel.id) + '" type="button">💬 Publier au chat</button>' +
        '<button class="vtt-btn danger" data-duel-close="' + esc(duel.id) + '" type="button">Fermer</button>' +
        '</div></div>';
    }

    const canAtk = isGM || (myUserId && duel.attackerOwnerId === myUserId);
    const canDef = isGM || (myUserId && duel.defenderOwnerId === myUserId);
    const ab = duel.myBet && duel.myBet.side === 'attacker' ? duel.myBet : duel.attackerBet;
    const db = duel.myBet && duel.myBet.side === 'defender' ? duel.myBet : duel.defenderBet;

    return '<div class="vtt-card">' +
      '<h4>🎭 Mises simultanées masquées — ' + esc(duel.attackerName) + ' vs ' + esc(duel.defenderName) + '</h4>' +
      '<div class="tb-status">Arme : ' + esc(duel.weaponName) + ' (' + duel.weaponDmg + ' dég., Perf ' + duel.piercing + ') · Zone : ' + esc(duel.targetZone) + (duel.aimLevel === 2 ? ' (Point vital -2)' : '') + '</div>' +
      '<div class="vtt-grid-2" style="margin-top:8px;">' +
      '<div class="vtt-card"><h4>⚔️ Attaquant ' + (duel.attackerReady || duel.attackerBet ? '✅ Prêt' : '⏳') + '</h4>' +
      (canAtk ? (
        '<div class="vtt-grid-2">' +
        '<select class="vtt-select" id="duelAtkDie">' + (rules ? rules.DICE_SCALE.map((d) => '<option value="' + d + '"' + (d === duel.atkDie ? ' selected' : '') + '>' + d + '</option>').join('') : '') + '</select>' +
        '<input class="vtt-select" id="duelAtkTr" type="number" min="0" max="6" value="' + (ab ? ab.trainings : duel.atkTrainings) + '" title="Entraînements">' +
        '<input class="vtt-select" id="duelAtkPa" type="number" min="0" max="7" value="' + (ab ? ab.effortPa : 0) + '" placeholder="Effort PA" title="Effort PA">' +
        '<input class="vtt-select" id="duelAtkPe" type="number" min="0" max="7" value="' + (ab ? ab.effortPe : 0) + '" placeholder="Effort PE" title="Effort PE">' +
        '</div><button class="vtt-btn on" data-duel-commit="attacker" data-duel-id="' + esc(duel.id) + '" type="button" style="margin-top:6px;width:100%;">🔒 Valider mise Attaque</button>'
      ) : '<p class="tb-status">En attente du joueur attaquant…</p>') +
      '</div>' +
      '<div class="vtt-card"><h4>🛡️ Défenseur ' + (duel.defenderReady || duel.defenderBet ? '✅ Prêt' : '⏳') + '</h4>' +
      (canDef ? (
        '<div class="vtt-grid-2">' +
        '<select class="vtt-select" id="duelDefDie">' + (rules ? rules.DICE_SCALE.map((d) => '<option value="' + d + '"' + (d === duel.defDie ? ' selected' : '') + '>' + d + '</option>').join('') : '') + '</select>' +
        '<input class="vtt-select" id="duelDefTr" type="number" min="0" max="6" value="' + (db ? db.trainings : duel.defTrainings) + '" title="Entraînements">' +
        '<input class="vtt-select" id="duelDefPa" type="number" min="0" max="7" value="' + (db ? db.effortPa : 0) + '" placeholder="Effort PA" title="Effort PA">' +
        '<input class="vtt-select" id="duelDefPe" type="number" min="0" max="7" value="' + (db ? db.effortPe : 0) + '" placeholder="Effort PE" title="Effort PE">' +
        '</div><label style="font-size:11px;display:flex;gap:4px;margin-top:4px;"><input type="checkbox" id="duelDefPassive"' + (db && db.passive ? ' checked' : '') + '> Défense Passive (0)</label>' +
        '<label style="font-size:11px;display:flex;gap:4px;"><input type="checkbox" id="duelDefNoPa"' + (db && db.noPa ? ' checked' : '') + '> Défense à sec (−2)</label>' +
        '<button class="vtt-btn on" data-duel-commit="defender" data-duel-id="' + esc(duel.id) + '" type="button" style="margin-top:6px;width:100%;">🔒 Valider mise Défense</button>'
      ) : '<p class="tb-status">En attente du joueur défenseur…</p>') +
      '</div></div>' +
      (isGM ? '<div style="display:flex;gap:6px;margin-top:8px;justify-content:flex-end;"><button class="vtt-btn amber" data-duel-reveal="' + esc(duel.id) + '" type="button">👁️ Révéler &amp; Résoudre (MJ)</button><button class="vtt-btn danger" data-duel-close="' + esc(duel.id) + '" type="button">Annuler</button></div>' : '') +
      '</div>';
  }

  function openDuelModal(attackerId, defenderId) {
    const rules = window.CodexVTTRules;
    if (!window.CodexVTT || !rules) return;
    const back = document.createElement('div');
    back.className = 'vtt-modal-back';
    document.body.appendChild(back);

    function render() {
      const st = window.CodexVTT.getState();
      const sc = activeScene(st);
      const isGM = window.CodexVTT.isGM();
      const me = window.CodexTables && window.CodexTables.getMe ? window.CodexTables.getMe() : null;
      const myId = me && me.id;
      const tokens = (sc && sc.tokens) || [];
      const activeDuels = Object.values((st && st.duels) || {});

      let h = '<div class="vtt-modal"><div class="vtt-modal-head"><h3>⚔️ Face-à-Face Masqué (Livre V §20.2)</h3>' +
        '<button class="vtt-btn" id="vttDuelCloseModal" type="button">✕</button></div>';

      if (activeDuels.length) {
        activeDuels.forEach((d) => { h += renderDuelCardHtml(d, sc, isGM, myId); });
      }

      if (tokens.length >= 2) {
        const atk = tokens.find((t) => t.id === attackerId) || tokens[0];
        const inv = (atk && atk.inventory) || [];
        h += '<div class="vtt-card" style="margin-top:10px;"><h4>Nouveau Face-à-Face</h4>' +
          '<div class="vtt-grid-2">' +
          '<div class="tb-field"><label>Attaquant</label><select id="vttNewDuelAtk">' + tokens.map((t) => '<option value="' + esc(t.id) + '"' + (atk && t.id === atk.id ? ' selected' : '') + '>' + esc(t.name) + '</option>').join('') + '</select></div>' +
          '<div class="tb-field"><label>Défenseur</label><select id="vttNewDuelDef">' + tokens.map((t) => '<option value="' + esc(t.id) + '"' + (defenderId && t.id === defenderId ? ' selected' : '') + '>' + esc(t.name) + '</option>').join('') + '</select></div>' +
          '<div class="tb-field"><label>Zone visée</label><select id="vttNewDuelZone">' + rules.BODY_ZONES.map((z) => '<option value="' + z.id + '"' + (z.id === 'torso' ? ' selected' : '') + '>' + esc(z.label) + '</option>').join('') + '</select></div>' +
          '<div class="tb-field"><label>Visée</label><select id="vttNewDuelAim"><option value="1">Niv. 1 (Standard 0)</option><option value="2">Niv. 2 (Point vital −2)</option></select></div>' +
          '<div class="tb-field"><label>Arme / Attaque</label><input id="vttNewDuelWep" value="' + esc(inv[0] ? inv[0].nom : 'Frappe tactique') + '"></div>' +
          '<div class="tb-field"><label>Dégâts base / Perforante</label><div class="tb-row"><input id="vttNewDuelDmg" type="number" min="0" value="' + (inv[0] ? inv[0].degats : 3) + '"><input id="vttNewDuelPerf" type="number" min="0" value="' + (inv[0] ? inv[0].perforante : 0) + '" title="Perforante"></div></div>' +
          '</div><button class="vtt-btn on" id="vttCreateDuelBtn" type="button">🎭 Ouvrir le Face-à-Face masqué</button></div>';
      } else {
        h += '<p class="tb-status">Place au moins 2 pions sur le plateau pour lancer un Face-à-Face.</p>';
      }
      h += '</div>';
      back.innerHTML = h;

      back.querySelector('#vttDuelCloseModal').addEventListener('click', () => { unsub(); back.remove(); });
      const crBtn = back.querySelector('#vttCreateDuelBtn');
      if (crBtn) crBtn.addEventListener('click', () => {
        window.CodexVTT.sendOp('vtt_duel', {
          action: 'create',
          sceneId: sc.id,
          attackerTokenId: back.querySelector('#vttNewDuelAtk').value,
          defenderTokenId: back.querySelector('#vttNewDuelDef').value,
          targetZone: back.querySelector('#vttNewDuelZone').value,
          aimLevel: parseInt(back.querySelector('#vttNewDuelAim').value, 10),
          weaponName: back.querySelector('#vttNewDuelWep').value,
          weaponDmg: parseInt(back.querySelector('#vttNewDuelDmg').value, 10) || 0,
          piercing: parseInt(back.querySelector('#vttNewDuelPerf').value, 10) || 0
        });
      });
      back.querySelectorAll('[data-duel-commit]').forEach((b) => b.addEventListener('click', () => {
        const side = b.getAttribute('data-duel-commit');
        const duelId = b.getAttribute('data-duel-id');
        if (side === 'attacker') {
          window.CodexVTT.sendOp('vtt_duel', {
            action: 'commit', duelId, side: 'attacker',
            die: back.querySelector('#duelAtkDie').value,
            trainings: parseInt(back.querySelector('#duelAtkTr').value, 10) || 0,
            effortPa: parseInt(back.querySelector('#duelAtkPa').value, 10) || 0,
            effortPe: parseInt(back.querySelector('#duelAtkPe').value, 10) || 0
          });
        } else {
          window.CodexVTT.sendOp('vtt_duel', {
            action: 'commit', duelId, side: 'defender',
            die: back.querySelector('#duelDefDie').value,
            trainings: parseInt(back.querySelector('#duelDefTr').value, 10) || 0,
            effortPa: parseInt(back.querySelector('#duelDefPa').value, 10) || 0,
            effortPe: parseInt(back.querySelector('#duelDefPe').value, 10) || 0,
            passive: back.querySelector('#duelDefPassive').checked,
            noPa: back.querySelector('#duelDefNoPa').checked
          });
        }
      }));
      back.querySelectorAll('[data-duel-reveal]').forEach((b) => b.addEventListener('click', () => {
        window.CodexVTT.sendOp('vtt_duel', { action: 'reveal', duelId: b.getAttribute('data-duel-reveal') });
      }));
      back.querySelectorAll('[data-duel-close]').forEach((b) => b.addEventListener('click', () => {
        window.CodexVTT.sendOp('vtt_duel', { action: 'close', duelId: b.getAttribute('data-duel-close') });
      }));
      back.querySelectorAll('[data-duel-xp]').forEach((b) => b.addEventListener('click', () => {
        const duel = activeDuels.find((x) => x.id === b.getAttribute('data-duel-xp'));
        if (duel) {
          window.CodexVTT.sendOp('vtt_token', { action: 'tick_xp', sceneId: sc.id, tokenId: duel.attackerTokenId });
          b.disabled = true;
          b.textContent = '✓ +1 XP coché';
        }
      }));
      back.querySelectorAll('[data-duel-pub]').forEach((b) => b.addEventListener('click', () => {
        const duel = activeDuels.find((x) => x.id === b.getAttribute('data-duel-pub'));
        if (duel && duel.result && window.CodexTables && window.CodexTables.sendRichMessage) {
          const r = duel.result;
          window.CodexTables.sendRichMessage(
            '⚔️ ' + duel.attackerName + ' → ' + duel.defenderName + ' (' + duel.weaponName + ') : Δ ' + (r.delta >= 0 ? '+' + r.delta : r.delta) + (r.hit ? ', ' + r.finalDamage + ' dégâts' : ', esquivé'),
            { kind: 'combat', combat: { attacker: duel.attackerName, defender: duel.defenderName, delta: r.delta, finalDamage: r.finalDamage, hit: r.hit, xpEligible: r.xpEligible, zone: duel.targetZone } }
          );
          b.disabled = true;
        }
      }));
    }

    const unsub = window.CodexVTT.on('state', render);
    back.addEventListener('click', (e) => { if (e.target === back) { unsub(); back.remove(); } });
    render();
  }

  function showHandoutOverlay(ho) {
    if (!ho) return;
    const back = document.createElement('div');
    back.className = 'vtt-modal-back';
    back.innerHTML = '<div class="vtt-modal">' +
      '<div class="vtt-modal-head"><h3>🖼️ ' + esc(ho.title || 'Indice') + '</h3><button class="vtt-btn" id="vttHoClose" type="button">✕</button></div>' +
      (ho.imageUrl ? '<img src="' + esc(ho.imageUrl) + '" style="max-width:100%;max-height:60vh;display:block;margin:0 auto 12px;border-radius:10px;">' : '') +
      (ho.text ? '<p style="white-space:pre-wrap;line-height:1.5;">' + esc(ho.text) + '</p>' : '') +
      '</div>';
    document.body.appendChild(back);
    back.querySelector('#vttHoClose').addEventListener('click', () => back.remove());
    back.addEventListener('click', (e) => { if (e.target === back) back.remove(); });
  }

  function openHandoutsModal() {
    if (!window.CodexVTT) return;
    const back = document.createElement('div');
    back.className = 'vtt-modal-back';
    document.body.appendChild(back);

    function render() {
      const st = window.CodexVTT.getState();
      const isGM = window.CodexVTT.isGM();
      const list = (st && st.handouts) || [];
      let h = '<div class="vtt-modal"><div class="vtt-modal-head"><h3>🖼️ Handouts &amp; Indices</h3><button class="vtt-btn" id="vttHosClose" type="button">✕</button></div>';
      if (isGM) {
        h += '<div class="vtt-card" style="margin-bottom:10px;"><h4>Nouveau Handout</h4>' +
          '<div class="tb-field"><label>Titre</label><input id="vttHoTitle" placeholder="Ex : Lettre de Valerius"></div>' +
          '<div class="tb-field"><label>Image URL (optionnel)</label><input id="vttHoImg" placeholder="https://..."></div>' +
          '<div class="tb-field"><label>Texte / Indice</label><textarea id="vttHoTxt" placeholder="Contenu révélé aux joueurs…"></textarea></div>' +
          '<button class="vtt-btn on" id="vttHoCreate" type="button">+ Créer Handout</button></div>';
      }
      if (!list.length) h += '<p class="tb-status">Aucun handout pour cette table.</p>';
      list.forEach((ho) => {
        h += '<div class="vtt-zone-row"><span><strong>' + esc(ho.title) + '</strong></span>' +
          '<span style="display:flex;gap:4px;">' +
          '<button class="vtt-mini-btn" data-ho-view="' + esc(ho.id) + '" type="button">👁️ Voir</button>' +
          (isGM ? '<button class="vtt-mini-btn" data-ho-show="' + esc(ho.id) + '" type="button">📺 Projeter</button><button class="vtt-mini-btn" data-ho-del="' + esc(ho.id) + '" type="button">✕</button>' : '') +
          '</span></div>';
      });
      h += '</div>';
      back.innerHTML = h;
      back.querySelector('#vttHosClose').addEventListener('click', () => { unsub(); back.remove(); });
      const cr = back.querySelector('#vttHoCreate');
      if (cr) cr.addEventListener('click', () => {
        window.CodexVTT.sendOp('vtt_handout', {
          action: 'create',
          title: back.querySelector('#vttHoTitle').value,
          imageUrl: back.querySelector('#vttHoImg').value,
          text: back.querySelector('#vttHoTxt').value
        });
      });
      back.querySelectorAll('[data-ho-view]').forEach((b) => b.addEventListener('click', () => {
        const ho = list.find((x) => x.id === b.getAttribute('data-ho-view'));
        if (ho) showHandoutOverlay(ho);
      }));
      back.querySelectorAll('[data-ho-show]').forEach((b) => b.addEventListener('click', () => {
        window.CodexVTT.sendOp('vtt_handout', { action: 'show', handoutId: b.getAttribute('data-ho-show') });
      }));
      back.querySelectorAll('[data-ho-del]').forEach((b) => b.addEventListener('click', () => {
        window.CodexVTT.sendOp('vtt_handout', { action: 'delete', handoutId: b.getAttribute('data-ho-del') });
      }));
    }

    const unsub = window.CodexVTT.on('state', render);
    back.addEventListener('click', (e) => { if (e.target === back) { unsub(); back.remove(); } });
    render();
  }

  window.CodexVTTCombat = {
    triggerReaction,
    openDuelModal,
    openHandoutsModal,
    showHandoutOverlay
  };
})();
