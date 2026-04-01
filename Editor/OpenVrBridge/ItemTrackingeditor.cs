using UnityEngine;
using UnityEditor;
using VaroniaBackOffice;

#if STEAMVR_ENABLED
using Valve.VR;
#endif

[CustomEditor(typeof(ItemTracking))]
public class ItemTrackingEditor : Editor
{
    SerializedProperty backend;
    SerializedProperty autoFind, applyToParent, trackerIndex, targetClass, useSerialFilter, targetSerial;
    SerializedProperty openXRDeviceType, viveTrackerRole;
    SerializedProperty positionOffset, rotationOffset;
    SerializedProperty found, foundIndex, foundModel, foundSerial, isTracking;

    static bool stylesBuilt = false;
    static GUIStyle headerStyle, tagStyle, sectionStyle, infoLabelStyle, infoValueStyle, footerStyle, buttonStyle, bigStatusStyle;

    static readonly Color colBg           = new Color(0.11f, 0.11f, 0.14f, 1f);
    static readonly Color colCard         = new Color(0.15f, 0.15f, 0.19f, 1f);
    static readonly Color colAccent       = new Color(0.30f, 0.85f, 0.65f, 1f);
    static readonly Color colAccentDim    = new Color(0.30f, 0.85f, 0.65f, 0.15f);
    static readonly Color colWarn         = new Color(1f, 0.75f, 0.30f, 1f);
    static readonly Color colTextPrimary  = new Color(0.92f, 0.92f, 0.95f, 1f);
    static readonly Color colTextSecond   = new Color(0.55f, 0.55f, 0.62f, 1f);
    static readonly Color colDivider      = new Color(1f, 1f, 1f, 0.06f);
    static readonly Color colOpenXR       = new Color(0.40f, 0.65f, 1.00f, 1f);

    static Texture2D texBg, texCard, texBtn, texBtnHover;

    void OnEnable()
    {
        backend         = serializedObject.FindProperty("backend");
        autoFind        = serializedObject.FindProperty("autoFind");
        applyToParent   = serializedObject.FindProperty("applyToParent");
        trackerIndex    = serializedObject.FindProperty("trackerIndex");
        targetClass     = serializedObject.FindProperty("targetClass");
        useSerialFilter = serializedObject.FindProperty("useSerialFilter");
        targetSerial    = serializedObject.FindProperty("targetSerial");
        openXRDeviceType = serializedObject.FindProperty("openXRDeviceType");
        viveTrackerRole  = serializedObject.FindProperty("viveTrackerRole");
        positionOffset  = serializedObject.FindProperty("positionOffset");
        rotationOffset  = serializedObject.FindProperty("rotationOffset");
        found           = serializedObject.FindProperty("found");
        foundIndex      = serializedObject.FindProperty("foundIndex");
        foundModel      = serializedObject.FindProperty("foundModel");
        foundSerial     = serializedObject.FindProperty("foundSerial");
        isTracking      = serializedObject.FindProperty("isTracking");
        stylesBuilt = false;
    }

    void BuildStyles()
    {
        if (stylesBuilt) return;
        stylesBuilt = true;

        texBg   = MakeTex(colBg);
        texCard = MakeRoundedTex(32, 32, colCard, 6);
        texBtn  = MakeRoundedTex(32, 32, new Color(0.22f, 0.22f, 0.28f, 1f), 5);
        texBtnHover = MakeRoundedTex(32, 32, new Color(0.28f, 0.28f, 0.35f, 1f), 5);

        headerStyle    = new GUIStyle { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = colTextPrimary } };
        bigStatusStyle = new GUIStyle { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        tagStyle       = new GUIStyle { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { background = MakeRoundedTex(32, 32, colAccentDim, 6), textColor = colAccent }, padding = new RectOffset(8, 8, 3, 3) };
        sectionStyle   = new GUIStyle { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = colAccent }, margin = new RectOffset(0, 0, 10, 4) };
        infoLabelStyle = new GUIStyle { fontSize = 11, normal = { textColor = colTextSecond } };
        infoValueStyle = new GUIStyle { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = colTextPrimary }, alignment = TextAnchor.MiddleRight };
        footerStyle    = new GUIStyle { fontSize = 9, normal = { textColor = colTextSecond }, alignment = TextAnchor.MiddleCenter };
        buttonStyle    = new GUIStyle { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = colTextPrimary, background = texBtn }, hover = { background = texBtnHover }, padding = new RectOffset(16, 16, 8, 8) };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        BuildStyles();

        GUIStyle labelStyleBackup = new GUIStyle(EditorStyles.label);
        EditorStyles.label.normal.textColor = colTextPrimary;

        Rect totalRect = EditorGUILayout.BeginVertical();
        GUI.DrawTexture(totalRect, texBg);
        EditorGUILayout.Space(12);

        // --- HEADER ---
        bool isSteamVR    = backend.enumValueIndex == (int)ItemTracking.TrackingBackend.SteamVR;
        bool isFound      = found.boolValue;
        bool isTrackingVal = isTracking.boolValue;
        Color accentColor = isSteamVR ? colAccent : colOpenXR;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16);
        GUILayout.Label("VARONIA BRIDGE", headerStyle);
        GUILayout.FlexibleSpace();
        tagStyle.normal.textColor = isFound ? accentColor : colWarn;
        GUILayout.Label(isFound ? "  LINKED  " : "  SCANNING  ", tagStyle);
        GUILayout.Space(16);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // --- BACKEND SELECTOR ---
        DrawCard(() => {
            DrawSectionLabel("BACKEND");
            EditorGUILayout.PropertyField(backend, new GUIContent("Tracking Backend"));
        }, accentColor.VaroniaAlpha(0.4f));

        EditorGUILayout.Space(8);

        // --- STATUS CARD ---
        DrawCard(() => {
            if (isFound) {
                bigStatusStyle.normal.textColor = accentColor;
                if (isSteamVR)
                    GUILayout.Label("INDEX " + foundIndex.intValue, bigStatusStyle);
                else
                    GUILayout.Label("DEVICE BOUND", bigStatusStyle);

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(foundModel.stringValue + " | " + foundSerial.stringValue, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            } else {
                bigStatusStyle.normal.textColor = colWarn;
                GUILayout.Label("SEARCHING...", bigStatusStyle);
            }

            DrawDivider();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Color trackColor = isTrackingVal ? new Color(0.2f, 0.9f, 0.4f, 1f) : new Color(0.9f, 0.25f, 0.25f, 1f);
            Rect dotRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12), GUILayout.Height(12));
            EditorGUI.DrawRect(new Rect(dotRect.x, dotRect.y + 2, 10, 10), trackColor);
            GUILayout.Space(6);
            GUIStyle trackStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, normal = { textColor = trackColor } };
            GUILayout.Label(isTrackingVal ? "TRACKING ACTIF" : "TRACKING PERDU", trackStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }, isFound ? accentColor : colTextSecond);

        EditorGUILayout.Space(8);

        // --- CONFIG CARD (selon backend) ---
        if (isSteamVR)
        {
            DrawCard(() => {
                DrawSectionLabel("STEAMVR CONFIG");
                EditorGUILayout.PropertyField(autoFind);
                EditorGUILayout.PropertyField(applyToParent);

                if (autoFind.boolValue) {
#if STEAMVR_ENABLED
                    EditorGUILayout.PropertyField(targetClass);
#else
                    EditorGUILayout.HelpBox("STEAMVR_ENABLED n'est pas défini — targetClass masqué.", MessageType.Warning);
#endif
                    DrawDivider();
                    DrawSectionLabel("SERIAL FILTER");
                    EditorGUILayout.PropertyField(useSerialFilter);
                    if (useSerialFilter.boolValue) EditorGUILayout.PropertyField(targetSerial);
                } else {
                    EditorGUILayout.PropertyField(trackerIndex);
                }
            }, accentColor.VaroniaAlpha(0.3f));
        }
        else
        {
            DrawCard(() => {
                DrawSectionLabel("OPENXR CONFIG");
                EditorGUILayout.PropertyField(applyToParent);
                EditorGUILayout.PropertyField(openXRDeviceType, new GUIContent("Device Type"));

                bool isViveTracker = openXRDeviceType.enumValueIndex == (int)ItemTracking.OpenXRDeviceType.ViveTracker;
                if (isViveTracker)
                {
                    EditorGUILayout.PropertyField(viveTrackerRole, new GUIContent("Tracker Role"));
                }
            }, colOpenXR.VaroniaAlpha(0.3f));
        }

        EditorGUILayout.Space(8);

        // --- OFFSET CARD ---
        DrawCard(() => {
            DrawSectionLabel("OFFSETS");
            EditorGUILayout.PropertyField(positionOffset);
            EditorGUILayout.PropertyField(rotationOffset);
            if (GUILayout.Button("RESET OFFSETS", EditorStyles.miniButton)) {
                positionOffset.vector3Value = Vector3.zero;
                rotationOffset.vector3Value = Vector3.zero;
            }
        }, colWarn.VaroniaAlpha(0.4f));

        EditorGUILayout.Space(16);
        if (GUILayout.Button("RESCAN SYSTEM", buttonStyle, GUILayout.Height(34)))
            ((ItemTracking)target).Rescan();

        EditorGUILayout.Space(16);
        GUILayout.Label("Varonia OpenVR Bridge · 2026", footerStyle);
        EditorGUILayout.Space(8);
        EditorGUILayout.EndVertical();

        EditorStyles.label.normal.textColor = labelStyleBackup.normal.textColor;

        serializedObject.ApplyModifiedProperties();
        if (Application.isPlaying) Repaint();
    }

    void DrawCard(System.Action content, Color accent)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(12);
        Rect r = EditorGUILayout.BeginVertical();
        if (texCard) GUI.DrawTexture(r, texCard);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 2, r.height), accent);
        GUILayout.Space(10);
        content();
        GUILayout.Space(10);
        EditorGUILayout.EndVertical();
        GUILayout.Space(12);
        EditorGUILayout.EndHorizontal();
    }

    void DrawSectionLabel(string text) => GUILayout.Label(text.ToUpper(), sectionStyle);

    void DrawDivider()
    {
        Rect r = GUILayoutUtility.GetRect(1, 10);
        EditorGUI.DrawRect(new Rect(r.x + 10, r.y + 5, r.width - 20, 1), colDivider);
    }

    static Texture2D MakeTex(Color col)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, col);
        t.Apply(); t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }

    static Texture2D MakeRoundedTex(int w, int h, Color col, int radius)
    {
        var t = new Texture2D(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) t.SetPixel(x, y, col);
        t.Apply(); t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }
}

public static class VaroniaColorExtensions
{
    public static Color VaroniaAlpha(this Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);
}
