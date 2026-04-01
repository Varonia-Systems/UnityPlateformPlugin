using System.Collections.Generic;
using UnityEngine;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Dessine les boundaries chargées par VaroniaSpatialLoader via des LineRenderers au sol.
    /// Placez ce composant sur un GameObject vide dans votre scène.
    /// </summary>
    public class BasicBoundary : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────────

        [Tooltip("Matériau des lignes. Laisser vide pour utiliser un matériau Unlit généré automatiquement.")]
        [SerializeField] private Material lineMaterial;

        [Tooltip("Épaisseur des lignes en unités monde.")]
        [SerializeField] private float lineWidth = 0.05f;

        [Tooltip("Décalage Y des lignes par rapport au sol.")]
        [SerializeField] private float yOffset = 0.02f;

        [Tooltip("Utiliser la coordonnée Y des points (sinon tout est aplati à yOffset).")]
        [SerializeField] private bool usePointY = false;

        // ─── Private ──────────────────────────────────────────────────────────────

        private readonly List<GameObject> _boundaryObjects = new List<GameObject>();

        // ─────────────────────────────────────────────────────────────────────────

        private void Start()
        {
            if (VaroniaSpatialLoader.Data != null)
                Build();
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
            Build();
        }

        // ─── Build ────────────────────────────────────────────────────────────────

        public void Build()
        {
            Clear();

            var spatial = VaroniaSpatialLoader.Data as Spatial;
            if (spatial?.Boundaries == null || spatial.Boundaries.Count == 0)
            {
                Debug.LogWarning("[BasicBoundary] Aucune boundary dans les données spatiales.");
                return;
            }

            for (int i = 0; i < spatial.Boundaries.Count; i++)
                BuildBoundaryLine(spatial.Boundaries[i], i);

            Debug.Log($"[BasicBoundary] {_boundaryObjects.Count} boundary(ies) dessinée(s).");
        }

        private void BuildBoundaryLine(Boundary_ boundary, int index)
        {
            if (boundary?.Points == null || boundary.Points.Count < 2) return;

            var go = new GameObject($"Boundary_{index}");
            go.transform.SetParent(transform, false);
            _boundaryObjects.Add(go);

            var lr = go.AddComponent<LineRenderer>();

            // Color
            var   bc  = boundary.BoundaryColor;
            Color col = bc != null ? new Color(bc.x, bc.y, bc.z, 1f) : Color.green;

            // Material
            lr.material = lineMaterial != null
                ? lineMaterial
                : BuildUnlitMaterial(col);

            lr.startColor       = col;
            lr.endColor         = col;
            lr.widthMultiplier  = boundary.MainBoundary ? lineWidth * 2f : lineWidth;
            lr.loop             = true;
            lr.numCornerVertices    = 20;
            lr.useWorldSpace    = false;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows   = false;
            lr.positionCount    = boundary.Points.Count;

            for (int i = 0; i < boundary.Points.Count; i++)
            {
                var p = boundary.Points[i];
                float y = usePointY ? p.y + yOffset : yOffset;
                lr.SetPosition(i, new Vector3(p.x, y, p.z));
            }
        }

        // ─── Clear ────────────────────────────────────────────────────────────────

        public void Clear()
        {
            foreach (var go in _boundaryObjects)
                if (go != null) Destroy(go);
            _boundaryObjects.Clear();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static Material BuildUnlitMaterial(Color col)
        {
            var shader = Shader.Find("Unlit/Color")
                      ?? Shader.Find("Hidden/Internal-Colored")
                      ?? Shader.Find("Sprites/Default");

            var mat    = new Material(shader != null ? shader : Shader.Find("Standard"));
            mat.color  = col;
            return mat;
        }
    }
}
