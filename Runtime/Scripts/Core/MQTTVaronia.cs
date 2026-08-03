using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using M2MqttUnity;
using Newtonsoft.Json;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace VaroniaBackOffice
{
    public class MQTTVaronia : M2MqttUnityClient
    {

        public ESoftState SoftState { get; private set; }
        
        
        protected void Start()
        {
            BackOfficeVaronia.OnConfigLoaded += HandleConfigLoaded;

            // If config is already loaded (singleton pattern safety), trigger it manually
            if (BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.config != null)
            {
                HandleConfigLoaded();
            }
        }

        // OnConfigLoaded est un event STATIQUE : sans désabonnement, un MQTTVaronia détruit
        // (doublon, rechargement) resterait référencé et lèverait une MissingReferenceException.
        private void OnDestroy()
        {
            BackOfficeVaronia.OnConfigLoaded -= HandleConfigLoaded;
        }
        
        
        private void HandleConfigLoaded()
        {
            Debug.Log("#[MQTT] Config received via Event. Initializing connection...");
            var cfg = BackOfficeVaronia.Instance.config;
            this.brokerAddress = cfg.MQTT_ServerIP;
    
            // Now connect
            Connect();
        }
        
        
        
        protected override void OnConnected()
        {
            base.OnConnected();
            Debug.Log("#<color=Green>[Back Office Varonia] Successfully connected to the broker</color>");
            SoftState = ESoftState.GAME_LAUNCHED;

            // OnConnected est rappelé à CHAQUE reconnexion : sans arrêt de la précédente, les
            // coroutines de ping s'accumulaient (N reconnexions = N pings/seconde).
            if (_upConnectionRoutine != null) StopCoroutine(_upConnectionRoutine);
            _upConnectionRoutine = StartCoroutine(UpConnection());
            
            
            Subscribe();

            // rejoue les messages émis avant l'établissement de la connexion
            while (_pendingMsgs.Count > 0)
                PublishMsg(_pendingMsgs.Dequeue());
        }

        
        
        public void Subscribe()
        {
            client.Subscribe(new string[] { "ServerToUnity/" + BackOfficeVaronia.Instance.config.MQTT_IDClient }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
        }
        
        // Messages émis avant que la connexion (asynchrone) soit établie : mis en attente
        // puis flushés dans OnConnected. Évite l'erreur au boot (SetSoftState dès le Start)
        // et ne perd pas les premiers états.
        readonly Queue<string> _pendingMsgs = new Queue<string>();
        const int MaxPendingMsgs = 30;

        public void PublishMsg(string Msg)
        {
            // Poste sans back office (pas de broker configuré) : on ignore silencieusement.
            var cfg = BackOfficeVaronia.Instance != null ? BackOfficeVaronia.Instance.config : null;
            if (cfg == null || string.IsNullOrEmpty(cfg.MQTT_ServerIP))
                return;

            // Connexion pas encore établie : en attente (la connexion MQTT est asynchrone).
            if (client == null || !client.IsConnected)
            {
                if (_pendingMsgs.Count < MaxPendingMsgs) _pendingMsgs.Enqueue(Msg);
                return;
            }

            try
            {
                client.Publish("UnityToServer/" + cfg.MQTT_IDClient, System.Text.Encoding.UTF8.GetBytes(Msg), MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Back Office Varonia] Error while publishing message: " + e.Message);
            }
        }
        
        
        

        protected override void DecodeMessage(string topic, byte[] message) // Receive Message
        {
            // IMPORTANT : la désérialisation DOIT rester dans le try. Un JSON malformé qui remonte
            // ici ferait échouer ProcessMqttMessageBackgroundQueue, la file ne serait jamais vidée
            // et le message fautif serait rejoué indéfiniment (réception MQTT bloquée).
            try
            {
                var payload = JsonConvert.DeserializeObject<MQTT_Payload>(System.Text.Encoding.UTF8.GetString(message));

                if (payload == null)
                {
                    Debug.LogWarning("[MQTT] Payload vide ou non désérialisable — message ignoré.");
                    return;
                }

                if (payload.sMethod == "GET_SOFTPARTYSTART_RESULT")
                    BackOfficeVaronia.Instance.TriggerStartGame(false);

                if (payload.sMethod == "GET_SOFTPARTYSKIPTUTOANDSTART_RESULT")
                    BackOfficeVaronia.Instance.TriggerStartGame(true);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MQTT] Failed to handle payload: {e.Message}");
            }
        }
        
        
        
        protected override void OnConnectionFailed(string errorMessage)
        {
            Debug.LogError($"[Back Office Varonia] MQTT Fail to connect : {errorMessage}");
        }
        
     
        public void SetSoftState(ESoftState eSoft)
        {
            PublishMsg(JsonConvert.SerializeObject(new MQTT_Payload() { sMethod = "SET_SOFTSTATE", CallerDeviceID = BackOfficeVaronia.Instance.config.MQTT_IDClient, Items = { { "SoftState", eSoft } } }));
//            Debug.Log($"[MQTT] Published SoftState: {eSoft}");
            // Transition vers GAME_INPARTY = début de mission. On teste la transition (et pas l'état)
            // car le ping (UpConnection) re-publie le même état chaque seconde → pas de ré-armement.
            if (eSoft == ESoftState.GAME_INPARTY && SoftState != ESoftState.GAME_INPARTY)
                SoftPartyStarted = true;
            SoftState = eSoft;
        }
        
        
        
        
        
        
        
        // Ping Serveur — une seule instance active à la fois (voir OnConnected).
        private Coroutine _upConnectionRoutine;

        IEnumerator UpConnection()
        {
            while (true)
            {
                yield return new WaitForSeconds(1);
                SetSoftState(SoftState);
            }
        }

        
        
        
        
        
        /// <summary>True = mission en cours. Passe à true à la transition vers GAME_INPARTY (= début
        /// de partie, signalé par les scénarios) ou via SetSoftPartyStarted(), et à false via
        /// SetSoftPartyClosed(). Posé même sans MQTT. Lu par la GodView pour le chrono de mission.</summary>
        public bool SoftPartyStarted { get; private set; }

        public void SetSoftPartyStarted()
        {
            SoftPartyStarted = true;
            if (!String.IsNullOrEmpty(BackOfficeVaronia.Instance.config.MQTT_ServerIP))
            {
                PublishMsg(JsonConvert.SerializeObject(new MQTT_Payload() { sMethod = "SET_SOFTPARTYSTARTED", CallerDeviceID = BackOfficeVaronia.Instance.config.MQTT_IDClient }));
            }
        }

     
        public void SetSoftPartyClosed()
        {
            SoftPartyStarted = false;
            if (!String.IsNullOrEmpty(BackOfficeVaronia.Instance.config.MQTT_ServerIP))
            {
                PublishMsg(JsonConvert.SerializeObject(new MQTT_Payload() { sMethod = "SET_SOFTPARTYCLOSED", CallerDeviceID =BackOfficeVaronia.Instance.config.MQTT_IDClient }));
            }
        }
        
        public void SetSoftPiloteDevice(string Key, bool State)
        {
            if (!String.IsNullOrEmpty(BackOfficeVaronia.Instance.config.MQTT_ServerIP))
            {
                PublishMsg(JsonConvert.SerializeObject(new MQTT_Payload() { sMethod = "SET_SOFTPILOTDEVICE", CallerDeviceID = BackOfficeVaronia.Instance.config.MQTT_IDClient, Items = { { "Key", Key }, { "State", State } } }));
                SETDB_ADDEVENT("SET_SOFTPILOTDEVICE");
            }
        }
        
        public void SETDB_ADDEVENT(string Type)
        {
            if (!String.IsNullOrEmpty(BackOfficeVaronia.Instance.config.MQTT_ServerIP))
            {
                PublishMsg(JsonConvert.SerializeObject(new MQTT_Payload() { sMethod = "SETDB_ADDEVENT", CallerDeviceID = BackOfficeVaronia.Instance.config.MQTT_IDClient, Items = { { "Type", Application.productName + "_" + Type } } }));
            }

        }

        /// <summary>
        /// Envoie la note (1..5 étoiles) donnée par le joueur au back-office. Même logique que les autres
        /// SET_ : payload MQTT_Payload publié sur UnityToServer/&lt;IDClient&gt;, uniquement si un serveur
        /// MQTT est configuré. Le back-office reçoit sMethod="SET_SOFTRATING" avec Rating + GameValue.
        /// En plus, on trace l'événement via SETDB_ADDEVENT (canal déjà supporté) pour ne rien perdre.
        /// </summary>
        public void SetRating(int rating)
        {
            if (String.IsNullOrEmpty(BackOfficeVaronia.Instance.config.MQTT_ServerIP))
                return;

            var D = new Dictionary<string, object>
            {
                { "Rating",    rating },
                { "GameValue", ReadGameId() },
            };
            PublishMsg(JsonConvert.SerializeObject(new MQTT_Payload
            {
                sMethod        = "SET_SOFTRATING",
                CallerDeviceID = BackOfficeVaronia.Instance.config.MQTT_IDClient,
                Items          = D
            }));

            // Trace aussi comme événement DB (déjà géré côté serveur) : "<Produit>_Rating_<n>".
            SETDB_ADDEVENT("Rating_" + rating);
        }

        /// <summary>GameID lu depuis StreamingAssets/GameID.txt (9999 par défaut si absent/illisible).
        /// Source unique pour SetRating et SetScore.</summary>
        private static int ReadGameId()
        {
            try
            {
                using (var sr = new StreamReader(Application.streamingAssetsPath + "/GameID.txt"))
                    return int.Parse(sr.ReadToEnd());
            }
            // Fichier absent = cas NORMAL (jeu non référencé côté back-office) → défaut silencieux.
            catch { return 9999; }
        }

        
        
#if GAME_SCORE
        public void SetScore(GameScore Score)
        {

            if (!String.IsNullOrEmpty(BackOfficeVaronia.Instance.config.MQTT_ServerIP))
            {
                int GameId = ReadGameId();

                var D = new Dictionary<string, object>();
                D.Add("Data", Score);
                D.Add("GameValue", GameId);
                PublishMsg(JsonConvert.SerializeObject(new MQTT_Payload() { sMethod = "SET_SOFTSCORE", CallerDeviceID = BackOfficeVaronia.Instance.config.MQTT_IDClient, Items = D }));
            }

        }
#endif

        
        
        
        
    }


    public class MQTT_Payload
    {
        public int CallerDeviceID { get; set; }
        public int TargetDeviceID { get; set; }
      
        public string sMethod { get; set; }
        public Dictionary<string, object> Items { get; set; }

        public MQTT_Payload()
        {
            Items = new Dictionary<string, object>();
        }
    }
    
    }
    

