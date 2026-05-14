using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VaroniaBackOffice;

namespace VBO_Ultimate.Runtime.Scripts.Input
{
    public class VaroniaWeaponTracking : MonoBehaviour
    {
        // ─── Registry global (pour TrackingSuppressionZone & autres) ──────────────
        // HashSet<> alloc-free pour iterate quand on cherche les armes actives.
        private static readonly HashSet<VaroniaWeaponTracking> s_active = new HashSet<VaroniaWeaponTracking>();
        public static IReadOnlyCollection<VaroniaWeaponTracking> ActiveInstances => s_active;

        /// <summary>
        /// Override externe pour forcer le tracking en "lost" indépendamment du
        /// hardware. Utilisé par <see cref="TrackingSuppressionZone"/> (holster, caisse,
        /// underwater, etc.). Plusieurs sources peuvent s'additionner via le counter
        /// : appelle <see cref="AddForceLost"/>/<see cref="RemoveForceLost"/>.
        /// </summary>
        public bool ExternalForceLost => _forceLostCounter > 0;
        private int _forceLostCounter;

        public void AddForceLost()    { _forceLostCounter++; }
        public void RemoveForceLost() { if (_forceLostCounter > 0) _forceLostCounter--; }

        private void OnEnable()  { s_active.Add(this); }
        private void OnDisable() { s_active.Remove(this); _forceLostCounter = 0; }

        [Header("Weapon Index")]
        [Tooltip("Index de cette arme dans VaroniaInput (0 = première arme, 1 = deuxième, etc.).")]
        public int weaponIndex = 0;

        [Header("Force ID")]
        public bool forceId = false;
        public int forcedId = 0;

        [Header("Tracker")]
        public ItemTracking trackerFollower;

        [Header("Override AutoFind")]
        [Tooltip("Si activé, override autoFind du ItemTracking avec les paramètres ci-dessous.")]
        public bool overrideAutoFind = false;
        public enum TrackingParentMode {ThisTransform, DirectItemTracking }
        public TrackingParentMode parentTrackingMode = TrackingParentMode.ThisTransform;

        // ── SteamVR Override ──
#if STEAMVR_ENABLED
        public bool overriddenAutoFind = false;
        public string overriddenTargetSerial = "LHR-XXXXXXXX";
        public bool overriddenUseSerialFilter = false;
        public int overriddenTrackerIndex = 3;
        public Valve.VR.ETrackedDeviceClass overriddenTargetClass = Valve.VR.ETrackedDeviceClass.GenericTracker;
#endif

        // ── Backend Override ──
        public ItemTracking.TrackingBackend overriddenBackend = ItemTracking.TrackingBackend.OpenXR;

        // ── OpenXR Override ──
        public ItemTracking.OpenXRDeviceType overriddenOpenXRDeviceType = ItemTracking.OpenXRDeviceType.ViveTracker;
        public ItemTracking.ViveTrackerRole  overriddenViveTrackerRole  = ItemTracking.ViveTrackerRole.HandheldObject;

        [Header("État du tracking (lecture seule)")]
        [SerializeField] private bool trackingLost = false;

        
        public _Weapon weap;
        


        private IEnumerator Start()
        {
            yield return new WaitUntil(() => VaroniaWeapon.Instance != null);

            int controllerId;

            if (forceId)
            {
                controllerId = forcedId;
                Debug.Log($"[VaroniaWeaponTracking] Force ID enabled — using ID: {controllerId}");
            }
            else
            {
                yield return new WaitUntil(() => BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.config != null);

                // Nouveau système multi-armes : on prend le binding à l'index correspondant.
                // Si la liste est vide, GetWeaponBinding retombe sur l'ancien Controller (arme 0).
                var binding = BackOfficeVaronia.Instance.config.GetWeaponBinding(weaponIndex);
                if (binding != null)
                {
                    controllerId = (int)binding.Controller;

                    // Si ForceSteamId est défini (>= 0), on force le tracking SteamVR par index.
                    // Ça écrase les overrides Inspector au runtime — ApplyTrackerSettings les utilisera.
                    if (binding.ForceSteamId >= 0)
                    {
                        overrideAutoFind        = true;
                        overriddenBackend       = ItemTracking.TrackingBackend.SteamVR;
#if STEAMVR_ENABLED
                        overriddenTrackerIndex  = binding.ForceSteamId;
                        Debug.Log($"[VaroniaWeaponTracking] weaponIndex={weaponIndex} : override SteamVR trackerIndex={binding.ForceSteamId} depuis GlobalConfig.Devices.");
#else
                        Debug.LogWarning($"[VaroniaWeaponTracking] weaponIndex={weaponIndex} : ForceSteamId={binding.ForceSteamId} défini mais STEAMVR_ENABLED off — ignoré.");
#endif
                    }
                }
                else
                {
                    Debug.LogWarning($"[VaroniaWeaponTracking] Aucun WeaponBinding pour weaponIndex={weaponIndex} — vérifie GlobalConfig.Devices.");
                    controllerId = (int)BackOfficeVaronia.Instance.config.Controller;
                }
            }

            _WeaponInfo entry = VaroniaWeapon.Instance.GetWeaponById(controllerId);

            if (entry != null && entry.prefabWeapon != null)
            {
                GameObject spawned = Instantiate(entry.prefabWeapon, transform.position, transform.rotation, transform);
                Debug.Log($"#[VaroniaWeaponTracking] Weapon spawned for Controller ID: {controllerId}");

                weap = spawned.GetComponent<_Weapon>();

                // Bind le _WeaponInfo utilisé pour ce spawn → résout le cas "un même
                // prefab référencé par plusieurs SOs". Le _Weapon attend cet appel
                // (sinon il déclenche un fallback warning au frame suivant).
                if (weap != null) weap.Init(entry);

                if (trackerFollower == null)
                    trackerFollower = spawned.GetComponentInChildren<ItemTracking>();

                ApplyTrackerSettings();
            }
            else
            {
                Debug.LogWarning($"[VaroniaWeaponTracking] No weapon prefab found for Controller ID: {controllerId}");
            }
        }

        private void ApplyTrackerSettings()
        {
            if (trackerFollower == null) return;

            // Toujours appliquer le mode de parenté
            trackerFollower.applyToParent = (parentTrackingMode == TrackingParentMode.ThisTransform);

            if (!overrideAutoFind) return;

            trackerFollower.backend = overriddenBackend;

#if STEAMVR_ENABLED
            if (trackerFollower.backend == ItemTracking.TrackingBackend.SteamVR)
            {
                trackerFollower.autoFind          = overriddenAutoFind;
                trackerFollower.targetSerial      = overriddenTargetSerial;
                trackerFollower.useSerialFilter   = overriddenUseSerialFilter;
                trackerFollower.trackerIndex      = overriddenTrackerIndex;
                trackerFollower.targetClass       = overriddenTargetClass;
            }
#endif

            if (trackerFollower.backend == ItemTracking.TrackingBackend.OpenXR)
            {
                trackerFollower.openXRDeviceType = overriddenOpenXRDeviceType;
                trackerFollower.viveTrackerRole  = overriddenViveTrackerRole;
                trackerFollower.Rescan();
            }
        }

        private void Update()
        {
            if (trackerFollower != null)
            {
                trackingLost = !trackerFollower.isTracking || ExternalForceLost;
                // Propage l'override vers ItemTracking pour affichage dans son custom editor
                // (pas d'effet runtime — purement visuel/diagnostic).
                trackerFollower.externalForceLost = ExternalForceLost;
            }
        }
    }
}
