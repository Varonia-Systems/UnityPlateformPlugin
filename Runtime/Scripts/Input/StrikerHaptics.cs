using System.Collections;
using System.Collections.Generic;
using UnityEngine;



#if STRIKER_LINK
using StrikerLink.Unity.Runtime.Core;
using StrikerLink.Unity.Runtime.HapticEngine;
#endif

namespace VBO_Ultimate.Runtime.Scripts.Input
{
    public class StrikerHaptics : MonoBehaviour
    {
#if STRIKER_LINK
        // ── Singleton ────────────────────────────────────────────────────────────
        private static StrikerHaptics _instance;
        public static StrikerHaptics Instance => _instance;
   

        // ── Fields ───────────────────────────────────────────────────────────────
        public StrikerDevice striker;
        public StrikerController controller;

        public List<HapticLibraryAsset> Library = new List<HapticLibraryAsset>();
        
        
        
        
        

        // ── Unity lifecycle ──────────────────────────────────────────────────────
        private void Awake()
        {
            
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
          //  _striker    = GetComponent<StrikerDevice>();
          //  _controller = GetComponent<StrikerController>();
        }
        

        /// <summary>
        /// Ajoute un asset à la librairie haptics et met à jour le StrikerController.
        /// </summary>
        public void AddToLibrary(HapticLibraryAsset asset)
        {
            if (asset == null || Library.Contains(asset))
                return;

            Library.Add(asset);
            RefreshController();
        }

        /// <summary>
        /// Retire un asset de la librairie haptics et met à jour le StrikerController.
        /// </summary>
        public void RemoveFromLibrary(HapticLibraryAsset asset)
        {
            if (asset == null || !Library.Contains(asset))
                return;

            Library.Remove(asset);
            RefreshController();
        }

        /// <summary>
        /// Joue un haptic immédiatement sur le device Striker.
        /// </summary>
        public void PlayHaptic(HapticEffectAsset haptic)
        {
            if (striker == null || haptic == null)
                return;

            striker.FireHaptic(haptic);
        }

        /// <summary>
        /// Joue un haptic à partir de son nom (doit être présent dans la Library).
        /// </summary>
        public void PlayHaptic(string hapticName)
        {
            if (striker == null)
                return;

            striker.FireHaptic(hapticName, hapticName);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private void RefreshController()
        {
            StartCoroutine(IE());
            
            
            IEnumerator IE()
            {
                yield return new WaitUntil(() => striker!=null);
                yield return new WaitUntil(() => striker.isConnected);
                Debug.Log("<color=yellow>[Striker] UpdateHapticLibrary</color>");
                
                if (controller == null)
                    controller = GetComponent<StrikerController>();

                if (controller != null)
                {
                    controller.hapticLibraries = Library;
                    controller.UpdateHapticLibrary();
                }
            }
        }
#endif
    }


}
