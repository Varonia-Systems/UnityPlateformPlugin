using System.Collections.Generic;
using UnityEngine;

#if STRIKER_LINK
using StrikerLink.Unity.Runtime.HapticEngine;
#endif
namespace VaroniaBackOffice
{
    /// <summary>Une scène d'un tableau "Advanced Change Scene" : nom de scène + vignette.</summary>
    [System.Serializable]
    public class AdvancedSceneEntry
    {
        public string    sceneName   = "";
        public string    description = "";
        public Texture2D image;
    }

    /// <summary>
    /// Un "tableau" de scènes affiché dans le menu F1 quand la valeur du champ sélecteur
    /// GameConfig (<see cref="VaroniaRuntimeSettings.advancedSelectorField"/>) vaut
    /// <see cref="matchValue"/>.
    /// </summary>
    [System.Serializable]
    public class AdvancedSceneBoard
    {
        public string                   label     = "";   // titre affiché (optionnel)
        public int                      matchValue = 0;    // valeur du sélecteur qui active ce tableau
        public List<AdvancedSceneEntry> scenes    = new List<AdvancedSceneEntry>();
    }

    /// <summary>
    /// Référence le prefab VaroniaManager pour l'auto-instanciation.
    /// Câblé automatiquement par VaroniaPackageWiring à l'ouverture de l'éditeur.
    /// </summary>
    public class VaroniaRuntimeSettings : ScriptableObject
    {
        private static VaroniaRuntimeSettings _cached;

        /// <summary>
        /// Charge les paramètres VaroniaRuntimeSettings. 
        /// Cherche exclusivement dans Resources (Varonia/VaroniaRuntimeSettings ou VaroniaRuntimeSettings).
        /// </summary>
        public static VaroniaRuntimeSettings Load()
        {
            if (_cached != null) return _cached;

            // Chercher dans Resources (pour le runtime et l'éditeur)
            _cached = Resources.Load<VaroniaRuntimeSettings>("Varonia/VaroniaRuntimeSettings");
            if (_cached == null) _cached = Resources.Load<VaroniaRuntimeSettings>("VaroniaRuntimeSettings");

            return _cached;
        }

        public GameObject managerPrefab;

        /// <summary>
        /// Si false, VaroniaAutoInit n'instanciera rien au démarrage.
        /// Configurable via Edit > Project Settings > Varonia Back Office.
        /// </summary>
        public bool autoInit = true;

        /// <summary>
        /// Version du package, copiée automatiquement depuis package.json par VaroniaPackageWiring.
        /// Accessible en build via Resources.
        /// </summary>
        public string packageVersion = "—";

        /// <summary>
        /// Si true, le define STEAMVR_ENABLED est ajouté automatiquement aux Scripting Define Symbols.
        /// Géré via Edit > Project Settings > Varonia Back Office.
        /// </summary>
        public bool enableSteamVR = false;

        /// <summary>
        /// Si true, le define GAME_CONFIG est ajouté automatiquement aux Scripting Define Symbols.
        /// Géré via Edit > Project Settings > Varonia Back Office.
        /// </summary>
        public bool useGameConfig = false;

        /// <summary>
        /// Si true, le define GAME_SCORE est ajouté automatiquement aux Scripting Define Symbols.
        /// Géré via Edit > Project Settings > Varonia Back Office.
        /// </summary>
        public bool useGameScore = false;

        /// <summary>
        /// If true, AutoSizing will automatically start on the first player input (Primary button).
        /// </summary>
        public bool autoStartCalibrationOnInput = true;

        /// <summary>
        /// Filtres d'exclusion pour VaroniaConsoleDisplay.
        /// Toute entrée de log dont le texte contient l'un de ces termes sera masquée.
        /// Configurable via Edit > Project Settings > Varonia Back Office.
        /// </summary>
        public List<string> consoleExcludeFilters = new List<string>();

        /// <summary>
        /// Effet haptique joué à l'initialisation du Striker (StrikerLink.Unity.Runtime requis).
        /// </summary>
#if STRIKER_LINK
        public HapticEffectAsset InitStrikerHaptic;
        public HapticLibraryAsset InitStrikerLibrary;

        /// <summary>
        /// Librairies haptiques supplémentaires chargées au démarrage du Striker,
        /// en plus de <see cref="InitStrikerLibrary"/>. Chaque entrée est ajoutée
        /// via StrikerHaptics.AddToLibrary (les doublons/null sont ignorés).
        /// </summary>
        public List<HapticLibraryAsset> InitStrikerLibraries = new List<HapticLibraryAsset>();
#endif

        [Header("Advanced Change Scene")]
        /// <summary>
        /// Si true, le menu F1 n'affiche plus la liste classique des scènes du build,
        /// mais les tableaux personnalisés (<see cref="sceneBoards"/>) avec vignettes.
        /// </summary>
        public bool advancedChangeScene = false;

        /// <summary>
        /// Nom du champ GameConfig (int ou enum, choisi par réflexion dans Project Settings)
        /// dont la valeur sélectionne le tableau à afficher. Vide / GameConfig absent →
        /// on affiche le premier tableau.
        /// </summary>
        public string advancedSelectorField = "";

        /// <summary>
        /// Tableaux de scènes du mode avancé. Celui dont <see cref="AdvancedSceneBoard.matchValue"/>
        /// égale la valeur courante du sélecteur est affiché (sinon le premier).
        /// </summary>
        public List<AdvancedSceneBoard> sceneBoards = new List<AdvancedSceneBoard>();

        [Header("Debug Scene Menu (Optional)")]
        /// <summary>
        /// Name of the GameObject to receive the scene selection event.
        /// </summary>
        [Tooltip("Name of the GameObject to receive the scene selection event.")]
        public string debugMenuTargetObjectName;

        /// <summary>
        /// Name of the method to call on the target object when a scene is selected.
        /// The method should accept a string parameter (the scene name).
        /// </summary>
        [Tooltip("Name of the method to call on the target object. It should accept a string (scene name).")]
        public string debugMenuTargetMethodName;
    }
}
