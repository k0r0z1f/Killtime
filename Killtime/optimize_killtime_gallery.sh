#!/usr/bin/env bash
set -euo pipefail

echo "==================================================================="
echo " [KILLTIME] INITIALISATION DU PROTOCOLE DE MINIATURISATION"
echo "==================================================================="

# 1. Résolution stricte du répertoire des images
IMG_DIR=""
if [ -d "./Killtime/img" ]; then
    IMG_DIR="./Killtime/img"
elif [ -d "./img" ]; then
    IMG_DIR="./img"
else
    IMG_DIR=$(find . -maxdepth 3 -type d -name "img" | head -n 1)
fi

if [ -z "$IMG_DIR" ] || [ ! -d "$IMG_DIR" ]; then
    echo "[-] Erreur critique : Répertoire 'img' introuvable depuis $(pwd)"
    exit 1
fi

THUMBS_DIR="$IMG_DIR/thumbs"
mkdir -p "$THUMBS_DIR"

echo "[+] Répertoire source des images : $IMG_DIR"
echo "[+] Répertoire cible des miniatures : $THUMBS_DIR"

# 2. Vérification d'ImageMagick (convert ou magick)
CMD_CONVERT=""
if command -v magick &> /dev/null; then
    CMD_CONVERT="magick"
elif command -v convert &> /dev/null; then
    CMD_CONVERT="convert"
else
    echo "[*] ImageMagick non détecté. Installation automatique via apt..."
    sudo apt-get update -qq && sudo apt-get install -y imagemagick
    CMD_CONVERT="convert"
fi
echo "[+] Outil de conversion validé : $CMD_CONVERT"

# 3. Génération batch des miniatures dans /thumbs (Largeur max 380px, qualité 82%, sans EXIF)
echo "[*] Compression et génération des vignettes..."
COUNT=0
find "$IMG_DIR" -maxdepth 1 -type f \( -iname "*.jpg" -o -iname "*.jpeg" -o -iname "*.png" -o -iname "*.webp" \) -print0 | while IFS= read -r -d '' img; do
    FNAME=$(basename "$img")
    TARGET="$THUMBS_DIR/$FNAME"
    
    # Éviter de recalculer si déjà généré et à jour
    if [ ! -f "$TARGET" ] || [ "$img" -nt "$TARGET" ]; then
        $CMD_CONVERT "$img" -auto-orient -thumbnail 380x\> -quality 82 -strip "$TARGET"
        echo "  -> Miniature créée : $FNAME"
    fi
    COUNT=$((COUNT + 1))
done

TOTAL_THUMBS=$(find "$THUMBS_DIR" -maxdepth 1 -type f | wc -l)
echo "[+] Traitement image terminé : $TOTAL_THUMBS miniatures prêtes dans $THUMBS_DIR"

# 4. Détection et modification de index.html
HTML_FILE="index.html"
if [ ! -f "$HTML_FILE" ]; then
    HTML_FILE=$(find . -maxdepth 2 -name "index.html" | head -n 1)
fi

if [ -z "$HTML_FILE" ] || [ ! -f "$HTML_FILE" ]; then
    echo "[-] Erreur critique : 'index.html' introuvable."
    exit 1
fi

echo "[*] Mise à jour des ancres et injection du moteur Lightbox dans $HTML_FILE..."

python3 - << PYEOF
import os
import re

html_file = "$HTML_FILE"

with open(html_file, "r", encoding="utf-8") as f:
    content = f.read()

# 1. Remplacer les images de catalogue pour pointer vers thumbs/ avec lazy-loading
def patch_thumb_src(match):
    prefix = match.group(1)   # ex: ./Killtime/img/ ou Killtime/img/
    filename = match.group(2) # ex: Candice.jpg
    if filename.startswith("thumbs/"):
        return match.group(0)
    return f'<img class="images-category-img" src="{prefix}thumbs/{filename}" loading="lazy"'

content = re.sub(r'<img\s+class="images-category-img"\s+src="([^"]*?/img/)([^"]+)"', patch_thumb_src, content)

# 2. Injection du Lightbox asynchrone (Affichage plein écran et téléchargement uniquement au clic)
lightbox_code = """
<!-- ================================================================= -->
<!-- KILLTIME ON-DEMAND FULLSCREEN LIGHTBOX (ASYNC HIGH-RES LOADER)   -->
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
    font-size: 0.95em;
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
    const galleryLinks = document.querySelectorAll('.images-category a');
    const box = document.getElementById('killtime-lightbox');
    const boxImg = document.getElementById('lightbox-img');
    const loader = document.getElementById('lightbox-loader');
    const caption = document.getElementById('lightbox-caption');

    galleryLinks.forEach(link => {
        link.addEventListener('click', function(e) {
            const targetUrl = this.getAttribute('href');
            if (!targetUrl || !targetUrl.match(/\\.(jpg|jpeg|png|webp|gif)$/i)) {
                return;
            }
            e.preventDefault();

            box.classList.add('active');
            box.setAttribute('aria-hidden', 'false');
            document.body.style.overflow = 'hidden';

            loader.style.display = 'flex';
            boxImg.style.display = 'none';
            boxImg.src = '';

            const thumb = this.querySelector('img');
            caption.textContent = thumb ? (thumb.alt || '') : '';

            // Chargement à la demande : la requête réseau HD part UNIQUEMENT ici
            const highRes = new Image();
            highRes.src = targetUrl;
            highRes.onload = function() {
                boxImg.src = targetUrl;
                loader.style.display = 'none';
                boxImg.style.display = 'block';
            };
            highRes.onerror = function() {
                loader.innerHTML = '<span style="color:#ff7b72; font-family:sans-serif;">Échec du chargement haute résolution.</span>';
            };
        });
    });

    document.addEventListener('keydown', function(e) {
        if (e.key === 'Escape') {
            closeKilltimeLightbox();
        }
    });
});
</script>
"""

if "killtime-lightbox" not in content:
    content = content.replace("</body>", lightbox_code + "\n</body>")

with open(html_file, "w", encoding="utf-8") as f:
    f.write(content)

print("[+] index.html mis à jour : liaisons miniatures 'thumbs/' et Lightbox on-demand actifs.")
PYEOF

echo "==================================================================="
echo " [SUCCÈS] DÉPLOIEMENT TERMINÉ"
echo "==================================================================="
