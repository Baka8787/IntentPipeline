"""Phase 1 (docs/57 §6, §12): classify safe_unity_test.sh records against the APPROVED known-failure list.

    python -I docs/artifacts/test-orchestrator/classify_results.py RECORD_DIR [RECORD_DIR ...]
           [--known docs/artifacts/test-orchestrator/known-failures.json] [--json OUT.json] [--markdown OUT.md]

Read-only: never edits the known-failure list (Q6: adding, changing or removing entries is the user's decision).

    python -I .../classify_results.py --verify-entries RECORD_DIR [...]   (D-1: check the list against baseline records)

Overall verdict / exit code:
    VERIFIED 0 · VERIFIED_WITH_KNOWN_FAILURES 10 · FAILED 1 · BLOCKED 20 · INCOMPLETE 30 · usage / invalid list 2
Precedence: any new failure -> FAILED (even when another record was blocked: a regression is never hidden);
otherwise any environment block -> BLOCKED; otherwise anything not executed -> INCOMPLETE.
"""
import argparse
import os
import sys

sys.dont_write_bytecode = True   # never leave __pycache__ in docs/ (it would itself be a change)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import orchestrator_lib as lib  # noqa: E402

EXIT = {"VERIFIED": 0, "VERIFIED_WITH_KNOWN_FAILURES": 10, "FAILED": 1, "BLOCKED": 20, "INCOMPLETE": 30}
BLOCKED_CODES = set(range(10, 33)) | {99, 130}
INCOMPLETE_CODES = {2, 3, 4, 5}
REQUIRED = ("id", "mode", "fullName", "symptoms", "cause", "approvedBy", "approvedOn", "approvalRef", "source",
            "reinvestigateWhen")


class KnownListError(Exception):
    pass


def load_known(path):
    """Fail closed: a malformed list must stop classification, not silently match nothing."""
    try:
        data = lib.load_json(path)
    except (OSError, ValueError) as e:
        raise KnownListError("cannot read %s: %s" % (path, e))
    if not isinstance(data, dict) or data.get("schema") != 1 or not isinstance(data.get("entries"), list):
        raise KnownListError("expected {\"schema\": 1, \"entries\": [...]}")
    seen_ids, seen_names = set(), set()
    for i, e in enumerate(data["entries"]):
        where = "entry %d" % i
        if not isinstance(e, dict):
            raise KnownListError(where + " is not an object")
        for k in REQUIRED:
            if k not in e or e[k] in (None, "", []):
                raise KnownListError("%s: '%s' is required (Q6: every entry records who approved it)" % (where, k))
        if e["mode"] not in lib.MODES:
            raise KnownListError("%s: mode must be EditMode or PlayMode" % where)
        if "." not in e["fullName"] or e["fullName"] != e["fullName"].strip():
            raise KnownListError("%s: fullName must be the complete test name" % where)
        for k in ("symptoms", "source", "reinvestigateWhen"):
            if not isinstance(e[k], list) or not all(isinstance(x, str) and lib.normalize_message(x) for x in e[k]):
                raise KnownListError("%s: %s must be a non-empty list of non-empty strings" % (where, k))
        if e["id"] in seen_ids:
            raise KnownListError("%s: duplicate id %s" % (where, e["id"]))
        key = (e["mode"], e["fullName"])
        if key in seen_names:
            raise KnownListError("%s: duplicate test %s" % (where, e["fullName"]))
        seen_ids.add(e["id"])
        seen_names.add(key)
    return data["entries"]


def symptom_matches(entry, message):
    msg = lib.normalize_message(message)
    return all(lib.normalize_message(s) in msg for s in entry["symptoms"])


def classify_record(record, known):
    """Returns a dict: outcome, reason, failures[], known_items[], not_executed[]."""
    out = {"dir": record.dir, "mode": record.mode, "filter": record.filter, "exit": record.exit_code,
           "verdict": record.verdict, "reason": record.reason, "failures": [], "known_items": [], "not_executed": [],
           "counts": {}}
    if record.mode not in lib.MODES:
        out.update(outcome="INCOMPLETE", reason="record has no valid mode (meta.txt)")
        return out
    if record.summary is None or record.exit_code is None:
        out.update(outcome="INCOMPLETE", reason="record has no summary / exit code (entry still running or killed)")
        return out
    if record.check_only:
        out.update(outcome="INCOMPLETE", reason="--check-only: nothing was submitted")
        return out

    code = record.exit_code
    if code in BLOCKED_CODES:
        outcome = "BLOCKED"
    elif code in INCOMPLETE_CODES:
        outcome = "INCOMPLETE"
    elif code in (0, 1):
        outcome = None
    else:
        out.update(outcome="INCOMPLETE", reason="unknown entry exit code %d" % code)
        return out

    if record.results is None:
        # 0/1 without results contradicts the entry's own contract: fail closed.
        if outcome is None:
            out.update(outcome="INCOMPLETE", reason="exit %d but %s" % (code, record.status_error))
        else:
            out["outcome"] = outcome
        return out

    by_name = {}
    statuses = {}
    for r in record.results:
        by_name[r["FullName"]] = r
        statuses[r["Status"]] = statuses.get(r["Status"], 0) + 1
    out["counts"] = statuses
    known_here = [e for e in known if e["mode"] == record.mode]
    known_by_name = {e["fullName"]: e for e in known_here}

    for r in record.results:
        st = r["Status"]
        if st == "Passed":
            continue
        if st == "Failed":
            e = known_by_name.get(r["FullName"])
            # A known test failing with a DIFFERENT symptom is a new failure (D-1), not a variant of the known one.
            matched = e is not None and symptom_matches(e, r.get("Message"))
            out["failures"].append({"fullName": r["FullName"], "kind": "KNOWN_MATCH" if matched else "NEW_FAILURE",
                                    "knownId": e["id"] if matched else None,
                                    "symptomChangedFrom": e["id"] if (e is not None and not matched) else None,
                                    "message": lib.normalize_message(r.get("Message"))[:400]})
        else:  # Skipped / Ignored / Inconclusive: selected but not verified
            out["not_executed"].append({"fullName": r["FullName"], "status": st})

    for e in known_here:
        r = by_name.get(e["fullName"])
        if r is None:
            kind = "KNOWN_MISSING_FROM_FULL_RUN" if record.is_full_run else "KNOWN_NOT_RUN"
        elif r["Status"] == "Passed":
            kind = "KNOWN_NOT_OBSERVED"
        else:
            continue  # reported in failures
        out["known_items"].append({"id": e["id"], "fullName": e["fullName"], "kind": kind})

    kinds = {f["kind"] for f in out["failures"]}
    if outcome is None:
        if code == 1 and not out["failures"]:
            outcome, out["reason"] = "INCOMPLETE", "entry reported failures but the result list has none"
        elif "NEW_FAILURE" in kinds:
            outcome = "FAILED"
        elif any(r["Status"] == "Inconclusive" for r in record.results):
            outcome = "INCOMPLETE"
        elif kinds == {"KNOWN_MATCH"}:
            outcome = "VERIFIED_WITH_KNOWN_FAILURES"
        else:
            outcome = "VERIFIED"
    elif "NEW_FAILURE" in kinds:
        out["blocked_but_failed"] = True
    out["outcome"] = outcome
    return out


def overall(items, known):
    modes_run = {i["mode"] for i in items}
    not_run_modes = [e for e in known if e["mode"] not in modes_run]
    if any(f["kind"] == "NEW_FAILURE" for i in items for f in i["failures"]):
        verdict = "FAILED"
    elif any(i["outcome"] == "BLOCKED" for i in items):
        verdict = "BLOCKED"
    elif any(i["outcome"] == "INCOMPLETE" for i in items):
        verdict = "INCOMPLETE"
    elif any(i["outcome"] == "VERIFIED_WITH_KNOWN_FAILURES" for i in items):
        verdict = "VERIFIED_WITH_KNOWN_FAILURES"
    else:
        verdict = "VERIFIED"
    return verdict, [{"id": e["id"], "fullName": e["fullName"], "kind": "KNOWN_NOT_RUN",
                      "detail": "%s was not run" % e["mode"]} for e in not_run_modes]


def classify(record_dirs, known_path):
    known = load_known(known_path)
    items = [classify_record(lib.Record(d), known) for d in record_dirs]
    verdict, extra = overall(items, known)
    return {"verdict": verdict, "knownList": known_path, "knownListSha256": lib.sha256_file(known_path),
            "records": items, "knownNotRunModes": extra}


def verify_entries(record_dirs, known_path):
    """D-1: before an entry is trusted, show it against real baseline records. An entry is CONFIRMED only when the
    test failed in at least one record with every symptom present, and in no record failed with another symptom."""
    known = load_known(known_path)
    records = [lib.Record(d) for d in record_dirs]
    out = []
    for e in known:
        rows = []
        for rec in records:
            if rec.mode != e["mode"] or rec.results is None:
                continue
            r = next((x for x in rec.results if x["FullName"] == e["fullName"]), None)
            if r is not None:
                rows.append({"record": rec.dir, "gitHead": rec.git_head, "status": r["Status"],
                             "symptomsMatch": r["Status"] == "Failed" and symptom_matches(e, r.get("Message"))})
        matched = sum(1 for x in rows if x["symptomsMatch"])
        mismatched = sum(1 for x in rows if x["status"] == "Failed" and not x["symptomsMatch"])
        passed = sum(1 for x in rows if x["status"] == "Passed")
        state = "CONFIRMED" if matched and not mismatched else ("SYMPTOM_MISMATCH" if mismatched else "NOT_OBSERVED")
        out.append({"id": e["id"], "fullName": e["fullName"], "state": state, "records": len(rows),
                    "failedMatching": matched, "failedOtherSymptom": mismatched, "passed": passed, "rows": rows})
    return out


def to_markdown(report):
    not_exec = sum(len(i["not_executed"]) for i in report["records"])
    L = ["# Test result classification", "", "**Verdict: %s**" % report["verdict"]
         + ("  ·  ⚠ %d selected test(s) NOT executed (skipped / ignored / inconclusive) — not verified, listed below" % not_exec
            if not_exec else ""), "",
         "Known-failure list: `%s` (sha256 %s…)" % (report["knownList"], report["knownListSha256"][:12]), ""]
    for i in report["records"]:
        L.append("## %s — %s" % (i["mode"], i["outcome"]))
        L.append("- record: `%s`" % i["dir"])
        L.append("- entry: exit %s %s — %s" % (i["exit"], i["verdict"] or "", i["reason"] or ""))
        L.append("- filter: `%s`" % (i["filter"] if i["filter"] else "(full run)"))
        if i["counts"]:
            L.append("- results: " + ", ".join("%s %d" % kv for kv in sorted(i["counts"].items())))
        for f in i["failures"]:
            tag = " (%s)" % f["knownId"] if f["knownId"] else (" (symptom changed from %s)" % f["symptomChangedFrom"] if f["symptomChangedFrom"] else "")
            L.append("- **%s** `%s`%s — %s" % (f["kind"], f["fullName"], tag, f["message"][:200]))
        for k in i["known_items"]:
            L.append("- %s `%s` (%s)" % (k["kind"], k["fullName"], k["id"]))
        for n in i["not_executed"]:
            L.append("- NOT EXECUTED (%s) `%s`" % (n["status"], n["fullName"]))
        L.append("")
    for k in report["knownNotRunModes"]:
        L.append("- %s `%s` (%s): %s" % (k["kind"], k["fullName"], k["id"], k["detail"]))
    return "\n".join(L) + "\n"


def main(argv):
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("records", nargs="+")
    ap.add_argument("--known", default=os.path.join(here, "known-failures.json"))
    ap.add_argument("--json")
    ap.add_argument("--markdown")
    ap.add_argument("--verify-entries", action="store_true",
                    help="check every known entry against the given baseline records instead of classifying")
    a = ap.parse_args(argv)
    for d in a.records:
        if not os.path.isdir(d):
            print("usage: record dir not found: %s" % d, file=sys.stderr)
            return 2
    if a.verify_entries:
        try:
            res = verify_entries(a.records, a.known)
        except KnownListError as e:
            print("KNOWN_LIST_INVALID: %s" % e, file=sys.stderr)
            return 2
        lib.utf8_stdout()
        for v in res:
            print("%s %s: %s — %d record(s) contain it, %d failed with the approved symptoms, %d failed otherwise, %d passed"
                  % (v["id"], v["fullName"], v["state"], v["records"], v["failedMatching"], v["failedOtherSymptom"], v["passed"]))
        return 0 if res and all(v["state"] == "CONFIRMED" for v in res) else 1
    try:
        report = classify(a.records, a.known)
    except KnownListError as e:
        print("KNOWN_LIST_INVALID: %s" % e, file=sys.stderr)
        return 2
    md = to_markdown(report)
    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            f.write(lib.dump_json(report))
    if a.markdown:
        with open(a.markdown, "w", encoding="utf-8") as f:
            f.write(md)
    lib.utf8_stdout()
    print(md, end="")
    return EXIT[report["verdict"]]


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
