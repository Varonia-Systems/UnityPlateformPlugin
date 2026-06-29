using UnityEngine;
#if STEAMVR_ENABLED
using Valve.VR;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VaroniaBackOffice
{
    public static class SteamVRBridge
    {
        #if STEAMVR_ENABLED
        
        private static bool _initializedByUs = false;
        private static CVRSystem _system;

        // Anti-spam : sans VR, OpenVR.Init() échoue (appel natif lourd/bloquant). On NE le
        // relance PAS à chaque frame — re-tentative seulement toutes les InitRetryCooldown s,
        // et on ne loggue l'indisponibilité qu'une fois.
        private const float InitRetryCooldown = 5f;
        private static float _nextInitAttempt = 0f;
        private static bool  _initFailLogged  = false;

        // Poses de tous les devices, pollées UNE fois par frame et partagées entre TOUS les
        // ItemTracking (évite N appels natifs GetDeviceToAbsoluteTrackingPose redondants/frame).
        private static readonly TrackedDevicePose_t[] _sharedPoses = new TrackedDevicePose_t[64];
        private static int _posesFrame = -1;

        public static CVRSystem GetSystem()
        {
            // On refuse toute (ré)initialisation pendant le teardown : évite de
            // recréer une session OpenVR au moment où l'éditeur quitte le Play Mode.
            if (IsShuttingDown) return null;

            // Si Unity a déjà initialisé OpenVR (SteamVR natif)
            if (OpenVR.System != null) return OpenVR.System;

            // Déjà initialisé par nous (background) → cache, pas de ré-init.
            if (_initializedByUs && _system != null) return _system;

            // Cooldown anti-spam quand l'init échoue (pas de VR) : 1 tentative / InitRetryCooldown s.
            if (Time.realtimeSinceStartup < _nextInitAttempt) return null;
            _nextInitAttempt = Time.realtimeSinceStartup + InitRetryCooldown;

            EVRInitError error = EVRInitError.None;
            _system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);

            if (error == EVRInitError.None && _system != null)
            {
                _initializedByUs = true;
                _initFailLogged  = false;
                Debug.Log("#<color=cyan>[VRBridge] Initialisé en mode BACKGROUND.</color>");
            }
            else
            {
                _system = null;
                if (!_initFailLogged)
                {
                    Debug.LogWarning($"#[VRBridge] SteamVR non disponible : {error} (nouvelle tentative dans {InitRetryCooldown:0}s).");
                    _initFailLogged = true;
                }
            }

            return _system;
        }

        /// <summary>
        /// Renvoie les poses de tous les devices, pollées UNE SEULE FOIS par frame (partagées).
        /// null si aucune session VR. À utiliser par tous les ItemTracking au lieu de poller
        /// chacun de leur côté.
        /// </summary>
        public static TrackedDevicePose_t[] GetPosesThisFrame()
        {
            var sys = GetSystem();
            if (sys == null) return null;

            if (_posesFrame != Time.frameCount)
            {
                _posesFrame = Time.frameCount;
                sys.GetDeviceToAbsoluteTrackingPose(
                    ETrackingUniverseOrigin.TrackingUniverseStanding, 0, _sharedPoses);
            }
            return _sharedPoses;
        }

        public static bool IsShuttingDown { get; private set; } = false;

        /// <summary>True si OpenVR a été initialisé par nous en mode Background (pas SteamVR natif).</summary>
        public static bool InitializedByUs => _initializedByUs;
        
        // Appelé à la sortie du Play Mode / fermeture.
        //
        // /!\ ON N'APPELLE PAS OpenVR.Shutdown() DANS L'ÉDITEUR /!\
        // Preuve via Editor.log : Shutdown() crash dans vrclient_x64.dll pendant
        // le teardown du Play Mode (SteamVR déjà lancé). On se contente de couper
        // l'accès managé : plus aucun script ne touche le pointeur natif, et la
        // session OpenVR background reste ouverte (réutilisée à la prochaine entrée
        // en Play Mode, ou nettoyée par l'OS à la fermeture du process).
        public static void SafeShutdown()
        {
            // Idempotent : plusieurs sources peuvent l'appeler à la sortie du Play Mode
            // (hook playModeStateChanged + OnApplicationQuit des composants). On ne
            // traite/loggue qu'une seule fois.
            if (IsShuttingDown) return;

            IsShuttingDown = true;
            _system = null;
            Debug.Log("#<color=yellow>[VRBridge] Soft shutdown : références managées libérées (pas de OpenVR.Shutdown).</color>");
        }
        
        
        
        // Réarme l'état au tout début de chaque session (Play Mode éditeur ET build),
        // y compris quand le rechargement de domaine est désactivé (les statics persistent).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            IsShuttingDown = false;
            _nextInitAttempt = 0f;   // re-tentative immédiate au début d'une nouvelle session
            _initFailLogged  = false;
            _posesFrame      = -1;
        }

#if UNITY_EDITOR
        // S'enregistre dès le chargement des assemblies éditeur : la fermeture propre
        // ne dépend PLUS de la présence d'un composant particulier dans la scène.
        [InitializeOnLoadMethod]
        static void InstallEditorHooks()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            EditorApplication.quitting -= SafeShutdown;
            EditorApplication.quitting += SafeShutdown;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                // Déclenché APRÈS le dernier Update() : plus aucun script ne tapera
                // dans le pointeur natif après ce point, on peut fermer sans risque.
                SafeShutdown();
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                IsShuttingDown = false;
            }
        }
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallQuitHook()
        {
            Application.wantsToQuit += OnWantsToQuit;
        }

        static bool OnWantsToQuit()
        {
            // Fermeture propre de la session OpenVR avant de quitter le build.
            SafeShutdown();
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            return false;
        }
#endif
        #endif

    }
    
}