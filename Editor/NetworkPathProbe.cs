using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Existence d'un fichier ou d'un dossier, testée en tâche de fond et mise en cache, pour les chemins qui peuvent
    /// être sur le réseau (NAS, serveur de build). À utiliser dans OnGUI à la place de Directory.Exists / File.Exists :
    /// sur un partage lent, endormi ou injoignable, ces appels bloquent le fil principal le temps du délai SMB
    /// (jusqu'à plusieurs dizaines de secondes) — et une fenêtre redessinée 10 fois par seconde fige tout l'éditeur.
    /// Tant que le premier test n'a pas abouti (quelques ms sur un partage qui répond), le chemin est « inconnu » :
    /// Exists renvoie faux, Missing aussi. Le résultat est rafraîchi toutes les <see cref="RefreshSeconds"/> s, sans
    /// jamais deux tests en même temps pour un même chemin ; les fenêtres sont redessinées quand il change.
    /// </summary>
    [InitializeOnLoad]
    internal static class NetworkPathProbe
    {
        const double RefreshSeconds = 5.0;

        class Entry
        {
            public volatile bool Exists;
            public volatile bool Known;
            public volatile bool Running;
            public double NextCheck;
        }

        static readonly Dictionary<string, Entry> s_entries = new Dictionary<string, Entry>();
        static volatile bool s_changed;

        static NetworkPathProbe()
        {
            EditorApplication.update += RepaintIfChanged;
        }

        /// <summary>Vrai si le dossier existe (faux tant que ce n'est pas encore connu).</summary>
        public static bool DirectoryExists(string path) => Get(path, false).Exists;

        /// <summary>Vrai si le fichier existe (faux tant que ce n'est pas encore connu).</summary>
        public static bool FileExists(string path) => Get(path, true).Exists;

        /// <summary>Vrai seulement si un test a abouti et que le dossier n'existe pas (pour un avertissement « manquant »).</summary>
        public static bool DirectoryMissing(string path)
        {
            var e = Get(path, false);
            return e.Known && !e.Exists;
        }

        /// <summary>Relance tous les tests au prochain appel (après une copie ou une création de dossier).</summary>
        public static void Invalidate()
        {
            foreach (var e in s_entries.Values) e.NextCheck = 0;
        }

        static Entry Get(string path, bool isFile)
        {
            if (string.IsNullOrEmpty(path)) return new Entry { Known = true };
            string key = (isFile ? "f:" : "d:") + path;
            if (!s_entries.TryGetValue(key, out var e)) s_entries[key] = e = new Entry();

            double now = EditorApplication.timeSinceStartup;
            if (!e.Running && now >= e.NextCheck)
            {
                e.Running = true;
                e.NextCheck = now + RefreshSeconds;
                Task.Run(() =>
                {
                    bool exists;
                    try { exists = isFile ? File.Exists(path) : Directory.Exists(path); }
                    catch { exists = false; }
                    if (!e.Known || exists != e.Exists) s_changed = true;
                    e.Exists = exists;
                    e.Known = true;
                    e.Running = false;
                });
            }
            return e;
        }

        static void RepaintIfChanged()
        {
            if (!s_changed) return;
            s_changed = false;
            InternalEditorUtility.RepaintAllViews();
        }
    }
}
