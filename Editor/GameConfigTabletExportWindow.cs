using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Construit l'export "Tablet" : pour chaque champ marqué tablet, demande id / label /
    /// description / type puis copie un tableau JSON de descripteurs d'UI.
    ///   • bool   → checkbox      • int/float → numeric      • string → text
    ///   • enum   → radio (un choix par valeur, value = nom de l'enum)
    /// defaultvalue = valeur actuelle du champ. Les métadonnées sont mémorisées dans le projet.
    /// </summary>
    public class GameConfigTabletExportWindow : EditorWindow
    {
        static readonly Color colBg     = new Color(0.11f, 0.11f, 0.14f, 1f);
        static readonly Color colCard   = new Color(0.15f, 0.15f, 0.19f, 1f);
        static readonly Color colAccent = new Color(1f, 0.45f, 0.45f, 1f); // rouge tablet
        static readonly Color colText   = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color colMuted  = new Color(0.55f, 0.55f, 0.62f, 1f);

        static readonly string[] Types = { "checkbox", "numeric", "text", "radio" };
        const int T_CHECKBOX = 0, T_NUMERIC = 1, T_TEXT = 2, T_RADIO = 3;

        private class Row
        {
            public string key;        // binding
            public JToken value;      // valeur actuelle → defaultvalue (non-radio)
            public string id, label, description;
            public int    typeIdx;

            // Radio (enum)
            public bool     isEnum;
            public string[] enumNames;
            public string   enumCurrent;
            public string[] choiceLabels; // parallèle à enumNames (éditable)
        }

        private List<Row> _rows = new List<Row>();
        private Vector2 _scroll;

        private static string MetaPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ProjectSettings", "VBO_GameConfigTabletMeta.json"));

        public static void Show(JObject tabletSubset, Dictionary<string, (string[] names, string current)> enums)
        {
            var w = GetWindow<GameConfigTabletExportWindow>(true, "Tablet export", true);
            w.Build(tabletSubset ?? new JObject(), enums ?? new Dictionary<string, (string[], string)>());
            w.minSize = new Vector2(560, 580);
            w.Show();
        }

        private void Build(JObject subset, Dictionary<string, (string[] names, string current)> enums)
        {
            var meta = LoadMeta();
            _rows = new List<Row>();

            foreach (var p in subset.Properties())
            {
                bool isEnum     = enums.TryGetValue(p.Name, out var en);
                bool binaryEnum = isEnum && en.names != null && en.names.Length == 2;

                int    typeIdx;
                JToken value = p.Value;

                if (binaryEnum)
                {
                    // Enum à 2 valeurs → checkbox (coché = 2ᵉ valeur de l'enum).
                    typeIdx = T_CHECKBOX;
                    value   = new JValue(en.current == en.names[1]);
                    isEnum  = false;
                }
                else if (isEnum)
                {
                    typeIdx = T_RADIO;
                }
                else
                {
                    typeIdx = InferTypeIdx(p.Value);
                    if (typeIdx < 0) continue; // type non supporté → ignoré
                }

                meta.TryGetValue(p.Name, out var m);

                var row = new Row
                {
                    key         = p.Name,
                    value       = value,
                    id          = !string.IsNullOrEmpty(m.id)    ? m.id    : Slug(p.Name),
                    label       = !string.IsNullOrEmpty(m.label) ? m.label : p.Name,
                    description = m.description ?? "",
                    typeIdx     = isEnum ? T_RADIO : (m.typeIdx >= 0 ? m.typeIdx : typeIdx),
                    isEnum      = isEnum,
                };

                if (isEnum)
                {
                    row.enumNames    = en.names ?? new string[0];
                    row.enumCurrent  = en.current ?? "";
                    row.choiceLabels = new string[row.enumNames.Length];
                    for (int i = 0; i < row.enumNames.Length; i++)
                        row.choiceLabels[i] = (m.choiceLabels != null && m.choiceLabels.TryGetValue(row.enumNames[i], out var cl) && !string.IsNullOrEmpty(cl))
                            ? cl : row.enumNames[i];
                }

                _rows.Add(row);
            }
        }

        private static int InferTypeIdx(JToken t)
        {
            switch (t.Type)
            {
                case JTokenType.Boolean: return T_CHECKBOX;
                case JTokenType.Integer:
                case JTokenType.Float:   return T_NUMERIC;
                case JTokenType.String:  return T_TEXT;
                default:                 return -1;
            }
        }

        private static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colBg);

            GUILayout.Space(10);
            var header = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = colAccent } };
            EditorGUILayout.BeginHorizontal(); GUILayout.Space(12);
            GUILayout.Label("EXPORT TABLET  —  renseigne id / label / description", header);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);

            if (_rows.Count == 0)
            {
                EditorGUILayout.BeginHorizontal(); GUILayout.Space(12);
                GUILayout.Label("Aucun champ Tablet supporté (checkbox/numeric/text/radio).",
                    new GUIStyle(EditorStyles.label) { normal = { textColor = colMuted } });
                EditorGUILayout.EndHorizontal();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var r in _rows) DrawRow(r);
            EditorGUILayout.EndScrollView();

            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal(); GUILayout.Space(12);
            GUILayout.FlexibleSpace();
            var copyStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, fixedHeight = 36, normal = { textColor = colAccent } };
            if (GUILayout.Button($"📋  COPIER LE JSON TABLET  ({_rows.Count})", copyStyle))
                CopyTabletJson();
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);
        }

        private void DrawRow(Row r)
        {
            EditorGUILayout.BeginHorizontal(); GUILayout.Space(12);
            var rect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(new Rect(rect.x - 4, rect.y - 4, rect.width + 8, rect.height + 8), colCard);
            EditorGUI.DrawRect(new Rect(rect.x - 4, rect.y - 4, rect.width + 8, 2), colAccent);
            GUILayout.Space(4);

            var bind = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = colText } };
            string head = r.isEnum
                ? $"binding : {r.key}   ·   enum   ·   default = {r.enumCurrent}"
                : $"binding : {r.key}   ·   default = {PreviewValue(r.value)}";
            GUILayout.Label(head, bind);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("type", GUILayout.Width(80));
            if (r.isEnum)
                GUILayout.Label("radio  (auto enum)", new GUIStyle(EditorStyles.label) { normal = { textColor = colMuted } });
            else
                r.typeIdx = EditorGUILayout.Popup(r.typeIdx, new[] { Types[0], Types[1], Types[2] }, GUILayout.Width(110));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("id", GUILayout.Width(80));
            r.id = EditorGUILayout.TextField(r.id);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("label", GUILayout.Width(80));
            r.label = EditorGUILayout.TextField(r.label);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("description", "Optionnel — omis si vide. Le </br> est autorisé."), GUILayout.Width(80));
            r.description = EditorGUILayout.TextArea(r.description, GUILayout.MinHeight(34));
            EditorGUILayout.EndHorizontal();

            // ── Choix du radio (enum) : value figée, label éditable ──
            if (r.isEnum && r.enumNames != null)
            {
                GUILayout.Space(2);
                GUILayout.Label("choix (value → label)", new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = colMuted } });
                for (int i = 0; i < r.enumNames.Length; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(12);
                    string mark = r.enumNames[i] == r.enumCurrent ? "●" : "○";
                    GUILayout.Label($"{mark} {r.enumNames[i]}", GUILayout.Width(150));
                    GUILayout.Label("→", GUILayout.Width(16));
                    r.choiceLabels[i] = EditorGUILayout.TextField(r.choiceLabels[i]);
                    EditorGUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);
        }

        private static string PreviewValue(JToken t)
        {
            if (t == null) return "null";
            string s = t.ToString(Formatting.None).Replace("\n", " ");
            return s.Length > 40 ? s.Substring(0, 40) + "…" : s;
        }

        private void CopyTabletJson()
        {
            var arr = new JArray();
            foreach (var r in _rows)
            {
                var o = new JObject { ["id"] = r.id };

                if (r.isEnum)
                {
                    o["type"]    = "radio";
                    o["label"]   = r.label;
                    o["binding"] = r.key;
                    if (!string.IsNullOrEmpty(r.description)) o["description"] = r.description;

                    var choices = new JArray();
                    for (int i = 0; i < r.enumNames.Length; i++)
                        choices.Add(new JObject
                        {
                            ["id"]    = r.id + "_" + Slug(r.enumNames[i]),
                            ["label"] = r.choiceLabels[i],
                            ["value"] = r.enumNames[i],
                        });
                    o["choices"]      = choices;
                    o["defaultvalue"] = r.enumCurrent;
                }
                else
                {
                    o["type"]    = Types[r.typeIdx];
                    o["label"]   = r.label;
                    o["binding"] = r.key;
                    if (!string.IsNullOrEmpty(r.description)) o["description"] = r.description;
                    o["defaultvalue"] = r.value != null ? r.value.DeepClone() : JValue.CreateNull();
                }

                arr.Add(o);
            }

            EditorGUIUtility.systemCopyBuffer = arr.ToString(Formatting.Indented);
            SaveMeta();
            Debug.Log($"[GameConfig Tablet] {arr.Count} contrôle(s) copié(s) dans le presse-papier.");
            Close();
        }

        // ─── Persistance des métadonnées (projet) ───────────────────────────────────

        private struct Meta
        {
            public string id, label, description;
            public int    typeIdx;
            public Dictionary<string, string> choiceLabels;
        }

        private Dictionary<string, Meta> LoadMeta()
        {
            var d = new Dictionary<string, Meta>();
            try
            {
                if (File.Exists(MetaPath))
                {
                    var root = JObject.Parse(File.ReadAllText(MetaPath));
                    foreach (var prop in root.Properties())
                    {
                        if (!(prop.Value is JObject m)) continue;
                        var meta = new Meta
                        {
                            id          = m["id"]?.ToString(),
                            label       = m["label"]?.ToString(),
                            description = m["description"]?.ToString(),
                            typeIdx     = m["typeIdx"]?.ToObject<int>() ?? -1,
                        };
                        if (m["choiceLabels"] is JObject cl)
                        {
                            meta.choiceLabels = new Dictionary<string, string>();
                            foreach (var c in cl.Properties())
                                meta.choiceLabels[c.Name] = c.Value?.ToString();
                        }
                        d[prop.Name] = meta;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GameConfig Tablet] Lecture méta impossible : {e.Message}");
            }
            return d;
        }

        private void SaveMeta()
        {
            try
            {
                JObject root;
                try { root = File.Exists(MetaPath) ? JObject.Parse(File.ReadAllText(MetaPath)) : new JObject(); }
                catch { root = new JObject(); }

                foreach (var r in _rows)
                {
                    var o = new JObject
                    {
                        ["id"]          = r.id,
                        ["label"]       = r.label,
                        ["description"] = r.description,
                        ["typeIdx"]     = r.typeIdx,
                    };
                    if (r.isEnum && r.enumNames != null)
                    {
                        var cl = new JObject();
                        for (int i = 0; i < r.enumNames.Length; i++)
                            cl[r.enumNames[i]] = r.choiceLabels[i];
                        o["choiceLabels"] = cl;
                    }
                    root[r.key] = o;
                }

                File.WriteAllText(MetaPath, root.ToString(Formatting.Indented));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameConfig Tablet] Écriture méta impossible : {e.Message}");
            }
        }
    }
}
