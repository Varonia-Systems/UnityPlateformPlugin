using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace VaroniaBackOffice
{
    public class DeviceMQTTIdentifierFinder : EditorWindow
    {
        private string _brokerAddress = "localhost";
        private MqttClient _client;
        private bool _isScanning = false;
        private string _foundMac = "";
        private Dictionary<string, (bool primary, bool secondary)> _deviceStates = new Dictionary<string, (bool, bool)>();

        // ── Devices cibles (depuis GlobalConfig.Devices) : on écrit le MAC trouvé dans l'Identifier du device choisi ──
        private string[] _deviceLabels = new string[0];
        private int      _deviceCount  = 0;
        private int      _selectedDeviceIndex = 0;

        // ── Mode de détection ──
        private enum FindMode { PrimarySecondary = 0, PrimaryOnly = 1 }
        private FindMode _mode = FindMode.PrimarySecondary;
        private static readonly string[] ModeLabels =
        {
            "Primary + Secondary (recommandé)",
            "Tir principal seul (risque d'erreur +)"
        };

        // Styles & Colors
        private static readonly Color colBg = new Color(0.11f, 0.11f, 0.14f, 1f);
        private static readonly Color colCard = new Color(0.15f, 0.15f, 0.19f, 1f);
        private static readonly Color colAccent = new Color(0.30f, 0.85f, 0.65f, 1f);
        private static readonly Color colError = new Color(1f,    0.40f, 0.40f, 1f);
        private static readonly Color colTextPrimary = new Color(0.92f, 0.92f, 0.95f, 1f);
        private static readonly Color colTextSecond = new Color(0.55f, 0.55f, 0.62f, 1f);

        private GUIStyle _headerStyle;
        private GUIStyle _cardStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _labelStyle;
        private bool _stylesInitialized = false;

        [MenuItem("Varonia/Find Device MQTT Identifier")]
        public static void ShowWindow()
        {
            var window = GetWindow<DeviceMQTTIdentifierFinder>("Device MQTT Identifier Finder");
            window.minSize = new Vector2(350, 250);
            window.Show();
        }

        [MenuItem("Varonia/Find Device MQTT Identifier", true)]
        public static bool ValidateShowWindow()
        {
            try { return File.Exists(GetConfigPath()); }
            catch { return false; }
        }

        // Chemin du GlobalConfig.json actif (dossier Varonia).
        private static string GetConfigPath()
        {
            string rootPath = Application.persistentDataPath.Replace(
                Application.companyName + "/" + Application.productName, "Varonia");
            return Path.Combine(rootPath, "GlobalConfig.json");
        }

        private void OnEnable()
        {
            LoadBrokerAddress();
            LoadDevices();
            _stylesInitialized = false;
        }

        // Charge la liste GlobalConfig.Devices pour alimenter le dropdown de sélection.
        private void LoadDevices()
        {
            _deviceLabels = new string[0];
            _deviceCount  = 0;
            try
            {
                string configPath = GetConfigPath();
                if (!File.Exists(configPath)) return;

                var root = JObject.Parse(File.ReadAllText(configPath));
                if (!(root["Devices"] is JArray devices)) return;

                _deviceCount  = devices.Count;
                _deviceLabels = new string[devices.Count];
                for (int i = 0; i < devices.Count; i++)
                {
                    var d = devices[i] as JObject;
                    string ctrlName = d?["Controller"]?.ToString() ?? "?";
                    if (int.TryParse(ctrlName, out int cv) && Enum.IsDefined(typeof(Controller), cv))
                        ctrlName = ((Controller)cv).ToString();
                    string id = d?["Identifier"]?.ToString() ?? "";
                    _deviceLabels[i] = string.IsNullOrEmpty(id)
                        ? $"#{i}  {ctrlName}"
                        : $"#{i}  {ctrlName}  ({id})";
                }
                if (_selectedDeviceIndex >= _deviceCount) _selectedDeviceIndex = 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DeviceMQTTIdentifierFinder] Lecture des Devices impossible : {e.Message}");
            }
        }

        private void OnDisable()
        {
            StopScan();
        }

        private void LoadBrokerAddress()
        {
            // Essayer de charger depuis BackOfficeVaronia.Instance si en mode Play
            if (Application.isPlaying && BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.config != null)
            {
                _brokerAddress = BackOfficeVaronia.Instance.config.MQTT_ServerIP;
                return;
            }

            // Sinon charger manuellement le fichier JSON
            try
            {
                string rootPath = Application.persistentDataPath.Replace(Application.companyName + "/" + Application.productName, "Varonia");
                string configPath = Path.Combine(rootPath, "GlobalConfig.json");

                if (File.Exists(configPath))
                {
                    string jsonContent = File.ReadAllText(configPath);
                    var cfg = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                    if (cfg != null && cfg.TryGetValue("MQTT_ServerIP", out object ip))
                    {
                        _brokerAddress = ip.ToString();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DeviceMQTTIdentifierFinder] Could not load MQTT_ServerIP from file: {e.Message}");
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                normal = { textColor = colTextPrimary },
                alignment = TextAnchor.MiddleCenter,
                margin = new RectOffset(0, 0, 10, 10)
            };

            _cardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(15, 15, 15, 15),
                margin = new RectOffset(10, 10, 10, 10),
                normal = { background = MakeTex(2, 2, colCard) }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                fixedHeight = 40,
                margin = new RectOffset(5, 5, 10, 10)
            };

            _labelStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                normal = { textColor = colTextSecond }
            };

            _stylesInitialized = true;
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i) pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void OnGUI()
        {
            InitStyles();

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colBg);

            EditorGUILayout.BeginVertical();
            
            GUILayout.Label("DEVICE MQTT IDENTIFIER FINDER", _headerStyle);

            EditorGUILayout.BeginVertical(_cardStyle);
            
            EditorGUILayout.LabelField("Broker IP:", _brokerAddress, EditorStyles.boldLabel);
            GUILayout.Space(5);

            // ── Device cible : où écrire le MAC trouvé (Identifier) ──
            using (new EditorGUI.DisabledScope(_isScanning))
            {
                EditorGUILayout.BeginHorizontal();
                if (_deviceCount > 0)
                {
                    _selectedDeviceIndex = EditorGUILayout.Popup("Device cible", _selectedDeviceIndex, _deviceLabels);
                }
                else
                {
                    EditorGUILayout.LabelField("Device cible", "— aucun —");
                }
                if (GUILayout.Button("↻", GUILayout.Width(26), GUILayout.Height(18))) LoadDevices();
                EditorGUILayout.EndHorizontal();
            }
            if (_deviceCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "Aucun device dans GlobalConfig.Devices. Ajoute au moins une arme dans l'éditeur GlobalConfig, puis ↻.",
                    MessageType.Warning);
            }
            GUILayout.Space(5);

            // ── Mode de détection (verrouillé pendant le scan) ──
            using (new EditorGUI.DisabledScope(_isScanning))
            {
                _mode = (FindMode)EditorGUILayout.Popup("Mode", (int)_mode, ModeLabels);
            }
            if (_mode == FindMode.PrimaryOnly)
            {
                EditorGUILayout.HelpBox(
                    "Écoute uniquement le tir principal : détection plus rapide, mais risque de faux " +
                    "positif plus élevé (toute arme qui tire pendant le scan sera prise).",
                    MessageType.Warning);
            }
            GUILayout.Space(5);

            EditorGUILayout.LabelField("Instructions:", EditorStyles.boldLabel);
            string step2 = _mode == FindMode.PrimaryOnly
                ? "2. Appuyez sur la gâchette principale (tir) de votre arme."
                : "2. Appuyez simultanément sur les gâchettes Primary et Secondary de votre arme.";
            GUILayout.Label("1. Click 'Start Scan'\n" + step2, _labelStyle);

            GUILayout.Space(15);

            if (!_isScanning)
            {
                using (new EditorGUI.DisabledScope(_deviceCount == 0))
                {
                    GUI.backgroundColor = colAccent;
                    if (GUILayout.Button("START SCAN", _buttonStyle))
                    {
                        StartScan();
                    }
                }
            }
            else
            {
                GUI.backgroundColor = colError;
                if (GUILayout.Button("STOP SCANNING...", _buttonStyle))
                {
                    StopScan();
                }
            }
            GUI.backgroundColor = Color.white;

            if (!string.IsNullOrEmpty(_foundMac))
            {
                GUILayout.Space(10);
                EditorGUILayout.HelpBox($"SUCCESS! MAC {_foundMac} assigné au device sélectionné (Identifier).", MessageType.Info);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();

            if (_isScanning)
            {
                Repaint();
            }
        }

        private void StartScan()
        {
            try
            {
                _foundMac = "";
                _deviceStates.Clear();
                _client = new MqttClient(_brokerAddress);
                _client.MqttMsgPublishReceived += OnMessageReceived;
                
                string clientId = "UnityEditor_Finder_" + Guid.NewGuid().ToString().Substring(0, 4);
                _client.Connect(clientId);

                if (_client.IsConnected)
                {
                    _client.Subscribe(new string[] { "DeviceToUnity/#" }, new byte[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE });
                    _isScanning = true;
                    Debug.Log($"[DeviceMQTTIdentifierFinder] Connected to {_brokerAddress} and scanning...");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[DeviceMQTTIdentifierFinder] Connection failed: {e.Message}");
                _isScanning = false;
            }
        }

        private void StopScan()
        {
            _isScanning = false;
            if (_client != null)
            {
                if (_client.IsConnected)
                {
                    try { _client.Disconnect(); } catch { }
                }
                _client.MqttMsgPublishReceived -= OnMessageReceived;
                _client = null;
            }
        }

        private void OnMessageReceived(object sender, MqttMsgPublishEventArgs e)
        {
            string topic = e.Topic;
            string payloadStr = Encoding.UTF8.GetString(e.Message);

            // DeviceToUnity/{MAC}/{ID}
            string[] parts = topic.Split('/');
            if (parts.Length < 3) return;

            string mac = parts[1];
            string sensorId = parts[2];
            bool isPressed = (payloadStr == "1");

            if (!_deviceStates.ContainsKey(mac)) _deviceStates[mac] = (false, false);
            var state = _deviceStates[mac];

            if (sensorId == "1") state.primary = isPressed;
            if (sensorId == "2") state.secondary = isPressed;

            _deviceStates[mac] = state;

            // Mode "Primary only" : on déclenche dès le tir principal.
            // Mode "Primary + Secondary" : il faut les deux gâchettes ensemble (plus sûr).
            bool found = _mode == FindMode.PrimaryOnly
                ? state.primary
                : (state.primary && state.secondary);

            if (found)
            {
                _foundMac = mac;
                // On utilise delayCall car on est dans un thread MQTT
                EditorApplication.delayCall += () =>
                {
                    ApplyFoundMac(mac);
                };
            }
        }

        private void ApplyFoundMac(string mac)
        {
            Debug.Log($"[DeviceMQTTIdentifierFinder] Weapon Detected: {mac}. Écriture dans le device sélectionné...");

            try
            {
                string configPath = GetConfigPath();
                if (!File.Exists(configPath))
                {
                    Debug.LogError($"[DeviceMQTTIdentifierFinder] GlobalConfig.json introuvable : {configPath}");
                    StopScan(); Repaint(); return;
                }

                var root = JObject.Parse(File.ReadAllText(configPath));
                if (!(root["Devices"] is JArray devices) ||
                    _selectedDeviceIndex < 0 || _selectedDeviceIndex >= devices.Count ||
                    !(devices[_selectedDeviceIndex] is JObject target))
                {
                    Debug.LogError($"[DeviceMQTTIdentifierFinder] Device #{_selectedDeviceIndex} introuvable dans GlobalConfig.Devices — MAC non écrit.");
                    StopScan(); Repaint(); return;
                }

                target["Identifier"] = mac;

                File.WriteAllText(configPath, root.ToString(Formatting.Indented));
                Debug.Log($"[DeviceMQTTIdentifierFinder] Identifier '{mac}' assigné au device #{_selectedDeviceIndex} → {configPath}");

                // Si on est en mode Play, on met à jour l'instance
                if (Application.isPlaying && BackOfficeVaronia.Instance != null)
                {
                    BackOfficeVaronia.Instance.LoadConfig();
                }

                LoadDevices(); // rafraîchit les labels (l'Identifier apparaît maintenant)
            }
            catch (Exception e)
            {
                Debug.LogError($"[DeviceMQTTIdentifierFinder] Échec écriture Identifier : {e.Message}");
            }

            StopScan();
            Repaint();
        }
    }
}
