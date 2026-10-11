#!/bin/bash
# Guarded Unity test loop -- FAIL-CLOSED (docs/53 §15, §17). Despite the historical file name it runs EditMode or
# PlayMode. Normal use is through safe_unity_test.sh (which starts the sampler and interprets results); call this
# directly only for repeated runs (N > 1).
#
# Before AND after every run, all of these must positively succeed, or the loop writes "STOP <reason>" and exits
# non-zero WITHOUT submitting another run (no retry, no restart):
#   - `unity status --json` answers within CLI_TIMEOUT; exactly one instance of PROJECT_PATH, state 'ready'
#   - the Editor MAIN thread answers ping_probe (2 attempts) and reports isCompiling=False, compileFailed=False,
#     isUpdating=False (and isPlaying=False before a run)                     else STOP editor-unresponsive-or-busy
#   - Editor.log readable; flood count and size are integers                  else STOP log-unreadable / monitor-error:*
#   - flood count == 0 (whole-log grep: the authoritative flood gate), size <= LOG_LIMIT_BYTES
#   - Editor private bytes is a number <= PRIVATE_LIMIT_GB
#   - sampler CSV (WATCH_CSV) exists, not stopped, last row well-formed and <= WATCH_MAX_AGE s old, contains this
#     Editor's pid as role=editor, and every row since the previous check has status exactly "OK"
#   - recompile_status: state in {idle, completed, up_to_date} AND failed=false AND errors=[] AND
#     compilationFailed=false (never the state name alone)                    else STOP compile-not-clean
#   - before a run only: test_status shows no run in flight (a timed-out / orphaned earlier run blocks new
#     submissions until it finishes)                                          else STOP previous-run-in-flight (25)
# A run is accepted only if run_tests answers success:true/result:"running" (a rejected request has an OUTER
# success:true -- the inner flag is checked). "completed" counts only if this run was seen running OR the status file
# was written after submission (a request dropped by a domain reload leaves the previous run's file behind).
# During a run: CLI timeout, vanished Editor, test_status error/cancelled/malformed, or RUN_TIMEOUT -> STOP (the
# in-flight run is not cancelled; only further submissions are prevented). A run that never starts within
# START_GRACE is INVALID; MAX_INVALID consecutive INVALIDs -> STOP.
# Every submission is logged as "SUBMIT <label><k>" BEFORE the request is sent.
# Configuration errors exit 2 before anything runs. One loop at a time (mkdir lock LOCK_DIR; held -> exit 27).
# SIGINT/SIGTERM/SIGHUP -> exit via the EXIT trap (lock released, temp removed); nothing further is submitted.
# All JSON is parsed by unity_test_json.py from files; user input is never spliced into a shell command.
#
# usage: playmode_ab_loop.sh <label> <first_index> <N_valid> <out>
# env:   WATCH_CSV (required)  MODE=EditMode|PlayMode  TEST_FILTER  INCLUDE_EXPLICIT=0|1  RUN_TIMEOUT
#        RUN_DIR (save each run's test_status JSON)  PROJECT_PATH  STATUS_FILE  CLI_TIMEOUT  LOGFILE
#        LOG_LIMIT_BYTES  PRIVATE_LIMIT_GB  WATCH_MAX_AGE  SETTLE_SEC  POLL_SEC  START_GRACE  MAX_INVALID
#        PING_FILE  LOCK_DIR  UNITY_CLI  PYTHON  PRIVATE_GB_CMD
#        WATCH_LINE_START  sampler CSV line count already audited by the caller (rows after it must all be OK)
#        CHECK_ONLY=1  run every pre-run check once and exit 0 -- submits nothing
export PATH="$PATH:$HOME/AppData/Local/Unity/bin"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
UNITY="${UNITY_CLI:-unity}"
PY="${PYTHON:-python}"
J="$HERE/unity_test_json.py"
MODE="${MODE:-PlayMode}"
TEST_FILTER="${TEST_FILTER:-}"
INCLUDE_EXPLICIT="${INCLUDE_EXPLICIT:-0}"
CLI_TIMEOUT="${CLI_TIMEOUT:-60}"
L="${LOGFILE:-$REPO/Logs/Editor.log}"
LOG_LIMIT_BYTES="${LOG_LIMIT_BYTES:-524288000}"
PRIVATE_LIMIT_GB="${PRIVATE_LIMIT_GB:-12}"
WATCH_MAX_AGE="${WATCH_MAX_AGE:-60}"
SETTLE_SEC="${SETTLE_SEC:-20}"
POLL_SEC="${POLL_SEC:-5}"
START_GRACE="${START_GRACE:-60}"
MAX_INVALID="${MAX_INVALID:-2}"
PING_FILE="${PING_FILE:-$HERE/ping_probe.cs.txt}"
LOCK_DIR="${LOCK_DIR:-$REPO/Logs/.playmode_ab_loop.lock}"
STATUS_FILE="${STATUS_FILE:-$REPO/Temp/pipeline_test_status.json}"
if [ -z "$PROJECT_PATH" ]; then
  if command -v cygpath >/dev/null; then PROJECT_PATH="$(cygpath -w "$REPO")"; else PROJECT_PATH="$REPO"; fi
fi
# Must equal editor_watch.ps1's -Marker default (same flood line, docs/53 §3).
MARK='Access version should be odd'
label=$1; k=$2; need=$3; out=$4
[ -n "$label" ] && [ -n "$k" ] && [ -n "$need" ] && [ -n "$out" ] || { echo "usage: $0 <label> <first> <N> <out>" >&2; exit 2; }

# ---- configuration validation: refuse to run rather than run with a disabled threshold or an unvetted argument ----
cfg() { echo "config error: $*" >&2; exit 2; }
[ -n "${DRYRUN:-}" ] && cfg "DRYRUN was removed; use CHECK_ONLY=1 (checks only, submits nothing)"
isint() { [[ "$1" =~ ^[0-9]+$ ]]; }
isnum() { [[ "$1" =~ ^[0-9]+([.][0-9]+)?$ ]]; }
case "$MODE" in EditMode) MODE_CLI=editmode; DEF_TIMEOUT=600 ;; PlayMode) MODE_CLI=playmode; DEF_TIMEOUT=1200 ;; *) cfg "MODE='$MODE' (EditMode|PlayMode)" ;; esac
RUN_TIMEOUT="${RUN_TIMEOUT:-$DEF_TIMEOUT}"
for v in CLI_TIMEOUT LOG_LIMIT_BYTES WATCH_MAX_AGE SETTLE_SEC POLL_SEC START_GRACE RUN_TIMEOUT MAX_INVALID k need; do
  isint "${!v}" || cfg "$v='${!v}' is not a non-negative integer"
done
isnum "$PRIVATE_LIMIT_GB" || cfg "PRIVATE_LIMIT_GB='$PRIVATE_LIMIT_GB' is not a number"
[ "$RUN_TIMEOUT" -ge 10 ] || cfg "RUN_TIMEOUT must be >= 10"
case "$INCLUDE_EXPLICIT" in 0|1) ;; *) cfg "INCLUDE_EXPLICIT='$INCLUDE_EXPLICIT' (0|1)" ;; esac
"$PY" -I "$J" validate-filter probe >/dev/null 2>&1 || cfg "python ($PY) cannot run $J"
if [ -n "$TEST_FILTER" ]; then "$PY" -I "$J" validate-filter "$TEST_FILTER" >/dev/null || cfg "TEST_FILTER rejected (allowed: letters, digits, _ . , : ( ) < > \" ' * + ? | [ ] / = - space; 1-400 chars; not starting with -)"; fi
[ "$INCLUDE_EXPLICIT" = 1 ] && [ -z "$TEST_FILTER" ] && cfg "INCLUDE_EXPLICIT=1 requires TEST_FILTER (never run every [Explicit] probe at once)"

stop() { echo "$label $1 STOP $2" >> "$out"; exit "${3:-20}"; }

# ---- single instance ----
mkdir "$LOCK_DIR" 2>/dev/null || stop init "another-loop-running-or-stale-lock(remove $LOCK_DIR only if no loop is running)" 27
T="$(mktemp -d)"
trap 'rmdir "$LOCK_DIR" 2>/dev/null; rm -rf "$T"' EXIT
trap 'exit 143' TERM INT HUP

# the real CLI only accepts eval_file paths ending in .cs, and needs a Windows-style path (repo stores probes as .cs.txt)
PING_CS="$T/ping_probe.cs"; cp "$PING_FILE" "$PING_CS" 2>/dev/null || stop init "ping-file-unreadable" 16
command -v cygpath >/dev/null && PING_CS="$(cygpath -m "$PING_CS")"

cli() { local o=$1; shift; timeout "$CLI_TIMEOUT" $UNITY "$@" --json > "$o" 2>&1; }   # args stay separate words
js() { "$PY" -I "$J" "$@"; }
private_gb() {
  if [ -n "$PRIVATE_GB_CMD" ]; then $PRIVATE_GB_CMD "$1"
  else powershell -NoProfile -Command "\$p=Get-Process -Id $1 -ErrorAction Stop; [math]::Round(\$p.PrivateMemorySize64/1GB,2)" 2>/dev/null; fi
}
editor_pid() {   # $1 tag -> prints pid or stops
  local p; cli "$T/status.json" status; [ $? -eq 124 ] && stop "$1" "cli-timeout:status" 10
  p=$(js status "$T/status.json" "$PROJECT_PATH") || stop "$1" "editor-not-ready:$p" 10
  echo "$p"
}
# The sampler (PowerShell Add-Content) briefly locks watch.csv while appending: reads can fail with "Device or resource
# busy". Every read works on a snapshot copied with retries; no snapshot = an explicit stop, never an empty "all OK".
snap_csv() {   # $1 source, $2 destination
  local i
  for i in 1 2 3 4 5 6 7 8 9 10; do cp "$1" "$2" 2>/dev/null && return 0; sleep 0.2; done
  return 1
}
WATCH_LINE="${WATCH_LINE_START:-}"
[ -z "$WATCH_LINE" ] || isint "$WATCH_LINE" || cfg "WATCH_LINE_START='$WATCH_LINE' is not an integer"

check() {   # $1 = phase tag; $2 = "pre" (must not be in Play Mode) or "post". Failure to OBTAIN a reading is a stop.
  local tag=$1 phase=$2 rc pid r fl sz pg last lastutc age bad n np=""
  pid=$(editor_pid "$tag") || exit $?
  [ "$phase" = pre ] && np=not-playing
  cli "$T/ping.json" cmd eval_file --file "$PING_CS"; [ $? -eq 124 ] && stop "$tag" "cli-timeout:ping" 10
  if ! r=$(js ping "$T/ping.json" "$PROJECT_PATH" $np); then
    sleep 5; cli "$T/ping.json" cmd eval_file --file "$PING_CS"
    r=$(js ping "$T/ping.json" "$PROJECT_PATH" $np) || stop "$tag" "editor-unresponsive-or-busy:$r" 15
  fi
  [ -r "$L" ] || stop "$tag" "log-unreadable" 16
  fl=$(grep -c "$MARK" "$L" 2>/dev/null); rc=$?
  { [ $rc -le 1 ] && isint "$fl"; } || stop "$tag" "monitor-error:flood-count(rc=$rc)" 16
  sz=$(stat -c %s "$L" 2>/dev/null); isint "$sz" || stop "$tag" "monitor-error:log-size" 16
  pg=$(private_gb "$pid"); isnum "$pg" || stop "$tag" "monitor-error:private-read('$pg')" 16
  # ---- sampler health ----
  [ -n "$WATCH_CSV" ] || stop "$tag" "no-watcher(WATCH_CSV unset)" 17
  [ -r "$WATCH_CSV" ] || stop "$tag" "watcher-csv-unreadable" 17
  snap_csv "$WATCH_CSV" "$T/watch.snap" || stop "$tag" "watcher-csv-unreadable(locked)" 17
  last=$(tail -n 1 "$T/watch.snap" | tr -d '\r')
  [[ "$last" == \#* ]] && stop "$tag" "watcher-stopped:${last#\# }" 17
  lastutc=${last%%,*}
  [[ "$lastutc" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?Z$ ]] || stop "$tag" "watcher-bad-row('${last:0:40}')" 17
  age=$(( $(date -u +%s) - $(date -u -d "$lastutc" +%s 2>/dev/null || echo 0) ))
  [ "$age" -le "$WATCH_MAX_AGE" ] || stop "$tag" "watcher-stale(${age}s)" 17
  tr -d '\r' < "$T/watch.snap" | awk -F',' -v t="$lastutc" -v p="$pid" '$1==t && $3==p && $4=="editor"{f=1} END{exit !f}' \
    || stop "$tag" "watcher-pid-mismatch(editor pid $pid not in last sample)" 17
  n=$(wc -l < "$T/watch.snap" | tr -d ' ')
  if [ -z "$WATCH_LINE" ]; then   # first check: judge the last sample only (older history predates this loop)
    bad=$(tr -d '\r' < "$T/watch.snap" | awk -F',' -v t="$lastutc" '$1==t && $NF!="OK"{print $NF; exit}')
  else                            # later checks: every row written since the previous check
    bad=$(tr -d '\r' < "$T/watch.snap" | awk -F',' -v from="$WATCH_LINE" 'NR>from && NR>1 && $NF!="OK"{print $NF; exit}')
  fi
  [ -z "$bad" ] || stop "$tag" "watcher-status:$bad" 17
  WATCH_LINE=$n
  echo "$label $tag pid=$pid flood_total=$fl log_bytes=$sz private_gb=$pg watcher_age=${age}s" >> "$out"
  [ "$fl" -gt 0 ] && stop "$tag" "flood-onset" 11
  [ "$sz" -gt "$LOG_LIMIT_BYTES" ] && stop "$tag" "log-limit" 12
  awk -v a="$pg" -v b="$PRIVATE_LIMIT_GB" 'BEGIN{exit !(a+0>b+0)}' && stop "$tag" "private-limit" 13
  cli "$T/recompile.json" cmd recompile_status; [ $? -eq 124 ] && stop "$tag" "cli-timeout:recompile_status" 10
  r=$(js recompile "$T/recompile.json" "$PROJECT_PATH") || stop "$tag" "compile-not-clean:$r" 14
  if [ "$phase" = pre ]; then
    cli "$T/idle.json" cmd test_status; [ $? -eq 124 ] && stop "$tag" "cli-timeout:test_status" 10
    r=$(js idle-for-submit "$T/idle.json" "$PROJECT_PATH") || stop "$tag" "$r" 25
  fi
  return 0
}

if [ "${CHECK_ONLY:-0}" = "1" ]; then check "$k-checkonly" pre; echo "$label check-only passed" >> "$out"; exit 0; fi

# run_tests arguments are built as an array of separate words -- the filter is never re-parsed by a shell.
RUN_ARGS=(cmd run_tests --mode "$MODE_CLI" --async_tests true)
[ -n "$TEST_FILTER" ] && RUN_ARGS+=(--filter "$TEST_FILTER")
[ "$INCLUDE_EXPLICIT" = 1 ] && RUN_ARGS+=(--include_explicit true)

valid=0; attempts=0; invalid_streak=0
while [ $valid -lt $need ] && [ $attempts -lt $((need+3)) ]; do
  attempts=$((attempts+1))
  sleep "$SETTLE_SEC"                     # let the previous run's play-mode exit / domain reload settle
  check "$k-pre" pre
  t0=$(date -u +%H:%M:%S); s0=$(date +%s)
  echo "SUBMIT $label$k mode=$MODE filter='${TEST_FILTER}' include_explicit=$INCLUDE_EXPLICIT" >> "$out"
  cli "$T/run.json" "${RUN_ARGS[@]}"; rc=$?
  [ $rc -eq 124 ] && stop "$k-run" "cli-timeout:run_tests" 10
  [ -n "$RUN_DIR" ] && cp "$T/run.json" "$RUN_DIR/$label$k-run_tests.json"
  r=$(js run-accepted "$T/run.json" "$PROJECT_PATH") || stop "$k-run" "run-not-accepted:$r" 22
  seen=0; done_=0
  while :; do
    el=$(( $(date +%s)-s0 ))
    cli "$T/ts.json" cmd test_status; [ $? -eq 124 ] && stop "$k-run" "cli-timeout:test_status" 10
    st=$(js test-status "$T/ts.json" "$PROJECT_PATH")
    case "$st" in
      running) seen=1 ;;
      completed)
        fresh=0
        [ $seen -eq 1 ] && fresh=1
        m=$(stat -c %Y "$STATUS_FILE" 2>/dev/null); isint "$m" && [ "$m" -ge "$s0" ] && fresh=1
        [ $fresh -eq 1 ] && { done_=1; break; } ;;      # otherwise: a previous run's file -- keep waiting
      error:*|cancelled) stop "$k-run" "run-$st" 23 ;;
      malformed:*) stop "$k-run" "result-$st" 24 ;;
      no_tests|unavailable:*|unknown:*) ;;              # transient (e.g. PlayMode domain reload); bounded below
      *) stop "$k-run" "test-status-parser:$st" 24 ;;
    esac
    [ $seen -eq 0 ] && [ $el -gt "$START_GRACE" ] && break
    [ $el -gt "$RUN_TIMEOUT" ] && stop "$k-run" "run-timeout(${el}s>${RUN_TIMEOUT}s)" 18
    editor_pid "$k-run" >/dev/null || exit $?
    sleep "$POLL_SEC"
  done
  if [ $done_ -eq 0 ]; then
    echo "$label$k start=${t0}Z INVALID (run did not start) dur=$(( $(date +%s)-s0 ))s" >> "$out"
    invalid_streak=$((invalid_streak+1))
    [ $invalid_streak -ge "$MAX_INVALID" ] && stop "$k-run" "consecutive-invalid-runs($invalid_streak)" 19
  else
    [ -n "$RUN_DIR" ] && cp "$T/ts.json" "$RUN_DIR/$label$k-test_status.json"
    sum=$(js report "$T/ts.json" "$PROJECT_PATH" | head -1)
    echo "$label$k start=${t0}Z dur=$(( $(date +%s)-s0 ))s $sum" >> "$out"; valid=$((valid+1)); invalid_streak=0
  fi
  check "$k-post" post
  k=$((k+1))
done
echo "$label done valid=$valid attempts=$attempts" >> "$out"
[ $valid -ge $need ] || exit 21
