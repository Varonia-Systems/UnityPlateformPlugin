using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Debug = UnityEngine.Debug;

// UnityEditor.PackageInfo (legacy, obsolète) entre en conflit avec celui du Package Manager.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VaroniaBackOffice.EditorTools
{
    /// <summary>État de comparaison local/distant d'un package Varonia.</summary>
    internal class VaroniaPackageStatus
    {
        public string Name;            // com.varonia.xxx
        public string DisplayName;
        public string LocalVersion;
        public string GitUrl;          // URL UPM complète (peut contenir #branche)
        public string Branch;          // branche/tag demandée (null = HEAD par défaut du dépôt)
        public bool   IsLocalFolder;   // installé en file:/embedded (machine de dev)
        public string LocalPath;       // resolvedPath si dossier local

        public string LocalHash;       // commit installé (UPM) ou HEAD du dossier local
        public string RemoteHash;      // HEAD distant
        public int    AheadCount;      // dossier local : commits non poussés
        public int    DirtyCount;      // dossier local : fichiers modifiés non commités
        public string Error;

        public volatile bool Done;

        public bool Checked   => Done;
        public bool HasUpdate => Done && Error == null && RemoteHash != null && LocalHash != null && RemoteHash != LocalHash;
        public bool NeedsPush => Done && IsLocalFolder && (AheadCount > 0 || DirtyCount > 0);

        public string RepoUrl
        {
            get
            {
                var m = Regex.Match(GitUrl ?? "", @"(https?://[^#?]+?)(\.git)?(\?|#|$)");
                return m.Success ? m.Groups[1].Value : null;
            }
        }

        public static string Short(string hash) => string.IsNullOrEmpty(hash) ? "?" : hash.Substring(0, Math.Min(7, hash.Length));
    }

    /// <summary>
    /// Compare le commit installé de chaque package Varonia (com.varonia.*) au HEAD de son dépôt Git
    /// via <c>git ls-remote</c> (identifiants Git de la machine : fonctionne sur les dépôts privés,
    /// exactement comme le Package Manager). Propose la mise à jour (Client.Add).
    ///
    /// Packages installés en <c>file:</c> (poste de développeur) : compare le HEAD du dossier au distant
    /// et signale les commits non poussés / fichiers non commités.
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
        private static bool _openOnlyIfUpdate;

        internal static IReadOnlyList<VaroniaPackageStatus> Statuses => _statuses;
        internal static bool IsRunning => _running;
        internal static bool IsUpdating => _updateQueue.Count > 0 || _addRequest != null;
        internal static string UpdatingName => _addRequest != null ? _updatingName : null;

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
            Check(openWindowWhenDone: false, openOnlyIfUpdate: true);
        }

        [MenuItem("Varonia/Mises à jour des packages", priority = 500)]
        internal static void OpenWindow()
        {
            VaroniaPackageUpdateWindow.Open();
            if (_statuses.Count == 0 && !_running) Check(openWindowWhenDone: false);
        }

        // ─── Vérification ─────────────────────────────────────────────────────────

        internal static void Check(bool openWindowWhenDone, bool openOnlyIfUpdate = false)
        {
            if (_running) return;
            _running            = true;
            _openWindowWhenDone = openWindowWhenDone;
            _openOnlyIfUpdate   = openOnlyIfUpdate;
            _statuses.Clear();
            EditorPrefs.SetString(PrefLastCheck, DateTime.UtcNow.Ticks.ToString());

            _listRequest = Client.List(true, false); // liste locale uniquement, pas de refresh du registre
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
                    Name          = pkg.name,
                    DisplayName   = string.IsNullOrEmpty(pkg.displayName) ? pkg.name : pkg.displayName,
                    LocalVersion  = pkg.version,
                    IsLocalFolder = pkg.source == PackageSource.Local || pkg.source == PackageSource.Embedded,
                    LocalPath     = pkg.resolvedPath,
                };

                if (pkg.source == PackageSource.Git)
                {
                    // packageId = "<nom>@<url git>[#revision]"
                    int at = pkg.packageId.IndexOf('@');
                    st.GitUrl    = at >= 0 ? pkg.packageId.Substring(at + 1) : null;
                    st.LocalHash = pkg.git != null ? pkg.git.hash : null;
                    st.Branch    = pkg.git != null && !string.IsNullOrEmpty(pkg.git.revision) ? pkg.git.revision : null;
                    if (st.GitUrl == null) st.Error = "URL Git introuvable";
                }
                else if (st.IsLocalFolder)
                {
                    st.GitUrl = ReadRemoteFromGitConfig(pkg.resolvedPath);
                    if (st.GitUrl == null) st.Error = "Dossier local sans dépôt Git (ou sans remote)";
                }
                else st.Error = "Source " + pkg.source + " non gérée";

                if (st.Error != null) st.Done = true;
                _statuses.Add(st);
            }

            _statuses.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            foreach (var st in _statuses)
                if (!st.Done) { var s = st; Task.Run(() => Probe(s)); }

            EditorApplication.update += PollProbes;
            VaroniaPackageUpdateWindow.RepaintIfOpen();
        }

        private static void PollProbes()
        {
            foreach (var st in _statuses)
                if (!st.Done) { VaroniaPackageUpdateWindow.RepaintIfOpen(); return; }
            EditorApplication.update -= PollProbes;
            Finish();
        }

        /// <summary>Thread de fond : interroge le dépôt distant (et le dépôt local si file:).</summary>
        private static void Probe(VaroniaPackageStatus st)
        {
            try
            {
                string url     = StripRevision(st.GitUrl);
                string refName = string.IsNullOrEmpty(st.Branch) ? "HEAD" : st.Branch;

                var remote = RunGit("ls-remote \"" + url + "\" " + refName, null);
                if (remote.code != 0 || string.IsNullOrWhiteSpace(remote.stdout))
                {
                    st.Error = "ls-remote a échoué : " + Truncate(remote.stderr, 160);
                    return;
                }
                st.RemoteHash = remote.stdout.Trim().Split('\t', ' ')[0];

                if (st.IsLocalFolder)
                {
                    var head = RunGit("rev-parse HEAD", st.LocalPath);
                    st.LocalHash = head.code == 0 ? head.stdout.Trim() : null;

                    var ahead = RunGit("rev-list --count @{u}..HEAD", st.LocalPath);
                    int.TryParse(ahead.stdout.Trim(), out st.AheadCount);

                    var status = RunGit("status --porcelain", st.LocalPath);
                    int dirty = 0;
                    foreach (var line in status.stdout.Split('\n'))
                        if (line.Trim().Length > 0 && !line.Contains(".claude")) dirty++;
                    st.DirtyCount = dirty;
                }
            }
            catch (Exception e) { st.Error = e.Message; }
            finally { st.Done = true; }
        }

        private static void Finish()
        {
            _running = false;
            bool anyNews = false;
            foreach (var s in _statuses) if (s.HasUpdate || s.NeedsPush) { anyNews = true; break; }

            if (anyNews)
            {
                var sb = new System.Text.StringBuilder("[Varonia] Packages : ");
                foreach (var s in _statuses)
                {
                    if (s.HasUpdate) sb.Append(s.DisplayName).Append(" (mise à jour dispo) · ");
                    if (s.NeedsPush) sb.Append(s.DisplayName).Append(" (travail local à pousser) · ");
                }
                Debug.Log(sb.ToString());
            }

            if (_openWindowWhenDone || (_openOnlyIfUpdate && anyNews))
                VaroniaPackageUpdateWindow.Open();
            VaroniaPackageUpdateWindow.RepaintIfOpen();
        }

        // ─── Mise à jour (Client.Add sur l'URL Git = bouton "Update" du Package Manager) ───

        private static readonly Queue<VaroniaPackageStatus> _updateQueue = new Queue<VaroniaPackageStatus>();
        private static AddRequest _addRequest;
        private static string _updatingName;

        internal static void Update(VaroniaPackageStatus st)
        {
            if (st == null || st.IsLocalFolder || string.IsNullOrEmpty(st.GitUrl)) return;
            _updateQueue.Enqueue(st);
            if (_addRequest == null) StartNextUpdate();
        }

        internal static void UpdateAll()
        {
            foreach (var st in _statuses)
                if (st.HasUpdate && !st.IsLocalFolder) _updateQueue.Enqueue(st);
            if (_addRequest == null) StartNextUpdate();
        }

        private static void StartNextUpdate()
        {
            if (_updateQueue.Count == 0) { _addRequest = null; VaroniaPackageUpdateWindow.RepaintIfOpen(); return; }
            var st = _updateQueue.Dequeue();
            _updatingName = st.DisplayName;
            _addRequest = Client.Add(st.GitUrl);
            EditorApplication.update += PollAdd;
            VaroniaPackageUpdateWindow.RepaintIfOpen();
        }

        private static void PollAdd()
        {
            if (_addRequest == null || !_addRequest.IsCompleted) return;
            EditorApplication.update -= PollAdd;

            if (_addRequest.Status == StatusCode.Success)
                Debug.Log("[Varonia] " + _addRequest.Result.name + " mis à jour → " + _addRequest.Result.version +
                          (_addRequest.Result.git != null ? " @ " + VaroniaPackageStatus.Short(_addRequest.Result.git.hash) : ""));
            else
                Debug.LogError("[Varonia] Mise à jour de " + _updatingName + " échouée : " + _addRequest.Error?.message);

            _addRequest = null;
            if (_updateQueue.Count > 0) StartNextUpdate();
            else EditorApplication.delayCall += () => Check(openWindowWhenDone: false); // rafraîchit les hashs affichés
        }

        // ─── Git helpers ──────────────────────────────────────────────────────────

        private static string StripRevision(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            int hash = url.IndexOf('#');
            if (hash >= 0) url = url.Substring(0, hash);
            int q = url.IndexOf('?');
            if (q >= 0) url = url.Substring(0, q);
            return url;
        }

        /// <summary>Remote 'origin' du dépôt contenant le dossier (remonte jusqu'à 4 niveaux).</summary>
        private static string ReadRemoteFromGitConfig(string folder)
        {
            try
            {
                string dir = folder;
                for (int i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
                {
                    string cfg = Path.Combine(dir, ".git", "config");
                    if (File.Exists(cfg))
                    {
                        var m = Regex.Match(File.ReadAllText(cfg), @"url\s*=\s*(\S+)", RegexOptions.IgnoreCase);
                        return m.Success ? m.Groups[1].Value : null;
                    }
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { /* dossier illisible : traité comme 'pas de dépôt' */ }
            return null;
        }

        private static (int code, string stdout, string stderr) RunGit(string args, string workDir)
        {
            var psi = new ProcessStartInfo("git", args)
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"; // jamais de prompt bloquant

            using (var p = Process.Start(psi))
            {
                // DEADLOCK CLASSIQUE ÉVITÉ : lire stdout puis stderr séquentiellement bloque si git
                // remplit le tampon stderr (~4 Ko) avant de fermer stdout — chacun attend l'autre, et
                // le timeout placé après les lectures ne se déclenchait jamais. stderr est donc lu
                // en parallèle, et le timeout borne réellement l'attente.
                var errTask = p.StandardError.ReadToEndAsync();
                var outTask = p.StandardOutput.ReadToEndAsync();

                if (!p.WaitForExit(20000))
                {
                    try { p.Kill(); } catch { }
                    return (-1, SafeResult(outTask), "timeout git (20 s) : " + SafeResult(errTask));
                }
                // WaitForExit(int) ne garantit pas la fin des flux asynchrones : on les attend explicitement.
                Task.WaitAll(new Task[] { outTask, errTask }, 5000);
                return (p.ExitCode, SafeResult(outTask), SafeResult(errTask));
            }
        }

        private static string SafeResult(Task<string> t)
            => t.IsCompleted && !t.IsFaulted ? t.Result : "";

        private static string Truncate(string s, int n)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s.Trim() : s.Substring(0, n).Trim() + "…");
    }
}
