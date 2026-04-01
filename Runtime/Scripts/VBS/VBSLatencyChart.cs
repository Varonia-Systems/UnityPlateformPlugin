using System;
using System.Collections;
using System.Text;
using UnityEngine;
#if STEAMVR_ENABLED 
using Valve.VR;
#endif
using VaroniaBackOffice;

public enum HelmetState { Ok = 0, NoGameFocusOrMicroLag = 2, NoStreamOrPowerOff = 3 }
public enum TrackingState { Ok = 0, Strange = 1, Lost = 2, BigLost = 3, NO = 4 }

public class VBSLatencyChart : MonoBehaviour
{
#if STEAMVR_ENABLED 

    public enum DisplayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    [Header("Display")]
    [SerializeField] private DisplayCorner corner = DisplayCorner.BottomRight;
    [SerializeField] private Vector2 size = new Vector2(340f, 130f);
    [SerializeField] private float margin = 12f;

    [Header("Chart Config")]
    [SerializeField] private int maxBars = 80;
    [SerializeField] private float barHeight = 18f;
    [SerializeField] private float barGap = 3f;

    static readonly Color ColBg = new Color(0.11f, 0.11f, 0.14f, 0.92f);
    static readonly Color ColGood = new Color(0.30f, 0.85f, 0.65f, 1f);
    static readonly Color ColWarn = new Color(1.00f, 0.75f, 0.30f, 1f);
    static readonly Color ColBad = new Color(1.00f, 0.40f, 0.40f, 1f);
    static readonly Color ColOrange = new Color(1.00f, 0.55f, 0.20f, 1f);
    static readonly Color ColPurple = new Color(0.70f, 0.30f, 0.90f, 1f);
    static readonly Color ColMutedFg = new Color(0.55f, 0.55f, 0.62f, 1f);
    static readonly Color ColDivider = new Color(1f, 1f, 1f, 0.06f);

    private Texture2D[] _history;
    private int _writeIdx;
    private CVRSystem _vrSystem;
    private string _headsetName = "—";
    private bool _ready = false;

    private int _lagCount = 0;
    private float _totalLagTime = 0f;
    private bool _wasLagging = false;

    private bool _stylesBuilt;
    private GUIStyle _headerStyle, _statusStyle, _valStyle, _statLabelStyle, _statValStyle;
    private Texture2D _texBg, _texDivider, _texAccent, _texGreen, _texYellow, _texOrange, _texPurple, _texRed;

    public HelmetState helmet;
    public TrackingState tracking;

    // ===== ANTI-CRASH =====
    private static bool _dead = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _dead = false;
    }


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void InstallQuitHook()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged += (state) =>
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                KillAll();
        };
#else
    Application.quitting += KillAll;
#endif
    }

    static void KillAll()
    {
        _dead = true;
        foreach (var chart in FindObjectsOfType<VBSLatencyChart>())
        {
            chart._vrSystem = null;
            chart._ready = false;
            chart.StopAllCoroutines();
            chart.enabled = false;
        }
    }

    private void Awake() => _history = new Texture2D[maxBars];

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(2);
        if (_dead) yield break;

        _vrSystem = SteamVRBridge.GetSystem();
        if (_vrSystem == null) { Destroy(this); yield break; }

        var sb = new StringBuilder(256);
        ETrackedPropertyError err = ETrackedPropertyError.TrackedProp_Success;
        _vrSystem.GetStringTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_ModelNumber_String, sb, 256, ref err);
        _headsetName = sb.Length > 0 ? sb.ToString() : "—";

        if (_headsetName != "Vive VBStreaming Focus3") { Destroy(this); yield break; }

        _ready = true;
        StartCoroutine(DataLoop());
    }

    private IEnumerator DataLoop()
    {
        var wait = new WaitForSeconds(0.1f);
        while (true)
        {
            if (_dead) yield break;

            UpdateStates();

            _history[_writeIdx] = GetCurrentStateTex();
            _writeIdx = (_writeIdx + 1) % maxBars;

            bool isHelmetLagging = (helmet != HelmetState.Ok);

            if (isHelmetLagging)
            {
                _totalLagTime += 0.1f;
                _wasLagging = true;
            }
            else
            {
                if (_wasLagging)
                {
                    _lagCount++;
                    _wasLagging = false;
                }
            }

            yield return wait;
        }
    }

    private void UpdateStates()
    {
        if (_vrSystem == null || _dead) return;

        EDeviceActivityLevel act = _vrSystem.GetTrackedDeviceActivityLevel(0);
        helmet = (act == EDeviceActivityLevel.k_EDeviceActivityLevel_UserInteraction) ? HelmetState.Ok :
                 (act == EDeviceActivityLevel.k_EDeviceActivityLevel_Idle) ? HelmetState.NoGameFocusOrMicroLag : HelmetState.NoStreamOrPowerOff;

        TrackedDevicePose_t[] poses = new TrackedDevicePose_t[1];
        _vrSystem.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseRawAndUncalibrated, 0, poses);
        tracking = !poses[0].bPoseIsValid ? TrackingState.Lost :
                   (poses[0].eTrackingResult == ETrackingResult.Running_OK) ? TrackingState.Ok : TrackingState.Strange;
    }

    private string GetHelmetFriendlyName(HelmetState state)
    {
        switch (state)
        {
            case HelmetState.Ok: return "READY / ACTIVE";
            case HelmetState.NoGameFocusOrMicroLag: return "FOCUS LOST / LAG";
            case HelmetState.NoStreamOrPowerOff: return "OFFLINE / DISCONNECTED";
            default: return "UNKNOWN";
        }
    }

    private string GetTrackingFriendlyName(TrackingState state)
    {
        switch (state)
        {
            case TrackingState.Ok: return "STABLE";
            case TrackingState.Strange: return "POOR QUALITY";
            case TrackingState.Lost: return "TRACKING LOST";
            default: return "ERROR";
        }
    }

    private Texture2D GetCurrentStateTex()
    {
        if (helmet == HelmetState.NoStreamOrPowerOff) return _texRed;
        if (helmet == HelmetState.NoGameFocusOrMicroLag) return _texPurple;
        if (tracking == TrackingState.Lost) return _texOrange;
        if (tracking == TrackingState.Strange) return _texYellow;
        return _texGreen;
    }

    private void OnGUI()
    {
        if (!_ready || _dead) return;
        EnsureStyles();

        Rect panel = GetPanelRect();
        GUI.DrawTexture(panel, _texBg);
        GUI.DrawTexture(new Rect(panel.x, panel.y, 3f, panel.height), _texAccent);

        GUI.Label(new Rect(panel.x + 12, panel.y + 6, 200, 20), $"VBS MONITOR • {_headsetName}", _headerStyle);
        GUI.DrawTexture(new Rect(panel.x + 8, panel.y + 26, panel.width - 16, 1), _texDivider);

        float statusY = panel.y + 32;
        GUI.Label(new Rect(panel.x + 12, statusY, 150, 20), "HELMET STATUS", _statusStyle);
        GUI.Label(new Rect(panel.x + 12, statusY + 12, 180, 20), GetHelmetFriendlyName(helmet), _valStyle);

        GUI.Label(new Rect(panel.x + 185, statusY, 150, 20), "TRACKING QUALITY", _statusStyle);
        GUI.Label(new Rect(panel.x + 185, statusY + 12, 140, 20), GetTrackingFriendlyName(tracking), _valStyle);

        float statsY = statusY + 32;
        GUI.DrawTexture(new Rect(panel.x + 8, statsY - 4, panel.width - 16, 1), _texDivider);

        GUI.Label(new Rect(panel.x + 12, statsY, 100, 20), "STREAM DROPS", _statLabelStyle);
        GUI.Label(new Rect(panel.x + 12, statsY + 11, 100, 20), $"{_lagCount} EVENTS", _statValStyle);

        GUI.Label(new Rect(panel.x + 120, statsY, 150, 20), "TOTAL DOWN TIME", _statLabelStyle);
        GUI.Label(new Rect(panel.x + 120, statsY + 11, 150, 20), $"{_totalLagTime:F1} SECONDS", _statValStyle);

        float chartX = panel.x + 8;
        float chartW = panel.width - 16;
        float barW = chartW / maxBars;
        float chartY = panel.y + panel.height - barHeight - 8;

        for (int i = 0; i < maxBars; i++)
        {
            int idx = (_writeIdx + i) % maxBars;
            Texture2D tex = _history[idx];
            if (tex == null) continue;
            GUI.DrawTexture(new Rect(chartX + (i * barW), chartY, barW - barGap, barHeight), tex);
        }
    }

    private Rect GetPanelRect()
    {
        float x = (corner == DisplayCorner.TopRight || corner == DisplayCorner.BottomRight) ? Screen.width - size.x - margin : margin;
        float y = (corner == DisplayCorner.BottomLeft || corner == DisplayCorner.BottomRight) ? Screen.height - size.y - margin : margin;
        return new Rect(x, y, size.x, size.y);
    }

    private void EnsureStyles()
    {
        if (_stylesBuilt) return;
        _texBg = MakeTex(ColBg);
        _texDivider = MakeTex(ColDivider);
        _texAccent = MakeTex(ColGood);
        _texGreen = MakeTex(ColGood);
        _texYellow = MakeTex(ColWarn);
        _texOrange = MakeTex(ColOrange);
        _texPurple = MakeTex(ColPurple);
        _texRed = MakeTex(ColBad);

        _headerStyle = new GUIStyle { fontSize = 9, fontStyle = FontStyle.Bold, normal = { textColor = ColMutedFg } };
        _statusStyle = new GUIStyle { fontSize = 7, fontStyle = FontStyle.Bold, normal = { textColor = ColMutedFg } };
        _valStyle = new GUIStyle { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        _statLabelStyle = new GUIStyle { fontSize = 7, fontStyle = FontStyle.Normal, normal = { textColor = ColMutedFg } };
        _statValStyle = new GUIStyle { fontSize = 9, fontStyle = FontStyle.Bold, normal = { textColor = ColOrange } };

        _stylesBuilt = true;
    }

    private static Texture2D MakeTex(Color col)
    {
        Texture2D t = new Texture2D(1, 1);
        t.SetPixel(0, 0, col);
        t.Apply();
        return t;
    }

    void OnDisable()
    {
        _vrSystem = null;
        _ready = false;
    }

    void OnApplicationQuit()
    {
        _dead = true;
        _vrSystem = null;
        _ready = false;
        SteamVRBridge.SafeShutdown();
    }
#endif
}