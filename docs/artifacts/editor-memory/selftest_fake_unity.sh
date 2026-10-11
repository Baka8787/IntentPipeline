#!/bin/bash
# Fake `unity` CLI for selftest_loop.sh (docs/53 §15). State lives in $FAKE_STATE (one file per knob).
# Never talks to Unity. Knobs (files in $FAKE_STATE):
#   ready=0|1  status_hang  eval=pong|timeout|hang  recompile=idle|busy
#   never_start  die_during_run  hang_test_status
#   flood_on_submit=N   (append a flood line to $FAKE_LOG when the N-th run completes)
#   grow_on_submit=BYTES (append BYTES to $FAKE_LOG when each run completes)
#   stuck_running (test_status reports "running" forever)  hang_run_tests (run_tests never returns)
#   watch_inject (when a run completes: append to $FAKE_WATCH an ERROR row 1 s old, then an OK row -- a transient
#                 sampler error that has already cleared by the next check)
#   project=NAME (project column printed by `status`, default FAKE)
S="$FAKE_STATE"; get() { cat "$S/$1" 2>/dev/null; }
case "$1" in
  status)
    [ -e "$S/status_hang" ] && sleep 999
    printf '連接埠\t狀態\t專案\t版本\tPID\n'
    [ "$(get ready)" = "0" ] || printf '7800\tready\t%s\t6000.5.1f1\t4242\n' "$(get project || echo FAKE)" ;;
  cmd)
    case "$2" in
      eval_file)
        # mirror the real CLI contract: only *.cs files are accepted
        [[ "$4" == *.cs ]] || { echo 'Error: Bad Request'; echo '  File must have a .cs extension'; exit 1; }
        case "$(get eval)" in
          timeout) echo 'Error: Main thread operation timed out after 5000ms' ;;
          hang) sleep 999 ;;
          *) echo '{"result":"pong isPlaying=False"}' ;;
        esac ;;
      recompile_status)
        [ "$(get recompile)" = "busy" ] && echo '{"status":"compiling"}' || echo '{"status":"idle","failed":false}' ;;
      run_tests)
        [ -e "$S/hang_run_tests" ] && sleep 999
        n=$(( $(get submits || echo 0) + 1 )); echo $n > "$S/submits"; echo 0 > "$S/polls"
        echo '{"success":true,"result":"running"}' ;;
      test_status)
        [ -e "$S/hang_test_status" ] && sleep 999
        if [ -e "$S/never_start" ]; then echo '{"status":"no_tests"}'; exit 0; fi
        p=$(( $(get polls || echo 0) + 1 )); echo $p > "$S/polls"
        if [ -e "$S/stuck_running" ]; then echo '{"status":"running"}'; exit 0; fi
        if [ -e "$S/die_during_run" ]; then echo 0 > "$S/ready"; echo '{"status":"running"}'; exit 0; fi
        if [ $p -lt 2 ]; then echo '{"status":"running"}'; exit 0; fi
        n=$(get submits)
        [ "$(get flood_on_submit)" = "$n" ] && echo 'Access version should be odd when acquiring lock' >> "$FAKE_LOG"
        g=$(get grow_on_submit); [ -n "$g" ] && head -c "$g" /dev/zero | tr '\0' 'x' >> "$FAKE_LOG"
        if [ -e "$S/watch_inject" ]; then
          printf '%s,1,4242,editor,3.5,1,3.5,1,1,1,10,0,,40,58,,ERROR:sample-exception:injected
' "$(date -u -d '-1 sec' +%Y-%m-%dT%H:%M:%S.0000000Z)" >> "$FAKE_WATCH"
          printf '%s,2,4242,editor,3.5,1,3.5,1,1,1,10,0,0,40,58,,OK
' "$(date -u +%Y-%m-%dT%H:%M:%S.0000000Z)" >> "$FAKE_WATCH"
        fi
        echo '{"status":"completed","summary":{"total":107,"passed":107,"failed":0}}' ;;
    esac ;;
esac
