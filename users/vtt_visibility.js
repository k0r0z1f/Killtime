/* ==========================================================================
   CODEX UNIVERSEL — VTT VISIBILITY FILTER (brouillard de guerre asymétrique)
   users/vtt_visibility.js — zéro dépendance, testable via `node --test`.

   Miroir serveur des règles FogOfWarSystem (C#, Livre VI §25.4) :
   - KT voit à 360° : AUCUN cône, aucune direction de vision. Le cap (yaw)
     est ignoré (conservé dans les paquets pour parité, jamais testé).
   - Portée = portée d'ambiance de la room (courte 6 / longue 12, imposée
     par le GM via state_sync.sightRange), JAMAIS les stats du personnage
     (Vision = jets de compétences : Observation, Écoute).
   - Murs (Cover Full = 2) entre observateur et cible => invisible.
     Approximation serveur : échantillonnage du segment en coordonnées
     axiales->pixels (même formule que HexCoordinates.ToWorldPosition,
     rayon 1) ; toute case intermédiaire Full bloque. Les extrémités sont
     exclues (on peut voir depuis/vers une case de mur ; le contact voit
     toujours). Fail-closed : en cas de doute on MASQUE (anti map-hack).
   - Contact (distance <= 1) : toujours visible.
   - Thermique : voit jusqu'à Portee/2 (arrondi sup.) même derrière un Full.
   ========================================================================== */
'use strict';

const COVER_FULL = 2;
const SHORT_SIGHT_RANGE = 6;
const LONG_SIGHT_RANGE = 12;
// Conservé pour isInCone (helper pur testé) : le 360° KT rend le cône inopérant en jeu.
const DEFAULT_HALF_ANGLE_DEG = 60;

function clampSightRange(v, fallback) {
    v = parseInt(v, 10);
    if (!Number.isFinite(v)) return fallback;
    return Math.max(1, Math.min(32, v));
}

// Distance hexagonale axiale (q + r + s = 0).
function hexDistance(aq, ar, bq, br) {
    const as = -aq - ar;
    const bs = -bq - br;
    return (Math.abs(aq - bq) + Math.abs(ar - br) + Math.abs(as - bs)) / 2;
}

// Axial -> pixels (miroir de HexCoordinates.ToWorldPosition, rayon 1).
function axialToPixel(q, r) {
    const SQRT3 = Math.sqrt(3);
    return { x: SQRT3 * q + (SQRT3 / 2) * r, z: 1.5 * r };
}

function normAngle360(deg) {
    let d = deg % 360;
    if (d < 0) d += 360;
    return d;
}

// Écart signé [-180, 180] entre deux caps.
function deltaAngle(a, b) {
    let d = normAngle360(b) - normAngle360(a);
    if (d > 180) d -= 360;
    if (d < -180) d += 360;
    return d;
}

function isInCone(obsQ, obsR, obsYawDeg, tgtQ, tgtR, panoramic) {
    if (panoramic) return true;
    if (obsQ === tgtQ && obsR === tgtR) return true;
    const o = axialToPixel(obsQ, obsR);
    const t = axialToPixel(tgtQ, tgtR);
    const dx = t.x - o.x;
    const dz = t.z - o.z;
    if (dx === 0 && dz === 0) return true;
    const targetYaw = Math.atan2(dx, dz) * 180 / Math.PI;
    return Math.abs(deltaAngle(obsYawDeg || 0, targetYaw)) <= DEFAULT_HALF_ANGLE_DEG + 1e-3;
}

// Cases hexagonales traversées par le segment (échantillonnage fin, extrémités
// exclues). Retourne des clés "q,r".
function hexesAlongSegment(aq, ar, bq, br) {
    const a = axialToPixel(aq, ar);
    const b = axialToPixel(bq, br);
    const distPx = Math.hypot(b.x - a.x, b.z - a.z);
    const steps = Math.max(1, Math.ceil(distPx / 0.25));
    const out = [];
    const seen = new Set();
    const SQRT3 = Math.sqrt(3);
    for (let i = 1; i < steps; i++) {
        const t = i / steps;
        const x = a.x + (b.x - a.x) * t;
        const z = a.z + (b.z - a.z) * t;
        // Inverse approx monde -> axial (miroir de TacticalHexGrid).
        const qFrac = ((SQRT3 / 3) * x - (1 / 3) * z);
        const rFrac = ((2 / 3) * z);
        const sFrac = -qFrac - rFrac;
        let q = Math.round(qFrac);
        let r = Math.round(rFrac);
        const s = Math.round(sFrac);
        const qDiff = Math.abs(q - qFrac);
        const rDiff = Math.abs(r - rFrac);
        const sDiff = Math.abs(s - sFrac);
        if (qDiff > rDiff && qDiff > sDiff) q = -r - s;
        else if (rDiff > sDiff) r = -q - s;
        if ((q === aq && r === ar) || (q === bq && r === br)) continue;
        const key = q + ',' + r;
        if (!seen.has(key)) {
            seen.add(key);
            out.push({ q, r });
        }
    }
    return out;
}

// covers : Map("q,r" -> coverInt) ou objet simple. Seul Full (=2) bloque.
function isBlockedByWall(aq, ar, bq, br, covers) {
    if (!covers) return false;
    const cells = hexesAlongSegment(aq, ar, bq, br);
    for (const c of cells) {
        const key = c.q + ',' + c.r;
        let v;
        if (typeof covers.get === 'function') v = covers.get(key);
        else v = covers[key];
        if (v === COVER_FULL || v === '2' || v === 2) return true;
    }
    return false;
}

/**
 * Test complet côté serveur : 360° (le cap est ignoré), portée d'ambiance,
 * murs Full. obs = { q, r, vision (portée d'ambiance), thermal }
 * tgt = { q, r }, covers = Map/Objet des murs Full.
 */
function isCellVisible(obs, tgt, covers) {
    if (!obs || !tgt) return false;
    const range = clampSightRange(obs.vision, LONG_SIGHT_RANGE);
    const dist = hexDistance(obs.q, obs.r, tgt.q, tgt.r);
    if (dist > range) return false;
    if (dist <= 1) return true; // contact : toujours visible.
    const thermalReach = obs.thermal ? Math.max(1, Math.ceil(range / 2)) : -1;
    if (isBlockedByWall(obs.q, obs.r, tgt.q, tgt.r, covers) && dist > thermalReach) return false;
    return true;
}

/**
 * Vrai si la cible est dans le champ d'AU MOINS un avatar de la liste.
 * observers : [{ q, r, yaw, vision, pano, thermal }]
 */
function isVisibleToAny(observers, tgt, covers) {
    if (!Array.isArray(observers) || observers.length === 0) return false;
    for (const o of observers) {
        if (isCellVisible(o, tgt, covers)) return true;
    }
    return false;
}

module.exports = {
    COVER_FULL,
    SHORT_SIGHT_RANGE,
    LONG_SIGHT_RANGE,
    DEFAULT_HALF_ANGLE_DEG,
    clampSightRange,
    hexDistance,
    axialToPixel,
    deltaAngle,
    isInCone,
    hexesAlongSegment,
    isBlockedByWall,
    isCellVisible,
    isVisibleToAny,
};
