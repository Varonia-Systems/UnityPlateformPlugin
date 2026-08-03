using UnityEngine;
using System.IO;
using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VaroniaBackOffice
{
    /// <summary>
    /// Central manager for the Varonia Back Office. 
    /// Handles configuration loading, singleton persistence, and global game start events.
    /// </summary>
    public class BackOfficeVaronia : MonoBehaviour
    {
        public static BackOfficeVaronia Instance { get; private set; }
        public static event Action OnConfigLoaded;
        /// <summary>Vrai dès que la config a été chargée (permet aux abonnés tardifs de rattraper l'événement).</summary>
        public static bool ConfigLoaded { get; private set; }
        public static event Action OnMovieChanged;
        public static void RaiseMovieChanged() => OnMovieChanged?.Invoke();

        /// <summary>Accès direct aux données Spatial désérialisées.</summary>
        public static Spatial Spatial => VaroniaSpatialLoader.Data as Spatial;

        [Header("Events")]
        public UnityEvent OnStartWithTuto;
        public UnityEvent OnStartSkipTuto;

        [Header("Status")]
        [SerializeField] private bool _isStarted = false;
        public bool IsStarted => _isStarted;
        
        [SerializeField] private bool _isTutoSkipped = false;
        public bool IsTutoSkipped => _isTutoSkipped;

        [Header("Camera")]
        [SerializeField] private Camera _mainCamera;
        public Camera MainCamera => _mainCamera;

        [Header("Rig")]
        [SerializeField] private Transform _rig;
        public Transform Rig
        {
            get => _rig;
            set => _rig = value;
        }

        private float _cameraNullTimer = 0f;
        private const float CameraWarningInterval = 5f;

        [Header("Settings")]
        public GlobalConfig config;
#if GAME_CONFIG
        public GameConfig gameConfig;
#endif
#if GAME_SCORE
        public GameScore gameScore;
#endif

        /// <summary>Champs dynamiques du JSON non déclarés dans GlobalConfig.</summary>
        public Dictionary<string, object> extraFields = new Dictionary<string, object>();
        
        /// <summary>Champs dynamiques du JSON non déclarés dans GameConfig.</summary>
        public Dictionary<string, object> gameConfigExtraFields = new Dictionary<string, object>();

        
        [HideInInspector]
        public MQTTVaronia mqttClient;

        private float _sceneLoadStartTime;
        private string _targetSceneName;
        private float _lastLoadDuration;
        private float _lastUpdateTime;

        // FDP Debug UI
        private float _fdpDebugTimer = 0f;
        private bool _showFdpDebug = false;
        private string _fdpPathFound = "";

        // Alerte "Controller ID not found" : une arme de Devices n'a pas d'enum Controller valide.
        private bool _showControllerWarning = false;
        private string _controllerWarningMsg = "";

        // Erreurs runtime remontées par les systèmes (armes, tracking…), affichées en bannière rouge.
        private readonly List<string> _runtimeErrors = new List<string>();

        // Notification "migration" (ancien format adapté) : bannière bleue (info), pas une erreur.
        private bool _showLegacyMigration = false;
        private string _legacyMigrationMsg = "";
        private float _legacyMigrationTimer = 0f; // auto-close (comme le FDP)

        /// <summary>
        /// Remonte une erreur runtime affichée comme bannière rouge in-game (même style que
        /// "Controller ID not found"). Dédoublonnée. Utilisé par ex. par VaroniaWeaponTracking.
        /// </summary>
        public static void ReportError(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            Debug.LogError("[BackOfficeVaronia] " + msg);
            if (Instance == null) return;
            if (!Instance._runtimeErrors.Contains(msg)) Instance._runtimeErrors.Add(msg);
        }

        // Surveillance runtime du DeviceMode : si la config change (ex. on devient spectateur),
        // on lève OnMovieChanged automatiquement pour que tous les overlays se réévaluent.
        private DeviceMode? _lastDeviceMode = null;

        // Merge FDP : ObjectCreationHandling.Replace → pour les listes/tableaux, le FDP
        // REMPLACE la liste existante au lieu d'y ajouter des éléments.
        private static readonly JsonSerializerSettings _fdpMergeSettings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        // ── Win32 : Minimize window ───────────────────────────────────────────────
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
        private const int SW_MINIMIZE = 6;
#endif

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            _lastUpdateTime = Time.realtimeSinceStartup;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // L'utilisateur revient des Settings Android. Si la permission est maintenant grantee,
            // on relance l'app pour que tout le pipeline (LoadConfig, etc.) repasse proprement.
            if (hasFocus && _waitingForAndroidPermission && VaroniaAndroidPermissions.HasAllFilesAccess())
            {
                _waitingForAndroidPermission = false;
                Debug.Log("[BackOfficeVaronia] Permission MANAGE_EXTERNAL_STORAGE accordee. Restart de l'app.");
                VaroniaAndroidPermissions.RestartApp();
            }
#endif
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            _sceneLoadStartTime = Time.realtimeSinceStartup;
            Debug.Log($"#[BackOfficeVaronia] Scene transition started (unloading: {scene.name})...");
            _lastUpdateTime = Time.realtimeSinceStartup;
        }

        private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            _targetSceneName = newScene.name;
            Debug.Log($"#[BackOfficeVaronia] Active scene change detected: {oldScene.name} -> {newScene.name}");
            _lastUpdateTime = Time.realtimeSinceStartup;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            float currentTime = Time.realtimeSinceStartup;
            _lastLoadDuration = currentTime - _sceneLoadStartTime;
            _targetSceneName = scene.name;

        //    Debug.Log($"#[BackOfficeVaronia] Scene Loaded: {scene.name} (Load took {_lastLoadDuration:F2}s)");

            _lastUpdateTime = currentTime;
        }

        private void Awake()
        {
            mqttClient = GetComponent<MQTTVaronia>();

            // Destroy() étant différé en fin de frame, il FAUT sortir explicitement sur un doublon :
            // sinon l'instance condamnée exécutait quand même LoadConfig() (réécriture de
            // GlobalConfig.json + OnConfigLoaded levé une seconde fois).
            if (!InitializeSingleton()) return;

            LoadConfig();
            CheckMainCamera();
            _lastUpdateTime = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            CheckMainCamera();
            CheckDeviceModeChanged();

            // Freeze detection logic
            float currentTime = Time.realtimeSinceStartup;
            float deltaTime = currentTime - _lastUpdateTime;
            
            if (deltaTime > 0.5f) // Threshold for freeze
            {
                Debug.Log($"#[BackOfficeVaronia] Main thread freeze detected: {deltaTime:F2}s");
            }
            _lastUpdateTime = currentTime;

            if (_fdpDebugTimer > 0)
            {
                _fdpDebugTimer -= Time.deltaTime;
                if (_fdpDebugTimer <= 0)
                {
                    _showFdpDebug = false;
                }
            }

            if (_legacyMigrationTimer > 0)
            {
                _legacyMigrationTimer -= Time.deltaTime;
                if (_legacyMigrationTimer <= 0)
                {
                    _showLegacyMigration = false;
                }
            }

            // Shortcut 'M' to minimize window
            if (IsMKeyDown())
            {
                MinimizeWindow();
            }
        }

        private bool IsMKeyDown()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.mKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.M);
#endif
        }

        private void MinimizeWindow()
        {
            Debug.Log("[BackOfficeVaronia] Minimizing window...");
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            IntPtr hWnd = GetActiveWindow();
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_MINIMIZE);
            }
            else
            {
                // Fallback via Process
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                ShowWindow(proc.MainWindowHandle, SW_MINIMIZE);
            }
#else
            Screen.fullScreen = false;
#endif
        }

        /// <summary>
        /// Détecte un changement de <see cref="GlobalConfig.DeviceMode"/> à l'exécution
        /// (ex. on passe joueur → spectateur) et lève <see cref="OnMovieChanged"/> pour que
        /// les overlays (info display, charts latence, FPS…) se réévaluent et se masquent.
        /// </summary>
        private void CheckDeviceModeChanged()
        {
            if (config == null) return;
            if (_lastDeviceMode != config.DeviceMode)
            {
                _lastDeviceMode = config.DeviceMode;
                RaiseMovieChanged();
            }
        }

        private void CheckMainCamera()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;

                if (_mainCamera == null)
                {
                    _cameraNullTimer += Time.deltaTime;
                    if (_cameraNullTimer >= CameraWarningInterval)
                    {
                        Debug.LogWarning("[BackOfficeVaronia] No MainCamera found! BackOfficeVaronia requires a MainCamera to function properly.");
                        _cameraNullTimer = 0f; // Reset to avoid log spam every frame after 5s
                    }
                }
                else
                {
                    _cameraNullTimer = 0f;
                }
            }
            else
            {
                _cameraNullTimer = 0f;
            }
        }

        /// <summary>Retourne false si cette instance est un doublon (elle est alors détruite) :
        /// l'appelant doit interrompre son initialisation.</summary>
        private bool InitializeSingleton()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return false;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            return true;
        }

        private void OnGUI()
        {
            if (!_showFdpDebug && !_showControllerWarning && !_showLegacyMigration && _runtimeErrors.Count == 0) return;

            float y = 50f;

            if (_showFdpDebug)
                DrawBanner(ref y,
                    "⚠  FICHIER FDP (OVERRIDE) DÉTECTÉ & CHARGÉ",
                    _fdpPathFound,
                    $"Closing in {_fdpDebugTimer:F1}s...",
                    new Color(1f, 0.60f, 0.10f, 1f)); // orange

            if (_showLegacyMigration)
                DrawBanner(ref y,
                    "♻  COMPATIBILITÉ LEGACY CONTROLLER",
                    _legacyMigrationMsg,
                    "Mets à jour ton GlobalConfig.json (Devices) pour retirer l'ancien format.",
                    new Color(0.35f, 0.62f, 1f, 1f)); // bleu = info

            if (_showControllerWarning)
                DrawBanner(ref y,
                    "⛔  CONTROLLER ID NOT FOUND",
                    _controllerWarningMsg,
                    "Vérifie le champ Controller (enum) de l'arme dans GlobalConfig.",
                    new Color(1f, 0.30f, 0.30f, 1f)); // rouge

            for (int i = 0; i < _runtimeErrors.Count; i++)
                DrawBanner(ref y,
                    "⛔  ERREUR",
                    _runtimeErrors[i],
                    null,
                    new Color(1f, 0.30f, 0.30f, 1f)); // rouge
        }

        /// <summary>Dessine une bannière d'alerte centrée en haut, et avance <paramref name="y"/> pour empiler.</summary>
        private void DrawBanner(ref float y, string title, string line1, string line2, Color titleColor)
        {
            Color colBg    = new Color(0.11f, 0.11f, 0.14f, 0.92f);
            Color colValue = new Color(0.92f, 0.92f, 0.95f, 1f);

            float width  = 560f;
            float height = 80f;
            float x = (Screen.width - width) / 2f;

            GUIStyle bgStyle = new GUIStyle(GUI.skin.box);
            Texture2D bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, colBg);
            bgTex.Apply();
            bgStyle.normal.background = bgTex;

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
            };
            labelStyle.normal.textColor = titleColor;

            GUIStyle pathStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                wordWrap = true,
            };
            pathStyle.normal.textColor = colValue;

            GUILayout.BeginArea(new Rect(x, y, width, height), bgStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(title, labelStyle);
            if (!string.IsNullOrEmpty(line1)) GUILayout.Label(line1, pathStyle);
            if (!string.IsNullOrEmpty(line2)) GUILayout.Label(line2, pathStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            y += height + 8f;
        }

        /// <summary>
        /// Loads the config from JSON. If the file doesn't exist, it creates a new one with default values.
        /// </summary>
        // Flag pour eviter de demander la permission en boucle si l'utilisateur revient sans avoir grante
        // Declaration guardee : ce flag n'est lu/ecrit que dans le bloc Android (cf. OnApplicationFocus
        // et LoadConfig), donc hors Android il declencherait un warning CS0414 "assigned but never used".
#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool _waitingForAndroidPermission = false;
#endif

        public void LoadConfig()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Verifier la permission MANAGE_EXTERNAL_STORAGE avant toute ecriture sous /storage/emulated/0/Varonia/
            if (!VaroniaAndroidPermissions.HasAllFilesAccess())
            {
                Debug.LogWarning("[BackOfficeVaronia] Permission MANAGE_EXTERNAL_STORAGE absente. Ouverture des Settings Android pour grant.");
                _waitingForAndroidPermission = true;
                VaroniaAndroidPermissions.RequestAllFilesAccess();
                return; // pas la peine de tenter d'ecrire, ca jettera UnauthorizedAccessException
            }
            string rootPath = "/storage/emulated/0/Varonia";
#else
            string rootPath = Application.persistentDataPath.Replace(
                Application.companyName + "/" + Application.productName, "Varonia");
#endif
            string configPath = Path.Combine(rootPath, "GlobalConfig.json");
            string fdpPath = Path.Combine(rootPath, "GlobalConfig.fdp");

            if (!Directory.Exists(rootPath)) Directory.CreateDirectory(rootPath);

            // 1. Charger ou créer la config de base
            if (File.Exists(configPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(configPath);
                    config = GlobalConfig.CreateFromJson(jsonContent);
                    Debug.Log($"#[BackOfficeVaronia] Config loaded from {configPath}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"#[BackOfficeVaronia] JSON Parse Error: {e.Message}");
                    config = new GlobalConfig();
                }
            }
            else
            {
                Debug.LogWarning("#[BackOfficeVaronia] Config file missing. Creating default GlobalConfig.json");
                config = new GlobalConfig();
                SaveConfig();
            }

            // 1.1 Charger GameConfig.json
#if GAME_CONFIG
            string gameConfigPath = Path.Combine(Application.persistentDataPath, "Config.json");
            string gameConfigFdpPath = Path.Combine(Application.persistentDataPath, "Config.fdp");
            
            if (File.Exists(gameConfigPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(gameConfigPath);
                    gameConfig = GameConfig.CreateFromJson(jsonContent);
                    
                    // Charger les champs dynamiques du GameConfig
                    gameConfigExtraFields = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent)
                                             ?? new Dictionary<string, object>();
                    
                    Debug.Log($"#[BackOfficeVaronia] GameConfig loaded from {gameConfigPath}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BackOfficeVaronia] GameConfig JSON Parse Error: {e.Message}");
                    gameConfig = new GameConfig();
                }
            }
            else
            {
                gameConfig = new GameConfig();
            }

            // 1.2 Si un Config.fdp existe, on merge par-dessus (le .fdp l'emporte pour GameConfig)
            if (File.Exists(gameConfigFdpPath))
            {
                try
                {
                    string fdpContent = File.ReadAllText(gameConfigFdpPath);
                    JsonConvert.PopulateObject(fdpContent, gameConfig, _fdpMergeSettings);
                    
                    // On merge aussi dans les extra fields pour la lecture à la volée
                    var fdpExtra = JsonConvert.DeserializeObject<Dictionary<string, object>>(fdpContent);
                    if (fdpExtra != null)
                    {
                        foreach (var kvp in fdpExtra)
                        {
                            gameConfigExtraFields[kvp.Key] = kvp.Value;
                        }
                    }
                    
                    Debug.LogWarning($"[BackOfficeVaronia] ⚠ Fichier FDP (override) GameConfig détecté et appliqué : {gameConfigFdpPath}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BackOfficeVaronia] GameConfig FDP Parse Error: {e.Message}");
                }
            }
#endif

            // 1.3 GameScore (Instance gérée par l'utilisateur)
#if GAME_SCORE
            if (gameScore == null) gameScore = new GameScore();
#endif

            // 2. Si un .fdp existe, on merge par-dessus (le .fdp l'emporte)
            if (File.Exists(fdpPath))
            {
                _fdpPathFound = fdpPath;
                _showFdpDebug = true;
                _fdpDebugTimer = 5f;
                try
                {
                    string fdpContent = File.ReadAllText(fdpPath);
                    JsonConvert.PopulateObject(fdpContent, config, _fdpMergeSettings);
                    Debug.LogWarning($"[BackOfficeVaronia] ⚠ Fichier FDP (override) GlobalConfig détecté et appliqué : {fdpPath}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BackOfficeVaronia] FDP Parse Error: {e.Message}");
                }
            }

#if GAME_CONFIG
            if (File.Exists(gameConfigFdpPath))
            {
                _fdpPathFound = gameConfigFdpPath;
                _showFdpDebug = true;
                _fdpDebugTimer = 5f;
            }
#endif

            // 3. Charger les champs dynamiques (GlobalConfig + FDP)
            try
            {
                string jsonContent = File.Exists(configPath) ? File.ReadAllText(configPath) : "{}";
                extraFields = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent)
                               ?? new Dictionary<string, object>();

                // Merge du FDP dans les extra fields
                if (File.Exists(fdpPath))
                {
                    string fdpContent = File.ReadAllText(fdpPath);
                    var fdpExtra = JsonConvert.DeserializeObject<Dictionary<string, object>>(fdpContent);
                    if (fdpExtra != null)
                    {
                        foreach (var kvp in fdpExtra)
                        {
                            extraFields[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch { extraFields = new Dictionary<string, object>(); }

            // Rétrocompat : si l'ancien système (Controller + WeaponMAC en JSON) est présent et que
            // Devices est vide, on le convertit en un papa + un tracker enfant auto-find.
            MigrateLegacyToDevices();

            ValidateControllers();

            // Snapshot stable des armes (papas) : le JSON ne bouge plus après ce point.
            VaroniaWeaponRegistry.Build(config);
            _runtimeErrors.Clear();

            ConfigLoaded = true;
            OnConfigLoaded?.Invoke();
        }

        /// <summary>
        /// Rétrocompat : si l'ancien système mono-arme est renseigné dans le JSON (clés "Controller"
        /// et/ou "WeaponMAC", lues via <see cref="extraFields"/> car les champs n'existent plus dans
        /// la classe) ET que <see cref="GlobalConfig.Devices"/> est vide/null, on convertit ça en :
        ///   • un papa qui garde le Controller + l'ancien WeaponMAC comme Identifier ;
        ///   • un enfant Tracker(60) (Identifier vide, ForceSteamId -1 → auto-find) lié au papa.
        /// </summary>
        private void MigrateLegacyToDevices()
        {
            _showLegacyMigration = false;
            _legacyMigrationMsg = "";

            if (config == null) return;
            if (config.Devices != null && config.Devices.Count > 0) return; // Devices déjà renseigné
            if (extraFields == null) return;

            object ctrlObj = null, macObj = null;
            foreach (var kv in extraFields)
            {
                if (string.Equals(kv.Key, "Controller", StringComparison.OrdinalIgnoreCase)) ctrlObj = kv.Value;
                else if (string.Equals(kv.Key, "WeaponMAC", StringComparison.OrdinalIgnoreCase)) macObj = kv.Value;
            }

            if (ctrlObj == null && macObj == null) return; // rien de legacy

            Controller legacyController = Controller.Unknown;
            try { if (ctrlObj != null) legacyController = (Controller)Convert.ToInt32(ctrlObj); }
            catch (Exception e)
            {
                // Migration silencieuse = config legacy non convertie sans que personne ne le sache.
                Debug.LogWarning($"[BackOfficeVaronia] Champ legacy 'Controller' illisible ('{ctrlObj}') : " +
                                 $"{e.Message}. Migration effectuée avec Controller.Unknown.");
            }

            string legacyMac = macObj != null ? macObj.ToString() : "";

            if (config.Devices == null) config.Devices = new List<WeaponBinding>();

            // Papa : conserve le Controller et l'ancien WeaponMAC comme Identifier.
            var papa = new WeaponBinding
            {
                Controller = legacyController,
                Identifier = legacyMac,
            };
            // Enfant tracker : Tracker(60), sans serial ni index → auto-find, lié au papa.
            var child = new WeaponBinding
            {
                Controller   = Controller.TRACKER,
                Identifier   = "",
                ForceSteamId = -1,
                LinkParent   = papa.Identifier,
            };

            config.Devices.Add(papa);
            config.Devices.Add(child);

            _showLegacyMigration = true;
            _legacyMigrationTimer = 5f; // se ferme après 5s
            _legacyMigrationMsg = $"Ancien format détecté et adapté automatiquement : arme {legacyController} " +
                                  $"(Identifier '{legacyMac}') + tracker auto-find.";

            Debug.LogWarning($"[BackOfficeVaronia] Migration legacy → Devices : arme Controller={legacyController} " +
                             $"Identifier='{legacyMac}' + tracker(60) auto-find.");
        }

        /// <summary>
        /// Vérifie que chaque arme de <see cref="GlobalConfig.Devices"/> possède un enum
        /// <see cref="Controller"/> valide. Si une arme n'a pas d'enum valide, affiche
        /// l'alerte "Controller ID not found".
        /// </summary>
        private void ValidateControllers()
        {
            _showControllerWarning = false;
            _controllerWarningMsg = "";
            if (config == null || config.Devices == null) return;

            var missing = new List<string>();
            for (int i = 0; i < config.Devices.Count; i++)
            {
                var d = config.Devices[i];
                if (d != null && HasNoControllerEnum(d.Controller))
                    missing.Add(string.IsNullOrEmpty(d.Identifier) ? $"Device #{i}" : $"Device #{i} ({d.Identifier})");
            }

            if (missing.Count > 0)
            {
                _controllerWarningMsg = string.Join("  •  ", missing);
                _showControllerWarning = true;
                Debug.LogError("[BackOfficeVaronia] Controller ID not found — " + _controllerWarningMsg);
            }
        }

        /// <summary>Vrai si la valeur n'est pas un enum Controller valide (Unknown ou valeur non définie).</summary>
        private static bool HasNoControllerEnum(Controller c)
            => c == Controller.Unknown || !System.Enum.IsDefined(typeof(Controller), c);

        /// <summary>
        /// Serializes the current config object and saves it to the persistent path.
        /// </summary>
        public void SaveConfig()
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                string rootPath = "/storage/emulated/0/Varonia";
                if (!Directory.Exists(rootPath)) Directory.CreateDirectory(rootPath);
#else
                string rootPath = Application.persistentDataPath.Replace(Application.companyName + "/" + Application.productName, "Varonia");
#endif
                string filePath = Path.Combine(rootPath, "GlobalConfig.json");

                string json = config.ToJson(); // Using the Newtonsoft method we discussed
                File.WriteAllText(filePath, json);
                
                Debug.Log($"[BackOfficeVaronia] Config saved successfully to {filePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BackOfficeVaronia] Failed to save config: {e.Message}");
            }
        }

        /// <summary>Une entrée de notation persistée dans Rating.json (date + note).</summary>
        [Serializable]
        public class RatingEntry
        {
            public string Date;   // ISO 8601 (DateTime.Now "o")
            public int    Rating; // 1..5
        }

        /// <summary>
        /// Ajoute une note joueur (date + rating) dans Rating.json, dans le dossier DU JEU
        /// (Application.persistentDataPath, à côté de Config.json).
        /// Le fichier est une LISTE : chaque appel AJOUTE une entrée (append), sans écraser l'historique.
        /// </summary>
        public void AppendRating(int rating)
        {
            try
            {
                string root = Application.persistentDataPath;
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                string path = Path.Combine(root, "Rating.json");

                List<RatingEntry> list = null;
                if (File.Exists(path))
                {
                    try { list = JsonConvert.DeserializeObject<List<RatingEntry>>(File.ReadAllText(path)); }
                    catch (Exception e) { Debug.LogWarning($"[Rating] Rating.json illisible, réinitialisé : {e.Message}"); }
                }
                if (list == null) list = new List<RatingEntry>();

                list.Add(new RatingEntry { Date = DateTime.Now.ToString("o"), Rating = rating });
                File.WriteAllText(path, JsonConvert.SerializeObject(list, Formatting.Indented));

                Debug.Log($"[Rating] Note {rating} ajoutée à {path} ({list.Count} entrée(s)).");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Rating] Écriture Rating.json impossible : {e.Message}");
            }
        }

        /// <summary>
        /// Cast robuste d'une valeur (réflexion ou JSON) vers T. Ne lève JAMAIS : renvoie
        /// <paramref name="defaultValue"/> si la valeur est null ou non convertible.
        /// </summary>
        private static T SafeCast<T>(object value, T defaultValue)
        {
            try
            {
                if (value == null) return defaultValue;
                if (value is T tv) return tv;
                if (typeof(T) == typeof(string)) return (T)(object)value.ToString();
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Récupère la valeur d'un champ du GlobalConfig par son nom (insensible à la casse).
        /// Exemple : GetConfigField&lt;string&gt;("ServerIP") ou GetConfigField&lt;int&gt;("MQTT_IDClient")
        /// </summary>
        /// <typeparam name="T">Le type attendu de la valeur.</typeparam>
        /// <param name="fieldName">Le nom du champ tel qu'il apparaît dans GlobalConfig.json.</param>
        /// <param name="defaultValue">Valeur retournée si le champ est introuvable.</param>
        /// <returns>La valeur du champ castée en T, ou defaultValue si introuvable.</returns>
        public T GetConfigField<T>(string fieldName, T defaultValue = default)
        {
            if (config == null)
            {
                Debug.LogWarning("[BackOfficeVaronia] Config not loaded.");
                return defaultValue;
            }
            if (string.IsNullOrEmpty(fieldName)) return defaultValue;

            var field = typeof(GlobalConfig).GetField(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (field != null)
                return SafeCast<T>(field.GetValue(config), defaultValue);

            var prop = typeof(GlobalConfig).GetProperty(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (prop != null)
                return SafeCast<T>(prop.GetValue(config), defaultValue);

            // Chercher dans les champs dynamiques du JSON (dico jamais supposé non-null).
            if (extraFields != null)
            {
                foreach (var key in extraFields.Keys)
                {
                    if (string.Equals(key, fieldName, StringComparison.OrdinalIgnoreCase))
                        return SafeCast<T>(extraFields[key], defaultValue);
                }
            }

            Debug.LogWarning($"[BackOfficeVaronia] Champ '{fieldName}' introuvable dans GlobalConfig.");
            return defaultValue;
        }

        /// <summary>
        /// Récupère la valeur d'un champ du GameConfig par son nom (insensible à la casse).
        /// </summary>
        public T GetGameConfigField<T>(string fieldName, T defaultValue = default)
        {
#if GAME_CONFIG
            if (gameConfig == null)
            {
                Debug.LogWarning("[BackOfficeVaronia] GameConfig not loaded.");
                return defaultValue;
            }
            if (string.IsNullOrEmpty(fieldName)) return defaultValue;

            var field = typeof(GameConfig).GetField(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (field != null)
                return SafeCast<T>(field.GetValue(gameConfig), defaultValue);

            var prop = typeof(GameConfig).GetProperty(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (prop != null)
                return SafeCast<T>(prop.GetValue(gameConfig), defaultValue);

            // Chercher dans les champs dynamiques du GameConfig (JSON uniquement)
            if (gameConfigExtraFields != null)
            {
                foreach (var key in gameConfigExtraFields.Keys)
                {
                    if (string.Equals(key, fieldName, StringComparison.OrdinalIgnoreCase))
                        return SafeCast<T>(gameConfigExtraFields[key], defaultValue);
                }
            }

            Debug.LogWarning($"[BackOfficeVaronia] Champ '{fieldName}' introuvable dans GameConfig.");
#else
            Debug.LogWarning("[BackOfficeVaronia] GameConfig is disabled (GAME_CONFIG define missing).");
#endif
            return defaultValue;
        }

        /// <summary>
        /// Récupère la valeur d'un champ du GameScore par réflexion.
        /// </summary>
        public T GetGameScoreField<T>(string fieldName, T defaultValue = default)
        {
#if GAME_SCORE
            if (gameScore == null)
            {
                Debug.LogWarning("[BackOfficeVaronia] GameScore instance is null.");
                return defaultValue;
            }
            if (string.IsNullOrEmpty(fieldName)) return defaultValue;

            var field = typeof(GameScore).GetField(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (field != null)
                return SafeCast<T>(field.GetValue(gameScore), defaultValue);

            var prop = typeof(GameScore).GetProperty(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (prop != null)
                return SafeCast<T>(prop.GetValue(gameScore), defaultValue);

            Debug.LogWarning($"[BackOfficeVaronia] Champ '{fieldName}' introuvable dans GameScore.");
#else
            Debug.LogWarning("[BackOfficeVaronia] GameScore is disabled (GAME_SCORE define missing).");
#endif
            return defaultValue;
        }

        public void TriggerStartGame(bool skipTuto)
        {
            if (_isStarted) return;
            _isStarted = true;
            _isTutoSkipped = skipTuto;
            
            if (skipTuto) OnStartSkipTuto?.Invoke();
            else OnStartWithTuto?.Invoke();
        }
    }
}