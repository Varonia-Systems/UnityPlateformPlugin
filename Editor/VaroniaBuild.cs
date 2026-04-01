using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.SceneManagement;

// ─── Build hooks ─────────────────────────────────────────────────────────────

class BuildProcessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report)
    {
        IncrementVersion.Increment();
        // Notre fenêtre prend la main dès le début de la compilation
        VaroniaBackOffice.Buildwindows.SetState("BUILD", "Compilation du projet en cours...", 0.04f);
        EditorUtility.ClearProgressBar(); // efface la progress bar Unity dans la status bar
    }
}

class BuildProcessor_2 : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPostprocessBuild(BuildReport report)
    {
        EditorUtility.ClearProgressBar();
        bool success = report.summary.result != BuildResult.Failed;
        VaroniaBackOffice.VaroniaBuild.EndBuild(success);
    }
}

public class IncrementVersion
{
    public static void Increment()
    {
        PlayerSettings.bundleVersion = DateTime.Now.ToString("yyyy.MM.dd HH:mm");
        UnityEngine.Debug.Log("New version : " + PlayerSettings.bundleVersion);
    }
}

// ─────────────────────────────────────────────────────────────────────────────

namespace VaroniaBackOffice
{
    public class VaroniaBuild : EditorWindow
    {
        // ─── State ────────────────────────────────────────────────────────────────
        [SerializeField] public bool   BetaVersion   = false;
        [SerializeField] public bool   LTS           = false;
        [SerializeField] public bool   Normal        = true;
        [SerializeField] public bool   CopyToServer  = true;
        [SerializeField] public bool   DontBuild     = false;
        [SerializeField] public bool   _Zip_         = true;
        [SerializeField] public string ChangeLogArea = "";

        public bool Zip_;

        static VaroniaBuild VaroniaBuild_;
        Vector2 _scrollLog;

        // ── Style cache ──
        static bool     stylesBuilt;
        static GUIStyle headerStyle;
        static GUIStyle sectionStyle;
        static GUIStyle footerStyle;
        static GUIStyle buttonStyle;
        static GUIStyle tagStyle;
        static GUIStyle changelogStyle;
        static GUIStyle buildTypeBtnOff;
        static GUIStyle buildTypeBtnOn;

        // ── Colors ──
        static readonly Color colBg          = new Color(0.11f, 0.11f, 0.14f, 1f);
        static readonly Color colCard        = new Color(0.15f, 0.15f, 0.19f, 1f);
        static readonly Color colAccent      = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color colAccentDim   = new Color(0.30f, 0.85f, 0.65f, 0.15f);
        static readonly Color colWarn        = new Color(1f,    0.75f, 0.30f, 1f);
        static readonly Color colWarnDim     = new Color(1f,    0.75f, 0.30f, 0.12f);
        static readonly Color colError       = new Color(1f,    0.40f, 0.40f, 1f);
        static readonly Color colErrorDim    = new Color(1f,    0.40f, 0.40f, 0.15f);
        static readonly Color colTextPrimary = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color colTextSecond  = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color colTextMuted   = new Color(0.40f, 0.40f, 0.47f, 1f);
        static readonly Color colDivider     = new Color(1f,    1f,    1f,    0.06f);
        static readonly Color colBtnNormal   = new Color(0.22f, 0.22f, 0.28f, 1f);
        static readonly Color colBtnHover    = new Color(0.28f, 0.28f, 0.35f, 1f);

        // ── Textures ──
        static Texture2D texBtn, texBtnHover, texAccentSolid, texWarnSolid, texChangelogBg, texTypeOff, texTypeOn;
        private static Texture2D _cachedPillTex;
        private static Color _cachedPillCol;
        private static Texture2D _cachedBuildNormalTex, _cachedBuildHoverTex, _cachedBuildActiveTex;
        private static Color _cachedBuildCol;
        private static Texture2D _cachedFolderTex;

        // ─────────────────────────────────────────────────────────────────────────

#if VBO_ADVANCED
        [MenuItem("Varonia/Build Menu")]
#endif
        public static void ShowWindow()
        {
            var w = GetWindow<VaroniaBuild>("Varonia Build");
            w.titleContent = new GUIContent("Varonia Build");
            w.minSize = new Vector2(620, 820);
            w.maxSize = new Vector2(620, 820);
        }

        void OnInspectorUpdate() => Repaint();

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        protected void OnEnable()
        {
            stylesBuilt = false;
            var data = EditorPrefs.GetString(Application.productName + "Build", JsonUtility.ToJson(this, false));
            JsonUtility.FromJsonOverwrite(data, this);
            VaroniaBuild_ = this;

            string logPath = Application.streamingAssetsPath + "/tempChangelog.txt";
            if (File.Exists(logPath))
                using (var sr = new StreamReader(logPath))
                    ChangeLogArea = sr.ReadToEnd();
        }

        protected void OnDisable()
        {
            EditorPrefs.SetString(Application.productName + "Build", JsonUtility.ToJson(this, false));
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

            if (texBtn == null) texBtn = MakeRoundedTex(32, 32, colBtnNormal, 5);
            if (texBtnHover == null) texBtnHover = MakeRoundedTex(32, 32, colBtnHover, 5);
            if (texAccentSolid == null) texAccentSolid = MakeRoundedTex(32, 32, colAccent, 5);
            if (texWarnSolid == null) texWarnSolid = MakeRoundedTex(32, 32, colWarn, 5);
            if (texChangelogBg == null) texChangelogBg = MakeTex(new Color(0.10f, 0.10f, 0.13f, 1f));
            if (texTypeOff == null) texTypeOff = MakeRoundedTex(32, 32, new Color(0.19f, 0.19f, 0.25f, 1f), 6);
            if (texTypeOn == null) texTypeOn = MakeRoundedTex(32, 32, colAccentDim, 6);

            headerStyle = new GUIStyle
            {
                fontSize  = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
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

            changelogStyle = new GUIStyle(EditorStyles.textArea)
            {
                fontSize = 11,
                normal   = { textColor = colTextPrimary, background = texChangelogBg },
                focused  = { textColor = colTextPrimary },
                padding  = new RectOffset(8, 8, 6, 6),
                wordWrap = true,
            };

            buildTypeBtnOff = new GUIStyle
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = colTextMuted,   background = texTypeOff },
                hover     = { textColor = colTextPrimary, background = MakeRoundedTex(32, 32, new Color(0.24f, 0.24f, 0.32f, 1f), 6) },
                padding   = new RectOffset(12, 12, 8, 8),
                margin    = new RectOffset(2, 2, 0, 0),
                border    = new RectOffset(6, 6, 6, 6),
            };

            buildTypeBtnOn = new GUIStyle(buildTypeBtnOff)
            {
                normal = { textColor = colAccent, background = texTypeOn },
            };
        }

        // ─── GUI ──────────────────────────────────────────────────────────────────

        void OnGUI()
        {
            BuildStyles();
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colBg);

            // ── Lire Game ID ──
            string gameId    = "";
            bool   hasGameId = false;
            try
            {
                using (var sr = new StreamReader(Application.streamingAssetsPath + "/GameID.txt"))
                {
                    gameId    = sr.ReadToEnd();
                    hasGameId = !string.IsNullOrEmpty(gameId);
                }
            }
            catch { }

            bool has7Zip      = !string.IsNullOrEmpty(EditorPrefs.GetString("VBO_7ZipPath"));
            bool hasBuildPath = !string.IsNullOrEmpty(EditorPrefs.GetString("VBO_BuildPath"));
            bool hasServer    = !string.IsNullOrEmpty(EditorPrefs.GetString("VBO_BuildServerPath"));
            bool hasContent   =  Directory.Exists(EditorPrefs.GetString("VBO_ContentSourcePath") + "/" + Application.productName);
            bool canBuild     = hasGameId && hasBuildPath;

            EditorGUILayout.Space(12);

            // ── Titre ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.Label("VARONIA BUILD", headerStyle);
            GUILayout.FlexibleSpace();

            Color  pillCol  = canBuild ? colAccent : colError;
            string pillText = canBuild ? "  READY  " : "  ERROR  ";
            var    pill     = new GUIStyle(tagStyle);
            pill.normal.textColor  = pillCol;
            if (_cachedPillTex == null || _cachedPillCol != pillCol)
            {
                _cachedPillCol = pillCol;
                _cachedPillTex = MakeRoundedTex(32, 32, new Color(pillCol.r, pillCol.g, pillCol.b, 0.15f), 6);
            }
            pill.normal.background = _cachedPillTex;
            GUILayout.Label(pillText, pill);

            GUILayout.Space(16);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(12);

            // ── Card Statut ──
            Color statusAccent = !canBuild ? colError : (!hasContent || !hasServer) ? colWarn : colAccent;
            DrawCard(() =>
            {
                DrawSectionLabel("STATUT  ·  GAME ID " + (hasGameId ? gameId : "—"));
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(6);
                DrawStatusRow("Game ID",     hasGameId,    gameId, "Not set",  isError: true);
                DrawStatusRow("Build Path",  hasBuildPath, "OK",   "Not set",  isError: true);
                DrawStatusRow("7-Zip",       has7Zip,      "OK",   "Not set",  isError: true);
                DrawStatusRow("Server Path", hasServer,    "OK",   "Not set",  isError: false);
                DrawStatusRow("Content",     hasContent,   "Found","Missing",  isError: false);
            }, statusAccent);

            EditorGUILayout.Space(8);

            // ── Card ChangeLog ──
            DrawCard(() =>
            {
                DrawSectionLabel("CHANGELOG");
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(6);
                _scrollLog     = EditorGUILayout.BeginScrollView(_scrollLog, GUILayout.Height(160));
                ChangeLogArea = GUILayout.TextArea(ChangeLogArea, changelogStyle, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }, colTextMuted);

            EditorGUILayout.Space(8);

            // ── Card Build Type ──
            DrawCard(() =>
            {
                DrawSectionLabel("BUILD TYPE");
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("NORMAL", Normal      ? buildTypeBtnOn : buildTypeBtnOff, GUILayout.Height(32), GUILayout.MinWidth(110)))
                { Normal = true; BetaVersion = false; LTS = false; }
                GUILayout.Space(4);
                if (GUILayout.Button("BETA",   BetaVersion ? buildTypeBtnOn : buildTypeBtnOff, GUILayout.Height(32), GUILayout.MinWidth(110)))
                { BetaVersion = true; Normal = false; LTS = false; }
                GUILayout.Space(4);
                if (GUILayout.Button("LTS",    LTS         ? buildTypeBtnOn : buildTypeBtnOff, GUILayout.Height(32), GUILayout.MinWidth(110)))
                { LTS = true; Normal = false; BetaVersion = false; }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(4);
            }, colAccent);

            EditorGUILayout.Space(8);

            // ── Card Advanced ──
            DrawCard(() =>
            {
                DrawSectionLabel("ADVANCED");
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(6);
                DrawToggleRow(ref _Zip_,        "Zip Build",                "Package the build into a ZIP archive");
                EditorGUILayout.Space(2);
                DrawToggleRow(ref CopyToServer, "Copy to Server",           "Copy the ZIP to the build server after compression");
                EditorGUILayout.Space(2);
                DrawToggleRow(ref DontBuild,    "Skip Build  (use existing)","Skip compilation and use already-built files");
            }, colTextMuted);

            EditorGUILayout.Space(12);

            // ── Bouton BUILD ──
            GUI.enabled = canBuild;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);

            Color buildCol   = DontBuild ? colWarn : colAccent;
            if (_cachedBuildNormalTex == null || _cachedBuildCol != buildCol)
            {
                _cachedBuildCol = buildCol;
                _cachedBuildNormalTex = MakeRoundedTex(32, 32, new Color(buildCol.r, buildCol.g, buildCol.b, 0.80f), 8);
                _cachedBuildHoverTex = MakeRoundedTex(32, 32, buildCol, 8);
                _cachedBuildActiveTex = MakeRoundedTex(32, 32, new Color(buildCol.r * 0.75f, buildCol.g * 0.75f, buildCol.b * 0.75f, 1f), 8);
            }

            var buildStyle = new GUIStyle
            {
                fontSize  = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white, background = _cachedBuildNormalTex },
                hover     = { textColor = Color.white, background = _cachedBuildHoverTex },
                active    = { textColor = Color.white, background = _cachedBuildActiveTex },
                border    = new RectOffset(8, 8, 8, 8),
            };

            string buildLabel = DontBuild ? "⚙️  ZIP & DEPLOY" : "🛠️  BUILD";
            if (GUILayout.Button(buildLabel, buildStyle, GUILayout.Height(58)))
            {
                savelog();
                if (_Zip_) Zip_ = true;
                if (!DontBuild) Build(gameId);
                else if (Zip_)  Zip(gameId);
            }

            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;

            EditorGUILayout.Space(8);

            // ── Boutons dossiers ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);

            var folderStyle = new GUIStyle(buttonStyle);
            if (_cachedFolderTex == null)
            {
                _cachedFolderTex = MakeRoundedTex(32, 32, new Color(colAccent.r, colAccent.g, colAccent.b, 0.13f), 5);
            }
            folderStyle.normal.background = _cachedFolderTex;
            folderStyle.normal.textColor  = colAccent;
            folderStyle.hover.textColor   = Color.white;

            if (GUILayout.Button("📁  Server Path", folderStyle, GUILayout.Height(30)))
                EditorUtility.RevealInFinder(EditorPrefs.GetString("VBO_BuildServerPath") + "/" + gameId);

            GUILayout.Space(4);

            if (GUILayout.Button("📁  Build Path", folderStyle, GUILayout.Height(30)))
                EditorUtility.RevealInFinder(EditorPrefs.GetString("VBO_BuildPath") + "/" + Application.productName);

            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            GUILayout.Label("Varonia Back Office  ·  Build Pipeline", footerStyle);
            EditorGUILayout.Space(6);
        }

        // ─── Row helpers ──────────────────────────────────────────────────────────

        void DrawStatusRow(string label, bool ok, string okText, string errText, bool isError)
        {
            Color col = ok ? colAccent : (isError ? colError : colWarn);
            EditorGUILayout.BeginHorizontal();

            var dotStyle = new GUIStyle
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = col },
                padding   = new RectOffset(0, 4, 2, 2),
            };
            GUILayout.Label("●", dotStyle, GUILayout.Width(16));

            var lblStyle = new GUIStyle
            {
                fontSize = 11,
                normal   = { textColor = colTextSecond },
                padding  = new RectOffset(0, 0, 2, 2),
            };
            GUILayout.Label(label, lblStyle, GUILayout.Width(100));
            GUILayout.FlexibleSpace();

            var valStyle = new GUIStyle
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = col },
                padding   = new RectOffset(0, 0, 2, 2),
            };
            GUILayout.Label(ok ? okText : errText, valStyle);
            EditorGUILayout.EndHorizontal();
        }

        void DrawToggleRow(ref bool value, string label, string tooltip = "")
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            bool v = EditorGUILayout.Toggle(value, GUILayout.Width(20));
            if (EditorGUI.EndChangeCheck()) value = v;

            var lblStyle = new GUIStyle
            {
                fontSize = 11,
                normal   = { textColor = value ? colTextPrimary : colTextSecond },
                padding  = new RectOffset(4, 0, 3, 3),
            };
            GUILayout.Label(new GUIContent(label, tooltip), lblStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ─── Draw helpers ─────────────────────────────────────────────────────────

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

        // ─── Logique (inchangée) ─────────────────────────────────────────────────

        void savelog()
        {
            using (var sw = new StreamWriter(Application.streamingAssetsPath + "/tempChangelog.txt", false))
            {
                sw.Write(ChangeLogArea);
            }
        }

        public static void EndBuild(bool success)
        {
            if (!success)
            {
                PlayFailure();
                Buildwindows.CloseWindow();
                UnityEngine.Debug.LogError("[VaroniaBuild] ❌ Build échoué.");
                return;
            }

            try
            {
                string gid;
                using (var sr = new StreamReader(Application.streamingAssetsPath + "/GameID.txt"))
                    gid = sr.ReadToEnd();
                VaroniaBuild_.Version(gid);
            }
            catch { }

            // On NE joue PAS PlaySuccess() ici — le son sera déclenché à la
            // toute fin du pipeline (après Zip + Copy si activés).
            Buildwindows.CloseWindow();

            if (VaroniaBuild_.Zip_)
            {
                string gid;
                using (var sr = new StreamReader(Application.streamingAssetsPath + "/GameID.txt"))
                    gid = sr.ReadToEnd();
                VaroniaBuild_.Zip(gid);
                // → PlaySuccess() appelé dans Zip() ou Copy() selon la config
            }
            else
            {
                // Pas de zip : build seul = fin du pipeline → succès immédiat
                PlaySuccess();
            }
        }

        static void PlaySuccess() => VaroniaBuildSounds.Play(success: true);
        static void PlayFailure() => VaroniaBuildSounds.Play(success: false);
        static void PlayStep()    => VaroniaBuildSounds.PlayStep();

        async void Version(string GameId)
        {
            await Task.Delay(1000);

            string buildPath = EditorPrefs.GetString("VBO_BuildPath") + "/" + Application.productName;
            string backup    = buildPath + "/" + Application.productName + "_BackUpThisFolder_ButDontShipItWithYourGame";
            string burst     = buildPath + "/" + Application.productName + "_BurstDebugInformation_DoNotShip";

            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            if (Directory.Exists(burst))  Directory.Delete(burst,  true);

            string changelogFile = buildPath + "/Changelog.txt";
            if (File.Exists(changelogFile)) File.Delete(changelogFile);
            using (var sw = new StreamWriter(changelogFile, true)) sw.Write(ChangeLogArea);

            var versionData = new
            {
                AppValue = int.Parse(GameId),
                Version  = DateTime.Now.ToString("yyyy.M.d"),
                IsBeta   = BetaVersion,
                IsLTS    = LTS,
            };

            using (var sw = new StreamWriter(buildPath + "/version.json"))
                sw.Write(JsonConvert.SerializeObject(versionData, Formatting.Indented));
        }

        async void Copy(string GameId)
        {
            string serverPath  = EditorPrefs.GetString("VBO_BuildServerPath");
            string contentSrc  = EditorPrefs.GetString("VBO_ContentSourcePath");
            string buildPath_  = EditorPrefs.GetString("VBO_BuildPath");
            string contentZip  = contentSrc + "/" + Application.productName + "/Content.zip";
            string gameZip     = buildPath_ + "/" + Application.productName + "/Game.zip";
            bool   hasContent  = File.Exists(contentZip);

            string dest = serverPath + "/" + GameId + "/"
                        + DateTime.Now.ToString("yyyy.M.d");
            if (BetaVersion) dest += "_BETA";
            if (LTS)         dest += "_LTS";

            Buildwindows.SetState("COPIE SERVEUR", "Création du dossier de destination...", 0f);
            Buildwindows.ShowWindow();
            Directory.CreateDirectory(dest);

            if (hasContent)
            {
                Buildwindows.SetState("COPIE SERVEUR", "Copie de Content.zip...", 0.10f);
                await Task.Run(() => File.Copy(contentZip, dest + "/Content.zip", true));
            }

            Buildwindows.SetState("COPIE SERVEUR", "Copie de Game.zip...", hasContent ? 0.55f : 0.10f);
            await Task.Run(() => File.Copy(gameZip, dest + "/Game.zip", true));

            if (hasContent)
            {
                Buildwindows.SetState("COPIE SERVEUR", "Nettoyage...", 0.95f);
                File.Delete(contentZip);
            }

            Buildwindows.SetState("COPIE SERVEUR", "Copie terminée ✓", 1f);

            // Fin du pipeline complet → son final succès
            PlaySuccess();

            await Task.Delay(700);
            Buildwindows.CloseWindow();
        }

        // ── Zip avec progression réelle lue depuis stdout de 7-Zip ───────────────

        async void Zip(string GameId)
        {
            Zip_ = false;

            string sevenZip    = EditorPrefs.GetString("VBO_7ZipPath") + "/7z.exe";
            string buildPath   = EditorPrefs.GetString("VBO_BuildPath") + "/" + Application.productName;
            string contentPath = EditorPrefs.GetString("VBO_ContentSourcePath") + "/" + Application.productName;
            string gameZip     = buildPath   + "/Game.zip";
            string contentZip  = contentPath + "/Content.zip";
            bool   hasContent  = Directory.Exists(contentPath);

            // ── Nettoyage ────────────────────────────────────────────────────────
            Buildwindows.SetState("PACKAGING", "Nettoyage des archives précédentes...", 0f);
            Buildwindows.ShowWindow();

            if (File.Exists(gameZip))    { File.Delete(gameZip);    await Task.Delay(300); }
            if (File.Exists(contentZip)) { File.Delete(contentZip); await Task.Delay(200); }

            // ── Zip Content ──────────────────────────────────────────────────────
            if (hasContent)
            {
                await RunZip(sevenZip,
                    $@"a -tZIP -bsp1 ""{contentZip}"" ""{contentPath}/*"" -r",
                    from: 0.03f, to: 0.48f,
                    label: "Compression du content  ·  Content.zip");
            }

            await Task.Delay(500);

            // ── Zip Game ─────────────────────────────────────────────────────────
            await RunZip(sevenZip,
                $@"a -tZIP -bsp1 ""{gameZip}"" ""{buildPath}/*"" -r",
                from: hasContent ? 0.50f : 0.03f, to: 0.97f,
                label: "Compression du jeu  ·  Game.zip");

            Buildwindows.SetState("PACKAGING", "Packaging terminé ✓", 1f);

            // Son intermédiaire : packaging terminé
            PlayStep();

            await Task.Delay(700);
            Buildwindows.CloseWindow();
            await Task.Delay(400);

            if (CopyToServer)
            {
                Copy(GameId);
                // → PlaySuccess() sera appelé en fin de Copy()
            }
            else
            {
                // Pas de copie → fin du pipeline → son final succès
                PlaySuccess();
            }
        }

        /// <summary>
        /// Lance 7-Zip et lit la progression depuis stdout (-bsp1).
        /// Met à jour Buildwindows.Progress en temps réel.
        /// </summary>
        static async Task RunZip(string exe, string args, float from, float to, string label)
        {
            Buildwindows.SetState("PACKAGING", label, from);

            var psi = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = args,
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            Process proc;
            try   { proc = Process.Start(psi); }
            catch { Buildwindows.SetState("PACKAGING", "❌ 7-Zip introuvable", from); return; }
            if (proc == null) return;

            // Thread dédié à la lecture char par char (7-Zip utilise \r, pas \n)
            var readThread = new System.Threading.Thread(() =>
            {
                try
                {
                    var reader = proc.StandardOutput;
                    var token  = new System.Text.StringBuilder(32);
                    int ch;

                    while ((ch = reader.Read()) != -1)
                    {
                        if (ch == '\r' || ch == '\n')
                        {
                            ParseToken(token.ToString(), from, to);
                            token.Clear();
                        }
                        else token.Append((char)ch);
                    }
                    // Dernier token éventuel sans terminateur
                    if (token.Length > 0) ParseToken(token.ToString(), from, to);
                }
                catch { /* lecture interrompue à la fermeture du process */ }
            });
            readThread.IsBackground = true;
            readThread.Start();

            while (!proc.HasExited)
                await Task.Delay(100);

            readThread.Join(1000);
            Buildwindows.Progress = to;
        }

        // Parse "  42%" ou "42%  3 - filename" → extrait le % et met à jour Progress
        static void ParseToken(string raw, float from, float to)
        {
            foreach (string part in raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim();
                if (p.EndsWith("%") && int.TryParse(p.TrimEnd('%'), out int pct))
                {
                    Buildwindows.Progress = from + Mathf.Clamp01(pct / 100f) * (to - from);
                    return;
                }
            }
        }

        void Build(string GameId)
        {
            Buildwindows.SetState("BUILD", "Compilation du projet en cours...", 0f, indeterminate: true);
            Buildwindows.ShowWindow(large: true);
            EditorUtility.ClearProgressBar();

            // EditorApplication.delayCall garantit l'exécution hors du player loop
            // ce qui est obligatoire pour BuildPipeline.BuildPlayer
            EditorApplication.delayCall += () =>
            {
                var levels = new List<string>();
                for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                    if (EditorBuildSettings.scenes[i].enabled)
                        levels.Add(SceneUtility.GetScenePathByBuildIndex(i));

                BuildPipeline.BuildPlayer(
                    levels.ToArray(),
                    EditorPrefs.GetString("VBO_BuildPath") + "/" + Application.productName + "/" + Application.productName + ".exe",
                    BuildTarget.StandaloneWindows64,
                    BuildOptions.None
                );
            };
        }
    }

    // ─── Fenêtre de progression ───────────────────────────────────────────────

    public class Buildwindows : EditorWindow
    {
        // ── État partagé (mis à jour depuis n'importe quel thread) ──────────────
        public static string StepLabel     = "BUILD";
        public static string SubLabel      = "";
        public static float  Progress      = 0f;
        public static bool   Indeterminate = false;

        // ── Win32 : always on top ─────────────────────────────────────────────────
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr FindWindow(string cls, string title);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                        int x, int y, int cx, int cy, uint flags);

        static readonly IntPtr HWND_TOPMOST    = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST  = new IntPtr(-2);
        const uint SWP_NOMOVE    = 0x0002;
        const uint SWP_NOSIZE    = 0x0001;
        const uint SWP_NOACTIVATE = 0x0010;

        bool _topmost; // mis à true après le premier SetWindowPos réussi

        // ── Couleurs ──────────────────────────────────────────────────────────────
        static readonly Color colBg          = new Color(0.11f, 0.11f, 0.14f, 1f);
        static readonly Color colAccent      = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color colAccentDim   = new Color(0.30f, 0.85f, 0.65f, 0.15f);
        static readonly Color colTextPrimary = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color colTextSecond  = new Color(0.55f, 0.55f, 0.62f, 1f);
        static readonly Color colBarBg       = new Color(1f, 1f, 1f, 0.07f);

        private static Texture2D _cachedTagTex;

        // ── API ──────────────────────────────────────────────────────────────────

        public static void SetState(string step, string sub, float progress, bool indeterminate = false)
        {
            StepLabel     = step;
            SubLabel      = sub;
            Progress      = Mathf.Clamp01(progress);
            Indeterminate = indeterminate;
            if (HasOpenInstances<Buildwindows>())
                GetWindow<Buildwindows>().Repaint();
        }

        // Grande fenêtre pour le BUILD (couvre mieux l'UI d'Unity), compacte pour ZIP/COPY
        public static void ShowWindow(bool large = false)
        {
            float W = large ? 680 : 540;
            float H = large ? 220 : 148;
            var w = GetWindow<Buildwindows>(true, "Varonia Build");
            w.minSize = w.maxSize = new Vector2(W, H);
            float cx = Screen.currentResolution.width  / 2f - W / 2f;
            float cy = Screen.currentResolution.height / 2f - H / 2f;
            w.position = new Rect(cx, cy, W, H);
        }

        public static void CloseWindow()
        {
            if (!HasOpenInstances<Buildwindows>()) return;
            // Retire le TOPMOST avant de fermer pour éviter tout artefact
            IntPtr hwnd = FindWindow(null, "Varonia Build");
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                             SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            GetWindow<Buildwindows>().Close();
        }

        void OnEnable()  => _topmost = false; // reset à chaque ouverture
        void OnInspectorUpdate() => Repaint();

        // ── Rendu ────────────────────────────────────────────────────────────────

        void OnGUI()
        {
            // Force always-on-top au premier frame (Win32) ───────────────────────
            if (!_topmost)
            {
                IntPtr hwnd = FindWindow(null, "Varonia Build");
                if (hwnd != IntPtr.Zero)
                {
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                                 SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    _topmost = true;
                }
            }

            float W = position.width;
            float H = position.height;

            // Fond
            EditorGUI.DrawRect(new Rect(0, 0, W, H), colBg);
            // Ligne accent en haut
            EditorGUI.DrawRect(new Rect(0, 0, W, 2), colAccent);

            // ── Tag step ────────────────────────────────────────────────────────
            GUILayout.Space(16);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(20);

            var titleStyle = new GUIStyle
            {
                fontSize  = 13,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = colTextPrimary },
                padding   = new RectOffset(0, 0, 2, 2),
            };
            GUILayout.Label("VARONIA BUILD", titleStyle);
            GUILayout.FlexibleSpace();

            if (_cachedTagTex == null)
            {
                _cachedTagTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _cachedTagTex.SetPixel(0, 0, colAccentDim);
                _cachedTagTex.Apply();
                _cachedTagTex.hideFlags = HideFlags.HideAndDontSave;
            }

            var tagStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = colAccent, background = _cachedTagTex },
                padding   = new RectOffset(10, 10, 4, 4),
                margin    = new RectOffset(0, 20, 0, 0),
            };
            GUILayout.Label(StepLabel, tagStyle);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(14);

            // ── Barre de progression ─────────────────────────────────────────────
            Rect barRect = GUILayoutUtility.GetRect(1, 10);
            barRect.x     += 20; barRect.width -= 40;

            EditorGUI.DrawRect(barRect, colBarBg);

            if (Indeterminate)
            {
                // Barre animée (shimmer) : un segment qui se déplace de gauche à droite
                float t        = (float)(EditorApplication.timeSinceStartup % 1.4) / 1.4f;
                float segW     = barRect.width * 0.32f;
                float segX     = barRect.x + (barRect.width + segW) * t - segW;
                float clampedX = Mathf.Clamp(segX, barRect.x, barRect.xMax);
                float clampedW = Mathf.Clamp(segX + segW, barRect.x, barRect.xMax) - clampedX;
                EditorGUI.DrawRect(new Rect(clampedX, barRect.y, clampedW, barRect.height), colAccent);
                EditorGUI.DrawRect(new Rect(clampedX, barRect.y, clampedW, barRect.height * 0.4f),
                                   new Color(1f, 1f, 1f, 0.12f));
            }
            else
            {
                float fillW = Mathf.Max(0, barRect.width * Progress);
                if (fillW > 0)
                    EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, fillW, barRect.height), colAccent);
                EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, fillW, barRect.height * 0.4f),
                                   new Color(1f, 1f, 1f, 0.08f));
            }

            GUILayout.Space(12);

            // ── Sous-label + pourcentage / indicateur ────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(20);

            var subStyle = new GUIStyle
            {
                fontSize = 10,
                normal   = { textColor = colTextSecond },
                padding  = new RectOffset(0, 0, 2, 2),
            };
            GUILayout.Label(SubLabel, subStyle);
            GUILayout.FlexibleSpace();

            var pctStyle = new GUIStyle
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = colAccent },
                padding   = new RectOffset(0, 0, 2, 2),
            };
            string pctLabel = Indeterminate ? "…" : $"{Mathf.RoundToInt(Progress * 100)} %";
            GUILayout.Label(pctLabel, pctStyle);
            GUILayout.Space(20);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(14);
        }
    }
}

// ─── Utility ─────────────────────────────────────────────────────────────────

public static class FolderSearchUtility
{
    public static string[] FindFoldersByName(string folderName)
    {
        return AssetDatabase.FindAssets($"t:DefaultAsset {folderName}")
            .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
            .Where(path => AssetDatabase.IsValidFolder(path) && path.EndsWith("/" + folderName))
            .ToArray();
    }
}
