using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Valve.VR;

#if STEAMVR_ENABLED
using Valve.VR;
#endif

namespace VaroniaBackOffice
{
    /// <summary>
    /// Graphe de latence ALVR temps réel via WebSocket ws://localhost:8082/api/events.
    /// Se connecte automatiquement au Start. Rendu via OnGUI — aucun Canvas requis.
    /// </summary>
    public class VaroniaLatencyChart : MonoBehaviour
    {
        
#if STEAMVR_ENABLED
        
        // ─── ALVR Statistics ──────────────────────────────────────────────────────

        public class StatisticsSummaryItem
        {
            public double video_packets_total    { get; set; }
            public double video_packets_per_sec  { get; set; }
            public double video_mbytes_total     { get; set; }
            public double video_mbits_per_sec    { get; set; }
            public double total_latency_ms       { get; set; }
            public double network_latency_ms     { get; set; }
            public double encode_latency_ms      { get; set; }
            public double decode_latency_ms      { get; set; }
            public double packets_lost_total     { get; set; }
            public double packets_lost_per_sec   { get; set; }
            public double client_fps             { get; set; }
            public double server_fps             { get; set; }
            public double bitrate_mbps           => video_mbits_per_sec;
            public double battery_hmd            { get; set; }
            public bool   hmd_plugged            { get; set; }
        }

        // ─── Config ───────────────────────────────────────────────────────────────

        public enum DisplayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Display")]
        [SerializeField] private DisplayCorner corner = DisplayCorner.BottomLeft;
        [SerializeField] private float         margin = 12f;
        [SerializeField] private Vector2       size   = new Vector2(340f, 160f);

        [Header("VSVR")]
        [SerializeField] private string wsUrl          = "ws://localhost:8082/api/events";
        [SerializeField] private float  timeoutSeconds = 5f;

        [Header("Chart Display")]
        [SerializeField] private int   maxBars    = 100;
        [SerializeField] private float maxLatency = 200f;
        [SerializeField] private float maxTotalLatency = 200f;

        [Header("Network Latency Thresholds (ms)")]
        [SerializeField] private float orangeThreshold = 100f;
        [SerializeField] private float redThreshold    = 140f;

        [Header("Total Latency Thresholds (ms)")]
        [SerializeField] private float orangeTotalThreshold = 100f;
        [SerializeField] private float redTotalThreshold    = 140f;

        [Header("Encode Latency Thresholds (ms)")]
        [SerializeField] private float orangeEncodeThreshold = 10f;
        [SerializeField] private float redEncodeThreshold    = 20f;

        [Header("Decode Latency Thresholds (ms)")]
        [SerializeField] private float orangeDecodeThreshold = 10f;
        [SerializeField] private float redDecodeThreshold    = 20f;

        // ─── Colors ───────────────────────────────────────────────────────────────

        static readonly Color ColBg      = new Color(0.11f, 0.11f, 0.14f, 0.92f);
        static readonly Color ColGood    = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color ColWarn    = new Color(1.00f, 0.75f, 0.30f, 1f);
        static readonly Color ColBad     = new Color(1.00f, 0.40f, 0.40f, 1f);
        static readonly Color ColMuted   = new Color(0.55f, 0.55f, 0.62f, 0.35f);
        static readonly Color ColMutedFg = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color ColValue   = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color ColDivider = new Color(1f,    1f,    1f,    0.06f);

        // ─── WebSocket state ──────────────────────────────────────────────────────

        private volatile bool                  _needStop;
        private volatile bool                  _wsConnected;
        private StatisticsSummaryItem          _live;          // written from thread, read on main
        private volatile bool                  _hasNewData;
        private readonly List<StatisticsSummaryItem> _avgBuffer = new List<StatisticsSummaryItem>();
        private DateTime                       _lastMessageTime = DateTime.UtcNow;

        // ─── Chart data ───────────────────────────────────────────────────────────
        private float[] _buffer;
        private float[] _totalBuffer;
        private int     _writeIdx;
        private float   _lastValue = -1f;

        private int     _lostStreamCount = 0;
        private bool    _isCurrentlyLost = false;
        private bool    _timeoutEventFired = false;
        private float   _lastTickTime = 0f;

        // ─── Styles ───────────────────────────────────────────────────────────────

        private bool      _stylesBuilt;
        private GUIStyle  _labelStyle;
        private GUIStyle  _valueStyle;
        private GUIStyle  _pillStyle;
        private GUIStyle  _statLabelStyle;
        private GUIStyle  _statValueStyle;
        private Texture2D _texBg, _texGood, _texWarn, _texBad, _texMuted, _texDivider, _texAccent;
        private Texture2D _texPillGood, _texPillBad, _texPillMuted, _texTotalMuted, _texAvgLine;
        private Color     _currentAccent;

        // ─────────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _buffer = new float[maxBars];
            _totalBuffer = new float[maxBars];
            for (int i = 0; i < maxBars; i++)
            {
                _buffer[i] = -1f;
                _totalBuffer[i] = -1f;
            }
        }


        private bool _ready;
        
        private IEnumerator Start()
        {
            yield return new WaitForSeconds(2);
            
            CVRSystem vr = SteamVRBridge.GetSystem();
            if (vr == null) Destroy(this);

            var _headsetName = "";
            var sb = new System.Text.StringBuilder(256);
            ETrackedPropertyError err = ETrackedPropertyError.TrackedProp_Success;
            vr.GetStringTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_ModelNumber_String, sb, 256, ref err);
            _headsetName = sb.Length > 0 ? sb.ToString() : "—";
                
            if(_headsetName != "Miramar" && _headsetName != "Oculus Quest2")
                Destroy(this);
            
                
            StartWebSocket();
            
            
            _ready = true;
            
        }

        private void OnDestroy()
        {
            _needStop = true;

            if (_texBg)       Destroy(_texBg);
            if (_texGood)     Destroy(_texGood);
            if (_texWarn)     Destroy(_texWarn);
            if (_texBad)      Destroy(_texBad);
            if (_texMuted)    Destroy(_texMuted);
            if (_texDivider)  Destroy(_texDivider);
            if (_texAccent)   Destroy(_texAccent);
            if (_texPillGood) Destroy(_texPillGood);
            if (_texPillBad)  Destroy(_texPillBad);
            if (_texPillMuted)Destroy(_texPillMuted);
            if (_texTotalMuted) Destroy(_texTotalMuted);
            if (_texAvgLine)    Destroy(_texAvgLine);
            if (_texGoodMuted)  Destroy(_texGoodMuted);
            if (_texWarnMuted)  Destroy(_texWarnMuted);
            if (_texBadMuted)   Destroy(_texBadMuted);
        }

        // ─── Update — consomme les nouvelles données sur le main thread ───────────

        private void Update()
        {
            if (!_ready) return;

            // Timeout check (no message for > 0.1s after first 5s of uptime)
            if (!_timeoutEventFired && (DateTime.UtcNow - _lastMessageTime).TotalSeconds > 0.1 && Time.time > 5)
            {
                _timeoutEventFired = true;
                OnWebsocketTimeout();
            }

            // Fréquence d'update forcée à 0.1s
            if (Time.time - _lastTickTime >= 0.1f)
            {
                _lastTickTime = Time.time;

                if (_live != null)
                {
                    // Si on a reçu de nouvelles données réelles, on réinitialise le flag timeout
                    if (_hasNewData && _live.network_latency_ms != -1)
                    {
                        _timeoutEventFired = false;
                    }

                    AddLatencyValue((float)_live.network_latency_ms, (float)_live.total_latency_ms);
                    _avgBuffer.Add(_live);
                    if (_avgBuffer.Count > 600) _avgBuffer.RemoveAt(0);
                    
                    _hasNewData = false;
                }
            }
        }

        private void OnWebsocketTimeout()
        {
            Debug.Log("[VSVR] /!\\ Lost Streaming connection /!\\");

            var lostStats = new StatisticsSummaryItem
            {
                total_latency_ms   = -1,
                network_latency_ms = -1,
                encode_latency_ms  = -1,
                decode_latency_ms  = -1
            };

            _live       = lostStats;
            _hasNewData = true; // Forcer la mise à jour pour enregistrer les -1
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>Ajoute une valeur de latence en ms. -1 = pas de donnée.</summary>
        public void AddLatencyValue(float latency, float totalLatency = -1f)
        {
            // Detection de perte de flux (transition vers -1)
            if (latency == -1f)
            {
                if (!_isCurrentlyLost)
                {
                    _lostStreamCount++;
                    _isCurrentlyLost = true;
                }
            }
            else
            {
                // Flux retrouvé
                _isCurrentlyLost = false;
            }

            _buffer[_writeIdx] = latency;
            _totalBuffer[_writeIdx] = totalLatency;
            _writeIdx = (_writeIdx + 1) % maxBars;
            _lastValue = latency;
        }

        public StatisticsSummaryItem GetAverage() => CalculerMoyenne(_avgBuffer);

        // ─── WebSocket ────────────────────────────────────────────────────────────

        private void StartWebSocket()
        {
            _needStop = false;

            Task.Run(async () =>
            {
                using (var ws = new ClientWebSocket())
                {
                    try
                    {
                        ws.Options.SetRequestHeader("X-ALVR", "true");
                        await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
                        _wsConnected = true;
                        _lastMessageTime = DateTime.UtcNow;
                    }
                    catch
                    {
                        _wsConnected = false;
                        return;
                    }

                    var receiveBuffer = new byte[4096];
                    var bufferString  = "";

                    while (ws.State == WebSocketState.Open && !_needStop)
                    {
                        // Timeout check
                        if ((DateTime.UtcNow - _lastMessageTime).TotalSeconds > timeoutSeconds)
                            _wsConnected = false;

                        try
                        {
                            var result = await ws.ReceiveAsync(
                                new ArraySegment<byte>(receiveBuffer), CancellationToken.None);

                            if (result.MessageType == WebSocketMessageType.Text)
                            {
                                _lastMessageTime = DateTime.UtcNow;
                                _wsConnected     = true;

                                bufferString += Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                                var parts = SplitBuffer(bufferString);

                                if (parts.Count > 1)
                                {
                                    try
                                    {
                                        var tData = JObject.Parse(parts[0]);
                                        string evtId = tData["event_type"]?["id"]?.ToString();

                                        if (evtId == "StatisticsSummary")
                                        {
                                            var stats = JsonConvert.DeserializeObject<StatisticsSummaryItem>(
                                                tData["event_type"]["data"].ToString());

                                            _live       = stats;
                                            _hasNewData = true;


                                            if (stats.network_latency_ms > 13)
                                                Debug.Log($"[VSVR] /!\\ Network Latency Alert '{stats.network_latency_ms}' /!\\");

                                        }
                                    }
                                    catch { /* ignore parse errors */ }

                                    bufferString = parts[1];
                                }
                            }
                        }
                        catch { /* socket error — loop will exit on next state check */ }
                    }
                }

                _wsConnected = false;
            });
        }

        private static List<string> SplitBuffer(string input)
        {
            return input
                .Split(new[] { "{\"timestamp\"" }, StringSplitOptions.None)
                .Where(w => !string.IsNullOrEmpty(w))
                .Select(s => "{\"timestamp\"" + s)
                .ToList();
        }

        private static StatisticsSummaryItem CalculerMoyenne(List<StatisticsSummaryItem> valeurs)
        {
            var result = new StatisticsSummaryItem();
            if (valeurs == null || valeurs.Count == 0) return result;

            var validTotal = valeurs.Where(v => v.total_latency_ms != -1).Select(v => v.total_latency_ms).ToList();
            var validNet   = valeurs.Where(v => v.network_latency_ms != -1).Select(v => v.network_latency_ms).ToList();
            var validEnc   = valeurs.Where(v => v.encode_latency_ms != -1).Select(v => v.encode_latency_ms).ToList();
            var validDec   = valeurs.Where(v => v.decode_latency_ms != -1).Select(v => v.decode_latency_ms).ToList();
            var validFps   = valeurs.Where(v => v.client_fps > 0).Select(v => v.client_fps).ToList();
            var validBitrate = valeurs.Where(v => v.video_mbits_per_sec > 0).Select(v => v.video_mbits_per_sec).ToList();

            result.total_latency_ms   = validTotal.Count > 0 ? validTotal.Average() : 0;
            result.network_latency_ms = validNet.Count > 0 ? validNet.Average() : 0;
            result.encode_latency_ms  = validEnc.Count > 0 ? validEnc.Average() : 0;
            result.decode_latency_ms  = validDec.Count > 0 ? validDec.Average() : 0;
            result.client_fps         = validFps.Count > 0 ? validFps.Average() : 0;
            result.video_mbits_per_sec = validBitrate.Count > 0 ? validBitrate.Average() : 0;

            return result;
        }

        // ─── Rendering ────────────────────────────────────────────────────────────


        private bool show;
        
        private void OnEnable()
        {
            BackOfficeVaronia.OnMovieChanged += OnMovieChanged;
        }

        private void OnDisable()
        {
            BackOfficeVaronia.OnMovieChanged -= OnMovieChanged;
        }

        private void OnMovieChanged()
        {
            if (BackOfficeVaronia.Instance != null)
                show = BackOfficeVaronia.Instance.config.hideMode == 0;
        }
        
        
        
        private void OnGUI()
        {
            
            if (!_ready || !show) return;
            
            
            EnsureStyles();

            Rect  panel  = GetPanelRect();
            Color accent = GetColor(_lastValue);

            // ── Background ──
            GUI.DrawTexture(panel, _texBg);

            // ── Left accent bar ──
            if (accent != _currentAccent)
            {
                _currentAccent = accent;
                _texAccent.SetPixel(0, 0, accent);
                _texAccent.Apply();
            }
            GUI.DrawTexture(new Rect(panel.x, panel.y, 3f, panel.height), _texAccent);

            const float HeaderH = 22f;
            const float StatsH  = 22f;
            const float PadH    =  6f;

            // ── Header row ──
            string vsvrLabel = "VSVR";
            if (_live != null && _live.video_mbits_per_sec > 0)
            {
                vsvrLabel += $" ({_live.video_mbits_per_sec:F1} Mbps)";
            }
            
            GUI.Label(
                new Rect(panel.x + 12f, panel.y + 4f, 150f, HeaderH),
                vsvrLabel, _labelStyle
            );

            // Connection pill
            bool connected = _wsConnected;
            string pillTxt = connected ? "● CONNECTED" : "● OFFLINE";
            _pillStyle.normal.textColor  = connected ? ColGood : ColBad;
            _pillStyle.normal.background = connected ? _texPillGood : _texPillBad;
            GUI.Label(
                new Rect(panel.x + panel.width - 110f, panel.y + 5f, 106f, HeaderH - 4f),
                pillTxt, _pillStyle
            );

            // Divider 1
            float div1Y = panel.y + HeaderH;
            GUI.DrawTexture(new Rect(panel.x + 8f, div1Y, panel.width - 16f, 1f), _texDivider);

            // ── Stats row ──
            float statsY = div1Y + 1f;
            if (_live != null)
            {
                DrawStat(panel.x, statsY, StatsH, panel.width,
                    "NET", _live.network_latency_ms,
                    "TOT", _live.total_latency_ms,
                    "ENC", _live.encode_latency_ms,
                    "DEC", _live.decode_latency_ms,
                    "FPS", _live.client_fps,
                    GetColor((float)_live.network_latency_ms),
                    GetTotalColor((float)_live.total_latency_ms, false),
                    GetEncodeColor((float)_live.encode_latency_ms),
                    GetDecodeColor((float)_live.decode_latency_ms),
                    ColValue, u5: ""
                );
            }
            else
            {
                GUI.Label(
                    new Rect(panel.x + 12f, statsY + 4f, panel.width - 16f, StatsH),
                    "En attente de données…", _statLabelStyle
                );
            }

            // Divider 2
            float div2Y = statsY + StatsH;
            GUI.DrawTexture(new Rect(panel.x + 8f, div2Y, panel.width - 16f, 1f), _texDivider);

            // ── Averages row ──
            float avgY = div2Y + 1f;
            if (_avgBuffer.Count > 0)
            {
                var avg = CalculerMoyenne(_avgBuffer);
                DrawStat(panel.x, avgY, StatsH, panel.width,
                    "Avg.NET", avg.network_latency_ms,
                    "Avg.TOT", avg.total_latency_ms,
                    "AVG.ENC", avg.encode_latency_ms,
                    "AVG.DEC", avg.decode_latency_ms,
                    "LOST",    _lostStreamCount,
                    GetColor((float)avg.network_latency_ms),
                    GetTotalColor((float)avg.total_latency_ms, false),
                    GetEncodeColor((float)avg.encode_latency_ms),
                    GetDecodeColor((float)avg.decode_latency_ms),
                    ColValue, u5: ""
                );
            }
            else
            {
                GUI.Label(
                    new Rect(panel.x + 12f, avgY + 4f, panel.width - 16f, StatsH),
                    "Calcul des moyennes…", _statLabelStyle
                );
            }

            // Divider 3
            float div3Y = avgY + StatsH;
            GUI.DrawTexture(new Rect(panel.x + 8f, div3Y, panel.width - 16f, 1f), _texDivider);

            // ── Chart area ──
            float chartX = panel.x + 8f;
            float chartY = div3Y + PadH;
            float chartW = panel.width - 16f;
            float chartH = panel.height - HeaderH - StatsH * 2f - PadH * 2f - 3f;
            float barW   = chartW / maxBars;

            for (int i = 0; i < maxBars; i++)
            {
                int   idx  = (_writeIdx + i) % maxBars;
                
                // 1. Total Latency (muted background)
                float vt   = _totalBuffer[idx];
                if (vt > 0f)
                {
                    float barHT = Mathf.Clamp01(vt / maxTotalLatency) * chartH;
                    GUI.DrawTexture(
                        new Rect(chartX + i * barW, chartY + chartH - barHT, Mathf.Max(barW - 1f, 1f), barHT),
                        GetTotalTex(vt)
                    );
                }
                else if (vt == -1f)
                {
                    // Draw full height gray for -1
                    GUI.DrawTexture(
                        new Rect(chartX + i * barW, chartY, Mathf.Max(barW - 1f, 1f), chartH),
                        _texMuted
                    );
                }

                // 2. Network Latency (foreground)
                float v    = _buffer[idx];
                float barH = v < 0f ? (v == -1f ? chartH : 2f) : Mathf.Clamp01(v / maxLatency) * chartH;
                Texture2D barTex = v == -1f ? _texMuted : GetTex(v);

                GUI.DrawTexture(
                    new Rect(chartX + i * barW, chartY + chartH - barH, Mathf.Max(barW - 1f, 1f), barH),
                    barTex
                );
            }

            // 3. Line for all total data points (connect them)
            // On dessine une ligne continue pour les points de total_latency_ms
            for (int i = 1; i < maxBars; i++)
            {
                int   idxPrev = (_writeIdx + i - 1) % maxBars;
                int   idxCurr = (_writeIdx + i) % maxBars;
                
                float vPrev = _totalBuffer[idxPrev];
                float vCurr = _totalBuffer[idxCurr];
                
                if (vPrev > 0f && vCurr > 0f)
                {
                    float yPrev = chartY + chartH - (Mathf.Clamp01(vPrev / maxTotalLatency) * chartH);
                    float yCurr = chartY + chartH - (Mathf.Clamp01(vCurr / maxTotalLatency) * chartH);
                    
                    // On dessine une ligne entre les deux points. Pour simplifier en OnGUI, on utilise DrawTexture par segments si horizontal.
                    // Mais ici on veut une ligne qui relie les points. Comme barW est petit, on peut juste dessiner des petits segments.
                    GUI.DrawTexture(new Rect(chartX + (i-1) * barW, yCurr, barW, 1f), _texAvgLine);
                }
            }

            // 4. Average Line (last 5 seconds)
            // On prend les ~300 derniers points si possible (pour 5s à 60Hz)
            // Mais ici on a _avgBuffer qui contient les objets StatisticsSummaryItem.
            if (_avgBuffer.Count > 0)
            {
                int countFor5s = 300; 
                var lastItems = _avgBuffer.Skip(Math.Max(0, _avgBuffer.Count - countFor5s)).ToList();
                double avgTotal = lastItems.Average(s => s.total_latency_ms);
                
                float lineY = chartY + chartH - (Mathf.Clamp01((float)avgTotal / maxTotalLatency) * chartH);
                GUI.DrawTexture(new Rect(chartX, lineY, chartW, 1f), _texAvgLine);
                
                // Optionnel: petit label pour la moyenne
                GUI.Label(new Rect(chartX + 2f, lineY - 12f, 50f, 12f), $"avg {avgTotal:F1}", _labelStyle);
            }
        }

        // ─── Stats row helper ─────────────────────────────────────────────────────

        private void DrawStat(float px, float py, float h, float totalW,
            string l1, double v1, string l2, double v2,
            string l3, double v3, string l4, double v4,
            string l5, double v5, Color c1, Color c2, Color c3, Color c4, Color c5, string u5 = "ms")
        {
            float colW = (totalW - 16f) / 5f; 
            
            DrawStatCol(px + colW * 0f + 8f, py, colW, h, l1, v1, c1);
            DrawStatCol(px + colW * 1f + 8f, py, colW, h, l2, v2, c2);
            DrawStatCol(px + colW * 2f + 8f, py, colW, h, l3, v3, c3);
            DrawStatCol(px + colW * 3f + 8f, py, colW, h, l4, v4, c4);
            DrawStatCol(px + colW * 4f + 8f, py, colW, h, l5, v5, c5, unit: u5);
        }

        private void DrawStatCol(float x, float y, float w, float h,
            string label, double value, Color valueColor, string unit = "ms")
        {
            float halfH = h * 0.44f;
            GUI.Label(new Rect(x, y + 1f,          w, halfH), label, _statLabelStyle);

            _statValueStyle.normal.textColor = valueColor;
            
            // Si c'est "LOST" ou "FPS", on n'affiche pas l'unité "ms"
            bool noUnit = (label == "LOST" || label == "FPS" || label == "Avg.FPS");
            string displayedUnit = noUnit ? "" : unit;
            
            GUI.Label(new Rect(x, y + halfH,       w, halfH + 2f),
                value >= 0 ? $"{value:F0}{displayedUnit}" : "—", _statValueStyle);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private Rect GetPanelRect()
        {
            float w = size.x, h = size.y;
            float x, y;
            switch (corner)
            {
                case DisplayCorner.TopLeft:
                    x = margin; y = margin; break;
                case DisplayCorner.TopRight:
                    x = Screen.width - w - margin; y = margin; break;
                case DisplayCorner.BottomLeft:
                    x = margin; y = Screen.height - h - margin; break;
                default:
                    x = Screen.width - w - margin; y = Screen.height - h - margin; break;
            }
            return new Rect(x, y, w, h);
        }

        private Color GetColor(float v)
        {
            if (v < 0f)               return ColMuted;
            if (v >= redThreshold)    return ColBad;
            if (v >= orangeThreshold) return ColWarn;
            return ColGood;
        }

        private Color GetEncodeColor(float v)
        {
            if (v < 0f)                     return ColMuted;
            if (v >= redEncodeThreshold)    return ColBad;
            if (v >= orangeEncodeThreshold) return ColWarn;
            return ColGood;
        }

        private Color GetDecodeColor(float v)
        {
            if (v < 0f)                     return ColMuted;
            if (v >= redDecodeThreshold)    return ColBad;
            if (v >= orangeDecodeThreshold) return ColWarn;
            return ColGood;
        }

        private Color GetTotalColor(float v, bool muted = true)
        {
            float alpha = muted ? 0.25f : 1.0f;
            if (v < 0f)                    return ColMuted;
            if (v >= redTotalThreshold)    return new Color(ColBad.r, ColBad.g, ColBad.b, alpha);
            if (v >= orangeTotalThreshold) return new Color(ColWarn.r, ColWarn.g, ColWarn.b, alpha);
            return new Color(ColGood.r, ColGood.g, ColGood.b, alpha);
        }

        private Texture2D GetTex(float v)
        {
            if (v < 0f)               return _texMuted;
            if (v >= redThreshold)    return _texBad;
            if (v >= orangeThreshold) return _texWarn;
            return _texGood;
        }

        private Texture2D GetTotalTex(float v)
        {
            if (v < 0f)                    return _texMuted;
            if (v >= redTotalThreshold)    return _texBadMuted;
            if (v >= orangeTotalThreshold) return _texWarnMuted;
            return _texGoodMuted;
        }

        private Texture2D _texGoodMuted, _texWarnMuted, _texBadMuted;

        private void EnsureStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt   = true;
            _currentAccent = ColGood;

            _texBg       = MakeTex(ColBg);
            _texGood     = MakeTex(ColGood);
            _texWarn     = MakeTex(ColWarn);
            _texBad      = MakeTex(ColBad);
            _texMuted    = MakeTex(ColMuted);
            _texDivider  = MakeTex(ColDivider);
            _texAccent   = MakeTex(ColGood);
            _texPillGood = MakeTex(new Color(ColGood.r, ColGood.g, ColGood.b, 0.15f));
            _texPillBad  = MakeTex(new Color(ColBad.r,  ColBad.g,  ColBad.b,  0.15f));
            _texPillMuted= MakeTex(new Color(0.4f, 0.4f, 0.4f, 0.15f));
            _texTotalMuted = MakeTex(new Color(0.7f, 0.7f, 0.8f, 0.15f));
            _texAvgLine    = MakeTex(new Color(1f, 1f, 1f, 0.5f));

            _texGoodMuted = MakeTex(new Color(ColGood.r, ColGood.g, ColGood.b, 0.15f));
            _texWarnMuted = MakeTex(new Color(ColWarn.r, ColWarn.g, ColWarn.b, 0.15f));
            _texBadMuted  = MakeTex(new Color(ColBad.r, ColBad.g, ColBad.b, 0.15f));

            _labelStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColMutedFg },
            };

            _pillStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = ColGood, background = _texPillGood },
                padding   = new RectOffset(4, 4, 2, 2),
            };

            _valueStyle = new GUIStyle
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColValue },
            };

            _statLabelStyle = new GUIStyle
            {
                fontSize  = 8,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColMutedFg },
            };

            _statValueStyle = new GUIStyle
            {
                fontSize  = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColValue },
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

        
#endif
    }

}
