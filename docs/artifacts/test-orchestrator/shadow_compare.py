"""Phase 2 shadow mode (docs/57 §12, Q5): compare what the plan WOULD have skipped with a real full run.

    python -I docs/artifacts/test-orchestrator/shadow_compare.py --plan PLAN.json --edit-record DIR --play-record DIR
           [--known known-failures.json] [--log Logs/test-orchestrator/shadow.tsv] [--snapshot last-verified.json]
           [--no-snapshot]

Shadow procedure (tests still run in FULL, through the safe entry, exactly as today):
  1. plan_tests.py  -> plan.json (records the work-tree state it planned against: preState)
  2. safe_unity_test.sh --mode EditMode   and   --mode PlayMode   (no filter)
  3. this script:
     - the sample is valid only if both records are complete, unfiltered, of the planned HEAD, and the work tree is
       still byte-identical to preState (otherwise the plan and the run looked at different code);
     - ESCAPE = a failure in a test the plan would NOT have run, that was not already failing (same name AND same
       normalised message) in the previous snapshot's full run. ESCAPE_SYMPTOM = same name, different message;
     - appends one row to shadow.tsv; writes the new last-verified snapshot when the sample is valid.
Exit: 0 valid sample, no escape · 1 escape(s) · 3 invalid sample (nothing concluded) · 2 usage.
"""
import argparse
import datetime
import os
import sys

sys.dont_write_bytecode = True   # never leave __pycache__ in docs/ (it would itself be a change)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import classify_results as classify  # noqa: E402
import orchestrator_lib as lib  # noqa: E402
import snapshot as snap_mod  # noqa: E402

TSV_HEADER = ("utc\thead\tcountsTowardQ5\tsnapshotStatus\tplanTier\tplaySelection\tselectedFixtures\testimateSeconds\tfullEstimateSeconds\t"
              "actualEditSeconds\tactualPlaySeconds\tvalid\tinvalidReason\tescapes\tescapeTests\tclassification\t"
              "snapshotWritten\tplan")


def fingerprint(mode, full_name, message):
    return "%s|%s|%s" % (mode, full_name, lib.normalize_message(message)[:300])


def would_have_run(plan, mode, full_name):
    for inv in plan["invocations"]:
        if inv["mode"] != mode or inv["kind"] != "tier":
            continue
        if inv["filter"] == "" or inv["filter"].lower() in full_name.lower():
            return True
    return False


def validate_sample(plan, edit, play, current_state):
    head = (plan.get("preState") or {}).get("head")
    if not head:
        return "plan has no preState (synthetic plan?)"
    for rec, mode in ((edit, "EditMode"), (play, "PlayMode")):
        if rec.mode != mode:
            return "%s record is %s" % (mode, rec.mode)
        if not rec.is_full_run:
            return "%s record is not a full run (filter=%r explicit=%s check_only=%s)" % (mode, rec.filter, rec.include_explicit, rec.check_only)
        if rec.exit_code not in (0, 1) or rec.results is None:
            return "%s record did not complete (exit %s, %s)" % (mode, rec.exit_code, rec.status_error or rec.verdict)
        if not rec.git_head or not head.startswith(rec.git_head):
            return "%s record ran at %s, plan was made at %s" % (mode, rec.git_head, head[:12])
    if not snap_mod.state_equal(plan["preState"], current_state):
        return "work tree changed between planning and now: the run may not have tested the planned state"
    return None


def compare(plan, edit, play, previous_failures):
    escapes = []
    failing_now = []
    prev = set(previous_failures)
    prev_names = {f.split("|")[0] + "|" + f.split("|")[1] for f in prev}
    for rec in (edit, play):
        for r in rec.results:
            if r["Status"] != "Failed":
                continue
            fp = fingerprint(rec.mode, r["FullName"], r.get("Message"))
            failing_now.append(fp)
            if would_have_run(plan, rec.mode, r["FullName"]) or fp in prev:
                continue
            kind = "ESCAPE_SYMPTOM" if (rec.mode + "|" + r["FullName"]) in prev_names else "ESCAPE"
            escapes.append({"kind": kind, "mode": rec.mode, "fullName": r["FullName"],
                            "message": lib.normalize_message(r.get("Message"))[:200]})
    return escapes, failing_now


def main(argv):
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--plan", required=True)
    ap.add_argument("--edit-record", required=True)
    ap.add_argument("--play-record", required=True)
    ap.add_argument("--known", default=os.path.join(here, "known-failures.json"))
    ap.add_argument("--log", default=os.path.join(repo, "Logs", "test-orchestrator", "shadow.tsv"))
    ap.add_argument("--snapshot", default=os.path.join(repo, "Logs", "test-orchestrator", "last-verified.json"))
    ap.add_argument("--map", default=os.path.join(here, "test-map.json"))
    ap.add_argument("--repo", default=repo)
    ap.add_argument("--no-snapshot", action="store_true")
    a = ap.parse_args(argv)
    lib.utf8_stdout()
    try:
        plan = lib.load_json(a.plan)
        edit, play = lib.Record(a.edit_record), lib.Record(a.play_record)
        report = classify.classify([a.edit_record, a.play_record], a.known)
        current = snap_mod.worktree_state(a.repo)
        map_hash = lib.sha256_file(a.map)
    except (OSError, ValueError, KeyError, lib.GitError, classify.KnownListError) as e:
        print("usage: %s: %s" % (e.__class__.__name__, e), file=sys.stderr)
        return 2

    invalid = validate_sample(plan, edit, play, current)
    if plan.get("mapSha256") != map_hash and not invalid:
        invalid = "test-map.json changed after planning"
    previous = []
    if os.path.isfile(a.snapshot):
        try:
            previous = lib.load_json(a.snapshot).get("failures", [])
        except (OSError, ValueError):
            previous = []
    escapes, failing_now = ([], []) if invalid else compare(plan, edit, play, previous)

    written = False
    if not invalid and not a.no_snapshot:
        state = snap_mod.worktree_state(a.repo, write_text_blobs=True)
        if snap_mod.state_equal(state, plan["preState"]):
            snap_mod.write(a.repo, a.snapshot, state, map_hash, failing_now,
                           [lib.norm_path(os.path.relpath(d, a.repo)) for d in (a.edit_record, a.play_record)])
            written = True

    row = [datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
           (plan.get("preState") or {}).get("head", "")[:12],
           # Q5 counts only valid samples planned against a trustworthy snapshot: without one the plan is R3 by rule
           # and cannot have skipped anything, so it proves nothing about the selector.
           "yes" if (not invalid and (plan.get("snapshot") or {}).get("status") == "valid") else "no",
           (plan.get("snapshot") or {}).get("status", "?"), plan["tier"], plan["playMode"]["selection"],
           str(len(plan["playMode"]["selectedFixtures"])), str(plan["playMode"]["estimateSeconds"]),
           str(plan["playMode"]["fullEstimateSeconds"]), str(edit.runner_duration), str(play.runner_duration),
           "no" if invalid else "yes", (invalid or "").replace("\t", " "), str(len(escapes)),
           ";".join(e["fullName"] for e in escapes), report["verdict"], "yes" if written else "no",
           lib.norm_path(os.path.relpath(os.path.abspath(a.plan), a.repo))]
    os.makedirs(os.path.dirname(os.path.abspath(a.log)), exist_ok=True)
    new_log = not os.path.isfile(a.log)
    with open(a.log, "a", encoding="utf-8") as f:
        if new_log:
            f.write(TSV_HEADER + "\n")
        f.write("\t".join(row) + "\n")

    print("shadow: %s · plan %s/%s · classification %s · escapes %d · snapshot %s"
          % ("INVALID (" + invalid + ")" if invalid else "valid", plan["tier"], plan["playMode"]["selection"],
             report["verdict"], len(escapes), "written" if written else "not written"))
    for e in escapes:
        print("  %s %s %s — %s" % (e["kind"], e["mode"], e["fullName"], e["message"]))
    if invalid:
        return 3
    return 1 if escapes else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
