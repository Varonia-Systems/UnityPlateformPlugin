using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VaroniaBackOffice
{
    /// <summary>
    /// Déplace le Rig (root de la main caméra) en debug mode (F10).
    /// ZQSD : avant/arrière/gauche/droite (axe Y verrouillé)
    /// R/F  : monter/descendre
    /// A/E  : rotation gauche/droite
    /// LShift : vitesse rapide
    /// </summary>
    public class DebugCameraRig : MonoBehaviour
    {
        // ─── Config ───────────────────────────────────────────────────────────────

        [Header("Rig")]
        [Tooltip("Le Transform root de la caméra (Rig). Si vide, utilise ce GameObject.")]
        [SerializeField] private Transform rig;

        [Tooltip("La caméra dont on lit la direction horizontale. Si vide, utilise Camera.main.")]
        [SerializeField] private Camera    cam;

        [Header("Speed")]
        [SerializeField] private float moveSpeed     = 3f;
        [SerializeField] private float fastMultiplier = 4f;
        [SerializeField] private float rotateSpeed   = 60f;

        // ─────────────────────────────────────────────────────────────────────────

        private void Reset()
        {
            cam = Camera.main;
            if (cam != null)
            {
                // Remonte jusqu'au root du rig (parent le plus haut)
                Transform t = cam.transform;
                while (t.parent != null) t = t.parent;
                rig = t;
            }
        }

        private void Awake()
        {
            AutoFillReferences();
        }

        private void AutoFillReferences()
        {
            if (cam == null) cam = Camera.main;
            if (rig == null && cam != null) rig = cam.transform.root;
        }

        private void Update()
        {
            if (rig == null || cam == null) AutoFillReferences();
            if (!DebugModeOverlay.IsSuperDebugMode) return;

            float dt    = Time.deltaTime;
            bool  fast  = IsKey(ShiftKey());
            float speed = moveSpeed * (fast ? fastMultiplier : 1f);

            // ── Direction horizontale de la caméra (axe Y verrouillé) ──
            Vector3 forward = Vector3.zero;
            Vector3 right   = Vector3.zero;

            if (cam != null)
            {
                forward = cam.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.001f) forward.Normalize();

                right = cam.transform.right;
                right.y = 0f;
                if (right.sqrMagnitude > 0.001f) right.Normalize();
            }

            // ── Translation ──
            Vector3 move = Vector3.zero;

            if (IsKey(ZKey()))  move += forward;
            if (IsKey(SKey()))  move -= forward;
            if (IsKey(DKey()))  move += right;
            if (IsKey(QKey()))  move -= right;
            if (IsKey(RKey()))  move += Vector3.up;
            if (IsKey(FKey()))  move -= Vector3.up;

            if (move.sqrMagnitude > 0.001f)
                rig.position += move.normalized * speed * dt;

            // ── Rotation (A/E) ──
            float rot = 0f;
            if (IsKey(AKey())) rot -= rotateSpeed * dt;
            if (IsKey(EKey())) rot += rotateSpeed * dt;

            if (rot != 0f)
                rig.Rotate(Vector3.up, rot, Space.World);

            // ── Reset position/rotation via T ──
            if (IsKeyDown(TKey()))
            {
                var spatial = BackOfficeVaronia.Spatial;
                if (spatial != null)
                {
                    if (spatial.SyncPos != null)
                        rig.position = spatial.SyncPos.asVec3();
                    if (spatial.SyncQuaterion != null)
                        rig.rotation = spatial.SyncQuaterion.asQuat();
                }
            }
        }

        // ─── Input abstraction (new / legacy) ────────────────────────────────────

#if ENABLE_INPUT_SYSTEM
        private static bool IsKey(Key k)
        {
            var kb = Keyboard.current;
            return kb != null && kb[k].isPressed;
        }
        private static bool IsKeyDown(Key k)
        {
            var kb = Keyboard.current;
            return kb != null && kb[k].wasPressedThisFrame;
        }
        // Positions physiques AZERTY : Z=W(qwerty), Q=A(qwerty), A=Q(qwerty), E=E, S=S, D=D, R=R, F=F
        private static Key ZKey()     => Key.W;
        private static Key QKey()     => Key.A;
        private static Key SKey()     => Key.S;
        private static Key DKey()     => Key.D;
        private static Key RKey()     => Key.R;
        private static Key FKey()     => Key.F;
        private static Key AKey()     => Key.Q;
        private static Key EKey()     => Key.E;
        private static Key TKey()     => Key.T;
        private static Key ShiftKey() => Key.LeftShift;
#else
        private static bool IsKey(KeyCode k)     => Input.GetKey(k);
        private static bool IsKeyDown(KeyCode k) => Input.GetKeyDown(k);
        private static KeyCode ZKey()     => KeyCode.Z;
        private static KeyCode QKey()     => KeyCode.Q;
        private static KeyCode SKey()     => KeyCode.S;
        private static KeyCode DKey()     => KeyCode.D;
        private static KeyCode RKey()     => KeyCode.R;
        private static KeyCode FKey()     => KeyCode.F;
        private static KeyCode AKey()     => KeyCode.A;
        private static KeyCode EKey()     => KeyCode.E;
        private static KeyCode TKey()     => KeyCode.T;
        private static KeyCode ShiftKey() => KeyCode.LeftShift;
#endif
    }
}
