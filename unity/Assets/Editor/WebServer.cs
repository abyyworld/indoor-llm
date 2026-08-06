// Starts and stops serve-study.py from the control panel.
//
// The browser route is the one that works on a headset somebody else owns -- no
// Developer Mode, no cable, no install -- so it should not require a terminal. This is
// the same script either researcher would run by hand; the panel just owns its lifetime
// so nobody has to remember to stop it, and so the addresses are shown rather than
// looked up.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace EmotionRooms.EditorTools
{
    [InitializeOnLoad]
    public static class WebServer
    {
        const int Port = 8443;
        static Process process;

        public static bool IsRunning { get { return process != null && !process.HasExited; } }

        public static string PanelUrl { get { return "https://localhost:" + Port + "/"; } }

        public static string HeadsetUrl
        {
            get { return "https://" + LocalAddress() + ":" + Port + "/vr.html"; }
        }

        static WebServer()
        {
            // A server left running past a domain reload would hold the port and the next
            // Start would fail with a message about the port rather than about the reload.
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        public static void Start(string repoPath)
        {
            if (IsRunning) return;

            if (!Directory.Exists(repoPath) ||
                !File.Exists(Path.Combine(repoPath, "serve-study.py")))
            {
                EditorUtility.DisplayDialog("Emotion Rooms",
                    "serve-study.py is not in the repo path.\n\nSet the repo path under " +
                    "\"If something goes wrong\".", "OK");
                return;
            }

            try
            {
                process = Process.Start(new ProcessStartInfo("/bin/bash",
                    "-c \"python3 serve-study.py\"")
                {
                    WorkingDirectory = repoPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                });

                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) Debug.Log("study server: " + e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning("study server: " + e.Data);
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                Debug.Log("Study server starting.\n  Researcher panel: " + PanelUrl +
                          "\n  In the headset:   " + HeadsetUrl);
            }
            catch (Exception e)
            {
                Debug.LogError("Could not start the study server: " + e.Message);
                process = null;
            }
        }

        public static void Stop()
        {
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); } catch (Exception) { }
            process = null;
            Debug.Log("Study server stopped.");
        }

        static string LocalAddress()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = address.Address.ToString();
                        if (!ip.StartsWith("169.254")) return ip;
                    }
                }
            }
            catch (Exception) { }
            return "localhost";
        }
    }
}
