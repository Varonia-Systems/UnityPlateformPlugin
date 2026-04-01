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
        private bool _isCurrentlyLoading = false;
        private float _lastUpdateTime;

        // FDP Debug UI
        private float _fdpDebugTimer = 0f;
        private bool _showFdpDebug = false;
        private string _fdpPathFound = "";

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

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            _isCurrentlyLoading = true;
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

            _isCurrentlyLoading = false;
            _lastUpdateTime = currentTime;
        }

        private void Awake()
        {
            mqttClient = GetComponent<MQTTVaronia>();
            InitializeSingleton();
            LoadConfig();
            CheckMainCamera();
            _lastUpdateTime = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            CheckMainCamera();

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

        private void InitializeSingleton()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnGUI()
        {
            if (!_showFdpDebug) return;

            // Theme colors (from WorldSpaceDebugUI)
            Color colBg = new Color(0.11f, 0.11f, 0.14f, 0.92f);
            Color colGood = new Color(0.30f, 0.85f, 0.65f, 1f);
            Color colValue = new Color(0.92f, 0.92f, 0.95f, 1f);

            float width = 500f;
            float height = 80f;
            float x = (Screen.width - width) / 2f;
            float y = 50f;

            // Style for background
            GUIStyle bgStyle = new GUIStyle(GUI.skin.box);
            Texture2D bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, colBg);
            bgTex.Apply();
            bgStyle.normal.background = bgTex;

            // Style for label
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.fontSize = 16;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.normal.textColor = colGood;

            // Style for path
            GUIStyle pathStyle = new GUIStyle(GUI.skin.label);
            pathStyle.alignment = TextAnchor.MiddleCenter;
            pathStyle.fontSize = 12;
            pathStyle.normal.textColor = colValue;

            GUILayout.BeginArea(new Rect(x, y, width, height), bgStyle);
            GUILayout.FlexibleSpace();
            
            GUILayout.Label("OVERRIDE FILE DETECTED & LOADED", labelStyle);
            GUILayout.Label(_fdpPathFound, pathStyle);

            GUILayout.Label($"Closing in {_fdpDebugTimer:F1}s...", pathStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Loads the config from JSON. If the file doesn't exist, it creates a new one with default values.
        /// </summary>
        public void LoadConfig()
        {
            string rootPath = Application.persistentDataPath.Replace(
                Application.companyName + "/" + Application.productName, "Varonia");
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
                    JsonConvert.PopulateObject(fdpContent, gameConfig);
                    
                    // On merge aussi dans les extra fields pour la lecture à la volée
                    var fdpExtra = JsonConvert.DeserializeObject<Dictionary<string, object>>(fdpContent);
                    if (fdpExtra != null)
                    {
                        foreach (var kvp in fdpExtra)
                        {
                            gameConfigExtraFields[kvp.Key] = kvp.Value;
                        }
                    }
                    
                    Debug.Log($"[BackOfficeVaronia] GameConfig FDP overrides applied from {gameConfigFdpPath}");
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
                    JsonConvert.PopulateObject(fdpContent, config);
                    Debug.Log($"[BackOfficeVaronia] FDP overrides applied from {fdpPath}");
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

            OnConfigLoaded?.Invoke();
        }

        /// <summary>
        /// Serializes the current config object and saves it to the persistent path.
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                string rootPath = Application.persistentDataPath.Replace(Application.companyName + "/" + Application.productName, "Varonia");
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

            var field = typeof(GlobalConfig).GetField(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (field != null)
                return (T)System.Convert.ChangeType(field.GetValue(config), typeof(T));

            var prop = typeof(GlobalConfig).GetProperty(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (prop != null)
                return (T)System.Convert.ChangeType(prop.GetValue(config), typeof(T));

            // Chercher dans les champs dynamiques du JSON
            foreach (var key in extraFields.Keys)
            {
                if (string.Equals(key, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var val = extraFields[key];
                        if (val == null) return defaultValue;

                        if (typeof(T) == typeof(string))
                            return (T)(object)val.ToString();

                        return (T)Convert.ChangeType(val, typeof(T));
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[BackOfficeVaronia] Erreur de cast pour '{fieldName}' dans GlobalConfig: {e.Message}");
                        return defaultValue;
                    }
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

            var field = typeof(GameConfig).GetField(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (field != null)
                return (T)System.Convert.ChangeType(field.GetValue(gameConfig), typeof(T));

            var prop = typeof(GameConfig).GetProperty(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (prop != null)
                return (T)System.Convert.ChangeType(prop.GetValue(gameConfig), typeof(T));

            // Chercher dans les champs dynamiques du GameConfig (JSON uniquement)
            foreach (var key in gameConfigExtraFields.Keys)
            {
                if (string.Equals(key, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var val = gameConfigExtraFields[key];
                        if (val == null) return defaultValue;

                        if (typeof(T) == typeof(string))
                            return (T)(object)val.ToString();

                        return (T)Convert.ChangeType(val, typeof(T));
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[BackOfficeVaronia] Erreur de cast pour '{fieldName}' dans GameConfig: {e.Message}");
                        return defaultValue;
                    }
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

            var field = typeof(GameScore).GetField(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (field != null)
                return (T)System.Convert.ChangeType(field.GetValue(gameScore), typeof(T));

            var prop = typeof(GameScore).GetProperty(fieldName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (prop != null)
                return (T)System.Convert.ChangeType(prop.GetValue(gameScore), typeof(T));

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