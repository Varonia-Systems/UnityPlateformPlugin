using UnityEngine;
using UnityEngine.Events;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using System.Runtime.InteropServices;
#endif

namespace VaroniaBackOffice
{
    /// <summary>
    /// Identifies one of the four available Varonia input buttons.
    /// </summary>
    public enum VaroniaButton
    {
        Primary    = 0,
        Secondary  = 1,
        Tertiary   = 2,
        Quaternary = 3,
    }

#if ENABLE_INPUT_SYSTEM

    // ─── Custom Input Device ────────────────────────────────────────────────

    /// <summary>
    /// Low-level state struct for the Varonia virtual device (4 buttons).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 14)]
    public struct VaroniaDeviceState : IInputStateTypeInfo
    {
        public static FourCC Format => new FourCC('V', 'B', 'O', 'I');
        public FourCC format => Format;

        [InputControl(name = "primary",    layout = "Button", bit = 0)]
        [InputControl(name = "secondary",  layout = "Button", bit = 1)]
        [InputControl(name = "tertiary",   layout = "Button", bit = 2)]
        [InputControl(name = "quaternary", layout = "Button", bit = 3)]
        [FieldOffset(0)] public byte buttons;

        [InputControl(name = "isConnected", layout = "Button", bit = 0)]
        [FieldOffset(1)] public byte isConnectedByte;

        [InputControl(name = "battery",     layout = "Integer")]
        [FieldOffset(2)] public int Battery;

        [InputControl(name = "rssi",        layout = "Analog")]
        [FieldOffset(6)] public float RSSI;

        [InputControl(name = "bootTime",    layout = "Integer")]
        [FieldOffset(10)] public int BootTime;
    }

    /// <summary>
    /// Virtual Varonia input device exposing 4 buttons to the Unity Input System.
    /// Supports multiple simultaneous instances (one per weapon).
    /// </summary>
    [InputControlLayout(stateType = typeof(VaroniaDeviceState), displayName = "Varonia Device")]
    public class VaroniaDevice : InputDevice
    {
        public ButtonControl  primary     { get; private set; }
        public ButtonControl  secondary   { get; private set; }
        public ButtonControl  tertiary    { get; private set; }
        public ButtonControl  quaternary  { get; private set; }
        public ButtonControl  isConnected { get; private set; }
        public IntegerControl battery     { get; private set; }
        public AxisControl    rssi        { get; private set; }
        public IntegerControl bootTime    { get; private set; }

        /// <summary> Index de l'arme associée à ce device (0-based). </summary>
        public int WeaponIndex { get; internal set; } = -1;

        /// <summary> Singleton du premier device (rétrocompatibilité). </summary>
        public static VaroniaDevice Current => All != null && All.Length > 0 ? All[0] : null;

        /// <summary> Tableau de tous les devices actifs, un par arme. </summary>
        public static VaroniaDevice[] All { get; private set; } = new VaroniaDevice[0];

        protected override void FinishSetup()
        {
            base.FinishSetup();
            primary     = GetChildControl<ButtonControl>("primary");
            secondary   = GetChildControl<ButtonControl>("secondary");
            tertiary    = GetChildControl<ButtonControl>("tertiary");
            quaternary  = GetChildControl<ButtonControl>("quaternary");
            isConnected = GetChildControl<ButtonControl>("isConnected");
            battery     = GetChildControl<IntegerControl>("battery");
            rssi        = GetChildControl<AxisControl>("rssi");
            bootTime    = GetChildControl<IntegerControl>("bootTime");
        }

        public override void MakeCurrent()
        {
            base.MakeCurrent();
        }

        protected override void OnRemoved()
        {
            base.OnRemoved();
        }

        /// <summary>
        /// Registers the VaroniaDevice layout with the Input System.
        /// </summary>
        public static void RegisterLayout()
        {
            InputSystem.RegisterLayout<VaroniaDevice>(
                matches: new InputDeviceMatcher().WithInterface("VaroniaDevice"));
        }

        /// <summary>
        /// Crée (ou récupère) le device pour l'arme à l'index donné.
        /// Le layout est enregistré sous le nom "VaroniaDevice{weaponIndex}" afin que
        /// la path InputActionProperty soit "&lt;VaroniaDevice0&gt;/primary", "&lt;VaroniaDevice1&gt;/primary", etc.
        /// </summary>
        public static VaroniaDevice CreateDevice(int weaponIndex)
        {
            // Agrandir le tableau si nécessaire
            if (All.Length <= weaponIndex)
            {
                var newAll = new VaroniaDevice[weaponIndex + 1];
                System.Array.Copy(All, newAll, All.Length);
                All = newAll;
            }

            if (All[weaponIndex] != null) return All[weaponIndex];

            // Enregistre un layout dérivé nommé "VaroniaDevice{weaponIndex}" pour que
            // la path soit <VaroniaDevice0>/primary, <VaroniaDevice1>/primary, etc.
            string layoutName = $"VaroniaDevice{weaponIndex}";
            InputSystem.RegisterLayout(
                $@"{{""name"":""{layoutName}"",""extend"":""VaroniaDevice""}}");

            var device = InputSystem.AddDevice(layoutName);
            var varoniaDevice = device as VaroniaDevice;
            if (varoniaDevice != null)
            {
                varoniaDevice.WeaponIndex = weaponIndex;
                All[weaponIndex] = varoniaDevice;
            }
            return varoniaDevice;
        }

        /// <summary>
        /// Supprime le device de l'arme à l'index donné.
        /// </summary>
        public static void RemoveDevice(int weaponIndex)
        {
            if (All.Length <= weaponIndex) return;
            if (All[weaponIndex] == null) return;
            InputSystem.RemoveDevice(All[weaponIndex]);
            All[weaponIndex] = null;
        }

        /// <summary>
        /// Supprime tous les devices actifs.
        /// </summary>
        public static void RemoveAllDevices()
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i] != null)
                {
                    InputSystem.RemoveDevice(All[i]);
                    All[i] = null;
                }
            }
            All = new VaroniaDevice[0];
        }

        /// <summary>
        /// Envoie les données de télémétrie au device de l'arme donnée.
        /// </summary>
        public static void SetDeviceData(int weaponIndex, bool isConnected, int battery, float rssi, int bootTime, bool[] currentStates = null)
        {
            if (All.Length <= weaponIndex || All[weaponIndex] == null) return;
            var device = All[weaponIndex];

            var state = new VaroniaDeviceState();
            state.buttons = 0;
            if (currentStates != null && currentStates.Length >= 4)
            {
                if (currentStates[0]) state.buttons |= 1 << 0;
                if (currentStates[1]) state.buttons |= 1 << 1;
                if (currentStates[2]) state.buttons |= 1 << 2;
                if (currentStates[3]) state.buttons |= 1 << 3;
            }

            state.isConnectedByte = isConnected ? (byte)1 : (byte)0;
            state.Battery  = battery;
            state.RSSI     = rssi;
            state.BootTime = bootTime;

            InputSystem.QueueStateEvent(device, state);
        }

        /// <summary>
        /// Envoie un état de bouton au device de l'arme donnée.
        /// </summary>
        internal static void SetButtonState(int weaponIndex, VaroniaButton button, bool pressed, bool[] currentStates)
        {
            if (All.Length <= weaponIndex || All[weaponIndex] == null) return;
            var device = All[weaponIndex];

            var state = new VaroniaDeviceState();
            state.buttons = 0;
            if (currentStates[0]) state.buttons |= 1 << 0;
            if (currentStates[1]) state.buttons |= 1 << 1;
            if (currentStates[2]) state.buttons |= 1 << 2;
            if (currentStates[3]) state.buttons |= 1 << 3;

            int bit = 1 << (int)button;
            if (pressed) state.buttons |= (byte)bit;
            else         state.buttons &= (byte)~bit;

            state.isConnectedByte = device.isConnected.isPressed ? (byte)1 : (byte)0;
            state.Battery  = device.battery.ReadValue();
            state.RSSI     = device.rssi.ReadValue();
            state.BootTime = device.bootTime.ReadValue();

            InputSystem.QueueStateEvent(device, state);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Auto-registers the VaroniaDevice layout when the Unity Editor loads.
    /// </summary>
    [UnityEditor.InitializeOnLoad]
    internal static class VaroniaDeviceEditorInit
    {
        static VaroniaDeviceEditorInit() => VaroniaDevice.RegisterLayout();
    }
#endif

#endif // ENABLE_INPUT_SYSTEM

    // ─── VaroniaInput ────────────────────────────────────────────────────────

    /// <summary>
    /// Gestionnaire d'input Varonia supportant plusieurs armes simultanées.
    /// Chaque arme possède son propre index (0-based), ses propres états de boutons,
    /// et son propre VaroniaDevice dans le New Input System.
    /// </summary>
    public class VaroniaInput : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────────────

        /// <summary> Global singleton instance. </summary>
        public static VaroniaInput Instance { get; private set; }

        // ── Multi-weapon state ────────────────────────────────────────────────

        /// <summary> Nombre d'armes actives (défini au démarrage via VaroniaRuntimeSettings). </summary>
        public static int WeaponCount { get; private set; } = 1;

        // États de boutons par arme : _states[weaponIndex][buttonIndex]
        private static bool[][] _states     = new bool[1][] { new bool[4] };
        private static bool[][] _lastStates = new bool[1][] { new bool[4] };
        private static int[]    _lastFrame  = new int[1];

        // Télémétrie par arme
        private static bool[]   _isConnected = new bool[1];
        private static int[]    _battery     = new int[1];
        private static float[]  _rssi        = new float[1];
        private static int[]    _bootTime    = new int[1];
        private static string[] _model       = new string[1];

        // ── Rétrocompatibilité (arme 0) ──────────────────────────────────────

        /// <summary> Retourne true si le bouton de l'arme 0 est enfoncé. </summary>
        public static bool GetButton(VaroniaButton button) => GetButton(0, button);

        /// <summary> Retourne true si le bouton de l'arme donnée est enfoncé. </summary>
        public static bool GetButton(int weaponIndex, VaroniaButton button)
        {
            UpdateLastStates(weaponIndex);
            if (weaponIndex < 0 || weaponIndex >= _states.Length) return false;
            return _states[weaponIndex][(int)button];
        }

        /// <summary> Retourne true durant la frame où le bouton de l'arme 0 est enfoncé. </summary>
        public static bool GetButtonDown(VaroniaButton button) => GetButtonDown(0, button);

        /// <summary> Retourne true durant la frame où le bouton de l'arme donnée est enfoncé. </summary>
        public static bool GetButtonDown(int weaponIndex, VaroniaButton button)
        {
            UpdateLastStates(weaponIndex);
            if (weaponIndex < 0 || weaponIndex >= _states.Length) return false;
            int idx = (int)button;
            return _states[weaponIndex][idx] && !_lastStates[weaponIndex][idx];
        }

        /// <summary> Retourne true durant la frame où le bouton de l'arme 0 est relâché. </summary>
        public static bool GetButtonUp(VaroniaButton button) => GetButtonUp(0, button);

        /// <summary> Retourne true durant la frame où le bouton de l'arme donnée est relâché. </summary>
        public static bool GetButtonUp(int weaponIndex, VaroniaButton button)
        {
            UpdateLastStates(weaponIndex);
            if (weaponIndex < 0 || weaponIndex >= _states.Length) return false;
            int idx = (int)button;
            return !_states[weaponIndex][idx] && _lastStates[weaponIndex][idx];
        }

        private static void UpdateLastStates(int weaponIndex)
        {
            if (weaponIndex < 0 || weaponIndex >= WeaponCount) return;
            int currentFrame = Time.frameCount;
            if (_lastFrame[weaponIndex] != currentFrame)
            {
                System.Array.Copy(_states[weaponIndex], _lastStates[weaponIndex], _states[weaponIndex].Length);
                _lastFrame[weaponIndex] = currentFrame;
            }
        }

   

        /// <summary> Connexion de l'arme 0. </summary>
        public static bool IsConnected => GetIsConnected(0);
        public static bool GetIsConnected(int weaponIndex) => weaponIndex < _isConnected.Length && _isConnected[weaponIndex];

        /// <summary> Batterie de l'arme 0. </summary>
        public static int Battery => GetBattery(0);
        public static int GetBattery(int weaponIndex) => weaponIndex < _battery.Length ? _battery[weaponIndex] : 0;

        /// <summary> RSSI de l'arme 0. </summary>
        public static float RSSI => GetRSSI(0);
        public static float GetRSSI(int weaponIndex) => weaponIndex < _rssi.Length ? _rssi[weaponIndex] : 0f;

        /// <summary> BootTime de l'arme 0. </summary>
        public static int BootTime => GetBootTime(0);
        public static int GetBootTime(int weaponIndex) => weaponIndex < _bootTime.Length ? _bootTime[weaponIndex] : 0;

        /// <summary> Modèle de l'arme 0. </summary>
        public static string Model => GetModel(0);
        public static string GetModel(int weaponIndex) => weaponIndex < _model.Length ? _model[weaponIndex] : null;

        // ── Mise à jour télémétrie ────────────────────────────────────────────

        /// <summary> Met à jour la télémétrie de l'arme 0 (rétrocompatibilité). </summary>
        public static void SetDeviceData(bool isConnected, int battery, float rssi, int bootTime, string model = null)
            => SetDeviceData(0, isConnected, battery, rssi, bootTime, model);

        /// <summary> Met à jour la télémétrie de l'arme à l'index donné. </summary>
        public static void SetDeviceData(int weaponIndex, bool isConnected, int battery, float rssi, int bootTime, string model = null)
        {
            if (weaponIndex < 0 || weaponIndex >= WeaponCount) return;

            _isConnected[weaponIndex] = isConnected;
            _battery[weaponIndex]     = battery;
            _rssi[weaponIndex]        = rssi;
            _bootTime[weaponIndex]    = bootTime;
            if (model != null) _model[weaponIndex] = model;

#if ENABLE_INPUT_SYSTEM
            VaroniaDevice.SetDeviceData(weaponIndex, isConnected, battery, rssi, bootTime, _states[weaponIndex]);
#endif
        }

        // ── UnityEvents (Inspector) — arme 0 uniquement ───────────────────────

        [Header("Primary (Weapon 0)")]
        public UnityEvent OnPrimaryDown    = new UnityEvent();
        public UnityEvent OnPrimaryUp      = new UnityEvent();

        [Header("Secondary (Weapon 0)")]
        public UnityEvent OnSecondaryDown  = new UnityEvent();
        public UnityEvent OnSecondaryUp    = new UnityEvent();

        [Header("Tertiary (Weapon 0)")]
        public UnityEvent OnTertiaryDown   = new UnityEvent();
        public UnityEvent OnTertiaryUp     = new UnityEvent();

        [Header("Quaternary (Weapon 0)")]
        public UnityEvent OnQuaternaryDown = new UnityEvent();
        public UnityEvent OnQuaternaryUp   = new UnityEvent();

        // ── Static C# Events — incluent l'index d'arme ───────────────────────

        /// <summary> Déclenché quand un bouton change d'état. Paramètres : weaponIndex, button, pressed. </summary>
        public static event System.Action<int, VaroniaButton, bool> OnButtonChanged;

        // Événements rétrocompatibles (arme 0 uniquement)
        public static event System.Action OnPrimaryDownStatic;
        public static event System.Action OnPrimaryUpStatic;
        public static event System.Action OnSecondaryDownStatic;
        public static event System.Action OnSecondaryUpStatic;
        public static event System.Action OnTertiaryDownStatic;
        public static event System.Action OnTertiaryUpStatic;
        public static event System.Action OnQuaternaryDownStatic;
        public static event System.Action OnQuaternaryUpStatic;

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Lire le nombre d'armes depuis VaroniaRuntimeSettings
            int count = 1;
            var settings = VaroniaRuntimeSettings.Load();
            if (settings != null) count = Mathf.Max(1, settings.weaponCount);

            InitWeapons(count);

#if ENABLE_INPUT_SYSTEM
            VaroniaDevice.RegisterLayout();
            for (int i = 0; i < WeaponCount; i++)
                VaroniaDevice.CreateDevice(i);
#endif
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
#if ENABLE_INPUT_SYSTEM
                VaroniaDevice.RemoveAllDevices();
#endif
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary> Initialise les tableaux internes pour le nombre d'armes donné. </summary>
        public static void InitWeapons(int count)
        {
            WeaponCount  = count;
            _states      = new bool[count][];
            _lastStates  = new bool[count][];
            _lastFrame   = new int[count];
            _isConnected = new bool[count];
            _battery     = new int[count];
            _rssi        = new float[count];
            _bootTime    = new int[count];
            _model       = new string[count];
            for (int i = 0; i < count; i++)
            {
                _states[i]     = new bool[4];
                _lastStates[i] = new bool[4];
                _lastFrame[i]  = -1;
            }
        }

        /// <summary>
        /// Définit l'état d'un bouton pour l'arme 0 (rétrocompatibilité).
        /// </summary>
        public static void SetButton(VaroniaButton button, bool pressed)
            => SetButton(0, button, pressed);

        /// <summary>
        /// Définit l'état d'un bouton pour l'arme à l'index donné.
        /// true = appuyé (Down), false = relâché (Up).
        /// </summary>
        public static void SetButton(int weaponIndex, VaroniaButton button, bool pressed)
        {
            if (weaponIndex < 0 || weaponIndex >= WeaponCount) return;

            int idx = (int)button;
            if (_states[weaponIndex][idx] == pressed) return;

            _states[weaponIndex][idx] = pressed;

#if ENABLE_INPUT_SYSTEM
            VaroniaDevice.SetButtonState(weaponIndex, button, pressed, _states[weaponIndex]);
#endif

            OnButtonChanged?.Invoke(weaponIndex, button, pressed);

            if (weaponIndex == 0)
            {
                if (Instance != null)
                    Instance.FireInstanceEvents(button, pressed);
                FireStaticEvents(button, pressed);
            }
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private void FireInstanceEvents(VaroniaButton button, bool pressed)
        {
            switch (button)
            {
                case VaroniaButton.Primary:
                    if (pressed) OnPrimaryDown?.Invoke();    else OnPrimaryUp?.Invoke();    break;
                case VaroniaButton.Secondary:
                    if (pressed) OnSecondaryDown?.Invoke();  else OnSecondaryUp?.Invoke();  break;
                case VaroniaButton.Tertiary:
                    if (pressed) OnTertiaryDown?.Invoke();   else OnTertiaryUp?.Invoke();   break;
                case VaroniaButton.Quaternary:
                    if (pressed) OnQuaternaryDown?.Invoke(); else OnQuaternaryUp?.Invoke(); break;
            }
        }

        private static void FireStaticEvents(VaroniaButton button, bool pressed)
        {
            switch (button)
            {
                case VaroniaButton.Primary:
                    if (pressed) OnPrimaryDownStatic?.Invoke();    else OnPrimaryUpStatic?.Invoke();    break;
                case VaroniaButton.Secondary:
                    if (pressed) OnSecondaryDownStatic?.Invoke();  else OnSecondaryUpStatic?.Invoke();  break;
                case VaroniaButton.Tertiary:
                    if (pressed) OnTertiaryDownStatic?.Invoke();   else OnTertiaryUpStatic?.Invoke();   break;
                case VaroniaButton.Quaternary:
                    if (pressed) OnQuaternaryDownStatic?.Invoke(); else OnQuaternaryUpStatic?.Invoke(); break;
            }
        }
    }
}
