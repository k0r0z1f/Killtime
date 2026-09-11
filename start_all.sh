#!/bin/bash
# ============================================
# start_all.sh — Lance tous les services Killtime
#   1. cycleServer.sh  (serveur Ruby HTTP, port 8000)
#   2. node users/server.js
#   3. ngrok
# Ctrl+C pour tout arrêter proprement.
# ============================================

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

NGROK_URL="unsaddle-computer-profile.ngrok-free.dev"
NGROK_PORT=3000

# Couleurs
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
NC='\033[0m'

PGIDS=()

cleanup() {
    echo ""
    echo -e "${RED}⏹  Arrêt de tous les services...${NC}"
    # Tuer chaque groupe de processus (inclut tous les enfants/petits-enfants)
    for pgid in "${PGIDS[@]}"; do
        kill -- -"$pgid" 2>/dev/null
    done
    sleep 0.5
    for pgid in "${PGIDS[@]}"; do
        kill -9 -- -"$pgid" 2>/dev/null
    done
    # Filet de sécurité : libérer les ports et tuer ngrok
    fuser -k 8000/tcp 2>/dev/null
    fuser -k 3000/tcp 2>/dev/null
    pkill -f ngrok 2>/dev/null
    echo -e "${RED}✔  Tous les services sont arrêtés.${NC}"
    exit 0
}

trap cleanup SIGINT SIGTERM

echo -e "${CYAN}╔══════════════════════════════════════════╗${NC}"
echo -e "${CYAN}║       🚀 Killtime — Démarrage           ║${NC}"
echo -e "${CYAN}╚══════════════════════════════════════════╝${NC}"
echo ""

# --- 1. cycleServer.sh (serveur Ruby HTTP) ---
echo -e "${GREEN}▶ [1/3] cycleServer.sh${NC}"
setsid bash "$SCRIPT_DIR/cycleServer.sh" 2>&1 | sed -u "s/^/[cycleServer] /" &
PGIDS+=($!)

# --- 2. Node users/server.js ---
echo -e "${GREEN}▶ [2/3] node users/server.js${NC}"
setsid node "$SCRIPT_DIR/users/server.js" 2>&1 | sed -u "s/^/[node-users]  /" &
PGIDS+=($!)

# --- 3. ngrok ---
echo -e "${GREEN}▶ [3/3] ngrok${NC}"
setsid ngrok http --url="$NGROK_URL" "$NGROK_PORT" 2>&1 | sed -u "s/^/[ngrok]       /" &
PGIDS+=($!)

echo ""
echo -e "${YELLOW}✔  Tous les services sont lancés.${NC}"
echo -e "${YELLOW}   Ctrl+C pour tout arrêter.${NC}"
echo ""

# Attendre que tous les processus tournent
wait
