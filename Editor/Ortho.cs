using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VaroniaBackOffice
{
    public class Ortho : EditorWindow
    {
        // ── Output path — shared key with VaroniaInfoUI_Addon ─────────────────────
        const string k_PathKey = "VBO_OrthoSourcePath";
        static string OrthoSourcePath => EditorPrefs.GetString(k_PathKey);

        // ── Modes ─────────────────────────────────────────────────────────────────
        enum OrthoMode { Start, Setup, Paint }
        OrthoMode _mode = OrthoMode.Start;

        // ── Camera ────────────────────────────────────────────────────────────────
        float  _orthoSize    = 10f;
        float  _cameraHeight = 50f;
        Camera _tempCam;

        // ── Paint tools ───────────────────────────────────────────────────────────
        enum PaintTool { Brush, Line, Rect, Eraser }
        PaintTool _activeTool = PaintTool.Brush;

        // ── Paint state ───────────────────────────────────────────────────────────
        Texture2D _capturedTex;
        Color     _brushColor = Color.red;
        int       _brushSize  = 8;
        bool      _texDirty;
        Rect      _paintDispRect;

        Color32[]              _originalPixels;
        readonly List<Color32[]> _undoHistory = new List<Color32[]>();
        const int k_MaxUndo = 20;

        bool       _shapeDragging;
        Vector2Int _shapeStartPx;
        Color32[]  _shapeBasePixels;

        bool       _brushStroking;
        Vector2Int _lastBrushPx;

        // ── Cursor state ──────────────────────────────────────────────────────────
        bool _cursorOverPaint;                    // true = custom cursor is active
        static Texture2D _cursorDraw, _cursorErase;

        // ── Style cache ───────────────────────────────────────────────────────────
        static bool     _stylesBuilt;
        static GUIStyle _headerStyle, _sectionStyle, _footerStyle;
        static GUIStyle _buttonStyle, _sliderLabelStyle, _sliderValueStyle, _tbLabelStyle;

        // ── Tool icon cache ───────────────────────────────────────────────────────
        static Texture2D _iconLine, _iconRect;

        // ── Colors ────────────────────────────────────────────────────────────────
        static readonly Color colBg          = new Color(0.11f, 0.11f, 0.14f, 1f);
        static readonly Color colCard        = new Color(0.15f, 0.15f, 0.19f, 1f);
        static readonly Color colAccent      = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color colAccentDim   = new Color(0.30f, 0.85f, 0.65f, 0.15f);
        static readonly Color colWarn        = new Color(1f,    0.75f, 0.30f, 1f);
        static readonly Color colError       = new Color(1f,    0.40f, 0.40f, 1f);
        static readonly Color colTextPrimary = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color colTextSecond  = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color colTextMuted   = new Color(0.40f, 0.40f, 0.47f, 1f);
        static readonly Color colDivider     = new Color(1f,    1f,    1f,    0.06f);
        static readonly Color colBtnNormal   = new Color(0.22f, 0.22f, 0.28f, 1f);
        static readonly Color colBtnHover    = new Color(0.28f, 0.28f, 0.35f, 1f);

        // ── UI textures ───────────────────────────────────────────────────────────
        static Texture2D _texBtn, _texBtnHover, _texAccentSolid;

        // ─────────────────────────────────────────────────────────────────────────

        [MenuItem("Varonia/Ortho View")]
        public static void ShowWindow()
        {
            var w = GetWindow<Ortho>("Ortho Capture");
            w.titleContent = new GUIContent("Ortho Capture");
            w.minSize = w.maxSize = new Vector2(480, 290);
        }

        void OnEnable()          => _stylesBuilt = false;
        void OnInspectorUpdate() => Repaint();
        void OnLostFocus()       => ResetCursor();

        // ─── Full-screen helpers ──────────────────────────────────────────────────

        // Return the Unity Editor's main window rect via reflection (ContainerWindow, ShowMode 4).
        // This works for both docked and floating layouts, in all recent Unity versions.
        static Rect GetMainWindowRect()
        {
            try
            {
                var T = typeof(EditorWindow).Assembly.GetType("UnityEditor.ContainerWindow");
                if (T == null) goto fallback;
                var fShow = T.GetField("m_ShowMode",
                                System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Instance);
                var pPos  = T.GetProperty("position",
                                System.Reflection.BindingFlags.Public |
                                System.Reflection.BindingFlags.Instance);
                if (fShow == null || pPos == null) goto fallback;

                foreach (Object win in Resources.FindObjectsOfTypeAll(T))
                    if ((int)fShow.GetValue(win) == 4)   // 4 = MainWindow
                        return (Rect)pPos.GetValue(win);
            }
            catch { /* reflection unavailable in this Unity build */ }

            fallback:
            return new Rect(0, 0, Screen.currentResolution.width,
                                  Screen.currentResolution.height);
        }

        // Resize/reposition this EditorWindow to cover the entire Unity editor.
        void GoFullScreen()
        {
            Rect r  = GetMainWindowRect();
            minSize = new Vector2(200, 200);
            maxSize = new Vector2(r.width + 100, r.height + 100);
            position = r;
        }

        // Restore window to the given fixed size after leaving paint mode.
        void GoWindowed(float w, float h)
        {
            var size = new Vector2(w, h);
            minSize = maxSize = size;
            // Re-centre on screen if needed
            position = new Rect(
                position.x + (position.width  - w) * 0.5f,
                position.y + (position.height - h) * 0.5f,
                w, h);
        }

        // ─── Custom cursor ─────────────────────────────────────────────────────────

        // Draw cursor (24×24) — thin crosshair with a small centre circle.
        // Clearly different from any default Unity arrow.
        static Texture2D MakeDrawCursorTex()
        {
            const int S = 24, C = 11;
            var t  = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            Color32 tr = new Color32(0,   0,   0,   0);
            Color32 wh = new Color32(255, 255, 255, 230);
            Color32 bk = new Color32(0,   0,   0,   180);
            for (int i = 0; i < px.Length; i++) px[i] = tr;

            // Horizontal arm (gap 4 px at centre)
            for (int x = 0; x < S; x++)
            {
                if (x >= C - 2 && x <= C + 2) continue;  // gap
                if (C - 1 >= 0) px[(C - 1) * S + x] = bk;
                px[C * S + x]                         = wh;
                if (C + 1 < S)  px[(C + 1) * S + x] = bk;
            }
            // Vertical arm
            for (int y = 0; y < S; y++)
            {
                if (y >= C - 2 && y <= C + 2) continue;  // gap
                if (C - 1 >= 0) px[y * S + (C - 1)] = bk;
                px[y * S + C]                         = wh;
                if (C + 1 < S)  px[y * S + (C + 1)] = bk;
            }
            // Centre dot (3×3)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                px[(C + dy) * S + (C + dx)] = wh;

            t.SetPixels32(px); t.Apply();
            t.filterMode = FilterMode.Point;
            t.hideFlags  = HideFlags.HideAndDontSave;
            return t;
        }

        // Erase cursor (24×24) — circle outline with a diagonal slash ( ⊘ ).
        static Texture2D MakeEraseCursorTex()
        {
            const int S = 24, C = 11;
            var t  = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            Color32 tr = new Color32(0,   0,   0,   0);
            Color32 wh = new Color32(255, 255, 255, 230);
            Color32 bk = new Color32(0,   0,   0,   180);
            for (int i = 0; i < px.Length; i++) px[i] = tr;

            // Circle outline (radius 9, 1-pixel wide with black border)
            const int R = 9;
            for (float a = 0; a < 360f; a += 1.5f)
            {
                float rad = a * Mathf.Deg2Rad;
                int cx = C + Mathf.RoundToInt((R - 1) * Mathf.Cos(rad));
                int cy = C + Mathf.RoundToInt((R - 1) * Mathf.Sin(rad));
                int fx = C + Mathf.RoundToInt(R       * Mathf.Cos(rad));
                int fy = C + Mathf.RoundToInt(R       * Mathf.Sin(rad));
                if (cx >= 0 && cx < S && cy >= 0 && cy < S) px[cy * S + cx] = bk;
                if (fx >= 0 && fx < S && fy >= 0 && fy < S) px[fy * S + fx] = wh;
            }
            // Diagonal slash (top-right → bottom-left), 2 px wide
            for (int i = -7; i <= 7; i++)
            {
                int x = Mathf.Clamp(C + i,     0, S - 1);
                int y = Mathf.Clamp(C - i,     0, S - 1);
                int x2= Mathf.Clamp(C + i + 1, 0, S - 1);
                px[y * S + x]  = wh;
                px[y * S + x2] = wh;
            }

            t.SetPixels32(px); t.Apply();
            t.filterMode = FilterMode.Point;
            t.hideFlags  = HideFlags.HideAndDontSave;
            return t;
        }

        static Texture2D DrawCursorTex
        {
            get { if (_cursorDraw  == null) _cursorDraw  = MakeDrawCursorTex();  return _cursorDraw;  }
        }
        static Texture2D EraseCursorTex
        {
            get { if (_cursorErase == null) _cursorErase = MakeEraseCursorTex(); return _cursorErase; }
        }

        void SetPaintCursor()
        {
            _cursorOverPaint = true;
            var tex  = _activeTool == PaintTool.Eraser ? EraseCursorTex : DrawCursorTex;
            Cursor.SetCursor(tex, new Vector2(11, 11), CursorMode.ForceSoftware);
        }

        void ResetCursor()
        {
            if (!_cursorOverPaint) return;
            _cursorOverPaint = false;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        // ── Texture helpers ───────────────────────────────────────────────────────

        static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        static Texture2D MakeRoundedTex(int w, int h, Color col, int radius)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool inside = true;
                if      (x < radius      && y < radius)
                    inside = new Vector2(x - radius,           y - radius).magnitude           <= radius;
                else if (x >= w - radius && y < radius)
                    inside = new Vector2(x - (w - radius - 1), y - radius).magnitude           <= radius;
                else if (x < radius      && y >= h - radius)
                    inside = new Vector2(x - radius,           y - (h - radius - 1)).magnitude <= radius;
                else if (x >= w - radius && y >= h - radius)
                    inside = new Vector2(x - (w - radius - 1), y - (h - radius - 1)).magnitude <= radius;
                t.SetPixel(x, y, inside ? col : clear);
            }
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        // ── Tool icon generators ──────────────────────────────────────────────────

        static Texture2D MakeLineIconTex()
        {
            const int S = 20;
            var t  = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            Color32 tr = new Color32(0,   0,   0,   0);
            Color32 wh = new Color32(210, 215, 220, 255);
            for (int i = 0; i < px.Length; i++) px[i] = tr;
            int x0 = 2, y0 = 2, x1 = S - 3, y1 = S - 3;
            int ddx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int ddy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = ddx + ddy;
            while (true)
            {
                void Set(int xx, int yy) { if (xx>=0&&xx<S&&yy>=0&&yy<S) px[yy*S+xx]=wh; }
                Set(x0,y0); Set(x0+1,y0); Set(x0,y0+1);
                if (x0==x1&&y0==y1) break;
                int e2=2*err;
                if (e2>=ddy){err+=ddy;x0+=sx;}
                if (e2<=ddx){err+=ddx;y0+=sy;}
            }
            t.SetPixels32(px); t.Apply();
            t.filterMode = FilterMode.Point;
            t.hideFlags  = HideFlags.HideAndDontSave;
            return t;
        }

        static Texture2D MakeRectIconTex()
        {
            const int S = 20;
            var t  = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            Color32 tr = new Color32(0,   0,   0,   0);
            Color32 wh = new Color32(210, 215, 220, 255);
            for (int i = 0; i < px.Length; i++) px[i] = tr;
            const int m = 3;
            for (int x = m; x <= S-m-1; x++) { px[m*S+x]=wh; px[(S-m-1)*S+x]=wh; }
            for (int y = m; y <= S-m-1; y++) { px[y*S+m]=wh; px[y*S+(S-m-1)]=wh; }
            t.SetPixels32(px); t.Apply();
            t.filterMode = FilterMode.Point;
            t.hideFlags  = HideFlags.HideAndDontSave;
            return t;
        }

        static GUIContent ToolContent(PaintTool tool)
        {
            switch (tool)
            {
                case PaintTool.Brush:
                {
                    var gc = EditorGUIUtility.IconContent("d_Grid.PaintTool");
                    return gc?.image != null ? new GUIContent(gc.image,"Brush") : new GUIContent("✏","Brush");
                }
                case PaintTool.Line:
                {
                    if (_iconLine == null) _iconLine = MakeLineIconTex();
                    return new GUIContent(_iconLine, "Line  (drag)");
                }
                case PaintTool.Rect:
                {
                    if (_iconRect == null) _iconRect = MakeRectIconTex();
                    return new GUIContent(_iconRect, "Rectangle  (drag)");
                }
                case PaintTool.Eraser:
                {
                    var gc = EditorGUIUtility.IconContent("d_Grid.EraserTool");
                    return gc?.image != null
                        ? new GUIContent(gc.image, "Eraser  (restores original)")
                        : new GUIContent("⌫",      "Eraser  (restores original)");
                }
                default: return GUIContent.none;
            }
        }

        // ── Style builder ─────────────────────────────────────────────────────────

        void BuildStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            _texBtn         = MakeRoundedTex(32, 32, colBtnNormal, 5);
            _texBtnHover    = MakeRoundedTex(32, 32, colBtnHover,  5);
            _texAccentSolid = MakeRoundedTex(32, 32, colAccent,    5);

            _headerStyle = new GUIStyle { fontSize=18, fontStyle=FontStyle.Bold,
                alignment=TextAnchor.MiddleCenter, normal={textColor=colTextPrimary} };
            _sectionStyle = new GUIStyle { fontSize=10, fontStyle=FontStyle.Bold,
                normal={textColor=colTextMuted},
                padding=new RectOffset(0,0,6,2), margin=new RectOffset(0,0,4,0) };
            _footerStyle = new GUIStyle { fontSize=9, normal={textColor=colTextMuted},
                alignment=TextAnchor.MiddleCenter, padding=new RectOffset(0,0,6,6) };
            _buttonStyle = new GUIStyle { fontSize=11, fontStyle=FontStyle.Bold,
                alignment=TextAnchor.MiddleCenter,
                normal ={textColor=colTextPrimary, background=_texBtn},
                hover  ={textColor=Color.white,   background=_texBtnHover},
                active ={textColor=Color.white,   background=_texAccentSolid},
                padding=new RectOffset(16,16,8,8), margin=new RectOffset(2,2,2,2),
                border =new RectOffset(5,5,5,5) };
            _sliderLabelStyle = new GUIStyle { fontSize=11,
                normal={textColor=colTextSecond}, padding=new RectOffset(0,0,3,3) };
            _sliderValueStyle = new GUIStyle { fontSize=10, fontStyle=FontStyle.Bold,
                alignment=TextAnchor.MiddleRight,
                normal={textColor=colAccent}, padding=new RectOffset(4,0,3,3) };
            _tbLabelStyle = new GUIStyle { fontSize=10, normal={textColor=colTextMuted},
                padding=new RectOffset(0,4,5,0), alignment=TextAnchor.MiddleLeft };
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────────

        void OnGUI()
        {
            BuildStyles();

            if (_texDirty && _capturedTex != null) { _capturedTex.Apply(); _texDirty = false; }

            if (_mode == OrthoMode.Paint)
            {
                Event ev = Event.current;
                if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Z && ev.control)
                { PerformUndo(); ev.Use(); }
            }

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colBg);

            switch (_mode)
            {
                case OrthoMode.Start: DrawStartScreen(); break;
                case OrthoMode.Setup: DrawSetupScreen(); break;
                case OrthoMode.Paint: DrawPaintScreen(); break;
            }
        }

        // ─── Start screen ─────────────────────────────────────────────────────────

        void DrawStartScreen()
        {
            ResizeTo(480, 290);

            EditorGUILayout.Space(16);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.Label("ORTHO CAPTURE", _headerStyle);
            GUILayout.Space(16);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            DrawCard(() =>
            {
                DrawSectionLabel("ABOUT");
                EditorGUILayout.Space(4); DrawDivider(); EditorGUILayout.Space(8);
                var infoStyle = new GUIStyle { fontSize=11, wordWrap=true,
                    normal={textColor=colTextSecond}, padding=new RectOffset(0,0,0,2) };
                GUILayout.Label(
                    "Places a temporary orthographic camera above the scene. " +
                    "Adjust zoom and altitude, capture a 1920×1080 JPEG, " +
                    "then annotate with the built-in paint tool before saving.",
                    infoStyle);
                EditorGUILayout.Space(6);
                string p = OrthoSourcePath;
                var pathStyle = new GUIStyle { fontSize=10, wordWrap=false,
                    normal={textColor=string.IsNullOrEmpty(p)?colTextMuted:colAccent},
                    padding=new RectOffset(0,0,0,2) };
                GUILayout.Label("💾  " + (string.IsNullOrEmpty(p)
                    ? "Assets/  (default — configure OrthoSourcePath in VBO Settings)"
                    : p), pathStyle);
                EditorGUILayout.Space(2);
            }, colAccent);

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            var startStyle = new GUIStyle { fontSize=15, fontStyle=FontStyle.Bold,
                alignment=TextAnchor.MiddleCenter, border=new RectOffset(8,8,8,8),
                normal={textColor=Color.white, background=MakeRoundedTex(32,32,new Color(colAccent.r,colAccent.g,colAccent.b,.75f),8)},
                hover ={textColor=Color.white, background=MakeRoundedTex(32,32,colAccent,8)},
                active={textColor=Color.white, background=MakeRoundedTex(32,32,new Color(colAccent.r*.75f,colAccent.g*.75f,colAccent.b*.75f,1f),8)} };
            if (GUILayout.Button("🛠  START SETUP", startStyle, GUILayout.Height(44)))
            { _mode = OrthoMode.Setup; UpdateCamera(); }
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);
            GUILayout.Label("Varonia Back Office  ·  Ortho View", _footerStyle);
        }

        // ─── Setup screen ─────────────────────────────────────────────────────────

        void DrawSetupScreen()
        {
            ResizeTo(480, 320);

            EditorGUILayout.Space(14);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.Label("ORTHO CAPTURE", _headerStyle);
            GUILayout.FlexibleSpace();
            var tagStyle = new GUIStyle { fontSize=9, fontStyle=FontStyle.Bold,
                alignment=TextAnchor.MiddleCenter,
                normal={textColor=colAccent, background=MakeTex(colAccentDim)},
                padding=new RectOffset(10,10,4,4), margin=new RectOffset(0,16,2,0) };
            GUILayout.Label("SETUP", tagStyle);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            DrawCard(() =>
            {
                DrawSectionLabel("CAMERA");
                EditorGUILayout.Space(4); DrawDivider(); EditorGUILayout.Space(10);
                EditorGUI.BeginChangeCheck();
                DrawSliderRow("Zoom (Size)",  ref _orthoSize,    1f, 150f, $"{_orthoSize:F1}");
                EditorGUILayout.Space(6);
                DrawSliderRow("Altitude (Y)", ref _cameraHeight, 1f, 300f, $"{_cameraHeight:F0} m");
                if (EditorGUI.EndChangeCheck()) UpdateCamera();
                EditorGUILayout.Space(4);
            }, colAccent);

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            var cancelStyle = new GUIStyle(_buttonStyle) { fontSize=11,
                normal={textColor=colError,   background=MakeRoundedTex(32,32,new Color(colError.r,colError.g,colError.b,.13f),5)},
                hover ={textColor=Color.white,background=MakeRoundedTex(32,32,new Color(colError.r,colError.g,colError.b,.45f),5)},
                active={textColor=Color.white,background=MakeRoundedTex(32,32,colError,5)} };
            if (GUILayout.Button("✕  Cancel", cancelStyle, GUILayout.Height(36), GUILayout.Width(110))) Close();
            GUILayout.Space(8);
            var capStyle = new GUIStyle { fontSize=13, fontStyle=FontStyle.Bold,
                alignment=TextAnchor.MiddleCenter, border=new RectOffset(8,8,8,8),
                normal={textColor=Color.white, background=MakeRoundedTex(32,32,new Color(colAccent.r,colAccent.g,colAccent.b,.75f),8)},
                hover ={textColor=Color.white, background=MakeRoundedTex(32,32,colAccent,8)},
                active={textColor=Color.white, background=MakeRoundedTex(32,32,new Color(colAccent.r*.75f,colAccent.g*.75f,colAccent.b*.75f,1f),8)} };
            if (GUILayout.Button("📸  CAPTURE  ·  1920×1080", capStyle, GUILayout.Height(36)))
                CaptureAndShow();
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);
            GUILayout.Label("Varonia Back Office  ·  Ortho View", _footerStyle);
        }

        // ─── Paint screen ─────────────────────────────────────────────────────────

        void DrawPaintScreen()
        {
            float W = position.width, H = position.height;
            const float TB_H = 58f, FOOT_H = 24f, PAD = 10f;

            EditorGUI.DrawRect(new Rect(0, 0, W, 2), colAccent);
            EditorGUI.DrawRect(new Rect(0, 2, W, TB_H - 2), new Color(.13f,.13f,.17f,1f));
            EditorGUI.DrawRect(new Rect(0, TB_H, W, 1), colDivider);

            // ── Toolbar ──
            GUILayout.BeginArea(new Rect(PAD, 8f, W - PAD * 2f, TB_H - 10f));
            GUILayout.BeginHorizontal();

            GUILayout.Label("Color", _tbLabelStyle, GUILayout.Width(34));
            _brushColor = EditorGUILayout.ColorField(GUIContent.none, _brushColor, false, false, false,
                GUILayout.Width(46), GUILayout.Height(36));
            GUILayout.Space(10);
            GUILayout.Label("Size", _tbLabelStyle, GUILayout.Width(28));
            _brushSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(_brushSize, 1, 50,
                GUILayout.Width(90), GUILayout.Height(36)));
            GUILayout.Label(_brushSize.ToString(), _sliderValueStyle, GUILayout.Width(24));
            GUILayout.Space(14);

            DrawToolButton(PaintTool.Brush);  GUILayout.Space(3);
            DrawToolButton(PaintTool.Line);   GUILayout.Space(3);
            DrawToolButton(PaintTool.Rect);   GUILayout.Space(3);
            DrawToolButton(PaintTool.Eraser);

            GUILayout.Space(10);
            int undos = _undoHistory.Count;
            var hintStyle = new GUIStyle { fontSize=9, alignment=TextAnchor.MiddleLeft,
                normal={textColor=colTextMuted}, padding=new RectOffset(0,0,8,0) };
            GUILayout.Label(undos > 0 ? $"Ctrl+Z ×{undos}" : "Ctrl+Z", hintStyle, GUILayout.Width(62));
            GUILayout.FlexibleSpace();

            var retakeStyle = new GUIStyle(_buttonStyle) { fontSize=10, padding=new RectOffset(10,10,5,5),
                normal={textColor=colWarn,     background=MakeRoundedTex(32,32,new Color(colWarn.r,colWarn.g,colWarn.b,.15f),5)},
                hover ={textColor=Color.white, background=MakeRoundedTex(32,32,new Color(colWarn.r,colWarn.g,colWarn.b,.45f),5)},
                active={textColor=Color.white, background=MakeRoundedTex(32,32,colWarn,5)} };
            if (GUILayout.Button("↩  Retake", retakeStyle, GUILayout.Height(36))) Retake();
            GUILayout.Space(6);
            var saveStyle = new GUIStyle { fontSize=11, fontStyle=FontStyle.Bold,
                alignment=TextAnchor.MiddleCenter, border=new RectOffset(6,6,6,6),
                padding=new RectOffset(12,12,5,5),
                normal={textColor=Color.white, background=MakeRoundedTex(32,32,new Color(colAccent.r,colAccent.g,colAccent.b,.75f),6)},
                hover ={textColor=Color.white, background=MakeRoundedTex(32,32,colAccent,6)},
                active={textColor=Color.white, background=MakeRoundedTex(32,32,new Color(colAccent.r*.75f,colAccent.g*.75f,colAccent.b*.75f,1f),6)} };
            if (GUILayout.Button("💾  Save", saveStyle, GUILayout.Height(36))) SaveAndExit();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // ── Image area ──
            float imgY = TB_H + PAD + 1f, imgH = H - imgY - FOOT_H - PAD;
            Rect imgArea = new Rect(PAD, imgY, W - PAD * 2f, imgH);
            EditorGUI.DrawRect(imgArea, new Color(0f, 0f, 0f, .35f));

            if (_capturedTex != null)
            {
                _paintDispRect = CalcFitRect(imgArea, (float)_capturedTex.width / _capturedTex.height);
                GUI.DrawTexture(_paintDispRect, _capturedTex, ScaleMode.StretchToFill);

                var bc = new Color(colAccent.r, colAccent.g, colAccent.b, .35f);
                float x = _paintDispRect.x, y = _paintDispRect.y,
                      w = _paintDispRect.width, h = _paintDispRect.height;
                EditorGUI.DrawRect(new Rect(x-1, y-1, w+2, 1), bc);
                EditorGUI.DrawRect(new Rect(x-1, y+h, w+2, 1), bc);
                EditorGUI.DrawRect(new Rect(x-1, y-1, 1, h+2), bc);
                EditorGUI.DrawRect(new Rect(x+w, y-1, 1, h+2), bc);

                // ── Custom cursor: set/reset based on mouse position over paint rect ──
                if (Event.current.type == EventType.Repaint || Event.current.isMouse)
                {
                    if (_paintDispRect.Contains(Event.current.mousePosition))
                        SetPaintCursor();
                    else
                        ResetCursor();
                }

                HandlePainting(_paintDispRect);
            }
            else
            {
                ResetCursor();
            }

            EditorGUI.DrawRect(new Rect(0, H-FOOT_H, W, FOOT_H), new Color(.09f,.09f,.12f,1f));
            GUI.Label(new Rect(0, H-FOOT_H+4f, W, FOOT_H),
                "Varonia Back Office  ·  Ortho View", _footerStyle);
        }

        void DrawToolButton(PaintTool tool)
        {
            bool active = _activeTool == tool;
            var content = ToolContent(tool);
            var s = new GUIStyle { alignment=TextAnchor.MiddleCenter, fontSize=14,
                padding=new RectOffset(4,4,4,4), border=new RectOffset(5,5,5,5),
                normal={textColor=active?new Color(.05f,.08f,.07f):colTextSecond, background=active?_texAccentSolid:_texBtn},
                hover ={textColor=active?new Color(.05f,.08f,.07f):Color.white,   background=active?_texAccentSolid:_texBtnHover},
                active={textColor=new Color(.05f,.08f,.07f), background=_texAccentSolid} };
            if (GUILayout.Button(content, s, GUILayout.Width(36), GUILayout.Height(36)))
            {
                _activeTool = tool;
                if (_cursorOverPaint) SetPaintCursor(); // update cursor immediately on tool change
            }
        }

        // ─── Paint helpers ────────────────────────────────────────────────────────

        static Rect CalcFitRect(Rect area, float imageAspect)
        {
            float a = area.width / area.height;
            if (imageAspect >= a) { float h=area.width/imageAspect; return new Rect(area.x, area.y+(area.height-h)*.5f, area.width, h); }
            else                  { float w=area.height*imageAspect; return new Rect(area.x+(area.width-w)*.5f, area.y, w, area.height); }
        }

        Vector2Int ScreenToPx(Vector2 sp, Rect dr)
        {
            float nx=(sp.x-dr.x)/dr.width, ny=1f-(sp.y-dr.y)/dr.height;
            return new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt(nx*(_capturedTex.width -1)),0,_capturedTex.width -1),
                Mathf.Clamp(Mathf.RoundToInt(ny*(_capturedTex.height-1)),0,_capturedTex.height-1));
        }

        void HandlePainting(Rect dispRect)
        {
            Event e = Event.current;
            if (e.button != 0) return;
            bool down=e.type==EventType.MouseDown, drag=e.type==EventType.MouseDrag, up=e.type==EventType.MouseUp;
            if (!down&&!drag&&!up) return;
            if (down && !dispRect.Contains(e.mousePosition)) return;
            Vector2Int px = ScreenToPx(e.mousePosition, dispRect);
            switch (_activeTool)
            {
                case PaintTool.Brush:  HandleBrushEvent(e, px, down, drag, up);  break;
                case PaintTool.Eraser: HandleEraserEvent(e, px, down, drag, up); break;
                case PaintTool.Line:
                case PaintTool.Rect:   HandleShapeEvent(e, px, down, drag, up);  break;
            }
        }

        // ── Brush ─────────────────────────────────────────────────────────────────

        void HandleBrushEvent(Event e, Vector2Int px, bool down, bool drag, bool up)
        {
            if (down)                    { PushUndo(); _brushStroking=true; _lastBrushPx=px; PaintCircle(px); e.Use(); Repaint(); }
            else if (drag&&_brushStroking){ PaintLineBresenham(_lastBrushPx,px); _lastBrushPx=px; e.Use(); Repaint(); }
            else if (up)                 { _brushStroking=false; e.Use(); }
        }

        void PaintCircle(Vector2Int c)
        {
            int W=_capturedTex.width, H=_capturedTex.height, r=Mathf.Max(1,_brushSize);
            for (int dy=-r;dy<=r;dy++) for (int dx=-r;dx<=r;dx++)
            {
                if (dx*dx+dy*dy>r*r) continue;
                _capturedTex.SetPixel(Mathf.Clamp(c.x+dx,0,W-1),Mathf.Clamp(c.y+dy,0,H-1),_brushColor);
            }
            _texDirty=true;
        }

        void PaintLineBresenham(Vector2Int a, Vector2Int b)
        {
            int x0=a.x,y0=a.y,x1=b.x,y1=b.y;
            int dx=Mathf.Abs(x1-x0),sx=x0<x1?1:-1, dy=-Mathf.Abs(y1-y0),sy=y0<y1?1:-1, err=dx+dy;
            while(true){ PaintCircle(new Vector2Int(x0,y0)); if(x0==x1&&y0==y1)break; int e2=2*err; if(e2>=dy){err+=dy;x0+=sx;} if(e2<=dx){err+=dx;y0+=sy;} }
        }

        // ── Eraser ────────────────────────────────────────────────────────────────

        void HandleEraserEvent(Event e, Vector2Int px, bool down, bool drag, bool up)
        {
            if (down)                    { PushUndo(); _brushStroking=true; _lastBrushPx=px; EraseCircle(px); e.Use(); Repaint(); }
            else if (drag&&_brushStroking){ EraseLineBresenham(_lastBrushPx,px); _lastBrushPx=px; e.Use(); Repaint(); }
            else if (up)                 { _brushStroking=false; e.Use(); }
        }

        void EraseCircle(Vector2Int c)
        {
            if (_originalPixels==null) return;
            int W=_capturedTex.width, H=_capturedTex.height, r=Mathf.Max(1,_brushSize);
            for (int dy=-r;dy<=r;dy++) for (int dx=-r;dx<=r;dx++)
            {
                if (dx*dx+dy*dy>r*r) continue;
                int px=Mathf.Clamp(c.x+dx,0,W-1), py=Mathf.Clamp(c.y+dy,0,H-1);
                _capturedTex.SetPixel(px,py,_originalPixels[py*W+px]);
            }
            _texDirty=true;
        }

        void EraseLineBresenham(Vector2Int a, Vector2Int b)
        {
            int x0=a.x,y0=a.y,x1=b.x,y1=b.y;
            int dx=Mathf.Abs(x1-x0),sx=x0<x1?1:-1, dy=-Mathf.Abs(y1-y0),sy=y0<y1?1:-1, err=dx+dy;
            while(true){ EraseCircle(new Vector2Int(x0,y0)); if(x0==x1&&y0==y1)break; int e2=2*err; if(e2>=dy){err+=dy;x0+=sx;} if(e2<=dx){err+=dx;y0+=sy;} }
        }

        // ── Shape tools (live preview) ────────────────────────────────────────────

        void HandleShapeEvent(Event e, Vector2Int px, bool down, bool drag, bool up)
        {
            if (down) { PushUndo(); _shapeStartPx=px; _shapeBasePixels=_capturedTex.GetPixels32(); _shapeDragging=true; e.Use(); Repaint(); }
            else if ((drag||up)&&_shapeDragging)
            {
                var work=(Color32[])_shapeBasePixels.Clone();
                if (_activeTool==PaintTool.Line) DrawLineIntoArray(work,_shapeStartPx,px);
                else                             DrawRectIntoArray(work,_shapeStartPx,px);
                _capturedTex.SetPixels32(work); _capturedTex.Apply(); _texDirty=false;
                if (up) _shapeDragging=false;
                e.Use(); Repaint();
            }
            else if (up) _shapeDragging=false;
        }

        static void StampCircle(Color32[] arr,int W,int H,int cx,int cy,int r,Color32 col)
        {
            if (r<=0){arr[Mathf.Clamp(cy,0,H-1)*W+Mathf.Clamp(cx,0,W-1)]=col;return;}
            for(int dy=-r;dy<=r;dy++) for(int dx=-r;dx<=r;dx++)
            { if(dx*dx+dy*dy>r*r)continue; arr[Mathf.Clamp(cy+dy,0,H-1)*W+Mathf.Clamp(cx+dx,0,W-1)]=col; }
        }

        void DrawLineIntoArray(Color32[] arr, Vector2Int a, Vector2Int b)
        {
            int W=_capturedTex.width,H=_capturedTex.height; Color32 col=_brushColor; int r=Mathf.Max(1,_brushSize);
            int x0=a.x,y0=a.y,x1=b.x,y1=b.y;
            int ddx=Mathf.Abs(x1-x0),sx=x0<x1?1:-1, ddy=-Mathf.Abs(y1-y0),sy=y0<y1?1:-1, err=ddx+ddy;
            while(true){ StampCircle(arr,W,H,x0,y0,r,col); if(x0==x1&&y0==y1)break; int e2=2*err; if(e2>=ddy){err+=ddy;x0+=sx;} if(e2<=ddx){err+=ddx;y0+=sy;} }
        }

        void DrawRectIntoArray(Color32[] arr, Vector2Int a, Vector2Int b)
        {
            int W=_capturedTex.width,H=_capturedTex.height; Color32 col=_brushColor; int r=Mathf.Max(1,_brushSize);
            int x0=Mathf.Min(a.x,b.x),y0=Mathf.Min(a.y,b.y),x1=Mathf.Max(a.x,b.x),y1=Mathf.Max(a.y,b.y);
            for(int x=x0;x<=x1;x++){StampCircle(arr,W,H,x,y0,r,col);StampCircle(arr,W,H,x,y1,r,col);}
            for(int y=y0;y<=y1;y++){StampCircle(arr,W,H,x0,y,r,col);StampCircle(arr,W,H,x1,y,r,col);}
        }

        // ── Undo ──────────────────────────────────────────────────────────────────

        void PushUndo()
        {
            if (_capturedTex==null) return;
            _undoHistory.Add(_capturedTex.GetPixels32());
            if (_undoHistory.Count>k_MaxUndo) _undoHistory.RemoveAt(0);
        }

        void PerformUndo()
        {
            if (_undoHistory.Count==0||_capturedTex==null) return;
            var snap=_undoHistory[_undoHistory.Count-1]; _undoHistory.RemoveAt(_undoHistory.Count-1);
            _capturedTex.SetPixels32(snap); _capturedTex.Apply();
            _texDirty=_shapeDragging=_brushStroking=false;
            Repaint();
        }

        // ─── Draw helpers ─────────────────────────────────────────────────────────

        void DrawCard(System.Action content, Color accentColor)
        {
            EditorGUILayout.BeginHorizontal(); GUILayout.Space(12);
            EditorGUILayout.BeginVertical();
            Rect r=EditorGUILayout.BeginVertical();
            r.x-=4;r.width+=8;r.y-=4;r.height+=8;
            EditorGUI.DrawRect(r,colCard);
            EditorGUI.DrawRect(new Rect(r.x,r.y,r.width,2),accentColor);
            GUILayout.Space(12); content(); GUILayout.Space(12);
            EditorGUILayout.EndVertical(); EditorGUILayout.EndVertical();
            GUILayout.Space(12); EditorGUILayout.EndHorizontal();
        }

        void DrawSectionLabel(string text) => GUILayout.Label(text, _sectionStyle);

        void DrawDivider()
        {
            Rect r=GUILayoutUtility.GetRect(1,1); r.x+=20; r.width-=40;
            EditorGUI.DrawRect(r,colDivider);
        }

        void DrawSliderRow(string label, ref float value, float min, float max, string valStr)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, _sliderLabelStyle, GUILayout.Width(104));
            value=GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(valStr, _sliderValueStyle, GUILayout.Width(58));
            EditorGUILayout.EndHorizontal();
        }

        void ResizeTo(float w, float h)
        {
            var size=new Vector2(w,h); if (minSize==size) return; minSize=maxSize=size;
        }

        // ─── Logic ────────────────────────────────────────────────────────────────

        void UpdateCamera()
        {
            // ⚠ Do NOT use ?? with UnityEngine.Object — C# ?? is CLR null-check, not Unity's ==.
            var go=GameObject.Find("TempOrthoCam");
            if (go==null) go=new GameObject("TempOrthoCam");
            _tempCam=go.GetComponent<Camera>();
            if (_tempCam==null) _tempCam=go.AddComponent<Camera>();
            _tempCam.orthographic     =true;
            _tempCam.orthographicSize =_orthoSize;
            _tempCam.transform.position=new Vector3(0,_cameraHeight,0);
            _tempCam.transform.rotation=Quaternion.Euler(90,0,-90);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            SceneView.RepaintAll();
        }

        void CaptureAndShow()
        {
            if (_tempCam==null) return;
            var rt=new RenderTexture(1920,1080,24);
            _tempCam.targetTexture=rt;
            if (_capturedTex!=null) DestroyImmediate(_capturedTex);
            _capturedTex=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            _capturedTex.hideFlags=HideFlags.HideAndDontSave;
            _tempCam.Render();
            RenderTexture.active=rt;
            _capturedTex.ReadPixels(new Rect(0,0,1920,1080),0,0);
            _capturedTex.Apply();
            _tempCam.targetTexture=null; RenderTexture.active=null; DestroyImmediate(rt);
            _originalPixels=_capturedTex.GetPixels32();
            _undoHistory.Clear();
            _shapeDragging=_brushStroking=_texDirty=false;
            _mode=OrthoMode.Paint;

            // ── Full-screen paint mode ─────────────────────────────────────────
            // Use GoFullScreen() which reads the actual Unity main window bounds
            // via reflection — more reliable than maximized=true on floating windows.
            GoFullScreen();
        }

        void SaveAndExit()
        {
            if (_capturedTex==null) return;
            if (_texDirty){_capturedTex.Apply();_texDirty=false;}
            byte[] bytes=_capturedTex.EncodeToJPG(95);
            string scene=SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(scene)) scene="UntitledScene";
            string fileName=$"{scene}_{_orthoSize:F0}.jpg";
            string dir=EditorPrefs.GetString(k_PathKey);
            if (string.IsNullOrEmpty(dir)||!Directory.Exists(dir)) dir=Application.dataPath;
            string path=Path.Combine(dir,fileName);
            File.WriteAllBytes(path,bytes);
            if (path.StartsWith(Application.dataPath)) AssetDatabase.Refresh();
            Debug.Log($"[Ortho] Saved → {path}");
            ResetCursor();
            Close();
        }

        void Retake()
        {
            if (_capturedTex!=null){DestroyImmediate(_capturedTex);_capturedTex=null;}
            _originalPixels=null; _undoHistory.Clear();
            _shapeDragging=_brushStroking=_texDirty=false;
            ResetCursor();
            GoWindowed(480, 320);
            _mode=OrthoMode.Setup;
            UpdateCamera();
        }

        void OnDestroy()
        {
            ResetCursor();
            if (_capturedTex!=null){DestroyImmediate(_capturedTex);_capturedTex=null;}
            var go=GameObject.Find("TempOrthoCam");
            if (go!=null) DestroyImmediate(go);
        }
    }
}
