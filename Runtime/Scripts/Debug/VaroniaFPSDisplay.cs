using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Widget FPS + mémoire : FPS instantané, moyenne, RAM process, VRAM.
    /// Rendu via OnGUI — aucun Canvas, aucun prefab requis.
    /// </summary>
    public class VaroniaFPSDisplay : MonoBehaviour
    {
        // ─── Config ───────────────────────────────────────────────────────────────

        public enum DisplayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Display")]
        [SerializeField] private DisplayCorner corner  = DisplayCorner.BottomRight;
        [SerializeField] private float         margin  = 12f;
        [SerializeField] private Vector2       size    = new Vector2(150f, 190f);

        [Header("FPS")]
        [SerializeField] private int   sampleCount      = 300;
        [SerializeField] private float updateInterval   = 0.1f;
        [SerializeField] private float memUpdateInterval = 0.5f;

        [Header("Thresholds")]
        [SerializeField] private int thresholdGood = 55;
        [SerializeField] private int thresholdWarn = 30;

        // ─── Colors ───────────────────────────────────────────────────────────────

        static readonly Color ColBg      = new Color(0.11f, 0.11f, 0.14f, 0.92f);
        static readonly Color ColGood    = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color ColWarn    = new Color(1.00f, 0.75f, 0.30f, 1f);
        static readonly Color ColBad     = new Color(1.00f, 0.40f, 0.40f, 1f);
        static readonly Color ColMuted   = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color ColValue   = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color ColDivider = new Color(1f,    1f,    1f,    0.06f);

        // ─── State ────────────────────────────────────────────────────────────────

        private float[] _samples;
        private int     _sampleIdx;
        private float   _fpsTimer;
        private float   _memTimer;
        private int     _fps;
        private int     _avg;
        private int     _sessionAvg;
        private double  _sessionFpsSum;
        private long    _sessionFrameCount;
        private int     _ramMb;
        private int     _vramMb;

        // ─── Styles ───────────────────────────────────────────────────────────────

        private bool      _stylesBuilt;
        private GUIStyle  _fpsStyle;
        private GUIStyle  _avgStyle;
        private GUIStyle  _sessionAvgStyle;
        private GUIStyle  _memLabelStyle;
        private GUIStyle  _memValueStyle;
        private GUIStyle  _timeStyle;
        private Texture2D _bgTex;
        private Texture2D _accentTex;
        private Texture2D _dividerTex;
        private Texture2D _squareGoodTex;
        private Texture2D _squareWarnTex;
        private Texture2D _squareBadTex;
        private Texture2D _squareEmptyTex;
        private Color     _lastAccentColor;

        // ─────────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _samples = new float[sampleCount];
        }

        private void OnDestroy()
        {
            if (_bgTex)          Destroy(_bgTex);
            if (_accentTex)      Destroy(_accentTex);
            if (_dividerTex)     Destroy(_dividerTex);
            if (_squareGoodTex)  Destroy(_squareGoodTex);
            if (_squareWarnTex)  Destroy(_squareWarnTex);
            if (_squareBadTex)   Destroy(_squareBadTex);
            if (_squareEmptyTex) Destroy(_squareEmptyTex);
        }

        // ─── Update ───────────────────────────────────────────────────────────────

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // ── FPS ──
            _samples[_sampleIdx] = dt;
            _sampleIdx = (_sampleIdx + 1) % sampleCount;

            _fpsTimer += dt;
            if (_fpsTimer >= updateInterval)
            {
                _fpsTimer = 0f;
                _fps = dt > 0f ? Mathf.RoundToInt(1f / dt) : 0;

                float sum = 0f;
                for (int i = 0; i < sampleCount; i++) sum += _samples[i];
                _avg = sum > 0f ? Mathf.RoundToInt(sampleCount / sum) : 0;

                // Moyenne cumulative de session — O(1), mémoire constante
                _sessionFpsSum += _fps;
                _sessionFrameCount++;
                _sessionAvg = _sessionFrameCount > 0
                    ? (int)Math.Round(_sessionFpsSum / _sessionFrameCount)
                    : 0;
            }

            // ── Memory (moins fréquent) ──
            _memTimer += dt;
            if (_memTimer >= memUpdateInterval)
            {
                _memTimer = 0f;
                _ramMb  = (int)(Profiler.GetTotalAllocatedMemoryLong()        / (1024 * 1024));
                _vramMb = (int)(Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024 * 1024));
            }
        }

        
        
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

            // Reset session avg à chaque changement de séquence
            _sessionFpsSum     = 0;
            _sessionFrameCount = 0;
            _sessionAvg        = 0;
        }


        bool show = true;
        
        
        // ─── Rendering ────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            
            if (!show) return;
            
            
            EnsureStyles();

            Color accent = _fps >= thresholdGood ? ColGood
                         : _fps >= thresholdWarn ? ColWarn
                         : ColBad;

            if (accent != _lastAccentColor)
            {
                _lastAccentColor = accent;
                _accentTex.SetPixel(0, 0, accent);
                _accentTex.Apply();
            }

            // Layout en hauteurs fixes (px) — indépendant de size.y
            float pad       = 10f;
            float squaresH  = 8f;
            float squaresGap = 3f;
            float fpsH      = 28f;
            float gapFpsAvg = 4f;
            float avgH      = 18f;
            float sessionH  = 18f;
            float divH      = 1f;
            float gapDiv    = 5f;
            float memH      = 18f;
            float timeH     = 18f;

            float totalH = pad + squaresH + squaresGap + fpsH + gapFpsAvg + avgH + sessionH + 2f + divH + gapDiv
                         + memH + memH + 2f + divH + gapDiv + timeH + pad;

            float panelW = size.x;
            Rect  panel  = GetPanelRectFixed(panelW, totalH);
            float x      = panel.x + 12f;
            float w      = panel.width - 16f;

            // ── Background ──
            GUI.DrawTexture(panel, _bgTex);

            // ── Left accent bar ──
            GUI.DrawTexture(new Rect(panel.x, panel.y, 3f, totalH), _accentTex);

            float ySquares = panel.y + pad;
            float yFps     = ySquares + squaresH + squaresGap;
            float yAvg     = yFps     + fpsH + gapFpsAvg;
            float ySession = yAvg     + avgH;
            float yDiv     = ySession + sessionH + 2f;
            float yRam  = yDiv  + divH + gapDiv;
            float yVram = yRam  + memH;
            float yDiv2 = yVram + memH + 2f;
            float yTime = yDiv2 + divH + gapDiv;

            // ── Sample squares (pixel-perfect, no gaps) ──
            {
                int   sqCount  = sampleCount;
                int   startX   = Mathf.RoundToInt(panel.x + 12f);
                int   endX     = Mathf.RoundToInt(panel.x + panel.width - 12f);
                int   availW   = endX - startX;
                int   sqY      = Mathf.RoundToInt(ySquares);
                int   sqH      = Mathf.RoundToInt(squaresH);
                for (int i = 0; i < sqCount; i++)
                {
                    // pixel-perfect subdivision : chaque segment couvre exactement sa tranche
                    int x0 = startX + Mathf.RoundToInt((float)i       * availW / sqCount);
                    int x1 = startX + Mathf.RoundToInt((float)(i + 1) * availW / sqCount);
                    if (x1 <= x0) x1 = x0 + 1;

                    int   bufIdx   = (_sampleIdx + i) % sampleCount;
                    float sampleDt = _samples[bufIdx];
                    Texture2D sqTex;
                    if (sampleDt <= 0f)
                        sqTex = _squareEmptyTex;
                    else
                    {
                        int sampleFps = Mathf.RoundToInt(1f / sampleDt);
                        sqTex = sampleFps >= thresholdGood ? _squareGoodTex
                              : sampleFps >= thresholdWarn ? _squareWarnTex
                              : _squareBadTex;
                    }
                    GUI.DrawTexture(new Rect(x0, sqY, x1 - x0, sqH), sqTex);
                }
            }

            // ── FPS ──
            _fpsStyle.normal.textColor = accent;
            GUI.Label(new Rect(x, yFps,  w, fpsH), $"FPS   {_fps}", _fpsStyle);

            // ── AVG (fenêtre glissante) ──
            GUI.Label(new Rect(x, yAvg,    w, avgH),    $"AVG   {_avg}",        _avgStyle);

            // ── AVG Session ──
            GUI.Label(new Rect(x, ySession, w, sessionH), $"SES   {_sessionAvg}", _sessionAvgStyle);

            // ── Divider ──
            GUI.DrawTexture(new Rect(panel.x + 8f, yDiv, panel.width - 16f, divH), _dividerTex);

            // ── RAM ──
            GUI.Label(new Rect(x,       yRam,  40f,   memH), "RAM",      _memLabelStyle);
            GUI.Label(new Rect(x + 40f, yRam,  w-40f, memH), $"{_ramMb} MB",  _memValueStyle);

            // ── VRAM ──
            GUI.Label(new Rect(x,       yVram, 40f,   memH), "VRAM",     _memLabelStyle);
            GUI.Label(new Rect(x + 40f, yVram, w-40f, memH), $"{_vramMb} MB", _memValueStyle);

            // ── Divider 2 ──
            GUI.DrawTexture(new Rect(panel.x + 8f, yDiv2, panel.width - 16f, divH), _dividerTex);

            // ── Heure ──
            GUI.Label(new Rect(x, yTime, w, timeH), DateTime.Now.ToString("HH:mm:ss"), _timeStyle);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private Rect GetPanelRectFixed(float w, float h)
        {
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

        private void EnsureStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            _bgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _bgTex.SetPixel(0, 0, ColBg);
            _bgTex.Apply();
            _bgTex.hideFlags = HideFlags.HideAndDontSave;

            _accentTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _accentTex.SetPixel(0, 0, ColGood);
            _accentTex.Apply();
            _accentTex.hideFlags = HideFlags.HideAndDontSave;

            _dividerTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _dividerTex.SetPixel(0, 0, ColDivider);
            _dividerTex.Apply();
            _dividerTex.hideFlags = HideFlags.HideAndDontSave;

            _squareGoodTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _squareGoodTex.SetPixel(0, 0, ColGood);
            _squareGoodTex.Apply();
            _squareGoodTex.hideFlags = HideFlags.HideAndDontSave;

            _squareWarnTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _squareWarnTex.SetPixel(0, 0, ColWarn);
            _squareWarnTex.Apply();
            _squareWarnTex.hideFlags = HideFlags.HideAndDontSave;

            _squareBadTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _squareBadTex.SetPixel(0, 0, ColBad);
            _squareBadTex.Apply();
            _squareBadTex.hideFlags = HideFlags.HideAndDontSave;

            _squareEmptyTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _squareEmptyTex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.08f));
            _squareEmptyTex.Apply();
            _squareEmptyTex.hideFlags = HideFlags.HideAndDontSave;

            _lastAccentColor = ColGood;

            _fpsStyle = new GUIStyle
            {
                fontSize  = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColGood },
            };

            _avgStyle = new GUIStyle
            {
                fontSize  = 11,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColMuted },
            };

            _sessionAvgStyle = new GUIStyle
            {
                fontSize  = 11,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColValue },
            };

            _memLabelStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColMuted },
            };

            _memValueStyle = new GUIStyle
            {
                fontSize  = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColValue },
            };

            _timeStyle = new GUIStyle
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColMuted },
            };
        }
    }
}
