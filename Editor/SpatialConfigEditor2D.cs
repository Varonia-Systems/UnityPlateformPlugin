using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VaroniaBackOffice
{
    public partial class SpatialConfigReflectionEditor
    {
        // ─── State ─────────────────────────────────────────────────────────────────

        // Viewport
        private float   _viewZoom = 30f;        // pixels per meter
        private Vector2 _viewPan;                // world (X, Z) at viewport center
        private bool    _viewInitialized;
        private Rect    _viewportRect;           // last viewport rect (screen coords)
        private const float SidebarWidth   = 300f;
        private const float ViewportMinHeight = 300f;
        // Fixed overhead above & below the viewport (header, tabs, editor-params card, footer, paddings).
        private const float ViewportOverhead = 360f;
        private float ViewportHeight => Mathf.Max(ViewportMinHeight, position.height - ViewportOverhead);

        // Selection
        private enum SelKind { None, Boundary, Vertex, Obstacle, SyncPos }
        private SelKind _selKind     = SelKind.None;
        private int     _selBoundary = -1;
        private int     _selVertex   = -1;
        private int     _selObstacle = -1;

        // Drag
        private enum DragKind { None, Pan, Vertex, BoundaryAll, SyncPos, SyncRot, Obstacle }
        private const float SyncRotHandlePx = 70f;
        private DragKind      _dragKind;
        private Vector2       _dragStartMouseWorld;
        private List<Vector2> _dragOriginalPoints;
        private Vector2       _dragOriginalPos;

        // Ortho
        private string       _orthoDir;
        private List<string> _orthoFiles = new List<string>();
        private int          _orthoIdx   = -1;
        private Texture2D    _orthoTex;
        private float        _orthoSizeMeters; // orthographicSize in world units (half vertical)
        private bool         _showOrtho   = true;
        private float        _orthoOpacity = 0.55f;
        private bool         _showGrid    = true;
        private float        _gridStep    = 1f; // meters
        private bool         _orthoLoaded;
        private Vector2      _inspScroll;

        // Reference rectangle (background visual aid)
        private bool  _showRect = true;
        private float _rectW    = 7f;    // meters along X
        private float _rectH    = 5.5f;  // meters along Z

        // Undo
        private readonly Stack<string> _undoStack = new Stack<string>(64);
        private const int UndoMaxDepth = 64;

        // Multiplier delta-tracking
        private double _lastMultiplier;
        private bool   _multiplierTracked;

        // ─── Public entry ──────────────────────────────────────────────────────────

        private void DrawEditor2DAndInspector()
        {
            if (!_orthoLoaded) { LoadOrthoFiles(); _orthoLoaded = true; }

            DrawEditorToolbar();
            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();

            // ── Viewport (left, expanding) ───────────────────────────────────────
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            Rect viewport = GUILayoutUtility.GetRect(
                GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(ViewportHeight));

            if (Event.current.type != EventType.Layout)
                _viewportRect = viewport;

            if (!_viewInitialized && Event.current.type == EventType.Repaint)
            {
                FitViewToContent();
                _viewInitialized = true;
            }

            DrawViewport(viewport);
            EditorGUILayout.EndVertical();

            GUILayout.Space(8);

            // ── Inspector sidebar (right) ────────────────────────────────────────
            Rect sideRect = EditorGUILayout.BeginVertical(
                GUILayout.Width(SidebarWidth),
                GUILayout.Height(ViewportHeight));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(sideRect, new Color(0.10f, 0.10f, 0.13f, 1f));
            DrawInspector();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        // ─── Toolbar (above viewport) ──────────────────────────────────────────────

        private void DrawEditorToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            // ── Ortho controls ──
            bool hasOrtho = _orthoFiles.Count > 0;

            GUI.enabled = hasOrtho;
            _showOrtho = GUILayout.Toggle(_showOrtho, " Ortho", EditorStyles.toolbarButton, GUILayout.Width(60));
            GUI.enabled = hasOrtho && _showOrtho;

            if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(24)))
                CycleOrtho(-1);

            string orthoLabel = hasOrtho
                ? $"{Path.GetFileNameWithoutExtension(_orthoFiles[_orthoIdx])}  ·  size {_orthoSizeMeters:0}m"
                : "(no ortho)";
            GUILayout.Label(orthoLabel, EditorStyles.toolbarButton, GUILayout.MinWidth(180));

            if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(24)))
                CycleOrtho(+1);

            GUILayout.Label("Opacity", EditorStyles.miniLabel, GUILayout.Width(50));
            _orthoOpacity = GUILayout.HorizontalSlider(_orthoOpacity, 0f, 1f, GUILayout.Width(90));

            GUI.enabled = true;

            GUILayout.Space(12);

            // ── Grid controls ──
            _showGrid = GUILayout.Toggle(_showGrid, " Grid", EditorStyles.toolbarButton, GUILayout.Width(54));
            GUI.enabled = _showGrid;
            GUILayout.Label("Step", EditorStyles.miniLabel, GUILayout.Width(34));
            _gridStep = Mathf.Max(0.1f, EditorGUILayout.FloatField(_gridStep, GUILayout.Width(46)));
            GUILayout.Label("m", EditorStyles.miniLabel, GUILayout.Width(14));
            GUI.enabled = true;

            GUILayout.Space(12);

            // ── Reference rect ──
            _showRect = GUILayout.Toggle(_showRect, " Rect", EditorStyles.toolbarButton, GUILayout.Width(54));
            GUI.enabled = _showRect;
            GUILayout.Label("W", EditorStyles.miniLabel, GUILayout.Width(14));
            _rectW = Mathf.Max(0.1f, EditorGUILayout.FloatField(_rectW, GUILayout.Width(46)));
            GUILayout.Label("H", EditorStyles.miniLabel, GUILayout.Width(14));
            _rectH = Mathf.Max(0.1f, EditorGUILayout.FloatField(_rectH, GUILayout.Width(46)));
            GUILayout.Label("m", EditorStyles.miniLabel, GUILayout.Width(14));
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            // ── View controls ──
            if (GUILayout.Button("Fit", EditorStyles.toolbarButton, GUILayout.Width(38)))
                FitViewToContent();
            if (GUILayout.Button("Reload ortho", EditorStyles.toolbarButton, GUILayout.Width(96)))
                LoadOrthoFiles();

            EditorGUILayout.EndHorizontal();
        }

        // ─── Viewport drawing & input ──────────────────────────────────────────────

        private void DrawViewport(Rect rect)
        {
            // Background fill
            EditorGUI.DrawRect(rect, new Color(0.07f, 0.07f, 0.09f));

            // Ortho background (clipped)
            if (_showOrtho && _orthoTex != null)
                DrawOrthoBackground(rect);

            // Grid + axes (Repaint only)
            if (Event.current.type == EventType.Repaint)
            {
                if (_showGrid) DrawGrid(rect);
                if (_showRect) DrawReferenceRect(rect);
                DrawAxes(rect);
                DrawBoundariesAndPoints(rect);
                DrawSyncPos(rect);
                DrawHud(rect);
            }

            // Input (mouse / wheel) — only when over rect
            HandleViewportInput(rect);
        }

        private void DrawOrthoBackground(Rect viewport)
        {
            // Texture is pre-rotated CW90 at load time, so right=+X, top=+Z map directly.
            // After rotation: tex.width corresponds to world X, tex.height corresponds to world Z.
            float wHalfX = _orthoSizeMeters;
            float wHalfZ = _orthoSizeMeters * (_orthoTex.height / (float)_orthoTex.width);

            // World rect covered by the viewport (clamped to image extents)
            Vector2 vMin = ScreenToWorld(new Vector2(viewport.xMin, viewport.yMax));
            Vector2 vMax = ScreenToWorld(new Vector2(viewport.xMax, viewport.yMin));

            float wxMin = Mathf.Max(-wHalfX, vMin.x);
            float wxMax = Mathf.Min(+wHalfX, vMax.x);
            float wzMin = Mathf.Max(-wHalfZ, vMin.y);
            float wzMax = Mathf.Min(+wHalfZ, vMax.y);
            if (wxMin >= wxMax || wzMin >= wzMax) return;

            // Screen rect of the visible image portion
            Vector2 sTL = WorldToScreen(new Vector2(wxMin, wzMax));
            Vector2 sBR = WorldToScreen(new Vector2(wxMax, wzMin));
            Rect target = Rect.MinMaxRect(
                Mathf.Max(sTL.x, viewport.xMin),
                Mathf.Max(sTL.y, viewport.yMin),
                Mathf.Min(sBR.x, viewport.xMax),
                Mathf.Min(sBR.y, viewport.yMax));
            if (target.width <= 0f || target.height <= 0f) return;

            // UV rect (rotated tex: u→X [0..1] ↔ [-wHalfX..+wHalfX], v→Z [0..1] ↔ [-wHalfZ..+wHalfZ])
            float u0 = (wxMin + wHalfX) / (2f * wHalfX);
            float u1 = (wxMax + wHalfX) / (2f * wHalfX);
            float v0 = (wzMin + wHalfZ) / (2f * wHalfZ);
            float v1 = (wzMax + wHalfZ) / (2f * wHalfZ);

            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, _orthoOpacity);
            GUI.DrawTextureWithTexCoords(target, _orthoTex, new Rect(u0, v0, u1 - u0, v1 - v0));
            GUI.color = prev;
        }

        private void DrawGrid(Rect rect)
        {
            // Adapt grid step to zoom: ensure ~24-96 px between lines.
            float effectiveStep = _gridStep;
            float pxPerStep     = effectiveStep * _viewZoom;
            while (pxPerStep < 24f) { effectiveStep *= 2f; pxPerStep = effectiveStep * _viewZoom; }
            while (pxPerStep > 96f) { effectiveStep *= 0.5f; pxPerStep = effectiveStep * _viewZoom; }

            // World extents covered by viewport
            Vector2 worldMin = ScreenToWorld(new Vector2(rect.xMin, rect.yMax));
            Vector2 worldMax = ScreenToWorld(new Vector2(rect.xMax, rect.yMin));

            float startX = Mathf.Floor(worldMin.x / effectiveStep) * effectiveStep;
            float startZ = Mathf.Floor(worldMin.y / effectiveStep) * effectiveStep;

            Color minor = new Color(1f, 1f, 1f, 0.05f);
            Color major = new Color(1f, 1f, 1f, 0.12f);

            Handles.BeginGUI();
            for (float x = startX; x <= worldMax.x; x += effectiveStep)
            {
                bool isMajor = Mathf.Abs(x % (effectiveStep * 5f)) < 0.001f
                            || Mathf.Abs(x % (effectiveStep * 5f) - effectiveStep * 5f) < 0.001f;
                Handles.color = isMajor ? major : minor;
                Vector2 a = WorldToScreen(new Vector2(x, worldMin.y));
                Vector2 b = WorldToScreen(new Vector2(x, worldMax.y));
                Handles.DrawLine(a, b);
            }
            for (float z = startZ; z <= worldMax.y; z += effectiveStep)
            {
                bool isMajor = Mathf.Abs(z % (effectiveStep * 5f)) < 0.001f
                            || Mathf.Abs(z % (effectiveStep * 5f) - effectiveStep * 5f) < 0.001f;
                Handles.color = isMajor ? major : minor;
                Vector2 a = WorldToScreen(new Vector2(worldMin.x, z));
                Vector2 b = WorldToScreen(new Vector2(worldMax.x, z));
                Handles.DrawLine(a, b);
            }
            Handles.EndGUI();

            // Step label (top-left)
            var lbl = new GUIStyle { fontSize = 9, normal = { textColor = new Color(1f, 1f, 1f, 0.5f) } };
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, 200, 14),
                $"grid {effectiveStep:0.##}m  ·  zoom {_viewZoom:0.#} px/m", lbl);
        }

        private void DrawReferenceRect(Rect rect)
        {
            float hw = _rectW * 0.5f;
            float hh = _rectH * 0.5f;
            Vector2 tl = WorldToScreen(new Vector2(-hw, +hh));
            Vector2 tr = WorldToScreen(new Vector2(+hw, +hh));
            Vector2 br = WorldToScreen(new Vector2(+hw, -hh));
            Vector2 bl = WorldToScreen(new Vector2(-hw, -hh));

            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.85f);
            DrawClippedLine(tl, tr, rect, 6f);
            DrawClippedLine(tr, br, rect, 6f);
            DrawClippedLine(br, bl, rect, 6f);
            DrawClippedLine(bl, tl, rect, 6f);
            Handles.EndGUI();

            // Dimensions label near top-right corner
            if (RectContainsWithMargin(rect, tr, 0f))
            {
                var lbl = new GUIStyle {
                    fontSize = 9,
                    normal   = { textColor = new Color(1f, 1f, 1f, 0.9f) },
                };
                GUI.Label(new Rect(tr.x + 4f, tr.y + 2f, 80f, 12f),
                    $"{_rectW:0.##} × {_rectH:0.##} m", lbl);
            }
        }

        private static void DrawClippedLine(Vector2 a, Vector2 b, Rect r, float lw)
        {
            if (ClipSegmentToRect(a, b, r, out var ca, out var cb))
                Handles.DrawAAPolyLine(lw, ca, cb);
        }

        private void DrawAxes(Rect rect)
        {
            Vector2 origin = WorldToScreen(Vector2.zero);
            Handles.BeginGUI();
            if (origin.y >= rect.yMin && origin.y <= rect.yMax)
            {
                Handles.color = new Color(0.8f, 0.3f, 0.3f, 0.6f); // world X axis = horizontal line
                Handles.DrawLine(new Vector3(rect.xMin, origin.y), new Vector3(rect.xMax, origin.y));
            }
            if (origin.x >= rect.xMin && origin.x <= rect.xMax)
            {
                Handles.color = new Color(0.3f, 0.5f, 0.9f, 0.6f); // world Z axis = vertical line
                Handles.DrawLine(new Vector3(origin.x, rect.yMin), new Vector3(origin.x, rect.yMax));
            }
            Handles.EndGUI();
        }

        private void DrawBoundariesAndPoints(Rect rect)
        {
            var spatial = _configObj as Spatial;
            if (spatial?.Boundaries == null) return;

            Handles.BeginGUI();
            for (int bi = 0; bi < spatial.Boundaries.Count; bi++)
            {
                var b = spatial.Boundaries[bi];
                if (b?.Points == null || b.Points.Count < 2) continue;

                bool isSelB = (_selKind == SelKind.Boundary || _selKind == SelKind.Vertex || _selKind == SelKind.Obstacle)
                              && _selBoundary == bi;

                Color col = (b.BoundaryColor != null)
                    ? new Color(b.BoundaryColor.x, b.BoundaryColor.y, b.BoundaryColor.z)
                    : Color.green;
                float lw = b.MainBoundary ? 4.5f : 2.8f;
                float lwSel  = lw + 3f;
                float lwHalo = lw + 8f;

                // ── Outline ─────────────────────────────────────────────────
                // Pre-compute once per segment to avoid clipping twice
                if (isSelB)
                {
                    // Halo (thick, soft)
                    Handles.color = new Color(col.r, col.g, col.b, 0.35f);
                    for (int i = 0; i < b.Points.Count; i++)
                    {
                        var p1 = b.Points[i];
                        var p2 = b.Points[(i + 1) % b.Points.Count];
                        if (p1 == null || p2 == null) continue;
                        Vector2 a = WorldToScreen(LocalToWorld2D(p1));
                        Vector2 c = WorldToScreen(LocalToWorld2D(p2));
                        if (ClipSegmentToRect(a, c, rect, out Vector2 ca, out Vector2 cb))
                            Handles.DrawAAPolyLine(lwHalo, ca, cb);
                    }
                }
                Handles.color = col;
                for (int i = 0; i < b.Points.Count; i++)
                {
                    var p1 = b.Points[i];
                    var p2 = b.Points[(i + 1) % b.Points.Count];
                    if (p1 == null || p2 == null) continue;
                    Vector2 a = WorldToScreen(LocalToWorld2D(p1));
                    Vector2 c = WorldToScreen(LocalToWorld2D(p2));
                    if (ClipSegmentToRect(a, c, rect, out Vector2 ca, out Vector2 cb))
                        Handles.DrawAAPolyLine(isSelB ? lwSel : lw, ca, cb);
                }

                // ── Vertices ───────────────────────────────────────────────
                for (int i = 0; i < b.Points.Count; i++)
                {
                    var p = b.Points[i];
                    if (p == null) continue;
                    Vector2 sp = WorldToScreen(LocalToWorld2D(p));
                    if (!RectContainsWithMargin(rect, sp, 16f)) continue;
                    bool selV = isSelB && _selKind == SelKind.Vertex && _selVertex == i;

                    if (isSelB)
                    {
                        // Halo ring around every vertex of selected boundary
                        Handles.color = new Color(col.r, col.g, col.b, 0.40f);
                        Handles.DrawSolidDisc(sp, Vector3.forward, selV ? 14f : 11f);
                    }
                    if (selV)
                    {
                        Handles.color = Color.white;
                        Handles.DrawSolidDisc(sp, Vector3.forward, 10f);
                    }
                    Handles.color = selV ? col : new Color(col.r, col.g, col.b, 0.95f);
                    float r = selV ? 6f : (isSelB ? 6.5f : 5.5f);
                    Handles.DrawSolidDisc(sp, Vector3.forward, r);
                }

                // Obstacles
                if (b.Obstacles != null)
                {
                    for (int oi = 0; oi < b.Obstacles.Count; oi++)
                    {
                        var o = b.Obstacles[oi];
                        if (o?.Position == null) continue;
                        Vector2 sp = WorldToScreen(LocalToWorld2D(o.Position));
                        float radius = Mathf.Max(5f, o.Scale * _viewZoom * 0.3f);
                        if (!RectContainsWithMargin(rect, sp, radius + 4f)) continue;
                        bool selO = isSelB && _selKind == SelKind.Obstacle && _selObstacle == oi;
                        Handles.color = selO ? Color.white : new Color(1f, 0.55f, 0.12f, 0.95f);
                        Handles.DrawWireDisc(sp, Vector3.forward, radius);
                        Handles.DrawWireDisc(sp, Vector3.forward, radius - 1f); // 2nd ring for visual weight
                        Handles.DrawSolidDisc(sp, Vector3.forward, selO ? 5f : 3.5f);
                    }
                }
            }
            Handles.EndGUI();
        }

        private static bool RectContainsWithMargin(Rect r, Vector2 p, float margin)
        {
            return p.x >= r.xMin - margin && p.x <= r.xMax + margin
                && p.y >= r.yMin - margin && p.y <= r.yMax + margin;
        }

        // Liang-Barsky 2D segment clipping to an axis-aligned rect.
        private static bool ClipSegmentToRect(Vector2 a, Vector2 b, Rect r, out Vector2 ca, out Vector2 cb)
        {
            float t0 = 0f, t1 = 1f;
            float dx = b.x - a.x, dy = b.y - a.y;

            if (!ClipTest(-dx, a.x - r.xMin, ref t0, ref t1) ||
                !ClipTest( dx, r.xMax - a.x, ref t0, ref t1) ||
                !ClipTest(-dy, a.y - r.yMin, ref t0, ref t1) ||
                !ClipTest( dy, r.yMax - a.y, ref t0, ref t1))
            { ca = cb = default; return false; }

            ca = new Vector2(a.x + t0 * dx, a.y + t0 * dy);
            cb = new Vector2(a.x + t1 * dx, a.y + t1 * dy);
            return true;
        }
        private static bool ClipTest(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-6f) return q >= 0f;
            float t = q / p;
            if (p < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else        { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }

        private void DrawSyncPos(Rect rect)
        {
            var spatial = _configObj as Spatial;
            if (spatial?.SyncPos == null) return;

            Vector2 sp = WorldToScreen(new Vector2(spatial.SyncPos.x, spatial.SyncPos.z));
            if (!RectContainsWithMargin(rect, sp, SyncRotHandlePx + 20f)) return;
            bool selPos = _selKind == SelKind.SyncPos && _dragKind != DragKind.SyncRot;
            bool isRotating = _dragKind == DragKind.SyncRot;

            Handles.BeginGUI();

            // ── Rotation handle: line + disc at rotated offset ──
            float yAngle = GetSyncYAngle(spatial);
            Vector2 handlePos = sp + SyncRotHandleOffset(yAngle);
            Handles.color = isRotating
                ? Color.white
                : new Color(colAccent.r, colAccent.g, colAccent.b, 0.85f);
            if (ClipSegmentToRect(sp, handlePos, rect, out var ra, out var rb))
                Handles.DrawAAPolyLine(2f, ra, rb);
            if (RectContainsWithMargin(rect, handlePos, 12f))
                Handles.DrawSolidDisc(handlePos, Vector3.forward, isRotating ? 7f : 5.5f);

            // ── Pos cross ──
            Handles.color = selPos ? Color.white : colAccent;
            float cs = selPos ? 10f : 8f;
            if (ClipSegmentToRect(sp - new Vector2(cs, 0), sp + new Vector2(cs, 0), rect, out var a1, out var b1))
                Handles.DrawAAPolyLine(2f, a1, b1);
            if (ClipSegmentToRect(sp - new Vector2(0, cs), sp + new Vector2(0, cs), rect, out var a2, out var b2))
                Handles.DrawAAPolyLine(2f, a2, b2);
            Handles.DrawWireDisc(sp, Vector3.forward, selPos ? 6f : 4.5f);

            // Angle readout while rotating
            if (isRotating && RectContainsWithMargin(rect, handlePos, 0f))
            {
                var lbl = new GUIStyle {
                    fontSize  = 10,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = Color.white },
                };
                GUI.Label(new Rect(handlePos.x - 24f, handlePos.y - 22f, 48f, 14f),
                    $"{Mathf.RoundToInt(yAngle)}°", lbl);
            }

            Handles.EndGUI();
        }

        private static Vector2 SyncRotHandleOffset(float yDeg)
        {
            // World Y rotation by yDeg of forward (+Z) → (sin(y), 0, cos(y)).
            // Viewport: world+X = screen+x, world+Z = screen-y.
            float r = yDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), -Mathf.Cos(r)) * SyncRotHandlePx;
        }

        private float GetSyncYAngle(Spatial s)
        {
            if (s?.SyncQuaterion == null) return 0f;
            var q = s.SyncQuaterion;
            float sq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (sq < 1e-6f) return 0f;
            float k = 1f / Mathf.Sqrt(sq);
            float a = new Quaternion(q.x * k, q.y * k, q.z * k, q.w * k).eulerAngles.y;
            if (a < 0f) a += 360f; if (a >= 360f) a -= 360f;
            return a;
        }

        private bool TryHitSyncRotHandle(Vector2 mouse)
        {
            var s = _configObj as Spatial;
            if (s?.SyncPos == null) return false;
            Vector2 sp = WorldToScreen(new Vector2(s.SyncPos.x, s.SyncPos.z));
            Vector2 hp = sp + SyncRotHandleOffset(GetSyncYAngle(s));
            return Vector2.Distance(hp, mouse) <= 10f;
        }

        private void DrawHud(Rect rect)
        {
            // Mouse world position (bottom-right)
            if (rect.Contains(Event.current.mousePosition))
            {
                Vector2 w = ScreenToWorld(Event.current.mousePosition);
                var s = new GUIStyle {
                    fontSize = 10,
                    alignment = TextAnchor.LowerRight,
                    normal = { textColor = new Color(1, 1, 1, 0.55f) },
                };
                GUI.Label(new Rect(rect.xMax - 160, rect.yMax - 16, 154, 14),
                    $"x {w.x:0.00}  z {w.y:0.00}", s);
            }
        }

        private void HandleViewportInput(Rect rect)
        {
            Event e = Event.current;
            bool over = rect.Contains(e.mousePosition);

            // Wheel zoom (centered on mouse)
            if (e.type == EventType.ScrollWheel && over)
            {
                Vector2 worldBefore = ScreenToWorld(e.mousePosition);
                float factor = (e.delta.y > 0f) ? 0.85f : 1.0f / 0.85f;
                _viewZoom = Mathf.Clamp(_viewZoom * factor, 2f, 400f);
                Vector2 worldAfter = ScreenToWorld(e.mousePosition);
                _viewPan += (worldBefore - worldAfter);
                e.Use();
                Repaint();
            }

            // Mouse down → start drag / select
            if (e.type == EventType.MouseDown && over)
            {
                Vector2 worldMouse = ScreenToWorld(e.mousePosition);

                // Pan: middle-button OR right-button (alt+left is reserved for vertex delete)
                if (e.button == 2 || e.button == 1)
                {
                    _dragKind = DragKind.Pan;
                    _dragStartMouseWorld = worldMouse;
                    e.Use();
                    return;
                }

                if (e.button == 0)
                {
                    // Hit-test in priority: SyncRot handle > SyncPos > vertex > obstacle > edge > inside boundary
                    if (TryHitSyncRotHandle(e.mousePosition))
                    {
                        _selKind = SelKind.SyncPos;
                        _dragKind = DragKind.SyncRot;
                        _dragStartMouseWorld = worldMouse;
                        e.Use(); Repaint(); return;
                    }

                    if (TryHitSyncPos(e.mousePosition))
                    {
                        _selKind = SelKind.SyncPos;
                        _dragKind = DragKind.SyncPos;
                        _dragStartMouseWorld = worldMouse;
                        var sp = ((Spatial)_configObj).SyncPos;
                        _dragOriginalPos = new Vector2(sp.x, sp.z);
                        e.Use(); Repaint(); return;
                    }

                    if (TryHitVertex(e.mousePosition, out int hb, out int hv))
                    {
                        _selKind = SelKind.Vertex;
                        _selBoundary = hb; _selVertex = hv; _selObstacle = -1;
                        if (e.alt)
                        {
                            // Alt+click → delete vertex
                            DeleteSelectedVertex();
                            _dragKind = DragKind.None;
                        }
                        else
                        {
                            _dragKind = DragKind.Vertex;
                            var p = ((Spatial)_configObj).Boundaries[hb].Points[hv];
                            _dragStartMouseWorld = worldMouse;
                            _dragOriginalPos = new Vector2(p.x, p.z);
                        }
                        e.Use(); Repaint(); return;
                    }

                    if (TryHitObstacle(e.mousePosition, out int ob, out int oo))
                    {
                        _selKind = SelKind.Obstacle;
                        _selBoundary = ob; _selObstacle = oo; _selVertex = -1;
                        _dragKind = DragKind.Obstacle;
                        var op = ((Spatial)_configObj).Boundaries[ob].Obstacles[oo].Position;
                        _dragStartMouseWorld = worldMouse;
                        _dragOriginalPos = new Vector2(op.x, op.z);
                        e.Use(); Repaint(); return;
                    }

                    if (TryHitEdge(e.mousePosition, out int eb, out int ei, out Vector2 onEdge, 8f))
                    {
                        // Insert vertex at click point on edge
                        InsertVertex(eb, ei + 1, onEdge);
                        _selKind = SelKind.Vertex;
                        _selBoundary = eb; _selVertex = ei + 1; _selObstacle = -1;
                        _dragKind = DragKind.Vertex;
                        _dragStartMouseWorld = worldMouse;
                        _dragOriginalPos = onEdge;
                        e.Use(); Repaint(); return;
                    }

                    if (TryHitBoundaryInterior(e.mousePosition, out int ib))
                    {
                        _selKind = SelKind.Boundary;
                        _selBoundary = ib; _selVertex = -1; _selObstacle = -1;
                        if (e.shift)
                        {
                            // Shift+drag = move whole boundary
                            _dragKind = DragKind.BoundaryAll;
                            _dragStartMouseWorld = worldMouse;
                            _dragOriginalPoints = new List<Vector2>();
                            foreach (var p in ((Spatial)_configObj).Boundaries[ib].Points)
                                _dragOriginalPoints.Add(new Vector2(p.x, p.z));
                        }
                        else
                        {
                            _dragKind = DragKind.None;
                        }
                        e.Use(); Repaint(); return;
                    }

                    // Empty click → deselect
                    _selKind = SelKind.None;
                    _selBoundary = _selVertex = _selObstacle = -1;
                    e.Use(); Repaint(); return;
                }
            }

            // Drag
            if (e.type == EventType.MouseDrag && _dragKind != DragKind.None)
            {
                Vector2 worldMouse = ScreenToWorld(e.mousePosition);
                Vector2 worldDelta = worldMouse - _dragStartMouseWorld;
                Vector2 localDelta = WorldToLocal2D(worldMouse) - WorldToLocal2D(_dragStartMouseWorld);

                switch (_dragKind)
                {
                    case DragKind.Pan:
                        _viewPan -= worldDelta; // pan is "drag the world", invert
                        break;

                    case DragKind.Vertex:
                    {
                        var p = ((Spatial)_configObj).Boundaries[_selBoundary].Points[_selVertex];
                        Vector2 np = _dragOriginalPos + localDelta;
                        p.x = np.x; p.z = np.y;
                        _isDirty = true;
                        break;
                    }
                    case DragKind.SyncPos:
                    {
                        var sp = ((Spatial)_configObj).SyncPos;
                        Vector2 np = _dragOriginalPos + worldDelta;
                        sp.x = np.x; sp.z = np.y;
                        _isDirty = true;
                        break;
                    }
                    case DragKind.SyncRot:
                    {
                        var sp = (Spatial)_configObj;
                        if (sp?.SyncPos == null) break;
                        if (sp.SyncQuaterion == null) sp.SyncQuaterion = new Vector4_(Quaternion.identity);
                        Vector2 syncScreen = WorldToScreen(new Vector2(sp.SyncPos.x, sp.SyncPos.z));
                        Vector2 dir = e.mousePosition - syncScreen;
                        if (dir.sqrMagnitude < 1e-4f) break;
                        float yDeg = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg;
                        if (yDeg < 0f) yDeg += 360f;
                        var nq = Quaternion.Euler(0f, yDeg, 0f);
                        sp.SyncQuaterion.x = nq.x; sp.SyncQuaterion.y = nq.y;
                        sp.SyncQuaterion.z = nq.z; sp.SyncQuaterion.w = nq.w;
                        _isDirty = true;
                        break;
                    }
                    case DragKind.Obstacle:
                    {
                        var op = ((Spatial)_configObj).Boundaries[_selBoundary].Obstacles[_selObstacle].Position;
                        Vector2 np = _dragOriginalPos + localDelta;
                        op.x = np.x; op.z = np.y;
                        _isDirty = true;
                        break;
                    }
                    case DragKind.BoundaryAll:
                    {
                        var pts = ((Spatial)_configObj).Boundaries[_selBoundary].Points;
                        for (int i = 0; i < pts.Count && i < _dragOriginalPoints.Count; i++)
                        {
                            Vector2 np = _dragOriginalPoints[i] + localDelta;
                            pts[i].x = np.x; pts[i].z = np.y;
                        }
                        _isDirty = true;
                        break;
                    }
                }
                e.Use(); Repaint();
            }

            if (e.type == EventType.MouseUp && _dragKind != DragKind.None)
            {
                _dragKind = DragKind.None;
                _dragOriginalPoints = null;
                e.Use(); Repaint();
            }

            // Repaint when hovering for HUD
            if (e.type == EventType.MouseMove && over) Repaint();
        }

        // ─── World <-> Screen mapping ──────────────────────────────────────────────

        private Vector2 ViewportCenter
        {
            get
            {
                return new Vector2(
                    _viewportRect.x + _viewportRect.width  * 0.5f,
                    _viewportRect.y + _viewportRect.height * 0.5f);
            }
        }

        private Vector2 WorldToScreen(Vector2 world)
        {
            Vector2 c = ViewportCenter;
            return new Vector2(
                c.x + (world.x - _viewPan.x) * _viewZoom,
                c.y - (world.y - _viewPan.y) * _viewZoom);
        }

        private Vector2 ScreenToWorld(Vector2 screen)
        {
            Vector2 c = ViewportCenter;
            return new Vector2(
                (screen.x - c.x) / _viewZoom + _viewPan.x,
                -(screen.y - c.y) / _viewZoom + _viewPan.y);
        }

        // ─── Sync transform (SyncPos + SyncQuaterion) ──────────────────────────────

        private Quaternion GetSyncRotation()
        {
            var s = _configObj as Spatial;
            if (s?.SyncQuaterion == null) return Quaternion.identity;
            var q = s.SyncQuaterion;
            float sq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (sq < 1e-6f) return Quaternion.identity;
            float k = 1f / Mathf.Sqrt(sq);
            return new Quaternion(q.x * k, q.y * k, q.z * k, q.w * k);
        }

        private Vector3 GetSyncPos()
        {
            var s = _configObj as Spatial;
            return s?.SyncPos != null ? new Vector3(s.SyncPos.x, s.SyncPos.y, s.SyncPos.z) : Vector3.zero;
        }

        private Vector2 LocalToWorld2D(Vector3_ p)
        {
            if (p == null) return Vector2.zero;
            Vector3 w = GetSyncPos() + GetSyncRotation() * new Vector3(p.x, p.y, p.z);
            return new Vector2(w.x, w.z);
        }

        private Vector2 LocalToWorld2D(Vector2 localXZ)
        {
            Vector3 w = GetSyncPos() + GetSyncRotation() * new Vector3(localXZ.x, 0f, localXZ.y);
            return new Vector2(w.x, w.z);
        }

        private Vector2 WorldToLocal2D(Vector2 worldXZ)
        {
            Vector3 local = Quaternion.Inverse(GetSyncRotation())
                          * (new Vector3(worldXZ.x, 0f, worldXZ.y) - GetSyncPos());
            return new Vector2(local.x, local.z);
        }

        // ─── Undo ──────────────────────────────────────────────────────────────────

        private string SerializeForUndo()
        {
            if (_configObj == null) return null;
            try { return JsonConvert.SerializeObject(_configObj); }
            catch { return null; }
        }

        private void PushUndoState(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (_undoStack.Count > 0 && _undoStack.Peek() == s) return; // dedupe
            if (_undoStack.Count >= UndoMaxDepth)
            {
                var arr = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = arr.Length - 2; i >= 0; i--) _undoStack.Push(arr[i]);
            }
            _undoStack.Push(s);
        }

        private void SnapshotUndo() => PushUndoState(SerializeForUndo());

        private void TryUndo()
        {
            if (_undoStack.Count == 0 || _configObj == null) return;
            string s = _undoStack.Pop();
            try
            {
                var t = _configObj.GetType();
                var newObj = JsonConvert.DeserializeObject(s, t);
                if (newObj != null)
                {
                    _configObj = newObj;
                    _isDirty = true;
                    _selKind = SelKind.None;
                    _selBoundary = _selVertex = _selObstacle = -1;
                    _dragKind = DragKind.None;
                    _dragOriginalPoints = null;
                    _multiplierTracked = false; // re-baseline against restored Multiplier
                    // Drop any active text-editor focus so float fields (SyncPos etc.)
                    // re-pick the restored value instead of showing their stale buffer.
                    GUI.FocusControl(null);
                    GUIUtility.keyboardControl = 0;
                    EditorGUIUtility.editingTextField = false;
                }
            }
            catch (Exception e) { Debug.LogError($"[SpatialConfig] Undo failed: {e.Message}"); }
            Repaint();
        }

        internal void HandleEditorEvents()
        {
            var e = Event.current;
            if (_configObj == null) return;

            // Multiplier change → rescale all boundary points & obstacles by (1+new)/(1+old).
            if (_configObj is Spatial sp)
            {
                if (!_multiplierTracked)
                {
                    _lastMultiplier = sp.Multiplier;
                    _multiplierTracked = true;
                }
                else if (Math.Abs(sp.Multiplier - _lastMultiplier) > 1e-9)
                {
                    ApplyMultiplierDelta(sp, _lastMultiplier, sp.Multiplier);
                    _lastMultiplier = sp.Multiplier;
                }
            }

            if (e.type == EventType.KeyDown && (e.control || e.command) && e.keyCode == KeyCode.Z)
            {
                TryUndo();
                e.Use();
                return;
            }
            if (e.type == EventType.MouseDown) SnapshotUndo();
        }

        private static void ApplyMultiplierDelta(Spatial s, double oldMul, double newMul)
        {
            double oldFactor = 1.0 + oldMul;
            double newFactor = 1.0 + newMul;
            if (Math.Abs(oldFactor) < 1e-6 || Math.Abs(newFactor) < 1e-6) return;
            float scale = (float)(newFactor / oldFactor);
            if (Mathf.Approximately(scale, 1f)) return;
            if (s?.Boundaries == null) return;
            foreach (var b in s.Boundaries)
            {
                if (b?.Points != null)
                    foreach (var p in b.Points)
                        if (p != null) { p.x *= scale; p.y *= scale; p.z *= scale; }
                if (b?.Obstacles != null)
                    foreach (var o in b.Obstacles)
                        if (o?.Position != null)
                        { o.Position.x *= scale; o.Position.y *= scale; o.Position.z *= scale; }
            }
        }

        private void FitViewToContent()
        {
            var spatial = _configObj as Spatial;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            bool hasAny = false;

            void include(float x, float z)
            {
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                hasAny = true;
            }

            if (spatial?.Boundaries != null)
            {
                foreach (var b in spatial.Boundaries)
                    if (b?.Points != null)
                        foreach (var p in b.Points)
                        {
                            if (p == null) continue;
                            Vector2 wp = LocalToWorld2D(p);
                            include(wp.x, wp.y);
                        }
            }
            if (spatial?.SyncPos != null) include(spatial.SyncPos.x, spatial.SyncPos.z);

            if (!hasAny || _orthoTex != null)
            {
                // No boundaries → fit to ortho image if any, else default.
                if (_orthoTex != null)
                {
                    float halfX = _orthoSizeMeters;
                    float halfZ = _orthoSizeMeters * (_orthoTex.height / (float)_orthoTex.width);
                    include(-halfX, -halfZ); include(+halfX, +halfZ);
                }
                else if (!hasAny)
                {
                    minX = -5; maxX = 5; minZ = -5; maxZ = 5;
                }
            }

            float padX = (maxX - minX) * 0.1f + 0.5f;
            float padZ = (maxZ - minZ) * 0.1f + 0.5f;
            minX -= padX; maxX += padX; minZ -= padZ; maxZ += padZ;

            _viewPan = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);

            float w = Mathf.Max(0.1f, maxX - minX);
            float h = Mathf.Max(0.1f, maxZ - minZ);
            float vw = Mathf.Max(50f, _viewportRect.width);
            float vh = Mathf.Max(50f, _viewportRect.height);
            _viewZoom = Mathf.Clamp(Mathf.Min(vw / w, vh / h), 2f, 400f);
            Repaint();
        }

        // ─── Hit testing ───────────────────────────────────────────────────────────

        private bool TryHitSyncPos(Vector2 mouse)
        {
            var spatial = _configObj as Spatial;
            if (spatial?.SyncPos == null) return false;
            Vector2 sp = WorldToScreen(new Vector2(spatial.SyncPos.x, spatial.SyncPos.z));
            return Vector2.Distance(sp, mouse) <= 9f;
        }

        private bool TryHitVertex(Vector2 mouse, out int boundary, out int vertex)
        {
            boundary = vertex = -1;
            var spatial = _configObj as Spatial;
            if (spatial?.Boundaries == null) return false;
            float bestDist = 8f;
            for (int bi = 0; bi < spatial.Boundaries.Count; bi++)
            {
                var b = spatial.Boundaries[bi];
                if (b?.Points == null) continue;
                for (int i = 0; i < b.Points.Count; i++)
                {
                    var p = b.Points[i]; if (p == null) continue;
                    Vector2 sp = WorldToScreen(LocalToWorld2D(p));
                    float d = Vector2.Distance(sp, mouse);
                    if (d <= bestDist) { bestDist = d; boundary = bi; vertex = i; }
                }
            }
            return boundary >= 0;
        }

        private bool TryHitObstacle(Vector2 mouse, out int boundary, out int obstacle)
        {
            boundary = obstacle = -1;
            var spatial = _configObj as Spatial;
            if (spatial?.Boundaries == null) return false;
            float bestDist = 12f;
            for (int bi = 0; bi < spatial.Boundaries.Count; bi++)
            {
                var b = spatial.Boundaries[bi];
                if (b?.Obstacles == null) continue;
                for (int oi = 0; oi < b.Obstacles.Count; oi++)
                {
                    var o = b.Obstacles[oi]; if (o?.Position == null) continue;
                    Vector2 sp = WorldToScreen(LocalToWorld2D(o.Position));
                    float d = Vector2.Distance(sp, mouse);
                    if (d <= bestDist) { bestDist = d; boundary = bi; obstacle = oi; }
                }
            }
            return boundary >= 0;
        }

        private bool TryHitEdge(Vector2 mouse, out int boundary, out int edgeIndex, out Vector2 localOnEdge, float pixelTol)
        {
            boundary = edgeIndex = -1;
            localOnEdge = Vector2.zero;
            var spatial = _configObj as Spatial;
            if (spatial?.Boundaries == null) return false;
            float bestDist = pixelTol;

            for (int bi = 0; bi < spatial.Boundaries.Count; bi++)
            {
                var b = spatial.Boundaries[bi];
                if (b?.Points == null || b.Points.Count < 2) continue;
                for (int i = 0; i < b.Points.Count; i++)
                {
                    var p1 = b.Points[i];
                    var p2 = b.Points[(i + 1) % b.Points.Count];
                    if (p1 == null || p2 == null) continue;
                    Vector2 sa = WorldToScreen(LocalToWorld2D(p1));
                    Vector2 sb = WorldToScreen(LocalToWorld2D(p2));
                    Vector2 closest = ProjectPointOnSegment(mouse, sa, sb);
                    float d = Vector2.Distance(closest, mouse);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        boundary = bi; edgeIndex = i;
                        // Convert clicked screen point back to LOCAL frame for inserting a point.
                        localOnEdge = WorldToLocal2D(ScreenToWorld(closest));
                    }
                }
            }
            return boundary >= 0;
        }

        private bool TryHitBoundaryInterior(Vector2 mouse, out int boundary)
        {
            boundary = -1;
            var spatial = _configObj as Spatial;
            if (spatial?.Boundaries == null) return false;
            // Polygon points are in LOCAL frame — convert mouse world → local before testing.
            Vector2 localMouse = WorldToLocal2D(ScreenToWorld(mouse));
            for (int bi = 0; bi < spatial.Boundaries.Count; bi++)
            {
                var b = spatial.Boundaries[bi];
                if (b?.Points == null || b.Points.Count < 3) continue;
                if (PointInPolygon(localMouse, b.Points)) { boundary = bi; return true; }
            }
            return false;
        }

        private static Vector2 ProjectPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            if (l2 < 1e-6f) return a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            return a + ab * t;
        }

        private static bool PointInPolygon(Vector2 pt, List<Vector3_> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = poly[i]; var pj = poly[j];
                if (pi == null || pj == null) continue;
                bool intersect = ((pi.z > pt.y) != (pj.z > pt.y)) &&
                                 (pt.x < (pj.x - pi.x) * (pt.y - pi.z) / ((pj.z - pi.z) + 1e-9f) + pi.x);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        // ─── Boundary mutation helpers ─────────────────────────────────────────────

        private void InsertVertex(int boundaryIdx, int at, Vector2 worldXZ)
        {
            var spatial = (Spatial)_configObj;
            var pts = spatial.Boundaries[boundaryIdx].Points;
            pts.Insert(at, new Vector3_(worldXZ.x, 0f, worldXZ.y));
            _isDirty = true;
        }

        private void DeleteSelectedVertex()
        {
            if (_selBoundary < 0 || _selVertex < 0) return;
            var spatial = (Spatial)_configObj;
            var pts = spatial.Boundaries[_selBoundary].Points;
            if (pts.Count <= 3) return; // keep at least a triangle
            pts.RemoveAt(_selVertex);
            _selVertex = -1;
            _selKind = SelKind.Boundary;
            _isDirty = true;
        }

        private void AddBoundary()
        {
            var spatial = (Spatial)_configObj;
            if (spatial.Boundaries == null) spatial.Boundaries = new List<Boundary_>();
            // Square 2×2m around current view center
            Vector2 c = _viewPan;
            var b = new Boundary_
            {
                Points = new List<Vector3_> {
                    new Vector3_(c.x - 1, 0, c.y - 1),
                    new Vector3_(c.x + 1, 0, c.y - 1),
                    new Vector3_(c.x + 1, 0, c.y + 1),
                    new Vector3_(c.x - 1, 0, c.y + 1),
                },
                BoundaryColor = new Vector3_(0.3f, 0.85f, 0.65f),
                Obstacles = new List<Obstacle_>(),
                MainBoundary = spatial.Boundaries.Count == 0,
                DisplayDistance = 3f,
            };
            spatial.Boundaries.Add(b);
            _selKind = SelKind.Boundary;
            _selBoundary = spatial.Boundaries.Count - 1;
            _selVertex = _selObstacle = -1;
            _isDirty = true;
        }

        private void RemoveSelectedBoundary()
        {
            if (_selBoundary < 0) return;
            var spatial = (Spatial)_configObj;
            spatial.Boundaries.RemoveAt(_selBoundary);
            _selKind = SelKind.None; _selBoundary = _selVertex = _selObstacle = -1;
            _isDirty = true;
        }

        private void AddObstacleToSelected()
        {
            if (_selBoundary < 0) return;
            var b = ((Spatial)_configObj).Boundaries[_selBoundary];
            if (b.Obstacles == null) b.Obstacles = new List<Obstacle_>();
            b.Obstacles.Add(new Obstacle_
            {
                Position = new Vector3_(_viewPan.x, 0f, _viewPan.y),
                Rotation = new Vector3_(),
                Size = ObstacleSize.Medium,
                Scale = 1f,
                SpecialId = -1,
            });
            _selKind = SelKind.Obstacle;
            _selObstacle = b.Obstacles.Count - 1;
            _selVertex = -1;
            _isDirty = true;
        }

        private void RemoveSelectedObstacle()
        {
            if (_selBoundary < 0 || _selObstacle < 0) return;
            var b = ((Spatial)_configObj).Boundaries[_selBoundary];
            b.Obstacles.RemoveAt(_selObstacle);
            _selObstacle = -1;
            _selKind = SelKind.Boundary;
            _isDirty = true;
        }

        // ─── Inspector sidebar ─────────────────────────────────────────────────────

        private void DrawInspector()
        {
            var sectionTitle = new GUIStyle(sectionStyle) { padding = new RectOffset(0, 0, 0, 6) };

            GUILayout.Space(8);
            _inspScroll = EditorGUILayout.BeginScrollView(_inspScroll, GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("INSPECTOR", sectionTitle);
            GUILayout.FlexibleSpace();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // ── Add buttons ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            if (GUILayout.Button("+ Boundary", GUILayout.Height(22))) AddBoundary();
            GUI.enabled = _selBoundary >= 0;
            if (GUILayout.Button("+ Obstacle", GUILayout.Height(22))) AddObstacleToSelected();
            GUI.enabled = true;
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            DrawBoundaryList();
            EditorGUILayout.Space(6);

            // ── Per-selection panels ──
            switch (_selKind)
            {
                case SelKind.Boundary: case SelKind.Vertex: DrawBoundaryInspector(); break;
                case SelKind.Obstacle:                       DrawObstacleInspector(); break;
                case SelKind.SyncPos:                        DrawSyncPosInspector();  break;
                default:                                     DrawNoSelection();       break;
            }

            // ── Spatial root fields (SyncPos, SyncQuaterion, Multiplier) ──
            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("SPATIAL", sectionTitle);
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            DrawSpatialFieldsCompact();

            EditorGUILayout.EndScrollView();
            GUILayout.Space(8);
        }

        private void DrawSpatialFieldsCompact()
        {
            if (_configObj == null || _knownFields == null) return;
            var s = _configObj as Spatial;
            if (s == null) return;

            // ── Friendly controls ──
            DrawFriendlySyncPos(s);
            DrawFriendlySyncQuatY(s);
            DrawFriendlyMultiplier(s);

            EditorGUILayout.Space(12);

            // ── Raw data ──
            var hdr = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = colTextMuted },
                padding   = new RectOffset(0, 0, 2, 2),
            };
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("RAW DATA", hdr);
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);

            foreach (var field in _knownFields)
            {
                if (field.Name == "Boundaries") continue;
                DrawRawSpatialField(field);
            }
        }

        private void DrawFriendlySyncPos(Spatial s)
        {
            if (s.SyncPos == null) s.SyncPos = new Vector3_();
            EditorGUI.BeginChangeCheck();
            float xCm = s.SyncPos.x * 100f;
            float zCm = s.SyncPos.z * 100f;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Pos X", fieldLabelStyle, GUILayout.Width(70));
            xCm = EditorGUILayout.FloatField(xCm);
            GUILayout.Label("cm", EditorStyles.miniLabel, GUILayout.Width(22));
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Pos Z", fieldLabelStyle, GUILayout.Width(70));
            zCm = EditorGUILayout.FloatField(zCm);
            GUILayout.Label("cm", EditorStyles.miniLabel, GUILayout.Width(22));
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                s.SyncPos.x = xCm / 100f;
                s.SyncPos.z = zCm / 100f;
                _isDirty = true;
            }
        }

        private void DrawFriendlySyncQuatY(Spatial s)
        {
            if (s.SyncQuaterion == null) s.SyncQuaterion = new Vector4_(Quaternion.identity);
            // Read current Y angle from quaternion (normalize defensively)
            float yAngle = 0f;
            {
                var q = s.SyncQuaterion;
                float sq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                if (sq > 1e-6f)
                {
                    float k = 1f / Mathf.Sqrt(sq);
                    yAngle = new Quaternion(q.x * k, q.y * k, q.z * k, q.w * k).eulerAngles.y;
                }
                if (yAngle < 0f) yAngle += 360f;
                if (yAngle >= 360f) yAngle -= 360f;
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Rot Y", fieldLabelStyle, GUILayout.Width(70));
            yAngle = GUILayout.HorizontalSlider(yAngle, 0f, 360f, GUILayout.ExpandWidth(true));
            GUILayout.Label($"{Mathf.RoundToInt(yAngle)}°", EditorStyles.miniLabel, GUILayout.Width(36));
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                var nq = Quaternion.Euler(0f, yAngle, 0f);
                s.SyncQuaterion.x = nq.x;
                s.SyncQuaterion.y = nq.y;
                s.SyncQuaterion.z = nq.z;
                s.SyncQuaterion.w = nq.w;
                _isDirty = true;
            }
        }

        private void DrawFriendlyMultiplier(Spatial s)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Multiplier", fieldLabelStyle, GUILayout.Width(70));
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            float pct = (float)(s.Multiplier * 100.0);
            pct = GUILayout.HorizontalSlider(pct, 0f, 200f, GUILayout.ExpandWidth(true));
            GUILayout.Label($"{Mathf.RoundToInt(pct)} %", EditorStyles.miniLabel, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();
            var warnStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                fontSize = 9,
                normal   = { textColor = colWarn },
            };
            GUILayout.Label("Values above 10% may degrade the user experience.", warnStyle);
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                s.Multiplier = pct / 100.0;
                _isDirty = true;
            }
        }

        private void DrawRawSpatialField(System.Reflection.FieldInfo field)
        {
            var value = field.GetValue(_configObj);
            var type  = field.FieldType;
            EditorGUI.BeginChangeCheck();
            object newValue = value;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label(field.Name, fieldLabelStyle, GUILayout.Width(70));

            if (type.Name == "Vector3_")
            {
                if (value == null) { value = Activator.CreateInstance(type); newValue = value; }
                var v = new Vector3(GetField(value, "x"), GetField(value, "y"), GetField(value, "z"));
                Vector3 nv = EditorGUILayout.Vector3Field(GUIContent.none, v);
                if (nv != v) { SetField(value, "x", nv.x); SetField(value, "y", nv.y); SetField(value, "z", nv.z); }
            }
            else if (type.Name == "Vector4_")
            {
                if (value == null) { value = Activator.CreateInstance(type); newValue = value; }
                var v = new Vector4(GetField(value, "x"), GetField(value, "y"), GetField(value, "z"), GetField(value, "w"));
                EditorGUILayout.BeginVertical();
                EditorGUILayout.BeginHorizontal();
                v.x = EditorGUILayout.FloatField(v.x);
                v.y = EditorGUILayout.FloatField(v.y);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                v.z = EditorGUILayout.FloatField(v.z);
                v.w = EditorGUILayout.FloatField(v.w);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                SetField(value, "x", v.x); SetField(value, "y", v.y);
                SetField(value, "z", v.z); SetField(value, "w", v.w);
            }
            else if (type == typeof(float))   newValue = EditorGUILayout.FloatField((float)value);
            else if (type == typeof(double))  newValue = EditorGUILayout.DoubleField((double)value);
            else if (type == typeof(int))     newValue = EditorGUILayout.IntField((int)value);
            else if (type == typeof(bool))    newValue = EditorGUILayout.Toggle((bool)value);
            else if (type == typeof(string))  newValue = EditorGUILayout.TextField((string)value ?? "");
            else { GUILayout.Label("(complex)", readOnlyStyle); }

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                field.SetValue(_configObj, newValue);
                _isDirty = true;
            }
        }

        private void DrawNoSelection()
        {
            var s = new GUIStyle {
                fontSize = 10, fontStyle = FontStyle.Italic, wordWrap = true,
                padding = new RectOffset(10, 10, 6, 6),
                normal = { textColor = colTextMuted },
            };
            GUILayout.Label(
                "Aucune sélection.\n\n" +
                "• Clic gauche → sélectionner\n" +
                "• Drag vertex → déplacer\n" +
                "• Clic sur arête → insérer un point\n" +
                "• Alt+clic vertex → supprimer\n" +
                "• Shift+drag boundary → déplacer toute la zone\n" +
                "• Molette → zoom · Clic milieu → pan",
                s);
        }

        private void DrawBoundaryList()
        {
            var spatial = _configObj as Spatial;
            int count = spatial?.Boundaries?.Count ?? 0;

            // Header
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label($"Boundaries ({count})", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();

            // SyncPos quick-select chip
            bool syncSel = _selKind == SelKind.SyncPos;
            var syncStyle = new GUIStyle(EditorStyles.miniButton);
            if (syncSel)
            {
                syncStyle.normal.textColor = colAccent;
                syncStyle.fontStyle = FontStyle.Bold;
            }
            if (GUILayout.Button("SyncPos", syncStyle, GUILayout.Width(70), GUILayout.Height(18)))
            {
                if (spatial != null)
                {
                    if (spatial.SyncPos == null) spatial.SyncPos = new Vector3_();
                    _selKind = SelKind.SyncPos;
                    _selBoundary = _selVertex = _selObstacle = -1;
                }
            }
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            if (count == 0) return;

            // List rows
            for (int i = 0; i < count; i++)
            {
                var b = spatial.Boundaries[i];
                if (b == null) continue;

                bool isSel = _selBoundary == i &&
                             (_selKind == SelKind.Boundary || _selKind == SelKind.Vertex || _selKind == SelKind.Obstacle);

                Color col = (b.BoundaryColor != null)
                    ? new Color(b.BoundaryColor.x, b.BoundaryColor.y, b.BoundaryColor.z)
                    : Color.green;

                Rect row = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
                if (Event.current.type == EventType.Repaint && isSel)
                    EditorGUI.DrawRect(new Rect(row.x + 4, row.y, row.width - 12, row.height),
                        new Color(col.r, col.g, col.b, 0.18f));

                GUILayout.Space(8);

                // Color swatch
                Rect swatch = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12), GUILayout.Height(12));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(swatch, col);

                GUILayout.Space(4);

                // Label (clickable area)
                string label = $"#{i}" + (b.MainBoundary ? "  ★" : "")
                                       + $"  ·  {(b.Points?.Count ?? 0)} pts";
                if (b.Obstacles != null && b.Obstacles.Count > 0) label += $"  ·  {b.Obstacles.Count} obs";

                var lblStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                if (isSel) { lblStyle.fontStyle = FontStyle.Bold; lblStyle.normal.textColor = Color.white; }

                if (GUILayout.Button(label, lblStyle, GUILayout.ExpandWidth(true), GUILayout.Height(18)))
                {
                    _selKind = SelKind.Boundary;
                    _selBoundary = i;
                    _selVertex = _selObstacle = -1;
                    Repaint();
                }

                // Frame button (recenter view on this boundary)
                if (GUILayout.Button("⌖", EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(18)))
                {
                    _selKind = SelKind.Boundary;
                    _selBoundary = i;
                    _selVertex = _selObstacle = -1;
                    FitViewToBoundary(i);
                    Repaint();
                }

                GUILayout.Space(8);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void FitViewToBoundary(int idx)
        {
            var spatial = _configObj as Spatial;
            if (spatial?.Boundaries == null || idx < 0 || idx >= spatial.Boundaries.Count) return;
            var b = spatial.Boundaries[idx];
            if (b?.Points == null || b.Points.Count == 0) return;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in b.Points)
            {
                if (p == null) continue;
                Vector2 wp = LocalToWorld2D(p);
                if (wp.x < minX) minX = wp.x; if (wp.x > maxX) maxX = wp.x;
                if (wp.y < minZ) minZ = wp.y; if (wp.y > maxZ) maxZ = wp.y;
            }
            float padX = Mathf.Max(0.5f, (maxX - minX) * 0.25f);
            float padZ = Mathf.Max(0.5f, (maxZ - minZ) * 0.25f);
            minX -= padX; maxX += padX; minZ -= padZ; maxZ += padZ;

            _viewPan = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
            float w = Mathf.Max(0.1f, maxX - minX);
            float h = Mathf.Max(0.1f, maxZ - minZ);
            float vw = Mathf.Max(50f, _viewportRect.width);
            float vh = Mathf.Max(50f, _viewportRect.height);
            _viewZoom = Mathf.Clamp(Mathf.Min(vw / w, vh / h), 2f, 400f);
        }

        private void DrawBoundaryInspector()
        {
            var spatial = (Spatial)_configObj;
            if (spatial.Boundaries == null || _selBoundary < 0 || _selBoundary >= spatial.Boundaries.Count)
            { DrawNoSelection(); return; }
            var b = spatial.Boundaries[_selBoundary];
            if (b == null) { DrawNoSelection(); return; }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label($"Boundary #{_selBoundary}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            var delStyle = new GUIStyle(GUI.skin.button) { normal = { textColor = colError } };
            if (GUILayout.Button("✕", delStyle, GUILayout.Width(22), GUILayout.Height(20)))
                RemoveSelectedBoundary();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            // Color (separate change-check so picker close commits reliably)
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Color", fieldLabelStyle, GUILayout.Width(120));
            Color cur = b.BoundaryColor != null
                ? new Color(b.BoundaryColor.x, b.BoundaryColor.y, b.BoundaryColor.z)
                : Color.green;
            EditorGUI.BeginChangeCheck();
            Color next = EditorGUILayout.ColorField(cur);
            if (EditorGUI.EndChangeCheck())
            {
                if (b.BoundaryColor == null) b.BoundaryColor = new Vector3_();
                b.BoundaryColor.x = next.r;
                b.BoundaryColor.y = next.g;
                b.BoundaryColor.z = next.b;
                _isDirty = true;
                Repaint();
            }
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            DrawInspBool(  "Main",            ref b.MainBoundary);
            DrawInspBool(  "Reverse",         ref b.Reverse);
            DrawInspBool(  "AlertLimit",      ref b.AlertLimit);
            DrawInspBool(  "MoreVisible",     ref b.BoundaryMoreVisible);
            DrawInspFloat( "DisplayDistance", ref b.DisplayDistance);
            DrawInspBool(  "HideLineFar",      ref b.HideLineFar);

            if (EditorGUI.EndChangeCheck()) _isDirty = true;

            // Vertex list (collapsible)
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label($"Points ({b.Points?.Count ?? 0})", EditorStyles.miniBoldLabel);
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            if (b.Points != null)
            {
                int del = -1;
                for (int i = 0; i < b.Points.Count; i++)
                {
                    var p = b.Points[i]; if (p == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(8);
                    bool isSel = _selKind == SelKind.Vertex && _selVertex == i;
                    var lbl = new GUIStyle(fieldLabelStyle);
                    if (isSel) lbl.normal.textColor = colAccent;
                    GUILayout.Label($"#{i}", lbl, GUILayout.Width(28));
                    EditorGUI.BeginChangeCheck();
                    float nx = EditorGUILayout.FloatField(p.x, GUILayout.MinWidth(40));
                    float nz = EditorGUILayout.FloatField(p.z, GUILayout.MinWidth(40));
                    if (EditorGUI.EndChangeCheck())
                    {
                        p.x = nx; p.z = nz;
                        _isDirty = true;
                    }
                    if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18))) del = i;
                    GUILayout.Space(8);
                    EditorGUILayout.EndHorizontal();
                }
                if (del >= 0 && b.Points.Count > 3)
                {
                    b.Points.RemoveAt(del);
                    if (_selVertex == del) { _selVertex = -1; _selKind = SelKind.Boundary; }
                    _isDirty = true;
                }
            }
        }

        private void DrawObstacleInspector()
        {
            var spatial = (Spatial)_configObj;
            if (spatial.Boundaries == null || _selBoundary < 0 || _selBoundary >= spatial.Boundaries.Count
                || _selObstacle < 0)
            { DrawNoSelection(); return; }
            var b = spatial.Boundaries[_selBoundary];
            if (b?.Obstacles == null || _selObstacle >= b.Obstacles.Count) { DrawNoSelection(); return; }
            var o = b.Obstacles[_selObstacle];
            if (o == null) { DrawNoSelection(); return; }
            if (o.Position == null) o.Position = new Vector3_();
            if (o.Rotation == null) o.Rotation = new Vector3_();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label($"Obstacle  ·  B#{_selBoundary} / O#{_selObstacle}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            var delStyle = new GUIStyle(GUI.skin.button) { normal = { textColor = colError } };
            if (GUILayout.Button("✕", delStyle, GUILayout.Width(22), GUILayout.Height(20)))
                RemoveSelectedObstacle();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();

            // Position
            DrawInspVector3("Position", o.Position);
            DrawInspVector3("Rotation", o.Rotation);
            DrawInspFloat(  "Scale",      ref o.Scale);
            DrawInspInt(    "SpecialId",  ref o.SpecialId);

            // Size enum
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Size", fieldLabelStyle, GUILayout.Width(120));
            o.Size = (ObstacleSize)EditorGUILayout.EnumPopup(o.Size);
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck()) _isDirty = true;
        }

        private void DrawSyncPosInspector()
        {
            var spatial = (Spatial)_configObj;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("SyncPos", EditorStyles.boldLabel);
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            if (spatial.SyncPos == null) spatial.SyncPos = new Vector3_();
            EditorGUI.BeginChangeCheck();
            DrawInspVector3("Pos", spatial.SyncPos);
            if (EditorGUI.EndChangeCheck()) _isDirty = true;
        }

        // ─── Inspector field helpers ──────────────────────────────────────────────

        private void DrawInspBool(string label, ref bool value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label(label, fieldLabelStyle, GUILayout.Width(120));
            value = EditorGUILayout.Toggle(value);
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInspFloat(string label, ref float value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label(label, fieldLabelStyle, GUILayout.Width(120));
            value = EditorGUILayout.FloatField(value);
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInspInt(string label, ref int value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label(label, fieldLabelStyle, GUILayout.Width(120));
            value = EditorGUILayout.IntField(value);
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInspVector3(string label, Vector3_ v)
        {
            if (v == null) return;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label(label, fieldLabelStyle, GUILayout.Width(120));
            v.x = EditorGUILayout.FloatField(v.x, GUILayout.MinWidth(40));
            v.y = EditorGUILayout.FloatField(v.y, GUILayout.MinWidth(40));
            v.z = EditorGUILayout.FloatField(v.z, GUILayout.MinWidth(40));
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        // ─── Ortho loading ─────────────────────────────────────────────────────────

        private void LoadOrthoFiles()
        {
            _orthoFiles.Clear();
            _orthoIdx = -1;
            if (_orthoTex != null) { DestroyImmediate(_orthoTex); _orthoTex = null; }

            string baseDir = EditorPrefs.GetString("VBO_OrthoSourcePath");
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) { _orthoDir = null; return; }

            // Optional sub-folder per GameID
            string gameId = "";
            try
            {
                string gidPath = Application.streamingAssetsPath + "/GameID.txt";
                if (File.Exists(gidPath)) gameId = File.ReadAllText(gidPath).Trim();
            }
            catch { }
            string dir = baseDir;
            if (!string.IsNullOrEmpty(gameId))
            {
                string sub = Path.Combine(baseDir, gameId);
                if (Directory.Exists(sub)) dir = sub;
            }
            _orthoDir = dir;

            try
            {
                foreach (var f in Directory.GetFiles(dir, "*.jpg", SearchOption.TopDirectoryOnly))
                    _orthoFiles.Add(f);
            }
            catch { }
            _orthoFiles.Sort();

            if (_orthoFiles.Count == 0) return;

            // Auto-pick by current scene name
            string scene = SceneManager.GetActiveScene().name;
            int pick = -1;
            for (int i = 0; i < _orthoFiles.Count; i++)
            {
                string name = Path.GetFileNameWithoutExtension(_orthoFiles[i]);
                int u = name.LastIndexOf('_');
                string sceneOfFile = (u > 0) ? name.Substring(0, u) : name;
                if (!string.IsNullOrEmpty(scene) && sceneOfFile == scene) { pick = i; break; }
            }
            if (pick < 0) pick = 0;
            LoadOrthoAt(pick);
        }

        private void LoadOrthoAt(int idx)
        {
            if (idx < 0 || idx >= _orthoFiles.Count) return;
            _orthoIdx = idx;
            string path = _orthoFiles[idx];

            if (_orthoTex != null) { DestroyImmediate(_orthoTex); _orthoTex = null; }
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var raw = new Texture2D(2, 2, TextureFormat.RGB24, false);
                raw.LoadImage(bytes);
                _orthoTex = RotateCW90(raw);
                _orthoTex.hideFlags = HideFlags.HideAndDontSave;
                DestroyImmediate(raw);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SpatialConfig] Ortho load failed: {e.Message}");
                _orthoTex = null;
            }

            // Parse orthoSize from filename (last "_<digits>" before .jpg)
            _orthoSizeMeters = 10f;
            string name = Path.GetFileNameWithoutExtension(path);
            var m = Regex.Match(name, @"_(\d+(?:\.\d+)?)$");
            if (m.Success && float.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v))
                _orthoSizeMeters = v;
        }

        // 90° CW rotation: aligns ortho capture (image_up = +X world) to viewport (right = +X, up = +Z).
        private static Texture2D RotateCW90(Texture2D src)
        {
            int Ws = src.width, Hs = src.height;
            int Wd = Hs, Hd = Ws;
            var dst = new Texture2D(Wd, Hd, TextureFormat.RGB24, false);
            var srcPx = src.GetPixels32();
            var dstPx = new Color32[Wd * Hd];
            for (int v = 0; v < Hd; v++)
            for (int u = 0; u < Wd; u++)
                dstPx[v * Wd + u] = srcPx[u * Ws + (Ws - 1 - v)];
            dst.SetPixels32(dstPx);
            dst.Apply(false, false);
            return dst;
        }

        private void CycleOrtho(int dir)
        {
            if (_orthoFiles.Count == 0) return;
            int n = _orthoFiles.Count;
            int next = ((_orthoIdx + dir) % n + n) % n;
            LoadOrthoAt(next);
            Repaint();
        }
    }
}
