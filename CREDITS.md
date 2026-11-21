# Credits

This mod stands on other people's work. Each entry names the project, its licence and what we took, with the evidence in
`docs/raw-sources/`. Copied code keeps its licence notice (AGENTS.md, ground rule 7).

## Code and libraries we ship or build on

| Project | Licence | What we use |
|---|---|---|
| [Reloaded-II](https://github.com/Reloaded-Project/Reloaded-II) | GPL-3.0 | The mod loader and the mod interfaces (`Reloaded.Mod.Interfaces`) the mod is built against |
| [Reloaded.Hooks](https://github.com/Reloaded-Project/Reloaded.Hooks) (`Reloaded.SharedLib.Hooks` 1.9.0, `Reloaded.Hooks.Definitions` 1.11.0) | LGPL-3.0 | The shared hooks library our hooks go through (ADR 0006) |
| [Prism](https://github.com/ethindp/prism) v0.18.3 | MPL-2.0 | Speech and braille output, fetched by SHA-256 at package time with its licences (ADR 0005) |
| [FastLZ](https://github.com/ariya/FastLZ) at `b1342dab` | MIT | Its decompressor (`fastlz.c`), ported to C# in `src/Metaphor.Core/Navigation/FastLz.cs` with its notice, to read the field `.NAV` tiles (#179). The licence ships in `licenses/fastlz/` ([note](docs/raw-sources/fastlz-and-recast-layer-cells-2026-11-21.md)) |

Build and test tools, not shipped: CsCheck, Mono.Cecil, Microsoft.CodeAnalysis.CSharp, xUnit and Microsoft.NET.Test.Sdk
(versions pinned in `Directory.Packages.props`); [Stryker.NET](https://github.com/stryker-mutator/stryker-net) and
[jscpd](https://github.com/kucherenko/jscpd) in the weekly slow checks. Their licences are on their package pages
[NEEDS VERIFICATION: not saved under `docs/raw-sources/`].
[FsCheck](https://github.com/fscheck/FsCheck) 3.6.0 (BSD-3-Clause, with its dependency `FSharp.Core` under MIT), not
shipped, runs the property tests of the in-game parsers (ADR 0015,
[licence](docs/raw-sources/fscheck-licence-2026-11-19.md)).
Check tools, pinned and not shipped ([licences](docs/raw-sources/check-tool-licences-2026-11-19.md)):
[OSV-Scanner](https://github.com/google/osv-scanner) 2.7.1 (Apache-2.0), CI's vulnerability audit
(`scripts/ci/vuln-audit.sh`); [lychee](https://github.com/lycheeverse/lychee) 0.24.2 (Apache-2.0, with an MIT licence
file too), [Vale](https://github.com/errata-ai/vale) 3.25.1 (MIT) and
[markdownlint-cli2](https://github.com/DavidAnson/markdownlint-cli2) 0.24.1 (MIT), the docs checks
(`scripts/setup/install-docs-tools.sh`, `tools/docs/`).
[Ghidra](https://github.com/NationalSecurityAgency/ghidra) 12.2 (Apache-2.0), installed locally and not shipped, runs
the local exe project (`scripts/dev/ghidra.ps1`, `tools/ghidra/`) and its RTTI analyzer labels the vtables we name; the RTTI
layout `MetaphorQuery.java` reads comes from its `Rtti4Model.java` and `TypeDescriptorModel.java`
(`ghidra-and-jdk-install-2026-11-17.md`).

## Projects whose code or findings we used

No code is copied from these; we took signatures, byte patterns and techniques.

| Project | Author | Licence | What we took |
|---|---|---|---|
| [MF.Toolkit](https://github.com/RyoTune/MF.Toolkit) at `b1f876da` | RyoTune | AGPL-3.0 | Message function signatures and argument shapes (`src/Metaphor.Hooks/DialogueHooks.cs`, `MessageListeningHooks.cs`, `NameLookupListeners.cs`; [note](docs/raw-sources/mf-toolkit-message-signatures-2026-11-15.md)); the Squirrel registration technique (`SquirrelRegistrationListener.cs`, development only; [note](docs/raw-sources/mf-toolkit-squirrel-registration-2026-11-16.md)) |
| [Ryo Framework](https://github.com/RyoTune/Ryo) at `1d661e15` | RyoTune | "General Mod License" (its `LICENSE.txt`) | CRI Atom function names, byte patterns and arguments (`src/Metaphor.Hooks/CriAudioListeners.cs`; [note](docs/raw-sources/ryo-cri-atom-patterns-2026-11-16.md)). Only names and patterns, no code |
| [CriFs.V2.Hook.ReloadedII](https://github.com/Sewer56/CriFs.V2.Hook.ReloadedII) at `82c79eef` | Sewer56 | LGPL-3.0 | CRI FileSystem release byte patterns, used as signature probes (`tests/Metaphor.Core.Tests/SignatureProbesTests.cs`, never built into the mod; [note](docs/raw-sources/crifs-v2-hook-metaphor-patterns-2026-11-15.md)) |
| [CriFsV2Lib](https://github.com/Sewer56/CriFsV2Lib) at `169b001c` | Sewer56 | none stated (no licence file) | The CPK container and `@UTF` table layout (`CriTableMetadata.cs`, `CriColumnFlags.cs`, `TocFinder.cs`, `TocReader.cs`, `TableContainerReader.cs`), which `src/Metaphor.Core/CpkArchive.cs` reads in its own code (#118). No code copied ([note](docs/raw-sources/cri-cpk-table-layout-2026-11-21.md)) |
| [recast-navigation-rs](https://github.com/rirurin/recast-navigation-rs) at `8a9f8065` | Rirurin | GPL-3.0 | The Atlus navmesh file header (version 3, agent settings, bounds) that reads the game's `.NAV` files (#179; [note](docs/raw-sources/recast-tile-cache-set-and-atl-header-2026-11-21.md)). Only the layout, no code |
| [recastnavigation](https://github.com/recastnavigation/recastnavigation) at `9f4ce644` | Recast Navigation contributors | Zlib | The navmesh, tile-cache set and tile layer layouts (#179; [notes](docs/raw-sources/recast-tile-cache-set-and-atl-header-2026-11-21.md)). Only the layouts, no code |

## Projects whose ideas we borrowed

| Project | Author | Licence | What we took, and where |
|---|---|---|---|
| [Persona-4-Golden-Access](https://github.com/AquaRose7/Persona-4-Golden-Access) at `dcff255` | AquaRose7 | GPL-3.0 | The short history of spoken dialogue lines (#126; `src/Metaphor.Core/Messages/DialogueHistory.cs`) and the output-then-speak fallback (#115; `src/Metaphor.Core/Announcements/OutputRoute.cs`); an installer that also installs Reloaded-II and turns the mod on without Reloaded-II's window (#303; `scripts/player/install.ps1`, `scripts/player/MetaphorInstall.psm1`). No code copied; its installer's source isn't published ([note](docs/raw-sources/persona-4-golden-access-2026-11-16.md)) |
| [Arcana](https://github.com/ChipsAhoiMcCoy/arcana) tag `v0.1.0-alpha.3` (`c7b8a908`) | ChipsAhoiMcCoy | Zlib | Design ideas for dialogue and menu accessibility, studied for #57, #58, #59, #83 and #115 (repeat key, announce a focus change once, duplicate suppression, voiced-line handling). No code copied; no code comment cites it yet [NEEDS VERIFICATION: which ideas shipped] ([note](docs/raw-sources/arcana-v0.1.0-alpha.3-2026-11-16.md)) |
| [BMMValidator](https://gitlab.com/BMMProject/Tools/BMMValidator) at `d69404ee` | BMMProject | Apache-2.0 | The idea of a checker players run on their install that says what's wrong (#283; `scripts/player/check-install.ps1`, `scripts/player/MetaphorInstall.psm1`). No code read or copied ([note](docs/raw-sources/install-check-sources-2026-11-19.md)) |
| [AccessMods](https://github.com/AccessMods) mods and [UnityAccessibilityLib](https://github.com/AccessMods/UnityAccessibilityLib) at `958e20c` | AccessMods organisation | MIT | Practice from their accessibility mods, mainly a blind player's report that names and highlighted words must be spoken (#83). No code copied; the code-level use is [NEEDS VERIFICATION] ([note](docs/raw-sources/accessmods-org-survey-2026-11-16.md)) |
| [FF6ScreenReader](https://github.com/BlindGuyNW/FF6ScreenReader) at `40a3427` | BlindGuyNW | None (all rights reserved) | Sound cues as generated tones on one waveOut handle per channel, with stereo panning (#637; `src/Metaphor.Core/Cues/CueMixer.cs`, `src/Metaphor.Hooks/WaveOutDevice.cs`), the approach of its `FFVI_Mod/Utils/SoundPlayer.cs`, `AudioChannel.cs`, `ToneGenerator.cs` and `FFVI_Mod/Core/AudioLoopManager.cs`. Unlicensed, so no code read or copied ([note](docs/raw-sources/ff6-screen-reader-audio-approach-2026-11-21.md)) |

Arcana itself builds on Grim Dark (Austin Hicks) and Dark Vision (Bradley Renshaw), both Zlib.
