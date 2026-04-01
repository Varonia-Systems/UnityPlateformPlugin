using UnityEngine;
using System;
using System.Runtime.InteropServices;
using System.IO;

namespace VaroniaBackOffice
{
    public static class OpenVRLoader
    {
        #if STEAMVR_ENABLED &&  !OFFICIELOPENVR
        
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Init()
        {
            
            
                
            
            string dllName = "openvr_api.dll";
            
            // 1. Chemin si installé via UPM (Packages/)
            string packagePath = Path.GetFullPath($"Packages/com.varonia.backofficeultimate/Runtime/Plugins/x86_64/{dllName}");
            
            // 2. Chemin si copié manuellement (Assets/)
            string assetsPath = Path.Combine(Application.dataPath, "VBO Ultimate/Plugins/x86_64/", dllName);

            string finalPath = File.Exists(packagePath) ? packagePath : assetsPath;

            if (File.Exists(finalPath))
            {
                IntPtr handle = LoadLibrary(finalPath);
                if (handle != IntPtr.Zero)
                    Debug.Log($"<color=green>[VRBridge] DLL chargée de force : {finalPath}</color>");
                else
                    Debug.LogError($"[VRBridge] Échec LoadLibrary sur : {finalPath}");
            }
            else
            {
                Debug.LogWarning("[VRBridge] DLL introuvable dans les dossiers standards. Unity essaiera le chargement par défaut.");
            }
        }
        
        #endif
    }
}