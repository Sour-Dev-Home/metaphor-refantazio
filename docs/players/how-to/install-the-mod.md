# Install the mod

> **Draft for testers on dev builds.** There is no release yet. This page is finished with the first alpha
> ([#131](https://github.com/Sour-Dev-Home/metaphor-accessibility/issues/131)). Steps marked `[NEEDS VERIFICATION]` have
> not been checked by ear with a screen reader yet. If one is wrong, say so in an issue.

This guide installs Reloaded-II, the program that loads the mod into the game, adds the game and the mod to it, and
makes a desktop shortcut that starts the game with the mod. It is written for NVDA. Each step says what you should
hear.

You need the game installed on PC (single-player only) and NVDA running.

## If Windows says the file is from the internet

Windows marks a downloaded file as from the internet, and the mod's scripts aren't code-signed, so it may warn or ask
before running them. Check the download first ([Check a download](check-a-download.md)); only then follow these.
Every step is by keyboard. `[NEEDS VERIFICATION]` All the spoken phrases below, by ear with NVDA and Narrator
([#573](https://github.com/Sour-Dev-Home/metaphor-accessibility/issues/573)); Microsoft doesn't document them
([notes](../../raw-sources/smartscreen-and-unblock-microsoft-docs-2026-11-20.md)).

### Unblock the zip before you unzip it

Unzipping in File Explorer copies the mark to every file inside, so clearing it on the zip first saves a warning later.

1. In File Explorer, move to the zip with the arrow keys. Press `Alt+Enter`. A Properties window opens and the screen
   reader reads its title, the zip's name followed by "Properties".
2. On the General tab, press `Tab` until you reach a check box named "Unblock", near the bottom, in the Security
   group. The screen reader reads "Unblock", "check box", "not checked". If there is no such check box, the file isn't
   marked: close the window with `Escape` and go on.
3. Press `Space`. It reads "checked".
4. Press `Tab` to "OK" and press `Enter`. The window closes.
5. Unzip: press the `Applications` key (or `Shift+F10`), choose "Extract All...", then press `Enter` on "Extract".

If you unzipped first, repeat steps 1 to 4 on `Install Metaphor accessibility.cmd` in the extracted folder.

### Get past "Windows protected your PC"

SmartScreen shows this blue window when a downloaded program has no reputation yet. It is not a verdict that the file
is harmful. `Install Metaphor accessibility.cmd` usually gets the older "Open File - Security Warning" window
instead (step 3 of the installer section below).

1. The window opens and the screen reader reads "Windows protected your PC", then its text, which names the file.
   Press `Tab`: focus lands on the link "More info". Press `Enter`.
2. A button "Run anyway" appears. Press `Tab` until the screen reader reads "Run anyway", "button", then press `Enter`.
   The file runs.
3. If the window offers only "Don't run", or you are on a work or school PC, an administrator has blocked the file;
   you can't go on from here. With Smart App Control on, Windows blocks the file with no such button: unblock it as
   above (`[NEEDS VERIFICATION]` whether that lifts the block), or start the installer with the command at the end of
   the next section.

## Install with the spoken installer

The zip holds two things: the mod's folder, `metaphor.accessibility`, and one file beside it,
`Install Metaphor accessibility.cmd`, which starts the installer in that folder. The installer does every step on this
page for you and says each one as it goes. It needs no clicks in Reloaded-II's window. `[NEEDS VERIFICATION]` All of
this section, by ear.

1. Extract the whole zip (unblock it first if Windows marked it, above). File Explorer opens the extracted folder.
   Opening the file inside the zip without extracting doesn't work: it says to extract the zip first.
2. Press `End` to reach the last item, "Install Metaphor accessibility", the one file there (File Explorer lists the
   mod's folder before it), and press Enter. `[NEEDS VERIFICATION]` What NVDA reads for it, and whether File Explorer
   opens the extracted folder after "Extract All".
3. Windows probably asks first, because the file came from the internet and isn't signed: an "Open File - Security
   Warning" window asking whether to run it. Choose `Run`. `[NEEDS VERIFICATION]` Its wording, its buttons and which one
   has the focus, by ear. With Smart App Control on, Windows blocks the file instead; then use the command at the end of
   this section. Why Windows asks, and what was checked:
   [notes](../../raw-sources/install-launcher-motw-2026-11-20.md).
4. A window opens and the installer starts. Answer its questions with `y` and Enter, or Enter alone where it says Enter means yes. It asks before it starts and
   before each download.
5. It finds the game, installs Reloaded-II 1.31.0 if you don't have it (in your user folder, no admin rights needed),
   installs the .NET 9 Desktop Runtime and the Visual C++ Redistributable if they're missing, copies the mod in, turns
   it on for the game, and puts a shortcut called "Metaphor ReFantazio with accessibility" on your desktop. The two
   runtimes need admin rights: when it installs one, Windows asks for permission, and you answer yes there too.
6. It ends by running the install check (below) and says what it found. Before you start the game with the mod for the
   first time, the check says the mod hasn't reported a start yet; that is expected.
7. The window stays open and its last line says "The installer has finished. Read what it said above, then press any
   key to close this window." Read back with NVDA's review cursor if you need to, then press a key.
8. Start the game from the desktop shortcut.

`install.cmd` in the mod's folder does the same as `Install Metaphor accessibility.cmd`. If Windows won't open either,
start the installer by typing instead: press `Windows+R`, type this with the mod's folder's full path in place of
`<folder>`, and press Enter. `[NEEDS VERIFICATION]` Whether this works with Smart App
Control on.

`powershell -ExecutionPolicy Bypass -NoExit -File "<folder>\install.ps1"`

Each step says whether it worked. A step that fails says what to do and stops there. Run it again after fixing that,
or to update the mod: it changes only what needs changing and says so. With NVDA, JAWS or ZoomText running, it speaks
each line and question through your screen reader, with the speech library the mod ships, so you don't need to read
the window. Windows' own voice speaks the steps when no screen reader is running. That voice is an English one when
Windows has one; otherwise the installer says so and uses Windows' default voice. Open it with Enter, not "Run as
administrator": it refuses to run as administrator. With User Account Control turned off every window is
administrator, so it says so and asks before going on. `[NEEDS VERIFICATION]` By ear with NVDA (#601).

The installer checks every file it downloads against a recorded hash. A download that gets no data for a minute, or
takes more than half an hour, stops, and the installer says what to do. If you ever open Reloaded-II's own window, it may
offer to update itself; that update comes from Reloaded-II's newest release, which nobody has checked with this mod.

The sections below do the same by hand.

## Install Reloaded-II

The mod is checked against Reloaded-II **1.31.0**
([run notes](../../raw-sources/mod-entry-point-first-run-2026-11-15.md)). Use that version. A newer one may work, but
nobody has checked.

1. Open the Reloaded-II releases page on GitHub: `https://github.com/Reloaded-Project/Reloaded-II/releases`. NVDA
   reads a page title naming the project.
2. Press `H` to move between headings until NVDA reads `1.31.0`. Newer releases come first, so go on past them.
3. Press `K` or `Tab` through the links under that release's "Assets" heading and open the Windows setup file.
   `[NEEDS VERIFICATION]` The exact asset name for 1.31.0, and whether the release offers a setup program or only a
   zip.
4. Run the setup. `[NEEDS VERIFICATION]` What the setup's dialogs say and which buttons NVDA finds. Accept the default
   folder unless you have a reason not to.

## Add the game to Reloaded-II

`[NEEDS VERIFICATION]` All of this section. Reloaded-II's window is reachable with NVDA mainly through the review
cursor and a simulated mouse click
([P4G Access notes](../../raw-sources/persona-4-golden-access-2026-11-16.md)).

1. Start Reloaded-II. NVDA should read its window title.
2. Find the button that adds an application, using NVDA's review cursor: `NVDA+numpad 7` moves to the top of the
   window, and `NVDA+numpad 9` or `NVDA+numpad 3` moves by line. `Shift+NVDA+arrows` is the laptop layout.
3. Choose the game's program file, `metaphor.exe`, in the game's install folder. Reloaded-II names the new entry after
   the program (its ID is `metaphor.exe`).

## Add the mod from a dev build

`[NEEDS VERIFICATION]` Where testers get a dev build, and what it is called. The project's CI builds a package of the
mod on every merge and keeps it as a workflow artifact with a SHA-256 hash
([Checks](../../contributors/reference/checks.md)). The package holds our mod only; Reloaded-II is installed separately.

1. Download the zip from a CI run on `main` and check it, as [Check a download](check-a-download.md) says.
2. Unzip it. You get a folder named for the mod ID, `metaphor.accessibility`.
3. Open Reloaded-II's settings file, `%APPDATA%\Reloaded-Mod-Loader-II\ReloadedII.json`, in Notepad. Find the value of
   `ModConfigDirectory`. This is the mods folder
   ([P4G Access notes](../../raw-sources/persona-4-golden-access-2026-11-16.md)).
4. Copy the `metaphor.accessibility` folder into that mods folder.
5. Enable the mod for the game. Doing this in Reloaded-II's window is hard with a screen reader, so the P4G Access mod
   ships a ready-made `AppConfig.json` for the game's folder under Reloaded-II's applications folder. We don't ship one
   yet. `[NEEDS VERIFICATION]` Enabling the mod by ear in the window: find the game's mod list with the review cursor,
   move to `metaphor.accessibility`, and click it.

Developers can skip all of this with `scripts/dev/session.ps1 deploy`
([deploy a dev build](../../contributors/how-to/deploy-a-dev-build.md)).

## Make a desktop shortcut that starts the game with the mod

Always start the game from this shortcut. These steps are P4G Access's method, which we expect to work the same here
`[NEEDS VERIFICATION]`.

1. Open Reloaded-II.
2. Press `Tab` to reach the application list and open the game's entry.
3. Use NVDA's review cursor (`NVDA+numpad` keys, or `Shift+NVDA+arrows` on a laptop) to reach `Create Shortcut` on the
   game's page.
4. Move the mouse to the review cursor with `NVDA+Shift+M`, then click with `NVDA+numpad slash`, or `NVDA+[` on a
   laptop.
5. A shortcut appears on the desktop, named after the game with `(Reloaded)` in it. `[NEEDS VERIFICATION]` the exact
   name and whether NVDA announces the click.

P4G Access's own guide says not to start the game from Steam, because it often fails to load some mods. Another
instruction from its author says Steam works while Reloaded-II is running, but the shortcut is more reliable. Which
holds for Metaphor is `[NEEDS VERIFICATION]`; use the shortcut.

## Launch

1. Close the game and Reloaded-II if they are running.
2. Open the `(Reloaded)` shortcut on the desktop. The game starts. You can close Reloaded-II's window if it shows up.
   `[NEEDS VERIFICATION]` whether Reloaded-II stays open.

## Copy in a Reloaded-II app config

Enabling a mod in Reloaded-II's window is hard with a screen reader. Persona-4-Golden-Access gets round it by shipping a
ready-made `AppConfig.json`. We don't ship one yet, so edit the one Reloaded-II made
([P4G Access notes](../../raw-sources/persona-4-golden-access-2026-11-16.md)). `[NEEDS VERIFICATION]` All of this
section, by ear.

1. In Notepad, open `%APPDATA%\Reloaded-Mod-Loader-II\ReloadedII.json`. Find `ApplicationConfigDirectory`. This is where
   Reloaded-II keeps one folder per game.
2. Open the folder for the game, the one holding an `AppConfig.json` whose `AppId` is `metaphor.exe`. If you added the
   game as above, it is there.
3. Open that `AppConfig.json` in Notepad. Find `EnabledMods`. Make it list the mod's ID, so the line reads
   `"EnabledMods": ["metaphor.accessibility"]`. Save the file.
4. If `AppLocation` doesn't point at your `metaphor.exe`, correct it. A relative path is read from the folder holding
   the file ([Reloaded-II config notes](../../raw-sources/reloaded-ii-app-and-mod-config-2026-11-15.md)).

## What the mod says when it loads

When the game starts, the mod speaks one of three messages once:

1. `Metaphor accessibility loaded`: the mod is running.
2. `This game version isn't supported yet; the mod is off`: your game build is one the mod doesn't know. It hooks
   nothing and the game runs as normal.
3. `Metaphor accessibility couldn't start; the mod is off`: any other failure.

You may hear nothing if your screen reader or Windows speech isn't reachable. Then look at the newest file in
`%APPDATA%\Reloaded-Mod-Loader-II\Logs\`, which lists the mod by its ID, `metaphor.accessibility`. If you hear the first
message, the install worked.

## If the mod says nothing: run the install check

The mod folder holds `check-install.ps1`, which says why the mod didn't load. It prints each result. With no screen
reader running, it also speaks each one with Windows' own voice, so it works when the mod's speech doesn't. With NVDA,
JAWS or ZoomText running it speaks each one through your screen reader. Spoken lines leave out folder paths
and long codes; the printed lines keep them. It changes no file or setting and reads nothing over the network. In
Windows PowerShell 5.1 it writes temporary files in your temp folder while it looks for JAWS and ZoomText, and removes
them.

1. Press `Windows+R`, type the line below with the path to your mod folder, and press `Enter`:

   `powershell -ExecutionPolicy Bypass -NoExit -File "<mods folder>\metaphor.accessibility\check-install.ps1"`

   The unzipped folder you downloaded works too.
2. It checks, in order: Reloaded-II is set up; the mod is in its mods folder; the game is added and the mod turned on
   for it; the game's version is one the mod knows; the speech library, `prism.dll`, is in place; how the mod last
   started. Each line starts with "Pass", "Problem" or "Not checked". A last "Note" says when no screen reader the
   mod supports is running; that isn't counted as a problem, since the mod then uses Windows' own voice.
3. The last line names the first thing to fix. Fix it, then run the check again.

`[NEEDS VERIFICATION]` By ear with NVDA, and whether Windows asks before running a script from a downloaded zip. If the
check finds nothing wrong and the mod is still silent, send its output with the
[alpha feedback form](#report-a-problem).

## Keys and buttons

Everything the mod reads is here. The mod reads no mouse input.

### Repeat and reader toggle

1. `R` on the keyboard, or `X` on a controller: repeats the last dialogue line in full. With no line yet, it says "No
   dialogue to repeat".
2. `T` on the keyboard, or `LB` and `RB` held together on a controller: turns spoken dialogue off or on. It says
   "Dialogue reader on" or "Dialogue reader off".

A press counts when the key goes down, not while it is held. A key already held when a dialogue window opens does
nothing. They can't be rebound yet.

They work only while a dialogue window is open, a line or a choice, and the game doesn't see them then. Anywhere else,
such as a menu or the field, they do nothing and keep their normal job in the game. Reader off silences dialogue lines
only, not the keys, the load message or the settings message.

### In the field: what is near and where you are

1. `N` on the keyboard: says what is near you, nearest first, up to 8 things: people, doors, chests and enemies. Each
   says its name, or its kind when it has none, then how far and which way from where you face, such as "Chest, 4
   steps ahead-left". "In reach" means you can probably use it from where you stand. With nothing near it says
   "Nothing nearby".
2. `LB` held, then `Down` or `Up` on the controller's D-pad: steps through the same list one thing at a time, such as
   "2 of 5. Door, 9 steps behind". After 10 seconds without a step it starts again at the nearest.
3. `L` on the keyboard, or `LB` held and `Left` on the D-pad: says where you are, by the place name the game last
   showed. Before the game has shown one, it says "Place not known yet".
4. With no key: walking up to a door or chest says "Door, in reach" or "Chest, in reach" once, after anything being
   said. Move away or turn, then come back, and it says it again. People aren't said this way yet.

They work only while you walk around freely, with no menu or dialogue open, and the game doesn't see the key, or the
D-pad direction, then. Anywhere else they keep their normal job in the game. Distances are rough steps for now: the mod
doesn't yet know how long a step is in the game, so treat them as near or far.

`[NEEDS VERIFICATION]` All by ear in the game. They need a supported game build.

### Menus and choices: no keys of their own

The mod adds no keys here. It speaks what the game's own cursor does, so use the game's own movement keys or buttons.

1. Title menu: when the cursor settles on an item, the mod speaks it with its position. The first time it adds
   "Main menu" in front, such as "Main menu, New Game, 1 of 4".
2. Reply choices in dialogue: when the choices appear, the mod speaks "Choose:" and the options. Moving the cursor
   speaks the option and its position, such as "Option, 2 of 3".
3. The menu during play: opening it speaks "Menu" and the entry, such as "Menu, Equipment, 3 of 9"; each move speaks
   the entry, such as "Party, 4 of 9". The entries, in order: Skill, Item, Equipment, Party, Follower, Quest, Calendar,
   Memorandum, System. The names are the mod's own wording, as the game draws them as pictures.
4. Any other menu screen: each move speaks the position and the list's length, such as "4 of 8", or the position
   alone, such as "4", when the mod can't read the length.

`[NEEDS VERIFICATION]` All by ear in the game. They need a supported game build.

### Voiced lines

For lines the voice actors speak, the mod can say the speaker only, or the full line. The default (`auto`) says the name
when the voice is English and the full line when it is Japanese. To change it, make a file named `settings.json` in the
mod's user-config folder, by default `User\Mods\metaphor.accessibility` next to Reloaded-II (`[NEEDS VERIFICATION]` where
it lands on your machine), holding:

`{ "voicedLines": "nameOnly" }`

The choices are `auto`, `nameOnly`, `fullLine` and `nothing`. The mod reads the file at start and again when you save
it, and says "Voiced lines: name only." (or the new value) when it changes. A file it can't use is ignored and the
default applies.

### Update check

Each time the game starts, the mod asks GitHub once for its newest release, and the title screen says "A newer version
of the mod is available:" and the version when there is one. It sends nothing about you. To stop it, add
`"checkForUpdates": false` to the same `settings.json`; see
[turn off the update check](turn-off-the-update-check.md).

### Braille

There is no braille setting. Each announcement goes to your screen reader's speech and braille together, where it
supports that, and to speech alone where it doesn't. `[NEEDS VERIFICATION]` By ear and with a display.

## Uninstall

The mod's folder holds `uninstall.cmd`, which starts the same installer to remove the mod. It says each step as the
install does. `[NEEDS VERIFICATION]` All of this section, by ear.

1. Close the game and Reloaded-II.
2. Open the mod's folder in File Explorer: the unzipped folder you downloaded, or the mod's folder in Reloaded-II's
   mods folder, which the uninstall then removes. Move to `uninstall.cmd` with the arrow keys, or type `uninstall`, and
   press Enter. Windows may ask first, as it does for `install.cmd`; choose `Run`. If Windows won't open it, press
   `Windows+R`, type this with the folder's full path in place of `<folder>`, and press Enter:

   `powershell -ExecutionPolicy Bypass -NoExit -File "<folder>\install.ps1" -Uninstall`

3. NVDA reads "Uninstall the mod?" and what it does. Type `yes` and press `Enter`. Enter alone stops without changing
   anything.
4. It says five steps, each starting with "Done", "Nothing to remove" or "Failed": it turns the mod off for the game in
   Reloaded-II, keeping your other mods on; removes the mod's folder and the mod's settings from Reloaded-II; deletes
   the desktop shortcut "Metaphor ReFantazio with accessibility"; and checks Reloaded-II.
5. If the installer installed Reloaded-II, it asks "Remove Reloaded-II too?". That also removes every other mod and game
   you added to Reloaded-II, so press `Enter` alone to keep it, or type `yes` and press `Enter` to remove it. A
   Reloaded-II you installed yourself is kept without asking.
6. It says it kept the .NET 9 Desktop Runtime and the Visual C++ Redistributable, which other programs may use. To
   remove them, use Installed apps in Windows Settings.
7. Its last line is "The uninstall is done", or "There was nothing left to remove" when you run it again. The window
   stays open and then says "The uninstall has finished. Read what it said above, then press any key to close this
   window."

It removes nothing outside Reloaded-II's folder, and leaves alone a folder that holds a link to somewhere else and a
shortcut of the same name that starts another program; a step that leaves something says so as "Failed" and what to
do. The other steps still run. Reloaded-II's settings file in your AppData folder stays.

## Report a problem

Use the Alpha feedback form on the project's issues page.

1. Find `report-log.txt` in the `metaphor.accessibility` folder in your mods folder. The mod writes it fresh each time
   the game starts, so copy it before you start the game again.
2. Open the form, answer its questions, and drag the file into the "Report log" box, or choose it with the file picker.
3. The log holds what the mod did (whether it loaded, how many hooks went in, errors, and which lines and choices the
   game asked for, by label such as `Title_System_08`), never the game's text, and your user name is replaced with
   `<redacted>`. Read it in Notepad first if you want to check.

## Credits

- The desktop-shortcut steps, the NVDA review-cursor steps and the idea of a copy-in app config come from
  [Persona-4-Golden-Access](https://github.com/AquaRose7/Persona-4-Golden-Access) by AquaRose7 (GPL-3.0), at `dcff255`
  (v2.2.6). No code is copied.
- Listing every key, with when it works, comes from the [AccessMods](https://github.com/AccessMods/PhoenixWrightTrilogy)
  organisation (MIT): blind players of its mods hit keys the README left out. Pinning the loader version follows its
  CONTRIBUTING.

More in [`CREDITS.md`](../../../CREDITS.md).
