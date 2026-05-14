using System.Collections.Generic;
using UnityEngine;

namespace VBO_Ultimate.Runtime.Scripts.Input
{
    /// <summary>
    /// Zone sphérique qui force le tracking "lost" sur les <see cref="VaroniaWeaponTracking"/>
    /// présentes à l'intérieur. Utile pour holsters, caisses, sacs, underwater, etc. —
    /// quand le tracker hardware peut accrocher un signal parasite alors qu'il est
    /// physiquement rangé / dans un environnement où on ne veut pas que l'arme bouge.
    ///
    /// Placez ce composant sur un GO positionné à l'emplacement de la zone (le composant
    /// utilise <c>transform.position</c> comme centre).
    ///
    /// Caractéristiques :
    /// - Hystérésis (enter &lt; exit) pour éviter le flapping en bordure de zone
    /// - Dwell time pour ignorer les passages rapides
    /// - Compteur de force-lost côté <see cref="VaroniaWeaponTracking"/> : plusieurs
    ///   zones peuvent se superposer sans se piler dessus (ex. holster + caisse)
    /// - Gizmo wire sphere pour visualiser les 2 rayons dans la Scene view
    /// - Auto-clean au OnDisable (libère le force-lost de toutes les armes qu'il maintenait)
    /// </summary>
    [AddComponentMenu("Varonia/Tracking Suppression Zone")]
    [DisallowMultipleComponent]
    public class TrackingSuppressionZone : MonoBehaviour
    {
        [Header("Hystérésis (mètres)")]
        [Tooltip("Distance à laquelle une arme entre dans la zone et déclenche le force-lost.")]
        [SerializeField, Min(0f)] private float enterRadius = 0.12f;

        [Tooltip("Distance à laquelle une arme sort de la zone et reprend son tracking. " +
                 "Doit être >= enterRadius pour éviter le flapping.")]
        [SerializeField, Min(0f)] private float exitRadius = 0.20f;

        [Header("Anti-clignote")]
        [Tooltip("Durée pendant laquelle l'arme doit rester dans le rayon d'entrée avant " +
                 "que le force-lost soit activé. Évite que des passages rapides déclenchent.")]
        [SerializeField, Min(0f)] private float enterDwell = 0.15f;

        [Header("Filtre par weaponIndex (optionnel)")]
        [Tooltip("Si renseigné, seules les armes dont VaroniaWeaponTracking.weaponIndex est " +
                 "dans cette liste sont impactées. Si vide, pas de filtrage par index.\n\n" +
                 "Exemple : un holster prévu pour l'arme à l'index 1 du loadout met [1].")]
        [SerializeField] private List<int> acceptedWeaponIndexes = new List<int>();

        [Header("Filtre par _WeaponInfo.ModelId (optionnel)")]
        [Tooltip("Si renseigné, seules les armes dont _WeaponInfo.ModelId (récupéré via " +
                 "weap.WeaponInfo) est dans cette liste sont impactées. Si vide, pas de filtrage " +
                 "par modèle.\n\nExemple : la zone holster Glock met [1] si Glock=ModelId 1. " +
                 "Robuste aux spawns dynamiques car ModelId est sur le SO _WeaponInfo.")]
        [SerializeField] private List<int> acceptedModelIds = new List<int>();

        [Tooltip("Si les 2 filtres sont renseignés : true = arme doit matcher LES DEUX (AND), " +
                 "false = arme doit matcher AU MOINS UN (OR). Sans effet si un seul filtre est utilisé.")]
        [SerializeField] private bool requireBothFilters = true;

        [Header("Gizmo")]
        [SerializeField] private Color gizmoEnterColor = new Color(1.00f, 0.30f, 0.30f, 0.90f);
        [SerializeField] private Color gizmoExitColor  = new Color(1.00f, 0.65f, 0.30f, 0.50f);
        [SerializeField] private bool  drawSolid       = true;

        [Header("Debug")]
        [Tooltip("Si true, log chaque frame le ModelId / weaponIndex de chaque arme + " +
                 "raison du skip si elle est exclue par les filtres. À activer ponctuellement " +
                 "pour diagnostiquer pourquoi une arme n'est pas catchée.")]
        [SerializeField] private bool debugLogs = false;
        private float _nextDebugTime;

        // ─── État interne par arme ────────────────────────────────────────────────
        private class TargetState
        {
            public bool  inZone;     // arme actuellement considérée comme dans la zone (post-dwell, post-hystérésis)
            public float dwellTimer; // accumulateur de temps passé dans le enterRadius
        }
        private readonly Dictionary<VaroniaWeaponTracking, TargetState> _states = new Dictionary<VaroniaWeaponTracking, TargetState>();

        // ─── Lifecycle ────────────────────────────────────────────────────────────
        private void OnValidate()
        {
            if (exitRadius < enterRadius) exitRadius = enterRadius;
        }

        private void OnDisable()
        {
            // Libère tous les force-lost qu'on maintenait → les armes reprennent leur état tracking normal
            foreach (var kv in _states)
            {
                if (kv.Key != null && kv.Value.inZone)
                    kv.Key.RemoveForceLost();
            }
            _states.Clear();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 center = transform.position;
            bool logThisFrame = debugLogs && Time.unscaledTime >= _nextDebugTime;
            if (logThisFrame) _nextDebugTime = Time.unscaledTime + 0.5f; // throttle à 2 Hz

            // Marqueur de "vu cette frame" pour cleanup des armes détruites/disabled
            // (sinon les TargetState s'accumulent ad vitam)
            // On utilise un set scratch — réutilisé via un membre static pour zéro alloc/frame.
            s_seenThisFrame.Clear();

            foreach (var w in VaroniaWeaponTracking.ActiveInstances)
            {
                if (w == null) continue;

                // Filtres (index + ModelId). Si une arme était DÉJÀ en zone et qu'on
                // change le filtre à chaud, le cleanup s_seenThisFrame en bas libère
                // proprement son force-lost.
                if (!PassesFilters(w))
                {
                    if (logThisFrame) Debug.Log(BuildDebugLine(w, "SKIP filtre"), this);
                    continue;
                }

                s_seenThisFrame.Add(w);

                // Position checkée : on prend le transform du _Weapon spawné si dispo
                // (= l'objet visuel qui bouge), sinon le VaroniaWeaponTracking lui-même.
                // Important si le tracking n'applique pas applyToParent.
                Transform tw = (w.weap != null) ? w.weap.transform : w.transform;
                float dist = Vector3.Distance(center, tw.position);

                if (logThisFrame) Debug.Log(BuildDebugLine(w, $"dist={dist:F3} m"), this);

                if (!_states.TryGetValue(w, out var state))
                {
                    state = new TargetState();
                    _states[w] = state;
                }

                if (!state.inZone)
                {
                    // Pas encore dans la zone : on accumule du dwell si on est <= enterRadius
                    if (dist <= enterRadius)
                    {
                        state.dwellTimer += dt;
                        if (state.dwellTimer >= enterDwell)
                        {
                            state.inZone = true;
                            w.AddForceLost();
                        }
                    }
                    else
                    {
                        state.dwellTimer = 0f;
                    }
                }
                else
                {
                    // Déjà dans la zone : sort si on dépasse exitRadius (hystérésis)
                    if (dist >= exitRadius)
                    {
                        state.inZone = false;
                        state.dwellTimer = 0f;
                        w.RemoveForceLost();
                    }
                }
            }

            // Cleanup : armes du dict qui n'existent plus ou plus actives → libère + retire
            if (_states.Count > s_seenThisFrame.Count)
            {
                s_keysToRemove.Clear();
                foreach (var kv in _states)
                {
                    if (!s_seenThisFrame.Contains(kv.Key))
                        s_keysToRemove.Add(kv.Key);
                }
                for (int i = 0; i < s_keysToRemove.Count; i++)
                {
                    var k = s_keysToRemove[i];
                    if (k != null && _states[k].inZone)
                        k.RemoveForceLost();
                    _states.Remove(k);
                }
            }
        }

        // Scratch reuse — évite l'alloc d'un HashSet / List par frame
        private static readonly HashSet<VaroniaWeaponTracking> s_seenThisFrame = new HashSet<VaroniaWeaponTracking>();
        private static readonly List<VaroniaWeaponTracking>    s_keysToRemove   = new List<VaroniaWeaponTracking>(4);

        /// <summary>
        /// Applique les 2 filtres optionnels (weaponIndex + ModelId). Listes vides = wildcard.
        /// Si les 2 sont renseignés, combinaison gouvernée par <see cref="requireBothFilters"/>.
        /// </summary>
        private bool PassesFilters(VaroniaWeaponTracking w)
        {
            bool hasIdxFilter   = acceptedWeaponIndexes.Count > 0;
            bool hasModelFilter = acceptedModelIds.Count > 0;

            // Aucun filtre actif = tout passe
            if (!hasIdxFilter && !hasModelFilter) return true;

            bool idxOk   = !hasIdxFilter   || ContainsInt(acceptedWeaponIndexes, w.weaponIndex);
            bool modelOk = !hasModelFilter || MatchesModelId(w);

            // Si un seul filtre est actif, on retourne juste son résultat.
            // Si les deux sont actifs, AND/OR selon le toggle.
            if (hasIdxFilter && hasModelFilter)
                return requireBothFilters ? (idxOk && modelOk) : (idxOk || modelOk);

            return hasIdxFilter ? idxOk : modelOk;
        }

        private static bool ContainsInt(List<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }

        private bool MatchesModelId(VaroniaWeaponTracking w)
        {
            // weap est assigné lors du spawn (VaroniaWeaponTracking → weap.Init(WeaponInfo)).
            // Si null ou pas encore init, on considère que l'arme ne matche pas le filtre
            // model (sinon on aurait un faux positif sur des armes spawnées mais pas binded).
            if (w.weap == null || w.weap.WeaponInfo == null) return false;
            return ContainsInt(acceptedModelIds, w.weap.WeaponInfo.ModelId);
        }

        /// <summary>
        /// Construit une ligne diagnostique pour debug : montre l'état actuel de
        /// l'arme (weaponIndex, ModelId si dispo) et la raison de l'inclusion/exclusion.
        /// </summary>
        private string BuildDebugLine(VaroniaWeaponTracking w, string suffix)
        {
            string weapStr = "weap=null";
            string infoStr = "";
            string modelStr = "(no ModelId)";

            if (w.weap != null)
            {
                weapStr = $"weap='{w.weap.name}'";
                if (w.weap.WeaponInfo != null)
                {
                    infoStr = $" info='{w.weap.WeaponInfo.name}'";
                    modelStr = $"ModelId={w.weap.WeaponInfo.ModelId}";
                }
                else
                {
                    infoStr = " info=null";
                }
            }
            return $"[TrackingSuppressionZone '{name}'] idx={w.weaponIndex} {modelStr} → {weapStr}{infoStr}  ({suffix})";
        }

        // ─── Gizmo ────────────────────────────────────────────────────────────────
        private void OnDrawGizmos()
        {
            Vector3 c = transform.position;

            // Sphère externe (exit) — plus pâle, en wire
            Gizmos.color = gizmoExitColor;
            Gizmos.DrawWireSphere(c, exitRadius);
            if (drawSolid)
            {
                var col = gizmoExitColor; col.a *= 0.15f;
                Gizmos.color = col;
                Gizmos.DrawSphere(c, exitRadius);
            }

            // Sphère interne (enter) — plus vive
            Gizmos.color = gizmoEnterColor;
            Gizmos.DrawWireSphere(c, enterRadius);
            if (drawSolid)
            {
                var col = gizmoEnterColor; col.a *= 0.25f;
                Gizmos.color = col;
                Gizmos.DrawSphere(c, enterRadius);
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Quand sélectionné, affiche les states actifs (lignes vers les armes ds la zone)
            if (!Application.isPlaying) return;
            Gizmos.color = Color.yellow;
            foreach (var kv in _states)
            {
                if (kv.Key == null || !kv.Value.inZone) continue;
                Gizmos.DrawLine(transform.position, kv.Key.transform.position);
            }
        }
    }
}
