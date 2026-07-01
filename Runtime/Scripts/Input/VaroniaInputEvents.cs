using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using VaroniaBackOffice;

namespace VBO_Ultimate.Runtime.Scripts.Input
{
    /// <summary>
    /// Expose les inputs d'une arme sous forme de <see cref="UnityEvent"/> (branchables
    /// dans l'inspector). Écoute le hub central <c>VaroniaInput.OnButtonChanged</c>, donc
    /// fonctionne quelle que soit la source (MQTT, Striker, souris en debug…).
    ///
    /// Cible : soit un <see cref="weaponIndex"/> direct, soit un <see cref="controller"/>
    /// (on résout alors le PREMIER device de GlobalConfig.Devices qui matche).
    /// </summary>
    public class VaroniaInputEvents : MonoBehaviour
    {
        [System.Serializable] public class ButtonEvent : UnityEvent<VaroniaButton, bool> { }

        [Header("Cible")]
        [Tooltip("Si coché : on résout le weaponIndex via le 1er device de GlobalConfig.Devices " +
                 "qui matche le Controller. Sinon on utilise directement Weapon Index.")]
        public bool matchByController = false;

        [Tooltip("Type de contrôleur à rechercher (si Match By Controller est coché).")]
        public Controller controller = Controller.Unknown;

        [Tooltip("Index d'arme ciblé (si Match By Controller est décoché).")]
        public int weaponIndex = 0;

        [Header("Events — appui (Down) / relâché (Up)")]
        public UnityEvent onPrimaryDown,    onPrimaryUp;     // tir principal
        public UnityEvent onSecondaryDown,  onSecondaryUp;
        public UnityEvent onTertiaryDown,   onTertiaryUp;
        public UnityEvent onQuaternaryDown, onQuaternaryUp;

        [Header("Event générique")]
        [Tooltip("Appelé pour tout bouton : (bouton, pressé).")]
        public ButtonEvent onAnyButton;

        /// <summary>Index d'arme effectivement écouté (après résolution).</summary>
        public int ResolvedIndex { get; private set; }
        /// <summary>True quand la cible est résolue et que les events sont actifs.</summary>
        public bool Resolved { get; private set; }

        private void OnEnable()
        {
            VaroniaInput.OnButtonChanged += OnButtonChanged;

            if (!matchByController)
            {
                ResolvedIndex = weaponIndex;
                Resolved      = true;
            }
        }

        private void OnDisable()
        {
            VaroniaInput.OnButtonChanged -= OnButtonChanged;
        }

        private IEnumerator Start()
        {
            if (!matchByController) yield break;

            // Résolution par Controller : on attend la config puis on prend le slot (position parmi
            // les papas) de la 1ʳᵉ arme qui matche — même index que celui utilisé par VaroniaInput.
            yield return new WaitUntil(() => BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.config != null);

            int slot = VaroniaWeaponRegistry.IndexOfController(controller);
            if (slot >= 0)
            {
                ResolvedIndex = slot;
                Resolved      = true;
                yield break;
            }

            Debug.LogWarning($"[VaroniaInputEvents] Aucune arme (papa) avec Controller={controller} dans GlobalConfig.Devices.");
        }

        private void OnButtonChanged(int idx, VaroniaButton button, bool pressed)
        {
            if (!Resolved || idx != ResolvedIndex) return;

            switch (button)
            {
                case VaroniaButton.Primary:    (pressed ? onPrimaryDown    : onPrimaryUp)?.Invoke();    break;
                case VaroniaButton.Secondary:  (pressed ? onSecondaryDown  : onSecondaryUp)?.Invoke();  break;
                case VaroniaButton.Tertiary:   (pressed ? onTertiaryDown   : onTertiaryUp)?.Invoke();   break;
                case VaroniaButton.Quaternary: (pressed ? onQuaternaryDown : onQuaternaryUp)?.Invoke(); break;
            }

            onAnyButton?.Invoke(button, pressed);
        }
    }
}
