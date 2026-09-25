using System.Collections;
using UnityEngine;

namespace VaroniaBackOffice
{
    public class VaroniaPosMul : MonoBehaviour
    {
        [SerializeField] public Transform camTransform;
        [SerializeField] public float coefMul = 0.1f;

        private IEnumerator Start()
        {
            // DontUseSpatialSync : pas de coef mul, l'objet reste tel qu'il est placé dans la scène.
            if (GlobalConfig.SpatialSyncDisabled)
            {
                coefMul = 0f;
                yield break;
            }

            yield return new WaitUntil(() => VaroniaSpatialLoader.Data != null);

            var spatial = VaroniaSpatialLoader.Data as Spatial;
            coefMul = spatial != null ? (float)spatial.Multiplier : 0f;
        }

        private void Update()
        {
            if (GlobalConfig.SpatialSyncDisabled)
                return;

            if (camTransform == null)
            {
                if (Camera.main != null)
                    camTransform = Camera.main.transform;
                return;
            }

            transform.localPosition = new Vector3(
                camTransform.localPosition.x * coefMul,
                0f,
                camTransform.localPosition.z * coefMul
            );
        }
    }
}
