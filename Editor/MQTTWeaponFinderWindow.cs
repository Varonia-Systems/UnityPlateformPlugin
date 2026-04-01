using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace VaroniaBackOffice
{
    public class MQTTWeaponFinderWindow : EditorWindow
    {
        private string _brokerAddress = "localhost";
        private MqttClient _client;
        private bool _isScanning = false;
        private string _foundMac = "";
        private Dictionary<string, (bool primary, bool secondary)> _deviceStates = new Dictionary<string, (bool, bool)>();

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

        [MenuItem("Varonia/Find MQTT Weapon")]
        public static void ShowWindow()
        {
            var window = GetWindow<MQTTWeaponFinderWindow>("MQTT Weapon Finder");
            window.minSize = new Vector2(350, 250);
            window.Show();
        }

        [MenuItem("Varonia/Find MQTT Weapon", true)]
        public static bool ValidateShowWindow()
        {
            try
            {
                // On réutilise la logique de chemin de LoadBrokerAddress
                string rootPath = UnityEngine.Application.persistentDataPath.Replace(UnityEngine.Application.companyName + "/" + UnityEngine.Application.productName, "Varonia");
                string configPath = Path.Combine(rootPath, "GlobalConfig.json");

                if (!File.Exists(configPath)) return false;

                string jsonContent = File.ReadAllText(configPath);
                var cfg = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                
                return cfg != null && cfg.ContainsKey("WeaponMAC");
            }
            catch
            {
                return false;
            }
        }

        private void OnEnable()
        {
            LoadBrokerAddress();
            _stylesInitialized = false;
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
                Debug.LogWarning($"[MQTTWeaponFinder] Could not load MQTT_ServerIP from file: {e.Message}");
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
            
            GUILayout.Label("MQTT WEAPON FINDER", _headerStyle);

            EditorGUILayout.BeginVertical(_cardStyle);
            
            EditorGUILayout.LabelField("Broker IP:", _brokerAddress, EditorStyles.boldLabel);
            GUILayout.Space(5);
            
            EditorGUILayout.LabelField("Instructions:", EditorStyles.boldLabel);
            GUILayout.Label("1. Click 'Start Scan'\n2. Press Primary and Secondary triggers simultaneously on your weapon.", _labelStyle);

            GUILayout.Space(15);

            if (!_isScanning)
            {
                GUI.backgroundColor = colAccent;
                if (GUILayout.Button("START SCAN", _buttonStyle))
                {
                    StartScan();
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
                EditorGUILayout.HelpBox($"SUCCESS! Found and assigned MAC: {_foundMac}", MessageType.Info);
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
                    Debug.Log($"[MQTTWeaponFinder] Connected to {_brokerAddress} and scanning...");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MQTTWeaponFinder] Connection failed: {e.Message}");
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

            if (state.primary && state.secondary)
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
            Debug.Log($"[MQTTWeaponFinder] Weapon Detected: {mac}. Saving to config...");

            try
            {
                string rootPath = Application.persistentDataPath.Replace(Application.companyName + "/" + Application.productName, "Varonia");
                string configPath = Path.Combine(rootPath, "GlobalConfig.json");

                Dictionary<string, object> configData;
                if (File.Exists(configPath))
                {
                    string jsonContent = File.ReadAllText(configPath);
                    configData = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent) ?? new Dictionary<string, object>();
                }
                else
                {
                    configData = new Dictionary<string, object>();
                }

                configData["WeaponMAC"] = mac;
                
                string newJson = JsonConvert.SerializeObject(configData, Formatting.Indented);
                File.WriteAllText(configPath, newJson);
                
                Debug.Log($"[MQTTWeaponFinder] WeaponMAC saved to {configPath}");
                
                // Si on est en mode Play, on met à jour l'instance
                if (Application.isPlaying && BackOfficeVaronia.Instance != null)
                {
                    BackOfficeVaronia.Instance.LoadConfig();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MQTTWeaponFinder] Failed to save MAC: {e.Message}");
            }

            StopScan();
            Repaint();
        }
    }
}
