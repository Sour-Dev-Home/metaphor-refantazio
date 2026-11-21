#!/usr/bin/env bash
# Tests for scripts/ci/what-runs.sh (#413): what a change runs, locally (#243) and in CI's jobs (#100).
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=../../scripts/check/wsl-run.sh
source "$ROOT/scripts/check/wsl-run.sh" # in Git Bash, runs this suite in WSL instead when it's set up (#341)
# shellcheck source=../../scripts/check/suite-env.sh
source "$ROOT/scripts/check/suite-env.sh" # the contributor's git config and shell don't reach it (#455)
cd "$ROOT" || exit 2
passed=0 failed=0
check() { local n="$1"; shift; if "$@"; then passed=$((passed + 1)); else failed=$((failed + 1)); echo "FAIL $n"; fi; }
sel() { printf '%s\n' "$@" | bash scripts/ci/what-runs.sh; }
has() { [[ "$out" == *"$1"* ]]; }
lacks() { [[ "$out" != *"$1"* ]]; }

out="$(sel src/Metaphor.Features.Menus/Foo.cs)"
check "feature change runs its tests" has "dotnet tests/Metaphor.Features.Menus.Tests/"
check "feature change runs the architecture tests (they reference it)" has "dotnet tests/Metaphor.Architecture.Tests/"
check "feature change skips the other feature's tests" lacks "Dialogue.Tests"
check "feature change skips Core tests" lacks "Core.Tests"
check "feature change skips Pester" lacks "pester"
check "reason is printed" has "src/Metaphor.Features.Menus/Foo.cs changed (via Metaphor.Features.Menus"

out="$(sel src/Metaphor.Core/X.cs)"
check "core change reaches Core tests" has "Core.Tests"
check "core change reaches Dialogue tests" has "Dialogue.Tests"
check "core change reaches Menus tests" has "Menus.Tests"
check "dependent reason names the file and the reference" has "src/Metaphor.Core/X.cs changed (via Metaphor.Core, which it references)"

out="$(sel tests/Metaphor.Core.Tests/A.cs)"
check "a test project change runs only itself" lacks "Menus.Tests"
check "a test project change runs itself" has "Core.Tests"

out="$(sel scripts/check.sh)"
check "scripts change runs Pester" has "pester scripts/check.sh changed"
check "scripts change runs no dotnet tests" lacks "dotnet"
out="$(sel scripts/dev/session.ps1)"
check "session script runs Pester" has "pester"

# The check scripts' own suite, which Git Bash without WSL runs only when they change (#336)
out="$(sel scripts/check.sh)"
check "a check script change runs test-check.sh" has "check-tests scripts/check.sh changed"
out="$(sel tests/check/test-check.sh)"
check "a check test change runs test-check.sh" has "check-tests tests/check/test-check.sh changed"
out="$(sel .github/workflows/ci.yml)"
check "a workflow change runs test-check.sh" has "check-tests"
out="$(sel src/Metaphor.Core/X.cs)"
check "a code change skips test-check.sh" lacks "check-tests"

out="$(sel docs/README.md)"
check "docs change skips test-check.sh" lacks "check-tests"
check "docs change selects the docs stage" has "docs docs/README.md changed"
check "docs change runs no tests" lacks "dotnet"
check "docs change skips Pester" lacks "pester"

out="$(sel Directory.Packages.props)"
check "build-wide file runs every test project" has "Dialogue.Tests"
check "build-wide file runs Core tests" has "Core.Tests"

# What the local build needs (#314)
out="$(sel tools/Metaphor.SessionLog/X.cs)"
check "a tool no test reaches is built, not tested" has "build tools/Metaphor.SessionLog/Metaphor.SessionLog.csproj"
check "a tool change runs no dotnet tests" lacks "dotnet"
out="$(sel src/Metaphor.Features.Menus/Foo.cs)"
check "a tested change names no extra build" lacks "build "
check "a tested change doesn't ask for the solution" lacks "solution"
out="$(sel docs/README.md)"
check "docs change builds nothing" lacks "build "
check "docs change doesn't ask for the solution" lacks "solution"
out="$(sel Directory.Packages.props)"
check "a build-wide file asks for the solution" has "solution Directory.Packages.props changed"

# Dependents no test reaches (#332): a Core change must still compile the tools and the stand-in game
out="$(sel src/Metaphor.Core/X.cs)"
check "a core change builds a tool that references it" has "build tools/Metaphor.SessionLog/Metaphor.SessionLog.csproj"
check "a core change builds the stand-in game" has "build tests/Metaphor.StandInGame/Metaphor.StandInGame.csproj"
check "a dependent's build names the change" has \
  "src/Metaphor.Core/X.cs changed (via Metaphor.Core, which it references; no test project covers it)"
out="$(sel src/Metaphor.Core/README.md)"
check "a docs file in a project builds nothing" lacks "build "
out="$(sel tests/Metaphor.Core.Tests/A.cs)"
check "a test-only change builds nothing extra" lacks "build "

# Every project that references a changed one, directly or not, is built by some selected line: read from the
# .csproj files here, so a new project can't slip past (#332).
refs_of() { # refs_of PROJECT: the projects its .csproj references, as repository paths
  grep -oE 'ProjectReference Include="[^"]+"' "$1" | sed -E 's/.*Include="([^"]+)"/\1/; s#\\#/#g' |
    while read -r r; do realpath -m --relative-to=. "$(dirname "$1")/$r"; done
}
mapfile -t all_projects < <(git ls-files -- '*.csproj')
declare -A refs
for p in "${all_projects[@]}"; do refs[$p]="$(refs_of "$p" | tr '\n' ' ')"; done
closure() { # closure PROJECT...: the projects and everything they reference, transitively, one per line
  local -A seen=()
  local todo=("$@") p q
  while [ "${#todo[@]}" -gt 0 ]; do
    p="${todo[0]}" todo=("${todo[@]:1}")
    [ -z "${seen[$p]:-}" ] || continue
    seen[$p]=1
    for q in ${refs[$p]:-}; do todo+=("$q"); done
  done
  printf '%s\n' "${!seen[@]}"
}
every_dependent_built() {
  local changed p q built missing=""
  for changed in "${all_projects[@]}"; do
    out="$(sel "$(dirname "$changed")/X.cs")"
    # shellcheck disable=SC2046 # one project per word
    built=" $(closure $(sed -nE 's/^(dotnet|build) ([^ ]+) .*/\2/p' <<< "$out") | tr '\n' ' ')"
    for p in "${all_projects[@]}"; do
      [[ " $(closure "$p" | tr '\n' ' ')" == *" $changed "* ]] || continue # p doesn't depend on the change
      [[ "$built" == *" $p "* ]] || missing+=" $p (changed: $changed)"
    done
  done
  [ -z "$missing" ] || echo "neither tested nor built:$missing"
  [ -z "$missing" ]
}
check "every project reachable from a changed one is tested or built" every_dependent_built

# A reference cycle (#321): reaches_test walks each project once, so it ends without nesting functions ever deeper.
cycle="$(mktemp -d)"
git init -q "$cycle"
mkdir -p "$cycle/src/A" "$cycle/src/B"
printf '<Project><ItemGroup><ProjectReference Include="..\\B\\B.csproj" /></ItemGroup></Project>\n' > "$cycle/src/A/A.csproj"
printf '<Project><ItemGroup><ProjectReference Include="..\\A\\A.csproj" /></ItemGroup></Project>\n' > "$cycle/src/B/B.csproj"
git -C "$cycle" add .
out="$(cd "$cycle" && printf 'src/A/X.cs\n' | FUNCNEST=50 timeout 60 bash "$ROOT/scripts/ci/what-runs.sh" 2>&1)"
check "a reference cycle ends" test $? -eq 0
check "a reference cycle nests no function deeper than the graph" lacks "nesting level"
check "a reference cycle builds both projects" has "build src/B/B.csproj"
rm -rf "$cycle"

# The pre-push's gate: `compile` for a file that can change the build or the tests, `pester` for a script (#413)
out="$(sel src/Metaphor.Core/X.cs)"
check "a source change asks the pre-push to build" has "compile src/Metaphor.Core/X.cs changed"
out="$(sel tools/docs/package-lock.json)"
check "a json file anywhere asks the pre-push to build" has "compile tools/docs/package-lock.json changed"
out="$(sel src/Metaphor.Core/README.md)"
check "a docs file in a project asks for no build" lacks "compile"
out="$(sel scripts/setup/onboard.ps1 tests/pester/onboard.Tests.ps1)"
check "a setup script and its Pester test select Pester (the 2026-11-19 push)" has \
  "pester scripts/setup/onboard.ps1 changed"
check "a setup script asks for no build" lacks "compile"
out="$(sel tests/pester/onboard.Tests.ps1)"
check "a Pester test alone selects Pester" has "pester tests/pester/onboard.Tests.ps1 changed"

# CI's jobs: the `code` line is the build and security job, the `pester` line the scripts job
out="$(sel docs/a.md README.md docs/raw-sources/x.txt)"
check "docs only skips the code job" lacks "code "
out="$(sel src/a.cs docs/a.md)"
check "a code change runs the code job" has "code src/a.cs changed"
out="$(sel .github/workflows/ci.yml)"
check "a workflow change runs the code job" has "code .github/workflows/ci.yml changed"
check "a workflow change runs the scripts job" has "pester .github/workflows/ci.yml changed"

# The selection is what the change needs; running everything (CI, METAPHOR_FULL_TESTS, `full`) is the caller's choice
out="$(GITHUB_ACTIONS=true METAPHOR_FULL_TESTS=1 sel docs/README.md)"
check "the environment never widens the selection" lacks "dotnet"
check "the environment never adds Pester" lacks "pester"
check "check.sh full never calls the selector" bash -c \
  "! sed -n '/^if \[ \"\$stage\" = full \]/,\$p' scripts/check.sh | grep -q what-runs"

# --job against a throwaway repository: which changed files let a CI job skip (#100)
jobs_repo="$(mktemp -d)"
(cd "$jobs_repo" && git init -q . && git config user.email t@example.invalid && git config user.name t &&
  git config commit.gpgsign false && echo a > README.md && git add -A && git commit -q -m "chore: base") || exit 2
base="$(git -C "$jobs_repo" rev-parse HEAD)"
# job NAME EXPECTED JOB EVENT PATH...: commit the paths on top of the base, then ask the script.
job() {
  local name="$1" expected="$2" job="$3" event="$4" got
  shift 4
  got="$(cd "$jobs_repo" && git checkout -q --detach "$base" && for path in "$@"; do
    mkdir -p "$(dirname "$path")" && echo "$RANDOM" >> "$path"
  done && git add -A && git commit -q -m "chore: change" &&
    EVENT_NAME="$event" GITHUB_OUTPUT='' bash "$ROOT/scripts/ci/what-runs.sh" --job "$job" "$base" "$(git rev-parse HEAD)")"
  check "$name (got $got)" [ "$got" = "run=$expected" ]
}
job "docs-only PR skips code jobs" false code pull_request docs/a.md README.md
job "raw-source PR skips code jobs" false code pull_request docs/raw-sources/x.txt
job "a code PR runs code jobs" true code pull_request src/a.cs docs/a.md
job "a workflow change runs code jobs" true code pull_request .github/workflows/ci.yml
job "docs-only PR skips Pester" false scripts pull_request docs/a.md
job "a source-only PR skips Pester" false scripts pull_request src/a.cs
job "a script change runs Pester" true scripts pull_request scripts/x.sh
job "a Pester test change runs Pester" true scripts pull_request tests/pester/a.Tests.ps1
job "the merge queue skips nothing (code)" true code merge_group docs/a.md
job "the merge queue skips nothing (scripts)" true scripts merge_group docs/a.md
job "a push to main skips nothing" true scripts push docs/a.md
GITHUB_ACTIONS=true job "CI's environment doesn't stop a PR skipping" false scripts pull_request docs/a.md
# pester-varied-setup (#655): only for the player and setup scripts, the Pester tests and their runners, in the merge
# queue and on main as on a PR; the nightly run and a run by hand take everything.
job "varied setup: a docs-only change skips it" false varied-setup merge_group docs/a.md
job "varied setup: a game-feature change skips it" false varied-setup merge_group src/Metaphor.Features.Menus/a.cs
job "varied setup: a dev script change skips it" false varied-setup merge_group scripts/dev/session.ps1
job "varied setup: an installer change runs it" true varied-setup merge_group scripts/player/install.ps1
job "varied setup: a setup script change runs it" true varied-setup merge_group scripts/setup/onboard.ps1
job "varied setup: a Pester test change runs it" true varied-setup merge_group tests/pester/install.Tests.ps1
job "varied setup: its runner changing runs it" true varied-setup merge_group scripts/check/run-pester.sh
job "varied setup: its Pester install changing runs it" true varied-setup merge_group scripts/ci/install-pester.sh
job "varied setup: a ci.yml change runs it" true varied-setup merge_group .github/workflows/ci.yml
job "varied setup: a PR's installer change runs it" true varied-setup pull_request scripts/player/check-install.ps1
job "varied setup: a push to main's docs change skips it" false varied-setup push docs/a.md
job "varied setup: a push to main's installer change runs it" true varied-setup push scripts/player/install.cmd
job "varied setup: the nightly run takes everything" true varied-setup schedule docs/a.md
job "varied setup: a run by hand takes everything" true varied-setup workflow_dispatch docs/a.md
out="$(cd "$jobs_repo" && EVENT_NAME=merge_group GITHUB_OUTPUT='' bash "$ROOT/scripts/ci/what-runs.sh" --job varied-setup '' '')"
check "varied setup: a merge-queue run with no diff runs it" [ "$out" = run=true ]
out="$(sel scripts/player/install.ps1)"
check "an installer change selects the varied setup" has "varied-setup scripts/player/install.ps1 changed"
out="$(sel scripts/dev/session.ps1)"
check "a dev script change doesn't select the varied setup" lacks "varied-setup"
out="$(cd "$jobs_repo" && GITHUB_OUTPUT="$jobs_repo/out" EVENT_NAME=pull_request \
  bash "$ROOT/scripts/ci/what-runs.sh" --job code "$base" "$base"; cat "$jobs_repo/out")"
check "an empty diff runs, and the answer reaches GITHUB_OUTPUT" [ "$out" = $'run=true\nrun=true' ]
out="$(bash scripts/ci/what-runs.sh --job nope 2>&1)"
check "an unknown job is a usage error" has "usage: what-runs.sh --job code|scripts"
rm -rf "$jobs_repo"

printf 'what-runs tests: %d passed, %d failed\n' "$passed" "$failed"
[ "$failed" -eq 0 ]
