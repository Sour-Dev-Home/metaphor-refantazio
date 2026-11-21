# Deploy a dev build

To try a change in the game, run `scripts/dev/session.ps1 deploy`. It builds the mod, puts the build in your Reloaded-II
mods folder, enables it for the game, launches the game, waits until the mod reports it loaded, then closes the game.
`-KeepGame` leaves it running to play.

You need the game registered as an application in Reloaded-II, and the game build in the
[known-builds list](add-a-game-build.md). The script refuses to run on any other build.

1. Close the game and Reloaded-II. The script won't start while either is running: the game holds the old build's
   files, and a running launcher can save over the enabled mods.
2. From the repository root, in PowerShell 7:

   ```powershell
   pwsh scripts/dev/session.ps1 deploy -KeepGame
   ```

   It finds the game and Reloaded-II with the same locator the installer uses: the application registered as
   `metaphor.exe`, then Steam's libraries, and `LauncherPath` in `ReloadedII.json`. The `METAPHOR_GAME_EXE` and
   `METAPHOR_RELOADED_EXE` environment variables override that, and `-GameExe` and `-LauncherExe` override those.
   `pwsh scripts/dev/session.ps1 status` shows what it found, without changing anything.

Every verb of `session.ps1` ends with one line, `ok: ...` or `failed: ... - what to do`. Exit code 0 is ok, 1 means it
failed after launching the game (the mod refused, timed out or the game exited), and 2 means it stopped before
launching anything. The full list of verbs and options: `Get-Help scripts/dev/session.ps1 -Full`.

What it changes in your Reloaded-II setup, and nothing else:

- the mod's folder in the mods folder named in `ReloadedII.json`, named by its mod ID. A previous deploy there is
  replaced. A folder of that name holding a different mod is left alone, and the script stops.
- the game's `AppConfig.json`, where the mod is added to `EnabledMods`. Mods you already enabled stay enabled. The
  load order (`SortedMods`) isn't touched, and the mod loads without it
  ([first run](../../raw-sources/mod-entry-point-first-run-2026-11-15.md)). The previous file is kept beside it as
  `AppConfig.json.deploy-backup`.

Everything else is put back when the session ends, also after a failure or Ctrl+C: `ReloadedII.json` as it was, the
game closed unless `-KeepGame`, and any opt-in file the session added removed. A session killed outright leaves
`ReloadedII.json.session-backup`, which the next launching verb restores first.

By default it builds the mod (`src/Metaphor.Hooks`, mod ID `metaphor.accessibility`). `-Project` builds another mod
project, whose build output must hold a `ModConfig.json`, and `-Configuration Release` a Release build.

A working deploy prints `ready on <game build>, hooks n/m`. The mod also writes a line naming its commit and the game
build to Reloaded-II's log for that run, the newest file in `%APPDATA%\Reloaded-Mod-Loader-II\Logs\`.

## Run

`run` deploys the Debug build the same way, with any opt-ins, lets the game run, and reports what the mod's development
log saw, without printing any game text:

```powershell
pwsh scripts/dev/session.ps1 run -Seconds 300 -OptIn ui-text-panes,dialogue-window
```

1. It deploys the Debug build and creates `opt-in-<name>.txt` in the mod's folder for each `-OptIn` name, before the one
   launch. It accepts only the names the mod reads ([listed below](#files-a-run-can-leave-in-the-mods-folder));
   `status` lists them, and any other name is refused with that list.
2. It prints `hooks n/m` from `mod-status.json` and the mod's `skipped`, `failed` and ready lines from Reloaded-II's log
   of that run, so a listener that didn't go in shows at once.
3. It watches the game for `-Seconds` (default 120) and prints `the game is still running after <n> s` or
   `the game exited after <n> s`. Then it closes the game, unless `-KeepGame`. Closing kills it (its window only asks
   "Exit game?"), so a save in progress is lost: use a test save.
4. It counts the records in the mod's `message-log.txt` by type, and the requests by the function they came through:

   ```text
   message log: 12 compile (840 source lines), 310 lookup, 5 register, 44 request
   requests by function: MsgRequestById 4, MsgRequestByName 40
   ```

5. It removes the opt-in files it added.

With `-SaveLog <folder>`, quitting the game ends the run instead of failing it, and `message-log.txt` and
`transcript.txt` are copied into a new timestamped folder there. Give `-Seconds` longer than you mean to play. It
refuses a folder inside a git repository, since the logs hold game text.

The log itself holds game text, so it stays in the mod's folder and is never printed or committed. Local paths in the
output are shortened (`<mods>`, `<game>`, `~` and so on), so it can go into an issue or a raw source as it is. It exits
0 when the game survived (or you quit it, with `-SaveLog`) and the mod wrote its log, 1 when the game exited, the mod
wasn't ready or there's no log, and 2 when it stopped before launching.

### Summarise a log

`tools/Metaphor.SessionLog` summarises `message-log.txt` without printing game text:

```bash
dotnet run --project tools/Metaphor.SessionLog -c Release -v q -- <message-log.txt> [--from <ms>] [--to <ms>] [--offset <ms>]
```

It prints the record count by kind, each screen class with its first and last time, its `screen` records and its cursor
inputs, and, with `--from` or `--to`, the counts inside that window. `--offset` is the gap in ms between the mod's
start and the recording's start; times then also show as video times (log time minus the offset). Records with
`text=`, `label=` or quoted strings are counted, never printed, so the output can go into an issue.

### Files a run can leave in the mod's folder

These files in the deployed mod's folder (`<mods>\metaphor.accessibility`) change what a Debug run does. The folder is
outside the repository, so none is ever committed. `run -OptIn <name>` adds the `opt-in-<name>.txt` ones for one session.

- `opt-in-create-msg-from-string.txt`: turns on the listener on `CreateMsgFromString` (#106), the first hook inside the
  exe's `.text1` section. It's off by default; only the file's presence counts, not its contents. Without it, Reloaded-II's
  log says `hook CreateMsgFromStringListener skipped: opt-in, and opt-in-create-msg-from-string.txt isn't in the mod's
  folder`, and the hook isn't counted in `hooks n/m`. With it, the hook still goes through the build check and signature
  scan, and counts as expected like any other. Use it for one short, separate run.
- `opt-in-ui-text-panes.txt`: the same, for the listener on the UI text pane's draw (#93), which runs every frame and
  logs drawn text (`docs/raw-sources/ui-focus-candidates-2026-11-16.md`).
- `opt-in-tutorial-window.txt`: turns on the log of the tutorial pop-up's update (#118), which runs every frame while a
  pop-up exists. The hook itself is always in, since it speaks the pop-up; the file adds a `tutorial window` record
  each time the window or its object changes, with what each points to
  ([finding](../../findings/tutorial-popup-window-static.md)).
- `opt-in-dialogue-window.txt`: turns on the two dialogue window hooks, which only log (#232)
  ([turn on the dialogue window hooks](turn-on-the-dialogue-window-hooks.md)).
- `opt-in-inspector.txt`: opens the live inspector's pipe in a Debug build
  ([inspect the running game](inspect-the-running-game.md)).
- `spike-player-name.txt`: the hero name you typed in the game, on its first line (trimmed, UTF-8). The listener compares
  each string with it and logs only `match=exact`, `contains` or `no`, never the name. Without the file it falls back to
  the `METAPHOR_SPIKE_PLAYER_NAME` environment variable, which Reloaded-II may not pass to the game.

A deploy replaces the mod's folder, removing any of these files left there.

The spoken load message names each opt-in whose file it found, as `message strings`, `text panes` or `dialogue window`,
for example "Metaphor accessibility loaded, dev opt-ins: text panes, dialogue window". An opt-in whose hooks didn't all
go in is said with `not active`; the log says why (#223).

## The scripts' own tests

The scripts' own tests run against the [stand-in game](../../../GLOSSARY.md#working-and-testing), with no game needed:

```powershell
Invoke-Pester tests/pester
```
