// Helper Android pour la permission MANAGE_EXTERNAL_STORAGE.
// Cette permission ne peut PAS etre demandee via le dialog runtime classique.
// Il faut rediriger l'utilisateur vers la page Settings dediee via un Intent.

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace VaroniaBackOffice
{
    public static class VaroniaAndroidPermissions
    {
        // Flags Android pour relancer l'app proprement (FLAG_ACTIVITY_NEW_TASK | FLAG_ACTIVITY_CLEAR_TASK)
        private const int IntentFlagNewTask   = 0x10000000;
        private const int IntentFlagClearTask = 0x00008000;

        /// <summary>
        /// True si l'app a la permission MANAGE_EXTERNAL_STORAGE (acces a tout le storage externe).
        /// </summary>
        public static bool HasAllFilesAccess()
        {
            try
            {
                using (var env = new AndroidJavaClass("android.os.Environment"))
                {
                    return env.CallStatic<bool>("isExternalStorageManager");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VaroniaAndroidPermissions] HasAllFilesAccess error : {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ouvre la page Settings "All files access" pour cette app.
        /// L'utilisateur doit activer le toggle manuellement.
        /// L'app passe en pause pendant ce temps. Au retour, check via HasAllFilesAccess().
        /// </summary>
        public static void RequestAllFilesAccess()
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    string packageName = activity.Call<string>("getPackageName");

                    using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                    using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + packageName))
                    using (var intent = new AndroidJavaObject("android.content.Intent",
                        "android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION"))
                    {
                        intent.Call<AndroidJavaObject>("setData", uri);
                        activity.Call("startActivity", intent);
                    }
                }
                Debug.Log("[VaroniaAndroidPermissions] Settings 'All files access' ouvert pour l'utilisateur.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VaroniaAndroidPermissions] RequestAllFilesAccess error : {e.Message}");
            }
        }

        /// <summary>
        /// Relance l'app : start un nouvel intent du package + kill le process courant.
        /// Utile apres que l'utilisateur ait grante MANAGE_EXTERNAL_STORAGE.
        /// </summary>
        public static void RestartApp()
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    string packageName = activity.Call<string>("getPackageName");

                    using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
                    using (var intent = pm.Call<AndroidJavaObject>("getLaunchIntentForPackage", packageName))
                    {
                        intent.Call<AndroidJavaObject>("addFlags", IntentFlagNewTask | IntentFlagClearTask);
                        activity.Call("startActivity", intent);
                    }
                }

                using (var process = new AndroidJavaClass("android.os.Process"))
                {
                    int pid = process.CallStatic<int>("myPid");
                    process.CallStatic("killProcess", pid);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VaroniaAndroidPermissions] RestartApp error : {e.Message}");
            }
        }
    }
}
#endif
