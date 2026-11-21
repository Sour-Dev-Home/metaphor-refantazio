#!/usr/bin/env bash
# CI's `pester` job runs one matrix entry per tests/pester/*.Tests.ps1 file (#314). A test file missing from the matrix
# would silently stop running in CI, so the two lists must agree. And setup-dotnet's package cache only where the job
# restores packages (#655). Reads the repository; writes only its own temporary folder.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=../../scripts/check/wsl-run.sh
source "$ROOT/scripts/check/wsl-run.sh" # in Git Bash, runs this suite in WSL instead when it's set up (#341)
# shellcheck source=../../scripts/check/suite-env.sh
source "$ROOT/scripts/check/suite-env.sh" # the contributor's git config and shell don't reach it (#455)
cd "$ROOT" || exit 2

matrix="$(sed -nE 's/^[[:space:]]*file: \[(.*)\]$/\1/p' .github/workflows/ci.yml | tr -d ' ' | tr ',' '\n' | sort)"
files="$(git ls-files 'tests/pester/*.Tests.ps1' | sed -E 's#.*/(.*)\.Tests\.ps1#\1#' | sort)"

failed=0
if [ -z "$matrix" ] || [ -z "$files" ]; then
  echo "FAIL could not read the matrix or the test files"
  failed=1
elif [ "$matrix" != "$files" ]; then
  echo "FAIL the pester matrix in ci.yml and tests/pester disagree:"
  diff <(echo "$matrix") <(echo "$files") | sed 's/^/  /'
  failed=1
fi
if ! grep -qE '^  scripts:$' .github/workflows/ci.yml; then
  echo "FAIL ci.yml lost the job named scripts, a required check"
  failed=1
fi

# setup-dotnet's package cache (#655): its post step fails the job with "Cache folder path is retrieved for .NET CLI but
# doesn't exist on disk" when the job restored nothing (actions/setup-dotnet v6.1.0, src/cache-save.ts; CI runs
# 44584066591 to 44590722916, 2026-11-19, before #395 and #401 turned it off in the security and Pester jobs). So
# `cache: true` only in a job with a step that restores (check.sh full or slow) under the same `if` as setup-dotnet.
# cache_without_restore FILE: print "FILE JOB" for each job that breaks it.
cache_without_restore() {
  awk -v file="$1" '
    function done_job() {
      if (job != "" && cached && !(restores && restore_if == cache_if)) print file " " job
      cached = 0; restores = 0; cache_if = ""; restore_if = ""
    }
    /^jobs:/ { in_jobs = 1; next }
    in_jobs && /^  [A-Za-z0-9_-]+:[[:space:]]*$/ { done_job(); job = $1; sub(":", "", job); next }
    in_jobs && /^[^[:space:]#]/ { done_job(); job = ""; in_jobs = 0; next }
    job == "" { next }
    /^[[:space:]]*- / { step_if = ""; in_setup = 0 }
    /^[[:space:]]*(- )?if:/ { cond = $0; sub(/^[[:space:]]*(- )?if:[[:space:]]*/, "", cond); step_if = cond; if (in_setup) cache_if = cond }
    /uses:[[:space:]]*actions\/setup-dotnet@/ { in_setup = 1; cache_if = step_if }
    in_setup && /^[[:space:]]*cache:[[:space:]]*true/ { cached = 1 }
    /scripts\/check\.sh (full|slow)/ { restores = 1; restore_if = step_if }
    END { done_job() }
  ' "$1"
}
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
cat > "$WORK/planted.yml" << 'EOF'
jobs:
  restores:
    steps:
      - uses: actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73
        if: steps.changes.outputs.run == 'true'
        with:
          cache: true
      - name: Build
        if: steps.changes.outputs.run == 'true'
        run: bash scripts/check.sh full
  no-restore:
    steps:
      - uses: actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73
        with:
          cache: true
      - run: pwsh -Command Invoke-Pester
  other-if:
    steps:
      - uses: actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73
        with:
          cache: true
      - if: github.event_name == 'push'
        run: bash scripts/check.sh full
  no-cache:
    steps:
      - uses: actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73
      - run: pwsh -Command Invoke-Pester
EOF
cache_checks=0
planted="$(cache_without_restore "$WORK/planted.yml")"
cache_checks=$((cache_checks + 1))
if [ "$planted" != "$WORK/planted.yml no-restore"$'\n'"$WORK/planted.yml other-if" ]; then
  echo "FAIL the cache rule misjudges the planted jobs; it flagged:"
  sed 's/^/  /' <<< "$planted"
  failed=1
fi
cache_checks=$((cache_checks + 1))
for workflow in .github/workflows/*.yml; do
  bad="$(cache_without_restore "$workflow")"
  [ -z "$bad" ] || { echo "FAIL setup-dotnet caches packages in a job that may restore none: $bad"; failed=1; }
done

[ "$failed" -eq 0 ] && echo "ci matrix tests: $((2 + cache_checks)) passed, 0 failed"
exit "$failed"
