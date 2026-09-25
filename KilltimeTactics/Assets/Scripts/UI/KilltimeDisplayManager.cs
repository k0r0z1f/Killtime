using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.UI
{
    /// <summary>
    /// Gestionnaire multi-écrans pour Killtime Tactics.
    /// Permet de :
    /// 1. Déplacer la fenêtre principale du jeu vers un écran cible (MoveMainWindowTo).
    /// 2. Mémoriser l'écran favori (PlayerPrefs) ou lire l'argument de ligne de commande (-monitor / -display).
    /// 3. Basculer instantanément d'écran via le raccourci Ctrl + Shift + D.
    /// </summary>
    public class KilltimeDisplayManager : MonoBehaviour
    {
        private static KilltimeDisplayManager _instance;
        private const string PrefKeyDisplayIndex = "KT_TargetDisplayIndex";

        private readonly List<DisplayInfo> _displays = new();
        private float _toastTimer = 0f;
        private string _toastMessage = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("[KilltimeDisplayManager]");
            _instance = go.AddComponent<KilltimeDisplayManager>();
            DontDestroyOnLoad(go);
        }

        private void Start()
        {
            RefreshDisplays();
            ApplyStartupDisplay();
        }

        private void Update()
        {
            // Raccourci Ctrl + Shift + D pour cycler à l'écran suivant
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) &&
                Input.GetKeyDown(KeyCode.D))
            {
                CycleNextDisplay();
            }

            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
            }
        }

        public void RefreshDisplays()
        {
            _displays.Clear();
            try
            {
                Screen.GetDisplayLayout(_displays);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KilltimeDisplayManager] GetDisplayLayout exception: {ex.Message}");
            }
        }

        private void ApplyStartupDisplay()
        {
            RefreshDisplays();
            if (_displays.Count <= 1) return;

            int targetIndex = -1;

            // 1. Priorité aux arguments CLI (-monitor ou -display)
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("-display", StringComparison.OrdinalIgnoreCase) ||
                    args[i].Equals("-monitor", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(args[i + 1], out int val))
                    {
                        // Les arguments -monitor de Unity sont souvent 1-based
                        targetIndex = (val >= 1 && val <= _displays.Count) ? val - 1 : val;
                        break;
                    }
                }
            }

            // 2. Préférence sauvegardée si aucun argument explicite
            if (targetIndex < 0 && PlayerPrefs.HasKey(PrefKeyDisplayIndex))
            {
                targetIndex = PlayerPrefs.GetInt(PrefKeyDisplayIndex, -1);
            }

            if (targetIndex >= 0 && targetIndex < _displays.Count)
            {
                MoveToDisplay(targetIndex, savePreference: false);
            }
        }

        public void CycleNextDisplay()
        {
            RefreshDisplays();
            if (_displays.Count <= 1)
            {
                ShowToast("Un seul écran détecté.");
                return;
            }

            int currentIdx = GetCurrentDisplayIndex();
            int nextIdx = (currentIdx + 1) % _displays.Count;
            MoveToDisplay(nextIdx, savePreference: true);
        }

        public void MoveToDisplay(int index, bool savePreference = true)
        {
            RefreshDisplays();
            if (index < 0 || index >= _displays.Count) return;

            var targetDisplay = _displays[index];
            try
            {
                Screen.MoveMainWindowTo(targetDisplay, Vector2Int.zero);
                string monName = string.IsNullOrEmpty(targetDisplay.name) ? $"Écran #{index + 1}" : targetDisplay.name;
                ShowToast($"Déplacé vers {monName} ({targetDisplay.width}x{targetDisplay.height})");

                if (savePreference)
                {
                    PlayerPrefs.SetInt(PrefKeyDisplayIndex, index);
                    PlayerPrefs.Save();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KilltimeDisplayManager] Échec du déplacement: {ex.Message}");
            }
        }

        public int GetCurrentDisplayIndex()
        {
            try
            {
                var cur = Screen.mainWindowDisplayInfo;
                for (int i = 0; i < _displays.Count; i++)
                {
                    if (_displays[i].name == cur.name &&
                        _displays[i].width == cur.width &&
                        _displays[i].height == cur.height)
                    {
                        return i;
                    }
                }
            }
            catch { /* fallback */ }
            return 0;
        }

        private void ShowToast(string message)
        {
            _toastMessage = message;
            _toastTimer = 3.5f;
            Debug.Log($"[KilltimeDisplayManager] {message}");
        }

        private void OnGUI()
        {
            if (_toastTimer > 0f && !string.IsNullOrEmpty(_toastMessage))
            {
                float w = 420f;
                float h = 32f;
                Rect r = new Rect((Screen.width - w) * 0.5f, 20f, w, h);

                Color prevBg = GUI.backgroundColor;
                Color prevCol = GUI.color;

                GUI.backgroundColor = new Color(0.08f, 0.12f, 0.18f, 0.92f);
                GUI.color = new Color(0.3f, 0.9f, 1f, 1f);

                GUI.Box(r, $"🖥 {_toastMessage}");

                GUI.backgroundColor = prevBg;
                GUI.color = prevCol;
            }
        }
    }
}
