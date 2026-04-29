using System;
using UnityEngine;
using UnityEditor;

namespace VaroniaBackOffice
{
    [CustomEditor(typeof(BackOfficeVaronia))]
    public class BackOfficeVaroniaEditor : Editor
    {
        // ── Logo ──────────────────────────────────────────────────────────────────
        private Texture2D _logo;
        private const float LogoSize = 200f;

        // ── Style cache ───────────────────────────────────────────────────────────
        static bool     stylesBuilt;
        static GUIStyle headerStyle;
        static GUIStyle sectionStyle;
        static GUIStyle footerStyle;
        static GUIStyle buttonStyle;
        static GUIStyle tagStyle;
        static GUIStyle fieldLabelStyle;
        static GUIStyle readOnlyStyle;

        // ── Colors ────────────────────────────────────────────────────────────────
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

        // ── Textures ──────────────────────────────────────────────────────────────
        static Texture2D texBtn, texBtnHover, texAccentSolid, texWarnSolid, texCard;
        static Texture2D texAccentDim, texWarnDim, texErrorDim, texDivider;

        // ─────────────────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            stylesBuilt = false;
           // string[] guids = AssetDatabase.FindAssets("VaroniaLogo t:Texture2D");
          //  if (guids.Length > 0)
            //    _logo = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

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

            headerStyle = new GUIStyle
            {
                fontSize  = 16,
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

            sectionStyle = new GUIStyle
            {
                fontSize  = 9,
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
                padding   = new RectOffset(0, 0, 4, 4),
            };

            buttonStyle = new GUIStyle
            {
                fontSize  = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = colTextPrimary, background = texBtn },
                hover     = { textColor = Color.white,   background = texBtnHover },
                active    = { textColor = Color.white,   background = texAccentSolid },
                padding   = new RectOffset(12, 12, 6, 6),
                margin    = new RectOffset(2, 2, 2, 2),
                border    = new RectOffset(5, 5, 5, 5),
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
                padding   = new RectOffset(4, 0, 3, 3),
            };
        }

        // ─── Inspector ────────────────────────────────────────────────────────────

        public override void OnInspectorGUI()
        {
            BuildStyles();
            serializedObject.Update();

            var script = (BackOfficeVaronia)target;

            // ── Dark background ──
            var bgRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(bgRect, colBg);

            GUILayout.Space(10);

            // ── Logo ──
            if (_logo != null)
            {
                float w = EditorGUIUtility.currentViewWidth - 24f;
                var r = GUILayoutUtility.GetRect(w, LogoSize);
                GUI.DrawTexture(r, _logo, ScaleMode.ScaleToFit);
                GUILayout.Space(6);
            }

            // ── Title bar ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.Label("BACK OFFICE", headerStyle);
            GUILayout.FlexibleSpace();

            // Status pill
            bool  isPlaying = Application.isPlaying;
            Color pillCol   = isPlaying ? colAccent : colTextMuted;
            string pillTxt  = isPlaying ? "  PLAYING  " : "  OFFLINE  ";
            var   pill      = new GUIStyle(tagStyle);
            pill.normal.textColor  = pillCol;
            pill.normal.background = MakeRoundedTex(32, 32, new Color(pillCol.r, pillCol.g, pillCol.b, 0.15f), 6);
            GUILayout.Label(pillTxt, pill);

            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12);

            // ── System Status card ──
            DrawCard(() =>
            {
                DrawSectionLabel("SYSTEM STATUS");
                DrawDivider();
                GUILayout.Space(6);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Is Game Started", fieldLabelStyle, GUILayout.Width(150));
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Toggle(script.IsStarted);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(2);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Soft State", fieldLabelStyle, GUILayout.Width(150));
                if (isPlaying && script.mqttClient != null)
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.EnumPopup(script.mqttClient.SoftState);
                }
                else
                {
                    GUILayout.Label("N/A (Offline)", readOnlyStyle);
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(2);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Main Camera", fieldLabelStyle, GUILayout.Width(150));
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(script.MainCamera, typeof(Camera), true);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(2);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Rig", fieldLabelStyle, GUILayout.Width(150));
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(script.Rig, typeof(Transform), true);
                EditorGUILayout.EndHorizontal();

            }, isPlaying ? colAccent : colTextMuted);

            GUILayout.Space(8);

            // ── JSON Configuration card ──
            DrawCard(() =>
            {
                DrawSectionLabel("JSON CONFIGURATION (LIVE)");
                DrawDivider();
                GUILayout.Space(6);

                if (script.config == null)
                {
                    GUILayout.Label("Config not loaded yet.", readOnlyStyle);
                }
                else
                {
                    DrawConfigFields(script.config);
                }

                if (script.extraFields != null && script.extraFields.Count > 0)
                {
                    GUILayout.Space(8);
                    GUILayout.Label("EXTRA / DYNAMIC FIELDS", sectionStyle);
                    DrawDivider();
                    GUILayout.Space(4);

                    foreach (var kvp in script.extraFields)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label(kvp.Key, fieldLabelStyle, GUILayout.Width(180));
                        GUILayout.Label(kvp.Value != null ? kvp.Value.ToString() : "null", readOnlyStyle);
                        EditorGUILayout.EndHorizontal();
                    }
                }

#if GAME_CONFIG
                if (script.gameConfig != null)
                {
                    GUILayout.Space(8);
                    GUILayout.Label("GAME CONFIG", sectionStyle);
                    DrawDivider();
                    GUILayout.Space(4);
                    DrawConfigFields(script.gameConfig);
                }
#endif

            }, colTextMuted);

            GUILayout.Space(8);

            // ── Start Events card ──
            DrawCard(() =>
            {
                DrawSectionLabel("START EVENTS");
                DrawDivider();
                GUILayout.Space(6);

                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnStartWithTuto"));
                GUILayout.Space(4);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnStartSkipTuto"));

            }, colAccent);

            GUILayout.Space(8);

            // ── Debug Controls card (Play Mode only) ──
            if (isPlaying)
            {
                DrawCard(() =>
                {
                    DrawSectionLabel("DEBUG CONTROLS");
                    DrawDivider();
                    GUILayout.Space(6);

                    // Game start
                    GUILayout.Label("Game Start", sectionStyle);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Start With Tuto", buttonStyle, GUILayout.Height(28)))
                        script.TriggerStartGame(false);
                    if (GUILayout.Button("Start Skip Tuto", buttonStyle, GUILayout.Height(28)))
                        script.TriggerStartGame(true);
                    EditorGUILayout.EndHorizontal();

                    // Soft state simulation
                    if (script.mqttClient != null)
                    {
                        GUILayout.Space(6);
                        GUILayout.Label("Force Soft State", sectionStyle);
                        EditorGUILayout.BeginHorizontal();

                        var warnBtn = new GUIStyle(buttonStyle);
                        warnBtn.normal.background = MakeRoundedTex(32, 32, colWarnDim, 5);
                        warnBtn.normal.textColor  = colWarn;
                        warnBtn.hover.textColor   = Color.white;
                        warnBtn.active.background = texWarnSolid;

                        if (GUILayout.Button("LAUNCHED", warnBtn, GUILayout.Height(28)))
                            script.mqttClient.SetSoftState(ESoftState.GAME_LAUNCHED);
                        if (GUILayout.Button("IN LOBBY",  warnBtn, GUILayout.Height(28)))
                            script.mqttClient.SetSoftState(ESoftState.GAME_INLOBBY);
                        if (GUILayout.Button("IN PARTY",  warnBtn, GUILayout.Height(28)))
                            script.mqttClient.SetSoftState(ESoftState.GAME_INPARTY);

                        EditorGUILayout.EndHorizontal();
                    }

                }, colWarn);

                Repaint();
            }
            else
            {
                DrawCard(() =>
                {
                    DrawSectionLabel("DEBUG CONTROLS");
                    DrawDivider();
                    GUILayout.Space(6);
                    GUILayout.Label("Debug buttons are only available in Play Mode.", readOnlyStyle);
                    GUILayout.Space(4);
                }, colTextMuted);
            }

            GUILayout.Space(8);
            GUILayout.Label("Varonia Back Office  ·  BackOfficeVaronia", footerStyle);
            GUILayout.Space(8);

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        void DrawCard(Action content, Color accentColor)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();

            Rect cardRect   = EditorGUILayout.BeginVertical();
            cardRect.x     -= 4;
            cardRect.width += 8;
            cardRect.y     -= 4;
            cardRect.height += 8;

            EditorGUI.DrawRect(cardRect, colCard);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 2), accentColor);

            GUILayout.Space(10);
            content();
            GUILayout.Space(10);

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        void DrawSectionLabel(string text) =>
            GUILayout.Label(text, sectionStyle);

        /// <summary>
        /// Affiche tous les champs publics d'instance d'un objet (typiquement GlobalConfig ou GameConfig)
        /// en read-only avec leur nom et leur valeur courante.
        /// </summary>
        void DrawConfigFields(object obj)
        {
            if (obj == null) return;

            var type = obj.GetType();
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var f in fields)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(f.Name, fieldLabelStyle, GUILayout.Width(180));

                object val;
                try { val = f.GetValue(obj); } catch { val = null; }
                string display = val != null ? val.ToString() : "null";

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.SelectableLabel(display, readOnlyStyle, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(1);
            }
        }

        void DrawDivider()
        {
            Rect r  = GUILayoutUtility.GetRect(1, 1);
            r.x    += 16;
            r.width -= 32;
            EditorGUI.DrawRect(r, colDivider);
        }

    }
}
