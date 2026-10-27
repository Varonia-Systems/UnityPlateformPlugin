using System;
using UnityEngine;
using UnityEngine.Events;

#if STEAMVR_ENABLED
using Valve.VR;
#endif

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.OpenXR.Features.Interactions;
#endif

namespace VaroniaBackOffice
{
    public class ItemTracking : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        //  ENUMS
        // ─────────────────────────────────────────────

        public enum TrackingBackend { SteamVR, OpenXR }
        public enum TrackingMode   { AutoByClass, ManualIndex }

        public enum OpenXRDeviceType
        {
            LeftController,
            RightController,
            ViveTracker
        }

        public enum ViveTrackerRole
        {
            // Preserve the numeric IDs already serialized in scenes and prefabs.
            First = -1,
            HandheldObject = 0,
            LeftFoot = 1,
            RightFoot = 2,
            LeftShoulder = 3,
            RightShoulder = 4,
            LeftElbow = 5,
            RightElbow = 6,
            LeftKnee = 7,
            RightKnee = 8,
            Waist = 9,
            Chest = 10,
            Camera = 11,
            Keyboard = 12,
            LeftWrist = 13,
            RightWrist = 14,
            LeftAnkle = 15,
            RightAnkle = 16
        }

        // ─────────────────────────────────────────────
        //  INSPECTOR
        // ─────────────────────────────────────────────

        [Header("Backend de Tracking")]
        public TrackingBackend backend = TrackingBackend.SteamVR;

        // ── SteamVR ──────────────────────────────────
        [Header("Configuration")]
        public TrackingMode mode = TrackingMode.AutoByClass;
        public bool autoFind = true;
        public bool applyToParent = false;
#if STEAMVR_ENABLED
        public ETrackedDeviceClass targetClass = ETrackedDeviceClass.GenericTracker;
#endif
        public int trackerIndex = 3;

        [Header("Filtre Avancé")]
        public bool useSerialFilter = false;
        public string targetSerial = "LHR-XXXXXXXX";

        // ── OpenXR ──────────────────────────────────
        [Header("OpenXR — Configuration")]
        public OpenXRDeviceType openXRDeviceType = OpenXRDeviceType.ViveTracker;
        public ViveTrackerRole  viveTrackerRole  = ViveTrackerRole.First;

        // ── Commun ───────────────────────────────────
        [Header("Offsets")]
        public Vector3 positionOffset = Vector3.zero;
        public Vector3 rotationOffset = Vector3.zero;

        [Header("État (lecture seule)")]
        public bool   found      = false;
        public bool   isTracking = false;
        public int    foundIndex = -1;
        public string foundModel = "";
        public string foundSerial = "";

        /// <summary>
        /// Flag externe (poussé chaque frame par <c>VaroniaWeaponTracking</c>) qui indique
        /// qu'une <c>TrackingSuppressionZone</c> a marqué cette arme comme "perdue" alors
        /// que le hardware tracking est toujours OK. Purement informatif côté ItemTracking
        /// — n'arrête PAS l'application de la pose, on continue à update le transform
        /// pour pouvoir redégainer. Affiché dans le custom editor d'ItemTracking.
        /// </summary>
        [System.NonSerialized] public bool externalForceLost = false;

        [Header("Événements")]
        public UnityEvent OnTrackingLost;
        public UnityEvent OnTrackingRestored;

        // ─────────────────────────────────────────────
        //  PRIVÉ — SteamVR
        // ─────────────────────────────────────────────

#if STEAMVR_ENABLED
        private TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[64];
        private System.Text.StringBuilder _sb = new System.Text.StringBuilder(256);
        private CVRSystem vr;
#endif

        // ─────────────────────────────────────────────
        //  PRIVÉ — OpenXR
        // ─────────────────────────────────────────────

#if ENABLE_INPUT_SYSTEM
        private InputDevice _openXRDevice;
        private bool        _openXRBound = false;
        private string _selectedOpenXRSerial = "";
        private string _selectedOpenXRProduct = "";
#endif

        // ─────────────────────────────────────────────
        //  PRIVÉ — Commun
        // ─────────────────────────────────────────────

        private Vector3 _lastPosition    = Vector3.zero;
        private int     _frozenFrameCount = 0;

        // ─────────────────────────────────────────────
        //  ANTI-CRASH
        // ─────────────────────────────────────────────

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

                    // Désactive TOUS les ItemTracking immédiatement
#if UNITY_2022_2_OR_NEWER
                    foreach (var tracker in FindObjectsByType<ItemTracking>(FindObjectsSortMode.None))
#else
                    foreach (var tracker in FindObjectsOfType<ItemTracking>())
#endif
                    {
#if STEAMVR_ENABLED
                        tracker.vr = null;
#endif
                        tracker.enabled = false;
                    }
                }
            };
        }
#endif

        // ─────────────────────────────────────────────
        //  UNITY LIFECYCLE
        // ─────────────────────────────────────────────

        void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            // Backend may be configured after Instantiate/OnEnable by the weapon spawner.
            InputSystem.onDeviceChange += OnInputDeviceChange;
#endif
        }

        void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            InputSystem.onDeviceChange -= OnInputDeviceChange;
#endif

#if STEAMVR_ENABLED
            vr = null;
#endif
#if ENABLE_INPUT_SYSTEM
            _openXRBound  = false;
            _openXRDevice = null;
            _selectedOpenXRSerial = "";
            _selectedOpenXRProduct = "";
#endif
        }

        void OnApplicationQuit()
        {
            _dead = true;
#if STEAMVR_ENABLED
            vr = null;
#endif
        }

        void Update()
        {
            if (_dead) return;

            switch (backend)
            {
                case TrackingBackend.SteamVR: UpdateSteamVR(); break;
                case TrackingBackend.OpenXR:  UpdateOpenXR();  break;
            }
        }

        // ─────────────────────────────────────────────
        //  STEAMVR
        // ─────────────────────────────────────────────

        void UpdateSteamVR()
        {
#if STEAMVR_ENABLED
            if (_dead) return;

            // Poll PARTAGÉ : une seule requête de poses par frame pour TOUS les ItemTracking
            // (au lieu d'une par instance). null = pas de session VR → on ne martèle pas.
            _poses = SteamVRBridge.GetPosesThisFrame();
            if (_poses == null)
            {
                SetTrackingState(false);
                return;
            }

            // vr nécessaire à FindTracker (classe device / serial). GetSystem est caché → quasi gratuit.
            if (vr == null) vr = SteamVRBridge.GetSystem();
            if (vr == null)
            {
                SetTrackingState(false);
                return;
            }

            if (autoFind)
                FindTracker();
            else
                foundIndex = trackerIndex;

            ApplyPose(foundIndex);
#else
            Debug.LogWarning("[ItemTracking] SteamVR backend sélectionné mais STEAMVR_ENABLED n'est pas défini.");
#endif
        }

#if STEAMVR_ENABLED
        void FindTracker()
        {
            if (vr == null || _dead) return;

            // Déjà verrouillé sur un device avec une pose valide : rien à refaire cette frame.
            // Le serial a déjà été validé lors du find initial (cf. boucle ci-dessous), donc on
            // sort AUSSI avec useSerialFilter actif → évite un re-scan des 64 devices + des allocs
            // GetStringTrackedDeviceProperty à chaque frame. Si la pose se perd, la condition
            // tombe et on relance la recherche.
            if (found && foundIndex != -1
                && (uint)foundIndex < 64
                && _poses[foundIndex].bPoseIsValid)
            {
                return;
            }

            for (uint i = 0; i < 64; i++)
            {
                if (_dead) return;

                if (vr.GetTrackedDeviceClass(i) == targetClass && _poses[i].bPoseIsValid)
                {
                    string deviceSerial = GetPropertyString(i,
                        ETrackedDeviceProperty.Prop_SerialNumber_String);

                    if (useSerialFilter && !string.IsNullOrEmpty(targetSerial))
                    {
                        if (deviceSerial != targetSerial) continue;
                    }

                    found = true;
                    foundIndex = (int)i;
                    foundSerial = deviceSerial;
                    foundModel = GetPropertyString(i,
                        ETrackedDeviceProperty.Prop_ModelNumber_String);
                    return;
                }
            }
            found = false;
        }

        string GetPropertyString(uint index, ETrackedDeviceProperty prop)
        {
            if (vr == null || _dead) return "";

            ETrackedPropertyError err = ETrackedPropertyError.TrackedProp_Success;
            _sb.Clear();
            vr.GetStringTrackedDeviceProperty(index, prop, _sb, 256, ref err);
            return _sb.ToString();
        }

        void ApplyPose(int index)
        {
            if (index < 0 || index >= _poses.Length || !_poses[index].bPoseIsValid || _poses[index].eTrackingResult != ETrackingResult.Running_OK)
            {
                SetTrackingState(false);
                return;
            }

            Transform targetT = (applyToParent && transform.parent != null)
                ? transform.parent : transform;

            HmdMatrix34_t m = _poses[index].mDeviceToAbsoluteTracking;
            Vector3 rawPos = new Vector3(m.m3, m.m7, -m.m11);

            Matrix4x4 mat = Matrix4x4.identity;
            mat[0, 0] = m.m0;  mat[0, 1] = m.m1;  mat[0, 2] = -m.m2;
            mat[1, 0] = m.m4;  mat[1, 1] = m.m5;  mat[1, 2] = -m.m6;
            mat[2, 0] = -m.m8; mat[2, 1] = -m.m9; mat[2, 2] = m.m10;

            Quaternion rawRot = mat.rotation;
            targetT.localPosition = rawPos + (rawRot * positionOffset);
            targetT.localRotation = rawRot * Quaternion.Euler(rotationOffset);
           
            
            
            
            if (rawPos == _lastPosition)
            {
                _frozenFrameCount++;
                if (_frozenFrameCount >= 3) SetTrackingState(false);
            }
            else
            {
                _frozenFrameCount = 0;
                SetTrackingState(true);
            }
            _lastPosition = rawPos;
        }
#endif

        // ─────────────────────────────────────────────
        //  OPENXR
        // ─────────────────────────────────────────────

        void UpdateOpenXR()
        {
#if OPENXR && ENABLE_INPUT_SYSTEM
            if (!_openXRBound || _openXRDevice == null || !_openXRDevice.added)
                TryBindOpenXRDevice();

            if (_openXRDevice == null || !_openXRDevice.added)
            {
                SetTrackingState(false);
                return;
            }

            ApplyPoseOpenXR();
#else
            SetTrackingState(false);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        void TryBindOpenXRDevice()
        {

            _openXRBound  = false;
            _openXRDevice = null;

            switch (openXRDeviceType)
            {
                case OpenXRDeviceType.LeftController:
                    _openXRDevice = FindXRController(XRController.leftHand);
                    break;

                case OpenXRDeviceType.RightController:
                    _openXRDevice = FindXRController(XRController.rightHand);
                    break;

                case OpenXRDeviceType.ViveTracker:
                    _openXRDevice = FindViveTracker(viveTrackerRole, _selectedOpenXRSerial, _selectedOpenXRProduct);
                    if (_openXRDevice != null)
                    {
                        _selectedOpenXRSerial = _openXRDevice.description.serial ?? "";
                        _selectedOpenXRProduct = _openXRDevice.description.product ?? "";
                    }
                    break;
            }

            _openXRBound = (_openXRDevice != null);
            found        = _openXRBound;
            foundIndex   = -1;
            foundSerial  = _openXRDevice?.description.serial ?? "";
            foundModel   = _openXRDevice?.description.product ?? "";

        }
#endif

#if ENABLE_INPUT_SYSTEM
        static InputDevice FindXRController(XRController hint)
        {

            return hint != null && hint.added ? hint : null;


        }
        
#endif
        
#if ENABLE_INPUT_SYSTEM
        static InputDevice FindViveTracker(ViveTrackerRole role, string serial = "", string product = "")
        {
            string rolePath = RoleToString(role);
            if (role != ViveTrackerRole.First && string.IsNullOrEmpty(rolePath)) return null;

            foreach (var dev in InputSystem.devices)
            {
                if (!(dev is ViveTrackerProfile.ViveTracker tracker)) continue;
                if (!string.IsNullOrEmpty(serial) && dev.description.serial != serial) continue;
                // OpenXR often exposes a role endpoint rather than a hardware serial.
                if (string.IsNullOrEmpty(serial) && !string.IsNullOrEmpty(product)
                    && dev.description.product != product) continue;
                if (role != ViveTrackerRole.First && !MatchesRole(dev, rolePath)) continue;
                if (TryReadTrackedPose(tracker, out _, out _)) return dev;
            }
            return null;
        }

        static bool MatchesRole(InputDevice device, string rolePath)
        {
            string expected = NormalizeRole(rolePath);
            if (NormalizeRole(device.description.product).Contains(expected)
                || NormalizeRole(device.displayName).Contains(expected)) return true;
            foreach (var usage in device.usages)
                if (NormalizeRole(usage.ToString()) == expected) return true;
            return false;
        }

        static string NormalizeRole(string value) =>
            (value ?? "").Replace("_", "").Replace(" ", "").Replace("-", "").ToLowerInvariant();

        static bool TryReadTrackedPose(TrackedDevice device, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            if (device == null || !device.added || !device.enabled) return false;
            const int poseFlags = (int)(UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation);

            // Vive Tracker : seule la PoseControl « devicePose » est alignée sur l'état envoyé par le
            // plugin OpenXR. devicePosition / deviceRotation (offsets 60/72 en dur dans le layout)
            // tombent sur IsTracked/TrackingState et hors de l'état : rotation nulle → jamais trouvé.
            if (device is ViveTrackerProfile.ViveTracker vive && vive.devicePose != null)
            {
                var pose = vive.devicePose;
                if (pose.isTracked.ReadValue() < 0.5f) return false;
                if ((pose.trackingState.ReadValue() & poseFlags) != poseFlags) return false;
                pos = pose.position.ReadValue();
                rot = pose.rotation.ReadValue();
            }
            else
            {
                if (device.isTracked.ReadValue() < 0.5f) return false;
                if ((device.trackingState.ReadValue() & poseFlags) != poseFlags) return false;
                pos = device.devicePosition.ReadValue();
                rot = device.deviceRotation.ReadValue();
            }
            return IsFinite(pos.x) && IsFinite(pos.y) && IsFinite(pos.z)
                && IsFinite(rot.x) && IsFinite(rot.y) && IsFinite(rot.z) && IsFinite(rot.w)
                && Quaternion.Dot(rot, rot) > 0.0001f;
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
#endif

        public static string RoleToString(ViveTrackerRole role)
        {
            switch (role)
            {
                case ViveTrackerRole.HandheldObject:  return "handheld_object";
                case ViveTrackerRole.LeftFoot:        return "left_foot";
                case ViveTrackerRole.RightFoot:       return "right_foot";
                case ViveTrackerRole.LeftShoulder:    return "left_shoulder";
                case ViveTrackerRole.RightShoulder:   return "right_shoulder";
                case ViveTrackerRole.LeftElbow:       return "left_elbow";
                case ViveTrackerRole.RightElbow:      return "right_elbow";
                case ViveTrackerRole.LeftKnee:        return "left_knee";
                case ViveTrackerRole.RightKnee:       return "right_knee";
                case ViveTrackerRole.Waist:           return "waist";
                case ViveTrackerRole.Chest:           return "chest";
                case ViveTrackerRole.Camera:          return "camera";
                case ViveTrackerRole.Keyboard:        return "keyboard";
                case ViveTrackerRole.LeftWrist:       return "left_wrist";
                case ViveTrackerRole.RightWrist:      return "right_wrist";
                case ViveTrackerRole.LeftAnkle:       return "left_ankle";
                case ViveTrackerRole.RightAnkle:      return "right_ankle";
                default:                              return "";
            }
        }

#if ENABLE_INPUT_SYSTEM
        void ApplyPoseOpenXR()
        {
            if (!TryReadTrackedPose(_openXRDevice as TrackedDevice, out Vector3 pos, out Quaternion rot))
            {
                SetTrackingState(false);
                return;
            }

            Transform targetT = (applyToParent && transform.parent != null) ? transform.parent : transform;
            targetT.localPosition = pos + (rot * positionOffset);
            targetT.localRotation = rot * Quaternion.Euler(rotationOffset);
            // A motionless gun can still have a valid tracked pose.
            SetTrackingState(true);
        }
#endif

#if ENABLE_INPUT_SYSTEM
        void OnInputDeviceChange(InputDevice dev, InputDeviceChange change)
        {

            // Unrelated devices must not steal a gun already bound to a tracker.
            if (dev == _openXRDevice && (change == InputDeviceChange.Removed
                || change == InputDeviceChange.Disconnected))
            {
                _openXRBound = false;
                found = false;
                SetTrackingState(false);
            }
        }
#endif

        // ─────────────────────────────────────────────
        //  COMMUN
        // ─────────────────────────────────────────────

        public void Rescan()
        {
            found        = false;
            foundIndex   = -1;
            foundModel   = "";
            foundSerial  = "";
#if ENABLE_INPUT_SYSTEM
            _openXRBound  = false;
            _openXRDevice = null;
            _selectedOpenXRSerial = "";
            _selectedOpenXRProduct = "";
#endif
            SetTrackingState(false);
        }

        private void SetTrackingState(bool newState)
        {
            if (isTracking == newState) return;

            isTracking = newState;

            if (isTracking)
                OnTrackingRestored?.Invoke();
            else
                OnTrackingLost?.Invoke();
        }
    }
}
