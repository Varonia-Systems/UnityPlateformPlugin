using UnityEngine;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Instancie automatiquement le prefab VaroniaManager avant le chargement
    /// de toute scène. Équivalent d'un Unreal GameSubsystem — aucun setup requis.
    /// </summary>
    internal static class VaroniaAutoInit
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Déjà actif (singleton DontDestroyOnLoad)
            if (BackOfficeVaronia.Instance != null) return;

            var settings = VaroniaRuntimeSettings.Load();

            if (settings == null || settings.managerPrefab == null)
            {
                Debug.LogError(
                    "[VBO] VaroniaRuntimeSettings introuvable ou prefab non assigné.\n" +
                    "Redémarrez l'éditeur pour relancer le câblage automatique."
                );
                return;
            }

            if (!settings.autoInit) return;

            var go  = Object.Instantiate(settings.managerPrefab);
            go.name = settings.managerPrefab.name;
        }
    }
}
