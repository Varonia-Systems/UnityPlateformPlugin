// Fenêtre éditeur : pousse les fichiers de config Varonia vers un device Android (ADB).
//   • GlobalConfig.json  → /storage/emulated/0/Varonia/GlobalConfig.json
//   • NewSpatial.json    → /storage/emulated/0/Varonia/NewSpatial.json
//   • Config.json (GameConfig) → /storage/emulated/0/Android/data/<package>/files/Config.json
//
// Sélection du device ADB + cases à cocher par fichier.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VaroniaBackOffice.EditorTools
{
    public class VaroniaPushConfigWindow : EditorWindow
    {
        // ─── Couleurs (alignées sur les autres éditeurs) ──
        static readonly Color ColBg     = new Color(0.11f, 0.11f, 0.14f, 1f);
        static readonly Color ColCard   = new Color(0.15f, 0.15f, 0.19f, 1f);
        static readonly Color ColAccent = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color ColWarn   = new Color(1f, 0.60f, 0.10f, 1f);
        static readonly Color ColError  = new Color(1f, 0.40f, 0.40f, 1f);
        static readonly Color ColText   = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color ColMuted  = new Color(0.55f, 0.55f, 0.62f, 1f);

        struct DeviceInfo
        {
            public string Serial, Model;
            public override string ToString() => string.IsNullOrEmpty(Model) ? Serial : $"{Serial}  ({Model})";
        }

        class PushFile
        {
            public string Label, Host, Device;
            public bool   Selected;
            public bool   Available => File.Exists(Host);
        }

        List<DeviceInfo> _devices = new List<DeviceInfo>();
        int  _deviceIdx = 0;
        bool _allDevices = false;
        List<PushFile> _files = new List<PushFile>();
        string _adbError;
        Vector2 _scroll;

        static GUIStyle _header, _section, _path, _cardStyle;
        static Texture2D _cardTex;
        static bool _styles;

        [MenuItem("Varonia/PICO Device/Push Files", priority = 211)]
        public static void Open()
        {
            var w = GetWindow<VaroniaPushConfigWindow>("Push Files");
            w.minSize = new Vector2(480, 420);
            w.RefreshAll();
            w.Show();
        }

        private void OnEnable() => RefreshAll();

        private void RefreshAll()
        {
            BuildFiles();
            _devices = ListDevices(out _adbError) ?? new List<DeviceInfo>();
            if (_deviceIdx >= _devices.Count) _deviceIdx = 0;
        }

        private void BuildFiles()
        {
            string varonia = Application.persistentDataPath
                .Replace(Application.companyName + "/" + Application.productName, "Varonia");
            string pkg = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
            if (string.IsNullOrEmpty(pkg)) pkg = Application.identifier;

            _files = new List<PushFile>
            {
                new PushFile {
                    Label  = "GlobalConfig.json",
                    Host   = Path.Combine(varonia, "GlobalConfig.json"),
                    Device = "/storage/emulated/0/Varonia/GlobalConfig.json",
                },
                new PushFile {
                    Label  = "NewSpatial.json",
                    Host   = Path.Combine(varonia, "NewSpatial.json"),
                    Device = "/storage/emulated/0/Varonia/NewSpatial.json",
                },
                new PushFile {
                    Label  = "Config.json  (GameConfig)",
                    Host   = Path.Combine(Application.persistentDataPath, "Config.json"),
                    Device = $"/storage/emulated/0/Android/data/{pkg}/files/Config.json",
                },
            };
            foreach (var f in _files) f.Selected = f.Available;
        }

        // ─── GUI ──────────────────────────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_styles && _cardTex != null) return;
            _styles = true;
            _cardTex = MakeTex(ColCard);
            _cardStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10), margin = new RectOffset(0, 0, 4, 4), normal = { background = _cardTex } };
            _header  = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, normal = { textColor = ColText } };
            _section = new GUIStyle(EditorStyles.boldLabel) { fontSize = 10, normal = { textColor = ColMuted } };
            _path    = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = ColMuted } };
        }

        static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); t.hideFlags = HideFlags.HideAndDontSave; return t;
        }

        private void OnGUI()
        {
            EnsureStyles();
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), ColBg);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            GUILayout.Space(6);
            GUILayout.Label("PUSH FILES → ANDROID DEVICE (ADB)", _header);
            GUILayout.Label($"{Application.productName} · {Application.companyName}", _path);
            GUILayout.Space(6);

            // ── Devices ──
            DrawCard("DEVICES ADB", ColAccent, () =>
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(_devices.Count > 0 ? $"{_devices.Count} device(s)" : "Aucun device", _section);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("↻ Refresh", GUILayout.Width(80))) RefreshAll();
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(4);

                if (!string.IsNullOrEmpty(_adbError))
                {
                    EditorGUILayout.HelpBox("adb : " + _adbError + "\n(adb doit être dans le PATH système)", MessageType.Error);
                }
                else if (_devices.Count == 0)
                {
                    EditorGUILayout.HelpBox("Branche le casque en USB (debug autorisé) ou 'adb connect <ip>', puis Refresh.", MessageType.Warning);
                }
                else
                {
                    for (int i = 0; i < _devices.Count; i++)
                    {
                        bool isSel = !_allDevices && _deviceIdx == i;
                        bool now = EditorGUILayout.ToggleLeft(_devices[i].ToString(), isSel);
                        if (now && !isSel) { _deviceIdx = i; _allDevices = false; }
                    }
                    GUILayout.Space(2);
                    _allDevices = EditorGUILayout.ToggleLeft("Tous les devices", _allDevices);
                }
            });

            // ── Fichiers ──
            DrawCard("FICHIERS À COPIER", ColAccent, () =>
            {
                foreach (var f in _files)
                {
                    using (new EditorGUI.DisabledScope(!f.Available))
                    {
                        EditorGUILayout.BeginHorizontal();
                        f.Selected = EditorGUILayout.Toggle(f.Selected && f.Available, GUILayout.Width(18));
                        GUILayout.Label(f.Label, new GUIStyle(EditorStyles.label) { normal = { textColor = f.Available ? ColText : ColMuted } });
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(f.Available ? "✓ présent" : "✗ absent",
                            new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = f.Available ? ColAccent : ColError } });
                        EditorGUILayout.EndHorizontal();
                        GUILayout.Label("→ " + f.Device, _path);
                    }
                    GUILayout.Space(4);
                }
            });

            GUILayout.Space(4);

            // ── Bouton push ──
            int selCount     = _files.Count(f => f.Selected && f.Available);
            bool hasTarget   = _devices.Count > 0;
            using (new EditorGUI.DisabledScope(!(hasTarget && selCount > 0)))
            {
                var pushStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = 40 };
                string target = _allDevices ? "tous les devices" : (_devices.Count > 0 ? _devices[Mathf.Clamp(_deviceIdx, 0, _devices.Count - 1)].Model : "—");
                if (GUILayout.Button($"PUSH  ({selCount} fichier(s) → {target})", pushStyle))
                    PushSelected();
            }

            GUILayout.Space(6);
            EditorGUILayout.EndScrollView();
        }

        private void DrawCard(string title, Color accent, Action body)
        {
            Rect r = EditorGUILayout.BeginVertical(_cardStyle);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2f), accent);
            GUILayout.Space(2);
            GUILayout.Label(title, _section);
            GUILayout.Space(2);
            body();
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
        }

        // ─── Push ────────────────────────────────────────────────────────────────

        private void PushSelected()
        {
            var targets = _allDevices ? _devices : new List<DeviceInfo> { _devices[Mathf.Clamp(_deviceIdx, 0, _devices.Count - 1)] };
            var sel = _files.Where(f => f.Selected && f.Available).ToList();

            int ok = 0, fail = 0;
            foreach (var dev in targets)
                foreach (var f in sel)
                {
                    string dir = f.Device.Substring(0, f.Device.LastIndexOf('/'));
                    RunAdb(dev.Serial, $"shell mkdir -p \"{dir}\"", out _, out _);
                    int r = RunAdb(dev.Serial, $"push \"{f.Host}\" \"{f.Device}\"", out string o, out string e);
                    if (r == 0) { ok++; Debug.Log($"[PushConfig] OK [{dev}] {f.Label} → {f.Device}\n{o}"); }
                    else        { fail++; Debug.LogError($"[PushConfig] FAIL [{dev}] {f.Label} (code {r}) : {e}\n{o}"); }
                }

            EditorUtility.DisplayDialog("Push Config",
                $"Terminé.\n\n{ok} OK / {fail} échec(s).\nVoir la Console pour le détail.", "OK");
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
                if (string.IsNullOrEmpty(line) || line.StartsWith("List of devices") || line.StartsWith("*")) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[1] != "device") continue;
                string model = "";
                for (int i = 2; i < parts.Length; i++)
                    if (parts[i].StartsWith("model:")) { model = parts[i].Substring("model:".Length); break; }
                list.Add(new DeviceInfo { Serial = parts[0], Model = model });
            }
            return list;
        }

        private static int RunAdb(string serial, string args, out string stdOut, out string stdErr)
        {
            string fullArgs = string.IsNullOrEmpty(serial) ? args : ("-s " + serial + " " + args);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "adb", Arguments = fullArgs,
                    UseShellExecute = false, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) { stdOut = ""; stdErr = "Process.Start returned null"; return -1; }
                    stdOut = p.StandardOutput.ReadToEnd();
                    stdErr = p.StandardError.ReadToEnd();
                    p.WaitForExit(20000);
                    return p.ExitCode;
                }
            }
            catch (Exception e)
            {
                stdOut = ""; stdErr = "adb introuvable ou erreur lancement : " + e.Message;
                return -1;
            }
        }
    }
}
