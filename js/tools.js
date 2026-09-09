/**
 * SYSTÈME DE JEU RP — OUTILS INTERACTIFS (LIVRE IX)
 * Moteur logique : Fiche PJ, Atelier de Sorts (XP=PA), Lanceur de Dés & Critiques, Challenges
 */

document.addEventListener('DOMContentLoaded', () => {
    initCharacterSheet();
    initSpellWorkshop();
    initDiceRoller();
    initChallengeResolver();
    initClairsentienceSimulator();
});

// Toast notification helper
function showToast(msg) {
    let toast = document.getElementById('toolToast');
    if (!toast) {
        toast = document.createElement('div');
        toast.id = 'toolToast';
        toast.className = 'toast-msg';
        document.body.appendChild(toast);
    }
    toast.innerHTML = `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 14 14"/></svg> <span>${msg}</span>`;
    toast.classList.add('show');
    setTimeout(() => toast.classList.remove('show'), 2800);
}

/* ==========================================================================
   MODULE 1 : GÉNÉRATEUR DE FICHE DE PERSONNAGE DYNAMIQUE
   ========================================================================== */
function initCharacterSheet() {
    const raceSelect = document.getElementById('csRace');
    const profileSelect = document.getElementById('csProfile');
    const nameInput = document.getElementById('csName');

    const attrInputs = {
        for: document.getElementById('attrFor'),
        agi: document.getElementById('attrAgi'),
        con: document.getElementById('attrCon'),
        rap: document.getElementById('attrRap'),
        int: document.getElementById('attrInt'),
        eru: document.getElementById('attrEru'),
        cha: document.getElementById('attrCha'),
        ins: document.getElementById('attrIns'),
        mag: document.getElementById('attrMag')
    };

    const vitals = {
        pa: document.getElementById('vitalPa'),
        cap: document.getElementById('vitalCap'),
        max: document.getElementById('vitalMax'),
        ess: document.getElementById('vitalEss'),
        trainedCount: document.getElementById('vitalTrainedCount'),
        ptsLeft: document.getElementById('csPointsLeft')
    };

    if (!raceSelect || !attrInputs.for) return;

    // Racial modifiers table
    const raceModifiers = {
        humain: { for: 0, agi: 0, con: 0, rap: 0, int: 0, eru: 0, cha: 0, ins: 0, minSens: 3, note: 'Polyvalent, sens min 3' },
        drakka: { for: 1, agi: 0, con: 1, rap: 0, int: -1, eru: 0, cha: -1, ins: 0, minSens: 3, note: '+1 FOR, +1 CON, -1 INT, -1 CHA' },
        taurien: { for: 0, agi: 1, con: 0, rap: 1, int: 0, eru: 0, cha: 0, ins: 0, minSens: 3, note: '+1 AGI, +1 RAP, affinité Sylvestre' },
        cleien: { for: -1, agi: 0, con: -1, rap: 0, int: 1, eru: 1, cha: 0, ins: 1, minSens: 4, note: '-1 FOR, -1 CON, +1 INT, +1 ÉRU, +1 INS, sens min 4' },
        mikyai: { for: 1, agi: 0, con: 1, rap: 0, int: 0, eru: -1, cha: -1, ins: 0, minSens: 3, note: '+1 FOR, +1 CON, -1 ÉRU, -1 CHA, écailles dures' },
        vardien: { for: 0, agi: 0, con: 1, rap: 0, int: 0, eru: 0, cha: 0, ins: 1, minSens: 3, note: '+1 CON, +1 INS, survie des frontières' }
    };

    function updateCharacterCalculations() {
        const race = raceSelect.value;
        const profile = profileSelect.value;
        const mods = raceModifiers[race] || raceModifiers.humain;

        // Get base values
        const vals = {};
        let sumAttr = 0;
        for (const [k, el] of Object.entries(attrInputs)) {
            let base = parseInt(el.value) || 0;
            if (base < 0) base = 0;
            vals[k] = base;
            if (k !== 'mag') sumAttr += base;
        }

        // Apply racial display hint
        const raceNoteEl = document.getElementById('csRaceNote');
        if (raceNoteEl) raceNoteEl.textContent = mods.note;

        // Effective stats
        const eff = {
            for: Math.max(1, vals.for + mods.for),
            agi: Math.max(1, vals.agi + mods.agi),
            con: Math.max(1, vals.con + mods.con),
            rap: Math.max(1, vals.rap + mods.rap),
            int: Math.max(1, vals.int + mods.int),
            eru: Math.max(1, vals.eru + mods.eru),
            cha: Math.max(1, vals.cha + mods.cha),
            ins: Math.max(1, vals.ins + mods.ins),
            mag: vals.mag
        };

        // Min of 8 attributes
        const core8 = [eff.for, eff.agi, eff.con, eff.rap, eff.int, eff.eru, eff.cha, eff.ins];
        const minCore = Math.min(...core8);

        // PA Formula: Max(Agi, Int) + Rap + Min(Core)
        const paBase = Math.max(eff.agi, eff.int) + eff.rap + minCore;

        // Encaissement = Con * 2
        const capBase = eff.con * 2;

        // Lethal Max = Con * 5
        const maxBase = eff.con * 5;

        // Essoufflement = Con
        const essBase = eff.con;

        // Update dashboard
        if (vitals.pa) vitals.pa.textContent = paBase;
        if (vitals.cap) vitals.cap.textContent = capBase;
        if (vitals.max) vitals.max.textContent = maxBase;
        if (vitals.ess) vitals.ess.textContent = essBase + ' tours';
        if (vitals.trainedCount) vitals.trainedCount.textContent = eff.eru;

        // Budget points check
        let budgetTotal = 24; // Standard PJ baseline (1 at 5, 1 at 4, 15 secondary)
        if (profile === 'elite') budgetTotal = 25;
        if (profile === 'sbire') budgetTotal = 12;
        if (vals.mag > 0 && profile === 'hero') budgetTotal = 26; // +2 pts if magic awake

        const ptsUsed = sumAttr;
        const ptsRemaining = budgetTotal - ptsUsed;
        if (vitals.ptsLeft) {
            vitals.ptsLeft.textContent = `${ptsRemaining} pt(s) restants (Total alloué : ${ptsUsed}/${budgetTotal})`;
            if (ptsRemaining < 0) {
                vitals.ptsLeft.style.color = 'var(--vital-ruby)';
            } else if (ptsRemaining === 0) {
                vitals.ptsLeft.style.color = '#34d399';
            } else {
                vitals.ptsLeft.style.color = 'var(--temporal-amber)';
            }
        }

        // Limit trained skills check to ERU
        updateSkillsLimit(eff.eru);
    }

    function updateSkillsLimit(maxTrained) {
        const checkboxes = document.querySelectorAll('.skill-check:checked');
        const countSpan = document.getElementById('trainedSkillsChecked');
        if (countSpan) countSpan.textContent = `${checkboxes.length} / ${maxTrained}`;
        if (checkboxes.length > maxTrained && countSpan) {
            countSpan.style.color = 'var(--vital-ruby)';
        } else if (countSpan) {
            countSpan.style.color = 'var(--arcane-cyan)';
        }
    }

    // Bind listeners
    for (const el of Object.values(attrInputs)) {
        el.addEventListener('input', updateCharacterCalculations);
    }
    raceSelect.addEventListener('change', updateCharacterCalculations);
    profileSelect.addEventListener('change', updateCharacterCalculations);

    // Stepper buttons (+/-)
    document.querySelectorAll('.stepper-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            const targetId = btn.getAttribute('data-target');
            const step = parseInt(btn.getAttribute('data-step')) || 1;
            const input = document.getElementById(targetId);
            if (input) {
                let current = parseInt(input.value) || 0;
                let min = parseInt(input.getAttribute('min')) || 0;
                let max = parseInt(input.getAttribute('max')) || 10;
                let nextVal = Math.min(max, Math.max(min, current + step));
                input.value = nextVal;
                updateCharacterCalculations();
            }
        });
    });

    // Skill checkboxes
    document.querySelectorAll('.skill-check').forEach(cb => {
        cb.addEventListener('change', () => {
            const eru = parseInt(attrInputs.eru.value) || 1;
            const mods = raceModifiers[raceSelect.value] || raceModifiers.humain;
            updateSkillsLimit(Math.max(1, eru + mods.eru));
        });
    });

    // Action buttons
    const btnCopy = document.getElementById('csBtnCopy');
    const btnSave = document.getElementById('csBtnSave');
    const btnLoad = document.getElementById('csBtnLoad');
    const btnReset = document.getElementById('csBtnReset');

    if (btnCopy) {
        btnCopy.addEventListener('click', () => {
            const name = nameInput.value || 'Héros sans nom';
            const race = raceSelect.options[raceSelect.selectedIndex].text;
            const profile = profileSelect.options[profileSelect.selectedIndex].text;
            const pa = vitals.pa.textContent;
            const cap = vitals.cap.textContent;
            const max = vitals.max.textContent;
            const ess = vitals.ess.textContent;

            const trainedSkills = Array.from(document.querySelectorAll('.skill-check:checked'))
                .map(cb => cb.getAttribute('data-skill')).join(', ') || 'Aucune';

            const summary = `=== FICHE DE PERSONNAGE RP ===\n` +
                `Nom : ${name}\nEspèce : ${race}\nProfil : ${profile}\n\n` +
                `-- ATTRIBUTS BRUTS --\n` +
                `FOR: ${attrInputs.for.value} | AGI: ${attrInputs.agi.value} | CON: ${attrInputs.con.value} | RAP: ${attrInputs.rap.value}\n` +
                `INT: ${attrInputs.int.value} | ÉRU: ${attrInputs.eru.value} | CHA: ${attrInputs.cha.value} | INS: ${attrInputs.ins.value}\n` +
                `MAGIE: ${attrInputs.mag.value}\n\n` +
                `-- JAUGES VITALES & TACTIQUES --\n` +
                `Points d'Action (PA/tour) : ${pa}\n` +
                `Seuil d'Encaissement (CAP) : ${cap} blessures\n` +
                `Maximum Létal : ${max} blessures\n` +
                `Plafond d'Essoufflement : ${ess}\n\n` +
                `Compétences Entraînées (+1 niveau de dé) :\n${trainedSkills}\n` +
                `==============================`;

            navigator.clipboard.writeText(summary).then(() => {
                showToast('Fiche de personnage copiée dans le presse-papier !');
            });
        });
    }

    if (btnSave) {
        btnSave.addEventListener('click', () => {
            const data = {
                name: nameInput.value,
                race: raceSelect.value,
                profile: profileSelect.value,
                attributes: {}
            };
            for (const [k, el] of Object.entries(attrInputs)) {
                data.attributes[k] = el.value;
            }
            data.skills = Array.from(document.querySelectorAll('.skill-check:checked')).map(cb => cb.id);
            localStorage.setItem('rp_codex_character', JSON.stringify(data));
            showToast('Fiche sauvegardée dans le navigateur (localStorage) !');
        });
    }

    if (btnLoad) {
        btnLoad.addEventListener('click', () => {
            const raw = localStorage.getItem('rp_codex_character');
            if (!raw) {
                showToast('Aucune fiche sauvegardée trouvée.');
                return;
            }
            try {
                const data = JSON.parse(raw);
                if (data.name) nameInput.value = data.name;
                if (data.race) raceSelect.value = data.race;
                if (data.profile) profileSelect.value = data.profile;
                if (data.attributes) {
                    for (const [k, v] of Object.entries(data.attributes)) {
                        if (attrInputs[k]) attrInputs[k].value = v;
                    }
                }
                document.querySelectorAll('.skill-check').forEach(cb => {
                    cb.checked = (data.skills && data.skills.includes(cb.id));
                });
                updateCharacterCalculations();
                showToast('Fiche rechargée avec succès !');
            } catch (e) {
                showToast('Erreur lors du chargement des données.');
            }
        });
    }

    if (btnReset) {
        btnReset.addEventListener('click', () => {
            if (confirm('Réinitialiser la fiche de personnage aux valeurs par défaut ?')) {
                nameInput.value = 'Roger de Vardis';
                raceSelect.value = 'humain';
                profileSelect.value = 'hero';
                attrInputs.for.value = 3;
                attrInputs.agi.value = 3;
                attrInputs.con.value = 3;
                attrInputs.rap.value = 3;
                attrInputs.int.value = 3;
                attrInputs.eru.value = 3;
                attrInputs.cha.value = 2;
                attrInputs.ins.value = 2;
                attrInputs.mag.value = 0;
                document.querySelectorAll('.skill-check').forEach(cb => cb.checked = false);
                updateCharacterCalculations();
                showToast('Fiche réinitialisée.');
            }
        });
    }

    // Initialize first calculation
    updateCharacterCalculations();
}

/* ==========================================================================
   MODULE 2 : ATELIER DE CRÉATION DE SORTS MODULAIRE (1 XP = 1 PA)
   ========================================================================== */
function initSpellWorkshop() {
    const spellName = document.getElementById('spName');
    const spellDesc = document.getElementById('spDesc');
    const spellAttr = document.getElementById('spAttr');
    const effectInputs = document.querySelectorAll('.spell-effect-input');

    const previewName = document.getElementById('spCardName');
    const previewType = document.getElementById('spCardType');
    const previewDesc = document.getElementById('spCardDesc');
    const previewPa = document.getElementById('spCardPa');
    const previewXp = document.getElementById('spCardXp');
    const previewList = document.getElementById('spCardEffectsList');
    const btnCopySpell = document.getElementById('spBtnCopy');

    if (!spellName || !previewName) return;

    function updateSpellCalculations() {
        let totalEffectCost = 0;
        const categoriesUsed = new Set();
        const activeEffects = [];

        effectInputs.forEach(input => {
            let val = 0;
            if (input.type === 'checkbox') {
                if (input.checked) val = 1;
            } else {
                val = parseInt(input.value) || 0;
            }

            if (val > 0) {
                const costPerUnit = parseInt(input.getAttribute('data-cost')) || 1;
                const category = input.getAttribute('data-category') || 'offensif';
                const label = input.getAttribute('data-label') || 'Effet';
                const unitText = input.getAttribute('data-unit') || '';

                categoriesUsed.add(category);
                const cost = val * costPerUnit;
                totalEffectCost += cost;

                activeEffects.push({
                    text: `${label} : ${val > 1 ? val + ' ' + unitText : unitText || 'Actif'} (${cost} XP)`,
                    cost: cost
                });
            }
        });

        // Hybrid category surcharge: +1 XP per category beyond 1st
        const hybridSurcharge = Math.max(0, categoriesUsed.size - 1);
        const totalXp = totalEffectCost + hybridSurcharge;
        const activationPa = totalEffectCost; // Rule: hybrid surcharge doesn't count towards combat PA

        // Update previews
        previewName.textContent = spellName.value.trim() || 'Sortilège Modulaire';
        const catArray = Array.from(categoriesUsed).map(c => c.toUpperCase());
        const catStr = catArray.length > 0 ? catArray.join(' / ') : 'UTILITAIRE';
        previewType.textContent = `${catStr} — Source : ${spellAttr.value}`;
        previewDesc.textContent = spellDesc.value.trim() || 'Aucune description narrative spécifiée.';

        previewPa.textContent = `${activationPa} PA`;
        previewXp.textContent = `Coût : ${totalXp} XP (${hybridSurcharge > 0 ? '+' + hybridSurcharge + ' XP hybride inclus' : 'Pur'})`;

        previewList.innerHTML = '';
        if (activeEffects.length === 0) {
            previewList.innerHTML = '<li><em>Sélectionnez au moins un effet dans la matrice ci-dessus.</em></li>';
        } else {
            activeEffects.forEach(eff => {
                const li = document.createElement('li');
                li.innerHTML = eff.text;
                previewList.appendChild(li);
            });
            if (hybridSurcharge > 0) {
                const li = document.createElement('li');
                li.innerHTML = `<strong>Surcoût Hybride :</strong> +${hybridSurcharge} XP (technique multi-branches)`;
                previewList.appendChild(li);
            }
        }
    }

    // Bind listeners
    spellName.addEventListener('input', updateSpellCalculations);
    spellDesc.addEventListener('input', updateSpellCalculations);
    spellAttr.addEventListener('change', updateSpellCalculations);
    effectInputs.forEach(inp => inp.addEventListener('input', updateSpellCalculations));

    if (btnCopySpell) {
        btnCopySpell.addEventListener('click', () => {
            const cardText = `=== FICHE DE SORTILÈGE MODULAIRE ===\n` +
                `Nom : ${previewName.textContent}\n` +
                `Nature : ${previewType.textContent}\n` +
                `Coût d'Activation : ${previewPa.textContent} (Action en combat)\n` +
                `Coût d'Apprentissage : ${previewXp.textContent}\n\n` +
                `Description : ${previewDesc.textContent}\n\n` +
                `Effets Techniques :\n` +
                Array.from(previewList.querySelectorAll('li')).map(li => `- ${li.textContent}`).join('\n') +
                `\n====================================`;
            navigator.clipboard.writeText(cardText).then(() => {
                showToast('Carte de sortilège copiée dans le presse-papier !');
            });
        });
    }

    updateSpellCalculations();
}

/* ==========================================================================
   MODULE 3 : LANCEUR POLYÉDRIQUE & RÉSOLUTION CRITIQUE INSTANTANÉE
   ========================================================================== */
function initDiceRoller() {
    const dieSelect = document.getElementById('diceStepSelect');
    const paSlider = document.getElementById('dicePaBoost');
    const paValDisplay = document.getElementById('dicePaVal');
    const otherModInput = document.getElementById('diceOtherMod');
    const dcInput = document.getElementById('diceDc');
    const isOpposedCheck = document.getElementById('diceIsOpposed');
    const defRollInput = document.getElementById('diceDefRoll');
    const btnRoll = document.getElementById('btnRollDice');

    const dieCube = document.getElementById('diceCubeDisplay');
    const dieFaceVal = document.getElementById('dieFaceVal');
    const dieSubText = document.getElementById('dieSubText');
    const bannerTotal = document.getElementById('diceBannerTotal');
    const critCard = document.getElementById('critConsequenceCard');

    if (!dieSelect || !btnRoll) return;

    // Update PA display
    paSlider.addEventListener('input', () => {
        paValDisplay.textContent = `+${paSlider.value} PA`;
    });

    const opposedContainer = document.getElementById('opposedContainer');
    if (isOpposedCheck && opposedContainer) {
        const updateOpposedVis = () => {
            opposedContainer.style.display = isOpposedCheck.checked ? 'block' : 'none';
        };
        isOpposedCheck.addEventListener('change', updateOpposedVis);
        updateOpposedVis();
    }

    // Die definitions
    const dieSteps = {
        'd2': { count: 1, sides: 2, mod: 0, label: '1d2' },
        'd4-1': { count: 1, sides: 4, mod: -1, label: '1d4 - 1' },
        'd4': { count: 1, sides: 4, mod: 0, label: '1d4' },
        'd4+1': { count: 1, sides: 4, mod: 1, label: '1d4 + 1' },
        'd4+2': { count: 1, sides: 4, mod: 2, label: '1d4 + 2' },
        'd4+3': { count: 1, sides: 4, mod: 3, label: '1d4 + 3' },
        'd6': { count: 1, sides: 6, mod: 0, label: '1d6' },
        'd6+1': { count: 1, sides: 6, mod: 1, label: '1d6 + 1' },
        'd6+2': { count: 1, sides: 6, mod: 2, label: '1d6 + 2' },
        'd8': { count: 1, sides: 8, mod: 0, label: '1d8' },
        'd8+2': { count: 1, sides: 8, mod: 2, label: '1d8 + 2' },
        'd10': { count: 1, sides: 10, mod: 0, label: '1d10' },
        'd12': { count: 1, sides: 12, mod: 0, label: '1d12' },
        'd12+2': { count: 1, sides: 12, mod: 2, label: '1d12 + 2' },
        '2d8': { count: 2, sides: 8, mod: 0, label: '2d8' },
        '2d10': { count: 2, sides: 10, mod: 0, label: '2d10' },
        '2d12': { count: 2, sides: 12, mod: 0, label: '2d12' },
        '2d12+10': { count: 2, sides: 12, mod: 10, label: '2d12 + 10' }
    };

    // Critical consequences lookup from Livre II Chapitre 8
    const critTable = {
        1: { off: 'La cible tombe à terre', def: 'La cible tombe à terre', soc: 'Interlocuteur déstabilisé', util: 'Tâche accomplie sans bruit' },
        2: { off: 'Cible repoussée d\'1 case', def: '+1 EC bonus pour la cible', soc: 'Ascendant psychologique', util: 'Gain d\'élan cinétique' },
        3: { off: 'Avantage contre la cible', def: 'Défense impénétrable', soc: 'Confiance immédiate', util: 'Précision chirurgicale' },
        4: { off: '+1 EC pour l\'attaquant', def: '+1 EC pour l\'attaquant', soc: '+1 EC en persuasion', util: '+1 EC pour la tâche' },
        5: { off: '+2 Dégâts directs', def: '+2 Défense bonus', soc: 'L\'interlocuteur cède', util: 'Économie de matériel' },
        6: { off: 'Cible étourdie (-1 EC)', def: 'Cible étourdie en retour', soc: 'Interlocuteur sidéré', util: 'Découverte inattendue' },
        7: { off: 'Cible étourdie (-1 EC)', def: 'Défense magistrale', soc: 'Sympathie spontanée', util: 'Temps divisé par deux' },
        8: { off: 'Récupère 1 effet', def: 'Récupère 1 effet', soc: 'Récupère 1 effet', util: 'Récupère 1 effet' },
        9: { off: 'Récupère 1 Essoufflement', def: 'Récupère 1 Essoufflement', soc: 'Regain de sang-froid', util: 'Regain de vigueur' },
        10: { off: 'Cible ralentie (PA doublés)', def: 'Attaquant ralenti', soc: 'Opposition neutralisée', util: 'Ralentissement temporel' },
        11: { off: 'Désarmement immédiat', def: 'Arme ennemie déviée', soc: 'Éblouissement oratoire', util: 'Solution ingénieuse' },
        12: { off: 'Cible déstabilisée (-2 EC)', def: 'Attaquant déstabilisé', soc: 'Effondrement de l\'argument', util: 'Composant réparé/sauvé' },
        13: { off: '+3 Dégâts directs', def: '+3 Défense bonus', soc: 'Ascendant total', util: 'Déclenchement instantané' },
        14: { off: 'Perte de 2 PA cible', def: 'Gain de 2 PA esquive', soc: 'Silence imposé', util: 'Optimisation parfaite' },
        15: { off: 'Cible immobilisée', def: 'Contre-prise totale', soc: 'Pacte scellé', util: 'Percée structurelle' },
        16: { off: 'Récupère 2 PA', def: 'Récupère 2 PA', soc: 'Récupère 2 PA', util: 'Récupère 2 PA' },
        17: { off: '+4 Dégâts critiques', def: '+4 Riposte immédiate', soc: 'Ferveur communicative', util: 'Analyse télépathique' },
        18: { off: 'Cible aveuglée 1 tour', def: 'Esquive mirifique', soc: 'Adhésion fanatique', util: 'Synchronicité parfaite' },
        19: { off: 'Armure ennemie fissurée', def: 'Absorption complète', soc: 'Vérité absolue révélée', util: 'Surfréquence quantique' },
        20: { off: '3 Dégâts Résiduels continus', def: 'Contre-attaque automatique', soc: 'Ralliement inconditionnel', util: 'Survoltage arcanique total' },
        21: { off: 'Cible paralysée 1 tour', def: 'Inversion cinétique', soc: 'Hypnose involontaire', util: 'Téléportation réflexe' },
        22: { off: '+6 Dégâts perforants', def: 'Bouclier impénétrable', soc: 'Autorité cosmique', util: 'Remontée chronale' },
        23: { off: 'Destruction d\'équipement', def: 'Arme ennemie brisée', soc: 'Capitulation spontanée', util: 'Surcharge du réacteur' },
        24: { off: 'Cible Inconsciente', def: 'Désarçonnement fatal ennemi', soc: 'Soumission absolue', util: 'Succès mirifique critique' }
    };

    btnRoll.addEventListener('click', () => {
        const stepKey = dieSelect.value;
        const cfg = dieSteps[stepKey] || dieSteps['d6'];
        const paBonus = parseInt(paSlider.value) || 0;
        const otherMod = parseInt(otherModInput.value) || 0;
        const targetDc = parseInt(dcInput.value) || 0;
        const isOpposed = isOpposedCheck.checked;
        const defScore = isOpposed ? (parseInt(defRollInput.value) || 0) : targetDc;

        // Visual rolling animation
        dieCube.classList.add('rolling');
        btnRoll.disabled = true;

        let rollTicks = 0;
        const maxTicks = 10;
        const interval = setInterval(() => {
            dieFaceVal.textContent = Math.floor(Math.random() * cfg.sides) + 1;
            rollTicks++;
            if (rollTicks >= maxTicks) {
                clearInterval(interval);
                finalizeRoll();
            }
        }, 40);

        function finalizeRoll() {
            dieCube.classList.remove('rolling');
            btnRoll.disabled = false;

            // Generate real roll
            let rolls = [];
            let isCritSuccess = false;
            let isCritFail = false;

            for (let i = 0; i < cfg.count; i++) {
                let r = Math.floor(Math.random() * cfg.sides) + 1;
                rolls.push(r);
                if (r === cfg.sides) isCritSuccess = true;
                if (r === 1 && cfg.count === 1) isCritFail = true;
            }

            const rawDiceSum = rolls.reduce((a, b) => a + b, 0);
            const totalScore = rawDiceSum + cfg.mod + paBonus + otherMod;
            const diff = totalScore - defScore;

            dieFaceVal.textContent = totalScore;
            dieSubText.textContent = `Dés [${rolls.join(', ')}] ${cfg.mod !== 0 ? (cfg.mod > 0 ? '+' + cfg.mod : cfg.mod) : ''} + ${paBonus} PA + ${otherMod} Mod`;

            // Reset banner classes
            bannerTotal.className = 'dice-total-banner';
            critCard.classList.remove('visible');

            if (isCritFail) {
                bannerTotal.textContent = `ÉCHEC CRITIQUE ! (Total : ${totalScore} vs ND ${defScore} — Diff : ${diff})`;
                bannerTotal.classList.add('fail');
                showToast('Échec critique détecté (1 naturel sur le dé) ! Pénalité de tour.');
            } else if (isCritSuccess) {
                // Determine consequence roll (roll second die of same calibre up to d24)
                let consequenceRoll = Math.floor(Math.random() * Math.min(24, cfg.sides * cfg.count)) + 1;
                bannerTotal.textContent = `RÉUSSITE CRITIQUE ! (Total : ${totalScore} vs ND ${defScore} — Diff : +${diff})`;
                bannerTotal.classList.add('crit-success');

                // Reveal consequence card
                const effect = critTable[consequenceRoll] || critTable[1];
                document.getElementById('critRollVal').textContent = `Jet de Conséquence : ${consequenceRoll}`;
                document.getElementById('critOffText').textContent = effect.off;
                document.getElementById('critDefText').textContent = effect.def;
                document.getElementById('critSocText').textContent = effect.soc;
                document.getElementById('critUtilText').textContent = effect.util;
                critCard.classList.add('visible');
                showToast(`Réussite critique ! Conséquence n°${consequenceRoll} déclenchée.`);
            } else if (diff >= 0) {
                if (isOpposed && diff === 0) {
                    // Differential 0 in opposed combat: deviance hit location
                    const devianceLocations = ['Tête / Capteur', 'Bras droit', 'Bras gauche', 'Torse / Plastron', 'Jambe droite', 'Jambe gauche'];
                    const loc = devianceLocations[Math.floor(Math.random() * devianceLocations.length)];
                    bannerTotal.textContent = `SUCCÈS DE JUSTESSE (Diff : 0) — Impact dévié : ${loc}`;
                    bannerTotal.classList.add('success');
                } else {
                    bannerTotal.textContent = `RÉUSSITE (Total : ${totalScore} vs ND ${defScore} — Diff : +${diff})`;
                    bannerTotal.classList.add('success');
                }
            } else {
                bannerTotal.textContent = `ÉCHEC (Total : ${totalScore} vs ND ${defScore} — Diff : ${diff})`;
                bannerTotal.classList.add('fail');
            }
        }
    });
}

/* ==========================================================================
   MODULE 4 : RÉSOLVEUR DE CHALLENGES & ÉPREUVES PROLONGÉES
   ========================================================================== */
function initChallengeResolver() {
    const stepsContainer = document.getElementById('challengeStepsContainer');
    const stepCountSelect = document.getElementById('challengeStepCount');
    const rewardBadge = document.getElementById('challengeRewardDisplay');
    const statusDisplay = document.getElementById('challengeStatusDisplay');
    const btnResetChallenge = document.getElementById('btnResetChallenge');

    if (!stepsContainer || !stepCountSelect) return;

    let challengeData = [];

    function renderSteps() {
        const count = parseInt(stepCountSelect.value) || 4;
        stepsContainer.innerHTML = '';
        challengeData = [];

        for (let i = 1; i <= count; i++) {
            const stepObj = { id: i, status: 'pending', retries: 0 };
            challengeData.push(stepObj);

            const row = document.createElement('div');
            row.className = 'challenge-step-row';
            row.id = `challengeStepRow_${i}`;
            row.innerHTML = `
                <div class="step-info">
                    <div class="step-index-pill">${i}</div>
                    <div class="step-details">
                        <h5>Étape ${i} : Épreuve Tactique</h5>
                        <span id="stepMeta_${i}">Statut : En attente | Retries : 0</span>
                    </div>
                </div>
                <div class="step-actions">
                    <button class="step-btn" data-action="success" data-step="${i}">Réussite</button>
                    <button class="step-btn" data-action="fail" data-step="${i}">Échec</button>
                    <button class="step-btn" data-action="retry" data-step="${i}">+1 Retry</button>
                </div>
            `;
            stepsContainer.appendChild(row);
        }

        bindStepActions();
        calculateReward();
    }

    function bindStepActions() {
        stepsContainer.querySelectorAll('.step-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const stepIdx = parseInt(btn.getAttribute('data-step')) - 1;
                const action = btn.getAttribute('data-action');
                const step = challengeData[stepIdx];
                const row = document.getElementById(`challengeStepRow_${step.id}`);
                const meta = document.getElementById(`stepMeta_${step.id}`);

                if (action === 'success') {
                    step.status = 'success';
                    row.className = 'challenge-step-row success';
                } else if (action === 'fail') {
                    step.status = 'failed';
                    row.className = 'challenge-step-row failed';
                } else if (action === 'retry') {
                    step.retries++;
                }

                meta.textContent = `Statut : ${step.status.toUpperCase()} | Retries : ${step.retries}`;
                calculateReward();
            });
        });
    }

    function calculateReward() {
        let successes = 0;
        let totalRetries = 0;
        let failures = 0;
        let pending = 0;

        challengeData.forEach(s => {
            if (s.status === 'success') successes++;
            if (s.status === 'failed') failures++;
            if (s.status === 'pending') pending++;
            totalRetries += s.retries;
        });

        // Official Formula: Reward = (Successes * 2) - Retries, min 2 if at least 1 success
        let reward = (successes * 2) - totalRetries;
        if (successes > 0 && reward < 2) reward = 2;
        if (successes === 0) reward = 0;

        rewardBadge.textContent = `${reward} case(s) de progression / XP`;

        if (pending > 0) {
            statusDisplay.textContent = `Défi en cours (${successes}/${challengeData.length} validées, ${pending} restantes)`;
            statusDisplay.style.color = 'var(--text-secondary)';
        } else if (failures === 0) {
            statusDisplay.textContent = `CHALLENGE ACCOMPLI AVEC BRIO ! Récompense maximale accordée.`;
            statusDisplay.style.color = '#34d399';
        } else if (successes >= Math.ceil(challengeData.length / 2)) {
            statusDisplay.textContent = `CHALLENGE VALIDÉ SUR LE FIL (${failures} échec(s) encaissé(s)).`;
            statusDisplay.style.color = 'var(--temporal-amber)';
        } else {
            statusDisplay.textContent = `ÉCHEC DU DÉFI — Trop d'entraves subies. Conséquences narratives requises.`;
            statusDisplay.style.color = 'var(--vital-ruby)';
        }
    }

    stepCountSelect.addEventListener('change', renderSteps);
    if (btnResetChallenge) btnResetChallenge.addEventListener('click', renderSteps);

    renderSteps();
}

/* ==========================================================================
   MODULE 5 : SIMULATEUR CHRONOTRAMES & CLAIRSENTIENCE
   ========================================================================== */
function initClairsentienceSimulator() {
    const tierSelect = document.getElementById('chronoTierSelect');
    const paDisplay = document.getElementById('chronoPaCost');
    const horizonDisplay = document.getElementById('chronoHorizon');
    const bottleneckDisplay = document.getElementById('chronoBottleneck');
    const paradoxDisplay = document.getElementById('chronoParadoxRisk');

    if (!tierSelect || !paDisplay) return;

    const tiers = {
        1: { horizon: 'Quelques Secondes (Combat immédiat)', pa: '2 PA', bottleneck: 'Négligeable (Flux local stable)', risk: '0% — Dérive quasi impossible' },
        2: { horizon: '1 à 10 Minutes', pa: '4 PA', bottleneck: 'Très faible (Inflexions mineures)', risk: '5% — Micro-échos de causalité' },
        3: { horizon: '1 à 24 Heures', pa: '6 PA', bottleneck: 'Modéré (Présence de nœuds décisionnels)', risk: '15% — Risque de décalage chronal' },
        4: { horizon: '1 Semaine à 1 Mois', pa: '8 PA', bottleneck: 'Élevé (Goulots d\'étranglement identifiés)', risk: '30% — Effet papillon inversé actif' },
        5: { horizon: '1 An à 1 Décennie', pa: '12 PA', bottleneck: 'Critique (Convergences historiques rigides)', risk: '50% — Forte résistance du fleuve temporel' },
        6: { horizon: '1 Siècle (Ères planétaires)', pa: '16 PA', bottleneck: 'Majeur (Attracteurs universels)', risk: '75% — Altération d\'entités et lignées' },
        7: { horizon: '1 Millénaire', pa: '20 PA', bottleneck: 'Goulot Cosmique Infranchissable', risk: '90% — Rupture de chronotrame locale' },
        8: { horizon: 'Temps Géologique & Origines (Cosmos)', pa: '25+ PA', bottleneck: 'Singularité / Démiurge', risk: '99% — Paradoxe existentiel instantané' }
    };

    tierSelect.addEventListener('change', () => {
        const t = tiers[tierSelect.value] || tiers[1];
        paDisplay.textContent = t.pa;
        horizonDisplay.textContent = t.horizon;
        bottleneckDisplay.textContent = t.bottleneck;
        paradoxDisplay.textContent = t.risk;
    });
}
