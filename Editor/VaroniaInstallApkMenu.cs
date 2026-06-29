// Editor menu : installe le dernier APK builde sur le premier device ADB connecte.
//
// Resolution du "dernier APK" : on regarde les 2 emplacements possibles et on prend
// le plus recent :
//   1. Le path du pipeline VBO   : <VBO_BuildPath>/<ProductName>/<ProductName>.apk
//   2. Le dernier path du menu Unity standard (Build Settings)
//
// "Premier device" = premier device au statut "device" dans `adb devices`
// (les entrees offline/unauthorized et l'emulateur fantome sont ignores).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VaroniaBackOffice.EditorTools
{
    internal static class VaroniaInstallApkMenu
    {
        [MenuItem("Varonia/PICO Device/Install Last APK", priority = 210)]
        private static void InstallLastApk()
        {
            // 1) Trouver le dernier APK builde
            string apkPath = ResolveLatestApk(out string source);
            if (apkPath == null)
            {
                EditorUtility.DisplayDialog("Install APK",
                    "Aucun APK trouve.\n\nBuild d'abord via Varonia/Build Menu ou File > Build Settings.", "OK");
                return;
            }

            // 2) Trouver le premier device ADB valide
            string serial = GetFirstDevice(out string model, out string err);
            if (serial == null)
            {
                EditorUtility.DisplayDialog("Install APK",
                    "Aucun device ADB connecte.\n\n" + (err ?? "Brancher le casque et autoriser le debug USB."), "OK");
                return;
            }

            var apkInfo = new FileInfo(apkPath);
            if (!EditorUtility.DisplayDialog("Install APK",
                $"Installer :\n{apkPath}\n({apkInfo.Length / (1024 * 1024)} MB, builde le {apkInfo.LastWriteTime:dd/MM HH:mm}, source : {source})\n\n" +
                $"Sur :\n{serial}{(string.IsNullOrEmpty(model) ? "" : $"  ({model})")}\n\n" +
                "adb install -r (reinstall, conserve les donnees)", "Installer", "Annuler"))
                return;

            // 3) Install avec progress bar (les gros APK prennent du temps)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "adb",
                    Arguments              = $"-s {serial} install -r -d \"{apkPath}\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                using (var p = Process.Start(psi))
                {
                    float t = 0f;
                    while (!p.HasExited && t < 300f)
                    {
                        EditorUtility.DisplayProgressBar("Install APK",
                            $"adb install vers {serial}... ({t:F0}s)", Mathf.PingPong(t * 0.1f, 1f));
                        System.Threading.Thread.Sleep(250);
                        t += 0.25f;
                    }
                    EditorUtility.ClearProgressBar();

                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();

                    if (!p.HasExited) { p.Kill(); throw new TimeoutException("adb install > 300s"); }

                    bool success = p.ExitCode == 0 && stdout.Contains("Success");
                    if (success)
                    {
                        Debug.Log($"[VaroniaInstallApk] OK - {apkPath} -> {serial}\n{stdout}");
                        EditorUtility.DisplayDialog("Install APK", $"✓ Installation reussie sur {serial}.", "OK");
                    }
                    else
                    {
                        Debug.LogError($"[VaroniaInstallApk] FAIL (code {p.ExitCode})\nstdout: {stdout}\nstderr: {stderr}");
                        EditorUtility.DisplayDialog("Install APK",
                            $"Echec installation (code {p.ExitCode}) :\n\n{(string.IsNullOrEmpty(stderr) ? stdout : stderr)}", "OK");
                    }
                }
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[VaroniaInstallApk] Exception : {e.Message}");
                EditorUtility.DisplayDialog("Install APK", "Erreur : " + e.Message, "OK");
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>Retourne l'APK le plus recent parmi les emplacements connus, ou null.</summary>
        private static string ResolveLatestApk(out string source)
        {
            source = null;
            var candidates = new List<(string path, string label)>();

            // Pipeline VBO
            string vboPath = EditorPrefs.GetString("VBO_BuildPath");
            if (!string.IsNullOrEmpty(vboPath))
                candidates.Add((Path.Combine(vboPath, Application.productName, Application.productName + ".apk"), "VBO Build Menu"));

            // Dernier build du menu Unity standard
            string unityPath = EditorUserBuildSettings.GetBuildLocation(BuildTarget.Android);
            if (!string.IsNullOrEmpty(unityPath))
                candidates.Add((unityPath, "Unity Build Settings"));

            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (var (path, label) in candidates)
            {
                if (!File.Exists(path)) continue;
                var time = File.GetLastWriteTime(path);
                if (time > bestTime)
                {
                    bestTime = time;
                    best = path;
                    source = label;
                }
            }
            return best;
        }

        /// <summary>Premier device ADB au statut "device" (ignore offline/unauthorized/ghost).</summary>
        private static string GetFirstDevice(out string model, out string err)
        {
            model = null;
            err = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "adb",
                    Arguments              = "devices -l",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);

                    foreach (var rawLine in output.Split('\n'))
                    {
                        var line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("List of devices") || line.StartsWith("*")) continue;

                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2 || parts[1] != "device") continue;

                        for (int i = 2; i < parts.Length; i++)
                            if (parts[i].StartsWith("model:")) { model = parts[i].Substring("model:".Length); break; }

                        return parts[0];
                    }
                }
            }
            catch (Exception e)
            {
                err = "adb introuvable ou erreur : " + e.Message;
            }
            return null;
        }
    }
}
