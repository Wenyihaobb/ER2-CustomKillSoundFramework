# Custom Kill Sound Framework

A plugin for **Easy Red 2** that plays *your own* hit, kill and headshot feedback sounds when the local player shoots an enemy soldier.

It is a **framework**: it ships no audio at all. You supply the WAV files, and
the plugin discovers them by file name.

> [!WARNING]
> **Please read before using or publishing this mod.**
>
> - **AI-generated code.** The entire plugin was written by an AI coding
>   assistant. It has not had a professional code/security review. See
>   [NOTICE.md](NOTICE.md).
> - **Not tested in multiplayer.** Development and testing happened only in
>   single-player / offline battles. Multiplayer, co-op, PvP and dedicated
>   servers are **untested**, and a client-side mod may not be permitted
>   online. Use at your own risk.
> - **No sound files are included.** This repository contains no audio and no
>   download links. Supply your own lawfully obtained sounds. Do not upload
>   third-party game audio here or into a Release. See [NOTICE.md](NOTICE.md).

---

## Features

- **Bring your own sounds.** Any number of files per type, discovered at
  runtime from one folder, picked at random. No fixed file list, no rebuild.
- **Strict, simple naming:** `(headshot | kill | hit)` + optional number +
  `.wav`, case-insensitive. Anything else is ignored and never played.
- **One sound per attack.** A grenade or shell hitting ten soldiers produces a
  single sound, not ten overlapping ones.
- **Kill always wins over hit.** If anyone in that volley dies, you hear the
  kill sound. Priority: `headshot > kill > hit`.
- **Only your own hits count.** Enemy→player, ally→enemy and enemy→ally combat
  never triggers anything. Head-hit signals are matched to the exact soldier
  you hit, by unit pointer.
- **Infantry only.** Vehicles, aircraft and emplaced weapons deliberately keep
  the game's original audio.
- **No perceived latency:** leading silence in a WAV is trimmed automatically
  when the clip is decoded. Your files on disk are never modified.
- **Master switch with startup check:** if no valid sound file is found when the
  game loads, the plugin turns itself off, writes one English warning to
  `LogOutput.log`, and leaves it to you to fix the files and re-enable it.
- **Optional debug log** that explains exactly why a sound did or did not play.
- Bilingual (Chinese / English) configuration with deliberately minimal
  comments; the full documentation lives here and on the mod page.

## Requirements

| | |
| --- | --- |
| Game | Easy Red 2 (Windows, Steam version tested) |
| Loader | BepInEx 6 for Unity **IL2CPP**, installed and working |
| Build only | .NET 6 SDK, plus BepInEx's generated `interop` assemblies |
| Runtime | Your own uncompressed PCM `.wav` files |

You do **not** need the .NET SDK if you only install a prebuilt DLL.

## Installation

1. Install BepInEx 6 (IL2CPP build) into the Easy Red 2 folder and start the
   game once so it generates `BepInEx/`.
2. Create these folders (note the spaces in the plugin folder name):

   ```
   <GameDir>/BepInEx/plugins/Custom Kill Sound Framework/
   <GameDir>/BepInEx/plugins/Custom Kill Sound Framework/Sounds/
   ```

3. Put `CustomKillSoundFramework.dll` (from a Release, or built yourself) into
   the plugin folder.
4. Put your `.wav` files into `Sounds/` using the naming rules below.
5. Launch the game. On first start the plugin writes
   `<GameDir>/BepInEx/config/Custom_Kill_Sound_Framework.cfg`.

If `Sounds/` is empty or every file name is invalid at game start, the plugin
sets `MasterEnable = false` in the cfg and logs **one** English warning. Check
the folder, then set the key back to `true` yourself and reload the battle or
restart the game.

## Sound file naming

The file name without extension must be:

```
(headshot | kill | hit) [number] .wav
```

| Rule | Detail |
| --- | --- |
| Type word | Must be **exactly** `headshot`, `kill` or `hit`; case-insensitive. |
| After the type word | Only optional decimal digits. **No `sound` suffix**, no underscores, no spaces, no extra words. |
| Extension | `.wav` only, case-insensitive, uncompressed PCM. |
| File count | Unlimited per type. A type may also be missing entirely. |
| Single file | No number needed (`hit.wav`). |
| Several files | Add numbers (`hit1.wav`, `hit2.wav`, ...); one is picked at random. |
| Mixed naming | If an unnumbered file coexists with numbered ones, it acts as #1 and the numbered ones shift up for display: `hit` / `hit1` / `hit2` → #1 / #2 / #3. **No file is dropped** — all join the random pool. |

Accepted: `hit.wav`, `HIT12.WAV`, `hit1.wav`, `kill.wav`, `kill3.wav`,
`headshot.wav`, `HEADSHOT2.WAV`.

Ignored: `hitsound.wav`, `hitsound1.wav`, `killsound3.wav`,
`headshotsound2.wav`, `hitmarker.wav`, `hits.wav`, `my_kill.wav`,
`hit_1.wav`, `hit 2.wav`, `clip.mp3`, `readme.txt`.

Having sounds for only one or two types is fine — the missing type simply stays
silent and does not affect the others.

**Format:** uncompressed RIFF/WAVE PCM, 8/16/24/32-bit integer or 32-bit float,
mono or stereo. Compressed formats (MP3, M4A/AAC, OGG, ADPCM WAV) are **not**
supported — convert them first. The reader skips unknown chunks, so ordinary
PCM WAV files from any editor work.

The folder is re-scanned on each battle load, so adding or replacing files
takes effect after reloading or switching battles. See
[Sounds/README.md](Sounds/README.md) for a short in-repo reminder.

## Configuration

`<GameDir>/BepInEx/config/Custom_Kill_Sound_Framework.cfg`

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| `0.Master` | `MasterEnable` | `true` | Global switch. When `false`, nothing plays even if the three switches below are on. Auto-set to `false` at game start when no valid sound file is found. |
| `1.Switches` | `EnableHeadshot` | `true` | Headshot sound, played on a head hit that kills (overrides kill and hit). |
| | `EnableHit` | `true` | Hit sound, played when you wound an enemy without a kill. |
| | `EnableKill` | `true` | Kill sound, played when you kill an enemy soldier, including vehicle crew. |
| `2.Volume` | `HeadshotVolume` | `1.00` | `0.00`–`1.00`, two decimal places. |
| | `HitVolume` | `1.00` | `0.00`–`1.00`, two decimal places. |
| | `KillVolume` | `1.00` | `0.00`–`1.00`, two decimal places. |
| `3.Debug` | `EnableDebugLog` | `false` | Write decision logs to `BepInEx/LogOutput.log`. |

The cfg is read **once when a battle loads** (and once at plugin load). Editing
it mid-match takes effect after reloading or switching battles. Only warnings
and errors are logged by default; the debug switch adds informational lines.
The shipped cfg intentionally contains only short per-key comments — setup and
naming instructions belong here and on the mod page.

## How it works (short version)

A small set of game methods is patched with Harmony:

- `DamageFeedback.OnSoldierHit` is the **only** event allowed to start a
  feedback decision, and only when the shooter is the local player and the
  target is an enemy soldier.
- Head hits (`Soldier.DamageWithAnimation`, `BodyPart.HitPart`,
  `ItemHelmet.HitPart`, `GameStatistics.OnHeadshotHelmet`) and kill evidence
  (`Soldier.Kill` / `KillSynched`, `GameStatistics.OnPlayerGetKill`) can only
  **modify** an open decision, matched by unit pointer.
- Events within ~50 ms are merged into one "attack batch". Inputs are
  OR-accumulated, so one dead soldier among many still counts as a kill. The
  batch resolves once after ~16 ms (40 ms for a multi-target volley).
- Sound is played through a single 2D `AudioSource` with `PlayOneShot`. WAVs
  are decoded directly into `AudioClip` objects — no AssetBundle, coroutine or
  web request. `WavTrim` removes leading silence from the decoded buffer.
- `BattleManager.Start` resets per-battle state and re-reads the cfg;
  `PlayerController.Update` drives the per-frame decision tick.

This design is why AI-vs-AI combat stays silent and why a volley plays once.

## Debug logging

Set `EnableDebugLog = true` to write `[CKSF]` lines to
`BepInEx/LogOutput.log`, for example:

```
[CKSF] trim kill.wav: removed 201.1 ms of leading silence
[CKSF] HIT #3 ptr=2304339574784 targetDead=False anyKill=True (local player -> enemy soldier)
[CKSF] DEATH evidence ptr=2301681899520 matchedBatch=True
[CKSF] LATENCY hit->decision 16.4 ms
[CKSF] DECISION -> KILL (batchHits=10, anyKill=True, anyHead=False)
[CKSF] PLAY kill vol=1.00 (clip ready)
```

The log explains why a sound was ignored, which head-hit source fired, why a
volley was merged, and how much latency remained. `IGNORED` lines are the most
useful when a sound did not play. Repeated signals are capped and output is
limited to 3000 lines per battle. Warnings/errors are always written regardless
of this switch — including the single "no valid sound files" warning.

Notes: this game's BepInEx disk log level is `Fatal, Error, Warning, Message`
(Info is excluded), so the plugin uses `LogMessage` for debug lines and
`LogWarning` for that startup warning. BepInEx may overwrite `LogOutput.log` at
each game start and buffers writes — exit the game to flush if the file looks
empty. Turn the switch off again after troubleshooting.

## Building from source

The project needs paths to your own game installation and to BepInEx's
generated interop assemblies. These are machine-specific and are **never**
committed.

1. Install the .NET 6 SDK and BepInEx 6 (IL2CPP) into the game. Start the game
   once so the interop assemblies are generated.
2. Copy `Directory.Build.props.example` to `Directory.Build.props` (gitignored)
   and edit the two paths:

   ```xml
   <GameDir>C:\...\steamapps\common\Easy Red 2</GameDir>
   <InteropDir>C:\...\folder\containing\Assembly-CSharp.dll</InteropDir>
   ```

   `InteropDir` is whichever folder holds `Assembly-CSharp.dll`,
   `Il2Cppmscorlib.dll` and the `UnityEngine.*` interop assemblies — commonly
   `<GameDir>/BepInEx/interop`, but any location works. Alternatively pass them
   on the command line with `-p:GameDir="..." -p:InteropDir="..."`.
3. Build and install:

   ```bat
   build.bat
   ```

   or

   ```bat
   dotnet build CustomKillSoundFramework.csproj -c Release -p:InstallToGame=true
   ```

`build.bat` picks its SDK in this order: the `ER2_DOTNET` environment variable
→ an optional private `%USERPROFILE%\.dotnet6sdk\dotnet.exe` → `dotnet` on
PATH. Set `ER2_DOTNET` to a specific `dotnet.exe` if you keep several SDKs.

`-p:InstallToGame=true` copies the DLL and the cfg template into the game and
creates an empty `Sounds/` folder. **It never copies any audio** — you provide
that yourself. Without the flag, the DLL is only built to
`bin/Release/net6.0/`. No game binaries or interop DLLs are part of this
repository.

## Known limitations

- Tested on Windows / Steam / single-player only. Multiplayer untested (see
  the warning above).
- Only enemy **soldiers** produce feedback. Vehicle, aircraft and emplaced
  weapon hit/destroy sounds are intentionally left untouched.
- A game update can change method signatures and break the Harmony patches.
  Check for a new release after major game updates.
- Config changes take effect on the next battle load, not instantly.
- There is still roughly one frame (~16 ms, ~40 ms for a multi-target volley)
  of decision latency, deliberately kept so kill/headshot evidence can arrive
  before the batch resolves.
- The code is AI-generated and unverified by a human Unity specialist; see
  [NOTICE.md](NOTICE.md).

## Contributing

Issues and pull requests are welcome. Please do **not** commit sound files,
game DLLs, interop assemblies, build output or your local
`Directory.Build.props`. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Legal / credits

- Code: MIT — see [LICENSE](LICENSE). The license covers the original source
  code, build scripts and documentation only; [NOTICE.md](NOTICE.md) states its
  exact scope.
- **No audio is distributed.** You must supply your own lawfully obtained
  sounds. This framework has no affiliation with, and does not require or
  recommend audio from, any particular game. See [NOTICE.md](NOTICE.md) for
  the project's history and third-party rights.
- Easy Red 2 by Marco Amadei / Corvostudio di Amadei Marco — unaffiliated fan
  mod.
- BepInEx, Harmony, Il2CppInterop and Unity remain under their own licenses.

**This project is unofficial, non-commercial, AI-generated, and untested in
multiplayer. Provided as-is, without warranty of any kind.**
