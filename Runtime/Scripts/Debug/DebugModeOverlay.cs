using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VaroniaBackOffice
{
    /// <summary>
    /// Overlay 2D Screen Space affiché en haut à droite.
    /// F10 (1er appui)  : active le mode debug  (IsDebugMode = true).
    /// F10 (2e appui)   : active le super debug  (IsSuperDebugMode = true) → force les debugRender de _Weapon.
    /// F10 (3e appui)   : tout désactive.
    /// Style identique aux autres widgets Varonia (OnGUI, même palette de couleurs).
    /// </summary>
    public class DebugModeOverlay : MonoBehaviour
    {
        // ─── Variables statiques publiques ────────────────────────────────────────

        public static bool IsDebugMode      { get; private set; } = false;
        public static bool IsSuperDebugMode { get; private set; } = false;

        public static event Action<bool> OnSuperDebugChanged;
        public static event Action<bool> OnDebugChanged;

        // ─── Config ───────────────────────────────────────────────────────────────

        [Header("Display")]
        [SerializeField] private float   margin = 12f;
        [SerializeField] private Vector2 size   = new Vector2(160f, 52f);

        [Header("Toggle Key")]
        [Tooltip("Touche pour cycler entre les modes debug. Défaut : F10")]
    #if ENABLE_INPUT_SYSTEM
        private Key toggleKey_ = Key.F10;
    #else
        private KeyCode toggleKey_ = KeyCode.F10;
    #endif

        // ─── Colors (même palette que VaroniaFPSDisplay / WorldSpaceDebugUI) ─────

        static readonly Color ColBg      = new Color(0.11f, 0.11f, 0.14f, 0.92f);
        static readonly Color ColGood    = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color ColSuper   = new Color(1.00f, 0.75f, 0.20f, 1f);
        static readonly Color ColBad     = new Color(1.00f, 0.40f, 0.40f, 1f);
        static readonly Color ColMuted   = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color ColValue   = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color ColDivider = new Color(0.25f, 0.25f, 0.30f, 1f);

        // ─── Styles ───────────────────────────────────────────────────────────────

        private bool      _stylesBuilt;
        private GUIStyle  _labelStyle;
        private GUIStyle  _valueStyle;
        private GUIStyle  _keyStyle;
        private GUIStyle  _descStyle;
        private Texture2D _bgTex;
        private Texture2D _accentTex;
        private Texture2D _dividerTex;
        private Color     _lastAccentColor;

        // ─── Keybinds à afficher ──────────────────────────────────────────────────

        static readonly (string key, string desc)[] BindingsDebug =
        {
        };

        static readonly (string key, string desc)[] BindingsSuperDebug =
        {
            ("ZQSD",   "move"),
            ("R / F",  "up / down"),
            ("A / E",  "rotate"),
            ("SHIFT",  "fast"),
            ("T",      "reset pos"),
            ("NUM +/-", "timescale ±0.1"),
            ("NUM *",   "timescale +1"),
            ("NUM Enter", "timescale reset"),
        };

        // ─────────────────────────────────────────────────────────────────────────

        private void OnDestroy()
        {
            if (_bgTex)     Destroy(_bgTex);
            if (_accentTex) Destroy(_accentTex);
            if (_dividerTex) Destroy(_dividerTex);
        }

        
        
        
        
        
        
        
        
        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && IsSuperDebugMode)
            {
                var kb = Keyboard.current;
                if (kb.numpadPlusKey.wasPressedThisFrame)
                    Time.timeScale = Mathf.Round((Time.timeScale + 0.1f) * 10f) / 10f;
                else if (kb.numpadMinusKey.wasPressedThisFrame)
                    Time.timeScale = Mathf.Max(0f, Mathf.Round((Time.timeScale - 0.1f) * 10f) / 10f);
                else if (kb.numpadMultiplyKey.wasPressedThisFrame)
                    Time.timeScale = Mathf.Round((Time.timeScale + 1f) * 10f) / 10f;
                else if (kb.numpadEnterKey.wasPressedThisFrame)
                    Time.timeScale = 1f;
            }

            if (Keyboard.current == null || !Keyboard.current[toggleKey_].wasPressedThisFrame)
                return;
#else
            if (IsSuperDebugMode)
            {
                if (Input.GetKeyDown(KeyCode.KeypadPlus))
                    Time.timeScale = Mathf.Round((Time.timeScale + 0.1f) * 10f) / 10f;
                else if (Input.GetKeyDown(KeyCode.KeypadMinus))
                    Time.timeScale = Mathf.Max(0f, Mathf.Round((Time.timeScale - 0.1f) * 10f) / 10f);
                else if (Input.GetKeyDown(KeyCode.KeypadMultiply))
                    Time.timeScale = Mathf.Round((Time.timeScale + 1f) * 10f) / 10f;
                else if (Input.GetKeyDown(KeyCode.KeypadEnter))
                    Time.timeScale = 1f;
            }

            if (!Input.GetKeyDown(toggleKey_))
                return;
#endif

            if (!IsDebugMode)
            {
                // OFF → DEBUG
                IsDebugMode      = true;
                IsSuperDebugMode = false;
                OnDebugChanged?.Invoke(true);
                Debug.Log("[DebugModeOverlay] Mode : DEBUG");
            }
            else if (!IsSuperDebugMode)
            {
                // DEBUG → SUPER DEBUG
                IsSuperDebugMode = true;
             
                OnSuperDebugChanged?.Invoke(true);
                Debug.Log("[DebugModeOverlay] Mode : SUPER DEBUG");
            }
            else
            {
                // SUPER DEBUG → OFF
                IsSuperDebugMode = false;
                IsDebugMode      = false;
                OnSuperDebugChanged?.Invoke(false);
                OnDebugChanged?.Invoke(false);
                Debug.Log("[DebugModeOverlay] Mode : OFF");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!IsDebugMode) return;

            EnsureStyles();

            Color accent = IsSuperDebugMode ? ColSuper : ColGood;

            if (accent != _lastAccentColor)
            {
                _lastAccentColor = accent;
                _accentTex.SetPixel(0, 0, accent);
                _accentTex.Apply();
            }

            // ── Calcul hauteur dynamique ──
            const float headerH   = 52f;
            const float rowH      = 16f;
            const float divH      = 1f;
            const float divGap    = 6f;
            const float bottomPad = 6f;
            var activeBindings = IsSuperDebugMode ? BindingsSuperDebug : BindingsDebug;
            float extraRows = IsSuperDebugMode ? 1f : 0f;
            float totalH = headerH + divH + divGap + (activeBindings.Length + extraRows) * rowH + bottomPad;

            Rect panel = new Rect(Screen.width - size.x - margin, margin, size.x, totalH);
            float H = panel.height;

            // ── Background ──
            GUI.DrawTexture(panel, _bgTex);

            // ── Left accent bar (3px) ──
            GUI.DrawTexture(new Rect(panel.x, panel.y, 3f, H), _accentTex);

            // ── Header : DEBUG MODE / ON ou SUPER DEBUG ──
            float lx    = panel.x + 10f;
            float lw    = panel.width - 14f;
            float hRowH = headerH * 0.45f;
            float pad   = headerH * 0.07f;

            _labelStyle.normal.textColor = ColMuted;
            GUI.Label(new Rect(lx, panel.y + pad, lw, hRowH), "DEBUG MODE", _labelStyle);

            _valueStyle.normal.textColor = accent;
            string modeLabel = IsSuperDebugMode ? "SUPER" : "ON";
            GUI.Label(new Rect(lx, panel.y + pad + hRowH, lw, hRowH), modeLabel, _valueStyle);

            // ── Divider ──
            float divY = panel.y + headerH;
            GUI.DrawTexture(new Rect(panel.x + 10f, divY, panel.width - 20f, divH), _dividerTex);

            // ── Keybinds ──
            float ky    = divY + divGap;
            float keyW  = 52f;
            float descX = lx + keyW + 4f;
            float descW = lw - keyW - 4f;

            foreach (var (key, desc) in activeBindings)
            {
                _keyStyle.normal.textColor  = ColValue;
                _descStyle.normal.textColor = ColMuted;
                GUI.Label(new Rect(lx,    ky, keyW,  rowH), key,  _keyStyle);
                GUI.Label(new Rect(descX, ky, descW, rowH), desc, _descStyle);
                ky += rowH;
            }

            if (IsSuperDebugMode)
            {
                _keyStyle.normal.textColor  = ColSuper;
                _descStyle.normal.textColor = ColValue;
                GUI.Label(new Rect(lx,    ky, keyW,  rowH), "Time Scale ",                          _keyStyle);
                GUI.Label(new Rect(descX, ky, descW, rowH), Time.timeScale.ToString("F1"), _descStyle);
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

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

            _lastAccentColor = ColGood;

            _labelStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColMuted },
            };

            _valueStyle = new GUIStyle
            {
                fontSize  = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColGood },
            };

            _keyStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColValue },
            };

            _descStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = ColMuted },
            };
        }
    }
}
