#!/usr/bin/env bash
# Tests for scripts/check/suite-env.sh (#455): under a contributor's hostile global git config and shell (commit
# signing with an unusable signer, failing global hooks, another default branch, the check's switches exported), a
# suite's scratch repos still commit and push, and every suite sources it.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=../../scripts/check/wsl-run.sh
source "$ROOT/scripts/check/wsl-run.sh" # in Git Bash, runs this suite in WSL instead when it's set up (#341)
# shellcheck source=../../scripts/check/suite-env.sh
source "$ROOT/scripts/check/suite-env.sh"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
passed=0
failed=0

check() { # check NAME CONDITION...
  local name="$1"
  shift
  if "$@"; then passed=$((passed + 1)); else failed=$((failed + 1)); echo "FAIL $name"; fi
}
has() { [[ "$out" == *"$1"* ]]; }
lacks() { [[ "$out" != *"$1"* ]]; }

# The hostile config. Its paths in the form this git reads: C:/... in Git Bash.
HOSTILE="$WORK/hostile"
mkdir -p "$HOSTILE/hooks" "$HOSTILE/template/hooks" || exit 2
hostile_dir="$(cd "$HOSTILE" && { pwd -W 2> /dev/null || pwd; })"
for hook in pre-commit commit-msg pre-push; do
  printf '#!/bin/sh\necho "global %s hook ran" >&2\nexit 1\n' "$hook" > "$HOSTILE/hooks/$hook"
  cp "$HOSTILE/hooks/$hook" "$HOSTILE/template/hooks/$hook"
done
chmod +x "$HOSTILE"/hooks/* "$HOSTILE"/template/hooks/* || exit 2
cat > "$HOSTILE/gitconfig" << EOF || exit 2
[user]
	name = hostile
	email = hostile@example.invalid
[commit]
	gpgsign = true
[gpg]
	program = false
[core]
	hooksPath = $hostile_dir/hooks
	autocrlf = true
[init]
	templateDir = $hostile_dir/template
	defaultBranch = trunk
[log]
	showSignature = true
EOF

# scenario [--isolated]: in a subshell with the hostile config and shell (suite-env.sh sourced on top with
# --isolated), init a repo, commit, push to a bare remote, and print what a suite would see.
scenario() {
  (
    export GIT_CONFIG_GLOBAL="$HOSTILE/gitconfig" METAPHOR_SCAN_ALL=1 METAPHOR_CHECK_SLOTS=9 GIT_AUTHOR_NAME=hostile
    # shellcheck source=../../scripts/check/suite-env.sh
    [ "${1:-}" != --isolated ] || source "$ROOT/scripts/check/suite-env.sh"
    local repo="$WORK/repo-$RANDOM-$RANDOM"
    git init -q --bare "$repo.git" && git init -q "$repo" && cd "$repo" || exit 2
    printf 'a\n' > a.txt && git add a.txt && git commit -q -m 'chore: start' && echo committed
    git push -q "$repo.git" HEAD 2>&1 && echo pushed
    echo "branch $(git branch --show-current), author $(git log -1 --format=%an 2> /dev/null)"
    echo "scan-all ${METAPHOR_SCAN_ALL:-unset}, slots ${METAPHOR_CHECK_SLOTS:-unset}"
  ) 2>&1
}

# The hostile config is hostile: without suite-env.sh, its signer and hooks stop the commit.
out="$(scenario)"
check "the hostile config stops a commit without suite-env.sh" lacks committed
check "the hostile shell reaches a suite without suite-env.sh" has "scan-all 1"

out="$(scenario --isolated)"
check "a commit needs no signer and runs no global hook" has committed
check "a push runs no global hook" has pushed
check "no global hook ran" lacks "hook ran"
check "the default branch and identity are the suites' own" has "branch main, author test"
check "the check's switches are unset" has "scan-all unset, slots unset"

# The build slots and job count (#629, #655): a suite gets its own lock folder, so another check run taking or releasing
# a slot can't change what its cases see, and no held slot or job count from outside reaches them. suite_jobs still
# sizes the suite's own pool from the outside's slots.
slots() { # slots: in a subshell with an outside run's slots, source suite-env.sh and print what a suite sees
  (
    export METAPHOR_CHECK_LOCK="$WORK/shared/lock" METAPHOR_CHECK_HELD="$WORK/shared/lock" METAPHOR_TEST_JOBS="${1:-}"
    [ -n "${1:-}" ] || unset METAPHOR_TEST_JOBS
    # shellcheck source=../../scripts/check/suite-env.sh
    source "$ROOT/scripts/check/suite-env.sh"
    echo "lock ${METAPHOR_CHECK_LOCK:-unset}, held ${METAPHOR_CHECK_HELD:-unset}, jobs ${METAPHOR_TEST_JOBS:-unset}"
    echo "suite jobs $(suite_jobs)"
  )
}
mkdir -p "$WORK/shared/lock.2" && echo "$$" > "$WORK/shared/lock.2/pid" || exit 2 # another live run holds slot 2
out="$(slots 7)"
check "the suite's lock folder is its own" lacks "lock $WORK/shared/lock,"
check "the suite's lock folder is set" lacks "lock unset"
check "no held slot reaches the suite" has "held unset"
check "no job count reaches the suite" has "jobs unset"
check "suite_jobs keeps the outside's job count" has "suite jobs 7"
cores="$(nproc 2> /dev/null || echo 2)"
[ "$cores" -le 8 ] || cores=8
half=$((cores / 2 > 0 ? cores / 2 : 1))
out="$(slots)"
check "suite_jobs shares the cores with the outside's other runs" has "suite jobs $half"
first="$(slots | head -n 1)"
check "each run gets its own lock folder" [ "$first" != "$(slots | head -n 1)" ]

# Every suite sources it, so one run by itself is isolated too.
while read -r suite; do
  check "$suite sources suite-env.sh" grep -q '^source "$ROOT/scripts/check/suite-env.sh"' "$ROOT/$suite"
done < <(bash "$ROOT/scripts/check/run-check-suites.sh" --list)

echo "test-suite-env: $passed passed, $failed failed"
[ "$failed" -eq 0 ]
