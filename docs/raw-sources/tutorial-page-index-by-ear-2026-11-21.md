# Tutorial pop-up page index, by-ear run of PR #630's build, 2026-11-21

- **Source:** the maintainer's by-ear run of PR #630's Debug build with `-OptIn tutorial-window` (NVDA on), relayed by
  the main session on 2026-11-21, and that run's `message-log.txt` (local, not saved here), read with a throwaway
  Python 3.12.10 script that printed field numbers only.
- **Saved:** 2026-11-21.
- **Game build:** `548ddc955c176867f062c44f94c03dd9ac33caefb278a5ac388e792aedf09504` (Steam build 18330018).

**What the maintainer heard and did.** The pop-up's title, page count and first page were spoken; later pages weren't
(expected). Tutorial 46 opened at about 288.5 s. Pages turned to page 2 at about 355.75 s, page 3 at about 379.46 s,
then back to 2 and 1 at about 392.53 and 392.83 s, with cue 27 at each.

**What the log shows.** The window's `tutorial window` records at 302,702 ms (page 1, the last record before the first
turn), 355,751, 379,455, 392,526 and 392,826 ms, the same window (class `0x22576a0`) and the same object at `+0xE0`
(class `0x2257668`) throughout. Of every logged field of the window, the object and what they point to, one steps with
the turns:

| Time (ms) | Page | object `+0x8` | object `+0xC` |
|---|---|---|---|
| 302,702 | 1 | 0 | 3 |
| 355,751 | 2 | 1 | 3 |
| 379,455 | 3 | 2 | 3 |
| 392,526 | 2 | 1 | 3 |
| 392,826 | 1 | 0 | 3 |

The other fields that changed (in the object's pointee at `+0x90`, offsets `+0x4C` to `+0x7C`) went from 0 to fixed
values on the first two turns and then stayed put, so they don't count pages. So `+0x8` of the object is the page shown,
from 0, and `+0xC` the page count, as #594's run found.
