using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;
#if STEAMVR_ENABLED
using Valve.VR;
#endif

namespace VaroniaBackOffice
{
    
    
    public enum Controller
    {
        Unknown = -1,
        PICO_VSVR_CTRL = 6, 
        FOCUS3_VBS_VaroniaGun = 3, 
        FOCUS3_VBS_Striker = 50,
        FOCUS3_VBS_HK416 = 101,
        PICO_VSVR_VaroniaGun = 70,
        PICO_VSVR_Striker = 80,
        PICO_VSVR_HK416 = 416,
        VORTEX_WEAPON_FOCUS = 501,
    }
    
    
    
    /// <summary>
    /// Represents the global configuration for the Varonia application.
    /// Maps directly to the GlobalConfig.json file.
    /// </summary>
    [System.Serializable]
    public class GlobalConfig
    {
        [Header("Network")]
        /// <summary> The IP address of the main game server. </summary>
        public string ServerIP = "localhost"; 
        
        /// <summary> The IP address of the MQTT broker. </summary>
        public string MQTT_ServerIP = "localhost"; 
        
        /// <summary> Unique client identifier for the MQTT connection. </summary>
        public int MQTT_IDClient = 0; 

        [Header("Preferences")]
        /// <summary> Role of the device (e.g., Server_Player, Client_Spectator). </summary>
        public DeviceMode DeviceMode = DeviceMode.Server_Player; 
        
        /// <summary> Selected UI and localized content language. </summary>
        public string Language = "Fr"; 
        
        /// <summary> Player's dominant hand for input/VR. </summary>
        public MainHand MainHand = MainHand.Right;  
        
        /// <summary> Local display name for the player. </summary>
        public string PlayerName = "Varonia Player";


        [FormerlySerializedAs("hideMode")] public int HideMode;


        public bool Direct;
        
        
        
        
        
        [Header("Controller")]
        public Controller Controller = 0;

        [Header("VR")]
        /// <summary>
        /// Manual override for the detected VR headset name.
        /// When empty, the name is auto-detected via OpenVR (<c>Prop_ModelNumber_String</c>)
        /// or OpenXR (<c>InputDevices</c>). When set, it overrides detection AND drives
        /// which debug latency chart is shown ("Pico 4 Ultra" → VSVR/ALVR chart,
        /// "Vive Focus 3" → VBS chart).
        /// </summary>
        public string HeadsetName = "";

        // ─── Headset resolution ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the effective headset name: <see cref="HeadsetName"/> if set,
        /// otherwise auto-detected from OpenVR / OpenXR.
        /// </summary>
        public static string ResolveHeadsetName()
        {
            var cfg = BackOfficeVaronia.Instance != null ? BackOfficeVaronia.Instance.config : null;
            if (cfg != null && !string.IsNullOrWhiteSpace(cfg.HeadsetName))
                return cfg.HeadsetName.Trim();
            return AutoDetectHeadsetName();
        }

        private static string AutoDetectHeadsetName()
        {
#if STEAMVR_ENABLED
            try
            {
                var vr = SteamVRBridge.GetSystem();
                if (vr != null)
                {
                    var sb  = new System.Text.StringBuilder(256);
                    var err = ETrackedPropertyError.TrackedProp_Success;
                    vr.GetStringTrackedDeviceProperty(
                        0, ETrackedDeviceProperty.Prop_ModelNumber_String, sb, 256, ref err);
                    if (sb.Length > 0)
                        return RemapKnownAlias(sb.ToString());
                }
            }
            catch { }
#endif
            var headsets = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, headsets);
            if (headsets.Count > 0)
            {
                var hmd = headsets[0];
                string manufacturer = (hmd.manufacturer ?? "").Trim();
                string name         = (hmd.name ?? "").Trim();
                if (!string.IsNullOrEmpty(manufacturer) &&
                    !name.StartsWith(manufacturer, System.StringComparison.OrdinalIgnoreCase))
                    return $"{manufacturer} {name}";
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            return "—";
        }

        private static string RemapKnownAlias(string raw)
        {
            if (raw == "Miramar" || raw == "Oculus Quest2") return "Pico 4 Ultra";
            if (raw == "Vive VBStreaming Focus3")           return "Vive Focus 3";
            return raw;
        }

        /// <summary>True if the resolved headset is a Pico 4 Ultra (VSVR / ALVR streaming).</summary>
        public static bool IsPico4Ultra()
        {
            string n = ResolveHeadsetName();
            return n == "Pico 4 Ultra" || n == "Miramar" || n == "Oculus Quest2";
        }

        /// <summary>True if the resolved headset is a Vive Focus 3 (VBS streaming).</summary>
        public static bool IsViveFocus3()
        {
            string n = ResolveHeadsetName();
            return n == "Vive Focus 3" || n == "Vive VBStreaming Focus3";
        }

        /// <summary>
        /// Deserializes a JSON string into a GlobalConfig object using Newtonsoft.Json.
        /// </summary>
        /// <param name="jsonString">The raw JSON data.</param>
        /// <returns>A populated GlobalConfig object or null if deserialization fails.</returns>
        public static GlobalConfig CreateFromJson(string jsonString)
        {
            try 
            {
                return JsonConvert.DeserializeObject<GlobalConfig>(jsonString);
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[GlobalConfig] Deserialization Error: {e.Message}");
                return null; 
            }
        }
        
        
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }
}