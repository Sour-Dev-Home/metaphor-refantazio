#!/usr/bin/env bash
# Black-box tests for scripts/check.sh: each test plants content in a temporary git repo, runs the check there and
# asserts the exit code and the message.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=../../scripts/check/wsl-run.sh
source "$ROOT/scripts/check/wsl-run.sh" # in Git Bash, runs this suite in WSL instead when it's set up (#341)
# shellcheck source=../../scripts/check/suite-env.sh
source "$ROOT/scripts/check/suite-env.sh" # the contributor's git config and shell don't reach it (#455)
CHECK="$ROOT/scripts/check.sh"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# The check's functions: most cases run a stage with `main` in a subshell of this shell, which saves starting a bash and
# reading the script each time (#328; Git Bash and real-time antivirus make every process start expensive).
SOURCED_CHECK="$CHECK"
# The folders holding this clone's docs tools at this checkout's pins (scripts/setup/install-docs-tools.sh, #353), so the docs
# cases' repos run the real tools. Under WSL, lychee and Vale come from the distro's PATH instead.
DOCS_TOOLS_PATH="$(cd "$ROOT" && bash scripts/setup/install-docs-tools.sh --path)" || DOCS_TOOLS_PATH=""
# shellcheck source=../../scripts/check.sh
source "$CHECK" || exit 2
# shellcheck source=../../scripts/check/live-log.sh
source "$ROOT/scripts/check/live-log.sh" || exit 2
# shellcheck source=../../scripts/check/pool.sh
source "$ROOT/scripts/check/pool.sh" || exit 2

passed=0
failed=0

# One initialised repo, copied for each case: one cp, where a `git init` and four `git config` calls were five starts.
TEMPLATE="$WORK/template"
git init -q "$TEMPLATE" || exit 2
git -C "$TEMPLATE" config user.name test
git -C "$TEMPLATE" config user.email test@example.invalid
git -C "$TEMPLATE" config core.autocrlf false
git -C "$TEMPLATE" config commit.gpgsign false

# new_repo: create an empty git repo and cd into it.
new_repo() {
  local dir="$WORK/repo-$RANDOM-$RANDOM"
  cp -r "$TEMPLATE" "$dir" && cd "$dir" || exit 2
}

# parent_dir PATH: create PATH's folder, when it has one, without a dirname start.
parent_dir() { [[ "$1" != */* ]] || mkdir -p "${1%/*}"; }

# check_stage ARGS...: run the check: `main` in this subshell, or a new bash when CHECK names another copy of it. Call it
# inside $(...), since main exits.
check_stage() {
  if [ "$CHECK" = "$SOURCED_CHECK" ]; then
    main "$@"
  else
    bash "$CHECK" "$@"
  fi
}

# run_check ARGS...: run the check, capturing its exit code and combined output.
run_check() {
  out="$(check_stage "$@" 2>&1)"
  code=$?
}

# expect_absent TEXT...: the last run's output contains none of TEXT.
expect_absent() {
  local text ok=1
  for text in "$@"; do
    [[ "$out" == *"$text"* ]] && ok=0
  done
  if [ "$ok" -eq 1 ]; then
    passed=$((passed + 1))
  else
    failed=$((failed + 1))
    printf 'FAIL %s\n  want none of: %s\n  got output:\n%s\n' "$current" "$*" "$(sed 's/^/    /' <<<"$out")"
  fi
}

# expect CODE [TEXT...]: the last run exited with CODE and its output contains every TEXT.
expect() {
  local want="$1" ok=1 text
  shift
  [ "$code" -eq "$want" ] || ok=0
  for text in "$@"; do
    [[ "$out" == *"$text"* ]] || ok=0
  done
  if [ "$ok" -eq 1 ]; then
    passed=$((passed + 1))
  else
    failed=$((failed + 1))
    printf 'FAIL %s\n  want exit %s and text: %s\n  got exit %s, output:\n%s\n' \
      "$current" "$want" "$*" "$code" "$(sed 's/^/    /' <<<"$out")"
  fi
}

# A hook in a worktree gets GIT_DIR and GIT_INDEX_FILE for the worktree's git dir. Inherited by the Pester suites, they
# made a `git -C <scratch> init` set core.bare=true in the shared config, breaking every checkout. A step (timed_step)
# runs without them; here the hook's repository and its worktree are scratch ones.
test_steps_run_without_the_hooks_git_environment() {
  new_repo
  local repo="$PWD" wt="$WORK/wt-$RANDOM-$RANDOM" scratch="$WORK/scratch-$RANDOM-$RANDOM"
  git commit -q --allow-empty -m init && git worktree add -q "$wt" && mkdir "$scratch" || exit 2
  out="$(
    export GIT_DIR="$repo/.git/worktrees/${wt##*/}" GIT_INDEX_FILE="$repo/.git/worktrees/${wt##*/}/index"
    live_log="" step_times=()
    timed_step init git -C "$scratch" init -q 2>&1
    echo "core.bare=$(git --git-dir="$repo/.git" config --get core.bare)"
    [ -d "$scratch/.git" ] && echo "scratch initialised"
  )"
  code=$?
  expect 0 "core.bare=false" "scratch initialised"
}

# #321: an absolute core.hooksPath makes every worktree run one checkout's hooks, not its own branch's.
test_rules_warn_about_an_absolute_hooks_path() {
  local path
  for path in /c/clone/scripts/githooks 'C:\clone\scripts\githooks' C:/clone/scripts/githooks; do
    new_repo
    git config core.hooksPath "$path"
    run_check rules
    expect 0 "core.hooksPath is an absolute path" "git config core.hooksPath scripts/githooks"
  done
  git config core.hooksPath scripts/githooks
  run_check rules
  expect_absent "core.hooksPath"
}

test_blocks_staged_cpk() {
  new_repo
  printf 'not really an archive' > data.cpk
  git add data.cpk
  run_check fast
  expect 1 "game content" "data.cpk"
}

test_blocks_every_game_extension_even_uppercase() {
  local ext
  for ext in pak exe DLL asi dmp mdmp gpr gzf gbf idb i64 til nam id0 id1 id2 bndb dd64 mp4 MKV webm mov avi m4v flv vtt srt; do
    new_repo
    printf 'x' > "file.$ext"
    git add -f "file.$ext"
    run_check fast
    expect 1 "game content" "file.$ext"
  done
}

test_names_build_output_for_our_own_dll() {
  new_repo
  printf 'x' > Mod.dll
  git add -f Mod.dll
  run_check fast
  expect 1 "Mod.dll" "game content or build output" "executable extension"
}

test_passes_ordinary_source() {
  new_repo
  printf 'class Mod {}\n' > Mod.cs
  git add Mod.cs
  run_check fast
  expect 0
}

test_fast_stage_prints_its_time_against_the_stage_1_budget() {
  new_repo
  run_check fast
  expect 0 "timing: stage 1 (fast) started" "(budget 10 s)"
}

test_failing_fast_stage_still_prints_its_time_and_fails() {
  new_repo
  printf 'x' > data.cpk
  git add data.cpk
  run_check fast
  expect 1 "game content" "timing: stage 1 (fast)"
}

test_untimed_stage_prints_no_time() {
  new_repo
  run_check rules
  expect 0
  expect_absent "timing:"
}

test_blocks_file_in_ignored_game_folder_added_with_force() {
  new_repo
  printf 'game/\n' > .gitignore
  mkdir -p game/sub
  printf 'harmless-looking text\n' > game/sub/notes.txt
  git add .gitignore
  git add -f game/sub/notes.txt
  run_check fast
  expect 1 "game content" "game/sub/notes.txt" "game folder"
}

test_blocks_file_inside_ghidra_project_folder() {
  new_repo
  mkdir -p work/Metaphor.rep/idata
  printf 'x' > work/Metaphor.rep/idata/00.prp
  git add -f work/Metaphor.rep/idata/00.prp
  run_check fast
  expect 1 "game content" "Metaphor.rep/idata/00.prp" "disassembler"
}

test_blocks_query_output_in_the_ghidra_folder() {
  new_repo
  mkdir -p out/ghidra/out
  printf 'void FUN_14021e1c0(void) {}\n' > out/ghidra/out/decompiled.txt
  git add -f out/ghidra/out/decompiled.txt
  run_check fast
  expect 1 "game content" "out/ghidra/out/decompiled.txt" "game folder"
}

test_blocks_files_in_the_root_ghidra_folder() {
  new_repo
  mkdir -p ghidra/projects
  printf 'void FUN_14021e1c0(void) {}\n' > ghidra/projects/notes.txt
  git add -f ghidra/projects/notes.txt
  run_check fast
  expect 1 "game content" "ghidra/projects/notes.txt" "game folder"
}

test_blocks_large_binary_with_harmless_name() {
  new_repo
  head -c $((2 * 1024 * 1024)) /dev/urandom > texture.png
  git add texture.png
  run_check fast
  expect 1 "game content" "texture.png" "larger than"
}

test_blocks_small_file_whose_hash_is_listed() {
  new_repo
  mkdir -p scripts/ci
  # SHA-256 of the three bytes "abc" (FIPS 180-2 test vector).
  printf '# known game files\nBA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD  some game file\n' \
    > scripts/ci/game-content.sha256
  printf 'abc' > readme-snippet.txt
  git add scripts/ci/game-content.sha256 readme-snippet.txt
  run_check fast
  expect 1 "game content" "readme-snippet.txt" "hash"
}

test_rules_stage_blocks_committed_content_that_skipped_the_hook() {
  new_repo
  mkdir -p extracted
  printf 'x' > extracted/model.bin
  git add -f extracted/model.bin
  git commit -q -m "sneak in" --no-verify
  run_check rules
  expect 1 "game content" "extracted/model.bin"
}

test_rules_stage_passes_clean_repo() {
  new_repo
  printf '# notes\n' > notes.md
  git add notes.md
  git commit -q -m "notes" --no-verify
  run_check rules
  expect 0
}

test_full_stage_fails_closed_without_a_solution() {
  new_repo
  printf '# notes\n' > notes.md
  git add notes.md
  git commit -q -m "notes" --no-verify
  run_check full
  expect 2 "Metaphor.sln"
}

test_signatures_stage_fails_closed_without_a_game_exe() {
  new_repo
  out="$(unset METAPHOR_GAME_EXE; check_stage signatures 2>&1)"
  code=$?
  expect 2 "METAPHOR_GAME_EXE"
}

test_signatures_stage_fails_closed_when_the_game_exe_is_missing() {
  new_repo
  METAPHOR_GAME_EXE="$WORK/no-such-game.exe" run_check signatures
  expect 2 "no file at"
}

test_rejects_unknown_stage() {
  new_repo
  run_check fastest
  expect 2 "usage"
}

test_fails_closed_outside_a_git_repo() {
  mkdir -p "$WORK/not-a-repo" && cd "$WORK/not-a-repo" || exit 2
  run_check rules
  expect 2 "could not list files"
}

test_matches_hash_only_line_with_crlf_ending() {
  new_repo
  mkdir -p scripts/ci
  printf 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\r\n' > scripts/ci/game-content.sha256
  printf 'abc' > snippet.txt
  git add scripts/ci/game-content.sha256 snippet.txt
  run_check fast
  expect 1 "game content" "snippet.txt" "hash"
}

test_passes_nested_source_folder_named_game() {
  new_repo
  mkdir -p src/Mod/Game
  printf 'class GameState {}\n' > src/Mod/Game/GameState.cs
  git add src/Mod/Game/GameState.cs
  run_check fast
  expect 0
}

test_blocks_root_game_folder_in_any_case() {
  new_repo
  mkdir -p Game
  printf 'x' > Game/data.txt
  git add Game/data.txt
  run_check fast
  expect 1 "game content" "Game/data.txt" "game folder"
}

test_skips_submodule_entries() {
  new_repo
  git update-index --add --cacheinfo 160000,1111111111111111111111111111111111111111,vendor/lib
  run_check fast
  expect 0
  run_check rules
  expect 0
}

test_judges_staged_content_not_working_tree() {
  new_repo
  head -c $((2 * 1024 * 1024)) /dev/urandom > big.bin
  git add big.bin
  printf 'small now' > big.bin
  run_check fast
  expect 1 "big.bin" "larger than"
}

test_blocks_private_pattern_with_trailing_backslash() {
  new_repo
  mkdir -p .claude
  printf '# private\nD:\\Games\\Secret\\\n' > .claude/pii-patterns.txt
  printf 'see D:\\Games\\Secret\\save for details\n' > notes.md
  git add notes.md
  run_check fast
  expect 1 "personal information" "notes.md:1" "private pattern"
}

test_warns_when_private_patterns_are_missing() {
  new_repo
  printf 'hello\n' > notes.md
  git add notes.md
  run_check fast
  expect 0 "warning" "no private patterns" ".claude/pii-patterns.txt"
}

test_reads_private_patterns_from_environment() {
  new_repo
  printf 'contact jane-q-public for access\n' > notes.md
  git add notes.md
  PII_PATTERNS=$'# from a secret\r\njane-q-public\r\n' run_check fast
  expect 1 "personal information" "notes.md:1" "private pattern"
}

test_fails_when_required_private_patterns_are_missing() {
  new_repo
  printf 'hello\n' > notes.md
  git add notes.md
  REQUIRE_PRIVATE_PATTERNS=1 run_check fast
  expect 2 "no private patterns"
}

# Planted personal information is assembled at run time, so this file never holds it and passes its own scan.
users="Use""rs"
home="ho""me"
at="@"

test_blocks_windows_user_path() {
  new_repo
  printf 'ok\nbuilt in C:\\%s\\alice\\src\n' "$users" > build.md
  git add build.md
  run_check fast
  expect 1 "personal information" "build.md:2" "local user path"
}

test_blocks_unix_home_and_mac_user_paths() {
  new_repo
  printf 'cd /%s/alice/mods\n' "$home" > a.sh
  printf 'open /%s/alice/Desktop\n' "$users" > b.sh
  git add a.sh b.sh
  run_check fast
  expect 1 "a.sh:1" "b.sh:1" "local user path"
}

test_blocks_git_bash_and_wsl_user_paths() {
  new_repo
  printf 'cd /c/%s/alice/mods\n' "$users" > a.sh
  printf 'cd /mnt/c/%s/alice/mods\n' "$users" > b.sh
  git add a.sh b.sh
  run_check fast
  expect 1 "a.sh:1" "b.sh:1" "local user path"
}

test_passes_ssh_remotes_and_retina_image_names() {
  new_repo
  printf 'clone git%sgithub.com:owner/repo.git\nuse icon%s2x.png\n' "$at" "$at" > notes.md
  git add notes.md
  run_check fast
  expect 0
}

test_trims_whitespace_around_private_patterns() {
  new_repo
  mkdir -p .claude
  printf '  jane-q-public  \n' > .claude/pii-patterns.txt
  printf 'signed, jane-q-public.\n' > notes.md
  git add notes.md
  run_check fast
  expect 1 "notes.md:1" "private pattern"
}

test_blocks_email_address() {
  new_repo
  printf 'mail alice.smith%sgmail.com\n' "$at" > notes.md
  git add notes.md
  run_check fast
  expect 1 "personal information" "notes.md:1" "email address"
}

test_passes_email_in_third_party_licence_notice() {
  new_repo
  mkdir -p src/P/licenses/x
  printf 'Copyright (C) 2020 Alice Smith <alice.smith%sgmail.com>\n' "$at" > src/P/licenses/x/LICENSE.MIT
  git add src/P/licenses/x/LICENSE.MIT
  run_check fast
  expect 0
}

# Outside src/<project>/licenses/<library>/, or a name that only starts like a notice, the email rule still applies.
test_blocks_email_beside_a_licence_notice_or_elsewhere() {
  new_repo
  mkdir -p src/P/licenses/x docs/x licenses/x src/P/q/licenses/x
  for file in src/P/licenses/x/README.md src/P/licenses/x/NOTICEBOARD.md src/P/licenses/x/LICENSE.a.b \
    docs/x/LICENSE licenses/x/LICENSE src/P/q/licenses/x/LICENSE; do
    printf 'mail alice.smith%sgmail.com\n' "$at" > "$file"
    git add "$file"
  done
  run_check fast
  expect 1 "src/P/licenses/x/README.md:1: email address" "src/P/licenses/x/NOTICEBOARD.md:1: email address" \
    "src/P/licenses/x/LICENSE.a.b:1: email address" "docs/x/LICENSE:1: email address" \
    "licenses/x/LICENSE:1: email address" "src/P/q/licenses/x/LICENSE:1: email address"
}

test_blocks_private_pattern_in_licence_notice() {
  new_repo
  mkdir -p .claude src/P/licenses/x
  printf 'jane-q-public\n' > .claude/pii-patterns.txt
  printf 'Copyright (C) 2020 jane-q-public\n' > src/P/licenses/x/LICENSE.MIT
  git add src/P/licenses/x/LICENSE.MIT
  run_check fast
  expect 1 "src/P/licenses/x/LICENSE.MIT:1: matches a private pattern"
}

test_blocks_user_path_in_licence_notice() {
  new_repo
  mkdir -p src/P/licenses/x
  printf 'built in C:\\%s\\jane\n' "$users" > src/P/licenses/x/NOTICE
  git add src/P/licenses/x/NOTICE
  run_check fast
  expect 1 "src/P/licenses/x/NOTICE:1: local user path"
}

test_passes_placeholders_and_example_addresses() {
  new_repo
  printf 'no paths like C:\\%s\\... or C:/%s/ here\n' "$users" "$users" > a.md
  printf 'write to you%sexample.com, bot%sexample.invalid or 1+me%susers.noreply.github.com\n' "$at" "$at" "$at" > b.md
  printf 'see https://api.github.com/users/octocat\n' > c.md
  git add a.md b.md c.md
  run_check fast
  expect 0
}

test_blocks_private_pattern_in_file_name() {
  new_repo
  mkdir -p .claude
  printf 'jane-q-public\n' > .claude/pii-patterns.txt
  printf 'nothing personal\n' > Jane-Q-Public-notes.md
  git add Jane-Q-Public-notes.md
  run_check fast
  expect 1 "personal information" "Jane-Q-Public-notes.md: file name matches a private pattern"
}

test_ignores_blank_lines_in_private_patterns() {
  new_repo
  mkdir -p .claude
  printf 'jane-q-public\n\n   \n' > .claude/pii-patterns.txt
  printf 'nothing personal\n' > notes.md
  git add notes.md
  run_check fast
  expect 0
}

# raw_source PATH [skip-source|skip-saved|skip-build]: stage a raw source with the three header lines the existing
# sources use, leaving out one when asked.
raw_source() {
  local path="$1" skip="${2:-}"
  parent_dir "$path"
  {
    printf '# A saved page\n\n'
    [ "$skip" = skip-source ] || printf -- '- **Source:** <https://example.org/page>\n'
    [ "$skip" = skip-saved ] || printf -- '- **Saved:** 2026-11-13.\n'
    [ "$skip" = skip-build ] || printf -- '- **Game build:** none; this is about tooling.\n'
    printf '\n## Findings\n\n- Something.\n'
  } > "$path"
  git add "$path"
}

test_passes_raw_source_with_full_header() {
  new_repo
  raw_source docs/raw-sources/page-2026-11-13.md
  run_check fast
  expect 0
}

test_blocks_raw_source_without_source() {
  new_repo
  raw_source docs/raw-sources/page-2026-11-13.md skip-source
  run_check fast
  expect 1 "raw source: docs/raw-sources/page-2026-11-13.md: header has no Source line"
  expect_absent "no Saved line" "no Game build line"
}

test_blocks_raw_source_without_saved_date() {
  new_repo
  raw_source docs/raw-sources/page-2026-11-13.md skip-saved
  run_check fast
  expect 1 "raw source: docs/raw-sources/page-2026-11-13.md: header has no Saved line"
  expect_absent "no Source line" "no Game build line"
}

test_blocks_raw_source_with_saved_line_but_no_date() {
  new_repo
  mkdir -p docs/raw-sources
  printf '# Page\n\n- **Source:** a tool\n- **Saved:** last week\n- **Game build:** none\n' > docs/raw-sources/p.md
  git add docs/raw-sources/p.md
  run_check fast
  expect 1 "raw source: docs/raw-sources/p.md: header has no Saved line"
}

test_passes_raw_source_with_reworded_labels() {
  new_repo
  mkdir -p docs/raw-sources
  printf '# Page\n\n- **Source:** a tool\n- **Date saved:** 2026-11-13\n- **Game build(s) the sources mention:** PC\n' \
    > docs/raw-sources/p.md
  git add docs/raw-sources/p.md
  run_check fast
  expect 0
}

test_blocks_raw_source_without_game_build() {
  new_repo
  raw_source docs/raw-sources/page-2026-11-13.md skip-build
  run_check fast
  expect 1 "raw source: docs/raw-sources/page-2026-11-13.md: header has no Game build line"
  expect_absent "no Source line" "no Saved line"
}

test_ignores_header_lines_below_the_first_section() {
  new_repo
  mkdir -p docs/raw-sources
  printf '# Page\n\n- **Source:** a tool\n- **Saved:** 2026-11-13\n\n## Notes\n\n- **Game build:** none\n' \
    > docs/raw-sources/p.md
  git add docs/raw-sources/p.md
  run_check fast
  expect 1 "raw source: docs/raw-sources/p.md: header has no Game build line"
}

test_passes_raw_sources_readme_without_header() {
  new_repo
  mkdir -p docs/raw-sources
  printf '# Raw sources\n\nSaved copies.\n' > docs/raw-sources/README.md
  git add docs/raw-sources/README.md
  run_check fast
  expect 0
}

# finding PATH BUILD-LINE: stage a finding note whose header holds BUILD-LINE (empty for none).
finding() {
  parent_dir "$1"
  printf '# Something about a format\n\n%s\n\n## Evidence\n\n- A capture.\n' "$2" > "$1"
  git add "$1"
}

build_sha="$(printf 'a%.0s' {1..64})"

test_passes_finding_naming_a_game_build_by_sha() {
  new_repo
  finding docs/findings/format.md "- **Game build:** $build_sha (Steam build 123)"
  run_check fast
  expect 0
}

test_passes_header_values_in_code_quotes() {
  new_repo
  note docs/raw-sources/p.md '- **Source:** `gh api`' '- **Saved:** 2026-11-13' "- **Game build:** \`$build_sha\` (SHA-256)"
  finding docs/findings/format.md "- **Game build:** \`$build_sha\`"
  run_check fast
  expect 0
}

test_blocks_finding_without_game_build() {
  new_repo
  finding docs/findings/format.md ""
  run_check fast
  expect 1 "finding: docs/findings/format.md: header has no Game build line"
}

test_blocks_finding_whose_game_build_is_not_a_sha() {
  new_repo
  finding docs/findings/format.md "- **Game build:** latest"
  run_check fast
  expect 1 "finding: docs/findings/format.md: header has no Game build line"
}

# note PATH LINES...: stage a note whose header is the given lines, followed by one section.
note() {
  local path="$1"
  shift
  parent_dir "$path"
  { printf '# A note\n\n'; printf '%s\n' "$@"; printf '\n## Section\n\n- Text.\n'; } > "$path"
  git add "$path"
}

test_blocks_raw_source_whose_values_are_blank_or_junk() {
  new_repo
  note docs/raw-sources/p.md $'- **Source:**\t' '- **Saved:** 2026-11-13x' '- **Game build:** -'
  run_check fast
  expect 1 "header has no Source line" "header has no Saved line with a YYYY-MM-DD date" "header has no Game build line"
}

test_passes_raw_source_with_colon_outside_bold_and_plural_label() {
  new_repo
  note docs/raw-sources/p.md '- **Sources**: a tool' '- **Saved**: 2026-11-13' '- **Game build**: none'
  run_check fast
  expect 0
}

test_blocks_non_markdown_files_in_note_folders() {
  new_repo
  mkdir -p docs/raw-sources docs/findings
  printf 'capture\n' > docs/raw-sources/capture.txt
  printf 'note\n' > docs/findings/NOTE.MD
  git add docs
  run_check fast
  expect 1 "raw source: docs/raw-sources/capture.txt: not a .md file" "finding: docs/findings/NOTE.MD: not a .md file"
}

test_passes_findings_readme_without_game_build() {
  new_repo
  note docs/findings/README.md '- **Owner:** nobody'
  run_check fast
  expect 0
}

# message_check TEXT: run the commit-msg stage on a message file holding TEXT (printf escapes allowed). Not
# check_message, which is the check's own function.
message_check() {
  local file="$WORK/message-$RANDOM"
  printf "$1" > "$file"
  run_check commit-msg "$file"
}

test_passes_typed_subject_with_body_and_trailers() {
  new_repo
  message_check 'feat(check): block bad commit messages\n\nWhy it matters, in two lines\nat most five.\n\nCo-Authored-By: Someone <someone@example.com>\nSigned-off-by: Someone <someone@example.com>\n'
  expect 0
}

test_blocks_subject_without_type_prefix() {
  new_repo
  message_check 'Add a thing\n'
  expect 1 "commit message: subject needs a type prefix"
}

# The hooks and CI run check.sh as a script, never sourced. Its main runs only when executed, so if that guard broke,
# every stage would exit 0 without checking anything. These run it as a new bash, as the hooks do.
test_script_mode_fails_a_bad_message() {
  new_repo
  local file="$WORK/message-$RANDOM"
  printf 'Add a thing\n' > "$file"
  out="$(bash "$CHECK" commit-msg "$file" 2>&1)"
  code=$?
  expect 1 "commit message: subject needs a type prefix"
}

test_script_mode_passes_a_good_message() {
  new_repo
  local file="$WORK/message-$RANDOM"
  printf 'feat: a good subject\n' > "$file"
  out="$(bash "$CHECK" commit-msg "$file" 2>&1)"
  code=$?
  expect 0
}

test_blocks_subject_over_72_characters() {
  new_repo
  message_check "feat: $(printf 'x%.0s' {1..67})\n" # 73 characters
  expect 1 "commit message: subject is 73 characters; the limit is 72"
}

test_counts_subject_length_in_characters_not_bytes() {
  new_repo
  message_check "docs: $(printf 'é%.0s' {1..66})\n" # 72 characters, 138 bytes
  expect 0
}

test_blocks_body_over_five_lines() {
  new_repo
  message_check 'fix: a bug\n\none\ntwo\n\nthree\nfour\nfive\nsix\n\nCo-Authored-By: Someone <someone@example.com>\n'
  expect 1 "commit message: body has 6 lines; the limit is 5"
}

test_blocks_body_without_blank_line_after_subject() {
  new_repo
  message_check 'fix: a bug\nno blank line\n'
  expect 1 "commit message: leave a blank line after the subject"
}

test_ignores_comment_lines_and_text_below_the_scissors() {
  new_repo
  message_check 'fix: a bug\n\nWhy.\n# Please enter the commit message\n# a\n# b\n# c\n# d\n# ------------------------ >8 ------------------------\ndiff --git a/x b/x\n1\n2\n3\n4\n5\n6\n'
  expect 0
}

test_passes_git_generated_subjects() {
  local subject
  for subject in 'Revert "feat: a thing"' 'fixup! feat: a thing' 'squash! fix: b' 'amend! docs: c'; do
    new_repo
    message_check "$subject\n"
    expect 0
  done
}

test_passes_subject_followed_only_by_trailers() {
  new_repo
  message_check 'feat: pair work\n\nCo-Authored-By: a <a@example.com>\nCo-Authored-By: b <b@example.com>\nCo-Authored-By: c <c@example.com>\nCo-Authored-By: d <d@example.com>\nCo-Authored-By: e <e@example.com>\nSigned-off-by: f <f@example.com>\n'
  expect 0
}

test_blocks_empty_message() {
  new_repo
  message_check '# only a comment\n\n'
  expect 1 "commit message: message is empty"
  expect_absent "type prefix"
}

test_ignores_trailing_spaces_and_crlf_line_endings() {
  new_repo
  message_check "fix: $(printf 'x%.0s' {1..67})   \r\n\r\nWhy.\r\n" # 72 characters before the spaces
  expect 0
}

test_commits_stage_names_each_bad_commit_in_the_range() {
  local base bad
  new_repo
  git commit -q --allow-empty -m 'not typed, but before the range'
  base="$(git rev-parse HEAD)"
  git commit -q --allow-empty -m 'feat: a good one'
  git commit -q --allow-empty -m 'a bad one'
  bad="$(git rev-parse --short HEAD)"
  run_check commits "$base..HEAD"
  expect 1 "commit $bad: subject needs a type prefix"
  expect_absent "before the range" "good one"
}

test_commits_stage_passes_a_clean_range_and_skips_merges() {
  local base
  new_repo
  git commit -q --allow-empty -m 'chore: start'
  base="$(git rev-parse HEAD)"
  git switch -q -c side
  git commit -q --allow-empty -m 'feat: on a branch'
  git switch -q -
  git merge -q --no-ff --no-edit side # "Merge branch 'side'" has no type prefix
  run_check commits "$base..HEAD"
  expect 0
}

test_commits_stage_fails_closed_on_a_bad_range() {
  new_repo
  git commit -q --allow-empty -m 'chore: start'
  run_check commits "nope..HEAD"
  expect 2 "could not list the commits in nope..HEAD"
}

# #455: with log.showSignature=true, git log prints a signed commit's verification ahead of --format's output, where
# the stage read the short hash; every commit then failed the message check.
test_commits_stage_reads_signed_commits_under_log_show_signature() {
  local base signer
  new_repo
  signer="$(pwd -W 2> /dev/null || pwd)/sign.sh"
  printf '%s\n' '#!/bin/sh' 'case "$*" in' \
    "  *--verify*) echo '[GNUPG:] GOODSIG 0 t'; echo 'gpg: Good signature' >&2 ;;" \
    "  *) cat > /dev/null; printf '%s\\n' '-----BEGIN PGP SIGNATURE-----' 'eA==' '-----END PGP SIGNATURE-----'" \
    "     echo '[GNUPG:] SIG_CREATED ' >&2 ;;" 'esac' > sign.sh && chmod +x sign.sh || exit 2
  git commit -q --allow-empty -m 'chore: start'
  base="$(git rev-parse HEAD)"
  git config gpg.program "$signer" && git config log.showSignature true
  git commit -q -S --allow-empty -m 'feat: a signed one'
  if [[ "$(git log -1 --format=%h)" == *"Good signature"* ]]; then
    run_check commits "$base..HEAD"
  else
    out="the commit wasn't signed, or git log showed no signature" code=9
  fi
  expect 0
}

# docs_repo: a new repo holding the real docs-check configs, with the installed docs tools on PATH.
docs_repo() {
  new_repo
  mkdir -p tools/docs
  cp "$ROOT/tools/docs/.vale.ini" "$ROOT/tools/docs/.markdownlint-cli2.jsonc" tools/docs/
  cp -r "$ROOT/tools/docs/.vale" tools/docs/
  PATH="$DOCS_TOOLS_PATH:$PATH"
}

test_docs_stage_fails_on_a_broken_relative_link() {
  docs_repo
  printf '# Notes\n\nSee [the guide](missing.md).\n' > notes.md
  git add .
  run_check docs
  expect 1 "broken link" "notes.md"
}

test_docs_stage_fails_on_a_missing_anchor() {
  docs_repo
  printf '# Other\n\n## Real part\n' > other.md
  printf '# Notes\n\nSee [it](other.md#no-such-part).\n' > notes.md
  git add .
  run_check docs
  expect 1 "broken link" "notes.md:3" "other.md#no-such-part"
}

# This repository's own GitHub links are checked against the working tree, still offline (#436).
own_repo=https://github.com/Sour-Dev-Home/metaphor-accessibility

test_docs_stage_fails_on_an_own_absolute_link_to_a_missing_path() {
  docs_repo
  printf '# Notes\n\nSee [the guide](%s/blob/main/docs/missing.md).\n' "$own_repo" > notes.md
  git add .
  run_check docs
  expect 1 "broken link: notes.md:3: $own_repo/blob/main/docs/missing.md"
}

test_docs_stage_fails_on_an_own_absolute_link_to_a_missing_anchor_or_folder() {
  docs_repo
  mkdir -p docs
  printf '# Other\n\n## Real part\n' > docs/other.md
  printf '# Notes\n\n[a](%s/blob/main/docs/other.md#no-such-part)\n[b](%s/tree/main/gone)\n' "$own_repo" "$own_repo" > notes.md
  git add .
  run_check docs
  expect 1 "notes.md:3: $own_repo/blob/main/docs/other.md#no-such-part" "notes.md:4: $own_repo/tree/main/gone"
}

test_docs_stage_passes_own_absolute_links_to_existing_paths() {
  docs_repo
  mkdir -p docs
  printf '# Other\n\n## Real part\n' > docs/other.md
  printf '# Notes\n\n[a](%s/blob/main/docs/other.md#real-part)\n[b](%s/tree/main/docs)\n[c](%s/issues/1)\n' \
    "$own_repo" "$own_repo" "$own_repo" > notes.md
  git add .
  run_check docs
  expect 0
  expect_absent "broken link"
}

test_docs_stage_checks_own_absolute_links_from_a_path_with_a_space_and_an_accent() {
  docs_repo
  local dir="$WORK/clone with spacé-$RANDOM"
  mkdir -p "$dir" && cp -r . "$dir/repo" && cd "$dir/repo" || exit 2
  mkdir -p docs
  printf '# Other\n' > docs/other.md
  printf '# Notes\n\n[a](%s/blob/main/docs/other.md)\n' "$own_repo" > notes.md
  git add .
  run_check docs
  expect 0
  printf '[b](%s/blob/main/docs/missing.md)\n' "$own_repo" >> notes.md
  git add .
  run_check docs
  expect 1 "broken link: notes.md:4: $own_repo/blob/main/docs/missing.md"
  expect_absent "notes.md:3"
}

test_docs_stage_fails_on_a_relative_link_under_dot_github() {
  docs_repo
  mkdir -p .github
  printf '# Other\n' > other.md
  printf '# Contributing\n\nSee [it](../other.md), [here](#contributing) and [there](%s/blob/main/other.md).\n' \
    "$own_repo" > .github/CONTRIBUTING.md
  git add .
  run_check docs
  expect 1 "relative link: .github/CONTRIBUTING.md:3: ../other.md"
  expect_absent "#contributing" "there"
}

test_docs_stage_warns_on_a_filler_word() {
  docs_repo
  printf '# Notes\n\nThis is basically how the loader works.\n' > notes.md
  git add .
  run_check docs
  expect 0 "warning: notes.md:3" "basically"
}

test_docs_stage_warns_on_a_markdown_lint_finding() {
  docs_repo
  printf '# Notes\n\n\n\nToo many blank lines above.\n' > notes.md
  git add .
  run_check docs
  expect 0 "warning: notes.md:3" "MD012"
}

# markdownlint-cli2 loads a config from each linted file's folder, even with --config, and a .cjs or .mjs one runs code
# (issue #322), so the stage fails before running it.
test_docs_stage_fails_on_a_markdownlint_config_outside_tools_docs() {
  docs_repo
  mkdir -p docs
  printf '# Notes\n\nPlain words.\n' > docs/notes.md
  printf 'require("fs").writeFileSync("ran", "");\nmodule.exports = {};\n' > docs/.markdownlint-cli2.cjs
  git add .
  run_check docs
  expect 1 "docs/.markdownlint-cli2.cjs" "tools/docs/"
  out="the config ran" code=0
  [ ! -e ran ] || code=1
  expect 0
}

# Windows reads config names and folders in any case (issue #324): a .MarkdownLint config is blocked too, and so is one
# in Tools/Docs/, which Windows merges with tools/docs/ but the review scope doesn't cover.
test_docs_stage_fails_on_a_markdownlint_config_in_any_case() {
  docs_repo
  mkdir -p docs
  printf '# Notes\n\nPlain words.\n' > docs/notes.md
  printf 'module.exports = {};\n' > docs/.MarkdownLint-cli2.cjs
  git add .
  # Staged without a file: on Windows, Tools/Docs/ on disk is tools/docs/.
  local blob
  blob="$(git hash-object -w docs/.MarkdownLint-cli2.cjs)"
  git update-index --add --cacheinfo "100644,$blob,Tools/Docs/.markdownlint.cjs"
  run_check docs
  expect 1 "docs/.MarkdownLint-cli2.cjs" "Tools/Docs/.markdownlint.cjs"
}

# words N: print N words of plain prose.
words() { local i; for ((i = 0; i < $1; i++)); do printf 'word '; done; echo; }

test_docs_stage_warns_on_an_adr_over_250_words() {
  docs_repo
  mkdir -p docs/adr
  { echo '# Long decision'; echo; words 260; } > docs/adr/0009-long.md
  git add .
  run_check docs
  expect 0 "warning: docs/adr/0009-long.md: 260 words" "250"
}

test_docs_stage_ignores_title_and_code_in_adr_word_count() {
  docs_repo
  mkdir -p docs/adr
  { echo '# A title of several words here'; echo; words 240; echo; echo '```text'; words 50; echo '```'; } \
    > docs/adr/0009-short.md
  git add .
  run_check docs
  expect 0
  expect_absent "0009-short.md: "
}

test_pr_body_stage_warns_over_300_words() {
  docs_repo
  { echo '## Summary'; words 310; } > "$WORK/body.md"
  run_check pr-body "$WORK/body.md"
  expect 0 "warning: PR description: 310 words" "300"
}

test_pr_body_stage_leaves_out_the_evidence_section() {
  docs_repo
  { echo '## Summary'; words 250; echo '## Evidence'; words 200; echo '## Merge Danger'; words 20; } > "$WORK/body.md"
  run_check pr-body "$WORK/body.md"
  expect 0
  expect_absent "warning"
}

test_pr_body_stage_warns_on_a_filler_word() {
  docs_repo
  printf '## Summary\n\nThis basically adds a check.\n' > "$WORK/body.md"
  run_check pr-body "$WORK/body.md"
  expect 0 "warning: pr-description.md:3" "basically"
}

test_pr_body_stage_fails_closed_without_a_file() {
  new_repo
  run_check pr-body "$WORK/no-such-body.md"
  expect 2 "pr-body stage needs"
}

# run_check_without_docs_tools ARGS...: run_check with a PATH that has git and the shell's basics but no docs tools.
run_check_without_docs_tools() {
  local git
  git="$(command -v git)"
  out="$(PATH="${git%/*}:/usr/bin:/bin" && check_stage "$@" 2>&1)"
  code=$?
}

test_docs_stage_skips_missing_tools_with_a_warning() {
  new_repo
  printf '# Notes\n' > notes.md
  git add .
  run_check_without_docs_tools docs
  expect 0 "warning: lychee is not installed" "warning: vale is not installed"
}

test_docs_stage_fails_closed_on_a_missing_tool_in_ci() {
  new_repo
  printf '# Notes\n' > notes.md
  git add .
  REQUIRE_DOCS_TOOLS=1 run_check_without_docs_tools docs
  expect 2 "lychee is not installed, but REQUIRE_DOCS_TOOLS=1"
}

# The docs tools live once per clone, under its common git dir, in one folder per pin (#353): every worktree finds them,
# and a branch pinning another version or hash gets its own folder rather than another branch's binary.
test_docs_tools_live_in_the_clone_keyed_by_pin() {
  local main_path tree
  new_repo
  git commit -q --allow-empty -m "chore: start"
  main_path="$(bash "$ROOT/scripts/setup/install-docs-tools.sh" --path 2>&1)"
  tree="$WORK/tree-$RANDOM-$RANDOM"
  git worktree add -q "$tree" 2> /dev/null || { out="git worktree add failed" code=9 && expect 0; return; }
  out="$(cd "$tree" && bash "$ROOT/scripts/setup/install-docs-tools.sh" --path 2>&1)"
  code=$?
  local platform=WINDOWS vale_sha lychee_sha
  [ "$(uname -s)" != Linux ] || platform=LINUX
  vale_sha="$(sed -n "s/^VALE_SHA256_$platform=//p" "$ROOT/scripts/setup/install-docs-tools.sh")"
  lychee_sha="$(sed -n "s/^LYCHEE_SHA256_$platform=//p" "$ROOT/scripts/setup/install-docs-tools.sh")"
  [ "$out" = "$main_path" ] || code=1 out="worktree: $out; main checkout: $main_path"
  expect 0 "$PWD/.git/docs-tools/lychee-" "$lychee_sha" ":$PWD/.git/docs-tools/vale-" "$vale_sha" \
    ":$PWD/.git/docs-tools/markdownlint-"
}

test_docs_tools_with_another_pin_get_another_folder() {
  local before after
  new_repo
  mkdir -p scripts/setup tools/docs
  cp "$ROOT/scripts/setup/install-docs-tools.sh" scripts/setup/
  cp "$ROOT/tools/docs/package.json" "$ROOT/tools/docs/package-lock.json" tools/docs/
  before="$(bash scripts/setup/install-docs-tools.sh --path)"
  sed -i 's/^\(VALE_SHA256_[A-Z]*=\).*/\1'"$(printf '0%.0s' {1..64})"'/' scripts/setup/install-docs-tools.sh
  printf '\n' >> tools/docs/package-lock.json
  after="$(bash scripts/setup/install-docs-tools.sh --path)"
  code=0 out="before: $before; after: $after"
  [ "${before%%:*}" = "${after%%:*}" ] || code=1 # lychee's pin didn't change
  IFS=: read -r _ b_vale b_lint <<<"$before"
  IFS=: read -r _ a_vale a_lint <<<"$after"
  [ "$b_vale" != "$a_vale" ] || code=2
  [ "$b_lint" != "$a_lint" ] || code=3
  expect 0
}

# docs_tools_installed: fake a finished install of every docs tool in the current clone, marker and tool file each, and
# set lint_dir to markdownlint's folder and lychee_bin to the stand-in lychee. lychee and Vale are stand-ins that run,
# since the installer starts each one (#385).
docs_tools_installed() {
  local dir exe="" lychee vale
  [ "$(uname -s)" = Linux ] || exe=.exe
  IFS=: read -r lychee vale lint_dir <<<"$(bash "$ROOT/scripts/setup/install-docs-tools.sh" --path)"
  lint_dir="${lint_dir%/node_modules/.bin}"
  mkdir -p "$lint_dir/node_modules/.bin" && : > "$lint_dir/node_modules/.bin/markdownlint-cli2"
  lychee_bin="$lychee/lychee$exe"
  mkdir -p "$lychee" "$vale" && printf '#!/bin/sh\necho stand-in\n' | tee "$lychee_bin" > "$vale/vale$exe"
  chmod +x "$lychee_bin" "$vale/vale$exe"
  for dir in "$lychee" "$vale" "$lint_dir"; do echo "${dir##*/}" > "$dir/.installed"; done
}

# docs_tools_stubs: a folder with a curl and an npm that fail loudly, or, with npm=ok, an npm that installs a stand-in
# markdownlint-cli2 into its --prefix folder. Prints the folder.
docs_tools_stubs() {
  local stubs="$WORK/stubs-$RANDOM-$RANDOM"
  mkdir -p "$stubs"
  printf '#!/bin/sh\necho "$0 must not run" >&2\nexit 7\n' | tee "$stubs/curl" > "$stubs/npm"
  [ "${1:-}" != npm=ok ] ||
    printf '#!/bin/sh\nmkdir -p "$3/node_modules/.bin" && : > "$3/node_modules/.bin/markdownlint-cli2"\n' > "$stubs/npm"
  chmod +x "$stubs/curl" "$stubs/npm"
  echo "$stubs"
}

# A re-run, or a full in another worktree, finds the clone's install and fetches nothing.
test_docs_tools_install_skips_a_pin_already_in_the_clone() {
  local lint_dir
  new_repo
  docs_tools_installed
  out="$(PATH="$(docs_tools_stubs):$PATH" bash "$ROOT/scripts/setup/install-docs-tools.sh" 2>&1)"
  code=$?
  [[ "$out" != *"must not run"* ]] || code=8
  expect 0
}

# An installed lychee that can't start, as the Windows build without the Visual C++ runtime: the install fails and
# names it, on the already-installed path too (#385).
test_docs_tools_install_fails_when_a_tool_does_not_run() {
  local lint_dir lychee_bin
  new_repo
  docs_tools_installed
  printf '#!/bin/sh\nexit 127\n' > "$lychee_bin"
  out="$(PATH="$(docs_tools_stubs):$PATH" bash "$ROOT/scripts/setup/install-docs-tools.sh" 2>&1)"
  code=$?
  [[ "$out" != *"must not run"* ]] || code=8
  [[ "$out" != *"vale is installed"* ]] || code=9
  expect 1 "lychee is installed in"
}

# A folder with the pin's name but no marker, such as an empty one, is not an install: it gets replaced (#353 review).
test_docs_tools_install_replaces_an_incomplete_folder() {
  local lint_dir
  new_repo
  docs_tools_installed
  rm -rf "$lint_dir" && mkdir -p "$lint_dir"
  out="$(PATH="$(docs_tools_stubs npm=ok):$PATH" bash "$ROOT/scripts/setup/install-docs-tools.sh" 2>&1)"
  code=$?
  [ -f "$lint_dir/node_modules/.bin/markdownlint-cli2" ] && [ "$(cat "$lint_dir/.installed")" = "${lint_dir##*/}" ] ||
    code=8 out="$out; not reinstalled: $(ls -A "$lint_dir")"
  expect 0
}

# A killed install leaves its .partial-* folder: the next install removes one over an hour old, not a running one's.
test_docs_tools_install_removes_old_partial_folders() {
  local lint_dir shared
  new_repo
  docs_tools_installed
  shared="${lint_dir%/*}"
  mkdir -p "$shared/.partial-old" "$shared/.partial-new"
  touch -d '2 hours ago' "$shared/.partial-old"
  out="$(PATH="$(docs_tools_stubs):$PATH" bash "$ROOT/scripts/setup/install-docs-tools.sh" 2>&1)"
  code=$?
  [ ! -e "$shared/.partial-old" ] && [ -d "$shared/.partial-new" ] || code=8 out="$out; left: $(ls -A "$shared")"
  expect 0
}

test_docs_stage_finds_the_tools_installed_for_the_clone() {
  local tree vale_dir
  new_repo
  mkdir -p tools/docs
  cp "$ROOT/tools/docs/.vale.ini" tools/docs/
  printf '# Notes\n' > notes.md
  git add . && git commit -q -m "docs: notes"
  IFS=: read -r _ vale_dir _ <<<"$(bash "$ROOT/scripts/setup/install-docs-tools.sh" --path)"
  mkdir -p "$vale_dir"
  printf '#!/bin/sh\necho "notes.md:1:1:Project.Stub:the clone'"'"'s vale ran"\n' > "$vale_dir/vale"
  chmod +x "$vale_dir/vale"
  tree="$WORK/tree-$RANDOM-$RANDOM"
  git worktree add -q "$tree" 2> /dev/null && cd "$tree" || { out="git worktree add failed" code=9 && expect 0; return; }
  run_check_without_docs_tools docs
  expect 0 "notes.md:1: the clone's vale ran" "lychee is not installed"
}

# #365: docs_stage works from the repository root but leaves the caller's root and folder as they were, so a step run
# after it in the same run reads the paths it expects.
test_docs_stage_leaves_the_callers_root_and_folder_alone() {
  docs_repo
  mkdir sub
  printf '# Notes\n' > notes.md
  git add .
  cd sub || exit 2
  out="$(root=caller-root failures=0 warnings=0 && docs_stage > /dev/null 2>&1 && echo "after: root=$root dir=${PWD##*/}")"
  code=$?
  expect 0 "after: root=caller-root dir=sub"
}

test_docs_stage_passes_good_links_and_skips_raw_sources() {
  docs_repo
  mkdir -p docs/raw-sources
  printf '# Other\n\n## Real part\n' > other.md
  printf '# Notes\n\nSee [it](other.md#real-part) and [here](#notes).\n' > notes.md
  printf '# Saved page\n\n[Their link](/wiki/elsewhere.md)\n' > docs/raw-sources/page.md
  git add .
  run_check docs
  expect 0
  expect_absent "broken link"
}

# pre-commit: the repo rules, then each extra check only when its file types are staged (#78).

test_pre_commit_still_runs_the_repo_rules() {
  new_repo
  printf 'not really an archive' > data.cpk
  git add data.cpk
  run_check pre-commit
  expect 1 "game content" "data.cpk"
}

test_pre_commit_runs_the_docs_checks_when_markdown_is_staged() {
  docs_repo
  printf '# Notes\n\nSee [it](missing.md).\n' > notes.md
  git add notes.md
  run_check pre-commit
  expect 1 "broken link" "notes.md"
}

test_pre_commit_blames_only_the_check_that_failed() {
  docs_repo
  printf 'not really an archive' > data.cpk
  printf '# Notes\n\nFine.\n' > notes.md
  git add .
  run_check pre-commit
  expect 1 "game content"
  expect_absent "docs: see the problems above"
}

test_pre_commit_skips_the_docs_checks_when_no_markdown_is_staged() {
  docs_repo
  printf '# Notes\n\nSee [it](missing.md).\n' > notes.md
  git add notes.md && git commit -q -m "docs: notes"
  printf 'plain\n' > other.txt
  git add other.txt
  run_check pre-commit
  expect 0
  expect_absent "broken link"
}

test_pre_commit_says_how_to_check_changed_signatures_without_the_game() {
  new_repo
  mkdir -p src/Metaphor.Core
  printf '[]\n' > src/Metaphor.Core/hook-signatures.json
  git add .
  out="$(unset METAPHOR_GAME_EXE; check_stage pre-commit 2>&1)"
  code=$?
  expect 0 "hook signatures changed" "METAPHOR_GAME_EXE"
}

# pre-push: build and tests only when the pushed commits change code. Git passes the refs on stdin.

ZERO=0000000000000000000000000000000000000000

# push_repo FILE: a repo with a first commit, then a second adding FILE. Sets BASE and HEAD to the two commits.
push_repo() {
  new_repo
  printf 'first\n' > readme.txt
  git add . && git commit -q -m "docs: first"
  BASE="$(git rev-parse HEAD)"
  parent_dir "$1"
  printf 'second\n' > "$1"
  git add . && git commit -q -m "feat: second"
  HEAD="$(git rev-parse HEAD)"
}

# run_pre_push LOCAL REMOTE: run the pre-push stage for one ref, as git would.
run_pre_push() {
  # A private lock path and slot count, whatever the environment exports, so the machine's real build slots never
  # decide these runs (#321). hold_lock sets $test_slots.
  out="$(printf 'refs/heads/topic %s refs/heads/topic %s\n' "$1" "$2" |
    METAPHOR_CHECK_LOCK="$PWD/test-build.lock" METAPHOR_CHECK_SLOTS="${test_slots:-2}" check_stage pre-push 2>&1)"
  code=$?
}

test_pre_push_skips_the_build_when_no_code_changed() {
  push_repo docs/notes.md
  run_pre_push "$HEAD" "$BASE"
  expect 0 "no code changes"
}

test_pre_push_builds_when_code_changed() {
  push_repo src/Menu.cs
  run_pre_push "$HEAD" "$BASE"
  expect 2 "pre-push stage needs Metaphor.sln"
}

test_pre_push_runs_pester_when_a_script_changed() {
  # The 2026-11-19 push of scripts/setup/onboard.ps1 skipped everything as "no code changes" (#413).
  push_repo scripts/setup/onboard.ps1
  run_pre_push "$HEAD" "$BASE"
  expect 2 "pre-push stage needs Metaphor.sln"
  expect_absent "no code changes"
  push_repo tests/pester/a.Tests.ps1
  run_pre_push "$HEAD" "$BASE"
  expect 2 "pre-push stage needs Metaphor.sln"
}

test_pre_push_builds_when_it_cant_tell_what_changed() {
  # A new branch with no origin/main to compare against.
  push_repo docs/notes.md
  run_pre_push "$HEAD" "$ZERO"
  expect 2 "pre-push stage needs Metaphor.sln"
}

test_pre_push_skips_a_deleted_branch() {
  push_repo src/Menu.cs
  run_pre_push "$ZERO" "$HEAD"
  expect 0
  expect_absent "Metaphor.sln"
}

# #456: a script change selects the Pester tests; without pwsh the pre-push run says it skipped them, as CI won't.
test_pre_push_warns_when_pwsh_is_missing_for_the_pester_tests() {
  out="$(unset GITHUB_ACTIONS METAPHOR_FULL_TESTS SKIP_SCRIPT_TESTS
    PATH="$WORK/no-tools" run_selected_pester "pester scripts/dev/session.ps1 changed" 2>&1)"
  code=$?
  expect 0 "warning: pwsh isn't on PATH, so the Pester tests didn't run" "PowerShell 7"
}

# hold_lock: every build slot (one) is held by a live process, so a build stage would have to wait.
hold_lock() {
  test_slots=1
  mkdir "$PWD/test-build.lock"
  echo "$$" > "$PWD/test-build.lock/pid"
}

test_pre_push_stops_at_once_when_the_lock_is_held_and_no_pass_is_recorded() {
  push_repo src/Menu.cs
  hold_lock
  run_pre_push "$HEAD" "$BASE"
  unset test_slots
  expect 2 "every build slot is busy" "bash scripts/check.sh full"
}

# #321: an exported real lock path or slot count never reaches the pre-push tests, which use their own.
test_pre_push_tests_ignore_the_machine_lock() {
  push_repo src/Menu.cs
  local real="$PWD/machine.lock"
  mkdir "$real" && echo "$$" > "$real/pid"
  METAPHOR_CHECK_LOCK="$real" METAPHOR_CHECK_SLOTS=1 run_pre_push "$HEAD" "$BASE"
  expect 2 "pre-push stage needs Metaphor.sln"
  expect_absent "every build slot is busy"
}

test_pre_push_reuses_a_recorded_pass_without_taking_the_lock() {
  push_repo src/Menu.cs
  hold_lock
  echo "full $(git rev-parse "$HEAD^{tree}")" > "$(git rev-parse --absolute-git-dir)/check-pass-cache"
  run_pre_push "$HEAD" "$BASE"
  unset test_slots
  expect 0 "reused the recorded pass"
}

# The must-run security review: real commits through hooks that run the check, as scripts/githooks does.

STAMP="$ROOT/scripts/ci/security-stamp.sh"
HOOKS="$WORK/hooks"
mkdir -p "$HOOKS"
printf '#!/usr/bin/env bash\nexec bash "%s" pre-commit\n' "$CHECK" > "$HOOKS/pre-commit"
printf '#!/usr/bin/env bash\nexec bash "%s" commit-msg "$1"\n' "$CHECK" > "$HOOKS/commit-msg"
chmod +x "$HOOKS/pre-commit" "$HOOKS/commit-msg"
# The stamp-spending hooks, each only where the repository has it, so a missing one fails the tests that need it.
for hook in post-commit post-merge; do
  if [ -f "$ROOT/scripts/githooks/$hook" ]; then
    printf '#!/usr/bin/env bash\nexec bash "%s" spend\n' "$STAMP" > "$HOOKS/$hook" && chmod +x "$HOOKS/$hook"
  fi
done
# A merge that commits itself runs pre-merge-commit, not pre-commit (#321); likewise only where the repository has it.
if [ -f "$ROOT/scripts/githooks/pre-merge-commit" ]; then
  printf '#!/usr/bin/env bash\nexec bash "%s" pre-merge-commit\n' "$CHECK" > "$HOOKS/pre-merge-commit"
  chmod +x "$HOOKS/pre-merge-commit"
fi

# review_repo: a repo using the hooks, with a first commit, then a staged change to scope file FILE (default: the hook
# layer).
review_repo() {
  new_repo
  git config core.hooksPath "$HOOKS"
  printf 'first\n' > readme.txt
  git add readme.txt && git commit -q --no-verify -m "docs: first"
  stage_file "${1:-src/Metaphor.Hooks/Hooks/Speech.ps1}"
}

# stage_file FILE [TEXT]: write TEXT (default: a random line) to FILE and stage it.
stage_file() {
  parent_dir "$1"
  printf '%s\n' "${2:-line $RANDOM}" > "$1"
  git add "$1"
}

# commit SUBJECT: commit through the hooks, capturing the exit code and output.
commit() {
  out="$(git commit -q -m "$1" 2>&1)"
  code=$?
}

# expect_message TEXT...: the last commit's message contains every TEXT.
expect_message() {
  out="$(git log -1 --format=%B)"
  code=0
  expect 0 "$@"
}

test_review_refuses_a_scope_commit_without_a_stamp() {
  review_repo
  commit "feat: unreviewed hook"
  expect 1 "security review: the staged hook or deploy code has no matching stamp" "security-reviewer"
}

test_review_passes_a_stamped_scope_commit_and_adds_its_trailer() {
  local hash
  review_repo scripts/dev/session.ps1
  bash "$STAMP" > /dev/null
  hash="$(bash "$STAMP" hash)"
  commit "feat: reviewed session script"
  expect 0
  expect_message "Security-Review: $hash"
  [ "$(bash "$STAMP" hash HEAD)" = "$hash" ] || out="the committed scope hashes differently: $hash" code=1
  expect 0
}

test_review_refuses_an_edit_after_stamping() {
  review_repo
  bash "$STAMP" > /dev/null
  stage_file src/Metaphor.Hooks/Hooks/Speech.ps1 "changed after the review"
  commit "feat: hook changed after review"
  expect 1 "no matching stamp"
}

# expect_stamp STATE: the stamp file is present or absent.
expect_stamp() {
  out="stamp should be $1" code=0
  [ -f "$(git rev-parse --git-path security-review-stamp)" ] && [ "$1" = absent ] && code=1
  [ -f "$(git rev-parse --git-path security-review-stamp)" ] || [ "$1" = absent ] || code=1
  expect 0
}

# A stamp is single-use: the post-commit hook spends it on the commit carrying its trailer (#246).
test_stamp_is_spent_by_the_commit_it_reviewed() {
  review_repo docs/notes.txt
  bash "$STAMP" > /dev/null
  commit "docs: unscoped first" # carries no trailer, so the stamp stays
  expect 0
  expect_stamp present
  stage_file "$SPEECH"
  bash "$STAMP" > /dev/null
  commit "feat: reviewed hook"
  expect 0
  expect_stamp absent
  git reset -q --soft HEAD~1 # the same scope again needs a fresh review
  commit "feat: reviewed hook"
  expect 1 "no matching stamp"
}

# #321: --no-verify skips commit-msg, so the commit carries no trailer, but post-commit still runs: a commit that
# records the stamped scope spends the stamp, so a later merge can't pass as stamped on it.
test_stamp_is_spent_by_a_scope_commit_without_its_trailer() {
  review_repo
  bash "$STAMP" > /dev/null
  git commit -q --no-verify -m "feat: hook, hooks bypassed"
  expect_stamp absent
}

# git merge commits on its own and runs post-merge, not post-commit.
test_stamp_is_spent_by_a_merge_that_commits_itself() {
  merge_repo "$SPEECH" "main${LINES#1}" "$SPEECH" "${LINES%9}branch" # both sides' hunks: needs a stamp
  git merge -q --no-commit origin/main > /dev/null 2>&1
  bash "$STAMP" > /dev/null # reviews the result the merge below reproduces
  git merge --abort
  merge_main
  expect 0
  expect_message "Security-Review: "
  expect_stamp absent
}

test_stamp_survives_a_fast_forward() {
  review_repo docs/notes.txt
  commit "docs: notes"
  git switch -q -c ahead
  stage_file docs/more.txt
  commit "docs: more"
  git switch -q -
  bash "$STAMP" > /dev/null
  out="$(git merge -q --ff-only ahead 2>&1)"
  code=$?
  expect 0
  expect_stamp present
}

test_stamp_survives_a_commit_refused_at_its_message() {
  review_repo
  bash "$STAMP" > /dev/null
  commit "not typed"
  expect 1 "subject needs a type prefix"
  expect_stamp present
  commit "feat: reviewed hook"
  expect 0
}

test_review_override_passes_and_records_its_reason() {
  review_repo
  out="$(SECURITY_REVIEW_SKIP="reviewer down, checked by hand" git commit -q -m "fix: urgent hook fix" 2>&1)"
  code=$?
  expect 0 "security review: skipped (reviewer down, checked by hand)"
  expect_message "Security-Review-Skipped: reviewer down, checked by hand"
  expect_absent "Security-Review: "
}

test_review_refuses_a_blank_override() {
  review_repo
  out="$(SECURITY_REVIEW_SKIP="  " git commit -q -m "fix: blank reason" 2>&1)"
  code=$?
  expect 1 "no matching stamp"
}

test_review_leaves_commits_outside_the_scope_alone() {
  review_repo src/Metaphor.Hooks.Tests/SpeechTests.ps1 # a sibling folder, not the hook layer
  stage_file tools/check-other.sh # all of scripts/ is in scope (#410), tools/ only in part
  commit "test: outside the scope"
  expect 0
  expect_message "test: outside the scope"
  expect_absent "Security-Review"
}

test_review_replaces_a_hand_written_trailer() {
  review_repo
  bash "$STAMP" > /dev/null
  commit $'feat: reviewed hook\n\nSecurity-Review-Skipped: written by hand'
  expect 0
  expect_message "Security-Review: $(bash "$STAMP" hash HEAD)"
  expect_absent "written by hand"
  # The hook layer's folder counts: the hash differs from the first commit's, which has no scope files.
  [ "$(bash "$STAMP" hash HEAD)" != "$(bash "$STAMP" hash HEAD~1)" ] || out="the hook layer wasn't hashed" code=1
  expect 0
}

test_review_message_keeps_its_shape_with_the_trailer() {
  review_repo
  bash "$STAMP" > /dev/null
  commit $'feat: reviewed hook\n\nWhy.\n\nCo-Authored-By: Someone <someone@example.com>'
  expect 0
  run_check commits HEAD~1..HEAD
  expect 0
}

test_commits_stage_fails_a_scope_commit_without_a_review_trailer() {
  local base
  review_repo
  base="$(git rev-parse HEAD)"
  git commit -q --no-verify -m "feat: skipped the hooks"
  run_check commits "$base..HEAD"
  expect 1 "commit $(git rev-parse --short HEAD): touches the security-review scope without a Security-Review trailer"
}

test_commits_stage_fails_a_review_trailer_for_other_content() {
  local base
  review_repo
  base="$(git rev-parse HEAD)"
  git commit -q --no-verify -m $'feat: stale trailer\n\nSecurity-Review: 0000000000000000000000000000000000000000000000000000000000000000'
  run_check commits "$base..HEAD"
  expect 1 "touches the security-review scope without a Security-Review trailer"
}

test_commits_stage_passes_reviewed_skipped_and_unscoped_commits() {
  local base
  review_repo
  base="$(git rev-parse HEAD)"
  bash "$STAMP" > /dev/null
  commit "feat: reviewed"
  stage_file scripts/dev/session.ps1
  SECURITY_REVIEW_SKIP="by hand" git commit -q -m "fix: skipped" > /dev/null 2>&1
  stage_file docs/notes.txt
  commit "docs: unscoped"
  run_check commits "$base..HEAD"
  expect 0
  [ "$(git rev-list --count "$base..HEAD")" -eq 3 ] || out="expected 3 commits" code=1
  expect 0
}

# Clean merges from main (#246, #174; docs/adr/0012-review-trailer-covers-clean-merges.md).
SPEECH=src/Metaphor.Hooks/Hooks/Speech.ps1
LINES=$'1\n2\n3\n4\n5\n6\n7\n8\n9'

# merge_repo [MAIN_FILE MAIN_TEXT [BRANCH_FILE BRANCH_TEXT]]: a repo using the hooks, where main (origin/main, $main)
# wrote MAIN_TEXT to MAIN_FILE (default: a new hook file) and the checked-out branch, parted from it at the first commit,
# committed a reviewed BRANCH_TEXT to BRANCH_FILE (default: the speech hook, which both start with as $LINES).
merge_repo() {
  new_repo
  git config core.hooksPath "$HOOKS"
  stage_file readme.txt first
  stage_file "$SPEECH" "$LINES"
  git commit -q --no-verify -m "docs: first"
  git switch -q -c mainline
  stage_file "${1:-src/Metaphor.Hooks/Hooks/Menu.ps1}" "${2:-menu from main}"
  git commit -q --no-verify -m "feat: on main" -m "Security-Review-Skipped: reviewed on main"
  main="$(git rev-parse HEAD)"
  git update-ref refs/remotes/origin/main "$main"
  git switch -q -c feature HEAD~1
  stage_file "${3:-$SPEECH}" "${4:-speech from the branch}"
  bash "$STAMP" > /dev/null
  commit "feat: reviewed speech change" # spends the stamp, so the merges below are judged without it
  expect 0
}

# merge_main: merge origin/main through the hooks, capturing the exit code and output.
merge_main() {
  out="$(git merge -q --no-ff -m "chore: merge main" "${1:-origin/main}" 2>&1)"
  code=$?
}

# #321: git merge commits on its own without running pre-commit, so the pre-merge-commit hook runs the same checks.
test_a_merge_that_commits_itself_runs_the_pre_commit_checks() {
  merge_repo data.cpk "not really an archive"
  merge_main
  expect 1 "game content" "data.cpk"
}

test_review_passes_a_clean_merge_from_main_and_says_why() {
  merge_repo
  merge_main
  expect 0 "security review: not needed, a clean merge from main"
  expect_message "chore: merge main"
  expect_absent "Security-Review"
  run_check commits "$main..HEAD"
  expect 0
}

test_review_passes_a_merge_whose_only_conflict_is_outside_the_scope() {
  merge_repo
  git switch -q mainline
  stage_file readme.txt "main's readme"
  git commit -q --no-verify -m "docs: readme on main"
  main="$(git rev-parse HEAD)"
  git update-ref refs/remotes/origin/main "$main"
  git switch -q feature
  stage_file readme.txt "the branch's readme"
  commit "docs: readme on the branch"
  git merge -q origin/main > /dev/null 2>&1
  stage_file readme.txt "both readmes"
  commit "chore: merge main" # through the pre-commit hook too
  expect 0 "clean merge from main"
  run_check commits "$main..HEAD" # past the unscoped readme commit, to the reviewed one
  expect 0
}

test_review_refuses_a_merge_resolving_a_scope_conflict() {
  merge_repo "$SPEECH" "speech from main"
  git merge -q origin/main > /dev/null 2>&1
  stage_file "$SPEECH" "both, resolved by hand"
  commit "chore: merge main"
  expect 1 "no matching stamp"
  git commit -q --no-verify -m "chore: merge main"
  run_check commits "$main..HEAD"
  expect 1 "commit $(git rev-parse --short HEAD): touches the security-review scope without a Security-Review trailer"
}

test_review_passes_a_stamped_scope_conflict_resolution() {
  merge_repo "$SPEECH" "speech from main"
  git merge -q origin/main > /dev/null 2>&1
  stage_file "$SPEECH" "both, resolved by hand"
  bash "$STAMP" > /dev/null
  commit "chore: merge main"
  expect 0
  expect_message "Security-Review: $(bash "$STAMP" hash HEAD)"
  run_check commits "$main..HEAD"
  expect 0
}

# Git merges the two sides' hunks without a conflict, but the result is neither parent's file: a new state to review.
test_review_refuses_a_merge_that_combines_both_sides_of_a_scope_file() {
  merge_repo "$SPEECH" "main${LINES#1}" "$SPEECH" "${LINES%9}branch"
  merge_main
  expect 1 "no matching stamp"
}

test_review_refuses_a_clean_merge_of_a_branch_not_on_main() {
  merge_repo
  git update-ref refs/remotes/origin/main HEAD~1 # main hasn't taken the mainline branch
  merge_main mainline
  expect 1 "no matching stamp"
  git merge --abort
  git update-ref -d refs/remotes/origin/main # no main to compare with at all
  merge_main mainline
  expect 1 "no matching stamp"
}

# The stamp the branch's commit spent can't let a later `-s ours` merge through with its trailer.
test_review_refuses_an_ours_merge_after_a_stamped_commit() {
  merge_repo "$SPEECH" "speech fixed on main" src/Metaphor.Hooks/Hooks/Menu.ps1 "menu from the branch"
  expect_stamp absent
  out="$(git merge -q -s ours -m "chore: merge main" origin/main 2>&1)"
  code=$?
  expect 1 "no matching stamp"
}

# Keeping a side's version is clean only when the other side left the file as their merge base had it: an `-s ours`
# merge, or `checkout --ours` on a conflict, would otherwise drop a fix made on main without a review.
test_review_refuses_an_ours_merge_that_drops_a_scope_change_from_main() {
  merge_repo "$SPEECH" "speech fixed on main" src/Metaphor.Hooks/Hooks/Menu.ps1 "menu from the branch"
  out="$(git merge -q -s ours -m "chore: merge main" origin/main 2>&1)"
  code=$?
  expect 1 "no matching stamp"
  git commit -q --no-verify -m "chore: merge main" # the refused merge, committed past the hooks
  run_check commits "$main..HEAD"
  expect 1 "commit $(git rev-parse --short HEAD): touches the security-review scope without a Security-Review trailer"
}

test_review_refuses_a_scope_conflict_resolved_with_one_side() {
  merge_repo "$SPEECH" "speech fixed on main"
  git merge -q origin/main > /dev/null 2>&1
  git checkout -q --ours -- "$SPEECH" && git add "$SPEECH"
  commit "chore: merge main"
  expect 1 "no matching stamp"
  git merge --abort
  git merge -q origin/main > /dev/null 2>&1
  git checkout -q --theirs -- "$SPEECH" && git add "$SPEECH" # drops the branch's reviewed change instead
  commit "chore: merge main"
  expect 1 "no matching stamp"
}

test_commits_stage_fails_a_clean_merge_over_an_unreviewed_commit() {
  local unreviewed
  merge_repo
  stage_file "$SPEECH" "unreviewed"
  git commit -q --no-verify -m "feat: skipped the hooks"
  unreviewed="$(git rev-parse --short HEAD)"
  merge_main
  expect 0 "clean merge from main"
  run_check commits "$main..HEAD"
  expect 1 "commit $unreviewed: touches the security-review scope without a Security-Review trailer"
}

# CI judges only HEAD, so trailerless merges from before the rule don't fail a branch reviewed since.
test_commits_stage_ignores_old_trailerless_merges_below_a_reviewed_head() {
  merge_repo "$SPEECH" "speech from main"
  git merge -q origin/main > /dev/null 2>&1
  stage_file "$SPEECH" "resolved before the rule"
  git commit -q --no-verify -m "chore: merge main"
  stage_file "$SPEECH" "reviewed since"
  bash "$STAMP" > /dev/null
  commit "feat: reviewed on top"
  run_check commits "$main..HEAD"
  expect 0
  stage_file docs/notes.txt
  commit "docs: after the review"
  run_check commits "$main..HEAD"
  expect 0
}

# outside_repo [FILE]: a repo with one reviewed commit changing FILE (default: a scope file), ready for the commits
# stage; sets $base. A fake gh answers the timeline call with the lines in $TIMELINE (what the real call's --jq filter
# prints), a permission call with admin for the logins in $FAKE_ADMINS (default: maint), write for any other, and a pull
# request call with its head, $FAKE_HEAD (default: head1), on branch topic of fork/r, created at $FAKE_CREATED
# (default: 2026-11-16T08:00:00Z); it fails the call for the login
# in $FAKE_PERMISSION_FAIL. The review-gate runs call prints $RUNS (head, creation time and head repository, a tab
# between; run_gate's default: one run at head1 at 2026-11-16T09:00:00Z, so head1 arrived then).
# $FAKE_TIMELINE_FAIL fails the timeline call, $FAKE_HEAD_FAIL the pull request call, $FAKE_RUNS_FAIL the runs call,
# $FAKE_GH_FAIL every call.
FAKE_BIN="$WORK/fakebin"
mkdir -p "$FAKE_BIN"
cat > "$FAKE_BIN/gh" <<'EOF'
#!/usr/bin/env bash
if [ -n "${FAKE_GH_FAIL:-}" ]; then exit 1; fi
for arg; do
  case "$arg" in
    repos/*/collaborators/*/permission)
      login="${arg#*/collaborators/}" login="${login%/permission}"
      [ "$login" = "${FAKE_PERMISSION_FAIL:-}" ] && exit 1
      case " ${FAKE_ADMINS:-maint} " in *" $login "*) echo admin ;; *) echo write ;; esac
      exit ;;
    repos/*/pulls/*)
      [ -z "${FAKE_HEAD_FAIL:-}" ] || exit 1
      printf '%s\ttopic\tfork/r\t%s\n' "${FAKE_HEAD:-head1}" "${FAKE_CREATED:-2026-11-16T08:00:00Z}"; exit ;;
    repos/*/actions/workflows/review-gate.yml/runs)
      [ -z "${FAKE_RUNS_FAIL:-}" ] || exit 1
      printf '%b' "${RUNS:-}"; exit ;;
  esac
done
[ -z "${FAKE_TIMELINE_FAIL:-}" ] || exit 1
printf "%b" "${TIMELINE:-}"
EOF
chmod +x "$FAKE_BIN/gh"

outside_repo() {
  review_repo "${1:-src/Metaphor.Hooks/Hooks/Speech.ps1}"
  base="$(git rev-parse HEAD)" # review_repo staged the change; review and commit it here
  bash "$STAMP" > /dev/null
  git commit -q -m "feat: reviewed change" > /dev/null 2>&1 || out="could not commit the reviewed change" code=1
}

# pr_files: the PR's changed files from $base, one line each, the way the review-gate workflow lists them from the API:
# the path, then a tab and the old path for a rename. Counted from the merge base, as the API counts them.
pr_files() {
  git diff --name-status -M "$base...HEAD" | awk -F '\t' '$1 ~ /^R/ { print $3 "\t" $2; next } { print $2 }'
}

# run_gate ASSOCIATION: the review-label stage the way the review-gate workflow runs it, for a PR #7 by $PR_AUTHOR
# (default: contrib) updated at 2026-11-16T10:00:00Z, with head head1. $PR_FILES (default: pr_files) lists its files,
# $PR_CHANGED_FILES (default: their count) is the count the event gives; $PR_EVENT_ACTION and $PR_EVENT_LABEL name the
# event.
run_gate() {
  local files="$WORK/pr-files"
  if [ -n "${PR_FILES+set}" ]; then printf '%b' "$PR_FILES" > "$files"; else pr_files > "$files"; fi
  out="$(PATH="$FAKE_BIN:$PATH" TIMELINE="${TIMELINE:-}" FAKE_GH_FAIL="${FAKE_GH_FAIL:-}" FAKE_HEAD="${FAKE_HEAD:-}" \
    FAKE_HEAD_FAIL="${FAKE_HEAD_FAIL:-}" FAKE_RUNS_FAIL="${FAKE_RUNS_FAIL:-}" FAKE_CREATED="${FAKE_CREATED:-}" \
    RUNS="${RUNS-head1\t2026-11-16T09:00:00Z\tfork/r\n}" \
    FAKE_PERMISSION_FAIL="${FAKE_PERMISSION_FAIL:-}" FAKE_TIMELINE_FAIL="${FAKE_TIMELINE_FAIL:-}" \
    PR_AUTHOR_ASSOCIATION="$1" PR_AUTHOR="${PR_AUTHOR:-contrib}" PR_NUMBER=7 GITHUB_REPOSITORY=o/r \
    PR_UPDATED_AT=2026-11-16T10:00:00Z PR_HEAD_SHA=head1 PR_CHANGED_FILES="${PR_CHANGED_FILES:-$(grep -c '' "$files")}" \
    PR_EVENT_ACTION="${PR_EVENT_ACTION:-synchronize}" PR_EVENT_LABEL="${PR_EVENT_LABEL:-}" \
    check_stage review-label "$files" 2>&1)"
  code=$?
}

# run_pr ASSOCIATION: what CI asks of a PR: the commits stage in repo-rules, then the review-label stage in review-gate
# (run_gate). The output is both stages', the exit code the higher of the two.
run_pr() {
  local commits_out commits_code
  commits_out="$(PATH="$FAKE_BIN:$PATH" FAKE_GH_FAIL="${FAKE_GH_FAIL:-}" \
    FAKE_PERMISSION_FAIL="${FAKE_PERMISSION_FAIL:-}" \
    PR_AUTHOR_ASSOCIATION="$1" PR_AUTHOR="${PR_AUTHOR:-contrib}" GITHUB_REPOSITORY=o/r \
    check_stage commits "$base..HEAD" 2>&1)"
  commits_code=$?
  run_gate "$1"
  out="$commits_out"$'\n'"$out"
  [ "$commits_code" -le "$code" ] || code=$commits_code
}

# Issue #330: repo-rules runs the PR's own copy of the workflow, so the label moved to the review-gate workflow, which
# runs main's copy. The commits stage judges trailers only, and needs no timeline.
test_commits_stage_leaves_the_label_to_the_review_gate() {
  outside_repo
  out="$(PATH="$FAKE_BIN:$PATH" FAKE_TIMELINE_FAIL=1 PR_AUTHOR_ASSOCIATION=CONTRIBUTOR PR_AUTHOR=contrib \
    GITHUB_REPOSITORY=o/r check_stage commits "$base..HEAD" 2>&1)"
  code=$?
  expect 0
  expect_absent "security-reviewed"
}

test_review_gate_counts_a_rename_out_of_docs() {
  outside_repo docs/notes.txt
  TIMELINE=""
  PR_FILES='docs/notes.txt\tsrc/notes.txt\n' run_gate CONTRIBUTOR
  expect 1 "add the security-reviewed label"
  PR_FILES='docs/a.md\ndocs/b.md\tdocs/c.md\n' FAKE_GH_FAIL=1 run_gate CONTRIBUTOR
  expect 0
}

# The API lists at most 3000 files, so a list shorter than the event's count could hide a change outside docs/.
test_review_gate_fails_closed_on_a_short_file_list() {
  outside_repo docs/notes.txt
  PR_CHANGED_FILES=2 run_gate CONTRIBUTOR
  expect 2 "listed 1 of the PR's 2 changed files"
  PR_FILES='' PR_CHANGED_FILES=1 run_gate OWNER
  expect 2 "listed 0 of the PR's 1 changed files"
}

# A labeled event's updated_at is the label's own time, so the run the label starts uses GitHub's payload instead: the
# head when the label went on must still be the head.
test_review_gate_passes_on_the_label_event_while_the_head_is_unchanged() {
  outside_repo
  TIMELINE='push\t2026-11-16T09:00:00Z\nlabeled\t2026-11-16T09:30:00Z\tmaint\n' # before updated_at
  PR_EVENT_ACTION=labeled PR_EVENT_LABEL=security-reviewed run_gate CONTRIBUTOR
  expect 0
  PR_EVENT_ACTION=labeled PR_EVENT_LABEL=security-reviewed FAKE_HEAD=head2 run_gate CONTRIBUTOR
  expect 1 "pushed to after"
  TIMELINE='labeled\t2026-11-16T09:30:00Z\tmaint\npush\t2026-11-16T09:40:00Z\n' # a force push after the label
  PR_EVENT_ACTION=labeled PR_EVENT_LABEL=security-reviewed run_gate CONTRIBUTOR
  expect 1 "before the PR's latest push (2026-11-16T09:40:00Z)"
  TIMELINE='labeled\t2026-11-16T09:30:00Z\tcollab\n'
  PR_EVENT_ACTION=labeled PR_EVENT_LABEL=security-reviewed run_gate CONTRIBUTOR
  expect 1 "added by collab, who isn't an admin"
}

# #321: any other label event's updated_at is that event's own time, newer than the review label, so its run reads when
# the head arrived from the review-gate runs (server times, not commit dates) and keeps a reviewed PR green.
test_review_gate_judges_another_label_event_by_when_the_head_arrived() {
  local action
  outside_repo
  TIMELINE='push\t2026-11-16T09:00:00Z\nlabeled\t2026-11-16T09:30:00Z\tmaint\n'
  RUNS='head1\t2026-11-16T10:00:00Z\tfork/r\nhead1\t2026-11-16T09:30:05Z\tfork/r\nhead1\t2026-11-16T09:00:10Z\tfork/r\n'
  RUNS+='head0\t2026-11-16T08:00:00Z\tfork/r\n'
  for action in labeled unlabeled; do
    PR_EVENT_ACTION=$action PR_EVENT_LABEL=enhancement run_gate CONTRIBUTOR
    expect 0
  done
  # A push after the label whose commits are backdated: its run's time gives it away.
  TIMELINE='push\t2026-11-16T08:59:00Z\nlabeled\t2026-11-16T09:30:00Z\tmaint\n'
  RUNS='head1\t2026-11-16T10:00:00Z\tfork/r\nhead1\t2026-11-16T09:40:00Z\tfork/r\nhead0\t2026-11-16T09:30:05Z\tfork/r\n'
  RUNS+='head1\t2026-11-16T09:00:00Z\tfork/r\n' # head1 was the head before too: only the latest stretch counts
  PR_EVENT_ACTION=labeled PR_EVENT_LABEL=enhancement run_gate CONTRIBUTOR
  expect 1 "before the PR's latest push (2026-11-16T09:40:00Z)"
  # Runs on a same-named branch of another repository don't count.
  RUNS='head1\t2026-11-16T09:40:00Z\tfork/r\nhead1\t2026-11-16T09:00:00Z\tother/r\n'
  PR_EVENT_ACTION=labeled PR_EVENT_LABEL=enhancement run_gate CONTRIBUTOR
  expect 1 "before the PR's latest push (2026-11-16T09:40:00Z)"
  # The runs filter by branch, not PR: an older PR's run on a reused branch name can't date the head before this PR.
  RUNS='head1\t2026-11-16T08:00:00Z\tfork/r\n'
  PR_EVENT_ACTION=labeled PR_EVENT_LABEL=enhancement FAKE_CREATED=2026-11-16T09:45:00Z run_gate CONTRIBUTOR
  expect 1 "before the PR's latest push (2026-11-16T09:45:00Z)"
  # The head moved since the event: the push's own run judges it.
  RUNS='head1\t2026-11-16T09:00:00Z\tfork/r\n'
  PR_EVENT_ACTION=labeled PR_EVENT_LABEL=enhancement FAKE_HEAD=head2 run_gate CONTRIBUTOR
  expect 1 "pushed to after"
  # Removing the review label still fails.
  TIMELINE='push\t2026-11-16T09:00:00Z\nlabeled\t2026-11-16T09:30:00Z\tmaint\nunlabeled\t2026-11-16T09:50:00Z\tmaint\n'
  PR_EVENT_ACTION=unlabeled PR_EVENT_LABEL=security-reviewed run_gate CONTRIBUTOR
  expect 1 "add the security-reviewed label"
}

test_review_gate_fails_closed_when_the_runs_cannot_be_read() {
  outside_repo
  TIMELINE='push\t2026-11-16T09:00:00Z\nlabeled\t2026-11-16T09:30:00Z\tmaint\n'
  PR_EVENT_ACTION=labeled PR_EVENT_LABEL=enhancement FAKE_RUNS_FAIL=1 run_gate CONTRIBUTOR
  expect 2 "could not tell when the head of PR #7 arrived"
  RUNS='head0\t2026-11-16T09:00:00Z\tfork/r\n' # no run at the head
  PR_EVENT_ACTION=labeled PR_EVENT_LABEL=enhancement run_gate CONTRIBUTOR
  expect 2 "could not tell when the head of PR #7 arrived"
}

test_review_gate_fails_closed_when_the_head_cannot_be_read() {
  outside_repo
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tmaint\n'
  PR_EVENT_ACTION=labeled PR_EVENT_LABEL=security-reviewed FAKE_HEAD_FAIL=1 run_gate CONTRIBUTOR
  expect 2 "could not read the head of PR #7"
}

# Issue #309: every PR by someone other than the owner (or an admin) needs the label, collaborators and members too.
test_non_owner_pr_fails_without_the_label() {
  local assoc
  outside_repo
  TIMELINE=""
  for assoc in COLLABORATOR MEMBER CONTRIBUTOR NONE FIRST_TIME_CONTRIBUTOR; do
    run_pr "$assoc"
    expect 1 "add the security-reviewed label"
  done
}

test_non_owner_pr_outside_the_scope_still_needs_the_label() {
  outside_repo src/notes.txt
  TIMELINE=""
  run_pr COLLABORATOR
  expect 1 "add the security-reviewed label"
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tmaint\n'
  run_pr COLLABORATOR
  expect 0
}

test_non_owner_docs_only_pr_needs_no_label_or_timeline() {
  outside_repo docs/notes.txt
  TIMELINE=""
  FAKE_GH_FAIL=1 run_pr COLLABORATOR
  expect 0
  FAKE_GH_FAIL=1 run_pr CONTRIBUTOR
  expect 0
}

test_non_owner_pr_passes_with_a_maintainers_label_added_after_the_latest_push() {
  local assoc
  outside_repo
  TIMELINE='push\t2026-11-16T09:00:00Z\nlabeled\t2026-11-16T11:00:00Z\tmaint\n'
  for assoc in COLLABORATOR MEMBER CONTRIBUTOR; do
    run_pr "$assoc"
    expect 0
  done
}

test_non_owner_pr_fails_when_a_non_admin_added_the_label() {
  outside_repo
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tcollab\n'
  run_pr COLLABORATOR
  expect 1 "added by collab, who isn't an admin"
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tmaint\nunlabeled\t2026-11-16T11:10:00Z\tcollab\nlabeled\t2026-11-16T11:20:00Z\tcollab\n'
  run_pr COLLABORATOR
  expect 1 "added by collab, who isn't an admin"
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tbad/../login\n' # never reaches the API path
  run_pr COLLABORATOR
  expect 1 "added by bad/../login, who isn't an admin"
}

test_non_owner_pr_fails_closed_when_the_labellers_permission_cannot_be_read() {
  outside_repo
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tmaint\n'
  FAKE_PERMISSION_FAIL=maint run_pr COLLABORATOR
  expect 2 "could not read the permission of maint"
}

test_non_owner_scope_change_needs_the_trailer_and_the_label() {
  review_repo
  base="$(git rev-parse HEAD)"
  git commit -q --no-verify -m "feat: scope change, no review"
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tmaint\n'
  run_pr COLLABORATOR
  expect 1 "touches the security-review scope without a Security-Review trailer"
  run_pr CONTRIBUTOR
  expect 1 "touches the security-review scope without a Security-Review trailer"
}

# SECURITY_REVIEW_SKIP is the maintainer's, so a skip trailer only counts on the owner's PR.
test_non_owner_skip_trailer_does_not_count() {
  review_repo
  base="$(git rev-parse HEAD)"
  git commit -q --no-verify -m "feat: scope change" -m "Security-Review-Skipped: by hand"
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tmaint\n'
  run_pr COLLABORATOR
  expect 1 "touches the security-review scope without a Security-Review trailer"
  PR_AUTHOR=maint run_pr MEMBER
  expect 0
}

test_outside_pr_fails_when_the_label_predates_a_push() {
  outside_repo
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tmaint\npush\t2026-11-16T12:00:00Z\n'
  run_pr CONTRIBUTOR
  expect 1 "before the PR's latest push (2026-11-16T12:00:00Z)"
  TIMELINE='labeled\t2026-11-16T08:00:00Z\tmaint\n' # older than the event's own updated_at
  run_pr COLLABORATOR
  expect 1 "before the PR's latest push (2026-11-16T10:00:00Z)"
}

test_outside_pr_fails_when_the_label_was_removed_after_adding() {
  outside_repo
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tmaint\nunlabeled\t2026-11-16T11:30:00Z\tmaint\n'
  run_pr CONTRIBUTOR
  expect 1 "add the security-reviewed label"
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tmaint\nunlabeled\t2026-11-16T11:30:00Z\tmaint\nlabeled\t2026-11-16T12:00:00Z\tmaint\n'
  run_pr CONTRIBUTOR
  expect 0
}

test_outside_pr_fails_closed_when_the_timeline_cannot_be_read() {
  outside_repo
  FAKE_TIMELINE_FAIL=1 run_pr CONTRIBUTOR
  expect 2 "could not read the timeline of PR #7"
}

test_outside_pr_merge_commit_cannot_hide_a_scope_change() {
  review_repo docs/notes.txt
  git commit -q --no-verify -m "docs: notes"
  git checkout -q -b upstream HEAD~1
  stage_file readme.txt
  git commit -q --no-verify -m "docs: upstream"
  base="$(git rev-parse HEAD)"
  git checkout -q -
  git merge -q --no-ff --no-commit upstream > /dev/null
  stage_file src/Metaphor.Hooks/Hooks/Speech.ps1 # changed inside the merge commit only
  git commit -q --no-verify -m "Merge upstream"
  TIMELINE=""
  run_pr CONTRIBUTOR
  expect 1 "add the security-reviewed label"
}

test_outside_pr_gate_runs_from_a_copy_outside_the_repository() {
  local gate="$WORK/base-scripts" script
  outside_repo
  mkdir -p "$gate/check" "$gate/ci"
  # What CI copies from the base branch: these two and check.sh's modules.
  for script in check.sh ci/security-stamp.sh $(cd "$(dirname "$CHECK")" && echo check/*.sh); do
    cp "$(dirname "$CHECK")/$script" "$gate/$script"
  done
  TIMELINE=""
  CHECK="$gate/check.sh" run_pr CONTRIBUTOR
  expect 1 "add the security-reviewed label"
}

test_outside_pr_still_checks_commit_messages() {
  outside_repo
  git commit -q --no-verify --allow-empty -m "not typed"
  TIMELINE='labeled\t2026-11-16T11:00:00Z\tmaint\n'
  run_pr CONTRIBUTOR
  expect 1 "subject needs a type prefix"
}

# A clean merge from main satisfies the trailer rule, but an outside PR still needs the label for its whole diff.
test_outside_pr_clean_merge_from_main_still_needs_the_label() {
  merge_repo
  merge_main
  base="$main" TIMELINE=""
  run_pr CONTRIBUTOR
  expect 1 "add the security-reviewed label"
  expect_absent "without a Security-Review trailer"
}

# The owner, or an admin (the maintainer's own PRs show MEMBER in an organization's repository), is judged by trailers.
test_maintainer_pr_uses_trailers_and_ignores_the_label() {
  outside_repo
  TIMELINE=""
  FAKE_GH_FAIL=1 run_pr OWNER # neither the timeline nor a permission is read
  expect 0
  PR_AUTHOR=maint run_pr MEMBER
  expect 0
  PR_AUTHOR=maint run_pr COLLABORATOR
  expect 0
  FAKE_PERMISSION_FAIL=maint PR_AUTHOR=maint run_pr MEMBER
  expect 2 "could not read the permission of maint"
  git commit -q --no-verify --allow-empty -m "fix: no trailer"
  stage_file scripts/dev/session.ps1
  git commit -q --no-verify -m "fix: scope without trailer"
  run_pr OWNER
  expect 1 "touches the security-review scope without a Security-Review trailer"
}

# The guards that enforce the ground rules are in the scope too (issue #306).
GUARD_FILES=(scripts/check.sh scripts/check/report.sh scripts/check/run.sh scripts/check/pool.sh scripts/check/docs.sh
  scripts/check/build.sh scripts/check/message.sh scripts/check/review.sh scripts/check/rules.sh
  scripts/ci/game-content.sha256 scripts/check/tree-hash.sh scripts/ci/what-runs.sh
  scripts/check/stage-timing.sh scripts/setup/install-docs-tools.sh scripts/ci/conflict-alert.sh
  scripts/check/check-status.sh scripts/check/run-pester.sh scripts/check/live-log.sh
  scripts/check/wsl-run.sh scripts/check/run-check-suites.sh scripts/setup/wsl-tools.txt scripts/setup/setup-wsl.ps1
  scripts/githooks/pre-commit scripts/githooks/pre-push
  scripts/githooks/commit-msg scripts/githooks/post-commit scripts/githooks/post-merge scripts/githooks/pre-merge-commit
  scripts/ci/security-stamp.sh scripts/ci/github-outage.sh scripts/ci/vuln-audit.sh .github/workflows/ci.yml
  .github/workflows/slow.yml .github/workflows/a-new-one.yml .claude/agents/security-reviewer.md
  tests/check/test-check.sh tests/bash/test-anything.sh tools/slow/survivors.mjs tools/docs/package-lock.json
  .config/dotnet-tools.json scripts/dev/session.ps1 scripts/setup/onboard.ps1 scripts/player/MetaphorInstall.psm1
  scripts/setup/claude.ps1 scripts/dev/ghidra.ps1 scripts/player/check-install.ps1 scripts/player/install.ps1
  scripts/dev/ReloadedDev.psm1 scripts/player/install-pins.json)

# Every script the gate runs, transitively from check.sh, the git hooks and the workflows, is in the scope, so a change
# to one can't take over the CI steps after it (BASH_ENV or PATH through $GITHUB_ENV) without a review.
test_scope_covers_every_script_the_gate_runs() {
  local scope_paths queue=() seen=" " file ref entry covered missing="" parts resolved part
  read -ra scope_paths <<<"$(sed -n '/^scope=(/,/)$/p' "$STAMP" | tr '\n' ' ' | sed 's/^scope=(//; s/).*$//')"
  read -ra queue <<<"$(cd "$ROOT" && echo scripts/check.sh scripts/githooks/* .github/workflows/*)"
  while [ "${#queue[@]}" -gt 0 ]; do
    file="${queue[0]}" queue=("${queue[@]:1}")
    case "$seen" in *" $file "*) continue ;; esac
    seen+="$file "
    covered=""
    for entry in "${scope_paths[@]}"; do
      if [ "$file" = "$entry" ] || { [[ "$entry" == */ ]] && [[ "$file" == "$entry"* ]]; }; then covered=1; fi
    done
    [ -n "$covered" ] || missing+=" $file"
    # Script names on lines that aren't comments, walked wherever they resolve: from the root ("$root/scripts/x.sh"
    # leaves "/scripts/x.sh"), from the referencing file's folder ("$here/x.sh", "../player/x.psm1"), or from scripts/
    # ("$check_dir/check/report.sh" in a file check.sh sources).
    for ref in $(grep -vE '^[[:space:]]*#' "$ROOT/$file" |
      grep -oE '[A-Za-z0-9_./-]*[A-Za-z0-9_-]+\.(sh|mjs|cjs|js|ps1|psm1|py)\b' | sort -u); do
      ref="${ref#./}" ref="${ref#/}"
      for ref in "$ref" "${file%/*}/$ref" "scripts/$ref"; do
        IFS=/ read -ra parts <<<"$ref"
        resolved=()
        for part in "${parts[@]}"; do
          case $part in
            "" | .) ;;
            ..) [ "${#resolved[@]}" -eq 0 ] || unset 'resolved[${#resolved[@]}-1]' ;;
            *) resolved+=("$part") ;;
          esac
        done
        printf -v ref '%s/' "${resolved[@]}"
        ref="${ref%/}"
        [ -f "$ROOT/$ref" ] && queue+=("$ref")
      done
    done
  done
  out="outside the scope:$missing" code=0
  [ -z "$missing" ] || code=1
  expect 0
}

# The scope names directories (#410, ADR 0014): a file added under one is in scope with no list edit. The names below
# appear nowhere in the stamp script. inspect.ps1 and signature-spike.ps1 joined the scope this way with #410;
# session.ps1 and GameSession.psm1 replaced them (#437).
test_stamp_touches_a_new_file_in_each_scoped_directory() {
  local file
  for file in scripts/dev/session.ps1 scripts/dev/GameSession.psm1 scripts/never-listed.ps1 scripts/never-listed/tool.psm1 \
    src/Metaphor.Hooks/NeverListed.cs .github/workflows/never-listed.yml tests/check/never-listed.sh \
    tests/bash/test-never-listed.sh tools/docs/never-listed.mjs tools/slow/never-listed.mjs; do
    review_repo "$file"
    out="$file" code=0
    bash "$STAMP" touches || code=$?
    expect 0
  done
}

# Git pathspecs are case-sensitive, but Windows puts Scripts/x.ps1 in scripts/ (#432): a case variant of a scoped path
# is in scope, staged, committed or in a range.
test_stamp_touches_a_case_variant_of_a_scoped_path() {
  local file base
  for file in Scripts/x.ps1 SRC/Metaphor.HOOKS/X.cs .GitHub/Workflows/x.yml DIRECTORY.BUILD.props; do
    review_repo "$file"
    out="staged $file" code=0
    bash "$STAMP" touches || code=$?
    expect 0
    base="$(git rev-parse HEAD)"
    git commit -q --no-verify -m "fix: case variant"
    out="committed $file" code=0
    bash "$STAMP" touches HEAD || code=$?
    expect 0
    out="range $file" code=0
    bash "$STAMP" touches "$base..HEAD" || code=$?
    expect 0
    [ "$(bash "$STAMP" hash HEAD)" != "$(bash "$STAMP" hash "$base")" ] || out="hash ignores $file" code=1
    expect 0
  done
  review_repo Scripts/x.ps1
  commit "feat: case-variant script"
  expect 1 "no matching stamp"
}

test_stamp_touches_each_guard_file_but_not_docs() {
  local file
  for file in "${GUARD_FILES[@]}"; do
    review_repo "$file"
    out="$file" code=0
    bash "$STAMP" touches || code=$?
    expect 0
  done
  review_repo docs/notes.md
  out="docs only" code=0
  bash "$STAMP" touches || code=$?
  expect 1
}

# What decides dotnet restore, and so the lock files the security job audits (issues #322, #324): MSBuild's
# auto-imported files in any folder, the SDK pin (global.json in any folder too) and the package sources. A change to
# one could hide a vulnerable package.
AUDIT_FILES=(Directory.Build.props src/App/Directory.Build.targets src/App/Directory.Build.rsp
  Directory.Packages.props src/App/Directory.Packages.props Directory.Solution.targets after.Metaphor.sln.targets
  global.json src/App/global.json nuget.config NuGet.config src/App/NuGet.Config)

test_stamp_touches_each_file_the_audit_reads() {
  local file
  for file in "${AUDIT_FILES[@]}"; do
    review_repo "$file"
    out="$file" code=0
    bash "$STAMP" touches || code=$?
    expect 0
  done
  review_repo src/App/App.cs
  out="code only" code=0
  bash "$STAMP" touches || code=$?
  expect 1
}

# A commit's scope hash covers the audit files in subfolders too, and matches the staged hash the stamp recorded.
test_commit_scope_hash_covers_a_nested_audit_file() {
  local staged
  review_repo src/App/Directory.Build.targets
  staged="$(bash "$STAMP" hash)"
  git commit -q --no-verify -m "build: nested targets"
  out="staged $staged, committed $(bash "$STAMP" hash HEAD), before $(bash "$STAMP" hash HEAD~1)" code=0
  [ "$(bash "$STAMP" hash HEAD)" = "$staged" ] || code=1
  [ "$(bash "$STAMP" hash HEAD)" != "$(bash "$STAMP" hash HEAD~1)" ] || code=1
  expect 0
}

test_commits_stage_fails_an_audit_file_commit_without_a_review_trailer() {
  local base
  review_repo src/App/Directory.Packages.props
  base="$(git rev-parse HEAD)"
  git commit -q --no-verify -m "build: skipped the hooks"
  run_check commits "$base..HEAD"
  expect 1 "touches the security-review scope without a Security-Review trailer"
}

test_review_refuses_a_check_script_commit_without_a_stamp() {
  review_repo scripts/check.sh
  commit "perf(check): unreviewed scan change"
  expect 1 "no matching stamp" "security-reviewer"
}

test_commits_stage_fails_a_guard_commit_without_a_review_trailer() {
  local base
  review_repo scripts/check.sh
  base="$(git rev-parse HEAD)"
  git commit -q --no-verify -m "perf(check): skipped the hooks"
  run_check commits "$base..HEAD"
  expect 1 "touches the security-review scope without a Security-Review trailer"
}

# legacy_hash: the scope hash a commit stamped before #306 carries, over the old scope's files in HEAD.
legacy_hash() {
  git ls-tree -r HEAD -- src/Metaphor.Hooks/ scripts/dev/deploy-dev.ps1 scripts/dev/smoke-test.ps1 scripts/release/package-mod.sh \
    scripts/dev/fetch-prism.ps1 scripts/dev/ReloadedDev.psm1 scripts/dev/capture.ps1 |
    awk -F'\t' '{ split($1, m, " "); print m[1], m[3], $2 }' | sha256sum | cut -d' ' -f1
}

test_commits_stage_passes_a_hook_commit_stamped_under_the_old_scope() {
  local base
  review_repo
  stage_file scripts/check.sh "already here before the hook change"
  git commit -q --no-verify -m "chore: guard file from before #306" -m "Security-Review-Skipped: setup"
  base="$(git rev-parse HEAD)"
  stage_file src/Metaphor.Hooks/Hooks/Speech.ps1
  git commit -q --no-verify -m "feat: hook reviewed before #306"
  git commit -q --no-verify --amend -m "feat: hook reviewed before #306" -m "Security-Review: $(legacy_hash)"
  run_check commits "$base..HEAD"
  expect 0
}

test_commits_stage_fails_a_guard_commit_with_an_old_scope_trailer() {
  local base
  review_repo
  base="$(git rev-parse HEAD)"
  stage_file scripts/check.sh
  git commit -q --no-verify -m "perf(check): old-scope trailer"
  git commit -q --no-verify --amend -m "perf(check): old-scope trailer" -m "Security-Review: $(legacy_hash)"
  run_check commits "$base..HEAD"
  expect 1 "touches the security-review scope without a Security-Review trailer"
}

# A merge's changes are counted against its first parent, so a guard change inside it voids an old-scope trailer.
test_commits_stage_fails_a_merge_changing_a_guard_with_an_old_scope_trailer() {
  merge_repo
  git merge -q --no-ff --no-commit origin/main > /dev/null 2>&1
  stage_file scripts/check.sh "changed inside the merge"
  git commit -q --no-verify -m "chore: merge main"
  git commit -q --no-verify --amend -m "chore: merge main" -m "Security-Review: $(legacy_hash)"
  run_check commits "$main..HEAD"
  expect 1 "touches the security-review scope without a Security-Review trailer"
}

test_stamp_script_rejects_unknown_arguments() {
  new_repo
  out="$(bash "$STAMP" nope 2>&1)"
  code=$?
  expect 2 "usage: scripts/ci/security-stamp.sh"
}

# --- The batched file scan (issue #297) ---

# every_rule_fixture: commit a tree where every file rule trips at least once, beside files that pass.
every_rule_fixture() {
  new_repo
  mkdir -p scripts/ci game tools/x.rep docs/raw-sources docs/findings "with space"
  printf 'BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD  some game file\n' \
    > scripts/ci/game-content.sha256
  printf 'abc' > hashed.txt
  printf 'x' > game/a.txt
  printf 'x' > tools/x.rep/00.prp
  printf 'x' > a.cpk && printf 'x' > b.EXE && printf 'x' > c.dmp && printf 'x' > d.idb
  head -c $((1024 * 1024 + 7)) /dev/zero > big.bin
  printf '# A page\n\n- **Saved:** soon\n\n## Body\n\n- **Source:** below the header\n' > docs/raw-sources/bad.md
  printf '# Readme\n' > docs/raw-sources/README.md
  printf 'x' > docs/raw-sources/data.txt
  printf '# A finding\n\n- **Game build:** 1.0\n' > docs/findings/f.md
  printf '{}' > docs/findings/x.json
  printf 'line one\nC:\\%s\\bob\\x and /%s/alice/y\nmail me: a.person%smail.com, b@example.com\n' \
    "$users" "$home" "$at" > paths.txt
  printf 'x\0y /%s/carol/z\n' "$home" > binary.dat
  printf 'git@github.com:o/r.git icon@2x.png\n' > clean.txt
  printf 'one\ntwo zz-private-name\n' > "with space/zz-private-name.txt"
  git add -f .
  git commit -q --no-verify -m "fixture"
}

test_rules_reports_every_rule_exactly_as_before() {
  every_rule_fixture
  PII_PATTERNS=zz-private-name run_check rules
  out="$(grep -v '^scan:' <<<"$out")"
  local want
  # Captured from the per-file scan before #297, which printed findings file by file in index order.
  want="$(cat <<'EOF'
game content: a.cpk: game archive extension
game content or build output: b.EXE: executable extension
game content: big.bin: larger than 1048576 bytes (1048583)
personal information: binary.dat:1: local user path
game content: c.dmp: memory dump extension
game content: d.idb: disassembler database extension
finding: docs/findings/f.md: header has no Game build line naming the executable's SHA-256
finding: docs/findings/x.json: not a .md file
raw source: docs/raw-sources/bad.md: header has no Source line
raw source: docs/raw-sources/bad.md: header has no Saved line with a YYYY-MM-DD date
raw source: docs/raw-sources/bad.md: header has no Game build line
raw source: docs/raw-sources/data.txt: not a .md file
game content: game/a.txt: inside a game folder
game content: hashed.txt: hash matches a known game file
personal information: paths.txt:2: local user path
personal information: paths.txt:3: email address
game content: tools/x.rep/00.prp: inside a disassembler project folder
personal information: with space/zz-private-name.txt: file name matches a private pattern
personal information: with space/zz-private-name.txt:2: matches a private pattern
EOF
)"
  if [ "$code" -eq 1 ] && [ "$out" = "$want" ]; then
    passed=$((passed + 1))
  else
    failed=$((failed + 1))
    printf 'FAIL %s\n  exit %s, diff (want, got):\n%s\n' "$current" "$code" \
      "$(diff <(echo "$want") <(echo "$out") | sed 's/^/    /')"
  fi
}

# with_origin_main: commit what is staged and point origin/main at it, so later commits count as changed.
with_origin_main() {
  git commit -q --no-verify --allow-empty -m "base"
  git update-ref refs/remotes/origin/main HEAD
}

test_rules_scans_only_files_changed_since_origin_main_locally() {
  new_repo
  printf 'x' > old.cpk
  git add old.cpk
  with_origin_main
  printf 'x' > new.pak
  git add new.pak
  git commit -q --no-verify -m "new"
  printf 'x' > staged.dll
  git add staged.dll
  run_check rules
  expect 1 "new.pak" "staged.dll" "scan: 2 files changed since origin/main"
  expect_absent "old.cpk"
}

test_rules_scans_the_whole_tree_in_ci_and_on_request() {
  local env
  for env in GITHUB_ACTIONS=true METAPHOR_SCAN_ALL=1; do
    new_repo
    printf 'x' > old.cpk
    git add old.cpk
    with_origin_main
    printf 'fine\n' > new.txt
    git add new.txt
    out="$(export "$env" &&check_stage rules 2>&1)"
    code=$?
    expect 1 "old.cpk" "scan: every tracked file (2)"
  done
}

test_rules_scans_the_whole_tree_when_the_rules_change() {
  new_repo
  printf 'x' > old.cpk
  git add old.cpk
  with_origin_main
  mkdir -p scripts/ci
  printf '# none yet\n' > scripts/ci/game-content.sha256
  git add scripts/ci/game-content.sha256
  run_check rules
  expect 1 "old.cpk" "scan: every tracked file"
}

# The rules live in scripts/check/ too since the split (#371), so a change to a module alone rescans everything.
test_rules_scans_the_whole_tree_when_a_check_module_changes() {
  new_repo
  printf 'x' > old.cpk
  git add old.cpk
  with_origin_main
  mkdir -p scripts/check
  printf '# none yet\n' > scripts/check/rules.sh
  git add scripts/check/rules.sh
  run_check rules
  expect 1 "old.cpk" "scan: every tracked file"
}

# stage_blob MODE PATH CONTENT: put CONTENT in the index at PATH with MODE, without touching the working tree, so a path
# the file system can't hold (an invalid UTF-8 byte) or a symlink works on every platform.
stage_blob() {
  local blob
  blob="$(printf '%s' "$3" | git hash-object -w --stdin)"
  git update-index --add --cacheinfo "$1,$blob,$2"
}

# GNU grep 3.5 and later print "binary file matches" instead of the lines when a line isn't valid UTF-8 (Ubuntu CI).
test_rules_reads_names_and_headers_past_invalid_utf8() {
  new_repo
  stage_blob 100644 "$(printf 'bad\377name.txt')" 'fine'
  stage_blob 100644 zz-private-name.txt 'fine'
  stage_blob 100644 docs/findings/f.md "$(printf '# Bad \377 title\n\n- **Game build:** %064d\n' 0)"
  out="$(LC_ALL=C.UTF-8 PII_PATTERNS=zz-private-name check_stage rules 2>&1)"
  code=$?
  expect 1 "personal information: zz-private-name.txt: file name matches a private pattern"
  expect_absent "header has no"
}

test_rules_and_fast_scan_a_symlink_turned_into_a_file() {
  new_repo
  stage_blob 120000 link.txt 'target'
  with_origin_main
  stage_blob 100644 link.txt "mail alice.smith${at}gmail.com"
  run_check rules
  expect 1 "link.txt:1: email address"
  run_check fast
  expect 1 "link.txt:1: email address"
}

test_rules_and_fast_scan_the_right_files_from_a_subfolder() {
  new_repo
  mkdir -p src
  printf 'ok\n' > src/keep.txt
  git add src/keep.txt
  with_origin_main
  printf 'x' > src/new.cpk
  git add src/new.cpk
  cd src || exit 2
  run_check rules
  expect 1 "game content: src/new.cpk" "scan: 1 files changed since origin/main"
  run_check fast
  expect 1 "game content: src/new.cpk"
}

# count_starts FILES: run `rules` on a fresh repo of FILES text files and notes, counting the external commands it starts
# through logging stand-ins for the tools the scan uses.
count_starts() {
  local files="$1" shims="$WORK/shims-$RANDOM" log="$WORK/starts-$RANDOM" tool real i
  mkdir -p "$shims"
  for tool in git grep sha256sum perl awk sed wc cut tr cat xargs find mkdir sort head; do
    real="$(command -v "$tool")" || continue
    printf '#!/bin/sh\necho %s >> "%s"\nexec "%s" "$@"\n' "$tool" "$log" "$real" > "$shims/$tool"
    chmod +x "$shims/$tool"
  done
  new_repo
  mkdir -p scripts/ci docs/findings
  printf '0000000000000000000000000000000000000000000000000000000000000000  nothing\n' > scripts/ci/game-content.sha256
  for ((i = 0; i < files; i++)); do
    printf 'text %s\n' "$i" > "file$i.txt"
    printf '# F\n\n- **Game build:** 1.0\n' > "docs/findings/f$i.md"
  done
  git add -A && git commit -q --no-verify -m files
  : > "$log"
  PII_PATTERNS=zz-nobody PATH="$shims:$PATH" bash "$CHECK" rules > /dev/null 2>&1
  wc -l < "$log" | tr -d '[:space:]'
}

test_rules_process_starts_do_not_grow_with_the_file_count() {
  local few many
  current=test_rules_process_starts_do_not_grow_with_the_file_count
  few="$(count_starts 2)"
  many="$(count_starts 40)"
  out="2x2 files: $few starts; 40x2 files: $many starts"
  code=0
  [ "$many" -le "$few" ] || code=1
  expect 0 "starts"
}

# Each test runs in its own subshell, with its own temp folder and its own counters, several at a time (#314: the suite
# took tens of minutes in one process on Windows, where every git and bash start is slow). Each prints
# "[done/total] NAME ok|FAIL (seconds s)" as it finishes, also into the clone's live log (scripts/check/live-log.sh), which
# scripts/check/check-status.sh reads. Their output is shown whole, in test order, after all have finished.
# METAPHOR_TEST_JOBS=1 runs one at a time and must give the same pass and fail counts. An optional argument runs only
# the tests whose names match it (an extended regular expression).
JOBS="$(pool_default_jobs)"
RESULTS="$WORK/results"

# run_test NAME: the pool's job (scripts/check/pool.sh), in its own subshell: run one test with its own folder and
# counters, its counts in $RESULTS. The pool keeps its output.
run_test() {
  local name="$1"
  WORK="$WORK/t-$name"
  passed=0 failed=0 current="$name"
  "$name"
  echo "$passed $failed" > "$RESULTS/$name.counts"
}

# finished NAME CODE SECONDS: the pool's callback; print and log the test's progress line. A test with no counts
# stopped before it finished, and one whose subshell exited non-zero (a crash or a kill) failed.
finished() {
  local name="$1" p=0 f=1
  read -r p f < "$RESULTS/$name.counts" 2> /dev/null || f=1
  [ "$2" = 0 ] || [ "$f" -gt 0 ] || f=1
  secs[$name]="$3"
  live_line "$name" "$f" "$3"
}

names=()
while read -r _ _ name; do
  [[ "$name" == test_* && "$name" =~ ${1:-.} ]] && names+=("$name")
done < <(declare -F)
[ "${#names[@]}" -gt 0 ] || { echo "check tests: no test matches '${1:-}'"; exit 1; }
# One mkdir for every test's folder, with short relative names.
cd "$WORK" && mkdir -p results "${names[@]/#/t-}" && cd "$OLDPWD" || exit 2
declare -A secs=()
live_start test-check "${#names[@]}" "$ROOT"
# Only this suite's own lines: the cases' runs of `main full` must not append to it.
unset METAPHOR_LIVE_LOG
pool_init "$RESULTS" "$JOBS" finished || exit 2
for name in "${names[@]}"; do pool_run "$name" run_test "$name"; done
pool_wait

times=""
for name in "${names[@]}"; do
  IFS= read -r -d '' text < "${pool_file[$name]}"
  printf '%s' "$text"
  if read -r p f < "$RESULTS/$name.counts" 2> /dev/null; then
    passed=$((passed + p))
    failed=$((failed + f))
    if [ "${pool_code[$name]}" != 0 ] && [ "$f" -eq 0 ]; then
      failed=$((failed + 1))
      printf 'FAIL %s\n  the test exited with code %s\n' "$name" "${pool_code[$name]}"
    fi
  else
    failed=$((failed + 1))
    printf 'FAIL %s\n  the test stopped before it finished\n' "$name"
  fi
  times+="${secs[$name]} $name"$'\n'
done
echo "slowest tests (seconds):"
sort -rn <<< "${times%$'\n'}" | head -n 8 | sed 's/^/  /'

summary="check tests: $passed passed, $failed failed"
echo "$summary"
live_end "$((failed > 0))"
[ "$failed" -eq 0 ]
