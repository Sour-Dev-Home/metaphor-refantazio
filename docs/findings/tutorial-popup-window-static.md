# A tutorial pop-up opens by tutorial number in `.ecode`; its pages are drawn from `.text1`

- **Game build:** `548ddc955c176867f062c44f94c03dd9ac33caefb278a5ac388e792aedf09504` (Steam build 18330018)
- **Method:** Opening the shipped executable in a disassembler as-is, accepting that protected parts may be unreadable
- **Sources:** none, see Steps; follows [`tutorial-popups-route.md`](tutorial-popups-route.md) and
  [`title-names-voice-tutorials-static.md`](title-names-voice-tutorials-static.md)

For #118: the pop-up is `UUIC_Common_TutorialWindow@ui@app`, driven by `UUIC_Common_TutorialWindow_Controller`. The
controller opens it with a **tutorial number** (`0xfae440` loops over numbers below `0xce`, so probably 206 of
them), not a label. The open, the update and the controller are in `.ecode`; turning the number into pages, and
turning pages, happens in `.text1` thunks, so no `.ecode` function takes a page label or index. What the mod can
hook is the open (the number) and the window's update (its state each frame). Nothing here reads the page text yet;
the DevOnly listeners in `src/Metaphor.Hooks/TutorialWindowListeners.cs` log what the next run needs to find it.

## Evidence

1. **The window.** Vtable `0x22576a0`, `0xe8` bytes (deleting destructor `vf0`, `0xfad790`, frees `0xe8`). The
   destructor `0xfad360` names its members: `+0x30` a `StaticArray<int,2>`, `+0x50` a `StaticArray<std::string,4>`
   (elements from `+0x60`, `0x20` each), `+0xe0` an owned object. `vf9` (`0xfadd50`) is "ended": `+0x18 == 3`.
   `vf1` (`0xfadd80`) is "ready" and ends in a `.text1` thunk.
2. **The update**, `vf13` (`0xfaed70`): a state machine on `+0x18` (0, 1, 2, 3 = ended). It loops over the two ints at
   `+0x30`, frees each through a `.text1` thunk and sets it to `-1`, and on one branch of state 1 it calls
   `0x92e3e0` with 1 (the sound heard at the close). The two ints are probably the two message files' ids
   (`System_Tutorial_1.msg` holds the titles, `_2.msg` the pages, by their label families in the run's
   compile records) `[NEEDS VERIFICATION]`.
3. **The controller**, vtable `0x2257790`, `vf13` (`0xfaf0c0`): state at `+0x24` (0 open, 1 shown, 2 done; `vf9`
   `0xfadd70` tests `== 2`), the tutorial number at `+0x18`, flags at `+0x1c` to `+0x1e`, and a mode byte at `+0x20`
   that picks one of two windows from two getters (`0xfac5a0`, `0xfac660`). It is the only `.ecode` caller of either
   open (a raw scan of `.ecode` for `call rel32`).
4. **The opens.** `0xfadf00` (`void (window*, uint32* tutorial, bool force, uint8, uint8)`) is this window's: it
   writes the number to `+0x1c`, fills the four strings with `COMMON/movie/tutorial/` paths from a per-page accessor
   (`0x3e2ad0`, a `jmp` into `.text1`), hands the number to a `.text1` thunk with the child at `+0x20`, builds a
   `0x98`-byte object in `.text1` into `+0xe0`, then calls `0x92e3e0` with `0x1d` (29, probably the open's sound, by
   item 5). `0xfae440` (`void (window*, uint32* tutorial, bool force)`) is the same for the other window (object at
   `+0x30`); its class is `[NEEDS VERIFICATION]`, but it shows the party-join pop-up
   ([raw source](../raw-sources/tutorial-party-join-window-by-ear-2026-11-21.md)). The per-tutorial data accessors near
   `0x3e2a70` all jump into `.text1`.
5. **The sounds match the run.** In the by-ear run of 2026-11-20 (maintainer; logs local, counts and cue numbers only)
   each of the five pop-ups logs cue 29 about 35 ms after the two files compile, cue 27 once per page turn and cue 1
   at the close. No `.ecode` call plays 27 from the window's code (a scan for `0x92e3e0` calls with an immediate 27
   finds six, none in `0xfa0000`-`0xfc0000`), so page turns are handled in `.text1` too.
6. **Why the text took no hooked route.** The run's 36 `lookup` records all have message id 3, none a tutorial file's
   id; its 657 `resolve` records include serial 1154 before each pop-up and no tutorial serial. The text is fetched
   inside `.text1`.

## What the mod could do

- **Know a pop-up opened, and which:** a hook on `0xfadf00` (and `0xfae440`) reads the tutorial number. The table
  behind the `.text1` accessors is `COMMON/ui/tutorial/TutorialIDPC.bin`, which maps the number to its title's and
  pages' labels ([finding](tutorial-table-maps-numbers-to-labels.md)); the mod reads it from the archive.
- **Know which page shows:** the `+0xe0` object's `+0x8`, from 0, beside the page count at `+0xc`: it stepped with each
  page turn of a by-ear run ([raw source](../raw-sources/tutorial-page-index-by-ear-2026-11-21.md)).
- **The text:** once the label is known, the mod already has it: the message library keeps every compiled file by
  label, and both tutorial files compile as each pop-up opens.

## Steps

Ghidra 12.2 with JDK 25.0.5+8, the shared project built by `pwsh scripts/dev/ghidra.ps1 build` on the build above,
Windows 11 x64, 2026-11-20. Each line is one `ghidra.ps1` query unless it says otherwise. Nothing in `.text1` was
decompiled; thunks into it were not followed.

1. `vtable app::ui::UUIC_Common_TutorialWindow`, `vtable app::ui::UUIC_Common_TutorialWindow_Controller`.
2. `global 0x22576a0` (only the destructor `0xfad360` writes it), `decompile 0xfad360`, `0xfad790`, `0xfadd80`,
   `0xfaed70`.
3. `Metaphor.SignatureCheck --bytes <exe> 0xfaf0c0 0x120`, read by hand (the controller); `--bytes` on `0xfadd50`
   and `0xfadd70` (the `vf9`s), `0xfac5a0`, `0xfac660`, `0x3e2a70` to `0x3e2cd0`, `0x9128e0`.
4. `decompile 0xfae440`, `0xfadf00`.
5. Python 3.12 over the exe's `.ecode` bytes: `call rel32` sites targeting the opens, the getters and the updates, and
   calls to `0x92e3e0` with `lea ecx,[rdx+imm8]` or `mov ecx,imm32` just before; `decompile 0xf39411` (one of the six
   with 27: a menu, not the window).
6. The by-ear run's `message-log.txt`: record kinds counted, and the records within 120 s after each pair of tutorial
   compiles, other than input, listed by kind, number and cue id (no text).
7. `Metaphor.SignatureCheck <exe>`: `TutorialWindowShow` (`0xfadf00`), `TutorialWindowShowOther` (`0xfae440`) and
   `TutorialWindowUpdate` (`0xfaed70`) each match once.

The class and file names are the exe's own; no game text is quoted.
