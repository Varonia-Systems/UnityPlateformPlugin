using System;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt.Messages;
using VaroniaBackOffice;

namespace VBO_Ultimate.Runtime.Scripts.Input
{
    /// <summary>
    /// Version simplifiée de <see cref="MQTT_Weapon"/> : ne gère QUE les inputs
    /// (gâchette principale → Primary, secondaire → Secondary).
    ///
    /// Au lieu de se baser sur le weaponIndex, ce composant cherche dans
    /// <c>GlobalConfig.Devices</c> le PREMIER binding dont le <see cref="Controller"/>
    /// correspond à <see cref="controller"/>, et s'abonne à son Identifier (MAC).
    /// Le weaponIndex utilisé pour VaroniaInput = l'index de ce device dans la liste.
    /// </summary>
    public class MQTTInput : MonoBehaviour
    {
        [Tooltip("Type de contrôleur à rechercher dans GlobalConfig.Devices (premier match).")]
        public Controller controller = Controller.Unknown;

        private int    _weaponIndex;
        private string _mac;

        /// <summary>Index d'arme résolu (index du device trouvé dans GlobalConfig.Devices).</summary>
        public int WeaponIndex => _weaponIndex;
        /// <summary>True une fois le device trouvé et l'abonnement MQTT effectué.</summary>
        public bool Resolved { get; private set; }
        /// <summary>Identifier (MAC) du device résolu, ou vide.</summary>
        public string Mac => _mac;

        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        private IEnumerator Start()
        {
            yield return new WaitUntil(() => BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.config != null);
            yield return new WaitUntil(() => BackOfficeVaronia.Instance.mqttClient != null
                                          && BackOfficeVaronia.Instance.mqttClient.client != null);

            // ── Slot (position parmi les papas) du premier device qui matche le Controller ──
            int foundIndex = VaroniaWeaponRegistry.IndexOfController(controller);
            if (foundIndex < 0)
            {
                Debug.LogWarning($"[MQTTInput] Aucune arme (papa) avec Controller={controller} dans GlobalConfig.Devices.");
                yield break;
            }

            var binding = VaroniaWeaponRegistry.GetParent(foundIndex);
            _mac = binding != null ? binding.Identifier : null;
            if (string.IsNullOrEmpty(_mac))
            {
                Debug.LogWarning($"[MQTTInput] Arme Controller={controller} (slot {foundIndex}) sans Identifier — abandon.");
                yield break;
            }

            _weaponIndex = foundIndex;
            VaroniaInput.EnsureWeapon(_weaponIndex);

            Debug.Log($"[MQTTInput] Subscribe to {_mac} (Controller={controller}, weaponIndex={_weaponIndex})");
            BackOfficeVaronia.Instance.mqttClient.client.Subscribe(
                new string[] { "DeviceToUnity/" + _mac + "/#" },
                new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
            BackOfficeVaronia.Instance.mqttClient.ReceiveMsg += OnMqtt;

            Resolved = true;
        }

        private void OnDestroy()
        {
            if (BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.mqttClient != null)
                BackOfficeVaronia.Instance.mqttClient.ReceiveMsg -= OnMqtt;
        }

        private void OnMqtt(string title, byte[] value)
        {
            if (string.IsNullOrEmpty(title) || !title.StartsWith("DeviceToUnity")) return;

            var parts = title.Split('/');
            if (parts.Length < 3) return;

            // Filtre par MAC (ReceiveMsg est global → on ignore les autres armes).
            if (!string.IsNullOrEmpty(_mac) && parts[1] != _mac) return;

            string key = parts[parts.Length - 1];
            string val = System.Text.Encoding.UTF8.GetString(value);

            _mainThreadQueue.Enqueue(() =>
            {
                if (DebugModeOverlay.IsDebugMode) return;

                bool pressed = val == "1";
                switch (key)
                {
                    case "1": VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Primary,    pressed); break;
                    case "2": VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Secondary,  pressed); break;
                    case "3": VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Tertiary,   pressed); break;
                    case "4": VaroniaInput.SetButton(_weaponIndex, VaroniaButton.Quaternary, pressed); break;
                }
            });
        }

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out Action action))
                action?.Invoke();
        }
    }
}
