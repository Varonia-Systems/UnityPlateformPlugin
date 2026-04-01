using System;
using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice
{
    [CustomEditor(typeof(AutoSizing))]
    public class AutoSizingEditor : Editor
    {
        private AutoSizing _script;

        // ── Style cache ───────────────────────────────────────────────────────────
        private bool _stylesBuilt;
        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _footerStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _tagStyle;
        private GUIStyle _fieldLabelStyle;
        private GUIStyle _readOnlyStyle;
        private GUIStyle _valueStyle;

        // ── Colors ────────────────────────────────────────────────────────────────
        private static readonly Color colBg          = new Color(0.11f, 0.11f, 0.14f, 1f);
        private static readonly Color colCard        = new Color(0.15f, 0.15f, 0.19f, 1f);
        private static readonly Color colAccent      = new Color(0.30f, 0.85f, 0.65f, 1f);
        private static readonly Color colAccentDim   = new Color(0.30f, 0.85f, 0.65f, 0.15f);
        private static readonly Color colWarn        = new Color(1f,    0.75f, 0.30f, 1f);
        private static readonly Color colWarnDim     = new Color(1f,    0.75f, 0.30f, 0.12f);
        private static readonly Color colError       = new Color(1f,    0.40f, 0.40f, 1f);
        private static readonly Color colErrorDim    = new Color(1f,    0.40f, 0.40f, 0.15f);
        private static readonly Color colTextPrimary = new Color(0.92f, 0.92f, 0.95f, 1f);
        private static readonly Color colTextSecond  = new Color(0.55f, 0.55f, 0.62f, 1f);
        private static readonly Color colTextMuted   = new Color(0.40f, 0.40f, 0.47f, 1f);
        private static readonly Color colDivider     = new Color(1f,    1f,    1f,    0.06f);
        private static readonly Color colBtnNormal   = new Color(0.22f, 0.22f, 0.28f, 1f);
        private static readonly Color colBtnHover    = new Color(0.28f, 0.28f, 0.35f, 1f);

        // ── Textures ──────────────────────────────────────────────────────────────
        private Texture2D _texBtn, _texBtnHover, _texAccentSolid, _texWarnSolid, _texCard;
        private Texture2D _texAccentDim, _texWarnDim, _texErrorDim, _texDivider;
        private Texture2D _texPillCalibrating, _texPillReady, _texPillOffline;

        private void OnEnable()
        {
            _script = (AutoSizing)target;
            _stylesBuilt = false;
        }

        private static Texture2D MakeTex(Color col)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, col);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        private static Texture2D MakeRoundedTex(int w, int h, Color col, int radius)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool inside = true;
                    if      (x < radius      && y < radius)          inside = new Vector2(x - radius,            y - radius).magnitude            <= radius;
                    else if (x >= w - radius && y < radius)          inside = new Vector2(x - (w - radius - 1),  y - radius).magnitude            <= radius;
                    else if (x < radius      && y >= h - radius)     inside = new Vector2(x - radius,            y - (h - radius - 1)).magnitude  <= radius;
                    else if (x >= w - radius && y >= h - radius)     inside = new Vector2(x - (w - radius - 1),  y - (h - radius - 1)).magnitude  <= radius;
                    t.SetPixel(x, y, inside ? col : clear);
                }
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            _texCard        = MakeRoundedTex(32, 32, colCard, 6);
            _texAccentDim   = MakeRoundedTex(32, 32, colAccentDim, 6);
            _texWarnDim     = MakeRoundedTex(32, 32, colWarnDim, 6);
            _texErrorDim    = MakeRoundedTex(32, 32, colErrorDim, 6);
            _texDivider     = MakeTex(colDivider);
            _texBtn         = MakeRoundedTex(32, 32, colBtnNormal, 5);
            _texBtnHover    = MakeRoundedTex(32, 32, colBtnHover, 5);
            _texAccentSolid = MakeRoundedTex(32, 32, colAccent, 5);
            _texWarnSolid   = MakeRoundedTex(32, 32, colWarn, 5);

            _texPillCalibrating = MakeRoundedTex(32, 32, new Color(colWarn.r, colWarn.g, colWarn.b, 0.15f), 6);
            _texPillReady       = MakeRoundedTex(32, 32, new Color(colAccent.r, colAccent.g, colAccent.b, 0.15f), 6);
            _texPillOffline     = MakeRoundedTex(32, 32, new Color(colTextMuted.r, colTextMuted.g, colTextMuted.b, 0.15f), 6);

            _headerStyle = new GUIStyle
            {
                fontSize  = 16,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = colTextPrimary },
                padding   = new RectOffset(0, 0, 0, 0),
                margin    = new RectOffset(0, 0, 0, 0),
            };

            _tagStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = colAccent, background = _texAccentDim },
                padding   = new RectOffset(8, 8, 3, 3),
                margin    = new RectOffset(0, 4, 0, 0),
                border    = new RectOffset(6, 6, 6, 6),
            };

            _sectionStyle = new GUIStyle
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = colTextMuted },
                padding   = new RectOffset(0, 0, 6, 2),
                margin    = new RectOffset(0, 0, 4, 0),
            };

            _footerStyle = new GUIStyle
            {
                fontSize  = 9,
                normal    = { textColor = colTextMuted },
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(0, 0, 4, 4),
            };

            _buttonStyle = new GUIStyle
            {
                fontSize  = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = colTextPrimary, background = _texBtn },
                hover     = { textColor = Color.white,   background = _texBtnHover },
                active    = { textColor = Color.white,   background = _texAccentSolid },
                padding   = new RectOffset(12, 12, 6, 6),
                margin    = new RectOffset(2, 2, 2, 2),
                border    = new RectOffset(5, 5, 5, 5),
            };

            _fieldLabelStyle = new GUIStyle
            {
                fontSize = 11,
                normal   = { textColor = colTextSecond },
                padding  = new RectOffset(0, 0, 3, 3),
            };

            _readOnlyStyle = new GUIStyle
            {
                fontSize  = 11,
                fontStyle = FontStyle.Italic,
                normal    = { textColor = colTextMuted },
                padding   = new RectOffset(4, 0, 3, 3),
            };

            _valueStyle = new GUIStyle(_headerStyle)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = colAccent }
            };
        }

        public override void OnInspectorGUI()
        {
            BuildStyles();
            serializedObject.Update();

            var bgRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(bgRect, colBg);

            GUILayout.Space(10);

            // ── Title bar ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.Label("AUTO SIZING", _headerStyle);
            GUILayout.FlexibleSpace();

            bool isPlaying = Application.isPlaying;
            Color pillCol = _script.IsCalibrating ? colWarn : (isPlaying ? colAccent : colTextMuted);
            string pillTxt = _script.IsCalibrating ? "  CALIBRATING  " : (isPlaying ? "  READY  " : "  OFFLINE  ");
            Texture2D pillBg = _script.IsCalibrating ? _texPillCalibrating : (isPlaying ? _texPillReady : _texPillOffline);

            var pillStyle = new GUIStyle(_tagStyle);
            pillStyle.normal.textColor = pillCol;
            pillStyle.normal.background = pillBg;
            
            // Calculer la taille du label pour éviter qu'il ne s'étende trop
            Vector2 pillSize = pillStyle.CalcSize(new GUIContent(pillTxt));
            GUILayout.Label(pillTxt, pillStyle, GUILayout.Width(pillSize.x), GUILayout.Height(pillSize.y));

            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12);

            // ── Config card ──
            DrawCard(() =>
            {
                DrawSectionLabel("CONFIGURATION");
                DrawDivider();
                GUILayout.Space(6);

                DrawField("Target Samples", "targetSamples");
                DrawField("Max Look Angle", "maxLookAngle");
                // Le champ autoStartOnInputIndex0 n'existe pas dans le script, je le retire

            }, colTextMuted);

            GUILayout.Space(8);

            // ── Safety card ──
            DrawCard(() =>
            {
                DrawSectionLabel("SÉCURITÉ ANTI-TROLL");
                DrawDivider();
                GUILayout.Space(6);

                DrawField("Max Vertical Speed", "maxVerticalSpeed");

            }, colError);

            GUILayout.Space(8);

            // ── Calibration card ──
            DrawCard(() =>
            {
                DrawSectionLabel("CALIBRATION STATUS");
                DrawDivider();
                GUILayout.Space(10);

                if (_script.IsCalibrating)
                {
                    float progress = (float)_script.CurrentSamples / _script.TargetSamples;
                    Rect r = GUILayoutUtility.GetRect(18, 18);
                    EditorGUI.DrawRect(r, colBg);
                    EditorGUI.DrawRect(new Rect(r.x, r.y, r.width * progress, r.height), colAccent);
                    
                    GUILayout.Space(4);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Progress", _fieldLabelStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{_script.CurrentSamples} / {_script.TargetSamples}", _readOnlyStyle);
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label("Current Retained Size", _fieldLabelStyle);
                    string sizeText = _script.CurrentRetainedSize > 0.01f ? $"{_script.CurrentRetainedSize:F2} m" : "-- m";
                    GUILayout.Label(sizeText, _valueStyle, GUILayout.Height(35));
                }

                GUILayout.Space(12);

                using (new EditorGUI.DisabledScope(!isPlaying || _script.IsCalibrating))
                {
                    if (GUILayout.Button("START CALIBRATION", _buttonStyle, GUILayout.Height(30)))
                    {
                        _script.StartCalibration();
                    }
                }

                if (!isPlaying)
                {
                    GUILayout.Space(4);
                    GUILayout.Label("Calibration requires Play Mode.", _readOnlyStyle);
                }

            }, isPlaying ? colAccent : colTextMuted);

            GUILayout.Space(12);
            GUILayout.Label("Varonia Back Office  ·  AutoSizing", _footerStyle);
            GUILayout.Space(8);

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
            if (_script.IsCalibrating) Repaint();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private void DrawCard(Action content, Color accentColor)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            
            // On utilise un style personnalisé avec une marge interne via padding pour éviter de manipuler cardRect manuellement
            GUIStyle cardStyle = new GUIStyle();
            cardStyle.normal.background = _texCard;
            cardStyle.padding = new RectOffset(10, 10, 12, 10);
            cardStyle.margin = new RectOffset(0, 0, 4, 4);
            cardStyle.border = new RectOffset(6, 6, 6, 6);

            EditorGUILayout.BeginVertical(cardStyle);

            // Reserve a rect for the accent bar to avoid "cannot call GetLast immediately after beginning a group" error
            Rect accentRect = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(accentRect, accentColor);
            }

            content();

            EditorGUILayout.EndVertical();
            
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSectionLabel(string text) => GUILayout.Label(text, _sectionStyle);

        private void DrawDivider()
        {
            GUILayout.Space(4);
            Rect r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            r.x += 4;
            r.width -= 8;
            EditorGUI.DrawRect(r, colDivider);
            GUILayout.Space(4);
        }

        private void DrawField(string label, string propName)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, _fieldLabelStyle, GUILayout.Width(150));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(propName), GUIContent.none);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
        }
    }
}
