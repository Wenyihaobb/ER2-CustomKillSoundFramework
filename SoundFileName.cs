using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CustomKillSoundFramework
{
    internal enum SoundKind
    {
        Hit,
        Kill,
        Headshot
    }

    /// <summary>One recognised sound file in the Sounds folder.</summary>
    internal sealed class SoundEntry
    {
        internal string FullPath;
        internal string FileName;
        internal SoundKind Kind;
        internal bool HasNumber;
        internal long Number;
        /// <summary>1-based slot shown in the debug log; never used to drop a file.</summary>
        internal long DisplayIndex;
    }

    /// <summary>
    /// Decides which .wav files in the Sounds folder are valid, and what kind
    /// of feedback each one belongs to.
    ///
    /// Grammar (case-insensitive, extension must be .wav):
    ///
    ///     (headshot | kill | hit) [ digits ]
    ///
    /// The type word must be spelled EXACTLY headshot, kill or hit, and nothing
    /// except an optional decimal number may follow it. In particular a
    /// "sound" suffix is NOT allowed. Anything else (hitsound.wav,
    /// killsound3.wav, headshotsound2.wav, game_hitsound1.wav, hitmarker.wav,
    /// hits.wav, my_kill.wav, hit_1.wav, hit 2.wav, an .mp3, ...) is ignored
    /// and never played.
    ///
    /// Examples that ARE accepted:
    ///     hit.wav, HIT.WAV, hit1.wav, hit2.wav, hit12.wav,
    ///     kill.wav, kill3.wav, headshot.wav, HEADSHOT2.WAV
    ///
    /// There is no limit on how many files each kind may have, and a kind may
    /// be absent entirely. Numbering is only a label: every accepted file is
    /// loaded and joins the random pool of its kind. If an unnumbered file and
    /// numbered files coexist, the unnumbered one is treated as slot 1 and the
    /// numbered ones shift up by one for display purposes only
    /// (hit / hit1 / hit2 -> #1 / #2 / #3). No file is dropped or overwritten
    /// because of a number collision.
    /// </summary>
    internal static class SoundFileName
    {
        /// <summary>Parses one file name. Returns false for anything not matching the grammar.</summary>
        internal static bool TryParse(string fileName, out SoundKind kind,
                                      out bool hasNumber, out long number)
        {
            kind = SoundKind.Hit;
            hasNumber = false;
            number = 0;

            if (string.IsNullOrEmpty(fileName))
                return false;
            if (!string.Equals(Path.GetExtension(fileName), ".wav",
                               StringComparison.OrdinalIgnoreCase))
                return false;

            string stem = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(stem))
                return false;
            stem = stem.ToLowerInvariant();

            string rest;
            if (stem.StartsWith("headshot", StringComparison.Ordinal))
            {
                kind = SoundKind.Headshot;
                rest = stem.Substring("headshot".Length);
            }
            else if (stem.StartsWith("kill", StringComparison.Ordinal))
            {
                kind = SoundKind.Kill;
                rest = stem.Substring("kill".Length);
            }
            else if (stem.StartsWith("hit", StringComparison.Ordinal))
            {
                kind = SoundKind.Hit;
                rest = stem.Substring("hit".Length);
            }
            else
            {
                return false; // not one of the three exact type words
            }

            // Strict: only digits may follow the type word. No "sound" suffix,
            // no separators, no extra words.
            if (rest.Length == 0)
                return true; // unnumbered: hit.wav / kill.wav / headshot.wav

            for (int i = 0; i < rest.Length; i++)
                if (rest[i] < '0' || rest[i] > '9')
                    return false;

            if (!long.TryParse(rest, NumberStyles.None,
                               CultureInfo.InvariantCulture, out number))
                number = long.MaxValue; // absurdly long number: keep the file anyway

            hasNumber = true;
            return true;
        }

        /// <summary>
        /// Scans a folder and returns every accepted file, grouped by kind.
        /// Files that do not match the grammar are skipped silently (they are
        /// never played). Kinds with no files simply produce an empty group.
        /// The returned lists are sorted by their display slot so the debug
        /// output is stable.
        /// </summary>
        internal static List<SoundEntry> Scan(string directory)
        {
            var result = new List<SoundEntry>();
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception)
            {
                return result;
            }

            // Parse first, then assign display slots per kind.
            var byKind = new List<SoundEntry>[3];
            for (int i = 0; i < byKind.Length; i++)
                byKind[i] = new List<SoundEntry>();

            foreach (string path in files)
            {
                string name = Path.GetFileName(path);
                SoundKind kind;
                bool hasNumber;
                long number;
                if (!TryParse(name, out kind, out hasNumber, out number))
                    continue; // strict: unknown names are never played

                var entry = new SoundEntry
                {
                    FullPath = path,
                    FileName = name,
                    Kind = kind,
                    HasNumber = hasNumber,
                    Number = number
                };
                byKind[(int)kind].Add(entry);
            }

            foreach (var group in byKind)
            {
                if (group.Count == 0)
                    continue;

                bool hasUnnumbered = false;
                foreach (var e in group)
                    if (!e.HasNumber)
                        hasUnnumbered = true;

                foreach (var e in group)
                {
                    if (!e.HasNumber)
                        e.DisplayIndex = 1;
                    else
                        e.DisplayIndex = hasUnnumbered
                            ? (e.Number == long.MaxValue ? long.MaxValue : e.Number + 1)
                            : e.Number;
                }

                group.Sort(Compare);
                result.AddRange(group);
            }

            return result;
        }

        private static int Compare(SoundEntry a, SoundEntry b)
        {
            int c = a.Kind.CompareTo(b.Kind);
            if (c != 0) return c;
            c = a.DisplayIndex.CompareTo(b.DisplayIndex);
            if (c != 0) return c;
            c = a.HasNumber.CompareTo(b.HasNumber); // unnumbered first on a tie
            if (c != 0) return c;
            return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A change signature so adding/removing a file can be detected
        /// without decoding all WAV data again on every battle load.</summary>
        internal static string Signature(List<SoundEntry> entries)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in entries)
            {
                sb.Append(e.FileName).Append('|');
                try
                {
                    var fi = new FileInfo(e.FullPath);
                    sb.Append(fi.Length).Append(':').Append(fi.LastWriteTimeUtc.Ticks);
                }
                catch (Exception)
                {
                    sb.Append("?");
                }
                sb.Append(';');
            }
            return sb.ToString();
        }
    }
}
