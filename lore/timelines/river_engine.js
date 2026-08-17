const canvas = document.getElementById('riverCanvas');
const ctx = canvas.getContext('2d');
let projectData = null;
let scaleX = 1;
let translateX = 0;
let translateY = 250; 
const PIXELS_PER_YEAR = 100;

let isDragging = false;
let lastMouseX = 0;
let lastMouseY = 0;

let hoveredEventIdx = null;
const contextMenu = document.getElementById('contextMenu');
let contextMenuTargetIdx = null;

let hitboxes = [];
const branchColors = ["#f44336", "#2196f3", "#4caf50", "#9c27b0", "#ff9800", "#00bcd4"];

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
    isDragging = true;
    lastMouseX = e.clientX;
    lastMouseY = e.clientY;
    canvas.style.cursor = 'grabbing';
});

window.addEventListener('mouseup', () => {
    isDragging = false;
    canvas.style.cursor = 'grab';
});

canvas.addEventListener('mousemove', (e) => {
    if (isDragging) {
        const dx = e.clientX - lastMouseX;
        const dy = e.clientY - lastMouseY;
        translateX += dx;
        translateY += dy;
        lastMouseX = e.clientX;
        lastMouseY = e.clientY;
        render();
    } else {
        const rect = canvas.getBoundingClientRect();
        const mouseX = e.clientX - rect.left;
        const mouseY = e.clientY - rect.top;
        
        let found = null;
        for (let h of hitboxes) {
            if (mouseX >= h.left && mouseX <= h.right && mouseY >= h.top && mouseY <= h.bottom) {
                found = h.idx;
                break;
            }
        }
        if (found !== hoveredEventIdx) {
            hoveredEventIdx = found;
            render();
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
}, {passive: false});

canvas.addEventListener('dblclick', (e) => {
    if (hoveredEventIdx !== null) {
        const ev = projectData.events[hoveredEventIdx];
        const targetX = valToX(ev.float_val);
        scaleX = 2.5;
        translateX = (canvas.width / 2) - targetX * scaleX;
        render();
    }
});

canvas.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    if (hoveredEventIdx !== null) {
        contextMenuTargetIdx = hoveredEventIdx;
        const rect = canvas.getBoundingClientRect();
        contextMenu.style.left = (e.clientX - rect.left) + 'px';
        contextMenu.style.top = (e.clientY - rect.top) + 'px';
        contextMenu.style.display = 'block';
    }
});

document.addEventListener('click', (e) => {
    if (e.target.closest('#contextMenu')) return;
    contextMenu.style.display = 'none';
});

document.getElementById('ctxEdit').addEventListener('click', () => {
    if (contextMenuTargetIdx !== null) {
        openEventModal(contextMenuTargetIdx);
        contextMenu.style.display = 'none';
    }
});

document.getElementById('ctxRemove').addEventListener('click', () => {
    if (contextMenuTargetIdx !== null) {
        projectData.events.splice(contextMenuTargetIdx, 1);
        contextMenu.style.display = 'none';
        render();
    }
});

// --- UI BUTTONS ---
document.getElementById('btnAddEvent').addEventListener('click', () => openEventModal(null));
document.getElementById('btnAddBranch').addEventListener('click', () => openBranchModal());
document.getElementById('btnGotoDate').addEventListener('click', () => openGotoModal());
document.getElementById('btnFitAll').addEventListener('click', fitToScreen);
document.getElementById('btnSave').addEventListener('click', saveProject);

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

function updateLineDropdowns() {
    const sel = document.getElementById('eventLineSelect');
    const pSel = document.getElementById('branchParentSelect');
    sel.innerHTML = ''; pSel.innerHTML = '';
    Object.keys(projectData.timelines).forEach(k => {
        sel.add(new Option(k, k));
        pSel.add(new Option(k, k));
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
        document.getElementById('eventLineSelect').disabled = (ev.type === 'trigger');
        
        const d = floatToDate(ev.float_val);
        document.getElementById('eventY').value = d.year;
        document.getElementById('eventM').value = d.month;
        document.getElementById('eventD').value = d.day;
        
        document.getElementById('eventName').value = ev.name;
        document.getElementById('eventChapter').value = ev.chapter_part || '';
        
        document.getElementById('btnSaveEvent').onclick = () => saveEvent(idx);
        
        if (ev.type === 'trigger') {
            document.getElementById('branchArrivalGroup').style.display = 'block';
            const arr = floatToDate(projectData.timelines[ev.target_branch].start_val);
            document.getElementById('eventArrivalY').value = arr.year;
            document.getElementById('eventArrivalM').value = arr.month;
            document.getElementById('eventArrivalD').value = arr.day;
        } else {
            document.getElementById('branchArrivalGroup').style.display = 'none';
        }
    } else {
        document.getElementById('eventModalTitle').innerText = 'Add Fixed Event (|)';
        document.getElementById('eventLineSelect').disabled = false;
        document.getElementById('branchArrivalGroup').style.display = 'none';
        document.getElementById('eventName').value = '';
        document.getElementById('eventChapter').value = '';
        document.getElementById('btnSaveEvent').onclick = () => saveEvent(null);
    }
}

function saveEvent(idx) {
    const line_name = document.getElementById('eventLineSelect').value;
    const y = parseInt(document.getElementById('eventY').value);
    const m = parseInt(document.getElementById('eventM').value);
    const d = parseInt(document.getElementById('eventD').value);
    const float_val = dateToFloat(y, m, d);
    const date_str = `${String(y).padStart(4, '0')}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
    const name = document.getElementById('eventName').value;
    const chap = document.getElementById('eventChapter').value.trim();
    
    if (idx !== null) {
        const ev = projectData.events[idx];
        ev.line_name = line_name;
        ev.float_val = float_val;
        ev.date_str = date_str;
        ev.name = name;
        if (chap) ev.chapter_part = chap; else delete ev.chapter_part;
        
        if (ev.type === 'trigger') {
            const ay = parseInt(document.getElementById('eventArrivalY').value);
            const am = parseInt(document.getElementById('eventArrivalM').value);
            const ad = parseInt(document.getElementById('eventArrivalD').value);
            projectData.timelines[ev.target_branch].start_val = dateToFloat(ay, am, ad);
        }
    } else {
        const ev = { float_val, date_str, name, line_name, type: 'fixed' };
        if (chap) ev.chapter_part = chap;
        projectData.events.push(ev);
    }
    
    closeModal();
    render();
}

function openBranchModal() {
    updateLineDropdowns();
    document.getElementById('branchTriggerName').value = '';
    document.getElementById('branchNewName').value = '';
    document.getElementById('modalOverlay').style.display = 'flex';
    document.getElementById('branchModal').style.display = 'block';
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
    
    const bCount = Object.keys(projectData.timelines).length;
    const yOff = (bCount % 2 !== 0) ? (bCount * 120) : -(bCount * 120);
    
    if (projectData.color_index === undefined) projectData.color_index = 0;
    const color = branchColors[projectData.color_index % branchColors.length];
    projectData.color_index++;
    
    projectData.timelines[bName] = { y: yOff, color, start_val: dateToFloat(ay, am, ad), parent: parentLine };
    projectData.events.push({
        float_val: dateToFloat(dy, dm, dd),
        date_str: `${String(dy).padStart(4, '0')}-${String(dm).padStart(2, '0')}-${String(dd).padStart(2, '0')}`,
        name: tName, line_name: parentLine, type: 'trigger', target_branch: bName
    });
    
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
}
document.getElementById('btnCancelEvent').onclick = closeModal;
document.getElementById('btnCancelBranch').onclick = closeModal;
document.getElementById('btnCancelGoto').onclick = closeModal;

function fitToScreen() {
    let vals = projectData.events.map(e => e.float_val);
    Object.values(projectData.timelines).forEach(t => {
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

function drawGrid() {
    const minXScreen = 0;
    const maxXScreen = canvas.width;
    
    const minVal = (minXScreen - translateX) / (PIXELS_PER_YEAR * scaleX);
    const maxVal = (maxXScreen - translateX) / (PIXELS_PER_YEAR * scaleX);
    const valRange = maxVal - minVal;

    const steps = [
        100000, 10000, 5000, 1000, 500, 100, 50, 10, 5, 1, 
        0.5, 0.1, 10/300.0, 5/300.0, 1/300.0    
    ];

    let step = steps[steps.length - 1];
    for (let s of steps) {
        if (valRange / s >= 4) {
            step = s;
            break;
        }
    }

    ctx.fillStyle = '#0f1216';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    ctx.strokeStyle = '#282d32'; 
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

    ctx.fillStyle = 'rgba(15, 18, 22, 1)';
    ctx.fillRect(0, 0, canvas.width, 25);

    ctx.fillStyle = '#b4b4b4';
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
    const padX = 2;
    const padY = 2;
    
    hitboxes = [];

    const events = projectData.events;
    const timelines = projectData.timelines;

    events.forEach((e, idx) => {
        const line = timelines[e.line_name];
        if (!line) return;

        const screenX = translateX + valToX(e.float_val) * scaleX;
        const lineY = translateY + line.y;
        
        const markerOffset = e.type === 'trigger' ? 8 : 15;
        const isHovered = (idx === hoveredEventIdx);
        const hlColor = '#00ffff';
        
        ctx.lineWidth = 3;
        if (e.type === 'trigger') {
            ctx.strokeStyle = isHovered ? hlColor : line.color;
            ctx.fillStyle = isHovered ? hlColor : '#0f1216';
            ctx.beginPath();
            ctx.arc(screenX, lineY, markerOffset, 0, Math.PI * 2);
            ctx.fill();
            ctx.stroke();
        } else {
            ctx.strokeStyle = isHovered ? hlColor : '#ffffff';
            ctx.beginPath();
            ctx.moveTo(screenX, lineY - markerOffset);
            ctx.lineTo(screenX, lineY + markerOffset);
            ctx.stroke();
        }

        const chap = e.chapter_part ? `[${e.chapter_part}] ` : '';
        const name = `${chap}${e.name}`;
        const date = `[${e.date_str}]`;

        ctx.font = 'bold 10px monospace';
        const nameWidth = ctx.measureText(name).width;
        const dateWidth = ctx.measureText(date).width;
        const w = Math.max(nameWidth, dateWidth) + 4;
        const h = 24; 

        const baseX = screenX - (w / 2);
        const baseY = lineY + markerOffset + 10;
        let currentY = baseY;

        let overlap = true;
        let iterations = 0;
        while (overlap && iterations < 50) {
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
        hitboxes.push({ idx, left: baseX, right: baseX + w, top: currentY, bottom: currentY + h });

        if (currentY > baseY + 5) {
            ctx.strokeStyle = isHovered ? hlColor : 'rgba(100, 100, 100, 0.6)';
            ctx.lineWidth = isHovered ? 2 : 1;
            ctx.setLineDash([2, 4]);
            ctx.beginPath();
            ctx.moveTo(screenX, lineY + markerOffset);
            ctx.lineTo(screenX, currentY);
            ctx.stroke();
            ctx.setLineDash([]);
        }

        ctx.fillStyle = isHovered ? hlColor : (e.type === 'trigger' ? '#ff6464' : '#c8c8c8');
        ctx.fillText(name, baseX + 2, currentY + 10);
        ctx.fillText(date, baseX + 2, currentY + 22);
    });
}

function render() {
    drawGrid();

    if (!projectData) return;

    const timelines = projectData.timelines;
    const events = projectData.events;

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

            if (dx < 1.0) {
                ctx.beginPath();
                ctx.moveTo(screenTriggerX, parentScreenY);
                ctx.bezierCurveTo(screenTriggerX - 40, screenControlY, screenStartX + 40, screenControlY, screenStartX, screenY);
                ctx.stroke();
            } else {
                const dirX = screenStartX >= screenTriggerX ? 1 : -1;
                const cruise = Math.min(75.0, dx / 2.0);
                const upEndX = screenTriggerX + (cruise * dirX);
                const downStartX = screenStartX - (cruise * dirX);
                const kappa = 0.55228;

                ctx.beginPath();
                ctx.moveTo(screenTriggerX, parentScreenY);
                const cp1Y = parentScreenY + (screenControlY - parentScreenY) * kappa;
                const cp2X = upEndX - (cruise * dirX) * kappa;
                ctx.bezierCurveTo(screenTriggerX, cp1Y, cp2X, screenControlY, upEndX, screenControlY);
                ctx.stroke();
                
                ctx.setLineDash([2, 5]);
                drawInfiniteLine(upEndX, downStartX, screenControlY);
                ctx.setLineDash([5, 10]);

                ctx.beginPath();
                const cp3X = downStartX + (cruise * dirX) * kappa;
                const cp4Y = screenY + (screenControlY - screenY) * kappa;
                ctx.bezierCurveTo(cp3X, screenControlY, screenStartX, cp4Y, screenStartX, screenY);
                ctx.stroke();
            }

            ctx.setLineDash([]);
            
            const depYDir = controlY > parentY ? 1 : -1;
            drawArrow(screenTriggerX, parentScreenY + (40 * depYDir), depYDir === 1, color);

            const arrYDir = yPos > controlY ? 1 : -1;
            drawArrow(screenStartX, screenY - (40 * arrYDir), arrYDir === 1, color);

            ctx.lineWidth = 4;
            drawInfiniteLine(screenStartX, translateX + maxX * scaleX, screenY);

            ctx.fillStyle = color;
            ctx.beginPath();
            ctx.arc(screenStartX, screenY, 6, 0, Math.PI * 2);
            ctx.fill();

            ctx.font = 'bold 12px Arial';
            ctx.fillText(name, screenStartX + 15, screenY - 30);
        }
    });

    drawEvents();
}

fetch('../../scripts_and_data/River%20of%20Time.json')
    .then(res => res.json())
    .then(data => {
        projectData = data;
        resizeCanvas();
        if (projectData.events && projectData.events.length > 0) {
            fitToScreen();
        }
    })
    .catch(err => console.error("Error loading timeline data:", err));
