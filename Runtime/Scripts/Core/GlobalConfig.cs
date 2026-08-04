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
        FOCUS3_VBS_CTRL = 4,
        FOCUS3_VBS_VaroniaGun = 3,
        FOCUS3_VBS_HK416 = 101,
        FOCUS3_VBS_Striker = 50,

        HandTracking = 5,

        PICO_VSVR_CTRL = 6,
        TRACKER = 60,
        PICO_VSVR_VaroniaGun = 70,
        PICO_VSVR_Striker = 80,
        PICO_VSVR_HK416 = 416,

        // Auto-detected VR controllers (per-side); reported by the dashboard.
        LeftController = 61,
        RightController = 62,

        Unknown = -1,
        PICO_VSVR_Glock = 417,
        VORTEX_WEAPON_FOCUS = 501,

        HMD = 777,
    }


    /// <summary>
    /// Décrit une arme typée (nouveau système multi-armes).
    /// L'index de cet item dans <see cref="GlobalConfig.Devices"/> correspond au
    /// weaponIndex utilisé par VaroniaInput / VaroniaWeaponTracking.
    /// </summary>
    [System.Serializable]
    public class WeaponBinding
    {
        /// <summary> Identifiant du contrôleur / modèle d'arme. </summary>
        public Controller Controller = Controller.Unknown;

        /// <summary>
        /// Identifiant unique du device : adresse MAC (abonnement MQTT) OU serial de tracking
        /// (tracker SteamVR type 'LHR-XXXXXXXX', ID OpenXR, etc.).
        /// Fusion des anciens champs SerialNumber + TrackingId.
        /// </summary>
        public string Identifier = "";

        /// <summary>
        /// Marque cette arme comme celle par défaut. Un seul <see cref="WeaponBinding"/> de la liste
        /// <see cref="GlobalConfig.Devices"/> peut être IsDefault à la fois (exclusivité gérée par l'éditeur).
        /// </summary>
        public bool IsDefault = false;

        /// <summary>
        /// Identifiant (<see cref="Identifier"/>) de l'arme "parent" de ce device.
        /// Vide = pas de parent (racine). Permet de construire une hiérarchie parent → enfants.
        /// </summary>
        public string LinkParent = "";

        /// <summary> Force un Steam device index spécifique (-1 = pas de forçage / auto). </summary>
        public int ForceSteamId = -1;
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

        [Header("Spatial")]
        /// <summary>
        /// Si true, tout le système spatial est neutralisé : VaroniaSync n'applique pas
        /// SyncPos / SyncQuaterion et n'instancie aucune boundary, et une boundary posée
        /// manuellement dans la scène ne construit rien.
        /// Par défaut false = fonctionnement normal.
        /// </summary>
        public bool DontUseSpatialSync = false;

        /// <summary>
        /// True si le spatial doit être ignoré (voir <see cref="DontUseSpatialSync"/>).
        /// Lecture défensive : renvoie false tant que la config n'est pas chargée.
        /// </summary>
        public static bool SpatialSyncDisabled =>
            BackOfficeVaronia.Instance != null
            && BackOfficeVaronia.Instance.config != null
            && BackOfficeVaronia.Instance.config.DontUseSpatialSync;
        
        
        
        
        
        /// <summary>
        /// Liste typée de devices. Un "papa" (racine, <see cref="WeaponBinding.LinkParent"/> vide)
        /// = une arme. Un enfant (LinkParent renseigné) = son tracker de suivi.
        /// L'index d'arme runtime NE correspond PAS à l'index dans cette liste : il est attribué
        /// dynamiquement par <see cref="VaroniaWeaponRegistry"/> (position parmi les papas).
        /// </summary>
        [Header("Devices (multi-arme)")]
        public List<WeaponBinding> Devices = new List<WeaponBinding>();

        /// <summary>Renvoie l'entrée <see cref="Devices"/> à l'index donné, ou null si hors bornes.</summary>
        public WeaponBinding GetWeaponBinding(int index)
        {
            if (Devices != null && index >= 0 && index < Devices.Count)
                return Devices[index];

            return null;
        }

        /// <summary>True si ce device est un "papa" (racine) : pas de <see cref="WeaponBinding.LinkParent"/>.</summary>
        public static bool IsParent(WeaponBinding d) => d != null && string.IsNullOrEmpty(d.LinkParent);

        /// <summary>
        /// Renvoie l'arme par défaut : le premier papa <see cref="WeaponBinding.IsDefault"/>,
        /// à défaut le premier papa de la liste. Null si aucun papa.
        /// </summary>
        public WeaponBinding GetDefaultWeapon()
        {
            if (Devices == null) return null;
            WeaponBinding first = null;
            foreach (var d in Devices)
            {
                if (!IsParent(d)) continue;
                if (first == null) first = d;
                if (d.IsDefault) return d;
            }
            return first;
        }

        /// <summary>
        /// Renvoie l'enfant tracker d'un papa : premier device dont <see cref="WeaponBinding.LinkParent"/>
        /// vaut <paramref name="parentIdentifier"/>. Null si aucun.
        /// </summary>
        public WeaponBinding GetTrackerChild(string parentIdentifier)
        {
            if (string.IsNullOrEmpty(parentIdentifier) || Devices == null) return null;
            foreach (var d in Devices)
                if (d != null && d.LinkParent == parentIdentifier) return d;
            return null;
        }

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
                // Garde-fou anti-crash : OpenVR.Init peut retourner un système "valide"
                // alors qu'aucun HMD n'est connecté (runtime SteamVR installé mais
                // casque débranché ou SteamVR pas lancé). Dans ce cas
                // GetStringTrackedDeviceProperty(0, ...) crash dans vrclient_x64.dll
                // (segfault natif, le try/catch C# ne peut pas l'attraper).
                // IsHmdPresent interroge juste le registry runtime — safe à appeler
                // sans HMD branché, retourne false si pas de casque.
                if (!OpenVR.IsHmdPresent()) return "";

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
            // Sonde OpenVR : échec attendu si SteamVR n'est pas lancé/présent. On retombe sur
            // InputDevices juste en dessous → silence volontaire, pas une erreur à signaler.
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