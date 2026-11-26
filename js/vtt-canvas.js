/* ==========================================================================
   VTT CANVAS — Moteur 2D multi-calques (Hex pointy-top + Carré, 1 case = 1,5 m)
   js/vtt-canvas.js
   Parité géométrique stricte avec users/vtt_visibility.js :
     x = cellSize * (sqrt(3)*q + (sqrt(3)/2)*r), y = cellSize * 1.5 * r
   ========================================================================== */
(function () {
  'use strict';

  const SQRT3 = Math.sqrt(3);
  const FACTION_COLORS = {
    pj: '--arcane-cyan',
    ally: '--matrix-emerald',
    npc: '--temporal-amber',
    enemy: '--vital-ruby',
    vehicle: '--void-violet'
  };
  const ZONE_STYLE = {
    fire: { fill: 'rgba(244,63,94,0.28)', stroke: '#f43f5e', icon: '🔥' },
    smoke: { fill: 'rgba(148,163,184,0.38)', stroke: '#94a3b8', icon: '💨' },
    gas: { fill: 'rgba(16,185,129,0.28)', stroke: '#10b981', icon: '☣️' },
    stasis: { fill: 'rgba(139,92,246,0.28)', stroke: '#8b5cf6', icon: '⏳' },
    difficult: { fill: 'rgba(245,158,11,0.24)', stroke: '#f59e0b', icon: '⛰️' }
  };

  function cssVar(name, fallback) {
    try {
      const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
      return v || fallback;
    } catch (e) { return fallback; }
  }

  class VTTCanvasEngine {
    constructor(canvas) {
      this.canvas = canvas;
      this.ctx = canvas.getContext('2d');
      this.vtt = null;
      this.scene = null;
      this.isGM = false;
      this.myUserId = null;
      this.selectedTokenId = null;

      this.tool = 'select'; // select | measure | ping | draw | zone | drop | wall
      this.templateMode = 'line'; // line | cone | blast1 | blast2 | blast3 | reach2
      this.drawColor = '#f59e0b';
      this.drawKind = 'free';
      this.zoneKind = 'smoke';
      this.wallKind = 'wall';

      this.cam = { x: 240, y: 140, zoom: 1 };
      this.hoverCell = null;
      this.imgCache = new Map();
      this.pings = [];

      // Interaction state
      this.dragPan = null;
      this.dragToken = null; // { token, startCell, waypoints: [], curCell }
      this.measurePts = null; // { aWorld, bWorld, aCell, bCell }
      this.drawStroke = null;
      this.wallDraft = null;
      this.spaceHeld = false;
      this.pinchState = null;

      this.dirty = true;
      this._raf = null;
      this.onSelectToken = null;
      this.onHoverCell = null;
      this.onOp = null;

      this.bindEvents();
      this.loop();
    }

    setState(vttState, opts) {
      this.vtt = vttState || null;
      if (opts) {
        if (opts.isGM !== undefined) this.isGM = !!opts.isGM;
        if (opts.userId !== undefined) this.myUserId = opts.userId;
      }
      const prevId = this.scene ? this.scene.id : null;
      this.scene = this.vtt && Array.isArray(this.vtt.scenes)
        ? (this.vtt.scenes.find((s) => s.id === this.vtt.activeSceneId) || this.vtt.scenes[0])
        : null;
      if (this.scene && this.scene.id !== prevId && !prevId) {
        this.fitToScene();
      }
      if (this.selectedTokenId && this.scene && !this.scene.tokens.some((t) => t.id === this.selectedTokenId)) {
        this.selectedTokenId = null;
        if (this.onSelectToken) this.onSelectToken(null);
      }
      this.dirty = true;
    }

    setTool(tool) {
      this.tool = tool || 'select';
      this.canvas.className = 'tool-' + this.tool;
      this.measurePts = null;
      this.drawStroke = null;
      this.wallDraft = null;
      this.dirty = true;
    }

    addPing(p) {
      this.pings.push({
        x: p.x || 0,
        y: p.y || 0,
        q: p.q,
        r: p.r,
        color: p.color || '#38bdf8',
        label: p.label || '',
        born: performance.now(),
        duration: 2400
      });
      this.dirty = true;
    }

    focusOn(x, y, zoom) {
      const rect = this.canvas.getBoundingClientRect();
      if (Number.isFinite(zoom) && zoom > 0) this.cam.zoom = Math.max(0.25, Math.min(3, zoom));
      this.cam.x = rect.width / 2 - x * this.cam.zoom;
      this.cam.y = rect.height / 2 - y * this.cam.zoom;
      this.dirty = true;
    }

    fitToScene() {
      if (!this.scene) return;
      const rect = this.canvas.getBoundingClientRect();
      if (!rect.width || !rect.height) return;
      const sz = this.scene.cellSize || 54;
      const w = this.scene.gridType === 'hex' ? sz * SQRT3 * (this.scene.cols + 0.5) : sz * this.scene.cols;
      const h = this.scene.gridType === 'hex' ? sz * 1.5 * this.scene.rows + sz : sz * this.scene.rows;
      const zx = (rect.width - 60) / Math.max(200, w);
      const zy = (rect.height - 60) / Math.max(200, h);
      this.cam.zoom = Math.max(0.25, Math.min(1.6, Math.min(zx, zy)));
      this.cam.x = (rect.width - w * this.cam.zoom) / 2 + 20;
      this.cam.y = (rect.height - h * this.cam.zoom) / 2 + 20;
      this.dirty = true;
    }

    screenToWorld(sx, sy) {
      return {
        x: (sx - this.cam.x) / this.cam.zoom,
        y: (sy - this.cam.y) / this.cam.zoom
      };
    }

    cellToWorld(q, r) {
      const sc = this.scene;
      if (!sc) return { x: 0, y: 0 };
      const sz = sc.cellSize || 54;
      const ox = sc.offsetX || 0;
      const oy = sc.offsetY || 0;
      if (sc.gridType === 'square') {
        return { x: ox + (q + 0.5) * sz, y: oy + (r + 0.5) * sz };
      }
      return {
        x: ox + sz * (SQRT3 * q + (SQRT3 / 2) * r),
        y: oy + sz * (1.5 * r)
      };
    }

    worldToCell(wx, wy) {
      const sc = this.scene;
      if (!sc) return { q: 0, r: 0 };
      const sz = sc.cellSize || 54;
      const x = wx - (sc.offsetX || 0);
      const y = wy - (sc.offsetY || 0);
      if (sc.gridType === 'square') {
        return { q: Math.floor(x / sz), r: Math.floor(y / sz) };
      }
      const qFrac = ((SQRT3 / 3) * x - (1 / 3) * y) / sz;
      const rFrac = ((2 / 3) * y) / sz;
      const sFrac = -qFrac - rFrac;
      let q = Math.round(qFrac);
      let r = Math.round(rFrac);
      const s = Math.round(sFrac);
      const qd = Math.abs(q - qFrac);
      const rd = Math.abs(r - rFrac);
      const sd = Math.abs(s - sFrac);
      if (qd > rd && qd > sd) q = -r - s;
      else if (rd > sd) r = -q - s;
      return { q, r };
    }

    cellDistance(a, b) {
      if (!a || !b) return 0;
      if (this.scene && this.scene.gridType === 'square') {
        return Math.max(Math.abs(a.q - b.q), Math.abs(a.r - b.r));
      }
      const as = -a.q - a.r;
      const bs = -b.q - b.r;
      return Math.round((Math.abs(a.q - b.q) + Math.abs(a.r - b.r) + Math.abs(as - bs)) / 2);
    }

    traceCells(a, b) {
      const steps = Math.max(1, this.cellDistance(a, b));
      const wa = this.cellToWorld(a.q, a.r);
      const wb = this.cellToWorld(b.q, b.r);
      const out = [];
      const seen = new Set();
      for (let i = 0; i <= steps; i++) {
        const t = i / steps;
        const c = this.worldToCell(wa.x + (wb.x - wa.x) * t, wa.y + (wb.y - wa.y) * t);
        const k = c.q + ',' + c.r;
        if (!seen.has(k)) { seen.add(k); out.push(c); }
      }
      return out;
    }

    tokenAtWorld(wx, wy) {
      if (!this.scene) return null;
      const sz = this.scene.cellSize || 54;
      for (let i = this.scene.tokens.length - 1; i >= 0; i--) {
        const t = this.scene.tokens[i];
        const p = this.cellToWorld(t.q, t.r);
        const rad = sz * 0.44 * (t.size || 1);
        if (Math.hypot(wx - p.x, wy - p.y) <= rad) return t;
      }
      return null;
    }

    canControlToken(tok) {
      if (!tok) return false;
      if (this.isGM) return true;
      return this.myUserId && tok.ownerUserId === this.myUserId;
    }

    getMapImage(src) {
      if (!src) return null;
      const entry = this.imgCache.get(src);
      if (entry) return entry.loaded ? entry.img : null;
      const img = new Image();
      const rec = { img, loaded: false };
      this.imgCache.set(src, rec);
      img.onload = () => { rec.loaded = true; this.dirty = true; };
      img.src = src;
      return null;
    }

    bindEvents() {
      const c = this.canvas;
      const localXY = (ev) => {
        const r = c.getBoundingClientRect();
        return { x: ev.clientX - r.left, y: ev.clientY - r.top };
      };

      window.addEventListener('keydown', (e) => {
        if (e.code === 'Space' && document.activeElement && !/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)) {
          this.spaceHeld = true;
        }
        if ((e.key === 'w' || e.key === 'W') && this.dragToken && this.dragToken.curCell) {
          this.dragToken.waypoints.push({ ...this.dragToken.curCell });
          this.dirty = true;
        }
      });
      window.addEventListener('keyup', (e) => {
        if (e.code === 'Space') this.spaceHeld = false;
      });

      c.addEventListener('contextmenu', (e) => {
        e.preventDefault();
        if (this.dragToken && this.dragToken.curCell) {
          this.dragToken.waypoints.push({ ...this.dragToken.curCell });
          this.dirty = true;
        }
      });

      c.addEventListener('wheel', (e) => {
        e.preventDefault();
        const pt = localXY(e);
        const before = this.screenToWorld(pt.x, pt.y);
        const factor = e.deltaY < 0 ? 1.12 : 0.89;
        this.cam.zoom = Math.max(0.25, Math.min(3.0, this.cam.zoom * factor));
        this.cam.x = pt.x - before.x * this.cam.zoom;
        this.cam.y = pt.y - before.y * this.cam.zoom;
        this.dirty = true;
      }, { passive: false });

      c.addEventListener('pointerdown', (e) => {
        if (!this.scene) return;
        const pt = localXY(e);
        const w = this.screenToWorld(pt.x, pt.y);
        const cell = this.worldToCell(w.x, w.y);

        if (e.button === 1 || (e.button === 2 && !this.dragToken) || (e.button === 0 && this.spaceHeld)) {
          this.dragPan = { sx: pt.x, sy: pt.y, cx: this.cam.x, cy: this.cam.y };
          c.setPointerCapture(e.pointerId);
          return;
        }
        if (e.button !== 0) return;

        if (this.tool === 'ping') {
          const center = this.cellToWorld(cell.q, cell.r);
          if (this.onOp) this.onOp('vtt_ping', { sceneId: this.scene.id, q: cell.q, r: cell.r, x: center.x, y: center.y });
          return;
        }

        if (this.tool === 'zone' && this.isGM) {
          if (this.onOp) {
            this.onOp('vtt_zone', {
              action: 'add', sceneId: this.scene.id,
              kind: this.zoneKind, label: this.zoneKind, q: cell.q, r: cell.r, radius: 1, turnsLeft: 3
            });
          }
          return;
        }

        if (this.tool === 'drop' && this.isGM) {
          const name = prompt('Objet ou arme au sol :', 'Épée / Pack munitions');
          if (name && name.trim() && this.onOp) {
            this.onOp('vtt_drop', { action: 'add', sceneId: this.scene.id, name: name.trim(), q: cell.q, r: cell.r });
          }
          return;
        }

        if (this.tool === 'measure') {
          this.measurePts = { aWorld: w, bWorld: w, aCell: cell, bCell: cell };
          c.setPointerCapture(e.pointerId);
          this.dirty = true;
          return;
        }

        if (this.tool === 'draw') {
          this.drawStroke = { kind: this.drawKind, color: this.drawColor, width: 3, points: [w, w] };
          c.setPointerCapture(e.pointerId);
          this.dirty = true;
          return;
        }

        if (this.tool === 'wall' && this.isGM) {
          this.wallDraft = { kind: this.wallKind, q: cell.q, r: cell.r, startCell: cell, endCell: cell, x1: w.x, y1: w.y, x2: w.x, y2: w.y };
          c.setPointerCapture(e.pointerId);
          this.dirty = true;
          return;
        }

        if (this.tool === 'fog' && this.isGM) {
          this.fogDrag = e.shiftKey || this.fogMode === 'hide' ? 'hide' : 'reveal';
          if (this.onOp) this.onOp('vtt_fog', { action: this.fogDrag, sceneId: this.scene.id, cells: [cell] });
          c.setPointerCapture(e.pointerId);
          return;
        }

        if (this.tool === 'light' && this.isGM) {
          if (this.onOp) {
            this.onOp('vtt_light', { action: 'add', sceneId: this.scene.id, q: cell.q, r: cell.r, x: w.x, y: w.y, range: 10, label: 'Torche 10P' });
          }
          return;
        }

        if (this.tool === 'erase') {
          this.eraseAtWorld(w.x, w.y);
          return;
        }

        // tool === 'select'
        const tok = this.tokenAtWorld(w.x, w.y);
        if (tok) {
          this.selectedTokenId = tok.id;
          if (this.onSelectToken) this.onSelectToken(tok);
          if (this.canControlToken(tok)) {
            this.dragToken = { token: tok, startCell: { q: tok.q, r: tok.r }, waypoints: [], curCell: { q: tok.q, r: tok.r } };
            c.setPointerCapture(e.pointerId);
          }
          this.dirty = true;
          return;
        }

        // Clic sur porte (MJ) pour basculer ouvert/fermé
        if (this.isGM) {
          const door = (this.scene.walls || []).find((wl) => wl.kind === 'door' && wl.q === cell.q && wl.r === cell.r);
          if (door && this.onOp) {
            this.onOp('vtt_wall', { action: 'toggle_door', sceneId: this.scene.id, wallId: door.id });
            return;
          }
        }

        this.selectedTokenId = null;
        if (this.onSelectToken) this.onSelectToken(null);
        this.dragPan = { sx: pt.x, sy: pt.y, cx: this.cam.x, cy: this.cam.y };
        c.setPointerCapture(e.pointerId);
        this.dirty = true;
      });

      c.addEventListener('pointermove', (e) => {
        if (!this.scene) return;
        const pt = localXY(e);
        if (this.dragPan) {
          this.cam.x = this.dragPan.cx + (pt.x - this.dragPan.sx);
          this.cam.y = this.dragPan.cy + (pt.y - this.dragPan.sy);
          this.dirty = true;
          return;
        }
        const w = this.screenToWorld(pt.x, pt.y);
        const cell = this.worldToCell(w.x, w.y);
        this.hoverCell = cell;
        if (this.onHoverCell) this.onHoverCell(cell);

        if (this.dragToken) {
          this.dragToken.curCell = cell;
          this.dirty = true;
          return;
        }
        if (this.measurePts) {
          this.measurePts.bWorld = w;
          this.measurePts.bCell = cell;
          this.dirty = true;
          return;
        }
        if (this.drawStroke) {
          if (this.drawStroke.kind === 'free') {
            const last = this.drawStroke.points[this.drawStroke.points.length - 1];
            if (Math.hypot(w.x - last.x, w.y - last.y) > 4) this.drawStroke.points.push(w);
          } else {
            this.drawStroke.points[1] = w;
          }
          this.dirty = true;
          return;
        }
        if (this.wallDraft) {
          this.wallDraft.x2 = w.x;
          this.wallDraft.y2 = w.y;
          this.wallDraft.endCell = cell;
          this.dirty = true;
          return;
        }
        if (this.fogDrag && this.isGM) {
          const key = cell.q + ',' + cell.r;
          if (this._lastFogKey !== key) {
            this._lastFogKey = key;
            if (this.onOp) this.onOp('vtt_fog', { action: this.fogDrag, sceneId: this.scene.id, cells: [cell] });
          }
          return;
        }
        this.dirty = true;
      });

      c.addEventListener('pointerup', () => {
        if (this.dragPan) { this.dragPan = null; }
        if (this.fogDrag) { this.fogDrag = null; this._lastFogKey = null; }
        if (this.dragToken) {
          const dt = this.dragToken;
          this.dragToken = null;
          if (dt.curCell && (dt.curCell.q !== dt.startCell.q || dt.curCell.r !== dt.startCell.r)) {
            const { steps, apCost } = this.computeDragPath(dt);
            if (this.onOp) {
              this.onOp('vtt_token', {
                action: 'move',
                sceneId: this.scene.id,
                tokenId: dt.token.id,
                q: dt.curCell.q,
                r: dt.curCell.r,
                apCost,
                spendAp: !!(this.vtt && this.vtt.combat && this.vtt.combat.active),
                steps
              });
            }
          }
          this.dirty = true;
        }
        if (this.drawStroke) {
          const ds = this.drawStroke;
          this.drawStroke = null;
          if (ds.points.length >= 2 && this.onOp) {
            this.onOp('vtt_draw', { action: 'add', sceneId: this.scene.id, kind: ds.kind, color: ds.color, width: ds.width, points: ds.points });
          }
          this.dirty = true;
        }
        if (this.wallDraft) {
          const wd = this.wallDraft;
          this.wallDraft = null;
          if (this.onOp) {
            const cells = this.traceCells(wd.startCell || { q: wd.q, r: wd.r }, wd.endCell || { q: wd.q, r: wd.r });
            this.onOp('vtt_wall', {
              action: 'add', sceneId: this.scene.id,
              kind: wd.kind, q: wd.q, r: wd.r,
              q2: (wd.endCell && wd.endCell.q) ?? wd.q,
              r2: (wd.endCell && wd.endCell.r) ?? wd.r,
              cells, x1: wd.x1, y1: wd.y1, x2: wd.x2, y2: wd.y2
            });
          }
          this.dirty = true;
        }
      });

      // Pinch-zoom tactile 2 doigts
      c.addEventListener('touchstart', (e) => {
        if (e.touches.length === 2) {
          const t0 = e.touches[0], t1 = e.touches[1];
          this.pinchState = { dist: Math.hypot(t1.clientX - t0.clientX, t1.clientY - t0.clientY), zoom: this.cam.zoom };
        }
      }, { passive: true });
      c.addEventListener('touchmove', (e) => {
        if (e.touches.length === 2 && this.pinchState) {
          const t0 = e.touches[0], t1 = e.touches[1];
          const d = Math.hypot(t1.clientX - t0.clientX, t1.clientY - t0.clientY);
          this.cam.zoom = Math.max(0.25, Math.min(3.0, this.pinchState.zoom * (d / Math.max(10, this.pinchState.dist))));
          this.dirty = true;
        }
      }, { passive: true });
      c.addEventListener('touchend', () => { this.pinchState = null; });
    }

    computeDragPath(dt) {
      const nodes = [dt.startCell, ...dt.waypoints, dt.curCell];
      const cells = [];
      for (let i = 0; i < nodes.length - 1; i++) {
        const seg = this.traceCells(nodes[i], nodes[i + 1]);
        for (let j = (i === 0 ? 0 : 1); j < seg.length; j++) cells.push(seg[j]);
      }
      const steps = Math.max(0, cells.length - 1);
      const tok = dt.token;
      const rules = window.CodexVTTRules;
      let apCost = steps;
      if (rules && this.scene) {
        apCost = 0;
        const statuses = Array.isArray(tok.statuses) ? tok.statuses : [];
        const ralenti = statuses.includes('Ralenti');
        const prone = statuses.includes('À Terre') || statuses.includes('Déstabilisé');
        for (let i = 1; i < cells.length; i++) {
          const c = cells[i];
          const inDiff = (this.scene.zones || []).some((z) => z.kind === 'difficult' && this.cellDistance(c, z) <= (z.radius || 0));
          apCost += rules.computeStepApCost({ difficult: inDiff, ralenti, prone, armorEnc: tok.armorEnc || 0 });
        }
      }
      return { cells, steps, apCost };
    }

    loop() {
      const now = performance.now();
      this.pings = this.pings.filter((p) => now - p.born < p.duration);
      const hasAnim = this.pings.length > 0 || (this.vtt && this.vtt.combat && this.vtt.combat.active);
      if (this.dirty || hasAnim) {
        this.render(now);
        this.dirty = false;
      }
      this._raf = requestAnimationFrame(() => this.loop());
    }

    pointSegDist(px, py, x1, y1, x2, y2) {
      const dx = x2 - x1, dy = y2 - y1;
      const l2 = dx * dx + dy * dy;
      if (l2 === 0) return Math.hypot(px - x1, py - y1);
      const t = Math.max(0, Math.min(1, ((px - x1) * dx + (py - y1) * dy) / l2));
      return Math.hypot(px - (x1 + t * dx), py - (y1 + t * dy));
    }

    eraseAtWorld(wx, wy) {
      if (!this.scene || !this.onOp) return;
      const sc = this.scene;
      const sz = sc.cellSize || 54;
      const tol = sz * 0.45;
      if (this.isGM) {
        const w = (sc.walls || []).find((wl) => this.pointSegDist(wx, wy, wl.x1, wl.y1, wl.x2, wl.y2) <= tol);
        if (w) { this.onOp('vtt_wall', { action: 'delete', sceneId: sc.id, wallId: w.id }); return; }
        const lg = (sc.lights || []).find((l) => Math.hypot(wx - l.x, wy - l.y) <= tol);
        if (lg) { this.onOp('vtt_light', { action: 'delete', sceneId: sc.id, lightId: lg.id }); return; }
        const zn = (sc.zones || []).find((z) => { const p = this.cellToWorld(z.q, z.r); return Math.hypot(wx - p.x, wy - p.y) <= sz * ((z.radius || 0) + 0.65); });
        if (zn) { this.onOp('vtt_zone', { action: 'delete', sceneId: sc.id, zoneId: zn.id }); return; }
        const drp = (sc.drops || []).find((d) => { const p = this.cellToWorld(d.q, d.r); return Math.hypot(wx - p.x, wy - p.y) <= tol; });
        if (drp) { this.onOp('vtt_drop', { action: 'delete', sceneId: sc.id, dropId: drp.id }); return; }
      }
      const drw = (sc.drawings || []).find((d) => Array.isArray(d.points) && d.points.some((pt) => Math.hypot(wx - pt.x, wy - pt.y) <= tol));
      if (drw) { this.onOp('vtt_draw', { action: 'delete', sceneId: sc.id, drawId: drw.id }); }
    }

    raySegmentDist(ox, oy, dx, dy, x1, y1, x2, y2) {
      const sx = x2 - x1, sy = y2 - y1;
      const denom = dx * sy - dy * sx;
      if (Math.abs(denom) < 1e-8) return Infinity;
      const qpx = x1 - ox, qpy = y1 - oy;
      const t = (qpx * sy - qpy * sx) / denom;
      const u = (qpx * dy - qpy * dx) / denom;
      if (t >= 0 && u >= 0 && u <= 1) return t;
      return Infinity;
    }

    computeVisibilityPolygon(ox, oy, maxR, blockingWalls, rays) {
      const n = rays || 96;
      const pts = [];
      for (let i = 0; i < n; i++) {
        const ang = (Math.PI * 2 * i) / n;
        const dx = Math.cos(ang), dy = Math.sin(ang);
        let best = maxR;
        for (let j = 0; j < blockingWalls.length; j++) {
          const w = blockingWalls[j];
          const d = this.raySegmentDist(ox, oy, dx, dy, w.x1, w.y1, w.x2, w.y2);
          if (d < best) best = d;
        }
        pts.push({ x: ox + dx * best, y: oy + dy * best });
      }
      return pts;
    }

    drawHexPath(ctx, cx, cy, size) {
      ctx.beginPath();
      for (let i = 0; i < 6; i++) {
        const ang = (Math.PI / 180) * (60 * i - 30);
        const px = cx + size * Math.cos(ang);
        const py = cy + size * Math.sin(ang);
        if (i === 0) ctx.moveTo(px, py);
        else ctx.lineTo(px, py);
      }
      ctx.closePath();
    }

    render(now) {
      const c = this.canvas;
      const rect = c.getBoundingClientRect();
      if (!rect.width || !rect.height) return;
      const dpr = window.devicePixelRatio || 1;
      const tw = Math.round(rect.width * dpr);
      const th = Math.round(rect.height * dpr);
      if (c.width !== tw || c.height !== th) { c.width = tw; c.height = th; }

      const ctx = this.ctx;
      ctx.save();
      ctx.scale(dpr, dpr);
      ctx.fillStyle = cssVar('--bg-canvas', '#0b0f19');
      ctx.fillRect(0, 0, rect.width, rect.height);

      if (!this.scene) {
        ctx.fillStyle = cssVar('--text-muted', '#64748b');
        ctx.font = '14px sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText('Sélectionne ou crée une table pour afficher le plateau VTT.', rect.width / 2, rect.height / 2);
        ctx.restore();
        return;
      }

      const sc = this.scene;
      const sz = sc.cellSize || 54;
      ctx.translate(this.cam.x, this.cam.y);
      ctx.scale(this.cam.zoom, this.cam.zoom);

      // Calque 1 : Carte battlemap
      const mapSrc = sc.mapDataUrl || sc.mapUrl;
      if (mapSrc) {
        const img = this.getMapImage(mapSrc);
        if (img) ctx.drawImage(img, 0, 0);
      }

      // Calque 1b : Atlas d'Hybris (si scène Atlas)
      if (sc.kind === 'atlas' && Array.isArray(sc.landmarks) && sc.landmarks.length) {
        const byCode = {};
        const mapW = sc.cols * sz * 1.4;
        const mapH = sc.rows * sz * 1.4;
        sc.landmarks.forEach((lm) => {
          const lx = Number.isFinite(lm.x) ? lm.x * mapW : this.cellToWorld(lm.q || 0, lm.r || 0).x;
          const ly = Number.isFinite(lm.y) ? lm.y * mapH : this.cellToWorld(lm.q || 0, lm.r || 0).y;
          byCode[lm.code] = { x: lx, y: ly, lm };
        });
        ctx.save();
        (sc.routes || []).forEach((rt) => {
          const a = byCode[rt.from], b = byCode[rt.to];
          if (!a || !b) return;
          ctx.beginPath();
          ctx.moveTo(a.x, a.y);
          ctx.lineTo(b.x, b.y);
          ctx.strokeStyle = rt.sea ? '#38bdf8' : '#f59e0b';
          ctx.lineWidth = 2;
          ctx.setLineDash([6, 4]);
          ctx.stroke();
        });
        ctx.setLineDash([]);
        Object.values(byCode).forEach(({ x, y, lm }) => {
          ctx.beginPath();
          ctx.arc(x, y, 6, 0, Math.PI * 2);
          ctx.fillStyle = lm.cat === 'CentralHub' ? '#38bdf8' : '#fbbf24';
          ctx.fill();
          ctx.font = 'bold 11px sans-serif';
          ctx.fillStyle = cssVar('--text-primary', '#f1f5f9');
          ctx.fillText(lm.nom || lm.code, x + 9, y + 4);
        });
        ctx.restore();
      }

      // Calque 2 : Grille
      const gAlpha = sc.gridOpacity ?? 0.35;
      if (gAlpha > 0) {
        ctx.strokeStyle = cssVar('--text-muted', '#64748b');
        ctx.globalAlpha = gAlpha;
        ctx.lineWidth = 1;
        for (let r = 0; r < sc.rows; r++) {
          for (let q = 0; q < sc.cols; q++) {
            const aq = sc.gridType === 'hex' ? q - Math.floor(r / 2) : q;
            const p = this.cellToWorld(aq, r);
            if (sc.gridType === 'square') {
              ctx.strokeRect(p.x - sz / 2, p.y - sz / 2, sz, sz);
            } else {
              this.drawHexPath(ctx, p.x, p.y, sz);
              ctx.stroke();
            }
          }
        }
        ctx.globalAlpha = 1;
      }

      // Case survolée
      if (this.hoverCell) {
        const hp = this.cellToWorld(this.hoverCell.q, this.hoverCell.r);
        ctx.save();
        ctx.strokeStyle = cssVar('--arcane-cyan', '#38bdf8');
        ctx.lineWidth = 1.5;
        ctx.globalAlpha = 0.55;
        if (sc.gridType === 'square') ctx.strokeRect(hp.x - sz / 2, hp.y - sz / 2, sz, sz);
        else { this.drawHexPath(ctx, hp.x, hp.y, sz); ctx.stroke(); }
        ctx.restore();
      }

      // Calque 3 : Zones persistantes & Objets au sol
      for (const z of sc.zones || []) {
        const zp = this.cellToWorld(z.q, z.r);
        const st = ZONE_STYLE[z.kind] || ZONE_STYLE.smoke;
        const rad = sz * ((z.radius || 0) + 0.65);
        ctx.save();
        ctx.beginPath();
        ctx.arc(zp.x, zp.y, rad, 0, Math.PI * 2);
        ctx.fillStyle = st.fill;
        ctx.fill();
        ctx.strokeStyle = st.stroke;
        ctx.lineWidth = 2;
        ctx.stroke();
        ctx.fillStyle = '#fff';
        ctx.font = 'bold 12px sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText(st.icon + (z.turnsLeft ? ' ' + z.turnsLeft + 't' : ''), zp.x, zp.y + 4);
        ctx.restore();
      }

      for (const drp of sc.drops || []) {
        const dp = this.cellToWorld(drp.q, drp.r);
        ctx.save();
        ctx.fillStyle = cssVar('--temporal-amber', '#f59e0b');
        ctx.beginPath();
        ctx.arc(dp.x, dp.y, sz * 0.22, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = cssVar('--text-primary', '#fff');
        ctx.font = 'bold 10px sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText('🎒 ' + drp.name.slice(0, 14), dp.x, dp.y + sz * 0.36);
        ctx.restore();
      }

      // Calque 4 : Dessins libres + brouillon
      const allDraws = [...(sc.drawings || [])];
      if (this.drawStroke) allDraws.push(this.drawStroke);
      for (const d of allDraws) {
        if (!d || !Array.isArray(d.points) || d.points.length < 2) continue;
        ctx.save();
        ctx.strokeStyle = d.color || '#f59e0b';
        ctx.lineWidth = d.width || 3;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        if (d.gmOnly) ctx.globalAlpha = 0.55;
        ctx.beginPath();
        if (d.kind === 'rect') {
          const p0 = d.points[0], p1 = d.points[1];
          ctx.strokeRect(p0.x, p0.y, p1.x - p0.x, p1.y - p0.y);
        } else if (d.kind === 'circle') {
          const p0 = d.points[0], p1 = d.points[1];
          ctx.arc(p0.x, p0.y, Math.hypot(p1.x - p0.x, p1.y - p0.y), 0, Math.PI * 2);
          ctx.stroke();
        } else {
          ctx.moveTo(d.points[0].x, d.points[0].y);
          for (let i = 1; i < d.points.length; i++) ctx.lineTo(d.points[i].x, d.points[i].y);
          ctx.stroke();
        }
        ctx.restore();
      }

      // Calque 5 : Pions
      const activeId = this.vtt && this.vtt.combat && this.vtt.combat.active ? this.vtt.combat.activeTokenId : null;
      for (const tok of sc.tokens || []) {
        const tp = this.cellToWorld(tok.q, tok.r);
        const rad = sz * 0.42 * (tok.size || 1);
        const col = tok.color || cssVar(FACTION_COLORS[tok.faction] || '--arcane-cyan', '#38bdf8');
        ctx.save();
        if (tok.gmOnly || tok.hidden) ctx.globalAlpha = 0.55;

        // Aura éventuelle
        if (tok.auraRadius > 0) {
          ctx.beginPath();
          ctx.arc(tp.x, tp.y, sz * (tok.auraRadius + 0.5), 0, Math.PI * 2);
          ctx.strokeStyle = tok.auraColor || col;
          ctx.globalAlpha = 0.35;
          ctx.lineWidth = 2;
          ctx.setLineDash([6, 4]);
          ctx.stroke();
          ctx.setLineDash([]);
          ctx.globalAlpha = (tok.gmOnly || tok.hidden) ? 0.55 : 1;
        }

        // Halo tour actif
        if (activeId && tok.id === activeId) {
          const pulse = 4 + Math.sin(now / 220) * 2.5;
          ctx.beginPath();
          ctx.arc(tp.x, tp.y, rad + pulse, 0, Math.PI * 2);
          ctx.strokeStyle = cssVar('--temporal-amber', '#f59e0b');
          ctx.lineWidth = 3;
          ctx.stroke();
        }

        // Halo sélection
        if (this.selectedTokenId === tok.id) {
          ctx.beginPath();
          ctx.arc(tp.x, tp.y, rad + 4, 0, Math.PI * 2);
          ctx.strokeStyle = '#ffffff';
          ctx.lineWidth = 2;
          ctx.stroke();
        }

        // Corps du pion
        ctx.beginPath();
        ctx.arc(tp.x, tp.y, rad, 0, Math.PI * 2);
        ctx.fillStyle = cssVar('--bg-surface-elevated', '#182235');
        ctx.fill();
        ctx.lineWidth = 3;
        ctx.strokeStyle = col;
        ctx.stroke();

        // Initiales
        ctx.fillStyle = cssVar('--text-primary', '#f1f5f9');
        ctx.font = 'bold ' + Math.max(11, Math.round(rad * 0.65)) + 'px sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        const initials = (tok.name || '?').split(/\s+/).map((w) => w[0]).join('').slice(0, 2).toUpperCase();
        ctx.fillText(initials, tp.x, tp.y);

        // Nom & barre PA
        ctx.textBaseline = 'alphabetic';
        ctx.font = 'bold 11px sans-serif';
        ctx.fillStyle = cssVar('--text-primary', '#f1f5f9');
        ctx.fillText((tok.gmOnly ? '👁 ' : '') + tok.name, tp.x, tp.y + rad + 13);

        if (!tok.statsMasked && tok.maxPa) {
          const bw = rad * 1.8;
          const bx = tp.x - bw / 2;
          const by = tp.y - rad - 8;
          ctx.fillStyle = 'rgba(0,0,0,0.6)';
          ctx.fillRect(bx, by, bw, 4);
          ctx.fillStyle = cssVar('--arcane-cyan', '#38bdf8');
          ctx.fillRect(bx, by, bw * Math.max(0, Math.min(1, (tok.pa || 0) / tok.maxPa)), 4);
        } else if (tok.healthLabel) {
          ctx.font = '10px monospace';
          ctx.fillStyle = cssVar('--text-gold', '#fbbf24');
          ctx.fillText(tok.healthLabel, tp.x, tp.y - rad - 5);
        }

        if (Array.isArray(tok.statuses) && tok.statuses.length) {
          ctx.font = '9px sans-serif';
          ctx.fillStyle = cssVar('--vital-ruby', '#f43f5e');
          ctx.fillText('[' + tok.statuses.slice(0, 2).join(', ') + (tok.statuses.length > 2 ? '…' : '') + ']', tp.x, tp.y + rad + 24);
        }
        ctx.restore();
      }

      // Calque 6 : Murs, portes, couverts & brouillard léger
      const allWalls = [...(sc.walls || [])];
      if (this.wallDraft) allWalls.push(this.wallDraft);
      for (const w of allWalls) {
        ctx.save();
        ctx.lineCap = 'round';
        if (w.kind === 'door') {
          ctx.strokeStyle = w.open ? '#10b981' : (w.locked ? '#f43f5e' : '#f59e0b');
          ctx.lineWidth = 6;
          if (w.open) ctx.setLineDash([4, 4]);
        } else if (w.kind === 'window') {
          ctx.strokeStyle = '#38bdf8';
          ctx.lineWidth = 4;
          ctx.setLineDash([6, 3]);
        } else if (w.kind === 'cover_half' || w.kind === 'cover_3q') {
          ctx.strokeStyle = '#f59e0b';
          ctx.lineWidth = 4;
        } else {
          ctx.strokeStyle = '#e2e8f0';
          ctx.lineWidth = 6;
        }
        ctx.beginPath();
        ctx.moveTo(w.x1, w.y1);
        ctx.lineTo(w.x2, w.y2);
        ctx.stroke();
        ctx.restore();
      }

      // Sources de lumière (Torche/Lampe 10P)
      for (const lg of sc.lights || []) {
        if (!lg.on) continue;
        const lRad = sz * (lg.range || 10) * 1.4;
        ctx.save();
        const grad = ctx.createRadialGradient(lg.x, lg.y, 4, lg.x, lg.y, lRad);
        grad.addColorStop(0, 'rgba(251,191,36,0.22)');
        grad.addColorStop(1, 'rgba(251,191,36,0)');
        ctx.fillStyle = grad;
        ctx.beginPath();
        ctx.arc(lg.x, lg.y, lRad, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = lg.color || '#fbbf24';
        ctx.beginPath();
        ctx.arc(lg.x, lg.y, 5, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
      }

      if (sc.fogEnabled) {
        const blocking = (sc.walls || []).filter((w) => w && (w.kind === 'wall' || w.kind === 'cover_full' || (w.kind === 'door' && !w.open)));
        ctx.save();
        ctx.fillStyle = this.isGM ? 'rgba(6,8,13,0.28)' : 'rgba(6,8,13,0.78)';
        ctx.beginPath();
        ctx.rect(-3000, -3000, 10000, 10000);
        const defaultSightPx = sz * (sc.sightRange || 12) * 1.65;

        const addCutoutPoly = (ox, oy, radPx) => {
          const poly = blocking.length ? this.computeVisibilityPolygon(ox, oy, radPx, blocking, 96) : null;
          if (poly && poly.length >= 3) {
            ctx.moveTo(poly[0].x, poly[0].y);
            for (let i = poly.length - 1; i >= 0; i--) ctx.lineTo(poly[i].x, poly[i].y);
            ctx.closePath();
          } else {
            ctx.moveTo(ox + radPx, oy);
            ctx.arc(ox, oy, radPx, 0, Math.PI * 2, true);
          }
        };

        for (const t of sc.tokens || []) {
          if (t.faction === 'pj' || t.faction === 'ally' || (this.myUserId && t.ownerUserId === this.myUserId)) {
            const p = this.cellToWorld(t.q, t.r);
            const rPx = t.lightOn ? Math.max(defaultSightPx, sz * (t.lightRange || 10) * 1.65) : defaultSightPx;
            addCutoutPoly(p.x, p.y, rPx);
          }
        }
        for (const lg of sc.lights || []) {
          if (lg.on) addCutoutPoly(lg.x, lg.y, sz * (lg.range || 10) * 1.65);
        }
        for (const key of sc.revealedCells || []) {
          const [cq, cr] = String(key).split(',').map(Number);
          if (!Number.isFinite(cq) || !Number.isFinite(cr)) continue;
          const cp = this.cellToWorld(cq, cr);
          ctx.moveTo(cp.x + sz * 0.95, cp.y);
          ctx.arc(cp.x, cp.y, sz * 0.95, 0, Math.PI * 2, true);
        }
        ctx.fill('evenodd');
        // Cases masquées manuellement par le MJ : recouvrent la découpe.
        if (Array.isArray(sc.hiddenCells) && sc.hiddenCells.length) {
          ctx.beginPath();
          for (const key of sc.hiddenCells) {
            const [hq, hr] = String(key).split(',').map(Number);
            if (!Number.isFinite(hq) || !Number.isFinite(hr)) continue;
            const hp = this.cellToWorld(hq, hr);
            if (sc.gridType === 'square') ctx.rect(hp.x - sz / 2, hp.y - sz / 2, sz, sz);
            else {
              for (let i = 0; i < 6; i++) {
                const ang = (Math.PI / 180) * (60 * i - 30);
                const px = hp.x + sz * Math.cos(ang), py = hp.y + sz * Math.sin(ang);
                if (i === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py);
              }
              ctx.closePath();
            }
          }
          ctx.fill();
        }
        ctx.restore();
      }

      // Calque 7a : Aperçu trajet drag-snap + coût PA
      if (this.dragToken && this.dragToken.curCell) {
        const { cells, steps, apCost } = this.computeDragPath(this.dragToken);
        if (cells.length >= 2) {
          const overBudget = (this.dragToken.token.pa || 0) < apCost;
          const lineCol = overBudget ? '#f43f5e' : '#38bdf8';
          ctx.save();
          ctx.strokeStyle = lineCol;
          ctx.lineWidth = 3;
          ctx.setLineDash([8, 6]);
          ctx.beginPath();
          cells.forEach((cl, i) => {
            const pt = this.cellToWorld(cl.q, cl.r);
            if (i === 0) ctx.moveTo(pt.x, pt.y);
            else ctx.lineTo(pt.x, pt.y);
          });
          ctx.stroke();
          ctx.setLineDash([]);
          const endPt = this.cellToWorld(this.dragToken.curCell.q, this.dragToken.curCell.r);
          const meters = (steps * 1.5).toFixed(1).replace('.0', '');
          const label = `${steps}P (${meters}m) · ${apCost} PA`;
          ctx.font = 'bold 12px monospace';
          const tw2 = ctx.measureText(label).width + 14;
          ctx.fillStyle = 'rgba(6,8,13,0.88)';
          ctx.fillRect(endPt.x - tw2 / 2, endPt.y - sz * 0.85, tw2, 22);
          ctx.strokeStyle = lineCol;
          ctx.strokeRect(endPt.x - tw2 / 2, endPt.y - sz * 0.85, tw2, 22);
          ctx.fillStyle = lineCol;
          ctx.textAlign = 'center';
          ctx.fillText(label, endPt.x, endPt.y - sz * 0.85 + 15);
          ctx.restore();
        }
      }

      // Calque 7b : Règle & Gabarits tactiques
      if (this.measurePts) {
        const m = this.measurePts;
        const dist = this.cellDistance(m.aCell, m.bCell);
        const meters = (dist * 1.5).toFixed(1).replace('.0', '');
        ctx.save();
        if (this.templateMode === 'blast1' || this.templateMode === 'blast2' || this.templateMode === 'blast3' || this.templateMode === 'reach2') {
          const rCells = this.templateMode === 'reach2' ? 2 : parseInt(this.templateMode.slice(5), 10);
          const rings = this.templateMode === 'reach2' ? [2] : [1, 2, 3].slice(0, rCells);
          const rColors = ['#f43f5e', '#f59e0b', '#38bdf8'];
          rings.forEach((rc, idx) => {
            ctx.beginPath();
            ctx.arc(m.bWorld.x, m.bWorld.y, sz * (rc + 0.45), 0, Math.PI * 2);
            ctx.fillStyle = idx === 0 ? 'rgba(244,63,94,0.18)' : 'rgba(245,158,11,0.08)';
            ctx.fill();
            ctx.strokeStyle = rColors[idx];
            ctx.lineWidth = 2;
            ctx.stroke();
          });
        } else if (this.templateMode === 'cone') {
          const ang = Math.atan2(m.bWorld.y - m.aWorld.y, m.bWorld.x - m.aWorld.x);
          const len = Math.max(sz, Math.hypot(m.bWorld.x - m.aWorld.x, m.bWorld.y - m.aWorld.y));
          ctx.beginPath();
          ctx.moveTo(m.aWorld.x, m.aWorld.y);
          ctx.arc(m.aWorld.x, m.aWorld.y, len, ang - 0.45, ang + 0.45);
          ctx.closePath();
          ctx.fillStyle = 'rgba(245,158,11,0.22)';
          ctx.fill();
          ctx.strokeStyle = '#f59e0b';
          ctx.lineWidth = 2;
          ctx.stroke();
        }
        ctx.beginPath();
        ctx.moveTo(m.aWorld.x, m.aWorld.y);
        ctx.lineTo(m.bWorld.x, m.bWorld.y);
        ctx.strokeStyle = '#fbbf24';
        ctx.lineWidth = 2.5;
        ctx.stroke();
        ctx.font = 'bold 12px monospace';
        const txt = `${dist}P (${meters} m)`;
        ctx.fillStyle = 'rgba(6,8,13,0.88)';
        ctx.fillRect(m.bWorld.x + 8, m.bWorld.y - 22, ctx.measureText(txt).width + 12, 20);
        ctx.fillStyle = '#fbbf24';
        ctx.fillText(txt, m.bWorld.x + 14, m.bWorld.y - 8);
        ctx.restore();
      }

      // Calque 7c : Pings animés
      for (const pg of this.pings) {
        const t = (now - pg.born) / pg.duration;
        const r1 = sz * (0.3 + t * 1.9);
        ctx.save();
        ctx.globalAlpha = Math.max(0, 1 - t);
        ctx.strokeStyle = pg.color || '#38bdf8';
        ctx.lineWidth = 3;
        ctx.beginPath();
        ctx.arc(pg.x, pg.y, r1, 0, Math.PI * 2);
        ctx.stroke();
        if (pg.label) {
          ctx.font = 'bold 11px sans-serif';
          ctx.fillStyle = pg.color || '#38bdf8';
          ctx.textAlign = 'center';
          ctx.fillText(pg.label, pg.x, pg.y - r1 - 4);
        }
        ctx.restore();
      }

      ctx.restore();
    }
  }

  window.CodexVTTCanvas = VTTCanvasEngine;
})();
