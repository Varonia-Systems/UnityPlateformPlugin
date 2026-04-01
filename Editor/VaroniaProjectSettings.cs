using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace VaroniaBackOffice
{
    [InitializeOnLoad]
    internal static class VaroniaProjectSettings
    {
        // ─── Constantes ───────────────────────────────────────────────────────────

        private const string DefineSymbol    = "VBO_ADVANCED";
        private const string SteamVRSymbol   = "STEAMVR_ENABLED";
        private const string StrikerSymbol   = "STRIKER_LINK";
        private const string GameConfigSymbol= "GAME_CONFIG";
        private const string GameScoreSymbol = "GAME_SCORE";

        static VaroniaProjectSettings()
        {
            ApplyForceDisableBoundary();
        }

        public static void ApplyForceDisableBoundary()
        {
            bool force = EditorPrefs.GetBool("VBO_ForceDisableBoundaryWarning", false);
            var type = Type.GetType("VaroniaBackOffice.AdvBoundary, VboAdvBoundary.Runtime");
            if (type == null) return;
            var prop = type.GetProperty("ForceDisableBoundaryWarning", BindingFlags.Public | BindingFlags.Static);
            if (prop != null) prop.SetValue(null, force);
        }

        // Chemins possibles (package UPM ou Assets local)
        private static readonly string[] SettingsAssetPaths =
        {
            "Assets/Resources/Varonia/VaroniaRuntimeSettings.asset",
        };

        // ─── Couleurs ─────────────────────────────────────────────────────────────

        private static readonly Color ColorHeader      = new Color(0.15f, 0.15f, 0.18f, 1f);
        private static readonly Color ColorAccentBlue  = new Color(0.25f, 0.55f, 1.00f, 1f);
        private static readonly Color ColorAccentGreen = new Color(0.20f, 0.80f, 0.45f, 1f);
        private static readonly Color ColorAccentOrange= new Color(1.00f, 0.60f, 0.10f, 1f);
        private static readonly Color ColorAccentPurple= new Color(0.65f, 0.35f, 1.00f, 1f);
        private static readonly Color ColorSeparator   = new Color(0.35f, 0.35f, 0.40f, 1f);

        // ─── Styles (lazy) ────────────────────────────────────────────────────────

        private static GUIStyle _styleCard;
        private static GUIStyle _styleSectionTitle;
        private static GUIStyle _styleDescription;
        private static GUIStyle _styleBadge;
        private static GUIStyle _styleVersion;

        static void EnsureStyles()
        {
            if (_styleCard != null) return;

            _styleCard = new GUIStyle(GUI.skin.box)
            {
                padding  = new RectOffset(12, 12, 10, 10),
                margin   = new RectOffset(0, 0, 4, 4),
                normal   = { background = MakeTex(1, 1, new Color(0.18f, 0.18f, 0.22f, 1f)) }
            };

            _styleSectionTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 12,
                alignment = TextAnchor.MiddleLeft
            };

            _styleDescription = new GUIStyle(EditorStyles.label)
            {
                fontSize  = 10,
                wordWrap  = true,
                normal    = { textColor = new Color(0.65f, 0.65f, 0.70f, 1f) }
            };

            _styleBadge = new GUIStyle(GUI.skin.label)
            {
                fontSize        = 9,
                fontStyle       = FontStyle.Bold,
                alignment       = TextAnchor.MiddleCenter,
                padding         = new RectOffset(6, 6, 2, 2),
                normal          = { background = MakeTex(1, 1, new Color(0.25f, 0.55f, 1f, 0.25f)),
                                    textColor   = new Color(0.55f, 0.80f, 1f, 1f) }
            };

            _styleVersion = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 9
            };
        }

        static Texture2D MakeTex(int w, int h, Color col)
        {
            var tex = new Texture2D(w, h);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }

        // ─── Helpers Defines ──────────────────────────────────────────────────────

#if UNITY_2021_2_OR_NEWER
        static NamedBuildTarget GetNamedBuildTarget()
        {
            var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            return NamedBuildTarget.FromBuildTargetGroup(group);
        }

        static string GetDefines() => PlayerSettings.GetScriptingDefineSymbols(GetNamedBuildTarget());
        static void   SetDefines(string d) => PlayerSettings.SetScriptingDefineSymbols(GetNamedBuildTarget(), d);
#else
        static BuildTargetGroup GetBuildTargetGroup()
        {
            return BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
        }

        static string GetDefines() => PlayerSettings.GetScriptingDefineSymbolsForGroup(GetBuildTargetGroup());
        static void   SetDefines(string d) => PlayerSettings.SetScriptingDefineSymbolsForGroup(GetBuildTargetGroup(), d);
#endif
        static bool   HasDefine(string defines, string symbol) => defines.Contains(symbol);

        static string AddDefine(string defines, string symbol)
            => string.IsNullOrEmpty(defines) ? symbol : defines + ";" + symbol;

        static string RemoveDefine(string defines, string symbol)
        {
            var list = new List<string>(defines.Split(';'));
            list.Remove(symbol);
            return string.Join(";", list);
        }

        static void EnsureGameConfigAssembly()
        {
            string assemblyName = "Vbo.GameConfig";
            bool assemblyExists = false;

            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == assemblyName)
                {
                    assemblyExists = true;
                    break;
                }
            }

            if (!assemblyExists)
            {
                string folderPath = "Assets/GameConfig";
                if (!AssetDatabase.IsValidFolder(folderPath))
                {
                    AssetDatabase.CreateFolder("Assets", "GameConfig");
                }

                string asmdefPath = System.IO.Path.Combine(folderPath, assemblyName + ".asmdef");
                if (!System.IO.File.Exists(asmdefPath))
                {
                    string asmdefContent = "{\n    \"name\": \"" + assemblyName + "\",\n    \"references\": [],\n    \"includePlatforms\": [],\n    \"excludePlatforms\": [],\n    \"allowUnsafeCode\": false,\n    \"overrideReferences\": false,\n    \"precompiledReferences\": [],\n    \"autoReferenced\": true,\n    \"defineConstraints\": [],\n    \"versionDefines\": [],\n    \"noEngineReferences\": false\n}";
                    System.IO.File.WriteAllText(asmdefPath, asmdefContent);
                    Debug.Log($@"[Varonia] Created assembly definition: {asmdefPath}");
                }

                string scriptPath = System.IO.Path.Combine(folderPath, "GameConfig.cs");
                if (!System.IO.File.Exists(scriptPath))
                {
                    string scriptContent = @"using UnityEngine;

namespace VaroniaBackOffice
{
    [System.Serializable]
    public class GameConfig
    {
        public bool testValue;
     
        public static GameConfig CreateFromJson(string jsonString)
        {
            try 
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<GameConfig>(jsonString);
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($""[GameConfig] Deserialization Error: {e.Message}"");
                return null; 
            }
        }

        public string ToJson()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented);
        }
    }
}";
                    System.IO.File.WriteAllText(scriptPath, scriptContent);
                    Debug.Log($@"[Varonia] Created base GameConfig script: {scriptPath}");
                }

                AssetDatabase.Refresh();
            }
        }

        static void EnsureGameScoreAssembly()
        {
            string assemblyName = "Vbo.GameScore";
            bool assemblyExists = false;

            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == assemblyName)
                {
                    assemblyExists = true;
                    break;
                }
            }

            if (!assemblyExists)
            {
                string folderPath = "Assets/GameScore";
                if (!AssetDatabase.IsValidFolder(folderPath))
                {
                    AssetDatabase.CreateFolder("Assets", "GameScore");
                }

                string asmdefPath = System.IO.Path.Combine(folderPath, assemblyName + ".asmdef");
                if (!System.IO.File.Exists(asmdefPath))
                {
                    string asmdefContent = "{\n    \"name\": \"" + assemblyName + "\",\n    \"references\": [],\n    \"includePlatforms\": [],\n    \"excludePlatforms\": [],\n    \"allowUnsafeCode\": false,\n    \"overrideReferences\": false,\n    \"precompiledReferences\": [],\n    \"autoReferenced\": true,\n    \"defineConstraints\": [],\n    \"versionDefines\": [],\n    \"noEngineReferences\": false\n}";
                    System.IO.File.WriteAllText(asmdefPath, asmdefContent);
                    Debug.Log($@"[Varonia] Created assembly definition: {asmdefPath}");
                }

                string scriptPath = System.IO.Path.Combine(folderPath, "GameScore.cs");
                if (!System.IO.File.Exists(scriptPath))
                {
                    string scriptContent = @"using UnityEngine;

namespace VaroniaBackOffice
{
    [System.Serializable]
    public class GameScore
    {
        public int Score { get; set; }
    }
}";
                    System.IO.File.WriteAllText(scriptPath, scriptContent);
                    Debug.Log($@"[Varonia] Created base GameScore script: {scriptPath}");
                }

                AssetDatabase.Refresh();
            }
        }

        // ─── Chargement Settings ──────────────────────────────────────────────────

        static VaroniaRuntimeSettings LoadSettings()
        {
            // On utilise la méthode Load() centralisée qui passe par Resources.Load
            var s = VaroniaRuntimeSettings.Load();
            if (s != null) return s;

            // En dernier recours en éditeur, si Resources.Load a échoué (ex: asset pas encore importé/indexé)
            // on tente une recherche par type via AssetDatabase.
            var guids = AssetDatabase.FindAssets("t:VaroniaRuntimeSettings");
            if (guids.Length > 0)
            {
                // Priorité à celui dans un dossier Resources
                string bestPath = null;
                foreach (var guid in guids)
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (p.Contains("/Resources/"))
                    {
                        bestPath = p;
                        break;
                    }
                    if (bestPath == null) bestPath = p;
                }
                return AssetDatabase.LoadAssetAtPath<VaroniaRuntimeSettings>(bestPath);
            }

            return null;
        }

        // ─── SettingsProvider ─────────────────────────────────────────────────────

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/Varonia Back Office", SettingsScope.Project)
            {
                label    = "Varonia Back Office",
                keywords = new HashSet<string> { "Varonia", "BackOffice", "SteamVR", "OpenXR", "MQTT", "Tracking" },
                guiHandler = DrawGUI
            };
        }

        // ─── GUI ──────────────────────────────────────────────────────────────────

        private static void DrawGUI(string searchContext)
        {
            EnsureStyles();

            var settings = LoadSettings();

            // ── Header ──────────────────────────────────────────────────────────
            DrawHeader();

            if (settings == null)
            {
                GUILayout.Space(8);
                EditorGUILayout.HelpBox(
                    "⚠  VaroniaRuntimeSettings not found.\n" +
                    "Searching by type 'VaroniaRuntimeSettings' in Assets.\n\n" +
                    "Restart the editor or check the package installation.",
                    MessageType.Warning
                );
                return;
            }

            var so = new SerializedObject(settings);
            so.Update();

            string defines = GetDefines();

            GUILayout.Space(6);

            // ── Section : Initialisation ─────────────────────────────────────────
            DrawSectionCard(
                "⚙  Initialization",
                "Controls the automatic startup and core parameters of VaroniaManager.",
                ColorAccentBlue,
                () =>
                {
                    var autoProp = so.FindProperty("autoInit");
                    EditorGUILayout.PropertyField(autoProp, new GUIContent(
                        "Auto-instantiation",
                        "If enabled, the VaroniaManager prefab is instantiated automatically " +
                        "before any scene loads (DontDestroyOnLoad)."
                    ));

                    if (!autoProp.boolValue)
                    {
                        GUILayout.Space(4);
                        EditorGUILayout.HelpBox(
                            "Auto-instantiation disabled — VaroniaManager must be " +
                            "placed manually in each scene.",
                            MessageType.Info
                        );
                    }

                    GUILayout.Space(6);
                    DrawSeparator(ColorSeparator);
                    GUILayout.Space(6);

                    var weaponProp = so.FindProperty("weaponCount");
                    EditorGUILayout.PropertyField(weaponProp, new GUIContent(
                        "Weapon Count",
                        "Number of simultaneous Varonia weapons (devices). Each weapon creates its own VaroniaDevice in the Input System."
                    ));
                }
            );

            // ── Section : Prefab ─────────────────────────────────────────────────
            DrawSectionCard(
                "📦  Prefab Manager",
                "Reference to the VaroniaManager prefab automatically wired by VaroniaPackageWiring.",
                ColorAccentPurple,
                () =>
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.PropertyField(
                            so.FindProperty("managerPrefab"),
                            new GUIContent("Manager Prefab", "Automatically wired — do not modify manually.")
                        );
                    }
                }
            );

            // ── Section : Advanced ───────────────────────────────────────────────
            DrawSectionCard(
                "🔧  Advanced Parameters",
                "Enables advanced Varonia menus (Build Menu, extended Project Settings) " +
                "via the VBO_ADVANCED define.",
                ColorAccentBlue,
                () =>
                {
                    bool current = HasDefine(defines, DefineSymbol);
                    bool next    = DrawToggleRow("Enable Advanced Parameters",
                        "Unlocks the Varonia/Build Menu and Varonia/Project Settings menus.",
                        current);
                    if (next != current)
                    {
                        SetDefines(next ? AddDefine(defines, DefineSymbol) : RemoveDefine(defines, DefineSymbol));
                        defines = GetDefines();
                    }

                    if (current)
                    {
                        GUILayout.Space(2);
                        EditorGUILayout.LabelField("✔  VBO_ADVANCED active — advanced menus available.", _styleDescription);
                    }
                }
            );

            // ── Section : AutoSizing ───────────────────────────────────────────
            DrawSectionCard(
                "📏  AutoSizing",
                "Global configuration for automatic player size calibration.",
                ColorAccentGreen,
                () =>
                {
                    EditorGUILayout.PropertyField(
                        so.FindProperty("autoStartCalibrationOnInput"),
                        new GUIContent("Calibrate on First Shot", "If enabled, the first shot/input from the player after the game starts will trigger auto-calibration.")
                    );
                }
            );

            // ── Section : SteamVR ────────────────────────────────────────────────
            DrawSectionCard(
                "🎮  SteamVR Tracking",
                "Enables SteamVR (OpenVR) support for controller and Vive Tracker tracking " +
                "via SteamVRBridge.",
                ColorAccentGreen,
                () =>
                {
                    bool current = HasDefine(defines, SteamVRSymbol);
                    bool next    = DrawToggleRow("Enable SteamVR",
                        "Required to use the SteamVR backend in ItemTracking and VaroniaWeaponTracking.",
                        current);

                    if (next != current)
                    {
                        SetDefines(next ? AddDefine(defines, SteamVRSymbol) : RemoveDefine(defines, SteamVRSymbol));
                        defines = GetDefines();
                        bool active = HasDefine(defines, SteamVRSymbol);
                        so.FindProperty("enableSteamVR").boolValue = active;
                    }

                    GUILayout.Space(4);

                    if (current)
                    {
                        DrawStatusRow("STEAMVR_ENABLED", true,
                            "SteamVR active — OpenVR tracking available.");
                    }
                    else
                    {
                        DrawStatusRow("STEAMVR_ENABLED", false,
                            "SteamVR inactive — only the OpenXR backend will be compiled.");
                    }
                }
            );


            // ── Section : Striker Haptics ────────────────────────────────────────
#if STRIKER_LINK
            DrawSectionCard(
                "🎯  Striker Haptics",
                "Haptic effects played on Striker initialization (StrikerLink.Unity.Runtime required).",
                ColorAccentOrange,
                () =>
                {
                    bool strikerActive = HasDefine(defines, StrikerSymbol);
         

                    GUILayout.Space(6);

                    EditorGUILayout.PropertyField(
                        so.FindProperty("InitStrikerHaptic"),
                        new GUIContent("Init Haptic Effect",
                            "HapticEffectAsset played when the Striker device is initialized.")
                    );

                    EditorGUILayout.PropertyField(
                        so.FindProperty("InitStrikerLibrary"),
                        new GUIContent("Init Haptic Library",
                            "HapticLibraryAsset used during Striker initialization.")
                    );
                }
            );
#endif

            // ── Section : Game Config ───────────────────────────────────────────
            DrawSectionCard(
                "📄  Game Config",
                "Enables the use of the optional GameConfig system.",
                ColorAccentPurple,
                () =>
                {
                    bool current = HasDefine(defines, GameConfigSymbol);
                    bool next    = DrawToggleRow("Enable Game Config",
                        "Required to use the GameConfig class and related features in BackOfficeVaronia.",
                        current);

                    if (next != current)
                    {
                        if (next)
                        {
                            EnsureGameConfigAssembly();
                        }
                        SetDefines(next ? AddDefine(defines, GameConfigSymbol) : RemoveDefine(defines, GameConfigSymbol));
                        defines = GetDefines();
                        bool active = HasDefine(defines, GameConfigSymbol);
                        so.FindProperty("useGameConfig").boolValue = active;
                    }

                    if (current)
                    {
                        DrawStatusRow("GAME_CONFIG", true,
                            "Game Config active — GameConfig class is available.");
                    }
                    else
                    {
                        DrawStatusRow("GAME_CONFIG", false,
                            "Game Config inactive — GameConfig features are disabled.");
                    }
                }
            );

            // ── Section : Game Score ───────────────────────────────────────────
            DrawSectionCard(
                "🏆  Game Score",
                "Enables the use of the optional GameScore system.",
                ColorAccentPurple,
                () =>
                {
                    bool current = HasDefine(defines, GameScoreSymbol);
                    bool next    = DrawToggleRow("Enable Game Score",
                        "Required to use the GameScore class and related features in BackOfficeVaronia.",
                        current);

                    if (next != current)
                    {
                        if (next)
                        {
                            EnsureGameScoreAssembly();
                        }
                        SetDefines(next ? AddDefine(defines, GameScoreSymbol) : RemoveDefine(defines, GameScoreSymbol));
                        defines = GetDefines();
                        bool active = HasDefine(defines, GameScoreSymbol);
                        so.FindProperty("useGameScore").boolValue = active;
                    }

                    if (current)
                    {
                        DrawStatusRow("GAME_SCORE", true,
                            "Game Score active — GameScore class is available.");
                    }
                    else
                    {
                        DrawStatusRow("GAME_SCORE", false,
                            "Game Score inactive — GameScore features are disabled.");
                    }
                }
            );

            // ── Section : Console Debug ──────────────────────────────────────────
            DrawSectionCard(
                "🖥  Console Debug",
                "Filtres d'exclusion pour VaroniaConsoleDisplay. " +
                "Toute entrée de log contenant l'un de ces termes sera masquée dans la console overlay.",
                ColorAccentOrange,
                () =>
                {
                    var filtersProp = so.FindProperty("consoleExcludeFilters");
                    EditorGUILayout.LabelField("Exclude Filters (contains)", EditorStyles.boldLabel);
                    GUILayout.Space(2);

                    for (int i = 0; i < filtersProp.arraySize; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        var elem = filtersProp.GetArrayElementAtIndex(i);
                        elem.stringValue = EditorGUILayout.TextField(elem.stringValue);
                        if (GUILayout.Button("✕", GUILayout.Width(24)))
                            filtersProp.DeleteArrayElementAtIndex(i);
                        EditorGUILayout.EndHorizontal();
                    }

                    GUILayout.Space(4);
                    if (GUILayout.Button("+ Add Filter", GUILayout.Width(100)))
                    {
                        filtersProp.InsertArrayElementAtIndex(filtersProp.arraySize);
                        filtersProp.GetArrayElementAtIndex(filtersProp.arraySize - 1).stringValue = "";
                    }
                }
            );

            // ── Section : Debug Scene Menu ───────────────────────────────────────
            DrawSectionCard(
                "🛠  Debug Scene Menu",
                "Routes the debug scene menu selection to a specific GameObject/method.",
                ColorAccentBlue,
                () =>
                {
                    EditorGUILayout.PropertyField(
                        so.FindProperty("debugMenuTargetObjectName"),
                        new GUIContent("Target Object Name", "Name of the GameObject in the scene that should receive the message.")
                    );

                    EditorGUILayout.PropertyField(
                        so.FindProperty("debugMenuTargetMethodName"),
                        new GUIContent("Target Method Name", "Name of the method to call on that GameObject (accepts string).")
                    );

                    if (string.IsNullOrEmpty(so.FindProperty("debugMenuTargetObjectName").stringValue))
                    {
                        GUILayout.Space(4);
                        EditorGUILayout.HelpBox("If left empty, the menu will directly load the scene via SceneManager.", MessageType.Info);
                    }
                }
            );

            // ── Footer ───────────────────────────────────────────────────────────
            GUILayout.Space(12);
            DrawSeparator(ColorSeparator);
            GUILayout.Space(4);
            EditorGUILayout.LabelField("Varonia Back Office Ultimate  •  VBO", _styleVersion);
            GUILayout.Space(4);

            // ── Sauvegarde ───────────────────────────────────────────────────────
            if (so.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }

        // ─── Composants UI ────────────────────────────────────────────────────────

        static void DrawHeader()
        {
            var rect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(rect, ColorHeader);

            GUILayout.Space(10);

            // Barre colorée gauche
            var barRect = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
            DrawGradientBar(barRect);

            GUILayout.Space(8);

            EditorGUILayout.LabelField("VARONIA BACK OFFICE", new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 16,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white }
            });

            EditorGUILayout.LabelField("Project Configuration", new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 10
            });

            GUILayout.Space(8);
            EditorGUILayout.EndVertical();
        }

        static void DrawGradientBar(Rect rect)
        {
            // Simule un dégradé avec 3 rects colorées
            float third = rect.width / 3f;
            EditorGUI.DrawRect(new Rect(rect.x,               rect.y, third,  rect.height), ColorAccentBlue);
            EditorGUI.DrawRect(new Rect(rect.x + third,       rect.y, third,  rect.height), ColorAccentGreen);
            EditorGUI.DrawRect(new Rect(rect.x + third * 2f,  rect.y, third,  rect.height), ColorAccentPurple);
        }

        static void DrawSectionCard(string title, string description, Color accentColor, System.Action content)
        {
            EditorGUILayout.BeginVertical(_styleCard);

            // Titre avec barre colorée
            EditorGUILayout.BeginHorizontal();
            var accentRect = GUILayoutUtility.GetRect(3, 18, GUILayout.Width(3));
            EditorGUI.DrawRect(accentRect, accentColor);
            GUILayout.Space(6);
            EditorGUILayout.LabelField(title, _styleSectionTitle);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);
            EditorGUILayout.LabelField(description, _styleDescription);
            GUILayout.Space(6);

            DrawSeparator(new Color(0.30f, 0.30f, 0.35f, 1f));
            GUILayout.Space(6);

            content?.Invoke();

            EditorGUILayout.EndVertical();
        }

        static bool DrawToggleRow(string label, string tooltip, bool value)
        {
            EditorGUILayout.BeginHorizontal();
            bool result = EditorGUILayout.Toggle(value, GUILayout.Width(16));
            GUILayout.Space(8);
            EditorGUILayout.LabelField(new GUIContent(label, tooltip), _styleSectionTitle);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        static void DrawStatusRow(string symbol, bool active, string message)
        {
            EditorGUILayout.BeginHorizontal();

            var badgeStyle = new GUIStyle(_styleBadge);
            if (active)
            {
                badgeStyle.normal.background = MakeTex(1, 1, new Color(0.20f, 0.70f, 0.35f, 0.30f));
                badgeStyle.normal.textColor  = new Color(0.40f, 1.00f, 0.55f, 1f);
            }
            else
            {
                badgeStyle.normal.background = MakeTex(1, 1, new Color(0.50f, 0.50f, 0.55f, 0.20f));
                badgeStyle.normal.textColor  = new Color(0.60f, 0.60f, 0.65f, 1f);
            }

            GUILayout.Label(active ? "● ON" : "○ OFF", badgeStyle, GUILayout.Width(48));
            GUILayout.Space(6);
            EditorGUILayout.LabelField(message, _styleDescription);
            EditorGUILayout.EndHorizontal();
        }

        static void DrawSeparator(Color color)
        {
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, color);
        }
    }
}
