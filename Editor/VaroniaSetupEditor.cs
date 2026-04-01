using System.IO;
using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice
{
    /// <summary>
    /// S'exécute automatiquement à l'ouverture de l'éditeur.
    /// Crée VaroniaRuntimeSettings.asset dans Resources/ et le câble
    /// au prefab du package — aucune action manuelle requise.
    /// </summary>
    [InitializeOnLoad]
    internal static class VaroniaPackageWiring
    {
        private const string PackageRoot    = "Assets/VBO Ultimate"; // Chemin par défaut si non trouvé via AssetDatabase
        private const string SettingsAssetName = "VaroniaRuntimeSettings.asset";
        private const string TargetResourcesDir = "Assets/Resources/Varonia";
        private const string RootSettingsPath = TargetResourcesDir + "/" + SettingsAssetName;
        private const string PrefabName    = "[BACK OFFICE VARONIA]";

        static VaroniaPackageWiring()
        {
            // delayCall évite de tourner pendant un import en cours
            EditorApplication.delayCall += EnsureWired;
        }

        private static void EnsureWired()
        {
            // Refresh pour que Unity importe les dossiers créés côté filesystem
            // avant de tenter quoi que ce soit sur l'AssetDatabase
            AssetDatabase.Refresh();

            // Trouve le prefab n'importe où (Assets ou Packages)
            var guids = AssetDatabase.FindAssets($"{PrefabName} t:Prefab");
            if (guids.Length == 0)
            {
                Debug.LogError($"[VBO] Prefab '{PrefabName}' introuvable dans le projet.");
                return;
            }

            var prefabPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            // Recherche par type au lieu de chemin fixe
            VaroniaRuntimeSettings settings = null;
            var settingsGuids = AssetDatabase.FindAssets("t:VaroniaRuntimeSettings");
            string currentSettingsPath = null;

            if (settingsGuids.Length > 0)
            {
                // On privilégie celui dans Assets/Resources/Varonia
                foreach (var guid in settingsGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(SettingsAssetName) && path.Contains("/Resources/"))
                    {
                        currentSettingsPath = path;
                        break;
                    }
                    if (currentSettingsPath == null) currentSettingsPath = path;
                }
                settings = AssetDatabase.LoadAssetAtPath<VaroniaRuntimeSettings>(currentSettingsPath);
            }

            // Si trouvé dans un package (ou hors de Resources), on doit le déplacer/recréer dans Assets/Resources
            bool isNew = (settings == null);
            bool inPackage = !isNew && currentSettingsPath.StartsWith("Packages/");
            bool notInResources = !isNew && !currentSettingsPath.Contains("/Resources/");

            // Lit la version depuis package.json (recherche dynamique)
            string version = "—";
            var pkgGuids = AssetDatabase.FindAssets("package t:TextAsset");
            foreach (var pkgGuid in pkgGuids)
            {
                string p = AssetDatabase.GUIDToAssetPath(pkgGuid);
                if (p.EndsWith("package.json") && (p.Contains("VBO Ultimate") || p.Contains("com.varonia.backofficeultimate")))
                {
                    string fullPath = Path.GetFullPath(p);
                    if (File.Exists(fullPath))
                    {
                        try
                        {
                            var pkg = JsonUtility.FromJson<PackageJsonData>(File.ReadAllText(fullPath));
                            if (pkg != null && !string.IsNullOrEmpty(pkg.version))
                            {
                                version = pkg.version;
                                break;
                            }
                        }
                        catch { /* continue */ }
                    }
                }
            }

            // Déjà câblé correctement et dans Resources → rien à faire
            if (settings != null && !inPackage && !notInResources && settings.managerPrefab == prefab && settings.packageVersion == version) return;

            if (isNew || inPackage || notInResources)
            {
                if (inPackage || notInResources)
                {
                    Debug.LogWarning($"[VBO] VaroniaRuntimeSettings trouvé hors de Resources ({currentSettingsPath}). Création d'une copie dans {TargetResourcesDir} pour garantir le fonctionnement en build.");
                }

                // S'assurer que le dossier Assets/Resources/Varonia existe
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                    AssetDatabase.CreateFolder("Assets", "Resources");
                if (!AssetDatabase.IsValidFolder("Assets/Resources/Varonia"))
                    AssetDatabase.CreateFolder("Assets/Resources", "Varonia");

                // On évite d'écraser si on est juste en train de mettre à jour le câblage
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<VaroniaRuntimeSettings>();
                    settings.managerPrefab  = prefab;
                    settings.packageVersion = version;
                    AssetDatabase.CreateAsset(settings, RootSettingsPath);
                    Debug.Log($"[VBO] VaroniaRuntimeSettings créé dans Resources : {RootSettingsPath}");
                }
                else
                {
                    // Si on l'avait mais qu'il était mal placé, on pourrait le déplacer ou simplement en créer un nouveau
                    // Ici on préfère en créer un nouveau au bon endroit si celui d'avant n'était pas dans Resources
                    settings = ScriptableObject.CreateInstance<VaroniaRuntimeSettings>();
                    settings.managerPrefab  = prefab;
                    settings.packageVersion = version;
                    AssetDatabase.CreateAsset(settings, RootSettingsPath);
                    Debug.Log($"[VBO] VaroniaRuntimeSettings recréé au bon emplacement : {RootSettingsPath}");
                }
            }
            else
            {
                settings.managerPrefab  = prefab;
                settings.packageVersion = version;
                EditorUtility.SetDirty(settings);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[VBO] VaroniaManager câblé automatiquement via prefab : {prefabPath}");
        }
    }

    [System.Serializable]
    internal class PackageJsonData { public string version; }
}
