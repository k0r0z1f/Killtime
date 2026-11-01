/**
 * SYSTÈME DE JEU RP — OUTILS INTERACTIFS (LIVRE IX)
 * Moteur logique : Fiche PJ, Atelier de Sorts (XP=PA), Lanceur de Dés & Critiques, Challenges
 */

let __toolsBooted = false;

// Source unique (RP-074/082) : les data/livre_N.json priment ; en cas
// d'échec de chargement (fichier local, hors-ligne), chaque module
// garde ses miroirs intégrés en repli, sans changer de comportement.
const CodexData = { l1: null, l2: null, l4: null, l7: null, l10: null };
window.CodexData = CodexData;

async function loadCodexData() {
    const urls = { l1: 'data/livre_1.json', l2: 'data/livre_2.json', l4: 'data/livre_4.json', l7: 'data/livre_7.json', l10: 'data/livre_10.json' };
    await Promise.all(Object.keys(urls).map(async (k) => {
        try {
            const r = await fetch(urls[k], { cache: 'no-store' });
            if (r.ok) CodexData[k] = await r.json();
        } catch (e) { /* repli miroirs intégrés */ }
    }));
}

function stripHtml(s) {
    const d = document.createElement('div');
    d.innerHTML = s || '';
    return (d.textContent || '').replace(/\s+/g, ' ').trim();
}

// 'd2' -> '1d2', '2d6' inchangé : forme canon des paliers (Livre II §6.1).
function normDe(s) {
    s = String(s || '').replace(/\s+/g, '');
    return /^\d/.test(s) ? s : '1' + s;
}

async function bootTools() {
    if (__toolsBooted) return;
    __toolsBooted = true;
    try { await loadCodexData(); } catch (e) { /* repli miroirs intégrés */ }
    initCharacterSheet();
    initSpellWorkshop();
    initDiceRoller();
    initChallengeResolver();
    initClairsentienceSimulator();
}
window.bootTools = bootTools;

document.addEventListener('DOMContentLoaded', () => {
    // Livre IX : contenu rendu en async par js/livre.js -> boot après rendu.
    // Page statique (fiche_pj) : les outils sont déjà dans le DOM.
    if (document.getElementById('lxContenu')) return;
    bootTools();
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
    const ageInput = document.getElementById('csAge');
    const sexeSelect = document.getElementById('csSexe');
    const mondeInput = document.getElementById('csMonde');
    const giftSelect = document.getElementById('csGift');
    const pillarMajorSelect = document.getElementById('csPillarMajor');
    const pillarMinorSelect = document.getElementById('csPillarMinor');
    const pillarsRow = document.getElementById('csPillarsRow');
    const enfanceSelect = document.getElementById('csEnfance');
    const metierSelect = document.getElementById('csMetier');
    const evenementSelect = document.getElementById('csEvenement');
    const ancrageInput = document.getElementById('csAncrage');
    const visionInput = document.getElementById('csVision');
    const ouieInput = document.getElementById('csOuie');

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

    // DONNÉES CANON (Livre I §2.2) : aucune espèce ne modifie les attributs.
    // Chaque espèce donne : sens min, affinité Magie et 1 DON RACIAL choisi parmi 5.
    const RACE_DATA = {
        humain: { label: 'Humain', sensMin: 3, magie: 'Impossible à Limitée', note: 'Polyvalent, sens min 3',
            gifts: ['Endurance Coloniale', 'Touche-à-tout', 'Instinct de Survie', 'Réseau Colonial', 'Sang-Froid'],
            giftNotes: ['+1 Essoufflement max ; ignore les malus climatiques légers.', '+1 entraînement gratuit à la création (compétence non-magique).', '+2 Nature & Survie ; +1 Observation.', '+2 Communication (négocier, interroger) en zone civilisée ; 1 contact par station.', '1×/session : annule 1 Essoufflement ou ignore un échec de moral/peur.'] },
        derek: { label: 'Derek', sensMin: 3, magie: 'Limitée', note: 'Robuste des profondeurs, sens min 3',
            gifts: ['Ossature Dense', 'Sang des Profondeurs', 'Maître-Fondeur', 'Sens de la Pierre', 'Gueule de Granit'],
            giftNotes: ['+2 Encaissement ; -1 dégâts contondants et chutes.', 'Vision nocturne ; +2 Constitution vs froid, -2 dégâts de froid ; ébloui 1 tour en entrant dans une lumière vive.', '+2 Forger (Artisanat) ; armure forgée par soi +1 protection.', 'Sent le danger de la roche à ~30 m (gaz, éboulement, poches) ; +2 Nature & Survie (orientation) en souterrain.', '+2 Constitution vs gaz et poisons des mines ; apnée 4× CON tours.'] },
        taurien: { label: 'Taurien', sensMin: 3, magie: 'Normale', note: 'Sylvestre, affinité élémentaire, sens min 3',
            gifts: ['Accord Sylvestre', 'Grâce Féline', 'Sens des Flux', 'Pas Feuillu', 'Sève Vive'],
            giftNotes: ['-1 PA et +1 Magie Élémentale (terre/eau/vent/feu).', '+1 esquive en milieu naturel ; -2 dégâts de chute.', 'Sent eau/vie/magie à ~50 m (Observation) ; +2 Observation en nature, -2 en milieu pollué/artificiel.', '+2 discrétion en nature ; ignore les terrains difficiles naturels.', '1×/jour au contact du sol naturel : purge 1 Essoufflement ou 1 résiduel de 2 ou moins.'] },
        cleien: { label: 'Cléien', sensMin: 4, magie: 'Inhérente', note: 'Hypersensible arcanique, sens min 4',
            gifts: ['Sang-Chaud de Cléia', 'Résonance Arcanique', 'Sensibilité à fleur de peau', 'Frugalité Cleyane', 'Silhouette Mineure'],
            giftNotes: ['-2 dégâts feu/chaleur ; -1 Encaissement, +1 dégât de froid subi.', '+1 canalisation ; progression Magie +1 case ; critique magique = +1 Essoufflement.', '+2 détection/lecture émotionnelle ; -2 vs flash/bang.', 'Besoins eau/nourriture ÷ 2 ; +2 CON vs privation.', '+1 discrétion/esquive vs taille humaine et plus ; -1 dégât au corps-à-corps.'] },
        mikyai: { label: 'Mikyai', sensMin: 3, magie: 'Impossible à Limitée', note: 'Écailles renforcées, sens min 3',
            gifts: ['Écailles de Karkjiue', 'Régénération Karkjiue', 'Technicien-Guerrier', 'Armes Naturelles', 'Queue Préhensile'],
            giftNotes: ['+1 Armure naturelle vs tranchant/perçant ; +2 Encaissement ; +1 dégât froid subi.', '1×/combat : un tick résiduel saute 1 cran ; stoppe un Saignement léger sans soin.', '+2 armes lourdes/réparation impériale ; ignore 1 pt d\u2019encombrement d\u2019armure.', 'Griffes/crocs (2 dégâts, jamais désarmé) ; +1 escalade.', '3e main utilitaire (pas d\u2019attaque) ; +1 escalade/natation.'] },
        vardien: { label: 'Vardien / Frontalier', sensMin: 3, magie: 'Variable', note: 'Volonté d\u2019acier des bordures, sens min 3',
            gifts: ['Trempe des Bordures', 'Gueule des Mines', 'Système Récup', 'Ne Lâche Rien', 'Contrebandier'],
            giftNotes: ['+2 Volonté/moral/peur/intimidation ; +1 Essoufflement max.', '+2 CON immunitaire (toxines, gaz, radiations faibles).', '+2 bricolage/récup/sabotage ; réparations à moitié prix.', '1×/session : à 0 PV sur un coup ≤ Encaissement, reste conscient 1 tour sans Dernier Souffle.', '+2 discrétion/falsification/douanes ; 1 contact marché noir par zone.'] }
    };
    // Alias de compatibilité (anciennes sauvegardes 'nain' = Derek canon).
    const RACE_ALIASES = { nain: 'derek' };
    function resolveRace(r) { return RACE_DATA[r] ? r : (RACE_ALIASES[r] || 'humain'); }

    const ATTR_LABELS = { for: 'Force (FOR)', agi: 'Agilité (AGI)', con: 'Constitution (CON)', rap: 'Rapidité (RAP)', int: 'Intelligence (INT)', eru: 'Érudition (ÉRU)', cha: 'Charisme (CHA)', ins: 'Instinct (INS)', mag: 'Magie' };
    const CORE8 = ['for', 'agi', 'con', 'rap', 'int', 'eru', 'cha', 'ins'];

    // Tables d'historique (Livre VII, chap. 28-30) — libellés courts pour le créateur.
    const ENFANCES = [{n:'Soutes & Docks',d:'Enfant de station : +2 Subterfuge Manuel en milieu confiné.'},{n:'Rues & Bas-fonds',d:'Gamin des toits : +2 Observation en urbain.'},{n:'Ferme & Frontière',d:'Enfant des confins : +2 Nature & Survie en rural/sauvage.'},{n:'Clan & Tribu',d:'Sang des anciens : +2 Intuition en groupe.'},{n:'Cité & École',d:'Enfant instruit : +2 Académie (1 domaine).'},{n:'Caserne & Ordre',d:'Enfant de troupe : +2 Athlétisme.'}];
    const METIERS = [{n:'Milicien',d:'+2 Tir (1 arme) & Observation ; armure légère ; rappelable.'},{n:'Médic',d:'+2 Premiers Soins & triage ; kit de soin ; serment du blessé.'},{n:'Mécano',d:'+2 Ingénierie & dépannage ; caisse à outils ; dette atelier.'},{n:'Pilote',d:'+2 Pilotage & navigation ; licence + contact ; crédit nef.'},{n:'Prospecteur',d:'+2 Survie extrême & endurance ; équipement ; exclusivité.'},{n:'Négociant',d:'+2 Négocier & évaluer ; contact + licence ; dette.'},{n:'Archiviste',d:'+2 Académie & détails ; archives ; censure.'},{n:'Artisan',d:'+2 Artisanat & entretien ; établi ; commande en cours.'},{n:'Chasseur',d:'+2 Traque & discrétion (nature) ; pièges ; quota.'},{n:'Infiltré',d:'+2 Discrétion & tromper ; faux papiers + planque ; service dû.'},{n:'Herboriste',d:'+2 Botanique & pharmacopée ; remèdes ; clientèle dangereuse.'},{n:'Officier',d:'+2 Leadership & intimidation ; insigne ; responsabilité.'}];
    const EVENEMENTS = [{n:'Frôlé la mort',d:'1×/session : annule 1 Essoufflement (coup > Encaissement) ; cauchemars.'},{n:'Dette de vie',d:'+2 pour protéger un allié ; créancier rappelable.'},{n:'Trahison subie',d:'Relance un jet intuition ; traître actif.'},{n:'Long exil',d:'+2 Survie en terrain inconnu ; −2 premier contact.'},{n:'Contact 5e Force',d:'1 question fermée au MJ ; repérable par sens magiques.'},{n:'Miracle inexpliqué',d:'Ignore 1 échec moral/peur ; un culte s’intéresse à toi.'},{n:'Guerre intérieure',d:'+2 sang-froid (violence) ; fracas = test INS ou +1 Essoufflement.'},{n:'Crime impuni',d:'+2 couvrir ses traces ; un témoin existe.'},{n:'Mentor perdu',d:'+2 domaine du mentor ; promesse à honorer.'},{n:'Écho temporel',d:'1 question fermée (danger) ; +1 Essoufflement/usage.'},{n:'Ruine & misère',d:'+2 improviser avec rien ; −2 contact huppé.'},{n:'Gloire locale',d:'+2 Communication (zone exploit) ; rival jaloux.'}];

    // Échelle Livre II §6.1 : valeur d'attribut -> dé sans entraînement.
    // Source : data/livre_2.json (crans_base), repli seuils codés.
    function dieForValue(v) {
        const M = CodexData.l2 && CodexData.l2.mecaniques;
        if (M && M.crans_base) {
            for (const r of M.crans_base) {
                if (v <= r.tranche[1]) return normDe(r.de_sans_entrainement);
            }
            const last = M.crans_base[M.crans_base.length - 1];
            return normDe(last.de_sans_entrainement);
        }
        if (v <= 2) return '1d2'; if (v <= 4) return '1d4'; if (v <= 6) return '1d6'; if (v <= 8) return '1d8'; return '1d10';
    }

    // Palier d'entraînement (Livre I Étape 5) : +1 palier = +1 cran sur l'échelle, plafond 1d12.
    const DIE_LADDER = ['1d2', '1d4', '1d6', '1d8', '1d10', '1d12'];
    function stepDie(die, steps) {
        let i = DIE_LADDER.indexOf(die);
        if (i < 0) i = 0;
        return DIE_LADDER[Math.min(DIE_LADDER.length - 1, i + Math.max(0, steps))];
    }

    // Surcouche Codex JSON (RP-074/082) : écrase les miroirs par le canon
    // quand les JSON ont chargé (gardes de cardinalité : sinon repli).
    (function overlayCodexData() {
        const L1 = CodexData.l1, L7 = CodexData.l7, L2 = CodexData.l2;
        if (L1 && L1.mecaniques && L1.mecaniques.especes_par_nom) {
            const SLUGS = { 'Humain': 'humain', 'Derek': 'derek', 'Taurien': 'taurien', 'Cléien': 'cleien', 'Mikyai': 'mikyai', 'Vardien / Frontaliers': 'vardien' };
            const raw = {};
            (L1.especes || []).forEach(s => { raw[s.nom] = s; });
            Object.keys(SLUGS).forEach(nom => {
                const slug = SLUGS[nom], src = raw[nom];
                if (!src || !RACE_DATA[slug] || !(src.dons || []).length) return;
                RACE_DATA[slug] = {
                    label: src.nom, sensMin: src.sens_min, magie: src.magie,
                    note: (src.origine || '') + ', sens min ' + src.sens_min,
                    gifts: src.dons.map(g => g.nom),
                    giftNotes: src.dons.map(g => stripHtml(g.effet || ''))
                };
            });
        }
        const M7 = L7 && L7.mecaniques;
        if (M7) {
            if (M7.enfances && M7.enfances.length === 6) {
                ENFANCES.length = 0;
                M7.enfances.forEach(e => ENFANCES.push({ n: String(e.nom).split(' — ')[0], d: e.texte }));
            }
            if (M7.metiers && M7.metiers.length === 12) {
                METIERS.length = 0;
                M7.metiers.forEach(m => METIERS.push({ n: m.metier, d: m.bonus + ' ; ' + m.atout + (m.attache ? ' ; ' + m.attache : '') }));
            }
            if (M7.evenements && M7.evenements.length === 12) {
                EVENEMENTS.length = 0;
                M7.evenements.forEach(e => EVENEMENTS.push({ n: e.evenement, d: e.effet + ' ; ' + e.sequelle }));
            }
        }
        if (L2 && L2.echelle_des) {
            DIE_LADDER.length = 0;
            L2.echelle_des.forEach(dd => DIE_LADDER.push(normDe(dd)));
        }
    })();

    // Options d'espèces reconstruites depuis RACE_DATA (wordings unifiés
    // page/créateur, les deux pages) en préservant le choix courant/migré.
    (function rebuildRaceOptions() {
        if (!raceSelect) return;
        const cur = resolveRace(raceSelect.value || 'humain');
        raceSelect.innerHTML = '';
        Object.keys(RACE_DATA).forEach(k => {
            const opt = document.createElement('option');
            opt.value = k;
            opt.textContent = RACE_DATA[k].label + ' (' + RACE_DATA[k].note + ')';
            raceSelect.appendChild(opt);
        });
        raceSelect.value = cur;
    })();
    // à gauche du stepper, mise à jour en temps réel avec la valeur.
    function ensureAttrDiceBadges() {
        for (const [k, el] of Object.entries(attrInputs)) {
            if (!el) continue;
            const group = el.closest('.tool-form-group');
            if (!group) continue;
            let badge = group.querySelector('[data-attr-die="' + k + '"]');
            let wrap = badge ? badge.closest('.attr-die-wrap') : null;
            if (!badge) {
                wrap = document.createElement('div');
                wrap.className = 'attr-die-wrap';
                badge = document.createElement('span');
                badge.className = 'pill-badge ' + (k === 'mag' ? 'amber' : 'cyan') + ' attr-die';
                badge.setAttribute('data-attr-die', k);
                badge.title = 'Dé sans entraînement (Livre II §6.1)';
                badge.textContent = '—';
                wrap.appendChild(badge);
            }
            if (badge.closest('.attr-row')) continue;
            const stepper = group.querySelector('.stepper-box');
            if (!stepper) { group.appendChild(wrap); continue; }
            let row = group.querySelector('.attr-row');
            if (!row) {
                row = document.createElement('div');
                row.className = 'attr-row';
                stepper.before(row);
            }
            if (stepper.parentElement !== row) row.appendChild(stepper);
            if (wrap.parentElement !== row) row.appendChild(wrap);
            row.insertBefore(wrap, stepper);
        }
    }
    function refreshAttrDiceBadges(eff) {
        document.querySelectorAll('.attr-die').forEach(el => {
            const k = el.getAttribute('data-attr-die');
            if (!k || eff[k] === undefined) return;
            if (k === 'mag') {
                el.textContent = eff.mag >= 5 ? 'Éveillé' : (eff.mag > 0 ? 'Éveil partiel' : 'Dormante');
            } else {
                el.textContent = dieForValue(eff[k]);
            }
        });
        // Compat : supprime l'ancienne rangée de dés séparée si un vieux HTML la contient encore.
        const legacy = document.getElementById('csDiceRow');
        if (legacy) legacy.remove();
    }

    // Niveau 0-3 par compétence (3 cases) : { 'Nom compétence': niveau }.
    function getSkillLevels() {
        const levels = {};
        document.querySelectorAll('.skill-check').forEach(cb => {
            if (cb.checked) {
                const k = cb.getAttribute('data-skill') || cb.id;
                levels[k] = (levels[k] || 0) + 1;
            }
        });
        return levels;
    }

    // Score de base canon d'une compétence (Livre III §10.1, calcul_rang_base) :
    // moyenne de ses attributs associés, arrondie vers le haut ; un seul
    // attribut = sa valeur. 'vision'/'ouie' = sens 1d6 (Observation, Écoute, Ballistique).
    function skillBaseScore(attrList, ext) {
        const nums = attrList.map(k => ext[k]).filter(v => typeof v === 'number' && !isNaN(v));
        if (!nums.length) return 0;
        return Math.ceil(nums.reduce((a, b) => a + b, 0) / nums.length);
    }

    // Infos canon d'une compétence depuis sa première case (data-attrs).
    function skillBaseInfo(skillName) {
        const el = document.querySelector('.skill-check[data-skill="' + skillName + '"]');
        if (!el) return { attrs: [], mag: false };
        const raw = el.getAttribute('data-attrs') || el.getAttribute('data-attr') || '';
        return {
            attrs: raw.split(',').map(s => s.trim().toLowerCase()).filter(Boolean),
            mag: el.hasAttribute('data-mag')
        };
    }

    // Badges de dés par compétence (créateur) : dé de base → dé entraîné.
    function refreshSkillDice(eff) {
        const ext = { ...eff };
        ext.vision = parseInt(visionInput.value) || 3;
        ext.ouie = parseInt(ouieInput.value) || 3;
        const byBase = {};
        document.querySelectorAll('.skill-check').forEach(cb => {
            const base = cb.id.replace(/-\d$/, '');
            if (!byBase[base]) {
                const raw = cb.getAttribute('data-attrs') || cb.getAttribute('data-attr') || '';
                byBase[base] = {
                    boxes: [],
                    attrs: raw.split(',').map(s => s.trim().toLowerCase()).filter(Boolean),
                    mag: cb.hasAttribute('data-mag')
                };
            }
            if (cb.checked) byBase[base].boxes.push(cb);
        });
        for (const base in byBase) {
            const el = document.querySelector('.skill-die[data-skillref="' + base + '"]');
            if (!el) continue;
            const g = byBase[base];
            let score = skillBaseScore(g.attrs, ext);
            // Corps Augmenté (Livre III §11.1) : en offensif, un éveillé retient
            // le meilleur entre sa MAG et la moyenne FOR/AGI.
            if (g.mag && ext.mag > 0) score = Math.max(score, ext.mag);
            const baseDie = dieForValue(score);
            const lvl = g.boxes.length;
            el.textContent = stepDie(baseDie, lvl);
            el.className = 'pill-badge skill-die ' + (lvl > 0 ? 'amber' : 'cyan');
        }
    }

    // Bonus mécaniques directs de certains dons raciaux (le reste = rappel narratif).
    function giftMechanics(race, gi) {
        if ((race === 'derek' && gi === 0) || (race === 'mikyai' && gi === 0)) return { cap: 2, ess: 0 };
        if ((race === 'humain' && gi === 0) || (race === 'vardien' && gi === 0)) return { cap: 0, ess: 1 };
        return { cap: 0, ess: 0 };
    }

    function currentRace() { return resolveRace(raceSelect.value); }

    function updateGiftOptions(keepIndex) {
        if (!giftSelect) return;
        const r = currentRace();
        const prev = keepIndex ? parseInt(giftSelect.value) : 0;
        giftSelect.innerHTML = '';
        RACE_DATA[r].gifts.forEach((g, i) => {
            const opt = document.createElement('option');
            opt.value = String(i);
            opt.textContent = (i + 1) + '. ' + g;
            opt.title = RACE_DATA[r].giftNotes[i] || '';
            giftSelect.appendChild(opt);
        });
        giftSelect.value = String(Math.min(Math.max(prev || 0, 0), 4));
    }

    // Menus déroulants à options décrites : fermé = le choix seul ; ouvert =
    // chaque option avec son effet (tooltip intégré au choosing). Le <select>
    // natif reste la source de vérité (caché) pour la sauvegarde et la synchro.
    const descDropdowns = [];
    function attachDescDropdown(selectEl, ddId, getItems) {
        if (!selectEl || document.getElementById(ddId)) return null;
        selectEl.style.display = 'none';
        const wrap = document.createElement('div');
        wrap.id = ddId;
        wrap.className = 'cs-dd';
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'cs-dd-btn';
        btn.setAttribute('aria-haspopup', 'listbox');
        btn.setAttribute('aria-expanded', 'false');
        const menu = document.createElement('div');
        menu.className = 'cs-dd-menu';
        menu.setAttribute('role', 'listbox');
        menu.hidden = true;
        wrap.appendChild(btn);
        wrap.appendChild(menu);
        selectEl.after(wrap);
        const api = {
            key: '',
            open() { this.key = ''; this.render(); menu.hidden = false; btn.setAttribute('aria-expanded', 'true'); },
            close() { menu.hidden = true; btn.setAttribute('aria-expanded', 'false'); },
            render() {
                const items = getItems();
                if (!items.length) return;
                const cur = String(selectEl.value);
                let sel = items.findIndex(it => String(it.value) === cur);
                if (sel < 0) sel = 0;
                btn.innerHTML = '';
                const label = document.createElement('span');
                label.textContent = items[sel].label;
                const caret = document.createElement('span');
                caret.textContent = '▾';
                caret.setAttribute('aria-hidden', 'true');
                btn.appendChild(label);
                btn.appendChild(caret);
                btn.title = items[sel].desc || '';
                const key = cur + ':' + items.length + ':' + items[0].label;
                if (key === this.key && menu.childElementCount) return;
                this.key = key;
                menu.innerHTML = '';
                items.forEach((it) => {
                    const row = document.createElement('div');
                    const isSel = String(it.value) === cur;
                    row.className = 'cs-dd-item' + (isSel ? ' selected' : '');
                    row.setAttribute('role', 'option');
                    row.setAttribute('aria-selected', isSel ? 'true' : 'false');
                    if (it.desc) row.title = it.desc;
                    const n = document.createElement('strong');
                    n.textContent = it.label;
                    row.appendChild(n);
                    if (it.desc) {
                        const d = document.createElement('span');
                        d.textContent = it.desc;
                        row.appendChild(d);
                    }
                    row.addEventListener('click', () => {
                        selectEl.value = String(it.value);
                        this.key = '';
                        this.close();
                        updateCharacterCalculations();
                        flushSync(); // choix discret : sauvegarde immédiate
                    });
                    menu.appendChild(row);
                });
            }
        };
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            if (menu.hidden) {
                for (const dd of descDropdowns) { if (dd !== api) { try { dd.close(); } catch (err) { /* ignore */ } } }
                api.open();
            } else api.close();
        });
        btn.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') api.close();
            else if ((e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown') && menu.hidden) {
                e.preventDefault();
                for (const dd of descDropdowns) { if (dd !== api) { try { dd.close(); } catch (err) { /* ignore */ } } }
                api.open();
            }
        });
        document.addEventListener('click', (e) => { if (!wrap.contains(e.target)) api.close(); });
        document.addEventListener('keydown', (e) => { if (e.key === 'Escape') api.close(); });
        descDropdowns.push(api);
        return api;
    }
    function renderDescDropdowns() {
        for (const dd of descDropdowns) {
            try { dd.render(); } catch (e) { /* ignore */ }
        }
    }
    function attachSheetDropdowns() {
        attachDescDropdown(giftSelect, 'csDdGift', () => {
            const r = currentRace();
            return RACE_DATA[r].gifts.map((g, i) => ({ value: String(i), label: (i + 1) + '. ' + g, desc: RACE_DATA[r].giftNotes[i] || '' }));
        });
        attachDescDropdown(enfanceSelect, 'csDdEnfance', () =>
            ENFANCES.map((e, i) => ({ value: String(i + 1), label: (i + 1) + '. ' + e.n, desc: e.d })));
        attachDescDropdown(metierSelect, 'csDdMetier', () =>
            METIERS.map((m, i) => ({ value: String(i + 1), label: (i + 1) + '. ' + m.n, desc: m.d })));
        attachDescDropdown(evenementSelect, 'csDdEvenement', () =>
            [{ value: '0', label: 'Aucun (optionnel)', desc: 'Pas d\u2019événement marquant.' }]
                .concat(EVENEMENTS.map((e, i) => ({ value: String(i + 1), label: (i + 1) + '. ' + e.n, desc: e.d }))));
    }

    // ---- SYNCHRO TEMPS RÉEL + ONGLETS PERSONNAGES (multi-fiches) ----
    // Même origine => localStorage partagé + BroadcastChannel instantané (+ event
    // 'storage' en repli). Une table = jusqu'à 12 onglets (PJ + PNJ), chacun avec sa
    // fiche complète. Le stockage partagé est ROSTER_KEY ; l'onglet actif est propre
    // à chaque fenêtre (sessionStorage) pour comparer deux personnages côte à côte.
    // La clé historique SYNC_KEY reflète toujours l'onglet actif (compatibilité).
    const SYNC_KEY = 'rp_codex_character';
    const SYNC_CH_NAME = 'kt-fiche-pj-sync';
    const ROSTER_KEY = 'rp_codex_characters_v1';
    const ACTIVE_KEY = 'rp_codex_active_tab';
    const MAX_TABS = 12;
    let roster = { tabs: [], lastActiveId: null };
    const syncId = Math.random().toString(36).slice(2);
    let syncTimer = null;
    let suppressSync = false;
    let lastSyncJson = '';
    let lastRosterJson = '';
    let syncChannel = null;
    try { if ('BroadcastChannel' in window) syncChannel = new BroadcastChannel(SYNC_CH_NAME); } catch (e) { syncChannel = null; }

    function newTabId() {
        try { if (window.crypto && crypto.randomUUID) return 'pj_' + crypto.randomUUID().slice(0, 8); } catch (e) { /* repli */ }
        return 'pj_' + Date.now().toString(36) + Math.floor(Math.random() * 1e6).toString(36);
    }
    function activeTabId() {
        try {
            const s = sessionStorage.getItem(ACTIVE_KEY);
            if (s && roster.tabs.some(t => t.id === s)) return s;
        } catch (e) { /* stockage indisponible */ }
        if (roster.lastActiveId && roster.tabs.some(t => t.id === roster.lastActiveId)) return roster.lastActiveId;
        return roster.tabs.length ? roster.tabs[0].id : null;
    }
    function getActiveTab() {
        const id = activeTabId();
        return roster.tabs.find(t => t.id === id) || null;
    }
    function tabDisplayName(tab) {
        const custom = (tab.label || '').trim();
        if (custom) return custom;
        const n = (tab.data && tab.data.name || '').trim();
        return n || 'Sans nom';
    }
    function setActiveTabId(id) {
        roster.lastActiveId = id;
        try { sessionStorage.setItem(ACTIVE_KEY, id); } catch (e) { /* ignore */ }
    }
    function readStoredRoster() {
        try {
            const raw = localStorage.getItem(ROSTER_KEY);
            if (!raw) return null;
            const parsed = JSON.parse(raw);
            if (!parsed || !Array.isArray(parsed.tabs)) return null;
            return parsed;
        } catch (e) { return null; }
    }
    // Fiche canon de Roger (Livre I §6) : base d'un nouvel onglet / réinitialisation.
    function buildDefaultData(name) {
        return {
            v: 2,
            _rev: 1,
            name: name || 'Nouveau héros',
            age: '27',
            sexe: 'M',
            monde: 'Frontière Chaude',
            race: 'humain',
            gift: 3,
            profile: 'hero',
            pillarMajor: 'for',
            pillarMinor: 'con',
            attributes: { for: '5', agi: '3', con: '4', rap: '3', int: '2', eru: '3', cha: '1', ins: '3', mag: '0' },
            vision: '4',
            ouie: '3',
            enfance: '2',
            metier: '1',
            evenement: '2',
            ancrage: '',
            skills: ['skMainsNues-1', 'skMainsNues-2', 'skEsquive-1']
        };
    }
    function updateActiveFromForm() {
        const tab = getActiveTab();
        if (!tab) return;
        const prevRev = revOf(tab.data);
        tab.data = collectSheetData();
        if (prevRev) tab.data._rev = prevRev; // l'incrément se fait dans doSync
        if (!tab.customLabel) tab.label = (tab.data.name || '').trim();
        tab.updatedAt = Date.now();
    }
    function persistRoster() {
        try {
            const tab = getActiveTab();
            // Clé historique d'abord : quand deux fenêtres affichent deux onglets
            // différents, l'événement ROSTER (traité après) a toujours le dernier
            // mot et restaure le bon onglet actif — jamais l'inverse.
            if (tab) localStorage.setItem(SYNC_KEY, JSON.stringify(tab.data));
            lastRosterJson = JSON.stringify(roster);
            localStorage.setItem(ROSTER_KEY, lastRosterJson);
        } catch (e) { /* stockage indisponible */ }
    }
    function broadcastTab() {
        scheduleCloudSave();
        if (!syncChannel) return;
        const tab = getActiveTab();
        if (!tab) return;
        try {
            syncChannel.postMessage({
                source: syncId,
                tabId: tab.id,
                data: tab.data,
                tabsMeta: roster.tabs.map(t => ({ id: t.id, label: tabDisplayName(t), updatedAt: t.updatedAt || 0 })),
                savedAt: Date.now()
            });
        } catch (e) { /* ignore */ }
    }
    // ---- SAUVEGARDE SERVEUR (si connecté) / LOCALE (sinon) ----
    // Non connecté : tout reste dans le navigateur (localStorage).
    // À la connexion : le local est transféré vers le serveur (union par
    // id, le local gagne ; local vierge => on adopte le serveur), puis
    // chaque modif est poussée en autosave (debounce 2 s).
    // Sans serveur joignable : repli silencieux sur le local.
    let cloudTimer = null;
    let cloudBootDone = false;
    let cloudState = 'local';
    let localIsFresh = true;
    function getApiBase() {
        if (window.CODEX_API_BASE) return window.CODEX_API_BASE.replace(/\/+$/, '');
        if (window.location.protocol === 'file:') return 'http://localhost:3000';
        if (window.location.port === '8000') return window.location.protocol + '//' + window.location.hostname + ':3000';
        if ((window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1') && window.location.port !== '3000') return 'http://localhost:3000';
        return '';
    }
    function isLoggedIn() {
        try { return !!(window.CodexAuth && window.CodexAuth.currentUser && window.CodexAuth.currentUser.id); }
        catch (e) { return false; }
    }
    function updateCloudBadge(state) {
        if (state) cloudState = state;
        const el = document.getElementById('csCloudState');
        if (!el) return;
        if (cloudState === 'ok') {
            el.textContent = 'Serveur ✓';
            el.className = 'pill-badge emerald';
            el.title = 'Connecté : fiches sauvegardées dans votre fichier serveur.';
        } else if (cloudState === 'sending') {
            el.textContent = 'Envoi…';
            el.className = 'pill-badge amber';
            el.title = 'Transfert vers le serveur en cours…';
        } else if (cloudState === 'error') {
            el.textContent = 'Serveur injoignable';
            el.className = 'pill-badge ruby';
            el.title = 'Serveur injoignable : sauvegarde locale conservée, nouvel essai à la prochaine modif.';
        } else {
            cloudState = 'local';
            el.textContent = 'Sauvegarde locale';
            el.className = 'pill-badge cyan';
            el.title = 'Non connecté : fiches conservées dans ce navigateur. Transférées au serveur à la connexion.';
        }
    }
    function scheduleCloudSave() {
        if (!isLoggedIn()) { updateCloudBadge('local'); return; }
        updateCloudBadge('sending');
        if (cloudTimer) clearTimeout(cloudTimer);
        cloudTimer = setTimeout(pushRosterToServer, 2000);
    }
    function sanitizeTab(t) {
        return t && typeof t.id === 'string' && t.data && typeof t.data === 'object';
    }
    function mergeRosters(localTabs, serverTabs) {
        const byId = new Map();
        for (const t of serverTabs) if (sanitizeTab(t)) byId.set(t.id, t);
        // Conflit sur un même id : la révision la plus haute gagne (le local
        // gagne à égalité) — un « Hb » serveur périmé ne ressuscite plus.
        for (const t of localTabs) if (sanitizeTab(t)) {
            const prev = byId.get(t.id);
            if (!prev || revOf(t.data) >= revOf(prev.data)) byId.set(t.id, t);
        }
        return [...byId.values()].slice(0, MAX_TABS);
    }
    async function pullServerRoster() {
        const res = await fetch(getApiBase() + '/api/characters', {
            method: 'GET',
            headers: { 'ngrok-skip-browser-warning': 'true' },
            credentials: 'include'
        });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const body = await res.json();
        return Array.isArray(body.tabs) ? body.tabs.filter(sanitizeTab) : [];
    }
    async function pushRosterToServer() {
        cloudTimer = null;
        if (!isLoggedIn()) { updateCloudBadge('local'); return; }
        if (!cloudBootDone) { transferLocalToServer(); return; }
        updateActiveFromForm();
        try {
            const res = await fetch(getApiBase() + '/api/characters', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json', 'ngrok-skip-browser-warning': 'true' },
                credentials: 'include',
                body: JSON.stringify({ tabs: roster.tabs })
            });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            updateCloudBadge('ok');
        } catch (e) { updateCloudBadge('error'); }
    }
    async function transferLocalToServer() {
        if (!isLoggedIn()) { updateCloudBadge('local'); return; }
        updateCloudBadge('sending');
        try {
            updateActiveFromForm();
            let serverTabs = [];
            try { serverTabs = await pullServerRoster(); }
            catch (e) { /* serveur injoignable : le local reste la référence */ }
            // Local vierge (premier lancement) : on adopte le serveur tel quel.
            const merged = (localIsFresh && serverTabs.length && roster.tabs.length <= 1)
                ? serverTabs.slice(0, MAX_TABS)
                : mergeRosters(roster.tabs, serverTabs);
            const pulled = merged.filter(m => !roster.tabs.some(t => t.id === m.id)).length;
            roster.tabs = merged;
            localIsFresh = false;
            if (!roster.tabs.length) {
                roster.tabs.push({ id: newTabId(), label: '', customLabel: false, updatedAt: Date.now(), data: buildDefaultData('Nouveau héros 1') });
            }
            if (!getActiveTab()) setActiveTabId(roster.tabs[0].id);
            persistRoster();
            renderTabs();
            suppressSync = true;
            try {
                const tab = getActiveTab();
                if (tab && tab.data) applySheetData(tab.data);
                try { lastSyncJson = JSON.stringify(collectSheetData()); } catch (e) { /* ignore */ }
            } finally { suppressSync = false; }
            const res = await fetch(getApiBase() + '/api/characters', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json', 'ngrok-skip-browser-warning': 'true' },
                credentials: 'include',
                body: JSON.stringify({ tabs: roster.tabs })
            });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            cloudBootDone = true;
            updateCloudBadge('ok');
            showToast(pulled > 0
                ? 'Serveur synchronisé : ' + pulled + ' fiche(s) récupérée(s), ' + merged.length + ' au total.'
                : 'Fiches transférées vers le serveur (' + merged.length + ').');
        } catch (e) {
            cloudBootDone = false;
            updateCloudBadge('error');
            showToast('Serveur injoignable : sauvegarde locale conservée.');
        }
    }
    function ensureTabsBar() {
        if (document.getElementById('csTabsBar')) return;
        const host = (vitals.ptsLeft && vitals.ptsLeft.closest) ? vitals.ptsLeft.closest('.tool-section') : null;
        if (!host) return;
        const anchor = host.querySelector('.tool-header-bar');
        const bar = document.createElement('div');
        bar.id = 'csTabsBar';
        bar.className = 'cs-tabs-bar';
        bar.setAttribute('role', 'tablist');
        bar.setAttribute('aria-label', 'Personnages de la table');
        const list = document.createElement('div');
        list.id = 'csTabsList';
        list.className = 'cs-tabs-list';
        bar.appendChild(list);
        const actions = document.createElement('div');
        actions.className = 'cs-tabs-actions';
        const count = document.createElement('span');
        count.id = 'csTabsCount';
        count.className = 'pill-badge cyan';
        count.title = 'Nombre d\'onglets personnages';
        actions.appendChild(count);
        const cloud = document.createElement('span');
        cloud.id = 'csCloudState';
        cloud.className = 'pill-badge cyan';
        actions.appendChild(cloud);
        updateCloudBadge();
        const mkBtn = (id, label, title, fn) => {
            const b = document.createElement('button');
            b.type = 'button';
            b.id = id;
            b.className = 'tool-btn secondary cs-tabs-btn';
            b.textContent = label;
            b.title = title;
            b.addEventListener('click', fn);
            actions.appendChild(b);
        };
        mkBtn('csTabAdd', '+ Nouveau', 'Créer un nouvel onglet personnage', createNewTab);
        mkBtn('csTabDup', 'Dupliquer', 'Dupliquer l\'onglet actif dans un nouvel onglet', duplicateActiveTab);
        mkBtn('csTabRename', 'Renommer', 'Renommer l\'onglet actif (double-clic sur l\'onglet aussi)', () => renameTab());
        mkBtn('csTabDelete', 'Supprimer', 'Supprimer l\'onglet actif (pensez à Exporter JSON avant)', () => deleteTab());
        bar.appendChild(actions);
        if (anchor && anchor.parentElement === host) host.insertBefore(bar, anchor);
        else host.prepend(bar);
    }
    function renderTabs() {
        const list = document.getElementById('csTabsList');
        if (!list) return;
        const activeId = activeTabId();
        list.innerHTML = '';
        roster.tabs.forEach((tab, idx) => {
            const b = document.createElement('button');
            b.type = 'button';
            b.className = 'cs-tab' + (tab.id === activeId ? ' active' : '');
            b.setAttribute('role', 'tab');
            b.setAttribute('aria-selected', tab.id === activeId ? 'true' : 'false');
            b.title = 'Personnage ' + (idx + 1) + ' — afficher (double-clic : renommer)';
            b.textContent = tabDisplayName(tab) || ('Personnage ' + (idx + 1));
            b.addEventListener('click', () => switchTab(tab.id));
            b.addEventListener('dblclick', () => renameTab(tab.id));
            list.appendChild(b);
        });
        const count = document.getElementById('csTabsCount');
        if (count) count.textContent = roster.tabs.length + ' / ' + MAX_TABS;
    }
    function switchTab(id) {
        if (!roster.tabs.some(t => t.id === id)) return;
        if (id === activeTabId()) { renderTabs(); return; }
        if (!suppressSync) updateActiveFromForm();
        persistRoster();
        setActiveTabId(id);
        suppressSync = true;
        try {
            const tab = getActiveTab();
            if (tab && tab.data) applySheetData(tab.data);
            try { lastSyncJson = JSON.stringify(collectSheetData()); } catch (e) { /* ignore */ }
        } finally {
            suppressSync = false;
        }
        renderTabs();
    }
    function addTab(data, opts) {
        opts = opts || {};
        if (roster.tabs.length >= MAX_TABS) { showToast('Limite de ' + MAX_TABS + ' onglets atteinte.'); return null; }
        if (!suppressSync) updateActiveFromForm();
        const tab = { id: newTabId(), label: opts.label || '', customLabel: !!opts.customLabel, updatedAt: Date.now(), data: data };
        roster.tabs.push(tab);
        if (opts.makeActive !== false) setActiveTabId(tab.id);
        persistRoster();
        if (opts.makeActive !== false) {
            suppressSync = true;
            try {
                applySheetData(tab.data);
                try { lastSyncJson = JSON.stringify(collectSheetData()); } catch (e) { /* ignore */ }
            } finally { suppressSync = false; }
        }
        renderTabs();
        broadcastTab();
        return tab;
    }
    function createNewTab() {
        const tab = addTab(buildDefaultData('Nouveau héros ' + (roster.tabs.length + 1)), {});
        if (tab) showToast('Nouvel onglet : ' + tabDisplayName(tab));
    }
    function duplicateActiveTab() {
        const src = getActiveTab();
        if (!src) return;
        updateActiveFromForm();
        const data = JSON.parse(JSON.stringify(src.data));
        data.name = (data.name || 'Sans nom') + ' (copie)';
        const tab = addTab(data, {});
        if (tab) showToast('Onglet dupliqué : ' + tabDisplayName(tab));
    }
    function renameTab(id) {
        const tab = roster.tabs.find(t => t.id === (id || activeTabId()));
        if (!tab) return;
        const next = prompt('Nom de l\'onglet personnage :', tabDisplayName(tab));
        if (next === null) return;
        const clean = next.trim().slice(0, 40);
        if (!clean) { showToast('Nom vide ignoré.'); return; }
        if (!suppressSync && tab.id === activeTabId()) updateActiveFromForm();
        tab.label = clean;
        tab.customLabel = true;
        tab.updatedAt = Date.now();
        persistRoster();
        renderTabs();
        broadcastTab();
        showToast('Onglet renommé : ' + clean);
    }
    function deleteTab(id) {
        const targetId = id || activeTabId();
        const idx = roster.tabs.findIndex(t => t.id === targetId);
        if (idx < 0) return;
        const tab = roster.tabs[idx];
        if (!confirm('Supprimer l\'onglet « ' + tabDisplayName(tab) + ' » ? Sa fiche sera perdue (pensez à Exporter JSON avant).')) return;
        const wasActive = targetId === activeTabId();
        roster.tabs.splice(idx, 1);
        if (!roster.tabs.length) {
            roster.tabs.push({ id: newTabId(), label: '', customLabel: false, updatedAt: Date.now(), data: buildDefaultData('Nouveau héros 1') });
        }
        if (wasActive) setActiveTabId(roster.tabs[Math.min(idx, roster.tabs.length - 1)].id);
        persistRoster();
        if (wasActive) {
            suppressSync = true;
            try {
                applySheetData(getActiveTab().data);
                try { lastSyncJson = JSON.stringify(collectSheetData()); } catch (e) { /* ignore */ }
            } finally { suppressSync = false; }
        }
        renderTabs();
        broadcastTab();
        showToast('Onglet supprimé.');
    }
    function scheduleSync() {
        if (suppressSync) return;
        if (syncTimer) clearTimeout(syncTimer);
        // Fin de frappe détectée après 1,2 s d'inactivité : aucun sync
        // (persist + broadcast) pendant qu'on tape, les calculs restent live.
        syncTimer = setTimeout(doSync, 1200);
    }
    // Saisie terminée (blur, Entrée, sélection) : purge le debounce en attente.
    function flushSync() {
        if (suppressSync) return;
        if (!syncTimer) return;
        clearTimeout(syncTimer);
        syncTimer = null;
        doSync();
    }
    function doSync() {
        if (suppressSync) return;
        syncTimer = null;
        try {
            updateActiveFromForm();
            const tab = getActiveTab();
            if (!tab) return;
            const json = contentJson(tab.data);
            if (json === lastSyncJson) { persistRoster(); return; }
            // Changement local réel : horloge de révision +1 (last-writer-wins
            // entre fenêtres, et immunité contre les états périmés).
            tab.data._rev = revOf(tab.data) + 1;
            lastSyncJson = json;
            persistRoster();
            renderTabs();
            broadcastTab();
        } catch (e) { /* ignore */ }
    }
    // Horloge de révision par fiche : chaque sauvegarde locale incrémente
    // _rev. Une donnée entrante plus ancienne est ignorée — c'est ce qui
    // empêche un état périmé (ex. « Hb ») d'écraser la frappe en cours,
    // même avec une autre fenêtre restée sur l'ancien code (rev absente = 0).
    function revOf(d) { return (d && typeof d._rev === 'number' && d._rev > 0) ? d._rev : 0; }
    function contentJson(d) {
        if (!d || typeof d !== 'object') return '';
        const c = {};
        for (const k of Object.keys(d)) if (k !== '_rev') c[k] = d[k];
        return JSON.stringify(c);
    }
    function applyRemoteSheet(data, tabId) {
        if (!data || typeof data !== 'object') return;
        if (tabId && tabId !== activeTabId()) {
            // Onglet non affiché édité ailleurs : stock mis à jour, formulaire inchangé.
            const tab = roster.tabs.find(t => t.id === tabId);
            if (tab) {
                if (revOf(data) < revOf(tab.data)) return; // état périmé : ignoré
                tab.data = data;
                tab.updatedAt = Date.now();
                if (!tab.customLabel) tab.label = (data.name || '').trim();
            } else {
                roster.tabs.push({ id: tabId, label: (data.name || '').trim(), customLabel: false, updatedAt: Date.now(), data: data });
                persistRoster();
            }
            renderTabs();
            return;
        }
        const activeTab = getActiveTab();
        if (activeTab && revOf(data) < revOf(activeTab.data)) return; // état périmé : ignoré
        suppressSync = true;
        try {
            const tab = getActiveTab();
            if (tab) {
                tab.data = data;
                tab.updatedAt = Date.now();
                if (!tab.customLabel) tab.label = (data.name || '').trim();
            }
            applySheetData(data, { preserveFocused: true });
            try { lastSyncJson = JSON.stringify(collectSheetData()); } catch (e) { /* ignore */ }
            renderTabs();
        } finally {
            suppressSync = false;
        }
    }
    function applyRemoteMeta(msg) {
        if (!msg || !Array.isArray(msg.tabsMeta)) return;
        let changed = false;
        const metaById = new Map(msg.tabsMeta.map(m => [m.id, m]));
        for (const tab of roster.tabs) {
            const m = metaById.get(tab.id);
            if (m && tab.label !== m.label) { tab.label = m.label; tab.customLabel = true; changed = true; }
        }
        // Suppression distante : on ne retire que les onglets plus vieux que la sauvegarde distante.
        for (let i = roster.tabs.length - 1; i >= 0; i--) {
            const tab = roster.tabs[i];
            if (!metaById.has(tab.id) && (tab.updatedAt || 0) < (msg.savedAt || 0)) {
                const wasActive = tab.id === activeTabId();
                roster.tabs.splice(i, 1);
                changed = true;
                if (wasActive && msg.tabId && metaById.has(msg.tabId)) setActiveTabId(msg.tabId);
            }
        }
        if (!getActiveTab() && roster.tabs.length) { setActiveTabId(roster.tabs[0].id); changed = true; }
        // Pas de persist si rien n'a changé : réécrire le roster ici renvoie
        // un écho qui écrase la frappe en cours dans l'autre fenêtre.
        if (changed) persistRoster();
        renderTabs();
    }

    // Garde-fous création (Livre I §3) : les steppers ne peuvent pas sortir
    // des fourchettes légales — piliers verrouillés sur leur cible
    // (majeur 5 Héros / 4 Élite, mineur 4), secondaires 1-3, sbire 1-3 partout.
    function clampCreationRanges() {
        if (!profileSelect) return;
        const profile = profileSelect.value;
        if (profile === 'sbire') {
            for (const k of CORE8) {
                const el = attrInputs[k];
                if (!el) continue;
                el.min = 1; el.max = 3;
                if (+el.value > 3) el.value = 3;
                if (+el.value < 1) el.value = 1;
            }
            return;
        }
        const isHero = (profile === 'hero');
        const maj = pillarMajorSelect ? pillarMajorSelect.value : null;
        const min = pillarMinorSelect ? pillarMinorSelect.value : null;
        for (const k of CORE8) {
            const el = attrInputs[k];
            if (!el) continue;
            if (k === maj) {
                const t = isHero ? 5 : 4;
                el.min = t; el.max = t; el.value = t;
            } else if (k === min) {
                el.min = 4; el.max = 4; el.value = 4;
            } else {
                el.min = 1; el.max = 3;
                if (+el.value > 3) el.value = 3;
                if (+el.value < 1) el.value = 1;
            }
        }
    }

    function updateCharacterCalculations() {
        clampCreationRanges();
        const race = currentRace();
        const profile = profileSelect.value;
        const rdata = RACE_DATA[race];
        const giftIdx = giftSelect ? (parseInt(giftSelect.value) || 0) : 0;

        // Valeurs brutes (les espèces ne modifient PAS les attributs — Livre I §2.2).
        const vals = {};
        for (const [k, el] of Object.entries(attrInputs)) {
            let base = parseInt(el.value) || 0;
            if (k === 'mag') { base = Math.min(5, Math.max(0, base)); }
            else { base = Math.min(10, Math.max(1, base)); }
            el.value = base;
            vals[k] = base;
        }

        const raceNoteEl = document.getElementById('csRaceNote');
        if (raceNoteEl) raceNoteEl.textContent = rdata.note + ' | Magie : ' + rdata.magie;
        const giftNoteEl = document.getElementById('csGiftNote');
        if (giftNoteEl) giftNoteEl.textContent = rdata.giftNotes[giftIdx] || '';
        renderDescDropdowns();

        const eff = {};
        for (const k of CORE8) eff[k] = vals[k];
        eff.mag = vals.mag;

        const minCore = Math.min(...CORE8.map(k => eff[k]));

        // Dérivés (Livre I §4) + bonus mécaniques du don racial.
        const mech = giftMechanics(race, giftIdx);
        const paBase = Math.max(eff.agi, eff.int) + eff.rap + minCore;
        const capBase = eff.con * 2 + mech.cap;
        const maxBase = eff.con * 5;
        const essBase = eff.con + mech.ess;

        if (vitals.pa) vitals.pa.textContent = paBase;
        if (vitals.cap) vitals.cap.textContent = capBase;
        if (vitals.max) vitals.max.textContent = maxBase;
        if (vitals.ess) vitals.ess.textContent = essBase + ' tours';
        if (vitals.trainedCount) vitals.trainedCount.textContent = eff.eru;

        // Initiative (Livre I §4.3) : Max(RAP, AGI, INT) converti en dé.
        const initVals = [['RAP', eff.rap], ['AGI', eff.agi], ['INT', eff.int]];
        initVals.sort((a, b) => b[1] - a[1]);
        const initEl = document.getElementById('vitalInit');
        if (initEl) initEl.textContent = dieForValue(initVals[0][1]) + ' (' + initVals[0][0] + ' ' + initVals[0][1] + ')';
        const miracleEl = document.getElementById('vitalMiracle');
        if (miracleEl) miracleEl.textContent = (profile === 'sbire') ? '0 (Sbire)' : '1 (PJ)';

        // Dés d'attributs intégrés à chaque caractéristique (Livre II §6.1), sans entraînement.
        refreshAttrDiceBadges(eff);

        // Badges de dés par compétence (avec paliers d'entraînement).
        refreshSkillDice(eff);

        // ---- Validation du budget (Livre I §3) ----
        const errors = [];
        const awakened = eff.mag > 0;
        const pillarMajor = pillarMajorSelect ? pillarMajorSelect.value : 'for';
        const pillarMinor = pillarMinorSelect ? pillarMinorSelect.value : 'con';
        let budgetTotal = 0, ptsUsed = 0, expected = '';

        if (profile === 'sbire') {
            budgetTotal = 12; // Livre I §3 : 12 pts sur les 8 attributs, min 1 max 3, aucun à 0.
            ptsUsed = CORE8.reduce((s, k) => s + eff[k], 0);
            expected = 'Sbire : 12 pts sur les 8 attributs (Magie hors budget).';
            for (const k of CORE8) {
                if (eff[k] < 1 || eff[k] > 3) errors.push(ATTR_LABELS[k] + ' doit être entre 1 et 3 (sbire).');
            }
            if (awakened) errors.push('Note : le Livre I §3 indique 11 pts si Magie débloquée pour un sbire — appliquez 12 par tolérance MJ.');
        } else {
            const isHero = (profile === 'hero');
            const pilMajVal = isHero ? 5 : 4, pilMinVal = isHero ? 4 : 4;
            const secBudget = awakened ? 17 : 15;
            budgetTotal = pilMajVal + pilMinVal + secBudget;
            const others = CORE8.filter(k => k !== pillarMajor && k !== pillarMinor);
            ptsUsed = CORE8.reduce((s, k) => s + eff[k], 0);
            expected = (isHero ? 'Héros PJ : pilier ' + pillarMajor.toUpperCase() + ' = 5, pilier ' + pillarMinor.toUpperCase() + ' = 4, puis exactement ' + secBudget + ' pts dans les 6 autres (1-3 chacun).'
                              : 'Élite : piliers ' + pillarMajor.toUpperCase() + ' = 4 et ' + pillarMinor.toUpperCase() + ' = 4, puis exactement ' + secBudget + ' pts dans les 6 autres (1-3 chacun).')
                       + (awakened ? ' Éveil (Magie ≥ 1, hors budget) : budget secondaires 17 au lieu de 15.' : '');
            if (pillarMajor === pillarMinor) errors.push('Les deux piliers doivent être deux attributs différents.');
            if (eff[pillarMajor] !== pilMajVal) errors.push('Pilier majeur ' + pillarMajor.toUpperCase() + ' : attendu ' + pilMajVal + ', actuel ' + eff[pillarMajor] + '.');
            if (eff[pillarMinor] !== pilMinVal) errors.push('Pilier mineur ' + pillarMinor.toUpperCase() + ' : attendu ' + pilMinVal + ', actuel ' + eff[pillarMinor] + '.');
            for (const k of others) {
                if (eff[k] < 1 || eff[k] > 3) errors.push(ATTR_LABELS[k] + ' (secondaire) doit être entre 1 et 3 à la création, actuel ' + eff[k] + '.');
            }
            const othersSum = others.reduce((s, k) => s + eff[k], 0);
            if (othersSum !== secBudget) errors.push('Secondaires : ' + othersSum + '/' + secBudget + ' pts (écart ' + (othersSum - secBudget > 0 ? '+' : '') + (othersSum - secBudget) + ').');
        }

        const ptsRemaining = budgetTotal - ptsUsed;
        if (vitals.ptsLeft) {
            vitals.ptsLeft.textContent = (errors.length === 0)
                ? 'Fiche valide — Total ' + ptsUsed + '/' + budgetTotal
                : (ptsRemaining + ' pt(s) d\u2019écart (Total ' + ptsUsed + '/' + budgetTotal + ')');
            vitals.ptsLeft.style.color = (errors.length === 0) ? '#34d399' : 'var(--vital-ruby)';
        }
        const validMsg = document.getElementById('csValidationMsg');
        if (validMsg) {
            validMsg.innerHTML = '';
            const rule = document.createElement('div');
            rule.style.cssText = 'font-size:0.78rem;color:var(--text-secondary);margin-bottom:6px;';
            rule.textContent = expected;
            validMsg.appendChild(rule);
            if (errors.length === 0) {
                const ok = document.createElement('div');
                ok.style.cssText = 'font-size:0.8rem;color:#34d399;font-weight:700;';
                ok.textContent = '✓ Budget conforme au Livre I §3. Pensez au don racial + historique (Livre VII).';
                validMsg.appendChild(ok);
            } else {
                const ul = document.createElement('ul');
                ul.style.cssText = 'margin:0;padding-left:18px;font-size:0.8rem;color:var(--vital-ruby);';
                errors.forEach(e => { const li = document.createElement('li'); li.textContent = e; ul.appendChild(li); });
                validMsg.appendChild(ul);
            }
        }

        // Détail d'historique sous les menus (intitulé complet + effet/rappel).
        const histDetail = document.getElementById('csHistoryDetail');
        if (histDetail) {
            histDetail.innerHTML = '';
            const rows = [];
            if (enfanceSelect && ENFANCES[parseInt(enfanceSelect.value) - 1]) rows.push(['Enfance', ENFANCES[parseInt(enfanceSelect.value) - 1]]);
            if (metierSelect && METIERS[parseInt(metierSelect.value) - 1]) rows.push(['Métier', METIERS[parseInt(metierSelect.value) - 1]]);
            if (evenementSelect && evenementSelect.value !== '0' && EVENEMENTS[parseInt(evenementSelect.value) - 1]) rows.push(['Événement', EVENEMENTS[parseInt(evenementSelect.value) - 1]]);
            for (const [label, entry] of rows) {
                const div = document.createElement('div');
                div.style.cssText = 'font-size:0.78rem;color:var(--text-secondary);margin-top:4px;';
                const b = document.createElement('strong');
                b.style.color = 'var(--text-primary)';
                b.textContent = label + ' : ' + entry.n + ' — ';
                div.appendChild(b);
                div.appendChild(document.createTextNode(entry.d));
                histDetail.appendChild(div);
            }
        }

        updateSkillsLimit(eff.eru);
        refreshStepperTitles();
        scheduleSync();
    }

    // Tasse les cases cochées vers les premières positions de chaque
    // compétence : seul le nombre de cases compte (+1 palier par case),
    // un trou (ex. case 2 cochée sans la 1) est refermé automatiquement.
    function normalizeSkillBoxes() {
        const groups = {};
        document.querySelectorAll('.skill-check').forEach(cb => {
            const base = cb.id.replace(/-\d$/, '');
            (groups[base] = groups[base] || []).push(cb);
        });
        for (const base in groups) {
            const boxes = groups[base].sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
            const n = boxes.filter(cb => cb.checked).length;
            boxes.forEach((cb, i) => { cb.checked = i < n; });
        }
    }

    function updateSkillsLimit(maxTrained) {
        normalizeSkillBoxes();
        const checkboxes = document.querySelectorAll('.skill-check:checked');
        const countSpan = document.getElementById('trainedSkillsChecked');
        if (countSpan) countSpan.textContent = `${checkboxes.length} / ${maxTrained}`;
        if (checkboxes.length > maxTrained && countSpan) {
            countSpan.style.color = 'var(--vital-ruby)';
        } else if (countSpan) {
            countSpan.style.color = 'var(--arcane-cyan)';
        }
        // Survol des cases non cochées : prévient quand le quota est plein.
        const fullMsg = checkboxes.length >= Math.max(1, maxTrained)
            ? 'Entraînements gratuits pleins : ' + checkboxes.length + '/' + maxTrained + ' (Érudition). Décoche 1 case ailleurs pour en cocher ici. +1 ÉRU = +1 gratuit.'
            : '';
        document.querySelectorAll('.skill-check:not(:checked)').forEach(cb => {
            if (fullMsg) cb.title = fullMsg;
            else cb.removeAttribute('title');
        });
    }

    // Piliers (Livre I §3) : changer de pilier ÉCHANGE les valeurs avec
    // l'ancien pilier (budget total conservé), force la cible du nouveau
    // (majeur = 5 Héros / 4 Élite, mineur = 4) puis recalcule tout.
    function applyPillarTargets() {
        if (!pillarMajorSelect || !pillarMinorSelect) return;
        if (profileSelect && profileSelect.value === 'sbire') return;
        const isHero = !profileSelect || profileSelect.value === 'hero';
        const maj = pillarMajorSelect.value, min = pillarMinorSelect.value;
        if (attrInputs[maj]) attrInputs[maj].value = isHero ? 5 : 4;
        if (attrInputs[min] && min !== maj) attrInputs[min].value = 4;
    }

    function syncPillarPrev() {
        if (pillarMajorSelect) pillarMajorSelect.dataset.prev = pillarMajorSelect.value;
        if (pillarMinorSelect) pillarMinorSelect.dataset.prev = pillarMinorSelect.value;
    }

    // Listes croisées : le majeur n'apparaît pas dans la droplist mineur et inversement.
    function rebuildPillarSelect(sel, excludeVal) {
        if (!sel) return;
        const cur = sel.value;
        sel.innerHTML = '';
        for (const k of CORE8) {
            if (k === excludeVal) continue;
            const opt = document.createElement('option');
            opt.value = k;
            opt.textContent = ATTR_LABELS[k];
            sel.appendChild(opt);
        }
        if (cur && cur !== excludeVal) sel.value = cur;
        // Sinon conflit : le select bascule sur la première option restante.
    }

    function refreshPillarOptions() {
        if (!pillarMajorSelect || !pillarMinorSelect) return;
        rebuildPillarSelect(pillarMinorSelect, pillarMajorSelect.value);
        rebuildPillarSelect(pillarMajorSelect, pillarMinorSelect.value);
    }

    // Bouton ⇄ : échange les rôles majeur/mineur (valeurs échangées, budget conservé).
    function swapPillars() {
        if (!pillarMajorSelect || !pillarMinorSelect) return;
        const m = pillarMajorSelect.value, n = pillarMinorSelect.value;
        if (!m || !n || m === n) return;
        if (attrInputs[m] && attrInputs[n]) {
            const tmp = attrInputs[m].value;
            attrInputs[m].value = attrInputs[n].value;
            attrInputs[n].value = tmp;
        }
        for (const sel of [pillarMajorSelect, pillarMinorSelect]) {
            const cur = sel.value;
            sel.innerHTML = '';
            for (const k of CORE8) {
                const opt = document.createElement('option');
                opt.value = k;
                opt.textContent = ATTR_LABELS[k];
                sel.appendChild(opt);
            }
            sel.value = cur;
        }
        pillarMajorSelect.value = n;
        pillarMinorSelect.value = m;
        refreshPillarOptions();
        syncPillarPrev();
        applyPillarTargets();
        updateCharacterCalculations();
    }

    function bindPillarSwap(selectEl) {
        if (!selectEl) return;
        selectEl.addEventListener('focus', () => { selectEl.dataset.prev = selectEl.value; });
        selectEl.addEventListener('change', () => {
            const prev = selectEl.dataset.prev;
            const cur = selectEl.value;
            if (prev && prev !== cur && attrInputs[prev] && attrInputs[cur]) {
                attrInputs[prev].value = attrInputs[cur].value;
            }
            refreshPillarOptions();
            syncPillarPrev();
            applyPillarTargets();
            updateCharacterCalculations();
        });
    }

    // Bind listeners
    for (const el of Object.values(attrInputs)) {
        el.addEventListener('input', updateCharacterCalculations);
    }
    raceSelect.addEventListener('change', () => { updateGiftOptions(false); clampSenses(); updateCharacterCalculations(); });
    profileSelect.addEventListener('change', () => { applyPillarTargets(); updateCharacterCalculations(); });
    if (giftSelect) giftSelect.addEventListener('change', updateCharacterCalculations);
    if (pillarMajorSelect) bindPillarSwap(pillarMajorSelect);
    if (pillarMinorSelect) bindPillarSwap(pillarMinorSelect);
    const btnSwapPillars = document.getElementById('csBtnSwapPillars');
    if (btnSwapPillars) btnSwapPillars.addEventListener('click', swapPillars);
    for (const el of [nameInput, ageInput, sexeSelect, mondeInput, ancrageInput, visionInput, ouieInput, enfanceSelect, metierSelect, evenementSelect]) {
        if (el) el.addEventListener('input', updateCharacterCalculations);
    }

    // Bulle d'avertissement sur clic bloqué (verrou pilier / plafond création).
    let stepTipEl = null;
    function showStepBlockTip(btn, reason) {
        if (stepTipEl) stepTipEl.remove();
        stepTipEl = document.createElement('div');
        stepTipEl.textContent = reason;
        stepTipEl.style.cssText = 'position:fixed;z-index:9999;max-width:260px;padding:8px 10px;font-size:0.8rem;color:var(--text-primary);background:var(--bg-surface-elevated);border:1px solid var(--vital-ruby);border-radius:8px;box-shadow:0 8px 24px rgba(0,0,0,0.5);pointer-events:none;';
        document.body.appendChild(stepTipEl);
        const r = btn.getBoundingClientRect();
        stepTipEl.style.left = Math.min(window.innerWidth - 270, Math.max(8, r.left - 60)) + 'px';
        stepTipEl.style.top = (r.bottom + 8) + 'px';
        setTimeout(() => { if (stepTipEl) { stepTipEl.remove(); stepTipEl = null; } }, 2200);
    }

    function stepBlockReason(input, step) {
        const key = Object.keys(attrInputs).find(k => attrInputs[k] === input);
        const profile = profileSelect ? profileSelect.value : 'hero';
        const maj = pillarMajorSelect ? pillarMajorSelect.value : null;
        const min = pillarMinorSelect ? pillarMinorSelect.value : null;
        if (profile !== 'sbire' && key === maj) return 'Pilier majeur verrouillé à ' + (profile === 'hero' ? 5 : 4) + ' — change le pilier pour modifier.';
        if (profile !== 'sbire' && key === min) return 'Pilier mineur verrouillé à 4 — change le pilier pour modifier.';
        if (step > 0) {
            const budgetMsg = budgetBlockReason(key, step);
            if (budgetMsg) return budgetMsg;
            return 'Plafond de création : 3 (Livre I §3).';
        }
        return 'Minimum 1.';
    }

    // Budget total (Livre I §3) : un + qui ferait dépasser le total permis
    // est bloqué avec la même bulle d'avertissement (retire 1 pt ailleurs).
    // Magie hors budget : jamais bloquée ici (plafond 5 seul).
    function budgetBlockReason(key, step) {
        if (step <= 0 || !key || key === 'mag') return null;
        const profile = profileSelect ? profileSelect.value : 'hero';
        const valOf = (k) => parseInt(attrInputs[k] ? attrInputs[k].value : '0') || 0;
        if (profile === 'sbire') {
            const total = CORE8.reduce((s, k) => s + valOf(k), 0);
            if (total + step > 12) return 'Budget plein : ' + total + '/12 (Sbire, Livre I §3). Retire 1 point ailleurs pour ajouter ici.';
            return null;
        }
        const maj = pillarMajorSelect ? pillarMajorSelect.value : null;
        const min = pillarMinorSelect ? pillarMinorSelect.value : null;
        if (key === maj || key === min) return null;
        const magVal = valOf('mag');
        const secBudget = magVal > 0 ? 17 : 15;
        const othersSum = CORE8.filter(k => k !== maj && k !== min).reduce((s, k) => s + valOf(k), 0);
        if (othersSum + step > secBudget) {
            return 'Budget plein : ' + othersSum + '/' + secBudget + ' en secondaires'
                + (magVal > 0 ? ' (Éveil)' : ' (17 si Magie éveillée)')
                + '. Retire 1 point ailleurs pour ajouter ici.';
        }
        return null;
    }

    // Survol des boutons + : le title natif prévient avant le clic quand
    // le budget est déjà plein (complète la bulle affichée au clic bloqué).
    function refreshStepperTitles() {
        document.querySelectorAll('.stepper-btn[data-step="1"]').forEach(btn => {
            const input = document.getElementById(btn.getAttribute('data-target'));
            if (!input) return;
            const key = Object.keys(attrInputs).find(k => attrInputs[k] === input);
            const msg = budgetBlockReason(key, 1);
            if (msg) btn.title = msg;
            else btn.removeAttribute('title');
        });
    }

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
                if (nextVal === current) {
                    showStepBlockTip(btn, stepBlockReason(input, step));
                    return;
                }
                const key = Object.keys(attrInputs).find(k => attrInputs[k] === input);
                const budgetMsg = budgetBlockReason(key, step);
                if (budgetMsg) {
                    showStepBlockTip(btn, budgetMsg);
                    refreshStepperTitles();
                    return;
                }
                input.value = nextVal;
                updateCharacterCalculations();
            }
        });
    });

    // Skill checkboxes (plafond = ÉRU effective, Livre I Étape 5) : une case
    // cochée au-delà du quota est refusée avec la bulle d'avertissement.
    document.querySelectorAll('.skill-check').forEach(cb => {
        cb.addEventListener('change', () => {
            const eru = parseInt(attrInputs.eru.value) || 1;
            const max = Math.max(1, eru);
            if (cb.checked && document.querySelectorAll('.skill-check:checked').length > max) {
                cb.checked = false;
                const n = document.querySelectorAll('.skill-check:checked').length;
                showStepBlockTip(cb, 'Entraînements gratuits pleins : ' + n + '/' + max + ' (Érudition). Décoche 1 case ailleurs pour en cocher ici. +1 ÉRU = +1 gratuit.');
                updateSkillsLimit(max);
                return;
            }
            updateCharacterCalculations();
        });
    });
    // Sauvegarde quand la saisie est terminée (blur / Entrée / sélection),
    // jamais pendant la frappe : flush immédiat du debounce en attente.
    // Enregistré après les handlers ci-dessus pour purger leur timer.
    for (const el of [nameInput, ageInput, sexeSelect, mondeInput, ancrageInput, visionInput, ouieInput, enfanceSelect, metierSelect, evenementSelect, giftSelect, raceSelect, profileSelect, pillarMajorSelect, pillarMinorSelect]) {
        if (el) el.addEventListener('change', flushSync);
    }
    document.querySelectorAll('.skill-check').forEach(cb => cb.addEventListener('change', flushSync));

    // Sens : tirage 1d6 clampé au minimum racial (Livre I Étape 4).
    function clampSenses() {
        const min = RACE_DATA[currentRace()].sensMin;
        for (const inp of [visionInput, ouieInput]) {
            if (!inp) continue;
            let v = parseInt(inp.value) || min;
            inp.value = Math.min(6, Math.max(min, v));
            inp.setAttribute('min', min);
        }
        const sensNote = document.getElementById('csSensNote');
        if (sensNote) sensNote.textContent = 'Min racial : ' + min + ' (1d6, Livre I Étape 4)';
    }
    const btnVision = document.getElementById('csBtnVision');
    const btnOuie = document.getElementById('csBtnOuie');
    if (btnVision && visionInput) btnVision.addEventListener('click', () => {
        visionInput.value = Math.max(RACE_DATA[currentRace()].sensMin, 1 + Math.floor(Math.random() * 6));
        updateCharacterCalculations();
    });
    if (btnOuie && ouieInput) btnOuie.addEventListener('click', () => {
        ouieInput.value = Math.max(RACE_DATA[currentRace()].sensMin, 1 + Math.floor(Math.random() * 6));
        updateCharacterCalculations();
    });

    // Tirages d'historique (Livre VII).
    function bindHistoryRoll(btnId, selectEl, sides) {
        const btn = document.getElementById(btnId);
        if (btn && selectEl) btn.addEventListener('click', () => {
            selectEl.value = String(1 + Math.floor(Math.random() * sides));
            updateCharacterCalculations();
        });
    }
    bindHistoryRoll('csBtnEnfance', enfanceSelect, 6);
    bindHistoryRoll('csBtnMetier', metierSelect, 12);
    bindHistoryRoll('csBtnEvenement', evenementSelect, 12);

    // Peuple les selects statiques (piliers, historique).
    function populateStaticSelects() {
        for (const sel of [pillarMajorSelect, pillarMinorSelect]) {
            if (!sel || sel.options.length > 0) continue;
            for (const k of CORE8) {
                const opt = document.createElement('option');
                opt.value = k;
                opt.textContent = ATTR_LABELS[k];
                sel.appendChild(opt);
            }
        }
        if (pillarMajorSelect) pillarMajorSelect.value = 'for';
        if (pillarMinorSelect) pillarMinorSelect.value = 'con';
        refreshPillarOptions();
        syncPillarPrev();
        if (enfanceSelect && enfanceSelect.options.length === 0) {
            ENFANCES.forEach((e, i) => {
                const opt = document.createElement('option');
                opt.value = String(i + 1);
                opt.textContent = (i + 1) + '. ' + e.n;
                enfanceSelect.appendChild(opt);
            });
        }
        if (metierSelect && metierSelect.options.length === 0) {
            METIERS.forEach((m, i) => {
                const opt = document.createElement('option');
                opt.value = String(i + 1);
                opt.textContent = (i + 1) + '. ' + m.n;
                metierSelect.appendChild(opt);
            });
        }
        if (evenementSelect && evenementSelect.options.length === 0) {
            const none = document.createElement('option');
            none.value = '0';
            none.textContent = 'Aucun (optionnel)';
            evenementSelect.appendChild(none);
            EVENEMENTS.forEach((e, i) => {
                const opt = document.createElement('option');
                opt.value = String(i + 1);
                opt.textContent = (i + 1) + '. ' + e.n;
                evenementSelect.appendChild(opt);
            });
        }
    }

    function collectSheetData() {
        const data = {
            v: 2,
            name: nameInput.value,
            age: ageInput ? ageInput.value : '',
            sexe: sexeSelect ? sexeSelect.value : '',
            monde: mondeInput ? mondeInput.value : '',
            race: currentRace(),
            gift: giftSelect ? parseInt(giftSelect.value) || 0 : 0,
            profile: profileSelect.value,
            pillarMajor: pillarMajorSelect ? pillarMajorSelect.value : 'for',
            pillarMinor: pillarMinorSelect ? pillarMinorSelect.value : 'con',
            attributes: {},
            vision: visionInput ? visionInput.value : '',
            ouie: ouieInput ? ouieInput.value : '',
            enfance: enfanceSelect ? enfanceSelect.value : '',
            metier: metierSelect ? metierSelect.value : '',
            evenement: evenementSelect ? evenementSelect.value : '',
            ancrage: ancrageInput ? ancrageInput.value : ''
        };
        for (const [k, el] of Object.entries(attrInputs)) {
            data.attributes[k] = el.value;
        }
        data.skills = Array.from(document.querySelectorAll('.skill-check:checked')).map(cb => cb.id);
        return data;
    }

    // opts.preserveFocused : ne jamais écraser le champ en cours d'édition
    // (voies distantes uniquement). La frappe locale gagne toujours.
    function applySheetData(data, opts) {
        const focused = (opts && opts.preserveFocused) ? document.activeElement : null;
        function setVal(el, v) { if (el && el !== focused) el.value = v; }
        if (data.name !== undefined) setVal(nameInput, data.name);
        if (data.age !== undefined) setVal(ageInput, data.age);
        if (data.sexe !== undefined) setVal(sexeSelect, data.sexe);
        if (data.monde !== undefined) setVal(mondeInput, data.monde);
        if (data.race) setVal(raceSelect, resolveRace(data.race));
        if (data.profile) setVal(profileSelect, data.profile);
        if (data.pillarMajor) setVal(pillarMajorSelect, data.pillarMajor);
        if (data.pillarMinor) setVal(pillarMinorSelect, data.pillarMinor);
        refreshPillarOptions();
        syncPillarPrev();
        if (data.attributes) {
            for (const [k, v] of Object.entries(data.attributes)) {
                if (attrInputs[k]) setVal(attrInputs[k], v);
            }
        }
        updateGiftOptions(false);
        if (data.gift === 0 || data.gift) setVal(giftSelect, String(data.gift));
        if (data.vision !== undefined) setVal(visionInput, data.vision);
        if (data.ouie !== undefined) setVal(ouieInput, data.ouie);
        if (data.enfance) setVal(enfanceSelect, String(data.enfance));
        if (data.metier) setVal(metierSelect, String(data.metier));
        if (data.evenement === 0 || data.evenement) setVal(evenementSelect, String(data.evenement));
        if (data.ancrage !== undefined) setVal(ancrageInput, data.ancrage);
        document.querySelectorAll('.skill-check').forEach(cb => {
            if (cb === focused) return;
            const base = cb.id.replace(/-\d$/, '');
            cb.checked = !!(data.skills && (data.skills.includes(cb.id)
                // Compat anciennes sauvegardes (1 case par compétence = niveau 1).
                || (data.skills.includes(base) && cb.id.endsWith('-1'))));
        });
        clampSenses();
        updateCharacterCalculations();
    }

    function histTxt(arr, v) { const o = arr[parseInt(v) - 1]; return o ? (o.n + ' — ' + o.d) : '—'; }

    function buildSummaryText() {
        const d = collectSheetData();
        const rdata = RACE_DATA[d.race];
        const skillLevels = getSkillLevels();
        const trainedSkills = Object.entries(skillLevels).map(([name, lvl]) => {
            const info = skillBaseInfo(name);
            const ext = {};
            for (const k of Object.keys(d.attributes)) ext[k] = +d.attributes[k] || 0;
            ext.vision = +d.vision || 3;
            ext.ouie = +d.ouie || 3;
            let score = skillBaseScore(info.attrs, ext);
            if (info.mag && ext.mag > 0) score = Math.max(score, ext.mag);
            return `${name} +${lvl} (${stepDie(dieForValue(score), lvl)})`;
        }).join(', ') || 'Aucune';
        const enfTxt = d.enfance ? histTxt(ENFANCES, d.enfance) : '—';
        const metTxt = d.metier ? histTxt(METIERS, d.metier) : '—';
        const eveTxt = (d.evenement && d.evenement !== '0') ? histTxt(EVENEMENTS, d.evenement) : 'Aucun';
        return `=== FICHE DE PERSONNAGE RP (Codex v2.4) ===\n` +
            `Nom : ${d.name || 'Héros sans nom'}${d.age ? ' | Âge : ' + d.age : ''}${d.sexe ? ' | Sexe : ' + d.sexe : ''}\n` +
            `Espèce : ${rdata.label} | Don racial : ${rdata.gifts[d.gift]} (${rdata.giftNotes[d.gift]})\n` +
            `Monde d'origine : ${d.monde || '—'} | Profil : ${profileSelect.options[profileSelect.selectedIndex].text}\n` +
            `Piliers : ${d.pillarMajor.toUpperCase()} / ${d.pillarMinor.toUpperCase()}\n\n` +
            `-- ATTRIBUTS (dés sans entraînement, Livre II §6.1) --\n` +
            `FOR: ${d.attributes.for} (${dieForValue(+d.attributes.for)}) | AGI: ${d.attributes.agi} (${dieForValue(+d.attributes.agi)}) | CON: ${d.attributes.con} (${dieForValue(+d.attributes.con)}) | RAP: ${d.attributes.rap} (${dieForValue(+d.attributes.rap)})\n` +
            `INT: ${d.attributes.int} (${dieForValue(+d.attributes.int)}) | ÉRU: ${d.attributes.eru} (${dieForValue(+d.attributes.eru)}) | CHA: ${d.attributes.cha} (${dieForValue(+d.attributes.cha)}) | INS: ${d.attributes.ins} (${dieForValue(+d.attributes.ins)})\n` +
            `MAGIE: ${d.attributes.mag} | Vision: ${d.vision} | Ouïe: ${d.ouie}\n\n` +
            `-- JAUGES VITALES & TACTIQUES (Livre I §4) --\n` +
            `PA/tour : ${vitals.pa.textContent} (= Max(AGI,INT) + RAP + Min)\n` +
            `Encaissement : ${vitals.cap.textContent} (CON×2 + don) | Maximum Létal : ${vitals.max.textContent} (CON×5)\n` +
            `Essoufflement : ${vitals.ess.textContent} | Initiative : ${document.getElementById('vitalInit').textContent} | Miracle : ${document.getElementById('vitalMiracle').textContent}\n\n` +
            `-- HISTORIQUE (Livre VII, Étape 1bis) --\n` +
            `Enfance : ${enfTxt}\nMétier : ${metTxt}\nÉvénement : ${eveTxt}\nAncrage : ${d.ancrage || '—'}\n\n` +
            `Compétences Entraînées (gratuites = ÉRU, +1 palier) :\n${trainedSkills}\n` +
            `==============================`;
    }

    // Action buttons
    const btnCopy = document.getElementById('csBtnCopy');
    const btnSave = document.getElementById('csBtnSave');
    const btnLoad = document.getElementById('csBtnLoad');
    const btnReset = document.getElementById('csBtnReset');

    if (btnCopy) {
        btnCopy.addEventListener('click', () => {
            const summary = buildSummaryText();
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(summary).then(() => {
                    showToast('Fiche de personnage copiée dans le presse-papier !');
                }).catch(() => showToast('Copie impossible dans ce navigateur.'));
            } else {
                showToast('Presse-papier non disponible (contexte non sécurisé). Utilisez Export JSON.');
            }
        });
    }

    const btnExport = document.getElementById('csBtnExport');
    if (btnExport) {
        btnExport.addEventListener('click', () => {
            const data = collectSheetData();
            const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
            const a = document.createElement('a');
            const safeName = (data.name || 'heros').toLowerCase().replace(/[^a-z0-9àâäéèêëîïôöùûüç_-]+/gi, '_');
            a.href = URL.createObjectURL(blob);
            a.download = 'fiche_' + safeName + '.json';
            document.body.appendChild(a);
            a.click();
            setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 500);
            showToast('Fiche exportée en JSON !');
        });
    }

    const fileImport = document.getElementById('csFileImport');
    const btnImport = document.getElementById('csBtnImport');
    if (btnImport && fileImport) {
        btnImport.addEventListener('click', () => fileImport.click());
        fileImport.addEventListener('change', () => {
            const f = fileImport.files && fileImport.files[0];
            if (!f) return;
            const reader = new FileReader();
            reader.onload = () => {
                try {
                    const data = JSON.parse(reader.result);
                    applySheetData(data);
                    showToast('Fiche importée avec succès !');
                } catch (e) {
                    showToast('Fichier JSON invalide.');
                }
                fileImport.value = '';
            };
            reader.readAsText(f);
        });
    }

    if (btnSave) {
        btnSave.addEventListener('click', () => {
            if (syncTimer) { clearTimeout(syncTimer); syncTimer = null; }
            doSync();
            showToast('Fiche sauvegardée dans le navigateur (localStorage) !');
        });
    }

    if (btnLoad) {
        btnLoad.addEventListener('click', () => {
            let raw = null;
            try { raw = localStorage.getItem(SYNC_KEY); } catch (e) { raw = null; }
            if (!raw) {
                showToast('Aucune fiche sauvegardée trouvée.');
                return;
            }
            try {
                suppressSync = true;
                applySheetData(JSON.parse(raw));
                try { lastSyncJson = JSON.stringify(collectSheetData()); } catch (e) { /* ignore */ }
                suppressSync = false;
                showToast('Fiche rechargée avec succès !');
            } catch (e) {
                suppressSync = false;
                showToast('Erreur lors du chargement des données.');
            }
        });
    }

    // Choix canon de Roger (Livre I §6) : Mains Nues ×2, Esquive ×1.
    const ROGER_DEFAULT_SKILLS = ['skMainsNues-1', 'skMainsNues-2', 'skEsquive-1'];
    function applyRogerDefaultSkills() {
        document.querySelectorAll('.skill-check').forEach(cb => {
            cb.checked = ROGER_DEFAULT_SKILLS.includes(cb.id);
        });
    }

    if (btnReset) {
        btnReset.addEventListener('click', () => {
            if (confirm('Réinitialiser la fiche de personnage aux valeurs par défaut ?')) {
                nameInput.value = 'Roger de Vardis';
                if (ageInput) ageInput.value = '27';
                if (sexeSelect) sexeSelect.value = 'M';
                if (mondeInput) mondeInput.value = 'Frontière Chaude';
                raceSelect.value = 'humain';
                profileSelect.value = 'hero';
                if (pillarMajorSelect) pillarMajorSelect.value = 'for';
                if (pillarMinorSelect) pillarMinorSelect.value = 'con';
                refreshPillarOptions();
                syncPillarPrev();
                updateGiftOptions(false);
                if (giftSelect) giftSelect.value = '3';
                attrInputs.for.value = 5;
                attrInputs.agi.value = 3;
                attrInputs.con.value = 4;
                attrInputs.rap.value = 3;
                attrInputs.int.value = 2;
                attrInputs.eru.value = 3;
                attrInputs.cha.value = 1;
                attrInputs.ins.value = 3;
                attrInputs.mag.value = 0;
                if (visionInput) visionInput.value = '4';
                if (ouieInput) ouieInput.value = '3';
                if (enfanceSelect) enfanceSelect.value = '2';
                if (metierSelect) metierSelect.value = '1';
                if (evenementSelect) evenementSelect.value = '2';
                if (ancrageInput) ancrageInput.value = '';
                applyRogerDefaultSkills();
                clampSenses();
                updateCharacterCalculations();
                showToast('Fiche réinitialisée (Héros PJ valide).');
            }
        });
    }

    // Peuplement initial des selects (piliers, dons, historique), puis premier calcul.
    populateStaticSelects();
    ensureAttrDiceBadges();
    attachSheetDropdowns(); // dons + enfance/métier/événement : menus descriptifs
    updateGiftOptions(false);
    if (giftSelect) giftSelect.value = '3'; // Réseau Colonial : défaut = exemple Roger canon
    applyRogerDefaultSkills(); // Tir, Esquive, Médecine (écrasé par la sauvegarde éventuelle ci-dessous)
    if (pillarsRow) pillarsRow.style.display = (profileSelect.value === 'sbire') ? 'none' : '';
    profileSelect.addEventListener('change', () => {
        if (pillarsRow) pillarsRow.style.display = (profileSelect.value === 'sbire') ? 'none' : '';
    });
    clampSenses();
    // Hydratation initiale : roster multi-onglets (migration depuis la clé
    // mono-fiche). Le formulaire part de l'onglet actif partagé, pas des défauts.
    ensureTabsBar();
    suppressSync = true;
    try {
        const stored = readStoredRoster();
        if (stored && stored.tabs.length) {
            roster = stored;
            localIsFresh = false;
        } else {
            let migrated = null;
            try {
                const raw = localStorage.getItem(SYNC_KEY);
                if (raw) migrated = JSON.parse(raw);
            } catch (e) { migrated = null; }
            if (migrated && typeof migrated === 'object' && migrated.attributes) {
                roster = { tabs: [{ id: newTabId(), label: '', customLabel: false, updatedAt: Date.now(), data: migrated }], lastActiveId: null };
                localIsFresh = false;
            } else {
                localIsFresh = true;
                roster = { tabs: [{ id: newTabId(), label: '', customLabel: false, updatedAt: Date.now(), data: collectSheetData() }], lastActiveId: null };
            }
            persistRoster();
        }
        if (!getActiveTab() && roster.tabs.length) setActiveTabId(roster.tabs[0].id);
        const activeTab = getActiveTab();
        if (activeTab && activeTab.data) applySheetData(activeTab.data);
    } catch (e) { /* defauts conserves */ }
    suppressSync = false;
    renderTabs();
    updateCharacterCalculations();
    try { lastSyncJson = JSON.stringify(collectSheetData()); lastRosterJson = JSON.stringify(roster); } catch (e) { /* ignore */ }
    // Reception temps reel : BroadcastChannel (instantane) + event storage (repli).
    if (syncChannel) {
        syncChannel.onmessage = (ev) => {
            const msg = ev && ev.data;
            if (!msg || msg.source === syncId) return;
            // Données d'abord : applyRemoteMeta persiste le roster — il doit
            // voir les données fraîches, sinon il réécrit l'ancien état et
            // l'écho écrase la frappe en cours dans l'autre fenêtre.
            if (msg.data && msg.tabId) applyRemoteSheet(msg.data, msg.tabId);
            else if (msg.data) applyRemoteSheet(msg.data, null); // compat ancien protocole
            if (msg.tabsMeta) applyRemoteMeta(msg);
        };
    }
    window.addEventListener('storage', (e) => {
        if (e.key === ROSTER_KEY && e.newValue) {
            if (e.newValue === lastRosterJson) return;
            let parsed = null;
            try { parsed = JSON.parse(e.newValue); } catch (err) { return; }
            if (!parsed || !Array.isArray(parsed.tabs) || !parsed.tabs.length) return;
            lastRosterJson = e.newValue;
            suppressSync = true;
            try {
                // Sauvegarde notre version de l'onglet actif : si l'écriture
                // reçue est périmée (autre fenêtre en retard), on la restaure.
                const mine = getActiveTab();
                const myTab = mine ? { id: mine.id, label: mine.label, customLabel: mine.customLabel, updatedAt: mine.updatedAt, data: mine.data } : null;
                roster = parsed;
                if (!getActiveTab()) setActiveTabId(roster.tabs[0].id);
                const t = getActiveTab();
                if (myTab && t && t.id === myTab.id && revOf(t.data) < revOf(myTab.data)) {
                    const idx = roster.tabs.findIndex(x => x.id === myTab.id);
                    if (idx >= 0) roster.tabs[idx] = myTab;
                }
                const t2 = getActiveTab();
                if (t2 && t2.data) {
                    const incoming = contentJson(t2.data);
                    // Si l'onglet affiché n'a pas changé, ne pas réécrire le
                    // formulaire : réassigner les <input> déplace le curseur
                    // et mange la frappe en cours.
                    if (incoming && incoming !== lastSyncJson) {
                        applySheetData(t2.data, { preserveFocused: true });
                        try { lastSyncJson = JSON.stringify(collectSheetData()); } catch (err2) { /* ignore */ }
                    }
                }
                renderTabs();
            } finally { suppressSync = false; }
            return;
        }
        // Branche SYNC_KEY volontairement supprimée : chaque persist écrit
        // aussi ROSTER_KEY (traité ci-dessus). L'ancien écho SYNC_KEY
        // réappliquait un état périmé et écrasait la frappe en cours.
        // SYNC_KEY reste écrit pour le bouton « Charger la Sauvegarde ».
    });
    // Connexion : transfert du local vers le serveur, puis autosave.
    // Déconnexion : retour au local seul.
    window.addEventListener('codex:auth:login', () => { cloudBootDone = false; transferLocalToServer(); });
    window.addEventListener('codex:auth:logout', () => {
        cloudBootDone = false;
        if (cloudTimer) { clearTimeout(cloudTimer); cloudTimer = null; }
        updateCloudBadge('local');
    });
    if (isLoggedIn()) transferLocalToServer();
    // Pastille "Sync auto" a cote du badge de budget (les deux pages, meme moteur).
    try {
        const ptsLeft = document.getElementById('csPointsLeft');
        if (ptsLeft && ptsLeft.parentElement && !document.getElementById('csSyncState')) {
            const badge = document.createElement('span');
            badge.id = 'csSyncState';
            badge.className = 'pill-badge cyan';
            badge.title = 'Modifs propagees en direct entre la fiche du Livre IX et la fenetre detachee.';
            badge.textContent = 'Sync auto';
            ptsLeft.parentElement.appendChild(badge);
        }
    } catch (e) { /* ignore */ }
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

    // Die definitions (repli intégré) ; surcouche Livre II : repères de
    // seuils + échelle canonique, mêmes clés que le <select> statique.
    function parseNdM(key) {
        const m = /^(\d*)d(\d+)\s*([+-]\s*\d+)?$/.exec(String(key || ''));
        if (!m) return null;
        const mod = m[3] ? parseInt(m[3].replace(/\s+/g, ''), 10) : 0;
        const count = parseInt(m[1] || '1', 10), sides = parseInt(m[2], 10);
        const k = (count === 1 ? 'd' + sides : count + 'd' + sides) + (mod ? (mod > 0 ? '+' + mod : mod) : '');
        return { key: k, count: count, sides: sides, mod: mod, label: k };
    }
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
        '2d6': { count: 2, sides: 6, mod: 0, label: '2d6' },
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

    // Surcouche Livre II (RP-082) : paliers + critiques canoniques.
    // Critiques : 13 paliers canoniques, valeurs intermédiaires rabattues
    // sur le palier inférieur (le livre ne définit que ces 13 résultats).
    (function overlayDiceCanon() {
        const M = CodexData.l2 && CodexData.l2.mecaniques;
        if (M && M.seuils) {
            (M.seuils.detail || []).forEach(s => {
                const p = parseNdM(s.tirage_repere);
                if (p) dieSteps[p.key] = { count: p.count, sides: p.sides, mod: p.mod, label: p.label };
            });
        }
        if (CodexData.l2 && CodexData.l2.echelle_des) {
            CodexData.l2.echelle_des.forEach(dd => {
                const p = parseNdM(dd);
                if (p && !dieSteps[p.key]) dieSteps[p.key] = { count: p.count, sides: p.sides, mod: p.mod, label: p.label };
            });
        }
        const P = M && M.critique && M.critique.par_resultat;
        if (P) {
            const keys = Object.keys(P).map(Number).sort((a, b) => a - b);
            for (let n = 1; n <= 24; n++) {
                const below = keys.filter(k => k <= n).pop();
                const e = P[String(below === undefined ? keys[0] : below)];
                if (e) critTable[n] = { off: e.offensif, def: e.defensif, soc: e.social, util: e.utilitaire };
            }
        }
    })();

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
            dieSubText.textContent = `Dés [${rolls.join(', ')}] ${cfg.mod !== 0 ? (cfg.mod > 0 ? '+' + cfg.mod : cfg.mod) : ''} + ${paBonus} PA + ${otherMod} situationnel (jamais mod carac)`;

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

    // Source unique : data/livre_2.json (mecaniques.defi), repli valeurs codées.
    function defiParams() {
        const D = CodexData.l2 && CodexData.l2.mecaniques && CodexData.l2.mecaniques.defi;
        return {
            etapesMin: (D && D.etapes && D.etapes.min) || 3,
            etapesMax: (D && D.etapes && D.etapes.max) || 7,
            rattrapagesMax: (D && D.rattrapages && D.rattrapages.max) || 3,
            minimumValide: (D && D.minimum_valide) || 2
        };
    }

    function renderSteps() {
        const P0 = defiParams();
        const count = Math.min(P0.etapesMax, Math.max(P0.etapesMin, parseInt(stepCountSelect.value) || 4));
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
                    if (step.retries >= defiParams().rattrapagesMax) {
                        showToast('Rattrapages épuisés (max ' + defiParams().rattrapagesMax + ', Livre II §9).');
                        return;
                    }
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

        // Formule canon : Récompense = (Réussies × 2) − Rattrapages, plancher si validé.
        // Livre II §9 + Livre I §5 : récompense = cases de progression (piste pleine = +1 XP lié).
        const P = defiParams();
        let reward = (successes * 2) - totalRetries;
        if (successes > 0 && reward < P.minimumValide) reward = P.minimumValide;
        if (successes === 0) reward = 0;

        rewardBadge.textContent = `${reward} case(s) de progression (Livre I §5)`;

        if (pending > 0) {
            statusDisplay.textContent = `Défi en cours (${successes}/${challengeData.length} validées, ${pending} restantes)`;
            statusDisplay.style.color = 'var(--text-secondary)';
        } else if (failures === 0) {
            statusDisplay.textContent = `CHALLENGE ACCOMPLI AVEC BRIO ! Répartissez ${reward} cases dans les compétences utilisées (piste pleine = +1 XP lié).`;
            statusDisplay.style.color = '#34d399';
        } else if (successes >= Math.ceil(challengeData.length / 2)) {
            statusDisplay.textContent = `CHALLENGE VALIDÉ SUR LE FIL (${failures} échec(s)). Répartissez ${reward} cases (Livre I §5).`;
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

    // Tiers du simulateur (repli intégré) ; source : data/livre_10.json.
    const tiers = (CodexData.l10 && CodexData.l10.mecaniques && CodexData.l10.mecaniques.simulateur_chronotrames) || {
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
