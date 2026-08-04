using UnityEngine;
using UnityEngine.Rendering;
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

            // Dans la plupart des prefabs d'arme, BeginRayCast est authoré SOUS le DebugRender.
            // Ce sous-arbre étant désactivé hors mode debug, tout ce qui vise depuis l'arme
            // (laser de la notation, pointage UI…) ne marchait qu'avec le debug activé. On le
            // sort du DebugRender — en conservant sa pose monde — pour le rendre indépendant.
            if (beginRaycast != null && debugRender != null
                && beginRaycast != debugRender.transform
                && beginRaycast.IsChildOf(debugRender.transform))
            {
                beginRaycast.SetParent(transform, true);
            }

            FixDebugMaterialsForBuiltInPipeline();

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

        private static Shader s_builtInFallbackShader;

        /// <summary>
        /// La quasi-totalité des matières du DebugRender est authorée en Universal Render
        /// Pipeline/Lit. Dans un jeu resté en pipeline intégré ce shader n'existe pas : Unity
        /// retombe sur Hidden/InternalErrorShader et l'arme s'affiche en magenta. On leur
        /// réassigne alors le Standard du pipeline intégré.
        ///
        /// Rien n'est perdu au passage : ces matières viennent d'une conversion Standard → URP,
        /// et l'upgrader a laissé les anciennes propriétés (_MainTex, _BumpMap, _Metallic…) dans
        /// m_SavedProperties. Unity re-lie les propriétés sauvegardées par nom quand on change de
        /// shader, donc textures et réglages reviennent seuls — comme une réparation manuelle
        /// dans l'Inspector. Seuls les mots-clés sont à réarmer explicitement.
        ///
        /// Ne fait rien quand une SRP est active : les projets URP gardent leurs matières
        /// intactes. Travaille sur des instances (Renderer.materials), jamais sur les assets.
        /// </summary>
        private void FixDebugMaterialsForBuiltInPipeline()
        {
            if (debugRender == null) return;
            if (GraphicsSettings.currentRenderPipeline != null) return;

            if (s_builtInFallbackShader == null)
                s_builtInFallbackShader = Shader.Find("Standard") ?? Shader.Find("Varonia/Basic");
            if (s_builtInFallbackShader == null) return;

            foreach (var rend in debugRender.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rend.materials;
                bool changed = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];

                    if (m == null || m.shader == null)
                    {
                        mats[i] = new Material(s_builtInFallbackShader) { color = Color.grey };
                        changed = true;
                        continue;
                    }

                    if (m.shader.name != "Hidden/InternalErrorShader") continue;

                    m.shader = s_builtInFallbackShader;
                    RestoreBuiltInKeywords(m);
                    changed = true;
                }

                if (changed) rend.materials = mats;
            }
        }

        /// <summary>
        /// Le Standard n'active ses maps que si le mot-clé correspondant est armé, et ceux hérités
        /// d'URP ne portent pas les mêmes noms (_METALLICSPECGLOSSMAP vs _METALLICGLOSSMAP).
        /// </summary>
        private static void RestoreBuiltInKeywords(Material m)
        {
            if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null)
                m.EnableKeyword("_NORMALMAP");

            if (m.HasProperty("_MetallicGlossMap") && m.GetTexture("_MetallicGlossMap") != null)
                m.EnableKeyword("_METALLICGLOSSMAP");

            if (m.HasProperty("_ParallaxMap") && m.GetTexture("_ParallaxMap") != null)
                m.EnableKeyword("_PARALLAXMAP");

            if (m.HasProperty("_DetailAlbedoMap") && m.GetTexture("_DetailAlbedoMap") != null)
                m.EnableKeyword("_DETAIL_MULX2");

            if (m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") != null)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
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

            // Sans ça la liste garde une entrée morte après un changement de scène, et tout
            // consommateur qui lit currentweapons[0] (Rating3DUI, MixedRealityHandFollow…)
            // tombe sur un objet détruit.
            if (VaroniaWeapon.Instance != null)
                VaroniaWeapon.Instance.currentweapons.Remove(this);
        }

        private void OnSuperDebugChanged(bool active)
        {
            if (debugRender != null)
                debugRender.SetActive(active);
        }
    }
}
