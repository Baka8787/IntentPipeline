#!/bin/bash
# Self-test for playmode_ab_loop.sh (docs/53 §15): every stop path, driven by a fake `unity` CLI, a synthetic
# Editor.log and a synthetic sampler CSV. Never touches Unity. Exit 0 = all cases pass.
# Each case asserts: exit code, the STOP reason, and how many runs were SUBMITTED (no submission after a stop).
HERE="$(cd "$(dirname "$0")" && pwd)"
LOOP="$HERE/playmode_ab_loop.sh"
fails=0
# the repo path contains a space ("Unity Project"); UNITY_CLI is word-split by the loop, so give it a space-free wrapper
WRAP=$(mktemp -d)/fake-unity; printf '#!/bin/bash
exec bash "%s" "$@"
' "$HERE/selftest_fake_unity.sh" > "$WRAP"; chmod +x "$WRAP"
PGBCMD=$(dirname "$WRAP")/fake-pgb; printf '#!/bin/bash
echo "$PGB_VALUE"
' > "$PGBCMD"; chmod +x "$PGBCMD"   # ignores the pid argument
now_utc() { date -u +%Y-%m-%dT%H:%M:%S.0000000Z; }
watch_csv() {  # $1=file $2=status $3=utc (default now) $4=optional trailing line
  echo "utc,elapsed_s,pid,role,private_gb,working_set_gb,paged_gb,handles,threads,cpu_s,log_bytes,log_delta_bytes,marker_in_delta,sys_commit_gb,sys_commit_limit_gb,stop_reason,status" > "$1"
  echo "${3:-$(now_utc)},0,${WPID:-4242},editor,3.5,1,3.5,1,1,1,10,0,0,40,58,,$2" >> "$1"
  [ -n "$4" ] && echo "$4" >> "$1"
  return 0
}
# run_case <name> <expected_exit> <expected_reason_substring> <expected_submits> <N> [KEY=VAL ...]  (setup via $SETUP)
run_case() {
  local name=$1 want_rc=$2 want_reason=$3 want_sub=$4 n=$5; shift 5
  local d; d=$(mktemp -d)
  mkdir -p "$d/state"; echo 'clean start' > "$d/Editor.log"; watch_csv "$d/watch.csv" OK
  FAKE_STATE="$d/state" FAKE_LOG="$d/Editor.log" D="$d" eval "$SETUP"
  env FAKE_STATE="$d/state" FAKE_LOG="$d/Editor.log" FAKE_WATCH="$d/watch.csv" UNITY_CLI="$WRAP" \
      PROJECT_MATCH=FAKE LOCK_DIR="$d/lock" \
      LOGFILE="$d/Editor.log" WATCH_CSV="$d/watch.csv" PRIVATE_GB_CMD="$PGBCMD" PGB_VALUE="${PGB:-3.5}" \
      SETTLE_SEC=0 POLL_SEC=0 START_GRACE=1 RUN_TIMEOUT=20 CLI_TIMEOUT=3 PING_FILE="$HERE/ping_probe.cs.txt" "$@" \
      bash "$LOOP" T 1 "$n" "$d/out.txt" >/dev/null 2>&1
  local rc=$? sub=0
  touch "$d/out.txt"   # a config refusal exits before writing anything: that is 0 submissions
  sub=$(grep -c '^SUBMIT' "$d/out.txt")
  local reason; reason=$(grep -o 'STOP .*' "$d/out.txt" | head -1)
  if [ "$rc" = "$want_rc" ] && [ "$sub" = "$want_sub" ] && { [ -z "$want_reason" ] || [[ "$reason" == *"$want_reason"* ]]; }; then
    echo "PASS $name  rc=$rc submits=$sub ${reason}"
  else
    echo "FAIL $name  rc=$rc(want $want_rc) submits=$sub(want $want_sub) reason='$reason'(want '$want_reason')"; fails=$((fails+1))
    sed 's/^/      /' "$d/out.txt" | tail -5
  fi
  [ -n "$POSTCHECK" ] && { D="$d" eval "$POSTCHECK" || { echo "FAIL $name  postcheck: $POSTCHECK"; fails=$((fails+1)); }; }
  rm -rf "$d"
}

SETUP=':'                                                   ; run_case "L1 healthy -> 2 runs, exit 0"            0  ""                         2 2
SETUP='echo 1 > $FAKE_STATE/flood_on_submit'                ; run_case "L2 flood after run 1 -> stop, no run 2"   11 "flood-onset"              1 3
SETUP='echo 2000 > $FAKE_STATE/grow_on_submit'              ; run_case "L3 log limit after run 1 -> stop"         12 "log-limit"                1 3 LOG_LIMIT_BYTES=1000
PGB='13'; SETUP=':'                                    ; run_case "L4 private > limit -> stop before submit"  13 "private-limit"            0 2; PGB=
SETUP='echo 0 > $FAKE_STATE/ready'                          ; run_case "L5 editor not ready -> stop"               10 "editor-not-ready"         0 2
SETUP='echo timeout > $FAKE_STATE/eval'                     ; run_case "L6 main thread unresponsive -> stop"       15 "editor-unresponsive"      0 2
SETUP='touch $FAKE_STATE/status_hang'                       ; run_case "L7 CLI hangs -> timeout -> stop"           10 "cli-timeout:status"       0 2
SETUP='rm -f $FAKE_LOG'                                     ; run_case "L8 log unreadable -> stop"                 16 "log-unreadable"           0 2
PGB='n/a'; SETUP=':'                                   ; run_case "L9 memory read fails -> stop"              16 "monitor-error:private-read" 0 2; PGB=
SETUP='rm -f $D/watch.csv'                                  ; run_case "L10 sampler CSV missing -> stop"           17 "watcher-csv-unreadable"   0 2
SETUP='watch_csv $D/watch.csv OK 2020-01-01T00:00:00.0000000Z'; run_case "L11 sampler stale -> stop"              17 "watcher-stale"            0 2
SETUP='watch_csv $D/watch.csv ERROR:log-unreadable'         ; run_case "L12 sampler reports ERROR -> stop"         17 "watcher-status:ERROR"     0 2
SETUP='watch_csv $D/watch.csv OK "" "# stopped: max-minutes"'; run_case "L13 sampler stopped -> stop"             17 "watcher-stopped"          0 2
SETUP='watch_csv $D/watch.csv ALERT:delta-exceeds-scan-cap' ; run_case "L14 sampler partial scan -> stop"          17 "watcher-status:ALERT"     0 2
SETUP='touch $FAKE_STATE/die_during_run'                    ; run_case "L15 editor dies mid-run -> stop, no run 2" 10 "editor-not-ready"         1 3
SETUP='touch $FAKE_STATE/never_start'                       ; run_case "L16 runs never start -> 2 INVALID -> stop" 19 "two-consecutive-invalid"  2 3
SETUP='echo busy > $FAKE_STATE/recompile'                   ; run_case "L17 recompiling -> stop"                   14 "recompile-not-idle"       0 2
SETUP='touch $FAKE_STATE/hang_test_status'                  ; run_case "L18 test_status hangs -> stop, no run 2"   10 "cli-timeout:test_status"  1 3
SETUP=':'                                                   ; run_case "L19 WATCH_CSV unset -> stop"               17 "no-watcher"               0 2 WATCH_CSV=

SETUP=':'                                                   ; run_case "L20 CHECK_ONLY healthy -> exit 0, submits 0"  0 ""                       0 2 CHECK_ONLY=1
SETUP='echo 1 > $FAKE_STATE/ready; echo timeout > $FAKE_STATE/eval'; run_case "L21 CHECK_ONLY still fails closed"  15 "editor-unresponsive"     0 2 CHECK_ONLY=1

# --- added after the independent review (docs/53 §15.7) ---
SETUP='touch $FAKE_STATE/stuck_running'                     ; run_case "L22 run never completes -> run-timeout"      18 "run-timeout"            1 3 RUN_TIMEOUT=3
SETUP='touch $FAKE_STATE/hang_run_tests'                    ; run_case "L23 run_tests hangs -> stop, no run 2"      10 "cli-timeout:run_tests"  1 3
SETUP='touch $FAKE_STATE/watch_inject'                      ; run_case "L24 transient sampler ERROR mid-run -> stop" 17 "watcher-status:ERROR"   1 3
SETUP=':'                                                   ; run_case "L25 legacy DRYRUN=1 -> refuse, no run"       2  ""                       0 2 DRYRUN=1
SETUP=':'                                                   ; run_case "L26 LOG_LIMIT_BYTES=1kB -> refuse"           2  ""                       0 2 LOG_LIMIT_BYTES=1kB
SETUP=':'                                                   ; run_case "L27 PRIVATE_LIMIT_GB=abc -> refuse"          2  ""                       0 2 PRIVATE_LIMIT_GB=abc
SETUP='WPID=11111 watch_csv $D/watch.csv OK'                ; run_case "L28 sampler watches another pid -> stop"     17 "watcher-pid-mismatch"   0 2
SETUP=':'                                                   ; run_case "L29 Editor of another project -> stop"       10 "editor-not-ready"       0 2 PROJECT_MATCH=CharacterController
SETUP='mkdir $D/lock'; POSTCHECK='[ -d $D/lock ]'           ; run_case "L30 second loop -> refuse, keeps other lock" 2  "another-loop-running"   0 2; POSTCHECK=
SETUP='watch_csv $D/watch.csv OK "" "garbage-without-timestamp,OK"'; run_case "L31 malformed last CSV row -> stop"  17 "watcher-bad-row"        0 2
SETUP=':'; POSTCHECK='[ ! -d $D/lock ]'                     ; run_case "L32 lock released after normal exit"        0  ""                       2 2; POSTCHECK=

echo "loop selftest: $fails failure(s)"
exit $fails
