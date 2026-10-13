using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.UI
{
    /// <summary>
    /// Cycle de vie des docks (repli / restauration / anim). Partial de <see cref="FloatingWindowChrome"/>.
    /// Champs partagés (_docked, etc.) déclarés dans le noyau.
    /// </summary>
    public static partial class FloatingWindowChrome
    {
        /// <summary>
        /// Vrai tant que la fenêtre n'a pas retrouvé sa taille normale complète.
        /// </summary>
        public static bool IsDocked(int windowId)
            => _docked.ContainsKey(windowId);

        /// <summary>
        /// Restaure un dock déjà mémorisé. Le rect fourni reste le rect normal de
        /// la fenêtre ; la vignette dockée est recalculée pour la résolution courante.
        /// </summary>
        public static void RestoreDockedWindow(int windowId, Rect fullRect)
        {
            if (fullRect.width <= 0f || fullRect.height <= 0f) return;

            _docked[windowId] = new DockState
            {
                fullRect = fullRect,
                animFrom = fullRect,
                // État stable immédiatement : pas d'animation de repli au lancement.
                animStart = Time.realtimeSinceStartup - DockAnimDuration,
                restoring = false
            };
            RecalculateDockTargets();
        }

        /// <summary>Supprime un dock sans modifier le rect normal mémorisé.</summary>
        public static void ClearDockedWindow(int windowId)
        {
            if (_docked.Remove(windowId)) RecalculateDockTargets();
        }

        private static float Ease01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Rect LerpRect(Rect a, Rect b, float t)
        {
            return new Rect(
                Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t),
                Mathf.Lerp(a.width, b.width, t), Mathf.Lerp(a.height, b.height, t));
        }

        /// <summary>Opacité courante : 1 = normal, DockedAlpha = replié (animé entre les deux).</summary>
        public static float DockAlpha(int windowId)
        {
            return Mathf.Lerp(1f, DockedAlpha, Mathf.Clamp01(DockGhostT(windowId)));
        }

        /// <summary>Progression fantôme 0..1 (0 = normal, 1 = replié stable ; animée aux transitions).</summary>
        public static float DockGhostT(int windowId)
        {
            if (!_docked.TryGetValue(windowId, out var d)) return 0f;
            float t = Ease01((Time.realtimeSinceStartup - d.animStart) / DockAnimDuration);
            return d.restoring ? 1f - t : t;
        }

        /// <summary>
        /// Échelle uniforme issue de la largeur : un dock représente une réduction
        /// proportionnelle de la fenêtre normale, sans déformation ni recadrage.
        /// </summary>
        public static float DockContentScale(int windowId, Rect dockedRect)
        {
            if (!_docked.TryGetValue(windowId, out var d)) return 1f;

            float sourceWidth = Mathf.Max(1f, d.fullRect.width);
            float targetWidth = Mathf.Max(1f, dockedRect.width);
            return Mathf.Clamp01(targetWidth / sourceWidth);
        }

        /// <summary>Rect logique complet à dessiner pour un aperçu docké mis à l'échelle.</summary>
        public static bool TryGetDockSourceRect(int windowId, out Rect sourceRect)
        {
            sourceRect = default;
            if (!_docked.TryGetValue(windowId, out var d)) return false;
            if (d.fullRect.width <= 0f || d.fullRect.height <= 0f) return false;
            sourceRect = d.fullRect;
            return true;
        }

        /// <summary>
        /// Replie les fenêtres ouvertes vers le bord d'écran le plus proche (gauche ou droite)
        /// en conservant au plus près leur hauteur d'origine et en interdisant tout chevauchement.
        /// </summary>
        public static void OnMapClicked()
        {
            PurgeStaleDock();
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < _zOrder.Count; i++)
            {
                int id = _zOrder[i];
                if (id == NoDockWindowId) continue;
                if (!ProviderVisible(id)) continue;
                // Un aperçu docké est déjà à son état cible : le relancer à chaque
                // clic carte le ferait repartir de son rect courant et scintiller.
                // Même pendant son animation de restauration, on le laisse finir
                // naturellement plutôt que de l'inverser au clic suivant.
                if (_docked.ContainsKey(id)) continue;
                if (!ProviderRect(id, out Rect cur)) continue;
                if (cur.height <= CollapsedHeight + 1f) continue;

                _docked[id] = new DockState
                {
                    fullRect = cur,
                    animFrom = cur,
                    animStart = now,
                    restoring = false
                };
            }

            RecalculateDockTargets();
        }

        /// <summary>
        /// Restaure toutes les fenêtres repliées : le retour est animé vers leur rect d'origine.
        /// </summary>
        public static bool RestoreAll()
        {
            PurgeStaleDock();
            if (_docked.Count == 0) return false;
            float now = Time.realtimeSinceStartup;
            foreach (var kvp in _docked)
            {
                if (ProviderRect(kvp.Key, out Rect cur)) kvp.Value.animFrom = cur;
                kvp.Value.restoring = true;
                kvp.Value.animStart = now;
            }
            return true;
        }

        /// <summary>Restaure une seule fenêtre dockée, sans toucher aux autres.</summary>
        public static bool RestoreDockedWindow(int windowId)
        {
            if (!_docked.TryGetValue(windowId, out DockState state) || state.restoring) return false;
            if (ProviderRect(windowId, out Rect current)) state.animFrom = current;
            state.restoring = true;
            state.animStart = Time.realtimeSinceStartup;
            return true;
        }

        private static void PurgeStaleDock()
        {
            if (_docked.Count == 0) return;
            List<int> stale = null;
            foreach (var kvp in _docked)
            {
                if (!ProviderVisible(kvp.Key)) (stale ??= new List<int>()).Add(kvp.Key);
            }
            if (stale == null) return;
            foreach (int id in stale) _docked.Remove(id);
        }

        /// <summary>
        /// À appeler dans OnGUI avant GUI.Window. Impose le rect animé calculé sans chevauchement.
        /// </summary>
        public static bool ApplyDockedRect(ref Rect rect, int windowId)
        {
            if (!_docked.TryGetValue(windowId, out DockState d)) return false;
            if (!ProviderVisible(windowId))
            {
                _docked.Remove(windowId);
                RecalculateDockTargets();
                return false;
            }

            float t = Ease01((Time.realtimeSinceStartup - d.animStart) / DockAnimDuration);
            if (d.restoring)
            {
                rect = LerpRect(d.animFrom, d.fullRect, t);
                if (t >= 1f)
                {
                    rect = d.fullRect;
                    _docked.Remove(windowId);
                    RecalculateDockTargets();
                    FocusWindow(windowId);
                }
                return false;
            }

            if (d.targetDockRect.width <= 0f)
            {
                RecalculateDockTargets();
            }

            rect = LerpRect(d.animFrom, d.targetDockRect, t);
            return true;
        }
    }
}
