using UnityEngine;
#if STEAMVR_ENABLED
using Valve.VR;
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
        
        // Appelé par le composant Unity à la fermeture
        public static void SafeShutdown()
        {
            IsShuttingDown = true;
            
            if(OpenVR.System == null)
                return;
            
            if (_initializedByUs)
            {
                OpenVR.Shutdown();
                _initializedByUs = false;
                _system = null;
                Debug.Log("#<color=yellow>[VRBridge] Shutdown propre effectué.</color>");
            }
        }
        
        
        
       #if !UNITY_EDITOR 
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallQuitHook()
        {
            Application.wantsToQuit += OnWantsToQuit;
        }


        static bool OnWantsToQuit()
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            return false; 
        }
#endif
        #endif

    }
    
}