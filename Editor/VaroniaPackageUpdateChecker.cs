using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

// UnityEditor.PackageInfo (legacy, obsolète) entre en conflit avec celui du Package Manager.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VaroniaBackOffice.EditorTools
{
    /// <summary>État de comparaison local/distant d'un package Varonia.</summary>
    internal class VaroniaPackageStatus
    {
        public string Name;           // com.varonia.xxx
        public string DisplayName;
        public string LocalVersion;
        public string RemoteVersion;
        public string RepoOwner;      // Varonia-Systems
        public string RepoName;       // UnityVBO
        public string Error;          // message si la vérification a échoué

        public bool Checked  => RemoteVersion != null || Error != null;
        public bool HasUpdate => RemoteVersion != null &&
                                 VaroniaPackageUpdateChecker.CompareSemver(RemoteVersion, LocalVersion) > 0;
        public string RepoUrl => RepoOwner != null ? $"https://github.com/{RepoOwner}/{RepoName}" : null;
    }

    /// <summary>
    /// Compare la version locale de chaque package Varonia (com.varonia.*) à celle du
    /// package.json publié sur son dépôt GitHub, et signale les mises à jour disponibles.
    /// Purement informatif : aucune mise à jour n'est appliquée automatiquement.
    ///
    /// Le dépôt est déduit :
    ///   • du remote git du dossier du package (installation locale "file:", cas des développeurs) ;
    ///   • ou de l'URL UPM du package s'il a été installé directement depuis Git (cas des jeux).
    /// </summary>
    [InitializeOnLoad]
    internal static class VaroniaPackageUpdateChecker
    {
        private const string PrefLastCheck = "VBO_PkgUpdate_LastCheckTicks";
        private const string PrefAutoCheck = "VBO_PkgUpdate_AutoCheck";
        private const double CheckIntervalHours = 24.0;

        private static readonly List<VaroniaPackageStatus> _statuses = new List<VaroniaPackageStatus>();
        private static ListRequest _listRequest;
        private static bool _running;
        private static bool _openWindowWhenDone;

        internal static IReadOnlyList<VaroniaPackageStatus> Statuses => _statuses;
        internal static bool IsRunning => _running;

        internal static bool AutoCheckEnabled
        {
            get => EditorPrefs.GetBool(PrefAutoCheck, true);
            set => EditorPrefs.SetBool(PrefAutoCheck, value);
        }

        static VaroniaPackageUpdateChecker()
        {
            // Différé : au chargement du domaine, le Package Manager n'est pas encore prêt.
            EditorApplication.delayCall += AutoCheckIfDue;
        }

        private static void AutoCheckIfDue()
        {
            if (!AutoCheckEnabled) return;

            long ticks = 0;
            long.TryParse(EditorPrefs.GetString(PrefLastCheck, "0"), out ticks);
            var last = ticks > 0 ? new DateTime(ticks) : DateTime.MinValue;

            if ((DateTime.UtcNow - last).TotalHours < CheckIntervalHours) return;

            // Vérification silencieuse : la fenêtre ne s'ouvre que s'il y a effectivement du neuf.
            Check(openWindowWhenDone: false, openOnlyIfUpdate: true);
        }

        [MenuItem("Varonia/Mises à jour des packages", priority = 500)]
        internal static void OpenWindow()
        {
            VaroniaPackageUpdateWindow.Open();
            if (_statuses.Count == 0 && !_running) Check(openWindowWhenDone: false);
        }

        private static bool _openOnlyIfUpdate;

        internal static void Check(bool openWindowWhenDone, bool openOnlyIfUpdate = false)
        {
            if (_running) return;

            _running            = true;
            _openWindowWhenDone = openWindowWhenDone;
            _openOnlyIfUpdate   = openOnlyIfUpdate;
            _statuses.Clear();
            EditorPrefs.SetString(PrefLastCheck, DateTime.UtcNow.Ticks.ToString());

            // offlineMode = true : on ne veut que la liste locale, pas un refresh du registre.
            _listRequest = Client.List(true, false);
            EditorApplication.update += PollList;
        }

        private static void PollList()
        {
            if (_listRequest == null || !_listRequest.IsCompleted) return;
            EditorApplication.update -= PollList;

            if (_listRequest.Status != StatusCode.Success)
            {
                _running = false;
                Debug.LogWarning("[Varonia] Liste des packages indisponible : " + _listRequest.Error?.message);
                VaroniaPackageUpdateWindow.RepaintIfOpen();
                return;
            }

            foreach (var pkg in _listRequest.Result)
            {
                if (pkg.name == null || !pkg.name.StartsWith("com.varonia.")) continue;

                var st = new VaroniaPackageStatus
                {
                    Name         = pkg.name,
                    DisplayName  = string.IsNullOrEmpty(pkg.displayName) ? pkg.name : pkg.displayName,
                    LocalVersion = pkg.version,
                };

                string gitUrl = ResolveGitUrl(pkg);
                if (!TryParseGitHub(gitUrl, out st.RepoOwner, out st.RepoName))
                    st.Error = "Aucun dépôt GitHub détecté (package non versionné ?)";

                _statuses.Add(st);
            }

            _statuses.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            _pending = 0;
            foreach (var st in _statuses)
                if (st.Error == null) { _pending++; FetchRemoteVersion(st); }

            if (_pending == 0) Finish();
            VaroniaPackageUpdateWindow.RepaintIfOpen();
        }

        private static int _pending;

        /// <summary>URL git du package : celle d'UPM si installé depuis Git, sinon le remote du dossier.</summary>
        private static string ResolveGitUrl(PackageInfo pkg)
        {
            // Installé depuis Git : packageId vaut "<nom>@<url git>". On n'utilise pas PackageInfo.repository,
            // absent des versions d'Unity les plus anciennes supportées par le package.
            if (pkg.source == PackageSource.Git && !string.IsNullOrEmpty(pkg.packageId))
            {
                int at = pkg.packageId.IndexOf('@');
                if (at >= 0 && at < pkg.packageId.Length - 1)
                    return pkg.packageId.Substring(at + 1);
            }

            // Installation locale (file:) ou embarquée → on lit le remote du dépôt du dossier.
            try
            {
                string cfg = Path.Combine(pkg.resolvedPath ?? "", ".git/config");
                if (File.Exists(cfg))
                {
                    var m = Regex.Match(File.ReadAllText(cfg), @"url\s*=\s*(\S+github\.com\S+)",
                                        RegexOptions.IgnoreCase);
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            catch { /* dossier illisible : traité comme 'pas de dépôt' */ }

            return null;
        }

        /// <summary>Extrait owner/repo d'une URL GitHub (forme https ou ssh).</summary>
        private static bool TryParseGitHub(string url, out string owner, out string repo)
        {
            owner = repo = null;
            if (string.IsNullOrEmpty(url)) return false;

            var m = Regex.Match(url, @"github\.com[/:]([^/]+)/([^/\s]+?)(\.git)?(\?|#|$)",
                                RegexOptions.IgnoreCase);
            if (!m.Success) return false;

            owner = m.Groups[1].Value;
            repo  = m.Groups[2].Value;
            return true;
        }

        // ─── Récupération de la version distante ────────────────────────────────────

        private static void FetchRemoteVersion(VaroniaPackageStatus st)
        {
            TryBranch(st, "main", () => TryBranch(st, "master", () =>
            {
                st.Error = "package.json introuvable (dépôt privé, ou branche autre que main/master)";
                OnePackageDone();
            }));
        }

        private static void TryBranch(VaroniaPackageStatus st, string branch, Action onFail)
        {
            string url = $"https://raw.githubusercontent.com/{st.RepoOwner}/{st.RepoName}/{branch}/package.json";
            var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            var op = req.SendWebRequest();

            void Poll()
            {
                if (!op.isDone) return;
                EditorApplication.update -= Poll;

#if UNITY_2020_1_OR_NEWER
                bool ok = req.result == UnityWebRequest.Result.Success;
#else
                bool ok = !(req.isNetworkError || req.isHttpError);
#endif
                if (ok)
                {
                    st.RemoteVersion = ExtractVersion(req.downloadHandler.text);
                    if (st.RemoteVersion == null)
                        st.Error = "Version illisible dans le package.json distant";
                    req.Dispose();
                    OnePackageDone();
                }
                else
                {
                    req.Dispose();
                    onFail();
                }
            }

            EditorApplication.update += Poll;
        }

        /// <summary>Lit le champ "version" d'un package.json sans dépendre d'un désérialiseur.</summary>
        private static string ExtractVersion(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var m = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static void OnePackageDone()
        {
            _pending--;
            VaroniaPackageUpdateWindow.RepaintIfOpen();
            if (_pending <= 0) Finish();
        }

        private static void Finish()
        {
            _running = false;

            bool anyUpdate = false;
            foreach (var s in _statuses) if (s.HasUpdate) { anyUpdate = true; break; }

            if (_openWindowWhenDone || (_openOnlyIfUpdate && anyUpdate))
                VaroniaPackageUpdateWindow.Open();

            VaroniaPackageUpdateWindow.RepaintIfOpen();
        }

        // ─── Semver ────────────────────────────────────────────────────────────────

        /// <summary>Compare deux versions "x.y.z" : &gt;0 si a est plus récente que b.
        /// Les suffixes (-preview, +build) sont ignorés.</summary>
        internal static int CompareSemver(string a, string b)
        {
            int[] va = ParseSemver(a), vb = ParseSemver(b);
            for (int i = 0; i < 3; i++)
                if (va[i] != vb[i]) return va[i].CompareTo(vb[i]);
            return 0;
        }

        private static int[] ParseSemver(string v)
        {
            var res = new int[3];
            if (string.IsNullOrEmpty(v)) return res;

            int cut = v.IndexOfAny(new[] { '-', '+' });
            if (cut > 0) v = v.Substring(0, cut);

            var parts = v.Split('.');
            for (int i = 0; i < 3 && i < parts.Length; i++)
                int.TryParse(parts[i], out res[i]);
            return res;
        }
    }
}
