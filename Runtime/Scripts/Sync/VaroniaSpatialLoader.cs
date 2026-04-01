using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Charge NexSpatial.json au démarrage.
    /// </summary>
    public class VaroniaSpatialLoader : MonoBehaviour
    {
        // ─── Accès statique ───────────────────────────────────────────────────────

        /// <summary>Objet Spatial désérialisé. Cast en Spatial depuis le projet jeu.</summary>
        public static object Data { get; private set; }

        /// <summary>Déclenché quand le fichier est chargé avec succès.</summary>
        public static event Action OnLoaded;

        // ─────────────────────────────────────────────────────────────────────────

        private void Start() => Load();

        // ─── Chargement ───────────────────────────────────────────────────────────

        public void Load()
        {
            string path = Path.Combine(
                Application.persistentDataPath
                    .Replace(Application.companyName + "/" + Application.productName, "Varonia"),
                "NewSpatial.json"
            );

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[VBO Spatial] NewSpatial.json introuvable : {path}");
                return;
            }

            // Cherche la classe Spatial dans toutes les assemblies chargées
         

            try
            {
                string json = File.ReadAllText(path);
                 Data = JsonConvert.DeserializeObject<Spatial>(json);
                Debug.Log($"[VBO Spatial] Chargé → {path}");
                OnLoaded?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[VBO Spatial] Erreur lecture : {e.Message}");
            }
        }
    }
}
