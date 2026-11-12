using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace CustomKillSoundFramework
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Easy Red 2.exe")]
    public sealed class CksfPlugin : BasePlugin
    {
        internal const string PluginGuid = "com.wenyi.er2.customkillsoundframework";
        internal const string PluginName = "Custom Kill Sound Framework";
        internal const string PluginVersion = "1.0.0";
        internal const string ConfigFileName = "Custom_Kill_Sound_Framework.cfg";

        internal static CksfPlugin Instance { get; private set; }
        internal static ManualLogSource Logger { get; private set; }
        internal static ModConfig Settings { get; private set; }

        /// <summary>
        /// Effective master switch. Mirrors [0.Master] MasterEnable, but can also
        /// be forced off for this session by the startup empty-folder check.
        /// Every playback path goes through this one gate.
        /// </summary>
        internal static volatile bool MasterOn;

        /// <summary>
        /// The single, fixed sound folder:
        ///   &lt;GameRoot&gt;\BepInEx\plugins\Custom Kill Sound Framework\Sounds
        /// Falls back to a Sounds folder next to the DLL only if that fixed
        /// folder does not exist (e.g. the DLL was dropped into plugins root).
        /// </summary>
        internal static string SoundsDirectory
        {
            get
            {
                string fixedDir = Path.Combine(Paths.PluginPath,
                                               "Custom Kill Sound Framework", "Sounds");
                if (Directory.Exists(fixedDir))
                    return fixedDir;

                string dllDir = Path.GetDirectoryName(typeof(CksfPlugin).Assembly.Location);
                if (!string.IsNullOrEmpty(dllDir))
                {
                    string besideDll = Path.Combine(dllDir, "Sounds");
                    if (Directory.Exists(besideDll))
                        return besideDll;
                }
                return fixedDir; // let SoundBank report the missing folder once
            }
        }

        private ConfigFile _configFile;

        public override void Load()
        {
            Instance = this;
            Logger = base.Log;

            try
            {
                string cfgPath = Path.Combine(Paths.ConfigPath, ConfigFileName);
                WriteConfigTemplateIfMissing(cfgPath);

                // saveOnInit=false: the shipped bilingual template stays exactly as written.
                _configFile = new ConfigFile(cfgPath, false);
                // Only our own code writes cfg values. Prevent BepInEx from
                // rewriting (and stripping the bilingual header of) the file
                // when the startup check switches the master key off.
                _configFile.SaveOnConfigSet = false;
                Settings = new ModConfig(_configFile);
                Diag.Enabled = Settings.EnableDebugLog.Value;
                MasterOn = Settings.MasterEnable.Value;

                // Must run before AddComponent: the component's Awake preloads
                // sounds and needs to see the effective master state.
                CheckSoundsAtStartup(cfgPath);

                // Registers the IL2CPP type and attaches it to the persistent BepInEx manager.
                if (AddComponent<SoundBehaviour>() == null)
                {
                    Logger.LogError("[CKSF] Failed to attach SoundBehaviour; sound hooks stay inactive.");
                    return;
                }

                new Harmony(PluginGuid).PatchAll(typeof(CksfPlugin).Assembly);
                if (Diag.Enabled)
                {
                    Diag.Log("=== Custom Kill Sound Framework " + PluginVersion + " | debug log ON ===");
                    Diag.Log("Sounds folder: " + SoundsDirectory + " | Config: " + cfgPath);
                    Diag.LogSettings(Settings);
                }
            }
            catch (Exception e)
            {
                Logger.LogError("[CKSF] Load failed: " + e);
            }
        }

        private static bool _startupChecked;
        private static bool _emptyFolderWarningLogged;

        /// <summary>
        /// Auto-detection at game load. If the master switch is ON but the Sounds
        /// folder contains no file that matches the naming rules, turn the master
        /// switch OFF (in memory AND in the cfg file) and emit exactly ONE English
        /// warning. The player must fix the files and re-enable the key manually.
        ///
        /// This warning is deliberately NOT routed through Diag: it has to reach
        /// LogOutput.log even with debug logging off, so it uses LogWarning
        /// (Warning is part of this game's disk log levels).
        /// </summary>
        private static void CheckSoundsAtStartup(string cfgPath)
        {
            if (_startupChecked)
                return;
            _startupChecked = true;

            try
            {
                if (Settings == null)
                    return;

                string dir = SoundsDirectory;
                int count = SoundFileName.Scan(dir).Count;
                if (count > 0)
                {
                    if (Diag.Enabled)
                        Diag.Log("Startup check: " + count +
                                 " valid sound file(s) found; master switch=" + MasterOn);
                    return;
                }

                // No usable sound files: force the session off. Persist only if
                // the key is still true, so a deliberate "off" stays untouched,
                // but the player still gets one warning per game launch.
                MasterOn = false;
                if (Settings.MasterEnable.Value)
                {
                    try
                    {
                        Settings.MasterEnable.Value = false;
                        WriteMasterSwitchToCfg(cfgPath, false);
                    }
                    catch (Exception e)
                    {
                        if (Diag.Enabled)
                            Diag.Log("Could not persist master switch: " + e.Message);
                    }
                }

                if (_emptyFolderWarningLogged)
                    return;
                _emptyFolderWarningLogged = true;
                Logger?.LogWarning(
                    "[CKSF] WARNING: No valid sound files found in \"" + dir + "\". " +
                    "The master switch (" + ModConfig.MasterKey + ") is automatically OFF " +
                    "(written as false in the config file), so no sounds will play. " +
                    "Check that your .wav files are in that folder and named exactly headshot / kill / hit " +
                    "with an optional number and NO 'sound' suffix (e.g. hit1.wav, kill.wav, headshot.wav). " +
                    "After fixing the files, set " + ModConfig.MasterKey + " = true in the config yourself " +
                    "and reload the battle or restart the game.");
            }
            catch (Exception e)
            {
                // Detection must never break plugin loading. Keep the single
                // warning above as the only user-facing line.
                if (Diag.Enabled)
                    Diag.Log("Startup sound check failed: " + e.Message);
            }
        }

        /// <summary>
        /// Edits ONLY the master key line, preserving every bilingual comment in
        /// the cfg. If an older cfg has no such key yet, append a small section.
        /// ConfigFile.Save() is intentionally not used: it would rewrite the
        /// whole file and drop the header comments.
        /// </summary>
        private static void WriteMasterSwitchToCfg(string path, bool value)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                var lines = new List<string>(File.ReadAllLines(path, Encoding.UTF8));
                string text = value ? "true" : "false";
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i];
                    if (line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string key = line.Substring(0, eq).Trim();
                    if (string.Equals(key, ModConfig.MasterKey, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = ModConfig.MasterKey + " = " + text;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    lines.Add("");
                    lines.Add("[" + ModConfig.MasterSection + "]");
                    lines.Add("## 总开关 / Master switch (auto-disabled: no valid sound files found at game start)");
                    lines.Add(ModConfig.MasterKey + " = " + text);
                }

                File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                if (Diag.Enabled)
                    Diag.Log("Could not persist master switch: " + e.Message);
            }
        }

        /// <summary>Read the cfg once per battle load, including the master and debug switches.</summary>
        internal void ReloadConfigFile()
        {
            try
            {
                _configFile?.Reload();
                MasterOn = Settings != null && Settings.MasterEnable.Value;
                // When the switch is off, stay completely silent (as promised in cfg).
                Diag.Enabled = Settings != null && Settings.EnableDebugLog.Value;
                if (Diag.Enabled)
                {
                    Diag.Log("Battle loaded: cfg reloaded, debug log ON, master=" + MasterOn);
                    Diag.LogSettings(Settings);
                }
            }
            catch (Exception e) { Logger.LogWarning("[CKSF] Config reload failed: " + e.Message); }
        }

        private static void WriteConfigTemplateIfMissing(string path)
        {
            if (File.Exists(path))
                return;

            try
            {
                using (Stream stream = typeof(CksfPlugin).Assembly
                    .GetManifestResourceStream("CustomKillSoundFramework.Default.cfg"))
                {
                    if (stream == null)
                        return;

                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        File.WriteAllText(path, reader.ReadToEnd(), new UTF8Encoding(false));
                }
            }
            catch (Exception e)
            {
                Logger?.LogWarning("[CKSF] Could not write cfg template: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Player-facing debug log, controlled at runtime by cfg key
    /// [3.Debug] EnableDebugLog (default: false).
    ///
    /// IMPORTANT: this game's BepInEx disk log level is
    /// "Fatal, Error, Warning, Message" -- Info/Debug are NOT written to
    /// LogOutput.log. So debug lines MUST use LogMessage.
    ///
    /// When the switch is OFF, every call site is guarded by "if (Diag.Enabled)"
    /// so no string is built and no log call happens at all. Warnings and errors
    /// are emitted directly through Logger and never go through this class.
    /// </summary>
    internal static class Diag
    {
        /// <summary>Mirrors the cfg switch; read once at plugin load and at each battle load.</summary>
        internal static volatile bool Enabled;

        // A long battle can generate thousands of hits; cap output so the log
        // file does not grow without bound while the player is troubleshooting.
        internal const int MaxLinesPerBattle = 3000;
        private static int _lines;
        private static bool _limitReported;
        private static readonly Dictionary<string, int> Signals = new Dictionary<string, int>();

        internal static void Reset()
        {
            _lines = 0;
            _limitReported = false;
            Signals.Clear();
        }

        /// <summary>Always guard with "if (Diag.Enabled)" at the call site.</summary>
        internal static void Log(string message)
        {
            if (!Enabled)
                return;
            if (_lines >= MaxLinesPerBattle)
            {
                if (!_limitReported)
                {
                    _limitReported = true;
                    CksfPlugin.Logger?.LogMessage(
                        "[CKSF] Debug log line limit (" + MaxLinesPerBattle +
                        ") reached for this battle; further lines suppressed.");
                }
                return;
            }
            _lines++;
            CksfPlugin.Logger?.LogMessage("[CKSF] " + message);
        }

        /// <summary>Like Log, but each distinct signal name is printed at most twice per battle.</summary>
        internal static void Signal(string name)
        {
            if (!Enabled)
                return;
            int n;
            Signals.TryGetValue(name, out n);
            if (n >= 2 || Signals.Count > 96)
                return;
            Signals[name] = n + 1;
            Log("signal " + name);
        }

        internal static void LogSettings(ModConfig cfg)
        {
            if (!Enabled || cfg == null)
                return;
            Log("Settings: master=" + CksfPlugin.MasterOn +
                " (cfg=" + cfg.MasterEnable.Value + ")" +
                ", headshot=" + cfg.EnableHeadshot.Value +
                ", hit=" + cfg.EnableHit.Value +
                ", kill=" + cfg.EnableKill.Value +
                " | vol head=" + ModConfig.Volume(cfg.HeadshotVolume).ToString("0.00") +
                ", hit=" + ModConfig.Volume(cfg.HitVolume).ToString("0.00") +
                ", kill=" + ModConfig.Volume(cfg.KillVolume).ToString("0.00"));
        }
    }
}
