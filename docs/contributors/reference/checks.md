# Checks

One command runs every check, locally (Git Bash on Windows) and in CI:

| Stage | Runs | Where |
|---|---|---|
| `scripts/check.sh fast` | repo rules on staged files | by hand |
| `scripts/check.sh pre-commit` | `fast`, then formatting for staged C#, `docs` when Markdown is staged, `signatures` when `hook-signatures.json` is staged and `METAPHOR_GAME_EXE` is set, and a matching security-review stamp when the security-review scope is staged ([security review](security-review.md)) | pre-commit hook |
| `scripts/check.sh pre-push` | the Release build, then `Unit` and `RoundTrip` tests, when the pushed commits change code (refs on stdin) | pre-push hook |
| `scripts/check.sh rules` | repo rules on every tracked file in CI; locally on the files changed since `origin/main` (every file without `origin/main`, when the rules change, or with `METAPHOR_SCAN_ALL=1`) | CI on Ubuntu |
| `scripts/check.sh full` | repo rules, then the .NET build, `Unit` and `RoundTrip` tests, formatting, the scripts' own Pester tests (Pester 5 or later; a version other than the pin warns) and the check-script suites ([`run-check-suites.sh`](../../../scripts/check/run-check-suites.sh)) | CI: Linux on pull requests, Windows in the merge queue and on `main` (see below) |
| `scripts/check.sh docs` | broken relative links and anchors, links to missing paths on this repository's main branch, and relative links under `.github/` fail; Markdown lint, Vale and ADRs over 250 words warn | CI on Ubuntu |
| `scripts/check.sh slow` | Stryker.NET mutation testing of `Metaphor.Core`, the parsers' property tests at 10,000 cases from fresh seeds (ADR 0015) and a jscpd duplication report, into `artifacts/slow/`; reports only, fails only when a tool can't run | weekly CI on Windows (`slow.yml`, also run by hand); never gates a merge |
| `scripts/check.sh signatures [EXE]` | each hook signature matches exactly once in the game exe on disk, no launch ([how-to](../how-to/check-hook-signatures.md)) | locally, before a hook PR |
| `scripts/check.sh pr-body FILE` | warns when a PR description passes 300 words, not counting Evidence | CI on pull requests |
| `scripts/check.sh commit-msg FILE` | one commit message's shape (see the script's header); appends the `Security-Review` trailer to a commit touching the security-review scope | commit-msg hook |
| `scripts/check.sh commits BASE..HEAD` | each commit's shape in a pull request, and a matching `Security-Review` or `Security-Review-Skipped` trailer on each one touching the scope | CI on Ubuntu |

`full` prints each step's time (`restore`, `build`, `tests`, `format`, `pester`, `check-scripts`) and keeps them in the
run log. Locally the pre-push build covers only the test projects the change selects (and any changed project or
dependent of one that no test reaches); a build-wide file or an unplaceable change builds the solution.
`tests/check/test-check.sh` runs its tests in parallel (`METAPHOR_TEST_JOBS=1` runs them one at a time), as does
`scripts/check/run-pester.sh`, and `scripts/check/run-check-suites.sh` runs the suites in the same bounded pool
([`pool.sh`](../../../scripts/check/pool.sh)). These print a line per step, test or file as it finishes
(`[37/132] test_x ok (2.1 s)`) and write it to the clone's live log, `<git dir>/check-live-log`, which
`scripts/check/check-status.sh` reads (`test-check: 37/132, 12 min`). Started from Git Bash, the check-script suites
run in WSL when it's set up, in seconds instead of minutes
([set up a clone](../how-to/set-up-a-clone.md#run-the-check-script-suites-in-wsl)).

Without `METAPHOR_GAME_EXE`, the pre-commit hook skips `signatures` and says how to run them. A merge that commits
itself runs the same checks as a commit (`pre-merge-commit` hook).

## Recorded pass

A passing `full` leaves a [recorded pass](../../../GLOSSARY.md#review-and-ci) in the git dir, keyed by the index's tree
hash; `full` or `pre-push` on an identical tree says it reused the pass and skips the work. `pre-push` takes a
machine-wide build slot only when it builds, so a docs-only push never waits on it. Run `full`, then push: with no
recorded pass and every slot busy, `pre-push` stops at once instead of waiting, since a waiting push is cut (exit 141).

## RoundTrip tests

`RoundTrip` tests check our format code against real game files, so they need game data and are skipped without it (CI
never has any, per [ADR 0001](../../adr/0001-ci-never-sees-game-content.md)). To run them, point `METAPHOR_GAME_DATA`
at your extracted files, or put them in the git-ignored `extracted/` folder. If the variable is set, `extracted/` isn't
checked. Put the [game build](../../../GLOSSARY.md) on the first line of `game-build.txt` in that folder; the tests show
it in their names.

## In CI

`full` needs the .NET SDK pinned in `global.json`. CI runs it by two paths under the same required checks
(`repo-rules`, `build`, `scripts`, `security`, and `review-gate`, the label gate run from `main`'s copy of
[`review-gate.yml`](../../../.github/workflows/review-gate.yml)):

- **Pull requests** do the .NET work on Linux, minus the tests marked `WindowsOnly`, and skip what a change doesn't
  touch. The Pester script tests run on Windows as parallel `pester` jobs, one per test file, and the `scripts` check
  waits for them all (#314). Each job starts with a `git diff` check (`scripts/ci/what-runs.sh`, which also picks
  the pre-push's tests, #413) and finishes green at once when the change can't affect it.
- **The merge queue and pushes to `main`** run everything on Windows, then package, zip and rebuild the mod
  reproducibly. Beside the build, `pester-varied-setup` runs every Pester file once under a German culture, from a
  checkout and temporary folder whose paths hold a space and non-ASCII characters (#459), and `scripts` waits for it
  too. On `main`, `attest` signs the zip ([Check a download](../../players/how-to/check-a-download.md)). The
  path-by-path detail is in [how a change reaches main](../explanation/how-a-change-reaches-main.md) and
  [ADR 0008](../../adr/0008-ci-fast-path-and-windows-gate.md).
- **A `v*` tag on a commit on `main`** runs the `Release` workflow (#310), which drafts an attested release:
  [Cut a release](../how-to/cut-a-release.md). Players' builds stay Windows-built, because a Linux build isn't
  byte-identical ([measurement](../../raw-sources/linux-build-spike-2026-11-16.md)). A local package won't match CI's
  hash, because CI-only build settings remove machine paths; the CI artifact is the one that counts.

A pull request that passes can still fail in the queue; run `full` on Windows locally before you push a change near
Windows behaviour. Mark a test that only holds on Windows with `[Trait("Category", "WindowsOnly")]` instead of `Unit`.

## Budgets

Each stage is timed against its budget from [#1](https://github.com/Sour-Dev-Home/metaphor-accessibility/issues/1)
(`scripts/check/stage-timing.sh`). `fast` and `pre-commit` (stage 1, 10 s) and `signatures` (stage 4, 30 s) print a
`timing:` line when they finish. CI's `timing` job lists each job's duration on the run's summary page and times stage
2 from the first job's start to the last one's finish (5 min); `slow.yml` lists stage 6's, which has no budget. A stage
over budget prints a warning, and in CI an annotation on the run; it never fails the run.

## Content rules

Turn on the git hooks and set up your private patterns once per clone:
[set up a clone](../how-to/set-up-a-clone.md#2-clone-and-set-up).

The checks also fail a raw source without its header ([`docs/raw-sources/`](../../raw-sources/README.md)) and a finding
without its game build ([`docs/findings/`](../../findings/README.md)).

The checks block game content and personal information. CI checks general patterns on every pull request and
merge-queue run, and the private list on pushes to `main`, from a secret of the `main-push` environment, which only
`main` may use. To set it, a maintainer runs:

```sh
gh secret set PII_PATTERNS --env main-push < .claude/pii-patterns.txt
```

Pushes to `main` fail until the secret is set. The check never prints a matching line or pattern, because CI logs are
public.

The general patterns are local user folders (`C:\Users\<name>` and its Git Bash, WSL, Linux and macOS forms) and email
addresses, apart from reserved example domains, GitHub no-reply addresses, SSH remotes and `@2x` image names. A
third-party licence notice, a file named `LICENSE`, `COPYING` or `NOTICE` (with at most one extension, such as `.MIT`)
directly inside `src/<project>/licenses/<library>/`, skips the email rule only, because it ships verbatim with its
author's address
([ADR 0005](../../adr/0005-prism-speech-backend.md)). The user-folder and private patterns still apply to it.
