# Tutorial pop-up by-ear run, 2026-11-21

- **Source:** the maintainer's comment on issue #118, 2026-11-21T05:18:24Z (GitHub account `SourE-dev`, the
  repository's admin), reporting a by-ear run of PR #594's development listeners. Logs local, not saved here.
- **Saved:** 2026-11-21.
- **Game build:** `548ddc955c176867f062c44f94c03dd9ac33caefb278a5ac388e792aedf09504` (Steam build 18330018).

The comment, verbatim except the tutorial's on-screen title, replaced with `[title]` (no game text in the repository):

> By ear, maintainer, 2026-11-21 (test build: main + #578 #583 #591 #593 #594 #605; NVDA): the open record fired:
> tutorial id=46 is "[title]", 3 pages (model +0x8's second field = 3, the page count). Page turns (cue 27 at ≈391,
> 419, 450 s) logged a window record each, but no logged field counts the page: only model +0x8's first field flips
> 0/1. The page index is outside the logged bytes; widen the search.

`[title]` is the message `docs/findings/tutorial-table-maps-numbers-to-labels.md` finds for tutorial 46's title label.
