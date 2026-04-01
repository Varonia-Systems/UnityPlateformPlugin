#if OPENXR


using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.XR;
#endif
using UnityEngine.Scripting;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.XR.OpenXR.Features.Interactions
{
#if UNITY_EDITOR
    [UnityEditor.XR.OpenXR.Features.OpenXRFeature(UiName = "Vive Tracker Profile",
        BuildTargetGroups = new[] { BuildTargetGroup.Standalone},
        Company = "HTC",
        Desc = "Allows for mapping input to HTC Vive Tracker.",
        DocumentationLink = "https://www.khronos.org/registry/OpenXR/specs/1.0/html/xrspec.html#XR_HTCX_vive_tracker_interaction",
        OpenxrExtensionStrings = extensionString,
        Version = "0.0.1",
        Category = UnityEditor.XR.OpenXR.Features.FeatureCategory.Interaction,
        FeatureId = featureId)]
#endif
    public class ViveTrackerProfile : OpenXRInteractionFeature
    {
        public const string featureId = "com.unity.openxr.feature.input.vivetrackerprofile";
        
#if ENABLE_INPUT_SYSTEM
    [Preserve, InputControlLayout(displayName = "Vive Tracker")]
    public class ViveTracker : TrackedDevice
    {
        [Preserve, InputControl(offset = 0, aliases = new[] { "device", "gripPose" })]
        public PoseControl devicePose { get; private set; }
        
        [Preserve]
        // [Preserve, InputControl(offset = 53)]
        new public ButtonControl isTracked { get; private set; }
        
        [Preserve]
        // [Preserve, InputControl(offset = 56)]
        new public IntegerControl trackingState { get; private set; }
        
        [Preserve, InputControl(offset = 60, aliases = new[] { "gripPosition" })]
        new public Vector3Control devicePosition { get; private set; }
        
        [Preserve, InputControl(offset = 72, aliases = new[] { "gripOrientation" })]
        new public QuaternionControl deviceRotation { get; private set; }
        
        protected override void FinishSetup()
        {
            base.FinishSetup();
            devicePose = GetChildControl<PoseControl>("devicePose");
            isTracked = GetChildControl<ButtonControl>("isTracked");
            trackingState = GetChildControl<IntegerControl>("trackingState");
            devicePosition = GetChildControl<Vector3Control>("devicePosition");
            deviceRotation = GetChildControl<QuaternionControl>("deviceRotation");
        }
    }
#endif

        public const string profile = "/interaction_profiles/htc/vive_tracker_htcx";
        
        public const string gripPose = "/input/grip/pose";

        private const string kDeviceLocalizedName = "VIVE Tracker";
        
        public const string extensionString = "XR_HTCX_vive_tracker_interaction";

        protected override bool OnInstanceCreate(ulong instance)
        {
            if (!OpenXRRuntime.IsExtensionEnabled(extensionString))
            {
                Debug.Log($"{extensionString} not supported!");
                return false;
            }
            
            return base.OnInstanceCreate(instance);
        }
        
        protected override void RegisterDeviceLayout()
        {
#if ENABLE_INPUT_SYSTEM
            InputSystem.InputSystem.RegisterLayout(typeof(ViveTracker),
                        matches: new InputDeviceMatcher()
                        .WithInterface(XRUtilities.InterfaceMatchAnyVersion)
                        .WithProduct(kDeviceLocalizedName));
#endif
        }
        
        protected override void UnregisterDeviceLayout()
        {
#if ENABLE_INPUT_SYSTEM
            InputSystem.InputSystem.RemoveLayout(typeof(ViveTracker).Name);
#endif
        }
        
        protected override void RegisterActionMapsWithRuntime()
        {
            ActionMapConfig actionMap = new ActionMapConfig()
            {
                name = "vivetracker",
                localizedName = kDeviceLocalizedName,
                desiredInteractionProfile = profile,
                manufacturer = "HTC",
                serialNumber = "",
                deviceInfos = new List<DeviceConfig>()
                {
                    // XR_NULL_PATH makes it crash?
                    // new DeviceConfig()
                    // {
                    //     characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                    //     userPath = "/user/vive_tracker_htcx/role/XR_NULL_PATH"
                    // },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/handheld_object"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/left_foot"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/right_foot"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/left_shoulder"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/right_shoulder"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/left_elbow"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/right_elbow"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/left_knee"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/right_knee"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/waist"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/chest"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/camera"
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.TrackedDevice),
                        userPath = "/user/vive_tracker_htcx/role/keyboard"
                    },
                },
                actions = new List<ActionConfig>()
                {
                    new ActionConfig()
                    {
                        name = "devicePose",
                        localizedName = "Device Pose",
                        type = ActionType.Pose,
                        usages = new List<string>()
                        {
                            "Device"
                        },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = gripPose,
                                interactionProfileName = profile,
                            }
                        }
                    },
                }
            };

            AddActionMap(actionMap);
        }
    }
}

#endif