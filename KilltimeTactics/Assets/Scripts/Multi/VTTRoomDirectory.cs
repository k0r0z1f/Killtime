using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Killtime.Multi
{
    /// <summary>
    /// Client d'annuaire public des tables (server browser auto-hébergé).
    /// Interroge GET /api/vtt/rooms?build=X sur le même webserver que le hub WS :
    /// aucun compte tiers, aucun package — UnityWebRequest uniquement.
    /// Le hub ne liste que les rooms `public` du même build (filtrage serveur).
    /// </summary>
    [DisallowMultipleComponent]
    public class VTTRoomDirectory : MonoBehaviour
    {
        [SerializeField] private int _timeoutSec = 10;

        /// <summary>
        /// Convertit l'URL WS du hub (ws://hôte:port/ws/vtt) en URL HTTP
        /// d'annuaire (http://hôte:port/api/vtt/rooms?build=...).
        /// </summary>
        public static string BuildDirectoryUrl(string serverUrl, string buildVersion)
        {
            string http = (serverUrl ?? "").Trim();
            if (http.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                http = "https://" + http.Substring(6);
            else if (http.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
                http = "http://" + http.Substring(5);
            if (!http.StartsWith("http://") && !http.StartsWith("https://"))
                http = "http://" + http;
            // Ne garde que scheme + host + port (on jette /ws/vtt ou autre chemin).
            int schemeEnd = http.IndexOf("://", StringComparison.Ordinal) + 3;
            int slash = http.IndexOf('/', schemeEnd);
            string baseUrl = slash >= 0 ? http.Substring(0, slash) : http;
            string url = baseUrl + "/api/vtt/rooms";
            if (!string.IsNullOrEmpty(buildVersion))
                url += "?build=" + UnityWebRequest.EscapeURL(buildVersion);
            return url;
        }

        /// <summary>done(rooms, error) — error est null en cas de succès.</summary>
        public void FetchPublicRooms(string serverUrl, string buildVersion, Action<List<VTTPublicRoom>, string> done)
        {
            StartCoroutine(FetchRoutine(serverUrl, buildVersion, done));
        }

        private IEnumerator FetchRoutine(string serverUrl, string buildVersion, Action<List<VTTPublicRoom>, string> done)
        {
            string url = BuildDirectoryUrl(serverUrl, buildVersion);
            List<VTTPublicRoom> rooms = null;
            string error = null;
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.Max(3, _timeoutSec);
                // Traverse l'interstitiel ngrok (offre gratuite) ; sans effet en direct.
                req.SetRequestHeader("ngrok-skip-browser-warning", "true");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    error = string.IsNullOrEmpty(req.error) ? "requête annuaire impossible" : req.error;
                }
                else
                {
                    try
                    {
                        var resp = JsonUtility.FromJson<VTTRoomDirectoryResponse>(req.downloadHandler.text);
                        rooms = (resp != null && resp.rooms != null) ? resp.rooms : new List<VTTPublicRoom>();
                    }
                    catch (Exception e)
                    {
                        error = "réponse annuaire illisible : " + e.Message;
                    }
                }
            }
            try { done?.Invoke(rooms, error); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}
