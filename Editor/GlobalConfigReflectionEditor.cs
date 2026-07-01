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
    public class GlobalConfigReflectionEditor : EditorWindow
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

        // Profils (debug)
        private string _newProfileName = "";
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

        [MenuItem("Varonia/GlobalConfig")]
        public static void ShowWindow()
        {
            var w = GetWindow<GlobalConfigReflectionEditor>("GlobalConfig");
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

            Type configType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                configType = assembly.GetType("VaroniaBackOffice.GlobalConfig");
                if (configType != null) break;
            }

            if (configType == null)
            {
                Debug.LogError("[Config Editor] Type 'GlobalConfig' introuvable dans les assemblies.");
                return;
            }

            _knownFields = configType.GetFields(BindingFlags.Public | BindingFlags.Instance);

            _savePath = Path.Combine(
                Application.persistentDataPath
                    .Replace(Application.companyName + "/" + Application.productName, "Varonia"),
                "GlobalConfig.json"
            );

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
                    Debug.LogError($"[Config Editor] Erreur lecture JSON : {e.Message}");
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
                Debug.LogError($"[Config Editor] Erreur lecture JSON : {e.Message}");
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
            GUILayout.Label("GLOBAL CONFIG", headerStyle);
            GUILayout.FlexibleSpace();

            bool fileExists = _savePath != null && File.Exists(_savePath);
            Color pillCol   = _isDirty ? colWarn : (fileExists ? colAccent : colTextMuted);
            string pillText = _isDirty ? "  UNSAVED  " : (fileExists ? "  SYNCED  " : "  NO FILE  ");
            string pillTip  = _isDirty
                ? "You have unsaved changes. Click SAVE to write them to GlobalConfig.json."
                : (fileExists
                    ? "All changes are saved to disk. The file on disk matches the in-memory config."
                    : "No GlobalConfig.json found yet — saving will create one at the path shown below.");
            var pillStyle   = new GUIStyle(tagStyle);
            pillStyle.normal.textColor  = pillCol;
            pillStyle.normal.background = MakeRoundedTex(32, 32, new Color(pillCol.r, pillCol.g, pillCol.b, 0.15f), 6);
            GUILayout.Label(new GUIContent(pillText, pillTip), pillStyle);

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
                    GUILayout.Label("Type 'GlobalConfig' not found", warnTitleStyle);
                    var subStyle = new GUIStyle(footerStyle) { normal = { textColor = colTextSecond } };
                    GUILayout.Label("Make sure the package is compiled.", subStyle);
                    EditorGUILayout.Space(8);
                    if (GUILayout.Button("REFRESH", buttonStyle, GUILayout.Height(32)))
                        Refresh();
                }, colWarn);

                EditorGUILayout.Space(8);
                DrawFooter();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUIStyle.none, GUIStyle.none);

            // ── Profiles card (debug : plusieurs configs cachées) — en haut pour être visible ──
            DrawCard(() =>
            {
                DrawSectionLabel("PROFILES  ·  DEBUG SWITCH");
                EditorGUILayout.Space(4);
                DrawDivider();
                EditorGUILayout.Space(6);
                DrawProfiles();
            }, colWarn);

            EditorGUILayout.Space(8);

            // ── Known fields card ──
            DrawCard(() =>
            {
                DrawSectionLabel("KNOWN FIELDS  ·  REFLECTION");
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
                    DrawSectionLabel("EXTRA FIELDS  ·  JSON ONLY");
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
                DrawSectionLabel("ADD FIELD");
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

        // ─── Tooltips ─────────────────────────────────────────────────────────────
        // Centralisé ici pour pouvoir documenter chaque champ d'un coup d'œil.
        // La clé est le nom du field (reflection) → texte affiché au hover du label.
        private static readonly Dictionary<string, string> FieldTooltips = new Dictionary<string, string>
        {
            // Network
            { "ServerIP",      "IP address of the main game server. Use 'localhost' for local testing." },
            { "MQTT_ServerIP", "IP address of the MQTT broker used for cross-process messaging." },
            { "MQTT_IDClient", "Unique numeric ID used by this client when connecting to the MQTT broker. Must be different per machine on the same broker." },

            // Preferences
            { "DeviceMode", "Role of this device in the session: Server+Player, Client+Player, Spectator, etc." },
            { "Language",   "UI and localized content language (e.g. 'Fr', 'En')." },
            { "MainHand",   "Player's dominant hand. Drives weapon hold side and input bindings." },
            { "PlayerName", "Display name used in lobbies, scoreboards and chat." },
            { "HideMode",   "Internal display mode flag. Leave at 0 unless instructed." },
            { "Direct",     "Optional fast-path used after a crash or resync — skips some steps (depending on the game) to rejoin faster." },

            // Devices (multi-arme)
            { "Devices", "Per-weapon binding list. Index in this list = weaponIndex used by VaroniaInput / VaroniaWeaponTracking." },

            // VR
            { "HeadsetName", "Manual override for detected VR headset name (e.g. 'Pico 4 Ultra', 'Vive Focus 3'). Leave empty to auto-detect via OpenVR / OpenXR. Drives debug latency chart selection." },
        };

        private static string GetTooltip(string fieldName) =>
            FieldTooltips.TryGetValue(fieldName, out var t) ? t : "";

        private void DrawKnownFields()
        {
            foreach (var field in _knownFields)
                DrawReflectedField(field);
        }

        private void DrawReflectedField(FieldInfo field)
        {
            var value = field.GetValue(_configObj);
            var type  = field.FieldType;

            // ── Cas spécial : liste typée de WeaponBinding (nouveau système multi-armes) ──
            if (type == typeof(List<WeaponBinding>))
            {
                DrawWeaponBindingList(field, value as List<WeaponBinding>);
                return;
            }

            EditorGUI.BeginChangeCheck();
            object newValue;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(field.Name, GetTooltip(field.Name)), fieldLabelStyle, GUILayout.Width(150));

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
                
                // On ajoute le int entre parenthèses pour chaque option
                string[] labels = new string[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    int intVal = (int)values.GetValue(i);
                    labels[i]  = $"{names[i]} ({intVal})";
                }

                int cur    = Array.IndexOf(values, value);
                int next   = EditorGUILayout.Popup(Mathf.Max(cur, 0), labels);
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

        // ─── WeaponBinding list (nouveau système multi-armes) ────────────────────

        private void DrawWeaponBindingList(FieldInfo field, List<WeaponBinding> list)
        {
            if (list == null)
            {
                list = new List<WeaponBinding>();
                field.SetValue(_configObj, list);
                _isDirty = true;
            }

            // Encadrement rouge léger autour de TOUT le bloc Devices.
            Rect blockRect = EditorGUILayout.BeginVertical();

            // ── Header : nom + badge de comptage ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(field.Name, GetTooltip(field.Name)), fieldLabelStyle, GUILayout.Width(150));

            int parentCount = 0;
            foreach (var d in list) if (GlobalConfig.IsParent(d)) parentCount++;
            string countLabel = $"  {parentCount} weapon{(parentCount > 1 ? "s" : "")} · {list.Count} device{(list.Count > 1 ? "s" : "")}  ";
            var countStyle = new GUIStyle(badgeStyle);
            GUILayout.Label(new GUIContent(countLabel, "Papas (armes) · total devices (papas + trackers). L'index runtime = position parmi les papas."), countStyle);

            if (list.Count == 0)
            {
                var emptyStyle = new GUIStyle(badgeStyle);
                emptyStyle.normal.textColor  = colTextSecond;
                emptyStyle.normal.background = MakeRoundedTex(32, 32, new Color(0.4f, 0.4f, 0.4f, 0.15f), 4);
                GUILayout.Label(new GUIContent(
                    "  empty — no weapon configured  ",
                    "List is empty — no weapon will be resolved. Add at least one device."), emptyStyle);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ── Header de colonnes (si au moins une entrée) ──
            if (list.Count > 0)
            {
                var colHeaderStyle = new GUIStyle(fieldLabelStyle)
                {
                    fontSize = 9,
                    normal = { textColor = colTextMuted },
                };
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("",            colHeaderStyle, GUILayout.Width(30));
                GUILayout.Label(new GUIContent("Controller",
                    "Weapon model / controller type for this slot. The numeric value in parentheses is the controller ID used by VaroniaWeapon to spawn the right prefab."), colHeaderStyle);
                GUILayout.Label(new GUIContent("Identifier",
                    "Unique device id: MAC address (for MQTT) OR tracking serial (SteamVR 'LHR-XXXXXXXX', OpenXR device id, etc.). Merges the old Serial + Tracking ID. Mutually exclusive with 'Force Steam'."), colHeaderStyle);
                GUILayout.Label("", colHeaderStyle, GUILayout.Width(24)); // espace pour le "OR"
                GUILayout.Label(new GUIContent("Force Steam",
                    "SteamVR device index override. -1 = auto (disabled), >= 0 = force this slot to track the SteamVR device at that index. Mutually exclusive with 'Identifier'."), colHeaderStyle, GUILayout.Width(70));
                GUILayout.Label(new GUIContent("Parent",
                    "Parent weapon of this device, referenced by its Identifier. Children are shown indented under their parent."), colHeaderStyle, GUILayout.Width(120));
                GUILayout.Label(new GUIContent("Default",
                    "Marks this weapon as the default one. Only a single entry can be default at a time."), colHeaderStyle, GUILayout.Width(48));
                GUILayout.Label("",            colHeaderStyle, GUILayout.Width(24));
                EditorGUILayout.EndHorizontal();
            }

            // ── Lignes : [index] [Controller dropdown] [Identifier] [OR] [Force Steam] [Default] [✕] ──
            int toRemove = -1;
            var controllerValues = Enum.GetValues(typeof(Controller));
            var controllerNames  = Enum.GetNames(typeof(Controller));
            string[] controllerLabels = new string[controllerNames.Length];
            for (int i = 0; i < controllerNames.Length; i++)
            {
                int iv = (int)controllerValues.GetValue(i);
                controllerLabels[i] = $"{controllerNames[i]} ({iv})";
            }

            // ── Pré-calcul de la hiérarchie (parent via Identifier) ──────────────────
            // Map Identifier -> index (première occurrence).
            var idToIndex = new Dictionary<string, int>();
            for (int k = 0; k < list.Count; k++)
            {
                var id = list[k]?.Identifier;
                if (!string.IsNullOrEmpty(id) && !idToIndex.ContainsKey(id)) idToIndex[id] = k;
            }
            // Index du parent de chaque entrée (-1 = racine).
            int[] parentIdx = new int[list.Count];
            for (int k = 0; k < list.Count; k++)
            {
                parentIdx[k] = -1;
                var pid = list[k]?.LinkParent;
                if (!string.IsNullOrEmpty(pid) && idToIndex.TryGetValue(pid, out int pi) && pi != k)
                    parentIdx[k] = pi;
            }
            // Profondeur (avec garde anti-cycle).
            int[] depth = new int[list.Count];
            for (int k = 0; k < list.Count; k++)
            {
                int d = 0, cur = parentIdx[k], guard = 0;
                while (cur >= 0 && guard++ < list.Count) { d++; cur = parentIdx[cur]; }
                depth[k] = d;
            }
            // Ordre d'affichage : DFS depuis les racines → enfants juste sous leur parent.
            var displayOrder = new List<int>(list.Count);
            var visited = new bool[list.Count];
            void Dfs(int idx)
            {
                if (idx < 0 || idx >= list.Count || visited[idx]) return;
                visited[idx] = true;
                displayOrder.Add(idx);
                for (int c = 0; c < list.Count; c++)
                    if (parentIdx[c] == idx) Dfs(c);
            }
            for (int k = 0; k < list.Count; k++) if (parentIdx[k] < 0) Dfs(k);
            for (int k = 0; k < list.Count; k++) if (!visited[k]) displayOrder.Add(k); // orphelins / cycles

            // Options du dropdown Parent : "(none)" + tous les Identifier non vides.
            var parentIds    = new List<string> { "" };
            var parentLabels = new List<string> { "(none)" };
            for (int k = 0; k < list.Count; k++)
            {
                var id = list[k]?.Identifier;
                if (string.IsNullOrEmpty(id)) continue;
                parentIds.Add(id);
                parentLabels.Add($"#{k}  {id}");
            }
            string[] parentLabelsArr = parentLabels.ToArray();

            // Détecte si affecter newParentId à childIndex créerait un cycle.
            bool WouldCycle(int childIndex, string newParentId)
            {
                if (string.IsNullOrEmpty(newParentId)) return false;
                if (!idToIndex.TryGetValue(newParentId, out int p)) return false;
                int guard = 0;
                while (p >= 0 && guard++ <= list.Count)
                {
                    if (p == childIndex) return true;
                    var pid = list[p]?.LinkParent;
                    if (string.IsNullOrEmpty(pid) || !idToIndex.TryGetValue(pid, out p)) break;
                }
                return false;
            }

            // Encadré bleu pour les Trackers (60).
            Color colTrackerBlue = new Color(0.35f, 0.62f, 1f, 1f);
            var trackerBoxStyle = new GUIStyle
            {
                normal  = { background = MakeRoundedTex(32, 32, new Color(colTrackerBlue.r, colTrackerBlue.g, colTrackerBlue.b, 0.12f), 4) },
                border  = new RectOffset(4, 4, 4, 4),
                padding = new RectOffset(0, 0, 0, 0),
                margin  = new RectOffset(0, 0, 0, 0),
            };

            foreach (int i in displayOrder)
            {
                var entry = list[i] ?? (list[i] = new WeaponBinding());

                // Force Steam n'est proposé que pour les Trackers (60). Capturé ici (avant le dropdown
                // Controller) pour garder un nombre de contrôles IMGUI constant sur la frame.
                bool isTracker = entry.Controller == Controller.TRACKER;

                // Encadré bleu sur toute la ligne pour un Tracker (60).
                Rect rowRect = EditorGUILayout.BeginHorizontal(isTracker ? trackerBoxStyle : GUIStyle.none);

                // Indentation hiérarchique + connecteur pour les enfants.
                if (depth[i] > 0)
                {
                    GUILayout.Space(depth[i] * 16f);
                    var connStyle = new GUIStyle(badgeStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
                    connStyle.normal.textColor = colTextMuted;
                    connStyle.normal.background = null;
                    GUILayout.Label("└", connStyle, GUILayout.Width(12));
                }

                // Index badge — fond orange si l'entrée a un parent (LinkParent résolu).
                bool hasParent = parentIdx[i] >= 0;
                var idxStyle = new GUIStyle(badgeStyle) { alignment = TextAnchor.MiddleCenter };
                if (hasParent)
                {
                    idxStyle.normal.background = MakeRoundedTex(32, 32, new Color(1f, 0.55f, 0.15f, 0.85f), 4);
                    idxStyle.normal.textColor  = Color.white;
                }
                GUILayout.Label(new GUIContent($"#{i}",
                    hasParent ? "This device has a parent (LinkParent)." : null), idxStyle, GUILayout.Width(30));

                // Controller dropdown
                int curIdx = Array.IndexOf(controllerValues, entry.Controller);
                EditorGUI.BeginChangeCheck();
                int nextIdx = EditorGUILayout.Popup(Mathf.Max(curIdx, 0), controllerLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    entry.Controller = (Controller)controllerValues.GetValue(nextIdx);
                    _isDirty = true;
                }

                // ── Exclusion mutuelle : Identifier XOR ForceSteamId ──
                // Si l'un est rempli, l'autre est grisé (disabled) avec un tooltip.
                // Les deux peuvent rester vides/-1 (= aucun override).
                bool hasIdentifier   = !string.IsNullOrEmpty(entry.Identifier);
                // Force Steam ne compte (pour l'exclusion) que sur un Tracker.
                bool hasForceSteamId = isTracker && entry.ForceSteamId >= 0;

                // Identifier textfield (MAC MQTT ou serial de tracking) — disabled si ForceSteamId set
                bool identifierDisabled = hasForceSteamId && !hasIdentifier;
                GUI.enabled = !identifierDisabled;
                EditorGUI.BeginChangeCheck();
                string identifierPlaceholder = identifierDisabled
                    ? "(disabled — Force Steam set)"
                    : (entry.Identifier ?? "");
                string nextIdentifier = EditorGUILayout.TextField(identifierPlaceholder);
                if (EditorGUI.EndChangeCheck() && !identifierDisabled)
                {
                    entry.Identifier = nextIdentifier;
                    _isDirty = true;
                }
                GUI.enabled = true;

                // Séparateur visuel "OR" entre les deux colonnes exclusives
                var orStyle = new GUIStyle(badgeStyle)
                {
                    fontSize = 8,
                    alignment = TextAnchor.MiddleCenter,
                };
                orStyle.normal.textColor  = colTextMuted;
                orStyle.normal.background = MakeRoundedTex(32, 32, new Color(1f, 1f, 1f, 0.04f), 4);
                GUILayout.Label(new GUIContent("OR",
                    "Identifier and Force Steam are mutually exclusive — pick one, or leave both empty for auto."),
                    orStyle, GUILayout.Width(24));

                // ForceSteamId : uniquement pour les Trackers (60). Sinon champ "N/A" grisé.
                if (isTracker)
                {
                    // disabled si Identifier est set (exclusion mutuelle)
                    bool forceSteamDisabled = hasIdentifier && !hasForceSteamId;
                    GUI.enabled = !forceSteamDisabled;
                    EditorGUI.BeginChangeCheck();
                    int nextForceSteamId = EditorGUILayout.IntField(entry.ForceSteamId, GUILayout.Width(70));
                    if (EditorGUI.EndChangeCheck() && !forceSteamDisabled)
                    {
                        entry.ForceSteamId = nextForceSteamId;
                        _isDirty = true;
                    }
                    GUI.enabled = true;
                }
                else
                {
                    // Visuel uniquement : la valeur ForceSteamId de l'entrée n'est pas modifiée.
                    var naStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
                    naStyle.normal.textColor = colTextMuted;
                    GUILayout.Label(new GUIContent("—",
                        "Force Steam n'est disponible que pour les contrôleurs Tracker (60)."),
                        naStyle, GUILayout.Width(70));
                }

                // ── Parent (LinkParent via Identifier) ──
                int parentSel = 0;
                if (!string.IsNullOrEmpty(entry.LinkParent))
                {
                    int found = parentIds.IndexOf(entry.LinkParent);
                    parentSel = found >= 0 ? found : 0;
                }
                EditorGUI.BeginChangeCheck();
                int nextParentSel = EditorGUILayout.Popup(parentSel, parentLabelsArr, GUILayout.Width(120));
                if (EditorGUI.EndChangeCheck())
                {
                    string chosen = parentIds[Mathf.Clamp(nextParentSel, 0, parentIds.Count - 1)];
                    // Refuse de se pointer soi-même ou de créer un cycle.
                    bool isSelf = !string.IsNullOrEmpty(chosen) && chosen == entry.Identifier;
                    if (!isSelf && !WouldCycle(i, chosen))
                    {
                        entry.LinkParent = chosen;
                        _isDirty = true;
                    }
                    else
                    {
                        Debug.LogWarning("[GlobalConfig] Parent refusé (soi-même ou cycle détecté).");
                    }
                }

                // ── IsDefault : toggle exclusif (un seul par liste). Un enfant ne peut PAS être default. ──
                GUILayout.Space(12);
                if (hasParent && entry.IsDefault)
                {
                    entry.IsDefault = false; // un enfant perd son statut de default
                    _isDirty = true;
                }
                using (new EditorGUI.DisabledScope(hasParent))
                {
                    EditorGUI.BeginChangeCheck();
                    bool nextDefault = EditorGUILayout.Toggle(entry.IsDefault, GUILayout.Width(36));
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (nextDefault)
                        {
                            // On coche celui-ci et on décoche tous les autres.
                            for (int j = 0; j < list.Count; j++)
                                if (list[j] != null) list[j].IsDefault = (j == i);
                        }
                        else
                        {
                            entry.IsDefault = false; // autorise 0 défaut
                        }
                        _isDirty = true;
                    }
                }

                // Remove button
                var removeStyle = new GUIStyle(buttonStyle)
                {
                    fontSize = 10,
                    padding  = new RectOffset(4, 4, 3, 3),
                };
                removeStyle.normal.background = MakeRoundedTex(32, 32, colErrorDim, 4);
                removeStyle.normal.textColor  = colError;
                removeStyle.hover.textColor   = Color.white;
                if (GUILayout.Button(new GUIContent("✕", "Remove this weapon binding from the list."),
                    removeStyle, GUILayout.Width(24), GUILayout.Height(20)))
                    toRemove = i;

                EditorGUILayout.EndHorizontal();

                // Bordure bleue (encadré) autour de la ligne d'un Tracker (60), volontairement translucide.
                if (isTracker && Event.current.type == EventType.Repaint)
                {
                    Color border = new Color(colTrackerBlue.r, colTrackerBlue.g, colTrackerBlue.b, 0.40f);
                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, rowRect.width, 1f), border);
                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1f, rowRect.width, 1f), border);
                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 1f, rowRect.height), border);
                    EditorGUI.DrawRect(new Rect(rowRect.xMax - 1f, rowRect.y, 1f, rowRect.height), border);
                }

                EditorGUILayout.Space(2);
            }

            if (toRemove >= 0)
            {
                list.RemoveAt(toRemove);
                _isDirty = true;
            }

            // ── Bouton "+" pour ajouter une arme ──
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(150);

            var addStyle = new GUIStyle(buttonStyle)
            {
                fontSize = 10,
                padding  = new RectOffset(8, 8, 4, 4),
            };
            addStyle.normal.background = MakeRoundedTex(32, 32, colAccentDim, 4);
            addStyle.normal.textColor  = colAccent;
            addStyle.hover.textColor   = Color.white;
            addStyle.active.background = texAccentSolid;

            if (GUILayout.Button(new GUIContent("+ ADD DEVICE", "Append a new empty device binding to the list."),
                addStyle, GUILayout.Height(22)))
            {
                list.Add(new WeaponBinding());
                _isDirty = true;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.EndVertical();

            // Encadrement rouge léger autour de tout le bloc Devices.
            if (Event.current.type == EventType.Repaint)
            {
                Color rb = new Color(1f, 0.38f, 0.38f, 0.30f);
                EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.y, blockRect.width, 1f), rb);
                EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.yMax - 1f, blockRect.width, 1f), rb);
                EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.y, 1f, blockRect.height), rb);
                EditorGUI.DrawRect(new Rect(blockRect.xMax - 1f, blockRect.y, 1f, blockRect.height), rb);
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
                GUILayout.Label(new GUIContent($"[{kvp.Value.Type}]",
                    "JSON token type — string, int, float or bool."), badgeStyle, GUILayout.Width(60));
                GUILayout.Label(new GUIContent(kvp.Key,
                    "Extra JSON key not present on the GlobalConfig class. Will be written back as-is on save."), fieldLabelStyle, GUILayout.Width(110));

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
                if (GUILayout.Button(new GUIContent("✕", "Remove this extra field from the saved JSON."),
                    removeStyle, GUILayout.Width(24), GUILayout.Height(22)))
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
            GUILayout.Label(new GUIContent("Name",
                "JSON key for the new field. Must not collide with an existing known field or extra field."),
                fieldLabelStyle, GUILayout.Width(45));
            _newKey     = EditorGUILayout.TextField(_newKey);
            _newTypeIdx = EditorGUILayout.Popup(_newTypeIdx, TypeLabels, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Value", "Initial value for the new field."),
                fieldLabelStyle, GUILayout.Width(45));
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
            if (GUILayout.Button(new GUIContent("+ ADD FIELD",
                "Add the field to the in-memory extras. Click SAVE afterwards to persist it to disk."),
                addStyle, GUILayout.Height(30)))
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

            if (GUILayout.Button(new GUIContent("REFRESH",
                "Reload GlobalConfig.json from disk. Discards any unsaved changes in this window."),
                buttonStyle, GUILayout.Height(34), GUILayout.MinWidth(110)))
                Refresh();

            GUILayout.FlexibleSpace();

            Color   saveColor = _isDirty ? colWarn : colAccent;
            var     saveStyle = new GUIStyle(buttonStyle);
            saveStyle.normal.background = MakeRoundedTex(32, 32, new Color(saveColor.r, saveColor.g, saveColor.b, 0.2f), 5);
            saveStyle.normal.textColor  = saveColor;
            saveStyle.hover.textColor   = Color.white;
            saveStyle.active.background = _isDirty ? texWarnSolid : texAccentSolid;

            string saveLabel = _isDirty ? "  SAVE  ●" : "  SAVE";
            if (GUILayout.Button(new GUIContent(saveLabel,
                "Write current values (known fields + extra fields) to GlobalConfig.json on disk."),
                saveStyle, GUILayout.Height(34), GUILayout.MinWidth(150)))
                SaveToJson();

            GUILayout.Space(16);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            var pathStyle = new GUIStyle(footerStyle) { wordWrap = true };
            GUILayout.Label(new GUIContent(_savePath ?? "—",
                "Absolute path of the JSON file being read/written."), pathStyle);

            EditorGUILayout.Space(2);
            GUILayout.Label("Varonia Back Office  ·  GlobalConfig", footerStyle);
        }

        // ─── Save ─────────────────────────────────────────────────────────────────

        private void SaveToJson()
        {
            if (_configObj == null)
            {
                Debug.LogError("[Config Editor] Rien à sauvegarder.");
                return;
            }

            string dir = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_savePath, BuildCurrentJObject().ToString(Formatting.Indented));
            _isDirty = false;
            Debug.Log($"[Config Editor] Sauvegardé → {_savePath}");
        }

        /// <summary>Construit le JObject de la config en mémoire (known fields + extra fields).</summary>
        private JObject BuildCurrentJObject()
        {
            var jObj = new JObject();
            if (_knownFields != null)
                foreach (var field in _knownFields)
                {
                    var v = field.GetValue(_configObj);
                    jObj[field.Name] = v != null ? JToken.FromObject(v) : JValue.CreateNull();
                }
            foreach (var kvp in _extraFields)
                jObj[kvp.Key] = kvp.Value;
            return jObj;
        }

        // ─── Profils (debug : plusieurs GlobalConfig "cachés", switch rapide) ───────

        /// <summary>Dossier des profils, à côté du GlobalConfig.json actif.</summary>
        private string ProfilesDir()
        {
            string dir = Path.GetDirectoryName(_savePath);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "Profiles");
        }

        private string[] ListProfiles()
        {
            string pdir = ProfilesDir();
            if (string.IsNullOrEmpty(pdir) || !Directory.Exists(pdir)) return System.Array.Empty<string>();
            var files = Directory.GetFiles(pdir, "*.json");
            var names = new string[files.Length];
            for (int i = 0; i < files.Length; i++) names[i] = Path.GetFileNameWithoutExtension(files[i]);
            System.Array.Sort(names);
            return names;
        }

        private static string SanitizeProfileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        /// <summary>Sauve la config en mémoire comme profil nommé (n'altère pas le GlobalConfig.json actif).</summary>
        private void SaveProfile(string rawName)
        {
            if (_configObj == null) return;
            string name = SanitizeProfileName(rawName);
            if (name == null) { Debug.LogWarning("[Config Editor] Nom de profil invalide."); return; }

            string pdir = ProfilesDir();
            if (!Directory.Exists(pdir)) Directory.CreateDirectory(pdir);

            string path = Path.Combine(pdir, name + ".json");
            File.WriteAllText(path, BuildCurrentJObject().ToString(Formatting.Indented));
            AssetDatabase.Refresh();
            Debug.Log($"[Config Editor] Profil sauvegardé → {path}");
        }

        /// <summary>Bascule : copie le profil dans le GlobalConfig.json actif puis recharge.</summary>
        private void SwitchToProfile(string name)
        {
            string pdir = ProfilesDir();
            string path = Path.Combine(pdir, name + ".json");
            if (!File.Exists(path)) { Debug.LogWarning($"[Config Editor] Profil introuvable : {path}"); return; }

            if (!EditorUtility.DisplayDialog("Switch GlobalConfig",
                $"Remplacer le GlobalConfig actif par le profil « {name} » ?\n\n" +
                "Les modifications non sauvegardées de la config actuelle seront perdues.",
                "Switch", "Annuler"))
                return;

            string activeDir = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(activeDir) && !Directory.Exists(activeDir))
                Directory.CreateDirectory(activeDir);

            File.Copy(path, _savePath, true);
            Refresh();
            Debug.Log($"[Config Editor] Profil « {name} » activé.");
            GUIUtility.ExitGUI(); // la mise en page a changé (Refresh) → on termine proprement la frame IMGUI
        }

        private void DeleteProfile(string name)
        {
            string pdir = ProfilesDir();
            string path = Path.Combine(pdir, name + ".json");
            if (!File.Exists(path)) return;

            if (!EditorUtility.DisplayDialog("Supprimer le profil",
                $"Supprimer définitivement le profil « {name} » ?", "Supprimer", "Annuler"))
                return;

            File.Delete(path);
            string meta = path + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
            AssetDatabase.Refresh();
            Debug.Log($"[Config Editor] Profil « {name} » supprimé.");
        }

        private void DrawProfiles()
        {
            var profiles = ListProfiles();

            if (profiles.Length == 0)
            {
                GUILayout.Label("Aucun profil. Sauvegardez la config courante ci-dessous.", readOnlyStyle);
            }
            else
            {
                foreach (var name in profiles)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(name, fieldLabelStyle, GUILayout.MinWidth(80));
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(new GUIContent("Switch",
                        "Remplace le GlobalConfig actif par ce profil et recharge."),
                        buttonStyle, GUILayout.Width(70), GUILayout.Height(22)))
                        SwitchToProfile(name);

                    if (GUILayout.Button(new GUIContent("Overwrite",
                        "Écrase ce profil avec la config actuellement affichée."),
                        buttonStyle, GUILayout.Width(82), GUILayout.Height(22)))
                        SaveProfile(name);

                    var del = new GUIStyle(buttonStyle);
                    del.normal.textColor = colError;
                    del.hover.textColor  = Color.white;
                    if (GUILayout.Button(new GUIContent("✕", "Supprimer ce profil."),
                        del, GUILayout.Width(26), GUILayout.Height(22)))
                        DeleteProfile(name);

                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(2);
                }
            }

            EditorGUILayout.Space(6);
            DrawDivider();
            EditorGUILayout.Space(6);

            // Sauver la config courante comme nouveau profil.
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Save current as",
                "Sauve la config affichée comme nouveau profil (n'altère pas le GlobalConfig actif)."),
                fieldLabelStyle, GUILayout.Width(110));
            _newProfileName = EditorGUILayout.TextField(_newProfileName);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newProfileName)))
            {
                if (GUILayout.Button("Save", buttonStyle, GUILayout.Width(70), GUILayout.Height(20)))
                {
                    string n = _newProfileName;
                    _newProfileName = "";
                    GUI.FocusControl(null);
                    SaveProfile(n);
                }
            }
            EditorGUILayout.EndHorizontal();

            string pdir = ProfilesDir();
            if (!string.IsNullOrEmpty(pdir))
            {
                EditorGUILayout.Space(2);
                var pathStyle = new GUIStyle(footerStyle) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
                GUILayout.Label(new GUIContent("→ " + pdir, "Dossier de stockage des profils."), pathStyle);
            }
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