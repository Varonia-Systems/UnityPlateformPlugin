using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VaroniaBackOffice
{
    /// <summary>
    /// Console overlay : F12 cycle Masqué → Errors → Warnings → Infos → Tout → Tagged.
    /// Lit player.log (build) ou Editor.log (éditeur) de façon asynchrone.
    /// Rendu via OnGUI — aucun Canvas, aucun prefab requis.
    /// </summary>
    public class VaroniaConsoleDisplay : MonoBehaviour
    {
        // ─── Enums ────────────────────────────────────────────────────────────────

        public enum DisplayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

        public enum ConsoleMode
        {
            Hidden   = 0,
            Errors   = 1,
            Warnings = 2,
            Infos    = 3,
            All      = 4,
            Tagged   = 5,   // logs commençant par '#'
        }

        // ─── Config ───────────────────────────────────────────────────────────────

        [Header("Display")]
        [SerializeField] private DisplayCorner corner   = DisplayCorner.BottomLeft;
        [SerializeField] private float         margin   = 12f;
        [SerializeField] private Vector2       size     = new Vector2(680f, 420f);
        [Tooltip("Taille de la police des entrées de log. Tout le layout s'adapte automatiquement.")]
        [SerializeField] private int           fontSize = 11;

        [Header("Filters (Inspector)")]
        [SerializeField] private bool   showErrors      = true;
        [SerializeField] private bool   showWarnings    = true;
        [SerializeField] private bool   showInfos       = true;
        [SerializeField] private int    maxEntries      = 200;

        [Header("Log Refresh")]
        [SerializeField] private float logRefreshInterval = 1.5f;

        /// <summary>Facteur d'échelle manuel (1 = 1080p).</summary>
        [Header("UI Scale")]
        public float scaleFactor = 1f;

        // ─── Colors (même palette que VaroniaFPSDisplay) ─────────────────────────

        static readonly Color ColBg      = new Color(0.11f, 0.11f, 0.14f, 0.92f);
        static readonly Color ColRowAlt  = new Color(1f,    1f,    1f,    0.03f);
        static readonly Color ColError   = new Color(1.00f, 0.40f, 0.40f, 1f);
        static readonly Color ColWarn    = new Color(1.00f, 0.75f, 0.30f, 1f);
        // Blanc cassé proche de la console Unity (pas vert)
        static readonly Color ColInfo    = new Color(0.85f, 0.85f, 0.88f, 1f);
        static readonly Color ColMuted   = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color ColValue   = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color ColDivider = new Color(1f,    1f,    1f,    0.06f);
        static readonly Color ColHeader  = new Color(0.16f, 0.16f, 0.20f, 1.00f);
        static readonly Color ColPopupBg = new Color(0.10f, 0.10f, 0.13f, 0.97f);

        // ─── State ────────────────────────────────────────────────────────────────

        private ConsoleMode _mode = ConsoleMode.Hidden;

        private readonly List<LogEntry> _entries  = new List<LogEntry>();
        private readonly List<LogEntry> _filtered = new List<LogEntry>();
        private readonly object         _lock     = new object();

        // Lecture asynchrone
        private Thread        _readerThread;
        private volatile bool _threadRunning;
        private volatile bool _dirty;
        private string        _logPath;
        private long          _lastFilePos;

        // Scroll
        private Vector2 _scroll;
        private bool    _autoScroll = true;

        // Double-clic
        private int    _lastClickIndex  = -1;
#if UNITY_2020_1_OR_NEWER
        private double _lastClickTime   = -1.0;
        private const double DblClickDelay = 0.35;
#else
        private float  _lastClickTime   = -1.0f;
        private const float DblClickDelay = 0.35f;
#endif

        // Popup détail
        private bool     _popupOpen;
        private string   _popupText;
        private string   _popupStack;
        private Color    _popupColor;
        private DateTime _popupTime;
        private Vector2  _popupScroll;

        // Hover
        private int _hoveredIndex = -1;

        // Cache layout des lignes (hauteur variable selon le texte)
        private string[] _rowContents = new string[0];
        private float[]  _rowOffsets  = new float[0];
        private float[]  _rowHeights  = new float[0];

        // ── Command line ──
        private string _cmdInput = "";
        private readonly List<string> _cmdHistory = new List<string>();
        private int _cmdHistoryIndex = -1;
        private bool _focusCmd;
        private readonly Dictionary<string, Action<string[]>> _commands = new Dictionary<string, Action<string[]>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _cmdHelp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _suggestions = new List<string>();
        private const string CmdControlName = "VaroniaCmdInput";

        // F12 maintenu
        private float _f12HoldTime;
        private const float F12HoldThreshold = 1.0f;
        private bool  _f12HoldFired;

        // Badge erreurs
        private int       _errorCount;
        private Texture2D _badgeBgTex;

        // Styles (reconstruits si fontSize change)
        private bool      _stylesBuilt;
        private float     _lastScale = 1f;
        private int       _builtFontSize = -1;
        private GUIStyle  _headerStyle;
        private GUIStyle  _modeStyle;
        private GUIStyle  _entryStyle;
        private GUIStyle  _btnStyle;
        private GUIStyle  _popupStyle;
        private GUIStyle  _popupStackStyle;
        private GUIStyle  _popupTitleStyle;
        private GUIStyle  _cmdStyle;
        private Texture2D _bgTex;
        private Texture2D _headerTex;
        private Texture2D _accentTex;
        private Texture2D _dividerTex;
        private Texture2D _errorTex;
        private Texture2D _warnTex;
        private Texture2D _infoTex;
        private Texture2D _rowAltTex;
        private Texture2D _popupBgTex;
        private Texture2D _popupHeaderTex;
        private Texture2D _rowHoverTex;

        // ─────────────────────────────────────────────────────────────────────────

        private struct LogEntry
        {
            public LogType Type;
            public string  Text;
            public string  StackTrace;
            public DateTime Time;
        }

        // ─── Early capture (avant tout MonoBehaviour) ─────────────────────────────

        private static readonly List<LogEntry> _earlyEntries = new List<LogEntry>();
        private static bool _earlyCapturing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void EarlyInit()
        {
            _earlyEntries.Clear();
            _earlyCapturing = true;
            Application.logMessageReceivedThreaded += OnEarlyLog;
        }

        private static void OnEarlyLog(string condition, string stackTrace, LogType type)
        {
            if (!_earlyCapturing) return;
            lock (_earlyEntries)
                _earlyEntries.Add(new LogEntry { Type = type, Text = condition, StackTrace = stackTrace, Time = DateTime.Now });
        }

        // ─────────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _logPath = GetLogPath();
        }

        private void OnEnable()
        {
            RegisterCommandsOnce();

            // Stopper la capture statique et fusionner les entrées précoces
            _earlyCapturing = false;
            Application.logMessageReceivedThreaded -= OnEarlyLog;

            lock (_earlyEntries)
            {
                foreach (var e in _earlyEntries)
                {
                    _entries.Add(e);
                }
                _earlyEntries.Clear();
            }
            _dirty = true;

            Application.logMessageReceivedThreaded += OnLogReceived;
            StartReaderThread();
        }

        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLogReceived;
            StopReaderThread();
        }

        private void OnDestroy()
        {
            StopReaderThread();
            DestroyTextures();
        }

        // ─── Runtime log capture ──────────────────────────────────────────────────

        private void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            var entry = new LogEntry { Type = type, Text = condition, StackTrace = stackTrace, Time = DateTime.Now };
            lock (_lock)
            {
                _entries.Add(entry);
                if (_entries.Count > maxEntries)
                    _entries.RemoveAt(0);
                _dirty = true;
            }
        }

        // ─── Async file reader ────────────────────────────────────────────────────

        private void StartReaderThread()
        {
            _threadRunning = true;
            _lastFilePos   = 0;
            _readerThread  = new Thread(ReaderLoop) { IsBackground = true, Name = "VaroniaLogReader" };
            _readerThread.Start();
        }

        private void StopReaderThread()
        {
            _threadRunning = false;
            _readerThread?.Join(500);
            _readerThread = null;
        }

        private void ReaderLoop()
        {
            while (_threadRunning)
            {
                Thread.Sleep((int)(logRefreshInterval * 1000));

                if (string.IsNullOrEmpty(_logPath) || !File.Exists(_logPath))
                    continue;

                try
                {
                    using (var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length < _lastFilePos) _lastFilePos = 0;
                        if (fs.Length == _lastFilePos) continue;

                        fs.Seek(_lastFilePos, SeekOrigin.Begin);

                        var sb = new StringBuilder();
                        using (var sr = new StreamReader(fs, Encoding.UTF8, false, 4096, true))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                                sb.AppendLine(line);
                        }

                        _lastFilePos = fs.Position;
                        if (sb.Length == 0) continue;

                        var newEntries = ParseLogLines(sb.ToString());
                        if (newEntries.Count == 0) continue;

                        lock (_lock)
                        {
                            foreach (var e in newEntries)
                            {
                                _entries.Add(e);
                                if (_entries.Count > maxEntries)
                                    _entries.RemoveAt(0);
                            }
                            _dirty = true;
                        }
                    }
                }
                catch { }
            }
        }

        private static List<LogEntry> ParseLogLines(string raw)
        {
            var result = new List<LogEntry>();
            foreach (var line in raw.Split('\n'))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;

                LogType type;
                if (t.StartsWith("[Error]", StringComparison.OrdinalIgnoreCase) || t.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                    type = LogType.Error;
                else if (t.StartsWith("[Warning]", StringComparison.OrdinalIgnoreCase) || t.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
                    type = LogType.Warning;
                else
                    type = LogType.Log;

                result.Add(new LogEntry { Type = type, Text = t, StackTrace = string.Empty, Time = DateTime.Now });
            }
            return result;
        }

        // ─── Update ───────────────────────────────────────────────────────────────

        private void Update()
        {
            // ── F12 : appui court = cycle mode, maintenu = Debug.Log test ──
            bool f12Down = IsF12Down();
            if (f12Down)
            {
                _f12HoldTime += Time.unscaledDeltaTime;
                if (_f12HoldTime >= F12HoldThreshold && !_f12HoldFired)
                {
                    _f12HoldFired = true;
                    Debug.Log("[VaroniaConsole] Test log — " + DateTime.Now.ToString("HH:mm:ss"));
                    Debug.LogWarning("[VaroniaConsole] Test warning — " + DateTime.Now.ToString("HH:mm:ss"));
                    Debug.LogError("[VaroniaConsole] Test error — " + DateTime.Now.ToString("HH:mm:ss"));
                }
            }
            else
            {
                if (_f12HoldTime > 0f && _f12HoldTime < F12HoldThreshold && !_f12HoldFired)
                {
                    // Appui court relâché → cycle
                    _mode  = (ConsoleMode)(((int)_mode + 1) % 6);
                    _dirty = true;
                }
                _f12HoldTime  = 0f;
                _f12HoldFired = false;
            }

            if (_dirty)
            {
                _dirty = false;
                RebuildFiltered();
                if (_autoScroll)
                    _scroll.y = float.MaxValue;
            }
        }

        // Retourne true tant que F12 est maintenu
        private static bool IsF12Down()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null) return kb.f12Key.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.F12);
#else
            return false;
#endif
        }

        // ─── Command system ─────────────────────────────────────────────────────────

        private void RegisterCommandsOnce()
        {
            if (_commands.Count > 0) return;

            Register("--globalconfig", "Affiche les valeurs de GlobalConfig", _ =>
            {
                var bo = BackOfficeVaronia.Instance;
                if (bo == null || bo.config == null) { Debug.LogWarning("[cmd] GlobalConfig indisponible."); return; }
                Debug.Log("# GlobalConfig :\n" + bo.config.ToJson());
            });

#if GAME_CONFIG
            Register("--gameconfig", "Affiche les valeurs de GameConfig", _ =>
            {
                var bo = BackOfficeVaronia.Instance;
                if (bo == null || bo.gameConfig == null) { Debug.LogWarning("[cmd] GameConfig indisponible."); return; }
                Debug.Log("# GameConfig :\n" +
                    Newtonsoft.Json.JsonConvert.SerializeObject(bo.gameConfig, Newtonsoft.Json.Formatting.Indented));
            });
#endif

            Register("--help", "Liste les commandes disponibles", _ =>
            {
                var sb = new StringBuilder("# Commandes :\n");
                foreach (var kv in _cmdHelp) sb.AppendLine($"  • {kv.Key} — {kv.Value}");
                Debug.Log(sb.ToString());
            });

            Register("--clear", "Vide la console", _ =>
            {
                lock (_lock) _entries.Clear();
                _dirty = true;
            });
        }

        private void Register(string name, string help, Action<string[]> handler)
        {
            _commands[name] = handler;
            _cmdHelp[name]  = help;
        }

        private void ExecuteCommand(string raw)
        {
            string line = (raw ?? "").Trim();
            if (line.Length == 0) return;

            _cmdHistory.Add(line);
            _cmdHistoryIndex = _cmdHistory.Count;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string name = parts[0];
            var args = new string[Mathf.Max(0, parts.Length - 1)];
            if (args.Length > 0) Array.Copy(parts, 1, args, 0, args.Length);

            if (_commands.TryGetValue(name, out var handler))
            {
                try { handler(args); }
                catch (Exception e) { Debug.LogError($"[cmd] '{name}' erreur : {e.Message}"); }
            }
            else
            {
                Debug.LogWarning($"[cmd] commande inconnue : '{name}'  (tape '--help')");
            }

            _cmdInput = "";
        }

        // Met à jour la liste des suggestions selon le préfixe tapé (tant qu'on est sur le nom).
        private void ComputeSuggestions()
        {
            _suggestions.Clear();
            string prefix = (_cmdInput ?? "").TrimStart();
            if (prefix.Length == 0 || prefix.Contains(" ")) return; // on ne complète que le nom
            foreach (var name in _commands.Keys)
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    _suggestions.Add(name);
            _suggestions.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void AutoComplete()
        {
            if (_suggestions.Count == 0) return;
            _cmdInput = _suggestions[0] + " ";
            _focusCmd = true;
        }

        private void HistoryNav(int dir)
        {
            if (_cmdHistory.Count == 0) return;
            _cmdHistoryIndex = Mathf.Clamp(_cmdHistoryIndex + dir, 0, _cmdHistory.Count);
            _cmdInput = _cmdHistoryIndex < _cmdHistory.Count ? _cmdHistory[_cmdHistoryIndex] : "";
            _focusCmd = true;
        }

        private void RebuildFiltered()
        {
            _filtered.Clear();
            int errCount = 0;
            lock (_lock)
            {
                foreach (var e in _entries)
                {
                    if (PassesFilterBase(e))
                    {
                        if (e.Type == LogType.Error || e.Type == LogType.Exception || e.Type == LogType.Assert)
                            errCount++;
                    }
                    if (PassesFilter(e)) _filtered.Add(e);
                }
            }
            _errorCount = errCount;
        }

        // Filtre de base : type + excludeContains (sans filtre de mode)
        private bool PassesFilterBase(LogEntry e)
        {
            if (e.Type == LogType.Error || e.Type == LogType.Exception || e.Type == LogType.Assert)
            { if (!showErrors)   return false; }
            else if (e.Type == LogType.Warning)
            { if (!showWarnings) return false; }
            else
            { if (!showInfos)    return false; }

            var rs = VaroniaRuntimeSettings_Helper.Get();
            if (rs != null && rs.consoleExcludeFilters != null)
            {
                foreach (var f in rs.consoleExcludeFilters)
                {
                    if (!string.IsNullOrEmpty(f) && e.Text != null &&
                        e.Text.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                        return false;
                }
            }
            return true;
        }

        private bool PassesFilter(LogEntry e)
        {
            if (e.Type == LogType.Error || e.Type == LogType.Exception || e.Type == LogType.Assert)
            { if (!showErrors)   return false; }
            else if (e.Type == LogType.Warning)
            { if (!showWarnings) return false; }
            else
            { if (!showInfos)    return false; }

            // Filtres depuis VaroniaRuntimeSettings
            var rs = VaroniaRuntimeSettings_Helper.Get();
            if (rs != null && rs.consoleExcludeFilters != null)
            {
                foreach (var f in rs.consoleExcludeFilters)
                {
                    if (!string.IsNullOrEmpty(f) && e.Text != null &&
                        e.Text.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                        return false;
                }
            }

            switch (_mode)
            {
                case ConsoleMode.Hidden:   return false;
                case ConsoleMode.Errors:   return e.Type == LogType.Error || e.Type == LogType.Exception || e.Type == LogType.Assert;
                case ConsoleMode.Warnings: return e.Type == LogType.Warning;
                case ConsoleMode.Infos:    return e.Type == LogType.Log;
                case ConsoleMode.Tagged:   return e.Text != null && e.Text.TrimStart().StartsWith("#");
                default:                   return true;
            }
        }

        // ─── Rendering ────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            // ── Badge erreurs quand console cachée ──
            if (_mode == ConsoleMode.Hidden)
            {
                if (_errorCount > 0)
                    DrawErrorBadge();
                return;
            }

            float scale = (Screen.height / 1080f) * scaleFactor;
            EnsureStyles(scale);

            // Dimensions dynamiques selon fontSize
            float sFontSize = fontSize * scale;
            float lineH   = sFontSize + 8f * scale;
            float headerH = sFontSize + 16f * scale;
            float dotSize = Mathf.Max(6f * scale, sFontSize * 0.55f);
            float dotX    = 4f * scale;
            float textX   = dotX + dotSize + 4f * scale;

            Rect panel = GetPanelRect(scale);

            // ── Background ──
            GUI.DrawTexture(panel, _bgTex);
            GUI.DrawTexture(new Rect(panel.x, panel.y, 3f * scale, panel.height), _accentTex);

            // ── Header ──
            Rect headerR = new Rect(panel.x + 3f * scale, panel.y, panel.width - 3f * scale, headerH);
            GUI.DrawTexture(headerR, _headerTex);

            string modeLabel = _mode.ToString().ToUpper();
            GUI.Label(new Rect(panel.x + 14f * scale, panel.y + 2f * scale, 300f * scale, headerH), "CONSOLE  ·  " + modeLabel, _headerStyle);

            // Bouton auto-scroll
            float btnW = 60f * scale, btnH = headerH - 6f * scale;
            string asBtnLabel = _autoScroll ? "↓ AUTO" : "↓ OFF";
            if (GUI.Button(new Rect(panel.x + panel.width - btnW - 4f * scale, panel.y + 3f * scale, btnW, btnH), asBtnLabel, _btnStyle))
                _autoScroll = !_autoScroll;

            // ── Divider ──
            float divY = panel.y + headerH;
            GUI.DrawTexture(new Rect(panel.x + 8f * scale, divY, panel.width - 16f * scale, 1f * scale), _dividerTex);

            // ── Barre de commande réservée en bas ──
            float inputH = sFontSize + 14f * scale;

            // ── Scroll view ──
            float bodyY = divY + 2f * scale;
            float bodyH = panel.height - headerH - 2f * scale - inputH;
            Rect  bodyR = new Rect(panel.x + 4f * scale, bodyY, panel.width - 8f * scale, bodyH);

            // Largeur de texte dispo (pour le word-wrap / calcul de hauteur)
            float textW = (bodyR.width - 4f * scale) - textX - 4f * scale;
            if (textW < 20f * scale) textW = 20f * scale;

            // Contenu + hauteur de chaque ligne (variable selon la longueur du texte).
            int count = _filtered.Count;
            if (_rowContents.Length < count) { _rowContents = new string[count]; _rowOffsets = new float[count]; _rowHeights = new float[count]; }
            float totalH = 0f;
            for (int i = 0; i < count; i++)
            {
                var entry = _filtered[i];
                string timePrefix = entry.Time != default ? entry.Time.ToString("HH:mm:ss") + "  " : "";
                _rowContents[i] = timePrefix + (entry.Text ?? "");
                float h = _entryStyle.CalcHeight(new GUIContent(_rowContents[i]), textW);
                h = Mathf.Max(h, lineH) + 4f * scale;
                _rowOffsets[i] = totalH;
                _rowHeights[i] = h;
                totalH += h;
            }

            Rect viewRect = new Rect(0, 0, bodyR.width - 4f * scale, Mathf.Max(totalH, bodyH));

            _scroll = GUI.BeginScrollView(bodyR, _scroll, viewRect, false, false, GUIStyle.none, GUIStyle.none);

            Event ev = Event.current;

            // Mise à jour de la ligne survolée (fonctionne en MouseMove ET Repaint)
            {
                Vector2 localMouse = ev.mousePosition;
                int hovered = -1;
                for (int i = 0; i < count; i++)
                {
                    Rect r = new Rect(0f, _rowOffsets[i], viewRect.width, _rowHeights[i]);
                    if (r.Contains(localMouse)) { hovered = i; break; }
                }
                if (hovered != _hoveredIndex)
                {
                    _hoveredIndex = hovered;
                    GUI.changed = true;
                }
            }

            for (int i = 0; i < count; i++)
            {
                var   entry = _filtered[i];
                float yLine = _rowOffsets[i];
                float hRow  = _rowHeights[i];
                Rect  lineR = new Rect(0f, yLine, viewRect.width, hRow);

                // Zébrage
                if (i % 2 == 1)
                    GUI.DrawTexture(lineR, _rowAltTex);

                // Surbrillance survol
                if (i == _hoveredIndex)
                    GUI.DrawTexture(lineR, _rowHoverTex);

                // Couleur selon type
                Color col;
                Texture2D dot;
                if (entry.Type == LogType.Error || entry.Type == LogType.Exception || entry.Type == LogType.Assert)
                { col = ColError; dot = _errorTex; }
                else if (entry.Type == LogType.Warning)
                { col = ColWarn;  dot = _warnTex;  }
                else
                { col = ColInfo;  dot = _infoTex;  }

                // Pastille alignée sur la 1ʳᵉ ligne
                float dotY = yLine + (lineH - dotSize) * 0.5f + 2f * scale;
                GUI.DrawTexture(new Rect(dotX, dotY, dotSize, dotSize), dot);

                // Texte multi-ligne (word-wrap) sur toute la hauteur de la ligne.
                _entryStyle.normal.textColor = col;
                GUI.Label(new Rect(textX, yLine + 2f * scale, textW, hRow - 4f * scale), _rowContents[i], _entryStyle);

                // Détection double-clic
                if (ev.type == EventType.MouseDown && lineR.Contains(ev.mousePosition))
                {
#if UNITY_2020_1_OR_NEWER
                    double now = Time.realtimeSinceStartupAsDouble;
#else
                    float now = Time.realtimeSinceStartup;
#endif
                    if (_lastClickIndex == i && (now - _lastClickTime) < DblClickDelay)
                    {
                        // Double-clic → ouvrir popup avec texte + stacktrace
                        _popupText   = entry.Text;
                        _popupStack  = entry.StackTrace;
                        _popupColor  = col;
                        _popupTime   = entry.Time;
                        _popupOpen   = true;
                        _popupScroll = Vector2.zero;
                        ev.Use();
                    }
                    else
                    {
                        _lastClickIndex = i;
                        _lastClickTime  = now;
                    }
                }
            }

            GUI.EndScrollView();

            // ── Barre de commande ──
            DrawCommandBar(panel, scale, inputH);

            // ── Popup détail ──
            if (_popupOpen)
                DrawPopup(panel, scale);
        }

        private void DrawCommandBar(Rect panel, float scale, float inputH)
        {
            float sFontSize = fontSize * scale;
            Event e = Event.current;

            ComputeSuggestions();

            Rect inRow = new Rect(panel.x + 3f * scale, panel.y + panel.height - inputH, panel.width - 3f * scale, inputH);
            GUI.DrawTexture(inRow, _headerTex);

            bool focused = GUI.GetNameOfFocusedControl() == CmdControlName;
            if (focused && _suggestions.Count > 0)
                DrawSuggestions(inRow, scale, sFontSize);

            GUI.Label(new Rect(inRow.x + 8f * scale, inRow.y, 16f * scale, inputH), ">", _headerStyle);

            GUI.SetNextControlName(CmdControlName);
            Rect tf = new Rect(inRow.x + 22f * scale, inRow.y + 3f * scale, inRow.width - 30f * scale, inputH - 6f * scale);
            _cmdInput = GUI.TextField(tf, _cmdInput ?? "", _cmdStyle);

            // Le TextField single-line CONSOMME le KeyDown de Entrée → on valide sur KeyUp.
            bool isCmd = GUI.GetNameOfFocusedControl() == CmdControlName;
            if (isCmd && e.type == EventType.KeyUp &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                ExecuteCommand(_cmdInput);
                GUIUtility.keyboardControl = 0; // resync (vide) le champ au refocus
                _focusCmd = true;
            }
            // Tab / historique : sur KeyDown (non consommés par le champ pour ces touches).
            else if (isCmd && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Tab)        { AutoComplete();  GUIUtility.keyboardControl = 0; e.Use(); }
                else if (e.keyCode == KeyCode.UpArrow)   { HistoryNav(-1); GUIUtility.keyboardControl = 0; e.Use(); }
                else if (e.keyCode == KeyCode.DownArrow) { HistoryNav(+1); GUIUtility.keyboardControl = 0; e.Use(); }
            }

            if (_focusCmd)
            {
                _focusCmd = false;
                GUI.FocusControl(CmdControlName);
            }
        }

        private void DrawSuggestions(Rect inRow, float scale, float sFontSize)
        {
            int   n     = Mathf.Min(_suggestions.Count, 6);
            float itemH = sFontSize + 6f * scale;
            float h     = n * itemH;
            Rect  box   = new Rect(inRow.x + 22f * scale, inRow.y - h - 2f * scale, 240f * scale, h);

            GUI.DrawTexture(box, _popupBgTex);
            _entryStyle.normal.textColor = ColValue;

            for (int i = 0; i < n; i++)
            {
                Rect item = new Rect(box.x, box.y + i * itemH, box.width, itemH);
                if (item.Contains(Event.current.mousePosition))
                    GUI.DrawTexture(item, _rowHoverTex);
                if (GUI.Button(item, "  " + _suggestions[i], _entryStyle))
                {
                    _cmdInput = _suggestions[i] + " ";
                    _focusCmd = true;
                }
            }
        }

        private void DrawErrorBadge()
        {
            float scale = (Screen.height / 1080f) * scaleFactor;
            EnsureStyles(scale);
            float sz = 28f * scale;
            float sMargin = margin * scale;
            float x = Screen.width - sz - sMargin;
            float y = sMargin;
            Rect r = new Rect(x, y, sz, sz);

            if (_badgeBgTex == null)
                _badgeBgTex = MakeTex(new Color(0.85f, 0.15f, 0.15f, 0.92f));

            GUI.DrawTexture(r, _badgeBgTex);

            var badgeStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt(Mathf.Max(9, fontSize - 1) * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white },
            };
            GUI.Label(r, _errorCount > 99 ? "99+" : _errorCount.ToString(), badgeStyle);
        }

        private void DrawPopup(Rect anchor, float scale)
        {
            // Fenêtre centrée sur le panel principal
            float pw = Mathf.Min(anchor.width - 40f * scale, 600f * scale);
            float ph = Mathf.Min(anchor.height - 60f * scale, 340f * scale);
            float px = anchor.x + (anchor.width  - pw) * 0.5f;
            float py = anchor.y + (anchor.height - ph) * 0.5f;
            Rect  popR = new Rect(px, py, pw, ph);

            // Fond + bordure accent
            GUI.DrawTexture(popR, _popupBgTex);
            GUI.DrawTexture(new Rect(popR.x, popR.y, 3f * scale, popR.height), _accentTex);

            // Header popup
            float sFontSize = fontSize * scale;
            float popHdrH = sFontSize + 16f * scale;
            Rect  popHdrR = new Rect(popR.x + 3f * scale, popR.y, popR.width - 3f * scale, popHdrH);
            GUI.DrawTexture(popHdrR, _popupHeaderTex);
            string popupTimeStr = _popupTime != default ? "  " + _popupTime.ToString("HH:mm:ss") : "";
            GUI.Label(new Rect(popR.x + 14f * scale, popR.y + 2f * scale, popR.width - 80f * scale, popHdrH), "LOG" + popupTimeStr, _popupTitleStyle);

            // Bouton fermer
            float closeBtnW = Mathf.Max(70f * scale, sFontSize * 6f);
            if (GUI.Button(new Rect(popR.x + popR.width - closeBtnW - 6f * scale, popR.y + 3f * scale, closeBtnW, popHdrH - 6f * scale), "✕ CLOSE", _btnStyle))
                _popupOpen = false;

            // Divider
            float pdivY = popR.y + popHdrH;
            GUI.DrawTexture(new Rect(popR.x + 8f * scale, pdivY, popR.width - 16f * scale, 1f * scale), _dividerTex);

            // Corps scrollable avec le texte complet + stacktrace
            float bodyY = pdivY + 6f * scale;
            float bodyH = popR.height - popHdrH - 8f * scale;
            Rect  bodyR = new Rect(popR.x + 8f * scale, bodyY, popR.width - 16f * scale, bodyH);

            // Contenu complet : message + séparateur + stacktrace
            bool hasStack = !string.IsNullOrWhiteSpace(_popupStack);
            float msgH   = _popupStyle.CalcHeight(new GUIContent(_popupText), bodyR.width - 8f * scale);
            float sepH   = hasStack ? (sFontSize + 8f * scale) : 0f;
            float stkH   = hasStack ? _popupStackStyle.CalcHeight(new GUIContent(_popupStack), bodyR.width - 8f * scale) : 0f;
            float totalContentH = msgH + sepH + stkH + 8f * scale;

            Rect viewRect = new Rect(0, 0, bodyR.width - 8f * scale, Mathf.Max(totalContentH, bodyH));

            _popupScroll = GUI.BeginScrollView(bodyR, _popupScroll, viewRect, false, false, GUIStyle.none, GUIStyle.none);

            // Message principal
            GUI.Label(new Rect(0, 0, viewRect.width, msgH), _popupText, _popupStyle);

            // Stacktrace
            if (hasStack)
            {
                float sepY = msgH + 4f * scale;
                GUI.DrawTexture(new Rect(0, sepY, viewRect.width, 1f * scale), _dividerTex);

                // Label "STACKTRACE"
                GUI.Label(new Rect(0, sepY + 2f * scale, viewRect.width, sFontSize + 4f * scale), "STACKTRACE", _popupTitleStyle);

                float stkY = sepY + 2f * scale + sFontSize + 4f * scale;
                GUI.Label(new Rect(0, stkY, viewRect.width, stkH), _popupStack, _popupStackStyle);
            }

            GUI.EndScrollView();

            // Clic en dehors → fermer
            Event ev = Event.current;
            if (ev.type == EventType.MouseDown && !popR.Contains(ev.mousePosition))
            {
                _popupOpen = false;
                ev.Use();
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private Rect GetPanelRect(float scale)
        {
            float w = size.x * scale, h = size.y * scale;
            float x, y;
            float sMargin = margin * scale;
            switch (corner)
            {
                case DisplayCorner.TopLeft:    x = sMargin;                      y = sMargin;                       break;
                case DisplayCorner.TopRight:   x = Screen.width - w - sMargin;  y = sMargin;                       break;
                case DisplayCorner.BottomLeft: x = sMargin;                      y = Screen.height - h - sMargin;   break;
                default:                       x = Screen.width - w - sMargin;  y = Screen.height - h - sMargin;   break;
            }
            return new Rect(x, y, w, h);
        }

        private static string GetLogPath()
        {
#if UNITY_EDITOR
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Unity", "Editor", "Editor.log");
#else
            return Path.Combine(Application.persistentDataPath, "Player.log");
#endif
        }

        private void EnsureStyles(float scale)
        {
            // Reconstruction si fontSize ou scale a changé en cours de jeu
            if (_stylesBuilt && _builtFontSize == fontSize && Mathf.Approximately(scale, _lastScale)) return;

            if (_stylesBuilt) DestroyTextures();

            _stylesBuilt   = true;
            _builtFontSize = fontSize;
            _lastScale     = scale;

            float sFontSize = fontSize * scale;

            _bgTex        = MakeTex(ColBg);
            _headerTex    = MakeTex(ColHeader);
            _dividerTex   = MakeTex(ColDivider);
            _accentTex    = MakeTex(new Color(0.40f, 0.55f, 0.90f, 1f));  // bleu accent
            _errorTex     = MakeTex(ColError);
            _warnTex      = MakeTex(ColWarn);
            _infoTex      = MakeTex(ColInfo);
            _rowAltTex    = MakeTex(ColRowAlt);
            _popupBgTex   = MakeTex(ColPopupBg);
            _popupHeaderTex = MakeTex(new Color(0.13f, 0.13f, 0.17f, 1f));
            _rowHoverTex  = MakeTex(new Color(1f, 1f, 1f, 0.09f));

            _headerStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt(sFontSize),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColValue },
            };

            _modeStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt(Mathf.Max(9, fontSize - 1) * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColMuted },
            };

            _entryStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt(sFontSize),
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperLeft,   // multi-ligne : démarre en haut
                wordWrap  = true,                   // hauteur variable selon le texte
                clipping  = TextClipping.Clip,
                normal    = { textColor = ColValue },
                padding   = new RectOffset(Mathf.RoundToInt(2 * scale), Mathf.RoundToInt(4 * scale), 0, 0),
                border    = new RectOffset(0, 0, 0, 0),
            };

            _btnStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt(Mathf.Max(9, fontSize - 2) * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = ColMuted, background = MakeTex(new Color(0.2f, 0.2f, 0.25f, 0.9f)) },
                hover     = { textColor = ColValue, background = MakeTex(new Color(0.25f, 0.25f, 0.32f, 0.9f)) },
                border    = new RectOffset(Mathf.RoundToInt(2 * scale), Mathf.RoundToInt(2 * scale), Mathf.RoundToInt(2 * scale), Mathf.RoundToInt(2 * scale)),
                padding   = new RectOffset(Mathf.RoundToInt(4 * scale), Mathf.RoundToInt(4 * scale), Mathf.RoundToInt(2 * scale), Mathf.RoundToInt(2 * scale)),
            };

            _popupStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt((fontSize + 2) * scale),
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperLeft,
                wordWrap  = true,
                clipping  = TextClipping.Clip,
                normal    = { textColor = ColValue },
                padding   = new RectOffset(Mathf.RoundToInt(4 * scale), Mathf.RoundToInt(4 * scale), Mathf.RoundToInt(4 * scale), Mathf.RoundToInt(4 * scale)),
            };

            _popupTitleStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt(sFontSize),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColValue },
            };

            _cmdStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize  = Mathf.RoundToInt(sFontSize),
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColValue },
                focused   = { textColor = ColValue },
            };

            _popupStackStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt(Mathf.Max(9, fontSize) * scale),
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperLeft,
                wordWrap  = true,
                clipping  = TextClipping.Clip,
                normal    = { textColor = ColMuted },
                padding   = new RectOffset(Mathf.RoundToInt(4 * scale), Mathf.RoundToInt(4 * scale), Mathf.RoundToInt(2 * scale), Mathf.RoundToInt(2 * scale)),
            };
        }

        private static Texture2D MakeTex(Color col)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, col);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        private void DestroyTextures()
        {
            void D(Texture2D t) { if (t) Destroy(t); }
            D(_bgTex); D(_headerTex); D(_dividerTex); D(_accentTex);
            D(_errorTex); D(_warnTex); D(_infoTex); D(_rowAltTex);
            D(_popupBgTex); D(_popupHeaderTex); D(_rowHoverTex);
            D(_badgeBgTex);
        }
    }

    // Helper statique pour charger VaroniaRuntimeSettings
    internal static class VaroniaRuntimeSettings_Helper
    {
        public static VaroniaRuntimeSettings Get()
        {
            return VaroniaRuntimeSettings.Load();
        }
    }
}
