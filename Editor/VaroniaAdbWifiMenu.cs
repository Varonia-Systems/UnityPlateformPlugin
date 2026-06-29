// Editor menu : active l'ADB WiFi sur le premier device ADB branche en USB.
//
// Flow classique :
//   1. Recupere l'IP WiFi du device (adb shell ip route)
//   2. Passe adbd en mode TCP : adb tcpip 5555
//   3. Connecte : adb connect <ip>:5555
// Apres ca, le cable USB peut etre debranche.

using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VaroniaBackOffice.EditorTools
{
    internal static class VaroniaAdbWifiMenu
    {
        private const int Port = 5555;

        [MenuItem("Varonia/PICO Device/Enable ADB WiFi", priority = 212)]
        private static void EnableAdbWifi()
        {
            // 1) Premier device USB (serial sans ":" — un serial ip:port = deja en WiFi)
            string serial = GetFirstUsbDevice(out string model, out bool alreadyWifi, out string err);
            if (serial == null)
            {
                EditorUtility.DisplayDialog("ADB WiFi",
                    alreadyWifi
                        ? "Le seul device connecte est deja en ADB WiFi."
                        : "Aucun device ADB branche en USB.\n\n" + (err ?? "Brancher le casque en cable d'abord."),
                    "OK");
                return;
            }

            // 2) IP WiFi du device
            RunAdb($"-s {serial} shell ip route", out string routeOut, out _, 10000);
            var ipMatch = Regex.Match(routeOut ?? "", @"src\s+(\d+\.\d+\.\d+\.\d+)");
            if (!ipMatch.Success)
            {
                EditorUtility.DisplayDialog("ADB WiFi",
                    $"Impossible de recuperer l'IP WiFi de {serial}.\n\n" +
                    "Le casque est-il connecte a un reseau WiFi ?", "OK");
                return;
            }
            string ip = ipMatch.Groups[1].Value;

            if (!EditorUtility.DisplayDialog("ADB WiFi",
                $"Device : {serial}{(string.IsNullOrEmpty(model) ? "" : $" ({model})")}\n" +
                $"IP WiFi : {ip}\n\n" +
                $"Activer le mode TCP ({Port}) et connecter en WiFi ?\n" +
                "Le cable USB pourra ensuite etre debranche.", "Activer", "Annuler"))
                return;

            // 3) tcpip + connect
            RunAdb($"-s {serial} tcpip {Port}", out string tcpOut, out string tcpErr, 10000);
            if ((tcpOut ?? "").IndexOf("restarting", StringComparison.OrdinalIgnoreCase) < 0)
                Debug.LogWarning($"[VaroniaAdbWifi] tcpip output inattendu : {tcpOut} {tcpErr}");

            System.Threading.Thread.Sleep(2000); // adbd redemarre sur le device

            int code = RunAdb($"connect {ip}:{Port}", out string connectOut, out string connectErr, 10000);
            bool ok = code == 0 && (connectOut ?? "").IndexOf("connected", StringComparison.OrdinalIgnoreCase) >= 0;

            if (ok)
            {
                Debug.Log($"[VaroniaAdbWifi] OK - {connectOut.Trim()}");
                EditorUtility.DisplayDialog("ADB WiFi",
                    $"✓ {connectOut.Trim()}\n\n" +
                    "Tu peux debrancher le cable USB.\n\n" +
                    $"Pour reconnecter plus tard :\nadb connect {ip}:{Port}", "OK");
            }
            else
            {
                Debug.LogError($"[VaroniaAdbWifi] FAIL : {connectOut} {connectErr}");
                EditorUtility.DisplayDialog("ADB WiFi",
                    $"Echec de la connexion :\n{connectOut}{connectErr}\n\n" +
                    "Verifier que le PC et le casque sont sur le meme reseau.", "OK");
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string GetFirstUsbDevice(out string model, out bool onlyWifiFound, out string err)
        {
            model = null;
            onlyWifiFound = false;
            err = null;

            int code = RunAdb("devices -l", out string output, out string stderr, 10000);
            if (code != 0) { err = stderr; return null; }

            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("List of devices") || line.StartsWith("*")) continue;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[1] != "device") continue;

                if (parts[0].Contains(":")) { onlyWifiFound = true; continue; } // deja en WiFi

                for (int i = 2; i < parts.Length; i++)
                    if (parts[i].StartsWith("model:")) { model = parts[i].Substring("model:".Length); break; }

                return parts[0];
            }
            return null;
        }

        private static int RunAdb(string args, out string stdOut, out string stdErr, int timeoutMs)
        {
            stdOut = "";
            stdErr = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "adb",
                    Arguments              = args,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                using (var p = Process.Start(psi))
                {
                    stdOut = p.StandardOutput.ReadToEnd();
                    stdErr = p.StandardError.ReadToEnd();
                    p.WaitForExit(timeoutMs);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception e)
            {
                stdErr = "adb introuvable ou erreur : " + e.Message;
                return -1;
            }
        }
    }
}
