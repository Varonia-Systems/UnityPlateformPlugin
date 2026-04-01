using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Sons de build — tout est appelé depuis le thread principal Unity.
    /// PlaySound(SND_ASYNC) retourne en < 1 ms et Windows gère la suite.
    /// Ne pas utiliser Task.Run : quand le thread background se termine,
    /// SND_ASYNC coupe le son immédiatement.
    /// </summary>
    public static class VaroniaBuildSounds
    {
        static byte[] _wavSuccess;
        static byte[] _wavFailure;
        static byte[] _wavStep;

        const string k_SoundsDir   = "Editor/Resources/Varonia/Sounds";
        const string k_BaseSuccess = "build_success";
        const string k_BaseFailure = "build_failure";
        const string k_BaseStep    = "build_step";

        static readonly string[] k_Exts = { ".wav", ".mp3" };

        static byte[] WavSuccess
        {
            get
            {
                if (_wavSuccess == null)
                    _wavSuccess = BuildWav(new[] { 523f, 659f, 784f }, new[] { 0.14f, 0.14f, 0.38f });
                return _wavSuccess;
            }
        }
        static byte[] WavFailure
        {
            get
            {
                if (_wavFailure == null)
                    _wavFailure = BuildWav(new[] { 440f, 349f, 262f }, new[] { 0.18f, 0.18f, 0.48f });
                return _wavFailure;
            }
        }
        // Son intermédiaire : court bip ascendant A4→E5 (étape complétée)
        static byte[] WavStep
        {
            get
            {
                if (_wavStep == null)
                    _wavStep = BuildWav(new[] { 440f, 659f }, new[] { 0.06f, 0.14f });
                return _wavStep;
            }
        }

        // ─── winmm.dll ────────────────────────────────────────────────────────────

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        static extern int mciSendString(string cmd, System.Text.StringBuilder ret, int retLen, IntPtr hwnd);

        const uint SND_ASYNC     = 0x0001;  // retourne immédiatement, Windows gère la suite
        const uint SND_FILENAME  = 0x20000;
        const uint SND_NODEFAULT = 0x0002;

        // ─── API publique — appelée sur le thread principal Unity ─────────────────

        public static void Play(bool success)
        {
            string path = FindFilePath(success ? k_BaseSuccess : k_BaseFailure);
            // Si le fichier externe existe mais que la lecture échoue → WAV de fallback
            if (path != null && PlayFileNative(path, alias: "vbo_build")) return;
            PlayWavFallback(success ? WavSuccess : WavFailure);
        }

        /// <summary>
        /// Son intermédiaire court — étape complétée (ZIP done, COPY done…)
        /// Peut être remplacé par un fichier build_step.wav/.mp3 dans le package.
        /// </summary>
        public static void PlayStep()
        {
            string path = FindFilePath(k_BaseStep);
            if (path != null && PlayFileNative(path, alias: "vbo_step")) return;
            PlayWavFallback(WavStep);
        }

        // ─── Lecture fichier (WAV ou MP3) ─────────────────────────────────────────

        // Retourne true si la lecture a pu être lancée, false si échec (→ fallback WAV)
        static bool PlayFileNative(string path, string alias = "vbo_build")
        {
            try
            {
                if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    // WAV : PlaySound SND_ASYNC — retourne instantanément
                    return PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
                }
                else
                {
                    return PlayMp3Mci(path, alias);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VaroniaBuildSounds] {e.Message}");
                return false;
            }
        }

        // Tente d'ouvrir un MP3 via MCI en essayant plusieurs types de devices.
        // Retourne true dès qu'un open réussit, false si tous échouent.
        static bool PlayMp3Mci(string path, string alias)
        {
            // Fermer une éventuelle session précédente sur cet alias
            mciSendString($"close {alias}", null, 0, IntPtr.Zero);

            // Essayer dans l'ordre : auto-detect → mpegvideo → wmpavideo
            // "mpegvideo" = codec MPEG intégré Windows
            // "wmpavideo" = moteur Windows Media Player (supporte tous les MP3)
            string[] deviceTypes = { "", "mpegvideo", "wmpavideo" };

            foreach (string dtype in deviceTypes)
            {
                string typeClause = dtype.Length > 0 ? $" type {dtype}" : "";
                int err = mciSendString(
                    $"open \"{path}\"{typeClause} alias {alias}",
                    null, 0, IntPtr.Zero);

                if (err == 0)
                {
                    mciSendString($"play {alias}", null, 0, IntPtr.Zero);
                    return true;
                }
            }

            // Tous les types MCI ont échoué → le caller utilisera le fallback WAV synthétisé
            return false;
        }

        // ─── Fallback WAV synthétisé ──────────────────────────────────────────────

        static void PlayWavFallback(byte[] wav)
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "vbo_build_sound.wav");
                File.WriteAllBytes(tmp, wav);
                PlaySound(tmp, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VaroniaBuildSounds] Fallback WAV : {e.Message}");
            }
        }

        // ─── Recherche du fichier (chemin absolu disque) ──────────────────────────

        static string FindFilePath(string baseName)
        {
            // Méthode 1 : PackageInfo
            try
            {
                var info = PackageInfo.FindForAssembly(typeof(VaroniaBuildSounds).Assembly);
                if (info != null)
                {
                    string dir = Path.Combine(info.resolvedPath, k_SoundsDir);
                    foreach (string ext in k_Exts)
                    {
                        string p = Path.Combine(dir, baseName + ext);
                        if (File.Exists(p)) return Path.GetFullPath(p); // normalise \ et /
                    }
                }
            }
            catch { }

            // Méthode 2 : scan du dossier Packages/ à la racine du projet
            try
            {
                string root   = Path.GetDirectoryName(Application.dataPath);
                string pkgDir = Path.Combine(root, "Packages");
                if (Directory.Exists(pkgDir))
                {
                    foreach (string pkg in Directory.GetDirectories(pkgDir))
                    {
                        string dir = Path.Combine(pkg, k_SoundsDir);
                        foreach (string ext in k_Exts)
                        {
                            string p = Path.Combine(dir, baseName + ext);
                            if (File.Exists(p)) return Path.GetFullPath(p); // normalise \ et /
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        // ─── Génération WAV synthétisé (PCM 16-bit mono 44100 Hz) ────────────────

        static byte[] BuildWav(float[] freqs, float[] durations, float sampleRate = 44100f)
        {
            if (freqs.Length != durations.Length)
                throw new ArgumentException("freqs et durations doivent avoir la même longueur.");

            int totalSamples = 0;
            for (int i = 0; i < durations.Length; i++)
                totalSamples += (int)(sampleRate * durations[i]);

            var samples = new short[totalSamples];
            int pos     = 0;
            int fadeLen = Math.Max(1, (int)(sampleRate * 0.010f));

            for (int i = 0; i < freqs.Length; i++)
            {
                int   count = (int)(sampleRate * durations[i]);
                float freq  = freqs[i];

                for (int j = 0; j < count && pos < samples.Length; j++, pos++)
                {
                    float env = 1f;
                    if (j < fadeLen)              env = (float)j / fadeLen;
                    else if (j > count - fadeLen) env = (float)(count - j) / fadeLen;

                    double phase = 2.0 * Math.PI * freq * j / sampleRate;
                    samples[pos] = (short)(Math.Sin(phase) * 28000.0 * env);
                }
            }

            int dataBytes = totalSamples * 2;
            using (var ms = new MemoryStream(44 + dataBytes))
            {
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write((byte)'R'); bw.Write((byte)'I'); bw.Write((byte)'F'); bw.Write((byte)'F');
                    bw.Write(36 + dataBytes);
                    bw.Write((byte)'W'); bw.Write((byte)'A'); bw.Write((byte)'V'); bw.Write((byte)'E');
                    bw.Write((byte)'f'); bw.Write((byte)'m'); bw.Write((byte)'t'); bw.Write((byte)' ');
                    bw.Write(16);
                    bw.Write((short)1);
                    bw.Write((short)1);
                    bw.Write((int)sampleRate);
                    bw.Write((int)(sampleRate * 2));
                    bw.Write((short)2);
                    bw.Write((short)16);
                    bw.Write((byte)'d'); bw.Write((byte)'a'); bw.Write((byte)'t'); bw.Write((byte)'a');
                    bw.Write(dataBytes);
                    foreach (short s in samples)
                        bw.Write(s);

                    return ms.ToArray();
                }
            }
        }
    }
}
