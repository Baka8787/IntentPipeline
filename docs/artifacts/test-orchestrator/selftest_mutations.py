"""Mutation check for the orchestrator self-tests (docs/57 §9): each mutation breaks one rule in a COPY of this folder;
the self-test suite must go red for every one of them. Proves the tests can fail, not only that they pass.

    python -I docs/artifacts/test-orchestrator/selftest_mutations.py
Exit 0 = every mutation was caught.
"""
import os
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))

MUTATIONS = [
    ("high-risk table ignored", "plan_tests.py",
     'hr = next((h for h in tmap["highRisk"] if lib.matches(base, h["pattern"])), None)', "hr = None"),
    ("WriterRules authority ignored", "plan_tests.py",
     'if os.path.basename(base) in writers:', "if False:"),
    ("cross-layer / interface content rules off", "plan_tests.py",
     "            hit = content_rules(change, facts, runtime=True)", "            hit = None"),
    ("unknown path downgraded to R1", "plan_tests.py",
     'return done("R3", "UNMAPPED", "runtime script outside every module in test-map.json")',
     'return done("R1", "UNMAPPED", "runtime script outside every module in test-map.json")'),
    ("invalid snapshot no longer forces R3", "plan_tests.py",
     '    if snapshot_reason:\n        tier = "R3"', '    if snapshot_reason:\n        pass'),
    ("gate context no longer forces R3", "plan_tests.py",
     '    if context != "dev":\n        tier = "R3"', '    if context != "dev":\n        pass'),
    ("filters chosen even when dearer than full", "plan_tests.py", "if est >= full:", "if False:"),
    ("probes auto-selected", "plan_tests.py",
     '    if tier == "R2":\n        selected = {', '    if tier == "R2":\n        selected = set(tmap["probeFixtures"]) | {'),
    ("editor auto-run marker ignored", "plan_tests.py",
     'auto = [m for m in tmap["editorAutoRunMarkers"] if m in text]', "auto = []"),
    ("snapshot WIP ignored (diff vs base commit only)", "snapshot.py",
     'then = snap["worktree"].get(p) or _blob_at(repo, base, p)', "then = _blob_at(repo, base, p)"),
    ("not-ancestor base accepted", "snapshot.py",
     'if lib.git(repo, "merge-base", "--is-ancestor", base, "HEAD", check=False).returncode != 0:', "if False:"),
    ("staleness ignored", "snapshot.py", "if age.days >= max_age_days:", "if False:"),
    ("symptoms ignored (name-only match)", "classify_results.py",
     "    return all(lib.normalize_message(s) in msg for s in entry[\"symptoms\"])", "    return True"),
    ("approval not required", "classify_results.py",
     'REQUIRED = ("id", "mode", "fullName", "symptoms", "cause", "approvedBy", "approvedOn", "approvalRef", "source",\n            "reinvestigateWhen")',
     'REQUIRED = ("id", "mode", "fullName", "symptoms")'),
    ("BLOCKED hides a new failure", "classify_results.py",
     '    if any(f["kind"] == "NEW_FAILURE" for i in items for f in i["failures"]):\n        verdict = "FAILED"\n    elif any(i["outcome"] == "BLOCKED" for i in items):',
     '    if any(i["outcome"] == "BLOCKED" for i in items):\n        verdict = "BLOCKED"\n    elif any(f["kind"] == "NEW_FAILURE" for i in items for f in i["failures"]):'),
    ("symptom change accepted as the known failure", "classify_results.py",
     'matched = e is not None and symptom_matches(e, r.get("Message"))', "matched = e is not None"),
    ("shadow: measured savings ignore what was skipped", "shadow_compare.py",
     'ran = sum(float(r.get("Duration") or 0) for r in play.results if would_have_run(plan, "PlayMode", r["FullName"]))',
     'ran = sum(float(r.get("Duration") or 0) for r in play.results)'),
    ("report: counts samples without a trustworthy snapshot", "shadow_report.py",
     'counting = [s for s in valid if snapshot_validity(s)[0] == "valid"]', "counting = valid"),
    ("report: Q5 follows snapshot advancement instead of sample validity", "shadow_report.py",
     "valid = [s for s in readable if sample_validity(s)[0]]", "valid = [s for s in readable if snapshot_validity(s)[1]]"),
    ("report: escape investigations never required", "shadow_report.py",
     '            if not inv:\n                pending.append(item)', '            if False:\n                pending.append(item)'),
    ("snapshot advanced past a new failure", "shadow_compare.py",
     '    if verdict not in ("VERIFIED", "VERIFIED_WITH_KNOWN_FAILURES"):', "    if False:"),
    ("snapshot advanced despite skipped tests", "shadow_compare.py",
     "    if not_executed:\n        return", "    if False:\n        return"),
    ("snapshot advanced from an invalid sample", "shadow_compare.py",
     '    if invalid:\n        return "invalid sample: " + invalid', '    if False:\n        return "invalid sample: " + invalid'),
    ("known failures counted as escapes", "shadow_compare.py",
     'if f["kind"] != "NEW_FAILURE" or would_have_run', 'if would_have_run'),
    ("shadow: every test counted as selected", "shadow_compare.py",
     '        if inv["filter"] == "" or inv["filter"].lower() in full_name.lower():', "        if True:"),
    ("validity: uncommitted work-tree drift not checked", "sample_validity.py",
     '    add("worktree", not drift,', '    add("worktree", True,'),
    ("validity: path outside the doc-only allowlist accepted", "sample_validity.py",
     '    if not a:\n        return False, "not in the doc-only allowlist"', '    if False:\n        return False, "not in the doc-only allowlist"'),
    ("validity: allowlist consulted before the deny list", "sample_validity.py",
     '    d = lib.first_match(path, pol["deny"])',
     '    d = None if lib.first_match(path, pol["allow"]) else lib.first_match(path, pol["deny"])'),
    ("validity: fast-forward not checked", "sample_validity.py",
     'if lib.git(repo, "merge-base", "--is-ancestor", plan_head, current_head, check=False).returncode != 0:', "if False:"),
    ("validity: merge commits accepted", "sample_validity.py", "    if merges:", "    if False:"),
    ("validity: plan input hashes not compared", "sample_validity.py",
     '        add("inputs", not changed,', '        add("inputs", True,'),
    ("validity: plan without input hashes accepted after HEAD moved", "sample_validity.py",
     '        add("inputs", head_policy == HEAD_UNCHANGED,', '        add("inputs", True,'),
    ("validity: records may come from any commit", "sample_validity.py",
     "if not r.git_head or not any(h.startswith(r.git_head) for h in tested_heads)]", "if not r.git_head]"),
    ("validity: test-map hash not compared", "sample_validity.py",
     '    add("map", plan.get("mapSha256") == map_hash,', '    add("map", True,'),
    ("shadow: snapshot records the current HEAD instead of the tested one", "shadow_compare.py",
     'tested = {"head": plan["preState"]["head"],', 'tested = {"head": current["head"],'),
    ("mirror: any difference allowed", "check_skill_mirror.py", "        if x == y:\n            continue", "        if True:\n            continue"),
]


def main():
    getattr(sys.stdout, "reconfigure", lambda **_: None)(encoding="utf-8")
    caught = 0
    for name, file, old, new in MUTATIONS:
        try:
            # Same depth as the real folder so the copy still resolves the real repository root.
            dst = os.path.join(REPO, "Logs", "test-orchestrator", "_mutant")
            shutil.rmtree(dst, ignore_errors=True)
            shutil.copytree(HERE, dst)
            path = os.path.join(dst, file)
            with open(path, encoding="utf-8") as f:
                src = f.read()
            if src.count(old) != 1:
                print("  BROKEN mutation (anchor not found exactly once): %s" % name)
                continue
            with open(path, "w", encoding="utf-8") as f:
                f.write(src.replace(old, new))
            p = subprocess.run([sys.executable, "-I", os.path.join(dst, "selftest.py")],
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            ok = p.returncode != 0
            caught += ok
            print("  %s %s" % ("caught " if ok else "MISSED ", name))
        finally:
            shutil.rmtree(os.path.join(REPO, "Logs", "test-orchestrator", "_mutant"), ignore_errors=True)
    print("mutations: %d/%d caught" % (caught, len(MUTATIONS)))
    return 0 if caught == len(MUTATIONS) else 1


if __name__ == "__main__":
    sys.exit(main())
