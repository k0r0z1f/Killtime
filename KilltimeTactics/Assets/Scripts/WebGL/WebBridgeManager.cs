using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Killtime.Core.Character;

namespace Killtime.WebGL
{
    /// <summary>
    /// Gestionnaire de communication bidirectionnelle WebGL (C# ↔ JavaScript).
    /// Permet d'échanger des données en temps réel avec le portail et les outils du Codex Killtime.
    /// </summary>
    public class WebBridgeManager : MonoBehaviour
    {
        public static WebBridgeManager Instance { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void NotifyCodexCombatEvent(string eventJson);

        [DllImport("__Internal")]
        private static extern void SyncCharacterStatsToWeb(string characterJson);
#else
        private static void NotifyCodexCombatEvent(string eventJson) => Debug.Log($"[WebGL Mock] NotifyCodexCombatEvent: {eventJson}");
        private static void SyncCharacterStatsToWeb(string characterJson) => Debug.Log($"[WebGL Mock] SyncCharacterStatsToWeb: {characterJson}");
#endif

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void SendCombatEvent(string eventType, string logMessage, int damage = 0)
        {
            string json = $"{{\"type\":\"{eventType}\",\"message\":\"{logMessage}\",\"damage\":{damage}}}";
            NotifyCodexCombatEvent(json);
        }

        public void SyncCharacter(CharacterStats stats)
        {
            if (stats == null) return;
            string json = $"{{\"name\":\"{stats.Name}\",\"hp\":{stats.CurrentHealth},\"maxHp\":{stats.MaxHealth},\"ap\":{stats.CurrentActionPoints},\"maxAp\":{stats.MaxActionPoints},\"essoufflement\":{stats.Essoufflement}}}";
            SyncCharacterStatsToWeb(json);
        }

        /// <summary>
        /// Méthode appelée depuis JavaScript via unityInstance.SendMessage('WebBridge', 'ImportCharacterJson', jsonString)
        /// </summary>
        public void ImportCharacterJson(string json)
        {
            Debug.Log($"[WebBridgeManager] Données PJ reçues du Web: {json}");
            // Traitement et injection dans l'unité active
        }
    }
}
