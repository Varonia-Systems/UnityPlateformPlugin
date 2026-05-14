// Editor menu : push le NewSpatial.json local (Windows host) vers le device Android
// connecte en ADB, dans /storage/emulated/0/Varonia/NewSpatial.json
//
// Si plusieurs devices sont connectes, affiche un menu contextuel pour choisir.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VaroniaBackOffice.EditorTools
{
    internal static class VaroniaPushSpatialMenu
    {
        private const string AndroidTargetDir  = "/storage/emulated/0/Varonia";
        private const string AndroidTargetPath = "/storage/emulated/0/Varonia/NewSpatial.json";
        private const string FileName          = "NewSpatial.json";

        private struct DeviceInfo
        {
            public string Serial;
            public string Model;
            public override string ToString() => string.IsNullOrEmpty(Model) ? Serial : $"{Serial}  ({Model})";
        }

        [MenuItem("Varonia/Push NewSpatial.json to Device", priority = 200)]
        private static void PushSpatial()
        {
            // 1) Verifier source cote Windows
            string rootPath = Application.persistentDataPath
                .Replace(Application.companyName + "/" + Application.productName, "Varonia");
            string sourcePath = Path.Combine(rootPath, FileName);

            if (!File.Exists(sourcePath))
            {
                EditorUtility.DisplayDialog(
                    "Push " + FileName,
                    "Fichier introuvable cote Windows :\n\n" + sourcePath +
                    "\n\nGenere d'abord ta config spatiale via l'outil Varonia.",
                    "OK");
                return;
            }

            // 2) Lister les devices ADB connectes
            var devices = ListDevices(out string listErr);
            if (devices == null)
            {
                EditorUtility.DisplayDialog("Push " + FileName,
                    "Erreur lors de l'execution de 'adb devices' :\n\n" + listErr +
                    "\n\nVerifier qu'adb est dans le PATH systeme.", "OK");
                return;
            }
            if (devices.Count == 0)
            {
                EditorUtility.DisplayDialog("Push " + FileName,
                    "Aucun device ADB connecte.\n\n" +
                    "Brancher le casque en USB et autoriser le debug, ou lancer 'adb connect <ip>'.", "OK");
                return;
            }

            // 3) Si un seul device : push direct. Sinon : menu de choix.
            if (devices.Count == 1)
            {
                DoPush(devices[0], sourcePath);
                return;
            }

            ShowDeviceChoiceMenu(devices, sourcePath);
        }

        private static void ShowDeviceChoiceMenu(List<DeviceInfo> devices, string sourcePath)
        {
            var menu = new GenericMenu();
            foreach (var dev in devices)
            {
                var devCopy = dev; // capture par valeur pour la lambda
                menu.AddItem(new GUIContent(dev.ToString()), false, () => DoPush(devCopy, sourcePath));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("→ All devices"), false, () =>
            {
                int ok = 0, fail = 0;
                foreach (var d in devices)
                {
                    if (DoPushSilent(d, sourcePath)) ok++; else fail++;
                }
                EditorUtility.DisplayDialog("Push " + FileName,
                    $"Termine.\n\n{ok} OK / {fail} echec(s).\nVoir Console pour le detail.", "OK");
            });
            menu.ShowAsContext();
        }

        private static void DoPush(DeviceInfo device, string sourcePath)
        {
            // S'assurer du dossier cible
            int mkRes = RunAdb(device.Serial, "shell mkdir -p " + AndroidTargetDir, out _, out string mkErr);
            if (mkRes != 0)
                Debug.LogWarning($"[VaroniaPushSpatial] mkdir warning ({device.Serial}, code {mkRes}) : {mkErr}");

            // Push
            int pushRes = RunAdb(device.Serial,
                "push \"" + sourcePath + "\" " + AndroidTargetPath,
                out string pushOut, out string pushErr);

            if (pushRes == 0)
            {
                Debug.Log($"[VaroniaPushSpatial] OK [{device}] - {sourcePath} -> {AndroidTargetPath}\n{pushOut}");
                EditorUtility.DisplayDialog("Push " + FileName,
                    $"Push reussi sur :\n{device}\n\nSource :\n{sourcePath}\n\nDestination :\n{AndroidTargetPath}",
                    "OK");
            }
            else
            {
                Debug.LogError($"[VaroniaPushSpatial] FAIL [{device}] code {pushRes} : {pushErr}\n{pushOut}");
                EditorUtility.DisplayDialog("Push " + FileName,
                    $"Erreur push sur :\n{device}\n\nCode {pushRes} :\n{pushErr}", "OK");
            }
        }

        private static bool DoPushSilent(DeviceInfo device, string sourcePath)
        {
            RunAdb(device.Serial, "shell mkdir -p " + AndroidTargetDir, out _, out _);
            int pushRes = RunAdb(device.Serial,
                "push \"" + sourcePath + "\" " + AndroidTargetPath,
                out string pushOut, out string pushErr);
            if (pushRes == 0)
            {
                Debug.Log($"[VaroniaPushSpatial] OK [{device}]");
                return true;
            }
            Debug.LogError($"[VaroniaPushSpatial] FAIL [{device}] code {pushRes} : {pushErr}");
            return false;
        }

        // ─── ADB helpers ─────────────────────────────────────────────────────────

        private static List<DeviceInfo> ListDevices(out string err)
        {
            err = null;
            int code = RunAdb(null, "devices -l", out string output, out string stderr);
            if (code != 0)
            {
                err = string.IsNullOrEmpty(stderr) ? ("adb devices code " + code) : stderr;
                return null;
            }
            var list = new List<DeviceInfo>();
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("List of devices")) continue;
                if (line.StartsWith("*")) continue; // "* daemon ..." messages

                // Format : <serial>  device  [props...]   ou  <serial>  offline   etc.
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (parts[1] != "device") continue; // skip offline/unauthorized

                string model = "";
                for (int i = 2; i < parts.Length; i++)
                {
                    if (parts[i].StartsWith("model:")) { model = parts[i].Substring("model:".Length); break; }
                }
                list.Add(new DeviceInfo { Serial = parts[0], Model = model });
            }
            return list;
        }

        private static int RunAdb(string deviceSerial, string args, out string stdOut, out string stdErr)
        {
            string fullArgs = string.IsNullOrEmpty(deviceSerial) ? args : ("-s " + deviceSerial + " " + args);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "adb",
                    Arguments              = fullArgs,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) { stdOut = ""; stdErr = "Process.Start returned null"; return -1; }
                    stdOut = p.StandardOutput.ReadToEnd();
                    stdErr = p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return p.ExitCode;
                }
            }
            catch (Exception e)
            {
                stdOut = "";
                stdErr = "adb introuvable ou erreur lancement : " + e.Message;
                return -1;
            }
        }
    }
}
