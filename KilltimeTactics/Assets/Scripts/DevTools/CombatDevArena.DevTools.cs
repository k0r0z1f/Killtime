using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Chrono;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.CameraSystem;
using Killtime.WebGL;
using Killtime.UI;
using Killtime.Tactics.Arena;

namespace Killtime.Tactics
{
    // Chrono, utilitaires dev, musique, spawns heroiques (RD-152).
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
        public void RecordChronoSnapshot(string description)
        {
            _chronoService.RecordChronoSnapshot(description);
        }

        public void RewindLastSnapshot()
        {
            _chronoService.RewindLastSnapshot();
        }

        public void RewindToSnapshot(int index)
        {
            _chronoService.RewindToSnapshot(index);
        }

        public bool SwitchChronoBranch(TimelineId id)
        {
            return _chronoService.SwitchBranch(id);
        }

        public void RefillAPAll()
        {
            _chronoService.RefillAPAll();
        }

        public void HealAndCureAll()
        {
            _chronoService.HealAndCureAll();
        }

        public void KillCurrentTarget()
        {
            _chronoService.KillCurrentTarget();
        }

        public void RemoveUnit(TacticalUnit unit)
        {
            _chronoService.RemoveUnit(unit);
        }

        public void ResetArena()
        {
            CombatHUD.ResetTelemetry();

            // 0. Nettoyer les armes/objets au sol (gourdins, lames...) du combat précédent.
            DroppedWeaponPickup.ClearAllDropped();
            _smokeZones.Clear();
            _gridVisualizer?.UpdatePersistentZones(_smokeZones);
            CombatUI.DroppedWeaponContextMenuUI.Instance?.CloseMenu();

            // 1. Interrompre toutes les coroutines actives et rétablir le temps réel
            StopAllCoroutines();
            _guard.ForceReset();
            _aiController?.StopAITurn();
            _cinematicDirector?.ResetCinematicState();

            // 2. Fermer les menus contextuels et purger la sélection
            CombatUI.CombatContextMenuUI.Instance?.CloseMenu();
            CombatUI.TacticalSelectionManager.Instance?.ClearSelection();

            // 3. Purger les nœuds de la grille et le gestionnaire de tours
            _grid?.ResetGridState();
            _turnManager?.ClearUnits();
            _turnManager?.ResetCombatState();

            // 4. Nettoyer les surbrillances visuelles
            if (_gridVisualizer != null)
            {
                _gridVisualizer.ClearPathPreview();
                _gridVisualizer.SetHoveredCoord(null);
                _gridVisualizer.SetTargetCoord(null);
                _gridVisualizer.SetReachableCoords(null);
            }

            // 5. Si une carte personnalisée est chargée, la rétablir fidèlement
            if (HasLoadedMap)
            {
                RestoreLoadedMap();
                return;
            }

            // 6. Recréer les unités par défaut
            SetupArenaUnits();

            // 7. Réappliquer le layout d'obstacles et cibler le premier mannequin
            ApplyArenaLayout(_currentLayout);
            if (SparringDummies.Count > 0)
            {
                SelectTarget(SparringDummies[0]);
            }

            // 8. Recentrer la caméra sur le joueur
            if (_cameraController != null && PlayerUnit != null)
            {
                _cameraController.FocusOn(PlayerUnit.transform);
            }

            if (Killtime.Multi.VTTTableSync.Instance != null 
                && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction 
                && Killtime.Multi.VTTRoomManager.Instance != null 
                && Killtime.Multi.VTTRoomManager.Instance.InRoom 
                && Killtime.Multi.VTTRoomManager.Instance.IsGM)
            {
                Killtime.Multi.VTTTableSync.Instance.BroadcastArenaReset((int)_currentLayout);
            }

            RecordChronoSnapshot("Réinitialisation de l'Arène");    
            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.Play(SoundId.Arena_Reset, 0.8f);
                _adaptiveTurnCount = 0;
                KilltimeAudioManager.Instance.PlayMusic(MusicMood.Combat, 0, 0.5f, forceRestart: true);
                UpdateAdaptiveMusic();
            }
            Log("🔄 Arène entièrement réinitialisée et stabilisée.");
        }

        /// <summary>
        /// Pilote la musique adaptative depuis l'état tactique réel :
        /// ratio PV alliés, survivants par camp, avancement des tours.
        /// Appelé sur événements (tour, dégâts, reset) — jamais chaque frame.
        /// </summary>
        private void UpdateAdaptiveMusic()
        {
            var mgr = KilltimeAudioManager.Instance;
            if (mgr == null) return;
            try
            {
                int alliesAlive = 0, enemiesAlive = 0;
                float hpSum = 0f, maxSum = 0f;
                float eHpSum = 0f, eMaxSum = 0f;
                foreach (var u in TacticalUnitRegistry.AllUnits)
                {
                    if (u == null || u.Stats == null) continue;
                    bool alive = u.Stats.IsAlive && !u.Stats.IsSurrendered;
                    if (u.IsPlayerControlled)
                    {
                        if (alive) alliesAlive++;
                        hpSum += Mathf.Max(0, u.Stats.CurrentHealth);
                        maxSum += Mathf.Max(1, u.Stats.MaxHealth);
                    }
                    else
                    {
                        if (alive) enemiesAlive++;
                        eHpSum += Mathf.Max(0, u.Stats.CurrentHealth);
                        eMaxSum += Mathf.Max(1, u.Stats.MaxHealth);
                    }
                }
                float ratio = maxSum > 0f ? Mathf.Clamp01(hpSum / maxSum) : 1f;
                float enemyRatio = eMaxSum > 0f ? Mathf.Clamp01(eHpSum / eMaxSum) : -1f;
                bool isPlayerTurn = _turnManager == null || _turnManager.ActiveUnit == null || _turnManager.ActiveUnit.IsPlayerControlled;
                mgr.NotifyBattleState(ratio, alliesAlive, enemiesAlive, _adaptiveTurnCount, enemyRatio, isPlayerTurn);
            }
            catch (System.Exception) { /* audio jamais bloquant */ }
        }

        public TacticalUnit SpawnThomas(HexCoordinates coords, bool isPlayer = true)
        {
            return _unitStore.SpawnThomas(coords, isPlayer);
        }

        public TacticalUnit SpawnJohn(HexCoordinates coords, bool isPlayer = true)
        {
            return _unitStore.SpawnJohn(coords, isPlayer);
        }

        public TacticalUnit SpawnErika(HexCoordinates coords, bool isPlayer = true)
        {
            return _unitStore.SpawnErika(coords, isPlayer);
        }

        public TacticalUnit SpawnVance(HexCoordinates coords, bool isPlayer = true)
        {
            return _unitStore.SpawnVance(coords, isPlayer);
        }

        public void SpawnHeroicTrio(HexCoordinates centerCoords)
        {
            _unitStore.SpawnHeroicTrio(centerCoords);
        }

        public void SpawnHeroicQuatuor(HexCoordinates centerCoords)
        {
            _unitStore.SpawnHeroicQuatuor(centerCoords);
        }

        public void SpawnHeroicQuintet(HexCoordinates centerCoords)
        {
            _unitStore.SpawnHeroicQuintet(centerCoords);
        }

        public void SpawnHeroicSextet(HexCoordinates centerCoords)
        {
            _unitStore.SpawnHeroicSextet(centerCoords);
        }
    }
}
