using UnityEngine;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Applique SyncPos + SyncQuaternion sur son transform et instancie UNE SEULE FOIS
    /// le boundaryPrefab, lequel gère lui-même toutes les Boundary.
    /// </summary>
    public class VaroniaSync : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────────

        [Tooltip("Prefab instancié une fois. Doit avoir un composant BoundaryVisual.")]
        [SerializeField] private GameObject boundaryPrefab;

        // ─── Private ──────────────────────────────────────────────────────────────

        private GameObject _instance;

        // ─────────────────────────────────────────────────────────────────────────

        private void Start()
        {
            if (BackOfficeVaronia.Instance != null)
            {
                BackOfficeVaronia.Instance.Rig = transform;
            }

            if (VaroniaSpatialLoader.Data != null)
                Apply();
            else
                VaroniaSpatialLoader.OnLoaded += OnSpatialLoaded;
        }

        private void OnDestroy()
        {
            VaroniaSpatialLoader.OnLoaded -= OnSpatialLoaded;
        }

        private void OnSpatialLoaded()
        {
            VaroniaSpatialLoader.OnLoaded -= OnSpatialLoaded;
            Apply();
        }

        // ─── Apply ────────────────────────────────────────────────────────────────

        public void Apply()
        {
            var spatial = VaroniaSpatialLoader.Data as Spatial;
            if (spatial == null)
            {
                Debug.LogWarning("[VaroniaSync] Données Spatial introuvables.");
                return;
            }

            // ── Transform ─────────────────────────────────────────────────────────
            if (spatial.SyncPos != null)
                transform.position = spatial.SyncPos.asVec3();

            if (spatial.SyncQuaterion != null)
                transform.rotation = spatial.SyncQuaterion.asQuat();

            // ── Prefab (une seule instance) ───────────────────────────────────────
            if (_instance != null)
                Destroy(_instance);

            if (boundaryPrefab == null)
            {
                Debug.LogWarning("[VaroniaSync] Aucun boundaryPrefab assigné.");
                return;
            }

            _instance = Instantiate(boundaryPrefab, transform);
            _instance.transform.localPosition = new Vector3(0,0.1f,0);
            
        }

        // ─── Accessors (Editor) ───────────────────────────────────────────────────

        public bool HasPrefab => boundaryPrefab != null;
    }
}
