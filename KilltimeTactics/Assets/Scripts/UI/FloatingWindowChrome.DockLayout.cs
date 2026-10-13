using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.UI
{
    /// <summary>
    /// Layout des docks (colonnes, anti-chevauchement, bouton restaurer-tout).
    /// Partial de <see cref="FloatingWindowChrome"/>.
    /// </summary>
    public static partial class FloatingWindowChrome
    {
        private struct DockEntry
        {
            public int id;
            public DockState state;
        }

        private static void RecalculateDockTargets()
        {
            if (_docked.Count == 0) return;

            var leftList = new List<DockEntry>();
            var rightList = new List<DockEntry>();
            float screenMidX = Screen.width * 0.5f;

            foreach (var kvp in _docked)
            {
                if (!ProviderVisible(kvp.Key)) continue;
                var state = kvp.Value;
                float midX = state.fullRect.x + state.fullRect.width * 0.5f;
                if (midX < screenMidX)
                    leftList.Add(new DockEntry { id = kvp.Key, state = state });
                else
                    rightList.Add(new DockEntry { id = kvp.Key, state = state });
            }

            // Écran étroit : les deux colonnes se recouvriraient horizontalement
            // (une colonne standard fait 160px + 36px de gouttière) -> une seule pile.
            if (leftList.Count > 0 && rightList.Count > 0 && Screen.width > 0f
                && Screen.width < (DockColumnEdgeInset + 160f) * 2f)
            {
                leftList.AddRange(rightList);
                rightList.Clear();
            }

            LayoutDockColumn(leftList, isRight: false);
            LayoutDockColumn(rightList, isRight: true);
            // Colonne gauche et droite peuvent se recouvrir (écran étroit : les deux
            // premières vignettes atterrissent à la même hauteur et se chevauchent
            // côte à côte). Passe globale : aucune paire de vignettes ne se recouvre.
            ResolveAllDockOverlaps(leftList, rightList);
        }

        /// <summary>
        /// Passe globale anti-chevauchement sur TOUTES les vignettes (gauche + droite).
        /// Traite de haut en bas : chaque dock qui en recouvre un déjà placé descend
        /// sous lui. Couvre les collisions inter-colonnes (écran étroit) et tout résidu
        /// intra-colonne (ex : clamp écran après poussée anti-bloqueur).
        /// </summary>
        private static void ResolveAllDockOverlaps(List<DockEntry> leftList, List<DockEntry> rightList)
        {
            int total = leftList.Count + rightList.Count;
            if (total < 2) return;
            var all = new List<DockEntry>(total);
            all.AddRange(leftList);
            all.AddRange(rightList);
            all.Sort((a, b) => a.state.targetDockRect.y.CompareTo(b.state.targetDockRect.y));
            for (int i = 0; i < all.Count; i++)
            {
                Rect r = all[i].state.targetDockRect;
                if (r.width <= 0f || r.height <= 0f) continue;
                int guard = 0;
                bool moved;
                do
                {
                    moved = false;
                    for (int j = 0; j < i; j++)
                    {
                        Rect p = all[j].state.targetDockRect;
                        if (p.width <= 0f || p.height <= 0f) continue;
                        if (r.Overlaps(p))
                        {
                            r.y = p.yMax + DockMargin;
                            moved = true;
                        }
                    }
                } while (moved && guard++ < 32);
                float maxY = Mathf.Max(0f, Screen.height - 12f - r.height);
                if (r.y > maxY) r.y = maxY;
                all[i].state.targetDockRect = r;
            }
        }

        private static void LayoutDockColumn(List<DockEntry> list, bool isRight)
        {
            int count = list.Count;
            if (count == 0) return;

            float w = Mathf.Clamp(Screen.width * 0.13f, 160f, 210f);
            float colX = isRight
                ? Mathf.Max(DockMargin, Screen.width - DockColumnEdgeInset - w)
                : DockColumnEdgeInset;
            Rect colRect = new Rect(colX, 0f, w, Screen.height);

            float topSafeY = isRight ? 90f : 94f;
            float bottomSafeY = Screen.height - 60f;

            for (int i = 0; i < _exclusionZones.Count; i++)
            {
                var provider = _exclusionZones[i];
                if (provider == null) continue;
                Rect zone;
                try { zone = provider(); } catch { continue; }
                if (zone.width <= 0f || zone.height <= 0f) continue;

                if (zone.xMax > colRect.xMin && zone.xMin < colRect.xMax)
                {
                    float zoneCenterY = zone.y + zone.height * 0.5f;
                    if (zoneCenterY < Screen.height * 0.5f)
                    {
                        float neededTop = zone.yMax + DockMargin;
                        if (neededTop > topSafeY) topSafeY = neededTop;
                    }
                    else
                    {
                        float neededBottom = zone.yMin - DockMargin;
                        if (neededBottom < bottomSafeY) bottomSafeY = neededBottom;
                    }
                }
            }

            list.Sort((a, b) => a.state.fullRect.y.CompareTo(b.state.fullRect.y));

            // Une carte dockée garde le ratio de sa fenêtre d'origine. Si la pile ne
            // tient pas, c'est la largeur commune de la colonne qui diminue : contenu,
            // largeur et hauteur sont alors tous réduits dans les mêmes proportions.
            float availableHeight = Mathf.Max(44f, bottomSafeY - topSafeY);
            float totalContentAspect = 0f;
            for (int i = 0; i < count; i++)
            {
                Rect source = list[i].state.fullRect;
                totalContentAspect += Mathf.Max(0.05f,
                    Mathf.Max(1f, source.height - TitleHeight) / Mathf.Max(1f, source.width));
            }

            float widthToFitStack = (availableHeight - count * TitleHeight - (count - 1) * DockMargin)
                / Mathf.Max(0.05f, totalContentAspect);
            w = Mathf.Clamp(Mathf.Min(w, widthToFitStack), 48f, w);
            colX = isRight
                ? Mathf.Max(DockMargin, Screen.width - DockColumnEdgeInset - w)
                : DockColumnEdgeInset;

            float[] heights = new float[count];
            float[] ys = new float[count];
            for (int i = 0; i < count; i++)
            {
                Rect source = list[i].state.fullRect;
                float contentAspect = Mathf.Max(0.05f,
                    Mathf.Max(1f, source.height - TitleHeight) / Mathf.Max(1f, source.width));
                heights[i] = TitleHeight + contentAspect * w;
                ys[i] = i == 0 ? topSafeY : ys[i - 1] + heights[i - 1] + DockMargin;
            }

            // Les vignettes dockées sont fixes : une fenêtre non dockée qui passe
            // dessus (drag / resize / ouverture) ne les déplace jamais. Seul
            // l'empilement interne de la colonne (ordre haut->bas) est préservé
            // ici ; les collisions entre colonnes sont réglées par
            // ResolveAllDockOverlaps.
            for (int i = 0; i < count; i++)
            {
                Rect r = new Rect(colX, ys[i], w, heights[i]);
                int guard = 0;
                bool moved;
                do
                {
                    moved = false;
                    for (int j = 0; j < i; j++)
                    {
                        Rect prev = new Rect(colX, ys[j], w, heights[j]);
                        if (r.Overlaps(prev))
                        {
                            r.y = prev.yMax + DockMargin;
                            moved = true;
                        }
                    }
                } while (moved && guard++ < 24);
                // Reste à l'écran : au pire on remonte (colonne saturée, cas pathologique
                // où un chevauchement résiduel est préférable à une vignette inaccessible).
                float maxY = Mathf.Max(topSafeY, Screen.height - 12f - r.height);
                if (r.y > maxY) r.y = maxY;
                ys[i] = r.y;
            }

            for (int i = 0; i < count; i++)
            {
                list[i].state.targetDockRect = new Rect(colX, ys[i], w, heights[i]);
            }
        }

        private static Rect DockRestoreAllButtonRect(Rect dockRect)
        {
            bool isLeft = dockRect.center.x < Screen.width * 0.5f;
            float x = isLeft
                ? Mathf.Max(2f, dockRect.xMin - DockRestoreAllButtonGap - DockRestoreAllButtonWidth)
                : Mathf.Min(Screen.width - DockRestoreAllButtonWidth - 2f,
                    dockRect.xMax + DockRestoreAllButtonGap);
            float y = Mathf.Clamp(dockRect.center.y - DockRestoreAllButtonHeight * 0.5f, 4f,
                Mathf.Max(4f, Screen.height - DockRestoreAllButtonHeight - 4f));
            return new Rect(x, y, DockRestoreAllButtonWidth, DockRestoreAllButtonHeight);
        }

        /// <summary>
        /// Retrouve le dock survolé. La zone de sa commande latérale reste active
        /// afin que le bouton ne disparaisse pas pendant le déplacement de souris.
        /// </summary>
        private static bool TryGetHoveredDock(Vector2 mp, out int windowId, out Rect dockRect)
        {
            windowId = 0;
            dockRect = default;
            if (_docked.Count == 0) return false;
            foreach (var kvp in _docked)
            {
                if (kvp.Value.restoring) continue;
                if (!ProviderVisible(kvp.Key)) continue;
                if (!ProviderRect(kvp.Key, out Rect live)) continue;
                if (live.Contains(mp) || DockRestoreAllButtonRect(live).Contains(mp))
                {
                    windowId = kvp.Key;
                    dockRect = live;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gère les clics autour d'un aperçu docké avant le rendu des GUI.Window.
        /// Le bouton latéral est testé avant l'aperçu lui-même, ce qui garantit le
        /// même comportement aux deux bords de l'écran, indépendamment de l'ordre
        /// de rendu IMGUI des fenêtres.
        /// </summary>
        private static bool ConsumeDockedClick(Vector2 mp)
        {
            if (!TryGetHoveredDock(mp, out int hoveredId, out Rect dockRect)) return false;
            // Une fenêtre normale au premier plan qui recouvre le dock garde le
            // clic : on ne restaure jamais un aperçu caché sous une autre fenêtre.
            // (Une fenêtre derrière le dock, elle, ne bloque pas sa restauration.)
            if (IsCoveredByFrontNormalWindow(mp, hoveredId)) return false;

            if (DockRestoreAllButtonRect(dockRect).Contains(mp))
            {
                RestoreAll();
                return true;
            }

            if (dockRect.Contains(mp))
            {
                RestoreDockedWindow(hoveredId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Dessine le bouton latéral de restauration globale pour la fenêtre dockée
        /// actuellement survolée. Chaque fenêtre appelle cette méthode, mais seule
        /// celle qui est survolée effectue le rendu.
        /// </summary>
        public static void DrawDockRestoreAllButton(int windowId)
        {
            Event e = Event.current;
            if (e == null || !TryGetHoveredDock(e.mousePosition, out int hoveredId, out Rect dockRect)
                || hoveredId != windowId)
                return;
            // Bouton recouvert par une fenêtre normale au premier plan : la fenêtre
            // du dessus garde la main, on ne dessine pas de commande par-dessus elle.
            if (IsCoveredByFrontNormalWindow(e.mousePosition, hoveredId)) return;

            Rect buttonRect = DockRestoreAllButtonRect(dockRect);
            // L'action est traitée en pré-fenêtre par ConsumeDockedClick : cette
            // boîte conserve le visuel de bouton sans dépendre du hot-control IMGUI.
            GUI.Box(buttonRect, new GUIContent("↔", "Restaurer toutes les fenêtres dockées"), GUI.skin.button);
        }
    }
}
