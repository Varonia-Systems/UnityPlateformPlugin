// DebugInputOverlay — overlay 2D affichant l'état des 4 boutons VaroniaInput
// (+ état souris en debug mode, + telemetry tracker/connected/battery/rssi/boot).
//
// IMGUI par défaut, UI Toolkit avec define VBO_UITOOLKIT_OVERLAYS.
// Toggle : Project Settings → Varonia Back Office → Debug Overlays Rendering.

using UnityEngine;
using VBO_Ultimate.Runtime.Scripts.Input;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if VBO_UITOOLKIT_OVERLAYS
using UnityEngine.UIElements;
#endif

namespace VaroniaBackOffice
{
    public class DebugInputOverlay : MonoBehaviour
    {
        public enum DisplayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Display")]
        [SerializeField] private DisplayCorner corner = DisplayCorner.TopRight;
        [SerializeField] private Vector2 margin = new Vector2(12f, 12f);
        [SerializeField] private Vector2 size   = new Vector2(160f, 160f);

        [Header("UI Scale")]
        public float scaleFactor = 1f;

        // ─── Palette ─────────────────────────────────────────────────────────────
        static readonly Color ColBg     = new Color(0.11f, 0.11f, 0.14f, 0.92f);
        static readonly Color ColGood   = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color ColBad    = new Color(1.00f, 0.40f, 0.40f, 1f);
        static readonly Color ColMuted  = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color ColValue  = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color ColBtnOff = new Color(0.20f, 0.20f, 0.24f, 1f);

        // ─── Weapon ──────────────────────────────────────────────────────────────
        private int _weaponIndex;
        private VaroniaWeaponTracking tracking;

        // ─── État ────────────────────────────────────────────────────────────────
        private float _lastInputTime = -1f;
        private bool _mouseLeft, _mouseRight, _mouseMiddle, _mouseScroll;
        private bool show;

        // ═══════════════════════════════════════════════════════════════════════
        //  Lifecycle
        // ═══════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            tracking = GetComponentInParent<VaroniaWeaponTracking>();
            if (tracking != null)
            {
                _weaponIndex = tracking.weaponIndex;
                margin.y = margin.y + _weaponIndex * (size.y);
            }
            VaroniaInput.OnButtonChanged += OnButtonChangedHandler;
            OnMovieChanged();
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

        private void OnDestroy()
        {
            VaroniaInput.OnButtonChanged -= OnButtonChangedHandler;
#if !VBO_UITOOLKIT_OVERLAYS
            if (_bgTex)     Destroy(_bgTex);
            if (_accentTex) Destroy(_accentTex);
            if (_btnOnTex)  Destroy(_btnOnTex);
            if (_btnOffTex) Destroy(_btnOffTex);
            if (_btnFireTex) Destroy(_btnFireTex);
#endif
        }

        private void OnMovieChanged()
        {
            if (BackOfficeVaronia.Instance != null)
                show = BackOfficeVaronia.Instance.config.HideMode == 0;
#if VBO_UITOOLKIT_OVERLAYS
            if (_panel != null)
                _panel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
#endif
        }

        private void OnButtonChangedHandler(int weaponIndex, VaroniaButton button, bool pressed)
        {
            if (weaponIndex == _weaponIndex && pressed)
                _lastInputTime = Time.unscaledTime;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Update
        // ═══════════════════════════════════════════════════════════════════════

        private void Update()
        {
            if (DebugModeOverlay.IsSuperDebugMode)
            {
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null)
                {
                    _mouseLeft   = Mouse.current.leftButton.isPressed;
                    _mouseRight  = Mouse.current.rightButton.isPressed;
                    _mouseMiddle = Mouse.current.middleButton.isPressed;
                    _mouseScroll = Mathf.Abs(Mouse.current.scroll.ReadValue().y) > 0.01f;
                }
                else { _mouseLeft = _mouseRight = _mouseMiddle = _mouseScroll = false; }
#else
                _mouseLeft   = Input.GetMouseButton(0);
                _mouseRight  = Input.GetMouseButton(1);
                _mouseMiddle = Input.GetMouseButton(2);
                _mouseScroll = Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f;
#endif
                VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Primary,    _mouseLeft);
                VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Secondary,  _mouseRight);
                VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Tertiary,   _mouseMiddle);
                VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Quaternary, _mouseScroll);
            }

#if VBO_UITOOLKIT_OVERLAYS
            UpdateUITK();
#endif
        }

#if VBO_UITOOLKIT_OVERLAYS
        // ═══════════════════════════════════════════════════════════════════════
        //  UI Toolkit
        // ═══════════════════════════════════════════════════════════════════════

        private UIDocument _doc;
        private PanelSettings _panelSettings;
        private VisualElement _root, _panel, _accent;
        private Label _titleLabel, _lastInputValue;
        private VisualElement[] _btnEls = new VisualElement[4];
        private Label[] _btnLabels = new Label[4];
        private Label _trackedValue, _connectedValue;
        private VisualElement _batteryRow, _rssiRow, _bootRow;
        private Label _batteryValue, _rssiValue, _bootValue;

        // Cache pour minimiser les rebuilds de string dans Update (60 Hz)
        private float _lastLastInputShown = float.NaN;
        private bool _lastTrackedState = false;
        private bool _lastConnState = false;
        private int _lastBatteryShown = int.MinValue;
        private float _lastRssiShown = float.NaN;
        private long _lastBootShown = long.MinValue;
        private string _lastTitleShown;
        private bool[] _btnLastStates = new bool[4];

        private void UpdateUITK()
        {
            if (_panel == null) return;

            // Title — change rarement, compare strings ref-first
            string title = !string.IsNullOrEmpty(VaroniaInput.GetModel(_weaponIndex))
                ? VaroniaInput.GetModel(_weaponIndex) : "INPUT VARONIA";
            if (!ReferenceEquals(title, _lastTitleShown) && title != _lastTitleShown)
            {
                _lastTitleShown = title;
                _titleLabel.text = title;
            }

            // Boutons — pas de string alloc, mais on évite quand même de re-set la couleur identique
            bool isDebug = DebugModeOverlay.IsDebugMode;
            bool b0 = isDebug ? _mouseLeft   : VaroniaInput.GetButton(_weaponIndex, VaroniaButton.Primary);
            bool b1 = isDebug ? _mouseRight  : VaroniaInput.GetButton(_weaponIndex, VaroniaButton.Secondary);
            bool b2 = isDebug ? _mouseMiddle : VaroniaInput.GetButton(_weaponIndex, VaroniaButton.Tertiary);
            bool b3 = isDebug ? _mouseScroll : VaroniaInput.GetButton(_weaponIndex, VaroniaButton.Quaternary);
            UpdateBtn(0, b0, isDebug);
            UpdateBtn(1, b1, isDebug);
            UpdateBtn(2, b2, isDebug);
            UpdateBtn(3, b3, isDebug);

            // Last input — round à 0.1s
            float elapsed = _lastInputTime < 0f ? -1f : Mathf.Round((Time.unscaledTime - _lastInputTime) * 10f) / 10f;
            if (elapsed != _lastLastInputShown)
            {
                _lastLastInputShown = elapsed;
                _lastInputValue.text = elapsed < 0f ? "—" : elapsed.ToString("F1") + " s";
            }

            // Telemetry tracked
            bool isTracked = tracking != null && tracking.trackerFollower != null && tracking.trackerFollower.isTracking;
            if (isTracked != _lastTrackedState)
            {
                _lastTrackedState = isTracked;
                _trackedValue.text = isTracked ? "yes" : "no";
                _trackedValue.style.color = isTracked ? ColGood : ColBad;
            }

            // Telemetry connected
            bool isConn = VaroniaInput.GetIsConnected(_weaponIndex);
            if (isConn != _lastConnState)
            {
                _lastConnState = isConn;
                _connectedValue.text = isConn ? "connected" : "disconnected";
                _connectedValue.style.color = isConn ? ColGood : ColBad;
            }

            // Battery
            int battery = VaroniaInput.GetBattery(_weaponIndex);
            _batteryRow.style.display = battery != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (battery != 0 && battery != _lastBatteryShown)
            {
                _lastBatteryShown = battery;
                _batteryValue.text = battery + " %";
            }

            // RSSI — round à 0.01
            double rssi = VaroniaInput.GetRSSI(_weaponIndex);
            _rssiRow.style.display = rssi != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            float rssiRounded = (float)System.Math.Round(rssi, 2);
            if (rssi != 0 && rssiRounded != _lastRssiShown)
            {
                _lastRssiShown = rssiRounded;
                _rssiValue.text = rssiRounded.ToString("F2") + " dBm";
            }

            // Boot
            long boot = VaroniaInput.GetBootTime(_weaponIndex);
            _bootRow.style.display = boot != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (boot != 0 && boot != _lastBootShown)
            {
                _lastBootShown = boot;
                _bootValue.text = boot + " s";
            }
        }

        private void UpdateBtn(int i, bool isOn, bool isDebug)
        {
            // On évite l'assignment de style si l'état n'a pas changé.
            if (isOn == _btnLastStates[i]) return;
            _btnLastStates[i] = isOn;
            _btnEls[i].style.backgroundColor = isOn ? (isDebug ? ColBad : ColGood) : ColBtnOff;
        }

        private void BuildOverlay_UITK()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.sortingOrder = 100;

            var uiGo = new GameObject("[DebugInputOverlayUI]");
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
                    paddingLeft = 10, paddingRight = 10, paddingTop = 6, paddingBottom = 6,
                    borderTopLeftRadius = 4, borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                    display = show ? DisplayStyle.Flex : DisplayStyle.None,
                }
            };
            PositionPanel_UITK();
            _root.Add(_panel);

            _accent = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0, bottom = 0, width = 3, backgroundColor = ColGood }
            };
            _panel.Add(_accent);

            // Title
            _titleLabel = MakeLabel("INPUT VARONIA", 9, FontStyle.Bold, ColMuted, TextAnchor.MiddleLeft);
            _titleLabel.style.height = 18;
            _panel.Add(_titleLabel);

            // 4 boutons
            var btnRow = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style = { flexDirection = FlexDirection.Row, height = 28, marginTop = 4, justifyContent = Justify.SpaceBetween }
            };
            _panel.Add(btnRow);

            for (int i = 0; i < 4; i++)
            {
                var btn = new VisualElement
                {
                    pickingMode = PickingMode.Ignore,
                    style =
                    {
                        width = 28, height = 28,
                        backgroundColor = ColBtnOff,
                        alignItems = Align.Center, justifyContent = Justify.Center,
                    }
                };
                var lbl = MakeLabel((i + 1).ToString(), 13, FontStyle.Bold, ColValue, TextAnchor.MiddleCenter);
                btn.Add(lbl);
                btnRow.Add(btn);
                _btnEls[i]   = btn;
                _btnLabels[i] = lbl;
            }

            // Last input
            var lastLbl = MakeLabel("last input :", 9, FontStyle.Normal, ColMuted, TextAnchor.MiddleLeft);
            lastLbl.style.height = 16; lastLbl.style.marginTop = 6;
            _panel.Add(lastLbl);

            _lastInputValue = MakeLabel("—", 11, FontStyle.Bold, ColValue, TextAnchor.MiddleLeft);
            _lastInputValue.style.height = 16;
            _panel.Add(_lastInputValue);

            // Telemetry
            _panel.Add(MakeTelemetryRow("tracked :",   "no",           ColBad,   out _trackedValue));
            _panel.Add(MakeTelemetryRow("connected :", "disconnected", ColBad,   out _connectedValue));
            _batteryRow = MakeTelemetryRow("battery :", "—", ColValue, out _batteryValue);
            _batteryRow.style.display = DisplayStyle.None;
            _panel.Add(_batteryRow);
            _rssiRow = MakeTelemetryRow("rssi :", "—", ColValue, out _rssiValue);
            _rssiRow.style.display = DisplayStyle.None;
            _panel.Add(_rssiRow);
            _bootRow = MakeTelemetryRow("boot :", "—", ColValue, out _bootValue);
            _bootRow.style.display = DisplayStyle.None;
            _panel.Add(_bootRow);
        }

        private VisualElement MakeTelemetryRow(string labelText, string initialValue, Color valueColor, out Label valueLabel)
        {
            var row = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style = { flexDirection = FlexDirection.Row, height = 16, alignItems = Align.Center, marginTop = 2 }
            };
            var l = MakeLabel(labelText, 9, FontStyle.Normal, ColMuted, TextAnchor.MiddleLeft);
            l.style.width = 70;
            row.Add(l);
            valueLabel = MakeLabel(initialValue, 11, FontStyle.Bold, valueColor, TextAnchor.MiddleLeft);
            valueLabel.style.flexGrow = 1;
            row.Add(valueLabel);
            return row;
        }

        private static Label MakeLabel(string text, int fontSize, FontStyle fStyle, Color color, TextAnchor anchor)
        {
            return new Label(text)
            {
                pickingMode = PickingMode.Ignore,
                style = { fontSize = fontSize, color = color, unityFontStyleAndWeight = fStyle, unityTextAlign = anchor }
            };
        }

        private void PositionPanel_UITK()
        {
            if (_panel == null) return;
            float mx = margin.x, my = margin.y;
            _panel.style.width = size.x * scaleFactor;
            switch (corner)
            {
                case DisplayCorner.TopLeft:
                    _panel.style.left = mx; _panel.style.top = my;
                    _panel.style.right = StyleKeyword.Auto; _panel.style.bottom = StyleKeyword.Auto;
                    break;
                case DisplayCorner.TopRight:
                    _panel.style.right = mx; _panel.style.top = my;
                    _panel.style.left = StyleKeyword.Auto; _panel.style.bottom = StyleKeyword.Auto;
                    break;
                case DisplayCorner.BottomLeft:
                    _panel.style.left = mx; _panel.style.bottom = my;
                    _panel.style.right = StyleKeyword.Auto; _panel.style.top = StyleKeyword.Auto;
                    break;
                default:
                    _panel.style.right = mx; _panel.style.bottom = my;
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
        private GUIStyle  _titleStyle, _btnLabelStyle, _lastInputLabelStyle, _lastInputValueStyle;
        private Texture2D _bgTex, _accentTex, _btnOnTex, _btnOffTex, _btnFireTex;

        private void OnGUI()
        {
            if (!show) return;
            if (Event.current.type != EventType.Repaint) return;

            float scale = (Screen.height / 1080f) * scaleFactor;
            EnsureStyles(scale);

            Rect panel = GetPanelRect(scale);
            float W = panel.width, H = panel.height;

            GUI.DrawTexture(panel, _bgTex);
            GUI.DrawTexture(new Rect(panel.x, panel.y, 3f * scale, H), _accentTex);

            float lx  = panel.x + 10f * scale;
            float pad = 6f * scale;
            float titleH = 18f * scale;
            string title = !string.IsNullOrEmpty(VaroniaInput.GetModel(_weaponIndex))
                ? VaroniaInput.GetModel(_weaponIndex) : "INPUT VARONIA";
            GUI.Label(new Rect(lx, panel.y + pad, W - 14f * scale, titleH), title, _titleStyle);

            float btnAreaY = panel.y + pad + titleH + 4f * scale;
            float btnSize = 28f * scale;
            float btnSpacing = (W - 20f * scale - btnSize * 4f) / 3f;

            string[] labels = { "1", "2", "3", "4" };
            bool[] states = DebugModeOverlay.IsDebugMode
                ? new bool[] { _mouseLeft, _mouseRight, _mouseMiddle, _mouseScroll }
                : new bool[] {
                    VaroniaInput.GetButton(_weaponIndex, VaroniaButton.Primary),
                    VaroniaInput.GetButton(_weaponIndex, VaroniaButton.Secondary),
                    VaroniaInput.GetButton(_weaponIndex, VaroniaButton.Tertiary),
                    VaroniaInput.GetButton(_weaponIndex, VaroniaButton.Quaternary)
                };

            for (int i = 0; i < 4; i++)
            {
                bool isOn = states[i];
                float bx = lx + i * (btnSize + btnSpacing);
                Texture2D activeTex = isOn ? (DebugModeOverlay.IsDebugMode ? _btnFireTex : _btnOnTex) : _btnOffTex;
                GUI.DrawTexture(new Rect(bx, btnAreaY, btnSize, btnSize), activeTex);
                GUI.Label(new Rect(bx, btnAreaY, btnSize, btnSize), labels[i], _btnLabelStyle);
            }

            float lastY = btnAreaY + btnSize + 6f * scale;
            GUI.Label(new Rect(lx, lastY, W - 14f * scale, 16f * scale), "last input :", _lastInputLabelStyle);

            string lastVal = _lastInputTime < 0f
                ? "—" : (Time.unscaledTime - _lastInputTime).ToString("F1") + " s";
            GUI.Label(new Rect(lx, lastY + 16f * scale, W - 14f * scale, 16f * scale), lastVal, _lastInputValueStyle);

            float telY = lastY + 16f * scale + 18f * scale;
            bool isTracked = tracking != null && tracking.trackerFollower != null && tracking.trackerFollower.isTracking;
            DrawTelemetryRow(lx, telY, W, "tracked :", isTracked ? "yes" : "no", isTracked ? ColGood : ColBad, scale);
            telY += 16f * scale;

            string connLabel = VaroniaInput.GetIsConnected(_weaponIndex) ? "connected" : "disconnected";
            Color  connColor = VaroniaInput.GetIsConnected(_weaponIndex) ? ColGood : ColBad;
            DrawTelemetryRow(lx, telY, W, "connected :", connLabel, connColor, scale);
            telY += 16f * scale;

            if (VaroniaInput.GetBattery(_weaponIndex) != 0)
            {
                DrawTelemetryRow(lx, telY, W, "battery :", VaroniaInput.GetBattery(_weaponIndex) + " %", ColValue, scale);
                telY += 16f * scale;
            }
            if (VaroniaInput.GetRSSI(_weaponIndex) != 0)
            {
                DrawTelemetryRow(lx, telY, W, "rssi :", VaroniaInput.GetRSSI(_weaponIndex).ToString("F2") + " dBm", ColValue, scale);
                telY += 16f * scale;
            }
            if (VaroniaInput.GetBootTime(_weaponIndex) != 0)
            {
                DrawTelemetryRow(lx, telY, W, "boot :", VaroniaInput.GetBootTime(_weaponIndex) + " s", ColValue, scale);
                telY += 16f * scale;
            }
        }

        private void DrawTelemetryRow(float x, float y, float W, string label, string value, Color valueColor, float scale)
        {
            GUI.Label(new Rect(x, y, 60f * scale, 15f * scale), label, _lastInputLabelStyle);
            GUIStyle valStyle = new GUIStyle(_lastInputValueStyle) { normal = { textColor = valueColor } };
            GUI.Label(new Rect(x + 60f * scale, y, W - 74f * scale, 15f * scale), value, valStyle);
        }

        private Rect GetPanelRect(float scale)
        {
            float w = size.x * scale, h = size.y * scale;
            float x, y;
            float mx = margin.x * scale, my = margin.y * scale;
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
            _lastScale = scale;

            if (_bgTex == null)     _bgTex     = MakeTex(ColBg);
            if (_accentTex == null) _accentTex = MakeTex(ColGood);
            if (_btnOnTex == null)  _btnOnTex  = MakeTex(ColGood);
            if (_btnOffTex == null) _btnOffTex = MakeTex(ColBtnOff);
            if (_btnFireTex == null) _btnFireTex = MakeTex(ColBad);

            _titleStyle = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(9 * scale), fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft, normal = { textColor = ColMuted },
            };
            _btnLabelStyle = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(13 * scale), fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter, normal = { textColor = ColValue },
            };
            _lastInputLabelStyle = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(9 * scale), fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft, normal = { textColor = ColMuted },
            };
            _lastInputValueStyle = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(11 * scale), fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft, normal = { textColor = ColValue },
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
