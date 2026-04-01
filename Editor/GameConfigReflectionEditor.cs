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
    public class GameConfigReflectionEditor : EditorWindow
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

        [MenuItem("Varonia/GameConfig")]
        public static void ShowWindow()
        {
            var w = GetWindow<GameConfigReflectionEditor>("GameConfig");
            w.minSize = new Vector2(440, 540);
        }

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            stylesBuilt = false;
            Refresh();
        }
        private void OnFocus() => Refresh();

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
                fontSize  = 11,
                fontStyle = FontStyle.Italic,
                normal    = { textColor = colTextMuted },
                padding   = new RectOffset(0, 0, 3, 3),
                alignment = TextAnchor.MiddleRight,
            };
        }

        // ─── Refresh ──────────────────────────────────────────────────────────────

        private void Refresh()
        {
            _configObj   = null;
            _knownFields = null;
            _extraFields.Clear();

            // Search for GameConfig by simple name across all loaded assemblies
            Type configType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == "GameConfig")
                    {
                        configType = type;
                        break;
                    }
                }
                if (configType != null) break;
            }

            if (configType == null)
            {
                Debug.LogWarning("[GameConfig Editor] Type 'GameConfig' introuvable dans les assemblies.");
                return;
            }

            _knownFields = configType.GetFields(BindingFlags.Public | BindingFlags.Instance);

            _savePath = Path.Combine(Application.persistentDataPath, "Config.json");

            if (File.Exists(_savePath))
            {
                try
                {
                    var jObj = JObject.Parse(File.ReadAllText(_savePath));
                    _configObj = Activator.CreateInstance(configType);
                    foreach (var field in _knownFields)
                    {
                        if (!jObj.TryGetValue(field.Name, out JToken token)) continue;
                        try { field.SetValue(_configObj, token.ToObject(field.FieldType)); }
                        catch { /* ignore type mismatches — keep default */ }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GameConfig Editor] Erreur lecture JSON : {e.Message}");
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
                Debug.LogError($"[GameConfig Editor] Erreur lecture JSON : {e.Message}");
            }
        }

        // ─── GUI ──────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            BuildStyles();

            // Full-window dark background
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colBg);

            EditorGUILayout.Space(12);

            // ── Title bar ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.Label("GAME CONFIG", headerStyle);
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
                    var warnTitleStyle = new GUIStyle
                    {
                        fontSize  = 13,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal    = { textColor = colWarn },
                        padding   = new RectOffset(0, 0, 8, 4),
                        wordWrap  = true,
                    };
                    GUILayout.Label("Type 'GameConfig' introuvable", warnTitleStyle);
                    var subStyle = new GUIStyle(footerStyle) { normal = { textColor = colTextSecond } };
                    GUILayout.Label("Assurez-vous que la classe GameConfig est compilée dans le projet.", subStyle);
                    EditorGUILayout.Space(8);
                    if (GUILayout.Button("RAFRAÎCHIR", buttonStyle, GUILayout.Height(32)))
                        Refresh();
                }, colWarn);

                EditorGUILayout.Space(8);
                DrawFooter();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUIStyle.none, GUIStyle.none);

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
            GUILayout.Label(field.Name, fieldLabelStyle, GUILayout.Width(150));

            if (type == typeof(string))
                newValue = EditorGUILayout.TextField((string)value ?? "");
            else if (type == typeof(int))
                newValue = EditorGUILayout.IntField((int)value);
            else if (type == typeof(float))
                newValue = EditorGUILayout.FloatField((float)value);
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
                GUILayout.Label($"[{type.Name}] {value}", readOnlyStyle);
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
            GUILayout.Label("Varonia Back Office  ·  GameConfig", footerStyle);
        }

        // ─── Save ─────────────────────────────────────────────────────────────────

        private void SaveToJson()
        {
            if (_configObj == null)
            {
                Debug.LogError("[GameConfig Editor] Rien à sauvegarder.");
                return;
            }

            var jObj = new JObject();
            foreach (var field in _knownFields)
            {
                var v = field.GetValue(_configObj);
                jObj[field.Name] = v != null ? JToken.FromObject(v) : JValue.CreateNull();
            }
            foreach (var kvp in _extraFields)
                jObj[kvp.Key] = kvp.Value;

            string dir = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_savePath, jObj.ToString(Formatting.Indented));
            _isDirty = false;
            Debug.Log($"[GameConfig Editor] Sauvegardé → {_savePath}");
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

            Rect cardRect  = EditorGUILayout.BeginVertical();
            cardRect.x    -= 4;
            cardRect.width += 8;
            cardRect.y    -= 4;
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
