using UnityEngine;
using UnityEditor;

#if STEAMVR_ENABLED
using Valve.VR;
#endif

using System.Text;
using System.Collections.Generic;


public class OpenVRVaroniaMonitor : EditorWindow
{
#if STEAMVR_ENABLED
    // ── Logic Data ───────────────────────────────────────────────────────────
    private Vector2 scrollPos;
    private bool autoRefresh = false;
    private bool isPersistentInit = false;
    private CVRSystem vr;
    private List<DeviceEntry> entries = new List<DeviceEntry>();

    private static Dictionary<uint, bool> foldoutStates = new Dictionary<uint, bool>();
    private static Dictionary<uint, bool> advancedStates = new Dictionary<uint, bool>();
    private static Dictionary<uint, bool> rawStates = new Dictionary<uint, bool>();

    private class DeviceEntry
    {
        public uint index;
        public ETrackedDeviceClass deviceClass;
        public string model;
        public string serial;
        public string mainBody;
        public string advancedBody;
        public string inputBody;
        public string rawBody; // Le dump complet
        public bool isConnected;
        
        public bool Foldout {
            get => foldoutStates.ContainsKey(index) && foldoutStates[index];
            set => foldoutStates[index] = value;
        }
        public bool ShowAdvanced {
            get => advancedStates.ContainsKey(index) && advancedStates[index];
            set => advancedStates[index] = value;
        }
        public bool ShowRaw {
            get => rawStates.ContainsKey(index) && rawStates[index];
            set => rawStates[index] = value;
        }
    }

    // ── Style & Colors ───────────────────────────────────────────────────────
    static readonly Color colBg          = new Color(0.11f, 0.11f, 0.14f, 1f);
    static readonly Color colCard        = new Color(0.15f, 0.15f, 0.19f, 1f);
    static readonly Color colAccent      = new Color(0.30f, 0.85f, 0.65f, 1f);
    static readonly Color colWarn        = new Color(1f,    0.75f, 0.30f, 1f);
    static readonly Color colError       = new Color(1f,    0.40f, 0.40f, 1f);
    static readonly Color colTextPrimary = new Color(0.92f, 0.92f, 0.95f, 1f);
    static readonly Color colTextSecond  = new Color(0.55f, 0.55f, 0.62f, 1f);
    static readonly Color colTextMuted   = new Color(0.40f, 0.40f, 0.47f, 1f);
    static readonly Color colDivider     = new Color(1f,    1f,    1f,    0.06f);

    private GUIStyle headerStyle, sectionStyle, tagStyle, bodyStyle, richFoldoutStyle, rawStyle;
    private bool stylesBuilt = false;

    [MenuItem("Varonia/OpenVR Full Monitor")]
    static void Open() => GetWindow<OpenVRVaroniaMonitor>("VR Monitor").minSize = new Vector2(600, 750);

    private void OnDisable() => StopPersistentVR();
    private void OnInspectorUpdate() { if (autoRefresh) { DoScan(); Repaint(); } }

    void BuildStyles()
    {
        if (stylesBuilt) return;
        headerStyle = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = colTextPrimary } };
        sectionStyle = new GUIStyle { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = colAccent }, margin = new RectOffset(0,0,10,2) };
        tagStyle = new GUIStyle { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        bodyStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, fontSize = 11, normal = { textColor = colTextSecond } };
        rawStyle = new GUIStyle(EditorStyles.textArea) { richText = true, wordWrap = true, fontSize = 10, normal = { textColor = colTextSecond, background = null } };
        
        richFoldoutStyle = new GUIStyle(EditorStyles.label) {
            richText = true, fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = colTextPrimary }
        };
        stylesBuilt = true;
    }

    void OnGUI()
    {
        BuildStyles();
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colBg);

        DrawHeader();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        GUILayout.Space(10);

        foreach (var e in entries)
        {
            Color classCol = GetClassColor(e.deviceClass);
            DrawCard(() => 
            {
                // TITRE CUSTOM (Fix balises color)
                EditorGUILayout.BeginHorizontal();
                string arrow = e.Foldout ? "▼" : "▶";
                if (GUILayout.Button($"{arrow}  [{e.index}] {e.model}  <color=#666666>{e.serial}</color>", richFoldoutStyle))
                    e.Foldout = !e.Foldout;
                
                GUILayout.FlexibleSpace();
                GUILayout.Label($" {e.deviceClass} ", GetTagStyle(e.isConnected ? classCol : colError));
                EditorGUILayout.EndHorizontal();

                if (e.Foldout)
                {
                    DrawDivider();
                    GUILayout.Label(e.mainBody, bodyStyle);
                    
                    if (!string.IsNullOrEmpty(e.inputBody))
                    {
                        GUILayout.Space(5);
                        GUILayout.Label("LIVE INPUTS", sectionStyle);
                        GUILayout.Label(e.inputBody, bodyStyle);
                    }

                    // --- SECTION AVANCÉE ---
                    GUILayout.Space(5);
                    Rect advRect = EditorGUILayout.BeginVertical();
                    EditorGUI.DrawRect(advRect, new Color(0,0,0,0.20f));
                    e.ShowAdvanced = EditorGUILayout.Foldout(e.ShowAdvanced, "FIRMWARE & OPTICS", true);
                    if (e.ShowAdvanced) {
                        GUILayout.BeginHorizontal(); GUILayout.Space(15);
                        GUILayout.Label(e.advancedBody, bodyStyle);
                        GUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndVertical();

                    // --- SECTION RAW (DUMP COMPLET) ---
                    GUILayout.Space(5);
                    Rect rawRect = EditorGUILayout.BeginVertical();
                    EditorGUI.DrawRect(rawRect, new Color(0,0,0,0.35f));
                    e.ShowRaw = EditorGUILayout.Foldout(e.ShowRaw, "RAW DEVICE DUMP (FULL PROPERTIES)", true);
                    if (e.ShowRaw) {
                        GUILayout.BeginHorizontal(); GUILayout.Space(15);
                        EditorGUILayout.SelectableLabel(e.rawBody, rawStyle, GUILayout.Height(300));
                        GUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndVertical();
                }
            }, e.isConnected ? classCol : colError);
            GUILayout.Space(8);
        }

        GUILayout.Space(20);
        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        Rect hRect = EditorGUILayout.BeginVertical(GUILayout.Height(60));
        EditorGUI.DrawRect(hRect, colCard);
        GUILayout.Space(15);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(15);
        GUILayout.Label("OPENVR FULL MONITOR", headerStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(autoRefresh ? "● LIVE ACTIVE" : "○ START LIVE", GUILayout.Width(110), GUILayout.Height(24)))
            autoRefresh = !autoRefresh;
        if (GUILayout.Button("FORCE SCAN", GUILayout.Width(100), GUILayout.Height(24)))
            DoScan();
        GUILayout.Space(15);
        EditorGUILayout.EndHorizontal();
        EditorGUI.DrawRect(new Rect(0, hRect.height-1, position.width, 1), colDivider);
        EditorGUILayout.EndVertical();
    }

    void DoScan()
    {
        if (Application.isPlaying && OpenVR.System != null) vr = OpenVR.System;
        else if (autoRefresh && vr == null) {
            EVRInitError err = EVRInitError.None;
            vr = OpenVR.Init(ref err, EVRApplicationType.VRApplication_Background);
            isPersistentInit = (vr != null);
        } else if (!autoRefresh) {
            EVRInitError err = EVRInitError.None;
            vr = OpenVR.Init(ref err, EVRApplicationType.VRApplication_Background);
        }

        if (vr == null) return;

        List<DeviceEntry> newEntries = new List<DeviceEntry>();
        TrackedDevicePose_t[] poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        vr.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, poses);

        for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
        {
            ETrackedDeviceClass cls = vr.GetTrackedDeviceClass(i);
            if (cls == ETrackedDeviceClass.Invalid) continue;

            StringBuilder main = new StringBuilder();
            StringBuilder adv = new StringBuilder();
            StringBuilder inp = new StringBuilder();
            StringBuilder raw = new StringBuilder();

            string sn = GetStr(i, ETrackedDeviceProperty.Prop_SerialNumber_String);
            if (string.IsNullOrEmpty(sn)) sn = "N/A";

            // --- 1. MAIN ---
            float batt = GetFloat(i, ETrackedDeviceProperty.Prop_DeviceBatteryPercentage_Float);
            main.AppendLine($"<b>Serial :</b> {sn}");
            main.AppendLine($"<b>Batterie :</b> {(batt*100):F0}% {(GetBool(i, ETrackedDeviceProperty.Prop_DeviceIsCharging_Bool)?"[Charging]":"")}");
            
            TrackedDevicePose_t pose = poses[i];
            if (pose.bPoseIsValid) {
                HmdMatrix34_t m = pose.mDeviceToAbsoluteTracking;
                main.AppendLine($"<b>Position :</b> ({m.m3:F3}, {m.m7:F3}, {-m.m11:F3})");
            }

            // --- 2. INPUTS ---
            if (cls == ETrackedDeviceClass.Controller || cls == ETrackedDeviceClass.GenericTracker) {
                VRControllerState_t st = new VRControllerState_t();
                if(vr.GetControllerState(i, ref st, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t)))) {
                    inp.AppendLine($"  Trigger : {st.rAxis1.x:F3} | Grip: {st.rAxis2.x:F3} | Pad: {st.rAxis0.x:F3}, {st.rAxis0.y:F3}");
                    List<string> btns = new List<string>();
                    if ((st.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_SteamVR_Trigger)) != 0) btns.Add("TRIGGER");
                    if ((st.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_Grip)) != 0) btns.Add("GRIP");
                    if ((st.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_A)) != 0) btns.Add("A/X");
                    if ((st.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_ApplicationMenu)) != 0) btns.Add("MENU");
                    if(btns.Count > 0) inp.AppendLine($"  <color=#FFD44C><b>[PRESSED]</b> {string.Join(", ", btns)}</color>");
                }
            }

            // --- 3. ADVANCED ---
            adv.AppendLine($"Manufacturer : {GetStr(i, ETrackedDeviceProperty.Prop_ManufacturerName_String)}");
            adv.AppendLine($"Render Model : {GetStr(i, ETrackedDeviceProperty.Prop_RenderModelName_String)}");

            // --- 4. RAW DUMP (Copie conforme du script original) ---
            raw.AppendLine("<b>=== IDENTITE ===</b>");
            raw.AppendLine("  Index : " + i);
            raw.AppendLine("  Model : " + GetStr(i, ETrackedDeviceProperty.Prop_ModelNumber_String));
            raw.AppendLine("  Serial : " + sn);
            raw.AppendLine("  TrackingSystemName : " + GetStr(i, ETrackedDeviceProperty.Prop_TrackingSystemName_String));
            raw.AppendLine("\n<b>=== FIRMWARE ===</b>");
            raw.AppendLine("  FirmwareVersion : " + GetStr(i, ETrackedDeviceProperty.Prop_TrackingFirmwareVersion_String));
            raw.AppendLine("  HardwareRevision : " + GetStr(i, ETrackedDeviceProperty.Prop_HardwareRevision_String));
            raw.AppendLine("  UpdateAvailable : " + GetBool(i, ETrackedDeviceProperty.Prop_Firmware_UpdateAvailable_Bool));
            raw.AppendLine("\n<b>=== ICONS ===</b>");
            raw.AppendLine("  Path Ready : " + GetStr(i, ETrackedDeviceProperty.Prop_NamedIconPathDeviceReady_String));
            raw.AppendLine("  Path Alert : " + GetStr(i, ETrackedDeviceProperty.Prop_NamedIconPathDeviceReadyAlert_String));
            raw.AppendLine("\n<b>=== DIVERS ===</b>");
            raw.AppendLine("  InstallPath : " + GetStr(i, ETrackedDeviceProperty.Prop_InstallPath_String));
            raw.AppendLine("  HasCamera : " + GetBool(i, ETrackedDeviceProperty.Prop_HasCamera_Bool));

            newEntries.Add(new DeviceEntry {
                index = i, deviceClass = cls, model = GetStr(i, ETrackedDeviceProperty.Prop_ModelNumber_String),
                serial = sn, isConnected = pose.bDeviceIsConnected, 
                mainBody = main.ToString(), advancedBody = adv.ToString(), inputBody = inp.ToString(), rawBody = raw.ToString()
            });
        }
        entries = newEntries;
        if (!autoRefresh && !Application.isPlaying) StopPersistentVR();
    }

    private void StopPersistentVR() { if (isPersistentInit) { OpenVR.Shutdown();
                                                              isPersistentInit = false; vr = null; } }

    void DrawCard(System.Action content, Color accentColor) {
        EditorGUILayout.BeginHorizontal(); GUILayout.Space(10);
        EditorGUILayout.BeginVertical();
        Rect r = EditorGUILayout.BeginVertical();
        r.x -= 4; r.width += 8; r.y -= 2; r.height += 4;
        EditorGUI.DrawRect(r, colCard);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height), accentColor);
        GUILayout.Space(5); content(); GUILayout.Space(5);
        EditorGUILayout.EndVertical(); EditorGUILayout.EndVertical();
        GUILayout.Space(10); EditorGUILayout.EndHorizontal();
    }

    void DrawDivider() { Rect r = GUILayoutUtility.GetRect(1, 1); r.y += 2; EditorGUI.DrawRect(r, colDivider); GUILayout.Space(6); }

    GUIStyle GetTagStyle(Color c) {
        var s = new GUIStyle(tagStyle);
        s.normal.background = MakeRoundedTex(32,32, new Color(c.r, c.g, c.b, 0.15f), 4);
        s.normal.textColor = c; s.padding = new RectOffset(8,8,2,2); return s;
    }

    Color GetClassColor(ETrackedDeviceClass c) {
        if (c == ETrackedDeviceClass.HMD) return colAccent;
        if (c == ETrackedDeviceClass.Controller) return colWarn;
        return colTextMuted;
    }

    string GetStr(uint i, ETrackedDeviceProperty p) {
        var sb = new StringBuilder(512); var err = ETrackedPropertyError.TrackedProp_Success;
        vr.GetStringTrackedDeviceProperty(i, p, sb, 512, ref err); return sb.ToString();
    }
    float GetFloat(uint i, ETrackedDeviceProperty p) { var err = ETrackedPropertyError.TrackedProp_Success; return vr.GetFloatTrackedDeviceProperty(i, p, ref err); }
    bool GetBool(uint i, ETrackedDeviceProperty p) { var err = ETrackedPropertyError.TrackedProp_Success; return vr.GetBoolTrackedDeviceProperty(i, p, ref err); }

    static Texture2D MakeRoundedTex(int w, int h, Color col, int radius) {
        var t = new Texture2D(w, h);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, col);
        t.Apply(); return t;
    }
 #endif
}