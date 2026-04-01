using System.Collections;
using uPLibrary.Networking.M2Mqtt;
using UnityEngine;
#if STEAMVR_ENABLED
using Valve.VR;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VaroniaBackOffice
{
    /// <summary>
    /// Lightweight info widget: game version, package version, MQTT status.
    /// Rendered via OnGUI — no Canvas or prefab required.
    /// </summary>
    public class VaroniaInfoDisplay : MonoBehaviour
    {
        // ─── Config ───────────────────────────────────────────────────────────────

        public enum DisplayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Display")]
        [SerializeField] private DisplayCorner corner = DisplayCorner.BottomLeft;
        [SerializeField] private float         margin = 12f;
        [SerializeField] private Vector2       size   = new Vector2(200f, 130f);

        [Header("Camera")]
        [SerializeField] private Camera _camera;

        // ─── Colors ───────────────────────────────────────────────────────────────

        static readonly Color ColBg     = new Color(0.11f, 0.11f, 0.14f, 0.92f);
        static readonly Color ColGood   = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color ColBad    = new Color(1.00f, 0.40f, 0.40f, 1f);
        static readonly Color ColMuted  = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color ColValue  = new Color(0.92f, 0.92f, 0.95f, 1f);

        // ─── Cached data ──────────────────────────────────────────────────────────

        private string _gameVersion;
        private string _packageVersion;
        private string _headsetName;

        // ─── Styles ───────────────────────────────────────────────────────────────

        private bool      _stylesBuilt;
        private GUIStyle  _bgStyle;
        private GUIStyle  _labelStyle;
        private GUIStyle  _valueStyle;
        private GUIStyle  _pillStyle;
        private Texture2D _bgTex;
        private Texture2D _accentTex;
        private Texture2D _pillGoodTex;
        private Texture2D _pillBadTex;

        // ─────────────────────────────────────────────────────────────────────────

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
           
#if STEAMVR_ENABLED
            CVRSystem vr = SteamVRBridge.GetSystem();
            if (vr != null)
            {
                var sb = new System.Text.StringBuilder(256);
                ETrackedPropertyError err = ETrackedPropertyError.TrackedProp_Success;
                vr.GetStringTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_ModelNumber_String, sb, 256, ref err);
                _headsetName = sb.Length > 0 ? sb.ToString() : "—";
                
                if(_headsetName == "Miramar" || _headsetName == "Oculus Quest2")
                {
                    _headsetName = "Pico 4 Ultra";
                }
                
                if(_headsetName == "Vive VBStreaming Focus3")
                {
                    _headsetName = "Vive Focus 3";
                }
            }
            else
            {
  #endif
                var headsets = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
                UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                    UnityEngine.XR.InputDeviceCharacteristics.HeadMounted, headsets);
                _headsetName = headsets.Count > 0 ? headsets[0].name : "—";
#if STEAMVR_ENABLED
            }
#endif
        }

        
        private void OnDestroy()
        {
            if (_bgTex      != null) Destroy(_bgTex);
            if (_accentTex  != null) Destroy(_accentTex);
            if (_pillGoodTex!= null) Destroy(_pillGoodTex);
            if (_pillBadTex != null) Destroy(_pillBadTex);
        }

        // ─── Rendering ────────────────────────────────────────────────────────────

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            bool f8 = kb != null && kb[Key.F8].wasPressedThisFrame;
#else
            bool f8 = Input.GetKeyDown(KeyCode.F8);
#endif
            if (f8)
            {
                if (BackOfficeVaronia.Instance != null)
                {
                    BackOfficeVaronia.Instance.config.hideMode ++;
                    
                    if(BackOfficeVaronia.Instance.config.hideMode > 2)
                        BackOfficeVaronia.Instance.config.hideMode = 0;
                    
                    BackOfficeVaronia.RaiseMovieChanged();
                    Debug.Log($"[VaroniaInfoDisplay] Hide mode: {BackOfficeVaronia.Instance.config.hideMode}");
                }
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
        }

        bool show;
        
        private void OnGUI()
        {
            if(!show) return;
            
            EnsureStyles();

            bool mqttConnected = MqttClient.IsConnected__;
            Rect panel = GetPanelRect();

            // ── Background ──
            GUI.DrawTexture(panel, _bgTex);

            // ── Left accent bar ──
            GUI.DrawTexture(
                new Rect(panel.x, panel.y, 3f, panel.height),
                mqttConnected ? _accentTex : _pillBadTex
            );

            float rowH = panel.height / 6f;
            float textX = panel.x + 12f;
            float textW = panel.width - 60f;
            float valX  = panel.x + panel.width - 90f;
            float valW  = 86f;

            // ── Row 1 : Game ──
            GUI.Label(new Rect(textX, panel.y + rowH * 0f + 4f, textW, rowH), "GAME", _labelStyle);
            GUI.Label(new Rect(valX,  panel.y + rowH * 0f + 4f, valW,  rowH), _gameVersion, _valueStyle);

            // ── Row 2 : Back Office ──
            GUI.Label(new Rect(textX, panel.y + rowH * 1f,       textW, rowH), "BACK OFFICE", _labelStyle);
            GUI.Label(new Rect(valX,  panel.y + rowH * 1f,       valW,  rowH), _packageVersion, _valueStyle);

            // ── Row 3 : Estimated Height ──
            string camY = "—";
            if (_camera == null) _camera = Camera.main;
            if (_camera != null && BackOfficeVaronia.Instance !=null &&  BackOfficeVaronia.Instance.Rig != null) camY = (_camera.transform.localPosition.y + 0.09f+BackOfficeVaronia.Instance.Rig.localPosition.y).ToString("F2") + " m";
            GUI.Label(new Rect(textX, panel.y + rowH * 2f,       textW, rowH), "CURRENT HEIGHT", _labelStyle);
            GUI.Label(new Rect(valX,  panel.y + rowH * 2f,       valW,  rowH), camY, _valueStyle);

            // ── Row 4 : Player Size (AutoSizing) ──
            string playerSizeStr = AutoSizing.Player_Size > 0.1f ? AutoSizing.Player_Size.ToString("F2") + " m" : "—";
            GUI.Label(new Rect(textX, panel.y + rowH * 3f,       textW, rowH), "PLAYER SIZE", _labelStyle);
            GUI.Label(new Rect(valX,  panel.y + rowH * 3f,       valW,  rowH), playerSizeStr, _valueStyle);

            // ── Row 5 : Headset ──
            GUI.Label(new Rect(textX, panel.y + rowH * 4f,       textW, rowH), "HEADSET", _labelStyle);
            GUI.Label(new Rect(valX,  panel.y + rowH * 4f,       valW,  rowH), _headsetName ?? "—", _valueStyle);

            // ── Row 6 : MQTT pill ──
            GUI.Label(new Rect(textX, panel.y + rowH * 5f - 2f, textW, rowH), "MQTT", _labelStyle);

            _pillStyle.normal.background = mqttConnected ? _pillGoodTex : _pillBadTex;
            _pillStyle.normal.textColor  = mqttConnected ? ColGood      : ColBad;
            GUI.Label(
                new Rect(valX, panel.y + rowH * 5f,  valW, rowH - 4f),
                mqttConnected ? "CONNECTED" : "OFFLINE",
                _pillStyle
            );
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
                default: // BottomRight
                    x = Screen.width - w - margin; y = Screen.height - h - margin; break;
            }
            return new Rect(x, y, w, h);
        }

        private void EnsureStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            _bgTex = MakeTex(ColBg);
            _accentTex   = MakeTex(ColGood);
            _pillGoodTex = MakeTex(new Color(ColGood.r, ColGood.g, ColGood.b, 0.15f));
            _pillBadTex  = MakeTex(new Color(ColBad.r,  ColBad.g,  ColBad.b,  0.15f));

            _bgStyle = new GUIStyle { normal = { background = _bgTex } };

            _labelStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColMuted },
            };

            _valueStyle = new GUIStyle
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = ColValue },
            };

            _pillStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = ColGood, background = _pillGoodTex },
                padding   = new RectOffset(4, 4, 2, 2),
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
    }
}