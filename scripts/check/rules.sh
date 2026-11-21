# shellcheck shell=bash
# check.sh's repository rules (ADR 0013): the file listing and the file scan, which fast, pre-commit, rules and full
# run: personal information (general and private patterns), game content (folders, extensions, size and known hashes)
# and the raw-source and finding headers. Sourced by scripts/check.sh only; it uses report and die from report.sh.
#
# Run globals: list_entries reads `stage` and `tmp`, and sets `scan_scope`, which main resets; scan_files reads `stage`,
# `tmp`, `root` and `scan_scope`; report updates `failures`. Reads GITHUB_ACTIONS, METAPHOR_SCAN_ALL, PII_PATTERNS,
# REQUIRE_PRIVATE_PATTERNS and the git-ignored .claude/pii-patterns.txt.
# Private to this module: max_bytes, known_hashes, user_path_re, email_re, not_email_re, licence_notice_re, and the scan's folder and
# arrays: blobs, paths, ids, sizes, notes, found_hash, found_user, found_email, found_name, found_private, has_source,
# has_saved, has_build and has_sha.

# list_entries prints "<mode> <blob> <stage>\t<path>\0" for each file to check, in `git ls-files -s -z` form. Changed
# files are every change but a deletion (--diff-filter=d), so a symlink turned into a file is scanned too. They are
# listed in $tmp/changed, with paths from the repository root: the script runs from there.
# fast and pre-commit scan the staged changes. Elsewhere CI, or METAPHOR_SCAN_ALL=1, scans every tracked file: CI does
# so before anything merges. Otherwise the scan covers the files changed since the merge-base with origin/main, staged
# changes included (issue #297). Without origin/main, or when the rules themselves change, it scans every file.
list_entries() {
  local base
  case "$stage" in
    fast | pre-commit)
      git diff --cached --name-only --diff-filter=d -z > "$tmp/changed" && changed_entries ;;
    *)
      if [ "${GITHUB_ACTIONS:-}" != true ] && [ -z "${METAPHOR_SCAN_ALL:-}" ] &&
        base="$(git merge-base HEAD origin/main 2> /dev/null)" &&
        git diff --cached --name-only --diff-filter=d -z "$base" -- > "$tmp/changed" &&
        ! grep -qzxE 'scripts/(check\.sh|check/.*|ci/game-content\.sha256)' "$tmp/changed"; then
        scan_scope="changed since origin/main"
        changed_entries
      else
        scan_scope=all
        git ls-files -s -z
      fi ;;
  esac
}

# changed_entries: print the index entries of the paths in $tmp/changed. One `git ls-files` of the whole index, kept
# when the path is listed, instead of an xargs and a `git ls-files` per batch (#328): the changed paths are files, so
# matching them exactly lists what a literal pathspec per path would, in the same index order.
changed_entries() {
  local path entry
  local -A wanted=()
  [ -s "$tmp/changed" ] || return 0
  while IFS= read -r -d '' path; do wanted[$path]=1; done < "$tmp/changed"
  git ls-files -s -z > "$tmp/index" || return
  while IFS= read -r -d '' entry; do
    [ -z "${wanted[${entry#*$'\t'}]:-}" ] || printf '%s\0' "$entry"
  done < "$tmp/index"
}

# Policy, not a measurement: our code and notes have no reason to exceed this, so anything bigger needs a look.
max_bytes=$((1024 * 1024))

# read_known_hashes: set known_hashes to the known game-file hashes in the index's scripts/ci/game-content.sha256, lower
# case, one per line. The file holds one SHA-256 per line, then a label naming its game build; '#' starts a comment.
# One `git cat-file --batch` and builtins, where a `git cat-file -e`, `git show`, tr, sed and awk were five starts (#328).
read_known_hashes() {
  local listing head line
  known_hashes=""
  listing="$(git cat-file --batch <<< :scripts/ci/game-content.sha256)" || die "could not read scripts/ci/game-content.sha256"
  head="${listing%%$'\n'*}"
  [[ "$head" == *" missing" ]] && return 0
  [[ "$head" =~ ^[0-9a-f]+\ blob\ [0-9]+$ ]] || die "could not read scripts/ci/game-content.sha256"
  [ "$listing" != "$head" ] || return 0 # an empty file
  while IFS= read -r line; do
    line="${line//$'\r'/}" && line="${line%%#*}"
    [[ "$line" =~ ^[[:blank:]]*([^[:blank:]]+) ]] && known_hashes+="${BASH_REMATCH[1],,}"$'\n'
  done <<< "${listing#*$'\n'}"
}

# pattern_lines: copy stdin's lines to stdout without CRs, leading or trailing spaces, comments or blank lines.
pattern_lines() {
  local line
  while IFS= read -r line || [ -n "$line" ]; do
    line="${line//$'\r'/}"
    line="${line#"${line%%[![:space:]]*}"}" && line="${line%"${line##*[![:space:]]}"}"
    [ -z "$line" ] || [ "${line:0:1}" = "#" ] || printf '%s\n' "$line"
  done
}

# read_private_patterns: write the private personal-information patterns to $tmp/private, one per line. They are
# matched as fixed strings so special characters can't break the scan. The git-ignored .claude/pii-patterns.txt holds
# them locally; CI passes them in PII_PATTERNS. One per line, '#' comments.
read_private_patterns() {
  if [ -n "${PII_PATTERNS:-}" ]; then
    pattern_lines <<< "$PII_PATTERNS"
  elif [ -f "$root/.claude/pii-patterns.txt" ]; then
    pattern_lines < "$root/.claude/pii-patterns.txt"
  fi > "$tmp/private"
  if [ ! -s "$tmp/private" ]; then
    # CI sets REQUIRE_PRIVATE_PATTERNS on pushes to main, where a missing secret must not pass quietly.
    [ "${REQUIRE_PRIVATE_PATTERNS:-}" = 1 ] && die "no private patterns, but REQUIRE_PRIVATE_PATTERNS=1 (is the secret set?)"
    echo "warning: no private patterns, so personal information is checked against general patterns only." \
      "Copy .claude/pii-patterns.example.txt to .claude/pii-patterns.txt and fill it in." >&2
  fi
}

report_game_content() {
  report "game content: $1: $2"
}

# General personal-information patterns run everywhere, CI included. Unlike the private list they are regular
# expressions, which is safe because we write and test them here.
# User folders: C:\Users\<name>, /c/Users/<name> (Git Bash), /mnt/c/Users/<name> (WSL), /home/<name>, /Users/<name>.
user_path_re='[A-Za-z]:[\\/]+[Uu][Ss][Ee][Rr][Ss][\\/]+[A-Za-z0-9_]'
user_path_re+='|(^|[^A-Za-z0-9_.:-])((/mnt)?/[A-Za-z]/[Uu]sers|/home|/Users)/[A-Za-z0-9_]'
email_re='[a-z0-9._%+-]+@[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}'
# Email-shaped text that can't belong to a person: reserved example domains, GitHub's no-reply addresses, SSH remotes
# (git@host) and high-resolution image names (icon@2x.png).
not_email_re='@([a-z0-9-]+\.)*(example\.(com|org|net)|[a-z0-9-]+\.(invalid|example|test)|users\.noreply\.github\.com)$'
not_email_re+='|^git@|@[0-9]+x\.[a-z]+$'
# A third-party licence notice, such as src/Metaphor.Core/licenses/fastlz/LICENSE.MIT: the email rule skips it.
# Only src/<project>/licenses/<library>/, and only LICENSE, COPYING or NOTICE with at most one extension.
licence_notice_re='^src/[^/]+/licenses/[^/]+/(LICENSE|COPYING|NOTICE)(\.[A-Za-z0-9]+)?$'

# The scan (issue #297). Process starts dominate the run time in Git Bash, so nothing starts once per file: one
# `git cat-file --batch` writes every blob to $blobs/<n> (n is its index in `paths`), one sha256sum pass hashes them,
# and each rule runs one grep over all of them. Findings are gathered per file, then printed file by file in index
# order, exactly as a per-file loop would print them. scan_files sets blobs to $tmp/blobs.

# scan_lines FOUND REASON SKIP-RE GREP-ARGS...: add a finding to the array named FOUND, per file, for each line where
# grep finds a match, ignoring matches that SKIP-RE (lower case; empty skips nothing) accepts. Only the line number is
# printed, never the matched text, because CI logs are public.
scan_lines() {
  local -n found_in="$1"
  local reason="$2" skip="$3" status line n rest number text last=""
  shift 3
  grep -rnao "$@" -- "$blobs" > "$tmp/matches"
  status=$?
  [ "$status" -le 1 ] || die "could not scan the files to check"
  # grep reads one file at a time, so a file's matches are together and in line order.
  while IFS= read -r line; do
    line="${line#"$blobs"/}" && n="${line%%:*}" && rest="${line#*:}" && number="${rest%%:*}" && text="${rest#*:}"
    [ -n "$skip" ] && [[ "${text,,}" =~ $skip ]] && continue
    [ "$n:$number" = "$last" ] && continue
    last="$n:$number"
    found_in[n]+="personal information: ${paths[n]}:$number: $reason"$'\n'
  done < "$tmp/matches"
}

# header_has HAS LABEL VALUE-RE: set HAS[n] for each note whose header (the lines before its first "## " heading) has a
# "- **LABEL:** value" line whose value matches VALUE-RE. The label matches as a word in any case, plural or inside
# longer wording ("Date saved:", "Game build(s) the sources mention:"), with the colon inside or after the bold. One
# opening quote before the value (`, ", ' or <) is skipped. $tmp/headers holds every note's header lines as "n:line".
header_has() {
  local -n has_line="$1"
  local line
  # -a: GNU grep 3.5+ withholds the lines of a file with invalid UTF-8 ("binary file matches") and still exits 0.
  grep -aiE "^[0-9]+:- \*\*([^*]* )?${2}s?([^*a-z][^*]*)?(:\*\*|\*\*:)[[:space:]]*[\`\"'<]?${3}" "$tmp/headers" \
    > "$tmp/header-lines"
  [ $? -le 1 ] || die "could not read the note headers"
  while IFS= read -r line; do has_line[${line%%:*}]=1; done < "$tmp/header-lines"
}

# emit FINDINGS: report each line of FINDINGS (each ends in a newline).
emit() {
  local rest="$1"
  while [ -n "$rest" ]; do
    report "${rest%%$'\n'*}"
    rest="${rest#*$'\n'}"
  done
}

# scan_files: the file rules on the entries list_entries wrote to $tmp/entries.
scan_files() {
  local scan_started=$SECONDS entry meta hash size i n path lower line idlist hash_them=0
  local -A known=()
  blobs="$tmp/blobs"
  read_known_hashes
  read_private_patterns
  for hash in $known_hashes; do known[$hash]=1; done
  paths=() ids=() sizes=() notes=()
  found_hash=() found_user=() found_email=() found_name=() found_private=() has_source=() has_saved=() has_build=()
  has_sha=()
  while IFS= read -r -d '' entry; do
    meta="${entry%%$'\t'*}" # "<mode> <blob> <stage>"
    [ "${meta%% *}" = 160000 ] && continue # a submodule: its content lives in another repository
    meta="${meta#* }"
    paths+=("${entry#*$'\t'}") ids+=("${meta%% *}")
  done < "$tmp/entries"

  if [ "${#paths[@]}" -gt 0 ]; then
    command -v perl > /dev/null || die "the file scan needs perl (Git for Windows and Linux include it)"
    # Notes cite their sources and pin their game build (AGENTS.md ground rules 1 and 2).
    for i in "${!paths[@]}"; do
      case "${paths[i]}" in
        docs/raw-sources/README.md | docs/findings/README.md) ;;
        docs/raw-sources/*.md | docs/findings/*.md) notes+=("$i") ;;
      esac
    done
    [ -z "$known_hashes" ] || hash_them=1
    # Each blob as "<id> blob <size>\n<content>\n". perl writes the content to $blobs/<n>, prints its size (and its
    # SHA-256 when there are known hashes to match), and writes each note's header lines (those before its first "## "
    # heading) to $tmp/headers as "<n>:<line>". One perl start where a sha256sum and an awk pass, each behind an xargs
    # and two subshells, were eight (#328).
    printf -v idlist '%s\n' "${ids[@]}"
    git cat-file --batch <<< "${idlist%$'\n'}" | perl -e '
      binmode STDIN;
      my ($dir, $hash_them, $headers, @notes) = @ARGV;
      my %note = map { $_ => 1 } @notes;
      require Digest::SHA if $hash_them;
      mkdir($dir) or die "check: could not create $dir: $!\n";
      open(my $hdr, ">:raw", $headers) or die "check: $headers: $!\n";
      my $n = 0;
      while (defined(my $head = <STDIN>)) {
        my ($id, $type, $size) = split / /, $head;
        die "check: object $id is missing\n" unless defined $size && $type eq "blob";
        chomp $size;
        my $data = "";
        while (length($data) < $size) {
          read(STDIN, $data, $size - length($data), length($data)) or die "check: object $id is cut short\n";
        }
        getc(STDIN);
        open(my $out, ">:raw", "$dir/$n") or die "check: $dir/$n: $!\n";
        print $out $data;
        close($out) or die "check: $dir/$n: $!\n";
        if ($note{$n}) {
          open(my $in, "<", \$data) or die "check: note $n: $!\n";
          while (defined(my $line = <$in>)) {
            chomp $line;
            last if $line =~ /^## /;
            print $hdr "$n:$line\n";
          }
        }
        print $hash_them ? "$size " . Digest::SHA::sha256_hex($data) . "\n" : "$size\n";
        $n++;
      }
      close($hdr) or die "check: $headers: $!\n";' "$blobs" "$hash_them" "$tmp/headers" "${notes[@]}" > "$tmp/sizes" ||
      die "could not read the files to check"
    n=0
    while read -r size hash; do
      sizes+=("$size")
      [ "$hash_them" -eq 0 ] || [ -z "${known[$hash]:-}" ] || found_hash[n]=1
      n=$((n + 1))
    done < "$tmp/sizes"
    [ "${#sizes[@]}" -eq "${#paths[@]}" ] || die "could not read the files to check"

    if [ "${#notes[@]}" -gt 0 ]; then
      header_has has_source Source '[[:alnum:]]'
      header_has has_saved Saved '[0-9]{4}-[0-9]{2}-[0-9]{2}([^0-9a-z]|$)'
      header_has has_build "Game build" '[[:alnum:]]'
      # A finding names its game build by the executable's SHA-256 (GLOSSARY.md).
      header_has has_sha "Game build" '[0-9a-f]{64}([^0-9a-f]|$)'
    fi

    scan_lines found_user "local user path" "" -E "$user_path_re"
    scan_lines found_email "email address" "$not_email_re" -iE "$email_re"
    if [ -s "$tmp/private" ]; then
      # One name per line, so grep's line number is the file's index plus one.
      printf '%s\n' "${paths[@]//$'\n'/ }" > "$tmp/names"
      grep -naiF -f "$tmp/private" "$tmp/names" > "$tmp/name-matches" # -a as in header_has
      [ $? -le 1 ] || die "could not scan the file names"
      while IFS= read -r line; do found_name[${line%%:*} - 1]=1; done < "$tmp/name-matches"
      scan_lines found_private "matches a private pattern" "" -iF -f "$tmp/private"
    fi
  fi

  for i in "${!paths[@]}"; do
    path="${paths[i]}"
    # Game folders are the git-ignored ones at the repo root. Lower-cased because Windows paths are case-insensitive.
    lower="${path,,}"
    case "$lower" in
      game/* | extracted/* | dumps/* | captures/* | ghidra/* | out/ghidra/*) report_game_content "$path" "inside a game folder" ;;
    esac
    case "/$lower" in
      *.rep/*) report_game_content "$path" "inside a disassembler project folder" ;;
      *.cpk | *.pak) report_game_content "$path" "game archive extension" ;;
      # Our own builds are blocked too: they live as CI artifacts tied to a commit, never in the repository.
      *.exe | *.dll | *.asi) report "game content or build output: $path: executable extension" ;;
      *.dmp | *.mdmp) report_game_content "$path" "memory dump extension" ;;
      # Gameplay captures and their captions (the chapter scrub, scripts/dev/scrub.ps1) hold game footage and text.
      *.mp4 | *.mkv | *.webm | *.mov | *.avi | *.m4v | *.flv | *.vtt | *.srt)
        report_game_content "$path" "video or caption extension" ;;
      *.gpr | *.gzf | *.gbf | *.idb | *.i64 | *.til | *.nam | *.id0 | *.id1 | *.id2 | *.bndb | *.dd64)
        report_game_content "$path" "disassembler database extension" ;;
    esac

    size="${sizes[i]}"
    [ "$size" -le "$max_bytes" ] || report_game_content "$path" "larger than $max_bytes bytes ($size)"
    [ -z "${found_hash[i]:-}" ] || report_game_content "$path" "hash matches a known game file"

    case "$path" in
      docs/raw-sources/README.md | docs/findings/README.md) ;;
      docs/raw-sources/*.md)
        [ -n "${has_source[i]:-}" ] || report "raw source: $path: header has no Source line"
        [ -n "${has_saved[i]:-}" ] || report "raw source: $path: header has no Saved line with a YYYY-MM-DD date"
        [ -n "${has_build[i]:-}" ] || report "raw source: $path: header has no Game build line" ;;
      docs/findings/*.md)
        [ -n "${has_sha[i]:-}" ] ||
          report "finding: $path: header has no Game build line naming the executable's SHA-256" ;;
      docs/raw-sources/*) report "raw source: $path: not a .md file" ;;
      docs/findings/*) report "finding: $path: not a .md file" ;;
    esac

    emit "${found_user[i]:-}"
    # A third-party licence notice ships verbatim, its author's email included (ADR 0005), so only the generic email
    # rule skips it. The user-path and private-pattern rules still apply, so the maintainer's own details are caught.
    [[ "$path" =~ $licence_notice_re ]] || emit "${found_email[i]:-}"
    [ -z "${found_name[i]:-}" ] || report "personal information: $path: file name matches a private pattern"
    emit "${found_private[i]:-}"
  done
  case "$stage" in
    rules | full)
      if [ "$scan_scope" = all ]; then
        echo "scan: every tracked file (${#paths[@]}) in $((SECONDS - scan_started)) s"
      else
        echo "scan: ${#paths[@]} files $scan_scope in $((SECONDS - scan_started)) s" \
          "(CI scans every tracked file; METAPHOR_SCAN_ALL=1 does so here)"
      fi ;;
  esac
}
