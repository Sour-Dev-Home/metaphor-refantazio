# The party-join pop-up opens through the second tutorial window, by-ear run of 2026-11-21

- **Source:** the maintainer's by-ear run of PR #630's Debug build (NVDA on), relayed by the main session on
  2026-11-21, and that run's `message-log.txt` (local, not saved here), read with grep.
- **Saved:** 2026-11-21.
- **Game build:** `548ddc955c176867f062c44f94c03dd9ac33caefb278a5ac388e792aedf09504` (Steam build 18330018).

**What the maintainer heard.** A pop-up announcing the first party member joining wasn't voiced.

**What the log shows.** Both `System_Tutorial` message files compiled at 966,359 ms. At 966,393 ms there was one `open`
record through `TutorialWindowShowOther` (`0xfae440`), for window `0x5eaeb5f0`, with `id=1` and `force=0`. Its object at
`+0x30` is class `0x2257668`, the same class as the other window's `+0xE0` object, with `+0x8=0x0,0x1` (page 0 of 1).
Its `+0x20` field holds the window's own address, as the other window's object does. No `TutorialWindowUpdate` record
names this window. In the tutorial table, entry 1 is a one-page party-member pop-up
([finding](../findings/tutorial-table-maps-numbers-to-labels.md)).
