#!/usr/bin/env bash
# CI's pester-varied-setup job runs only when what it tests changed, and nightly (#655); the required `scripts` check
# reads its result and never lets a skip pass where it should have run. What-runs' selection itself is in
# tests/bash/test-what-runs.sh. Reads the repository, writes nothing.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=../../scripts/check/wsl-run.sh
source "$ROOT/scripts/check/wsl-run.sh" # in Git Bash, runs this suite in WSL instead when it's set up (#341)
# shellcheck source=../../scripts/check/suite-env.sh
source "$ROOT/scripts/check/suite-env.sh" # the contributor's git config and shell don't reach it (#455)
cd "$ROOT" || exit 2
CI=.github/workflows/ci.yml
passed=0 failed=0
check() { local n="$1"; shift; if "$@"; then passed=$((passed + 1)); else failed=$((failed + 1)); echo "FAIL $n"; fi; }

# job_block JOB: the job's lines in ci.yml, up to the next job.
job_block() { awk -v job="  $1:" '$0 == job { on = 1; print; next } on && /^  [A-Za-z0-9_-]+:[[:space:]]*$/ { exit } on' "$CI"; }
varied="$(job_block pester-varied-setup)"

check "ci.yml runs nightly" grep -qE '^  schedule:' "$CI"
check "the varied setup asks what-runs.sh" grep -qF 'what-runs.sh --job varied-setup "$BASE" "$HEAD"' <<< "$varied"
check "the varied setup's checkout has the history to diff" grep -qF 'fetch-depth: 0' <<< "$varied"
check "the varied setup diffs the queue's merge" grep -qF 'github.event.merge_group.base_sha' <<< "$varied"
# Every step after the change check is skipped together, or none is: one left out would run (or fail) on its own.
steps_after="$(awk '/id: changes/ { on = 1; next } on && /^      - / { n++ } END { print n + 0 }' <<< "$varied")"
gated="$(awk '/id: changes/ { on = 1; next } on && /^        if: steps\.changes\.outputs\.run == .true.$/ { n++ } END { print n + 0 }' <<< "$varied")"
check "every step after the change check waits on it ($gated of $steps_after)" [ "$steps_after" -gt 0 ] && [ "$gated" = "$steps_after" ]
for job in repo-rules build pester security; do
  check "$job skips the nightly run" grep -qE "^    if: github.event_name != 'schedule'" <<< "$(job_block "$job")"
done

# The aggregator, run as CI runs it, for each event and result.
gate="$(job_block scripts | awk '/^        run: \|/ { on = 1; next } on && /^        [^ ]/ { exit } on { sub(/^          /, ""); print }')"
check "the scripts job's script was found" [ -n "$gate" ]
scripts_gate() { # scripts_gate EVENT PESTER VARIED: exit 0 when `scripts` passes
  EVENT_NAME="$1" RESULT="$2" VARIED="$3" bash -c "$gate" > /dev/null 2>&1
}
passes() { check "scripts passes: $*" scripts_gate "$@"; }
scripts_gate_fails() { ! scripts_gate "$@"; }
fails() { check "scripts fails: $*" scripts_gate_fails "$@"; }
passes pull_request success skipped
passes merge_group success success
passes push success success
passes schedule skipped success
fails merge_group success skipped
fails push success skipped
fails schedule skipped skipped
fails merge_group success failure
fails merge_group success cancelled
fails pull_request failure skipped
fails pull_request skipped skipped
fails merge_group skipped success

echo "varied-setup tests: $passed passed, $failed failed"
[ "$failed" -eq 0 ]
