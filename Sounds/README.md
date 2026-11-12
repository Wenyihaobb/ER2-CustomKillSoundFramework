# Sound files go here — but NOT in this repository

This folder is intentionally **empty of audio**. It exists in the repository
only so the expected location is documented. **No sound files are distributed
here**, and none are covered by the MIT licence.

> **Important:** the files you actually use must be placed in the **game's**
> plugin folder, not in this repository:
>
> ```
> <GameDir>/BepInEx/plugins/Custom Kill Sound Framework/Sounds/
> ```

You must supply your own lawfully obtained sounds. See `NOTICE.md` for the
attribution, the project's history, and why third-party game audio must not be
committed, attached to issues, or bundled in a release or fork.

## Naming rules

File name without extension = `(headshot | kill | hit)` + optional number,
extension `.wav`, case-insensitive:

| Accepted | Ignored |
| --- | --- |
| `hit.wav`, `hit1.wav`, `hit2.wav`, `HIT12.WAV` | `hitsound.wav`, `hitsound1.wav` |
| `kill.wav`, `kill3.wav` | `killsound3.wav`, `my_kill.wav` |
| `headshot.wav`, `HEADSHOT2.WAV` | `headshotsound2.wav`, `hitmarker.wav`, `hit_1.wav` |

- The type word must be **exactly** `headshot`, `kill` or `hit`.
- After it, **only optional digits** — no `sound` suffix, no separators, no
  extra words.
- There is **no limit** on how many files a type may have; one is picked at
  random. A single file needs no number. A type can also be missing entirely.
- If an unnumbered file coexists with numbered ones, it acts as #1 and the
  numbered ones shift up (`hit` / `hit1` / `hit2` → #1 / #2 / #3). All files
  join the random pool; none is dropped.
- Non-matching files are skipped and never played.

## Format

Uncompressed **RIFF/WAVE PCM**: 8-, 16-, 24- or 32-bit integer, or 32-bit
float; mono or stereo. Compressed formats (MP3, M4A/AAC, OGG, ADPCM WAV) are
not supported and must be converted first.

Leading silence in a file is trimmed automatically at load time, so a WAV that
starts with 100–200 ms of silence still gives immediate feedback. Your file on
disk is never modified.

The folder is re-scanned on each battle load; reload or switch battles after
adding or replacing files. If no valid file is found at game start, the plugin
sets `MasterEnable = false` in the cfg and writes one English warning to
`BepInEx/LogOutput.log`; fix the files and re-enable the key yourself.

See the project root `README.md` for full setup and configuration details.
