using UnityEngine;
using VaroniaBackOffice;

namespace VBO_Ultimate.Runtime.Scripts.Input
{


    public class _Weapon : MonoBehaviour
    {
        [HideInInspector]
        public float BatteryLevel;
        [HideInInspector]
        public double RSSI;
        [HideInInspector]
        public long BOOT_Time;
        [HideInInspector]
        public bool IsConnected;

        public _WeaponInfo WeaponInfo;

        public GameObject debugRender;
        
        public Transform beginRaycast;
        
        [HideInInspector]
        public ItemTracking trackingOpenVR;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            if (debugRender!=null) debugRender.SetActive(false);

            VaroniaWeapon.Instance.currentweapons.Add(this);

            // Transmet le DisplayNameModel à VaroniaInput pour l'arme correspondante
            var tracking = GetComponentInParent<VaroniaWeaponTracking>();
            int weaponIdx = tracking != null ? tracking.weaponIndex : 0;
            if (WeaponInfo != null && !string.IsNullOrEmpty(WeaponInfo.DisplayNameModel))
                VaroniaInput.SetDeviceData(weaponIdx, false, 0, 0f, 0, WeaponInfo.DisplayNameModel);

            
                var A = GetComponent<ItemTracking>();
                A.positionOffset = WeaponInfo.postionOffset;
                A.rotationOffset = WeaponInfo.rotationOffset;
                
                trackingOpenVR = A;
                
            

            DebugModeOverlay.OnDebugChanged += OnSuperDebugChanged;
        }

        private void OnDestroy()
        {
            DebugModeOverlay.OnDebugChanged -= OnSuperDebugChanged;
        }

        private void OnSuperDebugChanged(bool active)
        {
            if (debugRender != null)
                debugRender.SetActive(active);
        }
    }
}
