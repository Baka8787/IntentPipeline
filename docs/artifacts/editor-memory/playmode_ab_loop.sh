#!/bin/bash
# Run the PlayMode suite until N valid runs complete (max N+3 attempts) -- FAIL-CLOSED (docs/53 §15).
#
# Before AND after every run, all of these must positively succeed, or the loop writes "STOP <reason>" and exits
# non-zero WITHOUT submitting another run (no retry, no restart):
#   - `unity status` answers within CLI_TIMEOUT and lists a 'ready' Editor whose
#     project column contains PROJECT_MATCH                                           else STOP cli-timeout / editor-not-ready
#   - the Editor MAIN thread answers a ping eval (2 attempts: a long-idle Editor
#     can miss the first one)                                                         else STOP editor-unresponsive
#   - Editor.log is readable; flood-marker count and size are real numbers            else STOP log-unreadable / monitor-error:*
#   - flood count == 0 (whole-log grep: THIS is the authoritative flood gate; the
#     sampler's marker count is a second, advisory signal), log size <= limit         else STOP flood-onset / log-limit
#   - Editor private bytes is a real number and <= PRIVATE_LIMIT_GB                   else STOP monitor-error:private-read / private-limit
#   - the sampler (editor_watch.ps1, WATCH_CSV) exists, has not stopped, its last
#     sample is <= WATCH_MAX_AGE s old and contains this Editor's pid as role=editor,
#     and EVERY row written since the previous check has status exactly "OK"
#     (first check: the last sample only)                                            else STOP no-watcher / watcher-*
#   - recompile_status is idle                                                        else STOP recompile-not-idle
# During a run: a CLI timeout, a vanished Editor, or no completion within RUN_TIMEOUT -> STOP (the in-flight run is
# not cancelled; only further submissions are prevented). A run that never starts within START_GRACE is logged
# INVALID; two consecutive INVALIDs -> STOP.
# Every submission is logged as "SUBMIT <label><k>" BEFORE the request is sent, so "no further submission after a
# stop" can be checked from the output file alone.
# Configuration errors (non-numeric thresholds, the removed DRYRUN mode) exit 2 before anything runs.
# Only one loop may run at a time (mkdir lock LOCK_DIR; a stale lock after a killed loop must be removed by hand).
#
# usage: playmode_ab_loop.sh <label> <first_index> <N_valid> <out>
# env:   WATCH_CSV (required)  UNITY_CLI  CLI_TIMEOUT  PRIVATE_GB_CMD  LOGFILE  LOG_LIMIT_BYTES  PRIVATE_LIMIT_GB
#        WATCH_MAX_AGE  SETTLE_SEC  POLL_SEC  START_GRACE  RUN_TIMEOUT  PING_FILE  PROJECT_MATCH  LOCK_DIR
#        CHECK_ONLY=1  run every pre-run check once against the live Editor and exit 0 -- submits nothing
export PATH="$PATH:$HOME/AppData/Local/Unity/bin"
HERE="$(cd "$(dirname "$0")" && pwd)"
UNITY="${UNITY_CLI:-unity}"
CLI_TIMEOUT="${CLI_TIMEOUT:-60}"
L="${LOGFILE:-$HERE/../../../Logs/Editor.log}"
LOG_LIMIT_BYTES="${LOG_LIMIT_BYTES:-524288000}"
PRIVATE_LIMIT_GB="${PRIVATE_LIMIT_GB:-12}"
WATCH_MAX_AGE="${WATCH_MAX_AGE:-60}"
SETTLE_SEC="${SETTLE_SEC:-20}"
POLL_SEC="${POLL_SEC:-5}"
START_GRACE="${START_GRACE:-60}"
RUN_TIMEOUT="${RUN_TIMEOUT:-600}"
PING_FILE="${PING_FILE:-$HERE/ping_probe.cs.txt}"
PROJECT_MATCH="${PROJECT_MATCH:-CharacterController}"
LOCK_DIR="${LOCK_DIR:-$HERE/../../../Logs/.playmode_ab_loop.lock}"
# Must equal editor_watch.ps1's -Marker default (same flood line, docs/53 §3).
MARK='Access version should be odd'
label=$1; k=$2; need=$3; out=$4
[ -n "$label" ] && [ -n "$k" ] && [ -n "$need" ] && [ -n "$out" ] || { echo "usage: $0 <label> <first> <N> <out>" >&2; exit 2; }

# ---- configuration validation: refuse to run rather than run with a disabled threshold ----
[ -n "${DRYRUN:-}" ] && { echo "DRYRUN was removed; use CHECK_ONLY=1 (checks only, submits nothing)" >&2; exit 2; }
isint() { [[ "$1" =~ ^[0-9]+$ ]]; }
isnum() { [[ "$1" =~ ^[0-9]+([.][0-9]+)?$ ]]; }
for v in CLI_TIMEOUT LOG_LIMIT_BYTES WATCH_MAX_AGE SETTLE_SEC POLL_SEC START_GRACE RUN_TIMEOUT k need; do
  isint "${!v}" || { echo "config error: $v='${!v}' is not a non-negative integer" >&2; exit 2; }
done
isnum "$PRIVATE_LIMIT_GB" || { echo "config error: PRIVATE_LIMIT_GB='$PRIVATE_LIMIT_GB' is not a number" >&2; exit 2; }

stop() { echo "$label $1 STOP $2" >> "$out"; exit "${3:-20}"; }

# ---- single instance ----
mkdir "$LOCK_DIR" 2>/dev/null || stop init "another-loop-running(lock $LOCK_DIR)" 2
trap 'rmdir "$LOCK_DIR" 2>/dev/null' EXIT

# the real CLI only accepts eval_file paths ending in .cs, and needs a Windows-style path (repo stores probes as .cs.txt)
PING_CS="$(mktemp -d)/ping_probe.cs"; cp "$PING_FILE" "$PING_CS" 2>/dev/null || stop init "ping-file-unreadable" 16
command -v cygpath >/dev/null && PING_CS="$(cygpath -m "$PING_CS")"

u() { timeout "$CLI_TIMEOUT" $UNITY "$@" 2>&1; }
private_gb() {
  if [ -n "$PRIVATE_GB_CMD" ]; then $PRIVATE_GB_CMD "$1"
  else powershell -NoProfile -Command "\$p=Get-Process -Id $1 -ErrorAction Stop; [math]::Round(\$p.PrivateMemorySize64/1GB,2)" 2>/dev/null; fi
}
WATCH_LINE=""   # CSV line count at the previous check; rows after it must all be OK

check() {   # $1 = phase tag. Any failure to OBTAIN a reading is itself a stop.
  local tag=$1 s rc pid r fl sz pg last lastutc age bad n
  s=$(u status); rc=$?
  [ $rc -eq 124 ] && stop "$tag" "cli-timeout:status" 10
  pid=$(echo "$s" | tr -d '\r' | awk -F'\t' -v pm="$PROJECT_MATCH" '$2=="ready" && index($3,pm)>0 {print $5}' | head -1)
  isint "$pid" || stop "$tag" "editor-not-ready" 10
  r=$(u cmd eval_file --file "$PING_CS")
  if ! echo "$r" | grep -q '"result":"pong'; then
    sleep 5; r=$(u cmd eval_file --file "$PING_CS")
    echo "$r" | grep -q '"result":"pong' || stop "$tag" "editor-unresponsive" 15
  fi
  [ -r "$L" ] || stop "$tag" "log-unreadable" 16
  fl=$(grep -c "$MARK" "$L" 2>/dev/null); rc=$?
  { [ $rc -le 1 ] && isint "$fl"; } || stop "$tag" "monitor-error:flood-count(rc=$rc)" 16
  sz=$(stat -c %s "$L" 2>/dev/null); isint "$sz" || stop "$tag" "monitor-error:log-size" 16
  pg=$(private_gb "$pid"); isnum "$pg" || stop "$tag" "monitor-error:private-read('$pg')" 16
  # ---- sampler health ----
  [ -n "$WATCH_CSV" ] || stop "$tag" "no-watcher(WATCH_CSV unset)" 17
  [ -r "$WATCH_CSV" ] || stop "$tag" "watcher-csv-unreadable" 17
  last=$(tail -n 1 "$WATCH_CSV" | tr -d '\r')
  [[ "$last" == \#* ]] && stop "$tag" "watcher-stopped:${last#\# }" 17
  lastutc=${last%%,*}
  [[ "$lastutc" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?Z$ ]] || stop "$tag" "watcher-bad-row('${last:0:40}')" 17
  age=$(( $(date -u +%s) - $(date -u -d "$lastutc" +%s 2>/dev/null || echo 0) ))
  [ "$age" -le "$WATCH_MAX_AGE" ] || stop "$tag" "watcher-stale(${age}s)" 17
  tr -d '\r' < "$WATCH_CSV" | awk -F',' -v t="$lastutc" -v p="$pid" '$1==t && $3==p && $4=="editor"{f=1} END{exit !f}' \
    || stop "$tag" "watcher-pid-mismatch(editor pid $pid not in last sample)" 17
  n=$(wc -l < "$WATCH_CSV" | tr -d ' ')
  if [ -z "$WATCH_LINE" ]; then   # first check: judge the last sample only (older history predates this loop)
    bad=$(tr -d '\r' < "$WATCH_CSV" | awk -F',' -v t="$lastutc" '$1==t && $NF!="OK"{print $NF; exit}')
  else                            # later checks: every row written since the previous check
    bad=$(tr -d '\r' < "$WATCH_CSV" | awk -F',' -v from="$WATCH_LINE" 'NR>from && NR>1 && $NF!="OK"{print $NF; exit}')
  fi
  [ -z "$bad" ] || stop "$tag" "watcher-status:$bad" 17
  WATCH_LINE=$n
  echo "$label $tag pid=$pid flood_total=$fl log_bytes=$sz private_gb=$pg watcher_age=${age}s" >> "$out"
  [ "$fl" -gt 0 ] && stop "$tag" "flood-onset" 11
  [ "$sz" -gt "$LOG_LIMIT_BYTES" ] && stop "$tag" "log-limit" 12
  awk -v a="$pg" -v b="$PRIVATE_LIMIT_GB" 'BEGIN{exit !(a+0>b+0)}' && stop "$tag" "private-limit" 13
  u cmd recompile_status | grep -q '"status":"idle"' || stop "$tag" "recompile-not-idle" 14
  return 0
}

if [ "${CHECK_ONLY:-0}" = "1" ]; then check "$k-checkonly"; echo "$label check-only passed" >> "$out"; exit 0; fi

valid=0; attempts=0; invalid_streak=0
while [ $valid -lt $need ] && [ $attempts -lt $((need+3)) ]; do
  attempts=$((attempts+1))
  sleep "$SETTLE_SEC"                     # let the previous run's play-mode exit / domain reload settle
  check "$k-pre"
  t0=$(date -u +%H:%M:%S); s0=$(date +%s)
  echo "SUBMIT $label$k" >> "$out"
  r=$(u cmd run_tests --mode PlayMode --async_tests true); rc=$?
  [ $rc -eq 124 ] && stop "$k-run" "cli-timeout:run_tests" 10
  seen=0; done_=0; st=""
  while :; do
    el=$(( $(date +%s)-s0 ))
    st=$(u cmd test_status); rc=$?
    [ $rc -eq 124 ] && stop "$k-run" "cli-timeout:test_status" 10
    echo "$st" | grep -q '"running"' && seen=1
    if [ $seen -eq 1 ] && echo "$st" | grep -q '"completed"'; then done_=1; break; fi
    [ $seen -eq 0 ] && [ $el -gt "$START_GRACE" ] && break
    [ $el -gt "$RUN_TIMEOUT" ] && stop "$k-run" "run-timeout(${el}s)" 18
    u status | tr -d '\r' | awk -F'\t' -v pm="$PROJECT_MATCH" '$2=="ready" && index($3,pm)>0 {f=1} END{exit !f}' \
      || stop "$k-run" "editor-not-ready" 10
    sleep "$POLL_SEC"
  done
  sum=$(echo "$st" | tr -d ' \r\n' | grep -o '"summary":{[^}]*}' | head -1)
  if [ $done_ -eq 0 ] || [ -z "$sum" ]; then
    echo "$label$k start=${t0}Z INVALID (run did not start / no summary) dur=$(( $(date +%s)-s0 ))s" >> "$out"
    invalid_streak=$((invalid_streak+1))
    [ $invalid_streak -ge 2 ] && stop "$k-run" "two-consecutive-invalid-runs" 19
  else
    echo "$label$k start=${t0}Z dur=$(( $(date +%s)-s0 ))s $sum" >> "$out"; valid=$((valid+1)); invalid_streak=0
  fi
  check "$k-post"
  k=$((k+1))
done
echo "$label done valid=$valid attempts=$attempts" >> "$out"
[ $valid -ge $need ] || exit 21
