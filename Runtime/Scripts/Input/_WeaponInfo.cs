using UnityEngine;

namespace VBO_Ultimate.Runtime.Scripts.Input
{
    public enum TrackingType
    {
        None = 0,
        OpenVr_Trackeur=1,
        OpenVr_Controller=2,
        OpenXr_Controller=3,
        OpenXr_Trackeur=4,
    }


    public enum InputType
    {
      None = 0,
      Mqtt = 1,
      Stiker = 2,
      Controller = 3,
    }


    [CreateAssetMenu(fileName = "_WeaponInfo", menuName = "VBO/_WeaponInfo")]
    public class _WeaponInfo : ScriptableObject
    {
        
        [Header("Infos")] 
        public int ModelId;
        public string DisplayNameModel;

        
        [Header("SteamVR")] 
        public Vector3 postionOffset;
        public Vector3  rotationOffset;
        [RequireWeaponComponent]
        public GameObject prefabWeapon;
        
        [Header("OpenXR")] 
        [Label("Postion Offset")]
        public Vector3 postionOffset_openxr;
        [Label("Rotation Offset")]
        public Vector3  rotationOffset_openxr;
        [RequireWeaponComponent]   [Label("Prefab Weapon")]
        public GameObject prefabWeapon_openxr;
        
        
        [Header("Input Type")] 
        public InputType inputType;

    }
}