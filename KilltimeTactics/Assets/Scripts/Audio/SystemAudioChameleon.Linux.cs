using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Audio.Experimental
{
    /// <summary>SystemAudioChameleon — PipeWire Linux — scan/link/unlink anti-Larsen (partial, move-only).</summary>
    public partial class SystemAudioChameleon : MonoBehaviour
    {
        public void RefreshLinuxApps()
        {
            DetectedMediaApps.Clear();
            if (Application.platform != RuntimePlatform.LinuxPlayer && Application.platform != RuntimePlatform.LinuxEditor)
                return;

            string outputs = RunCommandSimple("pw-link", "-o");
            if (string.IsNullOrEmpty(outputs)) return;

            var lines = outputs.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var appMap = new Dictionary<string, MediaAppInfo>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;

                string node = line.Substring(0, colon).Trim();
                string port = line.Substring(colon + 1).Trim();
                string nodeLower = node.ToLowerInvariant();
                string portLower = port.ToLowerInvariant();

                if (nodeLower.StartsWith("alsa_") ||
                    nodeLower.StartsWith("system") ||
                    nodeLower.StartsWith("v4l2") ||
                    nodeLower.StartsWith("midi") ||
                    nodeLower.StartsWith("bluez_") ||
                    nodeLower.Contains("fmod") ||
                    nodeLower.Contains("speech-dispatcher") ||
                    nodeLower.Contains("easyeffects") ||
                    nodeLower.Contains("wireplumber") ||
                    nodeLower.Contains("pipewire") ||
                    nodeLower.Contains("dummy") ||
                    nodeLower.Contains("loopback") ||
                    nodeLower.Contains("unity") ||
                    nodeLower.Contains("killtime"))
                {
                    continue;
                }

                bool isPlaybackPort = portLower.Contains("output") || portLower.Contains("playback") || portLower.EndsWith("_fl") || portLower.EndsWith("_fr");
                bool isNotCapture = !portLower.Contains("capture") && !portLower.Contains("monitor") && !portLower.Contains("midi");
                if (!isPlaybackPort || !isNotCapture) continue;

                string displayName = CleanApplicationName(node);

                if (!appMap.TryGetValue(node, out var appInfo))
                {
                    appInfo = new MediaAppInfo
                    {
                        DisplayName = displayName,
                        NodeName = node
                    };
                    appMap[node] = appInfo;
                }

                if (!appInfo.Ports.Contains(line))
                {
                    appInfo.Ports.Add(line);
                }
            }

            DetectedMediaApps.AddRange(appMap.Values);
        }

        public bool LinkLinuxApp(MediaAppInfo app)
        {
            if (app == null || app.Ports.Count == 0) return false;

            if (!IsRecordingActive)
            {
                StartCapture();
                System.Threading.Thread.Sleep(150);
            }

            string inputs = RunCommandSimple("pw-link", "-i");
            if (string.IsNullOrEmpty(inputs))
            {
                LinkStatusMessage = "<color=#FF4444>pw-link -i n'a retourné aucun port.</color>";
                return false;
            }

            var inLines = inputs.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var unityPorts = new List<string>();

            for (int i = 0; i < inLines.Length; i++)
            {
                string line = inLines[i].Trim();
                string lower = line.ToLowerInvariant();
                if (lower.Contains("fmod") || lower.Contains("unity") || lower.Contains("killtime") || lower.Contains("alsa plug-in"))
                {
                    if (!lower.Contains("output_") && !lower.Contains("playback_") && !lower.Contains("monitor_"))
                    {
                        unityPorts.Add(line);
                    }
                }
            }

            if (unityPorts.Count == 0)
            {
                LinkStatusMessage = "<color=#FF4444>Port d'enregistrement Unity introuvable dans PipeWire.</color>";
                return false;
            }

            SeverLoopbackFeedbackLinks(unityPorts);

            if (!string.IsNullOrEmpty(LinkedLinuxApp) && !string.Equals(LinkedLinuxApp, app.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var prevApp = DetectedMediaApps.Find(a => string.Equals(a.DisplayName, LinkedLinuxApp, StringComparison.OrdinalIgnoreCase));
                if (prevApp != null) UnlinkLinuxApp(prevApp);
            }

            int linkedCount = 0;
            string lastError = null;
            int maxLinks = Math.Min(app.Ports.Count, unityPorts.Count);

            for (int i = 0; i < maxLinks; i++)
            {
                var (exitCode, stdout, stderr) = ExecuteDirectLink(app.Ports[i], unityPorts[i], disconnect: false);
                if (exitCode == 0 || (!string.IsNullOrEmpty(stderr) && (stderr.ToLowerInvariant().Contains("exist") || stderr.ToLowerInvariant().Contains("already"))))
                {
                    linkedCount++;
                }
                else
                {
                    lastError = stderr;
                }
            }

            if (linkedCount > 0)
            {
                LinkedLinuxApp = app.DisplayName;
                LinkStatusMessage = $"<color=#00FF88>Connecté à {app.DisplayName} (Anti-Larsen actif)</color>";
                return true;
            }

            LinkStatusMessage = $"<color=#FF4444>Échec liaison ({lastError?.Trim()})</color>";
            return false;
        }

        private void SeverLoopbackFeedbackLinks(List<string> unityPorts)
        {
            string linksOutput = RunCommandSimple("pw-link", "-l");
            if (string.IsNullOrEmpty(linksOutput)) return;

            var lines = linksOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int arrowIdx = line.IndexOf("->", StringComparison.Ordinal);
                if (arrowIdx <= 0) continue;

                string src = line.Substring(0, arrowIdx).Trim();
                string dst = line.Substring(arrowIdx + 2).Trim();

                if (unityPorts.Contains(dst))
                {
                    string srcLower = src.ToLowerInvariant();
                    if (srcLower.Contains("monitor") || srcLower.Contains("alsa_output"))
                    {
                        ExecuteDirectLink(src, dst, disconnect: true);
                    }
                }
            }
        }

        public bool UnlinkLinuxApp(MediaAppInfo app)
        {
            if (app == null || app.Ports.Count == 0) return false;

            string inputs = RunCommandSimple("pw-link", "-i");
            if (string.IsNullOrEmpty(inputs)) return false;

            var inLines = inputs.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var unityPorts = new List<string>();

            for (int i = 0; i < inLines.Length; i++)
            {
                string line = inLines[i].Trim();
                string lower = line.ToLowerInvariant();
                if (lower.Contains("fmod") || lower.Contains("unity") || lower.Contains("killtime") || lower.Contains("alsa plug-in"))
                {
                    if (!lower.Contains("output_") && !lower.Contains("playback_") && !lower.Contains("monitor_"))
                    {
                        unityPorts.Add(line);
                    }
                }
            }

            int maxLinks = Math.Min(app.Ports.Count, unityPorts.Count);
            for (int i = 0; i < maxLinks; i++)
            {
                ExecuteDirectLink(app.Ports[i], unityPorts[i], disconnect: true);
            }

            if (string.Equals(LinkedLinuxApp, app.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                LinkedLinuxApp = null;
            }

            LinkStatusMessage = $"<color=yellow>Liaison rompue avec {app.DisplayName}.</color>";
            return true;
        }

        private static string CleanApplicationName(string rawNode)
        {
            string lower = rawNode.ToLowerInvariant();
            if (lower.Contains("chrome")) return "Google Chrome";
            if (lower.Contains("chromium")) return "Chromium";
            if (lower.Contains("firefox")) return "Firefox";
            if (lower.Contains("spotify")) return "Spotify";
            if (lower.Contains("vlc")) return "VLC Media Player";
            if (lower.Contains("mpv")) return "MPV Player";
            if (lower.Contains("discord")) return "Discord";
            if (lower.Contains("brave")) return "Brave Browser";
            return rawNode;
        }

        private static string RunCommandSimple(string cmd, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return null;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1000);
                return output;
            }
            catch
            {
                return null;
            }
        }

        private static (int exitCode, string stdout, string stderr) ExecuteDirectLink(string srcPort, string dstPort, bool disconnect = false)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "pw-link",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (disconnect) psi.ArgumentList.Add("-d");
                psi.ArgumentList.Add(srcPort);
                psi.ArgumentList.Add(dstPort);

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return (-1, "", "Process null");
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(1000);
                return (proc.ExitCode, stdout, stderr);
            }
            catch (Exception ex)
            {
                return (-1, "", ex.Message);
            }
        }
    }
}
