/**
 * Reader Progress Slider — Shared Component
 * Stores the reader's current chapter position in localStorage.
 * Used across index.html and Overview.html to keep state synced.
 * 
 * Usage: include this script, then call:
 *   ReaderProgress.mount(containerSelector)
 */
const ReaderProgress = (function () {
    const STORAGE_KEY = 'killtime-reader-progress';

    const VOLUMES = [
        {
            id: 'vol1',
            title: 'Volume I — The Awakening Storm',
            chapters: [
                { num: 0, label: 'Haven\'t started yet' },
                { num: 1, label: 'Ch 1: The Atomic Crucible' },
                { num: 2, label: 'Ch 2: Echoes of the Storm' },
                { num: 3, label: 'Ch 3: The Kinetic Delta' },
                { num: 4, label: 'Ch 4: The Outlier Variables' },
                { num: 5, label: 'Ch 5: The Resonance of Friction' },
                { num: 6, label: 'Ch 6: The Anatomy of a Spark' },
                { num: 7, label: 'Ch 7: The Perimeter Breach' },
                { num: 8, label: 'Ch 8: The Deep Green Void' },
                { num: 9, label: 'Ch 9: The Labyrinth of Rust' },
                { num: 10, label: 'Ch 10: The Weight of the Shadows' },
                { num: 11, label: 'Ch 11: The Mountain\'s Shadow' },
                { num: 12, label: 'Ch 12: The Iron Path' },
                { num: 13, label: 'Ch 13: The Hollowed Aegis' },
                { num: 14, label: 'Ch 14: The Absolute Pressure' },
                { num: 15, label: 'Ch 15: The Fractured Anvil' },
                { num: 16, label: 'Ch 16: Chains of Justice' },
            ]
        }
        // Future volumes can be added here
    ];

    function load() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            if (raw) return JSON.parse(raw);
        } catch (e) { /* ignore */ }
        // Default: haven't started
        return { vol1: 0 };
    }

    function save(state) {
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
        } catch (e) { /* ignore */ }
    }

    /** Returns the current reader state */
    function getState() {
        return load();
    }

    /** Returns the chapter number the reader is at for a given volume */
    function getChapter(volumeId) {
        const state = load();
        return state[volumeId] || 0;
    }

    /** Injects CSS into the page (once) */
    function injectStyles() {
        if (document.getElementById('reader-progress-styles')) return;
        const style = document.createElement('style');
        style.id = 'reader-progress-styles';
        style.textContent = `
            .rp-widget {
                padding: 15px;
                border-radius: 8px;
                font-family: Arial, sans-serif;
            }
            .rp-title {
                font-size: 0.9em;
                font-weight: bold;
                text-transform: uppercase;
                letter-spacing: 1px;
                text-align: center;
                margin-bottom: 12px;
                padding-bottom: 8px;
                border-bottom: 1px solid rgba(255,255,255,0.15);
                color: inherit;
            }
            .rp-vol-title {
                font-size: 0.85em;
                font-weight: bold;
                color: #58a6ff;
                margin-bottom: 8px;
            }
            .rp-slider-wrap {
                position: relative;
                margin-bottom: 6px;
            }
            .rp-slider {
                -webkit-appearance: none;
                appearance: none;
                width: 100%;
                height: 6px;
                border-radius: 3px;
                background: linear-gradient(to right, #58a6ff var(--rp-pct, 0%), rgba(255,255,255,0.15) var(--rp-pct, 0%));
                outline: none;
                cursor: pointer;
                transition: background 0.15s ease;
            }
            .rp-slider::-webkit-slider-thumb {
                -webkit-appearance: none;
                appearance: none;
                width: 16px;
                height: 16px;
                border-radius: 50%;
                background: #58a6ff;
                border: 2px solid #fff;
                cursor: pointer;
                box-shadow: 0 0 6px rgba(88,166,255,0.5);
                transition: transform 0.15s ease, box-shadow 0.15s ease;
            }
            .rp-slider::-webkit-slider-thumb:hover {
                transform: scale(1.3);
                box-shadow: 0 0 12px rgba(88,166,255,0.8);
            }
            .rp-slider::-moz-range-thumb {
                width: 16px;
                height: 16px;
                border-radius: 50%;
                background: #58a6ff;
                border: 2px solid #fff;
                cursor: pointer;
                box-shadow: 0 0 6px rgba(88,166,255,0.5);
            }
            .rp-chapter-label {
                font-size: 0.8em;
                text-align: center;
                margin-top: 6px;
                min-height: 1.2em;
                opacity: 0.9;
                color: inherit;
                transition: opacity 0.2s ease;
            }
            .rp-chapter-label .rp-label-chapter {
                color: #79c0ff;
                font-weight: bold;
            }
            .rp-progress-bar-bg {
                width: 100%;
                height: 4px;
                border-radius: 2px;
                background: rgba(255,255,255,0.1);
                margin-top: 8px;
                overflow: hidden;
            }
            .rp-progress-bar-fill {
                height: 100%;
                border-radius: 2px;
                background: linear-gradient(90deg, #58a6ff, #79c0ff);
                transition: width 0.4s ease;
            }
            .rp-pct-label {
                font-size: 0.75em;
                text-align: right;
                margin-top: 3px;
                opacity: 0.6;
                color: inherit;
            }

            /* Standalone widget (for pages that use it outside a sidebar) */
            .rp-widget-standalone {
                background: linear-gradient(135deg, rgba(15,23,42,0.95), rgba(30,41,59,0.95));
                color: #e2e8f0;
                border: 1px solid rgba(255,255,255,0.1);
                box-shadow: 0 2px 12px rgba(0,0,0,0.3);
            }
        `;
        document.head.appendChild(style);
    }

    /** Build the slider widget HTML for a volume */
    function buildVolumeSlider(vol, currentChapter) {
        const maxCh = vol.chapters.length - 1; // -1 because index 0 = "haven't started"
        const pct = maxCh > 0 ? Math.round((currentChapter / maxCh) * 100) : 0;
        const currentLabel = vol.chapters[currentChapter] || vol.chapters[0];

        return `
            <div class="rp-vol-title">${vol.title}</div>
            <div class="rp-slider-wrap">
                <input type="range" class="rp-slider" data-rp-slider="${vol.id}"
                       min="0" max="${maxCh}" value="${currentChapter}"
                       style="--rp-pct: ${pct}%;">
            </div>
            <div class="rp-chapter-label" data-rp-label="${vol.id}">
                <span class="rp-label-chapter">${currentLabel.label}</span>
            </div>
            <div class="rp-progress-bar-bg">
                <div class="rp-progress-bar-fill" data-rp-fill="${vol.id}" style="width: ${pct}%;"></div>
            </div>
            <div class="rp-pct-label" data-rp-pct="${vol.id}">${pct}% read</div>
        `;
    }

    /** Mount the widget into a container element */
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
                const ch = state[vol.id] || 0;
                html += buildVolumeSlider(vol, ch);
            });

            html += `</div>`;
            container.innerHTML = html;

            // Bind event listeners to all sliders inside this specific container
            VOLUMES.forEach(vol => {
                const slider = container.querySelector(`input.rp-slider[data-rp-slider="${vol.id}"]`);
                if (!slider) return;

                slider.addEventListener('input', function () {
                    const val = parseInt(this.value, 10);
                    const s = load();
                    s[vol.id] = val;
                    save(s);
                    // Systemic update: immediately synchronize all mounted instances across the document
                    refresh();
                    window.dispatchEvent(new CustomEvent('reader-progress-changed', {
                        detail: { volumeId: vol.id, chapter: val, chapters: vol.chapters }
                    }));
                });
            });
        });
    }

    /** Refresh all mounted slider UIs from current localStorage (no re-mount) */
    function refresh() {
        const state = load();
        VOLUMES.forEach(vol => {
            const ch = state[vol.id] || 0;
            const maxCh = vol.chapters.length - 1;
            const pct = maxCh > 0 ? Math.round((ch / maxCh) * 100) : 0;
            const chInfo = vol.chapters[ch] || vol.chapters[0];

            const sliders = document.querySelectorAll(`input.rp-slider[data-rp-slider="${vol.id}"]`);
            sliders.forEach(slider => {
                slider.value = ch;
                slider.style.setProperty('--rp-pct', pct + '%');
            });

            const labels = document.querySelectorAll(`[data-rp-label="${vol.id}"]`);
            labels.forEach(label => {
                label.innerHTML = `<span class="rp-label-chapter">${chInfo.label}</span>`;
            });

            const fills = document.querySelectorAll(`[data-rp-fill="${vol.id}"]`);
            fills.forEach(fill => {
                fill.style.width = pct + '%';
            });

            const pctLabels = document.querySelectorAll(`[data-rp-pct="${vol.id}"]`);
            pctLabels.forEach(pctLabel => {
                pctLabel.textContent = pct + '% read';
            });
        });
    }

    // Auto-refresh on back/forward navigation regardless of cache serialization state
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

    return { mount, refresh, getState, getChapter, VOLUMES };
})();
