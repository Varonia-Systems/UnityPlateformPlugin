using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VaroniaBackOffice
{
    public class AutoSizing : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private int targetSamples = 150;
        [SerializeField] private float maxLookAngle = 10f;
        
        [Header("Sécurité Anti-Troll")]
        [Tooltip("Si la hauteur change de plus de X cm entre deux mesures, on ignore (mouvement trop brusque)")]
        [SerializeField] private float maxVerticalSpeed = 0.03f; 
        
        public static float Player_Size;
        public bool IsCalibrating => _isCalibrating;
        public float CurrentRetainedSize => Player_Size;
        public int CurrentSamples => _capturedSamples.Count;
        public int TargetSamples => targetSamples;

        private List<float> _capturedSamples = new List<float>();
        private bool _isCalibrating = false;
        private float _lastHeight;


        private bool first = true;
        private VaroniaRuntimeSettings _settings;
        
        private void Start()
        {
            _settings = VaroniaRuntimeSettings.Load();
        }
        
        private void Update()
        {
            bool canAutoStart = _settings != null && _settings.autoStartCalibrationOnInput;

            if (canAutoStart && !_isCalibrating && BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.IsStarted && first)
            {
                if (VaroniaInput.GetButton(0, VaroniaButton.Primary))
                {
                    first = false;
                    StartCalibration();
                }
            }
        }

        public void StartCalibration()
        {
            if (_isCalibrating ||  BackOfficeVaronia.Instance == null || BackOfficeVaronia.Instance.Rig == null || BackOfficeVaronia.Instance.MainCamera == null) return;
            
            Debug.Log("<color=green>Calibration en cours...</color>");
            StartCoroutine(CalibrationRoutine());
        }

        private IEnumerator CalibrationRoutine()
        {
            _isCalibrating = true;
            _capturedSamples.Clear();
            _lastHeight = BackOfficeVaronia.Instance.MainCamera.transform.localPosition.y+BackOfficeVaronia.Instance.Rig.localPosition.y+0.09f;

            while (_capturedSamples.Count < targetSamples)
            {
                Transform cam = BackOfficeVaronia.Instance.MainCamera.transform;
                float currentHeight = cam.localPosition.y+BackOfficeVaronia.Instance.Rig.localPosition.y+0.09f;
                
                // 1. Check Angle (Tête droite ?)
                float angleX = cam.localEulerAngles.x;
                if (angleX > 180) angleX -= 360;
                bool isHeadLevel = Mathf.Abs(angleX) <= maxLookAngle;

                // 2. Check Stabilité (Est-ce qu'il est en train de s'accroupir/se lever ?)
                float verticalDelta = Mathf.Abs(currentHeight - _lastHeight);
                bool isStable = verticalDelta < maxVerticalSpeed;

                if (isHeadLevel && isStable && currentHeight > 0.5f)
                {
                    _capturedSamples.Add(currentHeight);
                }

                _lastHeight = currentHeight;
                yield return new WaitForSeconds(0.02f);
            }

            // --- TRAITEMENT ANTI-ACCROUPISSEMENT ---
            
            // On trie du plus petit au plus grand
            var sorted = _capturedSamples.OrderBy(n => n).ToList();

            // S'il s'est accroupi une partie du temps, les valeurs basses sont au début.
            // SOLUTION : On ne garde que le "Top 30%" des valeurs les plus hautes.
            // Pourquoi ? Parce qu'en VR, ta taille réelle est FORCÉMENT la valeur la plus haute 
            // que tu peux atteindre en étant stable et tête droite.
            int skipCount = (int)(sorted.Count * 0.7f); 
            var topSamples = sorted.Skip(skipCount).ToList(); 

            // On fait la moyenne du sommet de la pyramide
            Player_Size = topSamples.Average();

            Debug.Log($"<color=green>Calibration Finie.</color> Taille retenue (moyenne du top 30%): {Player_Size:F2}m");
            _isCalibrating = false;
        }
    }
}