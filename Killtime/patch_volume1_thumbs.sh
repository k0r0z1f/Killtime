#!/usr/bin/env bash
set -euo pipefail

echo "==================================================================="
echo " [KILLTIME] OPTIMISATION DES RESSOURCES TRACKBOARDS - VOLUME I"
echo "==================================================================="

# 1. Localisation du dossier d'images et de thumbs
IMG_DIR=""
if [ -d "./Killtime/img" ]; then
    IMG_DIR="./Killtime/img"
elif [ -d "./img" ]; then
    IMG_DIR="./img"
else
    IMG_DIR=$(find . -maxdepth 3 -type d -name "img" | head -n 1)
fi

THUMBS_DIR="$IMG_DIR/thumbs"
mkdir -p "$THUMBS_DIR"

CMD_CONVERT=""
if command -v magick &> /dev/null; then
    CMD_CONVERT="magick"
elif command -v convert &> /dev/null; then
    CMD_CONVERT="convert"
else
    echo "[*] Installation de imagemagick requise..."
    sudo apt-get update -qq && sudo apt-get install -y imagemagick
    CMD_CONVERT="convert"
fi

# 2. Synchronisation de toutes les images PNG/JPG vers thumbs/ (préservation transparence PNG)
echo "[*] Synchronisation des vignettes dans $THUMBS_DIR..."
find "$IMG_DIR" -maxdepth 1 -type f \( -iname "*.jpg" -o -iname "*.jpeg" -o -iname "*.png" -o -iname "*.webp" \) -print0 | while IFS= read -r -d '' file; do
    FNAME=$(basename "$file")
    TARGET="$THUMBS_DIR/$FNAME"
    if [ ! -f "$TARGET" ] || [ "$file" -nt "$TARGET" ]; then
        $CMD_CONVERT "$file" -auto-orient -thumbnail 380x\> -quality 85 "$TARGET"
        echo "  -> Miniature générée : $FNAME"
    fi
done

# 3. Patching de Volume I et des autres manuscrits HTML
echo "[*] Injection du routage 'thumbs/' et du moteur Lightbox dans les manuscrits..."

python3 - << 'PYEOF'
import os
import re

# Ciblage de tous les fichiers HTML de manuscrits
target_files = []
for root, dirs, files in os.walk("."):
    for f in files:
        if f.endswith(".html") and ("Volume" in f or "Killtime" in f):
            target_files.append(os.path.join(root, f))

lightbox_code = r"""
<!-- ================================================================= -->
<!-- KILLTIME MANUSCRIPT LIGHTBOX ENGINE (ACTOR & LOCATION HIGH-RES) -->
<!-- ================================================================= -->
<div id="killtime-lightbox" class="killtime-lightbox" aria-hidden="true">
    <div class="lightbox-overlay" onclick="closeKilltimeLightbox()"></div>
    <div class="lightbox-container">
        <button class="lightbox-close" onclick="closeKilltimeLightbox()" aria-label="Fermer">✕</button>
        <div class="lightbox-loader" id="lightbox-loader">
            <div class="lightbox-spinner"></div>
            <span class="lightbox-loader-text">Chargement de la matrice haute résolution...</span>
        </div>
        <img id="lightbox-img" class="lightbox-img" src="" alt="Full view" style="display: none;">
        <div id="lightbox-caption" class="lightbox-caption"></div>
    </div>
</div>

<style>
.actor-icon, .location-icon {
    cursor: pointer !important;
}
.killtime-lightbox {
    display: none;
    position: fixed;
    top: 0;
    left: 0;
    width: 100vw;
    height: 100vh;
    z-index: 10000;
    align-items: center;
    justify-content: center;
}
.killtime-lightbox.active {
    display: flex;
}
.lightbox-overlay {
    position: absolute;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    background: rgba(10, 14, 23, 0.94);
    backdrop-filter: blur(8px);
}
.lightbox-container {
    position: relative;
    z-index: 10001;
    max-width: 90vw;
    max-height: 90vh;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
}
.lightbox-close {
    position: absolute;
    top: -45px;
    right: 0;
    background: transparent;
    border: 1px solid rgba(255, 255, 255, 0.2);
    color: #fff;
    font-size: 20px;
    width: 36px;
    height: 36px;
    border-radius: 50%;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: all 0.2s ease;
}
.lightbox-close:hover {
    background: rgba(255, 255, 255, 0.15);
    border-color: #58a6ff;
    color: #58a6ff;
    transform: scale(1.1);
}
.lightbox-img {
    max-width: 90vw;
    max-height: 82vh;
    object-fit: contain;
    border-radius: 6px;
    box-shadow: 0 10px 40px rgba(0, 0, 0, 0.8), 0 0 1px 1px rgba(88, 166, 255, 0.3);
    animation: fadeIn 0.25s ease-out;
}
.lightbox-caption {
    margin-top: 12px;
    color: #c9d1d9;
    font-family: 'Segoe UI', Tahoma, sans-serif;
    font-size: 1.05em;
    font-weight: bold;
    text-align: center;
    letter-spacing: 0.5px;
}
.lightbox-loader {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 15px;
}
.lightbox-spinner {
    width: 44px;
    height: 44px;
    border: 3px solid rgba(88, 166, 255, 0.2);
    border-top: 3px solid #58a6ff;
    border-radius: 50%;
    animation: spin 0.8s linear infinite;
}
.lightbox-loader-text {
    color: #58a6ff;
    font-family: 'Courier New', monospace;
    font-size: 0.9em;
    letter-spacing: 1px;
}
@keyframes spin {
    0% { transform: rotate(0deg); }
    100% { transform: rotate(360deg); }
}
@keyframes fadeIn {
    from { opacity: 0; transform: scale(0.97); }
    to { opacity: 1; transform: scale(1); }
}
</style>

<script>
function closeKilltimeLightbox() {
    const box = document.getElementById('killtime-lightbox');
    const img = document.getElementById('lightbox-img');
    if (box) {
        box.classList.remove('active');
        box.setAttribute('aria-hidden', 'true');
        document.body.style.overflow = '';
        if (img) img.src = '';
    }
}

document.addEventListener('DOMContentLoaded', function() {
    const trackboardIcons = document.querySelectorAll('.actor-icon, .location-icon');
    const box = document.getElementById('killtime-lightbox');
    const boxImg = document.getElementById('lightbox-img');
    const loader = document.getElementById('lightbox-loader');
    const caption = document.getElementById('lightbox-caption');

    trackboardIcons.forEach(icon => {
        icon.addEventListener('click', function(e) {
            const img = this.querySelector('img');
            if (!img) return;

            let thumbSrc = img.getAttribute('src');
            if (!thumbSrc || thumbSrc.includes('ui-avatars.com')) return;

            e.preventDefault();

            // Reconstitution de l'URL haute définition (retrait de 'thumbs/')
            const fullResUrl = thumbSrc.replace('/thumbs/', '/');

            box.classList.add('active');
            box.setAttribute('aria-hidden', 'false');
            document.body.style.overflow = 'hidden';

            loader.style.display = 'flex';
            boxImg.style.display = 'none';
            boxImg.src = '';

            const name = this.getAttribute('data-name') || img.getAttribute('alt') || '';
            caption.textContent = name;

            // Chargement à la demande de l'image haute définition
            const highRes = new Image();
            highRes.src = fullResUrl;
            highRes.onload = function() {
                boxImg.src = fullResUrl;
                loader.style.display = 'none';
                boxImg.style.display = 'block';
            };
            highRes.onerror = function() {
                // Fallback direct sur la source vignette si la HD échoue
                boxImg.src = thumbSrc;
                loader.style.display = 'none';
                boxImg.style.display = 'block';
            };
        });
    });

    document.addEventListener('keydown', function(e) {
        if (e.key === 'Escape') closeKilltimeLightbox();
    });
});
</script>
"""

# Regex ciblant src="../img/..." ou src="./Killtime/img/..." sans doubler 'thumbs/'
img_pattern = re.compile(r'src="((?:\.\./img/|\./Killtime/img/|Killtime/img/|/killtime/killtime/img/))(?!(?:thumbs/))([^"]+)"')

for file_path in target_files:
    with open(file_path, "r", encoding="utf-8") as f:
        content = f.read()

    # Remplacement des images pour pointer vers thumbs/ avec lazy-loading
    def replace_src(match):
        prefix = match.group(1)
        filename = match.group(2)
        return f'src="{prefix}thumbs/{filename}" loading="lazy"'

    new_content = img_pattern.sub(replace_src, content)

    # Injection du Lightbox si pas encore présent dans le fichier
    if "killtime-lightbox" not in new_content and ("actor-icon" in new_content or "location-icon" in new_content):
        new_content = new_content.replace("</body>", lightbox_code + "\n</body>")

    if new_content != content:
        with open(file_path, "w", encoding="utf-8") as f:
            f.write(new_content)
        print(f"[+] Patché avec succès : {file_path}")

PYEOF

echo "==================================================================="
echo " [SUCCÈS] VOLUME I RÉALIGNÉ SUR THUMBS/ ET LIGHTBOX ACTIF"
echo "==================================================================="
