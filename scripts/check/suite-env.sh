#!/usr/bin/env bash
# The check-script suites' environment (#455): every suite sources this once ROOT is set, right after
# scripts/check/wsl-run.sh, so the contributor's git config and shell can't change what a suite's git or check runs do.
# Sourced; builtins only.
#
# - git reads scripts/check/suite.gitconfig as its global config and no system config: a contributor's commit signing,
#   global hooks (core.hooksPath, init.templateDir), log.showSignature, init.defaultBranch or core.autocrlf don't reach
#   the suites' scratch repos, and those repos get a fixed identity. A repo's own config still applies.
# - The check's switches, and git's identity and config variables, are unset: a case that needs one sets it. The
#   runners' own variables (METAPHOR_NO_WSL, METAPHOR_LIVE_LOG, METAPHOR_LIVE_PID) stay.
# - The build slots (#629, #655): METAPHOR_CHECK_LOCK points at this run's own lock folder, under TMPDIR, and
#   METAPHOR_CHECK_HELD and METAPHOR_TEST_JOBS are unset, so other check runs taking or releasing slots can't change
#   what a case sees, and a case's check runs never wait on the machine's slots. A case that needs a lock sets its own.
#   suite_jobs prints the job count for the suite's own worker pool (pool_default_jobs, scripts/check/pool.sh) from
#   the slots and job count as they were outside.
suite_outer_jobs="${METAPHOR_TEST_JOBS:-}" suite_outer_lock="${METAPHOR_CHECK_LOCK:-}"
suite_outer_held="${METAPHOR_CHECK_HELD:-}" suite_outer_slots="${METAPHOR_CHECK_SLOTS:-}"
suite_jobs() {
  (
    unset METAPHOR_TEST_JOBS METAPHOR_CHECK_LOCK METAPHOR_CHECK_HELD METAPHOR_CHECK_SLOTS
    [ -z "$suite_outer_jobs" ] || export METAPHOR_TEST_JOBS="$suite_outer_jobs"
    [ -z "$suite_outer_lock" ] || export METAPHOR_CHECK_LOCK="$suite_outer_lock"
    [ -z "$suite_outer_held" ] || export METAPHOR_CHECK_HELD="$suite_outer_held"
    [ -z "$suite_outer_slots" ] || export METAPHOR_CHECK_SLOTS="$suite_outer_slots"
    # shellcheck source=pool.sh
    source "$ROOT/scripts/check/pool.sh" && pool_default_jobs
  )
}

export GIT_CONFIG_GLOBAL="$ROOT/scripts/check/suite.gitconfig" GIT_CONFIG_NOSYSTEM=1
unset GIT_CONFIG GIT_CONFIG_PARAMETERS GIT_CONFIG_COUNT GIT_CONFIG_SYSTEM \
  GIT_AUTHOR_NAME GIT_AUTHOR_EMAIL GIT_AUTHOR_DATE GIT_COMMITTER_NAME GIT_COMMITTER_EMAIL GIT_COMMITTER_DATE \
  PII_PATTERNS REQUIRE_PRIVATE_PATTERNS REQUIRE_DOCS_TOOLS SECURITY_REVIEW_SKIP SKIP_SCRIPT_TESTS \
  GITHUB_ACTIONS GITHUB_STEP_SUMMARY \
  METAPHOR_SCAN_ALL METAPHOR_FULL_TESTS METAPHOR_CHECK_SLOTS METAPHOR_CHECK_LOCK METAPHOR_CHECK_POLL \
  METAPHOR_CHECK_NOW METAPHOR_CHECK_PS_CMD METAPHOR_PESTER_BATCH METAPHOR_GAME_EXE METAPHOR_STATUS_URL \
  METAPHOR_CHECK_HELD METAPHOR_TEST_JOBS
# $BASHPID: a live run's own number, so two suites running at once never share a folder; a dead run's leftover slot
# is taken over (scripts/check/run.sh).
export METAPHOR_CHECK_LOCK="${TMPDIR:-/tmp}/metaphor-suite-$BASHPID.lock"
