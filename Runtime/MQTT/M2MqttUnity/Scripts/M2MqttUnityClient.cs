/*
The MIT License (MIT)

Copyright (c) 2018 Giovanni Paolo Vigano'

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;


/// <summary>
/// Adaptation for Unity of the M2MQTT library (https://github.com/eclipse/paho.mqtt.m2mqtt),
/// modified to run on UWP (also tested on Microsoft HoloLens).
/// </summary>
namespace M2MqttUnity
{
    /// <summary>
    /// Generic MonoBehavior wrapping a MQTT client, using a double buffer to postpone message processing in the main thread. 
    /// </summary>
    public class M2MqttUnityClient : MonoBehaviour
    {
        [HideInInspector]
        public string brokerAddress = "localhost";
        [HideInInspector]
        public int brokerPort = 1883;
        [HideInInspector]
        public bool isEncrypted = false;
        [HideInInspector]
        public int connectionDelay = 500;
        [HideInInspector]
        public int timeoutOnConnection = MqttSettings.MQTT_CONNECT_TIMEOUT;
    
        [HideInInspector]
        public string mqttUserName = null;
        [HideInInspector]
        public string mqttPassword = null;



 

        /// <summary>
        /// Wrapped MQTT client
        /// </summary>
        public MqttClient client;

        private List<MqttMsgPublishEventArgs> messageQueue1 = new List<MqttMsgPublishEventArgs>();
        private List<MqttMsgPublishEventArgs> messageQueue2 = new List<MqttMsgPublishEventArgs>();
        private List<MqttMsgPublishEventArgs> frontMessageQueue = null;
        private List<MqttMsgPublishEventArgs> backMessageQueue = null;
        private bool mqttClientConnectionClosed = false;
        private bool mqttClientConnected = false;



        public event Action<string, byte[]> ReceiveMsg;


        /// <summary>
        /// Event fired when a connection is successfully established
        /// </summary>
        public event Action ConnectionSucceeded;
        /// <summary>
        /// Event fired when failing to connect
        /// </summary>
        public event Action ConnectionFailed;

        // [Varonia] État de la tentative de connexion en cours. La connexion M2Mqtt (DNS, socket,
        // attente du CONNACK) est entièrement bloquante : exécutée sur le main thread elle gelait
        // le jeu au démarrage quand le broker était lent ou injoignable. Elle part donc sur un
        // thread de fond, et ces champs (volatile : écrits par ce thread, lus par la coroutine)
        // ramènent le résultat sur le main thread.
        private volatile bool connectFinished;
        private volatile MqttClient connectResult;
        private volatile string connectError;
        // Connexion abandonnée (composant détruit / appli fermée) : le thread referme lui-même le
        // client s'il arrive trop tard, sinon ses threads internes resteraient actifs.
        private volatile bool connectAbandoned;
        private bool connecting = false;

        /// <summary>
        /// True while a connection attempt is running in background.
        /// </summary>
        public bool IsConnecting { get { return connecting; } }

        /// <summary>
        /// Connect to the broker using current settings.
        /// </summary>
        public virtual void Connect()
        {
            if (connecting)
            {
                return;
            }
            if (client == null || !client.IsConnected)
            {
                StartCoroutine(DoConnect());
            }
        }

        /// <summary>
        /// Disconnect from the broker, if connected.
        /// </summary>
        public virtual void Disconnect()
        {
            if (client != null)
            {
                StartCoroutine(DoDisconnect());
            }
        }

        /// <summary>
        /// Override this method to take some actions before connection (e.g. display a message)
        /// </summary>
        protected virtual void OnConnecting()
        {
            Debug.LogFormat("Connecting to broker on {0}:{1}...", brokerAddress, brokerPort.ToString());
        }

        /// <summary>
        /// Override this method to take some actions if the connection succeeded.
        /// </summary>
        protected virtual void OnConnected()
        {
            Debug.LogFormat("Connected to {0}:{1}...", brokerAddress, brokerPort.ToString());

            SubscribeTopics();

            if (ConnectionSucceeded != null)
            {
                ConnectionSucceeded();
            }
        }

        /// <summary>
        /// Override this method to take some actions if the connection failed.
        /// </summary>
        protected virtual void OnConnectionFailed(string errorMessage)
        {
            Debug.LogWarning("Connection failed.");
            if (ConnectionFailed != null)
            {
                ConnectionFailed();
            }
        }

        /// <summary>
        /// Override this method to subscribe to MQTT topics.
        /// </summary>
        protected virtual void SubscribeTopics()
        {
        }

        /// <summary>
        /// Override this method to unsubscribe to MQTT topics (they should be the same you subscribed to with SubscribeTopics() ).
        /// </summary>
        protected virtual void UnsubscribeTopics()
        {
        }

        /// <summary>
        /// Disconnect before the application quits.
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            CloseConnection();
        }

        /// <summary>
        /// Initialize MQTT message queue
        /// Remember to call base.Awake() if you override this method.
        /// </summary>
        protected virtual void Awake()
        {
            frontMessageQueue = messageQueue1;
            backMessageQueue = messageQueue2;
        }

     

        /// <summary>
        /// Override this method for each received message you need to process.
        /// </summary>
        protected virtual void DecodeMessage(string topic, byte[] message)
        {
            Debug.LogFormat("Message received on topic: {0}", topic);
        }

        /// <summary>
        /// Override this method to take some actions when disconnected.
        /// </summary>
        protected virtual void OnDisconnected()
        {
            Debug.Log("Disconnected.");
        }

        /// <summary>
        /// Override this method to take some actions when the connection is closed.
        /// </summary>
        protected virtual void OnConnectionLost()
        {
            Debug.LogWarning("CONNECTION LOST!");
        }

        /// <summary>
        /// Processing of income messages and events is postponed here in the main thread.
        /// Remember to call ProcessMqttEvents() in Update() method if you override it.
        /// </summary>
        protected virtual void Update()
        {
            ProcessMqttEvents();
        }

        protected virtual void ProcessMqttEvents()
        {
            // process messages in the main queue
            SwapMqttMessageQueues();
            ProcessMqttMessageBackgroundQueue();
            // process messages income in the meanwhile
            SwapMqttMessageQueues();
            ProcessMqttMessageBackgroundQueue();

            if (mqttClientConnectionClosed)
            {
                mqttClientConnectionClosed = false;
                OnConnectionLost();
            }
        }

        private void ProcessMqttMessageBackgroundQueue()
        {
            // [Varonia] Chaque message est traité isolément : une exception sur un message
            // (ex. JSON malformé) ne doit ni interrompre les suivants, ni empêcher le Clear()
            // final — sinon le message fautif resterait en file et serait rejoué à chaque frame.
            try
            {
                foreach (MqttMsgPublishEventArgs msg in backMessageQueue)
                {
                    try
                    {
                        if (msg.Topic.StartsWith("ServerToUnity"))
                            DecodeMessage(msg.Topic, msg.Message);
                        else if (ReceiveMsg != null)
                            // [Varonia] null quand plus aucun consommateur n'est abonné (armes
                            // détruites entre deux scènes) : ce n'est pas une erreur.
                            ReceiveMsg.Invoke(msg.Topic, msg.Message);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[MQTT] Message ignoré (topic '{msg.Topic}') : {e.Message}");
                    }
                }
            }
            finally
            {
                backMessageQueue.Clear();
            }
        }

        /// <summary>
        /// Swap the message queues to continue receiving message when processing a queue.
        /// </summary>
        private void SwapMqttMessageQueues()
        {
            frontMessageQueue = frontMessageQueue == messageQueue1 ? messageQueue2 : messageQueue1;
            backMessageQueue = backMessageQueue == messageQueue1 ? messageQueue2 : messageQueue1;
        }

        private void OnMqttMessageReceived(object sender, MqttMsgPublishEventArgs msg)
        {
            frontMessageQueue.Add(msg);
        }

        private void OnMqttConnectionClosed(object sender, EventArgs e)
        {
            // Set unexpected connection closed only if connected (avoid event handling in case of controlled disconnection)
            mqttClientConnectionClosed = mqttClientConnected;
            mqttClientConnected = false;
        }

        /// <summary>
        /// Connects to the broker using the current settings.
        /// </summary>
        /// <returns>The execution is done in a coroutine.</returns>
        private IEnumerator DoConnect()
        {
            connecting = true;

            // wait for the given delay
            yield return new WaitForSecondsRealtime(connectionDelay / 1000f);
            // leave some time to Unity to refresh the UI
            yield return new WaitForEndOfFrame();

            // un client issu d'une tentative précédente ratée est jeté avant de réessayer
            if (client != null && !client.IsConnected)
            {
                CloseConnection();
            }
            else if (client != null)
            {
                connecting = false;
                yield break;
            }

            OnConnecting();

            // leave some time to Unity to refresh the UI
            yield return new WaitForEndOfFrame();

            // [Varonia] Copies locales : le thread de fond ne doit toucher ni à l'API Unity ni à
            // des champs que l'inspecteur pourrait modifier pendant la tentative.
            string address = brokerAddress;
            int port = brokerPort;
            bool encrypted = isEncrypted;
            int timeout = timeoutOnConnection;
            string userName = mqttUserName;
            string password = mqttPassword;
            string clientId = Guid.NewGuid().ToString();

            connectFinished = false;
            connectResult = null;
            connectError = null;
            connectAbandoned = false;

            Thread connectThread = new Thread(() =>
            {
                MqttClient newClient = null;
                try
                {
                    // création du client : résolution DNS incluse (bloquant)
#if (!UNITY_EDITOR && UNITY_WSA_10_0 && !ENABLE_IL2CPP)
                    newClient = new MqttClient(address, port, encrypted, encrypted ? MqttSslProtocols.SSLv3 : MqttSslProtocols.None);
#else
                    newClient = new MqttClient(address, port, encrypted, null, null, encrypted ? MqttSslProtocols.SSLv3 : MqttSslProtocols.None);
#endif
                    newClient.Settings.TimeoutOnConnection = timeout;
                    // ouverture du socket + attente du CONNACK (bloquant)
                    newClient.Connect(clientId, userName, password);

                    if (!newClient.IsConnected)
                    {
                        // broker joint mais connexion refusée : on referme, sinon les threads
                        // internes du client resteraient actifs
                        connectError = "CONNECTION FAILED!";
                        try { newClient.Disconnect(); }
                        catch { }
                    }
                    else if (connectAbandoned)
                    {
                        // trop tard : plus personne pour exploiter ce client, on le referme ici
                        try { newClient.Disconnect(); }
                        catch { }
                    }
                    else
                    {
                        connectResult = newClient;
                    }
                }
                catch (Exception e)
                {
                    connectError = e.Message;
                    if (newClient != null)
                    {
                        try { newClient.Disconnect(); }
                        catch { }
                    }
                }
                finally
                {
                    connectFinished = true;
                }
            });
            // thread de fond : ne doit jamais empêcher la fermeture du jeu
            connectThread.IsBackground = true;
            connectThread.Start();

            // le jeu continue de tourner normalement pendant la tentative
            while (!connectFinished)
            {
                yield return null;
            }

            connecting = false;

            if (connectResult != null)
            {
                client = connectResult;
                connectResult = null;
                client.ConnectionClosed += OnMqttConnectionClosed;
                // register to message received
                client.MqttMsgPublishReceived += OnMqttMessageReceived;
                mqttClientConnected = true;
                OnConnected();
            }
            else
            {
                client = null;
                string error = connectError ?? "CONNECTION FAILED!";
                Debug.LogWarningFormat("Failed to connect to {0}:{1}: {2}", address, port, error);
                OnConnectionFailed(error);
            }
        }

        private IEnumerator DoDisconnect()
        {
            yield return new WaitForEndOfFrame();
            CloseConnection();
            OnDisconnected();
        }

        private void CloseConnection()
        {
            // [Varonia] une tentative encore en vol ne doit pas aboutir derrière notre dos
            connectAbandoned = true;
            mqttClientConnected = false;
            if (client != null)
            {
                if (client.IsConnected)
                {
                    UnsubscribeTopics();
                    client.Disconnect();
                }
                client.MqttMsgPublishReceived -= OnMqttMessageReceived;
                client.ConnectionClosed -= OnMqttConnectionClosed;
                client = null;
            }
        }

#if ((!UNITY_EDITOR && UNITY_WSA_10_0))
        private void OnApplicationFocus(bool focus)
        {
            // On UWP 10 (HoloLens) we cannot tell whether the application actually got closed or just minimized.
            // (https://forum.unity.com/threads/onapplicationquit-and-ondestroy-are-not-called-on-uwp-10.462597/)
            if (focus)
            {
                Connect();
            }
            else
            {
                CloseConnection();
            }
        }
#endif
    }
}
