using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Killtime.Multi
{
    /// <summary>
    /// Client WebSocket bas niveau pour le hub VTT (/ws/vtt).
    /// - Standalone / Éditeur : System.Net.WebSockets.ClientWebSocket (zéro package).
    /// - WebGL : pont natif navigateur via VTTWebSocket.jslib (ClientWebSocket
    ///   n'existe pas sur WebGL, le .jslib utilise le WebSocket du browser).
    /// Les callbacks réseau arrivent sur un thread secondaire : tout est
    /// repompé vers le thread Unity dans Update() (file thread-safe).
    /// </summary>
    [DisallowMultipleComponent]
    public class VTTNetworkClient : MonoBehaviour
    {
        [Header("Hub VTT")]
        [Tooltip("Ex: wss://votre-domaine/ws/vtt (prod) ou ws://localhost:3000/ws/vtt (local).")]
        [SerializeField] private string _serverUrl = VTTProtocol.DefaultHubUrl;
        [SerializeField] private float _reconnectDelaySec = 3f;
        [SerializeField] private bool _autoReconnect = false;

        public string ServerUrl => _serverUrl;
        public bool IsConnected => _connected;
        public event Action OnConnected;
        public event Action<string> OnDisconnected; // raison
        public event Action<string> OnMessage;      // JSON brut
        public event Action<string> OnError;        // message d'erreur lisible

        private volatile bool _connected;
        private readonly ConcurrentQueue<string> _incoming = new();
        private readonly ConcurrentQueue<Action> _mainThread = new();

        private float _nextReconnectAt = -1f;
        private string _lastUrl;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void VTTWS_Connect(string url, string goName);
        [DllImport("__Internal")] private static extern void VTTWS_Send(string msg);
        [DllImport("__Internal")] private static extern void VTTWS_Close();
        private string _goName;
#else
        private System.Net.WebSockets.ClientWebSocket _ws;
        private CancellationTokenSource _cts;
#endif

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
#if UNITY_WEBGL && !UNITY_EDITOR
            _goName = gameObject.name;
#endif
        }

        private void Update()
        {
            while (_mainThread.TryDequeue(out var a))
            {
                try { a?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            }
            while (_incoming.TryDequeue(out var msg))
            {
                try { OnMessage?.Invoke(msg); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (_autoReconnect && !_connected && _nextReconnectAt > 0f && Time.realtimeSinceStartup >= _nextReconnectAt)
            {
                _nextReconnectAt = -1f;
                Connect(_lastUrl ?? _serverUrl);
            }
        }

        public void SetServerUrl(string url)
        {
            _serverUrl = url;
        }

        public void Connect(string url = null)
        {
            _lastUrl = string.IsNullOrEmpty(url) ? _serverUrl : url;
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                _goName = gameObject.name;
                VTTWS_Connect(_lastUrl, _goName);
            }
            catch (Exception e) { EmitError("WS connect failed: " + e.Message); }
#else
            _ = ConnectStandaloneAsync(_lastUrl);
#endif
        }

        public void Disconnect(string reason = "client_leave")
        {
            _nextReconnectAt = -1f;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { VTTWS_Close(); } catch { }
            SetDisconnected(reason);
#else
            _ = DisconnectStandaloneAsync(reason);
#endif
        }

        public void Send(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { VTTWS_Send(json); }
            catch (Exception e) { EmitError("WS send failed: " + e.Message); }
#else
            _ = SendStandaloneAsync(json);
#endif
        }

        // ---------- Callbacks invoqués depuis le .jslib WebGL (SendMessage) ----------
#if UNITY_WEBGL && !UNITY_EDITOR
        // Noms appelés par VTTWebSocket.jslib : ne pas renommer.
        public void OnVTTWSOpen(string _)
        {
            _mainThread.Enqueue(() => { SetConnected(); });
        }
        public void OnVTTWSMessage(string msg)
        {
            _incoming.Enqueue(msg ?? "");
        }
        public void OnVTTWSClose(string reason)
        {
            _mainThread.Enqueue(() => SetDisconnected(string.IsNullOrEmpty(reason) ? "ws_close" : reason));
        }
        public void OnVTTWSError(string err)
        {
            _mainThread.Enqueue(() => EmitError(string.IsNullOrEmpty(err) ? "ws_error" : err));
        }
#endif

        private void SetConnected()
        {
            _connected = true;
            try { OnConnected?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        private void SetDisconnected(string reason)
        {
            bool was = _connected;
            _connected = false;
            if (_autoReconnect) _nextReconnectAt = Time.realtimeSinceStartup + _reconnectDelaySec;
            if (was)
            {
                try { OnDisconnected?.Invoke(reason); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        private void EmitError(string err)
        {
            Debug.LogWarning($"[VTT] {err}");
            try { OnError?.Invoke(err); } catch (Exception e) { Debug.LogException(e); }
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private async Task ConnectStandaloneAsync(string url)
        {
            try { await DisconnectStandaloneAsync("reconnect", silent: true); } catch { }
            _cts = new CancellationTokenSource();
            var ws = new System.Net.WebSockets.ClientWebSocket();
            _ws = ws;
            try
            {
                await ws.ConnectAsync(new Uri(url), _cts.Token);
                _mainThread.Enqueue(SetConnected);
                _ = ReceiveLoopAsync(ws, _cts.Token);
            }
            catch (Exception e)
            {
                _mainThread.Enqueue(() =>
                {
                    EmitError("Connexion VTT impossible (" + url + ") : " + e.Message);
                    SetDisconnected("connect_failed");
                });
            }
        }

        private async Task ReceiveLoopAsync(System.Net.WebSockets.ClientWebSocket ws, CancellationToken token)
        {
            var buffer = new byte[64 * 1024];
            var sb = new StringBuilder();
            try
            {
                while (ws.State == System.Net.WebSockets.WebSocketState.Open && !token.IsCancellationRequested)
                {
                    sb.Clear();
                    System.Net.WebSockets.WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                        {
                            _mainThread.Enqueue(() => SetDisconnected("server_close"));
                            return;
                        }
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);
                    _incoming.Enqueue(sb.ToString());
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                _mainThread.Enqueue(() =>
                {
                    EmitError("Lien VTT interrompu : " + e.Message);
                    SetDisconnected("recv_error");
                });
            }
        }

        private async Task SendStandaloneAsync(string json)
        {
            var ws = _ws;
            if (ws == null || ws.State != System.Net.WebSockets.WebSocketState.Open) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(new ArraySegment<byte>(bytes),
                    System.Net.WebSockets.WebSocketMessageType.Text, true,
                    _cts != null ? _cts.Token : CancellationToken.None);
            }
            catch (Exception e)
            {
                _mainThread.Enqueue(() => EmitError("Envoi VTT impossible : " + e.Message));
            }
        }

        private async Task DisconnectStandaloneAsync(string reason, bool silent = false)
        {
            var ws = _ws;
            _ws = null;
            try { if (_cts != null) _cts.Cancel(); } catch { }
            if (ws != null)
            {
                try
                {
                    if (ws.State == System.Net.WebSockets.WebSocketState.Open)
                        await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, reason,
                            CancellationToken.None);
                    ws.Dispose();
                }
                catch { }
            }
            try { if (_cts != null) _cts.Dispose(); } catch { }
            _cts = null;
            if (!silent) _mainThread.Enqueue(() => SetDisconnected(reason));
            else _connected = false;
        }

        private void OnDestroy()
        {
            try { if (_cts != null) _cts.Cancel(); } catch { }
        }
#endif
    }
}
