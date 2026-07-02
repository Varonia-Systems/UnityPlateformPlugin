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
        private void OnDisable()
        {
            s_active.Remove(this);
            _forceLostCounter = 0;
            // Libère l'entrée réclamée pour qu'un futur spawn puisse la reprendre.
            if (weaponIndex >= 0) { VaroniaWeaponRegistry.Release(weaponIndex); weaponIndex = -1; }
        }

        // Slot runtime attribué au Start par VaroniaWeaponRegistry (position parmi les papas).
        // -1 = pas encore réclamé. N'est PLUS authoré dans l'inspector.
        public int weaponIndex { get; private set; } = -1;

        [Header("Sélection de l'arme")]
        [Tooltip("Si coché : cette arme prend le Controller choisi ci-dessous (1er papa non réclamé de ce type). " +
                 "Sinon : le device IsDefault, à défaut le premier papa libre de la liste.")]
        public bool forceId = false;
        [Tooltip("Controller ciblé quand 'forceId' est coché.")]
        public Controller forcedController = Controller.Unknown;

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
            yield return new WaitUntil(() => BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.config != null);

            // ── Réclame une arme (papa) dans le registry → slot stable + binding ──
            int slot = VaroniaWeaponRegistry.Claim(forceId, forcedController);
            if (slot < 0)
            {
                string what = forceId ? $"du Controller {forcedController}" : "libre";
                BackOfficeVaronia.ReportError($"Aucune arme {what} disponible dans GlobalConfig.Devices pour cette arme de scène ('{name}').");
                yield break;
            }
            weaponIndex = slot;

            // Alloue le slot d'input (grandit les tableaux + crée le device new-input-system).
            VaroniaInput.EnsureWeapon(slot);

            var binding = VaroniaWeaponRegistry.GetParent(slot);
            int controllerId = (int)binding.Controller;

            // ── Tracker enfant (suivi) ──
            //   • ForceSteamId >= 0 → SteamVR par index (autoFind off).
            //   • sinon Identifier renseigné → SteamVR par serial (autoFind + filtre serial).
            //   • sinon (-1 ET pas d'Identifier) → ancien système : auto-find, prend le 1er tracker.
            var tracker = VaroniaWeaponRegistry.GetTracker(slot);
            if (tracker != null)
            {
                overrideAutoFind  = true;
                overriddenBackend = ItemTracking.TrackingBackend.SteamVR;
#if STEAMVR_ENABLED
                bool hasIndex  = tracker.ForceSteamId >= 0;
                bool hasSerial = !string.IsNullOrEmpty(tracker.Identifier);

                // Défaut projet : le mode choisi n'utilise QUE son champ. Si ce champ n'est pas
                // renseigné → auto-find (l'autre champ est ignoré, pas de fallback croisé).
                var settings = VaroniaRuntimeSettings.Load();
                bool preferIndex = settings != null &&
                                   settings.defaultTrackerSource == TrackerTrackingSource.ForceSteamIndex;

                bool useIndex  =  preferIndex && hasIndex;
                bool useSerial = !preferIndex && hasSerial;

                if (useIndex)
                {
                    overriddenAutoFind        = false; // index précis → pas de scan
                    overriddenUseSerialFilter = false;
                    overriddenTrackerIndex    = tracker.ForceSteamId;
                    Debug.Log($"[VaroniaWeaponTracking] slot={slot} : tracker SteamVR par index {tracker.ForceSteamId}.");
                }
                else if (useSerial)
                {
                    overriddenAutoFind        = true;  // FindTracker() ne tourne que si autoFind == true
                    overriddenUseSerialFilter = true;
                    overriddenTargetSerial    = tracker.Identifier;
                    Debug.Log($"[VaroniaWeaponTracking] slot={slot} : tracker SteamVR par serial '{tracker.Identifier}'.");
                }
                else
                {
                    // Ni index ni serial → ancien système : auto-find du premier tracker (aucune erreur).
                    overriddenAutoFind        = true;
                    overriddenUseSerialFilter = false;
                    Debug.Log($"[VaroniaWeaponTracking] slot={slot} : tracker SteamVR auto-find (premier tracker).");
                }
#else
                if (tracker.ForceSteamId >= 0)
                    Debug.LogWarning($"[VaroniaWeaponTracking] slot={slot} : ForceSteamId défini mais STEAMVR_ENABLED off — ignoré.");
#endif
            }
            else
            {
                BackOfficeVaronia.ReportError($"Tracker introuvable pour l'arme #{slot} ({binding.Controller}). Ajoute un enfant tracker (LinkParent = Identifier de l'arme) dans GlobalConfig.Devices.");
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
