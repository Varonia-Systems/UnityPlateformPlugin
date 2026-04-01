using UnityEngine;

namespace VBO_Ultimate.Runtime.Scripts.Input
{
    [ExecuteAlways]
    public class WeaponOffsetCalibrator : MonoBehaviour
    {
        [HideInInspector]
        public WeaponGhostLibrary ghostLibrary;
        public int selectedIndex = 0;

        [Header("Gizmo")]
        public Color ghostColor = new Color(1f, 0f, 0f, 0.3f);
        public Color wireColor  = new Color(1f, 0f, 0f, 1f);

        public WeaponGhostEntry ActiveEntry =>
            ghostLibrary != null &&
            ghostLibrary.entries != null &&
            selectedIndex >= 0 &&
            selectedIndex < ghostLibrary.entries.Count
                ? ghostLibrary.entries[selectedIndex]
                : null;

        private void OnValidate() => EnforceLocalZero();

        private void Update() => EnforceLocalZero();

        private void EnforceLocalZero()
        {
            if (transform.localPosition != Vector3.zero)
                transform.localPosition = Vector3.zero;
            if (transform.localRotation != Quaternion.identity)
                transform.localRotation = Quaternion.identity;
            if (transform.localScale != Vector3.one)
                transform.localScale = Vector3.one;
        }

        private void OnDrawGizmos()
        { 
            var entry = ActiveEntry;
            if (entry == null || entry.mesh == null) return;

            Vector3 pos = transform.position + transform.rotation * entry.positionOffset;
            Quaternion rot = transform.rotation * Quaternion.Euler(entry.rotationOffset);

            Gizmos.color = ghostColor;
            Gizmos.DrawMesh(entry.mesh, pos, rot, Vector3.one);

            Gizmos.color = wireColor;
            Gizmos.DrawWireMesh(entry.mesh, pos, rot, Vector3.one);
        }
    }
}
