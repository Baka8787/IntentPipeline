"""Strict parsers / validators for the guarded Unity test entry (docs/53 §17).

Every subcommand reads a file written by `unity ... --json` (never stdin) and exits 0 only when the
input positively proves the condition. Anything missing, malformed, or unexpected exits non-zero with
a one-line reason on stdout -- the callers treat that as a stop (fail-closed).

Contracts verified against com.unity.pipeline 0.7.0-exp.1 source and live output (docs/53 §17.1):
  envelope:        {"success": bool, "data": {..., "result": <x>, "target": {"projectPath": ...}}, "errors": [...]}
  status:          {"success": true, "data": {"instances": [{"project", "pid", "state"}]}}
  recompile_status result (JSON string): {"status", "failed", "errors": [], "compilationFailed"}
  run_tests result (object):             {"success": bool, "result": "running", "StatusPath", "error"?}
  test_status result (JSON string):      {"status": running|completed|error|cancelled|no_tests, "summary", "results"}
  eval_file result (object):             {"success": bool, "result": "pong k=v ..."}

usage: python -I unity_test_json.py <subcommand> <args...>
  status         <file> <project>                 -> prints pid of the single 'ready' instance of <project>
  ping           <file> <project> [not-playing]   -> main thread answered; not compiling / no compile errors / not updating;
                                                     with not-playing also: not in Play Mode and no unsaved (dirty) scene
  recompile      <file> <project>                 -> compile state is clean (state AND error evidence)
  run-accepted   <file> <project>                 -> the run request was accepted ("running")
  test-status    <file> <project>                 -> prints running|completed|cancelled|no_tests|error:..|unavailable:..|unknown:..|malformed:..
  report         <file> <project>                 -> prints summary + failed tests; exit 0 all passed, 1 failures, 3 no tests,
                                                     4 malformed/contradictory, 5 nothing executed (all skipped)
  recompile-trigger <file> <project>              -> `recompile` answer: prints up_to_date|compiling (exit 0) or failed:.. (exit 1)
  recompile-poll <file> <project>                 -> prints done:<state> (exit 0, clean), wait (exit 10), or fail:<why> (exit 1)
  idle-for-submit <file> <project>                -> test_status shows no run in flight (no_tests/completed/error/cancelled)
  validate-filter <text>                          -> exit 0 if the filter is acceptable
"""
import json
import re
import sys

ALLOWED_COMPILE_STATES = {"idle", "completed", "up_to_date"}
FILTER_RE = re.compile(r'^[A-Za-z0-9_.,:()<>"\'*+?|\[\] /=-]{1,400}$')


def norm(path):
    return str(path).replace("\\", "/").rstrip("/").lower()


def fail(msg, code=1):
    print(msg.replace("\n", " ")[:300])
    sys.exit(code)


def load(path):
    try:
        with open(path, encoding="utf-8-sig") as f:
            text = f.read()
    except OSError as e:
        fail(f"unreadable:{e.__class__.__name__}")
    try:
        return json.loads(text)
    except ValueError:
        fail("not-json:" + text.strip()[:120])


def envelope(path, project):
    """Return data.result after proving the envelope succeeded and came from <project>."""
    env = load(path)
    if not isinstance(env, dict):
        fail("envelope-not-object")
    if env.get("success") is not True:
        errs = env.get("errors") or []
        msg = errs[0].get("message") if errs and isinstance(errs[0], dict) else "no-detail"
        fail("cli-error:" + str(msg))
    data = env.get("data")
    if not isinstance(data, dict) or data.get("success") is not True:
        fail("command-not-successful")
    target = data.get("target")
    if not isinstance(target, dict) or norm(target.get("projectPath", "")) != norm(project):
        fail("wrong-project-target:" + str((target or {}).get("projectPath")))
    if "result" not in data:
        fail("no-result")
    return data["result"]


def as_obj(result, what):
    if isinstance(result, str):
        try:
            result = json.loads(result)
        except ValueError:
            fail(f"{what}-result-not-json")
    if not isinstance(result, dict):
        fail(f"{what}-result-not-object")
    return result


def cmd_status(path, project):
    env = load(path)
    if not isinstance(env, dict) or env.get("success") is not True:
        fail("cli-error")
    inst = ((env.get("data") or {}).get("instances"))
    if not isinstance(inst, list):
        fail("no-instances-field")
    mine = [i for i in inst if isinstance(i, dict) and norm(i.get("project", "")) == norm(project)]
    if len(mine) != 1:
        fail(f"instances-for-project={len(mine)}")
    i = mine[0]
    if i.get("state") != "ready":
        fail("state=" + str(i.get("state")))
    pid = i.get("pid")
    if not isinstance(pid, int) or pid <= 0:
        fail("bad-pid")
    print(pid)


def cmd_ping(path, project, not_playing=False):
    r = envelope(path, project)
    if not isinstance(r, dict) or r.get("success") is not True:
        fail("eval-failed")
    text = r.get("result")
    if not isinstance(text, str) or not text.startswith("pong"):
        fail("no-pong")
    kv = dict(re.findall(r"(\w+)=(\w+)", text))
    need = {"isCompiling": "False", "compileFailed": "False", "isUpdating": "False"}
    if not_playing:   # before a run: not in Play Mode, and no unsaved scene (the runner's modal save dialog hangs the Editor)
        need["isPlaying"] = "False"
        need["sceneDirty"] = "False"
    for k, want in need.items():
        if k not in kv:
            fail(f"ping-missing:{k}")
        if kv[k] != want:
            fail(f"{k}={kv[k]}")
    print("ok")


def recompile_verdict(r, allowed):
    """('ok', state) | ('wait', state) | ('fail', why). Clean = allowed state AND every error signal false/empty."""
    for key, typ in (("status", str), ("failed", bool), ("errors", list), ("compilationFailed", bool)):
        if key not in r:
            return "fail", f"recompile-missing:{key}"
        if not isinstance(r[key], typ):
            return "fail", f"recompile-bad-type:{key}"
    if r["failed"] or r["errors"] or r["compilationFailed"]:
        return "fail", f"compile-errors(failed={r['failed']},errors={len(r['errors'])},compilationFailed={r['compilationFailed']})"
    if r["status"] in allowed:
        return "ok", r["status"]
    if r["status"] in ("compiling", "triggered"):
        return "wait", r["status"]
    return "fail", "compile-state=" + r["status"]


def cmd_recompile(path, project):
    v, why = recompile_verdict(as_obj(envelope(path, project), "recompile"), ALLOWED_COMPILE_STATES)
    if v != "ok":
        fail(why if v == "fail" else "compile-state=" + why)
    print("ok:" + why)


def cmd_recompile_trigger(path, project):
    r = envelope(path, project)
    if not isinstance(r, dict) or not isinstance(r.get("status"), str):
        fail("recompile-trigger-malformed")
    if r["status"] in ("up_to_date", "compiling"):
        print(r["status"]); return
    fail(f"{r['status']}:{r.get('message')}")


def cmd_recompile_poll(path, project):
    env = load(path)
    if not isinstance(env, dict) or env.get("success") is not True:
        print("wait"); sys.exit(10)          # the domain reload after a compile drops connections; caller bounds the wait
    # after an explicit recompile only a finished compile counts: 'idle' would mean the status file was lost
    v, why = recompile_verdict(as_obj(envelope(path, project), "recompile"), {"completed", "up_to_date"})
    if v == "ok":
        print("done:" + why); return
    if v == "wait":
        print("wait"); sys.exit(10)
    fail(why)


def cmd_idle_for_submit(path, project):
    env = load(path)
    if not isinstance(env, dict) or env.get("success") is not True:
        fail("test_status-unavailable")
    r = as_obj(envelope(path, project), "test_status")
    st = r.get("status")
    if st in ("no_tests", "completed", "error", "cancelled"):
        print("ok:" + st); return
    fail("previous-run-in-flight:" + str(st))


def cmd_run_accepted(path, project):
    r = envelope(path, project)
    if not isinstance(r, dict):
        fail("run-result-not-object")
    if r.get("success") is not True:
        fail("rejected:" + str(r.get("error")))
    if r.get("result") != "running":
        fail("result=" + str(r.get("result")))
    print("ok")


def summary_of(r):
    s = r.get("summary")
    if not isinstance(s, dict):
        return None, "no-summary"
    keys = ("total", "passed", "failed", "skipped", "inconclusive")
    if any(not isinstance(s.get(k), int) or s.get(k) < 0 for k in keys):
        return None, "bad-summary-fields"
    if s["total"] != s["passed"] + s["failed"] + s["skipped"] + s["inconclusive"]:
        return None, "summary-does-not-add-up"
    if not isinstance(r.get("results"), list):
        return None, "no-results-list"
    return s, None


def cmd_test_status(path, project):
    env = load(path)
    if not isinstance(env, dict) or env.get("success") is not True:
        print("unavailable:cli-error")   # e.g. during a PlayMode domain reload; caller bounds the wait
        return
    r = as_obj(envelope(path, project), "test_status")
    st = r.get("status")
    if st == "completed":
        s, why = summary_of(r)
        print("completed" if s else "malformed:" + why)
    elif st in ("running", "cancelled", "no_tests"):
        print(st)
    elif st == "error":
        print("error:" + str(r.get("message"))[:200].replace("\n", " "))
    else:
        print("unknown:" + str(st))


def cmd_report(path, project):
    r = as_obj(envelope(path, project), "test_status")
    if r.get("status") != "completed":
        fail("not-completed:" + str(r.get("status")), 4)
    s, why = summary_of(r)
    if not s:
        fail("malformed:" + why, 4)
    print("SUMMARY total={total} passed={passed} failed={failed} skipped={skipped} inconclusive={inconclusive}".format(**s)
          + f" duration={r.get('duration')}")
    bad = [x for x in r["results"] if isinstance(x, dict) and x.get("Status") in ("Failed", "Inconclusive")]
    for x in bad:
        msg = (x.get("Message") or "").strip().splitlines()
        print(f"{x.get('Status', '?').upper()} {x.get('FullName')} :: {msg[0][:200] if msg else ''}")
    if s["total"] == 0:
        sys.exit(3)
    if bad and not (s["failed"] or s["inconclusive"]):
        print("CONTRADICTION per-test entries report failures but the summary says failed=0 inconclusive=0")
        sys.exit(4)
    if s["failed"] or s["inconclusive"]:
        if not bad:
            print("WARNING summary reports failures but no per-test failure entries were returned")
        sys.exit(1)
    if s["passed"] == 0:
        print("NOTHING-EXECUTED every selected test was skipped/ignored")
        sys.exit(5)
    sys.exit(0)


def cmd_validate_filter(text):
    if not FILTER_RE.match(text) or text.startswith("-"):
        fail("invalid-filter")
    print("ok")


def main():
    # test messages are often Chinese: always emit UTF-8, never the console code page (cp950 garbles them)
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except (AttributeError, ValueError):
        pass
    a = sys.argv[1:]
    if not a:
        fail("usage", 2)
    c = a[0]
    try:
        if c == "status" and len(a) == 3: cmd_status(a[1], a[2])
        elif c == "ping" and len(a) in (3, 4): cmd_ping(a[1], a[2], len(a) == 4 and a[3] == "not-playing")
        elif c == "recompile" and len(a) == 3: cmd_recompile(a[1], a[2])
        elif c == "run-accepted" and len(a) == 3: cmd_run_accepted(a[1], a[2])
        elif c == "test-status" and len(a) == 3: cmd_test_status(a[1], a[2])
        elif c == "report" and len(a) == 3: cmd_report(a[1], a[2])
        elif c == "validate-filter" and len(a) == 2: cmd_validate_filter(a[1])
        elif c == "recompile-trigger" and len(a) == 3: cmd_recompile_trigger(a[1], a[2])
        elif c == "recompile-poll" and len(a) == 3: cmd_recompile_poll(a[1], a[2])
        elif c == "idle-for-submit" and len(a) == 3: cmd_idle_for_submit(a[1], a[2])
        else: fail("usage", 2)
    except SystemExit:
        raise
    except Exception as e:   # any unexpected parser failure is a stop, never a pass
        fail("parser-exception:" + e.__class__.__name__, 5)


if __name__ == "__main__":
    main()
