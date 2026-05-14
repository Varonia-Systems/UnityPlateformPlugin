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

        [Tooltip("Auto-rempli au runtime par VaroniaWeaponTracking quand il spawn ce prefab. " +
                 "Ne pas assigner à la main — le même prefab peut être référencé par plusieurs " +
                 "_WeaponInfo, c'est le spawner qui sait lequel utiliser.")]
        public _WeaponInfo WeaponInfo;

        public GameObject debugRender;

        public Transform beginRaycast;

        [HideInInspector]
        public ItemTracking trackingOpenVR;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            if (debugRender != null) debugRender.SetActive(false);

            VaroniaWeapon.Instance.currentweapons.Add(this);

            DebugModeOverlay.OnDebugChanged += OnSuperDebugChanged;

            // L'init dépendante du WeaponInfo se fait via Init() — appelé par
            // VaroniaWeaponTracking juste après le spawn pour que le bon SO soit
            // attribué (un prefab peut être référencé par plusieurs _WeaponInfo,
            // c'est le contexte de spawn qui tranche).
            //
            // Fallback : si Init n'est pas appelé (prefab posé en scene sans
            // VaroniaWeaponTracking), on fait quand même un best-effort à la fin
            // du frame pour pas crasher la ItemTracking config.
            StartCoroutine(WaitAndFallbackInit());
        }

        private System.Collections.IEnumerator WaitAndFallbackInit()
        {
            // Laisse VaroniaWeaponTracking le temps de call Init() dans la même frame
            yield return null;
            if (WeaponInfo != null) yield break;

            // Fallback : warning + tentative de récupération par nom de prefab
            Debug.LogWarning($"[_Weapon] '{name}' : WeaponInfo non assigné par le spawner. " +
                             "Fallback Resources lookup par nom — si plusieurs SOs matchent, " +
                             "le premier trouvé est utilisé (potentiellement le mauvais).");

            var allInfos = Resources.LoadAll<_WeaponInfo>("");
            string myName = gameObject.name.Replace("(Clone)", "").Trim();
            for (int i = 0; i < allInfos.Length; i++)
            {
                var info = allInfos[i];
                if (info == null) continue;
                if ((info.prefabWeapon != null && info.prefabWeapon.name == myName)
                 || (info.prefabWeapon_openxr != null && info.prefabWeapon_openxr.name == myName))
                {
                    Init(info);
                    yield break;
                }
            }
            Debug.LogError($"[_Weapon] '{name}' : aucun _WeaponInfo correspondant trouvé en fallback.");
        }

        /// <summary>
        /// Appelé par <see cref="VaroniaWeaponTracking"/> (ou tout autre spawner)
        /// juste après l'instanciation, pour binder le bon <see cref="_WeaponInfo"/>
        /// à cette instance. Idempotent : on peut re-appeler pour swap d'arme à
        /// la volée en play mode.
        /// </summary>
        public void Init(_WeaponInfo info)
        {
            WeaponInfo = info;
            if (info == null) return;

            // Transmet le DisplayNameModel à VaroniaInput pour l'arme correspondante
            var tracking = GetComponentInParent<VaroniaWeaponTracking>();
            int weaponIdx = tracking != null ? tracking.weaponIndex : 0;
            if (!string.IsNullOrEmpty(info.DisplayNameModel))
                VaroniaInput.SetDeviceData(weaponIdx, false, 0, 0f, 0, info.DisplayNameModel);

            // Applique les offsets de tracking depuis le SO
            var itemTracking = GetComponent<ItemTracking>();
            if (itemTracking != null)
            {
                itemTracking.positionOffset = info.postionOffset;
                itemTracking.rotationOffset = info.rotationOffset;
                trackingOpenVR = itemTracking;
            }
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
