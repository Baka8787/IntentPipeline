#!/bin/bash
# Parser self-test against REAL CLI output captured from this project's Editor (fixtures/real-*.json, 2026-10-11,
# com.unity.pipeline 0.7.0-exp.1, unity CLI --json). Guards against the fake CLI drifting from the real contract.
HERE="$(cd "$(dirname "$0")" && pwd)"
J="$HERE/unity_test_json.py"; F="$HERE/fixtures"
P='D:\Unity Project\CharacterController'
fails=0
t() {  # <name> <want_rc> <want_stdout_substring> <subcommand> <args...>
  local name=$1 want_rc=$2 want=$3; shift 3
  local out rc; out=$(python -I "$J" "$@" 2>&1); rc=$?
  if [ "$rc" = "$want_rc" ] && [[ "$out" == *"$want"* ]]; then echo "PASS $name"; else echo "FAIL $name  rc=$rc(want $want_rc) out='${out:0:160}'"; fails=$((fails+1)); fi
}
t "P1 status -> pid of this project"            0 "99620"                    status        "$F/real-status.json" "$P"
t "P2 status for another project -> refuse"     1 "instances-for-project=0"  status        "$F/real-status.json" 'D:\Other\Project'
t "P3 ping (post-run) ok"                       0 "ok"                       ping          "$F/real-ping.json" "$P"
t "P4 ping (pre-run, not playing, clean) ok"    0 "ok"                       ping          "$F/real-ping.json" "$P" not-playing
t "P5 ping from another project -> refuse"      1 "wrong-project-target"     ping          "$F/real-ping.json" 'D:\Other\Project'
t "P6 recompile idle+clean ok"                  0 "ok:idle"                  recompile     "$F/real-recompile-idle.json" "$P"
t "P7 rejected run (outer success:true) -> no"  1 "rejected:Invalid mode"    run-accepted  "$F/real-run-rejected.json" "$P"
t "P8 test_status no_tests"                     0 "no_tests"                 test-status   "$F/real-test-status-no-tests.json" "$P"
t "P9 test_status completed"                    0 "completed"                test-status   "$F/real-test-status-completed-pass.json" "$P"
t "P10 report pass -> 0"                        0 "SUMMARY total=7 passed=7" report        "$F/real-test-status-completed-pass.json" "$P"
t "P11 report 1 failed -> 1, name listed"       1 "FAILED Project.Tests.PlayMode.MotionDriverCommittedMovementPlayModeTests.C0_" report "$F/real-test-status-completed-1-failed.json" "$P"
t "P12 report zero tests -> 3"                  3 "SUMMARY total=0"          report        "$F/real-test-status-completed-0-tests.json" "$P"
t "P13 CLI error envelope -> refuse"            1 "cli-error:Bad Request"    ping          "$F/real-cli-error.json" "$P"
t "P14 CLI error as status -> refuse"           1 "cli-error"                status        "$F/real-cli-error.json" "$P"
t "P15 report on a non-completed file -> 4"     4 "not-completed:no_tests"   report        "$F/real-test-status-no-tests.json" "$P"
t "P16 missing file -> refuse"                  1 "unreadable"               recompile     "$F/does-not-exist.json" "$P"
t "P17 filter: real probe name ok"              0 "ok"                       validate-filter 'Project.Tests.PlayMode.StairTraversalPlayModeTests.Player_X("Stairs (1)")'
t "P18 filter: \$() rejected"                   1 "invalid-filter"           validate-filter 'a$(id)'
t "P19 filter: newline rejected"                1 "invalid-filter"           validate-filter $'a\nb'
t "P20 recompile trigger up_to_date (real)"     0 "up_to_date"               recompile-trigger "$F/real-recompile-trigger.json" "$P"
t "P21 recompile poll after trigger (real)"     0 "done:up_to_date"          recompile-poll "$F/real-recompile-after.json" "$P"
t "P22 recompile poll: idle is NOT done"        1 "compile-state=idle"       recompile-poll "$F/real-recompile-idle.json" "$P"
t "P23 idle-for-submit: no_tests (real)"        0 "ok:no_tests"              idle-for-submit "$F/real-test-status-no-tests.json" "$P"
t "P24 idle-for-submit: completed (real)"       0 "ok:completed"             idle-for-submit "$F/real-test-status-completed-pass.json" "$P"
X=$(mktemp -d)
mk() { python -I -c "import json,sys; r=sys.argv[2]; print(json.dumps({'success':True,'data':{'result':r,'target':{'projectPath':sys.argv[3]},'success':True}}))" x "$1" "$P" > "$X/$2"; }
mk '{"status":"running"}' run.json
mk '{"status":"completed","duration":1,"summary":{"total":2,"passed":0,"failed":0,"skipped":2,"inconclusive":0},"results":[]}' skip.json
mk '{"status":"completed","duration":1,"summary":{"total":1,"passed":1,"failed":0,"skipped":0,"inconclusive":0},"results":[{"FullName":"X","Status":"Failed","Message":"boom"}]}' contra.json
mk '{"status":"completed","duration":1,"summary":{"total":1,"passed":0,"failed":0,"skipped":0,"inconclusive":1},"results":[{"FullName":"Y","Status":"Inconclusive","Message":"?"}]}' inconc.json
t "P25 idle-for-submit: running -> refuse"      1 "previous-run-in-flight"   idle-for-submit "$X/run.json" "$P"
t "P26 report all skipped -> 5"                 5 "NOTHING-EXECUTED"         report "$X/skip.json" "$P"
t "P27 report contradictory -> 4"               4 "CONTRADICTION"            report "$X/contra.json" "$P"
t "P28 report inconclusive -> 1"                1 "INCONCLUSIVE Y"           report "$X/inconc.json" "$P"
rm -rf "$X"
echo "parser selftest: $fails failure(s)"
exit $fails
