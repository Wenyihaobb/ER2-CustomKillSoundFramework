using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CustomKillSoundFramework
{
    /// <summary>
    /// Holds the decoded clips (one dynamic list per feedback kind) and one 2D
    /// AudioSource. Unity objects created during BepInEx Load/Awake are destroyed
    /// when the game switches into a battle scene, so EnsureReady() validates
    /// both the source and the clips on every play and rebuilds what went stale.
    ///
    /// Sound files are discovered purely by file name; see SoundFileName.cs for
    /// the grammar. There is no fixed list and no upper limit on file count.
    /// </summary>
    internal static class SoundBank
    {
        private static readonly System.Random Rng = new System.Random();
        private static GameObject _host;
        private static AudioSource _source;
        private static int _warnLogs;
        private static long _lastLoadMs;

        private static readonly List<AudioClip>[] Pools =
        {
            new List<AudioClip>(), // SoundKind.Hit
            new List<AudioClip>(), // SoundKind.Kill
            new List<AudioClip>()  // SoundKind.Headshot
        };

        private static bool _loaded;
        /// <summary>null = never scanned; "" = scanned but nothing usable.</summary>
        private static string _signature;

        /// <summary>Warnings stay enabled, but an endless failure must not flood the log.</summary>
        private static void Warn(string message)
        {
            if (_warnLogs++ > 10)
                return;
            CksfPlugin.Logger?.LogWarning("[CKSF] " + message);
        }

        internal static void Initialize()
        {
            // When the master switch is off (manually, or because the startup
            // check found no sound files), do not even create Unity objects.
            // The startup check owns the single user-facing warning.
            if (!CksfPlugin.MasterOn)
            {
                if (Diag.Enabled)
                    Diag.Log("Preload skipped: master switch is OFF");
                return;
            }
            EnsureReady();
        }

        /// <summary>Re-scan the Sounds folder on the next play (called on battle load).</summary>
        internal static void Invalidate()
        {
            _loaded = false;
        }

        internal static void EnsureReady()
        {
            if (_source == null)
            {
                try
                {
                    _host = new GameObject("CustomKillSoundFramework_Audio");
                    UnityEngine.Object.DontDestroyOnLoad(_host);
                    var src = _host.AddComponent<AudioSource>();
                    if (src == null)
                        throw new InvalidOperationException("AddComponent<AudioSource> returned null");
                    src.playOnAwake = false;
                    src.loop = false;
                    src.volume = 1f;
                    src.spatialBlend = 0f;
                    _source = src;
                    if (Diag.Enabled)
                        Diag.Log("2D AudioSource created (spatialBlend=0)");
                }
                catch (Exception e)
                {
                    Warn("AudioSource creation failed: " + e.Message);
                }
            }

            if (_source == null)
                return;

            // Guard against Unity dropping clips on a scene change, and against
            // a folder that has not been scanned yet. This is a cheap reference
            // check: no directory I/O on the normal hot path.
            if (_loaded && ClipsAlive())
                return;

            // At most one full decode per 250 ms, even if a file is missing.
            long now = Environment.TickCount64;
            if (now - _lastLoadMs > 250)
            {
                _lastLoadMs = now;
                LoadClips();
            }
        }

        private static bool ClipsAlive()
        {
            for (int k = 0; k < Pools.Length; k++)
            {
                var pool = Pools[k];
                for (int i = 0; i < pool.Count; i++)
                    if (pool[i] == null)
                        return false;
            }
            return true;
        }

        private static void LoadClips()
        {
            string dir = CksfPlugin.SoundsDirectory;
            if (!Directory.Exists(dir))
            {
                for (int k = 0; k < Pools.Length; k++)
                    Pools[k].Clear();
                _loaded = true;
                _signature = string.Empty;
                // Startup detection already warned once; keep this debug-only
                // so the log never fills with repeated "no sounds" lines.
                if (Diag.Enabled)
                    Diag.Log("Sounds folder not found: " + dir);
                return;
            }

            var entries = SoundFileName.Scan(dir);
            string signature = SoundFileName.Signature(entries);

            // Same file list and all clips still alive -> reuse the decoded
            // clips. This makes a battle reload free; a new/removed/edited
            // file changes the signature and triggers a real decode.
            if (!string.IsNullOrEmpty(_signature) && signature == _signature && ClipsAlive())
            {
                _loaded = true;
                if (Diag.Enabled)
                    Diag.Log("Sounds unchanged, reusing decoded clips (" + entries.Count + " files)");
                return;
            }

            for (int k = 0; k < Pools.Length; k++)
                Pools[k].Clear();

            int hit = 0, kill = 0, headshot = 0;
            int skipped = 0;
            foreach (var e in entries)
            {
                var clip = Decode(e.FullPath, e.FileName);
                if (clip == null)
                {
                    skipped++;
                    continue;
                }
                Protect(clip);
                Pools[(int)e.Kind].Add(clip);
                if (e.Kind == SoundKind.Hit) hit++;
                else if (e.Kind == SoundKind.Kill) kill++;
                else headshot++;

                if (Diag.Enabled)
                    Diag.Log("sound " + KindName(e.Kind) + " #" + e.DisplayIndex +
                             " <- " + e.FileName);
            }

            _signature = signature;
            _loaded = true;

            if (Diag.Enabled)
                Diag.Log("Sounds loaded: hit=" + hit + " kill=" + kill +
                         " headshot=" + headshot + " skipped=" + skipped +
                         " | folder=" + dir);

            if (entries.Count == 0 && Diag.Enabled)
                Diag.Log("No sound files matched the naming rules in " + dir +
                         ". Use headshot / kill / hit + optional number, .wav only (no 'sound' suffix).");
        }

        private static string KindName(SoundKind kind)
        {
            if (kind == SoundKind.Headshot) return "headshot";
            if (kind == SoundKind.Kill) return "kill";
            return "hit";
        }

        private static AudioClip Decode(string path, string fileName)
        {
            try
            {
                int trimmed, rate;
                var clip = WavLoader.Load(path, out trimmed, out rate);
                if (Diag.Enabled && rate > 0)
                    Diag.Log("trim " + fileName + ": removed " +
                             (1000.0 * trimmed / rate).ToString("0.0") +
                             " ms of leading silence");
                return clip;
            }
            catch (Exception e)
            {
                Warn("Could not decode " + fileName + ": " + e.Message);
                return null;
            }
        }

        private static void Protect(UnityEngine.Object obj)
        {
            if (obj == null)
                return;
            try { UnityEngine.Object.DontDestroyOnLoad(obj); }
            catch { /* EnsureReady will re-decode if it disappears */ }
        }

        private static AudioClip Pick(SoundKind kind)
        {
            var pool = Pools[(int)kind];
            if (pool.Count == 0)
                return null;
            return pool[Rng.Next(pool.Count)];
        }

        private static void Play(SoundKind kind, float volume)
        {
            // Single choke point for ALL playback paths.
            if (!CksfPlugin.MasterOn)
            {
                if (Diag.Enabled)
                    Diag.Log("PLAY skipped: master switch is OFF");
                return;
            }

            EnsureReady();
            AudioClip clip = Pick(kind);
            string tag = KindName(kind);

            if (_source == null || clip == null || volume <= 0f)
            {
                if (Diag.Enabled)
                    Diag.Log("PLAY skipped: " + tag + " source=" + (_source != null) +
                             " clip=" + (clip != null) + " vol=" + volume.ToString("0.00") +
                             " (a kind with no files simply stays silent; other kinds are unaffected)");
                return;
            }

            if (Diag.Enabled)
                Diag.Log("PLAY " + tag + " vol=" + volume.ToString("0.00") + " (clip ready)");

            try { _source.PlayOneShot(clip, volume); }
            catch (Exception e) { Warn("PlayOneShot failed: " + e.Message); }
        }

        internal static void PlayHit()
        {
            var cfg = CksfPlugin.Settings;
            if (cfg == null || !cfg.EnableHit.Value) return;
            Play(SoundKind.Hit, ModConfig.Volume(cfg.HitVolume));
        }

        internal static void PlayHeadshot()
        {
            var cfg = CksfPlugin.Settings;
            if (cfg == null || !cfg.EnableHeadshot.Value) return;
            Play(SoundKind.Headshot, ModConfig.Volume(cfg.HeadshotVolume));
        }

        internal static void PlayKill()
        {
            var cfg = CksfPlugin.Settings;
            if (cfg == null || !cfg.EnableKill.Value) return;
            Play(SoundKind.Kill, ModConfig.Volume(cfg.KillVolume));
        }
    }
}
