/**
 * Reader Progress Slider — Shared Component
 * Stores the reader's current chapter and subchapter position in localStorage.
 * Used across index.html, Overview.html, and manuscript pages to keep state synced.
 * 
 * Interaction Model:
 * 1. Clean default view: 16 whole chapters on the slider track. Zero subchapters displayed.
 * 2. Active fast sliding: Coarse scrubbing through whole chapters.
 * 3. Linger on chapter (>280ms): The slider track ZOOMS and CROPS IN, centering the selected chapter.
 *    ONLY the subchapters for that specific chapter appear inside the magnified track (rendered smaller than chapters),
 *    and become the next direct selectable choices on the slider.
 * 4. Stop sliding (release): The slider smoothly ZOOMS BACK TO NORMAL view, and subchapters disappear.
 * 
 * Usage: include this script, then call:
 *   ReaderProgress.mount(containerSelector)
 */
const ReaderProgress = (function () {
    const STORAGE_KEY = 'killtime-reader-progress';
    const LINGER_DELAY_MS = 280; // Delay before slider track zooms in
    const ZOOM_SCALE = 3.6;      // Magnification factor when cropped in

    const VOLUMES = [
        {
            id: 'vol1',
            title: 'Volume I — The Awakening Storm',
            chapters: [
                { num: 0, label: 'Haven\'t started yet', title: 'Haven\'t started yet', parts: [] },
                { num: 1, label: 'Ch 1: The Atomic Crucible', title: 'The Atomic Crucible', parts: [] },
                { num: 2, label: 'Ch 2: Echoes of the Storm', title: 'Echoes of the Storm', parts: [] },
                {
                    num: 3,
                    label: 'Ch 3: The Kinetic Delta',
                    title: 'The Kinetic Delta',
                    parts: [
                        { part: 1, id: 'chapter-3-part-1', label: 'Part 1: The Unmonitored Variable' },
                        { part: 2, id: 'chapter-3-part-2', label: 'Part 2: Discharge Protocol' },
                        { part: 3, id: 'chapter-3-part-3', label: 'Part 3: The Ghost and the Void' },
                        { part: 4, id: 'chapter-3-part-4', label: 'Part 4: Thermodynamic Friction' },
                        { part: 5, id: 'chapter-3-part-5', label: 'Part 5: The Kinetic Delta' }
                    ]
                },
                {
                    num: 4,
                    label: 'Ch 4: The Outlier Variables',
                    title: 'The Outlier Variables',
                    parts: [
                        { part: 1, id: 'chapter-4-part-1', label: 'Part 1: The Gilded Current' },
                        { part: 2, id: 'chapter-4-part-2', label: 'Part 2: The Gravity of Iron' },
                        { part: 3, id: 'chapter-4-part-3', label: 'Part 3: The Friction of the World' }
                    ]
                },
                {
                    num: 5,
                    label: 'Ch 5: The Resonance of Friction',
                    title: 'The Resonance of Friction',
                    parts: [
                        { part: 1, id: 'chapter-5-part-1', label: 'Part 1: The Acoustic Void' },
                        { part: 2, id: 'chapter-5-part-2', label: 'Part 2: The Kinetic Intersection' },
                        { part: 3, id: 'chapter-5-part-3', label: 'Part 3: The Calculus of the Spark' },
                        { part: 4, id: 'chapter-5-part-4', label: 'Part 4: Stepping into the Light' },
                        { part: 5, id: 'chapter-5-part-5', label: 'Part 5: The Unseen Sparks' }
                    ]
                },
                {
                    num: 6,
                    label: 'Ch 6: The Anatomy of a Spark',
                    title: 'The Anatomy of a Spark',
                    parts: [
                        { part: 1, id: 'chapter-6-part-1', label: 'Part 1: The Acoustic Breach' },
                        { part: 2, id: 'chapter-6-part-2', label: 'Part 2: The Kinetic Merge' },
                        { part: 3, id: 'chapter-6-part-3', label: 'Part 3: Sleepwalking Carnage' },
                        { part: 4, id: 'chapter-6-part-4', label: 'Part 4: The Anchor of Iron' }
                    ]
                },
                {
                    num: 7,
                    label: 'Ch 7: The Perimeter Breach',
                    title: 'The Perimeter Breach',
                    parts: [
                        { part: 1, id: 'chapter-7-part-1', label: 'Part 1: Orbital Friction' },
                        { part: 2, id: 'chapter-7-part-2', label: 'Part 2: Terminal Velocity' }
                    ]
                },
                {
                    num: 8,
                    label: 'Ch 8: The Deep Green Void',
                    title: 'The Deep Green Void',
                    parts: [
                        { part: 1, id: 'chapter-8-part-1', label: 'Part 1: The Mountain\'s Root' },
                        { part: 2, id: 'chapter-8-part-2', label: 'Part 2: Shadows and Giants' }
                    ]
                },
                {
                    num: 9,
                    label: 'Ch 9: The Labyrinth of Rust',
                    title: 'The Labyrinth of Rust',
                    parts: [
                        { part: 1, id: 'chapter-9-part-1', label: 'Part 1: The Acoustic Void' },
                        { part: 2, id: 'chapter-9-part-2', label: 'Part 2: Kinetic Momentum' }
                    ]
                },
                {
                    num: 10,
                    label: 'Ch 10: The Weight of the Shadows',
                    title: 'The Weight of the Shadows',
                    parts: [
                        { part: 1, id: 'chapter-10-part-1', label: 'Part 1: Terminal Velocity' },
                        { part: 2, id: 'chapter-10-part-2', label: 'Part 2: The Mathematical Impossibility' },
                        { part: 3, id: 'chapter-10-part-3', label: 'Part 3: The Sterile Extraction' },
                        { part: 4, id: 'chapter-10-part-4', label: 'Part 4: The Anchor\'s Burden' },
                        { part: 5, id: 'chapter-10-part-5', label: 'Part 5: Beneath the Serran Sky' }
                    ]
                },
                {
                    num: 11,
                    label: 'Ch 11: The Mountain\'s Shadow',
                    title: 'The Mountain\'s Shadow',
                    parts: [
                        { part: 1, id: 'chapter-11-part-1', label: 'Part 1: The White Abyss' },
                        { part: 2, id: 'chapter-11-part-2', label: 'Part 2: The Apex Instinct' },
                        { part: 3, id: 'chapter-11-part-3', label: 'Part 3: The Shifting Currents' },
                        { part: 4, id: 'chapter-11-part-4', label: 'Part 4: Predators of the Deep' },
                        { part: 5, id: 'chapter-11-part-5', label: 'Part 5: The Gilded Cage' }
                    ]
                },
                {
                    num: 12,
                    label: 'Ch 12: The Iron Path',
                    title: 'The Iron Path',
                    parts: [
                        { part: 1, id: 'chapter-12-part-1', label: 'Part 1: Sparks in the Void' },
                        { part: 2, id: 'chapter-12-part-2', label: 'Part 2: The Weight of the Stone' },
                        { part: 3, id: 'chapter-12-part-3', label: 'Part 3: Toxic Resonance' },
                        { part: 4, id: 'chapter-12-part-4', label: 'Part 4: The Subterranean Maw' },
                        { part: 5, id: 'chapter-12-part-5', label: 'Part 5: The Collapse of the Sanctuary' }
                    ]
                },
                {
                    num: 13,
                    label: 'Ch 13: The Hollowed Aegis',
                    title: 'The Hollowed Aegis',
                    parts: [
                        { part: 1, id: 'chapter-13-part-1', label: 'Part 1: Descent into the Dark' },
                        { part: 2, id: 'chapter-13-part-2', label: 'Part 2: The Apex of the Deep' },
                        { part: 3, id: 'chapter-13-part-3', label: 'Part 3: The Calculus of Treason' },
                        { part: 4, id: 'chapter-13-part-4', label: 'Part 4: The Crucible of Stone' }
                    ]
                },
                {
                    num: 14,
                    label: 'Ch 14: The Absolute Pressure',
                    title: 'The Absolute Pressure',
                    parts: [
                        { part: 1, id: 'chapter-14-part-1', label: 'Part 1: The Bottleneck' },
                        { part: 2, id: 'chapter-14-part-2', label: 'Part 2: The Bleeding Perimeter' },
                        { part: 3, id: 'chapter-14-part-3', label: 'Part 3: The Phalanx' },
                        { part: 4, id: 'chapter-14-part-4', label: 'Part 4: The Shockwave' },
                        { part: 5, id: 'chapter-14-part-5', label: 'Part 5: Decapitation' }
                    ]
                },
                {
                    num: 15,
                    label: 'Ch 15: The Fractured Anvil',
                    title: 'The Fractured Anvil',
                    parts: [
                        { part: 1, id: 'chapter-15-part-1', label: 'Part 1: The Crumbling Foundation' },
                        { part: 2, id: 'chapter-15-part-2', label: 'Part 2: The Imperial Missile' },
                        { part: 3, id: 'chapter-15-part-3', label: 'Part 3: Thermal Shock' },
                        { part: 4, id: 'chapter-15-part-4', label: 'Part 4: The Divergent Vector' },
                        { part: 5, id: 'chapter-15-part-5', label: 'Part 5: Terminal Mass' }
                    ]
                },
                {
                    num: 16,
                    label: 'Ch 16: Chains of Justice',
                    title: 'Chains of Justice',
                    parts: [
                        { part: 1, id: 'chapter-16-part-1', label: 'Part 1: The Weight of the Ash' },
                        { part: 2, id: 'chapter-16-part-2', label: 'Part 2: Judgment in the Halls' },
                        { part: 3, id: 'chapter-16-part-3', label: 'Part 3: Echoes of the Trial' },
                        { part: 4, id: 'chapter-16-part-4', label: 'Part 4: The Obsidian Cipher' },
                        { part: 5, id: 'chapter-16-part-5', label: 'Part 5: Unfinished Threads' }
                    ]
                },
            ]
        }
    ];

    function load() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            if (raw) return JSON.parse(raw);
        } catch (e) { /* ignore */ }
        return { vol1: { chapter: 0, part: 0 } };
    }

    function save(state) {
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
        } catch (e) { /* ignore */ }
    }

    function normalizePos(rawVal) {
        if (typeof rawVal === 'number') {
            return { chapter: rawVal, part: 0 };
        } else if (rawVal && typeof rawVal === 'object') {
            return {
                chapter: typeof rawVal.chapter === 'number' ? rawVal.chapter : 0,
                part: typeof rawVal.part === 'number' ? rawVal.part : 0
            };
        }
        return { chapter: 0, part: 0 };
    }

    function getPosition(volumeId) {
        const state = load();
        return normalizePos(state[volumeId]);
    }

    function getChapter(volumeId) {
        return getPosition(volumeId).chapter;
    }

    function getPart(volumeId) {
        return getPosition(volumeId).part;
    }

    function getState() {
        return load();
    }

    function getVolume(volumeId) {
        return VOLUMES.find(v => v.id === volumeId) || VOLUMES[0];
    }

    /** Calculate percentage progress taking subchapters into account */
    function calculatePct(vol, chNum, partNum) {
        const maxCh = vol.chapters.length - 1;
        if (maxCh <= 0 || chNum <= 0) return 0;
        
        const chData = vol.chapters[chNum];
        let fraction = 0;
        if (chData && chData.parts && chData.parts.length > 0 && partNum > 0) {
            fraction = partNum / chData.parts.length;
        } else {
            fraction = partNum > 0 ? 1 : 0;
        }

        const totalProgress = (chNum - 1) + (fraction > 0 ? fraction : 1);
        return Math.min(100, Math.max(0, Math.round((totalProgress / maxCh) * 100)));
    }

    /** Injects CSS into the page (once) */
    function injectStyles() {
        if (document.getElementById('reader-progress-styles')) return;
        const style = document.createElement('style');
        style.id = 'reader-progress-styles';
        style.textContent = `
            .rp-widget {
                padding: 14px 12px 14px 12px;
                border-radius: 8px;
                font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Arial, sans-serif;
                background: #0d1117;
                background: linear-gradient(180deg, rgba(17, 24, 39, 0.98), rgba(13, 17, 23, 0.98));
                border: 1px solid rgba(88, 166, 255, 0.3);
                box-shadow: 0 6px 20px rgba(0,0,0,0.6);
                user-select: none;
                touch-action: none;
                position: relative;
                z-index: 30;
                overflow: visible;
                box-sizing: border-box;
                backdrop-filter: blur(12px);
            }
            .rp-title {
                font-size: 0.86em;
                font-weight: 700;
                text-transform: uppercase;
                letter-spacing: 1.2px;
                text-align: center;
                margin-bottom: 10px;
                padding-bottom: 6px;
                border-bottom: 1px solid rgba(255,255,255,0.12);
                color: #e6edf3;
            }
            .rp-vol-header {
                display: flex;
                justify-content: space-between;
                align-items: baseline;
                margin-bottom: 4px;
                font-size: 0.82em;
                font-weight: 600;
                color: #58a6ff;
                line-height: 1.3;
                position: relative;
                z-index: 10;
            }
            .rp-vol-name {
                flex: 1;
                padding-right: 8px;
            }
            .rp-vol-pct {
                color: #8b949e;
                font-size: 0.92em;
                font-weight: 600;
                white-space: nowrap;
            }

            /* Dedicated Tooltip Anchor Zone (Safely positioned between header & slider with zero overlap) */
            .rp-hud-zone {
                position: relative;
                height: 24px;
                margin: 4px 0 6px 0;
                z-index: 60;
                display: flex;
                align-items: center;
            }
            .rp-thumb-hud {
                position: absolute;
                top: 0;
                left: clamp(45px, var(--rp-hud-left, 50%), calc(100% - 45px));
                transform: translateX(-50%);
                background: #010409;
                border: 1px solid #58a6ff;
                border-radius: 4px;
                padding: 2px 8px;
                font-size: 0.72em;
                color: #79c0ff;
                white-space: nowrap;
                pointer-events: none;
                box-shadow: 0 0 12px rgba(88, 166, 255, 0.7);
                z-index: 60;
                font-family: 'Courier New', Courier, monospace;
                line-height: 1.3;
            }
            .rp-thumb-hud::after {
                content: '';
                position: absolute;
                bottom: -5px;
                left: 50%;
                transform: translateX(-50%);
                border-width: 5px 5px 0;
                border-style: solid;
                border-color: #58a6ff transparent transparent;
                display: block;
                width: 0;
            }

            /* Custom Zoomable & Cropping Slider Viewport */
            .rp-slider-viewport {
                position: relative;
                height: 32px;
                margin: 2px 0 8px 0;
                border-radius: 6px;
                overflow: hidden;
                background: #0d1117;
                border: 1px solid #30363d;
                cursor: pointer;
                box-shadow: inset 0 2px 6px rgba(0,0,0,0.6);
                z-index: 10;
            }
            .rp-slider-viewport.is-zoomed {
                border-color: #58a6ff;
                box-shadow: 0 0 14px rgba(88, 166, 255, 0.45), inset 0 2px 8px rgba(0,0,0,0.8);
            }

            /* Zoomable Track Canvas */
            .rp-zoom-track {
                position: absolute;
                top: 0;
                left: 0;
                width: 100%;
                height: 100%;
                display: flex;
                align-items: center;
                transition: width 0.28s cubic-bezier(0.16, 1, 0.3, 1), left 0.28s cubic-bezier(0.16, 1, 0.3, 1);
                z-index: 1;
            }

            /* Inner Track Rail Canvas with 12px Inset to prevent end node clipping */
            .rp-track-inner {
                position: absolute;
                top: 0;
                left: 12px;
                right: 12px;
                height: 100%;
                display: flex;
                align-items: center;
                pointer-events: none;
            }

            /* Track Baseline Rail */
            .rp-track-rail {
                position: absolute;
                left: 0;
                right: 0;
                top: 50%;
                transform: translateY(-50%);
                height: 6px;
                background: #161b22;
                border-radius: 3px;
                z-index: 1;
            }
            .rp-track-fill {
                position: absolute;
                left: 0;
                top: 0;
                height: 100%;
                background: linear-gradient(90deg, #1f6feb, #58a6ff);
                border-radius: 3px;
                pointer-events: none;
                box-shadow: 0 0 8px rgba(88, 166, 255, 0.4);
                z-index: 2;
            }

            /* Chapter Major Ticks (Clean 16 chapters, fully visible) */
            .rp-chapter-node {
                position: absolute;
                top: 50%;
                transform: translate(-50%, -50%);
                width: 8px;
                height: 8px;
                border-radius: 50%;
                background: #8b949e;
                border: 1px solid #161b22;
                z-index: 5;
                transition: all 0.2s ease;
            }
            .rp-chapter-node.passed {
                background: #fff;
                box-shadow: 0 0 4px #58a6ff;
            }
            .rp-chapter-node.active-chapter {
                background: #58a6ff;
                box-shadow: 0 0 8px #58a6ff;
                transform: translate(-50%, -50%) scale(1.3);
            }

            /* Subchapter Minor Ticks Layer (Only displayed for the hovered chapter) */
            .rp-subchapter-layer {
                position: absolute;
                top: 0;
                left: 0;
                width: 100%;
                height: 100%;
                pointer-events: none;
                z-index: 15;
            }
            .rp-subchapter-node {
                position: absolute;
                top: 50%;
                transform: translate(-50%, -50%);
                width: 6px;
                height: 6px;
                border-radius: 50%;
                background: rgba(255, 123, 114, 0.9);
                border: 1px solid #ff7b72;
                cursor: pointer;
                transition: transform 0.15s ease, background 0.15s ease;
                display: flex;
                align-items: center;
                justify-content: center;
                z-index: 16;
                animation: rpSubPopIn 0.25s cubic-bezier(0.16, 1, 0.3, 1) forwards;
            }
            @keyframes rpSubPopIn {
                from { opacity: 0; transform: translate(-50%, -50%) scale(0.3); }
                to { opacity: 1; transform: translate(-50%, -50%) scale(1); }
            }
            .rp-subchapter-node.active-sub {
                width: 8px;
                height: 8px;
                background: #ff7b72;
                box-shadow: 0 0 10px #ff7b72;
                transform: translate(-50%, -50%) scale(1.4);
            }
            .rp-sub-num {
                position: absolute;
                top: -13px;
                left: 50%;
                transform: translateX(-50%);
                font-size: 0.68em;
                color: #ff7b72;
                font-family: 'Courier New', Courier, monospace;
                font-weight: bold;
                white-space: nowrap;
                line-height: 1;
            }

            /* Physical Thumb Handle on the slider */
            .rp-slider-thumb {
                position: absolute;
                top: 50%;
                transform: translate(-50%, -50%);
                width: 16px;
                height: 16px;
                border-radius: 50%;
                background: #58a6ff;
                border: 2px solid #ffffff;
                box-shadow: 0 0 8px rgba(88, 166, 255, 0.9);
                z-index: 25;
                pointer-events: none;
                transition: transform 0.1s ease;
            }
            .rp-slider-viewport.is-zoomed .rp-slider-thumb {
                background: #ff7b72;
                border-color: #ffffff;
                box-shadow: 0 0 12px rgba(255, 123, 114, 0.9);
                transform: translate(-50%, -50%) scale(1.15);
            }

            /* Status Display */
            .rp-status-display {
                margin-top: 4px;
                text-align: center;
                padding-top: 6px;
                border-top: 1px solid rgba(255,255,255,0.08);
                position: relative;
                z-index: 20;
            }
            .rp-current-ch {
                font-size: 0.86em;
                font-weight: 700;
                color: #79c0ff;
                margin-bottom: 2px;
                line-height: 1.2;
            }
            .rp-current-part {
                font-size: 0.78em;
                color: #ff7b72;
                font-style: italic;
                min-height: 1.2em;
                line-height: 1.2;
            }

            /* Standalone mode */
            .rp-widget-standalone {
                background: linear-gradient(135deg, rgba(15,23,42,0.98), rgba(30,41,59,0.98));
                color: #e2e8f0;
                border: 1px solid rgba(255,255,255,0.15);
                box-shadow: 0 4px 20px rgba(0,0,0,0.5);
            }
        `;
        document.head.appendChild(style);
    }

    /** Generate elements inside the zoom track */
    function renderTrackElements(vol) {
        const maxCh = vol.chapters.length - 1;
        let html = `<div class="rp-track-inner" data-rp-inner="${vol.id}">`;

        // Rail & Fill
        html += `
            <div class="rp-track-rail">
                <div class="rp-track-fill" data-rp-fill="${vol.id}"></div>
            </div>
        `;

        // Major Chapter Nodes (Clean 16 chapters)
        for (let i = 0; i <= maxCh; i++) {
            const pct = (i / maxCh) * 100;
            html += `<div class="rp-chapter-node" data-ch-node="${i}" style="left: ${pct}%;"></div>`;
        }

        // Subchapters Layer: Dynamically populated ONLY for the hovered chapter
        html += `<div class="rp-subchapter-layer" data-rp-sub-layer="${vol.id}"></div>`;

        // Thumb handle inside track
        html += `<div class="rp-slider-thumb" data-rp-thumb="${vol.id}"></div>`;

        html += `</div>`;
        return html;
    }

    /** Render ONLY the subchapters of the lingered chapter */
    function renderActiveSubchapters(volId, chNum, activePartNum) {
        const vol = getVolume(volId);
        const maxCh = vol.chapters.length - 1;
        const chData = vol.chapters[chNum];
        const subLayers = document.querySelectorAll(`[data-rp-sub-layer="${volId}"]`);

        if (!chData || !chData.parts || chData.parts.length === 0 || chNum <= 0) {
            subLayers.forEach(layer => { layer.innerHTML = ''; });
            return;
        }

        // Chapter C's span is from (C-1)/maxCh to C/maxCh
        const chStartPct = (chNum - 1) / maxCh;
        const chEndPct = chNum / maxCh;
        const chSpan = chEndPct - chStartPct;

        let html = '';
        chData.parts.forEach(p => {
            const fraction = (p.part - 0.5) / chData.parts.length;
            const nodePct = (chStartPct + (fraction * chSpan)) * 100;
            const isActive = p.part === activePartNum;

            html += `
                <div class="rp-subchapter-node ${isActive ? 'active-sub' : ''}" 
                     data-sub-part="${p.part}" style="left: ${nodePct}%;">
                    <span class="rp-sub-num">${p.part}</span>
                </div>
            `;
        });

        subLayers.forEach(layer => {
            layer.innerHTML = html;
        });
    }

    /** Clear subchapters from track */
    function clearActiveSubchapters(volId) {
        document.querySelectorAll(`[data-rp-sub-layer="${volId}"]`).forEach(layer => {
            layer.innerHTML = '';
        });
    }

    /** Build volume widget HTML */
    function buildVolumeSlider(vol, pos) {
        const ch = pos.chapter;
        const part = pos.part;
        const pct = calculatePct(vol, ch, part);
        const chData = vol.chapters[ch] || vol.chapters[0];

        const currentPartObj = (chData.parts && chData.parts.length > 0 && part > 0)
            ? chData.parts.find(p => p.part === part)
            : null;
        
        const partText = currentPartObj ? currentPartObj.label : '';

        return `
            <div class="rp-vol-header">
                <span class="rp-vol-name">${vol.title}</span>
                <span class="rp-vol-pct" data-rp-pct="${vol.id}">${pct}%</span>
            </div>

            <!-- Floating HUD Zone -->
            <div class="rp-hud-zone">
                <div class="rp-thumb-hud" data-rp-hud="${vol.id}">Chapter ${ch}</div>
            </div>

            <!-- Zoomable & Cropping Viewport Slider -->
            <div class="rp-slider-viewport" data-rp-viewport="${vol.id}">
                <div class="rp-zoom-track" data-rp-track="${vol.id}">
                    ${renderTrackElements(vol)}
                </div>
            </div>

            <!-- Clean Status Display -->
            <div class="rp-status-display">
                <div class="rp-current-ch" data-rp-ch-label="${vol.id}">${chData.label}</div>
                <div class="rp-current-part" data-rp-part-label="${vol.id}">${partText}</div>
            </div>
        `;
    }

    /** Mount into DOM */
    function mount(containerSelector, options) {
        let targets = [];
        if (typeof containerSelector === 'string') {
            targets = Array.from(document.querySelectorAll(containerSelector));
        } else if (Array.isArray(containerSelector)) {
            containerSelector.forEach(sel => {
                if (typeof sel === 'string') {
                    targets.push(...document.querySelectorAll(sel));
                } else if (sel) {
                    targets.push(sel);
                }
            });
        } else if (containerSelector) {
            targets = [containerSelector];
        }

        if (!targets || targets.length === 0) return;

        injectStyles();

        const standalone = options && options.standalone;
        const state = load();

        targets.forEach(container => {
            if (!container) return;

            let html = `<div class="rp-widget ${standalone ? 'rp-widget-standalone' : ''}">`;
            html += `<div class="rp-title">📖 Reader Progress</div>`;

            VOLUMES.forEach(vol => {
                const pos = normalizePos(state[vol.id]);
                html += buildVolumeSlider(vol, pos);
            });

            html += `</div>`;
            container.innerHTML = html;

            bindInteractiveZoomSlider(container);
        });

        // Initial sync of all mounts
        VOLUMES.forEach(vol => {
            const pos = normalizePos(state[vol.id]);
            syncAllUI(vol.id, pos.chapter, pos.part, false);
        });
    }

    /** Bind unified pointer interactions for zoom-in and direct sliding */
    function bindInteractiveZoomSlider(container) {
        VOLUMES.forEach(vol => {
            const viewport = container.querySelector(`[data-rp-viewport="${vol.id}"]`);
            const track = container.querySelector(`[data-rp-track="${vol.id}"]`);
            if (!viewport || !track) return;

            const maxCh = vol.chapters.length - 1;
            let isDragging = false;
            let isZoomed = false;
            let lingerTimer = null;
            let currentCh = getChapter(vol.id);
            let currentPart = getPart(vol.id);

            // Compute normalized progress fraction [0, 1] on the track from clientX
            function getTrackPosFromClientX(clientX) {
                const inner = track.querySelector(`[data-rp-inner="${vol.id}"]`);
                const innerRect = inner ? inner.getBoundingClientRect() : track.getBoundingClientRect();
                const relativeX = clientX - innerRect.left;
                return Math.max(0, Math.min(1, relativeX / innerRect.width));
            }

            // Enter Zoom Mode: anchors zoom directly on the active thumb position under the mouse pointer
            function enterZoom(chNum, pointerTrackPos) {
                const chData = vol.chapters[chNum];
                if (!chData || !chData.parts || chData.parts.length === 0 || chNum <= 0) return;

                isZoomed = true;
                viewport.classList.add('is-zoomed');

                const chStartPct = (chNum - 1) / maxCh;
                const chEndPct = chNum / maxCh;
                const chSpan = chEndPct - chStartPct;

                // Determine active part from where the user clicked
                if (typeof pointerTrackPos === 'number') {
                    const intra = (pointerTrackPos - chStartPct) / chSpan;
                    const clamped = Math.max(0, Math.min(0.999, intra));
                    currentPart = Math.min(chData.parts.length, Math.max(1, Math.floor(clamped * chData.parts.length) + 1));
                } else if (currentPart === 0) {
                    currentPart = 1;
                }

                // Calculate the exact thumb position on the track
                const thumbTrackFraction = chStartPct + ((currentPart - 0.5) / chData.parts.length) * chSpan;

                // Anchor zoom directly at the thumb position so the mouse pointer and thumb are unified!
                const scale = ZOOM_SCALE;
                const leftPct = thumbTrackFraction * (1 - scale) * 100;

                track.style.width = `${scale * 100}%`;
                track.style.left = `${leftPct}%`;

                renderActiveSubchapters(vol.id, chNum, currentPart);
                syncAllUI(vol.id, chNum, currentPart, true);
            }

            // Exit Zoom Mode: returns slider track to 100% normal view and clears subchapters
            function exitZoom() {
                isZoomed = false;
                viewport.classList.remove('is-zoomed');
                track.style.width = '100%';
                track.style.left = '0%';
                clearActiveSubchapters(vol.id);
                syncAllUI(vol.id, currentCh, currentPart, false);
            }

            // Handle Pointer Down
            viewport.addEventListener('pointerdown', function (e) {
                e.preventDefault();
                isDragging = true;
                viewport.setPointerCapture(e.pointerId);

                const trackPos = getTrackPosFromClientX(e.clientX);
                currentCh = Math.max(0, Math.min(maxCh, Math.round(trackPos * maxCh)));
                currentPart = 0;

                // Sync immediately in normal mode
                syncAllUI(vol.id, currentCh, currentPart, false);

                // Start linger timer to zoom in on this chapter
                if (lingerTimer) clearTimeout(lingerTimer);
                lingerTimer = setTimeout(() => {
                    if (isDragging) {
                        enterZoom(currentCh, trackPos);
                    }
                }, LINGER_DELAY_MS);
            });

            // Handle Pointer Move (Scrubbing across chapters or zoomed subchapters)
            viewport.addEventListener('pointermove', function (e) {
                if (!isDragging) return;
                const trackPos = getTrackPosFromClientX(e.clientX);

                if (!isZoomed) {
                    // Normal Mode: scrubbing whole chapters
                    const newCh = Math.max(0, Math.min(maxCh, Math.round(trackPos * maxCh)));
                    if (newCh !== currentCh) {
                        currentCh = newCh;
                        currentPart = 0;
                        syncAllUI(vol.id, currentCh, 0, false);

                        // Reset linger timer for new chapter
                        if (lingerTimer) clearTimeout(lingerTimer);
                        lingerTimer = setTimeout(() => {
                            if (isDragging) enterZoom(currentCh, trackPos);
                        }, LINGER_DELAY_MS);
                    }
                } else {
                    // Zoomed Mode: direct 1:1 scrub of subchapters under the mouse cursor
                    const chData = vol.chapters[currentCh];
                    if (chData && chData.parts && chData.parts.length > 0 && currentCh > 0) {
                        const chStartPct = (currentCh - 1) / maxCh;
                        const chEndPct = currentCh / maxCh;
                        const chSpan = chEndPct - chStartPct;

                        // Position within this chapter's span [0, 1]
                        const intraProgress = (trackPos - chStartPct) / chSpan;

                        // If user moves outside of the subchapter zone (>8% cushion), reset to chapters!
                        if (intraProgress < -0.08 || intraProgress > 1.08) {
                            exitZoom();
                            const newCh = Math.max(0, Math.min(maxCh, Math.round(trackPos * maxCh)));
                            currentCh = newCh;
                            currentPart = 0;
                            syncAllUI(vol.id, currentCh, 0, false);

                            if (lingerTimer) clearTimeout(lingerTimer);
                            lingerTimer = setTimeout(() => {
                                if (isDragging) enterZoom(currentCh, trackPos);
                            }, LINGER_DELAY_MS);
                        } else {
                            // Select part directly under mouse cursor
                            const clampedIntra = Math.max(0, Math.min(0.999, intraProgress));
                            const chosenPart = Math.min(chData.parts.length, Math.max(1, Math.floor(clampedIntra * chData.parts.length) + 1));

                            if (chosenPart !== currentPart) {
                                currentPart = chosenPart;
                                renderActiveSubchapters(vol.id, currentCh, currentPart);
                                syncAllUI(vol.id, currentCh, currentPart, true);
                            }
                        }
                    } else {
                        exitZoom();
                    }
                }
            });

            // Handle Pointer Up / Stop Sliding
            const handlePointerUp = function (e) {
                if (!isDragging) return;
                isDragging = false;
                if (lingerTimer) clearTimeout(lingerTimer);

                try {
                    viewport.releasePointerCapture(e.pointerId);
                } catch (err) { /* ignore */ }

                // Stop sliding -> slider zooms back to normal and subchapters disappear
                exitZoom();

                // Persist state
                const s = load();
                s[vol.id] = { chapter: currentCh, part: currentPart };
                save(s);

                // Dispatch global event for reader navigation
                window.dispatchEvent(new CustomEvent('reader-progress-changed', {
                    detail: {
                        volumeId: vol.id,
                        chapter: currentCh,
                        part: currentPart,
                        partId: (currentPart > 0) ? `chapter-${currentCh}-part-${currentPart}` : (currentCh > 0 ? `chapter-${currentCh}` : null)
                    }
                }));
            };

            viewport.addEventListener('pointerup', handlePointerUp);
            viewport.addEventListener('pointercancel', handlePointerUp);
        });
    }

    /** Synchronize all mounted widgets */
    function syncAllUI(volId, chNum, partNum, isZoomedState) {
        const vol = getVolume(volId);
        const maxCh = vol.chapters.length - 1;
        const pct = calculatePct(vol, chNum, partNum);
        const chData = vol.chapters[chNum] || vol.chapters[0];
        
        const currentPartObj = (chData.parts && chData.parts.length > 0 && partNum > 0)
            ? chData.parts.find(p => p.part === partNum)
            : null;
        
        const partText = currentPartObj ? currentPartObj.label : '';
        const hudText = partNum > 0 ? `Ch ${chNum} • Part ${partNum}` : (chNum > 0 ? `Chapter ${chNum}` : `Start`);

        // Calculate thumb position on the track
        let trackFraction = chNum / maxCh;
        if (chData.parts && chData.parts.length > 0 && partNum > 0 && chNum > 0) {
            const chStartFraction = (chNum - 1) / maxCh;
            const chEndFraction = chNum / maxCh;
            const chSpan = chEndFraction - chStartFraction;
            trackFraction = chStartFraction + ((partNum - 0.5) / chData.parts.length) * chSpan;
        }
        const thumbPct = Math.max(0, Math.min(100, trackFraction * 100));

        // Update all mounted DOM instances
        document.querySelectorAll(`[data-rp-fill="${volId}"]`).forEach(fill => {
            fill.style.width = `${thumbPct}%`;
        });
        document.querySelectorAll(`[data-rp-thumb="${volId}"]`).forEach(thumb => {
            thumb.style.left = `${thumbPct}%`;
        });
        document.querySelectorAll(`[data-rp-hud="${volId}"]`).forEach(hud => {
            hud.textContent = hudText;
            hud.style.setProperty('--rp-hud-left', `calc(12px + (100% - 24px) * ${chNum / maxCh})`);
        });
        document.querySelectorAll(`[data-rp-pct="${volId}"]`).forEach(p => {
            p.textContent = `${pct}%`;
        });

        // Labels
        document.querySelectorAll(`[data-rp-ch-label="${volId}"]`).forEach(lbl => {
            lbl.textContent = chData.label;
        });
        document.querySelectorAll(`[data-rp-part-label="${volId}"]`).forEach(lbl => {
            lbl.textContent = partText;
        });

        // Chapter node highlighting
        document.querySelectorAll(`[data-ch-node]`).forEach(node => {
            const nodeCh = parseInt(node.getAttribute('data-ch-node'), 10);
            node.classList.toggle('passed', nodeCh <= chNum);
            node.classList.toggle('active-chapter', nodeCh === chNum);
        });

        // Subchapter node highlighting
        document.querySelectorAll(`[data-sub-part]`).forEach(subNode => {
            const pNum = parseInt(subNode.getAttribute('data-sub-part'), 10);
            subNode.classList.toggle('active-sub', pNum === partNum);
        });
    }

    /** Refresh from current localStorage */
    function refresh() {
        const state = load();
        VOLUMES.forEach(vol => {
            const pos = normalizePos(state[vol.id]);
            syncAllUI(vol.id, pos.chapter, pos.part, false);
        });
    }

    // Auto-refresh on back/forward navigation
    window.addEventListener('pageshow', function () {
        refresh();
    });

    // Auto-refresh when document returns to active focus
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') {
            refresh();
        }
    });

    // Auto-refresh when another tab updates localStorage
    window.addEventListener('storage', function (event) {
        if (event.key === STORAGE_KEY) {
            refresh();
        }
    });

    return { mount, refresh, getState, getChapter, getPart, getPosition, VOLUMES };
})();


