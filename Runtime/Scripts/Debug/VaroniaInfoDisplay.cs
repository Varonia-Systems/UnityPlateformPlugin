// VaroniaInfoDisplay — widget infos (game version, package version, height, player size, headset, MQTT).
//
// IMGUI par défaut, UI Toolkit avec define VBO_UITOOLKIT_OVERLAYS.
// Toggle : Project Settings → Varonia Back Office → Debug Overlays Rendering.

using System.Collections;
using uPLibrary.Networking.M2Mqtt;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if VBO_UITOOLKIT_OVERLAYS
using UnityEngine.UIElements;
#endif

namespace VaroniaBackOffice
{
    public class VaroniaInfoDisplay : MonoBehaviour
    {
        public enum DisplayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Display")]
        [SerializeField] private DisplayCorner corner = DisplayCorner.BottomLeft;
        [SerializeField] private float         margin = 12f;
        [SerializeField] private Vector2       size   = new Vector2(200f, 130f);

        [Header("UI Scale")]
        public float scaleFactor = 1f;

        [Header("Camera")]
        [SerializeField] private Camera _camera;

        // ─── Palette ─────────────────────────────────────────────────────────────
        static readonly Color ColBg    = new Color(0.11f, 0.11f, 0.14f, 0.92f);
        static readonly Color ColGood  = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color ColBad   = new Color(1.00f, 0.40f, 0.40f, 1f);
        static readonly Color ColMuted = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color ColValue = new Color(0.92f, 0.92f, 0.95f, 1f);

        // ─── Cached data ─────────────────────────────────────────────────────────
        private string _gameVersion;
        private string _packageVersion;
        private string _headsetName;

        bool show = false;
        private bool _lastMqttConnected = false;
        private bool _firstUpdateDone = false;
        // Cache des valeurs arrondies pour éviter le rebuild de string chaque frame
        private float _lastHeightShown = float.NaN;
        private float _lastPlayerSizeShown = float.NaN;
        private string _lastHeadsetShown;

        // ═══════════════════════════════════════════════════════════════════════
        //  Lifecycle
        // ═══════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _gameVersion = Application.version;
            var settings = VaroniaRuntimeSettings.Load();
            _packageVersion = settings != null ? settings.packageVersion : "—";
        }

        IEnumerator Start()
        {
            yield return new WaitForSeconds(0.1f);
            BackOfficeVaronia.RaiseMovieChanged();
            yield return new WaitForSeconds(1f);
            _headsetName = GlobalConfig.ResolveHeadsetName();

#if VBO_UITOOLKIT_OVERLAYS
            // Le headset arrive tardivement → push une fois ici
            if (_headsetLabel != null) _headsetLabel.text = _headsetName ?? "—";
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
            if (_bgTex      != null) Destroy(_bgTex);
            if (_accentTex  != null) Destroy(_accentTex);
            if (_pillGoodTex!= null) Destroy(_pillGoodTex);
            if (_pillBadTex != null) Destroy(_pillBadTex);
        }
#endif

        private void OnMovieChanged()
        {
            if (BackOfficeVaronia.Instance != null)
            {
                var mode = BackOfficeVaronia.Instance.config.DeviceMode;
                bool isSpectator = mode == DeviceMode.Server_Spectator || mode == DeviceMode.Client_Spectator;
                show = !isSpectator && BackOfficeVaronia.Instance.config.HideMode == 0;
            }
#if VBO_UITOOLKIT_OVERLAYS
            if (_panel != null)
                _panel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
#endif
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            bool f8 = kb != null && kb[Key.F8].wasPressedThisFrame;
#else
            bool f8 = Input.GetKeyDown(KeyCode.F8);
#endif
            if (f8 && BackOfficeVaronia.Instance != null)
            {
                BackOfficeVaronia.Instance.config.HideMode++;
                if (BackOfficeVaronia.Instance.config.HideMode > 2)
                    BackOfficeVaronia.Instance.config.HideMode = 0;
                BackOfficeVaronia.RaiseMovieChanged();
                Debug.Log($"[VaroniaInfoDisplay] Hide mode: {BackOfficeVaronia.Instance.config.HideMode}");
            }

#if VBO_UITOOLKIT_OVERLAYS
            if (_panel == null) return;

            // Push texts (zero alloc steady state via comparaison value-types puis Label.text setter)
            bool mqttConnected = MqttClient.IsConnected__;
            if (mqttConnected != _lastMqttConnected || !_firstUpdateDone)
            {
                _lastMqttConnected = mqttConnected;
                _mqttLabel.text = mqttConnected ? "CONNECTED" : "OFFLINE";
                _mqttLabel.style.color = mqttConnected ? ColGood : ColBad;
                _mqttPill.style.backgroundColor = new Color(
                    mqttConnected ? ColGood.r : ColBad.r,
                    mqttConnected ? ColGood.g : ColBad.g,
                    mqttConnected ? ColGood.b : ColBad.b, 0.15f);
                _accent.style.backgroundColor = mqttConnected ? ColGood : ColBad;
            }

            if (!_firstUpdateDone)
            {
                _gameValue.text    = _gameVersion;
                _packageValue.text = _packageVersion;
                _firstUpdateDone = true;
            }

            // Height — round à 0.01m
            if (_camera == null) _camera = Camera.main;
            if (_camera != null && BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.Rig != null)
            {
                float y = _camera.transform.localPosition.y + 0.09f + BackOfficeVaronia.Instance.Rig.localPosition.y;
                float yRounded = Mathf.Round(y * 100f) / 100f;
                if (yRounded != _lastHeightShown)
                {
                    _lastHeightShown = yRounded;
                    _heightValue.text = yRounded.ToString("F2") + " m";
                }
            }

            // Player size — round à 0.01m
            float psize = AutoSizing.Player_Size;
            float psizeRounded = psize > 0.1f ? Mathf.Round(psize * 100f) / 100f : -1f;
            if (psizeRounded != _lastPlayerSizeShown)
            {
                _lastPlayerSizeShown = psizeRounded;
                _playerSizeValue.text = psizeRounded < 0f ? "—" : psizeRounded.ToString("F2") + " m";
            }

            // Headset — change rarement, compare référence string
            if (_headsetLabel != null && !string.IsNullOrEmpty(_headsetName) && _lastHeadsetShown != _headsetName)
            {
                _lastHeadsetShown = _headsetName;
                _headsetLabel.text = _headsetName;
            }
#endif
        }

#if VBO_UITOOLKIT_OVERLAYS
        // ═══════════════════════════════════════════════════════════════════════
        //  UI Toolkit
        // ═══════════════════════════════════════════════════════════════════════

        private UIDocument _doc;
        private PanelSettings _panelSettings;
        private VisualElement _root, _panel, _accent, _mqttPill;
        private Label _gameValue, _packageValue, _heightValue, _playerSizeValue, _headsetLabel, _mqttLabel;

        private void BuildOverlay_UITK()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.sortingOrder = 100;

            var uiGo = new GameObject("[InfoDisplayUI]");
            uiGo.transform.SetParent(transform, false);
            _doc = uiGo.AddComponent<UIDocument>();
            _doc.panelSettings = _panelSettings;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                    ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            _root.pickingMode = PickingMode.Ignore;
            if (font != null) _root.style.unityFont = font;

            _panel = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    width = size.x,
                    minHeight = size.y,
                    flexDirection = FlexDirection.Column,
                    backgroundColor = ColBg,
                    paddingLeft = 12, paddingRight = 12, paddingTop = 4, paddingBottom = 4,
                    borderTopLeftRadius = 4, borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                    display = show ? DisplayStyle.Flex : DisplayStyle.None,
                }
            };
            PositionPanel_UITK();
            _root.Add(_panel);

            // Accent bar
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

            _panel.Add(MakeRow("GAME",           "—", out _gameValue));
            _panel.Add(MakeRow("BACK OFFICE",    "—", out _packageValue));
            _panel.Add(MakeRow("CURRENT HEIGHT", "—", out _heightValue));
            _panel.Add(MakeRow("PLAYER SIZE",    "—", out _playerSizeValue));
            _panel.Add(MakeRow("HEADSET",        "—", out _headsetLabel));

            // MQTT row with pill
            var mqttRow = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style = { flexDirection = FlexDirection.Row, height = 22, alignItems = Align.Center }
            };
            var mqttLabel = MakeLabel("MQTT", 9, FontStyle.Bold, ColMuted, TextAnchor.MiddleLeft);
            mqttLabel.style.flexGrow = 1;
            mqttRow.Add(mqttLabel);

            _mqttPill = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    width = 86, height = 18,
                    paddingLeft = 4, paddingRight = 4,
                    backgroundColor = new Color(ColBad.r, ColBad.g, ColBad.b, 0.15f),
                    alignItems = Align.Center, justifyContent = Justify.Center,
                }
            };
            _mqttLabel = MakeLabel("OFFLINE", 9, FontStyle.Bold, ColBad, TextAnchor.MiddleCenter);
            _mqttPill.Add(_mqttLabel);
            mqttRow.Add(_mqttPill);
            _panel.Add(mqttRow);
        }

        private VisualElement MakeRow(string labelText, string initialValue, out Label valueLabel)
        {
            var row = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style = { flexDirection = FlexDirection.Row, height = 18, alignItems = Align.Center }
            };
            var l = MakeLabel(labelText, 9, FontStyle.Bold, ColMuted, TextAnchor.MiddleLeft);
            l.style.flexGrow = 1;
            row.Add(l);

            valueLabel = MakeLabel(initialValue, 11, FontStyle.Bold, ColValue, TextAnchor.MiddleRight);
            valueLabel.style.minWidth = 86;
            row.Add(valueLabel);
            return row;
        }

        private static Label MakeLabel(string text, int fontSize, FontStyle fStyle, Color color, TextAnchor anchor)
        {
            return new Label(text)
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    fontSize = fontSize, color = color,
                    unityFontStyleAndWeight = fStyle, unityTextAlign = anchor,
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
                default:
                    _panel.style.right = m; _panel.style.bottom = m;
                    _panel.style.left = StyleKeyword.Auto; _panel.style.top = StyleKeyword.Auto;
                    break;
            }
        }
#endif // VBO_UITOOLKIT_OVERLAYS

#if !VBO_UITOOLKIT_OVERLAYS
        // ═══════════════════════════════════════════════════════════════════════
        //  IMGUI (fallback)
        // ═══════════════════════════════════════════════════════════════════════

        private bool      _stylesBuilt;
        private float     _lastScale = 1f;
        private GUIStyle  _bgStyle, _labelStyle, _valueStyle, _pillStyle;
        private Texture2D _bgTex, _accentTex, _pillGoodTex, _pillBadTex;

        private void OnGUI()
        {
            if (!show) return;
            if (Event.current.type != EventType.Repaint) return;

            float scale = (Screen.height / 1080f) * scaleFactor;
            EnsureStyles(scale);

            bool mqttConnected = MqttClient.IsConnected__;
            Rect panel = GetPanelRect(scale);

            GUI.DrawTexture(panel, _bgTex);
            GUI.DrawTexture(
                new Rect(panel.x, panel.y, 3f * scale, panel.height),
                mqttConnected ? _accentTex : _pillBadTex
            );

            float rowH = panel.height / 6f;
            float textX = panel.x + 12f * scale;
            float textW = panel.width - 60f * scale;
            float valX  = panel.x + panel.width - 90f * scale;
            float valW  = 86f * scale;

            GUI.Label(new Rect(textX, panel.y + rowH * 0f + 4f * scale, textW, rowH), "GAME", _labelStyle);
            GUI.Label(new Rect(valX,  panel.y + rowH * 0f + 4f * scale, valW,  rowH), _gameVersion, _valueStyle);

            GUI.Label(new Rect(textX, panel.y + rowH * 1f, textW, rowH), "BACK OFFICE", _labelStyle);
            GUI.Label(new Rect(valX,  panel.y + rowH * 1f, valW,  rowH), _packageVersion, _valueStyle);

            string camY = "—";
            if (_camera == null) _camera = Camera.main;
            if (_camera != null && BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.Rig != null)
                camY = (_camera.transform.localPosition.y + 0.09f + BackOfficeVaronia.Instance.Rig.localPosition.y).ToString("F2") + " m";
            GUI.Label(new Rect(textX, panel.y + rowH * 2f, textW, rowH), "CURRENT HEIGHT", _labelStyle);
            GUI.Label(new Rect(valX,  panel.y + rowH * 2f, valW,  rowH), camY, _valueStyle);

            string playerSizeStr = AutoSizing.Player_Size > 0.1f ? AutoSizing.Player_Size.ToString("F2") + " m" : "—";
            GUI.Label(new Rect(textX, panel.y + rowH * 3f, textW, rowH), "PLAYER SIZE", _labelStyle);
            GUI.Label(new Rect(valX,  panel.y + rowH * 3f, valW,  rowH), playerSizeStr, _valueStyle);

            GUI.Label(new Rect(textX, panel.y + rowH * 4f, textW, rowH), "HEADSET", _labelStyle);
            GUI.Label(new Rect(valX,  panel.y + rowH * 4f, valW,  rowH), _headsetName ?? "—", _valueStyle);

            GUI.Label(new Rect(textX, panel.y + rowH * 5f - 2f * scale, textW, rowH), "MQTT", _labelStyle);
            _pillStyle.normal.background = mqttConnected ? _pillGoodTex : _pillBadTex;
            _pillStyle.normal.textColor  = mqttConnected ? ColGood      : ColBad;
            GUI.Label(
                new Rect(valX, panel.y + rowH * 5f, valW, rowH - 4f * scale),
                mqttConnected ? "CONNECTED" : "OFFLINE",
                _pillStyle
            );
        }

        private Rect GetPanelRect(float scale)
        {
            float w = size.x * scale, h = size.y * scale;
            float x, y;
            float mx = margin * scale, my = margin * scale;
            switch (corner)
            {
                case DisplayCorner.TopLeft:     x = mx; y = my; break;
                case DisplayCorner.TopRight:    x = Screen.width - w - mx; y = my; break;
                case DisplayCorner.BottomLeft:  x = mx; y = Screen.height - h - my; break;
                default:                        x = Screen.width - w - mx; y = Screen.height - h - my; break;
            }
            return new Rect(x, y, w, h);
        }

        private void EnsureStyles(float scale)
        {
            if (_stylesBuilt && Mathf.Approximately(scale, _lastScale)) return;
            _stylesBuilt = true;
            _lastScale   = scale;

            if (_bgTex == null)       _bgTex       = MakeTex(ColBg);
            if (_accentTex == null)   _accentTex   = MakeTex(ColGood);
            if (_pillGoodTex == null) _pillGoodTex = MakeTex(new Color(ColGood.r, ColGood.g, ColGood.b, 0.15f));
            if (_pillBadTex == null)  _pillBadTex  = MakeTex(new Color(ColBad.r,  ColBad.g,  ColBad.b,  0.15f));

            _bgStyle = new GUIStyle { normal = { background = _bgTex } };
            _labelStyle = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(9 * scale), fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft, normal = { textColor = ColMuted },
            };
            _valueStyle = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(11 * scale), fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight, normal = { textColor = ColValue },
            };
            _pillStyle = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(9 * scale), fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(Mathf.RoundToInt(4 * scale), Mathf.RoundToInt(4 * scale),
                                          Mathf.RoundToInt(2 * scale), Mathf.RoundToInt(2 * scale)),
            };
        }

        private static Texture2D MakeTex(Color col)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, col); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }
#endif // !VBO_UITOOLKIT_OVERLAYS
    }
}
