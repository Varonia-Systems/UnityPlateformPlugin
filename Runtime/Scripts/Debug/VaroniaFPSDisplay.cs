// VaroniaFPSDisplay — widget FPS / AVG / SES + horloge.
//
// DEUX IMPLÉMENTATIONS COEXISTENT dans CE FICHIER UNIQUE pour garder le même
// MonoScript guid (= les références prefab/scene survivent au toggle) :
//   • Par défaut → IMGUI (compatible toutes versions Unity)
//   • Avec define VBO_UITOOLKIT_OVERLAYS → UI Toolkit (Unity 2021.2+, zero alloc)
//
// Le toggle se fait via Project Settings → Varonia Back Office → "Debug Overlays Rendering".
// Les champs serialized (corner, margin, size, scaleFactor, sampleCount, thresholds, mini)
// sont communs aux deux implémentations → inspector identique.

using System;
using System.Collections;
using System.Text;
using UnityEngine;

#if VBO_UITOOLKIT_OVERLAYS
using UnityEngine.UIElements;
#endif

namespace VaroniaBackOffice
{
    public class VaroniaFPSDisplay : MonoBehaviour
    {
        // ─── Config (partagée IMGUI / UITK) ──────────────────────────────────────

        public enum DisplayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Display")]
        [SerializeField] private DisplayCorner corner = DisplayCorner.BottomRight;
        [SerializeField] private float margin = 12f;
        [SerializeField] private Vector2 size = new Vector2(150f, 190f);
        [SerializeField] private bool mini = false;

        [Header("UI Scale")]
        public float scaleFactor = 1f;

        [Header("FPS")]
        [SerializeField] private int sampleCount = 300;
        [SerializeField] private float updateInterval = 0.1f;

        [Header("Thresholds")]
        [SerializeField] private int thresholdGood = 55;
        [SerializeField] private int thresholdWarn = 30;

        // ─── Palette partagée ────────────────────────────────────────────────────

        static readonly Color ColBg      = new Color(0.11f, 0.11f, 0.14f, 0.92f);
        static readonly Color ColGood    = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color ColWarn    = new Color(1.00f, 0.75f, 0.30f, 1f);
        static readonly Color ColBad     = new Color(1.00f, 0.40f, 0.40f, 1f);
        static readonly Color ColMuted   = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color ColValue   = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color ColDivider = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color ColEmpty   = new Color(1f, 1f, 1f, 0.08f);

        // ─── Stats state (partagé) ───────────────────────────────────────────────

        private float[] _samples;
        private int     _sampleIdx;
        private int     _sampleFilled;
        private float   _fpsTimer;
        private int     _fps, _avg, _sessionAvg;
        private double  _sessionDtSum;
        private long    _sessionFrameCount;
        private int     _lastFps = -1, _lastAvg = -1, _lastSessionAvg = -1;
        private int     _lastSecond = -1;
        private readonly StringBuilder _sb = new StringBuilder(32);
        private bool show = true;

        // ════════════════════════════════════════════════════════════════════════
        //  Lifecycle
        // ════════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _samples = new float[sampleCount];
#if !VBO_UITOOLKIT_OVERLAYS
            RecalcLayout();
#endif
        }

        private void OnEnable()
        {
            BackOfficeVaronia.OnMovieChanged += OnMovieChanged;
#if VBO_UITOOLKIT_OVERLAYS
            BuildOverlay_UITK();
#endif
        }

        private void OnDisable()
        {
            BackOfficeVaronia.OnMovieChanged -= OnMovieChanged;
#if VBO_UITOOLKIT_OVERLAYS
            if (_panelSettings != null) Destroy(_panelSettings);
            if (_doc != null && _doc.gameObject != null) Destroy(_doc.gameObject);
#endif
        }

#if !VBO_UITOOLKIT_OVERLAYS
        private void OnDestroy()
        {
            ReleaseRT_IMGUI();
            if (_glMat) Destroy(_glMat);
        }
#endif

        private void OnMovieChanged()
        {
            if (BackOfficeVaronia.Instance != null)
                show = BackOfficeVaronia.Instance.config.HideMode == 0;

            // Reset session stats
            _sessionDtSum = 0.0;
            _sessionFrameCount = 0;
            _sessionAvg = 0;
            _lastSessionAvg = -1;

            // Auto-mini for spectator modes (delayed to let config settle)
            StopCoroutine(nameof(DelayedMiniCheck));
            StartCoroutine(nameof(DelayedMiniCheck));

#if VBO_UITOOLKIT_OVERLAYS
            if (_panel != null)
                _panel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
#endif
        }

        private IEnumerator DelayedMiniCheck()
        {
            yield return new WaitForSecondsRealtime(0.2f);
            if (BackOfficeVaronia.Instance != null)
            {
                var mode = BackOfficeVaronia.Instance.config.DeviceMode;
                Mini = (mode == DeviceMode.Server_Spectator || mode == DeviceMode.Client_Spectator);
            }
        }

        public bool Mini
        {
            get => mini;
            set
            {
                if (mini == value) return;
                mini = value;
#if VBO_UITOOLKIT_OVERLAYS
                if (_avgLabel != null)     _avgLabel.style.display     = mini ? DisplayStyle.None : DisplayStyle.Flex;
                if (_sessionLabel != null) _sessionLabel.style.display = mini ? DisplayStyle.None : DisplayStyle.Flex;
                if (_timeLabel != null)    _timeLabel.style.display    = mini ? DisplayStyle.None : DisplayStyle.Flex;
#else
                _rtDirty = true;
#endif
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  UPDATE — partagée pour les stats, dispatch vers le bon renderer
        // ════════════════════════════════════════════════════════════════════════

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool validFrame = dt > 0f && dt < 1f;

            _samples[_sampleIdx] = validFrame ? dt : 0f;
            _sampleIdx = (_sampleIdx + 1) % sampleCount;
            if (_sampleFilled < sampleCount) _sampleFilled++;

            if (validFrame)
            {
                _sessionDtSum += dt;
                _sessionFrameCount++;
            }

            // Display tick (10 Hz par défaut)
            _fpsTimer += dt;
            if (_fpsTimer >= updateInterval)
            {
                _fpsTimer = 0f;
                _fps = validFrame ? Mathf.RoundToInt(1f / dt) : _fps;

                float sum = 0f;
                int count = Mathf.Min(_sampleFilled, sampleCount);
                for (int i = 0; i < count; i++) sum += _samples[i];
                _avg = (sum > 0f && count > 0) ? Mathf.RoundToInt(count / sum) : 0;

                _sessionAvg = _sessionDtSum > 0.0
                    ? (int)Math.Round(_sessionFrameCount / _sessionDtSum)
                    : 0;

                // Strings mis à jour (zero alloc steady state grâce au StringBuilder réutilisé
                // + comparaison avant write). Utilisé par les deux impls.
                bool fpsChanged     = _fps != _lastFps;
                bool avgChanged     = _avg != _lastAvg;
                bool sessionChanged = _sessionAvg != _lastSessionAvg;

                if (fpsChanged)
                {
                    _lastFps = _fps;
                    _sb.Clear().Append("FPS   ").Append(_fps);
                    _cachedFps = _sb.ToString();
                    _cachedAccent = _fps >= thresholdGood ? ColGood
                                  : _fps >= thresholdWarn ? ColWarn : ColBad;
                }
                if (avgChanged)
                {
                    _lastAvg = _avg;
                    _sb.Clear().Append("AVG   ").Append(_avg);
                    _cachedAvg = _sb.ToString();
                }
                if (sessionChanged)
                {
                    _lastSessionAvg = _sessionAvg;
                    _sb.Clear().Append("SES   ").Append(_sessionAvg);
                    _cachedSessionAvg = _sb.ToString();
                }

#if VBO_UITOOLKIT_OVERLAYS
                // Push aux Labels UI Toolkit
                if (_fpsLabel != null)
                {
                    if (fpsChanged)
                    {
                        _fpsLabel.text = _cachedFps;
                        _fpsLabel.style.color = _cachedAccent;
                        if (_accent != null) _accent.style.backgroundColor = _cachedAccent;
                    }
                    if (avgChanged && _avgLabel != null)         _avgLabel.text     = _cachedAvg;
                    if (sessionChanged && _sessionLabel != null) _sessionLabel.text = _cachedSessionAvg;
                }

                // Refresh complet de l'ordre visuel des squares (10 Hz tick)
                if (_squareEls != null)
                {
                    int n = _squareEls.Length;
                    for (int i = 0; i < n; i++)
                    {
                        int bufIdx = (_sampleIdx + i) % n;
                        _squareEls[i].style.backgroundColor = SquareColorFor(_samples[bufIdx]);
                    }
                }
#else
                _rtDirty = true;
#endif
            }

            // Time (1 Hz)
            int sec = DateTime.Now.Second;
            if (sec != _lastSecond)
            {
                _lastSecond = sec;
                _cachedTime = DateTime.Now.ToString("HH:mm:ss");
#if VBO_UITOOLKIT_OVERLAYS
                if (_timeLabel != null) _timeLabel.text = _cachedTime;
#endif
            }

#if !VBO_UITOOLKIT_OVERLAYS
            if (mini != _lastMini)
            {
                _lastMini = mini;
                _rtDirty = true;
            }
#endif
        }

        // ─── Strings cache (partagé entre IMGUI et UITK) ─────────────────────────
        private string _cachedFps        = "FPS   0";
        private string _cachedAvg        = "AVG   0";
        private string _cachedSessionAvg = "SES   0";
        private string _cachedTime       = "00:00:00";
        private Color  _cachedAccent     = ColGood;

        private Color SquareColorFor(float sampleDt)
        {
            if (sampleDt <= 0f) return ColEmpty;
            int fps = Mathf.RoundToInt(1f / sampleDt);
            return fps >= thresholdGood ? ColGood
                 : fps >= thresholdWarn ? ColWarn : ColBad;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  IMPLÉMENTATION UI TOOLKIT
        // ════════════════════════════════════════════════════════════════════════

#if VBO_UITOOLKIT_OVERLAYS
        private UIDocument _doc;
        private PanelSettings _panelSettings;
        private VisualElement _root, _panel, _accent, _squaresContainer;
        private VisualElement[] _squareEls;
        private Label _fpsLabel, _avgLabel, _sessionLabel, _timeLabel;
        private Font _runtimeFont;

        private void BuildOverlay_UITK()
        {
            _runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.sortingOrder = 100;

            var uiGo = new GameObject("[FPSDisplayUI]");
            uiGo.transform.SetParent(transform, false);
            _doc = uiGo.AddComponent<UIDocument>();
            _doc.panelSettings = _panelSettings;

            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            _root.pickingMode = PickingMode.Ignore;
            if (_runtimeFont != null) _root.style.unityFont = _runtimeFont;

            _panel = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    width = size.x,
                    minHeight = 120,
                    flexDirection = FlexDirection.Column,
                    flexShrink = 0,
                    backgroundColor = ColBg,
                    paddingLeft = 10, paddingRight = 10, paddingTop = 8, paddingBottom = 8,
                    borderTopLeftRadius = 4, borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                }
            };
            PositionPanel_UITK();
            _root.Add(_panel);

            _accent = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    left = 0, top = 0, bottom = 0, width = 3,
                    backgroundColor = ColGood,
                }
            };
            _panel.Add(_accent);

            // Squares row
            _squaresContainer = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style = { flexDirection = FlexDirection.Row, height = 8, marginBottom = 3 }
            };
            _panel.Add(_squaresContainer);

            _squareEls = new VisualElement[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                var sq = new VisualElement
                {
                    pickingMode = PickingMode.Ignore,
                    style = { flexGrow = 1, height = 8, backgroundColor = ColEmpty }
                };
                _squaresContainer.Add(sq);
                _squareEls[i] = sq;
            }

            _fpsLabel     = MakeLabel_UITK("FPS   0", 15, FontStyle.Bold,   ColGood,  TextAnchor.MiddleRight);
            _fpsLabel.style.marginTop = 4; _fpsLabel.style.height = 28;
            _panel.Add(_fpsLabel);

            _avgLabel     = MakeLabel_UITK("AVG   0", 11, FontStyle.Normal, ColMuted, TextAnchor.MiddleRight);
            _avgLabel.style.height = 18;
            _panel.Add(_avgLabel);

            _sessionLabel = MakeLabel_UITK("SES   0", 11, FontStyle.Normal, ColValue, TextAnchor.MiddleRight);
            _sessionLabel.style.height = 18; _sessionLabel.style.marginBottom = 4;
            _panel.Add(_sessionLabel);

            var div = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style = { height = 1, backgroundColor = ColDivider, marginTop = 4, marginBottom = 4 }
            };
            _panel.Add(div);

            _timeLabel = MakeLabel_UITK("00:00:00", 10, FontStyle.Bold, ColMuted, TextAnchor.MiddleRight);
            _timeLabel.style.height = 18; _timeLabel.style.marginTop = 4;
            _panel.Add(_timeLabel);
        }

        private static Label MakeLabel_UITK(string text, int fontSize, FontStyle fStyle, Color color, TextAnchor anchor)
        {
            return new Label(text)
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    fontSize = fontSize,
                    color = color,
                    unityFontStyleAndWeight = fStyle,
                    unityTextAlign = anchor,
                }
            };
        }

        private void PositionPanel_UITK()
        {
            if (_panel == null) return;
            float m = margin;
            _panel.style.width = size.x * scaleFactor;
            switch (corner)
            {
                case DisplayCorner.TopLeft:
                    _panel.style.left = m; _panel.style.top = m;
                    _panel.style.right = StyleKeyword.Auto; _panel.style.bottom = StyleKeyword.Auto;
                    break;
                case DisplayCorner.TopRight:
                    _panel.style.right = m; _panel.style.top = m;
                    _panel.style.left = StyleKeyword.Auto; _panel.style.bottom = StyleKeyword.Auto;
                    break;
                case DisplayCorner.BottomLeft:
                    _panel.style.left = m; _panel.style.bottom = m;
                    _panel.style.right = StyleKeyword.Auto; _panel.style.top = StyleKeyword.Auto;
                    break;
                case DisplayCorner.BottomRight:
                default:
                    _panel.style.right = m; _panel.style.bottom = m;
                    _panel.style.left = StyleKeyword.Auto; _panel.style.top = StyleKeyword.Auto;
                    break;
            }
        }
#endif // VBO_UITOOLKIT_OVERLAYS

        // ════════════════════════════════════════════════════════════════════════
        //  IMPLÉMENTATION IMGUI (fallback / compat anciennes versions)
        // ════════════════════════════════════════════════════════════════════════

#if !VBO_UITOOLKIT_OVERLAYS
        private RenderTexture _rt;
        private Material      _glMat;
        private bool          _rtDirty = true;

        private const float Pad        = 10f;
        private const float SquaresH   = 8f;
        private const float SquaresGap = 3f;
        private const float FpsH       = 28f;
        private const float GapFpsAvg  = 4f;
        private const float AvgH       = 18f;
        private const float SessionH   = 18f;
        private const float DivH       = 1f;
        private const float GapDiv     = 5f;
        private const float TimeH      = 18f;

        private const float MiniPad     = 8f;
        private const float MiniFpsH    = 24f;
        private const float MiniSquaresH = 6f;

        private float _totalH, _miniH, _panelW;
        private bool  _lastMini;

        private bool     _stylesBuilt;
        private float    _lastScale = 1f;
        private GUIStyle _fpsStyle, _avgStyle, _sessionAvgStyle, _timeStyle;

        private void RecalcLayout()
        {
            _totalH = Pad + SquaresH + SquaresGap + FpsH + GapFpsAvg + AvgH + SessionH + 2f
                    + DivH + GapDiv + TimeH + Pad;
            _miniH  = MiniPad + MiniSquaresH + 2f + MiniFpsH + MiniPad;
            _panelW = size.x;
        }

        private void ReleaseRT_IMGUI()
        {
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        private void EnsureRT_IMGUI(float scale)
        {
            float currentH = (mini ? _miniH : _totalH) * scale;
            int rtW = Mathf.Max(1, (int)(_panelW * scale));
            int rtH = Mathf.Max(1, (int)currentH);

            if (_rt == null || _rt.width != rtW || _rt.height != rtH)
            {
                ReleaseRT_IMGUI();
                _rt = new RenderTexture(rtW, rtH, 0, RenderTextureFormat.ARGB32)
                {
                    filterMode = FilterMode.Point,
                    hideFlags  = HideFlags.HideAndDontSave,
                    useMipMap  = false,
                    autoGenerateMips = false,
                };
                _rt.Create();
                _rtDirty = true;
            }

            if (_glMat == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return;
                _glMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _glMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _glMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _glMat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
                _glMat.SetInt("_ZWrite",   0);
            }
        }

        private void RenderToRT_IMGUI()
        {
            if (_rt == null || _glMat == null) return;

            float W = _rt.width;
            float H = _rt.height;
            float scale = (float)H / (mini ? _miniH : _totalH);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.Clear(true, true, Color.clear);

            _glMat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, W, H, 0);

            GLRect_IMGUI(0, 0, W, H, ColBg);
            GLRect_IMGUI(0, 0, 3f * scale, H, _cachedAccent);

            if (mini)
            {
                RenderSquaresBatched_IMGUI(12f * scale, W - 12f * scale, MiniPad * scale, MiniSquaresH * scale);
            }
            else
            {
                RenderSquaresBatched_IMGUI(12f * scale, W - 12f * scale, Pad * scale, SquaresH * scale);
                float yDiv1 = (Pad + SquaresH + SquaresGap + FpsH + GapFpsAvg + AvgH + SessionH + 2f) * scale;
                GLRect_IMGUI(8f * scale, yDiv1, W - 16f * scale, DivH * scale, ColDivider);
            }

            GL.PopMatrix();
            RenderTexture.active = prev;
            _rtDirty = false;
        }

        private void RenderSquaresBatched_IMGUI(float xStart, float xEnd, float yTop, float height)
        {
            float availW = xEnd - xStart;
            int count = sampleCount;
            float invCount = 1f / count;

            GL.Begin(GL.QUADS);
            for (int i = 0; i < count; i++)
            {
                float x0 = xStart + i       * availW * invCount;
                float x1 = xStart + (i + 1) * availW * invCount;
                if (x1 - x0 < 1f) x1 = x0 + 1f;

                int bufIdx = (_sampleIdx + i) % count;
                GL.Color(SquareColorFor(_samples[bufIdx]));
                GL.Vertex3(x0, yTop,          0);
                GL.Vertex3(x1, yTop,          0);
                GL.Vertex3(x1, yTop + height, 0);
                GL.Vertex3(x0, yTop + height, 0);
            }
            GL.End();
        }

        private static void GLRect_IMGUI(float x, float y, float w, float h, Color c)
        {
            GL.Begin(GL.QUADS);
            GL.Color(c);
            GL.Vertex3(x,     y,     0);
            GL.Vertex3(x + w, y,     0);
            GL.Vertex3(x + w, y + h, 0);
            GL.Vertex3(x,     y + h, 0);
            GL.End();
        }

        private void OnGUI()
        {
            if (!show) return;
            if (Event.current.type != EventType.Repaint) return;

            float scale = (Screen.height / 1080f) * scaleFactor;
            EnsureStyles_IMGUI(scale);
            EnsureRT_IMGUI(scale);

            if (_rtDirty) RenderToRT_IMGUI();

            float currentH = (mini ? _miniH : _totalH) * scale;
            Rect panel = GetPanelRect_IMGUI(_panelW * scale, currentH, scale);

            GUI.DrawTexture(panel, _rt);

            float x = panel.x + 12f * scale;
            float w = panel.width - 16f * scale;

            if (mini)
            {
                float yFps = panel.y + (MiniPad + MiniSquaresH + 2f) * scale;
                _fpsStyle.normal.textColor = _cachedAccent;
                GUI.Label(new Rect(x, yFps, w, MiniFpsH * scale), _cachedFps, _fpsStyle);
            }
            else
            {
                float yFps     = panel.y + (Pad + SquaresH + SquaresGap) * scale;
                float yAvg     = yFps + (FpsH + GapFpsAvg) * scale;
                float ySession = yAvg + AvgH * scale;
                float yDiv1    = ySession + SessionH * scale + 2f * scale;
                float yTime    = yDiv1 + (DivH + GapDiv) * scale;

                _fpsStyle.normal.textColor = _cachedAccent;
                GUI.Label(new Rect(x, yFps,     w, FpsH * scale),     _cachedFps,        _fpsStyle);
                GUI.Label(new Rect(x, yAvg,     w, AvgH * scale),     _cachedAvg,        _avgStyle);
                GUI.Label(new Rect(x, ySession, w, SessionH * scale), _cachedSessionAvg, _sessionAvgStyle);
                GUI.Label(new Rect(x, yTime,    w, TimeH * scale),    _cachedTime,       _timeStyle);
            }
        }

        private Rect GetPanelRect_IMGUI(float w, float h, float scale)
        {
            float px, py;
            float sMargin = margin * scale;
            switch (corner)
            {
                case DisplayCorner.TopLeft:     px = sMargin;                      py = sMargin;                      break;
                case DisplayCorner.TopRight:    px = Screen.width - w - sMargin;   py = sMargin;                      break;
                case DisplayCorner.BottomLeft:  px = sMargin;                      py = Screen.height - h - sMargin;  break;
                default:                        px = Screen.width - w - sMargin;   py = Screen.height - h - sMargin;  break;
            }
            return new Rect(px, py, w, h);
        }

        private void EnsureStyles_IMGUI(float scale)
        {
            if (_stylesBuilt && Mathf.Approximately(scale, _lastScale)) return;
            _stylesBuilt = true;
            _lastScale = scale;

            _fpsStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt(15 * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColGood },
            };
            _avgStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt(11 * scale),
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColMuted },
            };
            _sessionAvgStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt(11 * scale),
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColValue },
            };
            _timeStyle = new GUIStyle
            {
                fontSize  = Mathf.RoundToInt(10 * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColMuted },
            };
        }
#endif // !VBO_UITOOLKIT_OVERLAYS
    }
}
