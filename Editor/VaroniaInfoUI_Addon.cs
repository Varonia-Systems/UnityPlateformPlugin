using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice
{
    public class VaroniaInfoUI_Addon : EditorWindow
    {
        // ─── State ────────────────────────────────────────────────────────────────
        private string _gameId            = "";
        private string _buildPath         = "";
        private string _buildServerPath   = "";
        private string _contentSourcePath = "";
        private string _orthoSourcePath   = "";
        private string _7ZipPath          = "";
        private bool   _forceDisableBoundary;
        private bool   _isDirty;

        private const string BaseKey = "VBO_";

        // ── Style cache ──
        static bool     stylesBuilt;
        static GUIStyle headerStyle;
        static GUIStyle sectionStyle;
        static GUIStyle footerStyle;
        static GUIStyle buttonStyle;
        static GUIStyle tagStyle;
        static GUIStyle fieldLabelStyle;

        // ── Colors ──
        static readonly Color colBg          = new Color(0.11f, 0.11f, 0.14f, 1f);
        static readonly Color colCard        = new Color(0.15f, 0.15f, 0.19f, 1f);
        static readonly Color colAccent      = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color colAccentDim   = new Color(0.30f, 0.85f, 0.65f, 0.15f);
        static readonly Color colWarn        = new Color(1f,    0.75f, 0.30f, 1f);
        static readonly Color colWarnDim     = new Color(1f,    0.75f, 0.30f, 0.12f);
        static readonly Color colTextPrimary = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color colTextSecond  = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color colTextMuted   = new Color(0.40f, 0.40f, 0.47f, 1f);
        static readonly Color colDivider     = new Color(1f,    1f,    1f,    0.06f);
        static readonly Color colBtnNormal   = new Color(0.22f, 0.22f, 0.28f, 1f);
        static readonly Color colBtnHover    = new Color(0.28f, 0.28f, 0.35f, 1f);

        // ── Textures (lazy) ──
        static Texture2D texBtn, texBtnHover, texAccentSolid, texWarnSolid;

        // ─────────────────────────────────────────────────────────────────────────

#if VBO_ADVANCED
        [MenuItem("Varonia/Project Settings")]
#endif
        public static void ShowWindow()
        {
            var w = GetWindow<VaroniaInfoUI_Addon>("Project Settings");
            w.minSize = new Vector2(520, 500);
        }

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            VaroniaProjectSettings.ApplyForceDisableBoundary();
            Load();
        }

        // ─── Load ─────────────────────────────────────────────────────────────────

        private void Load()
        {
            // GameID depuis StreamingAssets (accessible au runtime)
            string gamePath = Application.streamingAssetsPath + "/GameID.txt";
            if (!Directory.Exists(Application.streamingAssetsPath))
                Directory.CreateDirectory(Application.streamingAssetsPath);

            if (!File.Exists(gamePath))
            {
                File.WriteAllText(gamePath, "9999");
                _gameId = "9999";
            }
            else
            {
                _gameId = File.ReadAllText(gamePath);
            }

            // Chemins depuis EditorPrefs (stockage local, non versionné)
            _buildPath         = EditorPrefs.GetString(BaseKey + "BuildPath");
            _buildServerPath   = EditorPrefs.GetString(BaseKey + "BuildServerPath");
            _contentSourcePath = EditorPrefs.GetString(BaseKey + "ContentSourcePath");
            _orthoSourcePath   = EditorPrefs.GetString(BaseKey + "OrthoSourcePath");
            _7ZipPath          = EditorPrefs.GetString(BaseKey + "7ZipPath");
            _forceDisableBoundary = EditorPrefs.GetBool(BaseKey + "ForceDisableBoundaryWarning", false);

            SetAdvBoundaryForceDisable(_forceDisableBoundary);

            _isDirty = false;
        }

        private static void SetAdvBoundaryForceDisable(bool value)
        {
            var type = Type.GetType("VaroniaBackOffice.AdvBoundary, VboAdvBoundary.Runtime");
            if (type == null) return;
            var prop = type.GetProperty("ForceDisableBoundaryWarning", BindingFlags.Public | BindingFlags.Static);
            if (prop != null) prop.SetValue(null, value);
        }

        // ─── Save ─────────────────────────────────────────────────────────────────

        private void Save()
        {
            if (!Directory.Exists(Application.streamingAssetsPath))
                Directory.CreateDirectory(Application.streamingAssetsPath);
            try   { File.WriteAllText(Application.streamingAssetsPath + "/GameID.txt", _gameId); }
            catch (Exception e) { Debug.LogError($"[Project Settings] GameID : {e.Message}"); }

            EditorPrefs.SetString(BaseKey + "BuildPath",         _buildPath);
            EditorPrefs.SetString(BaseKey + "BuildServerPath",   _buildServerPath);
            EditorPrefs.SetString(BaseKey + "ContentSourcePath", _contentSourcePath);
            EditorPrefs.SetString(BaseKey + "OrthoSourcePath",   _orthoSourcePath);
            EditorPrefs.SetString(BaseKey + "7ZipPath",          _7ZipPath);
            EditorPrefs.SetBool(BaseKey + "ForceDisableBoundaryWarning", _forceDisableBoundary);

            SetAdvBoundaryForceDisable(_forceDisableBoundary);

            _isDirty = false;
            Debug.Log("[Project Settings] Sauvegardé.");
        }

        // ── Texture helpers ──────────────────────────────────────────────────────

        static Texture2D MakeTex(Color col)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, col);
            t.Apply();
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
                        inside = new Vector2(x - radius,            y - radius).magnitude           <= radius;
                    else if (x >= w - radius && y < radius)
                        inside = new Vector2(x - (w - radius - 1),  y - radius).magnitude           <= radius;
                    else if (x < radius      && y >= h - radius)
                        inside = new Vector2(x - radius,            y - (h - radius - 1)).magnitude <= radius;
                    else if (x >= w - radius && y >= h - radius)
                        inside = new Vector2(x - (w - radius - 1),  y - (h - radius - 1)).magnitude <= radius;
                    t.SetPixel(x, y, inside ? col : clear);
                }
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        // ── Style builder ─────────────────────────────────────────────────────────

        void BuildStyles()
        {
            if (stylesBuilt) return;
            stylesBuilt = true;

            texBtn         = MakeRoundedTex(32, 32, colBtnNormal, 5);
            texBtnHover    = MakeRoundedTex(32, 32, colBtnHover,  5);
            texAccentSolid = MakeRoundedTex(32, 32, colAccent,    5);
            texWarnSolid   = MakeRoundedTex(32, 32, colWarn,      5);

            headerStyle = new GUIStyle
            {
                fontSize  = 18,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = colTextPrimary },
                padding   = new RectOffset(0, 0, 0, 0),
            };

            tagStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = colAccent, background = MakeRoundedTex(32, 32, colAccentDim, 6) },
                padding   = new RectOffset(8, 8, 3, 3),
                margin    = new RectOffset(0, 4, 0, 0),
                border    = new RectOffset(6, 6, 6, 6),
            };

            sectionStyle = new GUIStyle
            {
                fontSize  = 10,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = colTextMuted },
                padding   = new RectOffset(0, 0, 6, 2),
                margin    = new RectOffset(0, 0, 4, 0),
            };

            footerStyle = new GUIStyle
            {
                fontSize  = 9,
                normal    = { textColor = colTextMuted },
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(0, 0, 6, 6),
            };

            buttonStyle = new GUIStyle
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = colTextPrimary, background = texBtn },
                hover     = { textColor = Color.white,   background = texBtnHover },
                active    = { textColor = Color.white,   background = texAccentSolid },
                padding   = new RectOffset(16, 16, 8, 8),
                margin    = new RectOffset(2, 2, 2, 2),
                border    = new RectOffset(5, 5, 5, 5),
            };

            fieldLabelStyle = new GUIStyle
            {
                fontSize = 11,
                normal   = { textColor = colTextSecond },
                padding  = new RectOffset(0, 0, 3, 3),
            };
        }

        // ─── GUI ──────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            BuildStyles();

            // Fond sombre pleine fenêtre
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colBg);

            EditorGUILayout.Space(12);

            // ── Titre ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.Label("PROJECT SETTINGS", headerStyle);
            GUILayout.FlexibleSpace();

            Color  pillCol  = _isDirty ? colWarn : colAccent;
            string pillText = _isDirty ? "  UNSAVED  " : "  SAVED  ";
            var    pill     = new GUIStyle(tagStyle);
            pill.normal.textColor  = pillCol;
            pill.normal.background = MakeRoundedTex(32, 32, new Color(pillCol.r, pillCol.g, pillCol.b, 0.15f), 6);
            GUILayout.Label(pillText, pill);

            GUILayout.Space(16);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(16);

            // ── Card Projet ──
            DrawCard(() =>
            {
                DrawSectionLabel("PROJET");
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(6);
                DrawFieldRow("Game ID", ref _gameId);
                EditorGUILayout.Space(6);
                DrawDivider();
                EditorGUILayout.Space(6);

                DrawSectionLabel("PARAMÈTRES ÉDITEUR");
                EditorGUILayout.Space(4);
                EditorGUI.BeginChangeCheck();
                bool force = EditorGUILayout.ToggleLeft("Ignore 'Out of Zone' alert ", _forceDisableBoundary, fieldLabelStyle);
                if (EditorGUI.EndChangeCheck())
                {
                    _forceDisableBoundary = force;
                    _isDirty = true;
                    EditorPrefs.SetBool(BaseKey + "ForceDisableBoundaryWarning", force);
                    VaroniaProjectSettings.ApplyForceDisableBoundary();
                }
            }, colAccent);

            EditorGUILayout.Space(8);

            // ── Card Chemins ──
            DrawCard(() =>
            {
                DrawSectionLabel("CHEMINS  ·  LOCAL MACHINE");
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(6);
                DrawPathRow("Build",          ref _buildPath,         isFolder: true,  tooltip: "Path where the game is built");
                EditorGUILayout.Space(3);
                DrawPathRow("Build Server",   ref _buildServerPath,   isFolder: true,  tooltip: "Path where the build is copied after compilation and zipping");
                EditorGUILayout.Space(3);
                DrawPathRow("Content Source", ref _contentSourcePath, isFolder: true,  tooltip: "Path to the game's content");
                EditorGUILayout.Space(3);
                DrawPathRow("Ortho Source",   ref _orthoSourcePath,   isFolder: true,  tooltip: "Path to the orthographic views");
                EditorGUILayout.Space(3);
                DrawPathRow("7-Zip",          ref _7ZipPath,          isFolder: false, isFile: true);
            }, colWarn);

            EditorGUILayout.Space(8);

            // ── Card Sons ──
            DrawCard(() =>
            {
                DrawSectionLabel("SONS  ·  BUILD");
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(8);

                // Boutons de prévisualisation
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                var previewStyle = new GUIStyle(buttonStyle) { fontSize = 10, padding = new RectOffset(10, 10, 5, 5) };
                previewStyle.normal.background = MakeRoundedTex(32, 32, new Color(colAccent.r, colAccent.g, colAccent.b, 0.13f), 5);
                previewStyle.normal.textColor  = colAccent;
                previewStyle.hover.textColor   = Color.white;
                if (GUILayout.Button("▶  Test succès", previewStyle, GUILayout.Height(24)))
                    VaroniaBuildSounds.Play(success: true);
                GUILayout.Space(4);
                var previewStyleStep = new GUIStyle(previewStyle);
                previewStyleStep.normal.background = MakeRoundedTex(32, 32, new Color(0.55f, 0.70f, 1f, 0.13f), 5);
                previewStyleStep.normal.textColor  = new Color(0.55f, 0.70f, 1f, 1f);
                previewStyleStep.hover.textColor   = Color.white;
                if (GUILayout.Button("▶  Test étape",  previewStyleStep, GUILayout.Height(24)))
                    VaroniaBuildSounds.PlayStep();
                GUILayout.Space(4);
                var previewStyleErr = new GUIStyle(previewStyle);
                previewStyleErr.normal.background = MakeRoundedTex(32, 32, new Color(1f, 0.40f, 0.40f, 0.13f), 5);
                previewStyleErr.normal.textColor  = new Color(1f, 0.40f, 0.40f, 1f);
                if (GUILayout.Button("▶  Test échec",  previewStyleErr, GUILayout.Height(24)))
                    VaroniaBuildSounds.Play(success: false);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(4);
            }, colAccent);

            // ── Warning contenu manquant ──
            bool contentMissing = !string.IsNullOrEmpty(_contentSourcePath)
                                  && !Directory.Exists(Path.Combine(_contentSourcePath, Application.productName));
            if (contentMissing)
            {
                EditorGUILayout.Space(8);
                DrawCard(() =>
                {
                    var warnStyle = new GUIStyle
                    {
                        fontSize  = 11,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal    = { textColor = colWarn },
                        wordWrap  = true,
                        padding   = new RectOffset(0, 0, 4, 4),
                    };
                    GUILayout.Label("Content manquant dans le dossier source", warnStyle);
                    var sub = new GUIStyle(footerStyle) { normal = { textColor = colTextSecond }, wordWrap = true };
                    GUILayout.Label(Path.Combine(_contentSourcePath, Application.productName), sub);
                }, colWarn);
            }

            EditorGUILayout.Space(8);
            DrawFooter();
            EditorGUILayout.Space(8);
        }

        // ─── Row helpers ──────────────────────────────────────────────────────────

        // Champ texte simple (ex: GameID)
        void DrawFieldRow(string label, ref string value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, fieldLabelStyle, GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            string v = EditorGUILayout.TextField(value);
            if (EditorGUI.EndChangeCheck()) { value = v; _isDirty = true; }
            EditorGUILayout.EndHorizontal();
        }

        // Champ chemin avec bouton Browse (…)
        void DrawPathRow(string label, ref string value, bool isFolder, bool isFile = false, string tooltip = "")
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(label, tooltip), fieldLabelStyle, GUILayout.Width(120));

            EditorGUI.BeginChangeCheck();
            string v = EditorGUILayout.TextField(value);
            if (EditorGUI.EndChangeCheck()) { value = v; _isDirty = true; }

            var browseStyle = new GUIStyle(buttonStyle)
            {
                fontSize = 10,
                padding  = new RectOffset(4, 4, 3, 3),
            };
            browseStyle.normal.background = MakeRoundedTex(32, 32, new Color(colAccent.r, colAccent.g, colAccent.b, 0.13f), 4);
            browseStyle.normal.textColor  = colAccent;
            browseStyle.hover.textColor   = Color.white;

            if (GUILayout.Button("…", browseStyle, GUILayout.Width(26), GUILayout.Height(20)))
            {
                string picked = isFile
                    ? EditorUtility.OpenFilePanel("Sélectionner " + label, value, "exe")
                    : EditorUtility.OpenFolderPanel("Sélectionner " + label, value, "");
                if (!string.IsNullOrEmpty(picked)) { value = picked; _isDirty = true; GUI.FocusControl(null); }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ─── Footer ───────────────────────────────────────────────────────────────

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);

            if (GUILayout.Button("RECHARGER", buttonStyle, GUILayout.Height(34), GUILayout.MinWidth(110)))
            {
                Load();
                GUI.FocusControl(null);
            }

            GUILayout.FlexibleSpace();

            Color  saveCol   = _isDirty ? colWarn : colAccent;
            var    saveStyle = new GUIStyle(buttonStyle);
            saveStyle.normal.background = MakeRoundedTex(32, 32, new Color(saveCol.r, saveCol.g, saveCol.b, 0.20f), 5);
            saveStyle.normal.textColor  = saveCol;
            saveStyle.hover.textColor   = Color.white;
            saveStyle.active.background = _isDirty ? texWarnSolid : texAccentSolid;

            if (GUILayout.Button(_isDirty ? "  SAUVEGARDER  ●" : "  SAUVEGARDER",
                                 saveStyle, GUILayout.Height(34), GUILayout.MinWidth(160)))
                Save();

            GUILayout.Space(16);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            GUILayout.Label("Varonia Back Office  ·  Project Settings  ·  Local machine", footerStyle);
        }

        // ─── DrawCard / DrawSectionLabel / DrawDivider ────────────────────────────

        void DrawCard(Action content, Color accentColor)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            EditorGUILayout.BeginVertical();

            Rect r   = EditorGUILayout.BeginVertical();
            r.x     -= 4; r.width  += 8;
            r.y     -= 4; r.height += 8;

            EditorGUI.DrawRect(r, colCard);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2), accentColor);

            GUILayout.Space(12);
            content();
            GUILayout.Space(12);

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();
        }

        void DrawSectionLabel(string text) => GUILayout.Label(text, sectionStyle);

        void DrawDivider()
        {
            Rect r   = GUILayoutUtility.GetRect(1, 1);
            r.x     += 20;
            r.width -= 40;
            EditorGUI.DrawRect(r, colDivider);
        }
    }
}
