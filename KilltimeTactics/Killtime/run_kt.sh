#!/usr/bin/env bash
# ==============================================================================
# run_kt.sh — Smart Multi-Screen Launcher for Killtime Tactics
#
# Detects which monitor the active terminal / window / mouse is currently on,
# and automatically starts KT.x86_64 on that display using Unity's -monitor flag.
# ==============================================================================

# Resolve symlink to real directory if invoked through a symlink
SOURCE="${BASH_SOURCE[0]}"
while [ -h "$SOURCE" ]; do
    DIR="$(cd -P "$(dirname "$SOURCE")" && pwd)"
    SOURCE="$(readlink "$SOURCE")"
    [[ $SOURCE != /* ]] && SOURCE="$DIR/$SOURCE"
done
SCRIPT_DIR="$(cd -P "$(dirname "$SOURCE")" && pwd)"
KT_BIN="$SCRIPT_DIR/KT.x86_64"

if [ ! -f "$KT_BIN" ]; then
    echo "❌ Erreur: $KT_BIN introuvable."
    exit 1
fi

chmod +x "$KT_BIN"

# Crucial for Unity Standalone Player: must execute inside the game directory
cd "$SCRIPT_DIR" || exit 1

# Check if user already passed -monitor explicitly
HAS_MONITOR_ARG=false
for arg in "$@"; do
    if [[ "$arg" == "-monitor" ]]; then
        HAS_MONITOR_ARG=true
        break
    fi
done

AUTO_MONITOR=1

if [ "$HAS_MONITOR_ARG" = false ]; then
    # Use python3 + xrandr / libX11 to detect active monitor index (1-based for Unity)
    DETECTED_MONITOR=$(python3 - << 'EOF' 2>/dev/null
import subprocess, os, re, ctypes

def get_window_or_mouse():
    # 1. Check window coordinates from WINDOWID or _NET_ACTIVE_WINDOW
    win_id = os.environ.get('WINDOWID')
    if not win_id:
        try:
            out = subprocess.check_output(['xprop', '-root', '_NET_ACTIVE_WINDOW'], stderr=subprocess.DEVNULL).decode()
            m = re.search(r'0x[0-9a-fA-F]+', out)
            if m: win_id = m.group(0)
        except Exception:
            pass
    if win_id:
        try:
            out = subprocess.check_output(['xwininfo', '-id', win_id], stderr=subprocess.DEVNULL).decode()
            x = int(re.search(r'Absolute upper-left X:\s+(-?\d+)', out).group(1))
            y = int(re.search(r'Absolute upper-left Y:\s+(-?\d+)', out).group(1))
            w = int(re.search(r'Width:\s+(\d+)', out).group(1))
            h = int(re.search(r'Height:\s+(\d+)', out).group(1))
            if w > 50 and h > 50:
                return (x + w // 2, y + h // 2)
        except Exception:
            pass

    # 2. Check mouse pointer position via libX11
    try:
        x11 = ctypes.cdll.LoadLibrary('libX11.so.6')
        display = x11.XOpenDisplay(None)
        if display:
            root = x11.XDefaultRootWindow(display)
            root_ret, child_ret = ctypes.c_ulong(), ctypes.c_ulong()
            rx, ry, wx, wy = ctypes.c_int(), ctypes.c_int(), ctypes.c_int(), ctypes.c_int()
            mask = ctypes.c_uint()
            res = x11.XQueryPointer(display, root, ctypes.byref(root_ret), ctypes.byref(child_ret),
                                   ctypes.byref(rx), ctypes.byref(ry),
                                   ctypes.byref(wx), ctypes.byref(wy),
                                   ctypes.byref(mask))
            x11.XCloseDisplay(display)
            if res:
                return (rx.value, ry.value)
    except Exception:
        pass
    return None

def detect_unity_monitor():
    try:
        out = subprocess.check_output(['xrandr', '--listmonitors'], stderr=subprocess.DEVNULL).decode().splitlines()[1:]
        pattern = re.compile(r'^\s*(\d+):\s*(\+\*|\+)?(\S+)\s+(\d+)/\d+x(\d+)/\d+\+(\d+)\+(\d+)\s+(\S+)')
        monitors = []
        for line in out:
            m = pattern.match(line)
            if m:
                idx, flags, name, w, h, x, y, out_name = m.groups()
                monitors.append({
                    'unity_index': int(idx) + 1,
                    'name': out_name,
                    'is_primary': '*' in (flags or ''),
                    'x': int(x), 'y': int(y),
                    'w': int(w), 'h': int(h)
                })

        pt = get_window_or_mouse()
        if pt:
            px, py = pt
            for mon in monitors:
                if mon['x'] <= px < mon['x'] + mon['w'] and mon['y'] <= py < mon['y'] + mon['h']:
                    return mon['unity_index']
    except Exception:
        pass
    return 1

print(detect_unity_monitor())
EOF
)
    if [[ -n "$DETECTED_MONITOR" && "$DETECTED_MONITOR" =~ ^[0-9]+$ ]]; then
        AUTO_MONITOR="$DETECTED_MONITOR"
    fi
fi

# Execute game
if [ "$HAS_MONITOR_ARG" = true ]; then
    echo "🎮 Lancement de Killtime Tactics avec arguments utilisateur : $@"
    exec "$KT_BIN" "$@"
else
    echo "🎮 Lancement de Killtime Tactics sur l'écran #$AUTO_MONITOR (détecté)..."
    exec "$KT_BIN" -monitor "$AUTO_MONITOR" "$@"
fi
