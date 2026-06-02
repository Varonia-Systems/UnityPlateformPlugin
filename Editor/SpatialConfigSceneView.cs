// ============================================================================
// SpatialConfigSceneView.cs
// Scene-view overlay that draws the SpatialConfig boundaries, toggled from the
// "Varonia" menu (Varonia ▸ Show Boundary In Scene).
//
// Standalone: works whether or not the SpatialConfig editor window is open.
//   • If the window IS open, the live (possibly-unsaved) config is drawn so the
//     scene reflects edits in real time.
//   • If it's closed, the boundary is loaded from the same JSON file the editor
//     uses (…/Varonia/NewSpatial.json).
//
// 2019.4-safe: only APIs available since Unity 2019.1 (SceneView.duringSceneGui,
// Handles.DrawAAPolyLine / SphereHandleCap / DrawWireDisc). No C# 8 syntax.
// ============================================================================
using System;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice
{
    [InitializeOnLoad]
    internal static class SpatialBoundarySceneOverlay
    {
        private const string MenuPath  = "Varonia/Show Boundary In Scene";
        private const string PrefKey   = "VBO_ShowBoundaryInScene";

        private static Spatial _diskCache; // boundary loaded from JSON (window-closed case)

        // ─── Boot ───────────────────────────────────────────────────────────────
        static SpatialBoundarySceneOverlay()
        {
            if (Enabled)
            {
                SceneView.duringSceneGui -= OnScene;
                SceneView.duringSceneGui += OnScene;
            }
        }

        private static bool Enabled
        {
            get { return EditorPrefs.GetBool(PrefKey, false); }
            set { EditorPrefs.SetBool(PrefKey, value); }
        }

        // ─── Menu toggle ────────────────────────────────────────────────────────
        [MenuItem(MenuPath, false, 100)]
        private static void Toggle()
        {
            bool on = !Enabled;
            Enabled = on;

            SceneView.duringSceneGui -= OnScene;
            if (on)
            {
                ReloadDisk();                       // refresh the on-disk fallback
                SceneView.duringSceneGui += OnScene;
            }
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        /// <summary>Repaint the scene if the overlay is on (called by the editor on edit).</summary>
        public static void RequestRepaint()
        {
            if (Enabled) SceneView.RepaintAll();
        }

        // ─── Data source: live window first, else on-disk JSON ────────────────────
        private static Spatial GetSpatial()
        {
            var windows = Resources.FindObjectsOfTypeAll<SpatialConfigReflectionEditor>();
            if (windows != null)
            {
                for (int i = 0; i < windows.Length; i++)
                {
                    if (windows[i] == null) continue;
                    var live = windows[i].GetLiveSpatial();
                    if (live != null) return live;
                }
            }
            return _diskCache;
        }

        private static void ReloadDisk()
        {
            _diskCache = null;
            try
            {
                string path = Path.Combine(
                    Application.persistentDataPath
                        .Replace(Application.companyName + "/" + Application.productName, "Varonia"),
                    "NewSpatial.json");
                if (File.Exists(path))
                    _diskCache = JsonConvert.DeserializeObject<Spatial>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Varonia] Show Boundary In Scene: lecture JSON échouée — " + e.Message);
            }
        }

        // ─── Draw ────────────────────────────────────────────────────────────────
        private static void OnScene(SceneView sv)
        {
            Spatial spatial = GetSpatial();
            if (spatial == null || spatial.Boundaries == null) return;

            Vector3    origin = PosOf(spatial);
            Quaternion rot    = RotOf(spatial);

            for (int bi = 0; bi < spatial.Boundaries.Count; bi++)
            {
                Boundary_ b = spatial.Boundaries[bi];
                if (b == null || b.Points == null || b.Points.Count < 2) continue;

                Color col = (b.BoundaryColor != null)
                    ? new Color(b.BoundaryColor.x, b.BoundaryColor.y, b.BoundaryColor.z)
                    : Color.green;

                int n = b.Points.Count;
                Vector3[] world = new Vector3[n + 1];
                for (int i = 0; i < n; i++)
                {
                    Vector3_ p = b.Points[i];
                    Vector3 local = (p != null) ? new Vector3(p.x, p.y, p.z) : Vector3.zero;
                    world[i] = origin + rot * local;
                }
                world[n] = world[0]; // close loop

                Handles.color = col;
                Handles.DrawAAPolyLine(b.MainBoundary ? 5f : 3f, world);

                for (int i = 0; i < n; i++)
                {
                    float s = HandleUtility.GetHandleSize(world[i]) * 0.06f;
                    Handles.SphereHandleCap(0, world[i], Quaternion.identity, s, EventType.Repaint);
                }

                if (b.Obstacles != null)
                {
                    Handles.color = new Color(1f, 0.55f, 0.12f, 1f);
                    for (int oi = 0; oi < b.Obstacles.Count; oi++)
                    {
                        Obstacle_ o = b.Obstacles[oi];
                        if (o == null || o.Position == null) continue;
                        Vector3 wp = origin + rot * new Vector3(o.Position.x, o.Position.y, o.Position.z);
                        Handles.DrawWireDisc(wp, Vector3.up, Mathf.Max(0.1f, o.Scale * 0.3f));
                    }
                }
            }
        }

        // ─── Transform helpers (from the Spatial's own Sync fields) ───────────────
        private static Vector3 PosOf(Spatial s)
        {
            return s.SyncPos != null ? new Vector3(s.SyncPos.x, s.SyncPos.y, s.SyncPos.z) : Vector3.zero;
        }

        private static Quaternion RotOf(Spatial s)
        {
            if (s.SyncQuaterion == null) return Quaternion.identity;
            var q = s.SyncQuaterion;
            float sq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (sq < 1e-6f) return Quaternion.identity;
            float k = 1f / Mathf.Sqrt(sq);
            return new Quaternion(q.x * k, q.y * k, q.z * k, q.w * k);
        }
    }
}
