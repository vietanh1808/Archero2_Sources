#!/usr/bin/env bash
#
# auto-commit-push.sh — commit pending files in batches and push after each batch.
#
# Flow:  pick N files -> drop oversized ones into .gitignore -> commit "." -> push
#        (wait until it finishes) -> repeat until nothing is left.
#
# Files at or above MAX_SIZE_MB are the ones that really belong in Git LFS, so
# they are never staged: the script appends them to .gitignore instead and
# records them in $BIGLOG so you can "git lfs track" them later.
#
# Usage:
#   ./auto-commit-push.sh                  # 500 files/batch, message ".", push to upstream
#   BATCH=1000 ./auto-commit-push.sh       # bigger batches
#   MAX_SIZE_MB=25 ./auto-commit-push.sh   # stricter size cut-off
#   MSG="wip" ./auto-commit-push.sh        # different commit message
#   MAX_ROUNDS=10 ./auto-commit-push.sh    # stop after 10 batches
#   DRY_RUN=1 ./auto-commit-push.sh        # show what would happen, change nothing
#   SCAN_ONLY=1 ./auto-commit-push.sh      # just list the oversized files and exit
#
set -uo pipefail

BATCH=${BATCH:-500}
MSG=${MSG:-.}
REMOTE=${REMOTE:-origin}
BRANCH=${BRANCH:-$(git branch --show-current)}
MAX_ROUNDS=${MAX_ROUNDS:-0}            # 0 = unlimited
PUSH_RETRIES=${PUSH_RETRIES:-5}
MAX_SIZE_MB=${MAX_SIZE_MB:-50}         # GitHub warns at 50 MB, hard-rejects at 100 MB
DRY_RUN=${DRY_RUN:-0}
SCAN_ONLY=${SCAN_ONLY:-0}
BIGLOG=${BIGLOG:-large-files-ignored.txt}

MAX_SIZE=$((MAX_SIZE_MB * 1024 * 1024))
IGNORE_HEADER="# --- auto-commit-push.sh: files >= ${MAX_SIZE_MB}MB, move to Git LFS if you need them ---"

cd "$(git rev-parse --show-toplevel)" || exit 1

TMP=$(mktemp -d)
LIST="$TMP/pending.z"
trap 'rm -rf "$TMP"' EXIT

log() { printf '%s | %s\n' "$(date +%H:%M:%S)" "$*"; }
human() {
  awk -v b="$1" 'BEGIN {
    if (b >= 1048576) printf "%.1f MB", b/1048576; else printf "%.0f KB", b/1024
  }'
}

# ---------------------------------------------------------------- pending list

# Every pending path (untracked + modified + deleted) as NUL-separated records.
refresh_list() {
  git status --porcelain -uall -z |
  awk 'BEGIN { RS="\0"; ORS="\0" }
       {
         if (skip) { print; skip=0; next }          # rename/copy source path
         st = substr($0, 1, 2)
         if (st ~ /R/ || st ~ /C/) skip = 1          # next record is the old path
         print substr($0, 4)
       }' > "$LIST"
}

count_z() { tr -dc '\0' < "$1" | wc -c | tr -d ' '; }

# ------------------------------------------------------------- oversized files

# A path turned into a root-anchored .gitignore pattern, with glob metacharacters
# and trailing spaces escaped so it matches exactly this one file.
esc_gitignore() {
  local p=$1 tail=''
  p=${p//\\/\\\\}; p=${p//\*/\\*}; p=${p//\?/\\?}; p=${p//\[/\\[}
  while [ "${p: -1}" = " " ]; do p=${p% }; tail="\\ $tail"; done
  printf '/%s%s\n' "$p" "$tail"
}

ignored_this_run=0

ignore_big_file() {
  local p=$1 sz=$2 pat
  pat=$(esc_gitignore "$p")

  # .gitignore has no effect on a file git already tracks — say so rather than
  # pretending it worked.
  if git ls-files --error-unmatch -- "$p" >/dev/null 2>&1; then
    log "  ! TRACKED and oversized ($(human "$sz")): $p"
    log "    .gitignore cannot untrack it - use 'git rm --cached' or move it to LFS."
    return
  fi

  log "  ~ too big ($(human "$sz")), ignoring: $p"
  ignored_this_run=$((ignored_this_run + 1))
  [ "$DRY_RUN" = "1" ] && return 0

  if ! grep -Fxq "$pat" .gitignore 2>/dev/null; then
    grep -Fxq "$IGNORE_HEADER" .gitignore 2>/dev/null || printf '\n%s\n' "$IGNORE_HEADER" >> .gitignore
    printf '%s\n' "$pat" >> .gitignore
  fi
  printf '%s\t%s\n' "$sz" "$p" >> "$BIGLOG"
}

# Split $1 (NUL list) into $2 (stageable). Oversized entries get ignored instead.
# Sizes come from one batched stat call, not one process per file.
split_oversized() {
  local slice=$1 ok=$2 rec sz p
  local -A big=()

  while IFS= read -r -d '' rec; do
    sz=${rec%%$'\t'*}
    p=${rec#*$'\t'}
    if [ "$sz" -ge "$MAX_SIZE" ]; then big["$p"]=$sz; fi
  done < <(xargs -0 -r stat --printf='%s\t%n\0' < "$slice" 2>/dev/null)

  : > "$ok"
  while IFS= read -r -d '' p; do
    if [ -n "${big["$p"]+set}" ]; then
      ignore_big_file "$p" "${big["$p"]}"
    else
      printf '%s\0' "$p" >> "$ok"          # includes deletions, which stat skips
    fi
  done < "$slice"
}

# ------------------------------------------------------------------- scan only

if [ "$SCAN_ONLY" = "1" ]; then
  refresh_list
  log "Scanning $(count_z "$LIST") pending file(s) for anything >= ${MAX_SIZE_MB}MB..."
  found=0
  while IFS= read -r -d '' rec; do
    sz=${rec%%$'\t'*}
    p=${rec#*$'\t'}
    [ "$sz" -ge "$MAX_SIZE" ] || continue
    found=$((found + 1))
    printf '  %10s  %s\n' "$(human "$sz")" "$p"
  done < <(xargs -0 -r stat --printf='%s\t%n\0' < "$LIST" 2>/dev/null)
  log "$found file(s) at or above the limit."
  exit 0
fi

# ----------------------------------------------------------------- commit loop

round=0
total_committed=0

while :; do
  refresh_list
  left=$(count_z "$LIST")
  [ "$left" -eq 0 ] && { log "Nothing left to commit. Done."; break; }
  log "Pending files: $left"

  # Consume the snapshot batch by batch. Only "git add" touches the worktree,
  # so the snapshot stays valid until it is exhausted.
  pass_marker="$total_committed:$ignored_this_run"
  offset=0
  while [ "$offset" -lt "$left" ]; do
    round=$((round + 1))
    if [ "$MAX_ROUNDS" -gt 0 ] && [ "$round" -gt "$MAX_ROUNDS" ]; then
      log "Reached MAX_ROUNDS=$MAX_ROUNDS, stopping."
      exit 0
    fi

    # ---- slice: files [offset, offset+BATCH) ----
    slice="$TMP/slice.z"; ok="$TMP/ok.z"
    awk -v skip="$offset" -v take="$BATCH" \
        'BEGIN { RS="\0"; ORS="\0" } NR > skip && NR <= skip + take { print }' \
        "$LIST" > "$slice"
    n=$(count_z "$slice")
    [ "$n" -eq 0 ] && break
    offset=$((offset + n))

    # ---- size gate ----
    split_oversized "$slice" "$ok"
    nok=$(count_z "$ok")
    log "[round $round] $nok/$n file(s) stageable  ($offset/$left)"

    if [ "$DRY_RUN" = "1" ]; then
      log "[round $round] DRY_RUN: skipping add/commit/push"
      continue
    fi
    # ---- add (CRLF warnings are pure noise on a Unity repo; keep real errors) ----
    if [ "$nok" -gt 0 ]; then
      if ! GIT_LITERAL_PATHSPECS=1 git add --pathspec-from-file="$ok" --pathspec-file-nul \
           2> >(grep -v 'will be replaced by CRLF' >&2); then
        log "[round $round] git add FAILED, aborting."
        exit 1
      fi
    fi
    git add -- .gitignore "$BIGLOG" 2>/dev/null   # also when the whole batch was oversized

    # ---- commit ----
    if git diff --cached --quiet; then
      log "[round $round] nothing to commit in this batch."
      continue
    fi
    if ! git commit -q -m "$MSG"; then
      log "[round $round] git commit FAILED, aborting."
      exit 1
    fi
    total_committed=$((total_committed + nok))
    log "[round $round] committed $(git rev-parse --short HEAD)"

    # ---- push (blocking; retry on transient network failure) ----
    attempt=1
    until git push "$REMOTE" "HEAD:$BRANCH"; do
      if [ "$attempt" -ge "$PUSH_RETRIES" ]; then
        log "[round $round] push FAILED after $attempt attempts. Fix it, then re-run this script."
        exit 1
      fi
      wait=$((attempt * 10))
      log "[round $round] push failed (attempt $attempt), retrying in ${wait}s..."
      sleep "$wait"
      attempt=$((attempt + 1))
    done
    log "[round $round] pushed OK. total committed this run: $total_committed"
  done

  [ "$DRY_RUN" = "1" ] && { log "DRY_RUN: one pass only, stopping."; break; }

  # Nothing committed and nothing newly ignored means the leftovers can never be
  # cleared (an oversized file that git already tracks, say) — stop rather than
  # rescan the same list forever.
  if [ "$pass_marker" = "$total_committed:$ignored_this_run" ]; then
    log "No progress in the last pass; $left file(s) still pending. Stopping."
    break
  fi
done

log "Finished. $round round(s), $total_committed file(s) committed."
if [ "$ignored_this_run" -gt 0 ]; then
  log "$ignored_this_run oversized file(s) were ignored - see $BIGLOG."
  log "To keep them, track the pattern with Git LFS and drop its line from .gitignore:"
  log "    git lfs install && git lfs track '*.ext' && git add .gitattributes"
fi
