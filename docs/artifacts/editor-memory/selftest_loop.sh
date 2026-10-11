#!/bin/bash
# Self-test for playmode_ab_loop.sh (docs/53 §15, §17): every stop path, driven by a fake `unity` CLI that emits the
# real --json envelope shapes, a synthetic Editor.log and a synthetic sampler CSV. Never touches Unity.
# Each case asserts: exit code, the STOP reason, and how many runs were SUBMITTED (no submission after a stop);
# some cases also assert the exact argv the CLI received (POSTCHECK). Exit 0 = all cases pass.
HERE="$(cd "$(dirname "$0")" && pwd)"
LOOP="$HERE/playmode_ab_loop.sh"
fails=0
# the repo path contains a space ("Unity Project"); UNITY_CLI is word-split by the loop, so give it a space-free wrapper
WRAPD=$(mktemp -d)
WRAP=$WRAPD/fake-unity; printf '#!/bin/bash\nexec bash "%s" "$@"\n' "$HERE/selftest_fake_unity.sh" > "$WRAP"; chmod +x "$WRAP"
PGBCMD=$WRAPD/fake-pgb; printf '#!/bin/bash\necho "$PGB_VALUE"\n' > "$PGBCMD"; chmod +x "$PGBCMD"   # ignores the pid argument
FAKE_PROJECT='C:/Fake/Proj'
now_utc() { date -u +%Y-%m-%dT%H:%M:%S.0000000Z; }
watch_csv() {  # $1=file $2=status $3=utc (default now) $4=optional trailing line
  echo "utc,elapsed_s,pid,role,private_gb,working_set_gb,paged_gb,handles,threads,cpu_s,log_bytes,log_delta_bytes,marker_in_delta,sys_commit_gb,sys_commit_limit_gb,stop_reason,status" > "$1"
  echo "${3:-$(now_utc)},0,${WPID:-4242},editor,3.5,1,3.5,1,1,1,10,0,0,40,58,,$2" >> "$1"
  [ -n "$4" ] && echo "$4" >> "$1"
  return 0
}
# run_case <name> <expected_exit> <expected_reason_substring> <expected_submits> <N> [KEY=VAL ...]   (setup via $SETUP)
run_case() {
  local name=$1 want_rc=$2 want_reason=$3 want_sub=$4 n=$5; shift 5
  local d; d=$(mktemp -d)
  mkdir -p "$d/state" "$d/runs"; echo 'clean start' > "$d/Editor.log"; watch_csv "$d/watch.csv" OK
  FAKE_STATE="$d/state" FAKE_LOG="$d/Editor.log" D="$d" eval "$SETUP"
  env FAKE_STATE="$d/state" FAKE_LOG="$d/Editor.log" FAKE_WATCH="$d/watch.csv" FAKE_PROJECT="$FAKE_PROJECT" \
      FAKE_STATUS_FILE="$d/status.json" STATUS_FILE="$d/status.json" UNITY_CLI="$WRAP" \
      PROJECT_PATH="$FAKE_PROJECT" LOCK_DIR="$d/lock" RUN_DIR="$d/runs" \
      LOGFILE="$d/Editor.log" WATCH_CSV="$d/watch.csv" PRIVATE_GB_CMD="$PGBCMD" PGB_VALUE="${PGB:-3.5}" \
      SETTLE_SEC=0 POLL_SEC=0 START_GRACE=1 CLI_TIMEOUT=3 PING_FILE="$HERE/ping_probe.cs.txt" "$@" \
      bash "$LOOP" T 1 "$n" "$d/out.txt" >/dev/null 2>&1
  local rc=$? sub
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
args_are() {   # exact argv of the first run_tests call (without the trailing --json and the "--" separator)
  local got; got=$(awk '$0=="--"{exit} {print}' "$D/state/run_args" | grep -v '^--json$' | tr '\n' '|')
  [ "$got" = "$1" ] || { echo "      argv was: $got"; return 1; }
}

# --- §15 safety cases ---
SETUP=':'                                                   ; run_case "L1 healthy -> 2 runs, exit 0"               0  ""                           2 2
SETUP='echo 1 > $FAKE_STATE/flood_on_submit'                ; run_case "L2 flood after run 1 -> stop, no run 2"      11 "flood-onset"                1 3
SETUP='echo 2000 > $FAKE_STATE/grow_on_submit'              ; run_case "L3 log limit after run 1 -> stop"            12 "log-limit"                  1 3 LOG_LIMIT_BYTES=1000
PGB='13'; SETUP=':'                                         ; run_case "L4 private > limit -> stop before submit"     13 "private-limit"              0 2; PGB=
SETUP='echo 0 > $FAKE_STATE/ready'                          ; run_case "L5 editor not ready -> stop"                  10 "editor-not-ready"           0 2
SETUP='echo timeout > $FAKE_STATE/eval'                     ; run_case "L6 main thread unresponsive -> stop"          15 "editor-unresponsive"        0 2
SETUP='touch $FAKE_STATE/status_hang'                       ; run_case "L7 CLI hangs -> timeout -> stop"              10 "cli-timeout:status"         0 2
SETUP='rm -f $FAKE_LOG'                                     ; run_case "L8 log unreadable -> stop"                    16 "log-unreadable"             0 2
PGB='n/a'; SETUP=':'                                        ; run_case "L9 memory read fails -> stop"                 16 "monitor-error:private-read" 0 2; PGB=
SETUP='rm -f $D/watch.csv'                                  ; run_case "L10 sampler CSV missing -> stop"              17 "watcher-csv-unreadable"     0 2
SETUP='watch_csv $D/watch.csv OK 2020-01-01T00:00:00.0000000Z'; run_case "L11 sampler stale -> stop"                 17 "watcher-stale"              0 2
SETUP='watch_csv $D/watch.csv ERROR:log-unreadable'         ; run_case "L12 sampler reports ERROR -> stop"            17 "watcher-status:ERROR"       0 2
SETUP='watch_csv $D/watch.csv OK "" "# stopped: max-minutes"'; run_case "L13 sampler stopped -> stop"                17 "watcher-stopped"            0 2
SETUP='watch_csv $D/watch.csv ALERT:delta-exceeds-scan-cap' ; run_case "L14 sampler partial scan -> stop"             17 "watcher-status:ALERT"       0 2
SETUP='touch $FAKE_STATE/die_during_run'                    ; run_case "L15 editor dies mid-run -> stop, no run 2"    10 "editor-not-ready"           1 3
SETUP='touch $FAKE_STATE/never_start'                       ; run_case "L16 runs never start -> stop"                 19 "consecutive-invalid-runs"   2 3
SETUP='touch $FAKE_STATE/hang_test_status'                  ; run_case "L18 test_status hangs -> stop, no run 2"      10 "cli-timeout:test_status"    1 3
SETUP=':'                                                   ; run_case "L19 WATCH_CSV unset -> stop"                  17 "no-watcher"                 0 2 WATCH_CSV=
SETUP=':'                                                   ; run_case "L20 CHECK_ONLY healthy -> exit 0"             0  ""                           0 2 CHECK_ONLY=1
SETUP='echo timeout > $FAKE_STATE/eval'                     ; run_case "L21 CHECK_ONLY still fails closed"            15 "editor-unresponsive"        0 2 CHECK_ONLY=1
SETUP='touch $FAKE_STATE/stuck_running'                     ; run_case "L22 PlayMode run never completes -> timeout"  18 "run-timeout"                1 3 RUN_TIMEOUT=10
SETUP='touch $FAKE_STATE/hang_run_tests'                    ; run_case "L23 run_tests hangs -> stop, no run 2"        10 "cli-timeout:run_tests"      1 3
SETUP='touch $FAKE_STATE/watch_inject'                      ; run_case "L24 transient sampler ERROR mid-run -> stop"  17 "watcher-status:ERROR"       1 3
SETUP=':'                                                   ; run_case "L25 legacy DRYRUN=1 -> refuse"                2  ""                           0 2 DRYRUN=1
SETUP=':'                                                   ; run_case "L26 LOG_LIMIT_BYTES=1kB -> refuse"            2  ""                           0 2 LOG_LIMIT_BYTES=1kB
SETUP=':'                                                   ; run_case "L27 PRIVATE_LIMIT_GB=abc -> refuse"           2  ""                           0 2 PRIVATE_LIMIT_GB=abc
SETUP='WPID=11111 watch_csv $D/watch.csv OK'                ; run_case "L28 sampler watches another pid -> stop"      17 "watcher-pid-mismatch"       0 2
SETUP=':'                                                   ; run_case "L29 Editor of another project -> stop"        10 "editor-not-ready"           0 2 PROJECT_PATH='C:/Other/Proj'
SETUP='mkdir $D/lock'; POSTCHECK='[ -d $D/lock ]'           ; run_case "L30 second loop -> refuse, keeps other lock"  27 "another-loop-running"       0 2; POSTCHECK=
SETUP='watch_csv $D/watch.csv OK "" "garbage-without-timestamp,OK"'; run_case "L31 malformed last CSV row -> stop"   17 "watcher-bad-row"            0 2
SETUP=':'; POSTCHECK='[ ! -d $D/lock ]'                     ; run_case "L32 lock released after normal exit"         0  ""                           2 2; POSTCHECK=

# --- §17 P0-a: compile state is judged on state AND error evidence, never the state name alone ---
SETUP='echo completed > $FAKE_STATE/recompile'              ; run_case "C1 completed + no errors -> runs"             0  ""                           1 1
SETUP='echo up_to_date > $FAKE_STATE/recompile'             ; run_case "C2 up_to_date + no errors -> runs"            0  ""                           1 1
SETUP='echo completed_err > $FAKE_STATE/recompile'          ; run_case "C3 completed WITH errors -> stop"             14 "compile-not-clean"          0 1
SETUP='echo native_failed > $FAKE_STATE/recompile'          ; run_case "C4 idle but compilationFailed -> stop"        14 "compile-not-clean"          0 1
SETUP='echo failed_flag > $FAKE_STATE/recompile'            ; run_case "C5 completed failed=true, no list -> stop"    14 "compile-not-clean"          0 1
SETUP='echo compiling > $FAKE_STATE/recompile'              ; run_case "C6 compiling -> stop"                         14 "compile-state=compiling"    0 1
SETUP='echo triggered > $FAKE_STATE/recompile'              ; run_case "C7 triggered -> stop"                         14 "compile-state=triggered"    0 1
SETUP='echo unknown > $FAKE_STATE/recompile'                ; run_case "C8 unrecognised state -> stop"                14 "compile-state=unknown"      0 1
SETUP='echo malformed > $FAKE_STATE/recompile'              ; run_case "C9 unparseable recompile_status -> stop"      14 "compile-not-clean"          0 1
SETUP='echo missing_field > $FAKE_STATE/recompile'          ; run_case "C10 field missing -> stop"                    14 "recompile-missing:failed"   0 1
SETUP='echo compilefailed > $FAKE_STATE/eval'               ; run_case "C11 status idle but Unity flag says failed"   15 "compileFailed=True"         0 1
SETUP='echo compiling > $FAKE_STATE/eval'                   ; run_case "C12 main thread says isCompiling -> stop"     15 "isCompiling=True"           0 1
SETUP='echo playing > $FAKE_STATE/eval'                     ; run_case "C13 Editor in Play Mode -> no submit"         15 "isPlaying=True"             0 1
SETUP='echo updating > $FAKE_STATE/eval'                    ; run_case "C14 AssetDatabase updating -> stop"           15 "isUpdating=True"            0 1
SETUP='echo dirty > $FAKE_STATE/eval'                       ; run_case "C15 unsaved scene -> no submit (modal hang)"  15 "sceneDirty=True"            0 1

# --- §17 P0-b: modes, filters, explicit, validation (argv checked verbatim) ---
SETUP=':'; POSTCHECK='args_are "--mode|editmode|--async_tests|true|"'
  run_case "R1 EditMode -> --mode editmode"                   0  ""  1 1 MODE=EditMode; POSTCHECK=
SETUP=':'; POSTCHECK='args_are "--mode|playmode|--async_tests|true|"'
  run_case "R2 PlayMode default -> --mode playmode"           0  ""  1 1; POSTCHECK=
SETUP=':'; POSTCHECK='args_are "--mode|playmode|--async_tests|true|--filter|Project.Tests.PlayMode.StairTraversalPlayModeTests.Player_X(\"Stairs (1)\")|--include_explicit|true|"'
  run_case "R3 filtered probe: filter verbatim + explicit"    0  ""  1 1 TEST_FILTER='Project.Tests.PlayMode.StairTraversalPlayModeTests.Player_X("Stairs (1)")' INCLUDE_EXPLICIT=1; POSTCHECK=
SETUP=':'; POSTCHECK='[ ! -e $D/state/run_args ]'
  run_case "R4 filter with shell metachars -> refuse"         2  ""  0 1 TEST_FILTER='X; rm -rf ~'; POSTCHECK=
SETUP=':'                                                   ; run_case "R5 filter starting with - -> refuse"          2  ""  0 1 TEST_FILTER='--include_explicit'
SETUP=':'                                                   ; run_case "R6 explicit without filter -> refuse"         2  ""  0 1 INCLUDE_EXPLICIT=1
SETUP=':'                                                   ; run_case "R7 MODE=All -> refuse"                        2  ""  0 1 MODE=All
SETUP=':'                                                   ; run_case "R8 INCLUDE_EXPLICIT=yes -> refuse"            2  ""  0 1 INCLUDE_EXPLICIT=yes
SETUP=':'                                                   ; run_case "R9 RUN_TIMEOUT=5 (<10) -> refuse"             2  ""  0 1 RUN_TIMEOUT=5
SETUP=':'                                                   ; run_case "R10 filter with backtick -> refuse"           2  ""  0 1 TEST_FILTER='a`id`'

# --- §17 run results / timeouts ---
SETUP='touch $FAKE_STATE/run_reject'                        ; run_case "T1 run rejected (inner success:false)"       22 "run-not-accepted:rejected"  1 2
SETUP='touch $FAKE_STATE/ts_error'                          ; run_case "T2 test_status error -> stop"                 23 "run-error"                  1 2
SETUP='touch $FAKE_STATE/ts_cancelled'                      ; run_case "T3 test_status cancelled -> stop"             23 "run-cancelled"              1 2
SETUP='touch $FAKE_STATE/ts_malformed'                      ; run_case "T4 summary does not add up -> stop"           24 "result-malformed"           1 2
SETUP='touch $FAKE_STATE/stale_completed'                   ; run_case "T5 stale completed (dropped request) -> no"   19 "consecutive-invalid-runs"   2 2
SETUP='touch $FAKE_STATE/fast_complete'                     ; run_case "T6 fresh completion never seen running -> ok" 0  ""                           1 1
SETUP='touch $FAKE_STATE/stuck_running'                     ; run_case "T7 EditMode run timeout -> stop"             18 "run-timeout"                1 2 MODE=EditMode RUN_TIMEOUT=10
SETUP='echo 2 > $FAKE_STATE/fail'; POSTCHECK='ls $D/runs/T1-test_status.json >/dev/null'
  run_case "T8 failing tests are a VALID run, report kept"    0  ""  1 1; POSTCHECK=

# --- §17 review fixes ---
SETUP='touch $FAKE_STATE/in_flight'                         ; run_case "L33 earlier run still in flight -> no submit" 25 "previous-run-in-flight"     0 1
SETUP='watch_csv $D/watch.csv ERROR:sample-exception:x "$(date -u -d "-2 sec" +%Y-%m-%dT%H:%M:%S.0000000Z)" "$(now_utc),1,4242,editor,3.5,1,3.5,1,1,1,10,0,0,40,58,,OK"'
  run_case "L34 ERROR row since caller's audit -> stop"     17 "watcher-status:ERROR"       0 1 WATCH_LINE_START=1
SETUP=':'                                                   ; run_case "L35 WATCH_LINE_START=abc -> refuse"           2  ""                           0 1 WATCH_LINE_START=abc
# L36: SIGTERM while a run is in progress -> exits 143, releases its lock, submits nothing more
d=$(mktemp -d); mkdir -p "$d/state"; echo clean > "$d/Editor.log"; watch_csv "$d/watch.csv" OK; echo 30 > "$d/state/complete_after"
env FAKE_STATE="$d/state" FAKE_LOG="$d/Editor.log" FAKE_WATCH="$d/watch.csv" FAKE_PROJECT="$FAKE_PROJECT" FAKE_STATUS_FILE="$d/status.json" \
    STATUS_FILE="$d/status.json" UNITY_CLI="$WRAP" PROJECT_PATH="$FAKE_PROJECT" LOCK_DIR="$d/lock" LOGFILE="$d/Editor.log" \
    WATCH_CSV="$d/watch.csv" PRIVATE_GB_CMD="$PGBCMD" PGB_VALUE=3.5 SETTLE_SEC=0 POLL_SEC=1 START_GRACE=5 CLI_TIMEOUT=3 \
    WATCH_MAX_AGE=600 PING_FILE="$HERE/ping_probe.cs.txt" bash "$LOOP" T 1 3 "$d/out.txt" >/dev/null 2>&1 &
lp=$!; sleep 6; kill -TERM $lp; wait $lp; lrc=$?; sleep 2
sub=$(grep -c '^SUBMIT' "$d/out.txt")
if [ $lrc -eq 143 ] && [ ! -d "$d/lock" ] && [ "$sub" = 1 ]; then echo "PASS L36 SIGTERM mid-run -> exit 143, lock released, no further submit"
else echo "FAIL L36 SIGTERM mid-run  rc=$lrc lock_left=$([ -d "$d/lock" ] && echo yes || echo no) submits=$sub"; fails=$((fails+1)); fi
rm -rf "$d"

rm -rf "$WRAPD"
echo "loop selftest: $fails failure(s)"
exit $fails
