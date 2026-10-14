"""Phase 2 shadow mode (docs/57 §12, §14; Q5): compare what the plan WOULD have skipped with a real full run.

    python -I docs/artifacts/test-orchestrator/shadow_compare.py --plan PLAN.json --edit-record DIR --play-record DIR
           [--known known-failures.json] [--samples Logs/test-orchestrator/shadow] [--log .../shadow.tsv]
           [--snapshot .../last-verified.json] [--no-snapshot]

Shadow procedure (tests still run in FULL, through the safe entry, exactly as today):
  1. plan_tests.py  -> plan.json (records the work-tree state it planned against: preState)
  2. safe_unity_test.sh --mode EditMode   and   --mode PlayMode   (no filter)
  3. this script:
     - the sample is valid only if both records are complete, unfiltered, of the planned HEAD, and the work tree is
       still byte-identical to preState (otherwise the plan and the run looked at different code);
     - ESCAPE = a NEW_FAILURE (classifier: not an approved known failure with its approved symptoms) in a test the
       plan would NOT have run. ESCAPE_SYMPTOM = a known test failing with a changed symptom. Every escape must later be
       investigated (escape-investigations.json): mapping gap / rule gap / flaky test;
     - writes one sample record (sample.json + sample.md: change set, planned scope, selected tests, full results,
       escapes, snapshot validity, estimated and measured savings) and appends one row to shadow.tsv;
     - advances the trusted snapshot ONLY when (§14.6): the sample is valid AND the classification is VERIFIED or
       VERIFIED_WITH_KNOWN_FAILURES (no new failure, nothing blocked, nothing incomplete) AND no selected test went
       unexecuted (skipped / ignored / inconclusive). Otherwise the previous snapshot stays untouched.
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

TSV_HEADER = ("utc\thead\tcountsTowardQ5\tsnapshotStatus\tplanTier\tplaySelection\tselectedFixtures\testimateSeconds\t"
              "fullEstimateSeconds\testimatedSavedSeconds\tmeasuredSavedSeconds\tactualEditSeconds\tactualPlaySeconds\t"
              "valid\tinvalidReason\tescapes\tescapeTests\tclassification\tsnapshotWritten\tsample")


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


def compare(plan, report):
    """Escapes come from the classifier: an approved known failure with its approved symptoms is never an escape."""
    escapes, failing_now = [], []
    for rec in report["records"]:
        for f in rec["failures"]:
            failing_now.append(fingerprint(rec["mode"], f["fullName"], f["message"]))
            if f["kind"] != "NEW_FAILURE" or would_have_run(plan, rec["mode"], f["fullName"]):
                continue
            escapes.append({"kind": "ESCAPE_SYMPTOM" if f.get("symptomChangedFrom") else "ESCAPE",
                            "mode": rec["mode"], "fullName": f["fullName"], "message": f["message"][:200],
                            "symptomChangedFrom": f.get("symptomChangedFrom"), "investigation": "PENDING"})
    return escapes, failing_now


def snapshot_blocker(invalid, report, no_snapshot):
    """Why the trusted baseline must NOT advance (None = it may). Order = most fundamental first."""
    if invalid:
        return "invalid sample: " + invalid
    if no_snapshot:
        return "--no-snapshot"
    verdict = report["verdict"]
    if verdict not in ("VERIFIED", "VERIFIED_WITH_KNOWN_FAILURES"):
        return "classification %s: a baseline is never advanced past a new failure, a block or an incomplete run" % verdict
    not_executed = sum(len(r["not_executed"]) for r in report["records"])
    if not_executed:
        return "%d selected test(s) not executed (skipped / ignored / inconclusive): verification incomplete" % not_executed
    return None


def savings(plan, play, overhead):
    """Estimated (from the plan's cost model) and measured (from the real full run's per-test durations) PlayMode
    time the plan would have saved. EditMode always runs in full (Q3), so it never contributes."""
    pm = plan["playMode"]
    full_est = pm.get("fullEstimateSeconds") or 0.0
    est = {"plannedSeconds": pm.get("estimateSeconds", 0.0), "fullSeconds": full_est,
           "savedSeconds": round(max(0.0, full_est - (pm.get("estimateSeconds") or 0.0)), 1)}
    if play.results is None:
        return est, None
    full_measured = overhead + float(play.runner_duration or sum(float(r.get("Duration") or 0) for r in play.results))
    play_invocations = [i for i in plan["invocations"] if i["mode"] == "PlayMode" and i["kind"] == "tier"]
    if any(i["filter"] == "" for i in play_invocations):
        planned = full_measured
    else:
        ran = sum(float(r.get("Duration") or 0) for r in play.results if would_have_run(plan, "PlayMode", r["FullName"]))
        planned = overhead * len(play_invocations) + ran
    measured = {"plannedSeconds": round(planned, 1), "fullSeconds": round(full_measured, 1),
                "savedSeconds": round(full_measured - planned, 1)}
    return est, measured


def sample_markdown(s):
    L = ["# Shadow sample %s" % s["id"], "",
         "- **valid**: %s%s · counts toward Q5: **%s**" % (s["valid"], "" if s["valid"] else " — " + s["invalidReason"], s["countsTowardQ5"]),
         "- snapshot at plan time: %s %s · new snapshot written: %s %s" % (
             s["snapshot"]["atPlan"].get("status"), s["snapshot"]["atPlan"].get("reason") or "",
             s["snapshot"]["written"], s["snapshot"]["notWrittenReason"] or ""),
         "- planned scope: **%s** (PlayMode %s)" % (s["plannedScope"]["tier"], s["selectedTests"]["selection"]),
         "- classification of the full run: **%s**" % s["fullResults"]["verdict"],
         "- savings — estimated: %s s · measured: %s s" % (s["savings"]["estimated"]["savedSeconds"],
                                                            (s["savings"]["measured"] or {}).get("savedSeconds", "n/a")),
         "", "## Escapes"]
    L += ["- %s %s `%s` — %s (investigation: %s)" % (e["kind"], e["mode"], e["fullName"], e["message"], e["investigation"])
          for e in s["escapes"]] or ["- none"]
    L += ["", "## Planned scope — why"] + ["- %s %s %s %s" % (r["tier"], r["rule"], r["path"], r["detail"])
                                          for r in s["plannedScope"]["reasons"][:20]]
    L += ["", "## Selected tests"] + ["- filter `%s`" % f for f in s["selectedTests"]["filters"]]
    L += ["- fixture %s" % f for f in s["selectedTests"]["selectedFixtures"]]
    L += ["", "## Not run by the plan"] + ["- %s — %s" % (n["fixture"], n["reason"]) for n in s["selectedTests"]["notRun"]]
    L += ["", "## Full results"]
    for r in s["fullResults"]["records"]:
        L.append("- %s %s exit %s %s — %s" % (r["mode"], r["dir"], r["exit"], r["outcome"],
                                               ", ".join("%s %d" % kv for kv in sorted(r["counts"].items()))))
        L += ["  - %s `%s`" % (f["kind"], f["fullName"]) for f in r["failures"]]
        L += ["  - NOT EXECUTED (%s) `%s`" % (n["status"], n["fullName"]) for n in r["not_executed"]]
    L += ["", "## Change set (%d paths: %s)" % (len(s["changeSet"]["changes"]),
                                                ", ".join("%s %d" % kv for kv in sorted(s["changeSet"]["byTier"].items())))]
    L += ["- %s %s → %s %s" % (c["status"], c["path"], c["tier"], c["rule"])
          for c in s["changeSet"]["changes"] if c["rule"] != "UNITY_INVISIBLE"][:200]
    return "\n".join(L) + "\n"


def main(argv):
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--plan", required=True)
    ap.add_argument("--edit-record", required=True)
    ap.add_argument("--play-record", required=True)
    ap.add_argument("--known", default=os.path.join(here, "known-failures.json"))
    ap.add_argument("--samples", default=os.path.join(repo, "Logs", "test-orchestrator", "shadow"))
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
        overhead = float(lib.load_json(a.map)["costModel"]["invocationOverheadSeconds"])
    except (OSError, ValueError, KeyError, lib.GitError, classify.KnownListError) as e:
        print("usage: %s: %s" % (e.__class__.__name__, e), file=sys.stderr)
        return 2

    invalid = validate_sample(plan, edit, play, current)
    if plan.get("mapSha256") != map_hash and not invalid:
        invalid = "test-map.json changed after planning"
    escapes, failing_now = ([], []) if invalid else compare(plan, report)

    written = False
    not_written = snapshot_blocker(invalid, report, a.no_snapshot)
    if not_written is None:
        state = snap_mod.worktree_state(a.repo, write_text_blobs=True)
        if snap_mod.state_equal(state, plan["preState"]):
            snap_mod.write(a.repo, a.snapshot, state, map_hash, failing_now,
                           [lib.norm_path(os.path.relpath(d, a.repo)) for d in (a.edit_record, a.play_record)])
            written = True
        else:
            not_written = "work tree changed while writing"

    now = datetime.datetime.now(datetime.timezone.utc)
    snap_at_plan = plan.get("snapshot") or {}
    counts = not invalid and snap_at_plan.get("status") == "valid"
    est, measured = savings(plan, play, overhead)
    by_tier = {}
    for c in plan.get("changes", []):
        by_tier[c["tier"]] = by_tier.get(c["tier"], 0) + 1
    sample_id = now.strftime("%Y%m%dT%H%M%SZ") + "-" + os.path.basename(os.path.dirname(os.path.abspath(a.plan)))[:60]
    sample = {
        "schema": 1, "id": sample_id, "utc": now.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "head": (plan.get("preState") or {}).get("head"), "plan": lib.norm_path(os.path.relpath(os.path.abspath(a.plan), a.repo)),
        "valid": not invalid, "invalidReason": invalid, "countsTowardQ5": bool(counts),
        "snapshot": {"atPlan": snap_at_plan, "written": written, "notWrittenReason": not_written},
        "changeSet": {"byTier": by_tier, "changes": plan.get("changes", [])},
        "plannedScope": {"tier": plan["tier"], "context": plan.get("context"), "reasons": plan.get("reasons", []),
                         "invocations": plan.get("invocations", [])},
        "selectedTests": {"selection": plan["playMode"]["selection"], "filters": plan["playMode"].get("filters", []),
                          "selectedFixtures": plan["playMode"].get("selectedFixtures", []),
                          "matchedFixtures": plan["playMode"].get("matchedFixtures", []),
                          "notRun": plan["playMode"].get("notRun", [])},
        "fullResults": {"verdict": report["verdict"], "knownListSha256": report["knownListSha256"],
                        "records": report["records"],
                        "runnerSeconds": {"EditMode": edit.runner_duration, "PlayMode": play.runner_duration}},
        "escapes": escapes,
        "savings": {"estimated": est, "measured": measured},
    }
    sdir = os.path.join(a.samples, sample_id)
    os.makedirs(sdir, exist_ok=True)
    with open(os.path.join(sdir, "sample.json"), "w", encoding="utf-8") as f:
        f.write(lib.dump_json(sample))
    with open(os.path.join(sdir, "sample.md"), "w", encoding="utf-8") as f:
        f.write(sample_markdown(sample))

    row = [sample["utc"], (sample["head"] or "")[:12], "yes" if counts else "no", snap_at_plan.get("status", "?"),
           plan["tier"], plan["playMode"]["selection"], str(len(plan["playMode"]["selectedFixtures"])),
           str(est["plannedSeconds"]), str(est["fullSeconds"]), str(est["savedSeconds"]),
           str((measured or {}).get("savedSeconds", "")), str(edit.runner_duration), str(play.runner_duration),
           "no" if invalid else "yes", (invalid or "").replace("\t", " "), str(len(escapes)),
           ";".join(e["fullName"] for e in escapes), report["verdict"], "yes" if written else "no",
           lib.norm_path(os.path.relpath(sdir, a.repo))]
    os.makedirs(os.path.dirname(os.path.abspath(a.log)), exist_ok=True)
    new_log = not os.path.isfile(a.log)
    with open(a.log, "a", encoding="utf-8") as f:
        if new_log:
            f.write(TSV_HEADER + "\n")
        f.write("\t".join(row) + "\n")

    print("shadow: %s · plan %s/%s · classification %s · escapes %d · snapshot %s · sample %s"
          % ("INVALID (" + invalid + ")" if invalid else "valid", plan["tier"], plan["playMode"]["selection"],
             report["verdict"], len(escapes), "written" if written else "not written", sdir))
    for e in escapes:
        print("  %s %s %s — %s" % (e["kind"], e["mode"], e["fullName"], e["message"]))
    if invalid:
        return 3
    return 1 if escapes else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
