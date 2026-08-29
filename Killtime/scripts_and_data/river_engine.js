const canvas = document.getElementById('riverCanvas');
const ctx = canvas.getContext('2d');
let projectData = null;
let scaleX = 1;
let translateX = 0;
let translateY = 250;
const PIXELS_PER_YEAR = 100;
const LOCAL_STORAGE_KEY = 'river_of_time_project_data';

let isDragging = false;
let isDraggingEvent = false;
let draggedEventIdx = null;
let dragHasMoved = false;
let rightPanMoved = false;
let lastMouseX = 0;
let lastMouseY = 0;

let hoveredEventIdx = null;
let hoveredDayTitleKey = null;
const contextMenu = document.getElementById('contextMenu');
let contextMenuTargetIdx = null;

let hitboxes = [];
const branchColors = ["#f44336", "#2196f3", "#4caf50", "#9c27b0", "#ff9800", "#00bcd4"];

// --- LOCAL STORAGE PERSISTENCE ---
function persistProjectData() {
    if (projectData) {
        try {
            localStorage.setItem(LOCAL_STORAGE_KEY, JSON.stringify(projectData));
        } catch (e) {
            console.error('Failed to persist River of Time data:', e);
        }
    }
}

// --- MATH UTILS ---
function dateToFloat(year, month, day) {
    const yearFraction = ((month - 1) * 30 + (day - 1)) / 300.0;
    return year + yearFraction;
}

function floatToDate(floatVal) {
    const cleanV = Math.round(floatVal * 300) / 300.0;
    const year = Math.floor(cleanV);
    const rem = cleanV - year;
    const totalDays = Math.round(rem * 300);
    const month = Math.floor(totalDays / 30) + 1;
    const day = (totalDays % 30) + 1;
    return { year, month, day };
}

function normalizeDateStr(y, m, d) {
    return `${String(y).padStart(4, '0')}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
}

function parseDateStr(str) {
    if (!str || typeof str !== 'string') return { year: 1772, month: 1, day: 1 };
    const parts = str.split('-').map(Number);
    return {
        year: isNaN(parts[0]) ? 1772 : parts[0],
        month: isNaN(parts[1]) ? 1 : parts[1],
        day: isNaN(parts[2]) ? 1 : parts[2]
    };
}

function valToX(val) {
    return val * PIXELS_PER_YEAR;
}

// --- DOM EVENT LISTENERS ---
function resizeCanvas() {
    const oldHeight = canvas.height || 0;
    canvas.width = canvas.clientWidth;
    canvas.height = canvas.clientHeight;
    if (oldHeight === 0 || isNaN(oldHeight)) {
        translateY = canvas.height / 2;
    } else {
        translateY += (canvas.height - oldHeight) / 2;
    }
    render();
}
window.addEventListener('resize', resizeCanvas);

const engineContainer = document.getElementById('engine-container');
if (window.ResizeObserver && engineContainer) {
    const ro = new ResizeObserver(() => {
        if (canvas.clientWidth !== canvas.width || canvas.clientHeight !== canvas.height) {
            resizeCanvas();
        }
    });
    ro.observe(engineContainer);
}

canvas.addEventListener('mousedown', (e) => {
    const rect = canvas.getBoundingClientRect();
    const mouseX = e.clientX - rect.left;
    const mouseY = e.clientY - rect.top;

    if (e.button === 0) {
        let clickedEventIdx = null;
        for (let h of hitboxes) {
            if (mouseX >= h.left && mouseX <= h.right && mouseY >= h.top && mouseY <= h.bottom) {
                if (!h.isDayTitle && !h.isTimeline && h.idx !== null && h.idx !== undefined) {
                    clickedEventIdx = h.idx;
                    break;
                }
            }
        }

        if (clickedEventIdx !== null && projectData && projectData.events && projectData.events[clickedEventIdx]) {
            isDraggingEvent = true;
            draggedEventIdx = clickedEventIdx;
            dragHasMoved = false;
            canvas.style.cursor = 'move';
        }
    } else if (e.button === 2) {
        isDragging = true;
        rightPanMoved = false;
        lastMouseX = e.clientX;
        lastMouseY = e.clientY;
        canvas.style.cursor = 'grabbing';
    }
});

window.addEventListener('mouseup', () => {
    if (isDraggingEvent) {
        if (dragHasMoved) {
            persistProjectData();
        }
        isDraggingEvent = false;
        draggedEventIdx = null;
        dragHasMoved = false;
        canvas.style.cursor = 'default';
        render();
    }
    if (isDragging) {
        isDragging = false;
        canvas.style.cursor = 'default';
    }
});

canvas.addEventListener('mousemove', (e) => {
    const rect = canvas.getBoundingClientRect();
    const mouseX = e.clientX - rect.left;
    const mouseY = e.clientY - rect.top;

    if (isDraggingEvent && draggedEventIdx !== null && projectData && projectData.events) {
        dragHasMoved = true;
        const ev = projectData.events[draggedEventIdx];
        if (ev) {
            // 1. Horizontal Date Snap (X-Axis)
            const worldX = (mouseX - translateX) / scaleX;
            const rawFloat = worldX / PIXELS_PER_YEAR;
            const d = floatToDate(rawFloat);
            ev.float_val = dateToFloat(d.year, d.month, d.day);
            ev.date_str = normalizeDateStr(d.year, d.month, d.day);

            // 2. Timeline Track Proximity Snap (Y-Axis)
            if (projectData.timelines) {
                let closestLine = ev.line_name;
                let minDist = Infinity;
                Object.keys(projectData.timelines).forEach(tName => {
                    const line = projectData.timelines[tName];
                    const lineY = translateY + line.y;
                    const dist = Math.abs(mouseY - lineY);
                    if (dist < minDist) {
                        minDist = dist;
                        closestLine = tName;
                    }
                });
                if (closestLine) {
                    ev.line_name = closestLine;
                }
            }
            render();
        }
    } else if (isDragging) {
        const dx = e.clientX - lastMouseX;
        const dy = e.clientY - lastMouseY;
        if (Math.abs(dx) > 1 || Math.abs(dy) > 1) {
            rightPanMoved = true;
        }
        translateX += dx;
        translateY += dy;
        lastMouseX = e.clientX;
        lastMouseY = e.clientY;
        render();
    } else {
        let foundIdx = null;
        let foundDayKey = null;

        for (let h of hitboxes) {
            if (mouseX >= h.left && mouseX <= h.right && mouseY >= h.top && mouseY <= h.bottom) {
                if (h.isDayTitle) {
                    foundDayKey = h.date_str;
                } else {
                    foundIdx = h.idx;
                }
                break;
            }
        }

        if (foundIdx !== hoveredEventIdx || foundDayKey !== hoveredDayTitleKey) {
            hoveredEventIdx = foundIdx;
            hoveredDayTitleKey = foundDayKey;
            render();
        }

        if (foundIdx !== null) {
            canvas.style.cursor = 'grab';
        } else {
            canvas.style.cursor = 'default';
        }
    }
});

canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    const zoomIntensity = 0.1;
    const wheel = e.deltaY < 0 ? 1 : -1;
    const zoomFactor = Math.exp(wheel * zoomIntensity);

    const rect = canvas.getBoundingClientRect();
    const mouseX = e.clientX - rect.left;

    const worldX = (mouseX - translateX) / scaleX;
    scaleX *= zoomFactor;

    if (scaleX < 0.00001) scaleX = 0.00001;
    if (scaleX > 1000) scaleX = 1000;

    translateX = mouseX - worldX * scaleX;
    render();
}, { passive: false });

canvas.addEventListener('dblclick', (e) => {
    const rect = canvas.getBoundingClientRect();
    const mouseX = e.clientX - rect.left;
    const mouseY = e.clientY - rect.top;
    const MAX_SCALE = 1000;

    for (let h of hitboxes) {
        if (mouseX >= h.left && mouseX <= h.right && mouseY >= h.top && mouseY <= h.bottom) {
            if (h.isDayTitle) {
                const parsed = parseDateStr(h.date_str);
                const targetX = valToX(dateToFloat(parsed.year, parsed.month, parsed.day));
                scaleX = MAX_SCALE;
                translateX = (canvas.width / 2) - targetX * scaleX;
                render();
                return;
            } else if (h.idx !== null && h.idx !== undefined) {
                const ev = projectData.events[h.idx];
                if (ev) {
                    const targetX = valToX(ev.float_val);
                    scaleX = MAX_SCALE;
                    translateX = (canvas.width / 2) - targetX * scaleX;
                    render();
                    return;
                }
            }
        }
    }

    if (hoveredEventIdx !== null) {
        const ev = projectData.events[hoveredEventIdx];
        if (ev) {
            const targetX = valToX(ev.float_val);
            scaleX = MAX_SCALE;
            translateX = (canvas.width / 2) - targetX * scaleX;
            render();
        }
    }
});

let contextMenuTargetDayKey = null;
let contextMenuTargetTimelineName = null;

canvas.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    if (rightPanMoved) {
        rightPanMoved = false;
        return;
    }
    const rect = canvas.getBoundingClientRect();
    const mouseX = e.clientX - rect.left;
    const mouseY = e.clientY - rect.top;

    contextMenuTargetIdx = null;
    let targetDayKey = null;
    let targetTimelineName = null;

    // Check hitboxes first for precise right-click target
    for (let h of hitboxes) {
        if (mouseX >= h.left && mouseX <= h.right && mouseY >= h.top && mouseY <= h.bottom) {
            if (h.isDayTitle) {
                targetDayKey = h.date_str;
                break;
            } else if (h.isTimeline) {
                targetTimelineName = h.timelineName;
                break;
            } else if (h.idx !== null && h.idx !== undefined) {
                contextMenuTargetIdx = h.idx;
                break;
            }
        }
    }

    // Fallback to hover states
    if (targetDayKey === null && contextMenuTargetIdx === null && targetTimelineName === null) {
        if (hoveredDayTitleKey !== null) {
            targetDayKey = hoveredDayTitleKey;
        } else if (hoveredEventIdx !== null) {
            contextMenuTargetIdx = hoveredEventIdx;
        }
    }

    const ctxEventGroup = document.getElementById('ctxEventGroup');
    const ctxDayGroup = document.getElementById('ctxDayGroup');
    const ctxTimelineGroup = document.getElementById('ctxTimelineGroup');

    if (targetDayKey !== null) {
        contextMenuTargetDayKey = targetDayKey;
        contextMenuTargetTimelineName = null;
        if (ctxEventGroup) ctxEventGroup.style.display = 'none';
        if (ctxTimelineGroup) ctxTimelineGroup.style.display = 'none';
        if (ctxDayGroup) ctxDayGroup.style.display = 'block';
        contextMenu.style.left = (e.clientX - rect.left) + 'px';
        contextMenu.style.top = (e.clientY - rect.top) + 'px';
        contextMenu.style.display = 'block';
    } else if (targetTimelineName !== null) {
        contextMenuTargetTimelineName = targetTimelineName;
        contextMenuTargetDayKey = null;
        if (ctxEventGroup) ctxEventGroup.style.display = 'none';
        if (ctxDayGroup) ctxDayGroup.style.display = 'none';
        if (ctxTimelineGroup) ctxTimelineGroup.style.display = 'block';
        contextMenu.style.left = (e.clientX - rect.left) + 'px';
        contextMenu.style.top = (e.clientY - rect.top) + 'px';
        contextMenu.style.display = 'block';
    } else if (contextMenuTargetIdx !== null) {
        contextMenuTargetDayKey = null;
        contextMenuTargetTimelineName = null;
        if (ctxDayGroup) ctxDayGroup.style.display = 'none';
        if (ctxTimelineGroup) ctxTimelineGroup.style.display = 'none';
        if (ctxEventGroup) ctxEventGroup.style.display = 'block';

        const ev = (projectData && projectData.events) ? projectData.events[contextMenuTargetIdx] : null;

        const ctxJumpToBranch = document.getElementById('ctxJumpToBranch');
        if (ctxJumpToBranch) {
            if (ev && ev.type === 'trigger' && ev.target_branch && projectData.timelines?.[ev.target_branch]) {
                ctxJumpToBranch.innerText = `⚡ Jump to Arrival (${ev.target_branch})`;
                ctxJumpToBranch.style.display = 'block';
            } else {
                ctxJumpToBranch.style.display = 'none';
            }
        }

        const ctxLinkToChapter = document.getElementById('ctxLinkToChapter');
        if (ctxLinkToChapter && ev) {
            const chapStr = (ev.chapter_part || ev.name) || '';
            const match = chapStr.match(/Chapter\s+(\d+)(?:\s*-\s*Part\s+(\d+))?/i);
            if (match) {
                const ch = match[1];
                const pt = match[2];
                ctxLinkToChapter.dataset.targetId = pt ? `chapter-${ch}-part-${pt}` : `chapter-${ch}`;
                ctxLinkToChapter.style.display = 'block';
            } else {
                ctxLinkToChapter.style.display = 'none';
                ctxLinkToChapter.dataset.targetId = '';
            }
        }

        contextMenu.style.left = (e.clientX - rect.left) + 'px';
        contextMenu.style.top = (e.clientY - rect.top) + 'px';
        contextMenu.style.display = 'block';
    } else {
        contextMenu.style.display = 'none';
    }
});

document.addEventListener('click', (e) => {
    if (e.target.closest('#contextMenu')) return;
    contextMenu.style.display = 'none';
});

// Day Title Context Actions
const ctxEditDayTitle = document.getElementById('ctxEditDayTitle');
if (ctxEditDayTitle) {
    ctxEditDayTitle.addEventListener('click', () => {
        if (contextMenuTargetDayKey !== null) {
            openDayTitleModal(contextMenuTargetDayKey);
            contextMenu.style.display = 'none';
        }
    });
}

function deleteDayTitle(targetDateKey) {
    if (!projectData || !projectData.day_titles) return;
    const parsed = parseDateStr(targetDateKey);
    const normKey = normalizeDateStr(parsed.year, parsed.month, parsed.day);
    const unnormKey = `${parsed.year}-${parsed.month}-${parsed.day}`;
    const targetFloat = dateToFloat(parsed.year, parsed.month, parsed.day);

    for (let k of Object.keys(projectData.day_titles)) {
        const kp = parseDateStr(k);
        const kNorm = normalizeDateStr(kp.year, kp.month, kp.day);
        const val = projectData.day_titles[k];

        if (k === targetDateKey || k === normKey || k === unnormKey || kNorm === normKey || k.includes(normKey)) {
            delete projectData.day_titles[k];
        } else if (typeof val === 'object' && val.float_val !== undefined) {
            if (Math.abs(val.float_val - targetFloat) < 0.001) {
                delete projectData.day_titles[k];
            }
        }
    }

    if (projectData.events) {
        projectData.events.forEach(e => {
            if (e.date_str) {
                const ep = parseDateStr(e.date_str);
                if (normalizeDateStr(ep.year, ep.month, ep.day) === normKey) {
                    delete e.day_title;
                }
            }
        });
    }

    persistProjectData();
    render();
}

const ctxDeleteDayTitle = document.getElementById('ctxDeleteDayTitle');
if (ctxDeleteDayTitle) {
    ctxDeleteDayTitle.addEventListener('click', () => {
        if (contextMenuTargetDayKey !== null) {
            deleteDayTitle(contextMenuTargetDayKey);
            contextMenuTargetDayKey = null;
            contextMenu.style.display = 'none';
        }
    });
}

// Event Context Actions
const ctxJumpToBranch = document.getElementById('ctxJumpToBranch');
if (ctxJumpToBranch) {
    ctxJumpToBranch.addEventListener('click', () => {
        if (contextMenuTargetIdx !== null && projectData && projectData.events) {
            const ev = projectData.events[contextMenuTargetIdx];
            if (ev && ev.type === 'trigger' && ev.target_branch && projectData.timelines?.[ev.target_branch]) {
                const targetLine = projectData.timelines[ev.target_branch];
                const targetFloat = targetLine.start_val || 0.0;
                const targetX = valToX(targetFloat);

                scaleX = 1.0;
                translateX = (canvas.width / 2) - targetX * scaleX;
                translateY = (canvas.height / 2) - (targetLine.y || 0);

                contextMenu.style.display = 'none';
                render();
                return;
            }
        }
        contextMenu.style.display = 'none';
    });
}

const ctxEditEvent = document.getElementById('ctxEditEvent') || document.getElementById('ctxEdit');
if (ctxEditEvent) {
    ctxEditEvent.addEventListener('click', () => {
        if (contextMenuTargetIdx !== null) {
            openEventModal(contextMenuTargetIdx);
            contextMenu.style.display = 'none';
        }
    });
}

const ctxNameThisDay = document.getElementById('ctxNameThisDay') || document.getElementById('ctxNameDay');
if (ctxNameThisDay) {
    ctxNameThisDay.addEventListener('click', () => {
        if (contextMenuTargetIdx !== null) {
            const ev = projectData.events[contextMenuTargetIdx];
            contextMenu.style.display = 'none';
            if (ev) {
                const d = floatToDate(ev.float_val);
                const accurateDateStr = normalizeDateStr(d.year, d.month, d.day);
                openDayTitleModal(accurateDateStr);
            }
        }
    });
}

const ctxRemoveEvent = document.getElementById('ctxRemoveEvent') || document.getElementById('ctxRemove');
if (ctxRemoveEvent) {
    ctxRemoveEvent.addEventListener('click', () => {
        if (contextMenuTargetIdx !== null) {
            projectData.events.splice(contextMenuTargetIdx, 1);
            contextMenu.style.display = 'none';
            persistProjectData();
            render();
        }
    });
}

// Timeline Context Actions
const ctxEditTimeline = document.getElementById('ctxEditTimeline');
if (ctxEditTimeline) {
    ctxEditTimeline.addEventListener('click', () => {
        if (contextMenuTargetTimelineName !== null) {
            openTimelineEditModal(contextMenuTargetTimelineName);
            contextMenu.style.display = 'none';
        }
    });
}

const ctxDeleteTimeline = document.getElementById('ctxDeleteTimeline');
if (ctxDeleteTimeline) {
    ctxDeleteTimeline.addEventListener('click', () => {
        if (contextMenuTargetTimelineName !== null) {
            const tName = contextMenuTargetTimelineName;

            // Gather all timelines to delete (recursive)
            const timelinesToDelete = new Set([tName]);
            let added = true;
            while (added) {
                added = false;
                Object.keys(projectData.timelines).forEach(t => {
                    if (timelinesToDelete.has(projectData.timelines[t].parent) && !timelinesToDelete.has(t)) {
                        timelinesToDelete.add(t);
                        added = true;
                    }
                });
            }

            // Count events that will be deleted
            let eventCount = 0;
            projectData.events.forEach(e => {
                if (timelinesToDelete.has(e.line_name) || timelinesToDelete.has(e.target_branch)) {
                    eventCount++;
                }
            });

            if (confirm(`WARNING (Nuclear Deletion): You are about to permanently delete ${timelinesToDelete.size} timeline(s) [${Array.from(timelinesToDelete).join(', ')}] and ${eventCount} associated event(s). Are you absolutely sure?`)) {
                // Delete the timelines
                timelinesToDelete.forEach(t => {
                    delete projectData.timelines[t];
                });

                // Delete the events
                projectData.events = projectData.events.filter(e => {
                    return !timelinesToDelete.has(e.line_name) && !timelinesToDelete.has(e.target_branch);
                });

                persistProjectData();
                render();
            }
            contextMenuTargetTimelineName = null;
            contextMenu.style.display = 'none';
        }
    });
}

const ctxLinkToChapter = document.getElementById('ctxLinkToChapter');
if (ctxLinkToChapter) {
    ctxLinkToChapter.addEventListener('click', () => {
        if (contextMenuTargetIdx !== null) {
            const targetId = ctxLinkToChapter.dataset.targetId;
            if (targetId) {
                const rc1Path = '../../manuscripts/Killtime - Volume I - The Awakening Storm.html';
                window.open(`${rc1Path}#${targetId}`, 'killtime_rc1');
            }
            contextMenu.style.display = 'none';
        }
    });
}

// --- UI BUTTONS ---
document.getElementById('btnAddEvent').addEventListener('click', () => openEventModal(null));
document.getElementById('btnAddBranch').addEventListener('click', () => openBranchModal());
const btnNameDay = document.getElementById('btnNameDay');
if (btnNameDay) btnNameDay.addEventListener('click', () => openDayTitleModal(null));
document.getElementById('btnGotoDate').addEventListener('click', () => openGotoModal());
document.getElementById('btnFitAll').addEventListener('click', fitToScreen);
document.getElementById('btnSave').addEventListener('click', saveProject);

const btnResetData = document.getElementById('btnResetData');
if (btnResetData) {
    btnResetData.addEventListener('click', () => {
        if (confirm("Reset timeline data to the master River of Time.json?")) {
            localStorage.removeItem(LOCAL_STORAGE_KEY);
            fetch('../../scripts_and_data/River%20of%20Time.json')
                .then(res => res.json())
                .then(data => {
                    projectData = data;
                    projectData.day_titles = projectData.day_titles || {};
                    persistProjectData();
                    resizeCanvas();
                    if (projectData.events && projectData.events.length > 0) {
                        fitToScreen();
                    }
                })
                .catch(err => console.error("Error reloading master data:", err));
        }
    });
}

const btnFullscreen = document.getElementById('btnFullscreen');
if (btnFullscreen) {
    btnFullscreen.addEventListener('click', () => {
        const container = document.getElementById('engine-wrapper');
        const doc = window.document;
        const docEl = container;

        const requestFullScreen = docEl.requestFullscreen || docEl.mozRequestFullScreen || docEl.webkitRequestFullScreen || docEl.msRequestFullscreen;
        const cancelFullScreen = doc.exitFullscreen || doc.mozCancelFullScreen || doc.webkitExitFullscreen || doc.msExitFullscreen;

        if (!doc.fullscreenElement && !doc.mozFullScreenElement && !doc.webkitFullscreenElement && !doc.msFullscreenElement) {
            requestFullScreen.call(docEl);
        } else {
            cancelFullScreen.call(doc);
        }
    });
}

const btnColorTimeline = document.getElementById('btnColorTimeline');
if (btnColorTimeline) {
    btnColorTimeline.addEventListener('click', () => {
        const lineSelect = document.getElementById('eventLineSelect');
        const evColorInput = document.getElementById('eventColor');
        if (lineSelect && evColorInput && projectData && projectData.timelines) {
            const lineData = projectData.timelines[lineSelect.value];
            if (lineData && lineData.color) {
                evColorInput.value = lineData.color;
            }
        }
    });
}

document.querySelectorAll('.btn-color-swatch').forEach(btn => {
    btn.addEventListener('click', (e) => {
        const targetColor = e.currentTarget.getAttribute('data-color');
        const evColorInput = document.getElementById('eventColor');
        if (evColorInput && targetColor) {
            evColorInput.value = targetColor;
        }
    });
});

function updateLineDropdowns() {
    const sel = document.getElementById('eventLineSelect');
    const tSel = document.getElementById('eventTargetLineSelect');
    const pSel = document.getElementById('branchParentSelect');
    const dSel = document.getElementById('dayTitleLineSelect');
    if (sel) sel.innerHTML = '';
    if (tSel) tSel.innerHTML = '';
    if (pSel) pSel.innerHTML = '';
    if (dSel) dSel.innerHTML = '';
    if (!projectData || !projectData.timelines) return;
    Object.keys(projectData.timelines).forEach(k => {
        if (sel) sel.add(new Option(k, k));
        if (tSel) tSel.add(new Option(k, k));
        if (pSel) pSel.add(new Option(k, k));
        if (dSel) dSel.add(new Option(k, k));
    });
}

function openEventModal(idx) {
    updateLineDropdowns();
    document.getElementById('modalOverlay').style.display = 'flex';
    document.getElementById('eventModal').style.display = 'block';

    if (idx !== null) {
        const ev = projectData.events[idx];
        document.getElementById('eventModalTitle').innerText = 'Edit Event';
        document.getElementById('eventLineSelect').value = ev.line_name;
        document.getElementById('eventLineSelectLabel').innerText = ev.type === 'trigger' ? 'Origin Timeline:' : 'Timeline:';

        const d = floatToDate(ev.float_val);
        document.getElementById('eventY').value = d.year;
        document.getElementById('eventM').value = d.month;
        document.getElementById('eventD').value = d.day;

        document.getElementById('eventName').value = ev.name;
        document.getElementById('eventChapter').value = ev.chapter_part || '';
        const evColorInput = document.getElementById('eventColor');
        if (evColorInput) evColorInput.value = ev.color || '#ffffff';

        document.getElementById('btnSaveEvent').onclick = () => saveEvent(idx);

        if (ev.type === 'trigger') {
            document.getElementById('eventTargetLineGroup').style.display = 'block';
            document.getElementById('eventTargetLineSelect').value = ev.target_branch;
            document.getElementById('branchArrivalGroup').style.display = 'block';
            const arr = floatToDate(projectData.timelines[ev.target_branch].start_val);
            document.getElementById('eventArrivalY').value = arr.year;
            document.getElementById('eventArrivalM').value = arr.month;
            document.getElementById('eventArrivalD').value = arr.day;
        } else {
            document.getElementById('eventTargetLineGroup').style.display = 'none';
            document.getElementById('branchArrivalGroup').style.display = 'none';
        }
    } else {
        document.getElementById('eventModalTitle').innerText = 'Add Fixed Event (|)';
        document.getElementById('eventLineSelectLabel').innerText = 'Timeline:';
        document.getElementById('eventTargetLineGroup').style.display = 'none';
        document.getElementById('branchArrivalGroup').style.display = 'none';
        document.getElementById('eventName').value = '';
        document.getElementById('eventChapter').value = '';
        const evColorInput = document.getElementById('eventColor');
        if (evColorInput) evColorInput.value = '#ffffff';
        document.getElementById('btnSaveEvent').onclick = () => saveEvent(null);
    }
}

function saveEvent(idx) {
    const line_name = document.getElementById('eventLineSelect').value;
    const y = parseInt(document.getElementById('eventY').value);
    const m = parseInt(document.getElementById('eventM').value);
    const d = parseInt(document.getElementById('eventD').value);
    const float_val = dateToFloat(y, m, d);
    const date_str = normalizeDateStr(y, m, d);
    const name = document.getElementById('eventName').value;
    const chap = document.getElementById('eventChapter').value.trim();
    const evColorInput = document.getElementById('eventColor');
    const color = evColorInput ? (evColorInput.value || '#ffffff') : '#ffffff';

    if (idx !== null) {
        const ev = projectData.events[idx];
        ev.line_name = line_name;
        ev.float_val = float_val;
        ev.date_str = date_str;
        ev.name = name;
        ev.color = color;
        if (chap) ev.chapter_part = chap; else delete ev.chapter_part;

        if (ev.type === 'trigger') {
            const newTargetBranch = document.getElementById('eventTargetLineSelect').value;
            ev.target_branch = newTargetBranch;
            const ay = parseInt(document.getElementById('eventArrivalY').value);
            const am = parseInt(document.getElementById('eventArrivalM').value);
            const ad = parseInt(document.getElementById('eventArrivalD').value);
            projectData.timelines[newTargetBranch].start_val = dateToFloat(ay, am, ad);
            projectData.timelines[newTargetBranch].parent = line_name;
        }
    } else {
        const ev = { float_val, date_str, name, line_name, type: 'fixed', color };
        if (chap) ev.chapter_part = chap;
        projectData.events.push(ev);
    }

    persistProjectData();
    closeModal();
    render();
}

let editingOriginalDayTitleKey = null;

function getDayTitleForDate(year, month, day) {
    if (!projectData || !projectData.day_titles) return { title: '', line_name: null };
    const targetY = parseInt(year);
    const targetM = parseInt(month);
    const targetD = parseInt(day);
    const targetNorm = normalizeDateStr(targetY, targetM, targetD);
    const targetFloat = dateToFloat(targetY, targetM, targetD);

    for (let k of Object.keys(projectData.day_titles)) {
        const val = projectData.day_titles[k];
        const title = typeof val === 'string' ? val : (val?.title || '');
        if (!title || !title.trim()) continue;

        const kp = parseDateStr(k);
        const kNorm = normalizeDateStr(kp.year, kp.month, kp.day);

        if (kNorm === targetNorm || k === targetNorm) {
            const line_name = (typeof val === 'object' && val.line_name) ? val.line_name : null;
            return { title: title.trim(), line_name };
        }

        if (typeof val === 'object' && val.float_val !== undefined) {
            if (Math.abs(val.float_val - targetFloat) < 0.001) {
                return { title: title.trim(), line_name: val.line_name || null };
            }
        }
    }

    return { title: '', line_name: null };
}

function openDayTitleModal(targetDateStr = null) {
    updateLineDropdowns();
    document.getElementById('modalOverlay').style.display = 'flex';
    const modal = document.getElementById('dayTitleModal');
    if (modal) modal.style.display = 'block';

    const lineSel = document.getElementById('dayTitleLineSelect');
    const yEl = document.getElementById('dayTitleY');
    const mEl = document.getElementById('dayTitleM');
    const dEl = document.getElementById('dayTitleD');
    const input = document.getElementById('dayTitleInput');

    let y = 1772, m = 2, d = 3;
    if (targetDateStr) {
        const parsed = parseDateStr(targetDateStr);
        y = parsed.year;
        m = parsed.month;
        d = parsed.day;
        editingOriginalDayTitleKey = normalizeDateStr(y, m, d);
    } else {
        y = yEl ? (parseInt(yEl.value) || 1772) : 1772;
        m = mEl ? (parseInt(mEl.value) || 2) : 2;
        d = dEl ? (parseInt(dEl.value) || 3) : 3;
        editingOriginalDayTitleKey = null;
    }

    if (yEl) { yEl.value = y; yEl.oninput = null; }
    if (mEl) { mEl.value = m; mEl.oninput = null; }
    if (dEl) { dEl.value = d; dEl.oninput = null; }

    // Load existing day title (if any) for this specific day
    const existing = getDayTitleForDate(y, m, d);
    if (input) {
        input.value = existing.title || '';
    }

    if (lineSel) {
        if (existing.line_name && projectData.timelines?.[existing.line_name]) {
            lineSel.value = existing.line_name;
        } else {
            const normKey = normalizeDateStr(y, m, d);
            const matchEv = projectData.events?.find(e => {
                if (!e.date_str) return false;
                const ep = parseDateStr(e.date_str);
                return normalizeDateStr(ep.year, ep.month, ep.day) === normKey;
            });
            if (matchEv && lineSel) lineSel.value = matchEv.line_name;
        }
    }

    if (input) {
        input.focus();
        input.select();
    }
}

const btnCancelDayTitle = document.getElementById('btnCancelDayTitle');
if (btnCancelDayTitle) btnCancelDayTitle.onclick = closeModal;

const btnSaveDayTitle = document.getElementById('btnSaveDayTitle');
if (btnSaveDayTitle) {
    btnSaveDayTitle.onclick = () => {
        const y = parseInt(document.getElementById('dayTitleY').value);
        const m = parseInt(document.getElementById('dayTitleM').value);
        const d = parseInt(document.getElementById('dayTitleD').value);
        const date_str = normalizeDateStr(y, m, d);
        const float_val = dateToFloat(y, m, d);
        const line_name = document.getElementById('dayTitleLineSelect')?.value || Object.keys(projectData.timelines)[0] || 'Main Line';
        const title = document.getElementById('dayTitleInput').value.trim();

        if (!projectData.day_titles) projectData.day_titles = {};

        // If moved date, delete the old date key
        if (editingOriginalDayTitleKey && editingOriginalDayTitleKey !== date_str) {
            deleteDayTitle(editingOriginalDayTitleKey);
        }

        if (title) {
            projectData.day_titles[date_str] = {
                title: title,
                line_name: line_name,
                float_val: float_val
            };
            persistProjectData();
            closeModal();
            render();
        } else {
            deleteDayTitle(date_str);
            closeModal();
        }

        editingOriginalDayTitleKey = null;
    };
}

// --- DYNAMIC TRACK ALLOCATION & COMPACTION ---
const TRACK_SPACING = 110;

function findOptimalBranchY(parentLineName) {
    const timelines = projectData.timelines || {};
    const parentData = timelines[parentLineName];
    const parentY = parentData ? (parentData.y || 0) : 0;
    const occupiedY = Object.values(timelines).map(t => t.y || 0);

    // Expand outward from the parent's actual Y coordinate
    for (let step = 1; step <= 50; step++) {
        const candidates = [
            parentY + (step * TRACK_SPACING),
            parentY - (step * TRACK_SPACING)
        ];
        for (let cand of candidates) {
            const collision = occupiedY.some(y => Math.abs(y - cand) < (TRACK_SPACING * 0.7));
            if (!collision) {
                return cand;
            }
        }
    }
    return parentY + TRACK_SPACING;
}

function compactTimelineTracks() {
    if (!projectData || !projectData.timelines) return;
    const timelines = projectData.timelines;

    // Lock the root Main Line to Y = 0
    const mainName = Object.keys(timelines).find(k => timelines[k].parent === null) || Object.keys(timelines)[0];
    if (timelines[mainName]) {
        timelines[mainName].y = 0;
    }

    const nonMain = Object.keys(timelines).filter(k => k !== mainName);

    // Sort tracks by their existing relative positions
    const aboveTracks = nonMain.filter(k => (timelines[k].y || 0) < 0)
        .sort((a, b) => (timelines[b].y || 0) - (timelines[a].y || 0)); // Closest to 0 first
    const belowTracks = nonMain.filter(k => (timelines[k].y || 0) >= 0)
        .sort((a, b) => (timelines[a].y || 0) - (timelines[b].y || 0)); // Closest to 0 first

    aboveTracks.forEach((name, idx) => {
        timelines[name].y = -((idx + 1) * TRACK_SPACING);
    });

    belowTracks.forEach((name, idx) => {
        timelines[name].y = ((idx + 1) * TRACK_SPACING);
    });

    persistProjectData();
    render();
}

function openBranchModal() {
    updateLineDropdowns();
    document.getElementById('branchTriggerName').value = '';
    document.getElementById('branchNewName').value = '';
    document.getElementById('modalOverlay').style.display = 'flex';
    document.getElementById('branchModal').style.display = 'block';
}

function openTimelineEditModal(name) {
    const tData = projectData.timelines[name];
    if (!tData) return;

    document.getElementById('timelineEditName').value = name;
    document.getElementById('timelineEditColor').value = tData.color || '#ffffff';
    document.getElementById('timelineEditY').value = tData.y || 0;

    if (tData.start_val !== undefined) {
        const d = floatToDate(tData.start_val);
        document.getElementById('timelineEditArrY').value = d.year;
        document.getElementById('timelineEditArrM').value = d.month;
        document.getElementById('timelineEditArrD').value = d.day;
    }

    const select = document.getElementById('timelineEditParentSelect');
    select.innerHTML = '<option value="">(None - Main Line)</option>';
    Object.keys(projectData.timelines).forEach(tName => {
        if (tName !== name) {
            const opt = document.createElement('option');
            opt.value = tName;
            opt.textContent = tName;
            if (tData.parent === tName) opt.selected = true;
            select.appendChild(opt);
        }
    });

    document.getElementById('modalOverlay').style.display = 'flex';
    const modal = document.getElementById('timelineModal');
    if (modal) modal.style.display = 'block';
}

const btnSaveTimeline = document.getElementById('btnSaveTimeline');
if (btnSaveTimeline) {
    btnSaveTimeline.addEventListener('click', () => {
        const oldName = contextMenuTargetTimelineName;
        const newName = document.getElementById('timelineEditName').value.trim();
        if (!newName) { alert("Timeline name cannot be empty."); return; }

        if (newName !== oldName && projectData.timelines[newName]) {
            alert("A timeline with this name already exists.");
            return;
        }

        const color = document.getElementById('timelineEditColor').value;
        const yOff = parseInt(document.getElementById('timelineEditY').value);
        const ay = parseInt(document.getElementById('timelineEditArrY').value);
        const am = parseInt(document.getElementById('timelineEditArrM').value);
        const ad = parseInt(document.getElementById('timelineEditArrD').value);
        const parentSel = document.getElementById('timelineEditParentSelect').value;
        const parent = parentSel === "" ? null : parentSel;

        const tData = projectData.timelines[oldName];
        if (tData) {
            tData.color = color;
            tData.y = isNaN(yOff) ? tData.y : yOff;
            tData.start_val = dateToFloat(ay, am, ad);
            tData.parent = parent;

            // Handle rename cascades
            if (newName !== oldName) {
                projectData.timelines[newName] = tData;
                delete projectData.timelines[oldName];

                Object.values(projectData.timelines).forEach(t => {
                    if (t.parent === oldName) t.parent = newName;
                });

                projectData.events.forEach(e => {
                    if (e.line_name === oldName) e.line_name = newName;
                    if (e.target_branch === oldName) e.target_branch = newName;
                });
            }
        }

        persistProjectData();
        closeModal();
        render();
    });
}

document.getElementById('btnSaveBranch').onclick = () => {
    const parentLine = document.getElementById('branchParentSelect').value;
    const dy = parseInt(document.getElementById('branchDepY').value);
    const dm = parseInt(document.getElementById('branchDepM').value);
    const dd = parseInt(document.getElementById('branchDepD').value);
    const ay = parseInt(document.getElementById('branchArrY').value);
    const am = parseInt(document.getElementById('branchArrM').value);
    const ad = parseInt(document.getElementById('branchArrD').value);

    const tName = document.getElementById('branchTriggerName').value;
    const bName = document.getElementById('branchNewName').value;

    if (!bName || projectData.timelines[bName]) {
        alert("Branch name empty or exists!");
        return;
    }

    const yOff = findOptimalBranchY(parentLine);

    if (projectData.color_index === undefined) projectData.color_index = 0;
    const color = branchColors[projectData.color_index % branchColors.length];
    projectData.color_index++;

    projectData.timelines[bName] = { y: yOff, color, start_val: dateToFloat(ay, am, ad), parent: parentLine };
    projectData.events.push({
        float_val: dateToFloat(dy, dm, dd),
        date_str: normalizeDateStr(dy, dm, dd),
        name: tName, line_name: parentLine, type: 'trigger', target_branch: bName
    });

    persistProjectData();
    closeModal();
    render();
};

function openGotoModal() {
    document.getElementById('modalOverlay').style.display = 'flex';
    document.getElementById('gotoModal').style.display = 'block';
}

document.getElementById('btnJumpDate').onclick = () => {
    const y = parseInt(document.getElementById('gotoY').value);
    const m = parseInt(document.getElementById('gotoM').value);
    const d = parseInt(document.getElementById('gotoD').value);
    const float_val = dateToFloat(y, m, d);

    scaleX = 1.0;
    translateX = (canvas.width / 2) - valToX(float_val) * scaleX;
    closeModal();
    render();
};

function closeModal() {
    document.getElementById('modalOverlay').style.display = 'none';
    document.getElementById('eventModal').style.display = 'none';
    document.getElementById('branchModal').style.display = 'none';
    document.getElementById('gotoModal').style.display = 'none';
    const dayModal = document.getElementById('dayTitleModal');
    if (dayModal) dayModal.style.display = 'none';
    const tModal = document.getElementById('timelineModal');
    if (tModal) tModal.style.display = 'none';
}
document.getElementById('btnCancelEvent').onclick = closeModal;
document.getElementById('btnCancelBranch').onclick = closeModal;
document.getElementById('btnCancelGoto').onclick = closeModal;
const btnCancelTimeline = document.getElementById('btnCancelTimeline');
if (btnCancelTimeline) btnCancelTimeline.onclick = closeModal;

function fitToScreen() {
    if (!projectData) return;
    let vals = (projectData.events || []).map(e => e.float_val);
    Object.values(projectData.timelines || {}).forEach(t => {
        if (t.parent !== null) vals.push(t.start_val || 0);
    });
    let minVal = -500; let maxVal = 500;
    if (vals.length > 0) {
        minVal = Math.min(...vals) - 500;
        maxVal = Math.max(...vals) + 500;
    }

    const w = valToX(maxVal) - valToX(minVal);
    scaleX = (canvas.width * 0.98) / w;
    const centerVal = (minVal + maxVal) / 2;
    translateX = (canvas.width / 2) - valToX(centerVal) * scaleX;
    render();
}

function saveProject() {
    const dataStr = "data:text/json;charset=utf-8," + encodeURIComponent(JSON.stringify(projectData, null, 4));
    const a = document.createElement('a');
    a.href = dataStr;
    a.download = "River of Time.json";
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
}

// --- RENDERING ---
function drawInfiniteLine(x1, x2, y) {
    const pad = 200;
    const drawX1 = Math.max(-pad, Math.min(x1, canvas.width + pad));
    const drawX2 = Math.max(-pad, Math.min(x2, canvas.width + pad));
    if (Math.abs(drawX2 - drawX1) < 0.1) return;
    ctx.beginPath();
    ctx.moveTo(drawX1, y);
    ctx.lineTo(drawX2, y);
    ctx.stroke();
}

function drawArrow(x, y, pointingDown, color) {
    ctx.strokeStyle = color;
    ctx.lineWidth = 3;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.beginPath();
    const size = 7;
    if (pointingDown) {
        ctx.moveTo(x - size, y - size);
        ctx.lineTo(x, y);
        ctx.lineTo(x + size, y - size);
    } else {
        ctx.moveTo(x - size, y + size);
        ctx.lineTo(x, y);
        ctx.lineTo(x + size, y + size);
    }
    ctx.stroke();
}

function isDarkTheme() {
    return document.documentElement.getAttribute('data-theme') !== 'light';
}

function drawGrid() {
    const minXScreen = 0;
    const maxXScreen = canvas.width;

    const minVal = (minXScreen - translateX) / (PIXELS_PER_YEAR * scaleX);
    const maxVal = (maxXScreen - translateX) / (PIXELS_PER_YEAR * scaleX);
    const valRange = maxVal - minVal;

    const steps = [
        100000, 10000, 5000, 1000, 500, 100, 50, 10, 5, 1,
        0.5, 0.1, 10 / 300.0, 5 / 300.0, 1 / 300.0
    ];

    let step = steps[steps.length - 1];
    for (let s of steps) {
        if (valRange / s >= 4) {
            step = s;
            break;
        }
    }

    const dark = isDarkTheme();

    ctx.fillStyle = dark ? '#0f1216' : '#f8fafc';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    ctx.strokeStyle = dark ? '#282d32' : '#e2e8f0';
    ctx.lineWidth = 1;

    const startCount = Math.floor(minVal / step);
    const endCount = Math.ceil(maxVal / step);

    for (let i = startCount; i <= endCount; i++) {
        const v = i * step;
        const screenX = translateX + v * PIXELS_PER_YEAR * scaleX;

        ctx.beginPath();
        ctx.moveTo(screenX, 0);
        ctx.lineTo(screenX, canvas.height);
        ctx.stroke();
    }

    ctx.fillStyle = dark ? 'rgba(15, 18, 22, 1)' : 'rgba(241, 245, 249, 1)';
    ctx.fillRect(0, 0, canvas.width, 25);

    ctx.fillStyle = dark ? '#b4b4b4' : '#475569';
    ctx.font = 'bold 10px Arial';

    for (let i = startCount; i <= endCount; i++) {
        const v = i * step;
        const screenX = translateX + v * PIXELS_PER_YEAR * scaleX;
        const d = floatToDate(v);

        let label = "";
        if (step >= 1) {
            label = `Year ${d.year}`;
        } else if (step >= 0.1) {
            if (d.day === 1) {
                label = `${d.year}-${String(d.month).padStart(2, '0')}`;
            } else {
                label = `${d.year}-${String(d.month).padStart(2, '0')}-${String(d.day).padStart(2, '0')}`;
            }
        } else {
            label = `${d.year}-${String(d.month).padStart(2, '0')}-${String(d.day).padStart(2, '0')}`;
        }

        ctx.fillText(label, screenX + 5, 18);
    }
}

function drawEvents() {
    const placedRects = [];
    const dayGroupLastY = {};
    const padX = 14;
    const padY = 2;

    const events = projectData.events || [];
    const timelines = projectData.timelines || {};
    const dark = isDarkTheme();

    // Group events by timeline and date to calculate diagonal stair-step indices
    const dayGroups = {};
    events.forEach((e, idx) => {
        const d = floatToDate(e.float_val);
        const normDate = normalizeDateStr(d.year, d.month, d.day);
        e.date_str = normDate;
        const key = `${e.line_name}_${normDate}`;
        if (!dayGroups[key]) dayGroups[key] = [];
        dayGroups[key].push(idx);
    });

    const intraDayIndex = new Array(events.length).fill(0);
    const dayTotalCounts = new Array(events.length).fill(1);
    Object.values(dayGroups).forEach(group => {
        group.forEach((eventIdx, pos) => {
            intraDayIndex[eventIdx] = pos;
            dayTotalCounts[eventIdx] = group.length;
        });
    });

    // Chronologically sort event indices for layout computation to eliminate insertion-order collision drift
    const sortedEventIndices = events.map((e, idx) => ({ e, idx }))
        .sort((a, b) => a.e.float_val - b.e.float_val || a.idx - b.idx);

    sortedEventIndices.forEach(({ e, idx }) => {
        const line = timelines[e.line_name];
        if (!line) return;

        const d = floatToDate(e.float_val);
        const cleanDayFloat = dateToFloat(d.year, d.month, d.day);
        const screenX = translateX + valToX(cleanDayFloat) * scaleX;
        const lineY = translateY + line.y;

        const isHovered = (idx === hoveredEventIdx) || (hoveredDayTitleKey === e.date_str);
        const hlColor = dark ? '#00ffff' : '#0284c7';

        const dayPos = intraDayIndex[idx];
        const totalOnDay = dayTotalCounts[idx];

        // Diagonally space multiple entries on the same date:
        const stepX = 6;
        const stepY = 10;
        const offsetX = totalOnDay > 1 ? dayPos * stepX : 0;
        const offsetY = totalOnDay > 1 ? dayPos * stepY : 0;
        const tickX = Math.round(screenX + offsetX);
        const tickY = Math.round(lineY - offsetY);

        const markerOffset = e.type === 'trigger' ? 8 : 15;
        const customColor = e.color || '#ffffff';
        const isWhiteDefault = !e.color || e.color.toLowerCase() === '#ffffff' || e.color.toLowerCase() === '#fff';
        const defaultTextColor = dark ? '#e2e8f0' : '#1e293b';
        const effectiveEventColor = isWhiteDefault ? defaultTextColor : customColor;
        const effectiveMarkerColor = isWhiteDefault ? (dark ? '#ffffff' : '#334155') : customColor;

        ctx.lineWidth = 3;
        if (e.type === 'trigger') {
            ctx.strokeStyle = isHovered ? hlColor : line.color;
            ctx.fillStyle = isHovered ? hlColor : (dark ? '#0f1216' : '#f8fafc');
            ctx.beginPath();
            ctx.arc(tickX, tickY, markerOffset, 0, Math.PI * 2);
            ctx.fill();
            ctx.stroke();
        } else {
            ctx.strokeStyle = isHovered ? hlColor : effectiveMarkerColor;
            ctx.beginPath();
            ctx.moveTo(tickX, tickY - markerOffset);
            ctx.lineTo(tickX, tickY + markerOffset);
            ctx.stroke();
        }

        // Add hitbox for the tick marker
        hitboxes.push({
            idx,
            left: tickX - 6,
            right: tickX + 6,
            top: tickY - markerOffset - 2,
            bottom: tickY + markerOffset + 2
        });

        const chap = e.chapter_part ? `[${e.chapter_part}] ` : '';
        const name = `${chap}${e.name}`;
        const date = `[${e.date_str}]`;

        ctx.font = 'bold 10px monospace';
        const nameWidth = ctx.measureText(name).width;
        const dateWidth = ctx.measureText(date).width;
        const w = Math.max(nameWidth, dateWidth) + 4;
        const h = 24;

        const baseX = screenX - (w / 2);
        const groupKey = `${e.line_name}_${e.date_str}`;
        const minStartY = (dayGroupLastY[groupKey] !== undefined)
            ? (dayGroupLastY[groupKey] + 35)
            : (lineY + 15 + 10);
        let currentY = minStartY;

        let overlap = true;
        let iterations = 0;
        const maxIter = events.length + 50;
        while (overlap && iterations < maxIter) {
            overlap = false;
            const rect1Left = baseX - padX;
            const rect1Right = baseX + w + padX;
            const rect1Top = currentY - padY;
            const rect1Bottom = currentY + h + padY;

            for (let i = 0; i < placedRects.length; i++) {
                const p = placedRects[i];
                if (!(rect1Right <= p[0] || rect1Left >= p[1] || rect1Bottom <= p[2] || rect1Top >= p[3])) {
                    overlap = true;
                    currentY += 35;
                    break;
                }
            }
            iterations++;
        }

        placedRects.push([baseX, baseX + w, currentY, currentY + h]);
        dayGroupLastY[groupKey] = currentY;
        hitboxes.push({ idx, left: baseX, right: baseX + w, top: currentY, bottom: currentY + h });

        // Connecting dashed line
        ctx.strokeStyle = isHovered ? hlColor : (dark ? 'rgba(100, 100, 100, 0.6)' : 'rgba(150, 150, 150, 0.6)');
        ctx.lineWidth = isHovered ? 2 : 1;
        ctx.setLineDash([2, 4]);
        ctx.beginPath();
        ctx.moveTo(tickX, tickY + markerOffset);
        ctx.lineTo(tickX, currentY);
        ctx.stroke();
        ctx.setLineDash([]);

        ctx.fillStyle = isHovered ? hlColor : (e.type === 'trigger' ? (dark ? '#ff6464' : '#dc2626') : effectiveEventColor);
        ctx.fillText(name, baseX + 2, currentY + 10);
        ctx.fillText(date, baseX + 2, currentY + 22);
    });

    // Dedicated Day Titles Renderer
    drawDayTitles(dayGroups);
}

function drawDayTitles(dayGroups) {
    if (!projectData || !projectData.day_titles) return;
    const dark = isDarkTheme();
    const hlColor = dark ? '#00ffff' : '#0284c7';
    const timelines = projectData.timelines || {};
    const events = projectData.events || [];

    // Collect all day titles
    const allTitles = Object.assign({}, projectData.day_titles);
    const placedDayTitles = [];

    const titleEntries = Object.keys(allTitles).map(dateKey => {
        const rawEntry = allTitles[dateKey];
        if (!rawEntry) return null;
        const titleText = (typeof rawEntry === 'string') ? rawEntry : (rawEntry.title || '');
        if (!titleText.trim()) return null;

        const parsed = parseDateStr(dateKey);
        const normDateKey = normalizeDateStr(parsed.year, parsed.month, parsed.day);
        const floatVal = (typeof rawEntry === 'object' && rawEntry.float_val !== undefined)
            ? rawEntry.float_val
            : dateToFloat(parsed.year, parsed.month, parsed.day);

        return { dateKey, rawEntry, titleText, normDateKey, floatVal };
    }).filter(e => e !== null);

    // Sort descending by floatVal (latest first) so upward stacking pushes earlier dates to the top
    titleEntries.sort((a, b) => b.floatVal - a.floatVal);

    titleEntries.forEach(entry => {
        const { dateKey, rawEntry, titleText, normDateKey, floatVal } = entry;

        // Find ALL events matching this normalized dateKey
        const matchEvents = events.filter(e => {
            if (e.date_str) {
                const ep = parseDateStr(e.date_str);
                if (normalizeDateStr(ep.year, ep.month, ep.day) === normDateKey) return true;
            }
            return Math.abs(e.float_val - floatVal) < 0.001;
        });

        // Determine timeline line & spatial coordinates
        let lineName = null;
        let eventFloat = floatVal;

        if (matchEvents.length > 0) {
            // ALWAYS lock to the actual timeline and float_val where the events exist
            lineName = matchEvents[0].line_name;
            eventFloat = matchEvents[0].float_val;
        } else if (typeof rawEntry === 'object' && rawEntry.line_name && timelines[rawEntry.line_name]) {
            lineName = rawEntry.line_name;
        } else {
            lineName = Object.keys(timelines)[0] || 'Main Line';
        }

        const line = timelines[lineName];
        if (!line) return;

        const N = matchEvents.length;
        const lineY = translateY + line.y;
        const screenX = translateX + valToX(eventFloat) * scaleX;

        // Exactly match the stair-step geometry used in drawEvents:
        // stepX = 6px, stepY = 10px, markerOffset = 15px
        const maxTickIndex = N > 1 ? (N - 1) : 0;
        const absoluteHighestTickTop = lineY - (maxTickIndex * 10) - 15;
        const clusterCenterX = screenX + (maxTickIndex * 6 / 2);

        // Badge Dimensions & Clean Clearance (22px clear vertical air gap above the peak tick)
        ctx.font = 'bold 11px monospace';
        const displayLabel = `★ ${titleText} ★`;
        const titleWidth = ctx.measureText(displayLabel).width;
        const badgeW = titleWidth + 18;
        const badgeH = 22;

        const clearanceAboveTicks = 22;
        let badgeY = absoluteHighestTickTop - clearanceAboveTicks - badgeH;
        const badgeX = clusterCenterX - (badgeW / 2);

        const padX = 4;
        let highestY = badgeY;

        for (let i = 0; i < placedDayTitles.length; i++) {
            const p = placedDayTitles[i];
            const rectLeft = badgeX - padX;
            const rectRight = badgeX + badgeW + padX;

            // Check horizontal overlap
            if (!(rectRight <= p[0] || rectLeft >= p[1])) {
                const candidateY = p[2] - badgeH - 8;
                if (candidateY < highestY) {
                    highestY = candidateY;
                }
            }
        }
        badgeY = highestY;

        const badgeBottomY = badgeY + badgeH;
        const titleCenterY = badgeY + (badgeH / 2);
        placedDayTitles.push([badgeX, badgeX + badgeW, badgeY, badgeY + badgeH]);

        const isGroupHovered = (hoveredDayTitleKey === dateKey) ||
            (matchEvents.length > 0 && matchEvents.some(ev => events.indexOf(ev) === hoveredEventIdx));

        // Dashed connector stem cleanly bridging the gap between badge bottom and peak tick
        const stemTargetY = N > 0 ? absoluteHighestTickTop : (lineY - 8);
        ctx.strokeStyle = isGroupHovered ? hlColor : (dark ? 'rgba(255, 215, 0, 0.7)' : 'rgba(180, 83, 9, 0.7)');
        ctx.lineWidth = isGroupHovered ? 2 : 1;
        ctx.setLineDash([2, 3]);
        ctx.beginPath();
        ctx.moveTo(clusterCenterX, badgeBottomY);
        ctx.lineTo(clusterCenterX, stemTargetY);
        ctx.stroke();
        ctx.setLineDash([]);

        // If no events exist on this date, draw an anchor tick on the line
        if (N === 0) {
            ctx.strokeStyle = isGroupHovered ? hlColor : (dark ? '#ffd700' : '#b45309');
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.moveTo(clusterCenterX, lineY - 8);
            ctx.lineTo(clusterCenterX, lineY + 8);
            ctx.stroke();
        }

        // Draw Badge Background
        ctx.fillStyle = isGroupHovered ? (dark ? '#1e293b' : '#fef08a') : (dark ? 'rgba(22, 18, 10, 0.94)' : 'rgba(254, 243, 199, 0.96)');
        ctx.strokeStyle = isGroupHovered ? hlColor : (dark ? '#ffd700' : '#b45309');
        ctx.lineWidth = 1.5;
        if (ctx.roundRect) {
            ctx.beginPath();
            ctx.roundRect(badgeX, badgeY, badgeW, badgeH, 4);
            ctx.fill();
            ctx.stroke();
        } else {
            ctx.fillRect(badgeX, badgeY, badgeW, badgeH);
            ctx.strokeRect(badgeX, badgeY, badgeW, badgeH);
        }

        // Draw Text
        ctx.fillStyle = isGroupHovered ? hlColor : (dark ? '#ffd700' : '#92400e');
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(displayLabel, clusterCenterX, titleCenterY);
        ctx.textAlign = 'left';
        ctx.textBaseline = 'alphabetic';

        // Hitbox for Click / Hover / Context Menu
        hitboxes.push({
            idx: matchEvents.length > 0 ? events.indexOf(matchEvents[0]) : null,
            isDayTitle: true,
            date_str: dateKey,
            line_name: lineName,
            title: titleText,
            left: badgeX,
            right: badgeX + badgeW,
            top: badgeY,
            bottom: badgeY + badgeH
        });
    });
}

function render() {
    hitboxes = [];
    drawGrid();

    if (!projectData) return;

    const timelines = projectData.timelines || {};
    const events = projectData.events || [];

    const drawnJumpsTop = [];
    const drawnJumpsBottom = [];
    const minX = -5000000 * PIXELS_PER_YEAR;
    const maxX = 5000000 * PIXELS_PER_YEAR;

    Object.keys(timelines).forEach(name => {
        const data = timelines[name];
        const yPos = data.y;
        const startX = valToX(data.start_val || 0.0);
        const color = data.color;
        const screenY = translateY + yPos;

        ctx.strokeStyle = color;

        if (data.parent === null) {
            ctx.lineWidth = 4;
            drawInfiniteLine(translateX + minX * scaleX, translateX + maxX * scaleX, screenY);

            ctx.fillStyle = color;
            ctx.font = 'bold 14px Arial';
            ctx.fillText(name, 50, screenY - 35);
            const textWidth = ctx.measureText(name).width;
            hitboxes.push({
                isTimeline: true, timelineName: name,
                left: 50 - 5, right: 50 + textWidth + 5,
                top: screenY - 35 - 15, bottom: screenY - 35 + 5
            });
        } else {
            const parentData = timelines[data.parent];
            const parentY = parentData ? parentData.y : 0;
            const parentScreenY = translateY + parentY;

            let triggerX = startX;
            const triggerEvent = events.find(e => e.target_branch === name);
            if (triggerEvent) triggerX = valToX(triggerEvent.float_val);

            const isTop = yPos < parentY;
            let baseControlYOffset = 120;
            const relevantJumps = isTop ? drawnJumpsTop : drawnJumpsBottom;

            const minJumpX = Math.min(triggerX, startX);
            const maxJumpX = Math.max(triggerX, startX);

            relevantJumps.forEach(jump => {
                if (!(maxJumpX < jump.x1 || minJumpX > jump.x2)) {
                    baseControlYOffset = Math.max(baseControlYOffset, jump.offset + 70);
                }
            });
            relevantJumps.push({ x1: minJumpX, x2: maxJumpX, offset: baseControlYOffset });

            const controlY = isTop ? Math.min(parentY, yPos) - baseControlYOffset : Math.max(parentY, yPos) + baseControlYOffset;
            const screenControlY = translateY + controlY;
            const screenTriggerX = translateX + triggerX * scaleX;
            const screenStartX = translateX + startX * scaleX;

            ctx.lineWidth = 3;
            ctx.setLineDash([5, 10]);
            ctx.lineCap = 'round';

            const dx = Math.abs(screenStartX - screenTriggerX);
            const pad = 200;
            const viewWidth = canvas.width;

            if (dx < 1.0) {
                const minSX = Math.min(screenTriggerX, screenStartX);
                const maxSX = Math.max(screenTriggerX, screenStartX);
                if (!(maxSX < -pad || minSX > viewWidth + pad)) {
                    ctx.beginPath();
                    ctx.moveTo(screenTriggerX, parentScreenY);
                    ctx.bezierCurveTo(screenTriggerX - 40, screenControlY, screenStartX + 40, screenControlY, screenStartX, screenY);
                    ctx.stroke();
                }
            } else {
                const dirX = screenStartX >= screenTriggerX ? 1 : -1;
                const cruise = Math.min(75.0, dx / 2.0);
                const upEndX = screenTriggerX + (cruise * dirX);
                const downStartX = screenStartX - (cruise * dirX);
                const kappa = 0.55228;

                // 1. Departure Curve
                const arc1Min = Math.min(screenTriggerX, upEndX);
                const arc1Max = Math.max(screenTriggerX, upEndX);
                if (!(arc1Max < -pad || arc1Min > viewWidth + pad)) {
                    ctx.beginPath();
                    ctx.moveTo(screenTriggerX, parentScreenY);
                    const cp1Y = parentScreenY + (screenControlY - parentScreenY) * kappa;
                    const cp2X = upEndX - (cruise * dirX) * kappa;
                    ctx.bezierCurveTo(screenTriggerX, cp1Y, cp2X, screenControlY, upEndX, screenControlY);
                    ctx.stroke();
                }

                // 2. Clamped Horizontal Bypass
                const hx1 = Math.min(upEndX, downStartX);
                const hx2 = Math.max(upEndX, downStartX);
                const drawHx1 = Math.max(-pad, Math.min(hx1, viewWidth + pad));
                const drawHx2 = Math.max(-pad, Math.min(hx2, viewWidth + pad));

                if (drawHx2 - drawHx1 > 0.1) {
                    if (drawHx1 > hx1) {
                        ctx.lineDashOffset = -((drawHx1 - hx1) % 15);
                    } else {
                        ctx.lineDashOffset = 0;
                    }
                    ctx.beginPath();
                    ctx.moveTo(drawHx1, screenControlY);
                    ctx.lineTo(drawHx2, screenControlY);
                    ctx.stroke();
                    ctx.lineDashOffset = 0;
                }

                // 3. Arrival Curve
                const arc2Min = Math.min(downStartX, screenStartX);
                const arc2Max = Math.max(downStartX, screenStartX);
                if (!(arc2Max < -pad || arc2Min > viewWidth + pad)) {
                    ctx.beginPath();
                    ctx.moveTo(downStartX, screenControlY);
                    const cp3X = downStartX + (cruise * dirX) * kappa;
                    const cp4Y = screenY - (screenY - screenControlY) * kappa;
                    ctx.bezierCurveTo(cp3X, screenControlY, screenStartX, cp4Y, screenStartX, screenY);
                    ctx.stroke();
                }
            }

            ctx.setLineDash([]);
            drawArrow(screenStartX, screenY, !isTop, color);

            ctx.lineWidth = 4;
            drawInfiniteLine(screenStartX, translateX + maxX * scaleX, screenY);

            ctx.fillStyle = color;
            ctx.beginPath();
            ctx.arc(screenStartX, screenY, 6, 0, Math.PI * 2);
            ctx.fill();

            ctx.font = 'bold 12px Arial';
            ctx.fillText(name, screenStartX + 15, screenY - 30);
            const textWidth = ctx.measureText(name).width;
            hitboxes.push({
                isTimeline: true, timelineName: name,
                left: screenStartX + 15 - 5, right: screenStartX + 15 + textWidth + 5,
                top: screenY - 30 - 15, bottom: screenY - 30 + 5
            });
        }
    });

    drawEvents();
}

function loadInitialData() {
    const cached = localStorage.getItem(LOCAL_STORAGE_KEY);
    if (cached) {
        try {
            projectData = JSON.parse(cached);
            // In loadInitialData():
            projectData.day_titles = projectData.day_titles || {};
            compactTimelineTracks(); // Automatically pulls distant branches into compact 110px spacing
            projectData.day_titles = projectData.day_titles || {};
            resizeCanvas();
            if (projectData.events && projectData.events.length > 0) {
                fitToScreen();
            }
            return;
        } catch (e) {
            console.error("Error loading cached timeline data:", e);
        }
    }

    fetch('../../scripts_and_data/River%20of%20Time.json')
        .then(res => res.json())
        .then(data => {
            projectData = data;
            projectData.day_titles = projectData.day_titles || {};
            persistProjectData();
            resizeCanvas();
            if (projectData.events && projectData.events.length > 0) {
                fitToScreen();
            }
        })
        .catch(err => console.error("Error loading master timeline data:", err));
}

loadInitialData();

// Re-render canvas automatically on theme toggle
window.addEventListener('killtime-theme-change', () => {
    if (projectData) render();
});
window.addEventListener('storage', (e) => {
    if (e.key === 'killtime_theme' && projectData) {
        render();
    } else if (e.key === LOCAL_STORAGE_KEY && e.newValue) {
        try {
            projectData = JSON.parse(e.newValue);
            render();
        } catch (err) { }
    }
});
