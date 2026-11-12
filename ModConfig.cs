using System;
using BepInEx.Configuration;

namespace CustomKillSoundFramework
{
    /// <summary>
    /// Bilingual, deliberately minimal descriptions. BepInEx rewrites the cfg
    /// file from these ConfigDescriptions, so long explanations here would grow
    /// the file back after every rewrite. Naming rules and setup instructions
    /// belong on the mod page and in the README, not in the cfg.
    /// </summary>
    internal sealed class ModConfig
    {
        internal const string MasterSection = "0.Master";
        internal const string MasterKey = "MasterEnable";

        internal readonly ConfigEntry<bool> MasterEnable;
        internal readonly ConfigEntry<bool> EnableHeadshot;
        internal readonly ConfigEntry<bool> EnableHit;
        internal readonly ConfigEntry<bool> EnableKill;

        internal readonly ConfigEntry<float> HeadshotVolume;
        internal readonly ConfigEntry<float> HitVolume;
        internal readonly ConfigEntry<float> KillVolume;

        internal readonly ConfigEntry<bool> EnableDebugLog;

        internal ModConfig(ConfigFile cfg)
        {
            MasterEnable = cfg.Bind(MasterSection, MasterKey, true,
                new ConfigDescription(
                    "总开关：关闭后不播放任何音效；无音效文件时会自动设为 false，检查文件后请手动改回 true。\n" +
                    "Master switch: OFF mutes every sound. Auto-set to false when no sound file is found; " +
                    "set it back to true yourself after fixing the files."));

            EnableHeadshot = cfg.Bind("1.Switches", "EnableHeadshot", true,
                new ConfigDescription(
                    "爆头音效开关（打头且击杀时播放）。\n" +
                    "Headshot sound (played on a head hit that kills)."));

            EnableHit = cfg.Bind("1.Switches", "EnableHit", true,
                new ConfigDescription(
                    "命中音效开关（命中敌人但未击杀时播放）。\n" +
                    "Hit sound (played when wounding an enemy without a kill)."));

            EnableKill = cfg.Bind("1.Switches", "EnableKill", true,
                new ConfigDescription(
                    "击杀音效开关（击杀敌方士兵时播放）。\n" +
                    "Kill sound (played when killing an enemy soldier)."));

            HeadshotVolume = BindVolume(cfg, "HeadshotVolume",
                "爆头音效音量", "Headshot volume");
            HitVolume = BindVolume(cfg, "HitVolume",
                "命中音效音量", "Hit volume");
            KillVolume = BindVolume(cfg, "KillVolume",
                "击杀音效音量", "Kill volume");

            EnableDebugLog = cfg.Bind("3.Debug", "EnableDebugLog", false,
                new ConfigDescription(
                    "调试日志开关：把判定过程写入 BepInEx\\LogOutput.log。\n" +
                    "Debug log: writes sound decisions to BepInEx\\LogOutput.log."));
        }

        private static ConfigEntry<float> BindVolume(ConfigFile cfg, string key, string zh, string en)
        {
            return cfg.Bind("2.Volume", key, 1.00f,
                new ConfigDescription(
                    zh + "，范围 0.00 - 1.00，两位小数。\n" +
                    en + ", 0.00 - 1.00, two decimal places.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        /// <summary>Clamp to 0.00-1.00 and quantise to two decimal places.</summary>
        internal static float Volume(ConfigEntry<float> entry)
        {
            float v = entry.Value;
            if (float.IsNaN(v) || float.IsInfinity(v))
                v = 1f;
            if (v < 0f) v = 0f;
            if (v > 1f) v = 1f;
            return (float)Math.Round(v * 100.0, MidpointRounding.AwayFromZero) / 100f;
        }
    }
}
