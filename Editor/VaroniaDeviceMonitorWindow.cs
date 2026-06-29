// Fenetre Editor (UI Toolkit) : monitore le casque connecte en ADB et ses infos.
//   - Identite : model, serial, version OS
//   - Etat : batterie (niveau, temperature, charge), stockage
//   - Reseau : mode de connexion (USB/WiFi), IP
//   - App Varonia installee (versionName)
//
// Les appels adb tournent dans un thread pour ne pas bloquer l'editor ;
// auto-refresh optionnel toutes les 10s.

using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaroniaBackOffice.EditorTools
{
    public class VaroniaDeviceMonitorWindow : EditorWindow
    {
        // ─── Data ────────────────────────────────────────────────────────────────
        private class DeviceData
        {
            public bool   found;
            public string serial, model, connType, androidVer, osBuild, ip;
            public int    batteryLevel = -1;
            public float  batteryTempC = -1;
            public string batteryStatus = "?";
            public float  storageUsedGb = -1, storageTotalGb = -1;
            public int    storagePct = -1;
            public string appVersion;
            public int    metricsHub = -1; // -1 inconnu, 0 off, 1 on
            public string error;
        }

        private volatile bool _fetchRunning;
        private DeviceData _pending;     // resultat pret a appliquer (worker -> UI)
        private bool _autoRefresh = true;

        // ─── UI refs ─────────────────────────────────────────────────────────────
        private Label _headerName, _headerStatus, _lastRefresh;
        private Label _serialV, _modelV, _osV, _androidV, _ipV, _connV, _batStatusV, _batTempV, _appV;
        private ProgressBar _batteryBar, _storageBar;
        private VisualElement _content, _noDevice;
        private Toggle _metricsToggle;
        private string _currentSerial; // serial du device affiche (pour les actions)

        private static readonly Color ColCardBg  = new Color(0.18f, 0.18f, 0.22f, 1f);
        private static readonly Color ColAccent  = new Color(0.30f, 0.85f, 0.65f, 1f);
        private static readonly Color ColWarn    = new Color(1f, 0.75f, 0.30f, 1f);
        private static readonly Color ColMuted   = new Color(0.55f, 0.55f, 0.62f, 1f);

        [MenuItem("Varonia/PICO Device/Device Monitor", priority = 200)]
        public static void ShowWindow()
        {
            var w = GetWindow<VaroniaDeviceMonitorWindow>();
            w.titleContent = new GUIContent("PICO Device");
            w.minSize = new Vector2(330, 480);
        }

        // ─── UI build ────────────────────────────────────────────────────────────

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 8;

            // ── Header ──
            var header = Row(root);
            _headerName = new Label("PICO DEVICE MONITOR");
            _headerName.style.fontSize = 14;
            _headerName.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(_headerName);

            _headerStatus = new Label("—");
            _headerStatus.style.unityTextAlign = TextAnchor.MiddleRight;
            _headerStatus.style.flexGrow = 1;
            _headerStatus.style.color = ColMuted;
            header.Add(_headerStatus);

            // ── Toolbar ──
            var toolbar = Row(root);
            toolbar.style.marginTop = 6;
            var refreshBtn = new Button(Refresh) { text = "↻  Refresh" };
            refreshBtn.style.height = 24;
            toolbar.Add(refreshBtn);
            var autoToggle = new Toggle("Auto (10s)") { value = _autoRefresh };
            autoToggle.RegisterValueChangedCallback(e => _autoRefresh = e.newValue);
            autoToggle.style.marginLeft = 8;
            toolbar.Add(autoToggle);
            _lastRefresh = new Label("");
            _lastRefresh.style.flexGrow = 1;
            _lastRefresh.style.unityTextAlign = TextAnchor.MiddleRight;
            _lastRefresh.style.color = ColMuted;
            _lastRefresh.style.fontSize = 10;
            toolbar.Add(_lastRefresh);

            // ── Message "pas de device" ──
            _noDevice = new VisualElement();
            var ndLabel = new Label("Aucun device ADB connecte.\nBrancher le casque (USB) ou activer l'ADB WiFi.");
            ndLabel.style.color = ColWarn;
            ndLabel.style.marginTop = 14;
            ndLabel.style.whiteSpace = WhiteSpace.Normal;
            _noDevice.Add(ndLabel);
            _noDevice.style.display = DisplayStyle.None;
            root.Add(_noDevice);

            // ── Contenu ──
            _content = new VisualElement();
            root.Add(_content);

            // Card identite
            var idCard = Card(_content, "IDENTITE", ColAccent);
            _modelV   = InfoRow(idCard, "Model");
            _serialV  = InfoRow(idCard, "Serial");
            _osV      = InfoRow(idCard, "OS Build");
            _androidV = InfoRow(idCard, "Android");

            // Card etat
            var statCard = Card(_content, "ETAT", ColAccent);
            _batteryBar = new ProgressBar { title = "Batterie", lowValue = 0, highValue = 100 };
            _batteryBar.style.marginTop = 4;
            statCard.Add(_batteryBar);
            _batStatusV = InfoRow(statCard, "Charge");
            _batTempV   = InfoRow(statCard, "Temp. batterie");
            _storageBar = new ProgressBar { title = "Stockage", lowValue = 0, highValue = 100 };
            _storageBar.style.marginTop = 6;
            statCard.Add(_storageBar);

            // Card reseau
            var netCard = Card(_content, "RESEAU", ColMuted);
            _connV = InfoRow(netCard, "Connexion");
            _ipV   = InfoRow(netCard, "IP WiFi");

            // Card app
            var appCard = Card(_content, "APP VARONIA", ColMuted);
            _appV = InfoRow(appCard, "com.varonia.milsim");

            // Card outils
            var toolsCard = Card(_content, "OUTILS", ColAccent);
            _metricsToggle = new Toggle("Live Metrics (HUD)") { value = false };
            _metricsToggle.style.marginTop = 4;
            _metricsToggle.RegisterValueChangedCallback(e => SetMetricsHub(e.newValue));
            toolsCard.Add(_metricsToggle);

            var metricsNote = new Label("Overlay metrics ON/OFF instantane (Developer Hub).");
            metricsNote.style.fontSize = 9;
            metricsNote.style.color = ColMuted;
            metricsNote.style.whiteSpace = WhiteSpace.Normal;
            toolsCard.Add(metricsNote);

            // Boutons app Varonia
            var appButtons = Row(toolsCard);
            appButtons.style.marginTop = 6;
            var launchBtn = new Button(() => AppAction("launch")) { text = "▶ Launch" };
            var stopBtn = new Button(() => AppAction("stop")) { text = "⏹ Stop" };
            var restartBtn = new Button(() => AppAction("restart")) { text = "🔄 Restart" };
            foreach (var b in new[] { launchBtn, stopBtn, restartBtn })
            {
                b.style.flexGrow = 1;
                b.style.height = 22;
                appButtons.Add(b);
            }

            // Build & Run : build l'APK, l'installe sur ce device et lance l'app
            // Install : installe juste le dernier APK buildé (sans build ni lancement)
            var buildRow = Row(toolsCard);
            buildRow.style.marginTop = 6;
            var buildRunBtn = new Button(BuildAndRun) { text = "🛠  BUILD & RUN" };
            var installBtn = new Button(InstallOnly) { text = "📲  INSTALL" };
            foreach (var b in new[] { buildRunBtn, installBtn })
            {
                b.style.flexGrow = 1;
                b.style.height = 26;
                b.style.unityFontStyleAndWeight = FontStyle.Bold;
                b.style.color = ColAccent;
                buildRow.Add(b);
            }

            // Auto-refresh scheduler + application des resultats du worker
            root.schedule.Execute(ApplyPendingIfAny).Every(150);
            root.schedule.Execute(() => { if (_autoRefresh && !_fetchRunning) Refresh(); }).Every(10000);

            Refresh();
        }

        // ─── Helpers UI ──────────────────────────────────────────────────────────

        private static VisualElement Row(VisualElement parent)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            parent.Add(r);
            return r;
        }

        private static VisualElement Card(VisualElement parent, string title, Color accent)
        {
            var card = new VisualElement();
            card.style.backgroundColor = ColCardBg;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = accent;
            card.style.marginTop = 8;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 8;
            card.style.paddingTop = 6;
            card.style.paddingBottom = 8;
            card.style.borderTopRightRadius = 4;
            card.style.borderBottomRightRadius = 4;

            var t = new Label(title);
            t.style.fontSize = 10;
            t.style.unityFontStyleAndWeight = FontStyle.Bold;
            t.style.color = ColMuted;
            card.Add(t);

            parent.Add(card);
            return card;
        }

        private static Label InfoRow(VisualElement card, string label)
        {
            var row = Row(card);
            row.style.marginTop = 3;
            var l = new Label(label);
            l.style.color = ColMuted;
            l.style.width = 120;
            l.style.fontSize = 11;
            row.Add(l);
            var v = new Label("—");
            v.style.flexGrow = 1;
            v.style.fontSize = 11;
            v.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(v);
            return v;
        }

        // ─── Refresh (worker thread) ─────────────────────────────────────────────

        private void Refresh()
        {
            if (_fetchRunning) return;
            _fetchRunning = true;
            _headerStatus.text = "refresh...";
            Task.Run(() =>
            {
                var d = new DeviceData();
                try { Fetch(d); }
                catch (Exception e) { d.error = e.Message; }
                _pending = d;
                _fetchRunning = false;
            });
        }

        private void ApplyPendingIfAny()
        {
            var d = _pending;
            if (d == null) return;
            _pending = null;

            _lastRefresh.text = DateTime.Now.ToString("HH:mm:ss");

            if (!d.found)
            {
                _headerStatus.text = "déconnecté";
                _headerStatus.style.color = ColWarn;
                _noDevice.style.display = DisplayStyle.Flex;
                _content.style.display = DisplayStyle.None;
                return;
            }

            _noDevice.style.display = DisplayStyle.None;
            _content.style.display = DisplayStyle.Flex;
            _headerStatus.text = "● connecté";
            _headerStatus.style.color = ColAccent;

            _modelV.text   = d.model ?? "—";
            _serialV.text  = d.serial ?? "—";
            _osV.text      = string.IsNullOrEmpty(d.osBuild) ? "—" : d.osBuild;
            _androidV.text = string.IsNullOrEmpty(d.androidVer) ? "—" : d.androidVer;

            if (d.batteryLevel >= 0)
            {
                _batteryBar.value = d.batteryLevel;
                _batteryBar.title = $"Batterie  {d.batteryLevel}%";
            }
            _batStatusV.text = d.batteryStatus;
            _batTempV.text   = d.batteryTempC >= 0 ? $"{d.batteryTempC:F1} °C" : "—";

            if (d.storagePct >= 0)
            {
                _storageBar.value = d.storagePct;
                _storageBar.title = $"Stockage  {d.storageUsedGb:F1} / {d.storageTotalGb:F1} GB  ({d.storagePct}%)";
            }

            _connV.text = d.connType ?? "—";
            _ipV.text   = string.IsNullOrEmpty(d.ip) ? "—" : d.ip;
            _appV.text  = string.IsNullOrEmpty(d.appVersion) ? "(non installee)" : d.appVersion;

            _currentSerial = d.serial;
            _metricsToggle.SetEnabled(d.metricsHub >= 0);
            _metricsToggle.SetValueWithoutNotify(d.metricsHub == 1);
        }

        private const string HubService = "com.pico.developerhubservice/.HubService";

        /// <summary>Active/desactive l'overlay Live Metrics du casque (Swan / PICO OS 6).
        /// Sequence verifiee :
        ///   1. Lancer l'app metrics via l'action `hub.settings.on` (la MetricsSettingActivity n'est
        ///      pas exportee, ce service exporte est la seule porte d'entree ADB).
        ///   2. Puis activer/desactiver l'overlay via `hub.on` / `hub.off` (+ cle key_metrics_hub).</summary>
        private void SetMetricsHub(bool enable)
        {
            var serial = _currentSerial;
            if (string.IsNullOrEmpty(serial)) return;
            Task.Run(() =>
            {
                // 1. Lancer l'app metrics
                RunAdb($"-s {serial} shell am startservice -n {HubService} -a com.pico.developer.hub.settings.on", out _, out _, 8000);
                System.Threading.Thread.Sleep(800);

                // 2. Etat de l'overlay
                RunAdb($"-s {serial} shell settings put global key_metrics_hub {(enable ? 1 : 0)}", out _, out _, 8000);
                RunAdb($"-s {serial} shell am startservice -n {HubService} -a com.pico.developer.hub.{(enable ? "on" : "off")}", out _, out _, 8000);
            });
            UnityEngine.Debug.Log($"[VaroniaDeviceMonitor] Live Metrics : app lancee puis overlay {(enable ? "ON" : "OFF")} ({serial})");
        }

        /// <summary>Build l'APK puis l'installe et lance l'app sur le device affiché
        /// (delegue a VaroniaBuild : fenetre de progression, sons, install ADB).</summary>
        private void BuildAndRun()
        {
            if (string.IsNullOrEmpty(_currentSerial))
            {
                EditorUtility.DisplayDialog("Build & Run", "Aucun device ADB connecté.", "OK");
                return;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUtility.DisplayDialog("Build & Run",
                    "La target active n'est pas Android.\nSwitch via File > Build Profiles avant de lancer.", "OK");
                return;
            }
            VaroniaBuild.BuildAndRun(_currentSerial);
        }

        /// <summary>Installe le dernier APK buildé sur le device affiché (sans build ni lancement).</summary>
        private void InstallOnly()
        {
            if (string.IsNullOrEmpty(_currentSerial))
            {
                EditorUtility.DisplayDialog("Install APK", "Aucun device ADB connecté.", "OK");
                return;
            }
            VaroniaBuild.InstallOnDevice(_currentSerial);
        }

        private const string VaroniaPackage = "com.varonia.milsim";

        /// <summary>Launch / stop / restart de l'app Varonia sur le device.</summary>
        private void AppAction(string action)
        {
            var serial = _currentSerial;
            if (string.IsNullOrEmpty(serial)) return;
            Task.Run(() =>
            {
                switch (action)
                {
                    case "launch":
                        RunAdb($"-s {serial} shell monkey -p {VaroniaPackage} 1", out _, out _, 10000);
                        break;
                    case "stop":
                        RunAdb($"-s {serial} shell am force-stop {VaroniaPackage}", out _, out _, 10000);
                        break;
                    case "restart":
                        RunAdb($"-s {serial} shell am force-stop {VaroniaPackage}", out _, out _, 10000);
                        System.Threading.Thread.Sleep(1000);
                        RunAdb($"-s {serial} shell monkey -p {VaroniaPackage} 1", out _, out _, 10000);
                        break;
                }
            });
            UnityEngine.Debug.Log($"[VaroniaDeviceMonitor] App {action} -> {VaroniaPackage} ({serial})");
        }

        // ─── Fetch ADB (appele depuis le worker) ─────────────────────────────────

        private static void Fetch(DeviceData d)
        {
            RunAdb("devices -l", out string devicesOut, out _, 8000);
            foreach (var rawLine in (devicesOut ?? "").Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("List of devices") || line.StartsWith("*")) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[1] != "device") continue;

                d.serial = parts[0];
                d.connType = d.serial.Contains(":") ? "WiFi (ADB TCP)" : "USB";
                foreach (var p in parts)
                    if (p.StartsWith("model:")) d.model = p.Substring("model:".Length);
                break;
            }
            if (d.serial == null) { d.found = false; return; }
            d.found = true;

            string s = d.serial;

            // Versions OS
            RunAdb($"-s {s} shell \"getprop ro.build.version.release; getprop ro.build.display.id\"", out string props, out _, 8000);
            var propLines = (props ?? "").Split('\n');
            if (propLines.Length > 0) d.androidVer = propLines[0].Trim();
            if (propLines.Length > 1) d.osBuild = propLines[1].Trim();

            // Batterie
            RunAdb($"-s {s} shell dumpsys battery", out string bat, out _, 8000);
            var mLevel = Regex.Match(bat ?? "", @"level:\s*(\d+)");
            if (mLevel.Success) d.batteryLevel = int.Parse(mLevel.Groups[1].Value);
            var mTemp = Regex.Match(bat ?? "", @"temperature:\s*(\d+)");
            if (mTemp.Success) d.batteryTempC = int.Parse(mTemp.Groups[1].Value) / 10f;
            var mStatus = Regex.Match(bat ?? "", @"status:\s*(\d+)");
            if (mStatus.Success)
            {
                switch (mStatus.Groups[1].Value)
                {
                    case "2": d.batteryStatus = "En charge"; break;
                    case "3": d.batteryStatus = "Sur batterie"; break;
                    case "4": d.batteryStatus = "Pas en charge"; break;
                    case "5": d.batteryStatus = "Pleine"; break;
                    default:  d.batteryStatus = "Inconnu"; break;
                }
            }

            // IP WiFi
            RunAdb($"-s {s} shell ip route", out string route, out _, 8000);
            var mIp = Regex.Match(route ?? "", @"src\s+(\d+\.\d+\.\d+\.\d+)");
            if (mIp.Success) d.ip = mIp.Groups[1].Value;

            // Stockage
            RunAdb($"-s {s} shell df /storage/emulated/0", out string df, out _, 8000);
            foreach (var rawLine in (df ?? "").Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("Filesystem")) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    if (long.TryParse(parts[1], out long totalKb)) d.storageTotalGb = totalKb / 1048576f;
                    if (long.TryParse(parts[2], out long usedKb))  d.storageUsedGb  = usedKb / 1048576f;
                    var mPct = Regex.Match(parts[4], @"(\d+)%");
                    if (mPct.Success) d.storagePct = int.Parse(mPct.Groups[1].Value);
                    break;
                }
            }

            // App Varonia
            RunAdb($"-s {s} shell \"dumpsys package com.varonia.milsim | grep versionName\"", out string pkg, out _, 8000);
            var mVer = Regex.Match(pkg ?? "", @"versionName=(.+)");
            if (mVer.Success) d.appVersion = mVer.Groups[1].Value.Trim();

            // Live Metrics HUD (PICO OS, decouvert sur Swan : settings global key_metrics_hub)
            RunAdb($"-s {s} shell settings get global key_metrics_hub", out string metrics, out _, 8000);
            if (int.TryParse((metrics ?? "").Trim(), out int hub)) d.metricsHub = hub;
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
                stdErr = "adb introuvable : " + e.Message;
                return -1;
            }
        }
    }
}
