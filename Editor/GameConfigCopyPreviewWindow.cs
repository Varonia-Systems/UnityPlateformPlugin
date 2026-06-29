using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Fenêtre d'aperçu avant copie : liste les clés/valeurs d'un JSON candidat avec une case
    /// par clé (« ça je garde / ça je vire »). Copie dans le presse-papier la sélection cochée.
    /// </summary>
    public class GameConfigCopyPreviewWindow : EditorWindow
    {
        static readonly Color colBg     = new Color(0.11f, 0.11f, 0.14f, 1f);
        static readonly Color colAccent = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color colText   = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color colMuted  = new Color(0.55f, 0.55f, 0.62f, 1f);

        private JObject _source;
        private List<string> _keys = new List<string>();
        private readonly Dictionary<string, bool> _include = new Dictionary<string, bool>();
        private Vector2 _scroll;
        private string _titleText = "Copy";

        private static GUIStyle _header, _keyStyle, _valStyle;
        private static bool _styles;

        public static void Show(string title, JObject source)
        {
            var w = GetWindow<GameConfigCopyPreviewWindow>(true, "Copy preview", true);
            w._titleText = title;
            w._source = source ?? new JObject();
            w._keys = w._source.Properties().Select(p => p.Name).ToList();
            w._include.Clear();
            foreach (var k in w._keys) w._include[k] = true;
            w.minSize = new Vector2(440, 500);
            w.Show();
        }

        private void EnsureStyles()
        {
            if (_styles) return;
            _styles = true;
            _header  = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = colText } };
            _keyStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, normal = { textColor = colText } };
            _valStyle = new GUIStyle(EditorStyles.label)     { fontSize = 10, normal = { textColor = colMuted }, clipping = TextClipping.Clip };
        }

        private void OnGUI()
        {
            EnsureStyles();
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colBg);

            GUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.Label($"{_titleText}  —  coche ce que tu gardes", _header);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);

            // Tout / Rien
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            if (GUILayout.Button("Tout", EditorStyles.miniButton, GUILayout.Width(60))) SetAll(true);
            if (GUILayout.Button("Rien", EditorStyles.miniButton, GUILayout.Width(60))) SetAll(false);
            GUILayout.FlexibleSpace();
            int chec_ = _include.Count(kv => kv.Value);
            GUILayout.Label($"{chec_}/{_keys.Count} sélectionné(s)", _valStyle);
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            DrawDivider();
            GUILayout.Space(4);

            // Liste
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var key in _keys)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(12);
                _include[key] = EditorGUILayout.Toggle(_include[key], GUILayout.Width(18));
                GUILayout.Label(key, _keyStyle, GUILayout.Width(190));
                GUILayout.Label(Preview(_source[key]), _valStyle);
                GUILayout.Space(12);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
            EditorGUILayout.EndScrollView();

            GUILayout.Space(4);
            DrawDivider();
            GUILayout.Space(6);

            // Footer copie
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            using (new EditorGUI.DisabledScope(chec_ == 0))
            {
                var copyStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 12, fontStyle = FontStyle.Bold, fixedHeight = 36,
                    normal = { textColor = colAccent },
                };
                if (GUILayout.Button($"📋  COPIER LA SÉLECTION  ({chec_})", copyStyle))
                    CopySelection();
            }
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);
        }

        private void SetAll(bool v)
        {
            foreach (var k in _keys) _include[k] = v;
        }

        private static string Preview(JToken token)
        {
            if (token == null) return "null";
            string s = token.Type == JTokenType.Object || token.Type == JTokenType.Array
                ? token.ToString(Formatting.None)
                : token.ToString();
            s = s.Replace("\n", " ").Replace("\r", " ");
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }

        private void CopySelection()
        {
            var o = new JObject();
            foreach (var key in _keys)
                if (_include.TryGetValue(key, out bool on) && on)
                    o[key] = _source[key];

            EditorGUIUtility.systemCopyBuffer = o.ToString(Formatting.Indented);
            Debug.Log($"[GameConfig Copy] {o.Count} clé(s) copiée(s) dans le presse-papier.");
            Close();
        }

        private void DrawDivider()
        {
            Rect r = GUILayoutUtility.GetRect(1, 1);
            r.x += 12; r.width -= 24;
            EditorGUI.DrawRect(r, new Color(1, 1, 1, 0.06f));
        }
    }
}
