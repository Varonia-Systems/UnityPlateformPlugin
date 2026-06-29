// Découverte automatique des packages tuto.
//
// Scanne toutes les assemblies chargées (via TypeCache) pour trouver les
// implémentations de IVaroniaTutorialProvider, les instancie, et collecte
// leurs TutorialPackage. Le résultat est trié par Order puis par nom.
//
// Le cache est invalidé après chaque recompilation (DidReloadScripts) et
// peut être rafraîchi à la main depuis la fenêtre.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice.Tutorials
{
    public static class VaroniaTutorialRegistry
    {
        private static List<TutorialPackage> _cache;

        public static IReadOnlyList<TutorialPackage> Packages => _cache ?? (_cache = Build());

        public static void Refresh() => _cache = Build();

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded() => _cache = null;

        private static List<TutorialPackage> Build()
        {
            var packages = new List<TutorialPackage>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<IVaroniaTutorialProvider>())
            {
                if (type.IsAbstract || type.IsInterface) continue;

                try
                {
                    var provider = (IVaroniaTutorialProvider)Activator.CreateInstance(type);
                    var pkg = provider.GetPackage();
                    if (pkg != null && pkg.Cards != null && pkg.Cards.Count > 0)
                        packages.Add(pkg);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Varonia Tuto] Le provider '{type.FullName}' a échoué : {e.Message}");
                }
            }

            return packages
                .OrderBy(p => p.Order)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
