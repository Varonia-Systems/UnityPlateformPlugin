using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using VaroniaBackOffice;
using VBO_Ultimate.Runtime.Scripts.Input;
#if STEAMVR_ENABLED
using Valve.VR;
#endif

public class Vortex_Weapon : _Weapon
{
    [Header("Live Data")]
    public float triggerValue;
    public float gripValue;
    public bool buttonPrimary;   // A (droite) / X (gauche)
    public bool buttonSecondary; // B (droite) / Y (gauche)

    public GameObject Left_R, Right_R;

    // OpenXR
    private InputDevice _controller;

#if STEAMVR_ENABLED
    // SteamVR
    private CVRSystem _vrSystem;
    private uint _deviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
#endif

    private bool _isLeft;
    private bool _useSteamVR;
    private int _weaponIndex;

    // ===== ANTI-CRASH =====
    private static bool _dead = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _dead = false;
    }

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void EditorQuitHook()
    {
        UnityEditor.EditorApplication.playModeStateChanged += (state) =>
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                _dead = true;
                foreach (var weapon in FindObjectsOfType<Vortex_Weapon>())
                {
#if STEAMVR_ENABLED
                    weapon._vrSystem = null;
                    weapon._deviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
#endif
                    weapon.StopAllCoroutines();
                    weapon.enabled = false;
                }
            }
        };
    }
#endif

    private IEnumerator Start()
    {
        if (_dead) yield break;
        yield return new WaitUntil(() => BackOfficeVaronia.Instance.config != null);
        if (_dead) yield break;

        _isLeft = BackOfficeVaronia.Instance.config.MainHand == MainHand.Left;

        Left_R.SetActive(_isLeft);
        Right_R.SetActive(!_isLeft);

        if (_isLeft)
            trackingOpenVR.targetSerial = "CTL_LEFT";

        var tracking = GetComponentInParent<VaroniaWeaponTracking>();
        _weaponIndex = tracking != null ? tracking.weaponIndex : 0;


        // Détection SteamVR ou OpenXR
#if STEAMVR_ENABLED
        // Si OpenVR est initialisé en mode Background (par nous), on force OpenXR pour les inputs
        if (!SteamVRBridge.IsShuttingDown)
            _vrSystem = SteamVRBridge.GetSystem();
        _useSteamVR = _vrSystem != null && !SteamVRBridge.InitializedByUs;
#else
        _useSteamVR = false;
#endif

        if (_useSteamVR)
        {
            
            Debug.Log("#<color=orange>[Vortex]</color> Mode SteamVR détecté.");
#if STEAMVR_ENABLED
            StartCoroutine(SetupSteamVRController());
            
                    
#if  OFFICIELOPENVR
            if(BackOfficeVaronia.Instance.config.MainHand == MainHand.Right)
            inputSource = SteamVR_Input_Sources.RightHand;
            else inputSource = SteamVR_Input_Sources.LeftHand;

#endif

#endif
        }
        else
        {
            Debug.Log("#<color=orange>[Vortex]</color> Mode OpenXR détecté.");
            StartCoroutine(SetupOpenXRController());
        }
    }

#if STEAMVR_ENABLED
    private IEnumerator SetupSteamVRController()
    {
        ETrackedControllerRole role = _isLeft
            ? ETrackedControllerRole.LeftHand
            : ETrackedControllerRole.RightHand;

        while (_deviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            _deviceIndex = _vrSystem.GetTrackedDeviceIndexForControllerRole(role);
            yield return new WaitForSeconds(0.5f);
        }

        Debug.Log("<color=orange>[Vortex]</color> Contrôleur SteamVR détecté ! Index : " + _deviceIndex);
    }
#endif

    private IEnumerator SetupOpenXRController()
    {
        XRNode node = _isLeft ? XRNode.LeftHand : XRNode.RightHand;
        InputDeviceCharacteristics controllerFlags = InputDeviceCharacteristics.Controller
            | (_isLeft ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right);

        while (!_controller.isValid)
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(controllerFlags, devices);
            if (devices.Count > 0) _controller = devices[0];
            yield return new WaitForSeconds(0.5f);
        }

        Debug.Log("<color=orange>[Vortex]</color> Contrôleur OpenXR détecté ! (" + _controller.name + ")");
    }

    private void Update()
    {
        if (_dead) return;

        // isConnected : contrôleur détecté selon le mode
#if STEAMVR_ENABLED
        bool isConnected = _useSteamVR
            ? _deviceIndex != OpenVR.k_unTrackedDeviceIndexInvalid
            : _controller.isValid && (_controller.characteristics & InputDeviceCharacteristics.Controller) != 0;
#else
        bool isConnected = _controller.isValid && (_controller.characteristics & InputDeviceCharacteristics.Controller) != 0;
#endif
        VaroniaInput.SetDeviceData(_weaponIndex, isConnected, 0, 0f, 0, WeaponInfo != null ? WeaponInfo.DisplayNameModel : "");

        if (DebugModeOverlay.IsDebugMode) return;

#if  OFFICIELOPENVR
        if (_useSteamVR)
            UpdateSteamVR();
        else
#endif
            UpdateOpenXR();

        // Envoi au système Varonia
        // Trigger → Primary
        VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Primary, triggerValue > 0.5f);

        // Grip → Secondary
        VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Secondary, gripValue > 0.5f);

        // Btn A/X → Tertiary
        VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Tertiary, buttonPrimary);

        // Btn B/Y → Quaternary
        VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Quaternary, buttonSecondary);
    }

#if OFFICIELOPENVR

    [Header("SteamVR Action Mapping")]
    [SerializeField] private string actionSetName   = "default";
    [SerializeField] private string triggerAxisName = "Squeeze";   // SteamVR_Action_Single
    [SerializeField] private string gripButtonName  = "GrabGrip";  // SteamVR_Action_Boolean

    public SteamVR_Input_Sources inputSource = SteamVR_Input_Sources.Any;

    private SteamVR_Action_Single  _triggerAxis;
    private SteamVR_Action_Boolean _gripButton;
    private bool _actionsResolved;

    private void ResolveSteamVRActions()
    {
        _triggerAxis = SteamVR_Input.GetAction<SteamVR_Action_Single> (actionSetName, triggerAxisName, false);
        _gripButton  = SteamVR_Input.GetAction<SteamVR_Action_Boolean>(actionSetName, gripButtonName,  false);

        if (_triggerAxis == null)
            Debug.LogWarning($"<color=orange>[Vortex]</color> Action '{actionSetName}/{triggerAxisName}' introuvable dans actions.json — triggerValue restera à 0.");
        if (_gripButton == null)
            Debug.LogWarning($"<color=orange>[Vortex]</color> Action '{actionSetName}/{gripButtonName}' introuvable dans actions.json — gripValue restera à 0.");

        _actionsResolved = true;
    }

    private void UpdateSteamVR()
    {
        if (!_actionsResolved) ResolveSteamVRActions();

        if (_triggerAxis != null)
            triggerValue = _triggerAxis.GetAxis(inputSource);

        if (_gripButton != null)
            gripValue = _gripButton.GetState(inputSource) ? 1f : 0f;
    }
#endif

    private void UpdateOpenXR()
    {
        if (!_controller.isValid) return;

        if (_controller.TryGetFeatureValue(CommonUsages.trigger, out float t))
            triggerValue = t;

        if (_controller.TryGetFeatureValue(CommonUsages.grip, out float g))
            gripValue = g;

        // A (droite) / X (gauche) → primaryButton
        if (_controller.TryGetFeatureValue(CommonUsages.primaryButton, out bool pb))
            buttonPrimary = pb;

        // B (droite) / Y (gauche) → secondaryButton
        if (_controller.TryGetFeatureValue(CommonUsages.secondaryButton, out bool sb))
            buttonSecondary = sb;
    }
}
