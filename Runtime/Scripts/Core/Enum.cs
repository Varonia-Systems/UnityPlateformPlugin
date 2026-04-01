namespace VaroniaBackOffice
{
    /// <summary>
    /// Defines the role and network status of the device within the Varonia ecosystem.
    /// </summary>
    public enum DeviceMode
    {
        /// <summary> Acts as a server and only displays the spectator view. </summary>
        Server_Spectator = 0,
        /// <summary> It acts as a server while simultaneously instantiating a player. </summary>
        Server_Player = 1,
        /// <summary> Remote device connected as a spectator. </summary>
        Client_Spectator = 2,
        /// <summary> Remote device connected as an active player. </summary>
        Client_Player = 3,
    }

    /// <summary>
    /// Defines the player's dominant hand for VR/Input interactions.
    /// </summary>
    public enum MainHand
    {
        /// <summary> Right-handed configuration. </summary>
        Right = 0,
        /// <summary> Left-handed configuration. </summary>
        Left = 1,
    }
    
    /// <summary>
    /// Software states used for MQTT synchronization. 
    /// These integer values must match the Back-Office protocol.
    /// </summary>
    public enum ESoftState
    {
      
        UNKNOWN = 0,
        READY = 1,
        GAME_LAUNCHED = 112,
        GAME_INLOBBY = 110,
        GAME_INPARTY = 115,
        GAME_CHECKING = 122,
        GAME_SAFETYING = 125,
        GAME_HOSTCONNECTING = 128,
    }
}