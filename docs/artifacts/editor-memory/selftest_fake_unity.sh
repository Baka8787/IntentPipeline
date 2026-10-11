#!/bin/bash
# Fake `unity` CLI for the self-tests (docs/53 §15, §17). Emits the SAME --json envelope shapes as the real CLI
# (see fixtures/real-*.json). Never talks to Unity. State lives in $FAKE_STATE (one file per knob):
#   ready=0                 status lists the project in state "starting"
#   project=PATH            project path reported (default $FAKE_PROJECT)
#   status_hang             `status` never returns
#   eval=pong|timeout|hang|compiling|compilefailed|playing|updating|dirty
#   recompile=idle|completed|up_to_date|completed_err|compiling|triggered|native_failed|failed_flag|unknown|malformed|missing_field
#   run_reject              run_tests answers outer success:true but inner success:false
#   hang_run_tests          run_tests never returns
#   never_start             test_status says no_tests forever
#   stuck_running           test_status says running forever
#   die_during_run          the instance disappears while running
#   hang_test_status        test_status never returns
#   ts_error / ts_cancelled / ts_malformed    terminal test_status variants
#   stale_completed         test_status returns "completed" at once, but the status file is OLD (dropped request)
#   fast_complete           completes on the first poll (never seen running), status file written now
#   complete_after=N        number of test_status polls before completion (default 2)
#   in_flight               before any submission test_status already says "running" (an orphaned earlier run)
#   recompile_trigger=up_to_date|compile_ok|compile_err|compile_hang|refused   behaviour of `recompile`
#                           (compile_*: recompile_status reports compiling for 2 polls, then the outcome)
#   total=N fail=N skip=N   summary of a completed run (default 3 / 0 / 0); total=0 -> no tests matched
#   flood_on_submit=N grow_on_submit=BYTES watch_inject    side effects when a run completes
# Every run_tests invocation appends its argv (one arg per line, then "--") to $FAKE_STATE/run_args.
S="$FAKE_STATE"; get() { cat "$S/$1" 2>/dev/null; }
PROJ="$(get project || echo "$FAKE_PROJECT")"
now() { date -u +%Y-%m-%dT%H:%M:%S.0000000Z; }
env_ok() {   # $1 = command, $2 = result (already JSON)
  printf '{"success":true,"command":"command %s","data":{"command":"%s","parameters":{},"result":%s,"target":{"host":"127.0.0.1","port":7800,"projectPath":"%s"},"success":true},"errors":[],"warnings":[]}\n' "$1" "$1" "$2" "$PROJ"
}
env_err() { printf '{"success":false,"command":"unity command %s","data":null,"errors":[{"code":"COMMAND_FAILED","message":"%s"}],"warnings":[]}\n' "$1" "$2"; exit 6; }
jstr() { local s=${1//\\/\\\\}; s=${s//\"/\\\"}; printf '"%s"' "$s"; }   # JSON string literal
completed_json() {
  local t=$(get total || echo 3) f=$(get fail || echo 0) k=$(get skip || echo 0) res="" i
  for ((i=1;i<=t;i++)); do
    st=Passed; [ $i -le $f ] && st=Failed; [ $i -gt $((t-k)) ] && st=Skipped
    res="$res{\"FullName\":\"Fake.Tests.T$i\",\"Status\":\"$st\",\"Duration\":0.1,\"Message\":$( [ $st = Failed ] && echo '"Expected 1 but was 2"' || echo null),\"StackTrace\":null},"
  done
  printf '{"status":"completed","duration":1.5,"summary":{"total":%d,"passed":%d,"failed":%d,"skipped":%d,"inconclusive":0},"results":[%s]}' "$t" "$((t-f-k))" "$f" "$k" "${res%,}"
}
case "$1" in
  status)
    [ -e "$S/status_hang" ] && sleep 999
    state=ready; [ "$(get ready)" = "0" ] && state=starting
    printf '{"success":true,"command":"status","data":{"count":1,"instances":[{"port":7800,"project":"%s","version":"6000.5.1f1","pid":4242,"state":"%s"}]},"errors":[],"warnings":[]}\n' "$PROJ" "$state" ;;
  cmd)
    case "$2" in
      eval_file)
        # mirror the real CLI contract: only *.cs files are accepted
        [[ "$4" == *.cs ]] || env_err eval_file 'Bad Request\n  File must have a .cs extension'
        p=False; c=False; f=False; u=False; dz=False
        case "$(get eval)" in
          timeout) env_err eval_file 'Pipeline server returned 400 Bad Request: Internal Server Error. Main thread operation timed out after 5000ms' ;;
          hang) sleep 999 ;;
          compiling) c=True ;; compilefailed) f=True ;; playing) p=True ;; updating) u=True ;; dirty) dz=True ;;
        esac
        env_ok eval_file "{\"output\":null,\"diagnostics\":[],\"success\":true,\"result\":\"pong isPlaying=$p isCompiling=$c compileFailed=$f isUpdating=$u sceneDirty=$dz\"}" ;;
      recompile)
        case "$(get recompile_trigger || echo up_to_date)" in
          up_to_date) env_ok recompile '{"status":"up_to_date","message":"No scripts needed recompilation."}' ;;
          refused)    env_ok recompile '{"status":"failed","message":"Scripts still have compile errors; nothing was recompiled."}' ;;
          *)          echo 0 > "$S/rc_polls"; env_ok recompile '{"status":"compiling","message":"Recompilation started."}' ;;
        esac ;;
      recompile_status)
        if [ -e "$S/rc_polls" ]; then
          n=$(( $(get rc_polls) + 1 )); echo $n > "$S/rc_polls"
          case "$(get recompile_trigger)" in
            compile_hang) r='{"status":"compiling","failed":false,"errors":[],"compilationFailed":false}' ;;
            compile_err) [ $n -le 2 ] && r='{"status":"compiling","failed":false,"errors":[],"compilationFailed":false}' \
                         || r='{"status":"completed","failed":true,"errors":["Assets/X.cs(1,1): error CS1002"],"compilationFailed":false}' ;;
            *) [ $n -le 2 ] && r='{"status":"compiling","failed":false,"errors":[],"compilationFailed":false}' \
                         || r='{"status":"completed","failed":false,"errors":[],"compilationFailed":false}' ;;
          esac
          env_ok recompile_status "$(jstr "$r")"; exit 0
        fi
        case "$(get recompile || echo idle)" in
          idle|completed|up_to_date|compiling|triggered|unknown) r="{\"status\":\"$(get recompile || echo idle)\",\"failed\":false,\"errors\":[],\"compilationFailed\":false}" ;;
          completed_err) r='{"status":"completed","failed":true,"errors":["Assets/X.cs(1,1): error CS1002: ; expected"],"compilationFailed":false}' ;;
          native_failed) r='{"status":"idle","failed":false,"errors":[],"compilationFailed":true}' ;;
          failed_flag)   r='{"status":"completed","failed":true,"errors":[],"compilationFailed":false}' ;;
          missing_field) r='{"status":"idle","errors":[],"compilationFailed":false}' ;;
          malformed)     r='{not json' ;;
        esac
        env_ok recompile_status "$(jstr "$r")" ;;
      run_tests)
        { shift 2; for a in "$@"; do echo "$a"; done; echo "--"; } >> "$S/run_args"
        [ -e "$S/hang_run_tests" ] && sleep 999
        if [ -e "$S/run_reject" ]; then
          env_ok run_tests '{"Summary":{"Total":0},"Results":[],"StatusPath":null,"success":false,"error":"Invalid mode"}'; exit 0
        fi
        n=$(( $(get submits || echo 0) + 1 )); echo $n > "$S/submits"; echo 0 > "$S/polls"
        env_ok run_tests '{"Summary":{"Total":0},"Results":[],"StatusPath":"Temp/pipeline_test_status.json","success":true,"result":"running","message":"Tests started in async mode."}' ;;
      test_status)
        [ -e "$S/hang_test_status" ] && [ -n "$(get submits)" ] && sleep 999
        if [ -e "$S/in_flight" ] && [ -z "$(get submits)" ]; then env_ok test_status "$(jstr '{"status":"running"}')"; exit 0; fi
        # like the real CLI: before anything was submitted there is no run (unless an orphaned one is simulated)
        if [ -z "$(get submits)" ]; then env_ok test_status "$(jstr '{"status":"no_tests","message":"No test run in progress"}')"; exit 0; fi
        if [ -e "$S/never_start" ]; then env_ok test_status "$(jstr '{"status":"no_tests","message":"No test run in progress"}')"; exit 0; fi
        p=$(( $(get polls || echo 0) + 1 )); echo $p > "$S/polls"
        if [ -e "$S/stale_completed" ]; then
          touch -d '2020-01-01' "$FAKE_STATUS_FILE"; env_ok test_status "$(jstr "$(completed_json)")"; exit 0
        fi
        if [ -e "$S/stuck_running" ]; then env_ok test_status "$(jstr '{"status":"running"}')"; exit 0; fi
        if [ -e "$S/die_during_run" ]; then echo 0 > "$S/ready"; env_ok test_status "$(jstr '{"status":"running"}')"; exit 0; fi
        if [ $p -lt "$(get complete_after || echo 2)" ] && [ ! -e "$S/fast_complete" ]; then env_ok test_status "$(jstr '{"status":"running"}')"; exit 0; fi
        [ -e "$S/ts_error" ] && { env_ok test_status "$(jstr '{"status":"error","message":"Tests did not complete"}')"; exit 0; }
        [ -e "$S/ts_cancelled" ] && { env_ok test_status "$(jstr '{"status":"cancelled","message":"Test run cancelled."}')"; exit 0; }
        [ -e "$S/ts_malformed" ] && { env_ok test_status "$(jstr '{"status":"completed","summary":{"total":5,"passed":1,"failed":0,"skipped":0,"inconclusive":0},"results":[]}')"; exit 0; }
        n=$(get submits)
        [ -n "$n" ] && [ "$(get flood_on_submit)" = "$n" ] && echo 'Access version should be odd when acquiring lock' >> "$FAKE_LOG"
        g=$(get grow_on_submit); [ -n "$g" ] && head -c "$g" /dev/zero | tr '\0' 'x' >> "$FAKE_LOG"
        if [ -e "$S/watch_inject" ]; then
          printf '%s,1,4242,editor,3.5,1,3.5,1,1,1,10,0,,40,58,,ERROR:sample-exception:injected\n' "$(date -u -d '-1 sec' +%Y-%m-%dT%H:%M:%S.0000000Z)" >> "$FAKE_WATCH"
          printf '%s,2,4242,editor,3.5,1,3.5,1,1,1,10,0,0,40,58,,OK\n' "$(now)" >> "$FAKE_WATCH"
        fi
        touch "$FAKE_STATUS_FILE"
        env_ok test_status "$(jstr "$(completed_json)")" ;;
    esac ;;
esac
