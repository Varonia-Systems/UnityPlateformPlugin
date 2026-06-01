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
        
        public static CVRSystem GetSystem()
        {
            // On refuse toute (ré)initialisation pendant le teardown : évite de
            // recréer une session OpenVR au moment où l'éditeur quitte le Play Mode.
            if (IsShuttingDown) return null;

            // Si Unity a déjà initialisé OpenVR (SteamVR natif)
            if (OpenVR.System != null) return OpenVR.System;
            
            // Sinon, on initialise en mode Background (pour OpenXR)
            if (_system == null && !_initializedByUs)
            {
                EVRInitError error = EVRInitError.None;
                _system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
                
                if (error == EVRInitError.None)
                {
                    _initializedByUs = true;
                    Debug.Log("#<color=cyan>[VRBridge] Initialisé en mode BACKGROUND.</color>");
                }
                else
                {
                    Debug.LogWarning("#[VRBridge] SteamVR non disponible : " + error);
                }
            }
            
            
            
            return _system;
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