#!/bin/bash
# safe_unity_test.sh -- THE way an agent session runs Unity tests in this repo (docs/53 §17).
#
#   bash docs/artifacts/editor-memory/safe_unity_test.sh --mode EditMode|PlayMode
#        [--filter NAME] [--include-explicit] [--timeout SEC] [--label TEXT] [--check-only]
#
# What it does (stops and says why at the first failure -- it never falls back to an unguarded run):
#   1. validates arguments (no unvetted text ever reaches a shell)       -> exit 2
#   2. preflight: python + unity CLI answer; exactly one 'ready' Editor whose project is THIS repo  -> exit 32
#   3. starts its own sampler (editor_watch.ps1) and waits for a healthy first sample naming that Editor's pid
#      (the first sample scans the whole current Editor.log, so an existing flood is caught here)  -> exit 30
#   3b. (not with --check-only) triggers `recompile` so the tests run against the CURRENT sources, waits for a
#      strictly clean completed/up_to_date and for the main thread to answer again       -> exit 14 / 26
#   4. runs the guarded loop (playmode_ab_loop.sh) for ONE run (or CHECK_ONLY)       -> exit 10-25, 27
#   5. reads the saved test_status JSON: PASS 0 / TESTS_FAILED 1 (names listed) / NO_TESTS_MATCHED 3 /
#      RESULT_MISSING_OR_MALFORMED 4 / NOTHING_EXECUTED 5 (every selected test skipped)
#   6. audits every sampler row written during the run; any non-OK row                             -> exit 31
#   7. always (also on Ctrl-C / SIGTERM / SIGHUP): stops the guarded loop FIRST (so nothing is submitted after the
#      entry gives up), then the sampler it started; the loop's trap releases its lock/temp; the record is kept.
# Long PlayMode runs take minutes: from an agent's Bash tool use run_in_background or a timeout >= 600000 ms, otherwise
# the tool kills the entry (safe -- the loop is stopped -- but the run is wasted).
# Record (kept, gitignored): Logs/safe-test/<UTC>-<label>/ meta.txt loop.txt loop.stderr watch.csv
#                            <label>1-test_status.json summary.txt
# ⛔ If this entry fails, do NOT run `unity cmd run_tests` directly: diagnose, report, and ask (CLAUDE.md).
#
# Test hooks (env): UNITY_CLI PYTHON SAMPLER_CMD SAFE_TEST_ROOT LOOP_SCRIPT SAMPLE_INTERVAL SAMPLER_START_TIMEOUT
# RECOMPILE_TIMEOUT (default 300) PING_FILE,
# plus the loop's own env (STATUS_FILE LOGFILE PRIVATE_GB_CMD LOCK_DIR CLI_TIMEOUT SETTLE_SEC POLL_SEC ...).
export PATH="$PATH:$HOME/AppData/Local/Unity/bin"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
PY="${PYTHON:-python}"
J="$HERE/unity_test_json.py"
UNITY="${UNITY_CLI:-unity}"
LOOP="${LOOP_SCRIPT:-$HERE/playmode_ab_loop.sh}"
ROOT="${SAFE_TEST_ROOT:-$REPO/Logs/safe-test}"
SAMPLE_INTERVAL="${SAMPLE_INTERVAL:-5}"
SAMPLER_START_TIMEOUT="${SAMPLER_START_TIMEOUT:-60}"
RECOMPILE_TIMEOUT="${RECOMPILE_TIMEOUT:-300}"
PING_FILE="${PING_FILE:-$HERE/ping_probe.cs.txt}"
if [ -z "$PROJECT_PATH" ]; then
  if command -v cygpath >/dev/null; then PROJECT_PATH="$(cygpath -w "$REPO")"; else PROJECT_PATH="$REPO"; fi
fi

usage() { sed -n '3,6p' "$0" >&2; echo "error: $*" >&2; exit 2; }

# ---------- 1. arguments ----------
MODE=""; FILTER=""; EXPLICIT=0; TIMEOUT=""; LABEL=""; CHECK=0
while [ $# -gt 0 ]; do
  case "$1" in
    --mode)             [ $# -ge 2 ] || usage "--mode needs a value"; MODE=$2; shift 2 ;;
    --filter)           [ $# -ge 2 ] || usage "--filter needs a value"; FILTER=$2; shift 2 ;;
    --include-explicit) EXPLICIT=1; shift ;;
    --timeout)          [ $# -ge 2 ] || usage "--timeout needs a value"; TIMEOUT=$2; shift 2 ;;
    --label)            [ $# -ge 2 ] || usage "--label needs a value"; LABEL=$2; shift 2 ;;
    --check-only)       CHECK=1; shift ;;
    *) usage "unknown argument '$1'" ;;
  esac
done
case "$MODE" in EditMode|PlayMode) ;; *) usage "--mode must be EditMode or PlayMode" ;; esac
[ -z "$TIMEOUT" ] || { [[ "$TIMEOUT" =~ ^[0-9]+$ ]] && [ "$TIMEOUT" -ge 10 ] && [ "$TIMEOUT" -le 7200 ]; } || usage "--timeout must be an integer 10..7200 seconds"
[ -n "$LABEL" ] || LABEL="$MODE"
[[ "$LABEL" =~ ^[A-Za-z0-9_-]{1,40}$ ]] || usage "--label must match [A-Za-z0-9_-]{1,40}"
[ $EXPLICIT -eq 0 ] || [ -n "$FILTER" ] || usage "--include-explicit requires --filter (the filter is a case-insensitive SUBSTRING of the full test name: give the full Class.Method so you select one probe)"
"$PY" -I "$J" validate-filter probe >/dev/null 2>&1 || { echo "PREFLIGHT python ($PY) cannot run the parser" >&2; exit 32; }
if [ -n "$FILTER" ]; then "$PY" -I "$J" validate-filter "$FILTER" >/dev/null || usage "--filter rejected (letters, digits, _ . , : ( ) < > \" ' * + ? | [ ] / = - space; 1-400 chars; not starting with -)"; fi
[[ "$SAMPLE_INTERVAL" =~ ^[0-9]+$ && "$SAMPLER_START_TIMEOUT" =~ ^[0-9]+$ && "$RECOMPILE_TIMEOUT" =~ ^[0-9]+$ ]] || usage "SAMPLE_INTERVAL / SAMPLER_START_TIMEOUT / RECOMPILE_TIMEOUT must be integers"

mkdir -p "$ROOT" && RD=$(mktemp -d "$ROOT/$(date -u +%Y%m%dT%H%M%SZ)-$LABEL.XXXX") || { echo "PREFLIGHT cannot create a record dir under $ROOT" >&2; exit 32; }
{ echo "start_utc=$(date -u +%FT%TZ)"; echo "mode=$MODE"; echo "filter=$FILTER"; echo "include_explicit=$EXPLICIT"
  echo "timeout=${TIMEOUT:-default}"; echo "check_only=$CHECK"; echo "project=$PROJECT_PATH"
  echo "git_head=$(git -C "$REPO" rev-parse --short HEAD 2>/dev/null)"; } > "$RD/meta.txt"

VERDICT=""; CODE=""; REASON=""; SAMPLER_BG=""; LOOP_PID=""
finish() {   # $1 verdict $2 exit code $3 reason
  VERDICT=$1; CODE=$2; REASON=$3
  exit "$CODE"
}
# The sampler (PowerShell Add-Content) briefly locks watch.csv while appending: reads can fail with "Device or resource
# busy". Every read works on a snapshot copied with retries; no snapshot = an explicit stop, never an empty "all OK".
snap_csv() {   # $1 source, $2 destination
  local i
  for i in 1 2 3 4 5 6 7 8 9 10; do cp "$1" "$2" 2>/dev/null && return 0; sleep 0.2; done
  return 1
}
sampler_is_ours() {   # the Windows process $1 exists AND is an editor_watch.ps1 writing into THIS record dir
  local cl; cl=$(powershell -NoProfile -Command "(Get-CimInstance Win32_Process -Filter 'ProcessId=$1').CommandLine" 2>/dev/null)
  [[ "$cl" == *editor_watch.ps1* && "$cl" == *"$(basename "$RD")"* ]]
}
cleanup() {
  local sp="" alive=no i
  if [ -n "$LOOP_PID" ] && kill -0 "$LOOP_PID" 2>/dev/null; then   # stop the loop FIRST: no submission after we give up
    kill -TERM "$LOOP_PID" 2>/dev/null
    for i in 1 2 3 4 5 6 7 8 9 10; do kill -0 "$LOOP_PID" 2>/dev/null || break; sleep 0.5; done
    kill -KILL "$LOOP_PID" 2>/dev/null
    echo "loop_pid=$LOOP_PID killed_by_entry_cleanup" >> "$RD/meta.txt"
  fi
  [ -s "$RD/sampler.pid" ] && sp=$(tr -d '\r\n ' < "$RD/sampler.pid")
  if [[ "$sp" =~ ^[0-9]+$ ]]; then
    if [ -n "$SAMPLER_CMD" ]; then          # custom/fake sampler: its pid file holds a shell pid
      kill "$sp" 2>/dev/null; sleep 0.2; kill -0 "$sp" 2>/dev/null && alive=yes
    else                                    # real sampler: a Windows pid -- only kill it if it IS our editor_watch
      if sampler_is_ours "$sp"; then taskkill //PID "$sp" //F >/dev/null 2>&1; sleep 1; fi
      sampler_is_ours "$sp" && alive=yes
    fi
  fi
  [ -n "$SAMPLER_BG" ] && { kill "$SAMPLER_BG" 2>/dev/null; wait "$SAMPLER_BG" 2>/dev/null; }
  [ -z "$VERDICT" ] && { VERDICT="ABORTED"; CODE=${CODE:-99}; REASON="entry exited unexpectedly"; }
  { echo "verdict=$VERDICT"; echo "exit=$CODE"; echo "reason=$REASON"; echo "end_utc=$(date -u +%FT%TZ)"
    echo "sampler_pid=${sp:-none} sampler_still_alive=$alive"; [ -s "$RD/report.txt" ] && cat "$RD/report.txt"; } > "$RD/summary.txt"
  echo "==== safe_unity_test: $VERDICT (exit $CODE) ===="
  [ -n "$REASON" ] && echo "reason: $REASON"
  [ -s "$RD/report.txt" ] && cat "$RD/report.txt"
  [ "$alive" = yes ] && echo "WARNING: sampler pid $sp is still running -- stop it by hand"
  echo "record: $RD"
}
trap cleanup EXIT
trap 'finish ABORTED 130 "entry interrupted by a signal; guarded loop and sampler stopped"' INT TERM HUP

# ---------- 2. preflight: the Editor must be THIS project's ----------
timeout 60 $UNITY status --json > "$RD/preflight-status.json" 2>&1
PID=$("$PY" -I "$J" status "$RD/preflight-status.json" "$PROJECT_PATH") || finish PREFLIGHT_FAILED 32 "editor-not-ready-for-this-project:$PID"

# ---------- 3. sampler ----------
RUN_TIMEOUT_EFF=${TIMEOUT:-$([ "$MODE" = EditMode ] && echo 600 || echo 1200)}
SAMPLER_MIN=$(( (RUN_TIMEOUT_EFF + 600 + 59) / 60 ))
if [ -n "$SAMPLER_CMD" ]; then
  $SAMPLER_CMD "$RD/watch.csv" "$RD/sampler.pid" "$SAMPLE_INTERVAL" "$SAMPLER_MIN" > "$RD/sampler.stdout" 2>&1 &
else
  powershell -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$HERE/editor_watch.ps1")" \
    -Out "$(cygpath -w "$RD/watch.csv")" -IntervalSec "$SAMPLE_INTERVAL" -MaxMinutes "$SAMPLER_MIN" \
    -PidFile "$(cygpath -w "$RD/sampler.pid")" > "$RD/sampler.stdout" 2>&1 &
fi
SAMPLER_BG=$!
healthy=""; s0=$(date +%s)
while [ $(( $(date +%s) - s0 )) -le "$SAMPLER_START_TIMEOUT" ]; do
  kill -0 "$SAMPLER_BG" 2>/dev/null || finish SAMPLER_UNHEALTHY 30 "sampler exited during start-up: $(tail -c 300 "$RD/sampler.stdout" 2>/dev/null | tr '\r\n' '  ')"
  if [ -s "$RD/watch.csv" ] && snap_csv "$RD/watch.csv" "$RD/.watch.snap" && [ "$(grep -c '^[0-9]' "$RD/.watch.snap")" -ge 1 ]; then
    last=$(grep '^[0-9]' "$RD/.watch.snap" | tail -n 1 | tr -d '\r'); lastutc=${last%%,*}
    bad=$(tr -d '\r' < "$RD/.watch.snap" | awk -F',' 'NR>1 && $1 ~ /^[0-9]/ && $NF!="OK"{print $NF; exit}')
    [ -n "$bad" ] && finish SAMPLER_UNHEALTHY 30 "sampler reports $bad on its first samples (e.g. a flood already in Editor.log)"
    tr -d '\r' < "$RD/.watch.snap" | awk -F',' -v t="$lastutc" -v p="$PID" '$1==t && $3==p && $4=="editor"{f=1} END{exit !f}' \
      && { WATCH_LINE_START=$(wc -l < "$RD/.watch.snap" | tr -d ' '); healthy=yes; break; }
  fi
  sleep 1
done
[ -n "$healthy" ] || finish SAMPLER_UNHEALTHY 30 "no healthy sample naming editor pid $PID within ${SAMPLER_START_TIMEOUT}s"
# WATCH_LINE_START (set above) = rows already audited; the loop judges every row after it

# ---------- 3b. compile the CURRENT sources (a test of stale assemblies would be a false PASS) ----------
if [ $CHECK -eq 0 ]; then
  timeout 120 $UNITY cmd recompile --json > "$RD/recompile-trigger.json" 2>&1
  [ $? -eq 124 ] && finish SAFETY_STOP 26 "recompile-trigger-timeout"
  st=$("$PY" -I "$J" recompile-trigger "$RD/recompile-trigger.json" "$PROJECT_PATH") || finish SAFETY_STOP 14 "recompile-refused:$st"
  echo "recompile_trigger=$st" >> "$RD/meta.txt"
  if [ "$st" = compiling ]; then
    c0=$(date +%s)
    while :; do
      sleep 2
      timeout 60 $UNITY cmd recompile_status --json > "$RD/recompile-poll.json" 2>&1
      r=$("$PY" -I "$J" recompile-poll "$RD/recompile-poll.json" "$PROJECT_PATH"); prc=$?
      [ $prc -eq 0 ] && break
      [ $prc -eq 10 ] || finish SAFETY_STOP 14 "compile-not-clean:$r"
      [ $(( $(date +%s) - c0 )) -le "$RECOMPILE_TIMEOUT" ] || finish SAFETY_STOP 26 "recompile-timeout(${RECOMPILE_TIMEOUT}s)"
    done
    echo "recompile_result=$r" >> "$RD/meta.txt"
    # 'completed' is written BEFORE the domain reload: wait until the main thread answers again (bounded)
    PCS="$RD/ping_probe.cs"; cp "$PING_FILE" "$PCS"; command -v cygpath >/dev/null && PCS="$(cygpath -m "$PCS")"
    while :; do
      timeout 30 $UNITY cmd eval_file --file "$PCS" --json > "$RD/recompile-ping.json" 2>&1
      "$PY" -I "$J" ping "$RD/recompile-ping.json" "$PROJECT_PATH" >/dev/null && break
      [ $(( $(date +%s) - c0 )) -le "$RECOMPILE_TIMEOUT" ] || finish SAFETY_STOP 26 "editor-not-back-after-recompile(${RECOMPILE_TIMEOUT}s)"
      sleep 2
    done
  fi
fi

# ---------- 4. guarded loop (one run) ----------
LOOP_ENV=(WATCH_CSV="$RD/watch.csv" MODE="$MODE" TEST_FILTER="$FILTER" INCLUDE_EXPLICIT="$EXPLICIT"
          RUN_DIR="$RD" MAX_INVALID=1 PROJECT_PATH="$PROJECT_PATH"
          WATCH_MAX_AGE="${WATCH_MAX_AGE:-$(( SAMPLE_INTERVAL * 4 + 10 ))}" SETTLE_SEC="${SETTLE_SEC:-5}"
          RUN_TIMEOUT="$RUN_TIMEOUT_EFF" WATCH_LINE_START="$WATCH_LINE_START")
[ $CHECK -eq 1 ] && LOOP_ENV+=(CHECK_ONLY=1)
env "${LOOP_ENV[@]}" bash "$LOOP" "$LABEL" 1 1 "$RD/loop.txt" 2> "$RD/loop.stderr" &
LOOP_PID=$!; echo "loop_pid=$LOOP_PID" >> "$RD/meta.txt"
wait "$LOOP_PID"; rc=$?
LOOP_PID=""
stopline=$(grep -o 'STOP .*' "$RD/loop.txt" 2>/dev/null | head -1)
TS="$RD/${LABEL}1-test_status.json"
# a run may have completed before a later safety stop: always show its results (they are not swallowed by the stop)
[ $rc -ne 0 ] && [ -s "$TS" ] && "$PY" -I "$J" report "$TS" "$PROJECT_PATH" > "$RD/report.txt"
case $rc in
  0) ;;
  2) finish CONFIG_ERROR 2 "$(tr '\r\n' '  ' < "$RD/loop.stderr")${stopline:+ $stopline}" ;;
  *) finish SAFETY_STOP "$rc" "${stopline:-guarded loop exited $rc without a STOP line}" ;;
esac
[ $CHECK -eq 1 ] && finish CHECK_OK 0 "all pre-run checks passed; nothing was submitted"

# ---------- 5. result ----------
[ -s "$TS" ] || finish RESULT_MISSING_OR_MALFORMED 4 "the run completed but no test_status JSON was saved"
"$PY" -I "$J" report "$TS" "$PROJECT_PATH" > "$RD/report.txt"; trc=$?

# ---------- 6. sampler audit: every row during the run must be OK ----------
snap_csv "$RD/watch.csv" "$RD/.watch.snap" || finish SAMPLER_ALERT 31 "sampler CSV could not be read for the final audit"
bad=$(tr -d '\r' < "$RD/.watch.snap" | awk -F',' 'NR>1 && $1 ~ /^[0-9]/ && $NF!="OK"{print $NF; exit}')
[ -n "$bad" ] && finish SAMPLER_ALERT 31 "sampler reported $bad during the run (test result kept in report)"
grep -q '^# stopped' "$RD/.watch.snap" && finish SAMPLER_ALERT 31 "sampler stopped during the run: $(grep '^# stopped' "$RD/.watch.snap" | head -1)"

case $trc in
  0) finish PASS 0 "" ;;
  1) finish TESTS_FAILED 1 "the run completed with failing tests (listed below)" ;;
  3) finish NO_TESTS_MATCHED 3 "the filter selected no tests" ;;
  5) finish NOTHING_EXECUTED 5 "every selected test was skipped/ignored -- nothing was verified" ;;
  *) finish RESULT_MISSING_OR_MALFORMED 4 "$(head -1 "$RD/report.txt")" ;;
esac
