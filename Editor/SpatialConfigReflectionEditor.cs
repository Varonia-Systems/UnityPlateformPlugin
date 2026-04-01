using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice
{
    public class SpatialConfigReflectionEditor : EditorWindow
    {
        // ─── State ────────────────────────────────────────────────────────────────
        private object      _configObj;
        private FieldInfo[] _knownFields;
        private string      _savePath;
        private bool        _isDirty;
        private Vector2     _scroll;

        // Extra fields (JSON keys not present in the class)
        private readonly Dictionary<string, JToken> _extraFields = new Dictionary<string, JToken>();

        // "Add field" form state
        private string _newKey       = "";
        private string _newValue     = "";
        private bool   _newBoolValue;
        private int    _newTypeIdx;
        private static readonly string[] TypeLabels = { "string", "int", "float", "bool" };

        private const string PrefForceDisableBoundary = "VBO_ForceDisableBoundaryWarning";

        private static bool GetAdvBoundaryForceDisable()
        {
            var type = Type.GetType("VaroniaBackOffice.AdvBoundary, VboAdvBoundary.Runtime");
            if (type == null) return false;
            var prop = type.GetProperty("ForceDisableBoundaryWarning", BindingFlags.Public | BindingFlags.Static);
            return prop != null && (bool)prop.GetValue(null);
        }

        private static void SetAdvBoundaryForceDisable(bool value)
        {
            var type = Type.GetType("VaroniaBackOffice.AdvBoundary, VboAdvBoundary.Runtime");
            if (type == null) return;
            var prop = type.GetProperty("ForceDisableBoundaryWarning", BindingFlags.Public | BindingFlags.Static);
            if (prop != null) prop.SetValue(null, value);
        }

        // ── Style cache ──
        static bool     stylesBuilt;
        static GUIStyle headerStyle;
        static GUIStyle sectionStyle;
        static GUIStyle infoLabelStyle;
        static GUIStyle footerStyle;
        static GUIStyle buttonStyle;
        static GUIStyle tagStyle;
        static GUIStyle miniTagStyle;
        static GUIStyle badgeStyle;
        static GUIStyle fieldLabelStyle;
        static GUIStyle readOnlyStyle;

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

        // ── Textures (lazy) ──
        static Texture2D texCard, texAccentDim, texWarnDim, texErrorDim;
        static Texture2D texDivider, texBtn, texBtnHover, texAccentSolid, texWarnSolid, texBadge;

        // ─────────────────────────────────────────────────────────────────────────

        [MenuItem("Varonia/SpatialConfig")]
        public static void ShowWindow()
        {
            var w = GetWindow<SpatialConfigReflectionEditor>("SpatialConfig");
            w.minSize = new Vector2(440, 540);
        }

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            stylesBuilt = false;
            VaroniaProjectSettings.ApplyForceDisableBoundary();
            Refresh();
        }
        private void OnFocus() => Refresh();

        // ── Texture helpers ───────────────────────────────────────────────────────

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
                    if      (x < radius        && y < radius)
                        inside = new Vector2(x - radius,              y - radius).magnitude              <= radius;
                    else if (x >= w - radius   && y < radius)
                        inside = new Vector2(x - (w - radius - 1),   y - radius).magnitude              <= radius;
                    else if (x < radius        && y >= h - radius)
                        inside = new Vector2(x - radius,              y - (h - radius - 1)).magnitude   <= radius;
                    else if (x >= w - radius   && y >= h - radius)
                        inside = new Vector2(x - (w - radius - 1),   y - (h - radius - 1)).magnitude   <= radius;
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

            texCard        = MakeRoundedTex(32, 32, colCard, 6);
            texAccentDim   = MakeRoundedTex(32, 32, colAccentDim, 6);
            texWarnDim     = MakeRoundedTex(32, 32, colWarnDim, 6);
            texErrorDim    = MakeRoundedTex(32, 32, colErrorDim, 6);
            texDivider     = MakeTex(colDivider);
            texBtn         = MakeRoundedTex(32, 32, colBtnNormal, 5);
            texBtnHover    = MakeRoundedTex(32, 32, colBtnHover, 5);
            texAccentSolid = MakeRoundedTex(32, 32, colAccent, 5);
            texWarnSolid   = MakeRoundedTex(32, 32, colWarn, 5);
            texBadge       = MakeRoundedTex(32, 32, new Color(0.40f, 0.85f, 1f, 0.15f), 4);

            headerStyle = new GUIStyle
            {
                fontSize  = 18,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = colTextPrimary },
                padding   = new RectOffset(0, 0, 0, 0),
                margin    = new RectOffset(0, 0, 0, 0),
            };

            tagStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = colAccent, background = texAccentDim },
                padding   = new RectOffset(8, 8, 3, 3),
                margin    = new RectOffset(0, 4, 0, 0),
                border    = new RectOffset(6, 6, 6, 6),
            };

            miniTagStyle = new GUIStyle(tagStyle) { fontSize = 8 };

            sectionStyle = new GUIStyle
            {
                fontSize  = 10,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = colTextMuted },
                padding   = new RectOffset(0, 0, 6, 2),
                margin    = new RectOffset(0, 0, 4, 0),
            };

            infoLabelStyle = new GUIStyle
            {
                fontSize = 11,
                normal   = { textColor = colTextSecond },
                padding  = new RectOffset(0, 0, 2, 2),
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

            badgeStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.40f, 0.85f, 1f, 1f), background = texBadge },
                padding   = new RectOffset(6, 6, 2, 2),
                margin    = new RectOffset(0, 4, 1, 1),
                border    = new RectOffset(4, 4, 4, 4),
            };

            fieldLabelStyle = new GUIStyle
            {
                fontSize = 11,
                normal   = { textColor = colTextSecond },
                padding  = new RectOffset(0, 0, 3, 3),
            };

            readOnlyStyle = new GUIStyle
            {
                fontSize  = 10,
                fontStyle = FontStyle.Italic,
                normal    = { textColor = colTextMuted },
                padding   = new RectOffset(0, 0, 3, 3),
                alignment = TextAnchor.MiddleRight,
                wordWrap  = false,
            };
        }

        // ─── Refresh ──────────────────────────────────────────────────────────────

        private void Refresh()
        {
            _configObj   = null;
            _knownFields = null;
            _extraFields.Clear();

            Type configType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == "Spatial")
                    {
                        configType = type;
                        break;
                    }
                }
                if (configType != null) break;
            }

            if (configType == null)
            {
                Debug.LogWarning("[SpatialConfig Editor] Type 'Spatial' introuvable dans les assemblies.");
                return;
            }

            _knownFields = configType.GetFields(BindingFlags.Public | BindingFlags.Instance);

            // Same directory as GlobalConfig.json
            _savePath = Path.Combine(
                Application.persistentDataPath
                    .Replace(Application.companyName + "/" + Application.productName, "Varonia"),
                "NewSpatial.json"
            );

            if (File.Exists(_savePath))
            {
                try
                {
                    var json = File.ReadAllText(_savePath);
                    _configObj = JsonConvert.DeserializeObject(json, configType);
                    if (_configObj == null)
                        _configObj = Activator.CreateInstance(configType);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SpatialConfig Editor] Erreur lecture JSON : {e.Message}");
                    _configObj = Activator.CreateInstance(configType);
                }
            }
            else
            {
                _configObj = Activator.CreateInstance(configType);
            }

            LoadExtraFields();
            _isDirty = false;
        }

        private void LoadExtraFields()
        {
            _extraFields.Clear();
            if (!File.Exists(_savePath)) return;
            try
            {
                var jObj  = JObject.Parse(File.ReadAllText(_savePath));
                var known = new HashSet<string>();
                if (_knownFields != null)
                    foreach (var f in _knownFields) known.Add(f.Name);
                foreach (var prop in jObj.Properties())
                    if (!known.Contains(prop.Name))
                        _extraFields[prop.Name] = prop.Value;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SpatialConfig Editor] Erreur lecture JSON : {e.Message}");
            }
        }

        // ─── GUI ──────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            BuildStyles();

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colBg);

            EditorGUILayout.Space(12);

            // ── Title bar ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.Label("SPATIAL CONFIG", headerStyle);
            GUILayout.FlexibleSpace();

            bool fileExists = _savePath != null && File.Exists(_savePath);
            Color pillCol   = _isDirty ? colWarn : (fileExists ? colAccent : colTextMuted);
            string pillText = _isDirty ? "  UNSAVED  " : (fileExists ? "  SYNCED  " : "  NO FILE  ");
            var pillStyle   = new GUIStyle(tagStyle);
            pillStyle.normal.textColor  = pillCol;
            pillStyle.normal.background = MakeRoundedTex(32, 32, new Color(pillCol.r, pillCol.g, pillCol.b, 0.15f), 6);
            GUILayout.Label(pillText, pillStyle);

            GUILayout.Space(16);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(16);

            // ── Type not found ──
            if (_configObj == null)
            {
                DrawCard(() =>
                {
                    var warnStyle = new GUIStyle
                    {
                        fontSize  = 13,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal    = { textColor = colWarn },
                        padding   = new RectOffset(0, 0, 8, 4),
                        wordWrap  = true,
                    };
                    GUILayout.Label("Type 'Spatial' introuvable", warnStyle);
                    var subStyle = new GUIStyle(footerStyle) { normal = { textColor = colTextSecond }, wordWrap = true };
                    GUILayout.Label(
                        "Copiez Samples~/NexSpatialTypes/SpatialTypes.cs dans Assets/ de votre projet jeu, puis recompilez.",
                        subStyle);
                    EditorGUILayout.Space(8);
                    if (GUILayout.Button("RAFRAÎCHIR", buttonStyle, GUILayout.Height(32)))
                        Refresh();
                }, colWarn);

                EditorGUILayout.Space(8);
                DrawFooter();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUIStyle.none, GUIStyle.none);

            // ── Editor Settings card ──
            DrawCard(() =>
            {
                DrawSectionLabel("PARAMÈTRES ÉDITEUR");
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(6);

                EditorGUI.BeginChangeCheck();
                bool forceDisable = EditorPrefs.GetBool(PrefForceDisableBoundary, false);
                bool newForce = EditorGUILayout.ToggleLeft(" Ignorer l'alerte 'Hors Zone' (3D Boundary)", forceDisable, fieldLabelStyle);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetBool(PrefForceDisableBoundary, newForce);
                    VaroniaProjectSettings.ApplyForceDisableBoundary();
                }
                EditorGUILayout.Space(2);
            }, colWarn);

            EditorGUILayout.Space(8);

            // ── Known fields card ──
            DrawCard(() =>
            {
                DrawSectionLabel("CHAMPS CONNUS  ·  RÉFLEXION");
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(6);
                DrawKnownFields();
            }, colAccent);

            EditorGUILayout.Space(8);

            // ── 2D Preview card ──
            DrawCard(() =>
            {
                DrawSectionLabel("APERÇU 2D  ·  BOUNDARIES");
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(6);
                DrawBoundaryPreview();
                EditorGUILayout.Space(2);
            }, new Color(0.40f, 0.65f, 1f, 1f));

            EditorGUILayout.Space(8);

            // ── Extra fields card ──
            if (_extraFields.Count > 0)
            {
                DrawCard(() =>
                {
                    DrawSectionLabel("CHAMPS SUPPLÉMENTAIRES  ·  JSON UNIQUEMENT");
                    EditorGUILayout.Space(4);
                    DrawDivider();
                    EditorGUILayout.Space(6);
                    DrawExtraFields();
                }, colWarn);

                EditorGUILayout.Space(8);
            }

            // ── Add field card ──
            DrawCard(() =>
            {
                DrawSectionLabel("AJOUTER UN CHAMP");
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(6);
                DrawAddField();
            }, colTextMuted);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            DrawFooter();
            EditorGUILayout.Space(8);
        }

        // ─── Known fields ─────────────────────────────────────────────────────────

        private void DrawKnownFields()
        {
            foreach (var field in _knownFields)
                DrawReflectedField(field);
        }

        private void DrawReflectedField(FieldInfo field)
        {
            var value = field.GetValue(_configObj);
            var type  = field.FieldType;

            EditorGUI.BeginChangeCheck();
            object newValue;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(field.Name, fieldLabelStyle, GUILayout.Width(160));

            if (type == typeof(string))
                newValue = EditorGUILayout.TextField((string)value ?? "");
            else if (type == typeof(int))
                newValue = EditorGUILayout.IntField((int)value);
            else if (type == typeof(float))
                newValue = EditorGUILayout.FloatField((float)value);
            else if (type == typeof(double))
                newValue = EditorGUILayout.DoubleField((double)value);
            else if (type == typeof(bool))
                newValue = EditorGUILayout.Toggle((bool)value, GUILayout.Width(20));
            else if (type.IsEnum)
            {
                var values = Enum.GetValues(type);
                var names  = Enum.GetNames(type);
                int cur    = Array.IndexOf(values, value);
                int next   = EditorGUILayout.Popup(Mathf.Max(cur, 0), names);
                newValue   = values.GetValue(next);
            }
            else
            {
                // Complex type (Vector3_, List<Boundary_>, etc.) — show JSON preview, read-only
                string preview = value != null
                    ? JsonConvert.SerializeObject(value, Formatting.None)
                    : "null";
                if (preview.Length > 80) preview = preview.Substring(0, 77) + "…";
                GUILayout.Label(preview, readOnlyStyle);
                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                field.SetValue(_configObj, newValue);
                _isDirty = true;
            }
        }

        // ─── Extra fields ─────────────────────────────────────────────────────────

        private void DrawExtraFields()
        {
            var toRemove = new List<string>();
            var toUpdate = new List<(string key, JToken value)>();

            foreach (var kvp in _extraFields)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"[{kvp.Value.Type}]", badgeStyle, GUILayout.Width(60));
                GUILayout.Label(kvp.Key, fieldLabelStyle, GUILayout.Width(110));

                JToken edited = DrawJTokenEditor(kvp.Value);
                if (edited.ToString() != kvp.Value.ToString())
                {
                    toUpdate.Add((kvp.Key, edited));
                    _isDirty = true;
                }

                var removeStyle = new GUIStyle(buttonStyle)
                {
                    fontSize = 10,
                    padding  = new RectOffset(4, 4, 3, 3),
                };
                removeStyle.normal.background = MakeRoundedTex(32, 32, colErrorDim, 4);
                removeStyle.normal.textColor  = colError;
                removeStyle.hover.textColor   = Color.white;
                if (GUILayout.Button("✕", removeStyle, GUILayout.Width(24), GUILayout.Height(22)))
                {
                    toRemove.Add(kvp.Key);
                    _isDirty = true;
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            foreach (var key in toRemove)          _extraFields.Remove(key);
            foreach (var (key, val) in toUpdate)   _extraFields[key] = val;
        }

        private static JToken DrawJTokenEditor(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Boolean: return JToken.FromObject(EditorGUILayout.Toggle(token.Value<bool>()));
                case JTokenType.Integer: return JToken.FromObject(EditorGUILayout.IntField(token.Value<int>()));
                case JTokenType.Float:   return JToken.FromObject(EditorGUILayout.FloatField(token.Value<float>()));
                default:                 return JToken.FromObject(EditorGUILayout.TextField(token.ToString()));
            }
        }

        // ─── Add field ────────────────────────────────────────────────────────────

        private void DrawAddField()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Nom", fieldLabelStyle, GUILayout.Width(45));
            _newKey     = EditorGUILayout.TextField(_newKey);
            _newTypeIdx = EditorGUILayout.Popup(_newTypeIdx, TypeLabels, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Valeur", fieldLabelStyle, GUILayout.Width(45));
            if (TypeLabels[_newTypeIdx] == "bool")
                _newBoolValue = EditorGUILayout.Toggle(_newBoolValue);
            else
                _newValue = EditorGUILayout.TextField(_newValue);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            bool canAdd = !string.IsNullOrWhiteSpace(_newKey)
                          && !_extraFields.ContainsKey(_newKey)
                          && (_knownFields == null || Array.FindIndex(_knownFields, f => f.Name == _newKey) < 0);

            var addStyle = new GUIStyle(buttonStyle);
            if (canAdd)
            {
                addStyle.normal.background = MakeRoundedTex(32, 32, colAccentDim, 5);
                addStyle.normal.textColor  = colAccent;
                addStyle.hover.textColor   = Color.white;
                addStyle.active.background = texAccentSolid;
            }

            GUI.enabled = canAdd;
            if (GUILayout.Button("+ AJOUTER LE CHAMP", addStyle, GUILayout.Height(30)))
            {
                string typeLabel = TypeLabels[_newTypeIdx];
                _extraFields[_newKey] = typeLabel == "bool"
                    ? JToken.FromObject(_newBoolValue)
                    : BuildToken(_newValue, typeLabel);
                _newKey       = "";
                _newValue     = "";
                _newBoolValue = false;
                _isDirty      = true;
            }
            GUI.enabled = true;
        }

        // ─── Footer ───────────────────────────────────────────────────────────────

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);

            if (GUILayout.Button("RAFRAÎCHIR", buttonStyle, GUILayout.Height(34), GUILayout.MinWidth(110)))
                Refresh();

            GUILayout.FlexibleSpace();

            Color   saveColor = _isDirty ? colWarn : colAccent;
            var     saveStyle = new GUIStyle(buttonStyle);
            saveStyle.normal.background = MakeRoundedTex(32, 32, new Color(saveColor.r, saveColor.g, saveColor.b, 0.2f), 5);
            saveStyle.normal.textColor  = saveColor;
            saveStyle.hover.textColor   = Color.white;
            saveStyle.active.background = _isDirty ? texWarnSolid : texAccentSolid;

            string saveLabel = _isDirty ? "  SAUVEGARDER  ●" : "  SAUVEGARDER";
            if (GUILayout.Button(saveLabel, saveStyle, GUILayout.Height(34), GUILayout.MinWidth(150)))
                SaveToJson();

            GUILayout.Space(16);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            var pathStyle = new GUIStyle(footerStyle) { wordWrap = true };
            GUILayout.Label(_savePath ?? "—", pathStyle);

            EditorGUILayout.Space(2);
            GUILayout.Label("Varonia Back Office  ·  SpatialConfig", footerStyle);
        }

        // ─── Save ─────────────────────────────────────────────────────────────────

        private void SaveToJson()
        {
            if (_configObj == null)
            {
                Debug.LogError("[SpatialConfig Editor] Rien à sauvegarder.");
                return;
            }

            string dir = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Serialize the full object (handles nested types: Vector3_, List<Boundary_>, etc.)
            var jObj = JObject.FromObject(_configObj);
            foreach (var kvp in _extraFields)
                jObj[kvp.Key] = kvp.Value;

            File.WriteAllText(_savePath, jObj.ToString(Formatting.Indented));
            _isDirty = false;
            Debug.Log($"[SpatialConfig Editor] Sauvegardé → {_savePath}");
        }

        // ─── 2D Boundary Preview ──────────────────────────────────────────────────

        private void DrawBoundaryPreview()
        {
            if (_configObj == null) return;
            var spatial = (Spatial)_configObj;

            bool hasData = spatial.Boundaries != null && spatial.Boundaries.Count > 0;

            Rect previewRect = GUILayoutUtility.GetRect(
                GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(hasData ? 290f : 52f)
            );

            EditorGUI.DrawRect(previewRect, new Color(0.07f, 0.07f, 0.09f));

            if (!hasData)
            {
                var noDataStyle = new GUIStyle
                {
                    fontSize  = 10,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = colTextMuted },
                };
                GUI.Label(previewRect, "Aucune boundary à afficher", noDataStyle);
                return;
            }

            // ── Compute world bounds ──────────────────────────────────────────────
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (var b in spatial.Boundaries)
            {
                if (b?.Points == null) continue;
                foreach (var p in b.Points)
                {
                    if (p == null) continue;
                    if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                    if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
                }
            }

            if (spatial.SyncPos != null)
            {
                if (spatial.SyncPos.x < minX) minX = spatial.SyncPos.x;
                if (spatial.SyncPos.x > maxX) maxX = spatial.SyncPos.x;
                if (spatial.SyncPos.z < minZ) minZ = spatial.SyncPos.z;
                if (spatial.SyncPos.z > maxZ) maxZ = spatial.SyncPos.z;
            }

            if (minX >= maxX || minZ >= maxZ) return;

            float padX = (maxX - minX) * 0.10f;
            float padZ = (maxZ - minZ) * 0.10f;
            minX -= padX; maxX += padX;
            minZ -= padZ; maxZ += padZ;

            float worldW = maxX - minX;
            float worldH = maxZ - minZ;

            // ── Fit into preview rect ─────────────────────────────────────────────
            float inset  = 14f;
            float drawW  = previewRect.width  - inset * 2f;
            float drawH  = previewRect.height - inset * 2f;
            float scale  = Mathf.Min(drawW / worldW, drawH / worldH);

            float ox = previewRect.x + inset + (drawW - worldW * scale) * 0.5f;
            float oy = previewRect.y + inset + (drawH - worldH * scale) * 0.5f;

            Vector3 ToScreen(Vector3_ p) =>
                new Vector3(ox + (p.x - minX) * scale, oy + (maxZ - p.z) * scale, 0f);

            if (Event.current.type != EventType.Repaint) return;

            // ── Grid ─────────────────────────────────────────────────────────────
            float fitW = worldW * scale;
            float fitH = worldH * scale;

            Handles.color = new Color(1f, 1f, 1f, 0.04f);
            const int gridN = 8;
            for (int i = 0; i <= gridN; i++)
            {
                float tx = ox + fitW * i / gridN;
                float ty = oy + fitH * i / gridN;
                Handles.DrawLine(new Vector3(tx, oy,       0), new Vector3(tx, oy + fitH, 0));
                Handles.DrawLine(new Vector3(ox, ty,       0), new Vector3(ox + fitW, ty, 0));
            }

            // ── Boundaries ───────────────────────────────────────────────────────
            foreach (var boundary in spatial.Boundaries)
            {
                if (boundary?.Points == null || boundary.Points.Count < 2) continue;

                var   bc  = boundary.BoundaryColor;
                Color col = bc != null ? new Color(bc.x, bc.y, bc.z) : Color.green;
                float lw  = boundary.MainBoundary ? 2.5f : 1.5f;

                // Outline
                Handles.color = col;
                var pts = boundary.Points;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (pts[i] == null || pts[(i + 1) % pts.Count] == null) continue;
                    Handles.DrawAAPolyLine(lw, ToScreen(pts[i]), ToScreen(pts[(i + 1) % pts.Count]));
                }

                // Vertex dots
                Handles.color = new Color(col.r, col.g, col.b, 0.55f);
                foreach (var p in pts)
                {
                    if (p == null) continue;
                    Handles.DrawSolidDisc(ToScreen(p), Vector3.forward, 2.5f);
                }

                // Obstacles
                if (boundary.Obstacles != null)
                {
                    foreach (var obs in boundary.Obstacles)
                    {
                        if (obs?.Position == null) continue;
                        Handles.color = new Color(1f, 0.55f, 0.12f, 0.85f);
                        float r = Mathf.Max(4f, obs.Scale * scale * 0.3f);
                        Handles.DrawWireDisc(ToScreen(obs.Position), Vector3.forward, r);
                        Handles.DrawSolidDisc(ToScreen(obs.Position), Vector3.forward, 2f);
                    }
                }
            }

            // ── SyncPos cross ────────────────────────────────────────────────────
            if (spatial.SyncPos != null)
            {
                var  sp = ToScreen(spatial.SyncPos);
                float cs = 8f;
                Handles.color = colAccent;
                Handles.DrawAAPolyLine(2f, sp - new Vector3(cs, 0), sp + new Vector3(cs, 0));
                Handles.DrawAAPolyLine(2f, sp - new Vector3(0, cs), sp + new Vector3(0, cs));
                Handles.DrawWireDisc(sp, Vector3.forward, 4.5f);
            }

            // ── Legend ───────────────────────────────────────────────────────────
            var legendStyle = new GUIStyle
            {
                fontSize  = 9,
                alignment = TextAnchor.LowerRight,
                normal    = { textColor = colTextMuted },
            };
            GUI.Label(
                new Rect(previewRect.x, previewRect.yMax - 18f, previewRect.width - 6f, 16f),
                $"{spatial.Boundaries.Count} boundar{(spatial.Boundaries.Count > 1 ? "ies" : "y")}",
                legendStyle
            );
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static JToken BuildToken(string raw, string typeLabel)
        {
            switch (typeLabel)
            {
                case "int":   return int.TryParse(raw,   out int i)   ? JToken.FromObject(i)   : JToken.FromObject(0);
                case "float": return float.TryParse(raw, out float f) ? JToken.FromObject(f)   : JToken.FromObject(0f);
                case "bool":  return bool.TryParse(raw,  out bool b)  ? JToken.FromObject(b)   : JToken.FromObject(false);
                default:      return JToken.FromObject(raw);
            }
        }

        void DrawCard(Action content, Color accentColor)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            EditorGUILayout.BeginVertical();

            Rect cardRect   = EditorGUILayout.BeginVertical();
            cardRect.x     -= 4;
            cardRect.width += 8;
            cardRect.y     -= 4;
            cardRect.height += 8;

            EditorGUI.DrawRect(cardRect, colCard);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 2), accentColor);

            GUILayout.Space(12);
            content();
            GUILayout.Space(12);

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();
        }

        void DrawSectionLabel(string text) =>
            GUILayout.Label(text, sectionStyle);

        void DrawDivider()
        {
            Rect r  = GUILayoutUtility.GetRect(1, 1);
            r.x    += 20;
            r.width -= 40;
            EditorGUI.DrawRect(r, colDivider);
        }
    }
}
