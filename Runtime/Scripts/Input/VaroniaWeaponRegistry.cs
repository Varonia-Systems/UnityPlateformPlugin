using System.Collections.Generic;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Liste stable des armes "papas" construite UNE FOIS au démarrage depuis
    /// <see cref="GlobalConfig.Devices"/> (le JSON ne bouge plus ensuite).
    ///
    /// - Chaque papa (device racine, sans LinkParent) a un index stable = sa position
    ///   dans cette liste (les trackers enfants sont exclus). Cet index sert de slot
    ///   pour <see cref="VaroniaInput"/>.
    /// - Les <c>VaroniaWeaponTracking</c> "réclament" (claim) une entrée au Start pour
    ///   s'attribuer un slot ; deux armes de scène ne peuvent pas prendre la même entrée.
    /// - Le claim est purement runtime (jamais écrit dans le JSON) et se libère au OnDisable.
    /// </summary>
    public static class VaroniaWeaponRegistry
    {
        private static readonly List<WeaponBinding> _parents = new List<WeaponBinding>();
        private static bool[] _claimed = new bool[0];
        private static GlobalConfig _config;

        /// <summary>Nombre d'armes (papas) dans le snapshot.</summary>
        public static int Count => _parents.Count;

        /// <summary>
        /// Slot de l'arme par défaut : le premier papa <see cref="WeaponBinding.IsDefault"/>,
        /// à défaut 0 (premier papa). Sert aux API rétrocompat sans index de VaroniaInput.
        /// </summary>
        public static int DefaultIndex
        {
            get
            {
                for (int i = 0; i < _parents.Count; i++)
                    if (_parents[i].IsDefault) return i;
                return 0;
            }
        }

        /// <summary>Construit le snapshot des papas. À appeler après le chargement de la config.</summary>
        public static void Build(GlobalConfig config)
        {
            _config = config;
            _parents.Clear();
            if (config != null && config.Devices != null)
            {
                foreach (var d in config.Devices)
                    if (GlobalConfig.IsParent(d)) _parents.Add(d);
            }
            _claimed = new bool[_parents.Count];
        }

        /// <summary>Papa à l'index (slot) donné, ou null.</summary>
        public static WeaponBinding GetParent(int index) =>
            (index >= 0 && index < _parents.Count) ? _parents[index] : null;

        /// <summary>Index (slot) du premier papa ayant ce Controller, ou -1. Ne réclame rien.</summary>
        public static int IndexOfController(Controller c)
        {
            for (int i = 0; i < _parents.Count; i++)
                if (_parents[i].Controller == c) return i;
            return -1;
        }

        /// <summary>Enfant tracker du papa à cet index (via LinkParent == papa.Identifier), ou null.</summary>
        public static WeaponBinding GetTracker(int index)
        {
            var p = GetParent(index);
            return (p != null && _config != null) ? _config.GetTrackerChild(p.Identifier) : null;
        }

        /// <summary>
        /// Réclame une arme et renvoie son index (slot), ou -1 si rien de disponible.
        /// - <paramref name="forceController"/> = true → premier papa NON réclamé dont le
        ///   Controller vaut <paramref name="wanted"/>.
        /// - sinon → le papa <see cref="WeaponBinding.IsDefault"/> non réclamé, à défaut le
        ///   premier papa non réclamé de la liste.
        /// </summary>
        public static int Claim(bool forceController, Controller wanted)
        {
            if (forceController)
            {
                for (int i = 0; i < _parents.Count; i++)
                    if (!_claimed[i] && _parents[i].Controller == wanted) { _claimed[i] = true; return i; }
                return -1;
            }

            // 1) le default non réclamé
            for (int i = 0; i < _parents.Count; i++)
                if (!_claimed[i] && _parents[i].IsDefault) { _claimed[i] = true; return i; }

            // 2) sinon le premier non réclamé
            for (int i = 0; i < _parents.Count; i++)
                if (!_claimed[i]) { _claimed[i] = true; return i; }

            return -1;
        }

        /// <summary>Libère l'entrée réclamée (arme détruite/désactivée) pour qu'elle soit re-réclamable.</summary>
        public static void Release(int index)
        {
            if (index >= 0 && index < _claimed.Length) _claimed[index] = false;
        }
    }
}
